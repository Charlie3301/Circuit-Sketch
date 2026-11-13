using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;

namespace Simulador.App;

/// <summary>
/// Uma base desenhada por quem usa: o SVG de corte que a pessoa importou
/// (a placa do robo dela). Vai no .sktch, dentro do circuito, para o projeto
/// abrir em outro computador com a base junto.
/// </summary>
public class BaseImportada
{
    /// <summary>O tipo da peca: "frame-svg-" e oito letras do SVG e da escala.</summary>
    public string Id { get; set; }

    /// <summary>O nome do arquivo, sem a extensao: e o nome da base na barra.</summary>
    public string Nome { get; set; }

    /// <summary>O SVG como veio.</summary>
    public string Svg { get; set; }

    /// <summary>Quanto o tamanho do arquivo foi multiplicado (1: o tamanho que o SVG diz).</summary>
    public double Escala { get; set; } = 1;
}

/// <summary>
/// Os frames da Alpha 25: a placa de acrilico do carrinho, o frame do drone,
/// a base do braco robotico e o suporte do servo -- e a base que a pessoa
/// importa de um SVG.
///
/// O contorno de cada um vem do arquivo de corte de um projeto aberto
/// (ferramentas/extrair-svg/pecas-frames-a25.py -> assets/frames.json), em
/// milimetros, e e ele que se desenha e que recebe o clique: o furo e furo,
/// o clique nele passa para o que esta embaixo. O material (o acrilico de
/// cada cor, o MDF, o compensado, a fibra de carbono) fica no Cor da peca.
///
/// Frame nao tem pino nem eletrica: e a base onde as pecas ficam. Arrastar o
/// frame leva junto o que esta em cima dele.
/// </summary>
public static class Frames
{
    public const string Prefixo = "frame-";
    public const string PrefixoImportado = "frame-svg-";

    /// <summary>Pixels de 96 dpi por milimetro.</summary>
    public const double A = 96 / 25.4;

    public enum Acabamento { Acrilico, Madeira, Carbono, Original }

    public sealed record Material(string Id, Color Cor, double Opacidade, Acabamento Acabamento);

    /// <summary>Os materiais, na ordem da barra.</summary>
    public static readonly Material[] Materiais =
    [
        new("acrilico", Color.FromRgb(0xD8, 0xEC, 0xF2), 0.34, Acabamento.Acrilico),
        new("acrilico-preto", Color.FromRgb(0x16, 0x16, 0x19), 0.93, Acabamento.Acrilico),
        new("acrilico-azul", Color.FromRgb(0x1F, 0x66, 0xC9), 0.58, Acabamento.Acrilico),
        new("acrilico-vermelho", Color.FromRgb(0xC9, 0x25, 0x2B), 0.58, Acabamento.Acrilico),
        new("acrilico-amarelo", Color.FromRgb(0xF4, 0xC2, 0x1B), 0.62, Acabamento.Acrilico),
        new("acrilico-verde", Color.FromRgb(0x17, 0x9A, 0x4B), 0.58, Acabamento.Acrilico),
        new("acrilico-laranja", Color.FromRgb(0xF2, 0x6B, 0x1D), 0.64, Acabamento.Acrilico),
        new("mdf", Color.FromRgb(0xA4, 0x7A, 0x4E), 1, Acabamento.Madeira),
        new("compensado", Color.FromRgb(0xE2, 0xC0, 0x8D), 1, Acabamento.Madeira),
        new("carbono", Color.FromRgb(0x26, 0x28, 0x2C), 1, Acabamento.Carbono),
        new("original", Colors.Transparent, 1, Acabamento.Original),
    ];

    /// <summary>O nome do material, no idioma de agora.</summary>
    public static string NomeDoMaterial(string id) => id switch
    {
        "acrilico" => Idioma.T("Acrílico transparente"),
        "acrilico-preto" => Idioma.T("Acrílico preto"),
        "acrilico-azul" => Idioma.T("Acrílico azul"),
        "acrilico-vermelho" => Idioma.T("Acrílico vermelho"),
        "acrilico-amarelo" => Idioma.T("Acrílico amarelo"),
        "acrilico-verde" => Idioma.T("Acrílico verde"),
        "acrilico-laranja" => Idioma.T("Acrílico laranja"),
        "mdf" => Idioma.T("MDF cru"),
        "compensado" => Idioma.T("Compensado"),
        "carbono" => Idioma.T("Fibra de carbono"),
        "original" => Idioma.T("Como no SVG"),
        _ => id,
    };

    /// <summary>Um frame: o contorno em mm (com os furos) e o material de fabrica.</summary>
    public sealed class Frame
    {
        public string Id { get; init; }
        public string Tipo { get; init; }
        public string Material { get; init; }
        public double LarguraMm { get; init; }
        public double AlturaMm { get; init; }

        /// <summary>O contorno com os furos (EvenOdd), em mm, congelado.</summary>
        public Geometry Contorno { get; init; }

        /// <summary>Cada figura sozinha, cheia: o que esta dentro de alguma esta em cima do frame.</summary>
        public IReadOnlyList<Geometry> Cascos { get; init; }

        /// <summary>A base importada que deu origem a este (nulo nos de fabrica).</summary>
        public BaseImportada Base { get; init; }
    }

    static readonly Dictionary<string, Frame> frames = [];
    static readonly List<string> deFabrica = [];

    static Frames()
    {
        var assembly = typeof(Frames).Assembly;
        string recurso = assembly.GetManifestResourceNames()
            .FirstOrDefault(r => r.EndsWith(".assets.frames.json", StringComparison.OrdinalIgnoreCase));
        if (recurso is null) return;

        using var fluxo = assembly.GetManifestResourceStream(recurso);
        using var leitor = new StreamReader(fluxo);
        using var doc = JsonDocument.Parse(leitor.ReadToEnd());
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            var mm = item.GetProperty("mm");
            var contorno = Geometry.Parse(item.GetProperty("caminho").GetString());
            var frame = new Frame
            {
                Id = item.GetProperty("id").GetString(),
                Tipo = item.GetProperty("tipo").GetString(),
                Material = item.GetProperty("material").GetString(),
                LarguraMm = mm[0].GetDouble(),
                AlturaMm = mm[1].GetDouble(),
                Contorno = Congelar(contorno),
                Cascos = CascosDe(contorno),
            };
            frames[frame.Id] = frame;
            deFabrica.Add(frame.Id);
        }
    }

    /// <summary>Os frames que vem com o programa, na ordem do frames.json.</summary>
    public static IReadOnlyList<string> DeFabrica => deFabrica;

    public static bool EhFrame(string id) => id is not null && id.StartsWith(Prefixo, StringComparison.Ordinal);

    public static bool EhImportado(string id) => id is not null && id.StartsWith(PrefixoImportado, StringComparison.Ordinal);

    public static Frame Obter(string id)
    {
        if (id is null) return null;
        lock (frames) return frames.TryGetValue(id, out var f) ? f : null;
    }

    /// <summary>A peca do catalogo: sem pinos, do tamanho da placa.</summary>
    public static PecaDef DefDe(Frame f)
        => new(f.Id, NomeDe(f), f.LarguraMm * A, f.AlturaMm * A, [], "Frames");

    static string NomeDe(Frame f) => f.Base is { } b
        ? Idioma.T("Base importada: {0}", b.Nome)
        : Idioma.T(Apresentar(f.Id));

    static string Apresentar(string id) => id switch
    {
        "frame-2wd" => Idioma.N("Chassi 2WD de carrinho"),
        "frame-4wd" => Idioma.N("Chassi 4WD de carrinho (250 × 150 mm)"),
        "frame-4wd-placa" => Idioma.N("Placa de chassi 4WD (203 × 305 mm)"),
        "frame-drone-5" => Idioma.N("Frame de drone de 5 polegadas (220 mm)"),
        "frame-drone-6" => Idioma.N("Frame de drone de 6 polegadas"),
        "frame-braco-mearm" => Idioma.N("Base de braço robótico (MeArm)"),
        "frame-braco-dearm" => Idioma.N("Base de braço robótico (DE-Arm)"),
        "frame-braco-servo" => Idioma.N("Placa do servo do braço robótico"),
        "frame-suporte-sg90" => Idioma.N("Suporte de servo SG90"),
        _ => id,
    };

    /// <summary>O material da peca: o que esta no Cor, ou o de fabrica do frame.</summary>
    public static Material MaterialDe(PecaNoCircuito peca)
    {
        var frame = Obter(peca?.Tipo);
        var escolhido = Materiais.FirstOrDefault(m => m.Id == peca?.Cor);
        if (escolhido is not null && (escolhido.Acabamento != Acabamento.Original || frame?.Base is not null)) return escolhido;
        return Materiais.FirstOrDefault(m => m.Id == frame?.Material) ?? Materiais[0];
    }

    /// <summary>Os materiais que este frame aceita (o "como no SVG" e so da base importada).</summary>
    public static IEnumerable<Material> MateriaisDe(string id)
        => Materiais.Where(m => m.Acabamento != Acabamento.Original || EhImportado(id));

    // ---------------- o clique ----------------

    /// <summary>O ponto (em px, no espaco da peca em pe) cai na placa, e nao num furo nem fora?</summary>
    public static bool NaPlaca(string id, Point local)
        => Obter(id) is { } f && f.Contorno.FillContains(new Point(local.X / A, local.Y / A));

    /// <summary>O ponto (em px, no espaco da peca em pe) esta dentro do contorno de fora (furo conta)?</summary>
    public static bool DentroDoContorno(string id, Point local)
    {
        if (Obter(id) is not { } f) return false;
        var mm = new Point(local.X / A, local.Y / A);
        foreach (var casco in f.Cascos)
            if (casco.FillContains(mm)) return true;
        return false;
    }

    // ---------------- a base importada ----------------

    /// <summary>O id da base: o mesmo SVG na mesma escala e a mesma peca.</summary>
    public static string IdDa(string svg, double escala)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(svg + "|" + escala.ToString("R", CultureInfo.InvariantCulture)));
        return PrefixoImportado + Convert.ToHexString(hash, 0, 4).ToLowerInvariant();
    }

    /// <summary>O tamanho, em mm, que o SVG diz que tem (nulo quando nao da para ler).</summary>
    public static (double larg, double alt)? TamanhoDoSvg(string svg)
        => Pecas.ViewportDoSvg(svg) is { } v ? (v.larg / A, v.alt / A) : null;

    /// <summary>
    /// Poe a base no catalogo (e o desenho dela nas Pecas). Falso quando o
    /// SVG nao se le. Registrar de novo a mesma base nao muda nada.
    /// </summary>
    public static bool Registrar(BaseImportada b)
    {
        if (b is null || string.IsNullOrEmpty(b.Svg) || !EhImportado(b.Id)) return false;
        if (Obter(b.Id) is { } ja && ja.Base?.Svg == b.Svg) { Catalogo.Registrar(DefDe(ja)); return true; }

        if (Pecas.LerParaFrame(b.Svg) is not { } lido) return false;
        double escala = b.Escala > 0 && double.IsFinite(b.Escala) ? b.Escala : 1;
        double fator = escala / A;   // px do SVG -> mm

        var figuras = FigurasFechadas(lido.desenho);
        Geometry contorno;
        if (figuras.Count == 0)
            contorno = new RectangleGeometry(new Rect(0, 0, lido.larg * fator, lido.alt * fator));
        else
        {
            var g = new StreamGeometry { FillRule = FillRule.EvenOdd };
            using (var c = g.Open())
                foreach (var fig in figuras)
                {
                    c.BeginFigure(new Point(fig[0].X * fator, fig[0].Y * fator), true, true);
                    c.PolyLineTo([.. fig.Skip(1).Select(p => new Point(p.X * fator, p.Y * fator))], true, false);
                }
            contorno = g;
        }

        var frame = new Frame
        {
            Id = b.Id,
            Tipo = "importado",
            Material = "acrilico",
            LarguraMm = lido.larg * fator,
            AlturaMm = lido.alt * fator,
            Contorno = Congelar(contorno),
            Cascos = CascosDe(contorno),
            Base = b,
        };
        lock (frames) frames[b.Id] = frame;
        Pecas.DefinirDesenho(b.Id, b.Svg);
        Catalogo.Registrar(DefDe(frame));
        return true;
    }

    /// <summary>As bases de um circuito (as do .sktch, as do historico): todas no catalogo.</summary>
    public static void RegistrarDo(Circuito circuito)
    {
        if (circuito?.Bases is not { Count: > 0 } bases) return;
        foreach (var b in bases) Registrar(b);
    }

    /// <summary>
    /// As figuras fechadas do desenho, em px do SVG. O SVG de corte e feito
    /// de linhas (o contorno e os furos, sem preenchimento): cada caminho
    /// fechado vira uma figura, e a regra par-impar decide o que e furo.
    /// </summary>
    static List<List<Point>> FigurasFechadas(Drawing desenho)
    {
        var saida = new List<List<Point>>();
        Juntar(desenho, Matrix.Identity, saida);

        // O mesmo caminho duas vezes (o preenchimento e a linha em desenhos
        // separados) se anularia no par-impar: fica um so.
        var unicas = new List<List<Point>>();
        foreach (var f in saida)
            if (!unicas.Any(u => Iguais(u, f))) unicas.Add(f);
        return unicas;

        static bool Iguais(List<Point> a, List<Point> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
                if (Math.Abs(a[i].X - b[i].X) > 0.01 || Math.Abs(a[i].Y - b[i].Y) > 0.01) return false;
            return true;
        }
    }

    static void Juntar(Drawing d, Matrix m, List<List<Point>> saida)
    {
        switch (d)
        {
            case DrawingGroup g:
                var mg = g.Transform is { } t ? t.Value * m : m;
                foreach (var filho in g.Children) Juntar(filho, mg, saida);
                break;

            case GeometryDrawing gd when gd.Geometry is not null:
            {
                // O retangulo invisivel que marca a viewport nao e da peca.
                bool semTinta = gd.Brush is null || (gd.Brush is SolidColorBrush s && s.Color.A == 0);
                if (gd.Pen is null && semTinta) break;

                var geometria = gd.Geometry.CloneCurrentValue();
                var propria = geometria.Transform?.Value ?? Matrix.Identity;
                geometria.Transform = null;
                var total = propria * m;
                var plana = geometria.GetFlattenedPathGeometry(0.02, ToleranceType.Absolute);
                foreach (var figura in plana.Figures)
                {
                    var pontos = new List<Point> { figura.StartPoint };
                    foreach (var seg in figura.Segments)
                        switch (seg)
                        {
                            case LineSegment l: pontos.Add(l.Point); break;
                            case PolyLineSegment pl: pontos.AddRange(pl.Points); break;
                        }
                    if (pontos.Count < 3) continue;
                    bool fechada = figura.IsClosed || (pontos[0] - pontos[^1]).Length < 0.05;
                    if (!fechada) continue;
                    for (int i = 0; i < pontos.Count; i++) pontos[i] = total.Transform(pontos[i]);
                    if (Math.Abs(Area(pontos)) < 0.5) continue;
                    saida.Add(pontos);
                }
                break;
            }
        }
    }

    static double Area(List<Point> p)
    {
        double s = 0;
        for (int i = 0, j = p.Count - 1; i < p.Count; j = i++) s += (p[j].X - p[i].X) * (p[j].Y + p[i].Y);
        return s / 2;
    }

    static Geometry Congelar(Geometry g)
    {
        if (g.CanFreeze) g.Freeze();
        return g;
    }

    /// <summary>Cada figura do contorno sozinha e cheia (o furo dela nao conta).</summary>
    static List<Geometry> CascosDe(Geometry contorno)
    {
        var lista = new List<Geometry>();
        var caminho = PathGeometry.CreateFromGeometry(contorno);
        foreach (var figura in caminho.Figures)
        {
            var uma = new PathGeometry([figura.Clone()]) { FillRule = FillRule.Nonzero };
            uma.Freeze();
            lista.Add(uma);
        }
        return lista;
    }
}
