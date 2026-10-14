using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Rendering;

namespace Simulador.App;

/// <summary>
/// A faixa à esquerda dos números de linha, onde se marca o ponto de
/// parada com um clique -- o mesmo lugar da Arduino IDE 2 e do VS Code,
/// para quem já viu um não precisar procurar.
///
/// Desenha a bolinha vermelha das linhas marcadas e a seta amarela da
/// linha em que o sketch está parado. Quem guarda as linhas é a janela;
/// a margem só pergunta.
/// </summary>
sealed class MargemDePausas(Func<int, bool> marcada) : AbstractMargin
{
    static readonly Brush Bolinha = Congelado(Color.FromRgb(0xE5, 0x39, 0x35));
    static readonly Brush Seta = Congelado(Color.FromRgb(0xF1, 0xC4, 0x0F));

    public event Action<int> Clicou;

    /// <summary>Linha parada no campo, ou -1.</summary>
    public int LinhaPausada { get; set; } = -1;

    protected override Size MeasureOverride(Size disponivel) => new(16, 0);

    protected override void OnTextViewChanged(TextView antigo, TextView novo)
    {
        if (antigo is not null) antigo.VisualLinesChanged -= Redesenhar;
        base.OnTextViewChanged(antigo, novo);
        if (novo is not null) novo.VisualLinesChanged += Redesenhar;
        InvalidateVisual();
    }

    void Redesenhar(object s, EventArgs e) => InvalidateVisual();

    protected override void OnRender(DrawingContext dc)
    {
        // Fundo transparente e não nulo: sem ele, o clique entre duas
        // bolinhas atravessaria a margem e não marcaria nada.
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));

        var tv = TextView;
        if (tv is null || !tv.VisualLinesValid) return;

        foreach (var vl in tv.VisualLines)
        {
            int linha = vl.FirstDocumentLine.LineNumber;
            double meio = vl.VisualTop - tv.VerticalOffset + vl.Height / 2;

            if (marcada(linha))
                dc.DrawEllipse(Bolinha, null, new Point(8, meio), 5, 5);

            if (linha == LinhaPausada)
            {
                var seta = new StreamGeometry();
                using (var g = seta.Open())
                {
                    g.BeginFigure(new Point(3, meio - 5), true, true);
                    g.LineTo(new Point(13, meio), true, false);
                    g.LineTo(new Point(3, meio + 5), true, false);
                }
                seta.Freeze();
                dc.DrawGeometry(Seta, new Pen(Brushes.Black, 0.8), seta);
            }
        }
    }

    protected override HitTestResult HitTestCore(PointHitTestParameters p)
        => new PointHitTestResult(this, p.HitPoint);

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        var tv = TextView;
        if (tv is null) return;

        var vl = tv.GetVisualLineFromVisualTop(e.GetPosition(this).Y + tv.VerticalOffset);
        if (vl is null) return;

        Clicou?.Invoke(vl.FirstDocumentLine.LineNumber);
        e.Handled = true;
    }

    internal static Brush Congelado(Color cor)
    {
        var pincel = new SolidColorBrush(cor);
        pincel.Freeze();
        return pincel;
    }
}

/// <summary>
/// O fundo das linhas: amarelo na que está parada, um vermelho bem
/// apagado nas marcadas. A bolinha sozinha, 16 pixels à esquerda, some
/// quando o olho está no meio do código.
/// </summary>
sealed class FundoDaPausa(Func<int, bool> marcada) : IBackgroundRenderer
{
    static readonly Brush Parada = MargemDePausas.Congelado(Color.FromArgb(0x55, 0xF1, 0xC4, 0x0F));
    static readonly Brush Marcada = MargemDePausas.Congelado(Color.FromArgb(0x30, 0xE5, 0x39, 0x35));

    public int LinhaPausada { get; set; } = -1;

    public KnownLayer Layer => KnownLayer.Background;

    public void Draw(TextView tv, DrawingContext dc)
    {
        foreach (var vl in tv.VisualLines)
        {
            int linha = vl.FirstDocumentLine.LineNumber;
            Brush fundo = linha == LinhaPausada ? Parada : marcada(linha) ? Marcada : null;
            if (fundo is null) continue;

            // As coordenadas aqui são as da parte visível: a faixa vai de
            // ponta a ponta do que aparece, role o texto para onde rolar.
            dc.DrawRectangle(fundo, null, new Rect(
                0, vl.VisualTop - tv.VerticalOffset, tv.ActualWidth, vl.Height));
        }
    }
}
