using System.IO;

namespace Simulador.App;

/// <summary>
/// Os testes do C++ de biblioteca (Alpha 19): o pre-processador que ve as
/// macros de cada placa, as classes, e a biblioteca instalada de fora --
/// pasta e .zip -- rodando de verdade.
/// </summary>
public static partial class Autoteste
{
    /// <summary>Compila para uma placa e roda o setup(): o que saiu no serial, ou o erro.</summary>
    static (string Serial, string Erro) RodarProjeto(string sketch, string placa = "esp32-devkit-v1",
                                                  IDictionary<string, string> abas = null,
                                                  IReadOnlyList<BibliotecaDeFora> bibliotecas = null)
    {
        try
        {
            var prog = Compilador.CompilarProjeto(new Projeto
            {
                Sketch = sketch,
                Arquivos = abas,
                Placa = placa,
                Bibliotecas = bibliotecas ?? [],
                DoSimulador = Acervo.EhDoSimulador,
            });
            var maq = new Maquina { Perfil = PerfilPlaca.De(placa) ?? PerfilPlaca.Esp32 };
            string Serial() => string.Join("|", maq.Serial.Select(
                l => l.Contains("] ") ? l[(l.IndexOf("] ") + 2)..] : l));
            var interp = new Interpretador(prog, maq);

            // Numa thread com pilha folgada, como a janela roda o sketch: a
            // recursao funda tem que estourar a pilha da PLACA, e nao a do PC.
            Exception falha = null;
            bool reiniciou = false;
            var thread = new Thread(() =>
            {
                try
                {
                    interp.Preparar();
                    interp.Setup();
                }
                // A placa reiniciou (panico, ESP.restart): o que saiu no serial
                // e, no lugar do erro, "reiniciou: " e o aviso do console.
                catch (ReinicioDaPlaca) { reiniciou = true; }
                catch (Exception e) { falha = e; }
            }, 256 << 20);
            thread.Start();
            thread.Join();
            if (falha is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(falha).Throw();
            if (reiniciou) return (Serial(), "reiniciou: " + string.Join(" / ", maq.Avisos));
            return (Serial(), null);
        }
        catch (Exception e) when (e is ErroDeSintaxe or ErroDeExecucao) { return (null, e.Message); }
    }

    static void EscreverArquivo(string pasta, string relativo, string texto)
    {
        string caminho = Path.Combine(pasta, relativo.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(caminho));
        File.WriteAllText(caminho, texto);
    }

    /// <summary>
    /// O pre-processador de verdade: "#ifdef ESP32" escolhe um lado pela
    /// placa, "#error" para a compilacao, macro com parametros funciona.
    /// Antes os dois lados de todo #if rodavam juntos.
    /// </summary>
    static void Motor_PreProcessador()
    {
        saida.AppendLine("pre-processador");

        const string porPlaca = """
            void setup() {
            #ifdef ESP32
              Serial.println("esp32");
            #elif defined(ARDUINO_ARCH_AVR) && ARDUINO >= 100
              Serial.println("avr");
            #else
              Serial.println("outra");
            #endif
            }
            void loop() {}
            """;
        var (naEsp, e1) = RodarProjeto(porPlaca, "esp32-devkit-v1");
        var (noUno, e2) = RodarProjeto(porPlaca, "arduino-uno");
        var (naPico, e3) = RodarProjeto(porPlaca, "pi-pico");
        saida.AppendLine($"        esp32 -> {naEsp ?? e1} | uno -> {noUno ?? e2} | pico -> {naPico ?? e3}");
        Conferir("#ifdef: a ESP32 ve o lado dela", naEsp == "esp32", naEsp ?? e1);
        Conferir("#elif: o Uno ve o do AVR", noUno == "avr", noUno ?? e2);
        Conferir("#else: a Pico cai no resto", naPico == "outra", naPico ?? e3);

        var (versao, e4) = RodarProjeto("""
            void setup() {
            #if ESP_ARDUINO_VERSION >= ESP_ARDUINO_VERSION_VAL(3, 0, 0) && defined(CONFIG_IDF_TARGET_ESP32S3)
              Serial.println("core 3 na S3");
            #endif
            }
            void loop() {}
            """, "esp32-s3-devkit");
        Conferir("#if: a versao do core e a variante do chip", versao == "core 3 na S3", versao ?? e4);

        var (fora, e5) = RodarProjeto("""
            #if 0
            isto nem e C++ @@@
            #endif
            void setup() { Serial.println("ok"); }
            void loop() {}
            """);
        Conferir("#if 0: o que esta dentro nem e lido", fora == "ok", fora ?? e5);

        const string soEsp = """
            #ifndef ESP32
            #error "Este sketch e so para ESP32"
            #endif
            void setup() { Serial.println("rodou"); }
            void loop() {}
            """;
        var (_, erro) = RodarProjeto(soEsp, "arduino-uno");
        var (rodou, semErro) = RodarProjeto(soEsp, "esp32-devkit-v1");
        saida.AppendLine($"        #error no Uno: {erro}");
        Conferir("#error: para a compilacao com a mensagem, como o compilador",
            erro?.Contains("Este sketch e so para ESP32") == true, erro);
        Conferir("#error: do lado que nao vale, nao atrapalha", rodou == "rodou", rodou ?? semErro);

        var (macros, e6) = RodarProjeto("""
            #define DOBRO(x) ((x) * 2)
            #define NOME(x) #x
            #define JUNTA(a, b) a##b
            #define LOG(fmt, ...) Serial.printf(fmt, ##__VA_ARGS__)
            #define VERSAO 3
            #undef VERSAO
            #ifndef VERSAO
            #define VERSAO 4
            #endif
            int valor12 = 7;
            void setup() {
              Serial.println(DOBRO(1 + 2));
              Serial.println(NOME(ola));
              Serial.println(JUNTA(valor, 12));
              LOG("sem nada\n");
              LOG("com %d\n", 5);
              Serial.println(VERSAO);
            }
            void loop() {}
            """);
        saida.AppendLine($"        macros -> {macros ?? e6}");
        Conferir("macro com parametros, #x, a##b, __VA_ARGS__ e #undef",
            macros == "6|ola|7|sem nada|com 5|4", macros ?? e6);

        var (bits, e7) = RodarProjeto("""
            const uint8_t tabela[] PROGMEM = {10, 20, 30};
            void setup() {
              byte x = 0;
              bitSet(x, 3);
              bitWrite(x, 0, 1);
              Serial.println(x);
              Serial.println(bitRead(x, 3));
              Serial.println(highByte(0x1234), HEX);
              Serial.println(bit(4));
              Serial.println(pgm_read_byte(&tabela[2]));
            }
            void loop() {}
            """, "arduino-uno");
        Conferir("Arduino.h: bitSet, bitRead, highByte, bit e pgm_read_byte",
            bits == "9|1|12|16|30", bits ?? e7);

        var (proto, e8) = RodarProjeto("""
            void piscar(int vezes = 2);
            int dobro(int x);
            void setup() { piscar(); Serial.println(dobro(21)); }
            void loop() {}
            void piscar(int vezes) { for (int i = 0; i < vezes; i++) Serial.println("pisca"); }
            int dobro(int x) { return x * 2; }
            """);
        Conferir("prototipo: a funcao vem depois, com o valor padrao do prototipo",
            proto == "pisca|pisca|42", proto ?? e8);

        var (_, semFim) = RodarProjeto("#ifdef ESP32\nvoid setup() {}\nvoid loop() {}\n");
        Conferir("#if sem #endif e erro, como no compilador", semFim?.Contains("#endif") == true, semFim);

        var (linha, e9) = RodarProjeto("void setup() {\n  Serial.println(__LINE__);\n}\nvoid loop() {}\n");
        Conferir("__LINE__ e a linha do sketch", linha == "2", linha ?? e9);
    }

    /// <summary>
    /// O C++ que as bibliotecas usam por dentro: classe, heranca, virtual,
    /// ponteiro, new/delete, referencia, sobrecarga, operador, callback,
    /// template, namespace e a heranca do Print.
    /// </summary>
    static void Motor_ClassesCpp()
    {
        saida.AppendLine("classes do C++");

        var (basico, e1) = RodarProjeto("""
            class Led {
              public:
                Led(int pino, bool invertido = false) : _pino(pino), _invertido(invertido) {}
                void ligar() { _vezes++; digitalWrite(_pino, _invertido ? LOW : HIGH); }
                void desligar();
                int vezes() const { return _vezes; }
                static int criados;
              private:
                int _pino;
                bool _invertido;
                int _vezes = 0;
            };
            int Led::criados = 10;
            void Led::desligar() { digitalWrite(_pino, _invertido ? HIGH : LOW); criados++; }
            Led verde(13);
            void setup() {
              verde.ligar(); verde.ligar(); verde.desligar();
              Serial.println(verde.vezes());
              Serial.println(Led::criados);
            }
            void loop() {}
            """);
        Conferir("classe: construtor, lista de inicializacao, metodo de fora e static",
            basico == "2|11", basico ?? e1);

        var (virtualOk, e2) = RodarProjeto("""
            class Animal { public: virtual String som() { return "..."; } String falar() { return "diz " + som(); } };
            class Gato : public Animal { public: String som() override { return "miau"; } };
            Gato g;
            Animal* a = &g;
            void setup() { Serial.println(a->falar()); }
            void loop() {}
            """);
        Conferir("heranca: o virtual chama o da classe de verdade, pelo ponteiro da base",
            virtualOk == "diz miau", virtualOk ?? e2);

        var (novo, e3) = RodarProjeto("""
            class Contador { public: Contador(int i) : n(i) {} ~Contador() { Serial.println("fim"); } int n; };
            void setup() { Contador* c = new Contador(4); c->n++; Serial.println(c->n); delete c; }
            void loop() {}
            """);
        Conferir("new, seta e delete com destrutor", novo == "5|fim", novo ?? e3);

        var (referencia, e4) = RodarProjeto("""
            void trocar(int &x, int &y) { int t = x; x = y; y = t; }
            void ler(float *valor) { *valor = 2.5; }
            void mostrar(int x) { Serial.println("int"); }
            void mostrar(const char* s) { Serial.println("texto"); }
            void mostrar(float f) { Serial.println("real"); }
            void setup() {
              int p = 1, q = 2; trocar(p, q); Serial.println(p * 10 + q);
              float t = 0; ler(&t); Serial.println(t);
              mostrar(3); mostrar("oi"); mostrar(2.5);
            }
            void loop() {}
            """);
        Conferir("referencia, ponteiro de saida e sobrecarga pelo tipo",
            referencia == "21|2.50|int|texto|real", referencia ?? e4);

        var (copia, e5) = RodarProjeto("""
            struct Ponto { int x; int y; };
            class Caixa { public: int v = 1; };
            struct Nota { int freq; int dur; };
            Nota melodia[] = { {262, 4}, {294, 8} };
            class Botao { public: Botao(int p) : pino(p) {} int pino; };
            Botao botoes[] = {Botao(2), Botao(3)};
            void setup() {
              Caixa k1; Caixa k2 = k1; k2.v = 9; Serial.println(k1.v);
              Ponto pt = {3, 4}; Serial.println(pt.x + pt.y);
              Serial.println(melodia[1].freq); Serial.println(botoes[1].pino);
              melodia[0].dur = 5; Serial.println(melodia[0].dur);
            }
            void loop() {}
            """);
        Conferir("copia de objeto, struct com chaves e vetor de struct e de objeto",
            copia == "1|7|294|3|5", copia ?? e5);

        var (operadores, e6) = RodarProjeto("""
            class Vetor2 {
              public:
                int x, y;
                Vetor2(int a, int b) : x(a), y(b) {}
                Vetor2 operator+(const Vetor2& o) { return Vetor2(x + o.x, y + o.y); }
                bool operator==(const Vetor2& o) { return x == o.x && y == o.y; }
            };
            void setup() {
              Vetor2 a(1, 2), b(3, 4);
              Vetor2 c = a + b;
              Serial.println(c.x * 10 + c.y);
              Serial.println(c == Vetor2(4, 6));
            }
            void loop() {}
            """);
        Conferir("operator+ e operator== da classe", operadores == "46|1", operadores ?? e6);

        var (chamadas, e7) = RodarProjeto("""
            typedef void (*Acao)(int);
            class Gatilho {
              public:
                void aoDisparar(Acao a) { _acao = a; }
                void disparar() { if (_acao) _acao(7); }
              private:
                Acao _acao = nullptr;
            };
            void mostrarNumero(int n) { Serial.println(n); }
            Gatilho gat;
            namespace util { int dobro(int x) { return x * 2; } }
            template <typename T> class Guarda { public: void por(T v) { _v = v; } T tirar() { return _v; } private: T _v; };
            enum class Cor { VERMELHO, VERDE = 5 };
            void setup() {
              gat.disparar(); gat.aoDisparar(mostrarNumero); gat.disparar();
              void (*f)() = []() { Serial.println("lambda"); }; f();
              Serial.println(util::dobro(4));
              Guarda<float> g; g.por(2.5); Serial.println(g.tirar());
              Serial.println((int)Cor::VERDE);
            }
            void loop() {}
            """);
        Conferir("callback por ponteiro de funcao, lambda, namespace, template e enum class",
            chamadas == "7|lambda|8|2.50|5", chamadas ?? e7);

        var (impressao, e8) = RodarProjeto("""
            class Tela : public Print {
              public:
                size_t write(uint8_t c) { n++; return 1; }
                int n = 0;
            };
            void setup() { Tela t; t.print("abc"); t.println(12); Serial.println(t.n); }
            void loop() {}
            """);
        Conferir("classe derivada do Print: print e println saem pelo write() dela",
            impressao == "7", impressao ?? e8);

        var (intervalo, e9) = RodarProjeto("""
            int leituras[] = {3, 5, 7};
            class Led { public: Led(int p) : pino(p) {} int pino; void dobrar() { pino *= 2; } };
            Led leds[] = {Led(1), Led(2)};
            void setup() {
              int soma = 0;
              for (int x : leituras) soma += x;
              Serial.println(soma);
              for (auto &x : leituras) x = x * 10;
              Serial.println(leituras[2]);
              for (Led &l : leds) l.dobrar();
              Serial.println(leds[1].pino);
              for (int i = 0, j = 10; i < 3; i++, j--) { if (i == 2) Serial.println(j); }
            }
            void loop() {}
            """);
        Conferir("for (x : vetor), com e sem referencia, e a virgula no passo do for",
            intervalo == "15|70|4|8", intervalo ?? e9);

        // A recursao sem fim estoura a pilha da placa -- a do loopTask (8 KB)
        // na ESP32, que para com o Guru Meditation do "Stack canary"; no Uno a
        // pilha encontra a heap. Nas duas a placa reinicia, e o simulador segue.
        const string semFim = """
            int sem_fim(int x) { return sem_fim(x + 1); }
            void setup() { sem_fim(0); }
            void loop() {}
            """;
        var (guru, pilha) = RodarProjeto(semFim);
        var (_, pilhaNoUno) = RodarProjeto(semFim, "arduino-uno");
        saida.AppendLine($"        recursao sem fim: {pilha} / uno: {pilhaNoUno}");
        Conferir("recursao sem fim estoura a pilha da placa (Stack canary na ESP32, a heap no Uno), e nao derruba o programa",
            guru?.Contains("Stack canary watchpoint triggered (loopTask)") == true && pilha?.StartsWith("reiniciou") == true
            && pilhaNoUno?.Contains("a pilha encostou na heap") == true, $"{guru} / {pilha} / {pilhaNoUno}");

        var (_, semCorpo) = RodarProjeto("""
            void falta(int x);
            void setup() { falta(1); }
            void loop() {}
            """);
        Conferir("funcao so declarada: o erro diz que falta o corpo",
            semCorpo?.Contains("nao tem corpo") == true, semCorpo);
    }

    /// <summary>
    /// A biblioteca de fora de ponta a ponta: instalar (pasta e .zip), o
    /// #include achar, o .cpp dela compilar junto, o erro dizer o arquivo
    /// e a linha, e a do simulador vencer a copia instalada.
    /// </summary>
    static void Bibliotecas_DeFora()
    {
        saida.AppendLine("bibliotecas de fora");
        string raiz = Path.Combine(Path.GetTempPath(), $"cs-libs-{Guid.NewGuid():N}");
        var instaladas = new List<string>();
        MainWindow janela = null;

        try
        {
            // ---- formato novo: library.properties + src/, com subpasta ----
            string botao = Path.Combine(raiz, "BotaoSimples");
            EscreverArquivo(botao, "library.properties",
                "name=BotaoSimples\nversion=1.0.0\nsentence=Botao com debounce e callback.\n");
            EscreverArquivo(botao, "src/BotaoSimples.h", """
                #ifndef BOTAO_SIMPLES_H
                #define BOTAO_SIMPLES_H
                #include <Arduino.h>
                #include "util/Filtro.h"

                class BotaoSimples {
                  public:
                    typedef void (*Acao)();
                    BotaoSimples(uint8_t pino, unsigned long espera = 30);
                    void begin();
                    void aoApertar(Acao acao) { _acao = acao; }
                    void tratar(bool nivel);
                    uint16_t apertos() const { return _apertos; }
                  private:
                    uint8_t _pino;
                    Filtro _filtro;
                    Acao _acao = nullptr;
                    uint16_t _apertos = 0;
                    bool _antes = true;
                };
                #endif
                """);
            EscreverArquivo(botao, "src/util/Filtro.h", """
                #pragma once
                class Filtro {
                  public:
                    explicit Filtro(unsigned long espera = 20) : _espera(espera) {}
                    bool estavel(bool nivel, unsigned long agora);
                  private:
                    unsigned long _espera;
                    unsigned long _desde = 0;
                    bool _ultimo = true;
                };
                """);
            EscreverArquivo(botao, "src/BotaoSimples.cpp", """
                #include "BotaoSimples.h"

                BotaoSimples::BotaoSimples(uint8_t pino, unsigned long espera)
                  : _pino(pino), _filtro(espera) {}

                void BotaoSimples::begin() {
                  pinMode(_pino, INPUT_PULLUP);
                }

                void BotaoSimples::tratar(bool nivel) {
                  if (!_filtro.estavel(nivel, millis())) return;
                  if (_antes && !nivel) {
                    _apertos++;
                    if (_acao != nullptr) _acao();
                  }
                  _antes = nivel;
                }
                """);
            EscreverArquivo(botao, "src/util/Filtro.cpp", """
                #include "Filtro.h"
                #include <Arduino.h>

                bool Filtro::estavel(bool nivel, unsigned long agora) {
                  if (nivel != _ultimo) { _ultimo = nivel; _desde = agora; return false; }
                  return agora - _desde >= _espera;
                }
                """);
            // Exemplo quebrado de proposito: a IDE nao compila examples/, e o simulador tambem nao.
            EscreverArquivo(botao, "examples/Basico/Basico.ino", "isto nao compila @@@");

            // ---- formato antigo: .h e .cpp na raiz, com namespace e static ----
            string contador = Path.Combine(raiz, "Contador");
            EscreverArquivo(contador, "Contador.h", """
                #ifndef CONTADOR_H
                #define CONTADOR_H
                namespace medidas {
                  class Contador {
                    public:
                      static int total;
                      Contador() { total++; }
                      int dobro(int x) const;
                  };
                }
                #endif
                """);
            EscreverArquivo(contador, "Contador.cpp", """
                #include "Contador.h"
                namespace medidas {
                  int Contador::total = 0;
                  int Contador::dobro(int x) const { return x * 2; }
                }
                """);

            // ---- uma que recusa a placa, e uma que quebra rodando ----
            string soEsp = Path.Combine(raiz, "SoEsp32");
            EscreverArquivo(soEsp, "SoEsp32.h", """
                #pragma once
                #ifndef ESP32
                #error "SoEsp32 so roda na ESP32"
                #endif
                inline int soEsp() { return 32; }
                """);

            string quebrada = Path.Combine(raiz, "Quebrada");
            EscreverArquivo(quebrada, "Quebrada.h", "#pragma once\nint dividir(int a, int b);\n");
            EscreverArquivo(quebrada, "Quebrada.cpp", "#include \"Quebrada.h\"\n\nint dividir(int a, int b) {\n  return a / b;\n}\n\n"
                                                     + "int ler(int i) {\n  int v[2] = {1, 2};\n  return v[i];\n}\n");

            // ---- uma Servo "de fora": a do simulador tem que vencer ----
            string servoFalso = Path.Combine(raiz, "Servo");
            EscreverArquivo(servoFalso, "Servo.h", """
                #pragma once
                class Servo { public: void attach(int p) {} void write(int a) {} int read() { return 12345; } };
                """);

            foreach (string pasta in new[] { botao, contador, soEsp, quebrada, servoFalso })
                instaladas.Add(Acervo.Instalar(pasta));

            // ---- o .zip do GitHub: uma pasta "-master" dentro, nome com espaco ----
            string zip = Path.Combine(raiz, "PiscaPisca-master.zip");
            using (var arquivo = System.IO.Compression.ZipFile.Open(zip, System.IO.Compression.ZipArchiveMode.Create))
            {
                void Entrada(string nome, string texto)
                {
                    using var w = new StreamWriter(arquivo.CreateEntry(nome).Open());
                    w.Write(texto);
                }
                Entrada("PiscaPisca-master/library.properties", "name=Pisca Pisca\nversion=0.1\n");
                Entrada("PiscaPisca-master/src/PiscaPisca.h", """
                    #pragma once
                    class PiscaPisca {
                      public:
                        const char* placa() {
                        #if defined(ESP32)
                          return "esp32";
                        #elif defined(ARDUINO_ARCH_AVR)
                          return "avr";
                        #else
                          return "outra";
                        #endif
                        }
                    };
                    """);
            }
            string doZip = Acervo.InstalarZip(zip);
            instaladas.Add(doZip);
            saida.AppendLine($"        instaladas: {string.Join(", ", instaladas)}");
            Conferir("zip: o nome vem do library.properties, sem espaco", doZip == "Pisca_Pisca", doZip);

            // .zip com caminho que foge da pasta: recusado.
            string malicioso = Path.Combine(raiz, "fuga.zip");
            using (var arquivo = System.IO.Compression.ZipFile.Open(malicioso, System.IO.Compression.ZipArchiveMode.Create))
            {
                using var w = new StreamWriter(arquivo.CreateEntry("../../fora.h").Open());
                w.Write("int x;");
            }
            string recusa = null;
            try { Acervo.InstalarZip(malicioso); }
            catch (InvalidOperationException e) { recusa = e.Message; }
            Conferir("zip: caminho que sai da pasta e recusado", recusa is not null, "instalou");

            // ---- o acervo ----
            var lista = Acervo.Instaladas();
            var doBotao = lista.FirstOrDefault(b => b.Nome == "BotaoSimples");
            Conferir("acervo: a instalada aparece, dizendo que roda o codigo dela",
                doBotao is not null && doBotao.Resumo.Contains("roda o código C++") && doBotao.Resumo.Contains("debounce"),
                doBotao?.Resumo);

            var paraCompilar = Acervo.ParaCompilar();
            var libBotao = paraCompilar.FirstOrDefault(b => b.Nome == "BotaoSimples");
            var libContador = paraCompilar.FirstOrDefault(b => b.Nome == "Contador");
            Conferir("acervo: formato novo le de src/, o antigo da raiz",
                libBotao?.Raiz == "src/" && libContador?.Raiz == "", $"{libBotao?.Raiz}|{libContador?.Raiz}");
            Conferir("acervo: examples/ fica de fora",
                libBotao is not null && !libBotao.Arquivos.Any(a => a.StartsWith("examples/")),
                string.Join(",", libBotao?.Arquivos ?? []));

            var semDono = Acervo.SemDono("#include <BotaoSimples.h>\n#include <PiscaPisca.h>\n#include <Contador.h>\nvoid setup() {}");
            Conferir("acervo: include de biblioteca instalada nao e mais 'faltando'",
                semDono.Count == 0, string.Join(",", semDono));

            // ---- rodando: o sketch usa as tres ----
            const string sketch = """
                #include <BotaoSimples.h>
                #include <Contador.h>
                #include <PiscaPisca.h>

                BotaoSimples botao(4, 10);
                medidas::Contador a, b;
                PiscaPisca pisca;

                void contou() { Serial.println("apertou"); }

                void setup() {
                  Serial.begin(115200);
                  botao.begin();
                  botao.aoApertar(contou);
                  bool niveis[] = {true, true, false, false, false, true, true};
                  for (int i = 0; i < 7; i++) { botao.tratar(niveis[i]); delay(10); }
                  Serial.println(botao.apertos());
                  Serial.println(medidas::Contador::total);
                  Serial.println(a.dobro(21));
                  Serial.println(pisca.placa());
                }

                void loop() {}
                """;
            var (naEsp, e1) = RodarProjeto(sketch, "esp32-devkit-v1", bibliotecas: paraCompilar);
            var (noUno, e2) = RodarProjeto(sketch, "arduino-uno", bibliotecas: paraCompilar);
            saida.AppendLine($"        esp32 -> {naEsp ?? e1}");
            saida.AppendLine($"        uno   -> {noUno ?? e2}");
            Conferir("biblioteca de fora: classes, .cpp da subpasta, callback e static rodam",
                naEsp == "apertou|1|2|42|esp32", naEsp ?? e1);
            Conferir("biblioteca de fora: o #if dela ve a placa", noUno?.EndsWith("|avr") == true, noUno ?? e2);

            var (_, recusou) = RodarProjeto("#include <SoEsp32.h>\nvoid setup() {}\nvoid loop() {}\n",
                                        "arduino-uno", bibliotecas: paraCompilar);
            Conferir("biblioteca de fora: o #error dela diz o arquivo e o motivo",
                recusou?.Contains("SoEsp32/SoEsp32.h") == true && recusou.Contains("so roda na ESP32"), recusou);

            var (_, quebrou) = RodarProjeto("#include <Quebrada.h>\nint ler(int i);\nvoid setup() { ler(5); }\nvoid loop() {}\n",
                                        bibliotecas: paraCompilar);
            saida.AppendLine($"        erro dentro dela: {quebrou}");
            Conferir("biblioteca de fora: erro rodando aponta o arquivo e a linha dela",
                quebrou?.StartsWith("Quebrada/Quebrada.cpp, linha 9") == true, quebrou);

            // A divisao inteira por zero nao e mensagem de erro: na ESP32 e o
            // Guru Meditation, e a placa reinicia. O console diz onde foi.
            var (guru, reiniciou) = RodarProjeto("#include <Quebrada.h>\nvoid setup() { dividir(1, 0); }\nvoid loop() {}\n",
                                                 bibliotecas: paraCompilar);
            Conferir("biblioteca de fora: divisao por zero dentro dela reinicia a ESP32, e o aviso aponta a linha",
                guru?.Contains("panic'ed (IntegerDivideByZero)") == true
                && reiniciou?.Contains("Quebrada/Quebrada.cpp, linha 4") == true, $"{guru} / {reiniciou}");

            var (servo, e3) = RodarProjeto("""
                #include <Servo.h>
                Servo s;
                void setup() { s.attach(13); s.write(90); Serial.println(s.read()); }
                void loop() {}
                """, bibliotecas: paraCompilar);
            Conferir("a Servo do simulador vence a copia instalada", servo == "90", servo ?? e3);

            // ---- a linha do sketch nao anda por causa do que foi incluido ----
            var abas = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["config.h"] = "#pragma once\nconst int PINO = 4;\nconst int ESPERA = 10;\n",
            };
            var (_, naLinha) = RodarProjeto("""
                #include "config.h"
                #include <BotaoSimples.h>

                void setup() {
                  inexistente++;
                }
                void loop() {}
                """, abas: abas, bibliotecas: paraCompilar);
            Conferir("erro no sketch: a linha e a do editor, mesmo com #include antes",
                naLinha?.StartsWith("linha 5:") == true, naLinha);

            // ---- no programa, pelo botao Rodar ----
            janela = new MainWindow();
            var circuito = new Circuito();
            circuito.Pecas.Add(new PecaNoCircuito { Id = "placa", Tipo = "esp32-devkit-v1", X = 0, Y = 0 });
            janela.TesteAbrir(circuito, sketch);
            janela.TesteRodar();
            bool rodouNoPrograma = EsperarPor(() =>
            {
                janela.TesteAtualizarPaineis();
                return janela.TesteSerial.Contains("esp32");
            }, 6000);
            Conferir("no programa: o Rodar compila a biblioteca instalada junto",
                rodouNoPrograma && janela.TesteSerial.Contains("apertou"), janela.TesteSerial + " | " + janela.TesteStatus);
            janela.TesteParar();
        }
        finally
        {
            try { janela?.Close(); } catch (InvalidOperationException) { }

            foreach (var b in Acervo.Instaladas().Where(b => instaladas.Contains(b.Nome)))
                try { Acervo.Remover(b); } catch (IOException) { }

            try { if (Directory.Exists(raiz)) Directory.Delete(raiz, recursive: true); }
            catch (IOException) { }
        }
    }
}
