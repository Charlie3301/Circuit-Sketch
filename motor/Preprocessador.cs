namespace Simulador;

/// <summary>
/// De que arquivo veio cada linha.
///
/// O sketch e o arquivo 0, e as linhas dele continuam sendo o numero que
/// o editor mostra. Os outros -- a aba config.h, o Botao.cpp de uma
/// biblioteca instalada -- ganham um numero proprio, e a linha deles viaja
/// codificada: arquivo * Faixa + linha.
///
/// E o que deixa a mensagem de erro dizer "Botao.cpp, linha 12" sem mudar
/// o tipo de nenhum no da arvore. Antes os arquivos do projeto eram
/// colados na frente do sketch, e todo erro do sketch apontava uma linha
/// deslocada pelo tamanho do que foi colado.
/// </summary>
public static class Origens
{
    public const int Faixa = 1_000_000;

    static readonly object trava = new();
    static readonly List<string> nomes = [null];
    static readonly Dictionary<string, int> porChave = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// O numero deste arquivo. A mesma chave da sempre o mesmo numero,
    /// entao rodar o sketch cem vezes nao enche a tabela.
    /// </summary>
    public static int Registrar(string chave, string nome)
    {
        lock (trava)
        {
            if (porChave.TryGetValue(chave, out int ja))
            {
                nomes[ja] = nome;
                return ja;
            }

            nomes.Add(nome);
            porChave[chave] = nomes.Count - 1;
            return nomes.Count - 1;
        }
    }

    public static int Codificar(int arquivo, int linha) => arquivo * Faixa + linha;

    /// <summary>A linha e do sketch que esta no editor?</summary>
    public static bool DoSketch(int linha) => linha < Faixa;

    public static int Arquivo(int linha) => linha < Faixa ? 0 : linha / Faixa;

    public static int Linha(int linha) => linha < Faixa ? linha : linha % Faixa;

    public static string Nome(int arquivo)
    {
        lock (trava) return arquivo > 0 && arquivo < nomes.Count ? nomes[arquivo] : null;
    }

    /// <summary>"linha 12" no sketch, "Botao.cpp, linha 12" nos outros.</summary>
    public static string Onde(int linha)
        => DoSketch(linha)
            ? $"linha {linha}"
            : $"{Nome(Arquivo(linha)) ?? "outro arquivo"}, linha {Linha(linha)}";
}

/// <summary>Um arquivo que o #include achou.</summary>
public sealed record ArquivoDeCodigo(int Indice, string Nome, string Texto);

/// <summary>Um #define: com parametros ou sem.</summary>
public sealed class Macro
{
    public string Nome { get; init; }

    /// <summary>Null numa macro sem parenteses ("#define LED 13").</summary>
    public List<string> Parametros { get; init; }

    /// <summary>"#define LOG(fmt, ...)": o resto dos argumentos vira __VA_ARGS__.</summary>
    public bool Variadica { get; init; }

    public List<Token> Corpo { get; init; } = [];
}

/// <summary>
/// O estado do pre-processador ao longo de um arquivo: as macros
/// definidas ate aqui e onde procurar o que o #include pede.
///
/// Antes o simulador ignorava toda diretiva que nao fosse #define. Um
/// "#ifdef ESP32" deixava passar os DOIS lados do #else, e o sketch que
/// na placa compila so um deles aqui rodava os dois. Com isto, cada placa
/// ve o que o core dela define -- o mesmo que o compilador de verdade.
/// </summary>
public sealed class Preprocessador
{
    public Dictionary<string, Macro> Macros { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Acha o arquivo de um #include: (nome, veio com aspas, arquivo de
    /// quem pediu). Null quando nao e arquivo -- a biblioteca que o
    /// simulador executa por dentro, ou uma que ninguem tem.
    /// </summary>
    public Func<string, bool, int, ArquivoDeCodigo> Resolver { get; init; }

    /// <summary>Cabecalho que existe sem ser arquivo, para o __has_include.</summary>
    public Func<string, bool> Conhecido { get; init; }

    /// <summary>Arquivos com #pragma once que ja entraram.</summary>
    public HashSet<int> UmaVezSo { get; } = [];

    /// <summary>Os #warning, para o console mostrar.</summary>
    public List<string> Avisos { get; } = [];

    internal int Fundo;

    public void Definir(string nome, string corpo = "1")
        => Macros[nome] = new Macro { Nome = nome, Corpo = Lexer.TokensDeTexto(corpo, 0) };

    public void DefinirComParametros(string nome, string[] parametros, string corpo)
        => Macros[nome] = new Macro
        {
            Nome = nome,
            Parametros = [.. parametros.Select(x => x == "..." ? "__VA_ARGS__" : x)],
            Variadica = parametros.Contains("..."),
            Corpo = Lexer.TokensDeTexto(corpo, 0),
        };

    /// <summary>
    /// O pre-processador de um sketch desta placa, com as macros que o
    /// core dela define. Null na placa vale como "Arduino sem placa": so o
    /// ARDUINO, que todo core define.
    /// </summary>
    public static Preprocessador DaPlaca(string idDaPlaca, Linguagem linguagem,
                                         Func<string, bool, int, ArquivoDeCodigo> resolver = null,
                                         Func<string, bool> conhecido = null)
    {
        var pp = new Preprocessador { Resolver = resolver, Conhecido = conhecido };

        // O C puro do SDK da Pico nao passa pelo core do Arduino: la nao
        // existe ARDUINO, e o "#ifdef ARDUINO" de uma biblioteca tem que
        // cair no outro lado.
        if (linguagem != Linguagem.Arduino) return pp;

        foreach (var (nome, valor) in MacrosDoCore(idDaPlaca)) pp.Definir(nome, valor);

        pp.DefinirComParametros("ESP_ARDUINO_VERSION_VAL", ["major", "minor", "patch"],
                                "((major << 16) | (minor << 8) | (patch))");

        // As macros do Arduino.h, escritas como la. Sao macros de verdade no
        // core -- "bitRead(x, 3)" vira a conta antes de compilar --, e por
        // isso moram aqui, e nao no interpretador.
        foreach (var (nome, parametros, corpo) in MacrosDoArduinoH)
        {
            if (parametros is null) pp.Definir(nome, corpo);
            else pp.DefinirComParametros(nome, parametros, corpo);
        }

        // O log do ESP-IDF e o do core. No nivel de depuracao que a IDE usa
        // por padrao ("None"), eles nao imprimem nada -- e e o que fazem aqui.
        if (pp.Macros.ContainsKey("ESP32") || pp.Macros.ContainsKey("ESP8266"))
        {
            foreach (string log in new[] { "ESP_LOGE", "ESP_LOGW", "ESP_LOGI", "ESP_LOGD", "ESP_LOGV",
                                           "ESP_EARLY_LOGE", "ESP_EARLY_LOGW", "ESP_EARLY_LOGI",
                                           "log_e", "log_w", "log_i", "log_d", "log_v", "log_n",
                                           "isr_log_e", "isr_log_w", "isr_log_i", "isr_log_d", "isr_log_v" })
                pp.Macros[log] = new Macro { Nome = log, Parametros = ["__VA_ARGS__"], Variadica = true };
            pp.DefinirComParametros("ESP_ERROR_CHECK", ["x"], "(x)");
        }
        return pp;
    }

    static readonly (string Nome, string[] Parametros, string Corpo)[] MacrosDoArduinoH =
    [
        ("bitRead", ["value", "bit"], "(((value) >> (bit)) & 0x01)"),
        ("bitSet", ["value", "bit"], "((value) |= (1UL << (bit)))"),
        ("bitClear", ["value", "bit"], "((value) &= ~(1UL << (bit)))"),
        ("bitToggle", ["value", "bit"], "((value) ^= (1UL << (bit)))"),
        ("bitWrite", ["value", "bit", "bitvalue"], "((bitvalue) ? bitSet(value, bit) : bitClear(value, bit))"),
        ("bit", ["b"], "(1UL << (b))"),
        ("lowByte", ["w"], "((uint8_t) ((w) & 0xff))"),
        ("highByte", ["w"], "((uint8_t) ((w) >> 8))"),
        ("_BV", ["bit"], "(1 << (bit))"),
        ("clockCyclesPerMicrosecond", [], "( F_CPU / 1000000L )"),
        ("clockCyclesToMicroseconds", ["a"], "( (a) / clockCyclesPerMicrosecond() )"),
        ("microsecondsToClockCycles", ["a"], "( (a) * clockCyclesPerMicrosecond() )"),
        ("DEG_TO_RAD", null, "0.017453292519943295769236907684886"),
        ("RAD_TO_DEG", null, "57.295779513082320876798154814105"),
        ("HALF_PI", null, "1.5707963267948966192313216916398"),
        ("TWO_PI", null, "6.283185307179586476925286766559"),
        ("EULER", null, "2.718281828459045235360287471352"),

        // A memoria de programa: na placa o dado mora na flash e se le por
        // estas funcoes; aqui ele mora onde sempre morou, e ler e ler.
        ("pgm_read_byte", ["addr"], "(*(addr))"),
        ("pgm_read_word", ["addr"], "(*(addr))"),
        ("pgm_read_dword", ["addr"], "(*(addr))"),
        ("pgm_read_float", ["addr"], "(*(addr))"),
        ("pgm_read_ptr", ["addr"], "(*(addr))"),
        ("pgm_read_byte_near", ["addr"], "(*(addr))"),
        ("pgm_read_word_near", ["addr"], "(*(addr))"),
        ("PSTR", ["s"], "(s)"),
        ("FPSTR", ["s"], "(s)"),
        ("memcpy_P", ["d", "s", "n"], "memcpy(d, s, n)"),
        ("strcpy_P", ["d", "s"], "strcpy(d, s)"),
        ("strncpy_P", ["d", "s", "n"], "strncpy(d, s, n)"),
        ("strcmp_P", ["a", "b"], "strcmp(a, b)"),
        ("strlen_P", ["s"], "strlen(s)"),
        ("sprintf_P", ["d", "..."], "sprintf(d, __VA_ARGS__)"),
        ("snprintf_P", ["d", "n", "..."], "snprintf(d, n, __VA_ARGS__)"),

        // "cli()" e "sei()" sao o noInterrupts()/interrupts() do AVR.
        ("cli", [], "noInterrupts()"),
        ("sei", [], "interrupts()"),
    ];

    /// <summary>
    /// As macros que o core de cada placa define na linha de comando do
    /// compilador. Os nomes sao os de verdade: e com eles que as
    /// bibliotecas escolhem o codigo de cada arquitetura.
    /// </summary>
    public static IEnumerable<(string Nome, string Valor)> MacrosDoCore(string id)
    {
        // A IDE 2 e o arduino-cli passam 10607; e o numero que um
        // "#if ARDUINO >= 100" compara.
        yield return ("ARDUINO", "10607");

        var perfil = id is null ? null : PerfilPlaca.De(id);
        if (perfil is null) yield break;

        string processador = perfil.Processador ?? "";

        if (id.StartsWith("esp32", StringComparison.Ordinal))
        {
            string variante = id switch
            {
                "esp32-s2-devkit" => "ESP32S2",
                "esp32-s3-devkit" => "ESP32S3",
                "esp32-c3-devkit" => "ESP32C3",
                "esp32-c6-devkit" => "ESP32C6",
                "esp32-h2-devkit" => "ESP32H2",
                _ => "ESP32",
            };

            yield return ("ESP32", "1");
            yield return ("ARDUINO_ARCH_ESP32", "1");
            yield return ("ESP_PLATFORM", "1");
            yield return ("CONFIG_IDF_TARGET_" + variante, "1");
            yield return (variante == "ESP32" ? "ARDUINO_ESP32_DEV" : $"ARDUINO_{variante}_DEV", "1");
            yield return ("ARDUINO_BOARD", variante == "ESP32" ? "\"ESP32_DEV\"" : $"\"{variante}_DEV\"");
            yield return ("ARDUINO_VARIANT", "\"" + variante.ToLowerInvariant() + "\"");
            yield return ("F_CPU", ((long)(perfil.Megahertz * 1_000_000)) + "L");
            yield return ("ESP_ARDUINO_VERSION_MAJOR", "3");
            yield return ("ESP_ARDUINO_VERSION_MINOR", "0");
            yield return ("ESP_ARDUINO_VERSION_PATCH", "7");
            yield return ("ESP_ARDUINO_VERSION", "((3 << 16) | (0 << 8) | 7)");
            yield return ("__cplusplus", "202100L");

            // O esp_attr.h, que o Arduino.h da ESP32 inclui: a memoria RTC
            // continua ligada no sono profundo.
            yield return ("RTC_DATA_ATTR", "__attribute__((section(\".rtc.data\")))");
            yield return ("RTC_RODATA_ATTR", "__attribute__((section(\".rtc.rodata\")))");
            yield return ("RTC_NOINIT_ATTR", "__attribute__((section(\".rtc_noinit\")))");
            yield return ("RTC_FAST_ATTR", "__attribute__((section(\".rtc.force_fast\")))");
            yield return ("RTC_SLOW_ATTR", "__attribute__((section(\".rtc.force_slow\")))");
            yield break;
        }

        if (id.StartsWith("esp8266", StringComparison.Ordinal) || id.StartsWith("esp-", StringComparison.Ordinal))
        {
            yield return ("ESP8266", "1");
            yield return ("ARDUINO_ARCH_ESP8266", "1");
            yield return (id == "esp8266-nodemcu" ? "ARDUINO_ESP8266_NODEMCU_ESP12E" : "ARDUINO_ESP8266_GENERIC", "1");
            yield return ("ARDUINO_BOARD", id == "esp8266-nodemcu" ? "\"ESP8266_NODEMCU_ESP12E\"" : "\"ESP8266_GENERIC\"");
            yield return ("F_CPU", "80000000L");
            yield return ("__cplusplus", "201703L");
            yield break;
        }

        if (processador == "RP2040")
        {
            yield return ("ARDUINO_ARCH_RP2040", "1");
            yield return ("PICO_RP2040", "1");
            yield return (id switch
            {
                "pi-pico-w" => "ARDUINO_RASPBERRY_PI_PICO_W",
                "nano-rp2040-connect" => "ARDUINO_NANO_RP2040_CONNECT",
                _ => "ARDUINO_RASPBERRY_PI_PICO",
            }, "1");
            yield return ("F_CPU", "133000000L");
            yield return ("__cplusplus", "201703L");
            yield break;
        }

        if (id.StartsWith("stm32", StringComparison.Ordinal) || id.StartsWith("st-nucleo", StringComparison.Ordinal))
        {
            yield return ("ARDUINO_ARCH_STM32", "1");
            switch (id)
            {
                case "st-nucleo-l031k6":
                    yield return ("ARDUINO_NUCLEO_L031K6", "1");
                    yield return ("STM32L0xx", "1");
                    yield return ("STM32L031xx", "1");
                    yield return ("F_CPU", "32000000L");
                    break;
                case "st-nucleo-c031c6":
                    yield return ("ARDUINO_NUCLEO_C031C6", "1");
                    yield return ("STM32C0xx", "1");
                    yield return ("STM32C031xx", "1");
                    yield return ("F_CPU", "48000000L");
                    break;
                default:
                    yield return ("ARDUINO_BLUEPILL_F103C8", "1");
                    yield return ("STM32F1xx", "1");
                    yield return ("STM32F103xB", "1");
                    yield return ("F_CPU", "72000000L");
                    break;
            }
            yield return ("__cplusplus", "201703L");
            yield break;
        }

        // O resto e AVR: Uno, Nano, Mega e os ATtiny85.
        yield return ("ARDUINO_ARCH_AVR", "1");
        yield return ("__AVR__", "1");
        yield return ("__AVR", "1");
        yield return ("AVR", "1");
        yield return ("__cplusplus", "201103L");

        switch (id)
        {
            case "arduino-mega":
                yield return ("ARDUINO_AVR_MEGA2560", "1");
                yield return ("__AVR_ATmega2560__", "1");
                yield return ("__AVR_ARCH__", "6");
                yield return ("ARDUINO_BOARD", "\"AVR_MEGA2560\"");
                yield return ("F_CPU", "16000000L");
                break;
            case "arduino-nano":
                yield return ("ARDUINO_AVR_NANO", "1");
                yield return ("__AVR_ATmega328P__", "1");
                yield return ("__AVR_ARCH__", "5");
                yield return ("ARDUINO_BOARD", "\"AVR_NANO\"");
                yield return ("F_CPU", "16000000L");
                break;
            case "attiny85":
                yield return ("ARDUINO_AVR_ATTINYX5", "1");
                yield return ("__AVR_ATtiny85__", "1");
                yield return ("__AVR_ARCH__", "25");
                yield return ("F_CPU", "8000000L");
                break;
            case "franzininho":
                // A Franzininho usa o core da Digispark.
                yield return ("ARDUINO_AVR_DIGISPARK", "1");
                yield return ("__AVR_ATtiny85__", "1");
                yield return ("__AVR_ARCH__", "25");
                yield return ("F_CPU", "16500000L");
                break;
            default:
                yield return ("ARDUINO_AVR_UNO", "1");
                yield return ("__AVR_ATmega328P__", "1");
                yield return ("__AVR_ARCH__", "5");
                yield return ("ARDUINO_BOARD", "\"AVR_UNO\"");
                yield return ("F_CPU", "16000000L");
                break;
        }
    }
}
