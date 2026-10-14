namespace Simulador.App;

/// <summary>
/// A bateria funcional (Alpha 24): com bateria no circuito, a placa esta sem
/// o cabo USB e so liga se a bateria chegar nela -- no VIN, no 5V, no VSYS,
/// no 3V3, direto ou pelo regulador de um driver --, dentro da faixa daquele
/// pino. Sem bateria nenhuma, a placa esta na USB, como sempre. E o cabo pode
/// ser ligado (ou tirado) na barra da placa.
/// </summary>
public static partial class Autoteste
{
    static void Bateria_Alimentacao()
    {
        saida.AppendLine("bateria funcional: a placa sem o cabo USB, alimentada (ou nao) pela bateria");

        // ---------------- a conta: quem liga, quem nao, quem queima ----------------
        static PecaNoCircuito Placa(string tipo) => new() { Id = "placa", Tipo = tipo, X = 0, Y = 0 };
        static PecaNoCircuito Bateria(double volts, string id = "bat")
            => new() { Id = id, Tipo = "bateria", X = 400, Y = 300, Volts = volts, MiliAmperes = 20000, MiliAmperesHora = 1000 };
        static void Ligar(Circuito c, string a, string pa, string b, string pb)
            => c.Fios.Add(new Fio { DePeca = a, DePino = pa, ParaPeca = b, ParaPino = pb });
        static Energia Conta(Circuito c, bool rodando = true)
        {
            var maq = new Maquina { Perfil = PerfilPlaca.De(c.Placas().First().Tipo) };
            if (rodando) maq.Reiniciar(); else maq.Desligar();
            var semCabo = c.Placas().Where(p => !EditorCircuito.TemCaboUsb(p, c)).Select(p => p.Id).ToHashSet();
            return Energia.Calcular(c, Simulador.App.Bancada.DeUmaSo(maq), null, null, null, null, semCabo);
        }
        static string Contar(Energia.AlimentacaoDaPlaca a)
            => a is null ? "(na USB)" : $"ligada={a.Ligada} queimou={a.Queimou} pino={a.Pino} {a.Volts:0.##} V {a.Motivo}";

        // O cabo, no automatico: sem bateria, na USB; com bateria, sem o cabo.
        {
            var c = new Circuito();
            var placa = Placa("esp32-devkit-v1");
            c.Pecas.Add(placa);
            bool semBateria = EditorCircuito.TemCaboUsb(placa, c);
            c.Pecas.Add(Bateria(7.4));
            bool comBateria = EditorCircuito.TemCaboUsb(placa, c);
            placa.Usb = true;
            bool forcado = EditorCircuito.TemCaboUsb(placa, c);
            c.Pecas.RemoveAt(1);
            placa.Usb = false;
            bool tirado = EditorCircuito.TemCaboUsb(placa, c);
            Conferir("alimentacao: sem bateria a placa esta na USB; com bateria, sem o cabo; a barra muda os dois",
                semBateria && !comBateria && forcado && !tirado, $"{semBateria}/{comBateria}/{forcado}/{tirado}");
        }

        // A bateria na mesa, sem fio: a placa nao liga.
        {
            var c = new Circuito();
            c.Pecas.AddRange([Placa("esp32-devkit-v1"), Bateria(7.4)]);
            var a = Conta(c).AlimentacaoDe("placa");
            Conferir("alimentacao: a bateria solta na mesa nao liga a placa (e o aviso diz onde ligar)",
                a is { Ligada: false, Queimou: false } && a.Motivo.Contains("VIN"), Contar(a));
        }

        // So o + no VIN, sem o - no GND: o circuito nao fecha.
        {
            var c = new Circuito();
            c.Pecas.AddRange([Placa("esp32-devkit-v1"), Bateria(7.4)]);
            Ligar(c, "bat", "+", "placa", "VIN");
            var a = Conta(c).AlimentacaoDe("placa");
            Conferir("alimentacao: o + no VIN sem o - no GND nao fecha o circuito", a is { Ligada: false }, Contar(a));
        }

        // A LiPo 2S no VIN: liga, o regulador faz os 3,3 V, e ela puxa a corrente dela.
        {
            var c = new Circuito();
            c.Pecas.AddRange([Placa("esp32-devkit-v1"), Bateria(7.4)]);
            Ligar(c, "bat", "+", "placa", "VIN");
            Ligar(c, "bat", "-", "placa", "GND.2");
            var conta = Conta(c);
            var a = conta.AlimentacaoDe("placa");
            double tres = conta.TensaoDoPino("placa", "3V3");
            double ma = conta.CorrenteDaBateria("bat") * 1000;
            Conferir("alimentacao: a LiPo 2S no VIN liga a ESP32 (o regulador faz os 3,3 V, ela puxa ~80 mA)",
                a is { Ligada: true, Pino: "VIN" } && a.Volts > 7.0 && Math.Abs(tres - 3.3) < 0.1 && ma is > 50 and < 120,
                $"{Contar(a)} | 3V3 {tres:0.00} V | {ma:0} mA");

            // Parada (a simulacao desligada): a entrada esta boa, mas ela nao puxa nada.
            var parada = Conta(c, rodando: false);
            double maParada = parada.CorrenteDaBateria("bat") * 1000;
            Conferir("alimentacao: com a simulacao parada a placa nao puxa corrente da bateria",
                parada.AlimentacaoDe("placa") is { Ligada: true } && maParada < 2, $"{maParada:0.0} mA");
        }

        // Duas pilhas AA no VIN: 3 V nao bastam para o regulador.
        {
            var c = new Circuito();
            c.Pecas.AddRange([Placa("esp32-devkit-v1"), Bateria(3.0)]);
            Ligar(c, "bat", "+", "placa", "VIN");
            Ligar(c, "bat", "-", "placa", "GND.2");
            var a = Conta(c).AlimentacaoDe("placa");
            Conferir("alimentacao: 3 V no VIN nao bastam (o aviso diz quanto ela precisa)",
                a is { Ligada: false, Queimou: false, Pino: "VIN" } && (a.Motivo.Contains("4.3") || a.Motivo.Contains("4,3")), Contar(a));
        }

        // 3 pilhas AA direto no 3V3: queima.
        {
            var c = new Circuito();
            c.Pecas.AddRange([Placa("esp32-devkit-v1"), Bateria(4.5)]);
            Ligar(c, "bat", "+", "placa", "3V3");
            Ligar(c, "bat", "-", "placa", "GND.1");
            var a = Conta(c).AlimentacaoDe("placa");
            Conferir("alimentacao: 4,5 V direto no 3V3 queimam a ESP32", a is { Ligada: false, Queimou: true, Pino: "3V3" }, Contar(a));
        }

        // 3,3 V no 3V3 (uma fonte regulada): liga.
        {
            var c = new Circuito();
            c.Pecas.AddRange([Placa("esp32-devkit-v1"), Bateria(3.3)]);
            Ligar(c, "bat", "+", "placa", "3V3");
            Ligar(c, "bat", "-", "placa", "GND.1");
            var a = Conta(c).AlimentacaoDe("placa");
            Conferir("alimentacao: 3,3 V no 3V3 ligam a ESP32 direto", a is { Ligada: true, Pino: "3V3" }, Contar(a));
        }

        // Uma placa alimentando a outra: a B (antes na lista) no 3V3 da A, que esta na bateria.
        {
            var c = new Circuito();
            var b = new PecaNoCircuito { Id = "b", Tipo = "esp32-devkit-v1", X = 400, Y = 0 };
            c.Pecas.AddRange([b, Placa("esp32-devkit-v1"), Bateria(7.4)]);
            Ligar(c, "bat", "+", "placa", "VIN");
            Ligar(c, "bat", "-", "placa", "GND.2");
            Ligar(c, "placa", "3V3", "b", "3V3");
            Ligar(c, "placa", "GND.1", "b", "GND.1");
            var conta = Conta(c);
            var a = conta.AlimentacaoDe("b");
            Conferir("alimentacao: a placa no 3V3 de outra (que esta na bateria) liga, qualquer que seja a ordem",
                conta.AlimentacaoDe("placa") is { Ligada: true } && a is { Ligada: true, Pino: "3V3" } && a.Volts is > 3.2 and < 3.35,
                Contar(a));
        }

        // O Uno: 9 V no VIN liga (e o 5V vira 5 V); 6 V nao (o regulador pede ~7 V).
        {
            var c = new Circuito();
            c.Pecas.AddRange([Placa("arduino-uno"), Bateria(9)]);
            Ligar(c, "bat", "+", "placa", "VIN");
            Ligar(c, "bat", "-", "placa", "GND.2");
            var conta = Conta(c);
            var a = conta.AlimentacaoDe("placa");
            double cinco = conta.TensaoDoPino("placa", "5V");
            c.Pecas[1].Volts = 6;
            var fraca = Conta(c).AlimentacaoDe("placa");
            Conferir("alimentacao: o Uno liga com 9 V no VIN (o 5V vira 5 V) e nao liga com 6 V",
                a is { Ligada: true, Pino: "VIN" } && Math.Abs(cinco - 5) < 0.15 && fraca is { Ligada: false },
                $"{Contar(a)} | 5V {cinco:0.00} V | com 6 V: {Contar(fraca)}");
        }

        // O Pico pelo VSYS.
        {
            var c = new Circuito();
            c.Pecas.AddRange([Placa("pi-pico"), Bateria(4.5)]);
            Ligar(c, "bat", "+", "placa", "VSYS");
            Ligar(c, "bat", "-", "placa", "GND.5");
            var a = Conta(c).AlimentacaoDe("placa");
            Conferir("alimentacao: o Pico liga com 3 pilhas AA no VSYS", a is { Ligada: true, Pino: "VSYS" }, Contar(a));
        }

        // O regulador do L298N (5 V, com 7 V ou mais no 12V) liga a ESP32 pelo VIN.
        {
            var c = new Circuito();
            c.Pecas.AddRange([Placa("esp32-devkit-v1"), Bateria(11.1),
                              new PecaNoCircuito { Id = "driver", Tipo = "l298n", X = 300, Y = 0 }]);
            Ligar(c, "bat", "+", "driver", "12V");
            Ligar(c, "bat", "-", "driver", "GND");
            Ligar(c, "driver", "5V", "placa", "VIN");
            Ligar(c, "driver", "GND", "placa", "GND.2");
            var a = Conta(c).AlimentacaoDe("placa");
            c.Pecas[1].Volts = 6;
            var semRegulador = Conta(c).AlimentacaoDe("placa");
            Conferir("alimentacao: o 5V do L298N liga a ESP32 (e com 6 V no 12V o regulador nao regula)",
                a is { Ligada: true, Pino: "VIN" } && Math.Abs(a.Volts - 5) < 0.3 && semRegulador is { Ligada: false },
                $"{Contar(a)} | com 6 V: {Contar(semRegulador)}");
        }

        // O BEC do ESC (5 V do fio vermelho do cabo de servo) liga a ESP32.
        {
            var c = new Circuito();
            c.Pecas.AddRange([Placa("esp32-devkit-v1"), Bateria(11.1),
                              new PecaNoCircuito { Id = "esc", Tipo = "esc", X = 300, Y = 0 }]);
            Ligar(c, "bat", "+", "esc", "BAT+");
            Ligar(c, "bat", "-", "esc", "BAT-");
            Ligar(c, "esc", "5V", "placa", "VIN");
            Ligar(c, "esc", "GND", "placa", "GND.2");
            var a = Conta(c).AlimentacaoDe("placa");
            Conferir("alimentacao: o BEC do ESC liga a ESP32 pelo VIN", a is { Ligada: true, Pino: "VIN" }, Contar(a));
        }

        // Com o cabo ligado na barra, a placa fica na USB mesmo com a bateria.
        {
            var c = new Circuito();
            var placa = Placa("esp32-devkit-v1");
            placa.Usb = true;
            c.Pecas.AddRange([placa, Bateria(7.4)]);
            var conta = Conta(c);
            Conferir("alimentacao: com o cabo ligado a placa fica na USB (o 3V3 e o de sempre)",
                conta.AlimentacaoDe("placa") is null && Math.Abs(conta.TensaoDoPino("placa", "3V3") - 3.3) < 0.05,
                $"{Contar(conta.AlimentacaoDe("placa"))} | 3V3 {conta.TensaoDoPino("placa", "3V3"):0.00} V");
        }

        // Na USB e com a bateria no VIN: o trilho da USB so fornece (o diodo do
        // VIN) -- a bateria alimenta o pino, e nao vira um "curto" contra a USB.
        {
            var c = new Circuito();
            var placa = Placa("esp32-devkit-v1");
            placa.Usb = true;
            c.Pecas.AddRange([placa, Bateria(7.4)]);
            Ligar(c, "bat", "+", "placa", "VIN");
            Ligar(c, "bat", "-", "placa", "GND.2");
            var conta = Conta(c);
            double ma = conta.CorrenteDaBateria("bat") * 1000;
            double vin = conta.TensaoDoPino("placa", "VIN"), tres = conta.TensaoDoPino("placa", "3V3");
            Conferir("alimentacao: na USB com a LiPo no VIN nao ha curto (o VIN fica com a bateria, o 3V3 com 3,3 V)",
                Math.Abs(ma) < 5 && vin > 7.0 && Math.Abs(tres - 3.3) < 0.05 && conta.SobretensaoNaUsb(placa) is null,
                $"{ma:0.0} mA | VIN {vin:0.00} V | 3V3 {tres:0.00} V");

            // E sem bateria nenhuma o VIN da USB continua nos 5 V de sempre.
            c.Fios.Clear();
            var soUsb = Conta(c);
            Conferir("alimentacao: na USB, sem nada de fora, o VIN tem 5 V e o 3V3 3,3 V",
                Math.Abs(soUsb.TensaoDoPino("placa", "VIN") - 5) < 0.01 && Math.Abs(soUsb.TensaoDoPino("placa", "3V3") - 3.3) < 0.01,
                $"VIN {soUsb.TensaoDoPino("placa", "VIN"):0.00} V | 3V3 {soUsb.TensaoDoPino("placa", "3V3"):0.00} V");
        }

        // Na USB, 3 pilhas AA empurrando o 3V3: o regulador nao segura, e a ESP32 queima.
        {
            var c = new Circuito();
            var placa = Placa("esp32-devkit-v1");
            placa.Usb = true;
            c.Pecas.AddRange([placa, Bateria(4.5)]);
            Ligar(c, "bat", "+", "placa", "3V3");
            Ligar(c, "bat", "-", "placa", "GND.1");
            var a = Conta(c).SobretensaoNaUsb(placa);
            Conferir("alimentacao: na USB, 4,5 V de fora no 3V3 queimam a ESP32 (antes: um curto com a USB)",
                a is { Queimou: true, Pino: "3V3", Direto: true }, Contar(a));
        }

        // O cabo vai no diagram.json, e volta.
        {
            var c = new Circuito();
            var placa = Placa("esp32-devkit-v1");
            placa.Usb = true;
            c.Pecas.AddRange([placa, Bateria(7.4)]);
            string json = ExportarWokwi.Gerar(c);
            var volta = ImportarWokwi.Ler(json).Circuito.Achar("placa");
            placa.Usb = null;
            string semAtributo = ExportarWokwi.Gerar(c);
            Conferir("alimentacao: o cabo USB vai no diagram.json (\"usb\") e volta; no automatico nao aparece",
                json.Contains("\"usb\": \"true\"") && volta?.Usb == true && !semAtributo.Contains("\"usb\""),
                json.Length > 300 ? json[..300] : json);
        }

        // ---------------- na janela: a placa liga e apaga com a bateria ----------------
        string pasta = AcharABateria();
        if (pasta is null)
        {
            Conferir("alimentacao: a pasta da bateria existe", false, "nao achei bateria/");
            return;
        }

        {
            string caminho = System.IO.Path.Combine(pasta, "alimentacao", "bateria-solta.sktch");
            var (circuito, codigo) = Sktch.Abrir(caminho);
            var janela = new MainWindow();
            try
            {
                janela.TesteAbrir(circuito, codigo);
                janela.TesteRodar();
                var ed = janela.TesteEditorCircuito;
                var maquina = janela.TesteMaquinaDa("placa");
                bool ligou() => LinhasDoSerial(maquina).Any(l => l.Contains("ligou pela bateria"));

                EsperarPor(ligou, 1500);
                bool apagada = !ligou() && !maquina.Energizada;
                string status = janela.TesteStatus;
                Conferir("alimentacao (janela): F5 com a bateria solta -- a placa nao liga, e o aviso diz por que",
                    apagada && status.Contains("sem energia"), $"energizada={maquina.Energizada} | {status}");

                // O fio da bateria com a simulacao rodando: ela liga sozinha, do boot.
                ed.Atual.Fios.Add(new Fio { DePeca = "bateria", DePino = "+", ParaPeca = "placa", ParaPino = "VIN", Cor = "#D23B33" });
                ed.Atual.Fios.Add(new Fio { DePeca = "bateria", DePino = "-", ParaPeca = "placa", ParaPino = "GND.2", Cor = "#1A1A1A" });
                ed.AvisarQueMudou();
                bool ligouPeloFio = EsperarPor(ligou, 6000);
                bool piscou = EsperarPor(() => maquina.Digital[2] == 1, 3000) && EsperarPor(() => maquina.Digital[2] == 0, 3000);
                Conferir("alimentacao (janela): ligar a bateria no VIN com a simulacao rodando liga a placa, do boot",
                    ligouPeloFio && piscou && ed.PlacaComEnergia("placa"),
                    $"{string.Join(" | ", LinhasDoSerial(maquina).TakeLast(3))} | {ed.SituacaoDaEnergia("placa")} | {janela.TesteStatus}");

                // O fio do + sai: ela apaga.
                ed.Atual.Fios.RemoveAll(f => f.DePeca == "bateria" && f.DePino == "+");
                ed.AvisarQueMudou();
                bool apagou = EsperarPor(() => !maquina.Energizada, 3000);
                Conferir("alimentacao (janela): tirar o fio do + apaga a placa", apagou,
                    $"energizada={maquina.Energizada} | {janela.TesteStatus}");

                // O cabo USB pela barra: ela liga de novo, mesmo sem a bateria.
                ed.DefinirUsb(ed.Atual.Achar("placa"), true);
                bool voltou = EsperarPor(() => maquina.Energizada && LinhasDoSerial(maquina).Any(l => l.Contains("ligou pela bateria")), 6000);
                Conferir("alimentacao (janela): o cabo USB ligado na barra da placa a liga de novo",
                    voltou && ed.SituacaoDaEnergia("placa").Contains("USB"), $"{ed.SituacaoDaEnergia("placa")} | {janela.TesteStatus}");
            }
            finally
            {
                janela.TesteParar();
                try { janela.Close(); } catch (InvalidOperationException) { }
            }
        }

        // ---------------- as receitas da pasta alimentacao/ ----------------
        // Arquivo, o que se espera, e o motor que tem que girar (com o rpm minimo).
        (string Arquivo, string Espera, string Motor, double Rpm, double Segundos)[] casos =
        [
            ("bateria-solta", "apagada", null, 0, 1.2),
            ("bateria-no-vin", "ligada", null, 0, 1.5),
            ("uno-bateria-9v", "ligada", null, 0, 1.5),
            ("pilhas-fracas", "apagada", null, 0, 1.2),
            ("pilhas-no-3v3", "queimada", null, 0, 1.2),
            ("pico-pilhas-vsys", "ligada", null, 0, 1.5),
            ("l298n-alimenta-placa", "ligada", "motor", 60, 2.6),
            ("bec-alimenta-placa", "ligada", "motor", 1500, 6.8),
        ];
        foreach (var (arquivo, espera, motor, rpmMinimo, segundos) in casos)
        {
            string caminho = System.IO.Path.Combine(pasta, "alimentacao", arquivo + ".sktch");
            if (!System.IO.File.Exists(caminho))
            {
                Conferir($"alimentacao: {arquivo}.sktch existe", false, caminho);
                continue;
            }

            var (circuito, codigo) = Sktch.Abrir(caminho);
            var janela = new MainWindow();
            try
            {
                janela.TesteAbrir(circuito, codigo);
                janela.TesteRodar();
                var ed = janela.TesteEditorCircuito;
                var maquina = janela.TesteMaquinaDa("placa");
                double maiorRpm = 0;
                var avisos = new HashSet<string>();
                bool Pronto() => espera switch
                {
                    "ligada" => LinhasDoSerial(maquina).Any(l => !l.StartsWith("💥") && !l.StartsWith("⚠"))
                                && (motor is null || maiorRpm >= rpmMinimo),
                    "queimada" => ed.EstaQueimada("placa"),
                    _ => true,
                };

                var relogio = System.Diagnostics.Stopwatch.StartNew();
                while (relogio.Elapsed.TotalSeconds < segundos || (!Pronto() && relogio.Elapsed.TotalSeconds < 3 * segundos))
                {
                    EsperarPor(() => false, 120);
                    if (motor is not null) maiorRpm = Math.Max(maiorRpm, Math.Abs(ed.RpmDoMotor(motor)));
                    foreach (var estouro in ed.EstourosPelaEnergia()) avisos.Add($"{estouro.Peca}: {estouro.Motivo}");
                }

                // O que o sketch escreveu (o aviso de queima tambem vai para o monitor).
                var doSketch = LinhasDoSerial(maquina).Where(l => !l.StartsWith("💥") && !l.StartsWith("⚠")).ToList();
                string serial = string.Join(" | ", doSketch.Take(3));
                string situacao = ed.SituacaoDaEnergia("placa");
                bool rodou = doSketch.Count > 0;
                bool ok = espera switch
                {
                    "ligada" => rodou && maquina.Energizada && (motor is null || maiorRpm >= rpmMinimo) && avisos.Count == 0,
                    "apagada" => !rodou && !maquina.Energizada && situacao.StartsWith("sem energia"),
                    "queimada" => !rodou && ed.EstaQueimada("placa") && avisos.Any(a => a.StartsWith("placa:")),
                    _ => false,
                };
                string visto = $"{espera}: serial '{serial}' | {situacao}" + (motor is null ? "" : $" | {maiorRpm:0} rpm")
                               + (avisos.Count > 0 ? " | " + string.Join(" / ", avisos) : "");
                saida.AppendLine($"        {arquivo,-22} {(visto.Length > 200 ? visto[..200] : visto)}");
                Conferir($"alimentacao: {arquivo} -- a placa fica {espera}", ok, visto);
            }
            finally
            {
                janela.TesteParar();
                try { janela.Close(); } catch (InvalidOperationException) { }
            }
        }
    }
}
