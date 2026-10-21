namespace Simulador.App;

public sealed class Logica
{
    public const int Indefinido = -1;

    static readonly string[] Portas =
    [
        "not-gate", "and-gate", "or-gate", "xor-gate",
        "nand-gate", "nor-gate", "xnor-gate", "mux-2",
        // Alpha 25: o buffer (o do Wokwi), o tri-state e as de tres entradas.
        "buffer-gate", "buffer-tristate",
        "and-gate-3", "nand-gate-3", "or-gate-3", "nor-gate-3", "xor-gate-3", "xnor-gate-3",
    ];

    static readonly string[] Sequenciais =
    [
        "flip-flop-d", "flip-flop-sr", "flip-flop-dsr", "clock-generator",
        "flip-flop-jk", "flip-flop-t", "latch-sr", "latch-d",
    ];

    /// <summary>Uma porta de dentro de um chip: a funcao, as entradas e a saida.</summary>
    sealed record PortaDoChip(string Funcao, string[] Entradas, string Saida);

    static PortaDoChip[] Quatro(string funcao)
        => [.. Enumerable.Range(1, 4).Select(k => new PortaDoChip(funcao, [$"{k}A", $"{k}B"], $"{k}Y"))];

    static PortaDoChip[] Tres(string funcao)
        => [.. Enumerable.Range(1, 3).Select(k => new PortaDoChip(funcao, [$"{k}A", $"{k}B", $"{k}C"], $"{k}Y"))];

    static PortaDoChip[] Duas(string funcao)
        => [.. Enumerable.Range(1, 2).Select(k => new PortaDoChip(funcao, [$"{k}A", $"{k}B", $"{k}C", $"{k}D"], $"{k}Y"))];

    static PortaDoChip[] Seis(string funcao)
        => [.. Enumerable.Range(1, 6).Select(k => new PortaDoChip(funcao, [$"{k}A"], $"{k}Y"))];

    /// <summary>
    /// Os chips 74HC (Alpha 25): cada um e um punhado de portas, com os nomes
    /// dos pinos da ficha. O 74HC74 e o 74HC157 tem conta propria (abaixo).
    /// </summary>
    static readonly Dictionary<string, PortaDoChip[]> Chips = new()
    {
        ["74hc00"] = Quatro("nand-gate"),
        ["74hc02"] = Quatro("nor-gate"),
        ["74hc08"] = Quatro("and-gate"),
        ["74hc32"] = Quatro("or-gate"),
        ["74hc86"] = Quatro("xor-gate"),
        ["74hc7266"] = Quatro("xnor-gate"),
        ["74hc04"] = Seis("not-gate"),
        // O Schmitt-trigger so muda o limiar (a histerese); no nivel logico e um NOT.
        ["74hc14"] = Seis("not-gate"),
        ["74hc10"] = Tres("nand-gate"),
        ["74hc11"] = Tres("and-gate"),
        ["74hc27"] = Tres("nor-gate"),
        ["74hc20"] = Duas("nand-gate"),
        ["74hc21"] = Duas("and-gate"),
        ["74hc74"] = [],
        ["74hc125"] = [],
        ["74hc157"] = [],
    };

    /// <summary>Um chip 74HC de verdade: so funciona com o VCC e o GND ligados.</summary>
    public static bool EhChip(string tipo) => tipo is not null && Chips.ContainsKey(tipo);

    public static bool EhPecaDeLogica(string tipo)
        => Portas.Contains(tipo) || Sequenciais.Contains(tipo) || EhChip(tipo);

    public static bool TemAlgumaNoCircuito(Circuito circuito)
        => circuito is not null && circuito.Pecas.Any(p => EhPecaDeLogica(p.Tipo));

    readonly Dictionary<string, int> nivelPorRaiz = [];
    readonly Dictionary<(string Peca, string Pino), int> saidas = [];
    Netlist rede;
    Circuito circuito;

    /// <summary>
    /// O nivel que cada peca de logica poe em cada saida dela, nesta conta. A
    /// conta de energia faz delas fontes: a porta ideal no nivel da placa, o
    /// chip pelo VCC ou pelo GND dele -- e e assim que a saida acende um LED.
    /// </summary>
    public IReadOnlyDictionary<(string Peca, string Pino), int> Saidas => saidas;

    public int NivelDoNo(string raiz)
        => raiz is not null && nivelPorRaiz.TryGetValue(raiz, out int n) ? n : Indefinido;

    public int NivelDoPino(string peca, string pino)
        => NivelDoNo(rede?.RaizDe(Netlist.No(peca, pino)));

    public IReadOnlyDictionary<string, int> Niveis => nivelPorRaiz;

    public static Logica Avaliar(Circuito circuito, Netlist rede, Bancada bancada,
                                 Dictionary<string, int> memoria, long tempoMs)
    {
        var logica = new Logica { circuito = circuito, rede = rede };
        if (circuito is null || rede is null) return logica;

        logica.Semear(bancada);

        for (int volta = 0; volta < 6; volta++)
        {
            bool mudou = false;
            foreach (var peca in circuito.Pecas)
            {
                if (!EhPecaDeLogica(peca.Tipo)) continue;
                mudou |= logica.Passo(peca, memoria, tempoMs);
            }
            if (!mudou) break;
        }

        return logica;
    }

    void Semear(Bancada bancada)
    {
        foreach (var peca in circuito.Pecas)
        {
            var def = Catalogo.Obter(peca.Tipo);
            if (def is null) continue;

            bool fonte = Netlist.EhFonte(peca.Tipo);
            var perfil = PerfilPlaca.De(peca.Tipo);
            var maq = perfil is not null ? bancada?.De(peca.Id) : null;

            foreach (var pino in def.Pinos)
            {
                if (fonte && Netlist.EhPinoDeTerra(pino.Nome)) Definir(peca.Id, pino.Nome, 0);
                else if (fonte && Netlist.EhPinoDeAlimentacao(pino.Nome)) Definir(peca.Id, pino.Nome, 1);

                if (maq is null || !maq.Energizada) continue;

                int gpio = perfil.Numero(pino.Nome);
                if (gpio < 0 || gpio >= Maquina.NumPinos) continue;
                if (maq.Modo[gpio] != 1) continue;

                Definir(peca.Id, pino.Nome, maq.Pwm[gpio] > 127 || maq.Digital[gpio] == 1 ? 1 : 0);
            }
        }
    }

    bool Definir(string peca, string pino, int nivel)
    {
        string raiz = rede.RaizDe(Netlist.No(peca, pino));
        if (raiz is null) return false;

        if (nivelPorRaiz.TryGetValue(raiz, out int antes) && antes == nivel) return false;

        nivelPorRaiz[raiz] = nivel;
        return true;
    }

    int Ler(string peca, string pino) => NivelDoPino(peca, pino);

    /// <summary>A peca poe este nivel nesta saida (e a conta de energia fica sabendo).</summary>
    bool Sair(string peca, string pino, int nivel)
    {
        saidas[(peca, pino)] = nivel;
        return Definir(peca, pino, nivel);
    }

    /// <summary>A conta de uma porta (de uma a quatro entradas), ou indefinido com alguma entrada solta.</summary>
    static int Porta(string funcao, params int[] e)
    {
        if (e.Length == 0 || e.Contains(Indefinido)) return Indefinido;
        int uns = e.Count(v => v == 1);
        return funcao switch
        {
            "not-gate" => 1 - e[0],
            "buffer-gate" => e[0],
            "and-gate" => uns == e.Length ? 1 : 0,
            "nand-gate" => uns == e.Length ? 0 : 1,
            "or-gate" => uns > 0 ? 1 : 0,
            "nor-gate" => uns > 0 ? 0 : 1,
            "xor-gate" => uns % 2,
            _ => 1 - uns % 2,
        };
    }

    bool Passo(PecaNoCircuito peca, Dictionary<string, int> memoria, long tempoMs)
    {
        if (EhChip(peca.Tipo)) return PassoDoChip(peca, memoria);

        int a = Ler(peca.Id, "A"), b = Ler(peca.Id, "B");

        switch (peca.Tipo)
        {
            case "not-gate":
            case "buffer-gate":
            {
                int q = Porta(peca.Tipo, a);
                return q != Indefinido && Sair(peca.Id, "Q", q);
            }

            case "and-gate":
            case "nand-gate":
            case "or-gate":
            case "nor-gate":
            case "xor-gate":
            case "xnor-gate":
            {
                int q = Porta(peca.Tipo, a, b);
                return q != Indefinido && Sair(peca.Id, "Q", q);
            }

            // As de tres entradas: a mesma conta, com o C.
            case "and-gate-3":
            case "nand-gate-3":
            case "or-gate-3":
            case "nor-gate-3":
            case "xor-gate-3":
            case "xnor-gate-3":
            {
                int q = Porta(peca.Tipo[..^2], a, b, Ler(peca.Id, "C"));
                return q != Indefinido && Sair(peca.Id, "Q", q);
            }

            // O tri-state so poe nivel com o EN em HIGH; senao a saida fica
            // solta, e outra peca pode mandar no mesmo fio (o barramento).
            case "buffer-tristate":
                return Ler(peca.Id, "EN") == 1 && a != Indefinido && Sair(peca.Id, "Q", a);

            case "mux-2":
            {
                int sel = Ler(peca.Id, "SEL");
                if (sel == Indefinido) return false;

                int escolhido = sel == 1 ? b : a;
                return escolhido != Indefinido && Sair(peca.Id, "Q", escolhido);
            }

            case "clock-generator":
            {
                double hz = 0.5 + peca.Giro * 19.5;
                long meio = (long)Math.Max(20, 500 / hz);
                int nivel = tempoMs / meio % 2 == 0 ? 1 : 0;

                memoria[peca.Id + ":q"] = nivel;
                return Sair(peca.Id, "CLK", nivel) | Definir(peca.Id, "NQ", 1 - nivel);
            }

            case "flip-flop-d":
            case "flip-flop-sr":
            case "flip-flop-dsr":
            case "flip-flop-jk":
            case "flip-flop-t":
            case "latch-sr":
            case "latch-d":
                return Sequencial(peca, memoria);
        }

        return false;
    }

    /// <summary>
    /// Um chip 74HC. Sem o VCC em HIGH e o GND em LOW ele nao faz nada -- as
    /// saidas ficam soltas, como no chip de verdade sem alimentacao. Entrada
    /// solta deixa a saida solta: no CMOS ela flutua, e a ficha manda ligar
    /// toda entrada sem uso. Vale tambem para os pinos de controle -- o PRE e o
    /// CLR do 74HC74, o G do 74HC157, o OE do 74HC125.
    /// </summary>
    bool PassoDoChip(PecaNoCircuito peca, Dictionary<string, int> memoria)
    {
        if (Ler(peca.Id, "VCC") != 1 || Ler(peca.Id, "GND") != 0) return false;
        bool mudou = false;

        foreach (var porta in Chips[peca.Tipo])
        {
            int q = Porta(porta.Funcao, [.. porta.Entradas.Select(e => Ler(peca.Id, e))]);
            if (q != Indefinido) mudou |= Sair(peca.Id, porta.Saida, q);
        }

        // O 74HC125: cada buffer passa o A com o OE em LOW; em HIGH a saida se solta.
        if (peca.Tipo == "74hc125")
            for (int k = 1; k <= 4; k++)
            {
                int a = Ler(peca.Id, $"{k}A");
                if (Ler(peca.Id, $"{k}OE") == 0 && a != Indefinido) mudou |= Sair(peca.Id, $"{k}Y", a);
            }

        if (peca.Tipo == "74hc157")
        {
            int s = Ler(peca.Id, "S"), g = Ler(peca.Id, "G");
            for (int k = 1; k <= 4; k++)
            {
                int y = g == 1 ? 0 : g == Indefinido || s == Indefinido ? Indefinido : Ler(peca.Id, s == 1 ? $"{k}B" : $"{k}A");
                if (y != Indefinido) mudou |= Sair(peca.Id, $"{k}Y", y);
            }
        }

        if (peca.Tipo == "74hc74")
        {
            for (int u = 1; u <= 2; u++)
            {
                string chaveQ = $"{peca.Id}:{u}q", chaveClk = $"{peca.Id}:{u}clk";
                int q = memoria.GetValueOrDefault(chaveQ, 0);
                int clk = Ler(peca.Id, $"{u}CLK");
                bool subiu = clk == 1 && memoria.GetValueOrDefault(chaveClk, 0) != 1;
                if (clk != Indefinido) memoria[chaveClk] = clk;

                // PRE e CLR sao ativos em LOW; os dois juntos poem Q e Q barrado em HIGH.
                // Algum deles solto: o flip-flop fica sem estado certo, e as saidas, soltas.
                int npre = Ler(peca.Id, $"{u}PRE"), nclr = Ler(peca.Id, $"{u}CLR");
                if (npre == Indefinido || nclr == Indefinido) continue;
                bool pre = npre == 0, clr = nclr == 0;
                int d = Ler(peca.Id, $"{u}D");
                if (pre && clr)
                {
                    memoria[chaveQ] = 1;
                    mudou |= Sair(peca.Id, $"{u}Q", 1) | Sair(peca.Id, $"{u}NQ", 1);
                    continue;
                }
                if (pre) q = 1;
                else if (clr) q = 0;
                else if (subiu && d != Indefinido) q = d;

                memoria[chaveQ] = q;
                mudou |= Sair(peca.Id, $"{u}Q", q) | Sair(peca.Id, $"{u}NQ", 1 - q);
            }
        }

        return mudou;
    }

    bool Sequencial(PecaNoCircuito peca, Dictionary<string, int> memoria)
    {
        string chaveQ = peca.Id + ":q", chaveClk = peca.Id + ":clk";

        int q = memoria.GetValueOrDefault(chaveQ, 0);
        int clk = Ler(peca.Id, "CLK");
        int antes = memoria.GetValueOrDefault(chaveClk, 0);

        bool subiu = clk == 1 && antes != 1;
        if (clk != Indefinido) memoria[chaveClk] = clk;

        int s = Ler(peca.Id, "S"), r = Ler(peca.Id, "R"), d = Ler(peca.Id, "D");

        if (peca.Tipo == "latch-sr")
        {
            // O latch de duas NOR: S e R em HIGH juntos poem as duas saidas em LOW.
            if (s == 1 && r == 1)
            {
                memoria[chaveQ] = 0;
                return Sair(peca.Id, "Q", 0) | Sair(peca.Id, "NQ", 0);
            }
            if (s == 1) q = 1;
            else if (r == 1) q = 0;
        }
        else if (peca.Tipo == "latch-d")
        {
            if (Ler(peca.Id, "EN") == 1 && d != Indefinido) q = d;
        }
        else if (peca.Tipo == "flip-flop-jk")
        {
            int j = Ler(peca.Id, "J"), k = Ler(peca.Id, "K");
            if (subiu && j != Indefinido && k != Indefinido)
                q = (j, k) switch { (1, 1) => 1 - q, (1, 0) => 1, (0, 1) => 0, _ => q };
        }
        else if (peca.Tipo == "flip-flop-t")
        {
            if (subiu && Ler(peca.Id, "T") == 1) q = 1 - q;
        }
        else if (peca.Tipo == "flip-flop-dsr")
        {
            if (s == 1) q = 1;
            else if (r == 1) q = 0;
            else if (subiu && d != Indefinido) q = d;
        }
        else if (peca.Tipo == "flip-flop-sr")
        {
            if (subiu)
            {
                if (s == 1 && r == 1) q = 1 - q;
                else if (s == 1) q = 1;
                else if (r == 1) q = 0;
            }
        }
        else if (subiu && d != Indefinido)
        {
            q = d;
        }

        memoria[chaveQ] = q;
        return Sair(peca.Id, "Q", q) | Sair(peca.Id, "NQ", 1 - q);
    }
}
