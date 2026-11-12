using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Simulador.App;

/// <summary>
/// O diagram.json do Wokwi, das duas pontas: os nomes dos tipos, dos pinos e
/// das cores la e aqui, e o jeito de escrever o arquivo que o editor deles
/// usa (objeto curto numa linha so, objeto comprido aberto, uma ligacao por
/// linha). O <see cref="ExportarWokwi"/> escreve e o <see cref="ImportarWokwi"/>
/// le com estas mesmas tabelas -- e por isso que a ida e volta fecha.
///
/// As pecas que o Wokwi nao tem (os motores, as pontes H, a bateria...) saem
/// como "circuitsketch-&lt;peca&gt;": o Wokwi avisa que nao conhece o tipo e
/// abre o resto, e aqui elas voltam inteiras.
/// </summary>
public static class FormatoWokwi
{
    /// <summary>O "editor" que o arquivo diz que o escreveu.</summary>
    public const string Editor = "circuitsketch";

    /// <summary>O prefixo das pecas que so existem aqui.</summary>
    public const string PrefixoNosso = "circuitsketch-";

    // =====================================================================
    //  os tipos
    // =====================================================================

    /// <summary>
    /// As pecas com o mesmo nome dos dois lados: "led" aqui e "wokwi-led" la.
    /// Sao os elementos do wokwi-elements e as pecas da documentacao deles
    /// (o desenho daqui veio de la, e os pinos tem os mesmos nomes).
    /// </summary>
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
        "stepper-motor", "tilt-switch", "wifi-ap",
        // O registrador de deslocamento: os pinos com os nomes deles (DS, SHCP, STCP, MR, OE, Q0..Q7, Q7S).
        "74hc595",
        // O DS18B20 (GND, DQ, VCC).
        "ds18b20",
    ];

    /// <summary>As pecas com nome diferente la (as placas novas sao "board-", nao "wokwi-").</summary>
    static readonly Dictionary<string, string> Diferentes = new()
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
        [Catalogo.IdProtoboard830] = "wokwi-breadboard",
        [Catalogo.IdProtoboard] = "wokwi-breadboard-half",
        [Catalogo.IdProtoboard170] = "wokwi-breadboard-mini",
        // O LCD com modulo I2C e o mesmo display la, com o atributo pins="i2c".
        ["lcd1602-i2c"] = "wokwi-lcd1602",
        ["lcd2004-i2c"] = "wokwi-lcd2004",
        // O OLED de quatro fios (I2C) e a placa "board-ssd1306"; o de sete
        // fios (SPI) e o elemento "wokwi-ssd1306".
        ["ssd1306-i2c"] = "board-ssd1306",
        ["vcc-symbol"] = "wokwi-vcc",
        ["modulo-rele-1"] = "wokwi-relay-module",
        ["gnd-symbol"] = "wokwi-gnd",
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

    /// <summary>Nomes que os arquivos deles trazem e que nao sao os que escrevemos (versoes velhas, variantes).</summary>
    static readonly Dictionary<string, string> Apelidos = new()
    {
        ["board-esp32-devkit-c-v4"] = "esp32-devkit-v1",
        ["wokwi-esp32-devkit-c-v4"] = "esp32-devkit-v1",
        ["wokwi-breadboard-full"] = Catalogo.IdProtoboard830,
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
        ["wokwi-and-gate"] = "and-gate", ["wokwi-or-gate"] = "or-gate", ["wokwi-not-gate"] = "not-gate",
    };

    /// <summary>
    /// De la para ca. Sem os LCDs com modulo: "wokwi-lcd1602" e o LCD de
    /// dezesseis vias, e so o atributo pins="i2c" faz dele o de modulo.
    /// </summary>
    static readonly Dictionary<string, string> DeVolta = Diferentes
        .Where(par => par.Key is not ("lcd1602-i2c" or "lcd2004-i2c"))
        .GroupBy(par => par.Value).ToDictionary(g => g.Key, g => g.First().Key);

    /// <summary>O tipo la ("wokwi-led", "board-esp32-s3-devkitc-1", "circuitsketch-motor-dc").</summary>
    public static string TipoParaWokwi(string nosso)
        => Diferentes.TryGetValue(nosso, out var outro) ? outro
         : MesmoNome.Contains(nosso) ? "wokwi-" + nosso
         : PrefixoNosso + nosso;

    /// <summary>O tipo daqui, ou nulo quando nao ha equivalente.</summary>
    public static string TipoDoWokwi(string deles)
    {
        if (string.IsNullOrEmpty(deles)) return null;
        if (DeVolta.TryGetValue(deles, out var nosso) || Apelidos.TryGetValue(deles, out nosso)) return nosso;
        // As outras ESP32 deles (a DevKitC, as XIAO...) caem na nossa DevKit V1:
        // e a placa da turma, e a traducao de pino cobre o que existe nas duas.
        if (deles.StartsWith("board-esp32", StringComparison.Ordinal)) return "esp32-devkit-v1";
        foreach (string prefixo in new[] { PrefixoNosso, "wokwi-", "board-" })
            if (deles.StartsWith(prefixo, StringComparison.Ordinal) && Catalogo.Obter(deles[prefixo.Length..]) is not null)
                return deles[prefixo.Length..];
        return Catalogo.Obter(deles) is not null ? deles : null;
    }

    // =====================================================================
    //  os pinos
    // =====================================================================

    static Dictionary<string, string> Portas(string saida) => new() { ["Q"] = saida };

    /// <summary>Os pinos com nome diferente la, por peca (daqui para la).</summary>
    static readonly Dictionary<string, Dictionary<string, string>> PinosDiferentes = new()
    {
        // O A4988 deles tem um GND so, e os nomes inteiros.
        ["a4988"] = new() { ["EN"] = "ENABLE", ["RST"] = "RESET", ["SLP"] = "SLEEP", ["GND.2"] = "GND" },
        // O Uno e a Mega deles nao tem os pinos do ICSP: vao os da barra, que sao os mesmos.
        ["arduino-uno"] = new() { ["12.2"] = "12", ["13.2"] = "13", ["11.2"] = "11", ["5V.2"] = "5V", ["RESET.2"] = "RESET",
                                  ["GND.4"] = "GND.1", ["5V.3"] = "5V", ["GND.5"] = "GND.2" },
        ["arduino-mega"] = new() { ["50.2"] = "50", ["52.2"] = "52", ["51.2"] = "51", ["5V.3"] = "5V", ["RESET.2"] = "RESET",
                                   ["GND.6"] = "GND.1", ["5V.4"] = "5V", ["GND.7"] = "GND.2" },
        // As DevKits da Espressif: o nosso EN e o RST da serigrafia (e do
        // Wokwi), e la todo pino de energia repetido e numerado desde o 1.
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

    public static string PinoParaWokwi(string tipo, string pino)
        => tipo is not null && PinosDiferentes.TryGetValue(tipo, out var mapa) && mapa.TryGetValue(pino, out var la) ? la : pino;

    /// <summary>
    /// O nome do pino daqui. Nulo quando nao existe mesmo -- melhor perder o
    /// fio e dizer qual do que criar um fio que aponta para o nada.
    /// </summary>
    public static string PinoDoWokwi(string tipo, string pino)
    {
        var def = tipo is null ? null : Catalogo.Obter(tipo);
        if (def is null) return pino;

        bool Existe(string n) => def.Pinos.Any(x => x.Nome == n);
        if (Existe(pino)) return pino;
        if (PinosDiferentes.TryGetValue(tipo, out var mapa))
            foreach (var (nosso, deles) in mapa)
                if (deles == pino && Existe(nosso)) return nosso;

        // ESP32: o diagrama da DevKitC traz o numero do GPIO, a placa daqui
        // tem o nome da serigrafia.
        foreach (string tentativa in Alternativas(pino))
            if (Existe(tentativa)) return tentativa;

        // "GND.2" pedido numa peca que so tem "GND", e o contrario.
        string semSufixo = pino.Contains('.') ? pino[..pino.IndexOf('.')] : null;
        if (semSufixo is not null && Existe(semSufixo)) return semSufixo;
        return def.Pinos.FirstOrDefault(x => x.Nome.StartsWith(pino + ".", StringComparison.Ordinal))?.Nome;
    }

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

    // =====================================================================
    //  as cores
    // =====================================================================

    static readonly (string Nome, string Hex)[] CoresDeFio =
    [
        ("black", "#000000"), ("red", "#D6202A"), ("green", "#00A651"), ("blue", "#1668C1"),
        ("yellow", "#F2C300"), ("white", "#F2F2F2"), ("orange", "#F07C1E"), ("purple", "#8A4FBE"),
        ("gray", "#808080"), ("brown", "#8B4A1B"), ("cyan", "#00BCD4"), ("magenta", "#E040A0"),
        ("gold", "#D4AF37"), ("limegreen", "#5CD65C"), ("violet", "#8A4FBE"), ("grey", "#808080"),
        ("pink", "#E040A0"),
    ];

    /// <summary>
    /// A cor do fio la: o nome, como o editor deles grava. Cor que nao esta na
    /// paleta deles sai em hexadecimal (CSS), e volta igual.
    /// </summary>
    public static string CorDoFioParaWokwi(string hex)
    {
        if (string.IsNullOrEmpty(hex)) return "green";
        foreach (var (nome, h) in CoresDeFio)
            if (string.Equals(h, hex, StringComparison.OrdinalIgnoreCase)) return nome;
        return hex.StartsWith('#') ? hex.ToUpperInvariant() : hex;
    }

    public static string CorDoFioDoWokwi(string cor)
    {
        if (string.IsNullOrEmpty(cor)) return "#00A651";
        foreach (var (nome, h) in CoresDeFio)
            if (string.Equals(nome, cor, StringComparison.OrdinalIgnoreCase)) return h;
        return cor.StartsWith('#') ? cor.ToUpperInvariant() : "#00A651";
    }

    static readonly (string Nosso, string Deles)[] CoresDeLed =
    [
        ("vermelho", "red"), ("amarelo", "yellow"), ("verde", "green"), ("azul", "blue"),
        ("branco", "white"), ("laranja", "orange"), ("roxo", "purple"), ("roxo", "violet"),
    ];

    public static string CorDoLedParaWokwi(string nossa)
        => CoresDeLed.FirstOrDefault(c => c.Nosso == nossa).Deles ?? "red";

    public static string CorDoLedDoWokwi(string deles)
        => CoresDeLed.FirstOrDefault(c => string.Equals(c.Deles, deles, StringComparison.OrdinalIgnoreCase)).Nosso ?? "vermelho";

    /// <summary>Os bracos que o wokwi-servo tambem tem ("horn"); o nenhum e os do MG996R vao no "braco".</summary>
    static readonly (string Nosso, string Deles)[] BracosDoServo = [("simples", "single"), ("duplo", "double"), ("cruz", "cross")];

    public static string BracoParaWokwi(string nosso) => BracosDoServo.FirstOrDefault(b => b.Nosso == nosso).Deles;

    public static string BracoDoWokwi(string deles)
        => BracosDoServo.FirstOrDefault(b => string.Equals(b.Deles, deles, StringComparison.OrdinalIgnoreCase)).Nosso;

    // =====================================================================
    //  os ids
    // =====================================================================

    /// <summary>
    /// O comeco do id de uma peca nova, no jeito deles: "led1", "r2", "btn1",
    /// "esp1", "uno1", "bb1". Um id legivel e o que faz o diagram.json se ler.
    /// </summary>
    public static string RaizDoId(string tipo) => tipo switch
    {
        "led" => "led",
        "rgb-led" => "rgb",
        "resistor" => "r",
        "pushbutton" or "pushbutton-6mm" => "btn",
        "buzzer" => "bz",
        "potentiometer" or "slide-potentiometer" => "pot",
        "arduino-uno" => "uno",
        "arduino-mega" => "mega",
        "arduino-nano" => "nano",
        "pi-pico" or "pi-pico-w" => "pico",
        "attiny85" or "franzininho" => "tiny",
        "stm32-bluepill" or "st-nucleo-c031c6" or "st-nucleo-l031k6" => "stm",
        _ when tipo.StartsWith("esp", StringComparison.Ordinal) && PerfilPlaca.EhPlaca(tipo) => "esp",
        _ when Catalogo.EhPcb(tipo) => "pcb",
        _ when Catalogo.EhProtoboard(tipo) => "bb",
        _ when tipo.StartsWith("lcd", StringComparison.Ordinal) => "lcd",
        _ when tipo.StartsWith("servo", StringComparison.Ordinal) => "servo",
        _ when tipo.StartsWith("motor", StringComparison.Ordinal) => "motor",
        _ when tipo.StartsWith("helice", StringComparison.Ordinal) => "helice",
        _ => Iniciais(tipo),
    };

    static string Iniciais(string tipo)
    {
        string letras = new([.. tipo.Where(char.IsAsciiLetter)]);
        return letras.Length == 0 ? "p" : letras[..Math.Min(4, letras.Length)];
    }

    // =====================================================================
    //  o texto
    // =====================================================================

    static readonly JsonSerializerOptions Texto = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>Uma string do JSON, com acento legivel ("Joao" com til, e nao ão).</summary>
    public static string Aspas(string s) => JsonSerializer.Serialize(s ?? "", Texto);

    /// <summary>Numero como o JavaScript escreve: sem ponto a toa e sem virgula.</summary>
    public static string Num(double v)
    {
        if (double.IsNaN(v) || double.IsInfinity(v)) return "0";
        string s = v.ToString("0.####", CultureInfo.InvariantCulture);
        return s == "-0" ? "0" : s;
    }

    /// <summary>A largura que o editor deles aceita numa linha antes de abrir o objeto.</summary>
    public const int Largura = 98;

    /// <summary>Os atributos que o simulador escreve e le (os do Wokwi que ele entende, e os dele).</summary>
    static readonly HashSet<string> ChavesNossas =
    [
        "nome", "cor", "ohms", "giro", "giro2", "giro3", "giro4", "bits", "volts", "corrente", "capacidade",
        "modelo", "ficha-volts", "ficha-rpm-kv", "ficha-resistencia", "ficha-corrente-vazio",
        "ficha-corrente-maxima", "ficha-inercia", "ficha-medida-a", "ficha-medida-b", "ficha-polos",
        "amperes", "acabamento", "ssid", "senha", "placa-que-manda", "janela-us", "espelhada", "teclas", "usb",
        "braco",
    ];

    /// <summary>Este atributo o simulador entende nesta peca? (Os outros sao guardados como vieram.)</summary>
    public static bool AtributoConhecido(string tipo, string chave) => chave switch
    {
        "color" => tipo == "led" || EditorCircuito.EhLcd(tipo),
        "background" => EditorCircuito.EhLcd(tipo),
        "value" => tipo == "resistor",
        "pins" => tipo is "lcd1602-i2c" or "lcd2004-i2c",
        "horn" => tipo == "servo",
        "text" => tipo == "texto",
        _ => ChavesNossas.Contains(chave),
    };

    /// <summary>
    /// Um valor JSON qualquer escrito como o editor deles escreve: numa linha
    /// so quando cabe (com espaco dentro das chaves), aberto quando nao.
    /// </summary>
    public static string Json(JsonElement e, int recuo, int usado)
    {
        string emLinha = EmLinha(e);
        if (recuo + usado + emLinha.Length + 1 <= Largura || e.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array))
            return emLinha;
        string dentro = new(' ', recuo + 2), fora = new(' ', recuo);
        var partes = e.ValueKind == JsonValueKind.Object
            ? e.EnumerateObject().Select(p => $"{dentro}{Aspas(p.Name)}: {Json(p.Value, recuo + 2, p.Name.Length + 4)}")
            : e.EnumerateArray().Select(v => dentro + Json(v, recuo + 2, 0));
        var lista = partes.ToList();
        if (lista.Count == 0) return e.ValueKind == JsonValueKind.Object ? "{}" : "[]";
        char abre = e.ValueKind == JsonValueKind.Object ? '{' : '[', fecha = e.ValueKind == JsonValueKind.Object ? '}' : ']';
        return abre + "\n" + string.Join(",\n", lista) + "\n" + fora + fecha;
    }

    static string EmLinha(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.Object => e.EnumerateObject().Any()
            ? "{ " + string.Join(", ", e.EnumerateObject().Select(p => $"{Aspas(p.Name)}: {EmLinha(p.Value)}")) + " }" : "{}",
        JsonValueKind.Array => e.EnumerateArray().Any()
            ? "[ " + string.Join(", ", e.EnumerateArray().Select(EmLinha)) + " ]" : "[]",
        JsonValueKind.String => Aspas(e.GetString()),
        _ => e.GetRawText(),
    };
}
