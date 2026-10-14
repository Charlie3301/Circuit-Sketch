using System.IO;

namespace Simulador.App;

/// <summary>
/// A AccelStepper de verdade (a da IDE, GPL, nao vem com o simulador) num
/// A4988 com o NEMA 17: aceleracao, desaceleracao e a posicao no fim. So
/// roda quando a biblioteca existe -- na IDE de quem roda o teste, ou na
/// pasta da variavel CS_ACCELSTEPPER --, como o DOOM com o WAD.
/// </summary>
public static partial class Autoteste
{
    internal static string AccelStepperParaTeste()
    {
        string doAmbiente = Environment.GetEnvironmentVariable("CS_ACCELSTEPPER");
        if (!string.IsNullOrEmpty(doAmbiente) && Directory.Exists(doAmbiente)) return doAmbiente;
        string daIde = Acervo.ProcurarPasta("AccelStepper.h");
        return string.IsNullOrEmpty(daIde) ? null : daIde;
    }

    static void Passo_AccelStepper()
    {
        saida.AppendLine("AccelStepper real num A4988");
        string pasta = AccelStepperParaTeste();
        if (pasta is null)
        {
            saida.AppendLine("        (a AccelStepper nao esta instalada aqui: pulado)");
            return;
        }
        try { Acervo.Instalar(pasta); }
        catch (Exception e) { saida.AppendLine("        (nao instalou: " + e.Message + ")"); }

        var c = new Circuito();
        var placa = new PecaNoCircuito { Id = "placa", Tipo = "esp32-devkit-v1", X = 0, Y = 0, Usb = true };
        var fonte = new PecaNoCircuito { Id = "fonte", Tipo = "bateria", X = 300, Y = 300, Volts = 12, MiliAmperes = 3000 };
        var driver = new PecaNoCircuito { Id = "driver", Tipo = "a4988", X = 300, Y = 0 };
        var motor = new PecaNoCircuito { Id = "motor", Tipo = "stepper-motor", X = 600, Y = 0 };
        c.Pecas.AddRange([placa, fonte, driver, motor]);
        void Ligar(PecaNoCircuito a, string pa, PecaNoCircuito b, string pb)
            => c.Fios.Add(new Fio { DePeca = a.Id, DePino = pa, ParaPeca = b.Id, ParaPino = pb });
        Ligar(fonte, "+", driver, "VMOT");
        Ligar(fonte, "-", driver, "GND");
        Ligar(placa, "GND.2", driver, "GND.2");
        Ligar(placa, "3V3", driver, "VDD");
        Ligar(placa, "D13", driver, "STEP");
        Ligar(placa, "D12", driver, "DIR");
        Ligar(driver, "RST", driver, "SLP");
        Ligar(driver, "1A", motor, "A+");
        Ligar(driver, "1B", motor, "A-");
        Ligar(driver, "2A", motor, "B+");
        Ligar(driver, "2B", motor, "B-");

        var janela = new MainWindow();
        try
        {
            janela.TesteAbrir(c, """
                #include <AccelStepper.h>

                AccelStepper motor(AccelStepper::DRIVER, 13, 12);
                unsigned long inicio, voltas = 0, maiorBuraco = 0, anterior = 0;

                void setup() {
                  Serial.begin(115200);
                  motor.setMaxSpeed(1000);
                  motor.setAcceleration(2000);
                  motor.moveTo(400);
                  inicio = millis();
                }

                void loop() {
                  unsigned long t = micros();
                  if (anterior != 0 && t - anterior > maiorBuraco) maiorBuraco = t - anterior;
                  anterior = t;
                  voltas++;
                  motor.run();
                  static bool avisou = false;
                  if (!avisou && motor.distanceToGo() == 0) {
                    avisou = true;
                    Serial.print("chegou ");
                    Serial.println(millis() - inicio);
                    Serial.print("voltas ");
                    Serial.println(voltas);
                    Serial.print("buraco ");
                    Serial.println(maiorBuraco);
                  }
                }
                """);
            janela.TesteRodar();
            var maquina = janela.TesteMaquinaDa("placa");
            EsperarPor(() => LinhasDoSerial(maquina).Any(l => l.StartsWith("buraco")), 8000);
            EsperarPor(() => false, 400);
            var linhas = LinhasDoSerial(maquina);
            long Numero(string prefixo)
                => linhas.FirstOrDefault(l => l.StartsWith(prefixo)) is { } l && long.TryParse(l.Split(' ').Last(), out long n) ? n : -1;
            long ms = Numero("chegou"), voltas = Numero("voltas"), buraco = Numero("buraco");
            double angulo = janela.TesteEditorCircuito.AnguloDoPassoParaTeste("motor");
            saida.AppendLine($"        moveTo(400) a 2000 passos/s2, maximo 1000/s: chegou em {ms} ms, eixo em {angulo:0.0} graus");
            saida.AppendLine($"        {voltas} voltas do loop() na rampa; a maior espera entre duas voltas foi {buraco / 1000.0:0.0} ms");
            Conferir("AccelStepper: 400 passos no A4988 dao 720 graus no NEMA 17", Math.Abs(angulo - 720) < 2, $"{angulo:0.0}");

            // A conta da propria AccelStepper (c0 = 0,676 * sqrt(2/a), o passo
            // seguinte pela equacao 13 do Austin, freando quando o que falta
            // cabe na frenagem), refeita fora com o loop() girando a cada 2 us
            // como na ESP32: o 400o passo cai em ~853 ms. Mais rapido que isso
            // e a rampa que nao existe; o simulador chega um pouco depois --
            // o loop() interpretado roda dezenas de vezes mais devagar que o
            // chip, e cada pausa do computador (a coleta de lixo do .NET, o
            // Windows dando a vez a outro programa) atrasa o passo que estava
            // para sair, como a AccelStepper atrasa num chip ocupado.
            Conferir("AccelStepper: a rampa tem o tempo da conta (853 ms na placa; o PC soma as pausas dele)",
                ms is >= 840 and <= 1300, $"{ms} ms");
        }
        finally
        {
            janela.TesteParar();
            try { janela.Close(); } catch (InvalidOperationException) { }
        }
    }
}
