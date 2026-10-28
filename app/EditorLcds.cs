using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Simulador.App;

/// <summary>
/// Os LCDs de caracteres (Alpha 25): a matriz de pontos 5 x 8 de cada
/// caractere, como no vidro de verdade -- o desenho tem os pontos apagados, e
/// aqui se pintam os acesos por cima. Ate a Alpha 24 eram letras da fonte
/// Consolas, e o createChar() nao aparecia.
///
/// A fonte e a da ROM A00 do HD44780: a mesma 5 x 7 da Adafruit_GFX nos
/// codigos de 32 a 125, com o que o chip troca (o 92 e o iene, o 126 e o 127
/// sao setas) e os simbolos que os sketches usam de cima (o 223, o grau).
/// Os codigos 0 a 7 (e 8 a 15) sao os do createChar().
/// </summary>
public partial class EditorCircuito
{
    sealed class TelaLcd
    {
        public Path Pontos;
        public Rectangle Sombra;
        public string Estado = "";
    }

    public static bool EhLcd(string tipo) => tipo is "lcd1602" or "lcd2004" or "lcd1602-i2c" or "lcd2004-i2c";

    static DesenhoDosLcds.Grade GradeDoLcd(string tipo)
        => EhLcd(tipo) ? DesenhoDosLcds.Grades[tipo.EndsWith("-i2c", StringComparison.Ordinal) ? tipo[..^4] : tipo] : null;

    /// <summary>A cor do vidro: o verde (letras escuras, o de fabrica) ou o azul (letras brancas).</summary>
    public static string CorDoLcd(PecaNoCircuito peca) => peca.Modelo == "azul" ? "azul" : "verde";

    /// <summary>Os simbolos da ROM A00 que nao sao os da fonte classica (oito linhas de 5 pontos, o bit 4 a esquerda).</summary>
    static readonly Dictionary<int, byte[]> SimbolosDoHd44780 = new()
    {
        [0x5C] = [0x11, 0x0A, 0x1F, 0x04, 0x1F, 0x04, 0x04, 0x00],   // o iene, no lugar da barra invertida
        [0x7E] = [0x00, 0x04, 0x02, 0x1F, 0x02, 0x04, 0x00, 0x00],   // a seta para a direita
        [0x7F] = [0x00, 0x04, 0x08, 0x1F, 0x08, 0x04, 0x00, 0x00],   // a seta para a esquerda
        [0xDF] = [0x1C, 0x14, 0x1C, 0x00, 0x00, 0x00, 0x00, 0x00],   // o grau (o "(char)223" dos termometros)
        [0xE4] = [0x00, 0x00, 0x11, 0x11, 0x11, 0x13, 0x1D, 0x10],   // o micro
        [0xF4] = [0x00, 0x0E, 0x11, 0x11, 0x11, 0x0A, 0x1B, 0x00],   // o ohm
        [0xFF] = [0x1F, 0x1F, 0x1F, 0x1F, 0x1F, 0x1F, 0x1F, 0x1F],   // o bloco cheio
        [0xB0] = [0x1C, 0x14, 0x1C, 0x00, 0x00, 0x00, 0x00, 0x00],   // o "°" escrito no texto: mostra o grau
    };

    /// <summary>As oito linhas (5 pontos cada, o bit 4 a esquerda) do caractere deste codigo.</summary>
    static void LinhasDoCaractere(char ch, Lcd lcd, Span<byte> linhas)
    {
        int c = ch;
        if (c < 16)
        {
            lcd.Glifos[c & 7].AsSpan().CopyTo(linhas);
            return;
        }
        if (SimbolosDoHd44780.TryGetValue(c, out var simbolo))
        {
            simbolo.AsSpan().CopyTo(linhas);
            return;
        }
        var colunas = TelaGrafica.ColunasDaLetra(c);
        for (int r = 0; r < 8; r++)
        {
            int linha = 0;
            for (int i = 0; i < 5; i++)
                if (r < 7 && (colunas[i] >> r & 1) != 0) linha |= 1 << (4 - i);
            linhas[r] = (byte)linha;
        }
    }

    void CriarTelaLcd(PecaNoCircuito peca, DesenhoDosLcds.Grade g)
    {
        var tela = new TelaLcd
        {
            Pontos = new Path { IsHitTestVisible = false },
            Sombra = new Rectangle
            {
                Width = g.VidroLargura, Height = g.VidroAltura, RadiusX = 1, RadiusY = 1,
                Fill = new SolidColorBrush(DesenhoDosLcds.Cores[CorDoLcd(peca)].VidroSemLuz), Opacity = 0.62,
                Visibility = System.Windows.Visibility.Collapsed, IsHitTestVisible = false,
            },
        };
        PorNaCamadaDa(peca, tela.Sombra);
        PorNaCamadaDa(peca, tela.Pontos);
        telasLcd[peca.Id] = tela;
        PosicionarTelaLcd(peca);
    }

    void PosicionarTelaLcd(PecaNoCircuito peca)
    {
        if (!telasLcd.TryGetValue(peca.Id, out var tela) || GradeDoLcd(peca.Tipo) is not { } g) return;
        Canvas.SetLeft(tela.Pontos, peca.X);
        Canvas.SetTop(tela.Pontos, peca.Y);
        Canvas.SetLeft(tela.Sombra, peca.X + g.VidroX);
        Canvas.SetTop(tela.Sombra, peca.Y + g.VidroY);
    }

    void RemoverTelaLcd(TelaLcd tela)
    {
        mundo.Children.Remove(tela.Pontos);
        mundo.Children.Remove(tela.Sombra);
    }

    /// <summary>
    /// Os pontos acesos: so redesenha quando algo mudou no display (o texto, um
    /// caractere do createChar, a luz de fundo) ou na peca (a cor).
    /// </summary>
    void AtualizarTelaLcd(PecaNoCircuito peca, Lcd lcd, bool recebe)
    {
        if (!telasLcd.TryGetValue(peca.Id, out var tela) || GradeDoLcd(peca.Tipo) is not { } g) return;
        string cor = CorDoLcd(peca);
        bool mostra = recebe && lcd is { Ligado: true, Mostrando: true };
        bool luz = lcd is null || lcd.LuzDeFundo;
        string estado = $"{(mostra ? lcd.Versao : -1)}|{cor}|{luz}";
        if (estado == tela.Estado) return;
        tela.Estado = estado;

        var cores = DesenhoDosLcds.Cores[cor];
        tela.Sombra.Fill = new SolidColorBrush(cores.VidroSemLuz);
        tela.Sombra.Visibility = luz ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
        if (!mostra)
        {
            tela.Pontos.Data = null;
            return;
        }

        var geometria = new StreamGeometry();
        Span<byte> linhas = stackalloc byte[8];
        using (var ctx = geometria.Open())
        {
            for (int lin = 0; lin < g.Linhas && lin < lcd.Linhas; lin++)
            {
                for (int col = 0; col < g.Colunas && col < lcd.Colunas; col++)
                {
                    char ch = lcd.Tela[lin, col];
                    if (ch == ' ') continue;
                    LinhasDoCaractere(ch, lcd, linhas);
                    double x0 = g.X + col * g.PassoX, y0 = g.Y + lin * g.PassoY;
                    for (int r = 0; r < 8; r++)
                    {
                        if (linhas[r] == 0) continue;
                        for (int i = 0; i < 5; i++)
                        {
                            if ((linhas[r] >> (4 - i) & 1) == 0) continue;
                            double x = x0 + i * g.PontoPassoX, y = y0 + r * g.PontoPassoY;
                            ctx.BeginFigure(new System.Windows.Point(x, y), true, true);
                            ctx.PolyLineTo([new(x + g.PontoLargura, y), new(x + g.PontoLargura, y + g.PontoAltura), new(x, y + g.PontoAltura)], false, false);
                        }
                    }
                }
            }
        }
        geometria.Freeze();
        tela.Pontos.Data = geometria;
        tela.Pontos.Fill = new SolidColorBrush(luz ? cores.Aceso : cores.AcesoSemLuz);
    }

    /// <summary>Para o teste: quantos pontos acesos o LCD desta peca mostra agora.</summary>
    internal int PontosAcesosDoLcdParaTeste(string idDaPeca)
    {
        if (!telasLcd.TryGetValue(idDaPeca, out var tela) || tela.Pontos.Data is not StreamGeometry geo) return 0;
        int n = 0;
        var caminho = geo.GetFlattenedPathGeometry();
        foreach (var _ in caminho.Figures) n++;
        return n;
    }

    /// <summary>Para o teste: o vidro esta escurecido (sem luz de fundo)?</summary>
    internal bool LcdSemLuzParaTeste(string idDaPeca)
        => telasLcd.TryGetValue(idDaPeca, out var tela) && tela.Sombra.Visibility == System.Windows.Visibility.Visible;
}
