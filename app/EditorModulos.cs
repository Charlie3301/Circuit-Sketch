using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Simulador.App;

/// <summary>
/// As luzinhas dos modulos de motor: a de energia do L298N e do L9110S, as
/// quatro fases do ULN2003. Elas dizem o que a conta de energia decidiu --
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
        _ => false,
    };

    /// <summary>Para o teste: a luz numero <paramref name="indice"/> do modulo esta acesa?</summary>
    internal bool LuzDeModuloAcesaParaTeste(string idDaPeca, int indice)
        => luzesDeModulo.TryGetValue(idDaPeca, out var luzes) && indice < luzes.Count && luzes[indice].Opacity > 0.5;
}
