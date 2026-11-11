namespace Simulador;

/// <summary>
/// O chip do OLED (SSD1306 ou SH1106) pendurado no I2C: entende os bytes do barramento, como
/// o de verdade. E o que uma biblioteca que fala com o chip mostra (o U8g2, a Adafruit
/// real, o esp_lcd, o Wire na mao). A Adafruit_SSD1306 que o simulador imita nao passa aqui:
/// ela escreve direto no painel.
/// </summary>
public sealed class OledI2c(Maquina dona, int endereco, string nome, int largura, int altura, bool sh1106)
    : DispositivoI2c(endereco, nome)
{
    readonly ChipOled chip = new(largura, altura, sh1106);

    public override void Receber(byte[] bytes)
    {
        base.Receber(bytes);
        // O scanner de I2C so pergunta se tem alguem: nao chegou nada para o chip.
        if (bytes.Length == 0) return;
        int i = 0;
        while (i < bytes.Length)
        {
            byte controle = bytes[i++];
            bool dado = (controle & 0x40) != 0;
            if ((controle & 0x80) == 0)
            {
                // Co = 0: o resto da transmissao e todo do mesmo tipo.
                for (; i < bytes.Length; i++)
                    if (dado) chip.Gravar(bytes[i]);
                    else chip.Comando(bytes[i]);
                break;
            }
            if (i >= bytes.Length) break;
            if (dado) chip.Gravar(bytes[i++]);
            else chip.Comando(bytes[i++]);
        }
        chip.Pintar(dona.CriarPainelOled(largura, altura, Origem?.Numero ?? 0, Endereco));
    }
}

/// <summary>
/// O controlador do OLED por dentro, sem o barramento: a memoria de 8 paginas, o ponteiro
/// que anda sozinho em cada modo de enderecamento, e os comandos que mudam o que o vidro
/// mostra. O I2C e o SPI entregam os mesmos bytes aqui -- so muda quem diz o que e comando
/// e o que e dado (o byte de controle no I2C, o pino D/C no SPI).
/// </summary>
public sealed class ChipOled(int largura, int altura, bool sh1106)
{
    // O SH1106 tem 132 colunas de memoria, e o vidro de 128 comeca na coluna 2.
    readonly int colunas = sh1106 ? 132 : 128;
    readonly int deslocamento = sh1106 ? 2 : 0;
    readonly byte[] memoria = new byte[(sh1106 ? 132 : 128) * 8];

    enum Modo { Horizontal, Vertical, Pagina }
    Modo modo = Modo.Pagina;
    int coluna, pagina, colunaIni, colunaFim = (sh1106 ? 132 : 128) - 1, paginaIni, paginaFim = 7;
    int linhaInicial;
    bool ligado, invertido, tudoAceso, segmentosTrocados, linhasTrocadas;
    int contraste = 0x7F;

    // O comando que ainda espera argumentos: eles podem vir em outra transmissao.
    int comandoAberto = -1, argumentosFaltando;
    readonly List<byte> argumentos = [];

    /// <summary>
    /// Um comando mudou o jeito de o vidro mostrar a memoria (ligou, inverteu, espelhou):
    /// quem pinta byte a byte tem que pintar tudo de novo.
    /// </summary>
    public bool MudouOVidro { get; set; }

    /// <summary>O pino RES em LOW: o chip volta ao estado de fabrica. A memoria fica.</summary>
    public void Reiniciar()
    {
        modo = Modo.Pagina;
        coluna = pagina = colunaIni = paginaIni = linhaInicial = 0;
        colunaFim = colunas - 1;
        paginaFim = 7;
        ligado = invertido = tudoAceso = segmentosTrocados = linhasTrocadas = false;
        contraste = 0x7F;
        comandoAberto = -1;
        argumentos.Clear();
        MudouOVidro = true;
    }

    /// <summary>Um byte de dados: cai onde o ponteiro esta. Devolve a pagina e a coluna em que caiu.</summary>
    public (int Pagina, int Coluna) Gravar(byte valor)
    {
        var onde = (pagina, coluna);
        memoria[pagina * colunas + coluna] = valor;
        switch (modo)
        {
            case Modo.Pagina:
                coluna = coluna + 1 >= colunas ? 0 : coluna + 1;
                break;
            case Modo.Horizontal:
                if (++coluna > colunaFim)
                {
                    coluna = colunaIni;
                    pagina = pagina + 1 > paginaFim ? paginaIni : pagina + 1;
                }
                break;
            case Modo.Vertical:
                if (++pagina > paginaFim)
                {
                    pagina = paginaIni;
                    coluna = coluna + 1 > colunaFim ? colunaIni : coluna + 1;
                }
                break;
        }
        return onde;
    }

    public void Comando(byte c)
    {
        if (comandoAberto >= 0)
        {
            argumentos.Add(c);
            if (--argumentosFaltando == 0) Fechar();
            return;
        }
        switch (c)
        {
            case 0x20 or 0x81 or 0xA8 or 0xAD or 0xD3 or 0xD5 or 0xD6 or 0xD9 or 0xDA or 0xDB or 0x8D: Abrir(c, 1); return;
            case 0x21 or 0x22 or 0xA3: Abrir(c, 2); return;
            case 0x29 or 0x2A: Abrir(c, 5); return;
            case 0x26 or 0x27: Abrir(c, 6); return;
            case 0xA0: segmentosTrocados = false; MudouOVidro = true; return;
            case 0xA1: segmentosTrocados = true; MudouOVidro = true; return;
            case 0xA4: tudoAceso = false; MudouOVidro = true; return;
            case 0xA5: tudoAceso = true; MudouOVidro = true; return;
            case 0xA6: invertido = false; MudouOVidro = true; return;
            case 0xA7: invertido = true; MudouOVidro = true; return;
            case 0xAE: ligado = false; MudouOVidro = true; return;
            case 0xAF: ligado = true; MudouOVidro = true; return;
            case 0xC0: linhasTrocadas = false; MudouOVidro = true; return;
            case 0xC8: linhasTrocadas = true; MudouOVidro = true; return;
        }
        if (c <= 0x0F) coluna = Math.Min((coluna & 0xF0) | c, colunas - 1);
        else if (c <= 0x1F) coluna = Math.Min((coluna & 0x0F) | ((c & 0x0F) << 4), colunas - 1);
        else if (c is >= 0x40 and <= 0x7F) { linhaInicial = c & 0x3F; MudouOVidro = true; }
        else if (c is >= 0xB0 and <= 0xB7) pagina = c & 0x07;
    }

    void Abrir(byte c, int quantos)
    {
        comandoAberto = c;
        argumentosFaltando = quantos;
        argumentos.Clear();
    }

    void Fechar()
    {
        switch (comandoAberto)
        {
            case 0x20:
                modo = (argumentos[0] & 3) switch { 0 => Modo.Horizontal, 1 => Modo.Vertical, _ => Modo.Pagina };
                break;
            case 0x21:
                colunaIni = coluna = Math.Min(argumentos[0] & 0x7F, colunas - 1);
                colunaFim = Math.Min(argumentos[1] & 0x7F, colunas - 1);
                break;
            case 0x22:
                paginaIni = pagina = argumentos[0] & 7;
                paginaFim = argumentos[1] & 7;
                break;
            case 0x81:
                contraste = argumentos[0];
                MudouOVidro = true;
                break;
        }
        comandoAberto = -1;
    }

    /// <summary>A memoria no vidro: o contrario do que os comandos de espelho pedem e como o modulo e montado.</summary>
    public void Pintar(TelaGrafica painel)
    {
        MudouOVidro = false;
        painel.Inverter(invertido);
        painel.Escurecer(contraste == 0);
        for (int y = 0; y < altura; y++)
        {
            int linha = ((linhasTrocadas ? y : altura - 1 - y) + linhaInicial) & 63;
            int linhaDaMemoria = linha / 8 * colunas;
            int bit = 1 << (linha % 8);
            for (int x = 0; x < largura; x++)
            {
                int col = (segmentosTrocados ? x : largura - 1 - x) + deslocamento;
                bool aceso = ligado && (tudoAceso || (memoria[linhaDaMemoria + col] & bit) != 0);
                painel.Pixels[y * largura + x] = aceso ? painel.CorAcesa : TelaGrafica.CorApagada;
            }
        }
        painel.Mudou();
    }

    /// <summary>
    /// So os oito pontos de um byte da memoria, no lugar do vidro em que eles aparecem: o
    /// SPI entrega byte por byte, e pintar o vidro inteiro a cada um seria mil vezes o
    /// trabalho de um quadro.
    /// </summary>
    public void PintarByte(TelaGrafica painel, int pag, int col)
    {
        int naTela = col - deslocamento;
        int x = segmentosTrocados ? naTela : largura - 1 - naTela;
        if ((uint)x >= (uint)largura) return;
        byte valor = memoria[pag * colunas + col];
        for (int b = 0; b < 8; b++)
        {
            int t = (pag * 8 + b - linhaInicial) & 63;
            if (t >= altura) continue;
            int y = linhasTrocadas ? t : altura - 1 - t;
            bool aceso = ligado && (tudoAceso || (valor & (1 << b)) != 0);
            painel.Pixels[y * largura + x] = aceso ? painel.CorAcesa : TelaGrafica.CorApagada;
        }
    }
}
