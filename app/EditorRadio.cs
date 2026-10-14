using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Simulador.App;

/// <summary>
/// O controle de radio na mesa (FS-i6, modo 2): os dois manches se arrastam
/// com o mouse -- o direito volta ao centro ao soltar, o esquerdo volta so
/// no leme; o acelerador fica onde parou, como no de verdade --, as chaves
/// SWA e SWB viram no clique, e o POWER liga e desliga o controle (com ele
/// desligado o receptor entra em failsafe). Tambem anda pelo teclado e pelo
/// controle de videogame (os vinculos da peca).
/// </summary>
public partial class EditorCircuito
{
    const double MmRadio = 96 / 25.4;

    /// <summary>As areas que o clique pega, em mm da peca (90 x 66).</summary>
    static readonly (double X, double Y, double R, string Nome)[] RegioesDoRadio =
    [
        (24.0, 38.0, 11.0, "manche-esq"),
        (66.0, 38.0, 11.0, "manche-dir"),
        (12.0, 9.5, 4.0, "SWA"),
        (78.0, 9.5, 4.0, "SWB"),
        (45.0, 56.5, 5.0, "liga"),
    ];

    /// <summary>Quanto o manche anda do centro ate o batente, em mm.</summary>
    const double CursoDoRadio = 8.0;

    /// <summary>O manche que o mouse esta segurando: a peca e o lado.</summary>
    (string Peca, string Lado)? mancheDoRadio;

    sealed class DesenhoDoRadio
    {
        public Ellipse[] Manches = new Ellipse[2];
        public Line[] Hastes = new Line[2];
        public Rectangle[] Chaves = new Rectangle[2];
        public Ellipse Luz;
        public TextBlock Tela;
    }

    readonly Dictionary<string, DesenhoDoRadio> desenhosDeRadio = [];

    string RegiaoDoRadio(PecaNoCircuito peca, PecaDef def, Point p)
    {
        if (peca.Tipo != "radio-controle") return null;
        var local = Geometria.PontoLocal(peca, def, p);
        foreach (var (x, y, r, nome) in RegioesDoRadio)
        {
            double dx = local.X - x * MmRadio, dy = local.Y - y * MmRadio;
            if (dx * dx + dy * dy <= r * r * MmRadio * MmRadio) return nome;
        }
        return null;
    }

    /// <summary>O clique numa area do controle.</summary>
    void ClicarNoRadio(PecaNoCircuito peca, string regiao)
    {
        switch (regiao)
        {
            case "manche-esq":
            case "manche-dir":
                mancheDoRadio = (peca.Id, regiao);
                CaptureMouse();
                break;
            case "SWA": peca.Bits ^= 1; break;
            case "SWB": peca.Bits ^= 2; break;
            case "liga": peca.Bits ^= 4; break;
        }
        AtualizarRadios();
    }

    /// <summary>O manche segue o mouse (Mover chama enquanto o botao esta apertado).</summary>
    bool MoverMancheDoRadio(Point p)
    {
        if (mancheDoRadio is not { } segurado) return false;
        var peca = Atual.Achar(segurado.Peca);
        var def = peca is null ? null : Catalogo.Obter(peca.Tipo);
        if (def is null) return true;

        var local = Geometria.PontoLocal(peca, def, p);
        double cx = (segurado.Lado == "manche-esq" ? 24.0 : 66.0) * MmRadio, cy = 38.0 * MmRadio;
        double x = Math.Clamp((local.X - cx) / (CursoDoRadio * MmRadio), -1, 1);
        double y = Math.Clamp(-(local.Y - cy) / (CursoDoRadio * MmRadio), -1, 1);
        if (segurado.Lado == "manche-esq") { peca.Giro4 = 0.5 + x / 2; peca.Giro = 0.5 + y / 2; }
        else { peca.Giro3 = 0.5 + x / 2; peca.Giro2 = 0.5 + y / 2; }
        AtualizarRadios();
        return true;
    }

    /// <summary>Soltou o manche: a mola traz de volta (menos o acelerador).</summary>
    bool SoltarMancheDoRadio()
    {
        if (mancheDoRadio is not { } segurado) return false;
        mancheDoRadio = null;
        ReleaseMouseCapture();
        if (Atual.Achar(segurado.Peca) is { } peca)
        {
            if (segurado.Lado == "manche-esq") peca.Giro4 = 0.5;
            else { peca.Giro3 = 0.5; peca.Giro2 = 0.5; }
        }
        AtualizarRadios();
        return true;
    }

    void CriarDesenhoDoRadio(PecaNoCircuito peca)
    {
        var d = new DesenhoDoRadio();
        for (int i = 0; i < 2; i++)
        {
            d.Hastes[i] = new Line
            {
                Stroke = new SolidColorBrush(Color.FromRgb(0x9A, 0xA0, 0xA8)),
                StrokeThickness = 4, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
                IsHitTestVisible = false,
            };
            d.Manches[i] = new Ellipse
            {
                Width = 7 * MmRadio, Height = 7 * MmRadio,
                Fill = new RadialGradientBrush(Color.FromRgb(0x6A, 0x6E, 0x76), Color.FromRgb(0x2A, 0x2C, 0x31)),
                Stroke = new SolidColorBrush(Color.FromRgb(0xC9, 0xCD, 0xD3)), StrokeThickness = 1.2,
                IsHitTestVisible = false,
            };
            d.Chaves[i] = new Rectangle
            {
                Width = 2.2 * MmRadio, Height = 4.2 * MmRadio, RadiusX = 2, RadiusY = 2,
                Fill = new SolidColorBrush(Color.FromRgb(0xE6, 0xE6, 0xE2)), IsHitTestVisible = false,
            };
        }
        d.Luz = new Ellipse { Width = 2.4 * MmRadio, Height = 2.4 * MmRadio, IsHitTestVisible = false };
        d.Tela = new TextBlock
        {
            FontFamily = new FontFamily("Consolas"), FontSize = 9.5,
            Foreground = new SolidColorBrush(Color.FromRgb(0xEA, 0xF4, 0xFF)), IsHitTestVisible = false,
        };
        foreach (UIElement e in new UIElement[] { d.Hastes[0], d.Hastes[1], d.Manches[0], d.Manches[1], d.Chaves[0], d.Chaves[1], d.Luz, d.Tela })
        {
            Panel.SetZIndex(e, ZDe(peca.Tipo) + 2);
            mundo.Children.Add(e);
        }
        desenhosDeRadio[peca.Id] = d;
        AtualizarRadio(peca);
    }

    void ApagarDesenhosDeRadio()
    {
        foreach (var d in desenhosDeRadio.Values)
            foreach (UIElement e in new UIElement[] { d.Hastes[0], d.Hastes[1], d.Manches[0], d.Manches[1], d.Chaves[0], d.Chaves[1], d.Luz, d.Tela })
                mundo.Children.Remove(e);
        desenhosDeRadio.Clear();
    }

    void AtualizarRadios()
    {
        foreach (var peca in Atual.Pecas)
            if (peca.Tipo == "radio-controle") AtualizarRadio(peca);
    }

    /// <summary>Os manches, as chaves, a luz e a tela do controle, pelo estado da peca.</summary>
    void AtualizarRadio(PecaNoCircuito peca)
    {
        if (!desenhosDeRadio.TryGetValue(peca.Id, out var d) || Catalogo.Obter(peca.Tipo) is not { } def) return;
        Point Local(double xmm, double ymm) => Geometria.PosicaoLocal(peca, def, xmm * MmRadio, ymm * MmRadio);

        (double X, double Y)[] manches =
        [
            (24.0 + (peca.Giro4 - 0.5) * 2 * CursoDoRadio, 38.0 - (peca.Giro - 0.5) * 2 * CursoDoRadio),
            (66.0 + (peca.Giro3 - 0.5) * 2 * CursoDoRadio, 38.0 - (peca.Giro2 - 0.5) * 2 * CursoDoRadio),
        ];
        for (int i = 0; i < 2; i++)
        {
            var centro = Local(i == 0 ? 24.0 : 66.0, 38.0);
            var ponta = Local(manches[i].X, manches[i].Y);
            d.Hastes[i].X1 = centro.X; d.Hastes[i].Y1 = centro.Y;
            d.Hastes[i].X2 = ponta.X; d.Hastes[i].Y2 = ponta.Y;
            Canvas.SetLeft(d.Manches[i], ponta.X - d.Manches[i].Width / 2);
            Canvas.SetTop(d.Manches[i], ponta.Y - d.Manches[i].Height / 2);
        }

        // SWA/SWB: para cima = 1000 us, para baixo = 2000 us (o de fabrica).
        for (int i = 0; i < 2; i++)
        {
            bool baixo = (peca.Bits & (1 << i)) != 0;
            var c = Local(i == 0 ? 12.0 : 78.0, baixo ? 10.6 : 8.4);
            Canvas.SetLeft(d.Chaves[i], c.X - d.Chaves[i].Width / 2);
            Canvas.SetTop(d.Chaves[i], c.Y - d.Chaves[i].Height / 2);
        }

        bool ligado = !ControleDesligado(peca);
        d.Luz.Fill = new SolidColorBrush(ligado ? Color.FromRgb(0x3C, 0xD0, 0x6A) : Color.FromRgb(0x3A, 0x1A, 0x1A));
        var luz = Local(45.0, 52.4);
        Canvas.SetLeft(d.Luz, luz.X - d.Luz.Width / 2);
        Canvas.SetTop(d.Luz, luz.Y - d.Luz.Height / 2);

        var canais = CanaisDoControle(peca);
        string texto = ligado
            ? $"1 {canais[0],4:0} 2 {canais[1],4:0}\n3 {canais[2],4:0} 4 {canais[3],4:0}\n5 {canais[4],4:0} 6 {canais[5],4:0}"
            : "\n  desligado";
        if (d.Tela.Text != texto) d.Tela.Text = texto;
        d.Tela.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var tela = Local(45.0, 16.5);
        Canvas.SetLeft(d.Tela, tela.X - d.Tela.DesiredSize.Width / 2);
        Canvas.SetTop(d.Tela, tela.Y - d.Tela.DesiredSize.Height / 2);
    }

    /// <summary>
    /// Os vinculos padrao do controle: os manches do controle de videogame nos
    /// do radio (modo 2) e os botoes A/B nas chaves. Teclado so se a pessoa
    /// escolher -- uma tecla padrao subiria o acelerador de quem digita no monitor.
    /// </summary>
    static readonly Dictionary<string, string> VinculosDoRadio = new()
    {
        [Vinculos.Mais] = "g:LSCima",
        [Vinculos.Menos] = "g:LSBaixo",
        ["leme-esq"] = "g:LSEsquerda",
        ["leme-dir"] = "g:LSDireita",
        [Vinculos.Esquerda] = "g:RSEsquerda",
        [Vinculos.Direita] = "g:RSDireita",
        [Vinculos.Cima] = "g:RSCima",
        [Vinculos.Baixo] = "g:RSBaixo",
        ["chave-a"] = "g:A",
        ["chave-b"] = "g:B",
    };

    readonly Dictionary<string, double> vinculoDoRadioAntes = [];

    /// <summary>
    /// O controle pelo teclado e pelo controle de videogame: o acelerador
    /// sobe e desce enquanto a tecla esta em baixo (e fica); o leme e o
    /// manche direito vao ate onde a tecla manda e voltam ao soltar.
    /// </summary>
    void AplicarVinculosDoRadio(PecaNoCircuito peca, EstadoDoControle controle, double segundos)
    {
        var mapa = peca.Teclas is { Count: > 0 } proprios ? proprios : VinculosDoRadio;
        double F(string acao) => mapa.TryGetValue(acao, out var v) ? Vinculos.Forca(v, controle) : 0;
        bool Ligou(string acao)
        {
            double agora = F(acao), antes = vinculoDoRadioAntes.GetValueOrDefault(peca.Id + acao);
            vinculoDoRadioAntes[peca.Id + acao] = agora;
            return agora > 0.5 && antes <= 0.5;
        }

        if (mancheDoRadio?.Peca == peca.Id) return;   // o mouse manda
        double acelera = F(Vinculos.Mais) - F(Vinculos.Menos);
        if (acelera != 0) peca.Giro = Math.Clamp(peca.Giro + acelera * segundos * 0.8, 0, 1);
        peca.Giro4 = 0.5 + (F("leme-dir") - F("leme-esq")) / 2;
        peca.Giro3 = 0.5 + (F(Vinculos.Direita) - F(Vinculos.Esquerda)) / 2;
        peca.Giro2 = 0.5 + (F(Vinculos.Cima) - F(Vinculos.Baixo)) / 2;
        if (Ligou("chave-a")) peca.Bits ^= 1;
        if (Ligou("chave-b")) peca.Bits ^= 2;
    }

    /// <summary>Para o teste: o canal que o controle manda (1..6), em us.</summary>
    internal double CanalDoControleParaTeste(string idDaPeca, int canal)
        => Atual.Achar(idDaPeca) is { } p ? CanaisDoControle(p)[canal - 1] : 0;
}
