namespace Simulador;

/// <summary>
/// Os registradores de E/S do AVR no C++: PORTx (a saida, ou o pull-up da
/// entrada), DDRx (a direcao) e PINx (o que o pino le; escrever nele troca
/// o PORTx) -- cada bit e um pino da placa, pela tabela do chip (Uno, Mega,
/// ATtiny85). E o "PORTB |= (1 &lt;&lt; PB5)" das aulas de microcontrolador, que
/// antes so rodava em Assembly. Acesso direto custa um ciclo, e nao os ~3,6
/// us do digitalWrite.
/// </summary>
public partial class Interpretador
{
    /// <summary>O latch de cada PORTx, como o chip guarda (vale mesmo nos bits de entrada: e o pull-up).</summary>
    readonly Dictionary<char, int> latchesDoAvr = [];

    static readonly System.Text.RegularExpressions.Regex RegistradorDePorta = new(@"^(PORT|DDR|PIN)([A-L])$");
    static readonly System.Text.RegularExpressions.Regex BitDePorta = new(@"^(P|PORT|DD|PIN)([A-L])([0-7])$");

    /// <summary>O pino do Arduino de cada bit da porta (-1: o bit nao sai na placa -- o cristal, o reset).</summary>
    int[] PinosDaPortaDoAvr(char porta) => (maq.Perfil?.Processador, porta) switch
    {
        ("ATmega328P", 'B') => [8, 9, 10, 11, 12, 13, -1, -1],
        ("ATmega328P", 'C') => [14, 15, 16, 17, 18, 19, -1, -1],
        ("ATmega328P", 'D') => [0, 1, 2, 3, 4, 5, 6, 7],
        ("ATtiny85", 'B') => [0, 1, 2, 3, 4, 5, -1, -1],
        ("ATmega2560", 'A') => [22, 23, 24, 25, 26, 27, 28, 29],
        ("ATmega2560", 'B') => [53, 52, 51, 50, 10, 11, 12, 13],
        ("ATmega2560", 'C') => [37, 36, 35, 34, 33, 32, 31, 30],
        ("ATmega2560", 'D') => [21, 20, 19, 18, -1, -1, -1, 38],
        ("ATmega2560", 'E') => [0, 1, -1, 5, 2, 3, -1, -1],
        ("ATmega2560", 'F') => [54, 55, 56, 57, 58, 59, 60, 61],
        ("ATmega2560", 'G') => [41, 40, 39, -1, -1, 4, -1, -1],
        ("ATmega2560", 'H') => [17, 16, -1, 6, 7, 8, 9, -1],
        ("ATmega2560", 'J') => [15, 14, -1, -1, -1, -1, -1, -1],
        ("ATmega2560", 'K') => [62, 63, 64, 65, 66, 67, 68, 69],
        ("ATmega2560", 'L') => [49, 48, 47, 46, 45, 44, 43, 42],
        _ => null,
    };

    /// <summary>"PB5", "PORTB5", "DDB5", "PIND2": o numero do bit.</summary>
    bool BitDoAvr(string nome, out Valor valor)
    {
        valor = Valor.Vazio;
        if (!EhAvr) return false;
        var m = BitDePorta.Match(nome);
        if (!m.Success || PinosDaPortaDoAvr(m.Groups[2].Value[0]) is null) return false;
        valor = Valor.DeInt(m.Groups[3].Value[0] - '0');
        return true;
    }

    int LatchDoAvr(char porta, int[] pinos)
    {
        if (latchesDoAvr.TryGetValue(porta, out int latch)) return latch;
        // Sem nada escrito ainda: o que os pinos mostram (o digitalWrite tambem mexe no latch).
        int v = 0;
        for (int bit = 0; bit < 8; bit++)
            if (pinos[bit] >= 0 && (maq.Modo[pinos[bit]] == 1 ? maq.Digital[pinos[bit]] != 0 : maq.Modo[pinos[bit]] == 2))
                v |= 1 << bit;
        return v;
    }

    /// <summary>Le PORTx, DDRx ou PINx. Falso: nao e um deles (ou a placa nao e AVR).</summary>
    bool LerRegistradorDoAvr(string nome, out Valor valor)
    {
        valor = Valor.Vazio;
        if (!EhAvr) return false;
        var m = RegistradorDePorta.Match(nome);
        if (!m.Success) return false;
        char porta = m.Groups[2].Value[0];
        if (PinosDaPortaDoAvr(porta) is not { } pinos) return false;

        int v = 0;
        for (int bit = 0; bit < 8; bit++)
        {
            int p = pinos[bit];
            if (p < 0) continue;
            bool um = m.Groups[1].Value switch
            {
                "DDR" => maq.Modo[p] == 1,
                "PIN" => maq.DigitalRead(p) != 0,
                _ => (LatchDoAvr(porta, pinos) & (1 << bit)) != 0,
            };
            if (um) v |= 1 << bit;
        }
        Cobrar(1);
        valor = Valor.DeC(v, TipoC.UChar);
        return true;
    }

    /// <summary>Escreve PORTx, DDRx ou PINx: cada bit vira o pino. Falso: nao e um deles.</summary>
    bool EscreverRegistradorDoAvr(string nome, Valor valor)
    {
        if (!EhAvr) return false;
        var m = RegistradorDePorta.Match(nome);
        if (!m.Success) return false;
        char porta = m.Groups[2].Value[0];
        if (PinosDaPortaDoAvr(porta) is not { } pinos) return false;
        int v = (int)((long)valor.Num & 0xFF);
        int latch = LatchDoAvr(porta, pinos);

        switch (m.Groups[1].Value)
        {
            case "PIN":
                // Escrever 1 no PINx troca o bit do PORTx (o ATmega328P e o 2560 fazem isso).
                AplicarPortaDoAvr(porta, pinos, latch ^ v);
                break;
            case "PORT":
                AplicarPortaDoAvr(porta, pinos, v);
                break;
            case "DDR":
                for (int bit = 0; bit < 8; bit++)
                {
                    int p = pinos[bit];
                    if (p < 0) continue;
                    bool saida = (v & (1 << bit)) != 0;
                    bool alto = (latch & (1 << bit)) != 0;
                    if (saida)
                    {
                        if (maq.Modo[p] != 1) maq.PinMode(p, 1);
                        maq.PinoDoPeriferico(p, alto ? 1 : 0, 0);
                    }
                    else if (maq.Modo[p] == 1 || maq.Modo[p] != (alto ? 2 : 0)) maq.PinMode(p, alto ? 2 : 0);
                }
                break;
        }
        Cobrar(1);
        return true;
    }

    /// <summary>O PORTx novo: nas saidas, o nivel; nas entradas, o pull-up.</summary>
    void AplicarPortaDoAvr(char porta, int[] pinos, int v)
    {
        latchesDoAvr[porta] = v;
        for (int bit = 0; bit < 8; bit++)
        {
            int p = pinos[bit];
            if (p < 0) continue;
            bool alto = (v & (1 << bit)) != 0;
            if (maq.Modo[p] == 1) maq.PinoDoPeriferico(p, alto ? 1 : 0, 0);
            else if (maq.Modo[p] != (alto ? 2 : 0)) maq.PinMode(p, alto ? 2 : 0);
        }
    }
}
