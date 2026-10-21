namespace Simulador;

/// <summary>
/// A tela do display TFT (ILI9341).
///
/// Era o último dos sete que "desenhavam e não simulavam": o sketch
/// mandava `fillScreen`, `drawRect`, `print`, e a peça continuava um
/// retângulo preto. Pior, as chamadas eram aceitas em silêncio, então
/// nada indicava que faltava alguma coisa.
///
/// **O texto também é pixel**, desenhado com a fonte 5x7 da própria
/// Adafruit_GFX. Até a Alpha 18 ele era um letreiro por cima do
/// desenho, e isso quebrava o jeito mais comum de atualizar um número:
/// "setTextColor(branco, preto); setCursor(0, 0); print(contagem);" —
/// na placa o fundo preto apaga o número de antes; aqui os letreiros se
/// empilhavam, um por cima do outro, até virar borrão. Com a letra em
/// pixel vale tudo o que vale no vidro de verdade: o fillRect apaga o
/// texto, o fundo opaco cobre o anterior, a linha comprida quebra no fim
/// da tela e a acentuação sai com os símbolos estranhos da tabela da
/// fonte. A lista <see cref="Textos"/> continua, só como registro do que
/// foi escrito e onde.
///
/// A cor entra como RGB565, que é o que a biblioteca usa, e sai como
/// ARGB de 32 bits, que é o que a tela do Windows quer.
/// </summary>
public sealed class TelaGrafica(int largura, int altura)
{
    /// <summary>
    /// O tamanho do painel, que não muda: 240x320 em pé.
    ///
    /// É nele que os pixels moram. Girar a tela não move pixel nenhum de
    /// lugar na memória -- muda só a conta que leva o (x,y) do sketch
    /// até a posição no painel.
    /// </summary>
    public int LarguraFisica { get; } = largura;
    public int AlturaFisica { get; } = altura;

    /// <summary>
    /// O tamanho que o SKETCH enxerga. Deitada, os dois trocam de lugar
    /// -- e é isso que `width()` e `height()` devolvem, como na
    /// biblioteca. Sketch que centraliza texto com `width()/2` depende
    /// disso para não escrever fora da tela.
    /// </summary>
    public int Largura => Rotacao % 2 == 0 ? LarguraFisica : AlturaFisica;
    public int Altura => Rotacao % 2 == 0 ? AlturaFisica : LarguraFisica;

    /// <summary>Um pixel por posição, em 0xAARRGGBB.</summary>
    public int[] Pixels { get; } = new int[largura * altura];

    /// <summary>
    /// O OLED (SSD1306): um bit por ponto, e a "cor" que o sketch passa e
    /// 0 (BLACK, apaga), 1 (WHITE, acende) ou 2 (INVERSE, troca o ponto) --
    /// as cores da Adafruit_SSD1306, e nao RGB565.
    /// </summary>
    public bool Monocromatica { get; init; }

    /// <summary>O ponto aceso do OLED: o branco frio dos modulos comuns.</summary>
    public int CorAcesa { get; init; } = unchecked((int)0xFFEAF4FF);

    /// <summary>O ponto apagado do OLED: o preto do vidro.</summary>
    public const int CorApagada = unchecked((int)0xFF07080B);

    /// <summary>Sobe a cada mudança: o editor só redesenha quando muda.</summary>
    public int Versao { get; private set; }

    /// <summary>Pixels que o controlador escreveu desde o comeco: os quadros por segundo saem dele.</summary>
    public long PixelsEscritos { get; set; }

    // ---- estado que a biblioteca guarda entre chamadas ----
    public int CursorX { get; private set; }
    public int CursorY { get; private set; }
    public int CorDoTexto { get; private set; } = unchecked((int)0xFFFFFFFF);
    public int TamanhoDoTexto { get; private set; } = 1;
    public int Rotacao { get; private set; }

    // A cor e o fundo em RGB565, como a biblioteca guarda. Fundo igual a
    // cor e o "transparente": a letra so pinta os pontos dela.
    int corDaLetra = 0xFFFF, fundoDaLetra = 0xFFFF;
    int tamanhoX = 1, tamanhoY = 1;

    /// <summary>setTextWrap: a linha comprida desce no fim da tela (ligado por padrao).</summary>
    public bool Quebrar { get; set; } = true;

    /// <summary>invertDisplay(true): o vidro mostra as cores invertidas.</summary>
    public bool Invertida { get; private set; }

    /// <summary>
    /// O vidro mostra a memoria? DISPOFF e SLPIN desligam a saida: com a
    /// luz de fundo acesa o painel fica branco, e a imagem continua
    /// guardada para quando voltar.
    /// </summary>
    public bool Mostrando { get; private set; } = true;

    /// <summary>Uma coisa escrita na tela, com onde e como.</summary>
    /// <param name="Rotacao">
    /// Como a tela estava quando isto foi escrito. O texto é desenhado
    /// por cima, com fonte de verdade, então quem mostra precisa saber
    /// para que lado virá-lo.
    /// </param>
    public sealed record Escrito(int X, int Y, string Texto, int Cor, int Tamanho,
                                 int Rotacao);

    readonly List<Escrito> textos = [];

    public IReadOnlyList<Escrito> Textos => textos;

    // ---------------- conversões ----------------

    /// <summary>
    /// RGB565 -> ARGB. É o formato que o sketch escreve (`0xF800` é
    /// vermelho), e é dele que saem as cores nomeadas da biblioteca.
    /// </summary>
    public static int DeRgb565(int cor565)
    {
        int r = (cor565 >> 11) & 0x1F;
        int g = (cor565 >> 5) & 0x3F;
        int b = cor565 & 0x1F;

        // 5 e 6 bits para 8: repete os bits de cima, senão o branco
        // (0xFFFF) sairia 248,252,248 em vez de branco.
        return unchecked((int)0xFF000000)
               | ((r * 255 / 31) << 16)
               | ((g * 255 / 63) << 8)
               | (b * 255 / 31);
    }

    // ---------------- desenho ----------------

    public void Limpar(int cor565)
    {
        int argb = Monocromatica ? ((cor565 & 0xFFFF) == 0 ? CorApagada : CorAcesa) : DeRgb565(cor565);
        if (Monocromatica && (cor565 & 0xFFFF) == 2)
            for (int i = 0; i < Pixels.Length; i++) Pixels[i] = Pixels[i] == CorAcesa ? CorApagada : CorAcesa;
        else Array.Fill(Pixels, argb);
        textos.Clear();
        Versao++;
    }

    public void Pixel(int x, int y, int cor565)
    {
        Pinta(x, y, cor565);
        Versao++;
    }

    /// <summary>
    /// Os pixels do painel mudaram por fora -- o controlador do ILI9341 do
    /// modo nativo escreve direto em <see cref="Pixels"/>, ja nas coordenadas
    /// do painel. Sobe a versao para o editor redesenhar.
    /// </summary>
    public void Mudou() => Versao++;

    /// <summary>
    /// Onde, no painel, cai o ponto que o sketch pediu.
    ///
    /// É a única conta que a rotação muda. Fazê-la aqui, na escrita,
    /// mantém o painel sempre em pé: quem mostra a tela não precisa
    /// saber que ela foi girada, e o texto -- que é desenhado por cima,
    /// com fonte de verdade -- gira junto pela mesma conta.
    /// </summary>
    public (int X, int Y) NoPainel(int x, int y) => Rotacao switch
    {
        1 => (LarguraFisica - 1 - y, x),
        2 => (LarguraFisica - 1 - x, AlturaFisica - 1 - y),
        3 => (y, AlturaFisica - 1 - x),
        _ => (x, y),
    };

    public void Retangulo(int x, int y, int larg, int alt, int cor565, bool cheio)
    {
        if (cheio)
        {
            // Como o fillRect da Adafruit_SPITFT: tamanho zero nao pinta
            // nada, e o negativo conta para o outro lado.
            if (larg == 0 || alt == 0) return;
            if (larg < 0) { x += larg + 1; larg = -larg; }
            if (alt < 0) { y += alt + 1; alt = -alt; }
            for (int j = 0; j < alt; j++)
                for (int i = 0; i < larg; i++)
                    Pinta(x + i, y + j, cor565);
        }
        else
        {
            for (int i = 0; i < larg; i++)
            {
                Pinta(x + i, y, cor565);
                Pinta(x + i, y + alt - 1, cor565);
            }
            for (int j = 0; j < alt; j++)
            {
                Pinta(x, y + j, cor565);
                Pinta(x + larg - 1, y + j, cor565);
            }
        }
        Versao++;
    }

    /// <summary>Linha por Bresenham: sem ponto flutuante e sem buraco.</summary>
    public void Linha(int x0, int y0, int x1, int y1, int cor565)
    {
        int dx = Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
        int dy = -Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
        int erro = dx + dy;

        while (true)
        {
            Pinta(x0, y0, cor565);
            if (x0 == x1 && y0 == y1) break;

            int e2 = 2 * erro;
            if (e2 >= dy) { erro += dy; x0 += sx; }
            if (e2 <= dx) { erro += dx; y0 += sy; }
        }
        Versao++;
    }

    /// <summary>Círculo pelo método do ponto médio, o mesmo da biblioteca.</summary>
    public void Circulo(int cx, int cy, int raio, int cor565, bool cheio)
    {
        int x = raio, y = 0, erro = 1 - raio;

        while (x >= y)
        {
            if (cheio)
            {
                for (int i = cx - x; i <= cx + x; i++)
                { Pinta(i, cy + y, cor565); Pinta(i, cy - y, cor565); }
                for (int i = cx - y; i <= cx + y; i++)
                { Pinta(i, cy + x, cor565); Pinta(i, cy - x, cor565); }
            }
            else
            {
                Pinta(cx + x, cy + y, cor565); Pinta(cx - x, cy + y, cor565);
                Pinta(cx + x, cy - y, cor565); Pinta(cx - x, cy - y, cor565);
                Pinta(cx + y, cy + x, cor565); Pinta(cx - y, cy + x, cor565);
                Pinta(cx + y, cy - x, cor565); Pinta(cx - y, cy - x, cor565);
            }

            y++;
            if (erro < 0) erro += 2 * y + 1;
            else { x--; erro += 2 * (y - x) + 1; }
        }
        Versao++;
    }

    void Pinta(int x, int y, int cor565)
    {
        // O recorte é no tamanho que o SKETCH vê: desenhar em (300, 10)
        // com a tela deitada é legítimo, e seria cortado se a conta
        // usasse o painel.
        if (x < 0 || y < 0 || x >= Largura || y >= Altura) return;

        var (px, py) = NoPainel(x, y);
        if (px < 0 || py < 0 || px >= LarguraFisica || py >= AlturaFisica) return;

        int k = py * LarguraFisica + px;
        if (Monocromatica)
            Pixels[k] = (cor565 & 0xFFFF) switch
            {
                0 => CorApagada,
                2 => Pixels[k] == CorAcesa ? CorApagada : CorAcesa,
                _ => CorAcesa,
            };
        else Pixels[k] = DeRgb565(cor565);
    }

    // ---------------- o painel do OLED ----------------

    /// <summary>O display() do SSD1306: o buffer inteiro vai para o vidro.</summary>
    public void CopiarDe(TelaGrafica buffer)
    {
        int n = Math.Min(Pixels.Length, buffer.Pixels.Length);
        if (buffer.LarguraFisica == LarguraFisica) Array.Copy(buffer.Pixels, Pixels, n);
        else
            for (int y = 0; y < Math.Min(AlturaFisica, buffer.AlturaFisica); y++)
                Array.Copy(buffer.Pixels, y * buffer.LarguraFisica, Pixels, y * LarguraFisica,
                           Math.Min(LarguraFisica, buffer.LarguraFisica));
        PixelsEscritos += n;
        Versao++;
    }

    /// <summary>O dim() do SSD1306: o contraste baixo deixa o desenho mais apagado.</summary>
    public bool Escurecido { get; private set; }

    public void Escurecer(bool escuro)
    {
        Escurecido = escuro;
        Versao++;
    }

    // A rolagem do SSD1306: o proprio painel anda, uma coluna a cada 5
    // quadros (~48 ms), so nas paginas (faixas de 8 linhas) pedidas.
    int rolagem, rolagemIni, rolagemFim;
    double rolagemDesdeMs;
    const double MsPorColuna = 48;

    /// <summary>O painel esta rolando sozinho: quem mostra tem que redesenhar mesmo sem mudanca.</summary>
    public bool Rolando => rolagem != 0;

    /// <summary>startscrollright/left (1/-1) e startscrolldiagright/left (2/-2), das paginas ini a fim.</summary>
    public void Rolar(int direcao, int paginaIni, int paginaFim, double agoraMs)
    {
        FixarRolagem(agoraMs);
        rolagem = direcao;
        rolagemIni = Math.Clamp(paginaIni, 0, 7);
        rolagemFim = Math.Clamp(paginaFim, rolagemIni, 7);
        rolagemDesdeMs = agoraMs;
        Versao++;
    }

    /// <summary>
    /// O stopscroll(): o painel para onde estava -- o SSD1306 deixa a
    /// memoria deslocada, e o datasheet manda reescrever (o display()).
    /// </summary>
    public void PararRolagem(double agoraMs)
    {
        FixarRolagem(agoraMs);
        rolagem = 0;
        Versao++;
    }

    void FixarRolagem(double agoraMs)
    {
        if (rolagem == 0) return;
        var visto = QuadroVisivel(agoraMs);
        Array.Copy(visto, Pixels, Pixels.Length);
        rolagemDesdeMs = agoraMs;
    }

    /// <summary>O que o vidro mostra agora: os pixels, com a rolagem do painel aplicada.</summary>
    public int[] QuadroVisivel(double agoraMs)
    {
        if (rolagem == 0) return Pixels;
        int passos = (int)Math.Max(0, (agoraMs - rolagemDesdeMs) / MsPorColuna);
        int l = LarguraFisica, a = AlturaFisica;
        var saida = (int[])Pixels.Clone();
        int dx = ((rolagem > 0 ? passos : -passos) % l + l) % l;
        for (int y = rolagemIni * 8; y < Math.Min(a, (rolagemFim + 1) * 8); y++)
            for (int x = 0; x < l; x++)
                saida[y * l + (x + dx) % l] = Pixels[y * l + x];
        if (Math.Abs(rolagem) == 2)
        {
            // A diagonal tambem sobe uma linha a cada passo, no quadro inteiro.
            var vertical = (int[])saida.Clone();
            int dy = passos % a;
            for (int y = 0; y < a; y++)
                Array.Copy(saida, y * l, vertical, ((y - dy + a) % a) * l, l);
            return vertical;
        }
        return saida;
    }

    // ---------------- texto ----------------

    public void PosicionarCursor(int x, int y)
    {
        CursorX = x;
        CursorY = y;
    }

    /// <summary>setTextColor(cor): so a letra, sem fundo.</summary>
    public void DefinirCorDoTexto(int cor565) => DefinirCorDoTexto(cor565, cor565);

    /// <summary>setTextColor(cor, fundo): o fundo pinta a caixa inteira da letra.</summary>
    public void DefinirCorDoTexto(int cor565, int fundo565)
    {
        corDaLetra = cor565 & 0xFFFF;
        fundoDaLetra = fundo565 & 0xFFFF;
        CorDoTexto = DeRgb565(corDaLetra);
    }

    public void DefinirTamanhoDoTexto(int tamanho) => DefinirTamanhoDoTexto(tamanho, tamanho);

    public void DefinirTamanhoDoTexto(int x, int y)
    {
        // A biblioteca troca o zero por 1; o teto e so para um numero
        // absurdo nao pintar a tela mil vezes.
        tamanhoX = Math.Clamp(x, 1, 64);
        tamanhoY = Math.Clamp(y, 1, 64);
        TamanhoDoTexto = tamanhoX;
    }

    /// <summary>
    /// Gira a tela. Muda o que `width()`/`height()` devolvem e para onde
    /// vão os pontos daqui em diante -- o que já está desenhado fica
    /// onde está, como no display de verdade.
    /// </summary>
    public void DefinirRotacao(int r)
    {
        Rotacao = ((r % 4) + 4) % 4;
        Versao++;
    }

    public void Inverter(bool invertida)
    {
        Invertida = invertida;
        Versao++;
    }

    public void MostrarImagem(bool mostrando)
    {
        Mostrando = mostrando;
        Versao++;
    }

    bool displayLigado = true, dormindo;

    /// <summary>
    /// Um comando escrito direto no controlador (sendCommand): os que
    /// mudam o que o vidro mostra. SLPIN e DISPOFF apagam a saida sem
    /// perder a memoria; SLPOUT e DISPON a trazem de volta.
    /// </summary>
    public void Comando(int comando)
    {
        switch (comando & 0xFF)
        {
            case 0x01: dormindo = true; displayLigado = false; break;   // SWRESET
            case 0x10: dormindo = true; break;                           // SLPIN
            case 0x11: dormindo = false; break;                          // SLPOUT
            case 0x28: displayLigado = false; break;                     // DISPOFF
            case 0x29: displayLigado = true; break;                      // DISPON
            case 0x20: Inverter(false); return;                          // INVOFF
            case 0x21: Inverter(true); return;                           // INVON
            default: return;
        }
        MostrarImagem(displayLigado && !dormindo);
    }

    /// <summary>
    /// begin() e init(): o controlador acorda, liga a saida e sai da
    /// inversao -- e a sequencia de partida da biblioteca.
    /// </summary>
    public void Iniciar()
    {
        dormindo = false;
        displayLigado = true;
        Invertida = false;
        MostrarImagem(true);
    }

    /// <summary>
    /// print e println: letra por letra, com a fonte da biblioteca. O
    /// texto vira os BYTES que a placa mandaria -- "ç" sao dois, e cada
    /// um cai num simbolo da tabela da fonte, como no vidro de verdade.
    /// </summary>
    public void Escrever(string texto, bool novaLinha)
    {
        if (!string.IsNullOrEmpty(texto))
        {
            var (px, py) = NoPainel(CursorX, CursorY);
            textos.Add(new Escrito(px, py, texto, CorDoTexto, TamanhoDoTexto, Rotacao));
            if (textos.Count > 200) textos.RemoveAt(0);

            foreach (byte b in TextoArduino.ParaBytes(texto)) EscreverByte(b);
        }

        // O println da Print manda "\r\n"; a biblioteca ignora o \r.
        if (novaLinha) EscreverByte((byte)'\n');
        Versao++;
    }

    /// <summary>O write(c) da Adafruit_GFX com a fonte classica.</summary>
    public void EscreverByte(byte c)
    {
        if (c == '\n')
        {
            CursorX = 0;
            CursorY += tamanhoY * 8;
            return;
        }
        if (c == '\r') return;

        if (Quebrar && CursorX + tamanhoX * 6 > Largura)
        {
            CursorX = 0;
            CursorY += tamanhoY * 8;
        }
        DesenharLetra(CursorX, CursorY, c, corDaLetra, fundoDaLetra, tamanhoX, tamanhoY);
        CursorX += tamanhoX * 6;
        Versao++;
    }

    /// <summary>As 5 colunas de um caractere da fonte classica (o bit de baixo em cima): o LCD de caracteres usa a mesma.</summary>
    public static ReadOnlySpan<byte> ColunasDaLetra(int c) => Fonte.AsSpan((c & 0xFF) * 5, 5);

    /// <summary>
    /// O drawChar da biblioteca: 5 colunas de 8 pontos, e a sexta coluna
    /// e o espaco entre letras -- que so e pintado com fundo opaco.
    /// </summary>
    public void DesenharLetra(int x, int y, int c, int cor565, int fundo565, int tamX, int tamY)
    {
        if (x >= Largura || y >= Altura || x + 6 * tamX - 1 < 0 || y + 8 * tamY - 1 < 0) return;

        c &= 0xFF;
        bool opaco = fundo565 != cor565;
        for (int i = 0; i < 5; i++)
        {
            int indice = c * 5 + i;
            int linha = indice < Fonte.Length ? Fonte[indice] : 0;
            for (int j = 0; j < 8; j++, linha >>= 1)
            {
                if ((linha & 1) != 0) Bloco(x + i * tamX, y + j * tamY, tamX, tamY, cor565);
                else if (opaco) Bloco(x + i * tamX, y + j * tamY, tamX, tamY, fundo565);
            }
        }
        if (opaco) Bloco(x + 5 * tamX, y, tamX, 8 * tamY, fundo565);
        Versao++;
    }

    void Bloco(int x, int y, int larg, int alt, int cor565)
    {
        for (int j = 0; j < alt; j++)
            for (int i = 0; i < larg; i++)
                Pinta(x + i, y + j, cor565);
    }

    /// <summary>
    /// getTextBounds da fonte classica: a caixa que o texto ocuparia a
    /// partir de (x, y), com a mesma quebra de linha do print.
    /// </summary>
    public (int X, int Y, int Largura, int Altura) MedirTexto(string texto, int x, int y)
    {
        int cx = x, cy = y, minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
        foreach (byte c in TextoArduino.ParaBytes(texto ?? ""))
        {
            if (c == '\n') { cx = 0; cy += tamanhoY * 8; continue; }
            if (c == '\r') continue;
            if (Quebrar && cx + tamanhoX * 6 > Largura) { cx = 0; cy += tamanhoY * 8; }
            minX = Math.Min(minX, cx);
            minY = Math.Min(minY, cy);
            maxX = Math.Max(maxX, cx + tamanhoX * 6 - 1);
            maxY = Math.Max(maxY, cy + tamanhoY * 8 - 1);
            cx += tamanhoX * 6;
        }
        return maxX < minX ? (x, y, 0, 0) : (minX, minY, maxX - minX + 1, maxY - minY + 1);
    }

    // ---------------- janela de escrita ----------------
    //
    // setAddrWindow + writePixels/pushColor: o jeito rapido de mandar uma
    // imagem inteira. Os pontos entram linha por linha dentro da janela.

    int janelaX, janelaY, janelaLarg = 1, janelaAlt = 1, janelaPonto;

    public void DefinirJanela(int x, int y, int larg, int alt)
    {
        janelaX = x;
        janelaY = y;
        janelaLarg = Math.Max(1, larg);
        janelaAlt = Math.Max(1, alt);
        janelaPonto = 0;
    }

    public void EmpurrarCor(int cor565)
    {
        int total = janelaLarg * janelaAlt;
        int k = janelaPonto % total;
        Pinta(janelaX + k % janelaLarg, janelaY + k / janelaLarg, cor565);
        janelaPonto = (janelaPonto + 1) % total;
        Versao++;
    }

    // ---------------- figuras da Adafruit_GFX ----------------

    public void Triangulo(int x0, int y0, int x1, int y1, int x2, int y2, int cor565, bool cheio)
    {
        if (!cheio)
        {
            Linha(x0, y0, x1, y1, cor565);
            Linha(x1, y1, x2, y2, cor565);
            Linha(x2, y2, x0, y0, cor565);
            return;
        }

        // O fillTriangle da biblioteca: ordena por y e enche linha a linha.
        if (y0 > y1) { (y0, y1) = (y1, y0); (x0, x1) = (x1, x0); }
        if (y1 > y2) { (y2, y1) = (y1, y2); (x2, x1) = (x1, x2); }
        if (y0 > y1) { (y0, y1) = (y1, y0); (x0, x1) = (x1, x0); }

        if (y0 == y2)
        {
            int a = Math.Min(x0, Math.Min(x1, x2)), b = Math.Max(x0, Math.Max(x1, x2));
            Retangulo(a, y0, b - a + 1, 1, cor565, true);
            return;
        }

        int dx01 = x1 - x0, dy01 = y1 - y0, dx02 = x2 - x0, dy02 = y2 - y0,
            dx12 = x2 - x1, dy12 = y2 - y1;
        int sa = 0, sb = 0;
        int ultima = y1 == y2 ? y1 : y1 - 1;
        int yy;
        for (yy = y0; yy <= ultima; yy++)
        {
            int a = x0 + sa / dy01, b = x0 + sb / dy02;
            sa += dx01;
            sb += dx02;
            if (a > b) (a, b) = (b, a);
            Retangulo(a, yy, b - a + 1, 1, cor565, true);
        }
        sa = dx12 * (yy - y1);
        sb = dx02 * (yy - y0);
        for (; yy <= y2; yy++)
        {
            int a = x1 + sa / dy12, b = x0 + sb / dy02;
            sa += dx12;
            sb += dx02;
            if (a > b) (a, b) = (b, a);
            Retangulo(a, yy, b - a + 1, 1, cor565, true);
        }
        Versao++;
    }

    public void RetanguloRedondo(int x, int y, int larg, int alt, int raio, int cor565, bool cheio)
    {
        int maximo = Math.Min(larg, alt) / 2;
        raio = Math.Clamp(raio, 0, maximo);
        if (raio == 0) { Retangulo(x, y, larg, alt, cor565, cheio); return; }

        if (cheio) Retangulo(x + raio, y, larg - 2 * raio, alt, cor565, true);
        else
        {
            Retangulo(x + raio, y, larg - 2 * raio, 1, cor565, true);
            Retangulo(x + raio, y + alt - 1, larg - 2 * raio, 1, cor565, true);
            Retangulo(x, y + raio, 1, alt - 2 * raio, cor565, true);
            Retangulo(x + larg - 1, y + raio, 1, alt - 2 * raio, cor565, true);
        }

        // Os quatro cantos: um quarto de circulo em cada.
        int f = 1 - raio, ddx = 1, ddy = -2 * raio, px = 0, py = raio;
        int esq = x + raio, dir = x + larg - raio - 1, cima = y + raio, baixo = y + alt - raio - 1;
        while (px < py)
        {
            if (f >= 0) { py--; ddy += 2; f += ddy; }
            px++;
            ddx += 2;
            f += ddx;
            if (cheio)
            {
                Retangulo(esq - py, cima - px, 1, baixo - cima + 2 * px + 1, cor565, true);
                Retangulo(dir + py, cima - px, 1, baixo - cima + 2 * px + 1, cor565, true);
                Retangulo(esq - px, cima - py, 1, baixo - cima + 2 * py + 1, cor565, true);
                Retangulo(dir + px, cima - py, 1, baixo - cima + 2 * py + 1, cor565, true);
            }
            else
            {
                Pinta(dir + px, baixo + py, cor565); Pinta(dir + py, baixo + px, cor565);
                Pinta(esq - px, baixo + py, cor565); Pinta(esq - py, baixo + px, cor565);
                Pinta(dir + px, cima - py, cor565); Pinta(dir + py, cima - px, cor565);
                Pinta(esq - px, cima - py, cor565); Pinta(esq - py, cima - px, cor565);
            }
        }
        if (cheio)
        {
            Retangulo(esq - raio, cima, 1, baixo - cima + 1, cor565, true);
            Retangulo(dir + raio, cima, 1, baixo - cima + 1, cor565, true);
        }
        Versao++;
    }

    /// <summary>
    /// A fonte 5x7 da Adafruit_GFX (glcdfont.c, licenca BSD -- ver
    /// LICENSE-adafruit-gfx.txt), a mesma que o display de verdade usa
    /// sem setFont(). Cinco bytes por caractere, uma coluna por byte, o
    /// bit de baixo em cima. E a tabela "classica", de 255 simbolos.
    /// </summary>
    static readonly byte[] Fonte =
    [
        0x00, 0x00, 0x00, 0x00, 0x00,   // 0
        0x3E, 0x5B, 0x4F, 0x5B, 0x3E,   // 1
        0x3E, 0x6B, 0x4F, 0x6B, 0x3E,   // 2
        0x1C, 0x3E, 0x7C, 0x3E, 0x1C,   // 3
        0x18, 0x3C, 0x7E, 0x3C, 0x18,   // 4
        0x1C, 0x57, 0x7D, 0x57, 0x1C,   // 5
        0x1C, 0x5E, 0x7F, 0x5E, 0x1C,   // 6
        0x00, 0x18, 0x3C, 0x18, 0x00,   // 7
        0xFF, 0xE7, 0xC3, 0xE7, 0xFF,   // 8
        0x00, 0x18, 0x24, 0x18, 0x00,   // 9
        0xFF, 0xE7, 0xDB, 0xE7, 0xFF,   // 10
        0x30, 0x48, 0x3A, 0x06, 0x0E,   // 11
        0x26, 0x29, 0x79, 0x29, 0x26,   // 12
        0x40, 0x7F, 0x05, 0x05, 0x07,   // 13
        0x40, 0x7F, 0x05, 0x25, 0x3F,   // 14
        0x5A, 0x3C, 0xE7, 0x3C, 0x5A,   // 15
        0x7F, 0x3E, 0x1C, 0x1C, 0x08,   // 16
        0x08, 0x1C, 0x1C, 0x3E, 0x7F,   // 17
        0x14, 0x22, 0x7F, 0x22, 0x14,   // 18
        0x5F, 0x5F, 0x00, 0x5F, 0x5F,   // 19
        0x06, 0x09, 0x7F, 0x01, 0x7F,   // 20
        0x00, 0x66, 0x89, 0x95, 0x6A,   // 21
        0x60, 0x60, 0x60, 0x60, 0x60,   // 22
        0x94, 0xA2, 0xFF, 0xA2, 0x94,   // 23
        0x08, 0x04, 0x7E, 0x04, 0x08,   // 24
        0x10, 0x20, 0x7E, 0x20, 0x10,   // 25
        0x08, 0x08, 0x2A, 0x1C, 0x08,   // 26
        0x08, 0x1C, 0x2A, 0x08, 0x08,   // 27
        0x1E, 0x10, 0x10, 0x10, 0x10,   // 28
        0x0C, 0x1E, 0x0C, 0x1E, 0x0C,   // 29
        0x30, 0x38, 0x3E, 0x38, 0x30,   // 30
        0x06, 0x0E, 0x3E, 0x0E, 0x06,   // 31
        0x00, 0x00, 0x00, 0x00, 0x00,   //  
        0x00, 0x00, 0x5F, 0x00, 0x00,   // !
        0x00, 0x07, 0x00, 0x07, 0x00,   // "
        0x14, 0x7F, 0x14, 0x7F, 0x14,   // #
        0x24, 0x2A, 0x7F, 0x2A, 0x12,   // $
        0x23, 0x13, 0x08, 0x64, 0x62,   // %
        0x36, 0x49, 0x56, 0x20, 0x50,   // &
        0x00, 0x08, 0x07, 0x03, 0x00,   // '
        0x00, 0x1C, 0x22, 0x41, 0x00,   // (
        0x00, 0x41, 0x22, 0x1C, 0x00,   // )
        0x2A, 0x1C, 0x7F, 0x1C, 0x2A,   // *
        0x08, 0x08, 0x3E, 0x08, 0x08,   // +
        0x00, 0x80, 0x70, 0x30, 0x00,   // ,
        0x08, 0x08, 0x08, 0x08, 0x08,   // -
        0x00, 0x00, 0x60, 0x60, 0x00,   // .
        0x20, 0x10, 0x08, 0x04, 0x02,   // /
        0x3E, 0x51, 0x49, 0x45, 0x3E,   // 0
        0x00, 0x42, 0x7F, 0x40, 0x00,   // 1
        0x72, 0x49, 0x49, 0x49, 0x46,   // 2
        0x21, 0x41, 0x49, 0x4D, 0x33,   // 3
        0x18, 0x14, 0x12, 0x7F, 0x10,   // 4
        0x27, 0x45, 0x45, 0x45, 0x39,   // 5
        0x3C, 0x4A, 0x49, 0x49, 0x31,   // 6
        0x41, 0x21, 0x11, 0x09, 0x07,   // 7
        0x36, 0x49, 0x49, 0x49, 0x36,   // 8
        0x46, 0x49, 0x49, 0x29, 0x1E,   // 9
        0x00, 0x00, 0x14, 0x00, 0x00,   // :
        0x00, 0x40, 0x34, 0x00, 0x00,   // ;
        0x00, 0x08, 0x14, 0x22, 0x41,   // <
        0x14, 0x14, 0x14, 0x14, 0x14,   // =
        0x00, 0x41, 0x22, 0x14, 0x08,   // >
        0x02, 0x01, 0x59, 0x09, 0x06,   // ?
        0x3E, 0x41, 0x5D, 0x59, 0x4E,   // @
        0x7C, 0x12, 0x11, 0x12, 0x7C,   // A
        0x7F, 0x49, 0x49, 0x49, 0x36,   // B
        0x3E, 0x41, 0x41, 0x41, 0x22,   // C
        0x7F, 0x41, 0x41, 0x41, 0x3E,   // D
        0x7F, 0x49, 0x49, 0x49, 0x41,   // E
        0x7F, 0x09, 0x09, 0x09, 0x01,   // F
        0x3E, 0x41, 0x41, 0x51, 0x73,   // G
        0x7F, 0x08, 0x08, 0x08, 0x7F,   // H
        0x00, 0x41, 0x7F, 0x41, 0x00,   // I
        0x20, 0x40, 0x41, 0x3F, 0x01,   // J
        0x7F, 0x08, 0x14, 0x22, 0x41,   // K
        0x7F, 0x40, 0x40, 0x40, 0x40,   // L
        0x7F, 0x02, 0x1C, 0x02, 0x7F,   // M
        0x7F, 0x04, 0x08, 0x10, 0x7F,   // N
        0x3E, 0x41, 0x41, 0x41, 0x3E,   // O
        0x7F, 0x09, 0x09, 0x09, 0x06,   // P
        0x3E, 0x41, 0x51, 0x21, 0x5E,   // Q
        0x7F, 0x09, 0x19, 0x29, 0x46,   // R
        0x26, 0x49, 0x49, 0x49, 0x32,   // S
        0x03, 0x01, 0x7F, 0x01, 0x03,   // T
        0x3F, 0x40, 0x40, 0x40, 0x3F,   // U
        0x1F, 0x20, 0x40, 0x20, 0x1F,   // V
        0x3F, 0x40, 0x38, 0x40, 0x3F,   // W
        0x63, 0x14, 0x08, 0x14, 0x63,   // X
        0x03, 0x04, 0x78, 0x04, 0x03,   // Y
        0x61, 0x59, 0x49, 0x4D, 0x43,   // Z
        0x00, 0x7F, 0x41, 0x41, 0x41,   // [
        0x02, 0x04, 0x08, 0x10, 0x20,   // 92
        0x00, 0x41, 0x41, 0x41, 0x7F,   // ]
        0x04, 0x02, 0x01, 0x02, 0x04,   // ^
        0x40, 0x40, 0x40, 0x40, 0x40,   // _
        0x00, 0x03, 0x07, 0x08, 0x00,   // `
        0x20, 0x54, 0x54, 0x78, 0x40,   // a
        0x7F, 0x28, 0x44, 0x44, 0x38,   // b
        0x38, 0x44, 0x44, 0x44, 0x28,   // c
        0x38, 0x44, 0x44, 0x28, 0x7F,   // d
        0x38, 0x54, 0x54, 0x54, 0x18,   // e
        0x00, 0x08, 0x7E, 0x09, 0x02,   // f
        0x18, 0xA4, 0xA4, 0x9C, 0x78,   // g
        0x7F, 0x08, 0x04, 0x04, 0x78,   // h
        0x00, 0x44, 0x7D, 0x40, 0x00,   // i
        0x20, 0x40, 0x40, 0x3D, 0x00,   // j
        0x7F, 0x10, 0x28, 0x44, 0x00,   // k
        0x00, 0x41, 0x7F, 0x40, 0x00,   // l
        0x7C, 0x04, 0x78, 0x04, 0x78,   // m
        0x7C, 0x08, 0x04, 0x04, 0x78,   // n
        0x38, 0x44, 0x44, 0x44, 0x38,   // o
        0xFC, 0x18, 0x24, 0x24, 0x18,   // p
        0x18, 0x24, 0x24, 0x18, 0xFC,   // q
        0x7C, 0x08, 0x04, 0x04, 0x08,   // r
        0x48, 0x54, 0x54, 0x54, 0x24,   // s
        0x04, 0x04, 0x3F, 0x44, 0x24,   // t
        0x3C, 0x40, 0x40, 0x20, 0x7C,   // u
        0x1C, 0x20, 0x40, 0x20, 0x1C,   // v
        0x3C, 0x40, 0x30, 0x40, 0x3C,   // w
        0x44, 0x28, 0x10, 0x28, 0x44,   // x
        0x4C, 0x90, 0x90, 0x90, 0x7C,   // y
        0x44, 0x64, 0x54, 0x4C, 0x44,   // z
        0x00, 0x08, 0x36, 0x41, 0x00,   // {
        0x00, 0x00, 0x77, 0x00, 0x00,   // |
        0x00, 0x41, 0x36, 0x08, 0x00,   // }
        0x02, 0x01, 0x02, 0x04, 0x02,   // ~
        0x3C, 0x26, 0x23, 0x26, 0x3C,   // 127
        0x1E, 0xA1, 0xA1, 0x61, 0x12,   // 128
        0x3A, 0x40, 0x40, 0x20, 0x7A,   // 129
        0x38, 0x54, 0x54, 0x55, 0x59,   // 130
        0x21, 0x55, 0x55, 0x79, 0x41,   // 131
        0x21, 0x54, 0x54, 0x78, 0x41,   // 132
        0x21, 0x55, 0x54, 0x78, 0x40,   // 133
        0x20, 0x54, 0x55, 0x79, 0x40,   // 134
        0x0C, 0x1E, 0x52, 0x72, 0x12,   // 135
        0x39, 0x55, 0x55, 0x55, 0x59,   // 136
        0x39, 0x54, 0x54, 0x54, 0x59,   // 137
        0x39, 0x55, 0x54, 0x54, 0x58,   // 138
        0x00, 0x00, 0x45, 0x7C, 0x41,   // 139
        0x00, 0x02, 0x45, 0x7D, 0x42,   // 140
        0x00, 0x01, 0x45, 0x7C, 0x40,   // 141
        0xF0, 0x29, 0x24, 0x29, 0xF0,   // 142
        0xF0, 0x28, 0x25, 0x28, 0xF0,   // 143
        0x7C, 0x54, 0x55, 0x45, 0x00,   // 144
        0x20, 0x54, 0x54, 0x7C, 0x54,   // 145
        0x7C, 0x0A, 0x09, 0x7F, 0x49,   // 146
        0x32, 0x49, 0x49, 0x49, 0x32,   // 147
        0x32, 0x48, 0x48, 0x48, 0x32,   // 148
        0x32, 0x4A, 0x48, 0x48, 0x30,   // 149
        0x3A, 0x41, 0x41, 0x21, 0x7A,   // 150
        0x3A, 0x42, 0x40, 0x20, 0x78,   // 151
        0x00, 0x9D, 0xA0, 0xA0, 0x7D,   // 152
        0x39, 0x44, 0x44, 0x44, 0x39,   // 153
        0x3D, 0x40, 0x40, 0x40, 0x3D,   // 154
        0x3C, 0x24, 0xFF, 0x24, 0x24,   // 155
        0x48, 0x7E, 0x49, 0x43, 0x66,   // 156
        0x2B, 0x2F, 0xFC, 0x2F, 0x2B,   // 157
        0xFF, 0x09, 0x29, 0xF6, 0x20,   // 158
        0xC0, 0x88, 0x7E, 0x09, 0x03,   // 159
        0x20, 0x54, 0x54, 0x79, 0x41,   // 160
        0x00, 0x00, 0x44, 0x7D, 0x41,   // 161
        0x30, 0x48, 0x48, 0x4A, 0x32,   // 162
        0x38, 0x40, 0x40, 0x22, 0x7A,   // 163
        0x00, 0x7A, 0x0A, 0x0A, 0x72,   // 164
        0x7D, 0x0D, 0x19, 0x31, 0x7D,   // 165
        0x26, 0x29, 0x29, 0x2F, 0x28,   // 166
        0x26, 0x29, 0x29, 0x29, 0x26,   // 167
        0x30, 0x48, 0x4D, 0x40, 0x20,   // 168
        0x38, 0x08, 0x08, 0x08, 0x08,   // 169
        0x08, 0x08, 0x08, 0x08, 0x38,   // 170
        0x2F, 0x10, 0xC8, 0xAC, 0xBA,   // 171
        0x2F, 0x10, 0x28, 0x34, 0xFA,   // 172
        0x00, 0x00, 0x7B, 0x00, 0x00,   // 173
        0x08, 0x14, 0x2A, 0x14, 0x22,   // 174
        0x22, 0x14, 0x2A, 0x14, 0x08,   // 175
        0xAA, 0x00, 0x55, 0x00, 0xAA,   // 176
        0xAA, 0x55, 0xAA, 0x55, 0xAA,   // 177
        0x00, 0x00, 0x00, 0xFF, 0x00,   // 178
        0x10, 0x10, 0x10, 0xFF, 0x00,   // 179
        0x14, 0x14, 0x14, 0xFF, 0x00,   // 180
        0x10, 0x10, 0xFF, 0x00, 0xFF,   // 181
        0x10, 0x10, 0xF0, 0x10, 0xF0,   // 182
        0x14, 0x14, 0x14, 0xFC, 0x00,   // 183
        0x14, 0x14, 0xF7, 0x00, 0xFF,   // 184
        0x00, 0x00, 0xFF, 0x00, 0xFF,   // 185
        0x14, 0x14, 0xF4, 0x04, 0xFC,   // 186
        0x14, 0x14, 0x17, 0x10, 0x1F,   // 187
        0x10, 0x10, 0x1F, 0x10, 0x1F,   // 188
        0x14, 0x14, 0x14, 0x1F, 0x00,   // 189
        0x10, 0x10, 0x10, 0xF0, 0x00,   // 190
        0x00, 0x00, 0x00, 0x1F, 0x10,   // 191
        0x10, 0x10, 0x10, 0x1F, 0x10,   // 192
        0x10, 0x10, 0x10, 0xF0, 0x10,   // 193
        0x00, 0x00, 0x00, 0xFF, 0x10,   // 194
        0x10, 0x10, 0x10, 0x10, 0x10,   // 195
        0x10, 0x10, 0x10, 0xFF, 0x10,   // 196
        0x00, 0x00, 0x00, 0xFF, 0x14,   // 197
        0x00, 0x00, 0xFF, 0x00, 0xFF,   // 198
        0x00, 0x00, 0x1F, 0x10, 0x17,   // 199
        0x00, 0x00, 0xFC, 0x04, 0xF4,   // 200
        0x14, 0x14, 0x17, 0x10, 0x17,   // 201
        0x14, 0x14, 0xF4, 0x04, 0xF4,   // 202
        0x00, 0x00, 0xFF, 0x00, 0xF7,   // 203
        0x14, 0x14, 0x14, 0x14, 0x14,   // 204
        0x14, 0x14, 0xF7, 0x00, 0xF7,   // 205
        0x14, 0x14, 0x14, 0x17, 0x14,   // 206
        0x10, 0x10, 0x1F, 0x10, 0x1F,   // 207
        0x14, 0x14, 0x14, 0xF4, 0x14,   // 208
        0x10, 0x10, 0xF0, 0x10, 0xF0,   // 209
        0x00, 0x00, 0x1F, 0x10, 0x1F,   // 210
        0x00, 0x00, 0x00, 0x1F, 0x14,   // 211
        0x00, 0x00, 0x00, 0xFC, 0x14,   // 212
        0x00, 0x00, 0xF0, 0x10, 0xF0,   // 213
        0x10, 0x10, 0xFF, 0x10, 0xFF,   // 214
        0x14, 0x14, 0x14, 0xFF, 0x14,   // 215
        0x10, 0x10, 0x10, 0x1F, 0x00,   // 216
        0x00, 0x00, 0x00, 0xF0, 0x10,   // 217
        0xFF, 0xFF, 0xFF, 0xFF, 0xFF,   // 218
        0xF0, 0xF0, 0xF0, 0xF0, 0xF0,   // 219
        0xFF, 0xFF, 0xFF, 0x00, 0x00,   // 220
        0x00, 0x00, 0x00, 0xFF, 0xFF,   // 221
        0x0F, 0x0F, 0x0F, 0x0F, 0x0F,   // 222
        0x38, 0x44, 0x44, 0x38, 0x44,   // 223
        0x7C, 0x2A, 0x2A, 0x3E, 0x14,   // 224
        0x7E, 0x02, 0x02, 0x06, 0x06,   // 225
        0x02, 0x7E, 0x02, 0x7E, 0x02,   // 226
        0x63, 0x55, 0x49, 0x41, 0x63,   // 227
        0x38, 0x44, 0x44, 0x3C, 0x04,   // 228
        0x40, 0x7E, 0x20, 0x1E, 0x20,   // 229
        0x06, 0x02, 0x7E, 0x02, 0x02,   // 230
        0x99, 0xA5, 0xE7, 0xA5, 0x99,   // 231
        0x1C, 0x2A, 0x49, 0x2A, 0x1C,   // 232
        0x4C, 0x72, 0x01, 0x72, 0x4C,   // 233
        0x30, 0x4A, 0x4D, 0x4D, 0x30,   // 234
        0x30, 0x48, 0x78, 0x48, 0x30,   // 235
        0xBC, 0x62, 0x5A, 0x46, 0x3D,   // 236
        0x3E, 0x49, 0x49, 0x49, 0x00,   // 237
        0x7E, 0x01, 0x01, 0x01, 0x7E,   // 238
        0x2A, 0x2A, 0x2A, 0x2A, 0x2A,   // 239
        0x44, 0x44, 0x5F, 0x44, 0x44,   // 240
        0x40, 0x51, 0x4A, 0x44, 0x40,   // 241
        0x40, 0x44, 0x4A, 0x51, 0x40,   // 242
        0x00, 0x00, 0xFF, 0x01, 0x03,   // 243
        0xE0, 0x80, 0xFF, 0x00, 0x00,   // 244
        0x08, 0x08, 0x6B, 0x6B, 0x08,   // 245
        0x36, 0x12, 0x36, 0x24, 0x36,   // 246
        0x06, 0x0F, 0x09, 0x0F, 0x06,   // 247
        0x00, 0x00, 0x18, 0x18, 0x00,   // 248
        0x00, 0x00, 0x10, 0x10, 0x00,   // 249
        0x30, 0x40, 0xFF, 0x01, 0x01,   // 250
        0x00, 0x1F, 0x01, 0x01, 0x1E,   // 251
        0x00, 0x19, 0x1D, 0x17, 0x12,   // 252
        0x00, 0x3C, 0x3C, 0x3C, 0x3C,   // 253
        0x00, 0x00, 0x00, 0x00, 0x00,   // 254
    ];

    /// <summary>
    /// Cor do ponto (x,y) COMO O SKETCH O VÊ, já desfazendo a rotação.
    ///
    /// É assim que o teste consegue cobrar "desenhei em (10,20), tem que
    /// estar em (10,20)" sem precisar refazer a conta da rotação -- que
    /// é justamente a conta que ele está testando.
    /// </summary>
    public int CorEm(int x, int y)
    {
        if (x < 0 || y < 0 || x >= Largura || y >= Altura) return 0;
        var (px, py) = NoPainel(x, y);
        if (px < 0 || py < 0 || px >= LarguraFisica || py >= AlturaFisica) return 0;
        return Pixels[py * LarguraFisica + px];
    }
}
