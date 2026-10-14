namespace Simulador;

/// <summary>
/// Descida recursiva sobre o subconjunto de C++/Arduino que a turma usa.
/// Nao e um compilador de C++: aceita o que os sketches precisam e
/// recusa o resto com erro de linha legivel.
///
/// Desde a Alpha 19 o subconjunto inclui o C++ das bibliotecas: classe
/// com construtor e metodos, heranca, "->", new, typedef, namespace,
/// prototipo de funcao, parametro por referencia e valor padrao. E o que
/// um .h e um .cpp de biblioteca de fora tem por dentro.
/// </summary>
public class Parser(List<Token> toks)
{
    int p = 0;

    Token Atual => toks[p];
    Token Espiar(int n = 1) => toks[Math.Min(p + n, toks.Count - 1)];
    Token Olhar(int k) => toks[Math.Clamp(k, 0, toks.Count - 1)];
    Token Avancar() => toks[p++];
    /// <summary>
    /// O token atual e este simbolo ou esta palavra?
    ///
    /// O TIPO importa. Sem ele, o texto de um literal valia como
    /// simbolo: um sketch com Serial.print(")") tinha a string ")"
    /// contada como o fecha-parenteses da chamada, e o erro saia tres
    /// tokens depois, falando de um ponto-e-virgula que nada tinha a ver
    /// com o problema. So operador e palavra podem casar aqui.
    /// </summary>
    bool Checar(string s) => Atual.Tipo is T.Op or T.Id && Atual.E(s);

    static bool Eh(Token t, string s) => t.Tipo is T.Op or T.Id && t.E(s);

    bool Aceitar(string s)
    {
        if (!Checar(s)) return false;
        p++; return true;
    }

    Token Exigir(string s)
    {
        if (!Checar(s))
            throw new ErroDeSintaxe($"esperava '{s}' mas encontrei '{Atual.Texto}'", Atual.Linha);
        return Avancar();
    }

    static readonly HashSet<string> Tipos =
    [
        "void","int","long","short","char","float","double","bool","boolean","byte",
        "unsigned","signed","const","static","volatile","String","word",

        // Qualificadores que nao mudam o valor, so o compilador. Um
        // projeto de varios arquivos comeca com "constexpr uint32_t
        // PERIODO = 40;" e "extern GnssState gnss;" -- sem eles na
        // lista, o parser lia o qualificador como o TIPO e parava no
        // nome que vinha depois.
        "constexpr","extern","inline","register","auto","mutable","virtual","explicit",
        "uint8_t","uint16_t","uint32_t","uint64_t","int8_t","int16_t","int32_t","int64_t","size_t",
        "uintptr_t","intptr_t","ptrdiff_t","ssize_t","time_t","wchar_t","char16_t","char32_t",

        // Do SDK da Pico: "uint" e o unsigned int dele, e o tempo tem tipo
        // proprio. O exemplo de PWM da Raspberry comeca com "uint slice".
        "uint","absolute_time_t",

        // Os tipos do ESP-IDF que o sono profundo e os pinos do RTC pedem:
        // "(gpio_num_t)PINO" e o cast que o C++ exige para passar um int.
        "gpio_num_t","esp_err_t","esp_sleep_wakeup_cause_t","esp_sleep_source_t",
        "esp_reset_reason_t","esp_sleep_ext1_wakeup_mode_t","esp_deepsleep_gpio_wake_up_mode_t",
        "gpio_mode_t","gpio_pull_mode_t","gpio_int_type_t","esp_sleep_pd_domain_t","esp_sleep_pd_option_t"
    ];

    /// <summary>Qualificadores: dizem como guardar, nao o que e.</summary>
    static readonly HashSet<string> Qualificadores =
    [
        "const","static","volatile","constexpr","extern","inline","register","mutable",
        "virtual","explicit"
    ];

    static readonly HashSet<string> Palavras =
    [
        "if","else","while","for","return","break","continue","do","switch",
        "case","default","enum","new","delete","typedef","using","namespace","template",
        "class","struct","union","operator","friend","public","private","protected",
        "goto","throw","try","catch","static_assert","sizeof","this"
    ];

    /// <summary>
    /// Tipos que valem NESTE sketch: os embutidos mais os que o proprio
    /// sketch declara.
    ///
    /// Precisa ser por Parser, e nao estatico como a lista de cima: um
    /// "enum Modo" declarado num sketch nao pode virar tipo conhecido do
    /// sketch seguinte. E o enum tem que virar tipo, senao a linha
    /// "Modo modoAtual = ALTERNADO;" nao e reconhecida como declaracao.
    /// </summary>
    readonly HashSet<string> tipos = [.. Tipos];

    /// <summary>Classes com comportamento: declarar uma e montar um objeto.</summary>
    readonly HashSet<string> classes = [];

    /// <summary>Structs so de campos: o "= {1, 2}" delas preenche campo a campo.</summary>
    readonly HashSet<string> estruturas = [];

    /// <summary>"typedef uint8_t pino_t;" e "using Pino = uint8_t;": o nome e o tipo de verdade.</summary>
    readonly Dictionary<string, string> apelidos = [];

    /// <summary>Namespaces declarados: "Bounce2::Button" e so "Button".</summary>
    readonly HashSet<string> espacos = [];

    /// <summary>Classes com template: "Fila&lt;int&gt; f" e uma Fila.</summary>
    readonly HashSet<string> modelos = [];

    readonly List<ClasseDecl> declaradas = [];

    /// <summary>O ultimo tipo lido tinha "extern" / "&amp;" / "static".</summary>
    bool ultimoExterno, ultimoReferencia, ultimoEstatico;

    // ================= topo =================
    public Programa AnalisarPrograma()
    {
        var globais = new List<Cmd>();
        var funcoes = new List<Funcao>();

        while (Atual.Tipo != T.Fim) AnalisarNoTopo(globais, funcoes);

        return new Programa(globais, funcoes) { Classes = declaradas };
    }

    void AnalisarNoTopo(List<Cmd> globais, List<Funcao> funcoes)
    {
        secaoDoAtributo = null;
        int antes = globais.Count;
        try { AnalisarNoTopoSemSecao(globais, funcoes); }
        finally
        {
            if (secaoDoAtributo is not null) MarcarSecao(globais, antes, secaoDoAtributo);
            secaoDoAtributo = null;
        }
    }

    void AnalisarNoTopoSemSecao(List<Cmd> globais, List<Funcao> funcoes)
    {
        if (Aceitar(";")) return;
        PularAtributos();
        if (Atual.Tipo == T.Fim) return;

        if (Checar("template")) { AnalisarModelo(() => AnalisarNoTopo(globais, funcoes)); return; }
        if (Checar("namespace")) { AnalisarEspaco(globais, funcoes); return; }
        if (Checar("using")) { AnalisarUsing(); return; }
        if (Checar("static_assert")) { PularAte(";"); return; }

        // extern "C" { ... } e extern "C" void f();
        if (Checar("extern") && Espiar().Tipo == T.Str)
        {
            p += 2;
            if (Aceitar("{"))
            {
                while (!Checar("}") && Atual.Tipo != T.Fim) AnalisarNoTopo(globais, funcoes);
                Exigir("}");
            }
            return;
        }

        if (Checar("typedef"))
        {
            globais.AddRange(AnalisarTypedef());
            return;
        }

        if (Checar("struct") || Checar("class") || Checar("union"))
        {
            // "struct tm agora;": a palavra e enfeite, o que vem depois e o tipo.
            if (EhDeclaracaoComPalavra()) p++;
            else
            {
                globais.AddRange(AnalisarClasse(apelidada: false));
                return;
            }
        }

        if (Checar("enum"))
        {
            globais.AddRange(AnalisarEnum());
            Aceitar(";");
            return;
        }

        if (EhConstrutorDeFora())
        {
            funcoes.Add(AnalisarConstrutorDeFora());
            return;
        }

        // "ISR(TIMER1_COMPA_vect) { ... }": a rotina de interrupcao do AVR,
        // presa a um timer do chip. Vira uma funcao que nada chama -- quem
        // a chamaria e o hardware do timer, que o C++ do simulador nao tem.
        if (Atual.Tipo == T.Id && Atual.Texto is "ISR" or "SIGNAL" or "INTERRUPT" && Eh(Espiar(), "("))
        {
            int linhaIsr = Atual.Linha;
            p += 2;
            string vetor = Atual.Tipo == T.Id ? Avancar().Texto : "vetor";
            while (!Checar(")") && Atual.Tipo != T.Fim) p++;
            Exigir(")");
            funcoes.Add(new Funcao("void", "__isr_" + vetor, [], AnalisarBloco(), linhaIsr));
            return;
        }

        int linha = Atual.Linha;
        string tipo = TentarTipo(local: false);
        if (tipo is null)
            throw new ErroDeSintaxe(
                $"esperava uma declaracao ou funcao, encontrei '{Atual.Texto}'", Atual.Linha);

        bool externo = ultimoExterno, referencia = ultimoReferencia;
        string nome = LerPonteiroDeFuncao();
        if (nome is not null) tipo = "funcao*";
        else nome = LerNomeDeclarado();

        if (Checar("(") && EhFuncao(tipo))
        {
            funcoes.Add(AnalisarFuncao(tipo, nome, linha));
            return;
        }

        globais.AddRange(AnalisarVariaveis(tipo, nome, linha, externo, referencia));
        Exigir(";");
    }

    bool EhInicioDeDeclaracao()
        => Atual.Tipo == T.Id && (tipos.Contains(Atual.Texto) || apelidos.ContainsKey(Atual.Texto));

    /// <summary>"struct tm agora;" -- e nao "struct Ponto { ... }".</summary>
    bool EhDeclaracaoComPalavra()
        => Espiar().Tipo == T.Id && !Eh(Espiar(2), "{") && !Eh(Espiar(2), ":") && !Eh(Espiar(2), ";")
           && !Eh(Espiar(2), "final")
           && (Espiar(2).Tipo == T.Id || Eh(Espiar(2), "*") || Eh(Espiar(2), "&"));

    /// <summary>Tira o namespace da frente: "Bounce2::Button" e "Button".</summary>
    string SemEspaco(string nome)
    {
        if (nome is null) return null;
        if (nome.StartsWith("::", StringComparison.Ordinal) && nome.IndexOf("::", 2, StringComparison.Ordinal) > 0)
            nome = nome[2..];

        while (true)
        {
            int k = nome.IndexOf("::", StringComparison.Ordinal);
            if (k <= 0 || !espacos.Contains(nome[..k])) return nome;
            nome = nome[(k + 2)..];
        }
    }

    /// <summary>O tipo de verdade por tras de um apelido.</summary>
    string Resolver(string nome)
    {
        for (int k = 0; k < 8 && nome is not null && apelidos.TryGetValue(nome, out var alvo); k++)
            nome = alvo;
        return nome;
    }

    static string SemPonteiro(string tipo) => tipo?.TrimEnd('*', ' ') ?? "";

    /// <summary>Tipo cuja variavel e um objeto montado por construtor.</summary>
    bool EhObjeto(string tipo)
    {
        tipo = SemPonteiro(tipo);
        if (classes.Contains(tipo)) return true;
        if (tipo.Contains(' ') || tipos.Contains(tipo) || apelidos.ContainsKey(tipo)) return false;
        return tipo.Length > 0 && tipo != "auto";
    }

    // ---------------- atributos, pulos ----------------

    /// <summary>
    /// A secao do ultimo "__attribute__((section(".rtc.data")))" -- e o que
    /// o RTC_DATA_ATTR da ESP32 vira. A declaracao que vem junto e marcada
    /// com ela.
    /// </summary>
    string secaoDoAtributo;

    /// <summary>"[[nodiscard]]", "__attribute__((packed))": so falam com o compilador.</summary>
    void PularAtributos()
    {
        while (true)
        {
            if (Checar("[") && Eh(Espiar(), "[")) { p = Fecha(p) + 1; continue; }
            if (Atual.Tipo == T.Id && Atual.Texto is "__attribute__" or "__declspec" or "alignas"
                && Eh(Espiar(), "("))
            {
                p++;
                int fim = Fecha(p);
                for (int k = p; k + 2 < fim; k++)
                    if (toks[k].Tipo == T.Id && toks[k].Texto == "section" && toks[k + 2].Tipo == T.Str)
                        secaoDoAtributo = toks[k + 2].Texto;
                p = fim + 1;
                continue;
            }
            return;
        }
    }

    /// <summary>As globais que acabaram de entrar levam a secao pedida.</summary>
    static void MarcarSecao(List<Cmd> globais, int desde, string secao)
    {
        for (int i = desde; i < globais.Count; i++)
        {
            if (globais[i] is CmdDecl d) globais[i] = d with { Secao = secao };
            else if (globais[i] is CmdVarios v)
                for (int j = 0; j < v.Corpo.Count; j++)
                    if (v.Corpo[j] is CmdDecl dj) v.Corpo[j] = dj with { Secao = secao };
        }
    }

    /// <summary>O indice que fecha o parentese, colchete ou chave aberto em <paramref name="abre"/>.</summary>
    int Fecha(int abre)
    {
        int fundo = 0;
        for (int k = abre; k < toks.Count; k++)
        {
            var t = toks[k];
            if (t.Tipo != T.Op) continue;
            if (t.Texto is "(" or "[" or "{") fundo++;
            else if (t.Texto is ")" or "]" or "}")
            {
                fundo--;
                if (fundo == 0) return k;
            }
        }
        throw new ErroDeSintaxe($"'{toks[abre].Texto}' sem o par que o fecha", toks[abre].Linha);
    }

    /// <summary>O "&gt;" que fecha o "&lt;" de um template. "&gt;&gt;" fecha dois.</summary>
    int FechaAngulo(int abre)
    {
        int fundo = 0;
        for (int k = abre; k < toks.Count; k++)
        {
            var t = toks[k];
            if (t.Tipo != T.Op) continue;
            switch (t.Texto)
            {
                case "<": fundo++; break;
                case ">": fundo--; break;
                case ">>": fundo -= 2; break;
                case "(": k = Fecha(k); break;
                case ";" or "{" or "}": return -1;
            }
            if (fundo <= 0) return k;
        }
        return -1;
    }

    void PularAte(string fim)
    {
        while (Atual.Tipo != T.Fim && !Checar(fim))
        {
            if (Checar("(") || Checar("{") || Checar("[")) { p = Fecha(p) + 1; continue; }
            p++;
        }
        Aceitar(fim);
    }

    // ---------------- template, namespace, using, typedef ----------------

    /// <summary>
    /// "template &lt;typename T&gt; class Fila { ... }".
    ///
    /// O interpretador nao tem tipo fixo: um T vale o que vier. Entao o
    /// template e lido como se o T fosse "auto" -- e "Fila&lt;int&gt;" e
    /// "Fila&lt;float&gt;" viram a mesma classe, que guarda o que receber.
    /// </summary>
    void AnalisarModelo(Action declaracao)
    {
        Exigir("template");
        Exigir("<");

        var parametros = new List<string>();
        while (!Checar(">") && Atual.Tipo != T.Fim)
        {
            if (Atual.Texto is "typename" or "class")
            {
                p++;
                Aceitar("...");
                if (Atual.Tipo == T.Id) parametros.Add(Avancar().Texto);
            }
            else
            {
                LerTipo();
                if (Atual.Tipo == T.Id) Avancar();
            }

            // valor padrao: "typename T = int", "size_t N = 8"
            if (Aceitar("="))
                while (!Checar(",") && !Checar(">") && Atual.Tipo != T.Fim)
                {
                    if (Checar("<")) { int f = FechaAngulo(p); p = f < 0 ? p + 1 : f + 1; continue; }
                    p++;
                }

            if (!Aceitar(",")) break;
        }
        Exigir(">");

        var ja = parametros.Where(tipos.Contains).ToHashSet();
        foreach (var n in parametros) { apelidos[n] = "auto"; tipos.Add(n); }

        if ((Checar("class") || Checar("struct")) && Espiar().Tipo == T.Id)
            modelos.Add(SemEspaco(Espiar().Texto));

        try { declaracao(); }
        finally
        {
            foreach (var n in parametros)
            {
                apelidos.Remove(n);
                if (!ja.Contains(n)) tipos.Remove(n);
            }
        }
    }

    void AnalisarEspaco(List<Cmd> globais, List<Funcao> funcoes)
    {
        Exigir("namespace");
        string nome = Atual.Tipo == T.Id ? Avancar().Texto : null;

        // "namespace curto = muito::comprido;"
        if (Aceitar("="))
        {
            string alvo = Avancar().Texto;
            if (nome is not null) espacos.Add(nome);
            _ = alvo;
            Exigir(";");
            return;
        }

        if (nome is not null)
            foreach (var parte in nome.Split("::", StringSplitOptions.RemoveEmptyEntries))
                espacos.Add(parte);

        Exigir("{");
        while (!Checar("}") && Atual.Tipo != T.Fim) AnalisarNoTopo(globais, funcoes);
        Exigir("}");
    }

    /// <summary>"using namespace x;", "using x::y;" e "using Nome = tipo;".</summary>
    void AnalisarUsing()
    {
        Exigir("using");
        if (Aceitar("namespace")) { PularAte(";"); return; }

        if (Atual.Tipo == T.Id && Eh(Espiar(), "="))
        {
            string nome = Avancar().Texto;
            p++;
            string alvo = LerTipoDeApelido();
            apelidos[nome] = alvo;
            tipos.Add(nome);
            CopiarCategoria(alvo, nome);
            Aceitar(";");
            return;
        }

        PularAte(";");
    }

    /// <summary>O lado direito de um using/typedef: um tipo ou um ponteiro de funcao.</summary>
    string LerTipoDeApelido()
    {
        string alvo = LerTipoFlexivel();

        // "void (*)(int)": ponteiro para funcao.
        if (Checar("("))
        {
            p = Fecha(p) + 1;
            if (Checar("(")) p = Fecha(p) + 1;
            return "funcao*";
        }
        return alvo;
    }

    void CopiarCategoria(string alvo, string nome)
    {
        string basico = SemPonteiro(alvo);
        if (classes.Contains(basico)) classes.Add(nome);
        if (estruturas.Contains(basico)) estruturas.Add(nome);
    }

    List<Cmd> AnalisarTypedef()
    {
        Exigir("typedef");

        if ((Checar("struct") || Checar("class") || Checar("union")) && !EhTypedefCurto())
            return AnalisarClasse(apelidada: true);

        if (Checar("enum"))
        {
            var membros = AnalisarEnum(apelidada: true);
            Aceitar(";");
            return [.. membros];
        }

        if (Checar("struct") || Checar("class") || Checar("union")) p++;

        string alvo = LerTipoFlexivel();

        // "typedef void (*Callback)(int);": o nome mora entre os parenteses.
        if (Checar("(") && (Eh(Espiar(), "*") || Eh(Espiar(), "&")))
        {
            p += 2;
            string nomeDaFuncao = Avancar().Texto;
            Exigir(")");
            if (Checar("(")) p = Fecha(p) + 1;
            Exigir(";");
            apelidos[nomeDaFuncao] = "funcao*";
            tipos.Add(nomeDaFuncao);
            return [];
        }

        // "typedef void Tratador(int);": o tipo de funcao sem o ponteiro.
        string nome = Avancar().Texto;
        if (Checar("(")) { p = Fecha(p) + 1; alvo = "funcao*"; }
        while (Aceitar("[")) { if (!Checar("]")) AnalisarExpr(); Exigir("]"); alvo = SemPonteiro(alvo) + "*"; }

        // "typedef int Par, *PonteiroPar;" -- mais de um nome.
        var nomes = new List<string> { nome };
        while (Aceitar(","))
        {
            while (Aceitar("*")) { }
            nomes.Add(Avancar().Texto);
        }
        Exigir(";");

        foreach (var n in nomes)
        {
            apelidos[n] = alvo;
            tipos.Add(n);
            CopiarCategoria(alvo, n);
        }
        return [];
    }

    /// <summary>"typedef struct Tag Nome;" -- sem corpo.</summary>
    bool EhTypedefCurto() => Espiar().Tipo == T.Id && Espiar(2).Tipo == T.Id && Eh(Espiar(3), ";");

    // ---------------- classes e structs ----------------

    /// <summary>
    /// "struct Ponto { int x, y; };", "class Botao : public Base { ... };".
    ///
    /// A struct so de campos continua virando <see cref="CmdEstrutura"/>,
    /// o caminho de sempre. O que tem metodo, construtor, base ou campo
    /// objeto vira <see cref="ClasseDecl"/>.
    /// </summary>
    List<Cmd> AnalisarClasse(bool apelidada)
    {
        int linha = Atual.Linha;
        string palavra = Avancar().Texto;
        PularAtributos();

        string nome = null;
        if (Atual.Tipo == T.Id && !Checar("final"))
        {
            nome = SemEspaco(Avancar().Texto);
            int ultimo = nome.LastIndexOf("::", StringComparison.Ordinal);
            if (ultimo >= 0) nome = nome[(ultimo + 2)..];
            if (Checar("<")) { int f = FechaAngulo(p); if (f > 0) p = f + 1; }
        }
        Aceitar("final");

        // "class Botao;": so avisa que o nome existe.
        if (nome is not null && Checar(";"))
        {
            p++;
            tipos.Add(nome);
            if (palavra == "class") classes.Add(nome);
            return [];
        }

        var bases = new List<string>();
        if (Aceitar(":"))
        {
            do
            {
                while (Atual.Texto is "public" or "private" or "protected" or "virtual") p++;
                string b = SemEspaco(Avancar().Texto);
                if (Checar("<")) { int f = FechaAngulo(p); if (f > 0) p = f + 1; }
                bases.Add(Resolver(b));
            } while (Aceitar(","));
        }

        // O nome entra na lista de tipos ANTES do corpo.
        //
        // Duas coisas dependem disso. Uma struct que guarda outra --
        // "struct GeoLine { GeoPoint a; }" -- so parseia se GeoPoint ja
        // for tipo conhecido; e uma que aponta para si mesma ("struct No
        // { No* proximo; }") so parseia se ela propria ja for. Antes,
        // campo com tipo de struct parava em "tipo esperado".
        if (nome is not null) tipos.Add(nome);
        bool jaEraClasse = nome is not null && classes.Contains(nome);
        if (nome is not null && palavra == "class") classes.Add(nome);

        Exigir("{");

        var campos = new List<Cmd>();
        var estaticos = new List<Cmd>();
        var metodos = new List<Funcao>();
        var depois = new List<Cmd>();

        while (!Checar("}") && Atual.Tipo != T.Fim)
            AnalisarMembro(nome ?? "", campos, estaticos, metodos, depois);

        Exigir("}");
        PularAtributos();

        // O que vem depois do corpo: "} GnssState;" (typedef) ou "} ponto;" (variavel).
        var aposCorpo = new List<(string Nome, bool Ponteiro)>();
        while (Atual.Tipo == T.Id || Checar("*"))
        {
            bool ponteiro = false;
            while (Aceitar("*")) ponteiro = true;
            if (Atual.Tipo != T.Id) break;
            aposCorpo.Add((Avancar().Texto, ponteiro));
            if (!Aceitar(",")) break;
        }

        var nomes = new List<string>();
        if (nome is not null) nomes.Add(nome);
        if (apelidada)
            foreach (var (n, _) in aposCorpo) nomes.Add(n);

        // "typedef struct { ... } GnssState;": o nome vem DEPOIS do
        // corpo, e tambem precisa virar tipo.
        foreach (string qual in nomes) tipos.Add(qual);

        if (nomes.Count == 0)
        {
            if (aposCorpo.Count == 0)
                throw new ErroDeSintaxe(
                    "esta struct nao tem nome, entao nao da para declarar uma variavel dela. "
                    + "Escreva \"struct Nome { ... };\" ou \"typedef struct { ... } Nome;\".", linha);

            // "struct { int x; } ponto;": a struct sem nome ganha um.
            nome = "__anonima" + Origens.Linha(linha) + "_" + Origens.Arquivo(linha);
            nomes.Add(nome);
            tipos.Add(nome);
        }

        string principal = nomes[0];
        bool temComportamento = palavra == "class" || jaEraClasse || metodos.Count > 0 || bases.Count > 0
                                || estaticos.Count > 0 || campos.Any(c => c is not CmdDecl);

        var saida = new List<Cmd>(depois);

        if (temComportamento)
        {
            foreach (var n in nomes) classes.Add(n);
            foreach (var n in nomes.Skip(1)) apelidos[n] = principal;

            var decl = new ClasseDecl(principal, bases, linha)
            {
                EhStruct = palavra != "class",
                Campos = campos,
                Estaticos = estaticos,
                Metodos = [.. metodos.Select(m => string.IsNullOrEmpty(m.Classe) ? m with { Classe = principal } : m)],
            };
            declaradas.Add(decl);
            saida.Add(decl);
        }
        else
        {
            foreach (var n in nomes) estruturas.Add(n);
            saida.Add(new CmdEstrutura(nomes, [.. campos.Cast<CmdDecl>()], linha));
        }

        if (!apelidada)
            foreach (var (n, ponteiro) in aposCorpo)
                saida.Add(temComportamento && !ponteiro
                    ? new CmdDeclObj(principal, n, [], linha)
                    : new CmdDecl(principal + (ponteiro ? "*" : ""), n, null, false, null, null, linha));

        Exigir(";");
        return saida;
    }

    /// <summary>Um membro de classe: campo, metodo, construtor, tipo de dentro.</summary>
    void AnalisarMembro(string classe, List<Cmd> campos, List<Cmd> estaticos, List<Funcao> metodos,
                        List<Cmd> depois)
    {
        if (Aceitar(";")) return;

        if (Atual.Texto is "public" or "private" or "protected" && Eh(Espiar(), ":"))
        {
            p += 2;
            return;
        }

        PularAtributos();

        if (Checar("friend") || Checar("static_assert")) { PularDeclaracao(); return; }
        if (Checar("using")) { AnalisarUsing(); return; }

        if (Checar("typedef"))
        {
            // "typedef enum { DRIVER = 1, ... } Tipo;" dentro da classe: as
            // constantes sao da CLASSE ("AccelStepper::DRIVER"), como no enum
            // sem typedef -- e nao campos de cada objeto.
            bool deEnum = Eh(Espiar(), "enum");
            foreach (var cmd in AnalisarTypedef())
                if (deEnum && cmd is CmdDecl) estaticos.Add(cmd);
                else RegistrarAninhado(classe, cmd, estaticos, campos);
            return;
        }

        if (Checar("template"))
        {
            AnalisarModelo(() => AnalisarMembro(classe, campos, estaticos, metodos, depois));
            return;
        }

        if (Checar("enum"))
        {
            string nomeDoEnum = Espiar().Tipo == T.Id && !Eh(Espiar(), "class") ? Espiar().Texto
                              : Eh(Espiar(), "class") || Eh(Espiar(), "struct") ? Espiar(2).Texto : null;
            var membros = AnalisarEnum();
            Aceitar(";");
            if (nomeDoEnum is not null && classe.Length > 0) tipos.Add(classe + "::" + nomeDoEnum);
            estaticos.AddRange(membros);
            return;
        }

        if ((Checar("struct") || Checar("class") || Checar("union")) && !EhDeclaracaoComPalavra())
        {
            string aninhado = Espiar().Tipo == T.Id ? Espiar().Texto : null;
            foreach (var cmd in AnalisarClasse(apelidada: false))
                RegistrarAninhado(classe, cmd, estaticos, campos);
            if (aninhado is not null && classe.Length > 0)
            {
                tipos.Add(classe + "::" + aninhado);
                apelidos[classe + "::" + aninhado] = aninhado;
            }
            return;
        }
        if (Checar("struct") || Checar("class") || Checar("union")) p++;

        int linha = Atual.Linha;

        // especificadores
        bool estatico = false, virtual_ = false;
        while (Atual.Tipo == T.Id && Atual.Texto is "static" or "virtual" or "inline" or "explicit"
                   or "constexpr" or "mutable" or "friend")
        {
            if (Atual.Texto == "static") estatico = true;
            if (Atual.Texto == "virtual") virtual_ = true;
            p++;
        }

        // destrutor: "~Botao()"
        if (Checar("~") && Espiar().Tipo == T.Id)
        {
            p++;
            string n = Avancar().Texto;
            metodos.Add(AnalisarFuncao("void", "~" + n, linha) with { Classe = classe, Virtual = virtual_ });
            return;
        }

        // construtor: "Botao(uint8_t pino) : _pino(pino) { }"
        if (Atual.Tipo == T.Id && Atual.Texto == classe && Eh(Espiar(), "("))
        {
            p++;
            metodos.Add(AnalisarFuncao("void", classe, linha, construtor: true) with { Classe = classe });
            return;
        }

        // conversao: "operator bool() const"
        if (Checar("operator"))
        {
            p++;
            string op = LerOperador();
            metodos.Add(AnalisarFuncao(op.Trim(), "operator" + op, linha)
                        with { Classe = classe, Estatico = estatico, Virtual = virtual_ });
            return;
        }

        string tipo = TentarTipo(local: false, membro: true)
            ?? throw new ErroDeSintaxe($"nao entendi '{Atual.Texto}' dentro da classe {classe}", Atual.Linha);
        bool referencia = ultimoReferencia;

        string nome = LerPonteiroDeFuncao();
        if (nome is not null) tipo = "funcao*";
        else if (Checar("operator")) { p++; nome = "operator" + LerOperador(); }
        else if (Atual.Tipo == T.Id) nome = SemEspaco(Avancar().Texto);
        else throw new ErroDeSintaxe($"esperava o nome do membro, encontrei '{Atual.Texto}'", Atual.Linha);

        if (Checar("("))
        {
            // Metodo. "Stream* x(...)" de um campo iniciado com parenteses
            // nao existe dentro de classe: la, parentese e sempre metodo.
            metodos.Add(AnalisarFuncao(tipo, nome, linha)
                        with { Classe = classe, Estatico = estatico, Virtual = virtual_ });
            return;
        }

        foreach (var cmd in AnalisarVariaveis(tipo, nome, linha, externo: false, referencia, dentroDeClasse: true))
            (estatico ? estaticos : campos).Add(cmd);
        Exigir(";");
    }

    /// <summary>Uma struct, classe ou enum declarada dentro de outra classe.</summary>
    static void RegistrarAninhado(string classe, Cmd cmd, List<Cmd> estaticos, List<Cmd> campos)
    {
        _ = classe;
        switch (cmd)
        {
            case ClasseDecl: break;                          // ja esta em "declaradas"
            case CmdEstrutura: estaticos.Add(cmd); break;    // registra a struct
            default: campos.Add(cmd); break;                 // "struct { ... } interno;"
        }
    }

    void PularDeclaracao()
    {
        while (Atual.Tipo != T.Fim && !Checar(";"))
        {
            if (Checar("{")) { p = Fecha(p) + 1; return; }
            if (Checar("(") || Checar("[")) { p = Fecha(p) + 1; continue; }
            p++;
        }
        Aceitar(";");
    }

    /// <summary>O que vem depois de "operator": "==", "()", "[]", "bool".</summary>
    string LerOperador()
    {
        if (Aceitar("(")) { Exigir(")"); return "()"; }
        if (Aceitar("[")) { Exigir("]"); return "[]"; }
        if (Atual.Tipo == T.Op) return Avancar().Texto;
        if (Checar("new") || Checar("delete"))
        {
            string n = " " + Avancar().Texto;
            if (Aceitar("[")) { Exigir("]"); n += "[]"; }
            return n;
        }
        return " " + LerTipo();
    }

    /// <summary>"Botao::Botao(...)" ou "Botao::~Botao()" fora da classe.</summary>
    bool EhConstrutorDeFora()
    {
        int k = p;
        while (Olhar(k).Tipo == T.Id && Olhar(k).Texto is "inline" or "constexpr" or "explicit") k++;
        var t = Olhar(k);
        if (t.Tipo != T.Id || !Eh(Olhar(k + 1), "(")) return false;

        string nome = SemEspaco(t.Texto);
        int corte = nome.LastIndexOf("::", StringComparison.Ordinal);
        if (corte <= 0) return false;

        string dono = nome[..corte];
        int corteDono = dono.LastIndexOf("::", StringComparison.Ordinal);
        if (corteDono >= 0) dono = dono[(corteDono + 2)..];
        string ultimo = nome[(corte + 2)..];
        return ultimo == dono || ultimo == "~" + dono;
    }

    Funcao AnalisarConstrutorDeFora()
    {
        while (Atual.Tipo == T.Id && Atual.Texto is "inline" or "constexpr" or "explicit") p++;
        int linha = Atual.Linha;
        string nome = SemEspaco(Avancar().Texto);
        int corte = nome.LastIndexOf("::", StringComparison.Ordinal);
        string ultimo = nome[(corte + 2)..];
        string dono = nome[..corte];
        int corteDono = dono.LastIndexOf("::", StringComparison.Ordinal);
        if (corteDono >= 0) dono = dono[(corteDono + 2)..];

        bool destrutor = ultimo.StartsWith('~');
        return AnalisarFuncao("void", dono + "::" + ultimo, linha, construtor: !destrutor);
    }

    // ---------------- enum ----------------

    /// <summary>
    /// "enum Modo { ALTERNADO, WIGWAG = 5, CORRIDA };"
    ///
    /// Vira uma constante por membro, contando de 0 em diante -- que e o
    /// que o C++ faz. O nome do enum entra na lista de tipos do sketch,
    /// entao "Modo m" passa a ser declaracao e "(Modo)x" passa a ser
    /// conversao.
    ///
    /// Antes o sketch parava na chave do enum com "esperava ';' mas
    /// encontrei '{'", e enum e a primeira coisa que aparece quando o
    /// exercicio tem mais de um modo de funcionamento.
    /// </summary>
    List<CmdDecl> AnalisarEnum(bool apelidada = false)
    {
        int linha = Atual.Linha;
        Exigir("enum");

        // "enum class Cor : uint8_t {" -- so o nome interessa aqui.
        if (Checar("class") || Checar("struct")) Avancar();

        string nome = !Checar("{") && Atual.Tipo == T.Id ? SemEspaco(Avancar().Texto) : null;
        if (Aceitar(":")) LerTipo();

        // "enum Cor;" -- so o aviso de que existe.
        if (nome is not null && Checar(";"))
        {
            tipos.Add(nome);
            apelidos[nome] = "int";
            return [];
        }

        // "enum Modo modo;" -- o tipo com a palavra na frente.
        if (nome is not null && !Checar("{"))
        {
            tipos.Add(nome);
            string variavel = Avancar().Texto;
            return AnalisarResto(nome, variavel, linha);
        }

        Exigir("{");

        var membros = new List<CmdDecl>();
        double proximo = 0;
        bool sabeOProximo = true;
        Expr anterior = null;

        while (!Checar("}") && Atual.Tipo != T.Fim)
        {
            int linhaMembro = Atual.Linha;
            string membro = Avancar().Texto;
            PularAtributos();

            if (Aceitar("="))
            {
                var valor = AnalisarExpr();
                membros.Add(new CmdDecl("int", membro, valor, false, null, null, linhaMembro));

                // So da para continuar a contagem se o valor for um numero
                // escrito ali. "A = f()" existe em C++, mas quem vem
                // depois dele nao tem como saber em que numero continuar.
                if (valor is Literal { EhTexto: false } lit)
                {
                    proximo = lit.Valor + 1;
                    sabeOProximo = true;
                }
                else
                {
                    sabeOProximo = false;

                    // "A = OUTRO_ENUM, B": o B e o A mais um, e o valor do A
                    // e constante -- o compilador aceita. "A = f()" nao e:
                    // funcao comum so da valor rodando, e o compilador recusa.
                    anterior = EhConstante(valor) ? new Variavel(membro, linhaMembro) : null;
                }
            }
            else
            {
                Expr valor = sabeOProximo
                    ? new Literal(proximo, null, true, false, linhaMembro)
                    : anterior is not null
                        ? new Binaria("+", anterior, new Literal(1, "1", true, false, linhaMembro), linhaMembro)
                        : throw new ErroDeSintaxe(
                            $"'{membro}' vem depois de um valor que so da para saber rodando, "
                            + "entao o simulador nao sabe em que numero continuar. "
                            + "Escreva o valor dele.", linhaMembro);

                membros.Add(new CmdDecl("int", membro, valor, false, null, null, linhaMembro));
                if (sabeOProximo) proximo++;
                else anterior = new Variavel(membro, linhaMembro);
            }

            if (!Aceitar(",")) break;
        }

        Exigir("}");

        if (nome is not null)
        {
            tipos.Add(nome);
            apelidos[nome] = "int";
        }

        // "typedef enum { ... } Estado;" -- o nome vem depois, e e tipo.
        if (apelidada)
        {
            while (Atual.Tipo == T.Id)
            {
                string apelido = Avancar().Texto;
                tipos.Add(apelido);
                apelidos[apelido] = "int";
                if (!Aceitar(",")) break;
            }
            return membros;
        }

        // "enum Modo { ... } modoAtual;" -- declara o tipo e uma variavel
        // na mesma linha.
        if (Atual.Tipo == T.Id && !Checar(";"))
        {
            string variavel = Avancar().Texto;
            membros.AddRange(AnalisarResto(nome ?? "int", variavel, linha));
        }

        return membros;
    }

    /// <summary>
    /// Uma conta que o compilador faz sozinho: numeros, nomes (de outro
    /// enum, de uma constante) e operadores. Chamada de funcao nao entra.
    /// </summary>
    static bool EhConstante(Expr e) => e switch
    {
        Literal { EhTexto: false } => true,
        Variavel => true,
        Unaria u => EhConstante(u.Alvo),
        Binaria b => EhConstante(b.Esq) && EhConstante(b.Dir),
        Ternaria t => EhConstante(t.Cond) && EhConstante(t.Sim) && EhConstante(t.Nao),
        Conversao c => EhConstante(c.Alvo),
        _ => false,
    };

    // ---------------- tipos ----------------

    string LerTipo()
    {
        ultimoExterno = false;
        ultimoReferencia = false;
        ultimoEstatico = false;

        var partes = new List<string>();
        while (Atual.Tipo == T.Id && (tipos.Contains(Atual.Texto) || apelidos.ContainsKey(Atual.Texto)))
        {
            // Nao dizem NADA sobre o tipo em si -- so como o compilador
            // pode tratar a variavel. "volatile" e obrigatorio em todo
            // exemplo de interrupcao ("volatile int cliques = 0;"), e
            // sem isto o sketch parava na primeira linha.
            if (Qualificadores.Contains(Atual.Texto))
            {
                if (Atual.Texto == "extern") ultimoExterno = true;
                if (Atual.Texto == "static") ultimoEstatico = true;
                p++;
                continue;
            }

            // Um nome de tipo depois de outro que ja fecha o tipo e o nome
            // da variavel: "Modo Modo;" nao, mas "Ponto origem" sim.
            if (partes.Count > 0 && !Combina(partes[^1], Atual.Texto)) break;

            string parte = Avancar().Texto;
            if (modelos.Contains(parte) && Checar("<"))
            {
                int f = FechaAngulo(p);
                if (f > 0) p = f + 1;
            }
            partes.Add(parte);
        }

        // O ponteiro fica no NOME do tipo ("char*"): e o que separa um
        // "char c" (um caractere) de um "char* texto" (um texto inteiro).
        bool ponteiro = false;
        while (Checar("*") || Checar("&") || Checar("&&") || (Checar("const") && partes.Count > 0))
        {
            if (Atual.Texto == "*") ponteiro = true;
            if (Atual.Texto is "&" or "&&") ultimoReferencia = true;
            p++;
        }
        if (partes.Count == 0)
            throw new ErroDeSintaxe("tipo esperado", Atual.Linha);

        string tipo = string.Join(" ", partes);
        if (partes.Count == 1)
        {
            string resolvido = Resolver(partes[0]);
            if (resolvido != partes[0])
            {
                if (resolvido.EndsWith('*')) { ponteiro = true; resolvido = SemPonteiro(resolvido); }
                tipo = resolvido;
            }
        }
        return tipo + (ponteiro ? "*" : "");
    }

    /// <summary>
    /// Dois nomes de tipo seguidos que formam UM tipo: "unsigned long",
    /// "long long", "signed char". Qualquer outra dupla e tipo e nome.
    /// </summary>
    static bool Combina(string anterior, string proximo)
        => anterior is "unsigned" or "signed" or "long" or "short"
           && proximo is "int" or "long" or "char" or "short" or "double" or "unsigned" or "signed";

    /// <summary>
    /// Um tipo que pode nao ser conhecido: "WiFiClient", "Fila&lt;int&gt;",
    /// "sensors_event_t". Usado onde nao ha duvida de que vem um tipo.
    /// </summary>
    string LerTipoFlexivel()
    {
        while (Atual.Tipo == T.Id && Qualificadores.Contains(Atual.Texto)) p++;

        if (EhInicioDeDeclaracao()) return LerTipo();

        if (Atual.Tipo != T.Id) throw new ErroDeSintaxe("tipo esperado", Atual.Linha);

        string nome = Resolver(SemEspaco(Avancar().Texto));
        if (Checar("<"))
        {
            int f = FechaAngulo(p);
            if (f > 0) p = f + 1;
            if (nome is "function") nome = "funcao*";
        }

        bool ponteiro = nome.EndsWith('*');
        ultimoReferencia = false;
        while (Checar("*") || Checar("&") || Checar("&&") || Checar("const"))
        {
            if (Atual.Texto == "*") ponteiro = true;
            if (Atual.Texto is "&" or "&&") ultimoReferencia = true;
            p++;
        }
        return SemPonteiro(nome) + (ponteiro ? "*" : "");
    }

    /// <summary>
    /// O tipo no comeco de uma declaracao, ou null se aqui nao comeca uma
    /// (e o parser volta para onde estava).
    ///
    /// Tipo conhecido e o caminho de sempre. O desconhecido -- a classe de
    /// uma biblioteca -- so conta quando o formato nao deixa duvida: dois
    /// nomes seguidos ("Servo motor"), ou nome, asteriscos e nome
    /// ("WiFiClient* cliente").
    /// </summary>
    string TentarTipo(bool local, bool membro = false)
    {
        int inicio = p;
        ultimoExterno = false;
        ultimoReferencia = false;

        if (Atual.Tipo != T.Id) return null;

        // Os qualificadores primeiro: "static WiFiClient c;" tem um tipo de
        // biblioteca depois do static, e "static int x;" um conhecido.
        bool externo = false, estatico = false;
        while (Atual.Tipo == T.Id && Qualificadores.Contains(Atual.Texto))
        {
            if (Atual.Texto == "extern") externo = true;
            if (Atual.Texto == "static") estatico = true;
            p++;
        }

        if (Atual.Tipo != T.Id) { p = inicio; return null; }

        if (tipos.Contains(Atual.Texto) || apelidos.ContainsKey(Atual.Texto))
        {
            // "Botao(3).ligar();" -- o nome de uma classe no comeco de uma
            // expressao nao e declaracao.
            if (local && classes.Contains(Atual.Texto) && Eh(Espiar(), "(")) { p = inicio; return null; }

            string conhecido = LerTipo();
            ultimoExterno |= externo;
            ultimoEstatico |= estatico;

            // Depois do tipo vem o nome -- ou o "(*" de um ponteiro de funcao.
            if (Atual.Tipo == T.Id || (membro && Checar("operator"))
                || (Checar("(") && Eh(Espiar(), "*")))
                return conhecido;

            p = inicio;
            return null;
        }

        if (Palavras.Contains(Atual.Texto)) { p = inicio; return null; }

        int q = p + 1;
        if (Eh(Olhar(q), "<"))
        {
            int f = FechaAngulo(q);
            if (f < 0) { p = inicio; return null; }
            q = f + 1;
        }

        int k = q;
        bool ponteiro = false, referencia = false;
        while (Eh(Olhar(k), "*") || Eh(Olhar(k), "&") || Eh(Olhar(k), "&&") || Eh(Olhar(k), "const"))
        {
            if (Olhar(k).Texto == "*") ponteiro = true;
            if (Olhar(k).Texto is "&" or "&&") referencia = true;
            k++;
        }

        var depois = Olhar(k);
        bool declara = depois.Tipo == T.Id && !Palavras.Contains(depois.Texto)
                       || (membro && Eh(depois, "operator"));

        // "a * b;" e "x & y;" como expressao nao fazem sentido, mas "a * b
        // = c" tambem nao: nome, ponteiro e nome seguido de ;, =, (, [ ou ,
        // so pode ser declaracao.
        if (declara && local && (ponteiro || referencia))
        {
            var fim = Olhar(k + 1);
            declara = Eh(fim, ";") || Eh(fim, "=") || Eh(fim, "(") || Eh(fim, "[") || Eh(fim, ",") || Eh(fim, "{");
        }

        if (!declara) { p = inicio; return null; }
        ultimoEstatico = estatico;

        string nome = Resolver(SemEspaco(Atual.Texto));
        if (Eh(Olhar(p + 1), "<") && nome == "function") nome = "funcao*";
        p = k;
        ultimoExterno = externo;
        ultimoReferencia = referencia;
        if (nome.EndsWith('*')) { ponteiro = true; nome = SemPonteiro(nome); }
        return nome + (ponteiro ? "*" : "");
    }

    /// <summary>
    /// "void (*aoApertar)(int)": o nome mora entre os parenteses, e a
    /// variavel guarda uma funcao. Devolve null se aqui nao tem isso.
    /// </summary>
    string LerPonteiroDeFuncao()
    {
        if (!Checar("(") || !(Eh(Espiar(), "*") || Eh(Espiar(), "&"))) return null;
        p += 2;
        string nome = Avancar().Texto;
        Exigir(")");
        if (Checar("(")) p = Fecha(p) + 1;
        return nome;
    }

    /// <summary>O nome de quem esta sendo declarado: "x", "Botao::ler", "operator==".</summary>
    string LerNomeDeclarado()
    {
        if (Checar("operator")) { p++; return "operator" + LerOperador(); }

        var t = Avancar();
        if (t.Tipo != T.Id)
            throw new ErroDeSintaxe($"esperava um nome, encontrei '{t.Texto}'", t.Linha);

        string nome = SemEspaco(t.Texto);

        // "bool Botao::operator==(...)"
        if (nome.EndsWith("::operator", StringComparison.Ordinal))
            return nome + LerOperador();
        return nome;
    }

    /// <summary>
    /// O tipo de um parâmetro de função.
    ///
    /// Além dos tipos que o parser conhece, aceita o de qualquer
    /// biblioteca: "WiFiClient &amp;cliente", "BLEServer *servidor",
    /// "esp_now_send_status_t estado". Na lista de parâmetros não há
    /// ambiguidade -- um nome seguido de outro nome, de * ou de &amp; só pode
    /// ser tipo e variável. Sem isto, o callback de quase toda biblioteca
    /// parava em "tipo esperado", e era justamente o código copiado do
    /// tutorial que não passava.
    ///
    /// O nome NÃO entra na lista de tipos do sketch: "esp_now_peer_info_t
    /// par;" continua sendo a declaração de um objeto, com campos.
    /// </summary>
    string LerTipoDoParametro()
    {
        ultimoReferencia = false;

        // "struct Leitura *l": em C a palavra vem junto do nome do tipo.
        while (Atual.Tipo == T.Id && Atual.Texto is "struct" or "class" or "enum" or "typename") p++;

        int inicio = p;
        while (Atual.Tipo == T.Id && Atual.Texto is "const" or "volatile") p++;

        if (Atual.Tipo == T.Id && !tipos.Contains(Atual.Texto) && !apelidos.ContainsKey(Atual.Texto)
            && !Palavras.Contains(Atual.Texto))
        {
            int q = p + 1;
            if (Eh(Olhar(q), "<")) { int f = FechaAngulo(q); if (f > 0) q = f + 1; }
            var depois = Olhar(q);
            if (depois.Tipo == T.Id || depois.Texto is "*" or "&" or "&&" || Eh(depois, ",") || Eh(depois, ")"))
            {
                string nome = Resolver(SemEspaco(Avancar().Texto));
                if (Checar("<"))
                {
                    int f = FechaAngulo(p);
                    if (f > 0) p = f + 1;
                    if (nome == "function") nome = "funcao*";
                }
                bool ponteiro = nome.EndsWith('*');
                while (Checar("*") || Checar("&") || Checar("&&") || Checar("const"))
                {
                    if (Atual.Texto == "*") ponteiro = true;
                    if (Atual.Texto is "&" or "&&") ultimoReferencia = true;
                    p++;
                }
                return SemPonteiro(nome) + (ponteiro ? "*" : "");
            }
        }

        p = inicio;
        return LerTipo();
    }

    // ---------------- funcoes ----------------

    /// <summary>
    /// No "(" depois do nome: e funcao (definicao ou prototipo) ou um
    /// objeto com os argumentos do construtor?
    ///
    /// "void piscar(int vezes);" e prototipo; "LiquidCrystal lcd(12, 11);"
    /// e objeto. O que decide e o que vem dentro e depois dos parenteses.
    /// </summary>
    bool EhFuncao(string tipo)
    {
        int fim = Fecha(p);
        int q = PularQualificadoresDeFuncao(fim + 1);
        var t = Olhar(q);

        if (Eh(t, "{") || Eh(t, "->") || Eh(t, ":")) return true;
        if (Eh(t, "=") && Olhar(q + 1).Texto is "0" or "default" or "delete") return true;
        if (!Eh(t, ";") && !Eh(t, ",")) return false;

        if (tipo == "void" || fim == p + 1) return true;
        return PareceListaDeParametros(p + 1);
    }

    int PularQualificadoresDeFuncao(int q)
    {
        while (true)
        {
            var t = Olhar(q);
            if (t.Tipo == T.Id && t.Texto is "const" or "volatile" or "override" or "final" or "noexcept"
                    or "throw" or "__attribute__")
            {
                q++;
                if (Eh(Olhar(q), "(")) q = Fecha(q) + 1;
                continue;
            }
            if (Eh(t, "&") || Eh(t, "&&")) { q++; continue; }
            return q;
        }
    }

    bool PareceListaDeParametros(int k)
    {
        var t = Olhar(k);
        if (t.Tipo != T.Id) return false;
        if (t.Texto is "void" or "const" or "struct" or "class" or "enum" or "unsigned" or "signed"
            or "volatile" or "typename")
            return true;
        if (tipos.Contains(t.Texto) || apelidos.ContainsKey(t.Texto))
            return !Eh(Olhar(k + 1), "(") && !Eh(Olhar(k + 1), ".");
        if (Palavras.Contains(t.Texto)) return false;

        var d = Olhar(k + 1);
        if (d.Tipo == T.Id) return true;
        if (Eh(d, "*") || Eh(d, "&"))
            return Olhar(k + 2).Tipo == T.Id && Olhar(k + 3).Texto is "," or ")" or "=" or "[";
        return false;
    }

    Funcao AnalisarFuncao(string tipo, string nome, int linha, bool construtor = false)
    {
        Exigir("(");
        var ps = LerParametros();
        Exigir(")");

        p = PularQualificadoresDeFuncao(p);

        // "auto f() -> int": o tipo de retorno no fim.
        if (Aceitar("->")) tipo = LerTipoFlexivel();

        var inicializadores = new List<Inicializador>();
        if (construtor && Aceitar(":")) inicializadores = LerInicializadores();

        PularAtributos();

        // "= 0" (virtual puro), "= default", "= delete".
        if (Aceitar("="))
        {
            string qual = Avancar().Texto;
            Exigir(";");
            var vazio = qual == "default" ? new CmdBloco([], linha) : null;
            return new Funcao(tipo, nome, ps, vazio, linha)
            {
                EhConstrutor = construtor,
                Inicializadores = inicializadores,
            };
        }

        if (Aceitar(";"))
            return new Funcao(tipo, nome, ps, null, linha) { EhConstrutor = construtor };

        var corpo = AnalisarBloco();
        return new Funcao(tipo, nome, ps, corpo, linha)
        {
            EhConstrutor = construtor,
            Inicializadores = inicializadores,
        };
    }

    List<Param> LerParametros()
    {
        var ps = new List<Param>();
        if (Checar(")")) return ps;
        if (Checar("void") && Eh(Espiar(), ")")) { p++; return ps; }

        do
        {
            if (Aceitar("...")) { ps.Add(new Param("...", "__va_args")); continue; }
            PularAtributos();

            string pt = LerTipoDoParametro();
            bool porReferencia = ultimoReferencia;
            string pn;

            // "void (*quandoApertar)(int)": ponteiro de funcao.
            if (Checar("(") && (Eh(Espiar(), "*") || Eh(Espiar(), "&")))
            {
                p += 2;
                pn = Atual.Tipo == T.Id ? Avancar().Texto : "_";
                Exigir(")");
                if (Checar("(")) p = Fecha(p) + 1;
                pt = "funcao*";
            }
            else pn = Atual.Tipo == T.Id ? Avancar().Texto : "_";

            bool ehVetor = false;
            List<Expr> dimsDoParam = null;
            while (Aceitar("["))
            {
                // A primeira dimensao do parametro nao importa (vira ponteiro);
                // as outras dizem onde cada linha comeca: "int m[][4]".
                Expr dim = Checar("]") ? null : AnalisarExpr();
                if (ehVetor && dim is not null) (dimsDoParam ??= []).Add(dim);
                ehVetor = true;
                Exigir("]");
            }

            Expr padrao = Aceitar("=") ? AnalisarExpr() : null;
            ps.Add(new Param(pt, pn) { Padrao = padrao, PorReferencia = porReferencia, EhVetor = ehVetor, Dimensoes = dimsDoParam });
        } while (Aceitar(","));

        return ps;
    }

    List<Inicializador> LerInicializadores()
    {
        var lista = new List<Inicializador>();
        do
        {
            string nome = SemEspaco(Avancar().Texto);
            if (Checar("<")) { int f = FechaAngulo(p); if (f > 0) p = f + 1; }
            nome = Resolver(nome);

            var args = new List<Expr>();
            string fecha = Aceitar("(") ? ")" : Aceitar("{") ? "}" : null;
            if (fecha is null)
                throw new ErroDeSintaxe($"esperava '(' depois de '{nome}' na lista de inicializacao", Atual.Linha);
            if (!Checar(fecha))
                do { args.Add(AnalisarExpr()); } while (Aceitar(","));
            Exigir(fecha);

            lista.Add(new Inicializador(nome, args));
        } while (Aceitar(","));
        return lista;
    }

    // ---------------- variaveis ----------------

    List<CmdDecl> AnalisarResto(string tipo, string nome, int linha)
    {
        var saida = new List<CmdDecl>();
        foreach (var cmd in AnalisarVariaveis(tipo, nome, linha, externo: false, referencia: false))
            if (cmd is CmdDecl d) saida.Add(d);
        Exigir(";");
        return saida;
    }

    /// <summary>
    /// Os nomes de uma declaracao: "int a = 1, b[4], *c;". Cada um vira um
    /// comando -- variavel, vetor ou objeto, conforme o tipo.
    /// </summary>
    List<Cmd> AnalisarVariaveis(string tipo, string nome, int linha, bool externo, bool referencia,
                                bool dentroDeClasse = false)
    {
        var saida = new List<Cmd>();
        string tipoDoNome = tipo;

        while (true)
        {
            var (arr, tam, itens, ini, colunas, args, dims) = AnalisarSufixoDecl(tipoDoNome);

            // "uint8_t ligado : 1;" -- campo de bits.
            if (dentroDeClasse && Aceitar(":")) AnalisarExpr();
            if (dentroDeClasse && ini is null && itens is null && Aceitar("=")) ini = AnalisarExpr();

            saida.Add(Declarador(tipoDoNome, nome, linha, externo, referencia, arr, tam, itens, ini, colunas, args, dims));

            if (!Aceitar(",")) return saida;

            // "int *a, *b": o asterisco e de cada nome, nao do tipo.
            tipoDoNome = SemPonteiro(tipo);
            bool ponteiro = false;
            while (Checar("*") || Checar("&"))
            {
                if (Atual.Texto == "*") ponteiro = true;
                p++;
            }
            if (ponteiro) tipoDoNome += "*";
            nome = SemEspaco(Avancar().Texto);
        }
    }

    Cmd Declarador(string tipo, string nome, int linha, bool externo, bool referencia, bool arr, Expr tam,
                   List<Expr> itens, Expr ini, Expr colunas, List<Expr> args, List<Expr> dims = null)
    {
        string basico = SemPonteiro(tipo);
        bool ponteiro = tipo.EndsWith('*') || referencia;

        // Objeto: "Servo s;", "Botao b(4);", "Botao b = Botao(4);",
        // "Botao b{4};", "File f = SD.open(...)". O "extern" so promete.
        if (!ponteiro && !arr && EhObjeto(basico))
            return externo && ini is null && args is null && itens is null
                ? new CmdVazio(linha)
                : new CmdDeclObj(basico, nome, args ?? itens ?? [], linha) { Inicial = ini };

        // "int x(5);" e "int x{5};": o valor inicial, escrito do outro jeito.
        if (!arr && ini is null && !estruturas.Contains(basico))
        {
            if (args is { Count: > 0 }) ini = args[0];
            else if (itens is not null)
            {
                ini = itens.Count > 0 ? itens[0] : null;
                itens = null;
            }
        }

        // Referencia e ponteiro de objeto guardam o objeto: sao o mesmo
        // no interpretador, que nao tem endereco.
        string tipoFinal = referencia && !tipo.EndsWith('*') ? tipo + "*" : tipo;
        if (referencia && !EhObjeto(basico)) tipoFinal = tipo;

        return new CmdDecl(tipoFinal, nome, ini, arr, tam, itens, linha)
        {
            Colunas = colunas,
            Dimensoes = dims,
            Externo = externo && ini is null && itens is null,
            PorReferencia = referencia && !arr && ini is not null,
        };
    }

    (bool Arr, Expr Tam, List<Expr> Itens, Expr Ini, Expr Colunas, List<Expr> Args, List<Expr> Dims) AnalisarSufixoDecl(string tipo)
    {
        bool arr = false;
        Expr tam = null;
        List<Expr> itens = null;
        List<Expr> args = null;

        Expr colunas = null;
        List<Expr> dims = null;

        if (Aceitar("["))
        {
            arr = true;
            if (!Checar("]")) tam = AnalisarExpr();
            Exigir("]");

            // As outras dimensoes: "char teclas[LINHAS][COLUNAS]" (todo
            // sketch de teclado matricial comeca assim), "byte quadros[N][8][8]"
            // (as animacoes de matriz de LED e de cubo). O vetor continua um
            // so, esticado; o Colunas e o passo do primeiro indice.
            while (Aceitar("["))
            {
                (dims ??= []).Add(Checar("]") ? new Literal(1, null, true, false, Atual.Linha) : AnalisarExpr());
                Exigir("]");
            }
            if (dims is not null)
                colunas = dims.Skip(1).Aggregate(dims[0], (a, b) => new Binaria("*", a, b, a.Linha));
        }

        PularAtributos();

        // Os itens de vetor de struct ficam agrupados: "{ {262, 4}, {294, 4} }"
        // sao duas notas, e nao quatro numeros. No vetor de duas dimensoes
        // tambem: cada chave e uma LINHA, e a linha curta ("{1}" numa de
        // tres) completa com zeros -- esticar tudo junto poria o 2 da
        // segunda linha no lugar do zero da primeira.
        bool agrupar = estruturas.Contains(SemPonteiro(tipo)) || classes.Contains(SemPonteiro(tipo))
                       || colunas is not null;

        Expr ini = null;
        if (!arr && Checar("(") && !Eh(Espiar(), "*"))
        {
            p++;
            args = [];
            if (!Checar(")"))
                do { args.Add(AnalisarExpr()); } while (Aceitar(","));
            Exigir(")");
        }
        else if (Checar("{"))
        {
            itens = LerItens(agrupar);
        }
        else if (Aceitar("="))
        {
            if (Checar("{")) itens = LerItens(agrupar);
            else ini = AnalisarExpr();
        }
        return (arr, tam, itens, ini, colunas, args, dims);
    }

    List<Expr> LerItens(bool agrupar)
    {
        Exigir("{");
        var itens = new List<Expr>();
        while (!Checar("}") && Atual.Tipo != T.Fim)
        {
            // Linha de um vetor 2D: "{'1','2','3','A'}". Os itens entram
            // ESTICADOS, um atras do outro -- quem cuida de voltar a ler
            // por linha e coluna e o interpretador, que guarda a largura.
            // Num vetor de struct, cada chave e UM item.
            if (Checar("{"))
            {
                int linhaDoItem = Atual.Linha;
                var dentro = LerItens(agrupar);
                if (agrupar) itens.Add(new ListaLiteral(dentro, false, linhaDoItem));
                else itens.AddRange(dentro);
            }
            // ".campo = valor": o nome do campo na frente, do C99.
            else if (Checar(".") && Espiar().Tipo == T.Id && Eh(Espiar(2), "="))
            {
                p++;
                int linhaDoItem = Atual.Linha;
                string campo = Avancar().Texto;
                p++;
                itens.Add(new ArgNomeado(campo, Checar("{")
                    ? new ListaLiteral(LerItens(true), false, linhaDoItem)
                    : AnalisarExpr(), linhaDoItem));
            }
            else itens.Add(AnalisarExpr());

            if (!Aceitar(",")) break;
        }
        Exigir("}");
        return itens;
    }

    // ================= comandos =================
    CmdBloco AnalisarBloco()
    {
        int linha = Atual.Linha;
        Exigir("{");
        var corpo = new List<Cmd>();
        while (!Checar("}") && Atual.Tipo != T.Fim) corpo.Add(AnalisarCmd());
        Exigir("}");
        return new CmdBloco(corpo, linha);
    }

    Cmd AnalisarCmd()
    {
        int linha = Atual.Linha;

        if (Checar("{")) return AnalisarBloco();
        if (Aceitar(";")) return new CmdVazio(linha);

        if (Checar("if"))
        {
            p++;
            Aceitar("constexpr");
            Exigir("("); var c = AnalisarExpr(); Exigir(")");
            var ent = AnalisarCmd();
            Cmd sen = null;
            if (Aceitar("else")) sen = AnalisarCmd();
            return new CmdSe(c, ent, sen, linha);
        }

        if (Checar("while"))
        {
            p++; Exigir("("); var c = AnalisarExpr(); Exigir(")");
            return new CmdEnquanto(c, AnalisarCmd(), linha);
        }

        if (Checar("for"))
        {
            p++; Exigir("(");
            if (EhForDeIntervalo()) return AnalisarForDeIntervalo(linha);

            Cmd ini = null;
            if (Checar(";")) Exigir(";");
            else { ini = AnalisarCmdSimples(); Exigir(";"); }
            Expr cond = Checar(";") ? null : AnalisarExpr();
            Exigir(";");
            Expr passo = Checar(")") ? null : AnalisarExprComVirgula();
            Exigir(")");
            return new CmdPara(ini, cond, passo, AnalisarCmd(), linha);
        }

        if (Checar("do"))
        {
            p++;
            var corpo = AnalisarCmd();
            if (!Aceitar("while"))
                throw new ErroDeSintaxe("esperava 'while' depois do 'do'", Atual.Linha);
            Exigir("("); var cond = AnalisarExpr(); Exigir(")"); Exigir(";");
            return new CmdFacaEnquanto(corpo, cond, linha);
        }

        if (Checar("switch"))
        {
            p++; Exigir("("); var suj = AnalisarExpr(); Exigir(")");
            Exigir("{");

            var casos = new List<CasoSwitch>();
            while (!Checar("}") && Atual.Tipo != T.Fim)
            {
                Expr valor = null;
                if (Aceitar("case")) { valor = AnalisarExpr(); Exigir(":"); }
                else if (Aceitar("default")) Exigir(":");
                else throw new ErroDeSintaxe(
                    $"dentro de switch esperava 'case' ou 'default', encontrei '{Atual.Texto}'",
                    Atual.Linha);

                // O corpo vai ate o proximo case/default: sem break, cai nele.
                var corpo = new List<Cmd>();
                while (!Checar("case") && !Checar("default") && !Checar("}") && Atual.Tipo != T.Fim)
                    corpo.Add(AnalisarCmd());

                casos.Add(new CasoSwitch(valor, corpo));
            }
            Exigir("}");
            return new CmdEscolha(suj, casos, linha);
        }

        if (Checar("return"))
        {
            p++;
            Expr v = Checar(";") ? null : AnalisarExpr();
            Exigir(";");
            return new CmdRetorno(v, linha);
        }

        if (Checar("break")) { p++; Exigir(";"); return new CmdBreak(linha); }
        if (Checar("continue")) { p++; Exigir(";"); return new CmdContinue(linha); }

        // "delete sensor;" e "delete[] buffer;"
        if (Checar("delete"))
        {
            p++;
            if (Aceitar("[")) Exigir("]");
            var alvo = AnalisarExpr();
            Exigir(";");
            return new CmdExpr(new Chamada(new Variavel("__apagar", linha), [alvo], linha), linha);
        }

        var c2 = AnalisarCmdSimples();
        Exigir(";");
        return c2;
    }

    /// <summary>
    /// "for (int x : v)": um ":" antes do primeiro ";", fora de parenteses
    /// e sem "?" antes (o ":" do ternario nao conta).
    /// </summary>
    bool EhForDeIntervalo()
    {
        int fundo = 0;
        bool ternario = false;
        for (int k = p; k < toks.Count; k++)
        {
            var t = toks[k];
            if (t.Tipo != T.Op) continue;
            if (t.Texto is "(" or "[" or "{") fundo++;
            else if (t.Texto is ")" or "]" or "}")
            {
                if (fundo == 0) return false;
                fundo--;
            }
            else if (fundo == 0 && t.Texto == ";") return false;
            else if (fundo == 0 && t.Texto == "?") ternario = true;
            else if (fundo == 0 && t.Texto == ":" && !ternario) return true;
        }
        return false;
    }

    Cmd AnalisarForDeIntervalo(int linha)
    {
        string tipo = LerTipoFlexivel();
        bool porReferencia = ultimoReferencia;
        string nome = Avancar().Texto;
        Exigir(":");
        var sequencia = AnalisarExpr();
        Exigir(")");
        return new CmdParaCadaC(tipo, nome, porReferencia, sequencia, AnalisarCmd(), linha);
    }

    /// <summary>Declaracao local ou expressao (sem consumir o ';').</summary>
    Cmd AnalisarCmdSimples()
    {
        int linha = Atual.Linha;

        if (Checar("enum"))
        {
            var membros = AnalisarEnum();
            return membros.Count == 1
                ? membros[0] : new CmdVarios([.. membros], linha);
        }

        if (Checar("static_assert")) { while (!Checar(";") && Atual.Tipo != T.Fim) p++; return new CmdVazio(linha); }

        // "struct tm agora;" dentro de uma funcao. A palavra "struct"
        // e enfeite aqui: o que importa e o tipo que vem depois. Sem
        // isto o parser lia "struct" como a CLASSE e "tm" como o nome
        // da variavel, e parava na palavra seguinte.
        if ((Checar("struct") || Checar("class")) && Espiar().Tipo == T.Id
            && (Espiar(2).Tipo == T.Id || Eh(Espiar(2), "*") || Eh(Espiar(2), "&"))) p++;

        string tipo = TentarTipo(local: true);
        if (tipo is not null)
        {
            bool externo = ultimoExterno, referencia = ultimoReferencia, estatico = ultimoEstatico;
            string nome = LerPonteiroDeFuncao();
            if (nome is not null) tipo = "funcao*";
            else nome = SemEspaco(Avancar().Texto);
            var ds = AnalisarVariaveis(tipo, nome, linha, externo, referencia);

            // "static" dentro da funcao: a variavel e uma so para todas as chamadas.
            if (estatico)
                ds = [.. ds.Select(c => c switch
                {
                    CmdDecl d => d with { EstaticoLocal = true },
                    CmdDeclObj o => o with { EstaticoLocal = true },
                    _ => c,
                })];

            // CmdVarios, nao CmdBloco: bloco criaria escopo novo e as
            // variaveis sumiriam ao fim da propria declaracao.
            return ds.Count == 1 ? ds[0] : new CmdVarios(ds, linha);
        }
        return new CmdExpr(AnalisarExprComVirgula(), linha);
    }

    // ================= expressoes =================
    public Expr AnalisarExpr() => Atribuir();

    /// <summary>
    /// "i++, j--" no passo do for: a virgula do C, que avalia os dois. So
    /// vale onde nao ha lista de argumentos em volta.
    /// </summary>
    Expr AnalisarExprComVirgula()
    {
        var e = AnalisarExpr();
        while (Checar(","))
        {
            int linha = Atual.Linha;
            p++;
            var dir = AnalisarExpr();
            e = new Chamada(new Variavel("__virgula", linha), [e, dir], linha);
        }
        return e;
    }

    static readonly string[] OpsAtrib =
        ["=", "+=", "-=", "*=", "/=", "%=", "&=", "|=", "^=", "<<=", ">>="];

    Expr Atribuir()
    {
        var esq = Ternario();
        if (Atual.Tipo == T.Op && OpsAtrib.Contains(Atual.Texto))
        {
            var op = Avancar();

            // "inimigos[i] = {2.5, 3.5, 3, true};": a struct inteira de uma
            // vez, com a lista entre chaves do C++11.
            if (op.Texto == "=" && Checar("{"))
                return new Atribuicao("=", esq, new ListaLiteral(LerItens(true), false, op.Linha), op.Linha);

            var dir = Atribuir();                       // associativo a direita
            return new Atribuicao(op.Texto, esq, dir, op.Linha);
        }
        return esq;
    }

    Expr Ternario()
    {
        var c = Binario(0);
        if (Aceitar("?"))
        {
            var s = AnalisarExpr();
            Exigir(":");
            var n = Atribuir();
            return new Ternaria(c, s, n, c.Linha);
        }
        return c;
    }

    // menor precedencia primeiro
    static readonly string[][] Niveis =
    [
        ["||"], ["&&"], ["|"], ["^"], ["&"],
        ["==", "!="], ["<", ">", "<=", ">="], ["<<", ">>"],
        ["+", "-"], ["*", "/", "%"]
    ];

    Expr Binario(int nivel)
    {
        if (nivel >= Niveis.Length) return Unario();
        var esq = Binario(nivel + 1);
        while (Atual.Tipo == T.Op && Niveis[nivel].Contains(Atual.Texto))
        {
            var op = Avancar();
            var dir = Binario(nivel + 1);
            esq = new Binaria(op.Texto, esq, dir, op.Linha);
        }
        return esq;
    }

    Expr Unario()
    {
        // "&" aqui e o "endereco de", como em decode(&results). Nao ha
        // ponteiro neste motor: quem precisa do objeto apontado -- so o
        // decode() do infravermelho, por enquanto -- olha o ARGUMENTO em
        // vez do valor. Sem aceitar o simbolo, o sketch nem compilava.
        if (Atual.Tipo == T.Op && Atual.Texto == "&"
            && (Espiar().Tipo == T.Id || Eh(Espiar(), "(")))
        {
            int linhaE = Atual.Linha;
            p++;
            return new Unaria("&", Unario(), linhaE);
        }

        // "*this", "*sensor": o objeto apontado -- que aqui e o proprio.
        if (Atual.Tipo == T.Op && Atual.Texto == "*")
        {
            int linhaP = Atual.Linha;
            p++;
            return new Unaria("*", Unario(), linhaP);
        }

        if (Atual.Tipo == T.Op && Atual.Texto is "!" or "-" or "+" or "~" or "++" or "--")
        {
            var op = Avancar();
            return new Unaria(op.Texto, Unario(), op.Linha);
        }

        if (Checar("new")) return AnalisarNovo();

        // Conversao: "(char)payload[i]", "(int)x", "(const char*)dados".
        // Nao confundir com "(a + b)": so vale quando o que vem depois do
        // parentese e um TIPO seguido de ")", de "*" ou de outra palavra de
        // tipo. Antes o cast era jogado fora, e "(char)codigo" continuava
        // sendo numero.
        if (Checar("(") && Espiar().Tipo == T.Id
            && (tipos.Contains(Espiar().Texto) || apelidos.ContainsKey(Espiar().Texto))
            && (Espiar(2).E(")") || Espiar(2).E("*") || Espiar(2).E("&")
                || tipos.Contains(Espiar(2).Texto)))
        {
            int linhaConversao = Atual.Linha;
            p++;
            string tipoAlvo = LerTipo();
            Exigir(")");
            return new Conversao(tipoAlvo, Unario(), linhaConversao);
        }
        return PosFixo();
    }

    /// <summary>"new Botao(4)", "new uint8_t[n]", "new Botao[3]".</summary>
    Expr AnalisarNovo()
    {
        int linha = Atual.Linha;
        Exigir("new");

        // "new (buffer) Tipo": o lugar de montar nao importa aqui.
        if (Checar("(") && !EhInicioDeDeclaracao()) p = Fecha(p) + 1;

        string tipo = SemPonteiro(LerTipoFlexivel());
        var args = new List<Expr>();
        Expr tamanho = null;

        if (Aceitar("["))
        {
            tamanho = AnalisarExpr();
            Exigir("]");
            if (Aceitar("(")) Exigir(")");
            else if (Checar("{")) args = LerItens(false);
        }
        else if (Aceitar("("))
        {
            if (!Checar(")"))
                do { args.Add(AnalisarExpr()); } while (Aceitar(","));
            Exigir(")");
        }
        else if (Checar("{")) args = LerItens(false);

        return new Novo(tipo, args, tamanho, linha);
    }

    Expr PosFixo()
    {
        var e = Primario();
        while (true)
        {
            if (Aceitar("("))
            {
                var args = new List<Expr>();
                if (!Checar(")"))
                    do { args.Add(AnalisarExpr()); } while (Aceitar(","));
                Exigir(")");
                e = new Chamada(e, args, e.Linha);
            }
            // "sensor->ler()" e "sensor.ler()": sem endereco no motor, a
            // seta e o ponto chegam ao mesmo objeto.
            else if (Aceitar(".") || Aceitar("->"))
            {
                if (Checar("~")) { p++; e = new Membro(e, "~" + Avancar().Texto, e.Linha); continue; }
                Aceitar("template");
                e = new Membro(e, SemEspaco(Avancar().Texto), e.Linha);
            }
            else if (Aceitar("["))
            {
                var i = AnalisarExpr();
                Exigir("]");
                e = new Indice(e, i, e.Linha);
            }
            else if (Atual.Tipo == T.Op && Atual.Texto is "++" or "--")
            {
                var op = Avancar();
                e = new PosFixa(op.Texto, e, op.Linha);
            }
            else return e;
        }
    }

    Expr Primario()
    {
        var t = Atual;

        if (t.Tipo == T.Num) { p++; return new Literal(t.Valor, t.Texto, t.Inteiro, false, t.Linha); }
        if (t.Tipo == T.Char) { p++; return new Literal(t.Valor, t.Texto, true, false, t.Linha) { Caractere = true }; }
        if (t.Tipo == T.Str)
        {
            p++;
            // "abc" "def": o C junta os textos vizinhos num so. E como se
            // quebra uma mensagem comprida -- e como sai de uma macro.
            string texto = t.Texto;
            while (Atual.Tipo == T.Str) texto += Avancar().Texto;
            return new Literal(0, texto, false, true, t.Linha);
        }

        // Funcao anonima: server.on("/", []() { ... }). A lista de captura
        // e ignorada: aqui toda funcao ja enxerga as globais.
        if (Checar("["))
        {
            int linhaLambda = Atual.Linha;
            p++;
            while (!Checar("]") && Atual.Tipo != T.Fim) p++;
            Exigir("]");

            var parametros = new List<Param>();
            if (Aceitar("("))
            {
                parametros = LerParametros();
                Exigir(")");
            }
            Aceitar("mutable");
            if (Aceitar("->")) LerTipoFlexivel();
            return new FuncaoAnonima(parametros, AnalisarBloco(), linhaLambda);
        }

        // "static_cast<int>(x)": o cast do C++, escrito por extenso.
        if (t.Tipo == T.Id && t.Texto is "static_cast" or "reinterpret_cast" or "const_cast" or "dynamic_cast"
            && Eh(Espiar(), "<"))
        {
            int linhaCast = t.Linha;
            p += 2;
            string alvo = LerTipoFlexivel();
            Exigir(">");
            Exigir("(");
            var dentro = AnalisarExpr();
            Exigir(")");
            return new Conversao(alvo, dentro, linhaCast);
        }

        if (Aceitar("("))
        {
            var e = AnalisarExprComVirgula();
            Exigir(")");
            return e;
        }

        // "sizeof(int)", "sizeof(unsigned long)", "sizeof(struct Leitura)":
        // dentro vem um TIPO. Lido como expressao, o "unsigned long"
        // parava a compilacao, e o "int" virava uma variavel que nao existe.
        if (t.Tipo == T.Id && t.Texto == "sizeof" && Eh(Espiar(), "(")
            && Espiar(2) is { Tipo: T.Id } dentroDoSizeof
            && (tipos.Contains(dentroDoSizeof.Texto) || apelidos.ContainsKey(dentroDoSizeof.Texto)
                || dentroDoSizeof.Texto is "struct" or "class" or "union" or "enum"))
        {
            int linhaSizeof = t.Linha;
            p += 2;
            while (Atual.Tipo == T.Id && Atual.Texto is "struct" or "class" or "union" or "enum") p++;
            string tipoMedido = EhInicioDeDeclaracao() ? LerTipo() : LerTipoFlexivel();
            Exigir(")");
            return new Chamada(new Variavel("sizeof", linhaSizeof),
                               [new NomeDeTipo(tipoMedido, linhaSizeof)], linhaSizeof);
        }

        if (t.Tipo == T.Id) { p++; return new Variavel(SemEspaco(t.Texto), t.Linha); }

        throw new ErroDeSintaxe($"expressao inesperada perto de '{t.Texto}'", t.Linha);
    }
}
