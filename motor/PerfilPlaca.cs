namespace Simulador;

/// <summary>
/// O que muda de uma placa para outra: como o nome do pino vira numero,
/// quantos pinos existem, a faixa do conversor analogico e quais avisos
/// fazem sentido.
///
/// Sem isto o simulador so servia para ESP32: o netlist procurava
/// literalmente o tipo "esp32-devkit-v1" e o padrao de nome "D&lt;n&gt;",
/// entao um Arduino Uno aparecia na tela mas nao acendia nada.
/// </summary>
public partial record PerfilPlaca(
    string Id,
    string Nome,
    int NumPinos,
    int MaxAdc,
    int LedInterno)
{
    /// <summary>Nome do pino -> numero, ou -1 se nao for pino de dados.</summary>
    public Func<string, int> Numero { get; init; } = _ => -1;

    /// <summary>Avisos de GPIO especificos do ESP32 (34-39, 6-11).</summary>
    public bool AvisosDoEsp32 { get; init; }

    /// <summary>A placa tem WiFi: ESP32 e Nano RP2040 Connect.</summary>
    public bool TemWifi { get; init; }

    /// <summary>Bluetooth classico, o do BluetoothSerial. So a ESP32 tem.</summary>
    public bool TemBluetoothClassico { get; init; }

    /// <summary>
    /// Pinos do I2C quando o sketch chama "Wire.begin()" sem dizer quais.
    ///
    /// No Uno sao A4 e A5, que na numeracao continuada viram 18 e 19; na
    /// ESP32 sao o 21 e o 22. Importa para o desenho: e por esses pinos
    /// que o modulo do LCD tem que estar ligado.
    /// </summary>
    public int SdaPadrao { get; init; } = -1;
    public int SclPadrao { get; init; } = -1;

    /// <summary>
    /// Tensao dos pinos de dados: 3,3 V na ESP32, 5 V no Arduino.
    ///
    /// Decide o que queima e o que so incomoda. Um LED sem resistor num
    /// pino de 3,3 V passa perto do limite e sobrevive; no mesmo pino de
    /// uma placa de 5 V, nao.
    /// </summary>
    public double TensaoDeOperacao { get; init; } = 3.3;

    /// <summary>Quanto um pino entrega antes de estragar, em mA.</summary>
    public double CorrenteDoPino { get; init; } = 40;

    /// <summary>
    /// Os bits que o analogRead() do core devolve sem ninguem pedir.
    ///
    /// NAO e o tamanho do conversor: a Pico e a STM32 tem ADC de 12 bits,
    /// mas o core delas para o Arduino entrega 10 (0 a 1023) por
    /// compatibilidade -- 12 so com analogReadResolution(12). A ESP32
    /// entrega 12 de fabrica. Antes o simulador devolvia sempre o tamanho
    /// do conversor, e o sketch acertado aqui lia um quarto na bancada.
    /// </summary>
    public int BitsDoAnalogRead { get; init; } = 10;

    /// <summary>Os bits do conversor de verdade: 12 com MaxAdc 4095, 10 com 1023.</summary>
    public int BitsDoConversor => (int)Math.Round(Math.Log2(MaxAdc + 1.0));

    /// <summary>
    /// O core tem analogReadResolution()? O do AVR e o do ESP8266 nao tem:
    /// la a chamada nem compila.
    /// </summary>
    public bool MudaResolucaoDoAdc => !De8Bits && Processador != "ESP8266";

    // ---------------- o que a placa tem de verdade ----------------
    //
    // Alpha 18: "limitadores de placas, para RAM de cada placa,
    // processador, cores, e etc. para ter uma experiencia mais
    // acurada". Sao os numeros reais das folhas de dados: um Uno tem
    // 2 KB de RAM, e um vetor de 600 int simplesmente NAO CABE nele.
    // Sem esses limites o simulador aceitava tudo, e o aluno so
    // descobria o problema na placa de verdade -- reiniciando sozinha,
    // sem mensagem nenhuma.

    /// <summary>O nome do chip: "ATmega328P", "RP2040".</summary>
    public string Processador { get; init; } = "?";

    /// <summary>Quantos nucleos o processador tem.</summary>
    public int Nucleos { get; init; } = 1;

    /// <summary>A frequencia de fabrica, em MHz.</summary>
    public double Megahertz { get; init; } = 16;

    /// <summary>RAM para as variaveis do sketch, em bytes.</summary>
    public int RamBytes { get; init; } = 2048;

    /// <summary>Flash disponivel para o programa, ja descontado o bootloader.</summary>
    public int FlashBytes { get; init; } = 32256;

    /// <summary>
    /// A PSRAM da placa, em bytes (0 = nao tem). E a memoria de fora do
    /// chip que o DOOM usa para os graficos: o ESP32-S3 N8R8 tem 8 MB.
    /// </summary>
    public long PsramBytes { get; init; }

    /// <summary>O tamanho do chip de flash inteiro, onde moram as particoes.</summary>
    public long FlashTotalBytes { get; init; } = 4L * 1024 * 1024;

    /// <summary>
    /// Processador de 8 bits, como os ATmega e o ATtiny.
    ///
    /// Muda o tamanho dos tipos: "int" ocupa 2 bytes no AVR e 4 nas
    /// placas de 32 bits. Num Uno e a diferenca entre caber e nao caber.
    /// </summary>
    public bool De8Bits { get; init; }

    /// <summary>
    /// O que o core gasta de RAM antes de o sketch existir.
    ///
    /// Varia MUITO de placa para placa, e por isso e um numero por
    /// placa e nao uma regra por arquitetura: num AVR sao os buffers do
    /// Serial e pouco mais, e numa ESP32 o proprio sistema ja come
    /// dezenas de KB antes da primeira linha do setup.
    /// </summary>
    public int BaseDaRam { get; init; } = 200;

    /// <summary>O tamanho do programa compilado sem sketch nenhum.</summary>
    public int BaseDaFlash { get; init; } = 450;

    /// <summary>
    /// Quanto um digitalWrite custa no chip, em microssegundos: uns 58
    /// ciclos do processador. No Uno sao 3,6 us -- e e por isso que um
    /// shiftOut leva perto de cem microssegundos, e nao zero.
    /// </summary>
    public double CustoDoPinoUs => 58.0 / Megahertz;

    /// <summary>
    /// Quanto a placa leva do reset ate o setup(), em ms: o bootloader da
    /// ROM e a partida do sistema na familia ESP32 (~300 ms), bem menos no
    /// ESP8266 e no RP2040, e no AVR o atraso de partida do cristal que o
    /// fusivel do Uno pede (65 ms) -- o Optiboot so espera um upload quando o
    /// reset vem do botao. Numa placa presa num ciclo de reinicios, e isto que
    /// separa um reinicio do outro.
    /// </summary>
    public int MsDoBoot => Processador switch
    {
        "Xtensa LX6" or "Xtensa LX7" or "RISC-V" => 300,
        "ESP8266" or "RP2040" => 100,
        _ when De8Bits => 65,
        _ => 10,
    };

    /// <summary>
    /// A frequencia do analogWrite neste pino, a do core de cada placa. No
    /// ATmega328P os pinos 5 e 6 sao do Timer0 (976,56 Hz) e os outros do
    /// Timer1 e do Timer2 (490,2 Hz); ESP32, ESP8266, RP2040 e STM32
    /// usam 1 kHz de fabrica.
    /// </summary>
    public double FrequenciaDoPwm(int pino) => Processador switch
    {
        "ATmega328P" => pino is 5 or 6 ? 976.5625 : 490.196,
        "ATmega2560" => pino is 4 or 13 ? 976.5625 : 490.196,
        _ when De8Bits => 490.196,
        _ => 1000,
    };

    /// <summary>
    /// O GPIO por onde o Serial sai, quando sai por um pino. -1 nas placas
    /// em que o Serial e a USB do proprio chip (RP2040, por exemplo): la
    /// ele nao passa por pino nenhum, e o analisador nao tem o que ver.
    /// </summary>
    public int PinoTx { get; init; } = -1;

    // ---------------- os nomes do pins_arduino.h ----------------
    //
    // "analogRead(A0)" parava em "variavel 'A0' nao foi declarada" em
    // quase toda placa que nao fosse Arduino -- e o mesmo com o SDA e o
    // SCL, o D5 da NodeMCU e o PC13 da Blue Pill. Sao nomes que o core de
    // cada placa define no proprio pins_arduino.h, e que todo tutorial usa.
    //
    // A tabela de cada placa e a do core oficial, copiada do arquivo -- e
    // SO ela. O que o core nao define continua sendo erro aqui, porque la
    // tambem nao compila: D5 existe na NodeMCU e nao existe na ESP32.

    /// <summary>Os nomes da tabela do core: "SDA" vira 21 na ESP32.</summary>
    public IReadOnlyDictionary<string, int> NomesDoCore { get; init; } = new Dictionary<string, int>();

    /// <summary>
    /// Nomes que seguem uma regra em vez de uma tabela: "PC13" na Blue
    /// Pill, "PB3" no ATtiny85, "D13" nas Nucleo. -1 quando nao e deles.
    /// </summary>
    public Func<string, int> NomePorRegra { get; init; } = _ => -1;

    /// <summary>
    /// O GPIO de um nome do core da placa, ou -1 se o core nao o define.
    ///
    /// Os A0..A7 dos Arduino continuam vindo do <see cref="Numero"/>, que
    /// ja contava depois dos digitais: A0 e o 14 no Uno e o 54 no Mega.
    /// </summary>
    public int PinoDoCore(string nome)
    {
        if (string.IsNullOrEmpty(nome)) return -1;
        if (NomesDoCore.TryGetValue(nome, out int doCore)) return doCore;

        int porRegra = NomePorRegra(nome);
        if (porRegra >= 0) return porRegra;

        return EhNomeAnalogico(nome) ? Numero(nome) : -1;
    }

    /// <summary>"A0" a "A19".</summary>
    public static bool EhNomeAnalogico(string nome)
        => nome is { Length: 2 or 3 } && nome[0] == 'A' && nome[1..].All(char.IsAsciiDigit);

    /// <summary>Junta pares e sequencias numa tabela de nomes do core.</summary>
    static Dictionary<string, int> Core(params IEnumerable<(string Nome, int Gpio)>[] partes)
    {
        var tabela = new Dictionary<string, int>();
        foreach (var parte in partes)
            foreach (var (nome, gpio) in parte) tabela[nome] = gpio;
        return tabela;
    }

    /// <summary>"A0".."A19" = 1..20: do primeiro ao ultimo nome, a partir do GPIO dado.</summary>
    static IEnumerable<(string, int)> Seguidos(string prefixo, int de, int ate, int primeiroGpio)
        => Enumerable.Range(de, ate - de + 1).Select(i => ($"{prefixo}{i}", primeiroGpio + i - de));

    static (string, int)[] Pares(params (string, int)[] pares) => pares;

    /// <summary>SPI e I2C do cabecalho do Uno: os mesmos no Uno e no Nano.</summary>
    static (string, int)[] BarramentosDoUno()
        => Pares(("SDA", 18), ("SCL", 19), ("SS", 10), ("MOSI", 11), ("MISO", 12), ("SCK", 13));

    /// <summary>A ficha da placa numa linha, para o console.</summary>
    public string Ficha => $"{Processador} · {Nucleos} núcleo{(Nucleos == 1 ? "" : "s")}"
                           + $" · {Megahertz:0.#} MHz · {RamBytes / 1024.0:0.#} KB de RAM";

    // ---------------- ajudantes de nome ----------------
    /// <summary>Tira o sufixo de pad repetido: "13.2" e o mesmo que "13".</summary>
    static string Base(string nome)
    {
        int ponto = nome.IndexOf('.');
        return ponto < 0 ? nome : nome[..ponto];
    }

    static int SoDigitos(string nome)
        => int.TryParse(Base(nome), out int n) ? n : -1;

    static int ComPrefixo(string nome, string prefixo)
    {
        string b = Base(nome);
        return b.StartsWith(prefixo) && int.TryParse(b[prefixo.Length..], out int n) ? n : -1;
    }

    /// <summary>A0 vira um numero depois dos digitais, como no Arduino.</summary>
    static int Analogico(string nome, int primeiro)
    {
        int k = ComPrefixo(nome, "A");
        return k < 0 ? -1 : primeiro + k;
    }

    // ---------------- perfis ----------------
    public static readonly PerfilPlaca Esp32 = new(
        "esp32-devkit-v1", "ESP32 DevKit V1", NumPinos: 40, MaxAdc: 4095, LedInterno: 2)
    {
        AvisosDoEsp32 = true,
        TemWifi = true,
        TemBluetoothClassico = true,
        SdaPadrao = 21, SclPadrao = 22,
        BitsDoAnalogRead = 12,

        // ESP32-WROOM-32. Os 520 KB de SRAM do chip nao sao todos do
        // sketch: o core fica com boa parte, e o que a IDE anuncia como
        // memoria dinamica sao 327.680 bytes.
        Processador = "Xtensa LX6", Nucleos = 2, Megahertz = 240,
        RamBytes = 327680, FlashBytes = 1310720,
        BaseDaRam = 20000, BaseDaFlash = 200000, PinoTx = 1,

        // variants/doitESP32devkitV1/pins_arduino.h. Nao ha A1 nem A2: os
        // GPIO 37 e 38 nao saem do modulo, e o core nao os define.
        NomesDoCore = Core(Pares(
            ("TX", 1), ("RX", 3), ("SDA", 21), ("SCL", 22),
            ("SS", 5), ("MOSI", 23), ("MISO", 19), ("SCK", 18),
            ("A0", 36), ("A3", 39), ("A4", 32), ("A5", 33), ("A6", 34), ("A7", 35),
            ("A10", 4), ("A11", 0), ("A12", 2), ("A13", 15), ("A14", 13),
            ("A15", 12), ("A16", 14), ("A17", 27), ("A18", 25), ("A19", 26),
            ("T0", 4), ("T1", 0), ("T2", 2), ("T3", 15), ("T4", 13),
            ("T5", 12), ("T6", 14), ("T7", 27), ("T8", 33), ("T9", 32),
            ("DAC1", 25), ("DAC2", 26))),

        Numero = nome => nome switch
        {
            "VP" => 36,
            "VN" => 39,
            "RX2" => 16,
            "TX2" => 17,
            "RX0" => 3,
            "TX0" => 1,
            _ => ComPrefixo(nome, "D"),
        }
    };

    public static readonly PerfilPlaca Uno = new(
        "arduino-uno", "Arduino Uno", NumPinos: 20, MaxAdc: 1023, LedInterno: 13)
    {
        TensaoDeOperacao = 5, CorrenteDoPino = 40,

        // ATmega328P: 32 KB de flash menos o meio KB do bootloader.
        Processador = "ATmega328P", Nucleos = 1, Megahertz = 16,
        RamBytes = 2048, FlashBytes = 32256, De8Bits = true,
        BaseDaRam = 200, BaseDaFlash = 450, PinoTx = 1,
        // No Arduino os analogicos continuam a numeracao: A0 e o pino 14.
        SdaPadrao = 18, SclPadrao = 19,   // A4 e A5
        NomesDoCore = Core(BarramentosDoUno()),
        Numero = nome => SoDigitos(nome) is var d && d >= 0 ? d : Analogico(nome, 14)
    };

    public static readonly PerfilPlaca Mega = new(
        "arduino-mega", "Arduino Mega", NumPinos: 70, MaxAdc: 1023, LedInterno: 13)
    {
        TensaoDeOperacao = 5, CorrenteDoPino = 40,

        Processador = "ATmega2560", Nucleos = 1, Megahertz = 16,
        RamBytes = 8192, FlashBytes = 253952, De8Bits = true,
        BaseDaRam = 200, BaseDaFlash = 650, PinoTx = 1,
        SdaPadrao = 20, SclPadrao = 21,
        NomesDoCore = Core(Pares(("SDA", 20), ("SCL", 21),
                                 ("SS", 53), ("MOSI", 51), ("MISO", 50), ("SCK", 52))),
        Numero = nome => SoDigitos(nome) is var d && d >= 0 ? d : Analogico(nome, 54)
    };

    public static readonly PerfilPlaca Nano = new(
        "arduino-nano", "Arduino Nano", NumPinos: 22, MaxAdc: 1023, LedInterno: 13)
    {
        TensaoDeOperacao = 5, CorrenteDoPino = 40,

        // O mesmo chip do Uno, com um bootloader maior: sobra menos
        // flash para o sketch.
        Processador = "ATmega328P", Nucleos = 1, Megahertz = 16,
        RamBytes = 2048, FlashBytes = 30720, De8Bits = true,
        BaseDaRam = 200, BaseDaFlash = 450, PinoTx = 1,
        SdaPadrao = 18, SclPadrao = 19,   // A4 e A5
        NomesDoCore = Core(BarramentosDoUno()),
        Numero = nome => SoDigitos(nome) is var d && d >= 0 ? d : Analogico(nome, 14)
    };

    public static readonly PerfilPlaca NanoRp2040 = new(
        "nano-rp2040-connect", "Nano RP2040 Connect", NumPinos: 22, MaxAdc: 1023, LedInterno: 13)
    {
        TensaoDeOperacao = 3.3, CorrenteDoPino = 12,
        TemWifi = true,

        Processador = "RP2040", Nucleos = 2, Megahertz = 133,
        RamBytes = 270336, FlashBytes = 16777216,   // 264 KB de SRAM, como o core da Arduino conta
        BaseDaRam = 8000, BaseDaFlash = 50000,
        SdaPadrao = 18, SclPadrao = 19,   // A4 e A5
        NomesDoCore = Core(BarramentosDoUno()),
        NomePorRegra = nome => ComPrefixo(nome, "D"),
        Numero = nome => ComPrefixo(nome, "D") is var d && d >= 0 ? d : Analogico(nome, 14)
    };

    public static readonly PerfilPlaca Franzininho = new(
        "franzininho", "Franzininho (ATtiny85)", NumPinos: 6, MaxAdc: 1023, LedInterno: 1)
    {
        TensaoDeOperacao = 5, CorrenteDoPino = 40,

        // Meio KB de RAM. E a placa que denuncia sozinha o String: um
        // Serial.println de String ja come um quinto dela.
        Processador = "ATtiny85", Nucleos = 1, Megahertz = 16.5,
        RamBytes = 512, FlashBytes = 6586, De8Bits = true,
        BaseDaRam = 15, BaseDaFlash = 300,
        NomesDoCore = Core(AnalogicosDoAttiny()),
        NomePorRegra = nome => ComPrefixo(nome, "PB"),
        Numero = nome => ComPrefixo(nome, "PB")
    };

    /// <summary>
    /// A0..A3 do ATtiny85 sao os canais do conversor, e cada um mora num
    /// pino fora de ordem: A0 no PB5, A1 no PB2, A2 no PB4 e A3 no PB3.
    /// </summary>
    static (string, int)[] AnalogicosDoAttiny()
        => Pares(("A0", 5), ("A1", 2), ("A2", 4), ("A3", 3));

    /// <summary>
    /// Raspberry Pi Pico.
    ///
    /// Os pinos se chamam GP0..GP28 na serigrafia, e e assim que o
    /// sketch os escreve -- tanto em C++ ("digitalWrite(15, HIGH)")
    /// quanto em MicroPython ("Pin(15, Pin.OUT)"), porque o numero do
    /// GP E o numero do pino. Essa coincidencia e o que torna a Pico a
    /// placa mais facil de acrescentar: nao ha tabela de traducao.
    ///
    /// O LED interno e o GP25.
    /// </summary>
    public static readonly PerfilPlaca PiPico = new(
        "pi-pico", "Raspberry Pi Pico", NumPinos: 30, MaxAdc: 4095, LedInterno: 25)
    {
        TensaoDeOperacao = 3.3,

        // O RP2040 entrega bem menos por pino do que um AVR de 5 V: o
        // limite de projeto e 12 mA, e passar disso estraga a saida.
        CorrenteDoPino = 12,

        Processador = "RP2040", Nucleos = 2, Megahertz = 133,
        RamBytes = 270336, FlashBytes = 2093056,    // 264 KB de SRAM (264 x 1024)
        BaseDaRam = 8000, BaseDaFlash = 50000,
        SdaPadrao = 4, SclPadrao = 5,
        NomesDoCore = NomesDaPico(),
        Numero = nome => ComPrefixo(nome, "GP")
    };

    /// <summary>
    /// O core da Pico para Arduino (arduino-pico): D0..D29 sao os proprios
    /// GP, A0..A3 sao o GP26..GP29, e os barramentos sao os de fabrica.
    /// </summary>
    static Dictionary<string, int> NomesDaPico()
        => Core(Seguidos("D", 0, 29, 0), Seguidos("A", 0, 3, 26),
                Pares(("SDA", 4), ("SCL", 5), ("SS", 17), ("MOSI", 19), ("MISO", 16), ("SCK", 18)));

    /// <summary>
    /// STM32F103C8, a "Blue Pill".
    ///
    /// Aqui o nome do pino NAO e um numero: e porta e bit, "PA5" e
    /// "PC13". O core do Arduino para STM32 numera as portas em
    /// sequencia -- PA0..PA15 viram 0..15, PB0..PB15 viram 16..31, PC0
    /// em diante vira 32 -- e e essa numeracao que o digitalWrite usa
    /// quando o sketch escreve um numero cru.
    ///
    /// O LED interno fica no PC13, que nessa conta e o 45. Ele acende
    /// com LOW na placa de verdade, e essa inversao aparece no sketch
    /// de todo mundo que usa a Blue Pill pela primeira vez.
    /// </summary>
    public static readonly PerfilPlaca Stm32BluePill = new(
        "stm32-bluepill", "STM32F103 (Blue Pill)", NumPinos: 48, MaxAdc: 4095, LedInterno: 45)
    {
        TensaoDeOperacao = 3.3,
        CorrenteDoPino = 20,

        Processador = "STM32F103C8", Nucleos = 1, Megahertz = 72,
        RamBytes = 20480, FlashBytes = 65536,
        BaseDaRam = 3000, BaseDaFlash = 11000,

        // I2C1: PB7 e o SDA, PB6 e o SCL.
        SdaPadrao = 23, SclPadrao = 22,

        // PA0..PC15 pelo nome, como o core escreve; A0..A7 sao o PA0..PA7,
        // e A8 e A9 o PB0 e o PB1.
        NomesDoCore = Core(Seguidos("A", 0, 7, 0), Pares(("A8", 16), ("A9", 17),
            ("SDA", 23), ("SCL", 22), ("SS", 4), ("MOSI", 7), ("MISO", 6), ("SCK", 5))),
        NomePorRegra = NumeroDaBluePill,
        Numero = NumeroDaBluePill
    };

    /// <summary>
    /// "PA5" -> 5, "PB0" -> 16, "PC13" -> 45.
    ///
    /// E a numeracao continuada que o core STM32 do Arduino usa: cada
    /// porta ocupa 16 numeros, na ordem A, B, C.
    /// </summary>
    static int NumeroDaBluePill(string nome)
    {
        string b = Base(nome);
        if (b.Length < 3 || b[0] != 'P') return -1;

        int porta = b[1] switch { 'A' => 0, 'B' => 16, 'C' => 32, _ => -1 };
        if (porta < 0) return -1;

        return int.TryParse(b[2..], out int bit) && bit is >= 0 and <= 15 ? porta + bit : -1;
    }

    static PerfilPlaca[] todos;

    /// <summary>
    /// Todas as placas, montada na primeira vez que alguem pergunta.
    ///
    /// Preguicosa de proposito: metade dos perfis mora no outro arquivo
    /// da classe, e num campo estatico normal a ordem de inicializacao
    /// entre os dois arquivos e escolhida pelo compilador -- entao este
    /// vetor saia com metade nula, dependendo de qual arquivo fosse
    /// primeiro. O sintoma era o pior possivel: TODO o catalogo
    /// explodia com "type initializer threw an exception".
    /// </summary>
    static PerfilPlaca[] Todos => todos ??=
        [Esp32, Uno, Mega, Nano, NanoRp2040, Franzininho, PiPico, Stm32BluePill,
         .. DaAlpha18];

    public static PerfilPlaca De(string idDaPeca)
        => Todos.FirstOrDefault(p => p.Id == idDaPeca);

    public static bool EhPlaca(string idDaPeca) => De(idDaPeca) is not null;

    public static IReadOnlyList<PerfilPlaca> Conhecidos => Todos;

    /// <summary>Maior numero de pino entre todas as placas: dimensiona os vetores.</summary>
    public static int MaiorNumPinos => Todos.Max(p => p.NumPinos);
}
