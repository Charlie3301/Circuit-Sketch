namespace Simulador.App;

public enum NivelDeEnergia { Nenhuma, Pouca, Aceitavel, Demais }

public sealed partial class Energia
{
    public const double SemCorrente = 0.0005;

    readonly Dictionary<string, int> indiceDoNo = [];
    readonly List<string> nos = [];
    readonly Dictionary<string, double> tensaoPorNo = [];
    readonly Dictionary<string, double> correntePorPeca = [];
    readonly Dictionary<string, double> correntePorNo = [];
    readonly Dictionary<string, NivelDeEnergia> nivelPorPeca = [];

    public IReadOnlyDictionary<string, double> Tensoes => tensaoPorNo;

    internal string Diagnostico()
        => $"{nos.Count} nos, {fontes.Count} fontes, {ramos.Count} ramos: "
           + string.Join(" | ", fontes.Select(f => $"no{f.No}={f.Tensao:0.0}V/{f.Resistencia:0.##}"))
           + " || " + string.Join(" | ", ramos.Select(r => $"{r.A}-{r.B} {r.Resistencia:0}ohm"));
    public IReadOnlyDictionary<string, double> CorrentesDePeca => correntePorPeca;

    public double TensaoDoNo(string raiz)
        => raiz is not null && tensaoPorNo.TryGetValue(raiz, out double v) ? v : 0;

    public double CorrenteDoNo(string raiz)
        => raiz is not null && correntePorNo.TryGetValue(raiz, out double i) ? i : 0;

    public double CorrenteDaPeca(string id)
        => id is not null && correntePorPeca.TryGetValue(id, out double i) ? i : 0;

    public NivelDeEnergia NivelDaPeca(string id)
        => id is not null && nivelPorPeca.TryGetValue(id, out var n) ? n : NivelDeEnergia.Nenhuma;

    public NivelDeEnergia NivelDoNo(string raiz)
    {
        double corrente = CorrenteDoNo(raiz);
        if (corrente < SemCorrente) return NivelDeEnergia.Nenhuma;
        if (corrente > 0.5) return NivelDeEnergia.Demais;
        if (corrente < 0.003) return NivelDeEnergia.Pouca;
        return NivelDeEnergia.Aceitavel;
    }

    /// <param name="Fem">
    /// Forca contra-eletromotriz, em volts, contra a corrente de A para B: a
    /// do motor girando. Ao contrario da Queda (o diodo), ela vale nos dois
    /// sentidos -- o motor girando para tras, ou freando, devolve corrente.
    /// </param>
    /// <param name="Rotulo">Nome do ramo dentro da peca (a fase do brushless), para ler a corrente.</param>
    sealed record Ramo(string Peca, int A, int B, double Resistencia, double Queda,
                       double Ideal, double Maximo, double Fem = 0, string Rotulo = null);

    sealed record Fonte(int No, double Tensao, double Resistencia);

    /// <summary>O nivel de cada saida das pecas de logica nesta conta (Logica.Saidas).</summary>
    IReadOnlyDictionary<(string Peca, string Pino), int> saidasLogicas;

    readonly List<Ramo> ramos = [];
    readonly List<Fonte> fontes = [];
    readonly HashSet<string> resistores = [];
    readonly Dictionary<(string Peca, string Rotulo), double> correnteDoRamo = [];

    Netlist rede;
    Circuito circuitoDaConta;
    IReadOnlyDictionary<string, double> forcas;

    /// <summary>A resistencia de agora das pecas que mudam de consumo (o servo parado ou girando, o ESC), em ohms.</summary>
    IReadOnlyDictionary<string, double> cargas;

    /// <summary>
    /// Um contato fechado agora: o botao apertado, a chave deslizante, o
    /// contato do rele. Dinamico -- muda a cada quadro --, entao nao entra no
    /// netlist: vira um ramo de 20 mOhm. Maximo e a corrente que o contato
    /// aguenta (o botao de 6 mm e de 50 mA: um motor por ele passa do ponto).
    /// </summary>
    public readonly record struct Contato(string Peca, string A, string B, double Maximo);

    public static Energia Calcular(Circuito circuito, Bancada bancada)
        => Calcular(circuito, bancada, null, null, null);

    /// <param name="semResistor">
    /// O netlist com resistor NAO conduzindo, ja montado (ele so muda com o
    /// circuito; a conta, a cada quadro). Nulo: monta aqui.
    /// </param>
    /// <param name="contatos">Os contatos fechados agora.</param>
    /// <param name="forcasDosMotores">A forca contra-eletromotriz de cada motor, pelo id da peca.</param>
    public static Energia Calcular(Circuito circuito, Bancada bancada, Netlist semResistor,
                                   IEnumerable<Contato> contatos,
                                   IReadOnlyDictionary<string, double> forcasDosMotores,
                                   IReadOnlyDictionary<string, double> cargasDinamicas = null,
                                   IReadOnlySet<string> placasSemCabo = null,
                                   IReadOnlySet<string> reguladoresDesligados = null,
                                   MemoriaDaEnergia memoria = null, double passo = 0,
                                   IReadOnlyDictionary<(string Peca, string Pino), int> saidasLogicas = null)
    {
        if (circuito is null) return new Energia();
        var rede = semResistor ?? new Netlist(circuito, resistorConduz: false);

        // Com PWM ligado em alguma coisa: uma conta por trecho do periodo, e a
        // media (EnergiaPwm.cs). A carga do capacitor anda uma vez, pela media.
        var emPwm = PinosEmPwm(circuito, bancada, rede);
        if (emPwm.Count > 0)
        {
            var contatosFixos = contatos?.ToList();
            var trechos = TrechosDoPeriodo(emPwm)
                .Select(t => (Um(circuito, bancada, rede, contatosFixos, forcasDosMotores, cargasDinamicas, placasSemCabo,
                                 reguladoresDesligados, memoria, passo, t.Altos, lembrar: false, saidasLogicas), t.Peso))
                .ToList();
            var media = Media(trechos);
            media.Lembrar(circuito);
            return media;
        }
        return Um(circuito, bancada, rede, contatos, forcasDosMotores, cargasDinamicas, placasSemCabo,
                  reguladoresDesligados, memoria, passo, null, lembrar: true, saidasLogicas);
    }

    /// <summary>Uma conta, com o PWM de cada pino em HIGH ou em LOW (ou na media, sem trechos).</summary>
    static Energia Um(Circuito circuito, Bancada bancada, Netlist rede, IEnumerable<Contato> contatos,
                      IReadOnlyDictionary<string, double> forcasDosMotores, IReadOnlyDictionary<string, double> cargasDinamicas,
                      IReadOnlySet<string> placasSemCabo, IReadOnlySet<string> reguladoresDesligados,
                      MemoriaDaEnergia memoria, double passo, IReadOnlyDictionary<(string, int), bool> pwmNoTrecho, bool lembrar,
                      IReadOnlyDictionary<(string Peca, string Pino), int> saidasLogicas)
    {
        var conta = new Energia();
        conta.saidasLogicas = saidasLogicas;
        conta.pwmNoTrecho = pwmNoTrecho;
        conta.memoria = memoria;
        conta.passo = passo;
        conta.placasSemCabo = placasSemCabo;
        conta.reguladoresDesligados = reguladoresDesligados;

        conta.rede = rede;
        conta.circuitoDaConta = circuito;
        conta.bancada = bancada;
        conta.forcas = forcasDosMotores;
        conta.cargas = cargasDinamicas;
        conta.Montar(circuito, conta.rede, bancada);
        if (contatos is not null) conta.Fechar(contatos);

        // Com ponte H no circuito, tres passadas: o que alimenta (sem as
        // pontes), os reguladores que isso liga, e entao as entradas e a
        // logica de cada driver -- que decidem as saidas da passada final.
        // Com placa sem o cabo USB, uma passada a mais depois dos reguladores:
        // e ai que se ve se a bateria (direto, ou pelo 5 V do L298N, pelo BEC
        // do ESC) chega na entrada dela -- e ela liga, puxa a corrente dela e
        // poe o 3V3 de pe antes da logica dos drivers.
        var drivers = circuito.Pecas.Where(x => DriverDeMotor.De(x.Tipo) is not null || x.Tipo == "esc").ToList();
        bool comPlacaSemCabo = placasSemCabo is { Count: > 0 } && circuito.Pecas.Any(x => placasSemCabo.Contains(x.Id));
        if (drivers.Count > 0 || comPlacaSemCabo)
        {
            if (comPlacaSemCabo) conta.CargasDeMedida();
            conta.Resolver();
            conta.LigarReguladores(drivers);
            conta.Resolver();
            if (comPlacaSemCabo)
            {
                conta.LigarPlacas();
                conta.Resolver();
            }
            if (drivers.Count > 0) conta.LigarPontes(drivers);
        }

        conta.Resolver();
        conta.Medir();
        if (lembrar) conta.Lembrar(circuito);
        return conta;
    }

    Bancada bancada;

    // =====================================================================
    //  as pontes H (DriversDeMotor)
    // =====================================================================

    /// <summary>
    /// Uma saida de driver, na media do PWM: ela vale K x a alimentacao mais
    /// (1 - K) x o terra do driver, atras da resistencia das chaves, menos a
    /// queda de saturacao (contra a corrente). E um transformador ideal: a
    /// corrente que sai pela saida vem K da alimentacao e (1 - K) do terra --
    /// o motor a meia velocidade puxa metade da corrente da bateria, como no
    /// PWM de verdade, e sem a "corrente de curto" de um modelo de resistores.
    /// </summary>
    sealed record MeiaPonte(string Peca, string Rotulo, int Saida, int Potencia, int Terra, double K,
                            double Resistencia, double QuedaAlta, double QuedaBaixa, double Maximo,
                            double Conducao = 1)
    {
        /// <summary>
        /// A queda media: a chave de cima conduz K do tempo, a de baixo o resto
        /// do tempo em que a saida esta ligada (Conducao - K); desligada, nada.
        /// </summary>
        public double Queda => K * QuedaAlta + Math.Max(0, Conducao - K) * QuedaBaixa;
    }

    readonly List<MeiaPonte> meiasPontes = [];

    /// <summary>
    /// O que os drivers disseram nesta conta: sem logica, sem GND em comum,
    /// tensao fora da faixa. O editor mostra como aviso (ou queima).
    /// </summary>
    public List<Estouro> AvisosDosDrivers { get; } = [];

    /// <summary>Driver com a logica alimentada nesta conta (a luz do modulo acende).</summary>
    public HashSet<string> DriversLigados { get; } = [];

    /// <summary>A fracao K de cada saida de driver ligada (nulo: desligada).</summary>
    public double? KDaSaida(string peca, string saida)
        => meiasPontes.FirstOrDefault(m => m.Peca == peca && m.Rotulo == saida) is { } m ? m.K : null;

    double TensaoDoIndice(int no) => tensoes is not null && no >= 0 && no < tensoes.Length ? tensoes[no] : 0;

    double TensaoEntre(string peca, string pinoA, string pinoB)
    {
        int a = No(rede, peca, pinoA), b = No(rede, peca, pinoB);
        return TensaoDoIndice(a) - TensaoDoIndice(b);
    }

    /// <summary>
    /// O regulador 78M05 do L298N: com o jumper (sempre, no modulo) e 7 V ou
    /// mais na entrada de 12 V, o pino de 5 V vira SAIDA de 5 V -- e da para
    /// alimentar a placa por ele. Abaixo de 7 V o regulador nao regula, e a
    /// logica do L298N precisa de 5 V de fora nesse pino.
    /// </summary>
    void LigarReguladores(List<PecaNoCircuito> drivers)
    {
        foreach (var peca in drivers)
        {
            // O BEC do ESC: 5 V no fio vermelho do cabo de servo, com bateria.
            if (peca.Tipo == "esc")
            {
                if (TensaoEntre(peca.Id, "BAT+", "BAT-") < 5.5) continue;
                int saida5 = No(rede, peca.Id, "5V"), gnd = No(rede, peca.Id, "GND");
                if (saida5 >= 0 && gnd >= 0)
                    ramos.Add(new Ramo(peca.Id, saida5, gnd, 0.1, 0, 0, 2.0, Fem: 5.0, Rotulo: "bec"));
                continue;
            }
            if (peca.Tipo != "l298n") continue;
            if (TensaoEntre(peca.Id, "12V", "GND") < 7) continue;
            int cinco = No(rede, peca.Id, "5V"), terra = No(rede, peca.Id, "GND");
            if (cinco < 0 || terra < 0) continue;
            ramos.Add(new Ramo(peca.Id, cinco, terra, 0.5, 0, 0, 0.5, Fem: 5.0, Rotulo: "regulador"));
        }
    }

    /// <summary>Os nos que tem alguma coisa ligada (fonte ou ramo): o resto esta solto.</summary>
    HashSet<int> NosComLigacao()
    {
        var com = new HashSet<int>();
        foreach (var f in fontes) com.Add(f.No);
        foreach (var r in ramos) { com.Add(r.A); com.Add(r.B); }
        return com;
    }

    void LigarPontes(List<PecaNoCircuito> drivers)
    {
        var ligados = NosComLigacao();
        foreach (var peca in drivers)
        {
            var def = DriverDeMotor.De(peca.Tipo);
            if (def is null) continue;
            int potencia = No(rede, peca.Id, def.Potencia), terra = No(rede, peca.Id, def.Terra);
            if (potencia < 0 || terra < 0) continue;

            double vPotencia = TensaoDoIndice(potencia) - TensaoDoIndice(terra);
            double vLogica = def.Logica is null ? vPotencia : TensaoEntre(peca.Id, def.Logica, def.Terra);

            // A logica vem de um trilho da placa, mas o GND do driver nao e o
            // da placa: a corrente da logica nao tem por onde voltar.
            if (def.Logica is not null && vLogica < def.LogicaMinima
                && LigadoEmTrilhoDePlaca(peca.Id, def.Logica) && !GndDePlaca(peca.Id, def.Terra))
            {
                AvisosDosDrivers.Add(new Estouro(peca.Id,
                    $"o {def.Nome} tira a logica ({def.Logica}) da placa, mas o GND dele nao esta ligado no GND da placa: "
                    + "a corrente nao tem por onde voltar, e a logica fica sem energia. Ligue os dois GND.",
                    Queimou: false));
                continue;
            }

            if (vPotencia > def.PotenciaMaxima * 1.1)
            {
                AvisosDosDrivers.Add(new Estouro(peca.Id,
                    $"o {def.Nome} recebeu {vPotencia:0.#} V no {def.Potencia}, e o maximo dele e {def.PotenciaMaxima:0.#} V: queimou."));
                continue;
            }
            if (vLogica < def.LogicaMinima)
            {
                // So reclama quando ha energia no motor: sem nada ligado, nao ha o que avisar.
                if (vPotencia > 1 && def.Logica is not null)
                    AvisosDosDrivers.Add(new Estouro(peca.Id, peca.Tipo == "l298n"
                        ? $"o L298N esta com {vPotencia:0.#} V no 12V, e abaixo de 7 V o regulador dele nao faz os 5 V da logica: "
                          + "ligue os 5 V da placa no pino 5V do modulo (com o GND em comum)."
                        : $"o {def.Nome} esta sem a alimentacao logica: o pino {def.Logica} precisa de pelo menos "
                          + $"{def.LogicaMinima:0.#} V ("
                          + (def.LogicaMinima > 3.3 ? "os 5 V da placa, no VIN -- os 3,3 V nao bastam" : "os 3,3 V ou os 5 V da placa")
                          + ").",
                        Queimou: false));
                continue;
            }
            if (vPotencia < def.PotenciaMinima)
            {
                if (vPotencia > 0.5)
                    AvisosDosDrivers.Add(new Estouro(peca.Id,
                        $"o {def.Nome} precisa de pelo menos {def.PotenciaMinima:0.#} V no {def.Potencia} e esta com {vPotencia:0.#} V: as saidas nao ligam.",
                        Queimou: false));
                continue;
            }

            DriversLigados.Add(peca.Id);
            bool semGndComum = false;
            double Entrada(string pino) => NivelDaEntrada(peca, pino, terra, ligados, ref semGndComum);

            foreach (var (saida, k) in def.Saidas(Entrada).ToList())
            {
                if (k is not { } fracao) continue;
                int no = No(rede, peca.Id, saida);
                if (no < 0) continue;
                double conducao = Math.Max(def.ConducaoDe(Entrada, saida), Math.Clamp(fracao, 0, 1));
                meiasPontes.Add(new MeiaPonte(peca.Id, saida, no, potencia, terra, Math.Clamp(fracao, 0, 1),
                                              def.Resistencia, def.QuedaAlta, def.QuedaBaixa, def.CorrenteMaxima,
                                              conducao));
            }

            if (semGndComum)
                AvisosDosDrivers.Add(new Estouro(peca.Id,
                    $"o {def.Nome} recebe os sinais da placa, mas o GND dele nao esta ligado no GND da placa: "
                    + "sem a referencia em comum, ele nao entende o HIGH e o LOW. Ligue os dois GND.",
                    Queimou: false));
        }
    }

    /// <summary>Este pino da peca esta no 3V3/5V/VIN de uma placa?</summary>
    bool LigadoEmTrilhoDePlaca(string peca, string pino)
    {
        string raiz = rede.RaizDe(Netlist.No(peca, pino));
        return raiz is not null && circuitoDaConta.Pecas.Any(p => PerfilPlaca.EhPlaca(p.Tipo)
            && Catalogo.Obter(p.Tipo) is { } d
            && d.Pinos.Any(x => Netlist.EhPinoDeAlimentacao(x.Nome) && rede.RaizDe(Netlist.No(p.Id, x.Nome)) == raiz));
    }

    /// <summary>Este pino da peca esta no GND de alguma placa?</summary>
    bool GndDePlaca(string peca, string pino)
    {
        string raiz = rede.RaizDe(Netlist.No(peca, pino));
        return raiz is not null && circuitoDaConta.Pecas.Any(p => PerfilPlaca.EhPlaca(p.Tipo)
            && Catalogo.Obter(p.Tipo) is { } d
            && d.Pinos.Any(x => Netlist.EhPinoDeTerra(x.Nome) && rede.RaizDe(Netlist.No(p.Id, x.Nome)) == raiz));
    }

    /// <summary>
    /// O nivel de uma entrada de driver, de 0 a 1 (a fracao do tempo em
    /// HIGH), ou <see cref="DriverDeMotor.Solta"/>. Um GPIO de saida no mesmo
    /// no manda direto, com o PWM dele -- desde que o GND da placa seja o
    /// mesmo do driver. Sem GPIO, vale a tensao no pino: 2 V acima do GND do
    /// driver e HIGH (o limiar TTL dos datasheets).
    /// </summary>
    double NivelDaEntrada(PecaNoCircuito driver, string pino, int terra, HashSet<int> ligados, ref bool semGndComum)
    {
        string raiz = rede.RaizDe(Netlist.No(driver.Id, pino));
        if (raiz is null) return DriverDeMotor.Solta;

        foreach (var placa in circuitoDaConta.Pecas)
        {
            var perfil = PerfilPlaca.De(placa.Tipo);
            var maq = perfil is null ? null : bancada?.De(placa.Id);
            if (maq is null || !maq.Energizada) continue;
            var defPlaca = Catalogo.Obter(placa.Tipo);
            foreach (var p in defPlaca.Pinos)
            {
                int gpio = perfil.Numero(p.Nome);
                if (gpio < 0 || gpio >= Maquina.NumPinos || maq.Modo[gpio] != 1) continue;
                if (rede.RaizDe(Netlist.No(placa.Id, p.Nome)) != raiz) continue;

                bool gndComum = defPlaca.Pinos.Any(g => Netlist.EhPinoDeTerra(g.Nome)
                    && rede.RaizDe(Netlist.No(placa.Id, g.Nome)) == rede.RaizDe(Netlist.No(driver.Id, DriverDeMotor.De(driver.Tipo).Terra)));
                if (!gndComum) { semGndComum = true; return 0; }
                return Math.Clamp(maq.Pwm[gpio] / 255.0, 0, 1);
            }
        }

        if (!indiceDoNo.TryGetValue(raiz, out int no) || !ligados.Contains(no)) return DriverDeMotor.Solta;
        return TensaoDoIndice(no) - TensaoDoIndice(terra) >= 2.0 ? 1 : 0;
    }


    void Fechar(IEnumerable<Contato> contatos)
    {
        foreach (var c in contatos)
        {
            int a = No(rede, c.Peca, c.A), b = No(rede, c.Peca, c.B);
            if (a < 0 || b < 0 || a == b) continue;
            ramos.Add(new Ramo(c.Peca, a, b, 0.02, 0, 0, c.Maximo));
        }
    }

    /// <summary>
    /// A corrente que a bateria entrega agora, em amperes (positiva: saindo
    /// pelo +, descarregando; negativa: alguem a esta carregando).
    /// </summary>
    public double CorrenteDaBateria(string peca) => -CorrenteDoRamo(peca, "bateria");

    /// <summary>A tensao do no deste pino, em volts (0: pino solto ou fora da conta).</summary>
    public double TensaoDoPino(string peca, string pino)
        => TensaoDoNo(rede?.RaizDe(Netlist.No(peca, pino)));

    /// <summary>
    /// A corrente de um ramo com nome (a fase "A" do brushless, o "motor"),
    /// com sinal: positiva do primeiro pino para o segundo.
    /// </summary>
    public double CorrenteDoRamo(string peca, string rotulo)
        => correnteDoRamo.TryGetValue((peca, rotulo), out double i) ? i : 0;

    public NivelDeEnergia NivelDoPino(string peca, string pino)
        => NivelDoNo(rede?.RaizDe(Netlist.No(peca, pino)));

    public double CorrenteDoPino(string peca, string pino)
        => CorrenteDoNo(rede?.RaizDe(Netlist.No(peca, pino)));

    int No(Netlist rede, string peca, string pino)
    {
        string raiz = rede.RaizDe(Netlist.No(peca, pino));
        if (raiz is null) return -1;

        if (indiceDoNo.TryGetValue(raiz, out int i)) return i;

        indiceDoNo[raiz] = nos.Count;
        nos.Add(raiz);
        return nos.Count - 1;
    }

    /// <summary>Um no que nao esta no netlist: o centro da estrela das fases do brushless.</summary>
    int NoInterno(string chave)
    {
        if (indiceDoNo.TryGetValue(chave, out int i)) return i;
        indiceDoNo[chave] = nos.Count;
        nos.Add(chave);
        return nos.Count - 1;
    }

    void Montar(Circuito circuito, Netlist rede, Bancada bancada)
    {
        var perfilDoCircuito = Netlist.PlacaDoCircuito(circuito);
        bool energizada = bancada?.Todas.Any(x => x.Value.Energizada) ?? false;

        // Duas passadas: as fontes de todas as pecas antes dos ramos. O LED
        // RGB precisa saber se o comum dele esta no positivo (anodo comum)
        // para pôr os diodos no sentido certo, e a bateria pode estar depois
        // dele na lista.
        foreach (var peca in circuito.Pecas)
        {
            var def = Catalogo.Obter(peca.Tipo);
            if (def is null) continue;

            var perfil = PerfilPlaca.De(peca.Tipo);
            bool ehPlaca = perfil is not null;
            bool ehFonte = Netlist.EhFonte(peca.Tipo);
            var maq = ehPlaca ? bancada?.De(peca.Id) : null;

            // A bateria e um ramo (abaixo), nao uma fonte presa ao terra.
            if (Netlist.EhBateria(peca.Tipo)) continue;

            // O no de cada pino nasce so quando uma fonte ou um ramo chega
            // nele. Antes todo pino virava no -- a protoboard de 830 furos
            // punha centenas de nos soltos na matriz, e com motor no circuito
            // a conta roda a cada quadro. Um no solto nao muda a resposta
            // de ninguem (ele so tem o vazamento de 1e-9 para o terra).
            foreach (var pino in def.Pinos)
            {
                void Fornecer(double tensao, double resistencia)
                {
                    int no = No(rede, peca.Id, pino.Nome);
                    if (no >= 0) fontes.Add(new Fonte(no, tensao, resistencia));
                }

                if (Netlist.EhPinoDeTerra(pino.Nome) && ehFonte)
                {
                    Fornecer(0, 0.05);
                    continue;
                }

                // A placa sem o cabo USB nao tem trilho ideal: o 3V3 e o 5V dela
                // so existem se a bateria a alimentar (LigarPlacas).
                if (Netlist.EhPinoDeAlimentacao(pino.Nome) && ehFonte && energizada && !(ehPlaca && SemCabo(peca.Id)))
                {
                    // Na USB, o trilho da placa so FORNECE: o VIN vem por um
                    // diodo, e o regulador do 3V3 nao absorve corrente. A bateria
                    // de fora acima dele nao "entra" na USB como num curto -- ela
                    // e que alimenta o pino (e, acima do que ele aguenta, queima).
                    double tensao = TensaoDoTrilho(pino.Nome, perfil ?? perfilDoCircuito);
                    int trilho = No(rede, peca.Id, pino.Nome);
                    int terra = ehPlaca && TerraDe(def) is { } gnd ? No(rede, peca.Id, gnd) : -1;
                    if (trilho >= 0 && terra >= 0 && trilho != terra)
                    {
                        ramos.Add(new Ramo(peca.Id + "/usb", terra, trilho, 0.05, QuedaDaUsb, 0, 0.5,
                                           Fem: -(tensao + QuedaDaUsb), Rotulo: pino.Nome));
                        trilhosDaUsb.Add(trilho);
                    }
                    else Fornecer(tensao, 0.05);
                    continue;
                }

                if (!ehPlaca || maq is null || !maq.Energizada) continue;

                int gpio = perfil.Numero(pino.Nome);
                if (gpio < 0 || gpio >= Maquina.NumPinos) continue;
                if (maq.Modo[gpio] != 1) continue;

                bool alto = maq.Pwm[gpio] > 0 || maq.Digital[gpio] == 1;
                double nivel = TensaoDoPwm(peca.Id, gpio, maq, perfil.TensaoDeOperacao)
                               ?? (alto ? perfil.TensaoDeOperacao * Math.Max(maq.Pwm[gpio], 1) / 255.0 : 0);
                Fornecer(alto ? nivel : 0, 30);
            }
        }

        // As saidas das portas ideais (Alpha 25): uma fonte no nivel da placa,
        // com a resistencia de um GPIO. As dos chips 74HC vao nos ramos (pelo
        // VCC e pelo GND do proprio chip).
        if (saidasLogicas is not null)
        {
            double alto = perfilDoCircuito?.TensaoDeOperacao ?? 5.0;
            foreach (var ((idPeca, pino), nivel) in saidasLogicas)
            {
                if (circuito.Achar(idPeca) is not { } logica || Logica.EhChip(logica.Tipo)) continue;
                int no = No(rede, idPeca, pino);
                if (no >= 0) fontes.Add(new Fonte(no, nivel == 1 ? alto : 0, 25));
            }
        }

        foreach (var peca in circuito.Pecas)
        {
            var def = Catalogo.Obter(peca.Tipo);
            if (def is null) continue;

            if (peca.Tipo is "resistor" or "ldr") resistores.Add(peca.Id);
            foreach (var ramo in RamosDaPeca(peca, def, rede))
                ramos.Add(ramo);
        }

        // Os reguladores de tensao: a entrada e a saida mudam com a propria resposta (o Resolver os acompanha).
        MontarConversores(circuito, rede);
        // Transistor, MOSFET e opto: o canal acompanha a base, o gate, o LED.
        MontarChaves(circuito, rede);
        // Os modulos de rele: a entrada (o LED do opto) liga a bobina.
        MontarModulosDeRele(circuito, rede);
    }

    /// <summary>O no tem uma fonte positiva presa nele (o trilho de uma placa ligada)?</summary>
    bool TemFontePositiva(int no) => no >= 0 && (trilhosDaUsb.Contains(no) || fontes.Any(f => f.No == no && f.Tensao > 0.5));

    /// <summary>Os nos dos trilhos das placas na USB (o 3V3, o 5V, o VIN).</summary>
    readonly HashSet<int> trilhosDaUsb = [];

    /// <summary>
    /// O "diodo" do trilho da USB: so o bastante para o ramo conduzir num
    /// sentido so. A tensao do trilho continua a de sempre (a Fem compensa).
    /// </summary>
    const double QuedaDaUsb = 0.001;

    /// <summary>
    /// O no esta no "+" de uma bateria? Olha so os fios (o netlist), sem
    /// a conta: e o que decide o sentido do LED RGB de anodo comum.
    /// </summary>
    bool NoPositivoDeBateria(Circuito circuito, string peca, string pino)
    {
        string alvo = rede.RaizDe(Netlist.No(peca, pino));
        if (alvo is null || circuito is null) return false;
        foreach (var bateria in circuito.Pecas)
            if (Netlist.EhBateria(bateria.Tipo) && rede.RaizDe(Netlist.No(bateria.Id, "+")) == alvo)
                return true;
        return false;
    }

    static double TensaoDoTrilho(string nome, PerfilPlaca perfil)
    {
        int ponto = nome.LastIndexOf('.');
        string limpo = ponto > 0 && nome[(ponto + 1)..].All(char.IsAsciiDigit)
            ? nome[..ponto] : nome;

        return limpo switch
        {
            "3V3" or "3.3V" => 3.3,
            "5V" or "VIN" or "VBUS" or "VUSB" or "V+" => 5.0,
            _ => perfil?.TensaoDeOperacao ?? 3.3,
        };
    }

    IEnumerable<Ramo> RamosDaPeca(PecaNoCircuito peca, PecaDef def, Netlist rede)
    {
        int Liga(string pino) => No(rede, peca.Id, pino);

        Ramo Entre(string a, string b, double resistencia, double queda,
                   double ideal, double maximo)
        {
            int na = Liga(a), nb = Liga(b);
            return na < 0 || nb < 0 ? null
                : new Ramo(peca.Id, na, nb, Math.Max(resistencia, 0.001), queda, ideal, maximo);
        }

        // A saida do chip 74HC e um transistor do proprio chip: em HIGH ela liga
        // o pino ao VCC dele, em LOW ao GND, por uns 50 ohms (a ficha, em 4,5 V).
        // Passar de 35 mA por saida estoura o chip (o maximo absoluto e 25 mA).
        if (Logica.EhChip(peca.Tipo) && saidasLogicas is not null)
            foreach (var pino in def.Pinos)
            {
                if (!saidasLogicas.TryGetValue((peca.Id, pino.Nome), out int nivel)) continue;
                var saida = nivel == 1 ? Entre("VCC", pino.Nome, 50, 0, 0.004, 0.035) : Entre(pino.Nome, "GND", 50, 0, 0.004, 0.035);
                if (saida is not null) yield return saida with { Rotulo = pino.Nome };
            }

        // Todas as baterias -- a personalizada, as prontas e os suportes de
        // pilhas -- sao o mesmo ramo; o suporte com um slot vazio nao liga nada.
        if (Netlist.EhBateria(peca.Tipo))
        {
            var ficha = Baterias.FichaDe(peca);
            if (ficha.Aberta) yield break;
            double maxima = Math.Max(1, ficha.MiliAmperes) / 1000.0;
            var r = Entre("+", "-", ResistenciaDaBateria(ficha), 0, maxima * 0.05, maxima);
            if (r is not null) yield return r with { Fem = ficha.Volts, Rotulo = "bateria" };
            yield break;
        }

        switch (peca.Tipo)
        {
            // A bateria e uma fonte de tensao FLUTUANTE: a forca
            // eletromotriz dela em serie com a resistencia interna, entre o +
            // e o -. Antes o "-" era preso ao terra da conta, e dois erros
            // saiam disso: o LED ligado so no "+" (sem fechar o circuito)
            // acendia, e duas baterias em serie brigavam em vez de somar.
            // Ramo do + para o -: descarregando, a corrente dele e negativa.
            // (O ramo de todas elas sai logo acima, antes do switch.)

            case "resistor":
            {
                double ohms = peca.Ohms <= 0 ? 220 : peca.Ohms;
                var r = Entre("1", "2", ohms, 0, 0.0008, Math.Sqrt(0.25 / ohms));
                if (r is not null) yield return r;
                yield break;
            }

            // Montados a parte (os indices dos ramos ficam fixos): os
            // reguladores e os semicondutores controlados.
            case "regulador-7805":
            case "ams1117":
            case "lm2596":
            case "mt3608":
            case var semicondutor when Semicondutores.EhControlado(semicondutor):
            case "modulo-rele-1":
            case "modulo-rele-2":
            case "modulo-rele-4":
                yield break;

            // O fusivel e um fio fino; queimado (o editor desliga), aberto.
            case "fusivel":
            {
                if (reguladoresDesligados?.Contains(peca.Id) == true) yield break;
                var r = Entre("1", "2", 0.05, 0, 0.01, Semicondutores.Protecao(peca).Valor);
                if (r is not null) yield return r with { Rotulo = "fusivel" };
                yield break;
            }

            // O varistor: aberto ate a tensao dele, depois segura (nos dois sentidos).
            case "varistor":
            {
                double v = Semicondutores.Protecao(peca).Valor;
                if (Entre("1", "2", 2.0, v, 0.001, 5) is { } ida) yield return ida with { Rotulo = "varistor" };
                if (Entre("2", "1", 2.0, v, 0.001, 5) is { } volta) yield return volta with { Rotulo = "varistor-volta" };
                yield break;
            }

            // O LDR: um resistor que muda com a luz (Comportamento.ResistenciaDoLdr).
            // Aguenta ~100 mW: passar de uns 20 mA ja o esquenta demais.
            case "ldr":
            {
                var r = Entre("1", "2", Comportamento.ResistenciaDoLdr(peca.Giro), 0, 0.0001, 0.02);
                if (r is not null) yield return r with { Rotulo = "ldr" };
                yield break;
            }

            // O capacitor e o indutor dependem do quadro anterior (EnergiaTransitoria.cs).
            case "capacitor":
            case "capacitor-eletrolitico":
            case "indutor":
            case "cristal":
                foreach (var r in RamosQueGuardamEnergia(peca, rede)) yield return r;
                yield break;

            // Os diodos: a queda direta e a resistencia que sobra; o zener tem
            // o segundo ramo, ao contrario, a partir da tensao dele.
            case "diodo":
            case "diodo-zener":
            case "diodo-schottky":
            {
                var ficha = Diodos.Da(peca);
                var direto = Entre("A", "C", ficha.Resistencia, ficha.Queda, 0.001, ficha.CorrenteMaxima);
                if (direto is not null) yield return direto with { Rotulo = "diodo" };
                if (ficha.Zener > 0 && Entre("C", "A", ficha.ResistenciaZener, ficha.Zener, 0.001, ficha.Potencia / ficha.Zener) is { } zener)
                    yield return zener with { Rotulo = "zener" };
                yield break;
            }

            case "led":
            {
                var r = Entre("A", "C", 18, 2.0, 0.012, 0.030);
                if (r is not null) yield return r with { Rotulo = "led" };
                yield break;
            }

            // O semaforo: cada LED com o resistor dele (330, 220 e 470 ohms), o catodo no GND.
            case "modulo-semaforo":
            {
                foreach (var (pino, ohms, queda) in new[] { ("R", 330.0, 1.9), ("Y", 220.0, 2.0), ("G", 470.0, 2.2) })
                {
                    var r = Entre(pino, "GND", ohms, queda, 0.009, 0.030);
                    if (r is not null) yield return r with { Rotulo = pino };
                }
                yield break;
            }

            // O comum no positivo (uma placa ligada, ou o + de uma bateria)
            // e o RGB de anodo comum: os tres diodos vao do comum para a cor.
            case "rgb-led":
            {
                int comum = Liga("COM");
                bool anodoComum = TemFontePositiva(comum) || NoPositivoDeBateria(circuitoDaConta, peca.Id, "COM");
                foreach (string cor in new[] { "R", "G", "B" })
                {
                    var r = anodoComum ? Entre("COM", cor, 18, 2.0, 0.012, 0.030)
                                       : Entre(cor, "COM", 18, 2.0, 0.012, 0.030);
                    if (r is not null) yield return r with { Rotulo = cor };
                }
                yield break;
            }

            // O display de 7 segmentos: oito LEDs (A a G e o ponto) com o comum;
            // como no RGB, o comum no positivo e o de anodo comum.
            case "7segment":
            {
                int comum = Liga("COM.1");
                bool anodoComum = TemFontePositiva(comum) || NoPositivoDeBateria(circuitoDaConta, peca.Id, "COM.1");
                foreach (string segmento in new[] { "A", "B", "C", "D", "E", "F", "G", "DP" })
                {
                    var r = anodoComum ? Entre("COM.1", segmento, 18, 2.0, 0.012, 0.030)
                                       : Entre(segmento, "COM.1", 18, 2.0, 0.012, 0.030);
                    if (r is not null) yield return r with { Rotulo = segmento };
                }
                yield break;
            }

            case "led-bar-graph":
                for (int k = 1; k <= 10; k++)
                {
                    var r = Entre($"A{k}", $"C{k}", 18, 2.0, 0.012, 0.030);
                    if (r is not null) yield return r with { Rotulo = k.ToString() };
                }
                yield break;

            case "buzzer":
            {
                var r = Entre("1", "2", 90, 0, 0.030, 0.060);
                if (r is not null) yield return r;
                yield break;
            }

            // A bobina de 8 ohms, 0,5 W: passa de 250 mA e esquenta demais.
            case "alto-falante":
            {
                var r = Entre("+", "-", 8, 0, 0.100, 0.250);
                if (r is not null) yield return r;
                yield break;
            }

            // O MAX98357A parado gasta uns 2,4 mA; no volume alto passa das
            // centenas -- a corrente do alto-falante sai do VIN dele.
            case "max98357a":
            {
                var r = Entre("VIN", "GND", 2000, 0, 0.0024, 0.8);
                if (r is not null) yield return r;
                yield break;
            }

            // O PCM5102A (com o regulador de 3,3 V do modulo): ~20 mA tocando.
            case "pcm5102a":
            {
                var r = Entre("VIN", "GND", 220, 0, 0.02, 0.1);
                if (r is not null) yield return r;
                yield break;
            }

            // O servo: sem saber o que ele faz, os ~550 mA de quem gira; com o
            // editor dizendo (parado, segurando, girando), a corrente de agora.
            case "servo":
            case "servo-mg996r":
            case "servo-360":
            {
                var modelo = ModeloDeServo.De(peca.Tipo);
                double ohms = cargas is not null && cargas.TryGetValue(peca.Id, out double rc) && rc > 0 ? rc : 9;
                var r = Entre("V+", "GND", ohms, 0, modelo.CorrenteGirando, modelo.CorrenteTravado * 1.1);
                if (r is not null) yield return r;
                yield break;
            }

            case "stepper-motor":
            {
                foreach (var (a, b) in new[] { ("A+", "A-"), ("B+", "B-") })
                {
                    var r = Entre(a, b, 50, 0, 0.12, 0.35);
                    if (r is not null) yield return r;
                }
                yield break;
            }

            // O biaxial sao dois motores de passo: quatro bobinas.
            case "biaxial-stepper":
            {
                foreach (var (a, b) in new[] { ("A1+", "A1-"), ("B1+", "B1-"), ("A2+", "A2-"), ("B2+", "B2-") })
                {
                    var r = Entre(a, b, 50, 0, 0.12, 0.35);
                    if (r is not null) yield return r with { Rotulo = a[..2] };
                }
                yield break;
            }

            case "ks2e-m-dc5":
            {
                var r = Entre("COIL1", "COIL2", 70, 0, 0.07, 0.12);
                if (r is not null) yield return r;
                yield break;
            }

            // O motor de escovas: o enrolamento e a forca contra-eletromotriz
            // de agora. Parado, ele e so a resistencia -- a corrente de
            // partida, a maior de todas; girando, a forca contra-eletromotriz
            // come quase toda a tensao e sobra a corrente de vazio.
            case "motor-dc":
            case "motor-tt":
            {
                var modelo = ModeloDeMotor.Da(peca);
                double fem = forcas is not null && forcas.TryGetValue(peca.Id, out double f) ? f : 0;
                // Ideal: a corrente de vazio; demais: a do eixo travado na
                // tensao nominal (passar disso, so com tensao acima dela).
                double vazio = modelo.AtritoSeco / modelo.Kt;
                double travado = modelo.CorrenteTravado(modelo.TensaoNominal) * 1.05;
                var r = Entre("+", "-", modelo.Resistencia, 0, vazio, travado);
                if (r is not null) yield return r with { Fem = fem, Rotulo = "motor" };
                yield break;
            }

            // O ESC: na bateria, a corrente que ele puxa agora (o editor diz,
            // pelo acelerador e pelo motor). O BEC entra depois, com a bateria.
            case "esc":
            case "esc-4em1":
            {
                if (cargas is not null && cargas.TryGetValue(peca.Id, out double rc) && rc > 0)
                {
                    var carga = Entre("BAT+", "BAT-", rc, 0, 0.5, 250);
                    if (carga is not null) yield return carga with { Rotulo = "bateria" };
                }
                yield break;
            }

            // O 28BYJ-48: quatro bobinas de 50 ohms do comum (vermelho) para
            // cada fio de fase. Quem fecha a bobina e o ULN2003, puxando a fase
            // para o GND: ~80 mA por fase ligada, com 5 V.
            case "motor-28byj48":
                foreach (string fase in new[] { "laranja", "amarelo", "rosa", "azul" })
                {
                    var r = Entre("vermelho", fase, 50, 0, 0.06, 0.2);
                    if (r is not null) yield return r with { Rotulo = fase };
                }
                yield break;

            // O A4988 e o DRV8825 seguram a corrente das bobinas no chopper:
            // da fonte sai a potencia que as bobinas gastam (~3 W num NEMA 17
            // a 1 A), como uma carga fixa no VMOT.
            case "a4988":
            case "drv8825":
            {
                var r = Entre("VMOT", "GND", 48, 0, 0.1, 2.0);
                if (r is not null) yield return r with { Rotulo = "vmot" };
                yield break;
            }

            // O brushless sao tres fases em estrela, com o centro dentro dele.
            case "motor-brushless":
            {
                int centro = NoInterno(peca.Id + ":*estrela");
                foreach (string fase in new[] { "A", "B", "C" })
                {
                    int no = Liga(fase);
                    if (no < 0) continue;
                    yield return new Ramo(peca.Id, no, centro, MotorSemEscova.ResistenciaDeFaseDe(ModeloDeMotor.Da(peca)),
                                          0, 0.5, 12, Rotulo: fase);
                }
                yield break;
            }

            case "pushbutton":
            case "pushbutton-6mm":
                yield break;

            case "potentiometer":
            case "slide-potentiometer":
            {
                var r = Entre("VCC", "GND", 10000, 0, 0.001, 0.01);
                if (r is not null) yield return r;
                yield break;
            }
        }

        var alimentacao = def.Pinos.FirstOrDefault(p => Netlist.EhPinoDeAlimentacao(p.Nome));
        var terra = def.Pinos.FirstOrDefault(p => Netlist.EhPinoDeTerra(p.Nome));
        // O modulo I2C (PCF8574) e alimentado pelo conector de 4 pinos (VCC e
        // GND); o VDD/VSS sao os do LCD, do outro lado.
        if (peca.Tipo == "modulo-i2c")
        {
            alimentacao = def.Pinos.FirstOrDefault(p => p.Nome == "VCC");
            terra = def.Pinos.FirstOrDefault(p => p.Nome == "GND");
        }

        if (alimentacao is not null && terra is not null && !Netlist.EhFonte(peca.Tipo)
            && !PerfilPlaca.EhPlaca(peca.Tipo))
        {
            double ohms = ResistenciaDeModulo(peca.Tipo), maximo = 0.15;
            if (peca.Tipo is "neopixel" or "led-ring" or "neopixel-matrix" && FitaDaPeca(peca) is { } fita)
                (ohms, maximo) = CargaDaFita(fita);
            var r = Entre(alimentacao.Nome, terra.Nome, ohms, 0, 0.02, maximo);
            if (r is not null) yield return r;
        }
    }

    /// <summary>
    /// A resistencia interna da bateria, tirada da corrente que ela
    /// aguenta.
    ///
    /// E o que faz a tensao CAIR quando se pede demais dela -- o efeito
    /// que explica por que o motor gira devagar com pilha velha. A conta
    /// e grosseira de proposito: tensao dividida pela corrente maxima da
    /// o ponto em que ela nao entrega mais nada.
    /// </summary>
    static double ResistenciaDaBateria(Baterias.Ficha ficha)
    {
        double maxima = Math.Max(1, ficha.MiliAmperes);
        // 8% da tensao caindo na corrente maxima; uma LiPo de drone (100 A) fica em ~0,01 ohm.
        return Math.Clamp(ficha.Volts / (maxima / 1000.0) * 0.08, 0.008, 50);
    }

    /// <summary>
    /// A fita de LED (WS2812) que um GPIO comanda pelo DIN desta peca. Nula
    /// sem GPIO no DIN (a fita encadeada no DOUT de outra, ou sem placa):
    /// ai vale a carga tipica de <see cref="ResistenciaDeModulo"/>.
    /// </summary>
    Fita FitaDaPeca(PecaNoCircuito peca)
    {
        string raiz = rede.RaizDe(Netlist.No(peca.Id, "DIN"));
        if (raiz is null) return null;
        foreach (var placa in circuitoDaConta.Pecas)
        {
            var perfil = PerfilPlaca.De(placa.Tipo);
            var maq = perfil is null ? null : bancada?.De(placa.Id);
            if (maq is null || maq.Fitas.Count == 0) continue;
            foreach (var p in Catalogo.Obter(placa.Tipo).Pinos)
            {
                int gpio = perfil.Numero(p.Nome);
                if (gpio >= 0 && maq.Fitas.TryGetValue(gpio, out var fita) && rede.RaizDe(Netlist.No(placa.Id, p.Nome)) == raiz)
                    return fita;
            }
        }
        return null;
    }

    /// <summary>
    /// A carga da fita pelas cores que ela mostra, em 5 V: cada WS2812B gasta
    /// ~1 mA parado e ate 20 mA por cor acesa (o branco cheio passa de 60 mA,
    /// a conta do guia da Adafruit), escalado pelo setBrightness. O maximo e
    /// o da fita inteira em branco: quem nao aguenta e a fonte, nao ela.
    /// </summary>
    static (double Ohms, double Maximo) CargaDaFita(Fita fita)
    {
        double ma = fita.Quantidade * 1.0;
        foreach (int c in fita.Cores)
            ma += ((c >> 16 & 0xFF) + (c >> 8 & 0xFF) + (c & 0xFF)) / 255.0 * 20.0 * fita.Brilho / 255.0;
        return (5.0 / (ma / 1000.0), fita.Quantidade * 0.065);
    }

    static double ResistenciaDeModulo(string tipo) => tipo switch
    {
        // O 74HC parado gasta microampères: a corrente e a das saidas (os ramos delas).
        _ when Logica.EhChip(tipo) => 100_000,
        "neopixel-matrix" => 20,
        "led-ring" => 40,
        "neopixel" => 90,
        "lcd1602" or "lcd2004" or "lcd1602-i2c" or "lcd2004-i2c" => 180,
        "ssd1306" or "ssd1306-i2c" or "ssd1306-128x32" or "sh1106" or "ili9341" or "st7789" or "st7735" => 250,
        // O nRF24L01 com amplificador: ~115 mA transmitindo, ~45 mA na media.
        "nrf24l01-pa-lna" => 75,
        // O TM1637 aceso (~35 mA) e a matriz do MAX7219 (~100 mA, a meia luz).
        "tm1637" => 150,
        "max7219-matriz" => 50,
        _ => 400,
    };

    double[] tensoes;

    void Resolver()
    {
        int n = nos.Count;
        if (n == 0) return;

        tensoes = new double[n];
        var ligados = new bool[ramos.Count];

        // Sentido de cada meia ponte: +1 fornecendo, -1 drenando, 0 parada
        // (a tensao nao vence a queda de saturacao). Comeca pelo palpite do K.
        var sentido = new int[meiasPontes.Count];
        var viradas = new int[meiasPontes.Count];
        for (int k = 0; k < meiasPontes.Count; k++) sentido[k] = meiasPontes[k].K >= 0.5 ? 1 : -1;

        // O transistor converge aos poucos (a resistencia do canal anda com a carga).
        int voltas = chaves.Count > 0 ? 60 : 14;
        for (int volta = 0; volta < voltas; volta++)
        {
            var g = new double[n, n];
            var i = new double[n];

            foreach (var fonte in fontes)
            {
                double gf = 1.0 / fonte.Resistencia;
                g[fonte.No, fonte.No] += gf;
                i[fonte.No] += gf * fonte.Tensao;
            }

            for (int k = 0; k < ramos.Count; k++)
            {
                var ramo = ramos[k];
                double gr = ramo.Queda > 0 && !ligados[k]
                    ? 1e-9
                    : 1.0 / ramo.Resistencia;

                g[ramo.A, ramo.A] += gr;
                g[ramo.B, ramo.B] += gr;
                g[ramo.A, ramo.B] -= gr;
                g[ramo.B, ramo.A] -= gr;

                if (ramo.Queda > 0 && ligados[k])
                {
                    double ieq = ramo.Queda / ramo.Resistencia;
                    i[ramo.A] += ieq;
                    i[ramo.B] -= ieq;
                }

                // Apagado, o diodo nao deixa passar nada -- nem a forca da fonte
                // que esta atras dele (o trilho da USB).
                if (ramo.Fem != 0 && (ramo.Queda <= 0 || ligados[k]))
                {
                    double ieq = ramo.Fem / ramo.Resistencia;
                    i[ramo.A] += ieq;
                    i[ramo.B] -= ieq;
                }
            }

            for (int k = 0; k < meiasPontes.Count; k++)
            {
                if (sentido[k] == 0) continue;
                var m = meiasPontes[k];
                double gm = 1.0 / m.Resistencia, a = m.K, b = 1 - m.K;
                double desvio = sentido[k] * m.Queda;
                int o = m.Saida, ps = m.Potencia, t = m.Terra;
                g[o, o] += gm; g[o, ps] -= gm * a; g[o, t] -= gm * b; i[o] -= gm * desvio;
                g[ps, ps] += gm * a * a; g[ps, t] += gm * a * b; g[ps, o] -= gm * a; i[ps] += gm * a * desvio;
                g[t, ps] += gm * b * a; g[t, t] += gm * b * b; g[t, o] -= gm * b; i[t] += gm * b * desvio;
            }

            for (int k = 0; k < n; k++) g[k, k] += 1e-9;

            tensoes = Gauss(g, i, n);

            // Os reguladores, com a resposta desta volta (e o estado dos
            // diodos que a deu): a saida que a entrada permite, e o que puxam dela.
            bool mudou = conversores.Count > 0 && AtualizarConversores(ligados);
            if (chaves.Count > 0 && AtualizarChaves(ligados)) mudou = true;

            for (int k = 0; k < meiasPontes.Count; k++)
            {
                var m = meiasPontes[k];
                if (m.Queda <= 0) continue;
                double ideal = m.K * tensoes[m.Potencia] + (1 - m.K) * tensoes[m.Terra];
                double sobra = ideal - tensoes[m.Saida];
                int deveria = sentido[k] == 0
                    ? (sobra > m.Queda ? 1 : sobra < -m.Queda ? -1 : 0)
                    : (sobra - sentido[k] * m.Queda) * sentido[k] >= 0 ? sentido[k] : -sentido[k];
                if (deveria == sentido[k]) continue;
                // Virou e desvirou: a tensao nao passa da queda nos dois sentidos -- parada.
                if (++viradas[k] > 2)
                {
                    if (sentido[k] == 0) continue;
                    deveria = 0;
                }
                sentido[k] = deveria;
                mudou = true;
            }
            for (int k = 0; k < ramos.Count; k++)
            {
                var ramo = ramos[k];
                if (ramo.Queda <= 0) continue;

                bool deveria = tensoes[ramo.A] - tensoes[ramo.B] - ramo.Fem > ramo.Queda;
                if (deveria == ligados[k]) continue;

                ligados[k] = deveria;
                mudou = true;
            }

            if (!mudou) break;
        }

        sentidoDasMeiasPontes = sentido;
    }

    int[] sentidoDasMeiasPontes = [];

    static double[] Gauss(double[,] g, double[] i, int n)
    {
        var a = new double[n, n + 1];
        for (int l = 0; l < n; l++)
        {
            for (int c = 0; c < n; c++) a[l, c] = g[l, c];
            a[l, n] = i[l];
        }

        for (int coluna = 0; coluna < n; coluna++)
        {
            int melhor = coluna;
            for (int l = coluna + 1; l < n; l++)
                if (Math.Abs(a[l, coluna]) > Math.Abs(a[melhor, coluna])) melhor = l;

            if (Math.Abs(a[melhor, coluna]) < 1e-14) continue;

            if (melhor != coluna)
                for (int c = coluna; c <= n; c++)
                    (a[coluna, c], a[melhor, c]) = (a[melhor, c], a[coluna, c]);

            for (int l = 0; l < n; l++)
            {
                if (l == coluna) continue;
                double fator = a[l, coluna] / a[coluna, coluna];
                if (fator == 0) continue;

                for (int c = coluna; c <= n; c++) a[l, c] -= fator * a[coluna, c];
            }
        }

        var v = new double[n];
        for (int l = 0; l < n; l++)
            v[l] = Math.Abs(a[l, l]) < 1e-14 ? 0 : a[l, n] / a[l, l];
        return v;
    }

    /// <summary>
    /// A tensao que os resistores impoem num pino -- a do meio de um divisor
    /// --, ou null quando nenhum resistor chega no no dele (pino solto, ou
    /// ligado so a um botao): ali a rede nao diz nada.
    /// </summary>
    public double? TensaoPorResistor(string peca, string pino)
    {
        string raiz = rede?.RaizDe(Netlist.No(peca, pino));
        if (raiz is null || tensoes is null || !indiceDoNo.TryGetValue(raiz, out int no)) return null;
        foreach (var ramo in ramos)
            if ((ramo.A == no || ramo.B == no) && resistores.Contains(ramo.Peca)) return tensoes[no];
        return null;
    }

    void Medir()
    {
        for (int k = 0; k < nos.Count; k++)
            tensaoPorNo[nos[k]] = tensoes is null ? 0 : tensoes[k];

        foreach (var ramo in ramos)
        {
            double va = tensoes is null ? 0 : tensoes[ramo.A];
            double vb = tensoes is null ? 0 : tensoes[ramo.B];

            // O diodo (Queda) so conduz num sentido. O resto -- resistor,
            // motor, contato -- conduz nos dois: o resistor ligado "ao
            // contrario" (o pino 1 no lado de baixo) mostrava corrente zero.
            double comSinal = (va - vb - ramo.Fem - ramo.Queda) / ramo.Resistencia;
            // O diodo apagado nao conduz nada -- nem "para tras".
            if (ramo.Queda > 0) comSinal = Math.Max(0, comSinal);
            if (ramo.Rotulo is not null) correnteDoRamo[(ramo.Peca, ramo.Rotulo)] = comSinal;
            double corrente = Math.Abs(comSinal);

            correntePorPeca[ramo.Peca] =
                Math.Max(correntePorPeca.GetValueOrDefault(ramo.Peca), corrente);

            foreach (int no in new[] { ramo.A, ramo.B })
            {
                string raiz = nos[no];
                correntePorNo[raiz] = Math.Max(correntePorNo.GetValueOrDefault(raiz), corrente);
            }

            var nivel = corrente < SemCorrente ? NivelDeEnergia.Nenhuma
                : corrente > ramo.Maximo ? NivelDeEnergia.Demais
                : corrente < ramo.Ideal * 0.15 ? NivelDeEnergia.Pouca
                : NivelDeEnergia.Aceitavel;

            if (!nivelPorPeca.TryGetValue(ramo.Peca, out var antes) || nivel > antes)
                nivelPorPeca[ramo.Peca] = nivel;
        }

        for (int k = 0; k < meiasPontes.Count; k++)
        {
            var m = meiasPontes[k];
            int s = k < sentidoDasMeiasPontes.Length ? sentidoDasMeiasPontes[k] : 1;
            double corrente = 0;
            if (s != 0 && tensoes is not null)
                corrente = (m.K * tensoes[m.Potencia] + (1 - m.K) * tensoes[m.Terra]
                            - tensoes[m.Saida] - s * m.Queda) / m.Resistencia;
            correnteDoRamo[(m.Peca, m.Rotulo)] = corrente;
            double modulo = Math.Abs(corrente);
            correntePorPeca[m.Peca] = Math.Max(correntePorPeca.GetValueOrDefault(m.Peca), modulo);
            foreach (int no in new[] { m.Saida, m.Potencia })
                correntePorNo[nos[no]] = Math.Max(correntePorNo.GetValueOrDefault(nos[no]), modulo);
            var nivel = modulo < SemCorrente ? NivelDeEnergia.Nenhuma
                : modulo > m.Maximo ? NivelDeEnergia.Demais : NivelDeEnergia.Aceitavel;
            if (!nivelPorPeca.TryGetValue(m.Peca, out var antes) || nivel > antes) nivelPorPeca[m.Peca] = nivel;
        }
    }
}
