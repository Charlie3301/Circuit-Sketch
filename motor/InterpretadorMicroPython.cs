namespace Simulador;

/// <summary>
/// Os modulos que o MicroPython traz de fabrica.
///
/// A lista existe para o erro ser util: quem escreve "import requests"
/// precisa ler que esse modulo nao existe AQUI e ver quais existem, em
/// vez de descobrir uma linha depois que "requests" nao foi declarado.
/// </summary>
public static class ModulosPy
{
    static readonly Dictionary<string, string[]> Tudo = new()
    {
        ["machine"] = ["Pin", "ADC", "PWM", "I2C", "SoftI2C", "SPI", "SoftSPI",
                       "Timer", "RTC", "UART", "reset", "freq", "unique_id", "idle"],
        ["time"] = ["sleep", "sleep_ms", "sleep_us", "ticks_ms", "ticks_us",
                    "ticks_diff", "ticks_add", "time", "localtime", "mktime"],
        ["utime"] = ["sleep", "sleep_ms", "sleep_us", "ticks_ms", "ticks_us",
                     "ticks_diff", "ticks_add", "time", "localtime", "mktime"],
        ["network"] = ["WLAN", "STA_IF", "AP_IF"],
        ["dht"] = ["DHT11", "DHT22"],
        ["neopixel"] = ["NeoPixel"],
        ["ssd1306"] = ["SSD1306_I2C", "SSD1306_SPI"],
        ["ujson"] = ["dumps", "loads"],
        ["json"] = ["dumps", "loads"],
        ["umqtt.simple"] = ["MQTTClient"],
        ["umqtt.robust"] = ["MQTTClient"],
        ["urequests"] = ["get", "post"],
        ["math"] = ["pi", "e", "sin", "cos", "tan", "asin", "acos", "atan", "atan2",
                    "sqrt", "pow", "exp", "log", "floor", "ceil", "fabs", "radians",
                    "degrees", "trunc", "fmod"],
        ["random"] = ["random", "randint", "randrange", "choice", "seed", "uniform",
                      "getrandbits"],
        ["urandom"] = ["random", "randint", "randrange", "choice", "seed", "uniform",
                       "getrandbits"],
        ["sys"] = ["exit", "platform", "version", "implementation"],
        ["gc"] = ["collect", "mem_free", "mem_alloc", "enable", "disable"],
        ["os"] = ["listdir", "mkdir", "remove", "rename", "uname"],
        ["micropython"] = ["const"],
        ["esp32"] = ["raw_temperature", "hall_sensor"],
        ["esp"] = ["osdebug"],
        ["binascii"] = ["hexlify", "unhexlify"],
        ["ubinascii"] = ["hexlify", "unhexlify"],
        ["struct"] = ["pack", "unpack"],
        ["ustruct"] = ["pack", "unpack"],
    };

    public static IEnumerable<string> Conhecidos => Tudo.Keys.OrderBy(k => k);

    public static bool Existe(string modulo) => Tudo.ContainsKey(modulo);

    public static string[] NomesDe(string modulo)
        => Tudo.TryGetValue(modulo, out var nomes) ? nomes : [];
}

/// <summary>
/// As pontes do MicroPython com as pecas do circuito.
///
/// Mesma regra do lado C++: aqui so mora a TRADUCAO. "pino.value(1)" e
/// "digitalWrite(pino, HIGH)" terminam os dois em Maquina.DigitalWrite,
/// e por isso o LED no desenho nao sabe -- nem precisa saber -- em que
/// linguagem o sketch foi escrito.
/// </summary>
public partial class Interpretador
{
    /// <summary>O sketch desta placa esta em MicroPython.</summary>
    bool EhPython => Prog.Linguagem == Linguagem.MicroPython;

    // =====================================================================
    //  constantes de modulo
    // =====================================================================
    /// <summary>
    /// "Pin.OUT", "network.STA_IF", "ADC.ATTN_11DB".
    ///
    /// Os numeros sao os do MicroPython de verdade. Isso importa quando
    /// o sketch guarda o modo numa variavel e so depois o usa.
    /// </summary>
    Valor ValorDeModulo(string modulo, string caminho, int linha)
    {
        switch (modulo + "." + caminho)
        {
            case "machine.Pin.IN": return Valor.DeInt(1);
            case "machine.Pin.OUT": return Valor.DeInt(3);
            case "machine.Pin.OPEN_DRAIN": return Valor.DeInt(7);
            case "machine.Pin.PULL_UP": return Valor.DeInt(2);
            case "machine.Pin.PULL_DOWN": return Valor.DeInt(1);
            case "machine.Pin.PULL_HOLD": return Valor.DeInt(4);
            case "machine.Pin.IRQ_RISING": return Valor.DeInt(1);
            case "machine.Pin.IRQ_FALLING": return Valor.DeInt(2);

            case "machine.ADC.ATTN_0DB": return Valor.DeInt(0);
            case "machine.ADC.ATTN_2_5DB": return Valor.DeInt(1);
            case "machine.ADC.ATTN_6DB": return Valor.DeInt(2);
            case "machine.ADC.ATTN_11DB": return Valor.DeInt(3);
            case "machine.ADC.WIDTH_12BIT": return Valor.DeInt(3);

            case "network.STA_IF": return Valor.DeInt(0);
            case "network.AP_IF": return Valor.DeInt(1);

            case "math.pi": return Valor.DeReal(Math.PI);
            case "math.e": return Valor.DeReal(Math.E);

            case "sys.platform": return Valor.DeTexto("esp32");
            case "sys.version": return Valor.DeTexto("3.4.0");
        }

        // "machine.Pin" sem chamar: e a classe, que so serve para
        // construir depois ou para ler uma constante dela.
        if (ModulosPy.NomesDe(modulo).Contains(caminho.Split('.')[0]))
            return Valor.DeReferencia(new MembroDeModuloPy(modulo, caminho));

        throw new ErroDeExecucao($"'{modulo}.{caminho}' nao existe no simulador", linha);
    }

    // =====================================================================
    //  chamadas
    // =====================================================================
    /// <summary>
    /// Os argumentos de uma chamada, separando os que vieram com nome.
    ///
    /// "I2C(0, scl=Pin(22), sda=Pin(21))" so faz sentido se scl e sda
    /// chegarem pelo nome: a ordem deles muda de sketch para sketch.
    /// </summary>
    (List<Valor> Pos, Dictionary<string, Valor> Nomeados) Argumentos(Chamada c, Ambiente amb)
    {
        var pos = new List<Valor>();
        var nomeados = new Dictionary<string, Valor>();

        foreach (var a in c.Args)
        {
            if (a is ArgNomeado n) nomeados[n.Nome] = Avaliar(n.Valor, amb);
            else pos.Add(Avaliar(a, amb));
        }

        return (pos, nomeados);
    }

    /// <summary>
    /// Toda chamada de um sketch em Python passa por aqui primeiro.
    ///
    /// Devolve false quando nao e coisa de Python -- e entao segue o
    /// caminho normal, que atende inclusive as funcoes que o proprio
    /// sketch declarou com "def".
    /// </summary>
    bool ChamarPython(Chamada c, Ambiente amb, out Valor r)
    {
        r = Valor.Vazio;

        // ---- metodo: alvo.metodo(...) ----
        if (c.Alvo is Membro m)
        {
            // "super().__init__(nome)": o metodo da classe de cima, no mesmo objeto.
            if (m.Obj is Chamada { Alvo: Variavel { Nome: "super" } })
            {
                r = ChamarDoSuper(m.Nome, c, amb);
                return true;
            }

            var dono = AvaliarSeDer(m.Obj, amb);
            if (dono is null) return false;

            switch (dono.Value.Referencia)
            {
                case ModuloPy mod:
                    r = ChamarDeModulo(mod.Nome, m.Nome, c, amb);
                    return true;

                case MembroDeModuloPy membro:
                    r = ChamarDeModulo(membro.Modulo, membro.Nome + "." + m.Nome, c, amb);
                    return true;

                // Um objeto de classe do sketch: o metodo dela.
                case Objeto doSketch when doSketch.Escopo is not null && classes.ContainsKey(doSketch.Classe):
                    r = ChamarObjeto(doSketch, m.Nome, c, amb);
                    return true;

                case Objeto o:
                {
                    var (pos, nomeados) = Argumentos(c, amb);
                    r = ChamarPecaPy(o, m.Nome, pos, nomeados, c, amb);
                    return true;
                }

                case ListaPy lista:
                    r = ChamarNaLista(lista, m.Nome, Argumentos(c, amb).Pos, c.Linha);
                    return true;

                case DicionarioPy dic:
                    r = ChamarNoDicionario(dic, m.Nome, Argumentos(c, amb).Pos, c.Linha);
                    return true;
            }

            if (dono.Value.EhTexto)
            {
                r = ChamarNoTexto(dono.Value.Texto, m.Nome, Argumentos(c, amb).Pos, c.Linha);
                return true;
            }

            return false;
        }

        if (c.Alvo is not Variavel v) return false;

        // ---- uma classe do sketch, ou uma excecao: "Botao(14)", "ValueError('x')" ----
        if (!funcoes.ContainsKey(v.Nome) && (classes.ContainsKey(v.Nome) || EhExcecaoEmbutida(v.Nome))
            && InstanciarPython(v.Nome, [.. c.Args.Select(a => Avaliar(a, amb))], amb, c.Linha) is { } instancia)
        {
            r = instancia;
            return true;
        }

        // ---- construtor importado direto: "Pin(2, Pin.OUT)" ----
        if (amb.Buscar(v.Nome, out var achado)
            && achado.Referencia is MembroDeModuloPy classe)
        {
            r = ChamarDeModulo(classe.Modulo, classe.Nome, c, amb);
            return true;
        }

        // ---- embutidas ----
        return ChamarEmbutida(v.Nome, c, amb, out r);
    }

    /// <summary>Uma funcao ou classe de dentro de um modulo.</summary>
    Valor ChamarDeModulo(string modulo, string nome, Chamada c, Ambiente amb)
    {
        var (args, nomeados) = Argumentos(c, amb);
        string S(int i) => i < args.Count ? args[i].Mostrar() : "";
        int linha = c.Linha;

        // O modulo pode ser o apelido: "import utime as t" -> t.sleep().
        string real = modulosImportados.GetValueOrDefault(modulo, modulo);

        switch (real)
        {
            case "machine": return DoMachine(nome, args, nomeados, linha);
            case "time" or "utime": return DoTempo(nome, args, linha);
            case "network": return DoRede(nome, args, linha);
            case "dht": return NovaPeca(nome == "DHT11" ? "DHT11" : "DHT22", args, linha);
            case "neopixel": return NovoNeoPixel(args, nomeados, linha);
            case "ssd1306": return NovoOled(nome, args, nomeados, linha);
            case "ujson" or "json": return DoJson(nome, args, linha);
            case "umqtt.simple" or "umqtt.robust": return NovoMqtt(args, nomeados, linha);
            case "math": return DoMatematica(nome, args, linha);
            case "random" or "urandom": return DoSorteio(nome, args, linha);
            case "gc": return nome == "mem_free" ? Valor.DeInt(96000) : Valor.Vazio;
            case "micropython": return nome == "const" && args.Count > 0 ? args[0] : Valor.Vazio;
            case "esp32":
                return nome == "raw_temperature"
                    ? Valor.DeInt(128) : Valor.DeInt(0);
            case "esp": return Valor.Vazio;

            case "sys":
                if (nome == "exit") throw new ExecucaoInterrompida();
                return Valor.Vazio;

            case "os":
                if (nome == "uname") return Valor.DeTexto("esp32");
                return Valor.Vazio;

            case "binascii" or "ubinascii":
                return nome == "hexlify"
                    ? Valor.DeTexto(string.Concat(S(0).Select(ch => ((int)ch).ToString("x2"))))
                    : Valor.DeTexto(S(0));

            case "urequests":
                throw new ErroDeExecucao(
                    "urequests ainda nao e suportado. Para falar HTTP a partir do "
                    + "sketch, use o MQTT ou o servidor web.", linha);
        }

        throw new ErroDeExecucao($"'{modulo}.{nome}()' ainda nao e suportado", linha);
    }

    // =====================================================================
    //  machine
    // =====================================================================
    Valor DoMachine(string nome, List<Valor> args, Dictionary<string, Valor> nomeados, int linha)
    {
        switch (nome)
        {
            case "Pin": return NovoPino(args, nomeados, linha);
            case "ADC": return NovoAdc(args, linha);
            case "PWM": return NovoPwm(args, nomeados, linha);
            case "I2C" or "SoftI2C": return NovoI2c(nome, args, nomeados, linha);
            case "UART": return NovaUartPy(args, nomeados, linha);

            case "reset":
                throw new ExecucaoInterrompida();

            case "freq":
                return Valor.DeInt(240_000_000);

            case "unique_id":
                return Valor.DeTexto(Maq.Rede?.MacDe(Maq) ?? "24:0A:C4:00:00:01");

            case "idle":
                Maq.Avancar(1);
                return Valor.Vazio;
        }

        throw new ErroDeExecucao($"machine.{nome}() ainda nao e suportado", linha);
    }

    /// <summary>
    /// "Pin(2, Pin.OUT)" / "Pin(14, Pin.IN, Pin.PULL_UP)".
    ///
    /// Os modos do MicroPython nao sao os do Arduino: la OUT e 3 e IN e
    /// 1. A traducao acontece aqui, e nao na Maquina, para a Maquina
    /// continuar falando so a lingua dela.
    /// </summary>
    Valor NovoPino(List<Valor> args, Dictionary<string, Valor> nomeados, int linha)
    {
        if (args.Count == 0)
            throw new ErroDeExecucao("Pin() precisa do numero do pino", linha);

        int pino = NumeroDePino(args[0], linha);
        var objeto = new Objeto("Pin") { Pino = pino };

        var modo = args.Count > 1 ? args[1]
                 : nomeados.TryGetValue("mode", out var m) ? m : Valor.Vazio;
        var puxador = args.Count > 2 ? args[2]
                    : nomeados.TryGetValue("pull", out var p) ? p : Valor.Vazio;

        if (args.Count > 1 || nomeados.ContainsKey("mode"))
        {
            int comoArduino = (int)modo.Num switch
            {
                3 or 7 => PinoSaida,
                _ => puxador.Num == 2 ? PinoEntradaComPullup : PinoEntrada
            };
            Maq.PinMode(pino, comoArduino);
        }

        if (nomeados.TryGetValue("value", out var inicial))
            Maq.DigitalWrite(pino, inicial.Verdadeiro ? 1 : 0);

        return Valor.DeReferencia(objeto);
    }

    /// <summary>
    /// O numero do pino, que pode vir como numero ou como outro Pin.
    ///
    /// "NeoPixel(Pin(12), 16)" passa o objeto; "ADC(34)" passa o numero.
    /// Os dois aparecem em sketch da turma.
    /// </summary>
    int NumeroDePino(Valor v, int linha)
    {
        if (v.Referencia is Objeto o && o.Pino >= 0) return o.Pino;
        if (v.Referencia is not null)
            throw new ErroDeExecucao("esperava o numero de um pino aqui", linha);

        // Pin("LED") e o LED da placa -- na Pico W e o unico jeito de
        // chegar nele, porque ele fica no radio e nao num GPIO. "GP15"
        // tambem vale, como esta escrito na serigrafia.
        if (v.EhTexto)
        {
            string nome = v.Texto.Trim();
            if (nome.Equals("LED", StringComparison.OrdinalIgnoreCase)) return Maq.Perfil.LedInterno;
            int daPlaca = Maq.Perfil.Numero(nome);
            if (daPlaca >= 0) return daPlaca;
            throw new ErroDeExecucao($"o pino \"{nome}\" nao existe nesta placa", linha);
        }
        return (int)v.Num;
    }

    /// <summary>
    /// "ADC(34)", "ADC(Pin(26))" e, na Pico, "ADC(0)" a "ADC(4)".
    ///
    /// No RP2040 o numero de 0 a 4 e o CANAL, e nao o pino: ADC(0) e o
    /// GP26, e ADC(4) e o sensor de temperatura de dentro do chip. Sem
    /// isto o ADC(0) lia o GP0 -- um pino que nem tem conversor --, e o
    /// exemplo de temperatura da Raspberry lia um botao.
    /// </summary>
    Valor NovoAdc(List<Valor> args, int linha)
    {
        if (args.Count == 0) throw new ErroDeExecucao("ADC() precisa do pino ou do canal", linha);

        var objeto = new Objeto("ADC");
        bool numeroCru = args[0].Referencia is null && !args[0].EhTexto;
        int n = NumeroDePino(args[0], linha);

        if (EhRp2040 && numeroCru && n is >= 0 and <= 4)
        {
            if (n == 4) objeto.Campos["_temperatura"] = Valor.DeInt(1);
            objeto.Pino = n == 4 ? -1 : 26 + n;
        }
        else objeto.Pino = n;

        return Valor.DeReferencia(objeto);
    }

    /// <summary>A leitura crua do conversor, na resolucao da placa.</summary>
    int LeituraDoAdcPy(Objeto o)
        => o.Campos.ContainsKey("_temperatura") ? Maq.LeituraDoSensorDeTemperatura : Maq.AnalogRead(o.Pino);

    /// <summary>
    /// "PWM(Pin(15))", "PWM(Pin(15), freq=1000, duty_u16=32768)" e o
    /// "PWM(pino, freq, duty)" da ESP32.
    /// </summary>
    Valor NovoPwm(List<Valor> args, Dictionary<string, Valor> nomeados, int linha)
    {
        var objeto = new Objeto("PWM") { Pino = NumeroDePino(args[0], linha) };
        Maq.PinMode(objeto.Pino, PinoSaida);

        if (nomeados.TryGetValue("freq", out var f)) objeto.Campos["freq"] = f;
        else if (args.Count > 1) objeto.Campos["freq"] = args[1];

        if (nomeados.TryGetValue("duty_u16", out var du)) AplicarDutyU16(objeto, du.Num);
        else if (nomeados.TryGetValue("duty_ns", out var dn)) AplicarDutyNs(objeto, dn.Num);
        else if (nomeados.TryGetValue("duty", out var d)) AplicarDuty(objeto, d.Num);
        else if (args.Count > 2) AplicarDuty(objeto, args[2].Num);

        return Valor.DeReferencia(objeto);
    }

    /// <summary>A frequencia pedida ao PWM, ou 0 para a de fabrica da placa.</summary>
    static double FrequenciaDoPwmPy(Objeto o)
        => o.Campos.TryGetValue("freq", out var f) ? f.Num : 0;

    /// <summary>O ciclo, de 0 a 1, vai para o pino com a frequencia do objeto.</summary>
    void AplicarCiclo(Objeto o, double ciclo)
    {
        ciclo = Math.Clamp(ciclo, 0, 1);
        o.Campos["_ciclo"] = Valor.DeReal(ciclo);
        Maq.AnalogWrite(o.Pino, (int)Math.Round(ciclo * 255), FrequenciaDoPwmPy(o));
    }

    /// <summary>O duty do MicroPython da ESP32 vai de 0 a 1023; o analogWrite, de 0 a 255.</summary>
    void AplicarDuty(Objeto o, double duty)
    {
        o.Campos["duty"] = Valor.DeInt((long)duty);
        o.Campos["_ciclo"] = Valor.DeReal(Math.Clamp(duty / 1023.0, 0, 1));
        Maq.AnalogWrite(o.Pino, Math.Clamp((int)(duty / 4), 0, 255), FrequenciaDoPwmPy(o));
    }

    void AplicarDutyU16(Objeto o, double duty)
    {
        o.Campos["duty_u16"] = Valor.DeInt((long)duty);
        AplicarCiclo(o, duty / 65535.0);
    }

    /// <summary>duty_ns: o tempo alto em nanossegundos, dentro do periodo da frequencia.</summary>
    void AplicarDutyNs(Objeto o, double ns)
    {
        double hz = FrequenciaDoPwmPy(o) > 0 ? FrequenciaDoPwmPy(o) : Maq.Perfil.FrequenciaDoPwm(o.Pino);
        o.Campos["duty_ns"] = Valor.DeInt((long)ns);
        AplicarCiclo(o, ns * hz / 1e9);
    }

    Valor NovaPeca(string classe, List<Valor> args, int linha)
    {
        var objeto = new Objeto(classe);
        if (args.Count > 0) objeto.Pino = NumeroDePino(args[0], linha);
        return Valor.DeReferencia(objeto);
    }

    Valor NovoNeoPixel(List<Valor> args, Dictionary<string, Valor> nomeados, int linha)
    {
        if (args.Count < 2)
            throw new ErroDeExecucao(
                "NeoPixel() precisa do pino e de quantos LEDs tem a fita", linha);

        int pino = NumeroDePino(args[0], linha);
        var objeto = new Objeto("NeoPixel")
        {
            Pino = pino,
            Fita = new Fita((int)args[1].Num)
        };

        Maq.Fitas[pino] = objeto.Fita;
        return Valor.DeReferencia(objeto);
    }

    Valor NovoMqtt(List<Valor> args, Dictionary<string, Valor> nomeados, int linha)
    {
        var cliente = new ClienteMqtt(Maq)
        {
            Host = args.Count > 1 ? args[1].Mostrar() : "",
            Porta = nomeados.TryGetValue("port", out var porta) ? (int)porta.Num : 1883,
        };

        var objeto = new Objeto("MQTTClient") { Rede = cliente };
        objeto.Campos["id"] = args.Count > 0 ? args[0] : Valor.DeTexto("micropython");

        if (nomeados.TryGetValue("user", out var u)) objeto.Campos["user"] = u;
        if (nomeados.TryGetValue("password", out var s)) objeto.Campos["senha"] = s;

        // MQTTClient(..., port=8883, ssl=True): o broker com TLS.
        if (nomeados.TryGetValue("ssl", out var tls) && tls.Verdadeiro)
        {
            cliente.Tls = true;
            cliente.SemConferirCertificado = true;
            if (!nomeados.ContainsKey("port")) cliente.Porta = 8883;
        }

        // Os posicionais do umqtt: MQTTClient(id, servidor, porta, usuario, senha).
        if (args.Count > 2 && !nomeados.ContainsKey("port") && args[2].Num > 0) cliente.Porta = (int)args[2].Num;
        if (args.Count > 3 && !nomeados.ContainsKey("user")) objeto.Campos["user"] = args[3];
        if (args.Count > 4 && !nomeados.ContainsKey("password")) objeto.Campos["senha"] = args[4];

        return Valor.DeReferencia(objeto);
    }

    // =====================================================================
    //  time
    // =====================================================================
    Valor DoTempo(string nome, List<Valor> args, int linha)
    {
        double A(int i) => i < args.Count ? args[i].Num : 0;

        switch (nome)
        {
            // Dormir zera o contador de passos: o limite existe para pegar
            // laco preso SEM espera, e o "while True" com sleep e
            // justamente o jeito certo de escrever o sketch em Python.
            case "sleep": Dormir((long)(A(0) * 1000)); return Valor.Vazio;
            case "sleep_ms": Dormir((long)A(0)); return Valor.Vazio;
            case "sleep_us": Dormir((long)(A(0) / 1000)); return Valor.Vazio;

            case "ticks_ms": return Valor.DeInt(Maq.Ms);
            case "ticks_us": return Valor.DeInt(Maq.Ms * 1000);
            case "ticks_diff": return Valor.DeInt(A(0) - A(1));
            case "ticks_add": return Valor.DeInt(A(0) + A(1));
            case "time": return Valor.DeInt(Maq.Ms / 1000);

            case "localtime":
            {
                var agora = DateTime.Now;
                return Valor.DeReferencia(ListaPy.De(
                    Valor.DeInt(agora.Year), Valor.DeInt(agora.Month), Valor.DeInt(agora.Day),
                    Valor.DeInt(agora.Hour), Valor.DeInt(agora.Minute), Valor.DeInt(agora.Second),
                    Valor.DeInt((int)agora.DayOfWeek), Valor.DeInt(agora.DayOfYear)));
            }
        }

        throw new ErroDeExecucao($"time.{nome}() ainda nao e suportado", linha);
    }

    void Dormir(long ms)
    {
        Maq.Avancar(ms);
        AtenderInterrupcoes();
        passos = 0;
    }

    // =====================================================================
    //  network
    // =====================================================================
    Valor DoRede(string nome, List<Valor> args, int linha)
    {
        if (nome != "WLAN")
            throw new ErroDeExecucao($"network.{nome}() ainda nao e suportado", linha);

        var objeto = new Objeto("WLAN");
        objeto.Campos["modo"] = args.Count > 0 ? args[0] : Valor.DeInt(0);
        return Valor.DeReferencia(objeto);
    }

    // =====================================================================
    //  json, math, random
    // =====================================================================
    Valor DoJson(string nome, List<Valor> args, int linha)
    {
        if (args.Count == 0) throw new ErroDeExecucao($"json.{nome}() precisa de um argumento", linha);

        if (nome == "dumps") return Valor.DeTexto(ParaJson(args[0]));
        if (nome == "loads") return DeJson(args[0].Mostrar(), linha);

        throw new ErroDeExecucao($"json.{nome}() ainda nao e suportado", linha);
    }

    static string ParaJson(Valor v) => v.Referencia switch
    {
        NadaPy => "null",
        DicionarioPy d => "{" + string.Join(", ", d.Pares().Select(
            par => Citar(par.Key) + ": " + ParaJson(par.Value))) + "}",
        ListaPy l => "[" + string.Join(", ", l.Itens.Select(ParaJson)) + "]",
        Objeto o => Citar("<" + o.Classe + ">"),
        _ => v.EhTexto ? Citar(v.Texto) : TextoPy.De(v)
    };

    static string Citar(string s)
        => ((char)34) + s.Replace(((char)92).ToString(), new string((char)92, 2))
                         .Replace(((char)34).ToString(), (char)92 + ((char)34).ToString())
           + ((char)34);

    /// <summary>
    /// Le JSON simples: objeto, lista, texto e numero.
    ///
    /// Nao e um analisador completo -- o que chega aqui e a carga de um
    /// MQTT ou uma resposta curta, e o aluno precisa de um dicionario
    /// para consultar, nao de conformidade com a norma.
    /// </summary>
    Valor DeJson(string texto, int linha)
    {
        int i = 0;
        var v = LerJson(texto, ref i, linha);
        return v;
    }

    Valor LerJson(string s, ref int i, int linha)
    {
        while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        if (i >= s.Length) throw new ErroDeExecucao("JSON incompleto", linha);

        char c = s[i];

        if (c == '{')
        {
            i++;
            var dic = new DicionarioPy();
            while (true)
            {
                while (i < s.Length && (char.IsWhiteSpace(s[i]) || s[i] == ',')) i++;
                if (i < s.Length && s[i] == '}') { i++; break; }

                var chave = LerJson(s, ref i, linha);
                while (i < s.Length && (char.IsWhiteSpace(s[i]) || s[i] == ':')) i++;
                dic.Por(chave.Mostrar(), LerJson(s, ref i, linha));
            }
            return Valor.DeReferencia(dic);
        }

        if (c == '[')
        {
            i++;
            var lista = new ListaPy();
            while (true)
            {
                while (i < s.Length && (char.IsWhiteSpace(s[i]) || s[i] == ',')) i++;
                if (i < s.Length && s[i] == ']') { i++; break; }
                lista.Itens.Add(LerJson(s, ref i, linha));
            }
            return Valor.DeReferencia(lista);
        }

        if (c == (char)34)
        {
            i++;
            var sb = new System.Text.StringBuilder();
            while (i < s.Length && s[i] != (char)34)
            {
                if (s[i] == (char)92 && i + 1 < s.Length) i++;
                sb.Append(s[i]);
                i++;
            }
            i++;
            return Valor.DeTexto(sb.ToString());
        }

        int ini = i;
        while (i < s.Length && !char.IsWhiteSpace(s[i]) && s[i] != ',' && s[i] != '}' && s[i] != ']') i++;
        string bruto = s[ini..i];

        if (bruto == "null") return Valor.Nenhum;
        if (bruto == "true") return Valor.DeBool(true);
        if (bruto == "false") return Valor.DeBool(false);
        if (double.TryParse(bruto, System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out double n))
            return bruto.Contains('.') ? Valor.DeReal(n) : Valor.DeInt(n);

        throw new ErroDeExecucao($"'{bruto}' nao e um valor de JSON", linha);
    }

    Valor DoMatematica(string nome, List<Valor> args, int linha)
    {
        double A(int i) => i < args.Count ? args[i].Num : 0;

        return nome switch
        {
            "sin" => Valor.DeReal(Math.Sin(A(0))),
            "cos" => Valor.DeReal(Math.Cos(A(0))),
            "tan" => Valor.DeReal(Math.Tan(A(0))),
            "asin" => Valor.DeReal(Math.Asin(A(0))),
            "acos" => Valor.DeReal(Math.Acos(A(0))),
            "atan" => Valor.DeReal(Math.Atan(A(0))),
            "atan2" => Valor.DeReal(Math.Atan2(A(0), A(1))),
            "sqrt" => Valor.DeReal(Math.Sqrt(A(0))),
            "pow" => Valor.DeReal(Math.Pow(A(0), A(1))),
            "exp" => Valor.DeReal(Math.Exp(A(0))),
            "log" => Valor.DeReal(args.Count > 1 ? Math.Log(A(0), A(1)) : Math.Log(A(0))),
            "floor" => Valor.DeInt(Math.Floor(A(0))),
            "ceil" => Valor.DeInt(Math.Ceiling(A(0))),
            "trunc" => Valor.DeInt(Math.Truncate(A(0))),
            "fabs" => Valor.DeReal(Math.Abs(A(0))),
            "fmod" => Valor.DeReal(Math.IEEERemainder(A(0), A(1))),
            "radians" => Valor.DeReal(A(0) * Math.PI / 180),
            "degrees" => Valor.DeReal(A(0) * 180 / Math.PI),
            _ => throw new ErroDeExecucao($"math.{nome}() ainda nao e suportado", linha)
        };
    }

    /// <summary>
    /// O sorteio do sketch.
    ///
    /// Semente fixa de proposito: dois alunos rodando o mesmo sketch tem
    /// que ver a mesma coisa, senao "funciona no meu" vira discussao.
    /// Quem quiser variar chama random.seed() com outro numero.
    /// </summary>
    Random sorteio;

    Valor DoSorteio(string nome, List<Valor> args, int linha)
    {
        sorteio ??= new Random(20260101);
        double A(int i) => i < args.Count ? args[i].Num : 0;

        switch (nome)
        {
            case "seed":
                sorteio = new Random(args.Count > 0 ? (int)A(0) : 20260101);
                return Valor.Vazio;
            case "random": return Valor.DeReal(sorteio.NextDouble());
            case "uniform": return Valor.DeReal(A(0) + sorteio.NextDouble() * (A(1) - A(0)));
            case "randint": return Valor.DeInt(sorteio.Next((int)A(0), (int)A(1) + 1));
            case "randrange": return Valor.DeInt(args.Count > 1
                ? sorteio.Next((int)A(0), (int)A(1))
                : sorteio.Next((int)A(0)));
            case "getrandbits": return Valor.DeInt(sorteio.Next(1 << Math.Clamp((int)A(0), 1, 30)));
            case "choice":
                if (args.Count > 0 && args[0].Referencia is ListaPy l && l.Itens.Count > 0)
                    return l.Itens[sorteio.Next(l.Itens.Count)];
                throw new ErroDeExecucao("random.choice() precisa de uma lista com itens", linha);
        }

        throw new ErroDeExecucao($"random.{nome}() ainda nao e suportado", linha);
    }
}
