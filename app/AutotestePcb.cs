namespace Simulador.App;

/// <summary>
/// A PCB perfurada (Alpha 24): o tamanho e o tipo, os furos sao isolados,
/// e so o fio entre dois furos liga uma coisa na outra.
/// </summary>
public static partial class Autoteste
{
    static void Pcb_Base()
    {
        saida.AppendLine("PCB perfurada");

        var def = Catalogo.Obter("pcb-10x6");
        Conferir("PCB: qualquer tamanho nasce na hora (10 x 6 = 60 furos), e o tamanho fora da faixa nao existe",
            def is { Pinos.Count: 60 } && Catalogo.Obter("pcb-2x900") is null && Catalogo.Obter(Catalogo.IdPcbPadrao) is not null,
            $"{def?.Pinos.Count} furos");
        Conferir("PCB: os furos sao isolados (nenhuma ligacao interna)",
            !Catalogo.LigacoesInternas("pcb-10x6").Any(), "");

        // Uma bateria em dois furos e um resistor em outros dois: sem fio por baixo, nada passa; com o fio, passa.
        static double Corrente(bool comFio)
        {
            var c = new Circuito();
            c.Pecas.AddRange([
                new PecaNoCircuito { Id = "pcb", Tipo = "pcb-10x6", X = 0, Y = 0 },
                new PecaNoCircuito { Id = "bat", Tipo = "bateria", X = 0, Y = 300, Volts = 5, MiliAmperes = 20000, MiliAmperesHora = 1000 },
                new PecaNoCircuito { Id = "r", Tipo = "resistor", X = 200, Y = 0, Ohms = 100 }]);
            void L(string a, string pa, string b, string pb) => c.Fios.Add(new Fio { DePeca = a, DePino = pa, ParaPeca = b, ParaPino = pb });
            L("bat", "+", "pcb", "x1y1"); L("r", "1", "pcb", "x3y1");
            L("r", "2", "pcb", "x3y3"); L("bat", "-", "pcb", "x3y3");
            if (comFio) L("pcb", "x1y1", "pcb", "x3y1");
            return Math.Abs(Energia.Calcular(c, null).CorrenteDaBateria("bat"));
        }
        double sem = Corrente(false), com = Corrente(true);
        Conferir("PCB: sem o fio entre os furos nao passa nada; com o fio, 50 mA no resistor de 100 ohms",
            sem < 1e-5 && com is > 0.045 and < 0.052, $"sem: {sem * 1000:0.000} mA | com: {com * 1000:0.0} mA");
    }
}
