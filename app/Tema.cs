using System.Windows;
using System.Windows.Media;

namespace Simulador.App;

/// <summary>
/// Os temas do programa.
///
/// Cada tema escolhe as cores de base -- fundo, painel, texto, destaque.
/// As outras saem delas por regra (o texto em cima do destaque, a cor da
/// seleção, a do aviso), e o tema só diz alguma coisa quando a regra não
/// serve. Assim um tema novo são dez linhas, e não trinta decisões.
///
/// O autoteste mede o contraste de cada tema pela conta da WCAG: um tema
/// com texto ilegível não passa, por mais bonito que seja na paleta.
/// </summary>
public static class Tema
{
    public const string Fundo = "Fundo";
    public const string Painel = "Painel";
    public const string Campo = "Campo";
    public const string Borda = "Borda";
    public const string Texto = "Texto";
    public const string TextoFraco = "TextoFraco";
    public const string Destaque = "Destaque";
    public const string FundoDoCircuito = "FundoDoCircuito";
    public const string PontoDaGrade = "PontoDaGrade";
    public const string TextoForte = "TextoForte";

    // ---- as que saem das outras ----

    /// <summary>O texto escrito EM CIMA do destaque (botão de ação, item escolhido).</summary>
    public const string TextoNoDestaque = "TextoNoDestaque";

    /// <summary>O fundo do item apontado num menu ou numa lista.</summary>
    public const string Selecao = "Selecao";

    /// <summary>O fundo de um botão com o mouse em cima.</summary>
    public const string Hover = "Hover";

    /// <summary>O texto e a borda das caixas de aviso.</summary>
    public const string Aviso = "Aviso";

    /// <summary>O texto de erro (o console pinta assim o que deu errado).</summary>
    public const string Erro = "Erro";
    public const string BordaDoAviso = "BordaDoAviso";

    /// <summary>As cores dos três painéis de texto: serial, rede e Bluetooth.</summary>
    public const string Terminal = "Terminal";
    public const string TerminalRede = "TerminalRede";
    public const string TerminalBt = "TerminalBt";

    public static readonly string[] Chaves =
    [
        Fundo, Painel, Campo, Borda, Texto, TextoForte, TextoFraco, Destaque,
        FundoDoCircuito, PontoDaGrade, TextoNoDestaque, Selecao, Hover,
        Aviso, BordaDoAviso, Erro, Terminal, TerminalRede, TerminalBt,
    ];

    /// <summary>Um tema: o nome que aparece, o grupo, se é escuro e as cores.</summary>
    public sealed record Paleta(string Id, string Nome, string Grupo, bool Escura,
                                Dictionary<string, Color> Cores);

    public const string GrupoBasicos = "Básicos";
    public const string GrupoDasPlacas = "Das placas";
    public const string GrupoDasCores = "Cores";

    static readonly List<Paleta> paletas =
    [
        // ================= básicos =================
        Nova("escuro", "Escuro", GrupoBasicos, true, new()
        {
            [Fundo] = Cor("#1E1E1E"), [Painel] = Cor("#252526"), [Campo] = Cor("#1B1B1B"),
            [Borda] = Cor("#3C3C3C"), [Texto] = Cor("#D4D4D4"), [TextoForte] = Cor("#EAEAEA"),
            [TextoFraco] = Cor("#858585"), [Destaque] = Cor("#00979D"),
            [FundoDoCircuito] = Cor("#101012"), [PontoDaGrade] = Cor("#2C2C30"),
            [Selecao] = Cor("#094771"),
        }),
        Nova("claro", "Claro", GrupoBasicos, false, new()
        {
            [Fundo] = Cor("#EFEFF1"), [Painel] = Cor("#FFFFFF"), [Campo] = Cor("#FFFFFF"),
            [Borda] = Cor("#C6C6CC"), [Texto] = Cor("#23232A"), [TextoForte] = Cor("#101014"),
            [TextoFraco] = Cor("#6B6B75"), [Destaque] = Cor("#00757B"),
            [FundoDoCircuito] = Cor("#F7F7F9"), [PontoDaGrade] = Cor("#C9C9D2"),
            [Selecao] = Cor("#CFE3F7"),
        }),

        // ================= das placas =================

        // A Arduino IDE 2 no tema de fábrica: branco, cinza-azulado e o
        // verde-azulado da barra de cima.
        Nova("arduino-ide", "Arduino IDE", GrupoDasPlacas, false, new()
        {
            [Fundo] = Cor("#ECF1F1"), [Painel] = Cor("#FFFFFF"), [Campo] = Cor("#F7F9F9"),
            [Borda] = Cor("#DAE3E3"), [Texto] = Cor("#374146"), [TextoForte] = Cor("#1B2327"),
            [TextoFraco] = Cor("#65737A"), [Destaque] = Cor("#008184"),
            [FundoDoCircuito] = Cor("#F7F9F9"), [PontoDaGrade] = Cor("#CCD8D8"),
            [Selecao] = Cor("#C9E6E6"),
        }),

        // A placa azul em cima da bancada preta.
        Nova("arduino", "Arduino", GrupoDasPlacas, true, new()
        {
            [Fundo] = Cor("#06090D"), [Painel] = Cor("#0D1620"), [Campo] = Cor("#070C12"),
            [Borda] = Cor("#1D3550"), [Texto] = Cor("#D6E4F2"), [TextoForte] = Cor("#F0F6FC"),
            [TextoFraco] = Cor("#7F95AD"), [Destaque] = Cor("#1B6FC0"),
            [FundoDoCircuito] = Cor("#04070B"), [PontoDaGrade] = Cor("#16283C"),
            [Selecao] = Cor("#123A63"),
        }),

        // O branco da Nucleo e o azul da ST.
        Nova("stm32", "STM32", GrupoDasPlacas, false, new()
        {
            [Fundo] = Cor("#EEF3F9"), [Painel] = Cor("#FFFFFF"), [Campo] = Cor("#FFFFFF"),
            [Borda] = Cor("#C5D3E3"), [Texto] = Cor("#0B2545"), [TextoForte] = Cor("#03234B"),
            [TextoFraco] = Cor("#526887"), [Destaque] = Cor("#0369A1"),
            [FundoDoCircuito] = Cor("#F7FAFD"), [PontoDaGrade] = Cor("#C8D6E6"),
            [Selecao] = Cor("#CDE7F7"),
        }),

        // A folha verde e a framboesa vermelha.
        Nova("raspberry-pi", "Raspberry Pi", GrupoDasPlacas, true, new()
        {
            [Fundo] = Cor("#0C1A10"), [Painel] = Cor("#122417"), [Campo] = Cor("#0A160D"),
            [Borda] = Cor("#24462E"), [Texto] = Cor("#DCEFE0"), [TextoForte] = Cor("#F1FAF3"),
            [TextoFraco] = Cor("#86A98E"), [Destaque] = Cor("#D22A55"),
            [FundoDoCircuito] = Cor("#08120B"), [PontoDaGrade] = Cor("#1B3522"),
            [Selecao] = Cor("#1F4D2B"),
        }),

        // Preto e branco, sem meio-termo -- o destaque tambem e branco, e
        // o texto em cima dele e preto.
        Nova("esp32", "ESP32", GrupoDasPlacas, true, new()
        {
            [Fundo] = Cor("#000000"), [Painel] = Cor("#0B0B0B"), [Campo] = Cor("#000000"),
            [Borda] = Cor("#3A3A3A"), [Texto] = Cor("#F2F2F2"), [TextoForte] = Cor("#FFFFFF"),
            [TextoFraco] = Cor("#9A9A9A"), [Destaque] = Cor("#FFFFFF"),
            [FundoDoCircuito] = Cor("#000000"), [PontoDaGrade] = Cor("#262626"),
            [Selecao] = Cor("#333333"),
        }),

        // ================= cores =================
        // Escuros, com a cor no fundo e no destaque: e o que se aguenta
        // olhando por uma aula inteira.
        Nova("azul", "Azul", GrupoDasCores, true, new()
        {
            [Fundo] = Cor("#0D1626"), [Painel] = Cor("#13203A"), [Campo] = Cor("#0B1322"),
            [Borda] = Cor("#27406B"), [Texto] = Cor("#DCE7F7"), [TextoForte] = Cor("#F3F7FD"),
            [TextoFraco] = Cor("#8497B8"), [Destaque] = Cor("#2563EB"),
            [FundoDoCircuito] = Cor("#09111F"), [PontoDaGrade] = Cor("#1B2C4A"),
        }),
        Nova("vermelho", "Vermelho", GrupoDasCores, true, new()
        {
            [Fundo] = Cor("#1A0D0E"), [Painel] = Cor("#261214"), [Campo] = Cor("#150A0B"),
            [Borda] = Cor("#4D2227"), [Texto] = Cor("#F5DEDF"), [TextoForte] = Cor("#FDF3F3"),
            [TextoFraco] = Cor("#B08A8D"), [Destaque] = Cor("#DC2626"),
            [FundoDoCircuito] = Cor("#120809"), [PontoDaGrade] = Cor("#3A1A1E"),
        }),
        Nova("verde", "Verde", GrupoDasCores, true, new()
        {
            [Fundo] = Cor("#0C1A12"), [Painel] = Cor("#11261A"), [Campo] = Cor("#0A150E"),
            [Borda] = Cor("#214A31"), [Texto] = Cor("#DAF2E2"), [TextoForte] = Cor("#F1FBF4"),
            [TextoFraco] = Cor("#85AE93"), [Destaque] = Cor("#22C55E"),
            [FundoDoCircuito] = Cor("#08130C"), [PontoDaGrade] = Cor("#1A3624"),
        }),
        Nova("amarelo", "Amarelo", GrupoDasCores, true, new()
        {
            [Fundo] = Cor("#1A1709"), [Painel] = Cor("#25210D"), [Campo] = Cor("#151207"),
            [Borda] = Cor("#4D4418"), [Texto] = Cor("#F5EFD3"), [TextoForte] = Cor("#FDFAEE"),
            [TextoFraco] = Cor("#B3A876"), [Destaque] = Cor("#F2C300"),
            [FundoDoCircuito] = Cor("#121005"), [PontoDaGrade] = Cor("#3A3313"),
        }),
        Nova("laranja", "Laranja", GrupoDasCores, true, new()
        {
            [Fundo] = Cor("#1C1209"), [Painel] = Cor("#28190C"), [Campo] = Cor("#170F07"),
            [Borda] = Cor("#54341A"), [Texto] = Cor("#F7E4D2"), [TextoForte] = Cor("#FDF5EE"),
            [TextoFraco] = Cor("#B79778"), [Destaque] = Cor("#F28C28"),
            [FundoDoCircuito] = Cor("#140C05"), [PontoDaGrade] = Cor("#3E2812"),
        }),
        Nova("rosa", "Rosa", GrupoDasCores, true, new()
        {
            [Fundo] = Cor("#1C0D17"), [Painel] = Cor("#291321"), [Campo] = Cor("#170A13"),
            [Borda] = Cor("#56284A"), [Texto] = Cor("#F7DDEE"), [TextoForte] = Cor("#FDF2F9"),
            [TextoFraco] = Cor("#B7879F"), [Destaque] = Cor("#DB2777"),
            [FundoDoCircuito] = Cor("#14090F"), [PontoDaGrade] = Cor("#3E1A32"),
        }),
    ];

    /// <summary>Os temas, na ordem da tela de configurações.</summary>
    public static IReadOnlyList<Paleta> Paletas => paletas;

    /// <summary>Id e nome de cada tema -- a forma que as telas antigas usavam.</summary>
    public static IEnumerable<(string Id, string Nome)> Todos => paletas.Select(p => (p.Id, p.Nome));

    public static string Atual { get; private set; } = "escuro";

    public static event Action Mudou;

    static Paleta PaletaAtual => paletas.FirstOrDefault(p => p.Id == Atual) ?? paletas[0];

    /// <summary>O tema atual é escuro? E o que decide as cores do código e dos terminais.</summary>
    public static bool EhEscuro => PaletaAtual.Escura;

    /// <summary>O tema com este id é escuro?</summary>
    public static bool EhEscuroOTema(string id)
        => (paletas.FirstOrDefault(p => p.Id == id) ?? paletas[0]).Escura;

    static Color Cor(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    public static string NomeDe(string id)
        => paletas.FirstOrDefault(p => p.Id == id)?.Nome ?? "Escuro";

    public static Paleta De(string id) => paletas.FirstOrDefault(p => p.Id == id);

    public static Color CorDe(string chave)
        => PaletaAtual.Cores.TryGetValue(chave, out var cor) ? cor : Colors.Magenta;

    public static Brush Pincel(string chave)
    {
        var pincel = new SolidColorBrush(CorDe(chave));
        pincel.Freeze();
        return pincel;
    }

    public static void Aplicar(string id)
    {
        if (De(id) is null) id = "escuro";
        Atual = id;
        Preferencias.Definir(Preferencias.Tema, id);
        Espalhar();
    }

    public static void AplicarSalvo()
    {
        Atual = Preferencias.Texto(Preferencias.Tema, "escuro");
        if (De(Atual) is null) Atual = "escuro";
        Espalhar();
    }

    static void Espalhar()
    {
        var recursos = Application.Current?.Resources;
        if (recursos is not null)
            foreach (var (chave, cor) in PaletaAtual.Cores)
            {
                var pincel = new SolidColorBrush(cor);
                pincel.Freeze();
                recursos[chave] = pincel;
            }

        Mudou?.Invoke();
    }

    // =====================================================================
    //  as cores que saem das outras
    // =====================================================================

    static Paleta Nova(string id, string nome, string grupo, bool escura, Dictionary<string, Color> cores)
    {
        void Se(string chave, Color cor) => cores.TryAdd(chave, cor);

        // Em cima do destaque: branco ou quase preto, o que contrastar mais.
        // É o que deixa o amarelo e o verde claro com letra escura, e o
        // tema preto e branco com letra preta no botão branco.
        Se(TextoNoDestaque, Contraste(cores[Destaque], Colors.White) >= 4.5
            ? Colors.White
            : Misturar(cores[Destaque], Colors.Black, 0.85));

        // A seleção é o painel puxado para o destaque -- o bastante para
        // se ver, sem apagar o texto por cima.
        Se(Selecao, Misturar(cores[Painel], cores[Destaque], escura ? 0.35 : 0.22));
        Se(Hover, Misturar(cores[Painel], cores[Texto], escura ? 0.09 : 0.07));

        Se(Aviso, escura ? Cor("#F1C40F") : Cor("#8A5A00"));
        Se(Erro, escura ? Cor("#F48371") : Cor("#B42318"));
        Se(BordaDoAviso, escura ? Cor("#8A6D1A") : Cor("#D9B44A"));

        Se(Terminal, escura ? Cor("#9CDCFE") : Cor("#10518C"));
        Se(TerminalRede, escura ? Cor("#B5CEA8") : Cor("#2A6B2A"));
        Se(TerminalBt, escura ? Cor("#CE9178") : Cor("#9A4A1C"));

        return new Paleta(id, nome, grupo, escura, cores);
    }

    /// <summary>Mistura duas cores: 0 é só a primeira, 1 é só a segunda.</summary>
    public static Color Misturar(Color a, Color b, double quanto)
        => Color.FromRgb(
            (byte)Math.Round(a.R + (b.R - a.R) * quanto),
            (byte)Math.Round(a.G + (b.G - a.G) * quanto),
            (byte)Math.Round(a.B + (b.B - a.B) * quanto));

    /// <summary>A razão de contraste da WCAG entre duas cores (1 a 21).</summary>
    public static double Contraste(Color a, Color b)
    {
        double la = Luminancia(a), lb = Luminancia(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    static double Luminancia(Color c)
    {
        static double Canal(byte v)
        {
            double s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Canal(c.R) + 0.7152 * Canal(c.G) + 0.0722 * Canal(c.B);
    }
}
