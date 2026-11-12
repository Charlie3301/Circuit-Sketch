namespace Simulador.App;

/// <summary>
/// A serial entre placas: o TX de uma no RX da outra, no ritmo do baud --
/// Serial2 da ESP32, SoftwareSerial do Uno, baud trocado virando lixo.
/// </summary>
public static partial class Autoteste
{
    static void Serial_EntrePlacas()
    {
        saida.AppendLine("serial entre placas");

        (string A, string B) Rodar(string tipoA, string sketchA, string tipoB, string sketchB,
                                   (string DeA, string ParaB)[] fios, Func<List<string>, List<string>, bool> pronto)
        {
            var c = new Circuito();
            var a = new PecaNoCircuito { Tipo = tipoA, X = 0, Y = 0, Sketch = sketchA };
            var b = new PecaNoCircuito { Tipo = tipoB, X = 0, Y = 600, Sketch = sketchB };
            c.Pecas.AddRange([a, b]);
            foreach (var (deA, paraB) in fios)
                c.Fios.Add(new Fio { DePeca = a.Id, DePino = deA, ParaPeca = b.Id, ParaPino = paraB });
            var janela = new MainWindow();
            janela.TesteAbrir(c, null);
            janela.TesteRodar();
            var ma = janela.TesteMaquinaDa(a.Id);
            var mb = janela.TesteMaquinaDa(b.Id);
            EsperarPor(() => pronto(LinhasDoSerial(ma), LinhasDoSerial(mb)), 6000);
            string sa = string.Join(" | ", LinhasDoSerial(ma)), sb = string.Join(" | ", LinhasDoSerial(mb));
            janela.TesteParar();
            return (sa, sb);
        }

        const string emissor = """
            int n = 0;
            void setup() {
              Serial.begin(115200);
              Serial2.begin(9600);
              delay(300);   // a outra placa abre a Serial2 antes do primeiro ping
            }
            void loop() {
              Serial2.print("ping ");
              Serial2.println(n++);
              delay(200);
              while (Serial2.available()) {
                String r = Serial2.readStringUntil('\n');
                Serial.print("voltou: ");
                Serial.println(r);
              }
            }
            """;
        string Eco(string baud) => $$"""
            void setup() {
              Serial.begin(115200);
              Serial2.begin({{baud}});
            }
            void loop() {
              if (Serial2.available()) {
                String s = Serial2.readStringUntil('\n');
                s.trim();
                Serial.print("recebi: ");
                Serial.println(s);
                Serial2.print("eco ");
                Serial2.println(s);
              }
            }
            """;

        // Duas ESP32: o TX2 (GPIO 25) de cada uma no RX2 (GPIO 4) da outra.
        var (a, b) = Rodar("esp32-devkit-v1", emissor, "esp32-devkit-v1", Eco("9600"),
            [("D25", "D4"), ("D4", "D25")],
            (la, lb) => la.Count(l => l.StartsWith("voltou: eco ping")) >= 2);
        saida.AppendLine($"        A: {a}");
        saida.AppendLine($"        B: {b}");
        Conferir("serial: o Serial2 de uma ESP32 chega no Serial2 da outra (TX2 no GPIO 25, RX2 no 4, os do core 3.3)",
            b.Contains("recebi: ping 0") && b.Contains("recebi: ping 1"), b);
        Conferir("serial: o eco volta pelo outro fio, e o readStringUntil() espera a linha inteira chegar",
            a.Contains("voltou: eco ping 0") && a.Contains("voltou: eco ping 1"), a);

        // Baud trocado: quem ouve a 115200 o que sai a 9600 recebe lixo -- byte a
        // byte (o readStringUntil esperaria um fim de linha que nunca vem, como na placa).
        const string ouvinteRapido = """
            void setup() {
              Serial.begin(115200);
              Serial2.begin(115200);
            }
            void loop() {
              while (Serial2.available()) {
                Serial.print("byte: ");
                Serial.println(Serial2.read());
              }
            }
            """;
        var (_, lixo) = Rodar("esp32-devkit-v1", emissor, "esp32-devkit-v1", ouvinteRapido,
            [("D25", "D4"), ("D4", "D25")],
            (_, lb) => lb.Count(l => l.StartsWith("byte: ")) >= 8);
        var recebidos = lixo.Split(" | ").Where(l => l.StartsWith("byte: ")).Select(l => (char)int.Parse(l[6..])).ToArray();
        Conferir("serial: com o baud diferente dos dois lados, o que chega e lixo (como na bancada)",
            recebidos.Length >= 8 && !new string(recebidos).Contains("ping"), lixo.Length > 300 ? lixo[..300] : lixo);

        // O Uno com SoftwareSerial (RX 10, TX 11) numa ESP32.
        const string unoComSoftSerial = """
            #include <SoftwareSerial.h>
            SoftwareSerial esp(10, 11);
            int n = 0;
            void setup() {
              Serial.begin(9600);
              esp.begin(9600);
            }
            void loop() {
              esp.print("ping ");
              esp.println(n++);
              delay(300);
              while (esp.available()) {
                String r = esp.readStringUntil('\n');
                Serial.print("voltou: ");
                Serial.println(r);
              }
            }
            """;
        var (uno, esp) = Rodar("arduino-uno", unoComSoftSerial, "esp32-devkit-v1", Eco("9600"),
            [("11", "D4"), ("10", "D25")],
            (lu, _) => lu.Any(l => l.StartsWith("voltou: eco ping")));
        saida.AppendLine($"        uno: {uno}");
        // Qualquer "ping N" inteiro vale: o Uno sai do reset em 65 ms e a ESP32
        // em 300, e o primeiro ping pode sair antes de o Serial2.begin() dela
        // abrir a porta -- na bancada ele tambem se perde (ou chega so o fim).
        Conferir("serial: a SoftwareSerial do Uno conversa com o Serial2 da ESP32",
            System.Text.RegularExpressions.Regex.IsMatch(esp, @"recebi: ping \d+") && uno.Contains("voltou: eco ping"),
            $"{uno} // {esp}");

        // O Uno nao tem Serial2: o compilador recusa.
        var (_, _, erroUno) = RodarComPinos("void setup() { Serial2.begin(9600); }\nvoid loop() {}", "arduino-uno", 500);
        Conferir("serial: Serial2 no Uno da o erro do compilador e sugere a SoftwareSerial",
            erroUno?.Contains("was not declared") == true && erroUno.Contains("SoftwareSerial"), erroUno ?? "rodou");
    }
}
