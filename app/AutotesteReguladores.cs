namespace Simulador.App;

/// <summary>
/// Os reguladores de tensao (Alpha 24): o 78xx, o AMS1117, o step-down
/// LM2596 e o step-up MT3608 -- a saida, a folga, o que puxam da entrada,
/// a protecao de corrente, a de tensao, o calor e a placa ligada por eles.
/// </summary>
public static partial class Autoteste
{
    static void Reguladores_DeTensao()
    {
        saida.AppendLine("reguladores de tensao: 78xx, AMS1117, LM2596 e MT3608");

        static PecaNoCircuito Bat(double volts, double mA = 20000)
            => new() { Id = "bat", Tipo = "bateria", X = 0, Y = 300, Volts = volts, MiliAmperes = mA, MiliAmperesHora = 2000 };
        static PecaNoCircuito Reg(string tipo, string modelo = null, double? volts = null)
            => new() { Id = "reg", Tipo = tipo, X = 200, Y = 300, Modelo = modelo, Volts = volts ?? Reguladores.VoltsDeFabrica(tipo) ?? 9 };
        static PecaNoCircuito Carga(double ohms) => new() { Id = "carga", Tipo = "resistor", X = 400, Y = 300, Ohms = ohms };
        static void Ligar(Circuito c, string a, string pa, string b, string pb)
            => c.Fios.Add(new Fio { DePeca = a, DePino = pa, ParaPeca = b, ParaPino = pb });
        static Energia Conta(Circuito c, IReadOnlySet<string> desligados = null)
        {
            var maq = new Maquina { Perfil = PerfilPlaca.Esp32 };
            maq.Reiniciar();
            var semCabo = c.Placas().Where(p => !EditorCircuito.TemCaboUsb(p, c)).Select(p => p.Id).ToHashSet();
            return Energia.Calcular(c, Simulador.App.Bancada.DeUmaSo(maq), null, null, null, null, semCabo, desligados);
        }
        // A bateria na entrada e um resistor de carga na saida.
        static Circuito Montar(PecaNoCircuito reg, double volts, double ohms)
        {
            var c = new Circuito();
            c.Pecas.AddRange([Bat(volts), reg, Carga(ohms)]);
            var (e, te, s, ts) = Reguladores.PinosDe(reg.Tipo);
            Ligar(c, "bat", "+", "reg", e);
            Ligar(c, "bat", "-", "reg", te);
            Ligar(c, "reg", s, "carga", "1");
            Ligar(c, "carga", "2", "reg", ts);
            return c;
        }
        static string Contar(Energia.EstadoDoRegulador e)
            => e is null ? "(sem estado)" : $"{e.Entrada:0.00} V/{e.CorrenteDeEntrada * 1000:0.0} mA -> {e.Saida:0.00} V/{e.CorrenteDeSaida * 1000:0.0} mA, "
                                          + $"{e.Potencia:0.00} W, queda={e.EmQueda} limite={e.Limitando} sobre={e.SobreTensao} passando={e.SoPassando}";

        // ---- 7805: 9 V para 5 V, 100 ohms (50 mA): entra o que sai, mais ~5 mA dele ----
        {
            var c = Montar(Reg("regulador-7805"), 9, 100);
            var conta = Conta(c);
            var e = conta.RegulagemDe("reg");
            double bateria = conta.CorrenteDaBateria("bat") * 1000;
            Conferir("regulador: o 7805 faz 5 V de 9 V, e da bateria sai a corrente da carga mais ~5 mA (linear)",
                e is { Ligado: true, EmQueda: false } && Math.Abs(e.Saida - 5) < 0.02 && Math.Abs(bateria - 55) < 1.5
                && Math.Abs(e.Potencia - (4 * 0.05 + 9 * 0.005)) < 0.02, $"{Contar(e)} | bateria {bateria:0.0} mA");
        }

        // ---- 7805 com 6 V: sem os 2 V de folga, ele so da 4 V ----
        {
            var e = Conta(Montar(Reg("regulador-7805"), 6, 100)).RegulagemDe("reg");
            Conferir("regulador: o 7805 com 6 V na entrada so da ~4 V (precisa de 2 V de folga)",
                e is { EmQueda: true } && Math.Abs(e.Saida - 4.0) < 0.1, Contar(e));
        }

        // ---- 7812, e a entrada acima do maximo ----
        {
            var e12 = Conta(Montar(Reg("regulador-7805", "7812"), 15, 1000)).RegulagemDe("reg");
            var e40 = Conta(Montar(Reg("regulador-7805"), 40, 100)).RegulagemDe("reg");
            Conferir("regulador: o 7812 da 12 V; o 7805 com 40 V na entrada (maximo 35) queima e nao da nada",
                e12 is not null && Math.Abs(e12.Saida - 12) < 0.05 && e40 is { SobreTensao: true, Ligado: false } && e40.Saida < 0.1,
                $"{Contar(e12)} || {Contar(e40)}");
        }

        // ---- AMS1117: 5 V para 3,3 V; com 4 V, so 2,9 V ----
        {
            var e5 = Conta(Montar(Reg("ams1117"), 5, 330)).RegulagemDe("reg");
            var e4 = Conta(Montar(Reg("ams1117"), 4, 330)).RegulagemDe("reg");
            var e50 = Conta(Montar(Reg("ams1117", "5.0"), 7.4, 330)).RegulagemDe("reg");
            Conferir("regulador: o AMS1117-3.3 da 3,3 V de 5 V e ~2,9 V de 4 V; o de 5 V da 5 V de 7,4 V",
                e5 is not null && Math.Abs(e5.Saida - 3.3) < 0.02 && e4 is { EmQueda: true } && Math.Abs(e4.Saida - 2.9) < 0.08
                && e50 is not null && Math.Abs(e50.Saida - 5.0) < 0.02, $"{Contar(e5)} || {Contar(e4)} || {Contar(e50)}");
        }

        // ---- LM2596: 12 V para 5 V com 1 A -- da bateria sai menos da metade ----
        {
            var c = Montar(Reg("lm2596"), 12, 5);
            var conta = Conta(c);
            var e = conta.RegulagemDe("reg");
            double bateria = conta.CorrenteDaBateria("bat");
            double esperada = e is null ? 0 : e.Saida * e.CorrenteDeSaida / (0.85 * e.Entrada) + 0.005;
            Conferir("regulador: o LM2596 (step-down) faz 5 V de 12 V e, com 1 A na saida, puxa ~0,5 A da bateria",
                e is { Ligado: true } && Math.Abs(e.Saida - 5) < 0.05 && Math.Abs(e.CorrenteDeSaida - 1) < 0.02
                && Math.Abs(bateria - esperada) < 0.01 && bateria < 0.55, $"{Contar(e)} | bateria {bateria:0.000} A");
        }

        // ---- LM2596: a folga, e a entrada abaixo do minimo ----
        {
            var e9 = Conta(Montar(Reg("lm2596", volts: 9), 9.5, 100)).RegulagemDe("reg");
            var e3 = Conta(Montar(Reg("lm2596"), 3, 100)).RegulagemDe("reg");
            Conferir("regulador: o LM2596 em 9 V com 9,5 V na entrada da ~8 V; com 3 V (abaixo de 4,5) desliga",
                e9 is { EmQueda: true } && Math.Abs(e9.Saida - 8.0) < 0.1 && e3 is { Ligado: false } && e3.Saida < 0.1,
                $"{Contar(e9)} || {Contar(e3)}");
        }

        // ---- MT3608: 3,7 V para 5 V; com 9 V ele so passa a entrada ----
        {
            var c = Montar(Reg("mt3608"), 3.7, 25);
            var conta = Conta(c);
            var e = conta.RegulagemDe("reg");
            double bateria = conta.CorrenteDaBateria("bat");
            var e9 = Conta(Montar(Reg("mt3608"), 9, 100)).RegulagemDe("reg");
            Conferir("regulador: o MT3608 (step-up) faz 5 V de 3,7 V puxando ~0,3 A; com 9 V na entrada sai ~8,6 V (so sobe)",
                e is { Ligado: true } && Math.Abs(e.Saida - 5) < 0.05 && Math.Abs(bateria - (5 * 0.2 / (0.9 * e.Entrada) + 0.002)) < 0.01
                && e9 is { SoPassando: true } && Math.Abs(e9.Saida - 8.6) < 0.1,
                $"{Contar(e)} | bateria {bateria:0.000} A || {Contar(e9)}");
        }

        // ---- a protecao de corrente: a saida do 7805 em curto (0,1 ohm) ----
        {
            var c = Montar(Reg("regulador-7805"), 9, 0.1);
            var conta = Conta(c);
            var e = conta.RegulagemDe("reg");
            double bateria = conta.CorrenteDaBateria("bat");
            Conferir("regulador: com a saida em curto o 7805 segura a corrente em ~2,2 A (a tensao cai)",
                e is { Limitando: true } && Math.Abs(e.CorrenteDeSaida - 2.2) < 0.1 && e.Saida < 0.5 && Math.Abs(bateria - 2.2) < 0.15,
                $"{Contar(e)} | bateria {bateria:0.00} A");
        }

        // ---- desligado pelo editor (protecao termica, queimado): sem saida ----
        {
            var e = Conta(Montar(Reg("regulador-7805"), 9, 100), new HashSet<string> { "reg" }).RegulagemDe("reg");
            Conferir("regulador: desligado (protecao termica) ele nao da saida", e is { Ligado: false } && e.Saida < 0.05, Contar(e));
        }

        // ---- a placa ligada pelo regulador (sem o cabo USB) ----
        {
            var c = new Circuito();
            c.Pecas.AddRange([new PecaNoCircuito { Id = "placa", Tipo = "arduino-uno", X = 0, Y = 0 }, Bat(9), Reg("regulador-7805")]);
            Ligar(c, "bat", "+", "reg", "IN");
            Ligar(c, "bat", "-", "reg", "GND");
            Ligar(c, "reg", "OUT", "placa", "5V");
            Ligar(c, "reg", "GND", "placa", "GND.2");
            var uno = Conta(c).AlimentacaoDe("placa");

            var c2 = new Circuito();
            c2.Pecas.AddRange([new PecaNoCircuito { Id = "placa", Tipo = "esp32-devkit-v1", X = 0, Y = 0 }, Bat(11.1), Reg("lm2596")]);
            Ligar(c2, "bat", "+", "reg", "IN+");
            Ligar(c2, "bat", "-", "reg", "IN-");
            Ligar(c2, "reg", "OUT+", "placa", "VIN");
            Ligar(c2, "reg", "OUT-", "placa", "GND.2");
            var conta2 = Conta(c2);
            var esp = conta2.AlimentacaoDe("placa");
            double bateria = conta2.CorrenteDaBateria("bat") * 1000;
            Conferir("regulador: o 7805 liga o Uno pelo 5V, e o LM2596 a ESP32 pelo VIN (que puxa ~40 mA da LiPo de 11,1 V)",
                uno is { Ligada: true, Pino: "5V" } && esp is { Ligada: true, Pino: "VIN" } && Math.Abs(esp.Volts - 5) < 0.05
                && bateria is > 30 and < 50,
                $"uno: {uno?.Ligada} {uno?.Pino} {uno?.Volts:0.00} V | esp: {esp?.Ligada} {esp?.Pino} {esp?.Volts:0.00} V | bateria {bateria:0.0} mA");
        }

        // ---- o calor: 7805 de 12 V com meio ampere esquenta 3,5 W e desliga sozinho ----
        {
            var c = Montar(Reg("regulador-7805"), 12, 10);
            var ed = new EditorCircuito();
            ed.Carregar(c);
            var maq = new Maquina { Perfil = PerfilPlaca.Esp32 };
            maq.Reiniciar();
            var bancada = Simulador.App.Bancada.DeUmaSo(maq);
            var rede = new Netlist(c);
            double aos30 = 0, protecaoEm = -1;
            for (double t = 0; t < 70; t += 0.5)
            {
                ed.AvancarEnergiaParaTeste(bancada, rede, 0.5);
                if (Math.Abs(t - 30) < 0.25) aos30 = ed.TemperaturaDoRegulador("reg");
                if (protecaoEm < 0 && ed.EmProtecaoTermicaParaTeste("reg")) protecaoEm = t;
            }
            var aviso = ed.EstourosPelaEnergia().FirstOrDefault(a => a.Peca == "reg");
            var e = ed.EnergiaDoQuadro.RegulagemDe("reg");
            Conferir("regulador: o 7805 de 12 V com 0,5 A esquenta e a protecao termica corta a saida (~48 s), com aviso",
                aos30 is > 110 and < 140 && protecaoEm is > 40 and < 58 && aviso is { Queimou: false } && aviso.Motivo.Contains("térmica"),
                $"30 s: {aos30:0} °C | protecao aos {protecaoEm:0.0} s | {Contar(e)} | {aviso?.Motivo}");

            // Desligado ele esfria e volta (o ciclo de quem esquece o dissipador).
            bool voltou = false;
            for (double t = 0; t < 30 && !voltou; t += 0.5)
            {
                ed.AvancarEnergiaParaTeste(bancada, rede, 0.5);
                voltou = !ed.EmProtecaoTermicaParaTeste("reg");
            }
            Conferir("regulador: protegido, o 7805 esfria ate 130 °C e liga de novo", voltou,
                $"{ed.TemperaturaDoRegulador("reg"):0} °C");
        }

        // ---- na janela: as receitas da bateria (a placa ligada pelo regulador) ----
        if (AcharABateria() is { } pasta)
        {
            foreach (var (arquivo, fala, rotulo) in new[]
                     {
                         ("regulador-7805", "ligou pelo regulador", "5,0 V"),
                         ("ams1117", "saida pronta", "3,3 V"),
                         ("lm2596", "saida pronta", "5,0 V"),
                         ("mt3608", "ligou pelo regulador", "5,0 V"),
                     })
            {
                string caminho = System.IO.Path.Combine(pasta, "componentes", arquivo + ".sktch");
                if (!System.IO.File.Exists(caminho)) { Conferir($"regulador: {arquivo}.sktch existe", false, caminho); continue; }
                var (circuito, codigo) = Sktch.Abrir(caminho);
                var janela = new MainWindow();
                try
                {
                    janela.TesteAbrir(circuito, codigo);
                    janela.TesteRodar();
                    var ed = janela.TesteEditorCircuito;
                    var maquina = janela.TesteMaquinaDa("placa");
                    bool falou = EsperarPor(() => LinhasDoSerial(maquina).Any(l => l.Contains(fala)), 5000);
                    EsperarPor(() => false, 400);
                    var avisos = ed.EstourosPelaEnergia().Select(a => $"{a.Peca}: {a.Motivo}").ToList();
                    string acima = ed.RotuloDeGiroParaTeste("peca") ?? "";
                    Conferir($"regulador (janela): {arquivo} liga a placa sem o cabo USB, sem aviso nenhum",
                        falou && maquina.Energizada && acima.Contains(rotulo) && avisos.Count == 0,
                        $"{string.Join(" | ", LinhasDoSerial(maquina).Take(2))} | rotulo '{acima}' | {ed.SituacaoDaEnergia("placa")}"
                        + (avisos.Count > 0 ? " | " + string.Join(" / ", avisos) : ""));
                }
                finally
                {
                    janela.TesteParar();
                    try { janela.Close(); } catch (InvalidOperationException) { }
                }
            }
        }

        // ---- o diagram.json leva o modelo e a tensao do trimpot ----
        {
            var c = new Circuito();
            c.Pecas.AddRange([new PecaNoCircuito { Id = "u1", Tipo = "regulador-7805", X = 0, Y = 0, Modelo = "7812" },
                              new PecaNoCircuito { Id = "u2", Tipo = "lm2596", X = 100, Y = 0, Volts = 9 },
                              new PecaNoCircuito { Id = "u3", Tipo = "mt3608", X = 300, Y = 0, Volts = 5 }]);
            string json = ExportarWokwi.Gerar(c);
            var volta = ImportarWokwi.Ler(json).Circuito;
            Conferir("regulador: o diagram.json leva o modelo (7812) e a saida dos ajustaveis (9 V, 5 V)",
                json.Contains("\"regulador-7805\"") && volta.Achar("u1")?.Modelo == "7812"
                && volta.Achar("u2")?.Volts == 9 && volta.Achar("u3")?.Volts == 5,
                json.Length > 400 ? json[..400] : json);
        }
    }
}
