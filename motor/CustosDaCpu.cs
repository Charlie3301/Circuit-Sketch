namespace Simulador;

/// <summary>
/// Quantos ciclos do processador cada coisa custa, em cada familia de chip.
///
/// O interpretador faz as contas na velocidade do computador, e isso nao e a
/// velocidade de placa nenhuma: um Uno achando os primos ate 20 mil leva mais
/// de um minuto (a divisao de 32 bits e feita em software, ~650 ciclos), e o
/// simulador respondia na hora; um laco de float que a ESP32 faz em
/// microssegundos (ela tem FPU) levava o tempo do interpretador. Com o custo
/// de cada operacao em ciclos do chip, o relogio do sketch anda o que a placa
/// andaria -- e o micros() mede o que mediria na bancada.
///
/// De onde vem os numeros:
///  - AVR: a tabela de desempenho da propria avr-libc (avr-gcc 4.7.1): fadd
///    108 ciclos, fmul 138 (375 sem o multiplicador, como no ATtiny85), fdiv
///    465, sqrt 492, sin 1653, pow 5047; a divisao de 32 bits do libgcc chega
///    a 653 instrucoes.
///  - Familia ESP32: o CoreMark de cada chip (1 nucleo): ESP32 504,85 a 240
///    MHz, S2 472,73, S3 613,86, C3 407,22 e C6 464,36 a 160 MHz -- e daqui
///    que sai a escala do codigo inteiro de cada um. A ESP32 e a S3 tem FPU de
///    precisao simples; S2, C3, C6 e H2 fazem float em software; o double e
///    em software em todas.
///  - RP2040: as rotinas de float da ROM (~60 ciclos por soma a 125 MHz,
///    medidas no forum da Raspberry Pi), o divisor inteiro do SIO (8 ciclos).
/// Sao estimativas da mesma ordem de grandeza da placa -- nao um emulador de
/// ciclo a ciclo. O Assembly do AVR continua contando ciclo a ciclo.
/// </summary>
public sealed class CustosDaCpu
{
    public string Nome { get; init; } = "";

    /// <summary>Soma, subtracao, comparacao, logica e bits: por largura (8, 16, 32, 64 bits).</summary>
    public double[] Soma { get; init; } = [1, 1, 1, 3];
    public double[] Multiplica { get; init; } = [1, 1, 1, 10];
    public double[] Divide { get; init; } = [20, 20, 20, 150];
    public double[] Desloca { get; init; } = [1, 1, 1, 3];

    public double FloatSoma { get; init; }
    public double FloatMultiplica { get; init; }
    public double FloatDivide { get; init; }
    public double FloatCompara { get; init; }
    public double FloatConverte { get; init; }
    public double DoubleSoma { get; init; }
    public double DoubleMultiplica { get; init; }
    public double DoubleDivide { get; init; }
    public double DoubleCompara { get; init; }
    public double DoubleConverte { get; init; }

    public double FloatRaiz { get; init; }
    public double FloatTrig { get; init; }
    public double FloatExpLog { get; init; }
    public double FloatPotencia { get; init; }
    public double DoubleRaiz { get; init; }
    public double DoubleTrig { get; init; }
    public double DoubleExpLog { get; init; }
    public double DoublePotencia { get; init; }

    /// <summary>Ler ou guardar uma variavel: no AVR, por byte (8 bits por vez); nos outros, por palavra.</summary>
    public double LerPorByte { get; init; }
    public double LerPalavra { get; init; } = 1;
    public double Indexar { get; init; } = 2;

    /// <summary>Chamar uma funcao do sketch: o call, o ret e o que se salva na pilha.</summary>
    public double ChamarFuncao { get; init; } = 10;

    /// <summary>O salto de cada comando (if, laco): o resto ja foi cobrado na conta.</summary>
    public double Comando { get; init; } = 2;

    /// <summary>Uma funcao de biblioteca sem custo proprio conhecido.</summary>
    public double ChamarDoCore { get; init; } = 40;

    /// <summary>
    /// MicroPython: o interpretador de bytecode da placa, por no da arvore. E
    /// o que faz o mesmo laco levar ~100 vezes o tempo do C.
    /// </summary>
    public double PythonPorNo { get; init; } = 250;

    public double AnalogReadUs { get; init; } = 20;
    public double Millis { get; init; } = 50;
    public double Micros { get; init; } = 50;

    /// <summary>O print de um numero: cada digito e uma divisao do long por 10.</summary>
    public double PorDigitoImpresso { get; init; } = 30;

    /// <summary>Copiar texto (String, strcpy): por byte.</summary>
    public double PorByteDeTexto { get; init; } = 2;

    /// <summary>Um malloc/new/realloc: o alocador da placa.</summary>
    public double Alocar { get; init; } = 400;

    /// <summary>
    /// O codigo inteiro do chip, relativo a ESP32 (CoreMark por MHz): a S3
    /// faz o mesmo trabalho em 82% dos ciclos, a C6 em 72%.
    /// </summary>
    public double Escala { get; init; } = 1;

    public double DaLargura(double[] tabela, int bits) => tabela[bits <= 8 ? 0 : bits <= 16 ? 1 : bits <= 32 ? 2 : 3] * Escala;

    // =====================================================================
    //  as familias
    // =====================================================================

    static readonly CustosDaCpu Avr = new()
    {
        Nome = "AVR",
        Soma = [1, 2, 4, 8], Multiplica = [2, 8, 40, 180], Divide = [80, 220, 640, 3500], Desloca = [3, 12, 30, 60],
        FloatSoma = 108, FloatMultiplica = 138, FloatDivide = 465, FloatCompara = 50, FloatConverte = 70,
        DoubleSoma = 108, DoubleMultiplica = 138, DoubleDivide = 465, DoubleCompara = 50, DoubleConverte = 70,
        FloatRaiz = 492, FloatTrig = 1900, FloatExpLog = 2450, FloatPotencia = 5047,
        DoubleRaiz = 492, DoubleTrig = 1900, DoubleExpLog = 2450, DoublePotencia = 5047,
        LerPorByte = 0.5, LerPalavra = 0, Indexar = 6, ChamarFuncao = 24, Comando = 2, ChamarDoCore = 30,
        PythonPorNo = 2000, AnalogReadUs = 111, Millis = 20, Micros = 56,
        PorDigitoImpresso = 700, PorByteDeTexto = 8, Alocar = 200,
    };

    /// <summary>O ATtiny85 nao tem multiplicador: ate o float multiplica em software.</summary>
    static readonly CustosDaCpu AvrSemMul = new()
    {
        Nome = "AVR sem MUL",
        Soma = [1, 2, 4, 8], Multiplica = [40, 150, 600, 2400], Divide = [80, 220, 640, 3500], Desloca = [3, 12, 30, 60],
        FloatSoma = 113, FloatMultiplica = 375, FloatDivide = 466, FloatCompara = 50, FloatConverte = 70,
        DoubleSoma = 113, DoubleMultiplica = 375, DoubleDivide = 466, DoubleCompara = 50, DoubleConverte = 70,
        FloatRaiz = 494, FloatTrig = 3700, FloatExpLog = 4400, FloatPotencia = 9293,
        DoubleRaiz = 494, DoubleTrig = 3700, DoubleExpLog = 4400, DoublePotencia = 9293,
        LerPorByte = 0.5, LerPalavra = 0, Indexar = 6, ChamarFuncao = 24, Comando = 2, ChamarDoCore = 30,
        PythonPorNo = 2000, AnalogReadUs = 111, Millis = 20, Micros = 56,
        PorDigitoImpresso = 700, PorByteDeTexto = 8, Alocar = 200,
    };

    /// <summary>ESP32 (Xtensa LX6): FPU de precisao simples, divisor inteiro de 32 bits.</summary>
    static CustosDaCpu XtensaComFpu(string nome, double escala) => new()
    {
        Nome = nome, Escala = escala,
        Soma = [1, 1, 1, 3], Multiplica = [2, 2, 2, 12], Divide = [12, 12, 12, 180], Desloca = [1, 1, 1, 4],
        FloatSoma = 3, FloatMultiplica = 3, FloatDivide = 20, FloatCompara = 3, FloatConverte = 3,
        DoubleSoma = 70, DoubleMultiplica = 100, DoubleDivide = 350, DoubleCompara = 30, DoubleConverte = 40,
        FloatRaiz = 30, FloatTrig = 150, FloatExpLog = 180, FloatPotencia = 400,
        DoubleRaiz = 800, DoubleTrig = 2500, DoubleExpLog = 2800, DoublePotencia = 6000,
        LerPalavra = 0.5, Indexar = 2, ChamarFuncao = 10, Comando = 1.5, ChamarDoCore = 40,
        PythonPorNo = 250, AnalogReadUs = 25, Millis = 60, Micros = 50,
        PorDigitoImpresso = 30, PorByteDeTexto = 2, Alocar = 600,
    };

    /// <summary>Sem FPU (ESP32-S2, e em software tambem nos RISC-V da Espressif).</summary>
    static CustosDaCpu SemFpu(string nome, double escala, double divide) => new()
    {
        Nome = nome, Escala = escala,
        Soma = [1, 1, 1, 3], Multiplica = [1, 1, 1, 8], Divide = [divide, divide, divide, 200], Desloca = [1, 1, 1, 3],
        FloatSoma = 55, FloatMultiplica = 65, FloatDivide = 150, FloatCompara = 18, FloatConverte = 25,
        DoubleSoma = 80, DoubleMultiplica = 120, DoubleDivide = 320, DoubleCompara = 28, DoubleConverte = 35,
        FloatRaiz = 350, FloatTrig = 1000, FloatExpLog = 1100, FloatPotencia = 2600,
        DoubleRaiz = 750, DoubleTrig = 2500, DoubleExpLog = 2700, DoublePotencia = 5800,
        LerPalavra = 0.5, Indexar = 2, ChamarFuncao = 8, Comando = 1.5, ChamarDoCore = 40,
        PythonPorNo = 250, AnalogReadUs = 25, Millis = 60, Micros = 50,
        PorDigitoImpresso = 40, PorByteDeTexto = 2, Alocar = 500,
    };

    static readonly CustosDaCpu Esp32 = XtensaComFpu("Xtensa LX6", 1.0);
    static readonly CustosDaCpu Esp32S3 = XtensaComFpu("Xtensa LX7", 2.10 / 2.56);
    static readonly CustosDaCpu Esp32S2 = SemFpu("Xtensa LX7 sem FPU", 2.10 / 1.97, 12);
    static readonly CustosDaCpu Esp32C3 = SemFpu("RISC-V", 2.10 / 2.55, 34);
    static readonly CustosDaCpu Esp32C6 = SemFpu("RISC-V", 2.10 / 2.90, 34);

    /// <summary>ESP8266 (Xtensa L106): sem FPU e sem divisor -- divide numa rotina da ROM.</summary>
    static readonly CustosDaCpu Esp8266 = new()
    {
        Nome = "ESP8266", Escala = 2.10 / 1.85,
        Soma = [1, 1, 1, 3], Multiplica = [2, 2, 3, 14], Divide = [45, 45, 45, 400], Desloca = [1, 1, 1, 4],
        FloatSoma = 70, FloatMultiplica = 80, FloatDivide = 250, FloatCompara = 25, FloatConverte = 35,
        DoubleSoma = 110, DoubleMultiplica = 200, DoubleDivide = 600, DoubleCompara = 40, DoubleConverte = 50,
        FloatRaiz = 400, FloatTrig = 1500, FloatExpLog = 1700, FloatPotencia = 3500,
        DoubleRaiz = 1000, DoubleTrig = 3500, DoubleExpLog = 4000, DoublePotencia = 8000,
        LerPalavra = 0.5, Indexar = 2, ChamarFuncao = 10, Comando = 1.5, ChamarDoCore = 40,
        PythonPorNo = 300, AnalogReadUs = 90, Millis = 50, Micros = 40,
        PorDigitoImpresso = 60, PorByteDeTexto = 2, Alocar = 400,
    };

    /// <summary>RP2040 (Cortex-M0+): float e double pelas rotinas otimizadas da ROM, divisor no SIO.</summary>
    static readonly CustosDaCpu Rp2040 = new()
    {
        Nome = "RP2040", Escala = 2.10 / 2.33,
        Soma = [1, 1, 1, 3], Multiplica = [1, 1, 1, 10], Divide = [20, 20, 20, 150], Desloca = [1, 1, 1, 3],
        FloatSoma = 60, FloatMultiplica = 57, FloatDivide = 71, FloatCompara = 20, FloatConverte = 25,
        DoubleSoma = 110, DoubleMultiplica = 120, DoubleDivide = 200, DoubleCompara = 30, DoubleConverte = 40,
        FloatRaiz = 70, FloatTrig = 150, FloatExpLog = 170, FloatPotencia = 400,
        DoubleRaiz = 250, DoubleTrig = 500, DoubleExpLog = 550, DoublePotencia = 1200,
        LerPalavra = 1.5, Indexar = 3, ChamarFuncao = 10, Comando = 2, ChamarDoCore = 40,
        PythonPorNo = 300, AnalogReadUs = 4, Millis = 40, Micros = 30,
        PorDigitoImpresso = 30, PorByteDeTexto = 3, Alocar = 300,
    };

    /// <summary>STM32F103 (Cortex-M3): divisor em hardware, float em software.</summary>
    static readonly CustosDaCpu CortexM3 = new()
    {
        Nome = "Cortex-M3", Escala = 2.10 / 2.6,
        Soma = [1, 1, 1, 3], Multiplica = [1, 1, 1, 6], Divide = [8, 8, 8, 150], Desloca = [1, 1, 1, 3],
        FloatSoma = 45, FloatMultiplica = 50, FloatDivide = 120, FloatCompara = 15, FloatConverte = 20,
        DoubleSoma = 70, DoubleMultiplica = 90, DoubleDivide = 300, DoubleCompara = 25, DoubleConverte = 30,
        FloatRaiz = 300, FloatTrig = 800, FloatExpLog = 900, FloatPotencia = 2000,
        DoubleRaiz = 700, DoubleTrig = 2000, DoubleExpLog = 2200, DoublePotencia = 5000,
        LerPalavra = 1.5, Indexar = 2, ChamarFuncao = 8, Comando = 2, ChamarDoCore = 60,
        PythonPorNo = 300, AnalogReadUs = 30, Millis = 30, Micros = 60,
        PorDigitoImpresso = 30, PorByteDeTexto = 3, Alocar = 300,
    };

    /// <summary>STM32L0 e C0 (Cortex-M0+): nem divisor -- divide em software.</summary>
    static readonly CustosDaCpu CortexM0 = new()
    {
        Nome = "Cortex-M0+", Escala = 2.10 / 2.3,
        Soma = [1, 1, 1, 3], Multiplica = [1, 1, 1, 12], Divide = [60, 60, 60, 400], Desloca = [1, 1, 1, 3],
        FloatSoma = 90, FloatMultiplica = 100, FloatDivide = 300, FloatCompara = 30, FloatConverte = 40,
        DoubleSoma = 150, DoubleMultiplica = 250, DoubleDivide = 700, DoubleCompara = 50, DoubleConverte = 60,
        FloatRaiz = 600, FloatTrig = 1600, FloatExpLog = 1800, FloatPotencia = 4000,
        DoubleRaiz = 1500, DoubleTrig = 4000, DoubleExpLog = 4400, DoublePotencia = 10000,
        LerPalavra = 1.5, Indexar = 3, ChamarFuncao = 10, Comando = 2, ChamarDoCore = 60,
        PythonPorNo = 350, AnalogReadUs = 30, Millis = 30, Micros = 60,
        PorDigitoImpresso = 60, PorByteDeTexto = 3, Alocar = 300,
    };

    public static CustosDaCpu De(PerfilPlaca placa)
    {
        if (placa is null) return Esp32;
        return placa.Processador switch
        {
            "ATtiny85" => AvrSemMul,
            _ when placa.De8Bits => Avr,
            "Xtensa LX6" => Esp32,
            "Xtensa LX7" => placa.Id == "esp32-s2-devkit" ? Esp32S2 : Esp32S3,
            "RISC-V" => placa.Id == "esp32-c3-devkit" ? Esp32C3 : Esp32C6,
            "ESP8266" => Esp8266,
            "RP2040" => Rp2040,
            "STM32F103C8" => CortexM3,
            _ => CortexM0,
        };
    }

    /// <summary>A conta de dois operandos, no tipo em que ela e feita.</summary>
    public double DaOperacao(string op, TipoC tipo, ModeloC modelo)
    {
        switch (tipo)
        {
            case TipoC.Float:
            case TipoC.Double when modelo.BitsDoDouble == 32:
            case TipoC.LongDouble when modelo.BitsDoLongDouble == 32:
                return op switch
                {
                    "+" or "-" => FloatSoma,
                    "*" => FloatMultiplica,
                    "/" => FloatDivide,
                    _ => FloatCompara,
                };
            case TipoC.Double:
            case TipoC.LongDouble:
                return op switch
                {
                    "+" or "-" => DoubleSoma,
                    "*" => DoubleMultiplica,
                    "/" => DoubleDivide,
                    _ => DoubleCompara,
                };
        }

        int bits = modelo.Bits(tipo);
        return op switch
        {
            "*" => DaLargura(Multiplica, bits),
            "/" or "%" => DaLargura(Divide, bits),
            "<<" or ">>" => DaLargura(Desloca, bits),
            _ => DaLargura(Soma, bits),
        };
    }

    /// <summary>Ler (ou guardar) um valor deste tipo.</summary>
    public double DeLer(TipoC tipo, ModeloC modelo)
    {
        if (LerPorByte > 0)
        {
            int bits = tipo == TipoC.Nenhum ? modelo.BitsDoInt : modelo.Bits(tipo);
            return LerPorByte * Math.Max(1, bits / 8);
        }
        return LerPalavra * (tipo is TipoC.LongLong or TipoC.ULongLong
                             || (tipo is TipoC.Double && modelo.BitsDoDouble == 64) ? 2 : 1) * Escala;
    }

    /// <summary>Uma funcao da math.h: raiz, trigonometria, exp/log, potencia.</summary>
    public double DaMatematica(string nome, bool emFloat)
    {
        string raiz = emFloat && nome.EndsWith('f') ? nome[..^1] : nome;
        return raiz switch
        {
            "sqrt" or "cbrt" or "hypot" => emFloat ? FloatRaiz : DoubleRaiz,
            "sin" or "cos" or "tan" or "asin" or "acos" or "atan" or "atan2"
                or "sinh" or "cosh" or "tanh" => emFloat ? FloatTrig : DoubleTrig,
            "exp" or "log" or "log10" or "log2" or "exp2" or "expm1" or "log1p" => emFloat ? FloatExpLog : DoubleExpLog,
            "pow" => emFloat ? FloatPotencia : DoublePotencia,
            _ => emFloat ? FloatSoma : DoubleSoma,
        };
    }
}
