namespace Simulador.App;

/// <summary>O transistor bipolar: o ganho, a juncao base-emissor, a saturacao e os limites.</summary>
public sealed record FichaDeTransistor(string Modelo, string Nome, bool Pnp, double Beta, double CorrenteMaxima, double Potencia,
                                       double Vbe = 0.65, double Vcesat = 0.2, string Svg = null);

/// <summary>
/// O MOSFET: a tensao de limiar (em modulo), o ganho (k, em A/V2 na
/// saturacao), a resistencia ligado, e o que ele aguenta sem dissipador. No
/// de canal P (CanalP) tudo vale com o gate ABAIXO do source.
/// </summary>
public sealed record FichaDeMosfet(string Modelo, string Nome, double Limiar, double K, double ResistenciaLigado,
                                   double CorrenteMaxima, double Potencia, double GateMaximo, string Svg = null, bool CanalP = false);

/// <summary>O optoacoplador: o CTR (a corrente do coletor sobre a do LED).</summary>
public sealed record FichaDeOpto(string Modelo, string Nome, double Ctr);

/// <summary>Fusivel e varistor: a corrente do fusivel, a tensao de grampo do varistor.</summary>
public sealed record FichaDeProtecao(string Modelo, string Nome, double Valor);

/// <summary>
/// Os semicondutores controlados (Alpha 24): o transistor, o MOSFET e o
/// optoacoplador -- o que passa no coletor (ou no dreno) depende da base (ou
/// do gate). A conta esta em EnergiaSemicondutores.cs.
///
/// Na Alpha 25 os bipolares viraram uma familia por PINAGEM (o pino de cada
/// perna e do tipo, nao do modelo): os TO-92 E-B-C (2N2222A, 2N3904, S8050...),
/// os TO-92 C-B-E (BC547, BC557, BC337...) e os de potencia B-C-E no TO-220
/// (TIP120, TIP31C, MJE3055T...). O MOSFET ganhou o de canal P (IRF9540N), e o
/// optoacoplador, o 4N35 de 6 pinos (com a base para fora).
/// </summary>
public static class Semicondutores
{
    public static bool EhBipolar(string tipo) => tipo is "transistor-npn" or "transistor-pnp" or "transistor-npn-cbe" or "transistor-pnp-cbe"
                                                    or "transistor-npn-potencia" or "transistor-pnp-potencia";

    public static bool EhPnp(string tipo) => tipo is "transistor-pnp" or "transistor-pnp-cbe" or "transistor-pnp-potencia";

    public static bool EhMosfet(string tipo) => tipo is "mosfet-n" or "mosfet-p";

    public static bool EhOpto(string tipo) => tipo is "optoacoplador" or "optoacoplador-4n35";

    public static bool EhControlado(string tipo) => EhBipolar(tipo) || EhMosfet(tipo) || EhOpto(tipo);

    public static bool TemModelo(string tipo) => EhControlado(tipo) || tipo is "fusivel" or "varistor";

    // ---- os TO-92 de pernas E, B, C (a face plana para a frente) ----
    static readonly FichaDeTransistor[] Npn =
    [
        new("2n2222a", "2N2222A", false, 150, 0.6, 0.5),
        new("2n3904", "2N3904", false, 150, 0.2, 0.625),
        new("2n4401", "2N4401", false, 150, 0.6, 0.625),
        new("2n5551", "2N5551 (160 V)", false, 120, 0.6, 0.625),
        new("s8050", "S8050", false, 200, 0.5, 0.625),
        new("s9013", "S9013", false, 150, 0.5, 0.625),
        new("s9014", Idioma.T("S9014 (baixo ruído)"), false, 300, 0.1, 0.45),
        new("mpsa13", "MPSA13 (Darlington)", false, 5000, 0.5, 0.625, Vbe: 1.3, Vcesat: 1.0),
        new("mpsa42", "MPSA42 (300 V)", false, 60, 0.5, 0.625),
    ];

    static readonly FichaDeTransistor[] Pnp =
    [
        new("2n2907a", "2N2907A", true, 150, 0.6, 0.5),
        new("2n3906", "2N3906", true, 150, 0.2, 0.625),
        new("2n4403", "2N4403", true, 150, 0.6, 0.625),
        new("2n5401", "2N5401 (150 V)", true, 120, 0.6, 0.625),
        new("s8550", "S8550", true, 200, 0.5, 0.625),
        new("s9012", "S9012", true, 150, 0.5, 0.625),
        new("s9015", Idioma.T("S9015 (baixo ruído)"), true, 300, 0.1, 0.45),
        new("mpsa92", "MPSA92 (300 V)", true, 60, 0.5, 0.625),
    ];

    // ---- os TO-92 de pernas C, B, E (a familia BC) ----
    static readonly FichaDeTransistor[] NpnCbe =
    [
        new("bc547", "BC547", false, 250, 0.1, 0.5),
        new("bc546", "BC546 (65 V)", false, 200, 0.1, 0.5),
        new("bc548", "BC548", false, 250, 0.1, 0.5),
        new("bc549", Idioma.T("BC549 (baixo ruído)"), false, 400, 0.1, 0.5),
        new("bc337", "BC337", false, 250, 0.8, 0.625),
    ];

    static readonly FichaDeTransistor[] PnpCbe =
    [
        new("bc557", "BC557", true, 250, 0.1, 0.5),
        new("bc556", "BC556 (65 V)", true, 200, 0.1, 0.5),
        new("bc558", "BC558", true, 250, 0.1, 0.5),
        new("bc327", "BC327", true, 250, 0.8, 0.625),
    ];

    // ---- os de potencia no TO-220, pernas B, C, E (sem dissipador: ~2 W) ----
    static readonly FichaDeTransistor[] NpnPotencia =
    [
        new("tip120", "TIP120 (Darlington)", false, 1000, 5.0, 2.0, Vbe: 1.4, Vcesat: 1.0),
        new("tip122", "TIP122 (Darlington, 100 V)", false, 1000, 5.0, 2.0, Vbe: 1.4, Vcesat: 1.0),
        new("tip31c", "TIP31C", false, 25, 3.0, 2.0, Vbe: 0.8, Vcesat: 0.5),
        new("tip41c", "TIP41C", false, 30, 6.0, 2.0, Vbe: 0.8, Vcesat: 0.5),
        new("mje3055t", "MJE3055T", false, 40, 10.0, 2.0, Vbe: 0.8, Vcesat: 0.6),
    ];

    static readonly FichaDeTransistor[] PnpPotencia =
    [
        new("tip125", "TIP125 (Darlington)", true, 1000, 5.0, 2.0, Vbe: 1.4, Vcesat: 1.0),
        new("tip127", "TIP127 (Darlington, 100 V)", true, 1000, 5.0, 2.0, Vbe: 1.4, Vcesat: 1.0),
        new("tip32c", "TIP32C", true, 25, 3.0, 2.0, Vbe: 0.8, Vcesat: 0.5),
        new("tip42c", "TIP42C", true, 30, 6.0, 2.0, Vbe: 0.8, Vcesat: 0.5),
        new("mje2955t", "MJE2955T", true, 40, 10.0, 2.0, Vbe: 0.8, Vcesat: 0.6),
    ];

    /// <summary>
    /// O IRLZ44N e de nivel logico: com 3,3 V no gate ja passa varios amperes.
    /// O IRF520 pede 10 V -- com 3,3 V quase nao liga, e com 5 V fica no meio,
    /// esquentando (o erro classico do modulo de MOSFET no Arduino). Os de
    /// nivel logico (IRLZ44N, IRL540N, IRLB8721) ligam com a placa; os outros,
    /// so com 10 V.
    /// </summary>
    static readonly FichaDeMosfet[] MosfetsN =
    [
        new("irlz44n", "IRLZ44N", 1.5, 3.1, 0.025, 47, 2.0, 16),
        new("irf520", "IRF520", 3.0, 0.4, 0.27, 9.2, 2.0, 20),
        new("irfz44n", "IRFZ44N", 3.0, 1.2, 0.0175, 49, 2.0, 20),
        new("irf540n", "IRF540N", 3.0, 0.8, 0.044, 33, 2.0, 20),
        new("irf3205", "IRF3205", 3.0, 3.0, 0.008, 75, 2.0, 20),
        new("irl540n", "IRL540N", 1.5, 2.5, 0.044, 36, 2.0, 16),
        new("irlb8721", "IRLB8721", 1.8, 6.0, 0.0087, 62, 2.0, 20),
    ];

    /// <summary>O de canal P: liga com o gate uns volts ABAIXO do source (o source no +).</summary>
    static readonly FichaDeMosfet[] MosfetsP =
    [
        new("irf9540n", "IRF9540N", 3.0, 0.5, 0.117, 23, 2.0, 20, CanalP: true),
        new("irf4905", "IRF4905", 3.0, 1.5, 0.02, 74, 2.0, 20, CanalP: true),
        new("irf9z34n", "IRF9Z34N", 3.0, 0.5, 0.1, 19, 2.0, 20, CanalP: true),
    ];

    /// <summary>O CTR: o PC817 e os iguais a ele, 100%; o 4N25 so a metade.</summary>
    static readonly FichaDeOpto[] Optos =
    [
        new("pc817", "PC817", 1.0), new("el817", "EL817", 1.0), new("ltv817", "LTV-817", 1.0), new("tlp521", "TLP521-1", 1.0),
    ];

    static readonly FichaDeOpto[] Optos6 =
    [
        new("4n35", "4N35", 1.0), new("4n25", "4N25", 0.5), new("4n37", "4N37", 1.0),
    ];

    static readonly FichaDeProtecao[] Fusiveis =
    [
        new("250ma", "250 mA", 0.25), new("500ma", "500 mA", 0.5), new("1a", "1 A", 1.0), new("2a", "2 A", 2.0), new("3a", "3 A", 3.0),
        new("5a", "5 A", 5.0), new("10a", "10 A", 10.0),
    ];

    static readonly FichaDeProtecao[] Varistores =
    [
        new("07d220k", "07D220K (22 V)", 22), new("14d270k", "14D270K (27 V)", 27), new("10d330k", "10D330K (33 V)", 33),
        new("14d470k", "14D470K (47 V)", 47), new("14d391k", "14D391K (390 V)", 390), new("20d471k", "20D471K (470 V)", 470),
    ];

    static FichaDeTransistor[] Bipolares(string tipo) => tipo switch
    {
        "transistor-pnp" => Pnp,
        "transistor-npn-cbe" => NpnCbe,
        "transistor-pnp-cbe" => PnpCbe,
        "transistor-npn-potencia" => NpnPotencia,
        "transistor-pnp-potencia" => PnpPotencia,
        _ => Npn,
    };

    static FichaDeMosfet[] Mosfets(string tipo) => tipo == "mosfet-p" ? MosfetsP : MosfetsN;

    static FichaDeOpto[] Optoacopladores(string tipo) => tipo == "optoacoplador-4n35" ? Optos6 : Optos;

    public static FichaDeTransistor Transistor(PecaNoCircuito p)
    {
        var lista = Bipolares(p.Tipo);
        return lista.FirstOrDefault(f => f.Modelo == p.Modelo) ?? lista[0];
    }

    public static FichaDeMosfet Mosfet(PecaNoCircuito p)
    {
        var lista = Mosfets(p.Tipo);
        return lista.FirstOrDefault(f => f.Modelo == p.Modelo) ?? lista[0];
    }

    public static FichaDeOpto Opto(PecaNoCircuito p)
    {
        var lista = Optoacopladores(p.Tipo);
        return lista.FirstOrDefault(f => f.Modelo == p.Modelo) ?? lista[0];
    }

    public static FichaDeProtecao Protecao(PecaNoCircuito p)
    {
        var lista = p.Tipo == "fusivel" ? Fusiveis : Varistores;
        // O de fabrica: o fusivel de 1 A e o varistor de 27 V.
        return lista.FirstOrDefault(f => f.Modelo == p.Modelo) ?? lista.First(f => f.Modelo == (p.Tipo == "fusivel" ? "1a" : "14d270k"));
    }

    /// <summary>Os modelos de cada tipo, na ordem da barra (o primeiro e o de fabrica).</summary>
    public static string[] Modelos(string tipo) =>
        EhBipolar(tipo) ? [.. Bipolares(tipo).Select(f => f.Modelo)]
        : EhMosfet(tipo) ? [.. Mosfets(tipo).Select(f => f.Modelo)]
        : EhOpto(tipo) ? [.. Optoacopladores(tipo).Select(f => f.Modelo)]
        : tipo == "fusivel" ? [.. Fusiveis.Select(f => f.Modelo)]
        : tipo == "varistor" ? [.. Varistores.Select(f => f.Modelo)]
        : [];

    /// <summary>Para a barra: (modelo, nome) de cada opcao, e o modelo de agora.</summary>
    public static (string Modelo, string Nome)[] Opcoes(string tipo) =>
        EhBipolar(tipo) ? [.. Bipolares(tipo).Select(f => (f.Modelo, $"{f.Nome} ({f.CorrenteMaxima:0.#} A)"))]
        : EhMosfet(tipo) ? [.. Mosfets(tipo).Select(f => (f.Modelo, Idioma.T("{0} (liga com {1}{2:0.#} V)", f.Nome, (f.CanalP ? "-" : ""), f.Limiar + Math.Sqrt(f.CorrenteMaxima / 4 / f.K))))]
        : EhOpto(tipo) ? [.. Optoacopladores(tipo).Select(f => (f.Modelo, $"{f.Nome} (CTR {f.Ctr * 100:0}%)"))]
        : tipo == "fusivel" ? [.. Fusiveis.Select(f => (f.Modelo, f.Nome))]
        : tipo == "varistor" ? [.. Varistores.Select(f => (f.Modelo, f.Nome))]
        : [];

    public static string ModeloAtual(PecaNoCircuito p) =>
        EhBipolar(p.Tipo) ? Transistor(p).Modelo
        : EhMosfet(p.Tipo) ? Mosfet(p).Modelo
        : EhOpto(p.Tipo) ? Opto(p).Modelo
        : p.Tipo is "fusivel" or "varistor" ? Protecao(p).Modelo
        : null;

    /// <summary>O desenho do modelo: o de fabrica e o do tipo; os outros, "tipo-modelo".</summary>
    public static string Svg(PecaNoCircuito p)
    {
        if (!EhControlado(p.Tipo)) return p.Tipo;
        var modelos = Modelos(p.Tipo);
        string atual = ModeloAtual(p);
        return atual == modelos[0] ? p.Tipo : $"{p.Tipo}-{atual}";
    }
}
