using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Simulador.App;

/// <summary>
/// Os selos de aviso no circuito: os mesmos avisos vermelhos que aparecem embaixo
/// do codigo (a ligacao errada, a corrente demais, o nivel de tensao, o pull-up
/// que falta), mas em cima da peca de que eles falam -- varios ao mesmo tempo.
///
/// O aviso de um pino so (o 5 V na porta de 3,3 V) ganha a argola no pino e o selo
/// do lado; o da peca inteira, o selo no canto de cima dela. O amarelo e o aviso; o
/// vermelho, o que ja queimou. O motivo inteiro fica na dica do mouse, e o selo some
/// quando a ligacao e corrigida.
/// </summary>
public partial class EditorCircuito
{
    readonly Canvas marcasDeAviso = new();
    bool marcasDeAvisoNoMundo;
    List<Estouro> avisosNaTela = [];

    /// <summary>
    /// A chave de um aviso fechado no X: a peca, o pino e o texto do selo (o motivo
    /// inteiro muda com a corrente de cada quadro; o selo, nao). Os fechados ficam no
    /// circuito (Circuito.AvisosFechados) e vao no .sktch -- o projeto reaberto
    /// continua sem eles; "mostrar de novo" nas configuracoes traz de volta.
    /// </summary>
    static string ChaveDoAviso(Estouro aviso) => $"{aviso.Peca}|{aviso.Pino}|{TextoDoSelo(aviso)}";

    bool Fechado(Estouro aviso) => Atual.AvisosFechados?.Contains(ChaveDoAviso(aviso)) == true;

    /// <summary>Fecha (ignora) o aviso: o selo dele some, e o projeto guarda que ele foi fechado.</summary>
    public void FecharAviso(Estouro aviso)
    {
        if (aviso is null || Fechado(aviso)) return;
        (Atual.AvisosFechados ??= []).Add(ChaveDoAviso(aviso));
        avisosNaTela = [.. avisosNaTela.Where(a => ChaveDoAviso(a) != ChaveDoAviso(aviso))];
        DesenharAvisos();
        AvisoFechado?.Invoke(aviso);
    }

    /// <summary>Disparado ao fechar um aviso no X (a janela diz na barra como trazer de volta).</summary>
    public event Action<Estouro> AvisoFechado;

    /// <summary>Quantos avisos estao fechados neste circuito.</summary>
    public int AvisosFechados => Atual.AvisosFechados?.Count ?? 0;

    /// <summary>Traz de volta os avisos fechados deste circuito (o botao das configuracoes).</summary>
    public void EsquecerAvisosFechados()
    {
        if (AvisosFechados == 0) return;
        Atual.AvisosFechados = null;
        // Redesenha ja, com a ultima lista (parado, nao ha proximo quadro para isso).
        if (ultimosAvisos is { } ultimos) MostrarAvisos(ultimos);
    }

    /// <summary>A ultima lista recebida, antes de tirar os fechados (para redesenhar sem esperar).</summary>
    List<Estouro> ultimosAvisos;

    /// <summary>Para o teste: fecha o aviso desta peca (e pino) como o X faria.</summary>
    internal bool FecharAvisoParaTeste(string peca, string pino)
    {
        var aviso = avisosNaTela.FirstOrDefault(a => a.Peca == peca && a.Pino == pino);
        if (aviso is null) return false;
        FecharAviso(aviso);
        return true;
    }

    /// <summary>Os avisos de agora; os que sumiram da lista somem da tela.</summary>
    public void MostrarAvisos(IEnumerable<Estouro> avisos)
    {
        ultimosAvisos = [.. avisos];
        var novos = ultimosAvisos.Where(a => a is not null && Atual.Achar(a.Peca) is not null && !Fechado(a))
                                 .Distinct().ToList();
        // A conta roda a cada quadro, e a ordem pode mudar: redesenhar so quando o
        // CONJUNTO muda -- redesenhar o selo fecha a dica que o mouse abriu nele.
        if (novos.Count == avisosNaTela.Count && novos.ToHashSet().SetEquals(avisosNaTela)) return;
        avisosNaTela = novos;
        DesenharAvisos();
    }

    /// <summary>Para o teste: os selos na tela agora (peca, pino -- null no da peca inteira -- e o texto curto).</summary>
    internal List<(string Peca, string Pino, string Marca)> AvisosNaTelaParaTeste
        => [.. avisosNaTela.Select(a => (a.Peca, a.Pino, TextoDoSelo(a)))];

    /// <summary>
    /// O texto curto do selo: a marca do aviso, ou o comeco do motivo ate a primeira
    /// pausa ("LED direto no pino 14, sem resistor"), sem passar de umas 46 letras.
    /// </summary>
    internal static string TextoDoSelo(Estouro aviso)
    {
        if (!string.IsNullOrEmpty(aviso.Marca)) return aviso.Marca;
        string motivo = (aviso.Motivo ?? "").Trim();
        int corte = motivo.IndexOfAny([':', '.', ';', '—']);
        // "curto: alimentação ligada direto no GND": o primeiro pedaco sozinho diz pouco.
        if (corte is > 0 and < 12)
        {
            int segundo = motivo.IndexOfAny(['.', ';', '—', ':'], corte + 1);
            corte = segundo > 0 ? segundo : motivo.Length;
        }
        string curto = corte > 0 ? motivo[..corte] : motivo;
        if (curto.Length > 46) curto = curto[..45].TrimEnd(' ', ',') + "…";
        return curto.Length > 0 ? char.ToUpperInvariant(curto[0]) + curto[1..] : "Aviso";
    }

    void DesenharAvisos()
    {
        if (!marcasDeAvisoNoMundo)
        {
            mundo.Children.Add(marcasDeAviso);
            Panel.SetZIndex(marcasDeAviso, 99000);
            marcasDeAvisoNoMundo = true;
        }
        marcasDeAviso.Children.Clear();

        double s = 1 / Math.Max(Zoom, 0.2);
        var amarelo = Color.FromRgb(0xF1, 0xC4, 0x0F);
        var vermelho = Color.FromRgb(0xD9, 0x3B, 0x2B);
        var escuro = new SolidColorBrush(Color.FromRgb(0x1E, 0x1A, 0x08));
        var empilhados = new Dictionary<string, int>();      // quantos selos ja tem em cada peca (ou pino)
        int pinosDaPeca(string peca) => empilhados.Keys.Count(k => k.StartsWith(peca + ":", StringComparison.Ordinal));

        foreach (var aviso in avisosNaTela.OrderByDescending(a => a.Queimou))
        {
            var peca = Atual.Achar(aviso.Peca);
            var def = peca is null ? null : Catalogo.Obter(peca.Tipo);
            if (def is null) continue;

            var corDoAviso = aviso.Queimou ? vermelho : amarelo;
            var cor = new SolidColorBrush(corDoAviso);
            var letra = aviso.Queimou ? Brushes.White : escuro;
            Point ancora;          // de onde sai a linha
            Point canto;           // onde fica o selo
            string chave;
            if (aviso.Pino is not null && PosicaoPino(aviso.Peca, aviso.Pino) is { } onde)
            {
                double r = 6 * s;
                var argola = new Ellipse
                {
                    Width = 2 * r, Height = 2 * r, Stroke = cor, StrokeThickness = 2 * s,
                    Fill = new SolidColorBrush(Color.FromArgb(0x40, corDoAviso.R, corDoAviso.G, corDoAviso.B)),
                    IsHitTestVisible = false,
                };
                Canvas.SetLeft(argola, onde.X - r);
                Canvas.SetTop(argola, onde.Y - r);
                marcasDeAviso.Children.Add(argola);
                chave = aviso.Peca + ":" + aviso.Pino;
                // Os pinos vizinhos de uma placa: os selos sobem em escada para nao se cobrirem.
                int degrau = (empilhados.GetValueOrDefault(chave) + pinosDaPeca(aviso.Peca)) % 4;
                ancora = new Point(onde.X + r * 0.7, onde.Y - r * 0.7);
                canto = new Point(onde.X + 14 * s, onde.Y - (26 + 18 * degrau) * s);
            }
            else
            {
                // O da peca inteira: no canto de cima da direita da caixa dela, empilhados para cima.
                var caixa = CaixaNoMundo(peca, def);
                chave = aviso.Peca;
                int n = empilhados.GetValueOrDefault(chave);
                ancora = new Point(caixa.Right - 4 * s, caixa.Top + 4 * s);
                canto = new Point(caixa.Right - 8 * s, caixa.Top - (22 + 18 * n) * s);
            }
            empilhados[chave] = empilhados.GetValueOrDefault(chave) + 1;

            marcasDeAviso.Children.Add(new Line
            {
                X1 = ancora.X, Y1 = ancora.Y, X2 = canto.X, Y2 = canto.Y + 10 * s,
                Stroke = cor, StrokeThickness = 1.5 * s, IsHitTestVisible = false,
            });

            var selo = new Border
            {
                Background = cor,
                BorderBrush = escuro,
                BorderThickness = new Thickness(1 * s),
                CornerRadius = new CornerRadius(3 * s),
                // Espaco a direita para o X nao cobrir o texto.
                Padding = new Thickness(4 * s, 1 * s, 12 * s, 1 * s),
                Child = new TextBlock
                {
                    Text = (aviso.Queimou ? "✖ " : "⚠ ") + TextoDoSelo(aviso),
                    FontSize = 10.5 * s,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = letra,
                },
                // A dica com as cores do tema (a do sistema saia clara no claro) e a borda do aviso.
                ToolTip = new ToolTip
                {
                    Background = Tema.Pincel(Tema.Painel),
                    BorderBrush = cor,
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(8, 6, 8, 6),
                    Content = new TextBlock
                    {
                        Text = (aviso.Queimou ? "Queimou: " : "") + aviso.Motivo,
                        TextWrapping = TextWrapping.Wrap, MaxWidth = 380,
                        Foreground = Tema.Pincel(Tema.Texto), FontSize = 12,
                    },
                },
                Cursor = System.Windows.Input.Cursors.Help,
            };
            Canvas.SetLeft(selo, canto.X);
            Canvas.SetTop(selo, canto.Y);
            marcasDeAviso.Children.Add(selo);

            // O X no canto de cima da direita: fecha (ignora) este aviso.
            selo.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double d = 13 * s;
            var fechar = new Border
            {
                Width = d, Height = d,
                CornerRadius = new CornerRadius(d / 2),
                Background = new SolidColorBrush(Color.FromRgb(0x26, 0x27, 0x2B)),
                BorderBrush = cor,
                BorderThickness = new Thickness(1.2 * s),
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = new ToolTip
                {
                    Background = Tema.Pincel(Tema.Painel),
                    Content = new TextBlock { Text = "Fechar este aviso (ignorar)", Foreground = Tema.Pincel(Tema.Texto) },
                },
                Child = new TextBlock
                {
                    Text = "✕", FontSize = 8 * s, FontWeight = FontWeights.Bold, Foreground = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                },
            };
            var esse = aviso;
            // O clique fica aqui: nao vira clique no circuito (nem ponta de multimetro, nem selecao).
            fechar.MouseLeftButtonDown += (_, e) => { e.Handled = true; FecharAviso(esse); };
            fechar.MouseLeftButtonUp += (_, e) => e.Handled = true;
            Canvas.SetLeft(fechar, canto.X + selo.DesiredSize.Width - d * 0.65);
            Canvas.SetTop(fechar, canto.Y - d * 0.4);
            marcasDeAviso.Children.Add(fechar);
        }
    }
}
