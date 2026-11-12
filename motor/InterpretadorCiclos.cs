namespace Simulador;

/// <summary>
/// A CPU simulada: cada coisa que o sketch faz custa os ciclos que custaria
/// no chip (<see cref="CustosDaCpu"/>), e esses ciclos viram o tempo da
/// placa. Sem isto o sketch andava na velocidade do computador: um Uno fazia
/// em zero milissegundos o que leva um minuto, e o micros() de um laco media
/// o interpretador, e nao a placa.
///
/// E aqui tambem que moram os watchdogs de verdade de cada chip, no lugar do
/// antigo "loop preso": o ESP8266 reinicia depois de ~3,2 s sem ceder o
/// processador; a ESP32 so vigia o loop quando o sketch pede
/// (enableLoopWDT()); o AVR e o RP2040, quando o sketch arma o watchdog.
/// Nenhuma placa para com mensagem de erro por um laco sem delay() -- e o
/// simulador tambem nao.
/// </summary>
public partial class Interpretador
{
    CustosDaCpu custos;
    CustosDaCpu Custos => custos ??= CustosDaCpu.De(maq.Perfil);

    /// <summary>Os ciclos gastos desde a ultima vez que viraram tempo.</summary>
    double ciclos;

    /// <summary>A cada tantos ciclos a conta vira tempo (e a thread espera, se o chip passou da parede).</summary>
    const double CiclosPorCobranca = 4000;

    /// <summary>
    /// A CPU simulada ligada: o relogio da placa anda com a conta. Desligada,
    /// o sketch roda na velocidade do computador, como antes.
    /// </summary>
    public bool CpuSimulada { get; set; } = true;

    /// <summary>
    /// O sketch marca o proprio ritmo: quem roda o loop() nao deve dormir entre
    /// uma volta e outra -- a CPU simulada ja espera o relogio da placa.
    /// </summary>
    bool RitmoDaCpu => CpuSimulada && Prog?.Montado is not ProgramaMontado;

    double MHz => Math.Max(1, maq.Perfil?.Megahertz ?? 16);

    void Cobrar(double c) => ciclos += c;

    /// <summary>A conta acumulada vira tempo de chip -- e os watchdogs olham o relogio.</summary>
    void PagarCiclos()
    {
        if (ciclos <= 0) return;
        double us = ciclos / MHz;
        ciclos = 0;
        if (!CpuSimulada) return;
        maq.GastarProcessador(us);
        ConferirWatchdogs();
    }

    /// <summary>Um comando: o salto dele, e a cobranca de vez em quando.</summary>
    void CobrarComando()
    {
        ciclos += EhPython ? Custos.PythonPorNo : Custos.Comando;
        if (ciclos >= CiclosPorCobranca) PagarCiclos();
    }

    /// <summary>Ler (ou guardar) uma variavel deste valor.</summary>
    void CobrarLeitura(Valor v) => ciclos += EhPython ? Custos.PythonPorNo : Custos.DeLer(v.Tipo, ModeloC);

    /// <summary>"x++": ler, somar um e guardar, no tipo de x.</summary>
    void CobrarIncremento(Valor novo)
    {
        if (EhPython)
        {
            ciclos += Custos.PythonPorNo;
            return;
        }
        var t = novo.Tipo == TipoC.Nenhum ? TipoC.Long : novo.Tipo;
        ciclos += Custos.DaOperacao("+", t, ModeloC) + 2 * Custos.DeLer(t, ModeloC);
    }

    /// <summary>A conta que o OperarC fez, no tipo em que ela foi feita.</summary>
    void CobrarOperacao(string op, TipoC tipo) => ciclos += Custos.DaOperacao(op, tipo, ModeloC);

    /// <summary>A conta das linguagens sem tipo do C (e do Python): por inteiro ou real.</summary>
    void CobrarOperacaoSemTipo(string op, bool inteiro)
        => ciclos += EhPython ? Custos.PythonPorNo : Custos.DaOperacao(op, inteiro ? TipoC.Long : TipoC.Double, ModeloC);

    /// <summary>Uma funcao do sketch chamada: o call, o ret, os registradores salvos.</summary>
    void CobrarChamada(int argumentos)
        => ciclos += EhPython ? Custos.PythonPorNo * (2 + argumentos) : Custos.ChamarFuncao + argumentos * Custos.LerPalavra;

    /// <summary>A conversao de um cast: inteiro para real e volta custam a rotina de conversao.</summary>
    void CobrarConversao(Valor de, string para)
    {
        var alvo = TiposC.DoNome(para, ModeloC);
        bool deReal = TiposC.EhReal(de.Tipo) || (de.Tipo == TipoC.Nenhum && !de.Inteiro);
        bool paraReal = TiposC.EhReal(alvo);
        if (deReal == paraReal) { ciclos += 1; return; }
        bool dbl = (paraReal ? alvo : de.Tipo) is TipoC.Double or TipoC.LongDouble && ModeloC.BitsDoDouble == 64;
        ciclos += dbl ? Custos.DoubleConverte : Custos.FloatConverte;
    }

    /// <summary>
    /// Uma funcao do core ou de biblioteca: a conta de antes vira tempo ANTES
    /// dela (o digitalWrite tem que cair no instante certo), e ela custa o
    /// dela.
    /// </summary>
    void AntesDoCore()
    {
        ciclos += Custos.ChamarDoCore;
        PagarCiclos();
    }

    /// <summary>O custo da funcao da math.h, somado depois de a conta ser feita.</summary>
    void CobrarMatematica(string nome, Valor resultado)
    {
        bool emFloat = resultado.Tipo == TipoC.Float || ModeloC.BitsDoDouble == 32;
        ciclos += Custos.DaMatematica(nome, emFloat);
    }

    /// <summary>O print de um numero: cada digito e uma divisao por 10 no chip.</summary>
    void CobrarImpressao(string texto, bool numero)
    {
        if (string.IsNullOrEmpty(texto)) return;
        ciclos += numero ? texto.Length * Custos.PorDigitoImpresso : texto.Length * Custos.PorByteDeTexto;
    }

    // =====================================================================
    //  os watchdogs de cada chip
    // =====================================================================

    /// <summary>ESP32: o loop vigiado pelo task watchdog (enableLoopWDT, esp_task_wdt_add).</summary>
    bool loopVigiado;

    /// <summary>O watchdog de hardware armado pelo sketch: AVR (wdt_enable) e RP2040 (wdt_begin).</summary>
    double prazoDoWatchdogUs;

    /// <summary>Quando o watchdog armado foi alimentado pela ultima vez, no relogio do chip.</summary>
    double watchdogAlimentadoEmUs;

    /// <summary>O ESP8266 com o soft watchdog desligado (ESP.wdtDisable()): sobra o de hardware.</summary>
    bool softWdtDesligado;

    const double SoftWdtDoEsp8266Us = 3_200_000;
    const double HardWdtDoEsp8266Us = 8_300_000;
    const double TaskWdtDaEsp32Us = 5_000_000;

    bool EhEsp8266 => maq.Perfil?.Processador == "ESP8266";
    bool EhFamiliaEsp32 => maq.Perfil?.Processador is "Xtensa LX6" or "Xtensa LX7" or "RISC-V";

    /// <summary>O sketch cedeu o processador (delay, yield, a volta do loop): o ESP8266 respira.</summary>
    void Ceder() => maq.Cedeu();

    void ConferirWatchdogs()
    {
        if (EhEsp8266)
        {
            double limite = softWdtDesligado ? HardWdtDoEsp8266Us : SoftWdtDoEsp8266Us;
            if (maq.UsSemCeder > limite) DispararWatchdogDoEsp8266(duro: softWdtDesligado);
        }
        else if (loopVigiado && EhFamiliaEsp32 && maq.UsSemCeder > TaskWdtDaEsp32Us)
            DispararTaskWatchdog();

        if (prazoDoWatchdogUs > 0 && maq.AgoraUs() - watchdogAlimentadoEmUs > prazoDoWatchdogUs)
            DispararWatchdogDeHardware();
    }

    /// <summary>
    /// Quanto do delay cabe antes do watchdog armado estourar. O watchdog do
    /// AVR nao para no delay(): um delay(5000) com wdt_enable(WDTO_2S) reinicia
    /// a placa no meio dele.
    /// </summary>
    long DelayAteOWatchdog(long ms)
    {
        if (prazoDoWatchdogUs <= 0) return ms;
        double resta = (watchdogAlimentadoEmUs + prazoDoWatchdogUs - maq.AgoraUs()) / 1000.0;
        return resta < ms ? Math.Max(0, (long)Math.Ceiling(resta)) : ms;
    }

    void DispararWatchdogDoEsp8266(bool duro)
    {
        Escrever("", true);
        if (duro)
        {
            // O de hardware nao passa pelo tratador: a placa so reinicia.
            Escrever(" ets Jan  8 2013,rst cause:4, boot mode:(3,6)", true);
            Escrever("", true);
            Escrever("wdt reset", true);
        }
        else
        {
            Escrever("--------------- CUT HERE FOR EXCEPTION DECODER ---------------", true);
            Escrever("", true);
            Escrever("Soft WDT reset", true);
            Escrever("", true);
            Escrever("Exception (4):", true);
            Escrever("epc1=0x40201c04 epc2=0x00000000 epc3=0x00000000 excvaddr=0x00000000 depc=0x00000000", true);
            Escrever("", true);
            Escrever(">>>stack>>>", true);
            Escrever("", true);
            Escrever("ctx: cont", true);
            Escrever("<<<stack<<<", true);
            Escrever("", true);
            Escrever("--------------- CUT HERE FOR EXCEPTION DECODER ---------------", true);
        }
        maq.Avisar(Idioma.T("o ESP8266 ficou {0:0.0} s sem ceder o processador e o watchdog {1} reiniciou a placa -- como na bancada. Um laco longo precisa de um delay() ou yield() no meio (o WiFi do ESP8266 depende disso).", maq.UsSemCeder / 1e6, (duro ? Idioma.T("de hardware") : Idioma.T("de software (~3,2 s)"))));
        throw Reiniciar(duro ? ReinicioDaPlaca.PeloWatchdogDeHardware : ReinicioDaPlaca.PeloWatchdog, 0, 0);
    }

    void DispararTaskWatchdog()
    {
        long ms = (long)(maq.AgoraUs() / 1000);
        int nucleo = (maq.Perfil?.Nucleos ?? 1) > 1 ? 1 : 0;
        Escrever($"E ({ms}) task_wdt: Task watchdog got triggered. The following tasks/users did not reset the watchdog in time:", true);
        Escrever($"E ({ms}) task_wdt:  - loopTask (CPU {nucleo})", true);
        Escrever($"E ({ms}) task_wdt: Tasks currently running:", true);
        if (nucleo == 1) Escrever($"E ({ms}) task_wdt: CPU 0: IDLE0", true);
        Escrever($"E ({ms}) task_wdt: CPU {nucleo}: loopTask", true);
        Escrever($"E ({ms}) task_wdt: Aborting.", true);
        Escrever("", true);
        Escrever($"abort() was called at PC 0x420178e7 on core {nucleo}", true);
        Escrever("", true);
        Escrever("Rebooting...", true);
        maq.Avisar(Idioma.T("o loop() ficou mais de 5 s sem voltar com o watchdog do loop ligado (enableLoopWDT), e a placa reiniciou -- como na bancada."));
        throw Reiniciar(ReinicioDaPlaca.PeloWatchdog, 0, 0);
    }

    void DispararWatchdogDeHardware()
    {
        maq.Avisar(Idioma.T("o watchdog armado pelo sketch ({0:0} ms) nao foi alimentado a tempo e reiniciou a placa, como na bancada.", prazoDoWatchdogUs / 1000));
        prazoDoWatchdogUs = 0;
        throw Reiniciar(ReinicioDaPlaca.PeloWatchdogDeHardware, 0, 0);
    }

    /// <summary>
    /// As funcoes de watchdog de cada core: o avr/wdt.h, o do ESP8266, o do
    /// loop da ESP32, o do RP2040. Falso: nao e uma delas.
    /// </summary>
    bool ChamarWatchdog(string nome, List<Valor> args, int linha, out Valor resultado)
    {
        resultado = Valor.Vazio;
        double A(int i) => i < args.Count ? args[i].Num : 0;

        switch (nome)
        {
            // avr/wdt.h: WDTO_15MS (0) ate WDTO_8S (9).
            case "wdt_enable":
                if (!maq.Perfil.De8Bits)
                    throw new ErroDeExecucao(Idioma.T("wdt_enable() e do avr/wdt.h: so existe nas placas AVR."), linha);
                prazoDoWatchdogUs = PrazoDoWdtDoAvr((int)A(0)) * 1000.0;
                watchdogAlimentadoEmUs = maq.AgoraUs();
                return true;
            case "wdt_reset":
                watchdogAlimentadoEmUs = maq.AgoraUs();
                return true;
            case "wdt_disable":
                prazoDoWatchdogUs = 0;
                return true;

            // ESP32 (core 3.x): o loop vigiado pelo task watchdog.
            case "enableLoopWDT":
                loopVigiado = true;
                return true;
            case "disableLoopWDT":
                loopVigiado = false;
                return true;
            case "feedLoopWDT":
            case "esp_task_wdt_reset":
                if (EhFamiliaEsp32) Ceder();
                resultado = Valor.DeInt(0);
                return true;
            case "esp_task_wdt_add":
                loopVigiado = true;
                resultado = Valor.DeInt(0);
                return true;
            case "esp_task_wdt_delete":
                loopVigiado = false;
                resultado = Valor.DeInt(0);
                return true;
            case "esp_task_wdt_init":
            case "esp_task_wdt_reconfigure":
            case "esp_task_wdt_deinit":
            case "enableCore0WDT": case "disableCore0WDT":
            case "enableCore1WDT": case "disableCore1WDT":
                resultado = Valor.DeInt(0);
                return true;
        }
        return false;
    }

    /// <summary>Os prazos do WDTO_* do avr/wdt.h, em ms.</summary>
    static double PrazoDoWdtDoAvr(int codigo) => codigo switch
    {
        0 => 15, 1 => 30, 2 => 60, 3 => 120, 4 => 250, 5 => 500, 6 => 1000, 7 => 2000, 8 => 4000, 9 => 8000,
        _ => 2000,
    };

    /// <summary>ESP.wdtEnable/wdtDisable/wdtFeed do ESP8266.</summary>
    bool WatchdogDoEsp(string metodo)
    {
        switch (metodo)
        {
            case "wdtFeed": Ceder(); return true;
            case "wdtDisable": softWdtDesligado = true; return true;
            case "wdtEnable": softWdtDesligado = false; Ceder(); return true;
        }
        return false;
    }

    /// <summary>rp2040.wdt_begin(ms) e rp2040.wdt_reset() do core da Pico.</summary>
    bool WatchdogDoRp2040(string metodo, List<Valor> args)
    {
        switch (metodo)
        {
            case "wdt_begin":
                prazoDoWatchdogUs = (args.Count > 0 ? args[0].Num : 1000) * 1000.0;
                watchdogAlimentadoEmUs = maq.AgoraUs();
                return true;
            case "wdt_reset":
                watchdogAlimentadoEmUs = maq.AgoraUs();
                return true;
        }
        return false;
    }
}
