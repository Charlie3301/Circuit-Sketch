namespace Simulador.App;

/// <summary>
/// O C# compilado de verdade (Roslyn + SketchDotNet): a linguagem inteira, a
/// API do Arduino que o C# do simulador ja tinha, a do nanoFramework/.NET IoT
/// (GpioController, evento do pino, PWM, ADC), threads e Timer, e as guardas
/// (a parada num laco vazio, a excecao com a linha, a recursao sem fim).
/// </summary>
public static partial class Autoteste
{
    static void CSharp_Roslyn()
    {
        saida.AppendLine("C# compilado de verdade (Roslyn)");

        // ---- o C# antigo do simulador (a API do Arduino) ----
        const string arduino = """
            using System;

            public class Program
            {
                public static void Main()
                {
                    int led = 13;
                    PinMode(led, OUTPUT);
                    Console.WriteLine("C# acordou");
                    for (int i = 0; i < 3; i++)
                    {
                        DigitalWrite(led, HIGH);
                        Delay(20);
                        DigitalWrite(led, LOW);
                        Delay(20);
                    }
                    Serial.Println($"piscou {3} vezes, millis > 100: {Millis() > 100}");
                }
            }
            """;
        var (serial, erro, compilado, m) = RodarNativo(arduino, 1500, "arduino-uno");
        Conferir("C# (Roslyn): o programa com a API do Arduino (PinMode, DigitalWrite, Delay, Console, Serial)",
            compilado && erro is null && serial == "C# acordou|piscou 3 vezes, millis > 100: True" && m.Modo[13] == 1,
            $"{compilado} {erro} | {serial}");

        // ---- a linguagem inteira e o GpioController do nanoFramework ----
        const string nano = """
            using System;
            using System.Collections.Generic;
            using System.Device.Gpio;
            using System.Diagnostics;
            using System.Linq;
            using System.Threading;

            namespace Pisca
            {
                public record Leitura(string Nome, int Valor);

                public interface IForma { double Area { get; } }
                public sealed class Quadrado : IForma { public double Lado { get; init; } public double Area => Lado * Lado; }

                public static class Extensoes
                {
                    public static T Maior<T>(this IEnumerable<T> lista) where T : IComparable<T> => lista.Max();
                }

                public class Program
                {
                    static event Action<int> Mudou;

                    public static void Main()
                    {
                        var gpio = new GpioController();
                        GpioPin led = gpio.OpenPin(2, PinMode.Output);
                        for (int i = 0; i < 4; i++)
                        {
                            led.Toggle();
                            Thread.Sleep(15);
                        }

                        var leituras = new List<Leitura> { new("a", 3), new("b", 9), new("c", 5) };
                        var nomes = string.Join(",", leituras.Where(l => l.Valor > 4).OrderBy(l => l.Valor).Select(l => l.Nome));
                        int total = 0;
                        Mudou += v => total += v;
                        Mudou?.Invoke(7);
                        IForma f = new Quadrado { Lado = 3 };
                        var (x, y) = (leituras.Count, new[] { 4, 8, 6 }.Maior());
                        string tipo = f switch { Quadrado { Lado: > 2 } q => $"quadrado {q.Area}", _ => "outro" };
                        Debug.WriteLine($"{nomes} {total} {tipo} {x}{y} {led.Read()}");
                        Thread.Sleep(Timeout.Infinite);
                    }
                }
            }
            """;
        (serial, erro, compilado, m) = RodarNativo(nano, 600);
        Conferir("C# (Roslyn): record, interface, init, genericos com restricao, extensao, LINQ, evento, tupla, switch de padrao e o GpioPin do nanoFramework",
            compilado && erro is null && serial == "c,b 7 quadrado 9 38 Low" && m.Modo[2] == 1, $"{compilado} {erro} | {serial}");

        // ---- o evento do pino (a interrupcao) ----
        const string evento = """
            using System.Device.Gpio;
            using System.Diagnostics;
            using System.Threading;

            int subidas = 0, descidas = 0;
            var gpio = new GpioController();
            var botao = gpio.OpenPin(4, PinMode.InputPullUp);
            botao.ValueChanged += (s, e) =>
            {
                if (e.ChangeType == PinEventTypes.Rising) subidas++;
                else descidas++;
            };
            Thread.Sleep(900);
            Debug.WriteLine(subidas >= 2 && descidas >= 2 ? "o pino avisou" : $"{subidas} {descidas}");
            """;
        int nivel = 1, voltas = 0;
        (serial, erro, compilado, _) = RodarNativo(evento, 1600, durante: maq =>
        {
            if (++voltas % 8 != 0) return;
            nivel ^= 1;
            maq.NivelExterno[4] = nivel;
        });
        Conferir("C# (Roslyn): programa de nivel superior com o ValueChanged do GpioPin (a borda de fora chega como evento)",
            compilado && erro is null && serial.StartsWith("o pino avisou"), $"{compilado} {erro} | {serial}");

        // ---- threads, lock, Timer e async ----
        const string threads = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;

            class Program
            {
                static readonly object trava = new();
                static int contador;

                static async Task Main()
                {
                    var t1 = new Thread(() => { for (int i = 0; i < 1000; i++) lock (trava) contador++; });
                    var t2 = new Thread(Somar);
                    t1.Start();
                    t2.Start();
                    t1.Join();
                    t2.Join();
                    int tiques = 0;
                    using var timer = new Timer(_ => Interlocked.Increment(ref tiques), null, 0, 20);
                    await Task.Delay(210);
                    int r = await Task.Run(() => 6 * 7);
                    Console.WriteLine($"{contador} {(tiques >= 8 && tiques <= 13 ? "timer ok" : tiques.ToString())} {r}");
                }

                static void Somar() { for (int i = 0; i < 1000; i++) lock (trava) contador++; }
            }
            """;
        (serial, erro, compilado, _) = RodarNativo(threads, 900);
        Conferir("C# (Roslyn): duas threads com lock, o Timer de 20 ms, async Main com Task.Delay e Task.Run",
            compilado && erro is null && serial == "2000 timer ok 42", $"{compilado} {erro} | {serial}");

        // ---- as guardas: a parada, a excecao com a linha, a recursao ----
        const string preso = """
            using System;
            Console.WriteLine("entrou");
            long k = 0;
            while (true) { try { k++; } catch (Exception) { } }
            """;
        var relogio = System.Diagnostics.Stopwatch.StartNew();
        (serial, erro, compilado, _) = RodarNativo(preso, 300);
        Conferir("C# (Roslyn): Parar tira a placa de um while(true) sem chamada, mesmo com catch (Exception) dentro",
            compilado && erro is null && serial == "entrou" && relogio.ElapsedMilliseconds < 3000,
            $"{compilado} {erro} | {serial} | {relogio.ElapsedMilliseconds} ms");

        const string excecao = "using System;\n\nint[] v = new int[3];\nConsole.WriteLine(\"antes\");\nv[5] = 1;\n";
        (serial, erro, compilado, _) = RodarNativo(excecao, 300);
        Conferir("C# (Roslyn): a excecao sem catch vira o erro da placa, com o tipo",
            compilado && erro is not null && erro.Contains("IndexOutOfRangeException") && serial == "antes", $"{compilado} {erro} | {serial}");

        const string recursao = "using System;\nint F(int n) => F(n + 1) + 1;\nConsole.WriteLine(F(0));\n";
        (serial, erro, compilado, _) = RodarNativo(recursao, 2000);
        Conferir("C# (Roslyn): a recursao sem fim vira erro (\"a pilha acabou\"), sem derrubar o simulador",
            compilado && erro is not null && erro.Contains("pilha acabou"), $"{compilado} {erro} | {serial}");

        // ---- o erro de compilacao na linha certa ----
        const string errado = "using System;\n\nclass Program\n{\n    static void Main()\n    {\n        int x = \"texto\";\n    }\n}\n";
        (serial, erro, _, _) = RodarNativo(errado, 50);
        Conferir("C# (Roslyn): o erro do compilador vem com o codigo e a linha (CS0029, linha 7)",
            erro is not null && erro.Contains("CS0029"), erro ?? "(sem erro)");
        try
        {
            CompiladorCSharp.Compilar(new Projeto { Sketch = errado, Placa = "esp32-devkit-v1" });
            Conferir("C# (Roslyn): a linha do erro", false, "compilou");
        }
        catch (ErroDeSintaxe e) { Conferir("C# (Roslyn): a linha do erro e a 7", e.Linha == 7, e.Linha.ToString()); }

        // ---- PWM e ADC ----
        const string pwm = """
            using System.Device.Adc;
            using System.Device.Pwm;
            using System.Diagnostics;

            var pwm = PwmChannel.CreateFromPin(2, 5000, 0.25);
            pwm.Start();
            pwm.DutyCycle = 0.75;
            var adc = new AdcController();
            var canal = adc.OpenChannel(6);   // GPIO34
            int v = canal.ReadValue();
            Debug.WriteLine($"{adc.MaxValue} {(v >= 0 && v <= 4095 ? "leu" : v.ToString())}");
            """;
        (serial, erro, compilado, _) = RodarNativo(pwm, 400);
        Conferir("C# (Roslyn): PwmChannel.CreateFromPin (o LEDC da ESP32) e o AdcController no canal 6 (GPIO34)",
            compilado && erro is null && serial == "4095 leu", $"{compilado} {erro} | {serial}");

        // ---- as ligacoes do Iot.Device, a porta serial e o I2C ----
        const string ligacoes = """
            using System.Device.I2c;
            using System.Device.Pwm;
            using System.Diagnostics;
            using System.IO.Ports;
            using Iot.Device.DHTxx;
            using Iot.Device.ServoMotor;
            using nanoFramework.Hardware.Esp32;

            var servo = new ServoMotor(PwmChannel.CreateFromPin(18, 50), 180, 500, 2500);
            servo.Start();
            servo.WriteAngle(90);

            var dht = new Dht22(4);
            double t = dht.Temperature.DegreesCelsius;
            bool leu = dht.IsLastReadSuccessful;

            Configuration.SetPinFunction(16, DeviceFunction.COM2_RX);
            Configuration.SetPinFunction(17, DeviceFunction.COM2_TX);
            using var porta = new SerialPort("COM2", 9600);
            porta.Open();
            porta.WriteLine("oi");

            var sensor = I2cDevice.Create(new I2cConnectionSettings(1, 0x68));
            var r = sensor.WriteByte(0x75);

            Debug.WriteLine($"{(leu && t > -40 && t < 80 ? "dht ok" : t.ToString())} {porta.IsOpen} {r.Status}");
            """;
        (serial, erro, compilado, m) = RodarNativo(ligacoes, 500);
        Conferir("C# (Roslyn): ServoMotor (o servo gira a 90 graus), Dht22, SerialPort no COM2 (pinos do SetPinFunction) e I2cDevice",
            compilado && erro is null && serial.StartsWith("dht ok True ") && Math.Abs(m.AnguloServo[18] - 90) <= 2,
            $"{compilado} {erro} | {serial} | servo {m.AnguloServo[18]}");
    }
}
