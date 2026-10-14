namespace Simulador.App;

/// <summary>
/// O motor de passo com a Stepper.h: a sequencia de duas bobinas, o tempo
/// do setSpeed() e o step() que bloqueia -- e o eixo que segue o campo das
/// bobinas, que so treme quando a ordem dos pinos esta errada.
/// </summary>
public static partial class Autoteste
{
    static void Passo_ComoAStepper()
    {
        saida.AppendLine("motor de passo (Stepper.h)");

        // O eixo do motor depois do sketch rodar, e o que o serial disse.
        (double Angulo, string Serial) Girar(string pinos, string passos)
        {
            var c = new Circuito();
            var placa = new PecaNoCircuito { Tipo = "esp32-devkit-v1", X = 0, Y = 0 };
            var motor = new PecaNoCircuito { Tipo = "stepper-motor", X = 500, Y = 0 };
            c.Pecas.AddRange([placa, motor]);
            string[] bobinas = ["A+", "B+", "A-", "B-"];
            int[] gpios = [14, 27, 26, 25];
            for (int k = 0; k < 4; k++)
                c.Fios.Add(new Fio { DePeca = placa.Id, DePino = $"D{gpios[k]}", ParaPeca = motor.Id, ParaPino = bobinas[k] });

            var janela = new MainWindow();
            janela.TesteAbrir(c, $$"""
                #include <Stepper.h>
                Stepper motor(200, {{pinos}});
                void setup() {
                  Serial.begin(115200);
                  motor.setSpeed(60);
                  delay(300);
                  unsigned long t0 = millis();
                  motor.step({{passos}});
                  Serial.print("ms: ");
                  Serial.println(millis() - t0);
                }
                void loop() {}
                """);
            janela.TesteRodar();
            var maquina = janela.TesteMaquinaDa(placa.Id);
            EsperarPor(() => LinhasDoSerial(maquina).Any(l => l.StartsWith("ms: ")), 6000);
            EsperarPor(() => false, 300);
            double angulo = janela.TesteEditorCircuito.MovimentoParaTeste(motor.Id).Angulo;
            string serial = string.Join(" | ", LinhasDoSerial(maquina));
            janela.TesteParar();
            return (angulo, serial);
        }

        // A ordem que o exemplo usa no 28BYJ-48 com o ULN2003 (IN1, IN3, IN2, IN4):
        // as duas pontas de cada bobina juntas.
        // O NEMA 17 do Wokwi: 200 passos por volta (1,8 grau por passo).
        var (frente, serialFrente) = Girar("14, 26, 27, 25", "200");
        long ms = NumeroDaLinha(serialFrente.Split(" | ").ToList(), "ms:");
        saida.AppendLine($"        200 passos: eixo em {frente:0.0} graus, step() levou {ms} ms");
        // O primeiro passo so alinha o rotor, que estava sem campo -- na
        // bancada tambem: o eixo anda 63 passos, e nao 64.
        Conferir("stepper: 200 passos na ordem certa dao uma volta do NEMA 17 (o primeiro so alinha o rotor)",
            Math.Abs(frente - 360) <= 9, $"{frente:0.0}");
        // O tempo, no relogio virtual (no real, o agendador do Windows soma alguns porcento).
        var (noVirtual, _, _, _) = RodarNaCpu("""
            #include <Stepper.h>
            Stepper motor(2048, 14, 26, 27, 25);
            void setup() {
              Serial.begin(115200);
              motor.setSpeed(10);
              unsigned long t0 = micros();
              motor.step(64);
              Serial.print("us: ");
              Serial.println(micros() - t0);
            }
            void loop() {}
            """, "esp32-devkit-v1", 5_000);
        long us = NumeroDaLinha(noVirtual, "us:");
        saida.AppendLine($"        no relogio virtual: step(64) em {us} us (no tempo real do app: {ms} ms)");
        Conferir("stepper: setSpeed(10) com 2048 passos por volta: 2929 us por passo, o step(64) bloqueia 187,5 ms",
            us is >= 187_400 and <= 188_000, us.ToString());

        var (tras, _) = Girar("14, 26, 27, 25", "-100");
        Conferir("stepper: step(-100) gira para tras (meia volta)", Math.Abs(tras + 180) <= 9, $"{tras:0.0}");

        var (errado, _) = Girar("14, 27, 26, 25", "64");
        saida.AppendLine($"        ordem de pinos errada: eixo em {errado:0.0} graus");
        Conferir("stepper: com a ordem dos pinos errada o motor so treme -- o campo vira do avesso a cada passo, e o eixo nao sai do lugar",
            Math.Abs(errado) < 12, $"{errado:0.0}");

        // No ESP8266 o step() comprido nao cede o processador: o soft WDT derruba a placa.
        var (_, reiniciou, avisos, msDoWdt) = RodarNaCpu("""
            #include <Stepper.h>
            Stepper motor(2048, 5, 4, 0, 2);
            void setup() {
              motor.setSpeed(10);
              motor.step(2048);
            }
            void loop() {}
            """, "esp8266-nodemcu", 10_000);
        Conferir("stepper no esp8266: um step(2048) a 10 rpm (6 s sem ceder) derruba a placa pelo soft WDT, como na bancada",
            reiniciou && msDoWdt is >= 3_000 and < 4_500, $"{reiniciou} {msDoWdt} ms / {avisos}");
    }
}
