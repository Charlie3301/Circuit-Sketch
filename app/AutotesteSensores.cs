namespace Simulador.App;

/// <summary>
/// Os sensores redesenhados na Alpha 25 (pecas-sensores.py): no tamanho real,
/// com os pinos no passo e na ordem da placa de verdade (o GY-521 de VCC a
/// INT, o DHT11 com o NC), e os LEDs dos modulos -- o PWR com o VCC, o DO
/// quando o sensor detecta.
/// </summary>
public static partial class Autoteste
{
    static void Sensores_Redesenhados()
    {
        saida.AppendLine("sensores: os desenhos novos, os pinos e os LEDs dos modulos");
        const double mm = 96 / 25.4;
        static bool Perto(double a, double b, double folga = 0.06) => Math.Abs(a - b) <= folga;
        static double Passo(PecaDef d, string a, string b)
        {
            var pa = d.Pinos.First(p => p.Nome == a); var pb = d.Pinos.First(p => p.Nome == b);
            return Math.Sqrt((pa.X - pb.X) * (pa.X - pb.X) + (pa.Y - pb.Y) * (pa.Y - pb.Y)) / mm;
        }

        string[] tipos = ["hc-sr04", "dht22", "dht11", "photoresistor-sensor", "ntc-temperature-sensor", "pir-motion-sensor",
                          "flame-sensor", "gas-sensor", "heart-beat-sensor", "big-sound-sensor", "small-sound-sensor", "mpu6050", "hx711"];
        var faltando = tipos.Where(t => !Pecas.Existe(t) || Catalogo.Obter(t) is not { Categoria: "Sensores" }).ToList();
        Conferir("sensores: os 13 tem desenho e estao na categoria Sensores", faltando.Count == 0, string.Join(", ", faltando));

        // ---- os pinos no passo de 2,54 mm, na ordem da placa ----
        var barras = new (string Tipo, string[] Pinos)[]
        {
            ("hc-sr04", ["VCC", "TRIG", "ECHO", "GND"]), ("dht22", ["VCC", "SDA", "NC", "GND"]), ("dht11", ["VCC", "DATA", "NC", "GND"]),
            ("photoresistor-sensor", ["VCC", "GND", "DO", "AO"]), ("flame-sensor", ["VCC", "GND", "DOUT", "AOUT"]),
            ("gas-sensor", ["AOUT", "DOUT", "GND", "VCC"]), ("ntc-temperature-sensor", ["GND", "VCC", "OUT"]),
            ("heart-beat-sensor", ["GND", "VCC", "OUT"]), ("big-sound-sensor", ["AOUT", "GND", "VCC", "DOUT"]),
            ("small-sound-sensor", ["AOUT", "GND", "VCC", "DOUT"]), ("pir-motion-sensor", ["VCC", "OUT", "GND"]),
            ("mpu6050", ["VCC", "GND", "SCL", "SDA", "XDA", "XCL", "AD0", "INT"]), ("hx711", ["GND", "DT", "SCK", "VCC"]),
        };
        var fora = barras.Where(b =>
        {
            var d = Catalogo.Obter(b.Tipo);
            return b.Pinos.Zip(b.Pinos.Skip(1)).Any(par => !Perto(Passo(d, par.First, par.Second), 2.54));
        }).Select(b => b.Tipo).ToList();
        Conferir("sensores: cada barra no passo de 2,54 mm, na ordem da placa (o DHT11 agora com as quatro pernas, VCC DATA NC GND)",
            fora.Count == 0, string.Join(", ", fora));
        var gy = Catalogo.Obter("mpu6050");
        Conferir("mpu6050: o GY-521 com o VCC na ponta esquerda e o INT na direita, como na serigrafia da placa",
            gy.Pinos.OrderBy(p => p.X).Select(p => p.Nome).SequenceEqual(["VCC", "GND", "SCL", "SDA", "XDA", "XCL", "AD0", "INT"]));
        var tamanhos = new (string Tipo, double L, double A)[]
        {
            ("hc-sr04", 45.25, 26.2), ("pir-motion-sensor", 32.3, 29.4), ("dht22", 15.5, 31.9), ("dht11", 12.4, 22.4),
            ("mpu6050", 21.2, 16.2), ("gas-sensor", 38.4, 22.4),
        };
        var errados = tamanhos.Where(t => !Perto(Catalogo.Obter(t.Tipo).Largura / mm, t.L, 0.2) || !Perto(Catalogo.Obter(t.Tipo).Altura / mm, t.A, 0.2))
            .Select(t => $"{t.Tipo} {Catalogo.Obter(t.Tipo).Largura / mm:0.0}x{Catalogo.Obter(t.Tipo).Altura / mm:0.0}").ToList();
        Conferir("sensores: no tamanho real (HC-SR04 de 45 mm, HC-SR501 de 32 x 24, AM2302 de 15,1 x 25, GY-521 de 21 x 16, MQ-2 de 32 x 22)",
            errados.Count == 0, string.Join(", ", errados));

        // ---- os LEDs dos modulos: o PWR com o VCC, o DO quando detecta ----
        (bool Pwr, bool Do) Luzes(string tipo, double giro, bool alimentado)
        {
            var c = new Circuito();
            c.Pecas.Add(new PecaNoCircuito { Id = "placa", Tipo = "esp32-devkit-v1", X = 0, Y = 0, Usb = true });
            c.Pecas.Add(new PecaNoCircuito { Id = "s", Tipo = tipo, X = 400, Y = 50, Giro = giro });
            if (alimentado)
            {
                c.Fios.Add(new Fio { DePeca = "s", DePino = "VCC", ParaPeca = "placa", ParaPino = "3V3" });
                c.Fios.Add(new Fio { DePeca = "s", DePino = "GND", ParaPeca = "placa", ParaPino = "GND.1" });
            }
            var ed = new EditorCircuito { EmSimulacao = true };
            ed.Carregar(c);
            var maq = new Maquina { Perfil = PerfilPlaca.Esp32 };
            maq.Reiniciar();
            var bancada = Simulador.App.Bancada.DeUmaSo(maq);
            ed.AplicarEntradas(bancada, new Netlist(c));
            return (ed.LuzDeModuloAcesaParaTeste("s", 0), ed.LuzDeModuloAcesaParaTeste("s", 1));
        }
        var ruins = new List<string>();
        foreach (string tipo in new[] { "photoresistor-sensor", "flame-sensor", "gas-sensor", "big-sound-sensor", "small-sound-sensor" })
        {
            var detecta = Luzes(tipo, 0.9, true);
            var parado = Luzes(tipo, 0.1, true);
            var desligado = Luzes(tipo, 0.9, false);
            if (detecta != (true, true) || parado != (true, false) || desligado != (false, false))
                ruins.Add($"{tipo}: {detecta} {parado} {desligado}");
        }
        Conferir("sensores: nos modulos do LM393 o PWR acende com o VCC, e o DO so quando o sensor passa do ponto; sem o VCC, nada",
            ruins.Count == 0, string.Join("; ", ruins));
        var gyLuz = Luzes("mpu6050", 0.5, true);
        Conferir("mpu6050: o LED de energia do GY-521 acende com o VCC", gyLuz.Pwr, $"{gyLuz}");

        // Com CS_FOTOS apontando uma pasta, os treze saem em foto, com os modulos ligados
        // (o DO aceso no LDR, no MQ-2 e no KY-037; apagado na chama e no KY-038).
        if (Environment.GetEnvironmentVariable("CS_FOTOS") is { Length: > 0 } fotos && System.IO.Directory.Exists(fotos))
        {
            var c = new Circuito();
            c.Pecas.Add(new PecaNoCircuito { Id = "placa", Tipo = "esp32-devkit-v1", X = 20, Y = 260, Usb = true });
            var lugares = new (string Tipo, double X, double Y, double Giro)[]
            {
                ("hc-sr04", 20, 20, 0.5), ("pir-motion-sensor", 210, 20, 0), ("dht22", 350, 20, 0.5), ("dht11", 430, 40, 0.5),
                ("mpu6050", 500, 30, 0.5), ("hx711", 600, 20, 0.5), ("photoresistor-sensor", 260, 170, 0.9),
                ("flame-sensor", 430, 170, 0.1), ("gas-sensor", 640, 150, 0.9), ("big-sound-sensor", 260, 260, 0.9),
                ("small-sound-sensor", 440, 260, 0.1), ("ntc-temperature-sensor", 620, 260, 0.5), ("heart-beat-sensor", 730, 260, 0.5),
            };
            foreach (var (tipo, x, y, giro) in lugares)
            {
                c.Pecas.Add(new PecaNoCircuito { Id = tipo, Tipo = tipo, X = x, Y = y, Giro = giro });
                if (Catalogo.Obter(tipo).Pinos.Any(p => p.Nome == "VCC"))
                {
                    c.Fios.Add(new Fio { DePeca = tipo, DePino = "VCC", ParaPeca = "placa", ParaPino = "3V3" });
                    c.Fios.Add(new Fio { DePeca = tipo, DePino = "GND", ParaPeca = "placa", ParaPino = "GND.1" });
                }
            }
            var edf = new EditorCircuito { EmSimulacao = true };
            edf.Carregar(c);
            var maq = new Maquina { Perfil = PerfilPlaca.Esp32 };
            maq.Reiniciar();
            var bancada = Simulador.App.Bancada.DeUmaSo(maq);
            edf.AplicarEntradas(bancada, new Netlist(c));
            edf.RefletirEstado(bancada, new Netlist(c));
            FotografarEditor(edf, System.IO.Path.Combine(fotos, "sensores.png"), 1500, 900);
        }
    }
}
