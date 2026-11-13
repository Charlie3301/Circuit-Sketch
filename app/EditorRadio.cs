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

    /// <summary>
    /// As areas que o clique pega, em mm da peca: o FS-i6 de frente, 174 x 194
    /// mm (as medidas da foto do fabricante, as mesmas do pecas-rc-a25.py).
    /// </summary>
    static readonly (double X, double Y, double R, string Nome)[] RegioesDoRadio =
    [
        (33.5, 103.0, 22.0, "manche-esq"),
        (140.5, 103.0, 22.0, "manche-dir"),
        (21.5, 65.5, 5.0, "SWA"),
        (42.0, 61.5, 5.0, "SWB"),
        (134.9, 175.7, 8.0, "liga"),
    ];

    /// <summary>O centro de cada manche (o meio do gimbal), em mm.</summary>
    static readonly (double X, double Y) MancheEsq = (33.5, 103.0), MancheDir = (140.5, 103.0);

    /// <summary>Quanto o manche anda do centro ate o batente, em mm.</summary>
    const double CursoDoRadio = 11.0;

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
        var centro = segurado.Lado == "manche-esq" ? MancheEsq : MancheDir;
        double cx = centro.X * MmRadio, cy = centro.Y * MmRadio;
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
                Stroke = new SolidColorBrush(Color.FromRgb(0xB9, 0xBF, 0xC6)),
                StrokeThickness = 3.0 * MmRadio, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
                IsHitTestVisible = false,
            };
            // A ponta do manche do FS-i6: o cilindro de aluminio vermelho serrilhado (8 mm).
            d.Manches[i] = new Ellipse
            {
                Width = 8 * MmRadio, Height = 8 * MmRadio,
                Fill = new RadialGradientBrush(Color.FromRgb(0xE2, 0x4A, 0x44), Color.FromRgb(0x7A, 0x14, 0x12)),
                Stroke = new SolidColorBrush(Color.FromRgb(0xF2, 0xC9, 0xC6)), StrokeThickness = 1.2,
                IsHitTestVisible = false,
            };
            // A alavanca cromada da chave.
            d.Chaves[i] = new Rectangle
            {
                Width = 2.4 * MmRadio, Height = 5.2 * MmRadio, RadiusX = 3, RadiusY = 3,
                Fill = new LinearGradientBrush(Color.FromRgb(0xF4, 0xF6, 0xF8), Color.FromRgb(0x8A, 0x91, 0x99), 0),
                Stroke = new SolidColorBrush(Color.FromRgb(0x5D, 0x64, 0x6B)), StrokeThickness = 0.8,
                IsHitTestVisible = false,
            };
        }
        d.Luz = new Ellipse { Width = 2.4 * MmRadio, Height = 2.4 * MmRadio, IsHitTestVisible = false };
        d.Tela = new TextBlock
        {
            // o visor do FS-i6: o cristal liquido verde-claro com os caracteres escuros
            FontFamily = new FontFamily("Consolas"), FontSize = 17, FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x22, 0x33, 0x24)), IsHitTestVisible = false,
        };
        foreach (UIElement e in new UIElement[] { d.Hastes[0], d.Hastes[1], d.Manches[0], d.Manches[1], d.Chaves[0], d.Chaves[1], d.Luz, d.Tela })
        {
            PorNaCamadaDa(peca, e);
        }
        desenhosDeRadio[peca.Id] = d;
        AtualizarRadio(peca);
    }

    void ApagarDesenhosDeRadio()
    {
        foreach (var d in desenhosDeRadio.Values)
            foreach (UIElement e in new UIElement[] { d.Hastes[0], d.Hastes[1], d.Manches[0], d.Manches[1], d.Chaves[0], d.Chaves[1], d.Luz, d.Tela })
                TirarDaTela(e);
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
            (MancheEsq.X + (peca.Giro4 - 0.5) * 2 * CursoDoRadio, MancheEsq.Y - (peca.Giro - 0.5) * 2 * CursoDoRadio),
            (MancheDir.X + (peca.Giro3 - 0.5) * 2 * CursoDoRadio, MancheDir.Y - (peca.Giro2 - 0.5) * 2 * CursoDoRadio),
        ];
        for (int i = 0; i < 2; i++)
        {
            var centro = Local(i == 0 ? MancheEsq.X : MancheDir.X, i == 0 ? MancheEsq.Y : MancheDir.Y);
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
            var (cxm, cym) = i == 0 ? (21.5, 65.5) : (42.0, 61.5);
            var c = Local(cxm, cym + (baixo ? 2.6 : -2.6));
            Canvas.SetLeft(d.Chaves[i], c.X - d.Chaves[i].Width / 2);
            Canvas.SetTop(d.Chaves[i], c.Y - d.Chaves[i].Height / 2);
        }

        bool ligado = !ControleDesligado(peca);
        d.Luz.Fill = new SolidColorBrush(ligado ? Color.FromRgb(0x3C, 0xD0, 0x6A) : Color.FromRgb(0x3A, 0x1A, 0x1A));
        var luz = Local(134.9, 163.2);
        Canvas.SetLeft(d.Luz, luz.X - d.Luz.Width / 2);
        Canvas.SetTop(d.Luz, luz.Y - d.Luz.Height / 2);

        var canais = CanaisDoControle(peca);
        string texto = ligado
            ? $"1 {canais[0],4:0} 2 {canais[1],4:0}\n3 {canais[2],4:0} 4 {canais[3],4:0}\n5 {canais[4],4:0} 6 {canais[5],4:0}"
            : Idioma.T("\n  desligado");
        if (d.Tela.Text != texto) d.Tela.Text = texto;
        d.Tela.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var tela = Local(85.6, 163.3);
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

    /// <summary>Os controles com algum manche na tecla no quadro de antes.</summary>
    readonly HashSet<string> radiosNaTecla = [];

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
        // Os manches com mola: a tecla leva ate onde manda, e soltar traz ao
        // centro. Sem tecla nenhuma (nem agora nem no quadro de antes) o
        // manche fica onde o resto o pos -- o diagrama, o teste, o Wokwi.
        double leme = F("leme-dir") - F("leme-esq");
        double lados = F(Vinculos.Direita) - F(Vinculos.Esquerda);
        double frente = F(Vinculos.Cima) - F(Vinculos.Baixo);
        bool comTecla = leme != 0 || lados != 0 || frente != 0;
        if (comTecla || radiosNaTecla.Contains(peca.Id))
        {
            peca.Giro4 = 0.5 + leme / 2;
            peca.Giro3 = 0.5 + lados / 2;
            peca.Giro2 = 0.5 + frente / 2;
        }
        if (comTecla) radiosNaTecla.Add(peca.Id); else radiosNaTecla.Remove(peca.Id);
        if (Ligou("chave-a")) peca.Bits ^= 1;
        if (Ligou("chave-b")) peca.Bits ^= 2;
    }

    /// <summary>Para o teste: o canal que o controle manda (1..6), em us.</summary>
    internal double CanalDoControleParaTeste(string idDaPeca, int canal)
        => Atual.Achar(idDaPeca) is { } p ? CanaisDoControle(p)[canal - 1] : 0;
}
