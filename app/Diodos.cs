namespace Simulador.App;

/// <summary>
/// A ficha de um diodo: a queda no sentido direto (Vf), a resistencia que
/// sobra depois dela, a corrente que ele aguenta e, no zener, a tensao em que
/// ele conduz ao contrario.
/// </summary>
/// <param name="Svg">O desenho do modelo (nulo: o padrao da peca).</param>
public sealed record FichaDeDiodo(string Modelo, string Nome, double Queda, double Resistencia, double CorrenteMaxima,
                                  double Zener = 0, double ResistenciaZener = 0, double Potencia = 0, string Svg = null)
{
    public string Resumo => Zener > 0 ? $"{Nome} ({Zener:0.#} V, {Potencia:0.#} W)" : $"{Nome} ({CorrenteMaxima:0.#} A)";
}

/// <summary>
/// Os diodos (Alpha 24): o comum, o zener e o schottky. Na conta de energia
/// cada um e um "diodo ideal com queda" (o mesmo modelo do LED): nao conduz
/// abaixo de Vf; acima, Vf mais a resistencia. O zener tem o segundo, ao
/// contrario, que conduz a partir da tensao dele.
/// </summary>
public static class Diodos
{
    public static bool EhDiodo(string tipo) => tipo is "diodo" or "diodo-zener" or "diodo-schottky";

    static readonly FichaDeDiodo[] Comuns =
    [
        new("1n4007", "1N4007", 0.70, 0.05, 1.0),
        new("1n4001", "1N4001 (50 V)", 0.70, 0.05, 1.0, Svg: "diodo-1n4001"),
        new("1n4004", "1N4004 (400 V)", 0.70, 0.05, 1.0, Svg: "diodo-1n4004"),
        new("uf4007", Idioma.T("UF4007 (rápido)"), 0.85, 0.08, 1.0, Svg: "diodo-uf4007"),
        new("1n4148", Idioma.T("1N4148 (sinal)"), 0.62, 1.0, 0.2, Svg: "diodo-1n4148"),
        new("1n5408", "1N5408", 0.75, 0.02, 3.0, Svg: "diodo-1n5408"),
        new("6a10", "6A10", 0.75, 0.01, 6.0, Svg: "diodo-6a10"),
    ];

    // O 1N47xx (1 W): a impedancia (Zzt) e a corrente maxima (P / Vz) do datasheet.
    static readonly FichaDeDiodo[] Zeners =
    [
        new("5v1", "Zener 1N4733", 0.70, 0.1, 0.2, 5.1, 7, 1.0),
        new("3v3", "Zener 1N4728", 0.70, 0.1, 0.3, 3.3, 10, 1.0, "diodo-zener-3v3"),
        new("4v7", "Zener 1N4732", 0.70, 0.1, 0.2, 4.7, 8, 1.0, "diodo-zener-4v7"),
        new("6v2", "Zener 1N4735", 0.70, 0.1, 0.15, 6.2, 2, 1.0, "diodo-zener-6v2"),
        new("9v1", "Zener 1N4739", 0.70, 0.1, 0.1, 9.1, 5, 1.0, "diodo-zener-9v1"),
        new("12v", "Zener 1N4742", 0.70, 0.1, 0.08, 12.0, 9, 1.0, "diodo-zener-12v"),
        new("15v", "Zener 1N4744", 0.70, 0.1, 0.06, 15.0, 14, 1.0, "diodo-zener-15v"),
        new("bzx55-5v1", "Zener BZX55C5V1", 0.70, 0.2, 0.1, 5.1, 40, 0.5, "diodo-zener-bzx55-5v1"),
    ];

    static readonly FichaDeDiodo[] Schottkys =
    [
        new("1n5819", "1N5819", 0.30, 0.05, 1.0),
        new("1n5817", "1N5817 (20 V)", 0.28, 0.05, 1.0, Svg: "diodo-schottky-1n5817"),
        new("1n5822", "1N5822", 0.30, 0.02, 3.0, Svg: "diodo-schottky-1n5822"),
        new("ss34", "SS34", 0.35, 0.03, 3.0, Svg: "diodo-schottky-ss34"),
        new("bat85", Idioma.T("BAT85 (sinal)"), 0.25, 2.0, 0.2, Svg: "diodo-schottky-bat85"),
        new("sb560", "SB560", 0.40, 0.01, 5.0, Svg: "diodo-schottky-sb560"),
    ];

    public static FichaDeDiodo[] ModelosDe(string tipo) => tipo switch
    {
        "diodo" => Comuns,
        "diodo-zener" => Zeners,
        "diodo-schottky" => Schottkys,
        _ => [],
    };

    public static FichaDeDiodo Da(PecaNoCircuito peca)
    {
        var modelos = ModelosDe(peca.Tipo);
        return modelos.Length == 0 ? null : modelos.FirstOrDefault(m => m.Modelo == peca.Modelo) ?? modelos[0];
    }
}
