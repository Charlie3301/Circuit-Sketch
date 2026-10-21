namespace Simulador;

/// <summary>
/// Os mostradores de LED com chip (Alpha 25): o TM1637 de 4 digitos (a
/// TM1637Display, de Avishay Orpaz) e a matriz 8 x 8 do MAX7219 (a
/// LedControl, de Eberhard Fahle). Como o resto, simulados no nivel da
/// biblioteca: o que o sketch chama e o que acende, sem o protocolo de dois
/// fios do TM1637 nem o SPI do MAX7219 bit a bit.
///
/// As contas sao as das bibliotecas: o showNumberDec() alinha a direita,
/// apaga os zeros da esquerda (ou nao, com leading_zero), poe o "-" no lugar
/// do primeiro zero apagado e trabalha com 16 bits (o 70000 vira 4464); o
/// showNumberDecEx() acende os pontos pelos bits de "dots", do 0x80 (o digito
/// 0) para baixo -- o 0x40 sao os dois pontos do relogio.
/// </summary>
public partial class Interpretador
{
    // o encodeDigit() da TM1637Display: 0 a 9 e A a F
    static readonly byte[] SegmentosDoDigito = [0x3F, 0x06, 0x5B, 0x4F, 0x66, 0x6D, 0x7D, 0x07, 0x7F, 0x6F, 0x77, 0x7C, 0x39, 0x5E, 0x79, 0x71];

    // a charTable da LedControl (o modo de 7 segmentos: DP, A, ..., G do bit 7 ao 0)
    static readonly byte[] CaracteresDaLedControl =
        [0x7E, 0x30, 0x6D, 0x79, 0x33, 0x5B, 0x5F, 0x70, 0x7F, 0x7B, 0x77, 0x1F, 0x0D, 0x3D, 0x4F, 0x47];

    /// <summary>"TM1637Display display(CLK, DIO);": o mostrador desses dois pinos.</summary>
    void MontarTm1637(Objeto obj, List<Valor> args)
    {
        int clk = args.Count > 0 ? (int)args[0].Num : -1, dio = args.Count > 1 ? (int)args[1].Num : -1;
        var mostrador = new Tm1637();
        lock (maq.Mostradores) maq.Mostradores[(clk, dio)] = mostrador;
        obj.Rede = mostrador;
        obj.Pinos = [clk, dio];
    }

    /// <summary>"LedControl lc = LedControl(DIN, CLK, CS, quantos);": a cadeia desses tres pinos.</summary>
    void MontarMax7219(Objeto obj, List<Valor> args)
    {
        int Arg(int k, int padrao) => k < args.Count ? (int)args[k].Num : padrao;
        var matriz = new Max7219(Arg(3, 1));
        lock (maq.Matrizes) maq.Matrizes[(Arg(0, -1), Arg(1, -1), Arg(2, -1))] = matriz;
        obj.Rede = matriz;
        obj.Pinos = [Arg(0, -1), Arg(1, -1), Arg(2, -1)];
    }

    Valor ChamarTm1637(Objeto obj, string metodo, Chamada c, Ambiente amb)
    {
        var m = (Tm1637)obj.Rede;
        int N = c.Args.Count;
        long A(int i, long padrao = 0) => i < N ? (long)Avaliar(c.Args[i], amb).Num : padrao;
        bool B(int i) => i < N && Avaliar(c.Args[i], amb).Verdadeiro;

        void Numero(int @base, long numero, int pontos, bool zeros, int tamanho, int posicao)
        {
            tamanho = Math.Clamp(tamanho, 1, 4);
            bool negativo = numero < 0;
            int num = (ushort)Math.Abs(numero);
            var digitos = new byte[4];
            if (num == 0 && !zeros)
            {
                digitos[tamanho - 1] = SegmentosDoDigito[0];
            }
            else
            {
                for (int i = tamanho - 1; i >= 0; i--)
                {
                    int digito = num % @base;
                    digitos[i] = digito == 0 && num == 0 && !zeros ? (byte)0 : SegmentosDoDigito[digito];
                    if (digito == 0 && num == 0 && negativo)
                    {
                        digitos[i] = 0x40;
                        negativo = false;
                    }
                    num /= @base;
                }
            }
            for (int i = 0; i < 4; i++, pontos <<= 1) digitos[i] |= (byte)(pontos & 0x80);
            for (int k = 0; k < tamanho; k++) m.Definir(posicao + k, digitos[k]);
        }

        switch (metodo)
        {
            case "setBrightness":
                m.Luz((int)A(0), N < 2 || B(1));
                return Valor.Vazio;
            case "setSegments":
            {
                if (N < 1 || !VetorDoArgumento(c.Args[0], amb, out var segmentos))
                    throw new ErroDeExecucao("display.setSegments(dados): os dados tem que ser um vetor de bytes", c.Linha);
                int tamanho = (int)A(1, 4), posicao = (int)A(2, 0);
                for (int k = 0; k < tamanho && k < segmentos.Length; k++) m.Definir(posicao + k, (byte)(long)segmentos[k].Num);
                return Valor.Vazio;
            }
            case "clear":
                for (int k = 0; k < 4; k++) m.Definir(k, 0);
                return Valor.Vazio;
            case "showNumberDec":
                Numero(10, A(0), 0, B(1), (int)A(2, 4), (int)A(3, 0));
                return Valor.Vazio;
            case "showNumberDecEx":
                Numero(10, A(0), (int)A(1), B(2), (int)A(3, 4), (int)A(4, 0));
                return Valor.Vazio;
            case "showNumberHexEx":
                Numero(16, (ushort)A(0), (int)A(1), B(2), (int)A(3, 4), (int)A(4, 0));
                return Valor.Vazio;
            case "encodeDigit":
                return Valor.DeInt(SegmentosDoDigito[A(0) & 15]);
            default:
                throw new ErroDeExecucao($"TM1637Display.{metodo}() ainda nao e suportado", c.Linha);
        }
    }

    Valor ChamarMax7219(Objeto obj, string metodo, Chamada c, Ambiente amb)
    {
        var m = (Max7219)obj.Rede;
        int N = c.Args.Count;
        long A(int i, long padrao = 0) => i < N ? (long)Avaliar(c.Args[i], amb).Num : padrao;
        bool B(int i) => i < N && Avaliar(c.Args[i], amb).Verdadeiro;
        int addr = (int)A(0);
        if (metodo != "getDeviceCount" && (addr < 0 || addr >= m.Quantidade)) return Valor.Vazio;   // a LedControl ignora

        void Ponto(int linha, int coluna, bool aceso)
        {
            if (linha is < 0 or > 7 || coluna is < 0 or > 7) return;
            byte bit = (byte)(0x80 >> coluna);
            m.Linhas[addr][linha] = aceso ? (byte)(m.Linhas[addr][linha] | bit) : (byte)(m.Linhas[addr][linha] & ~bit);
        }

        switch (metodo)
        {
            case "getDeviceCount": return Valor.DeInt(m.Quantidade);
            case "shutdown": m.Desligado[addr] = B(1); break;
            case "setScanLimit": m.Varredura[addr] = (int)Math.Clamp(A(1), 0, 7); break;
            case "setIntensity": m.Brilho[addr] = (int)Math.Clamp(A(1), 0, 15); break;
            case "clearDisplay": Array.Clear(m.Linhas[addr]); break;
            case "setLed": Ponto((int)A(1), (int)A(2), B(3)); break;
            case "setRow":
                if (A(1) is >= 0 and <= 7) m.Linhas[addr][A(1)] = (byte)A(2);
                break;
            case "setColumn":
                for (int linha = 0; linha < 8; linha++) Ponto(linha, (int)A(1), (A(2) >> (7 - linha) & 1) != 0);
                break;
            // O modo de 7 segmentos: o digito vira a linha, e os segmentos as colunas.
            case "setDigit":
                if (A(1) is >= 0 and <= 7) m.Linhas[addr][A(1)] = (byte)(CaracteresDaLedControl[A(2) & 15] | (B(3) ? 0x80 : 0));
                break;
            case "setChar":
            {
                if (A(1) is < 0 or > 7) break;
                char ch = (char)A(2);
                int v = ch switch
                {
                    >= '0' and <= '9' => CaracteresDaLedControl[ch - '0'],
                    >= 'a' and <= 'f' => CaracteresDaLedControl[ch - 'a' + 10],
                    >= 'A' and <= 'F' => CaracteresDaLedControl[ch - 'A' + 10],
                    '-' => 0x01, '_' => 0x08, 'H' or 'h' => 0x37, 'L' or 'l' => 0x0E, 'P' or 'p' => 0x67, '.' => 0x80,
                    _ => 0,
                };
                m.Linhas[addr][A(1)] = (byte)(v | (B(3) ? 0x80 : 0));
                break;
            }
            default:
                throw new ErroDeExecucao($"LedControl.{metodo}() ainda nao e suportado", c.Linha);
        }
        m.Mudou();
        return Valor.Vazio;
    }
}
