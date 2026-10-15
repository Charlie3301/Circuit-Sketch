namespace Simulador.App;

/// <summary>
/// A familia dos LEDs redesenhada (Alpha 25, pecas-leds.py): o display de 7
/// segmentos acende o segmento certo (catodo ou anodo comum), a barra acende
/// cada LED dela, e o NeoPixel, o anel e a matriz mostram a cor de cada
/// pixel -- encadeados pelo DOUT, cada um continuando a fita de onde o
/// anterior parou, e apagados sem alimentacao.
/// </summary>
public static partial class Autoteste
{
    static void Leds_Familia()
    {
        saida.AppendLine("LEDs: o que acende em cada peca");

        var c = new Circuito();
        var placa = new PecaNoCircuito { Id = "esp", Tipo = "esp32-devkit-v1", X = 30, Y = 40, Usb = true };
        c.Pecas.Add(placa);
        void Ligar(string a, string pa, string b, string pb, string cor = "#00A651")
            => c.Fios.Add(new Fio { DePeca = a, DePino = pa, ParaPeca = b, ParaPino = pb, Cor = cor });

        // os LEDs de 5 mm, um de cada cor
        string[] cores = ["vermelho", "verde", "amarelo", "azul", "branco", "laranja", "roxo"];
        string[] pinosDosLeds = ["D13", "D12", "D14", "D27", "D26", "D25", "D33"];
        for (int k = 0; k < cores.Length; k++)
        {
            c.Pecas.Add(new PecaNoCircuito { Id = $"led{k}", Tipo = "led", Cor = cores[k], X = 190 + k * 46, Y = 60 });
            Ligar("esp", pinosDosLeds[k], $"led{k}", "A");
            Ligar($"led{k}", "C", "esp", "GND.2", "#1A1A1A");
        }
        c.Pecas.Add(new PecaNoCircuito { Id = "rgb", Tipo = "rgb-led", X = 520, Y = 50 });
        Ligar("esp", "D21", "rgb", "R"); Ligar("esp", "D22", "rgb", "G"); Ligar("esp", "D23", "rgb", "B");
        Ligar("rgb", "COM", "esp", "GND.1", "#1A1A1A");

        // o 7 segmentos mostrando "3": A B C D G acesos, E F DP apagados
        c.Pecas.Add(new PecaNoCircuito { Id = "seg", Tipo = "7segment", X = 600, Y = 40 });
        foreach (var (nomeSeg, pino) in new[] { ("A", "D15"), ("B", "D2"), ("C", "D4"), ("D", "RX2"), ("G", "TX2"), ("E", "D5"), ("F", "D18"), ("DP", "D19") })
            Ligar("esp", pino, "seg", nomeSeg);
        Ligar("seg", "COM.1", "esp", "GND.1", "#1A1A1A");

        // a barra: seis LEDs no 3V3 (o trilho), os outros quatro soltos
        c.Pecas.Add(new PecaNoCircuito { Id = "barra", Tipo = "led-bar-graph", Cor = "verde", X = 680, Y = 40 });
        for (int k = 1; k <= 6; k++)
        {
            Ligar("esp", "3V3", "barra", $"A{k}", "#D23B33");
            Ligar("barra", $"C{k}", "esp", "GND.1", "#1A1A1A");
        }

        // NeoPixel -> anel -> matriz, todos na fita do GPIO 32
        c.Pecas.Add(new PecaNoCircuito { Id = "np", Tipo = "neopixel", X = 760, Y = 60 });
        c.Pecas.Add(new PecaNoCircuito { Id = "anel", Tipo = "led-ring", X = 190, Y = 220 });
        c.Pecas.Add(new PecaNoCircuito { Id = "mat", Tipo = "neopixel-matrix", X = 420, Y = 200 });
        Ligar("esp", "D32", "np", "DIN");
        Ligar("np", "DOUT", "anel", "DIN");
        Ligar("anel", "DOUT", "mat", "DIN");
        Ligar("np", "VDD", "esp", "VIN", "#D23B33"); Ligar("np", "VSS", "esp", "GND.2", "#1A1A1A");
        Ligar("anel", "VCC", "esp", "VIN", "#D23B33"); Ligar("anel", "GND", "esp", "GND.2", "#1A1A1A");
        Ligar("mat", "VCC", "esp", "VIN", "#D23B33"); Ligar("mat", "GND", "esp", "GND.2", "#1A1A1A");

        var ed = new EditorCircuito { EmSimulacao = true };
        ed.Carregar(c);
        var maq = new Maquina { Perfil = PerfilPlaca.Esp32 };
        maq.Reiniciar();
        var perfil = PerfilPlaca.Esp32;
        foreach (string p in pinosDosLeds) maq.Pwm[perfil.Numero(p)] = 255;
        maq.Pwm[21] = 255; maq.Pwm[23] = 140;                       // o RGB roxo
        foreach (string p in new[] { "D15", "D2", "D4", "RX2", "TX2" }) maq.Pwm[perfil.Numero(p)] = 255;
        var fita = new Fita(1 + 16 + 64);
        fita.Definir(0, Fita.Montar(255, 0, 0));
        for (int k = 0; k < 16; k++) fita.Definir(1 + k, Fita.Hsv(k * 65536L / 16, 255, 255));
        for (int lin = 0; lin < 8; lin++)
            for (int col = 0; col < 8; col++)
                fita.Definir(17 + lin * 8 + col, lin == col || lin + col == 7 ? Fita.Montar(40, 120, 255) : (lin + col) % 3 == 0 ? Fita.Montar(255, 160, 0) : 0);
        fita.Mostrar();
        maq.Fitas[32] = fita;

        var b = Simulador.App.Bancada.DeUmaSo(maq);
        var rede = new Netlist(c);
        ed.RefletirEstado(b, rede);

        var seg = ed.LuzesDoLedParaTeste("seg");
        string acesos = string.Join("", DesenhoDosLeds.Segmentos["7segment"].Keys.Where((_, i) => i < seg.Length && seg[i] > 0.3).OrderBy(n => n));
        Conferir("7 segmentos (catodo comum): mostra o 3 -- acendem A, B, C, D e G, e so eles", acesos == "ABCDG", acesos);

        var barra = ed.LuzesDoLedParaTeste("barra");
        Conferir("barra de LEDs: os seis no 3V3 acendem, os quatro soltos nao",
            barra.Length == 10 && barra.Take(6).All(v => v > 0.3) && barra.Skip(6).All(v => v == 0),
            string.Join("/", barra.Select(v => v.ToString("0.0"))));

        var np = ed.CoresDosPixelsParaTeste("np");
        var anel = ed.CoresDosPixelsParaTeste("anel");
        var mat = ed.CoresDosPixelsParaTeste("mat");
        static bool Perto(System.Windows.Media.Color a, int rgb)
        {
            int r = (rgb >> 16) & 0xFF, g = (rgb >> 8) & 0xFF, bb = rgb & 0xFF, m = Math.Max(r, Math.Max(g, bb));
            return m > 0 && Math.Abs(a.R - r * 255 / m) < 8 && Math.Abs(a.G - g * 255 / m) < 8 && Math.Abs(a.B - bb * 255 / m) < 8;
        }
        Conferir("NeoPixel: o primeiro LED da fita, vermelho", np.Length == 1 && Perto(np[0], fita.Cores[0]), np.FirstOrDefault().ToString());
        Conferir("anel encadeado no DOUT do NeoPixel: continua a fita (os LEDs 1 a 16), o 0 em cima",
            anel.Length == 16 && Enumerable.Range(0, 16).All(k => Perto(anel[k], fita.Cores[1 + k])),
            string.Join(" ", anel.Take(4)));
        Conferir("matriz encadeada no anel: os LEDs 17 a 80, linha por linha (as diagonais azuis)",
            mat.Length == 64 && Enumerable.Range(0, 64).All(k => fita.Cores[17 + k] == 0 ? mat[k] == System.Windows.Media.Colors.Transparent : Perto(mat[k], fita.Cores[17 + k])),
            string.Join(" ", mat.Take(9)));

        // Com CS_FOTOS apontando uma pasta, o circuito aceso sai em foto.
        if (Environment.GetEnvironmentVariable("CS_FOTOS") is { Length: > 0 } fotos && System.IO.Directory.Exists(fotos))
            FotografarEditor(ed, System.IO.Path.Combine(fotos, "leds-acesos.png"), 2600, 1520);

        // sem o 5 V na matriz, ela nao acende; sem o fio do DIN, o NeoPixel tambem nao
        c.Fios.RemoveAll(f => f.DePeca == "mat" && f.DePino == "VCC");
        c.Fios.RemoveAll(f => f.ParaPeca == "np" && f.ParaPino == "DIN");
        ed.Carregar(c);
        rede = new Netlist(c);
        ed.RefletirEstado(b, rede);
        Conferir("sem o VCC a matriz apaga; sem o DIN o NeoPixel (e o que vem depois dele) apaga",
            ed.LuzesDoLedParaTeste("mat").All(v => v == 0) && ed.LuzesDoLedParaTeste("np").All(v => v == 0)
            && ed.LuzesDoLedParaTeste("anel").All(v => v == 0));

        // anodo comum: o comum no 3V3, o segmento acende com o pino em LOW
        var c2 = new Circuito();
        c2.Pecas.Add(new PecaNoCircuito { Id = "esp", Tipo = "esp32-devkit-v1", X = 30, Y = 40, Usb = true });
        c2.Pecas.Add(new PecaNoCircuito { Id = "seg", Tipo = "7segment", X = 300, Y = 40 });
        foreach (var (s, pino) in new[] { ("A", "D15"), ("B", "D2"), ("C", "D4"), ("D", "RX2"), ("E", "TX2"), ("F", "D5"), ("G", "D18"), ("DP", "D19") })
            c2.Fios.Add(new Fio { DePeca = "esp", DePino = pino, ParaPeca = "seg", ParaPino = s });
        c2.Fios.Add(new Fio { DePeca = "seg", DePino = "COM.2", ParaPeca = "esp", ParaPino = "3V3" });
        var ed2 = new EditorCircuito { EmSimulacao = true };
        ed2.Carregar(c2);
        var maq2 = new Maquina { Perfil = PerfilPlaca.Esp32 };
        maq2.Reiniciar();
        // o "1": B e C em LOW (acesos), o resto em HIGH (apagados)
        foreach (string p in new[] { "D15", "RX2", "TX2", "D5", "D18", "D19" }) maq2.Pwm[perfil.Numero(p)] = 255;
        ed2.RefletirEstado(Simulador.App.Bancada.DeUmaSo(maq2), new Netlist(c2));
        var seg2 = ed2.LuzesDoLedParaTeste("seg");
        string acesos2 = string.Join("", DesenhoDosLeds.Segmentos["7segment"].Keys.Where((_, i) => i < seg2.Length && seg2[i] > 0.3).OrderBy(n => n));
        Conferir("7 segmentos (anodo comum no 3V3, pelo COM.2): o 1 com B e C em LOW", acesos2 == "BC", acesos2);
    }
}
