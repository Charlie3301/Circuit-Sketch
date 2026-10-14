namespace Simulador;

/// <summary>
/// Tudo o que os pinos de uma placa fizeram, com o tempo em
/// microssegundos. É o que o analisador lógico lê.
///
/// Antes o analisador olhava o nível do pino uma vez por quadro da tela
/// -- vinte e cinco vezes por segundo --, e tudo o que acontecia entre
/// duas olhadas sumia: o PWM virava uma linha reta, o shiftOut inteiro
/// cabia num instante, e a serial nem aparecia. Aqui fica cada borda,
/// no tempo em que aconteceu.
///
/// Três tipos de registro, e só o primeiro guarda borda por borda:
///
/// - as BORDAS de digitalWrite, shiftOut e companhia;
/// - os sinais PERIÓDICOS -- PWM, tone(), servo --, que o hardware gera
///   sozinho: guardar cada borda de um PWM de 1 kHz encheria a memória em
///   segundos, então fica "a partir de t, f Hz com tanto de ciclo", e as
///   bordas saem na hora de desenhar;
/// - os quadros da SERIAL: cada byte que sai pelo TX, com o baud.
///
/// Quem pede o desenho de um pino recebe as três coisas juntas, em
/// ordem, como um analisador de verdade veria no fio.
/// </summary>
public sealed class LinhaDoTempo
{
    /// <summary>Quantas bordas cada pino guarda antes de esquecer as mais velhas.</summary>
    public const int BordasPorPino = 1 << 16;

    const int PeriodosPorPino = 4096;
    const int QuadrosPorPino = 1 << 15;

    readonly object trava = new();

    sealed class Bordas
    {
        public readonly double[] Tempos = new double[BordasPorPino];
        public readonly byte[] Niveis = new byte[BordasPorPino];
        public long Total;

        public int Quantas => (int)Math.Min(Total, BordasPorPino);
        public int Indice(int k) => (int)((Total - Quantas + k) % BordasPorPino);
    }

    /// <summary>Um sinal que o hardware repete sozinho, de Desde até Ate.</summary>
    public readonly record struct Periodo(double Desde, double Ate, double Hz, double Ciclo);

    /// <summary>Um byte saindo pela serial: quando começou, qual e em que baud.</summary>
    public readonly record struct Quadro(double Inicio, byte Valor, int Baud);

    readonly Dictionary<int, Bordas> bordas = [];
    readonly Dictionary<int, List<Periodo>> periodos = [];
    readonly Dictionary<int, List<Quadro>> quadros = [];

    /// <summary>O fim da última transmissão da serial, por pino: o próximo byte espera por ele.</summary>
    readonly Dictionary<int, double> fimDaSerial = [];

    public void Limpar()
    {
        lock (trava)
        {
            bordas.Clear();
            periodos.Clear();
            quadros.Clear();
            fimDaSerial.Clear();
        }
    }

    // =====================================================================
    //  escrita
    // =====================================================================

    /// <summary>O pino foi para um nível -- e o que ele gerava sozinho acabou.</summary>
    public void Borda(double us, int pino, int nivel)
    {
        lock (trava)
        {
            FecharPeriodo(us, pino);

            if (!bordas.TryGetValue(pino, out var b)) bordas[pino] = b = new Bordas();

            // Tempo que voltou para trás (a placa reiniciou sem limpar):
            // começa de novo, para a busca por tempo continuar valendo.
            if (b.Quantas > 0 && us < b.Tempos[b.Indice(b.Quantas - 1)]) b.Total = 0;

            int i = (int)(b.Total % BordasPorPino);
            b.Tempos[i] = us;
            b.Niveis[i] = (byte)(nivel != 0 ? 1 : 0);
            b.Total++;
        }
    }

    /// <summary>
    /// A partir de <paramref name="us"/> o pino repete sozinho: <paramref name="hz"/>
    /// vezes por segundo, alto durante <paramref name="ciclo"/> (0 a 1) de cada volta.
    /// Ciclo 0 ou 1 não é sinal periódico: é nível, e vira borda.
    /// </summary>
    /// <param name="continuarFase">
    /// O hardware que ja esta gerando o sinal continua no mesmo compasso:
    /// mudar o ciclo de um PWM ou a largura do pulso do servo vale a partir
    /// da PROXIMA volta, sem recomecar a contagem. Sem isto, um servo.write
    /// a cada volta do loop encurtava o periodo do servo para o do loop.
    /// </param>
    public void Periodico(double us, int pino, double hz, double ciclo,
                          double duracaoUs = double.PositiveInfinity, bool continuarFase = false)
    {
        if (hz <= 0 || ciclo <= 0 || ciclo >= 1)
        {
            Borda(us, pino, ciclo >= 1 ? 1 : 0);
            return;
        }

        lock (trava)
        {
            if (continuarFase && periodos.TryGetValue(pino, out var abertos) && abertos.Count > 0
                && abertos[^1].Ate > us && Math.Abs(abertos[^1].Hz - hz) < 1e-6)
            {
                var anterior = abertos[^1];
                double volta = 1e6 / hz;
                double inicio = anterior.Desde + Math.Ceiling((us - anterior.Desde) / volta) * volta;

                abertos[^1] = anterior with { Ate = inicio };
                abertos.Add(new Periodo(inicio, inicio + duracaoUs, hz, ciclo));
                if (abertos.Count > PeriodosPorPino) abertos.RemoveRange(0, abertos.Count - PeriodosPorPino);
                return;
            }

            FecharPeriodo(us, pino);

            if (!periodos.TryGetValue(pino, out var lista)) periodos[pino] = lista = [];
            lista.Add(new Periodo(us, us + duracaoUs, hz, ciclo));
            if (lista.Count > PeriodosPorPino) lista.RemoveRange(0, lista.Count - PeriodosPorPino);
        }
    }

    /// <summary>
    /// O sinal periodico que o pino esta gerando sozinho neste instante (PWM,
    /// tone, servo), ou nulo. E o que diz a largura do pulso de um servo e a
    /// frequencia de um STEP no PWM.
    /// </summary>
    public Periodo? PeriodicoEm(int pino, double us)
    {
        lock (trava)
        {
            if (!periodos.TryGetValue(pino, out var lista)) return null;
            for (int i = lista.Count - 1; i >= 0 && i >= lista.Count - 4; i--)
                if (lista[i].Desde <= us && us < lista[i].Ate) return lista[i];
            return null;
        }
    }

    /// <summary>O sinal periódico do pino para aqui (noTone, detach).</summary>
    public void Parar(double us, int pino)
    {
        lock (trava) FecharPeriodo(us, pino);
    }

    void FecharPeriodo(double us, int pino)
    {
        if (!periodos.TryGetValue(pino, out var lista) || lista.Count == 0) return;
        var ultimo = lista[^1];
        if (ultimo.Ate > us) lista[^1] = ultimo with { Ate = Math.Max(ultimo.Desde, us) };
    }

    /// <summary>
    /// Bytes saindo pela serial no pino TX. Cada um ocupa dez bits (início,
    /// oito de dado, parada) e espera o anterior terminar -- é assim que a
    /// UART manda, um atrás do outro, sem a placa esperar.
    /// </summary>
    public void Serial(double us, int pino, ReadOnlySpan<byte> bytes, int baud)
    {
        if (pino < 0 || baud <= 0 || bytes.Length == 0) return;
        double bit = 1e6 / baud;

        lock (trava)
        {
            if (!quadros.TryGetValue(pino, out var lista)) quadros[pino] = lista = [];
            double t = Math.Max(us, fimDaSerial.GetValueOrDefault(pino, 0));

            foreach (byte b in bytes)
            {
                lista.Add(new Quadro(t, b, baud));
                t += 10 * bit;
            }

            fimDaSerial[pino] = t;
            if (lista.Count > QuadrosPorPino) lista.RemoveRange(0, lista.Count - QuadrosPorPino);
        }
    }

    // =====================================================================
    //  leitura
    // =====================================================================

    /// <summary>
    /// O que o pino fez entre <paramref name="de"/> e <paramref name="ate"/>:
    /// o nível no começo e cada mudança depois, em ordem.
    /// </summary>
    /// <param name="resolucaoUs">
    /// O menor tempo que ainda se enxerga (um pixel da tela). Um sinal
    /// periodico mais rapido que isso vira uma faixa cheia, dois pontos por
    /// pixel, em vez de cada borda: um PWM de 40 kHz num segundo de tela
    /// seriam oitenta mil bordas por quadro para desenhar o mesmo borrao.
    /// </param>
    public List<(double Us, int Nivel)> Onda(int pino, double de, double ate, int limite = 20000,
                                             double resolucaoUs = 0)
    {
        var saida = new List<(double, int)>();

        lock (trava)
        {
            // ---- as bordas escritas pelo programa ----
            int nivel = 0;
            var escritas = new List<(double Us, int Nivel)>();

            if (bordas.TryGetValue(pino, out var b) && b.Quantas > 0)
            {
                int primeira = PrimeiraDepois(b, de);
                if (primeira > 0) nivel = b.Niveis[b.Indice(primeira - 1)];

                for (int k = primeira; k < b.Quantas; k++)
                {
                    double t = b.Tempos[b.Indice(k)];
                    if (t > ate) break;
                    escritas.Add((t, b.Niveis[b.Indice(k)]));
                }
            }

            // ---- a serial: parada no alto, e cada quadro por cima ----
            if (quadros.TryGetValue(pino, out var dosQuadros) && dosQuadros.Count > 0
                && dosQuadros[0].Inicio <= ate)
            {
                // Depois do Serial.begin a linha fica em repouso ALTA.
                if (escritas.Count == 0 && nivel == 0) nivel = 1;

                foreach (var q in dosQuadros)
                {
                    double bit = 1e6 / q.Baud;
                    if (q.Inicio + 10 * bit < de) continue;
                    if (q.Inicio > ate) break;

                    int anterior = -1;
                    for (int k = 0; k < 10; k++)
                    {
                        int v = k == 0 ? 0 : k == 9 ? 1 : (q.Valor >> (k - 1)) & 1;
                        if (v == anterior) continue;
                        anterior = v;
                        double t = q.Inicio + k * bit;
                        if (t < de) { nivel = v; continue; }
                        if (t > ate) break;
                        escritas.Add((t, v));
                    }
                }

                escritas.Sort((x, y) => x.Us.CompareTo(y.Us));
            }

            // ---- os periodicos cortam por cima: enquanto valem, mandam ----
            if (periodos.TryGetValue(pino, out var dosPeriodos))
            {
                foreach (var p in dosPeriodos)
                {
                    if (p.Ate <= de || p.Desde >= ate) continue;

                    double inicio = Math.Max(p.Desde, de);
                    double fim = Math.Min(p.Ate, ate);
                    double volta = 1e6 / p.Hz, alto = volta * p.Ciclo;

                    // O que o programa escreveu enquanto o periodico valia
                    // nao chega no fio: quem manda e o hardware.
                    escritas.RemoveAll(e => e.Us >= inicio && e.Us < fim);

                    double n = Math.Floor((inicio - p.Desde) / volta);
                    double t0 = p.Desde + n * volta;

                    if (inicio == de) nivel = inicio - t0 < alto ? 1 : 0;
                    else escritas.Add((inicio, inicio - t0 < alto ? 1 : 0));

                    if (resolucaoUs > 0 && volta < 2 * resolucaoUs)
                    {
                        // Mais rapido que a tela: uma faixa, sobe e desce a cada pixel.
                        for (double t = inicio; t < fim && escritas.Count < limite; t += resolucaoUs)
                        {
                            escritas.Add((t, 1));
                            escritas.Add((t + resolucaoUs / 2, 0));
                        }
                    }
                    else
                        for (double t = t0; t < fim && escritas.Count < limite; t += volta)
                        {
                            if (t > inicio) escritas.Add((t, 1));
                            if (t + alto > inicio && t + alto < fim) escritas.Add((t + alto, 0));
                        }

                    // Quando o periodico acaba dentro da janela, o pino fica
                    // no nivel em que o hardware o deixou: baixo.
                    if (p.Ate < ate && p.Ate > de) escritas.Add((p.Ate, 0));
                }

                escritas.Sort((x, y) => x.Us.CompareTo(y.Us));
            }

            saida.Add((de, nivel));
            int ultimo = nivel;
            foreach (var (t, v) in escritas)
            {
                if (v == ultimo) continue;
                saida.Add((t, v));
                ultimo = v;
                if (saida.Count >= limite) break;
            }
        }

        return saida;
    }

    /// <summary>Os bytes da serial que cabem na janela -- para escrever a letra em cima do sinal.</summary>
    public List<Quadro> QuadrosEntre(int pino, double de, double ate)
    {
        lock (trava)
        {
            if (!quadros.TryGetValue(pino, out var lista)) return [];
            return [.. lista.Where(q => q.Inicio + 10e6 / q.Baud >= de && q.Inicio <= ate)];
        }
    }

    /// <summary>
    /// A última borda de subida do pino antes de <paramref name="ate"/>, ou NaN.
    ///
    /// Procura perto primeiro e vai alargando: num PWM a subida está a um
    /// período, e remontar dois segundos de onda a cada quadro seria jogar
    /// tempo fora; num pisca lento ela está longe, e tem que ser achada.
    /// </summary>
    public double UltimaSubida(int pino, double ate, double procurarAte = 2e6)
    {
        for (double alcance = 1000; ; alcance *= 8)
        {
            double olhado = Math.Min(alcance, procurarAte);
            var onda = Onda(pino, ate - olhado, ate);
            for (int k = onda.Count - 1; k >= 1; k--)
                if (onda[k].Nivel == 1 && onda[k - 1].Nivel == 0) return onda[k].Us;
            if (olhado >= procurarAte) return double.NaN;
        }
    }

    /// <summary>Algum registro neste pino? O analisador usa para dizer "sem sinal".</summary>
    public bool TemAlgo(int pino)
    {
        lock (trava)
            return bordas.ContainsKey(pino) || periodos.ContainsKey(pino) || quadros.ContainsKey(pino);
    }

    static int PrimeiraDepois(Bordas b, double us)
    {
        int lo = 0, hi = b.Quantas;
        while (lo < hi)
        {
            int meio = (lo + hi) / 2;
            if (b.Tempos[b.Indice(meio)] < us) lo = meio + 1; else hi = meio;
        }
        return lo;
    }
}
