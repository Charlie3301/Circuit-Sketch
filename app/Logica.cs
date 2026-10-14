namespace Simulador.App;

public sealed class Logica
{
    public const int Indefinido = -1;

    static readonly string[] Portas =
    [
        "not-gate", "and-gate", "or-gate", "xor-gate",
        "nand-gate", "nor-gate", "xnor-gate", "mux-2",
    ];

    static readonly string[] Sequenciais =
    [
        "flip-flop-d", "flip-flop-sr", "flip-flop-dsr", "clock-generator",
    ];

    public static bool EhPecaDeLogica(string tipo)
        => Portas.Contains(tipo) || Sequenciais.Contains(tipo);

    public static bool TemAlgumaNoCircuito(Circuito circuito)
        => circuito is not null && circuito.Pecas.Any(p => EhPecaDeLogica(p.Tipo));

    readonly Dictionary<string, int> nivelPorRaiz = [];
    Netlist rede;
    Circuito circuito;

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

    bool Passo(PecaNoCircuito peca, Dictionary<string, int> memoria, long tempoMs)
    {
        int a = Ler(peca.Id, "A"), b = Ler(peca.Id, "B");

        switch (peca.Tipo)
        {
            case "not-gate":
                return a == Indefinido || Definir(peca.Id, "Q", a == 1 ? 0 : 1);

            case "and-gate":
            case "nand-gate":
            case "or-gate":
            case "nor-gate":
            case "xor-gate":
            case "xnor-gate":
            {
                if (a == Indefinido || b == Indefinido) return false;

                int q = peca.Tipo switch
                {
                    "and-gate" => a & b,
                    "nand-gate" => 1 - (a & b),
                    "or-gate" => a | b,
                    "nor-gate" => 1 - (a | b),
                    "xor-gate" => a ^ b,
                    _ => 1 - (a ^ b),
                };
                return Definir(peca.Id, "Q", q);
            }

            case "mux-2":
            {
                int sel = Ler(peca.Id, "SEL");
                if (sel == Indefinido) return false;

                int escolhido = sel == 1 ? b : a;
                return escolhido != Indefinido && Definir(peca.Id, "Q", escolhido);
            }

            case "clock-generator":
            {
                double hz = 0.5 + peca.Giro * 19.5;
                long meio = (long)Math.Max(20, 500 / hz);
                int nivel = tempoMs / meio % 2 == 0 ? 1 : 0;

                memoria[peca.Id + ":q"] = nivel;
                return Definir(peca.Id, "CLK", nivel) | Definir(peca.Id, "NQ", 1 - nivel);
            }

            case "flip-flop-d":
            case "flip-flop-sr":
            case "flip-flop-dsr":
                return Sequencial(peca, memoria);
        }

        return false;
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

        if (peca.Tipo == "flip-flop-dsr")
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
        return Definir(peca.Id, "Q", q) | Definir(peca.Id, "NQ", 1 - q);
    }
}
