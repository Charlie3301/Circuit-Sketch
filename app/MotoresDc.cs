namespace Simulador.App;

/// <summary>
/// Um motor de escovas visto pelos dois fios e pelo eixo de SAIDA.
///
/// O modelo e o de livro: o enrolamento e uma resistencia em serie com a
/// forca contra-eletromotriz (Ke x velocidade), e o torque e Kt x corrente.
/// Num motor com caixa de reducao tudo ja vem medido no eixo de saida, e o
/// Kt sai menor que o Ke: a diferenca e o que a caixa perde.
///
/// Os numeros sao dos datasheets, e nao chutes, porque e por eles que o
/// aluno descobre o que a bancada vai fazer: que a porta da placa nao
/// arranca o motor, que o amarelo anda a ~250 rpm com 6 V e a ~110 rpm com
/// 3 V, que um PWM baixo nao tira o eixo do lugar mas, girando, ele so para
/// bem mais embaixo.
/// </summary>
/// <param name="Resistencia">Ohms entre os dois fios (o que limita a partida).</param>
/// <param name="Ke">Volts de forca contra-eletromotriz por rad/s do eixo de saida.</param>
/// <param name="Kt">N.m por ampere no eixo de saida (com a perda da reducao).</param>
/// <param name="Inercia">kg.m2 vistos do eixo de saida (o rotor, multiplicado pela reducao ao quadrado).</param>
/// <param name="AtritoSeco">N.m que o eixo perde girando, em qualquer velocidade.</param>
/// <param name="AtritoDePartida">N.m para tirar o eixo parado do lugar: e a tensao de partida.</param>
/// <param name="AtritoViscoso">N.m por rad/s: o que cresce com a velocidade.</param>
/// <param name="TensaoNominal">Volts para os quais o motor foi feito.</param>
/// <param name="GirosNaTela">Giros por segundo, no maximo, que o desenho mostra.</param>
/// <param name="CorrenteMaxima">Amperes que ele aguenta trabalhando (0: sem limite conhecido).</param>
/// <param name="ParesDePolos">Sem escova: os pares de imas do rotor (o 2212 tem 7; o inrunner de carrinho, 2).</param>
public sealed record ModeloDeMotor(string Nome, double Resistencia, double Ke, double Kt,
                                   double Inercia, double AtritoSeco, double AtritoDePartida,
                                   double AtritoViscoso, double TensaoNominal, double GirosNaTela,
                                   double CorrenteMaxima = 0, int ParesDePolos = 7)
{
    /// <summary>
    /// O motor "130" dos kits (Adafruit 711): 6 V nominais, 9100 rpm e 70 mA
    /// sem carga, 500 mA com o eixo travado, partida com 2,0 V.
    /// </summary>
    public static readonly ModeloDeMotor Motor130 = new(
        "motor DC 130",
        Resistencia: 12.0,
        Ke: 0.005414, Kt: 0.005414,
        Inercia: 2.0e-7,
        AtritoSeco: 0.000300, AtritoDePartida: 0.000902, AtritoViscoso: 8.3e-8,
        TensaoNominal: 6.0, GirosNaTela: 5.0, CorrenteMaxima: 0.5);

    /// <summary>
    /// O motor amarelo TT de reducao 1:48 (Adafruit 3777), pelas medidas da
    /// propria Adafruit: 120 rpm/150 mA sem carga com 3 V, 250 rpm/160 mA com
    /// 6 V, 1,5 A travado com 6 V; 0,4 e 0,8 kg.cm de torque travado.
    /// </summary>
    public static readonly ModeloDeMotor MotorTT = new(
        "motor TT amarelo",
        Resistencia: 4.0,
        Ke: 0.20474, Kt: 0.05811,
        Inercia: 3.7e-4,
        AtritoSeco: 0.008246, AtritoDePartida: 0.01237, AtritoViscoso: 4.02e-5,
        TensaoNominal: 6.0, GirosNaTela: 4.0, CorrenteMaxima: 1.5);

    /// <summary>
    /// O brushless A2212 1000KV visto como um motor de escovas -- e assim que
    /// ele se comporta atras de um ESC, que faz a comutacao: 1000 rpm por
    /// volt, 90 mOhm entre fases, 0,5 A sem carga com 10 V.
    /// </summary>
    public static readonly ModeloDeMotor A2212 = new(
        "motor brushless A2212",
        Resistencia: 0.090,
        Ke: 60 / (2 * Math.PI * 1000), Kt: 60 / (2 * Math.PI * 1000),
        Inercia: 4.0e-6,
        AtritoSeco: 0.0008, AtritoDePartida: 0.0015, AtritoViscoso: 3.8e-6,
        TensaoNominal: 11.1, GirosNaTela: 2.2, CorrenteMaxima: 12);

    public static ModeloDeMotor De(string tipo) => tipo switch
    {
        "motor-dc" => Motor130,
        "motor-tt" => MotorTT,
        "motor-brushless" => A2212,
        _ => null,
    };

    /// <summary>
    /// O motor desta peca: a ficha escolhida, a digitada ("personalizado"),
    /// ou o padrao do tipo. O 130 e o A2212 escolhidos na lista sao os de
    /// sempre, com as medidas da bancada.
    /// </summary>
    public static ModeloDeMotor Da(PecaNoCircuito peca)
    {
        if (peca is null) return null;
        if (peca.Modelo == "personalizado" && peca.Ficha is { } propria && FichasDeMotor.Para(peca.Tipo).Length > 0)
            return propria.Ficha(peca.Tipo == "motor-brushless").Modelo() with { Nome = "motor personalizado" };
        if (peca.Modelo is { } id && id is not ("130" or "a2212-1000") && FichasDeMotor.Achar(peca.Tipo, id) is { } ficha)
            return ficha.Modelo();
        return De(peca.Tipo);
    }

    /// <summary>A tensao que tira o eixo parado do lugar, sem carga.</summary>
    public double TensaoDePartida => AtritoDePartida * Resistencia / Kt;

    /// <summary>A corrente com o eixo travado, nesta tensao.</summary>
    public double CorrenteTravado(double volts) => Math.Abs(volts) / Resistencia;
}

/// <summary>
/// O eixo de um motor de escovas: velocidade, angulo e corrente.
///
/// A conta e exata para uma tensao constante no passo, e nao um Euler: o
/// motor e uma equacao linear (J dw/dt = Kt (V - Ke w)/R - atrito), que tem
/// solucao fechada -- uma exponencial ate a velocidade de regime. Com isso
/// um quadro lento da tela (100 ms) da o mesmo resultado que dez rapidos,
/// e o brushless, que tem constante de tempo de 4 ms, nao explode.
///
/// O atrito seco e o que da o comportamento de bancada: parado, o eixo so
/// sai com a tensao de partida; girando, ele continua ate bem abaixo dela.
/// </summary>
public sealed class MotorDc
{
    public ModeloDeMotor Modelo { get; }

    public MotorDc(ModeloDeMotor modelo) => Modelo = modelo;

    /// <summary>rad/s no eixo de saida; positivo e horario, olhando a ponta do eixo.</summary>
    public double Velocidade { get; private set; }

    /// <summary>Volts nos fios na ultima conta (+ menos -).</summary>
    public double Tensao { get; private set; }

    /// <summary>Amperes do fio + para o fio - (negativo: o motor girando para tras, ou freando).</summary>
    public double Corrente { get; private set; }

    /// <summary>Graus girados de verdade, acumulados (positivo: horario).</summary>
    public double Angulo { get; private set; }

    /// <summary>
    /// Graus do desenho. Nao e o angulo de verdade: a tela tem 25 quadros
    /// por segundo, e um eixo a 9100 rpm da 6 voltas entre dois deles --
    /// desenhado no angulo certo, ele pareceria parado ou girando para tras.
    /// O desenho gira no sentido certo, e cada vez mais rapido ate um teto;
    /// o numero de rpm vai no rotulo.
    /// </summary>
    public double AnguloNaTela { get; private set; }

    public double Rpm => Velocidade * 60 / (2 * Math.PI);

    /// <summary>A forca contra-eletromotriz de agora: o que o motor devolve ao circuito.</summary>
    public double ForcaContraEletromotriz => Modelo.Ke * Velocidade;

    /// <summary>Torque extra contra o giro, em N.m: a roda no chao, a helice no ar.</summary>
    public double Carga { get; set; }

    /// <summary>Inercia extra no eixo, em kg.m2: a roda ou a helice presa nele.</summary>
    public double InerciaExtra { get; set; }

    /// <summary>Torque contra o giro que cresce com o quadrado da velocidade (a helice), em N.m por (rad/s)^2.</summary>
    public double CargaQuadratica { get; set; }

    public void Zerar()
    {
        Velocidade = Tensao = Corrente = Angulo = AnguloNaTela = 0;
    }

    /// <summary>Avanca o eixo com esta tensao nos fios, por estes segundos.</summary>
    public void Avancar(double volts, double segundos)
    {
        Tensao = volts;
        if (segundos > 0)
        {
            double antes = Angulo;
            // Com helice o arrasto muda com a velocidade: passos de 5 ms, cada
            // um com o arrasto linearizado na velocidade do comeco dele.
            if (CargaQuadratica > 0)
                for (double feito = 0; feito < segundos - 1e-12;)
                {
                    double passo = Math.Min(0.005, segundos - feito);
                    Integrar(volts, passo);
                    feito += passo;
                }
            else Integrar(volts, segundos);
            double giros = (Angulo - antes) / 360 / segundos;
            AnguloNaTela = (AnguloNaTela + GirosNaTela(giros, Modelo.GirosNaTela) * 360 * segundos) % 360;
        }
        Corrente = (volts - Modelo.Ke * Velocidade) / Modelo.Resistencia;
    }

    /// <summary>
    /// Giros por segundo do desenho: iguais aos de verdade ate 3 por segundo
    /// (da para contar as voltas do amarelo), comprimidos acima disso e com
    /// teto -- o alvo de tacometro do motor 130 tem um risco so, e passado
    /// do teto ele pareceria andar para tras.
    /// </summary>
    public static double GirosNaTela(double giros, double teto)
    {
        double g = Math.Abs(giros);
        if (g > 3) g = 3 + 0.8 * Math.Log(g / 3);
        return Math.Sign(giros) * Math.Min(g, teto);
    }

    void Integrar(double volts, double segundos)
    {
        var m = Modelo;
        double inercia = m.Inercia + InerciaExtra;
        // O amortecimento: o eletrico (a forca contra-eletromotriz), o viscoso e
        // o da helice (k w^2 linearizado em k |w| w).
        double a = (m.Kt * m.Ke / m.Resistencia + m.AtritoViscoso + CargaQuadratica * Math.Abs(Velocidade)) / inercia;
        double torqueParado = m.Kt * volts / m.Resistencia;
        double atritoSeco = m.AtritoSeco + Carga;
        double atritoDePartida = m.AtritoDePartida + Carga;

        double resta = segundos;
        for (int volta = 0; volta < 4 && resta > 1e-12; volta++)
        {
            int sentido;
            if (Velocidade == 0)
            {
                // Parado: so sai com mais torque que o atrito de partida.
                if (Math.Abs(torqueParado) <= atritoDePartida) return;
                sentido = Math.Sign(torqueParado);
            }
            else sentido = Math.Sign(Velocidade);

            // Num sentido so, a equacao e linear: dw/dt = c - a w.
            double c = (torqueParado - sentido * atritoSeco) / inercia;
            double alvo = c / a;
            double w0 = Velocidade;

            // O regime esta do outro lado do zero: o eixo para no meio do
            // passo, e dali o atrito de partida decide (fica, ou volta).
            if (sentido * alvo < 0)
            {
                double tZero = Math.Log((w0 - alvo) / -alvo) / a;
                if (tZero < resta)
                {
                    Angulo += Graus(alvo * tZero + (w0 - alvo) * (1 - Math.Exp(-a * tZero)) / a);
                    Velocidade = 0;
                    resta -= tZero;
                    continue;
                }
            }

            double e = Math.Exp(-a * resta);
            Angulo += Graus(alvo * resta + (w0 - alvo) * (1 - e) / a);
            Velocidade = alvo + (w0 - alvo) * e;
            // Decaindo para zero sem cruzar (sem tensao e sem atrito seco):
            // abaixo de um centesimo de rpm, parou.
            if (Math.Abs(Velocidade) < 1e-3 && Math.Abs(alvo) < 1e-3) Velocidade = 0;
            resta = 0;
        }
    }

    static double Graus(double radianos) => radianos * 180 / Math.PI;
}

/// <summary>
/// O motor brushless (A2212 1000KV, 7 pares de polos), por dois caminhos.
///
/// Atras de um ESC ele e um motor de escovas com o ESC fazendo a comutacao,
/// e a conta e a do <see cref="MotorDc"/> com a tensao media que o ESC
/// entrega (<see cref="Acelerar"/>).
///
/// Pelas fases, sem ESC, o rotor segue o campo: as tres correntes viram um
/// vetor (a transformada de Clarke), e o ima do rotor vai para onde ele
/// aponta -- desde que a corrente de para vencer o atrito de partida. E o
/// que faz a sequencia de seis passos (ou a senoide do SimpleFOC) girar o
/// motor, a sequencia invertida girar para tras e duas fases trocadas
/// inverterem o sentido. Direto na placa a corrente e de miliamperes, e o
/// rotor nem se mexe -- como na bancada.
/// </summary>
public sealed class MotorSemEscova
{
    /// <summary>O A2212 de sempre.</summary>
    public MotorSemEscova() : this(ModeloDeMotor.A2212) { }

    public MotorSemEscova(ModeloDeMotor modelo) => Equivalente = new MotorDc(modelo);

    public int ParesDePolos => Math.Max(1, Equivalente.Modelo.ParesDePolos);

    /// <summary>Ohms de cada fase ate o centro da estrela (a metade da resistencia entre fases).</summary>
    public static double ResistenciaDeFaseDe(ModeloDeMotor modelo) => modelo.Resistencia / 2;

    public MotorDc Equivalente { get; }

    /// <summary>Graus mecanicos do rotor, acumulados.</summary>
    public double Angulo { get; private set; }

    public double AnguloNaTela { get; private set; }

    /// <summary>rad/s, pelo caminho que estiver valendo.</summary>
    public double Velocidade { get; private set; }

    public double Rpm => Velocidade * 60 / (2 * Math.PI);

    /// <summary>A corrente de fase (amplitude) da ultima conta pelas fases.</summary>
    public double CorrenteDeFase { get; private set; }

    /// <summary>Angulo eletrico do campo de antes, em graus (NaN: sem campo).</summary>
    double campoAnterior = double.NaN;

    public void Zerar()
    {
        Equivalente.Zerar();
        Angulo = AnguloNaTela = Velocidade = CorrenteDeFase = 0;
        campoAnterior = double.NaN;
    }

    /// <summary>
    /// Pelo ESC: a tensao media que ele poe no motor (o acelerador vezes a
    /// bateria). O angulo segue o eixo do motor equivalente.
    /// </summary>
    public void Acelerar(double volts, double segundos)
    {
        double antes = Equivalente.Angulo;
        Equivalente.Avancar(volts, segundos);
        Angulo += Equivalente.Angulo - antes;
        Velocidade = Equivalente.Velocidade;
        AvancarTela(Equivalente.Angulo - antes, segundos);
        campoAnterior = double.NaN;
    }

    /// <summary>
    /// Pelas fases: as correntes que entram em A, B e C (amperes) neste
    /// passo. O rotor segue o campo pelo caminho mais curto; campo virado do
    /// avesso (180 graus eletricos) nao tem lado para ir, e o rotor treme.
    /// </summary>
    public void SeguirCampo(double ia, double ib, double ic, double segundos)
    {
        // Clarke, com a amplitude preservada.
        double alfa = (2 * ia - ib - ic) / 3;
        double beta = (ib - ic) / Math.Sqrt(3);
        CorrenteDeFase = Math.Sqrt(alfa * alfa + beta * beta);

        double antes = Angulo;
        var m = Equivalente.Modelo;
        // O Kt do modelo e o de corrente continua (seis passos: a corrente
        // passa por duas fases, e o vetor dela tem 2/raiz(3) da amplitude).
        if (CorrenteDeFase * m.Kt * Math.Sqrt(3) / 2 > m.AtritoDePartida)
        {
            double campo = Math.Atan2(beta, alfa) * 180 / Math.PI;
            if (!double.IsNaN(campoAnterior))
            {
                double passo = ((campo - campoAnterior) % 360 + 540) % 360 - 180;
                if (Math.Abs(Math.Abs(passo) - 180) > 2)
                    Angulo += passo / ParesDePolos;
            }
            campoAnterior = campo;
        }
        // Sem corrente para mover o rotor, ele fica alinhado ao ultimo campo
        // forte: quando a corrente voltar, ele sai dali pelo caminho mais curto.

        // Pelas fases o rotor anda aos saltos (um passo num quadro, nada no
        // outro): a velocidade do rotulo e a media do ultimo quarto de segundo.
        if (segundos > 0)
        {
            double agora = (Angulo - antes) * Math.PI / 180 / segundos;
            Velocidade += (agora - Velocidade) * (1 - Math.Exp(-segundos / 0.25));
            if (Math.Abs(Velocidade) < 0.01 && agora == 0) Velocidade = 0;
        }
        AvancarTela(Angulo - antes, segundos);
    }

    /// <summary>
    /// O desenho anda o que o rotor andou neste passo -- devagar, salto por
    /// salto, como o motor de passo; rapido, comprimido como o do MotorDc.
    /// </summary>
    void AvancarTela(double graus, double segundos)
    {
        if (segundos <= 0) return;
        double giros = graus / 360 / segundos;
        AnguloNaTela = (AnguloNaTela
                        + MotorDc.GirosNaTela(giros, Equivalente.Modelo.GirosNaTela) * 360 * segundos) % 360;
    }
}
