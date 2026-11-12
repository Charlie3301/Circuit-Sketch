namespace Simulador.App;

/// <summary>
/// Os modulos de rele de 1, 2 e 4 canais (Alpha 24): os azuis dos kits, com
/// o rele SRD-05VDC-SL-C e um optoacoplador PC817 na entrada de cada canal.
///
/// Como o de verdade:
///   - a entrada e ativa em LOW: o VCC acende o LED do opto (e o LED do canal)
///     por um resistor de 1 kohm ate o pino IN; o pino em LOW fecha esse
///     caminho e puxa so ~2 mA. Em HIGH, ou solto, nada acontece;
///   - o opto liga o transistor, que poe a bobina (70 ohms) entre o VCC e o
///     GND: ~70 mA do VCC por canal ligado -- do 5V da placa, e nao do pino;
///   - a bobina de 5 V arma a partir de ~3,75 V (75% da nominal): com o VCC
///     nos 3,3 V da placa o LED acende e o rele NAO arma;
///   - o contato: COM no NO com o rele armado, no NC desarmado (10 A).
///
/// O de 1 canal tem os nomes do "wokwi-relay-module" (VCC, GND, IN; NO, COM,
/// NC); o modelo "alto" e o jumper H do modulo de 1 canal (ativo em HIGH).
/// </summary>
public static class ModulosDeRele
{
    public static bool EhModulo(string tipo) => tipo is "modulo-rele-1" or "modulo-rele-2" or "modulo-rele-4";

    public static int Canais(string tipo) => tipo switch
    {
        "modulo-rele-1" => 1,
        "modulo-rele-2" => 2,
        "modulo-rele-4" => 4,
        _ => 0,
    };

    static string Sufixo(string tipo, int canal) => Canais(tipo) == 1 ? "" : canal.ToString();

    public static string Entrada(string tipo, int canal) => "IN" + Sufixo(tipo, canal);
    public static string Comum(string tipo, int canal) => "COM" + Sufixo(tipo, canal);
    public static string Aberto(string tipo, int canal) => "NO" + Sufixo(tipo, canal);
    public static string Fechado(string tipo, int canal) => "NC" + Sufixo(tipo, canal);

    /// <summary>Ativo em LOW (o normal), ou em HIGH (o jumper H do de 1 canal: Modelo "alto").</summary>
    public static bool AtivoEmBaixo(PecaNoCircuito peca) => peca.Modelo != "alto";

    /// <summary>A bobina do SRD-05VDC: 70 ohms; arma com 3,75 V, aguenta ate ~7 V.</summary>
    public const double Bobina = 70, TensaoQueArma = 3.75, TensaoQueQueima = 7.0;

    /// <summary>A corrente da bobina armada (a bobina so arma acima dela).</summary>
    public const double CorrenteQueArma = TensaoQueArma / Bobina;
}

public sealed partial class Energia
{
    /// <summary>
    /// A entrada (o LED do opto, ate o pino) e a bobina de cada canal, como
    /// uma chave: a corrente do LED decide se o transistor poe a bobina no VCC.
    /// </summary>
    void MontarModulosDeRele(Circuito circuito, Netlist rede)
    {
        foreach (var peca in circuito.Pecas)
        {
            if (!ModulosDeRele.EhModulo(peca.Tipo)) continue;
            int vcc = No(rede, peca.Id, "VCC"), gnd = No(rede, peca.Id, "GND");
            if (vcc < 0 || gnd < 0 || vcc == gnd) continue;

            // O LED de energia: 1 kohm e o LED vermelho.
            ramos.Add(new Ramo(peca.Id, vcc, gnd, 1000, 1.8, 0.003, 0.03, Rotulo: "energia"));

            bool emBaixo = ModulosDeRele.AtivoEmBaixo(peca);
            for (int k = 1; k <= ModulosDeRele.Canais(peca.Tipo); k++)
            {
                int entrada = No(rede, peca.Id, ModulosDeRele.Entrada(peca.Tipo, k));
                if (entrada < 0) continue;   // IN solto: o canal fica desligado
                var chave = new Chave { Peca = peca };
                chave.Controle = ramos.Count;
                // O LED do opto e o LED do canal em serie (~3 V) e 1 kohm.
                ramos.Add(emBaixo
                    ? new Ramo(peca.Id, vcc, entrada, 1000, 3.0, 0.002, 0.03, Rotulo: $"entrada{k}")
                    : new Ramo(peca.Id, entrada, gnd, 1000, 3.0, 0.002, 0.03, Rotulo: $"entrada{k}"));
                chave.Canal = ramos.Count;
                ramos.Add(new Ramo(peca.Id, vcc, gnd, 1e9, 0, 0.07, 0.2, Rotulo: $"bobina{k}"));
                // O transistor satura com ~1 mA no LED do opto: a bobina e quem limita.
                chave.Ganho = 1000;
                chave.Saturacao = ModulosDeRele.Bobina;
                chave.QuedaDoCanal = 0;
                chaves.Add(chave);
            }
        }
    }
}

public partial class EditorCircuito
{
    /// <summary>A bobina deste canal esta armada?</summary>
    internal bool CanalDoModuloArmado(PecaNoCircuito peca, int canal, Maquina maq, Netlist rede)
    {
        if (EnergiaDoQuadro is { } conta)
            return Math.Abs(conta.CorrenteDoRamo(peca.Id, $"bobina{canal}")) >= ModulosDeRele.CorrenteQueArma * 0.98;

        // Sem a conta de energia (sem bateria nem motor no circuito): pelo
        // que esta ligado. O VCC num 5 V de placa; a entrada num GPIO no
        // nivel que liga (ou direto no GND, que e o LOW de sempre).
        if (rede is null) return false;
        var noVcc = rede.PinosDePlacaNoMesmoNo(Atual, peca.Id, "VCC").ToList();
        bool cincoVolts = noVcc.Any(n => Netlist.EhPinoDeAlimentacao(n) && !n.StartsWith("3V3", StringComparison.Ordinal)
                                         && !n.StartsWith("3.3V", StringComparison.Ordinal));
        if (!cincoVolts) return false;

        string entrada = ModulosDeRele.Entrada(peca.Tipo, canal);
        bool emBaixo = ModulosDeRele.AtivoEmBaixo(peca);
        var noIn = rede.PinosDePlacaNoMesmoNo(Atual, peca.Id, entrada).ToList();
        if (noIn.Any(Netlist.EhPinoDeTerra)) return emBaixo;
        if (noIn.Any(Netlist.EhPinoDeAlimentacao)) return !emBaixo;

        int gpio = rede.GpioDe(Atual, peca.Id, entrada);
        if (gpio < 0 || maq is null || maq.Modo[gpio] != 1) return false;
        bool alto = maq.Pwm[gpio] > 127;
        return emBaixo ? !alto : alto;
    }

    /// <summary>A cada quadro: o contato de cada canal leva o nivel do COM ao NO (armado) ou ao NC.</summary>
    void AplicarModuloDeRele(PecaNoCircuito peca, Maquina maq, Netlist rede)
    {
        if (relesSegurando.TryGetValue(peca.Id, out var antigos))
            foreach (int g in antigos) maq.NivelExterno[g] = -1;
        var segurando = new HashSet<int>();
        for (int k = 1; k <= ModulosDeRele.Canais(peca.Tipo); k++)
        {
            bool armado = CanalDoModuloArmado(peca, k, maq, rede);
            LevarNivel(peca, maq, rede, ModulosDeRele.Comum(peca.Tipo, k),
                       armado ? ModulosDeRele.Aberto(peca.Tipo, k) : ModulosDeRele.Fechado(peca.Tipo, k), segurando);
        }
        relesSegurando[peca.Id] = segurando;
    }

    /// <summary>Os contatos fechados de cada canal, para a conta de energia.</summary>
    IEnumerable<Energia.Contato> ContatosDoModuloDeRele(PecaNoCircuito peca, Bancada bancada, Netlist rede)
    {
        var maq = MaquinaDaPeca(bancada, rede, peca);
        for (int k = 1; k <= ModulosDeRele.Canais(peca.Tipo); k++)
        {
            bool armado = CanalDoModuloArmado(peca, k, maq, rede);
            yield return new Energia.Contato(peca.Id, ModulosDeRele.Comum(peca.Tipo, k),
                armado ? ModulosDeRele.Aberto(peca.Tipo, k) : ModulosDeRele.Fechado(peca.Tipo, k), 10.0);
        }
    }

    /// <summary>A luz de um modulo de rele: a de energia (o VCC) ou a do canal (a corrente no LED dele).</summary>
    bool LuzDoModuloDeRele(PecaNoCircuito peca, string chave)
    {
        var conta = EnergiaDoQuadro;
        if (chave == "vcc")
            return conta is not null ? conta.CorrenteDoRamo(peca.Id, "energia") > 0.0005
                                     : ultimaRede is { } r && r.PinosDePlacaNoMesmoNo(Atual, peca.Id, "VCC").Any(Netlist.EhPinoDeAlimentacao);
        if (!chave.StartsWith("canal", StringComparison.Ordinal) || !int.TryParse(chave[5..], out int k)) return false;
        if (conta is not null) return Math.Abs(conta.CorrenteDoRamo(peca.Id, $"entrada{k}")) > 0.0001;
        return ultimaRede is { } rede && CanalDoModuloArmado(peca, k, ultimaMaquinaDoRele.GetValueOrDefault(peca.Id), rede);
    }

    /// <summary>A rede e a maquina do ultimo quadro, para acender as luzes sem a conta de energia.</summary>
    Netlist ultimaRede;
    readonly Dictionary<string, Maquina> ultimaMaquinaDoRele = [];

    /// <summary>Os avisos: o VCC alto demais queima a bobina; baixo demais, o rele nao arma.</summary>
    IEnumerable<Estouro> AvisosDoModuloDeRele(PecaNoCircuito peca, Energia conta)
    {
        double vcc = conta.TensaoDoPino(peca.Id, "VCC") - conta.TensaoDoPino(peca.Id, "GND");
        string nome = Idioma.T("módulo de relé de {0} canal(is)", ModulosDeRele.Canais(peca.Tipo));
        if (vcc > ModulosDeRele.TensaoQueQueima)
        {
            yield return new Estouro(peca.Id, Idioma.T("o {0} recebeu {1:0.#} V no VCC: a bobina dos relés é de 5 V e queimou.", nome, vcc));
            yield break;
        }
        for (int k = 1; k <= ModulosDeRele.Canais(peca.Tipo); k++)
        {
            bool comandado = Math.Abs(conta.CorrenteDoRamo(peca.Id, $"entrada{k}")) > 0.0001;
            if (comandado && vcc < ModulosDeRele.TensaoQueArma)
            {
                yield return new Estouro(peca.Id, Idioma.T(
                    "o LED do canal {0} acende, mas o relé não arma: o VCC está com {1:0.#} V e a bobina de 5 V precisa de pelo menos 3,75 V. Ligue o VCC no 5V (ou no VIN) da placa.", k, vcc),
                    Queimou: false);
                yield break;
            }
        }
    }

    /// <summary>Para o teste: o canal (1..4) do modulo esta armado agora?</summary>
    internal bool CanalDoModuloParaTeste(string idDaPeca, int canal)
        => Atual.Achar(idDaPeca) is { } peca && ultimaRede is { } rede
           && CanalDoModuloArmado(peca, canal, ultimaMaquinaDoRele.GetValueOrDefault(peca.Id), rede);
}
