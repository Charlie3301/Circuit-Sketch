namespace Simulador.App;

/// <summary>
/// Os registradores do chip pelo endereco: o ponteiro para o GPIO_OUT, o
/// volatile uint32_t* que a TFT_eSPI guarda do SPI_CMD_REG. No C++ compilado
/// o endereco e o da placa, e cada acesso vira a leitura ou a escrita do
/// registrador no simulador (a pagina protegida, cs_ponte.cpp).
/// </summary>
public static partial class Autoteste
{
    static void Cpp_Registradores()
    {
        saida.AppendLine("Os registradores do chip por ponteiro (GPIO e SPI), no C++ compilado");
        if (!CompiladorNativo.Disponivel)
        {
            Conferir("registradores: o compilador (nativo/zig) esta instalado", false, "faltou nativo/zig/zig.exe");
            return;
        }

        const int Vermelho = unchecked((int)0xFFFF0000), Verde = unchecked((int)0xFF00FF00), Azul = unchecked((int)0xFF0000FF);
        static string Cor(TelaGrafica t, int x, int y)
            => t is null ? "sem tela" : ((uint)t.Pixels[y * t.LarguraFisica + x]).ToString("X8");
        static void PararNoPronto(Maquina maq)
        {
            lock (maq.Trava) if (maq.Serial.Any(l => l.Contains("pronto"))) maq.Parar = true;
        }

        // ---- o GPIO pelo ponteiro ----
        {
            const string sketch = """
                #include "soc/gpio_reg.h"
                void setup() {
                  Serial.begin(115200);
                  pinMode(2, OUTPUT); pinMode(4, OUTPUT);
                  pinMode(15, INPUT_PULLUP); pinMode(13, INPUT_PULLDOWN);
                  volatile uint32_t *sobe = (volatile uint32_t *)GPIO_OUT_W1TS_REG;
                  volatile uint32_t *desce = (volatile uint32_t *)GPIO_OUT_W1TC_REG;
                  *sobe = 1UL << 2;
                  Serial.print("a="); Serial.println(digitalRead(2));
                  *portOutputRegister(digitalPinToPort(4)) |= digitalPinToBitMask(4);
                  Serial.print("b="); Serial.println(digitalRead(4));
                  *desce = 1UL << 2;
                  Serial.print("c="); Serial.println(digitalRead(2));
                  uint32_t entrada = *portInputRegister(digitalPinToPort(15));
                  Serial.print("d="); Serial.print((entrada >> 15) & 1); Serial.print((entrada >> 13) & 1); Serial.println((entrada >> 4) & 1);
                  // O byte de cima do registrador de saida: so o pino 4 (bit 4 do byte 0) fica.
                  volatile uint8_t *byte0 = (volatile uint8_t *)GPIO_OUT_REG;
                  Serial.print("e="); Serial.println(*byte0, HEX);
                  // Um registrador que o simulador nao tem: a escrita se perde e a leitura volta zero.
                  volatile uint32_t *outro = (volatile uint32_t *)0x3FF48000;
                  *outro = 5;
                  Serial.print("f="); Serial.println(*outro);
                  Serial.println("pronto");
                }
                void loop() { delay(10); }
                """;
            var (serial, erro, nativo, m) = RodarNativo(sketch, 6000, durante: PararNoPronto);
            Conferir("registradores: o ponteiro para o GPIO (W1TS, W1TC, *portOutputRegister |= bit, *portInputRegister) mexe e le os pinos",
                nativo && erro is null && serial == "a=1|b=1|c=0|d=101|e=10|f=0|pronto" && m.Digital[2] == 0 && m.Digital[4] == 1,
                $"{nativo} {erro} | {serial} | {m.Digital[2]} {m.Digital[4]}");
        }

        // ---- o ponteiro quebrado continua sendo erro ----
        {
            const string sketch = """
                void setup() {
                  volatile int *p = (volatile int *)0x40;
                  *p = 1;
                }
                void loop() {}
                """;
            var (serial, erro, nativo, _) = RodarNativo(sketch, 3000);
            Conferir("registradores: o ponteiro invalido fora da faixa dos perifericos continua parando a placa com o erro",
                nativo && erro is not null && erro.Contains("escrita num endereco invalido"), $"{nativo} {erro} | {serial}");
        }

        // ---- o SPI pelos registradores, como a TFT_eSPI (a ESP32 classica, o VSPI) ----
        {
            const string sketch = """
                #include <SPI.h>
                #include "soc/spi_reg.h"
                #include "soc/gpio_struct.h"
                #define SPI_PORT VSPI
                volatile uint32_t *_spi_cmd = (volatile uint32_t *)(SPI_CMD_REG(SPI_PORT));
                volatile uint32_t *_spi_user = (volatile uint32_t *)(SPI_USER_REG(SPI_PORT));
                volatile uint32_t *_spi_mosi_dlen = (volatile uint32_t *)(SPI_MOSI_DLEN_REG(SPI_PORT));
                volatile uint32_t *_spi_w = (volatile uint32_t *)(SPI_W0_REG(SPI_PORT));
                const int TFT_CS = 15, TFT_DC = 2;
                #define DC_C GPIO.out_w1tc = (1 << TFT_DC)
                #define DC_D GPIO.out_w1ts = (1 << TFT_DC)
                #define CS_L GPIO.out_w1tc = (1 << TFT_CS)
                #define CS_H GPIO.out_w1ts = (1 << TFT_CS)
                #define TFT_WRITE_BITS(D, B) *_spi_mosi_dlen = B - 1; *_spi_w = D; *_spi_cmd = SPI_USR; while (*_spi_cmd & SPI_USR);
                #define tft_Write_8(C) TFT_WRITE_BITS(C, 8)
                uint32_t trocado(uint16_t c) { return (uint16_t)(c << 8 | c >> 8); }
                void cmd(uint8_t c) { DC_C; tft_Write_8(c); DC_D; }
                void janela(int x0, int y0, int x1, int y1) {
                  cmd(0x2A); TFT_WRITE_BITS(trocado(x1) << 16 | trocado(x0), 32);
                  cmd(0x2B); TFT_WRITE_BITS(trocado(y1) << 16 | trocado(y0), 32);
                  cmd(0x2C);
                }
                // O pushBlock: os 64 bytes ficam nos W0..W15 e cada disparo manda de novo.
                void bloco(uint16_t cor, uint32_t pontos) {
                  uint32_t cor32 = trocado(cor) << 16 | trocado(cor);
                  if (pontos > 31) {
                    *_spi_mosi_dlen = 511;
                    for (int i = 0; i < 16; i++) _spi_w[i] = cor32;
                    while (pontos > 31) { while ((*_spi_cmd) & SPI_USR); *_spi_cmd = SPI_USR; pontos -= 32; }
                    while ((*_spi_cmd) & SPI_USR);
                  }
                  if (pontos) {
                    *_spi_mosi_dlen = (pontos << 4) - 1;
                    for (int i = 0; i < 16; i++) _spi_w[i] = cor32;
                    *_spi_cmd = SPI_USR;
                    while ((*_spi_cmd) & SPI_USR);
                  }
                }
                void setup() {
                  Serial.begin(115200);
                  pinMode(TFT_CS, OUTPUT); pinMode(TFT_DC, OUTPUT); digitalWrite(TFT_CS, HIGH);
                  SPI.begin(18, 19, 23, -1);
                  SPI.beginTransaction(SPISettings(40000000, MSBFIRST, SPI_MODE0));
                  *_spi_user = SPI_USR_MOSI;
                  CS_L;
                  cmd(0x01); cmd(0x11); cmd(0x3A); tft_Write_8(0x55); cmd(0x36); tft_Write_8(0x48); cmd(0x29);
                  janela(0, 0, 239, 319);
                  Serial.println("inicio");
                  unsigned long t0 = millis();
                  bloco(0x07E0, 240UL * 320);
                  unsigned long levou = millis() - t0;
                  // Um ponto pelas macros do soc.h (o WRITE_PERI_REG do pushPixels).
                  janela(10, 20, 10, 20);
                  WRITE_PERI_REG(SPI_MOSI_DLEN_REG(SPI_PORT), 15);
                  WRITE_PERI_REG(SPI_W0_REG(SPI_PORT), 0x00F8);
                  SET_PERI_REG_MASK(SPI_CMD_REG(SPI_PORT), SPI_USR);
                  while (READ_PERI_REG(SPI_CMD_REG(SPI_PORT)) & SPI_USR);
                  // Uma linha de 32 pontos copiada de uma vez para os W0..W15 (o acesso largo).
                  static uint8_t linha[64];
                  for (int i = 0; i < 32; i++) { linha[2 * i] = 0x00; linha[2 * i + 1] = 0x1F; }
                  janela(0, 100, 31, 100);
                  memcpy((void *)_spi_w, linha, 64);
                  *_spi_mosi_dlen = 511;
                  *_spi_cmd = SPI_USR;
                  while (*_spi_cmd & SPI_USR);
                  CS_H;
                  SPI.endTransaction();
                  Serial.print("pronto em "); Serial.println(levou);
                }
                void loop() { delay(10); }
                """;
            var relogio = System.Diagnostics.Stopwatch.StartNew();
            long levou = -1, comecou = -1;
            var (serial, erro, nativo, m) = RodarNativo(sketch, 60000, antes: (_, maq) =>
                maq.AparelhosSpi = () => [new AparelhoNoSpi("ili9341", 15, 2, -1, 23, 19, 18)],
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
            int verdes = t?.Pixels.Count(c => c == Verde) ?? -1, azuis = t?.Pixels.Count(c => c == Azul) ?? -1;
            saida.AppendLine($"        a tela inteira pelos registradores do SPI: {levou} ms de relogio | {serial}");
            Conferir("registradores: o SPI pelos ponteiros da TFT_eSPI (W0..W15, MOSI_DLEN, SPI_USR) desenha no ILI9341",
                nativo && erro is null && serial.Contains("pronto") && t is { LarguraFisica: 240, AlturaFisica: 320 }
                && verdes == 240 * 320 - 33 && azuis == 32 && t.Pixels[20 * 240 + 10] == Vermelho
                && t.Pixels[100 * 240] == Azul && t.Pixels[100 * 240 + 31] == Azul && t.Pixels[100 * 240 + 32] == Verde,
                $"{nativo} {erro} | {serial} | verdes={verdes} azuis={azuis} {Cor(t, 10, 20)} {Cor(t, 0, 100)} {Cor(t, 32, 100)}");
        }

        // ---- a S3: o SPI2 (o FSPI), com o SPI_UPDATE antes do disparo ----
        {
            const string sketch = """
                #include <SPI.h>
                #include "soc/spi_reg.h"
                #include "soc/gpio_struct.h"
                #define SPI_PORT 2
                volatile uint32_t *_spi_cmd = (volatile uint32_t *)(SPI_CMD_REG(SPI_PORT));
                volatile uint32_t *_spi_user = (volatile uint32_t *)(SPI_USER_REG(SPI_PORT));
                volatile uint32_t *_spi_mosi_dlen = (volatile uint32_t *)(SPI_MS_DLEN_REG(SPI_PORT));
                volatile uint32_t *_spi_w = (volatile uint32_t *)(SPI_W0_REG(SPI_PORT));
                const int TFT_CS = 10, TFT_DC = 4;
                #define DC_C GPIO.out_w1tc = (1 << TFT_DC)
                #define DC_D GPIO.out_w1ts = (1 << TFT_DC)
                #define TFT_WRITE_BITS(D, B) *_spi_mosi_dlen = B - 1; *_spi_w = D; *_spi_cmd = SPI_UPDATE; while (*_spi_cmd & SPI_UPDATE); *_spi_cmd = SPI_USR; while (*_spi_cmd & SPI_USR);
                uint32_t trocado(uint16_t c) { return (uint16_t)(c << 8 | c >> 8); }
                void cmd(uint8_t c) { DC_C; TFT_WRITE_BITS(c, 8); DC_D; }
                void setup() {
                  Serial.begin(115200);
                  pinMode(TFT_CS, OUTPUT); pinMode(TFT_DC, OUTPUT);
                  SPI.begin(12, 13, 11, -1);
                  SPI.beginTransaction(SPISettings(40000000, MSBFIRST, SPI_MODE0));
                  *_spi_user = SPI_USR_MOSI;
                  GPIO.out_w1tc = (1 << TFT_CS);
                  cmd(0x01); cmd(0x11); cmd(0x3A); TFT_WRITE_BITS(0x55, 8); cmd(0x36); TFT_WRITE_BITS(0x48, 8); cmd(0x29);
                  cmd(0x2A); TFT_WRITE_BITS(trocado(5) << 16 | trocado(2), 32);
                  cmd(0x2B); TFT_WRITE_BITS(trocado(7) << 16 | trocado(7), 32);
                  cmd(0x2C);
                  _spi_w[1] = trocado(0x001F) << 16 | trocado(0x001F);
                  TFT_WRITE_BITS(trocado(0xF800) << 16 | trocado(0xF800), 64);
                  GPIO.out_w1ts = (1 << TFT_CS);
                  SPI.endTransaction();
                  Serial.println("pronto");
                }
                void loop() { delay(10); }
                """;
            var (serial, erro, nativo, m) = RodarNativo(sketch, 20000, placa: "esp32-s3-devkit", antes: (_, maq) =>
                maq.AparelhosSpi = () => [new AparelhoNoSpi("ili9341", 10, 4, -1, 11, 13, 12)], durante: PararNoPronto);
            var t = m.Tela;
            Conferir("registradores: na S3 o SPI2 pelos ponteiros (MS_DLEN, SPI_UPDATE e SPI_USR) desenha no ILI9341",
                nativo && erro is null && serial == "pronto" && t is { LarguraFisica: 240 }
                && t.Pixels[7 * 240 + 2] == Vermelho && t.Pixels[7 * 240 + 3] == Vermelho
                && t.Pixels[7 * 240 + 4] == Azul && t.Pixels[7 * 240 + 5] == Azul,
                $"{nativo} {erro} | {serial} | {Cor(t, 2, 7)} {Cor(t, 3, 7)} {Cor(t, 4, 7)} {Cor(t, 5, 7)}");
        }

        // ---- as outras: os mesmos registradores no endereco de cada chip ----
        foreach (string placa in new[] { "esp32-s2-devkit", "esp32-c3-devkit", "esp32-c6-devkit", "esp32-h2-devkit" })
        {
            const string sketch = """
                #include <SPI.h>
                #include "soc/spi_reg.h"
                #include "soc/gpio_reg.h"
                void setup() {
                  Serial.begin(115200);
                  pinMode(4, OUTPUT);
                  *(volatile uint32_t *)GPIO_OUT_W1TS_REG = 1UL << 4;
                  Serial.print(digitalRead(4));
                  volatile uint32_t *w = (volatile uint32_t *)(SPI_W0_REG(2));
                  w[3] = 0x12345678;
                  Serial.print(" "); Serial.print(w[3], HEX);
                  volatile uint32_t *disparo = (volatile uint32_t *)(SPI_CMD_REG(2));
                  *disparo = SPI_USR;
                  Serial.print(" "); Serial.println(*disparo & SPI_USR ? "preso" : "livre");
                  Serial.println("pronto");
                }
                void loop() { delay(10); }
                """;
            var (serial, erro, nativo, m) = RodarNativo(sketch, 20000, placa: placa, durante: PararNoPronto);
            Conferir($"registradores: na {placa} o GPIO e o SPI2 respondem no endereco do chip",
                nativo && erro is null && serial == "1 12345678 livre|pronto" && m.Digital[4] == 1, $"{nativo} {erro} | {serial}");
        }
    }
}
