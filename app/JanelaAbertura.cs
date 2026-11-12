using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Simulador.App;

/// <summary>
/// A tela de abertura, no estilo da do Blender: o que se quer fazer
/// está todo aqui, e não escondido em três menus.
///
/// De um lado as placas, para começar do zero; do outro os últimos
/// projetos, com a foto do circuito -- reconhecer pelo desenho é bem
/// mais rápido do que ler cinco nomes parecidos.
///
/// Quem não gosta desliga em Configurações, e ela nunca mais aparece.
/// </summary>
public class JanelaAbertura : Window
{
    // As cores do tema escolhido. Eram fixas no escuro, e a tela de
    // abertura -- a primeira coisa que se ve -- ignorava o tema claro.
    // O "ultima" usa a cor de aviso: amarelo no escuro, ambar no claro.
    static Brush Fundo => Tema.Pincel(Tema.Fundo);
    static Brush Painel => Tema.Pincel(Tema.Painel);
    static Brush Borda => Tema.Pincel(Tema.Borda);
    static Brush Texto => Tema.Pincel(Tema.TextoForte);
    static Brush Fraco => Tema.Pincel(Tema.TextoFraco);
    static Brush Destaque => Tema.Pincel(Tema.Aviso);

    /// <summary>Quantos cabem na lista antes de sobrar para o "mais".</summary>
    const int NaLista = 5;

    static readonly string[] Boasvindas =
    [
        Idioma.T("bem-vindo"), Idioma.T("bom te ver de novo"), Idioma.T("vamos montar alguma coisa"),
        Idioma.T("o ferro de solda está frio"), Idioma.T("sem fumaça hoje, espero"),
        Idioma.T("de volta à bancada"), Idioma.T("que circuito é o de hoje?"),
    ];

    /// <summary>Placa escolhida para um circuito novo, se foi essa a escolha.</summary>
    public string PlacaEscolhida { get; private set; }

    /// <summary>Projeto escolhido para abrir, se foi essa a escolha.</summary>
    public string ProjetoEscolhido { get; private set; }

    public JanelaAbertura(Window dono)
    {
        // Dono que ainda nao apareceu na tela recusa virar dono. Sem
        // dono a janela abre no meio da tela, que e o certo -- e e o que
        // acontece no autoteste, onde nada e mostrado.
        try { Owner = dono; } catch (InvalidOperationException) { }

        Title = "CircuitSketch";
        Width = 780;
        Height = 640;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = dono is null
            ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
        Background = Fundo;
        ShowInTaskbar = false;

        var raiz = new Grid();
        raiz.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        raiz.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        raiz.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        raiz.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        raiz.Children.Add(Cabecalho());

        if (AvisoDeTeste() is { } aviso)
        {
            Grid.SetRow(aviso, 1);
            raiz.Children.Add(aviso);
        }

        var meio = new Grid { Margin = new Thickness(20, 4, 20, 0) };
        meio.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        meio.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        meio.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.25, GridUnitType.Star) });

        var esquerda = Placas();
        Grid.SetColumn(esquerda, 0);
        meio.Children.Add(esquerda);

        var divisoria = new Border
        {
            Width = 1,
            Background = Borda,
            Margin = new Thickness(16, 4, 16, 4),
        };
        Grid.SetColumn(divisoria, 1);
        meio.Children.Add(divisoria);

        var direita = Projetos();
        Grid.SetColumn(direita, 2);
        meio.Children.Add(direita);

        Grid.SetRow(meio, 2);
        raiz.Children.Add(meio);

        var rodape = Rodape();
        if (rodape is FrameworkElement caixaDoRodape)
            caixaDoRodape.Margin = new Thickness(20, 0, 20, 18);
        Grid.SetRow(rodape, 3);
        raiz.Children.Add(rodape);

        Content = raiz;
    }

    // ---------------- faixa de abertura ----------------

    /// <summary>
    /// A faixa de cima: uma imagem larga com o nome por cima, como na
    /// abertura do Blender.
    ///
    /// Antes era uma linha fina com o icone e o titulo. O desenho grande
    /// nao e enfeite: e ele que diz, antes de qualquer texto, que este
    /// programa e de circuito -- e e a primeira coisa que alguem ve ao
    /// abrir.
    /// </summary>
    UIElement Cabecalho()
    {
        var faixa = new Grid
        {
            Height = 210,
            ClipToBounds = true,

            // O fundo e daqui, e nao do SVG: a caixa que o WPF calcula
            // para um desenho vetorial segue a TINTA, e com ela o
            // desenho nao encostava nas bordas da faixa -- sobrava preto
            // em cima. Com a cor por baixo, a faixa fecha de qualquer
            // jeito e a arte so acrescenta.
            Background = new LinearGradientBrush(
                Color.FromRgb(0x16, 0x37, 0x5C), Color.FromRgb(0x0A, 0x16, 0x26),
                new Point(0.1, 0), new Point(0.9, 1)),
        };

        faixa.Children.Add(ArteDaFaixa());

        // Escurece o pe da imagem para o texto branco ter onde cair,
        // venha a arte que vier.
        faixa.Children.Add(new Border
        {
            Height = 96,
            VerticalAlignment = VerticalAlignment.Bottom,
            Background = new LinearGradientBrush(
                Color.FromArgb(0x00, 0x1A, 0x1A, 0x1C),
                Color.FromArgb(0xE6, 0x1A, 0x1A, 0x1C),
                new Point(0.5, 0), new Point(0.5, 1)),
        });

        // A faixa e escura em qualquer tema (a arte e o degrade sao fixos):
        // o texto dela e claro sempre. Com a cor do tema, no tema claro o
        // nome saia escuro sobre escuro.
        var claro = new SolidColorBrush(Color.FromRgb(0xF2, 0xF6, 0xFA));
        var claroFraco = new SolidColorBrush(Color.FromRgb(0xB4, 0xC2, 0xD0));

        var versao = new TextBlock
        {
            Text = Versao(),
            Foreground = claro,
            FontSize = 12,
            Opacity = 0.75,
            Margin = new Thickness(0, 12, 16, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
        };
        faixa.Children.Add(versao);

        // O autor, no canto de baixo, do lado oposto ao nome do programa.
        faixa.Children.Add(new TextBlock
        {
            Text = Idioma.T("por Emannuel Serafim Oliveira"),
            Foreground = claro,
            FontSize = 12,
            Opacity = 0.85,
            Margin = new Thickness(0, 0, 18, 18),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
        });

        var canto = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(20, 0, 20, 14),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Bottom,
        };

        var icone = Icone();
        if (icone is not null)
            canto.Children.Add(new Image
            {
                Source = icone,
                Width = 40,
                Height = 40,
                Margin = new Thickness(0, 0, 12, 0),
                VerticalAlignment = VerticalAlignment.Center,
            });

        var nomes = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        nomes.Children.Add(new TextBlock
        {
            Text = "CircuitSketch",
            Foreground = claro,
            FontSize = 28,
            FontWeight = FontWeights.SemiBold,
        });
        nomes.Children.Add(new TextBlock
        {
            Text = Boasvindas[new Random().Next(Boasvindas.Length)],
            Foreground = claroFraco,
            FontSize = 12,
            FontStyle = FontStyles.Italic,
        });
        canto.Children.Add(nomes);

        faixa.Children.Add(canto);
        return faixa;
    }

    /// <summary>
    /// A arte da faixa: trilhas, ilhas de solda e duas pastilhas.
    ///
    /// Desenhada aqui, e nao carregada de um SVG: um desenho vetorial
    /// que passa pelo conversor traz junto a caixa da TINTA, e a faixa
    /// nascia com a arte encolhida num canto ou sem aparecer. Para um
    /// fundo decorativo nao vale a pena depender disso -- e assim ele
    /// acompanha o tamanho da janela de graca.
    /// </summary>
    static UIElement ArteDaFaixa()
    {
        var tela = new Canvas
        {
            Width = 780,
            Height = 210,
            IsHitTestVisible = false,
            ClipToBounds = false,
        };

        var trilha = new SolidColorBrush(Color.FromRgb(0x3D, 0x8F, 0xD1));
        var ilha = new SolidColorBrush(Color.FromRgb(0x6F, 0xB6, 0xE8));
        var placa = new SolidColorBrush(Color.FromRgb(0x12, 0x39, 0x5F));

        // Semente fixa: a abertura e a mesma toda vez que o programa
        // abre, e nao um desenho diferente a cada execucao.
        var sorte = new Random(1609);

        for (int t = 0; t < 46; t++)
        {
            double x = sorte.Next(-30, 800), y = sorte.Next(-20, 230);
            var caminho = new System.Windows.Shapes.Polyline
            {
                Stroke = trilha,
                StrokeThickness = 1.7,
                StrokeLineJoin = PenLineJoin.Round,
                Opacity = 0.30 + sorte.NextDouble() * 0.45,
            };
            caminho.Points.Add(new Point(x, y));

            for (int k = 0; k < sorte.Next(2, 5); k++)
            {
                double passo = new[] { 40, 60, 90 }[sorte.Next(3)];
                if (sorte.Next(2) == 0) x += sorte.Next(2) == 0 ? passo : -passo;
                else y += sorte.Next(2) == 0 ? passo : -passo;

                caminho.Points.Add(new Point(x, y));

                if (sorte.NextDouble() < 0.4)
                {
                    x += sorte.Next(2) == 0 ? 22 : -22;
                    y += sorte.Next(2) == 0 ? 22 : -22;
                    caminho.Points.Add(new Point(x, y));
                }
            }

            tela.Children.Add(caminho);

            var ponto = new System.Windows.Shapes.Ellipse
            {
                Width = 7, Height = 7, Fill = ilha, Opacity = 0.7,
            };
            Canvas.SetLeft(ponto, x - 3.5);
            Canvas.SetTop(ponto, y - 3.5);
            tela.Children.Add(ponto);
        }

        void Pastilha(double x, double y, double larg, double alt, int pinos)
        {
            var corpo = new System.Windows.Shapes.Rectangle
            {
                Width = larg, Height = alt, RadiusX = 5, RadiusY = 5,
                Fill = placa, Stroke = trilha, StrokeThickness = 1.4,
            };
            Canvas.SetLeft(corpo, x);
            Canvas.SetTop(corpo, y);
            tela.Children.Add(corpo);

            double passo = alt / (pinos + 1);
            for (int k = 0; k < pinos; k++)
            {
                foreach (double px in new[] { x - 7, x + larg })
                {
                    var perna = new System.Windows.Shapes.Rectangle
                    {
                        Width = 7, Height = 4, RadiusX = 1.2, RadiusY = 1.2,
                        Fill = ilha, Opacity = 0.85,
                    };
                    Canvas.SetLeft(perna, px);
                    Canvas.SetTop(perna, y + passo * (k + 1) - 2);
                    tela.Children.Add(perna);
                }
            }
        }

        Pastilha(150, 62, 96, 86, 6);
        Pastilha(548, 44, 78, 70, 5);

        // A placa de desenvolvimento, com os dois headers.
        const double px0 = 310, py0 = 40, pl = 180, pa = 130;

        var corpoDaPlaca = new System.Windows.Shapes.Rectangle
        {
            Width = pl, Height = pa, RadiusX = 6, RadiusY = 6,
            Fill = placa, Stroke = trilha, StrokeThickness = 1.6,
        };
        Canvas.SetLeft(corpoDaPlaca, px0);
        Canvas.SetTop(corpoDaPlaca, py0);
        tela.Children.Add(corpoDaPlaca);

        var moduloDaPlaca = new System.Windows.Shapes.Rectangle
        {
            Width = pl - 80, Height = pa - 56, RadiusX = 3, RadiusY = 3,
            Fill = new SolidColorBrush(Color.FromRgb(0x0E, 0x2D, 0x4D)),
            Stroke = trilha, StrokeThickness = 1, Opacity = 0.9,
        };
        Canvas.SetLeft(moduloDaPlaca, px0 + 40);
        Canvas.SetTop(moduloDaPlaca, py0 + 22);
        tela.Children.Add(moduloDaPlaca);

        for (int k = 0; k < 11; k++)
        {
            foreach (double bx in new[] { px0 + 7, px0 + pl - 29 })
            {
                var pino = new System.Windows.Shapes.Rectangle
                {
                    Width = 22, Height = 5, RadiusX = 1.6, RadiusY = 1.6,
                    Fill = ilha, Opacity = 0.7,
                };
                Canvas.SetLeft(pino, bx);
                Canvas.SetTop(pino, py0 + 12 + k * 10.6);
                tela.Children.Add(pino);
            }
        }

        return new Viewbox
        {
            Child = tela,
            Stretch = Stretch.UniformToFill,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
    }

    /// <summary>
    /// A versao como a pessoa a chama.
    ///
    /// Vem do InformationalVersion, que e onde cabe "Alpha 16"; o
    /// numero do assembly so serve de reserva se alguem tirar o campo.
    /// </summary>
    internal static string Versao()
    {
        string nome = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        // O .NET gruda "+<hash do commit>" quando o projeto esta num
        // repositorio. O que interessa e o que vem antes.
        if (!string.IsNullOrWhiteSpace(nome))
        {
            int mais = nome.IndexOf('+');
            return (mais < 0 ? nome : nome[..mais]).Trim();
        }

        var v = Assembly.GetExecutingAssembly().GetName().Version;
        return v is null ? "1.0" : $"{v.Major}.{v.Minor}.{v.Build}";
    }

    /// <summary>
    /// O aviso das versoes de teste: o programa esta em desenvolvimento e o
    /// que se ve nao e a versao final. Sai sozinho quando o nome da versao
    /// deixar de ser Alpha ou Beta.
    /// </summary>
    static UIElement AvisoDeTeste()
    {
        string versao = Versao();
        if (!versao.Contains("Alpha", StringComparison.OrdinalIgnoreCase)
            && !versao.Contains("Beta", StringComparison.OrdinalIgnoreCase))
            return null;

        var fundo = Destaque.Clone();
        fundo.Opacity = 0.14;

        var texto = new TextBlock
        {
            Foreground = Texto,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
        };
        texto.Inlines.Add(new System.Windows.Documents.Run("⚠  ") { Foreground = Destaque });
        texto.Inlines.Add(new System.Windows.Documents.Run(Idioma.T("Versão de teste ({0}). ", versao)) { FontWeight = FontWeights.SemiBold });
        texto.Inlines.Add(new System.Windows.Documents.Run(
            Idioma.T("O CircuitSketch está em desenvolvimento: esta não é a versão final. Pode ter erros, e o que aparece aqui ainda vai mudar.")));

        return new Border
        {
            Background = fundo,
            BorderBrush = Destaque,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(20, 8, 20, 8),
            Margin = new Thickness(0, 0, 0, 8),
            Child = texto,
        };
    }

    static ImageSource Icone()
    {
        try
        {
            return new BitmapImage(
                new Uri("pack://application:,,,/assets/CircuitSketch.ico"));
        }
        catch { return null; }
    }

    // ---------------- placas ----------------
    UIElement Placas()
    {
        var coluna = new StackPanel();
        coluna.Children.Add(Titulo(Idioma.T("NOVO CIRCUITO")));

        // A última placa usada vem primeiro: quem está no meio de um
        // trabalho abre o programa dez vezes na mesma placa.
        string ultima = Preferencias.Texto(Preferencias.UltimaPlaca, "esp32-devkit-v1");
        var todas = PerfilPlaca.Conhecidos
            .OrderByDescending(p => p.Id == ultima)
            .ToList();

        foreach (var perfil in todas.Take(NaLista))
            coluna.Children.Add(ItemDePlaca(perfil, perfil.Id == ultima));

        if (todas.Count > NaLista)
        {
            var mais = new Button
            {
                Content = Idioma.T("Mais placas ▾"),
                Style = (Style)FindResource("BotaoDaAbertura"),
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 6, 0, 0),
            };
            mais.Click += (_, _) =>
            {
                var menu = MenuDeMaisPlacas(todas.Skip(NaLista), EscolherPlaca);
                menu.PlacementTarget = mais;
                menu.IsOpen = true;
                menuAberto = menu;
            };
            botaoMaisPlacas = mais;
            coluna.Children.Add(mais);
        }

        return coluna;
    }

    /// <summary>
    /// O menu do "Mais placas", agrupado pela família.
    ///
    /// Com a Alpha 18 a lista passou de cinquenta placas. Num menu corrido
    /// desse tamanho a placa procurada fica abaixo da borda da tela, e
    /// achar o ESP32-C3 no meio de trinta ESP era rolar e ler um por um.
    /// Agrupado como as subabas da paleta, o menu tem uma entrada por
    /// família, e cada uma abre só as suas placas.
    /// </summary>
    internal static ContextMenu MenuDeMaisPlacas(IEnumerable<PerfilPlaca> placas,
                                                Action<string> escolher)
    {
        var menu = new ContextMenu();
        var ordem = Catalogo.Familias().ToList();

        foreach (var grupo in placas
                     .GroupBy(p => Catalogo.Familia(p.Id) ?? "Outras")
                     .OrderBy(g => ordem.IndexOf(g.Key) is int i && i >= 0 ? i : int.MaxValue))
        {
            var familia = new MenuItem { Header = $"{Catalogo.NomeDaFamilia(grupo.Key)}  ({grupo.Count()})" };

            foreach (var perfil in grupo)
            {
                var item = new MenuItem { Header = perfil.Nome, Tag = perfil.Id };
                item.Click += (_, _) => escolher(perfil.Id);
                familia.Items.Add(item);
            }

            menu.Items.Add(familia);
        }

        return menu;
    }

    UIElement ItemDePlaca(PerfilPlaca perfil, bool ultima)
    {
        var botao = new Button
        {
            Style = (Style)FindResource("BotaoDaAbertura"),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, 0, 0, 4),
        };

        var linha = new DockPanel();
        linha.Children.Add(new TextBlock
        {
            Text = Idioma.NaLinha(perfil.Nome),
            Foreground = Texto,
            FontSize = 13,
        });

        if (ultima)
        {
            var marca = new TextBlock
            {
                Text = Idioma.T("última"),
                Foreground = Destaque,
                FontSize = 10,
                VerticalAlignment = VerticalAlignment.Center,
            };
            DockPanel.SetDock(marca, Dock.Right);
            linha.Children.Insert(0, marca);
        }

        botao.Content = linha;
        botao.Click += (_, _) => EscolherPlaca(perfil.Id);
        return botao;
    }

    /// <summary>O botão "Mais placas", quando a lista não cabe inteira.</summary>
    Button botaoMaisPlacas;

    /// <summary>O último menu de placas aberto -- é o que o passeio fotografa.</summary>
    ContextMenu menuAberto;

    /// <summary>Para o passeio: o clique no "Mais placas", devolvendo o menu aberto.</summary>
    internal ContextMenu AbrirMaisPlacasParaTeste()
    {
        botaoMaisPlacas?.RaiseEvent(new RoutedEventArgs(
            System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        return menuAberto;
    }

    void EscolherPlaca(string tipo)
    {
        PlacaEscolhida = tipo;
        Close();
    }

    // ---------------- projetos ----------------
    UIElement Projetos()
    {
        var coluna = new StackPanel();
        coluna.Children.Add(Titulo(Idioma.T("PROJETOS RECENTES")));

        var lista = Recentes.Ler();
        if (lista.Count == 0)
        {
            coluna.Children.Add(new TextBlock
            {
                Text = Idioma.T("Nenhum ainda. Comece por uma placa, ou abra um arquivo .sktch."),
                Foreground = Fraco,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(2, 6, 0, 0),
            });
            return coluna;
        }

        foreach (string caminho in lista.Take(NaLista))
            coluna.Children.Add(ItemDeProjeto(caminho));

        if (lista.Count > NaLista)
        {
            var mais = new Button
            {
                Content = Idioma.T("Mais projetos ▾"),
                Style = (Style)FindResource("BotaoDaAbertura"),
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 6, 0, 0),
            };
            mais.Click += (_, _) =>
            {
                var menu = new ContextMenu { PlacementTarget = mais };
                foreach (string caminho in lista.Skip(NaLista))
                {
                    var item = new MenuItem { Header = Path.GetFileName(caminho) };
                    item.Click += (_, _) => EscolherProjeto(caminho);
                    menu.Items.Add(item);
                }
                menu.IsOpen = true;
            };
            coluna.Children.Add(mais);
        }

        return coluna;
    }

    UIElement ItemDeProjeto(string caminho)
    {
        var botao = new Button
        {
            Style = (Style)FindResource("BotaoDaAbertura"),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, 0, 0, 4),
            ToolTip = caminho,
        };

        var linha = new DockPanel();

        var foto = Miniaturas.Ler(caminho);
        var moldura = new Border
        {
            Width = 64,
            Height = 40,
            Background = Painel,
            BorderBrush = Borda,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Margin = new Thickness(0, 0, 10, 0),
            ClipToBounds = true,
            Child = foto is null
                ? new TextBlock
                {
                    Text = "·",
                    Foreground = Fraco,
                    FontSize = 18,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                }
                : new Image { Source = foto, Stretch = Stretch.UniformToFill },
        };
        DockPanel.SetDock(moldura, Dock.Left);
        linha.Children.Add(moldura);

        var nomes = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        nomes.Children.Add(new TextBlock
        {
            Text = Path.GetFileNameWithoutExtension(caminho),
            Foreground = Texto,
            FontSize = 13,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        nomes.Children.Add(new TextBlock
        {
            Text = Quando(caminho),
            Foreground = Fraco,
            FontSize = 10,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        linha.Children.Add(nomes);

        botao.Content = linha;
        botao.Click += (_, _) => EscolherProjeto(caminho);
        return botao;
    }

    static string Quando(string caminho)
    {
        try
        {
            var quando = File.GetLastWriteTime(caminho);
            var faz = DateTime.Now - quando;

            if (faz.TotalMinutes < 60) return Idioma.T("há {0} min", Math.Max(1, (int)faz.TotalMinutes));
            if (faz.TotalHours < 24) return Idioma.T("há {0} h", (int)faz.TotalHours);
            if (faz.TotalDays < 30) return Idioma.T("há {0} dia(s)", (int)faz.TotalDays);
            return quando.ToString("dd/MM/yyyy");
        }
        catch { return ""; }
    }

    void EscolherProjeto(string caminho)
    {
        ProjetoEscolhido = caminho;
        Close();
    }

    // ---------------- rodape ----------------
    UIElement Rodape()
    {
        var linha = new DockPanel { Margin = new Thickness(0, 14, 0, 0) };

        var esquerda = new StackPanel { Orientation = Orientation.Horizontal };

        var abrir = Botao(Idioma.T("Abrir…"), () =>
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = Idioma.T("Projeto do simulador (*.sktch)|*.sktch|Todos os arquivos|*.*"),
            };
            if (dlg.ShowDialog() == true) EscolherProjeto(dlg.FileName);
        });
        esquerda.Children.Add(abrir);

        string ultimo = Preferencias.Texto(Preferencias.UltimoProjeto);
        if (!string.IsNullOrWhiteSpace(ultimo) && File.Exists(ultimo))
        {
            esquerda.Children.Add(Botao(Idioma.T("Voltar à última sessão"),
                () => EscolherProjeto(ultimo)));
        }

        linha.Children.Add(esquerda);

        var direita = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        direita.Children.Add(Botao(Idioma.T("Roadmap"), MostrarRoadmap));
        DockPanel.SetDock(direita, Dock.Right);
        linha.Children.Add(direita);

        return linha;
    }

    Button Botao(string texto, Action aoClicar)
    {
        var botao = new Button
        {
            Content = texto,
            Style = (Style)FindResource("BotaoPequeno"),
            Margin = new Thickness(0, 0, 8, 0),
        };
        botao.Click += (_, _) => aoClicar();
        return botao;
    }

    /// <summary>
    /// O roadmap das proximas versoes, o mesmo que o autor publicou: a
    /// Alpha 24 (esta), a 25 e a 26/Beta 1.
    /// </summary>
    internal const string TextoDoRoadmap =
        """
        ROADMAP DO CIRCUITSKETCH

        ALPHA 24  —  esta versão (teste)
          ✓ diagram.json no formato do Wokwi: abre e salva projetos de lá
          ✓ bateria de verdade: a placa sem o cabo USB liga só pela bateria
          ✓ correções de lógica e da energia dos circuitos
          ✓ componentes novos: diodos (comum, Zener e Schottky),
            capacitores, indutores, cristais, transistores NPN e PNP,
            MOSFET, optoacoplador, fusível, varistor e os módulos de relé
            de 1, 2 e 4 canais
          ✓ PCB perfurada, com a opção de virar a placa para ligar por baixo
          ✓ som estéreo (PCM5102A); no player DAP, AAC, WMA e APE
          ✓ MicroPython com class, try/except, with, lambda, Timer, RTC,
            SPI e Pin.irq
          ✓ gerenciador de bibliotecas, que baixa as que faltam
          ✓ testes de cada pino de cada placa e de cada componente

        ALPHA 25
          · o programa inteiro em 14 idiomas: português, inglês, espanhol,
            italiano, alemão, francês, russo, norueguês, sueco, chinês
            tradicional e simplificado, árabe, hindi e hebraico
          · os desenhos dos componentes refeitos

        ALPHA 26  /  BETA 1
          · otimização e testes gerais
          · versões para Linux e macOS
          · lançamento público

        O que está aqui é o plano de agora: a ordem e o conteúdo de cada
        versão ainda podem mudar.
        """;

    void MostrarRoadmap() => MostrarTexto("Roadmap", TextoDoRoadmap);

    void MostrarTexto(string titulo, string texto) => JanelaDeTexto(this, titulo, texto).ShowDialog();

    /// <summary>A janela de um texto (o roadmap), sem mostrar: o passeio a abre sem prender o roteiro.</summary>
    internal static Window JanelaDeTexto(Window dono, string titulo, string texto)
    {
        var janela = new Window
        {
            Title = titulo,
            Width = 600,
            Height = 680,
            Background = Fundo,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        try { janela.Owner = dono; } catch (InvalidOperationException) { }

        janela.Content = new ScrollViewer
        {
            Margin = new Thickness(16),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = new TextBlock
            {
                Text = texto,
                Foreground = Texto,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 17,
                LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            },
        };
        return janela;
    }

    /// <summary>Texto que veio embutido no programa (assets/...).</summary>
    internal static string TextoEmbutido(string nome)
    {
        var assembly = Assembly.GetExecutingAssembly();
        string recurso = assembly.GetManifestResourceNames()
            .FirstOrDefault(r => r.EndsWith(".assets." + nome, StringComparison.OrdinalIgnoreCase));
        if (recurso is null) return null;

        using var fluxo = assembly.GetManifestResourceStream(recurso);
        if (fluxo is null) return null;

        using var leitor = new StreamReader(fluxo);
        return leitor.ReadToEnd();
    }

    static TextBlock Titulo(string texto) => new()
    {
        Text = texto,
        Foreground = Fraco,
        FontSize = 11,
        FontWeight = FontWeights.SemiBold,
        Margin = new Thickness(2, 0, 0, 8),
    };
}
