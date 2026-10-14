namespace Simulador.App;

/// <summary>
/// A CPU simulada: o relogio da placa anda com a conta, no ritmo do chip de
/// cada uma; a serial sai no ritmo do baud; e os watchdogs sao os de cada
/// chip -- no lugar do antigo "loop preso", que nao existe em placa nenhuma.
/// </summary>
public static partial class Autoteste
{
    /// <summary>
    /// Roda o setup() no tempo virtual, com um teto de relogio (e sem o limite
    /// de passos, que e o que a CPU simulada substitui). Devolve as linhas do
    /// serial, se a placa reiniciou, os avisos e o relogio no fim.
    /// </summary>
    static (List<string> Serial, bool Reiniciou, string Avisos, long Ms) RodarNaCpu(
        string sketch, string placa, long tetoMs, bool comLoop = false)
    {
        var prog = Compilador.CompilarProjeto(new Projeto { Sketch = sketch, Placa = placa, DoSimulador = Acervo.EhDoSimulador });
        var maq = new Maquina { Perfil = PerfilPlaca.De(placa) };
        maq.Reiniciar();
        maq.LimiteDoRelogioVirtual = tetoMs;
        var interp = new Interpretador(prog, maq) { LimitePassos = long.MaxValue };
        bool reiniciou = false;
        try
        {
            interp.Preparar();
            interp.Setup();
            while (comLoop && !maq.Parar) interp.Loop();
        }
        catch (ReinicioDaPlaca) { reiniciou = true; }
        catch (ExecucaoInterrompida) { }
        var linhas = maq.Serial.Select(l => l.Contains("] ") ? l[(l.IndexOf("] ") + 2)..] : l).ToList();
        return (linhas, reiniciou, string.Join(" / ", maq.Avisos), maq.Ms);
    }

    static long NumeroDaLinha(List<string> linhas, string prefixo)
    {
        var l = linhas.FirstOrDefault(x => x.StartsWith(prefixo, StringComparison.Ordinal));
        return l is not null && long.TryParse(l[prefixo.Length..].Trim().Split(' ')[0], out long n) ? n : -1;
    }

    static void Motor_CpuSimulada()
    {
        saida.AppendLine("a CPU simulada");

        // ---- o custo da conta, em cada chip ----
        const string contas = """
            volatile float acumulado = 1.0;
            void setup() {
              Serial.begin(115200);
              unsigned long t0 = micros();
              for (long i = 0; i < 20000; i++) acumulado = acumulado * 1.0001f;
              Serial.print("float: "); Serial.println(micros() - t0);
              t0 = micros();
              long primos = 0;
              for (long n = 2; n < 3000; n++) {
                bool primo = true;
                for (long d = 2; d * d <= n; d++) if (n % d == 0) { primo = false; break; }
                if (primo) primos++;
              }
              Serial.print("primos: "); Serial.println(micros() - t0);
            }
            void loop() {}
            """;
        var (uno, _, _, _) = RodarNaCpu(contas, "arduino-uno", 60_000);
        var (esp, _, _, _) = RodarNaCpu(contas, "esp32-devkit-v1", 60_000);
        var (c3, _, _, _) = RodarNaCpu(contas, "esp32-c3-devkit", 60_000);
        long fUno = NumeroDaLinha(uno, "float:"), fEsp = NumeroDaLinha(esp, "float:"), fC3 = NumeroDaLinha(c3, "float:");
        long pUno = NumeroDaLinha(uno, "primos:"), pEsp = NumeroDaLinha(esp, "primos:");
        saida.AppendLine($"        20000 float*: uno {fUno} us, esp32 {fEsp} us, c3 {fC3} us; primos ate 3000: uno {pUno} us, esp32 {pEsp} us");

        // O Uno faz o float em software (138 ciclos por multiplicacao a 16
        // MHz): perto de 200 ms. A ESP32 tem FPU: ~1 ms. A C3 nao tem FPU.
        Conferir("uno: 20000 multiplicacoes de float levam o tempo do AVR (100 a 400 ms)",
            fUno is > 100_000 and < 400_000, fUno.ToString());
        Conferir("esp32: com FPU, a mesma conta leva ~1 ms (0,3 a 4 ms)", fEsp is > 300 and < 4_000, fEsp.ToString());
        Conferir("esp32-c3: sem FPU, bem mais que a ESP32 e bem menos que o Uno",
            fC3 > fEsp * 3 && fC3 < fUno / 5, $"{fC3}");
        Conferir("uno: a divisao de 32 bits em software faz os primos levarem mais de 50x o tempo da ESP32",
            pUno > 50 * pEsp && pEsp > 0, $"{pUno} / {pEsp}");

        // ---- a serial no ritmo do baud ----
        const string serial = """
            void setup() {
              Serial.begin(9600);
              unsigned long t0 = millis();
              for (int i = 0; i < 20; i++) Serial.println("0123456789");
              unsigned long gasto = millis() - t0;
              Serial.flush();
              Serial.print("serial: "); Serial.println(gasto);
            }
            void loop() {}
            """;
        var (serialUno, _, _, _) = RodarNaCpu(serial, "arduino-uno", 60_000);
        long msSerial = NumeroDaLinha(serialUno, "serial:");
        // 20 linhas de 12 bytes = 240 bytes; 64 cabem na fila, os outros 176
        // saem a 1,04 ms cada (10 bits a 9600 baud): ~183 ms.
        saida.AppendLine($"        20 linhas a 9600 baud: {msSerial} ms");
        Conferir("serial a 9600 baud: com a fila de 64 bytes cheia, o print espera (~183 ms para 240 bytes)",
            msSerial is > 150 and < 220, msSerial.ToString());

        // ---- os watchdogs de cada chip ----
        const string preso = "void setup() { Serial.begin(115200); Serial.println(\"comecou\"); while (true) { delayMicroseconds(1000); } }\nvoid loop() {}\n";
        var (wdt8266, reiniciou8266, avisos8266, ms8266) = RodarNaCpu(preso, "esp8266-nodemcu", 20_000);
        Conferir("esp8266: laco sem ceder reinicia a placa pelo soft WDT (~3,2 s), com a mensagem do core",
            reiniciou8266 && wdt8266.Any(l => l.Contains("Soft WDT reset")) && ms8266 is >= 3_000 and < 4_000,
            $"{reiniciou8266} {ms8266} ms / {avisos8266}");

        var (_, reiniciouEsp, avisosEsp, msEsp) = RodarNaCpu(preso, "esp32-devkit-v1", 10_000);
        Conferir("esp32: o mesmo laco nao reinicia nada (o core so vigia o loop com enableLoopWDT)",
            !reiniciouEsp && msEsp >= 9_000, $"{reiniciouEsp} {msEsp} ms / {avisosEsp}");

        var (_, reiniciouUno, _, msUno) = RodarNaCpu(preso, "arduino-uno", 5_000);
        Conferir("uno: sem watchdog armado, o laco so gira (nada de erro de 'loop preso')",
            !reiniciouUno && msUno >= 4_500, $"{reiniciouUno} {msUno} ms");

        const string loopVigiado = """
            void setup() { Serial.begin(115200); enableLoopWDT(); }
            void loop() { unsigned long t = millis(); while (millis() - t < 6000) { delayMicroseconds(1000); } }
            """;
        var (twdt, reiniciouTwdt, _, msTwdt) = RodarNaCpu(loopVigiado, "esp32-s3-devkit", 20_000, comLoop: true);
        Conferir("esp32-s3: com enableLoopWDT(), um loop de 6 s dispara o task watchdog e a placa reinicia",
            reiniciouTwdt && twdt.Any(l => l.Contains("task_wdt: Task watchdog got triggered")) && msTwdt is >= 5_000 and < 6_000,
            $"{reiniciouTwdt} {msTwdt} ms");

        const string wdtAvr = """
            #include <avr/wdt.h>
            void setup() { Serial.begin(9600); wdt_enable(WDTO_2S); Serial.println("armado"); delay(5000); Serial.println("nunca"); }
            void loop() {}
            """;
        var (avr, reiniciouAvr, _, msAvr) = RodarNaCpu(wdtAvr, "arduino-uno", 20_000);
        Conferir("uno: wdt_enable(WDTO_2S) sem wdt_reset reinicia no meio do delay(5000), aos ~2 s",
            reiniciouAvr && !avr.Contains("nunca") && msAvr is >= 1_900 and < 2_200, $"{reiniciouAvr} {msAvr} ms");
    }
}
