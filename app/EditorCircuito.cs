using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace Simulador.App;

/// <summary>
/// Canvas de edicao: arrastar pecas, girar, ligar e selecionar fios,
/// zoom e pan.
///
/// A deteccao de pino e de fio e MATEMATICA, nao por elemento visual:
/// uma protoboard de 830 tem ~1200 furos e criar um Ellipse para cada um
/// mataria o desempenho. Varrer a lista a cada movimento custa nada perto
/// disso.
/// </summary>
public partial class EditorCircuito : Border
{
    public Circuito Atual { get; private set; } = new();

    readonly Canvas mundo = new();
    readonly ScaleTransform escala = new(1, 1);
    readonly TranslateTransform deslocamento = new(0, 0);

    readonly Dictionary<string, FrameworkElement> visuais = [];
    readonly Dictionary<string, Image> parteMovel = [];
    readonly Dictionary<string, Ellipse> brilhos = [];

    // ================= peça queimada =================
    //
    // Estado de execução, não do circuito: não vai para o .sktch. Quem
    // queimou continua queimado até rodar de novo, e aí ou o erro foi
    // corrigido ou queima na hora outra vez.

    readonly Dictionary<string, TextBlock> estouros = [];

    /// <summary>Ids das peças queimadas nesta execução.</summary>
    public IReadOnlyCollection<string> Queimadas => estouros.Keys;

    public bool EstaQueimada(string id) => estouros.ContainsKey(id);

    /// <summary>Põe o 💥 em cima da peça. Repetir não faz nada.</summary>
    public void Queimar(string id)
    {
        if (estouros.ContainsKey(id)) return;

        var peca = Atual.Achar(id);
        var def = peca is null ? null : Catalogo.Obter(peca.Tipo);
        if (def is null) return;

        var marca = new TextBlock
        {
            Text = "💥",
            // Sem isto o WPF cai numa fonte de simbolo e o emoji sai como
            // contorno preto e branco. A colorida e a "Segoe UI Emoji".
            FontFamily = new FontFamily("Segoe UI Emoji"),
            FontSize = Math.Clamp(Math.Min(def.Largura, def.Altura) * 0.9, 18, 48),
            IsHitTestVisible = false,
            // Fundo escuro por baixo para o emoji aparecer em cima de
            // peça clara -- na protoboard branca ele sumia.
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Colors.Black, BlurRadius = 8, ShadowDepth = 0, Opacity = 0.9
            }
        };

        Panel.SetZIndex(marca, 700);
        mundo.Children.Add(marca);
        estouros[id] = marca;
        PosicionarEstouro(peca, def);
    }

    public void ApagarQueimaduras()
    {
        foreach (var marca in estouros.Values) mundo.Children.Remove(marca);
        estouros.Clear();
        queimadasPelaAlimentacao.Clear();
        EsfriarReguladores();
        memoriaDaEnergia.Esquecer();
    }

    void PosicionarEstouro(PecaNoCircuito peca, PecaDef def)
    {
        if (!estouros.TryGetValue(peca.Id, out var marca)) return;

        marca.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var centro = Geometria.PosicaoLocal(peca, def, def.Largura / 2, def.Altura / 2);
        Canvas.SetLeft(marca, centro.X - marca.DesiredSize.Width / 2);
        Canvas.SetTop(marca, centro.Y - marca.DesiredSize.Height / 2);
    }
    readonly Dictionary<string, TextBlock> rotulosDePeca = [];
    readonly Dictionary<string, TextBlock[,]> telasLcd = [];
    readonly Dictionary<string, Rectangle> bracosServo = [];

    /// <summary>
    /// Grade de caracteres de cada display, ja em coordenadas da CAIXA da
    /// peca. Vem do pattern "characters" do proprio SVG, convertido pela
    /// escala do encaixe proporcional.
    /// </summary>
    record GradeLcd(int Colunas, int Linhas, double X, double Y,
                    double Larg, double Alt, double Fonte);

    // As duas telas tem o MESMO passo em milimetros, entao a grade em
    // pixels e a mesma; muda so quantas colunas e linhas cabem.
    // Do pattern "characters": 3,55 x 5,95 em (12,45 / 12,55) no viewBox,
    // vezes a escala 3,775 do encaixe proporcional.
    static readonly Dictionary<string, GradeLcd> Grades = new()
    {
        ["lcd1602"] = new(16, 2, 47.0, 47.4, 13.40, 22.46, 18.6),
        ["lcd2004"] = new(20, 4, 47.0, 47.7, 13.40, 22.46, 18.6),

        // A versao de I2C e a MESMA placa: muda so o conector. A tela
        // fica no mesmo lugar, entao a grade e a mesma.
        ["lcd1602-i2c"] = new(16, 2, 47.0, 47.4, 13.40, 22.46, 18.6),
        ["lcd2004-i2c"] = new(20, 4, 47.0, 47.7, 13.40, 22.46, 18.6),
    };
    readonly List<Path> visuaisFios = [];

    readonly Ellipse marcadorPino = new();
    readonly Border rotuloPino = new();
    readonly TextBlock textoPino = new();
    readonly Path fioEmCurso = new();

    public string CorDoFio { get; set; } = "#00A651";

    public bool VisaoDeEnergia
    {
        get => visaoDeEnergia;
        set
        {
            if (visaoDeEnergia == value) return;
            visaoDeEnergia = value;
            energia = null;
            proximaConta = DateTime.MinValue;
            if (!value) RestaurarCores();
            RedesenharFios();
        }
    }

    bool visaoDeEnergia;
    Energia energia;
    DateTime proximaConta = DateTime.MinValue;
    Netlist redeAtual;

    readonly Dictionary<string, ImageSource> fontesOriginais = [];

    readonly Dictionary<NivelDeEnergia, Path> marcasDeEnergia = new()
    {
        [NivelDeEnergia.Demais] = new Path(),
        [NivelDeEnergia.Aceitavel] = new Path(),
        [NivelDeEnergia.Pouca] = new Path(),
    };

    public Energia EnergiaAtual => energia;

    internal bool EstaEmCinzaParaTeste(string idDaPeca)
        => visuais.TryGetValue(idDaPeca, out var v) && v is Image img
           && img.Source is System.Windows.Media.Imaging.FormatConvertedBitmap;

    static readonly Dictionary<NivelDeEnergia, Color> CoresDaEnergia = new()
    {
        [NivelDeEnergia.Demais] = Color.FromRgb(0xFF, 0x52, 0x52),
        [NivelDeEnergia.Aceitavel] = Color.FromRgb(0xFF, 0xD5, 0x4F),
        [NivelDeEnergia.Pouca] = Color.FromRgb(0x4F, 0xC3, 0xF7),
        [NivelDeEnergia.Nenhuma] = Color.FromRgb(0x4A, 0x4A, 0x50),
    };

    void RestaurarCores()
    {
        foreach (var (id, visual) in visuais)
            if (visual is Image img && fontesOriginais.TryGetValue(id, out var fonte))
                img.Source = fonte;

        fontesOriginais.Clear();
        foreach (var marca in marcasDeEnergia.Values) marca.Data = null;
    }

    /// <summary>
    /// A análise de energia acabou de ser refeita.
    ///
    /// Quem mostra o resumo escuta isto. Sem o aviso, o resumo era lido
    /// UMA vez, no instante em que o modo abria -- e nesse instante a
    /// análise tinha acabado de ser zerada, então a barra dizia "rode a
    /// simulação" com a simulação rodando, e ficava dizendo.
    /// </summary>
    public event Action EnergiaMudou;

    public string ResumoDaEnergia()
    {
        // Rodando mas ainda sem a primeira medida: ela sai em menos de um
        // quarto de segundo. Mandar "rode a simulação" aqui era o erro.
        if (energia is null && EmSimulacao)
            return "visão de circuito: medindo por onde a corrente passa…";

        if (energia is null)
            return "visão de circuito: rode a simulação para ver por onde a corrente passa.";

        int demais = 0, ok = 0, pouca = 0;
        foreach (var peca in Atual.Pecas)
        {
            switch (energia.NivelDaPeca(peca.Id))
            {
                case NivelDeEnergia.Demais: demais++; break;
                case NivelDeEnergia.Aceitavel: ok++; break;
                case NivelDeEnergia.Pouca: pouca++; break;
            }
        }

        return $"visão de circuito · amarelo: corrente aceitável ({ok}) · "
               + $"vermelho: corrente demais ({demais}) · azul: corrente de menos ({pouca})";
    }

    void AtualizarEnergia(Bancada bancada, Netlist rede)
    {
        redeAtual = rede;

        if (!visaoDeEnergia)
        {
            if (fontesOriginais.Count > 0) RestaurarCores();
            return;
        }

        if (DateTime.UtcNow >= proximaConta)
        {
            proximaConta = DateTime.UtcNow.AddMilliseconds(220);
            energia = CalcularEnergia(bancada, rede);
            RedesenharFios();
            EnergiaMudou?.Invoke();
        }

        var grupos = new Dictionary<NivelDeEnergia, GeometryGroup>
        {
            [NivelDeEnergia.Demais] = new GeometryGroup(),
            [NivelDeEnergia.Aceitavel] = new GeometryGroup(),
            [NivelDeEnergia.Pouca] = new GeometryGroup(),
        };

        foreach (var peca in Atual.Pecas)
        {
            var def = Catalogo.Obter(peca.Tipo);
            if (def is null) continue;

            if (brilhos.TryGetValue(peca.Id, out var luz)) luz.Opacity = 0;

            if (visuais.TryGetValue(peca.Id, out var visual) && visual is Image img)
            {
                fontesOriginais[peca.Id] = fontesOriginais.GetValueOrDefault(peca.Id)
                                           ?? img.Source;
                var cinza = Pecas.Cinza(img.Source, Geometria.Tamanho(peca, def).Largura, Geometria.Tamanho(peca, def).Altura);
                if (!ReferenceEquals(img.Source, cinza)) img.Source = cinza;
            }

            if (energia is null) continue;

            var nivel = energia.NivelDaPeca(peca.Id);
            if (!grupos.TryGetValue(nivel, out var grupo)) continue;

            grupo.Children.Add(new RectangleGeometry(
                Rect.Inflate(CaixaNoMundo(peca, def), 3, 3), 5, 5));
        }

        foreach (var (nivel, grupo) in grupos) marcasDeEnergia[nivel].Data = grupo;
    }
    /// <summary>Fio solto (curva) ou reto (segmentos em L, como no Wokwi).</summary>
    /// <summary>Tracado que os fios NOVOS recebem.</summary>
    public string EstiloPadrao { get; set; } = Fio.EstiloSolto;
    public bool MostrarGrade { get; set; } = true;

    /// <summary>
    /// A grossura dos fios, das configuracoes: 1 e a de fabrica. Numa
    /// tela grande de projetor o fio fino some; num circuito cheio o
    /// grosso esconde os furos.
    /// </summary>
    public double FatorDosFios
    {
        get => fatorDosFios;
        set
        {
            if (Math.Abs(value - fatorDosFios) < 0.001) return;
            fatorDosFios = value;
            RedesenharFios();
        }
    }

    double fatorDosFios = 1;
    public double Zoom => escala.ScaleX;

    public event Action Mudou;
    public event Action SelecaoMudou;
    /// <summary>
    /// Disparado ENQUANTO se arrasta, a cada mudanca de posicao. Separado
    /// do Mudou porque este empilha desfazer -- um arraste viraria
    /// dezenas de passos no historico.
    /// </summary>
    public event Action Arrastou;

    readonly HashSet<string> selecionadas = [];
    readonly List<Rectangle> realces = [];
    readonly Rectangle laco = new();
    readonly Path pinosLigados = new();
    readonly Path marcasBotoes = new();
    readonly Path marcasApertadas = new();
    readonly Path slotsCheios = new();
    readonly Path saidasAcesas = new();
    readonly Path niveisLogicos = new();

    const int AmostrasDoAnalisador = 150;
    const int CanaisDoAnalisador = 8;

    static readonly (double X, double Y, double Larg, double Alt) TelaDoAnalisador =
        (0.2083, 0.1091, 0.75, 0.7818);

    readonly Dictionary<string, List<int>[]> ondasDoAnalisador = [];
    readonly Dictionary<string, Path[]> tracosDoAnalisador = [];
    bool lacando;
    Point lacoInicio;

    /// <summary>Ids de tudo que esta selecionado.</summary>
    public IReadOnlyCollection<string> Selecionadas => selecionadas;
    public int QuantidadeSelecionada => selecionadas.Count;

    /// <summary>A peca principal: so faz sentido quando ha uma so.</summary>
    public string PecaSelecionada => selecionadas.Count == 1 ? selecionadas.First() : null;

    readonly HashSet<int> fiosSelecionados = [];

    /// <summary>Fios no laco de selecao. Indices em Atual.Fios.</summary>
    public IReadOnlyCollection<int> FiosSelecionados => fiosSelecionados;

    /// <summary>O unico fio selecionado, ou -1 quando ha zero ou varios.</summary>
    public int FioSelecionado => fiosSelecionados.Count == 1 ? fiosSelecionados.First() : -1;
    public HashSet<string> BotoesPressionados { get; } = [];

    /// <summary>
    /// A tecla do teclado matricial que esta com o dedo em cima, por
    /// peca. Uma de cada vez, como o dedo de verdade.
    /// </summary>
    readonly Dictionary<string, string> teclaMatricial = [];

    /// <summary>Para o teste: o dedo numa tecla do teclado matricial (vazio: solta).</summary>
    internal void TeclaMatricialParaTeste(string idDoTeclado, string tecla)
    {
        if (string.IsNullOrEmpty(tecla)) teclaMatricial.Remove(idDoTeclado);
        else teclaMatricial[idDoTeclado] = tecla;
    }

    /// <summary>
    /// O manche do joystick que esta sendo arrastado agora, e se ele
    /// chegou a sair do centro.
    ///
    /// Sair do centro decide o que o clique significou: arrastar move os
    /// eixos, clicar sem arrastar aperta o SEL -- que e o botao de
    /// apertar o manche para baixo.
    /// </summary>
    (string Peca, bool Mexeu)? mancheArrastado;

    /// <summary>Quanto o manche anda, em pixels da peca, ate o fim do curso.</summary>
    const double CursoDoManche = 30.0;

    /// <summary>Placas com o botao BOOT segurado (leva o GPIO0 a LOW).</summary>
    public HashSet<string> BootPressionado { get; } = [];

    /// <summary>Placas com o RESET (ou o EN da ESP32) segurado agora.</summary>
    public HashSet<string> ResetSegurado { get; } = [];

    /// <summary>
    /// RESET apertado. Na placa real o chip fica parado enquanto o botao
    /// estiver embaixo, e so recomeca ao soltar -- por isso sao dois
    /// eventos, e nao um "reiniciar" so.
    /// </summary>
    public event Action<string> ResetApertado;

    /// <summary>RESET solto: a placa volta do setup().</summary>
    public event Action<string> ResetSolto;

    /// <summary>
    /// Os botoes soldados em cada placa, em coordenadas da CAIXA da peca.
    ///
    /// Todos MEDIDOS no proprio SVG e convertidos pela escala uniforme do
    /// desenho, do mesmo jeito que os da ESP32. Antes so a ESP32 tinha
    /// botao que respondia: o RESET das outras placas estava desenhado,
    /// mas clicar nele nao fazia nada.
    ///
    /// A conta de cada um: centro no viewBox, menos a origem do viewBox,
    /// vezes a escala (a menor entre largura e altura), mais a folga que o
    /// encaixe Uniform deixa no eixo que sobrou.
    /// </summary>
    internal static readonly Dictionary<string, (double X, double Y, double R, string Nome)[]>
        BotoesPorPlaca = new()
    {
        // <rect> de 11,1 x 9,96 em x=17,7 e x=77,6, y=177; viewBox 107x201
        // em caixa 107x204, escala 1 e 1,5 de folga vertical.
        ["esp32-devkit-v1"] = [(23.25, 183.5, 8.0, "EN"), (83.15, 183.5, 8.0, "BOOT")],

        // circle#reset-button cx=6,96 cy=4,53 r=1,54; viewBox -4 0 72,58 53,34
        // em caixa 274x202 -> escala 3,775 e 0,32 de folga vertical.
        ["arduino-uno"] = [(41.4, 17.4, 6.5, "RESET")],

        // o circulo tem rotate(269,81): cx=-24,9 cy=72,09 vai para (72,17; 24,66).
        // viewBox -4 0 102,66 53,45 em caixa 388x202 -> escala 3,779.
        ["arduino-mega"] = [(287.9, 93.2, 6.5, "RESET")],

        // cx=24,8 cy=8,7 r=1; viewBox -1,4 0 44,9 17,8 em caixa 170x67 ->
        // escala 3,764 e 0,5 de folga horizontal.
        ["arduino-nano"] = [(99.1, 32.7, 5.5, "RESET")],

        // circle cx=48,5 cy=37,7 r=3,48; viewBox 0 0 168 67,9 em caixa
        // 168x68 -> escala 1 e 0,05 de folga vertical.
        ["nano-rp2040-connect"] = [(48.5, 37.75, 5.5, "RESET")],

        // circle#reset-button cx=49,7 cy=4,3 r=1,4; viewBox 0 0 64 30 em
        // caixa 242x113 -> escala 3,767 e 0,47 de folga horizontal.
        ["franzininho"] = [(187.7, 16.2, 6.0, "RESET")],
    };

    /// <summary>
    /// Os vinte botoes do controle remoto, com o codigo que cada um manda.
    ///
    /// As posicoes saem do proprio SVG: cada botao e um &lt;use&gt; de raio
    /// 16,3 nas colunas 29,6 / 75,5 / 121,4 e nas linhas 37,9 / 75,2 /
    /// 113 / 152 / 190 / 228 / 266. O viewBox e 151x316, que e o tamanho
    /// da peca em pixels, entao a conta e direta.
    ///
    /// Os codigos sao os do controle de 21 teclas que vem em toda caixa
    /// de kit -- os mesmos que aparecem em todo tutorial de
    /// infravermelho. Escolha deliberada: o aluno compara com
    /// "0xFF30CF" e o numero bate com o que ele acharia na internet.
    /// </summary>
    static readonly (double X, double Y, double Raio, string Nome, long Codigo)[]
        BotoesDoControle =
    [
        (29.6, 37.9, 17, "power", 0xFFA25D), (121.4, 37.9, 17, "menu", 0xFFE21D),
        (29.6, 75.2, 17, "test", 0xFF22DD), (75.5, 75.2, 17, "+", 0xFF629D),
        (121.4, 75.2, 17, "back", 0xFF02FD),
        (29.6, 113, 17, "prev", 0xFFC23D), (75.5, 113, 17, "play", 0xFFA857),
        (121.4, 113, 17, "next", 0xFF906F),
        (29.6, 152, 17, "0", 0xFF6897), (75.5, 152, 17, "-", 0xFFE01F),
        (121.4, 152, 17, "c", 0xFFB04F),
        (29.6, 190, 17, "1", 0xFF30CF), (75.5, 190, 17, "2", 0xFF18E7),
        (121.4, 190, 17, "3", 0xFF7A85),
        (29.6, 228, 17, "4", 0xFF10EF), (75.5, 228, 17, "5", 0xFF38C7),
        (121.4, 228, 17, "6", 0xFF5AA5),
        (29.6, 266, 17, "7", 0xFF42BD), (75.5, 266, 17, "8", 0xFF4AB5),
        (121.4, 266, 17, "9", 0xFF52AD),
    ];

    /// <summary>Para o teste: as teclas do controle infravermelho e o codigo de cada uma.</summary>
    internal static IEnumerable<(string Nome, long Codigo)> TeclasDoControleParaTeste()
        => BotoesDoControle.Select(b => (b.Nome, b.Codigo));

    /// <summary>A tecla do controle que esta apertada agora, por peca.</summary>
    readonly Dictionary<string, (string Nome, long Codigo)> teclaDoControle = [];

    /// <summary>A ultima tecla entregue por controle, e quando (no relogio da placa): o aperto e o repeat do NEC.</summary>
    readonly Dictionary<string, (long Codigo, long Ms)> irEntregue = [];

    /// <summary>
    /// Aperta (ou solta) uma tecla do controle infravermelho pelo nome.
    ///
    /// O caminho normal e o clique no desenho. Este existe para o
    /// diagnostico e para o teste, que precisam apontar o controle sem
    /// ter mouse -- e apertar pelo caminho de verdade, e nao escrevendo
    /// direto na tabela de leituras, porque quem escreve la e o
    /// AplicarEntradas a cada quadro: uma injecao por fora seria apagada
    /// na volta seguinte.
    /// </summary>
    public bool ApertarNoControle(PecaNoCircuito peca, string nomeDaTecla)
    {
        if (peca?.Tipo != "ir-remote") return false;

        if (nomeDaTecla is null)
        {
            teclaDoControle.Remove(peca.Id);
            return true;
        }

        foreach (var (_, _, _, nome, codigo) in BotoesDoControle)
        {
            if (nome != nomeDaTecla) continue;
            teclaDoControle[peca.Id] = (nome, codigo);
            return true;
        }

        return false;
    }

    /// <summary>Os nomes das teclas do controle, na ordem do desenho.</summary>
    public static IEnumerable<string> TeclasDoControle
        => BotoesDoControle.Select(b => b.Nome);

    /// <summary>Qual tecla do controle esta sob o ponto, se alguma.</summary>
    static (string Nome, long Codigo)? TeclaDoControle(
        PecaNoCircuito peca, PecaDef def, Point p)
    {
        if (peca.Tipo != "ir-remote") return null;

        foreach (var (bx, by, raio, nome, codigo) in BotoesDoControle)
        {
            var c = Geometria.PosicaoLocal(peca, def, bx, by);
            double dx = c.X - p.X, dy = c.Y - p.Y;
            if (dx * dx + dy * dy <= raio * raio) return (nome, codigo);
        }
        return null;
    }

    /// <summary>Qual botao da placa esta sob o ponto, se algum.</summary>
    /// <summary>
    /// Os dois LEDs soldados na ESP32 DevKit V1.
    ///
    /// O SVG do Wokwi desenha o encapsulamento -- dois retangulos claros
    /// logo abaixo do modulo -- mas nao acende nada, entao a luz e uma
    /// sobreposicao nossa. As posicoes vieram do proprio SVG: rect x=35
    /// y=111 e x=69,5 y=110, de 3,8x5,3, num viewBox de 107x201 dentro de
    /// uma caixa de 107x204, o que deixa 1,5 de folga em cima.
    /// </summary>
    static readonly (double X, double Y, double R, int Gpio, Color Cor)[] LedsDaPlaca =
    [
        // Vermelho de energia: acende so por estar ligada, sem GPIO.
        (36.9, 115.2, 3.1, -1, Color.FromRgb(0xFF, 0x3B, 0x30)),
        // Azul no GPIO 2, que e o LED_BUILTIN da placa.
        (71.4, 114.2, 3.1, 2, Color.FromRgb(0x3B, 0x8C, 0xFF)),
    ];

    readonly Dictionary<string, List<Ellipse>> ledsDePlaca = [];

    /// <summary>
    /// Circulo que se aperta em cada botao de modulo, em coordenadas da
    /// CAIXA da peca. Medido no proprio SVG -- class="button-active-circle"
    /// nos botoes, id="knob" no joystick e no encoder -- e convertido pela
    /// mesma escala uniforme do desenho. Chutar essas medidas ja me custou
    /// tres rodadas neste projeto.
    /// </summary>
    internal static readonly Dictionary<string, (double X, double Y, double R)>
        CapasDeBotao = new()
    {
        // viewBox "-3 0 18 12" em caixa 67x45 -> escala 3,722.
        // circle cx=6 cy=6 r=3,822.
        ["pushbutton"] = (33.5, 22.5, 14.2),

        // o circulo esta dentro de um grupo com matrix(0,5056 ...),
        // viewBox "-3 0 7,4954 6" em caixa 28x23 -> escala 3,736.
        ["pushbutton-6mm"] = (14.0, 11.5, 7.2),

        // A alavanca da chave deslizante. O desenho tem id="handle" e
        // mede 2,74..3,99 x 0..2,06 no viewBox de 8,5 mm, que na caixa
        // de 32 px da 10,4..15,1 -- mais o curso de 5,8 px.
        ["slide-switch"] = (15.6, 4.5, 9.5),

        // knob cx=13,6 cy=13,6 r=10,6, viewBox 27,2x31,8 em caixa
        // 103x120 -> escala 3,774.
        ["analog-joystick"] = (51.5, 51.3, 40.0),

        // knob cx=36,6 cy=28,5 r=13,5, viewBox 116x70,4 em caixa
        // 116x70 -> escala 0,994.
        ["ky-040"] = (36.7, 28.3, 13.4),

        // O corpo do pendrive (pendrive-usb-movel.svg) vai de 21,2 a 33,6 mm
        // na caixa de 34x15 mm (129x57 px): o circulo pega o pendrive
        // encaixado e a boca do conector, por onde ele volta.
        ["pendrive-usb"] = (103.6, 28.3, 20.4),

        // A alavanca da chave fim de curso, da dobradica ate a roldana
        // (micro-switch-movel.svg: 5..26 mm x 1..8 mm na caixa de 28x19 mm).
        ["micro-switch"] = (60.5, 17.4, 22.0),
    };

    /// <summary>
    /// A capa e o unico ponto que APERTA; o resto do corpo arrasta, como
    /// no Wokwi.
    ///
    /// Antes apertar e arrastar eram o mesmo clique: segurar o botao
    /// entrava no caminho de arraste, e dois pixels de tremido na mao
    /// tiravam as pernas dos furos -- o raio do furo e 4,5 px. A rede era
    /// refeita sem a peca, PrimeiroGpio devolvia -1 e o aperto nunca
    /// chegava na Maquina. Ao soltar, o encaixe punha tudo no lugar e nao
    /// sobrava nenhum sinal de que algo tinha acontecido.
    /// </summary>
    /// <summary>
    /// Qual das oito chaves do DIP esta sob o ponto, ou -1.
    ///
    /// Usa as mesmas medidas do desenho das chaves, e nao uma copia: o
    /// alvo do clique e exatamente o retangulo que a pessoa ve.
    /// </summary>
    static int ChaveDipNoPonto(PecaNoCircuito peca, PecaDef def, Point p)
    {
        if (peca.Tipo != "dip-switch-8") return -1;

        for (int k = 0; k < 8; k++)
        {
            double x = DipPrimeira + k * DipPasso;
            var canto = Geometria.PosicaoLocal(peca, def, x, DipTopo);
            var oposto = Geometria.PosicaoLocal(
                peca, def, x + DipLargura, DipTopo + DipAltura);

            var caixa = new Rect(canto, oposto);
            caixa.Inflate(2, 2);
            if (caixa.Contains(p)) return k;
        }
        return -1;
    }

    /// <summary>
    /// As dezesseis teclas do teclado matricial, em pixels da peca.
    ///
    /// Vem do proprio SVG: cada tecla e um grupo com
    /// translate(x y) e data-key-name, e o retangulo tem 11,2 x 11 nas
    /// unidades do viewBox (70,336 de largura para 265,8 px de caixa).
    /// </summary>
    internal static readonly (double X, double Y, string Tecla)[] TeclasMatriciais =
        MontarTeclado();

    static (double, double, string)[] MontarTeclado()
    {
        const double escala = 265.8 / 70.336;
        (double x, double y, string tecla)[] cru =
        [
            (7, 10.7, "1"), (22, 10.7, "2"), (37, 10.7, "3"), (52, 10.7, "A"),
            (7, 25.0, "4"), (22, 25.0, "5"), (37, 25.0, "6"), (52, 25.0, "B"),
            (7, 39.3, "7"), (22, 39.3, "8"), (37, 39.3, "9"), (52, 39.3, "C"),
            (7, 53.6, "*"), (22, 53.6, "0"), (37, 53.6, "#"), (52, 53.6, "D"),
        ];

        // O centro da tecla: o retangulo mede 11,2 x 11 a partir do canto.
        return [.. cru.Select(t => ((t.x + 5.6) * escala, (t.y + 5.5) * escala, t.tecla))];
    }

    /// <summary>Meia tecla, em pixels da peca: o alvo do clique.</summary>
    const double RaioDaTecla = 20.0;

    static string TeclaMatricialNoPonto(PecaNoCircuito peca, PecaDef def, Point p)
    {
        if (peca.Tipo != "membrane-keypad") return null;

        foreach (var (x, y, tecla) in TeclasMatriciais)
        {
            var c = Geometria.PosicaoLocal(peca, def, x, y);
            if (Math.Abs(c.X - p.X) <= RaioDaTecla && Math.Abs(c.Y - p.Y) <= RaioDaTecla)
                return tecla;
        }
        return null;
    }

    /// <summary>
    /// Os dez furos do disco de telefone, em pixels da peca.
    ///
    /// Sao as posicoes dos numeros no proprio SVG -- o desenho nao tem
    /// viewBox, entao ja estao em pixels da caixa.
    /// </summary>
    internal static readonly (double X, double Y, int Numero)[] FurosDoDisco =
    [
        (230, 126, 1), (210, 79, 2), (168, 47, 3), (117, 41, 4), (72, 58, 5),
        (35, 97, 6), (28, 145, 7), (40, 194, 8), (78, 230, 9), (129, 243, 0),
    ];

    const double RaioDoFuro = 22.0;

    static int FuroDoDiscoNoPonto(PecaNoCircuito peca, PecaDef def, Point p)
    {
        if (peca.Tipo != "rotary-dialer") return -1;

        foreach (var (x, y, numero) in FurosDoDisco)
        {
            var c = Geometria.PosicaoLocal(peca, def, x, y);
            double dx = c.X - p.X, dy = c.Y - p.Y;
            if (dx * dx + dy * dy <= RaioDoFuro * RaioDoFuro) return numero;
        }
        return -1;
    }

    static bool NaCapaDoBotao(PecaNoCircuito peca, PecaDef def, Point p)
    {
        if (!CapasDeBotao.TryGetValue(peca.Tipo, out var capa)) return false;
        var c = Geometria.PosicaoLocal(peca, def, capa.X, capa.Y);
        double dx = c.X - p.X, dy = c.Y - p.Y;
        return dx * dx + dy * dy <= capa.R * capa.R;
    }

    /// <summary>
    /// Troca o desenho pela variante apertada, quando ela existe. So o
    /// pushbutton grande tem: antes o codigo usava "pushbutton-solto"
    /// para todos, entao apertar um joystick trocava o desenho dele por
    /// um botao.
    /// </summary>
    void DesenhoDoBotao(PecaNoCircuito peca, string estado)
    {
        string nome = $"{peca.Tipo}-{estado}";
        if (!Pecas.Existe(nome)) return;
        if (visuais.TryGetValue(peca.Id, out var v) && v is Image img)
            img.Source = Pecas.Obter(nome);
    }

    /// <summary>Para o teste: quanto a parte movel andou no eixo X.</summary>
    internal double DeslocamentoDoMovelParaTeste(string idDaPeca)
        => parteMovel.TryGetValue(idDaPeca, out var m)
           && m.RenderTransform is TransformGroup g && g.Children.Count > 0
           && g.Children[0] is TranslateTransform t ? t.X : 0;

    /// <summary>Para o teste: quantas marcas de "apertado" estao na tela.</summary>
    internal int MarcasApertadasParaTeste
        => marcasApertadas.Data is GeometryGroup g ? g.Children.Count : 0;

    void PressionarBotao(PecaNoCircuito peca)
    {
        BotoesPressionados.Add(peca.Id);
        DesenhoDoBotao(peca, "pressionado");
        DesenharBotoesDaPlaca();
        CaptureMouse();     // garante o MouseUp mesmo se sair da capa
    }

    static string BotaoDaPlaca(PecaNoCircuito peca, PecaDef def, Point p)
    {
        if (!BotoesPorPlaca.TryGetValue(peca.Tipo, out var botoes)) return null;

        foreach (var (bx, by, raio, nome) in botoes)
        {
            var c = Geometria.PosicaoLocal(peca, def, bx, by);
            double dx = c.X - p.X, dy = c.Y - p.Y;
            if (dx * dx + dy * dy <= raio * raio) return nome;
        }
        return null;
    }

    /// <summary>
    /// Anel discreto em cima de cada botao fisico. Sem marca ninguem
    /// descobre que da para apertar.
    /// </summary>
    void DesenharBotoesDaPlaca()
    {
        var soltos = new GeometryGroup();
        var apertados = new GeometryGroup();

        // A tecla do controle so ganha marca ENQUANTO esta apertada:
        // vinte aneis permanentes por cima do desenho seriam poluicao.
        foreach (var (id, tecla) in teclaDoControle)
        {
            var peca = Atual.Achar(id);
            var def = peca is null ? null : Catalogo.Obter(peca.Tipo);
            if (def is null) continue;

            foreach (var (bx, by, raio, nome, _) in BotoesDoControle)
            {
                if (nome != tecla.Nome) continue;
                apertados.Children.Add(new EllipseGeometry(
                    Geometria.PosicaoLocal(peca, def, bx, by), raio, raio));
            }
        }

        foreach (var peca in Atual.Pecas)
        {
            if (!BotoesPorPlaca.TryGetValue(peca.Tipo, out var botoes)) continue;
            var def = Catalogo.Obter(peca.Tipo);
            if (def is null) continue;

            foreach (var (bx, by, raio, nome) in botoes)
            {
                bool apertado = nome == "BOOT"
                    ? BootPressionado.Contains(peca.Id)
                    : ResetSegurado.Contains(peca.Id);
                (apertado ? apertados : soltos).Children.Add(new EllipseGeometry(
                    Geometria.PosicaoLocal(peca, def, bx, by), raio, raio));
            }
        }
        // A capa de QUALQUER botao apertado escurece, venha o aperto do
        // mouse ou de uma tecla vinculada. So o pushbutton grande tem
        // desenho proprio para o estado apertado, e mesmo nele a
        // diferenca e pequena demais para ler na tela; nos outros nao
        // havia sinal nenhum de que o clique pegou.
        foreach (var (idDoTeclado, tecla) in teclaMatricial)
        {
            var teclado = Atual.Achar(idDoTeclado);
            var defTeclado = teclado is null ? null : Catalogo.Obter(teclado.Tipo);
            if (defTeclado is null) continue;

            foreach (var (tx, ty, nome) in TeclasMatriciais)
            {
                if (nome != tecla) continue;
                apertados.Children.Add(new EllipseGeometry(
                    Geometria.PosicaoLocal(teclado, defTeclado, tx, ty), 17, 17));
            }
        }

        foreach (string id in BotoesPressionados)
        {
            var peca = Atual.Achar(id);
            if (peca is null || !CapasDeBotao.TryGetValue(peca.Tipo, out var capa)) continue;

            var def = Catalogo.Obter(peca.Tipo);
            if (def is null) continue;

            apertados.Children.Add(new EllipseGeometry(
                Geometria.PosicaoLocal(peca, def, capa.X, capa.Y), capa.R * 0.92, capa.R * 0.92));
        }

        marcasBotoes.Data = soltos;
        marcasApertadas.Data = apertados;
    }

    string arrastando;
    Point ondePegou;
    bool moveuDeVerdade;
    /// <summary>Onde o mouse desceu, para o limiar de arraste.</summary>
    Point pegouEm;

    /// <summary>
    /// Quanto a mao precisa andar antes de a peca comecar a se mexer.
    /// Sem isso um clique tremido de 1 px ja arrastava o componente: a
    /// perna saia do furo (raio 4,5 px) e a ligacao caia no meio do
    /// clique.
    /// </summary>
    const double LimiarArraste = 4;
    (string peca, string pino)? origemFio;
    bool panejando;
    Point panInicio;

    const double PassoGrade = Catalogo.Passo / 2;

    /// <summary>
    /// Peca arrastada da paleta cai onde foi solta.
    ///
    /// So o id viaja no arrasto; o objeto e montado aqui, ja no ponto
    /// certo -- e passando pelo mesmo encaixe nos furos que uma peca
    /// adicionada pelo teclado, senao a peca solta em cima da
    /// protoboard nao ficaria ligada em nada.
    /// </summary>
    void PrepararArrastoDaPaleta()
    {
        AllowDrop = true;

        DragOver += (_, e) =>
        {
            e.Effects = e.Data.GetDataPresent(JanelaPecas.FormatoArrasto)
                ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        };

        Drop += (_, e) =>
        {
            if (e.Data.GetData(JanelaPecas.FormatoArrasto) is not string tipo) return;

            var p = e.GetPosition(this);
            var def = Catalogo.Obter(tipo);
            if (def is null) return;

            // O ponteiro vira o CENTRO da peca, nao o canto: soltar em
            // cima de um furo tem que encaixar naquele furo.
            var mundo = new Point((p.X - deslocamento.X) / escala.ScaleX,
                                  (p.Y - deslocamento.Y) / escala.ScaleY);

            var peca = Adicionar(tipo, mundo);
            if (peca is not null) PecaSolta?.Invoke(peca);
            e.Handled = true;
        };
    }

    /// <summary>Avisa a janela para escrever na barra de status.</summary>
    public event Action<PecaNoCircuito> PecaSolta;

    public EditorCircuito()
    {
        Background = new SolidColorBrush(Color.FromRgb(0x10, 0x10, 0x12));
        ClipToBounds = true;
        Focusable = true;

        var grupo = new TransformGroup();
        grupo.Children.Add(escala);
        grupo.Children.Add(deslocamento);
        mundo.RenderTransform = grupo;
        mundo.Background = Brushes.Transparent;
        Child = mundo;

        PrepararArrastoDaPaleta();

        // Marcador do pino: anel VAZADO. O anterior era um disco cheio e
        // escondia justamente o pino que o aluno precisa mirar.
        marcadorPino.Width = marcadorPino.Height = 13;
        marcadorPino.Fill = Brushes.Transparent;
        marcadorPino.Stroke = new SolidColorBrush(Color.FromRgb(0xFF, 0xD5, 0x4F));
        marcadorPino.StrokeThickness = 2.2;
        marcadorPino.Visibility = Visibility.Collapsed;
        marcadorPino.IsHitTestVisible = false;
        Panel.SetZIndex(marcadorPino, 900);

        textoPino.Foreground = Brushes.Black;
        textoPino.FontSize = 10;
        textoPino.FontWeight = FontWeights.Bold;
        rotuloPino.Child = textoPino;
        rotuloPino.Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xD5, 0x4F));
        rotuloPino.CornerRadius = new CornerRadius(3);
        rotuloPino.Padding = new Thickness(4, 1, 4, 1);
        rotuloPino.Visibility = Visibility.Collapsed;
        rotuloPino.IsHitTestVisible = false;
        Panel.SetZIndex(rotuloPino, 901);

        fioEmCurso.StrokeThickness = 3;
        fioEmCurso.StrokeStartLineCap = PenLineCap.Round;
        fioEmCurso.StrokeEndLineCap = PenLineCap.Round;
        fioEmCurso.Visibility = Visibility.Collapsed;
        fioEmCurso.IsHitTestVisible = false;
        Panel.SetZIndex(fioEmCurso, 850);

        laco.Stroke = new SolidColorBrush(Color.FromRgb(0x00, 0xC8, 0xD0));
        laco.StrokeThickness = 1;
        laco.StrokeDashArray = [3, 2];
        laco.Fill = new SolidColorBrush(Color.FromArgb(0x22, 0x00, 0xC8, 0xD0));
        laco.Visibility = Visibility.Collapsed;
        laco.IsHitTestVisible = false;
        Panel.SetZIndex(laco, 950);
        mundo.Children.Add(laco);

        // Anel amarelo em cada pino que faz parte de alguma rede.
        pinosLigados.Stroke = new SolidColorBrush(Color.FromRgb(0xFF, 0xC1, 0x07));
        pinosLigados.StrokeThickness = 1.8;
        pinosLigados.Fill = new SolidColorBrush(Color.FromArgb(0x70, 0xFF, 0xC1, 0x07));
        pinosLigados.IsHitTestVisible = false;
        Panel.SetZIndex(pinosLigados, 700);
        mundo.Children.Add(pinosLigados);

        // Botoes fisicos por cima da placa, para serem visiveis e clicaveis.
        marcasBotoes.Stroke = new SolidColorBrush(Color.FromArgb(0xAA, 0xFF, 0xFF, 0xFF));
        marcasBotoes.StrokeThickness = 1.2;
        marcasBotoes.Fill = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF));
        marcasBotoes.IsHitTestVisible = false;
        Panel.SetZIndex(marcasBotoes, 720);
        mundo.Children.Add(marcasBotoes);

        // Botao da placa embaixo do dedo: escurece, para o clique ter
        // retorno -- segurar o RESET apaga a placa, e sem isto parecia
        // defeito.
        marcasApertadas.Stroke = new SolidColorBrush(Color.FromArgb(0xEE, 0xFF, 0xFF, 0xFF));
        marcasApertadas.StrokeThickness = 1.8;
        marcasApertadas.Fill = new SolidColorBrush(Color.FromArgb(0x99, 0x00, 0x00, 0x00));
        marcasApertadas.IsHitTestVisible = false;
        Panel.SetZIndex(marcasApertadas, 721);
        mundo.Children.Add(marcasApertadas);

        foreach (var (nivel, marca) in marcasDeEnergia)
        {
            marca.Stroke = new SolidColorBrush(CoresDaEnergia[nivel]);
            marca.StrokeThickness = 2;
            marca.Fill = new SolidColorBrush(Color.FromArgb(0x22,
                CoresDaEnergia[nivel].R, CoresDaEnergia[nivel].G, CoresDaEnergia[nivel].B));
            marca.IsHitTestVisible = false;
            Panel.SetZIndex(marca, 740);
            mundo.Children.Add(marca);
        }

        // Furo com perna dentro: ponto cheio, para o slot preenchido
        // ficar visivel mesmo com a peca por cima.
        slotsCheios.Fill = new SolidColorBrush(Color.FromRgb(0xFF, 0xC1, 0x07));
        slotsCheios.Stroke = new SolidColorBrush(Color.FromArgb(0x99, 0x00, 0x00, 0x00));
        slotsCheios.StrokeThickness = 0.6;
        slotsCheios.IsHitTestVisible = false;
        Panel.SetZIndex(slotsCheios, 60);
        mundo.Children.Add(slotsCheios);

        // Luz nos pinos de saida acesos (barra de LEDs, 7 segmentos).
        saidasAcesas.Fill = new SolidColorBrush(Color.FromArgb(0xDD, 0xFF, 0x50, 0x40));
        niveisLogicos.Fill = new SolidColorBrush(Color.FromArgb(0xEE, 0xFF, 0xD5, 0x4F));
        niveisLogicos.IsHitTestVisible = false;
        Panel.SetZIndex(niveisLogicos, 320);
        mundo.Children.Add(niveisLogicos);

        saidasAcesas.IsHitTestVisible = false;
        saidasAcesas.Effect = new System.Windows.Media.Effects.BlurEffect { Radius = 3 };
        Panel.SetZIndex(saidasAcesas, 160);
        mundo.Children.Add(saidasAcesas);
        mundo.Children.Add(fioEmCurso);
        mundo.Children.Add(marcadorPino);
        mundo.Children.Add(rotuloPino);

        MouseLeftButtonDown += AoApertar;
        MouseMove += AoMover;
        MouseLeftButtonUp += AoSoltar;
        MouseDown += AoApertarQualquer;
        MouseUp += AoSoltarQualquer;
        MouseWheel += AoGirarRoda;
        MouseLeave += (_, _) => EsconderMarcador();
    }

    void EsconderMarcador()
    {
        marcadorPino.Visibility = Visibility.Collapsed;
        rotuloPino.Visibility = Visibility.Collapsed;
    }

    static Color CorDoFundo => Tema.CorDe(Tema.FundoDoCircuito);
    static Color CorDaGrade => Tema.CorDe(Tema.PontoDaGrade);

    /// <summary>
    /// A grade do fundo, no passo de 0,1" da protoboard.
    ///
    /// Fica no FUNDO do editor, e nao dentro do canvas do circuito: o
    /// canvas tem o tamanho da area visivel e anda junto com o pan, entao
    /// a grade terminava numa borda invisivel -- arrastar um pouco e o
    /// fundo acabava. Aqui ela e um ladrilho que segue o deslocamento e o
    /// zoom, e por isso nao tem fim.
    /// </summary>
    public void AplicarGrade()
    {
        if (!MostrarGrade)
        {
            Background = new SolidColorBrush(CorDoFundo);
            return;
        }

        double passo = Catalogo.Passo * escala.ScaleX;

        // Muito longe, os pontos viram ruido cinza: melhor so o fundo.
        if (passo < 4.5)
        {
            Background = new SolidColorBrush(CorDoFundo);
            return;
        }

        double raio = Math.Clamp(0.7 * escala.ScaleX, 0.5, 1.8);
        var desenho = new DrawingGroup();
        desenho.Children.Add(new GeometryDrawing(
            new SolidColorBrush(CorDoFundo), null,
            new RectangleGeometry(new Rect(0, 0, passo, passo))));
        desenho.Children.Add(new GeometryDrawing(
            new SolidColorBrush(CorDaGrade), null,
            new EllipseGeometry(new Point(passo / 2, passo / 2), raio, raio)));

        // O ponto do ladrilho cai no MESMO lugar que um furo da
        // protoboard cairia: o ladrilho comeca meio passo antes do
        // deslocamento atual.
        Background = new DrawingBrush(desenho)
        {
            TileMode = TileMode.Tile,
            ViewportUnits = BrushMappingMode.Absolute,
            Stretch = Stretch.None,
            Viewport = new Rect(Resto(deslocamento.X - passo / 2, passo),
                                Resto(deslocamento.Y - passo / 2, passo),
                                passo, passo),
        };
    }

    /// <summary>Resto sempre positivo: o pan negativo tambem precisa ladrilhar.</summary>
    static double Resto(double a, double b) => ((a % b) + b) % b;

    // ================= desenhar =================
    public void Carregar(Circuito circuito)
    {
        Atual = circuito ?? new Circuito();
        selecionadas.Clear();
        fiosSelecionados.Clear();
        ZerarMotores();
        Redesenhar();
    }

    public void Redesenhar()
    {
        foreach (var v in visuais.Values) mundo.Children.Remove(v);
        foreach (var v in parteMovel.Values) mundo.Children.Remove(v);
        foreach (var (tela, _) in chavesDip.Values) mundo.Children.Remove(tela);
        chavesDip.Clear();

        foreach (var tracos in tracosDoAnalisador.Values)
            foreach (var traco in tracos) mundo.Children.Remove(traco);
        tracosDoAnalisador.Clear();
        ondasDoAnalisador.Clear();
        foreach (var v in brilhos.Values) mundo.Children.Remove(v);
        foreach (var tela in telasLcd.Values) foreach (var t in tela) mundo.Children.Remove(t);
        foreach (var b in bracosServo.Values) mundo.Children.Remove(b);
        foreach (var luzes in ledsDePlaca.Values)
            foreach (var l in luzes) mundo.Children.Remove(l);
        foreach (var marca in estouros.Values) mundo.Children.Remove(marca);
        estouros.Clear();

        // Tela do TFT, luz do rele e ponteiro do motor de passo saem
        // AQUI, e nao em ApagarQueimaduras().
        //
        // Estavam la por engano meu, e o efeito era so no TFT: Rodar()
        // chama ApagarQueimaduras(), a tela era removida do canvas e
        // ninguem a recriava -- ela so nasce em CriarVisual(). O rele e o
        // motor sobreviveram porque se recriam sozinhos a cada quadro,
        // e foi essa diferenca que escondeu o erro.
        foreach (var (imagem, letras) in telasTft.Values)
        {
            mundo.Children.Remove(imagem);
            mundo.Children.Remove(letras);
        }
        telasTft.Clear();
        versaoDaTft.Clear();

        foreach (var luz in luzesDeRele.Values) mundo.Children.Remove(luz);
        luzesDeRele.Clear();

        foreach (var par in ponteirosDePasso.Values)
            foreach (var ponteiro in par) mundo.Children.Remove(ponteiro);
        ponteirosDePasso.Clear();
        eixosDePasso.Clear();

        foreach (var r in rotulosDePeca.Values) mundo.Children.Remove(r);
        rotulosDePeca.Clear();
        ApagarRotulosDeGiro();
        ApagarLuzesDeModulo();
        ApagarDesenhosDeRadio();

        visuais.Clear(); parteMovel.Clear(); brilhos.Clear();
        telasLcd.Clear(); bracosServo.Clear(); ledsDePlaca.Clear();

        CalcularLadosDasPcbs();
        foreach (var peca in Atual.Pecas) CriarVisual(peca);
        ApagarOOutroLado();
        RedesenharFios();
        DesenharBotoesDaPlaca();
        AtualizarRealce();
    }

    /// <summary>
    /// O desenho de uma peca, como ele entra no circuito (sem posicao). E o
    /// mesmo para a foto de conferencia de uma peca sozinha (--desenho).
    /// </summary>
    internal FrameworkElement DesenhoDa(PecaNoCircuito peca, PecaDef def)
        => Catalogo.EhPcb(peca.Tipo) ? DesenharPcb(peca, def)
            : Catalogo.EhProtoboard(peca.Tipo)
            ? DesenharProtoboard(peca, def)
            : peca.Tipo == "texto"
                ? DesenharTexto(peca)
            : peca.Tipo == "resistor"
                ? DesenharResistor(peca, def)
                : new Image
                {
                    Source = Pecas.Obter(NomeSvg(peca, false)),
                    Width = Geometria.Tamanho(peca, def).Largura,
                    Height = Geometria.Tamanho(peca, def).Altura,
                    Stretch = Stretch.Uniform
                };

    void CriarVisual(PecaNoCircuito peca)
    {
        var def = Catalogo.Obter(peca.Tipo);
        if (def is null) return;

        FrameworkElement visual = DesenhoDa(peca, def);

        visual.Tag = peca.Id;
        Posicionar(visual, peca);
        AplicarSombra(visual);
        Panel.SetZIndex(visual, ZDe(peca.Tipo));
        mundo.Children.Add(visual);
        visuais[peca.Id] = visual;

        AtualizarRotulo(peca, def);

        if (peca.Tipo is "led" or "rgb-led")
        {
            var luz = new Ellipse
            {
                Width = def.Largura * 2.6,
                Height = def.Largura * 2.6,
                IsHitTestVisible = false,
                Opacity = 0
            };
            Panel.SetZIndex(luz, 150);
            mundo.Children.Add(luz);
            brilhos[peca.Id] = luz;
            PosicionarBrilho(peca, def);
            PosicionarEstouro(peca, def);
            PosicionarTft(peca, def);
        }

        if (peca.Tipo == "esp32-devkit-v1") CriarLedsDaPlaca(peca, def);
        CriarLuzesDeModulo(peca, def);
        if (Grades.TryGetValue(peca.Tipo, out var grade)) CriarTelaLcd(peca, grade);
        if (AreasDeTela.ContainsKey(peca.Tipo)) CriarTelaTft(peca, def);
        if (BracosDeServo.ContainsKey(peca.Tipo)) CriarBracoServo(peca, def);

        if (Pecas.Existe($"{peca.Tipo}-movel"))
        {
            string doModelo = FichasDeMotor.SvgDoModelo(peca, "movel");
            var movel = new Image
            {
                Source = Pecas.Obter(doModelo is not null && Pecas.Existe(doModelo) ? doModelo : $"{peca.Tipo}-movel"),
                Width = Geometria.Tamanho(peca, def).Largura,
                Height = Geometria.Tamanho(peca, def).Altura,
                Stretch = Stretch.Uniform,
                IsHitTestVisible = false,
                RenderTransformOrigin = new Point(0, 0),
                RenderTransform = NovoTransformeDeMovel(),
            };
            Canvas.SetLeft(movel, peca.X);
            Canvas.SetTop(movel, peca.Y);
            Panel.SetZIndex(movel, ZDe(peca.Tipo) + 1);
            mundo.Children.Add(movel);
            parteMovel[peca.Id] = movel;

            string corpoDoModelo = FichasDeMotor.SvgDoModelo(peca, "corpo");
            if (visual is Image img)
                img.Source = Pecas.Obter(corpoDoModelo is not null && Pecas.Existe(corpoDoModelo) ? corpoDoModelo : $"{peca.Tipo}-corpo");

            PosicionarMovel(peca, def);
            AnimarParteMovel(peca, def);
        }

        if (peca.Tipo == "dip-switch-8") CriarChavesDip(peca, def);
        if (peca.Tipo == "radio-controle") CriarDesenhoDoRadio(peca);
        if (peca.Tipo == "logic-analyzer") CriarAnalisador(peca);
    }

    /// <summary>
    /// A parte que se mexe de cada peca: onde fica o eixo do giro (em
    /// fracao da caixa) e quanto o desenho anda de ponta a ponta.
    ///
    /// Todas essas pecas eram desenhos parados. O professor listou uma
    /// por uma: "chave deslizante nao e animada, chave DIP 8 tambem nao,
    /// chave de telefone tambem nao, encoder rotativo tambem nao,
    /// joystick tambem nao, potenciometro deslizante tambem nao". Sem
    /// ver a peca reagir, nao da para saber se o clique pegou.
    ///
    /// Cada uma tem o seu SVG partido em dois -- "-corpo" e "-movel" --
    /// e o que muda aqui e so a conta do movimento.
    /// </summary>
    readonly record struct PecaAnimada(double EixoX, double EixoY,
                                       double InicioX = 0, double CursoX = 0,
                                       double CursoY = 0);

    static readonly Dictionary<string, PecaAnimada> Animadas = new()
    {
        // ---- giram em torno de um eixo ----
        ["potentiometer"] = new(0.5, 0.4),
        ["ky-040"] = new(36.6 / 116, 28.5 / 70),
        ["rotary-dialer"] = new(134.0 / 266, 134.0 / 286),
        ["stepper-motor"] = new(109.1 / 220, 109.1 / 235),

        // ---- deslizam ----
        ["slide-switch"] = new(0.5, 0.5, CursoX: 5.8),
        ["slide-potentiometer"] = new(0.5, 0.5, InicioX: -5.7, CursoX: 124.8),
        ["analog-joystick"] = new(0.5, 0.5, CursoX: 16, CursoY: 16),

        // O pendrive sai do conector: 9 mm para a direita.
        ["pendrive-usb"] = new(0.5, 0.5, CursoX: 34.0),

        // Os motores DC: o alvo no eixo do 130, o eixo branco do amarelo,
        // o sino do brushless (os centros saem do pecas-motores.py).
        ["motor-dc"] = new(0.5, 12.5 / 30.0),
        ["motor-tt"] = new(12.0 / 74.0, 0.5),
        ["motor-brushless"] = new(0.5, 18.0 / 44.0),
        ["motor-28byj48"] = new(0.5, 12.0 / 46.0),

        // O disco do servo de rotacao continua.
        ["servo-360"] = new(0.6875, 0.5),

        // Rodas e helices giram inteiras, no centro.
        ["roda-tt"] = new(0.5, 0.5),
        ["helice-130"] = new(0.5, 0.5),
        ["helice-1045"] = new(0.5, 0.5),
        ["helice-5045"] = new(0.5, 0.5),

        // A alavanca do fim de curso gira na dobradica (5 x 7,2 mm).
        ["micro-switch"] = new(5.0 / 28.0, 7.2 / 19.0),
    };

    /// <summary>
    /// Quatro transformes, nesta ordem: o movimento da parte, o giro do
    /// eixo, o espelho da peca e o giro da peca.
    ///
    /// Sao quatro e nao um porque os centros sao diferentes -- o eixo do
    /// botao nao e o centro da peca -- e RenderTransformOrigin so aceita
    /// um. Com o centro escrito em cada transforme, um potenciometro
    /// girado 90 graus continua com o botao no lugar certo (antes a
    /// parte movel simplesmente ignorava o giro da peca).
    /// </summary>
    static TransformGroup NovoTransformeDeMovel()
    {
        var grupo = new TransformGroup();
        grupo.Children.Add(new TranslateTransform());
        grupo.Children.Add(new RotateTransform());
        grupo.Children.Add(new ScaleTransform(1, 1));
        grupo.Children.Add(new RotateTransform());
        return grupo;
    }

    /// <summary>As oito chaves do DIP, desenhadas por cima do SVG.</summary>
    readonly Dictionary<string, (Canvas tela, Rectangle[] botoes)> chavesDip = [];

    // Medidas tiradas do proprio SVG do DIP (viewBox de 82,87 x 55,355,
    // que e o tamanho da peca em pixels): o primeiro rasgo comeca em
    // 4,693 e eles se repetem a cada 9,6 -- o passo de 0,1".
    const double DipPrimeira = 4.693, DipPasso = 9.6;
    const double DipLargura = 5.8168, DipTopo = 21.2, DipAltura = 13;

    /// <summary>
    /// O DIP tem oito chaves independentes -- 256 combinacoes -- entao
    /// nao da para trocar de desenho como no botao. Cada chave e um
    /// retangulo branco que sobe e desce dentro do rasgo, e o rasgo e
    /// pintado por baixo para cobrir a chave parada do SVG.
    /// </summary>
    void CriarChavesDip(PecaNoCircuito peca, PecaDef def)
    {
        var tela = new Canvas
        {
            Width = def.Largura,
            Height = def.Altura,
            IsHitTestVisible = false,
        };

        var botoes = new Rectangle[8];
        for (int k = 0; k < 8; k++)
        {
            double x = DipPrimeira + k * DipPasso;

            var rasgo = new Rectangle
            {
                Width = DipLargura,
                Height = DipAltura,
                Fill = new SolidColorBrush(Color.FromRgb(0x91, 0x7C, 0x6F)),
            };
            Canvas.SetLeft(rasgo, x);
            Canvas.SetTop(rasgo, DipTopo);
            tela.Children.Add(rasgo);

            botoes[k] = new Rectangle
            {
                Width = DipLargura - 0.6,
                Height = 6.2,
                RadiusX = 0.8,
                RadiusY = 0.8,
                Fill = new SolidColorBrush(Color.FromRgb(0xFF, 0xFE, 0xF4)),
            };
            Canvas.SetLeft(botoes[k], x + 0.3);
            tela.Children.Add(botoes[k]);
        }

        Panel.SetZIndex(tela, ZDe(peca.Tipo) + 1);
        mundo.Children.Add(tela);
        chavesDip[peca.Id] = (tela, botoes);

        PosicionarChavesDip(peca, def);
        MostrarChavesDip(peca, def);
    }

    void PosicionarChavesDip(PecaNoCircuito peca, PecaDef def)
    {
        if (!chavesDip.TryGetValue(peca.Id, out var dip)) return;
        dip.tela.Width = def.Largura;
        dip.tela.Height = def.Altura;
        Posicionar(dip.tela, peca);
    }

    void MostrarChavesDip(PecaNoCircuito peca, PecaDef def)
    {
        if (!chavesDip.TryGetValue(peca.Id, out var dip)) return;

        for (int k = 0; k < dip.botoes.Length; k++)
        {
            bool ligada = (peca.Bits & (1 << k)) != 0;

            // Ligada, a chave sobe para o lado do "ON" -- que no desenho
            // e o de cima.
            double alvo = ligada ? DipTopo + 0.4 : DipTopo + DipAltura - 6.6;

            // O Canvas.Top comeca NaN, e comparacao com NaN e sempre
            // falsa: sem este teste a chave nunca era posicionada e as
            // oito ficavam no canto de cima da peca.
            double agora = Canvas.GetTop(dip.botoes[k]);
            if (double.IsNaN(agora) || Math.Abs(agora - alvo) > 0.01)
                Canvas.SetTop(dip.botoes[k], alvo);
        }
        _ = def;
    }

    /// <summary>
    /// Inclina a peca inteira, somando ao giro e ao espelho dela.
    ///
    /// E o sensor de inclinacao: nele o que muda de estado e a POSICAO
    /// do corpo, e nao uma parte de dentro. O pivo fica perto do lado
    /// dos pinos, que e por onde ele fica preso.
    /// </summary>
    void InclinarPeca(PecaNoCircuito peca, PecaDef def, FrameworkElement visual, double graus)
    {
        if (visual.RenderTransform is TransformGroup pronto
            && pronto.Children.Count == 3
            && pronto.Children[2] is RotateTransform inclinacao)
        {
            if (Math.Abs(inclinacao.Angle - graus) > 0.01) inclinacao.Angle = graus;
            return;
        }

        visual.RenderTransformOrigin = new Point(0, 0);
        var grupo = new TransformGroup();
        grupo.Children.Add(new ScaleTransform(peca.Espelhada ? -1 : 1, 1,
                                              def.Largura / 2, def.Altura / 2));
        grupo.Children.Add(new RotateTransform(peca.Rotacao,
                                               def.Largura / 2, def.Altura / 2));
        grupo.Children.Add(new RotateTransform(graus, def.Largura * 0.94, def.Altura * 0.5));
        visual.RenderTransform = grupo;
    }

    /// <summary>Para o teste: apertar uma tecla do controle pelo nome.</summary>
    internal bool ApertarTeclaDoControle(string idDaPeca, string nome)
    {
        var peca = Atual.Achar(idDaPeca);
        var def = peca is null ? null : Catalogo.Obter(peca.Tipo);
        if (def is null) return false;

        foreach (var (bx, by, _, chamada, _) in BotoesDoControle)
        {
            if (chamada != nome) continue;
            return Apertar(Geometria.PosicaoLocal(peca, def, bx, by));
        }
        return false;
    }

    /// <summary>Para o teste: giro e deslocamento da parte movel, na tela.</summary>
    internal (double Angulo, double X, double Y) MovimentoParaTeste(string idDaPeca)
    {
        if (!parteMovel.TryGetValue(idDaPeca, out var movel)
            || movel.RenderTransform is not TransformGroup grupo || grupo.Children.Count < 4)
            return (0, 0, 0);

        var anda = (TranslateTransform)grupo.Children[0];
        var gira = (RotateTransform)grupo.Children[1];
        return (gira.Angle, anda.X, anda.Y);
    }

    /// <summary>Para o teste: altura de uma das oito chaves do DIP.</summary>
    internal double ChaveDipParaTeste(string idDaPeca, int indice)
        => chavesDip.TryGetValue(idDaPeca, out var dip)
           && indice >= 0 && indice < dip.botoes.Length
            ? Canvas.GetTop(dip.botoes[indice]) : double.NaN;

    /// <summary>Para o teste: quantos graus a peca esta inclinada.</summary>
    internal double InclinacaoParaTeste(string idDaPeca)
        => visuais.TryGetValue(idDaPeca, out var v)
           && v.RenderTransform is TransformGroup g && g.Children.Count == 3
           && g.Children[2] is RotateTransform r ? r.Angle : 0;

    /// <summary>O giro e o espelho da PECA na parte movel.</summary>
    void PosicionarMovel(PecaNoCircuito peca, PecaDef def)
    {
        if (!parteMovel.TryGetValue(peca.Id, out var movel)) return;

        Canvas.SetLeft(movel, peca.X);
        Canvas.SetTop(movel, peca.Y);

        if (movel.RenderTransform is not TransformGroup grupo || grupo.Children.Count < 4) return;

        var (largura, altura) = Geometria.Tamanho(peca, def);
        var espelho = (ScaleTransform)grupo.Children[2];
        espelho.ScaleX = peca.Espelhada ? -1 : 1;
        espelho.CenterX = largura / 2;
        espelho.CenterY = altura / 2;

        var giroDaPeca = (RotateTransform)grupo.Children[3];
        giroDaPeca.Angle = peca.Rotacao;
        giroDaPeca.CenterX = largura / 2;
        giroDaPeca.CenterY = altura / 2;
    }

    /// <summary>Onde a parte movel esta agora, pelo estado da peca.</summary>
    void AnimarParteMovel(PecaNoCircuito peca, PecaDef def)
    {
        if (!parteMovel.TryGetValue(peca.Id, out var movel)) return;
        if (!Animadas.TryGetValue(peca.Tipo, out var jeito)) return;
        if (movel.RenderTransform is not TransformGroup grupo || grupo.Children.Count < 4) return;

        var anda = (TranslateTransform)grupo.Children[0];
        var gira = (RotateTransform)grupo.Children[1];
        gira.CenterX = Geometria.Tamanho(peca, def).Largura * jeito.EixoX;
        gira.CenterY = Geometria.Tamanho(peca, def).Altura * jeito.EixoY;

        switch (peca.Tipo)
        {
            // 270 graus de curso, de -135 a +135, como o de verdade.
            case "potentiometer":
                gira.Angle = -135 + 270 * peca.Giro;
                break;

            // O encoder nao tem batente: uma volta inteira por curso.
            case "ky-040":
                gira.Angle = 360 * peca.Giro;
                break;

            // O disco de telefone volta girando, e e VOLTANDO que ele
            // manda os pulsos -- cada furo, 30 graus.
            case "rotary-dialer":
                gira.Angle = discagens.TryGetValue(peca.Id, out var discando)
                    ? discando.Angulo : 0;
                break;

            case "stepper-motor":
            case "motor-28byj48":
                gira.Angle = anguloPorDriver.TryGetValue(peca.Id, out double peloDriver) ? peloDriver
                    : eixosDePasso.TryGetValue(peca.Id, out var eixos) && eixos.Length > 0 ? eixos[0].Angulo : 0;
                break;

            case "motor-dc":
            case "motor-tt":
            case "motor-brushless":
                gira.Angle = AnguloDoEixo(peca);
                break;

            case "slide-switch":
                anda.X = peca.Giro > 0.5 ? jeito.CursoX : 0;
                break;

            case "servo-360":
                gira.Angle = AnguloDoServo(peca);
                break;

            // Encaixada num eixo, gira com ele; a helice rapida vira um borrao.
            case "roda-tt":
            case "helice-130":
            case "helice-1045":
            case "helice-5045":
            {
                var motor = MotorDoAcessorio(peca);
                gira.Angle = motor is null ? 0 : AnguloDoMotor(motor);
                double opacidade = motor is not null && peca.Tipo != "roda-tt" && Math.Abs(RpmDoEixo(motor)) > 400 ? 0.45 : 1;
                if (Math.Abs(movel.Opacity - opacidade) > 0.01) movel.Opacity = opacidade;
                break;
            }

            // Apertada, a alavanca desce ate encostar no pino do botao.
            case "micro-switch":
                gira.Angle = BotoesPressionados.Contains(peca.Id) ? 7 : 0;
                break;

            // Tirado, o pendrive fica fora do conector, meio apagado.
            case "pendrive-usb":
                anda.X = peca.Giro > 0.5 ? jeito.CursoX : 0;
                movel.Opacity = peca.Giro > 0.5 ? 0.45 : 1;
                break;

            case "slide-potentiometer":
                anda.X = jeito.InicioX + jeito.CursoX * peca.Giro;
                break;

            case "analog-joystick":
                anda.X = (peca.Giro - 0.5) * jeito.CursoX;
                // Y do controle para cima: o valor sobe, o manche sobe.
                anda.Y = -(peca.Giro2 - 0.5) * jeito.CursoY;
                break;
        }
    }

    void CriarLedsDaPlaca(PecaNoCircuito peca, PecaDef def)
    {
        var luzes = new List<Ellipse>();
        foreach (var (lx, ly, raio, _, cor) in LedsDaPlaca)
        {
            var luz = new Ellipse
            {
                Width = raio * 2,
                Height = raio * 2,
                Fill = new SolidColorBrush(cor),
                IsHitTestVisible = false,
                Opacity = 0,
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = cor, BlurRadius = 9, ShadowDepth = 0, Opacity = 0.9
                }
            };
            Panel.SetZIndex(luz, 145);
            mundo.Children.Add(luz);
            luzes.Add(luz);
        }
        ledsDePlaca[peca.Id] = luzes;
        PosicionarLedsDaPlaca(peca, def);
    }

    void PosicionarLedsDaPlaca(PecaNoCircuito peca, PecaDef def)
    {
        if (!ledsDePlaca.TryGetValue(peca.Id, out var luzes)) return;
        for (int i = 0; i < luzes.Count && i < LedsDaPlaca.Length; i++)
        {
            var (lx, ly, raio, _, _) = LedsDaPlaca[i];
            var p = Geometria.PosicaoLocal(peca, def, lx, ly);
            Canvas.SetLeft(luzes[i], p.X - raio);
            Canvas.SetTop(luzes[i], p.Y - raio);
        }
    }

    /// <summary>
    /// Uma celula de texto por caractere, na grade real do display. O
    /// editor novo tinha perdido isso: o LCD aparecia, mas nunca
    /// mostrava o que o sketch escrevia.
    /// </summary>
    // ================= display TFT =================

    /// <summary>Imagem da tela e o texto por cima, por peca.</summary>
    readonly Dictionary<string, (Image imagem, Canvas letras)> telasTft = [];

    /// <summary>Versao ja desenhada, para nao refazer o quadro a toa.</summary>
    readonly Dictionary<string, int> versaoDaTft = [];

    /// <summary>
    /// A area util do ILI9341 dentro do desenho da peca.
    ///
    /// A placa tem borda e conector: a tela nao ocupa o retangulo
    /// inteiro. Estes numeros saem do proprio SVG.
    /// </summary>
    /// <summary>
    /// A area de vidro de cada display, em fracao da caixa da peca.
    ///
    /// Sao numeros tirados do proprio SVG: a placa tem borda e conector,
    /// e a tela nao ocupa o retangulo inteiro.
    /// </summary>
    static readonly Dictionary<string, (double X, double Y, double Larg, double Alt)>
        AreasDeTela = new()
    {
        ["ili9341"] = (0.034, 0.065, 0.937, 0.816),
        ["st7789"] = (0.1625, 0.2208, 0.675, 0.7013),

        // O OLED: o retangulo de 128x64 de cada desenho (o SVG do de 8 pinos
        // tem a tela em 11.4,26 de 150x116; o I2C, a area acesa em 2.8,9.7
        // de 27.3x27.8 mm).
        ["ssd1306"] = (0.076, 0.2241, 0.8533, 0.5517),
        ["ssd1306-i2c"] = (0.1026, 0.3489, 0.7949, 0.3921),
    };

    void CriarTelaTft(PecaNoCircuito peca, PecaDef def)
    {
        var imagem = new Image
        {
            Width = def.Largura * AreasDeTela[peca.Tipo].Larg,
            Height = def.Altura * AreasDeTela[peca.Tipo].Alt,
            Stretch = Stretch.Fill,
            IsHitTestVisible = false,
            // Pixel de 240x320 mostrado em ~150 px: sem isto o WPF
            // borra tudo e o desenho do aluno vira mancha.
            SnapsToDevicePixels = true
        };
        RenderOptions.SetBitmapScalingMode(imagem, BitmapScalingMode.NearestNeighbor);

        var letras = new Canvas
        {
            Width = imagem.Width,
            Height = imagem.Height,
            IsHitTestVisible = false,
            ClipToBounds = true
        };

        Panel.SetZIndex(imagem, 300);
        Panel.SetZIndex(letras, 301);
        mundo.Children.Add(imagem);
        mundo.Children.Add(letras);
        telasTft[peca.Id] = (imagem, letras);

        PosicionarTft(peca, def);
    }

    void PosicionarTft(PecaNoCircuito peca, PecaDef def)
    {
        if (!telasTft.TryGetValue(peca.Id, out var tela)) return;

        var area = AreasDeTela[peca.Tipo];
        var canto = Geometria.PosicaoLocal(
            peca, def, def.Largura * area.X, def.Altura * area.Y);

        foreach (FrameworkElement elemento in new FrameworkElement[]
                 { tela.imagem, tela.letras })
        {
            Canvas.SetLeft(elemento, canto.X);
            Canvas.SetTop(elemento, canto.Y);
            elemento.RenderTransform = new RotateTransform(peca.Rotacao, 0, 0);
        }
    }

    /// <summary>
    /// Passa o buffer de pixels para o vidro. O texto ja esta nos pixels,
    /// com a fonte da biblioteca -- o que o sketch apagou com fillRect
    /// sumiu, como na placa.
    ///
    /// Por cima vai a camada da luz: a luz de fundo ligada num pino segue
    /// o pino (apagada com ele em LOW ou solto, meia luz no PWM), e o
    /// controlador desligado (DISPOFF, SLPIN) mostra o branco do painel
    /// sem imagem.
    /// </summary>
    void DesenharTft(PecaNoCircuito peca, TelaGrafica tela, double luz)
    {
        if (tela is null || !telasTft.TryGetValue(peca.Id, out var alvo)) return;

        AjustarLuzDaTft(alvo.letras, tela.Mostrando, luz);

        if (versaoDaTft.GetValueOrDefault(peca.Id, -1) == tela.Versao) return;
        versaoDaTft[peca.Id] = tela.Versao;

        if (alvo.imagem.Source is not WriteableBitmap mapa
            || mapa.PixelWidth != tela.LarguraFisica
            || mapa.PixelHeight != tela.AlturaFisica)
        {
            // O painel nao muda de tamanho quando a tela gira: quem gira
            // e a conta que leva o ponto do sketch ate o pixel.
            mapa = new WriteableBitmap(tela.LarguraFisica, tela.AlturaFisica,
                                       96, 96, PixelFormats.Bgra32, null);
            alvo.imagem.Source = mapa;
        }

        int[] pontos = tela.Pixels;
        if (tela.Invertida)
        {
            pontos = (int[])tela.Pixels.Clone();
            for (int i = 0; i < pontos.Length; i++) pontos[i] ^= 0x00FFFFFF;
        }

        mapa.WritePixels(new Int32Rect(0, 0, tela.LarguraFisica, tela.AlturaFisica),
                         pontos, tela.LarguraFisica * 4, 0);
    }

    /// <summary>
    /// O OLED: o que o ultimo display() mandou, com a rolagem do painel (que
    /// anda sozinha, e por isso redesenha mesmo sem mudanca). Sem begin(),
    /// ou sem energia, o vidro fica preto -- OLED nao tem luz de fundo.
    /// </summary>
    void DesenharOled(PecaNoCircuito peca, Maquina maq, Netlist rede)
    {
        if (!telasTft.TryGetValue(peca.Id, out var alvo)) return;
        var painel = PainelDaPeca(peca, maq, rede);
        bool aceso = painel is { Mostrando: true } && maq.Energizada;
        AjustarLuzDaTft(alvo.letras, aceso, aceso ? (painel.Escurecido ? 0.45 : 1) : 0);
        if (painel is null) return;

        if (!painel.Rolando && versaoDaTft.GetValueOrDefault(peca.Id, -1) == painel.Versao) return;
        versaoDaTft[peca.Id] = painel.Versao;

        if (alvo.imagem.Source is not WriteableBitmap mapa
            || mapa.PixelWidth != painel.LarguraFisica || mapa.PixelHeight != painel.AlturaFisica)
        {
            mapa = new WriteableBitmap(painel.LarguraFisica, painel.AlturaFisica, 96, 96, PixelFormats.Bgra32, null);
            alvo.imagem.Source = mapa;
        }

        int[] pontos = painel.QuadroVisivel(maq.AgoraUsLido / 1000);
        if (painel.Invertida)
        {
            pontos = (int[])pontos.Clone();
            for (int i = 0; i < pontos.Length; i++)
                pontos[i] = pontos[i] == painel.CorAcesa ? TelaGrafica.CorApagada : painel.CorAcesa;
        }
        mapa.WritePixels(new Int32Rect(0, 0, painel.LarguraFisica, painel.AlturaFisica), pontos, painel.LarguraFisica * 4, 0);
    }

    /// <summary>
    /// O vidro desta peca: o display do sketch no barramento em que o SDA e
    /// o SCL dela chegam, no endereco dela (0x3C). O OLED de I2C ligado em
    /// outros pinos fica preto -- como na bancada. O de SPI mostra o display
    /// de SPI do sketch.
    /// </summary>
    TelaGrafica PainelDaPeca(PecaNoCircuito peca, Maquina maq, Netlist rede)
    {
        lock (maq.PaineisOled)
        {
            if (peca.Tipo == "ssd1306") return maq.PaineisOled.FirstOrDefault(p => p.Spi).Painel;
            int sda = rede.GpioDe(Atual, peca.Id, "SDA"), scl = rede.GpioDe(Atual, peca.Id, "SCL");
            foreach (var (barramento, endereco, spi, painel) in maq.PaineisOled)
                if (!spi && endereco == 0x3C && maq.I2cNumero(barramento) is { } bus && NoFioDoBarramento(bus, sda, scl))
                    return painel;
            return null;
        }
    }

    static bool NoFioDoBarramento(BarramentoI2c bus, int sda, int scl)
        => bus.Ligado && sda >= 0 && bus.Sda == sda && bus.Scl == scl;

    /// <summary>A camada da luz: nada com o vidro aceso, preto com ele apagado.</summary>
    static void AjustarLuzDaTft(Canvas camada, bool mostrando, double luz)
    {
        luz = Math.Clamp(luz, 0, 1);

        // Sem imagem, o painel e branco na luz que tiver; com imagem, a
        // luz baixa escurece o desenho.
        Color cor = mostrando ? Colors.Black : Color.FromRgb((byte)(255 * luz), (byte)(255 * luz), (byte)(255 * luz));
        double opacidade = mostrando ? 1 - luz : 1;

        if (camada.Children.Count == 0)
            camada.Children.Add(new System.Windows.Shapes.Rectangle
            {
                Width = camada.Width,
                Height = camada.Height,
                IsHitTestVisible = false,
            });

        var vidro = (System.Windows.Shapes.Rectangle)camada.Children[0];
        if (vidro.Fill is not SolidColorBrush pincel || pincel.Color != cor) vidro.Fill = new SolidColorBrush(cor);
        if (Math.Abs(vidro.Opacity - opacidade) > 0.001) vidro.Opacity = opacidade;
        var visivel = opacidade < 0.004 ? Visibility.Collapsed : Visibility.Visible;
        if (vidro.Visibility != visivel) vidro.Visibility = visivel;
    }

    /// <summary>
    /// Quanto a luz de fundo do display acende. O pino LED (BLK no ST7789)
    /// ligado num GPIO segue o GPIO: HIGH acende, LOW e o pino solto
    /// apagam, e o PWM da meia luz. Ligado no 3V3 -- ou sem fio, como os
    /// circuitos antigos --, fica aceso. Sem energia, apaga.
    /// </summary>
    double LuzDaTft(PecaNoCircuito peca, Maquina maq, Netlist rede)
    {
        if (!maq.Energizada) return 0;
        int gpio = rede.GpioDe(Atual, peca.Id, peca.Tipo == "st7789" ? "BLK" : "LED");
        if (gpio < 0 || gpio >= maq.Modo.Length) return 1;
        if (maq.Modo[gpio] != 1) return 0;
        return maq.Pwm[gpio] / 255.0;
    }

    /// <summary>
    /// Opacidade do brilho de um LED, para o teste conferir de fora.
    ///
    /// E o que a pessoa ve: conferir maq.Pwm provaria que a maquina
    /// esta certa, nao que o LED certo acendeu.
    /// </summary>
    internal double BrilhoParaTeste(string idDaPeca)
        => brilhos.TryGetValue(idDaPeca, out var luz) ? luz.Opacity : -1;

    /// <summary>Quantos pontos o vidro de um display mostra acesos (o OLED acende num branco frio, e nao 0xFFFFFF).</summary>
    internal int AcesosNaTelaParaTeste(string idDaPeca)
    {
        if (!telasTft.TryGetValue(idDaPeca, out var alvo) || alvo.imagem.Source is not WriteableBitmap mapa) return 0;
        var pontos = new int[mapa.PixelWidth * mapa.PixelHeight];
        mapa.CopyPixels(pontos, mapa.PixelWidth * 4, 0);
        return pontos.Count(p => ((p >> 16) & 0xFF) + ((p >> 8) & 0xFF) + (p & 0xFF) > 3 * 128);
    }

    /// <summary>Estado da tela TFT desenhada, para o teste conferir.</summary>
    /// <param name="brancos">
    /// Quantos pontos brancos o vidro mostra -- e onde o texto (branco, a
    /// cor padrao) aparece, agora que ele e pixel.
    /// </param>
    internal (bool criada, bool comImagem, int largura, int altura, int brancos, double escuro)
        TftParaTeste(string idDaPeca)
    {
        if (!telasTft.TryGetValue(idDaPeca, out var alvo))
            return (false, false, 0, 0, 0, 0);

        var mapa = alvo.imagem.Source as WriteableBitmap;
        int brancos = 0;
        if (mapa is not null)
        {
            var pontos = new int[mapa.PixelWidth * mapa.PixelHeight];
            mapa.CopyPixels(pontos, mapa.PixelWidth * 4, 0);
            brancos = pontos.Count(p => (p & 0x00FFFFFF) == 0x00FFFFFF);
        }

        // O quanto a camada da luz escurece o vidro: 0 aceso, 1 apagado.
        double escuro = alvo.letras.Children.Count > 0
                        && alvo.letras.Children[0] is System.Windows.Shapes.Rectangle { Visibility: Visibility.Visible } vidro
                        && vidro.Fill is SolidColorBrush { Color: var cor } && cor == Colors.Black
            ? vidro.Opacity : 0;

        return (true, mapa is not null, mapa?.PixelWidth ?? 0, mapa?.PixelHeight ?? 0, brancos, escuro);
    }

    void CriarTelaLcd(PecaNoCircuito peca, GradeLcd g)
    {
        var celulas = new TextBlock[g.Linhas, g.Colunas];

        for (int lin = 0; lin < g.Linhas; lin++)
        {
            for (int col = 0; col < g.Colunas; col++)
            {
                var tb = new TextBlock
                {
                    Text = " ",
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = g.Fonte,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x10, 0x28, 0x08)),
                    Width = g.Larg,
                    TextAlignment = TextAlignment.Center,
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(tb, peca.X + g.X + col * g.Larg);
                Canvas.SetTop(tb, peca.Y + g.Y + lin * g.Alt - g.Fonte * 0.08);
                Panel.SetZIndex(tb, 130);
                mundo.Children.Add(tb);
                celulas[lin, col] = tb;
            }
        }
        telasLcd[peca.Id] = celulas;
    }

    /// <summary>
    /// Braco do servo desenhado por cima: o SVG do wokwi nao marca o
    /// horn com id, entao nao da para recortar e girar so ele.
    /// </summary>
    /// <summary>
    /// O eixo de cada servo (pixels da peca) e o tamanho do braco. O do SG90
    /// vem do SVG do Wokwi; o do MG996R, do pecas-servos.py.
    /// </summary>
    static readonly Dictionary<string, (double X, double Y, double Largura, double Altura)> BracosDeServo = new()
    {
        ["servo"] = (91.4, 60.0, 58, 6),
        ["servo-mg996r"] = (181.4, 49.1, 84, 8.5),
    };

    void CriarBracoServo(PecaNoCircuito peca, PecaDef def)
    {
        var medidas = BracosDeServo[peca.Tipo];
        var braco = new Rectangle
        {
            Width = medidas.Largura,
            Height = medidas.Altura,
            RadiusX = medidas.Altura / 2.4,
            RadiusY = medidas.Altura / 2.4,
            Fill = new SolidColorBrush(Color.FromRgb(0xF2, 0xF2, 0xF2)),
            Stroke = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
            StrokeThickness = 0.6,
            IsHitTestVisible = false,
            RenderTransformOrigin = new Point(0.08, 0.5),
            RenderTransform = new RotateTransform(0)
        };
        Panel.SetZIndex(braco, 140);
        mundo.Children.Add(braco);
        bracosServo[peca.Id] = braco;
        PosicionarBracoServo(peca, def);
    }

    // Centro do horn, lido do proprio SVG: <circle cx="91.467" cy="59.773">
    // no viewBox 170,08 x 119,55, que a caixa de 170x120 reproduz quase 1:1.
    const double ServoEixoX = 91.4, ServoEixoY = 60.0;

    void PosicionarBracoServo(PecaNoCircuito peca, PecaDef def)
    {
        if (!bracosServo.TryGetValue(peca.Id, out var braco)) return;
        var (ex, ey, _, _) = BracosDeServo.GetValueOrDefault(peca.Tipo, (ServoEixoX, ServoEixoY, 58, 6));
        var eixo = Geometria.PosicaoLocal(peca, def, ex, ey);
        Canvas.SetLeft(braco, eixo.X - braco.Width * 0.08);
        Canvas.SetTop(braco, eixo.Y - braco.Height / 2);
    }

    /// <summary>
    /// Protoboard no fundo, pecas por cima. E o que permite encaixar
    /// componentes sobre a placa sem brigar pelo clique.
    /// </summary>
    static int ZDe(string tipo) => Catalogo.EhProtoboard(tipo) ? 10 : EhAcessorio(tipo) ? 120 : 100;

    static void Posicionar(FrameworkElement visual, PecaNoCircuito peca)
    {
        Canvas.SetLeft(visual, peca.X);
        Canvas.SetTop(visual, peca.Y);
        visual.RenderTransformOrigin = new Point(0.5, 0.5);

        if (peca.Rotacao == 0 && !peca.Espelhada) { visual.RenderTransform = null; return; }

        var grupo = new TransformGroup();
        if (peca.Espelhada) grupo.Children.Add(new ScaleTransform(-1, 1));
        if (peca.Rotacao != 0) grupo.Children.Add(new RotateTransform(peca.Rotacao));
        visual.RenderTransform = grupo;
    }

    /// <summary>
    /// Quanto dura o giro de uma peca.
    ///
    /// Curto de proposito. A animacao existe para o olho ACOMPANHAR a
    /// peca ate a posicao nova -- num salto seco, quem girou tem que
    /// reconstruir sozinho o que aconteceu. Mais do que isto ja vira
    /// espera.
    /// </summary>
    static readonly Duration TempoDoGiro = new(TimeSpan.FromMilliseconds(160));

    /// <summary>
    /// Anima a peca do angulo velho ate o novo.
    ///
    /// So o DESENHO anda: os pinos, os fios e a area de clique ja estao
    /// no lugar novo desde o primeiro quadro. Animar a logica junto
    /// deixaria o circuito inconsistente durante a animacao -- um fio
    /// ligado num pino que ainda nao chegou.
    /// </summary>
    static void AnimarGiro(FrameworkElement visual, double de, double para)
    {
        if (visual?.RenderTransform is not TransformGroup grupo) return;

        var giro = grupo.Children.OfType<RotateTransform>().FirstOrDefault();
        if (giro is null) return;

        // Pelo caminho curto: de 270 para 0 e um quarto de volta para a
        // frente, e nao tres quartos para tras.
        double volta = para;
        if (Math.Abs(para - de) > 180) volta = para > de ? para - 360 : para + 360;

        var animacao = new System.Windows.Media.Animation.DoubleAnimation
        {
            From = de,
            To = volta,
            Duration = TempoDoGiro,
            EasingFunction = new System.Windows.Media.Animation.CubicEase
            {
                EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut
            },
            FillBehavior = System.Windows.Media.Animation.FillBehavior.Stop,
        };

        // Ao terminar, o valor fica sendo o da peca: a animacao so
        // empresta o angulo durante a viagem.
        animacao.Completed += (_, _) => giro.Angle = para;
        giro.BeginAnimation(RotateTransform.AngleProperty, animacao);
    }

    /// <summary>Sombra: separa a peca do fundo e do que estiver embaixo.</summary>
    static void AplicarSombra(FrameworkElement visual)
        => visual.Effect = new System.Windows.Media.Effects.DropShadowEffect
        {
            Color = Colors.Black,
            BlurRadius = 9,
            ShadowDepth = 2.5,
            Direction = 300,
            Opacity = 0.55
        };

    void PosicionarBrilho(PecaNoCircuito peca, PecaDef def)
    {
        if (!brilhos.TryGetValue(peca.Id, out var luz)) return;
        // O bulbo do LED fica no topo, em torno de 30% da altura.
        var centro = Geometria.PosicaoLocal(peca, def, def.Largura / 2, def.Altura * 0.30);
        Canvas.SetLeft(luz, centro.X - luz.Width / 2);
        Canvas.SetTop(luz, centro.Y - luz.Height / 2);
    }

    // ---------------- protoboard ----------------
    /// <summary>Cores em que as mini de 170 sao vendidas.</summary>
    internal static readonly (string id, string nome, string hex)[] CoresDeProtoboard =
    [
        ("branca", "Branca", "#F0EFE9"), ("vermelha", "Vermelha", "#D94A4A"),
        ("azul", "Azul", "#3E6FD0"), ("verde", "Verde", "#3FA35C"),
        ("amarela", "Amarela", "#E8C33A"), ("preta", "Preta", "#3A3A3C"),
    ];

    static Color CorDaProtoboard(PecaNoCircuito peca)
    {
        var achada = CoresDeProtoboard.FirstOrDefault(c => c.id == peca.Cor);
        string hex = achada.hex ?? CoresDeProtoboard[0].hex;
        return (Color)ColorConverter.ConvertFromString(hex);
    }

    /// <summary>Escura o bastante para pedir furo e rotulo claros?</summary>
    static bool Escura(Color c) => (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) < 140;

    /// <summary>Mesma cor mais clara ou mais escura, para o vao do canal.</summary>
    static Color Puxar(Color c, double fator) => Color.FromRgb(
        (byte)Math.Clamp(c.R * fator, 0, 255),
        (byte)Math.Clamp(c.G * fator, 0, 255),
        (byte)Math.Clamp(c.B * fator, 0, 255));

    // ---------------- PCB perfurada ----------------
    /// <summary>As cores da mascara de solda (e a fenolite, a marrom sem mascara).</summary>
    internal static readonly (string id, string nome, string hex)[] CoresDePcb =
    [
        ("verde", "Verde", "#1E6B3C"), ("azul", "Azul", "#1F4F9E"), ("preta", "Preta", "#1C1D20"),
        ("vermelha", "Vermelha", "#A3242A"), ("roxa", "Roxa", "#5B2A86"), ("fenolite", "Fenolite", "#C8955A"),
    ];

    FrameworkElement DesenharPcb(PecaNoCircuito peca, PecaDef def)
    {
        var (colunas, linhas) = Catalogo.TamanhoDaPcb(peca.Tipo)!.Value;
        double P = Catalogo.Passo, m = P;
        string hex = CoresDePcb.FirstOrDefault(c => c.id == peca.Cor).hex ?? CoresDePcb[0].hex;
        var mascara = (Color)ColorConverter.ConvertFromString(hex);
        bool fenolite = peca.Cor == "fenolite";
        // Virada: o lado de baixo, um tom mais escuro.
        if (peca.Virada) mascara = Puxar(mascara, 0.78);
        var caixa = new Canvas { Width = def.Largura, Height = def.Altura };

        caixa.Children.Add(new Rectangle
        {
            Width = def.Largura, Height = def.Altura, RadiusX = 2, RadiusY = 2,
            Fill = new SolidColorBrush(mascara),
            Stroke = new SolidColorBrush(Puxar(mascara, Escura(mascara) ? 1.6 : 0.8)), StrokeThickness = 1,
        });

        // As ilhas de cobre (estanhadas, prateadas; na fenolite, o cobre
        // nu) e o furo no meio de cada uma: dois Paths para a placa inteira.
        var ilhas = new GeometryGroup();
        var furos = new GeometryGroup();
        foreach (var pino in def.Pinos)
        {
            ilhas.Children.Add(new EllipseGeometry(new Point(pino.X, pino.Y), 3.1, 3.1));
            furos.Children.Add(new EllipseGeometry(new Point(pino.X, pino.Y), 1.15, 1.15));
        }
        caixa.Children.Add(new Path
        {
            Data = ilhas,
            Fill = new SolidColorBrush(fenolite ? Color.FromRgb(0xD9, 0x8E, 0x4A) : Color.FromRgb(0xC9, 0xCC, 0xD1)),
            Stroke = new SolidColorBrush(fenolite ? Color.FromRgb(0x8A, 0x52, 0x20) : Color.FromRgb(0x80, 0x85, 0x8C)),
            StrokeThickness = 0.35,
        });
        caixa.Children.Add(new Path { Data = furos, Fill = new SolidColorBrush(Color.FromRgb(0x16, 0x16, 0x18)) });

        // As soldas das pernas (so do lado de baixo): estanho por cima do furo.
        var soldas = SoldasDaPcb(peca, def);
        if (soldas.Children.Count > 0)
            caixa.Children.Add(new Path
            {
                Data = soldas,
                Fill = new RadialGradientBrush(Colors.White, Color.FromRgb(0x8E, 0x94, 0x9C))
                {
                    GradientOrigin = new Point(0.35, 0.3), Center = new Point(0.45, 0.4), RadiusX = 0.7, RadiusY = 0.7,
                },
                Stroke = new SolidColorBrush(Color.FromRgb(0x3E, 0x43, 0x4A)), StrokeThickness = 0.7,
            });

        // A serigrafia: a numeracao das colunas e das linhas, de 5 em 5.
        var corDaSerigrafia = fenolite ? Color.FromRgb(0x5A, 0x34, 0x12) : Color.FromRgb(0xE6, 0xE8, 0xEA);
        // Com a placa espelhada (virada), cada numero e desespelhado no lugar:
        // a serigrafia continua legivel.
        void Numero(string texto, double x, double y)
        {
            var rotulo = Rotulo(texto, x, y, corDaSerigrafia);
            if (peca.Espelhada)
            {
                rotulo.RenderTransformOrigin = new Point(0.5, 0.5);
                rotulo.RenderTransform = new ScaleTransform(-1, 1);
            }
            caixa.Children.Add(rotulo);
        }
        for (int c = 0; c < colunas; c += 5) Numero((c + 1).ToString(), m + c * P - 2.2, 0.2);
        for (int l = 4; l < linhas; l += 5) Numero((l + 1).ToString(), 0.6, m + l * P - 3.2);
        return caixa;
    }

    /// <summary>O tamanho da PCB selecionada, em furos: troca o tipo da peca (o tamanho e o tipo).</summary>
    public void DefinirTamanhoDaPcb(int colunas, int linhas)
    {
        var peca = Selecionada;
        if (peca is null || !Catalogo.EhPcb(peca.Tipo)) return;
        string novo = Catalogo.IdDaPcb(colunas, linhas);
        if (novo == peca.Tipo) return;
        peca.Tipo = novo;
        Redesenhar();
        Selecionar(peca.Id, false);
        Mudou?.Invoke();
    }

    /// <summary>A cor da mascara de solda da PCB selecionada.</summary>
    public void DefinirCorDaPcb(string cor)
    {
        var peca = Selecionada;
        if (peca is null || !Catalogo.EhPcb(peca.Tipo)) return;
        peca.Cor = cor;
        Redesenhar();
        Selecionar(peca.Id, false);
        Mudou?.Invoke();
    }

    static TextBlock Rotulo(string txt, double x, double y, Color cor)
    {
        var tb = new TextBlock
        {
            Text = txt,
            FontSize = 4.6,
            FontFamily = new FontFamily("Segoe UI"),
            Foreground = new SolidColorBrush(cor)
        };
        Canvas.SetLeft(tb, x);
        Canvas.SetTop(tb, y);
        return tb;
    }

    // ---------------- resistor com faixas ----------------
    static readonly Color[] FaixaDigito =
    [
        Color.FromRgb(0x1A,0x1A,0x1A), Color.FromRgb(0x8B,0x4A,0x1B), Color.FromRgb(0xD1,0x2B,0x2B),
        Color.FromRgb(0xE8,0x7C,0x1E), Color.FromRgb(0xF2,0xD0,0x21), Color.FromRgb(0x2E,0xA8,0x4A),
        Color.FromRgb(0x2B,0x59,0xBF), Color.FromRgb(0x8A,0x4F,0xBE), Color.FromRgb(0x88,0x88,0x88),
        Color.FromRgb(0xF2,0xF2,0xF2),
    ];

    static readonly Color Dourado = Color.FromRgb(0xC9, 0xA2, 0x27);

    /// <summary>
    /// Quatro faixas, como no resistor comum: dois digitos, o
    /// multiplicador e a tolerancia (dourado = 5%).
    /// </summary>
    public static Color[] FaixasDe(double ohms)
    {
        if (ohms < 1) ohms = 1;
        int expoente = 0;
        double v = ohms;
        while (v >= 100) { v /= 10; expoente++; }

        int dois = Math.Clamp((int)Math.Round(v), 10, 99);
        return
        [
            FaixaDigito[Math.Clamp(dois / 10, 0, 9)],
            FaixaDigito[Math.Clamp(dois % 10, 0, 9)],
            FaixaDigito[Math.Clamp(expoente, 0, 9)],
            Dourado,
        ];
    }

    /// <summary>
    /// Resistor com as faixas do valor real. O SVG traz faixas fixas;
    /// aqui elas passam a refletir os ohms escolhidos.
    /// </summary>
    static FrameworkElement DesenharResistor(PecaNoCircuito peca, PecaDef def)
    {
        var caixa = new Canvas { Width = def.Largura, Height = def.Altura };
        caixa.Children.Add(new Image
        {
            Source = Pecas.Obter("resistor"),
            Width = def.Largura,
            Height = def.Altura,
            Stretch = Stretch.Uniform
        });

        // As tres primeiras juntas, a de tolerancia afastada -- e assim
        // que se sabe por qual ponta comecar a ler.
        var faixas = FaixasDe(peca.Ohms);
        double largura = 2.6, passo = 3.8;
        double x0 = def.Largura * 0.30;

        for (int i = 0; i < faixas.Length; i++)
        {
            double x = i < 3 ? x0 + i * passo : x0 + 2 * passo + passo * 2.6;
            var barra = new Rectangle
            {
                Width = largura,
                Height = def.Altura * 0.62,
                RadiusX = 0.6,
                RadiusY = 0.6,
                Fill = new SolidColorBrush(faixas[i]),
                IsHitTestVisible = false
            };
            Canvas.SetLeft(barra, x);
            Canvas.SetTop(barra, def.Altura * 0.19);
            caixa.Children.Add(barra);
        }
        return caixa;
    }

    static string NomeSvg(PecaNoCircuito peca, bool ligado)
    {
        if (peca.Tipo == "led") return $"led-{peca.Cor}-{(ligado ? "on" : "off")}";
        if (peca.Tipo == "pushbutton") return ligado ? "pushbutton-pressionado" : "pushbutton-solto";
        if (EhEsc(peca.Tipo) && (peca.Amperes > 0 || peca.Acabamento is not null)) return NomeSvgDoEsc(peca);
        if (Reguladores.EhRegulador(peca.Tipo)) return NomeSvgDoRegulador(peca);
        if (Diodos.EhDiodo(peca.Tipo)) return Diodos.Da(peca).Svg ?? peca.Tipo;
        if (Semicondutores.TemModelo(peca.Tipo)) return Semicondutores.Svg(peca);
        return peca.Tipo;
    }

    // ================= fios =================

    /// <summary>Quanto cada fio se afasta do vizinho que ele cobria.</summary>
    const double SeparacaoFio = 5.0;

    readonly Dictionary<int, double> desviosDeFio = [];

    /// <summary>
    /// Fio que cobre outro sai de cima dele.
    ///
    /// Dois fios entre os mesmos dois pinos desenhavam exatamente o mesmo
    /// traço: viravam um só na tela, e apagar "o de cima" apagava o que a
    /// pessoa nao estava vendo. O sombreado ajudou a perceber, mas nao
    /// resolveu -- eles continuavam empilhados.
    ///
    /// Vale para "reto" e "degrau". O "solto" fica de fora de proposito:
    /// a curva dele ja depende da distancia entre os pinos, entao dois
    /// fios soltos entre os mesmos pinos ja caem por caminhos diferentes.
    ///
    /// Fio com dobra tambem fica de fora: quem moldou o fio a mao decide
    /// por onde ele passa, e empurrar seria desmanchar o trabalho.
    /// </summary>
    void CalcularDesvios()
    {
        desviosDeFio.Clear();

        // Agrupa por rota: mesmas duas pontas, na mesma ordem ou trocadas.
        var grupos = new Dictionary<(int, int, int, int, string), List<int>>();

        for (int i = 0; i < Atual.Fios.Count; i++)
        {
            var fio = Atual.Fios[i];
            if (fio.Estilo != Fio.EstiloReto && fio.Estilo != Fio.EstiloDegrau) continue;
            if (fio.Dobras.Count > 0) continue;

            var a = PosicaoPino(fio.DePeca, fio.DePino);
            var b = PosicaoPino(fio.ParaPeca, fio.ParaPino);
            if (a is null || b is null) continue;

            var p1 = ((int)Math.Round(a.Value.X), (int)Math.Round(a.Value.Y));
            var p2 = ((int)Math.Round(b.Value.X), (int)Math.Round(b.Value.Y));
            if (p2.CompareTo(p1) < 0) (p1, p2) = (p2, p1);

            var chave = (p1.Item1, p1.Item2, p2.Item1, p2.Item2, fio.Estilo);
            if (!grupos.TryGetValue(chave, out var lista)) grupos[chave] = lista = [];
            lista.Add(i);
        }

        foreach (var lista in grupos.Values)
        {
            if (lista.Count < 2) continue;

            // Espalha em torno do lugar original: com dois, um sobe e o
            // outro desce a mesma distancia, e nenhum "muda de lugar"
            // mais que o outro.
            for (int k = 0; k < lista.Count; k++)
                desviosDeFio[lista[k]] = (k - (lista.Count - 1) / 2.0) * SeparacaoFio;
        }
    }

    double DesvioDe(int indice) => desviosDeFio.GetValueOrDefault(indice);

    // ---- ganchos do teste da separacao ----

    /// <summary>Ponto do fio a fracao t, ja com o afastamento aplicado.</summary>
    internal Point PontoDoFioParaTeste(int indice, double t)
    {
        CalcularDesvios();
        var fio = Atual.Fios[indice];
        var a = PosicaoPino(fio.DePeca, fio.DePino);
        var b = PosicaoPino(fio.ParaPeca, fio.ParaPino);
        return NaCurva(fio, a.Value, b.Value, t, DesvioDe(indice));
    }

    /// <summary>O mesmo caminho do clique, para conferir de fora.</summary>
    internal int FioPertoParaTeste(Point p, double raio)
    {
        CalcularDesvios();
        return FioPerto(p, raio);
    }

    /// <summary>Grossura com que cada fio FOI desenhado: 4,5 destacado, 3 normal.</summary>
    readonly Dictionary<int, double> grossuraDesenhada = [];

    /// <summary>O fio esta com cara de selecionado na tela?</summary>
    internal bool FioDestacadoParaTeste(int indice)
        => grossuraDesenhada.TryGetValue(indice, out double g) && g > 4;

    void CriarAnalisador(PecaNoCircuito peca)
    {
        var tracos = new Path[CanaisDoAnalisador];
        for (int k = 0; k < CanaisDoAnalisador; k++)
        {
            tracos[k] = new Path
            {
                Stroke = new SolidColorBrush(Color.FromRgb(0x7F, 0xE0, 0x6E)),
                StrokeThickness = 1.4,
                IsHitTestVisible = false,
            };
            Panel.SetZIndex(tracos[k], 320);
            mundo.Children.Add(tracos[k]);
        }

        tracosDoAnalisador[peca.Id] = tracos;
        ondasDoAnalisador[peca.Id] = [.. Enumerable.Range(0, CanaisDoAnalisador)
                                                   .Select(_ => new List<int>())];
    }

    /// <summary>
    /// O analisador desenha o que os pinos fizeram DE VERDADE, com o tempo
    /// da linha do tempo da placa, na escala da peca -- e com disparo na
    /// subida, para o sinal repetido ficar parado na tela.
    ///
    /// Antes ele olhava o nivel uma vez por quadro da tela, vinte e cinco
    /// vezes por segundo: um PWM virava reta e um shiftOut nem aparecia.
    /// O canal que nao chega em pino de placa (vai numa porta logica, num
    /// botao solto) continua assim, amostrado -- nao ha linha do tempo
    /// fora das placas.
    /// </summary>
    void AtualizarAnalisadores(Bancada bancada, Netlist rede)
    {
        foreach (var peca in Atual.Pecas)
        {
            if (peca.Tipo != "logic-analyzer") continue;
            if (!ondasDoAnalisador.TryGetValue(peca.Id, out var ondas)) continue;

            var def = Catalogo.Obter(peca.Tipo);
            if (def is null) continue;

            var maq = MaquinaDaPeca(bancada, rede, peca);
            var fontes = Analisador.Fontes(Atual, rede, bancada, peca);
            double janela = Analisador.JanelaDe(peca);
            var (de, ate) = Analisador.Enquadrar(fontes, janela);

            var desenho = new List<(double X, int Nivel)>[CanaisDoAnalisador];
            for (int k = 0; k < CanaisDoAnalisador; k++)
            {
                var fonte = fontes[k];
                if (fonte.Ligada)
                {
                    desenho[k] = [.. Analisador.OndaParaDesenhar(fonte, de, ate, 240)
                                        .Select(p => ((p.Us - de) / (ate - de), p.Nivel))];
                    continue;
                }

                int nivel = NivelDoCanal(peca, $"D{k}", maq, rede);
                ondas[k].Add(nivel);
                if (ondas[k].Count > AmostrasDoAnalisador) ondas[k].RemoveAt(0);

                var amostras = ondas[k];
                desenho[k] = [.. amostras.Select((n, i) => (amostras.Count < 2 ? 0.0
                                  : (double)i / (AmostrasDoAnalisador - 1), n))];
            }

            quadrosDoAnalisador[peca.Id] = quadrosDoAnalisador.GetValueOrDefault(peca.Id) + 1;
            desenhoDoAnalisador[peca.Id] = desenho;
            DesenharOndas(peca, def, desenho);
        }
    }

    /// <summary>Quantos quadros cada analisador ja desenhou nesta execucao.</summary>
    readonly Dictionary<string, int> quadrosDoAnalisador = [];

    /// <summary>O ultimo desenho de cada canal: posicao (0 a 1) e nivel.</summary>
    readonly Dictionary<string, List<(double X, int Nivel)>[]> desenhoDoAnalisador = [];

    int NivelDoCanal(PecaNoCircuito peca, string pino, Maquina maq, Netlist rede)
    {
        int gpio = rede.GpioDe(Atual, peca.Id, pino);
        if (gpio >= 0 && maq is not null) return maq.DigitalRead(gpio);

        int logico = LogicaAtual?.NivelDoPino(peca.Id, pino) ?? Logica.Indefinido;
        return logico == Logica.Indefinido ? 0 : logico;
    }

    void DesenharOndas(PecaNoCircuito peca, PecaDef def, List<(double X, int Nivel)>[] ondas)
    {
        if (!tracosDoAnalisador.TryGetValue(peca.Id, out var tracos)) return;

        double x0 = def.Largura * TelaDoAnalisador.X;
        double y0 = def.Altura * TelaDoAnalisador.Y;
        double larg = def.Largura * TelaDoAnalisador.Larg;
        double alt = def.Altura * TelaDoAnalisador.Alt;
        double linha = alt / CanaisDoAnalisador;

        for (int k = 0; k < CanaisDoAnalisador; k++)
        {
            var pontos = ondas[k];
            if (pontos is null || pontos.Count == 0) { tracos[k].Data = null; continue; }

            double baixo = y0 + (k + 1) * linha - 1.5;
            double alto = y0 + k * linha + 1.5;
            double Y(int nivel) => nivel == 1 ? alto : baixo;
            double X(double fracao) => x0 + Math.Clamp(fracao, 0, 1) * larg;

            // Degraus: anda na horizontal ate a borda e sobe ou desce ali.
            var figura = new PathFigure
            {
                IsClosed = false,
                StartPoint = Geometria.PosicaoLocal(peca, def, X(pontos[0].X), Y(pontos[0].Nivel)),
            };

            for (int i = 1; i < pontos.Count; i++)
            {
                double x = X(pontos[i].X);
                figura.Segments.Add(new LineSegment(
                    Geometria.PosicaoLocal(peca, def, x, Y(pontos[i - 1].Nivel)), true));
                figura.Segments.Add(new LineSegment(
                    Geometria.PosicaoLocal(peca, def, x, Y(pontos[i].Nivel)), true));
            }

            // O ultimo nivel vai ate o fim da tela.
            figura.Segments.Add(new LineSegment(
                Geometria.PosicaoLocal(peca, def, X(1), Y(pontos[^1].Nivel)), true));

            tracos[k].Data = new PathGeometry { Figures = { figura } };
        }
    }

    /// <summary>Para o teste: quantos quadros o analisador ja desenhou.</summary>
    internal int QuadrosDoAnalisadorParaTeste(string idDaPeca)
        => quadrosDoAnalisador.GetValueOrDefault(idDaPeca);

    /// <summary>Para o teste: o canal mostra alguma borda no quadro atual?</summary>
    internal bool AnalisadorViuBordaParaTeste(string idDaPeca, int canal)
    {
        if (!desenhoDoAnalisador.TryGetValue(idDaPeca, out var desenho)) return false;
        if (canal < 0 || canal >= desenho.Length || desenho[canal] is null) return false;

        var pontos = desenho[canal];
        for (int i = 1; i < pontos.Count; i++)
            if (pontos[i].Nivel != pontos[i - 1].Nivel) return true;
        return false;
    }

    /// <summary>Para o teste: quantas bordas o canal mostra no quadro atual.</summary>
    internal int BordasNoAnalisadorParaTeste(string idDaPeca, int canal)
        => desenhoDoAnalisador.TryGetValue(idDaPeca, out var desenho)
           && canal >= 0 && canal < desenho.Length && desenho[canal] is { } pontos
            ? Math.Max(0, pontos.Count - 1) : -1;

    void AplicarCartoes(Bancada bancada, Netlist rede)
    {
        foreach (var (_, maq) in bancada.Todas) maq.Cartao.Presente = false;

        foreach (var peca in Atual.Pecas)
        {
            if (peca.Tipo != "microsd-card") continue;

            foreach (string pino in new[] { "CS", "SCK", "DI", "DO" })
            {
                var (placa, _) = rede.PinoDe(Atual, peca.Id, pino);
                if (placa is null) continue;

                bancada.De(placa).Cartao.Presente = true;
                break;
            }
        }
    }

    /// <summary>Os chips proprios do circuito.</summary>
    public ChipsNoCircuito Chips { get; } = new();

    /// <summary>
    /// Um quadro de cada chip proprio: le as entradas da rede, roda o
    /// codigo dele e devolve o que ele esta mandando para as placas.
    /// </summary>
    void AplicarChips(Bancada bancada, Netlist rede)
    {
        if (!Chips.Algum) return;

        Chips.Passo(Atual, rede, bancada);

        foreach (var peca in Atual.Pecas)
        {
            var def = Catalogo.Obter(peca.Tipo);
            if (def is null || !peca.Tipo.StartsWith(ChipCustom.Prefixo, StringComparison.Ordinal))
                continue;

            foreach (var pino in def.Pinos)
            {
                int nivel = Chips.SaidaDoPino(peca.Id, pino.Nome);
                if (nivel < 0) continue;

                var (placa, gpio) = rede.PinoDe(Atual, peca.Id, pino.Nome);
                if (placa is null || gpio < 0) continue;

                var maq = bancada.De(placa);
                if (maq is not null && maq.Modo[gpio] != 1) maq.NivelExterno[gpio] = nivel;
            }
        }
    }

    /// <summary>Relê os chips do projeto e da pasta, e redesenha.</summary>
    public void RecarregarChips()
    {
        Chips.Recarregar(Atual);
        Redesenhar();
    }

    void AplicarRadios(Bancada bancada, Netlist rede)
    {
        foreach (var (_, maq) in bancada.Todas) maq.RadioPresente = false;

        foreach (var peca in Atual.Pecas)
        {
            if (peca.Tipo != "nrf24l01") continue;

            foreach (string pino in new[] { "CE", "CSN", "SCK", "MOSI", "MISO" })
            {
                var (placa, _) = rede.PinoDe(Atual, peca.Id, pino);
                if (placa is null) continue;

                bancada.De(placa).RadioPresente = true;
                break;
            }
        }
    }

    public void AtualizarRotuloDe(PecaNoCircuito peca)
    {
        var def = peca is null ? null : Catalogo.Obter(peca.Tipo);
        if (def is not null) AtualizarRotulo(peca, def);
    }

    public IEnumerable<PontoDeAcesso> PontosDeAcessoDoCircuito()
    {
        foreach (var peca in Atual.Pecas)
            if (peca.Tipo == "wifi-ap" && !string.IsNullOrWhiteSpace(peca.Ssid))
                yield return new PontoDeAcesso(peca.Ssid.Trim(), peca.Senha ?? "");
    }

    /// <summary>
    /// De onde vem o som deste alto-falante: o I2S de uma placa, por um
    /// MAX98357A alimentado e ligado nos pinos que o sketch abriu, ou o tom de
    /// um pino (tone()), direto ou por um resistor. Nada: (null, 0).
    /// </summary>
    public (SaidaDeAudio Audio, int Hz) SomDoAltoFalante(PecaNoCircuito falante, Bancada bancada, Netlist rede)
    {
        string mais = rede.RaizDe(Netlist.No(falante.Id, "+"));
        string menos = rede.RaizDe(Netlist.No(falante.Id, "-"));
        if (mais is null || menos is null) return (null, 0);

        foreach (var amp in Atual.Pecas)
        {
            if (amp.Tipo != "max98357a") continue;

            // O borne do amplificador nos dois fios (a polaridade nao muda o som).
            string outMais = rede.RaizDe(Netlist.No(amp.Id, "OUT+"));
            string outMenos = rede.RaizDe(Netlist.No(amp.Id, "OUT-"));
            if (!((outMais == mais && outMenos == menos) || (outMais == menos && outMenos == mais))) continue;

            // Sem VIN e GND o chip nem liga; o SD no terra (ou num pino em
            // LOW) desliga a saida -- como no amplificador de verdade.
            if (!ChegaEm(rede, amp.Id, "VIN", terra: false) || !ChegaEm(rede, amp.Id, "GND", terra: true)) continue;
            if (ChegaEm(rede, amp.Id, "SD", terra: true)) continue;

            var (placa, bclk) = rede.PinoDe(Atual, amp.Id, "BCLK");
            if (placa is null) continue;
            var maq = bancada.De(placa);
            if (!maq.Energizada || maq.Audio is not { } audio) continue;

            int sd = rede.GpioDe(Atual, amp.Id, "SD");
            if (sd >= 0 && maq.Modo[sd] == 1 && maq.Digital[sd] == 0 && maq.Pwm[sd] == 0) continue;

            // So toca o que sai nos pinos em que ele esta: o I2S aberto em
            // outros pinos nao chega ao amplificador.
            if (bclk != audio.Bclk || rede.GpioDe(Atual, amp.Id, "LRC") != audio.Ws
                || rede.GpioDe(Atual, amp.Id, "DIN") != audio.Dout) continue;
            return (audio, 0);
        }

        // Direto num pino, ou por um resistor: o tom do tone().
        foreach (string lado in new[] { "+", "-" })
        {
            var (placa, gpio) = rede.PinoDe(Atual, falante.Id, lado);
            if (placa is null || gpio < 0) continue;
            int hz = bancada.De(placa).Frequencia[gpio];
            if (hz > 0) return (null, hz);
        }
        return (null, 0);
    }

    /// <summary>
    /// O som do PCM5102A: o DAC estereo com a saida de fone (o fone ligado no
    /// P2 dele toca no fone do computador). Precisa do VIN e do GND, do SCK no
    /// GND (ou solto: o PLL tira o clock do BCK) e do BCK, LCK e DIN nos pinos
    /// em que o sketch abriu o I2S. Nulo: nenhum tocando.
    /// </summary>
    public SaidaDeAudio SomDoPcm5102a(Bancada bancada, Netlist rede)
    {
        foreach (var dac in Atual.Pecas)
        {
            if (dac.Tipo != "pcm5102a") continue;
            if (!ChegaEm(rede, dac.Id, "VIN", terra: false) || !ChegaEm(rede, dac.Id, "GND", terra: true)) continue;
            if (ChegaEm(rede, dac.Id, "SCK", terra: false)) continue;   // o SCK no VCC: sem clock, mudo
            var (placa, bck) = rede.PinoDe(Atual, dac.Id, "BCK");
            if (placa is null) continue;
            var maq = bancada.De(placa);
            if (!maq.Energizada || maq.Audio is not { } audio) continue;
            if (bck != audio.Bclk || rede.GpioDe(Atual, dac.Id, "LCK") != audio.Ws
                || rede.GpioDe(Atual, dac.Id, "DIN") != audio.Dout) continue;
            return audio;
        }
        return null;
    }

    /// <summary>
    /// O no deste pino chega numa alimentacao (ou num terra) de verdade: o
    /// 3V3/5V/GND de uma placa, os simbolos de VCC e GND, a bateria.
    /// </summary>
    bool ChegaEm(Netlist rede, string idDaPeca, string pino, bool terra)
    {
        string raiz = rede.RaizDe(Netlist.No(idDaPeca, pino));
        if (raiz is null) return false;
        foreach (var fonte in Atual.Pecas)
        {
            if (!Netlist.EhFonte(fonte.Tipo) || Catalogo.Obter(fonte.Tipo) is not { } def) continue;
            foreach (var p in def.Pinos)
            {
                bool serve = Netlist.EhBateria(fonte.Tipo)
                    ? p.Nome == (terra ? "-" : "+")
                    : terra ? Netlist.EhPinoDeTerra(p.Nome) : Netlist.EhPinoDeAlimentacao(p.Nome);
                if (serve && rede.RaizDe(Netlist.No(fonte.Id, p.Nome)) == raiz) return true;
            }
        }
        return false;
    }

    /// <summary>
    /// O que o codigo C (modo nativo) acha no barramento SPI desta placa: os
    /// displays ILI9341 e os cartoes microSD, com o GPIO em que cada pino
    /// chega (-1: ligado em outra coisa, ou solto).
    /// </summary>
    public List<AparelhoNoSpi> AparelhosSpiDe(Bancada bancada, Netlist rede, Maquina maquina)
    {
        var lista = new List<AparelhoNoSpi>();
        foreach (var peca in Atual.Pecas)
        {
            // CS, DC, RST, MOSI, MISO, SCK, luz -- com os nomes da peca.
            string[] pinos = peca.Tipo switch
            {
                "ili9341" => ["CS", "D/C", "RST", "MOSI", "MISO", "SCK", "LED"],
                "microsd-card" => ["CS", null, null, "DI", "DO", "SCK", null],
                _ => null,
            };
            if (pinos is null) continue;

            var g = new int[pinos.Length];
            bool nestaPlaca = false;
            for (int i = 0; i < pinos.Length; i++)
            {
                g[i] = -1;
                if (pinos[i] is null) continue;
                var (placa, gpio) = rede.PinoDe(Atual, peca.Id, pinos[i]);
                if (placa is null || bancada.De(placa) != maquina) continue;
                g[i] = gpio;
                nestaPlaca = true;
            }
            if (!nestaPlaca) continue;
            lista.Add(new AparelhoNoSpi(peca.Tipo == "ili9341" ? "ili9341" : "sd", g[0], g[1], g[2], g[3], g[4], g[5], g[6]));
        }
        return lista;
    }

    public IEnumerable<(PecaNoCircuito Peca, Maquina Maquina)> CartoesDoCircuito(
        Bancada bancada, Netlist rede)
    {
        foreach (var peca in Atual.Pecas)
        {
            if (peca.Tipo != "microsd-card") continue;

            Maquina achada = null;
            foreach (string pino in new[] { "CS", "SCK", "DI", "DO" })
            {
                var (placa, _) = rede.PinoDe(Atual, peca.Id, pino);
                if (placa is null) continue;

                achada = bancada.De(placa);
                break;
            }

            yield return (peca, achada);
        }
    }

    /// <summary>
    /// Os pendrives do circuito, a placa de cada um e, quando a placa nao
    /// vai enxerga-lo, o porque. O pendrive fala pelo USB OTG, que so a
    /// ESP32-S3 e a S2 tem: D- no GPIO19, D+ no GPIO20, 5 V no VBUS.
    /// </summary>
    public IEnumerable<(PecaNoCircuito Peca, Maquina Maquina, string Problema)> PendrivesDoCircuito(
        Bancada bancada, Netlist rede)
    {
        foreach (var peca in Atual.Pecas)
        {
            if (peca.Tipo != "pendrive-usb") continue;

            var (placaMenos, gpioMenos) = rede.PinoDe(Atual, peca.Id, "D-");
            var (placaMais, gpioMais) = rede.PinoDe(Atual, peca.Id, "D+");
            string placa = placaMenos ?? placaMais;
            string tipoDaPlaca = placa is null ? null : Atual.Pecas.FirstOrDefault(p => p.Id == placa)?.Tipo;

            string problema = null;
            if (tipoDaPlaca is not ("esp32-s3-devkit" or "esp32-s2-devkit"))
                problema = "só a ESP32-S3 (e a S2) tem o USB OTG que lê um pendrive";
            else if (placaMenos != placaMais || gpioMenos != 19 || gpioMais != 20)
                problema = "o D- vai no GPIO19 e o D+ no GPIO20, que são os pinos do USB OTG";
            else if (!rede.AlcancaFonte(Atual, peca.Id, "VBUS", terra: false))
                problema = "o VBUS precisa dos 5 V da placa";
            else if (!rede.AlcancaFonte(Atual, peca.Id, "GND", terra: true))
                problema = "o GND do pendrive não está ligado";

            yield return (peca, placa is null ? null : bancada.De(placa), problema);
        }
    }

    void DesenharNiveisLogicos()
    {
        if (LogicaAtual is null) { niveisLogicos.Data = null; return; }

        var grupo = new GeometryGroup();
        foreach (var peca in Atual.Pecas)
        {
            if (!Logica.EhPecaDeLogica(peca.Tipo)) continue;

            var def = Catalogo.Obter(peca.Tipo);
            if (def is null) continue;

            foreach (var pino in def.Pinos)
            {
                if (LogicaAtual.NivelDoPino(peca.Id, pino.Nome) != 1) continue;
                grupo.Children.Add(new EllipseGeometry(
                    Geometria.PosicaoPino(peca, def, pino), 3.4, 3.4));
            }
        }
        niveisLogicos.Data = grupo;
    }

    Brush PincelDaEnergia(Fio fio)
    {
        var nivel = energia is null
            ? NivelDeEnergia.Nenhuma : energia.NivelDoPino(fio.DePeca, fio.DePino);
        return new SolidColorBrush(CoresDaEnergia[nivel]);
    }

    void RedesenharFios()
    {
        foreach (var f in visuaisFios) mundo.Children.Remove(f);
        visuaisFios.Clear();
        grossuraDesenhada.Clear();

        CalcularDesvios();

        for (int i = 0; i < Atual.Fios.Count; i++)
        {
            var fio = Atual.Fios[i];
            var a = PosicaoPino(fio.DePeca, fio.DePino);
            var b = PosicaoPino(fio.ParaPeca, fio.ParaPino);
            if (a is null || b is null) continue;

            bool escolhido = fiosSelecionados.Contains(i);
            grossuraDesenhada[i] = escolhido ? 4.5 : 3;
            var forma = Curva(fio, a.Value, b.Value, DesvioDe(i));

            // Contorno escuro por baixo: onde dois fios se cruzam, e ele
            // que diz qual passa por cima. Um traco mais grosso custa
            // quase nada, diferente de um borrao de sombra por fio, que
            // seria refeito a cada movimento do arraste.
            var contorno = new Path
            {
                Data = forma,
                Stroke = new SolidColorBrush(Color.FromArgb(0x8C, 0x08, 0x08, 0x0A)),
                StrokeThickness = (escolhido ? 4.5 : 3) * fatorDosFios + 2.4,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
                IsHitTestVisible = false
            };
            Panel.SetZIndex(contorno, escolhido ? 519 : 499);
            mundo.Children.Add(contorno);
            visuaisFios.Add(contorno);

            var traco = new Path
            {
                Data = forma,
                Stroke = visaoDeEnergia ? PincelDaEnergia(fio) : Pincel(fio.Cor),
                StrokeThickness = (escolhido ? 4.5 : 3) * fatorDosFios,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
                IsHitTestVisible = false
            };
            if (escolhido)
            {
                traco.Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = Colors.Cyan,
                    BlurRadius = 10,
                    ShadowDepth = 0,
                    Opacity = 0.95
                };
            }
            // O fio do outro lado da PCB: apagado, como visto atraves da placa.
            if (FioDoOutroLado(fio)) { traco.Opacity = 0.18; contorno.Opacity = 0.1; }
            Panel.SetZIndex(traco, escolhido ? 520 : 500);
            mundo.Children.Add(traco);
            visuaisFios.Add(traco);
        }

        RedesenharAlcas();
    }

    /// <summary>
    /// Os pontos por onde o fio passa: pino, dobras, pino.
    /// </summary>
    /// <summary>
    /// Alcance do ima do fio, em pixels do circuito. Meio passo da grade:
    /// mais que isso e o canto cola sozinho onde ninguem pediu.
    /// </summary>
    const double ImaDoFio = 5.0;

    /// <summary>
    /// Onde o canto do fio encosta ao ser arrastado.
    ///
    /// Duas regras, nesta ordem: a grade de 0,1" -- a MESMA das pecas -- e
    /// o ima dos vizinhos. Chegando perto da linha do pino (ou do canto do
    /// lado), o canto cola exatamente nela.
    ///
    /// O ima existe porque deixar o fio reto era loteria: o pino fica onde
    /// o desenho da peca manda, e isso quase nunca cai num ponto da grade.
    /// O canto encaixado na grade parava alguns pixels ao lado do pino, e
    /// o fio saia torto -- a queixa era "os fios tem uma grade separada".
    ///
    /// Alt solta o canto de qualquer grade, como no arraste de peca.
    /// </summary>
    Point EncaixarDobra(Fio fio, int indiceDobra, Point p)
    {
        if (Keyboard.IsKeyDown(Key.LeftAlt)) return p;

        double x = Math.Round(p.X / PassoGrade) * PassoGrade;
        double y = Math.Round(p.Y / PassoGrade) * PassoGrade;

        var a = PosicaoPino(fio.DePeca, fio.DePino);
        var b = PosicaoPino(fio.ParaPeca, fio.ParaPino);
        if (a is null || b is null) return new Point(x, y);

        // pontos = [pino A, dobra 0, dobra 1, ..., pino B]. Os vizinhos da
        // dobra i sao, entao, pontos[i] e pontos[i + 2].
        var pontos = CaminhoDoFio(fio, a.Value, b.Value);
        foreach (int k in new[] { indiceDobra, indiceDobra + 2 })
        {
            if (k < 0 || k >= pontos.Count) continue;
            var vizinho = pontos[k];
            if (Math.Abs(p.X - vizinho.X) < ImaDoFio) x = vizinho.X;
            if (Math.Abs(p.Y - vizinho.Y) < ImaDoFio) y = vizinho.Y;
        }
        return new Point(x, y);
    }

    /// <summary>Para o teste: onde o canto cairia se fosse arrastado ate aqui.</summary>
    internal Point EncaixarDobraParaTeste(int indiceFio, int indiceDobra, Point p)
        => EncaixarDobra(Atual.Fios[indiceFio], indiceDobra, p);

    static List<Point> CaminhoDoFio(Fio fio, Point a, Point b)
    {
        var pontos = new List<Point> { a };
        if (fio?.Dobras is { Count: > 0 } dobras)
            foreach (var d in dobras) pontos.Add(new Point(d.X, d.Y));
        pontos.Add(b);
        return pontos;
    }

    static Geometry Curva(string estilo, Point a, Point b) => estilo switch
    {
        Fio.EstiloReto => Linha(a, b),
        Fio.EstiloDegrau => Degrau(a, b),
        _ => Solto(a, b),
    };

    /// <summary>
    /// Traçado com as dobras do usuario. Sem dobras cai no formato do
    /// estilo; com dobras vira linha quebrada, porque a partir dai quem
    /// desenha a forma e quem arrastou os cantos.
    /// </summary>
    static Geometry Curva(Fio fio, Point a, Point b, double desvio = 0)
    {
        if (fio?.Dobras is not { Count: > 0 })
            return desvio == 0
                ? Curva(fio?.Estilo, a, b)
                : DoCaminho(PontosAfastados(fio?.Estilo, a, b, desvio));

        var pontos = CaminhoDoFio(fio, a, b);
        var fig = new PathFigure { StartPoint = pontos[0] };
        for (int i = 1; i < pontos.Count; i++)
            fig.Segments.Add(new LineSegment(pontos[i], true));
        var geo = new PathGeometry();
        geo.Figures.Add(fig);
        return geo;
    }

    /// <summary>Ponto a fracao t do caminho, ja contando as dobras.</summary>
    internal static Point NaCurva(Fio fio, Point a, Point b, double t, double desvio = 0)
    {
        if (fio?.Dobras is not { Count: > 0 })
            return desvio == 0
                ? NaCurva(fio?.Estilo, a, b, t)
                : NoCaminho(PontosAfastados(fio?.Estilo, a, b, desvio), t);

        var pontos = CaminhoDoFio(fio, a, b);
        int trechos = pontos.Count - 1;
        double pos = Math.Clamp(t, 0, 1) * trechos;
        int i = Math.Min((int)pos, trechos - 1);
        double f = pos - i;
        return new Point(pontos[i].X + (pontos[i + 1].X - pontos[i].X) * f,
                         pontos[i].Y + (pontos[i + 1].Y - pontos[i].Y) * f);
    }

    /// <summary>Linha direta: e o fio solto sem a barriga.</summary>
    /// <summary>
    /// O traçado do fio com os pontos do meio empurrados de lado.
    ///
    /// As PONTAS nao se mexem: elas estao nos pinos, e tirar o fio do
    /// pino para separar seria trocar um problema por outro. O que se
    /// afasta e o miolo -- no "reto" um ponto novo no meio, no "degrau"
    /// os dois cantos.
    /// </summary>
    static List<Point> PontosAfastados(string estilo, Point a, Point b, double desvio)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y;
        double comprimento = Math.Sqrt(dx * dx + dy * dy);
        if (comprimento < 0.01) return [a, b];

        // Perpendicular unitaria a reta que liga as duas pontas.
        double px = -dy / comprimento, py = dx / comprimento;
        Point Empurrar(Point q) => new(q.X + px * desvio, q.Y + py * desvio);

        if (estilo == Fio.EstiloDegrau)
        {
            double meio = (a.Y + b.Y) / 2;
            return [a, Empurrar(new Point(a.X, meio)),
                       Empurrar(new Point(b.X, meio)), b];
        }

        return [a, Empurrar(new Point((a.X + b.X) / 2, (a.Y + b.Y) / 2)), b];
    }

    static Geometry DoCaminho(List<Point> pontos)
    {
        var fig = new PathFigure { StartPoint = pontos[0] };
        for (int i = 1; i < pontos.Count; i++)
            fig.Segments.Add(new LineSegment(pontos[i], true));
        var geo = new PathGeometry();
        geo.Figures.Add(fig);
        return geo;
    }

    static Geometry Linha(Point a, Point b)
    {
        var fig = new PathFigure { StartPoint = a };
        fig.Segments.Add(new LineSegment(b, true));
        var geo = new PathGeometry();
        geo.Figures.Add(fig);
        return geo;
    }

    static Geometry Solto(Point a, Point b)
    {
        double folga = Math.Min(60, Math.Max(18, Math.Abs(b.X - a.X) * 0.4 + 18));
        var fig = new PathFigure { StartPoint = a };
        fig.Segments.Add(new BezierSegment(
            new Point(a.X, a.Y + folga), new Point(b.X, b.Y + folga), b, true));
        var geo = new PathGeometry();
        geo.Figures.Add(fig);
        return geo;
    }

    /// <summary>Tracado em L, no estilo do Wokwi.</summary>
    static Geometry Degrau(Point a, Point b)
    {
        double meio = (a.Y + b.Y) / 2;
        var fig = new PathFigure { StartPoint = a };
        fig.Segments.Add(new LineSegment(new Point(a.X, meio), true));
        fig.Segments.Add(new LineSegment(new Point(b.X, meio), true));
        fig.Segments.Add(new LineSegment(b, true));
        var geo = new PathGeometry();
        geo.Figures.Add(fig);
        return geo;
    }

    /// <summary>Ponto a fracao t de uma linha quebrada, trecho a trecho.</summary>
    static Point NoCaminho(List<Point> pontos, double t)
    {
        int trechos = pontos.Count - 1;
        double pos = Math.Clamp(t, 0, 1) * trechos;
        int i = Math.Min((int)pos, trechos - 1);
        double f = pos - i;
        return new Point(pontos[i].X + (pontos[i + 1].X - pontos[i].X) * f,
                         pontos[i].Y + (pontos[i + 1].Y - pontos[i].Y) * f);
    }

    internal static Point NaCurva(string estilo, Point a, Point b, double t)
    {
        if (estilo == Fio.EstiloReto)
            return new Point(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);

        if (estilo == Fio.EstiloDegrau)
        {
            double meio = (a.Y + b.Y) / 2;
            Point[] cantos = [a, new Point(a.X, meio), new Point(b.X, meio), b];
            double pos = t * 3;
            int i = Math.Min((int)pos, 2);
            double f = pos - i;
            return new Point(cantos[i].X + (cantos[i + 1].X - cantos[i].X) * f,
                             cantos[i].Y + (cantos[i + 1].Y - cantos[i].Y) * f);
        }

        double folga = Math.Min(60, Math.Max(18, Math.Abs(b.X - a.X) * 0.4 + 18));
        var c1 = new Point(a.X, a.Y + folga);
        var c2 = new Point(b.X, b.Y + folga);
        double u = 1 - t;
        return new Point(
            u * u * u * a.X + 3 * u * u * t * c1.X + 3 * u * t * t * c2.X + t * t * t * b.X,
            u * u * u * a.Y + 3 * u * u * t * c1.Y + 3 * u * t * t * c2.Y + t * t * t * b.Y);
    }

    /// <summary>Fio mais proximo do ponto: amostra a curva e mede.</summary>
    /// <summary>Raio da alca de dobra, e o alvo do clique.</summary>
    const double RaioDobra = 5.5;

    readonly List<Ellipse> alcasDobra = [];

    /// <summary>Dobra sendo arrastada: (fio, indice) ou null.</summary>
    (int fio, int dobra)? arrastandoDobra;

    /// <summary>
    /// Alcas de um fio selecionado: uma cheia em cada canto que existe e
    /// uma vazada no meio de cada trecho, que cria um canto novo ao ser
    /// arrastada. E como o Wokwi faz, e e o unico jeito de moldar o fio
    /// sem inventar um modo separado de edicao.
    /// </summary>
    IEnumerable<(Point pos, int indice, bool nova)> AlcasDoFio(int indiceFio)
    {
        if (indiceFio < 0 || indiceFio >= Atual.Fios.Count) yield break;

        var fio = Atual.Fios[indiceFio];
        var a = PosicaoPino(fio.DePeca, fio.DePino);
        var b = PosicaoPino(fio.ParaPeca, fio.ParaPino);
        if (a is null || b is null) yield break;

        // Quantas alcas existem sai das DOBRAS, nao do traçado
        // desenhado: o ponto do meio de um fio afastado nao e canto
        // nenhum, e contando por ali ele virava alca cheia -- e o fio
        // reto passava a ter tres alcas em vez de uma.
        var pontos = CaminhoDoFio(fio, a.Value, b.Value);
        double desvio = DesvioDe(indiceFio);

        for (int i = 1; i < pontos.Count - 1; i++)
            yield return (pontos[i], i - 1, false);

        // Meio de cada trecho: arrastar cria um canto ali. A posicao sai
        // do traçado DE VERDADE, para a alca ficar em cima do fio mesmo
        // quando ele foi afastado de um vizinho.
        int trechos = pontos.Count - 1;
        for (int i = 0; i < trechos; i++)
        {
            double t = (i + 0.5) / trechos;
            yield return (NaCurva(fio, a.Value, b.Value, t, desvio), i, true);
        }
    }

    /// <summary>
    /// Tira canto que nao dobra nada.
    ///
    /// Arrastar uma alca ate a linha dos vizinhos deixa um ponto no meio
    /// de um trecho reto: ele nao muda o desenho e ainda por cima gera
    /// mais uma alca para atrapalhar. Some sozinho, que e o que o Wokwi
    /// faz e o que a pessoa espera.
    /// </summary>
    void LimparDobrasRedundantes(int indiceFio)
    {
        if (indiceFio < 0 || indiceFio >= Atual.Fios.Count) return;
        var fio = Atual.Fios[indiceFio];
        if (fio.Dobras.Count == 0) return;

        var a = PosicaoPino(fio.DePeca, fio.DePino);
        var b = PosicaoPino(fio.ParaPeca, fio.ParaPino);
        if (a is null || b is null) return;

        for (int i = fio.Dobras.Count - 1; i >= 0; i--)
        {
            var pontos = CaminhoDoFio(fio, a.Value, b.Value);
            var antes = pontos[i];          // pontos[0] e o pino
            var meio = pontos[i + 1];
            var depois = pontos[i + 2];

            // DISTANCIA do canto ate a reta dos vizinhos, nao a area do
            // triangulo: a area cresce com o comprimento do fio, entao um
            // limite fixo nela reprovava fio curto e aprovava fio longo.
            double dx = depois.X - antes.X, dy = depois.Y - antes.Y;
            double comprimento = Math.Sqrt(dx * dx + dy * dy);

            double desvio = comprimento < 0.01
                ? Math.Sqrt((meio.X - antes.X) * (meio.X - antes.X)
                            + (meio.Y - antes.Y) * (meio.Y - antes.Y))
                : Math.Abs((meio.X - antes.X) * dy - (meio.Y - antes.Y) * dx) / comprimento;

            // Meio passo da grade: o canto nao consegue ficar mais perto
            // da reta do que isso enquanto o encaixe estiver ligado.
            if (desvio < PassoGrade * 0.6) fio.Dobras.RemoveAt(i);
        }
    }

    /// <summary>Tira o canto sob o ponteiro; usado pelo clique duplo.</summary>
    public bool RemoverDobraEm(Point p)
    {
        if (fiosSelecionados.Count != 1) return false;
        int indiceFio = FioSelecionado;
        if (indiceFio < 0 || indiceFio >= Atual.Fios.Count) return false;

        foreach (var (pos, indice, nova) in AlcasDoFio(indiceFio))
        {
            if (nova) continue;
            double dx = pos.X - p.X, dy = pos.Y - p.Y;
            if (dx * dx + dy * dy > RaioDobra * RaioDobra * 2.2) continue;

            Atual.Fios[indiceFio].Dobras.RemoveAt(indice);
            RedesenharFios();
            SelecaoMudou?.Invoke();
            Mudou?.Invoke();
            return true;
        }
        return false;
    }

    /// <summary>Desfaz todas as dobras dos fios selecionados.</summary>
    public void EndireitarFios()
    {
        if (fiosSelecionados.Count == 0) return;
        foreach (int i in fiosSelecionados)
            if (i >= 0 && i < Atual.Fios.Count) Atual.Fios[i].Dobras.Clear();
        RedesenharFios();
        SelecaoMudou?.Invoke();
        Mudou?.Invoke();
    }

    /// <summary>As alcas de um fio, para o autoteste conferir.</summary>
    internal IEnumerable<(Point pos, int indice, bool nova)> AlcasParaTeste(int fio)
        => AlcasDoFio(fio);

    void RedesenharAlcas()
    {
        foreach (var alca in alcasDobra) mundo.Children.Remove(alca);
        alcasDobra.Clear();

        // So o fio sozinho ganha alcas: com varios selecionados a tela
        // viraria um campo de bolinhas sem dizer de quem e cada uma.
        if (fiosSelecionados.Count != 1) return;

        foreach (var (pos, _, nova) in AlcasDoFio(FioSelecionado))
        {
            var alca = new Ellipse
            {
                Width = RaioDobra * 2,
                Height = RaioDobra * 2,
                Fill = nova ? new SolidColorBrush(Color.FromArgb(0x66, 0x9B, 0x59, 0xD0))
                            : new SolidColorBrush(Color.FromRgb(0x9B, 0x59, 0xD0)),
                Stroke = new SolidColorBrush(Color.FromRgb(0xE6, 0xE6, 0xE6)),
                StrokeThickness = nova ? 1 : 1.4,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(alca, pos.X - RaioDobra);
            Canvas.SetTop(alca, pos.Y - RaioDobra);
            Panel.SetZIndex(alca, 560);
            mundo.Children.Add(alca);
            alcasDobra.Add(alca);
        }
    }

    /// <summary>
    /// Fios que passam dentro do retangulo do laco. Amostra o proprio
    /// tracado em vez da reta entre as pontas: um fio solto faz barriga,
    /// e a barriga tambem conta como parte dele.
    /// </summary>
    IEnumerable<int> FiosNoRetangulo(Rect area)
    {
        for (int i = 0; i < Atual.Fios.Count; i++)
        {
            var fio = Atual.Fios[i];
            var a = PosicaoPino(fio.DePeca, fio.DePino);
            var b = PosicaoPino(fio.ParaPeca, fio.ParaPino);
            if (a is null || b is null) continue;

            double desvio = DesvioDe(i);
            var anterior = NaCurva(fio, a.Value, b.Value, 0, desvio);
            for (int k = 1; k <= 24; k++)
            {
                var atual = NaCurva(fio, a.Value, b.Value, k / 24.0, desvio);
                if (Cruza(area, anterior, atual)) { yield return i; break; }
                anterior = atual;
            }
        }
    }

    /// <summary>
    /// O segmento encosta no retangulo? (recorte de Liang-Barsky)
    ///
    /// Testar so os pontos amostrados nao bastava: um fio de 700 px vira
    /// 25 amostras a cada 30 px, e um laco estreito passava entre duas
    /// delas sem pegar o fio.
    /// </summary>
    static bool Cruza(Rect area, Point a, Point b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y;
        double t0 = 0, t1 = 1;

        foreach (var (p, q) in new[]
        {
            (-dx, a.X - area.Left), (dx, area.Right - a.X),
            (-dy, a.Y - area.Top), (dy, area.Bottom - a.Y),
        })
        {
            if (Math.Abs(p) < 1e-9)
            {
                if (q < 0) return false;          // paralelo e fora
                continue;
            }
            double r = q / p;
            if (p < 0) { if (r > t1) return false; if (r > t0) t0 = r; }
            else { if (r < t0) return false; if (r < t1) t1 = r; }
        }
        return true;
    }

    int FioPerto(Point p, double raio = 7)
    {
        int melhor = -1;
        double menor = raio * raio;

        for (int i = 0; i < Atual.Fios.Count; i++)
        {
            var fio = Atual.Fios[i];
            if (FioDoOutroLado(fio)) continue;
            var a = PosicaoPino(fio.DePeca, fio.DePino);
            var b = PosicaoPino(fio.ParaPeca, fio.ParaPino);
            if (a is null || b is null) continue;

            double desvio = DesvioDe(i);
            for (int k = 0; k <= 24; k++)
            {
                var q = NaCurva(fio, a.Value, b.Value, k / 24.0, desvio);
                double dx = q.X - p.X, dy = q.Y - p.Y;
                double d = dx * dx + dy * dy;
                if (d < menor) { menor = d; melhor = i; }
            }
        }
        return melhor;
    }

    static Brush Pincel(string hex)
    {
        try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }
        catch { return Brushes.LimeGreen; }
    }

    // ================= pinos e rotacao =================

    public Point? PosicaoPino(string idPeca, string nomePino)
    {
        var peca = Atual.Achar(idPeca);
        var def = peca is null ? null : Catalogo.Obter(peca.Tipo);
        var pino = def?.Pinos.FirstOrDefault(p => p.Nome == nomePino);
        if (pino is null) return null;
        return Geometria.PosicaoLocal(peca, def, pino.X, pino.Y);
    }


    /// <summary>
    /// Peca sob o ponto respeitando a ordem de desenho: o que esta por
    /// cima (LED sobre protoboard) ganha o clique.
    /// </summary>

    // ================= interacao =================
    Point NoMundo(MouseEventArgs e) => NoMundo(e.GetPosition(this));

    Point NoMundo(Point naTela)
        => new((naTela.X - deslocamento.X) / escala.ScaleX,
               (naTela.Y - deslocamento.Y) / escala.ScaleY);

    /// <summary>
    /// Onde o ponteiro esta, em coordenadas do circuito, ou null se ele
    /// estiver fora da area de desenho.
    /// </summary>
    Point? PonteiroNoMundo()
    {
        // Sem area desenhada nao ha "onde o mouse esta": e o caso do modo
        // sem janela, onde GetPosition devolveria (0,0) e o colar cairia
        // no canto do circuito sem ninguem ter pedido.
        if (ActualWidth <= 0 || ActualHeight <= 0) return null;

        var p = Mouse.GetPosition(this);
        if (p.X < 0 || p.Y < 0 || p.X > ActualWidth || p.Y > ActualHeight) return null;
        return NoMundo(p);
    }

    /// <summary>
    /// O botao direito pediu o menu.
    ///
    /// O editor nao monta o menu: quem sabe o que cabe nele e a janela,
    /// que tem os comandos, os atalhos e os dialogos. Aqui so se diz
    /// ONDE foi o clique e o que estava embaixo dele.
    /// </summary>
    public event Action<Point, Acerto> MenuPedido;

    /// <summary>Onde o botao direito desceu, para saber se houve arrasto.</summary>
    Point? direitoDesceuEm;

    void AoApertarQualquer(object s, MouseButtonEventArgs e)
    {
        Focus();
        if (e.ChangedButton is MouseButton.Middle
            || (e.ChangedButton == MouseButton.Right && origemFio is null))
        {
            panejando = true;
            panInicio = e.GetPosition(this);
            if (e.ChangedButton == MouseButton.Right) direitoDesceuEm = panInicio;
            Cursor = Cursors.SizeAll;
            CaptureMouse();
        }
    }

    /// <summary>
    /// Quanto a mao pode escorregar e o clique ainda contar como clique.
    ///
    /// O botao direito faz as DUAS coisas: arrastado, ele passeia pelo
    /// circuito; clicado, abre o menu. Sem uma folga, a tremida normal
    /// da mao faria o menu deixar de abrir.
    /// </summary>
    const double FolgaDoClique = 4.0;

    void AoSoltarQualquer(object s, MouseButtonEventArgs e)
    {
        if (panejando && e.ChangedButton is MouseButton.Middle or MouseButton.Right)
        {
            panejando = false;
            Cursor = Cursors.Arrow;
            ReleaseMouseCapture();
        }

        if (e.ChangedButton != MouseButton.Right) return;

        var desceu = direitoDesceuEm;
        direitoDesceuEm = null;
        if (desceu is null) return;

        var agora = e.GetPosition(this);
        if (Math.Abs(agora.X - desceu.Value.X) > FolgaDoClique
            || Math.Abs(agora.Y - desceu.Value.Y) > FolgaDoClique) return;

        var noMundo = NoMundo(agora);
        var acerto = Resolver(noMundo);

        // Clicar com o direito numa peca que nao estava selecionada
        // seleciona ela: e o que todo editor faz, e sem isso o menu
        // agiria sobre a peca errada.
        if (acerto.Peca is not null && !selecionadas.Contains(acerto.Peca.Id))
            Selecionar(acerto.Peca.Id, somar: false);

        MenuPedido?.Invoke(agora, acerto);
    }

    // ================= camadas de acerto =================
    //
    // Hover e clique usam ESTA funcao, e so ela. Antes cada um tinha a
    // sua logica: o marcador dizia "furo G16" e o clique fazia outra
    // coisa. A ordem abaixo e a regra do editor inteiro.
    //
    //   1 botao fisico da placa (BOOT / EN)
    //   2 pino de um componente        -> comeca fio
    //   3 corpo de um componente       -> seleciona e arrasta
    //   4 fio                          -> seleciona
    //   5 furo de protoboard           -> comeca fio
    //   6 corpo da protoboard          -> seleciona e arrasta
    //
    // O que resolve a reclamacao: o CORPO do componente vem ANTES do
    // furo da protoboard. Peca espetada na placa ganha o clique.
    public enum Camada
    {
        Nada, DobraFio, BotaoPlaca, ChaveDip, TeclaMatricial, FuroDoDisco,
        CapaBotao, PinoPeca, CorpoPeca, Furo, Fio, CorpoPlaca
    }

    public readonly record struct Acerto(
        Camada Camada, PecaNoCircuito Peca, string Pino, Point Posicao,
        int IndiceFio, string Botao)
    {
        /// <summary>Qual dobra do fio, quando a camada e DobraFio.</summary>
        public int IndiceDobra { get; init; }

        /// <summary>Qual das oito chaves, quando a camada e ChaveDip.</summary>
        public int IndiceChave { get; init; }

        /// <summary>A tecla do teclado matricial, ou o numero do disco.</summary>
        public string Rotulo { get; init; }

        /// <summary>A alca e de um canto que ainda nao existe?</summary>
        public bool DobraNova { get; init; }
    }

    const double RaioPino = 7;
    const double RaioFuro = 5;
    /// <summary>Alvo minimo do corpo: o resistor tem 11 px de altura e
    /// era praticamente impossivel acertar.</summary>
    const double AlvoMinimo = 20;

    static Rect CaixaDe(PecaNoCircuito peca, PecaDef def)
    {
        var (largura, altura) = Geometria.Tamanho(peca, def);
        double folgaX = Math.Max(0, (AlvoMinimo - largura) / 2);
        double folgaY = Math.Max(0, (AlvoMinimo - altura) / 2);
        return new Rect(peca.X - folgaX, peca.Y - folgaY,
                        largura + folgaX * 2, altura + folgaY * 2);
    }

    /// <summary>
    /// O ponto caiu no corpo desta peca, JA CONTANDO O GIRO.
    ///
    /// Era aqui o item 8 da lista do professor: "a peca inteira roda, os
    /// pontos e pinos, mas a tela em si continua no mesmo lugar". O
    /// desenho gira por um RotateTransform e os pinos por PosicaoLocal,
    /// mas o corpo era testado contra <see cref="CaixaDe"/>, um retangulo
    /// alinhado aos eixos que nunca olhou a rotacao. Um resistor deitado
    /// so respondia ao clique onde ele ESTARIA em pe.
    ///
    /// A correcao e uma so, e vale para todas as pecas: leva o ponto do
    /// clique para o espaco da peca e testa la, onde ela esta sempre em
    /// pe. Nao ha tabela por peca nem caso especial.
    /// </summary>
    static bool NoCorpo(PecaNoCircuito peca, PecaDef def, Point p)
    {
        var local = Geometria.PontoLocal(peca, def, p);
        var caixa = CorpoDe(def);

        // A mesma folga do alvo minimo, agora no espaco da peca: o
        // resistor tem 11 px de altura e era quase impossivel acertar.
        double folgaX = Math.Max(0, (AlvoMinimo - caixa.Width) / 2);
        double folgaY = Math.Max(0, (AlvoMinimo - caixa.Height) / 2);

        return local.X >= caixa.Left - folgaX && local.X <= caixa.Right + folgaX
               && local.Y >= caixa.Top - folgaY && local.Y <= caixa.Bottom + folgaY;
    }

    /// <summary>
    /// Ha uma peca encaixada nesta protoboard bem neste ponto?
    ///
    /// "Encaixada" no sentido literal: a peca esta por cima da placa e
    /// tem pino dentro dela. Uma peca que so passa por perto nao conta,
    /// e uma peca solta ao lado da protoboard tambem nao -- nesses casos
    /// o corpo continua ganhando, que e o comportamento de sempre.
    /// </summary>
    bool SobreAProtoboard(List<PecaNoCircuito> candidatas, PecaNoCircuito protoboard, Point p)
    {
        var daPlaca = Catalogo.Obter(protoboard.Tipo);
        if (daPlaca is null) return false;

        foreach (var peca in candidatas)
        {
            // So PLACA. Um resistor deitado tambem cobre furos, mas o
            // corpo dele e do tamanho do proprio componente: ali o clique
            // e para pegar o resistor, e sempre foi. A placa de
            // desenvolvimento e outra historia -- ela e um retangulo
            // grande que cobre fileiras inteiras sem usar nenhuma delas.
            if (!PerfilPlaca.EhPlaca(peca.Tipo)) continue;

            var def = Catalogo.Obter(peca.Tipo);
            if (def is null || !NoCorpoNaTela(peca, def, p)) continue;

            // A placa cobre este ponto. Ela esta espetada nesta protoboard?
            if (EncaixadaEm(peca, def, protoboard, daPlaca)) return true;
        }

        return false;
    }

    /// <summary>Algum pino desta peca cai dentro da area da protoboard.</summary>
    static bool EncaixadaEm(PecaNoCircuito peca, PecaDef def,
                            PecaNoCircuito protoboard, PecaDef daPlaca)
    {
        var area = CaixaNoMundo(protoboard, daPlaca);

        foreach (var pino in def.Pinos)
            if (area.Contains(Geometria.PosicaoLocal(peca, def, pino.X, pino.Y)))
                return true;

        return false;
    }

    /// <summary>
    /// A parte da caixa em que a peca REALMENTE esta desenhada.
    ///
    /// Era o item 9 da lista: com a ESP32 sobre a protoboard de 830,
    /// nao dava para espetar fio na fileira que sobrou. A causa era esta
    /// -- a caixa da ESP32 tem 204 px de altura, mas o desenho dela
    /// ocupa so o meio; as bordas vazias, em cima e embaixo, cobriam os
    /// trilhos de alimentacao e engoliam o clique deles.
    ///
    /// Testar pela TINTA faz o clique concordar com o que se ve. Quando
    /// a medida nao existe (peca sem desenho, ou desenho ainda nao
    /// carregado), vale a caixa inteira, que e o que valia antes.
    /// </summary>
    static Rect CorpoDe(PecaDef def)
    {
        var caixa = new Rect(0, 0, def.Largura, def.Altura);

        var tinta = Pecas.TintaDe(def.Id);
        if (tinta.IsEmpty || tinta.Width <= 0 || tinta.Height <= 0) return caixa;

        // Nunca MAIOR do que a caixa: a tinta de um SVG mal recortado
        // pode passar da borda, e ai quem manda continua sendo a caixa.
        tinta.Intersect(caixa);
        if (tinta.IsEmpty || tinta.Width < 2 || tinta.Height < 2) return caixa;

        return tinta;
    }

    internal Acerto Resolver(Point p)
    {
        // As pecas do outro lado de uma PCB virada nao recebem clique: o
        // clique e do furo e da trilha do lado que se ve.
        var naoPlacas = Atual.Pecas.Where(x => !Catalogo.EhProtoboard(x.Tipo) && !EstaDoOutroLado(x)).ToList();

        // 1) botao fisico
        foreach (var peca in naoPlacas)
        {
            var def = Catalogo.Obter(peca.Tipo);
            if (def is null) continue;
            string botao = BotaoDaPlaca(peca, def, p);
            if (botao is not null)
                return new Acerto(Camada.BotaoPlaca, peca, null, p, -1, botao);

            // O controle remoto tem vinte teclas desenhadas e nenhuma
            // funcionava: "esse tem botoes e nao sao operaveis".
            if (TeclaDoControle(peca, def, p) is { } tecla)
                return new Acerto(Camada.BotaoPlaca, peca, null, p, -1, tecla.Nome);

            // O controle de radio: os manches, as chaves e o POWER.
            if (RegiaoDoRadio(peca, def, p) is { } regiao)
                return new Acerto(Camada.BotaoPlaca, peca, null, p, -1, regiao);
        }

        // 1,5) alca de dobra do fio selecionado. Vem antes de tudo: ela
        // fica por cima do desenho e some quando o fio e desmarcado,
        // entao nunca rouba clique de outra coisa.
        if (fiosSelecionados.Count == 1)
        {
            foreach (var (pos, indice, nova) in AlcasDoFio(FioSelecionado))
            {
                double dx = pos.X - p.X, dy = pos.Y - p.Y;
                if (dx * dx + dy * dy > RaioDobra * RaioDobra * 2.2) continue;
                return new Acerto(Camada.DobraFio, null, null, pos, FioSelecionado, null)
                {
                    IndiceDobra = indice,
                    DobraNova = nova
                };
            }
        }

        // 2) chave do DIP. Vem antes de tudo o que e do corpo: as oito
        // chaves ficam no meio da peca, e quem clica nelas quer virar a
        // chave, nao arrastar o componente.
        for (int i = naoPlacas.Count - 1; i >= 0; i--)
        {
            var defDip = Catalogo.Obter(naoPlacas[i].Tipo);
            int qual = defDip is null ? -1 : ChaveDipNoPonto(naoPlacas[i], defDip, p);
            if (qual >= 0)
                return new Acerto(Camada.ChaveDip, naoPlacas[i], null, p, -1, null)
                {
                    IndiceChave = qual
                };
        }

        // 2,5) tecla do teclado matricial e furo do disco. Antes do
        // corpo pelo mesmo motivo da chave do DIP: quem clica numa tecla
        // quer digitar, nao arrastar a peca.
        for (int i = naoPlacas.Count - 1; i >= 0; i--)
        {
            var defTecla = Catalogo.Obter(naoPlacas[i].Tipo);
            if (defTecla is null) continue;

            if (TeclaMatricialNoPonto(naoPlacas[i], defTecla, p) is { } tecla)
                return new Acerto(Camada.TeclaMatricial, naoPlacas[i], null, p, -1, null)
                {
                    Rotulo = tecla
                };

            int numero = FuroDoDiscoNoPonto(naoPlacas[i], defTecla, p);
            if (numero >= 0)
                return new Acerto(Camada.FuroDoDisco, naoPlacas[i], null, p, -1, null)
                {
                    Rotulo = numero.ToString()
                };
        }

        // 3) capa de botao de modulo. Vem antes do pino porque no
        // botao de 6 mm a capa e do tamanho do raio do pino, e no meio
        // da peca quem manda e o aperto.
        for (int i = naoPlacas.Count - 1; i >= 0; i--)
        {
            var def = Catalogo.Obter(naoPlacas[i].Tipo);
            if (def is not null && NaCapaDoBotao(naoPlacas[i], def, p))
                return new Acerto(Camada.CapaBotao, naoPlacas[i], null, p, -1, null);
        }

        // 3) pino de componente
        var achado = PinoMaisPerto(naoPlacas, p, RaioPino);
        if (achado is not null)
            return new Acerto(Camada.PinoPeca, achado.Value.peca, achado.Value.pino,
                              achado.Value.pos, -1, null);

        // 3,5) furo LIVRE debaixo de uma peca encaixada na protoboard.
        //
        // Era o item 9 da lista: com a ESP32 espetada na protoboard de
        // 830, nao dava para por fio na fileira que sobrou. A ESP32 e um
        // retangulo de 107x204 que cobre a placa de ponta a ponta -- e
        // 142 dos 830 furos ficavam embaixo dela, sem alcance nenhum.
        //
        // Aqui o furo ganha do corpo. Nao de qualquer corpo: so do de uma
        // peca que esta ENCAIXADA naquela protoboard. E so de furo LIVRE,
        // porque o furo ocupado ja foi atendido como PINO no passo
        // anterior, que tem raio maior.
        //
        // Mover a peca continua funcionando: quem atravessa a protoboard
        // passa por cima do canal do meio, que nao tem furo nenhum -- e e
        // exatamente por ali que se pega um chip na bancada.
        var protoboards = Atual.Pecas.Where(x => Catalogo.EhProtoboard(x.Tipo)).ToList();
        var furoLivre = PinoMaisPerto(protoboards, p, RaioFuro);

        if (furoLivre is not null && SobreAProtoboard(naoPlacas, furoLivre.Value.peca, p))
            return new Acerto(Camada.Furo, furoLivre.Value.peca, furoLivre.Value.pino,
                              furoLivre.Value.pos, -1, null);

        // 4) corpo de componente (o ultimo da lista fica por cima)
        for (int i = naoPlacas.Count - 1; i >= 0; i--)
        {
            var def = Catalogo.Obter(naoPlacas[i].Tipo);
            if (def is not null && NoCorpoNaTela(naoPlacas[i], def, p))
                return new Acerto(Camada.CorpoPeca, naoPlacas[i], null, p, -1, null);
        }

        // 5) fio -- ANTES do furo. Sobre a protoboard os furos ficam a
        // cada 9,6 px e roubavam todo clique de fio que passasse por cima.
        int fio = FioPerto(p);
        if (fio >= 0) return new Acerto(Camada.Fio, null, null, p, fio, null);

        // 6) furo de protoboard. Raio menor que o dos pinos para o fio
        // ainda ter espaco entre um furo e outro.
        var placas = protoboards;
        if (furoLivre is not null)
            return new Acerto(Camada.Furo, furoLivre.Value.peca, furoLivre.Value.pino,
                              furoLivre.Value.pos, -1, null);

        // 7) corpo da protoboard
        for (int i = placas.Count - 1; i >= 0; i--)
        {
            var def = Catalogo.Obter(placas[i].Tipo);
            if (def is not null && NoCorpo(placas[i], def, p))
                return new Acerto(Camada.CorpoPlaca, placas[i], null, p, -1, null);
        }

        return new Acerto(Camada.Nada, null, null, p, -1, null);
    }

    static (PecaNoCircuito peca, string pino, Point pos)? PinoMaisPerto(
        List<PecaNoCircuito> candidatas, Point p, double raio)
    {
        (PecaNoCircuito, string, Point)? melhor = null;
        double menor = raio * raio;

        foreach (var peca in candidatas)
        {
            var def = Catalogo.Obter(peca.Tipo);
            if (def is null) continue;

            double raioCaixa = Math.Max(Geometria.Tamanho(peca, def).Largura, Geometria.Tamanho(peca, def).Altura) / 2 + raio + 4;
            double cx = peca.X + Geometria.Tamanho(peca, def).Largura / 2, cy = peca.Y + Geometria.Tamanho(peca, def).Altura / 2;
            if (Math.Abs(p.X - cx) > raioCaixa || Math.Abs(p.Y - cy) > raioCaixa) continue;

            foreach (var pino in def.Pinos)
            {
                var q = Geometria.PosicaoPino(peca, def, pino);
                double dx = q.X - p.X, dy = q.Y - p.Y;
                double d = dx * dx + dy * dy;
                if (d < menor) { menor = d; melhor = (peca, pino.Nome, q); }
            }
        }
        return melhor;
    }

    // ================= clique =================
    void AoApertar(object s, MouseButtonEventArgs e)
    {
        Focus();

        // Clique duplo num canto o desfaz, como no Wokwi. O Canvas nao
        // tem MouseDoubleClick (isso e de Control), entao vem da contagem.
        if (e.ClickCount == 2 && RemoverDobraEm(NoMundo(e))) { e.Handled = true; return; }

        if (Apertar(NoMundo(e))) e.Handled = true;
    }

    /// <summary>
    /// O clique de verdade, sem WPF no meio. Os handlers de mouse sao so
    /// adaptadores para isto: assim o autoteste exercita ESTE codigo, e
    /// nao uma copia dele que pode divergir.
    /// </summary>
    /// <returns>true quando o clique foi consumido aqui.</returns>
    internal bool Apertar(Point p)
    {
        var acerto = Resolver(p);

        switch (acerto.Camada)
        {
            case Camada.BotaoPlaca when acerto.Peca.Tipo == "radio-controle":
                ClicarNoRadio(acerto.Peca, acerto.Botao);
                return true;

            case Camada.BotaoPlaca when acerto.Peca.Tipo == "ir-remote":
            {
                var def = Catalogo.Obter(acerto.Peca.Tipo);
                if (def is not null && TeclaDoControle(acerto.Peca, def, p) is { } tecla)
                {
                    teclaDoControle[acerto.Peca.Id] = tecla;
                    DesenharBotoesDaPlaca();
                    CaptureMouse();
                }
                return true;
            }

            case Camada.BotaoPlaca:
                if (acerto.Botao == "BOOT") BootPressionado.Add(acerto.Peca.Id);
                else if (ResetSegurado.Add(acerto.Peca.Id)) ResetApertado?.Invoke(acerto.Peca.Id);
                DesenharBotoesDaPlaca();

                // Captura: soltar o mouse fora da tela ainda chega no
                // Soltar. Sem isto a placa ficaria presa no reset.
                CaptureMouse();
                return true;

            // Alca de dobra: pega e arrasta. A vazada vira canto de
            // verdade no momento em que e pega, e nao ao soltar -- assim
            // o fio ja acompanha o mouse desde o primeiro pixel.
            case Camada.DobraFio:
            {
                var fioAlvo = Atual.Fios[acerto.IndiceFio];
                int indice = acerto.IndiceDobra;

                if (acerto.DobraNova)
                {
                    fioAlvo.Dobras.Insert(indice,
                        new PontoFio { X = acerto.Posicao.X, Y = acerto.Posicao.Y });
                }
                arrastandoDobra = (acerto.IndiceFio, indice);
                RedesenharFios();
                CaptureMouse();
                return true;
            }

            // A tecla do teclado vale ENQUANTO esta apertada, como a
            // tecla de verdade: o getKey() do sketch le zero quando
            // ninguem esta com o dedo em cima.
            case Camada.TeclaMatricial:
                teclaMatricial[acerto.Peca.Id] = acerto.Rotulo;
                DesenharBotoesDaPlaca();
                CaptureMouse();
                return true;

            // O disco e um EVENTO: solta e ele volta girando, mandando
            // os pulsos. Clicar de novo no meio da volta nao atropela.
            case Camada.FuroDoDisco:
                if (!Discando(acerto.Peca.Id)
                    && int.TryParse(acerto.Rotulo, out int discado))
                    Discar(acerto.Peca.Id, discado);
                return true;

            // A chave do DIP vira no clique e fica: e uma chave, nao um
            // botao de pulso. Nao seleciona nem arrasta a peca.
            case Camada.ChaveDip:
            {
                bool ligada = (acerto.Peca.Bits & (1 << acerto.IndiceChave)) == 0;
                AlternarChaveDip(acerto.Peca, acerto.IndiceChave, ligada);
                return true;
            }

            // O manche do joystick se arrasta: segurar e mover leva os
            // dois eixos, e soltar devolve ao centro, como a mola do
            // joystick de verdade. Clicar sem arrastar aperta o SEL.
            case Camada.CapaBotao when acerto.Peca.Tipo == "analog-joystick":
                // Aperta JA: segurar o manche e apertar o SEL, como no
                // joystick de verdade. Se a mao comecar a arrastar, o
                // aperto e desfeito e o clique vira movimento.
                mancheArrastado = (acerto.Peca.Id, false);
                PressionarBotao(acerto.Peca);
                return true;

            // O pendrive sai no clique e volta no seguinte, com a simulacao
            // rodando ou nao: o sketch ve o pendrive sair e voltar.
            case Camada.CapaBotao when acerto.Peca.Tipo == "pendrive-usb":
                DefinirGiro(acerto.Peca, acerto.Peca.Giro > 0.5 ? 0 : 1);
                return true;

            // A chave deslizante nao e botao de pulso: o clique na
            // alavanca joga ela para o outro lado e ela fica la.
            case Camada.CapaBotao when acerto.Peca.Tipo == "slide-switch":
                DefinirGiro(acerto.Peca, acerto.Peca.Giro > 0.5 ? 0 : 1);
                return true;

            // Apertar nao seleciona nem arrasta -- e o mesmo caminho de
            // BOOT/EN, que sempre funcionaram justamente por sair aqui.
            case Camada.CapaBotao:
                PressionarBotao(acerto.Peca);
                return true;

            case Camada.PinoPeca:
            case Camada.Furo:
                LigarFio(acerto.Peca.Id, acerto.Pino);
                return true;
        }

        if (origemFio is not null) CancelarFio();

        if (acerto.Camada == Camada.Fio)
        {
            if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control)
                && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
                fiosSelecionados.Clear();
            if (!fiosSelecionados.Add(acerto.IndiceFio))
                fiosSelecionados.Remove(acerto.IndiceFio);
            selecionadas.Clear();
            RedesenharFios();
            AtualizarRealce();
            SelecaoMudou?.Invoke();
            return false;
        }

        bool somar = Keyboard.Modifiers.HasFlag(ModifierKeys.Control)
                     || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        var peca = acerto.Peca;

        // Vazio: comeca o laco de selecao.
        if (peca is null)
        {
            if (!somar) selecionadas.Clear();
            fiosSelecionados.Clear();

            // Redesenhar, e nao so limpar a lista: o fio guarda o destaque
            // no proprio traco, entao sem isto ele CONTINUAVA com cara de
            // selecionado e so perdia o brilho quando alguma outra coisa
            // redesenhava os fios.
            RedesenharFios();
            AtualizarRealce();
            SelecaoMudou?.Invoke();

            lacando = true;
            lacoInicio = p;
            Canvas.SetLeft(laco, p.X);
            Canvas.SetTop(laco, p.Y);
            laco.Width = laco.Height = 0;
            laco.Visibility = Visibility.Visible;
            CaptureMouse();
            return false;
        }

        // Clicar numa peca ja selecionada mantem o grupo: e assim que se
        // arrasta varias de uma vez.
        if (!selecionadas.Contains(peca.Id)) Selecionar(peca.Id, somar);
        else if (somar) { Selecionar(peca.Id, true); return false; }

        RedesenharFios();

        arrastando = peca.Id;
        moveuDeVerdade = false;
        ondePegou = new Point(p.X - peca.X, p.Y - peca.Y);
        pegouEm = p;

        // Arrastar a protoboard leva junto tudo que esta espetado nela.
        // Sem isso mover a placa deixava as pecas para tras, e era a
        // prova de que ela nao passava de um desenho de fundo.
        carona.Clear();

        // Todas as outras selecionadas vao junto.
        foreach (string id in selecionadas)
        {
            if (id == peca.Id) continue;
            var outra = Atual.Achar(id);
            if (outra is not null) carona.Add((id, outra.X - peca.X, outra.Y - peca.Y));
        }

        // E quem estiver espetado numa protoboard arrastada.
        if (Catalogo.EhProtoboard(peca.Tipo))
            foreach (var outra in EspetadasEm(peca))
                if (!selecionadas.Contains(outra.Id))
                    carona.Add((outra.Id, outra.X - peca.X, outra.Y - peca.Y));

        CaptureMouse();
        return false;
    }

    readonly List<(string id, double dx, double dy)> carona = [];

    /// <summary>Pecas com pelo menos uma perna dentro de um furo desta placa.</summary>
    IEnumerable<PecaNoCircuito> EspetadasEm(PecaNoCircuito placa)
    {
        var defPlaca = Catalogo.Obter(placa.Tipo);
        if (defPlaca is null) yield break;

        foreach (var peca in Atual.Pecas)
        {
            if (peca == placa || Catalogo.EhProtoboard(peca.Tipo)) continue;
            var def = Catalogo.Obter(peca.Tipo);
            if (def is null) continue;

            bool espetada = false;
            foreach (var pino in def.Pinos)
            {
                var q = Geometria.PosicaoPino(peca, def, pino);
                foreach (var furo in defPlaca.Pinos)
                {
                    var pf = Geometria.PosicaoPino(placa, defPlaca, furo);
                    double dx = pf.X - q.X, dy = pf.Y - q.Y;
                    if (dx * dx + dy * dy <= Netlist.RaioDoFuro * Netlist.RaioDoFuro)
                    { espetada = true; break; }
                }
                if (espetada) break;
            }
            if (espetada) yield return peca;
        }
    }

    void LigarFio(string idPeca, string pino)
    {
        if (origemFio is null)
        {
            origemFio = (idPeca, pino);
            fioEmCurso.Stroke = Pincel(CorDoFio);
            fioEmCurso.Visibility = Visibility.Visible;
            return;
        }

        if (origemFio.Value.peca == idPeca && origemFio.Value.pino == pino) return;

        // Com a PCB virada, o fio entre os furos dela e a trilha de baixo.
        string pcbDoFio = PcbDaPonta(origemFio.Value.peca) ?? PcbDaPonta(idPeca);
        Atual.Fios.Add(new Fio
        {
            DePeca = origemFio.Value.peca,
            DePino = origemFio.Value.pino,
            ParaPeca = idPeca,
            ParaPino = pino,
            Cor = CorDoFio,
            Lado = pcbDoFio is not null && Atual.Achar(pcbDoFio)?.Virada == true ? Fio.LadoDeBaixo : null,
        });
        // A trilha de baixo e o fio de estanho, reto, de furo a furo.
        if (Atual.Fios[^1].Lado == Fio.LadoDeBaixo)
        {
            Atual.Fios[^1].Cor = CorDaTrilha;
            Atual.Fios[^1].Estilo = Fio.EstiloReto;
        }
        CancelarFio();
        RedesenharFios();
        Mudou?.Invoke();
    }

    /// <summary>Esc, ou clique no vazio, abandona o fio em construcao.</summary>
    public void CancelarFio()
    {
        origemFio = null;
        fioEmCurso.Visibility = Visibility.Collapsed;
    }

    public bool LigandoFio => origemFio is not null;

    // ================= movimento =================
    void AoMover(object s, MouseEventArgs e)
    {
        if (panejando)
        {
            var atual = e.GetPosition(this);
            deslocamento.X += atual.X - panInicio.X;
            deslocamento.Y += atual.Y - panInicio.Y;
            panInicio = atual;
            return;
        }

        Mover(NoMundo(e), e.LeftButton == MouseButtonState.Pressed);
    }

    /// <summary>Mover o mouse, sem WPF no meio.</summary>
    internal void Mover(Point p, bool esquerdoApertado)
    {

        // O manche do controle de radio, idem.
        if (esquerdoApertado && MoverMancheDoRadio(p)) return;

        // O manche vem antes de tudo: enquanto ele esta na mao, o mouse
        // e dele e nao do laco nem do arraste de peca.
        if (mancheArrastado is { } manche && esquerdoApertado)
        {
            var peca = Atual.Achar(manche.Peca);
            var def = peca is null ? null : Catalogo.Obter(peca.Tipo);

            if (def is not null && CapasDeBotao.TryGetValue(peca.Tipo, out var capa))
            {
                var centro = Geometria.PosicaoLocal(peca, def, capa.X, capa.Y);

                double dx = Math.Clamp((p.X - centro.X) / CursoDoManche, -1, 1);
                double dy = Math.Clamp((p.Y - centro.Y) / CursoDoManche, -1, 1);

                if (!manche.Mexeu && (Math.Abs(dx) > 0.08 || Math.Abs(dy) > 0.08))
                {
                    mancheArrastado = (manche.Peca, true);

                    // Saiu do centro: nao era um aperto, era movimento.
                    BotoesPressionados.Remove(peca.Id);
                    DesenharBotoesDaPlaca();
                }

                // Y da tela cresce para baixo; no eixo do joystick, para
                // cima e valor maior.
                DefinirGiro(peca, 0.5 + dx / 2);
                DefinirGiro2(peca, 0.5 - dy / 2);
            }
            return;
        }
        // Marcador so aparece onde o clique REALMENTE vai ligar um fio.
        var acerto = Resolver(p);
        bool ehPino = acerto.Camada is Camada.PinoPeca or Camada.Furo;

        if (ehPino)
        {
            Canvas.SetLeft(marcadorPino, acerto.Posicao.X - marcadorPino.Width / 2);
            Canvas.SetTop(marcadorPino, acerto.Posicao.Y - marcadorPino.Height / 2);
            marcadorPino.Visibility = Visibility.Visible;

            textoPino.Text = NomeAmigavel(acerto.Pino);
            Canvas.SetLeft(rotuloPino, acerto.Posicao.X + 10);
            Canvas.SetTop(rotuloPino, acerto.Posicao.Y - 20);
            rotuloPino.Visibility = Visibility.Visible;
        }
        else EsconderMarcador();

        Cursor = acerto.Camada switch
        {
            Camada.DobraFio => Cursors.SizeAll,
            Camada.PinoPeca or Camada.Furo => Cursors.Cross,
            // Sem a mao ninguem descobre que aquele ponto aperta.
            Camada.BotaoPlaca or Camada.CapaBotao => Cursors.Hand,
            Camada.CorpoPeca or Camada.CorpoPlaca => Cursors.SizeAll,
            _ => Cursors.Arrow
        };

        if (origemFio is not null)
        {
            var a = PosicaoPino(origemFio.Value.peca, origemFio.Value.pino);
            if (a is not null)
                fioEmCurso.Data = Curva(EstiloPadrao, a.Value, ehPino ? acerto.Posicao : p);
        }

        if (lacando && esquerdoApertado)
        {
            var area = new Rect(lacoInicio, p);
            Canvas.SetLeft(laco, area.X);
            Canvas.SetTop(laco, area.Y);
            laco.Width = area.Width;
            laco.Height = area.Height;
            return;
        }

        // Arrastando um canto do fio: mexe so nele.
        if (arrastandoDobra is { } alvoDobra && esquerdoApertado)
        {
            if (alvoDobra.fio < Atual.Fios.Count)
            {
                var f = Atual.Fios[alvoDobra.fio];
                if (alvoDobra.dobra < f.Dobras.Count)
                {
                    var onde = EncaixarDobra(f, alvoDobra.dobra, p);
                    f.Dobras[alvoDobra.dobra].X = onde.X;
                    f.Dobras[alvoDobra.dobra].Y = onde.Y;
                    RedesenharFios();
                }
            }
            return;
        }

        if (arrastando is null || !esquerdoApertado) return;

        // Enquanto a mao nao passar do limiar, o clique ainda e um clique.
        if (!moveuDeVerdade
            && Math.Abs(p.X - pegouEm.X) < LimiarArraste
            && Math.Abs(p.Y - pegouEm.Y) < LimiarArraste) return;

        var peca2 = Atual.Achar(arrastando);
        if (peca2 is null) return;

        double nx = p.X - ondePegou.X, ny = p.Y - ondePegou.Y;
        bool livre = Keyboard.IsKeyDown(Key.LeftAlt);
        if (!livre)
        {
            nx = Math.Round(nx / PassoGrade) * PassoGrade;
            ny = Math.Round(ny / PassoGrade) * PassoGrade;
        }
        if (Math.Abs(nx - peca2.X) > 0.01 || Math.Abs(ny - peca2.Y) > 0.01) moveuDeVerdade = true;

        peca2.X = nx; peca2.Y = ny;
        var def2 = Catalogo.Obter(peca2.Tipo);

        // Gruda no furo ja durante o arraste (Alt segura solta a peca).
        if (def2 is not null && !livre && !Catalogo.EhProtoboard(peca2.Tipo))
        {
            var ajuste = Geometria.AjusteParaFuro(peca2, def2, Atual, Catalogo.Passo * 0.95);
            if (ajuste is not null) { peca2.X += ajuste.Value.X; peca2.Y += ajuste.Value.Y; }
        }

        // Placa arrastada leva a carona quem esta espetado nela.
        foreach (var (id, dx, dy) in carona)
        {
            var passageira = Atual.Achar(id);
            if (passageira is null) continue;
            passageira.X = peca2.X + dx;
            passageira.Y = peca2.Y + dy;
            Reposicionar(passageira);
        }

        Reposicionar(peca2);
        RedesenharFios();
        AtualizarRealce();

        // Refaz a rede a cada passo do arraste: assim os slots
        // preenchidos e os pinos ligados acompanham em tempo real,
        // igual aos aneis dos botoes.
        if (moveuDeVerdade) Arrastou?.Invoke();
    }

    /// <summary>
    /// A peca "Texto": uma anotacao solta no circuito.
    ///
    /// Nao tem pino nem SVG -- o desenho E o texto. Serve para escrever
    /// no circuito o que o desenho nao diz: a tensao de um trilho, o que
    /// um bloco faz, o nome da etapa do exercicio.
    ///
    /// O conteudo mora no mesmo campo "Nome" das outras pecas: quem ja
    /// sabe nomear um LED sabe escrever aqui, e o texto vai junto no
    /// .sktch sem campo novo nenhum.
    /// </summary>
    TextBlock DesenharTexto(PecaNoCircuito peca)
    {
        var alvo = new TextBlock
        {
            Text = TextoDaAnotacao(peca),
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Foreground = Tema.Pincel(Tema.TextoForte),
            TextWrapping = TextWrapping.NoWrap,
            Padding = new Thickness(4, 2, 4, 2),

            // Fundo quase transparente: o texto precisa ser pego pelo
            // clique mesmo onde nao ha letra, senao so da para arrastar
            // acertando o traco de uma letra.
            Background = new SolidColorBrush(Color.FromArgb(0x18, 0x80, 0x80, 0x80)),
        };

        alvo.Effect = new System.Windows.Media.Effects.DropShadowEffect
        {
            Color = Colors.Black, BlurRadius = 6, ShadowDepth = 0, Opacity = 0.9,
        };

        return alvo;
    }

    static string TextoDaAnotacao(PecaNoCircuito peca)
        => string.IsNullOrWhiteSpace(peca.Nome) ? "escreva aqui" : peca.Nome;

    /// <summary>
    /// Quanto cada anotacao ocupa depois de medida.
    ///
    /// O PecaDef do catalogo e um so para todas as anotacoes e tem
    /// tamanho fixo; o alvo do clique tem que acompanhar o que foi
    /// escrito, senao um texto longo so e pego pelos primeiros 120 px.
    /// </summary>
    readonly Dictionary<string, Size> caixasDeTexto = [];

    void AjustarCaixaDoTexto(PecaNoCircuito peca)
    {
        if (peca.Tipo != "texto") return;
        if (!visuais.TryGetValue(peca.Id, out var v) || v is not TextBlock alvo) return;

        alvo.Text = TextoDaAnotacao(peca);
        alvo.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

        caixasDeTexto[peca.Id] = new Size(
            Math.Max(40, alvo.DesiredSize.Width), Math.Max(20, alvo.DesiredSize.Height));
    }

    Rect CaixaNaTela(PecaNoCircuito peca, PecaDef def)
        => peca.Tipo == "texto" && caixasDeTexto.TryGetValue(peca.Id, out var medida)
            ? new Rect(peca.X, peca.Y, medida.Width, medida.Height)
            : CaixaDe(peca, def);

    /// <summary>
    /// O clique caiu na peca, tratando o Texto a parte.
    ///
    /// O Texto nao gira com a peca -- ele e um TextBlock que fica sempre
    /// em pe, como o nome embaixo dos componentes --, entao a caixa dele
    /// continua sendo a medida do texto, alinhada aos eixos.
    /// </summary>
    bool NoCorpoNaTela(PecaNoCircuito peca, PecaDef def, Point p)
        => peca.Tipo == "texto" && caixasDeTexto.TryGetValue(peca.Id, out var medida)
            ? new Rect(peca.X, peca.Y, medida.Width, medida.Height).Contains(p)
            : NoCorpo(peca, def, p);

    /// <summary>
    /// O nome da peca, embaixo dela.
    ///
    /// Fica num TextBlock proprio, e nao dentro do desenho: o desenho e um
    /// SVG que gira com a peca, e o nome tem que continuar em pe -- girar
    /// o componente nao vira o rotulo de cabeca para baixo.
    /// </summary>
    void AtualizarRotulo(PecaNoCircuito peca, PecaDef def)
    {
        // Na peca Texto o nome JA E o desenho: repetir embaixo dela
        // seria o mesmo texto duas vezes.
        if (peca.Tipo == "texto")
        {
            AjustarCaixaDoTexto(peca);
            if (rotulosDePeca.Remove(peca.Id, out var doTexto)) mundo.Children.Remove(doTexto);
            return;
        }

        string texto = string.IsNullOrWhiteSpace(peca.Nome) && peca.Tipo == "wifi-ap"
            ? peca.Ssid
            : peca.Nome;

        if (string.IsNullOrWhiteSpace(texto))
        {
            if (rotulosDePeca.Remove(peca.Id, out var antigo)) mundo.Children.Remove(antigo);
            return;
        }

        if (!rotulosDePeca.TryGetValue(peca.Id, out var rotulo))
        {
            rotulo = new TextBlock
            {
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(0xCF, 0xCF, 0xD4)),
                IsHitTestVisible = false,

                // Sombra preta por tras: o nome cai em cima da protoboard
                // branca tanto quanto em cima do fundo escuro.
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = Colors.Black, BlurRadius = 6, ShadowDepth = 0, Opacity = 0.95
                },
            };
            Panel.SetZIndex(rotulo, 600);
            mundo.Children.Add(rotulo);
            rotulosDePeca[peca.Id] = rotulo;
        }

        rotulo.Text = texto;
        rotulo.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

        var caixa = CaixaNoMundo(peca, def);
        Canvas.SetLeft(rotulo, caixa.X + caixa.Width / 2 - rotulo.DesiredSize.Width / 2);
        Canvas.SetTop(rotulo, caixa.Bottom + 3);
    }

    /// <summary>A caixa da peca no mundo, ja contando o giro.</summary>
    static Rect CaixaNoMundo(PecaNoCircuito peca, PecaDef def)
    {
        var cantos = new[]
        {
            Geometria.PosicaoLocal(peca, def, 0, 0),
            Geometria.PosicaoLocal(peca, def, def.Largura, 0),
            Geometria.PosicaoLocal(peca, def, def.Largura, def.Altura),
            Geometria.PosicaoLocal(peca, def, 0, def.Altura),
        };
        double x1 = cantos.Min(c => c.X), y1 = cantos.Min(c => c.Y);
        double x2 = cantos.Max(c => c.X), y2 = cantos.Max(c => c.Y);
        return new Rect(x1, y1, x2 - x1, y2 - y1);
    }

    /// <summary>
    /// Troca o nome da peca. Enquanto a pessoa digita, "avisar" e falso:
    /// um passo de desfazer por letra digitada nao ajuda ninguem.
    /// </summary>
    public void DefinirNome(PecaNoCircuito peca, string nome, bool avisar = true)
    {
        if (peca is null) return;
        peca.Nome = string.IsNullOrWhiteSpace(nome) ? null : nome.Trim();

        var def = Catalogo.Obter(peca.Tipo);
        if (def is not null) AtualizarRotulo(peca, def);
        if (avisar) Mudou?.Invoke();
    }

    /// <summary>Para o teste: o nome desenhado embaixo da peca, se houver.</summary>
    internal string RotuloParaTeste(string idDaPeca)
        => rotulosDePeca.TryGetValue(idDaPeca, out var r) ? r.Text : null;

    /// <summary>Para o teste: apagar o nome tira o rotulo da tela.</summary>
    internal bool DefinirNomeVazioParaTeste(PecaNoCircuito peca)
    {
        DefinirNome(peca, "");
        return RotuloParaTeste(peca.Id) is null;
    }

    void Reposicionar(PecaNoCircuito peca)
    {
        var def = Catalogo.Obter(peca.Tipo);
        if (def is not null) AtualizarRotulo(peca, def);
        if (visuais.TryGetValue(peca.Id, out var v))
        { Canvas.SetLeft(v, peca.X); Canvas.SetTop(v, peca.Y); }
        if (def is not null) PosicionarMovel(peca, def);
        if (def is not null && peca.Tipo == "dip-switch-8") PosicionarChavesDip(peca, def);
        if (def is not null)
        {
            PosicionarBrilho(peca, def);
            PosicionarEstouro(peca, def);
            PosicionarTft(peca, def);
        }
        if (def is not null && BracosDeServo.ContainsKey(peca.Tipo)) PosicionarBracoServo(peca, def);
        if (def is not null && peca.Tipo == "esp32-devkit-v1") PosicionarLedsDaPlaca(peca, def);
        if (def is not null) PosicionarLuzesDeModulo(peca, def);
        if (peca.Tipo == "radio-controle") AtualizarRadio(peca);

        if (Grades.TryGetValue(peca.Tipo, out var g)
            && telasLcd.TryGetValue(peca.Id, out var tela))
        {
            for (int lin = 0; lin < g.Linhas; lin++)
                for (int col = 0; col < g.Colunas; col++)
                {
                    Canvas.SetLeft(tela[lin, col], peca.X + g.X + col * g.Larg);
                    Canvas.SetTop(tela[lin, col], peca.Y + g.Y + lin * g.Alt - g.Fonte * 0.08);
                }
        }

        // Os aneis de BOOT/EN sao desenhados a parte: sem isto ficavam
        // para tras quando a placa era arrastada.
        if (BotoesPorPlaca.ContainsKey(peca.Tipo)) DesenharBotoesDaPlaca();
    }

    /// <summary>Nome legivel: no LED, dizer qual perna e + e qual e menos.</summary>
    static string NomeAmigavel(string pino) => pino switch
    {
        "A" => "A  (+ anodo)",
        "C" => "C  (- catodo)",
        "VCC" => "VCC  (+)",
        "GND" => "GND  (-)",
        _ => pino
    };

    void AoSoltar(object s, MouseButtonEventArgs e) => Soltar();

    /// <summary>Soltar o botao do mouse, sem WPF no meio.</summary>
    internal void Soltar()
    {
        if (SoltarMancheDoRadio()) return;

        bool tinhaBotaoDePlaca = BootPressionado.Count > 0 || ResetSegurado.Count > 0
                                 || teclaDoControle.Count > 0;
        BootPressionado.Clear();
        teclaDoControle.Clear();
        if (ResetSegurado.Count > 0)
        {
            var soltos = ResetSegurado.ToList();
            ResetSegurado.Clear();
            foreach (string id in soltos) ResetSolto?.Invoke(id);
        }
        if (tinhaBotaoDePlaca)
        {
            DesenharBotoesDaPlaca();
            ReleaseMouseCapture();
        }

        if (arrastandoDobra is { } dobraSolta)
        {
            arrastandoDobra = null;
            ReleaseMouseCapture();
            LimparDobrasRedundantes(dobraSolta.fio);
            RedesenharFios();
            SelecaoMudou?.Invoke();
            Mudou?.Invoke();
            return;
        }

        if (lacando)
        {
            lacando = false;
            laco.Visibility = Visibility.Collapsed;
            ReleaseMouseCapture();

            var area = new Rect(Canvas.GetLeft(laco), Canvas.GetTop(laco),
                                laco.Width, laco.Height);

            // Clique seco (laco minusculo) nao seleciona nada.
            if (area.Width > 4 || area.Height > 4)
            {
                foreach (var achada in Geometria.NoRetangulo(Atual, area))
                    selecionadas.Add(achada.Id);
                foreach (int i in FiosNoRetangulo(area)) fiosSelecionados.Add(i);
                AtualizarRealce();
                RedesenharFios();
                SelecaoMudou?.Invoke();
            }
            return;
        }

        // So encaixa quando arrasta UMA peca: encaixar o grupo inteiro
        // deslocaria pecas que ja estavam no lugar certo.
        if (arrastando is not null && moveuDeVerdade && selecionadas.Count <= 1
            && !Catalogo.EhProtoboard(Atual.Achar(arrastando)?.Tipo ?? ""))
            EncaixarNosFuros(arrastando);

        // Roda e helice soltas perto de um eixo encaixam nele.
        if (arrastando is not null && moveuDeVerdade && selecionadas.Count <= 1)
            EncaixarAcessorio(arrastando);

        if (mancheArrastado is { } largou)
        {
            var peca = Atual.Achar(largou.Peca);
            // A mola devolve os dois eixos ao meio. O aperto do SEL,
            // se houve, e solto logo abaixo com os outros botoes.
            if (peca is not null)
            {
                DefinirGiro(peca, 0.5);
                DefinirGiro2(peca, 0.5);
            }

            mancheArrastado = null;
        }

        if (teclaMatricial.Count > 0)
        {
            teclaMatricial.Clear();
            DesenharBotoesDaPlaca();
        }

        if (BotoesPressionados.Count > 0)
        {
            foreach (string id in BotoesPressionados)
                if (Atual.Achar(id) is { } apertada) DesenhoDoBotao(apertada, "solto");
            BotoesPressionados.Clear();
            DesenharBotoesDaPlaca();
            ReleaseMouseCapture();
        }

        if (arrastando is not null)
        {
            ReleaseMouseCapture();
            arrastando = null;
            carona.Clear();
            if (moveuDeVerdade) Mudou?.Invoke();
        }
    }

    void EncaixarNosFuros(string idPeca)
    {
        var peca = Atual.Achar(idPeca);
        var def = peca is null ? null : Catalogo.Obter(peca.Tipo);
        if (def is null) return;

        var ajuste = Geometria.AjusteParaFuro(peca, def, Atual, Catalogo.Passo * 0.95);
        if (ajuste is null) return;

        peca.X += ajuste.Value.X;
        peca.Y += ajuste.Value.Y;
        Reposicionar(peca);
        RedesenharFios();
        AtualizarRealce();
    }

    void AoGirarRoda(object s, MouseWheelEventArgs e)
    {
        var antes = NoMundo(e);

        // Roda do mouse sobre um potenciometro gira o potenciometro, nao
        // a tela. Sem isso ele era so enfeite: nada escrevia o valor
        // analogico e analogRead devolvia zero para sempre.
        // Vale o corpo E os pinos: passar a roda em cima do potenciometro
        // deve girar o potenciometro, nao dar zoom, mesmo que o cursor
        // esteja perto de uma perna.
        var sob = Resolver(antes);
        // Vale para o potenciometro e para qualquer sensor com controle.
        var alvoPot = sob.Peca is not null
            && (sob.Peca.Tipo is "potentiometer" or "ky-040"
                || Comportamento.De(sob.Peca.Tipo) is { TemControle: true, Chave: false })
            ? sob.Peca : null;

        if (alvoPot is not null)
        {
            // No encoder, um clique da roda do mouse e um clique dele: um ciclo inteiro (4 passos).
            double passo = alvoPot.Tipo == "ky-040" ? 4 / 40.0
                         : Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 0.01 : 0.05;
            AjustarPotenciometro(alvoPot, e.Delta > 0 ? passo : -passo);
            e.Handled = true;
            return;
        }
        AplicarZoom(escala.ScaleX * (e.Delta > 0 ? 1.12 : 1 / 1.12), antes, e.GetPosition(this));
        e.Handled = true;
    }

    /// <summary>
    /// Define a posicao do potenciometro (0 a 1).
    ///
    /// NAO dispara SelecaoMudou: isso reconstroi a tab, e o slider que o
    /// usuario esta arrastando seria destruido e recriado a cada
    /// movimento -- ele pulava direto para 0 ou 1.
    /// </summary>
    public void DefinirGiro(PecaNoCircuito peca, double valor)
    {
        if (peca is null) return;
        if (peca.Tipo is not ("potentiometer" or "analog-joystick" or "ky-040" or "pendrive-usb")
            && Comportamento.De(peca.Tipo) is not { TemControle: true })
            return;

        // O encoder nao tem batente: da a volta.
        double novo = peca.Tipo == "ky-040" ? valor - Math.Floor(valor) : Math.Clamp(valor, 0, 1);
        if (Math.Abs(novo - peca.Giro) < 1e-9) return;

        peca.Giro = novo;

        // O desenho segue o controle na hora, mesmo com a simulacao
        // parada: quem arrasta o botao quer ver o botao virar.
        var defGiro = Catalogo.Obter(peca.Tipo);
        if (defGiro is not null) AnimarParteMovel(peca, defGiro);

        Arrastou?.Invoke();
        GiroMudou?.Invoke(peca);
    }

    /// <summary>Segundo eixo, do joystick.</summary>
    public void DefinirGiro2(PecaNoCircuito peca, double valor)
    {
        if (peca is null || peca.Tipo != "analog-joystick") return;
        double novo = Math.Clamp(valor, 0, 1);
        if (Math.Abs(novo - peca.Giro2) < 1e-9) return;
        peca.Giro2 = novo;

        var defGiro2 = Catalogo.Obter(peca.Tipo);
        if (defGiro2 is not null) AnimarParteMovel(peca, defGiro2);

        Arrastou?.Invoke();
        GiroMudou?.Invoke(peca);
    }

    /// <summary>Liga ou desliga uma das 8 chaves do DIP.</summary>
    public void AlternarChaveDip(PecaNoCircuito peca, int indice, bool ligada)
    {
        if (peca is null || peca.Tipo != "dip-switch-8") return;
        int mascara = 1 << indice;
        peca.Bits = ligada ? peca.Bits | mascara : peca.Bits & ~mascara;

        var defDip = Catalogo.Obter(peca.Tipo);
        if (defDip is not null) MostrarChavesDip(peca, defDip);

        Arrastou?.Invoke();
    }

    /// <summary>Passo relativo, usado pela roda do mouse.</summary>
    public void AjustarPotenciometro(PecaNoCircuito peca, double passo)
        => DefinirGiro(peca, (peca?.Giro ?? 0) + passo);

    /// <summary>Avisa quem mostra a leitura, sem remontar a tab.</summary>
    public event Action<PecaNoCircuito> GiroMudou;

    void AplicarZoom(double novo, Point ancoraMundo, Point ancoraTela)
    {
        novo = Math.Clamp(novo, 0.15, 6);
        escala.ScaleX = escala.ScaleY = novo;
        deslocamento.X = ancoraTela.X - ancoraMundo.X * novo;
        deslocamento.Y = ancoraTela.Y - ancoraMundo.Y * novo;
        AplicarGrade();
    }

    // ================= comandos =================
    public void Deslocar(double dx, double dy)
    {
        deslocamento.X += dx;
        deslocamento.Y += dy;
        AplicarGrade();
    }

    /// <summary>
    /// Poe um ponto do circuito no meio da tela, com o zoom pedido.
    ///
    /// Existe para a captura de tela: revisar se a arte de uma peca cai
    /// em cima dos pinos dela pede olhar a peca de perto, e nao a tela
    /// inteira de longe.
    /// </summary>
    public void OlharPara(Point mundo, double zoom)
    {
        AplicarZoom(zoom, mundo, new Point(ActualWidth / 2, ActualHeight / 2));
        RedesenharFios();
    }

    public void MudarZoom(double fator)
    {
        var centroTela = new Point(ActualWidth / 2, ActualHeight / 2);
        var centroMundo = new Point((centroTela.X - deslocamento.X) / escala.ScaleX,
                                    (centroTela.Y - deslocamento.Y) / escala.ScaleY);
        AplicarZoom(escala.ScaleX * fator, centroMundo, centroTela);
    }

    public void Ajustar()
    {
        if (Atual.Pecas.Count == 0)
        {
            escala.ScaleX = escala.ScaleY = 1;
            deslocamento.X = deslocamento.Y = 40;
            return;
        }

        double x1 = double.MaxValue, y1 = double.MaxValue, x2 = double.MinValue, y2 = double.MinValue;
        foreach (var peca in Atual.Pecas)
        {
            var def = Catalogo.Obter(peca.Tipo);
            if (def is null) continue;
            x1 = Math.Min(x1, peca.X); y1 = Math.Min(y1, peca.Y);
            x2 = Math.Max(x2, peca.X + Geometria.Tamanho(peca, def).Largura); y2 = Math.Max(y2, peca.Y + Geometria.Tamanho(peca, def).Altura);
        }
        if (x2 <= x1 || y2 <= y1) return;

        double margem = 40;
        double z = Math.Clamp(Math.Min((ActualWidth - margem * 2) / (x2 - x1),
                                       (ActualHeight - margem * 2) / (y2 - y1)), 0.15, 3);
        escala.ScaleX = escala.ScaleY = z;
        deslocamento.X = (ActualWidth - (x2 - x1) * z) / 2 - x1 * z;
        deslocamento.Y = (ActualHeight - (y2 - y1) * z) / 2 - y1 * z;
    }

    /// <summary>
    /// Poe uma peca no circuito. Sem <paramref name="onde"/> ela vai
    /// para o meio da tela; com, vai para onde foi solta.
    /// </summary>
    public PecaNoCircuito Adicionar(string tipo, Point? onde = null)
    {
        var def = Catalogo.Obter(tipo);
        if (def is null) return null;

        var centro = onde ?? new Point(
            (ActualWidth / 2 - deslocamento.X) / escala.ScaleX,
            (ActualHeight / 2 - deslocamento.Y) / escala.ScaleY);
        var peca = new PecaNoCircuito
        {
            // Id legivel, no jeito do Wokwi ("led1", "r2", "esp1"): e por ele
            // que o diagram.json se le.
            Id = Atual.NovoId(tipo),
            Tipo = tipo,
            X = Math.Round((centro.X - def.Largura / 2) / PassoGrade) * PassoGrade,
            Y = Math.Round((centro.Y - def.Altura / 2) / PassoGrade) * PassoGrade,
            Ssid = tipo == "wifi-ap" ? "CircuitSketch-AP" : null,

            // O joystick em repouso fica no CENTRO, e nao no canto: os
            // dois eixos de um analogico solto ficam na metade da faixa.
            // Giro nasce em 0, que punha a alavanca no canto de baixo.
            Giro = tipo == "analog-joystick" ? 0.5 : 0,
        };
        // O regulador com trimpot sai de fabrica nos 5 V.
        if (Reguladores.VoltsDeFabrica(tipo) is { } volts) peca.Volts = volts;

        Atual.Pecas.Add(peca);
        CriarVisual(peca);
        EncaixarNosFuros(peca.Id);
        Selecionar(peca.Id, false);
        Mudou?.Invoke();
        return peca;
    }

    /// <summary>Gira a peca selecionada em passos de 90 graus.</summary>
    /// <summary>
    /// As animacoes estao ligadas.
    ///
    /// Desligadas no autoteste, que nao tem laco de quadros para
    /// termina-las, e desligaveis nas configuracoes para quem acha o
    /// movimento incomodo.
    /// </summary>
    public bool Animar { get; set; } = !Autoteste.Rodando;

    public void Girar()
    {
        if (selecionadas.Count == 0) return;
        foreach (string id in selecionadas)
        {
            var peca = Atual.Achar(id);
            var def = peca is null ? null : Catalogo.Obter(peca.Tipo);
            if (def is null) continue;

            double antes = peca.Rotacao;
            peca.Rotacao = (peca.Rotacao + 90) % 360;

            if (visuais.TryGetValue(peca.Id, out var v))
            {
                Posicionar(v, peca);

                // Sem animacao durante um teste: a suite roda sem laco de
                // quadros, e uma animacao pendurada nao terminaria nunca.
                if (Animar) AnimarGiro(v, antes, peca.Rotacao);
            }

            PosicionarBrilho(peca, def);
            PosicionarEstouro(peca, def);
            PosicionarTft(peca, def);
        }
        RedesenharFios();
        AtualizarRealce();
        Mudou?.Invoke();
    }

    /// <summary>Espelha a peca na horizontal.</summary>
    public void Espelhar()
    {
        if (selecionadas.Count == 0) return;
        foreach (string id in selecionadas)
        {
            var peca = Atual.Achar(id);
            var def = peca is null ? null : Catalogo.Obter(peca.Tipo);
            if (def is null) continue;

            peca.Espelhada = !peca.Espelhada;
            if (visuais.TryGetValue(peca.Id, out var v)) Posicionar(v, peca);
            PosicionarBrilho(peca, def);
            PosicionarEstouro(peca, def);
            PosicionarTft(peca, def);
        }
        RedesenharFios();
        AtualizarRealce();
        Mudou?.Invoke();
    }

    /// <summary>As cores das ESCs (o termo-retratil da simples, a placa da 4-em-1).</summary>
    internal static readonly (string id, string nome, string hex)[] CoresDeEsc =
    [
        ("azul", "Azul", "#1f5fb4"), ("preto", "Preto", "#26282c"), ("vermelho", "Vermelho", "#c0392b"),
        ("verde", "Verde", "#1e8449"), ("roxo", "Roxo", "#6c3483"), ("laranja", "Laranja", "#d35400"),
        ("rosa", "Rosa", "#d6337a"),
    ];

    /// <summary>As correntes que cada ESC tem desenho (as das lojas).</summary>
    internal static int[] AmperagensDoEsc(string tipo) => tipo == "esc-4em1" ? [35, 45, 60] : [12, 20, 30, 40, 60];

    static string NomeSvgDoEsc(PecaNoCircuito peca)
    {
        int[] opcoes = AmperagensDoEsc(peca.Tipo);
        double pedida = AmperesDoEsc(peca);
        int amperes = opcoes.OrderBy(a => Math.Abs(a - pedida)).First();
        string cor = CoresDeEsc.Any(c => c.id == peca.Acabamento) ? peca.Acabamento
                   : peca.Tipo == "esc-4em1" ? "preto" : "azul";
        return $"{peca.Tipo}-{amperes}a-{cor}";
    }

    /// <summary>A corrente e a cor da ESC selecionada; o desenho troca na hora.</summary>
    public void DefinirEsc(double? amperes, string cor)
    {
        var peca = Selecionada;
        if (peca is null || !EhEsc(peca.Tipo)) return;
        if (amperes is { } a) peca.Amperes = a;
        if (cor is not null) peca.Acabamento = cor;
        if (visuais.TryGetValue(peca.Id, out var v) && v is Image img)
            img.Source = Pecas.Obter(NomeSvg(peca, false));
        Mudou?.Invoke();
    }

    /// <summary>O modelo da peca selecionada (o diodo, o transistor): troca o desenho e a ficha.</summary>
    public void DefinirModeloSimples(string modelo)
    {
        var peca = Selecionada;
        if (peca is null) return;
        peca.Modelo = modelo;
        if (visuais.TryGetValue(peca.Id, out var v) && v is Image img)
            img.Source = Pecas.Obter(NomeSvg(peca, false));
        Mudou?.Invoke();
    }

    /// <summary>A ficha do motor selecionado (um da lista, ou "personalizado" com a ficha digitada).</summary>
    public void DefinirModeloDoMotor(string modelo, FichaPersonalizada ficha)
    {
        var peca = Selecionada;
        if (peca is null || !EhMotorDc(peca.Tipo)) return;
        peca.Modelo = modelo;
        peca.Ficha = modelo == "personalizado" ? ficha : null;
        // Outro motor, outro tamanho e outro rotulo: o desenho e refeito, e os
        // fios seguem os pinos para o lugar novo.
        Redesenhar();
        Selecionar(peca.Id, false);
        Mudou?.Invoke();
    }

    public void DefinirCorDoLed(string cor)
    {
        var peca = Selecionada;
        if (peca is null || peca.Tipo != "led") return;
        peca.Cor = cor;
        if (visuais.TryGetValue(peca.Id, out var v) && v is Image img)
            img.Source = Pecas.Obter(NomeSvg(peca, false));
        Mudou?.Invoke();
    }

    public void DefinirCorDaProtoboard(string cor)
    {
        var peca = Selecionada;
        if (peca is null || !Catalogo.EhProtoboard(peca.Tipo)) return;
        peca.Cor = cor;
        Redesenhar();
        Mudou?.Invoke();
    }

    /// <summary>
    /// O circuito mudou por fora do editor.
    ///
    /// E por aqui que a lista de componentes avisa: ela mexe no fio
    /// direto -- trocar o pino, a cor, o tracado --, e sem o aviso nada
    /// mais fica sabendo, entao o desfazer nao guarda e a rede continua
    /// com a ligacao antiga.
    /// </summary>
    public void AvisarQueMudou() => Mudou?.Invoke();

    /// <summary>
    /// Seleciona este fio, como um clique nele.
    ///
    /// E por aqui que a lista de componentes acha o fio no desenho.
    /// </summary>
    public void SelecionarFio(Fio fio)
    {
        int i = Atual.Fios.IndexOf(fio);
        if (i < 0) return;

        SelecionarFiosPorIndice([i]);
    }

    /// <summary>Seleciona fios pelo indice: usado pelo modo de captura.</summary>
    internal void SelecionarFiosPorIndice(IEnumerable<int> indices)
    {
        fiosSelecionados.Clear();
        selecionadas.Clear();
        foreach (int i in indices)
            if (i >= 0 && i < Atual.Fios.Count) fiosSelecionados.Add(i);
        RedesenharFios();
        AtualizarRealce();
        SelecaoMudou?.Invoke();
    }

    /// <summary>Seleciona pelo indice: usado pelo modo de captura.</summary>
    /// <summary>
    /// Seleciona a peca pelo Id, como um clique nela.
    ///
    /// E por aqui que a lista de componentes acha a peca no desenho.
    /// </summary>
    public void SelecionarPorId(string id)
    {
        if (Atual.Achar(id) is null) return;
        Selecionar(id, somar: false);
    }

    public void SelecionarPorIndice(int i, bool somar = false)
    {
        if (i < 0 || i >= Atual.Pecas.Count) return;
        Selecionar(Atual.Pecas[i].Id, somar);
    }

    public PecaNoCircuito Selecionada
        => PecaSelecionada is null ? null : Atual.Achar(PecaSelecionada);

    public void DefinirOhms(double ohms)
    {
        var peca = Selecionada;
        if (peca is null || peca.Tipo != "resistor") return;
        peca.Ohms = ohms;
        Redesenhar();
        Mudou?.Invoke();
    }

    /// <summary>
    /// Tira fios que nao mudam nada: repetidos, ligando um pino nele
    /// mesmo, ou unindo dois pontos que ja estao no mesmo no por outro
    /// caminho (mesma coluna da protoboard, por exemplo).
    /// </summary>
    public int OtimizarFios()
    {
        int antes = Atual.Fios.Count;
        var mantidos = new List<Fio>();
        var vistos = new HashSet<string>();

        foreach (var fio in Atual.Fios)
        {
            string a = Netlist.No(fio.DePeca, fio.DePino);
            string b = Netlist.No(fio.ParaPeca, fio.ParaPino);
            if (a == b) continue;

            string chave = string.CompareOrdinal(a, b) < 0 ? a + "|" + b : b + "|" + a;
            if (!vistos.Add(chave)) continue;

            // Redundante em relacao ao que JA foi mantido. Comparar
            // contra a lista inteira era errado: o fio util era julgado
            // dispensavel pela propria copia dele, e os dois sumiam.
            var parcial = new Circuito { Pecas = Atual.Pecas, Fios = mantidos };
            if (new Netlist(parcial).MesmoNo(a, b)) continue;

            mantidos.Add(fio);
        }

        Atual.Fios = mantidos;
        fiosSelecionados.Clear();
        RedesenharFios();
        SelecaoMudou?.Invoke();
        Mudou?.Invoke();
        return antes - mantidos.Count;
    }

    /// <summary>Cor do fio selecionado.</summary>
    public void DefinirCorDoFio(string hex)
    {
        if (fiosSelecionados.Count == 0) return;
        foreach (int i in fiosSelecionados)
            if (i >= 0 && i < Atual.Fios.Count) Atual.Fios[i].Cor = hex;
        RedesenharFios();
        Mudou?.Invoke();
    }

    public Fio FioAtual
        => FioSelecionado >= 0 && FioSelecionado < Atual.Fios.Count
            ? Atual.Fios[FioSelecionado] : null;

    /// <summary>Tracado dos fios selecionados; vale para todos de uma vez.</summary>
    public void DefinirEstiloDosFios(string estilo)
    {
        if (fiosSelecionados.Count == 0) return;
        foreach (int i in fiosSelecionados)
            if (i >= 0 && i < Atual.Fios.Count) Atual.Fios[i].Estilo = estilo;
        RedesenharFios();
        SelecaoMudou?.Invoke();
        Mudou?.Invoke();
    }

    /// <summary>Muda o tracado de TODOS os fios e o padrao dos proximos.</summary>
    public void DefinirEstiloDeTodos(string estilo)
    {
        EstiloPadrao = estilo;
        foreach (var f in Atual.Fios) f.Estilo = estilo;
        RedesenharFios();
        SelecaoMudou?.Invoke();
        Mudou?.Invoke();
    }

    public void RemoverSelecionada()
    {
        if (fiosSelecionados.Count > 0)
        {
            // De tras para frente: remover pelo indice mexe nos seguintes.
            foreach (int i in fiosSelecionados.OrderByDescending(x => x))
                if (i >= 0 && i < Atual.Fios.Count) Atual.Fios.RemoveAt(i);
            fiosSelecionados.Clear();
            RedesenharFios();
            SelecaoMudou?.Invoke();
            Mudou?.Invoke();
            if (selecionadas.Count == 0) return;
        }

        if (selecionadas.Count == 0) return;
        Atual.Fios.RemoveAll(f => selecionadas.Contains(f.DePeca)
                                  || selecionadas.Contains(f.ParaPeca));
        Atual.Pecas.RemoveAll(p => selecionadas.Contains(p.Id));
        selecionadas.Clear();

        Redesenhar();
        SelecaoMudou?.Invoke();
        Mudou?.Invoke();
    }

    readonly List<PecaNoCircuito> copiadas = [];

    /// <summary>
    /// Fios entre as pecas copiadas, guardados pelo INDICE em "copiadas"
    /// em vez do Id. Assim colar so precisa trocar indice por peca nova,
    /// sem depender de mapa de Id antigo para novo.
    /// </summary>
    readonly List<(int de, string dePino, int para, string paraPino,
                   string cor, string estilo)> fiosCopiados = [];

    public void Copiar()
    {
        copiadas.Clear();
        fiosCopiados.Clear();

        var indicePorId = new Dictionary<string, int>();
        idsOriginaisCopiados.Clear();
        foreach (string id in selecionadas)
        {
            var p = Atual.Achar(id);
            if (p is null) continue;
            indicePorId[id] = copiadas.Count;
            copiadas.Add(p.Copia());
            idsOriginaisCopiados.Add(id);
        }

        // So os fios com as DUAS pontas na selecao: um fio que sai para
        // fora viraria uma segunda ligacao no mesmo pino de la, que quase
        // nunca e o que se quer ao duplicar um trecho.
        foreach (var f in Atual.Fios)
        {
            if (!indicePorId.TryGetValue(f.DePeca ?? "", out int de)) continue;
            if (!indicePorId.TryGetValue(f.ParaPeca ?? "", out int para)) continue;
            fiosCopiados.Add((de, f.DePino, para, f.ParaPino, f.Cor, f.Estilo));
        }
    }

    /// <summary>
    /// Id de cada peca copiada ANTES da copia, na mesma ordem de
    /// "copiadas". Serve para a peca colada apontar para a placa colada
    /// junto com ela, e nao para a original.
    /// </summary>
    readonly List<string> idsOriginaisCopiados = [];

    public void Colar() => Colar(PonteiroNoMundo());

    /// <summary>
    /// Cola o que foi copiado com o CENTRO do grupo em "alvo". Com alvo
    /// nulo, desloca do lugar original -- o suficiente para nao empilhar.
    /// </summary>
    public void Colar(Point? destino)
    {
        if (copiadas.Count == 0) return;

        // Cola onde o ponteiro esta: e para onde a pessoa esta olhando.
        double dx, dy;
        if (destino is { } alvo)
        {
            // O centro do grupo vai para o ponteiro, e o conjunto inteiro
            // anda junto -- as pecas guardam as posicoes relativas.
            double minX = copiadas.Min(p => p.X), maxX = copiadas.Max(p => p.X);
            double minY = copiadas.Min(p => p.Y), maxY = copiadas.Max(p => p.Y);
            dx = alvo.X - (minX + maxX) / 2;
            dy = alvo.Y - (minY + maxY) / 2;

            // Sem encaixe na grade a peca colada nunca casaria com os
            // furos: o ponteiro cai em qualquer lugar.
            dx = Math.Round(dx / PassoGrade) * PassoGrade;
            dy = Math.Round(dy / PassoGrade) * PassoGrade;
        }
        else
        {
            dx = dy = Catalogo.Passo * 2;
        }

        selecionadas.Clear();
        var novas = new List<PecaNoCircuito>();

        foreach (var modelo in copiadas)
        {
            var nova = modelo.Copia();
            nova.Id = Atual.NovoId(nova.Tipo);
            nova.X += dx;
            nova.Y += dy;
            Atual.Pecas.Add(nova);
            CriarVisual(nova);
            selecionadas.Add(nova.Id);
            novas.Add(nova);
        }

        // "Qual placa manda" aponta para um id. Se a placa foi colada
        // junto, a peca colada passa a obedecer a placa COLADA -- senao a
        // copia de um circuito de duas placas mandaria na placa original.
        // Placa que ficou de fora mantem o id: se a peca nao a alcanca, a
        // escolha e ignorada sozinha.
        var novoIdDe = new Dictionary<string, string>();
        for (int i = 0; i < novas.Count && i < idsOriginaisCopiados.Count; i++)
            novoIdDe[idsOriginaisCopiados[i]] = novas[i].Id;

        foreach (var nova in novas)
            if (nova.PlacaQueManda is { } antiga && novoIdDe.TryGetValue(antiga, out var nov))
                nova.PlacaQueManda = nov;

        // As ligacoes vem junto: duplicar um trecho sem os fios obrigava
        // a religar tudo a mao.
        foreach (var (de, dePino, para, paraPino, cor, estilo) in fiosCopiados)
        {
            if (de >= novas.Count || para >= novas.Count) continue;
            Atual.Fios.Add(new Fio
            {
                DePeca = novas[de].Id, DePino = dePino,
                ParaPeca = novas[para].Id, ParaPino = paraPino,
                Cor = cor, Estilo = estilo ?? Fio.EstiloSolto
            });
        }

        // Colar de novo desloca mais, em vez de empilhar no mesmo lugar.
        // Os fios copiados usam indice, entao continuam valendo.
        copiadas.Clear();
        copiadas.AddRange(novas.Select(n => n.Copia()));

        fiosSelecionados.Clear();
        RedesenharFios();
        AtualizarRealce();
        SelecaoMudou?.Invoke();
        Mudou?.Invoke();
    }

    /// <summary>Um contorno por peca selecionada.</summary>
    void AtualizarRealce()
    {
        // Os aneis de BOOT/EN sao desenhados a parte e precisam seguir a
        // placa em qualquer mudanca: mover, girar, espelhar.
        DesenharBotoesDaPlaca();

        foreach (var r in realces) mundo.Children.Remove(r);
        realces.Clear();

        foreach (string id in selecionadas)
        {
            var peca = Atual.Achar(id);
            var def = peca is null ? null : Catalogo.Obter(peca.Tipo);
            if (def is null) continue;

            var r = new Rectangle
            {
                Width = Geometria.Tamanho(peca, def).Largura + 8,
                Height = Geometria.Tamanho(peca, def).Altura + 8,
                Stroke = new SolidColorBrush(Color.FromRgb(0x00, 0xC8, 0xD0)),
                StrokeThickness = 1.5,
                StrokeDashArray = [4, 3],
                Fill = new SolidColorBrush(Color.FromArgb(0x18, 0x00, 0xC8, 0xD0)),
                IsHitTestVisible = false,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = peca.Rotacao != 0 ? new RotateTransform(peca.Rotacao) : null
            };
            Canvas.SetLeft(r, peca.X - 4);
            Canvas.SetTop(r, peca.Y - 4);
            Panel.SetZIndex(r, 800);
            mundo.Children.Add(r);
            realces.Add(r);
        }
    }

    void Selecionar(string id, bool somar)
    {
        if (!somar) selecionadas.Clear();
        if (id is not null)
        {
            if (somar && selecionadas.Contains(id)) selecionadas.Remove(id);
            else selecionadas.Add(id);
        }
        fiosSelecionados.Clear();
        AtualizarRealce();
        SelecaoMudou?.Invoke();
    }

    public void LimparSelecao()
    {
        selecionadas.Clear();
        fiosSelecionados.Clear();
        AtualizarRealce();
        SelecaoMudou?.Invoke();
    }

    /// <summary>
    /// Pinta de amarelo todo pino JA LIGADO -- por fio ou por estar
    /// espetado num furo.
    ///
    /// Vale para qualquer peca, e nao so para as placas: sem a marca no
    /// modulo e no sensor nao da para saber se a perna pegou no furo, e
    /// essa era a duvida mais comum ao montar. A protoboard fica de
    /// fora porque os furos dela ja tem a marca propria -- acender o
    /// trilho inteiro viraria um borrao.
    /// </summary>
    public void MarcarPinosLigados(Netlist rede)
    {
        DesenharSlotsCheios(rede);

        var grupo = new GeometryGroup();
        if (rede is not null)
        {
            foreach (var peca in Atual.Pecas)
            {
                if (Catalogo.EhProtoboard(peca.Tipo)) continue;
                var def = Catalogo.Obter(peca.Tipo);
                if (def is null) continue;

                double raio = Catalogo.EhPlacaDeDesenvolvimento(peca.Tipo) ? 3.4 : 2.8;

                foreach (var pino in def.Pinos)
                {
                    if (!rede.EstaLigado(peca.Id, pino.Nome)) continue;
                    var q = Geometria.PosicaoPino(peca, def, pino);
                    grupo.Children.Add(new EllipseGeometry(q, raio, raio));
                }
            }
        }
        pinosLigados.Data = grupo;
    }

    /// <summary>
    /// Marca os furos da protoboard que tem perna dentro. So os
    /// ocupados: acender o trilho inteiro viraria um borrao.
    /// </summary>
    void DesenharSlotsCheios(Netlist rede)
    {
        var grupo = new GeometryGroup();
        if (rede is not null)
        {
            foreach (string no in rede.FurosOcupados)
            {
                int corte = no.IndexOf(':');
                if (corte < 0) continue;

                var placa = Atual.Achar(no[..corte]);
                var def = placa is null ? null : Catalogo.Obter(placa.Tipo);
                var furo = def?.Pinos.FirstOrDefault(f => f.Nome == no[(corte + 1)..]);
                if (furo is null) continue;

                grupo.Children.Add(new EllipseGeometry(
                    Geometria.PosicaoPino(placa, def, furo), 2.6, 2.6));
            }
        }
        slotsCheios.Data = grupo;
    }

    /// <summary>
    /// Leva o estado das PECAS para a Maquina: botao apertado, posicao
    /// do potenciometro, BOOT da placa.
    ///
    /// Ficava espalhado na janela, fora do alcance dos testes -- e era
    /// justamente onde o botao parava de funcionar sem ninguem perceber.
    /// </summary>
    /// <summary>
    /// Ponte para quem so tem uma maquina: teste de uma placa e os modos
    /// de linha de comando. Vale exatamente o que valia antes da
    /// bancada existir.
    /// </summary>
    public void AplicarEntradas(Maquina maq, Netlist rede)
        => AplicarEntradas(Bancada.DeUmaSo(maq), rede);

    public void RefletirEstado(Maquina maq, Netlist rede)
        => RefletirEstado(Bancada.DeUmaSo(maq), rede);

    /// <summary>
    /// A simulacao esta rodando. So entao os vinculos de tecla valem --
    /// parado, as teclas sao os atalhos do programa.
    /// </summary>
    public bool EmSimulacao { get; set; }

    /// <summary>Quem esta apertado por causa de um vinculo, e nao do mouse.</summary>
    readonly HashSet<string> apertadosPorVinculo = [];

    /// <summary>Forca de cada vinculo no quadro anterior, para achar a borda.</summary>
    readonly Dictionary<string, double> vinculoAntes = [];

    /// <summary>
    /// Teclado e controle de video game mexendo no circuito.
    ///
    /// E a diferenca entre "clique no botao com o mouse" e jogar com o
    /// circuito: com o manche do Xbox no joystick e o J no botao, da
    /// para testar um jogo de dois botoes sozinho, com as duas maos no
    /// lugar certo.
    ///
    /// Vale so com a simulacao rodando, e e por isso que a janela
    /// desliga os atalhos de uma tecla so nessa hora: senao o "A" do
    /// jogo abriria a paleta de pecas no meio da partida.
    /// </summary>
    void AplicarVinculos()
    {
        if (!EmSimulacao) return;

        var controle = Controle.Ler();
        bool mudouDesenho = false;

        foreach (var peca in Atual.Pecas)
        {
            if (peca.Tipo == "radio-controle")
            {
                AplicarVinculosDoRadio(peca, controle, segundosDoQuadro);
                continue;
            }
            if (peca.Teclas is not { Count: > 0 } mapa) continue;

            double Forca(string acao)
                => mapa.TryGetValue(acao, out var v) ? Vinculos.Forca(v, controle) : 0;

            bool Ligou(string acao)
            {
                if (!mapa.TryGetValue(acao, out var v) || string.IsNullOrEmpty(v)) return false;
                string chave = peca.Id + "|" + acao;
                double agora = Vinculos.Forca(v, controle);
                double antes = vinculoAntes.GetValueOrDefault(chave);
                vinculoAntes[chave] = agora;
                return agora > 0.5 && antes <= 0.5;
            }

            bool Tem(params string[] acoes)
                => acoes.Any(a => mapa.TryGetValue(a, out var v) && !string.IsNullOrEmpty(v));

            switch (peca.Tipo)
            {
                case "pushbutton":
                case "pushbutton-6mm":
                case "micro-switch":
                case "analog-joystick" when Tem(Vinculos.Apertar):
                case "ky-040" when Tem(Vinculos.Apertar):
                {
                    bool apertado = Forca(Vinculos.Apertar) > 0.5;
                    if (apertado)
                    {
                        if (apertadosPorVinculo.Add(peca.Id)) BotoesPressionados.Add(peca.Id);
                    }
                    else if (apertadosPorVinculo.Remove(peca.Id))
                    {
                        BotoesPressionados.Remove(peca.Id);
                    }
                    break;
                }

                case "slide-switch":
                case "tilt-switch":
                case "pir-motion-sensor":
                case "pendrive-usb":
                    if (Ligou(Vinculos.Alternar))
                    {
                        peca.Giro = peca.Giro > 0.5 ? 0 : 1;
                        mudouDesenho = true;
                        GiroMudou?.Invoke(peca);
                    }
                    break;
            }

            // O manche anda enquanto a tecla esta em baixo e volta ao
            // meio quando solta -- como o de verdade.
            if (peca.Tipo == "analog-joystick"
                && Tem(Vinculos.Cima, Vinculos.Baixo, Vinculos.Esquerda, Vinculos.Direita))
            {
                double x = 0.5 + 0.5 * (Forca(Vinculos.Direita) - Forca(Vinculos.Esquerda));
                double y = 0.5 + 0.5 * (Forca(Vinculos.Cima) - Forca(Vinculos.Baixo));

                if (Math.Abs(x - peca.Giro) > 1e-6 || Math.Abs(y - peca.Giro2) > 1e-6)
                {
                    peca.Giro = Math.Clamp(x, 0, 1);
                    peca.Giro2 = Math.Clamp(y, 0, 1);
                    mudouDesenho = true;
                    GiroMudou?.Invoke(peca);
                }
            }

            // A roda do encoder anda de clique em clique: um toque na tecla,
            // um passo (a lista desce uma linha); segurada, repete como o
            // teclado repete uma letra.
            if (peca.Tipo == "ky-040" && Tem(Vinculos.Mais, Vinculos.Menos))
            {
                int sentido = (Forca(Vinculos.Mais) > 0.5 ? 1 : 0) - (Forca(Vinculos.Menos) > 0.5 ? 1 : 0);
                var agora = DateTime.UtcNow;
                bool passo = false;
                if (sentido == 0) repeticaoDaRoda.Remove(peca.Id);
                else if (!repeticaoDaRoda.TryGetValue(peca.Id, out var antes) || antes.Sentido != sentido)
                {
                    repeticaoDaRoda[peca.Id] = (sentido, agora.AddMilliseconds(400));
                    passo = true;
                }
                else if (agora >= antes.Proximo)
                {
                    repeticaoDaRoda[peca.Id] = (sentido, agora.AddMilliseconds(90));
                    passo = true;
                }
                if (passo)
                {
                    double girado = peca.Giro + sentido * 4 / 40.0;   // um clique: o ciclo inteiro do CLK, como a roda do mouse
                    peca.Giro = girado - Math.Floor(girado);
                    mudouDesenho = true;
                    GiroMudou?.Invoke(peca);
                }
            }

            // Botao giratorio: a tecla ANDA com ele, em vez de pular
            // para o extremo. Uma volta inteira leva cerca de um segundo
            // e meio segurando a tecla.
            else if (Tem(Vinculos.Mais, Vinculos.Menos))
            {
                double passo = (Forca(Vinculos.Mais) - Forca(Vinculos.Menos)) * 0.022;
                if (passo != 0)
                {
                    peca.Giro = Math.Clamp(peca.Giro + passo, 0, 1);
                    mudouDesenho = true;
                    GiroMudou?.Invoke(peca);
                }
            }

            if (mudouDesenho && Catalogo.Obter(peca.Tipo) is { } def)
                AnimarParteMovel(peca, def);
        }

        DesenharBotoesDaPlaca();
    }

    readonly Dictionary<string, int> memoriaLogica = [];

    /// <summary>A tecla que gira cada encoder: o sentido e quando ela repete.</summary>
    readonly Dictionary<string, (int Sentido, DateTime Proximo)> repeticaoDaRoda = [];

    /// <summary>Onde cada encoder esta de verdade (anda um passo por vez ate o giro da peca).</summary>
    readonly Dictionary<string, int> passosDoEncoder = [];

    public Logica LogicaAtual { get; private set; }

    void AplicarLogica(Bancada bancada, Netlist rede)
    {
        if (!Logica.TemAlgumaNoCircuito(Atual)) { LogicaAtual = null; return; }

        long tempo = bancada.Todas.Select(x => x.Value.Ms).DefaultIfEmpty(0).Max();
        LogicaAtual = Logica.Avaliar(Atual, rede, bancada, memoriaLogica, tempo);

        foreach (var peca in Atual.Pecas)
        {
            var perfil = PerfilPlaca.De(peca.Tipo);
            if (perfil is null) continue;

            var def = Catalogo.Obter(peca.Tipo);
            var maq = bancada.De(peca.Id);
            if (def is null || maq is null) continue;

            foreach (var pino in def.Pinos)
            {
                int gpio = perfil.Numero(pino.Nome);
                if (gpio < 0 || gpio >= Maquina.NumPinos) continue;
                if (maq.Modo[gpio] == 1) continue;

                int nivel = LogicaAtual.NivelDoPino(peca.Id, pino.Nome);
                if (nivel != Logica.Indefinido) maq.NivelExterno[gpio] = nivel;
            }
        }
    }

    public void AplicarEntradas(Bancada bancada, Netlist rede)
    {
        ultimaRede = rede;
        AplicarVinculos();

        if (rede is null || bancada is null) return;
        analogicosDeControle.Clear();

        foreach (var peca in Atual.Pecas)
        {
            // Uma peca, uma placa: o resto deste metodo continua escrito
            // como se houvesse uma maquina so.
            var maq = MaquinaDaPeca(bancada, rede, peca);

            switch (peca.Tipo)
            {
                case "pushbutton":
                case "pushbutton-6mm":
                {
                    // Uma perna costuma ir ao GND e a outra ao GPIO; nao da
                    // para saber qual de antemao, entao tenta as duas.
                    int gpio = PrimeiroGpio(rede, peca.Id, "1.l", "1.r", "2.l", "2.r");
                    if (gpio < 0) break;
                    bool apertado = BotoesPressionados.Contains(peca.Id);
                    maq.Pressionado[gpio] = apertado;

                    // O outro lado do botao preso no GND ou na alimentacao: apertado,
                    // o pino vai a esse nivel -- inclusive em INPUT com o pull-up de
                    // fora (o botao no GND le LOW, e nao HIGH).
                    bool ladoUm = rede.GpioDe(Atual, peca.Id, "1.l") == gpio || rede.GpioDe(Atual, peca.Id, "1.r") == gpio;
                    int outro = ladoUm ? Math.Max(NivelFixo(peca, rede, "2.l"), NivelFixo(peca, rede, "2.r"))
                                       : Math.Max(NivelFixo(peca, rede, "1.l"), NivelFixo(peca, rede, "1.r"));
                    if (relesSegurando.TryGetValue(peca.Id, out var antes))
                        foreach (int g in antes) maq.NivelExterno[g] = -1;
                    if (apertado && outro >= 0)
                    {
                        maq.NivelExterno[gpio] = outro;
                        relesSegurando[peca.Id] = [gpio];
                    }
                    else relesSegurando.Remove(peca.Id);
                    break;
                }

                case "potentiometer":
                {
                    int gpio = rede.GpioDe(Atual, peca.Id, "SIG");
                    if (gpio >= 0)
                    {
                        maq.Analogico[gpio] = (int)Math.Round(peca.Giro * maq.Perfil.MaxAdc);
                        analogicosDeControle.Add((maq, gpio));
                    }
                    break;
                }

                // A chave deslizante e um SPDT: o comum (2) vai ao 1 com a
                // alavanca a esquerda e ao 3 a direita, e leva o nivel de um
                // lado ao outro, como o contato do rele. Antes ela "apertava"
                // o pino, sem olhar onde cada lado estava ligado.
                case "slide-switch":
                {
                    if (relesSegurando.TryGetValue(peca.Id, out var antigos))
                        foreach (int g in antigos) maq.NivelExterno[g] = -1;
                    var segura = new HashSet<int>();
                    LevarNivel(peca, maq, rede, "2", peca.Giro > 0.5 ? "3" : "1", segura);
                    relesSegurando[peca.Id] = segura;
                    break;
                }

                case "ks2e-m-dc5":
                    AplicarRele(peca, maq, rede);
                    break;

                case "modulo-rele-1":
                case "modulo-rele-2":
                case "modulo-rele-4":
                    ultimaMaquinaDoRele[peca.Id] = maq;
                    AplicarModuloDeRele(peca, maq, rede);
                    break;

                // O fim de curso: o comum vai ao NO apertado e ao NC solto,
                // e leva o nivel de um lado ao outro, como o contato do rele.
                case "micro-switch":
                {
                    if (relesSegurando.TryGetValue(peca.Id, out var antes))
                        foreach (int g in antes) maq.NivelExterno[g] = -1;
                    var segura = new HashSet<int>();
                    LevarNivel(peca, maq, rede, "COM", BotoesPressionados.Contains(peca.Id) ? "NO" : "NC", segura);
                    relesSegurando[peca.Id] = segura;
                    break;
                }

                case "biaxial-stepper":
                    AplicarBiaxial(peca, maq, rede);
                    break;

                // Ligado num A4988/DRV8825, quem manda e o driver (depois do laco).
                case "stepper-motor":
                    if (DriverDoMotorDePasso(peca, rede).Driver is null) AplicarPasso(peca, maq, rede);
                    break;

                // Os motores DC nao leem pino nenhum: a tensao nos fios sai
                // da conta de energia, em AplicarMotores, depois do laco.
                case "motor-dc":
                case "motor-tt":
                case "motor-brushless":
                    break;

                case "rotary-dialer":
                    AplicarDisco(peca, maq, rede);
                    break;

                case "esp32-devkit-v1" when ledsDePlaca.TryGetValue(peca.Id, out var luzes):
                {
                    // Vermelho: so estar energizada. Azul: o GPIO 2.
                    for (int k = 0; k < luzes.Count && k < LedsDaPlaca.Length; k++)
                    {
                        int gpio = LedsDaPlaca[k].Gpio;
                        double forca = gpio < 0
                            ? (maq.Energizada ? 0.9 : 0)
                            : maq.Pwm[gpio] / 255.0;
                        double alvo = 0.94 * Luminancia(forca);
                        if (Math.Abs(luzes[k].Opacity - alvo) > 0.005) luzes[k].Opacity = alvo;
                    }
                    break;
                }

                case "esp32-devkit-v1":
                    // BOOT segurado leva o GPIO 0 a LOW.
                    maq.Pressionado[0] = BootPressionado.Contains(peca.Id);
                    break;

                case "analog-joystick":
                {
                    // Dois eixos independentes; o botao usa o mesmo
                    // mecanismo dos pushbuttons.
                    int gx = rede.GpioDe(Atual, peca.Id, "HORZ");
                    int gy = rede.GpioDe(Atual, peca.Id, "VERT");
                    if (gx >= 0) maq.Analogico[gx] = (int)Math.Round(peca.Giro * maq.Perfil.MaxAdc);
                    if (gy >= 0) maq.Analogico[gy] = (int)Math.Round(peca.Giro2 * maq.Perfil.MaxAdc);
                    analogicosDeControle.Add((maq, gx));
                    analogicosDeControle.Add((maq, gy));

                    int gs = rede.GpioDe(Atual, peca.Id, "SEL");
                    if (gs >= 0) maq.Pressionado[gs] = BotoesPressionados.Contains(peca.Id);
                    break;
                }

                case "dip-switch-8":
                {
                    // Cada chave impoe nivel no seu par de pinos: ligada
                    // leva a LOW, como uma chave para o GND.
                    for (int k = 0; k < 8; k++)
                    {
                        bool ligada = (peca.Bits & (1 << k)) != 0;
                        foreach (string lado in new[] { $"{k + 1}a", $"{k + 1}b" })
                        {
                            int g = rede.GpioDe(Atual, peca.Id, lado);
                            if (g >= 0) maq.NivelExterno[g] = ligada ? 0 : 1;
                        }
                    }
                    break;
                }

                case "ky-040":
                {
                    // Encoder em quadratura: a posicao vira o par CLK/DT.
                    //
                    // A sequencia e 11 -> 01 -> 00 -> 10, o codigo Gray de
                    // dois bits: a cada passo muda UM sinal so, e o descanso
                    // (no clique) e com os dois em HIGH, como no modulo de
                    // verdade, que tem os resistores de pull-up. A conta
                    // antiga ia 11 -> 00 -> 10 -> 01, mudando os dois de uma
                    // vez em metade dos passos -- e e na borda de um sinal que
                    // o sketch le o outro para saber o sentido.
                    //
                    // Ele nao tem batente: gira para sempre. E anda UM passo
                    // por vez ate onde a peca foi girada -- um giro rapido nao
                    // pula estados, porque na peca de verdade o giro e continuo.
                    int alvo = (int)Math.Round(peca.Giro * 40) % 40;
                    int passo = passosDoEncoder.GetValueOrDefault(peca.Id, alvo);
                    int falta = ((alvo - passo) % 40 + 60) % 40 - 20;
                    if (falta != 0) passo += Math.Sign(falta);
                    passosDoEncoder[peca.Id] = passo;
                    int clk = rede.GpioDe(Atual, peca.Id, "CLK");
                    int dt = rede.GpioDe(Atual, peca.Id, "DT");
                    if (clk >= 0) maq.NivelExterno[clk] = ((passo + 1) & 2) != 0 ? 0 : 1;
                    if (dt >= 0) maq.NivelExterno[dt] = (passo & 2) != 0 ? 0 : 1;

                    int sw = rede.GpioDe(Atual, peca.Id, "SW");
                    if (sw >= 0) maq.Pressionado[sw] = BotoesPressionados.Contains(peca.Id);
                    break;
                }

                default:
                    AplicarPelaTabela(peca, maq, rede);
                    break;
            }
        }

        // O infravermelho nao tem fio: o codigo do controle e uma
        // grandeza solta, e vai para todas as maquinas.
        foreach (var (_, m) in bancada.Todas) ConferirParInfravermelho(m);

        // As bordas dos pinos vigiados (attachInterrupt, Pin.irq), na hora em que
        // o botao mudou -- e nao so quando o sketch acordar do delay.
        foreach (var (_, m) in bancada.Todas) m.NotarEntradasVigiadas();

        AplicarLogica(bancada, rede);
        AplicarCartoes(bancada, rede);
        AplicarRadios(bancada, rede);
        AplicarChips(bancada, rede);
        var contaDoQuadro = AplicarEnergiaDoQuadro(bancada, rede);
        AplicarMotoresDePasso(bancada, rede, segundosDoQuadro);
        AplicarServos(bancada, rede, segundosDoQuadro);
        AplicarEscs(bancada, rede, segundosDoQuadro);
        AplicarReceptores(bancada, rede);
        AtualizarRadios();
        foreach (var esc in Atual.Pecas.Where(p => EhEsc(p.Tipo)))
        {
            MostrarRotuloAcima(esc, TextoDaPecaEsc(esc));
            foreach (var canal in CanaisDeEsc(esc))
                if (MotorDoCanal(canal, rede).Motor is { } bl) MostrarGiro(bl, MotorSemEscovaDe(bl).Rpm);
        }
        AplicarDivisores(bancada, rede, contaDoQuadro);
        MostrarEnergiaDasPlacas();
        MostrarRotulosDosReguladores();
    }

    /// <summary>Os pinos analogicos que um controle (potenciometro, joystick, sensor) definiu nesta passada.</summary>
    readonly HashSet<(Maquina, int)> analogicosDeControle = [];
    DateTime proximoDivisor;

    /// <summary>
    /// O conversor le a tensao do no: o pino analogico no meio de um divisor
    /// de resistores -- a bateria medida por todo projeto portatil -- le a
    /// tensao dividida, e nao zero. A conta e a da visao de energia, refeita a
    /// cada quarto de segundo (a bateria nao muda mais rapido que isso), e
    /// vale para o pino que tem resistor e que nenhum controle definiu.
    /// </summary>
    void AplicarDivisores(Bancada bancada, Netlist rede, Energia contaPronta = null)
    {
        if (DateTime.UtcNow < proximoDivisor) return;
        proximoDivisor = DateTime.UtcNow.AddMilliseconds(250);
        if (!Atual.Pecas.Any(p => p.Tipo is "resistor" or "ldr")) return;

        // Com motor no circuito a conta deste quadro ja foi feita.
        var conta = contaPronta ?? CalcularEnergia(bancada, rede);
        foreach (var placa in Atual.Pecas)
        {
            var perfil = PerfilPlaca.De(placa.Tipo);
            var def = perfil is null ? null : Catalogo.Obter(placa.Tipo);
            var maq = def is null ? null : bancada.De(placa.Id);
            if (maq is null || !maq.Energizada) continue;

            double fundoDeEscala = perfil.TensaoDeOperacao > 0 ? perfil.TensaoDeOperacao : 3.3;
            foreach (var pino in def.Pinos)
            {
                int gpio = perfil.Numero(pino.Nome);
                if (gpio < 0 || gpio >= Maquina.NumPinos || maq.Modo[gpio] == 1) continue;
                if (analogicosDeControle.Contains((maq, gpio))) continue;
                if (conta.TensaoPorResistor(placa.Id, pino.Nome) is not { } volts) continue;
                maq.Analogico[gpio] = (int)Math.Round(Math.Clamp(volts / fundoDeEscala, 0, 1) * maq.Perfil.MaxAdc);
            }
        }
    }

    /// <summary>
    /// Sensores descritos em Comportamentos: o valor da peca vira leitura
    /// analogica e/ou nivel logico nos pinos indicados.
    /// </summary>
    void AplicarPelaTabela(PecaNoCircuito peca, Maquina maq, Netlist rede)
    {
        var comp = Comportamento.De(peca.Tipo);
        if (comp is null) return;

        foreach (string pino in comp.Analogicos)
        {
            int gpio = rede.GpioDe(Atual, peca.Id, pino);
            if (gpio >= 0)
            {
                maq.Analogico[gpio] = comp.Leitura(peca.Giro, maq.Perfil.MaxAdc);
                analogicosDeControle.Add((maq, gpio));
            }
        }

        // O teclado matricial nao tem controle deslizante: quem manda e
        // o dedo. Sem tecla apertada o getKey() do sketch le zero, que e
        // o que a biblioteca de verdade devolve.
        if (peca.Tipo == "membrane-keypad")
        {
            string comDedo = teclaMatricial.GetValueOrDefault(peca.Id);
            maq.Leituras["keypad:tecla"] = comDedo is { Length: > 0 } ? comDedo[0] : 0;

            // A tecla liga o fio da linha dela ao da coluna dela: a biblioteca
            // do sketch acha a tecla pelos GPIOs desses dois fios.
            int gpioDaLinha = -1, gpioDaColuna = -1;
            int posicao = comDedo is { Length: > 0 } ? "123A456B789C*0#D".IndexOf(comDedo[0]) : -1;
            if (posicao >= 0)
            {
                gpioDaLinha = rede.GpioDe(Atual, peca.Id, $"R{posicao / 4 + 1}");
                gpioDaColuna = rede.GpioDe(Atual, peca.Id, $"C{posicao % 4 + 1}");
            }
            maq.Leituras["keypad:linha"] = gpioDaLinha;
            maq.Leituras["keypad:coluna"] = gpioDaColuna;
            return;
        }

        // Grandeza lida por biblioteca (distancia, temperatura, peso...).
        if (comp.Grandeza is not null)
        {
            double valor = comp.ValorReal(peca.Giro);

            if (comp.PinoDaGrandeza is null)
            {
                // De barramento: chave fixa, sem pino. O controle
                // infravermelho na barra em zero nao apaga o codigo da tecla
                // que o sketch ainda nao leu: quem o consome e o resume().
                if (peca.Tipo != "ir-remote" || valor > 0) maq.Leituras[comp.Grandeza] = valor;
            }
            else
            {
                int gpio = rede.GpioDe(Atual, peca.Id, comp.PinoDaGrandeza);
                if (gpio >= 0)
                {
                    maq.Leituras[$"{gpio}:{comp.Grandeza}"] = valor;

                    // O HC-SR04 e lido por pulseIn: a distancia vira o
                    // tempo de ida e volta do som (58 us por cm).
                    if (comp.Grandeza == "distancia")
                        maq.Leituras[$"{gpio}:pulso"] = valor * 58;

                    // O DHT expoe as duas grandezas no mesmo pino.
                    if (comp.Grandeza == "temperatura")
                        maq.Leituras[$"{gpio}:umidade"] = 30 + 50 * peca.Giro2;
                }
            }
        }

        // Tecla apertada no desenho manda mais que o controle deslizante
        // da barra: e o gesto mais direto, e e o que a pessoa acabou de
        // fazer.
        //
        // O NEC de verdade: o codigo sai UMA vez no aperto; segurando, so o
        // "repeat" (0xFFFFFFFF) a cada ~108 ms. Antes o codigo era reescrito
        // a cada quadro, e o resume() do sketch o pegava de novo: uma tecla
        // virava varias, e o "alternar" do LED voltava ao que era.
        if (peca.Tipo == "ir-remote" && teclaDoControle.TryGetValue(peca.Id, out var tecla))
        {
            if (!irEntregue.TryGetValue(peca.Id, out var entregue) || entregue.Codigo != tecla.Codigo)
            {
                maq.Leituras["ir:codigo"] = tecla.Codigo;
                irEntregue[peca.Id] = (tecla.Codigo, maq.Ms);
            }
            // O repeat so entra com o codigo de antes ja lido: o receptor guarda
            // o que decodificou ate o resume() -- com o PC ocupado, o sketch
            // pode demorar a ler, e o repeat nao pode passar por cima da tecla.
            else if (maq.Ms - entregue.Ms >= 108 && maq.Leitura("ir:codigo") <= 0)
            {
                maq.Leituras["ir:codigo"] = 0xFFFFFFFF;
                irEntregue[peca.Id] = (tecla.Codigo, maq.Ms);
            }
        }
        else if (peca.Tipo == "ir-remote") irEntregue.Remove(peca.Id);

        foreach (string pino in comp.Digitais)
        {
            int gpio = rede.GpioDe(Atual, peca.Id, pino);
            if (gpio < 0) continue;

            bool detectou = peca.Giro > comp.Limiar;
            // Modulo com saida invertida: detectar leva o pino a LOW.
            maq.NivelExterno[gpio] = comp.DigitalInvertido
                ? (detectou ? 0 : 1)
                : (detectou ? 1 : 0);
        }
    }

    /// <summary>
    /// O rele: bobina energizada muda o contato de lugar.
    ///
    /// O netlist e estatico -- ele e refeito quando o circuito muda, nao
    /// a cada quadro --, entao nao da para "religar" P em NO de verdade.
    /// O que da, e o que importa para o exercicio, e levar o NIVEL: se o
    /// contato que acabou de fechar tem terra (ou alimentacao) de um
    /// lado e um GPIO do outro, o GPIO passa a ler aquele nivel.
    ///
    /// E o mesmo caminho do botao, que tambem nao conduz no netlist.
    ///
    /// Em repouso vale NC (normalmente fechado), com a bobina ligada
    /// vale NO -- que e o que esta impresso na peca.
    /// </summary>
    /// <summary>Pinos que cada rele esta segurando agora.</summary>
    readonly Dictionary<string, HashSet<int>> relesSegurando = [];

    void AplicarRele(PecaNoCircuito peca, Maquina maq, Netlist rede)
    {
        bool ligado = BobinaEnergizada(peca, maq, rede);

        // Solta o que ESTE rele estava segurando, e so isso.
        //
        // A primeira versao soltava o pino do contato que abriu -- mas o
        // GPIO costuma estar no comum (P), nao no contato, entao o nivel
        // antigo ficava grudado nele e o rele nunca "desligava". Soltar
        // tudo indiscriminadamente tambem nao serve: apagaria o nivel
        // que outra peca esteja impondo no mesmo pino.
        if (relesSegurando.TryGetValue(peca.Id, out var antigos))
            foreach (int g in antigos) maq.NivelExterno[g] = -1;

        var segurando = new HashSet<int>();

        // O rele tem dois contatos independentes no mesmo corpo.
        for (int via = 1; via <= 2; via++)
            LevarNivel(peca, maq, rede, $"P{via}",
                       ligado ? $"NO{via}" : $"NC{via}", segurando);

        relesSegurando[peca.Id] = segurando;
        MostrarRele(peca, ligado);
    }

    /// <summary>
    /// A bobina pegou? Com a conta de energia, pela tensao nela, nos dois
    /// sentidos. O pino da placa entra na conta com ~30 ohms, e isso derruba
    /// a tensao na bobina de 70 ohms (a ESP32 da ~2,3 V; o Uno, ~3,5 V): o
    /// limite de 2 V e o que separa "o pino esta puxando" de "nao esta". Sem a
    /// conta, pelo pino:
    /// a bobina entre o pino e o GND liga com o HIGH; entre o 5V e o pino --
    /// como nos modulos, ativos em LOW --, com o LOW.
    /// </summary>
    bool BobinaEnergizada(PecaNoCircuito peca, Maquina maq, Netlist rede)
    {
        if (EnergiaDoQuadro is { } conta)
            return Math.Abs(conta.TensaoDoPino(peca.Id, "COIL1") - conta.TensaoDoPino(peca.Id, "COIL2")) >= 2.0;
        if (maq is null) return false;
        foreach (var (pino, outro) in new[] { ("COIL1", "COIL2"), ("COIL2", "COIL1") })
        {
            int gpio = rede.GpioDe(Atual, peca.Id, pino);
            if (gpio < 0) continue;
            bool noTrilho = rede.PinosDePlacaNoMesmoNo(Atual, peca.Id, outro).Any(Netlist.EhPinoDeAlimentacao);
            return noTrilho ? maq.Modo[gpio] == 1 && maq.Pwm[gpio] <= 127 : maq.Pwm[gpio] > 127;
        }
        return false;
    }

    /// <summary>
    /// Uma luz no corpo do rele: sem ela nao ha como saber se a bobina
    /// pegou, porque o efeito acontece do outro lado do componente.
    /// </summary>
    readonly Dictionary<string, Ellipse> luzesDeRele = [];

    void MostrarRele(PecaNoCircuito peca, bool ligado)
    {
        if (!luzesDeRele.TryGetValue(peca.Id, out var luz))
        {
            var def = Catalogo.Obter(peca.Tipo);
            if (def is null) return;

            luz = new Ellipse
            {
                Width = 7, Height = 7,
                Stroke = new SolidColorBrush(Color.FromRgb(0x20, 0x20, 0x20)),
                StrokeThickness = 1,
                IsHitTestVisible = false
            };
            Panel.SetZIndex(luz, 320);
            mundo.Children.Add(luz);
            luzesDeRele[peca.Id] = luz;

            var centro = Geometria.PosicaoLocal(peca, def, def.Largura / 2, def.Altura * 0.22);
            Canvas.SetLeft(luz, centro.X - 3.5);
            Canvas.SetTop(luz, centro.Y - 3.5);
        }

        var cor = ligado
            ? Color.FromRgb(0x3D, 0xDC, 0x5B)
            : Color.FromRgb(0x2A, 0x3A, 0x2E);
        if (luz.Fill is not SolidColorBrush b || b.Color != cor)
            luz.Fill = new SolidColorBrush(cor);
    }

    /// <summary>
    /// Liga dois pinos do rele: o que estiver preso num nivel de um lado
    /// aparece no GPIO do outro.
    /// </summary>
    void LevarNivel(PecaNoCircuito peca, Maquina maq, Netlist rede,
                    string ladoA, string ladoB, HashSet<int> segurando)
    {
        int gpioA = rede.GpioDe(Atual, peca.Id, ladoA);
        int gpioB = rede.GpioDe(Atual, peca.Id, ladoB);

        int nivelA = NivelFixo(peca, rede, ladoA);
        int nivelB = NivelFixo(peca, rede, ladoB);

        if (gpioA >= 0 && nivelB >= 0)
        { maq.NivelExterno[gpioA] = nivelB; segurando.Add(gpioA); }

        if (gpioB >= 0 && nivelA >= 0)
        { maq.NivelExterno[gpioB] = nivelA; segurando.Add(gpioB); }
    }

    // Qual rede esta presa em que nivel, calculado uma vez por netlist.
    //
    // A primeira versao varria TODAS as pecas e TODOS os pinos a cada
    // pergunta -- e sao quatro perguntas por rele, a cada quadro. Num
    // circuito com protoboard de 830 isso e mais de mil nos varridos
    // vinte e cinco vezes por segundo, para uma resposta que nao muda
    // enquanto o circuito nao muda.
    Netlist redeDosNiveis;
    Dictionary<string, int> niveisFixos;

    /// <summary>
    /// 0 se este pino chega no GND, 1 se chega na alimentacao, -1 se
    /// esta solto. E o que da para saber sem resolver o circuito.
    /// </summary>
    int NivelFixo(PecaNoCircuito peca, Netlist rede, string pino)
    {
        if (!ReferenceEquals(redeDosNiveis, rede))
        {
            redeDosNiveis = rede;
            niveisFixos = [];

            foreach (var outra in Atual.Pecas)
            {
                var def = Catalogo.Obter(outra.Tipo);
                if (def is null) continue;

                foreach (var p in def.Pinos)
                {
                    int nivel =
                        p.Nome == "GND" || p.Nome.StartsWith("GND.", StringComparison.Ordinal)
                            ? 0
                        : p.Nome is "3V3" or "3.3V" or "5V" or "VIN" or "VCC" ? 1
                        : -1;
                    if (nivel < 0) continue;

                    string r = rede.RaizDe(Netlist.No(outra.Id, p.Nome));
                    // GND ganha da alimentacao: rede com os dois e curto,
                    // e quem avisa disso e a Queima, nao o rele.
                    if (r is not null
                        && (!niveisFixos.TryGetValue(r, out int antes) || antes == 1))
                        niveisFixos[r] = nivel;
                }
            }
        }

        string raiz = rede.RaizDe(Netlist.No(peca.Id, pino));
        return raiz is not null && niveisFixos.TryGetValue(raiz, out int achado)
            ? achado : -1;
    }

    // ================= disco de telefone =================

    /// <summary>
    /// Discagens em andamento: quantas bordas ainda faltam.
    ///
    /// O disco nao e um controle de posicao como o potenciometro: ele
    /// e um EVENTO com duracao. Discar o 3 e mandar tres pulsos, um
    /// depois do outro, e o sketch conta as bordas. Um controle
    /// deslizante nao teria como representar isso.
    /// </summary>
    /// <summary>
    /// A discagem em curso de cada disco.
    ///
    /// Sao DUAS fases, como no telefone: primeiro o dedo leva o disco
    /// ate o batente, e so na VOLTA e que os pulsos saem. Antes so
    /// existia a volta, e o disco aparecia ja girado sem que nada
    /// tivesse acontecido na tela -- ninguem entendia o que via.
    /// </summary>
    sealed class Discagem
    {
        public int Numero;
        public double Angulo;
        public bool Indo = true;
        public int Bordas;
    }

    readonly Dictionary<string, Discagem> discagens = [];

    /// <summary>Graus por furo, como no disco de verdade.</summary>
    const double GrausPorFuro = 30.0;

    /// <summary>Quanto o disco anda por quadro na ida e na volta.</summary>
    const double PassoDaIda = 14.0, PassoDaVolta = 6.0;

    /// <summary>
    /// Comeca a discar um numero neste disco.
    ///
    /// Como no telefone de verdade, o "0" manda DEZ pulsos -- e uma das
    /// coisas que o exercicio existe para mostrar.
    /// </summary>
    public void Discar(string idDoDisco, int numero)
    {
        int pulsos = numero == 0 ? 10 : Math.Clamp(numero, 1, 10);

        discagens[idDoDisco] = new Discagem
        {
            Numero = numero,
            // Duas bordas por pulso: sobe e desce.
            Bordas = pulsos * 2,
        };

        Mudou?.Invoke();
    }

    /// <summary>Que numero este disco esta discando agora, ou -1.</summary>
    public int NumeroDiscado(string idDoDisco)
        => discagens.TryGetValue(idDoDisco, out var d) ? d.Numero : -1;

    public bool Discando(string idDoDisco) => discagens.ContainsKey(idDoDisco);

    /// <summary>
    /// Toca a discagem, uma borda por quadro.
    ///
    /// Uma borda por quadro da ~40 ms por borda, ou seja ~12 pulsos por
    /// segundo -- perto dos 10 pps do telefone de verdade, e devagar o
    /// bastante para o sketch do aluno contar sem precisar de
    /// interrupcao.
    ///
    /// DIAL fica em LOW enquanto o disco volta, que e como o aparelho
    /// avisa "estou discando"; PULSE e quem pulsa.
    /// </summary>
    void AplicarDisco(PecaNoCircuito peca, Maquina maq, Netlist rede)
    {
        int dial = rede.GpioDe(Atual, peca.Id, "DIAL");
        int pulse = rede.GpioDe(Atual, peca.Id, "PULSE");

        if (!discagens.TryGetValue(peca.Id, out var disco))
        {
            // Em repouso os dois ficam em repouso: HIGH, como o contato
            // fechado do disco parado.
            if (dial >= 0) maq.NivelExterno[dial] = 1;
            if (pulse >= 0) maq.NivelExterno[pulse] = 1;
            return;
        }

        // ---- ida: o dedo leva o disco ate o batente ----
        if (disco.Indo)
        {
            double alvo = (disco.Numero == 0 ? 10 : disco.Numero) * GrausPorFuro;
            disco.Angulo = Math.Min(alvo, disco.Angulo + PassoDaIda);

            // O DIAL cai assim que o disco sai do lugar -- e o contato
            // que o aparelho usa para calar o fone enquanto se disca.
            // O PULSE so trabalha na volta.
            if (dial >= 0) maq.NivelExterno[dial] = 0;
            if (pulse >= 0) maq.NivelExterno[pulse] = 1;

            if (disco.Angulo >= alvo - 0.001) disco.Indo = false;
            return;
        }

        // ---- volta: e aqui que os pulsos saem ----
        if (dial >= 0) maq.NivelExterno[dial] = 0;

        // Cada quadro consome uma borda. Par = LOW, impar = HIGH, entao
        // a contagem termina sempre com o pino de volta em HIGH.
        //
        // Acabadas as bordas, o PULSE fica em repouso enquanto o disco
        // termina de voltar: sem isto ele continuava alternando com a
        // contagem negativa e o sketch lia pulsos a mais do que o numero
        // discado.
        if (pulse >= 0)
            maq.NivelExterno[pulse] = disco.Bordas > 0 && disco.Bordas % 2 == 0 ? 0 : 1;

        if (disco.Bordas > 0) disco.Bordas--;
        disco.Angulo = Math.Max(0, disco.Angulo - PassoDaVolta);

        // So acaba quando as bordas terminaram E o disco chegou em casa:
        // senao ele para girado, ou continua pulsando ja parado.
        if (disco.Bordas <= 0 && disco.Angulo <= 0.001) discagens.Remove(peca.Id);
    }

    // ================= motor de passo biaxial =================

    /// <summary>Os dois eixos de cada motor biaxial do circuito.</summary>
    readonly Dictionary<string, EixoDePasso[]> eixosDePasso = [];

    /// <summary>Ponteiro que mostra o eixo girando.</summary>
    readonly Dictionary<string, Line[]> ponteirosDePasso = [];

    /// <summary>
    /// O motor biaxial: dois motores de passo no mesmo corpo.
    ///
    /// A conta sai dos PINOS, e nao da biblioteca Stepper. E de proposito:
    /// o exercicio de motor de passo costuma ser escrever a sequencia a
    /// mao com digitalWrite, e quem faz isso nunca cria um objeto
    /// Stepper. Lendo as bobinas, os dois jeitos funcionam.
    /// </summary>
    void AplicarBiaxial(PecaNoCircuito peca, Maquina maq, Netlist rede)
    {
        if (!eixosDePasso.TryGetValue(peca.Id, out var eixos))
            eixosDePasso[peca.Id] = eixos = [new EixoDePasso(), new EixoDePasso()];

        // A ordem importa: e a sequencia de acionamento, nao a ordem em
        // que os pinos aparecem no componente.
        string[][] bobinas =
        [
            ["A1+", "B1+", "A1-", "B1-"],
            ["A2+", "B2+", "A2-", "B2-"],
        ];

        for (int eixo = 0; eixo < 2; eixo++)
        {
            var ligadas = new bool[4];
            bool algumGpio = false;
            for (int k = 0; k < 4; k++)
            {
                int gpio = rede.GpioDe(Atual, peca.Id, bobinas[eixo][k]);
                algumGpio |= gpio >= 0;
                ligadas[k] = gpio >= 0 && maq.Pwm[gpio] > 127;
            }
            // Sem GPIO nas pontas: as bobinas numa ponte H (EditorPasso.cs).
            if (!algumGpio && BobinasPelaTensao(peca, bobinas[eixo]) is { } pelaPonte) ligadas = pelaPonte;
            eixos[eixo].Atualizar(ligadas);
        }

        MostrarBiaxial(peca, eixos);
    }

    /// <summary>
    /// O motor de passo de quatro fios (o 28BYJ-48 da caixa de peca).
    ///
    /// Mesma conta do biaxial, com um eixo so: as quatro bobinas na
    /// ordem da sequencia, e o angulo sai da diferenca de fase. Quem
    /// mostra o giro e o eixo do desenho, que agora e uma camada
    /// separada -- "o motor de 4 passos deveria ter alguma animacao(?)",
    /// e devia mesmo: ate aqui ele ficava parado girando.
    /// </summary>
    void AplicarPasso(PecaNoCircuito peca, Maquina maq, Netlist rede)
    {
        if (!eixosDePasso.TryGetValue(peca.Id, out var eixos))
            eixosDePasso[peca.Id] = eixos = [new EixoDePasso()];

        string[] bobinas = ["A+", "B+", "A-", "B-"];
        int[] gpios = [.. bobinas.Select(b => rede.GpioDe(Atual, peca.Id, b))];
        // Sem GPIO nas pontas: as bobinas numa ponte H (EditorPasso.cs).
        if (gpios.All(g => g < 0))
        {
            if (BobinasPelaTensao(peca, bobinas) is { } pelaPonte) eixos[0].Atualizar(pelaPonte);
            return;
        }
        eixos[0].Seguir(maq, gpios);
    }

    /// <summary>
    /// Um ponteiro por eixo. Sem isto o motor gira e ninguem ve: o
    /// desenho do componente e o mesmo parado ou andando.
    /// </summary>
    /// <summary>
    /// Onde as duas hastes do biaxial giram, em pixels da peca.
    ///
    /// Vem do SVG: o grupo esta em translate(45 30) e a haste em
    /// translate(60 86) dentro dele.
    /// </summary>
    static readonly Point EixoDoBiaxial = new(105, 116);

    void MostrarBiaxial(PecaNoCircuito peca, EixoDePasso[] eixos)
    {
        var def = Catalogo.Obter(peca.Tipo);
        if (def is null) return;

        if (!ponteirosDePasso.TryGetValue(peca.Id, out var ponteiros))
        {
            ponteiros = new Line[2];
            for (int i = 0; i < 2; i++)
            {
                ponteiros[i] = new Line
                {
                    // Uma dourada e uma prateada, como as duas hastes do
                    // desenho original.
                    Stroke = new SolidColorBrush(i == 0
                        ? Color.FromRgb(0xE8, 0xC1, 0x3B)
                        : Color.FromRgb(0xC8, 0xCC, 0xD2)),
                    StrokeThickness = i == 0 ? 4 : 3,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round,
                    IsHitTestVisible = false
                };
                Panel.SetZIndex(ponteiros[i], 320);
                mundo.Children.Add(ponteiros[i]);
            }
            ponteirosDePasso[peca.Id] = ponteiros;
        }

        // Os dois eixos sao CONCENTRICOS -- e um movimento de relogio,
        // e nao dois motores empilhados. O ponto vem do proprio
        // desenho: as hastes originais giravam em translate(60 86)
        // dentro de um grupo em translate(45 30).
        var centro = Geometria.PosicaoLocal(peca, def, EixoDoBiaxial.X, EixoDoBiaxial.Y);

        for (int i = 0; i < 2; i++)
        {
            // Uma curta e uma comprida, como o ponteiro das horas e o
            // dos minutos: assim da para ver os dois ao mesmo tempo.
            double raio = i == 0 ? 30 : 46;
            double rad = eixos[i].Angulo * Math.PI / 180;

            ponteiros[i].X1 = centro.X;
            ponteiros[i].Y1 = centro.Y;
            ponteiros[i].X2 = centro.X + Math.Cos(rad) * raio;
            ponteiros[i].Y2 = centro.Y + Math.Sin(rad) * raio;
        }
    }

    /// <summary>Angulo de um eixo, para o teste conferir de fora.</summary>
    internal double AnguloDoEixoParaTeste(string idDaPeca, int eixo)
        => eixosDePasso.TryGetValue(idDaPeca, out var eixos)
           && eixo >= 0 && eixo < eixos.Length ? eixos[eixo].Angulo : 0;

    /// <summary>
    /// Sem receptor no circuito, o controle remoto nao chega a lugar
    /// nenhum.
    ///
    /// O codigo do controle e uma grandeza solta ("ir:codigo"), sem pino
    /// -- infravermelho nao tem fio. Isso fazia o sketch receber codigo
    /// com o receptor fora da tela, que e justamente o erro de montagem
    /// que o aluno precisa ver.
    /// </summary>
    void ConferirParInfravermelho(Maquina maq)
    {
        bool temControle = Atual.Pecas.Any(x => x.Tipo == "ir-remote");
        bool temReceptor = Atual.Pecas.Any(x => x.Tipo == "ir-receiver");

        if (temControle && !temReceptor) maq.Leituras["ir:codigo"] = 0;
    }

    /// <summary>
    /// A maquina da placa em que esta peca esta ligada.
    ///
    /// Este e o unico lugar do editor que sabe que existe mais de uma
    /// placa. Resolvendo AQUI, uma vez por peca, todo o resto do desenho
    /// continua escrito como se houvesse uma so -- e foi o que permitiu
    /// juntar as duas coisas sem reescrever trinta e seis lugares.
    ///
    /// Peca ligada em duas placas (um botao compartilhado, por exemplo)
    /// fica com a primeira que aparecer. E uma ambiguidade de verdade:
    /// ninguem sabe qual das duas deveria mandar, e escolher em silencio
    /// e melhor do que sortear a cada quadro.
    ///
    /// Peca solta fica com a maquina desligada da bancada, entao ela
    /// apaga em vez de guardar o ultimo estado.
    /// </summary>
    Maquina MaquinaDaPeca(Bancada bancada, Netlist rede, PecaNoCircuito peca)
    {
        var def = Catalogo.Obter(peca.Tipo);
        if (def is null) return bancada.Solta;

        // A propria placa e obvia; protoboard tem mil furos e nao ha o
        // que perguntar sobre ela.
        if (PerfilPlaca.EhPlaca(peca.Tipo)) return bancada.De(peca.Id);
        if (Catalogo.EhProtoboard(peca.Tipo)) return bancada.Solta;

        // O controle infravermelho nao tem fio: a tecla chega na placa do
        // receptor que o "ve" (o primeiro ligado numa placa).
        if (peca.Tipo == "ir-remote")
        {
            foreach (var receptor in Atual.Pecas.Where(p => p.Tipo == "ir-receiver"))
                if (PlacasDaPeca(rede, receptor) is { Count: > 0 } doReceptor) return bancada.De(doReceptor[0]);
            return bancada.Solta;
        }

        var placas = PlacasDaPeca(rede, peca);
        if (placas.Count == 0) return bancada.Solta;

        // A escolha da pessoa vale -- desde que a peca ainda alcance
        // aquela placa. Escolha velha, de um fio que ja nao existe, cai
        // para a primeira em vez de mandar a peca obedecer uma placa com
        // a qual ela nao tem mais ligacao nenhuma.
        if (peca.PlacaQueManda is { } escolhida && placas.Contains(escolhida))
            return bancada.De(escolhida);

        return bancada.De(placas[0]);
    }

    /// <summary>
    /// As placas que esta peca alcanca por sinal, na ordem dos pinos.
    ///
    /// Terra nao entra: GND nao e pino numerado, entao nao aparece aqui
    /// -- e ligar o GND das duas placas e o certo a fazer, nao uma
    /// ambiguidade.
    /// </summary>
    public List<string> PlacasDaPeca(Netlist rede, PecaNoCircuito peca)
    {
        var placas = new List<string>();
        if (rede is null || peca is null) return placas;

        var def = Catalogo.Obter(peca.Tipo);
        if (def is null) return placas;

        foreach (var pino in def.Pinos)
        {
            var (placa, _) = rede.PinoDe(Atual, peca.Id, pino.Nome);
            if (placa is not null && !placas.Contains(placa)) placas.Add(placa);
        }
        return placas;
    }

    /// <summary>Escolhe qual placa manda na peca selecionada.</summary>
    public void DefinirPlacaQueManda(string idDaPlaca)
    {
        var peca = Selecionada;
        if (peca is null || peca.PlacaQueManda == idDaPlaca) return;
        peca.PlacaQueManda = idDaPlaca;
        Mudou?.Invoke();
    }

    /// <summary>
    /// Pecas ligadas em MAIS DE UMA placa, com os nomes das placas.
    ///
    /// Elas obedecem a primeira que aparecer, e isso e uma escolha
    /// arbitraria: ninguem sabe qual das duas deveria mandar. Escolher
    /// calado era pior -- o LED acendia com a placa errada e nada na
    /// tela explicava. Agora a janela avisa.
    ///
    /// Compartilhar o GND das duas placas NAO conta: o terra nao e pino
    /// numerado, entao nao aparece aqui. O que conta e sinal.
    /// </summary>
    public IEnumerable<(string Peca, string[] Placas)> PecasEntreDuasPlacas(Netlist rede)
    {
        if (rede is null) yield break;

        foreach (var peca in Atual.Pecas)
        {
            if (PerfilPlaca.EhPlaca(peca.Tipo) || Catalogo.EhProtoboard(peca.Tipo)) continue;

            var def = Catalogo.Obter(peca.Tipo);
            if (def is null) continue;

            var placas = PlacasDaPeca(rede, peca);
            if (placas.Count < 2) continue;

            // Quem ja escolheu nao e mais ambiguo: o aviso some. Escolha
            // que aponta para placa que a peca nao alcanca nao conta.
            if (peca.PlacaQueManda is { } escolhida && placas.Contains(escolhida))
                continue;

            yield return (peca.Id, [.. placas]);
        }
    }

    int PrimeiroGpio(Netlist rede, string peca, params string[] pinos)
    {
        foreach (string p in pinos)
        {
            int g = rede.GpioDe(Atual, peca, p);
            if (g >= 0) return g;
        }
        return -1;
    }

    // ================= reflexo da simulacao =================
    public void RefletirEstado(Bancada bancada, Netlist rede)
    {
        if (rede is null || bancada is null) return;

        DesenharSaidas(bancada, rede);

        // Alguma placa energizada? E o que diz se os trilhos estao vivos.
        bool bancadaLigada = bancada.Todas.Any(x => x.Value.Energizada);

        foreach (var peca in Atual.Pecas)
        {
            if (!visuais.TryGetValue(peca.Id, out var visual)) continue;

            var maq = MaquinaDaPeca(bancada, rede, peca);

            // O desenho da parte que se mexe segue o estado da peca,
            // qualquer que seja ela: botao girado, manche empurrado,
            // disco voltando, eixo do motor.
            var defAnimada = Catalogo.Obter(peca.Tipo);
            if (defAnimada is not null)
            {
                AnimarParteMovel(peca, defAnimada);
                if (peca.Tipo == "dip-switch-8") MostrarChavesDip(peca, defAnimada);
            }

            switch (peca.Tipo)
            {
                case "led":
                {
                    int brilho = BrilhoEntre(peca, "A", "C", maq, rede, bancadaLigada);

                    // Sem placa mandando nele -- a bateria, uma chave --, o
                    // brilho e o da corrente que a conta de energia achou.
                    if (brilho == 0 && EnergiaDoQuadro is { } conta)
                        brilho = BrilhoPelaCorrente(conta.CorrenteDoRamo(peca.Id, "led"));

                    // Queimado nao acende mais, que e o ponto do estouro.
                    if (estouros.ContainsKey(peca.Id)) brilho = 0;

                    // A arte acesa entra assim que ha corrente; quem faz
                    // o meio-termo entre uma e outra e o halo.
                    if (visual is Image img)
                    {
                        var alvo = Pecas.Obter(NomeSvg(peca, brilho > 0));
                        if (!ReferenceEquals(img.Source, alvo)) img.Source = alvo;
                    }

                    // Glow de verdade: halo radial que some nas bordas,
                    // com intensidade proporcional ao PWM.
                    if (brilhos.TryGetValue(peca.Id, out var luz))
                    {
                        double f = brilho / 255.0;
                        if (f <= 0) AjustarHalo(luz, 0);
                        else
                        {
                            var c = CorDoLed(peca.Cor);
                            // Do branco quente para a cor e depois some.
                            // O disco chapado na cor do LED que estava aqui
                            // parecia um adesivo por cima do bulbo.
                            luz.Fill = new RadialGradientBrush
                            {
                                GradientStops =
                                [
                                    new GradientStop(Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF), 0.00),
                                    new GradientStop(Color.FromArgb(0xB4, MisturarBranco(c, 0.55).R, MisturarBranco(c, 0.55).G, MisturarBranco(c, 0.55).B), 0.13),
                                    new GradientStop(Color.FromArgb(0x66, c.R, c.G, c.B), 0.34),
                                    new GradientStop(Color.FromArgb(0x00, c.R, c.G, c.B), 1.00),
                                ]
                            };
                            AjustarHalo(luz, f);
                        }
                    }
                    break;
                }

                case "ili9341":
                case "st7789":
                    DesenharTft(peca, maq.Tela, LuzDaTft(peca, maq, rede));
                    break;

                case "ssd1306":
                case "ssd1306-i2c":
                    DesenharOled(peca, maq, rede);
                    break;

                case "lcd1602":
                case "lcd2004":
                case "lcd1602-i2c":
                case "lcd2004-i2c":
                {
                    // O modulo de I2C so recebe o texto se o fio dele chega no Wire.
                    if (maq.Lcd is not { Ligado: true } lcd
                        || peca.Tipo.EndsWith("-i2c", StringComparison.Ordinal) && maq.I2c.Ligado
                           && !NoFioDoBarramento(maq.I2c, rede.GpioDe(Atual, peca.Id, "SDA"), rede.GpioDe(Atual, peca.Id, "SCL"))
                        || !telasLcd.TryGetValue(peca.Id, out var tela)
                        || !Grades.TryGetValue(peca.Tipo, out var g)) break;

                    for (int lin = 0; lin < g.Linhas && lin < lcd.Linhas; lin++)
                    {
                        string texto = lcd.LinhaTexto(lin);
                        for (int col = 0; col < g.Colunas && col < texto.Length; col++)
                        {
                            string ch = texto[col].ToString();
                            if (tela[lin, col].Text != ch) tela[lin, col].Text = ch;
                        }
                    }
                    break;
                }

                // O braco esta onde o servo chegou (ele anda na velocidade do
                // datasheet, e so com o V+ alimentado), e nao onde foi mandado.
                case "servo":
                case "servo-mg996r":
                {
                    if (bracosServo.TryGetValue(peca.Id, out var braco)
                        && braco.RenderTransform is RotateTransform giroS)
                    {
                        // 0 grau aponta para a esquerda, 180 para a direita.
                        giroS.Angle = peca.Rotacao + AnguloDoServo(peca) - 90;
                        braco.Opacity = servos.TryGetValue(peca.Id, out var est) && est.Recebendo ? 1.0 : 0.6;
                    }
                    break;
                }

                case "buzzer":
                {
                    // Acende um pouco enquanto toca: da para ver qual
                    // buzzer esta soando quando ha mais de um.
                    int gpioB = rede.GpioDe(Atual, peca.Id, "1");
                    if (gpioB < 0) gpioB = rede.GpioDe(Atual, peca.Id, "2");
                    visual.Opacity = gpioB >= 0 && maq.Frequencia[gpioB] > 0 ? 1.0 : 0.72;
                    break;
                }

                case "alto-falante":
                {
                    // Pulsa com o som: pelo amplificador, no volume do que
                    // esta tocando agora; num pino, aceso enquanto houver tom.
                    var (audio, hzDoPino) = SomDoAltoFalante(peca, bancada, rede);
                    double nivel = audio is not null ? Math.Clamp(audio.Nivel * 1.6, 0, 1) : hzDoPino > 0 ? 1 : 0;
                    visual.Opacity = 0.72 + 0.28 * nivel;
                    break;
                }

                case "tilt-switch":
                {
                    // Inclinar de verdade: o corpo vira, e a bolinha de
                    // dentro do desenho vira junto. Antes o sensor de
                    // inclinacao era a unica peca que "mudava de estado"
                    // sem nada mudar na tela.
                    var defT = Catalogo.Obter(peca.Tipo);
                    if (defT is not null)
                        InclinarPeca(peca, defT, visual, peca.Giro > 0.5 ? -17 : 0);
                    break;
                }
            }
        }

        DesenharNiveisLogicos();
        AtualizarAnalisadores(bancada, rede);
        AtualizarEnergia(bancada, rede);
    }

    /// <summary>
    /// Pecas de saida (LED RGB, barra de LEDs, display de 7 segmentos):
    /// um ponto de luz sobre cada pino aceso, na posicao do proprio pino.
    /// Nao precisa saber a geometria interna do desenho.
    /// </summary>
    void DesenharSaidas(Bancada bancada, Netlist rede)
    {
        var grupo = new GeometryGroup();
        var pincel = new GradientStopCollection();

        foreach (var peca in Atual.Pecas)
        {
            var comp = Comportamento.De(peca.Tipo);
            if (comp is null || comp.Saidas.Length == 0) continue;
            var def = Catalogo.Obter(peca.Tipo);
            if (def is null) continue;

            var maq = MaquinaDaPeca(bancada, rede, peca);

            if (peca.Tipo == "rgb-led") { DesenharRgb(peca, def, maq, rede); continue; }

            foreach (string nome in comp.Saidas)
            {
                int gpio = rede.GpioDe(Atual, peca.Id, nome);
                double forca = gpio >= 0 ? maq.Pwm[gpio] / 255.0 : 0;

                // A barra de LEDs na bateria: cada LED pela corrente dele.
                if (forca == 0 && peca.Tipo == "led-bar-graph" && EnergiaDoQuadro is { } conta
                    && nome.StartsWith('A') && !estouros.ContainsKey(peca.Id))
                    forca = BrilhoPelaCorrente(conta.CorrenteDoRamo(peca.Id, nome[1..])) / 255.0;
                if (forca <= 0) continue;

                var pino = def.Pinos.FirstOrDefault(x => x.Nome == nome);
                if (pino is null) continue;

                // O ponto cresce com o brilho: num display multiplexado
                // ou numa barra em rampa, da para ver a intensidade de
                // cada segmento, e nao so que ele esta ligado.
                double raio = 4.2 * (0.55 + 0.45 * Luminancia(forca));
                grupo.Children.Add(new EllipseGeometry(
                    Geometria.PosicaoPino(peca, def, pino), raio, raio));
            }
        }
        saidasAcesas.Data = grupo;
    }

    /// <summary>
    /// O brilho de um LED entre o anodo e o catodo, de 0 a 255: o quanto o
    /// anodo esta ALTO combinado com o quanto o catodo esta BAIXO.
    ///
    /// Antes so o anodo contava. Dois erros saiam disso, e os dois ao
    /// contrario da placa de verdade:
    ///
    /// - o LED ligado "ao contrario" -- anodo no 3V3, catodo no GPIO, aceso
    ///   com LOW -- nunca acendia. E o jeito certo nos modulos ESP-01, em
    ///   que o GPIO0 e o GPIO2 precisam estar altos na partida, e e como
    ///   vem o LED de bordo da NodeMCU;
    /// - o LED com o catodo solto acendia, sem ter por onde a corrente
    ///   voltar.
    /// </summary>
    int BrilhoEntre(PecaNoCircuito peca, string anodo, string catodo, Maquina maq, Netlist rede,
                    bool bancadaLigada)
    {
        int gA = rede.GpioDe(Atual, peca.Id, anodo);
        int gC = rede.GpioDe(Atual, peca.Id, catodo);

        // LED preso no TRILHO acende enquanto houver placa ligada: e o
        // caso do simbolo de VCC e do 3V3 da propria placa.
        int alto = gA >= 0 ? maq.Pwm[gA]
                 : bancadaLigada && rede.AlcancaFonte(Atual, peca.Id, anodo, terra: false) ? 255 : 0;
        int baixo = gC >= 0 ? 255 - maq.Pwm[gC]
                  : rede.AlcancaFonte(Atual, peca.Id, catodo, terra: true) ? 255 : 0;

        return Math.Min(alto, baixo);
    }

    /// <summary>LED RGB: mistura os tres canais num brilho so.</summary>
    void DesenharRgb(PecaNoCircuito peca, PecaDef def, Maquina maq, Netlist rede)
    {
        if (!brilhos.TryGetValue(peca.Id, out var luz)) return;

        // Anodo comum (COM no 3V3/5V): cada cor acende com o pino em LOW.
        bool anodoComum = rede.AlcancaFonte(Atual, peca.Id, "COM", terra: false);

        int Canal(string nome)
        {
            int g = rede.GpioDe(Atual, peca.Id, nome);
            if (g < 0) return 0;
            return anodoComum ? 255 - maq.Pwm[g] : maq.Pwm[g];
        }

        int r = Canal("R"), v = Canal("G"), b = Canal("B");

        // Na bateria, sem placa: cada cor pela corrente dela.
        if (r == 0 && v == 0 && b == 0 && EnergiaDoQuadro is { } conta)
        {
            r = BrilhoPelaCorrente(conta.CorrenteDoRamo(peca.Id, "R"));
            v = BrilhoPelaCorrente(conta.CorrenteDoRamo(peca.Id, "G"));
            b = BrilhoPelaCorrente(conta.CorrenteDoRamo(peca.Id, "B"));
        }
        if (estouros.ContainsKey(peca.Id)) r = v = b = 0;
        int forte = Math.Max(r, Math.Max(v, b));

        if (forte == 0) { AjustarHalo(luz, 0); return; }

        var cor = Color.FromRgb((byte)r, (byte)v, (byte)b);
        luz.Fill = new RadialGradientBrush
        {
            GradientStops =
            [
                new GradientStop(Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF), 0.00),
                new GradientStop(Color.FromArgb(0x99, cor.R, cor.G, cor.B), 0.16),
                new GradientStop(Color.FromArgb(0x00, cor.R, cor.G, cor.B), 1.00),
            ]
        };
        AjustarHalo(luz, forte / 255.0);
    }

    /// <summary>
    /// Do PWM (0..1) para o brilho que aparece na tela.
    ///
    /// Antes a conta era "0,30 + 0,70 x f": um analogWrite(1) ja acendia
    /// a 30%, e dai para cima subia numa reta. Isso e o contrario do que
    /// o olho faz -- e por isso o professor viu o sinal analogico "duro",
    /// com o LED pulando de apagado para quase aceso no primeiro degrau.
    ///
    /// A curva de gama 0,6 sobe devagar no comeco e acelera depois, que
    /// e a resposta do olho ao contrario; o resultado e uma rampa de
    /// analogWrite que entra e sai suave, sem degrau na largada.
    /// </summary>
    static double Luminancia(double f)
        => f <= 0 ? 0 : Math.Pow(Math.Clamp(f, 0, 1), 0.6);

    /// <summary>
    /// O halo tambem CRESCE com o brilho, e nao so fica mais opaco.
    ///
    /// E o que separa um LED a 10% de um a 100% de relance: a mancha de
    /// luz aumenta. So a opacidade mudando parecia o mesmo LED atras de
    /// um vidro mais ou menos sujo.
    /// </summary>
    static void AjustarHalo(Ellipse luz, double brilho)
    {
        double lum = Luminancia(brilho);
        luz.Opacity = 0.94 * lum;

        if (luz.RenderTransform is not ScaleTransform escala)
        {
            luz.RenderTransformOrigin = new Point(0.5, 0.5);
            luz.RenderTransform = escala = new ScaleTransform(1, 1);
        }
        double tamanho = 0.62 + 0.38 * lum;
        escala.ScaleX = escala.ScaleY = tamanho;
    }

    static Color MisturarBranco(Color c, double f) => Color.FromRgb(
        (byte)(c.R + (255 - c.R) * f),
        (byte)(c.G + (255 - c.G) * f),
        (byte)(c.B + (255 - c.B) * f));

    static Color CorDoLed(string nome) => nome switch
    {
        "vermelho" => Color.FromRgb(0xFF, 0x30, 0x30),
        "amarelo" => Color.FromRgb(0xFF, 0xE0, 0x40),
        "verde" => Color.FromRgb(0x50, 0xFF, 0x60),
        "azul" => Color.FromRgb(0x50, 0x90, 0xFF),
        "branco" => Color.FromRgb(0xFF, 0xFF, 0xFF),
        "laranja" => Color.FromRgb(0xFF, 0x9A, 0x30),
        "roxo" => Color.FromRgb(0xC0, 0x70, 0xFF),
        _ => Color.FromRgb(0xFF, 0xFF, 0xFF),
    };
}
