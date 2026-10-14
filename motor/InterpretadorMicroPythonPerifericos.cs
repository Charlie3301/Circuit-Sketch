namespace Simulador;

/// <summary>
/// O MicroPython com os mesmos perifericos do C++: o I2C nos pinos de fabrica
/// de cada porta (e o segundo barramento, e o SoftI2C), o SSD1306 desenhando
/// em pixels com a fonte 8x8 do framebuf, e a UART que sai pelo fio para
/// outra placa. Antes o OLED do Python era so uma tela de texto, o scan()
/// achava a peca em qualquer pino e machine.UART nao existia.
/// </summary>
public partial class Interpretador
{
    string ChipPy => maq.Perfil?.Id is { } id && id.StartsWith("esp32", StringComparison.Ordinal) ? ModuloNativo.Chip(id) : Chip;

    // ---------------- I2C ----------------

    /// <summary>Os pinos (scl, sda) de fabrica do I2C(id) no MicroPython (o machine_i2c.h de cada porta e o quickref).</summary>
    (int Scl, int Sda)? PinosDoI2cPy(int id) => (ChipPy, id) switch
    {
        ("esp32", 0) => (18, 19),
        ("esp32", 1) => (25, 26),
        ("esp32c3", 0) or ("esp32s3", 0) or ("esp32s3", 1) => (9, 8),
        ("esp32s2", 0) or ("esp32c6", 0) or ("esp32h2", 0) => (18, 19),
        ("esp32s2", 1) or ("esp32h2", 1) => (9, 8),
        ("RP2040", 0) => maq.Perfil?.Id == "nano-rp2040-connect" ? (13, 12) : (5, 4),
        ("RP2040", 1) => (7, 6),
        _ => null,
    };

    /// <summary>"I2C(0, scl=Pin(22), sda=Pin(21))", "SoftI2C(scl=Pin(5), sda=Pin(4))".</summary>
    Valor NovoI2c(string classe, List<Valor> args, Dictionary<string, Valor> nomeados, int linha)
    {
        int Pino(string nome) => nomeados.TryGetValue(nome, out var v) ? NumeroDePino(v, linha) : -1;
        int scl = Pino("scl"), sda = Pino("sda");
        BarramentoI2c bus;

        // O I2C do ESP8266 e por software, como o SoftI2C: qualquer pino, e os dois sao obrigatorios.
        if (classe == "SoftI2C" || Chip == "ESP8266")
        {
            if (scl < 0 && args.Count > 0 && args[0].Referencia is Objeto) scl = NumeroDePino(args[0], linha);
            if (sda < 0 && args.Count > 1 && args[1].Referencia is Objeto) sda = NumeroDePino(args[1], linha);
            if (scl < 0 || sda < 0)
                throw new ErroDeExecucao($"TypeError: o {classe} por software precisa de scl=Pin(...) e sda=Pin(...)", linha);
            bus = maq.I2cPorSoftware(sda, scl);
        }
        else
        {
            int id = args.Count > 0 && args[0].Referencia is null ? (int)args[0].Num
                   : nomeados.TryGetValue("id", out var i) ? (int)i.Num : 0;
            var fabrica = PinosDoI2cPy(id) ?? throw new ErroDeExecucao($"ValueError: I2C({id}) doesn't exist", linha);
            if (scl < 0) scl = fabrica.Scl;
            if (sda < 0) sda = fabrica.Sda;

            // A Pico so liga o i2c0 nos GPIO 0, 4, 8... (SDA) e 1, 5, 9... (SCL); o i2c1, nos 2, 6... e 3, 7...
            if (Chip == "RP2040")
            {
                if (sda % 4 != (id == 0 ? 0 : 2)) throw new ErroDeExecucao($"ValueError: bad SDA pin -- o I2C({id}) da Pico nao sai no GP{sda}", linha);
                if (scl % 4 != (id == 0 ? 1 : 3)) throw new ErroDeExecucao($"ValueError: bad SCL pin -- o I2C({id}) da Pico nao sai no GP{scl}", linha);
            }
            bus = id == 0 ? maq.I2c : maq.I2c1;
        }

        bus.End();
        bus.Begin(sda, scl);
        bus.Frequencia = nomeados.TryGetValue("freq", out var f) ? (long)f.Num : 400_000;
        bus.TamanhoDoBuffer = 4096;   // o MicroPython nao corta a transmissao em 32 bytes

        var objeto = new Objeto("I2C") { Rede = bus };
        objeto.Campos["scl"] = Valor.DeInt(scl);
        objeto.Campos["sda"] = Valor.DeInt(sda);
        return Valor.DeReferencia(objeto);
    }

    /// <summary>
    /// Quem responde no endereco -- ou o OSError da placa: ENODEV na ESP32 e
    /// na ESP8266, EIO na Pico. Com o aviso de onde a peca esta, se esta em
    /// outro fio.
    /// </summary>
    DispositivoI2c AparelhoPy(BarramentoI2c bus, int endereco, int linha)
    {
        if (bus.Achar(endereco) is { } quem) return quem;
        string erro = Chip == "RP2040" ? "[Errno 5] EIO" : "[Errno 19] ENODEV";
        string onde = bus.ForaDoFio(endereco) is { PinoSda: >= 0 } fora
            ? $" -- o {fora.Nome} esta nos pinos SDA {fora.PinoSda} / SCL {fora.PinoScl}, e este I2C usa SDA {bus.Sda} / SCL {bus.Scl}"
            : $" -- ninguem respondeu no endereco 0x{endereco:X2} (a peca esta ligada nos pinos deste I2C?)";
        throw new ErroDeExecucao($"OSError: {erro}{onde}", linha);
    }

    /// <summary>Os bytes de um valor do Python: b'...', bytearray, lista de numeros, um numero.</summary>
    static byte[] BytesPy(Valor v)
    {
        if (v.EhTexto) return TextoArduino.ParaBytes(v.Texto);
        if (v.Referencia is ListaPy lista) return [.. lista.Itens.Select(x => (byte)((long)x.Num & 0xFF))];
        return [(byte)((long)v.Num & 0xFF)];
    }

    /// <summary>O tempo da transmissao: 9 bits por byte (8 e o ACK) na frequencia do barramento.</summary>
    void GastarNoI2c(BarramentoI2c bus, int bytes)
        => maq.EsperarNoChip(bytes * 9 * 1e6 / Math.Max(1000, bus.Frequencia));

    Valor ChamarI2cPy(Objeto o, string metodo, List<Valor> args, Dictionary<string, Valor> nomeados, int linha)
    {
        var bus = o.Rede as BarramentoI2c ?? maq.I2c;
        int A(int i) => i < args.Count ? (int)args[i].Num : 0;

        switch (metodo)
        {
            // O scan() devolve os enderecos que responderam -- no fio deste I2C.
            case "scan":
            {
                var achados = new ListaPy();
                foreach (int endereco in bus.Aparelhos.Select(a => a.Endereco).Distinct()) achados.Itens.Add(Valor.DeInt(endereco));
                GastarNoI2c(bus, 112);
                return Valor.DeReferencia(achados);
            }

            case "writeto":
            {
                var quem = AparelhoPy(bus, A(0), linha);
                var bytes = args.Count > 1 ? BytesPy(args[1]) : [];
                quem.Receber(bytes);
                GastarNoI2c(bus, bytes.Length + 1);
                return Valor.DeInt(bytes.Length);
            }

            case "writeto_mem":
            {
                var quem = AparelhoPy(bus, A(0), linha);
                byte[] bytes = [(byte)A(1), .. args.Count > 2 ? BytesPy(args[2]) : []];
                quem.Receber(bytes);
                GastarNoI2c(bus, bytes.Length + 1);
                return Valor.Vazio;
            }

            case "readfrom":
            case "readfrom_mem":
            {
                var quem = AparelhoPy(bus, A(0), linha);
                bool doRegistrador = metodo == "readfrom_mem";
                if (doRegistrador) quem.Receber([(byte)A(1)]);
                int n = Math.Max(0, A(doRegistrador ? 2 : 1));
                var lidos = quem.Responder(n);
                GastarNoI2c(bus, n + (doRegistrador ? 3 : 1));
                return Valor.DeTexto(TextoArduino.DeBytes(lidos));
            }

            case "init":
            {
                int Pino(string nome) => nomeados.TryGetValue(nome, out var v) ? NumeroDePino(v, linha) : -1;
                int sda = Pino("sda"), scl = Pino("scl");
                bus.End();
                bus.Begin(sda >= 0 ? sda : bus.Sda, scl >= 0 ? scl : bus.Scl);
                if (nomeados.TryGetValue("freq", out var f)) bus.Frequencia = (long)f.Num;
                return Valor.Vazio;
            }

            case "deinit":
                bus.End();
                return Valor.Vazio;
        }

        throw new ErroDeExecucao($"I2C.{metodo}() ainda nao e suportado pelo simulador. O I2C tem scan, writeto, readfrom, "
                                 + "writeto_mem, readfrom_mem, init e deinit.", linha);
    }

    // ---------------- SSD1306 (o driver ssd1306.py, em cima do framebuf) ----------------

    /// <summary>
    /// "SSD1306_I2C(128, 64, i2c, addr=0x3c)", "SSD1306_SPI(128, 64, spi, dc, res, cs)".
    /// O construtor do driver ja manda os comandos de partida: sem a peca no
    /// fio do I2C, o OSError sai aqui -- o primeiro erro que todo mundo ve.
    /// </summary>
    Valor NovoOled(string classe, List<Valor> args, Dictionary<string, Valor> nomeados, int linha)
    {
        int l = args.Count > 0 ? Math.Max(8, (int)args[0].Num) : 128, a = args.Count > 1 ? Math.Max(8, (int)args[1].Num) : 64;
        bool spi = classe == "SSD1306_SPI";
        var objeto = new Objeto("SSD1306") { Lcd = Maq.CriarOled(), Tela = new TelaGrafica(l, a) { Monocromatica = true } };
        objeto.Lcd.Begin(21, 8);

        int endereco = -1, numero = -1;
        if (!spi)
        {
            var bus = args.Count > 2 && args[2].Referencia is Objeto { Rede: BarramentoI2c b } ? b
                : throw new ErroDeExecucao("SSD1306_I2C(largura, altura, i2c): o terceiro argumento e o I2C da tela", linha);
            endereco = nomeados.TryGetValue("addr", out var ad) ? (int)ad.Num : args.Count > 3 ? (int)args[3].Num : 0x3C;
            numero = bus.Numero;
            RegistrarNoI2c(objeto, endereco, numero);
            AparelhoPy(bus, endereco, linha);
            objeto.Campos["_freq"] = Valor.DeInt(bus.Frequencia);
        }
        objeto.Campos["_spi"] = Valor.DeBool(spi);

        var painel = Maq.CriarPainelOled(l, a, numero, endereco, spi);
        painel.Iniciar();
        painel.Escurecer(false);
        painel.PararRolagem(maq.AgoraUs() / 1000);
        painel.Limpar(0);
        objeto.Rede = painel;
        maq.EsperarNoChip(TempoDoShow(objeto));
        return Valor.DeReferencia(objeto);
    }

    /// <summary>O show(): o buffer inteiro pelo barramento (~23 ms no I2C a 400 kHz).</summary>
    static double TempoDoShow(Objeto o)
    {
        int bytes = o.Tela.LarguraFisica * ((o.Tela.AlturaFisica + 7) / 8) + 7;
        if (o.Campos.TryGetValue("_spi", out var s) && s.Verdadeiro) return bytes * 8 / 10.0 + 50;   // SPI a 10 MHz
        double freq = o.Campos.TryGetValue("_freq", out var f) ? f.Num : 400_000;
        return bytes * 9 * 1e6 / Math.Max(1000, freq);
    }

    Valor ChamarOledPy(Objeto o, string metodo, List<Valor> args, int linha)
    {
        var tela = o.Tela;
        var painel = o.Rede as TelaGrafica;
        int A(int i) => i < args.Count ? (int)args[i].Num : 0;
        int Cor(int i) => i < args.Count && args[i].Num != 0 ? 1 : 0;
        bool Cheio(int i) => i < args.Count && args[i].Verdadeiro;

        switch (metodo)
        {
            case "fill": tela.Limpar(Cor(0)); o.Lcd?.Limpar(); return Valor.Vazio;
            case "pixel":
                if (args.Count < 3) return Valor.DeInt(tela.CorEm(A(0), A(1)) == tela.CorAcesa ? 1 : 0);
                tela.Pixel(A(0), A(1), Cor(2));
                return Valor.Vazio;
            case "hline": if (A(2) > 0) tela.Retangulo(A(0), A(1), A(2), 1, Cor(3), true); return Valor.Vazio;
            case "vline": if (A(2) > 0) tela.Retangulo(A(0), A(1), 1, A(2), Cor(3), true); return Valor.Vazio;
            case "line": tela.Linha(A(0), A(1), A(2), A(3), Cor(4)); return Valor.Vazio;
            case "rect": if (A(2) > 0 && A(3) > 0) tela.Retangulo(A(0), A(1), A(2), A(3), Cor(4), Cheio(5)); return Valor.Vazio;
            case "fill_rect": if (A(2) > 0 && A(3) > 0) tela.Retangulo(A(0), A(1), A(2), A(3), Cor(4), true); return Valor.Vazio;
            case "ellipse": Elipse(tela, A(0), A(1), A(2), A(3), Cor(4), Cheio(5)); return Valor.Vazio;

            // text(s, x, y, c=1): a fonte 8x8 do framebuf, fundo transparente.
            case "text":
            {
                string s = args.Count > 0 ? args[0].Mostrar() : "";
                TextoDoFramebuf(tela, s, A(1), A(2), args.Count > 3 ? Cor(3) : 1);
                o.Lcd?.PosicionarCursor(A(1) / 6, A(2) / 8);
                o.Lcd?.Escrever(s);
                return Valor.Vazio;
            }

            case "scroll": RolarFramebuf(tela, A(0), A(1)); return Valor.Vazio;

            case "show":
                painel?.CopiarDe(tela);
                maq.EsperarNoChip(TempoDoShow(o));
                return Valor.Vazio;

            case "contrast": painel?.Escurecer(A(0) < 16); return Valor.Vazio;
            case "invert": painel?.Inverter(args.Count > 0 && args[0].Verdadeiro); return Valor.Vazio;
            case "poweroff": painel?.MostrarImagem(false); return Valor.Vazio;
            case "poweron": painel?.MostrarImagem(true); return Valor.Vazio;
            case "rotate": return Valor.Vazio;
            case "write_cmd":
                switch (A(0) & 0xFF)
                {
                    case 0xAE: painel?.MostrarImagem(false); break;
                    case 0xAF: painel?.MostrarImagem(true); break;
                    case 0xA6: painel?.Inverter(false); break;
                    case 0xA7: painel?.Inverter(true); break;
                }
                return Valor.Vazio;
        }

        throw new ErroDeExecucao($"SSD1306.{metodo}() ainda nao e suportado pelo simulador. A tela tem fill, pixel, hline, vline, "
                                 + "line, rect, fill_rect, ellipse, text, scroll, show, contrast, invert, poweroff e poweron.", linha);
    }

    /// <summary>O text() do framebuf: 8 colunas por letra, bit 0 em cima; fora de 32..127 vira o 127.</summary>
    static void TextoDoFramebuf(TelaGrafica tela, string s, int x, int y, int cor)
    {
        foreach (byte b in TextoArduino.ParaBytes(s))
        {
            int ch = b is < 32 or > 127 ? 127 : b;
            int k = (ch - 32) * 8;
            for (int j = 0; j < 8; j++)
            {
                int coluna = FonteDoFramebuf.Petme128[k + j];
                for (int dy = 0; coluna != 0; coluna >>= 1, dy++)
                    if ((coluna & 1) != 0) tela.Pixel(x + j, y + dy, cor);
            }
            x += 8;
        }
    }

    /// <summary>O scroll(dx, dy) do framebuf: move o desenho; o que fica para tras continua como estava.</summary>
    static void RolarFramebuf(TelaGrafica tela, int dx, int dy)
    {
        int l = tela.Largura, a = tela.Altura;
        var antes = new bool[l, a];
        for (int x = 0; x < l; x++)
            for (int y = 0; y < a; y++)
                antes[x, y] = tela.CorEm(x, y) == tela.CorAcesa;
        for (int x = 0; x < l; x++)
            for (int y = 0; y < a; y++)
            {
                int ox = x - dx, oy = y - dy;
                if (ox >= 0 && ox < l && oy >= 0 && oy < a) tela.Pixel(x, y, antes[ox, oy] ? 1 : 0);
            }
    }

    /// <summary>ellipse(x, y, xr, yr, c, f): o contorno (ou cheia), como o do framebuf.</summary>
    static void Elipse(TelaGrafica tela, int cx, int cy, int rx, int ry, int cor, bool cheia)
    {
        if (rx < 0 || ry < 0) return;
        bool Dentro(int x, int y) => rx == 0 || ry == 0
            ? x * x <= rx * rx && y * y <= ry * ry
            : (double)x * x / ((rx + 0.5) * (rx + 0.5)) + (double)y * y / ((ry + 0.5) * (ry + 0.5)) <= 1;
        for (int y = -ry; y <= ry; y++)
            for (int x = -rx; x <= rx; x++)
            {
                if (!Dentro(x, y)) continue;
                if (cheia || !Dentro(x + 1, y) || !Dentro(x - 1, y) || !Dentro(x, y + 1) || !Dentro(x, y - 1))
                    tela.Pixel(cx + x, cy + y, cor);
            }
    }

    // ---------------- UART ----------------

    readonly Dictionary<int, PortaSerial> uartsPy = [];

    /// <summary>Os pinos (tx, rx) de fabrica da UART(id) no MicroPython de cada porta (o quickref).</summary>
    (int Tx, int Rx)? PinosDaUartPy(int id) => (ChipPy, id) switch
    {
        ("esp32", 0) => (1, 3), ("esp32", 1) => (10, 9), ("esp32", 2) => (17, 16),
        ("esp32s2" or "esp32s3" or "esp32c3" or "esp32c6" or "esp32h2", 0) => PinosDaPorta("Serial") is { } s ? (s.Tx, s.Rx) : null,
        ("esp32s2" or "esp32s3" or "esp32c3" or "esp32c6" or "esp32h2", 1) => PinosDaPorta("Serial1") is { } s1 ? (s1.Tx, s1.Rx) : null,
        ("esp32s3", 2) => (20, 19),
        ("RP2040", 0) => (0, 1), ("RP2040", 1) => (4, 5),
        ("ESP8266", 0) => (1, 3), ("ESP8266", 1) => (2, -1),
        _ => null,
    };

    /// <summary>"UART(1, baudrate=9600, tx=Pin(17), rx=Pin(16))": a mesma UART do C++, que sai pelo fio.</summary>
    Valor NovaUartPy(List<Valor> args, Dictionary<string, Valor> nomeados, int linha)
    {
        int id = args.Count > 0 ? (int)args[0].Num : nomeados.TryGetValue("id", out var i) ? (int)i.Num : 1;
        var fabrica = PinosDaUartPy(id) ?? throw new ErroDeExecucao($"ValueError: UART({id}) doesn't exist", linha);

        if (!uartsPy.TryGetValue(id, out var p))
        {
            var (buffer, fifo) = TamanhosDaUart();
            p = new PortaSerial(maq, $"UART{id}", fabrica.Rx, fabrica.Tx, Math.Max(buffer, 256), fifo, null);
            uartsPy[id] = p;
        }
        ConfigurarUartPy(p, args.Count > 1 ? args[1] : (Valor?)null, nomeados, linha);
        var objeto = new Objeto("UART") { Rede = p };
        objeto.Campos["id"] = Valor.DeInt(id);
        return Valor.DeReferencia(objeto);
    }

    void ConfigurarUartPy(PortaSerial p, Valor? baudSolto, Dictionary<string, Valor> nomeados, int linha)
    {
        long baud = nomeados.TryGetValue("baudrate", out var b) ? (long)b.Num : baudSolto is { } bs ? (long)bs.Num : p.Baud > 0 ? p.Baud : 115200;
        if (nomeados.TryGetValue("tx", out var tx)) p.Tx = NumeroDePino(tx, linha);
        if (nomeados.TryGetValue("rx", out var rx)) p.Rx = NumeroDePino(rx, linha);
        if (nomeados.TryGetValue("rxbuf", out var rb)) p.Buffer = Math.Max(32, (int)rb.Num);
        // O timeout do MicroPython e 0: o read() devolve na hora o que ja chegou.
        p.TimeoutMs = nomeados.TryGetValue("timeout", out var t) ? Math.Max(0, (long)t.Num) : 0;
        LigarPorta(p, baud);
    }

    Valor ChamarUartPy(Objeto o, string metodo, List<Valor> args, Dictionary<string, Valor> nomeados, int linha)
    {
        var p = (PortaSerial)o.Rede;
        int A(int i) => i < args.Count ? (int)args[i].Num : -1;
        p.Atualizar();

        switch (metodo)
        {
            case "write":
            {
                var bytes = args.Count > 0 ? BytesPy(args[0]) : [];
                p.Transmitir(bytes);
                return Valor.DeInt(bytes.Length);
            }

            case "any": return Valor.DeInt(p.Entrada.Disponivel);

            // read(n): ate n bytes (ou o que chegou ate o timeout); nada -> None.
            case "read":
            {
                int n = A(0);
                EsperarNaPorta(p, () => n > 0 ? p.Entrada.Disponivel >= n : p.Entrada.Disponivel > 0 && !p.ChegandoAinda);
                if (p.Entrada.Disponivel == 0) return Valor.Nenhum;
                var lidos = new List<byte>();
                while ((n < 0 || lidos.Count < n) && p.Entrada.Ler() is var c && c >= 0) lidos.Add((byte)c);
                return Valor.DeTexto(TextoArduino.DeBytes([.. lidos]));
            }

            // readline(): ate o '\n' (incluido), ou o que chegou ate o timeout; nada -> None.
            case "readline":
            {
                EsperarNaPorta(p, () => p.Entrada.Contem('\n'));
                if (p.Entrada.Disponivel == 0) return Valor.Nenhum;
                bool temFim = p.Entrada.Contem('\n');
                string linhaLida = temFim ? p.Entrada.LerAte('\n') + "\n" : p.Entrada.LerTudo();
                return Valor.DeTexto(linhaLida);
            }

            case "init":
                ConfigurarUartPy(p, args.Count > 0 ? args[0] : (Valor?)null, nomeados, linha);
                return Valor.Vazio;
            case "deinit": p.Ligada = false; return Valor.Vazio;
            case "flush": p.Esvaziar(); return Valor.Vazio;
            case "txdone": return Valor.DeBool(p.CabeNoTx() >= p.FifoDoTx);
            case "sendbreak": return Valor.Vazio;
        }

        throw new ErroDeExecucao($"UART.{metodo}() ainda nao e suportado pelo simulador. A UART tem write, read, readline, any, "
                                 + "init, deinit, flush e txdone.", linha);
    }
}
