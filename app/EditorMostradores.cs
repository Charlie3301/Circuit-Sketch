using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Simulador.App;

/// <summary>
/// As saidas novas da Alpha 25 que acendem por cima do desenho:
///
///  - o TM1637 mostra os segmentos que a TM1637Display do sketch escreveu no
///    mostrador dos pinos CLK e DIO desta peca, no brilho do setBrightness();
///  - a matriz do MAX7219 mostra os pontos da LedControl dos pinos DIN, CLK e
///    CS -- a peca ligada no DOUT de outra e o aparelho seguinte da cadeia (o
///    0 e o que esta na placa, como na biblioteca);
///  - o semaforo acende cada LED pela corrente da conta (os resistores sao do
///    modulo).
///
/// Sem o VCC e o GND ligados, o TM1637 e a matriz nao acendem.
/// </summary>
public partial class EditorCircuito
{
    static readonly string[] CoresDoTm1637 = ["vermelho", "verde", "azul", "branco", "amarelo"];

    /// <summary>A cor do display do TM1637 (o modelo da peca): o vermelho e o de fabrica.</summary>
    public static string CorDoTm1637(PecaNoCircuito peca) => CoresDoTm1637.Contains(peca.Modelo) ? peca.Modelo : "vermelho";

    public static string[] OpcoesDeCorDoTm1637 => CoresDoTm1637;

    bool ModuloAlimentado(PecaNoCircuito peca, Netlist rede)
        => rede.AlcancaFonte(Atual, peca.Id, "VCC", terra: false) && rede.AlcancaFonte(Atual, peca.Id, "GND", terra: true);

    /// <summary>Desenha o que acende nas saidas novas; devolve false se a peca nao e uma delas.</summary>
    bool DesenharMostrador(PecaNoCircuito peca, PecaDef def, Bancada bancada, Netlist rede)
    {
        if (DesenhoDosMostradores.Segmentos.TryGetValue(peca.Tipo, out var segmentos))
            AcenderTm1637(peca, def, segmentos, MaquinaDaPeca(bancada, rede, peca), rede);
        else if (DesenhoDosMostradores.Pontos.TryGetValue(peca.Tipo, out var pontos))
            AcenderMatriz(peca, def, pontos.Centros, pontos.Raio, bancada, rede);
        else if (DesenhoDosMostradores.Luzes.TryGetValue(peca.Tipo, out var luzes))
            AcenderSemaforo(peca, def, luzes);
        else
            return false;
        return true;
    }

    // ---------------- o TM1637 ----------------

    void AcenderTm1637(PecaNoCircuito peca, PecaDef def, Dictionary<string, (double X, double Y)[]> segmentos, Maquina maq, Netlist rede)
    {
        var cor = CorDoLed(CorDoTm1637(peca));
        var luzes = LuzesDoLed(peca, segmentos.Count, () => new Path
        {
            Fill = new SolidColorBrush(cor),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = cor, BlurRadius = 6, ShadowDepth = 0, Opacity = 0.95 },
        });
        Tm1637 tm = null;
        if (maq is { Energizada: true } && !estouros.ContainsKey(peca.Id) && ModuloAlimentado(peca, rede))
        {
            int clk = rede.GpioDe(Atual, peca.Id, "CLK"), dio = rede.GpioDe(Atual, peca.Id, "DIO");
            lock (maq.Mostradores) maq.Mostradores.TryGetValue((clk, dio), out tm);
        }
        int i = 0;
        foreach (var (nome, contorno) in segmentos)
        {
            var luz = (Path)luzes[i++];
            bool aceso = tm is { Ligado: true }
                         && (nome == "COLON" ? (tm.Segmentos[1] & 0x80) != 0 : (tm.Segmentos[nome[1] - '1'] >> (nome[2] - 'A') & 1) != 0);
            double alvo = aceso ? 0.35 + 0.65 * (tm.Brilho + 1) / 8.0 : 0;
            if (alvo > 0)
            {
                if (luz.Fill is SolidColorBrush p && p.Color != cor)
                {
                    luz.Fill = new SolidColorBrush(cor);
                    if (luz.Effect is System.Windows.Media.Effects.DropShadowEffect s) s.Color = cor;
                }
                var figura = new PathFigure { IsClosed = true, IsFilled = true, StartPoint = Geometria.PosicaoLocal(peca, def, contorno[0].X, contorno[0].Y) };
                for (int k = 1; k < contorno.Length; k++)
                    figura.Segments.Add(new LineSegment(Geometria.PosicaoLocal(peca, def, contorno[k].X, contorno[k].Y), false));
                luz.Data = new PathGeometry([figura]);
            }
            if (Math.Abs(luz.Opacity - alvo) > 0.01) luz.Opacity = alvo;
        }
    }

    // ---------------- a matriz do MAX7219 ----------------

    /// <summary>A cadeia da LedControl que chega nesta peca, e qual aparelho dela a peca e.</summary>
    (Max7219 Cadeia, int Indice) MatrizDaPeca(PecaNoCircuito peca, Bancada bancada, Netlist rede, int profundidade = 0)
    {
        if (profundidade > 8) return (null, 0);
        int din = rede.GpioDe(Atual, peca.Id, "DIN");
        if (din >= 0)
        {
            if (MaquinaDaPeca(bancada, rede, peca) is not { Energizada: true } maq) return (null, 0);
            int clk = rede.GpioDe(Atual, peca.Id, "CLK"), cs = rede.GpioDe(Atual, peca.Id, "CS");
            lock (maq.Matrizes) return maq.Matrizes.TryGetValue((din, clk, cs), out var cadeia) ? (cadeia, 0) : (null, 0);
        }
        string raiz = rede.RaizDe(Netlist.No(peca.Id, "DIN"));
        if (raiz is null) return (null, 0);
        foreach (var outra in Atual.Pecas)
        {
            if (outra.Id == peca.Id || outra.Tipo != peca.Tipo || rede.RaizDe(Netlist.No(outra.Id, "DOUT")) != raiz) continue;
            var (cadeia, indice) = MatrizDaPeca(outra, bancada, rede, profundidade + 1);
            return (cadeia, indice + 1);
        }
        return (null, 0);
    }

    void AcenderMatriz(PecaNoCircuito peca, PecaDef def, (double X, double Y)[] centros, double raio, Bancada bancada, Netlist rede)
    {
        double lado = raio * 2.4;
        var vermelho = Color.FromRgb(0xFF, 0x2A, 0x1E);
        var luzes = LuzesDoLed(peca, centros.Length, () => new Ellipse
        {
            Width = lado, Height = lado,
            Fill = new RadialGradientBrush
            {
                GradientStops =
                [
                    new GradientStop(Color.FromArgb(0xFA, 0xFF, 0xD0, 0xC8), 0.0),
                    new GradientStop(Color.FromArgb(0xF0, vermelho.R, vermelho.G, vermelho.B), 0.35),
                    new GradientStop(Color.FromArgb(0x60, vermelho.R, vermelho.G, vermelho.B), 0.7),
                    new GradientStop(Color.FromArgb(0x00, vermelho.R, vermelho.G, vermelho.B), 1.0),
                ],
            },
        });
        var (cadeia, indice) = !estouros.ContainsKey(peca.Id) && ModuloAlimentado(peca, rede) ? MatrizDaPeca(peca, bancada, rede) : (null, 0);
        bool liga = cadeia is not null && indice < cadeia.Quantidade && !cadeia.Desligado[indice];
        double forca = liga ? 0.4 + 0.6 * (cadeia.Brilho[indice] + 1) / 16.0 : 0;
        for (int i = 0; i < centros.Length; i++)
        {
            var luz = (Ellipse)luzes[i];
            int linha = i / 8, coluna = i % 8;
            bool aceso = liga && linha <= cadeia.Varredura[indice] && (cadeia.Linhas[indice][linha] >> (7 - coluna) & 1) != 0;
            double alvo = aceso ? forca : 0;
            if (alvo > 0)
            {
                var p = Geometria.PosicaoLocal(peca, def, centros[i].X, centros[i].Y);
                Canvas.SetLeft(luz, p.X - lado / 2);
                Canvas.SetTop(luz, p.Y - lado / 2);
            }
            if (Math.Abs(luz.Opacity - alvo) > 0.01) luz.Opacity = alvo;
        }
    }

    // ---------------- o semaforo ----------------

    void AcenderSemaforo(PecaNoCircuito peca, PecaDef def, (double X, double Y, double Raio, string Cor, string Pino)[] luzesDef)
    {
        double maior = luzesDef.Max(l => l.Raio) * 3.6;
        var luzes = LuzesDoLed(peca, luzesDef.Length, () => new Ellipse { Width = maior, Height = maior });
        for (int i = 0; i < luzesDef.Length; i++)
        {
            var (x, y, raio, corTexto, pino) = luzesDef[i];
            var luz = (Ellipse)luzes[i];
            double corrente = estouros.ContainsKey(peca.Id) || EnergiaDoQuadro is not { } conta ? 0 : conta.CorrenteDoRamo(peca.Id, pino);
            // O LED de 8 mm com 2 a 9 mA ja brilha bem: o minimo aceso e mais alto que o do segmento.
            double alvo = corrente <= 0 ? 0 : 0.6 + 0.4 * Luminancia(BrilhoPelaCorrente(corrente) / 255.0);
            if (alvo > 0)
            {
                if (luz.Tag is not string antes || antes != corTexto)
                {
                    luz.Tag = corTexto;
                    var cor = (Color)ColorConverter.ConvertFromString(corTexto);
                    var claro = MisturarBranco(cor, 0.6);
                    luz.Fill = new RadialGradientBrush
                    {
                        GradientStops =
                        [
                            new GradientStop(Color.FromArgb(0xFF, 0xFF, 0xFF, 0xF4), 0.0),
                            new GradientStop(Color.FromArgb(0xF8, claro.R, claro.G, claro.B), 0.16),
                            new GradientStop(Color.FromArgb(0xE8, cor.R, cor.G, cor.B), 0.34),
                            new GradientStop(Color.FromArgb(0x70, cor.R, cor.G, cor.B), 0.56),
                            new GradientStop(Color.FromArgb(0x00, cor.R, cor.G, cor.B), 1.0),
                        ],
                    };
                }
                var p = Geometria.PosicaoLocal(peca, def, x, y);
                Canvas.SetLeft(luz, p.X - maior / 2);
                Canvas.SetTop(luz, p.Y - maior / 2);
            }
            if (Math.Abs(luz.Opacity - alvo) > 0.01) luz.Opacity = alvo;
        }
    }
}
