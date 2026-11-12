using System.Windows;

namespace Simulador.App;

/// <summary>
/// As baterias (Alpha 25). Tres familias, todas com o "+" e o "-":
///
///  - a bateria personalizada ("bateria"): o desenho generico de antes, com
///    a tensao, a corrente e a capacidade livres. E nela que os circuitos
///    salvos antes da Alpha 25 abrem, com os mesmos numeros;
///  - os pacotes prontos (a 9 V, as LiPo de 1S a 6S, o power bank, a de
///    chumbo): cada um com o seu desenho e os numeros de fabrica, que ainda
///    da para mudar na barra;
///  - os suportes de pilhas, com slots: AA, AAA, 18650 e CR2032. O tipo
///    diz o formato e quantos slots ("suporte-aa-4"); o Modelo, qual pilha
///    (alcalina, NiMH...); os Bits, os slots vazios. As pilhas ficam em
///    serie: a tensao e a soma, a capacidade e a corrente sao as de uma
///    pilha, e um slot vazio abre o circuito.
///
/// O leiaute dos suportes mora aqui (fora da interface) porque o catalogo
/// tira os pinos dele e o desenho, os slots: os dois do mesmo lugar.
/// </summary>
public static class Baterias
{
    const double Mm = 96 / 25.4;

    // =====================================================================
    //  os pacotes prontos
    // =====================================================================

    /// <summary>Uma bateria pronta: os numeros de fabrica (V, mA maximos, mAh); o Curto e o da barra.</summary>
    public sealed record Pacote(string Tipo, string Nome, double Volts, double MiliAmperes, double MiliAmperesHora, string Sobre)
    {
        public string Curto => Nome[..Nome.IndexOf(' ', Nome.StartsWith("LiPo", StringComparison.Ordinal) ? 5 : 0)] switch
        {
            "Bateria" => Tipo == "bateria-9v" ? "9 V" : Idioma.N("Chumbo 12 V"),
            "Power" => "Power bank",
            var curto => curto,
        };
    }

    public static readonly Pacote[] Pacotes =
    [
        new("bateria-9v", Idioma.N("Bateria 9 V (6F22)"), 9.0, 500, 550,
            Idioma.T("A alcalina retangular com clip: ~550 mAh, ate ~0,5 A. Boa para o Arduino parado; fraca para motor.")),
        new("lipo-1s-500", Idioma.N("LiPo 1S 3,7 V 500 mAh"), 3.7, 1000, 500,
            Idioma.T("A LiPo pequena de uma celula (503035), com a placa de protecao e o conector JST-PH: a das placas com carregador.")),
        new("lipo-2s-2200", Idioma.N("LiPo 2S 7,4 V 2200 mAh"), 7.4, 55000, 2200,
            Idioma.T("Duas celulas, 25C (ate 55 A), conector XT60 e o plugue de balanceamento.")),
        new("lipo-3s-2200", Idioma.N("LiPo 3S 11,1 V 2200 mAh"), 11.1, 66000, 2200,
            Idioma.T("Tres celulas, 30C (ate 66 A): a do A2212 com a helice 1045.")),
        new("lipo-4s-1500", Idioma.N("LiPo 4S 14,8 V 1500 mAh"), 14.8, 150000, 1500,
            Idioma.T("Quatro celulas, 100C (ate 150 A): a do drone de 5 polegadas.")),
        new("lipo-6s-1300", Idioma.N("LiPo 6S 22,2 V 1300 mAh"), 22.2, 130000, 1300,
            Idioma.T("Seis celulas, 100C (ate 130 A): a dos drones de 6S.")),
        new("power-bank", "Power bank 5 V 10000 mAh", 5.0, 2100, 6500,
            Idioma.T("A saida USB de 5 V, ate 2,1 A. Os 10000 mAh sao das celulas de 3,7 V: a 5 V rendem uns 6500 mAh.")),
        new("bateria-chumbo-12v", Idioma.N("Bateria de chumbo 12 V 7 Ah"), 12.0, 20000, 7000,
            Idioma.T("A selada (VRLA) de alarme e nobreak: pesada, barata, aguenta uns 20 A.")),
    ];

    public static Pacote PacoteDe(string tipo) => Array.Find(Pacotes, p => p.Tipo == tipo);

    // =====================================================================
    //  as pilhas e os suportes
    // =====================================================================

    /// <summary>Uma pilha: a tensao nominal, a corrente que aguenta (mA) e a capacidade (mAh).</summary>
    public sealed record Pilha(string Id, string Nome, double Volts, double MiliAmperes, double MiliAmperesHora);

    /// <summary>
    /// Um formato de pilha: as medidas (mm), a distancia entre os slots, as
    /// quantidades de slot que existem a venda e as pilhas que cabem.
    /// </summary>
    public sealed record Formato(string Id, string Nome, double Diametro, double Comprimento, double Passo,
                                 int[] Slots, int SlotsPadrao, Pilha[] Pilhas);

    public static readonly Formato[] Formatos =
    [
        new("aa", "AA", 14.5, 50.5, 15.6, [1, 2, 3, 4, 6, 8], 4,
        [
            new("alcalina", Idioma.T("Alcalina 1,5 V"), 1.5, 1000, 2500),
            new("nimh", Idioma.T("NiMH recarregável 1,2 V"), 1.2, 4000, 2000),
        ]),
        new("aaa", "AAA", 10.5, 44.5, 11.6, [1, 2, 3, 4], 4,
        [
            new("alcalina", Idioma.T("Alcalina 1,5 V"), 1.5, 500, 1100),
            new("nimh", Idioma.T("NiMH recarregável 1,2 V"), 1.2, 2000, 800),
        ]),
        new("18650", "18650", 18.6, 65.2, 20.6, [1, 2, 3, 4], 2,
        [
            new("comum", Idioma.T("Li-ion 3,7 V 2600 mAh"), 3.7, 5000, 2600),
            new("alta-descarga", Idioma.T("Li-ion 3,6 V 3000 mAh (alta descarga, 15 A)"), 3.6, 15000, 3000),
        ]),
        new("cr2032", "CR2032", 20.0, 3.2, 0, [1], 1,
        [
            new("litio", Idioma.T("Lítio 3 V 220 mAh"), 3.0, 20, 220),
        ]),
    ];

    public static Formato FormatoDe(string id) => Array.Find(Formatos, f => f.Id == id);

    /// <summary>O tipo do suporte: "suporte-aa-4", "suporte-cr2032".</summary>
    public static string TipoDoSuporte(string formato, int slots)
        => formato == "cr2032" ? "suporte-cr2032" : $"suporte-{formato}-{slots}";

    /// <summary>O formato e os slots de um tipo de suporte; nulo se nao e suporte.</summary>
    public static (Formato Formato, int Slots)? SuporteDe(string tipo)
    {
        if (tipo is null || !tipo.StartsWith("suporte-", StringComparison.Ordinal)) return null;
        if (tipo == "suporte-cr2032") return (FormatoDe("cr2032"), 1);
        int traco = tipo.LastIndexOf('-');
        var formato = FormatoDe(tipo[8..traco]);
        return formato is not null && formato.Id != "cr2032" && int.TryParse(tipo[(traco + 1)..], out int n)
               && formato.Slots.Contains(n) ? (formato, n) : null;
    }

    public static bool EhSuporte(string tipo) => SuporteDe(tipo) is not null;

    /// <summary>Toda bateria: a personalizada, os pacotes e os suportes.</summary>
    public static bool EhBateria(string tipo) => tipo == "bateria" || PacoteDe(tipo) is not null || EhSuporte(tipo);

    /// <summary>A pilha que esta no suporte (a escolhida, ou a primeira do formato).</summary>
    public static Pilha PilhaDe(PecaNoCircuito peca)
        => SuporteDe(peca.Tipo) is { } s ? Array.Find(s.Formato.Pilhas, p => p.Id == peca.Modelo) ?? s.Formato.Pilhas[0] : null;

    /// <summary>O slot k esta com a pilha? (O bit ligado e o slot vazio: o padrao, 0, e cheio.)</summary>
    public static bool TemPilha(PecaNoCircuito peca, int slot) => (peca.Bits & (1 << slot)) == 0;

    // =====================================================================
    //  o que a bateria entrega
    // =====================================================================

    /// <summary>A bateria vista do circuito; Aberta e o suporte com slot vazio (nao liga nada).</summary>
    public readonly record struct Ficha(double Volts, double MiliAmperes, double MiliAmperesHora, bool Aberta);

    public static Ficha FichaDe(PecaNoCircuito peca)
    {
        if (SuporteDe(peca.Tipo) is not { } s) return new(peca.Volts, peca.MiliAmperes, peca.MiliAmperesHora, false);
        var pilha = PilhaDe(peca);
        int cheios = Enumerable.Range(0, s.Slots).Count(k => TemPilha(peca, k));
        return new(pilha.Volts * cheios, pilha.MiliAmperes, pilha.MiliAmperesHora, cheios < s.Slots);
    }

    /// <summary>Os numeros de fabrica de um tipo (nulo: nao e bateria pronta nem suporte).</summary>
    public static (double Volts, double MiliAmperes, double MiliAmperesHora)? DeFabrica(string tipo)
    {
        if (PacoteDe(tipo) is { } p) return (p.Volts, p.MiliAmperes, p.MiliAmperesHora);
        if (SuporteDe(tipo) is { } s)
        {
            var pilha = s.Formato.Pilhas[0];
            return (pilha.Volts * s.Slots, pilha.MiliAmperes, pilha.MiliAmperesHora);
        }
        return null;
    }

    /// <summary>
    /// Copia o que a bateria entrega para os numeros da peca: e o que o
    /// arquivo guarda, e o que quem le Volts direto (o aviso do motor) ve.
    /// </summary>
    public static void Sincronizar(PecaNoCircuito peca)
    {
        if (!EhSuporte(peca.Tipo)) return;
        var f = FichaDe(peca);
        var pilha = PilhaDe(peca);
        peca.Volts = pilha.Volts * SuporteDe(peca.Tipo)!.Value.Slots;
        peca.MiliAmperes = f.MiliAmperes;
        peca.MiliAmperesHora = f.MiliAmperesHora;
    }

    // =====================================================================
    //  o suporte por dentro: a caixa, os slots e os fios
    // =====================================================================

    /// <summary>
    /// Onde fica cada coisa do suporte, em pixels da peca. As pilhas deitadas
    /// (o eixo na horizontal), uma em cima da outra; com 6 e 8, duas colunas
    /// de ponta a ponta, como nos suportes de 8 AA. Os fios saem pela direita.
    /// </summary>
    public sealed record Leiaute(double Largura, double Altura, Rect Caixa, Rect[] Slots, bool[] MaisNaDireita,
                                 Point SaidaDoMais, Point SaidaDoMenos, Point Mais, Point Menos, double Parede);

    static readonly Dictionary<string, Leiaute> leiautes = [];

    public static Leiaute LeiauteDe(string tipo)
    {
        if (SuporteDe(tipo) is not { } s) return null;
        lock (leiautes)
        {
            if (leiautes.TryGetValue(tipo, out var pronto)) return pronto;
            return leiautes[tipo] = Montar(s.Formato, s.Slots);
        }
    }

    static Leiaute Montar(Formato f, int n)
    {
        const double parede = 1.4, fio = 11.0;
        Rect caixa;
        var slots = new List<Rect>();
        var mais = new List<bool>();
        if (f.Id == "cr2032")
        {
            // A caixinha preta com o furo redondo da moeda.
            caixa = new Rect(0.4, 0.4, 25.0, 24.0);
            double r = f.Diametro / 2;
            slots.Add(new Rect(caixa.X + caixa.Width / 2 - r - 0.6, caixa.Y + caixa.Height / 2 - r, 2 * r, 2 * r));
            mais.Add(true);
        }
        else
        {
            int colunas = n <= 4 ? 1 : 2, linhas = n / colunas;
            double comprimento = f.Comprimento + 5.6;          // a mola de um lado, a placa do outro
            caixa = new Rect(0.4, 0.4, parede * 2 + colunas * comprimento + (colunas - 1) * parede,
                             parede * 2 + linhas * f.Passo);
            // Em serie, em zigue-zague: desce a primeira coluna, sobe a segunda.
            for (int k = 0; k < n; k++)
            {
                int c = k / linhas, l = c == 0 ? k % linhas : linhas - 1 - k % linhas;
                slots.Add(new Rect(caixa.X + parede + c * (comprimento + parede), caixa.Y + parede + l * f.Passo,
                                   comprimento, f.Passo));
                mais.Add(k % 2 == 0);
            }
        }
        double meio = caixa.Y + caixa.Height / 2;
        var saidaMais = new Point(caixa.Right, meio - 1.1);
        var saidaMenos = new Point(caixa.Right, meio + 1.1);
        double largura = caixa.Right + fio + 0.4, altura = caixa.Bottom + 0.4;
        Point P(Point p) => new(p.X * Mm, p.Y * Mm);
        Rect R(Rect r) => new(r.X * Mm, r.Y * Mm, r.Width * Mm, r.Height * Mm);
        return new Leiaute(Math.Round(largura * Mm, 2), Math.Round(altura * Mm, 2), R(caixa), [.. slots.Select(R)], [.. mais],
                           P(saidaMais), P(saidaMenos),
                           P(new Point(caixa.Right + fio, meio - 1.27)), P(new Point(caixa.Right + fio, meio + 1.27)), parede * Mm);
    }

    /// <summary>A peca do catalogo de um suporte: o tamanho e os dois pinos, na ponta dos fios.</summary>
    public static PecaDef DefDoSuporte(string tipo)
    {
        if (SuporteDe(tipo) is not { } s || LeiauteDe(tipo) is not { } l) return null;
        string nome = s.Formato.Id == "cr2032" ? Idioma.T("Suporte de bateria CR2032")
                    : Idioma.T("Suporte de pilhas {0} ({1}×)", s.Formato.Nome, s.Slots);
        return new PecaDef(tipo, nome, l.Largura, l.Altura,
        [
            new PinoDef("+", Math.Round(l.Mais.X, 2), Math.Round(l.Mais.Y, 2), ["power"], Idioma.T("Fio vermelho: o positivo das pilhas em serie")),
            new PinoDef("-", Math.Round(l.Menos.X, 2), Math.Round(l.Menos.Y, 2), ["power"], Idioma.T("Fio preto: o negativo (terra)")),
        ], "Alimentacao");
    }

    /// <summary>Os suportes que aparecem na lista de componentes (os outros, pela barra).</summary>
    public static readonly string[] SuportesNaPaleta = ["suporte-aa-4", "suporte-aaa-4", "suporte-18650-2", "suporte-cr2032"];

    /// <summary>Qual slot esta sob o ponto (em pixels da peca), ou -1.</summary>
    public static int SlotNoPonto(string tipo, Point local)
    {
        if (LeiauteDe(tipo) is not { } l) return -1;
        for (int k = 0; k < l.Slots.Length; k++)
            if (l.Slots[k].Contains(local)) return k;
        return -1;
    }
}
