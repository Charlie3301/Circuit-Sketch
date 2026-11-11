namespace Simulador.App;

/// <summary>
/// Os chips que escutam o barramento (os controladores dos displays de SPI):
/// o programa fala com o chip -- sem biblioteca imitada no meio -- e o vidro
/// mostra. Pelo SPI de hardware e batendo os pinos, no C interpretado, no
/// C++ compilado e no MicroPython.
/// </summary>
public static partial class Autoteste
{
    static void Chips_Spi()
    {
        saida.AppendLine("Os chips do SPI (ST7789, ILI9341, ST7735 e o OLED) pelos bytes do barramento");

        const int Vermelho = unchecked((int)0xFFFF0000), Verde = unchecked((int)0xFF00FF00), Azul = unchecked((int)0xFF0000FF);
        static string Cor(TelaGrafica t, int x, int y)
            => t is null ? "sem tela" : ((uint)t.Pixels[y * t.LarguraFisica + x]).ToString("X8");

        // O sketch interpretado, com os aparelhos do circuito.
        static (Maquina maq, string erro) Interpretado(string sketch, params AparelhoNoSpi[] aparelhos)
        {
            var maq = new Maquina { Perfil = PerfilPlaca.Esp32 };
            maq.Reiniciar();
            maq.AparelhosSpi = () => aparelhos;
            try
            {
                var interp = new Interpretador(new Parser(Lexer.Tokenizar(sketch)).AnalisarPrograma(), maq);
                interp.Preparar();
                interp.Setup();
                return (maq, null);
            }
            catch (Exception e) { return (maq, e.Message); }
        }

        // ---- o ILI9341 batendo os pinos (shiftOut), no sketch interpretado ----
        {
            const string sketch = """
                const int CS = 5, DC = 2, RST = 4, MOSI_ = 23, SCK_ = 18;
                void manda(byte b) { shiftOut(MOSI_, SCK_, MSBFIRST, b); }
                void cmd(byte c) { digitalWrite(DC, LOW); manda(c); digitalWrite(DC, HIGH); }
                void janela(int x0, int y0, int x1, int y1) {
                  cmd(0x2A); manda(x0 >> 8); manda(x0 & 255); manda(x1 >> 8); manda(x1 & 255);
                  cmd(0x2B); manda(y0 >> 8); manda(y0 & 255); manda(y1 >> 8); manda(y1 & 255);
                  cmd(0x2C);
                }
                void setup() {
                  pinMode(CS, OUTPUT); pinMode(DC, OUTPUT); pinMode(RST, OUTPUT);
                  pinMode(MOSI_, OUTPUT); pinMode(SCK_, OUTPUT);
                  digitalWrite(CS, HIGH); digitalWrite(RST, LOW); digitalWrite(RST, HIGH);

                  // Com o CS em HIGH nada entra: estes bytes sao de outro aparelho.
                  cmd(0x29); janela(5, 5, 5, 5); manda(0xFF); manda(0xFF);

                  digitalWrite(CS, LOW);
                  cmd(0x01); cmd(0x11); cmd(0x3A); manda(0x55);
                  cmd(0x36); manda(0x48);              // em pe: MX | BGR
                  cmd(0x29);
                  janela(0, 0, 2, 0);
                  manda(0xF8); manda(0x00);            // vermelho
                  manda(0x07); manda(0xE0);            // verde
                  manda(0x00); manda(0x1F);            // azul
                  janela(239, 319, 239, 319); manda(0xF8); manda(0x00);
                  cmd(0x36); manda(0x28);              // deitado: MV | BGR
                  janela(0, 0, 0, 0); manda(0x07); manda(0xE0);
                  cmd(0x36); manda(0xA8);              // MV | MY | BGR: so as linhas da memoria espelham
                  janela(0, 0, 0, 0); manda(0x00); manda(0x1F);
                  digitalWrite(CS, HIGH);
                }
                void loop() {}
                """;
            var (maq, erro) = Interpretado(sketch, new AparelhoNoSpi("ili9341", 5, 2, 4, 23, -1, 18));
            var t = maq.Tela;
            Conferir("chips: o ILI9341 entende os bytes batidos nos pinos (shiftOut), so com o CS em LOW",
                erro is null && t is { LarguraFisica: 240, AlturaFisica: 320, Mostrando: true }
                && t.Pixels[0] == Vermelho && t.Pixels[1] == Verde && t.Pixels[2] == Azul
                && t.Pixels[5 * 240 + 5] != unchecked((int)0xFFFFFFFF)
                && t.Pixels[239] == Verde,                // MV: o (0,0) deitado e o canto de cima da direita
                $"{erro} | {t?.LarguraFisica}x{t?.AlturaFisica} {Cor(t, 0, 0)} {Cor(t, 1, 0)} {Cor(t, 2, 0)} {Cor(t, 5, 5)} {Cor(t, 239, 0)}");

            // MV|MY: a coluna pedida anda pelas linhas da memoria, de baixo para cima -- o (0,0) cai
            // no canto de baixo da direita (por cima do vermelho que estava la), e nao no (0,0) do vidro.
            Conferir("chips: no MADCTL o MY espelha as linhas da memoria mesmo com os eixos trocados (a tabela do chip)",
                t is not null && t.Pixels[319 * 240 + 239] == Azul && t.Pixels[0] == Vermelho,
                $"{Cor(t, 239, 319)} {Cor(t, 0, 0)}");
        }

        // ---- a biblioteca imitada manda no vidro: o chip fica de fora ----
        {
            const string sketch = """
                #include <SPI.h>
                #include <Adafruit_GFX.h>
                #include <Adafruit_ILI9341.h>
                Adafruit_ILI9341 tft(5, 2, 4);
                void setup() {
                  tft.begin();
                  tft.fillScreen(0xF800);
                  // Outro aparelho no mesmo barramento (o CS dele e outro pino): o display nao pode ler isto.
                  pinMode(15, OUTPUT); digitalWrite(15, LOW);
                  SPI.begin();
                  SPI.transfer(0x28); SPI.transfer(0x2C); SPI.transfer(0x00); SPI.transfer(0x00);
                  digitalWrite(15, HIGH);
                }
                void loop() {}
                """;
            var (maq, erro) = Interpretado(sketch, new AparelhoNoSpi("ili9341", 5, 2, 4, 23, -1, 18));
            var t = maq.Tela;
            Conferir("chips: com a Adafruit imitada desenhando, os bytes de outro aparelho no SPI nao estragam a tela",
                erro is null && t is { Mostrando: true } && t.Pixels[0] == Vermelho && t.Pixels[^1] == Vermelho,
                $"{erro} | {Cor(t, 0, 0)} mostrando={t?.Mostrando}");
        }

        if (!CompiladorNativo.Disponivel)
        {
            Conferir("chips: o compilador (nativo/zig) esta instalado", false, "faltou nativo/zig/zig.exe");
            return;
        }

        // ---- o ST7789 sem pino CS, pelo SPI de hardware no modo 3 (C++ compilado) ----
        {
            const string sketch = """
                #include <SPI.h>
                const int DC = 2, RST = 4;
                void cmd(uint8_t c) { digitalWrite(DC, LOW); SPI.transfer(c); digitalWrite(DC, HIGH); }
                void dado(uint8_t d) { SPI.transfer(d); }
                void janela(int x0, int y0, int x1, int y1) {
                  cmd(0x2A); dado(x0 >> 8); dado(x0); dado(x1 >> 8); dado(x1);
                  cmd(0x2B); dado(y0 >> 8); dado(y0); dado(y1 >> 8); dado(y1);
                  cmd(0x2C);
                }
                void setup() {
                  Serial.begin(115200);
                  pinMode(DC, OUTPUT); pinMode(RST, OUTPUT);
                  SPI.begin(18, -1, 23, -1);
                  digitalWrite(RST, HIGH); delay(2); digitalWrite(RST, LOW); delay(2); digitalWrite(RST, HIGH); delay(2);
                  SPI.beginTransaction(SPISettings(8000000, MSBFIRST, SPI_MODE3));
                  cmd(0x01); delay(2); cmd(0x11);
                  cmd(0x3A); dado(0x55);
                  cmd(0x36); dado(0xC0);               // em pe: MX | MY, e a janela comeca na linha 80
                  cmd(0x21); cmd(0x29);
                  janela(0, 80, 239, 81);
                  for (int i = 0; i < 240; i++) { dado(0xF8); dado(0x00); }
                  for (int i = 0; i < 240; i++) { dado(0x07); dado(0xE0); }
                  janela(10, 100, 10, 100); dado(0x00); dado(0x1F);
                  cmd(0x36); dado(0x00);               // de ponta-cabeca: sem deslocamento
                  janela(0, 0, 0, 0); dado(0xFF); dado(0xFF);
                  SPI.endTransaction();
                  Serial.println("pronto");
                }
                void loop() { delay(10); }
                """;
            var (serial, erro, nativo, m) = RodarNativo(sketch, 4000, antes: (_, maq) =>
                maq.AparelhosSpi = () => [new AparelhoNoSpi("st7789", -1, 2, 4, 23, -1, 18)],
                durante: maq => { lock (maq.Trava) if (maq.Serial.Any(l => l.Contains("pronto"))) maq.Parar = true; });
            var t = m.Tela;
            Conferir("chips: o ST7789 sem CS, pelo SPI de hardware no modo 3 (o repouso do SCK nao vira bit)",
                nativo && erro is null && serial.Contains("pronto")
                && t is { LarguraFisica: 240, AlturaFisica: 240, Mostrando: true, Invertida: false }
                && t.Pixels[0] == Vermelho && t.Pixels[239] == Vermelho && t.Pixels[240] == Verde
                && t.Pixels[20 * 240 + 10] == Azul && t.Pixels[239 * 240 + 239] == unchecked((int)0xFFFFFFFF),
                $"{nativo} {erro} | {serial} | {t?.LarguraFisica}x{t?.AlturaFisica} mostrando={t?.Mostrando} invertida={t?.Invertida} "
                + $"{Cor(t, 0, 0)} {Cor(t, 239, 0)} {Cor(t, 0, 1)} {Cor(t, 10, 20)} {Cor(t, 239, 239)}");
        }

        // ---- o ILI9341 no HSPI, a tela inteira (C++ compilado) ----
        {
            const string sketch = """
                #include <SPI.h>
                SPIClass hspi(HSPI);
                const int CS = 15, DC = 2;
                void cmd(uint8_t c) { digitalWrite(DC, LOW); hspi.transfer(c); digitalWrite(DC, HIGH); }
                void setup() {
                  Serial.begin(115200);
                  pinMode(CS, OUTPUT); pinMode(DC, OUTPUT);
                  hspi.begin();
                  hspi.beginTransaction(SPISettings(40000000, MSBFIRST, SPI_MODE0));
                  digitalWrite(CS, LOW);
                  cmd(0x01); cmd(0x11); cmd(0x36); hspi.transfer(0x48); cmd(0x3A); hspi.transfer(0x55); cmd(0x29);
                  cmd(0x2A); hspi.transfer16(0); hspi.transfer16(239);
                  cmd(0x2B); hspi.transfer16(0); hspi.transfer16(319);
                  cmd(0x2C);
                  static uint8_t linha[480];
                  for (int i = 0; i < 240; i++) { linha[2 * i] = 0x07; linha[2 * i + 1] = 0xE0; }
                  Serial.println("inicio");
                  unsigned long t0 = millis();
                  for (int y = 0; y < 320; y++) hspi.writeBytes(linha, 480);
                  digitalWrite(CS, HIGH);
                  hspi.endTransaction();
                  Serial.print("pronto em "); Serial.println(millis() - t0);
                }
                void loop() { delay(10); }
                """;
            var relogio = System.Diagnostics.Stopwatch.StartNew();
            long levou = -1, comecou = -1;
            var (serial, erro, nativo, m) = RodarNativo(sketch, 60000, antes: (_, maq) =>
                maq.AparelhosSpi = () => [new AparelhoNoSpi("ili9341", 15, 2, -1, 13, 12, 14)],
                durante: maq =>
                {
                    lock (maq.Trava)
                    {
                        if (comecou < 0 && maq.Serial.Any(l => l.Contains("inicio"))) comecou = relogio.ElapsedMilliseconds;
                        if (!maq.Serial.Any(l => l.Contains("pronto"))) return;
                    }
                    if (levou < 0) levou = relogio.ElapsedMilliseconds - comecou;
                    maq.Parar = true;
                });
            var t = m.Tela;
            int verdes = t?.Pixels.Count(c => c == Verde) ?? -1;
            saida.AppendLine($"        a tela inteira (153600 bytes pelo SPI): {levou} ms de relogio | {serial}");
            Conferir("chips: o SPIClass(HSPI) do C++ compilado leva a tela inteira ao ILI9341 (o segundo barramento da ESP32)",
                nativo && erro is null && serial.Contains("pronto") && verdes == 240 * 320,
                $"{nativo} {erro} | {serial} | verdes={verdes}");
        }

        // ---- o OLED de SPI pelos bytes do chip (C++ compilado) ----
        {
            const string sketch = """
                #include <SPI.h>
                const int CS = 5, DC = 2, RST = 4;
                void cmd(uint8_t c) { digitalWrite(DC, LOW); digitalWrite(CS, LOW); SPI.transfer(c); digitalWrite(CS, HIGH); }
                void setup() {
                  Serial.begin(115200);
                  pinMode(CS, OUTPUT); pinMode(DC, OUTPUT); pinMode(RST, OUTPUT);
                  digitalWrite(CS, HIGH);
                  digitalWrite(RST, LOW); delay(2); digitalWrite(RST, HIGH);
                  SPI.begin();
                  cmd(0xAE); cmd(0x20); cmd(0x00); cmd(0xA1); cmd(0xC8); cmd(0x8D); cmd(0x14); cmd(0xAF);
                  cmd(0x21); cmd(0); cmd(127); cmd(0x22); cmd(0); cmd(7);
                  digitalWrite(DC, HIGH); digitalWrite(CS, LOW);
                  SPI.transfer(0x01);                   // (0,0)
                  SPI.transfer(0x80);                   // (1,7)
                  for (int i = 2; i < 128; i++) SPI.transfer(0x00);
                  SPI.transfer(0xFF);                   // a segunda pagina: (0,8) a (0,15)
                  digitalWrite(CS, HIGH);
                  Serial.println("pronto");
                }
                void loop() { delay(10); }
                """;
            var (serial, erro, nativo, m) = RodarNativo(sketch, 4000, antes: (_, maq) =>
                maq.AparelhosSpi = () => [new AparelhoNoSpi("ssd1306", 5, 2, 4, 23, -1, 18)],
                durante: maq => { lock (maq.Trava) if (maq.Serial.Any(l => l.Contains("pronto"))) maq.Parar = true; });
            TelaGrafica p;
            lock (m.PaineisOled) p = m.PaineisOled.FirstOrDefault(x => x.Spi).Painel;
            bool Aceso(int x, int y) => p is not null && p.Pixels[y * 128 + x] == p.CorAcesa;
            int acesos = p?.Pixels.Count(c => c == p.CorAcesa) ?? -1;
            Conferir("chips: o OLED de SPI mostra o que o programa grava no SSD1306 (D/C, enderecamento horizontal)",
                nativo && erro is null && serial.Contains("pronto") && p is { LarguraFisica: 128, AlturaFisica: 64 }
                && Aceso(0, 0) && Aceso(1, 7) && !Aceso(1, 0) && Aceso(0, 8) && Aceso(0, 15) && !Aceso(0, 16) && acesos == 10,
                $"{nativo} {erro} | {serial} | acesos={acesos}");
        }

        // ---- o MicroPython: o ssd1306.py dele pelo SPI, e o ST7735 pelo SoftSPI ----
        {
            const string py = """
                from machine import Pin, SPI, SoftSPI
                import ssd1306, time
                spi = SPI(1, baudrate=1000000, sck=Pin(18), mosi=Pin(23), miso=Pin(19))
                oled = ssd1306.SSD1306_SPI(128, 64, spi, Pin(2), Pin(4), Pin(5))
                oled.fill(0)
                oled.pixel(3, 5, 1)
                oled.text("Oi", 0, 16)
                oled.show()

                # O ST7735 em outros pinos, batendo os bits.
                bits = SoftSPI(baudrate=500000, polarity=0, phase=0, sck=Pin(14), mosi=Pin(13), miso=Pin(12))
                cs = Pin(15, Pin.OUT, value=1)
                dc = Pin(27, Pin.OUT, value=0)
                rst = Pin(26, Pin.OUT, value=1)
                rst(0); time.sleep_ms(2); rst(1); time.sleep_ms(2)
                def cmd(c, dados=b""):
                    cs(0); dc(0); bits.write(bytes([c]))
                    if dados:
                        dc(1); bits.write(dados)
                    cs(1)
                cmd(0x01); cmd(0x11); cmd(0x3A, b"\x05"); cmd(0x36, b"\xC0"); cmd(0x29)
                cmd(0x2A, b"\x00\x00\x00\x7F"); cmd(0x2B, b"\x00\x00\x00\x9F")
                cmd(0x2C, b"\xF8\x00" * 128 + b"\x07\xE0")
                cmd(0x2A, b"\x00\x7F\x00\x7F"); cmd(0x2B, b"\x00\x9F\x00\x9F")
                cmd(0x2C, b"\x00\x1F")
                print("pronto")
                """;
            var (serial, erro, nativo, m) = RodarNativo(py, 8000, antes: (_, maq) =>
                maq.AparelhosSpi = () => [new AparelhoNoSpi("ssd1306", 5, 2, 4, 23, -1, 18),
                                          new AparelhoNoSpi("st7735", 15, 27, 26, 13, -1, 14)],
                durante: maq => { lock (maq.Trava) if (maq.Serial.Any(l => l.Contains("pronto"))) maq.Parar = true; });
            TelaGrafica p;
            lock (m.PaineisOled) p = m.PaineisOled.FirstOrDefault(x => x.Spi).Painel;
            int noTexto = 0;
            if (p is not null)
                for (int y = 16; y < 24; y++)
                    for (int x = 0; x < 16; x++)
                        if (p.Pixels[y * 128 + x] == p.CorAcesa) noTexto++;
            int acesos = p?.Pixels.Count(c => c == p.CorAcesa) ?? -1;
            Conferir("chips: o ssd1306.py do MicroPython (SSD1306_SPI) desenha no OLED de SPI da bancada",
                nativo && erro is null && serial.Contains("pronto") && p is not null
                && p.Pixels[5 * 128 + 3] == p.CorAcesa && noTexto > 15 && acesos == noTexto + 1,
                $"{nativo} {erro} | {serial} | acesos={acesos} no texto={noTexto}");

            var t = m.Tela;
            Conferir("chips: o ST7735 pelo SoftSPI do MicroPython (128x160, em pe com MX|MY, cores em RGB)",
                t is { LarguraFisica: 128, AlturaFisica: 160, Mostrando: true }
                && t.Pixels[0] == Vermelho && t.Pixels[127] == Vermelho && t.Pixels[128] == Verde
                && t.Pixels[159 * 128 + 127] == Azul,
                $"{t?.LarguraFisica}x{t?.AlturaFisica} {Cor(t, 0, 0)} {Cor(t, 127, 0)} {Cor(t, 0, 1)} {Cor(t, 127, 159)}");
        }
    }
}
