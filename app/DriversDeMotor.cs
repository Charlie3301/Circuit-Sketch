namespace Simulador.App;

/// <summary>
/// As pontes H e os drivers de motor DC: de que pinos vem a potencia, quanto
/// cada chave perde, e o que cada combinacao de entradas faz nas saidas.
///
/// O driver nao e uma regra de "motor gira para frente": ele e um par de
/// meias pontes na conta de energia, e o motor gira pela tensao que sobra
/// nos fios dele. E isso que faz o L298N perder os seus ~2 V (o motor
/// amarelo com 6 V de pilhas anda a ~170 rpm, nao a 250), o TB6612 quase
/// nada, o PWM virar velocidade, e o freio (as duas saidas no mesmo nivel)
/// ser diferente da roda livre (a saida desligada).
///
/// Os numeros sao dos datasheets: tensao de saturacao das chaves (o L298N e
/// bipolar e cai ~1 V em cada uma; os de MOSFET so tem resistencia), a faixa
/// da alimentacao e a corrente por canal.
/// </summary>
public sealed record DriverDeMotor(
    string Tipo,
    string Nome,
    string Potencia,          // o pino da alimentacao do motor (VS, VM, B+...)
    string Terra,             // o GND de potencia
    string Logica,            // o pino da alimentacao logica (null: o driver tira da potencia)
    double LogicaMinima,      // volts minimos na logica (ou na potencia, sem pino de logica)
    double PotenciaMinima, double PotenciaMaxima,
    double Resistencia,       // ohms de cada chave ligada
    double QuedaAlta, double QuedaBaixa,   // volts de saturacao (bipolar)
    double CorrenteMaxima,    // amperes por saida
    string[] Entradas,
    Func<Func<string, double>, IEnumerable<(string Saida, double? K)>> Saidas,
    IReadOnlyDictionary<string, string> EnableDaSaida = null,
    bool EnableSoltoLiga = false)
{
    /// <summary>
    /// A fracao do tempo em que as chaves desta saida conduzem: o enable no
    /// PWM deixa a saida desligada no resto do tempo (e ai nenhuma chave
    /// perde a queda de saturacao dela). Sem enable, a chave de cima ou a de
    /// baixo esta sempre ligada: 1.
    /// </summary>
    public double ConducaoDe(Func<string, double> entrada, string saida)
    {
        if (EnableDaSaida is null || !EnableDaSaida.TryGetValue(saida, out string en)) return 1;
        double e = entrada(en);
        return e < 0 ? (EnableSoltoLiga ? 1 : 0) : Math.Clamp(e, 0, 1);
    }

    /// <summary>Entrada solta, sem nada que a puxe: -1.</summary>
    public const double Solta = -1;

    static double Nivel(double d) => d < 0 ? 0 : d;

    public static readonly IReadOnlyDictionary<string, DriverDeMotor> Todos = new[]
    {
        // ---- L298N (o modulo vermelho): bipolar, 2 A por canal, ~2 V de queda ----
        // ENA/ENB sem fio = o jumper no lugar (sempre ligado); com fio, o PWM.
        // VCEsat(H) 0,95-1,35 V e VCEsat(L) 0,85-1,2 V a 1 A (datasheet ST).
        new DriverDeMotor("l298n", "L298N", "12V", "GND", "5V", 4.5, 4.5, 46,
            Resistencia: 0.45, QuedaAlta: 0.75, QuedaBaixa: 0.60, CorrenteMaxima: 2.0,
            Entradas: ["ENA", "IN1", "IN2", "IN3", "IN4", "ENB"],
            Saidas: e => MeiaPonteComEnable(e, ("ENA", "IN1", "OUT1"), ("ENA", "IN2", "OUT2"),
                                               ("ENB", "IN3", "OUT3"), ("ENB", "IN4", "OUT4"),
                                               enableSoltoLiga: true),
            EnableDaSaida: new Dictionary<string, string>
            { ["OUT1"] = "ENA", ["OUT2"] = "ENA", ["OUT3"] = "ENB", ["OUT4"] = "ENB" },
            EnableSoltoLiga: true),

        // ---- L293D (o CI de 16 pinos): 600 mA por canal, ~1,4 V de queda em cima ----
        // VOH = VCC2 - 1,4 V e VOL = 1,2 V tipicos a 0,6 A (datasheet TI).
        new DriverDeMotor("l293d", "L293D", "VCC2", "GND.1", "VCC1", 4.5, 4.5, 36,
            Resistencia: 0.67, QuedaAlta: 1.0, QuedaBaixa: 0.8, CorrenteMaxima: 0.6,
            Entradas: ["1,2EN", "1A", "2A", "3,4EN", "3A", "4A"],
            Saidas: e => MeiaPonteComEnable(e, ("1,2EN", "1A", "1Y"), ("1,2EN", "2A", "2Y"),
                                               ("3,4EN", "3A", "3Y"), ("3,4EN", "4A", "4Y"),
                                               enableSoltoLiga: false),
            EnableDaSaida: new Dictionary<string, string>
            { ["1Y"] = "1,2EN", ["2Y"] = "1,2EN", ["3Y"] = "3,4EN", ["4Y"] = "3,4EN" }),

        // ---- L9110S (o modulo pequeno): as saidas seguem as entradas ----
        // L/L e H/H freiam (as duas saidas no mesmo nivel); 800 mA por canal.
        new DriverDeMotor("l9110s", "L9110S", "VCC", "GND", null, 2.5, 2.5, 12,
            Resistencia: 0.6, QuedaAlta: 0.35, QuedaBaixa: 0.25, CorrenteMaxima: 0.8,
            Entradas: ["A-IA", "A-IB", "B-IA", "B-IB"],
            Saidas: e => [("A1", Nivel(e("A-IA"))), ("A2", Nivel(e("A-IB"))),
                          ("B1", Nivel(e("B-IA"))), ("B2", Nivel(e("B-IB")))]),

        // ---- TB6612FNG: MOSFET, 1,2 A por canal, so a resistencia ----
        // STBY em LOW (ou solto) desliga tudo; IN1/IN2 baixos = roda livre;
        // altos = freio; o PWM decide quanto do tempo a saida conduz.
        new DriverDeMotor("tb6612fng", "TB6612FNG", "VM", "GND", "VCC", 2.7, 2.5, 13.5,
            Resistencia: 0.5, QuedaAlta: 0, QuedaBaixa: 0, CorrenteMaxima: 1.2,
            Entradas: ["PWMA", "AIN1", "AIN2", "STBY", "BIN1", "BIN2", "PWMB"],
            Saidas: e => e("STBY") < 0.5 ? [("AO1", null), ("AO2", null), ("BO1", null), ("BO2", null)]
                : [.. CanalTb6612(e, "PWMA", "AIN1", "AIN2", "AO1", "AO2"),
                   .. CanalTb6612(e, "PWMB", "BIN1", "BIN2", "BO1", "BO2")]),

        // ---- DRV8833: MOSFET, 1,5 A por canal; a placa ja puxa o SLP para cima ----
        // 0/0 = roda livre, 1/1 = freio, PWM numa entrada = velocidade.
        new DriverDeMotor("drv8833", "DRV8833", "VM", "GND", null, 2.7, 2.7, 10.8,
            Resistencia: 0.18, QuedaAlta: 0, QuedaBaixa: 0, CorrenteMaxima: 1.5,
            Entradas: ["AIN1", "AIN2", "BIN1", "BIN2", "SLP"],
            Saidas: e => e("SLP") is >= 0 and < 0.5 ? [("AO1", null), ("AO2", null), ("BO1", null), ("BO2", null)]
                : [.. CanalDrv8833(e, "AIN1", "AIN2", "AO1", "AO2"),
                   .. CanalDrv8833(e, "BIN1", "BIN2", "BO1", "BO2")]),

        // ---- BTS7960 (o modulo IBT-2, 43 A): duas meias pontes independentes ----
        // M+ segue o RPWM com o R_EN alto; M- segue o LPWM com o L_EN alto.
        new DriverDeMotor("bts7960", "BTS7960 (IBT-2)", "B+", "B-", "VCC", 4.5, 5.5, 27,
            Resistencia: 0.008, QuedaAlta: 0, QuedaBaixa: 0, CorrenteMaxima: 43,
            Entradas: ["RPWM", "LPWM", "R_EN", "L_EN"],
            Saidas: e => [("M+", e("R_EN") >= 0.5 ? Nivel(e("RPWM")) : null),
                          ("M-", e("L_EN") >= 0.5 ? Nivel(e("LPWM")) : null)]),

        // ---- ULN2003 (a placa do 28BYJ-48): Darlingtons que puxam para o GND ----
        // INn em HIGH liga a saida ao GND (o comum do motor esta no +); em LOW a
        // saida fica solta. VCE(sat) ~0,9 V a 100 mA (datasheet TI). Nao precisa
        // de alimentacao para chavear: o + so alimenta o motor (e os LEDs).
        new DriverDeMotor("uln2003", "ULN2003", "+", "-", null, double.NegativeInfinity,
            double.NegativeInfinity, 50,
            Resistencia: 1.5, QuedaAlta: 0, QuedaBaixa: 0.75, CorrenteMaxima: 0.5,
            Entradas: ["IN1", "IN2", "IN3", "IN4"],
            Saidas: e => [("A", e("IN1") >= 0.5 ? 0.0 : null), ("B", e("IN2") >= 0.5 ? 0.0 : null),
                          ("C", e("IN3") >= 0.5 ? 0.0 : null), ("D", e("IN4") >= 0.5 ? 0.0 : null)]),
    }.ToDictionary(d => d.Tipo);

    public static DriverDeMotor De(string tipo) => tipo is null ? null : Todos.GetValueOrDefault(tipo);

    /// <summary>
    /// A ponte com enable (L298N, L293D): com o enable no PWM, a saida conduz
    /// so naquela fracao do tempo; no resto ela vai para baixo (na media e o
    /// mesmo que a roda livre de um motor pequeno, e serve para os dois jeitos
    /// de ligar: motor entre duas saidas, ou entre uma saida e o GND). Enable
    /// em LOW fixo: a saida desliga (alta impedancia), e o motor roda livre.
    /// </summary>
    static IEnumerable<(string, double?)> MeiaPonteComEnable(
        Func<string, double> e, (string En, string In, string Out) a, (string En, string In, string Out) b,
        (string En, string In, string Out) c, (string En, string In, string Out) d, bool enableSoltoLiga)
    {
        foreach (var (en, entrada, saida) in new[] { a, b, c, d })
        {
            double habilita = e(en);
            if (habilita < 0) habilita = enableSoltoLiga ? 1 : 0;
            yield return (saida, habilita <= 0 ? null : habilita * Nivel(e(entrada)));
        }
    }

    static IEnumerable<(string, double?)> CanalTb6612(Func<string, double> e, string pwm, string in1, string in2,
                                                      string out1, string out2)
    {
        double p = Nivel(e(pwm)), d1 = Nivel(e(in1)), d2 = Nivel(e(in2));
        // Os dois em LOW: parado em alta impedancia (roda livre).
        if (d1 < 0.5 && d2 < 0.5) { yield return (out1, null); yield return (out2, null); yield break; }
        // Com o PWM em LOW, freio curto (as duas em LOW); em HIGH, a tabela.
        yield return (out1, p * d1 * (1 - d2));
        yield return (out2, p * d2 * (1 - d1));
    }

    static IEnumerable<(string, double?)> CanalDrv8833(Func<string, double> e, string in1, string in2,
                                                       string out1, string out2)
    {
        double d1 = Nivel(e(in1)), d2 = Nivel(e(in2));
        if (d1 < 0.02 && d2 < 0.02) { yield return (out1, null); yield return (out2, null); yield break; }
        yield return (out1, d1 * (1 - d2));
        yield return (out2, d2 * (1 - d1));
    }
}
