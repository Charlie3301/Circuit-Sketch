using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace Simulador.App;

/// <summary>
/// Tira um painel do lugar dele e devolve depois.
///
/// A parte delicada de soltar um painel nao e a janela: e o RETORNO. No
/// WPF um elemento so pode ter um pai, e quem devolve sem tirar do pai
/// atual leva um "o elemento ja e filho logico de outro elemento" --
/// que foi exatamente o que aconteceu na Alpha 17. Fechar a janela
/// solta zerava o Content da janela, mas o Border que embrulhava o
/// painel continuava segurando ele.
///
/// Por isso isto fica separado da <see cref="JanelaSolta"/>: da para
/// exercitar o vai-e-volta inteiro sem mostrar janela nenhuma, que e o
/// que o autoteste faz.
/// </summary>
sealed class PainelSolto
{
    readonly Panel dono;
    readonly Decorator donoUnico;
    readonly int posicao;

    public UIElement Painel { get; private set; }

    /// <summary>Conseguiu tirar do lugar, e sabe como devolver.</summary>
    public bool Valido => Painel is not null;

    public PainelSolto(UIElement alvo)
    {
        // Sao dois casos: o painel esta dentro de um Panel (Grid,
        // StackPanel) ou dentro de um Border, que so tem um filho.
        // Guardar o INDICE importa no Grid: devolver o painel no fim da
        // lista mudaria a ordem de desenho.
        switch (PaiDe(alvo))
        {
            case Panel p:
                dono = p;
                posicao = p.Children.IndexOf(alvo);
                break;

            case Decorator d:
                donoUnico = d;
                break;

            // Sem pai conhecido nao da para devolver, e um painel que
            // nao volta e pior do que um painel que nao solta.
            default: return;
        }

        TirarDoPai(alvo);
        Painel = alvo;
    }

    public void Devolver()
    {
        if (Painel is null) return;

        var volta = Painel;
        Painel = null;

        // ANTES de reinserir. O painel esteve dentro do Border da janela
        // solta, e sem este passo o WPF recusa o novo pai.
        TirarDoPai(volta);

        if (dono is not null) dono.Children.Insert(Math.Min(posicao, dono.Children.Count), volta);
        else donoUnico.Child = volta;
    }

    /// <summary>
    /// O pai do elemento, seja qual for a arvore.
    ///
    /// A arvore visual so existe depois da primeira renderizacao; a
    /// logica existe desde que o XAML foi lido. Tentar as duas faz isto
    /// funcionar tambem antes de a janela aparecer -- e e assim que o
    /// autoteste consegue rodar.
    /// </summary>
    static DependencyObject PaiDe(UIElement alvo)
        => System.Windows.Media.VisualTreeHelper.GetParent(alvo)
           ?? LogicalTreeHelper.GetParent(alvo);

    /// <summary>Desliga o elemento de quem o estiver segurando.</summary>
    internal static void TirarDoPai(UIElement alvo)
    {
        switch (PaiDe(alvo))
        {
            case Panel p: p.Children.Remove(alvo); break;
            case Decorator d: d.Child = null; break;
            case ContentControl cc when ReferenceEquals(cc.Content, alvo): cc.Content = null; break;
            case ContentPresenter cp when ReferenceEquals(cp.Content, alvo): cp.Content = null; break;
        }
    }
}

/// <summary>
/// Um painel do programa numa janela propria.
///
/// Item 6.1 da lista do professor: "uma divisoria ao clicar em abas de
/// janelas que seja possivel criar janelas diferentes com o codigo,
/// .ino ou .json, visao de circuito, componentes, monitor serial, rede".
///
/// O painel nao e copiado nem redesenhado: ele MUDA DE LUGAR. Sai da
/// janela principal, entra aqui, e volta para o mesmo buraco quando esta
/// janela fecha. Por isso o monitor serial solto continua recebendo as
/// linhas e o editor solto continua sendo o mesmo editor -- nao ha dois
/// de nada, nem estado para sincronizar.
/// </summary>
public class JanelaSolta : Window
{
    readonly string nome;

    /// <summary>A janela principal, para saber quando o arrasto passa por cima dela.</summary>
    readonly Window principal;

    /// <summary>A faixa de "solte para reacoplar", escondida enquanto nao serve.</summary>
    Border avisoDeEncaixe;

    /// <summary>
    /// O que esta solto agora, por nome.
    ///
    /// Guarda o PAINEL, e nao a janela: no autoteste nao ha janela
    /// nenhuma -- criar uma prenderia o processo, que so encerra quando
    /// a ultima some --, e o vai-e-volta precisa funcionar do mesmo
    /// jeito nos dois casos.
    /// </summary>
    static readonly Dictionary<string, (PainelSolto Painel, Border Moldura, JanelaSolta Janela)>
        abertos = [];

    public static bool EstaSolto(string nome) => abertos.ContainsKey(nome);

    /// <summary>
    /// Avisa que um painel saiu ou voltou. Quem cuida do layout escuta.
    ///
    /// Precisa ser um aviso, e nao o retorno de Alternar: fechar a
    /// janela solta pelo X da barra devolve o painel sem passar por
    /// botao nenhum, e sem o aviso a janela principal ficaria com o
    /// buraco aberto de um painel que ja voltou.
    /// </summary>
    public static Action<string> AoMudar { get; set; }

    /// <summary>Traz de volta tudo o que estiver solto. Chamado ao fechar o programa.</summary>
    public static void FecharTodas()
    {
        foreach (string nome in abertos.Keys.ToList()) Devolver(nome);
    }

    /// <summary>Solta o painel numa janela nova, ou traz de volta o que ja esta solto.</summary>
    public static void Alternar(Window principal, string nome, string titulo, UIElement painel)
    {
        if (abertos.ContainsKey(nome)) { Devolver(nome); return; }
        if (painel is null) return;

        var solto = new PainelSolto(painel);
        if (!solto.Valido) return;

        // A moldura existe SEMPRE, inclusive no autoteste.
        //
        // Era aqui que o teste da Alpha 17 passava por fora do bug: sem
        // janela, o painel nunca entrava num Border, e o retorno nunca
        // esbarrava no pai que ninguem tinha desfeito.
        var moldura = new Border
        {
            Margin = new Thickness(10),
            Child = solto.Painel,
        };

        JanelaSolta janela = null;
        if (!Autoteste.Rodando)
        {
            janela = new JanelaSolta(principal, nome, titulo, moldura);
            janela.Show();
        }

        abertos[nome] = (solto, moldura, janela);
        AoMudar?.Invoke(nome);
    }

    static void Devolver(string nome)
    {
        if (!abertos.Remove(nome, out var quem)) return;

        // A ordem importa: primeiro a moldura larga o painel, depois a
        // janela fecha, e so entao o painel volta para o lugar. Fechar
        // com o painel dentro o levaria junto.
        quem.Moldura.Child = null;

        if (quem.Janela is not null)
        {
            quem.Janela.Content = null;
            quem.Janela.Close();
        }

        quem.Painel.Devolver();
        AoMudar?.Invoke(nome);
    }

    JanelaSolta(Window principal, string nome, string titulo, Border moldura)
    {
        this.nome = nome;
        this.principal = principal;

        Title = "CircuitSketch — " + titulo + "  (arraste para cima do programa para reacoplar)";
        Width = 720;
        Height = 480;
        MinWidth = 320;
        MinHeight = 200;
        Background = Tema.Pincel(Tema.Fundo);
        ShowInTaskbar = true;

        // O dono so pode ser definido depois que a janela principal
        // apareceu na tela.
        try { Owner = principal; } catch (InvalidOperationException) { }

        // A faixa fica POR CIMA do painel, e nao empurrando ele: um
        // aviso que muda a altura do conteudo faria o texto pular no
        // meio do arrasto.
        avisoDeEncaixe = new Border
        {
            Visibility = Visibility.Collapsed,
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(10, 10, 10, 0),
            Padding = new Thickness(10, 6, 10, 6),
            CornerRadius = new CornerRadius(4),
            Background = new SolidColorBrush(Color.FromRgb(0x00, 0x60, 0x64)),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x00, 0x97, 0x9D)),
            Child = new TextBlock
            {
                Text = "solte aqui para reacoplar",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xF4, 0xF5)),
                HorizontalAlignment = HorizontalAlignment.Center,
            },
        };

        Content = new Grid { Children = { moldura, avisoDeEncaixe } };

        // Fechar pela barra da janela devolve o painel do mesmo jeito
        // que o botao da seta. O Devolver cuida de tirar o painel da
        // moldura antes de reinseri-lo.
        Closed += (_, _) => Devolver(this.nome);
    }

    // ================= arrastar de volta =================
    //
    // Alpha 18: "adicionar funcionalidade de ARRASTAR, assim, quando eu
    // arrastar uma janela, posso reacoplar ela sem precisar fechar".
    //
    // Fechar para devolver funciona, mas e um jeito torto de dizer
    // "quero isto de volta no lugar" -- e nao ha como saber que fechar
    // devolve em vez de descartar. Arrastar por cima da janela principal
    // e soltar diz a mesma coisa sem precisar de instrucao.

    /// <summary>A janela esta sendo movida pelo usuario.</summary>
    const int MovendoAJanela = 0x0216;

    /// <summary>O usuario soltou o botao e o arrasto acabou.</summary>
    const int AcabouDeMover = 0x0232;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        (PresentationSource.FromVisual(this) as HwndSource)?.AddHook(Vigiar);
    }

    /// <summary>
    /// Acompanha o arrasto pelas mensagens do Windows.
    ///
    /// Enquanto a janela e arrastada o WPF nao entrega eventos de mouse
    /// -- quem esta no comando e o laco de movimento do proprio Windows.
    /// Por isso as duas mensagens: uma avisa que esta se movendo, e a
    /// outra, que e a que importa, avisa o instante exato em que o botao
    /// foi solto.
    /// </summary>
    IntPtr Vigiar(IntPtr janela, int mensagem, IntPtr w, IntPtr l, ref bool tratada)
    {
        if (mensagem == MovendoAJanela)
        {
            avisoDeEncaixe.Visibility = SobreAPrincipal()
                ? Visibility.Visible : Visibility.Collapsed;
        }
        else if (mensagem == AcabouDeMover)
        {
            avisoDeEncaixe.Visibility = Visibility.Collapsed;
            if (SobreAPrincipal()) Dispatcher.BeginInvoke(new Action(() => Devolver(nome)));
        }

        return IntPtr.Zero;
    }

    /// <summary>O ponteiro esta por cima da janela principal?</summary>
    bool SobreAPrincipal()
    {
        if (principal is null || !principal.IsVisible) return false;
        if (!GetCursorPos(out var onde)) return false;

        try
        {
            // PointToScreen ja resolve o DPI: os dois cantos saem em
            // pixels de verdade, do mesmo jeito que o GetCursorPos.
            var canto = principal.PointToScreen(new Point(0, 0));
            var fim = principal.PointToScreen(
                new Point(principal.ActualWidth, principal.ActualHeight));

            return Dentro(canto, fim, new Point(onde.X, onde.Y));
        }
        catch (InvalidOperationException)
        {
            // A principal ainda nao esta na tela.
            return false;
        }
    }

    /// <summary>
    /// O ponto esta dentro do retangulo.
    ///
    /// Fica separado -- e a unica parte disto que da para conferir sem
    /// uma janela de verdade sendo arrastada por alguem.
    /// </summary>
    internal static bool Dentro(Point canto, Point fim, Point onde)
        => onde.X >= Math.Min(canto.X, fim.X) && onde.X <= Math.Max(canto.X, fim.X)
           && onde.Y >= Math.Min(canto.Y, fim.Y) && onde.Y <= Math.Max(canto.Y, fim.Y);

    [StructLayout(LayoutKind.Sequential)]
    struct Ponto { public int X; public int Y; }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GetCursorPos(out Ponto onde);
}
