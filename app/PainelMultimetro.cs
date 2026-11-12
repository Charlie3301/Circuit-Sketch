using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using Path = System.Windows.Shapes.Path;

namespace Simulador.App;

/// <summary>
/// O multimetro na tela, no canto de cima da direita da aba MULTÍMETRO: a capa de
/// borracha, o visor de cristal liquido (os digitos de sete segmentos, com os
/// apagados aparecendo de leve, como no visor de verdade), a chave giratoria com
/// os simbolos que vem impressos nele (V~, V⎓, Ω, ·))), A) e as duas entradas das
/// pontas: a COM (preta) e a VΩA (vermelha).
///
/// As cores sao as do objeto, e nao as do tema: e um aparelho em cima da mesa.
/// </summary>
public sealed class PainelMultimetro : Border
{
    static readonly (FuncaoDoMultimetro Funcao, double Angulo, string Dica)[] Posicoes =
    [
        (FuncaoDoMultimetro.TensaoAlternada, -76, Idioma.T("Tensão alternada (V~): o PWM e o tone, em valor eficaz")),
        (FuncaoDoMultimetro.TensaoContinua, -38, Idioma.T("Tensão contínua (V⎓)")),
        (FuncaoDoMultimetro.Resistencia, 0, Idioma.T("Resistência (Ω): entre as duas pontas")),
        (FuncaoDoMultimetro.Continuidade, 38, Idioma.T("Continuidade (·))): bipa abaixo de 50 Ω")),
        (FuncaoDoMultimetro.Corrente, 76, Idioma.T("Corrente (A): a que passa pelo que a ponta vermelha toca")),
    ];

    static readonly Color Ambar = Color.FromRgb(0xFF, 0xC8, 0x57);

    readonly VisorLcd visor = new();
    readonly RotateTransform giroDaChave = new();
    readonly Dictionary<FuncaoDoMultimetro, FrameworkElement> icones = [];
    readonly TextBlock txtNota = new();
    readonly TextBlock txtVermelha = new(), txtPreta = new();
    readonly Ellipse anelVermelho = new(), anelPreto = new();
    System.Media.SoundPlayer bipe;
    bool bipando;

    public FuncaoDoMultimetro Funcao { get; private set; } = FuncaoDoMultimetro.TensaoContinua;

    /// <summary>O clique esquerdo encosta a ponta preta (escolhida clicando na entrada COM).</summary>
    public bool PretaNoEsquerdo { get; private set; }

    public event Action FuncaoMudou;
    public event Action PontaAtivaMudou;

    /// <summary>A ultima leitura mostrada (para o teste).</summary>
    internal LeituraDoMultimetro UltimaLeitura { get; private set; }

    public PainelMultimetro()
    {
        Width = 244;
        // A capa de borracha.
        Background = new LinearGradientBrush(Color.FromRgb(0xE8, 0x8A, 0x2E), Color.FromRgb(0xB8, 0x5E, 0x14), 90);
        CornerRadius = new CornerRadius(18);
        Padding = new Thickness(7);
        Effect = new DropShadowEffect { BlurRadius = 14, ShadowDepth = 3, Opacity = 0.55, Color = Colors.Black };
        Cursor = Cursors.Arrow;

        var corpo = new Border
        {
            Background = new LinearGradientBrush(Color.FromRgb(0x3A, 0x3D, 0x43), Color.FromRgb(0x22, 0x24, 0x28), 90),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x16, 0x17, 0x1A)),
            BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(10, 8, 10, 9),
        };
        Child = corpo;

        var pilha = new StackPanel();
        corpo.Child = pilha;

        var topo = new DockPanel { Margin = new Thickness(2, 0, 2, 6) };
        var auto = new TextBlock { Text = "TRUE RMS", FontSize = 8.5, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Ambar) };
        DockPanel.SetDock(auto, Dock.Right);
        topo.Children.Add(auto);
        topo.Children.Add(new TextBlock
        {
            Text = Idioma.T("MULTÍMETRO DIGITAL"), FontSize = 8.5, FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(0xC8, 0xCB, 0xD0)),
        });
        pilha.Children.Add(topo);

        // O visor: o vidro verde-acinzentado com a moldura escura.
        pilha.Children.Add(new Border
        {
            Background = new LinearGradientBrush(Color.FromRgb(0xB4, 0xC1, 0x9E), Color.FromRgb(0x93, 0xA2, 0x80), 90),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x0E, 0x0F, 0x11)),
            BorderThickness = new Thickness(3),
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(4, 3, 4, 3),
            Child = visor,
            // O visor e a chave sao o aparelho: nao viram com a lingua.
            FlowDirection = FlowDirection.LeftToRight,
        });

        txtNota.FontSize = 10;
        txtNota.TextWrapping = TextWrapping.Wrap;
        txtNota.Foreground = new SolidColorBrush(Color.FromRgb(0xC9, 0xCD, 0xD2));
        txtNota.Margin = new Thickness(2, 6, 2, 0);
        txtNota.MinHeight = 26;
        pilha.Children.Add(txtNota);

        pilha.Children.Add(MontarChave());
        pilha.Children.Add(MontarEntradas());

        pilha.Children.Add(new TextBlock
        {
            Text = Idioma.T("Esquerdo: ponta vermelha · direito ou Shift+clique: preta · arraste no vazio para andar · Esc tira as pontas"),
            FontSize = 9, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 7, 2, 0),
            Foreground = new SolidColorBrush(Color.FromRgb(0x8E, 0x94, 0x9C)),
        });

        Escolher(Funcao, animar: false);
        Mostrar(null, null, null);
        IsVisibleChanged += (_, _) => { if (!IsVisible) Silenciar(); };
    }

    // =====================================================================
    //  a chave giratoria
    // =====================================================================

    FrameworkElement MontarChave()
    {
        const double L = 220, A = 128, Cx = L / 2, Cy = 84, RaioDoBotao = 34, RaioDosIcones = 60;
        var tela = new Canvas { Width = L, Height = A, Margin = new Thickness(0, 4, 0, 0), Background = Brushes.Transparent, FlowDirection = FlowDirection.LeftToRight };

        // As marcas de cada posicao, em volta do botao.
        foreach (var (funcao, angulo, dica) in Posicoes)
        {
            double rad = angulo * Math.PI / 180;
            double sx = Math.Sin(rad), sy = -Math.Cos(rad);
            tela.Children.Add(new Line
            {
                X1 = Cx + sx * (RaioDoBotao + 4), Y1 = Cy + sy * (RaioDoBotao + 4),
                X2 = Cx + sx * (RaioDoBotao + 10), Y2 = Cy + sy * (RaioDoBotao + 10),
                Stroke = new SolidColorBrush(Color.FromRgb(0xD9, 0xDC, 0xE0)), StrokeThickness = 2,
            });

            var icone = Icone(funcao);
            icone.ToolTip = dica;
            icone.Cursor = Cursors.Hand;
            icone.MouseLeftButtonDown += (_, e) => { Escolher(funcao); e.Handled = true; };
            icones[funcao] = icone;
            Canvas.SetLeft(icone, Cx + sx * RaioDosIcones - 16);
            Canvas.SetTop(icone, Cy + sy * RaioDosIcones - 13);
            tela.Children.Add(icone);
        }

        // O botao: o disco de borracha e a barra de pegar, com a seta.
        var disco = new Ellipse
        {
            Width = 2 * RaioDoBotao, Height = 2 * RaioDoBotao,
            Fill = new RadialGradientBrush(Color.FromRgb(0x5A, 0x5E, 0x65), Color.FromRgb(0x1F, 0x21, 0x25))
            { GradientOrigin = new Point(0.35, 0.3) },
            Stroke = new SolidColorBrush(Color.FromRgb(0x0C, 0x0D, 0x0F)), StrokeThickness = 2,
        };
        Canvas.SetLeft(disco, Cx - RaioDoBotao);
        Canvas.SetTop(disco, Cy - RaioDoBotao);
        tela.Children.Add(disco);

        var barra = new Canvas { Width = 0, Height = 0, RenderTransform = giroDaChave };
        var pega = new Rectangle
        {
            Width = 16, Height = 2 * RaioDoBotao - 6, RadiusX = 6, RadiusY = 6,
            Fill = new LinearGradientBrush(Color.FromRgb(0x4A, 0x4E, 0x55), Color.FromRgb(0x2A, 0x2C, 0x31), 0),
            Stroke = new SolidColorBrush(Color.FromRgb(0x10, 0x11, 0x13)), StrokeThickness = 1,
        };
        Canvas.SetLeft(pega, -8);
        Canvas.SetTop(pega, -RaioDoBotao + 3);
        barra.Children.Add(pega);
        var seta = new Path
        {
            Data = Geometry.Parse(FormattableString.Invariant($"M -4,{-RaioDoBotao + 13} L 0,{-RaioDoBotao + 5} L 4,{-RaioDoBotao + 13} Z")),
            Fill = Brushes.White,
        };
        barra.Children.Add(seta);
        Canvas.SetLeft(barra, Cx);
        Canvas.SetTop(barra, Cy);
        tela.Children.Add(barra);

        // Clicar no botao gira para a proxima; a roda do mouse gira para os dois lados.
        disco.Cursor = Cursors.Hand;
        disco.ToolTip = Idioma.T("Girar a chave (a roda do mouse também gira)");
        disco.MouseLeftButtonDown += (_, e) => { Girar(+1); e.Handled = true; };
        pega.MouseLeftButtonDown += (_, e) => { Girar(+1); e.Handled = true; };
        tela.MouseWheel += (_, e) => { Girar(e.Delta > 0 ? -1 : +1); e.Handled = true; };
        return tela;
    }

    void Girar(int passo)
    {
        int i = Array.FindIndex(Posicoes, p => p.Funcao == Funcao);
        int proximo = Math.Clamp(i + passo, 0, Posicoes.Length - 1);
        if (proximo != i) Escolher(Posicoes[proximo].Funcao);
    }

    void Escolher(FuncaoDoMultimetro funcao, bool animar = true)
    {
        bool mudou = funcao != Funcao;
        Funcao = funcao;
        double angulo = Posicoes.First(p => p.Funcao == funcao).Angulo;
        if (animar)
            giroDaChave.BeginAnimation(RotateTransform.AngleProperty,
                new DoubleAnimation(angulo, TimeSpan.FromMilliseconds(140)) { EasingFunction = new QuadraticEase() });
        else
        {
            giroDaChave.BeginAnimation(RotateTransform.AngleProperty, null);
            giroDaChave.Angle = angulo;
        }
        foreach (var (f, icone) in icones) PintarIcone(icone, f == funcao);
        visor.Funcao = funcao;
        if (mudou) FuncaoMudou?.Invoke();
    }

    /// <summary>Para o teste: gira a chave.</summary>
    internal void EscolherParaTeste(FuncaoDoMultimetro funcao) => Escolher(funcao, animar: false);

    static void PintarIcone(FrameworkElement icone, bool escolhido)
    {
        var cor = new SolidColorBrush(escolhido ? Ambar : Color.FromRgb(0xE4, 0xE6, 0xEA));
        foreach (var filho in ((Canvas)icone).Children)
            switch (filho)
            {
                case TextBlock t: t.Foreground = cor; break;
                case Shape s:
                    if (s.Stroke is not null) s.Stroke = cor;
                    if (s.Fill is not null) s.Fill = cor;
                    break;
            }
    }

    /// <summary>Os simbolos impressos em volta da chave, como no multimetro de verdade.</summary>
    static FrameworkElement Icone(FuncaoDoMultimetro funcao)
    {
        var c = new Canvas { Width = 32, Height = 26, Background = Brushes.Transparent };
        var branco = Brushes.White;
        TextBlock Letra(string texto, double x, double y, double tamanho = 15)
        {
            var t = new TextBlock { Text = texto, FontSize = tamanho, FontWeight = FontWeights.Bold, Foreground = branco };
            Canvas.SetLeft(t, x);
            Canvas.SetTop(t, y);
            return t;
        }
        Path Traco(string dados, double espessura = 1.6) => new()
        {
            Data = Geometry.Parse(dados), Stroke = branco, StrokeThickness = espessura,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
        };
        // O simbolo da continua: a linha cheia sobre a tracejada.
        void Continua(double x, double y)
        {
            // Os numeros com ponto, e nao com a virgula do portugues: o Geometry.Parse le a virgula como separador.
            c.Children.Add(Traco(FormattableString.Invariant($"M {x},{y} H {x + 11}")));
            c.Children.Add(Traco(FormattableString.Invariant(
                $"M {x},{y + 3.5} H {x + 2.5} M {x + 4.3},{y + 3.5} H {x + 6.8} M {x + 8.6},{y + 3.5} H {x + 11}"), 1.3));
        }

        switch (funcao)
        {
            case FuncaoDoMultimetro.TensaoAlternada:
                c.Children.Add(Letra("V", 3, 4));
                c.Children.Add(Traco("M 17,12 C 18.5,8 20.5,8 22,12 S 25.5,16 27,12"));
                break;
            case FuncaoDoMultimetro.TensaoContinua:
                c.Children.Add(Letra("V", 3, 4));
                Continua(17, 9.5);
                break;
            case FuncaoDoMultimetro.Resistencia:
                c.Children.Add(Letra("Ω", 9, 3, 16));
                break;
            case FuncaoDoMultimetro.Continuidade:
                c.Children.Add(new Ellipse { Width = 4, Height = 4, Fill = branco });
                Canvas.SetLeft(c.Children[^1], 6);
                Canvas.SetTop(c.Children[^1], 11);
                c.Children.Add(Traco("M 12,8 A 6,6 0 0 1 12,18"));
                c.Children.Add(Traco("M 16,5 A 9,9 0 0 1 16,21"));
                c.Children.Add(Traco("M 20,2 A 12,12 0 0 1 20,24"));
                break;
            default:
                c.Children.Add(Letra("A", 3, 4));
                Continua(17, 9.5);
                break;
        }
        return c;
    }

    // =====================================================================
    //  as entradas das pontas
    // =====================================================================

    FrameworkElement MontarEntradas()
    {
        // COM a esquerda e VΩA a direita como no aparelho, em qualquer lingua.
        var grade = new Grid { Margin = new Thickness(0, 2, 0, 0), FlowDirection = FlowDirection.LeftToRight };
        grade.ColumnDefinitions.Add(new ColumnDefinition());
        grade.ColumnDefinitions.Add(new ColumnDefinition());

        var preta = Entrada("COM", Color.FromRgb(0x15, 0x15, 0x17), Color.FromRgb(0x6A, 0x6E, 0x75), anelPreto, txtPreta, true);
        var vermelha = Entrada("VΩA", Color.FromRgb(0xC8, 0x22, 0x22), Color.FromRgb(0xF0, 0x6A, 0x6A), anelVermelho, txtVermelha, false);
        Grid.SetColumn(vermelha, 1);
        grade.Children.Add(preta);
        grade.Children.Add(vermelha);
        AtualizarEntradaAtiva();
        return grade;
    }

    FrameworkElement Entrada(string nome, Color cor, Color borda, Ellipse anel, TextBlock onde, bool preta)
    {
        var linha = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2, 0, 4, 0), Cursor = Cursors.Hand };
        var tomada = new Grid { Width = 24, Height = 24, VerticalAlignment = VerticalAlignment.Top };
        anel.Width = anel.Height = 24;
        anel.StrokeThickness = 2.5;
        tomada.Children.Add(anel);
        tomada.Children.Add(new Ellipse
        {
            Width = 18, Height = 18,
            Fill = new RadialGradientBrush(borda, cor) { GradientOrigin = new Point(0.35, 0.3) },
        });
        tomada.Children.Add(new Ellipse { Width = 7, Height = 7, Fill = new SolidColorBrush(Color.FromRgb(0x08, 0x08, 0x09)) });
        linha.Children.Add(tomada);

        var textos = new StackPanel { Margin = new Thickness(6, 0, 0, 0), Width = 76 };
        textos.Children.Add(new TextBlock
        {
            Text = nome, FontSize = 10, FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(preta ? Color.FromRgb(0xE4, 0xE6, 0xEA) : Color.FromRgb(0xFF, 0x7A, 0x7A)),
        });
        onde.FontSize = 9;
        onde.TextWrapping = TextWrapping.Wrap;
        if (Idioma.Cultura.TextInfo.IsRightToLeft) onde.FlowDirection = FlowDirection.RightToLeft;
        onde.Foreground = new SolidColorBrush(Color.FromRgb(0xB5, 0xBA, 0xC1));
        textos.Children.Add(onde);
        linha.Children.Add(textos);

        linha.ToolTip = preta
            ? Idioma.T("A ponta preta. Clique aqui para o clique esquerdo encostar a preta (bom no trackpad).")
            : Idioma.T("A ponta vermelha. Clique aqui para o clique esquerdo voltar a encostar a vermelha.");
        linha.MouseLeftButtonDown += (_, e) =>
        {
            if (PretaNoEsquerdo == preta) return;
            PretaNoEsquerdo = preta;
            AtualizarEntradaAtiva();
            PontaAtivaMudou?.Invoke();
            e.Handled = true;
        };
        return linha;
    }

    void AtualizarEntradaAtiva()
    {
        anelVermelho.Stroke = new SolidColorBrush(PretaNoEsquerdo ? Color.FromRgb(0x30, 0x32, 0x36) : Ambar);
        anelPreto.Stroke = new SolidColorBrush(PretaNoEsquerdo ? Ambar : Color.FromRgb(0x30, 0x32, 0x36));
    }

    // =====================================================================
    //  a leitura
    // =====================================================================

    /// <summary>Mostra a leitura no visor, e onde cada ponta esta.</summary>
    public void Mostrar(LeituraDoMultimetro leitura, string ondeVermelha, string ondePreta)
    {
        UltimaLeitura = leitura;
        visor.Mostrar(leitura);
        txtNota.Text = leitura?.Nota ?? "";
        txtVermelha.Text = ondeVermelha ?? Idioma.T("solta");
        txtPreta.Text = ondePreta ?? (Funcao is FuncaoDoMultimetro.Resistencia or FuncaoDoMultimetro.Continuidade
            ? Idioma.T("solta") : Idioma.T("solta (o terra conta)"));
        Bipar(leitura?.Bipe == true);
    }

    void Bipar(bool ligado)
    {
        if (ligado == bipando) return;
        bipando = ligado;
        if (Autoteste.Rodando || Passeio.Ativo) return;
        try
        {
            if (ligado)
            {
                bipe ??= new System.Media.SoundPlayer(new MemoryStream(SomDoBipe()));
                bipe.PlayLooping();
            }
            else bipe?.Stop();
        }
        catch { }
    }

    /// <summary>Para o bipe (saiu da aba, a simulacao parou).</summary>
    public void Silenciar() => Bipar(false);

    /// <summary>O bipe da continuidade: 2,4 kHz, meio segundo, que da a volta sem emenda.</summary>
    static byte[] SomDoBipe()
    {
        const int Taxa = 44100, Amostras = Taxa / 2;
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write("RIFF"u8); w.Write(36 + Amostras * 2); w.Write("WAVE"u8);
        w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(Taxa); w.Write(Taxa * 2);
        w.Write((short)2); w.Write((short)16);
        w.Write("data"u8); w.Write(Amostras * 2);
        for (int i = 0; i < Amostras; i++)
            w.Write((short)(Math.Sin(2 * Math.PI * 2400 * i / Taxa) * 6500));
        w.Flush();
        return ms.ToArray();
    }
}

/// <summary>
/// O visor de cristal liquido: quatro digitos de sete segmentos inclinados (com os
/// apagados aparecendo de leve), o sinal, os pontos decimais, a unidade e os
/// avisos de cima (AC, DC, AUTO, o simbolo da continuidade).
/// </summary>
public sealed class VisorLcd : FrameworkElement
{
    LeituraDoMultimetro leitura;
    FuncaoDoMultimetro funcao = FuncaoDoMultimetro.TensaoContinua;

    static readonly Brush Aceso = Congelado(Color.FromArgb(0xEE, 0x1A, 0x20, 0x15));
    static readonly Brush Apagado = Congelado(Color.FromArgb(0x16, 0x00, 0x00, 0x00));
    static readonly Typeface Fonte = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);

    static Brush Congelado(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    public VisorLcd()
    {
        Width = 190;
        Height = 78;
    }

    public FuncaoDoMultimetro Funcao
    {
        get => funcao;
        set { funcao = value; InvalidateVisual(); }
    }

    public void Mostrar(LeituraDoMultimetro nova)
    {
        leitura = nova;
        InvalidateVisual();
    }

    static string Segmentos(char c) => c switch
    {
        '0' or 'O' => "abcdef", '1' => "bc", '2' => "abdeg", '3' => "abcdg", '4' => "bcfg", '5' => "acdfg",
        '6' => "acdefg", '7' => "abc", '8' => "abcdefg", '9' => "abcdfg", '-' => "g", 'L' => "def", _ => "",
    };

    protected override void OnRender(DrawingContext dc)
    {
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        // Os avisos de cima.
        void Aviso(string texto, double x, double tamanho = 9.5)
            => dc.DrawText(new FormattedText(texto, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Fonte, tamanho, Aceso, dpi),
                           new Point(x, 1));
        if (funcao == FuncaoDoMultimetro.TensaoAlternada) Aviso("AC", 4);
        if (funcao is FuncaoDoMultimetro.TensaoContinua or FuncaoDoMultimetro.Corrente) Aviso("DC", 4);
        Aviso(Idioma.T("AUTO"), 26);
        if (funcao == FuncaoDoMultimetro.Continuidade)
        {
            dc.DrawEllipse(Aceso, null, new Point(64, 8), 1.6, 1.6);
            var arcos = Geometry.Parse("M 67,4.5 A 4,4 0 0 1 67,11.5 M 70,2.5 A 6.5,6.5 0 0 1 70,13.5 M 73,1 A 9,9 0 0 1 73,15");
            dc.DrawGeometry(null, new Pen(leitura?.Bipe == true ? Aceso : Apagado, 1.3), arcos);
        }
        if (funcao == FuncaoDoMultimetro.Continuidade && leitura?.Bipe == true) Aviso(Idioma.T("BIP"), 84);

        // Os digitos: o texto do visor em quatro casas, alinhado a direita.
        string texto = leitura?.Visor ?? "----";
        bool negativo = texto.Length > 1 && texto[0] == '-' && char.IsDigit(texto[1]);
        if (negativo) texto = texto[1..];
        var casas = new List<(char Digito, bool Ponto)>();
        foreach (char c in texto)
        {
            if (c == '.' && casas.Count > 0) casas[^1] = (casas[^1].Digito, true);
            else casas.Add((c, false));
        }
        if (texto == "OL") casas = [(' ', false), ('O', false), ('L', false), (' ', false)];
        while (casas.Count < 4) casas.Insert(0, (' ', false));
        if (casas.Count > 4) casas = casas[^4..];

        const double Topo = 18, W = 24, H = 46, T = 5, Passo = 30, X0 = 18;
        dc.PushTransform(new SkewTransform(-7, 0, 0, Topo + H / 2));

        // O sinal de menos, numa casa estreita.
        DesenharSegmento(dc, 'g', 4, Topo, 12, H, T, negativo ? Aceso : Apagado);

        for (int i = 0; i < 4; i++)
        {
            double x = X0 + i * Passo;
            string acesos = Segmentos(casas[i].Digito);
            foreach (char s in "abcdefg")
                DesenharSegmento(dc, s, x, Topo, W, H, T, acesos.Contains(s) ? Aceso : Apagado);
            if (i < 3)
                dc.DrawRectangle(casas[i].Ponto ? Aceso : Apagado, null, new Rect(x + W + 1.6, Topo + H - T, T - 0.6, T));
        }
        dc.Pop();

        // A unidade, embaixo a direita.
        string unidade = leitura?.Unidade ?? "";
        var ft = new FormattedText(unidade, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Fonte, 16, Aceso, dpi);
        dc.DrawText(ft, new Point(Width - ft.Width - 2, Topo + H - ft.Height + 2));
    }

    /// <summary>Um segmento: o hexagono comprido, horizontal ou em pe.</summary>
    static void DesenharSegmento(DrawingContext dc, char s, double x, double y, double w, double h, double t, Brush pincel)
    {
        double g = 0.9, m = h / 2, k = t / 2;
        (Point a, Point b, bool deitado) = s switch
        {
            'a' => (new Point(x + k + g, y + k), new Point(x + w - k - g, y + k), true),
            'g' => (new Point(x + k + g, y + m), new Point(x + w - k - g, y + m), true),
            'd' => (new Point(x + k + g, y + h - k), new Point(x + w - k - g, y + h - k), true),
            'f' => (new Point(x + k, y + k + g), new Point(x + k, y + m - g), false),
            'b' => (new Point(x + w - k, y + k + g), new Point(x + w - k, y + m - g), false),
            'e' => (new Point(x + k, y + m + g), new Point(x + k, y + h - k - g), false),
            _ => (new Point(x + w - k, y + m + g), new Point(x + w - k, y + h - k - g), false),
        };
        Point[] pontos = deitado
            ? [a, new(a.X + k, a.Y - k), new(b.X - k, b.Y - k), b, new(b.X - k, b.Y + k), new(a.X + k, a.Y + k)]
            : [a, new(a.X + k, a.Y + k), new(b.X + k, b.Y - k), b, new(b.X - k, b.Y - k), new(a.X - k, a.Y + k)];
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(pontos[0], true, true);
            ctx.PolyLineTo(pontos[1..], true, true);
        }
        geo.Freeze();
        dc.DrawGeometry(pincel, null, geo);
    }
}
