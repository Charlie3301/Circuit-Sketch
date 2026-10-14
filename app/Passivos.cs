namespace Simulador.App;

/// <summary>
/// O valor de um passivo que guarda energia: a capacitancia (F), a
/// indutancia (H) ou a frequencia (Hz, o cristal); a tensao que ele aguenta,
/// a resistencia que tem por dentro (ESR do capacitor, a do fio do indutor) e
/// a corrente maxima (a de saturacao do indutor).
/// </summary>
public sealed record FichaDePassivo(string Modelo, string Nome, double Valor, double Volts = 0, double Resistencia = 0, double Corrente = 0);

/// <summary>
/// Os capacitores, os indutores e os cristais (Alpha 24). O valor e o modelo
/// da peca (o "100n", o "470u-16v"), escolhido na barra. Na energia, o
/// capacitor e o indutor dependem do tempo: ver EnergiaTransitoria.cs.
/// </summary>
public static class Passivos
{
    public static bool EhPassivoComValor(string tipo) => tipo is "capacitor" or "capacitor-eletrolitico" or "indutor" or "cristal";

    static readonly FichaDePassivo[] Ceramicos =
    [
        .. new[] { ("10p", "10 pF", 10e-12), ("22p", "22 pF", 22e-12), ("100p", "100 pF", 100e-12), ("1n", "1 nF", 1e-9),
                   ("10n", "10 nF", 10e-9), ("100n", "100 nF (104)", 100e-9), ("1u", "1 µF", 1e-6) }
            .Select(c => new FichaDePassivo(c.Item1, c.Item2, c.Item3, Volts: 50, Resistencia: 0.05)),
    ];

    static readonly FichaDePassivo[] Eletroliticos =
    [
        .. new[] { ("1u-50v", "1 µF 50 V", 1e-6, 50.0, 3.0), ("10u-25v", "10 µF 25 V", 10e-6, 25, 1.5), ("47u-25v", "47 µF 25 V", 47e-6, 25, 0.8),
                   ("100u-25v", "100 µF 25 V", 100e-6, 25, 0.5), ("220u-16v", "220 µF 16 V", 220e-6, 16, 0.3),
                   ("470u-16v", "470 µF 16 V", 470e-6, 16, 0.2), ("1000u-16v", "1000 µF 16 V", 1000e-6, 16, 0.12),
                   ("2200u-16v", "2200 µF 16 V", 2200e-6, 16, 0.08) }
            .Select(c => new FichaDePassivo(c.Item1, c.Item2, c.Item3, c.Item4, c.Item5)),
    ];

    static readonly FichaDePassivo[] Indutores =
    [
        .. new[] { ("10uh", "10 µH", 10e-6, 0.03, 3.0), ("22uh", "22 µH", 22e-6, 0.05, 2.5), ("47uh", "47 µH", 47e-6, 0.08, 2.0),
                   ("100uh", "100 µH", 100e-6, 0.15, 1.5), ("330uh", "330 µH", 330e-6, 0.4, 0.8), ("1mh", "1 mH", 1e-3, 1.2, 0.5),
                   ("10mh", "10 mH", 10e-3, 8.0, 0.15) }
            .Select(c => new FichaDePassivo(c.Item1, c.Item2, c.Item3, Resistencia: c.Item4, Corrente: c.Item5)),
    ];

    static readonly FichaDePassivo[] Cristais =
    [
        new("16mhz", "16 MHz", 16e6), new("8mhz", "8 MHz", 8e6), new("12mhz", "12 MHz", 12e6), new("32khz", "32,768 kHz", 32768),
    ];

    public static FichaDePassivo[] ModelosDe(string tipo) => tipo switch
    {
        "capacitor" => Ceramicos,
        "capacitor-eletrolitico" => Eletroliticos,
        "indutor" => Indutores,
        "cristal" => Cristais,
        _ => [],
    };

    /// <summary>O de fabrica de cada peca: o 100 nF, o 100 µF, o 100 µH, o 16 MHz.</summary>
    static string Padrao(string tipo) => tipo switch
    {
        "capacitor" => "100n",
        "capacitor-eletrolitico" => "100u-25v",
        "indutor" => "100uh",
        _ => "16mhz",
    };

    public static FichaDePassivo Da(PecaNoCircuito peca)
    {
        var modelos = ModelosDe(peca.Tipo);
        if (modelos.Length == 0) return null;
        return modelos.FirstOrDefault(m => m.Modelo == peca.Modelo) ?? modelos.First(m => m.Modelo == Padrao(peca.Tipo));
    }

    /// <summary>Os dois terminais (o eletrolitico tem + e -).</summary>
    public static (string A, string B) Pinos(string tipo) => tipo == "capacitor-eletrolitico" ? ("+", "-") : ("1", "2");
}
