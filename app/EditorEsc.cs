namespace Simulador.App;

/// <summary>
/// O ESC (o controlador do motor brushless), como os de 30 A com BLHeli:
///
///   - ligado na bateria, toca a musica de partida pelo proprio motor (os
///     tres bipes subindo) e espera um sinal;
///   - so ARMA com o acelerador embaixo (1000 us) por meio segundo -- e o
///     que impede a helice de sair girando quando alguem liga a bateria com
///     o manche no meio. Acelerador no maximo ao ligar e a calibracao: ele
///     guarda o maximo, espera o minimo e arma;
///   - armado, o acelerador (1000 a 2000 us) e a fracao da bateria que vai
///     para o motor, e a corrente da bateria acompanha;
///   - sem sinal por 250 ms o motor para e ele desarma (o failsafe);
///   - o BEC poe 5 V no fio vermelho do cabo de servo: da para alimentar o
///     receptor, ou a placa pelo VIN.
///
/// As tres fases do motor nao tem ordem: o sentido sai da ordem em que os
/// fios chegam no motor, e trocar dois inverte -- como na bancada.
/// </summary>
public partial class EditorCircuito
{
    enum FaseDoEsc { Desligado, Partida, AguardandoBaixo, Calibrando, Armado }

    sealed class EstadoDoEsc
    {
        public string Peca;            // o id da peca (o 4-em-1 tem quatro canais nela)
        public int Canal;              // 0 no ESC simples; 1 a 4 no 4-em-1
        public double ExcessoHa;       // segundos acima da corrente dele
        public bool Queimado;
        public FaseDoEsc Fase;
        public double Tempo;           // segundos na fase atual
        public double BaixoHa;         // segundos com o acelerador embaixo
        public double SemSinalHa;
        public double Acelerador;      // 0 a 1
        public double CorrenteDaBateria;
        public double VoltsDaBateria;
        public int Tom;                // Hz tocando agora pelo motor (0: nada)
        public double ProximoBipe;
    }

    readonly Dictionary<string, EstadoDoEsc> escs = [];
    readonly List<Estouro> avisosDeEsc = [];

    /// <summary>Um canal de ESC: o simples tem um; o 4-em-1 (de drone), quatro, na mesma bateria.</summary>
    sealed record CanalDeEsc(PecaNoCircuito Esc, string Chave, string Sinal, string[] Fases, int Numero);

    public static bool EhEsc(string tipo) => tipo is "esc" or "esc-4em1";

    static IEnumerable<CanalDeEsc> CanaisDeEsc(PecaNoCircuito esc) => esc.Tipo == "esc-4em1"
        ? Enumerable.Range(1, 4).Select(k => new CanalDeEsc(esc, $"{esc.Id}#{k}", $"S{k}", [$"M{k}A", $"M{k}B", $"M{k}C"], k))
        : [new CanalDeEsc(esc, esc.Id, "SINAL", ["A", "B", "C"], 0)];

    /// <summary>A corrente do ESC (por canal, no 4-em-1): a da barra, ou a do modelo.</summary>
    public static double AmperesDoEsc(PecaNoCircuito esc) => esc.Amperes > 0 ? esc.Amperes : esc.Tipo == "esc-4em1" ? 45 : 30;

    /// <summary>
    /// As celulas de LiPo que ele aceita, como nas fichas: os de 12 A sao de
    /// 2S a 3S, os de 20 a 40 A de 2S a 4S, os de 60 A de 2S a 6S; o 4-em-1
    /// de drone, de 3S a 6S.
    /// </summary>
    public static (int Min, int Max) CelulasDoEsc(PecaNoCircuito esc) => esc.Tipo == "esc-4em1" ? (3, 6)
        : AmperesDoEsc(esc) switch { <= 12 => (2, 3), >= 60 => (2, 6), _ => (2, 4) };

    static string NomeDoEsc(PecaNoCircuito esc) => esc.Tipo == "esc-4em1"
        ? $"ESC 4-em-1 de {AmperesDoEsc(esc):0} A" : $"ESC de {AmperesDoEsc(esc):0} A";

    /// <summary>
    /// O motor brushless nas fases deste ESC, e o sentido (+1/-1) pela ordem
    /// dos fios; Faltando: uma fase do motor sem fio.
    /// </summary>
    (PecaNoCircuito Motor, int Sentido, bool Faltando) MotorDoEsc(PecaNoCircuito esc, Netlist rede)
        => MotorDoCanal(CanaisDeEsc(esc).First(), rede);

    /// <summary>O brushless esta nas fases de algum canal de ESC?</summary>
    bool MotorEmAlgumEsc(string idDoMotor, Netlist rede)
        => Atual.Pecas.Where(e => EhEsc(e.Tipo)).SelectMany(CanaisDeEsc).Any(c => MotorDoCanal(c, rede).Motor?.Id == idDoMotor);

    (PecaNoCircuito Motor, int Sentido, bool Faltando) MotorDoCanal(CanalDeEsc canal, Netlist rede)
    {
        var esc = canal.Esc;
        string[] saidas = canal.Fases;
        var raizes = saidas.Select(s => rede.RaizDe(Netlist.No(esc.Id, s))).ToArray();
        foreach (var motor in Atual.Pecas)
        {
            if (motor.Tipo != "motor-brushless") continue;
            var fases = new[] { "A", "B", "C" }.Select(f => rede.RaizDe(Netlist.No(motor.Id, f))).ToArray();
            // Para cada saida do ESC, a fase do motor no mesmo no.
            var mapa = raizes.Select(r => Array.IndexOf(fases, r)).ToArray();
            if (mapa.All(i => i < 0)) continue;
            if (mapa.Any(i => i < 0) || mapa.Distinct().Count() < 3) return (motor, 0, true);
            // A paridade da permutacao: par gira para um lado, impar para o outro.
            int inversoes = 0;
            for (int i = 0; i < 3; i++)
                for (int j = i + 1; j < 3; j++)
                    if (mapa[i] > mapa[j]) inversoes++;
            return (motor, inversoes % 2 == 0 ? 1 : -1, false);
        }
        return (null, 0, false);
    }

    void AplicarEscs(Bancada bancada, Netlist rede, double segundos)
    {
        avisosDeEsc.Clear();
        foreach (var canal in Atual.Pecas.Where(p => EhEsc(p.Tipo)).SelectMany(CanaisDeEsc).ToList())
        {
            var esc = canal.Esc;
            if (!escs.TryGetValue(canal.Chave, out var est))
                escs[canal.Chave] = est = new EstadoDoEsc { Peca = esc.Id, Canal = canal.Numero };
            string qual = canal.Numero > 0 ? $"o canal {canal.Numero} do {NomeDoEsc(esc)}" : $"o {NomeDoEsc(esc)}";
            var (celMin, celMax) = CelulasDoEsc(esc);

            double volts = EnergiaDoQuadro is { } conta
                ? conta.TensaoDoPino(esc.Id, "BAT+") - conta.TensaoDoPino(esc.Id, "BAT-") : 0;
            est.VoltsDaBateria = volts;
            double? pulso = PulsoNoSinal(esc, canal.Sinal, "GND", bancada, rede) ?? PulsoDoReceptor(esc, canal.Sinal, rede);
            var (motor, sentido, faltando) = MotorDoCanal(canal, rede);
            est.Tempo += segundos;
            est.Tom = 0;

            if (est.Queimado || volts > celMax * 4.2 + 1.5)
            {
                if (!est.Queimado)
                    avisosDeEsc.Add(new Estouro(esc.Id,
                        $"{qual} e de {celMin}S a {celMax}S (ate {celMax * 4.2:0.#} V) e recebeu {volts:0.#} V: queimou."));
                est.Fase = FaseDoEsc.Desligado;
                est.Acelerador = 0;
                AcelerarMotorDoEsc(motor, 0, sentido, segundos);
                continue;
            }
            if (volts < 5.5)
            {
                if (est.Fase != FaseDoEsc.Desligado) { est.Fase = FaseDoEsc.Desligado; est.Tempo = 0; }
                est.Acelerador = 0;
                if (pulso is not null)
                    avisosDeEsc.Add(new Estouro(esc.Id,
                        $"{qual} recebe o sinal, mas esta sem bateria (BAT+ e BAT-): ele precisa de uma LiPo de {celMin}S a {celMax}S.",
                        Queimou: false));
                AcelerarMotorDoEsc(motor, 0, sentido, segundos);
                continue;
            }

            if (est.Fase == FaseDoEsc.Desligado) { est.Fase = FaseDoEsc.Partida; est.Tempo = 0; }

            // Sinal valido: 900 a 2100 us.
            bool comSinal = pulso is >= 900 and <= 2100;
            est.SemSinalHa = comSinal ? 0 : est.SemSinalHa + segundos;
            double us = pulso ?? 0;

            switch (est.Fase)
            {
                // A musica de partida: tres bipes subindo, 0,15 s cada.
                case FaseDoEsc.Partida:
                    est.Tom = est.Tempo < 0.15 ? 880 : est.Tempo < 0.30 ? 988 : est.Tempo < 0.45 ? 1109 : 0;
                    if (est.Tempo >= 0.6)
                    {
                        est.Tempo = 0;
                        est.Fase = comSinal && us >= 1800 ? FaseDoEsc.Calibrando : FaseDoEsc.AguardandoBaixo;
                        est.BaixoHa = 0;
                        est.ProximoBipe = 0;
                    }
                    break;

                // Acelerador no maximo ao ligar: guarda o maximo, espera o minimo.
                case FaseDoEsc.Calibrando:
                    if (est.Tempo % 0.5 < 0.08) est.Tom = 1175;
                    if (comSinal && us <= 1100) { est.Fase = FaseDoEsc.Armado; est.Tempo = 0; }
                    break;

                case FaseDoEsc.AguardandoBaixo:
                    est.BaixoHa = comSinal && us <= 1100 ? est.BaixoHa + segundos : 0;
                    if (est.BaixoHa >= 0.5) { est.Fase = FaseDoEsc.Armado; est.Tempo = 0; break; }
                    // Passado um segundo esperando, um bipe curto a cada 2 s.
                    if (est.Tempo > 1.0)
                    {
                        if (est.Tempo >= est.ProximoBipe) est.ProximoBipe = est.Tempo + 2.0;
                        if (est.ProximoBipe - est.Tempo > 1.92) est.Tom = 784;
                    }
                    if (comSinal && us > 1100 && est.Tempo > 1.0)
                        avisosDeEsc.Add(new Estouro(esc.Id,
                            $"o ESC nao arma: o acelerador esta em {us:0} us. Ele so arma com o acelerador embaixo (1000 us) por meio segundo -- "
                            + "no codigo, esc.writeMicroseconds(1000) e um delay antes de acelerar.", Queimou: false));
                    break;

                case FaseDoEsc.Armado:
                    if (est.Tempo < 0.4) est.Tom = 1175;     // o bipe longo de "armado"
                    if (est.SemSinalHa > 0.25)
                    {
                        // Failsafe: sem sinal, para e desarma.
                        est.Fase = FaseDoEsc.AguardandoBaixo;
                        est.Tempo = 0;
                        est.ProximoBipe = 0;
                        est.Acelerador = 0;
                        break;
                    }
                    if (comSinal) est.Acelerador = Math.Clamp((us - 1060) / (1860 - 1060.0), 0, 1);
                    break;
            }

            if (est.Fase != FaseDoEsc.Armado) est.Acelerador = 0;
            if (motor is not null && faltando && est.Acelerador > 0)
                avisosDeEsc.Add(new Estouro(esc.Id,
                    "uma fase do motor brushless esta sem fio no ESC: com duas fases ele so treme (e o ESC pode queimar). Ligue A, B e C.",
                    Queimou: false));

            double aplicado = motor is null || faltando ? 0 : est.Acelerador * volts * sentido;
            var bl = AcelerarMotorDoEsc(motor, aplicado, sentido, segundos);

            // A corrente da bateria: a do motor vezes o acelerador (o ESC e um
            // conversor), mais o consumo dele e o do BEC.
            double noMotor = bl is null ? 0 : Math.Abs(bl.Equivalente.Corrente);
            est.CorrenteDaBateria = noMotor * est.Acelerador + 0.02;

            // Acima da corrente dele por mais de um segundo, o ESC queima -- e o
            // que acontece com o motor grande (ou a helice grande) num ESC pequeno.
            double limite = AmperesDoEsc(esc);
            est.ExcessoHa = est.Acelerador > 0 && noMotor > limite ? est.ExcessoHa + segundos : 0;
            if (est.ExcessoHa > 1.0)
            {
                est.Queimado = true;
                avisosDeEsc.Add(new Estouro(esc.Id,
                    $"{qual} passou mais de um segundo com {noMotor:0} A no motor, e ele e de {limite:0} A: queimou. "
                    + "Use um ESC acima da corrente maxima do motor (com folga), ou uma helice menor."));
            }
        }

        var vivas = Atual.Pecas.Where(p => EhEsc(p.Tipo)).SelectMany(CanaisDeEsc).Select(c => c.Chave).ToHashSet();
        foreach (string id in escs.Keys.Where(id => !vivas.Contains(id)).ToList()) escs.Remove(id);
    }

    MotorSemEscova AcelerarMotorDoEsc(PecaNoCircuito motor, double volts, int sentido, double segundos)
    {
        if (motor is null) return null;
        var bl = MotorSemEscovaDe(motor);
        bl.Acelerar(volts, segundos);
        motoresGuiadosPorEsc.Add(motor.Id);
        return bl;
    }

    /// <summary>Os brushless que um ESC moveu neste quadro (as fases nao contam para eles).</summary>
    readonly HashSet<string> motoresGuiadosPorEsc = [];

    /// <summary>O rotulo em cima do ESC; no 4-em-1, os quatro canais numa linha.</summary>
    string TextoDaPecaEsc(PecaNoCircuito esc)
    {
        var estados = CanaisDeEsc(esc).Select(c => escs.GetValueOrDefault(c.Chave)).Where(e => e is not null).ToList();
        if (estados.Count == 0) return null;
        if (estados.Any(e => e.Queimado)) return "ESC queimado";
        if (esc.Tipo != "esc-4em1") return TextoDoEsc(estados[0]);
        if (estados.All(e => e.Fase == FaseDoEsc.Desligado)) return null;
        if (estados.All(e => e.Fase == FaseDoEsc.Armado))
            return "4 armados · " + string.Join(" / ", estados.Select(e => $"{e.Acelerador * 100:0}%"));
        return TextoDoEsc(estados.First(e => e.Fase != FaseDoEsc.Armado));
    }

    /// <summary>O que o ESC esta fazendo, para o rotulo em cima dele.</summary>
    string TextoDoEsc(EstadoDoEsc est) => est.Fase switch
    {
        FaseDoEsc.Desligado => null,
        FaseDoEsc.Partida => "ESC ligando ♪",
        FaseDoEsc.Calibrando => "ESC calibrando: baixe o acelerador",
        FaseDoEsc.AguardandoBaixo => "ESC aguardando acelerador baixo",
        FaseDoEsc.Armado => $"ESC armado · {est.Acelerador * 100:0}%",
        _ => null,
    };

    /// <summary>O tom que algum ESC esta tocando pelo motor agora (0: nenhum).</summary>
    public int TomDosEscs() => escs.Values.Select(e => e.Tom).FirstOrDefault(t => t > 0);

    /// <summary>Para o teste: a fase do ESC ("Armado"...) e o acelerador.</summary>
    internal (string Fase, double Acelerador) EscParaTeste(string idDaPeca)
        => escs.TryGetValue(idDaPeca, out var e) ? (e.Fase.ToString(), e.Acelerador) : ("", 0);
}
