using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Simulador.App;

/// <summary>
/// O gerenciador de bibliotecas.
///
/// Em cima, o que o sketch aberto pede e ninguem atende -- que e a
/// resposta para "por que o meu codigo nao roda". Embaixo, a lista do
/// que o simulador executa e o que a pessoa instalou.
/// </summary>
public class JanelaBibliotecas : Window
{
    readonly StackPanel lista = new();
    readonly TextBox busca = new();
    readonly string sketch;

    public JanelaBibliotecas(Window dono, string sketch)
    {
        this.sketch = sketch;

        try { Owner = dono; } catch (InvalidOperationException) { }

        Title = "Bibliotecas";
        Width = 700;
        Height = 560;
        MinWidth = 520;
        MinHeight = 380;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Tema.Pincel(Tema.Fundo);

        var raiz = new DockPanel { Margin = new Thickness(16) };

        var topo = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        DockPanel.SetDock(topo, Dock.Top);
        topo.Children.Add(Faltando());

        busca.Style = (Style)Application.Current.FindResource("CampoEscuro");
        busca.ToolTip = "Procurar biblioteca pelo nome ou pelo #include";
        busca.TextChanged += (_, _) => Preencher();

        // O mesmo texto-fantasma da busca das configurações. Sem ele o
        // campo era uma faixa vazia sem explicação -- a dica só aparecia
        // parando o mouse em cima, e ninguém para o mouse numa faixa vazia.
        var dica = new TextBlock
        {
            Text = "Buscar pelo nome ou pelo #include",
            Foreground = Tema.Pincel(Tema.TextoFraco),
            FontSize = 12,
            IsHitTestVisible = false,
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        busca.TextChanged += (_, _) =>
            dica.Visibility = busca.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

        var comDica = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        comDica.Children.Add(busca);
        comDica.Children.Add(dica);
        topo.Children.Add(comDica);

        raiz.Children.Add(topo);

        // DockPanel, e nao StackPanel: numa pilha horizontal o "Fechar"
        // ignorava o alinhamento a direita e ficava colado nos outros.
        var barra = new DockPanel
        {
            LastChildFill = false,
            Margin = new Thickness(0, 12, 0, 0),
        };
        DockPanel.SetDock(barra, Dock.Bottom);
        barra.Children.Add(Botao("Instalar de um .zip…", InstalarZip));
        barra.Children.Add(Botao("Instalar de uma pasta…", Instalar));
        barra.Children.Add(Botao("Abrir a pasta das bibliotecas", AbrirPasta));

        var fechar = Botao("Fechar", Close);
        fechar.Margin = new Thickness(0);          // encosta na borda da lista
        DockPanel.SetDock(fechar, Dock.Right);
        barra.Children.Add(fechar);
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

    UIElement Faltando()
    {
        var sem = Acervo.SemDono(sketch);

        var caixa = new Border
        {
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(11, 8, 11, 9),
            Background = Tema.Pincel(Tema.Painel),
            BorderThickness = new Thickness(1),
            BorderBrush = sem.Count == 0
                ? Tema.Pincel(Tema.Borda)
                : new SolidColorBrush(Color.FromRgb(0x8A, 0x6D, 0x1A)),
        };

        var coluna = new StackPanel();
        coluna.Children.Add(new TextBlock
        {
            Text = sem.Count == 0
                ? "Este sketch não pede nenhuma biblioteca de fora."
                : $"O sketch pede {sem.Count} biblioteca(s) que o simulador não executa:",
            Foreground = Tema.Pincel(sem.Count == 0 ? Tema.TextoFraco : Tema.TextoForte),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
        });

        if (sem.Count > 0)
        {
            coluna.Children.Add(new TextBlock
            {
                Text = string.Join(" · ", sem),
                Foreground = new SolidColorBrush(Color.FromRgb(0xF1, 0xC4, 0x0F)),
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12,
                Margin = new Thickness(0, 5, 0, 0),
                TextWrapping = TextWrapping.Wrap,
            });
            coluna.Children.Add(new TextBlock
            {
                Text = "Instale a biblioteca (a pasta ou o .zip, como na Arduino IDE) e o "
                       + "simulador passa a rodar o código C++ dela.",
                Foreground = Tema.Pincel(Tema.TextoFraco),
                FontSize = 11,
                Margin = new Thickness(0, 6, 0, 0),
                TextWrapping = TextWrapping.Wrap,
            });
        }

        caixa.Child = coluna;
        return caixa;
    }

    void Preencher()
    {
        string filtro = busca.Text.Trim();
        lista.Children.Clear();

        var todas = Acervo.Todas()
            .Where(b => filtro.Length == 0
                        || b.Nome.Contains(filtro, StringComparison.OrdinalIgnoreCase)
                        || b.Cabecalho.Contains(filtro, StringComparison.OrdinalIgnoreCase)
                        || (b.Classes ?? "").Contains(filtro, StringComparison.OrdinalIgnoreCase)
                        || (b.Resumo ?? "").Contains(filtro, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (todas.Count == 0)
        {
            lista.Children.Add(new TextBlock
            {
                Text = "nenhuma biblioteca com esse nome",
                Foreground = Tema.Pincel(Tema.TextoFraco),
                FontSize = 12,
                Margin = new Thickness(2, 10, 0, 0),
            });
            return;
        }

        foreach (var biblioteca in todas) lista.Children.Add(Linha(biblioteca));
    }

    UIElement Linha(BibliotecaDoAcervo biblioteca)
    {
        var caixa = new Border
        {
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(11, 9, 11, 10),
            Margin = new Thickness(0, 0, 0, 7),
            Background = Tema.Pincel(Tema.Painel),
            BorderThickness = new Thickness(1),
            BorderBrush = Tema.Pincel(Tema.Borda),
        };

        var linha = new DockPanel();

        if (!biblioteca.DoSimulador)
        {
            var tirar = Botao("Remover", () =>
            {
                if (MessageBox.Show(this,
                        $"Remover \"{biblioteca.Nome}\" das bibliotecas instaladas?",
                        "CircuitSketch", MessageBoxButton.YesNo,
                        MessageBoxImage.Question) != MessageBoxResult.Yes)
                    return;

                try { Acervo.Remover(biblioteca); }
                catch (Exception e)
                {
                    MessageBox.Show(this, e.Message, "Não deu para remover",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                Preencher();
            });
            tirar.Margin = new Thickness(10, 0, 0, 0);
            tirar.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(tirar, Dock.Right);
            linha.Children.Add(tirar);
        }

        var coluna = new StackPanel();

        var titulo = new StackPanel { Orientation = Orientation.Horizontal };
        titulo.Children.Add(new TextBlock
        {
            Text = biblioteca.Nome,
            Foreground = Tema.Pincel(Tema.TextoForte),
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
        });
        titulo.Children.Add(new TextBlock
        {
            Text = biblioteca.DoSimulador ? "  roda no simulador" : "  instalada por você",
            Foreground = Tema.Pincel(biblioteca.DoSimulador ? Tema.Destaque : Tema.TextoFraco),
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
        });
        coluna.Children.Add(titulo);

        coluna.Children.Add(new TextBlock
        {
            Text = $"#include <{biblioteca.Cabecalho}>",
            Foreground = Tema.Pincel(Tema.TextoFraco),
            FontFamily = new FontFamily("Consolas"),
            FontSize = 11,
            Margin = new Thickness(0, 3, 0, 0),
        });

        coluna.Children.Add(new TextBlock
        {
            Text = biblioteca.Resumo,
            Foreground = Tema.Pincel(Tema.Texto),
            FontSize = 11,
            Margin = new Thickness(0, 3, 0, 0),
            TextWrapping = TextWrapping.Wrap,
        });

        if (!string.IsNullOrWhiteSpace(biblioteca.Classes))
            coluna.Children.Add(new TextBlock
            {
                Text = biblioteca.DoSimulador
                    ? "objetos: " + biblioteca.Classes
                    : $"{biblioteca.Arquivos} cabeçalho(s): {biblioteca.Classes}",
                Foreground = Tema.Pincel(Tema.TextoFraco),
                FontSize = 11,
                Margin = new Thickness(0, 3, 0, 0),
                TextWrapping = TextWrapping.Wrap,
            });

        linha.Children.Add(coluna);
        caixa.Child = linha;
        return caixa;
    }

    void Instalar()
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Escolha a pasta da biblioteca (a que tem os .h dentro)",
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            Instalada(Acervo.Instalar(dlg.FolderName));
        }
        catch (Exception e)
        {
            MessageBox.Show(this, e.Message, "Não deu para instalar",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    void InstalarZip()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Escolha o .zip da biblioteca (o que o GitHub ou o site dela entrega)",
            Filter = "Biblioteca compactada (*.zip)|*.zip",
        };
        if (dlg.ShowDialog(this) != true) return;

        try
        {
            Instalada(Acervo.InstalarZip(dlg.FileName));
        }
        catch (Exception e)
        {
            MessageBox.Show(this, e.Message, "Não deu para instalar",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    void Instalada(string nome)
    {
        Preencher();
        MessageBox.Show(this,
            $"\"{nome}\" entrou no acervo.\n\n"
            + "Quando o sketch fizer o #include dela, o simulador roda o código C++ "
            + "que vem dentro. Se ela mexer direto nos registradores de um chip "
            + "específico, o erro diz em que arquivo e linha isso acontece.",
            "Biblioteca instalada", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    void AbrirPasta()
    {
        try
        {
            System.IO.Directory.CreateDirectory(Acervo.Pasta);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = Acervo.Pasta,
                UseShellExecute = true,
            });
        }
        catch (Exception e)
        {
            MessageBox.Show(this, e.Message, "Não deu para abrir a pasta",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    Button Botao(string texto, Action aoClicar)
    {
        var botao = new Button
        {
            Content = texto,
            Style = (Style)FindResource("BotaoPequeno"),
            Margin = new Thickness(0, 0, 8, 0),
            Cursor = Cursors.Hand,
        };
        botao.Click += (_, _) => aoClicar();
        return botao;
    }

    /// <summary>Para o teste: quantas bibliotecas estao listadas agora.</summary>
    internal int ListadasParaTeste => lista.Children.OfType<Border>().Count();

    /// <summary>Para o teste: digitar na busca.</summary>
    internal void BuscarParaTeste(string texto) => busca.Text = texto;
}
