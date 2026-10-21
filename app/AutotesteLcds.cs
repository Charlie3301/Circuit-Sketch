namespace Simulador.App;

/// <summary>
/// Os LCDs de caracteres redesenhados (Alpha 25): o tamanho e os pinos pelos
/// desenhos dos fabricantes (a barra de 16 pinos em cima), a matriz de pontos
/// 5 x 8 no lugar das letras da fonte do Windows, o createChar() que agora
/// aparece, o grau do "(char)223", a luz de fundo e o noDisplay(), e as duas
/// cores (verde e azul).
/// </summary>
public static partial class Autoteste
{
    static void Lcd_Pontos()
    {
        saida.AppendLine("LCDs: o tamanho real, a matriz de pontos e o createChar");

        static PinoDef P(PecaDef d, string n) => d.Pinos.First(p => p.Nome == n);
        const double Mm = 96 / 25.4;

        // ---- o tamanho real e a barra em cima, com o pino 1 onde o desenho diz ----
        {
            var d16 = Catalogo.Obter("lcd1602");
            var d20 = Catalogo.Obter("lcd2004");
            bool barra16 = Math.Abs(P(d16, "VSS").X - 8.0 * Mm) < 0.2 && Math.Abs(P(d16, "VSS").Y - 2.5 * Mm) < 0.2
                           && Math.Abs(P(d16, "K").X - P(d16, "VSS").X - 38.1 * Mm) < 0.3;
            bool barra20 = Math.Abs(P(d20, "VSS").X - 10.0 * Mm) < 0.2 && Math.Abs(P(d20, "VSS").Y - 2.5 * Mm) < 0.2;
            var i2c = Catalogo.Obter("lcd1602-i2c");
            string ordem = string.Join(" ", i2c.Pinos.OrderBy(p => p.X).Select(p => p.Nome));
            Conferir("LCD: 16x2 de 80 x 36 mm e 20x4 de 98 x 60 mm, a barra de 16 pinos em cima (pino 1 a 8 e a 10 mm)",
                d16.Largura == 302 && d16.Altura == 136 && d20.Largura == 370 && d20.Altura == 227 && barra16 && barra20,
                $"{d16.Largura}x{d16.Altura} {d20.Largura}x{d20.Altura}, VSS ({P(d16, "VSS").X},{P(d16, "VSS").Y})");
            Conferir("LCD I2C: os 4 pinos do adaptador em cima, a esquerda (GND, VCC, SDA, SCL)",
                ordem == "GND VCC SDA SCL" && i2c.Pinos.All(p => Math.Abs(p.Y - 2.5 * Mm) < 0.2), ordem);
            Conferir("LCD: o desenho azul de cada um existe",
                new[] { "lcd1602-azul", "lcd2004-azul", "lcd1602-i2c-azul", "lcd2004-i2c-azul" }.All(Pecas.Existe));
        }

        // ---- a matriz de pontos ----
        var c = new Circuito();
        c.Pecas.Add(new PecaNoCircuito { Id = "placa", Tipo = "esp32-devkit-v1", X = 30, Y = 40, Usb = true });
        c.Pecas.Add(new PecaNoCircuito { Id = "lcd", Tipo = "lcd1602", X = 400, Y = 40 });
        var ed = new EditorCircuito { EmSimulacao = true };
        ed.Carregar(c);
        var maq = new Maquina { Perfil = PerfilPlaca.Esp32 };
        maq.Reiniciar();
        var b = Simulador.App.Bancada.DeUmaSo(maq);
        var rede = new Netlist(c);
        var lcd = maq.CriarLcd();
        lcd.Begin(16, 2);
        int Pontos() { ed.RefletirEstado(b, rede); return ed.PontosAcesosDoLcdParaTeste("lcd"); }

        lcd.Escrever("H");
        int h = Pontos();
        Conferir("LCD: o H acende os 17 pontos da letra (as duas colunas de 7 e o traco do meio)", h == 17, $"{h} pontos");

        lcd.DefinirGlifo(0, [0x00, 0x0A, 0x1F, 0x1F, 0x0E, 0x04, 0x00, 0x00]);
        lcd.Escrever("\u0000");
        int comCoracao = Pontos();
        Conferir("LCD: o createChar(0, coracao) e o write(0) mostram o coracao (mais 16 pontos)", comCoracao == 17 + 16, $"{comCoracao} pontos");

        lcd.Limpar();
        lcd.Escrever(((char)223).ToString());
        int grau = Pontos();
        Conferir("LCD: o (char)223 e o grau da ROM do HD44780 (8 pontos no alto), e nao uma letra da fonte do PC", grau == 8, $"{grau} pontos");

        lcd.Escrever("OK");
        lcd.Mostrar(false);
        int apagado = Pontos();
        lcd.Mostrar(true);
        int volta = Pontos();
        Conferir("LCD: o noDisplay() apaga os caracteres e o display() traz de volta", apagado == 0 && volta > grau, $"{apagado} / {volta}");

        lcd.Luz(false);
        Pontos();
        bool semLuz = ed.LcdSemLuzParaTeste("lcd");
        lcd.Luz(true);
        Pontos();
        Conferir("LCD: o noBacklight() escurece o vidro, e o backlight() acende de novo", semLuz && !ed.LcdSemLuzParaTeste("lcd"));

        // Com CS_FOTOS apontando uma pasta, os dois de cada cor saem em foto, escrevendo.
        if (Environment.GetEnvironmentVariable("CS_FOTOS") is { Length: > 0 } fotos && System.IO.Directory.Exists(fotos))
        {
            foreach (var (tipo, cor) in new[] { ("lcd1602", "verde"), ("lcd2004-i2c", "azul") })
            {
                var cf = new Circuito();
                cf.Pecas.Add(new PecaNoCircuito { Id = "lcd", Tipo = tipo, Modelo = cor, X = 20, Y = 20 });
                var edf = new EditorCircuito { EmSimulacao = true };
                edf.Carregar(cf);
                var mf = new Maquina { Perfil = PerfilPlaca.Esp32 };
                mf.Reiniciar();
                var l = mf.CriarLcd();
                bool grande = tipo.StartsWith("lcd2004");
                l.Begin(grande ? 20 : 16, grande ? 4 : 2);
                l.DefinirGlifo(0, [0x00, 0x0A, 0x1F, 0x1F, 0x0E, 0x04, 0x00, 0x00]);
                l.Escrever("CircuitSketch \u0000");
                l.PosicionarCursor(0, 1);
                l.Escrever("Temp: 24.5" + (char)223 + "C");
                if (grande)
                {
                    l.PosicionarCursor(0, 2);
                    l.Escrever("Umidade: 61%");
                    l.PosicionarCursor(0, 3);
                    l.Escrever("ABCDEFGHIJ0123456789");
                }
                edf.RefletirEstado(Simulador.App.Bancada.DeUmaSo(mf), new Netlist(cf));
                FotografarEditor(edf, System.IO.Path.Combine(fotos, $"{tipo}-{cor}.png"), grande ? 1700 : 1400, grande ? 1100 : 760);
            }
        }

        // ---- pelo sketch: o createChar, o write(0) e o noBacklight() do LiquidCrystal_I2C ----
        {
            var (m, _) = RodarCpp(
                "LiquidCrystal_I2C lcd(0x27, 16, 2); lcd.init(); lcd.createChar(0, coracao); "
                + "lcd.setCursor(0, 0); lcd.write(0); lcd.print(\"A\"); lcd.noBacklight();",
                "#include <LiquidCrystal_I2C.h>\nbyte coracao[8] = {0x00, 0x0A, 0x1F, 0x1F, 0x0E, 0x04, 0x00, 0x00};");
            var l = m.Lcd;
            Conferir("sketch: lcd.createChar(0, coracao) guarda o desenho, lcd.write(0) escreve o caractere 0 (e nao o texto \"0\")",
                l is not null && l.Glifos[0][1] == 0x0A && l.Glifos[0][3] == 0x1F && l.LinhaTexto(0).StartsWith("\u0000A"),
                l is null ? "sem LCD" : $"[{l.LinhaTexto(0).Replace("\u0000", "\\0")}]");
            Conferir("sketch: lcd.noBacklight() apaga a luz de fundo", l is { LuzDeFundo: false });
        }
    }
}
