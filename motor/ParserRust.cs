namespace Simulador;

/// <summary>
/// Rust, na mesma árvore das outras.
///
/// O que um front-end de Rust precisa acertar para um simulador de
/// microcontrolador -- e o que ele pode deixar de lado com honestidade:
///
///   - <c>fn main()</c> com <c>loop { }</c> dentro é o formato de todo
///     firmware em Rust. Vira setup e loop, como o <c>main()</c> do C;
///   - <c>let</c> declara, <c>let mut</c> declara mutável. Aqui as duas
///     viram a mesma declaração: o interpretador não impede escrita, e
///     fingir que impede daria um erro que o Rust de verdade dá em
///     tempo de COMPILAÇÃO, com outra mensagem e em outro momento;
///   - <c>match</c> vira <c>switch</c>, com o <c>break</c> que o Rust
///     não escreve porque os ramos dele não vazam;
///   - <c>for x in 0..10</c> vira o <c>for</c> do C, e <c>..=</c> inclui
///     o último;
///   - <c>unsafe</c>, <c>#![no_std]</c>, <c>use</c>, tempos de vida e
///     tipos genéricos são LIDOS e descartados. São a parte do Rust que
///     existe para o compilador provar coisas, e aqui não há o que
///     provar: o circuito é a prova.
///
/// O empréstimo e a posse não são simulados, e isto é dito em voz alta
/// em vez de mal-feito: quem escreve Rust embarcado aqui está testando
/// o CIRCUITO, e um verificador de posse pela metade reprovaria
/// programas corretos.
/// </summary>
public class ParserRust(List<Token> tokens)
{
    int pos;

    Token Atual => tokens[Math.Min(pos, tokens.Count - 1)];
    Token Proximo => tokens[Math.Min(pos + 1, tokens.Count - 1)];
    int Linha => Atual.Linha;

    bool Eh(string t) => Atual.Texto == t;
    bool ProximoEh(string t) => Proximo.Texto == t;

    Token Comer() => tokens[Math.Min(pos++, tokens.Count - 1)];

    bool Aceitar(string t)
    {
        if (!Eh(t)) return false;
        pos++;
        return true;
    }

    Token Exigir(string t)
    {
        if (!Eh(t)) throw new ErroDeSintaxe($"esperava '{t}' e veio '{Atual.Texto}'", Linha);
        return Comer();
    }

    // =================================================================
    //  o programa
    // =================================================================
    public Programa Analisar()
    {
        var globais = new List<Cmd>();
        var funcoes = new List<Funcao>();

        while (Atual.Tipo != T.Fim)
        {
            // Atributos: #![no_std], #[entry], #[derive(...)]. Dizem ao
            // compilador de verdade como montar o binário, e aqui não
            // mudam o que o programa faz.
            if (Eh("#")) { PularAtributo(); continue; }

            if (Eh("use") || Eh("mod") || Eh("extern") || Eh("crate")) { PularAtePontoEVirgula(); continue; }

            if (Eh("pub")) { Comer(); continue; }
            if (Eh("unsafe") && ProximoEh("fn")) { Comer(); continue; }

            if (Eh("fn")) { funcoes.Add(Funcao()); continue; }

            // "static LED: u8 = 13;" e "const N: usize = 4;"
            if (Eh("static") || Eh("const")) { globais.Add(Global()); continue; }

            // struct, enum, impl e trait: o simulador não tem tipos
            // próprios, então o corpo é lido e descartado. As funções
            // de dentro de um "impl" ficam, como funções soltas.
            if (Eh("struct") || Eh("enum") || Eh("type") || Eh("trait")) { PularItem(); continue; }

            if (Eh("impl")) { funcoes.AddRange(Implementacao()); continue; }

            if (Aceitar(";")) continue;

            throw new ErroDeSintaxe($"não esperava '{Atual.Texto}' no topo do arquivo", Linha);
        }

        return Montar(globais, funcoes);
    }

    Programa Montar(List<Cmd> globais, List<Funcao> funcoes)
        => Compilador.PartirNoLaco(
            new Programa(globais, funcoes) { Linguagem = Linguagem.Rust }, "main");

    // =================================================================
    //  itens
    // =================================================================
    Funcao Funcao()
    {
        int linha = Linha;
        Exigir("fn");

        string nome = Comer().Texto;
        PularGenericos();

        var parametros = new List<Param>();
        Exigir("(");

        while (!Eh(")") && Atual.Tipo != T.Fim)
        {
            // "&self" e "self": o objeto dono do método. Aqui as
            // funções são soltas, então ele some.
            if (Eh("&")) { Comer(); Aceitar("mut"); }
            if (Eh("self")) { Comer(); Aceitar(","); continue; }

            Aceitar("mut");
            string arg = Comer().Texto;

            string tipo = "int";
            if (Aceitar(":")) tipo = Tipo();

            parametros.Add(new Param(tipo, arg));
            if (!Aceitar(",")) break;
        }

        Exigir(")");

        string retorno = "void";
        if (Aceitar("->")) retorno = Tipo();

        // "where T: Copy" fica entre o retorno e o corpo.
        while (!Eh("{") && Atual.Tipo != T.Fim) Comer();

        var corpo = Bloco();
        return new Funcao(retorno, nome, parametros, corpo, linha);
    }

    List<Funcao> Implementacao()
    {
        Exigir("impl");

        // "impl Trait for Tipo {" -- tudo antes da chave é o cabeçalho.
        while (!Eh("{") && Atual.Tipo != T.Fim) Comer();
        Exigir("{");

        var saida = new List<Funcao>();

        while (!Eh("}") && Atual.Tipo != T.Fim)
        {
            if (Eh("#")) { PularAtributo(); continue; }
            if (Eh("pub")) { Comer(); continue; }
            if (Eh("fn")) { saida.Add(Funcao()); continue; }

            Comer();
        }

        Exigir("}");
        return saida;
    }

    Cmd Global()
    {
        int linha = Linha;
        Comer();   // static / const

        Aceitar("mut");
        string nome = Comer().Texto;

        string tipo = "int";
        if (Aceitar(":")) tipo = Tipo();

        Expr inicial = null;
        if (Aceitar("=")) inicial = Expressao();

        Aceitar(";");
        return new CmdDecl(tipo, nome, inicial, false, null, null, linha);
    }

    void PularAtributo()
    {
        Exigir("#");
        Aceitar("!");

        if (!Eh("[")) return;

        int fundo = 0;

        do
        {
            if (Eh("[")) fundo++;
            else if (Eh("]")) fundo--;
            Comer();
        }
        while (fundo > 0 && Atual.Tipo != T.Fim);
    }

    void PularAtePontoEVirgula()
    {
        while (Atual.Tipo != T.Fim && !Eh(";")) Comer();
        Aceitar(";");
    }

    void PularItem()
    {
        while (Atual.Tipo != T.Fim && !Eh("{") && !Eh(";")) Comer();

        if (Aceitar(";")) return;

        int fundo = 0;

        do
        {
            if (Eh("{")) fundo++;
            else if (Eh("}")) fundo--;
            Comer();
        }
        while (fundo > 0 && Atual.Tipo != T.Fim);
    }

    void PularGenericos()
    {
        if (!Eh("<")) return;

        int fundo = 0;

        do
        {
            if (Eh("<")) fundo++;
            else if (Eh(">")) fundo--;
            else if (Eh(">>")) fundo -= 2;
            Comer();
        }
        while (fundo > 0 && Atual.Tipo != T.Fim);
    }

    /// <summary>
    /// O tipo, reduzido ao que o interpretador precisa saber.
    ///
    /// Referência, mutabilidade, genérico e tempo de vida somem: eles
    /// existem para o compilador do Rust provar segurança de memória, e
    /// aqui não há memória para proteger -- o interpretador é dinâmico.
    /// O que fica é inteiro, real, texto ou booleano.
    /// </summary>
    string Tipo()
    {
        Aceitar("&");
        Aceitar("mut");

        if (Eh("(") )
        {
            // A tupla vazia "()" é o "void" do Rust.
            Comer();
            while (!Eh(")") && Atual.Tipo != T.Fim) Comer();
            Aceitar(")");
            return "void";
        }

        if (Eh("["))
        {
            Comer();
            string dentro = Tipo();
            while (!Eh("]") && Atual.Tipo != T.Fim) Comer();
            Aceitar("]");
            return dentro;
        }

        if (Eh("!")) { Comer(); return "void"; }

        string bruto = Atual.Tipo == T.Id ? Comer().Texto : "int";

        while (Aceitar("::")) bruto = Comer().Texto;
        PularGenericos();

        return bruto switch
        {
            "u8" or "i8" => "byte",
            "u16" or "i16" => "word",
            "u32" or "i32" or "usize" or "isize" or "u64" or "i64" => "int",
            "f32" or "f64" => "float",
            "bool" => "bool",
            "char" => "char",
            "str" or "String" => "String",
            _ => bruto,
        };
    }

    // =================================================================
    //  comandos
    // =================================================================
    CmdBloco Bloco()
    {
        int linha = Linha;
        Exigir("{");

        var corpo = new List<Cmd>();

        while (!Eh("}") && Atual.Tipo != T.Fim)
        {
            var cmd = Comando();
            if (cmd is not null) corpo.Add(cmd);
        }

        Exigir("}");
        return new CmdBloco(corpo, linha);
    }

    Cmd Comando()
    {
        int linha = Linha;

        if (Aceitar(";")) return null;
        if (Eh("#")) { PularAtributo(); return null; }

        if (Eh("let")) return Declaracao();
        if (Eh("if")) return Se();
        if (Eh("while")) return Enquanto();
        if (Eh("loop")) return LacoSemFim();
        if (Eh("for")) return Para();
        if (Eh("match")) return Escolha();
        if (Eh("unsafe")) { Comer(); return Bloco(); }
        if (Eh("{")) return Bloco();

        if (Eh("break")) { Comer(); Aceitar(";"); return new CmdBreak(linha); }
        if (Eh("continue")) { Comer(); Aceitar(";"); return new CmdContinue(linha); }

        if (Eh("return"))
        {
            Comer();
            Expr valor = Eh(";") || Eh("}") ? null : Expressao();
            Aceitar(";");
            return new CmdRetorno(valor, linha);
        }

        var expr = Expressao();

        if (Eh("=") || Eh("+=") || Eh("-=") || Eh("*=") || Eh("/=") || Eh("%=")
            || Eh("&=") || Eh("|=") || Eh("^=") || Eh("<<=") || Eh(">>="))
        {
            string op = Comer().Texto;
            var valor = Expressao();
            Aceitar(";");
            return new CmdExpr(new Atribuicao(op, expr, valor, linha), linha);
        }

        // A última expressão de um bloco, sem ponto-e-vírgula, É o
        // valor de retorno do bloco. É o jeito idiomático de escrever
        // "return x" em Rust.
        bool ultima = Eh("}");
        Aceitar(";");

        return ultima ? new CmdRetorno(expr, linha) : new CmdExpr(expr, linha);
    }

    Cmd Declaracao()
    {
        int linha = Linha;
        Exigir("let");
        Aceitar("mut");

        // "let (a, b) = ..." desmonta uma tupla. Só o primeiro nome é
        // guardado: o interpretador não tem tupla, e inventar uma aqui
        // faria a linguagem prometer o que não cumpre.
        if (Aceitar("("))
        {
            var nomes = new List<string>();

            while (!Eh(")") && Atual.Tipo != T.Fim)
            {
                Aceitar("mut");
                nomes.Add(Comer().Texto);
                if (!Aceitar(",")) break;
            }

            Exigir(")");
            if (Aceitar(":")) Tipo();

            Expr valorDaTupla = Aceitar("=") ? Expressao() : null;
            Aceitar(";");

            return new CmdVarios(
                [.. nomes.Select(n => (Cmd)new CmdDecl("int", n, valorDaTupla, false, null, null, linha))],
                linha);
        }

        string nome = Comer().Texto;

        string tipo = "int";
        bool vetor = false;
        Expr tamanho = null;

        if (Aceitar(":"))
        {
            // "[u8; 4]" é um vetor de quatro.
            if (Eh("["))
            {
                Comer();
                tipo = Tipo();
                vetor = true;
                if (Aceitar(";")) tamanho = Expressao();
                Aceitar("]");
            }
            else tipo = Tipo();
        }

        Expr inicial = null;
        List<Expr> itens = null;

        if (Aceitar("="))
        {
            if (Eh("["))
            {
                // "[0; 4]" são quatro zeros; "[1, 2, 3]" é a lista.
                Comer();
                var primeiro = Expressao();

                if (Aceitar(";"))
                {
                    tamanho = Expressao();
                    vetor = true;
                    inicial = primeiro;
                }
                else
                {
                    itens = [primeiro];
                    while (Aceitar(",")) { if (Eh("]")) break; itens.Add(Expressao()); }
                    vetor = true;
                }

                Exigir("]");
            }
            else inicial = Expressao();
        }

        Aceitar(";");
        return new CmdDecl(tipo, nome, inicial, vetor, tamanho, itens, linha);
    }

    Cmd Se()
    {
        int linha = Linha;
        Exigir("if");

        // "if let Some(x) = ..." é casamento de padrão. Aqui vale o
        // teste de que o valor existe, que é o que ele faz na prática.
        if (Aceitar("let"))
        {
            while (!Eh("=") && Atual.Tipo != T.Fim) Comer();
            Exigir("=");
        }

        var cond = ExpressaoSemChave();
        var entao = Bloco();

        Cmd senao = null;

        if (Aceitar("else"))
            senao = Eh("if") ? Se() : Bloco();

        return new CmdSe(cond, entao, senao, linha);
    }

    Cmd Enquanto()
    {
        int linha = Linha;
        Exigir("while");

        if (Aceitar("let"))
        {
            while (!Eh("=") && Atual.Tipo != T.Fim) Comer();
            Exigir("=");
        }

        var cond = ExpressaoSemChave();
        return new CmdEnquanto(cond, Bloco(), linha);
    }

    Cmd LacoSemFim()
    {
        int linha = Linha;
        Exigir("loop");
        return new CmdEnquanto(new Literal(1, "true", true, false, linha), Bloco(), linha);
    }

    /// <summary>
    /// "for i in 0..10" e "for i in 0..=10".
    ///
    /// A diferença entre os dois é uma volta, e é a que mais pega: o
    /// ".." exclui o último e o "..=" inclui.
    /// </summary>
    Cmd Para()
    {
        int linha = Linha;
        Exigir("for");

        Aceitar("mut");
        string nome = Comer().Texto;
        Exigir("in");

        var de = ExpressaoSemChave();

        bool inclusivo = Eh("..=");

        if (!Aceitar("..") && !Aceitar("..="))
        {
            // "for x in coisa" sem intervalo: percorrer coleção. O
            // interpretador tem o "para cada" do Python, que serve.
            var corpoDaColecao = Bloco();
            return new CmdParaCada([nome], de, corpoDaColecao, linha);
        }

        var ate = ExpressaoSemChave();
        var corpo = Bloco();

        var inicio = new CmdDecl("int", nome, de, false, null, null, linha);
        var variavel = new Variavel(nome, linha);

        return new CmdPara(inicio,
            new Binaria(inclusivo ? "<=" : "<", variavel, ate, linha),
            new PosFixa("++", variavel, linha),
            corpo, linha);
    }

    /// <summary>
    /// "match x { 1 =&gt; ..., _ =&gt; ... }"
    ///
    /// Vira o switch do C, com o break que o Rust não escreve: os ramos
    /// dele não vazam para o seguinte, e sem o break aqui todos
    /// vazariam.
    /// </summary>
    Cmd Escolha()
    {
        int linha = Linha;
        Exigir("match");

        var sujeito = ExpressaoSemChave();
        Exigir("{");

        var casos = new List<CasoSwitch>();

        while (!Eh("}") && Atual.Tipo != T.Fim)
        {
            var valores = new List<Expr>();
            bool qualquer = false;

            do
            {
                if (Eh("_")) { Comer(); qualquer = true; break; }
                valores.Add(Expressao());
            }
            while (Aceitar("|"));

            Exigir("=>");

            var corpo = new List<Cmd>();

            if (Eh("{"))
            {
                corpo.AddRange(Bloco().Corpo);
            }
            else
            {
                var expr = Expressao();
                corpo.Add(new CmdExpr(expr, linha));
            }

            corpo.Add(new CmdBreak(linha));
            Aceitar(",");

            if (qualquer) casos.Add(new CasoSwitch(null, corpo));
            else foreach (var v in valores) casos.Add(new CasoSwitch(v, corpo));
        }

        Exigir("}");
        return new CmdEscolha(sujeito, casos, linha);
    }

    // =================================================================
    //  expressões
    // =================================================================
    //
    // "semChave" existe por um motivo de gramática: em "if x { }" a
    // chave abre o BLOCO, e não um literal de struct. Sem essa
    // distinção, "if ligado {" leria "ligado { ... }" como construção
    // de struct e o programa inteiro desandava.
    bool semChave;

    Expr Expressao() => Ou();

    Expr ExpressaoSemChave()
    {
        bool antes = semChave;
        semChave = true;

        try { return Ou(); }
        finally { semChave = antes; }
    }

    Expr Ou()
    {
        var esq = E();
        while (Eh("||")) { Comer(); esq = new Binaria("||", esq, E(), Linha); }
        return esq;
    }

    Expr E()
    {
        var esq = Comparacao();
        while (Eh("&&")) { Comer(); esq = new Binaria("&&", esq, Comparacao(), Linha); }
        return esq;
    }

    Expr Comparacao()
    {
        var esq = BitOu();

        while (Eh("==") || Eh("!=") || Eh("<") || Eh(">") || Eh("<=") || Eh(">="))
        {
            string op = Comer().Texto;
            esq = new Binaria(op, esq, BitOu(), Linha);
        }

        return esq;
    }

    Expr BitOu()
    {
        var esq = BitE();

        while (Eh("|") || Eh("^"))
        {
            string op = Comer().Texto;
            esq = new Binaria(op, esq, BitE(), Linha);
        }

        return esq;
    }

    Expr BitE()
    {
        var esq = Deslocamento();

        while (Eh("&") && !ProximoEh("&"))
        {
            Comer();
            esq = new Binaria("&", esq, Deslocamento(), Linha);
        }

        return esq;
    }

    Expr Deslocamento()
    {
        var esq = Soma();

        while (Eh("<<") || Eh(">>"))
        {
            string op = Comer().Texto;
            esq = new Binaria(op, esq, Soma(), Linha);
        }

        return esq;
    }

    Expr Soma()
    {
        var esq = Produto();

        while (Eh("+") || Eh("-"))
        {
            string op = Comer().Texto;
            esq = new Binaria(op, esq, Produto(), Linha);
        }

        return esq;
    }

    Expr Produto()
    {
        var esq = Unaria();

        while (Eh("*") || Eh("/") || Eh("%"))
        {
            string op = Comer().Texto;
            esq = new Binaria(op, esq, Unaria(), Linha);
        }

        return esq;
    }

    Expr Unaria()
    {
        int linha = Linha;

        if (Eh("!")) { Comer(); return new Unaria("!", Unaria(), linha); }
        if (Eh("-")) { Comer(); return new Unaria("-", Unaria(), linha); }

        // "&x" e "&mut x" são empréstimos: em tempo de execução o valor
        // é o mesmo, e o que eles garantem é verificado em compilação.
        if (Eh("&")) { Comer(); Aceitar("mut"); return Unaria(); }
        if (Eh("*")) { Comer(); return Unaria(); }

        return Sufixos(Primaria());
    }

    Expr Primaria()
    {
        int linha = Linha;
        var t = Atual;

        if (t.Tipo == T.Num) { Comer(); return new Literal(t.Valor, t.Texto, t.Inteiro, false, linha); }
        if (t.Tipo == T.Str) { Comer(); return new Literal(0, t.Texto, false, true, linha); }

        if (t.Tipo == T.Char)
        {
            Comer();
            return new Literal(t.Valor, t.Texto, true, false, linha) { Caractere = true };
        }

        if (Eh("true")) { Comer(); return new Literal(1, "true", true, false, linha); }
        if (Eh("false")) { Comer(); return new Literal(0, "false", true, false, linha); }

        if (Aceitar("("))
        {
            bool antes = semChave;
            semChave = false;

            try
            {
                var dentro = Expressao();

                // Tupla: só o primeiro sobrevive, pelo mesmo motivo do
                // "let (a, b)".
                while (Aceitar(",")) { if (Eh(")")) break; Expressao(); }

                Exigir(")");
                return dentro;
            }
            finally { semChave = antes; }
        }

        if (Eh("["))
        {
            Comer();
            bool antes = semChave;
            semChave = false;

            try
            {
                var itens = new List<Expr>();

                while (!Eh("]") && Atual.Tipo != T.Fim)
                {
                    itens.Add(Expressao());
                    if (Aceitar(";")) { Expressao(); break; }
                    if (!Aceitar(",")) break;
                }

                Exigir("]");
                return new ListaLiteral(itens, false, linha);
            }
            finally { semChave = antes; }
        }

        if (Eh("if")) return ExprDoSe();

        if (t.Tipo == T.Id)
        {
            Comer();
            var alvo = (Expr)new Variavel(t.Texto, linha);

            // Caminho: "Pin::new", "gpio::Level::High". O último nome é
            // o que importa; o caminho até ele é organização de módulo.
            while (Eh("::"))
            {
                Comer();

                if (Eh("<")) { PularGenericos(); continue; }

                string parte = Comer().Texto;
                alvo = new Membro(alvo, parte, linha);
            }

            return alvo;
        }

        throw new ErroDeSintaxe($"não esperava '{t.Texto}' numa expressão", linha);
    }

    /// <summary>
    /// "let x = if c { 1 } else { 2 };" -- o if como VALOR.
    ///
    /// Vira a ternária, que é a forma que a árvore comum tem para
    /// "escolher entre dois valores".
    /// </summary>
    Expr ExprDoSe()
    {
        int linha = Linha;
        Exigir("if");

        var cond = ExpressaoSemChave();
        var sim = ValorDeBloco();

        Exigir("else");
        var nao = Eh("if") ? ExprDoSe() : ValorDeBloco();

        return new Ternaria(cond, sim, nao, linha);
    }

    Expr ValorDeBloco()
    {
        Exigir("{");

        bool antes = semChave;
        semChave = false;

        try
        {
            var valor = Expressao();
            while (!Eh("}") && Atual.Tipo != T.Fim) Comer();
            Exigir("}");
            return valor;
        }
        finally { semChave = antes; }
    }

    Expr Sufixos(Expr alvo)
    {
        while (true)
        {
            int linha = Linha;

            if (Eh("("))
            {
                Comer();

                bool antes = semChave;
                semChave = false;

                try
                {
                    var args = new List<Expr>();

                    while (!Eh(")") && Atual.Tipo != T.Fim)
                    {
                        args.Add(Expressao());
                        if (!Aceitar(",")) break;
                    }

                    Exigir(")");
                    alvo = new Chamada(alvo, args, linha);
                }
                finally { semChave = antes; }

                continue;
            }

            if (Eh("["))
            {
                Comer();

                bool antes = semChave;
                semChave = false;

                try
                {
                    var indice = Expressao();
                    Exigir("]");
                    alvo = new Indice(alvo, indice, linha);
                }
                finally { semChave = antes; }

                continue;
            }

            if (Eh(".") && (Proximo.Tipo == T.Id || Proximo.Tipo == T.Num))
            {
                Comer();
                string campo = Comer().Texto;
                PularGenericos();
                alvo = new Membro(alvo, campo, linha);
                continue;
            }

            // "x as u8": conversão de tipo.
            if (Eh("as"))
            {
                Comer();
                alvo = new Conversao(Tipo(), alvo, linha);
                continue;
            }

            // "x?" propaga erro. Aqui o valor segue em frente: sem
            // Result de verdade, propagar é não fazer nada.
            if (Eh("?")) { Comer(); continue; }

            return alvo;
        }
    }
}
