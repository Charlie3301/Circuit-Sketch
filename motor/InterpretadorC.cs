namespace Simulador;

/// <summary>
/// A conta do C com os tipos da placa.
///
/// No C++ do Arduino e no C puro, cada numero anda com o tipo que o
/// compilador lhe daria (<see cref="TipoC"/>): a variavel com o tipo da
/// declaracao, o numero escrito no codigo com o do sufixo e do tamanho, e o
/// que o core devolve com o tipo da funcao dele (millis() e unsigned long).
/// A conta de dois numeros segue as conversoes usuais e o resultado dobra
/// nos bits do tipo -- no Uno, "int x = 32767; x + 1" da -32768, como na
/// placa. As outras linguagens seguem com a conta de antes.
/// </summary>
public partial class Interpretador
{
    ModeloC modeloC;

    /// <summary>O C desta placa: int de 16 bits no AVR, char sem sinal no Xtensa...</summary>
    internal ModeloC ModeloC => modeloC ??= ModeloC.De(maq.Perfil);

    /// <summary>A conta segue os tipos do C: no C++ do Arduino e no C puro.</summary>
    internal bool EmC => (prog?.Linguagem ?? Linguagem.Arduino) is Linguagem.Arduino or Linguagem.C;

    void PrepararTiposC()
    {
        modeloC = null;
        global.Modelo = EmC ? ModeloC : null;
    }

    /// <summary>Um numero escrito no codigo, com o tipo que o C lhe da nesta placa.</summary>
    /// <summary>
    /// Cada literal do codigo ja com o tipo do C. O tipo sai do texto ("10UL",
    /// "3.5f", o tamanho do numero) e nao muda; descobri-lo a cada leitura
    /// criava textos e listas a cada volta do loop.
    /// </summary>
    readonly Dictionary<Literal, Valor> literaisC = new(ReferenceEqualityComparer.Instance);

    Valor LiteralC(Literal l)
    {
        if (literaisC.TryGetValue(l, out var pronto)) return pronto;
        var t = l.Caractere ? TipoC.Char : ModeloC.DoLiteral(l.Valor, l.Texto, l.Inteiro);
        var v = Valor.DeC(ModeloC.Converter(l.Valor, t), t);
        literaisC[l] = v;
        return v;
    }

    /// <summary>Um numero ja com o tipo do C que a funcao do core devolve.</summary>
    Valor DoTipo(double n, TipoC t) => EmC ? Valor.DeC(ModeloC.Converter(n, t), t) : Num(n, !TiposC.EhReal(t));

    /// <summary>
    /// O tipo de um operando. O numero sem tipo -- o que alguma funcao do
    /// simulador devolveu -- conta como long: largo o bastante para nao
    /// estourar no lugar errado.
    /// </summary>
    TipoC TipoDoOperando(Valor v, TipoC outro)
    {
        if (v.Tipo != TipoC.Nenhum) return v.Tipo;
        if (v.Caractere) return TipoC.Char;
        if (!v.Inteiro) return TipoC.Double;
        if (TiposC.EhInteiro(outro) && ModeloC.Bits(outro) > 32) return outro;
        return Math.Abs(v.Num) <= 2147483647 ? TipoC.Long : TipoC.LongLong;
    }

    static bool EhNumeroDeC(Valor v) => !v.EhTexto && v.Referencia is null;

    /// <summary>
    /// A conta de dois numeros como o compilador da placa a faz: os dois vao
    /// para o tipo comum (as conversoes usuais), a conta e feita nele e o
    /// resultado dobra para caber nos bits dele.
    /// </summary>
    Valor OperarC(string op, Valor x, Valor y, int linha)
    {
        var m = ModeloC;
        TipoC tx = TipoDoOperando(x, y.Tipo), ty = TipoDoOperando(y, x.Tipo);

        // Deslocamento: o tipo e o do operando da esquerda, promovido -- e
        // nao o comum. "1 << 20" no Uno e int, e da zero.
        if (op is "<<" or ">>")
        {
            if (TiposC.EhReal(tx) || TiposC.EhReal(ty)) throw OperandosInvalidos(op, tx, ty, linha);
            var tp = m.Promover(tx);
            CobrarOperacao(op, tp);
            long a = ComoLong(m.Converter(x.Num, tp));
            long n = (long)y.Num;
            int bits = m.Bits(tp);
            long r;
            if (n < 0 || n >= bits) r = op == "<<" || !m.ComSinal(tp) || a >= 0 ? 0 : -1;
            else if (op == "<<") r = unchecked(a << (int)n);
            else r = tp == TipoC.ULongLong ? unchecked((long)((ulong)a >> (int)n)) : a >> (int)n;
            return Inteiro(r, tp);
        }

        var tc = m.Comum(tx, ty);
        CobrarOperacao(op, tc);

        if (TiposC.EhReal(tc))
        {
            double a = m.Converter(x.Num, tc), b = m.Converter(y.Num, tc);
            switch (op)
            {
                case "+": return Valor.DeC(m.Converter(a + b, tc), tc);
                case "-": return Valor.DeC(m.Converter(a - b, tc), tc);
                case "*": return Valor.DeC(m.Converter(a * b, tc), tc);
                // Real dividido por zero e infinito (ou nan), sem excecao nenhuma:
                // e o IEEE 754 de todas as placas. O print() escreve "inf".
                case "/": return Valor.DeC(m.Converter(a / b, tc), tc);
                case "==": return Valor.DeC(a == b ? 1 : 0, TipoC.Bool);
                case "!=": return Valor.DeC(a != b ? 1 : 0, TipoC.Bool);
                case "<": return Valor.DeC(a < b ? 1 : 0, TipoC.Bool);
                case ">": return Valor.DeC(a > b ? 1 : 0, TipoC.Bool);
                case "<=": return Valor.DeC(a <= b ? 1 : 0, TipoC.Bool);
                case ">=": return Valor.DeC(a >= b ? 1 : 0, TipoC.Bool);
                case "%": case "&": case "|": case "^":
                    throw OperandosInvalidos(op, tx, ty, linha);
                default: throw new ErroDeExecucao(Idioma.T("operador '{0}' nao suportado", op), linha);
            }
        }

        bool u64 = tc == TipoC.ULongLong;
        long la = ComoLong(m.Converter(x.Num, tc)), lb = ComoLong(m.Converter(y.Num, tc));
        long res;
        switch (op)
        {
            case "+": res = unchecked(la + lb); break;
            case "-": res = unchecked(la - lb); break;
            case "*": res = unchecked(la * lb); break;
            case "/":
                if (lb == 0) return DividirPorZero(op, la, tc, linha);
                if (u64) res = unchecked((long)((ulong)la / (ulong)lb));
                else res = la == long.MinValue && lb == -1 ? la : la / lb;
                break;
            case "%":
                if (lb == 0) return DividirPorZero(op, la, tc, linha);
                if (u64) res = unchecked((long)((ulong)la % (ulong)lb));
                else res = lb == -1 ? 0 : la % lb;
                break;
            case "&": res = la & lb; break;
            case "|": res = la | lb; break;
            case "^": res = la ^ lb; break;
            case "==": return Valor.DeC(la == lb ? 1 : 0, TipoC.Bool);
            case "!=": return Valor.DeC(la != lb ? 1 : 0, TipoC.Bool);
            case "<": return Valor.DeC((u64 ? (ulong)la < (ulong)lb : la < lb) ? 1 : 0, TipoC.Bool);
            case ">": return Valor.DeC((u64 ? (ulong)la > (ulong)lb : la > lb) ? 1 : 0, TipoC.Bool);
            case "<=": return Valor.DeC((u64 ? (ulong)la <= (ulong)lb : la <= lb) ? 1 : 0, TipoC.Bool);
            case ">=": return Valor.DeC((u64 ? (ulong)la >= (ulong)lb : la >= lb) ? 1 : 0, TipoC.Bool);
            default: throw new ErroDeExecucao(Idioma.T("operador '{0}' nao suportado", op), linha);
        }
        return Inteiro(res, tc);
    }

    /// <summary>O numero (ja inteiro, ja no tipo) como long; o unsigned de 64 bits dobra.</summary>
    static long ComoLong(double v)
        => v >= 9.2233720368547758E18 ? unchecked((long)(ulong)v) : (long)v;

    /// <summary>
    /// O resultado inteiro de uma conta, dobrado nos bits do tipo -- no
    /// dominio do inteiro, sem passar por double, que perderia os bits de
    /// baixo acima de 2^53 (e sao eles que ficam depois de dobrar).
    /// </summary>
    Valor Inteiro(long r, TipoC t)
    {
        if (t == TipoC.ULongLong) return Valor.DeC(unchecked((ulong)r), t);
        if (t == TipoC.LongLong) return Valor.DeC(r, t);
        return Valor.DeC(TiposC.Envolver(r, ModeloC.Bits(t), ModeloC.ComSinal(t)), t);
    }

    ErroDeExecucao OperandosInvalidos(string op, TipoC a, TipoC b, int linha)
        => new(Idioma.T("invalid operands of types '{0}' and '{1}' to binary 'operator{2}': o compilador recusa {3}", ModeloC.NomeNoCompilador(a), ModeloC.NomeNoCompilador(b), op, (op == "%" ? Idioma.T("o '%' com real -- ele e so para inteiros (para real existe o fmod()).")
                            : Idioma.T("o '{0}' com real -- ele e so para inteiros.", op))), linha);

    /// <summary>
    /// "-x", "~x" e "+x": o operando e promovido (um byte vira int) e o
    /// resultado dobra no tipo promovido -- "-u" de um unsigned da o
    /// complemento, e nao um numero negativo.
    /// </summary>
    Valor UnariaC(string op, Valor x, int linha)
    {
        var m = ModeloC;
        var t = TipoDoOperando(x, TipoC.Nenhum);
        if (TiposC.EhReal(t))
        {
            if (op == "~")
                throw new ErroDeExecucao(Idioma.T("wrong type argument to bit-complement: o '~' do C e so para inteiros, e este valor e {0}.", m.NomeNoCompilador(t)), linha);
            return Valor.DeC(m.Converter(op == "-" ? -x.Num : x.Num, t), t);
        }
        var p = m.Promover(t);
        CobrarOperacao("+", p);
        long v = ComoLong(m.Converter(x.Num, p));
        long r = op switch { "-" => unchecked(-v), "~" => ~v, _ => v };
        return Inteiro(r, p);
    }

    /// <summary>
    /// "x++" e "x--" no tipo de x: o byte 255 mais um da 0; o float 0,5 mais
    /// um da 1,5 (antes virava o inteiro 1).
    /// </summary>
    Valor SomarUm(Valor atual, int passo)
    {
        if (!EmC || !EhNumeroDeC(atual)) return Valor.DeInt(atual.Num + passo);
        var t = TipoDoOperando(atual, TipoC.Nenhum);
        return Valor.DeC(ModeloC.Converter(atual.Num + passo, t), t);
    }

    /// <summary>
    /// O valor guardado num lugar que ja existe -- item de vetor, campo de
    /// struct: vai para o tipo do que estava la. O vetor de char continua de
    /// char ("msg[0] = 65" guarda 'A').
    /// </summary>
    Valor NoLugar(Valor atual, Valor novo)
    {
        if (atual.Caractere && novo.Referencia is null) novo = Valor.ComoCaractere(novo);
        if (atual.Tipo != TipoC.Nenhum && EmC) return ModeloC.ParaOTipo(novo, atual.Tipo);
        return novo;
    }

    /// <summary>O zero de um tipo: o valor de uma variavel ou de um item sem inicio.</summary>
    Valor ZeroDe(string tipo)
    {
        var zero = EhCaractere(tipo) ? Valor.DeCaractere(0) : Valor.DeInt(0);
        return EmC ? Coagir(zero, tipo) : zero;
    }

    /// <summary>
    /// A divisao inteira por zero, como cada chip termina: o Xtensa da ESP32
    /// (e o do ESP8266) para com uma excecao e a placa reinicia; o AVR, o
    /// RISC-V e o RP2040 devolvem o quociente com todos os bits em 1; o
    /// Cortex-M da STM32 devolve zero. Em nenhum deles o programa para com
    /// uma mensagem de erro -- e o simulador tambem nao pode parar.
    /// </summary>
    Valor DividirPorZero(string op, long dividendo, TipoC t, int linha)
    {
        var m = ModeloC;
        switch (m.DivisaoPorZero)
        {
            case DivisaoPorZero.Trava:
                TravarPorDivisao(linha);
                return Valor.Vazio;

            case DivisaoPorZero.Zero:
                return Valor.DeC(op == "%" ? m.Converter(dividendo, t) : 0, t);

            default:
                if (op == "%") return Valor.DeC(m.Converter(dividendo, t), t);
                // O divisor do AVR (e o do RP2040) inverte o sinal pelo do
                // dividendo; o RISC-V da -1 sempre.
                double q = !m.ComSinal(t) ? m.Faixa(t).Max
                         : m == ModeloC.RiscV || dividendo >= 0 ? -1 : 1;
                return Valor.DeC(q, t);
        }
    }

    /// <summary>
    /// A excecao do Xtensa: o monitor mostra o que a placa mostraria, o
    /// console explica, e a placa reinicia.
    /// </summary>
    void TravarPorDivisao(int linha)
    {
        bool esp8266 = maq.Perfil?.Processador == "ESP8266";
        if (esp8266)
        {
            // O ROM do ESP8266 executa uma instrucao ilegal na divisao por
            // zero, e o core a mostra como a excecao 6 (IntegerDivideByZero).
            Escrever("", true);
            Escrever("--------------- CUT HERE FOR EXCEPTION DECODER ---------------", true);
            Escrever("", true);
            Escrever("Exception (6):", true);
            Escrever("epc1=0x4000dce5 epc2=0x00000000 epc3=0x00000000 excvaddr=0x00000000 depc=0x00000000", true);
            Escrever("", true);
            Escrever(">>>stack>>>", true);
            Escrever("<<<stack<<<", true);
            Escrever("", true);
            Escrever("--------------- CUT HERE FOR EXCEPTION DECODER ---------------", true);
        }
        else
        {
            int nucleo = (maq.Perfil?.Nucleos ?? 1) > 1 ? 1 : 0;
            Escrever($"Guru Meditation Error: Core  {nucleo} panic'ed (IntegerDivideByZero). Exception was unhandled.", true);
            Escrever("", true);
            Escrever("Rebooting...", true);
        }
        maq.Avisar(Idioma.T("{0}: divisao inteira por zero. No {1} isso e uma excecao do processador, e a placa reinicia -- o mesmo acontece na bancada.", Origens.Onde(linha), maq.Perfil?.Processador));
        throw Reiniciar(ReinicioDaPlaca.PeloPanico, 0, 0);
    }

    // =====================================================================
    //  as funcoes da math.h e as macros do Arduino.h, com o tipo delas
    // =====================================================================

    /// <summary>
    /// round(), min(), max(), abs(), constrain(), sq(): no AVR sao macros do
    /// Arduino.h (e o round devolve long); na ESP32 vem do std:: -- round
    /// devolve double, e min/max exigem os dois argumentos do MESMO tipo.
    /// Null: nao e uma delas, ou a linguagem nao e C.
    /// </summary>
    Valor? MatematicaC(string nome, List<Valor> args, int linha)
    {
        if (!EmC || args.Count == 0 || !args.All(EhNumeroDeC)) return null;
        var m = ModeloC;
        TipoC T(int i) => TipoDoOperando(args[i], TipoC.Nenhum);
        double A(int i) => i < args.Count ? args[i].Num : 0;

        switch (nome)
        {
            case "floor": return Real(Math.Floor(A(0)), TipoC.Double);
            case "ceil": return Real(Math.Ceiling(A(0)), TipoC.Double);
            case "trunc": return Real(Math.Truncate(A(0)), TipoC.Double);
            case "round":
                return m.RoundDaMacro
                    ? Valor.DeC(m.Converter(A(0) >= 0 ? A(0) + 0.5 : A(0) - 0.5, TipoC.Long, deReal: true), TipoC.Long)
                    : Real(Math.Round(A(0), MidpointRounding.AwayFromZero), TipoC.Double);

            case "abs":
            {
                var t = T(0);
                var r = TiposC.EhReal(t) ? t : m.Promover(t);
                return Valor.DeC(m.Converter(Math.Abs(m.Converter(A(0), r)), r), r);
            }

            case "min":
            case "max":
            {
                if (args.Count < 2) return null;
                TipoC ta = T(0), tb = T(1);
                if (m.MinMaxDoStd && args[0].Tipo != TipoC.Nenhum && args[1].Tipo != TipoC.Nenhum && ta != tb)
                    throw new ErroDeExecucao(
                        Idioma.T("no matching function for call to '{0}({1}, {2})': no core da {3} o {4}() e o std::{5}, que exige os dois do MESMO tipo -- nao compila. Converta um deles, como em {6}((long)a, b).", nome, m.NomeNoCompilador(ta), m.NomeNoCompilador(tb), maq.Perfil.Processador, nome, nome, nome), linha);
                var tc = m.Comum(ta, tb);
                double a = m.Converter(A(0), tc), b = m.Converter(A(1), tc);
                return Valor.DeC(nome == "min" ? Math.Min(a, b) : Math.Max(a, b), tc);
            }

            case "constrain":
            {
                if (args.Count < 3) return null;
                var tc = m.Comum(m.Comum(T(0), T(1)), T(2));
                double v = m.Converter(A(0), tc), lo = m.Converter(A(1), tc), hi = m.Converter(A(2), tc);
                return Valor.DeC(v < lo ? lo : v > hi ? hi : v, tc);
            }

            case "sq":
            {
                var t = T(0);
                var r = TiposC.EhReal(t) ? t : m.Promover(t);
                double v = m.Converter(A(0), r);
                return Valor.DeC(m.Converter(v * v, r), r);
            }

            // long map(long, long, long, long, long): os argumentos viram
            // long ANTES da conta -- map(0.5, 0, 1, 0, 100) da 0 na placa.
            // A faixa vazia devolve -1 (o WMath da ESP32 diz isso; no AVR e
            // a divisao por zero do long, que tambem da -1).
            case "map":
            {
                if (args.Count < 5) return null;
                long L(int i) => (long)m.Converter(A(i), TipoC.Long, TiposC.EhReal(T(i)));
                long x = L(0), inMin = L(1), inMax = L(2), outMin = L(3), outMax = L(4);
                long run = TiposC.Envolver(inMax - inMin, 32, true);
                if (run == 0) return Valor.DeC(-1, TipoC.Long);
                long produto = TiposC.Envolver(unchecked(TiposC.Envolver(x - inMin, 32, true)
                                                         * TiposC.Envolver(outMax - outMin, 32, true)), 32, true);
                return Inteiro(produto / run + outMin, TipoC.Long);
            }

            case "radians": return Real(A(0) * 0.017453292519943295769236907684886, TipoC.Double);
            case "degrees": return Real(A(0) * 57.295779513082320876798154814105, TipoC.Double);
        }

        // A math.h: double nas de sempre, float nas terminadas em "f".
        bool emFloat = nome.EndsWith('f') && nome is not "erf" and not "modf" and not "frexpf";
        string raiz = emFloat ? nome[..^1] : nome;
        double? resultado = raiz switch
        {
            "sqrt" => Math.Sqrt(A(0)),
            "pow" => Math.Pow(A(0), A(1)),
            "exp" => Math.Exp(A(0)),
            "log" => Math.Log(A(0)),
            "log10" => Math.Log10(A(0)),
            "log2" => Math.Log2(A(0)),
            "sin" => Math.Sin(A(0)),
            "cos" => Math.Cos(A(0)),
            "tan" => Math.Tan(A(0)),
            "asin" => Math.Asin(A(0)),
            "acos" => Math.Acos(A(0)),
            "atan" => Math.Atan(A(0)),
            "atan2" => Math.Atan2(A(0), A(1)),
            "sinh" => Math.Sinh(A(0)),
            "cosh" => Math.Cosh(A(0)),
            "tanh" => Math.Tanh(A(0)),
            "hypot" => Math.Sqrt(A(0) * A(0) + A(1) * A(1)),
            "fmin" => Math.Min(A(0), A(1)),
            "fmax" => Math.Max(A(0), A(1)),
            "cbrt" => Math.Cbrt(A(0)),
            "copysign" => Math.CopySign(A(0), A(1)),
            "fabs" => Math.Abs(A(0)),
            // O fmod do C: o resto com o sinal do dividendo (o "%" do .NET
            // com double e exatamente isso), e nan com divisor zero.
            "fmod" => A(1) == 0 ? double.NaN : A(0) % A(1),
            "floor" => Math.Floor(A(0)),
            "ceil" => Math.Ceiling(A(0)),
            "trunc" => Math.Truncate(A(0)),
            "round" => Math.Round(A(0), MidpointRounding.AwayFromZero),
            "exp2" => Math.Pow(2, A(0)),
            "expm1" => Math.Exp(A(0)) - 1,
            "log1p" => Math.Log(1 + A(0)),
            _ => null,
        };
        if (resultado is null) return null;
        return Real(resultado.Value, emFloat ? TipoC.Float : TipoC.Double);
    }

    Valor Real(double v, TipoC t) => Valor.DeC(ModeloC.Converter(v, t), t);

    /// <summary>
    /// O tipo que o analogRead() do core devolve: int no AVR, no ESP8266 e no
    /// ArduinoCore-API; uint16_t no da ESP32; uint32_t no da STM32 -- e ai
    /// "analogRead(A0) - 2048" da negativo nunca.
    /// </summary>
    TipoC TipoDoAnalogRead => ModeloC.Nome switch
    {
        "Xtensa" or "RISC-V" => TipoC.UShort,
        "ARM" => TipoC.ULong,
        _ => TipoC.Int,
    };
}
