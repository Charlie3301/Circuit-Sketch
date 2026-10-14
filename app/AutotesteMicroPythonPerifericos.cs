namespace Simulador.App;

/// <summary>
/// O MicroPython com os perifericos do C++: o OLED em pixels (a fonte do
/// framebuf), o I2C nos pinos de fabrica de cada porta, e a UART que conversa
/// com outra placa pelo fio.
/// </summary>
public static partial class Autoteste
{
    static void MicroPython_Perifericos()
    {
        saida.AppendLine("micropython: oled, i2c e uart");

        // ---- o OLED no I2C(0) da ESP32 (SCL 18, SDA 19), desenhado em pixels ----
        {
            var c = new Circuito();
            var placa = new PecaNoCircuito { Tipo = "esp32-devkit-v1", X = 0, Y = 0 };
            var oled = new PecaNoCircuito { Tipo = "ssd1306-i2c", X = 400, Y = 0 };
            c.Pecas.AddRange([placa, oled]);
            foreach (var (pino, alvo) in new[] { ("SDA", "D19"), ("SCL", "D18"), ("VCC", "3V3"), ("GND", "GND.2") })
                c.Fios.Add(new Fio { DePeca = oled.Id, DePino = pino, ParaPeca = placa.Id, ParaPino = alvo });

            var janela = new MainWindow();
            janela.TesteAbrir(c, """
                from machine import Pin, I2C
                import ssd1306
                i2c = I2C(0)
                print(i2c.scan())
                oled = ssd1306.SSD1306_I2C(128, 64, i2c)
                oled.fill(0)
                oled.text("Ola", 0, 0)
                oled.fill_rect(0, 20, 128, 30, 1)
                oled.show()
                print("pronto")
                """);
            janela.TesteRodar();
            var maq = janela.TesteMaquinaDa(placa.Id);
            EsperarPor(() => LinhasDoSerial(maq).Contains("pronto") && janela.TesteEditorCircuito.AcesosNaTelaParaTeste(oled.Id) > 3000, 5000);
            int acesos = janela.TesteEditorCircuito.AcesosNaTelaParaTeste(oled.Id);
            var linhas = LinhasDoSerial(maq);
            janela.TesteParar();

            saida.AppendLine($"        {string.Join(" | ", linhas)}; acesos {acesos}");
            Conferir("micropython: o scan() do I2C(0) acha o OLED nos pinos de fabrica (SCL 18, SDA 19)",
                linhas.FirstOrDefault() == "[60]", string.Join(" | ", linhas));
            Conferir("micropython: o OLED desenha em pixels na peca (antes era uma tela de texto)",
                acesos > 3000, acesos.ToString());
        }

        // ---- o OLED em outros pinos: o OSError da placa, com o aviso de onde ele esta ----
        {
            var c = new Circuito();
            var placa = new PecaNoCircuito { Tipo = "esp32-devkit-v1", X = 0, Y = 0 };
            var oled = new PecaNoCircuito { Tipo = "ssd1306-i2c", X = 400, Y = 0 };
            c.Pecas.AddRange([placa, oled]);
            foreach (var (pino, alvo) in new[] { ("SDA", "D21"), ("SCL", "D22") })
                c.Fios.Add(new Fio { DePeca = oled.Id, DePino = pino, ParaPeca = placa.Id, ParaPino = alvo });

            var janela = new MainWindow();
            janela.TesteAbrir(c, """
                from machine import I2C
                import ssd1306
                oled = ssd1306.SSD1306_I2C(128, 64, I2C(0))
                print("passou")
                """);
            janela.TesteRodar();
            EsperarPor(() => janela.TesteConsole.Contains("OSError"), 4000);
            string console = janela.TesteConsole;
            janela.TesteParar();
            Conferir("micropython: OLED nos pinos 21/22 com o I2C(0) nos 18/19 da o OSError ENODEV, e diz onde a peca esta",
                console.Contains("ENODEV") && console.Contains("SDA 21 / SCL 22"), console.Length > 300 ? console[^300..] : console);
        }

        // ---- a UART do MicroPython conversando com o Serial2 de uma ESP32 em C++ ----
        {
            var c = new Circuito();
            var py = new PecaNoCircuito { Tipo = "esp32-devkit-v1", X = 0, Y = 0, Sketch = """
                from machine import UART
                import time
                u = UART(2, baudrate=9600)
                print("inicio")
                ecos = 0
                n = 0
                while ecos < 2 and n < 60:
                    u.write("ping " + str(n) + "\n")
                    n = n + 1
                    time.sleep_ms(150)
                    r = u.readline()
                    if r:
                        print("voltou:", r.strip())
                        ecos = ecos + 1
                """ };
            var cpp = new PecaNoCircuito { Tipo = "esp32-devkit-v1", X = 0, Y = 600, Sketch = """
                void setup() {
                  Serial.begin(115200);
                  Serial2.begin(9600, SERIAL_8N1, 16, 17);
                  Serial.println("pronto");
                }
                void loop() {
                  if (Serial2.available()) {
                    String s = Serial2.readStringUntil('\n');
                    Serial.print("recebi: ");
                    Serial.println(s);
                    Serial2.print("eco ");
                    Serial2.println(s);
                  }
                }
                """ };
            c.Pecas.AddRange([py, cpp]);
            // Na DevKit V1 os GPIO 16 e 17 sao o RX2 e o TX2 da serigrafia.
            c.Fios.Add(new Fio { DePeca = py.Id, DePino = "TX2", ParaPeca = cpp.Id, ParaPino = "RX2" });
            c.Fios.Add(new Fio { DePeca = cpp.Id, DePino = "TX2", ParaPeca = py.Id, ParaPino = "RX2" });
            var janela = new MainWindow();
            janela.TesteAbrir(c, null);
            janela.TesteRodar();
            var mPy = janela.TesteMaquinaDa(py.Id);
            var mCpp = janela.TesteMaquinaDa(cpp.Id);
            EsperarPor(() => LinhasDoSerial(mPy).Count(l => l.StartsWith("voltou")) >= 2, 6000);
            string a = string.Join(" | ", LinhasDoSerial(mPy)), b = string.Join(" | ", LinhasDoSerial(mCpp));
            string consoleDaUart = janela.TesteConsole;
            janela.TesteParar();
            if (!a.Contains("voltou"))
            {
                saida.AppendLine($"        console: {(consoleDaUart.Length > 400 ? consoleDaUart[^400..] : consoleDaUart)}");
                lock (mPy.Portas) saida.AppendLine($"        portas py: {string.Join(", ", mPy.Portas.Select(x => $"{x.Nome} rx{x.Rx} tx{x.Tx} {x.Baud} {x.Ligada}"))}");
                lock (mCpp.Portas) saida.AppendLine($"        portas c++: {string.Join(", ", mCpp.Portas.Select(x => $"{x.Nome} rx{x.Rx} tx{x.Tx} {x.Baud} {x.Ligada}"))}");
            }
            saida.AppendLine($"        python: {a}");
            saida.AppendLine($"        c++: {b}");
            Conferir("micropython: a UART(2) (TX 17, RX 16) chega no Serial2 de outra placa",
                b.Contains("recebi: ping"), b);
            Conferir("micropython: o readline() pega o eco que voltou pelo fio",
                a.Contains("voltou: eco ping"), a);
        }

        // ---- a Pico confere os pinos do I2C, e o texto usa a fonte 8x8 ----
        {
            var (_, _, erroPico) = RodarComPinos("from machine import I2C, Pin\ni2c = I2C(0, sda=Pin(3), scl=Pin(5))\n", "pi-pico", 500);
            Conferir("micropython: na Pico, o I2C(0) no GP3 da 'bad SDA pin'", erroPico?.Contains("bad SDA pin") == true, erroPico ?? "rodou");

            var (maq, serial, erro) = RodarComPinos("""
                from machine import I2C
                import ssd1306
                oled = ssd1306.SSD1306_I2C(128, 32, I2C(0))
                oled.fill(0)
                oled.text("A", 0, 0)
                oled.show()
                n = 0
                for y in range(8):
                    for x in range(8):
                        n = n + oled.pixel(x, y)
                print(n)
                """, "esp32-devkit-v1", 1000);
            int bitsDoA = FonteDoFramebuf.Petme128.Skip(('A' - 32) * 8).Take(8).Sum(b => System.Numerics.BitOperations.PopCount(b));
            Conferir("micropython: o text() desenha com a fonte 8x8 do framebuf",
                erro is null && serial.FirstOrDefault() == bitsDoA.ToString(), $"{string.Join(" | ", serial)} (a fonte tem {bitsDoA}) {erro}");
        }
    }
}
