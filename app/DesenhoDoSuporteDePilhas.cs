using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Simulador.App;

/// <summary>
/// O suporte de pilhas desenhado como o de verdade (Alpha 25): a caixa de
/// plastico preto com as divisorias, em cada slot a mola do lado do "-" e a
/// plaquinha do lado do "+", e a pilha deitada -- o cilindro com o rotulo
/// (a alcalina azul, a NiMH verde, as 18650), o botao do "+" e o fundo de
/// aco do "-", alternando o sentido de um slot para o outro (em serie). O
/// slot vazio mostra a mola, a placa e o + e o - moldados no fundo. O
/// CR2032 e a caixinha com a moeda. Os fios saem pela direita.
///
/// O leiaute (os slots, a saida dos fios, os pinos) e o do catalogo
/// (Baterias.LeiauteDe): o desenho nao tira nada do lugar.
/// </summary>
public partial class EditorCircuito
{
    FrameworkElement DesenharSuporteDePilhas(PecaNoCircuito peca, PecaDef def)
    {
        var desenho = DesenhoDoSuporteDePilhas(peca);
        var imagem = new Image { Source = new DrawingImage(desenho), Width = desenho.Bounds.Width, Height = desenho.Bounds.Height, Stretch = Stretch.Fill };
        imagem.Source.Freeze();
        var caixa = new Canvas { Width = def.Largura, Height = def.Altura };
        Canvas.SetLeft(imagem, desenho.Bounds.Left);
        Canvas.SetTop(imagem, desenho.Bounds.Top);
        caixa.Children.Add(imagem);
        return caixa;
    }

    /// <summary>As cores do rotulo de cada pilha: o corpo, a faixa do "+" e a letra.</summary>
    static (Color Corpo, Color Faixa, Color Letra, string Rotulo) RotuloDaPilha(string formato, Baterias.Pilha pilha) => (formato, pilha.Id) switch
    {
        ("aa" or "aaa", "nimh") => (Color.FromRgb(0x1E, 0x8A, 0x4C), Color.FromRgb(0xF1, 0xF3, 0xF2), Colors.White,
                                    $"Ni-MH  {pilha.MiliAmperesHora:0}mAh  1.2V"),
        ("aa" or "aaa", _) => (Color.FromRgb(0x1D, 0x3E, 0x8C), Color.FromRgb(0xC9, 0xCF, 0xD6), Colors.White,
                               $"ALCALINA  {formato.ToUpperInvariant()}  1.5V"),
        ("18650", "alta-descarga") => (Color.FromRgb(0xA3, 0x22, 0x3A), Color.FromRgb(0x2A, 0x2B, 0x2F), Colors.White, "18650  3000mAh  15A"),
        ("18650", _) => (Color.FromRgb(0x2A, 0x62, 0xC9), Color.FromRgb(0x1F, 0x2B, 0x44), Colors.White, "18650  2600mAh  3.7V"),
        _ => (Color.FromRgb(0xC4, 0xC9, 0xCF), Color.FromRgb(0xC4, 0xC9, 0xCF), Color.FromRgb(0x3A, 0x3E, 0x44), "CR2032"),
    };

    static DrawingGroup DesenhoDoSuporteDePilhas(PecaNoCircuito peca)
    {
        var (formato, _) = Baterias.SuporteDe(peca.Tipo)!.Value;
        var l = Baterias.LeiauteDe(peca.Tipo);
        var pilha = Baterias.PilhaDe(peca);
        double mm = 96 / 25.4;
        static SolidColorBrush Pincel(Color c) { var p = new SolidColorBrush(c); p.Freeze(); return p; }
        static Pen Caneta(Color c, double grossura) { var p = new Pen(Pincel(c), grossura) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }; p.Freeze(); return p; }
        var letra = new Typeface(new FontFamily("Arial"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
        var plastico = Color.FromRgb(0x1C, 0x1D, 0x20);
        var metal = new LinearGradientBrush(Color.FromRgb(0xEE, 0xF1, 0xF4), Color.FromRgb(0x8C, 0x93, 0x9B), 90);
        metal.Freeze();

        var desenho = new DrawingGroup();
        using (var dc = desenho.Open())
        {
            void Texto(string t, double tamanho, Color cor, Point centro, double giro = 0)
            {
                var ft = new FormattedText(t, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, letra, tamanho, Pincel(cor), 1.0);
                if (giro != 0) dc.PushTransform(new RotateTransform(giro, centro.X, centro.Y));
                dc.DrawText(ft, new Point(centro.X - ft.Width / 2, centro.Y - ft.Height / 2));
                if (giro != 0) dc.Pop();
            }

            // ---- os fios, por baixo da caixa: saem pela direita ----
            void Fio(Point de, Point ate, Color cor, Color escuro)
            {
                var g = new StreamGeometry();
                using (var c = g.Open())
                {
                    double meio = (de.X + ate.X) / 2 - 2.0;
                    c.BeginFigure(new Point(de.X - 2, de.Y), false, false);
                    c.BezierTo(new Point(meio, de.Y), new Point(meio, ate.Y), new Point(ate.X - 2.6 * mm, ate.Y), true, true);
                }
                g.Freeze();
                double grossura = 1.25 * mm;
                dc.DrawGeometry(null, Caneta(escuro, grossura), g);
                dc.DrawGeometry(null, Caneta(cor, grossura * 0.78), g);
                dc.PushTransform(new TranslateTransform(0, -grossura * 0.2));
                dc.DrawGeometry(null, Caneta(Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF), grossura * 0.22), g);
                dc.Pop();
                // a ponta estanhada
                dc.DrawRoundedRectangle(metal, Caneta(Color.FromRgb(0x6F, 0x75, 0x7D), 0.12 * mm),
                    new Rect(ate.X - 3.0 * mm, ate.Y - 0.38 * mm, 3.0 * mm, 0.76 * mm), 0.38 * mm, 0.38 * mm);
            }
            Fio(l.SaidaDoMais, l.Mais, Color.FromRgb(0xD0, 0x31, 0x2D), Color.FromRgb(0x7D, 0x17, 0x14));
            Fio(l.SaidaDoMenos, l.Menos, Color.FromRgb(0x2A, 0x2B, 0x2E), Color.FromRgb(0x0B, 0x0B, 0x0C));

            // ---- a caixa ----
            var corpo = new LinearGradientBrush(Color.FromRgb(0x2A, 0x2B, 0x2F), Color.FromRgb(0x16, 0x17, 0x19), 90);
            corpo.Freeze();
            dc.DrawRoundedRectangle(corpo, Caneta(Color.FromRgb(0x06, 0x06, 0x07), 0.3 * mm), l.Caixa, 1.4 * mm, 1.4 * mm);
            dc.DrawLine(Caneta(Color.FromRgb(0x44, 0x46, 0x4C), 0.3 * mm),
                        new Point(l.Caixa.Left + 1.6 * mm, l.Caixa.Top + 0.45 * mm), new Point(l.Caixa.Right - 1.6 * mm, l.Caixa.Top + 0.45 * mm));

            var (cCorpo, cFaixa, cLetra, rotulo) = RotuloDaPilha(formato.Id, pilha);

            if (formato.Id == "cr2032")
            {
                var furo = l.Slots[0];
                var centro = new Point(furo.X + furo.Width / 2, furo.Y + furo.Height / 2);
                double r = furo.Width / 2;
                dc.DrawEllipse(Pincel(Color.FromRgb(0x0E, 0x0F, 0x11)), Caneta(Color.FromRgb(0x05, 0x05, 0x06), 0.25 * mm), centro, r + 0.5 * mm, r + 0.5 * mm);
                if (Baterias.TemPilha(peca, 0))
                {
                    var moeda = new RadialGradientBrush(Color.FromRgb(0xF4, 0xF6, 0xF8), Color.FromRgb(0x9A, 0xA1, 0xA9))
                    {
                        GradientOrigin = new Point(0.35, 0.3), Center = new Point(0.45, 0.42), RadiusX = 0.7, RadiusY = 0.7,
                    };
                    moeda.Freeze();
                    dc.DrawEllipse(moeda, Caneta(Color.FromRgb(0x6A, 0x71, 0x79), 0.2 * mm), centro, r, r);
                    dc.DrawEllipse(null, Caneta(Color.FromRgb(0xB5, 0xBB, 0xC2), 0.25 * mm), centro, r - 1.0 * mm, r - 1.0 * mm);
                    Texto("+", 4.2 * mm, Color.FromRgb(0x4A, 0x4F, 0x56), new Point(centro.X, centro.Y - 4.6 * mm));
                    Texto("CR2032", 2.9 * mm, Color.FromRgb(0x3A, 0x3E, 0x44), new Point(centro.X, centro.Y + 0.6 * mm));
                    Texto("3V  LÍTIO", 1.8 * mm, Color.FromRgb(0x5A, 0x5F, 0x66), new Point(centro.X, centro.Y + 4.0 * mm));
                }
                else
                {
                    // sem a moeda: o fundo e a lingueta de mola do "-"
                    dc.DrawRoundedRectangle(metal, Caneta(Color.FromRgb(0x5A, 0x60, 0x68), 0.15 * mm),
                        new Rect(centro.X - 2.5 * mm, centro.Y - 4.0 * mm, 5.0 * mm, 8.0 * mm), 1.0 * mm, 1.0 * mm);
                    Texto("−", 3.0 * mm, Color.FromRgb(0x5A, 0x5D, 0x63), new Point(centro.X, centro.Y + 6.6 * mm));
                }
                // a lingueta do "+" que segura a moeda pela borda
                dc.DrawRoundedRectangle(metal, Caneta(Color.FromRgb(0x5A, 0x60, 0x68), 0.15 * mm),
                    new Rect(furo.Right - 1.2 * mm, centro.Y - 3.0 * mm, 3.2 * mm, 6.0 * mm), 0.6 * mm, 0.6 * mm);
            }
            else
            {
                double diametro = formato.Diametro * mm, comprimento = formato.Comprimento * mm;
                double mola = 3.6 * mm;                       // o espaco da mola, do lado do "-"
                for (int k = 0; k < l.Slots.Length; k++)
                {
                    var s = l.Slots[k];
                    bool maisNaDireita = l.MaisNaDireita[k];
                    double cy = s.Y + s.Height / 2;
                    // o fundo do slot, com o canal redondo
                    dc.DrawRoundedRectangle(Pincel(Color.FromRgb(0x10, 0x11, 0x13)), null,
                        new Rect(s.X, s.Y + 0.35 * mm, s.Width, s.Height - 0.7 * mm), 0.8 * mm, 0.8 * mm);
                    // as pontas: a mola (lado do "-") e a placa (lado do "+")
                    double xMola = maisNaDireita ? s.X : s.Right - mola;
                    double xPlaca = maisNaDireita ? s.Right - 0.9 * mm : s.X + 0.3 * mm;
                    var arame = Caneta(Color.FromRgb(0xB9, 0xBF, 0xC6), 0.42 * mm);
                    for (int e = 0; e < 4; e++)
                    {
                        double x = xMola + 0.5 * mm + e * (mola - 1.0 * mm) / 3;
                        dc.DrawEllipse(null, arame, new Point(x, cy), 0.45 * mm, diametro * 0.3);
                    }
                    dc.DrawRoundedRectangle(metal, Caneta(Color.FromRgb(0x5A, 0x60, 0x68), 0.12 * mm),
                        new Rect(xPlaca, cy - diametro * 0.32, 0.6 * mm, diametro * 0.64), 0.2 * mm, 0.2 * mm);

                    if (!Baterias.TemPilha(peca, k))
                    {
                        // vazio: o + e o - moldados no fundo
                        var molde = Color.FromRgb(0x3A, 0x3C, 0x42);
                        double tam = Math.Min(4.2 * mm, s.Height * 0.42);
                        Texto("+", tam, molde, new Point(maisNaDireita ? s.Right - 4.5 * mm : s.X + 4.5 * mm, cy));
                        Texto("−", tam, molde, new Point(maisNaDireita ? s.X + 6.0 * mm : s.Right - 6.0 * mm, cy));
                        continue;
                    }

                    // ---- a pilha: o cilindro deitado ----
                    double x0 = maisNaDireita ? s.X + mola : s.Right - mola - comprimento;
                    var cilindro = new Rect(x0, cy - diametro / 2, comprimento, diametro);
                    var sombra = new GradientStopCollection
                    {
                        new(Puxar(cCorpo, 0.45), 0), new(Puxar(cCorpo, 1.25), 0.22), new(cCorpo, 0.5), new(Puxar(cCorpo, 0.5), 1),
                    };
                    var corpoDaPilha = new LinearGradientBrush(sombra, 90);
                    corpoDaPilha.Freeze();
                    dc.DrawRoundedRectangle(corpoDaPilha, Caneta(Color.FromRgb(0x05, 0x05, 0x06), 0.18 * mm), cilindro, 0.9 * mm, 0.9 * mm);
                    // a faixa do lado do "+" (o ombro da tampa)
                    double faixa = comprimento * 0.13;
                    var faixaRet = new Rect(maisNaDireita ? cilindro.Right - faixa : cilindro.X, cilindro.Y, faixa, diametro);
                    var sombraFaixa = new GradientStopCollection
                    {
                        new(Puxar(cFaixa, 0.5), 0), new(Puxar(cFaixa, 1.2), 0.22), new(cFaixa, 0.5), new(Puxar(cFaixa, 0.55), 1),
                    };
                    var pincelFaixa = new LinearGradientBrush(sombraFaixa, 90);
                    pincelFaixa.Freeze();
                    dc.DrawRoundedRectangle(pincelFaixa, null, faixaRet, 0.9 * mm, 0.9 * mm);
                    // o fundo de aco do "-" e o botao do "+"
                    double aco = 0.7 * mm;
                    dc.DrawRectangle(metal, null, new Rect(maisNaDireita ? cilindro.X : cilindro.Right - aco, cilindro.Y + 0.5 * mm, aco, diametro - 1.0 * mm));
                    double botao = diametro * 0.38;
                    dc.DrawRoundedRectangle(metal, Caneta(Color.FromRgb(0x5A, 0x60, 0x68), 0.12 * mm),
                        new Rect(maisNaDireita ? cilindro.Right - 0.2 * mm : cilindro.X - 1.0 * mm, cy - botao / 2, 1.2 * mm, botao), 0.3 * mm, 0.3 * mm);
                    // o rotulo e o sinal de "+", na direcao da pilha
                    double meioDoRotulo = maisNaDireita ? cilindro.X + (comprimento - faixa) / 2 : cilindro.Right - (comprimento - faixa) / 2;
                    Texto(rotulo, diametro * 0.24, cLetra, new Point(meioDoRotulo, cy));
                    Texto("+", diametro * 0.42, Puxar(cFaixa, 0.35), new Point(faixaRet.X + faixaRet.Width / 2, cy));
                }
            }
        }
        desenho.Freeze();
        return desenho;
    }
}
