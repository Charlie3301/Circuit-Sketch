using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Plugin do Wokwi: abre os diagram.json feitos no Wokwi e escreve o circuito no
/// formato deles.
///
/// As pecas daqui tem o nome sem prefixo ("led", "esp32-s3-devkit"); as de la
/// vem com "wokwi-" ou "board-" ("wokwi-led", "board-esp32-s3-devkitc-1"), e
/// alguns pinos tem outro nome (o EN do A4988 la e ENABLE; a DevKitC traz o
/// pino pelo numero do GPIO). Sem este plugin o programa so le e escreve os
/// nomes daqui; com ele, o arquivo do Wokwi abre, e o "Exportar" ganha a opcao
/// "para o Wokwi".
///
/// Como todo plugin do CircuitSketch: uma classe estatica com Nome, Descricao,
/// Versao, Categoria e Autor, e os ganchos que ela quiser:
///   TipoImportado(tipo, atributos, existe)   o tipo de la, no nome daqui (null: nao sabe)
///   PinoImportado(tipo, pino, pinos)         o pino de la, no nome daqui
///   TipoExportado(tipo) / PinoExportado(tipo, pino) / AtributosExportados(tipo)
///                                            o caminho de volta, para o "Exportar"
///   Formato                                  o nome do formato no "Exportar"
/// </summary>
public static class PluginWokwi
{
    public const string Nome = "Wokwi";
    public const string Descricao = "Abre os diagram.json do Wokwi (as peças \"wokwi-led\", \"board-esp32-s3-devkitc-1\" e os "
                                    + "pinos deles viram os daqui) e exporta o circuito no formato do Wokwi.";
    public const string Versao = "1.1";
    public const string Categoria = "Importação e exportação";
    public const string Autor = "CircuitSketch";
    public const string Formato = "Wokwi";

    // =====================================================================
    //  as pecas
    // =====================================================================

    /// <summary>As pecas com o mesmo nome dos dois lados: "led" aqui e "wokwi-led" la (os elementos do wokwi-elements).</summary>
    static readonly HashSet<string> MesmoNome =
    [
        "7segment", "a4988", "analog-joystick", "arduino-mega", "arduino-nano", "arduino-uno", "attiny85",
        "biaxial-stepper", "big-sound-sensor", "buzzer", "clock-generator", "dht22", "dip-switch-8", "ds1307",
        "esp32-devkit-v1", "flame-sensor", "franzininho", "gas-sensor", "hc-sr04", "heart-beat-sensor", "hx711",
        "ili9341", "ir-receiver", "ir-remote", "ks2e-m-dc5", "ky-040", "lcd1602", "lcd2004", "led", "led-bar-graph",
        "led-ring", "logic-analyzer", "membrane-keypad", "microsd-card", "mpu6050", "nano-rp2040-connect",
        "neopixel", "neopixel-matrix", "ntc-temperature-sensor", "photoresistor-sensor", "pi-pico",
        "pir-motion-sensor", "potentiometer", "pushbutton", "pushbutton-6mm", "resistor", "rgb-led",
        "rotary-dialer", "servo", "slide-potentiometer", "slide-switch", "small-sound-sensor", "ssd1306",
        "stepper-motor", "tilt-switch", "wifi-ap", "74hc595", "ds18b20",
    ];

    /// <summary>As pecas com nome diferente la (as placas novas sao "board-", nao "wokwi-"): daqui para la.</summary>
    static readonly Dictionary<string, string> Exportados = new()
    {
        ["esp32-s2-devkit"] = "board-esp32-s2-devkitm-1",
        ["esp32-s3-devkit"] = "board-esp32-s3-devkitc-1",
        ["esp32-c3-devkit"] = "board-esp32-c3-devkitm-1",
        ["esp32-c6-devkit"] = "board-esp32-c6-devkitc-1",
        ["esp32-h2-devkit"] = "board-esp32-h2-devkitm-1",
        ["pi-pico-w"] = "board-pi-pico-w",
        ["st-nucleo-c031c6"] = "board-st-nucleo-c031c6",
        ["st-nucleo-l031k6"] = "board-st-nucleo-l031k6",
        ["stm32-bluepill"] = "board-stm32-bluepill",
        ["protoboard-830"] = "wokwi-breadboard",
        ["protoboard-400"] = "wokwi-breadboard-half",
        ["protoboard-170"] = "wokwi-breadboard-mini",
        // O LCD com modulo I2C e o mesmo display la, com o atributo pins="i2c".
        ["lcd1602-i2c"] = "wokwi-lcd1602",
        ["lcd2004-i2c"] = "wokwi-lcd2004",
        // O OLED de quatro fios (I2C) e a placa "board-ssd1306"; o de sete fios e o "wokwi-ssd1306".
        ["ssd1306-i2c"] = "board-ssd1306",
        ["vcc-symbol"] = "wokwi-vcc",
        ["gnd-symbol"] = "wokwi-gnd",
        ["modulo-rele-1"] = "wokwi-relay-module",
        ["texto"] = "wokwi-text",
        // As portas logicas e os flip-flops do simulador de logica deles.
        ["and-gate"] = "wokwi-gate-and-2",
        ["or-gate"] = "wokwi-gate-or-2",
        ["xor-gate"] = "wokwi-gate-xor-2",
        ["nand-gate"] = "wokwi-gate-nand-2",
        ["nor-gate"] = "wokwi-gate-nor-2",
        ["xnor-gate"] = "wokwi-gate-xnor-2",
        ["not-gate"] = "wokwi-gate-not",
        ["buffer-gate"] = "wokwi-gate-buffer",
        ["mux-2"] = "wokwi-mux-2",
        ["flip-flop-d"] = "wokwi-flip-flop-d",
        ["flip-flop-dsr"] = "wokwi-flip-flop-dsr",
    };

    /// <summary>Os nomes velhos e as variantes que os arquivos deles trazem (so de la para ca).</summary>
    static readonly Dictionary<string, string> Apelidos = new()
    {
        ["board-esp32-devkit-c-v4"] = "esp32-devkit-v1",
        ["wokwi-esp32-devkit-c-v4"] = "esp32-devkit-v1",
        ["wokwi-breadboard-full"] = "protoboard-830",
        ["wokwi-led-strip"] = "neopixel",
        ["wokwi-neopixel-canvas"] = "neopixel",
        ["board-ili9341-cap-touch"] = "ili9341",
        ["board-ili9341-touch"] = "ili9341",
        ["board-arduino-uno"] = "arduino-uno",
        ["board-arduino-mega"] = "arduino-mega",
        ["board-arduino-nano"] = "arduino-nano",
        ["board-arduino-nano-rp2040-connect"] = "nano-rp2040-connect",
        ["board-pi-pico"] = "pi-pico",
        ["board-franzininho"] = "franzininho",
        ["board-bluepill-f103c8"] = "stm32-bluepill",
        ["wokwi-and-gate"] = "and-gate",
        ["wokwi-or-gate"] = "or-gate",
        ["wokwi-not-gate"] = "not-gate",
    };

    /// <summary>De la para ca: os exportados ao contrario (menos os LCDs com modulo, que vem pelo atributo) e os apelidos.</summary>
    static readonly Dictionary<string, string> Importados = Exportados
        .Where(par => par.Key is not ("lcd1602-i2c" or "lcd2004-i2c"))
        .ToDictionary(par => par.Value, par => par.Key)
        .Concat(Apelidos).ToDictionary(par => par.Key, par => par.Value);

    /// <summary>O tipo daqui de uma peca do Wokwi, ou null quando nao ha equivalente.</summary>
    public static string TipoImportado(string tipo, IReadOnlyDictionary<string, string> atributos, Func<string, bool> existe)
    {
        string nosso = null;
        if (Importados.TryGetValue(tipo, out var traduzido)) nosso = traduzido;
        // As outras ESP32 deles (a DevKitC, as XIAO...) caem na nossa DevKit V1:
        // e a placa da turma, e a traducao de pino cobre o que existe nas duas.
        else if (tipo.StartsWith("board-esp32", StringComparison.Ordinal)) nosso = "esp32-devkit-v1";
        else
            foreach (string prefixo in new[] { "wokwi-", "board-" })
                if (tipo.StartsWith(prefixo, StringComparison.Ordinal) && existe(tipo[prefixo.Length..]))
                {
                    nosso = tipo[prefixo.Length..];
                    break;
                }

        // O LCD deles troca de conector pelo atributo: pins="i2c" sao quatro vias
        // (GND, VCC, SDA, SCL) em vez das dezesseis -- aqui e outra peca.
        if (nosso is "lcd1602" or "lcd2004" && atributos is not null
            && atributos.TryGetValue("pins", out var pinos) && pinos == "i2c")
            nosso += "-i2c";
        return nosso;
    }

    /// <summary>O tipo la; as pecas que o Wokwi nao tem saem como "circuitsketch-..." (la ele avisa e abre o resto).</summary>
    public static string TipoExportado(string tipo)
        => Exportados.TryGetValue(tipo, out var deles) ? deles
         : MesmoNome.Contains(tipo) ? "wokwi-" + tipo
         : "circuitsketch-" + tipo;

    /// <summary>O que o tipo la precisa a mais nos "attrs" (o pins="i2c" do LCD com modulo).</summary>
    public static Dictionary<string, string> AtributosExportados(string tipo)
        => tipo is "lcd1602-i2c" or "lcd2004-i2c" ? new() { ["pins"] = "i2c" } : null;

    // =====================================================================
    //  os pinos
    // =====================================================================

    static Dictionary<string, string> Portas(string saida) => new() { ["Q"] = saida };

    /// <summary>Os pinos com nome diferente la, por peca (o daqui e o de la).</summary>
    static readonly Dictionary<string, Dictionary<string, string>> PinosDiferentes = new()
    {
        // O A4988 deles tem um GND so, e os nomes inteiros.
        ["a4988"] = new() { ["EN"] = "ENABLE", ["RST"] = "RESET", ["SLP"] = "SLEEP", ["GND.2"] = "GND" },
        // O Uno e a Mega deles nao tem os pinos do ICSP: vao os da barra, que sao os mesmos.
        ["arduino-uno"] = new() { ["12.2"] = "12", ["13.2"] = "13", ["11.2"] = "11", ["5V.2"] = "5V", ["RESET.2"] = "RESET",
                                  ["GND.4"] = "GND.1", ["5V.3"] = "5V", ["GND.5"] = "GND.2" },
        ["arduino-mega"] = new() { ["50.2"] = "50", ["52.2"] = "52", ["51.2"] = "51", ["5V.3"] = "5V", ["RESET.2"] = "RESET",
                                   ["GND.6"] = "GND.1", ["5V.4"] = "5V", ["GND.7"] = "GND.2" },
        // As DevKits da Espressif: o nosso EN e o RST da serigrafia (e do Wokwi), e
        // la todo pino de energia repetido e numerado desde o 1.
        ["esp32-s3-devkit"] = new() { ["EN"] = "RST", ["3V3"] = "3V3.1", ["GND"] = "GND.1" },
        ["esp32-s2-devkit"] = new() { ["EN"] = "RST", ["GND"] = "GND.1" },
        ["esp32-c3-devkit"] = new() { ["EN"] = "RST", ["3V3"] = "3V3.1", ["5V"] = "5V.1", ["GND"] = "GND.1" },
        ["esp32-c6-devkit"] = new() { ["EN"] = "RST", ["GND"] = "GND.1", ["NC"] = "NC0", ["NC.2"] = "NC1" },
        ["esp32-h2-devkit"] = new() { ["EN"] = "RST", ["GND"] = "GND.1" },
        ["and-gate"] = Portas("OUT"), ["or-gate"] = Portas("OUT"), ["xor-gate"] = Portas("OUT"),
        ["nand-gate"] = Portas("OUT"), ["nor-gate"] = Portas("OUT"), ["xnor-gate"] = Portas("OUT"),
        ["not-gate"] = Portas("OUT"), ["mux-2"] = Portas("OUT"), ["buffer-gate"] = Portas("OUT"),
        ["flip-flop-d"] = new() { ["NQ"] = "NOTQ" },
        ["flip-flop-dsr"] = new() { ["NQ"] = "NOTQ" },
        // A Pico deles numera todos os GND (o primeiro e GND.1).
        ["pi-pico"] = new() { ["GND"] = "GND.1" },
        ["pi-pico-w"] = new() { ["GND"] = "GND.1" },
    };

    /// <summary>
    /// O pino daqui de um pino do arquivo deles (pinos: os da peca daqui). Null
    /// quando nao existe mesmo -- melhor perder o fio e dizer qual do que criar um
    /// fio que aponta para o nada.
    /// </summary>
    public static string PinoImportado(string tipo, string pino, string[] pinos)
    {
        bool Existe(string n) => Array.IndexOf(pinos, n) >= 0;
        if (Existe(pino)) return pino;
        if (PinosDiferentes.TryGetValue(tipo, out var mapa))
            foreach (var (nosso, deles) in mapa)
                if (deles == pino && Existe(nosso)) return nosso;

        // ESP32: o diagrama da DevKitC traz o numero do GPIO, a placa daqui tem o nome da serigrafia.
        foreach (string tentativa in Alternativas(pino))
            if (Existe(tentativa)) return tentativa;

        // "GND.2" pedido numa peca que so tem "GND", e o contrario.
        string semSufixo = pino.Contains('.') ? pino[..pino.IndexOf('.')] : null;
        if (semSufixo is not null && Existe(semSufixo)) return semSufixo;
        return pinos.FirstOrDefault(x => x.StartsWith(pino + ".", StringComparison.Ordinal));
    }

    /// <summary>O pino la (null: o mesmo nome).</summary>
    public static string PinoExportado(string tipo, string pino)
        => PinosDiferentes.TryGetValue(tipo, out var mapa) && mapa.TryGetValue(pino, out var la) ? la : null;

    static IEnumerable<string> Alternativas(string pino)
    {
        switch (pino)
        {
            case "16": yield return "RX2"; break;
            case "17": yield return "TX2"; break;
            case "1": yield return "TX0"; break;
            case "3": yield return "RX0"; break;
            case "TX": yield return "TX0"; yield return "TX2"; break;
            case "RX": yield return "RX0"; yield return "RX2"; break;
            // A entrada de 5 V: "VIN" na DevKit V1, "5V" num diagrama escrito a mao.
            case "5V": yield return "VIN"; yield return "VUSB"; break;
            case "VIN": yield return "5V"; break;
            case "VCC": yield return "VIN"; yield return "5V"; yield return "3V3"; break;
            case "3.3V": case "3V": yield return "3V3"; break;
            case "3V3": yield return "3.3V"; break;
            // O reset: RST nas DevKits novas, EN na DevKit V1.
            case "RST": yield return "EN"; break;
        }
        yield return "D" + pino;                            // 4 -> D4
        if (pino.StartsWith('D')) yield return pino[1..];   // D4 -> 4
        yield return "GPIO" + pino;
    }
}
