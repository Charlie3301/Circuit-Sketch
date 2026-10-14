namespace Simulador.App;

/// <summary>
/// O que cada peca faz na simulacao, como dado em vez de codigo.
///
/// Antes cada componente exigia um "case" novo no editor. Com a tabela,
/// um sensor analogico e uma linha: quais pinos devolvem leitura, quais
/// devolvem nivel logico e como se chama o controle na tab.
/// </summary>
public record Comportamento
{
    /// <summary>Pinos que devolvem leitura analogica (0..MaxAdc da placa).</summary>
    public string[] Analogicos { get; init; } = [];

    /// <summary>Pinos que devolvem nivel logico conforme o valor passa do limiar.</summary>
    public string[] Digitais { get; init; } = [];

    /// <summary>Pinos que a placa aciona e a peca mostra (LED, segmento).</summary>
    public string[] Saidas { get; init; } = [];

    /// <summary>Modulo com saida ativa em LOW, como a maioria dos sensores.</summary>
    public bool DigitalInvertido { get; init; }

    /// <summary>Rotulo do controle na tab do objeto.</summary>
    public string Rotulo { get; init; } = "Valor";

    /// <summary>Controle de liga/desliga em vez de deslizante.</summary>
    public bool Chave { get; init; }

    /// <summary>Acima disso o pino digital considera "detectado".</summary>
    public double Limiar { get; init; } = 0.5;

    /// <summary>
    /// Grandeza que a peca expoe para a biblioteca ler. Quando comeca
    /// com um prefixo ("mpu:", "hx711:"), e de barramento e nao depende
    /// de pino; senao a chave e "pino:grandeza".
    /// </summary>
    public string Grandeza { get; init; }
    /// <summary>Pino que identifica a grandeza, quando ela tem pino.</summary>
    public string PinoDaGrandeza { get; init; }
    public double Minimo { get; init; }
    public double Maximo { get; init; } = 100;
    public string Unidade { get; init; } = "";

    /// <summary>Valor real a partir da posicao 0..1 do controle.</summary>
    public double ValorReal(double giro) => Escala?.Invoke(giro) ?? Minimo + (Maximo - Minimo) * giro;

    /// <summary>
    /// A grandeza a partir do controle, quando ela nao anda em linha reta:
    /// a luz vai em decadas de lux, de 0,1 (noite fechada) a 100 mil (sol).
    /// </summary>
    public Func<double, double> Escala { get; init; }

    /// <summary>
    /// Que fracao da alimentacao sai no pino analogico, a partir do
    /// controle. Sem ela a saida e o proprio controle -- o potenciometro e
    /// o deslizante sao exatamente isso.
    /// </summary>
    public Func<double, double> Saida { get; init; }

    /// <summary>A leitura do conversor, de 0 ao maximo da placa.</summary>
    public int Leitura(double giro, int maxAdc)
        => (int)Math.Round(Math.Clamp(Saida?.Invoke(giro) ?? giro, 0, 1) * maxAdc);

    /// <summary>A posicao do controle de luz em lux: de 0,1 a 100 mil, em decadas.</summary>
    static double Lux(double giro) => Math.Pow(10, Math.Clamp(giro, 0, 1) * 6 - 1);

    /// <summary>
    /// A resistencia do LDR solto com a luz do controle: 50 kohm a 10 lux e
    /// gama 0,7 (as constantes do fotorresistor do Wokwi, RL10 50 e GAMMA
    /// 0.7, as mesmas do modulo abaixo) -- ~1,3 Mohm no escuro (0,1 lux) e
    /// ~80 ohm no sol forte (100 mil lux).
    /// </summary>
    public static double ResistenciaDoLdr(double giro)
        => 50_000 * Math.Pow(10, 0.7) / Math.Pow(Lux(giro), 0.7);

    /// <summary>
    /// O divisor do modulo de LDR: o LDR (50 kohm a 10 lux, gama 0,7) contra
    /// 10 kohm. Sao as constantes do exemplo do Wokwi -- GAMMA 0.7 e RL10
    /// 50 --, entao a conta que a turma copia devolve a luz daqui.
    /// </summary>
    static double FracaoDoLdr(double lux)
    {
        double resistencia = 50_000 * Math.Pow(10, 0.7) / Math.Pow(lux, 0.7);
        return resistencia / (resistencia + 10_000);
    }

    /// <summary>
    /// O NTC do modulo, beta 3950, num divisor com 10 kohm: a fracao que a
    /// formula de todo tutorial ("1 / (log(1 / (1023. / leitura - 1)) /
    /// BETA + 1.0 / 298.15) - 273.15") transforma de volta nesta temperatura.
    /// </summary>
    static double FracaoDoNtc(double celsius)
    {
        double e = Math.Exp(3950 * (1 / (celsius + 273.15) - 1 / 298.15));
        return e / (e + 1);
    }

    // ---------------------------------------------------------------
    static readonly Dictionary<string, Comportamento> tabela = new()
    {
        // ---- sensores com saida analogica e digital ----
        // O modulo de LDR: MAIS luz, MENOS tensao no AO -- o LDR fica entre
        // o AO e o terra, no modulo de verdade e no do Wokwi. Ate a Alpha 19
        // era o contrario aqui, e o "escuro e leitura alta" do simulador
        // saia invertido na bancada.
        ["photoresistor-sensor"] = new()
        {
            Analogicos = ["AO"], Digitais = ["DO"],
            DigitalInvertido = true, Rotulo = "Luz", Unidade = "lux",
            Escala = Lux, Saida = giro => FracaoDoLdr(Lux(giro)),
        },

        // O LDR solto, de duas pernas, e um RESISTOR que muda com a luz
        // (ResistenciaDoLdr): a leitura sai do divisor que a pessoa montou,
        // pela conta de energia -- com o LDR em cima, mais luz e leitura
        // maior; embaixo, menor; sem o resistor do divisor, nada. Antes o pino
        // ligado a qualquer perna lia a luz direto, montado como fosse.
        ["ldr"] = new()
        {
            Grandeza = "ldr:luz", Rotulo = "Luz", Unidade = "lux", Escala = Lux,
        },
        ["gas-sensor"] = new()
        {
            Analogicos = ["AOUT"], Digitais = ["DOUT"],
            DigitalInvertido = true, Rotulo = "Gas"
        },
        ["flame-sensor"] = new()
        {
            Analogicos = ["AOUT"], Digitais = ["DOUT"],
            DigitalInvertido = true, Rotulo = "Chama"
        },
        // O KY-037 e o KY-038 (os do kit da Joy-IT): o comparador poe o DOUT
        // em HIGH com o som acima do limiar (a documentacao da Joy-IT).
        ["big-sound-sensor"] = new()
        {
            Analogicos = ["AOUT"], Digitais = ["DOUT"],
            DigitalInvertido = false, Rotulo = "Som"
        },
        ["small-sound-sensor"] = new()
        {
            Analogicos = ["AOUT"], Digitais = ["DOUT"],
            DigitalInvertido = false, Rotulo = "Som"
        },

        // ---- so analogico ----
        // O controle e a TEMPERATURA, de -24 a 80 graus como no Wokwi, e a
        // leitura segue a curva do NTC. Antes era uma reta, e a formula do
        // tutorial dava uma temperatura que o controle nao mostrava.
        ["ntc-temperature-sensor"] = new()
        {
            Analogicos = ["OUT"], Rotulo = "Temperatura",
            Minimo = -24, Maximo = 80, Unidade = "°C",
            Saida = giro => FracaoDoNtc(-24 + 104 * giro),
        },
        ["heart-beat-sensor"] = new() { Analogicos = ["OUT"], Rotulo = "Pulso" },
        ["slide-potentiometer"] = new() { Analogicos = ["SIG"], Rotulo = "Posicao" },

        // ---- so digital ----
        ["tilt-switch"] = new() { Digitais = ["OUT"], Chave = true, Rotulo = "Inclinado" },
        ["pir-motion-sensor"] = new() { Digitais = ["OUT"], Chave = true, Rotulo = "Movimento" },
        ["slide-switch"] = new() { Digitais = ["1", "2", "3"], Chave = true, Rotulo = "Ligada" },

        // ---- saidas ----
        ["rgb-led"] = new() { Saidas = ["R", "G", "B"] },
        ["led-bar-graph"] = new()
        {
            Saidas = ["A1", "A2", "A3", "A4", "A5", "A6", "A7", "A8", "A9", "A10"]
        },
        ["7segment"] = new() { Saidas = ["A", "B", "C", "D", "E", "F", "G", "DP"] },
        ["ks2e-m-dc5"] = new() { Saidas = ["COIL1", "COIL2"], Rotulo = "Rele" },

        // ---- grandezas lidas por biblioteca ----
        // Nao ha pino "analogico": quem le e o objeto do sketch
        // (dht.readTemperature, sonar via pulseIn...). O controle da tab
        // define o valor, e a Maquina guarda em Leituras.
        ["hc-sr04"] = new()
        {
            Grandeza = "distancia", PinoDaGrandeza = "ECHO",
            Rotulo = "Distancia", Maximo = 400, Unidade = "cm"
        },
        ["dht22"] = new()
        {
            Grandeza = "temperatura", PinoDaGrandeza = "SDA",
            Rotulo = "Temperatura", Minimo = -40, Maximo = 80, Unidade = "C"
        },
        ["dht11"] = new()
        {
            Grandeza = "temperatura", PinoDaGrandeza = "DATA",
            Rotulo = "Temperatura", Minimo = 0, Maximo = 50, Unidade = "C"
        },
        ["hx711"] = new()
        {
            Grandeza = "hx711:peso", Rotulo = "Peso", Maximo = 5000, Unidade = "g"
        },
        ["mpu6050"] = new()
        {
            Grandeza = "mpu:ax", Rotulo = "Inclinacao",
            Minimo = -16384, Maximo = 16384, Unidade = ""
        },
        ["membrane-keypad"] = new() { Grandeza = "keypad:tecla", Rotulo = "Tecla" },
        ["ir-remote"] = new() { Grandeza = "ir:codigo", Rotulo = "Codigo IR", Maximo = 255 },
        ["clock-generator"] = new()
        {
            Grandeza = "clock:hz", Rotulo = "Frequencia",
            Minimo = 0.5, Maximo = 20, Unidade = "Hz"
        },
    };

    public static Comportamento De(string tipo) => tabela.GetValueOrDefault(tipo);

    /// <summary>Peca com controle na tab (o usuario mexe nela).</summary>
    public bool TemControle => Analogicos.Length > 0 || Digitais.Length > 0
                               || Grandeza is not null;

    public static IReadOnlyCollection<string> Cobertos => tabela.Keys;
}
