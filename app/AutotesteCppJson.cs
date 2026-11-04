using System.IO;

namespace Simulador.App;

public static partial class Autoteste
{
    /// <summary>
    /// A biblioteca de C++ moderno, que so o clang le (o ArduinoJson, o U8g2): com ela
    /// o projeto vai pelo compilador de verdade; e quando algo o tira de la, o erro
    /// conta o motivo, e nao uma linha de dentro da biblioteca. O ArduinoJson de verdade
    /// entra se CS_ARDUINOJSON apontar a pasta dele: ele nao vem com o simulador.
    /// </summary>
    static void Cpp_Json()
    {
        saida.AppendLine("C++ compilado: a biblioteca que so o clang le");
        if (!CompiladorNativo.Disponivel)
        {
            Conferir("biblioteca moderna: o compilador (nativo/zig) esta instalado", false, "faltou nativo/zig/zig.exe");
            return;
        }

        string raiz = Path.Combine(Path.GetTempPath(), $"cs-moderna-{Guid.NewGuid():N}");
        string pasta = Path.Combine(raiz, "Moderna");
        Directory.CreateDirectory(Path.Combine(pasta, "src"));
        try
        {
            File.WriteAllText(Path.Combine(pasta, "library.properties"), "name=Moderna\nversion=1.0.0\n");
            File.WriteAllText(Path.Combine(pasta, "src", "Moderna.h"), """
                #pragma once
                namespace Moderna {
                inline namespace V1 {
                template <class T> struct Caixa { T valor; T dobro() const { return valor + valor; } };
                }
                }
                """);
            var lib = Acervo.Carregar(pasta);
            const string corpo = """
                #include <Moderna.h>
                void setup() {
                  Serial.begin(115200);
                  Moderna::Caixa<int> c{21};
                  Serial.println(c.dobro());
                }
                void loop() { delay(100); }
                """;
            var (serial, erro, nativo, _) = RodarNativo(corpo, 1500, bibliotecas: [lib]);
            Conferir("biblioteca moderna: instalada, vai pelo compilador de verdade e roda",
                nativo && erro is null && serial?.Contains("42") == true, $"nativo={nativo} erro={erro} serial=[{serial}]");

            (serial, erro, nativo, _) = RodarNativo("#include <NaoExiste.h>\n" + corpo, 300, bibliotecas: [lib]);
            Conferir("biblioteca moderna: o #include que falta aparece como o motivo, na linha dele, e nao como erro dentro da biblioteca",
                !nativo && erro?.StartsWith("linha 1:", StringComparison.Ordinal) == true && erro.Contains("NaoExiste.h") && erro.Contains("nao esta instalada"), $"nativo={nativo} erro={erro}");

            (serial, erro, nativo, _) = RodarNativo("void x() { PORTB = 1; }\n" + corpo, 300, bibliotecas: [lib]);
            Conferir("biblioteca moderna: o registrador do AVR tira do compilador, e o erro diz isso",
                !nativo && erro?.Contains("PORTB") == true && erro.Contains("interpretador"), $"nativo={nativo} erro={erro}");
        }
        finally
        {
            try { Directory.Delete(raiz, true); } catch (IOException) { }
        }

        string real = Environment.GetEnvironmentVariable("CS_ARDUINOJSON");
        if (string.IsNullOrEmpty(real) || !Directory.Exists(real)) return;
        const string sketch = """
            #include <ArduinoJson.h>
            void setup() {
              Serial.begin(115200);
              JsonDocument doc;
              doc["sensor"] = "gps";
              doc["hora"] = 1351824120;
              JsonArray dados = doc["dados"].to<JsonArray>();
              dados.add(48.75608);
              dados.add(2.302038);
              String texto;
              serializeJson(doc, texto);
              Serial.println(texto);
              JsonDocument lido;
              DeserializationError falha = deserializeJson(lido, "{\"a\":[1,2,{\"b\":\"oi\"}],\"c\":1.5}");
              Serial.println(falha.c_str());
              const char *b = lido["a"][2]["b"];
              float c = lido["c"];
              Serial.println(b);
              Serial.println(c);
              serializeJsonPretty(lido, Serial);
              Serial.println();
              Serial.println("fim");
            }
            void loop() { delay(100); }
            """;
        var (ser, er, nat, _) = RodarNativo(sketch, 2500, bibliotecas: [Acervo.Carregar(real)]);
        Conferir("ArduinoJson de verdade: compila, monta o documento e le o texto",
            nat && er is null && ser?.Contains("\"sensor\":\"gps\"") == true && ser.Contains("Ok") && ser.Contains("oi") && ser.Contains("1.50") && ser.Contains("fim"),
            $"nativo={nat} erro={er} serial=[{ser}]");
    }
}
