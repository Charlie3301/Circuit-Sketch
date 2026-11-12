using System;
using System.Collections.Generic;

/// <summary>
/// Plugin do Wokwi: abre os diagram.json feitos no Wokwi.
///
/// As pecas daqui tem o nome sem prefixo ("led", "esp32-s3-devkit"); as de la
/// vem com "wokwi-" ou "board-" ("wokwi-led", "board-esp32-s3-devkitc-1"). Sem
/// este plugin o programa so reconhece os nomes daqui (e o "circuitsketch-" que
/// ele mesmo escreve); com ele, cada peca do Wokwi e lida como a daqui.
///
/// Como todo plugin do CircuitSketch: uma classe estatica com Nome, Descricao,
/// Versao, Categoria e Autor, e os ganchos que ela quiser. O TipoImportado recebe o tipo como veio
/// no arquivo e devolve o daqui (ou null, quando nao sabe); o "existe" diz se um
/// nome esta no catalogo de pecas.
/// </summary>
public static class PluginWokwi
{
    public const string Nome = "Wokwi";
    public const string Descricao = "Abre os diagram.json do Wokwi: lê as peças deles (\"wokwi-led\", "
                                    + "\"board-esp32-s3-devkitc-1\") com os nomes daqui (\"led\", \"esp32-s3-devkit\").";
    public const string Versao = "1.0";
    public const string Categoria = "Importação";
    public const string Autor = "CircuitSketch";

    /// <summary>As pecas com nome diferente la (as placas novas sao "board-", nao "wokwi-"), e os nomes velhos e variantes.</summary>
    static readonly Dictionary<string, string> Diferentes = new()
    {
        ["board-esp32-s2-devkitm-1"] = "esp32-s2-devkit",
        ["board-esp32-s3-devkitc-1"] = "esp32-s3-devkit",
        ["board-esp32-c3-devkitm-1"] = "esp32-c3-devkit",
        ["board-esp32-c6-devkitc-1"] = "esp32-c6-devkit",
        ["board-esp32-h2-devkitm-1"] = "esp32-h2-devkit",
        ["board-pi-pico-w"] = "pi-pico-w",
        ["board-st-nucleo-c031c6"] = "st-nucleo-c031c6",
        ["board-st-nucleo-l031k6"] = "st-nucleo-l031k6",
        ["board-stm32-bluepill"] = "stm32-bluepill",
        ["wokwi-breadboard"] = "protoboard-830",
        ["wokwi-breadboard-half"] = "protoboard-400",
        ["wokwi-breadboard-mini"] = "protoboard-170",
        // O OLED de quatro fios (I2C) e a placa "board-ssd1306"; o de sete fios e o "wokwi-ssd1306".
        ["board-ssd1306"] = "ssd1306-i2c",
        ["wokwi-vcc"] = "vcc-symbol",
        ["wokwi-gnd"] = "gnd-symbol",
        ["wokwi-relay-module"] = "modulo-rele-1",
        ["wokwi-text"] = "texto",
        // As portas logicas e os flip-flops do simulador de logica deles.
        ["wokwi-gate-and-2"] = "and-gate",
        ["wokwi-gate-or-2"] = "or-gate",
        ["wokwi-gate-xor-2"] = "xor-gate",
        ["wokwi-gate-nand-2"] = "nand-gate",
        ["wokwi-gate-nor-2"] = "nor-gate",
        ["wokwi-gate-xnor-2"] = "xnor-gate",
        ["wokwi-gate-not"] = "not-gate",
        ["wokwi-gate-buffer"] = "buffer-gate",
        ["wokwi-mux-2"] = "mux-2",
        ["wokwi-flip-flop-d"] = "flip-flop-d",
        ["wokwi-flip-flop-dsr"] = "flip-flop-dsr",

        // Os nomes velhos e as variantes que os arquivos deles trazem.
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

    /// <summary>O tipo daqui de uma peca do Wokwi, ou null quando nao ha equivalente.</summary>
    public static string TipoImportado(string tipo, Func<string, bool> existe)
    {
        if (Diferentes.TryGetValue(tipo, out var nosso)) return nosso;

        // As outras ESP32 deles (a DevKitC, as XIAO...) caem na nossa DevKit V1:
        // e a placa da turma, e a traducao de pino cobre o que existe nas duas.
        if (tipo.StartsWith("board-esp32", StringComparison.Ordinal)) return "esp32-devkit-v1";

        // O resto tem o mesmo nome dos dois lados: "wokwi-led" e o "led" daqui.
        foreach (string prefixo in new[] { "wokwi-", "board-" })
            if (tipo.StartsWith(prefixo, StringComparison.Ordinal) && existe(tipo[prefixo.Length..]))
                return tipo[prefixo.Length..];
        return null;
    }
}
