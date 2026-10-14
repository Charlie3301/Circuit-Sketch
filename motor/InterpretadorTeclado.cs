namespace Simulador;

/// <summary>
/// O teclado matricial com a biblioteca Keypad: a tecla que o sketch le
/// sai do mapa DELE (o makeKeymap) e dos pinos DELE (rowPins, colPins) -- a
/// biblioteca varre os pinos, e a tecla apertada liga o fio de uma linha ao
/// de uma coluna. Fio trocado ou mapa diferente do teclado dao outra letra,
/// como na bancada. Antes saia sempre a letra impressa na tecla.
/// </summary>
public partial class Interpretador
{
    sealed class TecladoDoSketch
    {
        public Valor[] Teclas;
        public int[] Linhas, Colunas;
        public int NColunas;
        public Rotina AoMudar;
        public int Estado;            // IDLE 0, PRESSED 1, HOLD 2, RELEASED 3
        public char Tecla;
        public double ApertouEmMs;
        public int SeguraMs = 500;     // o holdTime de fabrica
        public bool Avisando;
    }

    /// <summary>Keypad(makeKeymap(teclas), rowPins, colPins, ROWS, COLS).</summary>
    void MontarTeclado(Objeto obj, CmdDeclObj d, Ambiente amb)
    {
        if (d.Args.Count < 5) return;
        Expr mapa = d.Args[0] is Chamada { Alvo: Variavel { Nome: "makeKeymap" } } mk && mk.Args.Count > 0 ? mk.Args[0] : d.Args[0];
        if (!VetorDoArgumento(mapa, amb, out var teclas)
            || !VetorDoArgumento(d.Args[1], amb, out var linhas)
            || !VetorDoArgumento(d.Args[2], amb, out var colunas)) return;
        int nl = (int)Avaliar(d.Args[3], amb).Num, nc = (int)Avaliar(d.Args[4], amb).Num;
        obj.Rede = new TecladoDoSketch
        {
            Teclas = teclas,
            Linhas = [.. linhas.Take(nl).Select(v => (int)v.Num)],
            Colunas = [.. colunas.Take(nc).Select(v => (int)v.Num)],
            NColunas = nc,
        };
    }

    /// <summary>
    /// A tecla apertada agora, como a varredura da biblioteca acha: os dois
    /// GPIOs que a tecla liga, procurados nos pinos do sketch. Com os fios
    /// de linha e coluna trocados a varredura ainda acha uma tecla -- a da
    /// posicao transposta. Zero: nenhuma (ou o fio nao esta nos pinos dele).
    /// </summary>
    char TeclaApertada(Objeto obj)
    {
        int rotulo = (int)maq.Leitura("keypad:tecla", 0);
        if (obj.Rede is not TecladoDoSketch t || !maq.Leituras.ContainsKey("keypad:linha")) return (char)rotulo;
        int gl = (int)maq.Leitura("keypad:linha", -1), gc = (int)maq.Leitura("keypad:coluna", -1);
        if (rotulo == 0 || gl < 0 || gc < 0) return (char)0;
        int r = Array.IndexOf(t.Linhas, gl), c = Array.IndexOf(t.Colunas, gc);
        if (r < 0 || c < 0)
        {
            r = Array.IndexOf(t.Linhas, gc);
            c = Array.IndexOf(t.Colunas, gl);
        }
        if (r < 0 || c < 0) return (char)0;
        int k = r * t.NColunas + c;
        return k >= 0 && k < t.Teclas.Length ? (char)((long)t.Teclas[k].Num & 0xFF) : (char)0;
    }

    /// <summary>
    /// O getKeys() da biblioteca: olha o teclado e, quando o estado muda
    /// (apertou, segurou 500 ms, soltou), chama o addEventListener() do sketch.
    /// Devolve a tecla nova (o getKey() so ve o aperto).
    /// </summary>
    char OlharTeclado(Objeto obj)
    {
        char agora = TeclaApertada(obj);
        if (obj.Rede is not TecladoDoSketch t) return agora;
        if (t.Avisando) return (char)0;   // o getState() de dentro do proprio callback
        double ms = maq.AgoraUs() / 1000;
        char nova = (char)0;
        int antes = t.Estado;
        if (agora != 0 && (t.Estado is 0 or 3 || agora != t.Tecla))
        {
            t.Tecla = agora;
            t.Estado = 1;
            t.ApertouEmMs = ms;
            nova = agora;
        }
        else if (agora != 0 && t.Estado == 1 && ms - t.ApertouEmMs >= t.SeguraMs) t.Estado = 2;
        else if (agora == 0 && t.Estado is 1 or 2) t.Estado = 3;
        else if (agora == 0 && t.Estado == 3) t.Estado = 0;
        if (t.Estado != antes && t.Estado != 0 && t.AoMudar is { } rotina)
        {
            t.Avisando = true;
            try { ChamarRotina(rotina, Valor.DeCaractere(t.Tecla)); }
            finally { t.Avisando = false; }
        }
        return nova;
    }

    Valor ChamarTeclado(Objeto obj, string metodo, Chamada c, Ambiente amb)
    {
        if (metodo == "addEventListener")
        {
            if (obj.Rede is TecladoDoSketch t && c.Args.Count > 0) t.AoMudar = RotinaDe(c.Args[0], amb, "addEventListener()");
            return Valor.Vazio;
        }
        var args = c.Args.Select(a => Avaliar(a, amb)).ToList();
        var teclado = obj.Rede as TecladoDoSketch;
        switch (metodo)
        {
            case "getKey":
                if (teclado is null)
                {
                    // Sem os pinos do sketch (outro jeito de declarar): a letra da tecla, no aperto.
                    int rotulo = (int)maq.Leitura("keypad:tecla", 0);
                    int antes = (int)obj.Campos.GetValueOrDefault("_tecla", Valor.DeInt(0)).Num;
                    obj.Campos["_tecla"] = Valor.DeInt(rotulo);
                    return Valor.DeCaractere(rotulo > 0 && rotulo != antes ? rotulo : 0);
                }
                return Valor.DeCaractere(OlharTeclado(obj));

            // Espera um aperto novo, como a biblioteca: trava ate la.
            case "waitForKey":
                while (!maq.Parar)
                {
                    char k = teclado is null ? (char)0 : OlharTeclado(obj);
                    if (teclado is null)
                    {
                        int rotulo = (int)maq.Leitura("keypad:tecla", 0);
                        int antes = (int)obj.Campos.GetValueOrDefault("_tecla", Valor.DeInt(0)).Num;
                        obj.Campos["_tecla"] = Valor.DeInt(rotulo);
                        if (rotulo > 0 && rotulo != antes) return Valor.DeCaractere(rotulo);
                    }
                    else if (k != 0) return Valor.DeCaractere(k);
                    maq.Avancar(10);
                    passos = 0;
                }
                return Valor.DeCaractere(0);

            case "getState":
                if (teclado is not null) { OlharTeclado(obj); return Valor.DeInt(teclado.Estado); }
                return Valor.DeInt(maq.Leitura("keypad:tecla", 0) > 0 ? 1 : 0);
            case "isPressed":
                return Valor.DeBool(args.Count > 0 && TeclaApertada(obj) is var k2 && k2 != 0 && k2 == (char)(long)args[0].Num);
            case "keyStateChanged":
            {
                if (teclado is null) return Valor.DeBool(false);
                int antes = teclado.Estado;
                OlharTeclado(obj);
                return Valor.DeBool(teclado.Estado != antes);
            }
            case "setHoldTime":
                if (teclado is not null && args.Count > 0) teclado.SeguraMs = (int)args[0].Num;
                return Valor.Vazio;
            case "setDebounceTime": case "begin":
                return Valor.Vazio;
        }
        throw new ErroDeExecucao($"Keypad::{metodo}() ainda nao e suportado pelo simulador.", c.Linha);
    }
}
