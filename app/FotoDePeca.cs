using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Simulador.App;

/// <summary>
/// A foto de conferencia de uma peca sozinha, para o redesenho dos
/// componentes (Alpha 25): o mesmo desenho do circuito, num fundo neutro,
/// no zoom pedido -- e, com o recorte, so um pedaco dela de perto.
///
///   CircuitSketch.exe --desenho &lt;tipo&gt; &lt;arquivo.png&gt; [--zoom N] [--cor id]
///                     [--recorte x,y,largura,altura]   (em pixels da peca)
///                     [--furos]                         (marca o centro de cada pino)
///                     [--virada]                        (a PCB pelo lado de baixo)
///                     [--braco id]                      (o braco do servo: nenhum, simples, duplo...)
///                     [--modelo id] [--bits n]          (a pilha do suporte; os slots vazios, em bits)
///                     [--svg arquivo.svg]               (um SVG de fora no lugar do da peca, para testar)
///
/// O --furos desenha um ponto vermelho no lugar de cada pino do catalogo:
/// e o que mostra que o desenho novo nao tirou nenhum furo do lugar.
/// </summary>
internal static class FotoDePeca
{
    public static int Gravar(string tipo, string cor, string braco, double zoom, Rect? recorte, bool furos, bool virada, string png,
                             string modelo = null, int bits = 0, string svg = null)
    {
        var def = Catalogo.Obter(tipo);
        if (def is null) return 2;
        if (svg is not null) Pecas.DefinirDesenho(tipo, File.ReadAllText(svg));

        var peca = new PecaNoCircuito { Id = "foto", Tipo = tipo, Cor = cor, Virada = virada, Braco = braco, Modelo = modelo, Bits = bits };
        var editor = new EditorCircuito();
        var desenho = editor.DesenhoDa(peca, def);

        var tela = new Canvas { Width = def.Largura, Height = def.Altura };
        tela.Children.Add(desenho);
        // O braco do servo, que o editor desenha por cima, num angulo de exemplo.
        if (EditorCircuito.BracoParaFoto(peca, 125) is { } doServo) tela.Children.Add(doServo);
        if (furos)
            foreach (var pino in def.Pinos)
            {
                var ponto = new System.Windows.Shapes.Ellipse { Width = 1.2, Height = 1.2, Fill = Brushes.Red };
                Canvas.SetLeft(ponto, pino.X - 0.6);
                Canvas.SetTop(ponto, pino.Y - 0.6);
                tela.Children.Add(ponto);
            }

        // A arte que passa da caixa (a sobra do SVG) tambem entra na foto.
        double margem = 16 + Math.Max(0, Math.Max(-desenho.Margin.Left, -desenho.Margin.Top));
        var moldura = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x26, 0x27, 0x2B)),
            Padding = new Thickness(margem),
            Child = tela,
            LayoutTransform = new ScaleTransform(zoom, zoom),
        };
        moldura.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        moldura.Arrange(new Rect(moldura.DesiredSize));
        moldura.UpdateLayout();

        int largura = (int)Math.Ceiling(moldura.DesiredSize.Width);
        int altura = (int)Math.Ceiling(moldura.DesiredSize.Height);
        var foto = new RenderTargetBitmap(largura, altura, 96, 96, PixelFormats.Pbgra32);
        foto.Render(moldura);

        BitmapSource saida = foto;
        if (recorte is { } r)
        {
            var area = new Int32Rect(
                (int)Math.Clamp((r.X + margem) * zoom, 0, largura - 1),
                (int)Math.Clamp((r.Y + margem) * zoom, 0, altura - 1), 0, 0);
            area.Width = (int)Math.Clamp(r.Width * zoom, 1, largura - area.X);
            area.Height = (int)Math.Clamp(r.Height * zoom, 1, altura - area.Y);
            saida = new CroppedBitmap(foto, area);
        }

        var codificador = new PngBitmapEncoder();
        codificador.Frames.Add(BitmapFrame.Create(saida));
        using var arquivo = File.Create(png);
        codificador.Save(arquivo);
        return 0;
    }
}
