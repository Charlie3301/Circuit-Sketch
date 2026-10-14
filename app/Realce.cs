using System.IO;
using System.Text;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;

namespace Simulador.App;

/// <summary>
/// As cores do editor de código.
///
/// O AvalonEdit já traz um realce de C++, e ele quase serve: pinta
/// `int`, `if` e as aspas. O que ele não sabe é o que **este** editor
/// precisa mostrar — `pinMode`, `digitalWrite`, `HIGH`, `LED_BUILTIN`.
/// São essas palavras que o aluno procura na tela, e são justamente as
/// que sairiam sem cor.
///
/// A definição vai embutida no código, e não num arquivo `.xshd` solto,
/// pelo mesmo motivo dos SVGs e das demos: o executável é um arquivo só
/// e precisa continuar sendo.
///
/// As cores acompanham as do editor escuro do resto do programa, para
/// o painel não parecer colado de outro lugar.
/// </summary>
public static class Realce
{
    /// <summary>
    /// O que o simulador entende de verdade.
    ///
    /// A lista sai da tabela do interpretador, e não de um "C++ genérico":
    /// pintar `malloc` de azul prometeria uma coisa que o motor recusa.
    /// </summary>
    static readonly string[] Funcoes =
    [
        "pinMode", "digitalWrite", "digitalRead", "analogRead", "analogWrite",
        "delay", "delayMicroseconds", "millis", "micros",
        "tone", "noTone", "map", "constrain", "random", "randomSeed",
        "min", "max", "abs", "pow", "sqrt", "sin", "cos", "tan", "round",
        "attachInterrupt", "detachInterrupt", "digitalPinToInterrupt",
        "interrupts", "noInterrupts", "shiftOut", "shiftIn", "pulseIn",
        "setup", "loop",

        // Texto do C, que os exemplos de rede usam o tempo todo.
        "snprintf", "sprintf", "strcpy", "strncpy", "strcat", "strlen",
        "strcmp", "strncmp", "strstr", "atoi", "atol", "atof", "dtostrf", "itoa",
        "isDigit", "isAlpha", "isAlphaNumeric", "isSpace",
    ];

    static readonly string[] Constantes =
    [
        "HIGH", "LOW", "INPUT", "OUTPUT", "INPUT_PULLUP", "INPUT_PULLDOWN",
        "LED_BUILTIN", "true", "false", "NULL",
        "RISING", "FALLING", "CHANGE", "MSBFIRST", "LSBFIRST",
        "A0", "A1", "A2", "A3", "A4", "A5", "A6", "A7",

        // Objetos que ja existem no sketch, como no editor da Arduino IDE.
        "Serial", "WiFi", "SD", "ESP",

        // Rede: o que o sketch compara ou passa para as bibliotecas.
        "WL_CONNECTED", "WL_DISCONNECTED", "WL_IDLE_STATUS", "WL_CONNECT_FAILED",
        "WIFI_STA", "WIFI_AP", "WIFI_AP_STA", "WIFI_OFF",
        "HTTP_GET", "HTTP_POST", "HTTP_PUT", "HTTP_DELETE", "HTTP_ANY",
        "HEX", "DEC", "BIN", "OCT",
    ];

    static readonly string[] Tipos =
    [
        "void", "int", "long", "short", "char", "float", "double",
        "bool", "boolean", "byte", "word", "unsigned", "signed",
        "const", "static", "volatile", "String", "enum", "struct",
        "uint8_t", "uint16_t", "uint32_t", "uint64_t",
        "int8_t", "int16_t", "int32_t", "int64_t", "size_t",

        // Classes de rede: declarar uma delas e o comeco de todo sketch
        // de IoT, e sem cor parecia nome de variavel.
        "WiFiClient", "WiFiClientSecure", "WiFiServer", "WebServer",
        "HTTPClient", "PubSubClient", "BluetoothSerial", "IPAddress",
    ];

    static readonly string[] Controle =
    [
        "if", "else", "for", "while", "do", "switch", "case", "default",
        "break", "continue", "return", "class", "public", "private",
    ];

    // ---------------- MicroPython ----------------
    /// <summary>
    /// O que o simulador atende em MicroPython.
    ///
    /// Mesma regra da lista do Arduino: so entra o que o motor executa
    /// de verdade. Pintar "open" de azul prometeria arquivo, que a
    /// placa simulada nao tem.
    /// </summary>
    static readonly string[] EmbutidasPy =
    [
        "print", "range", "len", "str", "int", "float", "bool", "abs", "round",
        "min", "max", "sum", "ord", "chr", "hex", "bin", "pow", "list", "tuple",
        "dict", "sorted", "reversed", "enumerate", "zip", "type", "isinstance",
        "bytes", "bytearray", "input",
    ];

    static readonly string[] ModulosEClasses =
    [
        "machine", "time", "utime", "network", "dht", "neopixel", "ssd1306",
        "ujson", "json", "umqtt", "math", "random", "urandom", "sys", "gc", "os",
        "micropython", "esp32", "esp", "ubinascii", "ustruct",
        "Pin", "ADC", "PWM", "I2C", "SoftI2C", "SPI", "Timer", "RTC", "UART",
        "WLAN", "DHT11", "DHT22", "NeoPixel", "SSD1306_I2C", "MQTTClient",
    ];

    static readonly string[] ControlePy =
    [
        "if", "elif", "else", "for", "while", "break", "continue", "return",
        "def", "class", "import", "from", "as", "in", "is", "not", "and", "or",
        "pass", "global", "nonlocal", "del", "with", "try", "except", "finally",
        "raise", "lambda", "yield", "assert",
    ];

    static readonly string[] ConstantesPy =
    [
        "True", "False", "None", "OUT", "IN", "PULL_UP", "PULL_DOWN",
        "STA_IF", "AP_IF", "ATTN_11DB", "IRQ_RISING", "IRQ_FALLING",
    ];

    static IHighlightingDefinition definicao;
    static IHighlightingDefinition definicaoClara;
    static IHighlightingDefinition python;
    static IHighlightingDefinition pythonClaro;

    public static IHighlightingDefinition Arduino => definicao ??= Montar(false);

    public static IHighlightingDefinition ArduinoClaro => definicaoClara ??= Montar(true);

    public static IHighlightingDefinition Python => python ??= MontarPython(false);

    public static IHighlightingDefinition PythonClaro => pythonClaro ??= MontarPython(true);

    public static IHighlightingDefinition Do(string tema)
        => !Tema.EhEscuroOTema(tema) ? ArduinoClaro : Arduino;

    /// <summary>
    /// As cores certas para a linguagem do sketch.
    ///
    /// Quem chama passa a linguagem que o <see cref="Compilador"/>
    /// detectou -- assim a tela e a execucao concordam: se o editor
    /// esta pintando "def" de roxo, e porque o motor vai rodar aquilo
    /// como Python.
    /// </summary>
    public static IHighlightingDefinition Do(string tema, Linguagem linguagem)
        => linguagem == Linguagem.MicroPython
            ? (!Tema.EhEscuroOTema(tema) ? PythonClaro : Python)
            : (!Tema.EhEscuroOTema(tema) ? ArduinoClaro : Arduino);

    static IHighlightingDefinition Montar(bool claro)
    {
        // Verbatim (@"..."), e nao raw string ("""..."""): as regras aqui
        // sao expressoes regulares, cheias de barra invertida. Na raw
        // string a barra e literal, entao "\b" chegava no motor de regex
        // como barra-barra-b e a regra de numero nunca casava -- foi
        // assim que os numeros ficaram sem cor. No verbatim a barra
        // tambem e literal, mas e o que a regex quer; o que se escapa
        // aqui e a aspa, dobrando.
        string xml =
            $@"<SyntaxDefinition name=""Arduino{(claro ? "Claro" : "")}"" xmlns=""http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008"">
              <Color name=""Comentario"" foreground=""{(claro ? "#3F7F3F" : "#6A9955")}""/>
              <Color name=""Texto""      foreground=""{(claro ? "#A31515" : "#CE9178")}""/>
              <Color name=""Numero""     foreground=""{(claro ? "#1C7D3E" : "#B5CEA8")}""/>
              <Color name=""Controle""   foreground=""{(claro ? "#9A2FA0" : "#C586C0")}""/>
              <Color name=""Tipo""       foreground=""{(claro ? "#0B5FB0" : "#569CD6")}""/>
              <Color name=""Funcao""     foreground=""{(claro ? "#7A5B00" : "#DCDCAA")}""/>
              <Color name=""Constante""  foreground=""{(claro ? "#0E6FA8" : "#4FC1FF")}""/>
              <Color name=""Diretiva""   foreground=""{(claro ? "#6C6C6C" : "#9B9B9B")}""/>
              <RuleSet ignoreCase=""false"">"

            // Comentario e texto vem primeiro: dentro deles nada mais
            // vale, e e por isso que "// pinMode" nao fica amarelo.
            + @"
                <Span color=""Comentario"" begin=""//""/>
                <Span color=""Comentario"" multiline=""true"" begin=""/\*"" end=""\*/""/>
                <Span color=""Diretiva"" begin=""\#""/>
                <Span color=""Texto"" begin=""&quot;"" end=""&quot;""/>
                <Span color=""Texto"" begin=""'"" end=""'""/>"

            + $@"
                <Keywords color=""Controle"">{Palavras(Controle)}</Keywords>
                <Keywords color=""Tipo"">{Palavras(Tipos)}</Keywords>
                <Keywords color=""Constante"">{Palavras(Constantes)}</Keywords>
                <Keywords color=""Funcao"">{Palavras(Funcoes)}</Keywords>"

            + @"
                <Rule color=""Numero"">\b0[xX][0-9a-fA-F]+|\b\d+(\.\d+)?</Rule>
              </RuleSet>
            </SyntaxDefinition>";

        using var leitor = new System.Xml.XmlTextReader(new StringReader(xml));
        return HighlightingLoader.Load(leitor, HighlightingManager.Instance);
    }

    static IHighlightingDefinition MontarPython(bool claro)
    {
        string xml =
            $@"<SyntaxDefinition name=""MicroPython{(claro ? "Claro" : "")}"" xmlns=""http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008"">
              <Color name=""Comentario"" foreground=""{(claro ? "#3F7F3F" : "#6A9955")}""/>
              <Color name=""Texto""      foreground=""{(claro ? "#A31515" : "#CE9178")}""/>
              <Color name=""Numero""     foreground=""{(claro ? "#1C7D3E" : "#B5CEA8")}""/>
              <Color name=""Controle""   foreground=""{(claro ? "#9A2FA0" : "#C586C0")}""/>
              <Color name=""Tipo""       foreground=""{(claro ? "#0B5FB0" : "#569CD6")}""/>
              <Color name=""Funcao""     foreground=""{(claro ? "#7A5B00" : "#DCDCAA")}""/>
              <Color name=""Constante""  foreground=""{(claro ? "#0E6FA8" : "#4FC1FF")}""/>
              <RuleSet ignoreCase=""false"">"

            // O texto de TRES aspas vem antes do de uma: senao o
            // cabecalho de explicacao no topo do sketch fecharia na
            // primeira aspa e o resto do arquivo sairia todo colorido
            // de texto.
            + @"
                <Span color=""Texto"" multiline=""true"" begin=""&quot;&quot;&quot;"" end=""&quot;&quot;&quot;""/>
                <Span color=""Texto"" multiline=""true"" begin=""'''"" end=""'''""/>
                <Span color=""Comentario"" begin=""\#""/>
                <Span color=""Texto"" begin=""&quot;"" end=""&quot;""/>
                <Span color=""Texto"" begin=""'"" end=""'""/>"

            + $@"
                <Keywords color=""Controle"">{Palavras(ControlePy)}</Keywords>
                <Keywords color=""Tipo"">{Palavras(ModulosEClasses)}</Keywords>
                <Keywords color=""Constante"">{Palavras(ConstantesPy)}</Keywords>
                <Keywords color=""Funcao"">{Palavras(EmbutidasPy)}</Keywords>"

            + @"
                <Rule color=""Numero"">\b0[xXbB][0-9a-fA-F]+|\b\d+(\.\d+)?</Rule>
              </RuleSet>
            </SyntaxDefinition>";

        using var leitor = new System.Xml.XmlTextReader(new StringReader(xml));
        return HighlightingLoader.Load(leitor, HighlightingManager.Instance);
    }

    static string Palavras(string[] lista)
    {
        var texto = new StringBuilder();
        foreach (string w in lista) texto.Append($"<Word>{w}</Word>");
        return texto.ToString();
    }

    /// <summary>
    /// Objetos de biblioteca que o sketch cria (`lcd`, `servo`) não têm
    /// como entrar na lista fixa: o nome é escolhido por quem escreve.
    /// Ficam sem cor, e está certo — colori-los seria adivinhar.
    /// </summary>
    public static Brush FundoDaLinhaAtual => !Tema.EhEscuro
        ? new SolidColorBrush(Color.FromArgb(0x1A, 0x00, 0x00, 0x00))
        : new SolidColorBrush(Color.FromArgb(0x28, 0xFF, 0xFF, 0xFF));
}
