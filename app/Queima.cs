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
public sealed record Estouro(string Peca, string Motivo, bool Queimou = true);

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
                    "curto: alimentação ligada direto no GND"));
        }

        // ---- 2) saída em HIGH direto no terra ----
        foreach (var ponto in redes.Values)
        {
            if (!ponto.TerraDePlaca || !ponto.SaidaLigada || ponto.Resistor) continue;
            foreach (string placa in Placas(circuito))
                achados.Add(new Estouro(placa,
                    $"curto: o pino {ponto.Gpio} está em HIGH e ligado direto no GND"));
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
                $"curto: o pino {emBaixo.Gpio} está em LOW e ligado direto na alimentação"));
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
                ? $"os pinos {alta.Gpio} e {baixa.Gpio} da mesma placa"
                : $"o pino {alta.Gpio} de uma placa e o {baixa.Gpio} da outra";

            foreach (string placa in new[] { alta.Placa, baixa.Placa }.Distinct())
                achados.Add(new Estouro(placa,
                    $"conflito digital: {onde} estão ligados entre si, "
                    + "um em HIGH e o outro em LOW"));
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
                    "ligada ao contrário: o VCC no GND e o GND na alimentação"));
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
                    $"o {nome} está sendo alimentado pelo pino {saida.Gpio}: "
                    + $"ele puxa ~{precisa:0} mA e a porta entrega {saida.Corrente:0} mA. "
                    + "Alimente pelo 5V/VIN e use o pino só para o sinal."));
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
                        $"alto-falante ligado direto no {Volts(v)}: os 8 Ω puxam {v / 8 * 1000:0} mA, "
                        + $"{v * v / 8:0.#} W numa bobina de 0,5 W, e ela queima. O alto-falante vai na saída "
                        + "de um amplificador (o MAX98357A, pelo I2S)"));
                    break;
                }
                var saida = fonte.Saidas.FirstOrDefault(s => s.Alto);
                if (saida.Placa is null) continue;
                achados.Add(new Estouro(saida.Placa,
                    $"alto-falante direto no pino {saida.Gpio}: os 8 Ω pediriam uns {saida.Tensao / 8 * 1000:0} mA "
                    + $"e a porta segura a corrente em {saida.Corrente:0} mA, no limite dela. O som sai fraco e "
                    + "a porta trabalha fora da faixa. Use um amplificador (o MAX98357A, pelo I2S, ou um "
                    + "transistor), ou pelo menos um resistor de 100 Ω em série.",
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
        return [.. achados
            .GroupBy(x => x.Peca)
            .Select(g => g.OrderByDescending(x => x.Queimou).First())];
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
                $"LED ligado sem resistor no {Volts(fonte.TensaoDeTrilho)}: "
                + "o trilho não limita a corrente e ele queima");

        var saida = fonte.Saidas.FirstOrDefault(s => s.Alto);
        if (saida.Placa is null) return null;

        // Pino de dados: o que decide é quanto a porta consegue empurrar.
        double sobra = saida.Tensao - QuedaDoLed;
        if (sobra <= 0) return null;

        if (saida.Tensao >= 4.5)
            return new Estouro(idDaPeca,
                $"LED direto no pino {saida.Gpio}, sem resistor: com 5 V sobram "
                + $"{sobra:0.#} V em cima do LED e a porta passa dos "
                + $"{saida.Corrente:0} mA");

        return new Estouro(idDaPeca,
            $"LED direto no pino {saida.Gpio}, sem resistor. A porta de "
            + $"{Volts(saida.Tensao)} segura a corrente em {saida.Corrente:0} mA e o LED "
            + "sobrevive, mas está fora da faixa: na bancada ele perde brilho com o tempo. "
            + "Ponha 220 Ω.",
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
                    $"o motor de passo esta com as bobinas direto nos GPIOs (o {bobina} no pino {ponto.Saidas[0].Gpio}): "
                    + "no simulador a sequencia anda, mas na bancada cada bobina do NEMA 17 pede ~1 A e o motor nem se mexe. "
                    + "Use um driver (A4988, DRV8825) -- ou, para treinar a sequencia, o 28BYJ-48 com o ULN2003.",
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
                    $"o {nome} está com a fase {fio} direto no pino {saida.Gpio}: o brushless pede "
                    + "ampères em cada fase, chaveadas em sequência, e a porta entrega "
                    + $"{saida.Corrente:0} mA. Ele não gira assim; as três fases vão num ESC "
                    + "(ou num driver trifásico), e o pino só manda o sinal do ESC.",
                    Queimou: false);

            double pede = saida.Tensao / modelo.Resistencia * 1000;
            return new Estouro(peca.Id,
                $"o {nome} está ligado direto no pino {saida.Gpio}: a porta entrega uns "
                + $"{saida.Corrente:0} mA e o motor pede até {pede:0} mA na partida, então ele mal "
                + "sai do lugar. E, sem diodo, o pico de tensão ao desligar o motor pode matar a "
                + "porta. Use uma ponte H (L298N, L9110S, TB6612), um transistor com diodo ou um relé.",
                Queimou: false);
        }

        foreach (string fio in fios)
        {
            var ponto = Achar(redes, rede, peca.Id, fio);
            if (ponto is null || ponto.Resistor || ponto.TensaoDeTrilho <= 0) continue;

            double v = ponto.TensaoDeTrilho;
            double pede = v / modelo.Resistencia * 1000;
            return new Estouro(peca.Id,
                $"o {nome} está alimentado pelo {Volts(v)} da placa: na partida ele puxa até "
                + $"{pede:0} mA, mais do que o regulador (ou a USB) segura. Na bancada a tensão "
                + "cai e a placa reinicia na hora em que o motor liga (o \"brownout\"). Alimente o "
                + "motor por fora, com pilhas ou fonte, e o GND em comum com a placa.",
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
                        ponto.TensaoDeTrilho = Math.Max(ponto.TensaoDeTrilho,
                            TensaoDoPino(pino.Nome, perfilDaPeca ?? doCircuito));
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

                if (maq.Modo[gpio] != 1) continue;

                bool alto = maq.Pwm[gpio] > 0 || maq.Digital[gpio] == 1;
                var perfil = maq.Perfil ?? perfilDaPeca;

                if (ponto.Saidas.Any(s => s.Placa == peca.Id && s.Gpio == gpio)) continue;

                ponto.Saidas.Add((peca.Id, gpio, alto,
                    perfil.TensaoDeOperacao, perfil.CorrenteDoPino));
            }
        }

        return redes;
    }

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
