namespace Simulador;

/// <summary>
/// Os metodos dos objetos do MicroPython e as funcoes embutidas.
///
/// Fechando o trio: <c>InterpretadorPython</c> tem a linguagem,
/// <c>InterpretadorMicroPython</c> tem os construtores dos modulos, e
/// aqui esta o que se faz com o que eles devolvem -- "pino.value(1)",
/// "oled.text(...)", "print(...)", "len(...)".
/// </summary>
public partial class Interpretador
{
    // Os modos como a Maquina os entende. O MicroPython usa outros
    // numeros; a traducao fica em NovoPino.
    const int PinoEntrada = 0;
    const int PinoSaida = 1;
    const int PinoEntradaComPullup = 2;

    // =====================================================================
    //  metodos das pecas
    // =====================================================================
    Valor ChamarPecaPy(Objeto o, string metodo, List<Valor> args,
                       Dictionary<string, Valor> nomeados, Chamada c, Ambiente amb)
    {
        double A(int i) => i < args.Count ? args[i].Num : 0;
        int linha = c.Linha;

        switch (o.Classe)
        {
            // ---------- Timer, RTC e SPI (InterpretadorMicroPythonTempo.cs) ----------
            case "Timer": return ChamarTimerPy(o, metodo, nomeados, linha);
            case "RTC": return ChamarRtcPy(args, metodo, linha);
            case "SPI" or "SoftSPI": return ChamarSpiPy(o, metodo, args, nomeados, linha);

            // ---------- Pin ----------
            case "Pin":
                switch (metodo)
                {
                    case "value":
                        // Sem argumento le; com argumento escreve. E o
                        // mesmo metodo para as duas coisas, ao contrario
                        // do digitalRead/digitalWrite do Arduino.
                        if (args.Count == 0) return Valor.DeInt(Maq.DigitalRead(o.Pino));
                        Maq.DigitalWrite(o.Pino, args[0].Verdadeiro ? 1 : 0);
                        return Valor.Vazio;

                    case "on" or "high": Maq.DigitalWrite(o.Pino, 1); return Valor.Vazio;
                    case "off" or "low": Maq.DigitalWrite(o.Pino, 0); return Valor.Vazio;

                    case "toggle":
                        Maq.DigitalWrite(o.Pino, Maq.DigitalRead(o.Pino) != 0 ? 0 : 1);
                        return Valor.Vazio;

                    case "init":
                        Maq.PinMode(o.Pino, (int)A(0) is 3 or 7 ? PinoSaida : PinoEntrada);
                        return Valor.Vazio;

                    // "botao.irq(trigger=Pin.IRQ_FALLING, handler=apertou)": o callback
                    // roda na borda, com o Pin de argumento (InterpretadorMicroPythonTempo.cs).
                    case "irq":
                        return IrqDoPino(o, args, nomeados, linha);
                }
                break;

            // ---------- ADC ----------
            case "ADC":
                switch (metodo)
                {
                    // A Pico nao tem read(): o ADC do MicroPython no RP2040
                    // so conhece o read_u16. Na placa o programa para com
                    // AttributeError, e aqui tambem.
                    case "read":
                        if (EhRp2040)
                            throw new ErroDeExecucao(Idioma.T("ADC.read() nao existe na Pico: o MicroPython do RP2040 so tem read_u16(), que vai de 0 a 65535."), linha);
                        return Valor.DeInt(LeituraDoAdcPy(o));

                    // read_u16 devolve 16 bits, qualquer que seja o conversor:
                    // a leitura crua esticada ate 65535.
                    case "read_u16":
                        return Valor.DeInt((long)Math.Round(
                            LeituraDoAdcPy(o) * 65535.0 / Math.Max(1, Maq.Perfil.MaxAdc)));

                    case "read_uv":
                        return Valor.DeInt((long)Math.Round(
                            LeituraDoAdcPy(o) * 3_300_000.0 / Math.Max(1, Maq.Perfil.MaxAdc)));

                    case "atten" or "width": return Valor.Vazio;
                }
                break;

            // ---------- PWM ----------
            case "PWM":
                switch (metodo)
                {
                    case "duty":
                        if (args.Count == 0) return o.Campos.GetValueOrDefault("duty", Valor.DeInt(0));
                        AplicarDuty(o, A(0));
                        return Valor.Vazio;

                    case "duty_u16":
                        if (args.Count == 0)
                            return Valor.DeInt((long)Math.Round(
                                o.Campos.GetValueOrDefault("_ciclo", Valor.DeReal(0)).Num * 65535));
                        AplicarDutyU16(o, A(0));
                        return Valor.Vazio;

                    case "duty_ns":
                        if (args.Count == 0) return o.Campos.GetValueOrDefault("duty_ns", Valor.DeInt(0));
                        AplicarDutyNs(o, A(0));
                        return Valor.Vazio;

                    // Mudar a frequencia mantem o ciclo, como no chip -- e o
                    // analisador passa a mostrar o periodo novo.
                    case "freq":
                        if (args.Count == 0)
                            return o.Campos.GetValueOrDefault("freq",
                                Valor.DeInt((long)Maq.Perfil.FrequenciaDoPwm(o.Pino)));
                        o.Campos["freq"] = args[0];
                        if (o.Campos.TryGetValue("_ciclo", out var ciclo)) AplicarCiclo(o, ciclo.Num);
                        return Valor.Vazio;

                    case "deinit":
                        Maq.AnalogWrite(o.Pino, 0);
                        return Valor.Vazio;
                }
                break;

            // ---------- I2C ----------
            // O scan() devolve os enderecos que responderam no fio DESTE I2C. E
            // o primeiro comando que o aluno roda quando o display nao acende.
            case "I2C": return ChamarI2cPy(o, metodo, args, nomeados, linha);

            // ---------- UART ----------
            case "UART": return ChamarUartPy(o, metodo, args, nomeados, linha);

            // ---------- DHT ----------
            case "DHT11" or "DHT22":
                switch (metodo)
                {
                    // measure() le o sensor de uma vez; temperature() e
                    // humidity() so devolvem o que ela guardou.
                    case "measure":
                        o.Campos["temp"] = Valor.DeReal(Maq.Leitura($"{o.Pino}:temperatura", 25));
                        o.Campos["umidade"] = Valor.DeReal(Maq.Leitura($"{o.Pino}:umidade", 50));
                        Maq.Avancar(2);
                        return Valor.Vazio;

                    case "temperature":
                        return o.Campos.GetValueOrDefault(
                            "temp", Valor.DeReal(Maq.Leitura($"{o.Pino}:temperatura", 25)));

                    case "humidity":
                        return o.Campos.GetValueOrDefault(
                            "umidade", Valor.DeReal(Maq.Leitura($"{o.Pino}:umidade", 50)));
                }
                break;

            // ---------- NeoPixel ----------
            case "NeoPixel":
                switch (metodo)
                {
                    // O write() e que manda a fita inteira para os LEDs.
                    // Sem ele nada acende -- e e erro comum esquece-lo.
                    case "write": o.Fita.Mostrar(); return Valor.Vazio;

                    case "fill":
                    {
                        int cor = CorDePython(args.Count > 0 ? args[0] : Valor.Vazio, linha);
                        for (int i = 0; i < o.Fita.Quantidade; i++) o.Fita.Definir(i, cor);
                        return Valor.Vazio;
                    }

                    case "__len__": return Valor.DeInt(o.Fita.Quantidade);
                }
                break;

            // ---------- OLED ----------
            // Em pixels, com a fonte 8x8 do framebuf -- antes era uma tela de texto.
            case "SSD1306": return ChamarOledPy(o, metodo, args, linha);

            // ---------- WLAN ----------
            case "WLAN": return ChamarWlan(o, metodo, args, c, amb);

            // ---------- MQTT ----------
            case "MQTTClient": return ChamarMqttPy(o, metodo, args, c, amb);
        }

        throw new ErroDeExecucao(
            Idioma.T("{0}.{1}() ainda nao e suportado pelo simulador", o.Classe, metodo), linha);
    }

    /// <summary>
    /// A cor como o NeoPixel do MicroPython a recebe: uma tupla (r, g, b).
    /// </summary>
    static int CorDePython(Valor v, int linha)
    {
        if (v.Referencia is not ListaPy cor || cor.Itens.Count < 3)
            throw new ErroDeExecucao(
                Idioma.T("a cor de um LED da fita e uma tupla de tres numeros, como (148, 0, 211)"), linha);

        return Fita.Montar((int)cor.Itens[0].Num, (int)cor.Itens[1].Num, (int)cor.Itens[2].Num);
    }

    /// <summary>"rgb[3] = (148, 0, 211)": escrita direta num LED da fita.</summary>
    void EscreverNoObjeto(Objeto o, int i, Valor v, int linha)
    {
        if (o.Fita is null)
            throw new ErroDeExecucao(
                Idioma.T("nao da para escrever por indice num {0}", o.Classe), linha);

        if (i < 0) i += o.Fita.Quantidade;
        if (i < 0 || i >= o.Fita.Quantidade)
            throw new ErroDeExecucao(
                Idioma.T("a fita tem {0} LEDs: o indice {1} esta fora", o.Fita.Quantidade, i), linha);

        o.Fita.Definir(i, CorDePython(v, linha));
    }

    // =====================================================================
    //  WLAN
    // =====================================================================
    Valor ChamarWlan(Objeto o, string metodo, List<Valor> args, Chamada c, Ambiente amb)
    {
        ExigirWifi(c.Linha, $"WLAN.{metodo}()");

        var rede = Maq.Rede;
        var estado = rede.Wifi(Maq);
        bool ehPontoDeAcesso = o.Campos.GetValueOrDefault("modo", Valor.DeInt(0)).Num == 1;

        switch (metodo)
        {
            case "active":
                if (args.Count == 0) return Valor.DeBool(estado.Ligado || estado.ModoAp);
                o.Campos["ativa"] = args[0];
                return Valor.DeBool(args[0].Verdadeiro);

            case "connect":
                rede.ConectarWifi(Maq, args.Count > 0 ? args[0].Mostrar() : "",
                                  args.Count > 1 ? args[1].Mostrar() : null);
                return Valor.Vazio;

            case "isconnected":
                return Valor.DeBool(ehPontoDeAcesso ? estado.ModoAp : rede.WifiConectado(Maq));

            case "disconnect":
                rede.DesligarWifi(Maq);
                return Valor.Vazio;

            case "ifconfig":
                return Valor.DeReferencia(new ListaPy([
                    Valor.DeTexto(rede.WifiConectado(Maq) || estado.ModoAp ? estado.Ip : "0.0.0.0"),
                    Valor.DeTexto("255.255.255.0"),
                    Valor.DeTexto(Rede.Gateway),
                    Valor.DeTexto(Rede.Gateway)], true));

            case "config":
                // config('mac'), config(essid=...) -- o que o sketch pede
                // aqui e quase sempre o proprio endereco.
                return Valor.DeTexto(estado.Mac);

            case "status":
                return Valor.DeInt(rede.WifiConectado(Maq) ? 1010 : 1001);

            case "scan":
            {
                var achadas = new ListaPy();
                foreach (var (ssid, rssi) in rede.RedesVisiveis())
                    achadas.Itens.Add(Valor.DeReferencia(new ListaPy([
                        Valor.DeTexto(ssid), Valor.DeTexto(estado.Mac),
                        Valor.DeInt(1), Valor.DeInt(rssi),
                        Valor.DeInt(3), Valor.DeInt(0)], true)));
                Maq.Avancar(300);
                return Valor.DeReferencia(achadas);
            }
        }

        throw new ErroDeExecucao(Idioma.T("WLAN.{0}() ainda nao e suportado", metodo), c.Linha);
    }

    // =====================================================================
    //  MQTT
    // =====================================================================
    Valor ChamarMqttPy(Objeto o, string metodo, List<Valor> args, Chamada c, Ambiente amb)
    {
        var cliente = (ClienteMqtt)o.Rede;
        var rede = Maq.Rede;
        string S(int i) => i < args.Count ? args[i].Mostrar() : "";

        switch (metodo)
        {
            case "set_callback":
                o.Campos["callback"] = c.Args.Count > 0 && c.Args[0] is Variavel f
                    ? Valor.DeTexto(f.Nome)
                    : throw new ErroDeExecucao(
                        Idioma.T("set_callback() espera o nome de uma funcao"), c.Linha);
                return Valor.Vazio;

            case "connect":
            {
                ExigirWifi(c.Linha, "client.connect()");
                string usuario = o.Campos.GetValueOrDefault("user", Valor.DeTexto("")).Mostrar();
                string senha = o.Campos.GetValueOrDefault("senha", Valor.DeTexto("")).Mostrar();
                string id = o.Campos.GetValueOrDefault("id", Valor.DeTexto("micropython")).Mostrar();

                if (!rede.ConectarMqtt(cliente, id,
                                       usuario.Length > 0 && usuario != "None" ? usuario : null,
                                       senha.Length > 0 && senha != "None" ? senha : null))
                    throw new ErroDeExecucao(
                        cliente.Estado > 0
                            // O MQTTException(5) do umqtt: o broker respondeu que nao.
                            ? Idioma.T("MQTTException({0}): o broker '{1}' recusou a conexao {2}", cliente.Estado, cliente.Host, (cliente.Estado is 4 or 5 ? Idioma.T("(usuario ou senha).") : Idioma.T("(veja o painel Rede).")))
                            : Idioma.T("nao consegui conectar no broker MQTT '{0}'. Confira o endereco e se o WiFi ja conectou.", cliente.Host), c.Linha);
                return Valor.Vazio;
            }

            case "publish":
                if (!rede.PublicarMqtt(cliente, S(0), TextoArduino.ParaBytes(S(1)),
                                       args.Count > 2 && args[2].Verdadeiro))
                    throw new ErroDeExecucao(
                        Idioma.T("publish() falhou: o cliente MQTT nao esta conectado"), c.Linha);
                return Valor.Vazio;

            case "subscribe":
                rede.AssinarMqtt(cliente, S(0));
                return Valor.Vazio;

            case "check_msg" or "wait_msg":
            {
                rede.ManterMqtt(cliente);
                var mensagem = cliente.Proxima();

                // O wait_msg() espera a mensagem chegar -- e o laco que so
                // chama ele fica parado ali, como na placa. O check_msg()
                // olha e volta na hora.
                while (mensagem is null && metodo == "wait_msg" && cliente.Conectado)
                {
                    if (Maq.Parar) throw new ExecucaoInterrompida();
                    Maq.Avancar(10);
                    AtenderInterrupcoes();
                    rede.ManterMqtt(cliente);
                    mensagem = cliente.Proxima();
                }
                if (mensagem is null) return Valor.Nenhum;

                if (o.Campos.TryGetValue("callback", out var nome)
                    && funcoes.TryGetValue(nome.Mostrar(), out var rotina))
                {
                    var local = new Ambiente(Global);
                    if (rotina.Params.Count > 0)
                        local.Declarar(rotina.Params[0].Nome, Valor.DeTexto(mensagem.Topico));
                    if (rotina.Params.Count > 1)
                        local.Declarar(rotina.Params[1].Nome,
                                       Valor.DeTexto(TextoArduino.DeBytes(mensagem.Carga)));
                    ExecCorpoDeFuncao(rotina.Corpo, local, out _);
                }
                return Valor.Vazio;
            }

            case "disconnect":
                rede.DesconectarMqtt(cliente);
                return Valor.Vazio;

            case "ping":
                return Valor.Vazio;

            // set_last_will(topico, mensagem, retain=False, qos=0), antes do connect().
            case "set_last_will":
                cliente.Testamento = new TestamentoMqtt(S(0), TextoArduino.ParaBytes(S(1)),
                                                        args.Count > 2 && args[2].Verdadeiro,
                                                        args.Count > 3 ? (int)args[3].Num : 0);
                return Valor.Vazio;
        }

        throw new ErroDeExecucao(Idioma.T("client.{0}() ainda nao e suportado", metodo), c.Linha);
    }

    // =====================================================================
    //  funcoes embutidas
    // =====================================================================
    bool ChamarEmbutida(string nome, Chamada c, Ambiente amb, out Valor r)
    {
        r = Valor.Vazio;

        // Estas duas nao avaliam os argumentos do jeito normal.
        if (nome == "__formatar") { r = Interpolar(c, amb); return true; }
        if (nome == "print") { Imprimir(c, amb); return true; }

        if (!Embutidas.Contains(nome)) return false;

        var (args, nomeados) = Argumentos(c, amb);
        double A(int i) => i < args.Count ? args[i].Num : 0;
        var primeiro = args.Count > 0 ? args[0] : Valor.Vazio;
        int linha = c.Linha;

        switch (nome)
        {
            case "range": r = Intervalo(args, linha); return true;

            case "len": r = Valor.DeInt(Tamanho(primeiro, linha)); return true;

            case "str": r = Valor.DeTexto(TextoPy.De(primeiro)); return true;

            case "int":
                r = primeiro.EhTexto
                    ? Valor.DeInt(long.TryParse(primeiro.Texto.Trim(),
                        System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out long n)
                        ? n
                        : throw new ErroDeExecucao(
                            Idioma.T("int('{0}') nao da: isso nao e um numero", primeiro.Texto), linha))
                    : Valor.DeInt(Math.Truncate(primeiro.Num));
                return true;

            case "float":
                r = Valor.DeReal(primeiro.EhTexto
                    ? double.Parse(primeiro.Texto.Trim(),
                        System.Globalization.CultureInfo.InvariantCulture)
                    : primeiro.Num);
                return true;

            case "bool": r = Valor.DeBool(primeiro.Verdadeiro); return true;
            case "abs": r = primeiro.Inteiro
                ? Valor.DeInt(Math.Abs(primeiro.Num)) : Valor.DeReal(Math.Abs(primeiro.Num));
                return true;

            case "round":
                r = args.Count > 1
                    ? Valor.DeReal(Math.Round(A(0), (int)A(1)))
                    : Valor.DeInt(Math.Round(A(0), MidpointRounding.ToEven));
                return true;

            case "min" or "max":
            {
                var itens = args.Count == 1 ? [.. Percorrer(primeiro, linha)] : args;
                if (itens.Count == 0)
                    throw new ErroDeExecucao(Idioma.T("{0}() precisa de pelo menos um valor", nome), linha);
                r = nome == "min"
                    ? itens.Aggregate((a, b) => a.Num <= b.Num ? a : b)
                    : itens.Aggregate((a, b) => a.Num >= b.Num ? a : b);
                return true;
            }

            case "sum":
            {
                double total = Percorrer(primeiro, linha).Sum(v => v.Num);
                bool inteiro = Percorrer(primeiro, linha).All(v => v.Inteiro);
                r = inteiro ? Valor.DeInt(total) : Valor.DeReal(total);
                return true;
            }

            case "ord":
                r = Valor.DeInt(primeiro.EhTexto && primeiro.Texto.Length > 0
                    ? primeiro.Texto[0] : 0);
                return true;

            case "chr": r = Valor.DeTexto(((char)(int)A(0)).ToString()); return true;
            case "hex": r = Valor.DeTexto("0x" + ((long)A(0)).ToString("x")); return true;
            case "bin": r = Valor.DeTexto("0b" + Convert.ToString((long)A(0), 2)); return true;
            // Inteiro elevado a inteiro continua inteiro: "2 ** 10" e
            // 1024, e nao 1024.0. E o operador "**" que cai aqui.
            case "pow":
            {
                double resultado = Math.Pow(A(0), A(1));
                r = args.Count > 1 && args[0].Inteiro && args[1].Inteiro && A(1) >= 0
                    ? Valor.DeInt(resultado)
                    : Valor.DeReal(resultado);
                return true;
            }

            case "list" or "tuple":
                r = Valor.DeReferencia(args.Count == 0
                    ? new ListaPy([], nome == "tuple")
                    : new ListaPy([.. Percorrer(primeiro, linha)], nome == "tuple"));
                return true;

            case "dict":
                r = Valor.DeReferencia(new DicionarioPy());
                foreach (var (chave, valor) in nomeados)
                    ((DicionarioPy)r.Referencia).Por(chave, valor);
                return true;

            case "sorted":
            {
                var itens = Percorrer(primeiro, linha).ToList();
                itens.Sort((a, b) => a.EhTexto || b.EhTexto
                    ? string.CompareOrdinal(a.Mostrar(), b.Mostrar())
                    : a.Num.CompareTo(b.Num));
                if (nomeados.TryGetValue("reverse", out var aoContrario) && aoContrario.Verdadeiro)
                    itens.Reverse();
                r = Valor.DeReferencia(new ListaPy(itens));
                return true;
            }

            case "reversed":
            {
                var itens = Percorrer(primeiro, linha).ToList();
                itens.Reverse();
                r = Valor.DeReferencia(new ListaPy(itens));
                return true;
            }

            case "enumerate":
            {
                var pares = new ListaPy();
                int i = nomeados.TryGetValue("start", out var comeco) ? (int)comeco.Num
                      : args.Count > 1 ? (int)A(1) : 0;
                foreach (var item in Percorrer(primeiro, linha))
                    pares.Itens.Add(Valor.DeReferencia(
                        new ListaPy([Valor.DeInt(i++), item], true)));
                r = Valor.DeReferencia(pares);
                return true;
            }

            case "zip":
            {
                var listas = args.Select(a => Percorrer(a, linha).ToList()).ToList();
                var juntas = new ListaPy();
                int quantos = listas.Count == 0 ? 0 : listas.Min(l => l.Count);
                for (int i = 0; i < quantos; i++)
                    juntas.Itens.Add(Valor.DeReferencia(
                        new ListaPy([.. listas.Select(l => l[i])], true)));
                r = Valor.DeReferencia(juntas);
                return true;
            }

            case "type":
                r = Valor.DeTexto(NomeDoTipo(primeiro));
                return true;

            case "isinstance":
                r = Valor.DeBool(args.Count > 1
                    && NomeDoTipo(primeiro) == args[1].Mostrar());
                return true;

            // bytes(4) / bytearray(4): quatro bytes zerados; de uma lista ou de
            // um b'..', os bytes dela. O bytearray e mutavel (o readinto escreve
            // nele): vira uma lista de numeros; o bytes, o texto de bytes.
            case "bytes" or "bytearray":
            {
                byte[] conteudo = args.Count == 0 ? []
                    : !primeiro.EhTexto && primeiro.Referencia is null ? new byte[Math.Clamp((int)primeiro.Num, 0, 1 << 20)]
                    : BytesPy(primeiro);
                r = nome == "bytearray"
                    ? Valor.DeReferencia(ListaPy.De([.. conteudo.Select(x => Valor.DeInt(x))]))
                    : Valor.DeTexto(TextoArduino.DeBytes(conteudo));
                return true;
            }

            case "input":
                if (args.Count > 0) Escrever(args[0].Mostrar(), false);
                r = Valor.DeTexto(LerDoMonitor(linha));
                return true;

            case "__contem":
                r = Valor.DeBool(Contem(args[0], args[1], linha));
                return true;

            case "__apagar":
                return true;
        }

        return false;
    }

    /// <summary>
    /// O input() do Python: espera a pessoa digitar no monitor serial.
    ///
    /// Espera MESMO, como na placa de verdade -- o sketch da calculadora
    /// nao faz sentido de outro jeito. O laco solta na hora em que a
    /// simulacao para, entao o botao Parar continua respondendo.
    ///
    /// Com o relogio virtual (os testes e a linha de comando) nao ha
    /// monitor nem ninguem para digitar: ali a espera viraria travamento,
    /// e o simulador diz isso em vez de ficar parado para sempre.
    /// </summary>
    string LerDoMonitor(int linha)
    {
        if (!Maq.TempoReal)
        {
            // O TrimEnd do "\r" nao e detalhe: o monitor manda a linha
            // com CRLF, e um "+\r" que sobra faz toda comparacao de
            // texto do sketch falhar em silencio.
            string jaDigitado = Maq.EntradaSerial.LerAte('\n').TrimEnd('\r');
            if (jaDigitado.Length > 0) return jaDigitado;

            throw new ErroDeExecucao(
                Idioma.T("input() esta esperando alguem digitar no monitor serial, e aqui nao ha monitor. Rode o sketch pela janela do simulador e digite a resposta no painel Serial."), linha);
        }

        while (true)
        {
            if (Maq.Parar) throw new ExecucaoInterrompida();

            if (Maq.EntradaSerial.Disponivel > 0)
            {
                string lido = Maq.EntradaSerial.LerAte('\n');
                if (lido.Length > 0) return lido.TrimEnd('\r');
            }

            // Avancar (e nao Sleep) porque e ele que atende a pausa e o
            // pedido de parar.
            Maq.Avancar(20);
            passos = 0;
        }
    }

    static readonly HashSet<string> Embutidas =
    [
        "range", "len", "str", "int", "float", "bool", "abs", "round", "min", "max",
        "sum", "ord", "chr", "hex", "bin", "pow", "list", "tuple", "dict", "sorted",
        "reversed", "enumerate", "zip", "type", "isinstance", "bytes", "bytearray",
        "input", "__contem", "__apagar",
    ];

    static string NomeDoTipo(Valor v) => v.Referencia switch
    {
        NadaPy => "NoneType",
        ListaPy l => l.EhTupla ? "tuple" : "list",
        DicionarioPy => "dict",
        Objeto o => o.Classe,
        _ => v.EhTexto ? "str" : v.Inteiro ? "int" : "float"
    };

    int Tamanho(Valor v, int linha) => v.Referencia switch
    {
        ListaPy l => l.Itens.Count,
        DicionarioPy d => d.Ordem.Count,
        Objeto { Fita: not null } o => o.Fita.Quantidade,
        _ => v.EhTexto
            ? v.Texto.Length
            : throw new ErroDeExecucao(Idioma.T("len() so vale para lista, texto e dicionario"), linha)
    };

    bool Contem(Valor onde, Valor oque, int linha)
    {
        if (onde.Referencia is DicionarioPy d) return d.Itens.ContainsKey(oque.Mostrar());
        if (onde.EhTexto) return onde.Texto.Contains(oque.Mostrar(), StringComparison.Ordinal);
        return Percorrer(onde, linha).Any(v => v.Mostrar() == oque.Mostrar());
    }

    /// <summary>
    /// range(fim), range(inicio, fim) e range(inicio, fim, passo).
    ///
    /// Vira lista na hora, em vez de sequencia preguicosa: os ranges dos
    /// sketches tem dezenas de itens, e a lista pronta deixa "for i in
    /// range(16)" e "lista[2]" funcionarem pelo mesmo caminho.
    /// </summary>
    static Valor Intervalo(List<Valor> args, int linha)
    {
        if (args.Count == 0)
            throw new ErroDeExecucao(Idioma.T("range() precisa de pelo menos um numero"), linha);

        long inicio = args.Count > 1 ? (long)args[0].Num : 0;
        long fim = args.Count > 1 ? (long)args[1].Num : (long)args[0].Num;
        long passo = args.Count > 2 ? (long)args[2].Num : 1;

        if (passo == 0) throw new ErroDeExecucao(Idioma.T("o passo de range() nao pode ser zero"), linha);

        var itens = new List<Valor>();
        for (long i = inicio; passo > 0 ? i < fim : i > fim; i += passo)
        {
            if (itens.Count > 100_000)
                throw new ErroDeExecucao(
                    Idioma.T("este range() tem mais de 100 mil itens: confira os limites"), linha);
            itens.Add(Valor.DeInt(i));
        }

        return Valor.DeReferencia(new ListaPy(itens));
    }

    /// <summary>
    /// O print do Python, com "end" e "sep".
    ///
    /// O "end" importa mais do que parece: o sketch do Wokwi escreve
    /// "print('.', end='')" para desenhar a barrinha de progresso do
    /// WiFi, e sem ele cada ponto cairia numa linha.
    /// </summary>
    void Imprimir(Chamada c, Ambiente amb)
    {
        var (args, nomeados) = Argumentos(c, amb);

        string separador = nomeados.TryGetValue("sep", out var sep) ? sep.Mostrar() : " ";
        string fim = nomeados.TryGetValue("end", out var f) ? f.Mostrar() : "\n";

        string texto = string.Join(separador, args.Select(TextoPy.De));

        // A quebra de linha e a do monitor, e nao um caractere no texto:
        // e o que faz a linha aparecer inteira de uma vez.
        if (fim == "\n") Escrever(texto, true);
        else Escrever(texto + fim, false);
    }

    /// <summary>
    /// O f-string: "f'temperatura: {t:.1f}'".
    ///
    /// O molde chegou como texto cru do lexer; o que esta entre chaves e
    /// codigo de verdade, entao passa pelo mesmo parser do resto.
    /// </summary>
    Valor Interpolar(Chamada c, Ambiente amb)
    {
        string molde = c.Args.Count > 0 && c.Args[0] is Literal l ? l.Texto : "";
        var saida = new System.Text.StringBuilder();

        for (int i = 0; i < molde.Length; i++)
        {
            if (molde[i] == '{' && i + 1 < molde.Length && molde[i + 1] == '{')
            {
                saida.Append('{'); i++; continue;
            }
            if (molde[i] == '}' && i + 1 < molde.Length && molde[i + 1] == '}')
            {
                saida.Append('}'); i++; continue;
            }
            if (molde[i] != '{') { saida.Append(molde[i]); continue; }

            int fecha = molde.IndexOf('}', i);
            if (fecha < 0)
                throw new ErroDeExecucao(Idioma.T("faltou fechar uma chave no texto f'...'"), c.Linha);

            string dentro = molde[(i + 1)..fecha];
            i = fecha;

            string formato = null;
            int doisPontos = dentro.LastIndexOf(':');
            if (doisPontos > 0)
            {
                formato = dentro[(doisPontos + 1)..];
                dentro = dentro[..doisPontos];
            }

            var valor = AvaliarTexto(dentro, c.Linha, amb);
            saida.Append(formato is null ? TextoPy.De(valor) : ComFormato(valor, formato, c.Linha));
        }

        return Valor.DeTexto(saida.ToString());
    }

    /// <summary>Roda um pedaco de codigo que veio de dentro de um f-string.</summary>
    Valor AvaliarTexto(string codigo, int linha, Ambiente amb)
    {
        try
        {
            var toks = LexerPython.Tokenizar(codigo);
            toks.RemoveAll(t => t.Tipo is T.Nova or T.Entra or T.Sai);
            return Avaliar(new ParserPython(toks).AnalisarExpr(), amb);
        }
        catch (ErroDeSintaxe e)
        {
            throw new ErroDeExecucao(Idioma.T("dentro do f'...': {0}", e.Message), linha);
        }
    }

    /// <summary>O ".1f" e o "03d" que vem depois dos dois-pontos.</summary>
    static string ComFormato(Valor v, string formato, int linha)
    {
        if (formato.Length == 0) return TextoPy.De(v);

        char tipo = formato[^1];
        string largura = char.IsLetter(tipo) ? formato[..^1] : formato;
        if (!char.IsLetter(tipo)) tipo = 's';

        return UmCampo(tipo, largura, v, linha);
    }

    // =====================================================================
    //  metodos de texto, lista e dicionario
    // =====================================================================
    Valor ChamarNoTexto(string s, string metodo, List<Valor> args, int linha)
    {
        string S(int i) => i < args.Count ? args[i].Mostrar() : "";

        switch (metodo)
        {
            case "format": return Valor.DeTexto(FormatarChaves(s, args, linha));
            case "upper": return Valor.DeTexto(s.ToUpperInvariant());
            case "lower": return Valor.DeTexto(s.ToLowerInvariant());
            case "strip": return Valor.DeTexto(args.Count > 0 ? s.Trim(S(0).ToCharArray()) : s.Trim());
            case "lstrip": return Valor.DeTexto(s.TrimStart());
            case "rstrip": return Valor.DeTexto(s.TrimEnd());
            case "replace": return Valor.DeTexto(s.Replace(S(0), S(1)));
            case "startswith": return Valor.DeBool(s.StartsWith(S(0), StringComparison.Ordinal));
            case "endswith": return Valor.DeBool(s.EndsWith(S(0), StringComparison.Ordinal));
            case "find": return Valor.DeInt(s.IndexOf(S(0), StringComparison.Ordinal));
            case "count": return Valor.DeInt(s.Split(S(0)).Length - 1);
            case "isdigit": return Valor.DeBool(s.Length > 0 && s.All(char.IsDigit));
            case "isalpha": return Valor.DeBool(s.Length > 0 && s.All(char.IsLetter));
            case "zfill": return Valor.DeTexto(s.PadLeft((int)(args.Count > 0 ? args[0].Num : 0), '0'));
            case "encode" or "decode": return Valor.DeTexto(s);

            case "split":
            {
                var partes = args.Count > 0
                    ? s.Split(S(0))
                    : s.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                return Valor.DeReferencia(new ListaPy([.. partes.Select(Valor.DeTexto)]));
            }

            case "join":
            {
                if (args.Count == 0 || args[0].Referencia is not ListaPy lista)
                    throw new ErroDeExecucao(Idioma.T("join() precisa de uma lista"), linha);
                return Valor.DeTexto(string.Join(s, lista.Itens.Select(v => v.Mostrar())));
            }
        }

        throw new ErroDeExecucao(Idioma.T("o texto nao tem o metodo '{0}()'", metodo), linha);
    }

    /// <summary>
    /// O "{}".format(...) -- e a forma que aparece no sketch de MQTT do
    /// Wokwi, entao precisa aceitar "{}" e "{0}".
    /// </summary>
    static string FormatarChaves(string molde, List<Valor> args, int linha)
    {
        var saida = new System.Text.StringBuilder();
        int proximo = 0;

        for (int i = 0; i < molde.Length; i++)
        {
            if (molde[i] != '{') { saida.Append(molde[i]); continue; }
            if (i + 1 < molde.Length && molde[i + 1] == '{') { saida.Append('{'); i++; continue; }

            int fecha = molde.IndexOf('}', i);
            if (fecha < 0) throw new ErroDeExecucao(Idioma.T("faltou fechar uma chave no format()"), linha);

            string dentro = molde[(i + 1)..fecha];
            i = fecha;

            string formato = null;
            int doisPontos = dentro.IndexOf(':');
            if (doisPontos >= 0)
            {
                formato = dentro[(doisPontos + 1)..];
                dentro = dentro[..doisPontos];
            }

            var v = int.TryParse(dentro, out int pos)
                ? (pos < args.Count ? args[pos] : Valor.DeTexto(""))
                : (proximo < args.Count ? args[proximo++] : Valor.DeTexto(""));

            saida.Append(formato is null ? TextoPy.De(v) : ComFormato(v, formato, linha));
        }

        return saida.ToString();
    }

    Valor ChamarNaLista(ListaPy l, string metodo, List<Valor> args, int linha)
    {
        var primeiro = args.Count > 0 ? args[0] : Valor.Vazio;

        switch (metodo)
        {
            case "append": l.Itens.Add(primeiro); return Valor.Vazio;
            case "extend":
                if (primeiro.Referencia is ListaPy outra) l.Itens.AddRange(outra.Itens);
                return Valor.Vazio;

            case "insert":
                l.Itens.Insert(Math.Clamp((int)primeiro.Num, 0, l.Itens.Count),
                               args.Count > 1 ? args[1] : Valor.Vazio);
                return Valor.Vazio;

            case "pop":
            {
                if (l.Itens.Count == 0)
                    throw new ErroDeExecucao(Idioma.T("pop() numa lista vazia"), linha);
                int i = args.Count > 0 ? l.Posicao((int)primeiro.Num) : l.Itens.Count - 1;
                if (i < 0) throw new ErroDeExecucao(Idioma.T("indice fora da lista"), linha);
                var saiu = l.Itens[i];
                l.Itens.RemoveAt(i);
                return saiu;
            }

            case "remove":
            {
                int i = l.Itens.FindIndex(v => v.Mostrar() == primeiro.Mostrar());
                if (i >= 0) l.Itens.RemoveAt(i);
                return Valor.Vazio;
            }

            case "clear": l.Itens.Clear(); return Valor.Vazio;
            case "reverse": l.Itens.Reverse(); return Valor.Vazio;
            case "count":
                return Valor.DeInt(l.Itens.Count(v => v.Mostrar() == primeiro.Mostrar()));
            case "index":
                return Valor.DeInt(l.Itens.FindIndex(v => v.Mostrar() == primeiro.Mostrar()));

            case "sort":
                l.Itens.Sort((a, b) => a.EhTexto || b.EhTexto
                    ? string.CompareOrdinal(a.Mostrar(), b.Mostrar())
                    : a.Num.CompareTo(b.Num));
                return Valor.Vazio;
        }

        throw new ErroDeExecucao(Idioma.T("a lista nao tem o metodo '{0}()'", metodo), linha);
    }

    Valor ChamarNoDicionario(DicionarioPy d, string metodo, List<Valor> args, int linha)
    {
        string chave = args.Count > 0 ? args[0].Mostrar() : "";

        switch (metodo)
        {
            case "get":
                return d.Itens.TryGetValue(chave, out var v) ? v
                     : args.Count > 1 ? args[1] : Valor.Nenhum;

            case "keys":
                return Valor.DeReferencia(new ListaPy([.. d.Ordem.Select(Valor.DeTexto)]));

            case "values":
                return Valor.DeReferencia(new ListaPy([.. d.Ordem.Select(k => d.Itens[k])]));

            case "items":
                return Valor.DeReferencia(new ListaPy([.. d.Ordem.Select(
                    k => Valor.DeReferencia(
                        new ListaPy([Valor.DeTexto(k), d.Itens[k]], true)))]));

            case "pop":
            {
                if (!d.Itens.TryGetValue(chave, out var saiu))
                    return args.Count > 1 ? args[1] : Valor.Nenhum;
                d.Itens.Remove(chave);
                d.Ordem.Remove(chave);
                return saiu;
            }

            case "update":
                if (args.Count > 0 && args[0].Referencia is DicionarioPy outro)
                    foreach (var (k, valor) in outro.Pares()) d.Por(k, valor);
                return Valor.Vazio;

            case "clear":
                d.Itens.Clear();
                d.Ordem.Clear();
                return Valor.Vazio;
        }

        throw new ErroDeExecucao(Idioma.T("o dicionario nao tem o metodo '{0}()'", metodo), linha);
    }
}
