namespace Simulador;

/// <summary>
/// O SPI do chip no sketch interpretado: SPI.begin(), beginTransaction()
/// com o SPISettings, transfer() -- com os bits saindo de verdade nos pinos
/// MOSI e SCK, no ritmo do relogio do barramento, e o MISO lido a cada bit.
/// O analisador logico mostra o byte; um registrador de deslocamento montado
/// com flip-flops recebe; e o SD.begin() sabe em quais pinos o barramento
/// esta, para conferir a ligacao do cartao.
///
/// Antes nada disso existia: "SPI.begin()" parava o sketch com "o objeto
/// 'SPI' nao foi declarado", em qualquer placa.
///
/// Os pinos padrao sao os da placa (o SS, MOSI, MISO e SCK do core). Na
/// ESP32 o SPI.begin(sck, miso, mosi, ss) troca, e o SPIClass(HSPI) e outro
/// barramento; na Pico, setRX/setTX/setSCK/setCS; na STM32,
/// setMOSI/setMISO/setSCLK/setSSEL; no ESP8266, pins(). No AVR o SPI e fixo.
/// </summary>
public partial class Interpretador
{
    /// <summary>Um barramento SPI do chip, como o sketch o configurou.</summary>
    sealed class BarramentoDoSketch
    {
        public string Nome;
        public int Sck = -1, Miso = -1, Mosi = -1, Ss = -1;
        public bool Iniciado, CsPeloHardware, EmTransacao;
        public double Hz;
        public bool Msb = true;
        public int Modo;
    }

    /// <summary>O SPISettings: velocidade, ordem dos bits e modo.</summary>
    sealed record ConfiguracaoSpi(double Hz, bool Msb, int Modo);

    readonly Dictionary<string, BarramentoDoSketch> barramentosSpi = new(StringComparer.Ordinal);
    bool avisouDoSpiSemBegin;

    string Chip => maq.Perfil?.Processador ?? "";
    bool SpiDaEsp32Classica => Chip == "Xtensa LX6";

    /// <summary>A velocidade de um barramento que ninguem configurou: a do SPISettings() de cada core.</summary>
    double FrequenciaPadraoDoSpi => EhAvr ? 4_000_000 : EhFamiliaEsp32 || Chip == "ESP8266" ? 1_000_000 : 4_000_000;

    /// <summary>O SPI (ou o SPI1) global do core, com os pinos padrao da placa.</summary>
    BarramentoDoSketch BarramentoGlobal(string nome)
    {
        if (barramentosSpi.TryGetValue(nome, out var b)) return b;
        b = new BarramentoDoSketch { Nome = nome, Hz = FrequenciaPadraoDoSpi };
        if (nome == "SPI1" && EhFamiliaEsp32)
        {
            // O segundo barramento do core da ESP32: o HSPI. So a ESP32 classica
            // tem pinos de fabrica nele; nas outras eles vem do begin().
            if (SpiDaEsp32Classica) (b.Sck, b.Miso, b.Mosi, b.Ss) = (14, 12, 13, 15);
        }
        else if (nome == "SPI1")
        {
            // A Pico: o SPI1 do arduino-pico (variant da Pico).
            (b.Miso, b.Ss, b.Sck, b.Mosi) = (12, 13, 14, 15);
        }
        else
        {
            var perfil = maq.Perfil;
            b.Sck = perfil.PinoDoCore("SCK");
            b.Miso = perfil.PinoDoCore("MISO");
            b.Mosi = perfil.PinoDoCore("MOSI");
            b.Ss = perfil.PinoDoCore("SS");
        }
        barramentosSpi[nome] = b;
        return b;
    }

    /// <summary>"SPIClass hspi(HSPI)", "new SPIClass(VSPI)", "SPIClass SPI_2(PB15, PB14, PB13)".</summary>
    void MontarBarramentoSpi(Objeto obj, List<Valor> args, int linha)
    {
        var b = new BarramentoDoSketch { Nome = obj.Classe, Hz = FrequenciaPadraoDoSpi };
        int A(int i) => i < args.Count ? (int)args[i].Num : -1;
        if (EhFamiliaEsp32)
        {
            int numero = args.Count > 0 ? A(0) : (SpiDaEsp32Classica ? 2 : 0);
            if (SpiDaEsp32Classica && numero == 3) (b.Sck, b.Miso, b.Mosi, b.Ss) = (18, 19, 23, 5);        // VSPI
            else if (SpiDaEsp32Classica && numero == 2) (b.Sck, b.Miso, b.Mosi, b.Ss) = (14, 12, 13, 15);  // HSPI
            else if (SpiDaEsp32Classica && numero == 1)
                throw new ErroDeExecucao(Idioma.T("SPIClass(FSPI) na ESP32 e o barramento da memoria flash (GPIO 6 a 11): usar ele trava a placa. Use HSPI ou VSPI."), linha);
            else if (numero == 0)
            {
                var padrao = BarramentoGlobal("SPI");
                (b.Sck, b.Miso, b.Mosi, b.Ss) = (padrao.Sck, padrao.Miso, padrao.Mosi, padrao.Ss);
            }
            // O HSPI da S2/S3 nao tem pinos padrao: vem do begin(sck, miso, mosi, ss).
        }
        else if (Chip.StartsWith("STM32", StringComparison.Ordinal) && args.Count >= 3)
        {
            (b.Mosi, b.Miso, b.Sck) = (A(0), A(1), A(2));
            b.Ss = A(3);
        }
        else
        {
            var padrao = BarramentoGlobal("SPI");
            (b.Sck, b.Miso, b.Mosi, b.Ss) = (padrao.Sck, padrao.Miso, padrao.Mosi, padrao.Ss);
        }
        obj.Rede = b;
    }

    /// <summary>O barramento que um argumento aponta: "hspi", "&amp;SPI", "*vspi".</summary>
    BarramentoDoSketch BarramentoDoArgumento(Valor v)
    {
        if (v.Referencia is Objeto { Rede: BarramentoDoSketch b }) return b;
        if (v.EhTexto && v.Texto is "SPI" or "SPI1") return BarramentoGlobal(v.Texto);
        return BarramentoGlobal("SPI");
    }

    /// <summary>
    /// SPI_MODE0..3, SPI_CLOCK_DIVn, HSPI/VSPI/FSPI: os numeros de cada core
    /// (o SPI_MODE1 do AVR e 0x04, o da ESP32 e 1).
    /// </summary>
    bool ConstanteDoSpi(string nome, out Valor valor)
    {
        valor = Valor.Vazio;
        long? n = nome switch
        {
            "SPI_MODE0" => 0,
            "SPI_MODE1" => EhAvr ? 0x04 : 1,
            "SPI_MODE2" => EhAvr ? 0x08 : 2,
            "SPI_MODE3" => EhAvr ? 0x0C : 3,
            "SPI_MSBFIRST" => 1,
            "SPI_LSBFIRST" => 0,
            "FSPI" when EhFamiliaEsp32 => SpiDaEsp32Classica ? 1 : 0,
            "HSPI" when EhFamiliaEsp32 && Chip != "RISC-V" => SpiDaEsp32Classica ? 2 : 1,
            "VSPI" when SpiDaEsp32Classica => 3,
            _ when nome.StartsWith("SPI_CLOCK_DIV", StringComparison.Ordinal)
                   && int.TryParse(nome["SPI_CLOCK_DIV".Length..], out int div) && div is 2 or 4 or 8 or 16 or 32 or 64 or 128
                => EhAvr
                    ? div switch { 4 => 0x00, 16 => 0x01, 64 => 0x02, 128 => 0x03, 2 => 0x04, 8 => 0x05, _ => 0x06 }
                    : div,   // nos outros cores o valor guarda o divisor (16 MHz / n, como no AVR)
            _ => null,
        };
        if (n is null) return false;
        valor = Valor.DeInt(n.Value);
        return true;
    }

    /// <summary>O SPISettings(clock, ordem, modo) escrito no sketch.</summary>
    Objeto NovaConfiguracaoSpi(List<Valor> args)
    {
        double hz = args.Count > 0 ? args[0].Num : 4_000_000;
        bool msb = args.Count <= 1 || args[1].Num != 0;
        int modo = args.Count > 2 ? ModoDoSpi((int)args[2].Num) : 0;
        return new Objeto("SPISettings") { Rede = new ConfiguracaoSpi(hz, msb, modo) };
    }

    /// <summary>O modo 0..3, do numero do core.</summary>
    int ModoDoSpi(int n) => EhAvr ? (n >> 2) & 3 : n & 3;

    string NomeDoPinoSpi(int p) => EhAvr ? Idioma.T("pino {0}", p) : $"GPIO {p}";

    // =====================================================================
    //  os metodos
    // =====================================================================

    Valor ChamarSpi(BarramentoDoSketch b, string metodo, Chamada c, Ambiente amb)
    {
        var args = c.Args.Select(a => Avaliar(a, amb)).ToList();
        int A(int i) => i < args.Count ? (int)args[i].Num : -1;

        // O vetor de um argumento ("transfer(buf, 4)"): ele mesmo, e nao uma copia.
        Valor[] Vetor(int i) => i < c.Args.Count && VetorDoArgumento(c.Args[i], amb, out var v) ? v
                                : i < args.Count ? args[i].Referencia as Valor[] : null;

        switch (metodo)
        {
            case "begin":
            {
                // ESP32: begin(sck, miso, mosi, ss). O barramento que ja
                // comecou nao muda de pinos (o begin de novo nao faz nada).
                if (b.Iniciado) return Valor.DeBool(true);
                if (args.Count > 0)
                {
                    if (!(EhFamiliaEsp32 || Chip == "ESP8266"))
                        throw new ErroDeExecucao(Idioma.T("SPI.begin() com pinos e da ESP32: no {0} o SPI {1}", maq.Perfil?.Nome, (EhAvr ? Idioma.T("tem pinos fixos.") : Idioma.T("troca de pinos com setRX/setTX/setSCK (Pico) ou setMOSI/setMISO/setSCLK (STM32)."))),
                                                 c.Linha);
                    (b.Sck, b.Miso, b.Mosi) = (A(0), A(1), A(2));
                    if (args.Count > 3) b.Ss = A(3);
                }
                if (b.Sck < 0 || b.Mosi < 0)
                    throw new ErroDeExecucao(Idioma.T("{0}.begin(): {1} nao tem pino. {2}", b.Nome, (b.Sck < 0 ? Idioma.T("o SCK") : Idioma.T("o MOSI")), (EhFamiliaEsp32 ? Idioma.T("Passe os pinos: begin(sck, miso, mosi, ss).") : Idioma.T("A {0} nao tem SPI de hardware.", maq.Perfil?.Nome))),
                                             c.Linha);
                b.Iniciado = true;

                // Como o core faz: SCK e MOSI saida, em repouso; MISO entrada.
                // No AVR o SS vira saida em nivel alto -- senao, um SS em
                // nivel baixo poe o SPI em modo escravo e ele para.
                maq.PinMode(b.Sck, 1);
                maq.PinMode(b.Mosi, 1);
                if (b.Miso >= 0) maq.PinMode(b.Miso, 0);
                PorOSckEmRepouso(b);
                if (EhAvr && b.Ss >= 0)
                {
                    maq.PinMode(b.Ss, 1);
                    maq.DigitalWrite(b.Ss, 1);
                }
                return Valor.DeBool(true);
            }

            case "end":
                b.Iniciado = false;
                return Valor.Vazio;

            case "beginTransaction":
                if (args.Count > 0 && args[0].Referencia is Objeto { Rede: ConfiguracaoSpi conf })
                    (b.Hz, b.Msb, b.Modo) = (conf.Hz, conf.Msb, conf.Modo);
                b.EmTransacao = true;
                if (b.Iniciado) PorOSckEmRepouso(b);
                return Valor.Vazio;

            case "endTransaction":
                b.EmTransacao = false;
                return Valor.Vazio;

            case "transfer" when args.Count >= 2 && Vetor(0) is { } buffer:
            {
                // transfer(buf, n): cada byte sai e o que chega fica no lugar.
                int n = Math.Min(buffer.Length, Math.Max(0, A(1)));
                if (TransferirEmBloco(b, buffer, n, buffer)) return Valor.Vazio;
                for (int i = 0; i < n; i++)
                    buffer[i] = NoLugar(buffer[i], Valor.DeC(Transferir(b, (int)buffer[i].Num & 0xFF, 8, c.Linha), TipoC.UChar));
                return Valor.Vazio;
            }

            case "transfer":
            case "write":
                return Valor.DeC(Transferir(b, A(0) & 0xFF, 8, c.Linha), TipoC.UChar);

            case "transfer16":
            case "write16":
                return Valor.DeC(Transferir(b, A(0) & 0xFFFF, 16, c.Linha), TipoC.UShort);

            case "transfer32":
            case "write32":
                return Valor.DeC(Transferir(b, (long)(args.Count > 0 ? args[0].Num : 0) & 0xFFFFFFFFL, 32, c.Linha), TipoC.UInt);

            // ESP32: transferBytes(saida, entrada, n) e writeBytes(dados, n).
            case "transferBytes":
            case "writeBytes":
            case "writePixels":
            {
                var saida = Vetor(0);
                var entrada = metodo == "transferBytes" ? Vetor(1) : null;
                int n = metodo == "transferBytes" ? A(2) : A(1);
                if (saida is null) return Valor.Vazio;
                n = Math.Min(saida.Length, Math.Max(0, n));
                if (TransferirEmBloco(b, saida, n, entrada)) return Valor.Vazio;
                for (int i = 0; i < n; i++)
                {
                    long lido = Transferir(b, (int)saida[i].Num & 0xFF, 8, c.Linha);
                    if (entrada is not null && i < entrada.Length) entrada[i] = NoLugar(entrada[i], Valor.DeC(lido, TipoC.UChar));
                }
                return Valor.Vazio;
            }

            case "setBitOrder":
                b.Msb = A(0) != 0;
                return Valor.Vazio;

            case "setDataMode":
                b.Modo = ModoDoSpi(A(0));
                return Valor.Vazio;

            case "setFrequency":
                b.Hz = Math.Max(1, args.Count > 0 ? args[0].Num : 1_000_000);
                return Valor.Vazio;

            case "setClockDivider":
            {
                int n = A(0);
                int divisor = EhAvr
                    ? n switch { 0x00 => 4, 0x01 => 16, 0x02 => 64, 0x03 => 128, 0x04 => 2, 0x05 => 8, _ => 32 }
                    : Math.Max(1, n);
                b.Hz = (EhAvr ? (maq.Perfil?.Megahertz ?? 16) * 1_000_000 : 16_000_000) / divisor;
                return Valor.Vazio;
            }

            case "setHwCs":
                b.CsPeloHardware = args.Count > 0 && args[0].Verdadeiro;
                if (b.CsPeloHardware && b.Ss >= 0)
                {
                    maq.PinMode(b.Ss, 1);
                    maq.DigitalWrite(b.Ss, 1);
                }
                return Valor.Vazio;

            case "pinSS":
                return Valor.DeInt(b.Ss);

            // A Pico: os pinos antes do begin().
            case "setRX": b.Miso = A(0); return Valor.DeBool(true);
            case "setTX": b.Mosi = A(0); return Valor.DeBool(true);
            case "setSCK": b.Sck = A(0); return Valor.DeBool(true);
            case "setCS": b.Ss = A(0); return Valor.DeBool(true);

            // A STM32.
            case "setMOSI": b.Mosi = A(0); return Valor.Vazio;
            case "setMISO": b.Miso = A(0); return Valor.Vazio;
            case "setSCLK": b.Sck = A(0); return Valor.Vazio;
            case "setSSEL": b.Ss = A(0); return Valor.Vazio;

            // O ESP8266: pins(sck, miso, mosi, ss).
            case "pins":
                (b.Sck, b.Miso, b.Mosi) = (A(0), A(1), A(2));
                if (args.Count > 3) b.Ss = A(3);
                return Valor.DeBool(true);

            case "usingInterrupt":
            case "notUsingInterrupt":
            case "attachInterrupt":
            case "detachInterrupt":
                return Valor.Vazio;
        }

        throw new ErroDeExecucao(Idioma.T("{0}.{1}() ainda nao e suportado pelo simulador.", b.Nome, metodo), c.Linha);
    }

    /// <summary>
    /// Um bloco de bytes (writeBytes, transferBytes, transfer(buf, n)) para um
    /// chip que esta ouvindo o barramento -- o display com o CS em LOW: os
    /// bytes chegam nele inteiros e o relogio anda o tempo do barramento, sem
    /// as dezesseis bordas de cada byte nos pinos. E assim que o chip manda
    /// um bloco (a ESP32, de 64 em 64 bytes, sem o processador no meio), e e
    /// o que deixa uma tela inteira sair nos 30 ms que ela leva a 40 MHz.
    /// Falso: ninguem ouve (ou o bloco e pequeno) -- os bits vao pelos pinos,
    /// para o analisador logico e o registrador de deslocamento verem.
    /// </summary>
    bool TransferirEmBloco(BarramentoDoSketch b, Valor[] saida, int n, Valor[] entrada)
    {
        // Os displays amostram na subida do SCK, o bit mais alto primeiro:
        // fora disso o que chegaria nos pinos e outra coisa.
        if (n < 8 || !b.Iniciado || !b.Msb || b.Modo is 1 or 2 || b.CsPeloHardware) return false;
        if (maq.EscutaDoSpi is not { } escuta) return false;

        var bytes = new byte[n];
        for (int i = 0; i < n; i++) bytes[i] = (byte)((int)saida[i].Num & 0xFF);
        if (!escuta.Entregar(b.Sck, b.Mosi, bytes)) return false;

        // O display nao responde: o que volta e o nivel em que o MISO esta.
        if (entrada is not null)
        {
            int volta = b.Miso >= 0 && maq.DigitalRead(b.Miso) != 0 ? 0xFF : 0;
            for (int i = 0; i < n && i < entrada.Length; i++) entrada[i] = NoLugar(entrada[i], Valor.DeC(volta, TipoC.UChar));
        }

        maq.TempoDoPeriferico(n * 8e6 / Math.Max(1, b.Hz));
        maq.GastarProcessador(EhAvr ? 0.5 * n : EhFamiliaEsp32 ? 1.5 * ((n + 63) / 64) : 1.0 * n);
        return true;
    }

    /// <summary>
    /// O SCK vai para o nivel de repouso do modo. Para um chip que escuta os
    /// fios isso nao e um bit: sem o aviso, o display sem pino CS (o ST7789
    /// de 240x240, que pede o modo 3) contaria a subida como o primeiro bit
    /// de um byte e perderia o alinhamento de todos os seguintes.
    /// </summary>
    void PorOSckEmRepouso(BarramentoDoSketch b)
    {
        maq.PinoDoPeriferico(b.Sck, b.Modo >= 2 ? 1 : 0, 0);
        maq.EscutaDoSpi?.Repousou(b.Sck);
    }

    /// <summary>
    /// Um palavra de 8, 16 ou 32 bits pelo barramento: cada bit no MOSI, a
    /// borda do SCK no meio periodo, o MISO lido na borda de amostragem do
    /// modo -- e o relogio do chip anda o tempo do barramento. Devolve o que
    /// chegou pelo MISO.
    /// </summary>
    long Transferir(BarramentoDoSketch b, long saida, int bits, int linha)
    {
        if (!b.Iniciado)
        {
            if (EhAvr)
            {
                // No AVR o SPI desligado nunca levanta o SPIF: o transfer()
                // espera para sempre, e a placa fica parada aqui.
                if (!avisouDoSpiSemBegin)
                {
                    avisouDoSpiSemBegin = true;
                    maq.Avisar(Idioma.T("{0}: {1}.transfer() antes do {2}.begin(): no AVR o SPI esta desligado, o byte nunca termina de sair, e a placa trava aqui para sempre.", Origens.Onde(linha), b.Nome, b.Nome));
                }
                while (true)
                {
                    if (maq.Parar) throw maq.MotivoDaParada();
                    maq.EsperarMicros(1000);
                }
            }
            if (!avisouDoSpiSemBegin)
            {
                avisouDoSpiSemBegin = true;
                maq.Avisar(Idioma.T("{0}: {1}.transfer() antes do {2}.begin(): na placa nada sai pelos pinos, e o que volta e 0.", Origens.Onde(linha), b.Nome, b.Nome));
            }
            return 0;
        }

        bool cpol = b.Modo >= 2, cpha = (b.Modo & 1) != 0;
        int ativo = cpol ? 0 : 1, repouso = cpol ? 1 : 0;
        double meio = 0.5e6 / Math.Max(1, b.Hz);
        bool cs = b.CsPeloHardware && b.Ss >= 0;
        if (cs) maq.PinoDoPeriferico(b.Ss, 0, 0);

        long entrada = 0;
        for (int i = 0; i < bits; i++)
        {
            int k = b.Msb ? bits - 1 - i : i;
            int bit = (int)((saida >> k) & 1);
            if (!cpha)
            {
                // Modo 0 e 2: o dado muda na borda de repouso, e lido na ativa.
                maq.PinoDoPeriferico(b.Mosi, bit, 0);
                maq.PinoDoPeriferico(b.Sck, ativo, meio);
                if (b.Miso >= 0 && maq.DigitalRead(b.Miso) != 0) entrada |= 1L << k;
                maq.PinoDoPeriferico(b.Sck, repouso, meio);
            }
            else
            {
                // Modo 1 e 3: o dado muda na borda ativa, e lido na de repouso.
                maq.PinoDoPeriferico(b.Sck, ativo, i == 0 ? 0 : meio);
                maq.PinoDoPeriferico(b.Mosi, bit, 0);
                maq.PinoDoPeriferico(b.Sck, repouso, meio);
                if (b.Miso >= 0 && maq.DigitalRead(b.Miso) != 0) entrada |= 1L << k;
            }
        }
        if (cpha) maq.PinoDoPeriferico(b.Sck, repouso, meio);
        if (cs) maq.PinoDoPeriferico(b.Ss, 1, 0);

        // O processador: a chamada e a espera pelo fim do byte (o SPIF do
        // AVR, o registrador de estado da ESP32).
        maq.GastarProcessador(EhAvr ? 0.5 : EhFamiliaEsp32 ? 1.5 : 1.0);
        return entrada;
    }

    // =====================================================================
    //  o cartao SD no barramento
    // =====================================================================

    /// <summary>
    /// O SD.begin() confere a ligacao como a placa confere, sem querer: o
    /// cartao so responde no CS que o begin() usa, e com o MOSI, o MISO e o
    /// SCK do barramento. Nulo: esta certo -- ou o circuito nao diz (a linha
    /// de comando sem --aparelhos).
    /// </summary>
    string ProblemaNaLigacaoDoSd(int cs, BarramentoDoSketch b, out bool doCs)
    {
        doCs = false;
        if (maq.AparelhosSpi?.Invoke() is not { } aparelhos) return null;
        var cartoes = aparelhos.Where(a => a.Tipo == "sd").ToList();
        if (cartoes.Count == 0) return null;

        var sd = cartoes.FirstOrDefault(a => a.Cs == cs);
        if (sd is null)
        {
            int ligado = cartoes[0].Cs;
            doCs = true;
            return ligado < 0
                ? Idioma.T("o CS do cartao nao esta ligado na placa (o begin() usa o {0})", NomeDoPinoSpi(cs))
                : Idioma.T("o CS do cartao esta no {0}, e o begin() usa o {1}", NomeDoPinoSpi(ligado), NomeDoPinoSpi(cs));
        }

        string Linha(string nome, int doCartao, int doBarramento)
            => doCartao < 0 ? Idioma.T("o {0} do cartao nao esta ligado na placa", nome)
               : doBarramento >= 0 && doCartao != doBarramento
                   ? Idioma.T("o {0} do cartao esta no {1}, e o SPI da placa usa o {2}", nome, NomeDoPinoSpi(doCartao), NomeDoPinoSpi(doBarramento))
                   : null;
        return Linha("SCK", sd.Sck, b.Sck) ?? Linha(Idioma.T("DI (o MOSI)"), sd.Mosi, b.Mosi) ?? Linha(Idioma.T("DO (o MISO)"), sd.Miso, b.Miso);
    }

    /// <summary>Como trocar os pinos do SPI, em cada placa -- a dica do aviso.</summary>
    string DicaDosPinosDoSpi(BarramentoDoSketch b)
        => EhFamiliaEsp32 ? Idioma.T(" Ligue nos pinos do SPI (SCK {0}, MISO {1}, MOSI {2}) ou chame SPI.begin(sck, miso, mosi, cs) com os da ligacao antes do SD.begin().", b.Sck, b.Miso, b.Mosi)
           : EhAvr ? Idioma.T(" No {0} o SPI e fixo: SCK no {1}, MISO no {2}, MOSI no {3}.", maq.Perfil?.Nome, b.Sck, b.Miso, b.Mosi)
           : Idioma.T(" O SPI da placa usa SCK {0}, MISO {1} e MOSI {2}.", b.Sck, b.Miso, b.Mosi);
}
