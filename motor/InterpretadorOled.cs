namespace Simulador;

/// <summary>
/// O OLED SSD1306 com a Adafruit_SSD1306: um buffer de um bit por ponto na
/// RAM da placa (o malloc do begin()), onde o sketch desenha com a
/// Adafruit_GFX, e o painel, que so muda no display(). Antes era uma tela de
/// texto: drawPixel, linhas, circulos e bitmaps nao apareciam, e o desenho
/// aparecia sem o display().
///
/// O que vale da biblioteca 2.5.17: o begin() reserva o buffer com malloc
/// (num Uno apertado ele falha e devolve false: o "SSD1306 allocation
/// failed" dos exemplos), limpa e desenha o logotipo da Adafruit -- que
/// aparece no primeiro display(). invertDisplay() e dim() mudam o painel na
/// hora; a rolagem e do proprio painel. E o display() leva o tempo do
/// barramento: ~25 ms pelo I2C a 400 kHz.
/// </summary>
public partial class Interpretador
{
    /// <summary>"Adafruit_SSD1306 display(128, 64, &amp;Wire, -1)": o buffer do tamanho pedido.</summary>
    void MontarOled(Objeto obj, List<Valor> args)
    {
        int l = 128, a = 64;
        if (args.Count >= 2 && args[0].Num is 128 or 96 or 64 && args[1].Num is 64 or 32 or 48 or 16)
            (l, a) = ((int)args[0].Num, (int)args[1].Num);
        obj.Tela = new TelaGrafica(l, a) { Monocromatica = true };

        // SPI: (l, a, mosi, clk, dc, rst, cs) ou (l, a, &SPI, dc, rst, cs).
        bool spi = args.Count >= 6 || args.Count >= 5 && args[2].EhTexto && args[2].Texto is "SPI" or "SPI1";
        obj.Campos["_spi"] = Valor.DeBool(spi);

        // I2C: (l, a, &Wire1, rst) -- o barramento que o sketch deu; sem ele, o Wire.
        if (!spi) obj.Campos["_wire"] = Valor.DeInt(args.Count > 2 && BarramentoDoValor(args[2], 0) is { } b ? b.Numero : 0);
    }

    BarramentoI2c BarramentoDoOled(Objeto obj)
        => obj.Campos.TryGetValue("_wire", out var w) && w.Num == 1 ? maq.I2c1 : maq.I2c;

    Valor ChamarOled(Objeto obj, string metodo, Chamada c, Ambiente amb)
    {
        var buffer = obj.Tela ??= new TelaGrafica(128, 64) { Monocromatica = true };
        bool spi = obj.Campos.TryGetValue("_spi", out var s) && s.Verdadeiro;
        var painel = obj.Rede as TelaGrafica;
        double agoraMs = maq.AgoraUs() / 1000;

        switch (metodo)
        {
            case "begin":
            {
                var a = c.Args.Select(arg => Avaliar(arg, amb)).ToList();
                if (!obj.Campos.ContainsKey("_reservado"))
                {
                    long bytes = buffer.LarguraFisica * ((buffer.AlturaFisica + 7) / 8);
                    if (!Ram.Alocar(bytes, false, out long custo)) return Valor.DeBool(false);
                    obj.Campos["_reservado"] = Valor.DeInt(custo);
                }
                buffer.Limpar(0);
                bool alta = buffer.AlturaFisica > 32;
                int sl = alta ? SplashDoOled.Largura1 : SplashDoOled.Largura2, sa = alta ? SplashDoOled.Altura1 : SplashDoOled.Altura2;
                var splash = alta ? SplashDoOled.Splash1 : SplashDoOled.Splash2;
                int x0 = (buffer.Largura - sl) / 2, y0 = (buffer.Altura - sa) / 2, porLinha = (sl + 7) / 8;
                for (int j = 0; j < sa; j++)
                    for (int i = 0; i < sl; i++)
                        if ((splash[j * porLinha + i / 8] & (0x80 >> (i & 7))) != 0) buffer.Pixel(x0 + i, y0 + j, 1);

                // begin(vcc, endereco, reset, periphBegin). Sem endereco, o da
                // biblioteca: 0x3C no de 32 linhas e 0x3D nos outros -- o
                // 128x64 de 0x3C com "begin(SSD1306_SWITCHCAPVCC)" fica preto.
                int endereco = -1, numero = -1;
                if (!spi)
                {
                    var bus = BarramentoDoOled(obj);
                    // O SH1106 e begin(endereco, reset), com o 0x3C de padrao.
                    endereco = obj.Classe == "Adafruit_SH1106G" ? (a.Count > 0 && a[0].Num > 0 ? (int)a[0].Num & 0x7F : 0x3C)
                             : a.Count > 1 && a[1].Num > 0 ? (int)a[1].Num & 0x7F : buffer.AlturaFisica == 32 ? 0x3C : 0x3D;
                    numero = bus.Numero;
                    RegistrarNoI2c(obj, endereco, numero);
                    if (a.Count < 4 || a[3].Verdadeiro) ComecarBarramento(bus, NomeDoBarramento(bus));
                }
                painel = maq.CriarPainelOled(buffer.LarguraFisica, buffer.AlturaFisica, numero, endereco, spi);
                obj.Rede = painel;
                painel.Iniciar();
                painel.Escurecer(false);
                painel.PararRolagem(agoraMs);

                // A sequencia de partida do controlador: ~30 comandos no barramento.
                maq.EsperarNoChip(spi ? 100 : 1500);
                obj.Lcd?.Limpar();
                return Valor.DeBool(true);
            }

            case "display":
                if (painel is null || !obj.Campos.ContainsKey("_reservado")) return Valor.Vazio;
                painel.CopiarDe(buffer);
                // O buffer inteiro pelo barramento: 1024 bytes de 9 bits a
                // 400 kHz no I2C (~25 ms); 8 MHz no SPI (~1 ms).
                maq.EsperarNoChip(spi ? buffer.Pixels.Length / 8.0 + 50 : buffer.Pixels.Length / 8.0 * 9 / 0.4 + 1600);
                return Valor.Vazio;

            case "clearDisplay":
                buffer.Limpar(0);
                obj.Lcd?.Limpar();
                return Valor.Vazio;

            case "invertDisplay":
                painel?.Inverter(c.Args.Count > 0 && Avaliar(c.Args[0], amb).Verdadeiro);
                return Valor.Vazio;

            case "dim":
                painel?.Escurecer(c.Args.Count > 0 && Avaliar(c.Args[0], amb).Verdadeiro);
                return Valor.Vazio;

            case "startscrollright":
            case "startscrollleft":
            case "startscrolldiagright":
            case "startscrolldiagleft":
            {
                int ini = c.Args.Count > 0 ? (int)Avaliar(c.Args[0], amb).Num : 0;
                int fim = c.Args.Count > 1 ? (int)Avaliar(c.Args[1], amb).Num : 7;
                int direcao = metodo switch
                {
                    "startscrollright" => 1, "startscrollleft" => -1, "startscrolldiagright" => 2, _ => -2,
                };
                painel?.Rolar(direcao, ini, fim, agoraMs);
                return Valor.Vazio;
            }

            case "stopscroll":
                painel?.PararRolagem(agoraMs);
                return Valor.Vazio;

            // Os comandos do controlador escritos na mao.
            case "ssd1306_command":
            case "ssd1306_command1":
            {
                int cmd = c.Args.Count > 0 ? (int)Avaliar(c.Args[0], amb).Num & 0xFF : 0;
                if (obj.Campos.Remove("_contraste")) { painel?.Escurecer(cmd < 0x10); return Valor.Vazio; }
                switch (cmd)
                {
                    case 0xAE: painel?.MostrarImagem(false); break;   // DISPLAYOFF
                    case 0xAF: painel?.MostrarImagem(true); break;    // DISPLAYON
                    case 0xA6: painel?.Inverter(false); break;        // NORMALDISPLAY
                    case 0xA7: painel?.Inverter(true); break;         // INVERTDISPLAY
                    case 0x2E: painel?.PararRolagem(agoraMs); break;  // DEACTIVATE_SCROLL
                    case 0x81: obj.Campos["_contraste"] = Valor.DeInt(1); break;   // SETCONTRAST: o proximo e o valor
                }
                return Valor.Vazio;
            }

            case "getPixel":
            {
                int x = c.Args.Count > 0 ? (int)Avaliar(c.Args[0], amb).Num : 0;
                int y = c.Args.Count > 1 ? (int)Avaliar(c.Args[1], amb).Num : 0;
                return Valor.DeBool(buffer.CorEm(x, y) == buffer.CorAcesa);
            }

            case "getBuffer":
            {
                // O buffer no formato do controlador: paginas de 8 linhas, um byte por coluna.
                int l = buffer.LarguraFisica, paginas = (buffer.AlturaFisica + 7) / 8;
                var bytes = new Valor[l * paginas];
                for (int p = 0; p < paginas; p++)
                    for (int x = 0; x < l; x++)
                    {
                        int b = 0;
                        for (int bit = 0; bit < 8; bit++)
                        {
                            int y = p * 8 + bit;
                            if (y < buffer.AlturaFisica && buffer.Pixels[y * l + x] == buffer.CorAcesa) b |= 1 << bit;
                        }
                        bytes[p * l + x] = Valor.DeC(b, TipoC.UChar);
                    }
                return Valor.DeReferencia(bytes);
            }
        }

        // O texto tambem vai para a tela de caracteres (o que os testes e o
        // diagnostico leem), como antes.
        if (obj.Lcd is { } texto && metodo is "print" or "println" or "write" or "setCursor")
            return ChamarOledComTexto(obj, texto, metodo, c, amb);

        // O resto e a Adafruit_GFX, no buffer.
        return ChamarTft(obj, metodo, c, amb);
    }

    /// <summary>print/println/write/setCursor: no buffer (a GFX) e na tela de caracteres.</summary>
    Valor ChamarOledComTexto(Objeto obj, Lcd texto, string metodo, Chamada c, Ambiente amb)
    {
        var args = c.Args.Select(a => Avaliar(a, amb)).ToList();
        var pronta = new Chamada(c.Alvo, [.. args.Select(a => (Expr)new Pronto(a, c.Linha))], c.Linha);
        if (metodo == "setCursor") texto.PosicionarCursor((int)((args.Count > 0 ? args[0].Num : 0) / 6), (int)((args.Count > 1 ? args[1].Num : 0) / 8));
        else if (metodo == "write") texto.Escrever(args.Count > 0 ? args[0].Mostrar() : "");
        else texto.Escrever(Imprimivel(args));
        return ChamarTft(obj, metodo, pronta, amb);
    }
}
