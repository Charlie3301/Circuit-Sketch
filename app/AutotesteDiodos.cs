namespace Simulador.App;

/// <summary>Os diodos (Alpha 24): o sentido, a queda de cada tipo, o zener regulando e queimando.</summary>
public static partial class Autoteste
{
    static void Diodos_NoCircuito()
    {
        saida.AppendLine("diodos: comum, schottky e zener");

        static Circuito Serie(string tipo, string modelo, double volts, double ohms, bool aoContrario = false)
        {
            var c = new Circuito();
            c.Pecas.AddRange([
                new PecaNoCircuito { Id = "bat", Tipo = "bateria", X = 0, Y = 0, Volts = volts, MiliAmperes = 20000, MiliAmperesHora = 1000 },
                new PecaNoCircuito { Id = "d", Tipo = tipo, X = 200, Y = 0, Modelo = modelo },
                new PecaNoCircuito { Id = "r", Tipo = "resistor", X = 400, Y = 0, Ohms = ohms },
            ]);
            void Ligar(string a, string pa, string b, string pb) => c.Fios.Add(new Fio { DePeca = a, DePino = pa, ParaPeca = b, ParaPino = pb });
            Ligar("bat", "+", "d", aoContrario ? "C" : "A");
            Ligar("d", aoContrario ? "A" : "C", "r", "1");
            Ligar("r", "2", "bat", "-");
            return c;
        }
        static double Ma(Circuito c) => Energia.Calcular(c, null).CorrenteDaBateria("bat") * 1000;

        double comum = Ma(Serie("diodo", null, 5, 100));
        double invertido = Ma(Serie("diodo", null, 5, 100, aoContrario: true));
        double schottky = Ma(Serie("diodo-schottky", null, 5, 100));
        Conferir("diodos: o 1N4007 conduz 5 V com ~0,7 V de queda (43 mA em 100 ohms) e nada ao contrario",
            Math.Abs(comum - 43) < 1 && Math.Abs(invertido) < 0.01, $"{comum:0.0} mA / ao contrario {invertido:0.000} mA");
        Conferir("diodos: o schottky cai so ~0,3 V (47 mA)", Math.Abs(schottky - 47) < 1, $"{schottky:0.0} mA");

        // O regulador de zener: 9 V, 470 ohms, o zener de 5,1 V ao contrario no GND.
        {
            var c = new Circuito();
            c.Pecas.AddRange([
                new PecaNoCircuito { Id = "bat", Tipo = "bateria", X = 0, Y = 0, Volts = 9, MiliAmperes = 20000, MiliAmperesHora = 1000 },
                new PecaNoCircuito { Id = "r", Tipo = "resistor", X = 200, Y = 0, Ohms = 470 },
                new PecaNoCircuito { Id = "z", Tipo = "diodo-zener", X = 400, Y = 0 },
            ]);
            c.Fios.AddRange([
                new Fio { DePeca = "bat", DePino = "+", ParaPeca = "r", ParaPino = "1" },
                new Fio { DePeca = "r", DePino = "2", ParaPeca = "z", ParaPino = "C" },
                new Fio { DePeca = "z", DePino = "A", ParaPeca = "bat", ParaPino = "-" },
            ]);
            var conta = Energia.Calcular(c, null);
            double v = conta.TensaoDoPino("z", "C") - conta.TensaoDoPino("z", "A");
            Conferir("diodos: o zener de 5,1 V segura ~5,1 V com o resistor em serie", v is > 5.1 and < 5.25, $"{v:0.00} V");
        }

        // O zener direto na bateria: a corrente nao tem limite, e ele queima.
        {
            var c = Serie("diodo-zener", "5v1", 9, 0.001, aoContrario: true);
            var ed = new EditorCircuito();
            ed.Carregar(c);
            ed.AvancarEnergiaParaTeste(Simulador.App.Bancada.DeUmaSo(new Maquina()), new Netlist(c), 0.05);
            var queimou = ed.EstourosPelaEnergia().FirstOrDefault(e => e.Peca == "d");
            Conferir("diodos: o zener direto na bateria, sem resistor, queima (e o aviso diz o porque)",
                queimou is { Queimou: true } && queimou.Motivo.Contains("resistor"), queimou?.Motivo ?? "(nenhum aviso)");
        }
    }
}
