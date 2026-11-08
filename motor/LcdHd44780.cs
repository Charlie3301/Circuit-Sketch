namespace Simulador;

/// <summary>
/// O controlador HD44780 do LCD de caracteres: os comandos e os dados que o
/// programa manda (o clear, o set DDRAM address, o caractere da CGRAM...)
/// viram o que aparece no vidro. Quem fala com ele por bytes de verdade e o
/// modulo I2C (o PCF8574, <see cref="LcdPcf8574"/>) e o LCD paralelo do C#
/// (o LcdInterface.CreateGpio do nanoFramework).
/// </summary>
public sealed class ControladorHd44780(Func<Lcd> lcd, int colunas, int linhas)
{
    readonly byte[] cgram = new byte[64];
    int endereco;
    bool naCgram, incrementa = true, duasLinhas = true;

    /// <summary>Liga com 8 bits, como o chip acaba de ligar; o function set com DL=0 passa para 4.</summary>
    public bool OitoBits { get; private set; } = true;

    Lcd Vidro()
    {
        var l = lcd();
        if (!l.Ligado || l.Colunas != colunas || l.Linhas != linhas) l.Begin(colunas, linhas);
        return l;
    }

    public void Comando(byte c)
    {
        if (c >= 0x80)
        {
            naCgram = false;
            endereco = c & 0x7F;
        }
        else if (c >= 0x40)
        {
            naCgram = true;
            endereco = c & 0x3F;
        }
        else if (c >= 0x20)
        {
            OitoBits = (c & 0x10) != 0;
            duasLinhas = (c & 0x08) != 0;
            Vidro();
        }
        else if (c >= 0x10)
        {
            // O cursor anda (S/C = 0); o deslocamento da tela inteira o vidro do simulador nao faz.
            if ((c & 0x08) == 0) endereco = Math.Clamp(endereco + ((c & 0x04) != 0 ? 1 : -1), 0, 0x67);
        }
        else if (c >= 0x08) Vidro().Mostrar((c & 0x04) != 0);
        else if (c >= 0x04) incrementa = (c & 0x02) != 0;
        else if (c >= 0x02)
        {
            naCgram = false;
            endereco = 0;
        }
        else if (c == 0x01)
        {
            naCgram = false;
            endereco = 0;
            incrementa = true;
            Vidro().Limpar();
        }
    }

    public void Dado(byte d)
    {
        if (naCgram)
        {
            cgram[endereco & 0x3F] = (byte)(d & 0x1F);
            int glifo = (endereco & 0x3F) >> 3;
            Vidro().DefinirGlifo(glifo, [.. cgram.Skip(glifo * 8).Take(8).Select(b => (int)b)]);
            endereco = (endereco + (incrementa ? 1 : -1)) & 0x3F;
            return;
        }
        var (col, lin) = Posicao(endereco);
        if (col >= 0)
        {
            var l = Vidro();
            l.PosicionarCursor(col, lin);
            l.Escrever(((char)d).ToString());
        }
        endereco = Math.Clamp(endereco + (incrementa ? 1 : -1), 0, 0x67);
    }

    /// <summary>
    /// O endereco da DDRAM no vidro: a 2a linha comeca em 0x40, e nos de 4 linhas a 3a e a 4a
    /// continuam a 1a e a 2a (0x14 e 0x54 no 20x4). Fora do vidro: -1.
    /// </summary>
    (int Col, int Lin) Posicao(int a)
    {
        int metade = a >= 0x40 ? 1 : 0, dentro = a - metade * 0x40;
        if (!duasLinhas || linhas == 1) return a < colunas ? (a, 0) : (-1, 0);
        int lin = metade, col = dentro;
        if (linhas >= 4 && dentro >= colunas)
        {
            lin += 2;
            col -= colunas;
        }
        return col >= 0 && col < colunas && lin < linhas ? (col, lin) : (-1, 0);
    }

    /// <summary>A luz de fundo do modulo (o P3 do PCF8574).</summary>
    public void Luz(bool acesa)
    {
        var l = lcd();
        if (l.LuzDeFundo != acesa) l.Luz(acesa);
    }
}

/// <summary>
/// O LCD com o modulo I2C de verdade: o PCF8574 liga o P0 no RS, o P1 no RW,
/// o P2 no E, o P3 na luz de fundo e o P4 a P7 no D4 a D7. O HD44780 le o
/// meio byte na descida do E. Assim o driver que manda os bytes crus (o do
/// nanoFramework, o LiquidCrystal_PCF8574, o hd44780) escreve no vidro.
/// </summary>
public sealed class LcdPcf8574 : DispositivoI2c
{
    readonly ControladorHd44780 chip;
    int anterior;
    int? metadeAlta;

    public LcdPcf8574(Maquina dona, int endereco, string nome, int colunas, int linhas) : base(endereco, nome)
        => chip = new ControladorHd44780(dona.CriarLcd, colunas, linhas);

    public override void Receber(byte[] bytes)
    {
        base.Receber(bytes);
        foreach (byte b in bytes)
        {
            chip.Luz((b & 0x08) != 0);
            if ((anterior & 0x04) != 0 && (b & 0x04) == 0) Latch(anterior);
            anterior = b;
        }
    }

    void Latch(int pinos)
    {
        if ((pinos & 0x02) != 0) return;
        bool dado = (pinos & 0x01) != 0;
        int meio = pinos & 0xF0;
        if (chip.OitoBits)
        {
            // Com 8 bits o D0 a D3 ficam no ar (no modulo eles nem estao ligados): le zero.
            metadeAlta = null;
            Entregar(dado, (byte)meio);
            return;
        }
        if (metadeAlta is not { } alta)
        {
            metadeAlta = meio;
            return;
        }
        metadeAlta = null;
        Entregar(dado, (byte)(alta | meio >> 4));
    }

    void Entregar(bool dado, byte valor)
    {
        if (dado) chip.Dado(valor);
        else chip.Comando(valor);
    }
}
