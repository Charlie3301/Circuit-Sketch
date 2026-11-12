namespace Simulador.App;

/// <summary>
/// O 74HC595: a pinagem da ficha, a fila e o latch pela conta da Logica (o
/// relogio num botao), e o chip escutando os pinos da placa -- o shiftOut,
/// dois chips em fila pelo Q7S, o SPI de hardware byte a byte e em bloco, o
/// MR e o OE -- com LEDs nas saidas.
/// </summary>
public static partial class Autoteste
{
    static void Chips_595()
    {
        saida.AppendLine("o 74HC595: registrador de deslocamento");
        const double mm = 96 / 25.4;
        static void Ligar(Circuito c, string a, string pa, string b, string pb)
            => c.Fios.Add(new Fio { DePeca = a, DePino = pa, ParaPeca = b, ParaPino = pb });

        // ---- a peca: o DIP-16 da ficha da Nexperia, com os nomes do Wokwi ----
        {
            var d = Catalogo.Obter("74hc595");
            string[] ficha = ["Q1", "Q2", "Q3", "Q4", "Q5", "Q6", "Q7", "GND", "Q7S", "MR", "SHCP", "STCP", "OE", "DS", "Q0", "VCC"];
            var ys = d?.Pinos.Select(p => Math.Round(p.Y / mm, 2)).Distinct().OrderBy(y => y).ToArray() ?? [];
            bool certo = d is not null && Pecas.Existe("74hc595") && d.Categoria == "Logica"
                         && d.Pinos.Select(p => p.Nome).SequenceEqual(ficha)
                         && ys.Length == 2 && Math.Abs(ys[1] - ys[0] - 7.62) < 0.06
                         && Math.Abs(d.Pinos[0].Y / mm - ys[1]) < 0.06;
            Conferir("74HC595: DIP-16 com a pinagem da ficha (Q1..Q7, GND, Q7S, MR, SHCP, STCP, OE, DS, Q0, VCC), o pino 1 embaixo a esquerda",
                certo, d is null ? "(sem a peca)" : string.Join(",", d.Pinos.Select(p => p.Nome)));
            Conferir("74HC595: o wokwi-74hc595 abre como o 74HC595",
                FormatoWokwi.TipoDoWokwi("wokwi-74hc595") == "74hc595", FormatoWokwi.TipoDoWokwi("wokwi-74hc595") ?? "(nada)");
        }

        // ---- pela conta da Logica: o relogio e o dado vindos de niveis fixos ----
        {
            var memoria = new Dictionary<string, int>();
            Logica Quadro(int ds, int sh, int st, int mr = 1, int oe = 0)
            {
                var c = new Circuito();
                c.Pecas.Add(new PecaNoCircuito { Id = "placa", Tipo = "esp32-devkit-v1", X = 0, Y = 0, Usb = true });
                c.Pecas.Add(new PecaNoCircuito { Id = "vcc", Tipo = "vcc-symbol", X = 600, Y = 0 });
                c.Pecas.Add(new PecaNoCircuito { Id = "gnd", Tipo = "gnd-symbol", X = 600, Y = 200 });
                c.Pecas.Add(new PecaNoCircuito { Id = "ci", Tipo = "74hc595", X = 300, Y = 300 });
                Ligar(c, "ci", "VCC", "placa", "3V3"); Ligar(c, "ci", "GND", "placa", "GND.1");
                foreach (var (pino, v) in new[] { ("DS", ds), ("SHCP", sh), ("STCP", st), ("MR", mr), ("OE", oe) })
                    Ligar(c, v == 1 ? "vcc" : "gnd", v == 1 ? "VCC" : "GND", "ci", pino);
                var maq = new Maquina { Perfil = PerfilPlaca.Esp32 };
                maq.Reiniciar();
                return Logica.Avaliar(c, new Netlist(c), Simulador.App.Bancada.DeUmaSo(maq), memoria, 0);
            }
            string Saidas(Logica l) => string.Concat(Enumerable.Range(0, 8).Reverse().Select(q => l.NivelDoPino("ci", $"Q{q}") switch
            {
                1 => "1", 0 => "0", _ => "z",
            }));

            // 1, 0, 1, 1 entram (o primeiro vai mais longe): a fila fica 1011 nos Q3..Q0.
            foreach (int bit in new[] { 1, 0, 1, 1 }) { Quadro(bit, 0, 0); Quadro(bit, 1, 0); }
            string antes = Saidas(Quadro(1, 1, 0));
            string travado = Saidas(Quadro(0, 0, 1));
            string semOe = Saidas(Quadro(0, 0, 1, oe: 1));
            Quadro(0, 0, 0, mr: 0);
            Quadro(0, 0, 1, mr: 0);
            string zerado = Saidas(Quadro(0, 0, 1));
            Conferir("74HC595 pela Logica: os bits andam na subida do SHCP, as saidas so mudam na subida do STCP, o OE em HIGH solta e o MR zera a fila",
                antes == "00000000" && travado == "00001011" && semOe == "zzzzzzzz" && zerado == "00000000",
                $"{antes} {travado} {semOe} {zerado}");
        }

        // ---- rodando: a placa manda no relogio, as saidas acendem LEDs ----
        (string Serial, Logica L, EditorCircuito Ed) Rodar(string placa, string sketch, Action<Circuito> montar, string[] pecas, Func<Logica, bool> pronto)
        {
            var c = new Circuito();
            c.Pecas.Add(new PecaNoCircuito { Id = "placa", Tipo = placa, X = 0, Y = 0, Usb = true, Sketch = sketch });
            c.Pecas.Add(new PecaNoCircuito { Id = "vcc", Tipo = "vcc-symbol", X = 700, Y = 0 });
            c.Pecas.Add(new PecaNoCircuito { Id = "gnd", Tipo = "gnd-symbol", X = 700, Y = 500 });
            const string gnd = "GND.1";
            string vcc = placa == "arduino-uno" ? "5V" : "3V3";
            int k = 0;
            foreach (string id in pecas)
            {
                c.Pecas.Add(new PecaNoCircuito { Id = id, Tipo = "74hc595", X = 400, Y = 100 + 160 * k++ });
                Ligar(c, id, "VCC", "placa", vcc); Ligar(c, id, "GND", "placa", gnd);
                Ligar(c, id, "OE", "gnd", "GND"); Ligar(c, id, "MR", "vcc", "VCC");
            }
            montar(c);
            // Um LED em cada saida do primeiro chip (o Q0 e o Q7, pelo resistor).
            foreach (var (q, i) in new[] { ("Q0", 0), ("Q7", 1) })
            {
                c.Pecas.Add(new PecaNoCircuito { Id = $"r{i}", Tipo = "resistor", X = 600, Y = 100 + 60 * i, Ohms = 220 });
                c.Pecas.Add(new PecaNoCircuito { Id = $"led{i}", Tipo = "led", X = 660, Y = 100 + 60 * i, Cor = "vermelho" });
                Ligar(c, pecas[0], q, $"r{i}", "1"); Ligar(c, $"r{i}", "2", $"led{i}", "A"); Ligar(c, $"led{i}", "C", "placa", gnd);
            }
            var janela = new MainWindow();
            try
            {
                janela.TesteAbrir(c, sketch);
                janela.TesteRodar();
                var maq = janela.TesteMaquinaDa("placa");
                var ed = janela.TesteEditorCircuito;
                EsperarPor(() => LinhasDoSerial(maq).Contains("pronto") && ed.LogicaAtual is { } l && pronto(l), 20000);
                return (string.Join(" | ", LinhasDoSerial(maq)), ed.LogicaAtual, ed);
            }
            finally { janela.TesteParar(); }
        }
        static string Byte(Logica l, string id) => l is null ? "(sem conta)"
            : string.Concat(Enumerable.Range(0, 8).Reverse().Select(q => l.NivelDoPino(id, $"Q{q}") switch { 1 => "1", 0 => "0", _ => "z" }));

        // O Uno com o shiftOut: dois chips em fila (o Q7S do primeiro no DS do segundo).
        {
            const string sketch = """
                const int DS = 8, SH = 12, ST = 7;
                void setup() {
                  Serial.begin(9600);
                  pinMode(DS, OUTPUT); pinMode(SH, OUTPUT); pinMode(ST, OUTPUT);
                  digitalWrite(ST, LOW);
                  shiftOut(DS, SH, MSBFIRST, 0xA5);   // vai para o segundo chip
                  shiftOut(DS, SH, MSBFIRST, 0x81);   // fica no primeiro
                  digitalWrite(ST, HIGH);
                  Serial.println("pronto");
                }
                void loop() {}
                """;
            var (serial, l, ed) = Rodar("arduino-uno", sketch, c =>
            {
                foreach (string id in new[] { "a", "b" }) { Ligar(c, id, "SHCP", "placa", "12"); Ligar(c, id, "STCP", "placa", "7"); }
                Ligar(c, "a", "DS", "placa", "8");
                Ligar(c, "a", "Q7S", "b", "DS");
            }, ["a", "b"], l => Byte(l, "a") == "10000001" && Byte(l, "b") == "10100101");
            string a = Byte(l, "a"), b = Byte(l, "b");
            Conferir("74HC595 no Uno: o shiftOut de dois bytes passa o primeiro pelo Q7S ao segundo chip, e o STCP mostra os dois (10000001 e 10100101)",
                a == "10000001" && b == "10100101", $"{a} {b} | {serial}");
            bool acesos = (ed.EnergiaDoQuadro?.CorrenteDoRamo("led0", "led") ?? 0) > 0.001
                          && (ed.EnergiaDoQuadro?.CorrenteDoRamo("led1", "led") ?? 0) > 0.001;
            Conferir("74HC595 no Uno: o Q0 e o Q7 em HIGH acendem os LEDs pelo resistor", acesos);
        }

        // A ESP32 com o SPI de hardware: byte a byte (os bits vao pelos pinos) e em
        // bloco (16 bytes de uma vez, direto no chip): os dois ultimos ficam na fila.
        foreach (var (envio, esperaA, esperaB) in new[]
        {
            ("SPI.transfer(0xA5); SPI.transfer(0x3C);", "00111100", "10100101"),
            ("uint8_t bloco[16]; for (int i = 0; i < 16; i++) bloco[i] = i; bloco[14] = 0x5A; bloco[15] = 0xC3; SPI.writeBytes(bloco, 16);",
             "11000011", "01011010"),
        })
        {
            string sketch = $$"""
                #include <SPI.h>
                const int LATCH = 5;
                void setup() {
                  Serial.begin(115200);
                  pinMode(LATCH, OUTPUT);
                  SPI.begin(18, 19, 23, -1);
                  SPI.beginTransaction(SPISettings(8000000, MSBFIRST, SPI_MODE0));
                  digitalWrite(LATCH, LOW);
                  {{envio}}
                  digitalWrite(LATCH, HIGH);
                  SPI.endTransaction();
                  Serial.println("pronto");
                }
                void loop() {}
                """;
            var (serial, l, _) = Rodar("esp32-devkit-v1", sketch, c =>
            {
                foreach (string id in new[] { "a", "b" }) { Ligar(c, id, "SHCP", "placa", "D18"); Ligar(c, id, "STCP", "placa", "D5"); }
                Ligar(c, "a", "DS", "placa", "D23");
                Ligar(c, "a", "Q7S", "b", "DS");
            }, ["a", "b"], l => Byte(l, "a") == esperaA && Byte(l, "b") == esperaB);
            string a = Byte(l, "a"), b = Byte(l, "b");
            Conferir($"74HC595 na ESP32: o SPI de hardware {(envio.Contains("writeBytes") ? "em bloco (writeBytes de 16)" : "byte a byte (transfer)")} chega nos dois chips ({esperaA} e {esperaB})",
                a == esperaA && b == esperaB, $"{a} {b} | {serial}");
        }

        // O MR num pino da placa zera a fila; o latch so muda no STCP seguinte.
        {
            const string sketch = """
                const int DS = 8, SH = 12, ST = 7, MR = 4;
                void setup() {
                  Serial.begin(9600);
                  pinMode(DS, OUTPUT); pinMode(SH, OUTPUT); pinMode(ST, OUTPUT); pinMode(MR, OUTPUT);
                  digitalWrite(MR, HIGH);
                  shiftOut(DS, SH, MSBFIRST, 0xFF);
                  digitalWrite(MR, LOW);
                  digitalWrite(MR, HIGH);
                  digitalWrite(DS, HIGH);
                  digitalWrite(SH, HIGH);
                  digitalWrite(SH, LOW);
                  digitalWrite(ST, HIGH);
                  Serial.println("pronto");
                }
                void loop() {}
                """;
            var (serial, l, _) = Rodar("arduino-uno", sketch, c =>
            {
                // Este chip tem o MR na placa: sai o fio do VCC que o Rodar pos.
                c.Fios.RemoveAll(f => f.DePeca == "a" && f.DePino == "MR");
                Ligar(c, "a", "SHCP", "placa", "12"); Ligar(c, "a", "STCP", "placa", "7");
                Ligar(c, "a", "DS", "placa", "8"); Ligar(c, "a", "MR", "placa", "4");
            }, ["a"], l => Byte(l, "a") == "00000001");
            string a = Byte(l, "a");
            Conferir("74HC595: o MR em LOW zera a fila (sem ele seria 11111111): so o bit novo aparece no STCP (00000001)",
                a == "00000001", $"{a} | {serial}");
        }
    }
}
