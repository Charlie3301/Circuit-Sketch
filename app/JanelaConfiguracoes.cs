using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Simulador.App;

public sealed record AjustesDoPrograma(
    Func<bool> LerGrade, Action<bool> DefinirGrade,
    Func<string> LerEstiloDoFio, Action<string> DefinirEstiloDoFio,
    Func<bool> LerInternet, Action<bool> DefinirInternet,
    Func<string> LerCorDoFio, Action<string> DefinirCorDoFio,
    (string Nome, string Hex)[] Cores,
    Func<string> LerServidores, Func<string> LerBluetooth,
    Action AbrirPainelDeRede, Action AbrirPainelDeBluetooth,
    Action AbrirAjuda)
{
    /// <summary>Encolhe a janela principal ate caber na tela.</summary>
    public Action CaberNaTela { get; init; }

    /// <summary>Abre o acervo de bibliotecas.</summary>
    public Action AbrirBibliotecas { get; init; }

    /// <summary>Traz de volta os avisos fechados no X dos selos.</summary>
    public Action EsquecerAvisosFechados { get; init; }

    /// <summary>
    /// Poe na janela principal o que acabou de mudar aqui. Chamado a cada
    /// escolha: as configuracoes valem na hora, sem "Aplicar" nem reiniciar.
    /// </summary>
    public Action Aplicar { get; init; }

    /// <summary>As placas que o circuito novo pode trazer: id e nome.</summary>
    public IReadOnlyList<(string Id, string Nome)> Placas { get; init; } = [];
}

/// <summary>
/// As configuracoes, no formato do Blender: a lista de assuntos a
/// esquerda, um campo de busca em cima dela, e o assunto escolhido a
/// direita em blocos que abrem e fecham.
///
/// A versao anterior era uma coluna unica com tudo empilhado. Com sete
/// assuntos ela ja passava de uma tela de rolagem, e achar "tema" ou
/// "atalhos" virava caca -- nada dizia onde cada coisa estava nem
/// quanto ainda faltava para baixo.
/// </summary>
public class JanelaConfiguracoes : Window
{
    static readonly (string Id, string Nome)[] Tracados =
    [
        ("solto", Idioma.T("Solto (curva)")),
        ("reto", Idioma.T("Reto (linha direta)")),
        ("degrau", Idioma.T("Degrau (em L)")),
    ];

    /// <summary>Um assunto da lista da esquerda. Grupo separa com linha.</summary>
    sealed record Assunto(string Grupo, string Nome, string Palavras, Action<StackPanel> Montar);

    readonly AjustesDoPrograma ajustes;
    readonly List<Assunto> assuntos = [];

    // Refeitos a cada troca de tema (MontarJanela): a janela e montada
    // com as cores do tema da hora, e escolher outro tema com ela aberta
    // tem que repinta-la inteira.
    TextBox busca = new();
    StackPanel lista = new();
    StackPanel conteudo = new() { Margin = new Thickness(16, 12, 16, 16) };
    TextBlock titulo = new();
    ScrollViewer rolagem = new();

    string escolhido;

    public JanelaConfiguracoes(Window dono, AjustesDoPrograma ajustes)
    {
        this.ajustes = ajustes;

        try { Owner = dono; } catch (InvalidOperationException) { }

        Title = Idioma.T("Configurações");
        Width = 820;
        Height = 560;
        MinWidth = 640;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        Registrar();
        MontarJanela();

        // Escolheu um tema aqui dentro: a janela se repinta na hora, na
        // mesma pagina. Depois da volta do clique -- refazer a pagina de
        // dentro do proprio clique tiraria o chao do botao.
        void AoMudar() => Dispatcher.BeginInvoke(() =>
        {
            string texto = busca.Text;
            MontarJanela(escolhido);
            busca.Text = texto;
        });
        Tema.Mudou += AoMudar;
        Closed += (_, _) => Tema.Mudou -= AoMudar;
    }

    /// <summary>A janela inteira, com as cores do tema atual, aberta na pagina pedida.</summary>
    void MontarJanela(string pagina = null)
    {
        busca = new TextBox();
        lista = new StackPanel();
        conteudo = new StackPanel { Margin = new Thickness(16, 12, 16, 16) };
        titulo = new TextBlock();
        rolagem = new ScrollViewer();

        Background = Tema.Pincel(Tema.Fundo);

        var raiz = new Grid();
        raiz.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(186) });
        raiz.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        raiz.Children.Add(MontarLateral());

        var direita = new Grid();
        direita.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        direita.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        titulo.Foreground = Tema.Pincel(Tema.TextoForte);
        titulo.FontSize = 12;
        titulo.FontWeight = FontWeights.SemiBold;

        var faixa = new Border
        {
            Background = Tema.Pincel(Tema.Painel),
            BorderBrush = Tema.Pincel(Tema.Borda),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(16, 8, 16, 8),
            Child = titulo,
        };
        direita.Children.Add(faixa);

        rolagem.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        rolagem.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        rolagem.Content = conteudo;
        Grid.SetRow(rolagem, 1);
        direita.Children.Add(rolagem);

        Grid.SetColumn(direita, 1);
        raiz.Children.Add(direita);

        Content = raiz;

        Abrir(pagina ?? assuntos[0].Nome);
    }

    // =====================================================================
    //  A lista da esquerda
    // =====================================================================

    Border MontarLateral()
    {
        var coluna = new DockPanel { Margin = new Thickness(8, 10, 8, 10) };

        busca.Style = (Style)Application.Current.FindResource("CampoEscuro");
        busca.ToolTip = Idioma.T("Procurar uma configuração pelo nome");
        busca.TextChanged += (_, _) => Filtrar();

        // O texto-fantasma fica POR CIMA do campo, e nao dentro dele: um
        // TextBox com texto de verdade faria a busca comecar filtrando
        // por "Buscar" e a lista abriria vazia.
        var dica = new TextBlock
        {
            Text = Idioma.T("Buscar"),
            Foreground = Tema.Pincel(Tema.TextoFraco),
            FontSize = 12,
            IsHitTestVisible = false,
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        busca.TextChanged += (_, _) =>
            dica.Visibility = busca.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

        var comDica = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        comDica.Children.Add(busca);
        comDica.Children.Add(dica);
        DockPanel.SetDock(comDica, Dock.Top);
        coluna.Children.Add(comDica);

        var pilha = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = lista,
        };
        coluna.Children.Add(pilha);

        Preencher();

        return new Border
        {
            Background = Tema.Pincel(Tema.Painel),
            BorderBrush = Tema.Pincel(Tema.Borda),
            BorderThickness = new Thickness(0, 0, 1, 0),
            Child = coluna,
        };
    }

    void Preencher(string filtro = null)
    {
        lista.Children.Clear();

        string grupoAnterior = null;
        bool algum = false;

        foreach (var assunto in assuntos)
        {
            if (filtro is not null && !Bate(assunto, filtro)) continue;

            if (grupoAnterior is not null && assunto.Grupo != grupoAnterior)
                lista.Children.Add(new Border
                {
                    Height = 1,
                    Background = Tema.Pincel(Tema.Borda),
                    Margin = new Thickness(2, 7, 2, 7),
                });

            grupoAnterior = assunto.Grupo;
            algum = true;
            lista.Children.Add(BotaoDeAssunto(assunto));
        }

        if (!algum)
            lista.Children.Add(new TextBlock
            {
                Text = Idioma.T("nada com esse nome"),
                Foreground = Tema.Pincel(Tema.TextoFraco),
                FontSize = 11,
                Margin = new Thickness(6, 4, 0, 0),
                TextWrapping = TextWrapping.Wrap,
            });
    }

    static bool Bate(Assunto assunto, string filtro)
        => (assunto.Nome + " " + assunto.Palavras)
            .Contains(filtro, StringComparison.OrdinalIgnoreCase);

    Button BotaoDeAssunto(Assunto assunto)
    {
        bool atual = assunto.Nome == escolhido;

        var botao = new Button
        {
            Content = assunto.Nome,
            Tag = assunto.Nome,
            Cursor = Cursors.Hand,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(9, 5, 9, 5),
            Margin = new Thickness(0, 1, 0, 1),
            BorderThickness = new Thickness(0),
            FontSize = 12,
            Background = atual ? Tema.Pincel(Tema.Destaque) : Brushes.Transparent,
            Foreground = Tema.Pincel(atual ? Tema.TextoNoDestaque : Tema.Texto),
        };

        botao.Template = ModeloDoItem();
        botao.Click += (_, _) => Abrir(assunto.Nome);
        return botao;
    }

    /// <summary>
    /// O botao da lista sem o visual de botao do Windows: so um retangulo
    /// que acende. Com o template padrao, o item escolhido ficava com a
    /// borda cinza do sistema por cima da cor de destaque.
    /// </summary>
    static ControlTemplate ModeloDoItem()
    {
        var borda = new FrameworkElementFactory(typeof(Border));
        borda.SetBinding(Border.BackgroundProperty,
            new System.Windows.Data.Binding("Background")
            { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        borda.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
        borda.SetValue(Border.PaddingProperty, new Thickness(9, 5, 9, 5));

        var texto = new FrameworkElementFactory(typeof(ContentPresenter));
        borda.AppendChild(texto);

        return new ControlTemplate(typeof(Button)) { VisualTree = borda };
    }

    void Filtrar()
    {
        string filtro = busca.Text.Trim();
        Preencher(filtro.Length == 0 ? null : filtro);

        // Com a busca ativa, abrir o primeiro que sobrou poupa um clique.
        if (filtro.Length == 0) return;

        var primeiro = assuntos.FirstOrDefault(a => Bate(a, filtro));
        if (primeiro is not null && primeiro.Nome != escolhido) Abrir(primeiro.Nome);
    }

    void Abrir(string nome)
    {
        var assunto = assuntos.FirstOrDefault(a => a.Nome == nome);
        if (assunto is null) return;

        escolhido = nome;
        titulo.Text = nome;

        conteudo.Children.Clear();
        assunto.Montar(conteudo);

        rolagem.ScrollToTop();
        Preencher(busca.Text.Trim() is { Length: > 0 } f ? f : null);
    }

    // =====================================================================
    //  Os assuntos
    // =====================================================================

    /// <summary>
    /// As palavras de busca de um assunto: as do idioma em uso e as do portugues
    /// juntas -- quem usa o programa em ingles e procura "tema" tambem acha.
    /// </summary>
    static string Palavras(string original)
        => Idioma.T(original) is var traduzidas && traduzidas != original ? traduzidas + " " + original : original;

    void Registrar()
    {
        assuntos.Add(new("circuito", Idioma.T("Circuito"), Palavras(Idioma.N("grade fio traçado cor espessura grossura")), MontarCircuito));
        assuntos.Add(new("circuito", Idioma.T("Aparência"), Palavras(Idioma.N("tema claro escuro cores escala zoom interface")), MontarAparencia));
        assuntos.Add(new("circuito", Idioma.T("Tela e janela"),
            Palavras(Idioma.N("tamanho resolução monitor maximizado 1366 768")), MontarTela));

        assuntos.Add(new("codigo", Idioma.T("Editor de código"),
            Palavras(Idioma.N("fonte letra tamanho linha número recuo tab espaço parênteses chaves aspas zoom")), MontarEditor));
        assuntos.Add(new("codigo", Idioma.T("Monitor serial"),
            Palavras(Idioma.N("serial hora tempo linhas rolar fim de linha enviar enter limpar")), MontarMonitor));
        assuntos.Add(new("codigo", Idioma.T("Simulação"),
            Palavras(Idioma.N("rodar demo queimar queima som buzzer volume avisos selos aviso fechar ignorar")), MontarSimulacao));

        assuntos.Add(new("arquivos", Idioma.T("Arquivos e projetos"),
            Palavras(Idioma.N("salvar sozinho automático cópia backup bak recentes placa circuito novo")), MontarArquivos));

        assuntos.Add(new("entrada", Idioma.T("Atalhos do teclado"), Palavras(Idioma.N("teclas comandos")), MontarAtalhos));
        assuntos.Add(new("entrada", Idioma.T("Ao abrir o programa"), Palavras(Idioma.N("abertura splash reabrir último")), MontarAbertura));

        assuntos.Add(new("rede", Idioma.T("Rede e servidor"), Palavras(Idioma.N("internet wifi http servidor")), MontarRede));
        assuntos.Add(new("rede", Idioma.T("Bluetooth"), Palavras(Idioma.N("bt serial celular")), MontarBluetooth));

        assuntos.Add(new("fim", Idioma.T("Bibliotecas e plugins"),
            Palavras(Idioma.N("biblioteca include lib instalar língua tradução idioma")), MontarIdioma));
        assuntos.Add(new("fim", Idioma.T("Ajuda"), Palavras(Idioma.N("socorro atalhos manual padrão fábrica restaurar")), MontarAjuda));
    }

    // =====================================================================
    //  Configuracao guardada: le, mostra, grava e aplica
    // =====================================================================

    /// <summary>
    /// Uma caixa ligada a uma preferencia: marcar grava e ja aplica na
    /// janela principal.
    /// </summary>
    void CaixaGuardada(StackPanel painel, string rotulo, string chave, bool padrao)
        => Caixa(painel, rotulo, Preferencias.Ligado(chave, padrao), ligado =>
        {
            Preferencias.Definir(chave, ligado);
            ajustes.Aplicar?.Invoke();
        });

    /// <summary>
    /// Uma escolha entre opcoes fixas, ligada a uma preferencia. O valor da
    /// vez aparece ao lado do botao, com o nome que se le -- e nao o que se
    /// grava no arquivo.
    /// </summary>
    void EscolhaGuardada(StackPanel painel, string rotulo, string chave, string padrao,
                         IReadOnlyList<(string Valor, string Nome)> opcoes)
    {
        string atual = Preferencias.Texto(chave, padrao);
        var valor = Valor(opcoes.FirstOrDefault(o => o.Valor == atual).Nome
                          ?? opcoes.FirstOrDefault(o => o.Valor == padrao).Nome ?? atual);

        Escolha(painel, rotulo, valor, opcoes.Select(o => (o.Nome, (Action)(() =>
        {
            Preferencias.Definir(chave, o.Valor);
            valor.Text = o.Nome;
            ajustes.Aplicar?.Invoke();
        }))));
    }

    static (string, string)[] Numeros(params int[] valores)
        => [.. valores.Select(v => (v.ToString(), v.ToString()))];

    // =====================================================================
    //  Os assuntos
    // =====================================================================

    void MontarCircuito(StackPanel painel)
    {
        var grupo = Grupo(painel, Idioma.T("Fundo"));
        // Limpo e a grade desligada (o G liga e desliga); os outros sao o desenho dela.
        (string Valor, string Nome)[] fundos =
        [
            (EditorCircuito.FundoPontilhado, Idioma.T("Pontilhado")),
            (EditorCircuito.FundoGrade, Idioma.T("Grade")),
            (EditorCircuito.FundoQuadriculado, Idioma.T("Quadriculado")),
            (EditorCircuito.FundoLimpo, Idioma.T("Limpo")),
        ];
        string fundoAtual = !ajustes.LerGrade() ? EditorCircuito.FundoLimpo
            : Preferencias.Texto(Preferencias.EstiloDoFundo, EditorCircuito.FundoPontilhado);
        var nomeDoFundo = Valor(fundos.FirstOrDefault(f => f.Valor == fundoAtual).Nome ?? fundos[0].Nome);
        Escolha(grupo, Idioma.T("Fundo do circuito"), nomeDoFundo, fundos.Select(f => (f.Nome, (Action)(() =>
        {
            bool ligado = f.Valor != EditorCircuito.FundoLimpo;
            if (ligado) Preferencias.Definir(Preferencias.EstiloDoFundo, f.Valor);
            Preferencias.Definir(Preferencias.Grade, ligado);
            nomeDoFundo.Text = f.Nome;
            ajustes.Aplicar?.Invoke();
            ajustes.DefinirGrade(ligado);
        }))));
        Nota(grupo, Idioma.T("Os pontos e as linhas caem nos furos da protoboard (0,1\"). A tecla G liga e desliga."));

        var fios = Grupo(painel, Idioma.T("Fios"));

        var nomeDoTracado = Valor(NomeDoTracado(ajustes.LerEstiloDoFio()));
        Escolha(fios, Idioma.T("Traçado"), nomeDoTracado, Tracados.Select(t => (t.Nome, (Action)(() =>
        {
            ajustes.DefinirEstiloDoFio(t.Id);
            Preferencias.Definir(Preferencias.EstiloDoFio, t.Id);
            nomeDoTracado.Text = t.Nome;
        }))));
        Nota(fios, Idioma.T("Vale para os fios que já estão no circuito e para os próximos."));

        var nomeDaCor = Valor(NomeDaCor(ajustes.Cores, ajustes.LerCorDoFio()));
        Escolha(fios, Idioma.T("Cor do próximo fio"), nomeDaCor,
            ajustes.Cores.Select(c => (c.Nome, (Action)(() =>
            {
                ajustes.DefinirCorDoFio(c.Hex);
                nomeDaCor.Text = c.Nome;
            }))));

        EscolhaGuardada(fios, Idioma.T("Espessura"), Preferencias.EspessuraDoFio, "normal",
            [("fina", Idioma.T("Fina")), ("normal", Idioma.T("Normal")), ("grossa", Idioma.T("Grossa (para projetor)"))]);
    }

    /// <summary>
    /// Os temas como amostras, e nao como uma lista de nomes: com treze
    /// temas, "Laranja" num menu nao diz nada -- a amostra mostra o fundo,
    /// o painel, o destaque e a letra de cada um antes de escolher.
    /// </summary>
    void MontarAparencia(StackPanel painel)
    {
        foreach (var grupo in Tema.Paletas.GroupBy(p => p.Grupo))
        {
            var caixa = Grupo(painel, grupo.Key == Tema.GrupoBasicos
                ? Idioma.T("Tema") : grupo.Key == Tema.GrupoDasPlacas ? Idioma.T("Temas — das placas") : Idioma.T("Temas — cores"));

            var grade = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
            foreach (var paleta in grupo) grade.Children.Add(Amostra(paleta));
            caixa.Children.Add(grade);
        }

        Nota(painel, Idioma.T("Tema atual: {0}. Troca as cores do programa; o circuito continua igual.", Tema.NomeDe(Tema.Atual)));

        var tamanho = Grupo(painel, Idioma.T("Tamanho da interface"));
        EscolhaGuardada(tamanho, Idioma.T("Escala"), Preferencias.EscalaDaInterface, "100",
            [("75", "75%"), ("90", "90%"), ("100", Idioma.T("100% (de fábrica)")), ("110", "110%"),
             ("125", "125%"), ("150", "150%"), ("175", "175%")]);
        Nota(tamanho, Idioma.T("Deixa o programa inteiro maior ou menor: botões, letras e painéis. Maior ajuda no projetor e em tela de alta resolução; menor ajuda no notebook de 1366 × 768."));
    }

    /// <summary>Um cartao com as cores do tema, que aplica o tema no clique.</summary>
    Border Amostra(Tema.Paleta paleta)
    {
        bool atual = paleta.Id == Tema.Atual;

        static Brush De(Tema.Paleta p, string chave)
        {
            var pincel = new SolidColorBrush(p.Cores[chave]);
            pincel.Freeze();
            return pincel;
        }

        var desenho = new Grid();

        // A faixa de cima e o painel, com o destaque e duas linhas de
        // "texto" em cima do fundo: a janela do programa em miniatura.
        desenho.Children.Add(new Border
        {
            Height = 16,
            VerticalAlignment = VerticalAlignment.Top,
            Background = De(paleta, Tema.Painel),
            CornerRadius = new CornerRadius(5, 5, 0, 0),
        });
        desenho.Children.Add(new Border
        {
            Width = 24, Height = 9, CornerRadius = new CornerRadius(4.5),
            Background = De(paleta, Tema.Destaque),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(8, 24, 0, 0),
        });
        desenho.Children.Add(new Border
        {
            Width = 58, Height = 4, CornerRadius = new CornerRadius(2),
            Background = De(paleta, Tema.Texto),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(38, 26, 0, 0),
        });
        desenho.Children.Add(new Border
        {
            Width = 40, Height = 4, CornerRadius = new CornerRadius(2),
            Background = De(paleta, Tema.TextoFraco),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(38, 34, 0, 0),
        });
        desenho.Children.Add(new TextBlock
        {
            Text = paleta.Nome,
            Foreground = De(paleta, Tema.TextoForte),
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(8, 0, 0, 7),
        });

        if (atual)
            desenho.Children.Add(new Border
            {
                Width = 16, Height = 16, CornerRadius = new CornerRadius(8),
                Background = De(paleta, Tema.Destaque),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 7, 7),
                Child = new TextBlock
                {
                    Text = "✓",
                    FontSize = 10,
                    FontWeight = FontWeights.Bold,
                    Foreground = De(paleta, Tema.TextoNoDestaque),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            });

        var cartao = new Border
        {
            Width = 128,
            Height = 76,
            Margin = new Thickness(0, 0, 10, 10),
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(atual ? 2 : 1),
            BorderBrush = atual ? Tema.Pincel(Tema.Destaque) : Tema.Pincel(Tema.Borda),
            Background = De(paleta, Tema.Fundo),
            Cursor = Cursors.Hand,
            ToolTip = atual ? Idioma.T("{0} (em uso)", paleta.Nome) : Idioma.T("Usar o tema {0}", paleta.Nome),
            Tag = paleta.Id,
            Child = desenho,
        };
        cartao.MouseLeftButtonUp += (_, _) => Tema.Aplicar(paleta.Id);
        return cartao;
    }

    void MontarTela(StackPanel painel)
    {
        var area = SystemParameters.WorkArea;
        var grupo = Grupo(painel, Idioma.T("Tamanho da janela"));

        Nota(grupo, Idioma.T("Área útil desta tela: {0:0} × {1:0} pixels.", area.Width, area.Height));

        Caixa(grupo, Idioma.T("Lembrar o tamanho e a posição ao fechar"),
            Preferencias.Ligado(Preferencias.LembrarJanela, false),
            ligado => Preferencias.Definir(Preferencias.LembrarJanela, ligado));

        Caixa(grupo, Idioma.T("Abrir maximizado"),
            Preferencias.Ligado(Preferencias.AbrirMaximizado, false),
            ligado => Preferencias.Definir(Preferencias.AbrirMaximizado, ligado));

        if (ajustes.CaberNaTela is not null)
            Botao(grupo, Idioma.T("Ajustar a janela à tela agora"), () =>
            {
                ajustes.CaberNaTela();
                Close();
            });

        Nota(grupo,
            Idioma.T("Em tela de 1366 × 768 o programa já abre encolhido para caber. Se a janela tiver ficado maior que a tela por causa de um monitor trocado, este botão devolve ela para dentro."));
    }

    void MontarEditor(StackPanel painel)
    {
        var letra = Grupo(painel, Idioma.T("Letra"));

        // So as fontes de largura fixa que este computador tem: codigo em
        // letra proporcional desalinha tudo.
        var instaladas = Fonts.SystemFontFamilies.Select(f => f.Source).ToHashSet();
        var fontes = new[] { "Consolas", "Cascadia Mono", "Cascadia Code", "Courier New",
                             "Lucida Console", "JetBrains Mono", "Fira Code" }
                     .Where(instaladas.Contains).Select(f => (f, f)).ToList();
        if (fontes.Count == 0) fontes.Add(("Consolas", "Consolas"));
        EscolhaGuardada(letra, Idioma.T("Fonte"), Preferencias.FonteDoCodigo, "Consolas", fontes);

        EscolhaGuardada(letra, Idioma.T("Tamanho"), Preferencias.TamanhoDoCodigo, "12",
            Numeros(9, 10, 11, 12, 13, 14, 16, 18, 20, 24));
        CaixaGuardada(letra, Idioma.T("Aumentar e diminuir com Ctrl + roda do mouse"),
            Preferencias.ZoomComRoda, true);

        var aparece = Grupo(painel, Idioma.T("Como o código aparece"));
        CaixaGuardada(aparece, Idioma.T("Número das linhas"), Preferencias.NumerosDeLinha, true);
        CaixaGuardada(aparece, Idioma.T("Destacar a linha do cursor"), Preferencias.DestacarLinha, true);
        CaixaGuardada(aparece, Idioma.T("Quebrar as linhas compridas"), Preferencias.QuebrarLinhas, false);
        CaixaGuardada(aparece, Idioma.T("Mostrar espaços e tabulações"), Preferencias.MostrarEspacos, false);

        var digitando = Grupo(painel, Idioma.T("Digitando"));
        EscolhaGuardada(digitando, Idioma.T("Recuo"), Preferencias.TamanhoDoRecuo, "2",
            [("2", Idioma.T("2 espaços (o da Arduino IDE)")), ("4", Idioma.T("4 espaços")), ("8", Idioma.T("8 espaços"))]);
        CaixaGuardada(digitando, Idioma.T("A tecla Tab põe espaços"), Preferencias.TabViraEspacos, true);
        CaixaGuardada(digitando, Idioma.T("Fechar ( [ { e aspas sozinho"), Preferencias.FecharPares, true);
        Nota(digitando, Idioma.T("Como na Arduino IDE 2: abrir um parêntese já põe o que fecha, e o Enter entre as chaves desce a de fechar para a linha de baixo."));
    }

    void MontarMonitor(StackPanel painel)
    {
        var mostrar = Grupo(painel, Idioma.T("Mostrar"));
        CaixaGuardada(mostrar, Idioma.T("A hora de cada linha (em ms desde que a placa ligou)"),
            Preferencias.TempoNoSerial, true);
        CaixaGuardada(mostrar, Idioma.T("Rolar sozinho até a última linha"), Preferencias.RolarSerial, true);
        EscolhaGuardada(mostrar, Idioma.T("Linhas guardadas"), Preferencias.LinhasNoSerial, "400",
            Numeros(200, 400, 1000, 5000));
        EscolhaGuardada(mostrar, Idioma.T("Tamanho da letra"), Preferencias.TamanhoDoSerial, "11",
            Numeros(9, 10, 11, 12, 13, 14, 16));
        Nota(mostrar, Idioma.T("O monitor da Arduino IDE recebe todas as linhas; o limite é só o da tela."));

        var enviar = Grupo(painel, Idioma.T("Enviar"));
        EscolhaGuardada(enviar, Idioma.T("Fim de linha"), Preferencias.FimDeLinha, "lf",
            [("nada", Idioma.T("Nenhum")), ("lf", Idioma.T("Nova linha (\\n)")), ("cr", Idioma.T("Retorno de carro (\\r)")),
             ("crlf", Idioma.T("Os dois (\\r\\n)"))]);
        Nota(enviar, Idioma.T("O que vai depois do texto quando se aperta Enter, como a caixa do monitor "
                     + "da Arduino IDE. O readStringUntil('\\n') precisa da nova linha."));

        var rodar = Grupo(painel, Idioma.T("A cada Rodar"));
        CaixaGuardada(rodar, Idioma.T("Limpar o monitor"), Preferencias.LimparSerialAoRodar, true);
        CaixaGuardada(rodar, Idioma.T("Mostrar no monitor os avisos de ligação e de queima"),
            Preferencias.AvisosNoMonitor, true);
    }

    void MontarSimulacao(StackPanel painel)
    {
        var abrir = Grupo(painel, Idioma.T("Ao abrir"));
        CaixaGuardada(abrir, Idioma.T("Rodar o projeto assim que ele abre"), Preferencias.RodarAoAbrirProjeto, true);
        CaixaGuardada(abrir, Idioma.T("Rodar a demo assim que ela abre"), Preferencias.RodarAoAbrirDemo, false);

        var pecas = Grupo(painel, Idioma.T("Componentes"));
        CaixaGuardada(pecas, Idioma.T("Queimar o componente ligado errado"), Preferencias.SimularQueima, true);
        Nota(pecas, Idioma.T("Desligado, o LED sem resistor e o curto não queimam aqui — mas queimam na bancada. É bom só para montar rápido; para aprender, deixe ligado."));

        CaixaGuardada(pecas, Idioma.T("Mostrar os avisos em cima das peças"), Preferencias.SelosDeAviso, true);
        Nota(pecas, Idioma.T("O selo amarelo (ou vermelho, no que queimou) em cima da peça ou do pino, com o motivo na dica do mouse. O X de cada selo fecha aquele aviso, e o projeto guarda isso: reaberto, ele continua fechado."));
        if (ajustes.EsquecerAvisosFechados is not null)
            Botao(pecas, Idioma.T("Mostrar de novo os avisos fechados deste projeto"), () => ajustes.EsquecerAvisosFechados());

        var som = Grupo(painel, Idioma.T("Som"));
        CaixaGuardada(som, Idioma.T("Som do buzzer"), Preferencias.SomDoBuzzer, true);
        EscolhaGuardada(som, Idioma.T("Volume"), Preferencias.VolumeDoBuzzer, "medio",
            [("baixo", Idioma.T("Baixo")), ("medio", Idioma.T("Médio")), ("alto", Idioma.T("Alto"))]);
    }

    void MontarArquivos(StackPanel painel)
    {
        var salvar = Grupo(painel, Idioma.T("Salvar"));
        EscolhaGuardada(salvar, Idioma.T("Salvar sozinho"), Preferencias.SalvarSozinho, "0",
            [("0", Idioma.T("Nunca")), ("1", Idioma.T("A cada minuto")), ("2", Idioma.T("A cada 2 minutos")),
             ("5", Idioma.T("A cada 5 minutos")), ("10", Idioma.T("A cada 10 minutos"))]);
        Nota(salvar, Idioma.T("Só o projeto que já tem arquivo, e só quando mudou. Circuito novo e demo precisam do primeiro Ctrl+S, que pergunta onde guardar."));
        CaixaGuardada(salvar, Idioma.T("Guardar a versão anterior como .bak ao salvar"),
            Preferencias.CopiaDeSeguranca, false);

        var recentes = Grupo(painel, Idioma.T("Projetos recentes"));
        EscolhaGuardada(recentes, Idioma.T("Quantos na lista"), Preferencias.QuantosRecentes, "8",
            Numeros(5, 8, 12, 20, 30));

        var novo = Grupo(painel, Idioma.T("Circuito novo"));
        EscolhaGuardada(novo, Idioma.T("Começa com a placa"), Preferencias.PlacaDoCircuitoNovo, "",
            [("", Idioma.T("Nenhuma (escolher na paleta)")), .. ajustes.Placas.Select(p => (p.Id, p.Nome))]);
        Nota(novo, Idioma.T("O \"Novo projeto\" do menu. A tela de abertura continua oferecendo todas."));

        // O "author" do diagram.json, como no Wokwi. Vazio: o usuario do Windows.
        var autoria = Grupo(painel, Idioma.T("Autor dos projetos"));
        var campo = new TextBox
        {
            Text = Preferencias.Texto(Preferencias.Autor, ""),
            Width = 260,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 4, 0, 2),
            Style = (Style)FindResource("CampoEscuro"),
        };
        void Guardar() => Preferencias.Definir(Preferencias.Autor, campo.Text.Trim());
        campo.LostFocus += (_, _) => Guardar();
        campo.KeyDown += (_, ev) => { if (ev.Key == Key.Enter) Guardar(); };
        autoria.Children.Add(campo);
        Nota(autoria, Idioma.T("O \"author\" do diagram.json dos projetos novos (o Wokwi grava o mesmo campo). Vazio, vai o usuário do Windows ({0}). Cada projeto pode ter o seu: na aba DESCRIÇÃO (ou na linha \"author\" do diagram.json).", Environment.UserName));
    }

    void MontarAtalhos(StackPanel painel)
    {
        var grupo = Grupo(painel, Idioma.T("Teclas do programa"));
        Nota(grupo, Idioma.T("Rodar, salvar, girar, apagar e o resto. Durante a simulação o teclado é do circuito."));
        Botao(grupo, Idioma.T("Ver e trocar atalhos"), () => new JanelaAtalhos(this).ShowDialog());
    }

    void MontarAbertura(StackPanel painel)
    {
        var grupo = Grupo(painel, Idioma.T("Tela de abertura"));
        Caixa(grupo, Idioma.T("Mostrar a tela de abertura"),
            Preferencias.Ligado(Preferencias.MostrarAbertura, true),
            ligado => Preferencias.Definir(Preferencias.MostrarAbertura, ligado));
        Nota(grupo, Idioma.T("A tela com as placas e os projetos recentes, ao abrir o programa."));

        var ultimo = Grupo(painel, Idioma.T("Último projeto"));
        CaixaGuardada(ultimo, Idioma.T("Reabrir o último projeto no lugar da tela de abertura"),
            Preferencias.ReabrirUltimo, false);
        Nota(ultimo, Idioma.T("Para quem continua sempre de onde parou. Se o arquivo tiver sido apagado ou movido, volta a tela de abertura."));
    }

    void MontarRede(StackPanel painel)
    {
        var grupo = Grupo(painel, Idioma.T("Internet"));
        Caixa(grupo, Idioma.T("Usar a internet deste computador"), ajustes.LerInternet(), ligado =>
        {
            ajustes.DefinirInternet(ligado);
            Preferencias.Definir(Preferencias.UsarInternet, ligado);
        });
        Nota(grupo, Idioma.T("Desligada, os pedidos falham como numa placa sem rede — e o painel diz por quê."));

        var radio = Grupo(painel, Idioma.T("WiFi deste computador"));
        Caixa(radio, Idioma.T("Procurar redes com o WiFi da máquina"),
            Preferencias.UsarRadioDoComputador,
            ligado =>
            {
                Preferencias.Definir(Preferencias.UsarRadioDoPc, ligado);
                RadioDoComputador.Desligado = !ligado;
                RadioDoComputador.Esquecer();
            });
        Nota(radio, Idioma.T("Ligado, o WiFi.scanNetworks() do sketch lista as redes que este computador enxerga de verdade. A placa simulada não tem rádio: ela vê o que a máquina vê, e a conexão continua sendo simulada. Desligado, voltam as redes de mentira."));

        var navegador = Grupo(painel, Idioma.T("Servidor da placa"));
        Caixa(navegador, Idioma.T("Abrir a página no navegador quando a placa subir um servidor"),
            Preferencias.AbrirServidorNoNavegador,
            ligado => Preferencias.Definir(Preferencias.AbrirServidor, ligado));
        Nota(navegador, Idioma.T("É o que acontece com uma placa de verdade na rede: ela anuncia o endereço e você abre. Aqui o endereço é um localhost, porque a placa é simulada — mas a página é a que o sketch serve."));

        var servidores = Grupo(painel, Idioma.T("Servidores agora"));
        Nota(servidores, ajustes.LerServidores());
        Botao(servidores, Idioma.T("Abrir o painel Rede"), () => { ajustes.AbrirPainelDeRede(); Close(); });
    }

    void MontarBluetooth(StackPanel painel)
    {
        var grupo = Grupo(painel, Idioma.T("Dispositivos"));
        Nota(grupo, ajustes.LerBluetooth());
        Botao(grupo, Idioma.T("Abrir o painel Bluetooth"),
            () => { ajustes.AbrirPainelDeBluetooth(); Close(); });

        var simulado = Grupo(painel, Idioma.T("Celular simulado"));
        CaixaGuardada(simulado, Idioma.T("Abrir o celular sozinho quando uma placa ligar o Bluetooth"),
                      Preferencias.AbrirCelularSozinho, true);
        Nota(simulado, Idioma.T("Ele abre uma vez por rodada, quando o sketch chama SerialBT.begin() ou começa a anunciar pelo BLE. Desligado, o celular só abre pelo botão do painel Bluetooth."));

        var celular = Grupo(painel, Idioma.T("Celular de verdade"));
        Nota(celular, Idioma.T("Com o Bluetooth do computador ligado, a placa que chama SerialBT.begin(\"nome\") aparece no rádio do computador como o serviço \"CircuitSketch nome\". No celular: pareie com este computador (nas configurações de Bluetooth do Windows), abra o app de terminal Bluetooth (no Android, o Serial Bluetooth Terminal) e conecte no computador. O que o celular manda chega no SerialBT.read() do sketch."));
        CaixaGuardada(celular, Idioma.T("Usar o Bluetooth deste computador para o celular de verdade"),
                      Preferencias.BluetoothDoComputador, true);
        if (ServicoBluetoothDoPc.Motivo is { } motivo)
            Nota(celular, Idioma.T("Na última vez: {0}.", motivo));

        var doPc = Grupo(painel, Idioma.T("Bluetooth deste computador"));
        Nota(doPc, RadioDoComputador.Desligado
            ? Idioma.T("A consulta ao computador está desligada em WiFi deste computador.")
            : RadioDoComputador.Bluetooths().Count > 0
                ? Idioma.T("Aparelhos que a máquina conhece: ")
                  + string.Join(", ", RadioDoComputador.Bluetooths())
                : Idioma.T("Não foi possível usar o Bluetooth deste computador: {0}. As conexões entre a placa e o celular continuam simuladas.", (RadioDoComputador.MotivoDoBluetooth ?? Idioma.T("motivo desconhecido"))));
    }

    /// <summary>O nome do idioma na lista: "English", ou "English — 62%" enquanto a traducao anda.</summary>
    internal static string NomeDoIdioma(string codigo, string nome)
    {
        double feito = Idioma.Progresso(codigo);
        return feito >= 1 ? nome : $"{nome} — {Math.Floor(feito * 100):0}%";
    }

    void MontarIdioma(StackPanel painel)
    {
        var grupo = Grupo(painel, Idioma.T("Idioma"));

        // Todos os idiomas com catalogo; o que ainda nao esta completo vem com
        // a porcentagem traduzida ao lado do nome ("English — 62%"), para
        // ninguem escolher achando que a tela inteira vai mudar.
        var idiomas = Idioma.Disponiveis();
        EscolhaGuardada(grupo, Idioma.T("Idioma da interface"), Preferencias.Idioma, Idioma.Principal,
            [.. idiomas.Select(i => (i.Codigo, NomeDoIdioma(i.Codigo, i.Nome)))]);
        Nota(grupo, Idioma.T("O idioma novo vale ao reabrir o CircuitSketch."));
        if (idiomas.Any(i => Idioma.Progresso(i.Codigo) < 1))
            Nota(grupo, Idioma.T("Idioma com porcentagem ainda está sendo traduzido: o que falta aparece em português."));

        var bibliotecas = Grupo(painel, Idioma.T("Bibliotecas"));
        Nota(bibliotecas, Idioma.T("Quais o simulador executa, quais o seu sketch pede, e instalar uma de fora (a pasta ou o .zip) — o simulador roda o código C++ dela."));
        if (ajustes.AbrirBibliotecas is not null)
            Botao(bibliotecas, Idioma.T("Abrir o gerenciador de bibliotecas"),
                () => { ajustes.AbrirBibliotecas(); Close(); });

        var plugins = Grupo(painel, Idioma.T("Plugins"));
        Nota(plugins, Idioma.T("Cada plugin é uma pasta com arquivos .cs dentro de \"plugins\", ao lado do programa. Ele roda código no seu computador: só coloque plugins de quem você confia."));
        MontarPlugins(plugins);
        Botao(plugins, Idioma.T("Abrir a pasta de plugins"), () =>
        {
            try
            {
                Directory.CreateDirectory(Plugins.Pasta);
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{Plugins.Pasta}\"") { UseShellExecute = true });
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Plugins"); }
        });
        Nota(plugins, Idioma.T("O carregamento de componentes e microcontroladores de terceiros entra depois que o programa estiver fechado."));
    }

    /// <summary>
    /// A lista dos plugins, como a dos add-ons do Blender: a busca e a categoria em cima,
    /// e um bloco por plugin -- a caixa de ligar, "Categoria: Nome", e aberto, a descricao,
    /// a versao, quando foi instalado, o autor e os arquivos.
    /// </summary>
    void MontarPlugins(StackPanel painel)
    {
        Plugins.RegistrarInstalados();
        if (Plugins.Todos.Count == 0)
        {
            Nota(painel, Idioma.T("Nenhum plugin na pasta."));
            return;
        }

        string categoria = null;     // null: todas
        var procura = new TextBox
        {
            Style = (Style)Application.Current.FindResource("CampoEscuro"),
            Width = 220,
            Margin = new Thickness(0, 0, 10, 0),
            ToolTip = Idioma.T("Procurar pelo nome, pela descrição ou pelo autor"),
        };
        var valorDaCategoria = Valor(Idioma.T("Todas"));
        var blocos = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };

        var filtros = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 9, 0, 0) };
        filtros.Children.Add(procura);
        var linhaDaCategoria = new StackPanel();
        Escolha(linhaDaCategoria, Idioma.T("Categoria"), valorDaCategoria,
            new (string, Action)[] { (Idioma.T("Todas"), () => { categoria = null; Refazer(); }) }
                .Concat(Plugins.Categorias.Select(c => (Idioma.T(c), (Action)(() => { categoria = c; Refazer(); })))));
        if (linhaDaCategoria.Children.Count > 0 && linhaDaCategoria.Children[0] is FrameworkElement escolha)
        {
            linhaDaCategoria.Children.Clear();
            escolha.Margin = new Thickness(0);
            filtros.Children.Add(escolha);
        }
        painel.Children.Add(filtros);
        painel.Children.Add(blocos);
        procura.TextChanged += (_, _) => Refazer();
        Refazer();

        void Refazer()
        {
            valorDaCategoria.Text = categoria is null ? Idioma.T("Todas") : Idioma.T(categoria);
            blocos.Children.Clear();
            string termo = procura.Text.Trim();
            var vistos = Plugins.Todos
                .Where(p => categoria is null || p.Categoria == categoria)
                .Where(p => termo.Length == 0 || new[] { p.Nome, p.Descricao, p.Autor, p.Categoria, Idioma.T(p.Descricao), Idioma.T(p.Categoria) }
                    .Any(t => t?.Contains(termo, StringComparison.CurrentCultureIgnoreCase) == true))
                .ToList();
            if (vistos.Count == 0) Nota(blocos, Idioma.T("Nenhum plugin com esse filtro."));
            foreach (var plugin in vistos) blocos.Children.Add(BlocoDoPlugin(plugin));
        }
    }

    Border BlocoDoPlugin(Plugins.Plugin plugin)
    {
        var corpo = new Grid { Margin = new Thickness(22, 8, 0, 0), Visibility = Visibility.Collapsed };
        corpo.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        corpo.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        void Linha(string rotulo, string texto, string cor = Tema.Texto)
        {
            if (string.IsNullOrEmpty(texto)) return;
            int n = corpo.RowDefinitions.Count;
            corpo.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var r = new TextBlock
            {
                Text = rotulo, Foreground = Tema.Pincel(Tema.TextoFraco), FontSize = 11,
                Margin = new Thickness(0, 3, 12, 0), HorizontalAlignment = HorizontalAlignment.Right,
            };
            var v = new TextBlock
            {
                Text = texto, Foreground = Tema.Pincel(cor), FontSize = 11,
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0),
            };
            Grid.SetRow(r, n); Grid.SetRow(v, n); Grid.SetColumn(v, 1);
            corpo.Children.Add(r);
            corpo.Children.Add(v);
        }

        Linha(Idioma.T("Descrição:"), Idioma.T(plugin.Descricao));
        Linha(Idioma.T("Versão:"), plugin.Versao);
        if (plugin.Instalado is { } instalado)
            Linha(Idioma.T("Instalado:"), Idioma.T("na {0}, em {1}", instalado.Programa, instalado.Dia)
                                + (instalado.Versao == plugin.Versao || instalado.Versao.Length == 0 ? "" : Idioma.T(" (com a versão {0})", instalado.Versao)));
        Linha(Idioma.T("Autor:"), plugin.Autor);
        Linha(Idioma.T("Arquivo:"), string.Join(", ", plugin.Arquivos.Select(a => Path.Combine("plugins", a))));
        Linha(Idioma.T("Erro:"), plugin.Erro, Tema.Erro);

        var seta = new TextBlock
        {
            Text = "▸", Foreground = Tema.Pincel(Tema.TextoFraco), FontSize = 10,
            Width = 14, VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.Hand,
        };
        var caixa = new CheckBox
        {
            IsChecked = plugin.Ligado,
            IsEnabled = plugin.Erro is null,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
            ToolTip = plugin.Erro is null ? Idioma.T("Ligar ou desligar o plugin") : Idioma.T("Não carregou: veja o erro"),
        };
        string id = plugin.Id;
        caixa.Checked += (_, _) => Plugins.Ligar(id, true);
        caixa.Unchecked += (_, _) => Plugins.Ligar(id, false);
        var nome = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.Hand, FontSize = 12 };
        nome.Inlines.Add(new System.Windows.Documents.Run(Idioma.T(plugin.Categoria) + ": ") { Foreground = Tema.Pincel(Tema.TextoFraco) });
        nome.Inlines.Add(new System.Windows.Documents.Run(plugin.Nome)
        {
            Foreground = Tema.Pincel(plugin.Erro is null ? Tema.TextoForte : Tema.Erro), FontWeight = FontWeights.SemiBold,
        });

        var cabeca = new StackPanel { Orientation = Orientation.Horizontal, Background = Brushes.Transparent };
        cabeca.Children.Add(seta);
        cabeca.Children.Add(caixa);
        cabeca.Children.Add(nome);
        void Abrir(object s, MouseButtonEventArgs e)
        {
            bool aberto = corpo.Visibility == Visibility.Visible;
            corpo.Visibility = aberto ? Visibility.Collapsed : Visibility.Visible;
            seta.Text = aberto ? "▸" : "▾";
        }
        seta.MouseLeftButtonDown += Abrir;
        nome.MouseLeftButtonDown += Abrir;

        var dentro = new StackPanel();
        dentro.Children.Add(cabeca);
        dentro.Children.Add(corpo);
        return new Border
        {
            Style = (Style)Application.Current.FindResource("Cartao"),
            Padding = new Thickness(10, 7, 10, 7),
            Margin = new Thickness(0, 0, 0, 6),
            Tag = "plugin:" + plugin.Id,
            Child = dentro,
        };
    }

    void MontarAjuda(StackPanel painel)
    {
        var grupo = Grupo(painel, Idioma.T("Manual"));
        Nota(grupo, Idioma.T("A lista do que dá para fazer no circuito, no código e no teclado."));
        Botao(grupo, Idioma.T("Abrir a ajuda"), () => ajustes.AbrirAjuda());

        var fabrica = Grupo(painel, Idioma.T("Configurações de fábrica"));
        Nota(fabrica, Idioma.T("Volta todas as configurações ao que o programa traz de fábrica: tema, editor, monitor, simulação, arquivos e janela. Os projetos e a lista de recentes ficam como estão."));
        Botao(fabrica, Idioma.T("Voltar tudo ao padrão"), () =>
        {
            var r = MessageBox.Show(this, Idioma.T("Voltar todas as configurações ao padrão de fábrica?"),
                Idioma.T("Configurações de fábrica"), MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (r != MessageBoxResult.Yes) return;
            RestaurarPadroes();
        });
    }

    /// <summary>
    /// Tudo de volta ao de fabrica, aplicado na hora -- e esta janela
    /// remontada, porque cada caixa mostrava o valor de antes.
    /// </summary>
    internal void RestaurarPadroes()
    {
        Preferencias.Restaurar();
        Tema.AplicarSalvo();
        ajustes.Aplicar?.Invoke();
        MontarJanela(escolhido);
    }

    // =====================================================================
    //  Peças da tela
    // =====================================================================

    static string NomeDoTracado(string id)
        => Tracados.FirstOrDefault(t => t.Id == id).Nome ?? Idioma.T("Solto (curva)");

    static string NomeDaCor((string Nome, string Hex)[] cores, string hex)
        => cores.FirstOrDefault(c => c.Hex == hex).Nome ?? hex;

    /// <summary>Um bloco que abre e fecha, como os do Blender.</summary>
    StackPanel Grupo(StackPanel painel, string titulo)
    {
        var dentro = new StackPanel { Margin = new Thickness(14, 6, 0, 2) };

        var seta = new TextBlock
        {
            Text = "▾",
            Foreground = Tema.Pincel(Tema.TextoFraco),
            FontSize = 10,
            Margin = new Thickness(0, 0, 7, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };

        var cabeca = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Cursor = Cursors.Hand,
            Background = Brushes.Transparent,
            Margin = new Thickness(0, 10, 0, 0),
        };
        cabeca.Children.Add(seta);
        cabeca.Children.Add(new TextBlock
        {
            Text = titulo,
            Foreground = Tema.Pincel(Tema.TextoForte),
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        });

        cabeca.MouseLeftButtonDown += (_, _) =>
        {
            bool aberto = dentro.Visibility == Visibility.Visible;
            dentro.Visibility = aberto ? Visibility.Collapsed : Visibility.Visible;
            seta.Text = aberto ? "▸" : "▾";
        };

        painel.Children.Add(cabeca);
        painel.Children.Add(dentro);
        return dentro;
    }

    static void Nota(StackPanel painel, string texto) => painel.Children.Add(new TextBlock
    {
        Text = texto,
        Foreground = Tema.Pincel(Tema.TextoFraco),
        FontSize = 11,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 4, 0, 0),
    });

    static TextBlock Valor(string texto) => new()
    {
        Text = texto,
        Foreground = Tema.Pincel(Tema.Destaque),
        FontSize = 12,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(8, 0, 0, 0),
    };

    static void Caixa(StackPanel painel, string rotulo, bool marcada, Action<bool> aoMudar)
    {
        var caixa = new CheckBox
        {
            Content = rotulo,
            IsChecked = marcada,
            Foreground = Tema.Pincel(Tema.Texto),
            FontSize = 12,
            Margin = new Thickness(0, 6, 0, 0),
        };
        caixa.Checked += (_, _) => aoMudar(true);
        caixa.Unchecked += (_, _) => aoMudar(false);
        painel.Children.Add(caixa);
    }

    void Escolha(StackPanel painel, string rotulo, TextBlock valor,
                 IEnumerable<(string Nome, Action Fazer)> itens)
    {
        var botao = new Button
        {
            Content = rotulo + " ▾",
            Style = (Style)FindResource("BotaoPequeno"),
            HorizontalAlignment = HorizontalAlignment.Left,
        };

        var lista = itens.ToList();
        botao.Click += (_, _) =>
        {
            var menu = new ContextMenu { PlacementTarget = botao };
            foreach (var (nome, fazer) in lista)
            {
                var item = new MenuItem { Header = nome };
                item.Click += (_, _) => fazer();
                menu.Items.Add(item);
            }
            menu.IsOpen = true;
        };

        var linha = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 7, 0, 0),
        };
        linha.Children.Add(botao);
        linha.Children.Add(valor);
        painel.Children.Add(linha);
    }

    void Botao(StackPanel painel, string texto, Action aoClicar)
    {
        var botao = new Button
        {
            Content = texto,
            Style = (Style)FindResource("BotaoPequeno"),
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 9, 0, 0),
        };
        botao.Click += (_, _) => aoClicar();
        painel.Children.Add(botao);
    }

    /// <summary>Para o teste: os assuntos da lista da esquerda.</summary>
    internal List<string> AssuntosParaTeste => [.. assuntos.Select(a => a.Nome)];

    /// <summary>Para o teste: qual assunto esta aberto agora.</summary>
    internal string AssuntoAbertoParaTeste => escolhido;

    /// <summary>Para o teste: os ids dos temas que a pagina Aparencia mostra.</summary>
    internal List<string> TemasNaTelaParaTeste
        => [.. Descendentes(conteudo).OfType<Border>()
                .Where(b => b.Tag is string id && Tema.De(id) is not null)
                .Select(b => (string)b.Tag)];

    static IEnumerable<DependencyObject> Descendentes(DependencyObject raiz)
    {
        foreach (var filho in LogicalTreeHelper.GetChildren(raiz).OfType<DependencyObject>())
        {
            yield return filho;
            foreach (var neto in Descendentes(filho)) yield return neto;
        }
    }

    /// <summary>Para o passeio: abre um assunto como o clique na lista da esquerda.</summary>
    internal void AbrirParaTeste(string nome) => Abrir(nome);

    /// <summary>Para o teste: digitar na busca.</summary>
    internal void BuscarParaTeste(string texto) => busca.Text = texto;

    /// <summary>Para o teste: quantos itens sobraram na lista.</summary>
    internal int ItensDaListaParaTeste => lista.Children.OfType<Button>().Count();
}
