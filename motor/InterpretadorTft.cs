namespace Simulador;

/// <summary>
/// Os displays TFT (ILI9341 e ST7789) com a Adafruit_GFX.
///
/// Os argumentos sao lidos aqui, e nao avaliados todos de uma vez antes:
/// o drawRGBBitmap recebe um VETOR inteiro, e o getTextBounds recebe o
/// endereco das variaveis onde escreve a resposta.
/// </summary>
public partial class Interpretador
{
    Valor ChamarTft(Objeto obj, string metodo, Chamada c, Ambiente amb)
    {
        var tela = obj.Tela ?? (obj.Classe switch
        {
            "Adafruit_ST7789" => maq.CriarTela(240, 240),
            "Adafruit_ST7735" => maq.CriarTela(128, 160),
            _ => maq.CriarTela(240, 320),
        });

        var avaliados = new Valor[c.Args.Count];
        var prontos = new bool[c.Args.Count];
        Valor Arg(int i)
        {
            if (i >= c.Args.Count) return Valor.DeInt(0);
            if (!prontos[i]) { avaliados[i] = Avaliar(c.Args[i], amb); prontos[i] = true; }
            return avaliados[i];
        }
        int A(int i) => (int)(long)Arg(i).Num;
        int N = c.Args.Count;

        Valor[] Vetor(int i)
        {
            if (i < N && VetorDoArgumento(c.Args[i], amb, out var vetor)) return vetor;
            throw new ErroDeExecucao($"tft.{metodo}(): o argumento {i + 1} tem que ser um vetor (a imagem).", c.Linha);
        }

        switch (metodo)
        {
            // O ST7789 comeca com init(240, 240) em vez de begin().
            case "begin": case "init":
                tela.Iniciar();
                return Valor.DeBool(true);

            // O ST7735: initR(INITR_BLACKTAB) e o de 1,8"; o INITR_144GREENTAB, o
            // de 1,44" (128 x 128); o INITR_MINI160x80, o de 0,96" (80 x 160).
            case "initR": case "initB":
            {
                var (l, a) = metodo == "initR" ? A(0) switch { 0x101 => (128, 128), 0x04 => (80, 160), _ => (128, 160) } : (128, 160);
                if (tela.LarguraFisica != l || tela.AlturaFisica != a) obj.Tela = tela = maq.TrocarTela(l, a);
                tela.Iniciar();
                return Valor.Vazio;
            }
            case "display":
                return Valor.DeBool(true);

            case "fillScreen": tela.Limpar(A(0)); return Valor.Vazio;
            case "clearDisplay": tela.Limpar(0); return Valor.Vazio;

            case "drawPixel": case "writePixel":
                tela.Pixel(A(0), A(1), A(2));
                return Valor.Vazio;

            case "drawLine": case "writeLine":
                tela.Linha(A(0), A(1), A(2), A(3), A(4));
                return Valor.Vazio;

            // Retangulo de uma linha, e nao uma linha de Bresenham: com
            // altura zero a biblioteca nao pinta nada -- e o raycaster que
            // manda "parede de zero pixels" no meio da tela nao pode deixar
            // pontos soltos.
            case "drawFastHLine": case "writeFastHLine":
                tela.Retangulo(A(0), A(1), A(2), 1, A(3), true);
                return Valor.Vazio;
            case "drawFastVLine": case "writeFastVLine":
                tela.Retangulo(A(0), A(1), 1, A(2), A(3), true);
                return Valor.Vazio;

            case "drawRect":
                tela.Retangulo(A(0), A(1), A(2), A(3), A(4), false);
                return Valor.Vazio;
            case "fillRect": case "writeFillRect":
                tela.Retangulo(A(0), A(1), A(2), A(3), A(4), true);
                return Valor.Vazio;

            case "drawCircle": tela.Circulo(A(0), A(1), A(2), A(3), false); return Valor.Vazio;
            case "fillCircle": tela.Circulo(A(0), A(1), A(2), A(3), true); return Valor.Vazio;

            case "drawTriangle":
                tela.Triangulo(A(0), A(1), A(2), A(3), A(4), A(5), A(6), false);
                return Valor.Vazio;
            case "fillTriangle":
                tela.Triangulo(A(0), A(1), A(2), A(3), A(4), A(5), A(6), true);
                return Valor.Vazio;

            case "drawRoundRect":
                tela.RetanguloRedondo(A(0), A(1), A(2), A(3), A(4), A(5), false);
                return Valor.Vazio;
            case "fillRoundRect":
                tela.RetanguloRedondo(A(0), A(1), A(2), A(3), A(4), A(5), true);
                return Valor.Vazio;

            // ---------- texto ----------
            case "setCursor": tela.PosicionarCursor(A(0), A(1)); return Valor.Vazio;
            case "getCursorX": return Valor.DeInt(tela.CursorX);
            case "getCursorY": return Valor.DeInt(tela.CursorY);

            case "setTextColor":
                if (N >= 2) tela.DefinirCorDoTexto(A(0), A(1));
                else tela.DefinirCorDoTexto(A(0));
                return Valor.Vazio;
            case "setTextSize":
                if (N >= 2) tela.DefinirTamanhoDoTexto(A(0), A(1));
                else tela.DefinirTamanhoDoTexto(A(0));
                return Valor.Vazio;
            case "setTextWrap": tela.Quebrar = Arg(0).Verdadeiro; return Valor.Vazio;
            case "cp437": return Valor.Vazio;

            // setFont() sem nada volta para a fonte classica; as fontes GFX
            // (FreeSans9pt7b...) ainda nao estao aqui.
            case "setFont":
                if (N == 0 || Arg(0).Num == 0 && !Arg(0).EhTexto && Arg(0).Referencia is null) return Valor.Vazio;
                throw new ErroDeExecucao(
                    "tft.setFont(): as fontes da Adafruit_GFX (FreeSans, FreeMono...) ainda nao estao no "
                    + "simulador. Sem setFont, o texto sai na fonte 5x7 da biblioteca -- use setTextSize para aumentar.",
                    c.Linha);

            case "print":
                tela.Escrever(N == 0 ? "" : Imprimivel([.. Enumerable.Range(0, N).Select(Arg)]), false);
                return Valor.Vazio;
            case "println":
                tela.Escrever(N == 0 ? "" : Imprimivel([.. Enumerable.Range(0, N).Select(Arg)]), true);
                return Valor.Vazio;

            // O printf da Print, como o Serial.printf: "tft.printf("%3d%%", vida)".
            case "printf":
            {
                string formatado = Formatar([.. Enumerable.Range(0, N).Select(Arg)]);
                tela.Escrever(formatado, false);
                return Valor.DeInt(formatado.Length);
            }

            // write('A') ou write(65): um byte. write("abc"): o texto todo.
            case "write":
            {
                var v = Arg(0);
                if (v.EhTexto) tela.Escrever(v.Texto, false);
                else tela.EscreverByte((byte)((long)v.Num & 0xFF));
                return Valor.DeInt(1);
            }

            case "drawChar":
            {
                int tamX = N >= 7 ? A(5) : N >= 6 ? A(5) : 1;
                int tamY = N >= 7 ? A(6) : tamX;
                tela.DesenharLetra(A(0), A(1), A(2), A(3), A(4), Math.Max(1, tamX), Math.Max(1, tamY));
                return Valor.Vazio;
            }

            // getTextBounds(texto, x, y, &x1, &y1, &w, &h)
            case "getTextBounds":
            {
                var (bx, by, bl, ba) = tela.MedirTexto(Arg(0).Mostrar(), A(1), A(2));
                int[] respostas = [bx, by, bl, ba];
                for (int i = 0; i < 4 && 3 + i < N; i++)
                    Guardar(SemEndereco(c.Args[3 + i]), Valor.DeInt(respostas[i]), amb);
                return Valor.Vazio;
            }

            // ---------- tela ----------
            case "setRotation": tela.DefinirRotacao(A(0)); return Valor.Vazio;
            case "getRotation": return Valor.DeInt(tela.Rotacao);
            case "width": return Valor.DeInt(tela.Largura);
            case "height": return Valor.DeInt(tela.Altura);

            case "color565":
                return Valor.DeInt(((A(0) & 0xF8) << 8) | ((A(1) & 0xFC) << 3) | ((A(2) & 0xFF) >> 3));

            case "invertDisplay": tela.Inverter(Arg(0).Verdadeiro); return Valor.Vazio;

            // DISPOFF, SLPIN, INVON... escritos na mao.
            case "sendCommand": case "writeCommand":
                tela.Comando(A(0));
                return Valor.Vazio;
            case "enableDisplay": tela.Comando(Arg(0).Verdadeiro ? 0x29 : 0x28); return Valor.Vazio;
            case "enableSleep": tela.Comando(Arg(0).Verdadeiro ? 0x10 : 0x11); return Valor.Vazio;

            // O que um ILI9341 ligado e iniciado responde -- e o que o
            // exemplo graphicstest imprime no comeco.
            case "readcommand8":
                return Valor.DeInt(A(0) switch
                {
                    0x0A => 0x9C,
                    0x0B => 0x48,
                    0x0C => 0x05,
                    0x0F => 0xC0,
                    _ => 0,
                });

            // ---------- escrita rapida ----------
            case "startWrite": case "endWrite": case "dmaWait": case "setSPISpeed":
            case "enableTearing": case "setColRowStart":
                return Valor.Vazio;

            case "setAddrWindow": tela.DefinirJanela(A(0), A(1), A(2), A(3)); return Valor.Vazio;
            case "pushColor": tela.EmpurrarCor(A(0)); return Valor.Vazio;
            case "writeColor":
                for (int i = 0, n = Math.Max(0, A(1)); i < n; i++) tela.EmpurrarCor(A(0));
                return Valor.Vazio;
            case "writePixels":
            {
                var cores = Vetor(0);
                int n = N >= 2 ? Math.Min(A(1), cores.Length) : cores.Length;
                for (int i = 0; i < n; i++) tela.EmpurrarCor((int)(long)cores[i].Num);
                return Valor.Vazio;
            }

            // ---------- imagens ----------
            // drawRGBBitmap(x, y, cores, l, a) e (x, y, cores, mascara, l, a).
            case "drawRGBBitmap":
            {
                int x = A(0), y = A(1);
                var cores = Vetor(2);
                Valor[] mascara = N >= 6 ? Vetor(3) : null;
                int l = N >= 6 ? A(4) : A(3), a = N >= 6 ? A(5) : A(4);
                int bytesPorLinha = (l + 7) / 8;
                for (int j = 0; j < a; j++)
                    for (int i = 0; i < l; i++)
                    {
                        int k = j * l + i;
                        if (k >= cores.Length) break;
                        if (mascara is not null)
                        {
                            int m = j * bytesPorLinha + i / 8;
                            if (m >= mascara.Length || ((long)mascara[m].Num & (0x80 >> (i & 7))) == 0) continue;
                        }
                        tela.Pixel(x + i, y + j, (int)(long)cores[k].Num);
                    }
                return Valor.Vazio;
            }

            // drawBitmap: 1 bit por ponto, o bit de cima a esquerda.
            // drawXBitmap: o formato XBM, o bit de baixo a esquerda.
            case "drawBitmap":
            case "drawXBitmap":
            {
                int x = A(0), y = A(1);
                var bits = Vetor(2);
                int l = A(3), a = A(4), cor = A(5);
                bool comFundo = N >= 7;
                int fundo = comFundo ? A(6) : 0;
                int bytesPorLinha = (l + 7) / 8;
                for (int j = 0; j < a; j++)
                    for (int i = 0; i < l; i++)
                    {
                        int k = j * bytesPorLinha + i / 8;
                        long b = k < bits.Length ? (long)bits[k].Num : 0;
                        bool aceso = metodo == "drawBitmap" ? (b & (0x80 >> (i & 7))) != 0 : ((b >> (i & 7)) & 1) != 0;
                        if (aceso) tela.Pixel(x + i, y + j, cor);
                        else if (comFundo) tela.Pixel(x + i, y + j, fundo);
                    }
                return Valor.Vazio;
            }
        }

        throw new ErroDeExecucao($"'{obj.Classe}.{metodo}()' ainda nao e suportado", c.Linha);
    }
}
