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

    // Os ceramicos de disco (50 V), o multicamada de 1 uF e os de filme: o de
    // poliester verde (o "2A104J", 100 V) e o de caixa (63 V).
    static readonly FichaDePassivo[] Ceramicos =
    [
        .. new[] { ("10p", "10 pF", 10e-12), ("22p", "22 pF", 22e-12), ("100p", "100 pF", 100e-12), ("1n", "1 nF", 1e-9),
                   ("4n7", "4,7 nF (472)", 4.7e-9), ("10n", "10 nF", 10e-9), ("47n", "47 nF (473)", 47e-9),
                   ("100n", "100 nF (104)", 100e-9), ("220n", "220 nF (224)", 220e-9), ("470n", "470 nF (474)", 470e-9),
                   ("1u", "1 µF", 1e-6) }
            .Select(c => new FichaDePassivo(c.Item1, c.Item2, c.Item3, Volts: 50, Resistencia: 0.05)),
        new("10n-poliester", Idioma.T("10 nF poliéster (2A103J)"), 10e-9, Volts: 100, Resistencia: 0.03),
        new("100n-poliester", Idioma.T("100 nF poliéster (2A104J)"), 100e-9, Volts: 100, Resistencia: 0.03),
        new("100n-filme", Idioma.T("100 nF filme, caixa (63 V)"), 100e-9, Volts: 63, Resistencia: 0.02),
        new("1u-filme", Idioma.T("1 µF filme, caixa (63 V)"), 1e-6, Volts: 63, Resistencia: 0.02),
    ];

    // Os eletroliticos de aluminio (a ESR cai com o tamanho da lata) e os de
    // tantalo, a gota amarela: menores e de ESR mais baixa para o valor.
    static readonly FichaDePassivo[] Eletroliticos =
    [
        .. new[] { ("1u-50v", "1 µF 50 V", 1e-6, 50.0, 3.0), ("2u2-50v", "2,2 µF 50 V", 2.2e-6, 50, 2.5),
                   ("4u7-50v", "4,7 µF 50 V", 4.7e-6, 50, 2.0), ("10u-25v", "10 µF 25 V", 10e-6, 25, 1.5),
                   ("22u-25v", "22 µF 25 V", 22e-6, 25, 1.2), ("47u-25v", "47 µF 25 V", 47e-6, 25, 0.8),
                   ("100u-25v", "100 µF 25 V", 100e-6, 25, 0.5), ("100u-50v", "100 µF 50 V", 100e-6, 50, 0.4),
                   ("220u-16v", "220 µF 16 V", 220e-6, 16, 0.3), ("330u-25v", "330 µF 25 V", 330e-6, 25, 0.25),
                   ("470u-16v", "470 µF 16 V", 470e-6, 16, 0.2), ("470u-35v", "470 µF 35 V", 470e-6, 35, 0.15),
                   ("1000u-16v", "1000 µF 16 V", 1000e-6, 16, 0.12), ("2200u-16v", "2200 µF 16 V", 2200e-6, 16, 0.08),
                   ("3300u-16v", "3300 µF 16 V", 3300e-6, 16, 0.06),
                   ("1u-35v-tantalo", Idioma.T("1 µF 35 V tântalo"), 1e-6, 35, 5.0), ("10u-16v-tantalo", Idioma.T("10 µF 16 V tântalo"), 10e-6, 16, 2.0),
                   ("47u-10v-tantalo", Idioma.T("47 µF 10 V tântalo"), 47e-6, 10, 0.9) }
            .Select(c => new FichaDePassivo(c.Item1, c.Item2, c.Item3, c.Item4, c.Item5)),
    ];

    static readonly FichaDePassivo[] Indutores =
    [
        .. new[] { ("4u7h", "4,7 µH", 4.7e-6, 0.02, 4.0), ("10uh", "10 µH", 10e-6, 0.03, 3.0), ("22uh", "22 µH", 22e-6, 0.05, 2.5),
                   ("47uh", "47 µH", 47e-6, 0.08, 2.0), ("100uh", "100 µH", 100e-6, 0.15, 1.5), ("220uh", "220 µH", 220e-6, 0.3, 1.0),
                   ("330uh", "330 µH", 330e-6, 0.4, 0.8), ("470uh", "470 µH", 470e-6, 0.6, 0.6), ("1mh", "1 mH", 1e-3, 1.2, 0.5),
                   ("2m2h", "2,2 mH", 2.2e-3, 2.5, 0.35), ("10mh", "10 mH", 10e-3, 8.0, 0.15) }
            .Select(c => new FichaDePassivo(c.Item1, c.Item2, c.Item3, Resistencia: c.Item4, Corrente: c.Item5)),
    ];

    static readonly FichaDePassivo[] Cristais =
    [
        new("16mhz", "16 MHz", 16e6), new("8mhz", "8 MHz", 8e6), new("12mhz", "12 MHz", 12e6), new("32khz", "32,768 kHz", 32768),
        new("4mhz", "4 MHz", 4e6), new("11m0592", "11,0592 MHz (serial)", 11.0592e6), new("20mhz", "20 MHz", 20e6),
        new("25mhz", "25 MHz", 25e6),
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
