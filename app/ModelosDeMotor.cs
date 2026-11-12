namespace Simulador.App;

/// <summary>
/// A ficha de um motor como a loja escreve: tensao, rpm sem carga (ou KV, no
/// sem escova), resistencia, corrente sem carga, corrente maxima -- e as
/// medidas. E dela que sai o <see cref="ModeloDeMotor"/> (a mesma fisica do
/// 130 e do A2212, com os numeros do motor escolhido) e o tamanho do desenho.
///
/// Os valores sao os tipicos dos catalogos (Mabuchi e os sem escova de
/// aeromodelo/drone mais vendidos); cada fabricante varia um pouco, e por isso
/// o "personalizado" deixa a pessoa digitar a ficha do motor que tem na mao.
/// </summary>
/// <param name="RpmOuKv">Com escova: rpm sem carga na tensao da ficha. Sem escova: KV (rpm por volt).</param>
/// <param name="Resistencia">Ohms entre os dois fios (com escova) ou entre duas fases (sem escova).</param>
/// <param name="CorrenteVazio">Amperes sem carga (sem escova: medida com 10 V).</param>
/// <param name="CorrenteMaxima">Amperes que ele aguenta trabalhando (o "max current" da ficha).</param>
/// <param name="Inercia">kg.m2 do rotor.</param>
/// <param name="MedidaA">Com escova: o diametro da lata (mm). Sem escova: o diametro do estator (os "22" do 2205).</param>
/// <param name="MedidaB">Com escova: o comprimento da lata (mm). Sem escova: a altura do estator (os "05" do 2205).</param>
public sealed record FichaDeMotor(string Id, string Nome, bool SemEscova, double Volts, double RpmOuKv,
                                  double Resistencia, double CorrenteVazio, double CorrenteMaxima,
                                  double Inercia, double MedidaA, double MedidaB, int ParesDePolos = 7)
{
    /// <summary>O motor de livro com os numeros desta ficha.</summary>
    public ModeloDeMotor Modelo()
    {
        double r = Math.Max(0.002, Resistencia);
        double ke, w0;
        if (SemEscova)
        {
            ke = 60 / (2 * Math.PI * Math.Max(1, RpmOuKv));
            w0 = Math.Max(1, RpmOuKv) * 10 * 2 * Math.PI / 60;     // a corrente sem carga e medida com 10 V
        }
        else
        {
            w0 = Math.Max(1, RpmOuKv) * 2 * Math.PI / 60;
            ke = Math.Max(1e-5, (Volts - CorrenteVazio * r) / w0);
        }

        // A corrente sem carga e o atrito: uma parte seca (rolamento, escovas)
        // e uma que cresce com a velocidade (o ar, as perdas no ferro) -- a
        // mesma divisao medida no 130 e no A2212.
        double torqueVazio = ke * Math.Max(0, CorrenteVazio);
        double seco = torqueVazio * (SemEscova ? 0.17 : 0.75);
        double viscoso = torqueVazio * (SemEscova ? 0.83 : 0.25) / w0;
        return new ModeloDeMotor(
            Nome, r, ke, ke, Math.Max(1e-8, Inercia), seco, seco * (SemEscova ? 1.9 : 3.0), viscoso,
            Volts, SemEscova ? 2.2 : 5.0, CorrenteMaxima, ParesDePolos);
    }

    /// <summary>A linha que aparece na escolha: "RS-540 · 7,2 V · 23400 rpm · 12 A".</summary>
    public string Resumo => SemEscova
        ? $"{Nome} · {CorrenteMaxima:0} A max."
        : $"{Nome} · {Volts:0.#} V · {RpmOuKv:0} rpm · {CorrenteMaxima:0.#} A";

    /// <summary>
    /// O diametro que se ve no desenho: a lata (com escova, vista pela ponta do
    /// eixo) ou o sino (sem escova, visto de cima). O sino e o estator mais o
    /// ima e a parede: 2205 da ~27,6 mm, 1105 ~15 mm, 2814 ~34 mm, como medidos.
    /// </summary>
    public double DiametroVisivel => SemEscova ? MedidaA * 1.12 + 3.0 : MedidaA;
}

/// <summary>A ficha que a pessoa digitou ("personalizado"), guardada no projeto.</summary>
public sealed class FichaPersonalizada
{
    public double Volts { get; set; }
    public double RpmOuKv { get; set; }
    public double Resistencia { get; set; }
    public double CorrenteVazio { get; set; }
    public double CorrenteMaxima { get; set; }
    public double Inercia { get; set; }
    public double MedidaA { get; set; }
    public double MedidaB { get; set; }
    public int ParesDePolos { get; set; } = 7;

    public FichaPersonalizada Copia() => (FichaPersonalizada)MemberwiseClone();

    public static FichaPersonalizada De(FichaDeMotor f) => new()
    {
        Volts = f.Volts, RpmOuKv = f.RpmOuKv, Resistencia = f.Resistencia, CorrenteVazio = f.CorrenteVazio,
        CorrenteMaxima = f.CorrenteMaxima, Inercia = f.Inercia, MedidaA = f.MedidaA, MedidaB = f.MedidaB,
        ParesDePolos = f.ParesDePolos,
    };

    public FichaDeMotor Ficha(bool semEscova)
    {
        // Ficha antiga (sem medidas): o tamanho do motor padrao do tipo.
        double a = MedidaA > 0 ? MedidaA : semEscova ? 22 : 20;
        double b = MedidaB > 0 ? MedidaB : semEscova ? 12 : 25;
        return new("personalizado", Idioma.N("motor personalizado"), semEscova, Volts, RpmOuKv,
            Resistencia, CorrenteVazio, CorrenteMaxima, Inercia, a, b, Math.Max(1, ParesDePolos));
    }
}

public static class FichasDeMotor
{
    /// <summary>Os de escovas que cabem no corpo do "motor DC" (o 130 e o padrao).</summary>
    public static readonly FichaDeMotor[] ComEscova =
    [
        new("130", "130 (FA-130)", false, 6.0, 9100, 12.0, 0.07, 0.5, 2.0e-7, 20.0, 25.0),
        new("rs-360", "RS-360", false, 7.2, 12900, 0.60, 0.30, 2.5, 1.2e-6, 27.7, 30.5),
        new("rs-380", "RS-380", false, 7.2, 16400, 0.29, 0.50, 4.0, 1.8e-6, 27.7, 38.0),
        new("rs-540", "RS-540", false, 7.2, 23400, 0.10, 1.60, 12.0, 8.0e-6, 35.8, 50.0),
        new("rs-545", "RS-545", false, 12.0, 15000, 0.30, 0.60, 6.0, 1.0e-5, 35.8, 50.0),
        new("rs-550", "RS-550", false, 12.0, 19300, 0.17, 1.40, 10.0, 1.2e-5, 35.8, 57.5),
        new("rs-775", "RS-775", false, 12.0, 12000, 0.40, 0.70, 7.0, 4.0e-5, 42.0, 66.7),
    ];

    /// <summary>Os sem escova, do menor (drone de 2 a 3 polegadas) ao maior (multirrotor grande, carrinho).</summary>
    public static readonly FichaDeMotor[] SemEscova =
    [
        new("1105-5000", "1105 5000KV", true, 11.1, 5000, 0.240, 0.60, 10, 1.5e-7, 11, 5),
        new("1404-3800", "1404 3800KV", true, 14.8, 3800, 0.150, 0.60, 14, 4.0e-7, 14, 4),
        new("a2212-1000", "A2212 1000KV", true, 11.1, 1000, 0.090, 0.50, 12, 4.0e-6, 22, 12),
        new("a2212-1400", "A2212 1400KV", true, 11.1, 1400, 0.065, 0.90, 15, 4.0e-6, 22, 12),
        new("2216-880", "2216 880KV", true, 14.8, 880, 0.105, 0.45, 20, 8.0e-6, 22, 16),
        new("2205-2300", "2205 2300KV", true, 14.8, 2300, 0.075, 1.00, 25, 2.2e-6, 22, 5),
        new("2207-1750", "2207 1750KV", true, 22.2, 1750, 0.055, 1.10, 40, 3.0e-6, 22, 7),
        new("2306-2450", "2306 2450KV", true, 14.8, 2450, 0.060, 1.30, 35, 2.6e-6, 23, 6),
        new("2814-900", "2814 900KV", true, 14.8, 900, 0.060, 0.90, 35, 1.6e-5, 28, 14),
        new("3508-700", "3508 700KV", true, 22.2, 700, 0.100, 0.50, 20, 2.5e-5, 35, 8),
        new("3650-3100", "3650 3100KV (carrinho)", true, 7.4, 3100, 0.012, 2.80, 70, 1.6e-5, 36, 50, ParesDePolos: 2),
    ];

    /// <summary>As fichas que servem para esta peca (o amarelo tem a caixa de reducao: so ele mesmo).</summary>
    public static FichaDeMotor[] Para(string tipo) => tipo switch
    {
        "motor-dc" => ComEscova,
        "motor-brushless" => SemEscova,
        _ => [],
    };

    public static FichaDeMotor Achar(string tipo, string id) => Para(tipo).FirstOrDefault(f => f.Id == id);

    /// <summary>A ficha desta peca: a escolhida, a digitada, ou a do padrao do tipo.</summary>
    public static FichaDeMotor DaPeca(PecaNoCircuito peca)
    {
        var fichas = Para(peca?.Tipo);
        if (fichas.Length == 0) return null;
        if (peca.Modelo == "personalizado" && peca.Ficha is { } propria) return propria.Ficha(peca.Tipo == "motor-brushless");
        return Achar(peca.Tipo, peca.Modelo ?? "") ?? Achar(peca.Tipo, peca.Tipo == "motor-brushless" ? "a2212-1000" : "130");
    }

    /// <summary>
    /// Quanto o desenho cresce ou encolhe: o diametro do motor escolhido sobre
    /// o do desenho original (a lata de 20 mm do 130; o sino de 27,6 mm do
    /// A2212). Um RS-775 sai com o dobro, um 1105 com pouco mais da metade.
    /// </summary>
    public static double EscalaDe(PecaNoCircuito peca)
    {
        if (peca?.Tipo is not ("motor-dc" or "motor-brushless")) return 1;
        if (peca.Modelo is null) return 1;
        var ficha = DaPeca(peca);
        if (ficha is null) return 1;
        double base_ = peca.Tipo == "motor-brushless" ? 22 * 1.12 + 3.0 : 20.0;
        return Math.Clamp(ficha.DiametroVisivel / base_, 0.35, 3.0);
    }

    /// <summary>
    /// O nome do desenho com o rotulo deste modelo (o "RS-540" na tampa, o
    /// "2205 2300KV" no sino); nulo: o desenho padrao.
    /// </summary>
    public static string SvgDoModelo(PecaNoCircuito peca, string parte)
    {
        if (peca?.Tipo is not ("motor-dc" or "motor-brushless") || peca.Modelo is null) return null;
        string id = peca.Modelo == "personalizado" ? "personalizado" : peca.Modelo;
        return $"{peca.Tipo}-{id}-{parte}";
    }

    // =====================================================================
    //  a ficha pela medida: as leis de escala dos motores de verdade
    // =====================================================================

    /// <summary>
    /// A ficha personalizada refeita pelas medidas e pelo KV (ou pela tensao e
    /// o rpm, no de escovas): a resistencia, a corrente sem carga, a corrente
    /// maxima e a inercia que um motor desse tamanho tem.
    ///
    /// As contas sao leis de escala ajustadas nas fichas desta lista: a
    /// constante do motor (Km = Kt / raiz de R) cresce com o volume do
    /// estator; com o KV (que diz quantas voltas tem o fio), dela sai a
    /// resistencia. A corrente maxima e o calor que a superficie dele tira
    /// (perda = I2 R); o atrito, o rolamento e o ferro girando. Errar 20% e
    /// normal entre dois fabricantes do mesmo tamanho -- e a ordem de grandeza
    /// que importa: um 1105 aguenta ~12 A, um 2814 ~40 A.
    /// </summary>
    public static FichaPersonalizada Estimar(bool semEscova, FichaPersonalizada f)
    {
        var e = f.Copia();
        double a = Math.Max(1, f.MedidaA), b = Math.Max(1, f.MedidaB);
        if (semEscova)
        {
            double kv = Math.Max(10, f.RpmOuKv);
            double kt = 60 / (2 * Math.PI * kv);
            double volume = a * a * b / 2420.0;                       // em "estatores 2205"
            double km = 0.0152 * Math.Pow(volume, 0.75);
            e.Resistencia = Arredondar(kt / km * (kt / km));
            double perda = 0.8 * a * Math.Sqrt(b);                    // W que ele dissipa
            e.CorrenteMaxima = Arredondar(Math.Sqrt(perda / e.Resistencia));
            double torqueVazio = 0.0042 * Math.Pow(volume, 0.3) * Math.Pow(kv * 10 / 20000.0, 0.2);
            e.CorrenteVazio = Arredondar(torqueVazio / kt);
            double sino = a * 1.12 + 3.0;
            e.Inercia = 2.2e-6 * Math.Pow(sino / 27.64, 4) * Math.Sqrt(b / 5.0);
        }
        else
        {
            double volts = Math.Max(0.5, f.Volts), rpm = Math.Max(10, f.RpmOuKv);
            double w0 = rpm * 2 * Math.PI / 60;
            double kt = volts * 0.95 / w0;
            double volume = a * a * b / 29157.0;                      // em "latas RS-380"
            double km = 0.0076 * Math.Pow(volume, 0.6);
            e.Resistencia = Arredondar(kt / km * (kt / km));
            double perda = 0.007 * a * b;
            e.CorrenteMaxima = Arredondar(Math.Sqrt(perda / e.Resistencia));
            double torqueVazio = 2.0e-3 * volume * Math.Sqrt(rpm / 16000.0);
            e.CorrenteVazio = Arredondar(torqueVazio / kt);
            e.Inercia = 8.0e-6 * Math.Pow(a / 35.8, 4) * (b / 50.0);
        }
        return e;
    }

    /// <summary>Tres algarismos: a ficha estimada nao finge precisao que nao tem.</summary>
    static double Arredondar(double v)
    {
        if (v <= 0 || double.IsNaN(v)) return 0;
        double casas = Math.Pow(10, 2 - Math.Floor(Math.Log10(v)));
        return Math.Round(v * casas) / casas;
    }
}
