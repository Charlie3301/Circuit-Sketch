using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Simulador.App;

/// <summary>
/// O aviso de biblioteca que falta, na hora de rodar.
///
/// O professor pediu isto na cara: hoje um <c>#include</c> sem dono so
/// aparece como erro LA NA FRENTE, na linha em que a biblioteca e usada,
/// e quem esta aprendendo nao liga uma coisa a outra. A janela fecha
/// esse buraco antes de a simulacao comecar.
///
/// Ela nao baixa nada da internet, e diz isso com todas as letras -- o
/// programa e offline de proposito. O que ela faz, e que resolve o caso
/// de quase toda a turma, e PROCURAR a biblioteca onde a Arduino IDE ja
/// a instalou neste computador e oferecer a copia, com o tamanho que
/// isso vai ocupar.
/// </summary>
public class JanelaFaltando : Window
{
    /// <summary>A pessoa mandou rodar assim mesmo.</summary>
    public bool Prosseguir { get; private set; }

    /// <summary>Alguma biblioteca foi instalada nesta visita.</summary>
    public bool Instalou { get; private set; }

    readonly List<Faltando> faltando;
    readonly StackPanel lista = new();

    public JanelaFaltando(Window dono, List<Faltando> faltando, Linguagem linguagem)
    {
        this.faltando = faltando;

        try { Owner = dono; } catch (InvalidOperationException) { }

        Title = "Falta biblioteca";
        Width = 620;
        SizeToContent = SizeToContent.Height;
        MaxHeight = 620;
        MinWidth = 480;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Tema.Pincel(Tema.Fundo);

        var raiz = new DockPanel { Margin = new Thickness(18) };

        // ---- cabecalho ----
        var topo = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        DockPanel.SetDock(topo, Dock.Top);

        topo.Children.Add(new TextBlock
        {
            Text = faltando.Count == 1
                ? "O sketch pede uma biblioteca que o simulador não tem."
                : $"O sketch pede {faltando.Count} bibliotecas que o simulador não tem.",
            Foreground = Tema.Pincel(Tema.TextoForte),
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
        });

        topo.Children.Add(new TextBlock
        {
            Text = linguagem == Linguagem.MicroPython
                ? "Em MicroPython os módulos vêm dentro do firmware da placa: não há "
                  + "pasta para instalar. O simulador implementa os que implementa, e "
                  + "os que faltam estão listados abaixo."
                : "O simulador não baixa da internet — ele é offline de propósito. "
                  + "Quando a biblioteca já está instalada na Arduino IDE deste "
                  + "computador, dá para copiá-la daqui mesmo.",
            Foreground = Tema.Pincel(Tema.TextoFraco),
            FontSize = 12,
            Margin = new Thickness(0, 6, 0, 0),
            TextWrapping = TextWrapping.Wrap,
        });

        raiz.Children.Add(topo);

        // ---- rodape ----
        var barra = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0),
        };
        DockPanel.SetDock(barra, Dock.Bottom);

        barra.Children.Add(Botao("Procurar numa pasta…", ProcurarNaMao, destaque: false));
        barra.Children.Add(Botao("Rodar assim mesmo", () => { Prosseguir = true; Close(); },
                                 destaque: false));
        barra.Children.Add(Botao("Cancelar", Close, destaque: true));

        raiz.Children.Add(barra);

        raiz.Children.Add(new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = lista,
        });

        Content = raiz;
        Preencher();
    }

    void Preencher()
    {
        lista.Children.Clear();
        foreach (var f in faltando) lista.Children.Add(Cartao(f));
    }

    UIElement Cartao(Faltando f)
    {
        var caixa = new Border
        {
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(12, 10, 12, 11),
            Margin = new Thickness(0, 0, 0, 8),
            Background = Tema.Pincel(Tema.Painel),
            BorderThickness = new Thickness(1),
            BorderBrush = f.DoProjeto
                ? new SolidColorBrush(Color.FromRgb(0x8A, 0x6D, 0x1A))
                : f.Candidata is null
                    ? Tema.Pincel(Tema.Borda)
                    : new SolidColorBrush(Color.FromRgb(0x2E, 0x6B, 0x3A)),
        };

        var linha = new DockPanel();

        // O botao primeiro: no DockPanel, quem entra antes fica com o
        // espaco que pediu, e o texto ao lado ocupa o resto.
        if (f.DoProjeto)
        {
            var criar = Botao("Criar o arquivo", () => Criar(f), destaque: true);
            criar.Margin = new Thickness(12, 0, 0, 0);
            criar.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(criar, Dock.Right);
            linha.Children.Add(criar);
        }
        else if (f.Candidata is not null)
        {
            var instalar = Botao($"Instalar ({f.TamanhoEmTexto})", () => Instalar(f),
                                 destaque: true);
            instalar.Margin = new Thickness(12, 0, 0, 0);
            instalar.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(instalar, Dock.Right);
            linha.Children.Add(instalar);
        }

        var coluna = new StackPanel();

        coluna.Children.Add(new TextBlock
        {
            Text = f.Cabecalho,
            Foreground = new SolidColorBrush(Color.FromRgb(0xF1, 0xC4, 0x0F)),
            FontFamily = new FontFamily("Consolas"),
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
        });

        coluna.Children.Add(new TextBlock
        {
            Text = f.DoProjeto
                ? "Escrito com aspas: é um arquivo DESTE projeto, e não uma biblioteca. "
                  + "Ele deveria estar numa aba ao lado do sketch, e não está."
                : f.Candidata is null
                ? "Não achei esta biblioteca nas pastas da Arduino IDE deste computador."
                : $"Achei em {f.Candidata}\n"
                  + $"{f.Arquivos} arquivo(s), {f.TamanhoEmTexto} em disco.",
            Foreground = Tema.Pincel(Tema.TextoFraco),
            FontSize = 11,
            Margin = new Thickness(0, 5, 0, 0),
            TextWrapping = TextWrapping.Wrap,
        });

        if (f.Candidata is not null && !f.DoProjeto)
            coluna.Children.Add(new TextBlock
            {
                Text = "Instalando, o simulador passa a rodar o código C++ dela.",
                Foreground = Tema.Pincel(Tema.TextoFraco),
                FontSize = 11,
                FontStyle = FontStyles.Italic,
                Margin = new Thickness(0, 4, 0, 0),
                TextWrapping = TextWrapping.Wrap,
            });

        linha.Children.Add(coluna);
        caixa.Child = linha;
        return caixa;
    }

    /// <summary>Pede a janela para criar a aba que falta.</summary>
    void Criar(Faltando f)
    {
        CriarArquivo?.Invoke(f.Cabecalho);
        Instalou = true;

        faltando.Remove(f);
        if (faltando.Count == 0) { Prosseguir = true; Close(); return; }
        Preencher();
    }

    /// <summary>Quem sabe criar a aba e a janela principal.</summary>
    public Action<string> CriarArquivo { get; set; }

    void Instalar(Faltando f)
    {
        try
        {
            string nome = Acervo.Instalar(f.Candidata);
            Instalou = true;

            MessageBox.Show(this,
                $"'{nome}' foi copiada para o acervo ({f.TamanhoEmTexto}).\n\n"
                + "O simulador passa a rodar o código C++ dela.",
                "Biblioteca instalada", MessageBoxButton.OK, MessageBoxImage.Information);

            faltando.Remove(f);
            if (faltando.Count == 0) { Prosseguir = true; Close(); return; }
            Preencher();
        }
        catch (Exception e)
        {
            MessageBox.Show(this, "Não deu para copiar: " + e.Message,
                "Biblioteca", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>Quando a biblioteca esta em outro lugar do disco.</summary>
    void ProcurarNaMao()
    {
        var dialogo = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Escolha a pasta da biblioteca",
        };
        if (dialogo.ShowDialog(this) != true) return;

        try
        {
            long bytes = Acervo.TamanhoDaPasta(dialogo.FolderName);
            string nome = Acervo.Instalar(dialogo.FolderName);
            Instalou = true;

            MessageBox.Show(this,
                $"'{nome}' foi copiada para o acervo ({Acervo.EmTexto(bytes)}).",
                "Biblioteca instalada", MessageBoxButton.OK, MessageBoxImage.Information);

            Prosseguir = true;
            Close();
        }
        catch (Exception e)
        {
            MessageBox.Show(this, "Não deu para copiar: " + e.Message,
                "Biblioteca", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>
    /// So existe um estilo de botao no programa; o destaque e o peso da
    /// letra. Inventar um segundo estilo so para esta janela faria ela
    /// parecer colada de outro lugar.
    /// </summary>
    static Button Botao(string texto, Action acao, bool destaque)
    {
        var b = new Button
        {
            Content = texto,
            Margin = new Thickness(8, 0, 0, 0),
            Padding = new Thickness(14, 6, 14, 6),
            MinWidth = 110,
            FontWeight = destaque ? FontWeights.SemiBold : FontWeights.Normal,
            Style = (Style)Application.Current.FindResource("BotaoPequeno"),
        };
        b.Click += (_, _) => acao();
        return b;
    }
}
