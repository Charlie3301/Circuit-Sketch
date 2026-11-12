namespace Simulador;

/// <summary>
/// O machine.Timer, o machine.RTC, o Pin.irq e o machine.SPI do MicroPython
/// (Alpha 24).
///
/// O Timer e o Pin.irq chamam uma funcao do sketch (um def ou uma lambda) com
/// o proprio objeto de argumento, como no MicroPython: "def piscar(t):",
/// "lambda p: print(p.value())". O callback do Timer vence no relogio da
/// placa -- inclusive durante o time.sleep(), que anda de vencimento em
/// vencimento, e por isso um Timer de 500 ms pisca no tempo certo com o laco
/// principal dormindo 5 s.
/// </summary>
public partial class Interpretador
{
    sealed class TimerPy
    {
        public Objeto Objeto;
        public Rotina Rotina;
        public double Periodo;     // ms
        public double Proximo;     // ms da placa
        public bool Periodico;
    }

    readonly List<TimerPy> timersPy = [];

    /// <summary>As rotinas do Pin.irq, pelo nome que a Maquina vigia ("__irq_py_12"), com o Pin que vai de argumento.</summary>
    readonly Dictionary<string, (Rotina Rotina, Valor Argumento)> rotinasPy = [];

    /// <summary>O relogio do RTC: a hora acertada (ou a do computador, na partida) e o ms da placa em que foi.</summary>
    DateTime? rtcAcertadoEm;
    long rtcMsDoAcerto;

    /// <summary>Ha o que atender entre os comandos: interrupcao de pino ou Timer do Python.</summary>
    bool TemAtendimento => maq.Interrupcoes.Count > 0 || timersPy.Count > 0;

    /// <summary>A funcao que o sketch passou: o nome de um def, ou uma lambda.</summary>
    Rotina RotinaDeValor(Valor v, string ondeUsou, int linha)
    {
        if (v.Referencia is Rotina r) return r;
        if (v.EhTexto && funcoes.TryGetValue(v.Texto, out var f)) return new Rotina { Funcao = f, Captura = Global };
        throw new ErroDeExecucao(Idioma.T("{0}: o callback tem que ser uma funcao do sketch (def) ou uma lambda", ondeUsou), linha);
    }

    // =====================================================================
    //  Timer
    // =====================================================================

    Valor NovoTimerPy(List<Valor> args, Dictionary<string, Valor> nomeados, int linha)
    {
        var o = new Objeto("Timer");
        o.Campos["id"] = args.Count > 0 ? args[0] : Valor.DeInt(-1);
        if (nomeados.Count > 0) IniciarTimerPy(o, nomeados, linha);
        return Valor.DeReferencia(o);
    }

    /// <summary>"tim.init(period=500, mode=Timer.PERIODIC, callback=f)" -- ou freq= no lugar do period.</summary>
    void IniciarTimerPy(Objeto o, Dictionary<string, Valor> nomeados, int linha)
    {
        timersPy.RemoveAll(t => t.Objeto == o);
        if (!nomeados.TryGetValue("callback", out var cb) || cb.Referencia is NadaPy) return;
        double periodo = nomeados.TryGetValue("period", out var p) ? p.Num
            : nomeados.TryGetValue("freq", out var fr) && fr.Num > 0 ? 1000.0 / fr.Num
            : -1;
        if (periodo <= 0)
            throw new ErroDeExecucao(Idioma.T("Timer.init(): falta o period (em ms) ou a freq (em Hz)"), linha);
        bool periodico = !nomeados.TryGetValue("mode", out var modo) || modo.Num != 0;   // ONE_SHOT = 0
        timersPy.Add(new TimerPy
        {
            Objeto = o,
            Rotina = RotinaDeValor(cb, "Timer.init()", linha),
            Periodo = Math.Max(periodo, 1),
            Proximo = maq.Ms + Math.Max(periodo, 1),
            Periodico = periodico,
        });
    }

    Valor ChamarTimerPy(Objeto o, string metodo, Dictionary<string, Valor> nomeados, int linha)
    {
        switch (metodo)
        {
            case "init": IniciarTimerPy(o, nomeados, linha); return Valor.Vazio;
            case "deinit": timersPy.RemoveAll(t => t.Objeto == o); return Valor.Vazio;
            case "value":
            {
                var t = timersPy.FirstOrDefault(x => x.Objeto == o);
                return Valor.DeInt(t is null ? 0 : (long)Math.Max(0, t.Proximo - maq.Ms));
            }
        }
        throw new ErroDeExecucao(Idioma.T("Timer.{0}() ainda nao e suportado pelo simulador. O Timer tem init, deinit e value.", metodo), linha);
    }

    /// <summary>Os Timers vencidos: cada um chama o callback dele (com o Timer de argumento).</summary>
    void AtenderTimersPy()
    {
        if (timersPy.Count == 0) return;
        for (int volta = 0; volta < 1000; volta++)
        {
            var vencido = timersPy.Where(t => t.Proximo <= maq.Ms).MinBy(t => t.Proximo);
            if (vencido is null) return;
            if (vencido.Periodico) vencido.Proximo += vencido.Periodo;
            else timersPy.Remove(vencido);
            long antes = passos;
            try { ChamarRotina(vencido.Rotina, Valor.DeReferencia(vencido.Objeto)); }
            finally { passos = antes; }
        }
    }

    /// <summary>
    /// O time.sleep() com Timer ligado: anda ate o proximo vencimento, atende,
    /// e segue -- o callback roda no meio do sono, como na placa.
    /// </summary>
    void DormirComTimers(long ms)
    {
        long fim = maq.Ms + ms;
        while (maq.Ms < fim && !maq.Parar)
        {
            double proximo = timersPy.Count > 0 ? timersPy.Min(t => t.Proximo) : fim;
            long passo = (long)Math.Clamp(Math.Ceiling(proximo - maq.Ms), 1, fim - maq.Ms);
            maq.Avancar(passo);
            AtenderInterrupcoes();
        }
    }

    // =====================================================================
    //  Pin.irq
    // =====================================================================

    /// <summary>"botao.irq(trigger=Pin.IRQ_FALLING, handler=apertou)"; handler=None desliga.</summary>
    Valor IrqDoPino(Objeto pino, List<Valor> args, Dictionary<string, Valor> nomeados, int linha)
    {
        string nome = "__irq_py_" + pino.Pino;
        var handler = nomeados.TryGetValue("handler", out var h) ? h : args.Count > 0 ? args[0] : Valor.Vazio;
        int borda = nomeados.TryGetValue("trigger", out var tr) ? (int)tr.Num : args.Count > 1 ? (int)args[1].Num : 3;
        if (handler.Referencia is NadaPy || (handler.Referencia is null && !handler.EhTexto))
        {
            maq.PararDeVigiar(pino.Pino);
            rotinasPy.Remove(nome);
            return Valor.Vazio;
        }
        rotinasPy[nome] = (RotinaDeValor(handler, "Pin.irq()", linha), Valor.DeReferencia(pino));
        maq.Vigiar(pino.Pino, nome, borda is >= 1 and <= 3 ? borda : 3);
        return Valor.DeReferencia(pino);
    }

    /// <summary>A rotina do Python que uma interrupcao de pino chama (nula: nao e do Python).</summary>
    bool ChamarIrqPy(string nome)
    {
        if (!rotinasPy.TryGetValue(nome, out var irq)) return false;
        long antes = passos;
        try { ChamarRotina(irq.Rotina, irq.Argumento); }
        finally { passos = antes; }
        return true;
    }

    // =====================================================================
    //  RTC
    // =====================================================================

    /// <summary>A hora do RTC agora: a acertada (ou a do computador na partida) mais o que a placa andou.</summary>
    DateTime HoraDoRtcPy()
    {
        if (rtcAcertadoEm is null)
        {
            rtcAcertadoEm = DateTime.Now;
            rtcMsDoAcerto = maq.Ms;
        }
        return rtcAcertadoEm.Value.AddMilliseconds(maq.Ms - rtcMsDoAcerto);
    }

    Valor ChamarRtcPy(List<Valor> args, string metodo, int linha)
    {
        switch (metodo)
        {
            // datetime() le; datetime((ano, mes, dia, dia_da_semana, hora, min, seg, sub)) acerta.
            case "datetime" or "init":
            {
                if (args.Count > 0 && args[0].Referencia is ListaPy t)
                {
                    long N(int i) => i < t.Itens.Count ? (long)t.Itens[i].Num : 0;
                    try
                    {
                        rtcAcertadoEm = new DateTime((int)N(0), (int)Math.Max(1, N(1)), (int)Math.Max(1, N(2)),
                                                     (int)N(4), (int)N(5), (int)N(6)).AddMilliseconds(N(7) / 1000.0);
                    }
                    catch (ArgumentOutOfRangeException)
                    {
                        throw new ErroDeExecucao(Idioma.T("RTC.datetime(): data invalida (ano, mes, dia, dia_da_semana, hora, minuto, segundo, subsegundo)"), linha);
                    }
                    rtcMsDoAcerto = maq.Ms;
                    return Valor.Vazio;
                }
                var agora = HoraDoRtcPy();
                // O dia da semana do MicroPython: segunda = 0.
                return Valor.DeReferencia(ListaPy.De(
                    Valor.DeInt(agora.Year), Valor.DeInt(agora.Month), Valor.DeInt(agora.Day),
                    Valor.DeInt(((int)agora.DayOfWeek + 6) % 7), Valor.DeInt(agora.Hour), Valor.DeInt(agora.Minute),
                    Valor.DeInt(agora.Second), Valor.DeInt(agora.Millisecond * 1000)));
            }
            case "memory": return Valor.DeTexto("");
        }
        throw new ErroDeExecucao(Idioma.T("RTC.{0}() ainda nao e suportado pelo simulador. O RTC tem datetime e init.", metodo), linha);
    }

    // =====================================================================
    //  SPI
    // =====================================================================

    /// <summary>
    /// "SPI(1, baudrate=..., sck=Pin(14), mosi=Pin(13), miso=Pin(12))" ou o
    /// SoftSPI. Sem os pinos, os do barramento: na ESP32, o SPI(1) e o HSPI
    /// (14, 13, 12) e o SPI(2) o VSPI (18, 23, 19). Por baixo e o mesmo
    /// barramento do SPI do C++: os bytes vao para as pecas do circuito.
    /// </summary>
    Valor NovoSpiPy(string classe, List<Valor> args, Dictionary<string, Valor> nomeados, int linha)
    {
        int id = args.Count > 0 && classe == "SPI" ? (int)args[0].Num : 2;
        var b = new BarramentoDoSketch { Nome = $"SPI({id})" };
        (b.Sck, b.Mosi, b.Miso) = id == 1 ? (14, 13, 12) : (18, 23, 19);
        if (EhRp2040) (b.Sck, b.Mosi, b.Miso) = id == 1 ? (10, 11, 12) : (18, 19, 16);
        var o = new Objeto(classe) { Rede = b };
        ConfigurarSpiPy(o, b, nomeados, linha);
        return Valor.DeReferencia(o);
    }

    void ConfigurarSpiPy(Objeto o, BarramentoDoSketch b, Dictionary<string, Valor> nomeados, int linha)
    {
        int Pino(string nome, int padrao) => nomeados.TryGetValue(nome, out var v) && v.Referencia is not NadaPy ? NumeroDePino(v, linha) : padrao;
        b.Sck = Pino("sck", b.Sck);
        b.Mosi = Pino("mosi", b.Mosi);
        b.Miso = Pino("miso", b.Miso);
        b.Hz = nomeados.TryGetValue("baudrate", out var hz) ? hz.Num : b.Hz > 0 ? b.Hz : 1_000_000;
        int polaridade = nomeados.TryGetValue("polarity", out var pol) ? (int)pol.Num : b.Modo >> 1;
        int fase = nomeados.TryGetValue("phase", out var fa) ? (int)fa.Num : b.Modo & 1;
        b.Modo = polaridade << 1 | fase;
        b.Msb = !nomeados.TryGetValue("firstbit", out var primeiro) || primeiro.Num == 0;   // MSB = 0
        b.Iniciado = true;
        maq.PinMode(b.Sck, 1);
        maq.PinMode(b.Mosi, 1);
        if (b.Miso >= 0) maq.PinMode(b.Miso, 0);
        maq.PinoDoPeriferico(b.Sck, polaridade, 0);
    }

    Valor ChamarSpiPy(Objeto o, string metodo, List<Valor> args, Dictionary<string, Valor> nomeados, int linha)
    {
        var b = (BarramentoDoSketch)o.Rede;
        int Troca(int valor) => (int)Transferir(b, valor & 0xFF, 8, linha);
        int Enchimento() => nomeados.TryGetValue("write", out var w) ? (int)w.Num : args.Count > 1 ? (int)args[1].Num : 0;

        switch (metodo)
        {
            case "init": ConfigurarSpiPy(o, b, nomeados, linha); return Valor.Vazio;
            case "deinit": b.Iniciado = false; return Valor.Vazio;

            case "write":
            {
                var bytes = args.Count > 0 ? BytesPy(args[0]) : [];
                foreach (byte x in bytes) Troca(x);
                return Valor.Vazio;
            }

            // read(n, write=0): le n bytes mandando o enchimento.
            case "read":
            {
                int n = args.Count > 0 ? (int)args[0].Num : 0, enche = Enchimento();
                var lidos = new byte[Math.Max(0, n)];
                for (int i = 0; i < lidos.Length; i++) lidos[i] = (byte)Troca(enche);
                return Valor.DeTexto(TextoArduino.DeBytes(lidos));
            }

            // readinto(buf, write=0): enche o bytearray no lugar.
            case "readinto":
            {
                if (args.Count == 0 || args[0].Referencia is not ListaPy alvo)
                    throw new ErroDeExecucao(Idioma.T("SPI.readinto() precisa de um bytearray: buf = bytearray(4)"), linha);
                int enche = Enchimento();
                for (int i = 0; i < alvo.Itens.Count; i++) alvo.Itens[i] = Valor.DeInt(Troca(enche));
                return Valor.Vazio;
            }

            // write_readinto(saida, entrada): manda um e recebe o outro, byte a byte.
            case "write_readinto":
            {
                var saida = args.Count > 0 ? BytesPy(args[0]) : [];
                if (args.Count < 2 || args[1].Referencia is not ListaPy entrada)
                    throw new ErroDeExecucao(Idioma.T("SPI.write_readinto() precisa do bytearray de entrada"), linha);
                for (int i = 0; i < saida.Length; i++)
                {
                    int lido = Troca(saida[i]);
                    if (i < entrada.Itens.Count) entrada.Itens[i] = Valor.DeInt(lido);
                }
                return Valor.Vazio;
            }
        }
        throw new ErroDeExecucao(Idioma.T("{0}.{1}() ainda nao e suportado pelo simulador. O SPI tem init, deinit, write, read, readinto e write_readinto.", o.Classe, metodo), linha);
    }
}
