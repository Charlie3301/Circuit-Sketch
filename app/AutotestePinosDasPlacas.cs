namespace Simulador.App;

/// <summary>
/// Todo pino das placas da aba Arduino faz o que faz na placa de verdade
/// (Alpha 25): os GPIO chegam no GPIO (os do ICSP sao os mesmos da barra, o
/// SDA/SCL de cima da Mega sao o 20/21, o TX/RX do RP2040 o D1/D0), o 5V, o
/// 3V3, o VIN e o IOREF alimentam, o GND e terra, o RESET no GND segura a
/// placa e o AREF e a referencia do analogReference(EXTERNAL). Os do ICSP do
/// 16U2 (o chip do USB) vao nele, e o sketch nao os usa -- como na placa.
/// </summary>
public static partial class Autoteste
{
    static void Placas_TodosOsPinos()
    {
        saida.AppendLine("placas: a funcao de cada pino");

        static void Ligar(Circuito c, string a, string pa, string b, string pb)
            => c.Fios.Add(new Fio { DePeca = a, DePino = pa, ParaPeca = b, ParaPino = pb });

        static string Base(string n) => n.Contains('.') && n[(n.LastIndexOf('.') + 1)..].All(char.IsAsciiDigit) ? n[..n.LastIndexOf('.')] : n;

        // ---- cada pino tem uma funcao ----
        foreach (string tipo in new[] { "arduino-uno", "arduino-mega", "arduino-nano", "nano-rp2040-connect", "franzininho", "attiny85" })
        {
            var def = Catalogo.Obter(tipo);
            var perfil = PerfilPlaca.De(tipo);
            var sem = new List<string>();
            int gpios = 0, energia = 0, especiais = 0;
            foreach (var p in def.Pinos)
            {
                string b = Base(p.Nome);
                if (perfil.Numero(p.Nome) >= 0) gpios++;
                else if (Netlist.EhPinoDeAlimentacao(p.Nome) || Netlist.EhPinoDeTerra(p.Nome)) energia++;
                else if (b is "RESET" or "AREF" || b.EndsWith('2') && b is "MISO2" or "SCK2" or "MOSI2" or "RESET2") especiais++;
                else sem.Add(p.Nome);
            }
            Conferir($"{tipo}: todo pino tem funcao ({gpios} GPIO, {energia} de energia, {especiais} RESET/AREF/16U2)",
                sem.Count == 0, "sem funcao: " + string.Join(", ", sem));
        }

        // ---- os do ICSP sao os mesmos da barra; os nomes que faltavam ----
        foreach (var (tipo, pino, gpio) in new (string, string, int)[]
                 {
                     ("arduino-uno", "12.2", 12), ("arduino-uno", "13.2", 13), ("arduino-uno", "11.2", 11),
                     ("arduino-mega", "50.2", 50), ("arduino-mega", "52.2", 52), ("arduino-mega", "51.2", 51),
                     ("arduino-mega", "SDA", 20), ("arduino-mega", "SCL", 21),
                     ("nano-rp2040-connect", "TX", 1), ("nano-rp2040-connect", "RX", 0),
                     ("arduino-nano", "13.2", 13),
                 })
        {
            var c = new Circuito();
            c.Pecas.Add(new PecaNoCircuito { Id = "placa", Tipo = tipo, X = 0, Y = 0 });
            c.Pecas.Add(new PecaNoCircuito { Id = "led", Tipo = "led", X = 600, Y = 300 });
            Ligar(c, "placa", pino, "led", "A");
            int achado = new Netlist(c).GpioDe(c, "led", "A");
            Conferir($"{tipo}: o fio no {pino} chega no GPIO {gpio}", achado == gpio, $"chegou no {achado}");
        }

        // ---- o IOREF alimenta (5 V no Uno), como o 5V ----
        {
            var c = new Circuito();
            c.Pecas.Add(new PecaNoCircuito { Id = "placa", Tipo = "arduino-uno", X = 0, Y = 0, Usb = true });
            c.Pecas.Add(new PecaNoCircuito { Id = "r", Tipo = "resistor", X = 400, Y = 300, Ohms = 220 });
            c.Pecas.Add(new PecaNoCircuito { Id = "led", Tipo = "led", X = 600, Y = 300 });
            Ligar(c, "placa", "IOREF", "r", "1");
            Ligar(c, "r", "2", "led", "A");
            Ligar(c, "led", "C", "placa", "GND.4");
            var ed = new EditorCircuito();
            ed.Carregar(c);
            var maq = new Maquina { Perfil = PerfilPlaca.Uno };
            maq.Reiniciar();
            var conta = ed.AvancarEnergiaParaTeste(Simulador.App.Bancada.DeUmaSo(maq), new Netlist(c), 0.04);
            double mA = conta.CorrenteDoRamo("led", "led") * 1000;
            Conferir("Uno: o IOREF da 5 V (LED com 220 ohms ate o GND do ICSP: ~14 mA)", mA is > 10 and < 18, $"{mA:0.0} mA");
        }

        // ---- o RESET no GND segura a placa: por fio, pelo do ICSP e por um botao ----
        foreach (var (tipo, reset, gnd) in new[] { ("arduino-uno", "RESET", "GND.2"), ("arduino-uno", "RESET.2", "GND.4"),
                                                   ("arduino-mega", "RESET.2", "GND.6"), ("arduino-nano", "RESET.3", "GND.3"),
                                                   ("nano-rp2040-connect", "RESET", "GND.1") })
        {
            var c = new Circuito();
            c.Pecas.Add(new PecaNoCircuito { Id = "placa", Tipo = tipo, X = 0, Y = 0, Usb = true });
            var ed = new EditorCircuito { EmSimulacao = true };
            ed.Carregar(c);
            var maq = new Maquina { Perfil = PerfilPlaca.De(tipo) };
            maq.Reiniciar();
            var b = Simulador.App.Bancada.DeUmaSo(maq);
            var mudancas = new List<bool>();
            ed.ResetPorFioMudou += (_, preso) => mudancas.Add(preso);
            ed.AplicarEntradas(b, new Netlist(c));
            bool solto = !ed.ResetPorFioParaTeste("placa");
            Ligar(c, "placa", reset, "placa", gnd);
            ed.AplicarEntradas(b, new Netlist(c));
            bool preso = ed.ResetPorFioParaTeste("placa");
            c.Fios.Clear();
            ed.AplicarEntradas(b, new Netlist(c));
            Conferir($"{tipo}: o {reset} ligado no {gnd} segura a placa; tirou o fio, ela volta",
                solto && preso && !ed.ResetPorFioParaTeste("placa") && mudancas.SequenceEqual([true, false]),
                $"{solto}/{preso}/{string.Join(",", mudancas)}");
        }
        {
            var c = new Circuito();
            c.Pecas.Add(new PecaNoCircuito { Id = "placa", Tipo = "arduino-uno", X = 0, Y = 0, Usb = true });
            c.Pecas.Add(new PecaNoCircuito { Id = "bt", Tipo = "pushbutton", X = 400, Y = 300 });
            Ligar(c, "placa", "RESET", "bt", "1.l");
            Ligar(c, "bt", "2.l", "placa", "GND.2");
            var ed = new EditorCircuito { EmSimulacao = true };
            ed.Carregar(c);
            var maq = new Maquina { Perfil = PerfilPlaca.Uno };
            maq.Reiniciar();
            var b = Simulador.App.Bancada.DeUmaSo(maq);
            ed.AplicarEntradas(b, new Netlist(c));
            bool soltoAntes = !ed.ResetPorFioParaTeste("placa");
            ed.BotoesPressionados.Add("bt");
            ed.AplicarEntradas(b, new Netlist(c));
            bool apertado = ed.ResetPorFioParaTeste("placa");
            ed.BotoesPressionados.Remove("bt");
            ed.AplicarEntradas(b, new Netlist(c));
            Conferir("Uno: um botao entre o RESET e o GND reinicia a placa (segura enquanto apertado)",
                soltoAntes && apertado && !ed.ResetPorFioParaTeste("placa"));
        }

        // ---- o AREF e as referencias internas ----
        {
            var maq = new Maquina { Perfil = PerfilPlaca.Uno };
            maq.Reiniciar();
            maq.Analogico[14] = (int)Math.Round(1023 * 0.55 / 5.0);   // 0,55 V no A0
            int padrao = maq.AnalogReadDoArduino(14);
            maq.ReferenciaAnalogica = Maquina.ReferenciaInterna;
            int interna = maq.AnalogReadDoArduino(14);
            maq.ReferenciaAnalogica = Maquina.ReferenciaExterna;
            maq.TensaoNoAref = 1.1;
            int externa = maq.AnalogReadDoArduino(14);
            Conferir("Uno: 0,55 V le ~113 com a referencia de 5 V e ~512 com a de 1,1 V (INTERNAL, ou 1,1 V no AREF)",
                padrao is > 110 and < 116 && interna is > 505 and < 520 && externa is > 505 and < 520,
                $"{padrao} / {interna} / {externa}");
        }
        {
            var c = new Circuito();
            c.Pecas.Add(new PecaNoCircuito { Id = "placa", Tipo = "arduino-uno", X = 0, Y = 0, Usb = true });
            Ligar(c, "placa", "3.3V", "placa", "AREF");
            var ed = new EditorCircuito { EmSimulacao = true };
            ed.Carregar(c);
            var maq = new Maquina { Perfil = PerfilPlaca.Uno };
            maq.Reiniciar();
            maq.ReferenciaAnalogica = Maquina.ReferenciaExterna;
            var b = Simulador.App.Bancada.DeUmaSo(maq);
            var rede = new Netlist(c);
            ed.AvancarEnergiaParaTeste(b, rede, 0.04);
            ed.AplicarEntradas(b, rede);
            Conferir("Uno: com analogReference(EXTERNAL), o AREF ligado no 3V3 vira a referencia (3,3 V)",
                maq.TensaoNoAref is { } v && Math.Abs(v - 3.3) < 0.15, $"{maq.TensaoNoAref}");
        }
        {
            string erro = null;
            var maq = new Maquina { Perfil = PerfilPlaca.Uno };
            maq.Reiniciar();
            try
            {
                var prog = new Parser(Lexer.Tokenizar("void setup() { analogReference(INTERNAL); }" + Environment.NewLine + "void loop() {}")).AnalisarPrograma();
                var interp = new Interpretador(prog, maq);
                interp.Preparar();
                interp.Setup();
            }
            catch (Exception e) { erro = e.Message; }
            Conferir("o sketch com analogReference(INTERNAL) roda e muda a referencia do Uno para 1,1 V",
                erro is null && Math.Abs(maq.TensaoDeReferencia - 1.1) < 1e-9, erro ?? $"{maq.TensaoDeReferencia} V");
        }
    }
}
