namespace Simulador.App;

/// <summary>
/// As ESP8266 redesenhadas na Alpha 25 (placas-esp8266.py): a NodeMCU saida
/// do arquivo do Altium e os dezesseis modulos ESP-xx no tamanho real, cada
/// um so com os pinos que existem nele, no passo e na ordem do modulo; os
/// LEDs soldados (o azul do GPIO2, que acende com LOW) e os botoes RST e
/// FLASH da NodeMCU.
/// </summary>
public static partial class Autoteste
{
    static void Esp8266_Redesenhadas()
    {
        saida.AppendLine("esp8266: os desenhos novos, os pinos, os LEDs e os botoes");
        const double mm = 96 / 25.4;
        static bool Perto(double a, double b, double folga = 0.06) => Math.Abs(a - b) <= folga;
        static double Passo(PecaDef d, string a, string b)
        {
            var pa = d.Pinos.First(p => p.Nome == a); var pb = d.Pinos.First(p => p.Nome == b);
            return Math.Sqrt((pa.X - pb.X) * (pa.X - pb.X) + (pa.Y - pb.Y) * (pa.Y - pb.Y)) / mm;
        }

        // ---- o tamanho de cada placa (a caixa tem 0,2 mm de folga em volta) ----
        var tamanhos = new (string Tipo, double L, double A)[]
        {
            ("esp8266-nodemcu", 25.4, 48.26), ("esp-01", 14.3, 24.8), ("esp-02", 14.2, 14.2), ("esp-03", 12.2, 17.3),
            ("esp-04", 14.7, 12.1), ("esp-05", 14.2, 14.2), ("esp-06", 16.3, 13.1), ("esp-07", 16.0, 22.0),
            ("esp-08", 16.0, 18.0), ("esp-09", 10.0, 10.0), ("esp-10", 14.2, 10.0), ("esp-11", 17.3, 12.1),
            ("esp-12", 16.0, 24.0), ("esp-12e", 16.0, 24.0), ("esp-12f", 16.0, 24.0), ("esp-13", 18.0, 20.0), ("esp-14", 16.0, 24.0),
        };
        var errados = tamanhos.Where(t => !Pecas.Existe(t.Tipo)
                                          || !Perto(Catalogo.Obter(t.Tipo).Largura / mm - 0.4, t.L, 0.3)
                                          || !Perto(Catalogo.Obter(t.Tipo).Altura / mm - 0.4, t.A, 0.3))
            .Select(t => $"{t.Tipo} {Catalogo.Obter(t.Tipo).Largura / mm - 0.4:0.0}x{Catalogo.Obter(t.Tipo).Altura / mm - 0.4:0.0}").ToList();
        Conferir("esp8266: as 17 placas no tamanho real (a NodeMCU de 25,4 x 48,3, o ESP-01 de 14,3 x 24,8, o ESP-12 de 16 x 24, o ESP-09 de 10 x 10)",
            errados.Count == 0, string.Join(", ", errados));

        // ---- as filas de ilhas: o passo e a ordem de cada modulo ----
        string[] esq12 = ["RST", "ADC", "CH_PD", "GPIO16", "GPIO14", "GPIO12", "GPIO13", "VCC"];
        string[] dir12 = ["TXD", "RXD", "GPIO5", "GPIO4", "GPIO0", "GPIO2", "GPIO15", "GND"];
        string[] flash = ["GPIO11", "GPIO7", "GPIO9", "GPIO10", "GPIO8", "GPIO6"];
        var filas = new (string Tipo, double Passo, string[] Pinos)[]
        {
            ("esp-01", 2.54, ["VCC", "RST", "CH_PD", "TXD"]), ("esp-01", 2.54, ["RXD", "GPIO0", "GPIO2", "GND"]), ("esp-01", 2.54, ["VCC", "RXD"]),
            ("esp-02", 2.54, ["RST", "GPIO2", "GPIO0", "GPIO15"]), ("esp-02", 2.54, ["GND", "RXD", "TXD", "VCC"]),
            ("esp-03", 2.0, ["VCC", "GPIO14", "GPIO12", "GPIO13", "GPIO15", "GPIO2", "GPIO0"]),
            ("esp-03", 2.0, ["ANT", "CH_PD", "GPIO16", "RXD", "TXD", "NC", "GND"]),
            ("esp-04", 2.0, ["ANT", "CH_PD", "GPIO16", "RXD", "TXD", "NC", "GND"]),
            ("esp-04", 2.0, ["VCC", "GPIO14", "GPIO12", "GPIO13", "GPIO15", "GPIO2", "GPIO0"]),
            ("esp-05", 2.54, ["VCC", "TXD", "RXD", "GND", "RST"]),
            ("esp-07", 2.0, esq12), ("esp-07", 2.0, dir12), ("esp-08", 2.0, esq12), ("esp-08", 2.0, dir12),
            ("esp-10", 2.0, ["RST", "VCC", "TXD", "RXD", "GND"]),
            ("esp-11", 1.27, ["GND", "CH_PD", "RST", "RXD", "TXD", "GPIO0", "GPIO1", "VCC"]),
            ("esp-12", 2.0, esq12), ("esp-12", 2.0, dir12),
            ("esp-12e", 2.0, esq12), ("esp-12e", 2.0, dir12), ("esp-12e", 2.0, flash),
            ("esp-12f", 2.0, esq12), ("esp-12f", 2.0, dir12), ("esp-12f", 2.0, flash),
            ("esp-13", 1.5, ["VCC", "CH_PD", "GPIO14", "GPIO12", "GPIO13", "GPIO15", "GPIO2", "GPIO0", "GND"]),
            ("esp-13", 1.5, ["GND.2", "GPIO16", "ADC", "RST", "GPIO5", "GND.3", "TXD", "RXD", "GPIO4"]),
            ("esp-14", 2.0, ["PC5", "PC6", "PC7", "SWIM", "PD3", "PD4", "NRST", "GND"]),
            ("esp-14", 2.0, ["PC4", "PC3", "PB4", "PB5", "PA3", "VCC", "VDD", "VCAP"]),
            ("esp-14", 2.0, ["PD2", "PD5", "PD6", "PA1", "PA2", "GPIO0"]),
            ("esp8266-nodemcu", 2.54, ["A0", "RSV", "RSV.2", "SD3", "SD2", "SD1", "CMD", "SD0", "CLK", "GND", "3V3", "EN", "RST", "GND.2", "VIN"]),
            ("esp8266-nodemcu", 2.54, ["D0", "D1", "D2", "D3", "D4", "3V3.2", "GND.3", "D5", "D6", "D7", "D8", "RX", "TX", "GND.4", "3V3.3"]),
        };
        var foraDoPasso = filas.Where(f =>
        {
            var d = Catalogo.Obter(f.Tipo);
            return f.Pinos.Any(n => d.Pinos.All(p => p.Nome != n))
                   || f.Pinos.Zip(f.Pinos.Skip(1)).Any(par => !Perto(Passo(d, par.First, par.Second), f.Passo));
        }).Select(f => $"{f.Tipo} ({f.Pinos[0]}...)").ToList();
        Conferir("esp8266: cada fila de ilhas no passo do modulo (2,54 na NodeMCU e no ESP-01/02/05, 2,0 nos castelados, 1,5 no ESP-13, 1,27 no ESP-11), na ordem dele",
            foraDoPasso.Count == 0, string.Join(", ", foraDoPasso));

        // Nenhum pino a mais: o que esta no catalogo e o que as filas dizem
        // (mais o furo de GND do canto do ESP-05 e a ilha da antena do ESP-08).
        var aMais = filas.GroupBy(f => f.Tipo).Select(g =>
        {
            var esperados = g.SelectMany(f => f.Pinos).ToHashSet();
            if (g.Key == "esp-05") esperados.Add("GND.2");
            if (g.Key == "esp-08") esperados.Add("ANT");
            var nomes = Catalogo.Obter(g.Key).Pinos.Select(p => p.Nome).ToHashSet();
            return nomes.SetEquals(esperados) ? null : $"{g.Key}: {string.Join("/", nomes.Except(esperados))}|{string.Join("/", esperados.Except(nomes))}";
        }).Where(e => e is not null).ToList();
        Conferir("esp8266: cada placa so com os pinos que existem nela", aMais.Count == 0, string.Join("; ", aMais));
        var nodemcu = Catalogo.Obter("esp8266-nodemcu");
        Conferir("esp8266-nodemcu: as duas barras a 22,86 mm (nove passos) uma da outra, como no arquivo da placa",
            Perto(Passo(nodemcu, "A0", "D0"), 22.86, 0.08) && Perto(Passo(nodemcu, "VIN", "3V3.3"), 22.86, 0.08));

        // ---- os GPIO que cada modulo traz para fora ----
        HashSet<int> Gpios(string tipo)
        {
            var perfil = PerfilPlaca.De(tipo);
            return [.. Catalogo.Obter(tipo).Pinos.Select(p => perfil.Numero(p.Nome)).Where(n => n is >= 0 and < 17)];
        }
        var gpios = new (string Tipo, int[] Esperados)[]
        {
            ("esp-03", [0, 1, 2, 3, 12, 13, 14, 15, 16]), ("esp-04", [0, 1, 2, 3, 12, 13, 14, 15, 16]),
            ("esp-06", [0, 1, 2, 3, 12, 13, 14, 15, 16]), ("esp-09", [0, 1, 2, 3, 12, 13, 14, 15]),
            ("esp-11", [0, 1, 3]), ("esp-14", [0]), ("esp-05", [1, 3]), ("esp-10", [1, 3]), ("esp-01", [0, 1, 2, 3]),
            ("esp-12f", [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16]),
        };
        var trocados = gpios.Where(g => !Gpios(g.Tipo).SetEquals(g.Esperados))
            .Select(g => $"{g.Tipo}: {string.Join(",", Gpios(g.Tipo).Order())}").ToList();
        Conferir("esp8266: os GPIO de fora sao os do modulo de verdade (o ESP-03/04/06/09 sem GPIO4 e GPIO5, o ESP-11 com GPIO0 e GPIO1, do ESP-14 so o GPIO0)",
            trocados.Count == 0, string.Join("; ", trocados));

        // ---- os LEDs soldados ----
        (double[] Antes, double[] Depois) Leds(string tipo, Action<Maquina> mexer)
        {
            var c = new Circuito();
            c.Pecas.Add(new PecaNoCircuito { Id = "placa", Tipo = tipo, X = 0, Y = 0, Usb = true });
            var ed = new EditorCircuito();
            ed.Carregar(c);
            var maq = new Maquina { Perfil = PerfilPlaca.De(tipo) };
            maq.Reiniciar();
            var b = Simulador.App.Bancada.DeUmaSo(maq);
            var rede = new Netlist(c);
            ed.RefletirEstado(b, rede);
            var antes = ed.LedsDaPlacaParaTeste("placa");
            mexer(maq);
            ed.RefletirEstado(b, rede);
            return (antes, ed.LedsDaPlacaParaTeste("placa"));
        }
        var (nmAntes, nmDepois) = Leds("esp8266-nodemcu", m =>
        {
            m.Modo[2] = 1; m.Pwm[2] = 0;          // digitalWrite(D4, LOW): o do ESP-12E acende
            m.Modo[16] = 1; m.Pwm[16] = 255;      // digitalWrite(D0, HIGH): o da placa fica apagado
        });
        Conferir("esp8266-nodemcu: dois LEDs azuis, o do ESP-12E (GPIO2) e o da placa (GPIO16), apagados no comeco e acesos com LOW",
            nmAntes.Length == 2 && nmAntes.All(v => v < 0.05) && nmDepois[0] > 0.5 && nmDepois[1] < 0.05,
            string.Join("/", nmAntes.Select(v => v.ToString("0.00"))) + " -> " + string.Join("/", nmDepois.Select(v => v.ToString("0.00"))));
        var semLed = new List<string>();
        foreach (string tipo in new[] { "esp-12", "esp-12e", "esp-12f", "esp-07" })
        {
            var (_, aceso) = Leds(tipo, m => { m.Modo[2] = 1; m.Pwm[2] = 0; });
            int k = tipo == "esp-07" ? 1 : 0;           // no ESP-07 o primeiro e o de energia
            if (aceso.Length <= k || aceso[k] < 0.5) semLed.Add(tipo);
        }
        var (e01, _) = Leds("esp-01", _ => { });
        if (e01.Length != 2 || e01[0] < 0.5 || e01[1] > 0.05) semLed.Add("esp-01");
        Conferir("esp8266: o LED azul do GPIO2 nos ESP-12, 12E, 12F e 07 acende com LOW; o ESP-01 tem o vermelho de energia e o azul do TX",
            semLed.Count == 0, string.Join(", ", semLed));

        // ---- os botoes da NodeMCU: o FLASH leva o GPIO0 a LOW ----
        {
            var def = Catalogo.Obter("esp8266-nodemcu");
            var botoes = EditorCircuito.BotoesPorPlaca["esp8266-nodemcu"];
            bool dentro = botoes.Length == 2 && botoes.Count(x => x.Nome == "FLASH") == 1 && botoes.Count(x => x.Nome == "RST") == 1
                          && botoes.All(x => x.X > 0 && x.X < def.Largura && x.Y > 0 && x.Y < def.Altura);
            var c = new Circuito();
            c.Pecas.Add(new PecaNoCircuito { Id = "placa", Tipo = "esp8266-nodemcu", X = 0, Y = 0, Usb = true });
            var ed = new EditorCircuito { EmSimulacao = true };
            ed.Carregar(c);
            var maq = new Maquina { Perfil = PerfilPlaca.De("esp8266-nodemcu") };
            maq.Reiniciar();
            var b = Simulador.App.Bancada.DeUmaSo(maq);
            ed.AplicarEntradas(b, new Netlist(c));
            bool antes = maq.Pressionado[0];
            ed.BootPressionado.Add("placa");
            ed.AplicarEntradas(b, new Netlist(c));
            bool apertado = maq.Pressionado[0];
            ed.BootPressionado.Clear();
            ed.AplicarEntradas(b, new Netlist(c));
            Conferir("esp8266-nodemcu: RST e FLASH desenhados na caixa; o FLASH apertado leva o GPIO0 (D3) a LOW",
                dentro && !antes && apertado && !maq.Pressionado[0], $"{dentro}/{antes}/{apertado}");
        }

        // Com CS_FOTOS apontando uma pasta, a NodeMCU sai em foto, rodando: os
        // dois LEDs azuis acesos (D4 e D0 em LOW), o LED de fora no D5 e o
        // FLASH apertado.
        if (Environment.GetEnvironmentVariable("CS_FOTOS") is { Length: > 0 } fotos && System.IO.Directory.Exists(fotos))
        {
            var c = new Circuito();
            c.Pecas.Add(new PecaNoCircuito { Id = "placa", Tipo = "esp8266-nodemcu", X = 40, Y = 30, Usb = true });
            c.Pecas.Add(new PecaNoCircuito { Id = "r", Tipo = "resistor", X = 220, Y = 110, Ohms = 220 });
            c.Pecas.Add(new PecaNoCircuito { Id = "l", Tipo = "led", X = 330, Y = 80, Cor = "verde" });
            foreach (var (de, pde, para, ppara) in new[] { ("placa", "D5", "r", "1"), ("r", "2", "l", "A"), ("l", "C", "placa", "GND.3") })
                c.Fios.Add(new Fio { DePeca = de, DePino = pde, ParaPeca = para, ParaPino = ppara });
            var edf = new EditorCircuito { EmSimulacao = true };
            edf.Carregar(c);
            var maq = new Maquina { Perfil = PerfilPlaca.De("esp8266-nodemcu") };
            maq.Reiniciar();
            foreach (var (g, nivel) in new[] { (2, 0), (16, 0), (14, 1) }) { maq.PinMode(g, 1); maq.DigitalWrite(g, nivel); }
            var b = Simulador.App.Bancada.DeUmaSo(maq);
            var rede = new Netlist(c);
            edf.BootPressionado.Add("placa");
            edf.AplicarEntradas(b, rede);
            edf.RefletirEstado(b, rede);
            FotografarEditor(edf, System.IO.Path.Combine(fotos, "nodemcu.png"), 900, 700);
        }
    }
}
