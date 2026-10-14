namespace Simulador.App;

/// <summary>
/// O std::string: a API dele (size, substr, find, npos, to_string, stoi...)
/// e a regra do compilador que o simulador nao pode afrouxar -- o String do
/// Arduino nao vira std::string sozinho, e o "std::string x =
/// pCharacteristic->getValue();" dos tutoriais do core 2.x nao compila no 3.x.
/// </summary>
public static partial class Autoteste
{
    static void Motor_StdString()
    {
        saida.AppendLine("o std::string");

        const string api = """
            #include <string>
            using namespace std;
            void setup() {
              Serial.begin(115200);
              std::string a = "temperatura=23.5";
              string b = "ola";
              b.append(" mundo");
              b.push_back('!');
              Serial.println(b.c_str());
              size_t eq = a.find('=');
              Serial.print("eq: "); Serial.println(eq);
              Serial.print("valor: "); Serial.println(a.substr(eq + 1).c_str());
              Serial.print("dobro: "); Serial.println(std::stof(a.substr(eq + 1)) * 2);
              Serial.println(a.find("xyz") == std::string::npos ? "nao achou" : "achou");
              Serial.print("npos: "); Serial.println(std::string::npos);
              Serial.print("int: "); Serial.println(std::to_string(42).c_str());
              Serial.print("float: "); Serial.println(std::to_string(1.5).c_str());
              Serial.print("stoi: "); Serial.println(std::stoi("123abc") + 1);
            }
            void loop() {}
            """;
        var (_, serial, erro) = RodarComPinos(api, "esp32-devkit-v1", 2_000);
        saida.AppendLine($"        {string.Join(" | ", serial)} {erro}");
        Conferir("std::string: append e push_back mudam o texto, e \"string\" vale com o using namespace std",
            serial.Contains("ola mundo!"), string.Join("|", serial));
        Conferir("std::string: find, substr e stof", NumeroDaLinha(serial, "eq:") == 11 && serial.Contains("valor: 23.5")
            && serial.Contains("dobro: 47.00"), string.Join("|", serial));
        Conferir("std::string: o find que nao acha devolve npos (4294967295 num chip de 32 bits)",
            serial.Contains("nao achou") && serial.Contains("npos: 4294967295"), string.Join("|", serial));
        Conferir("std::string: to_string de int e de double (o \"%f\", seis casas) e stoi parando na letra",
            serial.Contains("int: 42") && serial.Contains("float: 1.500000") && serial.Contains("stoi: 124"), string.Join("|", serial));

        // O tutorial do core 2.x: no 3.x o getValue() da BLE devolve String.
        string erroDoCore = null;
        try
        {
            Compilador.CompilarProjeto(new Projeto
            {
                Placa = "esp32-devkit-v1",
                DoSimulador = Acervo.EhDoSimulador,
                Sketch = """
                    #include <BLEDevice.h>
                    class Cb : public BLECharacteristicCallbacks {
                      void onWrite(BLECharacteristic *pCharacteristic) {
                        std::string rxValue = pCharacteristic->getValue();
                        Serial.println(rxValue.c_str());
                      }
                    };
                    void setup() { BLEDevice::init("x"); }
                    void loop() {}
                    """,
            });
        }
        catch (ErroDeSintaxe e) { erroDoCore = e.Message; }
        Conferir("std::string: \"std::string x = pCharacteristic->getValue();\" nao compila no core 3.x, com a mensagem do GCC",
            erroDoCore?.Contains("conversion from 'String' to non-scalar type 'std::string'") == true, erroDoCore ?? "compilou");

        string semErro = null;
        try
        {
            Compilador.CompilarProjeto(new Projeto
            {
                Placa = "esp32-devkit-v1",
                DoSimulador = Acervo.EhDoSimulador,
                Sketch = "void setup() { std::string x = \"ok\"; String y = WiFi.localIP().toString(); }\nvoid loop() {}",
            });
        }
        catch (ErroDeSintaxe e) { semErro = e.Message; }
        Conferir("std::string: o que o compilador aceita continua passando (literal, String de toString)",
            semErro is null, semErro ?? "");
    }
}
