using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace Simulador.App;

/// <summary>
/// A versão nova: o nome, as novidades do release, o download e os botões.
/// Acompanha o estado das Atualizacoes ao vivo (procurando, baixando,
/// pronta, erro).
///
/// Dois jeitos:
/// - Ver: aberta pelo aviso do alto da janela ou pelo menu. "Atualizar
///   agora" fecha o programa e instala (esperando o download, se ainda
///   estiver no meio); "Depois" deixa para quando o programa fechar.
/// - Fechando: o programa está fechando e o download não terminou. A janela
///   espera e sai sozinha quando o instalador fica pronto; "Fechar sem
///   atualizar" desiste desta vez.
/// </summary>
public sealed class JanelaAtualizacao : Window
{
    public enum Modo { Ver, Fechando }

    readonly Modo modo;
    readonly TextBlock txtTitulo, txtSub, txtNotas, txtStatus, txtNota;
    readonly Border cartaoNotas;
    readonly ProgressBar barra;
    readonly Button btPrincipal, btOutro;
    readonly TextBlock linkGitHub;
    bool querAgora, saindo;

    /// <summary>Saiu pelo "Atualizar agora" (só no jeito Ver).</summary>
    public bool QuerAtualizarAgora { get; private set; }

    public JanelaAtualizacao(Window dono, Modo modo)
    {
        this.modo = modo;
        Title = Idioma.T("Atualização do CircuitSketch");
        Width = 520;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = dono is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        SetResourceReference(BackgroundProperty, Tema.Fundo);
        try { if (dono is not null) Owner = dono; } catch (InvalidOperationException) { }

        var coluna = new StackPanel { Margin = new Thickness(20, 18, 20, 16) };

        txtTitulo = new TextBlock { FontSize = 17, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
        txtTitulo.SetResourceReference(TextBlock.ForegroundProperty, Tema.TextoForte);
        coluna.Children.Add(txtTitulo);

        txtSub = new TextBlock { FontSize = 12, Margin = new Thickness(0, 4, 0, 12), TextWrapping = TextWrapping.Wrap };
        txtSub.SetResourceReference(TextBlock.ForegroundProperty, Tema.TextoFraco);
        coluna.Children.Add(txtSub);

        // As novidades: o texto do release, do jeito que foi escrito no GitHub.
        txtNotas = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap, LineHeight = 18 };
        txtNotas.SetResourceReference(TextBlock.ForegroundProperty, Tema.Texto);
        cartaoNotas = new Border
        {
            Style = (Style)Application.Current.FindResource("Cartao"),
            Child = new ScrollViewer
            {
                MaxHeight = 260,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = txtNotas,
            },
        };
        coluna.Children.Add(cartaoNotas);

        linkGitHub = new TextBlock { FontSize = 11, Margin = new Thickness(0, -4, 0, 10) };
        var link = new Hyperlink(new Run(Idioma.T("Ver no GitHub")));
        link.Click += (_, _) => AbrirPagina();
        link.SetResourceReference(TextElement.ForegroundProperty, Tema.Destaque);
        linkGitHub.Inlines.Add(link);
        coluna.Children.Add(linkGitHub);

        barra = new ProgressBar { Height = 6, Minimum = 0, Maximum = 1, Margin = new Thickness(0, 2, 0, 6) };
        barra.SetResourceReference(ProgressBar.ForegroundProperty, Tema.Destaque);
        barra.SetResourceReference(ProgressBar.BackgroundProperty, Tema.Painel);
        barra.BorderThickness = new Thickness(0);
        coluna.Children.Add(barra);

        txtStatus = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap };
        coluna.Children.Add(txtStatus);

        txtNota = new TextBlock { FontSize = 11, Margin = new Thickness(0, 10, 0, 0), TextWrapping = TextWrapping.Wrap };
        txtNota.SetResourceReference(TextBlock.ForegroundProperty, Tema.TextoFraco);
        coluna.Children.Add(txtNota);

        var botoes = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0),
        };
        btPrincipal = new Button { Style = (Style)Application.Current.FindResource("BotaoAcao") };
        btPrincipal.Click += (_, _) => Principal();
        btOutro = new Button { Style = (Style)Application.Current.FindResource("BotaoAcao"), Margin = new Thickness(0), IsCancel = true };
        btOutro.SetResourceReference(BackgroundProperty, Tema.Painel);
        btOutro.SetResourceReference(ForegroundProperty, Tema.Texto);
        btOutro.Click += (_, _) => Close();
        botoes.Children.Add(btPrincipal);
        botoes.Children.Add(btOutro);
        coluna.Children.Add(botoes);

        Content = coluna;

        Atualizacoes.Mudou += Mostrar;
        Closed += (_, _) => Atualizacoes.Mudou -= Mostrar;
        Mostrar();
    }

    // ---- o que os testes leem ----
    internal string TituloParaTeste => txtTitulo.Text;
    internal string NotasParaTeste => cartaoNotas.Visibility == Visibility.Visible ? txtNotas.Text : null;
    internal string StatusParaTeste => txtStatus.Visibility == Visibility.Visible ? txtStatus.Text : null;
    internal string BotaoParaTeste => btPrincipal.Visibility == Visibility.Visible ? btPrincipal.Content as string : null;
    internal string OutroParaTeste => btOutro.Content as string;

    static string Atual() => JanelaAbertura.Versao();

    static void Some(UIElement e, bool aparece) => e.Visibility = aparece ? Visibility.Visible : Visibility.Collapsed;

    void Status(string texto, bool erro = false)
    {
        txtStatus.Text = texto ?? "";
        txtStatus.SetResourceReference(TextBlock.ForegroundProperty, erro ? Tema.Erro : Tema.Texto);
        Some(txtStatus, !string.IsNullOrEmpty(texto));
    }

    /// <summary>O estado de agora, na tela.</summary>
    internal void Mostrar()
    {
        var nova = Atualizacoes.Nova;
        var estado = Atualizacoes.Agora;

        if (nova is null)
        {
            Some(cartaoNotas, false);
            Some(linkGitHub, false);
            Some(txtNota, false);
            Some(btPrincipal, false);
            btOutro.Content = Idioma.T("OK");
            txtSub.Text = Idioma.T("Você está na {0}.", Atual());
            switch (estado)
            {
                case Atualizacoes.Estado.EmDia:
                    txtTitulo.Text = Idioma.T("O CircuitSketch está atualizado");
                    txtSub.Text = Idioma.T("Você está na {0}, a versão mais nova.", Atual());
                    Some(barra, false);
                    Status(null);
                    break;
                case Atualizacoes.Estado.Erro:
                    txtTitulo.Text = Idioma.T("Não deu para procurar atualizações");
                    Some(barra, false);
                    Status(Atualizacoes.Erro, erro: true);
                    break;
                default:
                    txtTitulo.Text = Idioma.T("Procurando atualizações...");
                    barra.IsIndeterminate = true;
                    Some(barra, true);
                    Status(null);
                    break;
            }
            return;
        }

        txtTitulo.Text = modo == Modo.Fechando
            ? Idioma.T("Instalando a {0} ao fechar", nova.Nome)
            : Idioma.T("{0} disponível", nova.Nome);
        txtSub.Text = Idioma.T("Você está na {0}.", Atual());
        string notas = Notas(nova.Notas);
        txtNotas.Text = notas;
        Some(cartaoNotas, modo == Modo.Ver && notas.Length > 0);
        Some(linkGitHub, modo == Modo.Ver);

        barra.IsIndeterminate = false;
        switch (estado)
        {
            case Atualizacoes.Estado.Pronta:
                Some(barra, true);
                barra.Value = 1;
                Status(Idioma.T("Baixada e conferida: pronta para instalar."));
                break;
            case Atualizacoes.Estado.Erro:
                Some(barra, false);
                Status(Atualizacoes.Erro, erro: true);
                break;
            case Atualizacoes.Estado.Baixando:
                Some(barra, true);
                barra.Value = Atualizacoes.Progresso;
                Status(nova.Tamanho > 0
                    ? Idioma.T("Baixando: {0:0}% de {1:0.0} MB", Atualizacoes.Progresso * 100, nova.Tamanho / 1048576.0)
                    : Idioma.T("Baixando: {0:0}%", Atualizacoes.Progresso * 100));
                break;
            default:
                Some(barra, true);
                barra.IsIndeterminate = true;
                Status(null);
                break;
        }

        if (modo == Modo.Ver)
        {
            Some(btPrincipal, true);
            btPrincipal.Content = querAgora && estado != Atualizacoes.Estado.Pronta
                ? Idioma.T("Atualiza quando o download terminar")
                : estado == Atualizacoes.Estado.Erro ? Idioma.T("Tentar de novo") : Idioma.T("Atualizar agora");
            btPrincipal.IsEnabled = !(querAgora && estado == Atualizacoes.Estado.Baixando);
            btOutro.Content = Idioma.T("Depois");
            txtNota.Text = Idioma.T("\"Atualizar agora\" fecha o CircuitSketch, instala e abre de novo. Se deixar para depois, a versão nova é instalada sozinha quando você fechar o programa.");
            Some(txtNota, true);

            if (querAgora && estado == Atualizacoes.Estado.Pronta)
            {
                QuerAtualizarAgora = true;
                Sair();
            }
        }
        else
        {
            Some(btPrincipal, false);
            bool falhou = estado == Atualizacoes.Estado.Erro;
            btOutro.Content = falhou ? Idioma.T("Fechar") : Idioma.T("Fechar sem atualizar");
            txtNota.Text = falhou
                ? Idioma.T("A atualização fica para a próxima vez que o programa fechar.")
                : Idioma.T("O CircuitSketch fecha e instala a versão nova assim que o download terminar.");
            Some(txtNota, true);
            if (estado == Atualizacoes.Estado.Pronta) Sair();
        }
    }

    /// <summary>Sai com "sim", uma vez só (o estado pode mudar de novo no caminho).</summary>
    void Sair()
    {
        if (saindo || !IsLoaded) return;
        saindo = true;
        DialogResult = true;
    }

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        // Já estava pronta quando a janela abriu (no jeito Fechando).
        if (modo == Modo.Fechando && Atualizacoes.Agora == Atualizacoes.Estado.Pronta) Sair();
    }

    void Principal()
    {
        if (Atualizacoes.Agora == Atualizacoes.Estado.Erro) Atualizacoes.BaixarDeNovo();
        querAgora = true;
        Mostrar();
    }

    void AbrirPagina()
    {
        string url = Atualizacoes.Nova?.Pagina ?? Atualizacoes.PaginaDosReleases;
        if (!url.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase)) url = Atualizacoes.PaginaDosReleases;
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception) { }
    }

    /// <summary>
    /// O texto do release é Markdown: aqui ele vira texto corrido, sem os
    /// "#" dos títulos, os "**" do negrito e com as listas em pontos.
    /// </summary>
    internal static string Notas(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return "";
        var linhas = new List<string>();
        foreach (string bruta in markdown.Replace("\r", "").Split('\n'))
        {
            string l = bruta.TrimEnd();
            l = Regex.Replace(l, @"^\s{0,3}#{1,6}\s+", "");
            l = Regex.Replace(l, @"^(\s*)[-*+]\s+", "$1• ");
            l = l.Replace("**", "").Replace("__", "").Replace("`", "");
            l = Regex.Replace(l, @"\[([^\]]+)\]\((https?://[^)]+)\)", "$1");
            linhas.Add(l);
        }
        return Regex.Replace(string.Join("\n", linhas).Trim(), @"\n{3,}", "\n\n");
    }
}
