using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Simulador.App;

/// <summary>
/// A PCB perfurada desenhada como a de verdade (Alpha 25): a placa ilhada
/// dupla face de fibra (FR-4), a de 5 x 7 cm da bancada, e a de fenolite.
///
/// Na de fibra (verde, azul, preta, vermelha ou roxa -- a cor e a da mascara
/// de solda): cada ilha estanhada (HASL, prata com o brilho do estanho), a
/// abertura da mascara em volta dela, o furo de 1 mm com a parede
/// metalizada, a mascara brilhante e a borda da fibra cortada; ilhas nos
/// dois lados. A de fenolite e de face simples: de cima, so o papelao
/// marrom com os furos estampados; virada, as ilhas de cobre. A numeracao,
/// 1, 5, 10..., nos quatro lados.
///
/// Num desenho so, congelado (como a protoboard), e sem mudar nenhum furo de
/// lugar: cada um sai do pino do catalogo (Catalogo.MontarPcb).
/// </summary>
public partial class EditorCircuito
{
    FrameworkElement DesenharPcb(PecaNoCircuito peca, PecaDef def)
    {
        var (colunas, linhas) = Catalogo.TamanhoDaPcb(peca.Tipo)!.Value;
        double P = Catalogo.Passo, m = P, W = def.Largura, H = def.Altura;
        string hex = CoresDePcb.FirstOrDefault(c => c.id == peca.Cor).hex ?? CoresDePcb[0].hex;
        var mascara = (Color)ColorConverter.ConvertFromString(hex);
        bool fenolite = peca.Cor == "fenolite";
        // Virada: o lado de baixo, um tom mais escuro (a deixa de que e o outro lado).
        if (peca.Virada) mascara = Puxar(mascara, 0.82);
        static SolidColorBrush Pincel(Color c) { var p = new SolidColorBrush(c); p.Freeze(); return p; }
        static Pen Caneta(Color c, double grossura) { var p = new Pen(Pincel(c), grossura); p.Freeze(); return p; }

        var desenho = new DrawingGroup();
        using (var dc = desenho.Open())
        {
            // ---- a placa: a mascara brilhante e a borda da fibra cortada ----
            var brilho = new LinearGradientBrush(Puxar(mascara, 1.08), Puxar(mascara, 0.93), new Point(0, 0), new Point(1, 1));
            brilho.Freeze();
            var bordaDaFibra = fenolite ? Puxar(mascara, 0.72) : Color.FromRgb(0xA9, 0xB3, 0x82);
            dc.DrawRoundedRectangle(brilho, Caneta(bordaDaFibra, 0.9), new Rect(0, 0, W, H), 1.2, 1.2);

            var furos = def.Pinos.Select(p => new Point(p.X, p.Y)).ToList();
            bool comIlhas = !fenolite || peca.Virada;
            if (comIlhas)
            {
                // ---- as ilhas ----
                var aberturas = Circulos(furos, 3.95, 0, 0);
                var ilhas = Circulos(furos, 3.5, 0, 0);
                var brilhos = Circulos(furos, 1.15, -1.45, -1.45);
                (Color abertura, Color metal, Color aro, Color luz) = fenolite
                    // o cobre nu da fenolite, por baixo
                    ? (Puxar(mascara, 0.88), Color.FromRgb(0xC2, 0x6E, 0x34), Color.FromRgb(0x86, 0x45, 0x1C), Color.FromRgb(0xD9, 0x8C, 0x55))
                    // o estanho (HASL) da fibra, nos dois lados
                    : (Puxar(mascara, 1.22), Color.FromRgb(0xB4, 0xB9, 0xC0), Color.FromRgb(0x7E, 0x85, 0x8E), Color.FromRgb(0xD2, 0xD6, 0xDB));
                if (!fenolite) dc.DrawGeometry(Pincel(abertura), null, aberturas);
                dc.DrawGeometry(Pincel(metal), Caneta(aro, 0.35), ilhas);
                dc.DrawGeometry(Pincel(luz), null, brilhos);
                // a parede metalizada do furo, e o furo
                dc.DrawGeometry(Pincel(fenolite ? Color.FromRgb(0x6A, 0x36, 0x14) : Color.FromRgb(0x63, 0x69, 0x70)), null, Circulos(furos, 1.9, 0, 0));
            }
            else
            {
                // a fenolite de cima: o furo estampado, com a borda amassada
                dc.DrawGeometry(Pincel(Puxar(mascara, 0.8)), null, Circulos(furos, 2.25, 0, 0));
            }
            dc.DrawGeometry(Pincel(Color.FromRgb(0x14, 0x13, 0x12)), null, Circulos(furos, 1.62, 0.1, 0.1));

            // ---- as soldas das pernas (so do lado de baixo) ----
            var soldas = SoldasDaPcb(peca, def);
            if (soldas.Children.Count > 0)
            {
                var estanho = new RadialGradientBrush(Colors.White, Color.FromRgb(0x8E, 0x94, 0x9C))
                {
                    GradientOrigin = new Point(0.35, 0.3), Center = new Point(0.45, 0.4), RadiusX = 0.7, RadiusY = 0.7,
                };
                estanho.Freeze();
                foreach (var solda in soldas.Children)
                    dc.DrawGeometry(estanho, Caneta(Color.FromRgb(0x3E, 0x43, 0x4A), 0.7), solda);
            }

            // ---- a serigrafia: 1, 5, 10... nos quatro lados ----
            var tinta = fenolite ? Color.FromRgb(0x5A, 0x34, 0x12) : Color.FromRgb(0xEE, 0xF0, 0xEE);
            var letra = new Typeface(new FontFamily("Arial"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
            void Texto(string texto, double cx, double cy)
            {
                var ft = new FormattedText(texto, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, letra, 3.0, Pincel(tinta), 1.0);
                // Com a placa espelhada (virada), cada numero e desespelhado no
                // lugar: a serigrafia continua legivel.
                if (peca.Espelhada) dc.PushTransform(new ScaleTransform(-1, 1, cx, cy));
                dc.DrawText(ft, new Point(cx - ft.Width / 2, cy - ft.Height / 2));
                if (peca.Espelhada) dc.Pop();
            }
            static bool Marcado(int n) => n == 1 || n % 5 == 0;
            for (int c = 0; c < colunas; c++)
                if (Marcado(c + 1))
                {
                    string n = (c + 1).ToString(CultureInfo.InvariantCulture);
                    Texto(n, m + c * P, m * 0.42);
                    Texto(n, m + c * P, H - m * 0.42);
                }
            for (int l = 0; l < linhas; l++)
                if (Marcado(l + 1))
                {
                    string n = (l + 1).ToString(CultureInfo.InvariantCulture);
                    Texto(n, m * 0.42, m + l * P);
                    Texto(n, W - m * 0.42, m + l * P);
                }
        }
        desenho.Freeze();

        var imagem = new Image { Source = new DrawingImage(desenho), Width = desenho.Bounds.Width, Height = desenho.Bounds.Height, Stretch = Stretch.Fill };
        imagem.Source.Freeze();
        var caixa = new Canvas { Width = W, Height = H };
        Canvas.SetLeft(imagem, desenho.Bounds.Left);
        Canvas.SetTop(imagem, desenho.Bounds.Top);
        caixa.Children.Add(imagem);
        return caixa;
    }

    /// <summary>Muitos circulos numa geometria so, congelada (o centro deslocado de dx, dy).</summary>
    internal static StreamGeometry Circulos(IEnumerable<Point> centros, double raio, double dx, double dy)
    {
        var geometria = new StreamGeometry { FillRule = FillRule.Nonzero };
        using (var c = geometria.Open())
        {
            var tamanho = new Size(raio, raio);
            foreach (var p in centros)
            {
                double x = p.X + dx, y = p.Y + dy;
                c.BeginFigure(new Point(x - raio, y), true, true);
                c.ArcTo(new Point(x + raio, y), tamanho, 0, false, SweepDirection.Clockwise, false, false);
                c.ArcTo(new Point(x - raio, y), tamanho, 0, false, SweepDirection.Clockwise, false, false);
            }
        }
        geometria.Freeze();
        return geometria;
    }
}
