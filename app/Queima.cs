namespace Simulador.App;

/// <summary>
/// Um componente que estourou, e por quê.
/// </summary>
/// <param name="Peca">Id da peça queimada.</param>
/// <param name="Motivo">Frase curta, do jeito que aparece na tela.</param>
/// <param name="Queimou">
/// Falso quando a ligação está errada mas a porta não tem corrente para
/// destruir nada. Vira aviso na barra, e não peça estourada.
/// </param>
public sealed record Estouro(string Peca, string Motivo, bool Queimou = true)
{
    /// <summary>
    /// O pino do aviso, quando ele e de um pino so (o de 3,3 V que recebe 5 V, o VCC
    /// da peca de 5 V no 3V3): o editor poe a marca amarela em cima dele.
    /// </summary>
    public string Pino { get; init; }

    /// <summary>O texto curto da marca no pino ("5 V numa porta de 3,3 V").</summary>
    public string Marca { get; init; }
}

/// <summary>
/// Descobre ligação que queima componente.
///
/// Existe porque o simulador era bom demais: LED direto no GPIO, sem
/// resistor, acendia bonito e a pessoa levava esse circuito para a
/// bancada -- onde o LED apaga para sempre em alguns segundos. Simulador
/// que só mostra o certo ensina o errado.
///
/// A conta não é elétrica de verdade (não há corrente nem tensão aqui,
/// só nós ligados entre si). É a leitura de bancada, que dá no mesmo
/// para os erros que aparecem em aula:
///
///   1. **Curto de alimentação**: 3V3 e GND no mesmo nó.
///   2. **Saída em HIGH no terra** e **saída em LOW na alimentação**:
///      o pino vira o caminho todo da corrente.
///   3. **Duas saídas brigando** no mesmo nó, uma em HIGH e outra em
///      LOW. É o conflito digital: as duas portas se torram.
///   4. **LED sem resistor**, graduado pela fonte (ver abaixo).
///   5. **Peça alimentada ao contrário**: VCC no terra e GND no positivo.
///   6. **Atuador pendurado num pino de dados**: servo, motor e relé
///      pedem mais corrente do que qualquer GPIO entrega.
///   7. **Motor DC direto na placa**: no pino ele nem arranca; no 3V3 ou
///      no 5V da placa, a partida derruba a tensão e a placa reinicia.
///
/// A regra do resistor sai de graça do netlist: o resistor une as duas
/// pernas, então "tem resistor em série" é o mesmo que "um dos nós do
/// LED tem perna de resistor". Sem varrer caminho nenhum.
///
/// **Nem toda ligação errada queima.** Foi a correção que o professor
/// pediu: um LED direto num pino de 3,3 V não morre -- a porta não
/// entrega corrente para isso. Vira aviso. O mesmo LED num trilho de
/// 5 V, ou num pino de placa de 5 V, morre mesmo.
///
/// Só vale com a placa energizada -- circuito parado não queima nada.
/// </summary>
public static class Queima
{
    /// <summary>Queda no LED aceso, em volts. Vermelho ~1,8; azul ~3,0.</summary>
    const double QuedaDoLed = 2.0;

    /// <summary>Retrato de uma rede, para decidir sem varrer de novo.</summary>
    sealed class Ponto
    {
        public bool Terra;
        public bool Alimentacao;
        public bool Resistor;
        public int Gpio = -1;

        /// <summary>
        /// O nó tem um GND de PLACA, e não só o pino de terra de um
        /// módulo.
        ///
        /// A diferença decide tudo: o GND de um sensor é por onde a
        /// corrente SAI dele, não um terra de verdade. Tratar os dois
        /// como iguais fazia o simulador gritar "curto" quando alguém
        /// só tinha invertido a alimentação de um módulo.
        /// </summary>
        public bool TerraDePlaca;

        /// <summary>Maior tensão que uma PLACA põe neste nó (0 = nenhuma).</summary>
        public double TensaoDeTrilho;

        /// <summary>Os pinos de dados de placa neste nó, com a tensão da lógica de cada placa.</summary>
        public readonly List<(string Placa, string Pino, double Logica)> PinosDePlaca = [];

        /// <summary>As saídas de módulo neste nó (o ECHO do HC-SR04), com a tensão do VCC do módulo.</summary>
        public readonly List<(string Peca, string Pino, double Tensao)> SaidasDeModulo = [];

        /// <summary>Os pinos de alimentação de placa (e de símbolo de VCC) neste nó, com a tensão de cada um.</summary>
        public readonly List<(string Peca, string Pino, double Tensao, bool DePlaca)> Trilhos = [];

        /// <summary>Saídas ligadas neste nó: placa, pino e nível.</summary>
        public readonly List<(string Placa, int Gpio, bool Alto, double Tensao,
                              double Corrente)> Saidas = [];

        public readonly List<string> Pecas = [];

        public bool SaidaLigada => Saidas.Any(s => s.Alto);
        public bool SaidaEmBaixo => Saidas.Any(s => !s.Alto);
    }

    public static List<Estouro> Conferir(Circuito circuito, Netlist rede, Maquina maq)
        => Conferir(circuito, rede, Bancada.DeUmaSo(maq));

    public static List<Estouro> Conferir(Circuito circuito, Netlist rede, Bancada bancada)
    {
        var achados = new List<Estouro>();
        if (circuito is null || rede is null || bancada is null) return achados;

        // Nenhuma placa energizada: circuito parado nao queima nada.
        if (!bancada.Todas.Any(x => x.Value.Energizada)) return achados;

        var redes = Mapear(circuito, rede, bancada);

        // ---- 1) alimentação encostada no terra ----
        // Os DOIS têm que ser da placa: o trilho e o terra. O VCC de um
        // módulo encostado no GND da placa não é curto -- é o módulo
        // sendo alimentado ao contrário, que é a regra 6.
        foreach (var ponto in redes.Values)
        {
            if (!ponto.TerraDePlaca || ponto.TensaoDeTrilho <= 0) continue;
            foreach (string placa in Placas(circuito))
                achados.Add(new Estouro(placa,
                    Idioma.T("curto: alimentação ligada direto no GND")));
        }

        // ---- 2) saída em HIGH direto no terra ----
        foreach (var ponto in redes.Values)
        {
            if (!ponto.TerraDePlaca || !ponto.SaidaLigada || ponto.Resistor) continue;
            foreach (string placa in Placas(circuito))
                achados.Add(new Estouro(placa,
                    Idioma.T("curto: o pino {0} está em HIGH e ligado direto no GND", ponto.Gpio)));
        }

        // ---- 3) saída em LOW direto na alimentação ----
        // O espelho do caso 2, e igualmente fatal: a porta em LOW puxa o
        // trilho inteiro para o terra por dentro do chip.
        foreach (var ponto in redes.Values)
        {
            if (ponto.TensaoDeTrilho <= 0 || ponto.TerraDePlaca || ponto.Resistor) continue;
            var emBaixo = ponto.Saidas.FirstOrDefault(s => !s.Alto);
            if (emBaixo.Placa is null) continue;

            achados.Add(new Estouro(emBaixo.Placa,
                Idioma.T("curto: o pino {0} está em LOW e ligado direto na alimentação", emBaixo.Gpio)));
        }

        // ---- 4) duas saídas brigando no mesmo nó ----
        // O conflito digital: uma porta empurra para 3,3 V, a outra puxa
        // para 0 V, e a corrente só depende da resistência interna das
        // duas. É o erro que não aparece na tela -- o nível fica num
        // meio-termo qualquer -- e leva a placa junto.
        foreach (var ponto in redes.Values)
        {
            if (ponto.Resistor) continue;
            if (!ponto.SaidaLigada || !ponto.SaidaEmBaixo) continue;

            var alta = ponto.Saidas.First(s => s.Alto);
            var baixa = ponto.Saidas.First(s => !s.Alto);

            string onde = alta.Placa == baixa.Placa
                ? Idioma.T("os pinos {0} e {1} da mesma placa", alta.Gpio, baixa.Gpio)
                : Idioma.T("o pino {0} de uma placa e o {1} da outra", alta.Gpio, baixa.Gpio);

            foreach (string placa in new[] { alta.Placa, baixa.Placa }.Distinct())
                achados.Add(new Estouro(placa,
                    Idioma.T("conflito digital: {0} estão ligados entre si, um em HIGH e o outro em LOW", onde)));
        }

        // ---- 5) LED sem resistor ----
        foreach (var peca in circuito.Pecas)
        {
            foreach (var (anodo, catodo) in PernasDeLed(peca.Tipo))
            {
                var a = Achar(redes, rede, peca.Id, anodo);
                var k = Achar(redes, rede, peca.Id, catodo);
                if (a is null || k is null) continue;

                // Resistor de qualquer um dos lados já segura a corrente.
                if (a.Resistor || k.Resistor) continue;
                if (!k.TerraDePlaca) continue;

                var estouro = LedSemResistor(peca.Id, a);
                if (estouro is not null) achados.Add(estouro);
            }
        }

        // ---- 6) peça alimentada ao contrário ----
        foreach (var peca in circuito.Pecas)
        {
            var def = Catalogo.Obter(peca.Tipo);
            if (def is null || Catalogo.EhProtoboard(peca.Tipo)
                || PerfilPlaca.EhPlaca(peca.Tipo)) continue;

            // O que vale é onde o pino da peça FOI PARAR: o VCC dela no
            // terra da placa e o GND dela no trilho. Os dois nós ficam,
            // claro, com nome de alimentação E de terra ao mesmo tempo --
            // é justamente o sintoma -- então a conta olha os pinos da
            // PLACA, e não os nomes de quem está pendurado no nó.
            bool positivoNoTerra = def.Pinos.Any(p => EhAlimentacao(p.Nome)
                && Achar(redes, rede, peca.Id, p.Nome) is { TerraDePlaca: true });
            bool terraNoPositivo = def.Pinos.Any(p => EhTerra(p.Nome)
                && Achar(redes, rede, peca.Id, p.Nome) is { TensaoDeTrilho: > 0 });

            if (positivoNoTerra && terraNoPositivo)
                achados.Add(new Estouro(peca.Id,
                    Idioma.T("ligada ao contrário: o VCC no GND e o GND na alimentação")));
        }

        // ---- 7) atuador pendurado num pino de dados ----
        foreach (var peca in circuito.Pecas)
        {
            double precisa = CorrenteDe(peca.Tipo);
            if (precisa <= 0) continue;

            var def = Catalogo.Obter(peca.Tipo);
            if (def is null) continue;

            foreach (var pino in def.Pinos.Where(x => EhAlimentacao(x.Nome)))
            {
                var ponto = Achar(redes, rede, peca.Id, pino.Nome);

                // Só quando a ÚNICA fonte é um pino de dados: ligado no
                // trilho está certo, e é o que quase todo mundo faz.
                if (ponto is null || ponto.TensaoDeTrilho > 0) continue;
                var saida = ponto.Saidas.FirstOrDefault(s => s.Alto);
                if (saida.Placa is null || saida.Corrente >= precisa) continue;

                string nome = Catalogo.Obter(peca.Tipo)?.Nome ?? peca.Tipo;
                achados.Add(new Estouro(saida.Placa,
                    Idioma.T("o {0} está sendo alimentado pelo pino {1}: ele puxa ~{2:0} mA e a porta entrega {3:0} mA. Alimente pelo 5V/VIN e use o pino só para o sinal.", nome, saida.Gpio, precisa, saida.Corrente)));
            }
        }

        // ---- 8) alto-falante sem amplificador ----
        // Oito ohms sao quase um curto para quem liga direto. No pino, a
        // porta segura a corrente no limite dela: o som sai fraco e a porta
        // trabalha fora da faixa (aviso, como o LED sem resistor). No
        // trilho nao ha limite nenhum, e a bobina de 0,5 W recebe watts.
        foreach (var peca in circuito.Pecas)
        {
            if (peca.Tipo != "alto-falante") continue;
            var mais = Achar(redes, rede, peca.Id, "+");
            var menos = Achar(redes, rede, peca.Id, "-");
            if (mais is null || menos is null || mais.Resistor || menos.Resistor) continue;

            foreach (var (fonte, volta) in new[] { (mais, menos), (menos, mais) })
            {
                if (!volta.TerraDePlaca) continue;
                if (fonte.TensaoDeTrilho > 0)
                {
                    double v = fonte.TensaoDeTrilho;
                    achados.Add(new Estouro(peca.Id,
                        Idioma.T("alto-falante ligado direto no {0}: os 8 Ω puxam {1:0} mA, {2:0.#} W numa bobina de 0,5 W, e ela queima. O alto-falante vai na saída de um amplificador (o MAX98357A, pelo I2S)", Volts(v), v / 8 * 1000, v * v / 8)));
                    break;
                }
                var saida = fonte.Saidas.FirstOrDefault(s => s.Alto);
                if (saida.Placa is null) continue;
                achados.Add(new Estouro(saida.Placa,
                    Idioma.T("alto-falante direto no pino {0}: os 8 Ω pediriam uns {1:0} mA e a porta segura a corrente em {2:0} mA, no limite dela. O som sai fraco e a porta trabalha fora da faixa. Use um amplificador (o MAX98357A, pelo I2S, ou um transistor), ou pelo menos um resistor de 100 Ω em série.", saida.Gpio, saida.Tensao / 8 * 1000, saida.Corrente),
                    Queimou: false));
                break;
            }
        }

        // ---- 9) motor DC direto num pino, ou no trilho da placa ----
        foreach (var peca in circuito.Pecas)
        {
            var aviso = AvisoDeMotor(peca, redes, rede);
            if (aviso is not null) achados.Add(aviso);
        }

        // A mesma placa pode aparecer por dois motivos; o primeiro basta.
        // O que QUEIMA vem antes do que só avisa: a peça só estoura uma
        // vez, e o motivo grave é o que interessa.
        // Os avisos de nivel (10 e 11) vem a parte, um por pino: cada um tem a marca dele.
        return [.. achados
            .GroupBy(x => x.Peca)
            .Select(g => g.OrderByDescending(x => x.Queimou).First())
            .Concat(AvisosDeNivel(circuito, rede, redes).DistinctBy(x => (x.Peca, x.Pino)))];
    }

    /// <summary>
    /// O LED sem resistor queima -- ou não.
    ///
    /// É a correção que o professor pediu: "algumas portas não têm carga
    /// o suficiente para queimar o componente". Um GPIO de 3,3 V entrega
    /// uns 40 mA no limite, e o LED aguenta o tranco; o mesmo LED num
    /// trilho de 5 V, onde não há limite nenhum, vira fumaça.
    /// </summary>
    static Estouro LedSemResistor(string idDaPeca, Ponto fonte)
    {
        // Trilho: a fonte não tem limite de corrente. Queima sempre.
        if (fonte.TensaoDeTrilho > 0)
            return new Estouro(idDaPeca,
                Idioma.T("LED ligado sem resistor no {0}: o trilho não limita a corrente e ele queima", Volts(fonte.TensaoDeTrilho)));

        var saida = fonte.Saidas.FirstOrDefault(s => s.Alto);
        if (saida.Placa is null) return null;

        // Pino de dados: o que decide é quanto a porta consegue empurrar.
        double sobra = saida.Tensao - QuedaDoLed;
        if (sobra <= 0) return null;

        if (saida.Tensao >= 4.5)
            return new Estouro(idDaPeca,
                Idioma.T("LED direto no pino {0}, sem resistor: com 5 V sobram {1:0.#} V em cima do LED e a porta passa dos {2:0} mA", saida.Gpio, sobra, saida.Corrente));

        return new Estouro(idDaPeca,
            Idioma.T("LED direto no pino {0}, sem resistor. A porta de {1} segura a corrente em {2:0} mA e o LED sobrevive, mas está fora da faixa: na bancada ele perde brilho com o tempo. Ponha 220 Ω.", saida.Gpio, Volts(saida.Tensao), saida.Corrente),
            Queimou: false);
    }

    /// <summary>
    /// O motor DC onde ele nao deve ficar. Nenhum dos dois queima na hora --
    /// por isso e aviso --, mas os dois sao o erro mais comum de quem monta o
    /// primeiro carrinho:
    ///
    ///   - **direto num pino**: a porta entrega uns 40 mA e o motor pede
    ///     centenas na partida; ele nem sai do lugar (a conta de energia ja
    ///     mostra isso: o eixo fica parado). E sem diodo o pico de tensao ao
    ///     desligar ainda pode matar a porta.
    ///   - **no 3V3 ou no 5V da placa**: o regulador (ou a porta USB) nao
    ///     segura a partida, a tensao cai e a placa reinicia -- o "brownout"
    ///     que aparece no monitor serial quando o motor liga.
    ///
    /// O brushless tem o aviso dele: sem ESC, ele nao gira de jeito nenhum.
    /// </summary>
    static Estouro AvisoDeMotor(PecaNoCircuito peca, Dictionary<string, Ponto> redes, Netlist rede)
    {
        // O NEMA 17 nos GPIOs: o exercicio da sequencia funciona no simulador,
        // mas na bancada cada bobina pede ~1 A e o motor nem se mexe.
        if (peca.Tipo == "stepper-motor")
        {
            foreach (string bobina in new[] { "A+", "A-", "B+", "B-" })
            {
                var ponto = Achar(redes, rede, peca.Id, bobina);
                if (ponto is null || ponto.Resistor || ponto.Saidas.Count == 0) continue;
                return new Estouro(peca.Id,
                    Idioma.T("o motor de passo esta com as bobinas direto nos GPIOs (o {0} no pino {1}): no simulador a sequencia anda, mas na bancada cada bobina do NEMA 17 pede ~1 A e o motor nem se mexe. Use um driver (A4988, DRV8825) -- ou, para treinar a sequencia, o 28BYJ-48 com o ULN2003.", bobina, ponto.Saidas[0].Gpio),
                    Queimou: false);
            }
            return null;
        }

        string[] fios = peca.Tipo switch
        {
            "motor-dc" or "motor-tt" => ["+", "-"],
            "motor-brushless" => ["A", "B", "C"],
            _ => null,
        };
        if (fios is null) return null;

        var modelo = ModeloDeMotor.Da(peca);
        string nome = Catalogo.Obter(peca.Tipo)?.Nome ?? peca.Tipo;

        foreach (string fio in fios)
        {
            var ponto = Achar(redes, rede, peca.Id, fio);
            if (ponto is null || ponto.Resistor || ponto.Saidas.Count == 0) continue;
            var saida = ponto.Saidas[0];

            if (peca.Tipo == "motor-brushless")
                return new Estouro(peca.Id,
                    Idioma.T("o {0} está com a fase {1} direto no pino {2}: o brushless pede ampères em cada fase, chaveadas em sequência, e a porta entrega {3:0} mA. Ele não gira assim; as três fases vão num ESC (ou num driver trifásico), e o pino só manda o sinal do ESC.", nome, fio, saida.Gpio, saida.Corrente),
                    Queimou: false);

            double pede = saida.Tensao / modelo.Resistencia * 1000;
            return new Estouro(peca.Id,
                Idioma.T("o {0} está ligado direto no pino {1}: a porta entrega uns {2:0} mA e o motor pede até {3:0} mA na partida, então ele mal sai do lugar. E, sem diodo, o pico de tensão ao desligar o motor pode matar a porta. Use uma ponte H (L298N, L9110S, TB6612), um transistor com diodo ou um relé.", nome, saida.Gpio, saida.Corrente, pede),
                Queimou: false);
        }

        foreach (string fio in fios)
        {
            var ponto = Achar(redes, rede, peca.Id, fio);
            if (ponto is null || ponto.Resistor || ponto.TensaoDeTrilho <= 0) continue;

            double v = ponto.TensaoDeTrilho;
            double pede = v / modelo.Resistencia * 1000;
            return new Estouro(peca.Id,
                Idioma.T("o {0} está alimentado pelo {1} da placa: na partida ele puxa até {2:0} mA, mais do que o regulador (ou a USB) segura. Na bancada a tensão cai e a placa reinicia na hora em que o motor liga (o \"brownout\"). Alimente o motor por fora, com pilhas ou fonte, e o GND em comum com a placa.", nome, Volts(v), pede),
                Queimou: false);
        }

        return null;
    }

    static string Volts(double v)
        => v.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)
               .Replace('.', ',') + " V";

    static IEnumerable<string> Placas(Circuito circuito)
        => circuito.Placas().Select(x => x.Id);

    static Ponto Achar(Dictionary<string, Ponto> redes, Netlist rede,
                       string peca, string pino)
    {
        string raiz = rede.RaizDe(Netlist.No(peca, pino));
        return raiz is null ? null : redes.GetValueOrDefault(raiz);
    }

    static Dictionary<string, Ponto> Mapear(Circuito circuito, Netlist rede, Bancada bancada)
    {
        var redes = new Dictionary<string, Ponto>();

        // Um simbolo de VCC nao tem perfil proprio; a tensao dele e a da
        // placa que esta no circuito.
        var doCircuito = Netlist.PlacaDoCircuito(circuito);

        foreach (var peca in circuito.Pecas)
        {
            var def = Catalogo.Obter(peca.Tipo);
            if (def is null) continue;

            bool ehPlaca = PerfilPlaca.EhPlaca(peca.Tipo);
            var perfilDaPeca = PerfilPlaca.De(peca.Tipo);

            // Simbolo de VCC e de GND contam como trilho: e so para isso
            // que eles existem. A tensao e a da placa do circuito.
            bool ehFonte = Netlist.EhFonte(peca.Tipo);

            foreach (var pino in def.Pinos)
            {
                string raiz = rede.RaizDe(Netlist.No(peca.Id, pino.Nome));
                if (raiz is null) continue;

                if (!redes.TryGetValue(raiz, out var ponto))
                    redes[raiz] = ponto = new Ponto();

                ponto.Pecas.Add(peca.Id);

                if (peca.Tipo == "resistor") ponto.Resistor = true;
                if (EhTerra(pino.Nome))
                {
                    ponto.Terra = true;
                    if (ehFonte) ponto.TerraDePlaca = true;
                }
                if (EhAlimentacao(pino.Nome))
                {
                    ponto.Alimentacao = true;

                    // Fonte de verdade é a da PLACA: o VCC de um módulo é
                    // quem consome, não quem fornece.
                    if (ehFonte)
                    {
                        double v = TensaoDoPino(pino.Nome, perfilDaPeca ?? doCircuito);
                        ponto.TensaoDeTrilho = Math.Max(ponto.TensaoDeTrilho, v);
                        ponto.Trilhos.Add((peca.Id, pino.Nome, v, ehPlaca));
                    }
                }

                // Só os pinos da PLACA entram como saída. Toda saída é,
                // no fim, um pino de placa, e o nó que a alcança contém
                // esse pino -- então nada se perde, e o número do GPIO é
                // o do PRÓPRIO pino.
                //
                // Perguntar ao netlist dava o GPIO canônico do nó, que é
                // um só: dois pinos da mesma placa no mesmo fio (o
                // conflito digital) apareciam como um pino repetido, e a
                // briga passava batido.
                if (!ehPlaca || perfilDaPeca is null) continue;

                int gpio = perfilDaPeca.Numero(pino.Nome);
                if (gpio < 0 || gpio >= Maquina.NumPinos) continue;

                var maq = bancada.De(peca.Id);
                if (ponto.Gpio < 0) ponto.Gpio = gpio;
                ponto.PinosDePlaca.Add((peca.Id, pino.Nome, perfilDaPeca.TensaoDeOperacao));

                if (maq.Modo[gpio] != 1) continue;

                bool alto = maq.Pwm[gpio] > 0 || maq.Digital[gpio] == 1;
                var perfil = maq.Perfil ?? perfilDaPeca;

                if (ponto.Saidas.Any(s => s.Placa == peca.Id && s.Gpio == gpio)) continue;

                ponto.Saidas.Add((peca.Id, gpio, alto,
                    perfil.TensaoDeOperacao, perfil.CorrenteDoPino));
            }
        }

        // As saidas dos modulos: o comparador (LM393) e o HC-SR04 poem o sinal no
        // nivel do VCC deles -- alimentados com 5 V, sao 5 V num pino de 3,3 V.
        foreach (var peca in circuito.Pecas)
        {
            if (PerfilPlaca.EhPlaca(peca.Tipo) || Catalogo.EhProtoboard(peca.Tipo) || Netlist.EhFonte(peca.Tipo)) continue;
            var def = Catalogo.Obter(peca.Tipo);
            if (def is null) continue;
            double vcc = def.Pinos.Where(p => EhAlimentacao(p.Nome))
                .Select(p => Achar(redes, rede, peca.Id, p.Nome)?.TensaoDeTrilho ?? 0).DefaultIfEmpty(0).Max();
            if (vcc <= 0) continue;
            // O HC-SR501 tem regulador: a saida dele e de 3,3 V mesmo no 5 V.
            double nivel = peca.Tipo == "pir-motion-sensor" ? Math.Min(vcc, 3.3) : vcc;
            foreach (var pino in def.Pinos.Where(p => SaidasDeModulo.Contains(p.Nome)))
                if (Achar(redes, rede, peca.Id, pino.Nome) is { } ponto)
                    ponto.SaidasDeModulo.Add((peca.Id, pino.Nome, nivel));
        }

        return redes;
    }

    /// <summary>Os pinos de saida de modulo que saem no nivel do VCC dele.</summary>
    static readonly HashSet<string> SaidasDeModulo = ["ECHO", "DO", "D0", "DOUT", "OUT"];

    /// <summary>
    /// As pecas que pedem 5 V no VCC, e a tensao abaixo da qual elas nao funcionam
    /// direito. (O servo e o modulo de rele tem o aviso deles, com a conta de energia.)
    /// </summary>
    static readonly Dictionary<string, (double Minimo, string Sintoma)> PedemCincoVolts = new()
    {
        ["hc-sr04"] = (4.5, Idioma.T("o pulso do sensor sai fraco e o eco não volta: a distância lida fica em 0")),
        ["lcd1602"] = (4.5, Idioma.T("o contraste some e os caracteres não aparecem")),
        ["lcd2004"] = (4.5, Idioma.T("o contraste some e os caracteres não aparecem")),
        ["lcd1602-i2c"] = (4.5, Idioma.T("o contraste some e os caracteres não aparecem")),
        ["lcd2004-i2c"] = (4.5, Idioma.T("o contraste some e os caracteres não aparecem")),
        ["gas-sensor"] = (4.5, Idioma.T("o aquecedor do MQ-2 não esquenta e a leitura não vale")),
        ["max7219-matriz"] = (4.0, Idioma.T("o MAX7219 pede de 4 a 5,5 V: a matriz pisca ou apaga")),
        ["neopixel"] = (3.5, Idioma.T("o WS2812 pede pelo menos 3,5 V: as cores saem erradas")),
        ["led-ring"] = (3.5, Idioma.T("o WS2812 pede pelo menos 3,5 V: as cores saem erradas")),
        ["neopixel-matrix"] = (3.5, Idioma.T("o WS2812 pede pelo menos 3,5 V: as cores saem erradas")),
    };

    /// <summary>As pecas que aguentam no maximo 3,6 V no VCC: com 5 V, queimam.</summary>
    static readonly Dictionary<string, double> SoTresVolts = new()
    {
        ["nrf24l01"] = 3.6,
        ["nrf24l01-pa-lna"] = 3.6,
    };

    /// <summary>
    /// O nivel de tensao errado, em qualquer porta, nos dois sentidos -- e o que
    /// acontece se ficar assim:
    ///
    ///   10) uma tensao maior numa porta de dados menor (5 V no pino da ESP32);
    ///   11) uma tensao menor numa porta de dados maior (3,3 V no pino do Uno);
    ///   12) a peca de 5 V alimentada pelo 3,3 V;
    ///   13) 5 V num pino de alimentacao de 3,3 V (o 3V3 da placa, o VCC do nRF24L01).
    ///
    /// Sao avisos, com a marca no pino: na hora nada estoura no simulador, mas o
    /// texto diz o que a bancada faz.
    /// </summary>
    static IEnumerable<Estouro> AvisosDeNivel(Circuito circuito, Netlist rede, Dictionary<string, Ponto> redes)
    {
        foreach (var ponto in redes.Values)
        {
            if (ponto.Resistor) continue;     // o resistor (o divisor) ja segura

            // Quem poe tensao neste no, e com quanto. O pino em OUTPUT conta com a
            // tensao da placa dele em HIGH ou em LOW: e o que ele PODE pôr (e assim
            // a marca nao pisca com o LED).
            var fontes = new List<(string Texto, double V, string Dono, bool Saida)>();
            foreach (var t in ponto.Trilhos)
                fontes.Add((Idioma.T("o {0} do {1}", t.Pino, NomeDe(circuito, t.Peca)), t.Tensao, t.Peca, false));
            foreach (var saida in ponto.Saidas)
                fontes.Add((Idioma.T("o pino {0} do {1}, em OUTPUT", saida.Gpio, NomeDe(circuito, saida.Placa)), saida.Tensao, saida.Placa, true));
            foreach (var m in ponto.SaidasDeModulo)
                fontes.Add((Idioma.T("a saída {0} do {1}, alimentado com {2}", m.Pino, NomeDe(circuito, m.Peca), Volts(m.Tensao)), m.Tensao, m.Peca, false));
            if (fontes.Count == 0) continue;

            // ---- 10 e 11) as portas de dados das placas ----
            foreach (var (placa, pino, logica) in ponto.PinosDePlaca)
            {
                // A saida da propria placa neste no nao conta contra ela.
                bool saidaDela = ponto.Saidas.Any(x => x.Placa == placa);
                var outras = fontes.Where(f => !(f.Saida && f.Dono == placa)).ToList();

                var maior = outras.Where(f => f.V > logica + 0.6).OrderByDescending(f => f.V).FirstOrDefault();
                if (maior.Texto is not null)
                {
                    yield return new Estouro(placa,
                        Idioma.T("o pino {0} recebe {1} ({2}), e a porta do {3} é de {4}. Se ficar assim: a corrente entra pelos diodos de proteção da porta, ela esquenta e pode queimar com o tempo (ou o chip trava e reinicia). Ponha um divisor de tensão (1 kΩ em série e 2 kΩ para o GND) ou um conversor de nível lógico.", pino, Volts(maior.V), maior.Texto, NomeDe(circuito, placa), Volts(logica)),
                        Queimou: false) { Pino = pino, Marca = Idioma.T("{0} numa porta de {1}", Volts(maior.V), Volts(logica)) };
                    continue;
                }

                // A porta de entrada de 5 V com o HIGH de 3,3 V (a saida dela mesma nao conta).
                if (saidaDela) continue;
                var menor = outras.Where(f => f.V > 0 && f.V < logica - 1).OrderBy(f => f.V).FirstOrDefault();
                if (menor.Texto is null || outras.Any(f => f.V >= logica - 0.5)) continue;
                yield return new Estouro(placa,
                    Idioma.T("o pino {0} recebe {1} ({2}), e a porta do {3} é de {4}. Se ficar assim: o HIGH de {1} fica perto do limite que a porta entende como HIGH (uns 3 V no ATmega). Costuma funcionar, mas sem folga: com ruído ou fio comprido a leitura falha às vezes. Para garantir, use um conversor de nível lógico.", pino, Volts(menor.V), menor.Texto, NomeDe(circuito, placa), Volts(logica)),
                    Queimou: false) { Pino = pino, Marca = Idioma.T("{0} numa porta de {1}", Volts(menor.V), Volts(logica)) };
            }

            // ---- 13) 5 V no 3V3 de uma placa: dois trilhos diferentes no mesmo fio ----
            foreach (var baixo in ponto.Trilhos.Where(t => t.DePlaca && t.Tensao < 4))
            {
                var alto = fontes.Where(f => f.V > baixo.Tensao + 0.6).OrderByDescending(f => f.V).FirstOrDefault();
                if (alto.Texto is null) continue;
                yield return new Estouro(baixo.Peca,
                    Idioma.T("o {0} (a saída de {1} do regulador) está ligado em {2} ({3}). Se ficar assim: os {2} entram pela saída do regulador e chegam no chip, que aguenta no máximo 3,6 V: na bancada a placa queima. Nunca ligue dois trilhos de tensões diferentes no mesmo fio.", baixo.Pino, Volts(baixo.Tensao), Volts(alto.V), alto.Texto),
                    Queimou: false) { Pino = baixo.Pino, Marca = Idioma.T("{0} num pino de {1}", Volts(alto.V), Volts(baixo.Tensao)) };
            }
        }

        // ---- 12 e 13) as pecas: a de 5 V no 3,3 V, e a de 3,3 V no 5 V ----
        foreach (var peca in circuito.Pecas)
        {
            var def = Catalogo.Obter(peca.Tipo);
            if (def is null) continue;
            bool pede5 = PedemCincoVolts.TryGetValue(peca.Tipo, out var pede);
            bool so3 = SoTresVolts.TryGetValue(peca.Tipo, out double maximo);
            if (!pede5 && !so3) continue;
            foreach (var pino in def.Pinos.Where(p => EhAlimentacao(p.Nome)))
            {
                if (Achar(redes, rede, peca.Id, pino.Nome) is not { } ponto) continue;
                double v = ponto.TensaoDeTrilho;
                if (v <= 0) continue;
                if (pede5 && v < pede.Minimo)
                {
                    yield return new Estouro(peca.Id,
                        Idioma.T("precisa de {0} ou mais e está no {1}. Se ficar assim: {2}. Ligue o VCC no 5V (ou no VIN) da placa.", Volts(pede.Minimo), Volts(v), pede.Sintoma),
                        Queimou: false) { Pino = pino.Nome, Marca = Idioma.T("{0} numa peça de 5 V", Volts(v)) };
                    break;
                }
                if (so3 && v > maximo)
                {
                    yield return new Estouro(peca.Id,
                        Idioma.T("aguenta no máximo {0} no {1} e está no {2}. Se ficar assim: na bancada ele queima em segundos. Ligue no 3V3 da placa (e ponha um capacitor de 10 µF no VCC).", Volts(maximo), pino.Nome, Volts(v)),
                        Queimou: false) { Pino = pino.Nome, Marca = Idioma.T("{0} numa peça de {1}", Volts(v), Volts(maximo)) };
                    break;
                }
            }
        }
    }

    static string NomeDe(Circuito circuito, string idDaPeca)
        => circuito.Achar(idDaPeca) is { } p ? Catalogo.Obter(p.Tipo)?.Nome ?? p.Tipo : idDaPeca;

    /// <summary>Tensão que um pino de alimentação de placa entrega.</summary>
    static double TensaoDoPino(string nome, PerfilPlaca perfil)
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

    /// <summary>
    /// Quanta corrente a peça pede, em mA, quando é ela que consome.
    ///
    /// Zero para o que vive de sinal (sensor, display de I2C, botão): a
    /// regra só existe para o que a porta não aguenta.
    /// </summary>
    static double CorrenteDe(string tipo) => tipo switch
    {
        "servo" => 600,                  // com carga passa de 1 A
        "servo-mg996r" => 900,           // trava com 2,5 A
        "servo-360" => 220,
        "stepper-motor" or "biaxial-stepper" => 400,
        "ks2e-m-dc5" => 80,              // bobina do relé
        "neopixel-matrix" => 300,        // 64 LEDs no branco
        "led-ring" => 180,
        "neopixel" => 60,
        _ => 0,
    };

    /// <summary>
    /// Pares (anodo, catodo) que existem na peça.
    ///
    /// O LED RGB é três LEDs num corpo só, com o catodo em comum -- e
    /// cada cor precisa do seu resistor, que é o erro clássico de quem
    /// acha que "é um componente só, um resistor basta".
    /// </summary>
    static IEnumerable<(string anodo, string catodo)> PernasDeLed(string tipo)
    {
        switch (tipo)
        {
            case "led":
                yield return ("A", "C");
                break;

            case "rgb-led":
                yield return ("R", "COM");
                yield return ("G", "COM");
                yield return ("B", "COM");
                break;

            // Barra de LEDs: dez LEDs independentes no mesmo corpo.
            case "led-bar-graph":
                for (int i = 1; i <= 10; i++) yield return ($"A{i}", $"C{i}");
                break;
        }
    }

    // Os nomes de pino de terra e de alimentação moram no Netlist: o
    // desenho precisa da mesma resposta para acender um LED preso no
    // trilho, e duas listas parecidas ficariam diferentes num mês.
    static bool EhTerra(string pino) => Netlist.EhPinoDeTerra(pino);

    static bool EhAlimentacao(string pino) => Netlist.EhPinoDeAlimentacao(pino);
}
