using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Simulador.App;

/// <summary>
/// O frame desenhado pelo contorno de corte dele (Frames.cs), no material
/// escolhido: o acrilico (a chapa colorida e translucida, a borda cortada a
/// laser mais forte e o brilho da face), o MDF e o compensado (a madeira com
/// a borda queimada do laser) e a fibra de carbono (a trama 2x2 da sarja).
/// Os furos sao furos: o que esta embaixo aparece por eles.
///
/// Num desenho so, congelado, em milimetros (a escala de 96/25,4 por cima).
/// </summary>
public partial class EditorCircuito
{
    FrameworkElement DesenharFrame(PecaNoCircuito peca, PecaDef def)
    {
        double W = def.Largura, H = def.Altura;
        var frame = Frames.Obter(peca.Tipo);
        var material = Frames.MaterialDe(peca);

        // A base importada "como no SVG": o desenho do arquivo, sem material.
        if (frame is null || material.Acabamento == Frames.Acabamento.Original)
            return new Image { Source = Pecas.Obter(peca.Tipo), Width = W, Height = H, Stretch = Stretch.Fill };

        static SolidColorBrush Pincel(Color c) { var p = new SolidColorBrush(c); p.Freeze(); return p; }
        static Pen Caneta(Brush b, double grossura)
        {
            var p = new Pen(b, grossura) { LineJoin = PenLineJoin.Round };
            p.Freeze();
            return p;
        }
        static Color ComAlfa(Color c, double a) => Color.FromArgb((byte)Math.Round(Math.Clamp(a, 0, 1) * 255), c.R, c.G, c.B);

        var contorno = frame.Contorno;
        var cor = material.Cor;
        if (peca.Virada) cor = Puxar(cor, 0.85);

        var desenho = new DrawingGroup();
        using (var dc = desenho.Open())
        {
            // A caixa inteira, transparente: a imagem fica do tamanho da peca.
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, W, H));
            dc.PushTransform(new ScaleTransform(Frames.A, Frames.A));

            switch (material.Acabamento)
            {
                case Frames.Acabamento.Acrilico:
                {
                    // A chapa: a cor translucida, e o brilho da face polida por cima.
                    dc.DrawGeometry(Pincel(ComAlfa(cor, material.Opacidade)), null, contorno);
                    var brilho = new LinearGradientBrush(
                        [
                            new GradientStop(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF), 0),
                            new GradientStop(Color.FromArgb(0x0C, 0xFF, 0xFF, 0xFF), 0.45),
                            new GradientStop(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 0.55),
                            new GradientStop(Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF), 1),
                        ], new Point(0, 0), new Point(1, 1));
                    brilho.Freeze();
                    dc.DrawGeometry(brilho, null, contorno);

                    // A borda cortada: a luz que corre por dentro da chapa
                    // sai pela borda, que fica mais forte que a face.
                    bool escuro = cor.R + cor.G + cor.B < 120;
                    var borda = escuro ? Color.FromRgb(0x4A, 0x4C, 0x52) : Puxar(cor, 0.78);
                    dc.DrawGeometry(null, Caneta(Pincel(ComAlfa(borda, Math.Max(0.75, material.Opacidade + 0.3))), 0.55), contorno);
                    dc.PushTransform(new TranslateTransform(0.22, 0.22));
                    dc.DrawGeometry(null, Caneta(Pincel(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF)), 0.18), contorno);
                    dc.Pop();
                    break;
                }

                case Frames.Acabamento.Madeira:
                {
                    // A face lixada (um pouco mais clara no meio) e a borda
                    // queimada que o laser deixa em toda madeira cortada.
                    var face = new LinearGradientBrush(Puxar(cor, 1.04), Puxar(cor, 0.94), new Point(0, 0), new Point(1, 1));
                    face.Freeze();
                    dc.DrawGeometry(face, null, contorno);
                    bool compensado = material.Id == "compensado";
                    var queimado = compensado ? Color.FromRgb(0x5C, 0x3B, 0x1E) : Color.FromRgb(0x3D, 0x26, 0x14);
                    dc.DrawGeometry(null, Caneta(Pincel(queimado), 0.45), contorno);
                    break;
                }

                case Frames.Acabamento.Carbono:
                {
                    // A sarja 2x2: os cabos de fibra cruzados em degraus, cada
                    // um com o brilho no meio, e o verniz por cima.
                    dc.DrawGeometry(Trama(cor), null, contorno);
                    var verniz = new LinearGradientBrush(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF), Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), new Point(0, 0), new Point(0.6, 0.6));
                    verniz.Freeze();
                    dc.DrawGeometry(verniz, null, contorno);
                    // A borda fresada mostra a fibra crua, fosca e mais clara que a face.
                    dc.DrawGeometry(null, Caneta(Pincel(Color.FromRgb(0x5E, 0x61, 0x67)), 0.5), contorno);
                    break;
                }
            }
            dc.Pop();
        }
        desenho.Freeze();

        var imagem = new Image { Source = new DrawingImage(desenho), Width = W, Height = H, Stretch = Stretch.Fill };
        imagem.Source.Freeze();
        var caixa = new Canvas { Width = W, Height = H };
        caixa.Children.Add(imagem);
        return caixa;
    }

    /// <summary>A trama da fibra de carbono, em mm (um cabo de 1 mm, o desenho repete a cada 4).</summary>
    static Brush Trama(Color cor)
    {
        var celulas = new DrawingGroup();
        using (var dc = celulas.Open())
        {
            for (int x = 0; x < 4; x++)
                for (int y = 0; y < 4; y++)
                {
                    bool deitado = ((x + y) & 3) < 2;
                    var cabo = deitado
                        ? new LinearGradientBrush([new(Puxar(cor, 0.9), 0), new(Puxar(cor, 1.75), 0.5), new(Puxar(cor, 0.9), 1)], new Point(0, 0), new Point(0, 1))
                        : new LinearGradientBrush([new(Puxar(cor, 0.75), 0), new(Puxar(cor, 1.3), 0.5), new(Puxar(cor, 0.75), 1)], new Point(0, 0), new Point(1, 0));
                    cabo.Freeze();
                    dc.DrawRectangle(cabo, null, new Rect(x, y, 1, 1));
                }
        }
        celulas.Freeze();
        var trama = new DrawingBrush(celulas)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, 4, 4),
            ViewportUnits = BrushMappingMode.Absolute,
            Stretch = Stretch.Fill,
        };
        trama.Freeze();
        return trama;
    }
}
