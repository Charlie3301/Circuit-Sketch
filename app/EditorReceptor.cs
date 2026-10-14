namespace Simulador.App;

/// <summary>
/// O receptor de radio FlySky FS-iA6B e o controle FS-i6.
///
/// O controle e uma peca sem fio na mesa: dois manches (o esquerdo e o
/// acelerador e o leme, o direito o aileron e o profundor -- o "modo 2" de
/// fabrica) e as chaves SWA e SWB. O receptor, alimentado (4 a 6,5 V no
/// VCC) e com o controle ligado, poe nos fios o que eles mandam:
///
///   - CH1..CH6: um pulso de servo a cada 20 ms (1000 a 2000 us). Ligado num
///     servo ou num ESC, move/acelera SEM placa nenhuma -- e o aeromodelo;
///     ligado num GPIO, a placa le com pulseIn ou com interrupcao, no tempo
///     de verdade (o SinalExterno do motor);
///   - PPM: os oito canais num fio so, um pulso por canal, 22,5 ms por quadro;
///   - IBUS: os 14 canais por serial (115200 baud, 32 bytes a cada 7 ms),
///     na UART que tiver o RX naquele pino.
///
/// Controle desligado (ou longe): o receptor para de mandar pulsos -- o
/// failsafe de fabrica do iA6B --, e o ESC corta o motor.
/// </summary>
public partial class EditorCircuito
{
    /// <summary>O controle que este receptor escuta: o primeiro ligado na mesa (o "bind").</summary>
    PecaNoCircuito ControleLigado()
        => Atual.Pecas.FirstOrDefault(p => p.Tipo == "radio-controle" && !ControleDesligado(p));

    /// <summary>O bit 2 do controle e a chave de energia dele (desligado quando ligado o bit).</summary>
    static bool ControleDesligado(PecaNoCircuito controle) => (controle.Bits & 4) != 0;

    /// <summary>Os seis canais que o controle manda agora, em us (o modo 2 da FlySky).</summary>
    static double[] CanaisDoControle(PecaNoCircuito controle) =>
    [
        1000 + 1000 * Math.Clamp(controle.Giro3, 0, 1),     // CH1 aileron (direito, lados)
        1000 + 1000 * Math.Clamp(controle.Giro2, 0, 1),     // CH2 profundor (direito, cima/baixo)
        1000 + 1000 * Math.Clamp(controle.Giro, 0, 1),      // CH3 acelerador (esquerdo, cima/baixo)
        1000 + 1000 * Math.Clamp(controle.Giro4, 0, 1),     // CH4 leme (esquerdo, lados)
        (controle.Bits & 1) != 0 ? 2000 : 1000,              // CH5 SWA
        (controle.Bits & 2) != 0 ? 2000 : 1000,              // CH6 SWB
    ];

    /// <summary>O receptor esta alimentado (4 V ou mais entre o VCC e o GND)?</summary>
    bool ReceptorAlimentado(PecaNoCircuito rx)
        => EnergiaDoQuadro is { } conta && conta.TensaoDoPino(rx.Id, "VCC") - conta.TensaoDoPino(rx.Id, "GND") >= 4.0;

    /// <summary>Os canais que este receptor poe agora nos fios, ou nulo (sem energia, sem controle: sem pulso).</summary>
    double[] CanaisDoReceptor(PecaNoCircuito rx)
    {
        if (!ReceptorAlimentado(rx)) return null;
        return ControleLigado() is { } controle ? CanaisDoControle(controle) : null;
    }

    /// <summary>
    /// O pulso (us) que um canal de receptor poe no fio deste pino (o SINAL
    /// de um ESC, o PWM de um servo), ou nulo. O GND dos dois tem que ser o
    /// mesmo -- no cabo de servo ele vem junto.
    /// </summary>
    internal double? PulsoDoReceptor(PecaNoCircuito peca, string pino, Netlist rede)
    {
        string raiz = rede.RaizDe(Netlist.No(peca.Id, pino));
        if (raiz is null) return null;
        foreach (var rx in Atual.Pecas)
        {
            if (rx.Tipo != "receptor-rc") continue;
            for (int k = 1; k <= 6; k++)
            {
                if (rede.RaizDe(Netlist.No(rx.Id, $"CH{k}")) != raiz) continue;
                if (rede.RaizDe(Netlist.No(rx.Id, "GND")) != rede.RaizDe(Netlist.No(peca.Id, "GND"))) return null;
                return CanaisDoReceptor(rx)?[k - 1];
            }
        }
        return null;
    }

    /// <summary>Os pinos de placa que um receptor esta movendo (para soltar quando ele sair).</summary>
    readonly HashSet<(Maquina Maq, int Gpio)> pinosDoReceptor = [];

    /// <summary>Onde o proximo quadro iBUS chega, por receptor (relogio dos fios da placa, us).</summary>
    readonly Dictionary<string, double> proximoIbus = [];

    /// <summary>A cada quadro: os sinais que os receptores poem nos pinos das placas.</summary>
    void AplicarReceptores(Bancada bancada, Netlist rede)
    {
        var agoraMovidos = new HashSet<(Maquina, int)>();
        foreach (var rx in Atual.Pecas)
        {
            if (rx.Tipo != "receptor-rc") continue;
            bool alimentado = ReceptorAlimentado(rx);
            var canais = CanaisDoReceptor(rx);

            for (int k = 1; k <= 8; k++)
            {
                string pino = k <= 6 ? $"CH{k}" : k == 7 ? "PPM" : "IBUS";
                var (placa, gpio) = rede.PinoDe(Atual, rx.Id, pino);
                if (placa is null || gpio < 0) continue;
                var maq = bancada.De(placa);
                if (!GndComumComPlaca(rx, "GND", rede))
                {
                    maq.SinaisExternos[gpio] = null;
                    continue;
                }

                if (pino == "IBUS")
                {
                    if (canais is not null) MandarIbus(rx, maq, gpio, canais);
                    continue;
                }

                // Alimentado e sem controle: a saida fica em LOW (sem pulso).
                SinalExterno sinal = !alimentado ? null
                    : canais is null ? SemPulso
                    : pino == "PPM" ? TremPpm(canais)
                    : PulsoUnico(canais[k - 1]);
                if (!MesmoSinal(maq.SinaisExternos[gpio], sinal)) maq.SinaisExternos[gpio] = sinal;
                agoraMovidos.Add((maq, gpio));
            }
        }

        // O receptor saiu (ou o fio): o pino volta a ser so do sketch.
        foreach (var (maq, gpio) in pinosDoReceptor)
            if (!agoraMovidos.Contains((maq, gpio))) maq.SinaisExternos[gpio] = null;
        pinosDoReceptor.Clear();
        pinosDoReceptor.UnionWith(agoraMovidos);
    }

    static readonly SinalExterno SemPulso = new() { PeriodoUs = 20000, Pulsos = [] };

    static SinalExterno PulsoUnico(double us) => new() { PeriodoUs = 20000, Pulsos = [(0, us)] };

    /// <summary>O PPM: um pulso de 300 us no comeco de cada canal (oito), e o resto do quadro de sincronia.</summary>
    static SinalExterno TremPpm(double[] canais)
    {
        var pulsos = new List<(double, double)>();
        double t = 0;
        for (int k = 0; k < 8; k++)
        {
            pulsos.Add((t, 300));
            t += k < canais.Length ? canais[k] : 1500;
        }
        pulsos.Add((t, 300));
        return new SinalExterno { PeriodoUs = 22500, Pulsos = [.. pulsos] };
    }

    static bool MesmoSinal(SinalExterno a, SinalExterno b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a is null || b is null || a.PeriodoUs != b.PeriodoUs || a.Pulsos.Length != b.Pulsos.Length) return false;
        for (int i = 0; i < a.Pulsos.Length; i++)
            if (Math.Abs(a.Pulsos[i].Inicio - b.Pulsos[i].Inicio) > 0.5 || Math.Abs(a.Pulsos[i].Largura - b.Pulsos[i].Largura) > 0.5)
                return false;
        return true;
    }

    /// <summary>
    /// O iBUS: um quadro de 32 bytes a cada 7 ms (0x20 0x40, os 14 canais em
    /// little-endian e o checksum 0xFFFF menos a soma), a 115200 baud, na
    /// UART com o RX neste pino. Baud diferente na placa vira lixo, como na
    /// bancada.
    /// </summary>
    void MandarIbus(PecaNoCircuito rx, Maquina maq, int gpio, double[] canais)
    {
        var portas = maq.PortasNoPino(gpio);
        if (portas.Count == 0) { proximoIbus.Remove(rx.Id); return; }

        double agora = maq.AgoraNoFio;
        double proximo = proximoIbus.GetValueOrDefault(rx.Id, agora);
        if (proximo < agora - 100_000) proximo = agora;
        const double PorByte = 10 * 1e6 / 115200;

        while (proximo <= agora + 50_000)
        {
            var quadro = new byte[32];
            quadro[0] = 0x20;
            quadro[1] = 0x40;
            for (int c = 0; c < 14; c++)
            {
                int us = (int)Math.Round(c < canais.Length ? canais[c] : 1500);
                quadro[2 + 2 * c] = (byte)(us & 0xFF);
                quadro[3 + 2 * c] = (byte)(us >> 8);
            }
            int soma = 0;
            for (int i = 0; i < 30; i++) soma += quadro[i];
            int verifica = 0xFFFF - soma;
            quadro[30] = (byte)(verifica & 0xFF);
            quadro[31] = (byte)(verifica >> 8);

            foreach (var porta in portas)
            {
                bool casa = porta.Baud == 115200;
                for (int i = 0; i < 32; i++)
                    porta.Receber(casa ? quadro[i] : (byte)(((quadro[i] ^ 0xA5) * 7 + i) & 0xFF), proximo + (i + 1) * PorByte);
            }
            proximo += 7000;
        }
        proximoIbus[rx.Id] = proximo;
    }
}
