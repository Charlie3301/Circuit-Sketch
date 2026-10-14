using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Simulador.App;

/// <summary>
/// Os motores DC (de escovas e brushless) no editor: a conta eletrica de
/// cada quadro, o eixo girando e o rotulo de rpm.
///
/// O motor nao le pino de placa nenhum: ele so sente a TENSAO nos fios, que
/// sai da conta de energia do circuito inteiro. E isso que faz a mesma peca
/// funcionar com a bateria direto, por uma chave, pelo contato de um rele
/// e -- nas proximas pecas -- pela ponte H e pelo ESC, sem um caso para
/// cada um; e que faz o motor ligado direto num GPIO ficar parado, porque
/// a porta de 30 ohms nao segura a tensao com a corrente que ele pede.
/// </summary>
public partial class EditorCircuito
{
    readonly Dictionary<string, MotorDc> motoresDc = [];
    readonly Dictionary<string, MotorSemEscova> motoresSemEscova = [];
    readonly Dictionary<string, TextBlock> rotulosDeGiro = [];
    readonly System.Diagnostics.Stopwatch relogioDosMotores = new();

    /// <summary>
    /// O netlist da conta de energia (resistor sem conduzir), guardado:
    /// montar um netlist com protoboard custa caro, e a conta dos motores
    /// roda a cada quadro. Vale enquanto o netlist da simulacao for o mesmo
    /// objeto -- a janela monta um novo a cada mudanca no circuito.
    /// </summary>
    Netlist redeDaEnergia;
    Netlist redeDaEnergiaDe;

    public static bool EhMotorDc(string tipo) => tipo is "motor-dc" or "motor-tt" or "motor-brushless";

    MotorDc MotorDcDe(PecaNoCircuito peca)
    {
        var modelo = ModeloDeMotor.Da(peca);
        if (!motoresDc.TryGetValue(peca.Id, out var motor) || motor.Modelo != modelo)
            motoresDc[peca.Id] = motor = new MotorDc(modelo);
        return motor;
    }

    MotorSemEscova MotorSemEscovaDe(PecaNoCircuito peca)
    {
        // Trocou a ficha na barra: um motor novo (parado), com os numeros novos.
        var modelo = ModeloDeMotor.Da(peca);
        if (!motoresSemEscova.TryGetValue(peca.Id, out var motor) || motor.Equivalente.Modelo != modelo)
            motoresSemEscova[peca.Id] = motor = new MotorSemEscova(modelo);
        return motor;
    }

    /// <summary>Para o teste: rpm do eixo de um motor (0: nao e motor, ou parado).</summary>
    internal double RpmDoMotor(string idDaPeca)
        => motoresDc.TryGetValue(idDaPeca, out var dc) ? dc.Rpm
         : motoresSemEscova.TryGetValue(idDaPeca, out var bl) ? bl.Rpm : 0;

    /// <summary>Para o teste: o motor de escovas inteiro (tensao, corrente...).</summary>
    internal MotorDc MotorDcParaTeste(string idDaPeca) => motoresDc.GetValueOrDefault(idDaPeca);

    /// <summary>Para o teste: a peca tem a parte que gira na tela?</summary>
    internal bool TemParteMovelParaTeste(string idDaPeca) => parteMovel.ContainsKey(idDaPeca);

    /// <summary>Para o teste: o que o quadro da tela faz com a parte movel desta peca.</summary>
    internal void AnimarParaTeste(string idDaPeca)
    {
        var peca = Atual.Achar(idDaPeca);
        var def = peca is null ? null : Catalogo.Obter(peca.Tipo);
        if (def is not null) AnimarParteMovel(peca, def);
    }

    /// <summary>Para o teste: o texto do rotulo de giro (null: nao aparece).</summary>
    internal string RotuloDeGiroParaTeste(string idDaPeca)
        => rotulosDeGiro.TryGetValue(idDaPeca, out var r) && r.Visibility == Visibility.Visible ? r.Text : null;

    /// <summary>
    /// A conta de energia de agora, com o que muda a cada quadro: os
    /// contatos fechados e a forca contra-eletromotriz dos motores.
    /// </summary>
    internal Energia CalcularEnergia(Bancada bancada, Netlist rede)
    {
        if (rede is null || !ReferenceEquals(rede, redeDaEnergiaDe) || redeDaEnergia is null)
        {
            redeDaEnergia = new Netlist(Atual, resistorConduz: false);
            redeDaEnergiaDe = rede;
        }

        Dictionary<string, double> forcas = null;
        foreach (var (id, motor) in motoresDc)
        {
            if (motor.Velocidade == 0) continue;
            forcas ??= [];
            forcas[id] = motor.ForcaContraEletromotriz;
        }

        return Energia.Calcular(Atual, bancada, redeDaEnergia,
                                rede is null ? null : ContatosFechados(bancada, rede), forcas, CargasDinamicas(),
                                PlacasSemCabo(), ReguladoresDesligados());
    }

    // =====================================================================
    //  o cabo USB e a bateria (Alpha 24)
    // =====================================================================

    /// <summary>
    /// A placa tem o cabo USB? O automatico e o que se ve na mesa: sem bateria
    /// no circuito, a placa esta na USB; com bateria, o projeto e portatil --
    /// a placa esta sem o cabo, e so liga se a bateria a alimentar. Na barra
    /// da placa da para dizer que o cabo esta ligado (a placa na USB e a
    /// bateria so nos motores) ou desligado.
    /// </summary>
    public static bool TemCaboUsb(PecaNoCircuito placa, Circuito circuito)
        => placa.Usb ?? !circuito.Pecas.Any(p => Netlist.EhBateria(p.Tipo));

    HashSet<string> PlacasSemCabo()
        => [.. Atual.Placas().Where(p => !TemCaboUsb(p, Atual)).Select(p => p.Id)];

    /// <summary>As placas que queimaram pela entrada de alimentacao nesta rodada: nao ligam mais ate o proximo Rodar.</summary>
    readonly HashSet<string> queimadasPelaAlimentacao = [];

    /// <summary>A placa tem energia agora (a USB, ou a bateria chegando na entrada dela dentro da faixa)?</summary>
    public bool PlacaComEnergia(string idDaPlaca)
    {
        var placa = Atual.Achar(idDaPlaca);
        if (placa is null) return false;
        if (queimadasPelaAlimentacao.Contains(idDaPlaca)) return false;
        if (TemCaboUsb(placa, Atual)) return true;
        return EnergiaDoQuadro?.AlimentacaoDe(idDaPlaca) switch
        {
            { Ligada: true } => true,
            // Com a queima desligada nas configuracoes nada queima: a tensao
            // alta liga a placa (o aviso continua na barra dela).
            { Queimou: true } => !Preferencias.Ligado(Preferencias.SimularQueima, true),
            _ => false,
        };
    }

    /// <summary>De onde vem a energia da placa, ou por que ela esta apagada (para a barra e o aviso).</summary>
    public string SituacaoDaEnergia(string idDaPlaca)
    {
        var placa = Atual.Achar(idDaPlaca);
        if (placa is null) return "";
        if (queimadasPelaAlimentacao.Contains(idDaPlaca)) return "queimou pela alimentação";
        if (TemCaboUsb(placa, Atual)) return placa.Usb is null ? "na USB" : "na USB (cabo ligado)";
        return EnergiaDoQuadro?.AlimentacaoDe(idDaPlaca) switch
        {
            { Queimou: true, Motivo: var m } => $"queimou: {m}",
            { Ligada: true, Pino: { } pino } a => $"pela bateria, {a.Volts:0.#} V no {pino}" + (a.Motivo is { } m ? $" ({m})" : ""),
            { Motivo: { } motivo } => "sem energia: " + motivo,
            _ => "sem energia: sem o cabo USB (há bateria no circuito)",
        };
    }

    /// <summary>A situacao em poucas palavras, para a barra da placa (a frase inteira vai na dica).</summary>
    public string SituacaoCurta(string idDaPlaca)
    {
        var placa = Atual.Achar(idDaPlaca);
        if (placa is null) return "";
        if (queimadasPelaAlimentacao.Contains(idDaPlaca)) return "queimou";
        if (TemCaboUsb(placa, Atual)) return "na USB";
        return EnergiaDoQuadro?.AlimentacaoDe(idDaPlaca) switch
        {
            { Queimou: true, Pino: { } pino } a => $"queima: {a.Volts:0.0} V no {pino}",
            { Ligada: true, Pino: { } pino } a => $"bateria: {a.Volts:0.0} V no {pino}",
            { Ligada: true } => "ligada",
            { Pino: { } pino } a => $"sem energia ({a.Volts:0.0} V no {pino})",
            _ => "sem energia",
        };
    }

    /// <summary>O cabo USB da placa: nulo e o automatico (ligado so sem bateria no circuito).</summary>
    public void DefinirUsb(PecaNoCircuito placa, bool? usb)
    {
        if (placa is null || !PerfilPlaca.EhPlaca(placa.Tipo) || placa.Usb == usb) return;
        placa.Usb = usb;
        Mudou?.Invoke();
    }

    /// <summary>
    /// Em cima de cada placa sem o cabo, com a simulacao rodando: de onde vem
    /// a energia dela, ou que ela esta apagada. A placa na USB nao ganha nada.
    /// </summary>
    void MostrarEnergiaDasPlacas()
    {
        foreach (var placa in Atual.Placas())
        {
            string texto = null;
            if (EmSimulacao && !TemCaboUsb(placa, Atual))
                texto = EnergiaDoQuadro?.AlimentacaoDe(placa.Id) switch
                {
                    _ when queimadasPelaAlimentacao.Contains(placa.Id) => "💥 queimou pela alimentação",
                    { Ligada: true, Pino: { } pino } a => $"🔋 {a.Volts:0.0} V no {pino}",
                    _ when PlacaComEnergia(placa.Id) => "🔋 ligada",
                    _ => "⏻ sem energia",
                };
            MostrarRotuloAcima(placa, texto);
        }
    }

    /// <summary>
    /// A conta de energia de agora, fora do quadro: e o que o Rodar pergunta
    /// antes de ligar cada placa (sem cabo, so liga a que a bateria alimenta).
    /// </summary>
    public void AtualizarEnergiaAgora(Bancada bancada, Netlist rede)
    {
        if (PlacasSemCabo().Count == 0) return;
        EnergiaDoQuadro = CalcularEnergia(bancada, rede);
    }

    /// <summary>
    /// Os contatos que estao fechados agora. O netlist so conhece o que e
    /// fio; o botao apertado, a posicao da chave e o rele puxado mudam a cada
    /// quadro, e a conta de energia precisa deles: a bateria, a chave e o
    /// motor e o primeiro circuito de motor de todos.
    /// </summary>
    IEnumerable<Energia.Contato> ContatosFechados(Bancada bancada, Netlist rede)
    {
        foreach (var peca in Atual.Pecas)
        {
            switch (peca.Tipo)
            {
                // Botao de 6 mm: 50 mA a 12 V, pelo datasheet. Um motor
                // passando por ele e corrente demais -- e a visao de energia
                // mostra isso.
                case "pushbutton":
                case "pushbutton-6mm":
                    if (BotoesPressionados.Contains(peca.Id))
                        yield return new Energia.Contato(peca.Id, "1.l", "2.l", 0.05);
                    break;

                // O fim de curso: COM no NO apertado, no NC solto (5 A).
                case "micro-switch":
                    yield return new Energia.Contato(peca.Id, "COM",
                        BotoesPressionados.Contains(peca.Id) ? "NO" : "NC", 5.0);
                    break;

                // O pino do meio e o comum: na esquerda ele liga no 1, na
                // direita no 3 (a parte movel anda para a direita com Giro > 0,5).
                case "slide-switch":
                    yield return new Energia.Contato(peca.Id, "2", peca.Giro > 0.5 ? "3" : "1", 0.3);
                    break;

                case "dip-switch-8":
                    for (int k = 0; k < 8; k++)
                        if ((peca.Bits & (1 << k)) != 0)
                            yield return new Energia.Contato(peca.Id, $"{k + 1}a", $"{k + 1}b", 0.025);
                    break;

                // O rele: com a bobina puxada o comum (P) vai para o NO;
                // solta, para o NC. Os dois contatos do KS2E, 2 A cada.
                case "ks2e-m-dc5":
                {
                    bool puxado = BobinaEnergizada(peca, MaquinaDaPeca(bancada, rede, peca), rede);
                    for (int via = 1; via <= 2; via++)
                        yield return new Energia.Contato(peca.Id, $"P{via}",
                                                         puxado ? $"NO{via}" : $"NC{via}", 2.0);
                    break;
                }
            }
        }
    }

    /// <summary>
    /// A conta de energia do quadro, quando o circuito precisa dela a cada
    /// quadro: tem motor (que gira por ela) ou tem bateria (que acende LED e
    /// gira motor sem placa nenhuma). Nula nos outros casos -- o LED de uma
    /// placa continua acendendo pelo nivel do pino, sem conta.
    /// </summary>
    internal Energia EnergiaDoQuadro { get; private set; }

    /// <summary>Para o teste: cada quadro anda este tempo, e nao o do relogio de parede.</summary>
    internal double? PassoFixoParaTeste { get; set; }

    /// <summary>Segundos de fisica que ja passaram (o relogio dos motores e dos avisos que precisam durar).</summary>
    double tempoDaEnergia;

    /// <summary>O tempo deste quadro (o que o STEP no PWM anda, por exemplo).</summary>
    double segundosDoQuadro;

    /// <summary>Desde quando cada saida de driver esta acima da corrente maxima.</summary>
    readonly Dictionary<(string Peca, string Pino), double> excessoDesde = [];

    /// <summary>
    /// A cada quadro, com o tempo de verdade que passou: o motor e fisica, e
    /// gira com a bateria mesmo com a simulacao parada (como gira na mesa).
    /// </summary>
    /// <returns>A conta de energia deste quadro (nula sem motor nem bateria).</returns>
    Energia AplicarEnergiaDoQuadro(Bancada bancada, Netlist rede)
    {
        bool motores = Atual.Pecas.Any(p => EhMotorDc(p.Tipo));
        bool energia = motores || PlacasSemCabo().Count > 0 || Atual.Pecas.Any(p => Netlist.EhBateria(p.Tipo) || DriverDeMotor.De(p.Tipo) is not null
                                                       || DriverDePasso.De(p.Tipo) is not null || p.Tipo == "motor-28byj48"
                                                       || ModeloDeServo.De(p.Tipo) is not null || EhEsc(p.Tipo) || p.Tipo == "receptor-rc"
                                                       || Reguladores.EhRegulador(p.Tipo));
        if (!energia)
        {
            relogioDosMotores.Reset();
            EnergiaDoQuadro = null;
            AcenderLuzesDeModulo();
            return null;
        }
        double segundos = relogioDosMotores.IsRunning ? relogioDosMotores.Elapsed.TotalSeconds : 0;
        relogioDosMotores.Restart();
        if (PassoFixoParaTeste is { } fixo) segundos = fixo;

        // Uma janela parada (arrastada, minimizada) nao vira um salto de
        // fisica: no maximo um decimo de segundo por quadro.
        tempoDaEnergia += Math.Min(segundos, 0.1);
        segundosDoQuadro = Math.Min(segundos, 0.1);
        EnergiaDoQuadro = motores
            ? AvancarMotores(bancada, rede, Math.Min(segundos, 0.1))
            : CalcularEnergia(bancada, rede);
        AquecerReguladores(EnergiaDoQuadro, segundosDoQuadro);
        AcenderLuzesDeModulo();
        return EnergiaDoQuadro;
    }

    /// <summary>Para o teste: a conta do quadro, sem relogio de parede (passo fixo).</summary>
    internal Energia AvancarEnergiaParaTeste(Bancada bancada, Netlist rede, double segundos)
    {
        tempoDaEnergia += segundos;
        segundosDoQuadro = segundos;
        EnergiaDoQuadro = Atual.Pecas.Any(p => EhMotorDc(p.Tipo))
            ? AvancarMotores(bancada, rede, segundos)
            : CalcularEnergia(bancada, rede);
        AquecerReguladores(EnergiaDoQuadro, segundos);
        AcenderLuzesDeModulo();
        return EnergiaDoQuadro;
    }

    /// <summary>
    /// Do quanto passa no LED para o brilho de 0 a 255. O brilho do LED e
    /// quase proporcional a corrente; 20 mA e o "aceso" do datasheet.
    /// Abaixo de 0,2 mA o olho ja nao ve.
    /// </summary>
    internal static int BrilhoPelaCorrente(double amperes)
    {
        double i = Math.Abs(amperes);
        if (i < 0.0002) return 0;
        return (int)Math.Round(255 * Math.Min(1, i / 0.020));
    }

    /// <summary>
    /// O que a conta de energia queima, ou avisa, sem placa nenhuma no
    /// meio: o LED na bateria sem resistor, a bateria em curto. As regras
    /// da Queima olham as ligacoes com a placa; estas olham a corrente.
    /// </summary>
    internal List<Estouro> EstourosPelaEnergia()
    {
        var achados = new List<Estouro>();
        var conta = EnergiaDoQuadro;
        if (conta is null) return achados;

        // O que os drivers disseram (sem logica, sem GND em comum, tensao alta).
        achados.AddRange(conta.AvisosDosDrivers);
        achados.AddRange(avisosDePasso);
        achados.AddRange(avisosDeServo);
        achados.AddRange(avisosDeEsc);
        achados.AddRange(EstourosDosReguladores(conta));

        // A placa que recebeu tensao demais num pino de alimentacao -- sem o
        // cabo, pela bateria que a liga; na USB, por uma bateria empurrando o
        // trilho para cima: queima, e nao liga mais nesta rodada.
        foreach (var placa in Atual.Placas())
            if ((TemCaboUsb(placa, Atual) ? conta.SobretensaoNaUsb(placa) : conta.AlimentacaoDe(placa.Id))
                is { Queimou: true, Motivo: var motivo, Pino: var pino } queimou)
            {
                queimadasPelaAlimentacao.Add(placa.Id);
                bool temVin = Catalogo.Obter(placa.Tipo)?.Pinos.Any(p => p.Nome == "VIN") == true;
                achados.Add(new Estouro(placa.Id, queimou.Direto
                    ? $"{motivo}: esse pino vai direto no chip, sem regulador no caminho. "
                      + (temVin ? "Ligue a bateria no VIN (que passa pelo regulador da placa), ou ponha" : "Ponha")
                      + $" um regulador antes do {pino}."
                    : $"{motivo}: o regulador da placa não aguenta. Use uma bateria de tensão menor nesse pino, "
                      + $"ou ponha um regulador step-down antes do {pino}."));
            }

        foreach (var peca in Atual.Pecas)
        {
            // Saida de driver acima da corrente maxima por mais de um segundo
            // (a partida do motor passa disso por um instante, e pode): a
            // protecao termica corta.
            if (DriverDeMotor.De(peca.Tipo) is { } driver && Catalogo.Obter(peca.Tipo) is { } defDriver)
            {
                foreach (var pino in defDriver.Pinos)
                {
                    double a = Math.Abs(conta.CorrenteDoRamo(peca.Id, pino.Nome));
                    if (a <= driver.CorrenteMaxima * 1.05) { excessoDesde.Remove((peca.Id, pino.Nome)); continue; }
                    if (!excessoDesde.TryGetValue((peca.Id, pino.Nome), out double desde))
                        excessoDesde[(peca.Id, pino.Nome)] = desde = tempoDaEnergia;
                    if (tempoDaEnergia - desde < 1.0) continue;
                    achados.Add(new Estouro(peca.Id,
                        $"a saida {pino.Nome} do {driver.Nome} esta com {a:0.0#} A, e o maximo dele e "
                        + $"{driver.CorrenteMaxima:0.0#} A por canal: na bancada ele esquenta e a protecao termica "
                        + "desliga a saida. Use um driver maior ou um motor que puxe menos.",
                        Queimou: false));
                    break;
                }
            }

            // O motor acima da corrente maxima da ficha por mais de dois
            // segundos: ele aguenta a partida, mas trabalhando assim esquenta.
            if (EhMotorDc(peca.Tipo) && ModeloDeMotor.Da(peca) is { CorrenteMaxima: > 0 } fichaDoMotor)
            {
                double noMotor = peca.Tipo == "motor-brushless"
                    ? Math.Abs(MotorSemEscovaDe(peca).Equivalente.Corrente) : Math.Abs(MotorDcDe(peca).Corrente);
                if (noMotor <= fichaDoMotor.CorrenteMaxima * 1.05) excessoDesde.Remove((peca.Id, "motor"));
                else
                {
                    if (!excessoDesde.TryGetValue((peca.Id, "motor"), out double desde))
                        excessoDesde[(peca.Id, "motor")] = desde = tempoDaEnergia;
                    if (tempoDaEnergia - desde >= 2.0)
                        achados.Add(new Estouro(peca.Id,
                            $"o motor ({fichaDoMotor.Nome}) esta puxando {noMotor:0.#} A, e a ficha dele aguenta "
                            + $"{fichaDoMotor.CorrenteMaxima:0.#} A: trabalhando assim ele esquenta. "
                            + "Diminua a tensao ou a carga (a helice), ou use um motor maior.", Queimou: false));
                }
            }

            switch (peca.Tipo)
            {
                case "led":
                    AvaliarLed(peca, conta.CorrenteDoRamo(peca.Id, "led"), "o LED");
                    break;
                case "diodo":
                case "diodo-zener":
                case "diodo-schottky":
                {
                    // Acima do dobro da corrente da ficha, queima; acima dela, esquenta.
                    var ficha = Diodos.Da(peca);
                    double i = Math.Abs(conta.CorrenteDoRamo(peca.Id, "diodo"));
                    if (i > ficha.CorrenteMaxima * 2)
                        achados.Add(new Estouro(peca.Id, Idioma.T("o {0} conduziu {1:0.##} A, e aguenta {2:0.##} A: queimou.", ficha.Nome, i, ficha.CorrenteMaxima)));
                    else if (i > ficha.CorrenteMaxima * 1.05)
                        achados.Add(new Estouro(peca.Id, Idioma.T("o {0} está com {1:0.##} A, acima dos {2:0.##} A da ficha: esquenta.", ficha.Nome, i, ficha.CorrenteMaxima), Queimou: false));
                    // O zener: o que passa ao contrario vezes a tensao dele vira calor.
                    double pz = Math.Abs(conta.CorrenteDoRamo(peca.Id, "zener")) * ficha.Zener;
                    if (ficha.Zener > 0 && pz > ficha.Potencia * 1.5)
                        achados.Add(new Estouro(peca.Id, Idioma.T("o {0} está dissipando {1:0.##} W, e aguenta {2:0.#} W: queimou. Ponha um resistor em série para limitar a corrente.", ficha.Nome, pz, ficha.Potencia)));
                    break;
                }
                case "rgb-led":
                    foreach (string cor in new[] { "R", "G", "B" })
                        AvaliarLed(peca, conta.CorrenteDoRamo(peca.Id, cor), $"a cor {cor} do LED RGB");
                    break;
                case "led-bar-graph":
                    for (int k = 1; k <= 10; k++)
                        AvaliarLed(peca, conta.CorrenteDoRamo(peca.Id, k.ToString()), $"o LED {k} da barra");
                    break;
                case "bateria":
                {
                    double i = conta.CorrenteDaBateria(peca.Id);
                    double maxima = Math.Max(1, peca.MiliAmperes) / 1000.0;
                    if (i > maxima * 5)
                        achados.Add(new Estouro(peca.Id,
                            $"a bateria esta em curto: {i:0.0} A saindo de uma bateria de {peca.MiliAmperes:0} mA. "
                            + "Na bancada ela esquenta e se esgota em minutos (a pilha de litio pode inchar). "
                            + "Confira se o + nao encosta no - por um fio ou por uma peca sem resistencia.",
                            Queimou: false));
                    else if (i > maxima * 1.2)
                        achados.Add(new Estouro(peca.Id,
                            $"a bateria esta entregando {i * 1000:0} mA, acima dos {peca.MiliAmperes:0} mA que aguenta: "
                            + "a tensao cai e ela esquenta. Use uma bateria maior ou pilhas em paralelo.",
                            Queimou: false));
                    break;
                }
            }
        }
        return achados;

        void AvaliarLed(PecaNoCircuito peca, double amperes, string quem)
        {
            double ma = Math.Abs(amperes) * 1000;
            if (ma <= 30) return;
            double volts = Atual.Pecas.Where(p => Netlist.EhBateria(p.Tipo)).Select(p => p.Volts).DefaultIfEmpty(0).Max();
            string resistor = volts > 2.5 ? $" Com {volts:0.#} V, uns {ResistorDoLed(volts)} Ω em série." : "";
            achados.Add(ma > 50
                ? new Estouro(peca.Id,
                    $"{quem} passou {ma:0} mA, e o maximo dele e 30 mA: queimou. Sem nada para segurar a corrente, "
                    + $"o LED puxa o que a fonte da.{resistor}")
                : new Estouro(peca.Id,
                    $"{quem} esta com {ma:0} mA, acima dos 30 mA do maximo: acende forte, esquenta e dura pouco.{resistor}",
                    Queimou: false));
        }
    }

    /// <summary>O resistor comercial (serie E12) para uns 15 mA num LED vermelho com esta tensao.</summary>
    static int ResistorDoLed(double volts)
    {
        double ideal = (volts - 2.0) / 0.015;
        int[] e12 = [10, 12, 15, 18, 22, 27, 33, 39, 47, 56, 68, 82];
        for (int decada = 1; decada <= 100_000; decada *= 10)
            foreach (int v in e12)
                if (v * decada >= ideal) return v * decada;
        return 1_000_000;
    }

    /// <summary>Avanca todos os motores por este tempo (o teste usa direto, com passo fixo).</summary>
    internal Energia AvancarMotores(Bancada bancada, Netlist rede, double segundos)
    {
        AplicarCargasDosAcessorios();
        var conta = CalcularEnergia(bancada, rede);

        foreach (var peca in Atual.Pecas)
        {
            switch (peca.Tipo)
            {
                case "motor-dc":
                case "motor-tt":
                {
                    var motor = MotorDcDe(peca);
                    double volts = conta.TensaoDoPino(peca.Id, "+") - conta.TensaoDoPino(peca.Id, "-");
                    motor.Avancar(volts, segundos);
                    MostrarGiro(peca, motor.Rpm);
                    break;
                }

                case "motor-brushless":
                {
                    // Num ESC quem manda e ele (AplicarEscs, depois).
                    if (MotorEmAlgumEsc(peca.Id, rede ?? new Netlist(Atual)))
                        break;
                    var motor = MotorSemEscovaDe(peca);
                    motor.SeguirCampo(conta.CorrenteDoRamo(peca.Id, "A"),
                                      conta.CorrenteDoRamo(peca.Id, "B"),
                                      conta.CorrenteDoRamo(peca.Id, "C"), segundos);
                    MostrarGiro(peca, motor.Rpm);
                    break;
                }
            }
        }

        // Peca apagada: o eixo e o rotulo dela vao junto.
        var vivas = Atual.Pecas.Select(p => p.Id).ToHashSet();
        foreach (string id in motoresDc.Keys.Where(id => !vivas.Contains(id)).ToList()) motoresDc.Remove(id);
        foreach (string id in motoresSemEscova.Keys.Where(id => !vivas.Contains(id)).ToList()) motoresSemEscova.Remove(id);
        foreach (string id in rotulosDeGiro.Keys.Where(id => !vivas.Contains(id)).ToList())
        {
            mundo.Children.Remove(rotulosDeGiro[id]);
            rotulosDeGiro.Remove(id);
        }
        return conta;
    }

    /// <summary>O angulo do desenho do eixo, para a parte movel.</summary>
    double AnguloDoEixo(PecaNoCircuito peca)
        => peca.Tipo == "motor-brushless"
            ? motoresSemEscova.TryGetValue(peca.Id, out var bl) ? bl.AnguloNaTela : 0
            : motoresDc.TryGetValue(peca.Id, out var dc) ? dc.AnguloNaTela : 0;

    static readonly CultureInfo Brasil = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>
    /// O rpm em cima da peca, enquanto ela gira. O desenho gira com teto (a
    /// tela tem 25 quadros por segundo); a velocidade de verdade e esta.
    /// </summary>
    void MostrarGiro(PecaNoCircuito peca, double rpm)
    {
        bool girando = Math.Abs(rpm) >= 0.5;
        // A seta e a do que se ve: espelhada, a peca gira ao contrario na tela.
        bool horario = rpm > 0 ^ peca.Espelhada;
        MostrarRotuloAcima(peca, girando
            ? (horario ? "↻ " : "↺ ") + Math.Abs(rpm).ToString("#,0", Brasil) + " rpm" + ExtrasDoGiro(peca, rpm)
            : null);
    }

    /// <summary>Um rotulo pequeno em cima da peca (null: some): o rpm do motor, o estado do ESC.</summary>
    void MostrarRotuloAcima(PecaNoCircuito peca, string texto)
    {
        var def = Catalogo.Obter(peca.Tipo);
        if (def is null) return;

        bool girando = texto is not null;
        if (!rotulosDeGiro.TryGetValue(peca.Id, out var rotulo))
        {
            if (!girando) return;
            rotulo = new TextBlock
            {
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xEC, 0xF2)),
                Background = new SolidColorBrush(Color.FromArgb(0xB0, 0x1A, 0x1C, 0x20)),
                Padding = new Thickness(5, 1, 5, 1),
                IsHitTestVisible = false,
            };
            Panel.SetZIndex(rotulo, 160);
            mundo.Children.Add(rotulo);
            rotulosDeGiro[peca.Id] = rotulo;
        }

        var visivel = girando ? Visibility.Visible : Visibility.Collapsed;
        if (rotulo.Visibility != visivel) rotulo.Visibility = visivel;
        if (!girando) return;
        if (rotulo.Text != texto) rotulo.Text = texto;

        rotulo.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var caixa = CaixaNoMundo(peca, def);
        Canvas.SetLeft(rotulo, caixa.X + caixa.Width / 2 - rotulo.DesiredSize.Width / 2);
        Canvas.SetTop(rotulo, caixa.Y - rotulo.DesiredSize.Height - 3);
    }

    void ApagarRotulosDeGiro()
    {
        foreach (var r in rotulosDeGiro.Values) mundo.Children.Remove(r);
        rotulosDeGiro.Clear();
    }

    /// <summary>Circuito novo: os eixos voltam a zero (os ids podem se repetir).</summary>
    void ZerarMotores()
    {
        motoresDc.Clear();
        motoresSemEscova.Clear();
        contadoresDePasso.Clear();
        anguloPorDriver.Clear();
        servos.Clear();
        escs.Clear();
        relogioDosMotores.Reset();
        redeDaEnergia = null;
        redeDaEnergiaDe = null;
    }
}
