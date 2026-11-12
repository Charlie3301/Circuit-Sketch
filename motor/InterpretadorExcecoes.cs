namespace Simulador;

/// <summary>
/// Uma excecao do Python: levantada pelo sketch (raise), ou um erro do
/// simulador traduzido para o tipo que o MicroPython daria -- o sensor que
/// nao respondeu e um OSError, a divisao por zero um ZeroDivisionError. Sem
/// ninguem pegar, ela para o sketch como qualquer erro, com o tipo na frente.
/// </summary>
public class ExcecaoDoSketch(string tipo, string mensagem, Objeto objeto, int linha)
    : ErroDeExecucao(string.IsNullOrEmpty(mensagem) ? tipo : $"{tipo}: {mensagem}", linha)
{
    public string Tipo { get; } = tipo;
    public string Mensagem { get; } = mensagem;
    public Objeto Objeto { get; } = objeto;
}

/// <summary>
/// O try/except, o raise, o assert e o with do MicroPython (Alpha 24).
///
/// As excecoes embutidas tem a hierarquia do Python: "except OSError" pega o
/// TimeoutError, "except LookupError" pega o IndexError e o KeyError, e
/// "except Exception" pega todas menos o KeyboardInterrupt. Uma classe do
/// sketch que herda de uma delas ("class SensorFalhou(OSError)") entra na
/// mesma arvore.
/// </summary>
public partial class Interpretador
{
    static readonly Dictionary<string, string> PaiDaExcecao = new(StringComparer.Ordinal)
    {
        ["BaseException"] = null,
        ["Exception"] = "BaseException",
        ["KeyboardInterrupt"] = "BaseException",
        ["SystemExit"] = "BaseException",
        ["ArithmeticError"] = "Exception",
        ["ZeroDivisionError"] = "ArithmeticError",
        ["OverflowError"] = "ArithmeticError",
        ["LookupError"] = "Exception",
        ["IndexError"] = "LookupError",
        ["KeyError"] = "LookupError",
        ["ValueError"] = "Exception",
        ["UnicodeError"] = "ValueError",
        ["TypeError"] = "Exception",
        ["NameError"] = "Exception",
        ["AttributeError"] = "Exception",
        ["ImportError"] = "Exception",
        ["RuntimeError"] = "Exception",
        ["NotImplementedError"] = "RuntimeError",
        ["OSError"] = "Exception",
        ["TimeoutError"] = "OSError",
        ["StopIteration"] = "Exception",
        ["AssertionError"] = "Exception",
        ["MemoryError"] = "Exception",
    };

    /// <summary>O IOError e o EnvironmentError sao o OSError com outro nome.</summary>
    static string NomeVerdadeiro(string tipo) => tipo is "IOError" or "EnvironmentError" ? "OSError" : tipo;

    public static bool EhExcecaoEmbutida(string nome) => PaiDaExcecao.ContainsKey(NomeVerdadeiro(nome));

    /// <summary>O except mais perto que vale: o excepcao em curso (para o "raise" sozinho).</summary>
    ErroDeExecucao excecaoAtual;

    string PaiDe(string tipo)
    {
        if (classes.TryGetValue(tipo, out var info) && info.Decl.EhPython)
            return info.Decl.Bases.Count > 0 ? NomeVerdadeiro(info.Decl.Bases[0]) : null;
        return PaiDaExcecao.GetValueOrDefault(tipo);
    }

    /// <summary>"tipo" e "procurado", ou deriva dele?</summary>
    bool ExcecaoDeriva(string tipo, string procurado)
    {
        procurado = NomeVerdadeiro(procurado);
        int voltas = 0;
        for (string t = NomeVerdadeiro(tipo); t is not null && voltas++ < 40; t = PaiDe(t))
            if (t == procurado) return true;
        return false;
    }

    /// <summary>A classe do sketch e uma excecao (herda de uma das embutidas)?</summary>
    bool ClasseEhExcecao(string nome) => nome is not null && classes.ContainsKey(nome) && ExcecaoDeriva(nome, "BaseException");

    /// <summary>O tipo do Python para um erro do simulador.</summary>
    static string TipoDoErro(ErroDeExecucao e)
    {
        if (e is ExcecaoDoSketch x) return x.Tipo;
        // As regras olham o portugues: em outro idioma, a mensagem volta para ele antes.
        string m = Idioma.Desfazer(e.Texto).ToLowerInvariant();
        if (m.Contains("oserror")) return "OSError";   // o I2C e a UART do Python ja dizem
        if (m.Contains("divisao por zero") || m.Contains("dividir por zero") || m.Contains("modulo por zero")) return "ZeroDivisionError";
        if (m.Contains("indice") && (m.Contains("fora") || m.Contains("range"))) return "IndexError";
        if (m.Contains("chave") && (m.Contains("nao existe") || m.Contains("nao tem") || m.Contains("nao esta no dicionario"))) return "KeyError";
        if (m.Contains("nao foi declarada") || m.Contains("nao foi definid")) return "NameError";
        if (m.Contains("nao tem o campo") || m.Contains("nao tem o metodo") || m.Contains("nao tem atributo")) return "AttributeError";
        if (m.Contains("memoria") || m.Contains("heap")) return "MemoryError";
        if (m.Contains("i2c") || m.Contains("sensor") || m.Contains("nao respond") || m.Contains("timeout")
            || m.Contains("enodev") || m.Contains("etimedout") || m.Contains("arquivo") || m.Contains("cartao")
            || m.Contains("wifi") || m.Contains("conex")) return "OSError";
        if (m.Contains("invalid") || m.Contains("nao e um numero") || m.Contains("converter")) return "ValueError";
        if (m.Contains("tipo")) return "TypeError";
        return "RuntimeError";
    }

    /// <summary>O objeto da excecao: a classe e o tipo, o texto e a mensagem (o que o print(e) mostra).</summary>
    static Objeto NovaExcecao(string tipo, IReadOnlyList<Valor> args)
    {
        var o = new Objeto(NomeVerdadeiro(tipo)) { Texto = args.Count > 0 ? args[0].Mostrar() : "" };
        if (args.Count > 0 && !args[0].EhTexto && args[0].Referencia is null) o.Campos["errno"] = args[0];
        o.Campos["value"] = args.Count > 0 ? args[0] : Valor.Vazio;
        return o;
    }

    Valor ValorDaExcecao(ErroDeExecucao e)
        => e is ExcecaoDoSketch { Objeto: { } o }
            ? Valor.DeReferencia(o)
            : Valor.DeReferencia(NovaExcecao(TipoDoErro(e),
                [Valor.DeTexto(e is ExcecaoDoSketch x ? x.Mensagem : e.Texto)]));

    void ExecTente(CmdTente t, Ambiente amb)
    {
        ErroDeExecucao pendente = null;
        try
        {
            Exec(t.Corpo, amb);
            if (desvio == Desvio.Nenhum && t.Senao is not null) Exec(t.Senao, amb);
        }
        catch (ErroDeExecucao e)
        {
            string tipo = TipoDoErro(e);
            // O "except:" sozinho pega tudo, como no Python.
            var caso = t.Casos.FirstOrDefault(k => k.Tipos.Count == 0 || k.Tipos.Any(procurado => ExcecaoDeriva(tipo, procurado)));
            if (caso is null) pendente = e;
            else
            {
                var antes = excecaoAtual;
                excecaoAtual = e;
                try
                {
                    if (caso.Nome is not null) DeclararOuAtribuir(caso.Nome, ValorDaExcecao(e), amb);
                    Exec(caso.Corpo, amb);
                }
                catch (ErroDeExecucao deDentro) { pendente = deDentro; }   // levantou de dentro do except
                finally { excecaoAtual = antes; }
            }
        }

        if (t.Finalmente is not null)
        {
            // O finally roda sempre -- e um return dentro dele vence o de antes
            // (e engole a excecao, como no Python).
            var desvioAntes = desvio;
            var retornoAntes = valorDoRetorno;
            desvio = Desvio.Nenhum;
            Exec(t.Finalmente, amb);
            if (desvio == Desvio.Nenhum)
            {
                desvio = desvioAntes;
                valorDoRetorno = retornoAntes;
            }
            else pendente = null;
        }
        if (pendente is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(pendente);
    }

    void ExecLevanta(CmdLevanta l, Ambiente amb)
    {
        if (l.Valor is null)
        {
            if (excecaoAtual is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(excecaoAtual);
            throw new ExcecaoDoSketch("RuntimeError", "no active exception to reraise", null, l.Linha);
        }

        // "raise ValueError" -- a classe, sem chamar: um objeto sem mensagem.
        if (l.Valor is Variavel nome && (EhExcecaoEmbutida(nome.Nome) || ClasseEhExcecao(nome.Nome)))
            throw new ExcecaoDoSketch(NomeVerdadeiro(nome.Nome), "", NovaExcecao(nome.Nome, []), l.Linha);

        var v = Avaliar(l.Valor, amb);
        if (v.Referencia is Objeto o && (EhExcecaoEmbutida(o.Classe) || ClasseEhExcecao(o.Classe)))
            throw new ExcecaoDoSketch(o.Classe, o.Texto ?? "", o, l.Linha);
        throw new ExcecaoDoSketch("TypeError", "exceptions must derive from BaseException", null, l.Linha);
    }

    /// <summary>
    /// "with x as y:" -- o __enter__ (de uma classe do sketch; senao, o proprio
    /// x), o bloco, e o __exit__ ou o close() no fim, saindo bem ou por erro:
    /// o arquivo aberto no "with" sempre fecha.
    /// </summary>
    void ExecCom(CmdCom w, Ambiente amb, int item = 0)
    {
        if (item == w.Itens.Count)
        {
            Exec(w.Corpo, amb);
            return;
        }

        var (contexto, nome) = w.Itens[item];
        var v = Avaliar(contexto, amb);
        var obj = v.Referencia as Objeto;
        Funcao entrar = null, sair = null;
        if (obj is not null && classes.TryGetValue(obj.Classe, out var info))
        {
            entrar = AcharLista(info, "__enter__")?.FirstOrDefault();
            sair = AcharLista(info, "__exit__")?.FirstOrDefault();
        }
        var dentro = entrar is not null ? ChamarMetodo(obj, entrar, [], amb, w.Linha) : v;
        if (nome is not null) DeclararOuAtribuir(nome, dentro, amb);

        try
        {
            ExecCom(w, amb, item + 1);
        }
        finally
        {
            var desvioAntes = desvio;
            var retornoAntes = valorDoRetorno;
            desvio = Desvio.Nenhum;
            if (sair is not null)
                ChamarMetodo(obj, sair, [.. Enumerable.Range(0, sair.Params.Count).Select(_ => (Expr)new Pronto(Valor.Vazio, w.Linha))], amb, w.Linha);
            else if (obj is not null)
            {
                try { Avaliar(new Chamada(new Membro(new Pronto(v, w.Linha), "close", w.Linha), [], w.Linha), amb); }
                catch (ErroDeExecucao) { }   // quem nao tem close() nao precisa fechar
            }
            desvio = desvioAntes;
            valorDoRetorno = retornoAntes;
        }
    }

    /// <summary>
    /// Chamar uma classe no Python: "Botao(14)" monta o objeto (o __init__ e o
    /// construtor); "ValueError('x')" monta a excecao. Nulo: o nome nao e classe.
    /// </summary>
    Valor? InstanciarPython(string nome, List<Valor> args, Ambiente amb, int linha)
    {
        if (EhExcecaoEmbutida(nome)) return Valor.DeReferencia(NovaExcecao(nome, args));
        if (!classes.TryGetValue(nome, out var info) || !info.Decl.EhPython) return null;

        // A excecao do sketch sem __init__: "class SensorFalhou(OSError): pass".
        if (info.Construtores.Count == 0 && ClasseEhExcecao(nome))
        {
            var excecao = NovaExcecao(nome, args);
            return Valor.DeReferencia(new Objeto(nome) { Texto = excecao.Texto });
        }
        return Valor.DeReferencia(Construir(info, [.. args.Select(a => (Expr)new Pronto(a, linha))], amb, linha));
    }

    /// <summary>
    /// O escopo de um metodo de classe do Python: o do objeto, com o "self"
    /// nele -- e o nome da classe do metodo, que e por onde o super() sobe.
    /// </summary>
    Ambiente EscopoComSelf(Objeto obj, string classeDoMetodo = null)
    {
        if (!EhPython || obj?.Escopo is null || !classes.TryGetValue(obj.Classe, out var info) || !info.Decl.EhPython)
            return obj?.Escopo;
        var escopo = new Ambiente(obj.Escopo);
        escopo.Declarar("self", Valor.DeReferencia(obj));
        escopo.Declarar("__classe__", Valor.DeTexto(classeDoMetodo ?? obj.Classe));
        return escopo;
    }

    /// <summary>"super().metodo(...)": o da classe de cima da do metodo que esta rodando, no mesmo self.</summary>
    Valor ChamarDoSuper(string metodo, Chamada c, Ambiente amb)
    {
        if (!amb.Buscar("self", out var self) || self.Referencia is not Objeto obj
            || !amb.Buscar("__classe__", out var classe) || !classes.TryGetValue(classe.Texto ?? "", out var daqui))
            throw new ErroDeExecucao(Idioma.T("super() so funciona dentro de um metodo de classe"), c.Linha);
        var deCima = daqui.Bases.FirstOrDefault();
        if (deCima is null)
        {
            // A de cima e embutida (Exception, object): o __init__ dela nao faz nada aqui.
            if (metodo == "__init__") return Valor.Vazio;
            throw new ErroDeExecucao(Idioma.T("a classe {0} nao tem classe de cima com {1}()", daqui.Nome, metodo), c.Linha);
        }
        Funcao f = metodo == "__init__"
            ? (deCima.Construtores.Count > 0 ? Escolher(deCima.Construtores, c.Args, amb, c.Linha, deCima.Nome) : null)
            : ProcurarMetodo(deCima, metodo, c.Args, amb, c.Linha);
        if (f is null)
        {
            if (metodo == "__init__") return Valor.Vazio;
            throw new ErroDeExecucao(Idioma.T("a classe {0} nao tem o metodo {1}()", deCima.Nome, metodo), c.Linha);
        }
        return ChamarUsuario(f, c.Args, amb, EscopoComSelf(obj, deCima.Nome), c.Linha);
    }
}
