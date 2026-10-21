namespace Simulador.App;

/// <summary>
/// Os atuadores e a alimentacao redesenhados (Alpha 25): o buzzer de 12 mm com
/// as pernas a 7,6 mm (o 2 e o +, como no Wokwi), o alto-falante de 28 mm, o
/// KS2E-M-DC5 na grade do DIP-16, os modulos de rele sem a marca, o 78xx no
/// TO-220, o modulo do AMS1117 com os quatro pinos da placa (e o LED na saida),
/// o LM2596 com as ilhas nos cantos e o MT3608 com a saida do lado do trimpot.
/// </summary>
public static partial class Autoteste
{
    static void AtuadoresAlimentacao_Desenho()
    {
        saida.AppendLine("atuadores e alimentacao: o desenho, os pinos e o que mudou neles");

        static PinoDef P(PecaDef d, string n) => d.Pinos.First(p => p.Nome == n);
        const double Mm = 96 / 25.4;
        static bool Perto(double a, double b, double mm) => Math.Abs(Math.Abs(a - b) - mm * Mm) < 0.15;

        // ---- o tamanho real e os pinos dentro da caixa ----
        foreach (var (tipo, pinos, larg, alt) in new[] {
                     ("buzzer", 2, 46, 63), ("alto-falante", 2, 106, 123), ("ks2e-m-dc5", 8, 79, 38),
                     ("modulo-rele-1", 6, 98, 197), ("modulo-rele-2", 10, 151, 197), ("modulo-rele-4", 18, 287, 197),
                     ("regulador-7805", 3, 38, 102), ("ams1117", 4, 94, 42), ("lm2596", 4, 163, 79), ("mt3608", 4, 141, 65) })
        {
            var d = Catalogo.Obter(tipo);
            bool dentro = d.Pinos.All(p => p.X >= 0 && p.X <= d.Largura && p.Y >= 0 && p.Y <= d.Altura);
            Conferir($"{tipo}: {pinos} pinos, a caixa {larg}x{alt} px (o tamanho real), todos dentro dela",
                d.Pinos.Count == pinos && d.Largura == larg && d.Altura == alt && dentro && Pecas.Existe(tipo),
                $"{d.Pinos.Count} pinos, {d.Largura}x{d.Altura}, dentro {dentro}");
        }

        // ---- o buzzer: as pernas a 7,6 mm, o + (o 2) a direita, embaixo do corpo ----
        {
            var d = Catalogo.Obter("buzzer");
            Conferir("buzzer: as pernas a 7,6 mm (cabem na protoboard), o 2 (+) a direita do 1 (-), na mesma linha",
                Perto(P(d, "1").X, P(d, "2").X, 7.6) && P(d, "2").X > P(d, "1").X && Math.Abs(P(d, "1").Y - P(d, "2").Y) < 0.1
                && P(d, "2").Descricao.Contains('+') && P(d, "1").Descricao.Contains("GND"),
                $"1 ({P(d, "1").X},{P(d, "1").Y}) 2 ({P(d, "2").X},{P(d, "2").Y})");

            // As demos ligam o pino que toca no + e o - no GND, como o desenho mostra.
            var erradas = new List<string>();
            int comBuzzer = 0;
            foreach (var demo in Demos.Todas)
            {
                using var json = System.Text.Json.JsonDocument.Parse(Demos.Texto(demo.Recurso) ?? "{}");
                if (!json.RootElement.TryGetProperty("circuito", out var circ)) continue;
                var buzzers = circ.GetProperty("Pecas").EnumerateArray()
                                  .Where(p => p.GetProperty("Tipo").GetString() == "buzzer")
                                  .Select(p => p.GetProperty("Id").GetString()).ToHashSet();
                if (buzzers.Count == 0) continue;
                comBuzzer++;
                foreach (var fio in circ.GetProperty("Fios").EnumerateArray())
                {
                    foreach (var (peca, pino, outroPino) in new[] { ("DePeca", "DePino", "ParaPino"), ("ParaPeca", "ParaPino", "DePino") })
                    {
                        if (!buzzers.Contains(fio.GetProperty(peca).GetString())) continue;
                        string noBuzzer = fio.GetProperty(pino).GetString(), naPlaca = fio.GetProperty(outroPino).GetString();
                        bool terra = Netlist.EhPinoDeTerra(naPlaca);
                        if (terra != (noBuzzer == "1")) erradas.Add($"{demo.Recurso}: {noBuzzer}-{naPlaca}");
                    }
                }
            }
            Conferir("buzzer: nas demos, o GND vai no 1 (-) e o pino que toca no 2 (+)",
                comBuzzer >= 10 && erradas.Count == 0, $"{comBuzzer} demos; " + string.Join(", ", erradas));
        }

        // ---- o KS2E-M-DC5: as pernas 8, 6, 4 e 1 do DIP-16 (e as do outro lado) ----
        {
            var d = Catalogo.Obter("ks2e-m-dc5");
            bool grade = new[] { "1", "2" }.All(k =>
                Perto(P(d, "NO" + k).X, P(d, "NC" + k).X, 5.08) && Perto(P(d, "NC" + k).X, P(d, "P" + k).X, 5.08)
                && Perto(P(d, "P" + k).X, P(d, "COIL" + k).X, 7.62) && P(d, "COIL" + k).X > P(d, "P" + k).X);
            bool fileiras = Perto(P(d, "NO1").Y, P(d, "NO2").Y, 7.62) && Perto(P(d, "COIL1").Y, P(d, "COIL2").Y, 7.62);
            Conferir("ks2e-m-dc5: NO, NC, P e a bobina na grade do DIP-16; as duas fileiras a 7,62 mm",
                grade && fileiras, $"NO1 {P(d, "NO1").X} NC1 {P(d, "NC1").X} P1 {P(d, "P1").X} COIL1 {P(d, "COIL1").X}");
        }

        // ---- sem as marcas: o rele dos modulos sem "SONGLE", o KS2E sem o fabricante ----
        {
            var svgs = Pecas.TodosOsSvgs().ToDictionary(s => s.nome, s => s.texto);
            string Svg(string tipo) => svgs.GetValueOrDefault(tipo) ?? "";
            bool semMarca = new[] { "modulo-rele-1", "modulo-rele-2", "modulo-rele-4" }.All(t => !Svg(t).Contains("SONGLE") && Svg(t).Contains("SRD-05VDC-SL-C"))
                            && !Svg("ks2e-m-dc5").Contains("KEST") && Svg("ks2e-m-dc5").Contains("KS2E-M-DC5");
            Conferir("modulos de rele e KS2E: o modelo impresso, sem a marca do fabricante", semMarca);
        }

        // ---- o AMS1117: VIN e GND numa ponta, VOUT e GND.2 na outra, os dois GND um so ----
        {
            var d = Catalogo.Obter("ams1117");
            bool pontas = P(d, "VIN").X < d.Largura / 3.0 && P(d, "GND").X < d.Largura / 3.0
                          && P(d, "VOUT").X > d.Largura * 2 / 3.0 && P(d, "GND.2").X > d.Largura * 2 / 3.0
                          && Perto(P(d, "VIN").Y, P(d, "GND").Y, 2.54) && Perto(P(d, "VOUT").Y, P(d, "GND.2").Y, 2.54);
            var c0 = new Circuito();
            c0.Pecas.Add(new PecaNoCircuito { Id = "reg", Tipo = "ams1117", X = 0, Y = 0 });
            var r0 = new Netlist(c0);
            Conferir("ams1117: VIN/GND numa ponta e VOUT/GND.2 na outra (2,54 mm), e os dois GND sao a mesma trilha",
                pontas && r0.RaizDe(Netlist.No("reg", "GND")) == r0.RaizDe(Netlist.No("reg", "GND.2"))
                && Reguladores.PinosDe("ams1117") == ("VIN", "GND", "VOUT", "GND.2"));

            // O LED do modulo fica na saida: apagado sem entrada, aceso com 5 V no VIN.
            var c = new Circuito();
            c.Pecas.Add(new PecaNoCircuito { Id = "placa", Tipo = "esp32-devkit-v1", X = 0, Y = 0, Usb = true });
            c.Pecas.Add(new PecaNoCircuito { Id = "m", Tipo = "ams1117", X = 400, Y = 0, Volts = 3.3 });
            var ed = new EditorCircuito { EmSimulacao = true };
            ed.Carregar(c);
            var maq = new Maquina { Perfil = PerfilPlaca.Esp32 };
            maq.Reiniciar();
            var b = Simulador.App.Bancada.DeUmaSo(maq);
            ed.AvancarEnergiaParaTeste(b, new Netlist(c), 0.04);
            bool apagado = !ed.LuzDeModuloAcesaParaTeste("m", 0);
            c.Fios.Add(new Fio { DePeca = "m", DePino = "VIN", ParaPeca = "placa", ParaPino = "VIN" });
            c.Fios.Add(new Fio { DePeca = "m", DePino = "GND", ParaPeca = "placa", ParaPino = "GND.1" });
            ed.Carregar(c);
            ed.AvancarEnergiaParaTeste(b, new Netlist(c), 0.04);
            bool aceso = ed.LuzDeModuloAcesaParaTeste("m", 0);
            Conferir("ams1117: o LED da saida acende com 5 V no VIN (e fica apagado sem entrada)", apagado && aceso,
                $"{apagado}/{aceso}");
        }

        // ---- o LM2596: as quatro ilhas nos cantos; o MT3608: a saida do lado do trimpot ----
        {
            var d = Catalogo.Obter("lm2596");
            bool cantos = d.Pinos.All(p => Math.Min(p.X, d.Largura - p.X) < 2 * Mm && Math.Min(p.Y, d.Altura - p.Y) < 2 * Mm)
                          && P(d, "IN+").X < d.Largura / 2.0 && P(d, "IN+").Y < P(d, "IN-").Y && P(d, "OUT+").Y < P(d, "OUT-").Y;
            var m = Catalogo.Obter("mt3608");
            bool lados = P(m, "VOUT+").X < m.Largura / 2.0 && P(m, "VIN+").X > m.Largura / 2.0
                         && P(m, "VIN+").Y < P(m, "VIN-").Y && P(m, "VOUT+").Y < P(m, "VOUT-").Y;
            Conferir("lm2596: as ilhas nos quatro cantos (IN a esquerda, o + em cima); mt3608: VOUT a esquerda, VIN a direita",
                cantos && lados);
        }

        // ---- o 78xx: as tres pernas a 2,54 mm, na ordem IN, GND, OUT ----
        {
            var d = Catalogo.Obter("regulador-7805");
            Conferir("regulador-7805: IN, GND e OUT da esquerda para a direita, a 2,54 mm, na ponta das pernas",
                Perto(P(d, "IN").X, P(d, "GND").X, 2.54) && Perto(P(d, "GND").X, P(d, "OUT").X, 2.54)
                && P(d, "IN").X < P(d, "OUT").X && P(d, "IN").Y > d.Altura * 0.9);
        }
    }
}
