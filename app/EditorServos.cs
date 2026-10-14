namespace Simulador.App;

/// <summary>
/// Um modelo de servo, pelo datasheet: quanto ele gira por segundo na
/// tensao nominal, a faixa de alimentacao e a corrente parado, girando e
/// travado.
/// </summary>
public sealed record ModeloDeServo(
    string Tipo, string Nome,
    double GrausPorSegundo,     // na tensao nominal (4,8 V)
    double CorrenteParado, double CorrenteGirando, double CorrenteTravado,
    double TensaoMinima, double TensaoMaxima,
    bool Continuo = false, double Rpm = 0)
{
    public const double TensaoNominal = 4.8;

    public static readonly IReadOnlyDictionary<string, ModeloDeServo> Todos = new[]
    {
        // SG90 (o "servo" do Wokwi): 0,1 s/60 graus a 4,8 V; 4,8 a 6 V.
        new ModeloDeServo("servo", "servo SG90", 600, 0.006, 0.22, 0.65, 3.0, 7.2),
        // MG996R: 0,17 s/60 graus a 4,8 V (0,14 a 6 V); trava com 2,5 A.
        new ModeloDeServo("servo-mg996r", "servo MG996R", 353, 0.010, 0.70, 2.5, 4.0, 8.4),
        // FS90R: rotacao continua, 110 rpm a 4,8 V (130 a 6 V).
        new ModeloDeServo("servo-360", "servo 360 graus (FS90R)", 0, 0.006, 0.20, 0.55, 3.0, 7.2,
                          Continuo: true, Rpm: 110),
    }.ToDictionary(m => m.Tipo);

    public static ModeloDeServo De(string tipo) => tipo is null ? null : Todos.GetValueOrDefault(tipo);

    /// <summary>A velocidade escala com a tensao (o motor de dentro e DC), ate 25% acima da nominal.</summary>
    public double Fator(double volts) => Math.Clamp(volts / TensaoNominal, 0, 1.25);
}

/// <summary>
/// Os servos no editor: a largura do pulso no fio de sinal (lida no proprio
/// pino, seja do Servo.write, do writeMicroseconds ou de um ledcWrite a
/// 50 Hz), a alimentacao no V+, e o braco andando na velocidade do
/// datasheet -- antes ele pulava para o angulo, girava sem alimentacao e um
/// servo comandado por ledc nem se mexia.
/// </summary>
public partial class EditorCircuito
{
    sealed class EstadoDoServo
    {
        public double Angulo = 90;     // o braco (graus); no 360, o acumulado
        public double Velocidade;      // graus por segundo agora
        public bool Girando;
        public bool Recebendo;
    }

    readonly Dictionary<string, EstadoDoServo> servos = [];

    /// <summary>Os avisos dos servos neste quadro.</summary>
    readonly List<Estouro> avisosDeServo = [];

    EstadoDoServo ServoDe(PecaNoCircuito peca)
    {
        if (!servos.TryGetValue(peca.Id, out var e)) servos[peca.Id] = e = new EstadoDoServo();
        return e;
    }

    /// <summary>
    /// A largura do pulso (us) que chega neste pino de sinal, ou nulo: um
    /// GPIO gerando um sinal de 40 a 400 Hz com pulso de 0,4 a 2,7 ms (e o
    /// GND da peca no GND da placa).
    /// </summary>
    internal double? PulsoNoSinal(PecaNoCircuito peca, string pino, string pinoGnd, Bancada bancada, Netlist rede)
    {
        var (placa, gpio) = rede.PinoDe(Atual, peca.Id, pino);
        if (placa is null || gpio < 0) return null;
        var maq = bancada.De(placa);
        if (!maq.Energizada) return null;
        if (maq.Linha.PeriodicoEm(gpio, maq.AgoraUsLido) is not { } p || p.Hz < 40 || p.Hz > 400) return null;
        double us = p.Ciclo * 1e6 / p.Hz;
        if (us < 400 || us > 2700) return null;
        if (!GndComumComPlaca(peca, pinoGnd, rede)) return null;
        return us;
    }

    /// <summary>A cada quadro, depois da conta de energia: cada servo anda o que pode.</summary>
    void AplicarServos(Bancada bancada, Netlist rede, double segundos)
    {
        avisosDeServo.Clear();
        foreach (var peca in Atual.Pecas)
        {
            if (ModeloDeServo.De(peca.Tipo) is not { } modelo) continue;
            var est = ServoDe(peca);
            double volts = EnergiaDoQuadro is { } conta
                ? conta.TensaoDoPino(peca.Id, "V+") - conta.TensaoDoPino(peca.Id, "GND") : 0;
            double? pulso = PulsoNoSinal(peca, "PWM", "GND", bancada, rede) ?? PulsoDoReceptor(peca, "PWM", rede);
            est.Recebendo = pulso is not null;
            est.Girando = false;
            est.Velocidade = 0;

            if (volts > modelo.TensaoMaxima)
            {
                avisosDeServo.Add(new Estouro(peca.Id,
                    $"o {modelo.Nome} recebeu {volts:0.#} V no V+, e o maximo dele e {modelo.TensaoMaxima:0.#} V: queimou."));
                continue;
            }
            if (pulso is null)
            {
                if (rede.PinoDe(Atual, peca.Id, "PWM").Gpio >= 0 && !GndComumComPlaca(peca, "GND", rede))
                    avisosDeServo.Add(new Estouro(peca.Id,
                        $"o GND do {modelo.Nome} nao esta no GND da placa: sem a referencia em comum, ele nao entende o pulso.",
                        Queimou: false));
                continue;
            }
            if (volts < modelo.TensaoMinima)
            {
                avisosDeServo.Add(new Estouro(peca.Id,
                    $"o {modelo.Nome} recebe o pulso, mas o V+ esta com {volts:0.#} V: ele precisa de 4,8 a 6 V "
                    + "(o 5V/VIN da placa ou uma fonte, com o GND em comum) para girar.", Queimou: false));
                continue;
            }

            double fator = modelo.Fator(volts);
            if (modelo.Continuo)
            {
                // 1500 us para (com a folga de 20 us do potenciometro de ajuste).
                double desvio = pulso.Value - 1500;
                double fracao = Math.Abs(desvio) < 20 ? 0 : Math.Clamp(desvio / 500, -1, 1);
                est.Velocidade = -fracao * modelo.Rpm * 6 * fator;   // menor que 1500: horario
                est.Angulo = (est.Angulo + est.Velocidade * segundos) % 360;
                est.Girando = fracao != 0;
            }
            else
            {
                // A conta da Servo.h ao contrario: 544 us = 0 grau, 2400 us = 180.
                double alvo = Math.Clamp((pulso.Value - 544) * 180 / (2400 - 544), 0, 180);
                double passo = modelo.GrausPorSegundo * fator * segundos;
                double falta = alvo - est.Angulo;
                if (Math.Abs(falta) <= passo) est.Angulo = alvo;
                else est.Angulo += Math.Sign(falta) * passo;
                est.Girando = Math.Abs(falta) > 0.3;
                est.Velocidade = est.Girando ? Math.Sign(falta) * modelo.GrausPorSegundo * fator : 0;
            }

            if (volts < 4.5)
                avisosDeServo.Add(new Estouro(peca.Id,
                    $"o {modelo.Nome} esta com {volts:0.#} V no V+, abaixo dos 4,8 V dele: gira fraco e devagar "
                    + "(e o pico de corrente pode reiniciar a placa). Alimente com 5 V.", Queimou: false));
        }

        var vivas = Atual.Pecas.Select(p => p.Id).ToHashSet();
        foreach (string id in servos.Keys.Where(id => !vivas.Contains(id)).ToList()) servos.Remove(id);
    }

    /// <summary>
    /// A resistencia que cada carga variavel faz agora, para a conta de
    /// energia: o servo parado ou girando (a corrente dele em 5 V), o ESC
    /// pelo acelerador.
    /// </summary>
    Dictionary<string, double> CargasDinamicas()
    {
        var cargas = new Dictionary<string, double>();
        foreach (var peca in Atual.Pecas)
        {
            if (ModeloDeServo.De(peca.Tipo) is not { } modelo) continue;
            var est = servos.GetValueOrDefault(peca.Id);
            double amperes = est is null || !est.Recebendo ? modelo.CorrenteParado
                : est.Girando ? modelo.CorrenteGirando : modelo.CorrenteParado * 3;
            cargas[peca.Id] = 5.0 / amperes;
        }
        // Os quatro canais do 4-em-1 puxam da mesma bateria: somados, por peca.
        foreach (var porPeca in escs.Values.GroupBy(e => e.Peca))
        {
            double amperes = porPeca.Sum(e => e.CorrenteDaBateria);
            double volts = porPeca.Max(e => e.VoltsDaBateria);
            if (amperes > 0 && volts > 1) cargas[porPeca.Key] = volts / amperes;
        }
        return cargas;
    }

    /// <summary>O angulo do braco agora (o 360: o angulo acumulado do disco).</summary>
    double AnguloDoServo(PecaNoCircuito peca) => servos.TryGetValue(peca.Id, out var e) ? e.Angulo : 90;

    /// <summary>Para o teste: o angulo do braco/disco do servo.</summary>
    internal double AnguloDoServoParaTeste(string idDaPeca) => servos.TryGetValue(idDaPeca, out var e) ? e.Angulo : 90;

    /// <summary>Para o teste: graus por segundo do servo agora (o 360 girando).</summary>
    internal double VelocidadeDoServoParaTeste(string idDaPeca) => servos.TryGetValue(idDaPeca, out var e) ? e.Velocidade : 0;

    /// <summary>Para o teste: os avisos dos servos neste quadro.</summary>
    internal IReadOnlyList<Estouro> AvisosDeServoParaTeste => avisosDeServo;
}
