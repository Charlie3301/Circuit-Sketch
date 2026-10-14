namespace Simulador.App;

/// <summary>
/// O OLED SSD1306 como a Adafruit_SSD1306: buffer na RAM, painel que so muda
/// no display(), o logotipo do begin(), as cores BLACK/WHITE/INVERSE, o
/// tempo do I2C -- e a tela de verdade na peca do circuito.
/// </summary>
public static partial class Autoteste
{
    static int AcesosNoPainel(Maquina maq)
        => maq.PainelOled is { } p ? p.Pixels.Count(x => x == p.CorAcesa) : -1;

    static void Oled_ComoNaBiblioteca()
    {
        saida.AppendLine("OLED SSD1306");

        // Cada rodada para num ponto do exemplo: o painel e o que sobra no fim.
        string Ate(string passos) => $$"""
            #include <Wire.h>
            #include <Adafruit_GFX.h>
            #include <Adafruit_SSD1306.h>
            Adafruit_SSD1306 display(128, 64, &Wire, -1);
            void setup() {
              Serial.begin(115200);
              if (!display.begin(SSD1306_SWITCHCAPVCC, 0x3C)) {
                Serial.println(F("SSD1306 allocation failed"));
                for (;;);
              }
              {{passos}}
            }
            void loop() {}
            """;
        int Acesos(string passos) => AcesosNoPainel(RodarComPinos(Ate(passos), "esp32-devkit-v1", 2_000).Maq);

        int antes = Acesos("");
        int logotipo = Acesos("display.display();");
        int semDisplay = Acesos("display.display(); display.clearDisplay(); display.drawPixel(10, 10, SSD1306_WHITE);");
        int umPonto = Acesos("display.display(); display.clearDisplay(); display.drawPixel(10, 10, SSD1306_WHITE); display.display();");
        int inverso = Acesos("display.clearDisplay(); display.drawPixel(10, 10, SSD1306_WHITE); "
                             + "display.fillRect(0, 0, 20, 20, SSD1306_INVERSE); display.display();");
        var (_, comTempo, _) = RodarComPinos(Ate("unsigned long t0 = micros(); display.display(); "
                                                 + "Serial.print(\"display us: \"); Serial.println(micros() - t0);"), "esp32-devkit-v1", 2_000);
        long us = NumeroDaLinha(comTempo, "display us:");
        saida.AppendLine($"        pontos acesos: antes do display {antes}, logotipo {logotipo}, desenhou sem display {semDisplay}, "
                         + $"um ponto {umPonto}, inverso {inverso}; display() {us} us");
        Conferir("oled: depois do begin() o vidro fica apagado ate o primeiro display()", antes == 0, antes.ToString());
        Conferir("oled: o primeiro display() mostra o logotipo da Adafruit que o begin() desenhou", logotipo > 300, logotipo.ToString());
        Conferir("oled: o que o sketch desenha so aparece no display() (antes, o vidro continua com o logotipo)",
            semDisplay == logotipo && umPonto == 1, $"{semDisplay} / {umPonto}");
        Conferir("oled: INVERSE troca os pontos (20x20 com um ja aceso: 399 acesos)", inverso == 399, inverso.ToString());
        Conferir("oled: o display() leva o tempo do I2C a 400 kHz (~25 ms)", us is > 20_000 and < 30_000, us.ToString());

        // ---- no Uno sem RAM, o malloc do begin() falha ----
        const string apertado = """
            #include <Wire.h>
            #include <Adafruit_SSD1306.h>
            Adafruit_SSD1306 display(128, 64, &Wire, -1);
            char leituras[900];
            void setup() {
              Serial.begin(9600);
              leituras[0] = 1;
              if (!display.begin(SSD1306_SWITCHCAPVCC, 0x3C)) {
                Serial.println(F("SSD1306 allocation failed"));
                for (;;) delay(100);
              }
              Serial.println("ok");
            }
            void loop() {}
            """;
        var (_, uno, _) = RodarComPinos(apertado, "arduino-uno", 1_000);
        Conferir("oled no uno: com 900 bytes de globais, o malloc de 1 KB do begin() falha (\"SSD1306 allocation failed\")",
            uno.Contains("SSD1306 allocation failed"), string.Join("|", uno));

        // ---- no app: a tela aparece na peca ----
        {
            var c = new Circuito();
            var placa = new PecaNoCircuito { Tipo = "esp32-devkit-v1", X = 0, Y = 0 };
            var oled = new PecaNoCircuito { Tipo = "ssd1306-i2c", X = 400, Y = 0 };
            c.Pecas.AddRange([placa, oled]);
            foreach (var (pino, alvo) in new[] { ("SDA", "D21"), ("SCL", "D22"), ("VCC", "3V3"), ("GND", "GND.2") })
                c.Fios.Add(new Fio { DePeca = oled.Id, DePino = pino, ParaPeca = placa.Id, ParaPino = alvo });
            var janela = new MainWindow();
            janela.TesteAbrir(c, """
                #include <Adafruit_SSD1306.h>
                Adafruit_SSD1306 display(128, 64, &Wire, -1);
                void setup() {
                  display.begin(SSD1306_SWITCHCAPVCC, 0x3C);
                  display.clearDisplay();
                  display.setTextSize(2);
                  display.setTextColor(SSD1306_WHITE);
                  display.setCursor(0, 0);
                  display.println("CircuitSketch");
                  display.fillCircle(64, 48, 12, SSD1306_WHITE);
                  display.display();
                }
                void loop() {}
                """);
            janela.TesteRodar();
            bool apareceu = EsperarPor(() => janela.TesteEditorCircuito.AcesosNaTelaParaTeste(oled.Id) > 300, 5000);
            int acesos = janela.TesteEditorCircuito.AcesosNaTelaParaTeste(oled.Id);
            janela.TesteParar();
            Conferir("oled no app: o desenho do sketch aparece na peca do circuito (antes, a tela da peca nunca mudava)",
                apareceu, acesos.ToString());
        }
    }
}
