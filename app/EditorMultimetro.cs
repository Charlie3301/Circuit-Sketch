using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Simulador.App;

/// <summary>
/// O editor no modo multimetro: o botao esquerdo encosta a ponta vermelha, o
/// direito (ou o Shift+clique, para o trackpad) a preta, num pino, num furo ou
/// num fio. Nada se edita: nao se poe peca, nao se liga fio, nao se arrasta nem
/// se apaga. Arrastar no vazio anda pela tela (o trackpad nao tem o botao do
/// meio), e os botoes das pecas continuam apertando.
/// </summary>
public partial class EditorCircuito
{
    bool soMedir;

    /// <summary>O modo multimetro (a aba MULTÍMETRO): so medir.</summary>
    public bool SoMedir
    {
        get => soMedir;
        set
        {
            if (soMedir == value) return;
            soMedir = value;
            if (value)
            {
                if (origemFio is not null) CancelarFio();
                selecionadas.Clear();
                fiosSelecionados.Clear();
                AtualizarRealce();
                RedesenharFios();
                SelecaoMudou?.Invoke();
            }
            DesenharPontas();
        }
    }

    public PontaDoMultimetro PontaVermelha { get; private set; }
    public PontaDoMultimetro PontaPreta { get; private set; }

    /// <summary>A ponta que o clique esquerdo encosta: a vermelha, ou a preta (escolhida no painel).</summary>
    public bool PretaNoEsquerdo { get; set; }

    /// <summary>O que o visor mostra agora, para a etiqueta ao lado da ponta vermelha.</summary>
    public string TextoDaLeitura
    {
        get => textoDaLeitura;
        set
        {
            if (textoDaLeitura == value) return;
            textoDaLeitura = value;
            DesenharPontas();
        }
    }

    string textoDaLeitura = "";

    public event Action PontasMudaram;

    readonly Canvas marcasDasPontas = new() { IsHitTestVisible = false };
    bool marcasNoMundo;
    bool panejandoComEsquerdo;

    /// <summary>O apertar do esquerdo no multimetro. Sempre consome o clique.</summary>
    internal bool ApertarNoMultimetro(Point p, Point naTela, bool shift)
    {
        // Os botoes, as chaves e as teclas das pecas continuam funcionando: apertar
        // nao e editar, e medir com o botao apertado e o que se faz na bancada.
        var acerto = Resolver(p);
        if (acerto.Camada is Camada.BotaoPlaca or Camada.CapaBotao or Camada.ChaveDip
            or Camada.TeclaMatricial or Camada.FuroDoDisco)
            return Apertar(p);

        var ponta = PontaNoPonto(p);
        if (ponta is null)
        {
            // No vazio: arrastar anda pela tela.
            panejando = true;
            panejandoComEsquerdo = true;
            panInicio = naTela;
            Cursor = Cursors.SizeAll;
            CaptureMouse();
            return true;
        }
        PorPonta(ponta, preta: shift != PretaNoEsquerdo);
        return true;
    }

    /// <summary>O soltar do esquerdo que estava andando pela tela no multimetro.</summary>
    bool SoltarPanDoMultimetro()
    {
        if (!panejandoComEsquerdo) return false;
        panejandoComEsquerdo = false;
        panejando = false;
        Cursor = Cursors.Arrow;
        ReleaseMouseCapture();
        return true;
    }

    /// <summary>O clique do direito no multimetro (sem arrastar): a ponta preta.</summary>
    internal void CliqueDireitoNoMultimetro(Point p)
    {
        var ponta = PontaNoPonto(p);
        if (ponta is not null) PorPonta(ponta, preta: true);
    }

    internal void PorPonta(PontaDoMultimetro ponta, bool preta)
    {
        if (preta) PontaPreta = ponta;
        else PontaVermelha = ponta;
        DesenharPontas();
        PontasMudaram?.Invoke();
    }

    /// <summary>Para o teste e para o painel: encosta uma ponta num pino pelo nome.</summary>
    internal bool PorPontaNoPino(string idDaPeca, string pino, bool preta)
    {
        var onde = PosicaoPino(idDaPeca, pino);
        if (onde is null) return false;
        PorPonta(new PontaDoMultimetro(idDaPeca, pino, -1, onde.Value, RotuloDoPino(idDaPeca, pino)), preta);
        return true;
    }

    /// <summary>Tira as duas pontas (o Esc no multimetro).</summary>
    public void TirarPontas()
    {
        if (PontaVermelha is null && PontaPreta is null) return;
        PontaVermelha = PontaPreta = null;
        DesenharPontas();
        PontasMudaram?.Invoke();
    }

    /// <summary>
    /// Onde a ponta encosta neste ponto: o pino mais perto (de peca, e depois o furo
    /// de uma base), ou o fio que passa ali. Null no vazio.
    /// </summary>
    internal PontaDoMultimetro PontaNoPonto(Point p)
    {
        var pecas = Atual.Pecas.Where(x => !Catalogo.EhProtoboard(x.Tipo) && !EstaDoOutroLado(x)).ToList();
        var bases = Atual.Pecas.Where(x => Catalogo.EhProtoboard(x.Tipo)).ToList();
        var pino = PinoMaisPerto(pecas, p, RaioPino) ?? PinoMaisPerto(bases, p, RaioFuro);
        if (pino is { } achado)
            return new PontaDoMultimetro(achado.peca.Id, achado.pino, -1, achado.pos, RotuloDoPino(achado.peca.Id, achado.pino));

        int fio = FioPerto(p);
        if (fio >= 0)
        {
            var f = Atual.Fios[fio];
            return new PontaDoMultimetro(f.DePeca, f.DePino, fio, p,
                Idioma.T("fio {0} → {1}", RotuloDoPino(f.DePeca, f.DePino), RotuloDoPino(f.ParaPeca, f.ParaPino)));
        }
        return null;
    }

    /// <summary>"D4 (esp1)", "furo J12 (protoboard)".</summary>
    string RotuloDoPino(string idDaPeca, string pino)
    {
        var peca = Atual.Achar(idDaPeca);
        if (peca is null) return $"{pino} ({idDaPeca})";
        return Catalogo.EhProtoboard(peca.Tipo) ? Idioma.T("furo {0} ({1})", pino, peca.Id) : $"{pino} ({peca.Id})";
    }

    /// <summary>
    /// As pontas na tela: o bico de cada uma (vermelho e preto, com o + e o -) no
    /// lugar onde encostou, e a leitura do visor ao lado da vermelha.
    /// </summary>
    void DesenharPontas()
    {
        if (!marcasNoMundo)
        {
            mundo.Children.Add(marcasDasPontas);
            Panel.SetZIndex(marcasDasPontas, 100000);
            marcasNoMundo = true;
        }
        marcasDasPontas.Children.Clear();
        if (!soMedir) return;

        // A ponta de uma peca que saiu do circuito sai junto.
        if (PontaVermelha is { } v && Atual.Achar(v.Peca) is null) PontaVermelha = null;
        if (PontaPreta is { } pr && Atual.Achar(pr.Peca) is null) PontaPreta = null;

        foreach (var (ponta, preta) in new[] { (PontaPreta, true), (PontaVermelha, false) })
        {
            if (ponta is null) continue;
            var onde = ponta.Fio < 0 ? PosicaoPino(ponta.Peca, ponta.Pino) ?? ponta.Onde : ponta.Onde;
            DesenharBico(onde, preta, preta ? null : textoDaLeitura);
        }
    }

    void DesenharBico(Point onde, bool preta, string leitura)
    {
        var cor = preta ? Color.FromRgb(0x22, 0x22, 0x24) : Color.FromRgb(0xD6, 0x2B, 0x2B);
        double z = Math.Max(Zoom, 0.2);
        double s = 1 / z;      // o bico tem o mesmo tamanho na tela em qualquer zoom
        double p = 1.8 * s;    // a ponta de prova, maior que a etiqueta

        // O corpo da ponta de prova, inclinado, saindo do ponto para cima.
        var corpo = new Path
        {
            Data = Geometry.Parse("M 0,0 L 5,-9 L 8,-8 L 3,1 Z M 5,-9 L 13,-25 L 19,-22 L 8,-8 Z"),
            Fill = new SolidColorBrush(cor),
            Stroke = new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF)),
            StrokeThickness = 1,
            RenderTransform = new TransformGroup
            {
                Children = { new ScaleTransform(p, p), new RotateTransform(preta ? -28 : 0), new TranslateTransform(onde.X, onde.Y) },
            },
        };
        marcasDasPontas.Children.Add(corpo);

        // O bico de metal, um ponto no lugar exato.
        double r = 2.6 * p;
        var bico = new Ellipse
        {
            Width = 2 * r, Height = 2 * r,
            Fill = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xE8)),
            Stroke = new SolidColorBrush(cor), StrokeThickness = 1.2 * p,
        };
        Canvas.SetLeft(bico, onde.X - r);
        Canvas.SetTop(bico, onde.Y - r);
        marcasDasPontas.Children.Add(bico);

        var sinal = new TextBlock
        {
            Text = preta ? "−" : "+",
            FontSize = 10 * p,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
        };
        Canvas.SetLeft(sinal, onde.X + (preta ? -15 : 9) * p);
        Canvas.SetTop(sinal, onde.Y - 27 * p);
        marcasDasPontas.Children.Add(sinal);

        if (string.IsNullOrEmpty(leitura)) return;
        var etiqueta = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xE6, 0x1D, 0x23, 0x18)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x9E, 0xAD, 0x86)),
            BorderThickness = new Thickness(1 * s),
            CornerRadius = new CornerRadius(3 * s),
            Padding = new Thickness(5 * s, 1 * s, 5 * s, 1 * s),
            Child = new TextBlock
            {
                Text = leitura,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 11 * s,
                Foreground = new SolidColorBrush(Color.FromRgb(0xC9, 0xD6, 0xB0)),
            },
        };
        Canvas.SetLeft(etiqueta, onde.X + 21 * p);
        Canvas.SetTop(etiqueta, onde.Y - 40 * p);
        marcasDasPontas.Children.Add(etiqueta);
    }
}
