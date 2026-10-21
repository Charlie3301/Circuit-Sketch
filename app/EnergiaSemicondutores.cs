namespace Simulador.App;

/// <summary>
/// Os semicondutores controlados na conta (Alpha 24): transistor bipolar,
/// MOSFET e optoacoplador.
///
/// Cada um tem um ramo de controle e um canal. O controle e um diodo (a
/// juncao base-emissor; o LED do opto) -- o MOSFET nao tem: o gate so le a
/// tensao. O canal e um "diodo" (conduz num sentido so) cuja resistencia
/// acompanha o controle, volta a volta do Resolver:
///
///   - bipolar: a corrente que a carga deixaria passar, limitada a beta x Ib
///     (regiao ativa); quando a carga pede menos, ele satura (Vce ~0,2 V);
///   - MOSFET: abaixo do limiar, aberto; acima, k (Vgs - Vt)^2 na saturacao,
///     ou a resistencia de ligado (Rds on) se a carga pede menos -- e o diodo
///     do corpo, ao contrario;
///   - opto: o fototransistor leva a corrente do LED vezes o CTR (100% no
///     PC817, 50% no 4N25). No 4N35 a base fica para fora, e solta nao conta.
///
/// O MOSFET de canal P e o espelho do N: o canal vai do source (no +) ao
/// dreno, e liga com o gate abaixo do source.
/// </summary>
public sealed partial class Energia
{
    sealed class Chave
    {
        public PecaNoCircuito Peca;
        public int Controle = -1, Canal;       // indices em ramos
        public int Gate = -1, Fonte = -1;      // MOSFET: os nos de G e S
        public double Ganho;                    // beta, ou o CTR
        public double Saturacao;                // a R minima do canal
        public double QuedaDoCanal;             // Vce de saturacao
        public FichaDeMosfet Mosfet;
    }

    readonly List<Chave> chaves = [];

    /// <summary>Monta controle e canal de cada semicondutor (no fim do Montar, como os reguladores).</summary>
    void MontarChaves(Circuito circuito, Netlist rede)
    {
        foreach (var peca in circuito.Pecas)
        {
            if (!Semicondutores.EhControlado(peca.Tipo)) continue;
            int N(string pino) => No(rede, peca.Id, pino);
            var chave = new Chave { Peca = peca };
            switch (peca.Tipo)
            {
                case var bipolar when Semicondutores.EhBipolar(bipolar):
                {
                    var f = Semicondutores.Transistor(peca);
                    int e = N("E"), b = N("B"), c = N("C");
                    if (e < 0 || b < 0 || c < 0) continue;
                    bool pnp = Semicondutores.EhPnp(peca.Tipo);
                    // A base dos de potencia puxa bem mais (o TIP31C tem beta 25).
                    double baseMaxima = Math.Max(0.05, 3 * f.CorrenteMaxima / f.Beta);
                    chave.Controle = ramos.Count;
                    ramos.Add(pnp ? new Ramo(peca.Id, e, b, 1, f.Vbe, 0.001, baseMaxima, Rotulo: "base")
                                  : new Ramo(peca.Id, b, e, 1, f.Vbe, 0.001, baseMaxima, Rotulo: "base"));
                    chave.Canal = ramos.Count;
                    ramos.Add(pnp ? new Ramo(peca.Id, e, c, 1e9, f.Vcesat, 0.01, f.CorrenteMaxima, Rotulo: "coletor")
                                  : new Ramo(peca.Id, c, e, 1e9, f.Vcesat, 0.01, f.CorrenteMaxima, Rotulo: "coletor"));
                    chave.Ganho = f.Beta;
                    chave.Saturacao = 0.3;
                    chave.QuedaDoCanal = f.Vcesat;
                    break;
                }
                case "mosfet-n":
                case "mosfet-p":
                {
                    var f = Semicondutores.Mosfet(peca);
                    int g = N("G"), d = N("D"), s = N("S");
                    if (g < 0 || d < 0 || s < 0) continue;
                    chave.Gate = g;
                    chave.Fonte = s;
                    chave.Mosfet = f;
                    chave.Canal = ramos.Count;
                    // O canal: do dreno ao source no N; do source ao dreno no P (o source no +).
                    // E o diodo do corpo, ao contrario do canal.
                    if (f.CanalP)
                    {
                        ramos.Add(new Ramo(peca.Id, s, d, 1e9, 0.001, 0.01, f.CorrenteMaxima, Rotulo: "dreno"));
                        ramos.Add(new Ramo(peca.Id, d, s, 0.05, 0.8, 0.01, f.CorrenteMaxima, Rotulo: "corpo"));
                    }
                    else
                    {
                        ramos.Add(new Ramo(peca.Id, d, s, 1e9, 0.001, 0.01, f.CorrenteMaxima, Rotulo: "dreno"));
                        ramos.Add(new Ramo(peca.Id, s, d, 0.05, 0.8, 0.01, f.CorrenteMaxima, Rotulo: "corpo"));
                    }
                    chave.Saturacao = f.ResistenciaLigado;
                    break;
                }
                case "optoacoplador":
                case "optoacoplador-4n35":
                {
                    int a = N("A"), k = N("K"), c = N("C"), e = N("E");
                    if (a < 0 || k < 0 || c < 0 || e < 0) continue;
                    chave.Controle = ramos.Count;
                    ramos.Add(new Ramo(peca.Id, a, k, 5, 1.2, 0.005, 0.05, Rotulo: "led"));
                    chave.Canal = ramos.Count;
                    ramos.Add(new Ramo(peca.Id, c, e, 1e9, 0.2, 0.001, 0.05, Rotulo: "coletor"));
                    chave.Ganho = Semicondutores.Opto(peca).Ctr;
                    chave.Saturacao = 50;
                    chave.QuedaDoCanal = 0.2;
                    break;
                }
            }
            chaves.Add(chave);
        }
    }

    /// <summary>Uma volta das chaves: a resistencia do canal que o controle de agora permite.</summary>
    bool AtualizarChaves(bool[] ligados)
    {
        bool mudou = false;
        foreach (var ch in chaves)
        {
            var canal = ramos[ch.Canal];
            double vCanal = TensaoDoIndice(canal.A) - TensaoDoIndice(canal.B);
            double r;
            if (ch.Mosfet is { } f)
            {
                double vgs = TensaoDoIndice(ch.Gate) - TensaoDoIndice(ch.Fonte);
                if (f.CanalP) vgs = -vgs;               // o P liga com o gate abaixo do source
                if (vgs <= f.Limiar) r = 1e9;
                else
                {
                    double maximo = f.K * (vgs - f.Limiar) * (vgs - f.Limiar);
                    r = vCanal <= 0 ? f.ResistenciaLigado : Math.Max(f.ResistenciaLigado, vCanal / maximo);
                }
            }
            else
            {
                var controle = ramos[ch.Controle];
                double entrada = ligados[ch.Controle]
                    ? Math.Max(0, (TensaoDoIndice(controle.A) - TensaoDoIndice(controle.B) - controle.Queda) / controle.Resistencia)
                    : 0;
                double alvo = ch.Ganho * entrada;
                r = alvo < 1e-7 ? 1e9 : Math.Max(ch.Saturacao, (vCanal - ch.QuedaDoCanal) / alvo);
            }
            if (Math.Abs(r - canal.Resistencia) > 0.01 * Math.Min(r, canal.Resistencia) + 1e-6)
            {
                ramos[ch.Canal] = canal with { Resistencia = r };
                mudou = true;
            }
        }
        return mudou;
    }
}
