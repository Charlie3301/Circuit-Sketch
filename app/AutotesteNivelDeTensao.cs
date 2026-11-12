namespace Simulador.App;

/// <summary>
/// O nivel de tensao errado em qualquer porta, nos dois sentidos: 5 V numa porta
/// de 3,3 V, 3,3 V numa porta de 5 V, a peca de 5 V no 3,3 V, 5 V no 3V3 da placa
/// e no VCC de uma peca de 3,3 V -- cada um com a marca no pino e com o que
/// acontece se ficar assim.
/// </summary>
public static partial class Autoteste
{
    static void Queima_NivelDeTensao()
    {
        saida.AppendLine("o nivel de tensao: maior numa porta menor, menor numa porta maior");

        // A ESP32 (3,3 V) e o Uno (5 V) na mesma bancada, energizados, com o GND em comum.
        (Circuito C, Bancada B) Montar(Action<Circuito> pecas, Action<Bancada> estado = null)
        {
            var c = new Circuito();
            c.Pecas.Add(new PecaNoCircuito { Id = "esp", Tipo = "esp32-devkit-v1", X = 0, Y = 0 });
            c.Pecas.Add(new PecaNoCircuito { Id = "uno", Tipo = "arduino-uno", X = 400, Y = 0 });
            c.Fios.Add(new Fio { DePeca = "esp", DePino = "GND.1", ParaPeca = "uno", ParaPino = "GND.1" });
            pecas(c);
            var b = new Bancada();
            b.Sincronizar(c);
            b.Reiniciar();
            estado?.Invoke(b);
            return (c, b);
        }
        static void Fio(Circuito c, string a, string pa, string b, string pb)
            => c.Fios.Add(new Fio { DePeca = a, DePino = pa, ParaPeca = b, ParaPino = pb });
        static void Saida(Bancada b, string placa, int gpio, bool alto)
        {
            var m = b.De(placa);
            m.Modo[gpio] = 1;
            m.Digital[gpio] = alto ? 1 : 0;
        }
        List<Estouro> Avisos(Circuito c, Bancada b)
            => [.. Queima.Conferir(c, new Netlist(c), b).Where(x => x.Marca is not null)];
        string Lista(List<Estouro> l) => string.Join(" | ", l.Select(x => $"{x.Peca}:{x.Pino} [{x.Marca}] {x.Motivo}"));

        // ---- 5 V numa porta de 3,3 V: a saida do Uno no pino da ESP32 ----
        {
            var (c, b) = Montar(c => Fio(c, "uno", "13", "esp", "D4"), b => Saida(b, "uno", 13, alto: false));
            var avisos = Avisos(c, b);
            var a = avisos.FirstOrDefault(x => x.Peca == "esp" && x.Pino == "D4");
            Conferir("nivel: o pino 13 do Uno (em OUTPUT, ate em LOW) no D4 da ESP32 -- \"5 V numa porta de 3,3 V\", com o que acontece",
                a is { Queimou: false, Marca: "5 V numa porta de 3,3 V" } && a.Motivo.Contains("Se ficar assim")
                && a.Motivo.Contains("diodos de proteção") && a.Motivo.Contains("divisor"),
                Lista(avisos));
        }

        // ---- 3,3 V numa porta de 5 V: a saida da ESP32 na entrada do Uno ----
        {
            var (c, b) = Montar(c => Fio(c, "esp", "D5", "uno", "2"), b => Saida(b, "esp", 5, alto: true));
            var avisos = Avisos(c, b);
            var a = avisos.FirstOrDefault(x => x.Peca == "uno" && x.Pino == "2");
            Conferir("nivel: o D5 da ESP32 na entrada 2 do Uno -- \"3,3 V numa porta de 5 V\", e o HIGH sem folga",
                a is { Queimou: false, Marca: "3,3 V numa porta de 5 V" } && a.Motivo.Contains("Se ficar assim")
                && a.Motivo.Contains("sem folga") && !avisos.Any(x => x.Peca == "esp" && x.Pino == "D5"),
                Lista(avisos));
        }

        // ---- a saida de um modulo de 5 V: o ECHO do HC-SR04 no Uno vai na ESP32 ----
        {
            var (c, b) = Montar(c =>
            {
                c.Pecas.Add(new PecaNoCircuito { Id = "hc", Tipo = "hc-sr04", X = 200, Y = 300 });
                Fio(c, "hc", "VCC", "uno", "5V");
                Fio(c, "hc", "GND", "uno", "GND.2");
                Fio(c, "hc", "ECHO", "esp", "D18");
            });
            var avisos = Avisos(c, b);
            var a = avisos.FirstOrDefault(x => x.Peca == "esp" && x.Pino == "D18");
            Conferir("nivel: o ECHO do HC-SR04 alimentado com 5 V no D18 da ESP32 -- 5 V numa porta de 3,3 V",
                a is { Marca: "5 V numa porta de 3,3 V" } && a.Motivo.Contains("ECHO"), Lista(avisos));
        }

        // ---- a peca de 5 V no 3,3 V ----
        {
            var (c, b) = Montar(c =>
            {
                c.Pecas.Add(new PecaNoCircuito { Id = "hc", Tipo = "hc-sr04", X = 200, Y = 300 });
                Fio(c, "hc", "VCC", "esp", "3V3");
                Fio(c, "hc", "GND", "esp", "GND.2");
            });
            var avisos = Avisos(c, b);
            var a = avisos.FirstOrDefault(x => x.Peca == "hc" && x.Pino == "VCC");
            Conferir("nivel: o HC-SR04 no 3V3 -- \"3,3 V numa peça de 5 V\", e o eco que nao volta",
                a is { Marca: "3,3 V numa peça de 5 V" } && a.Motivo.Contains("Se ficar assim") && a.Motivo.Contains("eco"),
                Lista(avisos));
        }

        // ---- 5 V no VCC de uma peca de 3,3 V, e no 3V3 da placa ----
        {
            var (c, b) = Montar(c =>
            {
                c.Pecas.Add(new PecaNoCircuito { Id = "nrf", Tipo = "nrf24l01", X = 200, Y = 300 });
                Fio(c, "nrf", "VCC", "uno", "5V");
                Fio(c, "nrf", "GND", "uno", "GND.2");
                Fio(c, "esp", "3V3", "uno", "VIN");
            });
            var avisos = Avisos(c, b);
            var nrf = avisos.FirstOrDefault(x => x.Peca == "nrf" && x.Pino == "VCC");
            var tres = avisos.FirstOrDefault(x => x.Peca == "esp" && x.Pino == "3V3");
            Conferir("nivel: o nRF24L01 no 5V (\"5 V numa peça de 3,6 V\") e o 3V3 da ESP32 no VIN do Uno (\"5 V num pino de 3,3 V\")",
                nrf is { Marca: "5 V numa peça de 3,6 V" } && nrf.Motivo.Contains("queima")
                && tres is { Marca: "5 V num pino de 3,3 V" } && tres.Motivo.Contains("regulador"),
                Lista(avisos));
        }

        // ---- o que nao e erro nao avisa ----
        {
            var (c, b) = Montar(c =>
            {
                c.Pecas.Add(new PecaNoCircuito { Id = "hc", Tipo = "hc-sr04", X = 200, Y = 300 });
                c.Pecas.Add(new PecaNoCircuito { Id = "nrf", Tipo = "nrf24l01", X = 300, Y = 300 });
                c.Pecas.Add(new PecaNoCircuito { Id = "r1", Tipo = "resistor", X = 200, Y = 400, Ohms = 1000 });
                Fio(c, "hc", "VCC", "uno", "5V");          // o HC-SR04 no 5 V do Uno, com o ECHO no Uno
                Fio(c, "hc", "GND", "uno", "GND.2");
                Fio(c, "hc", "ECHO", "uno", "7");
                Fio(c, "nrf", "VCC", "esp", "3V3");        // o nRF24L01 no 3V3
                Fio(c, "nrf", "GND", "esp", "GND.2");
                Fio(c, "uno", "12", "r1", "1");            // o Uno na ESP32 por um resistor
                Fio(c, "r1", "2", "esp", "D4");
                Fio(c, "esp", "VIN", "uno", "5V");         // os 5 V nos 5 V
            }, b => Saida(b, "uno", 12, alto: true));
            var avisos = Avisos(c, b);
            Conferir("nivel: o que esta certo (5 V nos 5 V, o 3V3 no nRF, o HC-SR04 no Uno, o resistor no meio) nao avisa",
                avisos.Count == 0, Lista(avisos));
        }

        // ---- na janela, rodando: o Uno piscando o 13 no D4 da ESP32, e o HC-SR04 no 3V3 ----
        {
            var c = new Circuito();
            c.Pecas.Add(new PecaNoCircuito { Id = "esp", Tipo = "esp32-devkit-v1", X = 40, Y = 40, Usb = true,
                                              Sketch = "void setup() {}\nvoid loop() { delay(100); }" });
            c.Pecas.Add(new PecaNoCircuito { Id = "uno", Tipo = "arduino-uno", X = 360, Y = 40, Usb = true,
                                              Sketch = "void setup() { pinMode(13, OUTPUT); }\nvoid loop() { digitalWrite(13, HIGH); delay(200); digitalWrite(13, LOW); delay(200); }" });
            c.Pecas.Add(new PecaNoCircuito { Id = "hc", Tipo = "hc-sr04", X = 80, Y = 330 });
            Fio(c, "esp", "GND.1", "uno", "GND.1");
            Fio(c, "uno", "13", "esp", "D4");
            Fio(c, "hc", "VCC", "esp", "3V3");
            Fio(c, "hc", "GND", "esp", "GND.2");
            var janela = new MainWindow();
            try
            {
                janela.TesteAbrir(c, "void setup() {}\nvoid loop() { delay(100); }");
                janela.TesteRodar();
                var ed = janela.TesteEditorCircuito;
                EsperarPor(() => ed.AvisosNaTelaParaTeste.Count >= 2, 10000);
                var marcas = ed.AvisosNaTelaParaTeste;
                Conferir("nivel na janela: rodando, as marcas aparecem no D4 da ESP32 e no VCC do HC-SR04",
                    marcas.Any(m => m.Peca == "esp" && m.Pino == "D4") && marcas.Any(m => m.Peca == "hc" && m.Pino == "VCC"),
                    string.Join(" | ", marcas) + " || Modo[13] do Uno = " + janela.TesteMaquinaDa("uno").Modo[13]);
            }
            finally { janela.TesteParar(); }
        }

        // ---- a marca no pino ----
        {
            var (c, b) = Montar(c => Fio(c, "uno", "13", "esp", "D4"), b => Saida(b, "uno", 13, alto: true));
            var ed = new EditorCircuito();
            ed.Carregar(c);
            ed.MostrarAvisos(Avisos(c, b));
            var marcas = ed.AvisosNaTelaParaTeste;
            ed.MostrarAvisos([]);
            Conferir("nivel: a marca amarela vai no pino D4 da ESP32, com o texto curto, e some quando a ligacao sai",
                marcas.Any(m => m.Peca == "esp" && m.Pino == "D4" && m.Marca == "5 V numa porta de 3,3 V")
                && ed.AvisosNaTelaParaTeste.Count == 0,
                string.Join(" | ", marcas));
        }
    }
}
