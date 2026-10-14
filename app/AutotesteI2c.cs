namespace Simulador.App;

/// <summary>
/// Os dois barramentos I2C: o Wire1 e o TwoWire de cada core, as pecas
/// respondendo so no fio delas, e o begin() dos sensores dizendo a verdade.
/// </summary>
public static partial class Autoteste
{
    static void I2c_DoisBarramentos()
    {
        saida.AppendLine("i2c: dois barramentos");

        // ---- dois OLEDs 0x3C numa ESP32, um em cada barramento ----
        {
            var c = new Circuito();
            var placa = new PecaNoCircuito { Tipo = "esp32-devkit-v1", X = 0, Y = 0 };
            var um = new PecaNoCircuito { Tipo = "ssd1306-i2c", X = 400, Y = 0 };
            var dois = new PecaNoCircuito { Tipo = "ssd1306-i2c", X = 400, Y = 300 };
            c.Pecas.AddRange([placa, um, dois]);
            foreach (var (peca, sda, scl) in new[] { (um, "D21", "D22"), (dois, "D33", "D32") })
                foreach (var (pino, alvo) in new[] { ("SDA", sda), ("SCL", scl), ("VCC", "3V3"), ("GND", "GND.2") })
                    c.Fios.Add(new Fio { DePeca = peca.Id, DePino = pino, ParaPeca = placa.Id, ParaPino = alvo });

            var janela = new MainWindow();
            janela.TesteAbrir(c, """
                #include <Wire.h>
                #include <Adafruit_SSD1306.h>
                Adafruit_SSD1306 tela1(128, 64, &Wire, -1);
                Adafruit_SSD1306 tela2(128, 64, &Wire1, -1);
                void scan(TwoWire &bus, const char *nome) {
                  for (byte e = 1; e < 127; e++) {
                    bus.beginTransmission(e);
                    if (bus.endTransmission() == 0) { Serial.print(nome); Serial.print(" achou "); Serial.println(e, HEX); }
                  }
                }
                void setup() {
                  Serial.begin(115200);
                  Wire1.begin(33, 32);
                  tela1.begin(SSD1306_SWITCHCAPVCC, 0x3C);
                  tela2.begin(SSD1306_SWITCHCAPVCC, 0x3C);
                  tela1.clearDisplay();
                  tela1.fillRect(0, 0, 128, 32, SSD1306_WHITE);
                  tela1.display();
                  tela2.clearDisplay();
                  tela2.fillRect(0, 0, 16, 16, SSD1306_WHITE);
                  tela2.display();
                  scan(Wire, "Wire");
                  scan(Wire1, "Wire1");
                  Serial.println("fim");
                }
                void loop() {}
                """);
            janela.TesteRodar();
            var maq = janela.TesteMaquinaDa(placa.Id);
            EsperarPor(() => LinhasDoSerial(maq).Contains("fim")
                             && janela.TesteEditorCircuito.AcesosNaTelaParaTeste(um.Id) > 3000
                             && janela.TesteEditorCircuito.AcesosNaTelaParaTeste(dois.Id) > 100, 6000);
            int acesos1 = janela.TesteEditorCircuito.AcesosNaTelaParaTeste(um.Id);
            int acesos2 = janela.TesteEditorCircuito.AcesosNaTelaParaTeste(dois.Id);
            var linhas = LinhasDoSerial(maq);
            janela.TesteParar();

            saida.AppendLine($"        acesos: {acesos1} / {acesos2}; {string.Join(" | ", linhas)}");
            Conferir("i2c: dois OLEDs 0x3C, um no Wire e outro no Wire1, cada um com o seu desenho",
                acesos1 > 3000 && acesos2 is > 100 and < 1000, $"{acesos1} / {acesos2}");
            Conferir("i2c: o scanner acha um 0x3C em cada barramento",
                linhas.Contains("Wire achou 3C") && linhas.Contains("Wire1 achou 3C") && linhas.Count(l => l.Contains("achou")) == 2,
                string.Join(" | ", linhas));
        }

        // ---- a peca no fio errado nao responde, e o aviso diz onde ela esta ----
        {
            var c = new Circuito();
            var placa = new PecaNoCircuito { Tipo = "esp32-devkit-v1", X = 0, Y = 0 };
            var lcd = new PecaNoCircuito { Tipo = "lcd1602-i2c", X = 400, Y = 0 };
            var relogio = new PecaNoCircuito { Tipo = "ds1307", X = 400, Y = 300 };
            c.Pecas.AddRange([placa, lcd, relogio]);
            foreach (var (pino, alvo) in new[] { ("SDA", "D25"), ("SCL", "D26") })
                c.Fios.Add(new Fio { DePeca = lcd.Id, DePino = pino, ParaPeca = placa.Id, ParaPino = alvo });
            foreach (var (pino, alvo) in new[] { ("SDA", "D21"), ("SCL", "D22") })
                c.Fios.Add(new Fio { DePeca = relogio.Id, DePino = pino, ParaPeca = placa.Id, ParaPino = alvo });

            var janela = new MainWindow();
            janela.TesteAbrir(c, """
                #include <Wire.h>
                #include <RTClib.h>
                #include <Adafruit_MPU6050.h>
                RTC_DS1307 rtc;
                Adafruit_MPU6050 mpu;
                void setup() {
                  Serial.begin(115200);
                  Wire.begin();
                  Wire.beginTransmission(0x27);
                  Serial.print("lcd: ");
                  Serial.println(Wire.endTransmission());
                  Serial.print("rtc: ");
                  Serial.println(rtc.begin());
                  Serial.print("mpu: ");
                  Serial.println(mpu.begin());
                  Serial.print("mpu69: ");
                  Serial.println(mpu.begin(0x69));
                }
                void loop() {}
                """);
            janela.TesteRodar();
            var maq = janela.TesteMaquinaDa(placa.Id);
            EsperarPor(() => LinhasDoSerial(maq).Any(l => l.StartsWith("mpu69")), 4000);
            var linhas = LinhasDoSerial(maq);
            string avisos;
            lock (maq.Trava) avisos = string.Join(" | ", maq.Avisos);
            janela.TesteParar();

            saida.AppendLine($"        {string.Join(" | ", linhas)}");
            Conferir("i2c: o LCD nos pinos 25/26 nao responde no Wire dos 21/22 (endTransmission = 2)",
                linhas.Contains("lcd: 2"), string.Join(" | ", linhas));
            Conferir("i2c: o aviso diz em que pinos a peca esta",
                avisos.Contains("SDA 25 / SCL 26") && avisos.Contains("SDA 21 / SCL 22"), avisos);
            Conferir("i2c: rtc.begin() acha o DS1307 ligado; mpu.begin() sem o MPU6050 na tela devolve false",
                linhas.Contains("rtc: 1") && linhas.Contains("mpu69: 0"), string.Join(" | ", linhas));
            Conferir("i2c: o DS1307 responde no 0x68, mas nao passa no WHO_AM_I do MPU6050",
                linhas.Contains("mpu: 0"), string.Join(" | ", linhas));
        }

        // ---- cada core com o que ele tem ----
        {
            var (_, _, erroC3) = RodarComPinos("#include <Wire.h>\nvoid setup() { Wire1.begin(4, 5); }\nvoid loop() {}", "esp32-c3-devkit", 500);
            Conferir("i2c: a C3 tem um controlador so -- Wire1 nao existe", erroC3?.Contains("'Wire1' was not declared") == true, erroC3 ?? "rodou");

            var (_, _, erroUno) = RodarComPinos("#include <Wire.h>\nvoid setup() { Wire.begin(4, 5); }\nvoid loop() {}", "arduino-uno", 500);
            Conferir("i2c: Wire.begin(sda, scl) no Uno nao compila (os pinos sao fixos)", erroUno?.Contains("no matching function") == true, erroUno ?? "rodou");

            var (esp, serialEsp, erroEsp) = RodarComPinos("""
                #include <Wire.h>
                TwoWire I2Cdois = TwoWire(1);
                void setup() {
                  Serial.begin(115200);
                  Serial.println(Wire1.begin());
                  Serial.println(I2Cdois.begin(33, 32, 400000));
                  Serial.println(I2Cdois.getClock());
                  Wire.begin();
                  Wire.beginTransmission(0x40);
                  int n = 0;
                  for (int i = 0; i < 200; i++) n += Wire.write(i);
                  Serial.println(n);
                }
                void loop() {}
                """, "esp32-devkit-v1", 500);
            string s = string.Join(" | ", serialEsp);
            saida.AppendLine($"        esp32: {s} {erroEsp}");
            Conferir("i2c: o Wire1 da ESP32 sem pinos falha no begin(), como no core 3.3",
                erroEsp is null && serialEsp.FirstOrDefault() == "0", $"{s} {erroEsp}");
            Conferir("i2c: TwoWire(1) com begin(sda, scl, frequencia) e o mesmo Wire1, a 400 kHz",
                serialEsp.Count > 2 && serialEsp[1] == "1" && serialEsp[2] == "400000" && esp?.I2c1.Sda == 33, s);
            Conferir("i2c: o buffer da Wire na ESP32 e de 128 bytes", serialEsp.Count > 3 && serialEsp[3] == "128", s);

            var (_, serialUno, _) = RodarComPinos("""
                #include <Wire.h>
                void setup() {
                  Serial.begin(9600);
                  Wire.begin();
                  Wire.beginTransmission(0x40);
                  int n = 0;
                  for (int i = 0; i < 200; i++) n += Wire.write(i);
                  Serial.println(n);
                }
                void loop() {}
                """, "arduino-uno", 500);
            Conferir("i2c: no Uno o buffer e de 32", serialUno.FirstOrDefault() == "32", string.Join(" | ", serialUno));

            var (pico, serialPico, erroPico) = RodarComPinos("""
                #include <Wire.h>
                void setup() {
                  Serial.begin(115200);
                  Wire1.begin();
                  Serial.println("ok");
                  Wire.setSDA(3);
                }
                void loop() {}
                """, "pi-pico", 500);
            Conferir("i2c: o Wire1 da Pico nasce no GP26/GP27",
                pico?.I2c1.Sda == 26 && pico.I2c1.Scl == 27 && serialPico.Contains("ok"), $"{pico?.I2c1.Sda}/{pico?.I2c1.Scl}");
            Conferir("i2c: setSDA num pino que o i2c0 nao tem para a Pico, como o core",
                erroPico?.Contains("illegal pin 3") == true, erroPico ?? "rodou");

            // O begin() do OLED sem endereco: 0x3D no de 64 linhas (o da biblioteca).
            var (oled, _, erroOled) = RodarComPinos("""
                #include <Adafruit_SSD1306.h>
                Adafruit_SSD1306 tela(128, 64, &Wire, -1);
                void setup() { tela.begin(SSD1306_SWITCHCAPVCC); }
                void loop() {}
                """, "esp32-devkit-v1", 500);
            int enderecoDoOled;
            lock (oled.PaineisOled) enderecoDoOled = oled.PaineisOled.Count > 0 ? oled.PaineisOled[0].Endereco : -1;
            Conferir("i2c: o OLED de 128x64 sem endereco no begin() fala no 0x3D, como a Adafruit",
                erroOled is null && enderecoDoOled == 0x3D, erroOled ?? $"0x{enderecoDoOled:X2}");
        }
    }
}
