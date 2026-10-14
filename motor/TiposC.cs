using System.Collections.Concurrent;

namespace Simulador;

/// <summary>
/// Os tipos numericos do C e do C++.
///
/// O interpretador guardava todo numero num double com uma marca de
/// "inteiro", e isso escondia justamente os defeitos que mais aparecem na
/// bancada: no Uno o int tem 16 bits, e "1000 * 60" da -5536 -- no
/// simulador dava 60000. "int t = millis();" volta a zero depois de 32
/// segundos; "unsigned long espera = 1000 * 60 * 5;" nao espera cinco
/// minutos. Com o tipo andando junto do valor, a conta e a do compilador
/// da placa.
/// </summary>
public enum TipoC : byte
{
    /// <summary>Sem tipo de C: texto, objeto, ou numero de outra linguagem.</summary>
    Nenhum = 0,
    Bool, Char, SChar, UChar, Short, UShort, Int, UInt, Long, ULong, LongLong, ULongLong,
    Float, Double, LongDouble,
}

/// <summary>O que a divisao inteira por zero faz em cada chip.</summary>
public enum DivisaoPorZero : byte
{
    /// <summary>
    /// A rotina (ou o divisor) devolve o quociente com todos os bits em 1 e o
    /// resto igual ao dividendo: AVR, RISC-V, RP2040.
    /// </summary>
    TudoUm,

    /// <summary>O Cortex-M devolve zero (o divisor nao gera excecao de fabrica).</summary>
    Zero,

    /// <summary>
    /// O Xtensa gera excecao: IntegerDivideByZero na ESP32 (Guru Meditation),
    /// instrucao ilegal no ESP8266. A placa reinicia.
    /// </summary>
    Trava,
}

/// <summary>
/// O C de uma familia de chips: o tamanho do int e do double, o sinal do
/// char, o que a divisao por zero faz.
///
/// Os numeros sairam dos proprios compiladores das placas (avr-gcc,
/// xtensa-esp32-elf-gcc, riscv32-esp-elf-gcc com -dM -E): __SIZEOF_INT__,
/// __SIZEOF_DOUBLE__, __CHAR_UNSIGNED__.
/// </summary>
public sealed class ModeloC
{
    public string Nome { get; init; } = "";

    /// <summary>16 no AVR (__SIZEOF_INT__ 2), 32 nos outros.</summary>
    public int BitsDoInt { get; init; } = 32;

    /// <summary>32 no AVR (o double e o float), 64 nos outros.</summary>
    public int BitsDoDouble { get; init; } = 64;

    public int BitsDoLongDouble { get; init; } = 64;

    /// <summary>O char tem sinal so no AVR; Xtensa, RISC-V e ARM definem __CHAR_UNSIGNED__.</summary>
    public bool CharComSinal { get; init; }

    /// <summary>time_t: uint32_t na avr-libc; 64 bits na newlib (ESP-IDF 5, Pico, STM32).</summary>
    public TipoC TipoDoTimeT { get; init; } = TipoC.LongLong;

    public DivisaoPorZero DivisaoPorZero { get; init; } = DivisaoPorZero.TudoUm;

    /// <summary>
    /// round() e a macro do Arduino.h do AVR, que devolve long. Nas outras o
    /// round() e o da math.h (std::round), que devolve double.
    /// </summary>
    public bool RoundDaMacro { get; init; }

    /// <summary>
    /// min() e max() sao o std::min/std::max (ESP32, ESP8266): os dois
    /// argumentos precisam ser do MESMO tipo, senao nao compila. No AVR sao
    /// macros, e no ArduinoCore-API (Pico, STM32) um template de dois tipos.
    /// </summary>
    public bool MinMaxDoStd { get; init; }

    public static readonly ModeloC Avr = new()
    {
        Nome = "AVR", BitsDoInt = 16, BitsDoDouble = 32, BitsDoLongDouble = 32, CharComSinal = true,
        TipoDoTimeT = TipoC.ULong, DivisaoPorZero = DivisaoPorZero.TudoUm, RoundDaMacro = true,
    };

    public static readonly ModeloC Xtensa = new()
    {
        Nome = "Xtensa", DivisaoPorZero = DivisaoPorZero.Trava, MinMaxDoStd = true,
    };

    public static readonly ModeloC Esp8266 = new()
    {
        Nome = "ESP8266", DivisaoPorZero = DivisaoPorZero.Trava, MinMaxDoStd = true,
    };

    public static readonly ModeloC RiscV = new()
    {
        Nome = "RISC-V", BitsDoLongDouble = 128, DivisaoPorZero = DivisaoPorZero.TudoUm, MinMaxDoStd = true,
    };

    public static readonly ModeloC Rp2040 = new() { Nome = "RP2040", DivisaoPorZero = DivisaoPorZero.TudoUm };

    public static readonly ModeloC Arm = new() { Nome = "ARM", DivisaoPorZero = DivisaoPorZero.Zero };

    public static ModeloC De(PerfilPlaca placa)
    {
        if (placa is null) return Xtensa;
        if (placa.De8Bits) return Avr;
        return placa.Processador switch
        {
            "Xtensa LX6" or "Xtensa LX7" => Xtensa,
            "ESP8266" => Esp8266,
            "RISC-V" => RiscV,
            "RP2040" => Rp2040,
            _ => Arm,
        };
    }
}

/// <summary>As regras do C para os tipos numericos: promocao, conversao, faixa.</summary>
public static class TiposC
{
    static readonly ConcurrentDictionary<string, TipoC> porNome = new();

    /// <summary>
    /// O tipo de uma declaracao: "unsigned long", "const uint8_t", "volatile
    /// int", "byte". Ponteiro, String, classe e struct nao sao numero:
    /// <see cref="TipoC.Nenhum"/>.
    /// </summary>
    public static TipoC DoNome(string tipo)
    {
        if (string.IsNullOrWhiteSpace(tipo)) return TipoC.Nenhum;
        return porNome.GetOrAdd(tipo, Resolver);
    }

    static TipoC Resolver(string tipo)
    {
        if (tipo.Contains('*') || tipo.Contains('&') || tipo.Contains('[') || tipo.Contains('<')) return TipoC.Nenhum;

        var palavras = tipo.Replace("std::", "")
            .Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries)
            .Where(p => p is not ("const" or "volatile" or "static" or "register" or "inline" or "extern"
                                  or "constexpr" or "mutable" or "thread_local"))
            .ToList();
        if (palavras.Count == 0) return TipoC.Nenhum;

        if (palavras.Count == 1)
        {
            switch (palavras[0])
            {
                case "bool": case "boolean": return TipoC.Bool;
                case "char": case "KeypadEvent": return TipoC.Char;
                case "byte": case "uint8_t": case "uint_least8_t": case "uint_fast8_t": return TipoC.UChar;
                case "int8_t": case "int_least8_t": case "int_fast8_t": return TipoC.SChar;
                case "short": case "int16_t": case "int_least16_t": return TipoC.Short;
                case "uint16_t": case "uint_least16_t": return TipoC.UShort;
                case "int": case "signed": return TipoC.Int;
                case "unsigned": case "word": case "size_t": case "uint": return TipoC.UInt;
                case "long": return TipoC.Long;
                // stdint.h: int32_t e "long int" no AVR, Xtensa e RISC-V da
                // Espressif; "int" no ARM. Para a conta os dois tem 32 bits.
                case "int32_t": case "int_least32_t": case "int_fast32_t": case "int_fast16_t":
                case "ptrdiff_t": case "intptr_t": case "ssize_t":
                    return TipoC.Long;
                case "uint32_t": case "uint_least32_t": case "uint_fast32_t": case "uint_fast16_t":
                case "uintptr_t":
                    return TipoC.ULong;
                case "int64_t": case "int_least64_t": case "int_fast64_t": case "intmax_t": return TipoC.LongLong;
                case "uint64_t": case "uint_least64_t": case "uint_fast64_t": case "uintmax_t": return TipoC.ULongLong;
                case "float": return TipoC.Float;
                case "double": return TipoC.Double;
                case "time_t": return TipoC.LongLong;   // o modelo de cada placa acerta no DoNome(tipo, modelo)
                default: return TipoC.Nenhum;
            }
        }

        bool semSinal = palavras.Remove("unsigned");
        bool comSinal = palavras.Remove("signed");
        palavras.Remove("int");
        int longs = palavras.RemoveAll(p => p == "long");
        bool curto = palavras.Remove("short");
        bool caractere = palavras.Remove("char");
        bool dbl = palavras.Remove("double");
        if (palavras.Count > 0) return TipoC.Nenhum;

        if (dbl) return longs > 0 ? TipoC.LongDouble : TipoC.Double;
        if (caractere) return semSinal ? TipoC.UChar : comSinal ? TipoC.SChar : TipoC.Char;
        if (curto) return semSinal ? TipoC.UShort : TipoC.Short;
        return longs switch
        {
            0 => semSinal ? TipoC.UInt : TipoC.Int,
            1 => semSinal ? TipoC.ULong : TipoC.Long,
            _ => semSinal ? TipoC.ULongLong : TipoC.LongLong,
        };
    }

    /// <summary>O tipo de uma declaracao nesta placa (o time_t muda de uma para outra).</summary>
    public static TipoC DoNome(string tipo, ModeloC modelo)
    {
        var t = DoNome(tipo);
        if (t == TipoC.LongLong && modelo is not null && tipo.Trim().EndsWith("time_t", StringComparison.Ordinal))
            return modelo.TipoDoTimeT;
        return t;
    }

    public static bool EhReal(TipoC t) => t is TipoC.Float or TipoC.Double or TipoC.LongDouble;

    public static bool EhInteiro(TipoC t) => t is not (TipoC.Nenhum or TipoC.Float or TipoC.Double or TipoC.LongDouble);

    public static int Bits(this ModeloC m, TipoC t) => t switch
    {
        TipoC.Bool or TipoC.Char or TipoC.SChar or TipoC.UChar => 8,
        TipoC.Short or TipoC.UShort => 16,
        TipoC.Int or TipoC.UInt => m.BitsDoInt,
        TipoC.Long or TipoC.ULong => 32,
        TipoC.LongLong or TipoC.ULongLong => 64,
        TipoC.Float => 32,
        TipoC.Double => m.BitsDoDouble,
        TipoC.LongDouble => m.BitsDoLongDouble,
        _ => 64,
    };

    public static bool ComSinal(this ModeloC m, TipoC t) => t switch
    {
        TipoC.Char => m.CharComSinal,
        TipoC.SChar or TipoC.Short or TipoC.Int or TipoC.Long or TipoC.LongLong => true,
        TipoC.Float or TipoC.Double or TipoC.LongDouble => true,
        _ => false,
    };

    /// <summary>Int, Long e LongLong (e os sem sinal): a ordem da conversao usual.</summary>
    static int Posto(TipoC t) => t switch
    {
        TipoC.Int or TipoC.UInt => 1,
        TipoC.Long or TipoC.ULong => 2,
        TipoC.LongLong or TipoC.ULongLong => 3,
        _ => 0,
    };

    static TipoC SemSinal(TipoC t) => t switch
    {
        TipoC.Int => TipoC.UInt,
        TipoC.Long => TipoC.ULong,
        TipoC.LongLong => TipoC.ULongLong,
        _ => t,
    };

    /// <summary>
    /// A promocao inteira (C11 6.3.1.1): o que e menor que int vira int --
    /// ou unsigned int, quando o int nao cabe tudo. No AVR e o caso do
    /// unsigned short: 16 bits sem sinal nao cabem num int de 16.
    /// </summary>
    public static TipoC Promover(this ModeloC m, TipoC t)
    {
        switch (t)
        {
            case TipoC.Bool: case TipoC.Char: case TipoC.SChar: case TipoC.UChar: case TipoC.Short:
                return TipoC.Int;
            case TipoC.UShort:
                return m.BitsDoInt > 16 ? TipoC.Int : TipoC.UInt;
            default:
                return t;
        }
    }

    /// <summary>
    /// As conversoes aritmeticas usuais (C11 6.3.1.8): o tipo em que a conta
    /// de dois operandos e feita. E daqui que sai o "-1 &lt; 1u" falso numa
    /// placa de 32 bits -- o -1 vira 4294967295.
    /// </summary>
    public static TipoC Comum(this ModeloC m, TipoC a, TipoC b)
    {
        if (a == TipoC.LongDouble || b == TipoC.LongDouble) return TipoC.LongDouble;
        if (a == TipoC.Double || b == TipoC.Double) return TipoC.Double;
        if (a == TipoC.Float || b == TipoC.Float) return TipoC.Float;

        a = m.Promover(a);
        b = m.Promover(b);
        if (a == b) return a;

        bool sa = m.ComSinal(a), sb = m.ComSinal(b);
        if (sa == sb) return Posto(a) >= Posto(b) ? a : b;

        var (u, s) = sa ? (b, a) : (a, b);
        if (Posto(u) >= Posto(s)) return u;
        if (m.Bits(s) > m.Bits(u)) return s;
        return SemSinal(s);
    }

    /// <summary>O inteiro dobrado para caber em tantos bits, com ou sem sinal.</summary>
    public static long Envolver(long x, int bits, bool sinal)
    {
        if (bits >= 64) return x;
        long mascara = (1L << bits) - 1;
        x &= mascara;
        if (sinal && (x & (1L << (bits - 1))) != 0) x -= 1L << bits;
        return x;
    }

    /// <summary>
    /// Um numero convertido para o tipo, como a atribuicao do C faz: inteiro
    /// dobra (modulo 2^bits), real vira inteiro truncando, e o float perde as
    /// casas que nao cabem nos seus 24 bits de mantissa.
    ///
    /// <paramref name="deReal"/>: o valor vem de um float ou double. Fora da
    /// faixa do inteiro, ai ele satura -- e o que o conversor do chip faz
    /// (trunc.s do Xtensa, vcvt do ARM); inteiro para inteiro dobra.
    /// </summary>
    public static double Converter(this ModeloC m, double v, TipoC t, bool deReal = false)
    {
        switch (t)
        {
            case TipoC.Nenhum:
                return v;
            case TipoC.Bool:
                return v != 0 || double.IsNaN(v) ? 1 : 0;
            case TipoC.Float:
                return (float)v;
            case TipoC.Double:
                return m.BitsDoDouble == 32 ? (float)v : v;
            case TipoC.LongDouble:
                return m.BitsDoLongDouble == 32 ? (float)v : v;
        }

        int bits = m.Bits(t);
        bool sinal = m.ComSinal(t);

        if (double.IsNaN(v)) return 0;
        double truncado = Math.Truncate(v);

        // Real fora da faixa do inteiro: o conversor do chip satura. E
        // comportamento indefinido no C, mas e o que a placa faz.
        if (deReal || v != truncado || double.IsInfinity(v))
        {
            double menor = sinal ? -Math.Pow(2, bits - 1) : 0;
            double maior = sinal ? Math.Pow(2, bits - 1) - 1 : Math.Pow(2, bits) - 1;
            if (truncado < menor) return menor;
            if (truncado > maior) return maior;
            return truncado;
        }

        const double DoisA64 = 18446744073709551616.0;
        if (bits >= 64)
        {
            double r = truncado % DoisA64;
            if (sinal)
            {
                if (r >= 9.2233720368547758E18) r -= DoisA64;
                if (r < -9.2233720368547758E18) r += DoisA64;
                return r;
            }
            return r < 0 ? r + DoisA64 : r;
        }

        long inteiro;
        if (Math.Abs(truncado) < 9.2233720368547758E18) inteiro = (long)truncado;
        else
        {
            double r = truncado % DoisA64;
            if (r < 0) r += DoisA64;
            inteiro = unchecked((long)(ulong)r);
        }
        return Envolver(inteiro, bits, sinal);
    }

    /// <summary>
    /// O valor guardado num lugar do tipo <paramref name="t"/>: a atribuicao
    /// do C. Texto, objeto e vetor passam como estao.
    /// </summary>
    public static Valor ParaOTipo(this ModeloC m, Valor v, TipoC t)
    {
        if (t == TipoC.Nenhum || m is null || v.EhTexto || v.Referencia is not null) return v;
        bool deReal = EhReal(v.Tipo) || (v.Tipo == TipoC.Nenhum && !v.Inteiro);
        return Valor.DeC(m.Converter(v.Num, t, deReal), t);
    }

    /// <summary>O maior e o menor valor de um tipo inteiro nesta placa.</summary>
    public static (double Min, double Max) Faixa(this ModeloC m, TipoC t)
    {
        int bits = m.Bits(t);
        return m.ComSinal(t)
            ? (-Math.Pow(2, bits - 1), Math.Pow(2, bits - 1) - 1)
            : (0, Math.Pow(2, bits) - 1);
    }

    /// <summary>
    /// O tipo de um numero escrito no codigo (C11 6.4.4.1): decimal sem
    /// sufixo e o primeiro de int, long, long long em que ele cabe; hexa e
    /// octal podem virar sem sinal no caminho; U, L, LL e F mandam.
    ///
    /// E por isso que 40000 e long no Uno e int na ESP32 -- e "40000 * 2"
    /// nao estoura em nenhum dos dois.
    /// </summary>
    public static TipoC DoLiteral(this ModeloC m, double valor, string texto, bool inteiro)
    {
        texto ??= "";
        if (!inteiro)
        {
            char ultimo = texto.Length > 0 ? char.ToLowerInvariant(texto[^1]) : ' ';
            return ultimo == 'f' ? TipoC.Float : ultimo == 'l' ? TipoC.LongDouble : TipoC.Double;
        }

        // Os sufixos no fim: u, l, ul, lu, ll, ull...
        int fim = texto.Length;
        while (fim > 0 && "uUlL".Contains(texto[fim - 1])) fim--;
        string sufixo = texto[fim..].ToLowerInvariant();
        bool u = sufixo.Contains('u');
        int ls = sufixo.Count(c => c == 'l');
        bool decimalSemSufixoU = !(texto.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                                   || texto.StartsWith("0b", StringComparison.OrdinalIgnoreCase)
                                   || (texto.Length > 1 && texto[0] == '0' && fim > 1));

        IEnumerable<TipoC> candidatos = (u, ls, decimalSemSufixoU) switch
        {
            (true, 0, _) => [TipoC.UInt, TipoC.ULong, TipoC.ULongLong],
            (true, 1, _) => [TipoC.ULong, TipoC.ULongLong],
            (true, _, _) => [TipoC.ULongLong],
            (false, 0, true) => [TipoC.Int, TipoC.Long, TipoC.LongLong],
            (false, 0, false) => [TipoC.Int, TipoC.UInt, TipoC.Long, TipoC.ULong, TipoC.LongLong, TipoC.ULongLong],
            (false, 1, true) => [TipoC.Long, TipoC.LongLong],
            (false, 1, false) => [TipoC.Long, TipoC.ULong, TipoC.LongLong, TipoC.ULongLong],
            (false, _, true) => [TipoC.LongLong],
            _ => [TipoC.LongLong, TipoC.ULongLong],
        };

        foreach (var t in candidatos)
            if (valor <= m.Faixa(t).Max) return t;
        return TipoC.ULongLong;
    }

    /// <summary>Como o GCC escreve o tipo nas mensagens: "long unsigned int", "double".</summary>
    public static string NomeNoCompilador(this ModeloC m, TipoC t) => t switch
    {
        TipoC.Bool => "bool",
        TipoC.Char => "char",
        TipoC.SChar => "signed char",
        TipoC.UChar => "unsigned char",
        TipoC.Short => "short int",
        TipoC.UShort => "short unsigned int",
        TipoC.Int => "int",
        TipoC.UInt => "unsigned int",
        TipoC.Long => "long int",
        TipoC.ULong => "long unsigned int",
        TipoC.LongLong => "long long int",
        TipoC.ULongLong => "long long unsigned int",
        TipoC.Float => "float",
        TipoC.Double => "double",
        TipoC.LongDouble => "long double",
        _ => "?",
    };
}
