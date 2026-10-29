namespace Simulador.App;

/// <summary>
/// O C++ compilado de verdade (CompiladorNativo + SketchNativo): a linguagem
/// inteira (classe, template, STL, lambda, excecao), o core do Arduino pela
/// ponte (o mesmo serial, os mesmos pinos e o mesmo relogio do
/// interpretador), o erro de compilacao na linha certa, a falha que nao
/// derruba o simulador, a placa que para com o sketch preso num laco, e a
/// interrupcao que corta um laco sem chamada nenhuma.
/// </summary>
public static partial class Autoteste
{
    /// <summary>
    /// Compila nativo e roda o setup() e o loop() por <paramref name="ms"/>
    /// milissegundos (o relogio e o real). Devolve o serial, o erro, se o
    /// programa veio mesmo compilado, e a placa.
    /// </summary>
    static (string Serial, string Erro, bool Nativo, Maquina Maq) RodarNativo(string sketch, int ms = 300,
        string placa = "esp32-devkit-v1", Action<Maquina> durante = null, IDictionary<string, string> abas = null)
    {
        bool antes = Compilador.CppNativo;
        Compilador.CppNativo = true;
        var maq = new Maquina { Perfil = PerfilPlaca.De(placa) ?? PerfilPlaca.Esp32 };
        string Serial() => string.Join("|", maq.Serial.Select(l => l.Contains("] ") ? l[(l.IndexOf("] ") + 2)..] : l));
        try
        {
            var prog = Compilador.CompilarProjeto(new Projeto { Sketch = sketch, Placa = placa, Arquivos = abas, DoSimulador = Acervo.EhDoSimulador });
            bool nativo = prog.Compilado is not null;
            var interp = new Interpretador(prog, maq);
            Exception falha = null;
            var thread = new Thread(() =>
            {
                try
                {
                    interp.Preparar();
                    interp.Setup();
                    while (!maq.Parar && interp.TemLoop) interp.Loop();
                }
                catch (ExecucaoInterrompida) { }
                catch (Exception e) { falha = e; }
            }, 64 << 20) { IsBackground = true };
            thread.Start();
            var relogio = System.Diagnostics.Stopwatch.StartNew();
            while (thread.IsAlive && relogio.ElapsedMilliseconds < ms)
            {
                durante?.Invoke(maq);
                Thread.Sleep(5);
            }
            maq.Parar = true;
            bool parou = thread.Join(3000);
            maq.EncerrarNativo();
            if (!parou) return (Serial(), "a placa nao parou", nativo, maq);
            return (Serial(), falha?.Message, nativo, maq);
        }
        catch (Exception e) when (e is ErroDeSintaxe or ErroDeExecucao) { return (null, e.Message, false, maq); }
        finally { Compilador.CppNativo = antes; }
    }

    static void Cpp_Nativo()
    {
        saida.AppendLine("C++ compilado de verdade (clang + core nativo)");
        if (!CompiladorNativo.Disponivel)
        {
            Conferir("C++ nativo: o compilador (nativo/zig) esta instalado", false, "faltou nativo/zig/zig.exe");
            return;
        }

        // ---- a linguagem inteira ----
        const string linguagem = """
            #include <vector>
            #include <map>
            #include <functional>
            #include <memory>
            #include <algorithm>
            #include <string>

            template <typename T> T dobro(T x) { return x * 2; }

            struct Forma { virtual float area() const = 0; virtual ~Forma() {} };
            struct Quadrado : Forma { float l; Quadrado(float l) : l(l) {} float area() const override { return l * l; } };
            struct Circulo : Forma { float r; Circulo(float r) : r(r) {} float area() const override { return 3.0f * r * r; } };

            class Contador {
              int n = 0;
            public:
              Contador &operator++() { ++n; return *this; }
              int valor() const { return n; }
            };

            std::map<std::string, int> idades = {{"ana", 20}, {"bia", 31}};

            void setup() {
              Serial.begin(115200);
              std::vector<std::unique_ptr<Forma>> formas;
              formas.push_back(std::make_unique<Quadrado>(2));
              formas.push_back(std::make_unique<Circulo>(1));
              float soma = 0;
              for (auto &f : formas) soma += f->area();
              Serial.println(soma);

              std::vector<int> v = {5, 3, 9, 1};
              std::sort(v.begin(), v.end());
              String s;
              for (int x : v) s += String(x) + ",";
              Serial.println(s);

              int base = 10;
              std::function<int(int)> soma10 = [base](int k) { return k + base; };
              Serial.println(soma10(dobro(16)));

              for (auto &[nome, idade] : idades) { Serial.print(nome.c_str()); Serial.print("="); Serial.println(idade); }

              Contador c; ++c; ++c; ++c;
              Serial.println(c.valor());

              try { throw std::runtime_error("ops"); }
              catch (const std::exception &e) { Serial.print("pegou "); Serial.println(e.what()); }

              Serial.println(proxima(41));
            }

            void loop() { delay(1000); }

            int proxima(int x) { return x + 1; }
            """;
        var (serial, erro, nativo, _) = RodarNativo(linguagem, 200);
        Conferir("C++ nativo: classe virtual, template, STL, lambda, structured binding, excecao e funcao depois do uso (prototipo)",
            nativo && erro is null && serial == "7.00|1,3,5,9,|42|ana=20|bia=31|3|pegou ops|42", $"{nativo} {erro} | {serial}");

        // ---- o Print do Arduino ----
        const string impressao = """
            void setup() {
              Serial.begin(9600);
              Serial.println(3.14159, 3);
              Serial.println(255, HEX);
              Serial.println(5, BIN);
              Serial.println(-42);
              Serial.println('A');
              Serial.printf("%d-%s\n", 7, "x");
              String t = "  Ola Mundo  ";
              t.trim(); t.toUpperCase();
              Serial.println(t + " " + t.length() + " " + t.indexOf("MUNDO"));
              Serial.println(String(12.5f, 1) + F("!"));
            }
            void loop() { delay(1000); }
            """;
        (serial, erro, nativo, _) = RodarNativo(impressao, 200);
        Conferir("C++ nativo: print com casas, HEX, BIN, negativo, char, printf, String (trim, toUpperCase, indexOf, F())",
            nativo && erro is null && serial == "3.142|FF|101|-42|A|7-x|OLA MUNDO 9 4|12.5!", $"{erro} | {serial}");

        // ---- os pinos e o relogio ----
        const string pisca = """
            int trocas = 0;
            void setup() { pinMode(2, OUTPUT); Serial.begin(9600); }
            void loop() {
              unsigned long t0 = millis();
              digitalWrite(2, !digitalRead(2));
              trocas++;
              delay(50);
              unsigned long dt = millis() - t0;
              if (trocas == 3) Serial.println(dt >= 48 && dt <= 80 ? "tempo ok" : "tempo errado");
            }
            """;
        (serial, erro, nativo, var m) = RodarNativo(pisca, 400);
        Conferir("C++ nativo: pinMode/digitalWrite/digitalRead/millis/delay pelo simulador (o LED troca e o delay(50) dura 50 ms)",
            nativo && erro is null && serial.Contains("tempo ok") && m.Modo[2] == 1, $"{erro} | {serial}");

        // ---- o erro de compilacao na linha certa ----
        const string errado = "void setup() {\n  int x = 5\n  Serial.println(x);\n}\nvoid loop() {}\nint naoExiste() { return y; }\n";
        (serial, erro, _, _) = RodarNativo(errado, 50);
        Conferir("C++ nativo: o erro do clang vem com a linha do editor (o interpretador tambem recusa)",
            erro is not null && erro.StartsWith("linha 2:") && erro.Contains("expected ';'"), erro ?? "(sem erro)");

        // ---- a falha nao derruba o simulador ----
        const string falha = """
            void setup() { Serial.begin(9600); Serial.println("antes"); }
            void loop() { int *p = nullptr; volatile int z = 0; *p = 5 / (z + 1); delay(10); }
            """;
        (serial, erro, nativo, _) = RodarNativo(falha, 300);
        Conferir("C++ nativo: ponteiro nulo vira erro da placa (\"travou no loop\"), e o simulador segue",
            nativo && serial == "antes" && erro is not null && erro.Contains("travou no loop"), $"{erro} | {serial}");

        // ---- a placa para com o sketch preso num laco sem chamada nenhuma ----
        const string preso = """
            volatile unsigned long n = 0;
            void setup() { Serial.begin(9600); Serial.println("entrou"); }
            void loop() { while (true) { n++; } }
            """;
        // Compilado antes: o relogio mede a parada, e nao o clang.
        CompiladorNativo.Compilar(new Projeto { Sketch = preso, Placa = "esp32-devkit-v1" });
        var relogio = System.Diagnostics.Stopwatch.StartNew();
        (serial, erro, nativo, _) = RodarNativo(preso, 300);
        Conferir("C++ nativo: Parar tira a placa de um while(true) sem chamada (o vigia desvia a thread)",
            nativo && serial == "entrou" && erro is null && relogio.ElapsedMilliseconds < 2500, $"{erro} | {serial} | {relogio.ElapsedMilliseconds} ms");

        // ---- a interrupcao corta um laco sem chamada ----
        const string interrupcao = """
            volatile int bordas = 0;
            void contar() { bordas++; }
            void setup() {
              Serial.begin(9600);
              pinMode(4, INPUT);
              attachInterrupt(digitalPinToInterrupt(4), contar, CHANGE);
            }
            void loop() {
              unsigned long t0 = micros();
              volatile unsigned long k = 0;
              while (bordas < 4 && k < 4000000000UL) k++;
              Serial.println(bordas >= 4 ? "interrompeu" : "nao");
              detachInterrupt(digitalPinToInterrupt(4));
              while (true) delay(100);
            }
            """;
        int nivel = 0, quando = 0;
        (serial, erro, nativo, _) = RodarNativo(interrupcao, 600, durante: maq =>
        {
            if (++quando % 4 != 0) return;
            nivel ^= 1;
            maq.NivelExterno[4] = nivel;
        });
        Conferir("C++ nativo: attachInterrupt -- a borda de fora suspende o laco, a rotina roda, e o laco ve o volatile mudar",
            nativo && erro is null && serial.StartsWith("interrompeu"), $"{erro} | {serial}");

        // ---- as abas: .ino a mais e .cpp com .h ----
        var abas = new Dictionary<string, string>
        {
            ["util.h"] = "#pragma once\nint quadrado(int x);\n",
            ["util.cpp"] = "#include \"util.h\"\nint quadrado(int x) { return x * x; }\n",
            ["extra.ino"] = "void dizer(int v) { Serial.println(v); }\n",
        };
        (serial, erro, nativo, _) = RodarNativo("#include \"util.h\"\nvoid setup() { Serial.begin(9600); dizer(quadrado(9)); }\nvoid loop() { delay(100); }\n",
                                                200, abas: abas);
        Conferir("C++ nativo: as abas -- o .cpp com o .h, e a funcao de outra aba .ino",
            nativo && erro is null && serial == "81", $"{erro} | {serial}");

        // ---- as bibliotecas que o simulador imita, pelo C++ compilado ----
        const string bibliotecas = """
            #include <Servo.h>
            #include <LiquidCrystal_I2C.h>
            #include <Adafruit_NeoPixel.h>
            #include <EEPROM.h>
            #include <Preferences.h>
            #include <RTClib.h>
            #include <vector>

            Servo servo;
            LiquidCrystal_I2C lcd(0x27, 16, 2);
            Adafruit_NeoPixel fita(4, 5, NEO_GRB + NEO_KHZ800);
            Preferences prefs;
            RTC_DS1307 rtc;
            struct Config { int brilho; float limite; };

            void setup() {
              Serial.begin(115200);
              servo.attach(9);
              servo.write(45);
              Serial.println(servo.read());

              lcd.init();
              lcd.backlight();
              lcd.setCursor(0, 0);
              lcd.print("T=");
              lcd.print(23.456, 1);

              fita.begin();
              fita.setPixelColor(1, fita.Color(10, 20, 30));
              fita.show();
              Serial.println(fita.getPixelColor(1), HEX);

              EEPROM.begin(32);
              Config c = {7, 2.5f}, lida = {};
              EEPROM.put(0, c);
              EEPROM.commit();
              EEPROM.get(0, lida);
              Serial.println(String(lida.brilho) + " " + String(lida.limite, 1));

              prefs.begin("ajustes");
              prefs.putInt("voltas", 42);
              Serial.println(prefs.getInt("voltas", 0));

              rtc.begin();
              rtc.adjust(DateTime(2026, 10, 28, 14, 30, 0));
              DateTime agora = rtc.now();
              Serial.println(agora.year() == 2026 && agora.month() == 10 && agora.hour() == 14 ? "rtc ok" : "rtc errado");
            }
            void loop() { delay(100); }
            """;
        (serial, erro, nativo, m) = RodarNativo(bibliotecas, 400);
        string lcd = m.Lcd?.LinhaTexto(0)?.TrimEnd() ?? "";
        Conferir("C++ nativo: Servo, LCD I2C, NeoPixel, EEPROM.put/get de struct, Preferences e RTClib pelo simulador",
            nativo && erro is null && serial == "45|A141E|7 2.5|42|rtc ok" && m.AnguloServo[9] == 45 && lcd == "T=23.5",
            $"{nativo} {erro} | {serial} | lcd \"{lcd}\" servo {m.AnguloServo[9]}");

        // ---- a BLE da ESP32: os objetos do simulador e os callbacks do sketch ----
        const string ble = """
            #include <BLEDevice.h>
            #include <BLEServer.h>
            #include <BLE2902.h>

            int conexoes = 0;
            class AoConectar : public BLEServerCallbacks {
              void onConnect(BLEServer *s) override { conexoes++; }
            };
            class AoEscrever : public BLECharacteristicCallbacks {
              void onWrite(BLECharacteristic *c) override { Serial.println(c->getValue()); }
            };

            void setup() {
              Serial.begin(115200);
              BLEDevice::init("Robo");
              BLEServer *servidor = BLEDevice::createServer();
              servidor->setCallbacks(new AoConectar());
              BLEService *servico = servidor->createService("4fafc201-1fb5-459e-8fcc-c5c9c331914b");
              BLECharacteristic *c = servico->createCharacteristic("beb5483e-36e1-4688-b7f5-ea07361b26a8",
                  BLECharacteristic::PROPERTY_READ | BLECharacteristic::PROPERTY_WRITE | BLECharacteristic::PROPERTY_NOTIFY);
              c->addDescriptor(new BLE2902());
              c->setCallbacks(new AoEscrever());
              c->setValue("oi");
              servico->start();
              BLEDevice::getAdvertising()->addServiceUUID(servico->getUUID());
              BLEDevice::startAdvertising();
              Serial.println(c->getValue());
              Serial.println(servico->getUUID().toString());
              Serial.println(servidor == BLEDevice::createServer() ? "mesmo" : "outro");
              Serial.println(BLEDevice::getAddress().toString().length() == 17 ? "mac" : BLEDevice::getAddress().toString());
            }
            void loop() { delay(100); }
            """;
        (serial, erro, nativo, _) = RodarNativo(ble, 400);
        Conferir("C++ nativo: BLE da ESP32 -- servidor, servico, caracteristica, descritor, os callbacks do sketch e o mesmo objeto de volta",
            nativo && erro is null && serial == "oi|4fafc201-1fb5-459e-8fcc-c5c9c331914b|mesmo|mac", $"{nativo} {erro} | {serial}");

        // ---- as portas: SoftwareSerial no Uno e a UART pelo numero na ESP32 ----
        const string portas = """
            #include <SoftwareSerial.h>
            SoftwareSerial bt(10, 11);
            void setup() {
              Serial.begin(9600);
              bt.begin(9600);
              bt.listen();
              Serial.println(bt.isListening() ? "escutando" : "surda");
              Serial.println(bt.available());
            }
            void loop() { delay(100); }
            """;
        (serial, erro, nativo, _) = RodarNativo(portas, 300, "arduino-uno");
        string serialUno = serial;
        const string uart = """
            HardwareSerial gps(2);
            void setup() {
              Serial.begin(115200);
              gps.begin(9600, SERIAL_8N1, 16, 17);
              Serial.println(gps.available());
            }
            void loop() { delay(100); }
            """;
        var (serialEsp, erroEsp, nativoEsp, _) = RodarNativo(uart, 300);
        Conferir("C++ nativo: SoftwareSerial (Uno) e HardwareSerial(2) com os pinos do begin",
            nativo && erro is null && serialUno == "escutando|0" && nativoEsp && erroEsp is null && serialEsp == "0",
            $"{nativo} {erro} | {serialUno} || {nativoEsp} {erroEsp} | {serialEsp}");
    }
}
