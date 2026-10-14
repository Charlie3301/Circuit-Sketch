using System.Windows;

namespace Simulador.App;

/// <summary>
/// As baterias da Alpha 25: as prontas (cada uma com o seu desenho e os
/// numeros de fabrica) e os suportes de pilhas com slots -- a soma das
/// pilhas em serie, o slot vazio abrindo o circuito, a troca de pilha, de
/// formato e de numero de slots, e o diagram.json levando tudo.
/// </summary>
public static partial class Autoteste
{
    /// <summary>
    /// Os LEDs soldados nas placas da aba Arduino (Alpha 25): o de energia
    /// acende com a placa ligada e o LED_BUILTIN segue o pino dele.
    /// </summary>
    static void Placas_LedsDaPlaca()
    {
        saida.AppendLine("placas: os LEDs soldados");
        foreach (string tipo in new[] { "arduino-uno", "arduino-mega", "arduino-nano", "nano-rp2040-connect", "franzininho" })
        {
            var c = new Circuito();
            c.Pecas.Add(new PecaNoCircuito { Id = "placa", Tipo = tipo, X = 0, Y = 0, Usb = true });
            var ed = new EditorCircuito();
            ed.Carregar(c);
            var perfil = PerfilPlaca.De(tipo);
            var maq = new Maquina { Perfil = perfil };
            maq.Reiniciar();
            var b = Simulador.App.Bancada.DeUmaSo(maq);
            var rede = new Netlist(c);
            int led = perfil.LedInterno;
            maq.Pwm[led] = 0;
            ed.RefletirEstado(b, rede);
            var apagado = ed.LedsDaPlacaParaTeste("placa");
            maq.Pwm[led] = 255;
            ed.RefletirEstado(b, rede);
            var aceso = ed.LedsDaPlacaParaTeste("placa");
            Conferir($"{tipo}: o LED de energia acende ligada e o do LED_BUILTIN segue o pino {led}",
                apagado.Length >= 2 && apagado[0] > 0.5 && apagado[1] < 0.05 && aceso[1] > 0.5,
                string.Join("/", apagado.Select(v => v.ToString("0.00"))) + " -> " + string.Join("/", aceso.Select(v => v.ToString("0.00"))));
        }
    }

    static void Baterias_Presets()
    {
        saida.AppendLine("baterias: prontas e suportes de pilhas");

        static void Ligar(Circuito c, PecaNoCircuito a, string pa, PecaNoCircuito b, string pb)
            => c.Fios.Add(new Fio { DePeca = a.Id, DePino = pa, ParaPeca = b.Id, ParaPino = pb });

        // Bateria, resistor e LED, so com energia: a corrente no LED.
        static double CorrenteNoLed(PecaNoCircuito bateria, double ohms)
        {
            var c = new Circuito();
            var r = new PecaNoCircuito { Id = "r", Tipo = "resistor", X = 300, Y = 0, Ohms = ohms };
            var led = new PecaNoCircuito { Id = "led", Tipo = "led", X = 500, Y = 0 };
            bateria.Id = "bat";
            c.Pecas.AddRange([bateria, r, led]);
            Ligar(c, bateria, "+", r, "1");
            Ligar(c, r, "2", led, "A");
            Ligar(c, led, "C", bateria, "-");
            var ed = new EditorCircuito();
            ed.Carregar(c);
            var bancada = Simulador.App.Bancada.DeUmaSo(new Maquina { Perfil = PerfilPlaca.Esp32 });
            var rede = new Netlist(c);
            Energia conta = null;
            for (int k = 0; k < 5; k++) conta = ed.AvancarEnergiaParaTeste(bancada, rede, 0.04);
            return conta.CorrenteDoRamo(led.Id, "led") * 1000;
        }

        // ---- as prontas: no catalogo, com desenho, pinos e numeros de fabrica ----
        foreach (var pacote in Baterias.Pacotes)
        {
            var def = Catalogo.Obter(pacote.Tipo);
            bool pinos = def is not null && def.Pinos.Count == 2 && def.Pinos.Any(p => p.Nome == "+") && def.Pinos.Any(p => p.Nome == "-")
                         && def.Pinos.All(p => p.X >= 0 && p.X <= def.Largura && p.Y >= 0 && p.Y <= def.Altura);
            Conferir($"{pacote.Nome}: no catalogo, em Alimentacao, com o desenho e o + e o - dentro da caixa",
                pinos && def.Categoria == "Alimentacao" && Pecas.Existe(pacote.Tipo) && Netlist.EhBateria(pacote.Tipo),
                def is null ? "(sem peca)" : $"{def.Categoria}, {def.Pinos.Count} pinos");

            var ed = new EditorCircuito();
            ed.Carregar(new Circuito());
            var nova = ed.Adicionar(pacote.Tipo, new Point(300, 300));
            Conferir($"{pacote.Nome}: sai da lista com {pacote.Volts:0.#} V, {pacote.MiliAmperes:0} mA e {pacote.MiliAmperesHora:0} mAh",
                nova.Volts == pacote.Volts && nova.MiliAmperes == pacote.MiliAmperes && nova.MiliAmperesHora == pacote.MiliAmperesHora,
                $"{nova.Volts} V, {nova.MiliAmperes} mA, {nova.MiliAmperesHora} mAh");
        }

        // ---- a 9 V pronta acende o LED como a personalizada de 9 V ----
        {
            double pronta = CorrenteNoLed(new PecaNoCircuito { Tipo = "bateria-9v", X = 0, Y = 0, Volts = 9, MiliAmperes = 500, MiliAmperesHora = 550 }, 330);
            double antiga = CorrenteNoLed(new PecaNoCircuito { Tipo = "bateria", X = 0, Y = 0, Volts = 9, MiliAmperes = 500 }, 330);
            Conferir("bateria 9 V pronta + 330 ohms acende o LED (~20 mA), igual a personalizada de 9 V",
                pronta is > 18 and < 23 && Math.Abs(pronta - antiga) < 0.5, $"{pronta:0.0} / {antiga:0.0} mA");
        }

        // ---- o suporte de 4 AA: em serie, 6 V; com NiMH, 4,8 V ----
        {
            var suporte = new PecaNoCircuito { Tipo = "suporte-aa-4" };
            var f = Baterias.FichaDe(suporte);
            Conferir("suporte de 4 AA alcalinas: 6 V, 2500 mAh, ate 1 A", f.Volts == 6.0 && f.MiliAmperesHora == 2500 && f.MiliAmperes == 1000 && !f.Aberta,
                $"{f.Volts} V, {f.MiliAmperesHora} mAh, {f.MiliAmperes} mA");
            suporte.Modelo = "nimh";
            f = Baterias.FichaDe(suporte);
            Conferir("com NiMH: 4,8 V e 2000 mAh", Math.Abs(f.Volts - 4.8) < 1e-9 && f.MiliAmperesHora == 2000, $"{f.Volts} V");

            double cheio = CorrenteNoLed(new PecaNoCircuito { Tipo = "suporte-aa-4", X = 0, Y = 0 }, 220);
            double vazio = CorrenteNoLed(new PecaNoCircuito { Tipo = "suporte-aa-4", X = 0, Y = 0, Bits = 1 << 2 }, 220);
            Conferir("4 AA + 220 ohms acende o LED; sem a terceira pilha o circuito abre e ele apaga",
                cheio is > 12 and < 22 && Math.Abs(vazio) < 1e-3, $"{cheio:0.0} mA / {vazio:0.000} mA");
        }

        // ---- os formatos e os slots que existem ----
        foreach (var formato in Baterias.Formatos)
            foreach (int n in formato.Slots)
            {
                string tipo = Baterias.TipoDoSuporte(formato.Id, n);
                var def = Catalogo.Obter(tipo);
                var leiaute = Baterias.LeiauteDe(tipo);
                bool slotsCertos = leiaute is not null && leiaute.Slots.Length == n
                    && Enumerable.Range(0, n).All(k => Baterias.SlotNoPonto(tipo, new Point(leiaute.Slots[k].X + leiaute.Slots[k].Width / 2,
                                                                                            leiaute.Slots[k].Y + leiaute.Slots[k].Height / 2)) == k);
                bool dentro = def is not null && def.Pinos.All(p => p.X > leiaute.Caixa.Right && p.X <= def.Largura && p.Y >= 0 && p.Y <= def.Altura);
                Conferir($"suporte {formato.Nome} com {n} slot(s): {def?.Nome}, os slots no lugar e os fios saindo pela direita",
                    def is not null && slotsCertos && dentro && Baterias.FichaDe(new PecaNoCircuito { Tipo = tipo }).Volts == formato.Pilhas[0].Volts * n);
            }
        Conferir("um suporte que nao existe a venda (5 AA) nao e peca", Catalogo.Obter("suporte-aa-5") is null && !Netlist.EhBateria("suporte-aa-5"));

        // ---- no editor: tirar a pilha, trocar a pilha, o formato e os slots ----
        {
            var c = new Circuito();
            var suporte = new PecaNoCircuito { Id = "bat", Tipo = "suporte-aa-4", X = 100, Y = 100 };
            var led = new PecaNoCircuito { Id = "led", Tipo = "led", X = 500, Y = 100 };
            c.Pecas.AddRange([suporte, led]);
            Ligar(c, suporte, "+", led, "A");
            Ligar(c, led, "C", suporte, "-");
            var ed = new EditorCircuito();
            ed.Carregar(c);
            ed.SelecionarParaTeste("bat");

            ed.AlternarPilha(suporte, 1);
            bool tirou = !Baterias.TemPilha(suporte, 1) && Baterias.FichaDe(suporte).Aberta;
            ed.AlternarPilha(suporte, 1);
            Conferir("tirar e pôr a pilha do slot 2", tirou && Baterias.TemPilha(suporte, 1) && !Baterias.FichaDe(suporte).Aberta);

            ed.DefinirPilhaDoSuporte("nimh");
            bool nimh = suporte.Modelo == "nimh" && Math.Abs(suporte.Volts - 4.8) < 1e-9;
            ed.AlternarPilha(suporte, 3);
            ed.DefinirTipoDaBateria("suporte-aa-2");
            bool doisSlots = suporte.Tipo == "suporte-aa-2" && suporte.Modelo == "nimh" && suporte.Bits == 0
                             && Math.Abs(Baterias.FichaDe(suporte).Volts - 2.4) < 1e-9;
            ed.DefinirTipoDaBateria("suporte-18650-2");
            bool trocouFormato = suporte.Modelo is null && Math.Abs(Baterias.FichaDe(suporte).Volts - 7.4) < 1e-9;
            ed.DefinirTipoDaBateria("lipo-3s-2200");
            bool virouLipo = suporte.Tipo == "lipo-3s-2200" && suporte.Volts == 11.1 && suporte.MiliAmperesHora == 2200 && suporte.Bits == 0;
            bool fiosFicaram = c.Fios.Count == 2 && c.Fios.All(f => f.DePeca == "bat" || f.ParaPeca == "bat");
            Conferir("trocar a pilha (NiMH), os slots (4 -> 2, o slot 4 vazio some), o formato (18650) e para uma LiPo 3S, com os fios no lugar",
                nimh && doisSlots && trocouFormato && virouLipo && fiosFicaram,
                $"{nimh}/{doisSlots}/{trocouFormato}/{virouLipo}/{fiosFicaram}");
            ed.DefinirTipoDaBateria("bateria");
            Conferir("e de volta para a personalizada, com os numeros que estavam", suporte.Tipo == "bateria" && suporte.Volts == 11.1);
        }

        // ---- o clique na pilha: o primeiro seleciona o suporte, o seguinte tira a pilha ----
        {
            var c = new Circuito();
            var suporte = new PecaNoCircuito { Id = "bat", Tipo = "suporte-aa-4", X = 100, Y = 100, Rotacao = 90 };
            c.Pecas.Add(suporte);
            var ed = new EditorCircuito();
            ed.Carregar(c);
            var l = Baterias.LeiauteDe(suporte.Tipo);
            var alvo = Geometria.PosicaoLocal(suporte, Catalogo.Obter(suporte.Tipo), l.Slots[2].X + l.Slots[2].Width / 2, l.Slots[2].Y + l.Slots[2].Height / 2);
            ed.Apertar(alvo); ed.Soltar();
            bool primeiro = suporte.Bits == 0;
            ed.Apertar(alvo); ed.Soltar();
            bool segundo = suporte.Bits == 1 << 2;
            ed.Apertar(alvo); ed.Soltar();
            Conferir("clique na pilha (suporte girado 90 graus): o primeiro so seleciona, o segundo tira a pilha 3, o terceiro poe de volta",
                primeiro && segundo && suporte.Bits == 0, $"{primeiro}/{segundo}/{suporte.Bits}");
        }

        // ---- o diagram.json ----
        {
            var c = new Circuito();
            c.Pecas.Add(new PecaNoCircuito { Id = "bat1", Tipo = "suporte-aa-6", X = 0, Y = 0, Modelo = "nimh", Bits = 2, Volts = 7.2, MiliAmperes = 4000, MiliAmperesHora = 2000 });
            c.Pecas.Add(new PecaNoCircuito { Id = "bat2", Tipo = "lipo-3s-2200", X = 0, Y = 300, Volts = 11.1, MiliAmperes = 66000, MiliAmperesHora = 2200 });
            c.Pecas.Add(new PecaNoCircuito { Id = "bat3", Tipo = "bateria-9v", X = 0, Y = 500, Volts = 8.4, MiliAmperes = 500, MiliAmperesHora = 550 });
            string json = ExportarWokwi.Gerar(c);
            var volta = ImportarWokwi.Ler(json).Circuito;
            var s = volta?.Achar("bat1");
            var lipo = volta?.Achar("bat2");
            var nove = volta?.Achar("bat3");
            Conferir("diagram.json: o suporte de 6 AA com NiMH e um slot vazio, a LiPo 3S de fabrica (sem numeros no arquivo) e a 9 V mexida",
                s is { Tipo: "suporte-aa-6", Modelo: "nimh", Bits: 2 } && Baterias.FichaDe(s).Aberta
                && lipo is { Volts: 11.1, MiliAmperes: 66000, MiliAmperesHora: 2200 } && !json.Contains("\"66000\"")
                && nove is { Volts: 8.4 },
                $"{s?.Tipo}/{s?.Modelo}/{s?.Bits}; {lipo?.Volts}/{lipo?.MiliAmperes}; {nove?.Volts}");
        }

        // ---- a personalizada continua a de sempre ----
        {
            var antiga = new PecaNoCircuito { Tipo = "bateria", Volts = 6, MiliAmperes = 800, MiliAmperesHora = 2400 };
            var f = Baterias.FichaDe(antiga);
            Conferir("a bateria personalizada (a dos circuitos antigos) usa os numeros dela",
                f.Volts == 6 && f.MiliAmperes == 800 && f.MiliAmperesHora == 2400 && !f.Aberta && Catalogo.Obter("bateria")?.Nome == "Bateria personalizada");
        }
    }
}
