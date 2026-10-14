namespace Simulador.App;

/// <summary>
/// O tempo na conta de energia (Alpha 24): o capacitor e o indutor.
///
/// Ate aqui a conta era so de regime: cada quadro, as tensoes e correntes de
/// agora, sem passado. O capacitor e o indutor precisam do quadro anterior --
/// a carga que o capacitor tinha, a corrente que passava no indutor. A
/// <see cref="MemoriaDaEnergia"/> guarda isso de um quadro para o outro, e
/// cada um vira um ramo (Euler implicito, estavel com qualquer passo):
///
///   - capacitor: R = dt/C e Fem = a tensao que ele tinha. Sem passo (a conta
///     fora do quadro: a visao de energia, os divisores), ele e o que e em
///     regime: aberto.
///   - indutor: R = L/dt e Fem = -i_anterior * L/dt (a corrente continua);
///     sem passo, em regime, e so o fio dele (a resistencia).
/// </summary>
public sealed partial class Energia
{
    /// <summary>O que o capacitor e o indutor lembram de um quadro para o outro.</summary>
    public sealed class MemoriaDaEnergia
    {
        internal readonly Dictionary<string, double> Tensoes = [];
        internal readonly Dictionary<string, double> Correntes = [];

        /// <summary>A tensao em que o capacitor ficou (0: descarregado).</summary>
        public double TensaoDo(string idDoCapacitor) => Tensoes.GetValueOrDefault(idDoCapacitor);

        /// <summary>A corrente que o indutor tinha no fim do ultimo quadro.</summary>
        public double CorrenteDo(string idDoIndutor) => Correntes.GetValueOrDefault(idDoIndutor);

        public void Esquecer() { Tensoes.Clear(); Correntes.Clear(); }
    }

    MemoriaDaEnergia memoria;

    /// <summary>O tempo deste quadro, em segundos (0: conta de regime, sem passado).</summary>
    double passo;

    IEnumerable<Ramo> RamosQueGuardamEnergia(PecaNoCircuito peca, Netlist rede)
    {
        var ficha = Passivos.Da(peca);
        if (ficha is null || peca.Tipo == "cristal") yield break;   // o cristal so vibra ligado num oscilador
        var (pa, pb) = Passivos.Pinos(peca.Tipo);
        int a = No(rede, peca.Id, pa), b = No(rede, peca.Id, pb);
        if (a < 0 || b < 0) yield break;

        if (peca.Tipo == "indutor")
        {
            double dcr = Math.Max(ficha.Resistencia, 0.001);
            if (passo <= 0) { yield return new Ramo(peca.Id, a, b, dcr, 0, 0, ficha.Corrente, Rotulo: "indutor"); yield break; }
            double rl = ficha.Valor / passo;
            double anterior = memoria?.CorrenteDo(peca.Id) ?? 0;
            yield return new Ramo(peca.Id, a, b, rl + dcr, 0, 0, ficha.Corrente, Fem: -anterior * rl, Rotulo: "indutor");
            yield break;
        }

        double carga = memoria?.TensaoDo(peca.Id) ?? 0;
        double r = passo <= 0 ? 1e9 : passo / ficha.Valor + Math.Max(ficha.Resistencia, 0.001);
        yield return new Ramo(peca.Id, a, b, r, 0, 0, 1e3, Fem: carga, Rotulo: "capacitor");
    }

    /// <summary>Depois da conta do quadro: o que cada capacitor e indutor leva para o proximo.</summary>
    void Lembrar(Circuito circuito)
    {
        if (memoria is null || passo <= 0) return;
        foreach (var peca in circuito.Pecas)
        {
            if (peca.Tipo == "indutor") memoria.Correntes[peca.Id] = CorrenteDoRamo(peca.Id, "indutor");
            else if (peca.Tipo is "capacitor" or "capacitor-eletrolitico" && Passivos.Da(peca) is { } ficha)
            {
                // A carga nova e a de antes mais o que entrou (I dt / C) -- sem a
                // queda na ESR, que e do terminal e nao do capacitor.
                memoria.Tensoes[peca.Id] = memoria.TensaoDo(peca.Id) + CorrenteDoRamo(peca.Id, "capacitor") * passo / ficha.Valor;
            }
        }
    }
}
