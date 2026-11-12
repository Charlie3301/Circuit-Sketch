using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Simulador.App;

/// <summary>
/// As luzinhas dos modulos: a de energia do L298N e do L9110S, as quatro
/// fases do ULN2003, a dos reles, a do receptor de radio e a do infravermelho. Elas dizem o que a conta de energia decidiu --
/// o modulo esta com a logica alimentada, a fase A esta ligada -- sem a
/// pessoa ter que medir nada, que e para isso que elas existem na placa.
/// </summary>
public partial class EditorCircuito
{
    /// <summary>Onde fica cada luz (pixels da peca), o raio, a cor e o que a acende.</summary>
    static readonly Dictionary<string, (double X, double Y, double R, Color Cor, string Chave)[]> LuzesDosModulos = new()
    {
        // Os LEDs de energia dos drivers (pecas-drivers-a25.py, nas posicoes da foto de cada placa).
        ["l298n"] = [(82.96, 123.40, 0.9 * Mm, Color.FromRgb(0xFF, 0x3B, 0x30), "energia")],
        ["l9110s"] = [(36.47, 55.94, 0.75 * Mm, Color.FromRgb(0xFF, 0x3B, 0x30), "energia")],
        // A luz do receptor: acesa com o controle ligado, piscando sem ele.
        ["receptor-rc"] = [(74.46, 18.90, 0.75 * Mm, Color.FromRgb(0xFF, 0x3B, 0x30), "rx")],
        // O LED de sinal do receptor infravermelho (KY-022, pecas-outros.py):
        // acende enquanto chega o sinal do controle.
        ["ir-receiver"] = [(47.2, 42.3, 0.6 * Mm, Color.FromRgb(0xFF, 0x3B, 0x30), "ir")],
        // Os modulos de rele: a luz de energia e a de cada canal (o LED do opto).
        ["modulo-rele-1"] = [(49.97, 185.95, 0.9 * Mm, Color.FromRgb(0xFF, 0x3B, 0x30), "vcc"), (63.34, 141.66, 0.9 * Mm, Color.FromRgb(0xFF, 0x3B, 0x30), "canal1")],
        ["modulo-rele-2"] =
        [
            (59.57, 185.95, 0.9 * Mm, Color.FromRgb(0xFF, 0x3B, 0x30), "vcc"),
            (53.74, 141.66, 0.9 * Mm, Color.FromRgb(0xFF, 0x3B, 0x30), "canal1"),
            (120.94, 141.66, 0.9 * Mm, Color.FromRgb(0xFF, 0x3B, 0x30), "canal2"),
        ],
        ["modulo-rele-4"] =
        [
            (78.77, 185.95, 0.9 * Mm, Color.FromRgb(0xFF, 0x3B, 0x30), "vcc"),
            (53.74, 141.66, 0.9 * Mm, Color.FromRgb(0xFF, 0x3B, 0x30), "canal1"),
            (120.94, 141.66, 0.9 * Mm, Color.FromRgb(0xFF, 0x3B, 0x30), "canal2"),
            (188.14, 141.66, 0.9 * Mm, Color.FromRgb(0xFF, 0x3B, 0x30), "canal3"),
            (255.34, 141.66, 0.9 * Mm, Color.FromRgb(0xFF, 0x3B, 0x30), "canal4"),
        ],
        // Os LEDs de energia dos modulos (Alpha 25): o do adaptador I2C do LCD
        // (modulo-i2c.py) e o do PCM5102A (pecas-pcm5102a.py).
        ["modulo-i2c"] = [(22.3, 58.2, 0.6 * Mm, Color.FromRgb(0xFF, 0x3B, 0x30), "vcc-gnd")],
        ["pcm5102a"] = [(58.6, 48.8, 0.6 * Mm, Color.FromRgb(0xFF, 0x3B, 0x30), "vin-gnd")],
        // Os sensores (pecas-sensores.py): o PWR acende com o VCC, o DO quando o
        // sensor detecta (a saida do comparador).
        ["photoresistor-sensor"] = [(95.62, 15.28, 0.55 * Mm, Color.FromRgb(0xFF, 0x3B, 0x30), "vcc-gnd"), (95.62, 43.25, 0.55 * Mm, Color.FromRgb(0xFF, 0x3B, 0x30), "sensor-do")],
        ["flame-sensor"] = [(130.39, 15.28, 0.55 * Mm, Color.FromRgb(0xFF, 0x3B, 0x30), "vcc-gnd"), (130.39, 43.25, 0.55 * Mm, Color.FromRgb(0xFF, 0x3B, 0x30), "sensor-do")],
        ["gas-sensor"] = [(99.02, 12.26, 0.55 * Mm, Color.FromRgb(0xFF, 0x3B, 0x30), "vcc-gnd"), (99.02, 72.74, 0.55 * Mm, Color.FromRgb(0xFF, 0x3B, 0x30), "sensor-do")],
        ["big-sound-sensor"] = [(65.01, 53.57, 0.55 * Mm, Color.FromRgb(0xFF, 0x3B, 0x30), "vcc-gnd"), (65.01, 6.70, 0.55 * Mm, Color.FromRgb(0xFF, 0x3B, 0x30), "sensor-do")],
        ["small-sound-sensor"] = [(65.01, 53.57, 0.55 * Mm, Color.FromRgb(0xFF, 0x3B, 0x30), "vcc-gnd"), (65.01, 6.70, 0.55 * Mm, Color.FromRgb(0xFF, 0x3B, 0x30), "sensor-do")],
        ["mpu6050"] = [(63.59, 36.11, 0.55 * Mm, Color.FromRgb(0x3A, 0xFF, 0x6A), "vcc-gnd")],
        // O modulo de obstaculo (FC-51) e o seguidor de linha (TCRT5000): os LEDs verdes de energia e
        // de saida do primeiro, o de sinal do segundo (aceso com o reflexo).
        ["ir-obstacle-sensor"] = [(129.00, 11.00, 0.55 * Mm, Color.FromRgb(0x3A, 0xFF, 0x6A), "vcc-gnd"), (129.79, 39.80, 0.55 * Mm, Color.FromRgb(0x3A, 0xFF, 0x6A), "sensor-do")],
        ["tcrt5000"] = [(120.76, 7.18, 0.55 * Mm, Color.FromRgb(0xFF, 0x3B, 0x30), "sensor-do")],
        // O HC-05 (pecas-hc05.py): rapido procurando, duas piscadas conectado, devagar no modo AT.
        ["hc-05"] = [(3.73, 111.83, 0.5 * Mm, Color.FromRgb(0xFF, 0x3B, 0x30), "hc05")],
        // O LED do modulo do AMS1117 (pecas-reguladores.py): fica na saida, acende com o VOUT.
        ["ams1117"] = [(28.7, 35.1, 0.5 * Mm, Color.FromRgb(0xFF, 0x3B, 0x30), "vout-gnd")],
        // O roteador (pecas-wifi-ap.py): energia, WiFi, clientes e internet.
        ["wifi-ap"] =
        [
            (28.7, 93.0, 0.75 * Mm, Color.FromRgb(0x5C, 0xFF, 0x5A), "ap-energia"),
            (60.1, 93.0, 0.75 * Mm, Color.FromRgb(0x5C, 0xFF, 0x5A), "ap-wifi"),
            (91.1, 93.0, 0.75 * Mm, Color.FromRgb(0x5C, 0xFF, 0x5A), "ap-clientes"),
            (122.5, 93.0, 0.75 * Mm, Color.FromRgb(0x5C, 0xFF, 0x5A), "ap-internet"),
        ],
        ["uln2003"] =
        [
            // Os LEDs de 3 mm da placa verde (A..D de cima para baixo, ao lado do conector).
            (103.56, 15.69, 1.5 * Mm, Color.FromRgb(0xFF, 0x3B, 0x30), "A"),
            (103.56, 33.90, 1.5 * Mm, Color.FromRgb(0xFF, 0x3B, 0x30), "B"),
            (103.56, 52.12, 1.5 * Mm, Color.FromRgb(0xFF, 0x3B, 0x30), "C"),
            (103.56, 70.34, 1.5 * Mm, Color.FromRgb(0xFF, 0x3B, 0x30), "D"),
        ],
    };

    const double Mm = 96 / 25.4;

    readonly Dictionary<string, List<Ellipse>> luzesDeModulo = [];

    void CriarLuzesDeModulo(PecaNoCircuito peca, PecaDef def)
    {
        if (!LuzesDosModulos.TryGetValue(peca.Tipo, out var tabela)) return;
        var luzes = new List<Ellipse>();
        foreach (var (_, _, raio, cor, _) in tabela)
        {
            var luz = new Ellipse
            {
                Width = raio * 2,
                Height = raio * 2,
                Fill = new SolidColorBrush(cor),
                IsHitTestVisible = false,
                Opacity = 0,
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = cor, BlurRadius = 8, ShadowDepth = 0, Opacity = 0.9,
                },
            };
            PorNaCamadaDa(peca, luz);
            luzes.Add(luz);
        }
        luzesDeModulo[peca.Id] = luzes;
        PosicionarLuzesDeModulo(peca, def);
    }

    void PosicionarLuzesDeModulo(PecaNoCircuito peca, PecaDef def)
    {
        if (!luzesDeModulo.TryGetValue(peca.Id, out var luzes)
            || !LuzesDosModulos.TryGetValue(peca.Tipo, out var tabela)) return;
        for (int i = 0; i < luzes.Count && i < tabela.Length; i++)
        {
            var (lx, ly, raio, _, _) = tabela[i];
            var p = Geometria.PosicaoLocal(peca, def, lx, ly);
            Canvas.SetLeft(luzes[i], p.X - raio);
            Canvas.SetTop(luzes[i], p.Y - raio);
        }
    }

    void ApagarLuzesDeModulo()
    {
        foreach (var luzes in luzesDeModulo.Values)
            foreach (var l in luzes) mundo.Children.Remove(l);
        luzesDeModulo.Clear();
    }

    /// <summary>A cada quadro: cada luz pelo estado que a conta de energia deixou.</summary>
    void AcenderLuzesDeModulo()
    {
        foreach (var (id, luzes) in luzesDeModulo)
        {
            var peca = Atual.Achar(id);
            if (peca is null || !LuzesDosModulos.TryGetValue(peca.Tipo, out var tabela)) continue;
            for (int i = 0; i < luzes.Count && i < tabela.Length; i++)
            {
                double alvo = LuzAcesa(peca, tabela[i].Chave) ? 0.95 : 0;
                if (Math.Abs(luzes[i].Opacity - alvo) > 0.01) luzes[i].Opacity = alvo;
            }
        }
    }

    bool LuzAcesa(PecaNoCircuito peca, string chave) => ModulosDeRele.EhModulo(peca.Tipo) ? LuzDoModuloDeRele(peca, chave) : chave switch
    {
        "energia" => EnergiaDoQuadro?.DriversLigados.Contains(peca.Id) == true,
        // A fase do ULN2003: o LED esta na entrada, acende com ela em HIGH.
        "A" or "B" or "C" or "D" => EnergiaDoQuadro?.KDaSaida(peca.Id, chave) is not null,
        "rx" => ReceptorAlimentado(peca) && (ControleLigado() is not null || DateTime.Now.Millisecond < 500),
        "ir" => teclaDoControle.Count > 0 && EnergiaDoQuadro is { } conta
                && conta.TensaoDoPino(peca.Id, "VCC") - conta.TensaoDoPino(peca.Id, "GND") >= 2.5,
        // O LED de energia de um modulo: aceso com 2,5 V ou mais entre os pinos.
        "vcc-gnd" => TensaoDoModulo(peca) >= 2.5,
        // O LED do DO: o comparador disparou (o controle passou do limiar), com o modulo alimentado.
        "sensor-do" => TensaoDoModulo(peca) >= 2.5 && Comportamento.De(peca.Tipo) is { } comp && peca.Giro > comp.Limiar,
        "hc05" => Hc05.LedAceso(peca.Id),
        "vin-gnd" => EnergiaDoQuadro is { } c2 && c2.TensaoDoPino(peca.Id, "VIN") - c2.TensaoDoPino(peca.Id, "GND") >= 2.5,
        "vout-gnd" => EnergiaDoQuadro is { } c3 && c3.TensaoDoPino(peca.Id, "VOUT") - c3.TensaoDoPino(peca.Id, "GND.2") >= 1.8,
        // O roteador: ligado com a simulacao rodando; o WiFi com a rede nomeada;
        // clientes com alguma placa conectada nela; internet com o "Usar internet".
        "ap-energia" => EmSimulacao,
        "ap-wifi" => EmSimulacao && !string.IsNullOrWhiteSpace(peca.Ssid),
        "ap-clientes" => EmSimulacao && RedeDoRoteador?.ClientesDe(peca.Ssid) > 0,
        "ap-internet" => EmSimulacao && RedeDoRoteador?.UsarInternet == true,
        _ => false,
    };

    /// <summary>A tensao entre a alimentacao e o terra do modulo (o VCC e o GND, ou o V+ do TCRT5000).</summary>
    double TensaoDoModulo(PecaNoCircuito peca)
    {
        if (EnergiaDoQuadro is not { } conta || Catalogo.Obter(peca.Tipo) is not { } def) return 0;
        // O VCC e o GND quando a peca tem (o modulo I2C tem tambem o VDD e o VSS do LCD); senao o V+.
        var vcc = def.Pinos.FirstOrDefault(p => p.Nome == "VCC") ?? def.Pinos.FirstOrDefault(p => Netlist.EhPinoDeAlimentacao(p.Nome));
        var gnd = def.Pinos.FirstOrDefault(p => p.Nome == "GND") ?? def.Pinos.FirstOrDefault(p => Netlist.EhPinoDeTerra(p.Nome));
        return vcc is null || gnd is null ? 0 : conta.TensaoDoPino(peca.Id, vcc.Nome) - conta.TensaoDoPino(peca.Id, gnd.Nome);
    }

    /// <summary>A rede da bancada, para as luzes do roteador (a janela entrega).</summary>
    public Simulador.Rede RedeDoRoteador { get; set; }

    /// <summary>Para o teste: a luz numero <paramref name="indice"/> do modulo esta acesa?</summary>
    internal bool LuzDeModuloAcesaParaTeste(string idDaPeca, int indice)
        => luzesDeModulo.TryGetValue(idDaPeca, out var luzes) && indice < luzes.Count && luzes[indice].Opacity > 0.5;
}
