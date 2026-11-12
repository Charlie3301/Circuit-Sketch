using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Simulador.App;

/// <summary>
/// O diagram.json: o jeito de escrever o arquivo que o editor do Wokwi usa
/// (objeto curto numa linha so, objeto comprido aberto, uma ligacao por linha),
/// as cores, e os pinos dos arquivos deles. O <see cref="ExportarWokwi"/> escreve
/// e o <see cref="ImportarWokwi"/> le.
///
/// As pecas e os pinos vao com os nomes daqui ("led", "esp32-s3-devkit"), sem
/// prefixo. Os nomes do Wokwi ("wokwi-led", "board-esp32-s3-devkitc-1", o ENABLE
/// do A4988) sao do plugin do Wokwi (plugins/wokwi): e ele que abre os arquivos de
/// la e que escreve no formato deles.
/// </summary>
public static class FormatoWokwi
{
    /// <summary>O "editor" que o arquivo diz que o escreveu.</summary>
    public const string Editor = "circuitsketch";

    /// <summary>O prefixo que os diagramas escritos ate a Alpha 24 punham nas pecas que o Wokwi nao tem (ainda abrem).</summary>
    public const string PrefixoNosso = "circuitsketch-";

    // =====================================================================
    //  os tipos
    // =====================================================================

    /// <summary>
    /// O tipo daqui de uma peca de um diagram.json, ou nulo quando nao ha equivalente.
    /// Por dentro, so o que e nosso: o nome do catalogo e o "circuitsketch-" que a
    /// exportacao escreve. Os nomes do Wokwi ("wokwi-led", "board-esp32-s3-devkitc-1")
    /// sao do plugin do Wokwi (plugins/wokwi) -- desligado, eles nao abrem.
    /// </summary>
    public static string TipoDoWokwi(string deles, IReadOnlyDictionary<string, string> atributos = null)
    {
        if (string.IsNullOrEmpty(deles)) return null;
        if (deles.StartsWith(PrefixoNosso, StringComparison.Ordinal) && Catalogo.Obter(deles[PrefixoNosso.Length..]) is not null)
            return deles[PrefixoNosso.Length..];
        if (Catalogo.Obter(deles) is not null) return deles;
        return Plugins.TipoImportado(deles, atributos);
    }

    // =====================================================================
    //  os pinos
    // =====================================================================

    /// <summary>
    /// O nome do pino daqui. O nome exato vale direto; o resto (os pinos com outro
    /// nome nos arquivos do Wokwi, o numero do GPIO da DevKitC) e dos plugins. Nulo
    /// quando nao existe mesmo -- melhor perder o fio e dizer qual do que criar um
    /// fio que aponta para o nada.
    /// </summary>
    public static string PinoDoWokwi(string tipo, string pino)
    {
        var def = tipo is null ? null : Catalogo.Obter(tipo);
        if (def is null) return pino;
        if (def.Pinos.Any(x => x.Nome == pino)) return pino;
        return Plugins.PinoImportado(tipo, pino, [.. def.Pinos.Select(x => x.Nome)]);
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
