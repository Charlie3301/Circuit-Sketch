using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Simulador.App;

/// <summary>
/// O que acende nas pecas de LED com varios pontos de luz (Alpha 25):
///
///  - o display de 7 segmentos e a barra de 10 LEDs acendem o SEGMENTO, no
///    desenho dele (antes acendia um ponto no pino). O display serve de
///    catodo ou de anodo comum: o segmento acende com a corrente no sentido
///    que o comum deixar;
///  - o NeoPixel, o anel e a matriz mostram a cor de cada pixel da fita do
///    sketch (antes nao mostravam nada). A peca ligada no DOUT de outra
///    continua a fita de onde a outra parou, como na de verdade; sem o VCC
///    e o GND ligados, nao acende.
///
/// A geometria (o contorno de cada segmento, o centro de cada pixel) vem de
/// <see cref="DesenhoDosLeds"/>, gerado junto com os desenhos.
/// </summary>
public partial class EditorCircuito
{
    readonly Dictionary<string, List<Shape>> luzesDeLed = [];

    void ApagarLuzesDeLed()
    {
        foreach (var luzes in luzesDeLed.Values)
            foreach (var l in luzes) mundo.Children.Remove(l);
        luzesDeLed.Clear();
    }

    List<Shape> LuzesDoLed(PecaNoCircuito peca, int quantas, Func<Shape> nova)
    {
        if (luzesDeLed.TryGetValue(peca.Id, out var luzes) && luzes.Count == quantas) return luzes;
        if (luzes is not null)
            foreach (var l in luzes) mundo.Children.Remove(l);
        luzes = [];
        for (int i = 0; i < quantas; i++)
        {
            var l = nova();
            l.IsHitTestVisible = false;
            l.Opacity = 0;
            Panel.SetZIndex(l, 155);
            mundo.Children.Add(l);
            luzes.Add(l);
        }
        luzesDeLed[peca.Id] = luzes;
        return luzes;
    }

    void DesenharLuzesDeLed(Bancada bancada, Netlist rede)
    {
        bool bancadaLigada = bancada.Todas.Any(x => x.Value.Energizada);
        foreach (var peca in Atual.Pecas)
        {
            if (Catalogo.Obter(peca.Tipo) is not { } def) continue;
            if (DesenhoDosLeds.Segmentos.TryGetValue(peca.Tipo, out var segmentos))
                AcenderSegmentos(peca, def, segmentos, MaquinaDaPeca(bancada, rede, peca), rede, bancadaLigada);
            else if (DesenhoDosLeds.Pixels.TryGetValue(peca.Tipo, out var pixels))
                AcenderPixels(peca, def, pixels.Centros, pixels.Raio, bancada, rede);
        }
    }

    // ---------------- os segmentos ----------------

    /// <summary>A forca (0 a 255) de um segmento agora.</summary>
    int ForcaDoSegmento(PecaNoCircuito peca, string nome, Maquina maq, Netlist rede, bool bancadaLigada)
    {
        if (estouros.ContainsKey(peca.Id)) return 0;
        int forca;
        string ramo;
        if (peca.Tipo == "7segment")
        {
            // catodo comum (o segmento e o anodo) ou anodo comum (ao contrario)
            forca = Math.Max(BrilhoEntre(peca, nome, "COM.1", maq, rede, bancadaLigada),
                             BrilhoEntre(peca, "COM.1", nome, maq, rede, bancadaLigada));
            ramo = nome;
        }
        else
        {
            string k = nome[1..];
            forca = BrilhoEntre(peca, nome, "C" + k, maq, rede, bancadaLigada);
            ramo = k;
        }
        // Sem placa mandando (uma bateria, uma chave): pela corrente da conta.
        if (forca == 0 && EnergiaDoQuadro is { } conta)
            forca = BrilhoPelaCorrente(conta.CorrenteDoRamo(peca.Id, ramo));
        return forca;
    }

    void AcenderSegmentos(PecaNoCircuito peca, PecaDef def, Dictionary<string, (double X, double Y)[]> segmentos,
                          Maquina maq, Netlist rede, bool bancadaLigada)
    {
        var cor = CorDoLed(peca.Cor);
        var luzes = LuzesDoLed(peca, segmentos.Count, () => new Path
        {
            Fill = new SolidColorBrush(cor),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = cor, BlurRadius = 8, ShadowDepth = 0, Opacity = 0.95 },
        });
        int i = 0;
        foreach (var (nome, contorno) in segmentos)
        {
            var luz = (Path)luzes[i++];
            double forca = ForcaDoSegmento(peca, nome, maq, rede, bancadaLigada) / 255.0;
            double alvo = forca <= 0 ? 0 : 0.35 + 0.65 * Luminancia(forca);
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

    // ---------------- os pixels ----------------

    /// <summary>
    /// A fita do sketch que chega nesta peca, e a partir de que LED: o DIN
    /// num GPIO com fita e o comeco dela; o DIN no DOUT de outra peca de LED
    /// continua depois dos LEDs daquela.
    /// </summary>
    (Fita Fita, int Inicio) FitaDoLed(Bancada bancada, Netlist rede, PecaNoCircuito peca, int profundidade = 0)
    {
        if (profundidade > 32) return (null, 0);
        string raiz = rede.RaizDe(Netlist.No(peca.Id, "DIN"));
        if (raiz is null) return (null, 0);
        foreach (var placa in Atual.Pecas)
        {
            if (PerfilPlaca.De(placa.Tipo) is not { } perfil || bancada.De(placa.Id) is not { } maq || maq.Fitas.Count == 0) continue;
            foreach (var p in Catalogo.Obter(placa.Tipo).Pinos)
            {
                int gpio = perfil.Numero(p.Nome);
                if (gpio >= 0 && maq.Fitas.TryGetValue(gpio, out var fita) && rede.RaizDe(Netlist.No(placa.Id, p.Nome)) == raiz)
                    return (maq.Energizada ? fita : null, 0);
            }
        }
        foreach (var outra in Atual.Pecas)
        {
            if (outra.Id == peca.Id || !DesenhoDosLeds.Pixels.TryGetValue(outra.Tipo, out var dela)) continue;
            if (rede.RaizDe(Netlist.No(outra.Id, "DOUT")) != raiz) continue;
            var (fitaDela, inicio) = FitaDoLed(bancada, rede, outra, profundidade + 1);
            return (fitaDela, inicio + dela.Centros.Length);
        }
        return (null, 0);
    }

    /// <summary>A peca de LED enderecavel esta alimentada (o VCC/VDD e o GND/VSS ligados)?</summary>
    bool PixelsAlimentados(PecaNoCircuito peca, Netlist rede)
    {
        string vcc = peca.Tipo == "neopixel" ? "VDD" : "VCC", gnd = peca.Tipo == "neopixel" ? "VSS" : "GND";
        return rede.AlcancaFonte(Atual, peca.Id, vcc, terra: false) && rede.AlcancaFonte(Atual, peca.Id, gnd, terra: true);
    }

    void AcenderPixels(PecaNoCircuito peca, PecaDef def, (double X, double Y)[] centros, double raio, Bancada bancada, Netlist rede)
    {
        double lado = raio * 2.2;
        var luzes = LuzesDoLed(peca, centros.Length, () => new Ellipse { Width = lado, Height = lado });
        var (fita, inicio) = PixelsAlimentados(peca, rede) && !estouros.ContainsKey(peca.Id) ? FitaDoLed(bancada, rede, peca) : (null, 0);
        for (int i = 0; i < centros.Length; i++)
        {
            var luz = (Ellipse)luzes[i];
            int k = inicio + i;
            int c = fita is not null && k < fita.Cores.Length ? fita.Cores[k] : 0;
            byte r = (byte)((c >> 16) & 0xFF), g = (byte)((c >> 8) & 0xFF), b = (byte)(c & 0xFF);
            int maior = Math.Max(r, Math.Max(g, b));
            double alvo = maior == 0 ? 0 : 0.96 * Luminancia(maior / 255.0 * (fita?.Brilho ?? 255) / 255.0);
            if (alvo > 0)
            {
                var cor = Color.FromRgb((byte)(r * 255 / maior), (byte)(g * 255 / maior), (byte)(b * 255 / maior));
                if (luz.Tag is not Color antes || antes != cor)
                {
                    luz.Tag = cor;
                    luz.Fill = new RadialGradientBrush
                    {
                        GradientStops =
                        [
                            new GradientStop(Color.FromArgb(0xFA, MisturarBranco(cor, 0.75).R, MisturarBranco(cor, 0.75).G, MisturarBranco(cor, 0.75).B), 0.0),
                            new GradientStop(Color.FromArgb(0xF0, MisturarBranco(cor, 0.25).R, MisturarBranco(cor, 0.25).G, MisturarBranco(cor, 0.25).B), 0.18),
                            new GradientStop(Color.FromArgb(0xC8, cor.R, cor.G, cor.B), 0.42),
                            new GradientStop(Color.FromArgb(0x40, cor.R, cor.G, cor.B), 0.7),
                            new GradientStop(Color.FromArgb(0x00, cor.R, cor.G, cor.B), 1.0),
                        ],
                    };
                }
                var p = Geometria.PosicaoLocal(peca, def, centros[i].X, centros[i].Y);
                Canvas.SetLeft(luz, p.X - lado / 2);
                Canvas.SetTop(luz, p.Y - lado / 2);
            }
            if (Math.Abs(luz.Opacity - alvo) > 0.01) luz.Opacity = alvo;
        }
    }

    /// <summary>Para o teste: a forca de cada luz da peca (segmentos ou pixels), na ordem da tabela.</summary>
    internal double[] LuzesDoLedParaTeste(string idDaPeca)
        => luzesDeLed.TryGetValue(idDaPeca, out var luzes) ? [.. luzes.Select(l => l.Opacity)] : [];

    /// <summary>Para o teste: a cor de cada pixel aceso (transparente se apagado).</summary>
    internal Color[] CoresDosPixelsParaTeste(string idDaPeca)
        => luzesDeLed.TryGetValue(idDaPeca, out var luzes)
            ? [.. luzes.Select(l => l.Opacity > 0 && l.Tag is Color c ? c : Colors.Transparent)] : [];
}
