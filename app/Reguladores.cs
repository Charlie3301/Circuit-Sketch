namespace Simulador.App;

/// <summary>Como o regulador tira a saida da entrada.</summary>
public enum TipoDeRegulador
{
    /// <summary>O 78xx, o AMS1117: a diferenca entre a entrada e a saida vira calor.</summary>
    Linear,

    /// <summary>O step-down (LM2596): chaveado, troca tensao por corrente com pouca perda.</summary>
    Abaixador,

    /// <summary>O step-up (MT3608): chaveado, so sobe a tensao.</summary>
    Elevador,
}

/// <summary>
/// A ficha de um regulador de tensao, com os numeros do datasheet.
/// </summary>
/// <param name="Modelo">O que vai em <see cref="PecaNoCircuito.Modelo"/> ("7805", "3.3").</param>
/// <param name="Volts">A saida: a fixa dos lineares, a de fabrica dos ajustaveis.</param>
/// <param name="Folga">
/// Linear: a queda minima entre a entrada e a saida (o "dropout": ~2 V no
/// 78xx, ~1,1 V no AMS1117). Step-down: o que ele precisa a mais na entrada.
/// </param>
/// <param name="CorrenteMaxima">A corrente de saida da ficha (acima disso esquenta e limita).</param>
/// <param name="LimiteDeCorrente">Onde a protecao segura a corrente (um curto na saida).</param>
/// <param name="EntradaMinima">Os chaveados: abaixo disso o chip nao funciona.</param>
/// <param name="EntradaMaxima">Acima disso, queima.</param>
/// <param name="Quiescente">O que o proprio regulador puxa da entrada, em amperes.</param>
/// <param name="Eficiencia">Os chaveados: a potencia que sai sobre a que entra.</param>
/// <param name="ResistenciaTermica">Do chip ao ar, sem dissipador (graus por watt).</param>
/// <param name="AjusteMinimo">Os ajustaveis: a faixa do trimpot.</param>
public sealed record FichaDeRegulador(
    string Modelo, string Nome, TipoDeRegulador Tipo, double Volts, double Folga,
    double CorrenteMaxima, double LimiteDeCorrente, double EntradaMinima, double EntradaMaxima,
    double Quiescente, double Eficiencia, double ResistenciaTermica,
    double AjusteMinimo = 0, double AjusteMaximo = 0)
{
    /// <summary>O trimpot muda a saida (o LM2596, o MT3608)?</summary>
    public bool Ajustavel => AjusteMaximo > AjusteMinimo;

    /// <summary>Para a lista da barra: "7805 (5 V)".</summary>
    public string Resumo => Ajustavel ? Nome : $"{Nome} ({Volts:0.#} V)";
}

/// <summary>
/// Os reguladores de tensao (Alpha 24): o 78xx no TO-220, o modulo AMS1117,
/// o step-down LM2596 e o step-up MT3608. A conta eletrica esta na
/// <see cref="Energia"/> (EnergiaReguladores.cs); o calor, no editor.
/// </summary>
public static class Reguladores
{
    public static bool EhRegulador(string tipo) => tipo is "regulador-7805" or "ams1117" or "lm2596" or "mt3608";

    /// <summary>
    /// A familia 78xx: 2 V de folga, 1,5 A, ate 35 V na entrada; sem
    /// dissipador o TO-220 tem ~50 graus por watt (a 2 W ja passa de 125).
    /// </summary>
    public static readonly FichaDeRegulador[] Linha78 =
    [
        .. new[] { ("7805", 5.0), ("7806", 6.0), ("7808", 8.0), ("7809", 9.0), ("7812", 12.0), ("7815", 15.0) }
            .Select(m => new FichaDeRegulador(m.Item1, m.Item1, TipoDeRegulador.Linear, m.Item2, 2.0,
                                              1.5, 2.2, 0, 35, 0.005, 0, 50)),
    ];

    /// <summary>O AMS1117: 1,1 V de folga, 0,8 A no modulo, ate 15 V; o SOT-223 aquece mais rapido.</summary>
    public static readonly FichaDeRegulador[] Ams1117 =
    [
        new("3.3", "AMS1117-3.3", TipoDeRegulador.Linear, 3.3, 1.1, 0.8, 1.1, 0, 15, 0.005, 0, 75),
        new("5.0", "AMS1117-5.0", TipoDeRegulador.Linear, 5.0, 1.1, 0.8, 1.1, 0, 15, 0.005, 0, 75),
    ];

    /// <summary>
    /// O LM2596: entrada de 4,5 a 40 V, saida de 1,25 V ate ~1,5 V abaixo da
    /// entrada, 3 A (o limite do chip e ~4,5 A), ~85% de eficiencia.
    /// </summary>
    public static readonly FichaDeRegulador Lm2596 =
        new("lm2596", "LM2596 (step-down)", TipoDeRegulador.Abaixador, 5.0, 1.5, 3.0, 4.5, 4.5, 40, 0.005, 0.85, 35, 1.25, 35);

    /// <summary>
    /// O MT3608: entrada de 2 a 24 V, saida ate 28 V, ~90% de eficiencia; a
    /// chave dele aguenta 4 A, o que da ~1 A na saida de 5 V a partir de 3,7 V.
    /// </summary>
    public static readonly FichaDeRegulador Mt3608 =
        new("mt3608", "MT3608 (step-up)", TipoDeRegulador.Elevador, 5.0, 0, 1.0, 2.0, 2.0, 24, 0.002, 0.90, 150, 2, 28);

    /// <summary>Os modelos que a barra oferece para esta peca (vazio: so a tensao, no trimpot).</summary>
    public static FichaDeRegulador[] ModelosDe(string tipo) => tipo switch
    {
        "regulador-7805" => Linha78,
        "ams1117" => Ams1117,
        _ => [],
    };

    /// <summary>A ficha desta peca: o modelo escolhido, ou o de fabrica.</summary>
    public static FichaDeRegulador Da(PecaNoCircuito peca) => peca.Tipo switch
    {
        "regulador-7805" => Linha78.FirstOrDefault(f => f.Modelo == peca.Modelo) ?? Linha78[0],
        "ams1117" => Ams1117.FirstOrDefault(f => f.Modelo == peca.Modelo) ?? Ams1117[0],
        "lm2596" => Lm2596,
        "mt3608" => Mt3608,
        _ => null,
    };

    /// <summary>A saida que o regulador procura: a da ficha, ou a do trimpot (Volts da peca) nos ajustaveis.</summary>
    public static double SaidaDe(PecaNoCircuito peca)
    {
        var ficha = Da(peca);
        if (ficha is null) return 0;
        return ficha.Ajustavel ? Math.Clamp(peca.Volts, ficha.AjusteMinimo, ficha.AjusteMaximo) : ficha.Volts;
    }

    /// <summary>A tensao de fabrica de quem tem trimpot (a peca nova sai com ela).</summary>
    public static double? VoltsDeFabrica(string tipo) => tipo switch
    {
        "lm2596" => Lm2596.Volts,
        "mt3608" => Mt3608.Volts,
        _ => null,
    };

    /// <summary>Os pinos: a entrada (+ e -) e a saida (+ e -). Nos de tres pernas o GND e o mesmo.</summary>
    public static (string Entrada, string TerraDaEntrada, string Saida, string TerraDaSaida) PinosDe(string tipo) => tipo switch
    {
        "regulador-7805" => ("IN", "GND", "OUT", "GND"),
        "ams1117" => ("VIN", "GND", "VOUT", "GND"),
        "lm2596" => ("IN+", "IN-", "OUT+", "OUT-"),
        "mt3608" => ("VIN+", "VIN-", "VOUT+", "VOUT-"),
        _ => default,
    };

    /// <summary>O nome curto para os avisos: "o 7812", "o LM2596".</summary>
    public static string NomeCurto(PecaNoCircuito peca) => peca.Tipo switch
    {
        "regulador-7805" => Da(peca).Modelo,
        "ams1117" => Da(peca).Nome,
        "lm2596" => "LM2596",
        "mt3608" => "MT3608",
        _ => peca.Tipo,
    };
}
