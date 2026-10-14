namespace Simulador;

/// <summary>
/// Descida recursiva sobre o subconjunto de MicroPython que a turma usa.
///
/// Produz a MESMA arvore do <see cref="Parser"/> do C. Essa e a decisao
/// que sustenta o resto: o interpretador, o depurador, as maquinas e
/// todas as pontes de biblioteca (rede, I2C, radio, cartao) ja existem e
/// trabalham sobre essa arvore -- escrever um segundo interpretador so
/// para o Python jogaria fora tudo isso e criaria duas verdades sobre o
/// que cada peca faz.
///
/// O que o Python tem e o C nao tinha ganhou no nos proprios:
/// <see cref="CmdParaCada"/>, <see cref="ListaLiteral"/>,
/// <see cref="DicionarioLiteral"/>, <see cref="Fatia"/> e
/// <see cref="ArgNomeado"/>.
/// </summary>
public class ParserPython(List<Token> toks)
{
    int p = 0;

    Token Atual => toks[p];
    Token Espiar(int n = 1) => toks[Math.Min(p + n, toks.Count - 1)];
    Token Avancar() => toks[p++];
    bool Checar(string s) => Atual.Tipo is not T.Fim && Atual.Tipo is T.Id or T.Op && Atual.E(s);

    bool Aceitar(string s)
    {
        if (!Checar(s)) return false;
        p++; return true;
    }

    Token Exigir(string s)
    {
        if (!Checar(s))
            throw new ErroDeSintaxe($"esperava '{s}' mas encontrei '{Mostrar(Atual)}'", Atual.Linha);
        return Avancar();
    }

    static string Mostrar(Token t) => t.Tipo switch
    {
        T.Nova => "fim da linha",
        T.Entra => "um bloco indentado",
        T.Sai => "o fim do bloco",
        T.Fim => "o fim do arquivo",
        _ => t.Texto
    };

    /// <summary>
    /// O ultimo comando fechou a LINHA, e nao so a si mesmo.
    ///
    /// Distingue "x = 1" de "x = 1; y = 2": no bloco de uma linha so
    /// ("if x: y = 1") e isto que diz onde o bloco termina. Sem a
    /// distincao, o if engolia a linha seguinte.
    /// </summary>
    bool fechouALinha;

    void ExigirFimDeLinha()
    {
        // Varios comandos na mesma linha, separados por ";". E raro, mas
        // aparece em codigo colado de uma linha so.
        if (Checar(";")) { Avancar(); fechouALinha = Atual.Tipo == T.Nova; return; }
        if (Atual.Tipo == T.Nova) { Avancar(); fechouALinha = true; return; }
        if (Atual.Tipo is T.Fim or T.Sai) { fechouALinha = true; return; }
        throw new ErroDeSintaxe(
            $"esperava o fim da linha mas encontrei '{Mostrar(Atual)}'", Atual.Linha);
    }

    /// <summary>
    /// Palavras do Python que o simulador ainda nao entende.
    ///
    /// Recusar com nome e melhor do que engasgar num erro de sintaxe
    /// generico tres linhas depois: o aluno le "class ainda nao" e sabe
    /// exatamente o que tirar.
    /// </summary>
    static readonly Dictionary<string, string> Recusadas = new()
    {
        ["yield"] = "yield",
        ["async"] = "async",
        ["await"] = "await",
    };

    // ================= topo =================
    public Programa AnalisarPrograma()
    {
        var corpo = new List<Cmd>();
        var funcoes = new List<Funcao>();
        var classes = new List<ClasseDecl>();

        while (Atual.Tipo != T.Fim)
        {
            if (Atual.Tipo is T.Nova or T.Entra or T.Sai) { Avancar(); continue; }

            if (Checar("def"))
            {
                funcoes.Add(AnalisarDef());
                continue;
            }

            if (Checar("class"))
            {
                classes.Add(AnalisarClasse());
                continue;
            }

            corpo.Add(AnalisarComando());
        }

        return MontarPrograma(corpo, funcoes) with { Classes = classes };
    }

    /// <summary>
    /// Divide o corpo do modulo em setup() e loop().
    ///
    /// O sketch em MicroPython nao tem setup nem loop: ele roda de cima
    /// para baixo e, quando precisa repetir, termina num "while True:".
    /// O simulador inteiro e construido em cima de setup + loop -- e da
    /// cadencia do loop que saem o redesenho da tela, o botao de parar e
    /// o limite de passos.
    ///
    /// Entao a traducao e direta: tudo antes do "while True:" final vira
    /// setup, e o CORPO dele vira loop. Sketch sem "while True:" (o do
    /// OLED, que so escreve na tela uma vez) fica so com setup, e o
    /// simulador o trata como programa que terminou.
    /// </summary>
    static Programa MontarPrograma(List<Cmd> corpo, List<Funcao> funcoes)
    {
        List<Cmd> doSetup = corpo;
        List<Cmd> doLoop = null;
        int linhaDoLoop = 0;

        if (corpo.Count > 0 && corpo[^1] is CmdEnquanto ultimo && SempreVerdadeiro(ultimo.Cond))
        {
            doSetup = corpo.GetRange(0, corpo.Count - 1);
            doLoop = ultimo.Corpo is CmdBloco b ? b.Corpo : [ultimo.Corpo];
            linhaDoLoop = ultimo.Linha;
        }

        int linha = corpo.Count > 0 ? corpo[0].Linha : 1;
        funcoes.Add(new Funcao("void", "setup", [], new CmdBloco(doSetup, linha), linha));

        if (doLoop is not null)
            funcoes.Add(new Funcao("void", "loop", [],
                                   new CmdBloco(doLoop, linhaDoLoop), linhaDoLoop));

        return new Programa([], funcoes) { Linguagem = Linguagem.MicroPython };
    }

    /// <summary>"while True:" e "while 1:" -- o laco eterno do sketch.</summary>
    static bool SempreVerdadeiro(Expr cond)
        => cond is Variavel { Nome: "True" }
           || cond is Literal { EhTexto: false, Valor: 1 };

    Funcao AnalisarDef()
    {
        int linha = Exigir("def").Linha;
        string nome = Avancar().Texto;
        Exigir("(");

        var parametros = new List<Param>();

        while (!Checar(")"))
        {
            // self de metodo nao existe aqui, mas *args e **kwargs
            // aparecem em codigo colado: recusar com nome.
            if (Checar("*") || Checar("**"))
                throw new ErroDeSintaxe(
                    "parametro com * ou ** ainda nao e suportado", Atual.Linha);

            string arg = Avancar().Texto;

            // Anotacao de tipo: "def piscar(pino: int)". Nao muda nada.
            if (Aceitar(":")) AnalisarExpr();

            parametros.Add(new Param("auto", arg)
            {
                Padrao = Aceitar("=") ? AnalisarExpr() : null
            });

            if (!Aceitar(",")) break;
        }

        Exigir(")");

        // Anotacao de retorno: "-> int". Nao muda nada aqui.
        if (Aceitar("->")) AnalisarExpr();

        Exigir(":");
        return new Funcao("auto", nome, parametros, AnalisarBloco(), linha);
    }

    /// <summary>
    /// O bloco depois dos dois-pontos.
    ///
    /// Duas formas: indentado na linha seguinte, ou na mesma linha
    /// ("if x: y = 1"), que aparece bastante em codigo curto.
    /// </summary>
    CmdBloco AnalisarBloco()
    {
        int linha = Atual.Linha;

        if (Atual.Tipo != T.Nova)
        {
            var umSo = new List<Cmd>();
            do { umSo.Add(AnalisarComando()); }
            while (!fechouALinha && Atual.Tipo is not (T.Sai or T.Fim));
            return new CmdBloco(umSo, linha);
        }

        Avancar();
        if (Atual.Tipo != T.Entra)
            throw new ErroDeSintaxe(
                "esperava um bloco indentado depois dos dois-pontos", Atual.Linha);
        Avancar();

        var corpo = new List<Cmd>();
        while (Atual.Tipo is not (T.Sai or T.Fim))
        {
            if (Atual.Tipo == T.Nova) { Avancar(); continue; }
            corpo.Add(AnalisarComando());
        }
        if (Atual.Tipo == T.Sai) Avancar();

        return new CmdBloco(corpo, linha);
    }

    // ================= comandos =================
    Cmd AnalisarComando()
    {
        int linha = Atual.Linha;

        if (Atual.Tipo == T.Id && Recusadas.TryGetValue(Atual.Texto, out var oQue))
            throw new ErroDeSintaxe(
                $"{oQue} ainda nao e suportado pelo simulador", linha);

        if (Checar("import") || Checar("from")) return AnalisarImport();
        if (Checar("try")) return AnalisarTente();
        if (Checar("with")) return AnalisarCom();
        if (Checar("raise")) return AnalisarLevanta();
        if (Checar("assert")) return AnalisarAfirma();
        if (Checar("class")) throw new ErroDeSintaxe(
            "classe dentro de funcao ou de bloco ainda nao e suportada: declare a classe no comeco do arquivo", linha);
        if (Checar("if")) return AnalisarSe();
        if (Checar("while")) return AnalisarEnquanto();
        if (Checar("for")) return AnalisarPara();
        if (Checar("def")) throw new ErroDeSintaxe(
            "funcao dentro de outra ainda nao e suportada", linha);

        if (Aceitar("pass")) { ExigirFimDeLinha(); return new CmdVazio(linha); }
        if (Aceitar("break")) { ExigirFimDeLinha(); return new CmdBreak(linha); }
        if (Aceitar("continue")) { ExigirFimDeLinha(); return new CmdContinue(linha); }

        if (Aceitar("return"))
        {
            Expr valor = Atual.Tipo is T.Nova or T.Sai or T.Fim || Checar(";")
                ? null : AnalisarExpr();
            ExigirFimDeLinha();
            return new CmdRetorno(valor, linha);
        }

        // "global x" / "nonlocal x": o simulador ja procura de dentro
        // para fora, entao declarar nao muda nada -- mas recusar
        // atrapalharia sketch que funciona.
        if (Checar("global") || Checar("nonlocal"))
        {
            while (Atual.Tipo is not (T.Nova or T.Sai or T.Fim)) Avancar();
            ExigirFimDeLinha();
            return new CmdVazio(linha);
        }

        if (Checar("del"))
        {
            Avancar();
            var alvo = AnalisarExpr();
            ExigirFimDeLinha();
            return new CmdExpr(new Chamada(new Variavel("__apagar", linha), [alvo], linha), linha);
        }

        var e = AnalisarExpr();

        // Atribuicao. Em Python ela DECLARA -- quem resolve isso e o
        // interpretador, que so aqui tem o ambiente na mao.
        if (Atual.Tipo == T.Op && Atual.Texto is "=" or "+=" or "-=" or "*=" or "/=" or "%="
            or "//=" or "**=" or "&=" or "|=" or "^=" or "<<=" or ">>=")
        {
            string op = Avancar().Texto;
            var valor = AnalisarExpr();

            // "a = b = 0": encadeada.
            while (Checar("=")) { Avancar(); valor = AnalisarExpr(); }

            ExigirFimDeLinha();
            return new CmdExpr(new Atribuicao(op, e, valor, linha), linha);
        }

        ExigirFimDeLinha();
        return new CmdExpr(e, linha);
    }

    Cmd AnalisarImport()
    {
        int linha = Atual.Linha;

        if (Aceitar("from"))
        {
            string modulo = NomePontuado();
            Exigir("import");

            var nomes = new List<string>();
            if (Aceitar("*")) nomes.Add("*");
            else
                do
                {
                    string n = Avancar().Texto;
                    if (Aceitar("as")) { Avancar(); }
                    nomes.Add(n);
                }
                while (Aceitar(","));

            ExigirFimDeLinha();
            return new CmdImporta(modulo, nomes, null, linha);
        }

        Exigir("import");
        string mod = NomePontuado();
        string apelido = Aceitar("as") ? Avancar().Texto : null;
        ExigirFimDeLinha();
        return new CmdImporta(mod, [], apelido, linha);
    }

    /// <summary>"umqtt.simple" -- o nome do modulo pode ter ponto.</summary>
    string NomePontuado()
    {
        var sb = new System.Text.StringBuilder(Avancar().Texto);
        while (Checar(".")) { Avancar(); sb.Append('.').Append(Avancar().Texto); }
        return sb.ToString();
    }

    Cmd AnalisarSe()
    {
        int linha = Exigir("if").Linha;
        var cond = AnalisarExpr();
        Exigir(":");
        var entao = AnalisarBloco();

        // O elif e um else com um if dentro -- e assim que ele vira
        // arvore sem um no proprio.
        Cmd senao = null;
        PularLinhasEmBranco();

        if (Checar("elif"))
        {
            toks[p] = Atual with { Texto = "if" };
            senao = AnalisarSe();
        }
        else if (Aceitar("else"))
        {
            Exigir(":");
            senao = AnalisarBloco();
        }

        return new CmdSe(cond, entao, senao, linha);
    }

    /// <summary>
    /// O "else" de um if vem depois do bloco, e entre os dois pode ter
    /// sobrado quebra de linha do bloco que acabou de fechar.
    /// </summary>
    void PularLinhasEmBranco()
    {
        while (Atual.Tipo == T.Nova) Avancar();
    }

    Cmd AnalisarEnquanto()
    {
        int linha = Exigir("while").Linha;
        var cond = AnalisarExpr();
        Exigir(":");
        var corpo = AnalisarBloco();
        return new CmdEnquanto(cond, corpo, linha);
    }

    Cmd AnalisarPara()
    {
        int linha = Exigir("for").Linha;

        var nomes = new List<string> { Avancar().Texto };
        while (Aceitar(",")) nomes.Add(Avancar().Texto);

        Exigir("in");
        var sequencia = AnalisarExpr();
        Exigir(":");
        var corpo = AnalisarBloco();

        return new CmdParaCada(nomes, sequencia, corpo, linha);
    }

    // ================= classes, excecoes e with =================

    /// <summary>
    /// "class Botao(Base):" -- os def viram metodos (o self sai da lista: e o
    /// objeto), o __init__ vira o construtor, e "x = 0" no corpo da classe vira
    /// o valor de partida do campo em cada objeto.
    /// </summary>
    ClasseDecl AnalisarClasse()
    {
        int linha = Exigir("class").Linha;
        string nome = Avancar().Texto;
        var bases = new List<string>();
        if (Aceitar("("))
        {
            while (!Checar(")"))
            {
                bases.Add(NomePontuado());
                if (!Aceitar(",")) break;
            }
            Exigir(")");
        }
        Exigir(":");

        var metodos = new List<Funcao>();
        var campos = new List<Cmd>();
        void Item()
        {
            if (Checar("def"))
            {
                var f = AnalisarDef();
                bool comSelf = f.Params.Count > 0 && f.Params[0].Nome == "self";
                bool construtor = f.Nome == "__init__";
                metodos.Add(new Funcao("auto", construtor ? nome : f.Nome, comSelf ? f.Params.Skip(1).ToList() : f.Params, f.Corpo, f.Linha)
                {
                    Classe = nome,
                    EhConstrutor = construtor,
                    Estatico = !comSelf,
                });
                return;
            }
            var c = AnalisarComando();
            if (c is CmdExpr { E: Atribuicao { Op: "=", Alvo: Variavel v } a })
                campos.Add(new CmdDecl("auto", v.Nome, a.Valor, false, null, null, a.Linha));
            // "pass" e a docstring nao fazem nada.
        }

        if (Atual.Tipo != T.Nova) Item();   // "class Erro(Exception): pass"
        else
        {
            Avancar();
            if (Atual.Tipo != T.Entra)
                throw new ErroDeSintaxe("esperava o corpo da classe, indentado", Atual.Linha);
            Avancar();
            while (Atual.Tipo is not (T.Sai or T.Fim))
            {
                if (Atual.Tipo == T.Nova) { Avancar(); continue; }
                Item();
            }
            if (Atual.Tipo == T.Sai) Avancar();
        }

        return new ClasseDecl(nome, bases, linha) { Metodos = metodos, Campos = campos, EhPython = true };
    }

    Cmd AnalisarTente()
    {
        int linha = Exigir("try").Linha;
        Exigir(":");
        var corpo = AnalisarBloco();
        var casos = new List<CasoExcecao>();
        CmdBloco senao = null, finalmente = null;
        PularLinhasEmBranco();

        while (Checar("except"))
        {
            Avancar();
            var tipos = new List<string>();
            string nome = null;
            if (!Checar(":"))
            {
                if (Aceitar("("))
                {
                    do { tipos.Add(NomePontuado()); } while (Aceitar(",") && !Checar(")"));
                    Exigir(")");
                }
                else tipos.Add(NomePontuado());
                if (Aceitar("as")) nome = Avancar().Texto;
            }
            Exigir(":");
            casos.Add(new CasoExcecao(tipos, nome, AnalisarBloco()));
            PularLinhasEmBranco();
        }
        if (casos.Count > 0 && Aceitar("else"))
        {
            Exigir(":");
            senao = AnalisarBloco();
            PularLinhasEmBranco();
        }
        if (Aceitar("finally"))
        {
            Exigir(":");
            finalmente = AnalisarBloco();
        }
        if (casos.Count == 0 && finalmente is null)
            throw new ErroDeSintaxe("try sem except nem finally", linha);
        return new CmdTente(corpo, casos, senao, finalmente, linha);
    }

    Cmd AnalisarLevanta()
    {
        int linha = Exigir("raise").Linha;
        Expr valor = Atual.Tipo is T.Nova or T.Sai or T.Fim || Checar(";") ? null : AnalisarExpr();
        if (Aceitar("from")) AnalisarExpr();   // "raise X from e": a causa nao muda nada aqui
        ExigirFimDeLinha();
        return new CmdLevanta(valor, linha);
    }

    /// <summary>"assert x > 0, 'negativo'": um if que levanta o AssertionError.</summary>
    Cmd AnalisarAfirma()
    {
        int linha = Exigir("assert").Linha;
        var cond = AnalisarExpr();
        var args = new List<Expr>();
        if (Aceitar(",")) args.Add(AnalisarExpr());
        ExigirFimDeLinha();
        var erro = new CmdLevanta(new Chamada(new Variavel("AssertionError", linha), args, linha), linha);
        return new CmdSe(new Unaria("!", cond, linha), erro, null, linha);
    }

    Cmd AnalisarCom()
    {
        int linha = Exigir("with").Linha;
        var itens = new List<(Expr, string)>();
        do
        {
            var contexto = AnalisarExpr();
            string nome = Aceitar("as") ? Avancar().Texto : null;
            itens.Add((contexto, nome));
        }
        while (Aceitar(","));
        Exigir(":");
        return new CmdCom(itens, AnalisarBloco(), linha);
    }

    /// <summary>"lambda x, y=2: x + y" -- a funcao sem nome do C++, com um return so.</summary>
    Expr AnalisarLambda()
    {
        int linha = Exigir("lambda").Linha;
        var parametros = new List<Param>();
        while (!Checar(":"))
        {
            string arg = Avancar().Texto;
            parametros.Add(new Param("auto", arg) { Padrao = Aceitar("=") ? AnalisarTernaria() : null });
            if (!Aceitar(",")) break;
        }
        Exigir(":");
        var corpo = AnalisarExpr();
        return new FuncaoAnonima(parametros, new CmdBloco([new CmdRetorno(corpo, linha)], linha), linha);
    }

    // ================= expressoes =================
    public Expr AnalisarExpr() => Checar("lambda") ? AnalisarLambda() : AnalisarTernaria();

    /// <summary>"a if cond else b" -- o ternario do Python, ao contrario do C.</summary>
    Expr AnalisarTernaria()
    {
        var sim = AnalisarOu();
        if (!Checar("if")) return sim;

        int linha = Avancar().Linha;
        var cond = AnalisarOu();
        Exigir("else");
        var nao = AnalisarTernaria();
        return new Ternaria(cond, sim, nao, linha);
    }

    Expr AnalisarOu()
    {
        var e = AnalisarE();
        while (Checar("or"))
        {
            int linha = Avancar().Linha;
            e = new Binaria("||", e, AnalisarE(), linha);
        }
        return e;
    }

    Expr AnalisarE()
    {
        var e = AnalisarNao();
        while (Checar("and"))
        {
            int linha = Avancar().Linha;
            e = new Binaria("&&", e, AnalisarNao(), linha);
        }
        return e;
    }

    Expr AnalisarNao()
    {
        if (Checar("not"))
        {
            int linha = Avancar().Linha;
            return new Unaria("!", AnalisarNao(), linha);
        }
        return AnalisarComparacao();
    }

    static readonly string[] Comparadores = ["==", "!=", "<", ">", "<=", ">="];

    /// <summary>
    /// As comparacoes, inclusive ENCADEADAS: "0 &lt;= x &lt; 10".
    ///
    /// Em Python isso e "0 &lt;= x and x &lt; 10", e nao "(0 &lt;= x) &lt; 10" como
    /// seria em C. Ler a cadeia inteira antes de montar a arvore e o que
    /// permite gerar o "and" no meio.
    /// </summary>
    Expr AnalisarComparacao()
    {
        var e = AnalisarBitOu();
        Expr cadeia = null;

        while (true)
        {
            Expr direita;
            string op;
            int linha = Atual.Linha;

            // "is" e "is not": para o simulador, e comparar com None.
            if (Checar("is"))
            {
                Avancar();
                op = Aceitar("not") ? "!=" : "==";
                direita = AnalisarBitOu();
            }
            // "x in lista" e "x not in lista".
            else if (Checar("in") || (Checar("not") && Espiar().E("in")))
            {
                bool negado = Aceitar("not");
                Exigir("in");
                Expr dentro = new Chamada(new Variavel("__contem", linha),
                                          [AnalisarBitOu(), e], linha);
                e = negado ? new Unaria("!", dentro, linha) : dentro;
                continue;
            }
            else if (Atual.Tipo == T.Op && Comparadores.Contains(Atual.Texto))
            {
                op = Avancar().Texto;
                direita = AnalisarBitOu();
            }
            else break;

            var passo = new Binaria(op, e, direita, linha);
            cadeia = cadeia is null ? passo : new Binaria("&&", cadeia, passo, linha);

            // O lado direito vira o esquerdo do proximo elo: e o "x" de
            // "0 <= x < 10" aparecendo dos dois lados.
            e = direita;
        }

        return cadeia ?? e;
    }

    Expr AnalisarBitOu()
    {
        var e = AnalisarBitE();
        while (Atual.Tipo == T.Op && Atual.Texto is "|" or "^")
        {
            string op = Avancar().Texto;
            e = new Binaria(op, e, AnalisarBitE(), Atual.Linha);
        }
        return e;
    }

    Expr AnalisarBitE()
    {
        var e = AnalisarDeslocamento();
        while (Checar("&"))
        {
            Avancar();
            e = new Binaria("&", e, AnalisarDeslocamento(), Atual.Linha);
        }
        return e;
    }

    Expr AnalisarDeslocamento()
    {
        var e = AnalisarSoma();
        while (Atual.Tipo == T.Op && Atual.Texto is "<<" or ">>")
        {
            string op = Avancar().Texto;
            e = new Binaria(op, e, AnalisarSoma(), Atual.Linha);
        }
        return e;
    }

    Expr AnalisarSoma()
    {
        var e = AnalisarProduto();
        while (Atual.Tipo == T.Op && Atual.Texto is "+" or "-")
        {
            string op = Avancar().Texto;
            e = new Binaria(op, e, AnalisarProduto(), Atual.Linha);
        }
        return e;
    }

    Expr AnalisarProduto()
    {
        var e = AnalisarUnaria();
        while (Atual.Tipo == T.Op && Atual.Texto is "*" or "/" or "%" or "//")
        {
            string op = Avancar().Texto;
            e = new Binaria(op, e, AnalisarUnaria(), Atual.Linha);
        }
        return e;
    }

    Expr AnalisarUnaria()
    {
        if (Atual.Tipo == T.Op && Atual.Texto is "-" or "+" or "~")
        {
            string op = Avancar().Texto;
            return new Unaria(op, AnalisarUnaria(), Atual.Linha);
        }
        return AnalisarPotencia();
    }

    Expr AnalisarPotencia()
    {
        var e = AnalisarSufixo();
        if (!Checar("**")) return e;

        int linha = Avancar().Linha;
        // A potencia associa da direita para a esquerda: 2**3**2 = 512.
        return new Chamada(new Variavel("pow", linha), [e, AnalisarUnaria()], linha);
    }

    Expr AnalisarSufixo()
    {
        var e = AnalisarPrimaria();

        while (true)
        {
            if (Checar("."))
            {
                int linha = Avancar().Linha;
                e = new Membro(e, Avancar().Texto, linha);
                continue;
            }

            if (Checar("("))
            {
                int linha = Avancar().Linha;
                var args = new List<Expr>();
                while (!Checar(")"))
                {
                    args.Add(AnalisarArgumento());
                    if (!Aceitar(",")) break;
                }
                Exigir(")");
                e = new Chamada(e, args, linha);
                continue;
            }

            if (Checar("["))
            {
                int linha = Avancar().Linha;

                Expr inicio = Checar(":") ? null : AnalisarExpr();
                if (Aceitar(":"))
                {
                    Expr fim = Checar("]") ? null : AnalisarExpr();
                    Exigir("]");
                    e = new Fatia(e, inicio, fim, linha);
                }
                else
                {
                    Exigir("]");
                    e = new Indice(e, inicio, linha);
                }
                continue;
            }

            return e;
        }
    }

    /// <summary>Um argumento, que pode vir com nome: "I2C(0, scl=Pin(22))".</summary>
    Expr AnalisarArgumento()
    {
        if (Atual.Tipo == T.Id && Espiar().Tipo == T.Op && Espiar().E("=")
            && !Espiar(2).E("="))
        {
            string nome = Avancar().Texto;
            int linha = Avancar().Linha;
            return new ArgNomeado(nome, AnalisarExpr(), linha);
        }
        return AnalisarExpr();
    }

    Expr AnalisarPrimaria()
    {
        var t = Atual;

        if (t.Tipo == T.Num)
        {
            Avancar();
            return new Literal(t.Valor, null, t.Inteiro, false, t.Linha);
        }

        if (t.Tipo == T.Str)
        {
            Avancar();

            // Textos colados viram um so: "abc" "def" == "abcdef".
            var texto = t.Texto;
            bool formatado = t.Valor == LexerPython.Formatado;
            while (Atual.Tipo == T.Str)
            {
                formatado |= Atual.Valor == LexerPython.Formatado;
                texto += Avancar().Texto;
            }

            var literal = new Literal(0, texto, false, true, t.Linha);
            return formatado
                ? new Chamada(new Variavel("__formatar", t.Linha), [literal], t.Linha)
                : literal;
        }

        if (Checar("("))
        {
            int linha = Avancar().Linha;
            if (Aceitar(")")) return new ListaLiteral([], true, linha);

            var itens = new List<Expr> { AnalisarExpr() };
            bool tupla = false;
            while (Aceitar(","))
            {
                tupla = true;
                if (Checar(")")) break;
                itens.Add(AnalisarExpr());
            }
            Exigir(")");

            // "(x)" e so x entre parenteses; "(x,)" ja e tupla de um.
            return tupla ? new ListaLiteral(itens, true, linha) : itens[0];
        }

        if (Checar("["))
        {
            int linha = Avancar().Linha;
            var itens = new List<Expr>();
            while (!Checar("]"))
            {
                itens.Add(AnalisarExpr());
                if (!Aceitar(",")) break;
            }
            Exigir("]");
            return new ListaLiteral(itens, false, linha);
        }

        if (Checar("{"))
        {
            int linha = Avancar().Linha;
            var chaves = new List<Expr>();
            var valores = new List<Expr>();
            while (!Checar("}"))
            {
                chaves.Add(AnalisarExpr());
                Exigir(":");
                valores.Add(AnalisarExpr());
                if (!Aceitar(",")) break;
            }
            Exigir("}");
            return new DicionarioLiteral(chaves, valores, linha);
        }

        if (t.Tipo == T.Id)
        {
            Avancar();
            return new Variavel(t.Texto, t.Linha);
        }

        throw new ErroDeSintaxe($"nao entendi '{Mostrar(t)}' aqui", t.Linha);
    }
}
