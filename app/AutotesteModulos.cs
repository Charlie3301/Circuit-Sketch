namespace Simulador.App;

/// <summary>
/// Os modulos redesenhados (Alpha 25): o adaptador I2C do LCD (FC-113), o
/// nRF24L01, o MAX98357A (o da Adafruit) e o PCM5102A (GY-PCM5102), cada um na
/// escala real e com as barras onde a placa de verdade tem -- e os pinos que
/// faltavam no PCM5102A (as saidas de linha e os quatro de configuracao), com o
/// XSMT e o FMT mexendo no som como no chip.
/// </summary>
public static partial class Autoteste
{
    static void Modulos_Desenho()
    {
        saida.AppendLine("modulos: o desenho, as barras e os pinos de cada um");

        static PinoDef P(PecaDef d, string n) => d.Pinos.First(p => p.Nome == n);
        static bool Passo(double a, double b, int passos) => Math.Abs(Math.Abs(a - b) - passos * 9.6) < 0.15;

        // ---- o tamanho real e os pinos dentro da caixa ----
        foreach (var (tipo, pinos, larg, alt) in new[] { ("modulo-i2c", 20, 157, 72), ("nrf24l01", 8, 57, 110),
                                                        ("max98357a", 9, 67, 72), ("pcm5102a", 15, 64, 121) })
        {
            var d = Catalogo.Obter(tipo);
            bool dentro = d.Pinos.All(p => p.X >= 0 && p.X <= d.Largura && p.Y >= 0 && p.Y <= d.Altura);
            Conferir($"{tipo}: {pinos} pinos, a caixa {larg}x{alt} px (o tamanho real), todos dentro dela",
                d.Pinos.Count == pinos && d.Largura == larg && d.Altura == alt && dentro && Pecas.Existe(tipo),
                $"{d.Pinos.Count} pinos, {d.Largura}x{d.Altura}, dentro {dentro}");
        }

        // ---- o nRF24L01: a barra 2x4 atravessada na ponta, o pino 1 (GND) no canto ----
        {
            var d = Catalogo.Obter("nrf24l01");
            bool fora = new[] { "GND", "CE", "SCK", "MISO" }.All(n => Math.Abs(P(d, n).Y - P(d, "GND").Y) < 0.1);
            bool dentro = new[] { "VCC", "CSN", "MOSI", "IRQ" }.All(n => Math.Abs(P(d, n).Y - P(d, "VCC").Y) < 0.1);
            bool ordem = P(d, "GND").X > P(d, "CE").X && P(d, "CE").X > P(d, "SCK").X && P(d, "SCK").X > P(d, "MISO").X
                         && Passo(P(d, "GND").X, P(d, "MISO").X, 3) && Passo(P(d, "GND").Y, P(d, "VCC").Y, 1)
                         && Math.Abs(P(d, "GND").X - P(d, "VCC").X) < 0.1;
            Conferir("nrf24l01: 4 pinos na largura e 2 fileiras; GND no canto, o VCC atras dele, MISO na outra ponta",
                fora && dentro && ordem, $"GND ({P(d, "GND").X},{P(d, "GND").Y}) VCC ({P(d, "VCC").X},{P(d, "VCC").Y})");
        }

        // ---- o nRF24L01+PA+LNA (o de antena externa): a mesma barra, e o mesmo radio para o sketch ----
        {
            var d = Catalogo.Obter("nrf24l01-pa-lna");
            var curto = Catalogo.Obter("nrf24l01");
            bool mesmaBarra = d is not null && d.Pinos.Count == 8 && d.Pinos.All(p =>
                curto.Pinos.FirstOrDefault(q => q.Nome == p.Nome) is { } q
                && Math.Abs((p.X - P(d, "GND").X) - (q.X - P(curto, "GND").X)) < 0.2 && Math.Abs(p.Y - q.Y) < 0.2);
            Conferir("nrf24l01-pa-lna: no catalogo (Modulos, 15,5 x 41,5 mm), com a mesma barra 2x4 do nRF24L01",
                mesmaBarra && d.Largura == 59 && d.Altura == 157 && d.Categoria == "Modulos" && Pecas.Existe("nrf24l01-pa-lna"),
                d is null ? "sem peca" : $"{d.Largura}x{d.Altura} {d.Categoria}");
            var c = new Circuito();
            c.Pecas.Add(new PecaNoCircuito { Id = "placa", Tipo = "esp32-devkit-v1", X = 0, Y = 0, Usb = true });
            c.Pecas.Add(new PecaNoCircuito { Id = "r", Tipo = "nrf24l01-pa-lna", X = 400, Y = 0 });
            foreach (var (pr, pp) in new[] { ("VCC", "3V3"), ("GND", "GND.1"), ("CE", "D4"), ("CSN", "D5"), ("SCK", "D18"), ("MOSI", "D23"), ("MISO", "D19") })
                c.Fios.Add(new Fio { DePeca = "r", DePino = pr, ParaPeca = "placa", ParaPino = pp });
            var ed = new EditorCircuito { EmSimulacao = true };
            ed.Carregar(c);
            var maq = new Maquina { Perfil = PerfilPlaca.Esp32 };
            maq.Reiniciar();
            ed.AplicarEntradas(Simulador.App.Bancada.DeUmaSo(maq), new Netlist(c));
            Conferir("nrf24l01-pa-lna: ligado no SPI, a placa acha o radio (radio.begin() funciona)", maq.RadioPresente);
        }

        // ---- o MAX98357A: a barra na ordem da serigrafia e o borne com o - a esquerda ----
        {
            var d = Catalogo.Obter("max98357a");
            string barra = string.Join(" ", d.Pinos.Where(p => !p.Nome.StartsWith("OUT")).OrderBy(p => p.X).Select(p => p.Nome));
            Conferir("max98357a: LRC, BCLK, DIN, GAIN, SD, GND, VIN da esquerda para a direita; o OUT- a esquerda do OUT+",
                barra == "LRC BCLK DIN GAIN SD GND VIN" && P(d, "OUT-").X < P(d, "OUT+").X, barra);
        }

        // ---- o adaptador I2C: o pino 1 (VSS) a direita, ao lado do conector de 4 ----
        {
            var d = Catalogo.Obter("modulo-i2c");
            bool barra = P(d, "VSS").X > P(d, "K").X && Passo(P(d, "VSS").X, P(d, "K").X, 15)
                         && Math.Abs(P(d, "VSS").Y - P(d, "K").Y) < 0.1;
            bool i2c = new[] { "GND", "VCC", "SDA", "SCL" }.Select(n => P(d, n)).Zip(new[] { "VCC", "SDA", "SCL" }.Select(n => P(d, n)),
                (a, b) => Passo(a.Y, b.Y, 1) && Math.Abs(a.X - b.X) < 0.1).All(x => x) && P(d, "GND").X > P(d, "D7").X;
            Conferir("modulo-i2c: a barra do LCD com o pino 1 na direita; o GND, VCC, SDA e SCL em coluna na ponta", barra && i2c);
        }

        // ---- o PCM5102A: as duas barras, o terra unico, o XSMT e o FMT ----
        {
            var d = Catalogo.Obter("pcm5102a");
            string lateral = string.Join(" ", d.Pinos.Where(p => Math.Abs(p.X - P(d, "LROUT").X) < 0.1).OrderBy(p => p.Y).Select(p => p.Nome));
            string baixo = string.Join(" ", d.Pinos.Where(p => Math.Abs(p.Y - P(d, "VIN").Y) < 0.1).OrderBy(p => p.X).Select(p => p.Nome));
            Conferir("pcm5102a: a lateral (LROUT ... FLT) e a de baixo (SCK ... VIN), na ordem da serigrafia",
                lateral == "LROUT AGND ROUT AGND.2 A3V3 FMT XSMT DEMP FLT" && baixo == "SCK BCK DIN LCK GND VIN", lateral + " | " + baixo);

            var c0 = new Circuito();
            c0.Pecas.Add(new PecaNoCircuito { Id = "dac", Tipo = "pcm5102a", X = 0, Y = 0 });
            var r0 = new Netlist(c0);
            Conferir("pcm5102a: o GND e os dois AGND sao o mesmo terra",
                r0.RaizDe(Netlist.No("dac", "GND")) == r0.RaizDe(Netlist.No("dac", "AGND"))
                && r0.RaizDe(Netlist.No("dac", "GND")) == r0.RaizDe(Netlist.No("dac", "AGND.2")));

            SaidaDeAudio Tocando(Action<Circuito, Maquina> mexer)
            {
                var c = new Circuito();
                c.Pecas.AddRange([
                    new PecaNoCircuito { Id = "placa", Tipo = "esp32-devkit-v1", X = 0, Y = 0, Usb = true },
                    new PecaNoCircuito { Id = "dac", Tipo = "pcm5102a", X = 400, Y = 0 }]);
                void Ligar(string pd, string pp) => c.Fios.Add(new Fio { DePeca = "dac", DePino = pd, ParaPeca = "placa", ParaPino = pp });
                Ligar("VIN", "3V3"); Ligar("GND", "GND.1"); Ligar("SCK", "GND.2");
                Ligar("BCK", "D26"); Ligar("LCK", "D25"); Ligar("DIN", "D22");
                var maq = new Maquina { Perfil = PerfilPlaca.Esp32 };
                maq.Reiniciar();
                maq.Audio = new SaidaDeAudio(44100, 26, 25, 22, 1);
                mexer?.Invoke(c, maq);
                var ed = new EditorCircuito();
                ed.Carregar(c);
                return ed.SomDoPcm5102a(Simulador.App.Bancada.DeUmaSo(maq), new Netlist(c));
            }
            void Fio(Circuito c, string pd, string pp) => c.Fios.Add(new Fio { DePeca = "dac", DePino = pd, ParaPeca = "placa", ParaPino = pp });
            bool normal = Tocando(null) is not null;
            bool mudoNoGnd = Tocando((c, _) => Fio(c, "XSMT", "GND.1")) is null;
            bool mudoPeloPino = Tocando((c, m) => { Fio(c, "XSMT", "D4"); m.PinMode(4, 1); m.DigitalWrite(4, 0); }) is null;
            bool tocaPinoAlto = Tocando((c, m) => { Fio(c, "XSMT", "D4"); m.PinMode(4, 1); m.DigitalWrite(4, 1); }) is not null;
            bool formatoErrado = Tocando((c, _) => Fio(c, "FMT", "3V3")) is null;
            Conferir("pcm5102a: o XSMT no GND (ou num pino em LOW) emudece; em HIGH toca; o FMT em HIGH tira o som",
                normal && mudoNoGnd && mudoPeloPino && tocaPinoAlto && formatoErrado,
                $"{normal}/{mudoNoGnd}/{mudoPeloPino}/{tocaPinoAlto}/{formatoErrado}");
        }

        // ---- os LEDs de energia: o do I2C e o do PCM5102A ----
        foreach (var (tipo, vcc) in new[] { ("modulo-i2c", "VCC"), ("pcm5102a", "VIN") })
        {
            var c = new Circuito();
            c.Pecas.Add(new PecaNoCircuito { Id = "placa", Tipo = "esp32-devkit-v1", X = 0, Y = 0, Usb = true });
            c.Pecas.Add(new PecaNoCircuito { Id = "m", Tipo = tipo, X = 400, Y = 0 });
            var ed = new EditorCircuito { EmSimulacao = true };
            ed.Carregar(c);
            var maq = new Maquina { Perfil = PerfilPlaca.Esp32 };
            maq.Reiniciar();
            var b = Simulador.App.Bancada.DeUmaSo(maq);
            ed.AvancarEnergiaParaTeste(b, new Netlist(c), 0.04);
            bool apagado = !ed.LuzDeModuloAcesaParaTeste("m", 0);
            c.Fios.Add(new Fio { DePeca = "m", DePino = vcc, ParaPeca = "placa", ParaPino = "3V3" });
            c.Fios.Add(new Fio { DePeca = "m", DePino = "GND", ParaPeca = "placa", ParaPino = "GND.1" });
            ed.Carregar(c);
            ed.AvancarEnergiaParaTeste(b, new Netlist(c), 0.04);
            bool aceso = ed.LuzDeModuloAcesaParaTeste("m", 0);
            Conferir($"{tipo}: o LED de energia acende com o {vcc} e o GND ligados", apagado && aceso, $"{apagado}/{aceso}");
        }
    }
}
