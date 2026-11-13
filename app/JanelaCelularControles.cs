using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Simulador.App;

/// <summary>
/// Os controles do celular: o de carrinho RC (as setas, a velocidade e os
/// botoes extras, como os apps de "Bluetooth RC Controller" da aula) e um
/// de videogame (dois manches, a cruz, A/B/X/Y, os gatilhos), para os
/// projetos de robo e de carrinho que recebem comandos pelo Bluetooth.
///
/// Tudo o que eles mandam e texto, pelo Bluetooth classico (o SerialBT) se
/// o celular estiver conectado assim, ou pelo UART do BLE (o Nordic UART).
/// Cada botao manda o que a pessoa quiser: botao direito em cima dele troca.
/// </summary>
public sealed partial class JanelaCelular
{
    // =====================================================================
    //  A configuracao, guardada nas preferencias
    // =====================================================================

    /// <summary>Um botao de controle: o que manda ao apertar e ao soltar.</summary>
    public sealed class BotaoDeControle
    {
        public string Nome { get; set; } = "";
        public string Aperta { get; set; } = "";
        public string Solta { get; set; } = "";
        /// <summary>Liga e desliga a cada toque (o farol), em vez de valer so enquanto segura (a buzina).</summary>
        public bool Alterna { get; set; }

        public BotaoDeControle Copia() => new() { Nome = Nome, Aperta = Aperta, Solta = Solta, Alterna = Alterna };
    }

    public sealed class ConfiguracaoRc
    {
        /// <summary>F, B, L, R, as diagonais G, I, H, J e o parar S: o que cada seta manda.</summary>
        public Dictionary<string, string> Direcoes { get; set; } = [];
        public List<BotaoDeControle> Extras { get; set; } = [];
        public bool MandarVelocidade { get; set; } = true;
        public int Velocidade { get; set; } = 10;
        public int Fim { get; set; } = 3;

        public static readonly string[] IdsDasDirecoes = ["F", "B", "L", "R", "G", "I", "H", "J", "S"];

        public static ConfiguracaoRc DeFabrica() => new()
        {
            Direcoes = IdsDasDirecoes.ToDictionary(d => d, d => d),
            Extras =
            [
                new() { Nome = Idioma.T("Farol"), Aperta = "W", Solta = "w", Alterna = true },
                new() { Nome = Idioma.T("Luz de ré"), Aperta = "U", Solta = "u", Alterna = true },
                new() { Nome = Idioma.T("Buzina"), Aperta = "V", Solta = "v" },
                new() { Nome = Idioma.T("Pisca-alerta"), Aperta = "X", Solta = "x", Alterna = true },
            ],
        };
    }

    public sealed class ConfiguracaoVideogame
    {
        public Dictionary<string, BotaoDeControle> Botoes { get; set; } = [];
        /// <summary>A mensagem dos manches: {lx} {ly} {rx} {ry} de -100 a 100, {lt} {rt} de 0 a 100.</summary>
        public string Formato { get; set; } = "{lx},{ly},{rx},{ry}";
        public int Intervalo { get; set; } = 100;
        public bool MandarManches { get; set; } = true;
        public int Fim { get; set; } = 1;

        public static readonly string[] IdsDosBotoes =
            ["A", "B", "X", "Y", "LB", "RB", "LT", "RT", "VIEW", "MENU", "CIMA", "BAIXO", "ESQUERDA", "DIREITA", "L3", "R3"];

        public static ConfiguracaoVideogame DeFabrica() => new()
        {
            Botoes = IdsDosBotoes.ToDictionary(id => id, id => new BotaoDeControle
            {
                Aperta = id switch { "CIMA" => "UP", "BAIXO" => "DOWN", "ESQUERDA" => "LEFT", "DIREITA" => "RIGHT", _ => id },
            }),
        };
    }

    static T Carregar<T>(string chave, Func<T> deFabrica) where T : class
    {
        if (Autoteste.Rodando) return deFabrica();
        try
        {
            string json = Preferencias.Texto(chave);
            if (json.Length > 0 && JsonSerializer.Deserialize<T>(json) is { } lido) return lido;
        }
        catch (JsonException) { }
        return deFabrica();
    }

    static void Guardar<T>(string chave, T valor)
    {
        // O autoteste nao mexe nas preferencias da pessoa.
        if (Autoteste.Rodando) return;
        Preferencias.Definir(chave, JsonSerializer.Serialize(valor));
    }

    ConfiguracaoRc rc;
    ConfiguracaoVideogame videogame;

    // =====================================================================
    //  Para onde vai o que os controles mandam
    // =====================================================================

    static string FimDe(int qual) => qual switch { 0 => "\r\n", 1 => "\n", 2 => "\r", _ => "" };

    static string[] OpcoesDoFim() => [Idioma.T("fim: CR+LF"), Idioma.T("fim: LF"), Idioma.T("fim: CR"), Idioma.T("fim: nenhum")];

    CaracteristicaBle UartDoCelular() => rede.BleDoCelular?.Visiveis().FirstOrDefault(c => c.Uuid == Ble.UartRx);

    string OndeVaoOsControles(out bool conectado)
    {
        conectado = true;
        if (rede.CelularConectadoEm is { } classico)
            return Idioma.T("manda pelo Bluetooth clássico para \"{0}\"", classico);
        if (UartDoCelular() is not null)
            return Idioma.T("manda pelo BLE (UART) para \"{0}\"", rede.BleDoCelular.Nome);
        conectado = false;
        return Idioma.T("desconectado -- conecte numa placa em Dispositivos (Bluetooth clássico, ou BLE com o UART)");
    }

    readonly List<string> enviadosPelosControles = [];

    /// <summary>Manda um comando de controle; devolve se saiu.</summary>
    bool MandarDoControle(string texto, int fim, TextBlock registro, Queue<string> ultimos)
    {
        if (string.IsNullOrEmpty(texto)) return false;
        string tudo = texto + FimDe(fim);
        bool foi;
        if (rede.CelularConectadoEm is not null) foi = rede.EnviarDoCelular(tudo);
        else if (UartDoCelular() is { } rx) foi = rede.EscreverDoCelular(rx, Encoding.UTF8.GetBytes(tudo));
        else foi = false;

        if (foi)
        {
            enviadosPelosControles.Add(texto);
            if (enviadosPelosControles.Count > 200) enviadosPelosControles.RemoveAt(0);
        }
        ultimos.Enqueue(foi ? texto : "✕");
        while (ultimos.Count > 18) ultimos.Dequeue();
        registro.Text = Idioma.T("últimos: {0}", string.Join("  ", ultimos));
        return foi;
    }

    static TextBlock Registro() => new()
    {
        FontFamily = new FontFamily("Consolas"), FontSize = 13, Foreground = TextoFraco,
        TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 10, 0, 0),
    };

    // =====================================================================
    //  As pecas: tecla e manche
    // =====================================================================

    /// <summary>
    /// Uma tecla do controle: apertar e soltar com o mouse (ou o dedo, na
    /// tela de toque), e o botao direito troca o que ela manda.
    /// </summary>
    sealed class Tecla : Border
    {
        readonly TextBlock rotulo;
        readonly Brush cor, corApertada;
        readonly ScaleTransform escala = new(1, 1);
        public bool Apertada { get; private set; }
        public bool Alterna { get; set; }
        public Action Apertou, Soltou, Editar;

        public Tecla(string texto, Brush cor, Brush corDoTexto, double largura, double altura, double canto, double fonte = 16, Brush corApertada = null)
        {
            this.cor = cor;
            this.corApertada = corApertada ?? Destaque;
            Width = largura;
            Height = altura;
            CornerRadius = new CornerRadius(canto);
            Background = cor;
            BorderBrush = Borda;
            BorderThickness = new Thickness(1);
            Cursor = Cursors.Hand;
            RenderTransformOrigin = new Point(0.5, 0.5);
            RenderTransform = escala;
            rotulo = new TextBlock
            {
                Text = texto, Foreground = corDoTexto, FontSize = fonte, FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, 0, 4, 0),
            };
            Child = rotulo;

            Ligar();
        }

        /// <summary>
        /// A tecla com o feitio dela (o bumper, o gatilho): a forma ja vem na
        /// origem, e o rotulo fica centrado no ponto dado.
        /// </summary>
        public Tecla(string texto, Brush cor, Brush corDoTexto, Geometry forma, Point centro, double fonte, Brush corApertada = null)
        {
            this.cor = cor;
            this.corApertada = corApertada ?? Destaque;
            var limites = forma.Bounds;
            Width = limites.Right;
            Height = limites.Bottom;
            Cursor = Cursors.Hand;
            RenderTransformOrigin = new Point(0.5, 0.5);
            RenderTransform = escala;
            desenho = new Path { Data = forma, Fill = cor, Stroke = Borda, StrokeThickness = 1.5 };
            rotulo = new TextBlock
            {
                Text = texto, Foreground = corDoTexto, FontSize = fonte, FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center,
            };
            var caixa = new Grid { Width = 60, Height = 30, IsHitTestVisible = false, Children = { rotulo } };
            Canvas.SetLeft(caixa, centro.X - 30);
            Canvas.SetTop(caixa, centro.Y - 15);
            Child = new Canvas { Children = { desenho, caixa } };
            Ligar();
        }

        readonly Path desenho;

        void Ligar()
        {
            MouseLeftButtonDown += (_, e) =>
            {
                CaptureMouse();
                if (Alterna) { if (Apertada) Soltar(); else Apertar(); }
                else Apertar();
                e.Handled = true;
            };
            MouseLeftButtonUp += (_, e) => { ReleaseMouseCapture(); e.Handled = true; };
            LostMouseCapture += (_, _) => { if (!Alterna) Soltar(); };
            MouseRightButtonUp += (_, e) => { Editar?.Invoke(); e.Handled = true; };
        }

        public void Apertar()
        {
            if (Apertada) return;
            Mostrar(true);
            Apertou?.Invoke();
        }

        public void Soltar()
        {
            if (!Apertada) return;
            Mostrar(false);
            Soltou?.Invoke();
        }

        /// <summary>So o desenho, sem mandar nada (o teclado e o controle do computador acendem a tecla).</summary>
        public void Mostrar(bool apertada)
        {
            Apertada = apertada;
            if (desenho is not null) desenho.Fill = apertada ? corApertada : cor;
            else Background = apertada ? corApertada : cor;
            escala.ScaleX = escala.ScaleY = apertada ? 0.94 : 1;
        }

        public void TrocarRotulo(string texto) => rotulo.Text = texto;
    }

    /// <summary>
    /// Um manche: arrasta a cabeca; solto, volta ao meio. Como no controle, a
    /// cabeca anda pouco dentro do buraco; o arraste vale ate um pouco alem
    /// da borda, para dar para chegar no fim com o mouse.
    /// </summary>
    sealed class Manche : Grid
    {
        readonly double lado, raio, curso;
        readonly TranslateTransform deslocamento = new();
        public double X { get; private set; }
        public double Y { get; private set; }
        public Action Editar;

        public Manche(double lado, double cabeca)
        {
            this.lado = lado;
            curso = (lado - cabeca) / 2 - 2;
            raio = lado * 0.4;
            Width = Height = lado;
            Cursor = Cursors.Hand;
            // O buraco do corpo, com a borda em volta.
            Children.Add(new Ellipse
            {
                Fill = new RadialGradientBrush(Color.FromRgb(0x08, 0x09, 0x0B), Color.FromRgb(0x16, 0x19, 0x1E)),
                Stroke = Cor(0x46, 0x4D, 0x58), StrokeThickness = 1.5,
            });
            // A cabeca: a borda de borracha com a concha no meio.
            var grupo = new Grid { Width = cabeca, Height = cabeca, RenderTransform = deslocamento };
            grupo.Children.Add(new Ellipse
            {
                Fill = new RadialGradientBrush(Color.FromRgb(0x3A, 0x41, 0x4B), Color.FromRgb(0x1E, 0x22, 0x28)) { GradientOrigin = new Point(0.4, 0.35) },
                Stroke = Cor(0x52, 0x5A, 0x66), StrokeThickness = 1.5,
            });
            grupo.Children.Add(new Ellipse
            {
                Width = cabeca * 0.72, Height = cabeca * 0.72,
                Fill = new RadialGradientBrush(Color.FromRgb(0x1C, 0x20, 0x26), Color.FromRgb(0x2E, 0x34, 0x3D)),
                Stroke = Cor(0x28, 0x2D, 0x35), StrokeThickness = 1,
            });
            Children.Add(grupo);

            MouseLeftButtonDown += (_, e) => { CaptureMouse(); Mover(e.GetPosition(this)); e.Handled = true; };
            MouseMove += (_, e) => { if (IsMouseCaptured) Mover(e.GetPosition(this)); };
            MouseLeftButtonUp += (_, e) => { ReleaseMouseCapture(); e.Handled = true; };
            LostMouseCapture += (_, _) => Definir(0, 0);
            MouseRightButtonUp += (_, e) => { Editar?.Invoke(); e.Handled = true; };
        }

        void Mover(Point p)
        {
            double dx = (p.X - lado / 2) / raio, dy = (p.Y - lado / 2) / raio;
            double tamanho = Math.Sqrt(dx * dx + dy * dy);
            if (tamanho > 1) { dx /= tamanho; dy /= tamanho; }
            Definir(dx, -dy);
        }

        public void Definir(double x, double y)
        {
            x = Math.Clamp(x, -1, 1);
            y = Math.Clamp(y, -1, 1);
            if (Math.Abs(x - X) < 0.001 && Math.Abs(y - Y) < 0.001) return;
            X = x;
            Y = y;
            deslocamento.X = x * curso;
            deslocamento.Y = -y * curso;
        }
    }

    // =====================================================================
    //  A janelinha de trocar o que um botao manda
    // =====================================================================

    /// <summary>Devolve o botao trocado, ou null se cancelou; "apagar" diz se pediu para tirar o botao.</summary>
    BotaoDeControle EditarBotao(string titulo, BotaoDeControle atual, bool comNome, bool comSolta, bool comAlterna,
                                bool podeApagar, out bool apagar)
    {
        bool pediuApagar = false;
        BotaoDeControle resultado = null;
        var janela = new Window
        {
            Title = titulo, Owner = this, Width = 440, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = Fundo, FontFamily = FontFamily,
            ShowInTaskbar = false,
        };
        var pilha = new StackPanel { Margin = new Thickness(22, 18, 22, 18) };
        pilha.Children.Add(new TextBlock { Text = titulo, FontSize = 18, FontWeight = FontWeights.SemiBold, Foreground = TextoForte,
                                           Margin = new Thickness(0, 0, 0, 12), TextWrapping = TextWrapping.Wrap });

        TextBox CampoCom(string rotulo, string valor, string dica)
        {
            pilha.Children.Add(new TextBlock { Text = rotulo, Foreground = TextoFraco, FontSize = 12, Margin = new Thickness(0, 6, 0, 4) });
            var campo = Campo(dica);
            campo.Text = valor ?? "";
            System.Windows.Automation.AutomationProperties.SetName(campo, rotulo);
            pilha.Children.Add(campo);
            return campo;
        }

        var nome = comNome ? CampoCom(Idioma.T("Nome no botão"), atual.Nome, null) : null;
        var aperta = CampoCom(Idioma.T("Ao apertar, manda"), atual.Aperta, Idioma.T("O texto que a placa recebe (vazio: nada)"));
        var solta = comSolta ? CampoCom(Idioma.T("Ao soltar, manda"), atual.Solta, Idioma.T("Vazio: não manda nada ao soltar")) : null;
        ToggleButton alterna = null;
        if (comAlterna)
        {
            alterna = Chave(Idioma.T("Liga e desliga a cada toque"), atual.Alterna);
            alterna.HorizontalAlignment = HorizontalAlignment.Left;
            alterna.Margin = new Thickness(0, 12, 0, 0);
            alterna.ToolTip = Idioma.T("Ligado: um toque liga (manda o de apertar) e outro desliga (manda o de soltar), como um farol. Desligado: vale enquanto segura, como uma buzina.");
            pilha.Children.Add(alterna);
        }

        var botoes = new DockPanel { Margin = new Thickness(0, 18, 0, 0), LastChildFill = false };
        var salvar = Botao(Idioma.T("Salvar"), principal: true);
        salvar.IsDefault = true;
        salvar.Click += (_, _) =>
        {
            resultado = new BotaoDeControle
            {
                Nome = nome?.Text.Trim() ?? atual.Nome, Aperta = aperta.Text, Solta = solta?.Text ?? atual.Solta,
                Alterna = alterna?.IsChecked ?? atual.Alterna,
            };
            janela.Close();
        };
        var cancelar = Botao(Idioma.T("Cancelar"));
        cancelar.IsCancel = true;
        cancelar.Click += (_, _) => janela.Close();
        DockPanel.SetDock(salvar, Dock.Right);
        DockPanel.SetDock(cancelar, Dock.Right);
        botoes.Children.Add(salvar);
        botoes.Children.Add(cancelar);
        if (podeApagar)
        {
            var tirar = Botao(Idioma.T("Tirar o botão"));
            ((TextBlock)tirar.Content).Foreground = Vermelho;
            tirar.Margin = new Thickness(0);
            tirar.Click += (_, _) => { pediuApagar = true; janela.Close(); };
            DockPanel.SetDock(tirar, Dock.Left);
            botoes.Children.Add(tirar);
        }
        pilha.Children.Add(botoes);
        janela.Content = pilha;
        janela.Loaded += (_, _) => { (nome ?? aperta).Focus(); (nome ?? aperta).SelectAll(); };
        janela.ShowDialog();
        apagar = pediuApagar;
        return resultado;
    }

    // =====================================================================
    //  Controle RC
    // =====================================================================

    readonly TextBlock rcEstado = new() { FontSize = 13, Foreground = TextoFraco, TextWrapping = TextWrapping.Wrap };
    readonly TextBlock rcRegistro = Registro();
    readonly Queue<string> rcUltimos = new();
    readonly Dictionary<string, Tecla> rcSetas = [];
    readonly WrapPanel rcExtras = new();
    readonly TextBlock rcVelocidadeTexto = new() { FontSize = 13, Foreground = TextoForte, HorizontalAlignment = HorizontalAlignment.Center };
    Slider rcVelocidade;
    readonly HashSet<Key> rcTeclasSeguradas = [];
    string rcDirecaoDoTeclado;

    void MontarControleRc()
    {
        rc = Carregar(Preferencias.CelularControleRc, ConfiguracaoRc.DeFabrica);
        foreach (var id in ConfiguracaoRc.IdsDasDirecoes) rc.Direcoes.TryAdd(id, id);

        var pilha = new DockPanel();

        var topo = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
        DockPanel.SetDock(topo, Dock.Top);
        var padrao = Botao(Idioma.T("De fábrica"));
        padrao.ToolTip = Idioma.T("Volta todos os botões ao que vem de fábrica");
        padrao.Click += (_, _) =>
        {
            if (MessageBox.Show(this, Idioma.T("Voltar todos os botões do controle ao que vem de fábrica?"), Idioma.T("Controle RC"),
                                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            rc = ConfiguracaoRc.DeFabrica();
            Guardar(Preferencias.CelularControleRc, rc);
            RemontarRc();
        };
        DockPanel.SetDock(padrao, Dock.Right);
        topo.Children.Add(padrao);
        var fim = Ciclo(OpcoesDoFim(), i => { rc.Fim = i; Guardar(Preferencias.CelularControleRc, rc); }, rc.Fim);
        fim.ToolTip = Idioma.T("O que vai no fim de cada comando (toque para trocar). Os apps de carrinho mandam sem nada.");
        DockPanel.SetDock(fim, Dock.Right);
        topo.Children.Add(fim);
        var textos = new StackPanel();
        textos.Children.Add(Titulo(Idioma.T("Controle RC")));
        textos.Children.Add(rcEstado);
        topo.Children.Add(textos);
        pilha.Children.Add(topo);

        var dica = Subtitulo(Idioma.T("Segure uma seta para andar; ao soltar, ele manda o de parar. No teclado: as setas ou W A S D. Botão direito em qualquer botão troca o que ele manda."));
        DockPanel.SetDock(dica, Dock.Top);
        pilha.Children.Add(dica);

        DockPanel.SetDock(rcRegistro, Dock.Bottom);
        pilha.Children.Add(rcRegistro);

        var corpo = new Grid();
        corpo.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        corpo.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
        corpo.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // As setas, 3x3, com o parar no meio.
        var cruz = new Grid { HorizontalAlignment = HorizontalAlignment.Center };
        for (int i = 0; i < 3; i++)
        {
            cruz.RowDefinitions.Add(new RowDefinition { Height = new GridLength(92) });
            cruz.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(92) });
        }
        foreach (var (id, simbolo, linha, coluna) in new[]
                 {
                     ("G", "↖", 0, 0), ("F", "▲", 0, 1), ("I", "↗", 0, 2),
                     ("L", "◀", 1, 0), ("S", "■", 1, 1), ("R", "▶", 1, 2),
                     ("H", "↙", 2, 0), ("B", "▼", 2, 1), ("J", "↘", 2, 2),
                 })
        {
            bool diagonal = id is "G" or "I" or "H" or "J";
            bool parar = id == "S";
            var tecla = new Tecla(simbolo, parar ? Cor(0x3A, 0x22, 0x24) : Superficie2, parar ? Vermelho : TextoForte,
                                  diagonal ? 66 : 84, diagonal ? 66 : 84, parar ? 42 : 18, diagonal ? 22 : 28,
                                  parar ? Vermelho : Destaque);
            string qual = id;
            tecla.Apertou = () => MandarRc(rc.Direcoes[qual]);
            if (!parar) tecla.Soltou = () => MandarRc(rc.Direcoes["S"]);
            tecla.Editar = () => EditarDirecao(qual);
            tecla.ToolTip = NomeDaDirecao(qual);
            Grid.SetRow(tecla, linha);
            Grid.SetColumn(tecla, coluna);
            cruz.Children.Add(tecla);
            rcSetas[id] = tecla;
        }
        var cartaoCruz = Cartao(cruz);
        cartaoCruz.Padding = new Thickness(18);
        cartaoCruz.VerticalAlignment = VerticalAlignment.Top;
        corpo.Children.Add(cartaoCruz);

        // A velocidade e os botoes extras.
        var direita = new StackPanel();
        var velocidade = new StackPanel();
        var cabecaVelocidade = new DockPanel();
        var mandarVelocidade = Chave(Idioma.T("Mandar"), rc.MandarVelocidade);
        mandarVelocidade.ToolTip = Idioma.T("Manda 0 a 9 (e q para o máximo) quando a velocidade muda");
        mandarVelocidade.Click += (_, _) => { rc.MandarVelocidade = mandarVelocidade.IsChecked == true; Guardar(Preferencias.CelularControleRc, rc); };
        DockPanel.SetDock(mandarVelocidade, Dock.Right);
        cabecaVelocidade.Children.Add(mandarVelocidade);
        cabecaVelocidade.Children.Add(new TextBlock { Text = Idioma.T("VELOCIDADE"), FontSize = 12, FontWeight = FontWeights.SemiBold,
                                                      Foreground = Destaque, VerticalAlignment = VerticalAlignment.Center });
        velocidade.Children.Add(cabecaVelocidade);
        rcVelocidade = new Slider
        {
            Minimum = 0, Maximum = 10, TickFrequency = 1, IsSnapToTickEnabled = true, TickPlacement = TickPlacement.BottomRight,
            Value = Math.Clamp(rc.Velocidade, 0, 10), Margin = new Thickness(0, 10, 0, 4), Foreground = TextoFraco,
        };
        System.Windows.Automation.AutomationProperties.SetName(rcVelocidade, Idioma.T("Velocidade"));
        rcVelocidade.ValueChanged += (_, _) => MudouVelocidade();
        velocidade.Children.Add(rcVelocidade);
        velocidade.Children.Add(rcVelocidadeTexto);
        AtualizarTextoDaVelocidade();
        direita.Children.Add(Cartao(velocidade));

        var extras = new StackPanel();
        var cabecaExtras = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        var mais = Botao("+ " + Idioma.T("Botão"));
        mais.ToolTip = Idioma.T("Põe mais um botão no controle");
        mais.Click += (_, _) =>
        {
            var novo = EditarBotao(Idioma.T("Novo botão"), new BotaoDeControle { Nome = Idioma.T("Botão {0}", rc.Extras.Count + 1) },
                                   comNome: true, comSolta: true, comAlterna: true, podeApagar: false, out _);
            if (novo is null) return;
            rc.Extras.Add(novo);
            Guardar(Preferencias.CelularControleRc, rc);
            RemontarRc();
        };
        DockPanel.SetDock(mais, Dock.Right);
        cabecaExtras.Children.Add(mais);
        cabecaExtras.Children.Add(new TextBlock { Text = Idioma.T("BOTÕES"), FontSize = 12, FontWeight = FontWeights.SemiBold,
                                                  Foreground = Destaque, VerticalAlignment = VerticalAlignment.Center });
        extras.Children.Add(cabecaExtras);
        extras.Children.Add(rcExtras);
        direita.Children.Add(Cartao(extras));

        var rolagem = new ScrollViewer { Content = direita, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetColumn(rolagem, 2);
        corpo.Children.Add(rolagem);
        pilha.Children.Add(corpo);

        paginaRc.Child = pilha;
        RemontarRc();
    }

    static string NomeDaDirecao(string id) => id switch
    {
        "F" => Idioma.T("Frente"), "B" => Idioma.T("Ré"), "L" => Idioma.T("Esquerda"), "R" => Idioma.T("Direita"),
        "G" => Idioma.T("Frente e esquerda"), "I" => Idioma.T("Frente e direita"),
        "H" => Idioma.T("Ré e esquerda"), "J" => Idioma.T("Ré e direita"), _ => Idioma.T("Parar"),
    };

    void EditarDirecao(string id)
    {
        string titulo = id == "S"
            ? Idioma.T("Parar (manda também ao soltar qualquer seta)")
            : NomeDaDirecao(id);
        var novo = EditarBotao(titulo, new BotaoDeControle { Aperta = rc.Direcoes[id] }, comNome: false, comSolta: false,
                               comAlterna: false, podeApagar: false, out _);
        if (novo is null) return;
        rc.Direcoes[id] = novo.Aperta;
        Guardar(Preferencias.CelularControleRc, rc);
    }

    void RemontarRc()
    {
        rcExtras.Children.Clear();
        foreach (var botao in rc.Extras)
        {
            var b = botao;
            var tecla = new Tecla(b.Nome, Superficie2, TextoForte, 132, 60, 16, 14) { Alterna = b.Alterna, Margin = new Thickness(0, 0, 10, 10) };
            tecla.ToolTip = b.Alterna
                ? Idioma.T("Liga: \"{0}\"   ·   desliga: \"{1}\"", b.Aperta, b.Solta)
                : Idioma.T("Aperta: \"{0}\"   ·   solta: \"{1}\"", b.Aperta, b.Solta);
            tecla.Apertou = () => MandarRc(b.Aperta);
            tecla.Soltou = () => MandarRc(b.Solta);
            tecla.Editar = () =>
            {
                var novo = EditarBotao(b.Nome, b, comNome: true, comSolta: true, comAlterna: true, podeApagar: true, out bool apagar);
                int onde = rc.Extras.IndexOf(b);
                if (apagar) rc.Extras.RemoveAt(onde);
                else if (novo is not null) rc.Extras[onde] = novo;
                else return;
                Guardar(Preferencias.CelularControleRc, rc);
                RemontarRc();
            };
            rcExtras.Children.Add(tecla);
        }
        if (rc.Extras.Count == 0)
            rcExtras.Children.Add(new TextBlock { Text = Idioma.T("Nenhum botão: toque em + Botão."), Foreground = TextoFraco, FontSize = 13 });
        rcVelocidade.Value = Math.Clamp(rc.Velocidade, 0, 10);
    }

    void AtualizarTextoDaVelocidade()
    {
        int v = (int)rcVelocidade.Value;
        rcVelocidadeTexto.Text = Idioma.T("{0}%   ·   manda \"{1}\"", v * 10, CodigoDaVelocidade(v));
    }

    static string CodigoDaVelocidade(int v) => v >= 10 ? "q" : v.ToString(CultureInfo.InvariantCulture);

    void MudouVelocidade()
    {
        int v = (int)rcVelocidade.Value;
        AtualizarTextoDaVelocidade();
        if (rc is null || v == rc.Velocidade) return;
        rc.Velocidade = v;
        Guardar(Preferencias.CelularControleRc, rc);
        if (rc.MandarVelocidade) MandarRc(CodigoDaVelocidade(v));
    }

    bool MandarRc(string texto) => MandarDoControle(texto, rc.Fim, rcRegistro, rcUltimos);

    /// <summary>As setas do teclado (ou W A S D) guiam o carrinho com a pagina aberta; duas juntas fazem a diagonal.</summary>
    bool TeclaDoRc(Key apertada, bool desceu)
    {
        if (paginaRc.Visibility != Visibility.Visible || Keyboard.FocusedElement is TextBox) return false;
        var tecla = apertada switch
        {
            Key.Up or Key.W => Key.Up, Key.Down or Key.S => Key.Down,
            Key.Left or Key.A => Key.Left, Key.Right or Key.D => Key.Right, _ => Key.None,
        };
        if (tecla == Key.None) return false;
        if (desceu) rcTeclasSeguradas.Add(tecla);
        else rcTeclasSeguradas.Remove(tecla);

        bool cima = rcTeclasSeguradas.Contains(Key.Up) && !rcTeclasSeguradas.Contains(Key.Down);
        bool baixo = rcTeclasSeguradas.Contains(Key.Down) && !rcTeclasSeguradas.Contains(Key.Up);
        bool esquerda = rcTeclasSeguradas.Contains(Key.Left) && !rcTeclasSeguradas.Contains(Key.Right);
        bool direita = rcTeclasSeguradas.Contains(Key.Right) && !rcTeclasSeguradas.Contains(Key.Left);
        string direcao = (cima, baixo, esquerda, direita) switch
        {
            (true, _, true, _) => "G", (true, _, _, true) => "I", (_, true, true, _) => "H", (_, true, _, true) => "J",
            (true, _, _, _) => "F", (_, true, _, _) => "B", (_, _, true, _) => "L", (_, _, _, true) => "R", _ => null,
        };
        if (direcao == rcDirecaoDoTeclado) return true;
        if (rcDirecaoDoTeclado is not null) rcSetas[rcDirecaoDoTeclado].Mostrar(false);
        rcDirecaoDoTeclado = direcao;
        if (direcao is null) MandarRc(rc.Direcoes["S"]);
        else
        {
            rcSetas[direcao].Mostrar(true);
            MandarRc(rc.Direcoes[direcao]);
        }
        return true;
    }

    /// <summary>A janela perdeu o foco com uma seta segurada: o KeyUp nao vem, entao o carrinho para aqui.</summary>
    void SoltarTeclasDoRc()
    {
        if (rcTeclasSeguradas.Count == 0) return;
        rcTeclasSeguradas.Clear();
        if (rcDirecaoDoTeclado is null) return;
        rcSetas[rcDirecaoDoTeclado].Mostrar(false);
        rcDirecaoDoTeclado = null;
        MandarRc(rc.Direcoes["S"]);
    }

    // =====================================================================
    //  Controle de videogame
    // =====================================================================

    readonly TextBlock vgEstado = new() { FontSize = 13, Foreground = TextoFraco, TextWrapping = TextWrapping.Wrap };
    readonly TextBlock vgRegistro = Registro();
    readonly Queue<string> vgUltimos = new();
    readonly Dictionary<string, Tecla> vgTeclas = [];
    // Nas medidas do desenho (EscalaDoControle): o buraco de raio 106 e a cabeca de raio 76.
    readonly Manche mancheEsquerdo = new(212 * EscalaDoControle, 152 * EscalaDoControle),
                    mancheDireito = new(212 * EscalaDoControle, 152 * EscalaDoControle);
    readonly TextBlock vgDoComputador = new() { FontSize = 12, Foreground = TextoFraco, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
    ToggleButton vgUsarControle;
    DispatcherTimer vgManches, vgLeitura;
    double vgLt, vgRt;
    string vgUltimoManche = "";
    ushort vgBotoesAntes;

    void MontarControleVideogame()
    {
        videogame = Carregar(Preferencias.CelularControleVideogame, ConfiguracaoVideogame.DeFabrica);
        foreach (var (id, botao) in ConfiguracaoVideogame.DeFabrica().Botoes) videogame.Botoes.TryAdd(id, botao);

        var pilha = new DockPanel();
        var topo = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        DockPanel.SetDock(topo, Dock.Top);
        var padrao = Botao(Idioma.T("De fábrica"));
        padrao.ToolTip = Idioma.T("Volta todos os botões ao que vem de fábrica");
        padrao.Click += (_, _) =>
        {
            if (MessageBox.Show(this, Idioma.T("Voltar todos os botões do controle ao que vem de fábrica?"), Idioma.T("Controle de videogame"),
                                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            videogame = ConfiguracaoVideogame.DeFabrica();
            Guardar(Preferencias.CelularControleVideogame, videogame);
            vgFormato.Text = videogame.Formato;
            vgIntervalo.Text = videogame.Intervalo.ToString(CultureInfo.InvariantCulture);
            vgMandarManches.IsChecked = videogame.MandarManches;
            AtualizarDicasDoVideogame();
        };
        DockPanel.SetDock(padrao, Dock.Right);
        topo.Children.Add(padrao);
        var fim = Ciclo(OpcoesDoFim(), i => { videogame.Fim = i; Guardar(Preferencias.CelularControleVideogame, videogame); }, videogame.Fim);
        fim.ToolTip = Idioma.T("O que vai no fim de cada mensagem (toque para trocar)");
        DockPanel.SetDock(fim, Dock.Right);
        topo.Children.Add(fim);
        var textos = new StackPanel();
        textos.Children.Add(Titulo(Idioma.T("Controle de videogame")));
        textos.Children.Add(vgEstado);
        topo.Children.Add(textos);
        pilha.Children.Add(topo);

        // Embaixo: os manches e o controle do computador.
        var ajustes = new StackPanel();
        var linha1 = new WrapPanel();
        vgUsarControle = Chave(Idioma.T("Usar o controle do computador"));
        vgUsarControle.ToolTip = Idioma.T("Um controle de Xbox (ou compatível) ligado neste computador aperta os botões daqui");
        vgUsarControle.Margin = new Thickness(0, 0, 0, 0);
        vgUsarControle.Click += (_, _) => LigarControleDoComputador(vgUsarControle.IsChecked == true);
        linha1.Children.Add(vgUsarControle);
        linha1.Children.Add(vgDoComputador);
        ajustes.Children.Add(linha1);

        var linha2 = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        vgMandarManches = Chave(Idioma.T("Mandar os manches"), videogame.MandarManches);
        vgMandarManches.Margin = new Thickness(0);
        vgMandarManches.Click += (_, _) => { videogame.MandarManches = vgMandarManches.IsChecked == true; Guardar(Preferencias.CelularControleVideogame, videogame); };
        DockPanel.SetDock(vgMandarManches, Dock.Left);
        linha2.Children.Add(vgMandarManches);
        var rotuloIntervalo = new TextBlock { Text = Idioma.T("a cada (ms)"), Foreground = TextoFraco, FontSize = 13, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 8, 0) };
        DockPanel.SetDock(rotuloIntervalo, Dock.Left);
        linha2.Children.Add(rotuloIntervalo);
        vgIntervalo.Width = 70;
        vgIntervalo.Text = videogame.Intervalo.ToString(CultureInfo.InvariantCulture);
        System.Windows.Automation.AutomationProperties.SetName(vgIntervalo, Idioma.T("a cada (ms)"));
        vgIntervalo.LostFocus += (_, _) => GuardarAjustesDosManches();
        DockPanel.SetDock(vgIntervalo, Dock.Left);
        linha2.Children.Add(vgIntervalo);
        var rotuloFormato = new TextBlock { Text = Idioma.T("Mensagem"), Foreground = TextoFraco, FontSize = 13, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 8, 0) };
        DockPanel.SetDock(rotuloFormato, Dock.Left);
        linha2.Children.Add(rotuloFormato);
        vgFormato.Text = videogame.Formato;
        vgFormato.FontFamily = new FontFamily("Consolas");
        vgFormato.ToolTip = Idioma.T("{lx} {ly} {rx} {ry}: os manches, de -100 a 100 (para cima e para a direita é positivo). {lt} {rt}: os gatilhos, de 0 a 100.");
        System.Windows.Automation.AutomationProperties.SetName(vgFormato, Idioma.T("Mensagem"));
        vgFormato.LostFocus += (_, _) => GuardarAjustesDosManches();
        vgFormato.KeyDown += (_, ev) => { if (ev.Key == Key.Enter) { GuardarAjustesDosManches(); ev.Handled = true; } };
        linha2.Children.Add(vgFormato);
        ajustes.Children.Add(linha2);
        ajustes.Children.Add(vgRegistro);
        var cartaoAjustes = Cartao(ajustes);
        cartaoAjustes.Margin = new Thickness(0, 10, 0, 0);
        DockPanel.SetDock(cartaoAjustes, Dock.Bottom);
        pilha.Children.Add(cartaoAjustes);

        var dica = Subtitulo(Idioma.T("Arraste os manches; aperte os botões com o mouse (ou o dedo). Botão direito troca o que cada um manda; nos manches, o do clique (L3 e R3)."));
        dica.Margin = new Thickness(0, 0, 0, 6);
        DockPanel.SetDock(dica, Dock.Top);
        pilha.Children.Add(dica);

        pilha.Children.Add(new Viewbox { Child = DesenharVideogame(), Stretch = Stretch.Uniform, MaxHeight = 480 });
        paginaVideogame.Child = pilha;
        AtualizarDicasDoVideogame();

        mancheEsquerdo.Editar = () => EditarBotaoDoVideogame("L3", Idioma.T("Clique do manche esquerdo (L3)"));
        mancheDireito.Editar = () => EditarBotaoDoVideogame("R3", Idioma.T("Clique do manche direito (R3)"));

        vgUltimoManche = MensagemDosManches();
        vgManches = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(Math.Clamp(videogame.Intervalo, 20, 2000)) };
        vgManches.Tick += (_, _) => MandarManches();
        vgManches.Start();
        Closed += (_, _) => { vgManches.Stop(); vgLeitura?.Stop(); };
    }

    readonly TextBox vgFormato = Campo(), vgIntervalo = Campo();
    ToggleButton vgMandarManches;

    void GuardarAjustesDosManches()
    {
        if (int.TryParse(vgIntervalo.Text.Trim(), out int ms)) videogame.Intervalo = Math.Clamp(ms, 20, 2000);
        vgIntervalo.Text = videogame.Intervalo.ToString(CultureInfo.InvariantCulture);
        videogame.Formato = vgFormato.Text;
        vgManches.Interval = TimeSpan.FromMilliseconds(videogame.Intervalo);
        vgUltimoManche = MensagemDosManches();
        Guardar(Preferencias.CelularControleVideogame, videogame);
    }

    // ---- o desenho do controle ----
    //
    // Tracado do "Xbox One.svg" da colecao "8th gen console vector gamepad
    // collection" (OpenGameArt, CC0), sem o logo: as coordenadas sao as do
    // arquivo (2048 x 2048); aqui so se recorta o controle e se reduz a metade.

    const double EscalaDoControle = 0.5, ControleX0 = 290, ControleY0 = 515;

    static Point NoControle(double x, double y)
        => new((x - ControleX0) * EscalaDoControle, (y - ControleY0) * EscalaDoControle);

    /// <summary>Uma forma do arquivo, ja na escala e trazida para a origem; devolve onde ela fica na tela.</summary>
    static Geometry FormaDoControle(string d, out Point canto)
    {
        var g = Geometry.Parse(d).CloneCurrentValue();
        var m = new Matrix(EscalaDoControle, 0, 0, EscalaDoControle, -ControleX0 * EscalaDoControle, -ControleY0 * EscalaDoControle);
        g.Transform = new MatrixTransform(m);
        var b = g.Bounds;
        m.Translate(-b.X, -b.Y);
        g.Transform = new MatrixTransform(m);
        canto = b.TopLeft;
        return g;
    }

    const string CorpoDoControle = "M1169.5 737.5c45.667,-38.333 83.333,-72.667 129,-111 17.721,-16.292 27.764,-18.325 36,-17 74.333,16 148.667,32 223,48 21.544,7.396 34.369,31.51 47,56 60.416,193.717 127.677,383.857 149,598 5.803,175.234 -49.643,275.848 -158,312 -54.36,-4.982 -121.818,-41.476 -272,-277 -4.5,-7 -11.5,-18.5 -33,-18 -176.565,-2.708 -349.435,-2.708 -525.999,0 -21.501,-0.5 -28.5,11 -33,18 -150.183,235.524 -217.641,272.018 -272.001,277 -108.356,-36.152 -163.803,-136.766 -158,-312 21.324,-214.143 88.585,-404.283 149,-598 12.631,-24.49 25.457,-48.604 47,-56 74.334,-16 148.667,-32 223,-48 8.236,-1.325 18.28,0.708 36,17 45.667,38.333 83.334,72.667 129,111 26.447,18.978 257.553,18.978 284,0z";
    const string FrenteDoControle = "M1169.5 737.5c31.073,-26.824 62.146,-53.649 93.219,-80.473 -9.74,-23.509 -19.479,-47.018 -29.219,-70.527 -8,-4.333 -16,-8.667 -24,-13 -122.218,1.91 -241.781,1.91 -363.999,0 -8.001,4.333 -16,8.667 -24,13 -9.74,23.509 -19.48,47.018 -29.22,70.527 31.073,26.824 62.146,53.649 93.219,80.473 30.017,19.994 253.983,19.994 284,0z";
    const string BumperDireito = "M1209.5 573.5c152.119,-51.86 134.466,-33.154 287,22 28.5,12.5 44.326,37.262 56.146,60.955 -72.715,-15.652 -145.431,-31.303 -218.146,-46.955 -19.656,-3.415 -46.619,26.113 -71.781,47.527 -9.74,-23.509 -19.479,-47.018 -29.219,-70.527 -8,-4.333 -16,-8.667 -24,-13z";
    const string GatilhoDireito = "M1408.16 561.291c6.835,-13.267 13.71,-27.035 19.34,-24.791 23.442,-2.47 51.739,13.529 82,37 12.07,15.464 19.594,33.672 25.149,53.071 -7.978,-13.824 -18.397,-24.499 -34.987,-30.019 -23.27,-9.871 -55.968,-19.759 -91.502,-35.261z";
    const string BumperEsquerdo = "M845.501 573.5c-152.12,-51.86 -134.467,-33.154 -287.001,22 -28.5,12.5 -44.325,37.262 -56.146,60.955 72.715,-15.652 145.431,-31.303 218.146,-46.955 19.657,-3.415 46.619,26.113 71.781,47.527 9.74,-23.509 19.48,-47.018 29.22,-70.527 8,-4.333 15.999,-8.667 24,-13z";
    const string GatilhoEsquerdo = "M646.841 561.291c-6.836,-13.267 -13.71,-27.035 -19.341,-24.791 -23.442,-2.47 -51.738,13.529 -82,37 -12.07,15.464 -19.594,33.672 -25.148,53.071 7.977,-13.824 18.396,-24.499 34.986,-30.019 23.271,-9.871 55.969,-19.759 91.503,-35.261z";

    UIElement DesenharVideogame()
    {
        var tela = new Canvas();

        void Registrar(string id, Tecla t)
        {
            tela.Children.Add(t);
            vgTeclas[id] = t;
            t.Apertou = () => ApertouNoVideogame(id);
            t.Soltou = () => SoltouNoVideogame(id);
            t.Editar = () => EditarBotaoDoVideogame(id, id);
        }

        // Pelo centro e pelos raios do arquivo (canto < 0: redonda).
        void Botao(string id, string texto, Brush cor, Brush corDoTexto, double cx, double cy, double rx, double ry, double canto, double fonte, Brush apertada = null)
        {
            double l = 2 * rx * EscalaDoControle, a = 2 * ry * EscalaDoControle;
            var t = new Tecla(texto, cor, corDoTexto, l, a, canto < 0 ? Math.Min(l, a) / 2 : canto, fonte, apertada);
            var c = NoControle(cx, cy);
            Canvas.SetLeft(t, c.X - l / 2);
            Canvas.SetTop(t, c.Y - a / 2);
            Registrar(id, t);
        }

        // Com o feitio do arquivo (bumpers e gatilhos); o rotulo no ponto dado.
        void Moldado(string id, string d, double rx, double ry, Brush cor)
        {
            var forma = FormaDoControle(d, out var canto);
            var r = NoControle(rx, ry);
            var t = new Tecla(id, cor, TextoFraco, forma, new Point(r.X - canto.X, r.Y - canto.Y), 10);
            Canvas.SetLeft(t, canto.X);
            Canvas.SetTop(t, canto.Y);
            Registrar(id, t);
        }

        void Desenho(string d, Brush fundo, Brush linha, double grossura)
        {
            var forma = FormaDoControle(d, out var canto);
            var p = new Path { Data = forma, Fill = fundo, Stroke = linha, StrokeThickness = grossura, IsHitTestVisible = false };
            Canvas.SetLeft(p, canto.X);
            Canvas.SetTop(p, canto.Y);
            tela.Children.Add(p);
        }

        // Os gatilhos e os bumpers ficam atras do corpo, como no controle.
        Moldado("LT", GatilhoEsquerdo, 585, 570, Cor(0x2A, 0x2F, 0x37));
        Moldado("RT", GatilhoDireito, 1470, 570, Cor(0x2A, 0x2F, 0x37));
        Moldado("LB", BumperEsquerdo, 680, 590, Cor(0x34, 0x3A, 0x44));
        Moldado("RB", BumperDireito, 1375, 590, Cor(0x34, 0x3A, 0x44));

        Desenho(CorpoDoControle, new LinearGradientBrush(Color.FromRgb(0x33, 0x39, 0x42), Color.FromRgb(0x1D, 0x21, 0x27), 90), Cor(0x46, 0x4D, 0x58), 2);
        // A frente, entre os bumpers.
        Desenho(FrenteDoControle, new LinearGradientBrush(Color.FromRgb(0x15, 0x17, 0x1B), Color.FromRgb(0x25, 0x29, 0x30), 90), Cor(0x3A, 0x40, 0x4A), 1.2);

        // O botao do meio, liso: sem logo; o XInput nao o le, entao e so desenho.
        var meio = NoControle(1027.5, 640);
        double rMeio = 44 * EscalaDoControle;
        var botaoDoMeio = new Ellipse
        {
            Width = 2 * rMeio, Height = 2 * rMeio, IsHitTestVisible = false,
            Fill = new RadialGradientBrush(Color.FromRgb(0x2E, 0x33, 0x3B), Color.FromRgb(0x16, 0x19, 0x1D)) { GradientOrigin = new Point(0.4, 0.3) },
            Stroke = Cor(0x4A, 0x52, 0x5E), StrokeThickness = 1.2,
        };
        Canvas.SetLeft(botaoDoMeio, meio.X - rMeio);
        Canvas.SetTop(botaoDoMeio, meio.Y - rMeio);
        tela.Children.Add(botaoDoMeio);

        void Manche(Manche m, double cx, double cy)
        {
            var c = NoControle(cx, cy);
            Canvas.SetLeft(m, c.X - m.Width / 2);
            Canvas.SetTop(m, c.Y - m.Height / 2);
            tela.Children.Add(m);
        }

        // Os manches: o esquerdo em cima, o direito embaixo.
        Manche(mancheEsquerdo, 653.5, 837.5);
        Manche(mancheDireito, 1210.5, 1125.5);

        // A cruz: bracos de 64 no arquivo, com o miolo por baixo.
        var cruz = Cor(0x1C, 0x1F, 0x25);
        var miolo = NoControle(802.5, 1082.5);
        var centroDaCruz = new Border { Width = 64 * EscalaDoControle, Height = 64 * EscalaDoControle, Background = cruz, IsHitTestVisible = false };
        Canvas.SetLeft(centroDaCruz, miolo.X);
        Canvas.SetTop(centroDaCruz, miolo.Y);
        tela.Children.Add(centroDaCruz);
        Botao("CIMA", "▲", cruz, TextoFraco, 834.5, 1043.5, 32, 39, 3, 10);
        Botao("BAIXO", "▼", cruz, TextoFraco, 834.5, 1185.5, 32, 39, 3, 10);
        Botao("ESQUERDA", "◀", cruz, TextoFraco, 763.5, 1114.5, 39, 32, 3, 10);
        Botao("DIREITA", "▶", cruz, TextoFraco, 905.5, 1114.5, 39, 32, 3, 10);

        // View e Menu.
        Botao("VIEW", "⧉", cruz, TextoFraco, 923.5, 835.5, 37, 37, -1, 12);
        Botao("MENU", "≡", cruz, TextoFraco, 1137.5, 837.5, 37, 37, -1, 14);

        // A, B, X e Y: escuros, com a letra na cor de sempre.
        var cinza = Cor(0x16, 0x19, 0x1E);
        Botao("Y", "Y", cinza, Cor(0xF2, 0xC2, 0x30), 1408.5, 738.5, 50, 49, -1, 19, Cor(0xF2, 0xC2, 0x30));
        Botao("X", "X", cinza, Cor(0x4C, 0x9B, 0xEA), 1304.5, 837.5, 50, 49, -1, 19, Cor(0x4C, 0x9B, 0xEA));
        Botao("B", "B", cinza, Cor(0xE8, 0x5C, 0x52), 1512.5, 837.5, 50, 49, -1, 19, Cor(0xE8, 0x5C, 0x52));
        Botao("A", "A", cinza, Cor(0x72, 0xC9, 0x58), 1408.5, 940.5, 50, 49, -1, 19, Cor(0x72, 0xC9, 0x58));

        // O tamanho da tela e o do corpo (com a linha), que cobre todo o resto.
        var corpo = FormaDoControle(CorpoDoControle, out var cantoDoCorpo);
        tela.Width = Math.Ceiling(cantoDoCorpo.X + corpo.Bounds.Width + 2);
        tela.Height = Math.Ceiling(cantoDoCorpo.Y + corpo.Bounds.Height + 2);
        return tela;
    }

    void EditarBotaoDoVideogame(string id, string titulo)
    {
        var atual = videogame.Botoes[id];
        var novo = EditarBotao(titulo, atual, comNome: false, comSolta: true, comAlterna: false, podeApagar: false, out _);
        if (novo is null) return;
        videogame.Botoes[id] = novo;
        Guardar(Preferencias.CelularControleVideogame, videogame);
        AtualizarDicasDoVideogame();
    }

    void AtualizarDicasDoVideogame()
    {
        foreach (var (id, tecla) in vgTeclas)
        {
            var b = videogame.Botoes[id];
            tecla.ToolTip = Idioma.T("Aperta: \"{0}\"   ·   solta: \"{1}\"", b.Aperta, b.Solta);
        }
    }

    bool MandarVideogame(string texto) => MandarDoControle(texto, videogame.Fim, vgRegistro, vgUltimos);

    void ApertouNoVideogame(string id)
    {
        if (id == "LT") vgLt = Math.Max(vgLt, 1);
        if (id == "RT") vgRt = Math.Max(vgRt, 1);
        MandarVideogame(videogame.Botoes[id].Aperta);
    }

    void SoltouNoVideogame(string id)
    {
        if (id == "LT" && !lendoControle) vgLt = 0;
        if (id == "RT" && !lendoControle) vgRt = 0;
        MandarVideogame(videogame.Botoes[id].Solta);
    }

    static string Inteiro(double v) => ((int)Math.Round(v * 100)).ToString(CultureInfo.InvariantCulture);

    internal string MensagemDosManches() => (videogame.Formato ?? "")
        .Replace("{lx}", Inteiro(mancheEsquerdo.X)).Replace("{ly}", Inteiro(mancheEsquerdo.Y))
        .Replace("{rx}", Inteiro(mancheDireito.X)).Replace("{ry}", Inteiro(mancheDireito.Y))
        .Replace("{lt}", Inteiro(vgLt)).Replace("{rt}", Inteiro(vgRt));

    /// <summary>Os manches vao de tempos em tempos, e so quando mudam (parados, nao enchem a serial).</summary>
    void MandarManches()
    {
        if (!videogame.MandarManches || string.IsNullOrEmpty(videogame.Formato)) return;
        string agora = MensagemDosManches();
        if (agora == vgUltimoManche) return;
        if (!OndeVaiAlgo()) return;
        vgUltimoManche = agora;
        MandarVideogame(agora);
    }

    bool OndeVaiAlgo() => rede.CelularConectadoEm is not null || UartDoCelular() is not null;

    // ---- o controle de verdade, ligado no computador ----

    bool lendoControle;

    static readonly (ushort Mascara, string Id)[] MascarasDoControle =
    [
        (EstadoDoControle.A, "A"), (EstadoDoControle.B, "B"), (EstadoDoControle.X, "X"), (EstadoDoControle.Y, "Y"),
        (EstadoDoControle.Lb, "LB"), (EstadoDoControle.Rb, "RB"), (EstadoDoControle.Back, "VIEW"), (EstadoDoControle.Start, "MENU"),
        (EstadoDoControle.Cima, "CIMA"), (EstadoDoControle.Baixo, "BAIXO"),
        (EstadoDoControle.Esquerda, "ESQUERDA"), (EstadoDoControle.Direita, "DIREITA"),
        (EstadoDoControle.L3, "L3"), (EstadoDoControle.R3, "R3"),
    ];

    void LigarControleDoComputador(bool ligar)
    {
        if (!ligar)
        {
            vgLeitura?.Stop();
            vgDoComputador.Text = "";
            return;
        }
        vgLeitura ??= new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(16) };
        vgLeitura.Tick -= LerControleDoComputador;
        vgLeitura.Tick += LerControleDoComputador;
        vgBotoesAntes = 0;
        vgLeitura.Start();
        LerControleDoComputador(null, null);
    }

    void LerControleDoComputador(object s, EventArgs e) => AplicarControle(Controle.Ler());

    /// <summary>O estado de um controle de verdade vira o desta tela: as teclas acendem e mandam o que mandariam.</summary>
    internal void AplicarControle(EstadoDoControle estado)
    {
        vgDoComputador.Text = estado.Ligado ? Idioma.T("controle ligado") : Idioma.T("nenhum controle ligado no computador");
        vgDoComputador.Foreground = estado.Ligado ? Verde : Amarelo;
        if (!estado.Ligado) estado = EstadoDoControle.Desligado;

        lendoControle = true;
        try
        {
            foreach (var (mascara, id) in MascarasDoControle)
            {
                bool agora = (estado.Botoes & mascara) != 0, antes = (vgBotoesAntes & mascara) != 0;
                if (agora == antes) continue;
                if (vgTeclas.TryGetValue(id, out var tecla)) { if (agora) tecla.Apertar(); else tecla.Soltar(); }
                else MandarVideogame(agora ? videogame.Botoes[id].Aperta : videogame.Botoes[id].Solta);
            }
            vgBotoesAntes = estado.Botoes;

            // Os gatilhos: o valor vai nos manches; passando da metade, contam como apertados.
            vgLt = estado.Lt;
            vgRt = estado.Rt;
            if ((estado.Lt > 0.5) != vgTeclas["LT"].Apertada) { if (estado.Lt > 0.5) vgTeclas["LT"].Apertar(); else vgTeclas["LT"].Soltar(); }
            if ((estado.Rt > 0.5) != vgTeclas["RT"].Apertada) { if (estado.Rt > 0.5) vgTeclas["RT"].Apertar(); else vgTeclas["RT"].Soltar(); }

            if (!mancheEsquerdo.IsMouseCaptured) mancheEsquerdo.Definir(estado.Lx, estado.Ly);
            if (!mancheDireito.IsMouseCaptured) mancheDireito.Definir(estado.Rx, estado.Ry);
        }
        finally { lendoControle = false; }
    }

    void AtualizarControles()
    {
        string onde = OndeVaoOsControles(out bool conectado);
        rcEstado.Text = vgEstado.Text = onde;
        rcEstado.Foreground = vgEstado.Foreground = conectado ? Verde : TextoFraco;
    }

    // =====================================================================
    //  Para os testes
    // =====================================================================

    internal IReadOnlyList<string> EnviadosPelosControles => enviadosPelosControles;

    internal void RcApertar(string direcao) => rcSetas[direcao].Apertar();
    internal void RcSoltar(string direcao) => rcSetas[direcao].Soltar();
    internal void RcExtra(int qual)
    {
        // Como o toque: o que liga e desliga alterna, o resto aperta.
        var t = (Tecla)rcExtras.Children[qual];
        if (t.Alterna && t.Apertada) t.Soltar(); else t.Apertar();
    }
    internal void RcExtraSoltar(int qual) => ((Tecla)rcExtras.Children[qual]).Soltar();
    internal bool RcExtraAceso(int qual) => ((Tecla)rcExtras.Children[qual]).Apertada;
    internal void RcVelocidade(int valor) => rcVelocidade.Value = valor;
    internal bool RcTeclado(Key tecla, bool desceu) => TeclaDoRc(tecla, desceu);
    internal void VideogameApertar(string id) => vgTeclas[id].Apertar();
    internal void VideogameSoltar(string id) => vgTeclas[id].Soltar();
    internal void VideogameManches(double lx, double ly, double rx, double ry)
    {
        mancheEsquerdo.Definir(lx, ly);
        mancheDireito.Definir(rx, ry);
        MandarManches();
    }
    internal void VideogameFormato(string formato)
    {
        vgFormato.Text = formato;
        GuardarAjustesDosManches();
    }
    internal bool VideogameAceso(string id) => vgTeclas[id].Apertada;
    internal void VideogameDefinir(string id, string aperta, string solta)
        => videogame.Botoes[id] = new BotaoDeControle { Aperta = aperta, Solta = solta };
}
