using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Simulador.App;

/// <summary>
/// O celular simulado, em 1280x720: faz o papel do celular da aula com o
/// Bluetooth da ESP32. Procura as placas; conversa pelo Bluetooth classico
/// (o SerialBT), como o app "Serial Bluetooth Terminal"; e navega no BLE --
/// servicos, caracteristicas, ler, escrever, notificacoes --, como o nRF
/// Connect, com a conversa do Nordic UART quando a placa tem esse servico.
///
/// As cores sao as de um celular (o tema escuro do Android), e nao as do
/// programa: e um aparelho, como as pecas do circuito.
/// </summary>
public sealed partial class JanelaCelular : Window
{
    internal static SolidColorBrush Cor(byte r, byte g, byte b)
    {
        var p = new SolidColorBrush(Color.FromRgb(r, g, b));
        p.Freeze();
        return p;
    }

    internal static readonly SolidColorBrush Fundo = Cor(0x12, 0x14, 0x18), Superficie = Cor(0x1D, 0x21, 0x27),
        Superficie2 = Cor(0x27, 0x2C, 0x34), TextoForte = Cor(0xEC, 0xEF, 0xF4), TextoFraco = Cor(0x9A, 0xA3, 0xB0),
        Destaque = Cor(0x82, 0xB1, 0xFF), Verde = Cor(0x7E, 0xE0, 0x9E), Vermelho = Cor(0xFF, 0x8A, 0x80),
        Amarelo = Cor(0xFF, 0xD5, 0x80), Borda = Cor(0x33, 0x39, 0x42);

    readonly Rede rede;
    readonly DispatcherTimer tique;
    readonly TextBlock horaDaBarra = new() { Foreground = TextoForte, FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
    readonly Border paginaDispositivos = new(), paginaTerminal = new(), paginaBle = new(), paginaRc = new(), paginaVideogame = new();
    readonly List<(Button Botao, Border Pagina)> abas = [];

    public JanelaCelular(Window dono, Rede rede)
    {
        this.rede = rede;
        try { Owner = dono; } catch (InvalidOperationException) { }

        Title = Idioma.T("Celular (Bluetooth da ESP32)");
        Width = 1280;
        Height = 720;
        MinWidth = 900;
        MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Fundo;
        FontFamily = new FontFamily("Segoe UI");

        var raiz = new DockPanel();
        raiz.Children.Add(BarraDeStatus());

        var corpo = new Grid();
        corpo.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(230) });
        corpo.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var menu = new StackPanel { Background = Superficie, Margin = new Thickness(0) };
        menu.Children.Add(new TextBlock
        {
            Text = Idioma.T("Bluetooth"), FontSize = 22, FontWeight = FontWeights.SemiBold, Foreground = TextoForte,
            Margin = new Thickness(22, 22, 12, 4),
        });
        menu.Children.Add(new TextBlock
        {
            Text = Idioma.T("o celular da aula"), FontSize = 12, Foreground = TextoFraco, Margin = new Thickness(22, 0, 12, 20),
        });
        foreach (var (texto, pagina) in new[] { (Idioma.T("Dispositivos"), paginaDispositivos), (Idioma.T("Terminal (clássico)"), paginaTerminal),
                                                ("BLE", paginaBle), (Idioma.T("Controle RC"), paginaRc),
                                                (Idioma.T("Controle de videogame"), paginaVideogame) })
        {
            var botao = BotaoDoMenu(texto);
            botao.Click += (_, _) => Mostrar(pagina);
            menu.Children.Add(botao);
            abas.Add((botao, pagina));
        }
        corpo.Children.Add(menu);

        var conteudo = new Grid { Margin = new Thickness(28, 20, 28, 20) };
        foreach (var p in new[] { paginaDispositivos, paginaTerminal, paginaBle, paginaRc, paginaVideogame })
            conteudo.Children.Add(p);
        Grid.SetColumn(conteudo, 1);
        corpo.Children.Add(conteudo);
        raiz.Children.Add(corpo);
        Content = raiz;

        MontarDispositivos();
        MontarTerminal();
        MontarBle();
        MontarControleRc();
        MontarControleVideogame();
        Mostrar(paginaDispositivos);

        // As setas (ou W A S D) guiam o carrinho com o Controle RC aberto.
        PreviewKeyDown += (_, ev) => { if (TeclaDoRc(ev.Key, true)) ev.Handled = true; };
        PreviewKeyUp += (_, ev) => { if (TeclaDoRc(ev.Key, false)) ev.Handled = true; };
        Deactivated += (_, _) => SoltarTeclasDoRc();

        rede.ChegouNoCelular += AoChegarClassico;
        rede.NotificouOCelular += AoNotificar;
        rede.BleDoCelularCaiu += AoCairBle;

        tique = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(400) };
        tique.Tick += (_, _) => Atualizar();
        tique.Start();
        Atualizar();

        Closed += (_, _) =>
        {
            tique.Stop();
            rede.ChegouNoCelular -= AoChegarClassico;
            rede.NotificouOCelular -= AoNotificar;
            rede.BleDoCelularCaiu -= AoCairBle;
        };
    }

    // =====================================================================
    //  Partes comuns
    // =====================================================================

    UIElement BarraDeStatus()
    {
        var barra = new DockPanel { Background = Cor(0x0B, 0x0D, 0x10), Height = 30 };
        DockPanel.SetDock(barra, Dock.Top);
        horaDaBarra.Margin = new Thickness(18, 0, 0, 0);
        barra.Children.Add(horaDaBarra);

        var direita = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
                                       Margin = new Thickness(0, 0, 18, 0) };
        direita.Children.Add(new TextBlock
        {
            Text = ((char)0xE702).ToString(), FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 13,
            Foreground = Destaque, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0),
        });
        // As barrinhas do sinal.
        var sinal = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center,
                                     Margin = new Thickness(0, 0, 14, 0) };
        for (int i = 0; i < 4; i++)
            sinal.Children.Add(new Border
            {
                Width = 3, Height = 4 + i * 3, Margin = new Thickness(0, 0, 2, 0), CornerRadius = new CornerRadius(1),
                Background = TextoForte, VerticalAlignment = VerticalAlignment.Bottom,
            });
        direita.Children.Add(sinal);
        direita.Children.Add(new TextBlock { Text = "87%", Foreground = TextoForte, FontSize = 12,
                                             VerticalAlignment = VerticalAlignment.Center });
        barra.Children.Add(direita);
        return barra;
    }

    Button BotaoDoMenu(string texto)
    {
        var modelo = new ControlTemplate(typeof(Button));
        var borda = new FrameworkElementFactory(typeof(Border), "borda");
        borda.SetValue(Border.CornerRadiusProperty, new CornerRadius(0, 22, 22, 0));
        borda.SetValue(Border.PaddingProperty, new Thickness(22, 11, 12, 11));
        borda.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background")
            { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        var texto0 = new FrameworkElementFactory(typeof(ContentPresenter));
        borda.AppendChild(texto0);
        modelo.VisualTree = borda;
        return new Button
        {
            Content = texto, Template = modelo, Background = Brushes.Transparent, Foreground = TextoForte,
            FontSize = 15, Margin = new Thickness(0, 2, 16, 2), Cursor = Cursors.Hand,
            HorizontalContentAlignment = HorizontalAlignment.Left,
        };
    }

    void Mostrar(Border pagina)
    {
        foreach (var (botao, p) in abas)
        {
            p.Visibility = p == pagina ? Visibility.Visible : Visibility.Collapsed;
            botao.Background = p == pagina ? Superficie2 : Brushes.Transparent;
            botao.Foreground = p == pagina ? Destaque : TextoForte;
        }
    }

    internal static Button Botao(string texto, bool principal = false)
    {
        var modelo = new ControlTemplate(typeof(Button));
        var borda = new FrameworkElementFactory(typeof(Border));
        borda.SetValue(Border.CornerRadiusProperty, new CornerRadius(18));
        borda.SetValue(Border.PaddingProperty, new Thickness(16, 7, 16, 7));
        borda.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background")
            { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        borda.SetBinding(Border.BorderBrushProperty, new System.Windows.Data.Binding("BorderBrush")
            { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        borda.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        var conteudo = new FrameworkElementFactory(typeof(ContentPresenter));
        conteudo.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        borda.AppendChild(conteudo);
        modelo.VisualTree = borda;
        return new Button
        {
            Content = new TextBlock { Text = texto, FontSize = 13, Foreground = principal ? Fundo : Destaque },
            Template = modelo, Cursor = Cursors.Hand,
            Background = principal ? Destaque : Brushes.Transparent,
            BorderBrush = principal ? Destaque : Borda,
            Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
        };
    }

    internal static void TrocarTexto(ContentControl botao, string texto)
    {
        if (botao.Content is TextBlock t) t.Text = texto;
        else botao.Content = texto;
    }

    /// <summary>Uma chave de ligar e desligar: a pilula acende quando ligada.</summary>
    internal static ToggleButton Chave(string texto, bool ligada = false)
    {
        var modelo = new ControlTemplate(typeof(ToggleButton));
        var borda = new FrameworkElementFactory(typeof(Border));
        borda.SetValue(Border.CornerRadiusProperty, new CornerRadius(18));
        borda.SetValue(Border.PaddingProperty, new Thickness(16, 7, 16, 7));
        borda.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        borda.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background")
            { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        borda.SetBinding(Border.BorderBrushProperty, new System.Windows.Data.Binding("BorderBrush")
            { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        var conteudo = new FrameworkElementFactory(typeof(ContentPresenter));
        conteudo.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        borda.AppendChild(conteudo);
        modelo.VisualTree = borda;
        var rotulo = new TextBlock { Text = texto, FontSize = 13 };
        var chave = new ToggleButton
        {
            Content = rotulo, Template = modelo, Cursor = Cursors.Hand, BorderBrush = Destaque,
            Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
        };
        void Pintar()
        {
            bool ligado = chave.IsChecked == true;
            chave.Background = ligado ? Destaque : Brushes.Transparent;
            rotulo.Foreground = ligado ? Fundo : Destaque;
        }
        chave.Checked += (_, _) => Pintar();
        chave.Unchecked += (_, _) => Pintar();
        chave.IsChecked = ligada;
        Pintar();
        return chave;
    }

    /// <summary>Um botao que troca de opcao a cada toque (o fim de linha, texto ou hexa).</summary>
    internal static Button Ciclo(string[] opcoes, Action<int> mudou, int inicial = 0)
    {
        int atual = Math.Clamp(inicial, 0, opcoes.Length - 1);
        var botao = Botao(opcoes[atual]);
        botao.Click += (_, _) =>
        {
            atual = (atual + 1) % opcoes.Length;
            TrocarTexto(botao, opcoes[atual]);
            mudou(atual);
        };
        return botao;
    }

    internal static TextBox Campo(string dica = null) => new()
    {
        Background = Superficie2, Foreground = TextoForte, CaretBrush = TextoForte, BorderBrush = Borda,
        BorderThickness = new Thickness(1), Padding = new Thickness(10, 7, 10, 7), FontSize = 14,
        VerticalContentAlignment = VerticalAlignment.Center, ToolTip = dica,
    };

    internal static TextBlock Titulo(string texto) => new()
    {
        Text = texto, FontSize = 24, FontWeight = FontWeights.SemiBold, Foreground = TextoForte,
        Margin = new Thickness(0, 0, 0, 4),
    };

    internal static TextBlock Subtitulo(string texto) => new()
    {
        Text = texto, FontSize = 13, Foreground = TextoFraco, TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 0, 0, 16),
    };

    internal static Border Cartao(UIElement dentro) => new()
    {
        Background = Superficie, CornerRadius = new CornerRadius(14), Padding = new Thickness(16, 12, 16, 12),
        Margin = new Thickness(0, 0, 0, 10), Child = dentro, BorderBrush = Borda, BorderThickness = new Thickness(1),
    };

    void Atualizar()
    {
        horaDaBarra.Text = DateTime.Now.ToString("HH:mm");
        AtualizarDispositivos();
        AtualizarTerminal();
        AtualizarBle();
        EsvaziarPedacoClassico();
        AtualizarControles();
    }

    // =====================================================================
    //  Dispositivos
    // =====================================================================

    readonly StackPanel listaClassico = new(), listaBle = new();
    string assinaturaDaLista = "";

    void MontarDispositivos()
    {
        var pilha = new DockPanel();
        var topo = new StackPanel();
        DockPanel.SetDock(topo, Dock.Top);
        topo.Children.Add(Titulo(Idioma.T("Dispositivos")));
        topo.Children.Add(Subtitulo(Idioma.T("As placas que ligaram o Bluetooth na simulação. O clássico é o do SerialBT.begin(); "
                                    + "o BLE aparece quando a placa começa a anunciar (BLEDevice::startAdvertising()).")));
        pilha.Children.Add(topo);

        var colunas = new Grid();
        colunas.ColumnDefinitions.Add(new ColumnDefinition());
        colunas.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
        colunas.ColumnDefinitions.Add(new ColumnDefinition());

        var esquerda = new DockPanel();
        var titulo1 = new TextBlock { Text = Idioma.T("BLUETOOTH CLÁSSICO"), FontSize = 12, FontWeight = FontWeights.SemiBold,
                                      Foreground = Destaque, Margin = new Thickness(4, 0, 0, 10) };
        DockPanel.SetDock(titulo1, Dock.Top);
        esquerda.Children.Add(titulo1);
        esquerda.Children.Add(new ScrollViewer { Content = listaClassico, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        colunas.Children.Add(esquerda);

        var direita = new DockPanel();
        var titulo2 = new TextBlock { Text = Idioma.T("BLE (ANUNCIANDO)"), FontSize = 12, FontWeight = FontWeights.SemiBold,
                                      Foreground = Destaque, Margin = new Thickness(4, 0, 0, 10) };
        DockPanel.SetDock(titulo2, Dock.Top);
        direita.Children.Add(titulo2);
        direita.Children.Add(new ScrollViewer { Content = listaBle, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Grid.SetColumn(direita, 2);
        colunas.Children.Add(direita);

        pilha.Children.Add(colunas);
        paginaDispositivos.Child = pilha;
    }

    void AtualizarDispositivos()
    {
        var classicos = rede.DispositivosVisiveis();
        var bles = rede.AnunciosBle();
        string assinatura = string.Join("|", classicos) + "#" + rede.CelularConectadoEm + "#"
                            + string.Join("|", bles.Select(r => r.Nome + r.Endereco + string.Join(",", r.UuidsAnunciados)))
                            + "#" + rede.BleDoCelular?.Endereco;
        if (assinatura == assinaturaDaLista) return;
        assinaturaDaLista = assinatura;

        listaClassico.Children.Clear();
        foreach (string nome in classicos)
        {
            bool conectado = rede.CelularConectadoEm == nome;
            var linha = new DockPanel();
            var conectar = Botao(conectado ? Idioma.T("Abrir o terminal") : Idioma.T("Conectar"), principal: !conectado);
            conectar.Click += (_, _) =>
            {
                if (rede.CelularConectadoEm != nome) rede.ConectarCelular(nome);
                Mostrar(paginaTerminal);
                AtualizarTerminal();
            };
            DockPanel.SetDock(conectar, Dock.Right);
            linha.Children.Add(conectar);
            var textos = new StackPanel();
            textos.Children.Add(new TextBlock { Text = nome, FontSize = 16, Foreground = TextoForte });
            textos.Children.Add(new TextBlock
            {
                Text = conectado ? Idioma.T("conectado (porta serial, SPP)") : Idioma.T("porta serial (SPP)"),
                FontSize = 12, Foreground = conectado ? Verde : TextoFraco,
            });
            linha.Children.Add(textos);
            listaClassico.Children.Add(Cartao(linha));
        }
        if (classicos.Count == 0)
            listaClassico.Children.Add(new TextBlock
            {
                Text = Idioma.T("nenhuma placa com o SerialBT ligado"), Foreground = TextoFraco, FontSize = 13,
                Margin = new Thickness(4, 0, 0, 0),
            });

        listaBle.Children.Clear();
        foreach (var radio in bles)
        {
            var linha = new DockPanel();
            var conectar = Botao(Idioma.T("Conectar"), principal: true);
            conectar.Click += (_, _) =>
            {
                if (rede.ConectarBleDoCelular(radio))
                {
                    bleMostrado = null;
                    Mostrar(paginaBle);
                    AtualizarBle();
                }
            };
            DockPanel.SetDock(conectar, Dock.Right);
            linha.Children.Add(conectar);
            var textos = new StackPanel();
            textos.Children.Add(new TextBlock { Text = radio.Nome, FontSize = 16, Foreground = TextoForte });
            textos.Children.Add(new TextBlock
            {
                Text = $"{radio.Endereco.ToUpperInvariant()}   ·   {radio.Rssi} dBm", FontSize = 12, Foreground = TextoFraco,
            });
            foreach (string uuid in radio.UuidsAnunciados)
                textos.Children.Add(new TextBlock
                {
                    Text = Idioma.T("serviço {0}", (Ble.NomeDoUuid(uuid) is { } conhecido ? $"{conhecido} ({uuid})" : uuid)),
                    FontSize = 12, Foreground = TextoFraco,
                });
            linha.Children.Add(textos);
            listaBle.Children.Add(Cartao(linha));
        }
        if (rede.BleDoCelular is { } ligado)
        {
            // Conectada, a placa para de anunciar: ela aparece aqui, e nao na lista.
            var linha = new DockPanel();
            var sair = Botao(Idioma.T("Desconectar"));
            sair.Click += (_, _) => { rede.DesconectarBleDoCelular(); AtualizarBle(); };
            var abrir = Botao(Idioma.T("Abrir"), principal: true);
            abrir.Click += (_, _) => { Mostrar(paginaBle); AtualizarBle(); };
            DockPanel.SetDock(sair, Dock.Right);
            DockPanel.SetDock(abrir, Dock.Right);
            linha.Children.Add(sair);
            linha.Children.Add(abrir);
            var textos = new StackPanel();
            textos.Children.Add(new TextBlock { Text = ligado.Nome, FontSize = 16, Foreground = TextoForte });
            textos.Children.Add(new TextBlock
            {
                Text = Idioma.T("conectado -- enquanto isso, a placa não anuncia"), FontSize = 12, Foreground = Verde,
            });
            linha.Children.Add(textos);
            listaBle.Children.Insert(0, Cartao(linha));
        }
        if (bles.Count == 0 && rede.BleDoCelular is null)
            listaBle.Children.Add(new TextBlock
            {
                Text = Idioma.T("nenhuma placa anunciando"), Foreground = TextoFraco, FontSize = 13, Margin = new Thickness(4, 0, 0, 0),
            });
    }

    // =====================================================================
    //  Terminal (o SerialBT)
    // =====================================================================

    readonly TextBlock terminalConectado = new() { FontSize = 13, Foreground = TextoFraco };
    readonly StackPanel linhasDoTerminal = new();
    readonly ScrollViewer rolagemDoTerminal = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    readonly TextBox entradaDoTerminal = Campo(Idioma.T("Texto para a placa (Enter manda)"));
    int fimDeLinha;
    readonly Button desconectarClassico = Botao(Idioma.T("Desconectar"));
    readonly string[] macros = ["", "", "", "", ""];
    readonly StringBuilder pedacoClassico = new();
    DateTime ultimoPedaco;

    void MontarTerminal()
    {
        var pilha = new DockPanel();
        var topo = new DockPanel();
        DockPanel.SetDock(topo, Dock.Top);
        desconectarClassico.Click += (_, _) => { rede.DesconectarCelular(); AtualizarTerminal(); };
        DockPanel.SetDock(desconectarClassico, Dock.Right);
        topo.Children.Add(desconectarClassico);
        var textos = new StackPanel();
        textos.Children.Add(Titulo("Terminal Bluetooth"));
        textos.Children.Add(terminalConectado);
        topo.Children.Add(textos);
        pilha.Children.Add(topo);

        // As macros: um toque manda o texto guardado; o botao direito troca.
        var linhaDeMacros = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 0) };
        DockPanel.SetDock(linhaDeMacros, Dock.Bottom);
        for (int i = 0; i < macros.Length; i++)
        {
            int qual = i;
            var m = Botao($"M{i + 1}");
            m.Margin = new Thickness(0, 0, 8, 0);
            m.ToolTip = Idioma.T("Toque: manda o texto guardado. Botão direito: guardar outro texto.");
            m.Click += (_, _) =>
            {
                if (string.IsNullOrEmpty(macros[qual])) DefinirMacro(qual, m);
                else EnviarClassico(macros[qual]);
            };
            m.MouseRightButtonUp += (_, ev) => { DefinirMacro(qual, m); ev.Handled = true; };
            linhaDeMacros.Children.Add(m);
        }
        pilha.Children.Add(linhaDeMacros);

        var linhaDeEnvio = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
        DockPanel.SetDock(linhaDeEnvio, Dock.Bottom);
        var enviar = Botao(Idioma.T("Enviar"), principal: true);
        enviar.Click += (_, _) => EnviarDoCampo();
        DockPanel.SetDock(enviar, Dock.Right);
        linhaDeEnvio.Children.Add(enviar);
        var fim = Ciclo([Idioma.T("fim: CR+LF"), Idioma.T("fim: LF"), Idioma.T("fim: CR"), Idioma.T("fim: nenhum")], i => fimDeLinha = i);
        fim.ToolTip = Idioma.T("O que vai no fim de cada envio (toque para trocar)");
        DockPanel.SetDock(fim, Dock.Right);
        linhaDeEnvio.Children.Add(fim);
        entradaDoTerminal.KeyDown += (_, ev) =>
        {
            if (ev.Key != Key.Enter) return;
            EnviarDoCampo();
            ev.Handled = true;
        };
        linhaDeEnvio.Children.Add(entradaDoTerminal);
        pilha.Children.Add(linhaDeEnvio);

        rolagemDoTerminal.Content = linhasDoTerminal;
        pilha.Children.Add(new Border
        {
            Background = Cor(0x0E, 0x10, 0x13), CornerRadius = new CornerRadius(12), Padding = new Thickness(14, 10, 14, 10),
            Margin = new Thickness(0, 14, 0, 0), Child = rolagemDoTerminal, BorderBrush = Borda, BorderThickness = new Thickness(1),
        });
        paginaTerminal.Child = pilha;
    }

    void DefinirMacro(int qual, Button botao)
    {
        string texto = Dialogos.Perguntar(this, Idioma.T("Texto da macro M{0}", qual + 1), macros[qual]);
        if (texto is null) return;
        macros[qual] = texto;
        TrocarTexto(botao, string.IsNullOrEmpty(texto) ? $"M{qual + 1}" : texto.Length > 12 ? texto[..12] + "…" : texto);
    }

    string FimDeLinha => fimDeLinha switch { 0 => "\r\n", 1 => "\n", 2 => "\r", _ => "" };

    void EnviarDoCampo()
    {
        string texto = entradaDoTerminal.Text;
        if (EnviarClassico(texto)) entradaDoTerminal.Clear();
    }

    bool EnviarClassico(string texto)
    {
        if (rede.CelularConectadoEm is null)
        {
            LinhaNoTerminal(Idioma.T("não está conectado: escolha uma placa em Dispositivos"), Amarelo);
            return false;
        }
        rede.EnviarDoCelular(texto + FimDeLinha);
        LinhaNoTerminal(texto, Verde, "→ ");
        return true;
    }

    void LinhaNoTerminal(string texto, Brush cor, string prefixo = "")
    {
        var linha = new TextBlock
        {
            FontFamily = new FontFamily("Consolas"), FontSize = 14, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 1, 0, 1),
        };
        linha.Inlines.Add(new System.Windows.Documents.Run(DateTime.Now.ToString("HH:mm:ss.fff") + "  ") { Foreground = TextoFraco });
        linha.Inlines.Add(new System.Windows.Documents.Run(prefixo + texto) { Foreground = cor });
        linhasDoTerminal.Children.Add(linha);
        while (linhasDoTerminal.Children.Count > 600) linhasDoTerminal.Children.RemoveAt(0);
        rolagemDoTerminal.ScrollToEnd();
    }

    void AoChegarClassico(DispositivoBt de, byte[] dados)
    {
        string texto = TextoArduino.Corrigir(TextoArduino.DeBytes(dados));
        Dispatcher.BeginInvoke(() =>
        {
            pedacoClassico.Append(texto);
            ultimoPedaco = DateTime.Now;
            string tudo = pedacoClassico.ToString();
            int quebra;
            while ((quebra = tudo.IndexOf('\n')) >= 0)
            {
                LinhaNoTerminal(tudo[..quebra].TrimEnd('\r'), TextoForte);
                tudo = tudo[(quebra + 1)..];
            }
            pedacoClassico.Clear().Append(tudo);
        });
    }

    /// <summary>O que chegou sem quebra de linha aparece depois de um instante, como no app.</summary>
    void EsvaziarPedacoClassico()
    {
        if (pedacoClassico.Length == 0 || (DateTime.Now - ultimoPedaco).TotalMilliseconds < 300) return;
        LinhaNoTerminal(pedacoClassico.ToString().TrimEnd('\r'), TextoForte);
        pedacoClassico.Clear();
    }

    string conectadoAntes;

    void AtualizarTerminal()
    {
        string agora = rede.CelularConectadoEm;
        terminalConectado.Text = agora is null
            ? Idioma.T("desconectado -- escolha uma placa em Dispositivos")
            : Idioma.T("conectado em \"{0}\" pelo Bluetooth clássico", agora);
        terminalConectado.Foreground = agora is null ? TextoFraco : Verde;
        desconectarClassico.Visibility = agora is null ? Visibility.Collapsed : Visibility.Visible;
        if (agora != conectadoAntes)
        {
            if (agora is not null) LinhaNoTerminal(Idioma.T("conectado em \"{0}\"", agora), Destaque);
            else if (conectadoAntes is not null) LinhaNoTerminal(Idioma.T("conexão encerrada"), Amarelo);
            conectadoAntes = agora;
        }
    }

    // =====================================================================
    //  BLE: o navegador GATT e o UART
    // =====================================================================

    readonly TextBlock bleTitulo = Titulo("BLE"), bleDetalhe = new() { FontSize = 13, Foreground = TextoFraco };
    readonly Button bleDesconectar = Botao(Idioma.T("Desconectar"));
    readonly ToggleButton bleModoUart = Chave(Idioma.T("Conversa (UART)"));
    readonly StackPanel bleServicos = new();
    readonly ScrollViewer bleRolagem = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    readonly DockPanel bleUart = new();
    readonly StackPanel uartLinhas = new();
    readonly ScrollViewer uartRolagem = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    readonly TextBox uartEntrada = Campo(Idioma.T("Texto para a placa, pelo UART (Enter manda)"));
    readonly Dictionary<CaracteristicaBle, TextBlock> valoresMostrados = [];
    readonly Dictionary<CaracteristicaBle, ToggleButton> chavesMostradas = [];
    RadioBle bleMostrado;

    void MontarBle()
    {
        var pilha = new DockPanel();
        var topo = new DockPanel();
        DockPanel.SetDock(topo, Dock.Top);
        bleDesconectar.Click += (_, _) => { rede.DesconectarBleDoCelular(); AtualizarBle(); };
        DockPanel.SetDock(bleDesconectar, Dock.Right);
        topo.Children.Add(bleDesconectar);
        bleModoUart.Checked += (_, _) => MostrarModoBle();
        bleModoUart.Unchecked += (_, _) => MostrarModoBle();
        DockPanel.SetDock(bleModoUart, Dock.Right);
        topo.Children.Add(bleModoUart);
        var textos = new StackPanel();
        textos.Children.Add(bleTitulo);
        textos.Children.Add(bleDetalhe);
        topo.Children.Add(textos);
        pilha.Children.Add(topo);

        bleRolagem.Content = bleServicos;
        bleRolagem.Margin = new Thickness(0, 14, 0, 0);
        pilha.Children.Add(bleRolagem);

        // O UART: a conversa pelo servico Nordic UART.
        var envio = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
        DockPanel.SetDock(envio, Dock.Bottom);
        var mandar = Botao(Idioma.T("Enviar"), principal: true);
        mandar.Click += (_, _) => EnviarUart();
        DockPanel.SetDock(mandar, Dock.Right);
        envio.Children.Add(mandar);
        uartEntrada.KeyDown += (_, ev) =>
        {
            if (ev.Key != Key.Enter) return;
            EnviarUart();
            ev.Handled = true;
        };
        envio.Children.Add(uartEntrada);
        bleUart.Children.Add(envio);
        uartRolagem.Content = uartLinhas;
        bleUart.Children.Add(new Border
        {
            Background = Cor(0x0E, 0x10, 0x13), CornerRadius = new CornerRadius(12), Padding = new Thickness(14, 10, 14, 10),
            Child = uartRolagem, BorderBrush = Borda, BorderThickness = new Thickness(1),
        });
        bleUart.Margin = new Thickness(0, 14, 0, 0);
        bleUart.Visibility = Visibility.Collapsed;
        pilha.Children.Add(bleUart);

        paginaBle.Child = pilha;
    }

    void MostrarModoBle()
    {
        bool uart = bleModoUart.IsChecked == true;
        bleUart.Visibility = uart ? Visibility.Visible : Visibility.Collapsed;
        bleRolagem.Visibility = uart ? Visibility.Collapsed : Visibility.Visible;
        if (uart && rede.BleDoCelular is { } radio
            && radio.Visiveis().FirstOrDefault(c => c.Uuid == Ble.UartTx) is { Assinada: false } tx)
            rede.AssinarDoCelular(tx, true);
        SincronizarChaves();
    }

    /// <summary>Cada chave de notificacao mostra se o celular assinou mesmo.</summary>
    void SincronizarChaves()
    {
        foreach (var (car, chave) in chavesMostradas)
            if ((chave.IsChecked == true) != car.Assinada) chave.IsChecked = car.Assinada;
    }

    void EnviarUart()
    {
        var radio = rede.BleDoCelular;
        var rx = radio?.Visiveis().FirstOrDefault(c => c.Uuid == Ble.UartRx);
        if (rx is null || uartEntrada.Text.Length == 0) return;
        if (rede.EscreverDoCelular(rx, Encoding.UTF8.GetBytes(uartEntrada.Text)))
        {
            LinhaDoUart(uartEntrada.Text, Verde, "→ ");
            uartEntrada.Clear();
        }
    }

    void LinhaDoUart(string texto, Brush cor, string prefixo = "")
    {
        var linha = new TextBlock { FontFamily = new FontFamily("Consolas"), FontSize = 14, TextWrapping = TextWrapping.Wrap };
        linha.Inlines.Add(new System.Windows.Documents.Run(DateTime.Now.ToString("HH:mm:ss.fff") + "  ") { Foreground = TextoFraco });
        linha.Inlines.Add(new System.Windows.Documents.Run(prefixo + texto) { Foreground = cor });
        uartLinhas.Children.Add(linha);
        while (uartLinhas.Children.Count > 600) uartLinhas.Children.RemoveAt(0);
        uartRolagem.ScrollToEnd();
    }

    int servicosMostrados = -1;

    void AtualizarBle()
    {
        var radio = rede.BleDoCelular;
        bleDesconectar.Visibility = radio is null ? Visibility.Collapsed : Visibility.Visible;
        bool temUart = radio?.Visiveis().Any(c => c.Uuid == Ble.UartRx) == true;
        bleModoUart.Visibility = temUart ? Visibility.Visible : Visibility.Collapsed;

        if (radio is null)
        {
            bleTitulo.Text = Idioma.T("BLE");
            bleDetalhe.Text = Idioma.T("desconectado -- escolha uma placa que esteja anunciando, em Dispositivos");
            bleDetalhe.Foreground = TextoFraco;
            if (bleMostrado is not null || servicosMostrados != 0)
            {
                bleServicos.Children.Clear();
                valoresMostrados.Clear();
                chavesMostradas.Clear();
                bleMostrado = null;
                servicosMostrados = 0;
                bleModoUart.IsChecked = false;
            }
            return;
        }

        bleTitulo.Text = radio.Nome;
        bleDetalhe.Text = Idioma.T("{0}   ·   {1} dBm   ·   conectado ({2} na placa)", radio.Endereco.ToUpperInvariant(), radio.Rssi, (radio.NimBle ? Idioma.T("NimBLE") : Idioma.T("Bluedroid")));
        bleDetalhe.Foreground = Verde;

        int quantos = radio.Visiveis().Count();
        if (bleMostrado != radio || servicosMostrados != quantos)
        {
            bool primeiraVez = bleMostrado != radio;
            bleMostrado = radio;
            servicosMostrados = quantos;
            MontarServicos(radio);
            if (primeiraVez)
            {
                if (bleModoUart.IsChecked == temUart) MostrarModoBle();
                else bleModoUart.IsChecked = temUart;
            }
        }
        SincronizarChaves();
    }

    void MontarServicos(RadioBle radio)
    {
        bleServicos.Children.Clear();
        valoresMostrados.Clear();
        chavesMostradas.Clear();
        foreach (var servico in radio.ServicosIniciados())
        {
            var dentro = new StackPanel();
            dentro.Children.Add(new TextBlock
            {
                Text = Ble.NomeDoUuid(servico.Uuid) ?? Idioma.T("Serviço"), FontSize = 17, FontWeight = FontWeights.SemiBold,
                Foreground = TextoForte,
            });
            dentro.Children.Add(new TextBlock { Text = servico.Uuid, FontSize = 12, Foreground = TextoFraco,
                                                Margin = new Thickness(0, 0, 0, 8), FontFamily = new FontFamily("Consolas") });
            List<CaracteristicaBle> caracteristicas;
            lock (servico.Caracteristicas) caracteristicas = [.. servico.Caracteristicas];
            foreach (var car in caracteristicas) dentro.Children.Add(LinhaDaCaracteristica(car));
            bleServicos.Children.Add(Cartao(dentro));
        }
        if (bleServicos.Children.Count == 0)
            bleServicos.Children.Add(new TextBlock
            {
                Text = Idioma.T("a placa não iniciou nenhum serviço (pService->start())"), Foreground = TextoFraco, FontSize = 13,
            });
    }

    UIElement LinhaDaCaracteristica(CaracteristicaBle car)
    {
        var borda = new Border
        {
            Background = Superficie2, CornerRadius = new CornerRadius(10), Padding = new Thickness(12, 9, 12, 9),
            Margin = new Thickness(0, 4, 0, 4),
        };
        var pilha = new StackPanel();
        borda.Child = pilha;

        var cabeca = new DockPanel();
        var botoes = new StackPanel { Orientation = Orientation.Horizontal };
        DockPanel.SetDock(botoes, Dock.Right);
        cabeca.Children.Add(botoes);
        var nome = new StackPanel();
        nome.Children.Add(new TextBlock
        {
            Text = car.NomeConhecido ?? car.Descricao ?? Idioma.T("Característica"), FontSize = 15, Foreground = TextoForte,
        });
        nome.Children.Add(new TextBlock { Text = car.Uuid, FontSize = 12, Foreground = TextoFraco,
                                          FontFamily = new FontFamily("Consolas") });
        var etiquetas = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        foreach (var (tem, texto) in new[] { (car.Le, Idioma.T("LER")), (car.Escreve, Idioma.T("ESCREVER")), (car.EscreveSemResposta, Idioma.T("ESCREVER SEM RESPOSTA")),
                                             (car.Notifica, Idioma.T("NOTIFICAR")), (car.Indica, Idioma.T("INDICAR")) })
            if (tem)
                etiquetas.Children.Add(new Border
                {
                    Background = Cor(0x22, 0x33, 0x4D), CornerRadius = new CornerRadius(6), Padding = new Thickness(7, 2, 7, 2),
                    Margin = new Thickness(0, 0, 6, 0),
                    Child = new TextBlock { Text = texto, FontSize = 11, Foreground = Destaque },
                });
        nome.Children.Add(etiquetas);
        cabeca.Children.Add(nome);
        pilha.Children.Add(cabeca);

        var valor = new TextBlock
        {
            Text = Idioma.T("valor: {0}", (car.Le ? Idioma.T("(toque em Ler)") : "—")), FontSize = 13, Foreground = TextoForte,
            FontFamily = new FontFamily("Consolas"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0),
        };
        valoresMostrados[car] = valor;
        pilha.Children.Add(valor);

        if (car.Le)
        {
            var ler = Botao(Idioma.T("Ler"));
            ler.Click += (_, _) =>
            {
                var lido = rede.LerDoCelular(car);
                valor.Text = Idioma.T("valor: {0}", (lido is null ? Idioma.T("(a placa recusou a leitura)") : Ble.Mostrar(lido)));
            };
            botoes.Children.Add(ler);
        }
        if (car.Notifica || car.Indica)
        {
            var assinar = Chave(car.Notifica ? Idioma.T("Notificações") : Idioma.T("Indicações"), car.Assinada);
            assinar.Checked += (_, _) =>
            {
                if (!car.Assinada && !rede.AssinarDoCelular(car, true)) assinar.IsChecked = false;
            };
            assinar.Unchecked += (_, _) =>
            {
                if (car.Assinada && !rede.AssinarDoCelular(car, false)) assinar.IsChecked = true;
            };
            chavesMostradas[car] = assinar;
            botoes.Children.Add(assinar);
        }
        if (car.Escreve || car.EscreveSemResposta)
        {
            var linhaDeEscrita = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
            var enviar = Botao(Idioma.T("Escrever"), principal: true);
            bool emHexa = false;
            var modo = Ciclo([Idioma.T("Texto"), Idioma.T("Hexa")], i => emHexa = i == 1);
            modo.ToolTip = Idioma.T("Toque para trocar entre texto e bytes em hexa");
            var campo = Campo(Idioma.T("Texto, ou bytes em hexa (01 FF 3A) com Hexa"));
            enviar.Click += (_, _) =>
            {
                byte[] dados;
                if (emHexa)
                {
                    string limpo = new(campo.Text.Where(Uri.IsHexDigit).ToArray());
                    if (limpo.Length % 2 != 0) limpo = "0" + limpo;
                    dados = Convert.FromHexString(limpo);
                }
                else dados = Encoding.UTF8.GetBytes(campo.Text);
                valor.Text = rede.EscreverDoCelular(car, dados)
                    ? Idioma.T("escrito: ") + Ble.Mostrar(dados)
                    : Idioma.T("a placa recusou a escrita");
            };
            DockPanel.SetDock(enviar, Dock.Right);
            DockPanel.SetDock(modo, Dock.Right);
            linhaDeEscrita.Children.Add(enviar);
            linhaDeEscrita.Children.Add(modo);
            linhaDeEscrita.Children.Add(campo);
            pilha.Children.Add(linhaDeEscrita);
        }
        return borda;
    }

    void AoNotificar(RadioBle radio, CaracteristicaBle car, byte[] dados)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (valoresMostrados.TryGetValue(car, out var valor))
                valor.Text = Idioma.T("notificação {0:HH:mm:ss}: {1}", DateTime.Now, Ble.Mostrar(dados));
            if (car.Uuid == Ble.UartTx)
                LinhaDoUart(TextoArduino.Corrigir(TextoArduino.DeBytes(dados)).TrimEnd('\r', '\n'), TextoForte);
        });
    }

    void AoCairBle(RadioBle radio)
    {
        Dispatcher.BeginInvoke(() =>
        {
            LinhaDoUart(Idioma.T("a conexão BLE caiu (a placa desconectou ou reiniciou)"), Amarelo);
            AtualizarBle();
        });
    }

    /// <summary>Para o teste: mostra uma das paginas ("dispositivos", "terminal", "ble", "servicos", "rc", "videogame").</summary>
    internal void MostrarPagina(string qual)
    {
        Atualizar();
        Mostrar(qual switch
        {
            "terminal" => paginaTerminal, "ble" or "servicos" => paginaBle, "rc" => paginaRc, "videogame" => paginaVideogame,
            _ => paginaDispositivos,
        });
        if (qual == "servicos") bleModoUart.IsChecked = false;
    }

    /// <summary>Para o teste: manda um texto pelo terminal classico, ou pelo UART do BLE.</summary>
    internal void Digitar(string texto, bool pelaUart)
    {
        if (pelaUart)
        {
            uartEntrada.Text = texto;
            EnviarUart();
        }
        else EnviarClassico(texto);
    }

    /// <summary>Para o teste: a chave de notificacao do cartao da caracteristica (nula se nao tem).</summary>
    internal ToggleButton TesteChaveDe(CaracteristicaBle car) => chavesMostradas.GetValueOrDefault(car);
}
