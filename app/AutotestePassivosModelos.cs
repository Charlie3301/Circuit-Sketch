namespace Simulador.App;

/// <summary>
/// Os modelos novos dos passivos (Alpha 25): cada um com o seu desenho, os de
/// fabrica que nao mudaram (o fusivel de 1 A, o varistor de 27 V, o 100 nF, o
/// 100 uF), e os novos na conta -- o de filme e o de tantalo com o valor deles,
/// o tantalo ao contrario estoura como o eletrolitico.
/// </summary>
public static partial class Autoteste
{
    static void Passivos_Modelos()
    {
        saida.AppendLine("passivos: os modelos, os desenhos e os de fabrica");

        // ---- cada modelo tem o seu desenho ----
        var semDesenho = new List<string>();
        int modelos = 0;
        foreach (var tipo in new[] { "capacitor", "capacitor-eletrolitico", "indutor", "cristal" })
            foreach (var f in Passivos.ModelosDe(tipo))
            {
                modelos++;
                if (!Pecas.Existe($"{tipo}-{f.Modelo}")) semDesenho.Add($"{tipo}-{f.Modelo}");
            }
        foreach (var tipo in new[] { "fusivel", "varistor" })
            foreach (var m in Semicondutores.Modelos(tipo))
            {
                modelos++;
                if (!Pecas.Existe($"{tipo}-{m}")) semDesenho.Add($"{tipo}-{m}");
            }
        Conferir($"passivos: os {modelos} modelos (ceramicos, filme, eletroliticos, tantalo, indutores, cristais, fusiveis, varistores) tem desenho",
            modelos >= 60 && semDesenho.Count == 0, string.Join(", ", semDesenho));

        // ---- os de fabrica nao mudaram com a lista maior ----
        var nova = (string tipo) => new PecaNoCircuito { Tipo = tipo };
        Conferir("passivos: os de fabrica continuam o 100 nF, o 100 uF 25 V, o 100 uH, o 16 MHz, o fusivel de 1 A e o varistor de 27 V",
            Passivos.Da(nova("capacitor")).Modelo == "100n" && Passivos.Da(nova("capacitor-eletrolitico")).Modelo == "100u-25v"
            && Passivos.Da(nova("indutor")).Modelo == "100uh" && Passivos.Da(nova("cristal")).Modelo == "16mhz"
            && Semicondutores.Protecao(nova("fusivel")).Modelo == "1a" && Semicondutores.Protecao(nova("varistor")).Modelo == "14d270k");

        // ---- o tantalo de 10 uF ao contrario (12 V no -) estoura como o eletrolitico ----
        {
            var c = new Circuito();
            c.Pecas.AddRange([
                new PecaNoCircuito { Id = "bat", Tipo = "bateria", X = 0, Y = 0, Volts = 12, MiliAmperes = 20000, MiliAmperesHora = 1000 },
                new PecaNoCircuito { Id = "t", Tipo = "capacitor-eletrolitico", Modelo = "10u-16v-tantalo", X = 200, Y = 0 }]);
            c.Fios.Add(new Fio { DePeca = "bat", DePino = "+", ParaPeca = "t", ParaPino = "-" });
            c.Fios.Add(new Fio { DePeca = "t", DePino = "+", ParaPeca = "bat", ParaPino = "-" });
            var ed = new EditorCircuito();
            ed.Carregar(c);
            ed.AvancarEnergiaParaTeste(Simulador.App.Bancada.DeUmaSo(new Maquina()), new Netlist(c), 0.05);
            var estouro = ed.EstourosPelaEnergia().FirstOrDefault(e => e.Peca == "t");
            Conferir("tantalo: ligado ao contrario ele estoura (o + do tantalo e marcado na gota)", estouro is { Queimou: true }, estouro?.Motivo);
        }

        // ---- o de filme de 1 uF tem 1 uF, e aguenta os 63 V dele ----
        {
            var f = Passivos.Da(new PecaNoCircuito { Tipo = "capacitor", Modelo = "1u-filme" });
            Conferir("capacitor de filme: o de 1 uF em caixa tem 1 uF e 63 V", Math.Abs(f.Valor - 1e-6) < 1e-12 && f.Volts == 63);
        }
    }
}
