using System.Globalization;
using System.Text;

namespace Simulador.App;

/// <summary>
/// O que o analisador lógico sabe fazer, dividido entre o desenho em cima
/// da peça e a janela do analisador: de onde vem cada canal, que pedaço de
/// tempo mostrar, como escrever o tempo e como salvar para o PulseView.
///
/// O sinal em si mora na <see cref="LinhaDoTempo"/> de cada placa. Aqui
/// só se decide O QUE mostrar dele.
/// </summary>
public static class Analisador
{
    public const int Canais = 8;

    /// <summary>A escala de fábrica: 10 ms na tela inteira.</summary>
    public const double JanelaPadraoUs = 10_000;

    /// <summary>As escalas que a barra da peça oferece.</summary>
    public static readonly double[] Escalas = [100, 1_000, 10_000, 100_000, 1_000_000];

    /// <summary>Um canal e o pino de placa que ele enxerga.</summary>
    public readonly record struct Fonte(int Canal, string Placa, Maquina Maquina, int Gpio)
    {
        public bool Ligada => Maquina is not null && Gpio >= 0;
    }

    /// <summary>De onde vem cada um dos oito canais: a placa e o GPIO na mesma rede do D0..D7.</summary>
    public static List<Fonte> Fontes(Circuito circuito, Netlist rede, Bancada bancada,
                                     PecaNoCircuito analisador)
    {
        var fontes = new List<Fonte>(Canais);
        var maquinas = bancada.Todas.ToDictionary(x => x.Key, x => x.Value);

        for (int k = 0; k < Canais; k++)
        {
            var (placa, gpio) = rede.PinoDe(circuito, analisador.Id, $"D{k}");
            Maquina maq = null;
            if (placa is not null && !maquinas.TryGetValue(placa, out maq))
                maq = bancada.Quantas == 0 ? bancada.Solta : null;
            fontes.Add(new Fonte(k, placa, maq, gpio));
        }

        return fontes;
    }

    public static double JanelaDe(PecaNoCircuito analisador)
        => analisador.JanelaUs > 0 ? analisador.JanelaUs : JanelaPadraoUs;

    /// <summary>
    /// O pedaço de tempo a mostrar.
    ///
    /// Com disparo, ele começa um pouco antes da última subida do primeiro
    /// canal que tem sinal -- é o que o osciloscópio faz, e é o que deixa
    /// um PWM parado na tela em vez de correndo como um borrão. Sem subida
    /// nenhuma, a janela termina agora.
    /// </summary>
    public static (double De, double Ate) Enquadrar(IReadOnlyList<Fonte> fontes, double janela,
                                                    bool disparo = true, double? ate = null)
    {
        var ligadas = fontes.Where(f => f.Ligada).ToList();
        double agora = ate ?? (ligadas.Count > 0 ? ligadas.Max(f => f.Maquina.AgoraUsLido) : 0);

        if (disparo)
            foreach (var f in ligadas)
            {
                // Ate dois segundos para tras: um pisca lento numa tela de
                // 10 ms fica mostrando a ultima subida ate vir a proxima --
                // e o "normal" do osciloscopio, e nao uma reta.
                double limite = agora - janela * 0.9;
                double t = f.Maquina.Linha.UltimaSubida(f.Gpio, limite, Math.Max(4 * janela, 2e6));
                if (!double.IsNaN(t)) return (t - janela * 0.1, t + janela * 0.9);
            }

        return (agora - janela, agora);
    }

    /// <summary>
    /// A onda de um canal pronta para desenhar em <paramref name="pixels"/> de
    /// largura: sinal periodico rapido vira faixa, e bordas que caem no
    /// mesmo pixel viram uma barra cheia, com o nivel certo depois dela. O
    /// desenho fica igual ao de todas as bordas, com o tamanho da tela.
    /// </summary>
    public static List<(double Us, int Nivel)> OndaParaDesenhar(Fonte fonte, double de, double ate, double pixels)
    {
        double porPixel = (ate - de) / Math.Max(1, pixels);
        var onda = fonte.Maquina.Linha.Onda(fonte.Gpio, de, ate, 400_000, porPixel);
        if (onda.Count <= pixels * 3) return onda;

        var saida = new List<(double Us, int Nivel)> { onda[0] };
        int i = 1;
        while (i < onda.Count)
        {
            long coluna = (long)Math.Floor((onda[i].Us - de) / porPixel);
            int j = i;
            while (j + 1 < onda.Count && (long)Math.Floor((onda[j + 1].Us - de) / porPixel) == coluna) j++;

            if (j == i) saida.Add(onda[i]);
            else
            {
                saida.Add(onda[i]);
                saida.Add((onda[i].Us, 1 - onda[i].Nivel));
                saida.Add(onda[j]);
            }
            i = j + 1;
        }

        return saida;
    }

    /// <summary>Tempo do jeito que se lê: "250 µs", "1,5 ms", "2 s".</summary>
    public static string Tempo(double us)
    {
        double a = Math.Abs(us);
        var br = Idioma.Cultura;
        if (a < 1) return (us * 1000).ToString("0.#", br) + " ns";
        if (a < 1000) return us.ToString(a < 10 ? "0.##" : "0.#", br) + " µs";
        if (a < 1_000_000) return (us / 1000).ToString(a < 10_000 ? "0.###" : "0.#", br) + " ms";
        return (us / 1_000_000).ToString("0.###", br) + " s";
    }

    /// <summary>Frequência do jeito que se lê: "490,2 Hz", "5 kHz".</summary>
    public static string Frequencia(double hz)
    {
        var br = Idioma.Cultura;
        return hz >= 1000 ? (hz / 1000).ToString("0.###", br) + " kHz" : hz.ToString("0.#", br) + " Hz";
    }

    /// <summary>
    /// A captura em VCD, o formato que o PulseView e o GTKWave abrem -- e
    /// com eles os decodificadores de serial, I2C e SPI que a turma usa.
    /// Tempo em microssegundos, a partir do começo da janela.
    /// </summary>
    public static string Vcd(IReadOnlyList<Fonte> fontes, double de, double ate, string origem)
    {
        var t = new StringBuilder();
        t.Append("$date ").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")).Append(" $end\n");
        t.Append("$version CircuitSketch - ").Append(origem).Append(" $end\n");
        t.Append("$timescale 1 ns $end\n");
        t.Append("$scope module analisador $end\n");

        string Id(int k) => ((char)('!' + k)).ToString();

        foreach (var f in fontes)
        {
            string nome = f.Ligada ? $"D{f.Canal}_GPIO{f.Gpio}" : $"D{f.Canal}";
            t.Append($"$var wire 1 {Id(f.Canal)} {nome} $end\n");
        }

        t.Append("$upscope $end\n$enddefinitions $end\n");

        // Todas as mudanças de todos os canais, em ordem de tempo.
        var mudancas = new List<(long Ns, int Canal, int Nivel)>();
        foreach (var f in fontes)
        {
            if (!f.Ligada) { mudancas.Add((0, f.Canal, 0)); continue; }
            foreach (var (us, nivel) in f.Maquina.Linha.Onda(f.Gpio, de, ate, 1_000_000))
                mudancas.Add(((long)Math.Round((Math.Max(us, de) - de) * 1000), f.Canal, nivel));
        }

        long ultimo = -1;
        foreach (var (ns, canal, nivel) in mudancas.OrderBy(m => m.Ns).ThenBy(m => m.Canal))
        {
            if (ns != ultimo) { t.Append('#').Append(ns).Append('\n'); ultimo = ns; }
            t.Append(nivel).Append(Id(canal)).Append('\n');
        }

        t.Append('#').Append((long)Math.Round((ate - de) * 1000)).Append('\n');
        return t.ToString();
    }

    /// <summary>
    /// Medida do trecho em que o tempo <paramref name="us"/> cai: quanto
    /// dura o nível atual e, se o sinal repete, o período, a frequência e
    /// quanto dele fica alto.
    /// </summary>
    public static string Medir(List<(double Us, int Nivel)> onda, double us, double ate)
    {
        if (onda.Count == 0) return "";

        int i = 0;
        while (i + 1 < onda.Count && onda[i + 1].Us <= us) i++;

        double inicio = onda[i].Us;
        double fim = i + 1 < onda.Count ? onda[i + 1].Us : ate;
        var texto = new StringBuilder(onda[i].Nivel == 1 ? Idioma.T("ALTO") : Idioma.T("BAIXO")).Append(' ');
        texto.Append(Tempo(fim - inicio));

        // Periodo: da subida anterior a seguinte, quando as duas estao na janela.
        var subidas = new List<double>();
        for (int k = 1; k < onda.Count; k++)
            if (onda[k].Nivel == 1 && onda[k - 1].Nivel == 0) subidas.Add(onda[k].Us);

        int antes = subidas.FindLastIndex(s => s <= us);
        if (antes >= 0 && antes + 1 < subidas.Count)
        {
            double periodo = subidas[antes + 1] - subidas[antes];
            double alto = 0;
            for (int k = 1; k < onda.Count; k++)
                if (onda[k].Us > subidas[antes] && onda[k].Nivel == 0)
                {
                    alto = onda[k].Us - subidas[antes];
                    break;
                }

            texto.Append(" · ").Append(Idioma.T("período")).Append(' ').Append(Tempo(periodo))
                 .Append(" · ").Append(Frequencia(1e6 / periodo));
            if (alto > 0 && alto < periodo)
                texto.Append(" · ").Append((alto / periodo * 100).ToString("0.#", Idioma.Cultura))
                     .Append(Idioma.T("% alto"));
        }

        return texto.ToString();
    }
}
