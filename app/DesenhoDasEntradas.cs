// Gerado por ferramentas/extrair-svg/pecas-entradas.py -- nao edite a mao.
namespace Simulador.App;

/// <summary>
/// As medidas das entradas da Alpha 25 que o editor usa, em px da caixa de
/// cada peca: a capa que se aperta, o eixo do que gira, o curso do que
/// desliza, as chaves do DIP, as teclas do teclado e os furos do disco.
/// Saem do mesmo gerador dos desenhos: o clique cai onde a pessoa ve.
/// </summary>
static class DesenhoDasEntradas
{
    public static readonly (double X, double Y, double R) CapaPushbutton = (26.50, 24.00, 17.76);
    public static readonly (double X, double Y, double R) CapaPushbutton6mm = (16.50, 12.50, 9.83);
    /// <summary>O eixo do knob do potenciometro, em fracao da caixa.</summary>
    public const double PotEixoX = 0.5000;
    public const double PotEixoY = 0.3868;
    /// <summary>O curso do knob do deslizante (30 mm), em px.</summary>
    public const double DeslizanteCurso = 113.3858;
    public static readonly (double X, double Y, double R) CapaChaveDeslizante = (17.50, 9.83, 9.07);
    /// <summary>O curso da alavanca da chave deslizante (1,9 mm), em px.</summary>
    public const double ChaveDeslizanteCurso = 7.1811;
    /// <summary>As chaves do DIP: a borda do primeiro rasgo, o passo, o rasgo e a alavanca (px).</summary>
    public const double DipPrimeira = 5.0654;
    public const double DipPasso = 9.6000;
    public const double DipLargura = 5.6693;
    public const double DipTopo = 10.2402;
    public const double DipAltura = 17.7638;
    public const double DipAlavanca = 7.9370;
    public const double DipFolga = 0.7559;
    public const string DipCorDoRasgo = "#2b2527";
    /// <summary>O centro de cada tecla do teclado 4 x 4 (px) e meia tecla.</summary>
    public static readonly (double X, double Y, string Tecla)[] Teclas =
    [
        (43.06, 52.35, "1"),
        (102.02, 52.35, "2"),
        (160.98, 52.35, "3"),
        (219.94, 52.35, "A"),
        (43.06, 113.95, "4"),
        (102.02, 113.95, "5"),
        (160.98, 113.95, "6"),
        (219.94, 113.95, "B"),
        (43.06, 175.56, "7"),
        (102.02, 175.56, "8"),
        (160.98, 175.56, "9"),
        (219.94, 175.56, "C"),
        (43.06, 237.17, "*"),
        (102.02, 237.17, "0"),
        (160.98, 237.17, "#"),
        (219.94, 237.17, "D"),
    ];
    public const double MeiaTecla = 24.1890;
    public static readonly (double X, double Y, double R) CapaJoystick = (50.00, 53.67, 37.80);
    /// <summary>O centro do manche do joystick (fracao da caixa) e o curso de ponta a ponta (6 mm, em px).</summary>
    public const double JoystickEixoX = 0.5000;
    public const double JoystickEixoY = 0.3508;
    public const double JoystickCurso = 22.6772;
    public static readonly (double X, double Y, double R) CapaEncoder = (36.66, 29.67, 13.61);
    /// <summary>O eixo do encoder, em fracao da caixa.</summary>
    public const double EncoderEixoX = 0.2981;
    public const double EncoderEixoY = 0.4121;
    /// <summary>Os dez furos do disco (px): o "1" no batente, cada um 30 graus antes.</summary>
    public static readonly (double X, double Y, int Numero)[] Furos =
    [
        (229.01, 125.40, 1),
        (211.95, 78.52, 2),
        (173.73, 46.45, 3),
        (124.60, 37.78, 4),
        (77.72, 54.85, 5),
        (45.65, 93.06, 6),
        (36.99, 142.20, 7),
        (54.05, 189.08, 8),
        (92.27, 221.14, 9),
        (141.40, 229.81, 0),
    ];
    public const double RaioDoFuro = 21.1654;
    /// <summary>O eixo do disco, em fracao da caixa.</summary>
    public const double DiscoEixoX = 0.5000;
    public const double DiscoEixoY = 0.4566;
    public static readonly (double X, double Y, double R) CapaFimDeCurso = (40.65, 23.06, 22.68);
    /// <summary>A dobradica da alavanca do fim de curso (fracao da caixa) e quanto ela gira apertada (graus).</summary>
    public const double FimDeCursoEixoX = 0.0972;
    public const double FimDeCursoEixoY = 0.3758;
    public const double FimDeCursoGiro = 8.7152;
}
