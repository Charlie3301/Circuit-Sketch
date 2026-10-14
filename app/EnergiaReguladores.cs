namespace Simulador.App;

/// <summary>
/// Os reguladores de tensao na conta (Alpha 24). Cada um vira dois ramos:
///
///   - a SAIDA: uma fonte que so fornece (o "diodo" do trilho da USB), com a
///     tensao que ele consegue dar agora -- a nominal, ou a entrada menos a
///     folga quando a entrada esta baixa (o 7805 com 6 V da 4 V), ou nada;
///   - a ENTRADA: a carga que ele e para quem o alimenta. No linear a
///     corrente que sai e a que entra (a diferenca de tensao vira calor); no
///     chaveado a potencia que sai, dividida pela eficiencia, entra -- o
///     LM2596 de 12 V para 5 V puxa menos da metade da corrente da carga.
///
/// As duas dependem da propria resposta (a entrada cai com a carga, a carga
/// depende da saida), entao andam junto com as voltas do Resolver, ate parar
/// de mudar. A protecao de corrente e uma resistencia de saida que cresce o
/// bastante para segurar a corrente no limite (o curto na saida).
/// </summary>
public sealed partial class Energia
{
    /// <summary>O que o regulador esta fazendo nesta conta.</summary>
    /// <param name="Potencia">O que ele esquenta, em watts.</param>
    /// <param name="EmQueda">A entrada nao da a folga: a saida ficou abaixo da nominal.</param>
    /// <param name="Limitando">A carga pede mais que o limite, e ele segura a corrente (a tensao cai).</param>
    /// <param name="SobreTensao">Entrada acima do maximo da ficha.</param>
    /// <param name="SoPassando">O step-up com a entrada acima do ajuste: a saida e a entrada menos o diodo.</param>
    public sealed record EstadoDoRegulador(double Entrada, double Saida, double CorrenteDeEntrada, double CorrenteDeSaida,
                                           double Potencia, double Nominal, bool Ligado, bool EmQueda, bool Limitando,
                                           bool SobreTensao, bool SoPassando);

    sealed class Conversor
    {
        public PecaNoCircuito Peca;
        public FichaDeRegulador Ficha;
        public double Nominal;
        public int NoEntrada, NoTerraDaEntrada, NoSaida, NoTerraDaSaida;
        public int RamoEntrada, RamoSaida;
        public double ResistenciaDeSaida;
        public bool Desligado;

        // a ultima volta
        public double Vin, Vout, Iin, Iout, Alvo;
        public bool Limitando, SobreTensao, SoPassando;
    }

    readonly List<Conversor> conversores = [];

    /// <summary>Os reguladores que o editor desligou: a protecao termica, ou queimado.</summary>
    IReadOnlySet<string> reguladoresDesligados;

    /// <summary>O estado de um regulador nesta conta (nulo: nao e regulador, ou nao esta ligado em nada).</summary>
    public EstadoDoRegulador RegulagemDe(string idDaPeca)
    {
        var c = conversores.FirstOrDefault(x => x.Peca.Id == idDaPeca);
        if (c is null) return null;
        double potencia = Math.Max(0, c.Vin * c.Iin - Math.Max(0, c.Vout) * c.Iout);
        bool ligado = c.Alvo > 0.05;
        return new(c.Vin, c.Vout, c.Iin, c.Iout, potencia, c.Nominal, ligado,
                   ligado && !c.SoPassando && c.Alvo < c.Nominal - 0.05, c.Limitando, c.SobreTensao, c.SoPassando);
    }

    /// <summary>Monta os dois ramos de cada regulador do circuito (no fim do Montar: os indices ficam fixos).</summary>
    void MontarConversores(Circuito circuito, Netlist rede)
    {
        foreach (var peca in circuito.Pecas)
        {
            if (!Reguladores.EhRegulador(peca.Tipo) || Reguladores.Da(peca) is not { } ficha) continue;
            var (pe, te, ps, ts) = Reguladores.PinosDe(peca.Tipo);
            int a = No(rede, peca.Id, pe), b = No(rede, peca.Id, te), c = No(rede, peca.Id, ps), d = No(rede, peca.Id, ts);
            if (a < 0 || b < 0 || c < 0 || d < 0) continue;

            var conv = new Conversor
            {
                Peca = peca, Ficha = ficha, Nominal = Reguladores.SaidaDe(peca),
                NoEntrada = a, NoTerraDaEntrada = b, NoSaida = c, NoTerraDaSaida = d,
                ResistenciaDeSaida = ficha.Tipo == TipoDeRegulador.Abaixador ? 0.03 : 0.05,
                Desligado = reguladoresDesligados?.Contains(peca.Id) == true,
            };
            conv.RamoEntrada = ramos.Count;
            ramos.Add(new Ramo(peca.Id, a, b, 1e9, 0, 0, ficha.LimiteDeCorrente, Rotulo: "entrada"));
            conv.RamoSaida = ramos.Count;
            ramos.Add(new Ramo(peca.Id, d, c, conv.ResistenciaDeSaida, QuedaDaUsb, 0, ficha.CorrenteMaxima,
                               Fem: -QuedaDaUsb, Rotulo: "saida"));
            conversores.Add(conv);
        }
    }

    /// <summary>A saida que o regulador consegue dar com esta entrada (0: desligado).</summary>
    static double AlvoDe(Conversor c, double vin, out bool soPassando)
    {
        soPassando = false;
        var f = c.Ficha;
        switch (f.Tipo)
        {
            case TipoDeRegulador.Linear:
                // Abaixo da folga ele "segue" a entrada, 2 V abaixo (o 78xx com 6 V da 4 V).
                return vin <= 0.5 ? 0 : Math.Clamp(vin - f.Folga, 0, c.Nominal);

            case TipoDeRegulador.Abaixador:
                if (vin < f.EntradaMinima) return 0;
                return Math.Clamp(vin - f.Folga, 0, c.Nominal);

            default:
                // O step-up: com o chip desligado (entrada baixa) ou com a
                // entrada acima do ajuste, a corrente passa pelo indutor e
                // pelo diodo -- sai a entrada menos ~0,4 V.
                double passando = Math.Max(0, vin - 0.4);
                if (vin < f.EntradaMinima || passando >= c.Nominal)
                {
                    soPassando = passando > 0.05;
                    return passando;
                }
                return c.Nominal;
        }
    }

    /// <summary>
    /// Uma volta dos reguladores, com a resposta da volta anterior: a saida
    /// que cada um da, a protecao de corrente e o quanto ele puxa da entrada.
    /// </summary>
    /// <returns>Se algum mudou o bastante para valer outra volta.</returns>
    bool AtualizarConversores(bool[] ligados)
    {
        bool mudou = false;
        foreach (var c in conversores)
        {
            double vin = Math.Max(0, TensaoDoIndice(c.NoEntrada) - TensaoDoIndice(c.NoTerraDaEntrada));
            double vout = TensaoDoIndice(c.NoSaida) - TensaoDoIndice(c.NoTerraDaSaida);
            var saida = ramos[c.RamoSaida];
            // A corrente que saiu nesta volta: zero com o "diodo" da saida apagado
            // (a conta dele so vale ligado -- apagado, ela daria a corrente que
            // sairia, e o regulador "puxaria" isso da bateria).
            double iout = ligados[c.RamoSaida]
                ? Math.Max(0, (TensaoDoIndice(saida.A) - TensaoDoIndice(saida.B) - saida.Queda - saida.Fem) / saida.Resistencia)
                : 0;

            c.SobreTensao = vin > c.Ficha.EntradaMaxima;
            double alvo = c.Desligado || c.SobreTensao ? 0 : AlvoDe(c, vin, out c.SoPassando);
            if (c.Desligado || c.SobreTensao) c.SoPassando = false;

            // A protecao de corrente: a resistencia de saida que segura a carga
            // de agora (vout/iout) no limite. Com carga leve, a de sempre.
            double r = c.ResistenciaDeSaida;
            if (alvo > 0 && iout > 1e-6)
                r = Math.Max(r, alvo / c.Ficha.LimiteDeCorrente - Math.Max(vout, 0) / iout);
            c.Limitando = r > c.ResistenciaDeSaida * 1.5;

            // O que ele puxa da entrada: no linear e no chaveado passando direto,
            // a corrente da carga; chaveado, a potencia dela pela eficiencia.
            double iin = 0;
            if (vin > 0.3 && !c.SobreTensao)
                iin = c.Ficha.Quiescente + (c.Ficha.Tipo == TipoDeRegulador.Linear || c.SoPassando || alvo <= 0
                    ? iout
                    : alvo * iout / (c.Ficha.Eficiencia * vin));
            double rEntrada = iin <= 1e-9 ? 1e9 : Math.Max(vin, 0.05) / iin;

            double fem = -(alvo + QuedaDaUsb);
            if (Math.Abs(saida.Fem - fem) > 1e-4 || Math.Abs(saida.Resistencia - r) > 1e-4 * Math.Max(1, r))
            {
                ramos[c.RamoSaida] = saida with { Fem = fem, Resistencia = r };
                mudou = true;
            }
            var entrada = ramos[c.RamoEntrada];
            if (Math.Abs(entrada.Resistencia - rEntrada) > 1e-3 * Math.Max(entrada.Resistencia, rEntrada)
                && Math.Abs(vin / entrada.Resistencia - iin) > 1e-5)
            {
                ramos[c.RamoEntrada] = entrada with { Resistencia = rEntrada };
                mudou = true;
            }

            c.Vin = vin; c.Vout = vout; c.Iin = iin; c.Iout = iout; c.Alvo = alvo;
        }
        return mudou;
    }
}
