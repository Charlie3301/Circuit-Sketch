using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Simulador.App;

/// <summary>
/// A protoboard desenhada como a de verdade (Alpha 25): a MB-102 de 830
/// pontos, a de 400 e a mini de 170.
///
/// O que a placa real tem, de cima: as duas reguas de alimentacao que se
/// destacam (a emenda aparece), com a linha vermelha e a azul e os sinais
/// de + e - nas pontas; cada furo com o funil chanfrado do plastico, o
/// fundo escuro e a fenda da mola de bronze la dentro; o canal do meio,
/// fundo; a numeracao de 1, 5, 10... em cima e embaixo e as letras dos dois
/// lados; e os dentes de cauda-de-andorinha numa ponta (os entalhes, do
/// outro lado, ficam por baixo e nao aparecem). A luz vem de cima, da
/// esquerda.
///
/// Tudo vai num desenho so, congelado, mostrado como uma imagem vetorial:
/// um elemento na tela em vez de dezenas de formas e textos (a placa de 830
/// tem mais de mil furos, de cinco camadas cada) -- o quadro da simulacao
/// nao sente. E o desenho nao muda nenhum furo de lugar: cada um sai do
/// pino do catalogo (Catalogo.MontarProtoboard), como antes.
/// </summary>
public partial class EditorCircuito
{
    static FrameworkElement DesenharProtoboard(PecaNoCircuito peca, PecaDef def)
    {
        var f = Catalogo.FormatoDe(peca.Tipo);
        double P = Catalogo.Passo, m = P, W = def.Largura, H = def.Altura;
        int colunas = f.Colunas;
        double Y(int linha) => m + linha * P;
        double X(int coluna) => m + coluna * P;

        var corpo = CorDaProtoboard(peca);
        bool escura = Escura(corpo);
        Color Sombra(double quanto) => Puxar(corpo, escura ? 1 - quanto * 1.4 : 1 - quanto);
        Color Luz(double quanto) => escura ? Puxar(corpo, 1 + quanto * 4) : Puxar(corpo, 1 + quanto);
        static SolidColorBrush Pincel(Color c) { var p = new SolidColorBrush(c); p.Freeze(); return p; }

        var desenho = new DrawingGroup();
        using (var dc = desenho.Open())
        {
            void Faixa(double x, double y, double largura, double altura, Brush tinta)
                => dc.DrawRectangle(tinta, null, new Rect(x, y, largura, altura));

            // ---- o corpo, com os encaixes ----
            //
            // A regua do meio vai da emenda de cima a de baixo (na mini de 170,
            // que nao tem trilho, a placa inteira e uma peca so).
            double emendaDeCima = f.TemTrilhos ? (Y(f.TrilhoTopo + 1) + Y(f.A)) / 2 : 0;
            double emendaDeBaixo = f.TemTrilhos ? (Y(f.J) + Y(f.TrilhoBase)) / 2 : H;

            // Os dentes de cauda-de-andorinha (estreitos na borda, largos na
            // ponta), saindo da esquerda (e, na mini, de cima): o dente de uma
            // placa entra no entalhe da outra. Os entalhes ficam por baixo do
            // plastico e nao se veem de cima.
            var dentes = new GeometryGroup();
            void Dente(double centro, bool vertical, double escala)
            {
                double colo = 1.0 * P * escala, ponta = 1.35 * P * escala, fundo = 0.42 * P * escala;
                Point Pt(double ao, double ac) => vertical ? new Point(centro + ac, ao) : new Point(ao, centro + ac);
                var figura = new PathFigure { StartPoint = Pt(0, -colo / 2), IsClosed = true };
                figura.Segments.Add(new PolyLineSegment([Pt(-fundo, -ponta / 2), Pt(-fundo, ponta / 2), Pt(0, colo / 2)], true));
                dentes.Children.Add(new PathGeometry([figura]));
            }
            foreach (double yc in new[] { (Y(f.A + 1) + Y(f.A + 2)) / 2, (Y(f.F + 2) + Y(f.F + 3)) / 2 })
                Dente(yc, false, 0.6);
            if (f.TemTrilhos)
                foreach (double yc in new[] { (Y(f.TrilhoTopo) + Y(f.TrilhoTopo + 1)) / 2, (Y(f.TrilhoBase) + Y(f.TrilhoBase + 1)) / 2 })
                    Dente(yc, false, 0.65);
            else
                foreach (double parte in new[] { 0.3, 0.7 })
                    Dente(W * parte, true, 1);

            Geometry contorno = new RectangleGeometry(new Rect(0, 0, W, H), 3, 3);
            contorno = new CombinedGeometry(GeometryCombineMode.Union, contorno, dentes);
            contorno.Freeze();
            var fundoDoCorpo = new LinearGradientBrush(Luz(0.012), Sombra(0.035), new Point(0, 0), new Point(0, 1));
            fundoDoCorpo.Freeze();
            var borda = new Pen(Pincel(Sombra(escura ? 0.3 : 0.24)), 0.8);
            borda.Freeze();
            dc.DrawGeometry(fundoDoCorpo, borda, contorno);

            // ---- as emendas das reguas de alimentacao ----
            if (f.TemTrilhos)
                foreach (double y in new[] { emendaDeCima, emendaDeBaixo })
                {
                    Faixa(0, y - 0.35, W, 0.7, Pincel(Sombra(0.26)));
                    Faixa(0, y + 0.4, W, 0.45, Pincel(Luz(0.04)));
                }

            // ---- o canal do meio: fundo, com a parede de cima na sombra ----
            double topoDoCanal = Y(f.E) + P / 2, alturaDoCanal = (f.F - f.E - 1) * P;
            var canal = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(0, 1),
                GradientStops =
                {
                    new GradientStop(Sombra(0.34), 0),
                    new GradientStop(Sombra(0.2), 0.22),
                    new GradientStop(Sombra(0.12), 0.75),
                    new GradientStop(Sombra(0.07), 1),
                },
            };
            canal.Freeze();
            Faixa(0, topoDoCanal, W, alturaDoCanal, canal);
            Faixa(0, topoDoCanal, W, 0.5, Pincel(Sombra(0.4)));
            Faixa(0, topoDoCanal + alturaDoCanal - 0.5, W, 0.5, Pincel(Luz(0.05)));

            // ---- as linhas dos trilhos ----
            var vermelho = Color.FromRgb(0xD2, 0x32, 0x2D);
            var azul = Color.FromRgb(0x23, 0x55, 0xC4);
            var doTrilho = Catalogo.ColunasDoTrilho(peca.Tipo).ToList();
            double sinalEsquerdo = 0, sinalDireito = 0;
            if (doTrilho.Count > 0)
            {
                sinalEsquerdo = (X(doTrilho[0]) - 0.24 * P) / 2;
                sinalDireito = (W + X(doTrilho[^1]) + 0.24 * P) / 2;
                foreach (var (linha, desloc, cor) in new[]
                {
                    (f.TrilhoTopo, -0.62, vermelho), (f.TrilhoTopo + 1, 0.62, azul),
                    (f.TrilhoBase, -0.62, vermelho), (f.TrilhoBase + 1, 0.62, azul),
                })
                    Faixa(sinalEsquerdo + 0.45 * P, Y(linha) + desloc * P - 0.47, sinalDireito - sinalEsquerdo - 0.9 * P, 0.95, Pincel(cor));
            }

            // ---- os furos: o funil chanfrado, o fundo e o contato de metal ----
            //
            // Um rebaixo com a luz vindo de cima, da esquerda: o anel do funil
            // quase da cor do plastico, as paredes de cima e da esquerda na
            // sombra, as de baixo e da direita com um fio de luz, o buraco escuro
            // e, la no fundo, a fenda da mola de bronze. Uma geometria por camada.
            var funis = new List<Rect>();
            var sombras = new List<Rect>();
            var luzes = new List<Rect>();
            var buracos = new List<Rect>();
            var molas = new List<Rect>();
            const double funil = 4.4, buraco = 3.0, parede = 0.55;
            foreach (var pino in def.Pinos)
            {
                double x = pino.X, y = pino.Y, a = x - funil / 2, b = y - funil / 2;
                funis.Add(new Rect(a, b, funil, funil));
                sombras.Add(new Rect(a + 0.25, b + 0.25, funil - 0.5, parede));
                sombras.Add(new Rect(a + 0.25, b + 0.25, parede, funil - 0.5));
                luzes.Add(new Rect(a + 0.7, y + funil / 2 - 0.25 - 0.4, funil - 0.95, 0.4));
                luzes.Add(new Rect(x + funil / 2 - 0.25 - 0.4, b + 0.7, 0.4, funil - 0.95));
                buracos.Add(new Rect(x - buraco / 2, y - buraco / 2, buraco, buraco));
                molas.Add(new Rect(x - 0.7, y + 0.55, 1.4, 0.32));
            }
            foreach (var (retangulos, raio, cor) in new (List<Rect>, double, Color)[]
            {
                (funis, 0.8, Sombra(escura ? 0.2 : 0.1)),
                (sombras, 0, Sombra(escura ? 0.4 : 0.27)),
                (luzes, 0, Luz(escura ? 0.06 : 0.02)),
                (buracos, 0.4, escura ? Color.FromRgb(0x0C, 0x0C, 0x0D) : Color.FromRgb(0x29, 0x29, 0x27)),
                (molas, 0, escura ? Color.FromRgb(0x4E, 0x49, 0x40) : Color.FromRgb(0x63, 0x5D, 0x50)),
            })
                dc.DrawGeometry(Pincel(cor), null, Retangulos(retangulos, raio));

            // ---- a serigrafia ----
            var tinta = escura ? Color.FromRgb(0xD8, 0xD7, 0xD0) : Color.FromRgb(0x5C, 0x5B, 0x56);
            var normal = new Typeface(new FontFamily("Arial"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
            var negrita = new Typeface(new FontFamily("Arial"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
            void Texto(string texto, double cx, double cy, double tamanho, Color cor, bool negrito = false)
            {
                var ft = new FormattedText(texto, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                                           negrito ? negrita : normal, tamanho, Pincel(cor), 1.0);
                dc.DrawText(ft, new Point(cx - ft.Width / 2, cy - ft.Height / 2));
            }

            // As letras das linhas, dos dois lados (no meio da margem, sem encostar no furo).
            const string letras = "ABCDEFGHIJ";
            for (int l = 0; l < 10; l++)
            {
                double y = Y(l < 5 ? f.A + l : f.F + (l - 5));
                Texto(letras[l].ToString(), m * 0.43, y, 3.4, tinta);
                Texto(letras[l].ToString(), W - m * 0.43, y, 3.4, tinta);
            }

            // Os numeros das colunas: 1, 5, 10, 15... em cima e embaixo, como na placa.
            double numerosEmCima = f.TemTrilhos ? (emendaDeCima + Y(f.A)) / 2 - 0.05 * P : Y(f.A) / 2;
            double numerosEmBaixo = f.TemTrilhos ? (Y(f.J) + emendaDeBaixo) / 2 + 0.05 * P : (Y(f.J) + H) / 2;
            for (int c = 0; c < colunas; c++)
            {
                int n = c + 1;
                if (n != 1 && n % 5 != 0) continue;
                Texto(n.ToString(CultureInfo.InvariantCulture), X(c), numerosEmCima, 3.2, tinta);
                Texto(n.ToString(CultureInfo.InvariantCulture), X(c), numerosEmBaixo, 3.2, tinta);
            }

            // Os sinais dos trilhos, nas duas pontas de cada um.
            if (doTrilho.Count > 0)
                foreach (var (linha, sinal, cor) in new[]
                {
                    (f.TrilhoTopo, "+", vermelho), (f.TrilhoTopo + 1, "−", azul),
                    (f.TrilhoBase, "+", vermelho), (f.TrilhoBase + 1, "−", azul),
                })
                {
                    Texto(sinal, sinalEsquerdo, Y(linha), 5.6, cor, negrito: true);
                    Texto(sinal, sinalDireito, Y(linha), 5.6, cor, negrito: true);
                }
        }
        desenho.Freeze();

        // Os dentes saem da placa (x e y negativos): a imagem vai onde o
        // desenho comeca, e a caixa continua do tamanho da peca.
        var limites = desenho.Bounds;
        var imagem = new Image { Source = new DrawingImage(desenho), Width = limites.Width, Height = limites.Height, Stretch = Stretch.Fill };
        imagem.Source.Freeze();
        var caixa = new Canvas { Width = W, Height = H };
        Canvas.SetLeft(imagem, limites.Left);
        Canvas.SetTop(imagem, limites.Top);
        caixa.Children.Add(imagem);
        return caixa;
    }

    /// <summary>
    /// Muitos retangulos (de cantos redondos, com raio) numa geometria so,
    /// congelada: milhares de RectangleGeometry num GeometryGroup pesam no
    /// quadro do WPF; o caminho unico, nao.
    /// </summary>
    internal static StreamGeometry Retangulos(IEnumerable<Rect> retangulos, double raio)
    {
        var geometria = new StreamGeometry { FillRule = FillRule.Nonzero };
        using (var c = geometria.Open())
        {
            var canto = new Size(raio, raio);
            foreach (var r in retangulos)
            {
                if (raio <= 0)
                {
                    c.BeginFigure(r.TopLeft, true, true);
                    c.PolyLineTo([r.TopRight, r.BottomRight, r.BottomLeft], false, false);
                    continue;
                }
                c.BeginFigure(new Point(r.Left + raio, r.Top), true, true);
                c.LineTo(new Point(r.Right - raio, r.Top), false, false);
                c.ArcTo(new Point(r.Right, r.Top + raio), canto, 0, false, SweepDirection.Clockwise, false, false);
                c.LineTo(new Point(r.Right, r.Bottom - raio), false, false);
                c.ArcTo(new Point(r.Right - raio, r.Bottom), canto, 0, false, SweepDirection.Clockwise, false, false);
                c.LineTo(new Point(r.Left + raio, r.Bottom), false, false);
                c.ArcTo(new Point(r.Left, r.Bottom - raio), canto, 0, false, SweepDirection.Clockwise, false, false);
                c.LineTo(new Point(r.Left, r.Top + raio), false, false);
                c.ArcTo(new Point(r.Left + raio, r.Top), canto, 0, false, SweepDirection.Clockwise, false, false);
            }
        }
        geometria.Freeze();
        return geometria;
    }
}
