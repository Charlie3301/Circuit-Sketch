namespace Simulador;

/// <summary>
/// Objetos de biblioteca que os sketches usam (DHT, NeoPixel, Stepper...).
///
/// Decisao de projeto: simular no nivel da BIBLIOTECA, nao do fio. O
/// Wokwi emula o firmware real, entao precisa do protocolo 1-wire e do
/// I2C bit a bit. Aqui o sketch e interpretado, e o aluno nunca escreve
/// o protocolo -- ele escreve dht.readTemperature(). Simular a camada de
/// baixo seria trabalho enorme sem nada a mais na tela.
///
/// Os valores lidos vem de Maquina.Leituras, que o editor preenche a
/// partir dos controles de cada peca.
/// </summary>
public static class Bibliotecas
{
    /// <summary>Classes que podem ser declaradas no sketch.</summary>
    public static readonly string[] Conhecidas =
    [
        "Servo", "LiquidCrystal", "LiquidCrystal_I2C",
        "DHT", "Adafruit_NeoPixel", "Stepper", "Keypad",
        "Adafruit_MPU6050", "MPU6050", "RTC_DS1307", "DateTime", "HX711",
        "Adafruit_SSD1306", "IRrecv", "decode_results", "Adafruit_ILI9341",
        "Adafruit_ST7789",
        "File", "RF24",

        // Rede: WiFi, HTTP, MQTT e Bluetooth.
        "WiFiClient", "WiFiClientSecure", "WiFiServer", "WebServer",
        "HTTPClient", "PubSubClient", "BluetoothSerial", "IPAddress",

        // Telegram e a memoria que sobrevive ao reset. As duas entraram
        // juntas por causa do alarme da turma, que usa as duas: o bot
        // avisa e o Preferences lembra se o alarme estava armado.
        "UniversalTelegramBot", "Preferences",

        // O SPI do chip: outro barramento ("SPIClass hspi(HSPI)") e a configuracao
        // de uma transacao ("SPISettings(1000000, MSBFIRST, SPI_MODE0)").
        "SPIClass", "SPISettings",

        // A BLE da ESP32: os objetos que o sketch declara ou cria com new
        // (os outros -- servidor, servico, caracteristica -- vem do
        // BLEDevice::createServer() e dos create...()).
        "BLE2902", "BLE2901", "BLE2904", "BLEDescriptor", "BLEUUID", "BLEAdvertisementData",
        "BLESecurity", "BLEAddress",

        // As portas seriais declaradas no sketch.
        "HardwareSerial", "SoftwareSerial",

        // O segundo barramento I2C declarado no sketch.
        "TwoWire",

        // A ArduinoBLE (Nano RP2040 Connect e ESP32).
        "BLEService", "BLECharacteristic", "BLEDevice", "BLEStringCharacteristic",
        "BLEBoolCharacteristic", "BLEBooleanCharacteristic", "BLECharCharacteristic", "BLEUnsignedCharCharacteristic",
        "BLEByteCharacteristic", "BLEShortCharacteristic", "BLEUnsignedShortCharacteristic", "BLEWordCharacteristic",
        "BLEIntCharacteristic", "BLEUnsignedIntCharacteristic", "BLELongCharacteristic", "BLEUnsignedLongCharacteristic",
        "BLEFloatCharacteristic", "BLEDoubleCharacteristic",

        // A placa como cliente: procurando e conectando no servidor de outra.
        "BLEAdvertisedDevice", "BLEScan", "BLEScanResults", "BLEClient", "BLERemoteService",
        "BLERemoteCharacteristic",
    ];

    public static bool Existe(string classe) => Conhecidas.Contains(classe);
}

/// <summary>
/// Uma fita de LEDs enderecaveis (NeoPixel, anel ou matriz).
///
/// Sao DOIS vetores, como na biblioteca: o que o sketch escreveu e o que os
/// LEDs mostram. setPixelColor so mexe no primeiro; e o show() que manda a
/// fita inteira para os LEDs. Antes era um vetor so, e a fita acendia sem o
/// show() -- o sketch funcionava aqui e ficava apagado na bancada, que e o
/// erro mais comum de quem comeca com NeoPixel.
/// </summary>
public class Fita(int quantidade)
{
    public int Quantidade { get; } = Math.Clamp(quantidade, 1, 256);
    public int Brilho { get; set; } = 255;

    /// <summary>A cor que cada LED MOSTRA agora, em 0x00RRGGBB.</summary>
    public int[] Cores { get; } = new int[Math.Clamp(quantidade, 1, 256)];

    /// <summary>O que o sketch escreveu e ainda nao mandou com show().</summary>
    public int[] Pendentes { get; } = new int[Math.Clamp(quantidade, 1, 256)];

    /// <summary>Quantas vezes o show() mandou a fita para os LEDs.</summary>
    public int Envios { get; private set; }

    /// <summary>
    /// O maior numero de LEDs acesos ao mesmo tempo desde o inicio.
    ///
    /// Serve para julgar um pisca-pisca: o de seta acende a fita e
    /// apaga na MESMA volta do laco, entao quem olha so o estado final
    /// ve sempre zero e conclui que nao funciona.
    /// </summary>
    public int PicoAceso { get; private set; }

    /// <summary>setPixelColor: so no vetor do sketch, ate o proximo show().</summary>
    public void Definir(int i, int cor)
    {
        if (i < 0 || i >= Pendentes.Length) return;
        Pendentes[i] = cor & 0xFFFFFF;
    }

    /// <summary>O que o sketch escreveu no LED i (o getPixelColor).</summary>
    public int Ler(int i) => i >= 0 && i < Pendentes.Length ? Pendentes[i] : 0;

    /// <summary>clear(): zera o vetor do sketch. Os LEDs apagam no show().</summary>
    public void Limpar() => Array.Clear(Pendentes);

    /// <summary>show() / write(): a fita inteira vai para os LEDs.</summary>
    public void Mostrar()
    {
        Array.Copy(Pendentes, Cores, Cores.Length);
        Envios++;
        PicoAceso = Math.Max(PicoAceso, Cores.Count(c => c != 0));
    }

    public static int Montar(int r, int g, int b)
        => ((r & 0xFF) << 16) | ((g & 0xFF) << 8) | (b & 0xFF);

    // ---------------- as contas da Adafruit_NeoPixel ----------------
    //
    // Copiadas da biblioteca, e nao aproximadas: o arco-iris do exemplo
    // "strandtest" tem que sair aqui com as MESMAS cores da fita de
    // verdade, senao a pessoa ajusta a cor olhando para a tela e erra na
    // bancada.

    /// <summary>
    /// ColorHSV(matiz, saturacao, brilho): a matiz vai de 0 a 65535 e da a
    /// volta inteira no circulo de cores. E a conta da biblioteca, com o
    /// vermelho centrado na virada e as 1530 matizes que o RGB de 8 bits
    /// consegue mostrar.
    /// </summary>
    public static int Hsv(long matiz, int saturacao = 255, int valor = 255)
    {
        int hue = (int)(((matiz & 0xFFFF) * 1530L + 32768) / 65536);
        int r, g, b;

        if (hue < 510)
        {
            b = 0;
            if (hue < 255) { r = 255; g = hue; }
            else { r = 510 - hue; g = 255; }
        }
        else if (hue < 1020)
        {
            r = 0;
            if (hue < 765) { g = 255; b = hue - 510; }
            else { g = 1020 - hue; b = 255; }
        }
        else if (hue < 1530)
        {
            g = 0;
            if (hue < 1275) { r = hue - 1020; b = 255; }
            else { r = 255; b = 1530 - hue; }
        }
        else { r = 255; g = 0; b = 0; }

        int sat = saturacao & 0xFF, val = valor & 0xFF;
        long v1 = 1 + val;
        int s1 = 1 + sat;
        int s2 = 255 - sat;

        return (int)(((((((r * s1) >> 8) + s2) * v1) & 0xff00) << 8)
                   | ((((g * s1) >> 8) + s2) * v1 & 0xff00)
                   | (((((b * s1) >> 8) + s2) * v1) >> 8));
    }

    /// <summary>gamma8: a correcao de gama 2,6 da biblioteca, pela tabela dela.</summary>
    public static int Gama8(int x) => Gama[x & 0xFF];

    /// <summary>gamma32: a mesma correcao em cada um dos quatro bytes.</summary>
    public static int Gama32(long cor)
    {
        long saida = 0;
        for (int k = 0; k < 4; k++)
            saida |= (long)Gama[(int)((cor >> (8 * k)) & 0xFF)] << (8 * k);
        return (int)(saida & 0xFFFFFF);
    }

    /// <summary>sine8: o seno de 8 bits da biblioteca (0 a 255 entra e sai).</summary>
    public static int Seno8(int x) => Seno[x & 0xFF];

    // As duas tabelas sao as do Adafruit_NeoPixel.h, numero por numero.
    static readonly byte[] Seno =
    [
        128, 131, 134, 137, 140, 143, 146, 149, 152, 155, 158, 162, 165, 167, 170, 173,
        176, 179, 182, 185, 188, 190, 193, 196, 198, 201, 203, 206, 208, 211, 213, 215,
        218, 220, 222, 224, 226, 228, 230, 232, 234, 235, 237, 238, 240, 241, 243, 244,
        245, 246, 248, 249, 250, 250, 251, 252, 253, 253, 254, 254, 254, 255, 255, 255,
        255, 255, 255, 255, 254, 254, 254, 253, 253, 252, 251, 250, 250, 249, 248, 246,
        245, 244, 243, 241, 240, 238, 237, 235, 234, 232, 230, 228, 226, 224, 222, 220,
        218, 215, 213, 211, 208, 206, 203, 201, 198, 196, 193, 190, 188, 185, 182, 179,
        176, 173, 170, 167, 165, 162, 158, 155, 152, 149, 146, 143, 140, 137, 134, 131,
        128, 124, 121, 118, 115, 112, 109, 106, 103, 100, 97, 93, 90, 88, 85, 82,
        79, 76, 73, 70, 67, 65, 62, 59, 57, 54, 52, 49, 47, 44, 42, 40,
        37, 35, 33, 31, 29, 27, 25, 23, 21, 20, 18, 17, 15, 14, 12, 11,
        10, 9, 7, 6, 5, 5, 4, 3, 2, 2, 1, 1, 1, 0, 0, 0,
        0, 0, 0, 0, 1, 1, 1, 2, 2, 3, 4, 5, 5, 6, 7, 9,
        10, 11, 12, 14, 15, 17, 18, 20, 21, 23, 25, 27, 29, 31, 33, 35,
        37, 40, 42, 44, 47, 49, 52, 54, 57, 59, 62, 65, 67, 70, 73, 76,
        79, 82, 85, 88, 90, 93, 97, 100, 103, 106, 109, 112, 115, 118, 121, 124,
    ];

    static readonly byte[] Gama =
    [
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 1, 1, 1, 1,
        1, 1, 1, 1, 2, 2, 2, 2, 2, 2, 2, 2, 3, 3, 3, 3,
        3, 3, 4, 4, 4, 4, 5, 5, 5, 5, 5, 6, 6, 6, 6, 7,
        7, 7, 8, 8, 8, 9, 9, 9, 10, 10, 10, 11, 11, 11, 12, 12,
        13, 13, 13, 14, 14, 15, 15, 16, 16, 17, 17, 18, 18, 19, 19, 20,
        20, 21, 21, 22, 22, 23, 24, 24, 25, 25, 26, 27, 27, 28, 29, 29,
        30, 31, 31, 32, 33, 34, 34, 35, 36, 37, 38, 38, 39, 40, 41, 42,
        42, 43, 44, 45, 46, 47, 48, 49, 50, 51, 52, 53, 54, 55, 56, 57,
        58, 59, 60, 61, 62, 63, 64, 65, 66, 68, 69, 70, 71, 72, 73, 75,
        76, 77, 78, 80, 81, 82, 84, 85, 86, 88, 89, 90, 92, 93, 94, 96,
        97, 99, 100, 102, 103, 105, 106, 108, 109, 111, 112, 114, 115, 117, 119, 120,
        122, 124, 125, 127, 129, 130, 132, 134, 136, 137, 139, 141, 143, 145, 146, 148,
        150, 152, 154, 156, 158, 160, 162, 164, 166, 168, 170, 172, 174, 176, 178, 180,
        182, 184, 186, 188, 191, 193, 195, 197, 199, 202, 204, 206, 209, 211, 213, 215,
        218, 220, 223, 225, 227, 230, 232, 235, 237, 240, 242, 245, 247, 250, 252, 255,
    ];
}
