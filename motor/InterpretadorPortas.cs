namespace Simulador;

/// <summary>
/// As portas seriais do sketch: o Serial (que tambem sai pelos pinos TX/RX
/// da placa), o Serial1/Serial2/Serial3 de cada core, o HardwareSerial
/// declarado (ESP32, STM32) e a SoftwareSerial. Antes so o Serial existia:
/// "Serial2.begin()" parava o sketch, e duas placas nao conversavam pelo fio.
///
/// Os pinos padrao sao os de cada core (o HardwareSerial.h da ESP32 3.3,
/// o pins_arduino.h do Mega e da Pico); o begin(baud, config, rx, tx) da
/// ESP32, o setRX/setTX da Pico e o construtor da STM32 trocam.
/// </summary>
public partial class Interpretador
{
    readonly Dictionary<string, PortaSerial> portasGlobais = new(StringComparer.Ordinal);

    /// <summary>(rx, tx) de cada porta do core, ou nulo: a placa nao tem essa porta.</summary>
    (int Rx, int Tx)? PinosDaPorta(string nome)
    {
        var perfil = maq.Perfil;
        string id = perfil?.Id ?? "", chip = perfil?.Processador ?? "";
        string esp = id.StartsWith("esp32", StringComparison.Ordinal) ? ModuloNativo.Chip(id) : null;
        return (esp ?? chip, nome) switch
        {
            ("esp32", "Serial") => (3, 1), ("esp32", "Serial1") => (26, 27), ("esp32", "Serial2") => (4, 25),
            ("esp32s2", "Serial") => (44, 43), ("esp32s2", "Serial1") => (4, 5),
            ("esp32s3", "Serial") => (44, 43), ("esp32s3", "Serial1") => (15, 16), ("esp32s3", "Serial2") => (19, 20),
            ("esp32c3", "Serial") => (20, 21), ("esp32c3", "Serial1") => (18, 19),
            ("esp32c6", "Serial") => (17, 16), ("esp32c6", "Serial1") => (4, 5),
            ("esp32h2", "Serial") => (23, 24), ("esp32h2", "Serial1") => (0, 1),
            ("ESP8266", "Serial") => (3, 1), ("ESP8266", "Serial1") => (-1, 2),
            ("ATmega328P", "Serial") => (0, 1),
            ("ATmega2560", "Serial") => (0, 1), ("ATmega2560", "Serial1") => (19, 18),
            ("ATmega2560", "Serial2") => (17, 16), ("ATmega2560", "Serial3") => (15, 14),
            ("RP2040", "Serial") => (-1, -1),   // a USB do proprio chip
            ("RP2040", "Serial1") => (1, 0),
            ("RP2040", "Serial2") when id != "nano-rp2040-connect" => (9, 8),
            ("STM32F103C8", "Serial") or ("STM32F103C8", "Serial1") => (10, 9),
            ("STM32F103C8", "Serial2") => (3, 2), ("STM32F103C8", "Serial3") => (27, 26),
            (_, "Serial") => (-1, -1),
            _ => null,
        };
    }

    /// <summary>O buffer de recepcao e a fila do TX de cada core.</summary>
    (int Buffer, int Fifo) TamanhosDaUart()
    {
        string chip = maq.Perfil?.Processador ?? "";
        if (EhAvr) return (64, 64);
        if (EhFamiliaEsp32) return (256, 128);
        return chip switch { "ESP8266" => (256, 128), "RP2040" => (32, 32), _ => (64, 64) };
    }

    /// <summary>O Serial, o Serial1... da placa. Nulo: ela nao tem essa porta (e o compilador recusaria).</summary>
    PortaSerial PortaGlobal(string nome, int linha)
    {
        if (portasGlobais.TryGetValue(nome, out var p)) return p;
        if (PinosDaPorta(nome) is not { } pinos)
            throw new ErroDeExecucao($"'{nome}' was not declared in this scope -- a {maq.Perfil?.Nome} nao tem essa porta. "
                                     + (EhAvr ? "No Uno a unica UART e o Serial (pinos 0 e 1); para outra, use a SoftwareSerial." : ""),
                                     linha);
        var (buffer, fifo) = TamanhosDaUart();
        p = new PortaSerial(maq, nome, pinos.Rx, pinos.Tx, buffer, fifo, nome == "Serial" ? maq.EntradaSerial : null);
        portasGlobais[nome] = p;
        return p;
    }

    void LigarPorta(PortaSerial p, long baud)
    {
        p.Baud = baud;
        p.Ligada = true;
        lock (maq.Portas)
            if (!maq.Portas.Contains(p)) maq.Portas.Add(p);
    }

    /// <summary>"HardwareSerial MySerial(1)" (ESP32), "HardwareSerial Serial2(PA3, PA2)" (STM32), "SoftwareSerial gps(4, 3)".</summary>
    void MontarPortaSerial(Objeto obj, string classe, List<Valor> args)
    {
        var (buffer, fifo) = TamanhosDaUart();
        int A(int i) => i < args.Count ? (int)args[i].Num : -1;
        if (classe == "SoftwareSerial")
        {
            obj.Rede = new PortaSerial(maq, "SoftwareSerial", A(0), A(1), 64, 0) { PorSoftware = true };
            return;
        }
        if (args.Count >= 2)
        {
            obj.Rede = new PortaSerial(maq, "HardwareSerial", A(0), A(1), buffer, fifo);
            return;
        }
        string nome = A(0) switch { 0 => "Serial", 1 => "Serial1", 2 => "Serial2", _ => "Serial1" };
        var (rx, tx) = PinosDaPorta(nome) ?? (-1, -1);
        obj.Rede = new PortaSerial(maq, nome, rx, tx, buffer, fifo);
    }

    /// <summary>
    /// Espera o que a leitura precisa (o terminador, os n bytes) chegar pelo
    /// fio, ate o timeout da porta sem chegar nada -- como o Stream.
    /// </summary>
    void EsperarNaPorta(PortaSerial p, Func<bool> pronto)
    {
        p.Atualizar();
        double inicio = maq.AgoraNoFio;
        while (!pronto() && !maq.Parar)
        {
            double desde = Math.Max(inicio, p.UltimaChegadaUs);
            if (maq.AgoraNoFio - desde >= p.TimeoutMs * 1000) break;
            if (!maq.TempoReal && !p.ChegandoAinda) break;   // no virtual, nada mais vira
            maq.Avancar(1);
            p.Atualizar();
        }
    }

    Valor ChamarPorta(PortaSerial p, string metodo, Chamada c, Ambiente amb)
    {
        var args = c.Args.Select(a => Avaliar(a, amb)).ToList();
        int A(int i) => i < args.Count ? (int)args[i].Num : -1;

        switch (metodo)
        {
            case "begin":
            {
                // ESP32: begin(baud, config, rx, tx); a SoftwareSerial do AVR passa a escutar.
                if (args.Count >= 4)
                {
                    if (A(2) >= 0) p.Rx = A(2);
                    if (A(3) >= 0) p.Tx = A(3);
                }
                if (p.Tx >= 0) maq.PinMode(p.Tx, 1);
                LigarPorta(p, args.Count > 0 ? (long)args[0].Num : 9600);
                if (p.PorSoftware && EhAvr) EscutarSo(p);
                return Valor.DeBool(true);
            }
            case "end":
                p.Ligada = false;
                return Valor.Vazio;
            case "updateBaudRate":
                p.Baud = A(0);
                return Valor.Vazio;
            case "setPins":
                if (A(0) >= 0) p.Rx = A(0);
                if (A(1) >= 0) p.Tx = A(1);
                return Valor.DeBool(true);
            case "setRX": p.Rx = A(0); return Valor.DeBool(true);
            case "setTX": p.Tx = A(0); return Valor.DeBool(true);
            case "setRxBufferSize": p.Buffer = Math.Max(1, A(0)); return Valor.DeInt(p.Buffer);
            case "setTxBufferSize": return Valor.DeInt(0);
            case "setTimeout": p.TimeoutMs = Math.Max(0, A(0)); return Valor.Vazio;
            case "listen":
                if (p.PorSoftware && EhAvr) EscutarSo(p);
                return Valor.DeBool(true);
            case "isListening": return Valor.DeBool(p.Escutando);
            case "stopListening": p.Escutando = false; return Valor.DeBool(true);
            case "overflow":
            {
                bool foi = p.Transbordou;
                p.Transbordou = false;
                return Valor.DeBool(foi);
            }
            case "availableForWrite": return Valor.DeInt(p.CabeNoTx());
            case "flush": p.Esvaziar(); return Valor.Vazio;

            case "print":
            case "println":
            {
                string texto = Imprimivel(args) + (metodo == "println" ? "\r\n" : "");
                var bytes = TextoArduino.ParaBytes(texto);
                p.Transmitir(bytes);
                return Valor.DeInt(bytes.Length);
            }
            case "printf":
            {
                var bytes = TextoArduino.ParaBytes(Formatar(args));
                p.Transmitir(bytes);
                return Valor.DeInt(bytes.Length);
            }
            case "write":
            {
                byte[] bytes;
                if (c.Args.Count >= 2 && VetorDoArgumento(c.Args[0], amb, out var vetor))
                    bytes = [.. vetor.Take(Math.Max(0, A(1))).Select(v => (byte)((long)v.Num & 0xFF))];
                else if (args.Count > 0 && args[0].EhTexto)
                {
                    bytes = TextoArduino.ParaBytes(args[0].Texto);
                    if (args.Count > 1) bytes = bytes[..Math.Clamp(A(1), 0, bytes.Length)];
                }
                else bytes = [(byte)((long)(args.Count > 0 ? args[0].Num : 0) & 0xFF)];
                p.Transmitir(bytes);
                return Valor.DeInt(bytes.Length);
            }

            // As leituras: o que ja chegou; as que esperam, ate o timeout.
            case "available": p.Atualizar(); return Valor.DeInt(p.Entrada.Disponivel);
            case "read": p.Atualizar(); return Valor.DeInt(p.Entrada.Ler());
            case "peek": p.Atualizar(); return Valor.DeInt(p.Entrada.Espiar());
            case "readStringUntil":
            {
                int fim = args.Count > 0 ? CodigoDe(args[0]) : '\n';
                EsperarNaPorta(p, () => p.Entrada.Contem(fim));
                return Valor.DeTexto(p.Entrada.LerAte(fim));
            }
            case "readString":
                EsperarNaPorta(p, () => false);
                return Valor.DeTexto(p.Entrada.LerTudo());
            case "readBytes":
            case "readBytesUntil":
            {
                bool comFim = metodo == "readBytesUntil";
                int indiceDoVetor = comFim ? 1 : 0;
                int n = comFim ? A(2) : A(1);
                int fim = comFim ? CodigoDe(args[0]) : -1;
                EsperarNaPorta(p, () => p.Entrada.Disponivel >= n || comFim && p.Entrada.Contem(fim));
                if (!VetorDoArgumento(c.Args[indiceDoVetor], amb, out var destino)) return Valor.DeInt(0);
                int lidos = 0;
                while (lidos < n && lidos < destino.Length)
                {
                    int b = p.Entrada.Ler();
                    if (b < 0 || comFim && b == fim) break;
                    destino[lidos] = NoLugar(destino[lidos], Valor.DeC(b, TipoC.UChar));
                    lidos++;
                }
                return Valor.DeInt(lidos);
            }
            case "parseInt":
                EsperarNaPorta(p, () => TemNumeroTerminado(p.Entrada));
                return Valor.DeInt(p.Entrada.LerInteiro());
            case "parseFloat":
                EsperarNaPorta(p, () => TemNumeroTerminado(p.Entrada));
                return Valor.DeReal(p.Entrada.LerReal());
            case "find":
            {
                string alvo = args.Count > 0 ? args[0].Mostrar() : "";
                EsperarNaPorta(p, () => p.Entrada.ContemTexto(alvo));
                return Valor.DeBool(p.Entrada.Procurar(alvo));
            }
        }
        throw new ErroDeExecucao($"{p.Nome}.{metodo}() ainda nao e suportado pelo simulador.", c.Linha);
    }

    /// <summary>O parseInt() so termina quando o numero acaba (chegou algo que nao e digito).</summary>
    static bool TemNumeroTerminado(FluxoDeEntrada fluxo)
    {
        int b = fluxo.Espiar();
        if (b < 0) return false;
        var texto = TextoArduino.DeBytes(fluxo.Copia());
        int i = 0;
        while (i < texto.Length && !char.IsAsciiDigit(texto[i]) && texto[i] != '-') i++;
        while (i < texto.Length && (char.IsAsciiDigit(texto[i]) || texto[i] is '-' or '.')) i++;
        return i < texto.Length;
    }

    /// <summary>A SoftwareSerial do AVR: so uma escuta de cada vez.</summary>
    void EscutarSo(PortaSerial p)
    {
        lock (maq.Portas)
            foreach (var outra in maq.Portas)
                if (outra.PorSoftware) outra.Escutando = outra == p;
        p.Escutando = true;
    }

    /// <summary>O Serial (o monitor) tambem sai pelo pino TX da placa: o que ele imprime vai pelo fio.</summary>
    void SerialPeloFio(string metodo, List<Valor> args, string impresso)
    {
        if (maq.LigadosAoPino is null) return;
        var p = PortaGlobal("Serial", 0);
        if (!p.Ligada || p.Tx < 0) return;
        byte[] bytes = metodo == "write" && args.Count > 0 && !args[0].EhTexto
            ? [(byte)((long)args[0].Num & 0xFF)]
            : TextoArduino.ParaBytes(impresso);
        // O byte ja saiu pelo pino (EnviarPelaSerial): aqui ele so chega em quem esta no fio.
        var destinos = maq.LigadosAoPino(p.Tx);
        if (destinos is not { Count: > 0 }) return;
        double porByte = 10e6 / Math.Max(1, p.Baud), agora = maq.AgoraNoFio;
        foreach (var (maquina, gpio) in destinos)
            foreach (var porta in maquina.PortasNoPino(gpio))
            {
                bool casa = Math.Abs(porta.Baud - p.Baud) <= p.Baud * 0.03;
                for (int i = 0; i < bytes.Length; i++)
                    porta.Receber(casa ? bytes[i] : (byte)(((bytes[i] ^ 0xA5) * 7 + i) & 0xFF), agora + (i + 1) * porByte);
            }
    }
}
