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
                    if (dado) Gravar(bytes[i]);
                    else Comando(bytes[i]);
                break;
            }
            if (i >= bytes.Length) break;
            if (dado) Gravar(bytes[i++]);
            else Comando(bytes[i++]);
        }
        Mostrar();
    }

    void Gravar(byte valor)
    {
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
    }

    void Comando(byte c)
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
            case 0xA0: segmentosTrocados = false; return;
            case 0xA1: segmentosTrocados = true; return;
            case 0xA4: tudoAceso = false; return;
            case 0xA5: tudoAceso = true; return;
            case 0xA6: invertido = false; return;
            case 0xA7: invertido = true; return;
            case 0xAE: ligado = false; return;
            case 0xAF: ligado = true; return;
            case 0xC0: linhasTrocadas = false; return;
            case 0xC8: linhasTrocadas = true; return;
        }
        if (c <= 0x0F) coluna = Math.Min((coluna & 0xF0) | c, colunas - 1);
        else if (c <= 0x1F) coluna = Math.Min((coluna & 0x0F) | ((c & 0x0F) << 4), colunas - 1);
        else if (c is >= 0x40 and <= 0x7F) linhaInicial = c & 0x3F;
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
                break;
        }
        comandoAberto = -1;
    }

    /// <summary>A memoria no vidro: o contrario do que os comandos de espelho pedem e como o modulo e montado.</summary>
    void Mostrar()
    {
        var painel = dona.CriarPainelOled(largura, altura, Origem?.Numero ?? 0, Endereco);
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
}
