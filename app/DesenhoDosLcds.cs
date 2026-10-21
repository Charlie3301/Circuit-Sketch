namespace Simulador.App;

// GERADO por ferramentas/extrair-svg/pecas-lcd.py -- nao edite a mao.
//
// Onde ficam os pontos de cada caractere dos LCDs (px da caixa): o editor
// pinta os acesos por cima do vidro desenhado no SVG.
static class DesenhoDosLcds
{
    /// <summary>A grade: colunas, linhas, o canto do primeiro caractere, o passo do caractere e o do ponto, o tamanho do ponto e o vidro.</summary>
    public sealed record Grade(int Colunas, int Linhas, double X, double Y, double PassoX, double PassoY,
                               double PontoPassoX, double PontoPassoY, double PontoLargura, double PontoAltura,
                               double VidroX, double VidroY, double VidroLargura, double VidroAltura);

    public static readonly Dictionary<string, Grade> Grades = new()
    {
        ["lcd1602"] = new(16, 2, 44.98, 46.30, 13.417, 22.488, 2.268, 2.646, 2.079, 2.457, 29.29, 37.04, 243.78, 61.98),
        ["lcd2004"] = new(20, 4, 52.16, 74.08, 13.417, 20.220, 2.268, 2.268, 2.079, 2.079, 41.57, 65.76, 287.24, 95.24),
    };

    static System.Windows.Media.Color Cor(int rgb) => System.Windows.Media.Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

    /// <summary>A cor do ponto aceso com a luz de fundo, sem ela, e a do vidro apagado, por modelo.</summary>
    public static readonly Dictionary<string, (System.Windows.Media.Color Aceso, System.Windows.Media.Color AcesoSemLuz, System.Windows.Media.Color VidroSemLuz)> Cores = new()
    {
        ["verde"] = (Cor(0x1c2a0a), Cor(0x26300f), Cor(0x7d8a2c)),
        ["azul"] = (Cor(0xeef4ff), Cor(0x3a4c86), Cor(0x0e1f4f)),
    };
}
