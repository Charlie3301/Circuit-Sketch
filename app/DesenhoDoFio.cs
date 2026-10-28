using System.Windows;
using System.Windows.Media;

namespace Simulador.App;

/// <summary>
/// O fio da Alpha 25: o cabinho rigido de 22 AWG das protoboards, na
/// grossura de verdade (1,3 mm por fora da capa de PVC), com volume -- a
/// sombra no papel, a borda escura, o lado de baixo sombreado e o brilho
/// de cima -- e as duas pontas desencapadas: o cobre estanhado de ~1,2 mm
/// que entra no furo ou encosta no pino.
///
/// Um elemento so por fio, desenhado no OnRender: com cinco tracos e as
/// pontas por fio, um Path para cada parte multiplicaria os elementos do
/// Canvas, e o arraste redesenha todos os fios a cada quadro.
/// </summary>
sealed class DesenhoDoFio : FrameworkElement
{
    /// <summary>A capa de PVC do cabinho: 1,3 mm, em pixels do circuito.</summary>
    public const double Largura = 1.3 * 96 / 25.4;

    /// <summary>O pedaco desencapado de cada ponta.</summary>
    const double Ponta = 1.2 * 96 / 25.4;

    static readonly Brush Sombra = Congelar(new SolidColorBrush(Color.FromArgb(0x50, 0, 0, 0)));
    static readonly Brush Borda = Congelar(new SolidColorBrush(Color.FromArgb(0x9C, 0x08, 0x08, 0x0A)));
    static readonly Brush LadoDeBaixo = Congelar(new SolidColorBrush(Color.FromArgb(0x3A, 0, 0, 0)));
    static readonly Brush Brilho = Congelar(new SolidColorBrush(Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF)));
    static readonly Brush Estanho = Congelar(new LinearGradientBrush(
        [new GradientStop(Color.FromRgb(0xF2, 0xF4, 0xF6), 0), new GradientStop(Color.FromRgb(0xA9, 0xAF, 0xB6), 0.6),
         new GradientStop(Color.FromRgb(0x7C, 0x83, 0x8B), 1)], 90));
    static readonly Brush BordaDoEstanho = Congelar(new SolidColorBrush(Color.FromRgb(0x4E, 0x54, 0x5B)));

    readonly Geometry inteira, capa, pontas;
    readonly Brush cor;
    readonly double largura;

    /// <param name="forma">O caminho do fio, de pino a pino.</param>
    /// <param name="cor">A cor da capa (ou a da energia).</param>
    /// <param name="fator">A espessura das configuracoes (1 = a de verdade).</param>
    /// <param name="desencapado">Com as pontas de cobre (a trilha de baixo da PCB e estanho puro).</param>
    public DesenhoDoFio(Geometry forma, Brush cor, double fator, bool desencapado)
    {
        IsHitTestVisible = false;
        this.cor = cor;
        largura = Largura * fator;
        inteira = forma;
        capa = forma;
        pontas = null;
        if (!desencapado) return;

        // O caminho em linha quebrada: tira a ponta de cada lado da capa e
        // guarda os dois pedacos desencapados.
        var pontos = Pontos(forma);
        double total = 0;
        for (int i = 1; i < pontos.Count; i++) total += (pontos[i] - pontos[i - 1]).Length;
        if (total < 2 * Ponta + largura) return;

        var (inicio, aposInicio) = Cortar(pontos, Ponta);
        pontos.Reverse();
        var (fim, aposFim) = Cortar(pontos, Ponta);
        pontos.Reverse();

        // A capa: do corte do comeco ao corte do fim.
        var meio = new List<Point> { inicio };
        double andado = 0;
        for (int i = 1; i < pontos.Count; i++)
        {
            andado += (pontos[i] - pontos[i - 1]).Length;
            if (andado > Ponta && andado < total - Ponta) meio.Add(pontos[i]);
        }
        meio.Add(fim);
        capa = Linha(meio);

        var grupo = new GeometryGroup();
        grupo.Children.Add(new LineGeometry(pontos[0], aposInicio));
        grupo.Children.Add(new LineGeometry(pontos[^1], aposFim));
        grupo.Freeze();
        pontas = grupo;
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = largura;
        Pen Caneta(Brush b, double g, PenLineCap cap = PenLineCap.Round)
        {
            var p = new Pen(b, g) { StartLineCap = cap, EndLineCap = cap, LineJoin = PenLineJoin.Round };
            p.Freeze();
            return p;
        }

        // a sombra no papel (a luz vem de cima, a esquerda)
        dc.PushTransform(new TranslateTransform(w * 0.3, w * 0.45));
        dc.DrawGeometry(null, Caneta(Sombra, w), inteira);
        dc.Pop();

        // o cobre das pontas, por baixo da capa
        if (pontas is not null)
        {
            dc.DrawGeometry(null, Caneta(BordaDoEstanho, w * 0.5 + 0.8), pontas);
            dc.DrawGeometry(null, Caneta(Estanho, w * 0.5), pontas);
        }

        // a capa: a borda escura, a cor, o lado de baixo e o brilho de cima
        dc.DrawGeometry(null, Caneta(Borda, w + 1.4), capa);
        dc.DrawGeometry(null, Caneta(cor, w), capa);
        dc.PushTransform(new TranslateTransform(w * 0.17, w * 0.2));
        dc.DrawGeometry(null, Caneta(LadoDeBaixo, w * 0.42), capa);
        dc.Pop();
        dc.PushTransform(new TranslateTransform(-w * 0.16, -w * 0.2));
        dc.DrawGeometry(null, Caneta(Brilho, w * 0.18), capa);
        dc.Pop();
    }

    /// <summary>Os pontos do caminho (as curvas viram linha quebrada fina).</summary>
    static List<Point> Pontos(Geometry forma)
    {
        var lista = new List<Point>();
        var plana = forma.GetFlattenedPathGeometry(0.25, ToleranceType.Absolute);
        foreach (var fig in plana.Figures)
        {
            lista.Add(fig.StartPoint);
            foreach (var seg in fig.Segments)
            {
                if (seg is LineSegment l) lista.Add(l.Point);
                else if (seg is PolyLineSegment pl) lista.AddRange(pl.Points);
            }
        }
        return lista;
    }

    /// <summary>O ponto a <paramref name="d"/> do comeco, andando pelo caminho (e o mesmo, para a ponta).</summary>
    static (Point Corte, Point Ponta) Cortar(List<Point> pontos, double d)
    {
        double andado = 0;
        for (int i = 1; i < pontos.Count; i++)
        {
            double trecho = (pontos[i] - pontos[i - 1]).Length;
            if (andado + trecho >= d && trecho > 0)
            {
                var p = pontos[i - 1] + (pontos[i] - pontos[i - 1]) * ((d - andado) / trecho);
                return (p, p);
            }
            andado += trecho;
        }
        return (pontos[^1], pontos[^1]);
    }

    static Geometry Linha(List<Point> pontos)
    {
        var fig = new PathFigure { StartPoint = pontos[0] };
        if (pontos.Count > 1) fig.Segments.Add(new PolyLineSegment(pontos.Skip(1), true));
        var geo = new PathGeometry();
        geo.Figures.Add(fig);
        geo.Freeze();
        return geo;
    }

    static Brush Congelar(Brush b)
    {
        b.Freeze();
        return b;
    }
}
