namespace Simulador.App;

/// <summary>
/// O que o multimetro le da conta de energia: a tensao entre dois nos (a continua
/// e a alternada do PWM), a corrente e a resistencia que o ohmimetro veria.
/// </summary>
public sealed partial class Energia
{
    /// <summary>
    /// As tensoes de cada trecho do periodo do PWM, com o peso de cada um (nulo sem
    /// PWM): e delas que sai a parte alternada, como o multimetro de verdadeiro
    /// valor eficaz mede num pino em analogWrite.
    /// </summary>
    List<(Dictionary<string, double> Tensoes, double Peso)> tensoesDosTrechos;

    /// <summary>O no deste pino nesta conta (null: o pino nao esta em nenhum).</summary>
    public string RaizDoPino(string peca, string pino) => rede?.RaizDe(Netlist.No(peca, pino));

    /// <summary>A tensao continua (a media) do no A menos a do B; B nulo e o terra.</summary>
    public double ContinuaEntre(string a, string b) => TensaoDoNo(a) - (b is null ? 0 : TensaoDoNo(b));

    /// <summary>
    /// A parte alternada entre os dois nos, em volts eficazes: o desvio da tensao em
    /// cada trecho do PWM em torno da media. Num pino em PWM de 3,3 V com ciclo D, da
    /// 3,3 x raiz(D(1-D)) -- 1,65 V a 50%. Sem PWM, zero.
    /// </summary>
    public double AlternadaEntre(string a, string b)
    {
        if (tensoesDosTrechos is not { Count: > 1 } trechos) return 0;
        double V(Dictionary<string, double> t, string no) => no is not null && t.TryGetValue(no, out double v) ? v : 0;
        double soma = 0, pesos = 0;
        foreach (var (t, peso) in trechos) { soma += (V(t, a) - V(t, b)) * peso; pesos += peso; }
        if (pesos <= 0) return 0;
        double media = soma / pesos, variancia = 0;
        foreach (var (t, peso) in trechos)
        {
            double d = V(t, a) - V(t, b) - media;
            variancia += d * d * peso;
        }
        return Math.Sqrt(variancia / pesos);
    }

    /// <summary>Os ramos que nao sao resistencia para o ohmimetro: as fontes, as quedas, o que guarda carga, o que e da placa.</summary>
    static readonly HashSet<string> RotulosForaDoOhmimetro =
        ["capacitor", "placa", "medida", "regulador", "bec", "entrada", "saida", "base", "coletor", "dreno", "corpo", "led", "bateria"];

    /// <summary>
    /// A resistencia que o ohmimetro le entre dois nos: a rede dos ramos que sao
    /// resistencia (o resistor, o LDR, o potenciometro, a bobina, o fusivel, o
    /// contato fechado), com as fontes de fora -- o ohmimetro poe a corrente dele e
    /// mede a tensao. Null quando nao ha caminho (o "OL" do visor); zero no mesmo no.
    /// </summary>
    public double? ResistenciaEntre(string a, string b)
    {
        if (a is null || b is null) return null;
        if (a == b) return 0;
        if (!indiceDoNo.TryGetValue(a, out int ia) || !indiceDoNo.TryGetValue(b, out int ib)) return null;

        var vizinhos = new Dictionary<int, List<(int No, double G)>>();
        void Liga(int x, int y, double g)
        {
            if (!vizinhos.TryGetValue(x, out var lista)) vizinhos[x] = lista = [];
            lista.Add((y, g));
        }
        foreach (var r in ramos)
        {
            if (r.A < 0 || r.B < 0 || r.A == r.B || r.Queda != 0 || r.Fem != 0) continue;
            if (r.Resistencia <= 0 || r.Resistencia >= 1e8) continue;
            if (r.Rotulo is not null && RotulosForaDoOhmimetro.Contains(r.Rotulo)) continue;
            Liga(r.A, r.B, 1 / r.Resistencia);
            Liga(r.B, r.A, 1 / r.Resistencia);
        }

        // Os nos ligados ao A; sem o B entre eles, nao ha caminho.
        var alcancados = new List<int> { ia };
        var vistos = new HashSet<int> { ia };
        for (int k = 0; k < alcancados.Count; k++)
            if (vizinhos.TryGetValue(alcancados[k], out var lista))
                foreach (var (no, _) in lista)
                    if (vistos.Add(no)) alcancados.Add(no);
        if (!vistos.Contains(ib)) return null;

        // A conta do ohmimetro: 1 A entrando no A, o B no terra; a tensao do A e a resistencia.
        var incognitas = alcancados.Where(n => n != ib).ToList();
        var posicao = new Dictionary<int, int>();
        for (int i = 0; i < incognitas.Count; i++) posicao[incognitas[i]] = i;
        int n = incognitas.Count;
        var g = new double[n, n + 1];
        foreach (int no in incognitas)
        {
            int i = posicao[no];
            if (!vizinhos.TryGetValue(no, out var lista)) continue;
            foreach (var (outro, condutancia) in lista)
            {
                g[i, i] += condutancia;
                if (posicao.TryGetValue(outro, out int j)) g[i, j] -= condutancia;
            }
        }
        g[posicao[ia], n] = 1;

        // Gauss com pivo parcial.
        for (int c = 0; c < n; c++)
        {
            int pivo = c;
            for (int l = c + 1; l < n; l++) if (Math.Abs(g[l, c]) > Math.Abs(g[pivo, c])) pivo = l;
            if (Math.Abs(g[pivo, c]) < 1e-15) continue;
            if (pivo != c)
                for (int k = c; k <= n; k++) (g[c, k], g[pivo, k]) = (g[pivo, k], g[c, k]);
            for (int l = 0; l < n; l++)
            {
                if (l == c || g[l, c] == 0) continue;
                double f = g[l, c] / g[c, c];
                for (int k = c; k <= n; k++) g[l, k] -= f * g[c, k];
            }
        }
        int linhaDoA = posicao[ia];
        double r0 = Math.Abs(g[linhaDoA, linhaDoA]) < 1e-15 ? double.PositiveInfinity : g[linhaDoA, n] / g[linhaDoA, linhaDoA];
        return double.IsFinite(r0) && r0 >= 0 ? r0 : null;
    }
}
