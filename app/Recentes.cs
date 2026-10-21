using System.IO;

namespace Simulador.App;

/// <summary>
/// Os últimos projetos abertos.
///
/// Fica em %APPDATA% e não junto do executável: nos computadores do
/// laboratório o aluno costuma não ter escrita na pasta do programa, e
/// uma lista que some a cada aula não serve para nada.
/// </summary>
public static class Recentes
{
    /// <summary>Quantos projetos a lista guarda: 8 de fábrica, e as configurações mudam.</summary>
    public static int Maximo => Preferencias.Numero(Preferencias.QuantosRecentes, 8, 3, 30);

    static string Pasta => Armazem.Pasta;

    static string Arquivo => Path.Combine(Pasta, "recentes.txt");

    /// <summary>
    /// Lista em ordem, do mais recente para o mais antigo, já sem os
    /// arquivos que não existem mais -- apontar para um projeto apagado
    /// só serviria para dar erro ao clicar.
    /// </summary>
    public static List<string> Ler()
    {
        try
        {
            if (!File.Exists(Arquivo)) return [];
            return [.. File.ReadAllLines(Arquivo)
                        .Select(l => l.Trim())
                        .Where(l => l.Length > 0 && File.Exists(l))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Take(Maximo)];
        }
        catch { return []; }
    }

    /// <summary>Põe o caminho no topo; abrir de novo não duplica.</summary>
    public static void Registrar(string caminho)
    {
        if (string.IsNullOrWhiteSpace(caminho)) return;

        try
        {
            string cheio = Path.GetFullPath(caminho);
            var lista = Ler();
            lista.RemoveAll(l => string.Equals(l, cheio, StringComparison.OrdinalIgnoreCase));
            lista.Insert(0, cheio);

            Directory.CreateDirectory(Pasta);
            File.WriteAllLines(Arquivo, lista.Take(Maximo));
        }
        catch
        {
            // Sem escrita em %APPDATA% o programa segue: perder a lista
            // de recentes não é motivo para atrapalhar a aula.
        }
    }

    public static void Limpar()
    {
        try { if (File.Exists(Arquivo)) File.Delete(Arquivo); } catch { }
    }
}

/// <summary>
/// Os exemplos que vêm dentro do programa, um por placa.
///
/// Vão embutidos no assembly, como os SVGs: o executável é um arquivo só
/// e precisa continuar sendo. Assim o aluno tem por onde começar mesmo
/// numa máquina recém-formatada, sem baixar nada.
/// </summary>
public static class Demos
{
    /// <param name="Grupo">
    /// Onde a demo entra no menu, quando não é a família da placa. As de
    /// linguagem ficam juntas: quem procura "Rust" não sabe, nem deveria
    /// precisar saber, que ela roda num Uno.
    /// </param>
    public record Demo(string Recurso, string Nome, string Placa, string Grupo = null);

    /// <summary>O grupo das demos que existem para mostrar uma linguagem.</summary>
    public const string GrupoDasLinguagens = "Linguagens";

    static readonly Dictionary<string, string> tipos = [];

    /// <summary>
    /// O id da placa da demo, lido do próprio .sktch -- e guardado.
    ///
    /// Lido do arquivo, e não escrito numa tabela ao lado: é o circuito
    /// que diz qual placa ele usa, e uma tabela ficaria desatualizada na
    /// primeira demo que trocasse de placa.
    /// </summary>
    public static string TipoDaPlaca(Demo demo)
    {
        lock (tipos)
        {
            if (tipos.TryGetValue(demo.Recurso, out string guardado)) return guardado;

            string achado = null;
            foreach (System.Text.RegularExpressions.Match m in
                     System.Text.RegularExpressions.Regex.Matches(
                         Texto(demo.Recurso) ?? "", "\"Tipo\"\\s*:\\s*\"([^\"]+)\""))
            {
                if (!PerfilPlaca.EhPlaca(m.Groups[1].Value)) continue;
                achado = m.Groups[1].Value;
                break;
            }

            tipos[demo.Recurso] = achado;
            return achado;
        }
    }

    /// <summary>O submenu da demo em "Exemplos prontos".</summary>
    public static string GrupoDe(Demo demo)
        => demo.Grupo ?? Catalogo.Familia(TipoDaPlaca(demo)) ?? "Outras";

    /// <summary>
    /// As demos em grupos, na ordem do menu: as famílias na ordem das
    /// subabas da paleta, e as linguagens por último.
    ///
    /// Com a Alpha 18 eram 49 demos num submenu só -- mais alto que uma
    /// tela de 1080 linhas, e as de baixo não tinham como ser clicadas.
    /// </summary>
    public static IEnumerable<IGrouping<string, Demo>> EmGrupos()
    {
        var ordem = Catalogo.Familias().ToList();
        return Todas.GroupBy(GrupoDe)
                    .OrderBy(g => g.Key == GrupoDasLinguagens ? int.MaxValue
                                  : ordem.IndexOf(g.Key) is int i && i >= 0 ? i : int.MaxValue - 1);
    }

    /// <summary>
    /// As demos de uma familia, por placa, na ordem da lista. E o segundo
    /// nivel do menu: placa com uma demo so vira item direto, e placa com
    /// varias ganha o seu submenu.
    /// </summary>
    public static IEnumerable<IGrouping<string, Demo>> PorPlaca(IEnumerable<Demo> daFamilia)
        => daFamilia.GroupBy(d => d.Placa);

    /// <summary>
    /// Na ordem em que aparecem no menu: por familia, e dentro dela por
    /// placa.
    ///
    /// Ate a Alpha 18, vinte e seis placas abriam com o mesmo pisca-pisca.
    /// Desde a Alpha 19 cada placa tem um projeto proprio, e nenhum se
    /// repete -- cada demo mostra uma coisa que o CircuitSketch faz. As
    /// que nao sao das placas originais saem de
    /// ferramentas/extrair-svg/demos-por-placa.py e demos-linguagens.py.
    /// </summary>
    public static readonly Demo[] Todas =
    [
        // ---- ESP32 ----
        new("esp32-semaforo",         "Semáforo",          "ESP32"),
        new("esp32-wifi-servidor",    "Servidor web (WiFi)", "ESP32"),
        new("esp32-mqtt",             "MQTT",              "ESP32"),
        new("esp32-bluetooth",        "Controle por Bluetooth", "ESP32"),
        new("esp32-espnow",           "ESP-NOW: duas placas conversando", "ESP32"),
        new("esp32s2-relogio-ntp", "Relógio pela internet (NTP) no OLED", "ESP32-S2 DevKit"),
        new("esp32s3-anel-neopixel", "Anel NeoPixel com três efeitos", "ESP32-S3 DevKit"),
        new("esp32s3-base-zero", "BASE ZERO: jogo 3D no estilo DOOM", "ESP32-S3 DevKit"),
        new("esp32s3-doom", "ESP32-DOOM: o DOOM de verdade (PrBoom, em C)", "ESP32-S3 DevKit"),
        new("esp32s3-player-dap", "Player de música (DAP): cartão, pendrive, roda e bateria", "ESP32-S3 DevKit"),
        new("esp32c3-estufa", "Estufa: DHT22, janela com servo e LCD", "ESP32-C3 DevKit"),
        new("esp32c3-ble-lampada", "Lâmpada BLE: o celular controla pelo Bluetooth LE", "ESP32-C3 DevKit"),
        new("esp32c6-alarme", "Alarme com sensor de presença", "ESP32-C6 DevKit"),
        new("esp32h2-estacionamento", "Sensor de estacionamento (ultrassom)", "ESP32-H2 DevKit"),

        // ---- ESP8266 ----
        new("nodemcu-servidor", "Servidor web: LED e botão na página", "NodeMCU (ESP8266)"),
        new("esp01-tomada-mqtt", "Tomada inteligente por MQTT", "ESP-01"),
        new("esp02-campainha", "Campainha musical", "ESP-02"),
        new("esp03-termo-higrometro", "Termo-higrômetro com DHT11", "ESP-03"),
        new("esp04-semaforo-pedestre", "Semáforo com botão de pedestre", "ESP-04"),
        new("esp05-calculadora", "Calculadora pela serial (com analisador no TX)", "ESP-05"),
        new("esp06-genius", "Jogo Genius", "ESP-06"),
        new("esp07-scanner-wifi", "Scanner de redes WiFi com barra de sinal", "ESP-07"),
        new("esp08-reflexo", "Jogo de reflexo", "ESP-08"),
        new("esp09-dado", "Dado eletrônico", "ESP-09"),
        new("esp10-ponte-serial-web", "Ponte entre a serial e uma página web", "ESP-10"),
        new("esp11-minuteria", "Minuteria de escada", "ESP-11"),
        new("esp12-balanca", "Balança com HX711 e OLED", "ESP-12"),
        new("esp12e-rgb-navegador", "Cor do LED RGB pelo navegador", "ESP-12E"),
        new("esp12f-luminaria-mqtt", "Luminária NeoPixel por MQTT", "ESP-12F"),
        new("esp13-fechadura", "Fechadura com teclado e senha", "ESP-13"),
        new("esp14-placar", "Placar no anel de LEDs, pelo MQTT", "ESP-14"),

        // ---- Arduino ----
        new("uno-botao",              "Botão e LED",       "Arduino Uno"),
        new("uno-analisador",         "Analisador lógico: PWM, som, servo, serial e shiftOut", "Arduino Uno"),
        new("mega-barra",             "Barra de LEDs",     "Arduino Mega"),
        new("nano-potenciometro",     "Potenciômetro",     "Arduino Nano"),
        new("rp2040-servo",           "Servo",             "Nano RP2040"),
        new("franzininho-vela", "Vela eletrônica", "Franzininho"),
        new("attiny85-luz-noturna", "Luz noturna automática", "ATtiny85"),

        // ---- Raspberry Pi ----
        //
        // A Pico e a Pico W tem tres demos cada, uma por jeito de
        // programa-las: o core do Arduino, o MicroPython e o SDK em C.
        new("pico-piano", "Piano de oito teclas (Arduino)", "Raspberry Pi Pico"),
        new("pico-joystick-oled", "Joystick e OLED (MicroPython)", "Raspberry Pi Pico"),
        new("pico-sdk-respirar", "LED respirando com PWM (SDK em C)", "Raspberry Pi Pico"),
        new("picow-estacao-web", "Estação do tempo na web (Arduino)", "Raspberry Pi Pico W"),
        new("picow-mqtt", "MQTT em MicroPython", "Raspberry Pi Pico W"),
        new("picow-sdk-voltimetro", "Voltímetro com o ADC (SDK em C)", "Raspberry Pi Pico W"),

        // ---- STM32 ----
        new("stm32-blink",            "LED e botão",       "STM32 Blue Pill"),
        new("nucleo-l031-termometro", "Termômetro com NTC e alarme", "ST Nucleo-L031K6"),
        new("nucleo-c031-relogio-rtc", "Relógio com RTC e LCD", "ST Nucleo-C031C6"),

        // ---- uma por linguagem ----
        //
        // Cada uma faz um projeto que combina com ela, e nao mais o mesmo
        // pisca-pisca: os contratos do SPARK, os bits do Cowgol, o PORTB
        // inteiro num OUT do Assembly.
        new("ada-semaforo", "Semáforo em Ada", "Arduino Uno", GrupoDasLinguagens),
        new("spark-termostato", "Termostato com contratos em SPARK", "Arduino Uno", GrupoDasLinguagens),
        new("cowgol-contador", "Contador de apertos em Cowgol", "Arduino Uno", GrupoDasLinguagens),
        new("millipascal-respirar", "LED respirando em Millipascal", "Arduino Uno", GrupoDasLinguagens),
        new("rust-sirene", "Sirene em Rust", "Arduino Uno", GrupoDasLinguagens),
        new("csharp-medidor-de-luz", "Medidor de luz em C#", "ESP32", GrupoDasLinguagens),

        // O Assembly e AVR: so roda num ATmega.
        new("assembly-contador", "Contador binário em Assembly", "Arduino Uno", GrupoDasLinguagens),
    ];

    /// <summary>
    /// Conteúdo do .sktch embutido, ou null se sumiu do assembly.
    ///
    /// O nome do recurso usa o RootNamespace, não o AssemblyName, e
    /// procurar pelo sufixo já evitou duas vezes que renomear um dos
    /// dois quebrasse tudo em silêncio.
    /// </summary>
    public static string Texto(string recurso)
    {
        var assembly = typeof(Demos).Assembly;
        string sufixo = $".demos.{recurso}.sktch";

        string nome = assembly.GetManifestResourceNames()
            .FirstOrDefault(r => r.EndsWith(sufixo, StringComparison.OrdinalIgnoreCase));
        if (nome is null) return null;

        using var fluxo = assembly.GetManifestResourceStream(nome);
        if (fluxo is null) return null;
        using var leitor = new StreamReader(fluxo);
        return leitor.ReadToEnd();
    }
}
