namespace Simulador;

/// <summary>
/// As placas novas da Alpha 18.
///
/// Ficam num arquivo à parte porque são muitas -- vinte e sete --, e
/// misturá-las com as oito originais faria o <see cref="PerfilPlaca"/>
/// virar uma lista em que não se acha nada.
///
/// O que cada perfil precisa acertar é uma coisa só: como o NOME que
/// está na serigrafia vira o número do GPIO. É por esse nome que o aluno
/// liga o fio e é esse número que o sketch escreve -- errar aqui faz o
/// LED ficar apagado sem nenhuma mensagem de erro, que é o pior tipo de
/// defeito que um simulador pode ter.
/// </summary>
public partial record PerfilPlaca
{
    // =================================================================
    //  ESP32: as variantes novas
    // =================================================================
    //
    // Nestas a serigrafia traz o NÚMERO puro do GPIO -- "4", "15" --, e
    // não o "D4" da DevKit V1. É a própria Espressif que mudou: nas S2,
    // S3, C3, C6 e H2 não existe mais a numeração de placa.
    //
    // Nenhuma delas tem Bluetooth clássico: da ESP32 original para cá só
    // sobrou o BLE, e por isso o BluetoothSerial.h não compila nelas. O
    // simulador diz isso em vez de fingir que funciona.
    static int NumeroCru(string nome) => SoDigitos(nome);

    /// <summary>
    /// As DevKits com o LED RGB enderecavel soldado (o RGB_BUILTIN do core,
    /// no mesmo pino do LED_BUILTIN): S2 no 18, S3 no 48, C3/C6/H2 no 8.
    /// </summary>
    public static bool TemLedRgb(string id)
        => id is "esp32-s2-devkit" or "esp32-s3-devkit" or "esp32-c3-devkit" or "esp32-c6-devkit" or "esp32-h2-devkit";

    /// <summary>
    /// O numero cru, mais o TX e o RX da serigrafia -- que nestas placas sao
    /// os pinos da UART0, a que vai para o conversor USB. Cada variante os
    /// pos num GPIO diferente.
    /// </summary>
    static Func<string, int> NumeroComSerial(int tx, int rx)
        => nome => Base(nome) switch { "TX" => tx, "RX" => rx, _ => SoDigitos(nome) };

    // Os nomes de cada uma vem do variants/<chip>/pins_arduino.h do core
    // oficial da Espressif.

    public static readonly PerfilPlaca Esp32S2 = new(
        "esp32-s2-devkit", "ESP32-S2 DevKit", NumPinos: 47, MaxAdc: 4095, LedInterno: 18)
    {
        // O GPIO 46 (na barra desde a Alpha 25) e so de entrada no S2.
        TemWifi = true, BitsDoAnalogRead = 12, PinoSoDeEntrada = p => p == 46,
        SdaPadrao = 8, SclPadrao = 9,
        Processador = "Xtensa LX7", Nucleos = 1, Megahertz = 240,
        RamBytes = 327680, FlashBytes = 1310720,
        BaseDaRam = 20000, BaseDaFlash = 200000, PinoTx = 43,
        NomesDoCore = Core(Seguidos("A", 0, 19, 1), Seguidos("T", 1, 14, 1), Pares(
            ("TX", 43), ("RX", 44), ("SDA", 8), ("SCL", 9),
            ("SS", 34), ("MOSI", 35), ("MISO", 37), ("SCK", 36), ("DAC1", 17), ("DAC2", 18))),
        Numero = NumeroComSerial(43, 44),
    };

    public static readonly PerfilPlaca Esp32S3 = new(
        "esp32-s3-devkit", "ESP32-S3 DevKit", NumPinos: 49, MaxAdc: 4095, LedInterno: 48)
    {
        TemWifi = true, BitsDoAnalogRead = 12,
        SdaPadrao = 8, SclPadrao = 9,
        Processador = "Xtensa LX7", Nucleos = 2, Megahertz = 240,
        RamBytes = 327680, FlashBytes = 1310720,
        PsramBytes = 8L * 1024 * 1024, FlashTotalBytes = 8L * 1024 * 1024,
        BaseDaRam = 20000, BaseDaFlash = 200000, PinoTx = 43,
        NomesDoCore = Core(Seguidos("A", 0, 19, 1), Seguidos("T", 1, 14, 1), Pares(
            ("TX", 43), ("RX", 44), ("SDA", 8), ("SCL", 9),
            ("SS", 10), ("MOSI", 11), ("MISO", 13), ("SCK", 12))),
        Numero = NumeroComSerial(43, 44),
    };

    public static readonly PerfilPlaca Esp32C3 = new(
        "esp32-c3-devkit", "ESP32-C3 DevKit", NumPinos: 22, MaxAdc: 4095, LedInterno: 8)
    {
        TemWifi = true, BitsDoAnalogRead = 12,
        SdaPadrao = 8, SclPadrao = 9,
        Processador = "RISC-V", Nucleos = 1, Megahertz = 160,
        RamBytes = 327680, FlashBytes = 1310720,
        BaseDaRam = 20000, BaseDaFlash = 200000, PinoTx = 21,
        NomesDoCore = Core(Seguidos("A", 0, 5, 0), Pares(
            ("TX", 21), ("RX", 20), ("SDA", 8), ("SCL", 9),
            ("SS", 7), ("MOSI", 6), ("MISO", 5), ("SCK", 4))),
        Numero = NumeroComSerial(21, 20),
    };

    /// <summary>
    /// ESP32-C6. O I2C de fabrica e o 23 (SDA) e o 22 (SCL) -- ate a
    /// Alpha 19 estava 6 e 7 aqui, e o display ligado como no simulador
    /// ficava mudo na placa.
    /// </summary>
    public static readonly PerfilPlaca Esp32C6 = new(
        "esp32-c6-devkit", "ESP32-C6 DevKit", NumPinos: 31, MaxAdc: 4095, LedInterno: 8)
    {
        TemWifi = true, BitsDoAnalogRead = 12,
        SdaPadrao = 23, SclPadrao = 22,
        Processador = "RISC-V", Nucleos = 1, Megahertz = 160,
        RamBytes = 327680, FlashBytes = 1310720,
        BaseDaRam = 20000, BaseDaFlash = 200000, PinoTx = 16,
        NomesDoCore = Core(Seguidos("A", 0, 6, 0), Pares(
            ("TX", 16), ("RX", 17), ("SDA", 23), ("SCL", 22),
            ("SS", 18), ("MOSI", 19), ("MISO", 20), ("SCK", 21))),
        Numero = NumeroComSerial(16, 17),
    };

    /// <summary>
    /// ESP32-H2: a variante SEM WiFi.
    ///
    /// Ela é de Thread e Zigbee, e é o caso em que o simulador precisa
    /// dizer não: um sketch com WiFi.begin() nela não roda na placa de
    /// verdade, e passar aqui só adiaria a descoberta.
    /// </summary>
    public static readonly PerfilPlaca Esp32H2 = new(
        "esp32-h2-devkit", "ESP32-H2 DevKit", NumPinos: 28, MaxAdc: 4095, LedInterno: 8)
    {
        BitsDoAnalogRead = 12,
        // I2C de fabrica: 12 e 22 (eram 1 e 2 aqui ate a Alpha 19).
        SdaPadrao = 12, SclPadrao = 22,
        Processador = "RISC-V", Nucleos = 1, Megahertz = 96,
        RamBytes = 327680, FlashBytes = 1310720,
        BaseDaRam = 20000, BaseDaFlash = 200000, PinoTx = 24,
        NomesDoCore = Core(Seguidos("A", 0, 4, 1), Pares(
            ("TX", 24), ("RX", 23), ("SDA", 12), ("SCL", 22),
            ("SS", 0), ("MOSI", 25), ("MISO", 11), ("SCK", 10))),
        Numero = NumeroComSerial(24, 23),
    };

    // =================================================================
    //  ESP8266
    // =================================================================
    //
    // O D4 da NodeMCU é o GPIO2. Não é erro de digitação: a placa
    // renumerou os pinos e a tabela não bate com nada. É a pegadinha
    // mais comum da aula, e o motivo de esta tradução existir.
    static int NumeroDaNodeMcu(string nome) => Base(nome) switch
    {
        "D0" => 16, "D1" => 5, "D2" => 4, "D3" => 0, "D4" => 2,
        "D5" => 14, "D6" => 12, "D7" => 13, "D8" => 15,
        "RX" => 3, "TX" => 1,
        "A0" => 17,
        _ => -1,
    };

    public static readonly PerfilPlaca NodeMcu = new(
        "esp8266-nodemcu", "NodeMCU (ESP8266)", NumPinos: 18, MaxAdc: 1023, LedInterno: 2)
    {
        TemWifi = true,
        SdaPadrao = 4, SclPadrao = 5,   // D2 e D1
        Processador = "ESP8266", Nucleos = 1, Megahertz = 80,
        RamBytes = 81920, FlashBytes = 1044464,
        BaseDaRam = 28000, BaseDaFlash = 257000, PinoTx = 1,

        // variants/nodemcu/pins_arduino.h: o D5 do sketch e o GPIO14,
        // exatamente como na serigrafia.
        NomesDoCore = Core(DoEsp8266(), Pares(
            ("D0", 16), ("D1", 5), ("D2", 4), ("D3", 0), ("D4", 2), ("D5", 14),
            ("D6", 12), ("D7", 13), ("D8", 15), ("D9", 3), ("D10", 1),
            ("LED_BUILTIN_AUX", 16))),
        Numero = NumeroDaNodeMcu,
    };

    /// <summary>O que todo ESP8266 tem: variants/generic/common.h e pins_arduino.h.</summary>
    static (string, int)[] DoEsp8266()
        => Pares(("SDA", 4), ("SCL", 5), ("SS", 15), ("MOSI", 13), ("MISO", 12), ("SCK", 14),
                 ("A0", 17));

    /// <summary>
    /// Os módulos ESP-xx: aqui o nome do pino já É o GPIO.
    ///
    /// Sem placa no meio não há renumeração -- "GPIO4" é o GPIO 4 --, e
    /// é exatamente por isso que estes módulos são mais fáceis de ligar
    /// certo do que a NodeMCU.
    /// </summary>
    static int NumeroDoModuloEsp(string nome) => Base(nome) switch
    {
        // Os diagramas da Espressif escrevem TXD e RXD; quase todo
        // esquema de aula escreve TX e RX. Os quatro nomes valem, e
        // sao os mesmos GPIO 1 e 3 -- que nos modulos de cinco pinos
        // sao os UNICOS GPIO que saem para fora.
        "TXD" or "TX" => 1,
        "RXD" or "RX" => 3,
        "ADC" => 17,
        var b => ComPrefixo(b, "GPIO"),
    };

    static PerfilPlaca Modulo(string id, string nome) => new(
        id, nome, NumPinos: 18, MaxAdc: 1023, LedInterno: 2)
    {
        TemWifi = true,
        SdaPadrao = 4, SclPadrao = 5,
        Processador = "ESP8266", Nucleos = 1, Megahertz = 80,
        RamBytes = 81920, FlashBytes = 1044464,
        BaseDaRam = 28000, BaseDaFlash = 257000, PinoTx = 1,
        NomesDoCore = Core(DoEsp8266()),
        Numero = NumeroDoModuloEsp,
    };

    public static readonly PerfilPlaca Esp01 = Modulo("esp-01", "ESP-01");
    public static readonly PerfilPlaca Esp02 = Modulo("esp-02", "ESP-02");
    public static readonly PerfilPlaca Esp03 = Modulo("esp-03", "ESP-03");
    public static readonly PerfilPlaca Esp04 = Modulo("esp-04", "ESP-04");
    public static readonly PerfilPlaca Esp05 = Modulo("esp-05", "ESP-05");
    public static readonly PerfilPlaca Esp06 = Modulo("esp-06", "ESP-06");
    public static readonly PerfilPlaca Esp07 = Modulo("esp-07", "ESP-07");
    public static readonly PerfilPlaca Esp08 = Modulo("esp-08", "ESP-08");
    public static readonly PerfilPlaca Esp09 = Modulo("esp-09", "ESP-09");
    public static readonly PerfilPlaca Esp10 = Modulo("esp-10", "ESP-10");
    public static readonly PerfilPlaca Esp11 = Modulo("esp-11", "ESP-11");
    public static readonly PerfilPlaca Esp12 = Modulo("esp-12", "ESP-12");
    public static readonly PerfilPlaca Esp12E = Modulo("esp-12e", "ESP-12E");
    public static readonly PerfilPlaca Esp12F = Modulo("esp-12f", "ESP-12F");
    public static readonly PerfilPlaca Esp13 = Modulo("esp-13", "ESP-13");
    public static readonly PerfilPlaca Esp14 = Modulo("esp-14", "ESP-14");

    // =================================================================
    //  ATtiny85, o chip solto
    // =================================================================
    //
    // "ATtiny85 (separado do franzininho)": o Franzininho é uma PLACA
    // com este chip dentro. Quem projeta um circuito com o chip nu
    // precisa dele sozinho, em DIP-8, com os nomes do encapsulamento.
    public static readonly PerfilPlaca ATtiny85 = new(
        "attiny85", "ATtiny85", NumPinos: 6, MaxAdc: 1023, LedInterno: 1)
    {
        TensaoDeOperacao = 5, CorrenteDoPino = 40,
        SdaPadrao = 0, SclPadrao = 2,   // o USI: PB0 e PB2
        Processador = "ATtiny85", Nucleos = 1, Megahertz = 8,

        // Meio KB de RAM e 8 KB de flash inteiros: programado por ISP
        // não há bootloader comendo espaço, ao contrário do Franzininho.
        RamBytes = 512, FlashBytes = 8192, De8Bits = true,
        BaseDaRam = 15, BaseDaFlash = 200,
        NomesDoCore = Core(AnalogicosDoAttiny(), Pares(("SDA", 0), ("SCL", 2))),
        NomePorRegra = nome => ComPrefixo(nome, "PB"),
        Numero = nome => ComPrefixo(nome, "PB"),
    };

    // =================================================================
    //  ST Nucleo
    // =================================================================
    //
    // As duas trazem o cabeçalho do Arduino, e é com ele que a
    // numeração funciona: D<n> é o n, e A<n> continua a contagem depois
    // dos digitais -- a mesma regra do Uno e do Nano.
    public static readonly PerfilPlaca NucleoL031K6 = new(
        "st-nucleo-l031k6", "ST Nucleo-L031K6", NumPinos: 22, MaxAdc: 4095, LedInterno: 13)
    {
        TensaoDeOperacao = 3.3, CorrenteDoPino = 20,
        SdaPadrao = 18, SclPadrao = 19,   // A4 e A5
        Processador = "STM32L031K6", Nucleos = 1, Megahertz = 32,
        RamBytes = 8192, FlashBytes = 32768,
        BaseDaRam = 2000, BaseDaFlash = 10000,
        NomePorRegra = nome => ComPrefixo(nome, "D"),
        Numero = nome => ComPrefixo(nome, "D") is var d && d >= 0 ? d : Analogico(nome, 14),
    };

    public static readonly PerfilPlaca NucleoC031C6 = new(
        "st-nucleo-c031c6", "ST Nucleo-C031C6", NumPinos: 22, MaxAdc: 4095, LedInterno: 13)
    {
        TensaoDeOperacao = 3.3, CorrenteDoPino = 20,
        SdaPadrao = 14, SclPadrao = 15,   // D14 e D15, como no cabeçalho do Uno
        Processador = "STM32C031C6", Nucleos = 1, Megahertz = 48,
        RamBytes = 12288, FlashBytes = 32768,
        BaseDaRam = 2400, BaseDaFlash = 10000,
        NomesDoCore = Core(Pares(("SDA", 14), ("SCL", 15))),
        NomePorRegra = nome => ComPrefixo(nome, "D"),
        Numero = nome => ComPrefixo(nome, "D") is var d && d >= 0 ? d : Analogico(nome, 14),
    };

    // =================================================================
    //  Raspberry Pi Pico W
    // =================================================================
    //
    // Mesmo RP2040 da Pico, mais o rádio CYW43439. O LED muda de lugar
    // e é a diferença que mais pega: na Pico ele é o GP25, e na W ele
    // está no próprio rádio, fora dos GPIO.
    public static readonly PerfilPlaca PiPicoW = new(
        "pi-pico-w", "Raspberry Pi Pico W", NumPinos: 34, MaxAdc: 4095, LedInterno: 32)
    {
        TensaoDeOperacao = 3.3, CorrenteDoPino = 12,
        TemWifi = true, AdcVrefNoPino = true,
        SdaPadrao = 4, SclPadrao = 5,
        Processador = "RP2040", Nucleos = 2, Megahertz = 133,
        RamBytes = 270336, FlashBytes = 2093056,    // 264 KB de SRAM (264 x 1024)
        BaseDaRam = 8000, BaseDaFlash = 50000,
        NomesDoCore = NomesDaPico(),
        Numero = nome => ComPrefixo(nome, "GP"),
    };

    /// <summary>Tudo o que a Alpha 18 acrescentou, na ordem em que se procura.</summary>
    internal static readonly PerfilPlaca[] DaAlpha18 =
    [
        Esp32S2, Esp32S3, Esp32C3, Esp32C6, Esp32H2,
        NodeMcu,
        Esp01, Esp02, Esp03, Esp04, Esp05, Esp06, Esp07, Esp08,
        Esp09, Esp10, Esp11, Esp12, Esp12E, Esp12F, Esp13, Esp14,
        ATtiny85,
        NucleoL031K6, NucleoC031C6,
        PiPicoW,
    ];
}
