namespace Simulador.App;

/// <summary>
/// O tempo na energia (Alpha 24): o capacitor carrega como RC (63% em uma
/// constante de tempo), o indutor deixa a corrente subir e chegar a V/R, o
/// eletrolitico ao contrario avisa, e sem passo (a conta de regime) o
/// capacitor e aberto.
/// </summary>
public static partial class Autoteste
{
    static void Passivos_NoTempo()
    {
        saida.AppendLine("capacitor e indutor no tempo");

        static (EditorCircuito Ed, Bancada B, Netlist R) Montar(double volts, double ohms, string tipo, string modelo, bool aoContrario = false)
        {
            var c = new Circuito();
            c.Pecas.AddRange([
                new PecaNoCircuito { Id = "bat", Tipo = "bateria", X = 0, Y = 0, Volts = volts, MiliAmperes = 20000, MiliAmperesHora = 1000 },
                new PecaNoCircuito { Id = "r", Tipo = "resistor", X = 200, Y = 0, Ohms = ohms },
                new PecaNoCircuito { Id = "x", Tipo = tipo, X = 400, Y = 0, Modelo = modelo },
            ]);
            var (pa, pb) = Passivos.Pinos(tipo);
            if (aoContrario) (pa, pb) = (pb, pa);
            c.Fios.AddRange([
                new Fio { DePeca = "bat", DePino = "+", ParaPeca = "r", ParaPino = "1" },
                new Fio { DePeca = "r", DePino = "2", ParaPeca = "x", ParaPino = pa },
                new Fio { DePeca = "x", DePino = pb, ParaPeca = "bat", ParaPino = "-" },
            ]);
            var ed = new EditorCircuito();
            ed.Carregar(c);
            return (ed, Simulador.App.Bancada.DeUmaSo(new Maquina()), new Netlist(c));
        }

        // RC: 9 V, 10 kohm, 100 uF -> tau = 1 s. Em 1 s, ~63% (Euler implicito com 20 passos: ~62%).
        {
            var (ed, b, r) = Montar(9, 10_000, "capacitor-eletrolitico", "100u-25v");
            double aos0_1 = 0;
            for (int k = 1; k <= 20; k++)
            {
                ed.AvancarEnergiaParaTeste(b, r, 0.05);
                if (k == 2) aos0_1 = ed.TensaoDoCapacitorParaTeste("x");
            }
            double aos1 = ed.TensaoDoCapacitorParaTeste("x");
            for (int k = 0; k < 120; k++) ed.AvancarEnergiaParaTeste(b, r, 0.05);
            double cheio = ed.TensaoDoCapacitorParaTeste("x");
            Conferir("capacitor: 100 uF por 10 kohm em 9 V carrega como RC (~5,6 V em 1 s, cheio em ~6 s)",
                aos0_1 is > 0.7 and < 1.0 && aos1 is > 5.4 and < 5.9 && cheio > 8.9,
                $"0,1 s: {aos0_1:0.00} V | 1 s: {aos1:0.00} V | 7 s: {cheio:0.00} V");
        }

        // Sem passo (a conta de regime, a visao de energia): o capacitor e aberto.
        {
            var (ed, b, r) = Montar(9, 100, "capacitor", "100n");
            double ma = ed.CalcularEnergia(b, r).CorrenteDaBateria("bat") * 1000;
            Conferir("capacitor: em regime ele e aberto (nao passa corrente continua)", Math.Abs(ma) < 0.01, $"{ma:0.000} mA");
        }

        // O indutor: 5 V e 10 ohms -- a corrente sobe ate 0,5 A (e em regime e so o fio dele).
        {
            var (ed, b, r) = Montar(5, 10, "indutor", "10mh");
            ed.AvancarEnergiaParaTeste(b, r, 0.0005);
            double comeco = ed.EnergiaDoQuadro.CorrenteDoRamo("x", "indutor");
            for (int k = 0; k < 40; k++) ed.AvancarEnergiaParaTeste(b, r, 0.0005);
            double depois = ed.EnergiaDoQuadro.CorrenteDoRamo("x", "indutor");
            Conferir("indutor: a corrente sobe aos poucos (L/R = 1 ms) ate ~V/R",
                comeco is > 0.1 and < 0.3 && Math.Abs(depois - 5 / (10 + 8.0)) < 0.02,
                $"0,5 ms: {comeco:0.000} A | 20 ms: {depois:0.000} A");
        }

        // O eletrolitico ao contrario: o aviso.
        {
            var (ed, b, r) = Montar(9, 100, "capacitor-eletrolitico", "100u-25v", aoContrario: true);
            for (int k = 0; k < 40; k++) ed.AvancarEnergiaParaTeste(b, r, 0.05);
            var aviso = ed.EstourosPelaEnergia().FirstOrDefault(e => e.Peca == "x");
            Conferir("capacitor: o eletrolitico ao contrario avisa que estoura", aviso is not null && aviso.Motivo.Contains("contrário"),
                aviso?.Motivo ?? $"(nenhum aviso; {ed.TensaoDoCapacitorParaTeste("x"):0.00} V)");
        }
    }
}
