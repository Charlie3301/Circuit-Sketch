namespace Simulador;

/// <summary>
/// O ATmega328P como ele é, em números.
///
/// Endereços de registrador de E/S, mapa da memória de dados, vetores de
/// interrupção e contagem de ciclos saem todos do datasheet. Estão num
/// arquivo só porque são FATOS, e não decisões: mudá-los é mentir sobre
/// o chip.
///
/// Por que isso importa tanto aqui: um programa em Assembly marca tempo
/// contando instrução. Se o simulador cobrar um ciclo por instrução
/// quando o chip cobra dois, um laço de espera calibrado na tela sai com
/// metade do tempo na placa -- e um sistema de irrigação que rega dois
/// segundos em vez de quatro só mostra o defeito depois de plantado.
/// </summary>
public static class AvrIsa
{
    // =================================================================
    //  o mapa da memória de dados
    // =================================================================
    //
    //   0x0000 .. 0x001F   os 32 registradores
    //   0x0020 .. 0x005F   os 64 registradores de E/S (IN e OUT)
    //   0x0060 .. 0x00FF   E/S estendida (só por LDS e STS)
    //   0x0100 .. 0x08FF   2 KB de SRAM
    //
    // O detalhe que quase todo emulador simplificado erra: os
    // registradores ficam DENTRO do espaço de dados. "lds r16, 0x0002"
    // lê o r2, e um programa que faça isso tem que ver o r2.
    public const int TamanhoDosDados = 0x0900;
    public const int InicioDaSram = 0x0100;
    public const int FimDaSram = 0x08FF;

    /// <summary>O endereço de dados de um registrador de E/S.</summary>
    public const int BaseDaES = 0x20;

    // ---- registradores de E/S, no endereço de E/S (0x00..0x3F) ----
    public const int PINB = 0x03, DDRB = 0x04, PORTB = 0x05;
    public const int PINC = 0x06, DDRC = 0x07, PORTC = 0x08;
    public const int PIND = 0x09, DDRD = 0x0A, PORTD = 0x0B;
    public const int TIFR0 = 0x15, TIFR1 = 0x16, TIFR2 = 0x17;
    public const int EIFR = 0x1C, EIMSK = 0x1D;
    public const int SPL = 0x3D, SPH = 0x3E, SREG = 0x3F;

    // ---- E/S estendida, no endereço de DADOS ----
    public const int TCCR0A = 0x44, TCCR0B = 0x45, TCNT0 = 0x46;
    public const int OCR0A = 0x47, OCR0B = 0x48;
    public const int TIMSK0 = 0x6E, TIMSK1 = 0x6F, TIMSK2 = 0x70;
    public const int TCCR1A = 0x80, TCCR1B = 0x81;
    public const int TCNT1L = 0x84, TCNT1H = 0x85;
    public const int OCR1AL = 0x88, OCR1AH = 0x89;
    public const int TCCR2A = 0xB0, TCCR2B = 0xB1, TCNT2 = 0xB2;
    public const int EICRA = 0x69;
    public const int ADMUX = 0x7C, ADCSRA = 0x7A, ADCL = 0x78, ADCH = 0x79;

    /// <summary>
    /// Os nomes que o montador aceita, com o endereço de cada um.
    ///
    /// Os de E/S ficam no endereço de E/S -- que é o que IN, OUT, SBI e
    /// CBI usam. Os estendidos ficam no de dados, porque só se chega
    /// neles por LDS e STS.
    /// </summary>
    public static readonly Dictionary<string, int> Registradores =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["PINB"] = PINB, ["DDRB"] = DDRB, ["PORTB"] = PORTB,
            ["PINC"] = PINC, ["DDRC"] = DDRC, ["PORTC"] = PORTC,
            ["PIND"] = PIND, ["DDRD"] = DDRD, ["PORTD"] = PORTD,
            ["TIFR0"] = TIFR0, ["TIFR1"] = TIFR1, ["TIFR2"] = TIFR2,
            ["EIFR"] = EIFR, ["EIMSK"] = EIMSK,
            ["SPL"] = SPL, ["SPH"] = SPH, ["SREG"] = SREG,

            ["TCCR0A"] = TCCR0A, ["TCCR0B"] = TCCR0B, ["TCNT0"] = TCNT0,
            ["OCR0A"] = OCR0A, ["OCR0B"] = OCR0B,
            ["TIMSK0"] = TIMSK0, ["TIMSK1"] = TIMSK1, ["TIMSK2"] = TIMSK2,
            ["TCCR1A"] = TCCR1A, ["TCCR1B"] = TCCR1B,
            ["TCNT1L"] = TCNT1L, ["TCNT1H"] = TCNT1H, ["TCNT1"] = TCNT1L,
            ["OCR1AL"] = OCR1AL, ["OCR1AH"] = OCR1AH, ["OCR1A"] = OCR1AL,
            ["TCCR2A"] = TCCR2A, ["TCCR2B"] = TCCR2B, ["TCNT2"] = TCNT2,
            ["EICRA"] = EICRA,
            ["ADMUX"] = ADMUX, ["ADCSRA"] = ADCSRA,
            ["ADCL"] = ADCL, ["ADCH"] = ADCH,

            ["RAMEND"] = FimDaSram,
            ["SPMCSR"] = 0x37,
        };

    /// <summary>
    /// Os nomes de bit que os cabeçalhos da Atmel definem.
    ///
    /// "sbi PORTB, PB5" é como a folha de dados escreve, e é como
    /// aparece em todo exemplo. Sem esta tabela, "PB5" não vale nada e
    /// o programa acende o pino errado -- calado.
    /// </summary>
    public static readonly Dictionary<string, int> Bits =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["CS00"] = 0, ["CS01"] = 1, ["CS02"] = 2,
            ["CS10"] = 0, ["CS11"] = 1, ["CS12"] = 2,
            ["CS20"] = 0, ["CS21"] = 1, ["CS22"] = 2,
            ["WGM00"] = 0, ["WGM01"] = 1, ["WGM12"] = 3,
            ["TOIE0"] = 0, ["TOIE1"] = 0, ["TOIE2"] = 0,
            ["OCIE0A"] = 1, ["OCIE1A"] = 1, ["OCIE2A"] = 1,
            ["TOV0"] = 0, ["TOV1"] = 0, ["TOV2"] = 0,
            ["OCF0A"] = 1, ["OCF1A"] = 1,
            ["INT0"] = 0, ["INT1"] = 1,
            ["ISC00"] = 0, ["ISC01"] = 1, ["ISC10"] = 2, ["ISC11"] = 3,
            ["ADEN"] = 7, ["ADSC"] = 6, ["ADIF"] = 4,
            ["SREG_I"] = 7,
        };

    static AvrIsa()
    {
        // PB0..PB7, PC0..PC7, PD0..PD7: são só o número do bit, e
        // escrevê-los um a um seria vinte e quatro linhas iguais.
        foreach (char porta in "BCD")
            for (int bit = 0; bit < 8; bit++)
                Bits[$"P{porta}{bit}"] = bit;
    }

    // =================================================================
    //  os vetores de interrupção
    // =================================================================
    //
    // Endereços em PALAVRAS, de dois em dois, na ordem da folha de
    // dados. O primeiro é o reset, que é onde todo programa começa.
    public const int VetorReset = 0x0000;
    public const int VetorInt0 = 0x0002;
    public const int VetorInt1 = 0x0004;
    public const int VetorTimer2Ovf = 0x0012;
    public const int VetorTimer1CompA = 0x0016;
    public const int VetorTimer1Ovf = 0x001A;
    public const int VetorTimer0CompA = 0x001C;
    public const int VetorTimer0Ovf = 0x0020;

    /// <summary>Os nomes de vetor que o montador aceita no .org.</summary>
    public static readonly Dictionary<string, int> Vetores =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["INT0addr"] = VetorInt0,
            ["INT1addr"] = VetorInt1,
            ["OVF2addr"] = VetorTimer2Ovf,
            ["OC1Aaddr"] = VetorTimer1CompA,
            ["OVF1addr"] = VetorTimer1Ovf,
            ["OC0Aaddr"] = VetorTimer0CompA,
            ["OVF0addr"] = VetorTimer0Ovf,
        };

    // =================================================================
    //  o relógio
    // =================================================================
    /// <summary>
    /// A frequência de um Arduino Uno, em hertz.
    ///
    /// É o cristal de 16 MHz, que dá 62,5 ns por ciclo. Todo o resto do
    /// tempo em Assembly sai daqui.
    /// </summary>
    public const double Hertz = 16_000_000.0;

    /// <summary>Quantos nanossegundos vale um ciclo.</summary>
    public const double NanosPorCiclo = 1_000_000_000.0 / Hertz;

    // =================================================================
    //  os bits do SREG
    // =================================================================
    public const int BitC = 0, BitZ = 1, BitN = 2, BitV = 3;
    public const int BitS = 4, BitH = 5, BitT = 6, BitI = 7;
}
