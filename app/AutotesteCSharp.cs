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

        // ---- as telas e a fita do nanoFramework: os bytes de verdade no chip de cada peca ----
        const string lcd = """
            using System.Device.I2c;
            using System.Diagnostics;
            using Iot.Device.CharacterLcd;

            using var i2c = I2cDevice.Create(new I2cConnectionSettings(1, 0x27));
            using var lcd = new Lcd1602(i2c, false);
            lcd.Clear();
            lcd.Write("Ola C#");
            lcd.CreateCustomCharacter(0, new byte[] { 0, 10, 31, 31, 14, 4, 0, 0 });
            lcd.SetCursorPosition(3, 1);
            lcd.Write("T=21" + (char)0);
            Debug.WriteLine("escreveu");
            """;
        (serial, erro, compilado, m) = RodarNativo(lcd, 1500, antes: (_, maq) => maq.I2c.Ligar(new LcdPcf8574(maq, 0x27, "LCD", 16, 2)));
        string l0 = m?.Lcd?.LinhaTexto(0) ?? "", l1 = m?.Lcd?.LinhaTexto(1) ?? "";
        Conferir("C# (Roslyn): Lcd1602 do nanoFramework pelo PCF8574 -- o meio byte de cada comando escreve as duas linhas e o caractere da CGRAM",
            compilado && erro is null && serial == "escreveu" && l0 == "Ola C#          " && l1 == "   T=21\0        " && m.Lcd.Glifos[0][2] == 31,
            $"{compilado} {erro} | {serial} | \"{l0}\" \"{l1}\"");

        const string oled = """
            using System.Device.I2c;
            using System.Diagnostics;
            using Iot.Device.Ssd13xx;

            using var tela = new Ssd1306(I2cDevice.Create(new I2cConnectionSettings(1, Ssd1306.DefaultI2cAddress)), Ssd13xx.DisplayResolution.OLED128x64);
            tela.ClearScreen();
            tela.DrawPixel(5, 9);
            tela.DrawHorizontalLine(20, 40, 10);
            tela.Display();
            Debug.WriteLine("desenhou");
            """;
        (serial, erro, compilado, m) = RodarNativo(oled, 1500, antes: (_, maq) => maq.I2c.Ligar(new OledI2c(maq, 0x3C, "OLED", 128, 64, false)));
        var vidro = m?.PainelOled;
        bool Aceso(int x, int y) => vidro is not null && vidro.Pixels[y * vidro.LarguraFisica + x] == vidro.CorAcesa;
        int acesos = vidro is null ? 0 : vidro.Pixels.Count(p => p == vidro.CorAcesa);
        Conferir("C# (Roslyn): Ssd1306 do nanoFramework -- a inicializacao, o quadro e o Display() pagina por pagina acendem os pontos certos",
            compilado && erro is null && serial == "desenhou" && acesos == 11 && Aceso(5, 9) && Aceso(20, 40) && Aceso(29, 40) && !Aceso(30, 40),
            $"{compilado} {erro} | {serial} | acesos {acesos}");

        const string fita = """
            using System.Diagnostics;
            using System.Drawing;
            using Iot.Device.Ws28xx.Esp32;

            var leds = new Ws2812b(5, 4);
            leds.Image.SetPixel(1, 0, Color.FromArgb(10, 20, 30));
            leds.Image.SetPixel(3, 0, 200, 0, 100);
            leds.Update();
            Debug.WriteLine("mostrou");
            """;
        (serial, erro, compilado, m) = RodarNativo(fita, 1000);
        var aFita = m?.Fitas.GetValueOrDefault(5);
        Conferir("C# (Roslyn): Ws2812b do nanoFramework -- o Update() manda G R B pelo pino e a fita acende o LED 1 e o 3 com as cores certas",
            compilado && erro is null && serial == "mostrou" && aFita?.Quantidade == 4 && aFita.Cores[1] == Fita.Montar(10, 20, 30) && aFita.Cores[3] == Fita.Montar(200, 0, 100) && aFita.Cores[0] == 0,
            $"{compilado} {erro} | {serial} | {(aFita is null ? "sem fita" : string.Join(",", aFita.Cores.Select(c => c.ToString("X6"))))}");

        // ---- a rede do nanoFramework: o WiFi da bancada, o HttpClient e o MQTT ----
        using var servidor = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        servidor.Start();
        int portaHttp = ((System.Net.IPEndPoint)servidor.LocalEndpoint).Port;
        string pedidoHttp = "";
        _ = Task.Run(() =>
        {
            try
            {
                for (int k = 0; k < 2; k++)
                {
                    using var c = servidor.AcceptTcpClient();
                    var s = c.GetStream();
                    var b = new byte[4096];
                    int n = s.Read(b, 0, b.Length);
                    string pedido = System.Text.Encoding.ASCII.GetString(b, 0, n);
                    pedidoHttp += pedido.Split('\n')[0].Trim() + (pedido.Contains("\r\n\r\nluz=1") ? " luz=1" : "") + ";";
                    string corpo = pedido.StartsWith("GET") ? "{\"temp\":21}" : "criado";
                    string resposta = $"HTTP/1.1 {(pedido.StartsWith("GET") ? "200 OK" : "201 Created")}\r\nContent-Type: text/plain\r\nContent-Length: {corpo.Length}\r\nConnection: close\r\n\r\n{corpo}";
                    var r = System.Text.Encoding.ASCII.GetBytes(resposta);
                    s.Write(r, 0, r.Length);
                }
            }
            catch { }
        });
        string rede = """
            using System;
            using System.Diagnostics;
            using System.Net.Http;
            using System.Text;
            using System.Threading;
            using nanoFramework.M2Mqtt;
            using nanoFramework.M2Mqtt.Messages;
            using nanoFramework.Networking;

            bool ok = WifiNetworkHelper.ConnectDhcp("CircuitSketch-WiFi", "12345678", requiresDateTime: false, token: new CancellationTokenSource(10000).Token);
            Debug.WriteLine($"wifi {ok} {WifiNetworkHelper.Status}");

            using var http = new HttpClient();
            using var r = http.Get("http://127.0.0.1:PORTA/sensor");
            Debug.WriteLine($"get {(int)r.StatusCode} {r.Content.ReadAsString()}");
            using var p = http.Post("http://127.0.0.1:PORTA/luz", new StringContent("luz=1"));
            Debug.WriteLine($"post {(int)p.StatusCode} {p.IsSuccessStatusCode} {p.Content.ReadAsString()}");

            var mqtt = new MqttClient("broker.hivemq.com", 1883, false, null, null, MqttSslProtocols.None);
            string chegou = null;
            mqtt.MqttMsgPublishReceived += (s, e) => chegou = e.Topic + "=" + Encoding.UTF8.GetString(e.Message);
            Debug.WriteLine($"mqtt {mqtt.Connect("placa-cs") == MqttReasonCode.Success} {mqtt.IsConnected}");
            mqtt.Subscribe(new[] { "casa/luz" }, new[] { MqttQoSLevel.AtLeastOnce });
            mqtt.Publish("casa/luz", Encoding.UTF8.GetBytes("liga"));
            for (int i = 0; i < 100 && chegou == null; i++) Thread.Sleep(20);
            Debug.WriteLine($"recebeu {chegou}");
            mqtt.Disconnect();
            """.Replace("PORTA", portaHttp.ToString());
        (serial, erro, compilado, m) = RodarNativo(rede, 6000, antes: (_, maq) => maq.Rede = new Rede { UsarInternet = false });
        m?.Rede?.Encerrar();
        Conferir("C# (Roslyn): rede do nanoFramework -- WifiNetworkHelper.ConnectDhcp, HttpClient (GET e POST pelo WiFi da placa) e o MqttClient do M2Mqtt (assina, publica e recebe)",
            compilado && erro is null && serial == "wifi True NetworkIsReady|get 200 {\"temp\":21}|post 201 True criado|mqtt True True|recebeu casa/luz=liga"
            && pedidoHttp.Contains("GET /sensor") && pedidoHttp.Contains("POST /luz HTTP/1.1 luz=1"),
            $"{compilado} {erro} | {serial} | {pedidoHttp}");

        // ---- o BLE do nanoFramework: o servidor GATT em C#, o cliente NimBLE em C++ na outra placa ----
        const string servidorBle = """
            using System;
            using System.Diagnostics;
            using System.Threading;
            using nanoFramework.Device.Bluetooth;
            using nanoFramework.Device.Bluetooth.GenericAttributeProfile;

            BluetoothLEServer.Instance.DeviceName = "nano-cs";
            var provedor = GattServiceProvider.Create(Utilities.CreateUuidFromShortCode(0x181A)).ServiceProvider;
            var c = provedor.Service.CreateCharacteristic(Utilities.CreateUuidFromShortCode(0x2A6E), new GattLocalCharacteristicParameters
            {
                CharacteristicProperties = GattCharacteristicProperties.Read | GattCharacteristicProperties.Write | GattCharacteristicProperties.Notify,
                UserDescription = "temperatura",
            }).Characteristic;
            c.ReadRequested += (s, e) =>
            {
                var w = new DataWriter();
                w.WriteString("ola");
                e.GetRequest().RespondWithValue(w.DetachBuffer());
            };
            c.WriteRequested += (s, e) =>
            {
                var r = e.GetRequest();
                var leitor = DataReader.FromBuffer(r.Value);
                Debug.WriteLine("escreveu " + leitor.ReadString(r.Value.Length));
                r.Respond();
                var w = new DataWriter();
                w.WriteString("eco");
                c.NotifyValue(w.DetachBuffer());
            };
            provedor.StartAdvertising(new GattServiceProviderAdvertisingParameters { IsConnectable = true, IsDiscoverable = true });
            Debug.WriteLine(provedor.AdvertisementStatus.ToString());
            Thread.Sleep(Timeout.Infinite);
            """;
        const string clienteBle = """
            #include <NimBLEDevice.h>
            const NimBLEAdvertisedDevice *alvo = nullptr;
            class Busca : public NimBLEScanCallbacks {
              void onResult(const NimBLEAdvertisedDevice *d) override {
                if (d->isAdvertisingService(NimBLEUUID("181A"))) alvo = d;
              }
            };
            void setup() {
              Serial.begin(115200);
              delay(500);
              NimBLEDevice::init("cliente");
              NimBLEScan *scan = NimBLEDevice::getScan();
              scan->setScanCallbacks(new Busca());
              scan->start(1500);
              while (scan->isScanning()) delay(50);
              if (!alvo) { Serial.println("nao achou"); return; }
              Serial.println(("achou " + alvo->getName()).c_str());
              NimBLEClient *cli = NimBLEDevice::createClient();
              if (!cli->connect(alvo)) { Serial.println("nao conectou"); return; }
              NimBLERemoteCharacteristic *k = cli->getService("181A")->getCharacteristic("2A6E");
              Serial.println(("leu " + std::string(k->readValue())).c_str());
              k->subscribe(true, [](NimBLERemoteCharacteristic *, uint8_t *d, size_t n, bool) {
                Serial.println(("notificou " + std::string((char *)d, n)).c_str());
              });
              k->writeValue(std::string("oi"), true);
              delay(400);
              Serial.println("fim");
            }
            void loop() { delay(10); }
            """;
        var (serA, serB, erroBle) = RodarDuasNativas(servidorBle, clienteBle, "esp32-devkit-v1", 8000, (a, b) => b.Contains("fim"));
        Conferir("C# (Roslyn): BLE do nanoFramework -- o GattServiceProvider anuncia, a outra placa acha, le o ReadRequested, escreve (WriteRequested) e recebe o NotifyValue",
            erroBle is null && serA?.Contains("Started") == true && serB?.Contains("achou nano-cs") == true && serB.Contains("leu ola")
            && serA.Contains("escreveu oi") && serB.Contains("notificou eco"), $"{erroBle} | {serA} || {serB}");
    }
}
