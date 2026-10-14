using System.IO;
using System.IO.Compression;

namespace Simulador.App;

/// <summary>Uma biblioteca que o simulador conhece ou que a pessoa instalou.</summary>
public sealed record BibliotecaDoAcervo(
    string Nome, string Cabecalho, string Resumo, string Classes, bool DoSimulador)
{
    /// <summary>Instalada da pasta da pessoa: da para remover.</summary>
    public string Pasta { get; init; }

    /// <summary>Quantos .h vieram junto, quando foi instalada.</summary>
    public int Arquivos { get; init; }

    /// <summary>
    /// Quanto ocupa em disco, em bytes.
    ///
    /// Zero nas que vem no programa: elas ja estao dentro do executavel,
    /// e nao ocupam nada a mais. Foi o professor quem pediu o numero --
    /// ele quer saber o que vai gastar ANTES de instalar.
    /// </summary>
    public long Bytes { get; init; }

    /// <summary>O tamanho do jeito que a pessoa le.</summary>
    public string TamanhoEmTexto => Acervo.EmTexto(Bytes);
}

/// <summary>
/// Uma biblioteca que o sketch pediu e o simulador nao tem, com o que
/// da para fazer a respeito.
/// </summary>
/// <param name="Cabecalho">O que o #include (ou o import) pediu.</param>
/// <param name="Candidata">
/// Pasta achada no computador que parece ser essa biblioteca, ou null.
/// </param>
public sealed record Faltando(string Cabecalho, string Candidata, long Bytes, int Arquivos)
{
    /// <summary>
    /// O #include veio com ASPAS: e um arquivo do proprio projeto.
    ///
    /// A diferenca muda o conselho inteiro. Com aspas, o C procura ao
    /// LADO do sketch -- entao o que falta e uma aba que ninguem criou,
    /// e nenhuma biblioteca do mundo resolve. O laptimer da lista abre
    /// assim, pedindo config.h e app_state.h que nao vieram no arquivo.
    /// </summary>
    public bool DoProjeto { get; init; }

    public string Nome => Candidata is null
        ? Cabecalho
        : System.IO.Path.GetFileName(Candidata.TrimEnd(System.IO.Path.DirectorySeparatorChar));

    public string TamanhoEmTexto => Acervo.EmTexto(Bytes);
}

/// <summary>
/// O acervo de bibliotecas.
///
/// Dois tipos moram aqui. As do simulador sao implementadas por dentro:
/// a Servo move o servo do circuito, a WiFi fala com a rede simulada.
/// As instaladas pela pessoa -- uma pasta ou um .zip do GitHub, igual na
/// Arduino IDE -- tem o codigo C++ delas lido e executado pelo
/// interpretador, arquivo por arquivo, quando o sketch faz o #include.
///
/// O que esta janela faz:
///
/// - diz QUAIS bibliotecas o simulador ja executa, e o que cada uma da;
/// - olha os #include do sketch aberto e aponta os que nao tem dono --
///   e essa a resposta para "por que o meu codigo nao anda";
/// - instala uma biblioteca de fora em %APPDATA%\CircuitSketch\bibliotecas.
/// </summary>
public static class Acervo
{
    public static string Pasta => Path.Combine(Armazem.Pasta, "bibliotecas");

    /// <summary>As que o motor executa, com o que cada uma serve.</summary>
    static readonly BibliotecaDoAcervo[] DoSimulador =
    [
        new("Servo", "Servo.h", "Gira um servo de 0 a 180 graus.", "Servo", true),

        // Na ESP32 o servo e ESTA: a Servo.h da Arduino nao compila para
        // ela, e todo tutorial de ESP32 inclui a ESP32Servo.
        new("ESP32Servo", "ESP32Servo.h", "Servo na ESP32 (a Servo.h da Arduino nao roda nela).",
            "Servo, ESP32PWM", true),
        new("LiquidCrystal", "LiquidCrystal.h", "LCD 16x2 e 20x4 ligado por fios.",
            "LiquidCrystal", true),
        new("LiquidCrystal_I2C", "LiquidCrystal_I2C.h", "O mesmo LCD, com o modulo I2C.",
            "LiquidCrystal_I2C", true),
        new("DHT sensor library", "DHT.h", "Temperatura e umidade do DHT11 e DHT22.",
            "DHT", true),
        new("Adafruit NeoPixel", "Adafruit_NeoPixel.h", "Fitas, aneis e matrizes de LED.",
            "Adafruit_NeoPixel", true),
        new("Stepper", "Stepper.h", "Motor de passo por quatro pinos.", "Stepper", true),
        new("Keypad", "Keypad.h", "Teclado matricial de 4x4.", "Keypad", true),
        new("Adafruit SSD1306", "Adafruit_SSD1306.h", "Display OLED de 128x64.",
            "Adafruit_SSD1306", true),
        new("Adafruit ILI9341", "Adafruit_ILI9341.h", "Display TFT colorido.",
            "Adafruit_ILI9341", true),
        new("Adafruit ST7789", "Adafruit_ST7789.h", "Display colorido de 240x240.",
            "Adafruit_ST7789", true),
        new("IRremote", "IRremote.h", "Recebe o codigo do controle infravermelho.",
            "IRrecv, decode_results", true),
        new("MPU6050", "MPU6050.h", "Acelerometro e giroscopio.",
            "Adafruit_MPU6050, MPU6050", true),
        new("RTCLib", "RTClib.h", "Relogio de tempo real DS1307.", "RTC_DS1307", true),
        new("HX711", "HX711.h", "Celula de carga.", "HX711", true),
        new("SD", "SD.h", "Arquivos no cartao microSD.", "SD, File", true),
        new("Wire", "Wire.h", "O barramento I2C.", "Wire", true),
        new("SPI", "SPI.h", "O barramento SPI.", "SPI", true),
        new("WiFi", "WiFi.h", "Rede sem fio da ESP32 e da Nano RP2040.", "WiFi", true),
        new("WebServer", "WebServer.h", "Servidor de paginas na propria placa.",
            "WebServer, WiFiServer", true),
        new("HTTPClient", "HTTPClient.h", "Pedidos HTTP para fora.", "HTTPClient", true),
        new("PubSubClient", "PubSubClient.h", "MQTT.", "PubSubClient", true),
        new("BluetoothSerial", "BluetoothSerial.h", "Bluetooth classico da ESP32.",
            "BluetoothSerial", true),
        new("BLE", "BLEDevice.h",
            "Bluetooth LE da ESP32: servidor GATT, servicos, caracteristicas e notificacoes.",
            "BLEDevice, BLEServer, BLEService, BLECharacteristic", true),
        new("RF24", "RF24.h", "Radio NRF24L01.", "RF24", true),

        // O SDK em C da Raspberry Pi Pico. Nao e biblioteca do
        // Arduino: e o jeito oficial de programar a Pico sem ele, e o
        // pedido da Alpha 18 o chama de "Pi Pico SDK (Avançado)".
        new("Pico SDK", "pico/stdlib.h",
            "O SDK em C da Raspberry Pi Pico: gpio_put, sleep_ms, o main().",
            "gpio_put, gpio_set_dir, sleep_ms", true),
        new("Pico SDK — ADC", "hardware/adc.h",
            "Conversor analogico da Pico, pelo SDK em C.",
            "adc_init, adc_read", true),
        new("Pico SDK — PWM", "hardware/pwm.h",
            "PWM da Pico, pelo SDK em C.",
            "pwm_set_gpio_level", true),
        // O ESP8266 tem os proprios nomes de arquivo, mas a mesma API da
        // ESP32 -- e o que a NodeMCU e os modulos ESP-01 a ESP-14 incluem.
        new("ESP8266WiFi", "ESP8266WiFi.h", "WiFi do ESP8266 (NodeMCU e modulos ESP-01 a ESP-14).",
            "WiFi, WiFiClient, WiFiServer", true),
        new("ESP8266WebServer", "ESP8266WebServer.h", "Servidor de paginas no ESP8266.",
            "ESP8266WebServer", true),
        new("ESP8266HTTPClient", "ESP8266HTTPClient.h", "Pedidos HTTP no ESP8266.",
            "HTTPClient", true),
        new("ESP-Now", "esp_now.h", "Mensagens diretas entre ESP32.",
            "esp_now_send, esp_now_init", true),
        new("UniversalTelegramBot", "UniversalTelegramBot.h",
            "Bot do Telegram. Fala com a api.telegram.org de verdade.",
            "UniversalTelegramBot", true),
        new("Preferences", "Preferences.h",
            "Memoria da ESP32 que sobrevive ao reset.", "Preferences", true),
    ];

    /// <summary>
    /// Cabecalhos que o motor aceita sem ter classe propria: o core do
    /// Arduino e os que vem junto de uma biblioteca que ele ja executa.
    /// </summary>
    static readonly string[] Extras =
    [
        "Arduino.h", "math.h", "string.h", "stdio.h", "stdlib.h",
        "stdint.h", "EEPROM.h", "WiFiClient.h", "WiFiClientSecure.h",
        "nRF24L01.h", "printf.h", "esp_wifi.h", "Adafruit_GFX.h",
        "time.h", "sys/time.h", "esp_sntp.h", "WiFiUdp.h",

        // A IRremote.hpp e a mesma biblioteca da IRremote.h,
        // na forma nova de incluir; o uri/UriBraces.h vem
        // dentro do WebServer. As duas ja sao executadas.
        "IRremote.hpp", "uri/UriBraces.h", "uri/UriRegex.h",
        "Adafruit_Sensor.h", "IRremoteESP8266.h", "IRrecv.h",

        // O ESP-IDF que vem dentro do core da ESP32: o sono profundo, os
        // pinos do RTC e os atributos de memoria. E o Adafruit_ST77xx.h,
        // que o Adafruit_ST7789.h inclui.
        "esp_sleep.h", "driver/rtc_io.h", "driver/gpio.h", "esp_system.h",
        "esp_attr.h", "Adafruit_ST77xx.h",

        // A BLE do core: o BLEDevice.h e o principal; os outros vem junto.
        "BLEServer.h", "BLEUtils.h", "BLE2902.h", "BLE2901.h", "BLE2904.h", "BLEUUID.h",
        "BLEAdvertising.h", "BLECharacteristic.h", "BLEService.h", "BLEDescriptor.h",
        "BLEAddress.h", "BLESecurity.h",
    ];

    static HashSet<string> doSimulador;

    /// <summary>
    /// O cabecalho e de uma biblioteca que o simulador executa por dentro?
    /// Esses vencem a copia instalada: a Servo de verdade mexe nos timers
    /// do chip, a do simulador move o servo que esta no circuito.
    /// </summary>
    public static bool EhDoSimulador(string cabecalho)
    {
        doSimulador ??= new HashSet<string>(
            DoSimulador.Select(b => b.Cabecalho).Concat(Extras), StringComparer.OrdinalIgnoreCase);
        return cabecalho is not null && doSimulador.Contains(cabecalho);
    }

    /// <summary>Todas: as do simulador, as que vem junto em codigo e as que a pessoa instalou.</summary>
    public static List<BibliotecaDoAcervo> Todas()
    {
        var lista = new List<BibliotecaDoAcervo>(DoSimulador);
        var instaladas = Instaladas();
        foreach (var junto in VemJunto())
        {
            if (instaladas.Any(b => string.Equals(b.Nome, junto.Nome, StringComparison.OrdinalIgnoreCase))) continue;
            var cabecalhos = CabecalhosDe(junto).ToList();
            // O "includes=" do library.properties e o que a IDE poe no
            // sketch; sem ele, o .h com o nome da biblioteca.
            string principal = Propriedade(junto.Pasta, "includes")?.Split(',')[0].Trim() is { Length: > 0 } declarado
                ? declarado
                : cabecalhos.FirstOrDefault(h => string.Equals(
                      Path.GetFileNameWithoutExtension(h), junto.Nome, StringComparison.OrdinalIgnoreCase))
                  ?? cabecalhos.FirstOrDefault() ?? junto.Nome + ".h";
            string frase = Propriedade(junto.Pasta, "sentence");
            lista.Add(new BibliotecaDoAcervo(
                junto.Nome,
                principal,
                (string.IsNullOrWhiteSpace(frase) ? "" : frase.Trim() + " ")
                + "Vem com o simulador, em código C: roda no modo nativo, como na placa.",
                principal,
                true));
        }
        lista.AddRange(instaladas);
        return lista;
    }

    public static List<BibliotecaDoAcervo> Instaladas()
    {
        var lista = new List<BibliotecaDoAcervo>();

        try
        {
            if (!Directory.Exists(Pasta)) return lista;

            foreach (string pasta in Directory.GetDirectories(Pasta))
            {
                var biblioteca = Carregar(pasta);
                var cabecalhos = CabecalhosDe(biblioteca).ToList();
                string nome = Path.GetFileName(pasta);

                // O cabecalho com o nome da pasta e o que se inclui: e a
                // regra da IDE para escolher entre bibliotecas.
                string principal = cabecalhos.FirstOrDefault(h => string.Equals(
                                       Path.GetFileNameWithoutExtension(h), nome, StringComparison.OrdinalIgnoreCase))
                                   ?? cabecalhos.FirstOrDefault() ?? nome + ".h";

                string frase = Propriedade(pasta, "sentence");
                lista.Add(new BibliotecaDoAcervo(
                    nome,
                    principal,
                    (string.IsNullOrWhiteSpace(frase) ? "" : frase.Trim() + " ")
                    + "Instalada por você: o simulador roda o código C++ dela.",
                    string.Join(", ", cabecalhos.Take(4)),
                    false)
                {
                    Pasta = pasta,
                    Arquivos = cabecalhos.Count,
                    Bytes = TamanhoDaPasta(pasta),
                });
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }

        return lista;
    }

    static readonly HashSet<string> ExtensoesDeCodigo =
        new([".h", ".hpp", ".hh", ".c", ".cpp", ".cc", ".cxx", ".inl", ".ipp", ".tpp"],
            StringComparer.OrdinalIgnoreCase);

    /// <summary>Pastas de uma biblioteca que nao sao codigo dela: exemplos, testes, documentacao.</summary>
    static bool ForaDoCodigo(string relativo)
    {
        string primeiro = relativo.Split('/')[0].ToLowerInvariant();
        return primeiro is "examples" or "example" or "extras" or "test" or "tests" or "docs" or "doc"
            or ".github" or ".git" or ".vscode";
    }

    /// <summary>
    /// O formato novo das bibliotecas (o da Arduino IDE 1.5 em diante):
    /// library.properties na raiz e o codigo dentro de src/.
    /// </summary>
    static bool FormatoNovo(string pasta)
        => Directory.Exists(Path.Combine(pasta, "src"))
           && (File.Exists(Path.Combine(pasta, "library.properties"))
               || !Directory.EnumerateFiles(pasta, "*.h").Any());

    /// <summary>Uma pasta de biblioteca, do jeito que o motor le.</summary>
    public static BibliotecaDeFora Carregar(string pasta)
    {
        var arquivos = new List<string>();
        try
        {
            arquivos = Directory.EnumerateFiles(pasta, "*.*", SearchOption.AllDirectories)
                .Where(a => ExtensoesDeCodigo.Contains(Path.GetExtension(a)))
                .Select(a => Path.GetRelativePath(pasta, a).Replace('\\', '/'))
                .Where(r => !ForaDoCodigo(r))
                .OrderBy(r => r, StringComparer.Ordinal)
                .ToList();
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }

        return new BibliotecaDeFora
        {
            Nome = Path.GetFileName(pasta.TrimEnd(Path.DirectorySeparatorChar)),
            Pasta = pasta,
            Arquivos = arquivos,
            Raiz = FormatoNovo(pasta) ? "src/" : "",
            Ler = relativo => File.ReadAllText(Path.Combine(pasta, relativo.Replace('/', Path.DirectorySeparatorChar))),
        };
    }

    /// <summary>
    /// As instaladas e as que vem junto, prontas para o motor compilar com o
    /// sketch. Uma instalada vence a que vem junto com o mesmo nome: quem
    /// instalou uma versao sua quer usar a sua.
    /// </summary>
    public static List<BibliotecaDeFora> ParaCompilar()
    {
        var lista = new List<BibliotecaDeFora>();
        try
        {
            if (Directory.Exists(Pasta))
                foreach (string pasta in Directory.GetDirectories(Pasta).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
                    lista.Add(Carregar(pasta));
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }

        foreach (var junto in VemJunto())
            if (!lista.Any(b => string.Equals(b.Nome, junto.Nome, StringComparison.OrdinalIgnoreCase)))
                lista.Add(junto);
        return lista;
    }

    /// <summary>
    /// As bibliotecas em codigo que vem com o simulador (a pasta
    /// "bibliotecas" ao lado do programa), como a ESP32-DOOM: o codigo e o
    /// de verdade, que a placa tambem compila, e o motor roda como o de uma
    /// instalada.
    /// </summary>
    public static List<BibliotecaDeFora> VemJunto()
    {
        var lista = new List<BibliotecaDeFora>();
        try
        {
            string raiz = PastaDasQueVemJunto;
            if (raiz is null) return lista;
            foreach (string pasta in Directory.GetDirectories(raiz).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
                lista.Add(Carregar(pasta));
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return lista;
    }

    /// <summary>
    /// A pasta "bibliotecas" do simulador: ao lado do .exe quando instalado,
    /// ou algumas pastas acima quando roda do codigo-fonte.
    /// </summary>
    public static string PastaDasQueVemJunto
    {
        get
        {
            if (pastaDasQueVemJunto is not null) return pastaDasQueVemJunto.Length == 0 ? null : pastaDasQueVemJunto;
            string achada = "";
            string dir = AppContext.BaseDirectory;
            for (int i = 0; i < 6 && dir is not null; i++)
            {
                string candidata = Path.Combine(dir, "bibliotecas");
                if (Directory.Exists(candidata)) { achada = candidata; break; }
                dir = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar));
            }
            pastaDasQueVemJunto = achada;
            return achada.Length == 0 ? null : achada;
        }
    }
    static string pastaDasQueVemJunto;

    /// <summary>Os cabecalhos que um #include &lt;...&gt; acha nesta biblioteca.</summary>
    static IEnumerable<string> CabecalhosDe(BibliotecaDeFora b)
        => b.Arquivos
            .Where(a => Path.GetExtension(a).ToLowerInvariant() is ".h" or ".hpp" or ".hh")
            .Where(a => b.Raiz.Length == 0 || a.StartsWith(b.Raiz, StringComparison.OrdinalIgnoreCase))
            .Select(a => a[b.Raiz.Length..]);

    /// <summary>Um campo do library.properties ("name", "sentence"), ou null.</summary>
    static string Propriedade(string pasta, string chave)
    {
        try
        {
            string arquivo = Path.Combine(pasta, "library.properties");
            if (!File.Exists(arquivo)) return null;
            foreach (string linha in File.ReadLines(arquivo))
            {
                int igual = linha.IndexOf('=');
                if (igual > 0 && linha[..igual].Trim() == chave) return linha[(igual + 1)..].Trim();
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return null;
    }

    /// <summary>
    /// O nome da pasta no acervo. Vem do library.properties quando tem,
    /// com os espacos trocados -- "Adafruit NeoPixel" vira
    /// "Adafruit_NeoPixel", como a Arduino IDE faz. Sem ele, o nome da
    /// pasta, sem o "-master" que o GitHub poe no .zip.
    /// </summary>
    public static string NomeDaBiblioteca(string pasta)
    {
        string nome = Propriedade(pasta, "name");
        if (string.IsNullOrWhiteSpace(nome))
        {
            nome = Path.GetFileName(pasta.TrimEnd(Path.DirectorySeparatorChar));
            foreach (string sufixo in new[] { "-master", "-main", "-develop" })
                if (nome.EndsWith(sufixo, StringComparison.OrdinalIgnoreCase)) nome = nome[..^sufixo.Length];

            // "Bounce2-2.71" -- a versao que o GitHub poe no nome do .zip.
            nome = System.Text.RegularExpressions.Regex.Replace(nome, @"-v?\d+(\.\d+)+$", "");
        }

        return NomeLimpo(nome);
    }

    /// <summary>O nome da pasta de uma biblioteca: "DHT sensor library" vira "DHT_sensor_library".</summary>
    public static string NomeLimpo(string nome)
    {
        var limpo = new System.Text.StringBuilder();
        foreach (char c in (nome ?? "").Trim())
            limpo.Append(char.IsLetterOrDigit(c) || c is '_' or '-' or '.' ? c : '_');
        return limpo.Length == 0 ? "biblioteca" : limpo.ToString();
    }

    /// <summary>Copia uma pasta de biblioteca para o acervo.</summary>
    public static string Instalar(string origem)
    {
        if (!Directory.EnumerateFiles(origem, "*.*", SearchOption.AllDirectories)
                .Any(a => Path.GetExtension(a).ToLowerInvariant() is ".h" or ".hpp" or ".hh"))
            throw new InvalidOperationException(
                "Essa pasta não tem nenhum arquivo .h, então não parece uma biblioteca do Arduino. "
                + "Escolha a pasta que tem os .h (ou a src/ dentro dela).");

        string nome = NomeDaBiblioteca(origem);
        string destino = Path.Combine(Pasta, nome);

        Directory.CreateDirectory(destino);

        foreach (string arquivo in Directory.GetFiles(origem, "*.*", SearchOption.AllDirectories))
        {
            string relativo = Path.GetRelativePath(origem, arquivo);
            string alvo = Path.Combine(destino, relativo);

            Directory.CreateDirectory(Path.GetDirectoryName(alvo));
            File.Copy(arquivo, alvo, overwrite: true);
        }

        return nome;
    }

    /// <summary>
    /// Instala a partir de um .zip -- o "Adicionar biblioteca .ZIP" da
    /// Arduino IDE, e o jeito como toda biblioteca sai do GitHub.
    /// </summary>
    public static string InstalarZip(string arquivoZip)
    {
        string temporaria = Path.Combine(Path.GetTempPath(), "circuitsketch-zip-" + Guid.NewGuid().ToString("N"));
        try
        {
            Extrair(arquivoZip, temporaria);
            return Instalar(PastaDaBiblioteca(temporaria));
        }
        finally
        {
            try { if (Directory.Exists(temporaria)) Directory.Delete(temporaria, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>
    /// Abre o .zip sem deixar nada escapar da pasta de destino: um nome
    /// como "../../Windows/x.dll" dentro dele e recusado.
    /// </summary>
    static void Extrair(string arquivoZip, string destino)
    {
        const long Teto = 200L * 1024 * 1024;
        string raiz = Path.GetFullPath(destino) + Path.DirectorySeparatorChar;
        long total = 0;

        using var zip = System.IO.Compression.ZipFile.OpenRead(arquivoZip);
        foreach (var entrada in zip.Entries)
        {
            string alvo = Path.GetFullPath(Path.Combine(destino, entrada.FullName));
            if (!alvo.StartsWith(raiz, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"O .zip tem um caminho que sai da pasta: {entrada.FullName}");

            if (entrada.FullName.EndsWith('/') || entrada.FullName.EndsWith('\\'))
            {
                Directory.CreateDirectory(alvo);
                continue;
            }

            total += entrada.Length;
            if (total > Teto)
                throw new InvalidOperationException("O .zip passa de 200 MB: grande demais para uma biblioteca.");

            Directory.CreateDirectory(Path.GetDirectoryName(alvo));
            entrada.ExtractToFile(alvo, overwrite: true);
        }
    }

    /// <summary>
    /// A pasta da biblioteca dentro do que o .zip trouxe. O do GitHub vem
    /// com uma pasta so ("Bounce2-master/"), e a biblioteca e ela.
    /// </summary>
    static string PastaDaBiblioteca(string raiz)
    {
        string atual = raiz;
        for (int i = 0; i < 3; i++)
        {
            bool temCodigo = Directory.EnumerateFiles(atual).Any(a => ExtensoesDeCodigo.Contains(Path.GetExtension(a)))
                             || File.Exists(Path.Combine(atual, "library.properties"));
            var dentro = Directory.GetDirectories(atual).Where(d => !Path.GetFileName(d).StartsWith("__MACOSX")).ToArray();
            if (temCodigo || dentro.Length != 1) break;
            atual = dentro[0];
        }
        return atual;
    }

    public static void Remover(BibliotecaDoAcervo biblioteca)
    {
        if (biblioteca?.Pasta is null || !Directory.Exists(biblioteca.Pasta)) return;
        Directory.Delete(biblioteca.Pasta, recursive: true);
    }

    /// <summary>
    /// Quanto uma pasta ocupa, somando tudo o que tem dentro.
    ///
    /// Pasta que sumiu ou que o Windows nao deixa ler devolve zero, e
    /// nao excecao: o tamanho e informacao, e nao motivo para a janela
    /// deixar de abrir.
    /// </summary>
    public static long TamanhoDaPasta(string pasta)
    {
        try
        {
            if (!Directory.Exists(pasta)) return 0;
            return Directory.GetFiles(pasta, "*.*", SearchOption.AllDirectories)
                .Sum(a => new FileInfo(a).Length);
        }
        catch (IOException) { return 0; }
        catch (UnauthorizedAccessException) { return 0; }
    }

    /// <summary>O tamanho do jeito que se le: "1,4 MB", "312 KB".</summary>
    public static string EmTexto(long bytes)
    {
        if (bytes <= 0) return "0 KB";
        if (bytes < 1024) return bytes + " B";
        if (bytes < 1024 * 1024) return (bytes / 1024.0).ToString("0") + " KB";
        return (bytes / (1024.0 * 1024)).ToString("0.0") + " MB";
    }

    /// <summary>
    /// Onde as bibliotecas do Arduino costumam morar neste computador.
    ///
    /// A esmagadora maioria dos "faltou a biblioteca" da turma e de uma
    /// biblioteca que a pessoa JA TEM: ela instalou pela Arduino IDE e
    /// o simulador nao estava olhando para la. Procurar aqui transforma
    /// o aviso num botao que resolve.
    /// </summary>
    public static IEnumerable<string> PastasDoArduino()
    {
        foreach (var especial in new[]
                 {
                     Environment.SpecialFolder.MyDocuments,
                     Environment.SpecialFolder.UserProfile,
                 })
        {
            string raiz = Environment.GetFolderPath(especial);
            if (string.IsNullOrEmpty(raiz)) continue;

            yield return Path.Combine(raiz, "Arduino", "libraries");

            // O OneDrive move a pasta Documentos de lugar, e e o caso
            // desta maquina e da metade da turma.
            yield return Path.Combine(raiz, "OneDrive", "Documentos", "Arduino", "libraries");
            yield return Path.Combine(raiz, "OneDrive", "Documents", "Arduino", "libraries");
            yield return Path.Combine(raiz, "Documentos", "Arduino", "libraries");
        }
    }

    /// <summary>
    /// Procura no computador uma pasta de biblioteca que tenha este .h.
    ///
    /// Devolve null quando nao acha -- e ai o aviso diz so o que falta,
    /// sem prometer um botao que nao resolveria nada.
    /// </summary>
    public static string ProcurarPasta(string cabecalho)
    {
        string alvo = Path.GetFileName(cabecalho);
        if (string.IsNullOrWhiteSpace(alvo)) return null;

        foreach (string raiz in PastasDoArduino())
        {
            try
            {
                if (!Directory.Exists(raiz)) continue;

                foreach (string pasta in Directory.GetDirectories(raiz))
                {
                    var achados = Directory.GetFiles(pasta, alvo, SearchOption.AllDirectories);
                    if (achados.Length > 0) return pasta;
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        return null;
    }

    /// <summary>
    /// O que falta para este sketch rodar, ja com tamanho e candidata.
    ///
    /// Serve as duas linguagens: o #include do C e o import do Python
    /// fazem a mesma pergunta, e a resposta tem que ser a mesma janela.
    /// </summary>
    public static List<Faltando> OQueFalta(string sketch, IDictionary<string, string> arquivos = null)
    {
        var lista = new List<Faltando>();

        var comAspas = ComAspas(sketch);

        foreach (string cabecalho in Compilador.Detectar(sketch) == Linguagem.MicroPython
                 ? ModulosSemDono(sketch)
                 : SemDono(sketch))
        {
            // Arquivo que o projeto JA tem: nao falta nada.
            if (arquivos is not null && arquivos.ContainsKey(cabecalho)) continue;

            // Arquivo do projeto nao se procura na pasta das
            // bibliotecas: ele simplesmente nao existe ainda.
            if (comAspas.Contains(cabecalho))
            {
                lista.Add(new Faltando(cabecalho, null, 0, 0) { DoProjeto = true });
                continue;
            }

            string candidata = ProcurarPasta(cabecalho);
            lista.Add(new Faltando(
                cabecalho,
                candidata,
                candidata is null ? 0 : TamanhoDaPasta(candidata),
                candidata is null
                    ? 0
                    : Directory.GetFiles(candidata, "*.*", SearchOption.AllDirectories).Length));
        }

        return lista;
    }

    /// <summary>
    /// Os #include escritos com ASPAS -- os do proprio projeto.
    /// </summary>
    public static HashSet<string> ComAspas(string sketch)
    {
        var achados = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(sketch)) return achados;

        foreach (var linha in sketch.Replace("\r\n", "\n").Split('\n'))
        {
            string limpa = linha.Trim();
            if (!limpa.StartsWith("#include", StringComparison.Ordinal)) continue;

            int abre = limpa.IndexOf('\"');
            if (abre < 0) continue;

            int fecha = limpa.IndexOf('\"', abre + 1);
            if (fecha > abre + 1) achados.Add(limpa[(abre + 1)..fecha].Trim());
        }

        return achados;
    }

    /// <summary>
    /// Os "import" do sketch em MicroPython que o simulador nao tem.
    ///
    /// Aqui nao ha pasta para instalar: os modulos do MicroPython moram
    /// DENTRO do firmware da placa, e o simulador implementa os que
    /// implementa. O aviso e honesto sobre isso.
    /// </summary>
    public static List<string> ModulosSemDono(string sketch)
    {
        var faltando = new List<string>();
        if (string.IsNullOrWhiteSpace(sketch)) return faltando;

        foreach (var linha in sketch.Replace("\r\n", "\n").Split('\n'))
        {
            string limpa = linha.Trim();
            string modulo = null;

            if (limpa.StartsWith("import ", StringComparison.Ordinal))
                modulo = limpa[7..].Split([' ', ',', '#'], StringSplitOptions.RemoveEmptyEntries)
                    .FirstOrDefault();
            else if (limpa.StartsWith("from ", StringComparison.Ordinal))
                modulo = limpa[5..].Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .FirstOrDefault();

            if (string.IsNullOrWhiteSpace(modulo) || ModulosPy.Existe(modulo)) continue;
            if (!faltando.Contains(modulo, StringComparer.Ordinal)) faltando.Add(modulo);
        }

        return faltando;
    }

    /// <summary>
    /// Os #include do sketch que ninguem atende.
    ///
    /// E a pergunta que o aluno faz de verdade -- "por que nao roda?" --
    /// e ate aqui a resposta so aparecia como erro na linha em que a
    /// biblioteca era usada, bem longe do #include.
    /// </summary>
    public static List<string> SemDono(string sketch)
    {
        var faltando = new List<string>();
        if (string.IsNullOrWhiteSpace(sketch)) return faltando;

        var conhecidos = DoSimulador
            .Select(b => b.Cabecalho)
            .Concat(Extras)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Todo cabecalho de biblioteca instalada: o #include acha, e o
        // motor roda o que tem dentro.
        foreach (var instalada in ParaCompilar())
            foreach (string cabecalho in CabecalhosDe(instalada))
                conhecidos.Add(cabecalho);

        foreach (var linha in sketch.Replace("\r\n", "\n").Split('\n'))
        {
            string limpa = linha.Trim();
            if (!limpa.StartsWith("#include", StringComparison.Ordinal)) continue;

            int abre = limpa.IndexOfAny(['<', '"']);
            int fecha = limpa.LastIndexOfAny(['>', '"']);
            if (abre < 0 || fecha <= abre + 1) continue;

            string cabecalho = limpa[(abre + 1)..fecha].Trim();
            if (cabecalho.Length == 0 || conhecidos.Contains(cabecalho)) continue;
            if (!faltando.Contains(cabecalho, StringComparer.OrdinalIgnoreCase))
                faltando.Add(cabecalho);
        }

        return faltando;
    }
}
