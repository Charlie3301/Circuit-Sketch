using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Simulador.App;

/// <summary>
/// O braco (horn) do servo, desenhado por cima do corpo e girando no eixo
/// (Alpha 25): a peca de plastico branco parafusada no estriado -- o cubo
/// redondo, os bracos que afinam ate a ponta arredondada, os furos (vazados:
/// aparece o servo por baixo) e o parafuso no centro.
///
/// Cada servo escolhe o seu, ou nenhum (so o estriado). Os micro (SG90 e
/// FS90R) tem os tres do saquinho que vem com eles: o simples, o duplo e a
/// cruz. O MG996R, maior, tem tambem a estrela de seis bracos e o disco.
///
/// As medidas sao as dos bracos de verdade, em milimetros: os do SG90 (cubo
/// de 6,8 mm, parafuso M2, o simples com 15 mm do centro a ponta) e os do
/// MG996R (cubo de 9,8 mm, parafuso M3, o simples com 22,5 mm, o disco de
/// 24 mm). O eixo de cada servo e o do desenho do corpo
/// (ferramentas/extrair-svg/pecas-servos.py).
/// </summary>
public partial class EditorCircuito
{
    /// <summary>Um braco que sai do cubo: o angulo, o comprimento (do centro a ponta) e os furos, em mm.</summary>
    internal readonly record struct Haste(double Angulo, double Comprimento, double RaioDaPonta, double MeiaLarguraNaRaiz,
                                          int Furos, double RaioDoFuro, double PrimeiroFuro);

    /// <summary>
    /// Um tipo de braco: o cubo, o parafuso e as hastes -- ou, no disco, o
    /// raio dele e os furos (raio e angulo de cada um).
    /// </summary>
    internal sealed record ModeloDeBraco(double RaioDoCubo, double RaioDoParafuso, Haste[] Hastes,
                                         double RaioDoDisco = 0, (double Raio, double Angulo)[] FurosDoDisco = null);

    /// <summary>Onde fica o eixo de cada servo, em pixels da peca.</summary>
    static readonly Dictionary<string, Point> EixosDosServos = new()
    {
        ["servo"] = new(88.1, 60.0),
        ["servo-360"] = new(87.5, 45.35),
        ["servo-mg996r"] = new(119.1, 49.1),
    };

    public static bool TemBraco(string tipo) => tipo is not null && EixosDosServos.ContainsKey(tipo);

    static readonly (string Id, string Nome)[] BracosMicro =
        [("nenhum", "Nenhum"), ("simples", "Simples"), ("duplo", "Duplo"), ("cruz", "Cruz")];

    static readonly (string Id, string Nome)[] BracosGrandes =
        [.. BracosMicro, ("estrela", "Estrela (6 braços)"), ("disco", "Disco")];

    /// <summary>Os bracos que este servo aceita (vazio: nao e servo).</summary>
    internal static (string Id, string Nome)[] OpcoesDeBraco(string tipo)
        => tipo == "servo-mg996r" ? BracosGrandes : TemBraco(tipo) ? BracosMicro : [];

    /// <summary>O braco da peca: o escolhido, ou o que vem montado (a cruz no 360, o simples nos outros).</summary>
    internal static string BracoDa(PecaNoCircuito peca)
        => OpcoesDeBraco(peca.Tipo).Any(o => o.Id == peca.Braco) ? peca.Braco
         : peca.Tipo == "servo-360" ? "cruz" : "simples";

    /// <summary>As medidas do braco; nulo quando e nenhum.</summary>
    static ModeloDeBraco ModeloDoBraco(string tipo, string braco)
    {
        static Haste[] Em(int quantas, params Haste[] alternadas)
            => [.. Enumerable.Range(0, quantas).Select(k => alternadas[k % alternadas.Length] with { Angulo = 360.0 * k / quantas })];

        if (tipo == "servo-mg996r")
            return braco switch
            {
                "simples" => new(4.9, 2.75, [new(0, 22.5, 2.65, 3.8, 6, 0.8, 7.4)]),
                "duplo" => new(4.9, 2.75, Em(2, new Haste(0, 23.5, 2.4, 3.6, 6, 0.75, 7.4))),
                "cruz" => new(4.9, 2.75, Em(4, new Haste(0, 17.0, 2.2, 3.4, 4, 0.75, 7.2), new Haste(0, 14.0, 2.1, 3.3, 3, 0.75, 7.2))),
                "estrela" => new(4.9, 2.75, Em(6, new Haste(0, 13.6, 1.9, 2.9, 3, 0.7, 7.4))),
                // O disco: quatro fileiras de tres furos em cruz e, entre elas, quatro de dois.
                "disco" => new(4.9, 2.75, [], 12.0,
                    [.. Enumerable.Range(0, 4).SelectMany(k => new[] { (6.6, 90.0 * k), (8.6, 90.0 * k), (10.6, 90.0 * k), (7.6, 90.0 * k + 45), (9.6, 90.0 * k + 45) })]),
                _ => null,
            };
        return braco switch
        {
            "simples" => new(3.4, 1.7, [new(0, 15.0, 1.7, 2.65, 6, 0.5, 4.9)]),
            "duplo" => new(3.4, 1.7, Em(2, new Haste(0, 16.5, 1.5, 2.4, 6, 0.5, 5.0))),
            "cruz" => new(3.4, 1.7, Em(4, new Haste(0, 10.2, 1.35, 2.2, 3, 0.48, 4.9), new Haste(0, 8.6, 1.3, 2.1, 2, 0.48, 4.9))),
            _ => null,
        };
    }

    /// <summary>Onde o eixo fica no desenho do braco, para girar e posicionar.</summary>
    readonly Dictionary<string, Point> centrosDosBracos = [];

    /// <summary>O desenho do braco, com o eixo na origem e o braco para a direita (0 grau).</summary>
    static DrawingGroup DesenhoDoBraco(ModeloDeBraco b, double escala)
    {
        double mm = 96 / 25.4 * escala;
        double cubo = b.RaioDoCubo * mm;
        static Point Polar(double raio, double graus)
            => new(raio * Math.Cos(graus * Math.PI / 180), raio * Math.Sin(graus * Math.PI / 180));

        var furos = new GeometryGroup();
        void Furo(Point centro, double raio) => furos.Children.Add(new EllipseGeometry(centro, raio, raio));

        Geometry contorno;
        if (b.RaioDoDisco > 0)
        {
            contorno = new EllipseGeometry(new Point(0, 0), b.RaioDoDisco * mm, b.RaioDoDisco * mm);
            foreach (var (raio, angulo) in b.FurosDoDisco) Furo(Polar(raio * mm, angulo), 0.7 * mm);
        }
        else
        {
            // O cubo, mais cada braco que afina ate a ponta redonda; os furos
            // igualmente espacados ate o centro da ponta.
            var partes = new GeometryGroup { FillRule = FillRule.Nonzero };
            partes.Children.Add(new EllipseGeometry(new Point(0, 0), cubo, cubo));
            foreach (var h in b.Hastes)
            {
                double ponta = h.RaioDaPonta * mm, raiz = h.MeiaLarguraNaRaiz * mm, fim = h.Comprimento * mm - ponta;
                var haste = new StreamGeometry { Transform = new RotateTransform(h.Angulo) };
                using (var c = haste.Open())
                {
                    c.BeginFigure(new Point(0, -raiz), true, true);
                    c.LineTo(new Point(fim, -ponta), true, false);
                    c.ArcTo(new Point(fim, ponta), new Size(ponta, ponta), 0, false, SweepDirection.Clockwise, true, false);
                    c.LineTo(new Point(0, raiz), true, false);
                }
                partes.Children.Add(haste);
                double passo = (fim - h.PrimeiroFuro * mm) / Math.Max(1, h.Furos - 1);
                for (int k = 0; k < h.Furos; k++) Furo(Polar(h.PrimeiroFuro * mm + k * passo, h.Angulo), h.RaioDoFuro * mm);
            }
            contorno = partes.GetOutlinedPathGeometry();
        }
        contorno = Geometry.Combine(contorno, furos, GeometryCombineMode.Exclude, null);
        contorno.Freeze();

        static Pen Caneta(byte r, byte g, byte b, double grossura) { var p = new Pen(new SolidColorBrush(Color.FromRgb(r, g, b)), grossura); p.Freeze(); return p; }
        var desenho = new DrawingGroup();
        using (var dc = desenho.Open())
        {
            // A sombra no corpo do servo (a luz vem de cima, da esquerda).
            dc.PushTransform(new TranslateTransform(0.35 * mm, 0.45 * mm));
            dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(0x55, 0x00, 0x00, 0x00)), null, contorno);
            dc.Pop();

            var plastico = new LinearGradientBrush(Color.FromRgb(0xFB, 0xFB, 0xF7), Color.FromRgb(0xE2, 0xE2, 0xDB), 90);
            plastico.Freeze();
            var aresta = Caneta(0x9C, 0x9C, 0x94, 0.12 * mm);
            dc.DrawGeometry(plastico, aresta, contorno);

            if (b.RaioDoDisco > 0)
            {
                // O degrau da borda do disco, mais fino que o centro.
                dc.DrawEllipse(null, Caneta(0xD2, 0xD2, 0xCA, 0.14 * mm), new Point(0, 0), (b.RaioDoDisco - 1.1) * mm, (b.RaioDoDisco - 1.1) * mm);
            }

            // O cubo, mais alto que os bracos: a luz na borda de cima.
            var alto = new RadialGradientBrush(Color.FromRgb(0xFF, 0xFF, 0xFC), Color.FromRgb(0xE6, 0xE6, 0xDF))
            {
                GradientOrigin = new Point(0.38, 0.32), Center = new Point(0.45, 0.42), RadiusX = 0.7, RadiusY = 0.7,
            };
            alto.Freeze();
            dc.DrawEllipse(alto, aresta, new Point(0, 0), cubo, cubo);
            // O rebaixo do cubo, em volta do parafuso.
            dc.DrawEllipse(null, Caneta(0xC4, 0xC4, 0xBC, 0.12 * mm), new Point(0, 0), cubo * 0.72, cubo * 0.72);

            // O parafuso: a cabeca de metal com a fenda Phillips.
            double p = b.RaioDoParafuso * mm;
            var metal = new RadialGradientBrush(Color.FromRgb(0xF0, 0xF2, 0xF4), Color.FromRgb(0x86, 0x8C, 0x94))
            {
                GradientOrigin = new Point(0.35, 0.3), Center = new Point(0.45, 0.42), RadiusX = 0.65, RadiusY = 0.65,
            };
            metal.Freeze();
            dc.DrawEllipse(metal, Caneta(0x55, 0x5B, 0x63, 0.1 * mm), new Point(0, 0), p, p);
            var fenda = new Pen(new SolidColorBrush(Color.FromRgb(0x3E, 0x43, 0x4A)), p * 0.24) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            dc.DrawLine(fenda, new Point(-p * 0.55, 0), new Point(p * 0.55, 0));
            dc.DrawLine(fenda, new Point(0, -p * 0.55), new Point(0, p * 0.55));
        }
        desenho.Freeze();
        return desenho;
    }

    /// <summary>O desenho do braco desta peca; nulo sem braco.</summary>
    static DrawingGroup DesenhoDoBraco(PecaNoCircuito peca, double escala)
        => ModeloDoBraco(peca.Tipo, BracoDa(peca)) is { } modelo ? DesenhoDoBraco(modelo, escala) : null;

    /// <summary>
    /// O giro do braco na tela: o do servo (0 grau aponta para cima, 90 para
    /// a ponta do corpo, 180 para baixo), espelhado e girado com a peca.
    /// </summary>
    static double GiroDoBraco(PecaNoCircuito peca, double anguloDoServo)
    {
        double local = anguloDoServo - 90;
        if (peca.Espelhada) local = 180 - local;
        return peca.Rotacao + local;
    }

    void CriarBracoServo(PecaNoCircuito peca, PecaDef def)
    {
        if (DesenhoDoBraco(peca, Geometria.Escala(peca)) is not { } desenho) return;
        var limites = desenho.Bounds;
        var fonte = new DrawingImage(desenho);
        fonte.Freeze();
        // O eixo e a origem do desenho: dentro da imagem, ele fica em (-Left, -Top).
        var centro = new Point(-limites.Left, -limites.Top);
        var braco = new Image
        {
            Source = fonte,
            Width = limites.Width,
            Height = limites.Height,
            Stretch = Stretch.Fill,
            IsHitTestVisible = false,
            RenderTransform = new RotateTransform(GiroDoBraco(peca, AnguloDoServo(peca)), centro.X, centro.Y),
        };
        // Por cima do corpo e por baixo da roda encaixada no eixo do 360 (a
        // roda e acessorio, na camada de cima).
        PorNaCamadaDa(peca, braco);
        bracosServo[peca.Id] = braco;
        centrosDosBracos[peca.Id] = centro;
        PosicionarBracoServo(peca, def);
    }

    /// <summary>O braco da peca selecionada (um de <see cref="OpcoesDeBraco"/>).</summary>
    public void DefinirBracoDoServo(string braco)
    {
        var peca = Selecionada;
        if (peca is null || !OpcoesDeBraco(peca.Tipo).Any(o => o.Id == braco)) return;
        peca.Braco = braco;
        Redesenhar();
        Selecionar(peca.Id, false);
        Mudou?.Invoke();
    }

    /// <summary>
    /// Para a foto de conferencia (--desenho): o braco ja no lugar, num
    /// angulo do servo, para uma tela do tamanho da peca. Nulo se a peca nao
    /// tem braco desenhado por cima.
    /// </summary>
    internal static FrameworkElement BracoParaFoto(PecaNoCircuito peca, double anguloDoServo)
    {
        if (!EixosDosServos.TryGetValue(peca.Tipo, out var eixo) || DesenhoDoBraco(peca, 1) is not { } desenho) return null;
        var limites = desenho.Bounds;
        var braco = new Image
        {
            Source = new DrawingImage(desenho),
            Width = limites.Width,
            Height = limites.Height,
            Stretch = Stretch.Fill,
            RenderTransform = new RotateTransform(anguloDoServo - 90, -limites.Left, -limites.Top),
        };
        Canvas.SetLeft(braco, eixo.X + limites.Left);
        Canvas.SetTop(braco, eixo.Y + limites.Top);
        return braco;
    }

    void PosicionarBracoServo(PecaNoCircuito peca, PecaDef def)
    {
        if (!bracosServo.TryGetValue(peca.Id, out var braco) || !EixosDosServos.TryGetValue(peca.Tipo, out var eixoDaPeca)) return;
        var centro = centrosDosBracos.GetValueOrDefault(peca.Id);
        var eixo = Geometria.PosicaoLocal(peca, def, eixoDaPeca.X, eixoDaPeca.Y);
        Canvas.SetLeft(braco, eixo.X - centro.X);
        Canvas.SetTop(braco, eixo.Y - centro.Y);
        if (braco.RenderTransform is RotateTransform giro) giro.Angle = GiroDoBraco(peca, AnguloDoServo(peca));
    }

    /// <summary>Para o teste: o braco esta na tela? E girado de quantos graus, com o eixo onde.</summary>
    internal (bool Existe, double Giro, Point Eixo) BracoParaTeste(string idDaPeca)
        => bracosServo.TryGetValue(idDaPeca, out var b) && b.RenderTransform is RotateTransform g
            ? (true, g.Angle, new Point(Canvas.GetLeft(b) + g.CenterX, Canvas.GetTop(b) + g.CenterY))
            : (false, 0, default);

    /// <summary>
    /// A cada quadro: o braco esta onde o servo chegou. Inteiro sempre, como a
    /// peca de plastico na mao (antes ficava meio transparente sem o pulso --
    /// e sem placa a fisica roda no editor, entao ele vivia apagado); so
    /// apaga do outro lado da PCB virada, como o resto da peca.
    /// </summary>
    void AnimarBracoServo(PecaNoCircuito peca)
    {
        if (!bracosServo.TryGetValue(peca.Id, out var braco) || braco.RenderTransform is not RotateTransform giro) return;
        giro.Angle = GiroDoBraco(peca, AnguloDoServo(peca));
        double opacidade = doOutroLado.Contains(peca.Id) ? 0.2 : 1.0;
        if (Math.Abs(braco.Opacity - opacidade) > 0.01) braco.Opacity = opacidade;
    }
}
