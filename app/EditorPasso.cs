namespace Simulador.App;

/// <summary>
/// Os motores de passo atras de um driver: o NEMA 17 ("stepper-motor") num
/// A4988 ou DRV8825, e o 28BYJ-48 no ULN2003.
///
/// O driver de STEP/DIR conta as bordas do STEP (pelos eventos da placa,
/// nao pelo quadro), no micropasso que os pinos MS/M pedem, so quando esta
/// acordado, fora do reset, habilitado e com o VMOT dentro da faixa. O
/// motor anda o que o driver contou -- no sentido que as bobinas dizem: uma
/// bobina invertida inverte o giro, e as bobinas misturadas (uma ponta de
/// cada numa saida) so fazem o motor tremer.
/// </summary>
public partial class EditorCircuito
{
    readonly Dictionary<string, ContadorDePassos> contadoresDePasso = [];

    /// <summary>O angulo do motor de passo quando quem manda e um driver (nulo: os GPIOs).</summary>
    readonly Dictionary<string, double> anguloPorDriver = [];

    /// <summary>Os avisos dos motores de passo neste quadro (RST solto, VMOT baixo...).</summary>
    readonly List<Estouro> avisosDePasso = [];

    /// <summary>
    /// O nivel logico de um pino de peca: 1, 0, ou -1 (solto). Um GPIO de
    /// saida manda; um trilho (3V3/5V, GND) tambem; o SLP dos drivers de passo
    /// tem pull-up na placa, entao o RST ligado nele fica em HIGH.
    /// </summary>
    int NivelLogico(PecaNoCircuito peca, string pino, Bancada bancada, Netlist rede)
    {
        var (placa, gpio) = rede.PinoDe(Atual, peca.Id, pino);
        if (placa is not null && gpio >= 0)
        {
            var maq = bancada.De(placa);
            if (maq.Energizada && maq.Modo[gpio] == 1) return maq.Pwm[gpio] > 127 || maq.Digital[gpio] == 1 ? 1 : 0;
        }
        if (rede.AlcancaFonte(Atual, peca.Id, pino, terra: false)) return 1;
        if (rede.AlcancaFonte(Atual, peca.Id, pino, terra: true)) return 0;
        if (DriverDePasso.De(peca.Tipo) is { } passo && pino != passo.Sleep
            && rede.RaizDe(Netlist.No(peca.Id, pino)) == rede.RaizDe(Netlist.No(peca.Id, passo.Sleep)))
            return NivelLogico(peca, passo.Sleep, bancada, rede) is 0 ? 0 : 1;
        return -1;
    }

    /// <summary>O GND deste pino da peca e o GND de uma placa (a referencia dos sinais)?</summary>
    bool GndComumComPlaca(PecaNoCircuito peca, string pinoGnd, Netlist rede)
    {
        string raiz = rede.RaizDe(Netlist.No(peca.Id, pinoGnd));
        if (raiz is null) return false;
        foreach (var placa in Atual.Pecas)
        {
            if (!PerfilPlaca.EhPlaca(placa.Tipo) || Catalogo.Obter(placa.Tipo) is not { } def) continue;
            if (def.Pinos.Any(p => Netlist.EhPinoDeTerra(p.Nome) && rede.RaizDe(Netlist.No(placa.Id, p.Nome)) == raiz))
                return true;
        }
        return false;
    }

    double VoltsEntre(PecaNoCircuito peca, string a, string b)
        => EnergiaDoQuadro is { } conta ? conta.TensaoDoPino(peca.Id, a) - conta.TensaoDoPino(peca.Id, b) : 0;

    /// <summary>
    /// O motor de passo bipolar numa ponte H (o L298N ou o L293D da Stepper.h,
    /// o TB6612, o DRV8833): as pontas das bobinas nao estao num GPIO, e o
    /// sentido da corrente em cada bobina e o da tensao entre elas na conta de
    /// energia. As pontas na ordem do EixoDePasso (A+, B+, A-, B-); nula sem
    /// conta de energia.
    /// </summary>
    bool[] BobinasPelaTensao(PecaNoCircuito motor, string[] pontas)
    {
        if (EnergiaDoQuadro is null) return null;
        const double limiar = 1.0;   // menos que isso nao vence o atrito do rotor
        double a = VoltsEntre(motor, pontas[0], pontas[2]), b = VoltsEntre(motor, pontas[1], pontas[3]);
        return [a > limiar, b > limiar, a < -limiar, b < -limiar];
    }

    /// <summary>A4988/DRV8825: conta os passos deste quadro.</summary>
    void AplicarDriverDePasso(PecaNoCircuito peca, Bancada bancada, Netlist rede, double segundos)
    {
        var def = DriverDePasso.De(peca.Tipo);
        if (!contadoresDePasso.TryGetValue(peca.Id, out var contador))
            contadoresDePasso[peca.Id] = contador = new ContadorDePassos();

        double vmot = VoltsEntre(peca, def.Vmot, "GND");
        double vdd = def.Vdd is null ? vmot : VoltsEntre(peca, def.Vdd, "GND");
        int en = NivelLogico(peca, def.Enable, bancada, rede);
        int rst = NivelLogico(peca, def.Reset, bancada, rede);
        int slp = NivelLogico(peca, def.Sleep, bancada, rede);
        if (slp < 0) slp = 1;   // pull-up da placa

        var (placa, gpioStep) = rede.PinoDe(Atual, peca.Id, def.Step);
        int gpioDir = rede.GpioDe(Atual, peca.Id, def.Dir);
        bool gnd = GndComumComPlaca(peca, "GND", rede);

        bool energia = vmot >= def.VmotMinima && vdd >= 3.0;
        bool habilitado = energia && en != 1 && rst == 1 && slp == 1 && gnd;

        int Bit(string pino) => NivelLogico(peca, pino, bancada, rede) == 1 ? 1 : 0;
        int divisao = def.Divisao(Bit(def.Micropasso[0]), Bit(def.Micropasso[1]), Bit(def.Micropasso[2]));

        var maq = placa is null ? null : bancada.De(placa);
        if (maq is not null && gpioStep >= 0)
        {
            contador.Seguir(maq, gpioStep, gpioDir, divisao, habilitado);

            // STEP no PWM (ledc/tone): o hardware gera os passos sozinho.
            if (maq.Linha.PeriodicoEm(gpioStep, maq.AgoraUsLido) is { } periodo)
                contador.SomarPeriodico(periodo.Hz, segundos, gpioDir >= 0 ? maq.Digital[gpioDir] : 0, divisao, habilitado);
        }

        // ---- o que travou, em palavras ----
        if (gpioStep < 0) return;
        if (vmot > def.VmotMaxima * 1.1)
            avisosDePasso.Add(new Estouro(peca.Id, Idioma.T("o {0} recebeu {1:0.#} V no VMOT, e o maximo dele e {2:0} V: queimou.", def.Nome, vmot, def.VmotMaxima)));
        else if (vmot < def.VmotMinima)
            avisosDePasso.Add(new Estouro(peca.Id,
                vmot < 0.5
                    ? Idioma.T("o {0} esta sem alimentacao no VMOT: o motor precisa de uma fonte de {1:0.#} V ou mais ali (o + dela no VMOT, o - no GND).", def.Nome, def.VmotMinima)
                    : Idioma.T("o {0} esta com {1:0.#} V no VMOT, e o minimo dele e {2:0.#} V: com menos, ele nao liga (os 5 V da placa nao bastam).", def.Nome, vmot, def.VmotMinima),
                Queimou: false));
        else if (def.Vdd is not null && vdd < 3.0)
            avisosDePasso.Add(new Estouro(peca.Id, Idioma.T("o {0} esta sem a logica: o VDD precisa dos 3,3 V ou 5 V da placa.", def.Nome), Queimou: false));
        else if (rst != 1)
            avisosDePasso.Add(new Estouro(peca.Id, rst < 0
                ? Idioma.T("o RST do {0} esta solto: ele fica em reset e o motor nao anda. Ligue o RST no SLP (o jeito do esquema da Pololu) ou num pino em HIGH.", def.Nome)
                : Idioma.T("o RST do {0} esta em LOW: o driver fica em reset e o motor nao anda.", def.Nome), Queimou: false));
        else if (!gnd)
            avisosDePasso.Add(new Estouro(peca.Id,
                Idioma.T("o GND do {0} nao esta ligado no GND da placa: sem a referencia em comum, ele nao ve os pulsos do STEP.", def.Nome),
                Queimou: false));
    }

    /// <summary>
    /// O NEMA 17 ligado nas saidas de um driver de passo: de quem ele e, e em
    /// que sentido (as bobinas certas, invertidas, trocadas ou misturadas).
    /// </summary>
    (PecaNoCircuito Driver, int Sentido, bool Misturado) DriverDoMotorDePasso(PecaNoCircuito motor, Netlist rede)
    {
        string Raiz(PecaNoCircuito p, string pino) => rede.RaizDe(Netlist.No(p.Id, pino));
        string ap = Raiz(motor, "A+"), am = Raiz(motor, "A-"), bp = Raiz(motor, "B+"), bm = Raiz(motor, "B-");

        foreach (var driver in Atual.Pecas)
        {
            if (DriverDePasso.De(driver.Tipo) is not { } def) continue;
            string b1a = Raiz(driver, def.Bobina1[0]), b1b = Raiz(driver, def.Bobina1[1]);
            string b2a = Raiz(driver, def.Bobina2[0]), b2b = Raiz(driver, def.Bobina2[1]);
            var saidas = new[] { b1a, b1b, b2a, b2b };
            if (!new[] { ap, am, bp, bm }.Any(r => saidas.Contains(r))) continue;

            // A bobina A do motor numa bobina do driver, a B na outra.
            int Polo(string mais, string menos, string a, string b)
                => mais == a && menos == b ? 1 : mais == b && menos == a ? -1 : 0;
            int a1 = Polo(ap, am, b1a, b1b), b2 = Polo(bp, bm, b2a, b2b);
            if (a1 != 0 && b2 != 0) return (driver, a1 * b2, false);
            int a2 = Polo(ap, am, b2a, b2b), b1 = Polo(bp, bm, b1a, b1b);
            if (a2 != 0 && b1 != 0) return (driver, -a2 * b1, false);
            return (driver, 0, true);
        }
        return (null, 0, false);
    }

    /// <summary>
    /// Depois do laco de pecas: os NEMA 17 ligados num driver andam o que
    /// ele contou; os 28BYJ-48 seguem as fases que o ULN2003 recebe.
    /// </summary>
    void AplicarMotoresDePasso(Bancada bancada, Netlist rede, double segundos)
    {
        avisosDePasso.Clear();
        foreach (var peca in Atual.Pecas)
            if (DriverDePasso.De(peca.Tipo) is not null) AplicarDriverDePasso(peca, bancada, rede, segundos);

        foreach (var motor in Atual.Pecas)
        {
            if (motor.Tipo == "stepper-motor")
            {
                var (driver, sentido, misturado) = DriverDoMotorDePasso(motor, rede);
                if (driver is null) { anguloPorDriver.Remove(motor.Id); continue; }
                if (misturado)
                    avisosDePasso.Add(new Estouro(motor.Id,
                        Idioma.T("as bobinas do motor de passo estao misturadas nas saidas do driver: cada par (1A/1B, 2A/2B) tem que ser UMA bobina (A+ e A-, ou B+ e B-). Assim o motor so treme."), Queimou: false));
                double passos = contadoresDePasso.TryGetValue(driver.Id, out var c) ? c.Passos : 0;
                anguloPorDriver[motor.Id] = misturado ? 0 : sentido * passos * EixoDePasso.GrausPorPassoNema;
            }
            else if (motor.Tipo == "motor-28byj48") AplicarMotor28byj(motor, bancada, rede);
        }
    }

    /// <summary>
    /// O 28BYJ-48: cada fio de fase vai num conector do ULN2003 (A..D), que
    /// liga a fase quando a entrada dele (IN1..IN4) esta em HIGH. O eixo segue
    /// os GPIOs dessas entradas pelos eventos -- o mesmo EixoDePasso do NEMA,
    /// com o passo do 28BYJ-48 (32 no rotor, reducao de 63,68:1).
    /// </summary>
    void AplicarMotor28byj(PecaNoCircuito motor, Bancada bancada, Netlist rede)
    {
        if (!eixosDePasso.TryGetValue(motor.Id, out var eixos))
            eixosDePasso[motor.Id] = eixos = [new EixoDePasso(EixoDePasso.GrausPorPasso28byj)];

        string[] fases = ["laranja", "amarelo", "rosa", "azul"];
        PecaNoCircuito uln = null;
        var gpios = new int[4];
        Maquina maq = null;
        for (int k = 0; k < 4; k++)
        {
            gpios[k] = -1;
            string raiz = rede.RaizDe(Netlist.No(motor.Id, fases[k]));
            foreach (var p in Atual.Pecas)
            {
                if (p.Tipo != "uln2003") continue;
                string saida = new[] { "A", "B", "C", "D" }.FirstOrDefault(s => rede.RaizDe(Netlist.No(p.Id, s)) == raiz);
                if (saida is null) continue;
                uln = p;
                var (placa, gpio) = rede.PinoDe(Atual, p.Id, $"IN{" ABCD".IndexOf(saida[0])}");
                if (placa is not null) { gpios[k] = gpio; maq = bancada.De(placa); }
                break;
            }
        }

        if (uln is null)
        {
            if (fases.Any(f => rede.PinoDe(Atual, motor.Id, f).Gpio >= 0))
                avisosDePasso.Add(new Estouro(motor.Id,
                    Idioma.T("o 28BYJ-48 esta com as fases direto nos GPIOs: cada bobina pede ~80 mA e a porta nao chaveia isso. Ligue o motor no ULN2003 (o conector de 5 fios) e os IN1..IN4 nos GPIOs."), Queimou: false));
            return;
        }

        // O comum (vermelho) no + do ULN2003 com 4 V ou mais, e o GND do
        // ULN2003 sendo o da placa: sem isso as fases nao fecham.
        double volts = VoltsEntre(uln, "+", "-");
        bool alimentado = volts >= 4.0 && rede.RaizDe(Netlist.No(motor.Id, "vermelho")) == rede.RaizDe(Netlist.No(uln.Id, "+"));
        bool gnd = GndComumComPlaca(uln, "-", rede);
        if (!alimentado)
            avisosDePasso.Add(new Estouro(uln.Id,
                Idioma.T("o 28BYJ-48 precisa do fio vermelho no + do ULN2003, e de 5 V ali (o + e o - da placa do driver na fonte)."),
                Queimou: false));
        else if (!gnd)
            avisosDePasso.Add(new Estouro(uln.Id,
                Idioma.T("o - do ULN2003 nao esta no GND da placa: sem a referencia em comum, as entradas IN1..IN4 nao ligam as fases."),
                Queimou: false));

        if (maq is null || !alimentado || !gnd) return;
        eixos[0].Seguir(maq, gpios);
    }

    /// <summary>Para o teste: o angulo do motor de passo (pelo driver, ou pelas bobinas).</summary>
    internal double AnguloDoPassoParaTeste(string idDaPeca)
        => anguloPorDriver.TryGetValue(idDaPeca, out double a) ? a
         : eixosDePasso.TryGetValue(idDaPeca, out var eixos) && eixos.Length > 0 ? eixos[0].Angulo : 0;

    /// <summary>Para o teste: os passos (cheios) que um driver contou.</summary>
    internal double PassosDoDriverParaTeste(string idDaPeca)
        => contadoresDePasso.TryGetValue(idDaPeca, out var c) ? c.Passos : 0;
}
