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
        ["l298n"] = [(24.0 * Mm, 31.0 * Mm, 0.9 * Mm, Color.FromRgb(0xFF, 0x3B, 0x30), "energia")],
        ["l9110s"] = [(26.0 * Mm, 11.4 * Mm, 0.8 * Mm, Color.FromRgb(0xFF, 0x3B, 0x30), "energia")],
        // A luz do receptor: acesa com o controle ligado, piscando sem ele.
        ["receptor-rc"] = [(8.0 * Mm, 22.6 * Mm, 0.9 * Mm, Color.FromRgb(0xFF, 0x3B, 0x30), "rx")],
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
            (26.0 * Mm, 12.5 * Mm, 0.9 * Mm, Color.FromRgb(0xFF, 0x3B, 0x30), "A"),
            (26.0 * Mm, 15.5 * Mm, 0.9 * Mm, Color.FromRgb(0xFF, 0x3B, 0x30), "B"),
            (26.0 * Mm, 18.5 * Mm, 0.9 * Mm, Color.FromRgb(0xFF, 0x3B, 0x30), "C"),
            (26.0 * Mm, 21.5 * Mm, 0.9 * Mm, Color.FromRgb(0xFF, 0x3B, 0x30), "D"),
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
            Panel.SetZIndex(luz, 145);
            mundo.Children.Add(luz);
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
        "vcc-gnd" => EnergiaDoQuadro is { } c1 && c1.TensaoDoPino(peca.Id, "VCC") - c1.TensaoDoPino(peca.Id, "GND") >= 2.5,
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

    /// <summary>A rede da bancada, para as luzes do roteador (a janela entrega).</summary>
    public Simulador.Rede RedeDoRoteador { get; set; }

    /// <summary>Para o teste: a luz numero <paramref name="indice"/> do modulo esta acesa?</summary>
    internal bool LuzDeModuloAcesaParaTeste(string idDaPeca, int indice)
        => luzesDeModulo.TryGetValue(idDaPeca, out var luzes) && indice < luzes.Count && luzes[indice].Opacity > 0.5;
}
