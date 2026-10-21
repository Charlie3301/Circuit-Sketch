namespace Simulador.App;

/// <summary>
/// Os displays coloridos e os mostradores (Alpha 25): a ILI9341 de 2,2", a
/// ST7789 de 1,54" e a ST7735 de 1,8" (nova) no tamanho real, com a barra
/// onde o modulo tem; e as bibliotecas novas pelo sketch -- a Adafruit_ST7735
/// (o initR() escolhe o painel), a Adafruit_SH110X (o OLED de 1,3"), a
/// TM1637Display e a LedControl (o MAX7219).
/// </summary>
public static partial class Autoteste
{
    /// <summary>Roda o setup() de um sketch compilado como projeto (com os #include do simulador) e devolve a placa.</summary>
    static (Maquina Maq, string Erro) RodarComInclude(string corpo, string antes)
    {
        var maq = new Maquina { Perfil = PerfilPlaca.Esp32 };
        maq.Reiniciar();
        try
        {
            var prog = Compilador.CompilarProjeto(new Projeto
            {
                Sketch = antes + Environment.NewLine + "void setup() { " + corpo + " }" + Environment.NewLine + "void loop() {}",
                Placa = "esp32-devkit-v1",
                Bibliotecas = [],
                DoSimulador = Acervo.EhDoSimulador,
            });
            var interp = new Interpretador(prog, maq);
            interp.Preparar();
            interp.Setup();
            return (maq, null);
        }
        catch (Exception e)
        {
            return (maq, e.Message);
        }
    }

    static void Tft_Desenho()
    {
        saida.AppendLine("TFTs: o tamanho real, as barras e a ST7735");

        static PinoDef P(PecaDef d, string n) => d.Pinos.First(p => p.Nome == n);
        const double Mm = 96 / 25.4;
        static string Ordem(PecaDef d) => string.Join(" ", d.Pinos.OrderBy(p => p.X).Select(p => p.Nome));

        var ili = Catalogo.Obter("ili9341");
        Conferir("ILI9341 2,2\": 40,1 x 67,2 mm, a barra de 9 embaixo com o VCC a 9,89 mm da esquerda",
            ili.Largura == 152 && ili.Altura == 254 && Ordem(ili) == "VCC GND CS RST D/C MOSI SCK LED MISO"
            && Math.Abs(P(ili, "VCC").X - 9.89 * Mm) < 0.2 && P(ili, "VCC").Y > ili.Altura * 0.95, Ordem(ili));
        var s89 = Catalogo.Obter("st7789");
        Conferir("ST7789 1,54\": 32 x 43,72 mm, os 8 pinos em cima, centrados (GND ... BLK)",
            s89.Largura == 121 && s89.Altura == 165 && Ordem(s89) == "GND VCC SCL SDA RES DC CS BLK"
            && s89.Pinos.All(p => p.Y < 3 * Mm) && Math.Abs((P(s89, "GND").X + P(s89, "BLK").X) / 2 - s89.Largura / 2.0) < 1, Ordem(s89));
        var s35 = Catalogo.Obter("st7735");
        Conferir("ST7735 1,8\" (nova): 34,5 x 58 mm, a barra de 8 embaixo (VCC, GND, CS, RESET, A0, SDA, SCK, LED), na categoria Saida",
            s35 is { Largura: 130, Altura: 219, Categoria: "Saida" } && Ordem(s35) == "VCC GND CS RESET A0 SDA SCK LED"
            && s35.Pinos.All(p => p.Y > s35.Altura * 0.9) && Pecas.Existe("st7735"), s35 is null ? "sem peca" : Ordem(s35));

        // ---- a Adafruit_ST7735: o initR() escolhe o painel ----
        {
            var (m, _) = RodarComInclude("Adafruit_ST7735 tft = Adafruit_ST7735(5, 2, 4); tft.initR(INITR_BLACKTAB); tft.fillScreen(ST77XX_RED);",
                                  "#include <Adafruit_ST7735.h>");
            var tela = m.Tela;
            bool vermelho = tela is { LarguraFisica: 128, AlturaFisica: 160 } && tela.Pixels.All(p => p == TelaGrafica.DeRgb565(0xF800));
            var (m2, _) = RodarComInclude("Adafruit_ST7735 tft = Adafruit_ST7735(5, 2, 4); tft.initR(INITR_144GREENTAB);", "#include <Adafruit_ST7735.h>");
            Conferir("ST7735: initR(INITR_BLACKTAB) e o de 1,8\" (128 x 160) e o fillScreen() pinta ele inteiro; o INITR_144GREENTAB, o de 128 x 128",
                vermelho && m2.Tela is { LarguraFisica: 128, AlturaFisica: 128 },
                $"{tela?.LarguraFisica}x{tela?.AlturaFisica} / {m2.Tela?.LarguraFisica}x{m2.Tela?.AlturaFisica}");
        }

        // ---- a Adafruit_SH110X (o OLED de 1,3"): begin(0x3C, true) e o mesmo desenho do SSD1306 ----
        {
            var (m, _) = RodarComInclude("Adafruit_SH1106G tela = Adafruit_SH1106G(128, 64, &Wire, -1); tela.begin(0x3C, true); "
                                  + "tela.clearDisplay(); tela.fillRect(0, 0, 10, 10, SH110X_WHITE); tela.display();",
                                  "#include <Adafruit_SH110X.h>");
            TelaGrafica painel;
            lock (m.PaineisOled) painel = m.PaineisOled.FirstOrDefault(p => p.Endereco == 0x3C && !p.Spi).Painel;
            int acesos = painel?.QuadroVisivel(m.AgoraUsLido / 1000).Count(p => p != TelaGrafica.CorApagada) ?? -1;
            Conferir("SH1106: o begin(0x3C, true) acende o painel no 0x3C, e o fillRect de 10 x 10 acende 100 pontos", acesos == 100, $"{acesos}");
        }

        // ---- a TM1637Display: as contas da biblioteca ----
        {
            var (m, _) = RodarComInclude("TM1637Display d(2, 3); d.setBrightness(5); d.showNumberDecEx(1234, 0b01000000, false); "
                                  + "TM1637Display e(4, 5); e.showNumberDec(-12); "
                                  + "TM1637Display f(12, 13); f.showNumberDec(7, true); "
                                  + "TM1637Display g(14, 15); uint8_t oi[] = { SEG_B | SEG_C | SEG_E | SEG_F | SEG_G, SEG_B | SEG_C }; g.setSegments(oi, 2, 1);",
                                  "#include <TM1637Display.h>");
            string S(int clk, int dio) => m.Mostradores.TryGetValue((clk, dio), out var t) ? string.Join(",", t.Segmentos.Take(4).Select(b => b.ToString("X2"))) : "-";
            Conferir("TM1637: o 1234 com os dois pontos (0x40 no dots) acende 1, 2 com o ponto, 3 e 4; o brilho fica 5",
                S(2, 3) == "06,DB,4F,66" && m.Mostradores[(2, 3)].Brilho == 5, S(2, 3));
            Conferir("TM1637: o -12 sai alinhado a direita com o menos (_, -, 1, 2); o 7 com zeros a esquerda, 0007",
                S(4, 5) == "00,40,06,5B" && S(12, 13) == "3F,3F,3F,07", $"{S(4, 5)} / {S(12, 13)}");
            Conferir("TM1637: o setSegments(oi, 2, 1) escreve a partir do digito 1 (o \"HI\")", S(14, 15) == "00,76,06,00", S(14, 15));
        }

        // ---- a LedControl (o MAX7219): comeca desligado, o shutdown(0, false) acende ----
        {
            var (m, _) = RodarComInclude("LedControl lc = LedControl(12, 11, 10, 1); bool antes = true; "
                                  + "lc.shutdown(0, false); lc.setIntensity(0, 8); lc.clearDisplay(0); "
                                  + "lc.setLed(0, 2, 3, true); lc.setRow(0, 0, 0xFF); lc.setColumn(0, 7, 0b10000001);",
                                  "#include <LedControl.h>");
            var mx = m.Matrizes.GetValueOrDefault((12, 11, 10));
            Conferir("MAX7219: o shutdown(0, false) liga, o setLed(0, 2, 3) acende a coluna 3 da linha 2, o setRow e o setColumn",
                mx is not null && !mx.Desligado[0] && mx.Brilho[0] == 8 && mx.Linhas[0][2] == 0x10 && mx.Linhas[0][0] == 0xFF
                && (mx.Linhas[0][7] & 0x01) != 0 && (mx.Linhas[0][1] & 0x01) == 0,
                mx is null ? "sem matriz" : string.Join(",", mx.Linhas[0].Select(b => b.ToString("X2"))));
            var (m2, _) = RodarComInclude("LedControl lc = LedControl(12, 11, 10, 1); lc.setLed(0, 0, 0, true);", "#include <LedControl.h>");
            Conferir("MAX7219: sem o shutdown(0, false) ele fica desligado (como a LedControl deixa)",
                m2.Matrizes.GetValueOrDefault((12, 11, 10)) is { } d0 && d0.Desligado[0]);
        }

        // Com CS_FOTOS apontando uma pasta, cada TFT sai em foto, desenhando.
        if (Environment.GetEnvironmentVariable("CS_FOTOS") is { Length: > 0 } fotos && System.IO.Directory.Exists(fotos))
        {
            foreach (var (tipo, l, a) in new[] { ("ili9341", 240, 320), ("st7789", 240, 240), ("st7735", 128, 160) })
            {
                var c = new Circuito();
                c.Pecas.Add(new PecaNoCircuito { Id = "tft", Tipo = tipo, X = 20, Y = 20 });
                var ed = new EditorCircuito { EmSimulacao = true };
                ed.Carregar(c);
                var maq = new Maquina { Perfil = PerfilPlaca.Esp32 };
                maq.Reiniciar();
                var tela = maq.CriarTela(l, a);
                tela.Iniciar();
                tela.Limpar(0x0010);
                for (int k = 0; k < 6; k++)
                    tela.Circulo(l / 2, a / 2, 10 + k * l / 14, new[] { 0xF800, 0xFFE0, 0x07E0, 0x07FF, 0x001F, 0xF81F }[k], false);
                tela.Retangulo(8, 8, l - 16, a / 8, 0xFFFF, true);
                tela.Mudou();
                ed.RefletirEstado(Simulador.App.Bancada.DeUmaSo(maq), new Netlist(c));
                FotografarEditor(ed, System.IO.Path.Combine(fotos, $"{tipo}.png"), 600, 900);
            }

            // Os OLEDs, cada um numa cor, com o I2C ligado na ESP32.
            foreach (var (tipo, cor, altura) in new[] { ("ssd1306-i2c", "amarelo-azul", 64), ("ssd1306", "branco", 64),
                                                       ("ssd1306-128x32", "azul", 32), ("sh1106", "branco", 64) })
            {
                var c = new Circuito();
                c.Pecas.Add(new PecaNoCircuito { Id = "placa", Tipo = "esp32-devkit-v1", X = 600, Y = 600, Usb = true });
                c.Pecas.Add(new PecaNoCircuito { Id = "oled", Tipo = tipo, Modelo = cor, X = 20, Y = 20 });
                if (tipo != "ssd1306")
                {
                    c.Fios.Add(new Fio { DePeca = "oled", DePino = "SDA", ParaPeca = "placa", ParaPino = "D21" });
                    c.Fios.Add(new Fio { DePeca = "oled", DePino = "SCL", ParaPeca = "placa", ParaPino = "D22" });
                }
                var ed = new EditorCircuito { EmSimulacao = true };
                ed.Carregar(c);
                var maq = new Maquina { Perfil = PerfilPlaca.Esp32 };
                maq.Reiniciar();
                maq.I2c.Begin(21, 22);
                var painel = maq.CriarPainelOled(128, altura, maq.I2c.Numero, 0x3C, spi: tipo == "ssd1306");
                painel.Iniciar();
                painel.Limpar(0);
                painel.Retangulo(0, 0, 128, 14, 1, true);
                painel.Circulo(30, altura / 2 + 8, altura / 4, 1, false);
                painel.Linha(60, altura - 4, 124, 18, 1);
                painel.Retangulo(64, 20, 50, altura - 26, 1, false);
                painel.Mudou();
                ed.RefletirEstado(Simulador.App.Bancada.DeUmaSo(maq), new Netlist(c));
                FotografarEditor(ed, System.IO.Path.Combine(fotos, $"{tipo}-{cor}.png"), 560, 560);
            }
        }
    }
}
