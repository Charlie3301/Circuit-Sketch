using System.IO;
using System.Reflection;
using System.Text.Json;

namespace Simulador.App;

public record PinoDef(string Nome, double X, double Y, string[] Sinais, string Descricao);

public record PecaDef(
    string Id,
    string Nome,
    double Largura,
    double Altura,
    IReadOnlyList<PinoDef> Pinos,
    string Categoria);

/// <summary>
/// Catalogo de pecas: lido de assets/pecas.json, gerado por
/// ferramentas/extrair-svg a partir dos wokwi-elements.
///
/// A protoboard nao existe nos wokwi-elements, entao e sintetizada aqui:
/// como os furos seguem passo fixo, sai mais barato calcular do que
/// desenhar 700 elementos.
/// </summary>
public static class Catalogo
{
    /// <summary>Passo padrao de 0,1" (2,54 mm) em pixels de 96 dpi.</summary>
    public const double Passo = 2.54 * 96 / 25.4;   // ~9,6 px

    public const string IdProtoboard = "protoboard-400";
    public const string IdProtoboard830 = "protoboard-830";
    public const string IdProtoboard170 = "protoboard-170";

    // Geometria em passos de 0,1", contada de cima para baixo:
    //   0    margem
    //   1    trilho + (linha 1)
    //   2    trilho - (linha 2)
    //   4    A .. 8  E
    //   9-10 canal central (0,3")
    //  11    F .. 15 J
    //  17    trilho + (linha 1)
    //  18    trilho - (linha 2)
    //
    // O canal tem 0,3" de E a F, nao 0,2": e a medida que faz um DIP de
    // 0,3" montar a cavalo nele, e da area central 11 passos de A a J.
    // Com 10 a ESP32 -- que tem exatamente 1" entre as fileiras -- cobria
    // as dez linhas e nao sobrava nenhuma para ligar fio. Na bancada
    // sobra uma, que e o de que todo mundo reclama na DevKit V1.
    const int LinhaTrilhoTopo = 1;
    const int LinhaA = 4;
    const int LinhaF = 11;
    const int LinhaTrilhoBase = 17;
    const int LinhasTotais = 19;

    /// <summary>
    /// Onde fica cada linha de uma protoboard. As de 400 e 830 tem
    /// trilhos de alimentacao em cima e embaixo; a mini de 170 nao tem --
    /// e so a area central, e por isso ela e tao mais baixa.
    /// </summary>
    public record Formato(int Colunas, int A, int F, int TrilhoTopo,
                          int TrilhoBase, int Total, int GruposDeTrilho)
    {
        public bool TemTrilhos => GruposDeTrilho > 0;
        public int E => A + 4;
        public int J => F + 4;
    }

    static readonly Dictionary<string, Formato> formatos = new()
    {
        [IdProtoboard]    = new(30, LinhaA, LinhaF, LinhaTrilhoTopo,
                                LinhaTrilhoBase, LinhasTotais, 5),
        [IdProtoboard830] = new(63, LinhaA, LinhaF, LinhaTrilhoTopo,
                                LinhaTrilhoBase, LinhasTotais, 10),

        // Mini de 170 pontos: 17 colunas x 10 linhas, sem trilho. As
        // linhas sobem porque nao ha trilho em cima, e a placa fica com
        // 45,7 x 38,1 mm -- o tamanho das coloridas de bancada.
        [IdProtoboard170] = new(17, 1, 8, 0, 0, 13, 0),
    };

    public static Formato FormatoDe(string id) => formatos.GetValueOrDefault(id);

    static readonly Dictionary<string, PecaDef> porId = [];
    public static IReadOnlyCollection<PecaDef> Todas => porId.Values;

    static Catalogo()
    {
        CarregarJson();
        porId[IdProtoboard] = MontarProtoboard(IdProtoboard, "Protoboard 400");
        porId[IdProtoboard830] = MontarProtoboard(IdProtoboard830, "Protoboard 830");
        porId[IdProtoboard170] = MontarProtoboard(IdProtoboard170, "Protoboard 170");
    }

    public static PecaDef Obter(string id) => porId.GetValueOrDefault(id);

    /// <summary>
    /// Placa de desenvolvimento (ESP32, Arduino...). Elas tem dezenas de
    /// pinos parecidos, e e onde marcar o que esta ligado ajuda.
    /// </summary>
    public static bool EhPlacaDeDesenvolvimento(string id)
        => PerfilPlaca.EhPlaca(id);

    public static bool EhProtoboard(string id) => formatos.ContainsKey(id);

    /// <summary>
    /// A familia da placa -- "Arduino", "ESP32", ... --, ou null se o id
    /// nao for de uma placa de desenvolvimento.
    ///
    /// E o que faz as subabas da paleta existirem sem lista escrita a
    /// mao. A alternativa era uma tabela id->familia, que e exatamente o
    /// tipo de lista que se esquece de atualizar quando entra placa
    /// nova; aqui a placa nova cai sozinha no lugar certo pelo id.
    /// </summary>
    public static string Familia(string id)
    {
        if (id is null || !EhPlacaDeDesenvolvimento(id)) return null;

        // A ordem so importa numa placa: a "nano-rp2040-connect" tem o
        // chip da Raspberry, mas quem a vende e a Arduino, e e no menu
        // da Arduino que alguem vai procurar por ela.
        // O ATtiny85 nao e uma placa da Arduino -- e o chip do
        // Franzininho --, mas e no menu da Arduino que quem programa
        // com a IDE dela vai procurar, e foi assim que o pedido o
        // listou.
        if (id.StartsWith("arduino-") || id.StartsWith("nano-")
            || id == "attiny85" || id == "franzininho") return "Arduino";
        // ESP32 e ESP8266 sao chips diferentes, de geracoes diferentes, e
        // com pacotes diferentes na IDE. Numa aba so, a ESP-01 aparecia no
        // meio das ESP32 e parecia uma delas -- quem procurava a placa
        // que tem na mao tinha que ler vinte e tantos nomes. Separadas
        // desde a Alpha 19.
        if (id.StartsWith("esp32")) return "ESP32";
        if (id.StartsWith("esp")) return "ESP8266";
        if (id.StartsWith("pi-pico") || id.Contains("rp2040")) return "Raspberry Pi";
        if (id.StartsWith("stm32") || id.StartsWith("st-nucleo")) return "STM32";

        // Franzininho e o caso de hoje: placa de verdade, familia de
        // uma so. Chamar de Arduino seria mentira -- e de ATtiny, um
        // detalhe que nao ajuda quem procura pelo nome.
        return "Outras";
    }

    /// <summary>As familias que existem no catalogo, na ordem das subabas.</summary>
    public static IEnumerable<string> Familias()
        => Todas.Select(p => Familia(p.Id))
                .Where(f => f is not null)
                .Distinct()
                .OrderBy(OrdemDaFamilia)
                .ThenBy(f => f);

    /// <summary>"Outras" por ultimo; o resto pela ordem de quem se usa mais.</summary>
    static int OrdemDaFamilia(string familia) => familia switch
    {
        "Arduino" => 0, "ESP32" => 1, "ESP8266" => 2, "Raspberry Pi" => 3, "STM32" => 4, _ => 5,
    };

    /// <summary>
    /// Poe uma peca no catalogo em tempo de execucao.
    ///
    /// E por aqui que os chips proprios entram: o pecas.json vem
    /// embutido no executavel e e gerado pelo extrator, entao nao da
    /// para escrever nele. Registrar de novo o mesmo id troca a peca --
    /// e o que acontece quando alguem edita o .chip.json com o
    /// circuito aberto.
    /// </summary>
    public static void Registrar(PecaDef def)
    {
        if (def is null) return;
        porId[def.Id] = def;
    }

    /// <summary>Tira do catalogo as pecas registradas em execucao.</summary>
    public static void EsquecerRegistradas(string prefixo)
    {
        foreach (string id in porId.Keys.Where(
                     k => k.StartsWith(prefixo, StringComparison.Ordinal)).ToList())
            porId.Remove(id);
    }

    /// <summary>
    /// Endereco de fabrica das pecas que falam I2C, e por qual pino.
    ///
    /// E o que faz o scanner de I2C -- aquele exercicio de varrer 1 a 127
    /// e imprimir quem responde -- dar a resposta do circuito que esta na
    /// tela, e nao uma lista inventada. Quem registra e a janela, na
    /// maquina da placa em que o pino de dados esta ligado.
    /// </summary>
    public static (int Endereco, string Nome, string PinoDeDados) AparelhoDeI2c(string id)
        => id switch
        {
            "lcd1602-i2c" => (0x27, "LCD 16x2 I2C", "SDA"),
            "lcd2004-i2c" => (0x27, "LCD 20x4 I2C", "SDA"),
            "modulo-i2c" => (0x27, "Modulo I2C (PCF8574)", "SDA"),
            "mpu6050" => (0x68, "MPU6050", "SDA"),
            "ds1307" => (0x68, "Relogio DS1307", "SDA"),
            "ssd1306-i2c" => (0x3C, "Display OLED", "SDA"),
            _ => (-1, null, null),
        };

    public static int ColunasDe(string id) => FormatoDe(id)?.Colunas ?? 30;

    /// <summary>Quantos grupos de 5 furos tem cada trilho.</summary>
    public static int GruposDeTrilho(string id) => FormatoDe(id)?.GruposDeTrilho ?? 0;

    /// <summary>
    /// Colunas onde o trilho tem furo. Numa placa real os furos de
    /// alimentacao vem em grupos de 5 com um vao entre eles -- e o que
    /// fecha a conta: 63x10 = 630 no miolo + 4 trilhos de 50 = 830.
    /// </summary>
    public static IEnumerable<int> ColunasDoTrilho(string id)
    {
        int grupos = GruposDeTrilho(id);
        if (grupos == 0) yield break;
        int colunas = ColunasDe(id);

        // Grupo ocupa 5 colunas e pula 1. Centraliza o conjunto na placa.
        int usadas = grupos * 6 - 1;
        int inicio = Math.Max(1, (colunas - usadas) / 2);

        for (int g = 0; g < grupos; g++)
            for (int i = 0; i < 5; i++)
                yield return inicio + g * 6 + i;
    }

    static void CarregarJson()
    {
        var assembly = typeof(Catalogo).Assembly;
        string recurso = assembly.GetManifestResourceNames()
            .FirstOrDefault(r => r.EndsWith(".assets.pecas.json", StringComparison.OrdinalIgnoreCase));
        if (recurso is null) return;

        using var fluxo = assembly.GetManifestResourceStream(recurso);
        using var leitor = new StreamReader(fluxo);

        var doc = JsonDocument.Parse(leitor.ReadToEnd());
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            string id = item.GetProperty("id").GetString();
            var pinos = new List<PinoDef>();

            foreach (var p in item.GetProperty("pinos").EnumerateArray())
            {
                pinos.Add(new PinoDef(
                    p.GetProperty("nome").GetString(),
                    p.GetProperty("x").GetDouble(),
                    p.GetProperty("y").GetDouble(),
                    p.TryGetProperty("sinais", out var sg)
                        ? [.. sg.EnumerateArray().Select(x => x.GetString())]
                        : [],
                    p.TryGetProperty("descricao", out var d) ? d.GetString() : ""));
            }

            porId[id] = new PecaDef(
                id,
                Apresentar(id),
                item.GetProperty("largura").GetDouble(),
                item.GetProperty("altura").GetDouble(),
                pinos,
                Categorizar(id));
        }
    }

    // ---------------- protoboard ----------------
    /// <summary>
    /// Furos em passo exato de 0,1". Antes as linhas estavam calculadas
    /// com meio-passo aqui e ali e saiam desalinhadas; agora tudo vem de
    /// indices inteiros de linha.
    /// </summary>
    static PecaDef MontarProtoboard(string id, string nome)
    {
        var f = formatos[id];
        int colunas = f.Colunas;
        var pinos = new List<PinoDef>();
        double m = Passo;                          // margem

        double X(int c) => m + c * Passo;
        double Y(int linha) => m + linha * Passo;

        // Trilhos: 2 linhas em cima, 2 embaixo. Deixa um furo de folga em
        // cada ponta, como na placa real. A mini de 170 nao tem nenhum.
        foreach (var (linhaBase, prefixo) in f.TemTrilhos
                 ? new[] { (f.TrilhoTopo, "T"), (f.TrilhoBase, "B") }
                 : [])
        {
            for (int i = 0; i < 2; i++)
            {
                string trilho = prefixo + (i == 0 ? "+" : "-");
                foreach (int c in ColunasDoTrilho(id))
                {
                    pinos.Add(new PinoDef($"{trilho}{c}", X(c), Y(linhaBase + i),
                        [i == 0 ? "VCC" : "GND"], $"trilho {trilho}"));
                }
            }
        }

        // Area central: A-E em cima do canal, F-J embaixo.
        const string letras = "ABCDEFGHIJ";
        for (int l = 0; l < 10; l++)
        {
            int linha = l < 5 ? f.A + l : f.F + (l - 5);
            for (int c = 0; c < colunas; c++)
                pinos.Add(new PinoDef($"{letras[l]}{c}", X(c), Y(linha), [], $"{letras[l]}{c}"));
        }

        double larg = m * 2 + (colunas - 1) * Passo;
        double alt = m * 2 + f.Total * Passo;
        return new PecaDef(id, nome, larg, alt, pinos, "Base");
    }

    /// <summary>Linha (em passos) de cada elemento, para o desenho.</summary>
    public static (int trilhoTopo, int a, int f, int trilhoBase, int total) Linhas
        => (LinhaTrilhoTopo, LinhaA, LinhaF, LinhaTrilhoBase, LinhasTotais);

    /// <summary>
    /// Furos ligados entre si: colunas da area central e trilhos inteiros.
    /// E o que faz a protoboard conduzir de verdade no netlist.
    /// </summary>
    public static IEnumerable<(string a, string b)> LigacoesInternas(string idPeca)
    {
        // A juncao e um no so com quatro bordas: e para isso que ela
        // existe, juntar tres fios sem empilhar um por cima do outro.
        if (idPeca == "junction")
        {
            yield return ("1", "2");
            yield return ("1", "3");
            yield return ("1", "4");
            yield break;
        }

        // Os drivers de motor tem varios GND (e o L293D quatro): o mesmo no.
        // No IBT-2 o GND do conector e o B- da bateria sao a mesma trilha.
        if (idPeca == "bts7960") yield return ("GND", "B-");
        // O fio vermelho do conector do motor e o + da placa do ULN2003.
        if (idPeca == "uln2003") yield return ("COM", "+");
        // O GND do cabo de servo do ESC e o - da bateria.
        if (idPeca is "esc" or "esc-4em1") { yield return ("GND", "BAT-"); yield break; }
        // Os modulos chaveados: o - da entrada e o da saida sao a mesma trilha.
        if (idPeca == "lm2596") { yield return ("IN-", "OUT-"); yield break; }
        if (idPeca == "mt3608") { yield return ("VIN-", "VOUT-"); yield break; }
        if (idPeca is "a4988" or "drv8825") { yield return ("GND", "GND.2"); yield break; }
        if (DriverDeMotor.De(idPeca) is not null && Obter(idPeca) is { } driver)
        {
            foreach (var grupo in driver.Pinos.GroupBy(p => p.Nome.Split('.')[0]).Where(g => g.Count() > 1))
            {
                string primeiro = grupo.First().Nome;
                foreach (var outro in grupo.Skip(1)) yield return (primeiro, outro.Nome);
            }
            yield break;
        }

        if (!EhProtoboard(idPeca)) yield break;
        int colunas = ColunasDe(idPeca);
        const string letras = "ABCDEFGHIJ";

        for (int c = 0; c < colunas; c++)
        {
            // A-E formam um no; F-J outro (separados pelo canal).
            for (int l = 1; l < 5; l++) yield return ($"A{c}", $"{letras[l]}{c}");
            for (int l = 6; l < 10; l++) yield return ($"F{c}", $"{letras[l]}{c}");
        }

        // Os vaos sao so do desenho: eletricamente o trilho e um no unico
        // de ponta a ponta.
        var doTrilho = ColunasDoTrilho(idPeca).ToList();
        if (doTrilho.Count == 0) yield break;      // a mini de 170 nao tem trilho
        foreach (string t in new[] { "T+", "T-", "B+", "B-" })
            for (int i = 1; i < doTrilho.Count; i++)
                yield return ($"{t}{doTrilho[0]}", $"{t}{doTrilho[i]}");
    }

    // ---------------- apresentacao ----------------
    static string Apresentar(string id) => id switch
    {
        // Os nomes das placas saem do proprio perfil: e la que eles
        // ja estao escritos, e dois lugares dariam dois nomes.
        _ when PerfilPlaca.De(id) is { } perfil => perfil.Nome,

        "esp32-devkit-v1" => "ESP32 DevKit V1",
        "led" => "LED",
        "resistor" => "Resistor",
        "pushbutton" => "Botao",
        "pushbutton-6mm" => "Botao 6mm",
        "potentiometer" => "Potenciometro",
        "buzzer" => "Buzzer",
        "lcd1602" => "LCD 16x2",
        "lcd2004" => "LCD 20x4",
        "lcd1602-i2c" => "LCD 16x2 I2C",
        "lcd2004-i2c" => "LCD 20x4 I2C",
        "modulo-i2c" => "Modulo I2C (PCF8574)",
        "junction" => "Juncao de fios",
        "vcc-symbol" => "Simbolo VCC",
        "gnd-symbol" => "Simbolo GND",
        "st7789" => "Display 240x240 (ST7789)",
        "not-gate" => "Porta NOT",
        "and-gate" => "Porta AND",
        "or-gate" => "Porta OR",
        "xor-gate" => "Porta XOR",
        "nand-gate" => "Porta NAND",
        "nor-gate" => "Porta NOR",
        "xnor-gate" => "Porta XNOR",
        "mux-2" => "Multiplexador 2x1",
        "flip-flop-d" => "Flip-Flop D",
        "flip-flop-sr" => "Flip-Flop SR",
        "flip-flop-dsr" => "Flip-Flop DSR",
        "clock-generator" => "Gerador de clock",
        "logic-analyzer" => "Analisador logico",
        "hc-sr04" => "Sensor ultrassonico",
        "dht22" => "Sensor DHT22",
        "servo" => "Servo motor",
        "7segment" => "Display 7 segmentos",
        "membrane-keypad" => "Teclado matricial",
        "rgb-led" => "LED RGB",
        "photoresistor-sensor" => "Sensor de luz (LDR)",
        "ldr" => "LDR (fotorresistor)",
        "dht11" => "Sensor DHT11",
        "ntc-temperature-sensor" => "Sensor de temperatura NTC",
        "pir-motion-sensor" => "Sensor de presenca (PIR)",
        "slide-switch" => "Chave deslizante",
        "slide-potentiometer" => "Potenciometro deslizante",
        "tilt-switch" => "Sensor de inclinacao",
        "led-bar-graph" => "Barra de LEDs",
        "neopixel" => "NeoPixel",
        "neopixel-matrix" => "Matriz NeoPixel",
        // O professor perguntou por que o OLED tinha oito conexoes: a
        // peca que existia era a versao SPI. A de I2C, que e a que a
        // turma compra, tem quatro -- GND, VCC, SCL e SDA -- e agora e
        // ela que se chama so "Display OLED".
        "ssd1306" => "Display OLED SPI (8 pinos)",
        "ssd1306-i2c" => "Display OLED 128x64 (I2C)",
        "bateria" => "Bateria",
        "arduino-uno" => "Arduino Uno",
        "arduino-mega" => "Arduino Mega",
        "arduino-nano" => "Arduino Nano",
        "nano-rp2040-connect" => "Nano RP2040 Connect",
        "franzininho" => "Franzininho",
        "pi-pico" => "Raspberry Pi Pico",
        "stm32-bluepill" => "STM32F103 (Blue Pill)",
        "stepper-motor" => "Motor de passo",
        "analog-joystick" => "Joystick analogico",
        "dip-switch-8" => "Chave DIP 8",
        "ir-receiver" => "Receptor infravermelho",
        "ir-remote" => "Controle infravermelho",
        "microsd-card" => "Cartao microSD",
        "wifi-ap" => "Ponto de acesso WiFi",
        "texto" => "Texto",
        "nrf24l01" => "Modulo de radio NRF24L01",
        "flame-sensor" => "Sensor de chama",
        "gas-sensor" => "Sensor de gas",
        "heart-beat-sensor" => "Sensor de batimento",
        "big-sound-sensor" => "Sensor de som (grande)",
        "small-sound-sensor" => "Sensor de som",
        "ky-040" => "Encoder rotativo KY-040",
        "mpu6050" => "Acelerometro MPU6050",
        "ds1307" => "Relogio DS1307",
        "hx711" => "Celula de carga HX711",
        "ili9341" => "Display TFT ILI9341",
        "biaxial-stepper" => "Motor de passo biaxial",
        "rotary-dialer" => "Disco de telefone",
        "led-ring" => "Anel de LEDs",
        "ks2e-m-dc5" => "Rele KS2E",
        "alto-falante" => "Alto-falante",
        "max98357a" => "Amplificador I2S MAX98357A",
        "pendrive-usb" => "Pendrive USB",
        "motor-dc" => "Motor DC com escova (130, RS-540...)",
        "motor-tt" => "Motor TT amarelo (1:48)",
        "motor-brushless" => "Motor brushless (A2212, 2205...)",
        "l298n" => "Ponte H L298N",
        "l293d" => "Ponte H L293D (CI)",
        "l9110s" => "Ponte H L9110S",
        "tb6612fng" => "Driver de motor TB6612FNG",
        "drv8833" => "Driver de motor DRV8833",
        "bts7960" => "Driver de motor BTS7960 (43 A)",
        "a4988" => "Driver de passo A4988",
        "drv8825" => "Driver de passo DRV8825",
        "uln2003" => "Driver ULN2003 (28BYJ-48)",
        "motor-28byj48" => "Motor de passo 28BYJ-48",
        "micro-switch" => "Chave fim de curso (micro switch)",
        "servo-mg996r" => "Servo MG996R",
        "servo-360" => "Servo 360 (rotacao continua)",
        "esc" => "ESC (brushless, 12 a 60 A)",
        "esc-4em1" => "ESC 4-em-1 (drone)",
        "receptor-rc" => "Receptor de radio FlySky FS-iA6B",
        "radio-controle" => "Controle de radio FlySky FS-i6",
        "roda-tt" => "Roda 65 mm (motor TT)",
        "helice-130" => "Helice de 3 pas (motor 130)",
        "helice-1045" => "Helice 1045 (brushless)",
        "helice-5045" => "Helice 5045, 3 pas (drone de 5\")",
        "regulador-7805" => "Regulador de tensão 7805 (78xx)",
        "diodo" => "Diodo (1N4007, 1N4148...)",
        "capacitor" => "Capacitor cerâmico",
        "capacitor-eletrolitico" => "Capacitor eletrolítico",
        "indutor" => "Indutor",
        "cristal" => "Cristal (16 MHz...)",
        "diodo-zener" => "Diodo Zener",
        "diodo-schottky" => "Diodo Schottky",
        "ams1117" => "Regulador AMS1117 (3,3 V)",
        "lm2596" => "Regulador step-down LM2596",
        "mt3608" => "Regulador step-up MT3608",
        _ => id.Replace('-', ' '),
    };

    static string Categorizar(string id) => id switch
    {
        // Toda placa de desenvolvimento e "Placas" -- a lista vem do
        // PerfilPlaca, que e quem de fato sabe o que e placa. Escrever
        // os ids aqui de novo daria a lista que se esquece de
        // atualizar, e com vinte e sete placas novas isso aconteceria
        // na primeira semana.
        _ when PerfilPlaca.EhPlaca(id) => "Placas",

        "led" or "rgb-led" or "led-bar-graph" or "led-ring" or "neopixel"
            or "neopixel-matrix" or "7segment" or "lcd1602" or "lcd2004"
            or "lcd1602-i2c" or "lcd2004-i2c"
            or "ssd1306" or "ssd1306-i2c" or "ili9341" or "st7789" => "Saida",

        "pushbutton" or "pushbutton-6mm" or "potentiometer" or "slide-potentiometer"
            or "slide-switch" or "dip-switch-8" or "membrane-keypad"
            or "analog-joystick" or "ky-040" or "rotary-dialer"
            or "tilt-switch" or "micro-switch" => "Entrada",

        "hc-sr04" or "dht22" or "photoresistor-sensor" or "ntc-temperature-sensor"
            or "pir-motion-sensor" or "flame-sensor" or "gas-sensor"
            or "heart-beat-sensor" or "big-sound-sensor" or "small-sound-sensor"
            or "mpu6050" or "hx711" or "dht11" => "Sensores",

        "buzzer" or "ks2e-m-dc5" or "alto-falante" => "Atuadores",

        // A Alpha 23 trouxe motor, ponte H, driver, ESC, roda e helice: juntos
        // numa categoria so, com o servo e os de passo que ja existiam.
        "servo" or "servo-mg996r" or "servo-360" or "stepper-motor" or "biaxial-stepper"
            or "motor-dc" or "motor-tt" or "motor-brushless"
            or "l298n" or "l293d" or "l9110s" or "tb6612fng" or "drv8833" or "bts7960"
            or "a4988" or "drv8825" or "uln2003" or "motor-28byj48" or "esc" or "esc-4em1"
            or "receptor-rc" or "radio-controle"
            or "roda-tt" or "helice-130" or "helice-1045" or "helice-5045" => "Motores",

        "resistor" or "junction" or "ldr"
            or "capacitor" or "capacitor-eletrolitico" or "indutor" or "cristal" => "Passivos",
        "diodo" or "diodo-zener" or "diodo-schottky" => "Semicondutores",
        "vcc-symbol" or "gnd-symbol" or "bateria"
            or "regulador-7805" or "ams1117" or "lm2596" or "mt3608" => "Alimentacao",

        "not-gate" or "and-gate" or "or-gate" or "xor-gate" or "nand-gate"
            or "nor-gate" or "xnor-gate" or "mux-2" or "flip-flop-d"
            or "flip-flop-sr" or "flip-flop-dsr" or "clock-generator"
            or "logic-analyzer" => "Logica",
        "modulo-i2c" or "nrf24l01" or "max98357a" => "Modulos",
        "wifi-ap" => "Rede",
        "texto" => "Anotacao",
        IdProtoboard or IdProtoboard830 or IdProtoboard170 => "Base",
        _ => "Outros",
    };
}
