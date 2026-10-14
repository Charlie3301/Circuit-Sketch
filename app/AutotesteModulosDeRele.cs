namespace Simulador.App;

/// <summary>
/// Os modulos de rele de 1, 2 e 4 canais (Alpha 24): ativos em LOW, a bobina
/// pelo VCC (e nao pelo pino), o contato COM/NO/NC levando a carga, cada canal
/// sozinho, o VCC de 3,3 V que nao arma, os 12 V que queimam, o jumper H.
/// </summary>
public static partial class Autoteste
{
    static void ModulosDeRele_NoCircuito()
    {
        saida.AppendLine("modulos de rele (1, 2 e 4 canais)");

        // ---- o catalogo ----
        foreach (var (tipo, canais) in new[] { ("modulo-rele-1", 1), ("modulo-rele-2", 2), ("modulo-rele-4", 4) })
        {
            var def = Catalogo.Obter(tipo);
            var esperados = new List<string> { "VCC", "GND" };
            for (int k = 1; k <= canais; k++)
                esperados.AddRange([ModulosDeRele.Entrada(tipo, k), ModulosDeRele.Comum(tipo, k),
                                    ModulosDeRele.Aberto(tipo, k), ModulosDeRele.Fechado(tipo, k)]);
            var tem = def?.Pinos.Select(p => p.Nome).ToHashSet() ?? [];
            Conferir($"{tipo}: esta no catalogo com os {esperados.Count} pinos (VCC, GND, IN, COM, NO, NC de cada canal)",
                def is not null && esperados.All(tem.Contains) && tem.Count == esperados.Count,
                def is null ? "sem peca" : string.Join(",", tem));
            Conferir($"{tipo}: tem desenho", Pecas.Existe(tipo));
        }
        Conferir("o de 1 canal sai no diagram.json como o wokwi-relay-module (e volta)",
            FormatoWokwi.TipoParaWokwi("modulo-rele-1") == "wokwi-relay-module"
            && FormatoWokwi.TipoDoWokwi("wokwi-relay-module") == "modulo-rele-1", FormatoWokwi.TipoParaWokwi("modulo-rele-1"));

        static void Pino(Maquina m, int p, bool alto)
        {
            m.Modo[p] = 1;
            m.Digital[p] = alto ? 1 : 0;
            m.Pwm[p] = alto ? 255 : 0;
        }

        // Uno na USB; o modulo no 5V; a carga (bateria de 9 V, 470 ohms e um
        // LED) passa pelo contato de cada canal (COM no +, NO no resistor).
        (Circuito c, EditorCircuito ed, Maquina maq, Netlist rede) Montar(string tipo, string pinoVcc = "5V", double? fonteDoVcc = null,
                                                                         string modelo = null)
        {
            var c = new Circuito();
            c.Pecas.Add(new PecaNoCircuito { Id = "placa", Tipo = "arduino-uno", X = 0, Y = 0, Usb = true });
            c.Pecas.Add(new PecaNoCircuito { Id = "rele", Tipo = tipo, X = 400, Y = 0, Modelo = modelo });
            c.Pecas.Add(new PecaNoCircuito { Id = "bat", Tipo = "bateria", X = 0, Y = 400, Volts = 9, MiliAmperes = 2000, MiliAmperesHora = 1000 });
            void Ligar(string a, string pa, string b, string pb) => c.Fios.Add(new Fio { DePeca = a, DePino = pa, ParaPeca = b, ParaPino = pb });
            if (fonteDoVcc is { } volts)
            {
                c.Pecas.Add(new PecaNoCircuito { Id = "fonte", Tipo = "bateria", X = 200, Y = 400, Volts = volts, MiliAmperes = 5000, MiliAmperesHora = 1000 });
                Ligar("fonte", "+", "rele", "VCC");
                Ligar("fonte", "-", "placa", "GND.3");
            }
            else Ligar("placa", pinoVcc, "rele", "VCC");
            Ligar("placa", "GND.1", "rele", "GND");
            int canais = ModulosDeRele.Canais(tipo);
            for (int k = 1; k <= canais; k++)
            {
                Ligar("placa", (3 + k).ToString(), "rele", ModulosDeRele.Entrada(tipo, k));
                c.Pecas.Add(new PecaNoCircuito { Id = $"r{k}", Tipo = "resistor", X = 600, Y = k * 60, Ohms = 470 });
                c.Pecas.Add(new PecaNoCircuito { Id = $"led{k}", Tipo = "led", X = 700, Y = k * 60 });
                Ligar("bat", "+", "rele", ModulosDeRele.Comum(tipo, k));
                Ligar("rele", ModulosDeRele.Aberto(tipo, k), $"r{k}", "1");
                Ligar($"r{k}", "2", $"led{k}", "A");
                Ligar($"led{k}", "C", "bat", "-");
            }
            Ligar("bat", "-", "placa", "GND.2");
            var ed = new EditorCircuito();
            ed.Carregar(c);
            var maq = new Maquina { Perfil = PerfilPlaca.Uno };
            maq.Reiniciar();
            for (int k = 1; k <= canais; k++) Pino(maq, 3 + k, true);   // todos desligados (HIGH)
            return (c, ed, maq, new Netlist(c));
        }
        static Energia Andar(EditorCircuito ed, Maquina maq, Netlist rede)
        {
            var b = Simulador.App.Bancada.DeUmaSo(maq);
            Energia conta = null;
            for (int i = 0; i < 3; i++)   // o contato le a bobina do quadro anterior
            {
                ed.AplicarEntradas(b, rede);
                conta = ed.AvancarEnergiaParaTeste(b, rede, 0.02);
            }
            return conta;
        }
        static double Led(Energia conta, string id) => Math.Abs(conta.CorrenteDaPeca(id));

        // ---- 1 canal: LOW arma, HIGH desarma ----
        {
            var (_, ed, maq, rede) = Montar("modulo-rele-1");
            var solto = Andar(ed, maq, rede);
            double ledSolto = Led(solto, "led1");
            bool armadoSolto = ed.CanalDoModuloParaTeste("rele", 1);
            Pino(maq, 4, false);
            var ligado = Andar(ed, maq, rede);
            double bobina = ligado.CorrenteDoRamo("rele", "bobina1");
            double entrada = ligado.CorrenteDoRamo("rele", "entrada1");
            double ledLigado = Led(ligado, "led1");
            Conferir("1 canal: com o IN em HIGH o rele fica desarmado (o LED da carga apagado)",
                ledSolto < 0.001 && !armadoSolto, $"{ledSolto * 1000:0.0} mA");
            Conferir("1 canal: IN em LOW arma o rele, e o contato COM-NO acende o LED da carga (~15 mA)",
                ledLigado is > 0.010 and < 0.020 && ed.CanalDoModuloParaTeste("rele", 1), $"{ledLigado * 1000:0.0} mA");
            Conferir("1 canal: a bobina puxa ~70 mA do VCC, e o pino so ~2 mA (pelo optoacoplador)",
                bobina is > 0.06 and < 0.075 && entrada is > 0.0015 and < 0.0025,
                $"bobina {bobina * 1000:0} mA, pino {entrada * 1000:0.0} mA");
            Conferir("1 canal: a luz do canal e a de energia acendem", ed.LuzDeModuloAcesaParaTeste("rele", 0) && ed.LuzDeModuloAcesaParaTeste("rele", 1));
            Conferir("1 canal: sem aviso nenhum (o pino nao alimenta a bobina)", ed.EstourosPelaEnergia().Count == 0,
                string.Join(" / ", ed.EstourosPelaEnergia().Select(e => e.Motivo)));
            Pino(maq, 4, true);
            var deNovo = Andar(ed, maq, rede);
            Conferir("1 canal: o IN de volta em HIGH desarma (o LED apaga)", Led(deNovo, "led1") < 0.001, $"{Led(deNovo, "led1") * 1000:0.0} mA");
        }

        // ---- 4 canais: cada um sozinho ----
        {
            var (_, ed, maq, rede) = Montar("modulo-rele-4");
            bool certos = true;
            var visto = new List<string>();
            for (int k = 1; k <= 4; k++)
            {
                for (int j = 1; j <= 4; j++) Pino(maq, 3 + j, j != k);
                var conta = Andar(ed, maq, rede);
                var acesos = Enumerable.Range(1, 4).Where(j => Led(conta, $"led{j}") > 0.005).ToList();
                visto.Add($"IN{k}->{string.Join("", acesos)}");
                certos &= acesos.SequenceEqual([k]);
            }
            Conferir("4 canais: cada IN arma so o seu rele (o LED do canal certo, e so ele)", certos, string.Join(" ", visto));
            for (int j = 1; j <= 4; j++) Pino(maq, 3 + j, false);
            var todos = Andar(ed, maq, rede);
            double vcc = todos.CorrenteDoRamo("rele", "bobina1") + todos.CorrenteDoRamo("rele", "bobina2")
                         + todos.CorrenteDoRamo("rele", "bobina3") + todos.CorrenteDoRamo("rele", "bobina4");
            Conferir("4 canais: os quatro ligados puxam ~280 mA do VCC", vcc is > 0.24 and < 0.30, $"{vcc * 1000:0} mA");
        }

        // ---- 2 canais: o IN solto fica desligado ----
        {
            var (c, ed, maq, rede) = Montar("modulo-rele-2");
            c.Fios.RemoveAll(f => f.ParaPino == "IN2");
            ed.Carregar(c);
            rede = new Netlist(c);
            Pino(maq, 4, false);
            var conta = Andar(ed, maq, rede);
            Conferir("2 canais: o IN2 solto fica desarmado; o IN1 em LOW arma so o primeiro",
                Led(conta, "led1") > 0.005 && Led(conta, "led2") < 0.001, $"{Led(conta, "led1") * 1000:0.0} / {Led(conta, "led2") * 1000:0.0} mA");
        }

        // ---- VCC nos 3,3 V: o LED acende, o rele nao arma ----
        {
            var (_, ed, maq, rede) = Montar("modulo-rele-1", pinoVcc: "3.3V");
            Pino(maq, 4, false);
            var conta = Andar(ed, maq, rede);
            var aviso = ed.EstourosPelaEnergia().FirstOrDefault(e => e.Peca == "rele");
            Conferir("VCC em 3,3 V: o rele nao arma (a bobina de 5 V pede 3,75 V) e o editor avisa",
                Led(conta, "led1") < 0.001 && aviso is { Queimou: false } && aviso.Motivo.Contains("3,75"),
                aviso?.Motivo ?? "sem aviso");
        }

        // ---- VCC em 12 V: queima ----
        {
            var (_, ed, maq, rede) = Montar("modulo-rele-1", fonteDoVcc: 12);
            Pino(maq, 4, false);
            Andar(ed, maq, rede);
            var aviso = ed.EstourosPelaEnergia().FirstOrDefault(e => e.Peca == "rele");
            Conferir("VCC em 12 V: a bobina de 5 V queima", aviso is { Queimou: true }, aviso?.Motivo ?? "sem aviso");
        }

        // ---- o jumper H: ativo em HIGH ----
        {
            var (_, ed, maq, rede) = Montar("modulo-rele-1", modelo: "alto");
            var desligado = Andar(ed, maq, rede);   // o IN em HIGH
            double comHigh = Led(desligado, "led1");
            Pino(maq, 4, false);
            double comLow = Led(Andar(ed, maq, rede), "led1");
            Conferir("jumper em H: o IN em HIGH arma, em LOW desarma", comHigh > 0.005 && comLow < 0.001,
                $"HIGH {comHigh * 1000:0.0} mA, LOW {comLow * 1000:0.0} mA");
        }

        // ---- o contato leva o nivel logico: o NO no pino 9 le o GND do COM ----
        {
            var c = new Circuito();
            c.Pecas.Add(new PecaNoCircuito { Id = "placa", Tipo = "arduino-uno", X = 0, Y = 0, Usb = true });
            c.Pecas.Add(new PecaNoCircuito { Id = "rele", Tipo = "modulo-rele-1", X = 400, Y = 0 });
            void Ligar(string a, string pa, string b, string pb) => c.Fios.Add(new Fio { DePeca = a, DePino = pa, ParaPeca = b, ParaPino = pb });
            Ligar("placa", "5V", "rele", "VCC"); Ligar("placa", "GND.1", "rele", "GND"); Ligar("placa", "7", "rele", "IN");
            Ligar("rele", "COM", "placa", "GND.2"); Ligar("rele", "NO", "placa", "9");
            var ed = new EditorCircuito();
            ed.Carregar(c);
            var maq = new Maquina { Perfil = PerfilPlaca.Uno };
            maq.Reiniciar();
            var rede = new Netlist(c);
            Pino(maq, 7, true);
            Andar(ed, maq, rede);
            int solto = maq.NivelExterno[9];
            Pino(maq, 7, false);
            Andar(ed, maq, rede);
            int armado = maq.NivelExterno[9];
            Conferir("o contato leva o nivel: armado, o pino 9 (no NO) le o GND do COM; desarmado, fica solto",
                armado == 0 && solto == -1, $"armado {armado}, desarmado {solto}");
        }
    }
}
