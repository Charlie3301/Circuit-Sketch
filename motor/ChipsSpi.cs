namespace Simulador;

/// <summary>
/// Um chip pendurado no SPI que entende os bytes do barramento, como o de
/// verdade: o controlador de um display colorido, o do OLED. E o que mostra
/// alguma coisa quando o programa fala com o chip sem passar por uma
/// biblioteca que o simulador imita -- o SPI.transfer() na mao, a TFT_eSPI, o
/// U8g2, a Adafruit instalada de verdade, o st7789.py do MicroPython.
/// </summary>
public interface IChipSpi
{
    /// <summary>Bytes inteiros que chegaram com o CS em LOW: comando (D/C em LOW) ou dado.</summary>
    void Receber(ReadOnlySpan<byte> dados, bool ehDado);

    /// <summary>O pino de reset foi a LOW.</summary>
    void Resetar();
}

/// <summary>
/// Os chips do SPI escutando os fios da placa. Nao importa quem mexe nos
/// pinos -- o SPI de hardware, o shiftOut, um digitalWrite atras do outro, o
/// SoftSPI do MicroPython --: com o CS em LOW, cada subida do SCK guarda o
/// nivel do MOSI, e a oitava fecha um byte, que e comando ou dado conforme o
/// D/C naquele instante. E o que o chip faz.
/// </summary>
public sealed class EscutaSpi
{
    sealed class Ligado
    {
        public AparelhoNoSpi Aparelho;
        public IChipSpi Chip;
        public int Palavra, Bits;
    }

    readonly Maquina maq;
    readonly List<Ligado> ligados = [];
    readonly bool[] interessa;
    readonly byte[] um = new byte[1];

    public EscutaSpi(Maquina maq, IReadOnlyList<AparelhoNoSpi> aparelhos)
    {
        this.maq = maq;
        interessa = new bool[maq.Digital.Length];
        foreach (var a in aparelhos)
        {
            IChipSpi chip = a.Tipo switch
            {
                "ili9341" => new ChipTft(maq, ChipTft.Ili9341),
                "st7789" => new ChipTft(maq, ChipTft.St7789),
                "st7735" => new ChipTft(maq, ChipTft.St7735),
                "ssd1306" => new OledSpi(maq),
                _ => null,
            };
            if (chip is null) continue;
            ligados.Add(new Ligado { Aparelho = a, Chip = chip });

            // Sem o MOSI e o SCK chegando numa placa, nada entra pelos fios (o reset ainda vale).
            foreach (int p in new[] { a.Cs, a.Rst, a.Mosi >= 0 ? a.Sck : -1 })
                if (p >= 0 && p < interessa.Length) interessa[p] = true;
        }
    }

    public bool Vazia => ligados.Count == 0;

    /// <summary>O pino e de algum chip? (a pergunta barata, feita a cada mudanca de pino)</summary>
    public bool Interessa(int pino) => interessa[pino];

    /// <summary>O chip de um aparelho, para quem entrega os bytes prontos (o spi_master do ESP-IDF).</summary>
    public IChipSpi ChipDe(AparelhoNoSpi aparelho)
    {
        foreach (var l in ligados)
            if (ReferenceEquals(l.Aparelho, aparelho) || l.Aparelho == aparelho) return l.Chip;
        return null;
    }

    /// <summary>Um pino da placa mudou de nivel.</summary>
    public void Borda(int pino, int nivel)
    {
        lock (ligados)
            foreach (var l in ligados)
            {
                var a = l.Aparelho;
                if (pino == a.Rst && nivel == 0)
                {
                    l.Bits = l.Palavra = 0;
                    lock (l.Chip) l.Chip.Resetar();
                }
                if (pino == a.Cs) l.Bits = l.Palavra = 0;   // o CS e que marca o comeco do byte
                if (pino != a.Sck || nivel == 0 || a.Mosi < 0) continue;
                if (a.Cs >= 0 && maq.Digital[a.Cs] != 0) continue;

                l.Palavra = (l.Palavra << 1) | (maq.Digital[a.Mosi] & 1);
                if (++l.Bits < 8) continue;
                um[0] = (byte)l.Palavra;
                l.Bits = l.Palavra = 0;
                bool dado = a.Dc < 0 || maq.Digital[a.Dc] != 0;
                lock (l.Chip) l.Chip.Receber(um, dado);
            }
    }

    /// <summary>
    /// Um bloco de bytes inteiros do SPI de hardware, direto para os chips
    /// que estao nestes fios com o CS em LOW -- sem bater os oito bits de
    /// cada um nos pinos, que e o que deixa uma tela inteira (150 mil bytes)
    /// andar no tempo do barramento. Falso: ninguem esta ouvindo (ou um byte
    /// ficou pela metade), e quem chamou manda bit a bit.
    /// </summary>
    public bool Entregar(int sck, int mosi, ReadOnlySpan<byte> dados)
    {
        lock (ligados)
        {
            bool alguem = false;
            foreach (var l in ligados)
            {
                if (!Ouve(l, sck, mosi)) continue;
                if (l.Bits != 0) return false;
                alguem = true;
            }
            if (!alguem) return false;
            foreach (var l in ligados)
            {
                if (!Ouve(l, sck, mosi)) continue;
                var a = l.Aparelho;
                bool dado = a.Dc < 0 || maq.Digital[a.Dc] != 0;
                lock (l.Chip) l.Chip.Receber(dados, dado);
            }
            return true;
        }
    }

    bool Ouve(Ligado l, int sck, int mosi)
    {
        var a = l.Aparelho;
        return a.Sck == sck && a.Mosi == mosi && (a.Cs < 0 || maq.Digital[a.Cs] == 0);
    }

    /// <summary>
    /// O SPI de hardware pos o SCK no nivel de repouso do modo (o begin, o
    /// beginTransaction): nao e um bit. No display sem CS essa borda entraria
    /// como o primeiro bit de um byte e desalinharia todos os seguintes.
    /// </summary>
    public void Repousou(int sck)
    {
        lock (ligados)
            foreach (var l in ligados)
                if (l.Aparelho.Sck == sck) l.Bits = l.Palavra = 0;
    }
}

/// <summary>
/// O controlador de um display colorido (ILI9341, ST7789, ST7735), byte a
/// byte: comando com o D/C em LOW, dados com ele em HIGH. CASET e RASET abrem
/// a janela, RAMWR enche de pixels (RGB565, o byte alto primeiro), MADCTL
/// gira e espelha, COLMOD escolhe 16 ou 18 bits -- os comandos sao os mesmos
/// nos tres (o padrao MIPI DCS). O que muda de um para o outro e o painel: o
/// tamanho da memoria, que pedaco dela o vidro mostra, de que lado ele e
/// colado, a ordem das cores e se e IPS.
/// </summary>
public sealed class ChipTft : IChipSpi
{
    /// <param name="Colunas">Colunas da memoria do controlador.</param>
    /// <param name="Linhas">Linhas da memoria do controlador.</param>
    /// <param name="Largura">Largura do vidro: as primeiras colunas da memoria.</param>
    /// <param name="Altura">Altura do vidro: as primeiras linhas da memoria.</param>
    /// <param name="EspelhaX">O vidro e colado com a coluna 0 na direita do modulo em pe.</param>
    /// <param name="EspelhaY">O vidro e colado com a linha 0 embaixo do modulo em pe.</param>
    /// <param name="Bgr">O filtro de cores e BGR: o MADCTL tem que pedir BGR para o vermelho sair vermelho.</param>
    /// <param name="Ips">Painel IPS: as cores saem certas com a inversao LIGADA (o INVON da partida).</param>
    public sealed record Painel(string Nome, int Colunas, int Linhas, int Largura, int Altura,
                                bool EspelhaX, bool EspelhaY, bool Bgr, bool Ips);

    // ILI9341 de 2,2": o vidro e a memoria inteira. Em pe com MADCTL = MX|BGR (a rotacao 0 das bibliotecas).
    public static readonly Painel Ili9341 = new("ILI9341", 240, 320, 240, 320, true, false, true, false);

    // ST7789 IPS 240x240: a memoria e de 240x320 e o vidro mostra as linhas 0 a 239. Em pe com
    // MADCTL = MX|MY -- e ai a janela comeca na linha 80, o deslocamento que toda biblioteca soma.
    public static readonly Painel St7789 = new("ST7789", 240, 320, 240, 240, true, true, false, true);

    // ST7735 de 1,8" 128x160 (o de etiqueta preta, o INITR_BLACKTAB): sem deslocamento, cores em RGB.
    public static readonly Painel St7735 = new("ST7735", 128, 160, 128, 160, true, true, false, false);

    readonly Maquina maq;
    readonly Painel painel;
    TelaGrafica tela;

    int comando = -1;
    readonly byte[] parametros = new byte[16];
    int nParametros;
    int xs, xe, ys, ye;
    int x, y;
    int madctl;
    bool escrevendo;
    int pendente = -1;
    int bytesPorPixel = 2;
    readonly byte[] pixel3 = new byte[3];
    int nPixel3;

    static readonly int[] Rgb565 = MontarTabela();

    static int[] MontarTabela()
    {
        var t = new int[65536];
        for (int c = 0; c < 65536; c++) t[c] = TelaGrafica.DeRgb565(c);
        return t;
    }

    public ChipTft(Maquina maq, Painel painel)
    {
        this.maq = maq;
        this.painel = painel;
        Zerar();
    }

    /// <summary>O vidro deste chip, para os testes: nulo enquanto nenhum byte chegou.</summary>
    public TelaGrafica Tela => tela;

    void Zerar()
    {
        comando = -1;
        nParametros = 0;
        xs = 0; xe = painel.Colunas - 1; ys = 0; ye = painel.Linhas - 1;
        x = y = 0;
        madctl = 0;
        escrevendo = false;
        pendente = -1;
        nPixel3 = 0;
        bytesPorPixel = 2;
    }

    /// <summary>
    /// O chip saiu do reset: dormindo, com a saida desligada e a inversao de
    /// fabrica -- no IPS, sem o INVON as cores saem trocadas.
    /// </summary>
    void EstadoDeFabricaNoVidro()
    {
        tela.Comando(0x01);
        tela.Inverter(painel.Ips);
    }

    /// <summary>
    /// O vidro so passa a ser do chip quando chega o primeiro byte. Enquanto
    /// isso -- e sempre que o programa usa a biblioteca imitada, que desenha
    /// direto na tela -- o chip nao toca em nada.
    /// </summary>
    bool Acordar()
    {
        if (maq.TelaDaBiblioteca) return false;
        if (tela is not null && ReferenceEquals(tela, maq.Tela)) return true;
        tela = maq.TelaDoChip(painel.Largura, painel.Altura);
        EstadoDeFabricaNoVidro();
        return true;
    }

    public void Resetar()
    {
        Zerar();
        if (tela is not null && ReferenceEquals(tela, maq.Tela) && !maq.TelaDaBiblioteca) EstadoDeFabricaNoVidro();
    }

    public void Receber(ReadOnlySpan<byte> dados, bool ehDado)
    {
        if (dados.Length == 0 || !Acordar()) return;
        if (!ehDado)
        {
            foreach (byte b in dados) Comando(b);
            return;
        }

        if (escrevendo)
        {
            Pixels(dados);
            return;
        }

        foreach (byte b in dados)
        {
            if (nParametros < parametros.Length) parametros[nParametros++] = b;
            Aplicar();
        }
    }

    void Comando(byte c)
    {
        comando = c;
        nParametros = 0;
        escrevendo = false;
        pendente = -1;
        nPixel3 = 0;
        switch (c)
        {
            case 0x2C:   // RAMWR
                x = xs;
                y = ys;
                escrevendo = true;
                break;
            case 0x3C:   // RAMWRC: continua de onde parou
                escrevendo = true;
                break;
            case 0x01:   // SWRESET
                Zerar();
                comando = c;
                EstadoDeFabricaNoVidro();
                break;
            case 0x10: case 0x11: case 0x28: case 0x29:
                tela.Comando(c);
                break;
            case 0x20: tela.Inverter(painel.Ips); break;     // INVOFF
            case 0x21: tela.Inverter(!painel.Ips); break;    // INVON
        }
    }

    void Aplicar()
    {
        switch (comando)
        {
            case 0x2A when nParametros == 4:   // CASET
                xs = (parametros[0] << 8) | parametros[1];
                xe = (parametros[2] << 8) | parametros[3];
                break;
            case 0x2B when nParametros == 4:   // RASET
                ys = (parametros[0] << 8) | parametros[1];
                ye = (parametros[2] << 8) | parametros[3];
                break;
            case 0x36 when nParametros == 1:   // MADCTL
                madctl = parametros[0];
                break;
            case 0x3A when nParametros == 1:   // COLMOD: 0x55 16 bits, 0x66 18 bits
                bytesPorPixel = (parametros[0] & 0x07) == 0x06 ? 3 : 2;
                break;
        }
    }

    void Pixels(ReadOnlySpan<byte> dados)
    {
        bool mudou = false;
        int i = 0;
        if (bytesPorPixel == 3)
        {
            for (; i < dados.Length; i++)
            {
                pixel3[nPixel3++] = dados[i];
                if (nPixel3 < 3) continue;
                nPixel3 = 0;
                int c565 = ((pixel3[0] >> 3) << 11) | ((pixel3[1] >> 2) << 5) | (pixel3[2] >> 3);
                Pintar(c565);
                mudou = true;
            }
        }
        else
        {
            if (pendente >= 0 && dados.Length > 0)
            {
                Pintar((pendente << 8) | dados[0]);
                pendente = -1;
                i = 1;
                mudou = true;
            }
            for (; i + 1 < dados.Length; i += 2)
            {
                Pintar((dados[i] << 8) | dados[i + 1]);
                mudou = true;
            }
            if (i < dados.Length) pendente = dados[i];
        }
        if (mudou) tela.Mudou();
    }

    void Pintar(int c565)
    {
        // Onde o endereco cai na memoria. O MV troca os eixos: a coluna pedida
        // anda pelas linhas da memoria. O MX espelha sempre as colunas da
        // memoria e o MY as linhas -- com ou sem a troca.
        bool mv = (madctl & 0x20) != 0, mx = (madctl & 0x40) != 0, my = (madctl & 0x80) != 0;
        int coluna, linha;
        if (mv) { linha = x; coluna = y; }
        else { coluna = x; linha = y; }
        if (mx) coluna = painel.Colunas - 1 - coluna;
        if (my) linha = painel.Linhas - 1 - linha;

        // E onde esse ponto da memoria aparece no vidro, com o modulo em pe.
        int px = painel.EspelhaX ? painel.Largura - 1 - coluna : coluna;
        int py = painel.EspelhaY ? painel.Altura - 1 - linha : linha;
        if ((uint)px < (uint)painel.Largura && (uint)py < (uint)painel.Altura)
        {
            int argb = Rgb565[c565 & 0xFFFF];
            if (((madctl & 0x08) != 0) != painel.Bgr)   // a ordem pedida nao e a do filtro: vermelho e azul trocam
                argb = (argb & unchecked((int)0xFF00FF00)) | ((argb >> 16) & 0xFF) | ((argb & 0xFF) << 16);
            tela.Pixels[py * painel.Largura + px] = argb;
        }
        tela.PixelsEscritos++;

        if (++x > xe)
        {
            x = xs;
            if (++y > ye) y = ys;
        }
    }
}

/// <summary>
/// O OLED de SPI (SSD1306 de 128x64, o modulo de sete ou oito pinos): o
/// mesmo chip do de I2C, com o pino D/C dizendo o que e comando.
/// </summary>
public sealed class OledSpi(Maquina maq) : IChipSpi
{
    const int Largura = 128, Altura = 64;
    readonly ChipOled chip = new(Largura, Altura, sh1106: false);
    TelaGrafica vidro;

    public void Resetar()
    {
        chip.Reiniciar();
        if (vidro is not null && !maq.OledSpiDaBiblioteca) chip.Pintar(vidro);
    }

    public void Receber(ReadOnlySpan<byte> dados, bool ehDado)
    {
        // A Adafruit_SSD1306 imitada escreve direto no painel: o chip fica de fora.
        if (dados.Length == 0 || maq.OledSpiDaBiblioteca) return;
        var painel = maq.PainelOledDoChip(Largura, Altura);
        if (!ReferenceEquals(painel, vidro))
        {
            vidro = painel;
            chip.MudouOVidro = true;
        }

        foreach (byte b in dados)
        {
            if (!ehDado)
            {
                chip.Comando(b);
                continue;
            }
            var (pag, col) = chip.Gravar(b);
            if (!chip.MudouOVidro) chip.PintarByte(vidro, pag, col);
        }
        if (chip.MudouOVidro) chip.Pintar(vidro);
        else if (ehDado) vidro.Mudou();
    }
}
