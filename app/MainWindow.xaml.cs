using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Simulador.App;

public partial class MainWindow : Window
{
    /// <summary>
    /// Uma maquina por placa. Ver <see cref="Bancada"/>.
    /// </summary>
    readonly Bancada bancada = new();

    /// <summary>
    /// A maquina que a JANELA mostra: monitor serial, avisos, faixa do
    /// ADC nos rotulos. E a da placa selecionada, a mesma cujo codigo
    /// esta no editor -- as duas coisas andam juntas de proposito.
    /// </summary>
    Maquina maq => bancada.EmFoco;
    readonly Buzina buzina = new();

    /// <summary>O alto-falante do circuito tocando o I2S da placa (o som do DOOM).</summary>
    readonly Falante falante = new();

    /// <summary>O celular simulado (Bluetooth classico e BLE), quando aberto.</summary>
    JanelaCelular celular;

    /// <summary>O celular ja abriu sozinho nesta rodada: fechado pela pessoa, fica fechado.</summary>
    bool celularAbertoNestaRodada;

    /// <summary>
    /// Abre o celular (ou traz para a frente). Ele conversa com a rede da
    /// simulacao, que e a mesma de uma rodada para a outra -- pode ficar
    /// aberto entre um F5 e outro.
    /// </summary>
    void AbrirCelular(object s = null, RoutedEventArgs e = null)
    {
        if (celular is { IsLoaded: true })
        {
            if (celular.WindowState == WindowState.Minimized) celular.WindowState = WindowState.Normal;
            celular.Activate();
            return;
        }
        celular = new JanelaCelular(this, redeSimulada);
        celular.Closed += (_, _) => celular = null;
        celular.Show();
    }

    internal JanelaCelular TesteAbrirCelular()
    {
        AbrirCelular();
        return celular;
    }
    readonly Dictionary<string, Interpretador> interpretadores = [];
    readonly Dictionary<string, Thread> execucoes = [];

    /// <summary>
    /// O programa compilado de cada placa na rodada atual. O RESET recomeca
    /// a placa com ELE, e nao com o que estiver no editor agora: na placa
    /// real o reset roda o que foi gravado, nao o que mudou depois.
    /// </summary>
    readonly Dictionary<string, Programa> programas = [];

    /// <summary>Placas paradas com o RESET segurado.</summary>
    readonly HashSet<string> emReset = [];

    /// <summary>
    /// Placas apagadas por falta de energia nesta rodada: sem o cabo USB, e a
    /// bateria nao chega nelas (ou chega fraca). Ligam sozinhas quando chegar.
    /// </summary>
    readonly HashSet<string> semEnergia = [];

    // ---- rede ----
    /// <summary>
    /// A rede da bancada: uma so para todas as placas, porque rede e
    /// justamente o que liga uma placa a outra.
    /// </summary>
    readonly Rede redeSimulada = new();

    long eventosDeRedeVistos;
    readonly List<string> linhasDeRede = [];
    readonly List<string> linhasDeBluetooth = [];
    string linksMostrados = "";
    string dispositivosMostrados = "";
    string dispositivoBtEscolhido;
    string abaDoPainel = "serial";

    // ---- depurador ----
    // Os pontos de parada sao de cada placa: a linha 12 do sketch da Uno
    // nao e a linha 12 do sketch da ESP32.
    readonly Dictionary<string, SortedSet<int>> pausasPorPlaca = [];
    readonly Dictionary<string, Depurador> depuradores = [];
    MargemDePausas margemDePausas;
    FundoDaPausa fundoDaPausa;
    string placaPausada;
    int linhaPausada = -1;
    string textoDasVariaveis = "";
    Netlist rede;


    /// <summary>
    /// Numero da rodada. A thread guarda o seu e para assim que ele muda.
    /// So o maq.Parar nao bastava: Reiniciar() o zera, e a thread antiga
    /// voltava a rodar junto com a nova, duas mexendo na mesma Maquina.
    /// </summary>
    int geracao;

    readonly DispatcherTimer relogioTela;

    // Desfazer/refazer: pilha de instantaneos do circuito em JSON.
    // Simples e a prova de erro -- o circuito e pequeno.
    readonly List<string> historico = [];
    int posHistorico = -1;
    bool aplicandoHistorico;

    static readonly Brush Cinza = new SolidColorBrush(Color.FromRgb(0x85, 0x85, 0x85));
    static readonly Brush Vermelho = new SolidColorBrush(Color.FromRgb(0xF4, 0x47, 0x47));

    static readonly (string nome, string hex)[] CoresDeFio =
    [
        ("Preto", "#000000"), ("Branco", "#F2F2F2"), ("Cinza", "#8C8C8C"), ("Roxo", "#8A4FBE"),
        ("Azul", "#1668C1"), ("Verde", "#00A651"), ("Amarelo", "#F2C300"), ("Laranja", "#F07C1E"),
        ("Vermelho", "#D6202A"), ("Marrom", "#8B4A1B"),
    ];

    public MainWindow()
    {
        InitializeComponent();

        AjustarAJanelaDaTela();
        MostrarModo("simulacao");

        // Painel que sai leva o espaco dele junto. O aviso vale tambem
        // para o X da janela solta, que nao passa pelos botoes daqui.
        JanelaSolta.AoMudar = _ =>
        {
            AjustarVaos();
            AtualizarAbas();
        };

        LigarConsole();

        // O C/C++ compilado de verdade (CompiladorNativo): na janela, sim; nos
        // testes e no passeio, o interpretador de sempre. O primeiro uso monta
        // o cache do compilador -- aquece em segundo plano, para o primeiro
        // Rodar nao esperar por ele.
        if (!Autoteste.Rodando && !Passeio.Ativo)
        {
            Compilador.CppNativo = true;
            Compilador.AvisoDoCompilador = texto => Dispatcher.BeginInvoke(() => console.Info(texto));
            // A medida de desempenho conta os quadros da janela, nao os do compilador.
            if (!MedirDesempenho.Medindo)
            {
                CompiladorNativo.AquecerEmSegundoPlano(texto => Dispatcher.BeginInvoke(() => console.Info(texto)));
                CompiladorCSharp.AquecerEmSegundoPlano();
                Plugins.AquecerEmSegundoPlano(() => Dispatcher.BeginInvoke(Plugins.RegistrarInstalados));
            }
        }

        // O resumo da visão de circuito acompanha a medida. Vai direto
        // para a barra, sem passar pelo console: ele muda quatro vezes
        // por segundo, e registrar cada uma afogaria o que importa.
        editor.EnergiaMudou += () =>
        {
            if (modoDaTela != "circuito") return;
            MostrarNoStatus(editor.ResumoDaEnergia());
            AtualizarMultimetro();
        };

        // O multimetro (a aba MULTÍMETRO): as pontas, a chave e a ponta do clique esquerdo.
        editor.PontasMudaram += AtualizarMultimetro;
        painelMultimetro.FuncaoMudou += AtualizarMultimetro;
        painelMultimetro.PontaAtivaMudou += () => editor.PretaNoEsquerdo = painelMultimetro.PretaNoEsquerdo;
        // Rodar e parar mudam o que o multimetro le (parado, so o ohmimetro tem o que medir).
        btParar.IsEnabledChanged += (_, _) => { contaParada = null; AtualizarMultimetro(); };

        editor.Mudou += () =>
        {
            if (!aplicandoHistorico) Empilhar();
            ReconstruirRede();
            RefletirDiagrama();
            AtualizarComponentes();
            AtualizarNomeNaBarra();
            contaParada = null;
            AtualizarMultimetro();
        };
        editor.SelecaoMudou += AtualizarDica;
        editor.SelecaoMudou += SincronizarPlacaDoEditor;
        txtCodigo.TextChanged += (_, _) =>
        {
            GuardarCodigoDoEditor();
            AcompanharLinguagem();

            // Mexeu no codigo: o "rodar assim mesmo" de antes nao vale
            // mais, porque o #include pode ser outro agora.
            faltasPerdoadas.Clear();
        };

        // Durante o arraste so refaz a rede -- sem empilhar desfazer.
        editor.Arrastou += ReconstruirRede;
        editor.Arrastou += RefletirDiagrama;
        editor.GiroMudou += GiroMudouNoEditor;
        editor.SelecaoMudou += AtualizarBarraObjeto;
        editor.SelecaoMudou += () => AtualizarComponentes();
        editor.MenuPedido += MontarMenuDoCircuito;
        editor.ResetApertado += id => SegurarReset(id);
        editor.ResetSolto += SoltarReset;
        editor.RedeDoRoteador = redeSimulada;
        // O RESET da barra ligado no GND (por fio ou por um botao): a placa
        // fica parada enquanto ele estiver la. Parada a simulacao, a janela
        // esquece e o editor pergunta de novo quando ela rodar.
        editor.ResetPorFioMudou += (id, preso) =>
        {
            bool rodando = btParar.IsEnabled && programas.ContainsKey(id);
            if (!rodando) { if (preso) editor.EsquecerResetPorFio(id); return; }
            if (preso) SegurarReset(id, editor.PinoQueSegura(id) switch
            {
                "3V3_EN" => "o 3V3_EN está ligado no GND: o regulador desliga e a placa fica apagada até ele sair.",
                { } pino and not "RESET" => $"o {pino.Split('.')[0]} está ligado no GND: a placa fica parada até ele sair.",
                _ => "o RESET está ligado no GND: a placa fica parada até ele sair.",
            });
            else SoltarReset(id);
        };
        editor.PecaSolta += peca =>
            Aviso($"adicionado: {Catalogo.Obter(peca.Tipo)?.Nome}");

        // Realce de sintaxe do Arduino, nao de C++ generico: as palavras
        // que ganham cor sao as que o motor entende.
        txtCodigo.SyntaxHighlighting = Realce.Arduino;
        txtCodigo.Options.HighlightCurrentLine = true;
        txtCodigo.Options.ConvertTabsToSpaces = true;
        txtCodigo.Options.IndentationSize = 2;
        txtCodigo.TextArea.TextView.CurrentLineBackground = Realce.FundoDaLinhaAtual;
        txtCodigo.TextArea.TextView.CurrentLineBorder = null;
        txtCodigo.TextArea.TextView.LinkTextForegroundBrush = Cinza;

        // Configuracoes do editor que dependem de evento: o zoom com a roda
        // e os pares que fecham sozinhos.
        txtCodigo.PreviewMouseWheel += ZoomDoCodigo;
        txtCodigo.TextArea.TextEntering += PassarPorCimaDoPar;
        txtCodigo.TextArea.TextEntered += FecharOPar;
        txtCodigo.TextArea.PreviewKeyDown += EnterEntreChaves;
        MontarDepurador();
        AtualizarAbas();

        txtCodigo.Text = ModeloDeSketch;
        editor.Carregar(CircuitoInicial());
        editor.AplicarGrade();
        AdotarCodigo(ModeloDeSketch);
        Empilhar();
        ReconstruirRede();
        Loaded += (_, _) => editor.Ajustar();

        relogioTela = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(40)   // 25 fps
        };
        relogioTela.Tick += AtualizarTela;
        relogioTela.Start();

        menuOpcoes.Opened += MontarMenusDinamicos;
        PreviewKeyDown += Atalhos;
        Closing += AoFechar;

        // O salvamento automatico olha a cada meio minuto se esta na hora.
        salvador = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        salvador.Tick += (_, _) => SalvarSozinhoSePrecisar();
        salvador.Start();

        Tema.Mudou += AplicarTema;

        AplicarPreferencias();
        AtualizarDica();
        assinaturaInicial = AssinaturaAtual();
    }

    // ================= codigo por placa =================
    //
    // O programa e da placa, nao do arquivo. Com duas placas no mesmo
    // circuito as duas dividiam o mesmo campo de texto, entao nao havia
    // como ter um sketch para cada -- e nem dava para saber de quem era
    // o codigo que estava na tela.
    //
    // Agora o codigo mora em PecaNoCircuito.Codigo e o campo mostra o da
    // placa selecionada, com o nome dela em cima.

    /// <summary>Id da placa cujo codigo esta no campo de texto.</summary>
    string placaDoEditor;

    /// <summary>Codigo de quando nao ha placa nenhuma no circuito.</summary>
    string codigoSemPlaca = "";

    /// <summary>Ligado enquanto o proprio programa troca o texto.</summary>
    bool trocandoCodigo;

    /// <summary>A placa dona do que esta no campo, ou null.</summary>
    PecaNoCircuito PlacaDoEditor()
    {
        var placas = editor.Atual.Placas().ToList();
        return placas.FirstOrDefault(x => x.Id == placaDoEditor) ?? placas.FirstOrDefault();
    }

    /// <summary>
    /// Grava o que esta no campo na placa dona dele.
    ///
    /// Quando a placa dona foi apagada, o texto deixa de ser de alguem e
    /// nao e adotado por outra: colar o codigo da placa apagada em cima
    /// do codigo da que sobrou seria pior do que perde-lo.
    /// </summary>
    void GuardarCodigoDoEditor()
    {
        if (trocandoCodigo) return;

        if (arquivoAberto is not null)
        {
            // O diagrama agora e editavel: o texto VIRA o circuito. Nao
            // na hora, porem -- JSON pela metade e invalido quase o tempo
            // todo enquanto se digita.
            if (arquivoAberto == NomeDoDiagrama) { AgendarDiagrama(); return; }

            editor.Atual.Arquivos ??= new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);
            editor.Atual.Arquivos[arquivoAberto] = txtCodigo.Text;
            return;
        }

        var placas = editor.Atual.Placas().ToList();
        var dona = placas.FirstOrDefault(x => x.Id == placaDoEditor);
        if (dona is not null) dona.Sketch = txtCodigo.Text;
        else if (placas.Count == 0) codigoSemPlaca = txtCodigo.Text;
    }

    const string NomeDoDiagrama = "diagram.json";

    string arquivoAberto;

    /// <summary>
    /// A linguagem do sketch que esta no editor agora.
    ///
    /// Sai do proprio texto, e nao de uma opcao: o aluno cola um
    /// exemplo em MicroPython e o simulador acompanha, sem ele ter que
    /// avisar antes.
    /// </summary>
    Linguagem LinguagemDoEditor()
        => Compilador.Detectar(
            arquivoAberto is null ? txtCodigo.Text : PlacaDoEditor()?.Sketch ?? "");

    string NomeDoSketch()
    {
        var dona = PlacaDoEditor();
        string curto = dona?.Nome;
        if (string.IsNullOrWhiteSpace(curto)) curto = "sketch";
        return curto.Trim() + Compilador.Extensao(LinguagemDoEditor());
    }

    void AtualizarAbasDeArquivo()
    {
        if (abasDeArquivo is null) return;

        var nomes = new List<string> { null, NomeDoDiagrama };
        if (editor.Atual.Arquivos is not null)
            nomes.AddRange(editor.Atual.Arquivos.Keys.OrderBy(
                x => x, StringComparer.OrdinalIgnoreCase));

        if (arquivoAberto is not null && !nomes.Contains(arquivoAberto)) arquivoAberto = null;

        abasDeArquivo.Children.Clear();
        foreach (string nome in nomes)
        {
            var aba = new Button
            {
                Content = nome ?? NomeDoSketch(),
                Tag = nome,
                Style = (Style)FindResource("Aba"),
                Margin = new Thickness(0, 0, 14, 0),
                FontSize = 11,
                Foreground = Tema.Pincel(nome == arquivoAberto ? Tema.TextoForte : Tema.TextoFraco),
                ToolTip = nome is null
                    ? "O programa desta placa"
                    : nome == NomeDoDiagrama
                        ? "O circuito em JSON. Da para editar: o desenho segue o que "
                          + "estiver escrito aqui."
                        : nome,
            };
            aba.Click += (s, _) => AbrirArquivoDoProjeto((string)((Button)s).Tag);
            abasDeArquivo.Children.Add(aba);
        }
    }

    /// <summary>
    /// Aplica o texto do diagram.json ao circuito.
    ///
    /// Com atraso, e nao a cada tecla: enquanto se digita, o JSON fica
    /// invalido quase sempre, e um instante valido no meio do caminho
    /// (uma chave fechada cedo demais) apagaria pecas de verdade. O
    /// atraso curto resolve, e e o mesmo que qualquer editor faz.
    /// </summary>
    System.Windows.Threading.DispatcherTimer relogioDoDiagrama;

    /// <summary>Estamos reescrevendo a aba por causa de uma mudanca no desenho.</summary>
    bool escrevendoDiagrama;

    void AgendarDiagrama()
    {
        if (escrevendoDiagrama) return;

        relogioDoDiagrama ??= NovoRelogioDoDiagrama();
        relogioDoDiagrama.Stop();
        relogioDoDiagrama.Start();
    }

    System.Windows.Threading.DispatcherTimer NovoRelogioDoDiagrama()
    {
        var t = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        t.Tick += (_, _) => { t.Stop(); AplicarDiagrama(); };
        return t;
    }

    /// <summary>
    /// O circuito como diagram.json do Wokwi, com o nome do projeto, o autor
    /// e o editor -- o que a aba mostra e o que o menu Exportar salva.
    /// </summary>
    string DiagramaDoCircuito() => ExportarWokwi.Gerar(editor.Atual, NomeDoProjeto(), AutorDoProjeto());

    /// <summary>O "name": o do projeto, ou o nome do arquivo .sktch.</summary>
    string NomeDoProjeto()
        => editor.Atual.Nome
           ?? (caminhoDoProjeto is not null ? System.IO.Path.GetFileNameWithoutExtension(caminhoDoProjeto) : null)
           ?? "sketch";

    /// <summary>O "author": o do projeto, ou o das configuracoes (o usuario do Windows, se ninguem escreveu outro).</summary>
    string AutorDoProjeto() => editor.Atual.Autor ?? AutorPadrao();

    internal static string AutorPadrao()
    {
        string escrito = Preferencias.Texto(Preferencias.Autor, "").Trim();
        return escrito.Length > 0 ? escrito : Environment.UserName;
    }

    /// <summary>
    /// O texto da aba vira circuito. O formato e o do Wokwi; o formato velho
    /// daqui ("Pecas"/"Fios", de antes da Alpha 24) continua sendo aceito,
    /// para quem colar um circuito guardado assim.
    /// </summary>
    bool LerDiagramaDaAba(string texto, out Circuito novo, out string erro, out int perdidos)
    {
        novo = null;
        erro = null;
        perdidos = 0;
        if (string.IsNullOrWhiteSpace(texto)) { erro = "o arquivo esta vazio"; return false; }

        bool formatoVelho = texto.Contains("\"Pecas\"", StringComparison.Ordinal) && !texto.Contains("\"parts\"", StringComparison.Ordinal);
        if (formatoVelho) return Circuito.TentarDeJson(texto, out novo, out erro);

        try
        {
            var r = ImportarWokwi.Ler(texto, editor.Atual, estrito: true);
            if (r.Erro is not null) { erro = r.Erro; return false; }
            novo = r.Circuito;
            perdidos = r.FiosPerdidos.Count;
            return true;
        }
        catch (System.Text.Json.JsonException e)
        {
            // A mensagem do .NET traz a linha, que e o que a pessoa precisa
            // para achar a virgula que falta.
            string frase = e.Message;
            int ponto = frase.IndexOf(". ", StringComparison.Ordinal);
            if (ponto > 0) frase = frase[..(ponto + 1)];
            erro = e.LineNumber is not null ? $"linha {e.LineNumber + 1}: {frase}" : frase;
            return false;
        }
    }

    void AplicarDiagrama()
    {
        if (arquivoAberto != NomeDoDiagrama) return;

        if (!LerDiagramaDaAba(txtCodigo.Text, out var novo, out string erro, out int perdidos))
        {
            // O circuito na tela fica como estava: melhor um diagrama
            // desatualizado do que um circuito apagado por uma virgula.
            Erro("diagram.json: " + erro);
            return;
        }

        // O codigo das placas nao esta no diagrama por acaso: ele mora
        // no .sktch ao lado. Trazer o de antes evita que salvar o
        // diagrama apague o sketch de quem so mexeu numa coordenada.
        foreach (var placa in novo.Placas())
        {
            if (placa.Codigo is not null) continue;
            var antiga = editor.Atual.Achar(placa.Id);
            if (antiga is not null && antiga.Tipo == placa.Tipo) placa.Codigo = antiga.Codigo;
        }

        novo.Arquivos ??= editor.Atual.Arquivos;

        editor.Carregar(novo);
        bancada.Sincronizar(novo);
        ReconstruirRede();
        AtualizarCabecalhoDaPlaca();
        AtualizarAbasDeArquivo();

        // Entra no historico como qualquer outra mudanca: quem editou o
        // JSON e errou desfaz com Ctrl+Z, igual a quem arrastou a peca
        // para o lugar errado.
        Empilhar();

        int quebrados = novo.FiosQuebrados().Count() + perdidos;
        Aviso(quebrados == 0
            ? $"diagram.json aplicado: {novo.Pecas.Count} peças, {novo.Fios.Count} fios."
            : $"diagram.json aplicado, mas {quebrados} fio(s) apontam para pino que não existe.");
    }

    /// <summary>
    /// O desenho mudou: a aba do diagrama tem que acompanhar.
    ///
    /// Sem isto, arrastar uma peca com a aba aberta deixaria na tela um
    /// JSON que nao descreve mais o circuito -- e a proxima tecla
    /// digitada ali desfaria o arrasto.
    /// </summary>
    void RefletirDiagrama()
    {
        if (arquivoAberto != NomeDoDiagrama) return;

        escrevendoDiagrama = true;
        trocandoCodigo = true;
        try
        {
            int onde = txtCodigo.CaretOffset;
            txtCodigo.Text = DiagramaDoCircuito();
            txtCodigo.CaretOffset = Math.Min(onde, txtCodigo.Text.Length);
        }
        finally
        {
            trocandoCodigo = false;
            escrevendoDiagrama = false;
        }
    }

    /// <summary>
    /// Abre o editor de codigo numa janela propria, ou o traz de volta.
    ///
    /// O painel e o MESMO -- ele muda de lugar, e nao e copiado. Por isso
    /// o que esta digitado continua la, o realce continua valendo e o
    /// depurador continua marcando a linha certa.
    /// </summary>
    void SoltarPainelDoCodigo(object s = null, RoutedEventArgs e = null)
    {
        LembrarMedidas();
        JanelaSolta.Alternar(this, PainelCodigo, NomeDoSketch(), painelDoCodigo);
    }

    /// <summary>O monitor serial, a rede e o Bluetooth numa janela propria.</summary>
    void SoltarPainelDeBaixo(object s = null, RoutedEventArgs e = null)
    {
        LembrarMedidas();
        JanelaSolta.Alternar(this, PainelDeBaixo, "Monitor serial, rede e Bluetooth",
                             painelDeBaixo);
    }

    /// <summary>A lista de componentes numa janela propria.</summary>
    void SoltarComponentes(object s = null, RoutedEventArgs e = null)
        => JanelaSolta.Alternar(this, "componentes", "Componentes", painelComponentes);

    void RolarAbasDeArquivo(object s, MouseWheelEventArgs e)
    {
        rolagemDasAbas.ScrollToHorizontalOffset(
            rolagemDasAbas.HorizontalOffset - e.Delta);
        e.Handled = true;
    }

    void AbrirArquivoDoProjeto(string nome)
    {
        arquivoAberto = nome;

        trocandoCodigo = true;
        try
        {
            txtCodigo.Text = nome is null
                ? (PlacaDoEditor()?.Sketch ?? codigoSemPlaca ?? ModeloEmBranco)
                : nome == NomeDoDiagrama
                    ? DiagramaDoCircuito()
                    : editor.Atual.Arquivos?.GetValueOrDefault(nome) ?? "";
        }
        finally { trocandoCodigo = false; }

        // O diagrama era so leitura, e o professor pediu para poder
        // edita-lo: e o unico jeito de acertar uma coordenada teimosa
        // ou colar um circuito inteiro de outro lugar.
        txtCodigo.IsReadOnly = false;
        txtCodigo.SyntaxHighlighting = RealceDoArquivo(nome);

        AtualizarAbasDeArquivo();
        RedesenharPausas();
    }

    ICSharpCode.AvalonEdit.Highlighting.IHighlightingDefinition RealceDoArquivo(string nome)
    {
        if (nome is null) return Realce.Do(Tema.Atual, LinguagemDoEditor());

        string extensao = System.IO.Path.GetExtension(nome).ToLowerInvariant();
        if (extensao is ".ino" or ".h" or ".hpp" or ".c" or ".cpp" or ".cc")
            return Realce.Do(Tema.Atual, Linguagem.Arduino);
        if (extensao is ".py")
            return Realce.Do(Tema.Atual, Linguagem.MicroPython);

        try
        {
            return ICSharpCode.AvalonEdit.Highlighting.HighlightingManager.Instance
                .GetDefinitionByExtension(extensao);
        }
        catch { return null; }
    }

    void MenuDeArquivos(object s, RoutedEventArgs e)
    {
        bool doUsuario = arquivoAberto is not null && arquivoAberto != NomeDoDiagrama;

        var menu = new ContextMenu
        {
            PlacementTarget = btMenuArquivos,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom,
        };

        void Item(string texto, Action acao, bool ligado = true)
        {
            var item = new MenuItem { Header = texto, IsEnabled = ligado };
            item.Click += (_, _) => acao();
            menu.Items.Add(item);
        }

        Item("Novo arquivo", NovoArquivoDoProjeto);
        Item("Carregar arquivo…", CarregarArquivoDoProjeto);
        Item("Renomear", RenomearArquivoDoProjeto, doUsuario);
        Item("Apagar arquivo", ApagarArquivoDoProjeto, doUsuario);

        menu.IsOpen = true;
    }

    void NovoArquivoDoProjeto()
    {
        string nome = Dialogos.Perguntar(this, "Nome do arquivo novo", "anotacoes.md");
        if (nome is null) return;

        if (!NomeDeArquivoVale(nome)) return;

        editor.Atual.Arquivos ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        editor.Atual.Arquivos[nome] = "";

        Empilhar();
        AbrirArquivoDoProjeto(nome);
    }

    void CarregarArquivoDoProjeto()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Texto (*.txt;*.md;*.h;*.py;*.json;*.csv;*.ino)"
                     + "|*.txt;*.md;*.h;*.py;*.json;*.csv;*.ino|Todos os arquivos|*.*",
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            string nome = System.IO.Path.GetFileName(dlg.FileName);
            if (!NomeDeArquivoVale(nome)) return;

            editor.Atual.Arquivos ??= new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);
            editor.Atual.Arquivos[nome] = File.ReadAllText(dlg.FileName);

            Empilhar();
            AbrirArquivoDoProjeto(nome);
        }
        catch (Exception erro) { Erro(erro.Message); }
    }

    void RenomearArquivoDoProjeto()
    {
        if (arquivoAberto is null || arquivoAberto == NomeDoDiagrama) return;

        string velho = arquivoAberto;
        string novo = Dialogos.Perguntar(this, "Novo nome", velho);
        if (novo is null || novo == velho) return;

        if (!NomeDeArquivoVale(novo)) return;

        string conteudo = editor.Atual.Arquivos.GetValueOrDefault(velho) ?? "";
        editor.Atual.Arquivos.Remove(velho);
        editor.Atual.Arquivos[novo] = conteudo;

        Empilhar();
        AbrirArquivoDoProjeto(novo);
    }

    void ApagarArquivoDoProjeto()
    {
        if (arquivoAberto is null || arquivoAberto == NomeDoDiagrama) return;

        if (MessageBox.Show(this, $"Apagar \"{arquivoAberto}\" do projeto?", "CircuitSketch",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        editor.Atual.Arquivos?.Remove(arquivoAberto);
        if (editor.Atual.Arquivos?.Count == 0) editor.Atual.Arquivos = null;

        Empilhar();
        AbrirArquivoDoProjeto(null);
    }

    bool NomeDeArquivoVale(string nome)
    {
        if (nome.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0)
        {
            Erro($"\"{nome}\" tem caractere que não vale em nome de arquivo.");
            return false;
        }

        if (nome.Equals(NomeDoDiagrama, StringComparison.OrdinalIgnoreCase)
            || nome.Equals(NomeDoSketch(), StringComparison.OrdinalIgnoreCase))
        {
            Erro($"\"{nome}\" é o nome de uma aba que já existe.");
            return false;
        }

        return true;
    }

    /// <summary>
    /// A linguagem que o editor esta pintando agora.
    ///
    /// Guardada para NAO remontar o realce a cada tecla: so quando a
    /// resposta muda e que as cores e a aba trocam.
    /// </summary>
    Linguagem linguagemPintada = Linguagem.Arduino;

    void AcompanharLinguagem()
    {
        if (arquivoAberto is not null) return;

        var agora = Compilador.Detectar(txtCodigo.Text);
        if (agora == linguagemPintada) return;

        linguagemPintada = agora;
        txtCodigo.SyntaxHighlighting = Realce.Do(Tema.Atual, agora);
        AtualizarAbasDeArquivo();
        MostrarLinguagem();
    }

    /// <summary>Diz na barra o que o simulador entendeu.</summary>
    void MostrarLinguagem()
    {
        if (rotuloLinguagem is null) return;
        rotuloLinguagem.Text = Compilador.Nome(linguagemPintada);
    }

    /// <summary>Poe no campo o codigo de quem for o dono agora.</summary>
    void MostrarCodigoDaPlaca()
    {
        var dona = PlacaDoEditor();
        string codigo = (dona is null ? codigoSemPlaca : dona.Sketch) ?? ModeloEmBranco;

        // Placa que ainda nao tinha sketch fica com o modelo, para o
        // arquivo salvo nao ter uma placa sem codigo nenhum. Vale mesmo
        // com outra aba aberta: e estado do projeto, nao da tela.
        if (dona is not null && dona.Codigo is null) dona.Sketch = codigo;

        // Com OUTRA aba aberta (o diagrama, um .md do projeto), trocar de
        // placa nao pode arrastar o editor para o sketch dela: a pessoa
        // esta escrevendo em outro arquivo, e clicar numa peca do desenho
        // jogaria o cursor para fora do que ela estava digitando.
        if (arquivoAberto is not null)
        {
            AtualizarCabecalhoDaPlaca();
            AtualizarAbasDeArquivo();
            return;
        }

        txtCodigo.IsReadOnly = false;

        trocandoCodigo = true;
        try { txtCodigo.Text = codigo; }
        finally { trocandoCodigo = false; }

        // A placa nova pode estar em outra linguagem que a anterior.
        linguagemPintada = Compilador.Detectar(codigo);
        txtCodigo.SyntaxHighlighting = Realce.Do(Tema.Atual, linguagemPintada);
        MostrarLinguagem();

        AtualizarCabecalhoDaPlaca();
        RedesenharPausas();       // os pontos marcados sao da placa nova
    }

    /// <summary>
    /// Selecionar uma placa troca o codigo que aparece. So placa: clicar
    /// num LED nao mexe no campo, senao editar o circuito viraria um
    /// pisca-pisca de codigo.
    /// </summary>
    void SincronizarPlacaDoEditor()
    {
        var placas = editor.Atual.Placas().ToList();
        var escolhida = editor.Selecionada;

        string alvo =
            escolhida is not null && PerfilPlaca.EhPlaca(escolhida.Tipo) ? escolhida.Id
            : placas.Any(x => x.Id == placaDoEditor) ? placaDoEditor
            : placas.FirstOrDefault()?.Id;

        if (alvo != placaDoEditor)
        {
            GuardarCodigoDoEditor();      // o texto atual e da placa antiga
            placaDoEditor = alvo;
            MostrarCodigoDaPlaca();
            ReconstruirRede();            // o perfil segue a placa escolhida
        }
        else AtualizarCabecalhoDaPlaca();
    }

    void AtualizarCabecalhoDaPlaca()
    {
        MostrarBaudDoMonitor();
        var dona = PlacaDoEditor();
        var placas = editor.Atual.Placas().ToList();

        txtPlacaDoCodigo.Text = dona is null
            ? "sem placa no circuito"
            : PerfilPlaca.De(dona.Tipo)?.Nome ?? dona.Tipo;

        int qual = dona is null ? 0 : placas.FindIndex(x => x.Id == dona.Id) + 1;
        txtOutrasPlacas.Text = placas.Count < 2 ? "" : $"placa {qual} de {placas.Count}";
        txtOutrasPlacas.ToolTip = placas.Count < 2
            ? null
            : "Clique na outra placa, no circuito, para ver o código dela";

        AtualizarAbasDeArquivo();
    }

    /// <summary>
    /// Depois de abrir um arquivo: cada placa ja pode trazer o seu
    /// codigo dentro do circuito. Arquivo antigo (e .ino solto) tem um
    /// codigo so, e ele e da primeira placa.
    /// </summary>
    void AdotarCodigo(string codigo)
    {
        var placas = editor.Atual.Placas().ToList();

        if (codigo is not null && placas.All(x => x.Codigo is null))
        {
            if (placas.Count > 0) placas[0].Sketch = codigo;
            else codigoSemPlaca = codigo;
        }

        placaDoEditor = placas.FirstOrDefault()?.Id;
        MostrarCodigoDaPlaca();
    }

    /// <summary>
    /// O codigo que vai no campo "codigo" do .sktch. E o da primeira
    /// placa, sempre a mesma, para a assinatura de "tem alteracao nao
    /// salva" nao mudar so porque a pessoa clicou na outra placa.
    /// </summary>
    string CodigoPrincipal()
        => editor.Atual.Placas().FirstOrDefault()?.Sketch ?? codigoSemPlaca;

    string AssinaturaAtual()
    {
        GuardarCodigoDoEditor();

        // A descricao entra na assinatura: escrever tres paragrafos e
        // fechar sem salvar tem que perguntar, como mexer no circuito.
        return Sktch.Assinatura(editor.Atual, CodigoPrincipal())
               + "|" + txtDescricao.Text;
    }

    /// <summary>
    /// Compara o circuito com o ultimo estado salvo. Fechar sem avisar
    /// jogaria fora um circuito montado a mao.
    /// </summary>
    string circuitoSalvo;

    /// <summary>Como o projeto estava ao abrir: o que nao mudou nao avisa.</summary>
    string assinaturaInicial;

    void AoFechar(object s, System.ComponentModel.CancelEventArgs e)
    {
        // Nem o autoteste nem o passeio perguntam: o passeio abre demos e
        // mexe no analisador, e a pergunta de "salvar antes de sair" ficava
        // na tela sem ninguem para responder -- o processo nao terminava.
        if (!Autoteste.Rodando && !Passeio.Ativo
            && AssinaturaAtual() != (circuitoSalvo ?? assinaturaInicial))
        {
            var r = MessageBox.Show(this,
                "O projeto tem alterações não salvas.\n\nSalvar antes de sair?",
                "Alterações não salvas",
                MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);

            if (r == MessageBoxResult.Cancel) { e.Cancel = true; return; }
            if (r == MessageBoxResult.Yes)
            {
                SalvarCircuito();
                // Cancelou o diálogo de salvar: não fecha sem querer.
                if (AssinaturaAtual() != circuitoSalvo)
                { e.Cancel = true; return; }
            }
        }

        // Os paineis soltos voltam para a janela: fechar o programa com
        // uma janela filha aberta deixaria o WPF sem encerrar.
        JanelaSolta.FecharTodas();

        GuardarJanela();
        bancada.Parar();
        buzina.Dispose();
        falante.Dispose();
    }

    // ================= circuito inicial =================
    /// <summary>Semaforo montado: protoboard, ESP32, 3 LEDs com resistor.</summary>
    /// <summary>O circuito de abertura, para o autoteste conferir.</summary>
    internal static Circuito CircuitoDeAbertura() => CircuitoInicial();

    static Circuito CircuitoInicial()
    {
        var c = new Circuito();

        var placa = new PecaNoCircuito { Tipo = "esp32-devkit-v1", X = 40, Y = 120 };
        var board = new PecaNoCircuito { Tipo = Catalogo.IdProtoboard, X = 260, Y = 140 };
        c.Pecas.Add(placa);
        c.Pecas.Add(board);

        var defBoard = Catalogo.Obter(board.Tipo);
        var defLed = Catalogo.Obter("led");
        var defRes = Catalogo.Obter("resistor");

        // Posicao absoluta de um furo pelo nome.
        Point Furo(string nome)
        {
            var f = defBoard.Pinos.First(x => x.Nome == nome);
            return new Point(board.X + f.X, board.Y + f.Y);
        }

        // Coloca a peca de forma que o pino indicado caia dentro do furo.
        void Espetar(PecaNoCircuito peca, PecaDef def, string pino, string furo)
        {
            var pd = def.Pinos.First(x => x.Nome == pino);
            var alvo = Furo(furo);
            peca.X = alvo.X - pd.X;
            peca.Y = alvo.Y - pd.Y;
        }

        var cores = new[] { "vermelho", "amarelo", "verde" };
        // RX2 e o GPIO 16 no header da DevKit V1: nao existe pino
        // chamado "D16". Com o nome errado o fio ficava invalido e sumia
        // do desenho sem avisar -- o LED verde aparecia sem ligacao.
        var gpios = new[] { "D14", "D13", "RX2" };
        var fioCor = new[] { "#D6202A", "#F2C300", "#00A651" };

        for (int i = 0; i < 3; i++)
        {
            int col = 4 + i * 8;

            // LED espetado: anodo na coluna col, catodo na coluna ao lado.
            var led = new PecaNoCircuito { Tipo = "led", Cor = cores[i] };
            Espetar(led, defLed, "A", $"B{col}");
            c.Pecas.Add(led);

            // Resistor da coluna do catodo ate uma coluna mais adiante.
            var res = new PecaNoCircuito { Tipo = "resistor", Ohms = 220 };
            Espetar(res, defRes, "1", $"D{col - 1}");
            c.Pecas.Add(res);

            // Jumper do GPIO ate a coluna do anodo.
            c.Fios.Add(new Fio
            {
                DePeca = placa.Id, DePino = gpios[i],
                ParaPeca = board.Id, ParaPino = $"A{col}",
                Cor = fioCor[i]
            });

            // Da outra ponta do resistor para o trilho negativo.
            c.Fios.Add(new Fio
            {
                DePeca = board.Id, DePino = $"E{col + 5}",
                ParaPeca = board.Id, ParaPino = $"B-{col + 5}",
                Cor = "#000000"
            });
        }

        // Terra da placa no trilho negativo.
        c.Fios.Add(new Fio
        {
            DePeca = placa.Id, DePino = "GND.1",
            ParaPeca = board.Id, ParaPino = "B-2",
            Cor = "#000000"
        });

        return c;
    }

    // ================= paleta de cores =================
    string modoDaTela = "simulacao";

    internal string ModoDaTela => modoDaTela;

    void TrocarModo(object s, RoutedEventArgs e)
        => MostrarModo((string)((Button)s).Tag);

    internal void MostrarModo(string nome)
    {
        modoDaTela = nome;

        painelDescricao.Visibility = nome == "descricao"
            ? Visibility.Visible : Visibility.Collapsed;

        painelComponentes.Visibility = nome == "componentes"
            ? Visibility.Visible : Visibility.Collapsed;

        editor.VisaoDeEnergia = nome == "circuito";
        editor.SoMedir = nome == "circuito";
        painelMultimetro.Visibility = nome == "circuito" ? Visibility.Visible : Visibility.Collapsed;
        if (nome != "circuito") painelMultimetro.Silenciar();
        contaParada = null;
        editor.AplicarGrade();

        foreach (var (botao, qual) in new[]
                 { (btModoSimulacao, "simulacao"), (btModoCircuito, "circuito"),
                   (btModoComponentes, "componentes"), (btModoDescricao, "descricao") })
        {
            bool ativo = qual == nome;
            botao.Foreground = Tema.Pincel(ativo ? Tema.TextoForte : Tema.TextoFraco);
            botao.Background = ativo ? Tema.Pincel(Tema.Borda) : Brushes.Transparent;
        }

        if (nome == "circuito") { Aviso(editor.ResumoDaEnergia()); AtualizarMultimetro(); }
        else if (nome == "descricao") txtDescricao.Focus();
        else if (nome == "componentes") AtualizarComponentes(forcar: true);

        // Saindo do multímetro, o resumo dele não vale mais nada
        // -- e ficava na barra, falando de cores que já não estavam na
        // tela. Volta o estado da simulação no lugar.
        if (nome != "circuito" && txtStatus.Text.StartsWith("multímetro"))
            MostrarNoStatus(editor.EmSimulacao ? "rodando..." : "parado.");
    }

    /// <summary>A conta de energia do circuito parado, para o ohmimetro (refeita quando o circuito muda).</summary>
    Energia contaParada;

    /// <summary>
    /// A leitura do multimetro: a conta da simulacao (ou, parado, a do circuito sem
    /// energia, que basta ao ohmimetro), as pontas do editor e a chave do painel.
    /// </summary>
    void AtualizarMultimetro()
    {
        if (modoDaTela != "circuito" || painelMultimetro is null) return;
        bool rodando = editor.EmSimulacao;
        Energia conta;
        if (rodando) conta = editor.EnergiaAtual;
        else
        {
            try { conta = contaParada ??= editor.CalcularEnergia(bancada, rede); }
            catch { conta = null; }
        }
        var leitura = Multimetro.Medir(painelMultimetro.Funcao, conta, editor.PontaVermelha, editor.PontaPreta, rodando, editor.Atual);
        painelMultimetro.Mostrar(leitura, editor.PontaVermelha?.Rotulo, editor.PontaPreta?.Rotulo);
        editor.TextoDaLeitura = leitura.Curta;
    }

    /// <summary>Para o teste: o multimetro agora.</summary>
    internal PainelMultimetro TesteMultimetro => painelMultimetro;

    /// <summary>Para o teste: refaz a leitura agora (sem esperar a proxima conta de energia).</summary>
    internal LeituraDoMultimetro TesteMedir()
    {
        AtualizarMultimetro();
        return painelMultimetro.UltimaLeitura;
    }

    /// <summary>
    /// Troca o texto da barra SEM registrar no console.
    ///
    /// Para o que muda sozinho, várias vezes por segundo, e não é um
    /// acontecimento: o resumo da energia, o estado da simulação. O
    /// Aviso registra, e é para o que a pessoa vai querer reler depois.
    /// </summary>
    void MostrarNoStatus(string texto)
    {
        txtStatus.Foreground = Cinza;
        txtStatus.Text = texto;
        AjustarVaos();
    }

    // ================= historico =================
    void Empilhar()
    {
        string estado = editor.Atual.ParaJson();
        if (posHistorico >= 0 && posHistorico < historico.Count
            && historico[posHistorico] == estado) return;

        // Uma acao nova descarta o "refazer" pendente.
        if (posHistorico < historico.Count - 1)
            historico.RemoveRange(posHistorico + 1, historico.Count - posHistorico - 1);

        historico.Add(estado);
        if (historico.Count > 100) historico.RemoveAt(0);
        posHistorico = historico.Count - 1;
    }

    /// <summary>
    /// Projeto novo na tela: nada do anterior pode sobrar.
    ///
    /// Alpha 19: "ao trocar de arquivo ... deve resetar os estados, o
    /// monitor serial, console e etc para o novo arquivo não bagunçar".
    /// Cada caminho de abrir limpava uma coisa diferente, e a demo nem
    /// parava a simulação: o circuito novo aparecia com as placas do
    /// anterior ainda rodando por baixo, escrevendo no monitor e na rede.
    ///
    /// Todos passam por aqui ANTES de trocar o circuito. Parar depois
    /// guardaria o cartão SD da placa velha dentro da peça nova.
    /// </summary>
    void RecomecarSessao(string oQueAbriu)
    {
        PararExecucao();

        // Monitor serial: o que a placa anterior imprimiu, o que ficou
        // digitado para ela, e o espelho que a Arduino IDE acompanha.
        painelSerial.Clear();
        RedesenharSerial();
        serialPorPlaca.Clear();
        avisosPorPlaca.Clear();
        txtEntradaSerial.Clear();
        MonitorIde.Comecar();

        // Rede e Bluetooth. O que a rede anotou e ainda não apareceu é
        // dado por visto: senão sairia no painel do projeto novo.
        redeSimulada.Novos(ref eventosDeRedeVistos);
        linhasDeRede.Clear();
        linhasDeBluetooth.Clear();
        txtRede.Text = "";
        txtBluetooth.Text = "";
        txtEntradaBt.Clear();
        dispositivoBtEscolhido = null;
        redeSimulada.Broker.EsquecerRetidas();
        AtualizarLinksDeRede();

        // Depurador: as linhas marcadas eram do código anterior, e numa
        // linha qualquer do código novo parariam a placa sem motivo.
        pausasPorPlaca.Clear();
        RedesenharPausas();

        // Marcas e avisos do circuito anterior.
        editor.ApagarQueimaduras();
        faltasPerdoadas.Clear();
        avisosDeLigacao.Clear();
        queimadosNaRodada.Clear();
        servidoresAbertos.Clear();
        avisouDoRitmo = false;

        // O console recomeça dizendo o que abriu -- é a primeira pergunta
        // de quem olha para ele depois de trocar de projeto.
        console.Limpar();
        if (oQueAbriu is not null) console.Info(oQueAbriu);
    }

    /// <summary>
    /// Projeto novo, histórico novo.
    ///
    /// Abrir um arquivo, uma demo ou começar um circuito novo troca o
    /// projeto inteiro -- circuito, código e título. Se o histórico
    /// continuasse, o Ctrl+Z passaria do começo do projeto aberto e
    /// traria de volta o CIRCUITO do anterior, com o CÓDIGO do novo: uma
    /// mistura que não existe em arquivo nenhum, e que salvar gravaria
    /// por cima do projeto certo.
    /// </summary>
    void RecomecarHistorico()
    {
        historico.Clear();
        posHistorico = -1;
        Empilhar();
    }

    void Desfazer(object s = null, RoutedEventArgs e = null)
    {
        if (posHistorico <= 0) { Aviso("nada para desfazer"); return; }
        posHistorico--;
        AplicarDoHistorico();
    }

    void Refazer(object s = null, RoutedEventArgs e = null)
    {
        if (posHistorico >= historico.Count - 1) { Aviso("nada para refazer"); return; }
        posHistorico++;
        AplicarDoHistorico();
    }

    void AplicarDoHistorico()
    {
        aplicandoHistorico = true;
        editor.Carregar(Circuito.DeJson(historico[posHistorico]));
        editor.AplicarGrade();
        ReconstruirRede();
        aplicandoHistorico = false;
        AbrirArquivoDoProjeto(null);
        Aviso($"histórico {posHistorico + 1}/{historico.Count}");
    }

    // ================= comandos da barra =================
    void AbrirMenu(object s, RoutedEventArgs e)
    {
        btMenu.ContextMenu.PlacementTarget = btMenu;
        btMenu.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        btMenu.ContextMenu.IsOpen = true;
    }

    /// <summary>A paleta aberta; uma so, para nao encher a tela.</summary>
    JanelaPecas paleta;

    void AdicionarPeca(object s = null, RoutedEventArgs e = null)
    {
        // Aberta em vez de modal: dialogo modal desabilita esta janela, e
        // janela desabilitada nao aceita peca arrastada.
        if (paleta is not null) { paleta.Activate(); return; }

        // No multimetro nao se poe peca: adicionar volta para a SIMULAÇÃO.
        if (modoDaTela == "circuito") MostrarModo("simulacao");

        paleta = new JanelaPecas(this)
        {
            AoEscolher = tipo =>
            {
                editor.Adicionar(tipo);
                Aviso($"adicionado: {Catalogo.Obter(tipo)?.Nome}");
            }
        };
        paleta.Closed += (_, _) => paleta = null;
        paleta.Show();
    }

    void Girar(object s = null, RoutedEventArgs e = null) => editor.Girar();
    void Espelhar(object s = null, RoutedEventArgs e = null) => editor.Espelhar();

    void LimparSelecao(object s = null, RoutedEventArgs e = null) => editor.LimparSelecao();

    /// <summary>Nome, rotulo curto e explicacao de cada tracado.</summary>
    static readonly (string id, string curto, string dica)[] EstilosDeFio =
    [
        (Fio.EstiloSolto, "solto", "Curva que cai entre as pontas"),
        (Fio.EstiloReto, "reto", "Linha direta de ponta a ponta"),
        (Fio.EstiloDegrau, "degrau", "Traçado em L, como no Wokwi"),
    ];

    /// <summary>
    /// Recentes e demos mudam durante o uso, entao sao montados na hora
    /// de abrir o menu em vez de ficarem fixos no XAML.
    /// </summary>
    void MontarMenusDinamicos(object s = null, RoutedEventArgs e = null)
    {
        MontarExportarDosPlugins();
        miRecentes.Items.Clear();
        var lista = Recentes.Ler();

        if (lista.Count == 0)
        {
            miRecentes.Items.Add(new MenuItem
            {
                Header = "nenhum ainda",
                IsEnabled = false
            });
        }
        else
        {
            foreach (string caminho in lista)
            {
                var item = new MenuItem
                {
                    Header = System.IO.Path.GetFileName(caminho),
                    ToolTip = caminho,
                    Tag = caminho
                };
                item.Click += (s2, _) => AbrirProjetoDe((string)((MenuItem)s2).Tag);
                miRecentes.Items.Add(item);
            }

            miRecentes.Items.Add(new Separator());
            var limpar = new MenuItem { Header = "Limpar a lista" };
            limpar.Click += (_, _) => { Recentes.Limpar(); Aviso("lista de recentes limpa"); };
            miRecentes.Items.Add(limpar);
        }

        if (miDemos.Items.Count > 0) return;      // as demos nao mudam

        // Familia, depois placa: um submenu por familia e as linguagens
        // juntas no fim. Dentro da familia, a placa com uma demo so vira
        // item direto ("ESP-01 — Tomada inteligente por MQTT"); a que tem
        // varias ganha o seu submenu, e assim se acha qualquer demo pelo
        // nome da placa que se tem na mao.
        foreach (var grupo in Demos.EmGrupos())
        {
            var familia = new MenuItem { Header = $"{grupo.Key}  ({grupo.Count()})" };

            if (grupo.Key == Demos.GrupoDasLinguagens)
            {
                foreach (var demo in grupo) familia.Items.Add(ItemDaDemo(demo, demo.Nome));
            }
            else
            {
                foreach (var daPlaca in Demos.PorPlaca(grupo))
                {
                    if (daPlaca.Count() == 1)
                    {
                        var unica = daPlaca.First();
                        familia.Items.Add(ItemDaDemo(unica, $"{unica.Placa} — {unica.Nome}"));
                        continue;
                    }

                    var placa = new MenuItem { Header = $"{daPlaca.Key}  ({daPlaca.Count()})" };
                    foreach (var demo in daPlaca) placa.Items.Add(ItemDaDemo(demo, demo.Nome));
                    familia.Items.Add(placa);
                }
            }

            miDemos.Items.Add(familia);
        }
    }

    MenuItem ItemDaDemo(Demos.Demo demo, string titulo)
    {
        var item = new MenuItem { Header = titulo, Tag = demo.Recurso };
        item.Click += (s2, _) => AbrirDemo((string)((MenuItem)s2).Tag);
        return item;
    }

    /// <summary>
    /// Abre uma demo embutida. Ela nao tem caminho em disco, entao NAO
    /// entra nos recentes e nao conta como projeto salvo -- salvar pede
    /// um lugar novo, e assim o aluno nunca sobrescreve o exemplo.
    /// </summary>
    void AbrirDemo(string recurso)
    {
        string texto = Demos.Texto(recurso);
        if (texto is null) { Erro($"demo '{recurso}' não veio no programa"); return; }

        try
        {
            string temporario = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), $"circuitsketch-demo-{recurso}.sktch");
            File.WriteAllText(temporario, texto);

            var (circuito, codigo, descricao) = Sktch.AbrirCompleto(temporario);
            File.Delete(temporario);

            var demo = Demos.Todas.First(d => d.Recurso == recurso);
            RecomecarSessao($"exemplo aberto: {demo.Placa} — {demo.Nome}");

            // O exemplo nao tem arquivo: o nome dele (na barra e no diagram.json) e o do exemplo.
            circuito.Nome ??= $"{demo.Placa} — {demo.Nome}";
            editor.Carregar(circuito);
            editor.RecarregarChips();
            editor.AplicarGrade();

            // O código vai PARA A PLACA, como ao abrir um arquivo. Antes ele
            // só ia para o editor: a placa do editor continuava sendo a do
            // projeto anterior, e se o id dela não existisse na demo o
            // código ficava na tela sem dono -- e o Rodar rodava um sketch
            // vazio, sem erro nenhum.
            AdotarCodigo(codigo);
            txtDescricao.Text = descricao ?? "";

            circuitoSalvo = null;                 // e um exemplo, nao um arquivo
            RecomecarHistorico();
            ReconstruirRede();
            editor.Ajustar();

            Title = $"CircuitSketch - demo: {demo.Placa} {demo.Nome}";
            Aviso($"demo carregada: {demo.Placa} — {demo.Nome}");

            // A demo aberta e nao mexida nao pede para ser salva ao fechar --
            // pedia, porque a comparacao era com o circuito de ANTES dela.
            assinaturaInicial = AssinaturaAtual();
            caminhoDoProjeto = null;

            if (Preferencias.Ligado(Preferencias.RodarAoAbrirDemo, false))
                Dispatcher.BeginInvoke(() => Rodar(), DispatcherPriority.Background);
        }
        catch (Exception ex) { Erro($"não deu para abrir a demo: {ex.Message}"); }
    }

    void EstiloDeTodosOsFios(object s, RoutedEventArgs e)
    {
        string estilo = (string)((MenuItem)s).Tag;
        editor.DefinirEstiloDeTodos(estilo);
        MarcarEstiloNoMenu(estilo);
        Preferencias.Definir(Preferencias.EstiloDoFio, estilo);
        Aviso($"todos os fios: {EstilosDeFio.First(x => x.id == estilo).curto}");
    }

    void MarcarEstiloNoMenu(string estilo)
    {
        miFioSolto.IsChecked = estilo == Fio.EstiloSolto;
        miFioReto.IsChecked = estilo == Fio.EstiloReto;
        miFioDegrau.IsChecked = estilo == Fio.EstiloDegrau;
    }

    /// <summary>Tab do fio: cor, estilo do traçado e otimização.</summary>
    void MontarBarraDoFio()
    {
        barraObjeto.Visibility = Visibility.Visible;
        int quantos = editor.FiosSelecionados.Count;
        txtObjeto.Text = quantos > 1 ? $"{quantos} fios" : "Fio";
        opcoesObjeto.Children.Clear();

        // Com varios selecionados, a cor e o traçado valem para todos; a
        // marca do que esta ativo so faz sentido quando e um so.
        var atual = editor.FioAtual;

        foreach (var (nome, hex) in CoresDeFio)
        {
            var cor = (Color)ColorConverter.ConvertFromString(hex);
            var quadro = new System.Windows.Shapes.Rectangle
            {
                Width = 18,
                Height = 18,
                RadiusX = 3,
                RadiusY = 3,
                Margin = new Thickness(0, 0, 4, 0),
                Fill = new SolidColorBrush(cor),
                Stroke = atual?.Cor == hex ? Brushes.White
                                           : new SolidColorBrush(Color.FromRgb(0x5A, 0x5A, 0x5E)),
                StrokeThickness = atual?.Cor == hex ? 2.5 : 1,
                Cursor = Cursors.Hand,
                ToolTip = nome,
                Tag = hex
            };
            quadro.MouseLeftButtonDown += (s2, _) =>
            {
                string escolhida = (string)((System.Windows.Shapes.Rectangle)s2).Tag;
                editor.DefinirCorDoFio(escolhida);
                editor.CorDoFio = escolhida;
                Preferencias.Definir(Preferencias.CorDoFio, escolhida);
                AtualizarBarraObjeto();
            };
            opcoesObjeto.Children.Add(quadro);
        }

        opcoesObjeto.Children.Add(new Border
        {
            Width = 1,
            Background = new SolidColorBrush(Color.FromRgb(0x4A, 0x4A, 0x4E)),
            Margin = new Thickness(6, 2, 6, 2)
        });

        foreach (var (id, curto, dica) in EstilosDeFio)
        {
            bool marcado = atual?.Estilo == id;
            var botao = new Button
            {
                Style = (Style)FindResource("AcaoObj"),
                Content = curto,
                ToolTip = dica,
                Tag = id,
                FontWeight = marcado ? FontWeights.SemiBold : FontWeights.Normal,
                Background = marcado
                    ? new SolidColorBrush(Color.FromRgb(0x16, 0x68, 0xC1))
                    : new SolidColorBrush(Color.FromRgb(0x3C, 0x3C, 0x3C)),
                BorderBrush = marcado
                    ? Brushes.White : new SolidColorBrush(Color.FromRgb(0x5A, 0x5A, 0x5E))
            };
            botao.Click += (s2, _) =>
            {
                editor.DefinirEstiloDosFios((string)((Button)s2).Tag);
                AtualizarBarraObjeto();
            };
            opcoesObjeto.Children.Add(botao);
        }

        opcoesObjeto.Children.Add(new Border
        {
            Width = 1,
            Background = new SolidColorBrush(Color.FromRgb(0x4A, 0x4A, 0x4E)),
            Margin = new Thickness(6, 2, 6, 2)
        });

        var endireitar = new Button
        {
            Style = (Style)FindResource("AcaoObj"),
            Content = "endireitar",
            ToolTip = "Desfaz todos os cantos do fio"
        };
        endireitar.Click += (_, _) => { editor.EndireitarFios(); AtualizarBarraObjeto(); };
        opcoesObjeto.Children.Add(endireitar);

        var otimizar = new Button
        {
            Style = (Style)FindResource("AcaoObj"),
            Content = "otimizar",
            ToolTip = "Remove fios repetidos e os que não mudam nada no circuito"
        };
        otimizar.Click += (_, _) => Otimizar();
        opcoesObjeto.Children.Add(otimizar);
    }

    (PecaNoCircuito peca, Slider barra, TextBlock leitura)? potEmEdicao;
    (PecaNoCircuito peca, CheckBox caixa)? pendriveEmEdicao;
    (PecaNoCircuito peca, TextBlock texto)? usbEmEdicao;
    (PecaNoCircuito peca, TextBlock texto)? reguladorEmEdicao;

    /// <summary>Reflete no slider e na leitura sem remontar a tab.</summary>
    void GiroMudouNoEditor(PecaNoCircuito peca)
    {
        if (pendriveEmEdicao is { } pendrive && pendrive.peca == peca) pendrive.caixa.IsChecked = peca.Giro < 0.5;
        if (potEmEdicao is not { } alvo || alvo.peca != peca) return;
        if (Math.Abs(alvo.barra.Value - peca.Giro) > 1e-9) alvo.barra.Value = peca.Giro;
        alvo.leitura.Text = LeituraDoPot(peca);
    }

    string LeituraDoEixo(double giro)
        => $"{(int)Math.Round(giro * maq.Perfil.MaxAdc)}";

    string LeituraDoPot(PecaNoCircuito peca)
    {
        if (peca.Tipo == "ky-040") return $"passo {(int)Math.Round(peca.Giro * 40)} / 40";

        // Sensor com grandeza de verdade mostra a grandeza -- "24,0 °C",
        // "3.162 lux", "120 cm" --, e nao a leitura crua do conversor, que
        // nao dizia nada a quem mexia no controle de um DHT22.
        if (Comportamento.De(peca.Tipo) is { Unidade.Length: > 0 } comp)
            return FormatarGrandeza(comp.ValorReal(peca.Giro), comp.Unidade);

        return $"{(int)Math.Round(peca.Giro * maq.Perfil.MaxAdc)} / {maq.Perfil.MaxAdc}";
    }

    static string FormatarGrandeza(double valor, string unidade) => unidade switch
    {
        "lux" => valor < 10 ? $"{valor:0.0} lux" : $"{valor:N0} lux",
        "°C" or "C" => $"{valor:0.0} °C",
        _ => $"{valor:0} {unidade}",
    };

    void Otimizar(object s = null, RoutedEventArgs e = null)
    {
        int tirados = editor.OtimizarFios();
        Aviso(tirados == 0
            ? "nenhum fio sobrando"
            : $"{tirados} fio(s) removido(s) por não mudarem o circuito");
    }

    // ---------------- tab do objeto selecionado ----------------
    static readonly (string id, string nome, string hex)[] CoresDeLed =
    [
        ("vermelho", "Vermelho", "#E53935"), ("amarelo", "Amarelo", "#FDD835"),
        ("verde", "Verde", "#43A047"), ("azul", "Azul", "#1E88E5"),
        ("branco", "Branco", "#FAFAFA"), ("laranja", "Laranja", "#F57C00"),
        ("roxo", "Roxo", "#8E24AA"),
    ];

    /// <summary>
    /// Cada peca selecionada abre a sua propria tab: cor no LED,
    /// resistencia no resistor, e girar/espelhar/excluir em todas.
    /// </summary>
    /// <summary>A janela grande do analisador logico desta peca.</summary>
    JanelaAnalisador AbrirAnalisador(PecaNoCircuito peca)
    {
        var janela = new JanelaAnalisador(this, peca, () => Analisador.Fontes(editor.Atual, rede, bancada, peca));
        janela.Show();
        return janela;
    }

    /// <summary>Para o teste: abre o analisador desta peca, como o botao da barra.</summary>
    internal JanelaAnalisador TesteAbrirAnalisador(string idDaPeca)
        => AbrirAnalisador(editor.Atual.Pecas.First(p => p.Id == idDaPeca));

    void AtualizarBarraObjeto()
    {
        int quantas = editor.QuantidadeSelecionada;

        // Fio selecionado tem a sua propria tab.
        if (editor.FioAtual is not null) { MontarBarraDoFio(); return; }

        if (quantas == 0)
        {
            barraObjeto.Visibility = Visibility.Collapsed;
            return;
        }

        barraObjeto.Visibility = Visibility.Visible;
        opcoesObjeto.Children.Clear();
        potEmEdicao = null;
        pendriveEmEdicao = null;
        usbEmEdicao = null;
        reguladorEmEdicao = null;

        // Varias peças: só as ações que fazem sentido em grupo.
        if (quantas > 1)
        {
            txtObjeto.Text = $"{quantas} peças";
            opcoesObjeto.Children.Add(new TextBlock
            {
                Text = "girar, espelhar, copiar e excluir valem para todas",
                Foreground = new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0x9A)),
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center
            });
            return;
        }

        var peca = editor.Selecionada;
        var def = peca is null ? null : Catalogo.Obter(peca.Tipo);
        if (def is null) { barraObjeto.Visibility = Visibility.Collapsed; return; }

        txtObjeto.Text = def.Nome;

        // Nome da peca: aparece embaixo dela no circuito.
        var campoNome = new TextBox
        {
            Width = 130,
            Margin = new Thickness(0, 0, 12, 0),
            Style = (Style)FindResource("CampoEscuro"),
            Text = peca.Nome ?? "",
            ToolTip = "Nome da peça, escrito embaixo dela no circuito",
            VerticalAlignment = VerticalAlignment.Center,
        };
        campoNome.TextChanged += (_, _) => editor.DefinirNome(peca, campoNome.Text, avisar: false);
        campoNome.LostFocus += (_, _) => editor.DefinirNome(peca, campoNome.Text);
        // O Texto e a anotacao inteira: campo mais largo, e Shift+Enter quebra a linha.
        if (peca.Tipo == "texto")
        {
            campoNome.Width = 240;
            campoNome.AcceptsReturn = true;
            campoNome.MaxLines = 3;
            campoNome.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            campoNome.ToolTip = "O texto da anotação. Shift+Enter quebra a linha.";
        }
        campoNome.PreviewKeyDown += (_, ev) =>
        {
            if (ev.Key != Key.Enter || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) return;
            editor.DefinirNome(peca, campoNome.Text);
            Keyboard.ClearFocus();
            ev.Handled = true;
        };

        opcoesObjeto.Children.Add(new TextBlock
        {
            Text = "Nome",
            Foreground = Cinza,
            FontSize = 11,
            Margin = new Thickness(0, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center,
        });
        opcoesObjeto.Children.Add(campoNome);

        if (PerfilPlaca.EhPlaca(peca.Tipo)) MontarBarraDaPlaca(peca);

        if (peca.Tipo == "wifi-ap")
        {
            opcoesObjeto.Children.Add(new TextBlock
            {
                Text = "Rede",
                Foreground = Cinza,
                FontSize = 11,
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center,
            });
            opcoesObjeto.Children.Add(CampoDoPonto(peca, senha: false));

            opcoesObjeto.Children.Add(new TextBlock
            {
                Text = "Senha",
                Foreground = Cinza,
                FontSize = 11,
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center,
            });
            opcoesObjeto.Children.Add(CampoDoPonto(peca, senha: true));
        }

        // O analisador: a escala da tela da peca e a janela grande.
        if (peca.Tipo == "logic-analyzer")
        {
            var escala = new Button
            {
                Content = $"Escala: {Analisador.Tempo(Analisador.JanelaDe(peca))} ▾",
                Style = (Style)FindResource("BotaoPequeno"),
                Margin = new Thickness(0, 0, 6, 0),
                ToolTip = "Quanto tempo cabe na tela da peça",
                VerticalAlignment = VerticalAlignment.Center,
            };
            escala.Click += (_, _) =>
            {
                var menu = new ContextMenu { PlacementTarget = escala };
                foreach (double us in Analisador.Escalas)
                {
                    var item = new MenuItem
                    {
                        Header = Analisador.Tempo(us),
                        IsChecked = Math.Abs(Analisador.JanelaDe(peca) - us) < 0.5,
                    };
                    item.Click += (_, _) =>
                    {
                        peca.JanelaUs = us;
                        editor.AvisarQueMudou();
                        AtualizarBarraObjeto();
                    };
                    menu.Items.Add(item);
                }
                menu.IsOpen = true;
            };
            opcoesObjeto.Children.Add(escala);

            var abrirAnalisador = new Button
            {
                Content = "Abrir o analisador",
                Style = (Style)FindResource("BotaoPequeno"),
                Margin = new Thickness(0, 0, 12, 0),
                ToolTip = "Os oito canais grandes: zoom, pausa, medida no mouse e a serial "
                          + "escrita em cima do fio. Salva .vcd para o PulseView.",
                VerticalAlignment = VerticalAlignment.Center,
            };
            abrirAnalisador.Click += (_, _) => AbrirAnalisador(peca);
            opcoesObjeto.Children.Add(abrirAnalisador);
        }

        if (peca.Tipo is "microsd-card" or "pendrive-usb")
        {
            int quantos = peca.Arquivos?.Count ?? 0;
            var arquivosDoCartao = new Button
            {
                Content = peca.PastaDoComputador is not null ? "Arquivos (pasta)…" : quantos == 0 ? "Arquivos…" : $"Arquivos ({quantos})",
                Style = (Style)FindResource("BotaoPequeno"),
                Margin = new Thickness(0, 0, 12, 0),
                ToolTip = peca.Tipo == "pendrive-usb" ? "O que está gravado neste pendrive" : "O que está gravado neste cartão",
                VerticalAlignment = VerticalAlignment.Center,
            };
            arquivosDoCartao.Click += (_, _) => AbrirCartao(peca);
            opcoesObjeto.Children.Add(arquivosDoCartao);
        }

        if (peca.Tipo == "pendrive-usb")
        {
            // O mesmo que clicar no pendrive do desenho: com a simulacao
            // rodando, o sketch ve o pendrive sair e voltar.
            var encaixado = new CheckBox
            {
                IsChecked = peca.Giro < 0.5,
                Content = "encaixado",
                Foreground = new SolidColorBrush(Color.FromRgb(0xE6, 0xE6, 0xE6)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 12, 0),
                ToolTip = "Tirar e pôr o pendrive (ou clique nele, no circuito)",
            };
            encaixado.Click += (s2, _) => editor.DefinirGiro(peca, ((CheckBox)s2).IsChecked == true ? 0 : 1);
            pendriveEmEdicao = (peca, encaixado);
            opcoesObjeto.Children.Add(encaixado);
        }

        // Teclas: so aparece em peca que da para acionar com o teclado.
        if (Vinculos.Aceita(peca.Tipo))
        {
            int comTecla = peca.Teclas?.Count(x => !string.IsNullOrEmpty(x.Value)) ?? 0;
            var teclas = new Button
            {
                Content = comTecla == 0 ? "Teclas…" : $"Teclas ({comTecla})",
                Style = (Style)FindResource("BotaoPequeno"),
                Margin = new Thickness(0, 0, 12, 0),
                ToolTip = "Controlar esta peça pelo teclado ou por um controle de vídeo game",
                VerticalAlignment = VerticalAlignment.Center,
            };
            teclas.Click += (_, _) => EscolherTeclas(peca);
            opcoesObjeto.Children.Add(teclas);
        }

        if (peca.Tipo == "led")
        {
            foreach (var (id, nome, hex) in CoresDeLed)
            {
                var cor = (Color)ColorConverter.ConvertFromString(hex);
                var quadro = new System.Windows.Shapes.Rectangle
                {
                    Width = 20,
                    Height = 20,
                    RadiusX = 3,
                    RadiusY = 3,
                    Margin = new Thickness(0, 0, 4, 0),
                    Fill = new SolidColorBrush(cor),
                    // Branco marca a escolhida, como nos quadros do fio.
                    // Antes era o contrario: a marca da cor selecionada
                    // era quase preta, invisivel na tab escura.
                    Stroke = peca.Cor == id
                        ? Brushes.White
                        : new SolidColorBrush(Color.FromRgb(0x5A, 0x5A, 0x5E)),
                    StrokeThickness = peca.Cor == id ? 2.5 : 1,
                    Cursor = Cursors.Hand,
                    ToolTip = nome,
                    Tag = id
                };
                quadro.MouseLeftButtonDown += (s2, _) =>
                {
                    editor.DefinirCorDoLed((string)((System.Windows.Shapes.Rectangle)s2).Tag);
                    AtualizarBarraObjeto();
                };
                opcoesObjeto.Children.Add(quadro);
            }
        }
        else if (Catalogo.EhPcb(peca.Tipo))
        {
            var (colunas, linhas) = Catalogo.TamanhoDaPcb(peca.Tipo)!.Value;
            Numero(Idioma.T("Colunas"), Idioma.T("furos"), colunas, Catalogo.PcbMinimo, Catalogo.PcbMaximo,
                   v => { editor.DefinirTamanhoDaPcb((int)Math.Round(v), linhas); AtualizarBarraObjeto(); });
            Numero(Idioma.T("Linhas"), Idioma.T("furos"), linhas, Catalogo.PcbMinimo, Catalogo.PcbMaximo,
                   v => { editor.DefinirTamanhoDaPcb(colunas, (int)Math.Round(v)); AtualizarBarraObjeto(); });
            foreach (var (id, nome, hex) in EditorCircuito.CoresDePcb)
            {
                bool marcada = (peca.Cor ?? "verde") == id;
                var quadro = new System.Windows.Shapes.Rectangle
                {
                    Width = 20, Height = 20, RadiusX = 3, RadiusY = 3, Margin = new Thickness(0, 0, 4, 0),
                    Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)),
                    Stroke = marcada ? Brushes.White : new SolidColorBrush(Color.FromRgb(0x5A, 0x5A, 0x5E)),
                    StrokeThickness = marcada ? 2.5 : 1, Cursor = Cursors.Hand, ToolTip = Idioma.T(nome), Tag = id,
                };
                quadro.MouseLeftButtonDown += (s2, _) =>
                {
                    editor.DefinirCorDaPcb((string)((System.Windows.Shapes.Rectangle)s2).Tag);
                    AtualizarBarraObjeto();
                };
                opcoesObjeto.Children.Add(quadro);
            }
            var virar = new Button
            {
                Style = (Style)FindResource("AcaoObj"),
                Content = peca.Virada ? Idioma.T("desvirar (lado de cima)") : Idioma.T("virar (lado de baixo)"),
                ToolTip = Idioma.T("Vira a placa com o que está montado nela: o lado de baixo aparece, e o fio feito assim é a trilha de solda entre os furos."),
                Margin = new Thickness(8, 0, 0, 0),
            };
            virar.Click += (_, _) => { editor.VirarPcbSelecionada(); AtualizarBarraObjeto(); };
            opcoesObjeto.Children.Add(virar);
        }
        else if (Catalogo.EhProtoboard(peca.Tipo))
        {
            // As mini de 170 vem coloridas; deixa escolher a cor.
            foreach (var (id, nome, hex) in EditorCircuito.CoresDeProtoboard)
            {
                var cor = (Color)ColorConverter.ConvertFromString(hex);
                bool marcada = (peca.Cor ?? "branca") == id;
                var quadro = new System.Windows.Shapes.Rectangle
                {
                    Width = 20,
                    Height = 20,
                    RadiusX = 3,
                    RadiusY = 3,
                    Margin = new Thickness(0, 0, 4, 0),
                    Fill = new SolidColorBrush(cor),
                    Stroke = marcada ? Brushes.White
                                     : new SolidColorBrush(Color.FromRgb(0x5A, 0x5A, 0x5E)),
                    StrokeThickness = marcada ? 2.5 : 1,
                    Cursor = Cursors.Hand,
                    ToolTip = nome,
                    Tag = id
                };
                quadro.MouseLeftButtonDown += (s2, _) =>
                {
                    editor.DefinirCorDaProtoboard((string)((System.Windows.Shapes.Rectangle)s2).Tag);
                    AtualizarBarraObjeto();
                };
                opcoesObjeto.Children.Add(quadro);
            }
        }
        else if (peca.Tipo is "potentiometer" or "ky-040")
        {
            // O encoder so respondia a roda do mouse: nao tinha controle
            // nenhum na tab, entao quem nao soubesse do atalho nao tinha
            // como girá-lo.
            opcoesObjeto.Children.Add(new TextBlock
            {
                Text = peca.Tipo == "ky-040" ? "Giro" : "Posição",
                Foreground = new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0x9A)),
                FontSize = 10,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            });

            var barra = new Slider
            {
                Minimum = 0,
                Maximum = 1,
                Value = peca.Giro,
                Width = 140,
                SmallChange = 0.01,
                LargeChange = 0.1,
                IsSnapToTickEnabled = false,   // continuo, nao em degraus
                VerticalAlignment = VerticalAlignment.Center
            };
            var leitura = new TextBlock
            {
                Text = LeituraDoPot(peca),
                Foreground = new SolidColorBrush(Color.FromRgb(0xC8, 0xC8, 0xC8)),
                FontSize = 11,
                MinWidth = 90,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0)
            };

            barra.ValueChanged += (_, ev) => editor.DefinirGiro(peca, ev.NewValue);

            // A roda do mouse mexe no mesmo valor: o slider precisa
            // acompanhar sem disparar a si mesmo de volta.
            potEmEdicao = (peca, barra, leitura);

            opcoesObjeto.Children.Add(barra);
            opcoesObjeto.Children.Add(leitura);
        }
        else if (Comportamento.De(peca.Tipo) is { TemControle: true } comp)
        {
            // Todo sensor da tabela ganha o mesmo controle: deslizante
            // para os analogicos, liga/desliga para os de nivel logico.
            opcoesObjeto.Children.Add(new TextBlock
            {
                Text = comp.Rotulo,
                Foreground = new SolidColorBrush(Color.FromRgb(0x99, 0x99, 0x99)),
                FontSize = 10,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            });

            if (comp.Chave)
            {
                var chave = new CheckBox
                {
                    IsChecked = peca.Giro > 0.5,
                    Content = peca.Giro > 0.5 ? "sim" : "nao",
                    Foreground = new SolidColorBrush(Color.FromRgb(0xE6, 0xE6, 0xE6)),
                    VerticalAlignment = VerticalAlignment.Center
                };
                chave.Click += (s2, _) =>
                {
                    var cb = (CheckBox)s2;
                    editor.DefinirGiro(peca, cb.IsChecked == true ? 1 : 0);
                    cb.Content = cb.IsChecked == true ? "sim" : "nao";
                };
                opcoesObjeto.Children.Add(chave);
            }
            else
            {
                var barra = new Slider
                {
                    Minimum = 0, Maximum = 1, Value = peca.Giro, Width = 140,
                    SmallChange = 0.01, LargeChange = 0.1,
                    IsSnapToTickEnabled = false,
                    VerticalAlignment = VerticalAlignment.Center
                };
                var leitura = new TextBlock
                {
                    Text = LeituraDoPot(peca),
                    Foreground = new SolidColorBrush(Color.FromRgb(0xC8, 0xC8, 0xC8)),
                    FontSize = 11, MinWidth = 80,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(8, 0, 0, 0)
                };
                barra.ValueChanged += (_, ev) => editor.DefinirGiro(peca, ev.NewValue);
                potEmEdicao = (peca, barra, leitura);
                opcoesObjeto.Children.Add(barra);
                opcoesObjeto.Children.Add(leitura);
            }
        }
        else if (peca.Tipo == "analog-joystick")
        {
            // Os dois eixos. O vertical era simulado (AplicarEntradas ja
            // lia Giro2) mas nao havia nada que o mudasse: DefinirGiro2
            // estava no editor sem um unico chamador, entao o VERT ficava
            // parado no meio para sempre.
            foreach (var (rotulo, ehX) in new[] { ("X", true), ("Y", false) })
            {
                opcoesObjeto.Children.Add(new TextBlock
                {
                    Text = rotulo,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0x9A)),
                    FontSize = 10,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(ehX ? 0 : 10, 0, 4, 0)
                });

                var barra = new Slider
                {
                    Minimum = 0, Maximum = 1,
                    Value = ehX ? peca.Giro : peca.Giro2,
                    Width = 92,
                    SmallChange = 0.01, LargeChange = 0.1,
                    IsSnapToTickEnabled = false,
                    VerticalAlignment = VerticalAlignment.Center
                };
                var leitura = new TextBlock
                {
                    Text = LeituraDoEixo(ehX ? peca.Giro : peca.Giro2),
                    Foreground = new SolidColorBrush(Color.FromRgb(0xC8, 0xC8, 0xC8)),
                    FontSize = 11,
                    MinWidth = 44,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(6, 0, 0, 0)
                };
                barra.ValueChanged += (_, ev) =>
                {
                    if (ehX) editor.DefinirGiro(peca, ev.NewValue);
                    else editor.DefinirGiro2(peca, ev.NewValue);
                    leitura.Text = LeituraDoEixo(ev.NewValue);
                };
                opcoesObjeto.Children.Add(barra);
                opcoesObjeto.Children.Add(leitura);
            }

            opcoesObjeto.Children.Add(new Border
            {
                Width = 1,
                Background = new SolidColorBrush(Color.FromRgb(0x4A, 0x4A, 0x4E)),
                Margin = new Thickness(8, 2, 8, 2)
            });

            var centrar = new Button
            {
                Style = (Style)FindResource("AcaoObj"),
                Content = "centrar",
                ToolTip = "Volta os dois eixos ao repouso, como o joystick solto"
            };
            centrar.Click += (_, _) =>
            {
                editor.DefinirGiro(peca, 0.5);
                editor.DefinirGiro2(peca, 0.5);
                AtualizarBarraObjeto();
            };
            opcoesObjeto.Children.Add(centrar);
        }
        else if (peca.Tipo == "dip-switch-8")
        {
            // As oito chaves. O editor ja sabia acioná-las e a simulação
            // já lia os bits, mas não havia nada na tela para clicar.
            opcoesObjeto.Children.Add(new TextBlock
            {
                Text = "Chaves",
                Foreground = new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0x9A)),
                FontSize = 10,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            });

            for (int k = 0; k < 8; k++)
            {
                int indice = k;
                bool ligada = (peca.Bits & (1 << k)) != 0;
                var chave = new Button
                {
                    Style = (Style)FindResource("AcaoObj"),
                    Content = (k + 1).ToString(),
                    Padding = new Thickness(7, 3, 7, 3),
                    Margin = new Thickness(0, 0, 2, 0),
                    ToolTip = $"Chave {k + 1}: {(ligada ? "ligada" : "desligada")}",
                    Background = ligada
                        ? new SolidColorBrush(Color.FromRgb(0x16, 0x68, 0xC1))
                        : new SolidColorBrush(Color.FromRgb(0x3C, 0x3C, 0x3C)),
                    BorderBrush = ligada
                        ? Brushes.White : new SolidColorBrush(Color.FromRgb(0x5A, 0x5A, 0x5E))
                };
                chave.Click += (_, _) =>
                {
                    editor.AlternarChaveDip(peca, indice, (peca.Bits & (1 << indice)) == 0);
                    AtualizarBarraObjeto();
                };
                opcoesObjeto.Children.Add(chave);
            }
        }
        else if (EditorCircuito.TemBraco(peca.Tipo))
        {
            // O braco parafusado no eixo: um dos que vem com o servo, ou nenhum.
            opcoesObjeto.Children.Add(new TextBlock
            {
                Text = Idioma.T("Braço"), Foreground = Cinza, FontSize = 10,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0),
            });
            var bracos = EditorCircuito.OpcoesDeBraco(peca.Tipo);
            string montado = EditorCircuito.BracoDa(peca);
            Escolha([.. bracos.Select(b => Idioma.T(b.Nome))], Array.FindIndex(bracos, b => b.Id == montado),
                    i => { editor.DefinirBracoDoServo(bracos[i].Id); AtualizarBarraObjeto(); });
        }
        else if (Netlist.EhBateria(peca.Tipo))
        {
            MontarBarraDaBateria(peca);
        }
        else if (FichasDeMotor.Para(peca.Tipo).Length > 0)
        {
            MontarBarraDoMotor(peca);
        }
        else if (EditorCircuito.EhEsc(peca.Tipo))
        {
            MontarBarraDoEsc(peca);
        }
        else if (Reguladores.EhRegulador(peca.Tipo))
        {
            MontarBarraDoRegulador(peca);
        }
        else if (Passivos.EhPassivoComValor(peca.Tipo))
        {
            var modelos = Passivos.ModelosDe(peca.Tipo);
            opcoesObjeto.Children.Add(new TextBlock
            {
                Text = Idioma.T("Valor"), Foreground = Cinza, FontSize = 10,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0),
            });
            Escolha([.. modelos.Select(m => m.Nome)], Array.IndexOf(modelos, Passivos.Da(peca)), i =>
            {
                editor.DefinirModeloSimples(modelos[i].Modelo);
                AtualizarBarraObjeto();
            });
        }
        else if (Semicondutores.TemModelo(peca.Tipo))
        {
            var opcoes = Semicondutores.Opcoes(peca.Tipo);
            opcoesObjeto.Children.Add(new TextBlock
            {
                Text = Idioma.T("Modelo"), Foreground = Cinza, FontSize = 10,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0),
            });
            Escolha([.. opcoes.Select(o => o.Nome)], Array.FindIndex(opcoes, o => o.Modelo == Semicondutores.ModeloAtual(peca)), i =>
            {
                editor.DefinirModeloSimples(opcoes[i].Modelo);
                AtualizarBarraObjeto();
            });
        }
        else if (peca.Tipo is "pushbutton" or "dip-switch-8")
        {
            // A cor da capa do botao de 12 mm, ou a do corpo da chave DIP.
            opcoesObjeto.Children.Add(new TextBlock
            {
                Text = Idioma.T("Cor"), Foreground = Cinza, FontSize = 10,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0),
            });
            bool botao = peca.Tipo == "pushbutton";
            string[] cores = botao ? EditorCircuito.OpcoesDeCorDoBotao : EditorCircuito.OpcoesDeCorDoDip;
            Escolha([.. cores.Select(c => Idioma.T(char.ToUpper(c[0]) + c[1..]))],
                    Array.IndexOf(cores, botao ? EditorCircuito.CorDoBotao(peca) : EditorCircuito.CorDoDip(peca)), i =>
            {
                editor.DefinirModeloSimples(cores[i]);
                AtualizarBarraObjeto();
            });
        }
        else if (peca.Tipo == "tm1637")
        {
            // A cor do display: o vermelho, o verde, o azul, o branco ou o amarelo.
            opcoesObjeto.Children.Add(new TextBlock
            {
                Text = Idioma.T("Cor"), Foreground = Cinza, FontSize = 10,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0),
            });
            string[] cores = EditorCircuito.OpcoesDeCorDoTm1637;
            Escolha([.. cores.Select(c => Idioma.T(char.ToUpper(c[0]) + c[1..]))], Array.IndexOf(cores, EditorCircuito.CorDoTm1637(peca)), i =>
            {
                editor.DefinirModeloSimples(cores[i]);
                AtualizarBarraObjeto();
            });
        }
        else if (EditorCircuito.EhOled(peca.Tipo))
        {
            // A cor dos pontos: branco, azul, ou o de duas cores (amarelo em cima).
            opcoesObjeto.Children.Add(new TextBlock
            {
                Text = Idioma.T("Cor"), Foreground = Cinza, FontSize = 10,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0),
            });
            string[] cores = EditorCircuito.CoresDoOled(peca.Tipo);
            Escolha([.. cores.Select(c => c switch { "azul" => Idioma.T("Azul"), "amarelo-azul" => Idioma.T("Amarelo e azul"), _ => Idioma.T("Branco") })],
                    Array.IndexOf(cores, EditorCircuito.CorDoOled(peca)), i =>
            {
                editor.DefinirModeloSimples(cores[i]);
                AtualizarBarraObjeto();
            });
        }
        else if (EditorCircuito.EhLcd(peca.Tipo))
        {
            // A cor do vidro: o verde de letras escuras ou o azul de letras brancas.
            opcoesObjeto.Children.Add(new TextBlock
            {
                Text = Idioma.T("Cor"), Foreground = Cinza, FontSize = 10,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0),
            });
            string[] cores = ["verde", "azul"];
            Escolha([Idioma.T("Verde (letras escuras)"), Idioma.T("Azul (letras brancas)")],
                    Array.IndexOf(cores, EditorCircuito.CorDoLcd(peca)), i =>
            {
                editor.DefinirModeloSimples(cores[i]);
                AtualizarBarraObjeto();
            });
        }
        else if (Diodos.EhDiodo(peca.Tipo))
        {
            var modelos = Diodos.ModelosDe(peca.Tipo);
            opcoesObjeto.Children.Add(new TextBlock
            {
                Text = Idioma.T("Modelo"), Foreground = Cinza, FontSize = 10,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0),
            });
            Escolha([.. modelos.Select(m => m.Resumo)], Array.IndexOf(modelos, Diodos.Da(peca)), i =>
            {
                editor.DefinirModeloSimples(modelos[i].Modelo);
                AtualizarBarraObjeto();
            });
        }
        else if (peca.Tipo == "resistor")
        {
            opcoesObjeto.Children.Add(new TextBlock
            {
                Text = "Resistência",
                Foreground = new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0x9A)),
                FontSize = 10,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0)
            });

            var campo = new TextBox
            {
                Text = peca.Ohms.ToString("0"),
                Width = 62,
                FontSize = 12,
                Padding = new Thickness(4, 2, 4, 2),
                VerticalContentAlignment = VerticalAlignment.Center,
                Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x20)),
                Foreground = new SolidColorBrush(Color.FromRgb(0xE6, 0xE6, 0xE6)),
                CaretBrush = new SolidColorBrush(Color.FromRgb(0xE6, 0xE6, 0xE6)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x5A, 0x5A, 0x5E))
            };
            campo.KeyDown += (s2, ev) =>
            {
                if (ev.Key != Key.Enter) return;
                if (double.TryParse(((TextBox)s2).Text, out double v) && v > 0)
                    editor.DefinirOhms(v);
                ev.Handled = true;
            };
            campo.LostFocus += (s2, _) =>
            {
                if (double.TryParse(((TextBox)s2).Text, out double v) && v > 0)
                    editor.DefinirOhms(v);
            };
            opcoesObjeto.Children.Add(campo);

            opcoesObjeto.Children.Add(new TextBlock
            {
                Text = " Ω",
                Foreground = new SolidColorBrush(Color.FromRgb(0xC8, 0xC8, 0xC8)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(2, 0, 4, 0)
            });
        }
        else
        {
            opcoesObjeto.Children.Add(new TextBlock
            {
                Text = $"{def.Pinos.Count} pinos",
                Foreground = new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0x9A)),
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center
            });
        }

        MontarQuemManda(peca);
    }

    /// <summary>
    /// "manda: ● ESP32  Uno" -- so aparece na peca ligada em duas placas.
    ///
    /// Antes ela obedecia a primeira em silencio, e depois passou a
    /// avisar. Avisar sem deixar resolver so trocava o susto por uma
    /// mensagem; aqui a pessoa escolhe, e o aviso some.
    /// </summary>
    void MontarQuemManda(PecaNoCircuito peca)
    {
        rede ??= new Netlist(editor.Atual);
        var placas = editor.PlacasDaPeca(rede, peca);
        if (placas.Count < 2) return;

        bool escolheu = peca.PlacaQueManda is { } e && placas.Contains(e);
        string vale = escolheu ? peca.PlacaQueManda : placas[0];

        opcoesObjeto.Children.Add(new Border
        {
            Width = 1,
            Background = new SolidColorBrush(Color.FromRgb(0x4A, 0x4A, 0x4E)),
            Margin = new Thickness(8, 2, 6, 2)
        });

        opcoesObjeto.Children.Add(new TextBlock
        {
            Text = escolheu ? "manda:" : "manda (automático):",
            Foreground = new SolidColorBrush(Color.FromRgb(0xC8, 0xC8, 0xC8)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0)
        });

        foreach (string idPlaca in placas)
        {
            string id = idPlaca;
            var botao = new Button
            {
                Style = (Style)FindResource("AcaoObj"),
                // A bolinha marca a que vale agora. Cor de fundo seria
                // mais bonita, mas o estilo do botao a sobrescreve.
                Content = (id == vale ? "● " : "") + NomeCurtoDaPlaca(id).Trim('[', ']'),
                ToolTip = id == vale
                    ? "É esta placa que manda na peça agora"
                    : "Fazer esta placa mandar na peça"
            };
            botao.Click += (_, _) =>
            {
                editor.DefinirPlacaQueManda(id);
                AtualizarBarraObjeto();
                Aviso($"{Catalogo.Obter(peca.Tipo)?.Nome ?? peca.Tipo} agora obedece "
                      + $"à {NomeCurtoDaPlaca(id).Trim('[', ']')}");
            };
            opcoesObjeto.Children.Add(botao);
        }
    }

    /// <summary>Escolhe a resistencia; as faixas do desenho acompanham.</summary>
    /// <summary>O caminho de volta: grava o circuito como diagram.json.</summary>
    /// <summary>
    /// Grava o programa em Assembly no formato que o avrdude usa.
    ///
    /// E o que fecha a conta para quem vai usar isto de verdade: o
    /// binario que rodou na tela e o MESMO que vai para a placa. Sem
    /// isto, "funcionou no simulador" continuaria sendo uma promessa
    /// sem como conferir -- e um sistema de irrigacao so mostraria o
    /// defeito depois de plantado.
    ///
    /// So vale para Assembly: as outras linguagens sao interpretadas
    /// aqui, e nao ha binario para gravar. Dizer isso e melhor do que
    /// gerar um .hex vazio.
    /// </summary>
    void ExportarHex(object s = null, RoutedEventArgs e = null)
    {
        string codigo = CodigoPrincipal() ?? "";

        if (Compilador.Detectar(codigo) != Linguagem.Assembly)
        {
            Erro("o .hex só existe para Assembly: as outras linguagens rodam "
                 + "interpretadas, e não geram binário para gravar.");
            return;
        }

        ProgramaMontado montado;

        try { montado = Montador.Juntar(codigo); }
        catch (Exception ex) { Erro($"não montou: {ex.Message}"); return; }

        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "Programa gravável (*.hex)|*.hex",
            FileName = System.IO.Path.GetFileNameWithoutExtension(NomeDoSketch()) + ".hex",
        };

        if (dlg.ShowDialog() != true) return;

        try
        {
            File.WriteAllText(dlg.FileName, montado.Hex());

            console.Info($"gravável exportado: {montado.Bytes} bytes "
                         + $"({montado.Palavras} palavras) em {dlg.FileName}");

            console.Info("para gravar num Uno:  avrdude -c arduino -p m328p "
                         + $"-P COM3 -U flash:w:{System.IO.Path.GetFileName(dlg.FileName)}:i");

            Aviso($"{montado.Bytes} bytes exportados — é o mesmo binário que rodou aqui.");
        }
        catch (Exception ex) { Erro($"não deu para exportar: {ex.Message}"); }
    }

    /// <summary>Para o teste: o .hex do codigo que esta no editor.</summary>
    internal string TesteHex()
        => Montador.Juntar(CodigoPrincipal() ?? "").Hex();

    /// <summary>
    /// O "Exportar": o diagrama daqui, ou -- com um plugin de formato ligado, como o
    /// do Wokwi -- o diagrama no formato de fora (a escolha e o "Tipo" do dialogo).
    /// </summary>
    void ExportarParaWokwi(object s = null, RoutedEventArgs e = null) => Exportar(null);

    void Exportar(Plugins.Plugin para)
    {
        var formatos = Plugins.Exportadores;
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Filter = string.Join("|", new[] { "Diagrama do CircuitSketch (diagram.json)|*.json" }
                .Concat(formatos.Select(f => $"Diagrama para o {f.Formato} (diagram.json)|*.json"))),
            FilterIndex = para is null ? 1 : formatos.ToList().IndexOf(para) + 2,
            FileName = "diagram.json"
        };
        if (dlg.ShowDialog() != true) return;
        var formato = dlg.FilterIndex >= 2 && dlg.FilterIndex - 2 < formatos.Count ? formatos[dlg.FilterIndex - 2] : null;

        try
        {
            File.WriteAllText(dlg.FileName, ExportarWokwi.Gerar(editor.Atual, NomeDoProjeto(), AutorDoProjeto(), formato));
            if (formato is null)
            {
                Aviso($"exportado: {editor.Atual.Pecas.Count} peças, {editor.Atual.Fios.Count} fios");
                return;
            }
            // A peca que o formato de fora nao tem sai como "circuitsketch-<nome>": la
            // ela aparece como desconhecida, e aqui volta inteira. Melhor dizer agora.
            var soNossas = editor.Atual.Pecas.Select(x => x.Tipo).Distinct()
                .Where(t => formato.TipoExportado(t)?.StartsWith(FormatoWokwi.PrefixoNosso, StringComparison.Ordinal) == true)
                .ToList();
            Aviso(soNossas.Count == 0
                ? $"exportado para o {formato.Formato}: {editor.Atual.Pecas.Count} peças, {editor.Atual.Fios.Count} fios"
                : $"exportado para o {formato.Formato}; ele não tem {string.Join(", ", soNossas.Take(5))}"
                  + (soNossas.Count > 5 ? " e outras" : "") + " (ficam no arquivo para voltar para cá)");
        }
        catch (Exception ex) { Erro($"não deu para exportar: {ex.Message}"); }
    }

    /// <summary>"Exportar para o Wokwi (diagram.json)...", um por plugin de formato ligado, logo abaixo do Exportar.</summary>
    void MontarExportarDosPlugins()
    {
        foreach (var velho in menuOpcoes.Items.OfType<MenuItem>().Where(m => m.Tag is "exportar-plugin").ToList())
            menuOpcoes.Items.Remove(velho);
        int onde = menuOpcoes.Items.IndexOf(miExportar);
        foreach (var formato in Plugins.Exportadores)
        {
            var item = new MenuItem
            {
                Header = $"Exportar para o {formato.Formato} (diagram.json)...",
                Tag = "exportar-plugin",
                ToolTip = $"Pelo plugin {formato.Nome}: o circuito com os nomes de peças e pinos do {formato.Formato}",
            };
            var esse = formato;
            item.Click += (_, _) => Exportar(esse);
            menuOpcoes.Items.Insert(++onde, item);
        }
    }

    /// <summary>Para o teste: os itens do menu que os plugins puseram.</summary>
    internal List<string> TesteItensDeExportar()
    {
        MontarExportarDosPlugins();
        return [.. menuOpcoes.Items.OfType<MenuItem>().Where(m => m.Tag is "exportar-plugin").Select(m => (string)m.Header)];
    }

    void ImportarDoWokwi(object s = null, RoutedEventArgs e = null)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Diagrama (diagram.json)|*.json",
            FileName = "diagram.json"
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            var r = ImportarWokwi.Ler(File.ReadAllText(dlg.FileName));

            // O diagram.json traz só o circuito: o sketch continua sendo o
            // do editor. As placas importadas têm outros ids, e sem isto o
            // código ficava ligado a uma placa que não existe mais.
            GuardarCodigoDoEditor();
            string codigoAtual = CodigoPrincipal();

            RecomecarSessao($"circuito importado: {System.IO.Path.GetFileName(dlg.FileName)}");
            editor.Carregar(r.Circuito);
            AdotarCodigo(codigoAtual);
            editor.AplicarGrade();
            Empilhar();
            ReconstruirRede();
            editor.Ajustar();

            // Diz o que ficou de fora com nome e sobrenome: importar
            // "quase tudo" calado e pior do que nao importar, porque o
            // circuito parece certo e nao funciona.
            var faltou = new List<string>();
            if (r.Ignorados.Count > 0)
                faltou.Add($"{r.Ignorados.Count} tipo(s) sem equivalente ("
                           + string.Join(", ", r.Ignorados) + ")"
                           // as pecas do Wokwi so abrem com o plugin dele
                           + (r.Ignorados.Any(t => t.StartsWith("wokwi-", StringComparison.Ordinal) || t.StartsWith("board-", StringComparison.Ordinal))
                              && !Plugins.Todos.Any(p => p.Id == "wokwi" && p.Ligado)
                                  ? " -- as pecas do Wokwi precisam do plugin do Wokwi ligado (Configuracoes > Plugins)" : ""));
            if (r.FiosPerdidos.Count > 0)
                faltou.Add($"{r.FiosPerdidos.Count} fio(s) sem pino aqui ("
                           + string.Join("; ", r.FiosPerdidos.Take(4))
                           + (r.FiosPerdidos.Count > 4 ? "; ..." : "") + ")");

            string resumo = $"importado: {r.Circuito.Pecas.Count} peças, "
                            + $"{r.Circuito.Fios.Count} fios";
            if (faltou.Count == 0) Aviso(resumo);
            else Aviso(resumo + " -- fora: " + string.Join(" e ", faltou));
        }
        catch (Exception ex)
        {
            Erro($"não consegui ler o diagram.json: {ex.Message}");
        }
    }

    void Copiar(object s = null, RoutedEventArgs e = null) => editor.Copiar();
    void Colar(object s = null, RoutedEventArgs e = null) => editor.Colar();
    void Excluir(object s = null, RoutedEventArgs e = null) => editor.RemoverSelecionada();
    void Ajustar(object s = null, RoutedEventArgs e = null) => editor.Ajustar();
    void Aproximar(object s = null, RoutedEventArgs e = null) => editor.MudarZoom(1.2);
    void Afastar(object s = null, RoutedEventArgs e = null) => editor.MudarZoom(1 / 1.2);
    void FocarSerial(object s = null, RoutedEventArgs e = null) => rolagemSerial.Focus();

    void AlternarGrade(object s = null, RoutedEventArgs e = null)
    {
        DefinirGrade(!editor.MostrarGrade);
        Aviso(editor.MostrarGrade ? "grade ligada" : "grade desligada");
    }

    void DefinirGrade(bool mostrar)
    {
        editor.MostrarGrade = mostrar;
        editor.AplicarGrade();
        Preferencias.Definir(Preferencias.Grade, mostrar);
    }

    /// <summary>
    /// As configuracoes do programa num lugar so -- e gravadas.
    ///
    /// Estavam espalhadas pelo menu de tres pontos e nenhuma sobrevivia a
    /// fechar o programa: quem gosta da grade desligada desligava a grade
    /// toda aula.
    /// </summary>
    void AbrirConfiguracoes(object s = null, RoutedEventArgs e = null)
        => CriarConfiguracoes().ShowDialog();

    /// <summary>
    /// A janela de configurações montada e ainda não mostrada.
    ///
    /// Separada de abrir por causa do passeio pela interface: aberta com
    /// ShowDialog ela prenderia o roteiro até alguém fechar, e o passeio
    /// precisa dela na tela SEM esperar.
    /// </summary>
    JanelaConfiguracoes CriarConfiguracoes()
    {
        return new JanelaConfiguracoes(this, new AjustesDoPrograma(
            LerGrade: () => editor.MostrarGrade,
            DefinirGrade: DefinirGrade,
            LerEstiloDoFio: () => editor.EstiloPadrao,
            DefinirEstiloDoFio: estilo =>
            {
                editor.DefinirEstiloDeTodos(estilo);
                MarcarEstiloNoMenu(estilo);
            },
            LerInternet: () => miInternet.IsChecked,
            DefinirInternet: ligada =>
            {
                miInternet.IsChecked = ligada;
                AlternarInternet();
            },
            LerCorDoFio: () => editor.CorDoFio,
            DefinirCorDoFio: cor =>
            {
                editor.CorDoFio = cor;
                Preferencias.Definir(Preferencias.CorDoFio, cor);
            },
            Cores: CoresDeFio,
            LerServidores: ResumoDosServidores,
            LerBluetooth: ResumoDoBluetooth,
            AbrirPainelDeRede: () => { MostrarAba("rede"); FocarSerial(); },
            AbrirPainelDeBluetooth: () => { MostrarAba("bluetooth"); FocarSerial(); },
            AbrirAjuda: () => Ajuda())
        {
            CaberNaTela = CaberNaTela,
            AbrirBibliotecas = () => AbrirBibliotecas(),
            Aplicar = AplicarPreferencias,
            Placas = [.. PerfilPlaca.Conhecidos.Select(p => (p.Id, p.Nome))],
        });
    }

    /// <summary>
    /// Poe no programa o que ficou gravado da ultima vez.
    ///
    /// Roda depois do circuito de abertura carregar: e o circuito que
    /// traz os fios, e o tracado padrao so faz sentido com eles na tela.
    /// </summary>
    void AplicarTema()
    {
        AtualizarAbas();
        MostrarModo(modoDaTela);

        // O que e montado com a cor da hora precisa ser montado de novo:
        // as abas de arquivo do codigo, a arvore de componentes e as
        // linhas que o console ja escreveu.
        AtualizarAbasDeArquivo();
        AtualizarComponentes(forcar: true);
        RepintarConsole();
        editor.AplicarGrade();
        editor.Redesenhar();

        txtCodigo.SyntaxHighlighting = Realce.Do(Tema.Atual, linguagemPintada);
        txtCodigo.TextArea.TextView.CurrentLineBackground = Realce.FundoDaLinhaAtual;
        txtCodigo.TextArea.TextView.LinkTextForegroundBrush = Tema.Pincel(Tema.TextoFraco);

        // As cores do serial, da rede e do Bluetooth vem do tema, pelos
        // recursos Terminal, TerminalRede e TerminalBt -- e mudam sozinhas.
    }

    /// <summary>
    /// Poe a janela dentro da area util da tela.
    ///
    /// O tamanho de fabrica era 1400x800 fixo, e num monitor de
    /// 1366x768 -- que e o da sala de aula -- a janela nascia maior que
    /// a tela: a coluna do codigo ficava fora da borda direita e a barra
    /// de status embaixo do menu iniciar. Nada disso aparecia em quem
    /// desenvolve, porque a tela e maior.
    ///
    /// Tambem devolve a janela onde ela estava da ultima vez, quando a
    /// pessoa pediu para lembrar.
    /// </summary>
    void AjustarAJanelaDaTela()
    {
        var area = SystemParameters.WorkArea;

        if (Preferencias.Ligado(Preferencias.LembrarJanela, false)
            && double.TryParse(Preferencias.Texto(Preferencias.JanelaLargura), out double lg)
            && double.TryParse(Preferencias.Texto(Preferencias.JanelaAltura), out double at)
            && lg >= MinWidth && at >= MinHeight)
        {
            Width = lg;
            Height = at;

            if (double.TryParse(Preferencias.Texto(Preferencias.JanelaEsquerda), out double x)
                && double.TryParse(Preferencias.Texto(Preferencias.JanelaTopo), out double y))
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Left = x;
                Top = y;
            }
        }

        // Sempre cabe na tela, venha o tamanho de onde vier: um monitor
        // trocado nao pode deixar a janela inalcancavel.
        bool posta = WindowStartupLocation == WindowStartupLocation.Manual;
        var cabe = CaixaQueCabe(area, new Rect(posta ? Left : area.Left,
                                               posta ? Top : area.Top, Width, Height));

        Width = cabe.Width;
        Height = cabe.Height;

        if (posta)
        {
            Left = cabe.Left;
            Top = cabe.Top;
        }

        if (Preferencias.Ligado(Preferencias.AbrirMaximizado, false))
            WindowState = WindowState.Maximized;
    }

    /// <summary>Guarda onde a janela estava, para a proxima abertura.</summary>
    void GuardarJanela()
    {
        if (!Preferencias.Ligado(Preferencias.LembrarJanela, false)) return;

        var cultura = System.Globalization.CultureInfo.InvariantCulture;
        var caixa = WindowState == WindowState.Normal
            ? new Rect(Left, Top, Width, Height)
            : RestoreBounds;

        if (caixa.Width < MinWidth || caixa.Height < MinHeight) return;

        Preferencias.Definir(Preferencias.JanelaLargura, caixa.Width.ToString(cultura));
        Preferencias.Definir(Preferencias.JanelaAltura, caixa.Height.ToString(cultura));
        Preferencias.Definir(Preferencias.JanelaEsquerda, caixa.Left.ToString(cultura));
        Preferencias.Definir(Preferencias.JanelaTopo, caixa.Top.ToString(cultura));
    }

    /// <summary>
    /// Encaixa uma caixa dentro da area util da tela.
    ///
    /// Fica separada da janela -- e so conta -- porque e ela que decide
    /// se o programa cabe no monitor de 1366x768 da sala, e isso precisa
    /// de teste. Instanciar a janela num teste prende o processo: o WPF
    /// nao encerra enquanto ela existir.
    /// </summary>
    internal static Rect CaixaQueCabe(Rect area, Rect desejada)
    {
        double larg = Math.Min(desejada.Width, area.Width);
        double alt = Math.Min(desejada.Height, area.Height);

        double x = Math.Clamp(desejada.Left, area.Left, Math.Max(area.Left, area.Right - larg));
        double y = Math.Clamp(desejada.Top, area.Top, Math.Max(area.Top, area.Bottom - alt));

        return new Rect(x, y, larg, alt);
    }

    /// <summary>A caixa centralizada, com folga, para o botao das configuracoes.</summary>
    internal static Rect CaixaCentralizada(Rect area, double minLarg, double minAlt)
    {
        double larg = Math.Min(Math.Max(minLarg, area.Width * 0.96), area.Width);
        double alt = Math.Min(Math.Max(minAlt, area.Height * 0.96), area.Height);

        return new Rect(area.Left + (area.Width - larg) / 2,
                        area.Top + (area.Height - alt) / 2, larg, alt);
    }

    /// <summary>
    /// Os tres numeros da bateria, e o tempo que eles dao.
    ///
    /// O professor pediu capacidade, amperagem e voltagem configuraveis.
    /// Os tres mexem no circuito de verdade: a tensao manda no que os
    /// componentes recebem (e no que queima), a corrente maxima limita o
    /// quanto ela aguenta antes de a tensao cair, e a capacidade responde
    /// "quanto tempo isto dura" -- a conta que quase ninguem faz.
    /// </summary>
    void MontarBarraDaBateria(PecaNoCircuito peca)
    {
        if (Baterias.SuporteDe(peca.Tipo) is { } suporte)
        {
            MontarBarraDoSuporte(peca, suporte.Formato, suporte.Slots);
            return;
        }

        // Qual bateria: as prontas (o desenho e os numeros de fabrica) e a
        // personalizada, a de antes. Os numeros continuam editaveis.
        (string Tipo, string Nome)[] tipos = [.. Baterias.Pacotes.Select(p => (p.Tipo, p.Curto)), ("bateria", "Personalizada")];
        opcoesObjeto.Children.Add(new TextBlock
        {
            Text = Idioma.T("Bateria"), Foreground = Cinza, FontSize = 10,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0),
        });
        Escolha([.. tipos.Select(t => Idioma.T(t.Nome))], Array.FindIndex(tipos, t => t.Tipo == peca.Tipo),
                i => { editor.DefinirTipoDaBateria(tipos[i].Tipo); AtualizarBarraObjeto(); DepoisDeMexerNaBateria(peca); });

        Numero("Tensão", "V", peca.Volts, 0.1, 30,
               v => { peca.Volts = v; DepoisDeMexerNaBateria(peca); });

        Numero("Corrente", "mA", peca.MiliAmperes, 1, 200000,
               v => { peca.MiliAmperes = v; DepoisDeMexerNaBateria(peca); });

        Numero("Capacidade", "mAh", peca.MiliAmperesHora, 1, 100000,
               v => { peca.MiliAmperesHora = v; DepoisDeMexerNaBateria(peca); });

        // Mexeu nos numeros de uma pronta: o desenho fica, e a barra avisa.
        bool modificada = Baterias.DeFabrica(peca.Tipo) is { } fabrica
            && (Math.Abs(fabrica.Volts - peca.Volts) > 1e-6 || Math.Abs(fabrica.MiliAmperes - peca.MiliAmperes) > 1e-6
                || Math.Abs(fabrica.MiliAmperesHora - peca.MiliAmperesHora) > 1e-6);
        var duracao = new TextBlock
        {
            Foreground = Cinza,
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 0, 0),
            Text = DuracaoDaBateria(peca) + (modificada ? " · " + Idioma.T("números mudados") : ""),
            ToolTip = Baterias.PacoteDe(peca.Tipo)?.Sobre,
        };
        opcoesObjeto.Children.Add(duracao);
    }

    /// <summary>
    /// O suporte de pilhas: o formato (AA, AAA, 18650, CR2032), quantos slots
    /// e qual pilha; ao lado, o que ele entrega. A pilha de cada slot sai e
    /// volta com um clique nela (com o suporte selecionado).
    /// </summary>
    void MontarBarraDoSuporte(PecaNoCircuito peca, Baterias.Formato formato, int slots)
    {
        void Rotulo(string texto) => opcoesObjeto.Children.Add(new TextBlock
        {
            Text = Idioma.T(texto), Foreground = Cinza, FontSize = 10,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0),
        });

        Rotulo("Pilha");
        var formatos = Baterias.Formatos;
        Escolha([.. formatos.Select(f => f.Nome)], Array.IndexOf(formatos, formato), i =>
        {
            var novo = formatos[i];
            int quantos = novo.Slots.Contains(slots) ? slots : novo.SlotsPadrao;
            editor.DefinirTipoDaBateria(Baterias.TipoDoSuporte(novo.Id, quantos));
            AtualizarBarraObjeto();
            DepoisDeMexerNaBateria(peca);
        });
        if (formato.Pilhas.Length > 1)
        {
            var pilha = Baterias.PilhaDe(peca);
            Escolha([.. formato.Pilhas.Select(p => Idioma.T(p.Nome))], Array.IndexOf(formato.Pilhas, pilha), i =>
            {
                editor.DefinirPilhaDoSuporte(formato.Pilhas[i].Id);
                AtualizarBarraObjeto();
                DepoisDeMexerNaBateria(peca);
            });
        }
        if (formato.Slots.Length > 1)
        {
            Rotulo("Slots");
            Escolha([.. formato.Slots.Select(n => n.ToString(System.Globalization.CultureInfo.InvariantCulture))], Array.IndexOf(formato.Slots, slots), i =>
            {
                editor.DefinirTipoDaBateria(Baterias.TipoDoSuporte(formato.Id, formato.Slots[i]));
                AtualizarBarraObjeto();
                DepoisDeMexerNaBateria(peca);
            });
        }

        var ficha = Baterias.FichaDe(peca);
        int vazios = Enumerable.Range(0, slots).Count(k => !Baterias.TemPilha(peca, k));
        opcoesObjeto.Children.Add(new TextBlock
        {
            Foreground = vazios > 0 ? new SolidColorBrush(Color.FromRgb(0xE0, 0xA0, 0x40)) : Cinza,
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 0, 0),
            Text = vazios > 0
                ? Idioma.T(vazios == 1 ? "um slot vazio: o circuito fica aberto" : $"{vazios} slots vazios: o circuito fica aberto")
                : $"{ficha.Volts:0.0#} V · {ficha.MiliAmperesHora:0} mAh · {Idioma.T("até")} {ficha.MiliAmperes / 1000:0.###} A · {DuracaoDaBateria(peca)}",
            ToolTip = Idioma.T("Clique numa pilha, com o suporte selecionado, para tirar ou pôr de volta."),
        });
    }

    /// <summary>
    /// O regulador de tensao: o modelo (7805, 7812, AMS1117 de 3,3 ou 5 V) ou
    /// a saida do trimpot (LM2596, MT3608), e o que ele esta fazendo agora --
    /// a entrada, a saida, a corrente e o calor.
    /// </summary>
    void MontarBarraDoRegulador(PecaNoCircuito peca)
    {
        var ficha = Reguladores.Da(peca);
        var modelos = Reguladores.ModelosDe(peca.Tipo);
        if (modelos.Length > 0)
        {
            opcoesObjeto.Children.Add(new TextBlock
            {
                Text = "Modelo", Foreground = Cinza, FontSize = 10,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0),
            });
            Escolha([.. modelos.Select(m => m.Resumo)], Array.IndexOf(modelos, ficha), i =>
            {
                editor.DefinirRegulador(modelos[i].Modelo, null);
                AtualizarBarraObjeto();
                var nova = modelos[i];
                Aviso($"regulador {nova.Nome}: saída de {nova.Volts:0.#} V, até {nova.CorrenteMaxima:0.#} A; "
                      + $"a entrada precisa de ~{nova.Volts + nova.Folga:0.#} V ou mais (máximo {nova.EntradaMaxima:0} V).");
            });
        }
        else
        {
            Numero("Saída", "V", Reguladores.SaidaDe(peca), ficha.AjusteMinimo, ficha.AjusteMaximo, v =>
            {
                editor.DefinirRegulador(null, v);
                AtualizarBarraObjeto();
                Aviso($"{ficha.Nome}: saída ajustada em {v:0.##} V (o trimpot)"
                      + (ficha.Tipo == TipoDeRegulador.Abaixador ? $"; a entrada precisa de ~{v + ficha.Folga:0.#} V ou mais." : "; ele só sobe a tensão."));
            });
        }

        var situacao = new TextBlock
        {
            Text = editor.SituacaoDoRegulador(peca.Id, curta: true),
            ToolTip = editor.SituacaoDoRegulador(peca.Id, curta: false),
            Foreground = Cinza, FontSize = 11, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 12, 0), MaxWidth = 260, TextTrimming = TextTrimming.CharacterEllipsis,
        };
        opcoesObjeto.Children.Add(situacao);
        reguladorEmEdicao = (peca, situacao);
    }

    /// <summary>
    /// A placa: o cabo USB. No automatico ela esta na USB quando nao ha
    /// bateria no circuito; com bateria, o projeto e portatil e ela so liga se
    /// a bateria a alimentar (no VIN, no 5V, no 3V3). "Cabo ligado" e a placa
    /// na USB com a bateria so nos motores; "sem o cabo" e o contrario.
    /// </summary>
    void MontarBarraDaPlaca(PecaNoCircuito peca)
    {
        opcoesObjeto.Children.Add(new TextBlock
        {
            Text = "USB", Foreground = Cinza, FontSize = 10,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0),
        });
        bool automatico = EditorCircuito.TemCaboUsb(new PecaNoCircuito { Tipo = peca.Tipo }, editor.Atual);
        Escolha([automatico ? "automático (cabo ligado: não há bateria)" : "automático (sem o cabo: há bateria)",
                 "cabo ligado", "sem o cabo"],
            peca.Usb switch { null => 0, true => 1, false => 2 },
            i =>
            {
                editor.DefinirUsb(peca, i switch { 1 => true, 2 => false, _ => null });
                AtualizarBarraObjeto();
                Aviso($"{NomeCurtoDaPlaca(peca.Id).Trim('[', ']')}: {editor.SituacaoDaEnergia(peca.Id)}.");
            });

        // Curta, para os botoes da barra caberem; a frase inteira (o porque) na dica.
        var situacao = new TextBlock
        {
            Text = editor.SituacaoCurta(peca.Id),
            ToolTip = editor.SituacaoDaEnergia(peca.Id),
            Foreground = Cinza, FontSize = 11, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0), MaxWidth = 220, TextTrimming = TextTrimming.CharacterEllipsis,
        };
        opcoesObjeto.Children.Add(situacao);
        usbEmEdicao = (peca, situacao);
    }

    /// <summary>
    /// O motor: a ficha da lista (os de escova que cabem no corpo do 130, os
    /// sem escova de aeromodelo e de drone) ou a digitada. Mexer num numero
    /// da ficha vira "personalizado" -- e o motor de quem tem um na mao.
    /// </summary>
    void MontarBarraDoMotor(PecaNoCircuito peca)
    {
        bool semEscova = peca.Tipo == "motor-brushless";
        var fichas = FichasDeMotor.Para(peca.Tipo);
        string padrao = semEscova ? "a2212-1000" : "130";
        bool propria = peca.Modelo == "personalizado" && peca.Ficha is not null;
        var atual = propria ? peca.Ficha.Copia()
                  : FichaPersonalizada.De(FichasDeMotor.Achar(peca.Tipo, peca.Modelo ?? padrao) ?? fichas[0]);

        opcoesObjeto.Children.Add(new TextBlock
        {
            Text = "Motor", Foreground = Cinza, FontSize = 10,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0),
        });
        Escolha([.. fichas.Select(f => f.Resumo), "Personalizado (a ficha do seu motor)"],
            propria ? fichas.Length : Math.Max(0, Array.FindIndex(fichas, f => f.Id == (peca.Modelo ?? padrao))),
            i =>
            {
                if (i < fichas.Length) editor.DefinirModeloDoMotor(fichas[i].Id, null);
                else editor.DefinirModeloDoMotor("personalizado", atual.Copia());
                DepoisDeMexerNoMotor(peca);
            });

        // Os numeros da ficha. Qualquer um mexido vira a ficha "personalizado".
        // A medida e o KV (ou a tensao e o rpm) refazem o resto pelo tamanho --
        // um motor maior com o mesmo KV tem menos resistencia e aguenta mais
        // corrente; a resistencia e a corrente maxima digitadas valem como estao.
        void Mexer(Action<FichaPersonalizada> mudar, bool estimar)
        {
            var nova = atual.Copia();
            mudar(nova);
            if (estimar) nova = FichasDeMotor.Estimar(semEscova, nova);
            editor.DefinirModeloDoMotor("personalizado", nova);
            DepoisDeMexerNoMotor(peca);
        }
        if (semEscova)
        {
            Numero("KV", "rpm/V", atual.RpmOuKv, 50, 30000, v => Mexer(f => f.RpmOuKv = v, true));
            Numero("Estator Ø", "mm", atual.MedidaA, 5, 120, v => Mexer(f => f.MedidaA = v, true));
            Numero("altura", "mm", atual.MedidaB, 2, 80, v => Mexer(f => f.MedidaB = v, true));
        }
        else
        {
            Numero("Tensão", "V", atual.Volts, 0.5, 48, v => Mexer(f => f.Volts = v, true));
            Numero("Sem carga", "rpm", atual.RpmOuKv, 10, 60000, v => Mexer(f => f.RpmOuKv = v, true));
            Numero("Lata Ø", "mm", atual.MedidaA, 6, 90, v => Mexer(f => f.MedidaA = v, true));
            Numero("compr.", "mm", atual.MedidaB, 8, 150, v => Mexer(f => f.MedidaB = v, true));
        }
        Numero("R", "Ω", atual.Resistencia, 0.002, 200, v => Mexer(f => f.Resistencia = v, false));
        Numero("Máx.", "A", atual.CorrenteMaxima, 0.05, 300, v => Mexer(f => f.CorrenteMaxima = v, false));
    }

    /// <summary>
    /// Uma escolha na barra do objeto: o botao mostra a opcao de agora e abre
    /// o menu com todas (o ComboBox do Windows nao segue o tema escuro).
    /// </summary>
    void Escolha(string[] opcoes, int atual, Action<int> escolher)
    {
        var botao = new Button
        {
            Content = (atual >= 0 && atual < opcoes.Length ? opcoes[atual] : "") + "  \u25BE",
            Style = (Style)FindResource("BotaoPequeno"),
            Margin = new Thickness(0, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var menu = new ContextMenu { PlacementTarget = botao, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        for (int i = 0; i < opcoes.Length; i++)
        {
            int qual = i;
            var item = new MenuItem { Header = opcoes[i], IsCheckable = true, IsChecked = i == atual };
            item.Click += (_, _) => escolher(qual);
            menu.Items.Add(item);
        }
        botao.Click += (_, _) => menu.IsOpen = true;
        opcoesObjeto.Children.Add(botao);
    }

    void DepoisDeMexerNoMotor(PecaNoCircuito peca)
    {
        ReconstruirRede();
        RefletirDiagrama();
        AtualizarBarraObjeto();
        var modelo = ModeloDeMotor.Da(peca);
        double volts = modelo.TensaoNominal;
        double rpm = (volts - modelo.AtritoSeco / modelo.Kt * modelo.Resistencia) / modelo.Ke * 60 / (2 * Math.PI);
        double travado = volts / modelo.Resistencia;
        Aviso($"motor: {modelo.Nome} — com {volts:0.#} V, ~{rpm:0} rpm sem carga e {travado:0.#} A travado"
              + (modelo.CorrenteMaxima > 0 ? $" (aguenta {modelo.CorrenteMaxima:0.#} A trabalhando)" : ""));
    }

    /// <summary>
    /// A ESC: a corrente (e com ela as celulas que aceita) e a cor -- as de
    /// drone vem enfeitadas, e a de aeromodelo no termo-retratil colorido.
    /// </summary>
    void MontarBarraDoEsc(PecaNoCircuito peca)
    {
        opcoesObjeto.Children.Add(new TextBlock
        {
            Text = "Corrente", Foreground = Cinza, FontSize = 10,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0),
        });
        int[] opcoes = EditorCircuito.AmperagensDoEsc(peca.Tipo);
        double atual = EditorCircuito.AmperesDoEsc(peca);
        Escolha([.. opcoes.Select(a => $"{a} A")],
            Array.IndexOf(opcoes, opcoes.OrderBy(a => Math.Abs(a - atual)).First()),
            i =>
            {
                editor.DefinirEsc(opcoes[i], null);
                ReconstruirRede();
                AtualizarBarraObjeto();
                var (min, max) = EditorCircuito.CelulasDoEsc(peca);
                Aviso($"ESC de {opcoes[i]} A: bateria de {min}S a {max}S (ate {max * 4.2:0.#} V).");
            });

        string escolhida = peca.Acabamento ?? (peca.Tipo == "esc-4em1" ? "preto" : "azul");
        foreach (var (id, nome, hex) in EditorCircuito.CoresDeEsc)
        {
            bool marcada = escolhida == id;
            var quadro = new System.Windows.Shapes.Rectangle
            {
                Width = 20, Height = 20, RadiusX = 3, RadiusY = 3,
                Margin = new Thickness(0, 0, 4, 0),
                Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)),
                Stroke = marcada ? Brushes.White : new SolidColorBrush(Color.FromRgb(0x5A, 0x5A, 0x5E)),
                StrokeThickness = marcada ? 2.5 : 1,
                Cursor = Cursors.Hand, ToolTip = nome, Tag = id,
            };
            quadro.MouseLeftButtonDown += (s2, _) =>
            {
                editor.DefinirEsc(null, (string)((System.Windows.Shapes.Rectangle)s2).Tag);
                AtualizarBarraObjeto();
            };
            opcoesObjeto.Children.Add(quadro);
        }

        var (cMin, cMax) = EditorCircuito.CelulasDoEsc(peca);
        opcoesObjeto.Children.Add(new TextBlock
        {
            Text = $"{cMin}S a {cMax}S" + (peca.Tipo == "esc-4em1" ? " · 4 motores" : " · BEC 5 V"),
            Foreground = Cinza, FontSize = 11, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 0, 0),
        });
    }

    void DepoisDeMexerNaBateria(PecaNoCircuito peca)
    {
        ReconstruirRede();
        RefletirDiagrama();
        AtualizarBarraObjeto();
        Aviso($"bateria: {peca.Volts:0.#} V · {peca.MiliAmperes:0} mA · "
              + $"{peca.MiliAmperesHora:0} mAh — {DuracaoDaBateria(peca)}");
    }

    /// <summary>
    /// Quanto tempo a bateria dura se entregar a corrente maxima.
    ///
    /// E o limite otimista, e esta dito assim: a conta de verdade
    /// depende do que o circuito consome a cada instante, e o simulador
    /// nao integra corrente ao longo do tempo.
    /// </summary>
    static string DuracaoDaBateria(PecaNoCircuito peca)
    {
        var ficha = Baterias.FichaDe(peca);
        if (ficha.MiliAmperes <= 0) return "";

        double horas = ficha.MiliAmperesHora / ficha.MiliAmperes;
        return horas >= 1
            ? $"dura ~{horas:0.#} h na corrente máxima"
            : $"dura ~{horas * 60:0} min na corrente máxima";
    }

    /// <summary>Um campo de numero com rotulo e unidade, para a barra do objeto.</summary>
    void Numero(string rotulo, string unidade, double valor,
                double minimo, double maximo, Action<double> guardar)
    {
        opcoesObjeto.Children.Add(new TextBlock
        {
            Text = rotulo,
            Foreground = Cinza,
            FontSize = 10,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0),
        });

        var campo = new TextBox
        {
            Text = valor.ToString("0.##"),
            Width = 62,
            FontSize = 12,
            Style = (Style)FindResource("CampoEscuro"),
            VerticalAlignment = VerticalAlignment.Center,
        };

        void Aplicar()
        {
            if (!double.TryParse(campo.Text, out double v))
            {
                Erro($"{rotulo}: '{campo.Text}' não é um número.");
                campo.Text = valor.ToString("0.##");
                return;
            }

            double preso = Math.Clamp(v, minimo, maximo);
            if (preso != v)
                Aviso($"{rotulo} fica entre {minimo:0.##} e {maximo:0.##} {unidade}.");

            campo.Text = preso.ToString("0.##");
            guardar(preso);
        }

        campo.KeyDown += (_, ev) =>
        {
            if (ev.Key != Key.Enter) return;
            Aplicar();
            ev.Handled = true;
        };
        campo.LostFocus += (_, _) => Aplicar();

        opcoesObjeto.Children.Add(campo);
        opcoesObjeto.Children.Add(new TextBlock
        {
            Text = unidade,
            Foreground = Cinza,
            FontSize = 10,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 14, 0),
        });
    }

    /// <summary>Encolhe a janela ate caber na tela, e centraliza.</summary>
    internal void CaberNaTela()
    {
        var area = SystemParameters.WorkArea;
        var caixa = CaixaCentralizada(area, MinWidth, MinHeight);

        WindowState = WindowState.Normal;
        Width = caixa.Width;
        Height = caixa.Height;
        Left = caixa.Left;
        Top = caixa.Top;

        Aviso($"janela ajustada para {Width:0}x{Height:0} "
              + $"(área útil da tela: {area.Width:0}x{area.Height:0})");
    }

    void AplicarPreferencias()
    {
        Tema.AplicarSalvo();
        AplicarTema();
        editor.CorDoFio = Preferencias.Texto(Preferencias.CorDoFio, "#00A651");
        editor.MostrarGrade = Preferencias.Ligado(Preferencias.Grade, true);
        editor.AplicarGrade();

        string estilo = Preferencias.Texto(Preferencias.EstiloDoFio, Fio.EstiloSolto);
        editor.EstiloPadrao = estilo;
        MarcarEstiloNoMenu(estilo);

        miInternet.IsChecked = Preferencias.Ligado(Preferencias.UsarInternet, true);
        redeSimulada.UsarInternet = miInternet.IsChecked;

        AplicarPreferenciasDaAlpha19();

        // Ler a lista de redes do computador e coisa da pessoa, e nao
        // do programa: quem nao quiser, desliga e volta as simuladas.
        RadioDoComputador.Desligado = !Preferencias.UsarRadioDoComputador;
        RadioDoComputador.Esquecer();
        avisoDeRadioMostrado = null;
    }

    internal void TesteAplicarPreferencias() => AplicarPreferencias();

    /// <summary>Para o teste: fechar agora jogaria algo fora?</summary>
    internal bool TestePodeFecharSemPerguntar()
        => AssinaturaAtual() == (circuitoSalvo ?? assinaturaInicial);
    internal void TesteAbrirConfiguracoes() => AbrirConfiguracoes();

    /// <summary>Para o teste: o celular do painel Bluetooth conecta numa placa.</summary>
    internal bool TesteConectarCelular(string nome) => redeSimulada.ConectarCelular(nome);

    /// <summary>Para o teste: o celular manda uma linha, como o campo do painel.</summary>
    internal bool TesteEnviarPeloCelular(string texto) => redeSimulada.EnviarDoCelular(texto + "\n");

    /// <summary>Para o teste: abre uma demo embutida, como o menu Exemplos prontos.</summary>
    internal void TesteAbrirDemo(string recurso) => AbrirDemo(recurso);

    /// <summary>Para o teste: publica direto no broker interno.</summary>
    internal void TestePublicarNoBroker(string topico, string texto, bool retida)
        => redeSimulada.Broker.Publicar(topico, System.Text.Encoding.UTF8.GetBytes(texto), retida);

    /// <summary>Para o teste: quantas retidas o broker interno guarda.</summary>
    internal int TesteRetidasNoBroker => redeSimulada.Broker.QuantasRetidas;

    /// <summary>Para o teste: quantas linhas estão marcadas para pausar, somando as placas.</summary>
    internal int TesteQuantasPausas => pausasPorPlaca.Values.Sum(p => p.Count);

    /// <summary>Para o teste: faz o quadro da tela agora (serial, rede, bluetooth).</summary>
    internal void TesteAtualizarPaineis()
    {
        AtualizarSerial();
        AtualizarPainelDeRede();
    }

    /// <summary>Para o passeio: o clique no botão ⋮, devolvendo o menu aberto.</summary>
    internal ContextMenu TesteAbrirMenuDeOpcoes()
    {
        AbrirMenu(btMenu, null);
        return btMenu.ContextMenu;
    }

    /// <summary>
    /// Para o passeio: o botão direito em cima da primeira peça, devolvendo
    /// o menu aberto.
    /// </summary>
    internal ContextMenu TesteMenuDoCircuito()
    {
        editor.SelecionarPorIndice(0, false);
        var peca = editor.Atual.Pecas.FirstOrDefault();
        MontarMenuDoCircuito(new Point(240, 160),
            new EditorCircuito.Acerto(EditorCircuito.Camada.CorpoPeca, peca, null, default, -1, null));
        return editor.ContextMenu;
    }

    /// <summary>Para o passeio: o botão direito numa ligação da aba Componentes.</summary>
    internal ContextMenu TesteMenuDaLigacao(int indiceDoFio)
    {
        if (indiceDoFio < 0 || indiceDoFio >= editor.Atual.Fios.Count) return null;
        var fio = editor.Atual.Fios[indiceDoFio];

        IEnumerable<ItemDeComponente> Todos(IEnumerable<ItemDeComponente> itens)
            => itens.SelectMany(i => new[] { i }.Concat(Todos(i.Filhos ?? [])));

        var item = Todos(ArvoreDeComponentes.Montar(editor.Atual, rede, editor.Queimadas))
            .FirstOrDefault(i => i.Fio == fio);
        if (item is null) return null;

        var menu = MenuDaLigacao(item);
        menu.PlacementTarget = this;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Center;
        menu.IsOpen = true;
        return menu;
    }

    /// <summary>Para o teste: o submenu "Exemplos prontos", montado como ao abrir o menu.</summary>
    internal MenuItem TesteMenuDeDemos()
    {
        MontarMenusDinamicos();
        return miDemos;
    }

    /// <summary>Para o passeio: a janela de configurações, sem prender em ShowDialog.</summary>
    internal JanelaConfiguracoes TesteCriarConfiguracoes() => CriarConfiguracoes();

    /// <summary>
    /// O acervo de bibliotecas. Leva o sketch da placa do editor junto:
    /// e com ele que a janela diz quais #include ficaram sem dono.
    /// </summary>
    void AbrirBibliotecas(object s = null, RoutedEventArgs e = null)
        => new JanelaBibliotecas(this, txtCodigo.Text).ShowDialog();

    /// <summary>
    /// Comeca um chip proprio: escreve o .chip.json e o .chip.ino de
    /// exemplo nos arquivos do projeto e abre o codigo.
    ///
    /// Os dois arquivos ficam DENTRO do projeto, e nao numa pasta do
    /// computador: assim o chip viaja junto no .sktch e o circuito
    /// entregue ao professor abre com o chip que o aluno escreveu.
    /// </summary>
    void NovoChip(object s = null, RoutedEventArgs e = null)
    {
        string nome = Dialogos.Perguntar(this, "Nome do chip", "Contador");
        if (nome is null) return;

        string baseDoNome = ChipCustom.Identificador(nome)[ChipCustom.Prefixo.Length..];
        if (baseDoNome.Length == 0) { Erro("esse nome não dá para usar em arquivo."); return; }

        editor.Atual.Arquivos ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        string oJson = baseDoNome + ".chip.json";
        string oCodigo = baseDoNome + ".chip.ino";

        if (editor.Atual.Arquivos.ContainsKey(oJson))
        {
            Erro($"já existe um chip chamado \"{baseDoNome}\" neste projeto.");
            return;
        }

        editor.Atual.Arquivos[oJson] = ChipCustom.ModeloJson(nome);
        editor.Atual.Arquivos[oCodigo] = ChipCustom.ModeloCodigo;

        editor.RecarregarChips();
        Empilhar();
        AbrirArquivoDoProjeto(oCodigo);

        Aviso($"chip \"{nome}\" criado: edite {oJson} para os pinos e "
              + $"{oCodigo} para o comportamento.");
    }

    internal JanelaBibliotecas TesteJanelaDeBibliotecas()
        => new(null, txtCodigo.Text);

    /// <summary>Os cartoes foram carregados nesta rodada? So entao o que esta neles e o estado do cartao.</summary>
    bool cartoesDaRodada;

    /// <summary>O .sktch leva os arquivos em bytes que o sketch gravou -- ate este tanto, somados.</summary>
    const long MaiorCartaoNoProjeto = 16L * 1024 * 1024;

    /// <summary>
    /// Parou: o que ficou no cartao volta para a peca -- o texto e os
    /// arquivos em bytes que o sketch gravou, e tambem o que ele apagou. Os
    /// do computador nao: eles entram de novo, do disco, na proxima rodada.
    /// </summary>
    void GuardarCartoes()
    {
        if (rede is null || !cartoesDaRodada) return;
        cartoesDaRodada = false;

        foreach (var (peca, maquina) in editor.CartoesDoCircuito(bancada, rede))
            if (maquina is not null) GuardarArmazenamento(maquina.Cartao, peca, "cartão");
        foreach (var (peca, maquina, _) in editor.PendrivesDoCircuito(bancada, rede))
            if (maquina is not null) GuardarArmazenamento(maquina.Pendrive, peca, "pendrive");
    }

    void GuardarArmazenamento(CartaoSd origem, PecaNoCircuito peca, string oQue)
    {
        var copia = origem.Copia();
        peca.Arquivos = copia.Count > 0 ? copia : null;

        var gravados = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (var (nome, bytes) in origem.BinariosGravados().OrderBy(b => b.Value.Length))
        {
            if (total + bytes.Length > MaiorCartaoNoProjeto)
            {
                console.Aviso($"{oQue} \"{peca.Nome ?? peca.Id}\": {nome} ({Acervo.EmTexto(bytes.Length)}) é grande demais para "
                              + $"ir dentro do projeto e sai do {oQue} ao parar. Salve-o no computador pela janela do {oQue} "
                              + "enquanto a simulação roda.");
                continue;
            }
            total += bytes.Length;
            gravados[nome] = Convert.ToBase64String(bytes);
        }
        peca.ArquivosGravados = gravados.Count > 0 ? gravados : null;
    }

    /// <summary>
    /// O cartao (ou o pendrive) de uma placa com o que esta na peca: os
    /// arquivos de texto, os que o sketch gravou em bytes, os do computador
    /// e a pasta do computador -- esses dois lidos do disco so quando o
    /// sketch abre, como um cartao que foi ao PC e voltou.
    /// </summary>
    void CarregarArmazenamento(CartaoSd destino, PecaNoCircuito peca, string oQue)
    {
        string nomeDaPeca = peca.Nome ?? peca.Id;
        if (peca.Arquivos is not null)
            foreach (var (nome, texto) in peca.Arquivos) destino.Gravar(nome, texto);

        // Os que o sketch gravou em bytes nas rodadas de antes.
        if (peca.ArquivosGravados is not null)
            foreach (var (nome, base64) in peca.ArquivosGravados)
            {
                try { destino.GravarBinario(nome, Convert.FromBase64String(base64)); }
                catch (FormatException) { console.Aviso($"{oQue} \"{nomeDaPeca}\": {nome} estava estragado no projeto e ficou de fora."); }
            }

        if (peca.ArquivosDoComputador is not null)
            foreach (var (nome, caminho) in peca.ArquivosDoComputador)
            {
                try { destino.GravarDoDisco(nome, caminho); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
                {
                    console.Aviso($"{oQue} \"{nomeDaPeca}\": não deu para ler {caminho} ({ex.Message}). "
                                  + $"O {nome} fica fora do {oQue} nesta rodada.");
                }
            }

        if (peca.PastaDoComputador is { } pasta)
        {
            if (Directory.Exists(pasta)) destino.PastaDoComputador = pasta;
            else console.Aviso($"{oQue} \"{nomeDaPeca}\": não achei a pasta {pasta}; o {oQue} fica sem ela nesta rodada.");
        }
    }

    TextBox CampoDoPonto(PecaNoCircuito peca, bool senha)
    {
        var campo = new TextBox
        {
            Width = senha ? 110 : 150,
            Margin = new Thickness(0, 0, 12, 0),
            Style = (Style)FindResource("CampoEscuro"),
            Text = (senha ? peca.Senha : peca.Ssid) ?? "",
            ToolTip = senha
                ? "Senha da rede; deixe vazio para uma rede aberta"
                : "Nome da rede que o WiFi.begin() do sketch tem que pedir",
            VerticalAlignment = VerticalAlignment.Center,
        };

        void Guardar()
        {
            string texto = campo.Text.Trim();
            if (senha) peca.Senha = texto.Length == 0 ? null : texto;
            else peca.Ssid = texto.Length == 0 ? null : texto;

            editor.AtualizarRotuloDe(peca);
        }

        campo.TextChanged += (_, _) => Guardar();
        campo.LostFocus += (_, _) => { Guardar(); Empilhar(); };
        campo.KeyDown += (_, ev) =>
        {
            if (ev.Key != Key.Enter) return;
            Guardar();
            Empilhar();
            Keyboard.ClearFocus();
            ev.Handled = true;
        };
        return campo;
    }

    internal void AbrirCartao(PecaNoCircuito peca)
    {
        bool rodando = btParar.IsEnabled;
        bool ehPendrive = peca.Tipo == "pendrive-usb";
        var maquina = ehPendrive
            ? editor.PendrivesDoCircuito(bancada, rede).FirstOrDefault(x => x.Peca.Id == peca.Id).Maquina
            : editor.CartoesDoCircuito(bancada, rede).FirstOrDefault(x => x.Peca.Id == peca.Id).Maquina;
        var memoria = maquina is null ? null : ehPendrive ? maquina.Pendrive : maquina.Cartao;

        var arquivos = rodando && memoria is not null
            ? memoria.Copia()
            : peca.Arquivos is null
                ? []
                : new Dictionary<string, string>(peca.Arquivos, StringComparer.OrdinalIgnoreCase);
        var doComputador = peca.ArquivosDoComputador is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(peca.ArquivosDoComputador, StringComparer.OrdinalIgnoreCase);
        var gravados = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var (nome, base64) in peca.ArquivosGravados ?? [])
        {
            try { gravados[nome] = Convert.FromBase64String(base64); }
            catch (FormatException) { }
        }

        // Rodando, os arquivos em bytes sao os do cartao de agora (o WAD
        // que entrou, o que o sketch gravou); parado, os que a peca aponta
        // e os que o sketch gravou nas rodadas de antes.
        var janela = new JanelaCartao(this, arquivos, rodando,
                         rodando ? null : doComputador,
                         rodando && memoria is not null ? memoria.Binarios() : null,
                         rodando ? null : gravados,
                         rodando && memoria is not null ? memoria.Bytes : null,
                         ehPendrive ? "Pendrive USB" : "Cartão microSD",
                         peca.PastaDoComputador);
        janela.ShowDialog();

        if (rodando) return;

        peca.PastaDoComputador = janela.PastaDoComputador;
        peca.Arquivos = arquivos.Count > 0 ? arquivos : null;
        peca.ArquivosDoComputador = doComputador.Count > 0 ? doComputador : null;
        peca.ArquivosGravados = gravados.Count > 0
            ? gravados.ToDictionary(g => g.Key, g => Convert.ToBase64String(g.Value), StringComparer.OrdinalIgnoreCase)
            : null;
        Empilhar();
        AtualizarBarraObjeto();
    }

    /// <summary>
    /// O editor, o monitor, o som, os fios e a escala -- as configuracoes
    /// que a Alpha 19 trouxe. Roda ao abrir e a cada mudanca na janela de
    /// configuracoes, entao tudo vale na hora.
    /// </summary>
    void AplicarPreferenciasDaAlpha19()
    {
        string fonte = Preferencias.Texto(Preferencias.FonteDoCodigo, "Consolas");
        txtCodigo.FontFamily = new FontFamily(fonte);
        txtCodigo.FontSize = Preferencias.Numero(Preferencias.TamanhoDoCodigo, 12, 8, 32);
        txtCodigo.ShowLineNumbers = Preferencias.Ligado(Preferencias.NumerosDeLinha, true);
        txtCodigo.WordWrap = Preferencias.Ligado(Preferencias.QuebrarLinhas, false);
        txtCodigo.Options.HighlightCurrentLine = Preferencias.Ligado(Preferencias.DestacarLinha, true);
        txtCodigo.Options.IndentationSize = Preferencias.Numero(Preferencias.TamanhoDoRecuo, 2, 1, 8);
        txtCodigo.Options.ConvertTabsToSpaces = Preferencias.Ligado(Preferencias.TabViraEspacos, true);
        bool espacos = Preferencias.Ligado(Preferencias.MostrarEspacos, false);
        txtCodigo.Options.ShowSpaces = espacos;
        txtCodigo.Options.ShowTabs = espacos;

        txtSerial.FontSize = Preferencias.Numero(Preferencias.TamanhoDoSerial, 11, 8, 24);
        txtSerial.LineHeight = Math.Round(txtSerial.FontSize * 15 / 11.0);
        RedesenharSerial();

        GeradorDeOnda.Volume = Preferencias.Texto(Preferencias.VolumeDoBuzzer, "medio") switch
        {
            "baixo" => 0.4,
            "alto" => 1.6,
            _ => 1.0,
        };
        ultimoHz = -1;   // o proximo quadro reaplica o som, ligado ou mudo

        editor.FatorDosFios = Preferencias.Texto(Preferencias.EspessuraDoFio, "normal") switch
        {
            "fina" => 0.7,
            "grossa" => 1.45,
            _ => 1.0,
        };

        AplicarEscala(Preferencias.Numero(Preferencias.EscalaDaInterface, 100, 75, 175) / 100.0);
    }

    /// <summary>A interface inteira maior ou menor, como o zoom do navegador.</summary>
    void AplicarEscala(double escala)
    {
        if (Content is not FrameworkElement raiz) return;
        raiz.LayoutTransform = Math.Abs(escala - 1) < 0.001
            ? Transform.Identity
            : new ScaleTransform(escala, escala);
    }

    // ================= editor: zoom e pares =================

    /// <summary>Ctrl + roda do mouse: a letra do codigo cresce e diminui, e fica guardada.</summary>
    void ZoomDoCodigo(object s, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
        if (!Preferencias.Ligado(Preferencias.ZoomComRoda, true)) return;

        double tamanho = Math.Clamp(txtCodigo.FontSize + (e.Delta > 0 ? 1 : -1), 8, 32);
        txtCodigo.FontSize = tamanho;
        Preferencias.Definir(Preferencias.TamanhoDoCodigo, ((int)tamanho).ToString());
        e.Handled = true;
    }

    static readonly Dictionary<char, char> ParesDoCodigo = new()
    {
        ['('] = ')', ['['] = ']', ['{'] = '}', ['"'] = '"', ['\''] = '\'',
    };

    /// <summary>
    /// Abriu parentese, colchete, chave ou aspas: o fechamento entra junto,
    /// com o cursor no meio -- como na Arduino IDE 2.
    /// </summary>
    void FecharOPar(object s, TextCompositionEventArgs e)
    {
        if (e.Text.Length != 1 || !ParesDoCodigo.TryGetValue(e.Text[0], out char fecha)) return;
        if (!Preferencias.Ligado(Preferencias.FecharPares, true)) return;

        var doc = txtCodigo.Document;
        int pos = txtCodigo.CaretOffset;
        char depois = pos < doc.TextLength ? doc.GetCharAt(pos) : '\0';
        char antes = pos >= 2 ? doc.GetCharAt(pos - 2) : '\0';

        // Colado numa palavra, nao: "a(" no meio de um nome existente viraria
        // lixo. E aspas depois de letra ou de barra sao fechamento ou escape.
        if (char.IsLetterOrDigit(depois) || depois == '_') return;
        if (e.Text[0] is '"' or '\'' && (char.IsLetterOrDigit(antes) || antes == '\\')) return;

        doc.Insert(pos, fecha.ToString());
        txtCodigo.CaretOffset = pos;
    }

    /// <summary>Digitou o fechamento em cima do que entrou sozinho: so passa por cima.</summary>
    void PassarPorCimaDoPar(object s, TextCompositionEventArgs e)
    {
        if (e.Text.Length != 1 || e.Text[0] is not (')' or ']' or '}' or '"' or '\'')) return;
        if (!Preferencias.Ligado(Preferencias.FecharPares, true)) return;

        int pos = txtCodigo.CaretOffset;
        if (pos < txtCodigo.Document.TextLength && txtCodigo.Document.GetCharAt(pos) == e.Text[0])
        {
            txtCodigo.CaretOffset = pos + 1;
            e.Handled = true;
        }
    }

    /// <summary>
    /// Enter entre "{" e "}": a chave de fechar desce, e o cursor fica numa
    /// linha recuada no meio -- sem isto a chave que fechou sozinha ficava
    /// grudada no fim da linha nova.
    /// </summary>
    void EnterEntreChaves(object s, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || Keyboard.Modifiers != ModifierKeys.None) return;
        if (!Preferencias.Ligado(Preferencias.FecharPares, true)) return;

        var doc = txtCodigo.Document;
        int pos = txtCodigo.CaretOffset;
        if (pos == 0 || pos >= doc.TextLength
            || doc.GetCharAt(pos - 1) != '{' || doc.GetCharAt(pos) != '}') return;

        var linha = doc.GetLineByOffset(pos);
        string texto = doc.GetText(linha);
        string recuo = new([.. texto.TakeWhile(c => c is ' ' or '\t')]);
        string passo = txtCodigo.Options.IndentationString;
        string quebra = ICSharpCode.AvalonEdit.Document.TextUtilities.GetNewLineFromDocument(doc, linha.LineNumber);

        doc.Insert(pos, quebra + recuo + passo + quebra + recuo);
        txtCodigo.CaretOffset = pos + quebra.Length + recuo.Length + passo.Length;
        e.Handled = true;
    }

    string ResumoDosServidores()
    {
        var linhas = new List<string>
        {
            redeSimulada.UsarInternet
                ? "internet do computador: ligada"
                : "internet do computador: desligada",
            ResumoDoBroker(),
        };

        foreach (var servidor in redeSimulada.Servidores)
            if (servidor.UrlLocal is not null)
                linhas.Add($"{servidor.Dona.Nome}: {servidor.UrlLocal}");

        return string.Join("\n", linhas);
    }

    /// <summary>
    /// O broker MQTT em uma linha -- e nunca "localhost:0".
    ///
    /// Porta 0 quer dizer que ele não está aberto, e isso tem dois
    /// motivos bem diferentes: a simulação está parada (ele só abre ao
    /// rodar), ou a 1883 já era de outro broker no computador, e aí as
    /// placas conversam entre si mas nenhum app de fora as enxerga.
    /// </summary>
    string ResumoDoBroker()
    {
        if (redeSimulada.PortaDoBroker > 0)
            return $"broker MQTT em localhost:{redeSimulada.PortaDoBroker}";

        return btParar.IsEnabled
            ? "broker MQTT só dentro do programa: a porta 1883 já é de outro broker "
              + "neste computador, então apps de fora não enxergam as placas."
            : "broker MQTT: abre em localhost:1883 quando a simulação roda.";
    }

    string ResumoDoBluetooth()
    {
        var nomes = redeSimulada.DispositivosVisiveis().ToList();
        if (nomes.Count == 0)
            return "nenhuma placa com SerialBT.begin() rodando agora.";

        string conectado = redeSimulada.CelularConectadoEm;
        return string.Join("\n", nomes.Select(n =>
            n == conectado ? $"{n} (conectada)" : n));
    }

    void TelaCheia(object s = null, RoutedEventArgs e = null)
    {
        if (WindowStyle == WindowStyle.None)
        {
            WindowStyle = WindowStyle.SingleBorderWindow;
            WindowState = WindowState.Normal;
        }
        else
        {
            WindowStyle = WindowStyle.None;
            WindowState = WindowState.Maximized;
        }
    }

    void Ajuda(object s = null, RoutedEventArgs e = null)
    {
        MessageBox.Show(this,
            """
            CIRCUITO
              Arrastar peça .............. segurar e mover
              Selecionar várias .......... arrastar no vazio (laço)
              Somar à seleção ............ Ctrl ou Shift + clique
              Ligar fio .................. clicar num pino, depois no outro
              Selecionar fio ............. clicar em cima dele (Del apaga)
              Vários fios de uma vez ..... arrastar no vazio por cima deles
              Traçado do fio ............. tab do fio: solto, reto ou degrau
              Moldar o fio ............... selecione e arraste as bolinhas
              Novo canto ................. arraste a bolinha vazada do meio
              Tirar um canto ............. clique duplo em cima dele
              Cancelar fio ............... clicar no vazio
              Mover a tela ............... botão do meio ou direito
              Zoom ....................... roda do mouse
              Sem encaixe na grade ....... segurar Alt ao arrastar
              Apertar um botão ........... segurar o mouse sobre ele
              Reiniciar a placa .......... clicar no RESET dela (EN na ESP32);
                                           segurando, ela fica parada
              BOOT da ESP32 .............. segurar leva o GPIO 0 a LOW

            ABA COMPONENTES
              Clique direito numa ligação  trocar o pino da placa, a cor e
                                           o traçado do fio, ou apagá-lo

            CÓDIGO
              Linguagens ................. Arduino (C/C++), C, MicroPython,
                                           C#, Rust, Ada, SPARK, Cowgol,
                                           Millipascal e Assembly: o programa
                                           reconhece pelo próprio código
              Assembly ................... AVR de verdade, só no Uno e no
                                           Nano (ATmega328P), no ritmo dos
                                           16 MHz do chip
              Gravar na placa ............ menu ⋮ > Exportar programa
                                           gravável (.hex)

            PAINEL DE BAIXO
              CONSOLE .................... avisos e erros do simulador
              MONITOR SERIAL ............. o que o sketch imprime; o campo
                                           de baixo manda texto para ele
              REDE ....................... WiFi, HTTP e MQTT: o que cada placa
                                           fez, o link do servidor para abrir
                                           no navegador e o envio de MQTT
              BLUETOOTH .................. faz o papel do celular: conecta na
                                           placa e troca mensagens
              ↗ ao lado de cada aba ...... solta só aquela aba numa janela;
                                           arraste de volta para encaixar

            ATALHOS (os de fábrica; troque em Configurações)
              A .......... adicionar componente
              R .......... girar a peça 90°
              M .......... espelhar a peça
              setas ...... mover o cenário
              Esc ........ cancelar a ligação em curso
              F5 ......... rodar / parar
              F6 ......... pausar / continuar (depurador)
              F9 ......... ponto de parada na linha do cursor
              F10 ........ andar uma linha
              F .......... ajustar à tela
              + / - ...... aproximar / afastar
              G .......... alternar grade
              Del ........ excluir seleção
              Ctrl+S ..... salvar o projeto (.sktch: circuito + código)
              Ctrl+O ..... abrir projeto
              Ctrl+Z ..... desfazer
              Ctrl+Y ..... refazer
              Ctrl+C ..... copiar peça
              Ctrl+V ..... colar peça
              Ctrl+I ..... importar circuito (diagram.json)
              Ctrl+M ..... monitor serial
              Alt+Enter .. tela cheia
              ? .......... esta ajuda
            """,
            "Ajuda e atalhos", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    // ================= atalhos =================
    void Atalhos(object s, KeyEventArgs e)
    {
        bool noTexto = txtCodigo.IsKeyboardFocusWithin || EscrevendoEmCampo();
        var tecla = e.Key == Key.System ? e.SystemKey : e.Key;
        var mods = Keyboard.Modifiers;

        if (mods.HasFlag(ModifierKeys.Alt) && tecla is Key.Enter or Key.System)
        {
            TelaCheia();
            e.Handled = true;
            return;
        }

        switch (tecla)
        {
            case Key.Escape:
                if (modoDaTela == "circuito" && !noTexto && (editor.PontaVermelha is not null || editor.PontaPreta is not null))
                {
                    editor.TirarPontas();
                    Aviso("multímetro: pontas tiradas.");
                }
                else if (editor.LigandoFio) { editor.CancelarFio(); Aviso("ligação cancelada"); }
                else editor.Focus();
                e.Handled = true;
                return;

            case Key.Left when !noTexto: editor.Deslocar(60, 0); e.Handled = true; return;
            case Key.Right when !noTexto: editor.Deslocar(-60, 0); e.Handled = true; return;
            case Key.Up when !noTexto: editor.Deslocar(0, 60); e.Handled = true; return;
            case Key.Down when !noTexto: editor.Deslocar(0, -60); e.Handled = true; return;

            case Key.Add or Key.OemPlus when !noTexto: Aproximar(); e.Handled = true; return;
            case Key.Subtract or Key.OemMinus when !noTexto: Afastar(); e.Handled = true; return;
        }

        string acao = AtalhosDoPrograma.Id(tecla, mods);
        if (acao is null) return;

        bool comModificador = mods != ModifierKeys.None;
        if (noTexto && !comModificador && acao is not ("pausar" or "pausa-na-linha" or "passo"))
            return;

        if (!comModificador && !AtalhoDeUmaTeclaVale()
            && acao is not ("rodar" or "pausar" or "pausa-na-linha" or "passo"))
            return;

        // No multimetro o circuito nao se edita: so se mede.
        if (modoDaTela == "circuito" && !noTexto
            && acao is "desfazer" or "refazer" or "colar" or "girar" or "espelhar" or "excluir")
        {
            Aviso("no multímetro o circuito não se edita: volte para SIMULAÇÃO para mexer nele.");
            e.Handled = true;
            return;
        }

        switch (acao)
        {
            case "rodar": if (btParar.IsEnabled) Parar(); else Rodar(); break;
            case "pausar": PausarOuContinuar(); break;
            case "pausa-na-linha": AlternarPausa(txtCodigo.TextArea.Caret.Line); break;
            case "passo": Passo(); break;
            case "verificar": Verificar(); break;
            case "mandar": MandarParaPlaca(); break;

            case "novo": NovoCircuito(); break;
            case "salvar": SalvarCircuito(); break;
            case "salvar-como": SalvarComo(); break;
            case "abrir": AbrirCircuito(); break;
            case "importar": ImportarDoWokwi(); break;
            case "bibliotecas": AbrirBibliotecas(); break;
            case "monitor": FocarSerial(); break;

            case "desfazer": if (noTexto) return; Desfazer(); break;
            case "refazer": if (noTexto) return; Refazer(); break;
            case "copiar": if (noTexto) return; Copiar(); break;
            case "colar": if (noTexto) return; Colar(); break;

            case "adicionar": AdicionarPeca(); break;
            case "ajustar": Ajustar(); break;
            case "grade": AlternarGrade(); break;
            case "girar": editor.Girar(); break;
            case "espelhar": editor.Espelhar(); break;
            case "excluir": Excluir(); break;
            case "ajuda": Ajuda(); break;

            default: return;
        }

        e.Handled = true;
    }

    /// <summary>Alguma peca do circuito esta esperando tecla?</summary>
    bool UsaTeclado()
        => editor.Atual.Pecas.Any(x => x.Teclas is { Count: > 0 });

    /// <summary>
    /// Os atalhos de UMA TECLA valem agora?
    ///
    /// Rodando um circuito com teclas vinculadas, nao: as teclas soltas
    /// sao do circuito. Sem isto nao ha como jogar -- o "A" abriria a
    /// paleta de pecas no meio da partida, o "R" giraria a peca
    /// selecionada e as setas arrastariam o cenario.
    ///
    /// Os atalhos com Ctrl, o F5 e os do depurador continuam valendo:
    /// eles sao tratados antes e nem chegam aqui.
    /// </summary>
    static bool EscrevendoEmCampo()
        => Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase
           or System.Windows.Controls.ComboBox
           or System.Windows.Controls.PasswordBox;

    bool AtalhoDeUmaTeclaVale() => !editor.EmSimulacao || !UsaTeclado();

    internal bool TesteAtalhoDeUmaTeclaVale() => AtalhoDeUmaTeclaVale();

    /// <summary>
    /// Escolhe as teclas e os botoes de controle de uma peca.
    ///
    /// Uma linha por acao: o nome, o que esta ligado nela agora, um
    /// botao que espera a proxima tecla e uma lista com os controles do
    /// gamepad. Espera a tecla em vez de pedir para escolher numa lista
    /// gigante -- e o jeito de todo jogo, e e o unico que nao exige
    /// saber como o programa chama cada tecla.
    /// </summary>
    void EscolherTeclas(PecaNoCircuito peca)
    {
        var acoes = Vinculos.Acoes(peca.Tipo);
        if (acoes.Count == 0) return;

        peca.Teclas ??= [];

        var janela = new Window
        {
            Owner = this,
            Title = $"Teclas de {Catalogo.Obter(peca.Tipo)?.Nome ?? peca.Tipo}",
            SizeToContent = SizeToContent.Height,
            Width = 470,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E)),
        };

        var coluna = new StackPanel { Margin = new Thickness(16) };

        coluna.Children.Add(new TextBlock
        {
            Text = "Vale enquanto a simulação estiver rodando. "
                 + "Clique em \"Trocar\" e aperte a tecla, ou escolha um botão do controle.",
            Foreground = Cinza,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 14),
        });

        foreach (var (acao, rotulo) in acoes)
        {
            var linha = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };

            var nome = new TextBlock
            {
                Text = rotulo,
                Foreground = Brushes.White,
                FontSize = 13,
                Width = 150,
                VerticalAlignment = VerticalAlignment.Center,
            };
            DockPanel.SetDock(nome, Dock.Left);
            linha.Children.Add(nome);

            var atual = new TextBlock
            {
                Text = Vinculos.Rotulo(peca.Teclas.GetValueOrDefault(acao)),
                Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xD5, 0x4F)),
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Width = 120,
                VerticalAlignment = VerticalAlignment.Center,
            };
            DockPanel.SetDock(atual, Dock.Left);
            linha.Children.Add(atual);

            var limpar = new Button
            {
                Content = "Tirar",
                Style = (Style)FindResource("BotaoPequeno"),
                Margin = new Thickness(6, 0, 0, 0),
            };
            limpar.Click += (_, _) =>
            {
                peca.Teclas.Remove(acao);
                atual.Text = Vinculos.Rotulo(null);
                Empilhar();
            };
            DockPanel.SetDock(limpar, Dock.Right);
            linha.Children.Add(limpar);

            var doControle = new Button
            {
                Content = "Controle ▾",
                Style = (Style)FindResource("BotaoPequeno"),
                Margin = new Thickness(6, 0, 0, 0),
            };
            doControle.Click += (_, _) =>
            {
                var menu = new ContextMenu { PlacementTarget = doControle };
                foreach (var (codigo, comoChama) in Vinculos.DoControle)
                {
                    var item = new MenuItem { Header = comoChama };
                    item.Click += (_, _) =>
                    {
                        peca.Teclas[acao] = codigo;
                        atual.Text = Vinculos.Rotulo(codigo);
                        Empilhar();
                    };
                    menu.Items.Add(item);
                }
                menu.IsOpen = true;
            };
            DockPanel.SetDock(doControle, Dock.Right);
            linha.Children.Add(doControle);

            var trocar = new Button
            {
                Content = "Trocar",
                Style = (Style)FindResource("BotaoPequeno"),
            };
            trocar.Click += (_, _) =>
            {
                trocar.Content = "aperte…";
                atual.Text = "…";

                void Pegar(object _, KeyEventArgs ev)
                {
                    var tecla = ev.Key == Key.System ? ev.SystemKey : ev.Key;
                    ev.Handled = true;

                    janela.PreviewKeyDown -= Pegar;
                    trocar.Content = "Trocar";

                    if (Vinculos.Reservada(tecla))
                    {
                        atual.Text = Vinculos.Rotulo(peca.Teclas.GetValueOrDefault(acao));
                        Aviso($"a tecla {Vinculos.NomeDaTecla(tecla)} é do programa e "
                              + "não pode virar atalho de peça");
                        return;
                    }

                    string vinculo = Vinculos.DeTecla(tecla);
                    peca.Teclas[acao] = vinculo;
                    atual.Text = Vinculos.Rotulo(vinculo);
                    Empilhar();
                }

                janela.PreviewKeyDown += Pegar;
            };
            DockPanel.SetDock(trocar, Dock.Right);
            linha.Children.Add(trocar);

            coluna.Children.Add(linha);
        }

        var fechar = new Button
        {
            Content = "Fechar",
            Style = (Style)FindResource("BotaoPequeno"),
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0),
        };
        fechar.Click += (_, _) => janela.Close();
        coluna.Children.Add(fechar);

        janela.Content = coluna;
        janela.ShowDialog();
        AtualizarBarraObjeto();
    }

    // ================= arquivos =================

    /// <summary>
    /// O arquivo do projeto aberto: de onde ele veio, ou onde foi salvo por
    /// ultimo. E nele que o Ctrl+S grava. Nulo -- circuito novo, demo,
    /// diagrama importado --, o Ctrl+S pergunta onde.
    /// </summary>
    string caminhoDoProjeto;

    DispatcherTimer salvador;
    DateTime ultimoSalvamento = DateTime.Now;

    /// <summary>
    /// Ctrl+S: grava no arquivo do projeto. Antes TODO salvar abria o
    /// dialogo e perguntava o nome de novo -- e nao havia como o programa
    /// salvar sozinho.
    /// </summary>
    void SalvarCircuito(object s = null, RoutedEventArgs e = null)
    {
        if (caminhoDoProjeto is null) { SalvarComo(); return; }
        SalvarEm(caminhoDoProjeto);
    }

    /// <summary>Salvar como: sempre pergunta onde.</summary>
    void SalvarComo(object s = null, RoutedEventArgs e = null)
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "Projeto do simulador (*.sktch)|*.sktch",
            DefaultExt = Sktch.Extensao,
            FileName = caminhoDoProjeto is null ? "projeto" + Sktch.Extensao
                                                : System.IO.Path.GetFileName(caminhoDoProjeto),
        };
        if (caminhoDoProjeto is not null)
            dlg.InitialDirectory = System.IO.Path.GetDirectoryName(caminhoDoProjeto);
        if (dlg.ShowDialog() != true) return;

        SalvarEm(dlg.FileName);
    }

    bool SalvarEm(string caminho, bool sozinho = false)
    {
        try
        {
            GuardarCodigoDoEditor();

            // A copia de seguranca: o arquivo de antes vira .bak, e um
            // salvamento errado tem volta.
            if (Preferencias.Ligado(Preferencias.CopiaDeSeguranca, false) && File.Exists(caminho))
                File.Copy(caminho, caminho + ".bak", overwrite: true);

            // O autor fica gravado no projeto ao salvar: mudar o das
            // configuracoes depois nao troca o autor de quem ja salvou.
            editor.Atual.Autor ??= AutorPadrao();
            Sktch.Salvar(caminho, editor.Atual, CodigoPrincipal(), txtDescricao.Text);
            circuitoSalvo = AssinaturaAtual();
            caminhoDoProjeto = caminho;
            ultimoSalvamento = DateTime.Now;
            Recentes.Registrar(caminho);

            // A foto do circuito e o ultimo projeto: os dois sao para a
            // tela de abertura, e o momento de tirar a foto e este --
            // o circuito esta na tela e acabou de ser dado por pronto.
            Miniaturas.Guardar(caminho, editor);
            Preferencias.Definir(Preferencias.UltimoProjeto, caminho);
            Title = $"CircuitSketch - {System.IO.Path.GetFileName(caminho)}";
            Aviso(sozinho ? $"salvo sozinho: {System.IO.Path.GetFileName(caminho)}"
                          : $"projeto salvo em {System.IO.Path.GetFileName(caminho)}");
            return true;
        }
        catch (Exception ex)
        {
            Erro($"não deu para salvar: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// O salvamento automatico: so projeto que ja tem arquivo, so com
    /// mudanca, e so a cada N minutos das configuracoes. Circuito novo e
    /// demo nao tem para onde ir sem perguntar -- e perguntar sozinho seria
    /// pior do que nao salvar.
    /// </summary>
    internal bool SalvarSozinhoSePrecisar(bool agora = false)
    {
        int minutos = Preferencias.Numero(Preferencias.SalvarSozinho, 0, 0, 60);
        if (minutos <= 0 || caminhoDoProjeto is null) return false;
        if (!agora && DateTime.Now - ultimoSalvamento < TimeSpan.FromMinutes(minutos)) return false;

        ultimoSalvamento = DateTime.Now;
        GuardarCodigoDoEditor();
        if (AssinaturaAtual() == circuitoSalvo) return false;
        return SalvarEm(caminhoDoProjeto, sozinho: true);
    }

    /// <summary>
    /// Circuito novo, do zero. Antes so dava para comecar de novo fechando
    /// e abrindo o programa -- e ai voltava o circuito de exemplo.
    /// </summary>
    void NovoCircuito(object s = null, RoutedEventArgs e = null)
    {
        if (!PodeDescartar()) return;

        // As configuracoes podem dizer com qual placa o circuito novo nasce.
        string placa = Preferencias.Texto(Preferencias.PlacaDoCircuitoNovo);
        if (placa.Length > 0 && PerfilPlaca.EhPlaca(placa)) ComecarComPlaca(placa);
        else ComecarCircuitoNovo();
    }

    void ComecarCircuitoNovo()
    {
        RecomecarSessao("circuito novo");
        caminhoDoProjeto = null;
        editor.Carregar(new Circuito());
        editor.AplicarGrade();
        AdotarCodigo(ModeloDeSketch);
        txtDescricao.Text = "";

        assinaturaInicial = AssinaturaAtual();
        circuitoSalvo = null;
        RecomecarHistorico();
        ReconstruirRede();
        Title = "CircuitSketch";
        Aviso("circuito novo: arraste uma placa da paleta (A) para comecar.");
    }

    /// <summary>
    /// Pergunta antes de jogar fora o que nao foi salvo. Vale para o Novo
    /// e para o Abrir -- abrir outro projeto tambem descartava tudo sem
    /// avisar.
    /// </summary>
    bool PodeDescartar()
    {
        // O autoteste e o passeio nao tem quem responda a pergunta.
        if (Autoteste.Rodando || Passeio.Ativo) return true;
        if (AssinaturaAtual() == (circuitoSalvo ?? assinaturaInicial)) return true;

        var r = MessageBox.Show(this,
            "O projeto tem alterações não salvas.\n\nSalvar antes?",
            "Alterações não salvas", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);

        if (r == MessageBoxResult.Cancel) return false;
        if (r != MessageBoxResult.Yes) return true;

        SalvarCircuito();
        // Cancelou o diálogo de salvar: não descarta nada.
        return AssinaturaAtual() == circuitoSalvo;
    }

    void AbrirCircuito(object s = null, RoutedEventArgs e = null)
    {
        if (!PodeDescartar()) return;

        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            // O .json entra junto: e o que o programa salvava antes.
            Filter = "Projeto do simulador (*.sktch;*.json)|*.sktch;*.json"
        };
        if (dlg.ShowDialog() != true) return;

        AbrirProjetoDe(dlg.FileName);
    }

    public void CarregarSketch(string caminho)
    {
        string arquivo = Directory.Exists(caminho)
            ? Directory.GetFiles(caminho, "*.ino").FirstOrDefault()
              ?? Directory.GetFiles(caminho, "*.py").FirstOrDefault()
            : caminho;

        if (arquivo is null || !File.Exists(arquivo))
        {
            Aviso($"não achei nenhum .ino nem .py em: {caminho}");
            return;
        }

        // Projeto inteiro em vez de so o codigo. Vale para quem arrasta um
        // .sktch para cima do programa ou o abre com dois cliques: antes
        // isso carregava o JSON como se fosse codigo do sketch.
        if (arquivo.EndsWith(Sktch.Extensao, StringComparison.OrdinalIgnoreCase))
        {
            AbrirProjetoDe(arquivo);
            return;
        }

        // O .ino e da placa que esta no campo: quem manda um sketch de
        // fora esta mandando para a placa que ele esta olhando.
        trocandoCodigo = true;
        try { txtCodigo.Text = File.ReadAllText(arquivo); }
        finally { trocandoCodigo = false; }
        GuardarCodigoDoEditor();
        circuitoSalvo = AssinaturaAtual();
        Recentes.Registrar(arquivo);
        Title = $"CircuitSketch - {System.IO.Path.GetFileName(arquivo)}";

        // Veio da Arduino IDE: já começa rodando.
        Dispatcher.BeginInvoke(() => Rodar(), DispatcherPriority.Loaded);
    }

    /// <summary>
    /// Comeca um circuito novo ja com uma placa no meio da tela.
    ///
    /// E o caminho da tela de abertura: escolher "Arduino Uno" la tem
    /// que dar num circuito com um Arduino Uno, e nao num vazio.
    /// </summary>
    internal void ComecarComPlaca(string tipo)
    {
        ComecarCircuitoNovo();

        var def = Catalogo.Obter(tipo);
        if (def is null) return;

        editor.Adicionar(tipo);
        Preferencias.Definir(Preferencias.UltimaPlaca, tipo);
        ReconstruirRede();
        editor.Ajustar();
        AtualizarDica();

        assinaturaInicial = AssinaturaAtual();
    }

    /// <summary>Abre um projeto escolhido de fora (tela de abertura).</summary>
    internal void AbrirProjeto(string caminho) => AbrirProjetoDe(caminho);

    /// <summary>Abre um .sktch por caminho, sem dialogo.</summary>
    void AbrirProjetoDe(string caminho)
    {
        try
        {
            var (circuito, codigo, descricao) = Sktch.AbrirCompleto(caminho);
            RecomecarSessao($"projeto aberto: {System.IO.Path.GetFileName(caminho)}");
            editor.Carregar(circuito);
            editor.RecarregarChips();
            editor.AplicarGrade();
            AdotarCodigo(codigo);
            txtDescricao.Text = descricao ?? "";

            circuitoSalvo = AssinaturaAtual();
            RecomecarHistorico();
            ReconstruirRede();
            editor.Ajustar();
            Title = $"CircuitSketch - {System.IO.Path.GetFileName(caminho)}";
            Recentes.Registrar(caminho);
            Preferencias.Definir(Preferencias.UltimoProjeto, caminho);
            caminhoDoProjeto = caminho;

            if (editor.Atual.Placas().FirstOrDefault() is { } primeira)
                Preferencias.Definir(Preferencias.UltimaPlaca, primeira.Tipo);

            // Antes de rodar: se o circuito de verdade estiver guardado
            // na descricao, e melhor montar ele do que rodar o esqueleto.
            Dispatcher.BeginInvoke(OferecerDiagramaDaDescricao, DispatcherPriority.Loaded);
            if (Preferencias.Ligado(Preferencias.RodarAoAbrirProjeto, true))
                Dispatcher.BeginInvoke(() => Rodar(), DispatcherPriority.Background);
        }
        catch (Exception ex) { Erro($"não deu para abrir: {ex.Message}"); }
    }

    /// <summary>Usado pelo modo de captura para mostrar a tab do objeto.</summary>
    /// <summary>Abre um circuito por caminho, sem dialogo.</summary>
    internal void CarregarCircuitoDe(string caminho)
    {
        if (!File.Exists(caminho)) { Erro($"circuito nao encontrado: {caminho}"); return; }
        var (circuito, codigo, descricao) = Sktch.AbrirCompleto(caminho);
        RecomecarSessao($"projeto aberto: {System.IO.Path.GetFileName(caminho)}");
        editor.Carregar(circuito);
        editor.RecarregarChips();
        editor.AplicarGrade();
        AdotarCodigo(codigo);
        txtDescricao.Text = descricao ?? "";
        circuitoSalvo = AssinaturaAtual();
        RecomecarHistorico();
        ReconstruirRede();
        editor.Ajustar();

        // Sem isto a barra continua contando as pecas do circuito
        // anterior -- abrir um arquivo de 27 pecas e continuar lendo
        // "8 peças" faz duvidar do que abriu.
        AtualizarDica();
    }

    // ---- ganchos do teste do codigo por placa ----
    internal Circuito TesteCircuito => editor.Atual;
    internal string TesteCodigo => txtCodigo.Text;

    internal List<string> TesteAbasDeArquivo =>
        [.. abasDeArquivo.Children.OfType<Button>().Select(b => (string)b.Content)];

    internal string TesteArquivoAberto => arquivoAberto;

    internal bool TesteCodigoSoLeitura => txtCodigo.IsReadOnly;

    internal void TesteAbrirArquivo(string nome) => AbrirArquivoDoProjeto(nome);

    internal void TesteCriarArquivo(string nome, string conteudo)
    {
        editor.Atual.Arquivos ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        editor.Atual.Arquivos[nome] = conteudo;
        AbrirArquivoDoProjeto(nome);
    }

    /// <summary>
    /// Aplica o diagrama JA, sem esperar o relogio.
    ///
    /// Na janela de verdade a aplicacao e adiada meio segundo para nao
    /// reagir a cada tecla; o teste nao tem meio segundo para esperar.
    /// </summary>
    internal void TesteAplicarDiagrama() => AplicarDiagrama();

    /// <summary>Solta ou devolve um painel, como o botao da seta faz.</summary>
    /// <summary>Para o teste: poe um aviso, como um erro de compilacao poria.</summary>
    internal void TesteAvisar(string msg) => Aviso(msg);

    /// <summary>Para o teste: solta ou devolve uma das abas de baixo.</summary>
    internal void TesteSoltarAba(string qual) => SoltarAba(qual);

    /// <summary>Para o teste: esta aba esta aparecendo em algum lugar?</summary>
    internal bool TesteAbaAVista(string qual)
        => AbasDeBaixo().FirstOrDefault(a => a.Nome == qual).Painel?.Visibility
           == Visibility.Visible;

    internal void TesteSoltarPainel(string qual)
    {
        switch (qual)
        {
            case "codigo": SoltarPainelDoCodigo(); break;
            case "baixo": SoltarPainelDeBaixo(); break;
            case "componentes": SoltarComponentes(); break;
        }
    }

    /// <summary>O painel esta dentro da janela principal agora?</summary>
    internal bool TestePainelEstaEmCasa(string qual)
    {
        var painel = qual switch
        {
            "codigo" => (UIElement)painelDoCodigo,
            "baixo" => painelDeBaixo,
            "componentes" => painelComponentes,
            _ => AbasDeBaixo().FirstOrDefault(a => a.Nome == qual).Painel,
        };

        if (painel is null) return false;

        // Arvore LOGICA, e nao visual: no autoteste a janela nunca e
        // mostrada, e a arvore visual so nasce na primeira renderizacao.
        for (var no = (DependencyObject)painel; no is not null;
             no = LogicalTreeHelper.GetParent(no))
            if (ReferenceEquals(no, this)) return true;

        return false;
    }

    /// <summary>
    /// A conta de memoria da placa, e a recusa quando nao cabe.
    ///
    /// Alpha 18: "limitadores de placas, para RAM de cada placa,
    /// processador, cores, e etc. para ter uma experiencia mais
    /// acurada". Recusar so acontece acima de 100%: a conta e uma
    /// estimativa, e ela erra PARA MENOS de proposito -- recusar um
    /// sketch que funciona seria muito pior do que deixar passar um
    /// que aperta.
    /// </summary>
    bool Couber(PecaNoCircuito placa, Programa prog)
    {
        var perfil = PerfilPlaca.De(placa.Tipo);
        if (perfil is null) return true;

        var conta = Memoria.Estimar(prog, perfil);

        console.Info($"{perfil.Nome}: {perfil.Ficha}");
        foreach (string linha in conta.EmTexto().Split(Environment.NewLine))
            console.Info("  " + linha);

        if (conta.Estourou)
        {
            // O Erro() ja registra no console: registrar aqui tambem
            // punha a mesma frase duas vezes seguidas.
            Erro($"não cabe na placa — {conta.PorQueNaoCabe()}");

            interpretadores.Clear();
            depuradores.Clear();
            programas.Clear();
            return false;
        }

        if (conta.RamApertada)
            console.Aviso($"sobra pouca RAM ({conta.UsoDaRam:P0} em uso): "
                          + "com o programa rodando, a placa pode reiniciar sozinha.");

        return true;
    }

    /// <summary>O nome da placa como o console a chama.</summary>
    static string NomeDaPlaca(PecaNoCircuito placa)
        => PerfilPlaca.De(placa.Tipo)?.Nome ?? Catalogo.Obter(placa.Tipo)?.Nome ?? placa.Tipo;

    /// <summary>Para o teste: queima uma peca, como o estouro queimaria.</summary>
    internal void TesteQueimar(string id) => editor.Queimar(id);

    /// <summary>Para o teste: desfaz as queimaduras, como uma execucao nova faz.</summary>
    internal void TesteApagarQueimaduras() => editor.ApagarQueimaduras();

    /// <summary>Apaga a peca selecionada, como a tecla Del faz.</summary>
    internal void TesteRemoverSelecionada() => editor.RemoverSelecionada();

    /// <summary>A ultima mensagem que apareceu na barra.</summary>
    internal string TesteUltimoAviso => txtStatus?.Text ?? "";

    internal void TesteApagarArquivoAberto()
    {
        editor.Atual.Arquivos?.Remove(arquivoAberto);
        if (editor.Atual.Arquivos?.Count == 0) editor.Atual.Arquivos = null;
        AbrirArquivoDoProjeto(null);
    }
    internal string TesteCabecalho => txtPlacaDoCodigo.Text;
    internal string TesteRodape => txtOutrasPlacas.Text;
    internal string TesteCodigoPrincipal() => CodigoPrincipal();

    /// <summary>Escreve no campo como se fosse a pessoa digitando.</summary>
    internal void TesteDigitar(string texto) => txtCodigo.Text = texto;

    /// <summary>Carrega um circuito pelo mesmo caminho de abrir arquivo.</summary>
    internal void TesteAbrir(Circuito c, string codigo)
    {
        RecomecarSessao("projeto aberto: (teste)");
        editor.Carregar(c);
        editor.RecarregarChips();
        AdotarCodigo(codigo);

        // E a rede que acerta a bancada com o circuito: sem isto nao
        // existe maquina nenhuma, e o teste do monitor olharia um painel
        // vazio sem saber por que.
        ReconstruirRede();

        // Como abrir um arquivo: o que acabou de entrar e o estado
        // "salvo", e nao uma alteracao por salvar -- e o comeco do
        // historico, de onde o Ctrl+Z nao passa.
        circuitoSalvo = AssinaturaAtual();
        RecomecarHistorico();
    }

    /// <summary>Seleciona uma peca pelo indice, como um clique nela.</summary>
    internal void TesteSelecionar(int indice) => editor.SelecionarPorIndice(indice, false);

    /// <summary>Escreve no monitor de uma placa, como o sketch dela faria.</summary>
    internal void TesteFalarNoSerial(string idDaPlaca, string texto)
        => bancada.De(idDaPlaca).EscreverSerial(texto);

    internal void TesteAtualizarSerial() => AtualizarSerial();

    internal void TesteDarPorSalvo() => circuitoSalvo = AssinaturaAtual();
    internal string TesteSerial => string.Join(Environment.NewLine, LinhasDoSerial(0, painelSerial.Count));

    /// <summary>Para o teste: digitar no monitor e apertar Enviar.</summary>
    internal void TesteEnviarNoMonitor(string texto)
    {
        txtEntradaSerial.Text = texto;
        EnviarPeloSerial();
    }

    /// <summary>Para o teste: o que o botao do baud do monitor mostra.</summary>
    internal string TesteBaudDoMonitor => btBaudSerial.Content as string;

    /// <summary>O que a barra de status esta dizendo agora.</summary>
    internal string TesteStatus => txtStatus.Text;

    // ---- ganchos do teste de fluxo (rodar / parar / rodar) ----
    internal void TesteRodar() => Rodar();
    internal void TesteParar() => Parar();

    // ---- ganchos do depurador ----
    internal void TesteMarcarPausa(int linha) => AlternarPausa(linha);
    internal void TestePausarOuContinuar() => PausarOuContinuar();
    internal void TestePasso() => Passo();

    // ---- ganchos dos botoes da placa ----
    internal EditorCircuito TesteEditorCircuito => editor;

    /// <summary>Para o teste: o nome, o autor e a dica da barra de cima.</summary>
    internal (string Nome, string Autor, string Dica) TesteBarraDeCima => (txtNomeDoCircuito.Text, txtAutorDoCircuito.Text, txtDica.Text);
    internal void TesteNovoCircuito() => ComecarCircuitoNovo();

    /// <summary>Para o teste: o "Novo projeto" do menu, com as configuracoes.</summary>
    internal void TesteNovoProjetoDoMenu() => NovoCircuito();

    /// <summary>Para o teste: o editor de codigo.</summary>
    internal ICSharpCode.AvalonEdit.TextEditor TesteEditorDeCodigo => txtCodigo;

    /// <summary>Para o teste: salva num caminho, como o Salvar como.</summary>
    internal bool TesteSalvarEm(string caminho) => SalvarEm(caminho);

    /// <summary>Para o teste: o Ctrl+S.</summary>
    internal void TesteSalvar() => SalvarCircuito();

    /// <summary>Para o teste: a escala que a janela esta usando.</summary>
    internal double TesteEscala
        => (Content as FrameworkElement)?.LayoutTransform is ScaleTransform e ? e.ScaleX : 1;

    /// <summary>Para o teste: o ultimo tom mandado para a buzina.</summary>
    internal int TesteUltimoTom => ultimoHz;

    /// <summary>Para o teste: Ctrl+Z.</summary>
    internal void TesteDesfazer() => Desfazer();

    /// <summary>Para o teste: Ctrl+Y.</summary>
    internal void TesteRefazer() => Refazer();

    /// <summary>Para o teste: põe uma peça no meio da tela, como o Enter da paleta.</summary>
    internal void TesteAdicionarPeca(string tipo) => editor.Adicionar(tipo);
    internal Maquina TesteMaquinaDa(string idDaPlaca) => bancada.De(idDaPlaca);
    internal Bancada TesteBancada => bancada;
    internal Rede TesteRedeSimulada => redeSimulada;
    internal Netlist TesteRede => rede ??= new Netlist(editor.Atual);
    // ---- ganchos do painel de rede ----

    /// <summary>
    /// Para o teste: liga ou desliga a internet como o menu faz. So mexer na
    /// rede nao basta -- o Rodar volta a ler o menu.
    /// </summary>
    internal bool TesteUsarInternet
    {
        set { miInternet.IsChecked = value; redeSimulada.UsarInternet = value; }
    }
    internal void TesteMostrarAba(string nome) => MostrarAba(nome);
    internal void TesteOlharPara(double x, double y, double zoom)
        => editor.OlharPara(new Point(x, y), zoom);
    internal string TesteAbaVisivel => abaDoPainel;
    internal string TesteTextoDaRede => txtRede.Text;
    internal string TesteTextoDoBluetooth => txtBluetooth.Text;
    internal int TesteLinksDeRede => linksDeRede.Children.Count;
    internal void TesteAtualizarPainelDeRede() => AtualizarPainelDeRede();

    internal void TesteDigitarNoSerial(string texto)
    {
        txtEntradaSerial.Text = texto;
        EnviarPeloSerial();
    }

    internal void TesteConectarBluetooth(string nome)
    {
        dispositivoBtEscolhido = nome;
        ConectarOuSairDoBluetooth();
    }

    internal string TesteBluetoothEscolhido => txtBtEscolhido.Text;

    internal void TesteEnviarPeloBluetooth(string texto)
    {
        txtEntradaBt.Text = texto;
        EnviarPeloBluetooth();
    }

    internal bool TestePlacaRodando(string idDaPlaca)
        => execucoes.TryGetValue(idDaPlaca, out var t) && t.IsAlive;
    internal bool TesteThreadViva => execucoes.Values.Any(t => t is { IsAlive: true });

    internal string TesteDepurador()
        => $"pausada={linhaPausada} painel={painelVariaveis.Visibility} "
         + $"titulo=[{txtTituloVariaveis.Text}] vars=[{textoDasVariaveis}] "
         + $"pausas=[{string.Join(",", PausasDe(placaDoEditor))}] "
         + $"pausar={btPausar.IsEnabled}:{btPausar.Content} passo={btPasso.IsEnabled}";

    internal string TesteEstado()
    {
        int linhas;
        lock (maq.Trava) linhas = maq.Serial.Count;

        bool algumLigado = false;
        for (int i = 0; i < Maquina.NumPinos; i++)
            if (maq.Pwm[i] > 0) algumLigado = true;

        bool viva = execucoes.Values.Any(t => t is { IsAlive: true });
        int noPainel = painelSerial.Count;

        return $"serial={linhas} algumLedLigado={algumLigado} "
             + $"rodarHabilitado={btRodar.IsEnabled} pararHabilitado={btParar.IsEnabled} "
             + $"threadViva={viva} painelSerial={noPainel}linhas";
    }

    /// <summary>Varias de uma vez, para a captura mostrar a selecao multipla.</summary>
    internal void SelecionarVarias(IEnumerable<int> indices)
    {
        bool primeiro = true;
        foreach (int i in indices) { editor.SelecionarPorIndice(i, !primeiro); primeiro = false; }
    }

    /// <summary>O menu de opcoes, para a captura poder fotografa-lo.</summary>
    internal System.Windows.Controls.ContextMenu MenuDeOpcoes => menuOpcoes;

    /// <summary>Fios pelo indice: e a unica forma de a captura mostrar a tab do fio.</summary>
    internal void SelecionarFios(IEnumerable<int> indices)
    {
        editor.SelecionarFiosPorIndice(indices);
        AtualizarBarraObjeto();
        AtualizarDica();
    }

    void AbrirArquivo(object s, RoutedEventArgs e) => EnviarInoParaPlaca(s, e);

    /// <summary>
    /// Le um .ino do computador e poe na placa que esta no editor.
    ///
    /// "Enviar para a placa" e nao "abrir arquivo": com mais de uma placa
    /// no circuito o codigo e de UMA delas, e o nome tem que dizer em
    /// qual ele vai cair.
    /// </summary>
    void EnviarInoParaPlaca(object s = null, RoutedEventArgs e = null)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Sketch (*.ino;*.py)|*.ino;*.py"
                     + "|Arduino (*.ino)|*.ino|MicroPython (*.py)|*.py|Todos (*.*)|*.*"
        };
        if (dlg.ShowDialog() == true) CarregarSketch(dlg.FileName);
    }

    /// <summary>Guarda o sketch da placa do editor num .ino do computador.</summary>
    void BaixarInoDaPlaca(object s = null, RoutedEventArgs e = null)
    {
        var dona = PlacaDoEditor();
        string codigo = (dona is null ? codigoSemPlaca : dona.Sketch) ?? txtCodigo.Text;

        if (string.IsNullOrWhiteSpace(codigo))
        {
            Erro("esta placa ainda não tem código para baixar.");
            return;
        }

        string sugestao = string.IsNullOrWhiteSpace(dona?.Nome)
            ? (dona is null ? "sketch" : PerfilPlaca.De(dona.Tipo)?.Id ?? "sketch")
            : dona.Nome.Trim();

        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Filter = LinguagemDoEditor() == Linguagem.MicroPython
                ? "Sketch MicroPython (*.py)|*.py|Sketch do Arduino (*.ino)|*.ino"
                : "Sketch do Arduino (*.ino)|*.ino|Sketch MicroPython (*.py)|*.py",
            FileName = sugestao + Compilador.Extensao(LinguagemDoEditor()),
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            File.WriteAllText(dlg.FileName, codigo);
            Aviso($"código salvo em {System.IO.Path.GetFileName(dlg.FileName)}");
        }
        catch (Exception erro) { Erro(erro.Message); }
    }

    // ================= simulação =================
    /// <summary>
    /// Placa selecionada na Arduino IDE, quando o programa foi aberto
    /// por ela. Quem manda na simulacao e a placa do CIRCUITO -- isto
    /// existe so para avisar quando as duas discordam.
    /// </summary>
    internal string PlacaEscolhidaNaIde { get; set; }

    /// <summary>
    /// Avisa quando a placa da IDE nao e a que esta desenhada.
    ///
    /// Sem isso o aluno escolhe Arduino Uno na IDE, tem uma ESP32 no
    /// circuito, e o simulador roda como ESP32 sem dizer nada -- o tipo
    /// de divergencia que ninguem descobre olhando.
    /// </summary>
    /// <summary>
    /// Avisa quando ha mais de uma placa no circuito.
    ///
    /// O simulador roda UM programa numa Maquina so: a primeira placa
    /// define o perfil e as outras apenas despejam os pinos no mesmo
    /// espaco de GPIO. Com duas ESP32, o D14 de qualquer uma aciona a
    /// mesma saida. Nao da para descobrir isso olhando -- entao diz.
    /// </summary>
    void ConferirPlacaUnica()
    {
        var placas = editor.Atual.Pecas
            .Where(p => PerfilPlaca.EhPlaca(p.Tipo)).ToList();
        if (placas.Count < 2) return;

        string nomes = string.Join(" + ", placas
            .Select(p => PerfilPlaca.De(p.Tipo)?.Nome ?? p.Tipo).Distinct());
        string emFoco = PerfilPlaca.De(PlacaDoEditor()?.Tipo)?.Nome ?? "a primeira";

        // Aviso, e nao erro: rodar duas placas passou a funcionar. O que
        // ainda vale dizer e que o monitor mostra uma de cada vez.
        Aviso($"{placas.Count} placas ({nomes}) · cada uma roda o seu código · "
              + $"o monitor e os controles são da {emFoco}");
    }

    void ConferirPlacaDaIde()
    {
        if (string.IsNullOrEmpty(PlacaEscolhidaNaIde)) return;

        var doCircuito = editor.Atual.Pecas.FirstOrDefault(p => PerfilPlaca.EhPlaca(p.Tipo));
        if (doCircuito is null || doCircuito.Tipo == PlacaEscolhidaNaIde) return;

        string naIde = PerfilPlaca.De(PlacaEscolhidaNaIde)?.Nome ?? PlacaEscolhidaNaIde;
        string noDesenho = PerfilPlaca.De(doCircuito.Tipo)?.Nome ?? doCircuito.Tipo;
        Erro($"a Arduino IDE está em {naIde}, mas o circuito tem {noDesenho}. "
             + "Quem manda na simulação é o circuito.");
    }

    /// <summary>
    /// Pendura no barramento de cada placa as pecas de I2C do circuito.
    ///
    /// Pelo FIO, e nao pela simples presenca na tela: o aparelho so
    /// responde na placa em que o pino de dados dele chega. Modulo que
    /// ninguem ligou nao responde a scanner nenhum -- que e o que
    /// acontece na bancada, e e metade das horas perdidas com I2C.
    /// </summary>
    void LigarAparelhosDeI2c()
    {
        // Com circuito, so a peca da tela responde -- e so no barramento em
        // que o SDA e o SCL dela chegam (o Wire nos 21/22, o Wire1 nos que o
        // sketch escolher). O objeto declarado sem a peca ligada nao acha
        // ninguem, como na bancada.
        foreach (var (_, m) in bancada.Todas)
        {
            m.I2c.DesligarTodos();
            m.I2cPeloCircuito = true;
        }

        foreach (var peca in editor.Atual.Pecas)
        {
            var (endereco, nome, pinoDeDados) = Catalogo.AparelhoDeI2c(peca.Tipo);
            if (endereco < 0) continue;

            var (placa, sda) = rede.PinoDe(editor.Atual, peca.Id, pinoDeDados);
            if (placa is null) continue;
            var (placaDoClock, scl) = rede.PinoDe(editor.Atual, peca.Id, "SCL");
            if (placaDoClock != placa || scl < 0)
            {
                Aviso($"I2C: o SCL do {nome} nao esta ligado na placa -- sem o clock ele nao responde.");
                continue;
            }

            var maq = bancada.De(placa);
            // Dois no mesmo endereco e no mesmo fio e curto de dados: no
            // barramento de verdade os dois respondem juntos e a leitura sai
            // embaralhada. O DS1307 e o MPU6050 sao 0x68 os dois.
            // O OLED e o LCD com o modulo I2C entendem os bytes crus (o driver que nao e a biblioteca imitada).
            DispositivoI2c novo = peca.Tipo switch
            {
                "ssd1306-i2c" or "ssd1306-128x32" or "sh1106" => new OledI2c(maq, endereco, nome, 128, peca.Tipo == "ssd1306-128x32" ? 32 : 64, peca.Tipo == "sh1106") { PinoSda = sda, PinoScl = scl },
                "lcd1602-i2c" => new LcdPcf8574(maq, endereco, nome, 16, 2) { PinoSda = sda, PinoScl = scl },
                "lcd2004-i2c" => new LcdPcf8574(maq, endereco, nome, 20, 4) { PinoSda = sda, PinoScl = scl },
                _ => new DispositivoI2c(endereco, nome) { PinoSda = sda, PinoScl = scl },
            };
            var jaEstava = maq.I2c.Ligar(novo);

            if (jaEstava != novo)
                Aviso($"I2C: {nome} e {jaEstava.Nome} disputam o endereco "
                      + $"0x{endereco:X2} no mesmo fio. So um responde.");
        }
    }

    void ReconstruirRede()
    {
        rede = new Netlist(editor.Atual);

        // Cada placa tem a sua maquina, com o seu perfil: faixa do ADC,
        // pinos validos e LED_BUILTIN saem do tipo de cada uma.
        //
        // ANTES dos avisos de erro, de proposito. Estava no fim, e o
        // "placa: ESP32 · ADC 0-4095" apagava da barra o erro que acabava
        // de ser escrito -- justamente ao abrir um arquivo, que e quando
        // a bancada muda e quando o erro mais importa. O de fio em pino
        // inexistente sofria disso desde antes da bancada existir.
        //
        // A regra da barra de status e simples: informacao primeiro,
        // erro por ultimo, para o erro ser o que fica.
        bancada.IdEmFoco = PlacaDoEditor()?.Id;
        if (bancada.Sincronizar(editor.Atual))
        {
            var perfil = PerfilPlaca.De(PlacaDoEditor()?.Tipo);
            if (perfil is not null)
                Aviso($"placa: {perfil.Nome} · ADC 0–{perfil.MaxAdc} · "
                      + $"pinos 0–{perfil.NumPinos - 1}");
        }

        LigarAparelhosDeI2c();
        ConferirPlacaUnica();
        ConferirPlacaDaIde();

        // Peca ligada em duas placas obedece a primeira, e isso e
        // arbitrario. Dizer qual peca e quais placas e o minimo: o
        // sintoma (o LED responde a placa errada) nao aponta para a
        // causa sozinho.
        var entreDuas = editor.PecasEntreDuasPlacas(rede).ToList();
        if (entreDuas.Count > 0)
        {
            var (idPeca, placas) = entreDuas[0];
            var peca = editor.Atual.Achar(idPeca);
            string nome = Catalogo.Obter(peca?.Tipo)?.Nome ?? peca?.Tipo ?? "peça";
            string nomes = string.Join(" e ", placas.Select(NomeCurtoDaPlaca));

            // O aviso diz COMO resolver, e nao so o que esta errado.
            Erro($"{nome} está ligado em duas placas ({nomes}) e vai obedecer à "
                 + "primeira. Selecione a peça e escolha em \"manda\" qual placa vale"
                 + (entreDuas.Count > 1 ? $" — e mais {entreDuas.Count - 1} peça(s) assim." : "."));
        }

        // Fio para pino que nao existe some do desenho calado. Melhor
        // dizer do que deixar o aluno procurando a ligacao que "sumiu".
        var quebrados = editor.Atual.FiosQuebrados().Distinct().ToList();
        if (quebrados.Count > 0)
            Erro($"fio ligado em pino inexistente: {string.Join(", ", quebrados.Take(3))}"
                 + (quebrados.Count > 3 ? $" e mais {quebrados.Count - 3}" : ""));

        editor.MarcarPinosLigados(rede);
    }

    /// <summary>Uma compilacao em andamento: o Rodar de novo (o F5) espera ela acabar.</summary>
    bool compilando;

    async void Rodar(object s = null, RoutedEventArgs e = null)
    {
        if (compilando) return;
        PararExecucao();
        GuardarCodigoDoEditor();

        // Antes de tudo: o sketch pede biblioteca que nao existe aqui?
        // Descobrir isso DEPOIS, como erro na linha 40, foi a primeira
        // reclamacao da lista do professor.
        if (!AvisarBibliotecasQueFaltam()) return;

        // Placa nova a cada execucao: quem queimou volta inteiro e, se o
        // erro continuar, queima de novo em segundos.
        editor.ApagarQueimaduras();
        avisouDoRitmo = false;
        celularAbertoNestaRodada = false;
        avisosDeLigacao.Clear();
        queimadosNaRodada.Clear();
        servidoresAbertos.Clear();

        // O radio do computador pode ter mudado desde a ultima execucao:
        // o aluno ligou o WiFi, saiu do aviao, trocou de rede.
        RadioDoComputador.Esquecer();

        // Modo simulacao: daqui para a frente o teclado e do CIRCUITO.
        editor.EmSimulacao = true;

        bancada.Sincronizar(editor.Atual);
        bancada.Reiniciar();

        editor.Chips.Comecar(editor.Atual);
        editor.Hc05.Comecar(editor.Atual, rede, bancada, redeSimulada);
        editor.AplicarEntradas(bancada, rede);
        redeSimulada.DefinirPontosDeAcesso(editor.PontosDeAcessoDoCircuito());

        foreach (var (idDoChip, erro) in editor.Chips.Erros)
        {
            _ = idDoChip;
            Erro(erro);
        }

        // O cartao de cada placa comeca a rodada com o que esta na peca --
        // e so com isso: o que ficou na memoria da rodada anterior sai. O
        // pendrive tambem.
        foreach (var (_, m) in bancada.Todas)
        {
            m.Cartao.Limpar();
            m.Cartao.PastaDoComputador = null;
            m.Pendrive.Limpar();
            m.Pendrive.PastaDoComputador = null;
            m.PendriveConectado = null;
        }
        foreach (var (peca, maquina) in editor.CartoesDoCircuito(bancada, rede))
            if (maquina is not null) CarregarArmazenamento(maquina.Cartao, peca, "cartão");

        // O pendrive: encaixado (o clique no desenho tira e poe, com a
        // simulacao rodando) e ligado no USB OTG da placa.
        foreach (var (peca, maquina, problema) in editor.PendrivesDoCircuito(bancada, rede))
        {
            if (maquina is null) continue;
            CarregarArmazenamento(maquina.Pendrive, peca, "pendrive");
            if (problema is not null)
            {
                console.Aviso($"pendrive \"{peca.Nome ?? peca.Id}\": {problema}. A placa não vai ver o pendrive.");
                continue;
            }
            var pendrive = peca;
            maquina.PendriveConectado = () => pendrive.Giro < 0.5;
        }
        cartoesDaRodada = true;

        // O codigo C (modo nativo) conversa com o display e com o cartao
        // pelo barramento SPI: cada placa fica sabendo o que esta ligado nela
        // e em quais pinos. E uma foto do circuito na hora do Rodar.
        foreach (var (id, m) in bancada.Todas)
        {
            var aparelhos = editor.AparelhosSpiDe(bancada, rede, m);
            m.AparelhosSpi = () => aparelhos;

            // Os fios entre placas (a UART de uma no RX da outra): a mesma foto do circuito.
            var circuitoDaRodada = editor.Atual;
            var redeDaRodada = rede;
            string idDaPlaca = id;
            // O HC-05 tambem: o TX da placa no RXD dele chega na UART do modulo.
            var hc05 = editor.Hc05;
            m.LigadosAoPino = gpio => [.. redeDaRodada.PlacasNoMesmoNo(circuitoDaRodada, idDaPlaca, gpio)
                                          .Select(x => (bancada.De(x.Placa), x.Gpio))
                                          .Where(x => x.Item1 is not null)
                                          .Concat(hc05.NoFioDaPlaca(idDaPlaca, gpio))];
        }

        // Todas as placas na MESMA rede, com o nome curto que aparece no
        // painel: "[ESP32] conectou em ..." so faz sentido assim.
        redeSimulada.UsarInternet = miInternet.IsChecked;

        // O celular de verdade, pelo radio Bluetooth do computador. Nos
        // testes fica desligado: o autoteste nao mexe no radio da maquina.
        redeSimulada.UsarBluetoothDoPc = !Autoteste.Rodando && !Passeio.Ativo
                                         && Preferencias.Ligado(Preferencias.BluetoothDoComputador, true);
        foreach (var (id, m) in bancada.Todas)
        {
            m.TempoReal = true;
            m.Rede = redeSimulada;
            m.Nome = NomeCurtoDaPlaca(id).Trim('[', ']');
        }

        // Broker MQTT em localhost: um app de MQTT do proprio computador
        // conecta nele e conversa com as placas da bancada.
        redeSimulada.AbrirBrokerLocal(1883);
        ReconstruirRede();

        var placas = editor.Atual.Placas().ToList();
        if (placas.Count == 0) { Erro("nao ha placa no circuito para rodar."); return; }

        console.Info(placas.Count == 1
            ? $"compilando {NomeDaPlaca(placas[0])}…"
            : $"compilando {placas.Count} placas…");

        // Compila TODAS antes de comecar qualquer uma: metade rodando e
        // metade com erro de sintaxe seria pior de entender do que
        // nenhuma rodando.
        // O projeto inteiro: o "#include "config.h"" traz a aba, o
        // "#include <Botao.h>" traz a biblioteca instalada, e o
        // "#ifdef ESP32" ve as macros do core desta placa.
        var projetos = placas.Select(ProjetoDaPlaca).ToList();
        var compilados = new Programa[placas.Count];
        var falhas = new Exception[placas.Count];
        void CompilarTodas()
        {
            for (int i = 0; i < projetos.Count; i++)
            {
                try { compilados[i] = Compilador.CompilarProjeto(projetos[i]); }
                catch (Exception ex)
                {
                    falhas[i] = ex;
                    return;
                }
            }
        }
        // O compilador de C++ na primeira vez leva minutos (o core, o mbedTLS, a biblioteca grande):
        // a janela continua respondendo enquanto isso. Os testes e o passeio esperam ali mesmo.
        if (Autoteste.Rodando || Passeio.Ativo) CompilarTodas();
        else
        {
            var circuitoDaCompilacao = editor.Atual;
            compilando = true;
            btRodar.IsEnabled = false;
            try { await Task.Run(CompilarTodas); }
            finally
            {
                compilando = false;
                btRodar.IsEnabled = true;
            }
            // Trocou de projeto no meio: o que compilou ja nao e o da tela.
            if (!ReferenceEquals(circuitoDaCompilacao, editor.Atual)) return;
        }

        for (int i = 0; i < placas.Count; i++)
        {
            var placa = placas[i];
            try
            {
                if (falhas[i] is { } falha) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(falha).Throw();
                if (compilados[i] is not { } prog) return;
                foreach (string aviso in prog.Avisos) console.Aviso(aviso);
                programas[placa.Id] = prog;

                // Os .c vao para o TinyCC agora (o DOOM leva um ou dois
                // segundos): o aviso aparece antes da espera, e nao depois.
                int arquivosC = prog.Nativos.Count(f => f.Nome.EndsWith(".c", StringComparison.OrdinalIgnoreCase));
                if (arquivosC > 0)
                {
                    console.Info($"{NomeDaPlaca(placa)}: compilando {arquivosC} arquivo(s) em C (modo nativo)…");
                    Dispatcher.Invoke(() => { }, DispatcherPriority.Background);
                }
                interpretadores[placa.Id] = MontarInterpretador(placa.Id, prog);

                console.Info($"{NomeDaPlaca(placa)}: compilou em "
                             + Compilador.Nome(Compilador.Detectar(placa.Sketch ?? "")) + ".");

                // O sketch cabe nesta placa? E a conta que a IDE
                // imprime depois de compilar e que o simulador nunca
                // fazia: um vetor grande demais num Uno passava aqui e
                // so dava problema na placa de verdade.
                if (!Couber(placa, prog)) return;
            }
            catch (Exception ex)
            {
                interpretadores.Clear();
                depuradores.Clear();
                programas.Clear();
                Erro(placas.Count == 1
                    ? ex.Message
                    : $"{PerfilPlaca.De(placa.Tipo)?.Nome ?? placa.Tipo}: {ex.Message}");
                return;
            }
        }

        foreach (var (_, m) in bancada.Todas) m.IniciarRelogio();

        // Cada placa recomecou com a serial vazia, entao a contagem do que
        // ja foi mostrado recomeca sempre. Limpar o monitor, so se a pessoa
        // quiser -- quem compara uma execucao com a outra prefere que nao.
        serialPorPlaca.Clear();
        avisosPorPlaca.Clear();
        avisouBaud.Clear();
        if (Preferencias.Ligado(Preferencias.LimparSerialAoRodar, true))
        {
            painelSerial.Clear();
            RedesenharSerial();
        }
        else if (painelSerial.Count > 0)
        {
            painelSerial.Add("——— rodando de novo ———");
            RedesenharSerial();
        }

        // Execucao nova, monitor limpo -- e o que a IDE faz quando a
        // placa reinicia.
        MonitorIde.Comecar();
        console.Info($"rodando {placas.Count} placa(s).");
        Aviso("rodando...");

        // O pisca da lista de componentes: verde no que esta ligado, e
        // ele some para o branco. E a resposta visual a "o que
        // realmente conectou", na hora em que a pergunta aparece.
        PiscarLigacoes();

        btRodar.IsEnabled = false;
        btParar.IsEnabled = true;

        int minha = ++geracao;
        semEnergia.Clear();
        foreach (var placa in placas)
        {
            string id = placa.Id;

            // Sem o cabo USB, so liga a placa que a bateria alimenta. A outra
            // fica apagada, e liga sozinha quando a bateria chegar nela.
            if (!editor.PlacaComEnergia(id))
            {
                semEnergia.Add(id);
                bancada.De(id).Desligar();
                console.Aviso($"{NomeDaPlaca(placa)} {editor.SituacaoDaEnergia(id)}. Ela liga quando a energia chegar "
                              + "(ou ligue o cabo USB na barra da placa).");
                continue;
            }

            var thread = new Thread(() => Executar(minha, id), PilhaDoSketch) { IsBackground = true, Priority = ThreadPriority.BelowNormal };
            thread.IsBackground = true;
            execucoes[id] = thread;
            thread.Start();
        }
        if (semEnergia.Count > 0)
        {
            if (rede is not null) editor.RefletirEstado(bancada, rede);
            Aviso(semEnergia.Count == placas.Count
                ? (placas.Count == 1 ? "a placa está sem energia: " + editor.SituacaoDaEnergia(placas[0].Id).Replace("sem energia: ", "") + "."
                                     : "nenhuma placa tem energia: a bateria não chega nelas, e elas estão sem o cabo USB.")
                : $"rodando — {semEnergia.Count} placa(s) sem energia (veja o monitor).");
        }
    }

    /// <summary>
    /// A cada quadro, com a simulacao rodando: a placa sem o cabo que perdeu a
    /// energia (o fio da bateria saiu, a chave abriu, a bateria caiu demais)
    /// apaga; a que ganhou liga, do boot, como a placa de verdade.
    /// </summary>
    void ConferirEnergiaDasPlacas()
    {
        if (!btParar.IsEnabled || programas.Count == 0) return;
        foreach (string id in programas.Keys.ToList())
        {
            if (editor.Atual.Achar(id) is null) continue;
            bool tem = editor.PlacaComEnergia(id);
            if (semEnergia.Contains(id)) { if (tem) DarEnergia(id); }
            else if (!tem) CortarEnergia(id);
        }
    }

    /// <summary>A energia da placa acabou: ela para e apaga, como com o RESET segurado.</summary>
    void CortarEnergia(string idDaPlaca)
    {
        semEnergia.Add(idDaPlaca);
        var maquina = bancada.De(idDaPlaca);
        maquina.Parar = true;
        if (execucoes.TryGetValue(idDaPlaca, out var thread) && thread is { IsAlive: true })
            thread.Join(1500);
        if (placaPausada == idDaPlaca) SairDaPausa();

        maquina.Desligar();
        if (rede is not null) editor.RefletirEstado(bancada, rede);
        string nome = NomeCurtoDaPlaca(idDaPlaca).Trim('[', ']');
        string porque = editor.SituacaoDaEnergia(idDaPlaca);
        Aviso($"{nome} apagou — {porque}.");
        console.Aviso($"{nome} apagou: {porque}.");
    }

    /// <summary>A energia chegou: a placa liga do zero (o boot e o setup()), como ao ligar a bateria na de verdade.</summary>
    void DarEnergia(string idDaPlaca)
    {
        semEnergia.Remove(idDaPlaca);
        // Com o RESET segurado ela so recomeca quando o botao for solto.
        if (emReset.Contains(idDaPlaca) || !btParar.IsEnabled || !programas.TryGetValue(idDaPlaca, out var prog)) return;

        var maquina = bancada.De(idDaPlaca);
        maquina.Reiniciar();
        maquina.TempoReal = true;
        serialPorPlaca[idDaPlaca] = 0;
        avisosPorPlaca[idDaPlaca] = 0;
        if (rede is not null) editor.AplicarEntradas(bancada, rede);

        try { interpretadores[idDaPlaca] = MontarInterpretador(idDaPlaca, prog); }
        catch (Exception ex) { Erro(ex.Message); return; }

        int minha = geracao;
        int boot = maquina.Perfil?.MsDoBoot ?? 0;
        var novaThread = new Thread(() =>
        {
            Thread.Sleep(boot);
            if (minha != geracao || maquina.Parar) return;
            maquina.IniciarRelogio();
            Executar(minha, idDaPlaca);
        }, PilhaDoSketch) { IsBackground = true, Priority = ThreadPriority.BelowNormal };
        execucoes[idDaPlaca] = novaThread;
        novaThread.Start();
        string nome = NomeCurtoDaPlaca(idDaPlaca).Trim('[', ']');
        Aviso($"{nome} ligou — {editor.SituacaoDaEnergia(idDaPlaca)}.");
        console.Info($"{nome} ligou: {editor.SituacaoDaEnergia(idDaPlaca)}.");
    }

    /// <summary>
    /// Oferece importar o circuito que esta guardado na descricao.
    ///
    /// Varios .sktch da turma tem um diagram.json do Wokwi INTEIRO no
    /// campo de descricao -- um deles avisa isso no proprio nome do
    /// arquivo, "full diagram on description". O circuito de verdade
    /// estava ali o tempo todo, como texto, e o simulador nunca tinha
    /// olhado: a pessoa abria o projeto, via uma placa sozinha na tela e
    /// concluia que o arquivo tinha se perdido.
    ///
    /// Nao importa sozinho. Trocar o circuito de alguem sem perguntar
    /// seria pior do que o problema.
    /// </summary>
    void OferecerDiagramaDaDescricao()
    {
        if (Autoteste.Rodando) return;

        string descricao = txtDescricao.Text;
        if (!ImportarWokwi.PareceDiagrama(descricao)) return;

        // Circuito que ja tem mais do que a placa sozinha nao precisa de
        // resgate: quem chegou aqui perdeu o desenho, e nao o tem em
        // duplicata.
        if (editor.Atual.Pecas.Count > 2) return;

        ImportarWokwi.Resultado lido;
        try { lido = ImportarWokwi.Ler(descricao); }
        catch (Exception) { return; }

        if (lido.Circuito.Pecas.Count <= editor.Atual.Pecas.Count) return;

        var resposta = MessageBox.Show(this,
            "A descrição deste projeto tem um diagrama do Wokwi guardado dentro, com "
            + $"{lido.Circuito.Pecas.Count} peças e {lido.Circuito.Fios.Count} fios."
            + Environment.NewLine + Environment.NewLine
            + $"O circuito aberto tem só {editor.Atual.Pecas.Count}."
            + Environment.NewLine + Environment.NewLine
            + "Montar o circuito da descrição?",
            "Achei um circuito na descrição",
            MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (resposta != MessageBoxResult.Yes) return;

        // O código fica: o diagrama traz peças e fios, e o programa é do
        // .sktch. Trocar os dois apagaria o sketch de quem só queria o
        // desenho de volta.
        string codigo = PlacaDoEditor()?.Sketch ?? txtCodigo.Text;

        editor.Carregar(lido.Circuito);
        bancada.Sincronizar(lido.Circuito);
        ReconstruirRede();

        var placaNova = lido.Circuito.Placas().FirstOrDefault();
        if (placaNova is not null && placaNova.Codigo is null) placaNova.Sketch = codigo;

        placaDoEditor = placaNova?.Id;
        MostrarCodigoDaPlaca();
        AtualizarAbasDeArquivo();
        editor.Ajustar();
        Empilhar();

        string aviso = $"circuito montado da descrição: {lido.Circuito.Pecas.Count} peças, "
                       + $"{lido.Circuito.Fios.Count} fios";
        if (lido.Ignorados.Count > 0)
            aviso += $" · {lido.Ignorados.Count} peça(s) sem equivalente aqui: "
                     + string.Join(", ", lido.Ignorados.Distinct().Take(4));

        Aviso(aviso);
    }

    /// <summary>
    /// O que compila junto para esta placa: o sketch dela, as abas do
    /// projeto e as bibliotecas instaladas no acervo.
    /// </summary>
    Projeto ProjetoDaPlaca(PecaNoCircuito placa) => new()
    {
        Sketch = placa.Sketch ?? "",
        Arquivos = editor.Atual.Arquivos,
        Placa = placa.Tipo,
        Bibliotecas = Acervo.ParaCompilar(),
        DoSimulador = Acervo.EhDoSimulador,
    };

    /// <summary>
    /// A pilha da thread que roda o sketch. A padrao do .NET (1 MB) nao
    /// da conta de uma recursao funda interpretada; com esta, o limite de
    /// chamadas do interpretador chega antes e vira erro com linha, e nao
    /// o programa inteiro fechando.
    /// </summary>
    const int PilhaDoSketch = 64 * 1024 * 1024;

    /// <summary>
    /// Bibliotecas que faltam, ja avisadas.
    ///
    /// Guardado para NAO repetir a janela a cada F5: quem escolheu
    /// "rodar assim mesmo" nao quer ser perguntado de novo no segundo
    /// seguinte. Um sketch editado zera o combinado.
    /// </summary>
    readonly HashSet<string> faltasPerdoadas = [];

    /// <summary>
    /// Mostra o que falta e devolve se a simulacao pode comecar.
    ///
    /// Vale para as duas linguagens: o "#include" do C e o "import" do
    /// Python caem na mesma janela, porque a duvida do aluno e a mesma.
    /// </summary>
    bool AvisarBibliotecasQueFaltam()
    {
        if (Autoteste.Rodando) return true;

        foreach (var placa in editor.Atual.Placas())
        {
            string sketch = placa.Sketch ?? "";

            // O que o projeto resolve sozinho nao e biblioteca que falta.
            var falta = Acervo.OQueFalta(
                Montagem.Montar(sketch, editor.Atual.Arquivos), editor.Atual.Arquivos);
            falta.RemoveAll(f => faltasPerdoadas.Contains(f.Cabecalho));
            if (falta.Count == 0) continue;

            var janela = new JanelaFaltando(this, falta, Compilador.Detectar(sketch))
            {
                CriarArquivo = nome =>
                {
                    editor.Atual.Arquivos ??= new Dictionary<string, string>(
                        StringComparer.OrdinalIgnoreCase);

                    editor.Atual.Arquivos[nome] =
                        "// " + nome + Environment.NewLine
                        + "// Este arquivo faltava no projeto: o sketch o inclui com "
                        + "aspas, entao ele mora aqui do lado." + Environment.NewLine;
                },
            };
            janela.ShowDialog();

            if (janela.Instalou) AtualizarAbasDeArquivo();

            if (!janela.Prosseguir)
            {
                Erro("faltam bibliotecas: " + string.Join(", ", falta.Select(f => f.Cabecalho)));
                return false;
            }

            foreach (var f in falta) faltasPerdoadas.Add(f.Cabecalho);
        }

        return true;
    }

    /// <summary>
    /// Interpretador novo para uma placa, com o depurador ja ligado. O
    /// Rodar e o RESET montam a placa por aqui, para os dois nao divergirem.
    /// </summary>
    Interpretador MontarInterpretador(string idDaPlaca, Programa prog, ReinicioDaPlaca origem = null)
    {
        var maquina = bancada.De(idDaPlaca);

        // A origem entra ANTES do Preparar: e ao declarar as globais que a
        // memoria RTC volta com o que tinha antes do sono.
        var interpretador = new Interpretador(prog, maquina) { Origem = origem };
        interpretador.Preparar();

        // Sempre ligado, e nao so quando ha ponto marcado: o botao
        // de pausar precisa funcionar em qualquer sketch rodando.
        var depurador = new Depurador(maquina);
        depurador.DefinirPausas(PausasDe(idDaPlaca));
        depurador.Pausou += retrato =>
            Dispatcher.BeginInvoke(() => AoPausar(depurador, idDaPlaca, retrato));
        depurador.Retomou += () =>
            Dispatcher.BeginInvoke(() => AoRetomar(depurador, idDaPlaca));
        depurador.Retratou += retrato =>
            Dispatcher.BeginInvoke(() => AoRetratar(depurador, idDaPlaca, retrato));
        interpretador.Depurador = depurador;
        depuradores[idDaPlaca] = depurador;
        return interpretador;
    }

    /// <summary>
    /// RESET (ou EN) apertado: a placa para e apaga enquanto o botao estiver
    /// embaixo. As outras placas da bancada seguem rodando -- cada uma tem o
    /// seu botao, como na mesa do laboratorio.
    /// </summary>
    void SegurarReset(string idDaPlaca, string motivo = null)
    {
        if (!btParar.IsEnabled)
        {
            Aviso("a simulação está parada: o RESET só faz efeito com a placa rodando (F5).");
            return;
        }
        if (!programas.ContainsKey(idDaPlaca)) return;
        if (semEnergia.Contains(idDaPlaca))
        {
            Aviso(NomeCurtoDaPlaca(idDaPlaca).Trim('[', ']') + " está sem energia: o RESET não faz nada nela.");
            return;
        }

        emReset.Add(idDaPlaca);
        var maquina = bancada.De(idDaPlaca);
        maquina.Parar = true;
        if (execucoes.TryGetValue(idDaPlaca, out var thread) && thread is { IsAlive: true })
            thread.Join(1500);
        if (placaPausada == idDaPlaca) SairDaPausa();

        maquina.Desligar();
        if (rede is not null) editor.RefletirEstado(bancada, rede);
        Aviso(NomeCurtoDaPlaca(idDaPlaca).Trim('[', ']') + ": " + (motivo ?? "RESET segurado, solte para reiniciar."));
    }

    /// <summary>RESET solto: a placa recomeca do setup(), com o monitor seguindo.</summary>
    void SoltarReset(string idDaPlaca)
    {
        if (!emReset.Remove(idDaPlaca)) return;
        if (semEnergia.Contains(idDaPlaca)) return;   // apagada: liga quando a energia chegar
        if (!btParar.IsEnabled || !programas.TryGetValue(idDaPlaca, out var prog)) return;

        var maquina = bancada.De(idDaPlaca);
        maquina.Reiniciar();
        maquina.TempoReal = true;

        // Reiniciar esvazia a lista de linhas da placa; o painel continua
        // com o que ja mostrou, como o monitor da IDE depois de um reset.
        serialPorPlaca[idDaPlaca] = 0;
        avisosPorPlaca[idDaPlaca] = 0;
        if (rede is not null) editor.AplicarEntradas(bancada, rede);

        try { interpretadores[idDaPlaca] = MontarInterpretador(idDaPlaca, prog); }
        catch (Exception ex) { Erro(ex.Message); return; }

        int minha = geracao;
        var novaThread = new Thread(() => Executar(minha, idDaPlaca), PilhaDoSketch) { IsBackground = true, Priority = ThreadPriority.BelowNormal };
        execucoes[idDaPlaca] = novaThread;
        novaThread.Start();
        Aviso(NomeCurtoDaPlaca(idDaPlaca).Trim('[', ']') + " reiniciou.");
    }

    /// <summary>
    /// O sketch pediu para a placa comecar de novo: ESP.restart(), ou o
    /// sono profundo acabou. E o que o RESET solto faz -- a placa volta do
    /// setup() --, com a diferenca de que o interpretador novo sabe por que
    /// recomecou e traz a memoria RTC junto.
    /// </summary>
    void RecomecarPelaPlaca(int minha, string idDaPlaca, ReinicioDaPlaca motivo)
    {
        if (minha != geracao || !btParar.IsEnabled || emReset.Contains(idDaPlaca) || semEnergia.Contains(idDaPlaca)) return;
        if (!programas.TryGetValue(idDaPlaca, out var prog)) return;

        var maquina = bancada.De(idDaPlaca);
        maquina.Reiniciar();
        maquina.TempoReal = true;
        serialPorPlaca[idDaPlaca] = 0;
        avisosPorPlaca[idDaPlaca] = 0;

        // O chip reiniciou; o circuito, nao. A chave continua ligada e o
        // botao continua apertado -- sem isto, o setup() novo leria tudo
        // solto ate o proximo quadro da tela, e a placa que acabou de
        // acordar pela chave voltava a dormir.
        if (rede is not null) editor.AplicarEntradas(bancada, rede);

        try { interpretadores[idDaPlaca] = MontarInterpretador(idDaPlaca, prog, motivo); }
        catch (Exception ex) { Erro(ex.Message); return; }

        // O boot: do reset ao setup() passa o tempo que a placa leva de
        // verdade -- ~300 ms na ESP32 (a ROM e a partida do sistema). Uma placa
        // presa num ciclo de reinicios anda nesse ritmo, e nao aos milhares
        // por segundo. A espera e na thread nova (a rodada ja esta na fila,
        // como o "acabou" da antiga espera), e o relogio do chip comeca depois.
        int boot = maquina.Perfil?.MsDoBoot ?? 0;
        var novaThread = new Thread(() =>
        {
            Thread.Sleep(boot);
            if (minha != geracao || maquina.Parar) return;
            maquina.IniciarRelogio();
            Executar(minha, idDaPlaca);
        }, PilhaDoSketch) { IsBackground = true, Priority = ThreadPriority.BelowNormal };
        execucoes[idDaPlaca] = novaThread;
        novaThread.Start();
        Aviso(NomeCurtoDaPlaca(idDaPlaca).Trim('[', ']') + " " + motivo.Resumo + ".");
    }

    void Executar(int minha, string idDaPlaca)
    {
        var maquina = bancada.De(idDaPlaca);
        if (!interpretadores.TryGetValue(idDaPlaca, out var interp)) return;

        try
        {
            interp.Setup();
            while (!maquina.Parar && minha == geracao && interp.TemLoop)
            {
                long antes = maquina.Ms;
                interp.Loop();

                // Ceder CPU: em tempo real o cronômetro sempre anda, então o
                // que importa é se a volta custou menos de 1 ms. Sem isto um
                // sketch com millis() gira a 100%.
                //
                // O Assembly fica de fora: ele já cede a CPU sozinho,
                // esperando o relógio alcançar o chip. Dormir aqui também
                // somava 15 ms a cada milissegundo de chip.
                if (!interp.TemRitmoProprio && maquina.Ms - antes < 1) Thread.Sleep(1);
            }

            // O codigo C (modo nativo) entrou em panico entre uma volta e
            // outra: a placa reinicia, como a de verdade depois do Guru
            // Meditation.
            if (maquina.ReinicioPedido is { } pedido && minha == geracao) throw pedido;
        }
        // Antes da parada comum: o reinicio tambem e uma. A rodada nova
        // sai da fila da janela ANTES do "acabou" do finally, entao os
        // botoes nao piscam para o Rodar no meio.
        catch (ReinicioDaPlaca motivo)
        {
            Dispatcher.BeginInvoke(() => RecomecarPelaPlaca(minha, idDaPlaca, motivo));
        }
        catch (ExecucaoInterrompida) { }
        catch (Exception ex)
        {
            // BeginInvoke, nao Invoke: a interface pode estar esperando
            // esta thread terminar no Join, e Invoke travaria as duas.
            //
            // Com mais de uma placa, o erro diz de qual: "erro na linha
            // 12" sem dizer onde manda o aluno procurar no sketch errado.
            Dispatcher.BeginInvoke(() =>
            {
                if (minha != geracao) return;
                var peca = editor.Atual.Achar(idDaPlaca);
                string nome = PerfilPlaca.De(peca?.Tipo)?.Nome;
                Erro(bancada.Quantas > 1 && nome is not null
                    ? $"{nome}: {ex.Message}" : ex.Message);
            });
        }
        finally
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (minha != geracao) return;   // ja tem outra rodada

                // Placa segurada no RESET parou de proposito: a rodada
                // continua, e ela volta sozinha quando o botao for solto. A
                // placa sem energia tambem: liga quando a bateria chegar.
                if (emReset.Count > 0 || semEnergia.Count > 0) return;

                // Os botoes so voltam quando a ULTIMA placa parar.
                if (execucoes.Values.Any(t => t is { IsAlive: true }
                                              && t != Thread.CurrentThread)) return;

                btRodar.IsEnabled = true;
                btParar.IsEnabled = false;
            });
        }
    }


    void Parar(object s = null, RoutedEventArgs e = null)
    {
        PararExecucao();
        buzina.Parar();
        falante.Tocar(null);
        ultimoHz = -1;

        // Sem energia: LEDs apagam, buzzer cala, LCD apaga.
        foreach (var (_, m) in bancada.Todas) m.Desligar();
        if (rede is not null) editor.RefletirEstado(maq, rede);

        Aviso("parado.");
    }

    void PararExecucao()
    {
        editor.Chips.Parar();
        editor.Hc05.Parar();
        GuardarCartoes();
        editor.EmSimulacao = false;
        geracao++;              // invalida a rodada atual
        bancada.Parar();
        foreach (var thread in execucoes.Values)
            if (thread is { IsAlive: true }) thread.Join(1500);
        execucoes.Clear();
        interpretadores.Clear();
        depuradores.Clear();
        programas.Clear();
        emReset.Clear();
        semEnergia.Clear();

        // Servidores, conexoes e broker fecham junto com a simulacao.
        redeSimulada.Encerrar();
        SairDaPausa();

        btRodar.IsEnabled = true;
        btParar.IsEnabled = false;
    }

    // ================= depurador =================
    //
    // O motor (Depurador.cs) para a thread do sketch; aqui e so o que se
    // ve: a bolinha na margem, a linha amarela, o painel de variaveis e
    // os botoes. Os eventos do motor chegam na thread do sketch e sao
    // despachados para ca com BeginInvoke -- Invoke travaria a pausa com
    // a janela esperando a thread no Join do parar.

    static readonly Brush CorDoNome = MargemDePausas.Congelado(Color.FromRgb(0x9C, 0xDC, 0xFE));
    static readonly Brush CorDoNumero = MargemDePausas.Congelado(Color.FromRgb(0xB5, 0xCE, 0xA8));
    static readonly Brush CorDoTexto = MargemDePausas.Congelado(Color.FromRgb(0xCE, 0x91, 0x78));

    void MontarDepurador()
    {
        fundoDaPausa = new FundoDaPausa(MarcadaNoCampo);
        margemDePausas = new MargemDePausas(MarcadaNoCampo)
        {
            ToolTip = "Clique para marcar um ponto de parada (F9)"
        };
        margemDePausas.Clicou += AlternarPausa;

        // Antes dos numeros de linha, como na Arduino IDE.
        txtCodigo.TextArea.LeftMargins.Insert(0, margemDePausas);
        txtCodigo.TextArea.TextView.BackgroundRenderers.Add(fundoDaPausa);

        // Os botoes seguem o parar, que ja sabe quando ha sketch rodando --
        // inclusive quando a ultima placa termina sozinha.
        btParar.IsEnabledChanged += (_, _) => AtualizarBotoesDaPausa();
    }

    SortedSet<int> PausasDe(string idDaPlaca)
    {
        string chave = idDaPlaca ?? "";
        if (!pausasPorPlaca.TryGetValue(chave, out var linhas))
            pausasPorPlaca[chave] = linhas = [];
        return linhas;
    }

    bool MarcadaNoCampo(int linha)
        => pausasPorPlaca.TryGetValue(placaDoEditor ?? "", out var linhas) && linhas.Contains(linha);

    void AlternarPausa(int linha)
    {
        if (linha < 1) return;

        var linhas = PausasDe(placaDoEditor);
        bool marcou = linhas.Add(linha);
        if (!marcou) linhas.Remove(linha);

        // Com o sketch rodando, vale na hora.
        if (placaDoEditor is not null && depuradores.TryGetValue(placaDoEditor, out var depurador))
            depurador.DefinirPausas(linhas);

        RedesenharPausas();
        Aviso(marcou ? $"ponto de parada na linha {linha}."
                     : $"ponto de parada da linha {linha} tirado.");
    }

    void PausarOuContinuar(object s = null, RoutedEventArgs e = null)
    {
        if (!btParar.IsEnabled || depuradores.Count == 0) return;

        if (placaPausada is not null)
        {
            // Continua todas: com duas placas paradas, um "continuar" que
            // soltasse so uma deixaria a outra parada sem ninguem ver.
            foreach (var depurador in depuradores.Values) depurador.Continuar();
            Aviso("rodando...");
        }
        else
        {
            foreach (var depurador in depuradores.Values) depurador.Pausar();
            Aviso("pausando...");
        }
    }

    void Passo(object s = null, RoutedEventArgs e = null)
    {
        if (placaPausada is null || !depuradores.TryGetValue(placaPausada, out var depurador)) return;
        depurador.Passo();
    }

    void AoPausar(Depurador depurador, string idDaPlaca, Depurador.Retrato retrato)
    {
        // Pausa de uma rodada que ja acabou chega atrasada: fica de fora.
        if (!depuradores.TryGetValue(idDaPlaca, out var atual) || atual != depurador) return;

        placaPausada = idDaPlaca;
        linhaPausada = retrato.Linha;

        // Mostra o codigo de quem parou. Com duas placas, a pausa pode vir
        // da que nao esta no campo.
        if (placaDoEditor != idDaPlaca && editor.Atual.Placas().Any(p => p.Id == idDaPlaca))
        {
            GuardarCodigoDoEditor();
            placaDoEditor = idDaPlaca;
            MostrarCodigoDaPlaca();
        }

        if (retrato.Linha >= 1 && retrato.Linha <= txtCodigo.Document.LineCount)
            txtCodigo.ScrollToLine(retrato.Linha);

        MostrarVariaveis(idDaPlaca, retrato);
        RedesenharPausas();
        AtualizarBotoesDaPausa();
        Aviso($"pausado na linha {retrato.Linha}. F6 continua, F10 anda uma linha.");
    }

    void AoRetomar(Depurador depurador, string idDaPlaca)
    {
        if (!depuradores.TryGetValue(idDaPlaca, out var atual) || atual != depurador) return;
        if (placaPausada == idDaPlaca) SairDaPausa();
    }

    void SairDaPausa()
    {
        placaPausada = null;
        linhaPausada = -1;
        textoDasVariaveis = "";
        if (painelVariaveis is not null) painelVariaveis.Visibility = Visibility.Collapsed;
        RedesenharPausas();
        AtualizarBotoesDaPausa();
    }

    void AtualizarBotoesDaPausa()
    {
        if (btPausar is null) return;

        bool rodando = btParar.IsEnabled;
        bool pausado = rodando && placaPausada is not null;

        btPausar.IsEnabled = rodando;
        btAoVivo.IsEnabled = rodando;
        if (!rodando && aoVivo && painelVariaveis.Visibility == Visibility.Visible && placaPausada is null)
            painelVariaveis.Visibility = Visibility.Collapsed;
        btPausar.Content = pausado ? "▶" : "❚❚";
        btPausar.ToolTip = pausado ? "Continuar (F6)" : "Pausar (F6)";
        btPasso.IsEnabled = pausado;
    }

    void RedesenharPausas()
    {
        if (margemDePausas is null) return;

        // A seta so aparece se o codigo no campo e o da placa que parou.
        int linha = placaPausada is not null && placaPausada == placaDoEditor ? linhaPausada : -1;
        margemDePausas.LinhaPausada = linha;
        fundoDaPausa.LinhaPausada = linha;
        margemDePausas.InvalidateVisual();
        txtCodigo.TextArea.TextView.InvalidateLayer(
            ICSharpCode.AvalonEdit.Rendering.KnownLayer.Background);
    }

    // ================= variaveis ao vivo =================

    /// <summary>O painel das variaveis atualizando com o sketch rodando (o botao x=).</summary>
    bool aoVivo;
    DispatcherTimer relogioAoVivo;
    DateTime ultimoRetratoAoVivo = DateTime.MinValue;

    void AlternarAoVivo(object s = null, RoutedEventArgs e = null)
    {
        aoVivo = !aoVivo;
        btAoVivo.Opacity = aoVivo ? 1 : 0.75;
        btAoVivo.BorderBrush = aoVivo ? new SolidColorBrush(Color.FromRgb(0x76, 0xD7, 0xC4)) : null;
        btAoVivo.BorderThickness = new Thickness(aoVivo ? 2 : 0);
        if (aoVivo)
        {
            relogioAoVivo ??= new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Background, (_, _) => PedirRetratoAoVivo(), Dispatcher);
            relogioAoVivo.Start();
            PedirRetratoAoVivo();
            Aviso("variáveis ao vivo: o painel acompanha o sketch rodando.");
        }
        else
        {
            relogioAoVivo?.Stop();
            if (placaPausada is null) painelVariaveis.Visibility = Visibility.Collapsed;
            Aviso("variáveis ao vivo desligadas.");
        }
    }

    /// <summary>A placa das variaveis ao vivo: a do codigo no editor, ou a unica rodando.</summary>
    string PlacaAoVivo()
        => placaDoEditor is not null && depuradores.ContainsKey(placaDoEditor) ? placaDoEditor : depuradores.Keys.FirstOrDefault();

    void PedirRetratoAoVivo()
    {
        if (!aoVivo || !btParar.IsEnabled || placaPausada is not null) return;
        if (PlacaAoVivo() is not { } placa || !depuradores.TryGetValue(placa, out var depurador)) return;
        depurador.PedirRetrato();

        // O compilado que nunca chama delay (um laco cheio) nao atende o pedido:
        // as globais dele vem direto da memoria.
        if ((DateTime.UtcNow - ultimoRetratoAoVivo).TotalMilliseconds > 700 && depurador.GlobaisAgora() is { } globais)
            MostrarVariaveis(placa, globais, aoVivoAgora: true);
    }

    void AoRetratar(Depurador depurador, string idDaPlaca, Depurador.Retrato retrato)
    {
        if (!aoVivo || placaPausada is not null || !btParar.IsEnabled) return;
        if (!depuradores.TryGetValue(idDaPlaca, out var atual) || atual != depurador || PlacaAoVivo() != idDaPlaca) return;
        ultimoRetratoAoVivo = DateTime.UtcNow;
        MostrarVariaveis(idDaPlaca, retrato, aoVivoAgora: true);
    }

    /// <summary>Para o teste: liga ou desliga as variaveis ao vivo.</summary>
    internal void TesteAoVivo(bool ligar)
    {
        if (aoVivo != ligar) AlternarAoVivo();
    }

    /// <summary>Para o teste: o titulo e o texto do painel das variaveis.</summary>
    internal (string Titulo, string Texto, bool Visivel) TestePainelDeVariaveis
        => (txtTituloVariaveis.Text, textoDasVariaveis, painelVariaveis.Visibility == Visibility.Visible);

    void MostrarVariaveis(string idDaPlaca, Depurador.Retrato retrato, bool aoVivoAgora = false)
    {
        string dona = bancada.Quantas > 1 ? NomeCurtoDaPlaca(idDaPlaca).Trim('[', ']') + " · " : "";
        txtTituloVariaveis.Text = aoVivoAgora
            ? retrato.Linha > 0 ? $"AO VIVO · {dona}LINHA {retrato.Linha}" : $"AO VIVO · {dona}GLOBAIS"
            : $"PAUSADO · {dona}LINHA {retrato.Linha}";
        txtTituloVariaveis.Foreground = Tema.Pincel(aoVivoAgora ? Tema.Destaque : Tema.Aviso);
        painelVariaveis.BorderBrush = Tema.Pincel(aoVivoAgora ? Tema.Destaque : Tema.BordaDoAviso);

        var linhas = txtVariaveis.Inlines;
        linhas.Clear();
        var resumo = new List<string>();

        if (retrato.Variaveis.Count == 0)
            linhas.Add(new System.Windows.Documents.Run("nenhuma variável declarada até aqui")
                { Foreground = Cinza });

        foreach (var v in retrato.Variaveis)
        {
            if (resumo.Count > 0) linhas.Add(new System.Windows.Documents.LineBreak());

            linhas.Add(new System.Windows.Documents.Run(v.Nome) { Foreground = CorDoNome });
            linhas.Add(new System.Windows.Documents.Run(" = ") { Foreground = Cinza });
            linhas.Add(new System.Windows.Documents.Run(v.Valor)
                { Foreground = v.Valor.StartsWith('"') ? CorDoTexto : CorDoNumero });
            if (v.Global)
                linhas.Add(new System.Windows.Documents.Run("   global") { Foreground = Cinza, FontSize = 10 });

            resumo.Add($"{v.Nome} = {v.Valor}{(v.Global ? " (global)" : "")}");
        }

        textoDasVariaveis = string.Join(" | ", resumo);
        painelVariaveis.Visibility = Visibility.Visible;
        if (!aoVivoAgora) rolagemVariaveis.ScrollToTop();
    }

    // ================= painel de rede e bluetooth =================

    void TrocarAba(object s, RoutedEventArgs e)
    {
        string nome = (string)((Button)s).Tag;

        // Clicar na aba que esta solta TRAZ ELA DE VOLTA. So marcar como
        // escolhida uma aba que esta noutra janela deixaria este painel
        // vazio, com a aba acesa e nada embaixo.
        if (JanelaSolta.EstaSolto(nome)) { SoltarAba(nome); return; }

        abaDoPainel = nome;
        AtualizarAbas();
    }

    /// <summary>As tres abas de baixo, cada uma com o botao e o nome dela.</summary>
    (UIElement Painel, Button Botao, string Nome, string Titulo)[] AbasDeBaixo() =>
    [
        (abaConsole, btAbaConsole, "console", "Console"),
        (abaSerial, btAbaSerial, "serial", "Monitor serial"),
        (abaRede, btAbaRede, "rede", "Rede"),
        (abaBluetooth, btAbaBluetooth, "bluetooth", "Bluetooth"),
        (abaPlaca, btAbaPlaca, "placa", "Placa de verdade"),
    ];

    /// <summary>
    /// Solta uma aba de baixo sozinha, ou a traz de volta.
    ///
    /// Alpha 18: "assim posso ter, por exemplo, o bluetooth acoplado e o
    /// serial fora". A aba e a MESMA -- muda de lugar, nao e copiada --,
    /// entao o monitor solto continua recebendo as linhas do sketch.
    /// </summary>
    void SoltarAbaDeBaixo(object s, RoutedEventArgs e)
    {
        if ((s as FrameworkElement)?.Tag is string qual) SoltarAba(qual);
    }

    void SoltarAba(string qual)
    {
        var aba = AbasDeBaixo().FirstOrDefault(a => a.Nome == qual);
        if (aba.Painel is null) return;

        // Aqui dentro so uma aba aparece por vez. Soltar a que estava
        // escondida abriria uma janela em branco, entao ela acende
        // ANTES de sair.
        bool voltando = JanelaSolta.EstaSolto(qual);
        if (!voltando) aba.Painel.Visibility = Visibility.Visible;

        LembrarMedidas();
        JanelaSolta.Alternar(this, qual, aba.Titulo, aba.Painel);

        // A aba que VOLTA vira a aba da vez: quem pediu o monitor serial
        // de volta quer olhar para ele. Antes ele voltava escondido atras
        // da aba que estivesse aberta, e parecia que nao tinha voltado.
        if (voltando)
        {
            abaDoPainel = qual;
            AtualizarAbas();
        }
    }

    void AtualizarAbas()
    {
        // A aba escolhida saiu para outra janela: mostra aqui alguma que
        // tenha ficado, senao o painel vira um retangulo vazio.
        if (JanelaSolta.EstaSolto(abaDoPainel))
        {
            string aqui = AbasDeBaixo().FirstOrDefault(a => !JanelaSolta.EstaSolto(a.Nome)).Nome;
            if (aqui is not null) abaDoPainel = aqui;
        }

        foreach (var (painel, botao, nome, _) in AbasDeBaixo())
        {
            bool solta = JanelaSolta.EstaSolto(nome);

            // Na janela dela a aba esta sempre a vista: quem manda na
            // visibilidade aqui e a escolha da tira, e la nao ha tira.
            painel.Visibility = solta || abaDoPainel == nome
                ? Visibility.Visible : Visibility.Collapsed;

            botao.Foreground = Tema.Pincel(!solta && abaDoPainel == nome
                ? Tema.TextoForte : Tema.TextoFraco);
            botao.FontStyle = solta ? FontStyles.Italic : FontStyles.Normal;
            botao.ToolTip = solta ? "Está numa janela separada · clique para trazer de volta" : null;
        }

        // A aba da placa aberta sem nada rodando: diz para que ela serve.
        if (abaDoPainel == "placa" || JanelaSolta.EstaSolto("placa")) ApresentarAbaDaPlaca();
    }

    /// <summary>Mostra a aba pedida (e serve ao teste).</summary>
    void MostrarAba(string nome)
    {
        abaDoPainel = nome;
        AtualizarAbas();
    }

    /// <summary>A descricao do projeto, como esta na aba.</summary>
    internal string TesteDescricao
    {
        get => txtDescricao.Text;
        set => txtDescricao.Text = value;
    }

    void AlternarInternet(object s = null, RoutedEventArgs e = null)
    {
        redeSimulada.UsarInternet = miInternet.IsChecked;
        Aviso(miInternet.IsChecked
            ? "a rede do simulador pode usar a internet deste computador."
            : "rede sem internet: as placas conversam entre si e com o computador.");
    }

    void EnviarPeloSerial(object s = null, RoutedEventArgs e = null)
    {
        string texto = txtEntradaSerial.Text;
        if (texto.Length == 0) return;

        // Para a placa que esta no editor: e a dona do monitor em foco. Com o
        // baud do monitor diferente do sketch, os bytes chegam trocados.
        var maquina = bancada.De(placaDoEditor);
        var bytes = System.Text.Encoding.UTF8.GetBytes(texto + FimDeLinhaDoMonitor());
        if (BaudErrado(placaDoEditor, maquina, out _, out _)) bytes = Embaralhar(bytes);
        maquina.EntradaSerial.Adicionar(bytes);
        maquina.EscreverSerial($"[{maquina.Ms,7}ms] > {texto}");
        txtEntradaSerial.Clear();
        AtualizarSerial();
    }

    void TeclaNaEntradaSerial(object s, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        EnviarPeloSerial();
        e.Handled = true;
    }

    void PublicarNoMqtt(object s = null, RoutedEventArgs e = null)
    {
        string topico = txtTopicoMqtt.Text.Trim();
        if (topico.Length == 0) { Aviso("escreva o tópico antes de publicar."); return; }

        redeSimulada.PublicarDoPainel(topico, txtMensagemMqtt.Text);
        txtMensagemMqtt.Clear();
        AtualizarPainelDeRede();
    }

    void TeclaNoMqtt(object s, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        PublicarNoMqtt();
        e.Handled = true;
    }

    // =====================================================================
    //  o baud do monitor serial
    // =====================================================================

    static readonly int[] BaudsDoMonitor = [300, 1200, 2400, 4800, 9600, 19200, 38400, 57600, 74880, 115200,
                                            230400, 250000, 500000, 1000000, 2000000];

    /// <summary>As placas que ja levaram o aviso do baud errado nesta rodada.</summary>
    readonly HashSet<string> avisouBaud = [];

    /// <summary>
    /// O monitor desta placa esta num baud diferente do Serial.begin? (mais de
    /// 3% de diferenca, a mesma conta dos fios). Sem Serial.begin, nao ha o
    /// que comparar.
    /// </summary>
    bool BaudErrado(string idDaPlaca, Maquina maquina, out int doMonitor, out int doSketch)
    {
        doSketch = maquina?.BaudDaSerial ?? 0;
        doMonitor = editor.Atual.Achar(idDaPlaca)?.BaudDoMonitor ?? doSketch;
        return doSketch > 0 && doMonitor > 0 && Math.Abs(doMonitor - doSketch) > doSketch * 0.03;
    }

    /// <summary>O que o baud trocado faz com os bytes: a mesma conta da serial entre placas.</summary>
    static byte[] Embaralhar(byte[] bytes)
    {
        var saida = new byte[bytes.Length];
        for (int i = 0; i < bytes.Length; i++) saida[i] = (byte)(((bytes[i] ^ 0xA5) * 7 + i) & 0xFF);
        return saida;
    }

    /// <summary>Uma linha do monitor lida no baud errado: o tempo fica, o texto vira lixo.</summary>
    static string LinhaEmbaralhada(string linha)
    {
        var tempo = TempoDaLinha.Match(linha);
        if (!tempo.Success || tempo.Index != 0) return linha;
        string texto = linha[tempo.Length..];
        if (texto.StartsWith("> ")) return linha;                   // o eco do que se digitou
        var bytes = Embaralhar(System.Text.Encoding.UTF8.GetBytes(texto));
        var lixo = new System.Text.StringBuilder(bytes.Length);
        foreach (byte b in bytes) lixo.Append(b is >= 0x20 and < 0x7F ? (char)b : '\uFFFD');
        return linha[..tempo.Length] + lixo;
    }

    /// <summary>O rotulo do botao do baud: o da placa do editor (o escolhido, ou o do sketch).</summary>
    void MostrarBaudDoMonitor()
    {
        var maquina = placaDoEditor is null ? null : bancada.De(placaDoEditor);
        var peca = editor.Atual.Achar(placaDoEditor);
        int baud = peca?.BaudDoMonitor ?? (maquina?.BaudDaSerial is > 0 and var b ? b : 9600);
        string rotulo = $"{baud} baud";
        if (!Equals(btBaudSerial.Content, rotulo)) btBaudSerial.Content = rotulo;
    }

    void AbrirListaDeBaud(object s = null, RoutedEventArgs e = null)
    {
        var peca = editor.Atual.Achar(placaDoEditor);
        if (peca is null) { Aviso("não há placa no editor para o monitor serial."); return; }
        var maquina = bancada.De(placaDoEditor);
        int doSketch = maquina?.BaudDaSerial ?? 0;

        var menu = new ContextMenu { PlacementTarget = btBaudSerial };
        var seguir = new MenuItem
        {
            Header = doSketch > 0 ? $"Seguir o Serial.begin ({doSketch})" : "Seguir o Serial.begin",
            IsChecked = peca.BaudDoMonitor is null,
        };
        seguir.Click += (_, _) => EscolherBaud(peca, null);
        menu.Items.Add(seguir);
        menu.Items.Add(new Separator());
        foreach (int baud in BaudsDoMonitor)
        {
            var item = new MenuItem { Header = $"{baud} baud", IsChecked = peca.BaudDoMonitor == baud };
            item.Click += (_, _) => EscolherBaud(peca, baud);
            menu.Items.Add(item);
        }
        btBaudSerial.ContextMenu = menu;
        menu.IsOpen = true;
    }

    /// <summary>O baud escolhido vai para a peca da placa (e para o arquivo do projeto).</summary>
    internal void EscolherBaud(PecaNoCircuito peca, int? baud)
    {
        if (peca is null || peca.BaudDoMonitor == baud) return;
        peca.BaudDoMonitor = baud;
        avisouBaud.Remove(peca.Id);
        MostrarBaudDoMonitor();
    }

    /// <summary>A lista de placas visíveis, no menu escuro do programa.</summary>
    void AbrirListaDeBluetooth(object s = null, RoutedEventArgs e = null)
    {
        var dispositivos = redeSimulada.DispositivosVisiveis();
        if (dispositivos.Count == 0)
        {
            Aviso("nenhuma placa com Bluetooth ligado (falta SerialBT.begin no sketch).");
            return;
        }

        var menu = new ContextMenu { PlacementTarget = btDispositivosBt, IsOpen = true };
        foreach (string nome in dispositivos)
        {
            var item = new MenuItem { Header = nome, IsChecked = nome == dispositivoBtEscolhido };
            item.Click += (_, _) =>
            {
                dispositivoBtEscolhido = nome;
                dispositivosMostrados = "";        // força redesenhar o rótulo
                AtualizarPainelDeRede();
            };
            menu.Items.Add(item);
        }
        btDispositivosBt.ContextMenu = menu;
    }

    void ConectarOuSairDoBluetooth(object s = null, RoutedEventArgs e = null)
    {
        if (redeSimulada.CelularConectadoEm is not null)
        {
            redeSimulada.DesconectarCelular();
            AtualizarPainelDeRede();
            return;
        }

        string nome = dispositivoBtEscolhido ?? redeSimulada.DispositivosVisiveis().FirstOrDefault();
        if (nome is null) { Aviso("nenhuma placa com Bluetooth ligado (SerialBT.begin)."); return; }
        if (!redeSimulada.ConectarCelular(nome)) Aviso($"não achei \"{nome}\".");
        AtualizarPainelDeRede();
    }

    void EnviarPeloBluetooth(object s = null, RoutedEventArgs e = null)
    {
        string texto = txtEntradaBt.Text;
        if (texto.Length == 0) return;
        if (!redeSimulada.EnviarDoCelular(texto + "\n"))
        {
            Aviso("conecte o celular numa placa antes de enviar.");
            return;
        }
        txtEntradaBt.Clear();
        AtualizarPainelDeRede();
    }

    void TeclaNoBluetooth(object s, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        EnviarPeloBluetooth();
        e.Handled = true;
    }

    /// <summary>
    /// Traz o que a rede fez desde o ultimo quadro. O Bluetooth vai para a
    /// aba dele; o resto (WiFi, HTTP, MQTT) para a aba Rede.
    /// </summary>
    void AtualizarPainelDeRede()
    {
        var novos = redeSimulada.Novos(ref eventosDeRedeVistos);
        if (novos.Count > 0)
        {
            foreach (var evento in novos)
            {
                string linha = $"{evento.Hora:HH:mm:ss}  {evento.Placa} · {evento.Texto}";
                (evento.Tipo == "bt" ? linhasDeBluetooth : linhasDeRede).Add(linha);
            }

            if (linhasDeRede.Count > 300) linhasDeRede.RemoveRange(0, linhasDeRede.Count - 300);
            if (linhasDeBluetooth.Count > 300) linhasDeBluetooth.RemoveRange(0, linhasDeBluetooth.Count - 300);

            txtRede.Text = string.Join(Environment.NewLine, linhasDeRede);
            txtBluetooth.Text = string.Join(Environment.NewLine, linhasDeBluetooth);
            rolagemRede.ScrollToEnd();
            rolagemBt.ScrollToEnd();
        }

        AtualizarLinksDeRede();
        AtualizarDispositivosDeBluetooth();
        AtualizarAvisosDeRadio();
    }

    string avisoDeRadioMostrado;

    /// <summary>
    /// Diz na tela quando o WiFi e o Bluetooth estao sendo inventados.
    ///
    /// O professor foi explicito: o aviso NAO pode sair no monitor
    /// serial. O monitor e do sketch -- o que sai la e o que o programa
    /// do aluno imprimiu, e misturar recado do simulador com saida do
    /// programa confunde quem esta aprendendo a ler o monitor.
    /// </summary>
    void AtualizarAvisosDeRadio()
    {
        string doWifi = redeSimulada.MotivoDaSimulacao;
        string doBt = redeSimulada.MotivoDoBluetooth;

        string assinatura = doWifi + "#" + doBt;
        if (assinatura == avisoDeRadioMostrado) return;
        avisoDeRadioMostrado = assinatura;

        avisoDeRadio.Visibility = doWifi is null ? Visibility.Collapsed : Visibility.Visible;
        if (doWifi is not null)
            txtAvisoDeRadio.Text =
                "Não foi possível usar o WiFi deste computador: " + doWifi + ". "
                + "O simulador está mostrando redes simuladas no lugar das de verdade.";

        avisoDeBluetooth.Visibility = doBt is null ? Visibility.Collapsed : Visibility.Visible;
        if (doBt is not null)
            txtAvisoDeBluetooth.Text =
                "Não foi possível usar o Bluetooth deste computador: " + doBt + ". "
                + "As conexões entre a placa e o celular continuam simuladas.";
    }

    /// <summary>Os endereços que o navegador deste computador alcança.</summary>
    void AtualizarLinksDeRede()
    {
        var servidores = redeSimulada.Servidores;
        string assinatura = string.Join("|", servidores.Select(x => x.UrlLocal + x.Dona.Nome))
                            + "#" + redeSimulada.PortaDoBroker;
        if (assinatura == linksMostrados) return;
        linksMostrados = assinatura;

        linksDeRede.Children.Clear();
        foreach (var servidor in servidores)
        {
            if (servidor.UrlLocal is null) continue;
            string url = servidor.UrlLocal;

            // O pedido do professor: subir um servidor na placa tem que
            // abrir a pagina no navegador, como acontece quando se liga
            // uma ESP de verdade na rede. So na PRIMEIRA vez que este
            // endereco aparece -- reabrir a cada quadro seria praga.
            if (Preferencias.AbrirServidorNoNavegador && servidoresAbertos.Add(url))
                AbrirNoNavegador(url);

            // O endereco que o SKETCH anuncia (192.168.4.1 no modo AP)
            // nao existe neste computador: quem existe e a ponte em
            // localhost. Mostrar os dois lado a lado e o que liga uma
            // coisa a outra -- sem isso o aluno digita o IP que leu no
            // monitor serial e o navegador nao acha nada.
            string ipDaPlaca = redeSimulada.Wifi(servidor.Dona).Ip;

            var botao = new Button
            {
                Content = string.IsNullOrEmpty(ipDaPlaca) || ipDaPlaca == "0.0.0.0"
                    ? $"🌐 {servidor.Dona.Nome}: {url}"
                    : $"🌐 {servidor.Dona.Nome}: {ipDaPlaca}:{servidor.Porta} → {url}",
                Style = (Style)FindResource("BotaoPequeno"),
                Margin = new Thickness(0, 0, 6, 0),
                ToolTip = string.IsNullOrEmpty(ipDaPlaca)
                    ? "Abre no navegador deste computador"
                    : $"O sketch anuncia {ipDaPlaca}, que é o endereço da placa simulada."
                      + Environment.NewLine
                      + $"Do navegador desta máquina, a mesma página abre em {url}.",
            };
            botao.Click += (_, _) => AbrirNoNavegador(url);
            linksDeRede.Children.Add(botao);
        }

        if (redeSimulada.PortaDoBroker > 0)
            linksDeRede.Children.Add(new TextBlock
            {
                Text = $"broker MQTT: localhost:{redeSimulada.PortaDoBroker}",
                Foreground = Cinza,
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
            });
    }

    /// <summary>
    /// Enderecos que ja foram abertos nesta execucao.
    ///
    /// Zerado a cada Rodar: reiniciar a simulacao abre a pagina de novo,
    /// que e o que se espera de uma placa que reiniciou.
    /// </summary>
    readonly HashSet<string> servidoresAbertos = [];

    void AbrirNoNavegador(string url)
    {
        // Nem o autoteste nem o passeio abrem o navegador de quem os roda:
        // as demos de servidor web rodam nos dois, e cada uma abriria uma
        // aba no computador da pessoa.
        if (Autoteste.Rodando || Passeio.Ativo) return;

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url)
            { UseShellExecute = true });
        }
        catch (Exception ex) { Erro("nao consegui abrir o navegador: " + ex.Message); }
    }

    void AtualizarDispositivosDeBluetooth()
    {
        var dispositivos = redeSimulada.DispositivosVisiveis();
        string conectado = redeSimulada.CelularConectadoEm;
        string assinatura = string.Join(",", dispositivos) + "#" + conectado + "#" + dispositivoBtEscolhido;
        if (assinatura == dispositivosMostrados) return;
        dispositivosMostrados = assinatura;

        if (dispositivoBtEscolhido is null || !dispositivos.Contains(dispositivoBtEscolhido))
            dispositivoBtEscolhido = conectado ?? dispositivos.FirstOrDefault();

        txtBtEscolhido.Text = conectado is not null ? $"conectado em \"{conectado}\""
            : dispositivoBtEscolhido is not null ? $"escolhida: \"{dispositivoBtEscolhido}\""
            : "nenhuma placa com Bluetooth ligado";

        btDispositivosBt.IsEnabled = dispositivos.Count > 0;
        btConectarBt.Content = conectado is null ? "Conectar" : "Desconectar";
    }

    // ================= desenho =================
    int ultimoHz = -1;
    string ultimosTons = "";

    /// <summary>O que cada placa dormindo espera, para avisar uma vez so.</summary>
    readonly Dictionary<string, string> sonoAvisado = [];

    void AvisarSono()
    {
        foreach (var (id, maquina) in bancada.Todas)
        {
            string sono = maquina.Sono;
            if (sono == sonoAvisado.GetValueOrDefault(id)) continue;
            sonoAvisado[id] = sono;
            if (sono is not null)
                Aviso(NomeCurtoDaPlaca(id).Trim('[', ']') + " em sono profundo: " + sono + ".");
        }
    }

    void AtualizarTela(object s, EventArgs e)
    {
        rede ??= new Netlist(editor.Atual);
        AvisarSono();

        editor.AplicarEntradas(bancada, rede);
        redeSimulada.DefinirPontosDeAcesso(editor.PontosDeAcessoDoCircuito());
        ConferirQueimados();
        ConferirEnergiaDasPlacas();
        if (usbEmEdicao is { } usb && editor.Selecionada == usb.peca)
        {
            string curta = editor.SituacaoCurta(usb.peca.Id), inteira = editor.SituacaoDaEnergia(usb.peca.Id);
            if (usb.texto.Text != curta) usb.texto.Text = curta;
            if (!Equals(usb.texto.ToolTip, inteira)) usb.texto.ToolTip = inteira;
        }
        if (reguladorEmEdicao is { } reg && editor.Selecionada == reg.peca)
        {
            string curta = editor.SituacaoDoRegulador(reg.peca.Id, curta: true);
            string inteira = editor.SituacaoDoRegulador(reg.peca.Id, curta: false);
            if (reg.texto.Text != curta) reg.texto.Text = curta;
            if (!Equals(reg.texto.ToolTip, inteira)) reg.texto.ToolTip = inteira;
        }
        ConferirRitmoDoChip();
        editor.RefletirEstado(bancada, rede);

        // Buzzer e alto-falante num pino: o tom do primeiro que estiver
        // tocando. Alto-falante num amplificador I2S: o audio da placa.
        int hz = 0;
        var outrosTons = new List<int>();
        SaidaDeAudio somDoI2s = null;
        foreach (var peca in editor.Atual.Pecas)
        {
            if (peca.Tipo == "alto-falante")
            {
                var (audio, tom) = editor.SomDoAltoFalante(peca, bancada, rede);
                somDoI2s ??= audio;
                if (hz == 0) hz = tom;
                continue;
            }
            if (peca.Tipo != "buzzer") continue;
            // O buzzer toca o tom da placa em que ELE esta ligado, que
            // pode nao ser a que esta no editor.
            var (placa, gpio) = rede.PinoDe(editor.Atual, peca.Id, "1");
            if (gpio < 0) (placa, gpio) = rede.PinoDe(editor.Atual, peca.Id, "2");
            if (gpio < 0) continue;

            // Varios buzzers tocando juntos (os cilindros do motor): todos soam.
            var maquina = bancada.De(placa);
            if (maquina.Frequencia[gpio] <= 0) continue;
            if (hz == 0) hz = maquina.Frequencia[gpio];
            else outrosTons.Add(maquina.Frequencia[gpio]);
        }
        // O ESC bipa pelo proprio motor: a musica de partida, o "armado".
        if (hz == 0) hz = editor.TomDosEscs();
        if (!Preferencias.Ligado(Preferencias.SomDoBuzzer, true)) { hz = 0; somDoI2s = null; outrosTons.Clear(); }
        string assinaturaDosTons = string.Join(",", outrosTons);
        if (hz != ultimoHz || assinaturaDosTons != ultimosTons)
        {
            ultimoHz = hz;
            ultimosTons = assinaturaDosTons;
            if (outrosTons.Count == 0) buzina.Definir(hz);
            else buzina.Definir([hz, .. outrosTons]);
        }
        // A suite e o passeio nao tocam no alto-falante de verdade (como nao
        // leem o teclado): o teste confere o caminho do som, e nao a caixa.
        // Sem alto-falante tocando: o DAC estereo (PCM5102A) com o fone, nos dois canais.
        bool emEstereo = false;
        if (somDoI2s is null && Preferencias.Ligado(Preferencias.SomDoBuzzer, true)
            && editor.SomDoPcm5102a(bancada, rede) is { } doDac)
        {
            somDoI2s = doDac;
            emEstereo = true;
        }
        falante.Tocar(btParar.IsEnabled && !Controle.SemDispositivos ? somDoI2s : null, emEstereo);

        // Uma placa ligou o Bluetooth (o SerialBT, ou comecou a anunciar pelo
        // BLE): o celular abre sozinho, uma vez por rodada. A suite e o
        // passeio abrem quando querem.
        if (btParar.IsEnabled && !celularAbertoNestaRodada && !Controle.SemDispositivos
            && Preferencias.Ligado(Preferencias.AbrirCelularSozinho, true)
            && (redeSimulada.DispositivosVisiveis().Count > 0 || redeSimulada.AnunciosBle().Count > 0))
        {
            celularAbertoNestaRodada = true;
            AbrirCelular();
        }

        AtualizarSerial();
        AtualizarPainelDeRede();
    }

    /// <summary>
    /// Queima o que estiver ligado errado, do jeito que queimaria na
    /// bancada.
    ///
    /// Roda a cada quadro porque o erro pode aparecer no meio da
    /// execucao: o LED sem resistor so estoura quando o codigo acende
    /// o pino, e e justamente essa a hora de mostrar.
    ///
    /// A checagem varre o circuito inteiro, entao so acontece com a
    /// placa energizada -- parado nao ha o que conferir.
    /// </summary>
    /// <summary>
    /// Ligacao errada que a porta nao tem forca para destruir: aparece
    /// uma vez e some. Sem isto ela voltaria a cada quadro e entupiria
    /// o monitor com a mesma linha.
    /// </summary>
    readonly HashSet<string> avisosDeLigacao = [];

    /// <summary>Já avisou nesta execução que o computador não acompanha o chip.</summary>
    bool avisouDoRitmo;

    /// <summary>
    /// O computador está conseguindo rodar o ATmega328P em tempo real?
    ///
    /// Numa máquina de laboratório mais fraca, ou com várias placas em
    /// Assembly ao mesmo tempo, pode não estar. O aviso sai UMA vez, e
    /// diz o que importa: a lógica e os ciclos continuam exatos -- o
    /// .hex gravado roda no tempo certo na placa --, mas a tela anda mais
    /// devagar, e não serve para calibrar um tempo no olho.
    /// </summary>
    void ConferirRitmoDoChip()
    {
        if (avisouDoRitmo) return;

        foreach (var (id, maquina) in bancada.Todas)
        {
            // O Assembly conta ciclo por ciclo: so a tela atrasa. O sketch
            // interpretado anda no relogio de verdade: o que atrasa e a conta
            // -- e um loop() que so consulta um botao ja da menos voltas que
            // no chip de 80 a 240 MHz, sem que isso mude nada. So avisa
            // quando a diferenca e grande.
            bool assembly = programas.TryGetValue(id, out var prog) && prog?.Montado is ProgramaMontado;

            // O programa compilado (C/C++ pelo clang, C# pelo Roslyn) roda na
            // velocidade do PC: a conta do interpretador nao vale para ele.
            if (maquina.Sketch is not null) continue;

            // Dois segundos antes de julgar: a primeira leva inclui a
            // montagem do programa e o aquecimento do executor.
            if (maquina.MsDeChipRodando < 2000 || maquina.VelocidadeDoChip >= (assembly ? 0.9 : 0.5)) continue;

            avisouDoRitmo = true;
            int porcento = (int)Math.Round(maquina.VelocidadeDoChip * 100);
            string nome = NomeCurtoDaPlaca(id).Trim('[', ']');
            string chip = maquina.Perfil?.Processador ?? "chip";
            console.Aviso(assembly
                ? $"{nome}: este computador está rodando o {chip} a {porcento}% da velocidade real. A lógica e a "
                  + "contagem de ciclos continuam exatas — o .hex gravado roda no tempo certo na placa —, mas aqui a "
                  + "tela anda mais devagar que ela."
                : $"{nome}: este computador não acompanha o {chip} nas contas do sketch — está a {porcento}% da "
                  + "velocidade real. O millis() e o delay() continuam no tempo certo, mas um laço de conta dá menos "
                  + "voltas por segundo que na placa, e um tempo medido com micros() em volta de uma conta sai maior.");
            return;
        }
    }

    void ConferirQueimados()
    {
        // Desligada nas configuracoes: nada queima -- mas na bancada queima,
        // e a janela de configuracoes diz isso ao lado da caixa.
        if (!Preferencias.Ligado(Preferencias.SimularQueima, true)) { editor.MostrarAvisos([]); return; }
        bool noMonitor = Preferencias.Ligado(Preferencias.AvisosNoMonitor, true);

        // As ligacoes com a placa (Queima) e a corrente de verdade (a conta de
        // energia do quadro): esta segunda e a que ve o LED na bateria.
        // Cada aviso ganha o selo dele no circuito, em cima da peca (ou do pino); o que
        // queimou fica com o selo vermelho ate a proxima rodada.
        var naTela = new List<Estouro>();
        foreach (var estouro in Queima.Conferir(editor.Atual, rede, bancada)
                                     .Concat(editor.EstourosPelaEnergia()))
        {
            if (editor.EstaQueimada(estouro.Peca)) continue;
            if (!estouro.Queimou) naTela.Add(estouro);

            var alvo = editor.Atual.Achar(estouro.Peca);
            string comoChama = alvo is null ? "peça"
                : Catalogo.Obter(alvo.Tipo)?.Nome ?? alvo.Tipo;

            if (!estouro.Queimou)
            {
                if (!avisosDeLigacao.Add(estouro.Peca + "|" + estouro.Motivo)) continue;

                Erro($"⚠ {comoChama}: {estouro.Motivo}");
                if (noMonitor)
                    foreach (var (_, aviso) in bancada.Todas)
                        lock (aviso.Trava) aviso.Serial.Add($"⚠ {comoChama}: {estouro.Motivo}");
                continue;
            }

            editor.Queimar(estouro.Peca);
            queimadosNaRodada[estouro.Peca] = estouro;

            var peca = editor.Atual.Achar(estouro.Peca);
            string nome = peca is null ? "peça"
                : Catalogo.Obter(peca.Tipo)?.Nome ?? peca.Tipo;

            Erro($"💥 {nome} queimou — {estouro.Motivo}");
            console.Aviso($"{nome}: {estouro.Motivo}");

            // Tambem no monitor: a barra de status some no proximo aviso
            // e o estouro e a coisa mais importante que aconteceu. Vai
            // para TODAS as placas -- nao da para saber em qual monitor
            // a pessoa esta olhando, e perder o aviso e pior.
            if (noMonitor)
                foreach (var (_, m) in bancada.Todas)
                    lock (m.Trava) m.Serial.Add($"💥 {nome} queimou: {estouro.Motivo}");
        }
        naTela.AddRange(queimadosNaRodada.Values.Where(q => editor.EstaQueimada(q.Peca)));
        editor.MostrarAvisos(naTela);
    }

    /// <summary>O que queimou nesta rodada, e por que: o selo vermelho fica ate a proxima.</summary>
    readonly Dictionary<string, Estouro> queimadosNaRodada = [];

    /// <summary>
    /// Reconstroi o painel a partir da lista da Maquina. Antes era
    /// incremental, com um contador do que ja tinha sido mostrado -- e o
    /// contador dessincronizava toda vez que a lista era limpa.
    /// </summary>
    /// <summary>
    /// Quantas linhas de cada placa ja foram para o painel.
    ///
    /// Uma contagem por placa, e nao uma so: cada uma tem o seu monitor
    /// e elas imprimem em ritmos diferentes.
    /// </summary>
    readonly Dictionary<string, int> serialPorPlaca = [];
    readonly Dictionary<string, int> avisosPorPlaca = [];

    /// <summary>O que ja esta escrito no painel, na ordem em que chegou.</summary>
    readonly List<string> painelSerial = [];

    /// <summary>
    /// Junta o monitor de TODAS as placas num painel so.
    ///
    /// Antes mostrava so a selecionada, e a saida da outra ficava
    /// invisivel ate alguem clicar nela -- num circuito com duas placas
    /// conversando, era metade da conversa.
    ///
    /// Com mais de uma placa cada linha ganha o nome de quem falou. Com
    /// uma so, nada muda: prefixo em todas as linhas de um circuito
    /// comum seria ruido puro.
    /// </summary>
    void AtualizarSerial()
    {
        var novasDoQuadro = new List<string>();
        bool mudou = false;
        bool varias = bancada.Quantas > 1;

        foreach (var (id, maquina) in bancada.Todas)
        {
            // Os avisos da placa durante a rodada (o GPIO 35 sem pull-up, o
            // pino da flash, o cartao mal ligado): no console, uma vez cada.
            List<string> avisos = null;
            lock (maquina.Trava)
            {
                int vistos = avisosPorPlaca.GetValueOrDefault(id);
                if (maquina.Avisos.Count < vistos) vistos = 0;
                if (maquina.Avisos.Count > vistos) avisos = [.. maquina.Avisos.Skip(vistos)];
                avisosPorPlaca[id] = maquina.Avisos.Count;
            }
            if (avisos is not null)
                foreach (string aviso in avisos)
                    console.Aviso((varias ? NomeCurtoDaPlaca(id) + " " : "") + aviso);

            List<string> novas;
            lock (maquina.Trava)
            {
                int total = maquina.Serial.Count;
                int mostradas = serialPorPlaca.GetValueOrDefault(id);
                if (total == mostradas) continue;

                novas = [.. maquina.Serial.Skip(mostradas)];
                serialPorPlaca[id] = total;
            }

            mudou = true;
            novasDoQuadro.AddRange(novas);

            string etiqueta = varias ? NomeCurtoDaPlaca(id) + " " : "";
            // O monitor desta placa num baud diferente do sketch: o texto vira lixo, como na IDE.
            bool errado = BaudErrado(id, maquina, out int doMonitor, out int doSketch);
            if (errado && avisouBaud.Add(id))
                console.Aviso($"{(varias ? NomeCurtoDaPlaca(id) + " " : "")}o monitor está a {doMonitor} baud e o sketch "
                              + $"abriu a serial a {doSketch}: o texto sai embaralhado. Escolha {doSketch} no baud do monitor.");
            foreach (string linha in novas) painelSerial.Add(etiqueta + (errado ? LinhaEmbaralhada(linha) : linha));
        }

        if (!mudou) return;

        // O painel guarda as ultimas N (linha demais trava a rolagem; o N e
        // das configuracoes); o arquivo do monitor da IDE recebe tudo,
        // entao nada se perde.
        int limite = Preferencias.Numero(Preferencias.LinhasNoSerial, 400, 50, 20000);
        if (painelSerial.Count > limite)
            painelSerial.RemoveRange(0, painelSerial.Count - limite);

        RedesenharSerial();
        MostrarBaudDoMonitor();

        MonitorIde.Escrever(novasDoQuadro);
    }

    /// <summary>A marca de tempo que abre cada linha do monitor: "[   1200ms] ".</summary>
    static readonly System.Text.RegularExpressions.Regex TempoDaLinha =
        new(@"\[\s*\d+ms\] ", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>As linhas do monitor na tela, com ou sem o tempo de cada uma.</summary>
    IEnumerable<string> LinhasDoSerial(int comeco, int quantas)
    {
        bool comTempo = Preferencias.Ligado(Preferencias.TempoNoSerial, true);
        var linhas = painelSerial.Skip(comeco).Take(quantas);
        return comTempo ? linhas : linhas.Select(l => TempoDaLinha.Replace(l, "", 1));
    }

    /// <summary>Linhas a mais em cima e embaixo da parte que aparece, para rolar sem refazer o texto.</summary>
    const int FolgaDoSerial = 5;

    int primeiraNaTelaDoSerial, quantasNaTelaDoSerial;

    /// <summary>
    /// O monitor na tela. So as linhas que aparecem vao para o texto, e as margens fazem o papel
    /// das outras na barra de rolagem: refazer todas a cada println pesava na janela inteira.
    /// </summary>
    void RedesenharSerial()
    {
        bool rolar = Preferencias.Ligado(Preferencias.RolarSerial, true);
        double altura = txtSerial.LineHeight;
        double janela = rolagemSerial.ViewportHeight > 0 ? rolagemSerial.ViewportHeight : 600;
        int total = painelSerial.Count;
        int quantas = Math.Min(total, (int)Math.Ceiling(janela / altura) + 2 * FolgaDoSerial);
        int primeira = rolar ? total - quantas
            : Math.Clamp((int)(rolagemSerial.VerticalOffset / altura) - FolgaDoSerial, 0, total - quantas);

        MostrarLinhasDoSerial(primeira, quantas);
        // A largura da linha mais comprida de todas (Consolas: toda letra tem a mesma largura),
        // para a barra de lado nao mudar conforme o que esta aparecendo.
        int maisComprida = 0;
        foreach (string linha in painelSerial) maisComprida = Math.Max(maisComprida, linha.Length);
        txtSerial.MinWidth = maisComprida * txtSerial.FontSize * 0.55;
        if (rolar) rolagemSerial.ScrollToEnd();
    }

    void MostrarLinhasDoSerial(int primeira, int quantas)
    {
        double altura = txtSerial.LineHeight;
        txtSerial.Text = string.Join(Environment.NewLine, LinhasDoSerial(primeira, quantas));
        txtSerial.Margin = new Thickness(0, primeira * altura, 0, (painelSerial.Count - primeira - quantas) * altura);
        primeiraNaTelaDoSerial = primeira;
        quantasNaTelaDoSerial = quantas;
    }

    /// <summary>Quem rola com a roda ou a barra chega nas linhas que ainda nao estao no texto.</summary>
    void RolouOSerial(object s, ScrollChangedEventArgs e)
    {
        if (e.VerticalChange == 0 && e.ViewportHeightChange == 0) return;
        double altura = txtSerial.LineHeight;
        int topo = (int)(rolagemSerial.VerticalOffset / altura);
        int fim = (int)Math.Ceiling((rolagemSerial.VerticalOffset + rolagemSerial.ViewportHeight) / altura);
        bool faltaEmCima = topo < primeiraNaTelaDoSerial;
        bool faltaEmBaixo = fim > primeiraNaTelaDoSerial + quantasNaTelaDoSerial && primeiraNaTelaDoSerial + quantasNaTelaDoSerial < painelSerial.Count;
        if (!faltaEmCima && !faltaEmBaixo) return;

        int quantas = Math.Min(painelSerial.Count, fim - topo + 2 * FolgaDoSerial);
        MostrarLinhasDoSerial(Math.Clamp(topo - FolgaDoSerial, 0, painelSerial.Count - quantas), quantas);
    }

    /// <summary>O fim de linha que o monitor manda, como na Arduino IDE.</summary>
    static string FimDeLinhaDoMonitor() => Preferencias.Texto(Preferencias.FimDeLinha, "lf") switch
    {
        "nada" => "",
        "cr" => "\r",
        "crlf" => "\r\n",
        _ => "\n",
    };

    /// <summary>Nome curto da placa, para caber na frente de cada linha.</summary>
    string NomeCurtoDaPlaca(string id)
    {
        var peca = editor.Atual.Achar(id);
        string curto = ApelidoDaPlaca(peca);

        // Duas placas do mesmo tipo ficariam com o mesmo nome no monitor e
        // no painel Rede -- e a conversa entre elas e justamente o que se
        // quer acompanhar. Entao a segunda em diante ganha numero.
        var iguais = editor.Atual.Placas().Where(x => ApelidoDaPlaca(x) == curto).ToList();
        if (iguais.Count > 1)
        {
            int qual = iguais.FindIndex(x => x.Id == id) + 1;
            if (qual > 0) curto = $"{curto} {qual}";
        }

        return $"[{curto}]";
    }

    /// <summary>"ESP32 DevKit V1" -> "ESP32"; "Arduino Uno" -> "Uno".</summary>
    static string ApelidoDaPlaca(PecaNoCircuito peca)
    {
        string nome = PerfilPlaca.De(peca?.Tipo)?.Nome ?? peca?.Tipo ?? "placa";
        return nome.StartsWith("Arduino ", StringComparison.Ordinal)
            ? nome["Arduino ".Length..]
            : nome.Split(' ')[0];
    }

    /// <summary>O nome do circuito e o autor, na barra de cima, do lado do Verificar e do Mandar.</summary>
    void AtualizarNomeNaBarra()
    {
        if (txtNomeDoCircuito is null) return;
        txtNomeDoCircuito.Text = NomeDoProjeto();
        string autor = AutorDoProjeto();
        txtAutorDoCircuito.Text = string.IsNullOrWhiteSpace(autor) ? "" : "por " + autor;
    }

    void AtualizarDica()
    {
        AtualizarNomeNaBarra();
        if (editor.QuantidadeSelecionada > 1)
        {
            txtDica.Text = $"{editor.QuantidadeSelecionada} peças selecionadas · "
                           + "arraste para mover todas · Del exclui";
            return;
        }

        var peca = editor.Selecionada;
        var def = peca is null ? null : Catalogo.Obter(peca.Tipo);

        int fios = editor.FiosSelecionados.Count;
        if (fios > 0)
        {
            txtDica.Text = fios == 1
                ? $"fio selecionado · Del apaga · {editor.Atual.Fios.Count} fios no total"
                : $"{fios} fios selecionados · Del apaga todos · "
                  + $"{editor.Atual.Fios.Count} fios no total";
            return;
        }

        // Sem nada selecionado a dica fica vazia (a contagem de pecas e fios saiu
        // daqui: o lugar agora e do nome do circuito, do lado do Mandar).
        txtDica.Text = def is null
            ? ""
            : peca.Tipo == "resistor"
                ? $"Resistor {peca.Ohms:0} Ω · R gira · Del exclui"
                : $"{def.Nome} · {def.Pinos.Count} pinos · R gira · Del exclui";
    }

    // ================= status =================
    void Aviso(string msg)
    {
        txtStatus.Foreground = Cinza;
        txtStatus.Text = msg;

        // A linha de status guarda UMA mensagem: a anterior some sem
        // deixar rastro. No console ela fica, e e por isso que o console
        // existe -- depois de um erro, o que interessa costuma ser a
        // mensagem de antes dele.
        console.Info(msg);

        AtualizarDica();
        AjustarVaos();
    }

    void Erro(string msg)
    {
        txtStatus.Foreground = Vermelho;
        txtStatus.Text = msg;
        console.Erro(msg);
        AjustarVaos();
    }

    /// <summary>
    /// Sketch de uma placa que acabou de entrar no circuito.
    ///
    /// Nao da para reaproveitar o modelo de abertura aqui: ele acende os
    /// GPIO 13, 14 e 16, que existem na ESP32 e nao num Arduino Uno.
    /// LED_BUILTIN vale em todas -- cada perfil sabe qual e o dele.
    /// </summary>
    const string ModeloEmBranco = """
        void setup() {
          Serial.begin(115200);
          pinMode(LED_BUILTIN, OUTPUT);
        }

        void loop() {
          digitalWrite(LED_BUILTIN, HIGH);
          delay(500);
          digitalWrite(LED_BUILTIN, LOW);
          delay(500);
        }
        """;

    const string ModeloDeSketch = """
        const int LED_VERMELHO = 14;
        const int LED_AMARELO  = 13;
        const int LED_VERDE    = 16;

        unsigned long ultimaTroca = 0;
        int estado = 0;

        void setup() {
          Serial.begin(115200);
          pinMode(LED_VERMELHO, OUTPUT);
          pinMode(LED_AMARELO, OUTPUT);
          pinMode(LED_VERDE, OUTPUT);
        }

        void loop() {
          if (millis() - ultimaTroca >= 1000) {
            ultimaTroca = millis();
            estado = (estado + 1) % 3;
            Serial.println(estado);
          }
          digitalWrite(LED_VERMELHO, estado == 0);
          digitalWrite(LED_AMARELO,  estado == 1);
          digitalWrite(LED_VERDE,    estado == 2);
        }
        """;
}
