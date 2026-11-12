namespace Simulador;

/// <summary>
/// Ada, SPARK, Cowgol e Millipascal, num analisador só.
///
/// As quatro produzem a MESMA árvore que o C++ e o MicroPython
/// produzem. É a decisão que sustenta o simulador desde a Alpha 17: o
/// interpretador, o depurador, as máquinas e todas as bibliotecas são um
/// só, e cada linguagem é apenas mais uma porta de entrada. Um
/// interpretador por linguagem criaria nove verdades sobre o que
/// "acender o pino 13" significa.
///
/// O que se ganha por juntar as quatro aqui, em vez de um arquivo cada:
/// elas divergem em pouca coisa -- a palavra que abre uma sub-rotina, se
/// o <c>loop</c> exige <c>end loop</c>, se declara com <c>var</c> --, e
/// todo o resto (expressões, <c>if</c>, chamadas, tipos) é idêntico.
/// Quatro cópias divergiriam na primeira correção feita em três delas.
///
/// O ponto de entrada é o mesmo raciocínio do <c>main()</c> em C: o
/// procedimento principal vira <c>setup()</c> mais <c>loop()</c>,
/// partido no laço infinito que todo programa de microcontrolador tem.
/// </summary>
public class ParserPascal(List<Token> tokens, DialetoPascal dialeto)
{
    int pos;

    Token Atual => tokens[Math.Min(pos, tokens.Count - 1)];
    Token Proximo => tokens[Math.Min(pos + 1, tokens.Count - 1)];
    int Linha => Atual.Linha;

    bool Eh(string texto) => Atual.Texto == texto;
    bool ProximoEh(string texto) => Proximo.Texto == texto;

    Token Comer() => tokens[Math.Min(pos++, tokens.Count - 1)];

    bool Aceitar(string texto)
    {
        if (!Eh(texto)) return false;
        pos++;
        return true;
    }

    Token Exigir(string texto)
    {
        if (!Eh(texto))
            throw new ErroDeSintaxe(Idioma.T("esperava '{0}' e veio '{1}'", texto, Atual.Texto), Linha);

        return Comer();
    }

    /// <summary>A palavra que abre uma sub-rotina neste dialeto.</summary>
    bool AbreSubrotina()
        => Eh("procedure") || Eh("function") || Eh("sub") || Eh("proc");

    /// <summary>
    /// As sub-rotinas declaradas DENTRO de outra.
    ///
    /// Em Ada e em SPARK isso é comum e idiomático: o procedimento
    /// auxiliar mora na parte declarativa de quem o usa. Aqui elas
    /// sobem para o nível de cima, porque o interpretador só tem
    /// funções globais.
    ///
    /// O que se perde com isso é o aninhamento de escopo -- em Ada a
    /// de dentro enxerga as variáveis da de fora. Na prática de um
    /// programa de placa isso quase não aparece, e o que apareceria
    /// dá erro de variável não declarada, com a linha certa, em vez de
    /// rodar errado em silêncio.
    /// </summary>
    readonly List<Funcao> aninhadas = [];

    // =================================================================
    //  o programa
    // =================================================================
    public Programa Analisar()
    {
        var globais = new List<Cmd>();
        var funcoes = new List<Funcao>();

        while (Atual.Tipo != T.Fim)
        {
            // Cabeçalhos que não mudam o que o programa FAZ: o "with
            // Ada.Text_IO; use Ada.Text_IO;" de todo programa Ada, o
            // "include" do Cowgol, o "pragma SPARK_Mode". São lidos e
            // descartados de propósito -- o simulador já traz as
            // bibliotecas, e recusar o cabeçalho recusaria o programa
            // inteiro por causa da primeira linha.
            if (Eh("with") || Eh("use") || Eh("pragma") || Eh("include")
                || Eh("package") || Eh("extern") || Eh("interface"))
            {
                PularAteOPontoEVirgula();
                continue;
            }

            if (AbreSubrotina()) { funcoes.Add(Subrotina()); continue; }

            // As de dentro entram junto com a de fora.
            if (aninhadas.Count > 0)
            {
                funcoes.AddRange(aninhadas);
                aninhadas.Clear();
                continue;
            }

            if (Eh("type") || Eh("subtype") || Eh("typedef"))
            {
                PularAteOPontoEVirgula();
                continue;
            }

            // Declaração solta no topo: variável global.
            if (Atual.Tipo == T.Id && !LexerPascal.EhReservada(Atual.Texto)
                && (ProximoEh(":") || ProximoEh(",")))
            {
                globais.AddRange(Declaracao());
                continue;
            }

            if (Eh("var") || Eh("const") || Eh("data"))
            {
                Comer();
                globais.AddRange(Declaracao());
                continue;
            }

            // Bloco principal solto, sem procedimento em volta: é como
            // um programa curto de Millipascal costuma ser escrito.
            if (Eh("begin"))
            {
                funcoes.Add(DoCorpoPrincipal(Bloco("end")));
                continue;
            }

            if (Aceitar(";")) continue;

            throw new ErroDeSintaxe(
                Idioma.T("não esperava '{0}' aqui, fora de um procedimento", Atual.Texto), Linha);
        }

        funcoes.AddRange(aninhadas);
        aninhadas.Clear();

        return Montar(globais, funcoes);
    }

    /// <summary>
    /// Junta tudo no formato que o resto do simulador conhece.
    ///
    /// Sem setup/loop na mão, o procedimento principal é partido em
    /// dois pelo laço infinito que todo firmware tem: o que vem antes é
    /// o setup, o miolo do laço é o loop. É a mesma conversão que o
    /// <c>main()</c> em C recebe, e pelo mesmo motivo.
    /// </summary>
    Programa Montar(List<Cmd> globais, List<Funcao> funcoes)
        => Compilador.PartirNoLaco(
            new Programa(globais, funcoes) { Linguagem = LinguagemDoDialeto() }, "main");

    Linguagem LinguagemDoDialeto() => dialeto switch
    {
        DialetoPascal.Spark => Linguagem.Spark,
        DialetoPascal.Cowgol => Linguagem.Cowgol,
        DialetoPascal.Millipascal => Linguagem.Millipascal,
        _ => Linguagem.Ada,
    };

    Funcao DoCorpoPrincipal(CmdBloco corpo)
        => new("void", "main", [], corpo, corpo.Linha);

    // =================================================================
    //  sub-rotinas
    // =================================================================
    Funcao Subrotina()
    {
        bool ehFuncao = Eh("function");
        Comer();

        string nome = Comer().Texto;
        var parametros = Parametros();

        string retorno = "void";

        // Ada: "function F (...) return Integer is".
        if (Aceitar("return")) { retorno = Tipo(); ehFuncao = true; }

        // Cowgol: "sub F(a: uint8): (r: uint8) is".
        if (Aceitar(":"))
        {
            retorno = Eh("(") ? TipoEntreParenteses() : Tipo();
            ehFuncao = true;
        }

        // SPARK escreve os contratos entre o cabeçalho e o corpo. Eles
        // são LIDOS e descartados: provar o contrato é trabalho de uma
        // ferramenta de prova, e não de um simulador -- mas recusar o
        // programa por causa deles seria recusar SPARK inteiro.
        PularContratos();

        // Ada permite só a declaração, sem corpo. Ela não vira função.
        if (Aceitar(";")) return new Funcao(retorno, nome, parametros,
                                            new CmdBloco([], Linha), Linha);

        Aceitar("is");
        Aceitar("do");

        var locais = new List<Cmd>();

        // Entre o "is" e o "begin" ficam as declarações locais. Em
        // Cowgol e Millipascal elas vêm com "var" e não há "begin"
        // separado; nos dois casos o laço abaixo as recolhe.
        while (!Eh("begin") && Atual.Tipo != T.Fim)
        {
            if (Eh("var") || Eh("const")) { Comer(); locais.AddRange(Declaracao()); continue; }

            // Sub-rotina dentro da parte declarativa: ela sobe para o
            // nível de cima. Sem este ramo, o analisador parava aqui e
            // lia o corpo da de dentro como se fosse o da de fora --
            // e o programa compilava sem fazer nada.
            if (AbreSubrotina())
            {
                var dentro = Subrotina();

                // Só o corpo entra. A declaração sem corpo -- o
                // "spec" que Ada permite -- não vira função nenhuma.
                if (dentro.Corpo.Corpo.Count > 0) aninhadas.Add(dentro);
                continue;
            }

            if (Eh("type") || Eh("subtype") || Eh("pragma") || Eh("use"))
            {
                PularAteOPontoEVirgula();
                continue;
            }

            if (Atual.Tipo == T.Id && !LexerPascal.EhReservada(Atual.Texto)
                && (ProximoEh(":") || ProximoEh(",")))
            {
                locais.AddRange(Declaracao());
                continue;
            }

            if (Aceitar(";")) continue;
            break;
        }

        // Cowgol e Millipascal não escrevem "begin": o corpo começa
        // depois das declarações e vai até o "end".
        Aceitar("begin");

        var corpo = Bloco("end");

        // "end;", "end Nome;", "end sub;", "end proc;" -- todos
        // válidos, e todos curtos: no máximo três símbolos até o
        // ponto-e-vírgula. O limite importa porque sem ele um "end"
        // sem ponto-e-vírgula -- que o Millipascal aceita -- fazia o
        // laço engolir o resto do arquivo.
        Aceitar("end");

        for (int i = 0; i < 3 && Atual.Tipo != T.Fim && !Eh(";"); i++)
        {
            if (Eh("begin") || AbreSubrotina() || Eh("var") || Eh("const")) break;
            Comer();
        }

        Aceitar(";");

        var todos = new List<Cmd>(locais);
        todos.AddRange(corpo.Corpo);

        return new Funcao(retorno, nome, parametros, new CmdBloco(todos, corpo.Linha), corpo.Linha);
    }

    List<Param> Parametros()
    {
        var saida = new List<Param>();
        if (!Aceitar("(")) return saida;

        while (!Eh(")") && Atual.Tipo != T.Fim)
        {
            var nomes = new List<string> { Comer().Texto };
            while (Aceitar(",")) nomes.Add(Comer().Texto);

            string tipo = "int";
            if (Aceitar(":"))
            {
                // "in", "out" e "in out" dizem a direção, e não o tipo.
                Aceitar("in");
                Aceitar("out");
                Aceitar("inout");
                tipo = Tipo();
            }

            foreach (string n in nomes) saida.Add(new Param(tipo, n));

            if (!Aceitar(";")) Aceitar(",");
        }

        Exigir(")");
        return saida;
    }

    string TipoEntreParenteses()
    {
        Exigir("(");
        while (!Eh(":") && !Eh(")") && Atual.Tipo != T.Fim) Comer();

        string tipo = Aceitar(":") ? Tipo() : "int";

        while (!Eh(")") && Atual.Tipo != T.Fim) Comer();
        Exigir(")");
        return tipo;
    }

    /// <summary>
    /// O nome do tipo, traduzido para o que o interpretador entende.
    ///
    /// Aqui o tipo importa pouco -- o interpretador é dinâmico --, mas
    /// importa o bastante para separar inteiro de real e para o cálculo
    /// de memória da placa saber quantos bytes a variável ocupa.
    /// </summary>
    string Tipo()
    {
        if (Atual.Tipo != T.Id) return "int";

        string bruto = Comer().Texto;

        // "array (0 .. 9) of Integer", "array[10] of uint8".
        if (bruto == "array")
        {
            while (!Eh("of") && Atual.Tipo != T.Fim) Comer();
            Aceitar("of");
            return Tipo();
        }

        // Ada escreve "Integer range 0 .. 100".
        if (Eh("range")) PularAteOPontoEVirgulaSemComer();

        return Traduzir(bruto);
    }

    static string Traduzir(string tipo) => tipo.ToLowerInvariant() switch
    {
        "integer" or "natural" or "positive" or "int" or "i32" or "int32"
            or "int16" or "i16" or "short" => "int",

        "uint8" or "u8" or "byte" or "unsigned_char" => "byte",
        "int8" or "i8" => "char",
        "uint16" or "u16" or "word" => "word",
        "uint32" or "u32" or "int64" or "i64" or "uint64" or "u64"
            or "long_integer" => "long",

        "float" or "real" or "double" or "long_float" or "f32" or "f64" => "float",
        "boolean" or "bool" => "bool",
        "character" or "char" => "char",
        "string" => "String",

        _ => tipo,
    };

    // =================================================================
    //  comandos
    // =================================================================
    CmdBloco Bloco(params string[] fechos)
    {
        int linha = Linha;
        var corpo = new List<Cmd>();

        while (Atual.Tipo != T.Fim && !fechos.Contains(Atual.Texto))
        {
            var cmd = Comando();
            if (cmd is not null) corpo.Add(cmd);
        }

        return new CmdBloco(corpo, linha);
    }

    Cmd Comando()
    {
        int linha = Linha;

        if (Aceitar(";")) return null;

        if (Eh("if")) return Se();
        if (Eh("while")) return Enquanto();
        if (Eh("loop")) return LacoSemFim();
        if (Eh("for")) return Para();
        if (Eh("case")) return Escolha();
        if (Eh("declare") || Eh("begin")) return BlocoAninhado();

        if (Eh("return"))
        {
            Comer();
            Expr valor = Eh(";") ? null : Expressao();
            Aceitar(";");
            return new CmdRetorno(valor, linha);
        }

        if (Eh("exit"))
        {
            Comer();

            // "exit when C;" é o jeito de Ada escrever "if C then break".
            if (Aceitar("when"))
            {
                var cond = Expressao();
                Aceitar(";");
                return new CmdSe(cond, new CmdBreak(linha), null, linha);
            }

            Aceitar(";");
            return new CmdBreak(linha);
        }

        if (Eh("null")) { Comer(); Aceitar(";"); return new CmdVazio(linha); }

        // Millipascal atribui com "set x = v;".
        if (Eh("set"))
        {
            Comer();
            var alvo = Primaria();
            Exigir(Eh(":=") ? ":=" : "=");
            var valor = Expressao();
            Aceitar(";");
            return new CmdExpr(new Atribuicao("=", alvo, valor, linha), linha);
        }

        if (Eh("var") || Eh("const"))
        {
            Comer();
            var decls = Declaracao();
            return decls.Count == 1 ? decls[0] : new CmdVarios(decls, linha);
        }

        // Declaração sem palavra na frente: "X : Integer := 0;".
        if (Atual.Tipo == T.Id && !LexerPascal.EhReservada(Atual.Texto) && ProximoEh(":")
            && Proximo.Tipo == T.Op)
        {
            var decls = Declaracao();
            return decls.Count == 1 ? decls[0] : new CmdVarios(decls, linha);
        }

        // Sobrou: atribuição ou chamada.
        var expr = Expressao();

        if (Eh(":=") || Eh("="))
        {
            Comer();
            var valor = Expressao();
            Aceitar(";");
            return new CmdExpr(new Atribuicao("=", expr, valor, linha), linha);
        }

        Aceitar(";");
        return new CmdExpr(expr, linha);
    }

    Cmd BlocoAninhado()
    {
        int linha = Linha;
        var locais = new List<Cmd>();

        if (Aceitar("declare"))
        {
            while (!Eh("begin") && Atual.Tipo != T.Fim)
            {
                if (Eh("var") || Eh("const")) Comer();
                locais.AddRange(Declaracao());
            }
        }

        Exigir("begin");
        var corpo = Bloco("end");
        Exigir("end");
        Aceitar(";");

        var todos = new List<Cmd>(locais);
        todos.AddRange(corpo.Corpo);
        return new CmdBloco(todos, linha);
    }

    Cmd Se()
    {
        int linha = Linha;
        Exigir("if");

        var cond = Expressao();
        Aceitar("then");

        // Millipascal abre o ramo com "begin" em vez de "then".
        Aceitar("begin");

        var entao = Bloco("elsif", "elif", "else", "end");
        Cmd senao = null;

        if (Eh("elsif") || Eh("elif"))
        {
            // Um "elsif" é um "if" inteiro dentro do senão: assim o
            // "end if" de baixo fecha a corrente toda de uma vez, que é
            // exatamente o que a linguagem quer dizer.
            senao = SeEncadeado();
            return new CmdSe(cond, entao, senao, linha);
        }

        if (Aceitar("else"))
        {
            Aceitar("begin");
            senao = Bloco("end");
        }

        Exigir("end");
        Aceitar("if");
        Aceitar(";");

        return new CmdSe(cond, entao, senao, linha);
    }

    Cmd SeEncadeado()
    {
        int linha = Linha;
        Comer();   // elsif / elif

        var cond = Expressao();
        Aceitar("then");
        Aceitar("begin");

        var entao = Bloco("elsif", "elif", "else", "end");
        Cmd senao = null;

        if (Eh("elsif") || Eh("elif")) return new CmdSe(cond, entao, SeEncadeado(), linha);

        if (Aceitar("else"))
        {
            Aceitar("begin");
            senao = Bloco("end");
        }

        Exigir("end");
        Aceitar("if");
        Aceitar(";");

        return new CmdSe(cond, entao, senao, linha);
    }

    Cmd Enquanto()
    {
        int linha = Linha;
        Exigir("while");

        var cond = Expressao();
        Aceitar("loop");
        Aceitar("do");
        Aceitar("begin");

        var corpo = Bloco("end");

        Exigir("end");
        Aceitar("loop");
        Aceitar("while");
        Aceitar(";");

        return new CmdEnquanto(cond, corpo, linha);
    }

    Cmd LacoSemFim()
    {
        int linha = Linha;
        Exigir("loop");
        Aceitar("begin");

        var corpo = Bloco("end");

        Exigir("end");
        Aceitar("loop");
        Aceitar(";");

        return new CmdEnquanto(new Literal(1, "true", true, false, linha), corpo, linha);
    }

    /// <summary>
    /// "for I in 1 .. 10 loop", e a versão ao contrário com "reverse".
    ///
    /// Vira o <c>for</c> do C: a variável nasce no laço, anda de um em
    /// um e para no fim do intervalo. O detalhe que importa é que em
    /// Ada o limite é INCLUSIVO -- "1 .. 10" passa pelo 10 --, e usar
    /// "&lt;" aqui comeria a última volta de todo programa.
    /// </summary>
    Cmd Para()
    {
        int linha = Linha;
        Exigir("for");

        string nome = Comer().Texto;
        Aceitar("in");

        bool aoContrario = Aceitar("reverse");

        var de = Expressao();
        Exigir("..");
        var ate = Expressao();

        Aceitar("loop");
        Aceitar("do");
        Aceitar("begin");

        var corpo = Bloco("end");

        Exigir("end");
        Aceitar("loop");
        Aceitar(";");

        var inicio = new CmdDecl("int", nome, aoContrario ? ate : de, false, null, null, linha);
        var variavel = new Variavel(nome, linha);

        var cond = new Binaria(aoContrario ? ">=" : "<=", variavel,
                               aoContrario ? de : ate, linha);

        var passo = new PosFixa(aoContrario ? "--" : "++", variavel, linha);

        return new CmdPara(inicio, cond, passo, corpo, linha);
    }

    /// <summary>
    /// "case X is when 1 => ...; when others => ...; end case;"
    ///
    /// Vira o <c>switch</c> do C, com uma diferença que precisa ser
    /// respeitada: em Ada cada ramo termina sozinho, sem <c>break</c>.
    /// Por isso o break entra aqui, e não fica faltando.
    /// </summary>
    Cmd Escolha()
    {
        int linha = Linha;
        Exigir("case");

        var sujeito = Expressao();
        Aceitar("is");
        Aceitar("of");
        Aceitar("begin");

        var casos = new List<CasoSwitch>();

        while (Eh("when") || (Atual.Tipo != T.Fim && !Eh("end")))
        {
            if (!Aceitar("when")) break;

            var valores = new List<Expr>();
            bool ehOutros = false;

            do
            {
                if (Aceitar("others")) { ehOutros = true; break; }
                valores.Add(Expressao());
            }
            while (Aceitar("|"));

            Aceitar("=>");
            Aceitar(":");

            var corpo = Bloco("when", "end");
            corpo.Corpo.Add(new CmdBreak(corpo.Linha));

            if (ehOutros) casos.Add(new CasoSwitch(null, corpo.Corpo));
            else foreach (var v in valores) casos.Add(new CasoSwitch(v, corpo.Corpo));
        }

        Exigir("end");
        Aceitar("case");
        Aceitar(";");

        return new CmdEscolha(sujeito, casos, linha);
    }

    /// <summary>
    /// "A, B : Integer := 0;" -- uma linha pode declarar várias.
    /// </summary>
    List<Cmd> Declaracao()
    {
        var saida = new List<Cmd>();

        // Um grupo e "a, b : Integer := 0" (Ada) ou "brilho:i32"; o
        // Millipascal poe varios grupos na mesma linha, separados por
        // virgula: "var brilho:i32, passo:i32".
        do
        {
            int linha = Linha;

            var nomes = new List<string> { Comer().Texto };
            while (Eh(",") && !InicioDeGrupo()) { Comer(); nomes.Add(Comer().Texto); }

            string tipo = "int";
            Expr tamanho = null;

            if (Aceitar(":"))
            {
                Aceitar("constant");
                Aceitar("aliased");

                // "array (0 .. 9) of Integer" e "array[10] of uint8": o que
                // interessa é quantos cabem.
                if (Eh("array"))
                {
                    Comer();
                    tamanho = TamanhoDoVetor();
                    Aceitar("of");
                }

                tipo = Tipo();
            }

            Expr inicial = null;
            if (Aceitar(":=") || Aceitar("=")) inicial = Expressao();

            saida.AddRange(nomes.Select(n => (Cmd)new CmdDecl(
                tipo, n, inicial, tamanho is not null, tamanho, null, linha)));
        }
        while (Aceitar(","));

        Aceitar(";");
        return saida;
    }

    /// <summary>
    /// A virgula abre um grupo novo ("x:i32, y:i32"), e nao so mais um
    /// nome do mesmo grupo ("a, b : Integer")? E grupo quando depois do
    /// nome vem ":" -- e o nome de antes ja tinha tipo.
    /// </summary>
    bool InicioDeGrupo()
        => pos + 2 < tokens.Count && tokens[pos + 1].Tipo == T.Id && tokens[pos + 2].Texto == ":"
           && pos > 0 && tokens[pos - 1].Tipo == T.Id && TemTipoAntes();

    /// <summary>O grupo atual ja passou do ":"? (o nome antes da virgula e o tipo)</summary>
    bool TemTipoAntes() => pos > 1 && tokens[pos - 2].Texto == ":";

    /// <summary>
    /// Quantos itens cabem: "(0 .. 9)" são dez, "[10]" são dez.
    /// </summary>
    Expr TamanhoDoVetor()
    {
        int linha = Linha;
        bool colchete = Eh("[");

        if (!Aceitar("(") && !Aceitar("[")) return null;

        var primeiro = Expressao();

        if (Aceitar(".."))
        {
            var ultimo = Expressao();
            Aceitar(colchete ? "]" : ")");

            // Intervalo inclusivo: de 0 a 9 são dez posições.
            if (primeiro is Literal { EhTexto: false } a
                && ultimo is Literal { EhTexto: false } b)
                return new Literal(b.Valor - a.Valor + 1, "", true, false, linha);

            return new Binaria("+",
                new Binaria("-", ultimo, primeiro, linha),
                new Literal(1, "1", true, false, linha), linha);
        }

        Aceitar(colchete ? "]" : ")");
        return primeiro;
    }

    void PularAteOPontoEVirgula()
    {
        while (Atual.Tipo != T.Fim && !Eh(";")) Comer();
        Aceitar(";");
    }

    void PularAteOPontoEVirgulaSemComer()
    {
        while (Atual.Tipo != T.Fim && !Eh(";") && !Eh(":=") && !Eh(")")) Comer();
    }

    /// <summary>
    /// Os contratos de SPARK, lidos e descartados.
    ///
    /// "with Pre =&gt; X &gt; 0, Post =&gt; Y = X + 1" diz o que a
    /// sub-rotina promete. Provar isso é trabalho de uma ferramenta de
    /// prova; aqui o que importa é não recusar o programa por causa
    /// deles -- um SPARK sem contratos não é SPARK.
    /// </summary>
    void PularContratos()
    {
        if (!Eh("with") && !Eh("global") && !Eh("depends")
            && !Eh("pre") && !Eh("post")) return;

        int fundo = 0;

        // Para NO ponto-e-vírgula, sem comê-lo. Quem decide o que ele
        // significa é quem chamou: com ";" logo depois, a sub-rotina é
        // só uma declaração sem corpo; com "is", o corpo vem a seguir.
        // Comer o ";" aqui fazia o analisador ler a PRÓXIMA sub-rotina
        // como corpo desta.
        while (Atual.Tipo != T.Fim)
        {
            if (Eh("(")) fundo++;
            else if (Eh(")")) fundo--;
            else if (fundo == 0 && (Eh("is") || Eh(";"))) return;

            Comer();
        }
    }

    // =================================================================
    //  expressões
    // =================================================================
    //
    // A precedência é a de Ada, que é a mesma do Pascal: "and" e "or"
    // ficam abaixo das comparações, e o "not" é unário. A diferença que
    // pega quem vem do C é que "=" COMPARA -- atribuir é ":=".
    Expr Expressao() => Ou();

    Expr Ou()
    {
        var esq = E();

        while (Eh("or") || Eh("xor"))
        {
            string op = Comer().Texto == "xor" ? "^" : "||";

            // "or else" é o mesmo "or" com curto-circuito.
            Aceitar("else");
            esq = new Binaria(op, esq, E(), Linha);
        }

        return esq;
    }

    Expr E()
    {
        var esq = Comparacao();

        while (Eh("and"))
        {
            Comer();
            Aceitar("then");
            esq = new Binaria("&&", esq, Comparacao(), Linha);
        }

        return esq;
    }

    Expr Comparacao()
    {
        var esq = Deslocamento();

        while (Eh("=") || Eh("/=") || Eh("<") || Eh(">") || Eh("<=") || Eh(">=") || Eh("<>")
               || Eh("==") || Eh("!="))
        {
            string bruto = Comer().Texto;

            string op = bruto switch
            {
                "=" or "==" => "==",
                "/=" or "<>" or "!=" => "!=",
                _ => bruto,
            };

            esq = new Binaria(op, esq, Deslocamento(), Linha);
        }

        return esq;
    }

    /// <summary>"&lt;&lt;" e "&gt;&gt;" do Cowgol e do Millipascal, abaixo da soma, como no C.</summary>
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

    /// <summary>
    /// Nas linguagens com jeito de C, o "&amp;" e o "|" sao de bit. Em Ada o
    /// "&amp;" junta textos, e o "|" separa as opcoes do "when".
    /// </summary>
    bool OperadoresDeBit => dialeto is DialetoPascal.Cowgol or DialetoPascal.Millipascal;

    Expr Soma()
    {
        var esq = Produto();

        while (Eh("+") || Eh("-") || Eh("&") || (OperadoresDeBit && (Eh("|") || Eh("^"))))
        {
            // "&" junta textos em Ada. Somar texto é o que o
            // interpretador já faz com "+".
            string bruto = Comer().Texto;
            string op = bruto == "&" && !OperadoresDeBit ? "+" : bruto;
            esq = new Binaria(op, esq, Produto(), Linha);
        }

        return esq;
    }

    Expr Produto()
    {
        var esq = Unaria();

        while (Eh("*") || Eh("/") || Eh("mod") || Eh("rem") || Eh("%"))
        {
            string bruto = Comer().Texto;
            string op = bruto is "mod" or "rem" ? "%" : bruto;
            esq = new Binaria(op, esq, Unaria(), Linha);
        }

        return esq;
    }

    Expr Unaria()
    {
        int linha = Linha;

        if (Eh("not")) { Comer(); return new Unaria("!", Unaria(), linha); }
        if (Eh("~")) { Comer(); return new Unaria("~", Unaria(), linha); }
        if (Eh("-")) { Comer(); return new Unaria("-", Unaria(), linha); }
        if (Eh("+")) { Comer(); return Unaria(); }

        if (Eh("abs"))
        {
            Comer();
            return new Chamada(new Variavel("abs", linha), [Unaria()], linha);
        }

        return Potencia();
    }

    Expr Potencia()
    {
        var baseDoCalculo = Primaria();

        if (!Eh("**")) return baseDoCalculo;

        Comer();
        return new Chamada(new Variavel("pow", Linha), [baseDoCalculo, Unaria()], Linha);
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

        if (Aceitar("("))
        {
            var dentro = Expressao();
            Exigir(")");
            return Sufixos(dentro);
        }

        if (Eh("true")) { Comer(); return new Literal(1, "true", true, false, linha); }
        if (Eh("false")) { Comer(); return new Literal(0, "false", true, false, linha); }
        if (Eh("null")) { Comer(); return new Literal(0, "null", true, false, linha); }

        if (Eh("new")) { Comer(); return Primaria(); }

        if (t.Tipo == T.Id)
        {
            Comer();
            return Sufixos(new Variavel(t.Texto, linha));
        }

        throw new ErroDeSintaxe(Idioma.T("não esperava '{0}' numa expressão", t.Texto), linha);
    }

    /// <summary>
    /// O que pode vir depois de um nome: chamada, índice, campo, atributo.
    ///
    /// Em Ada o parêntese faz as duas coisas -- chamar e indexar --, e
    /// só o tipo do nome desfaz. O interpretador resolve isso em tempo
    /// de execução, então aqui sai sempre chamada e ele decide.
    /// </summary>
    Expr Sufixos(Expr alvo)
    {
        while (true)
        {
            int linha = Linha;

            if (Eh("("))
            {
                Comer();
                var args = new List<Expr>();

                while (!Eh(")") && Atual.Tipo != T.Fim)
                {
                    // Argumento com nome: "F (Pino => 13)".
                    if (Atual.Tipo == T.Id && ProximoEh("=>"))
                    {
                        string nome = Comer().Texto;
                        Comer();
                        args.Add(new ArgNomeado(nome, Expressao(), linha));
                    }
                    else args.Add(Expressao());

                    if (!Aceitar(",")) break;
                }

                Exigir(")");

                // Um nome seguido de parênteses com UM argumento pode
                // ser um índice. Quem sabe é o interpretador, que tem
                // o valor na mão; aqui sai chamada e ele converte.
                alvo = new Chamada(alvo, args, linha);
                continue;
            }

            if (Eh("["))
            {
                Comer();
                var indice = Expressao();
                Exigir("]");
                alvo = new Indice(alvo, indice, linha);
                continue;
            }

            if (Eh(".") && Proximo.Tipo == T.Id)
            {
                Comer();
                alvo = new Membro(alvo, Comer().Texto, linha);
                continue;
            }

            // Atributo de Ada: X'Length, X'First. Vira campo, que é o
            // mais perto que a árvore comum tem.
            if (Eh("'") && Proximo.Tipo == T.Id)
            {
                Comer();
                alvo = new Membro(alvo, Comer().Texto, linha);
                continue;
            }

            return alvo;
        }
    }
}
