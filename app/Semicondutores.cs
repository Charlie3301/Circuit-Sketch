namespace Simulador.App;

/// <summary>O transistor bipolar: o ganho, a juncao base-emissor, a saturacao e os limites.</summary>
public sealed record FichaDeTransistor(string Modelo, string Nome, bool Pnp, double Beta, double CorrenteMaxima, double Potencia,
                                       double Vbe = 0.65, double Vcesat = 0.2, string Svg = null);

/// <summary>
/// O MOSFET de canal N: a tensao de limiar, o ganho (k, em A/V2 na
/// saturacao), a resistencia ligado, e o que ele aguenta sem dissipador.
/// </summary>
public sealed record FichaDeMosfet(string Modelo, string Nome, double Limiar, double K, double ResistenciaLigado,
                                   double CorrenteMaxima, double Potencia, double GateMaximo, string Svg = null);

/// <summary>Fusivel e varistor: a corrente do fusivel, a tensao de grampo do varistor.</summary>
public sealed record FichaDeProtecao(string Modelo, string Nome, double Valor);

/// <summary>
/// Os semicondutores controlados (Alpha 24): o transistor, o MOSFET e o
/// optoacoplador -- o que passa no coletor (ou no dreno) depende da base (ou
/// do gate). A conta esta em EnergiaSemicondutores.cs.
/// </summary>
public static class Semicondutores
{
    public static bool EhControlado(string tipo) => tipo is "transistor-npn" or "transistor-pnp" or "mosfet-n" or "optoacoplador";

    public static bool TemModelo(string tipo) => tipo is "transistor-npn" or "transistor-pnp" or "mosfet-n" or "fusivel" or "varistor";

    static readonly FichaDeTransistor[] Npn =
    [
        new("2n2222a", "2N2222A", false, 150, 0.6, 0.5),
        new("2n3904", "2N3904", false, 150, 0.2, 0.625, Svg: "transistor-npn-2n3904"),
    ];

    static readonly FichaDeTransistor[] Pnp =
    [
        new("2n2907a", "2N2907A", true, 150, 0.6, 0.5),
        new("2n3906", "2N3906", true, 150, 0.2, 0.625, Svg: "transistor-pnp-2n3906"),
    ];

    /// <summary>
    /// O IRLZ44N e de nivel logico: com 3,3 V no gate ja passa varios amperes.
    /// O IRF520 pede 10 V -- com 3,3 V quase nao liga, e com 5 V fica no meio,
    /// esquentando (o erro classico do modulo de MOSFET no Arduino).
    /// </summary>
    static readonly FichaDeMosfet[] Mosfets =
    [
        new("irlz44n", "IRLZ44N", 1.5, 3.1, 0.025, 47, 2.0, 16),
        new("irf520", "IRF520", 3.0, 0.4, 0.27, 9.2, 2.0, 20, "mosfet-n-irf520"),
    ];

    static readonly FichaDeProtecao[] Fusiveis =
    [
        new("500ma", "500 mA", 0.5), new("1a", "1 A", 1.0), new("2a", "2 A", 2.0), new("5a", "5 A", 5.0), new("10a", "10 A", 10.0),
    ];

    static readonly FichaDeProtecao[] Varistores =
    [
        new("14d270k", "14D270K (27 V)", 27), new("14d470k", "14D470K (47 V)", 47), new("14d391k", "14D391K (390 V)", 390),
    ];

    public static FichaDeTransistor Transistor(PecaNoCircuito p)
    {
        var lista = p.Tipo == "transistor-pnp" ? Pnp : Npn;
        return lista.FirstOrDefault(f => f.Modelo == p.Modelo) ?? lista[0];
    }

    public static FichaDeMosfet Mosfet(PecaNoCircuito p) => Mosfets.FirstOrDefault(f => f.Modelo == p.Modelo) ?? Mosfets[0];

    public static FichaDeProtecao Protecao(PecaNoCircuito p)
    {
        var lista = p.Tipo == "fusivel" ? Fusiveis : Varistores;
        return lista.FirstOrDefault(f => f.Modelo == p.Modelo) ?? lista[p.Tipo == "fusivel" ? 1 : 0];
    }

    /// <summary>Para a barra: (modelo, nome) de cada opcao, e o modelo de agora.</summary>
    public static (string Modelo, string Nome)[] Opcoes(string tipo) => tipo switch
    {
        "transistor-npn" => [.. Npn.Select(f => (f.Modelo, $"{f.Nome} ({f.CorrenteMaxima:0.#} A)"))],
        "transistor-pnp" => [.. Pnp.Select(f => (f.Modelo, $"{f.Nome} ({f.CorrenteMaxima:0.#} A)"))],
        "mosfet-n" => [.. Mosfets.Select(f => (f.Modelo, $"{f.Nome} (liga com {f.Limiar + Math.Sqrt(f.CorrenteMaxima / 4 / f.K):0.#} V)"))],
        "fusivel" => [.. Fusiveis.Select(f => (f.Modelo, f.Nome))],
        "varistor" => [.. Varistores.Select(f => (f.Modelo, f.Nome))],
        _ => [],
    };

    public static string ModeloAtual(PecaNoCircuito p) => p.Tipo switch
    {
        "transistor-npn" or "transistor-pnp" => Transistor(p).Modelo,
        "mosfet-n" => Mosfet(p).Modelo,
        "fusivel" or "varistor" => Protecao(p).Modelo,
        _ => null,
    };

    public static string Svg(PecaNoCircuito p) => p.Tipo switch
    {
        "transistor-npn" or "transistor-pnp" => Transistor(p).Svg ?? p.Tipo,
        "mosfet-n" => Mosfet(p).Svg ?? p.Tipo,
        _ => p.Tipo,
    };
}
