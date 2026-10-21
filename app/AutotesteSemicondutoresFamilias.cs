namespace Simulador.App;

/// <summary>
/// As familias de semicondutores da Alpha 25: cada modelo com o seu desenho,
/// a pinagem de cada familia (E-B-C, C-B-E, B-C-E, G-D-S, os DIP do opto), o
/// Darlington (TIP120) que satura com pouca base, o MOSFET de canal P que liga
/// com o gate no GND e o CTR de cada optoacoplador (o 4N25 puxa a metade do
/// 4N35).
/// </summary>
public static partial class Autoteste
{
    static void Semicondutores_Familias()
    {
        saida.AppendLine("semicondutores: as familias, os modelos e as pinagens");

        static PinoDef P(PecaDef d, string n) => d.Pinos.First(p => p.Nome == n);
        static string Ordem(string tipo) => string.Join("-", Catalogo.Obter(tipo).Pinos.OrderBy(p => p.X).Select(p => p.Nome));

        // ---- cada modelo tem o seu desenho ----
        var semDesenho = new List<string>();
        int modelos = 0;
        foreach (var tipo in new[] { "transistor-npn", "transistor-pnp", "transistor-npn-cbe", "transistor-pnp-cbe", "transistor-npn-potencia",
                                     "transistor-pnp-potencia", "mosfet-n", "mosfet-p", "optoacoplador", "optoacoplador-4n35" })
        {
            foreach (var m in Semicondutores.Modelos(tipo))
            {
                modelos++;
                string svg = Semicondutores.Svg(new PecaNoCircuito { Tipo = tipo, Modelo = m });
                if (!Pecas.Existe(svg)) semDesenho.Add(svg);
            }
        }
        foreach (var tipo in new[] { "diodo", "diodo-zener", "diodo-schottky" })
        {
            foreach (var f in Diodos.ModelosDe(tipo))
            {
                modelos++;
                string svg = f.Svg ?? tipo;
                if (!Pecas.Existe(svg)) semDesenho.Add(svg);
            }
        }
        Conferir($"semicondutores: os {modelos} modelos tem cada um o seu desenho, e todos os tipos estao no catalogo (Semicondutores)",
            modelos >= 70 && semDesenho.Count == 0
            && new[] { "transistor-npn-cbe", "transistor-pnp-cbe", "transistor-npn-potencia", "transistor-pnp-potencia", "mosfet-p", "optoacoplador-4n35" }
                .All(t => Catalogo.Obter(t) is { Categoria: "Semicondutores" }),
            string.Join(", ", semDesenho));

        // ---- a pinagem de cada familia, da esquerda para a direita (a face para a frente) ----
        Conferir("semicondutores: E-B-C nos 2N/S80xx, C-B-E nos BC, B-C-E nos TIP/MJE, G-D-S nos MOSFETs",
            Ordem("transistor-npn") == "E-B-C" && Ordem("transistor-pnp") == "E-B-C"
            && Ordem("transistor-npn-cbe") == "C-B-E" && Ordem("transistor-pnp-cbe") == "C-B-E"
            && Ordem("transistor-npn-potencia") == "B-C-E" && Ordem("transistor-pnp-potencia") == "B-C-E"
            && Ordem("mosfet-n") == "G-D-S" && Ordem("mosfet-p") == "G-D-S",
            $"{Ordem("transistor-npn")} {Ordem("transistor-npn-cbe")} {Ordem("transistor-npn-potencia")} {Ordem("mosfet-p")}");

        // ---- os DIP: o pino 1 embaixo a esquerda, e dai no sentido anti-horario ----
        {
            var d4 = Catalogo.Obter("optoacoplador");
            var d6 = Catalogo.Obter("optoacoplador-4n35");
            const double Fileiras = 7.62 * 96 / 25.4;
            bool dip4 = P(d4, "A").Y > P(d4, "C").Y && P(d4, "A").X < P(d4, "K").X && P(d4, "E").X > P(d4, "C").X
                        && Math.Abs(P(d4, "A").X - P(d4, "C").X) < 0.1 && Math.Abs(P(d4, "A").Y - P(d4, "C").Y - Fileiras) < 0.2;
            string baixo = string.Join("-", d6.Pinos.Where(p => p.Y > d6.Altura / 2.0).OrderBy(p => p.X).Select(p => p.Nome));
            string cima = string.Join("-", d6.Pinos.Where(p => p.Y < d6.Altura / 2.0).OrderByDescending(p => p.X).Select(p => p.Nome));
            Conferir("optoacoplador: o PC817 com A, K embaixo e E (3), C (4) em cima; o 4N35 com A-K-NC embaixo e E-C-B em cima",
                dip4 && baixo == "A-K-NC" && cima == "E-C-B", $"4N35: {baixo} / {cima}");
        }

        // ---- os circuitos: a ESP32 com o D13 em HIGH, uma bateria para a carga ----
        static Maquina Pino13(bool alto)
        {
            var maq = new Maquina { Perfil = PerfilPlaca.Esp32 };
            maq.Reiniciar();
            maq.Modo[13] = 1;
            maq.Digital[13] = alto ? 1 : 0;
            maq.Pwm[13] = alto ? 255 : 0;
            return maq;
        }
        static Circuito Base(double volts)
        {
            var c = new Circuito();
            c.Pecas.AddRange([new PecaNoCircuito { Id = "placa", Tipo = "esp32-devkit-v1", X = 0, Y = 0, Usb = true },
                              new PecaNoCircuito { Id = "bat", Tipo = "bateria", X = 0, Y = 300, Volts = volts, MiliAmperes = 20000, MiliAmperesHora = 1000 }]);
            c.Fios.Add(new Fio { DePeca = "bat", DePino = "-", ParaPeca = "placa", ParaPino = "GND.2" });
            return c;
        }
        static void Ligar(Circuito c, string a, string pa, string b, string pb) => c.Fios.Add(new Fio { DePeca = a, DePino = pa, ParaPeca = b, ParaPino = pb });
        static PecaNoCircuito R(string id, double ohms) => new() { Id = id, Tipo = "resistor", X = 200, Y = 0, Ohms = ohms };

        // O bipolar ligando 100 ohms em 9 V, a base pelo resistor no D13.
        static (double Ic, double Vce) Chave(string tipo, string modelo, double ohmsDaBase)
        {
            var c = Base(9);
            c.Pecas.AddRange([new PecaNoCircuito { Id = "q", Tipo = tipo, Modelo = modelo, X = 300, Y = 0 }, R("rb", ohmsDaBase), R("carga", 100)]);
            Ligar(c, "placa", "D13", "rb", "1"); Ligar(c, "rb", "2", "q", "B");
            Ligar(c, "bat", "+", "carga", "1"); Ligar(c, "carga", "2", "q", "C"); Ligar(c, "q", "E", "bat", "-");
            var conta = Energia.Calcular(c, Simulador.App.Bancada.DeUmaSo(Pino13(true)));
            return (conta.CorrenteDoRamo("q", "coletor"), conta.TensaoDoPino("q", "C") - conta.TensaoDoPino("q", "E"));
        }
        var bc547 = Chave("transistor-npn-cbe", "bc547", 1000);
        Conferir("BC547 (C-B-E): com 1 kohm na base satura a carga de 100 ohms, como o 2N2222A",
            bc547.Ic is > 0.080 and < 0.090 && bc547.Vce < 0.5, $"{bc547.Ic * 1000:0.0} mA, Vce {bc547.Vce:0.00} V");
        var tip120 = Chave("transistor-npn-potencia", "tip120", 10_000);
        var tip31 = Chave("transistor-npn-potencia", "tip31c", 10_000);
        Conferir("TIP120 (Darlington): com 10 kohm na base ainda satura (Vce ~1 V, a queda do Darlington); o TIP31C, com a mesma base, nao",
            tip120.Ic > 0.075 && tip120.Vce is > 0.8 and < 1.4 && tip31.Ic < 0.01,
            $"TIP120: {tip120.Ic * 1000:0.0} mA, Vce {tip120.Vce:0.00} V | TIP31C: {tip31.Ic * 1000:0.00} mA");

        // ---- o MOSFET de canal P: source no +12 V, carga do dreno ao GND ----
        static double CanalP(bool gateNoGnd)
        {
            var c = Base(12);
            c.Pecas.AddRange([new PecaNoCircuito { Id = "m", Tipo = "mosfet-p", Modelo = "irf4905", X = 300, Y = 0 }, R("carga", 10)]);
            Ligar(c, "bat", "+", "m", "S"); Ligar(c, "m", "D", "carga", "1"); Ligar(c, "carga", "2", "bat", "-");
            Ligar(c, "m", "G", "bat", gateNoGnd ? "-" : "+");
            return Energia.Calcular(c, null).CorrenteDoRamo("m", "dreno");
        }
        double pLigado = CanalP(true), pDesligado = CanalP(false);
        Conferir("MOSFET P (IRF4905): o gate no GND (12 V abaixo do source) liga a carga inteira; no +12 V, desliga",
            pLigado is > 1.15 and < 1.21 && Math.Abs(pDesligado) < 1e-4, $"ligado {pLigado:0.00} A | desligado {pDesligado * 1000:0.000} mA");

        // ---- o CTR: o LED pelo D13 (330 ohms, ~6 mA), o coletor com 1 kohm para 5 V ----
        static double Coletor(string tipo, string modelo)
        {
            var c = Base(5);
            c.Pecas.AddRange([new PecaNoCircuito { Id = "o", Tipo = tipo, Modelo = modelo, X = 300, Y = 0 }, R("rl", 330), R("pull", 1000)]);
            Ligar(c, "placa", "D13", "rl", "1"); Ligar(c, "rl", "2", "o", "A"); Ligar(c, "o", "K", "placa", "GND.1");
            Ligar(c, "bat", "+", "pull", "1"); Ligar(c, "pull", "2", "o", "C"); Ligar(c, "o", "E", "bat", "-");
            return Energia.Calcular(c, Simulador.App.Bancada.DeUmaSo(Pino13(true))).TensaoDoPino("o", "C");
        }
        double v4n35 = Coletor("optoacoplador-4n35", "4n35"), v4n25 = Coletor("optoacoplador-4n35", "4n25");
        Conferir("opto: o 4N35 (CTR 100%) puxa o coletor para baixo; o 4N25 (50%), com o mesmo LED, so ate a metade",
            v4n35 < 0.6 && v4n25 > 1.0, $"4N35 {v4n35:0.00} V | 4N25 {v4n25:0.00} V");
    }
}
