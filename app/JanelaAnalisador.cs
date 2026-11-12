using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Simulador.App;

/// <summary>
/// A janela do analisador lógico: os oito canais grandes, com régua de
/// tempo, zoom, pausa, medida embaixo do mouse e a serial escrita em cima
/// do fio.
///
/// A peça no circuito mostra o sinal do tamanho de um selo; aqui é onde se
/// estuda. É o que o analisador de bancada faz com o PulseView: congela a
/// captura, aproxima na borda que interessa e mede -- a largura do pulso
/// do servo, o período do PWM, o byte que a serial mandou. E o botão de
/// salvar leva a mesma captura para o próprio PulseView, com os
/// decodificadores dele.
/// </summary>
public class JanelaAnalisador : Window
{
    readonly Func<List<Analisador.Fonte>> fontesAgora;
    readonly PecaNoCircuito peca;

    double janela;
    bool pausado;
    double fimCongelado;
    bool disparo = true;

    (double De, double Ate) visto;
    List<Analisador.Fonte> fontesVistas = [];
    readonly List<(double Us, int Nivel)>[] ondasVistas = new List<(double, int)>[Analisador.Canais];

    readonly Canvas tela = new() { ClipToBounds = true };
    readonly Canvas rotulos = new() { ClipToBounds = true };
    readonly TextBlock medida = new() { FontSize = 12, Margin = new Thickness(10, 6, 10, 8) };
    readonly TextBlock escala = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 12, 0) };
    readonly Button pausar;
    readonly DispatcherTimer relogio;

    Point? mouse;
    Point? arrastandoDe;
    double arrasteInicio;

    const double AlturaDaRegua = 26;
    const double LarguraDosRotulos = 128;

    public JanelaAnalisador(Window dono, PecaNoCircuito peca, Func<List<Analisador.Fonte>> fontes)
    {
        this.peca = peca;
        fontesAgora = fontes;
        janela = Analisador.JanelaDe(peca);

        try { Owner = dono; } catch (InvalidOperationException) { }
        Title = Idioma.T("Analisador lógico{0}", (string.IsNullOrWhiteSpace(peca.Nome) ? "" : $" — {peca.Nome}"));
        Width = 1000;
        Height = 560;
        MinWidth = 640;
        MinHeight = 380;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, Tema.Fundo);

        // ---- a barra de cima ----
        var barra = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 8, 10, 8) };

        pausar = Botao(Idioma.T("⏸ Pausar"), Idioma.T("Congela a captura para medir (Espaço)"), () => AlternarPausa());
        barra.Children.Add(pausar);
        barra.Children.Add(Botao("−", Idioma.T("Afasta: mais tempo na tela (roda do mouse)"), () => Zoom(1.25, 0.5)));
        barra.Children.Add(Botao("+", Idioma.T("Aproxima: menos tempo na tela (roda do mouse)"), () => Zoom(0.8, 0.5)));
        escala.SetResourceReference(TextBlock.ForegroundProperty, Tema.Texto);
        barra.Children.Add(escala);

        var comDisparo = new CheckBox
        {
            Content = Idioma.T("Disparo na subida"),
            IsChecked = true,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 14, 0),
            ToolTip = Idioma.T("Começa a tela na última subida do primeiro canal com sinal: o sinal repetido fica parado"),
        };
        comDisparo.SetResourceReference(ForegroundProperty, Tema.Texto);
        comDisparo.Checked += (_, _) => disparo = true;
        comDisparo.Unchecked += (_, _) => disparo = false;
        barra.Children.Add(comDisparo);

        barra.Children.Add(Botao(Idioma.T("Salvar .vcd…"), Idioma.T("Salva o que está na tela para abrir no PulseView ou no GTKWave"),
                                 SalvarVcd));

        // ---- os canais ----
        var meio = new Grid { Margin = new Thickness(10, 0, 10, 0) };
        meio.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(LarguraDosRotulos) });
        meio.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var fundoDosRotulos = new Border { CornerRadius = new CornerRadius(4, 0, 0, 4), Child = rotulos };
        fundoDosRotulos.SetResourceReference(Border.BackgroundProperty, Tema.Painel);
        meio.Children.Add(fundoDosRotulos);

        var fundoDaTela = new Border { CornerRadius = new CornerRadius(0, 4, 4, 0), Child = tela };
        fundoDaTela.SetResourceReference(Border.BackgroundProperty, Tema.FundoDoCircuito);
        Grid.SetColumn(fundoDaTela, 1);
        meio.Children.Add(fundoDaTela);

        tela.Background = Brushes.Transparent;   // para receber o mouse no vazio
        tela.MouseMove += (_, e) => { mouse = e.GetPosition(tela); Arrastar(e); };
        tela.MouseLeave += (_, _) => mouse = null;
        tela.MouseWheel += (_, e) =>
        {
            double onde = tela.ActualWidth > 0 ? e.GetPosition(tela).X / tela.ActualWidth : 0.5;
            Zoom(e.Delta > 0 ? 0.8 : 1.25, onde);
        };
        tela.MouseLeftButtonDown += (_, e) =>
        {
            if (!pausado) AlternarPausa();
            arrastandoDe = e.GetPosition(tela);
            arrasteInicio = fimCongelado;
            tela.CaptureMouse();
        };
        tela.MouseLeftButtonUp += (_, _) => { arrastandoDe = null; tela.ReleaseMouseCapture(); };

        medida.SetResourceReference(TextBlock.ForegroundProperty, Tema.TextoFraco);
        medida.Text = Idioma.T("Passe o mouse sobre um canal para medir. Clique e arraste para andar no tempo.");

        var raiz = new DockPanel();
        DockPanel.SetDock(barra, Dock.Top);
        raiz.Children.Add(barra);
        DockPanel.SetDock(medida, Dock.Bottom);
        raiz.Children.Add(medida);
        raiz.Children.Add(meio);
        Content = raiz;

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Space) { AlternarPausa(); e.Handled = true; }
            if (e.Key is Key.Add or Key.OemPlus) Zoom(0.8, 0.5);
            if (e.Key is Key.Subtract or Key.OemMinus) Zoom(1.25, 0.5);
        };

        relogio = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(40) };
        relogio.Tick += (_, _) => Desenhar();
        Loaded += (_, _) => { relogio.Start(); Desenhar(); };
        Closed += (_, _) => relogio.Stop();
    }

    Button Botao(string texto, string dica, Action fazer)
    {
        var botao = new Button
        {
            Content = texto,
            ToolTip = dica,
            Style = (Style)Application.Current.FindResource("BotaoPequeno"),
            Margin = new Thickness(0, 0, 6, 0),
            MinWidth = 30,
        };
        botao.Click += (_, _) => fazer();
        return botao;
    }

    void AlternarPausa()
    {
        pausado = !pausado;
        if (pausado) fimCongelado = visto.Ate;
        pausar.Content = pausado ? Idioma.T("▶ Continuar") : Idioma.T("⏸ Pausar");
    }

    void Zoom(double fator, double onde)
    {
        double antes = janela;
        janela = Math.Clamp(janela * fator, 10, 10_000_000);

        // Pausado, o zoom fica em volta do mouse: o ponto embaixo dele nao anda.
        if (pausado)
        {
            double t = visto.De + onde * antes;
            fimCongelado = t + (1 - onde) * janela;
        }
    }

    void Arrastar(MouseEventArgs e)
    {
        if (arrastandoDe is null || e.LeftButton != MouseButtonState.Pressed || tela.ActualWidth <= 0) return;
        double dx = e.GetPosition(tela).X - arrastandoDe.Value.X;
        fimCongelado = arrasteInicio - dx / tela.ActualWidth * janela;
    }

    // =====================================================================
    //  desenho
    // =====================================================================

    void Desenhar()
    {
        double largura = tela.ActualWidth, altura = tela.ActualHeight;
        if (largura < 20 || altura < 60) return;

        fontesVistas = fontesAgora();
        visto = pausado
            ? (fimCongelado - janela, fimCongelado)
            : Analisador.Enquadrar(fontesVistas, janela, disparo);

        var (de, ate) = visto;
        escala.Text = Idioma.T("{0} na tela", Analisador.Tempo(janela));

        tela.Children.Clear();
        rotulos.Children.Clear();

        double linha = (altura - AlturaDaRegua) / Analisador.Canais;
        var corDoSinal = Tema.Pincel(Tema.Destaque);
        var corDaGrade = Tema.Pincel(Tema.PontoDaGrade);
        var corDoTexto = Tema.Pincel(Tema.Texto);
        var corFraca = Tema.Pincel(Tema.TextoFraco);

        double X(double us) => (us - de) / (ate - de) * largura;

        for (int k = 0; k < Analisador.Canais; k++)
        {
            var fonte = k < fontesVistas.Count ? fontesVistas[k] : default;
            double topo = k * linha;
            double alto = topo + linha * 0.25, baixo = topo + linha * 0.8;

            // ---- o rotulo ----
            var nome = new TextBlock { Text = $"D{k}", FontWeight = FontWeights.SemiBold, FontSize = 13, Foreground = corDoTexto };
            Canvas.SetLeft(nome, 10); Canvas.SetTop(nome, topo + linha * 0.2);
            rotulos.Children.Add(nome);

            var origem = new TextBlock
            {
                Text = fonte.Ligada ? Idioma.T("GPIO {0}", fonte.Gpio) : Idioma.T("sem ligação"),
                FontSize = 11,
                Foreground = corFraca,
            };
            Canvas.SetLeft(origem, 40); Canvas.SetTop(origem, topo + linha * 0.24);
            rotulos.Children.Add(origem);

            // ---- a divisoria entre canais ----
            var divisa = new Line { X1 = 0, X2 = largura, Y1 = topo + linha, Y2 = topo + linha, Stroke = corDaGrade, StrokeThickness = 1 };
            tela.Children.Add(divisa);

            if (!fonte.Ligada)
            {
                ondasVistas[k] = null;
                var reta = new Line { X1 = 0, X2 = largura, Y1 = baixo, Y2 = baixo, Stroke = corFraca, StrokeThickness = 1, StrokeDashArray = [3, 3] };
                tela.Children.Add(reta);
                continue;
            }

            var onda = Analisador.OndaParaDesenhar(fonte, de, ate, largura);
            ondasVistas[k] = onda;

            var geometria = new StreamGeometry();
            using (var g = geometria.Open())
            {
                double Y(int n) => n == 1 ? alto : baixo;
                g.BeginFigure(new Point(0, Y(onda[0].Nivel)), false, false);
                for (int i = 1; i < onda.Count; i++)
                {
                    double x = X(onda[i].Us);
                    g.LineTo(new Point(x, Y(onda[i - 1].Nivel)), true, false);
                    g.LineTo(new Point(x, Y(onda[i].Nivel)), true, false);
                }
                g.LineTo(new Point(largura, Y(onda[^1].Nivel)), true, false);
            }
            geometria.Freeze();
            tela.Children.Add(new System.Windows.Shapes.Path { Data = geometria, Stroke = corDoSinal, StrokeThickness = 1.6 });

            // ---- a serial escrita em cima do fio ----
            foreach (var q in fonte.Maquina.Linha.QuadrosEntre(fonte.Gpio, de, ate))
            {
                double x0 = X(q.Inicio), x1 = X(q.Inicio + 10e6 / q.Baud);
                if (x1 - x0 < 14) break;   // longe demais para ler a letra

                string letra = q.Valor is >= 32 and < 127 ? ((char)q.Valor).ToString()
                             : q.Valor == 10 ? "\\n" : q.Valor == 13 ? "\\r" : $"0x{q.Valor:X2}";

                var caixa = new Border
                {
                    Width = Math.Max(0, x1 - x0 - 2),
                    Height = linha * 0.2,
                    CornerRadius = new CornerRadius(2),
                    BorderThickness = new Thickness(1),
                    BorderBrush = corFraca,
                    Child = new TextBlock
                    {
                        Text = letra, FontSize = 10, Foreground = corDoTexto,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                    },
                };
                Canvas.SetLeft(caixa, x0 + 1); Canvas.SetTop(caixa, topo + 2);
                tela.Children.Add(caixa);
            }
        }

        DesenharRegua(largura, altura, de, ate, corFraca, corDaGrade);
        DesenharMouse(largura, altura, linha, corDoTexto);
    }

    void DesenharRegua(double largura, double altura, double de, double ate, Brush corFraca, Brush corDaGrade)
    {
        double y = altura - AlturaDaRegua;
        tela.Children.Add(new Line { X1 = 0, X2 = largura, Y1 = y, Y2 = y, Stroke = corFraca, StrokeThickness = 1 });

        // Passo "redondo" (1, 2 ou 5 vezes uma potencia de dez) com umas 8 marcas.
        double bruto = (ate - de) / 8;
        double potencia = Math.Pow(10, Math.Floor(Math.Log10(bruto)));
        double passo = new[] { 1, 2, 5, 10 }.Select(m => m * potencia).First(p => p >= bruto);

        // Marcas redondas a partir da borda esquerda -- a mesma conta do
        // "t =" do mouse. Contadas do relogio da placa, saiam "9,883 ms".
        for (double t = de + passo; t < ate; t += passo)
        {
            double x = (t - de) / (ate - de) * largura;
            tela.Children.Add(new Line { X1 = x, X2 = x, Y1 = 0, Y2 = y, Stroke = corDaGrade, StrokeThickness = 1, Opacity = 0.5 });
            tela.Children.Add(new Line { X1 = x, X2 = x, Y1 = y, Y2 = y + 5, Stroke = corFraca, StrokeThickness = 1 });

            var marca = new TextBlock { Text = Analisador.Tempo(t - de), FontSize = 10, Foreground = corFraca };
            Canvas.SetLeft(marca, x + 3); Canvas.SetTop(marca, y + 6);
            tela.Children.Add(marca);
        }
    }

    void DesenharMouse(double largura, double altura, double linha, Brush corDoTexto)
    {
        if (mouse is not { } m || m.X < 0 || m.X > largura) return;

        tela.Children.Add(new Line
        {
            X1 = m.X, X2 = m.X, Y1 = 0, Y2 = altura - AlturaDaRegua,
            Stroke = corDoTexto, StrokeThickness = 1, Opacity = 0.6, StrokeDashArray = [4, 3],
        });

        int canal = Math.Clamp((int)(m.Y / linha), 0, Analisador.Canais - 1);
        medida.Text = MedidaEm(m.X / largura, canal);
    }

    /// <summary>A medida no ponto (0 a 1 da largura) do canal, como a barra de baixo mostra.</summary>
    string MedidaEm(double fracao, int canal)
    {
        var (de, ate) = visto;
        double us = de + fracao * (ate - de);
        string quando = $"t = {Analisador.Tempo(us - de)}";

        var onda = ondasVistas[canal];
        if (onda is null) return Idioma.T("{0}  ·  D{1} sem ligação", quando, canal);

        return $"{quando}  ·  D{canal}: {Analisador.Medir(onda, us, ate)}";
    }

    void SalvarVcd()
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Filter = Idioma.T("Captura do analisador (*.vcd)|*.vcd"),
            FileName = "captura.vcd",
        };
        if (dlg.ShowDialog(this) != true) return;

        File.WriteAllText(dlg.FileName, VcdDoQueEstaNaTela());
    }

    string VcdDoQueEstaNaTela()
        => Analisador.Vcd(fontesVistas, visto.De, visto.Ate,
                          string.IsNullOrWhiteSpace(peca.Nome) ? "analisador" : peca.Nome);

    // ---- para o teste ----
    internal void DesenharParaTeste() => Desenhar();
    internal int CanaisLigadosParaTeste => fontesVistas.Count(f => f.Ligada);
    internal int BordasParaTeste(int canal) => ondasVistas[canal] is { } o ? o.Count - 1 : -1;
    internal string MedidaParaTeste(double fracao, int canal) => MedidaEm(fracao, canal);
    internal string VcdParaTeste() => VcdDoQueEstaNaTela();
    internal void PausarParaTeste() { if (!pausado) AlternarPausa(); }
    internal void JanelaParaTeste(double us) => janela = us;
}
