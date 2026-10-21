namespace Simulador.App;

/// <summary>
/// A Logica da Alpha 25: os simbolos redesenhados com os pinos no mesmo lugar,
/// os chips 74HC (a pinagem da ficha, a tabela de cada porta, o 74HC74 e o
/// 74HC157, e nada sem o VCC e o GND), e as saidas -- da porta ideal e do chip
/// -- acendendo um LED pelo resistor.
/// </summary>
public static partial class Autoteste
{
    static void Logica_Chips()
    {
        saida.AppendLine("logica: os chips 74HC e as saidas que acendem LED");
        const double mm = 96 / 25.4;
        static bool Perto(double a, double b, double folga = 0.06) => Math.Abs(a - b) <= folga;
        static void Ligar(Circuito c, string a, string pa, string b, string pb)
            => c.Fios.Add(new Fio { DePeca = a, DePino = pa, ParaPeca = b, ParaPino = pb });
        static Maquina Placa() { var maq = new Maquina { Perfil = PerfilPlaca.Esp32 }; maq.Reiniciar(); return maq; }
        static Circuito ComChip(string tipo, bool alimentado = true)
        {
            var c = new Circuito();
            c.Pecas.Add(new PecaNoCircuito { Id = "placa", Tipo = "esp32-devkit-v1", X = 0, Y = 0, Usb = true });
            c.Pecas.Add(new PecaNoCircuito { Id = "vcc", Tipo = "vcc-symbol", X = 600, Y = 0 });
            c.Pecas.Add(new PecaNoCircuito { Id = "gnd", Tipo = "gnd-symbol", X = 600, Y = 200 });
            c.Pecas.Add(new PecaNoCircuito { Id = "ci", Tipo = tipo, X = 300, Y = 300 });
            if (alimentado) { Ligar(c, "ci", "VCC", "placa", "3V3"); Ligar(c, "ci", "GND", "placa", "GND.1"); }
            return c;
        }
        static void Nivel(Circuito c, string pino, int v) => Ligar(c, v == 1 ? "vcc" : "gnd", v == 1 ? "VCC" : "GND", "ci", pino);
        static Logica Rodar(Circuito c, Dictionary<string, int> memoria = null)
            => Logica.Avaliar(c, new Netlist(c), Simulador.App.Bancada.DeUmaSo(Placa()), memoria ?? [], 0);

        // ---- os desenhos: os simbolos no lugar de sempre, os chips no DIP de 0,3" ----
        string[] chips = ["74hc00", "74hc02", "74hc04", "74hc08", "74hc32", "74hc86", "74hc74", "74hc157",
                          "74hc7266", "74hc14", "74hc10", "74hc11", "74hc27", "74hc20", "74hc21", "74hc125"];
        string[] simbolos = ["not-gate", "and-gate", "or-gate", "xor-gate", "nand-gate", "nor-gate", "xnor-gate", "mux-2",
                             "flip-flop-d", "flip-flop-sr", "flip-flop-dsr", "clock-generator", "logic-analyzer",
                             "buffer-gate", "buffer-tristate", "and-gate-3", "nand-gate-3", "or-gate-3", "nor-gate-3",
                             "xor-gate-3", "xnor-gate-3", "flip-flop-jk", "flip-flop-t", "latch-sr", "latch-d"];
        var semDesenho = chips.Concat(simbolos).Where(t => !Pecas.Existe(t) || Catalogo.Obter(t) is not { Categoria: "Logica" }).ToList();
        Conferir("logica: os 25 simbolos e os 16 chips 74HC tem desenho e estao na categoria Logica", semDesenho.Count == 0, string.Join(", ", semDesenho));
        var and = Catalogo.Obter("and-gate");
        Conferir("logica: a porta continua com o A, o B e o Q nos mesmos furos (0,1\" do Wokwi)",
            and.Pinos.Select(p => (p.Nome, Math.Round(p.X / mm, 2), Math.Round(p.Y / mm, 2))).SequenceEqual([("A", 0.0, 3.81), ("B", 0.0, 8.89), ("Q", 17.78, 6.35)]));
        var errados = chips.Where(t =>
        {
            var d = Catalogo.Obter(t);
            int n = t == "74hc157" ? 16 : 14;
            var ys = d.Pinos.Select(p => Math.Round(p.Y / mm, 2)).Distinct().OrderBy(y => y).ToArray();
            var xs = d.Pinos.Select(p => p.X / mm).Distinct().OrderBy(x => x).ToArray();
            var p1 = d.Pinos[0];
            return d.Pinos.Count != n || ys.Length != 2 || !Perto(ys[1] - ys[0], 7.62)
                   || xs.Zip(xs.Skip(1)).Any(par => !Perto(par.Second - par.First, 2.54))
                   || !Perto(p1.X, xs.Min() * mm, 0.1) || !Perto(p1.Y / mm, ys[1])          // o pino 1 embaixo a esquerda
                   || d.Pinos[n - 1].Nome != "VCC" || d.Pinos[n / 2 - 1].Nome != "GND"
                   || d.Categoria != "Logica";
        }).ToList();
        Conferir("74HC: DIP de 14 (o 157, de 16) com as fileiras a 7,62 mm, o pino 1 embaixo a esquerda, GND no 7 (8) e VCC no 14 (16), na categoria Logica",
            errados.Count == 0, string.Join(", ", errados));

        // ---- a tabela de cada porta, em cada chip ----
        static int Conta(string f, int a, int b) => f switch
        {
            "74hc00" => 1 - (a & b), "74hc02" => 1 - (a | b), "74hc08" => a & b, "74hc32" => a | b, _ => a ^ b,
        };
        var erradas = new List<string>();
        foreach (string tipo in new[] { "74hc00", "74hc02", "74hc08", "74hc32", "74hc86" })
            for (int k = 1; k <= 4; k++)
                foreach (var (a, b) in new[] { (0, 0), (0, 1), (1, 0), (1, 1) })
                {
                    var c = ComChip(tipo);
                    Nivel(c, $"{k}A", a); Nivel(c, $"{k}B", b);
                    int y = Rodar(c).NivelDoPino("ci", $"{k}Y");
                    if (y != Conta(tipo, a, b)) erradas.Add($"{tipo} porta {k} ({a},{b}) = {y}");
                }
        {
            var c = ComChip("74hc04");
            for (int k = 1; k <= 6; k++) Nivel(c, $"{k}A", k % 2);
            var l = Rodar(c);
            for (int k = 1; k <= 6; k++)
                if (l.NivelDoPino("ci", $"{k}Y") != 1 - k % 2) erradas.Add($"74hc04 porta {k}");
        }
        Conferir("74HC00/02/08/32/86: as quatro portas de cada um batem com a tabela; o 74HC04 inverte as seis",
            erradas.Count == 0, string.Join("; ", erradas.Take(6)));

        // ---- os chips que faltavam: XNOR, Schmitt, 3 e 4 entradas, tri-state ----
        static int ContaN(string f, int[] e) => f switch
        {
            "74hc10" or "74hc20" => e.All(v => v == 1) ? 0 : 1,
            "74hc11" or "74hc21" => e.All(v => v == 1) ? 1 : 0,
            "74hc27" => e.Any(v => v == 1) ? 0 : 1,
            _ => e[0] == e[1] ? 1 : 0,                         // 74hc7266
        };
        var outras = new List<string>();
        foreach (var (tipo, portas, n) in new[] { ("74hc10", 3, 3), ("74hc11", 3, 3), ("74hc27", 3, 3), ("74hc20", 2, 4), ("74hc21", 2, 4), ("74hc7266", 4, 2) })
            for (int k = 1; k <= portas; k++)
                for (int bits = 0; bits < 1 << n; bits++)
                {
                    var c = ComChip(tipo);
                    int[] e = [.. Enumerable.Range(0, n).Select(i => bits >> i & 1)];
                    for (int i = 0; i < n; i++) Nivel(c, $"{k}{"ABCD"[i]}", e[i]);
                    int y = Rodar(c).NivelDoPino("ci", $"{k}Y");
                    if (y != ContaN(tipo, e)) outras.Add($"{tipo} porta {k} ({string.Join("", e)}) = {y}");
                }
        {
            var c = ComChip("74hc14");
            for (int k = 1; k <= 6; k++) Nivel(c, $"{k}A", k % 2);
            var l = Rodar(c);
            for (int k = 1; k <= 6; k++)
                if (l.NivelDoPino("ci", $"{k}Y") != 1 - k % 2) outras.Add($"74hc14 porta {k}");
        }
        Conferir("74HC10/11/27 (3 entradas), 20/21 (4 entradas) e 7266 (XNOR): todas as combinacoes de todas as portas; o 74HC14 inverte as seis",
            outras.Count == 0, string.Join("; ", outras.Take(6)));
        {
            int Y(int oe, int a)
            {
                var c = ComChip("74hc125");
                Nivel(c, "1OE", oe); Nivel(c, "1A", a);
                return Rodar(c).NivelDoPino("ci", "1Y");
            }
            Conferir("74HC125: com o OE em LOW o Y segue o A; em HIGH a saida fica solta (alta impedancia)",
                Y(0, 1) == 1 && Y(0, 0) == 0 && Y(1, 1) == Logica.Indefinido, $"{Y(0, 1)} {Y(0, 0)} {Y(1, 1)}");
        }

        // ---- as portas ideais novas: o buffer, o tri-state e as de tres entradas ----
        {
            static Circuito Ideal(string tipo, params (string Pino, int Nivel)[] entradas)
            {
                var c = new Circuito();
                c.Pecas.Add(new PecaNoCircuito { Id = "placa", Tipo = "esp32-devkit-v1", X = 0, Y = 0, Usb = true });
                c.Pecas.Add(new PecaNoCircuito { Id = "vcc", Tipo = "vcc-symbol", X = 600, Y = 0 });
                c.Pecas.Add(new PecaNoCircuito { Id = "gnd", Tipo = "gnd-symbol", X = 600, Y = 200 });
                c.Pecas.Add(new PecaNoCircuito { Id = "ci", Tipo = tipo, X = 300, Y = 300 });
                foreach (var (pino, v) in entradas) Nivel(c, pino, v);
                return c;
            }
            static int Q(string tipo, params (string, int)[] e) => Rodar(Ideal(tipo, e)).NivelDoPino("ci", "Q");
            var ruins = new List<string>();
            foreach (var (tipo, f) in new (string, Func<int, int, int, int>)[]
            {
                ("and-gate-3", (a, b, c) => a & b & c), ("nand-gate-3", (a, b, c) => 1 - (a & b & c)),
                ("or-gate-3", (a, b, c) => a | b | c), ("nor-gate-3", (a, b, c) => 1 - (a | b | c)),
                ("xor-gate-3", (a, b, c) => a ^ b ^ c), ("xnor-gate-3", (a, b, c) => 1 - (a ^ b ^ c)),
            })
                for (int bits = 0; bits < 8; bits++)
                {
                    int a = bits & 1, b = bits >> 1 & 1, c = bits >> 2 & 1;
                    int q = Q(tipo, ("A", a), ("B", b), ("C", c));
                    if (q != f(a, b, c)) ruins.Add($"{tipo}({a}{b}{c}) = {q}");
                }
            Conferir("logica: as portas de tres entradas (AND, NAND, OR, NOR, XOR, XNOR) batem com a tabela", ruins.Count == 0, string.Join("; ", ruins.Take(6)));
            Conferir("logica: o BUFFER repete o A; o tri-state repete com o EN em HIGH e solta a saida com ele em LOW",
                Q("buffer-gate", ("A", 1)) == 1 && Q("buffer-gate", ("A", 0)) == 0
                && Q("buffer-tristate", ("A", 1), ("EN", 1)) == 1 && Q("buffer-tristate", ("A", 1), ("EN", 0)) == Logica.Indefinido);

            // os sequenciais novos: varios quadros com a mesma memoria
            var mem = new Dictionary<string, int>();
            int Passo(string tipo, params (string, int)[] e) => Rodar(Ideal(tipo, e), mem).NivelDoPino("ci", "Q");
            var jk = new[] { Passo("flip-flop-jk", ("J", 1), ("K", 0), ("CLK", 0)), Passo("flip-flop-jk", ("J", 1), ("K", 0), ("CLK", 1)),
                             Passo("flip-flop-jk", ("J", 1), ("K", 1), ("CLK", 0)), Passo("flip-flop-jk", ("J", 1), ("K", 1), ("CLK", 1)),
                             Passo("flip-flop-jk", ("J", 1), ("K", 1), ("CLK", 0)), Passo("flip-flop-jk", ("J", 1), ("K", 1), ("CLK", 1)),
                             Passo("flip-flop-jk", ("J", 0), ("K", 1), ("CLK", 0)), Passo("flip-flop-jk", ("J", 1), ("K", 1), ("CLK", 0)) };
            mem.Clear();
            var t = new[] { Passo("flip-flop-t", ("T", 1), ("CLK", 0)), Passo("flip-flop-t", ("T", 1), ("CLK", 1)),
                            Passo("flip-flop-t", ("T", 1), ("CLK", 0)), Passo("flip-flop-t", ("T", 1), ("CLK", 1)),
                            Passo("flip-flop-t", ("T", 0), ("CLK", 0)), Passo("flip-flop-t", ("T", 0), ("CLK", 1)) };
            mem.Clear();
            var sr = new[] { Passo("latch-sr", ("S", 1), ("R", 0)), Passo("latch-sr", ("S", 0), ("R", 0)),
                             Passo("latch-sr", ("S", 0), ("R", 1)), Passo("latch-sr", ("S", 0), ("R", 0)) };
            mem.Clear();
            var ld = new[] { Passo("latch-d", ("D", 1), ("EN", 1)), Passo("latch-d", ("D", 0), ("EN", 0)),
                             Passo("latch-d", ("D", 0), ("EN", 1)), Passo("latch-d", ("D", 1), ("EN", 0)) };
            string Texto(int[] v) => string.Concat(v);
            Conferir("flip-flop JK: J sobe o Q, J e K juntos invertem a cada subida do CLK, e sem borda nada muda (0 1 1 0 0 1 1 1)",
                Texto(jk) == "01100111", Texto(jk));
            Conferir("flip-flop T: com o T em HIGH o Q inverte a cada subida; com ele em LOW, fica (0 1 1 0 0 0)", Texto(t) == "011000", Texto(t));
            Conferir("latch SR: o S liga, o R desliga, e sem os dois o Q guarda (1 1 0 0)", Texto(sr) == "1100", Texto(sr));
            Conferir("latch D: com o EN em HIGH o Q segue o D; com ele em LOW, guarda (1 1 0 0)", Texto(ld) == "1100", Texto(ld));
        }

        // ---- o BUFFER do Wokwi entra e sai com o nome deles ----
        {
            const string diagrama = """
            {
              "version": 1,
              "parts": [ { "type": "wokwi-gate-buffer", "id": "buf1", "top": 0, "left": 0, "attrs": {} } ],
              "connections": []
            }
            """;
            var ci = ImportarWokwi.Ler(diagrama).Circuito;
            string volta = ExportarWokwi.Gerar(ci);
            Conferir("wokwi: o wokwi-gate-buffer vira a porta BUFFER, e volta com o mesmo nome",
                ci.Pecas.FirstOrDefault()?.Tipo == "buffer-gate" && volta.Contains("wokwi-gate-buffer"), ci.Pecas.FirstOrDefault()?.Tipo ?? "(nada)");
        }

        {
            var c = ComChip("74hc08", alimentado: false);
            Nivel(c, "1A", 1); Nivel(c, "1B", 1);
            int semVcc = Rodar(c).NivelDoPino("ci", "1Y");
            Conferir("74HC08: sem o VCC e o GND ligados o chip nao faz nada (a saida fica solta)", semVcc == Logica.Indefinido, $"{semVcc}");
        }

        // ---- o 74HC74: a subida do CLK guarda o D; PRE e CLR em LOW mandam ----
        {
            var memoria = new Dictionary<string, int>();
            int Q(int clk, int d, int pre = 1, int clr = 1, string qual = "1Q")
            {
                var c = ComChip("74hc74");
                Nivel(c, "1CLK", clk); Nivel(c, "1D", d); Nivel(c, "1PRE", pre); Nivel(c, "1CLR", clr);
                return Rodar(c, memoria).NivelDoPino("ci", qual);
            }
            int parado = Q(0, 1);
            int subiu = Q(1, 1);
            int segura = Q(1, 0);              // o D muda sem borda: o Q fica
            int limpo = Q(1, 0, clr: 0);
            int ambos = Q(0, 0, pre: 0, clr: 0, qual: "1NQ");
            Conferir("74HC74: o Q pega o D so na subida do CLK, o CLR em LOW zera, e PRE e CLR juntos poem o Q barrado em HIGH",
                parado == 0 && subiu == 1 && segura == 1 && limpo == 0 && ambos == 1, $"{parado}{subiu}{segura}{limpo}{ambos}");
        }

        // ---- o 74HC157: o S escolhe, o G em HIGH desliga ----
        {
            int Y(int s, int g)
            {
                var c = ComChip("74hc157");
                Nivel(c, "S", s); Nivel(c, "G", g); Nivel(c, "1A", 0); Nivel(c, "1B", 1);
                return Rodar(c).NivelDoPino("ci", "1Y");
            }
            Conferir("74HC157: o S em LOW passa o A, em HIGH passa o B, e o G em HIGH leva a saida a LOW",
                Y(0, 0) == 0 && Y(1, 0) == 1 && Y(1, 1) == 0, $"{Y(0, 0)}{Y(1, 0)}{Y(1, 1)}");
        }

        // ---- as saidas acendem um LED pelo resistor ----
        static double CorrenteDoLed(Circuito c)
        {
            var maq = Placa();
            var bancada = Simulador.App.Bancada.DeUmaSo(maq);
            var logica = Logica.Avaliar(c, new Netlist(c), bancada, [], 0);
            return Energia.Calcular(c, bancada, null, null, null, saidasLogicas: logica.Saidas).CorrenteDaPeca("led") * 1000;
        }
        static void ComLed(Circuito c, string peca, string pino)
        {
            c.Pecas.Add(new PecaNoCircuito { Id = "r", Tipo = "resistor", X = 500, Y = 400, Ohms = 220 });
            c.Pecas.Add(new PecaNoCircuito { Id = "led", Tipo = "led", X = 560, Y = 400, Cor = "vermelho" });
            Ligar(c, peca, pino, "r", "1"); Ligar(c, "r", "2", "led", "A"); Ligar(c, "led", "C", "placa", "GND.2");
        }
        {
            var c = ComChip("74hc04");
            Nivel(c, "1A", 0);
            ComLed(c, "ci", "1Y");
            double aceso = CorrenteDoLed(c);
            var c0 = ComChip("74hc04");
            Nivel(c0, "1A", 1);
            ComLed(c0, "ci", "1Y");
            double apagado = CorrenteDoLed(c0);
            var cs = ComChip("74hc04", alimentado: false);
            Nivel(cs, "1A", 0);
            ComLed(cs, "ci", "1Y");
            double semVcc = CorrenteDoLed(cs);
            Conferir("74HC04: a saida em HIGH acende o LED pelo resistor (com o VCC do chip); em LOW, ou sem o VCC, nao",
                aceso > 2 && aceso < 12 && apagado < 0.05 && semVcc < 0.05, $"{aceso:0.0} / {apagado:0.00} / {semVcc:0.00} mA");
        }
        {
            var c = new Circuito();
            c.Pecas.Add(new PecaNoCircuito { Id = "placa", Tipo = "esp32-devkit-v1", X = 0, Y = 0, Usb = true });
            c.Pecas.Add(new PecaNoCircuito { Id = "vcc", Tipo = "vcc-symbol", X = 600, Y = 0 });
            c.Pecas.Add(new PecaNoCircuito { Id = "e", Tipo = "and-gate", X = 300, Y = 300 });
            Ligar(c, "vcc", "VCC", "e", "A"); Ligar(c, "vcc", "VCC", "e", "B");
            ComLed(c, "e", "Q");
            double ma = CorrenteDoLed(c);
            Conferir("and-gate: a porta ideal tambem acende o LED pela saida (no nivel da placa)", ma > 2 && ma < 12, $"{ma:0.0} mA");
        }

        // No editor, rodando: o 74HC08 com o D4 e o D5 em HIGH, o 74HC04 com uma
        // entrada no GND, a porta AND ideal -- cada saida com o seu LED (com
        // CS_FOTOS apontando uma pasta, sai em foto).
        {
            var c = new Circuito();
            c.Pecas.Add(new PecaNoCircuito { Id = "placa", Tipo = "esp32-devkit-v1", X = 20, Y = 40, Usb = true });
            c.Pecas.Add(new PecaNoCircuito { Id = "gnd", Tipo = "gnd-symbol", X = 250, Y = 330 });
            c.Pecas.Add(new PecaNoCircuito { Id = "and", Tipo = "74hc08", X = 300, Y = 40 });
            c.Pecas.Add(new PecaNoCircuito { Id = "not", Tipo = "74hc04", X = 300, Y = 170 });
            c.Pecas.Add(new PecaNoCircuito { Id = "ideal", Tipo = "and-gate", X = 300, Y = 290 });
            foreach (string ci in new[] { "and", "not" }) { Ligar(c, ci, "VCC", "placa", "3V3"); Ligar(c, ci, "GND", "placa", "GND.1"); }
            Ligar(c, "and", "1A", "placa", "D4"); Ligar(c, "and", "1B", "placa", "D5");
            Ligar(c, "and", "2A", "placa", "D4"); Ligar(c, "and", "2B", "gnd", "GND");
            Ligar(c, "not", "1A", "placa", "D5"); Ligar(c, "not", "2A", "gnd", "GND");
            Ligar(c, "ideal", "A", "placa", "D4"); Ligar(c, "ideal", "B", "placa", "D5");
            int k = 0;
            foreach (var (ci, pino, cor) in new[] { ("and", "1Y", "verde"), ("and", "2Y", "vermelho"), ("not", "1Y", "amarelo"),
                                                    ("not", "2Y", "azul"), ("ideal", "Q", "branco") })
            {
                double y = 40 + k * 75;
                c.Pecas.Add(new PecaNoCircuito { Id = $"r{k}", Tipo = "resistor", X = 470, Y = y + 20, Ohms = 220 });
                c.Pecas.Add(new PecaNoCircuito { Id = $"l{k}", Tipo = "led", X = 580, Y = y, Cor = cor });
                Ligar(c, ci, pino, $"r{k}", "1"); Ligar(c, $"r{k}", "2", $"l{k}", "A"); Ligar(c, $"l{k}", "C", "placa", "GND.2");
                k++;
            }
            var edf = new EditorCircuito { EmSimulacao = true };
            edf.Carregar(c);
            var maq = Placa();
            foreach (int g in new[] { 4, 5 }) { maq.PinMode(g, 1); maq.DigitalWrite(g, 1); }
            var bancada = Simulador.App.Bancada.DeUmaSo(maq);
            var rede = new Netlist(c);
            edf.AplicarEntradas(bancada, rede);
            edf.RefletirEstado(bancada, rede);
            var acesos = Enumerable.Range(0, 5).Select(i => (edf.EnergiaDoQuadro?.CorrenteDoRamo($"l{i}", "led") ?? 0) > 0.001 ? 1 : 0);
            string padrao = string.Concat(acesos);
            Conferir("logica no editor: 1 E 1 acende, 1 E 0 nao, NAO 1 nao, NAO 0 acende, e a AND ideal acende (10011)",
                padrao == "10011", padrao);
            if (Environment.GetEnvironmentVariable("CS_FOTOS") is { Length: > 0 } fotos && System.IO.Directory.Exists(fotos))
                FotografarEditor(edf, System.IO.Path.Combine(fotos, "logica.png"), 1300, 820);
        }
    }
}
