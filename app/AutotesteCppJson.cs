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

        // ---- o chip do OLED: os bytes do barramento viram o desenho ----
        const string oledCru = """
            #include <Wire.h>
            void comandos(std::initializer_list<uint8_t> lista) {
              Wire.beginTransmission(0x3C);
              Wire.write(0x00);
              for (uint8_t c : lista) Wire.write(c);
              Wire.endTransmission();
            }
            void setup() {
              Serial.begin(115200);
              Wire.begin();
              comandos({0xAE, 0x20, 0x00, 0xA1, 0xC8, 0x21, 10, 12, 0x22, 2, 2, 0xAF});
              Wire.beginTransmission(0x3C);
              Wire.write(0x40);
              Wire.write(0x01); Wire.write(0xFF); Wire.write(0x80);
              Wire.endTransmission();
              Wire.beginTransmission(0x3C);
              Wire.endTransmission();
              Serial.println("fim");
            }
            void loop() { delay(100); }
            """;
        var (serOled, erOled, natOled, maqOled) = RodarNativo(oledCru, 1500, antes: (_, m) => m.I2c.Ligar(new OledI2c(m, 0x3C, "OLED", 128, 64, false)));
        var vidro = maqOled?.PainelOled;
        bool Aceso(int x, int y) => vidro is not null && vidro.Pixels[y * vidro.LarguraFisica + x] == vidro.CorAcesa;
        int acesos = vidro is null ? 0 : vidro.Pixels.Count(p => p == vidro.CorAcesa);
        Conferir("OLED: o chip no I2C -- o modo horizontal, a coluna e a pagina pedidas e o bit de cada byte caem no ponto certo do vidro",
            natOled && erOled is null && serOled?.Contains("fim") == true && acesos == 10 && Aceso(10, 16) && Aceso(11, 16) && Aceso(11, 23) && Aceso(12, 23) && !Aceso(10, 17),
            $"nativo={natOled} erro={erOled} serial=[{serOled}] acesos={acesos}");

        // ---- como a IDE compila: as placas que nao sao ESP32, os protótipos, a versao e o relogio ----
        const string basico = """
            void setup() { Serial.begin(115200); Serial.println(dobro(21)); }
            void loop() { delay(100); }
            int dobro(int x) { return x + x; }
            """;
        foreach (string placa in new[] { "arduino-uno", "esp8266-nodemcu", "pi-pico", "esp32-h2-devkit" })
        {
            var (serB, erB, natB, _) = RodarNativo(basico, 1500, placa: placa);
            Conferir($"como a IDE: o core de {placa} compila e o sketch roda", natB && erB is null && serB?.Contains("42") == true, $"nativo={natB} erro={erB} serial=[{serB}]");
        }
        const string comoIde = """
            #ifdef __AVR__
            #include <NaoExisteNaEsp32.h>
            void soNoAvr(TipoDoAvr &x) { x.mexe(); }
            #endif
            #include <sys/time.h>
            #include <time.h>
            void setup() {
              Serial.begin(115200);
              registra(mostra);
              Serial.println(ESP_ARDUINO_VERSION >= ESP_ARDUINO_VERSION_VAL(3, 3, 0) ? "versao nova" : "versao velha");
            #if ESP32
              Serial.println("if esp32 verdadeiro");
            #endif
            #ifdef ESP32
              Serial.println("ifdef esp32");
            #endif
              struct timeval tv = {1700000000, 0};
              settimeofday(&tv, NULL);
              Serial.println((long)time(NULL) >= 1700000000 && (long)time(NULL) < 1700000100 ? "time ajustado" : "time errado");
              struct tm t;
              getLocalTime(&t);
              Serial.println(t.tm_year + 1900);
              Serial.println(t.tm_hour);
            }
            void loop() { delay(100); }
            void mostra(int x) { Serial.println(x); }
            void registra(void (*cb)(int)) { cb(7); }
            """;
        var (serIde, erIde, natIde, _) = RodarNativo(comoIde, 1500);
        Conferir("como a IDE: o #if de outra placa nao pesa, e a funcao com ponteiro de funcao ganha prototipo",
            natIde && erIde is null && serIde?.Contains("7") == true, $"nativo={natIde} erro={erIde} serial=[{serIde}]");
        Conferir("como a IDE: a versao do core e a do cabecalho (3.3.12), e o ESP32 vale em #ifdef, nao em #if",
            serIde?.Contains("versao nova") == true && serIde.Contains("ifdef esp32") && !serIde.Contains("if esp32 verdadeiro"), $"serial=[{serIde}]");
        Conferir("settimeofday: o time() e o getLocalTime contam da hora pedida, em UTC como na placa sem fuso",
            serIde?.Contains("time ajustado") == true && serIde.Contains("2023") && serIde.Contains("|22"), $"serial=[{serIde}]");

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
            File.WriteAllText(Path.Combine(pasta, "src", "SoParaOutraPlaca.h"), "#include <Puxada.h>\n");
            string pastaPuxada = Path.Combine(raiz, "Puxada");
            Directory.CreateDirectory(Path.Combine(pastaPuxada, "src"));
            File.WriteAllText(Path.Combine(pastaPuxada, "library.properties"), "name=Puxada\nversion=1.0.0\n");
            File.WriteAllText(Path.Combine(pastaPuxada, "src", "Puxada.h"), "#pragma once\n");
            File.WriteAllText(Path.Combine(pastaPuxada, "src", "Puxada.cpp"), "#error nao devia compilar: ninguem incluiu o cabecalho que a puxa\n");
            var lib = Acervo.Carregar(pasta);
            var puxada = Acervo.Carregar(pastaPuxada);
            const string corpo = """
                #include <Moderna.h>
                void setup() {
                  Serial.begin(115200);
                  Moderna::Caixa<int> c{21};
                  Serial.println(c.dobro());
                }
                void loop() { delay(100); }
                """;
            var (serial, erro, nativo, _) = RodarNativo(corpo, 1500, bibliotecas: [lib, puxada]);
            Conferir("biblioteca moderna: instalada, vai pelo compilador de verdade e roda",
                nativo && erro is null && serial?.Contains("42") == true, $"nativo={nativo} erro={erro} serial=[{serial}]");

            (serial, erro, nativo, _) = RodarNativo("#include <NaoExiste.h>\n" + corpo, 300, bibliotecas: [lib]);
            Conferir("biblioteca moderna: o #include que falta aparece como o motivo, na linha dele, e nao como erro dentro da biblioteca",
                !nativo && erro?.StartsWith("linha 1:", StringComparison.Ordinal) == true && erro.Contains("NaoExiste.h") && erro.Contains("nao esta instalada"), $"nativo={nativo} erro={erro}");

            (serial, erro, nativo, _) = RodarNativo("void x() { PORTB = 1; }\n" + corpo, 300, bibliotecas: [lib]);
            Conferir("biblioteca moderna: o registrador do AVR tira do compilador, e o erro diz isso",
                !nativo && erro?.Contains("PORTB") == true, $"nativo={nativo} erro={erro}");
        }
        finally
        {
            try { Directory.Delete(raiz, true); } catch (IOException) { }
        }

        if (Environment.GetEnvironmentVariable("CS_U8G2") is { } pastaU8 && Directory.Exists(pastaU8))
        {
            const string desenho = """
                #include <U8g2lib.h>
                #include <Wire.h>
                U8G2_SSD1306_128X64_NONAME_F_HW_I2C tela(U8G2_R0, U8X8_PIN_NONE);
                void setup() {
                  Serial.begin(115200);
                  tela.begin();
                  tela.clearBuffer();
                  tela.setFont(u8g2_font_ncenB08_tr);
                  tela.drawStr(0, 12, "Ola U8g2");
                  tela.sendBuffer();
                  Serial.println("fim");
                }
                void loop() { delay(100); }
                """;
            var libU8 = Acervo.Carregar(pastaU8);
            var projetoU8 = new Projeto { Sketch = desenho, Placa = "esp32-devkit-v1", Bibliotecas = [libU8], DoSimulador = Acervo.EhDoSimulador };
            saida.AppendLine($"U8G2 raiz={libU8.Raiz} arquivos={libU8.Arquivos.Count} porque_nao={CompiladorNativo.PorQueNao(projetoU8) ?? "(compila)"}");
            var relogioU8 = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                bool eraU8 = Compilador.CppNativo;
                Compilador.CppNativo = true;
                var dll = CompiladorNativo.Compilar(projetoU8, t => saida.AppendLine("U8G2 aviso: " + t));
                Compilador.CppNativo = eraU8;
                saida.AppendLine($"U8G2 compilou em {relogioU8.ElapsedMilliseconds} ms: {dll.Dll} {new FileInfo(dll.Dll).Length / 1024} KB");
            }
            catch (Exception e) { saida.AppendLine($"U8G2 clang ({relogioU8.ElapsedMilliseconds} ms): {e.Message}"); }
            var (serU8, erU8, natU8, maqU8) = RodarNativo(desenho, 3000, bibliotecas: [libU8], antes: (_, m) => m.I2c.Ligar(new OledI2c(m, 0x3C, "OLED", 128, 64, false)));
            var vidroU8 = maqU8?.PainelOled;
            var linhasU8 = new List<string>();
            for (int y = 0; vidroU8 is not null && y < 16; y++)
                linhasU8.Add(new string(Enumerable.Range(0, 64).Select(x => vidroU8.Pixels[y * 128 + x] == vidroU8.CorAcesa ? '#' : '.').ToArray()));
            saida.AppendLine($"U8G2 nativo={natU8} erro={erU8} serial=[{serU8}] painel={(vidroU8 is null ? "nulo" : "ok")}{Environment.NewLine}" + string.Join(Environment.NewLine, linhasU8));
            relogioU8.Restart();
            var dllDeNovo = CompiladorNativo.Compilar(new Projeto { Sketch = desenho + "// " + DateTime.Now.Ticks, Placa = "esp32-devkit-v1", Bibliotecas = [libU8], DoSimulador = Acervo.EhDoSimulador }, null);
            saida.AppendLine($"U8G2 sketch editado compilou em {relogioU8.ElapsedMilliseconds} ms");
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
        // ---- pela janela, instalada no acervo como a pessoa instala ----
        string instalada = Acervo.Instalar(real);
        bool eraNativoJanela = Compilador.CppNativo;
        Compilador.CppNativo = true;
        try
        {
            saida.AppendLine($"        instalada: {instalada}; falta para o sketch: [{string.Join(", ", Acervo.OQueFalta(sketch).Select(f => f.Cabecalho))}]");
            Conferir("ArduinoJson pela janela: instalado no acervo, o sketch nao aparece com biblioteca faltando",
                Acervo.OQueFalta(sketch).Count == 0, string.Join(", ", Acervo.OQueFalta(sketch).Select(f => f.Cabecalho)));

            var circuito = new Circuito();
            circuito.Pecas.Add(new PecaNoCircuito { Id = "esp", Tipo = "esp32-devkit-v1", X = 0, Y = 0 });
            var janela = new MainWindow();
            janela.TesteAbrir(circuito, sketch);
            janela.TesteRodar();
            bool rodou = EsperarPor(() => { janela.TesteAtualizarPaineis(); return janela.TesteSerial.Contains("fim"); }, 60000);
            var errosJanela = janela.TesteLinhasDoNivel(NivelDoConsole.Erro);
            saida.AppendLine($"        janela: serial=[{janela.TesteSerial.Replace(Environment.NewLine, "|")}] erros=[{string.Join(" / ", errosJanela)}]");
            Conferir("ArduinoJson pela janela: F5 compila, roda e o monitor serial mostra o JSON montado e o lido",
                rodou && errosJanela.Count == 0 && janela.TesteSerial.Contains("\"sensor\":\"gps\"") && janela.TesteSerial.Contains("oi") && janela.TesteSerial.Contains("1.50"),
                $"erros=[{string.Join(" / ", errosJanela)}] serial=[{janela.TesteSerial}]");
            janela.TesteParar();

            // O include que falta, na janela: o erro conta o motivo.
            var comFalta = new Circuito();
            comFalta.Pecas.Add(new PecaNoCircuito { Id = "esp", Tipo = "esp32-devkit-v1", X = 0, Y = 0 });
            janela.TesteAbrir(comFalta, "#include <Ethernet.h>\n" + sketch);
            janela.TesteRodar();
            EsperarPor(() => janela.TesteLinhasDoNivel(NivelDoConsole.Erro).Count > 0, 20000);
            var errosFalta = janela.TesteLinhasDoNivel(NivelDoConsole.Erro);
            saida.AppendLine($"        janela, com o Ethernet.h que falta: [{string.Join(" / ", errosFalta)}]");
            Conferir("ArduinoJson pela janela: com um #include que falta, o console aponta ele, e nao a linha de dentro do ArduinoJson",
                errosFalta.Any(e => e.Contains("Ethernet.h") && e.Contains("nao esta instalada")) && !errosFalta.Any(e => e.Contains("Converter.hpp")), string.Join(" / ", errosFalta));
            janela.TesteParar();
        }
        finally
        {
            Compilador.CppNativo = eraNativoJanela;
            Acervo.Remover(Acervo.Instaladas().First(b => b.Nome == instalada));
        }

        var (ser, er, nat, _) = RodarNativo(sketch, 2500, bibliotecas: [Acervo.Carregar(real)]);
        Conferir("ArduinoJson de verdade: compila, monta o documento e le o texto",
            nat && er is null && ser?.Contains("\"sensor\":\"gps\"") == true && ser.Contains("Ok") && ser.Contains("oi") && ser.Contains("1.50") && ser.Contains("fim"),
            $"nativo={nat} erro={er} serial=[{ser}]");
    }
}
