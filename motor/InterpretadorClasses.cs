namespace Simulador;

/// <summary>
/// Um valor ja calculado, usado onde o interpretador precisa passar um
/// argumento sem ter uma expressao para ele -- o byte que o print() de
/// uma classe derivada do Print entrega ao write() dela.
/// </summary>
public record Pronto(Valor Valor, int Linha) : Expr(Linha);

/// <summary>
/// As classes do C++: o que roda por dentro de uma biblioteca de fora.
///
/// Um objeto de classe tem um escopo proprio (<see cref="Objeto.Escopo"/>)
/// cujas variaveis SAO os campos. Um metodo roda num escopo filho dele,
/// e e por isso que "_pino" dentro do metodo acha o campo sem ninguem
/// escrever "this->". Acima do objeto fica o escopo dos static da classe,
/// e acima dele o das globais -- a mesma ordem de busca do C++.
/// </summary>
public partial class Interpretador
{
    sealed class ClasseInfo
    {
        public string Nome;
        public ClasseDecl Decl;
        public readonly List<ClasseInfo> Bases = [];
        public readonly Dictionary<string, List<Funcao>> Metodos = new(StringComparer.Ordinal);
        public readonly List<Funcao> Construtores = [];
        public Funcao Destrutor;
        public Ambiente Estaticos;
        public bool Iniciada;

        /// <summary>Deriva do Print do core: print() e println() saem pelo write() dela.</summary>
        public bool Imprime;

        /// <summary>A classe e as bases, da mais perto para a mais longe: montada quando as bases ja estao ligadas.</summary>
        public List<ClasseInfo> Linhagem;
    }

    readonly Dictionary<string, ClasseInfo> classes = new(StringComparer.Ordinal);

    /// <summary>
    /// As funcoes do programa com cada assinatura: "mostrar(int)" e
    /// "mostrar(String)" sao duas. O <see cref="funcoes"/> continua com uma
    /// por nome, que e o que os callbacks por nome usam.
    /// </summary>
    readonly Dictionary<string, List<Funcao>> sobrecargas = new(StringComparer.Ordinal);

    /// <summary>Quantas funcoes estao abertas uma dentro da outra agora.</summary>
    int chamadasAbertas;

    /// <summary>
    /// Mais do que isto e recursao sem fim. Na placa a pilha estoura bem
    /// antes -- a da ESP32 tem 8 KB --, e o chip reinicia.
    /// </summary>
    const int LimiteDeChamadas = 1000;

    /// <summary>C, C++ e C#: as linguagens do analisador de C++.</summary>
    bool EhDoC => (Prog?.Linguagem ?? Linguagem.Arduino) is Linguagem.Arduino or Linguagem.C or Linguagem.CSharp;

    // =================================================================
    //  Registro
    // =================================================================

    void RegistrarClasses()
    {
        foreach (var decl in prog.Classes ?? [])
        {
            if (classes.TryGetValue(decl.Nome, out var ja))
            {
                // O mesmo .h incluido por dois arquivos: e a mesma classe.
                if (ja.Decl.Linha == decl.Linha) continue;
                throw new ErroDeExecucao(
                    $"a classe {decl.Nome} foi declarada duas vezes "
                    + $"(a outra esta em {Origens.Onde(ja.Decl.Linha)})", decl.Linha);
            }

            var info = new ClasseInfo { Nome = decl.Nome, Decl = decl };
            foreach (var m in decl.Metodos) AdicionarMetodo(info, m);
            classes[decl.Nome] = info;
        }

        foreach (var info in classes.Values)
            foreach (string b in info.Decl.Bases)
            {
                if (classes.TryGetValue(b, out var baseInfo)) info.Bases.Add(baseInfo);
                else if (b is "Print" or "Stream" or "Printable" or "HardwareSerial") info.Imprime = true;
            }

        // As bases nao mudam mais: a linhagem de cada classe fica pronta, e a
        // busca de cada metodo chamado nao monta a arvore de novo.
        foreach (var info in classes.Values) info.Linhagem = MontarLinhagem(info);

        foreach (var info in classes.Values)
        {
            info.Imprime = Hierarquia(info).Any(k => k.Imprime);
            CriarEstaticos(info, 0);
        }
    }

    Ambiente CriarEstaticos(ClasseInfo info, int fundo)
    {
        if (info.Estaticos is not null) return info.Estaticos;
        if (fundo > 32)
            throw new ErroDeExecucao($"a classe {info.Nome} herda dela mesma", info.Decl.Linha);

        var pai = info.Bases.Count > 0 ? CriarEstaticos(info.Bases[0], fundo + 1) : global;
        info.Estaticos = new Ambiente(pai) { Classe = info.Nome };
        return info.Estaticos;
    }

    /// <summary>A classe e as bases, da mais perto para a mais longe.</summary>
    static List<ClasseInfo> Hierarquia(ClasseInfo info) => info.Linhagem ?? MontarLinhagem(info);

    static List<ClasseInfo> MontarLinhagem(ClasseInfo info)
    {
        var linhagem = new List<ClasseInfo>();
        var vistas = new HashSet<ClasseInfo>();
        var fila = new Stack<ClasseInfo>();
        fila.Push(info);
        while (fila.Count > 0)
        {
            var k = fila.Pop();
            if (!vistas.Add(k)) continue;
            linhagem.Add(k);
            for (int i = k.Bases.Count - 1; i >= 0; i--) fila.Push(k.Bases[i]);
        }
        return linhagem;
    }

    bool DerivaDe(string classe, string baseProcurada)
    {
        if (classe == baseProcurada) return true;
        return classes.TryGetValue(classe, out var info) && Hierarquia(info).Any(k => k.Nome == baseProcurada);
    }

    static void AdicionarMetodo(ClasseInfo info, Funcao f)
    {
        if (f.EhConstrutor || f.Nome == info.Nome)
        {
            Juntar(info.Construtores, f with { EhConstrutor = true });
            return;
        }

        if (f.Nome.StartsWith('~'))
        {
            if (f.Corpo is not null || info.Destrutor is null) info.Destrutor = f;
            return;
        }

        if (!info.Metodos.TryGetValue(f.Nome, out var lista)) info.Metodos[f.Nome] = lista = [];
        Juntar(lista, f);
    }

    static string TipoBasico(string tipo) => (tipo ?? "").TrimEnd('*', ' ').Replace("const ", "");

    static bool MesmosTipos(Funcao a, Funcao b)
    {
        for (int i = 0; i < a.Params.Count; i++)
            if (TipoBasico(a.Params[i].Tipo) != TipoBasico(b.Params[i].Tipo)) return false;
        return true;
    }

    /// <summary>
    /// Poe a funcao na lista das que tem o mesmo nome. O prototipo e a
    /// definicao da MESMA funcao viram uma so: o corpo de uma, os valores
    /// padrao da outra -- o C++ poe o "= 9600" no .h e o corpo no .cpp.
    /// </summary>
    static void Juntar(List<Funcao> lista, Funcao nova)
    {
        int k = lista.FindIndex(x => x.Params.Count == nova.Params.Count && MesmosTipos(x, nova));

        // Mesma quantidade de parametros, tipos escritos de outro jeito
        // (um typedef no .h, o tipo cru no .cpp): se so um dos dois tem
        // corpo, e a mesma funcao.
        if (k < 0)
            k = lista.FindIndex(x => x.Params.Count == nova.Params.Count
                                     && (x.Corpo is null) != (nova.Corpo is null)
                                     && lista.Count(y => y.Params.Count == nova.Params.Count) == 1);

        if (k < 0) { lista.Add(nova); return; }

        var velha = lista[k];
        if (velha.Corpo is not null && nova.Corpo is not null)
        {
            // O mesmo cabecalho lido duas vezes: nada a fazer.
            if (velha.Linha == nova.Linha) return;
            throw new ErroDeExecucao(
                $"{NomeParaMostrar(nova)}() foi escrita duas vezes (a outra esta em {Origens.Onde(velha.Linha)}). "
                + "O compilador recusa isso.", nova.Linha);
        }

        var comCorpo = velha.Corpo is not null ? velha : nova;
        var outra = ReferenceEquals(comCorpo, velha) ? nova : velha;

        var parametros = comCorpo.Params
            .Select((p, i) => p.Padrao is null && outra.Params[i].Padrao is not null
                ? p with { Padrao = outra.Params[i].Padrao }
                : p)
            .ToList();

        lista[k] = comCorpo with
        {
            Params = parametros,
            Estatico = velha.Estatico || nova.Estatico,
            Virtual = velha.Virtual || nova.Virtual,
            Classe = velha.Classe ?? nova.Classe,
            Inicializadores = comCorpo.Inicializadores.Count > 0 ? comCorpo.Inicializadores : outra.Inicializadores,
        };
    }

    static string NomeParaMostrar(Funcao f)
        => f.Classe is null ? f.Nome : f.EhConstrutor ? f.Classe + "::" + f.Classe : f.Classe + "::" + f.Nome;

    /// <summary>
    /// Uma funcao do programa. "Botao::ler" definida fora da classe vai
    /// morar na classe; as outras entram na lista de sobrecargas.
    /// </summary>
    void RegistrarFuncao(Funcao f)
    {
        if (f.Nome.Contains("::") && VincularDefinicao(f)) return;

        // As outras linguagens nao tem sobrecarga: a ultima definicao vale,
        // como no Python.
        if (!EhDoC)
        {
            if (f.Corpo is null) return;
            funcoes[f.Nome] = f;
            sobrecargas[f.Nome] = [f];
            return;
        }

        if (!sobrecargas.TryGetValue(f.Nome, out var lista)) sobrecargas[f.Nome] = lista = [];
        Juntar(lista, f);

        var comCorpo = lista.FirstOrDefault(x => x.Corpo is not null);
        if (comCorpo is not null) funcoes[f.Nome] = comCorpo;
    }

    /// <summary>"void Botao::ler() { ... }": o corpo de um metodo, escrito fora da classe.</summary>
    bool VincularDefinicao(Funcao f)
    {
        int corte = f.Nome.LastIndexOf("::", StringComparison.Ordinal);
        if (corte <= 0) return false;

        string dono = f.Nome[..corte], metodo = f.Nome[(corte + 2)..];
        int corteDono = dono.LastIndexOf("::", StringComparison.Ordinal);
        if (corteDono >= 0) dono = dono[(corteDono + 2)..];

        if (!classes.TryGetValue(dono, out var info)) return false;

        var nova = f with { Nome = metodo, Classe = dono };

        if (metodo.StartsWith('~'))
        {
            info.Destrutor = nova;
            return true;
        }

        if (metodo == dono)
        {
            Juntar(info.Construtores, nova with { EhConstrutor = true });
            return true;
        }

        if (!info.Metodos.TryGetValue(metodo, out var lista))
            throw new ErroDeExecucao(
                $"'{dono}::{metodo}' foi escrito fora da classe, mas a classe {dono} nao declara "
                + $"{metodo}() dentro dela. Declare o metodo na classe (no .h).", f.Linha);

        Juntar(lista, nova);
        return true;
    }

    /// <summary>
    /// Os static da classe ganham valor quando a declaracao dela passa --
    /// e antes disso ninguem pode usa-los, como no C++.
    /// </summary>
    void IniciarEstaticos(ClasseDecl decl)
    {
        if (!classes.TryGetValue(decl.Nome, out var info) || info.Iniciada) return;
        if (info.Decl.Linha != decl.Linha) return;
        info.Iniciada = true;

        foreach (var cmd in decl.Estaticos) Exec(cmd, info.Estaticos);
    }

    // =================================================================
    //  Objetos
    // =================================================================

    Objeto NovoObjeto(ClasseInfo info)
    {
        var obj = new Objeto(info.Nome);
        obj.Escopo = new Ambiente(info.Estaticos, obj.Campos, obj);
        return obj;
    }

    Objeto Construir(ClasseInfo info, List<Expr> args, Ambiente amb, int linha)
    {
        var obj = NovoObjeto(info);
        Inicializar(obj, info, args, amb, linha, 0);
        return obj;
    }

    /// <summary>
    /// Monta o objeto na ordem do C++: as bases primeiro, depois os campos
    /// na ordem em que foram declarados (com o valor da lista de
    /// inicializacao, se houver), e so entao o corpo do construtor.
    /// </summary>
    void Inicializar(Objeto obj, ClasseInfo info, List<Expr> args, Ambiente amb, int linha, int fundo)
    {
        if (fundo > 32)
            throw new ErroDeExecucao($"o construtor de {info.Nome} chama a si mesmo sem parar", linha);

        Funcao ctor = null;
        if (info.Construtores.Count > 0)
            ctor = Escolher(info.Construtores, args, amb, linha, info.Nome);
        else if (args.Count > 0)
        {
            // Sem construtor, "Ponto p = {1, 2}" preenche campo a campo.
            foreach (var b in info.Bases) Inicializar(obj, b, [], amb, linha, fundo + 1);
            PreencherCampos(obj, info, args, amb);
            return;
        }

        var local = new Ambiente(EscopoComSelf(obj, info.Nome));
        CobrarChamada(args.Count);
        chamadasAbertas++;
        try
        {
            if (PilhaCheia()) EstourarPilha(linha);
            if (ctor is not null) VincularParametros(ctor, args, amb, local, linha);

            // "Botao() : Botao(2) { }": um construtor que usa o outro.
            var delegado = ctor?.Inicializadores.FirstOrDefault(x => x.Nome == info.Nome);
            if (delegado is not null)
            {
                Inicializar(obj, info, delegado.Args, local, linha, fundo + 1);
                RodarCorpo(ctor, local);
                return;
            }

            // No Python, o __init__ da classe de cima so roda pelo super(): aqui
            // entram so os valores de partida dos campos dela ("x = 0" no corpo).
            if (info.Decl.EhPython)
                foreach (var deCima in Hierarquia(info).Skip(1).Reverse())
                    foreach (var campo in deCima.Decl.Campos)
                        IniciarCampo(obj, campo, null, local);
            else
                foreach (var b in info.Bases)
                {
                    var ini = ctor?.Inicializadores.FirstOrDefault(x => x.Nome == b.Nome);
                    Inicializar(obj, b, ini?.Args ?? [], local, linha, fundo + 1);
                }

            foreach (var campo in info.Decl.Campos)
                IniciarCampo(obj, campo, ctor?.Inicializadores, local);

            RodarCorpo(ctor, local);
        }
        finally
        {
            chamadasAbertas--;
            FecharEscopo(local);
        }
    }

    void RodarCorpo(Funcao f, Ambiente local)
    {
        if (f?.Corpo is null) return;
        ExecCorpoDeFuncao(f.Corpo, local, out _);
    }

    static string NomeDoCampo(Cmd campo) => campo switch
    {
        CmdDecl d => d.Nome,
        CmdDeclObj o => o.Nome,
        _ => null,
    };

    void IniciarCampo(Objeto obj, Cmd campo, List<Inicializador> inits, Ambiente local)
    {
        string nome = NomeDoCampo(campo);
        var ini = nome is null ? null : inits?.FirstOrDefault(x => x.Nome == nome);

        switch (campo)
        {
            // "_pino(pino)": o valor vem da lista, calculado com os
            // parametros do construtor.
            case CmdDecl d when ini is not null && !d.EhArray:
            {
                ExecDecl(d with { Inicial = null, Itens = null }, obj.Escopo);
                if (ini.Args.Count == 0) return;

                var v = Avaliar(ini.Args[0], local);
                if (obj.Escopo.BuscarObjetoLocal(d.Nome, out var estrutura) && v.Referencia is Objeto fonte)
                    foreach (var (k, valor) in fonte.Campos) estrutura.Campos[k] = valor;
                else
                    obj.Escopo.Atribuir(d.Nome, Coagir(v, d.Tipo));
                return;
            }

            case CmdDecl d:
                ExecDecl(d, obj.Escopo);
                return;

            // Objeto dentro do objeto: "Servo _servo;", "Botao _botao{4};".
            // Com a lista de inicializacao, os argumentos sao os dela.
            case CmdDeclObj o:
            {
                var feito = o with
                {
                    Args = ini?.Args ?? o.Args,
                    Inicial = ini is null ? o.Inicial : null,
                };
                var provisorio = new Ambiente(ini is not null ? local : obj.Escopo);
                ExecDeclObj(feito, provisorio);

                if (provisorio.TirarObjeto(o.Nome, out var dentro)) obj.Escopo.DeclararObjeto(o.Nome, dentro);
                else if (provisorio.BuscarLocal(o.Nome, out var valor)) obj.Escopo.Declarar(o.Nome, valor);
                return;
            }

            default:
                Exec(campo, obj.Escopo);
                return;
        }
    }

    /// <summary>"Ponto p = {1, 2}" numa classe sem construtor: um valor por campo.</summary>
    void PreencherCampos(Objeto obj, ClasseInfo info, List<Expr> itens, Ambiente amb)
    {
        foreach (var campo in info.Decl.Campos) IniciarCampo(obj, campo, null, obj.Escopo);

        var nomes = info.Decl.Campos.Select(NomeDoCampo).Where(n => n is not null).ToList();
        for (int i = 0; i < itens.Count; i++)
        {
            string nome;
            Expr valorExpr;
            if (itens[i] is ArgNomeado an) { nome = an.Nome; valorExpr = an.Valor; }
            else if (i < nomes.Count) { nome = nomes[i]; valorExpr = itens[i]; }
            else break;

            var decl = info.Decl.Campos.OfType<CmdDecl>().FirstOrDefault(d => d.Nome == nome);
            var v = Avaliar(valorExpr, amb);
            obj.Escopo.Atribuir(nome, decl is null ? v : Coagir(v, decl.Tipo));
        }
    }

    /// <summary>
    /// A copia de um objeto, como o C++ faz em "Botao b = a;": campo a
    /// campo, e os objetos de dentro copiados tambem. Ponteiro continua
    /// apontando para o mesmo lugar -- e o que a copia do C++ faz com eles.
    /// </summary>
    Objeto Copiar(Objeto o)
    {
        if (o.Escopo is null || !classes.TryGetValue(o.Classe, out var info)) return o;

        var copia = info.Construtores.FirstOrDefault(f => f.Corpo is not null && f.Params.Count == 1
                                                          && TipoBasico(f.Params[0].Tipo) == info.Nome);
        if (copia is not null)
            return Construir(info, [new Pronto(Valor.DeReferencia(o), 0)], global, 0);

        var novo = NovoObjeto(info);
        o.Escopo.CopiarConteudo(novo.Escopo, Copiar);
        return novo;
    }

    /// <summary>"a = b;" entre dois objetos: o operator= da classe, ou a copia campo a campo.</summary>
    bool AtribuirObjetoDeClasse(string nome, Valor v, Ambiente amb)
    {
        if (!amb.BuscarObjeto(nome, out var alvo) || alvo.Escopo is null) return false;
        if (!classes.TryGetValue(alvo.Classe, out var info)) return false;

        if (AcharLista(info, "operator=") is { } atribuir)
        {
            var f = Escolher(atribuir, [new Pronto(v, 0)], amb, 0, info.Nome + "::operator=");
            ChamarMetodo(alvo, f, [new Pronto(v, 0)], amb, 0);
            return true;
        }

        if (v.Referencia is not Objeto fonte || fonte.Escopo is null)
            throw new ErroDeExecucao(
                $"'{nome}' e um objeto {info.Nome}, e so recebe outro {info.Nome}", 0);

        fonte.Escopo.CopiarConteudo(alvo.Escopo, Copiar);
        return true;
    }

    /// <summary>Declaracao de objeto de uma classe do programa.</summary>
    bool DeclararObjetoDeClasse(CmdDeclObj d, Ambiente amb)
    {
        if (!classes.TryGetValue(d.Classe, out var info)) return false;

        Objeto obj;
        if (d.Inicial is Chamada { Alvo: Variavel construtor } ch && construtor.Nome == info.Nome)
            obj = Construir(info, ch.Args, amb, d.Linha);
        else if (d.Inicial is not null)
        {
            var v = Avaliar(d.Inicial, amb);
            obj = v.Referencia is Objeto fonte && DerivaDe(fonte.Classe, info.Nome)
                ? Copiar(fonte)
                // "Botao b = 4;": o construtor de um argumento converte.
                : Construir(info, [new Pronto(v, d.Linha)], amb, d.Linha);
        }
        else obj = Construir(info, d.Args, amb, d.Linha);

        amb.DeclararObjeto(d.Nome, obj);
        return true;
    }

    /// <summary>Um item de vetor de classe: "Botao botoes[] = {2, 3, Botao(4)};".</summary>
    Valor ElementoDeClasse(ClasseInfo info, Expr item, Ambiente amb, int linha)
    {
        Objeto obj;
        switch (item)
        {
            case null:
                obj = Construir(info, [], amb, linha);
                break;
            case ListaLiteral l:
                obj = Construir(info, l.Itens, amb, linha);
                break;
            case Chamada { Alvo: Variavel v } ch when v.Nome == info.Nome:
                obj = Construir(info, ch.Args, amb, linha);
                break;
            default:
            {
                // Conta uma vez so: o valor ja calculado vai para o construtor.
                var valor = Avaliar(item, amb);
                obj = valor.Referencia is Objeto fonte && DerivaDe(fonte.Classe, info.Nome)
                    ? Copiar(fonte)
                    : Construir(info, [new Pronto(valor, linha)], amb, linha);
                break;
            }
        }
        return Valor.DeReferencia(obj);
    }

    /// <summary>"new Botao(4)", "new uint8_t[16]".</summary>
    Valor AvaliarNovo(Novo nv, Ambiente amb)
    {
        classes.TryGetValue(nv.Tipo, out var info);

        if (nv.Tamanho is not null)
        {
            long n = (long)Avaliar(nv.Tamanho, amb).Num;
            if (n < 0 || n > 1_000_000)
                throw new ErroDeExecucao($"new {nv.Tipo}[{n}]: tamanho impossivel", nv.Linha);

            // O vetor ocupa a heap da placa: "new int[2000]" num Uno nao cabe.
            long custoDoVetor = ReservarParaNovo(n * Math.Max(1, TamanhoNaPilha(nv.Tipo)), nv.Linha);
            if (custoDoVetor < 0) return Valor.DeInt(0);
            var vetor = new Valor[n];
            for (int i = 0; i < n; i++)
                vetor[i] = info is not null ? ElementoDeClasse(info, null, amb, nv.Linha)
                         : estruturas.ContainsKey(nv.Tipo) ? Valor.DeReferencia(NovaEstrutura(nv.Tipo, null, amb))
                         : i < nv.Args.Count ? Coagir(Avaliar(nv.Args[i], amb), nv.Tipo)
                         : ZeroDe(nv.Tipo);
            AnotarNovo(vetor, custoDoVetor);
            return Valor.DeReferencia(vetor);
        }

        if (info is not null)
        {
            long custoDoObjeto = ReservarParaNovo(TamanhoNaPilha(nv.Tipo), nv.Linha);
            if (custoDoObjeto < 0) return Valor.DeInt(0);
            var objeto = Construir(info, nv.Args, amb, nv.Linha);
            AnotarNovo(objeto, custoDoObjeto);
            return Valor.DeReferencia(objeto);
        }

        if (estruturas.ContainsKey(nv.Tipo))
            return Valor.DeReferencia(NovaEstrutura(nv.Tipo, nv.Args, amb));

        // "new int(5)": um lugar so, que o ponteiro le com *p ou p[0].
        if (!EhObjetoDeBiblioteca(nv.Tipo))
            return Valor.DeReferencia(new[]
            {
                nv.Args.Count > 0 ? Coagir(Avaliar(nv.Args[0], amb), nv.Tipo) : Valor.DeInt(0),
            });

        // "new Adafruit_SSD1306(128, 64, &Wire)": o objeto da biblioteca, montado igual.
        var provisorio = new Ambiente(amb);
        ExecDeclObj(new CmdDeclObj(nv.Tipo, "__novo", nv.Args, nv.Linha), provisorio);
        return provisorio.TirarObjeto("__novo", out var feito)
            ? Valor.DeReferencia(feito)
            : Valor.DeInt(0);
    }

    static readonly HashSet<string> TiposEscalares =
    [
        "int","long","short","char","float","double","bool","boolean","byte","unsigned","signed",
        "word","String","uint8_t","uint16_t","uint32_t","uint64_t","int8_t","int16_t","int32_t",
        "int64_t","size_t","auto","unsigned int","unsigned long","unsigned char","unsigned short",
        "long long","unsigned long long","long double","signed char","uint","time_t",
    ];

    bool EhObjetoDeBiblioteca(string tipo) => !TiposEscalares.Contains(tipo) && !classes.ContainsKey(tipo);

    /// <summary>Metodos da String: "porta.length()" numa String e dela, e nao do Serial.</summary>
    static readonly HashSet<string> MetodosDaString =
    [
        "length","charAt","substring","indexOf","lastIndexOf","equals","equalsIgnoreCase","toInt",
        "toFloat","toDouble","trim","toUpperCase","toLowerCase","replace","remove","startsWith",
        "endsWith","concat","c_str","isEmpty","compareTo","setCharAt","getBytes","toCharArray","reserve",
    ];

    static bool EhMetodoDeTexto(string metodo) => MetodosDaString.Contains(metodo);

    /// <summary>Uma struct so de campos, zerada ou preenchida por "{1, 2}".</summary>
    Objeto NovaEstrutura(string tipo, List<Expr> itens, Ambiente amb)
    {
        var molde = estruturas[tipo];
        var estrutura = new Objeto(tipo);
        foreach (var campo in molde)
            estrutura.Campos[campo.Nome] = ValorInicialDoCampo(campo, amb);

        if (itens is not null) PreencherEstrutura(estrutura, molde, itens, amb);
        return estrutura;
    }

    void PreencherEstrutura(Objeto estrutura, List<CmdDecl> molde, List<Expr> itens, Ambiente amb)
    {
        for (int i = 0; i < itens.Count; i++)
        {
            CmdDecl campo;
            Expr valorExpr;
            if (itens[i] is ArgNomeado an)
            {
                campo = molde.FirstOrDefault(c => c.Nome == an.Nome);
                valorExpr = an.Valor;
            }
            else
            {
                if (i >= molde.Count) break;
                campo = molde[i];
                valorExpr = itens[i];
            }
            if (campo is null) continue;

            // Campo que e outra struct: "{ {1, 2}, {3, 4} }".
            if (valorExpr is ListaLiteral sub && estruturas.ContainsKey(SemPonteiro(campo.Tipo)))
            {
                estrutura.Campos[campo.Nome] = Valor.DeReferencia(NovaEstrutura(SemPonteiro(campo.Tipo), sub.Itens, amb));
                continue;
            }

            var v = Avaliar(valorExpr, amb);
            estrutura.Campos[campo.Nome] = campo.EhArray || campo.Tipo == "String"
                ? Valor.DeTexto(v.Mostrar())
                : Coagir(v, campo.Tipo);
        }
    }

    /// <summary>"delete sensor;": roda o destrutor, da classe para as bases.</summary>
    void Apagar(Valor v)
    {
        if (v.Referencia is not Objeto obj || obj.Escopo is null) return;
        if (!classes.TryGetValue(obj.Classe, out var info)) return;

        foreach (var k in Hierarquia(info))
            if (k.Destrutor?.Corpo is not null)
            {
                var escopo = new Ambiente(obj.Escopo);
                try { RodarCorpo(k.Destrutor, escopo); }
                finally { FecharEscopo(escopo); }
            }
    }

    // =================================================================
    //  Chamadas
    // =================================================================

    /// <summary>Os metodos com este nome, na classe mais perto que tem algum.</summary>
    static List<Funcao> AcharLista(ClasseInfo info, string nome)
    {
        foreach (var k in Hierarquia(info))
            if (k.Metodos.TryGetValue(nome, out var lista) && lista.Count > 0) return lista;
        return null;
    }

    Funcao ProcurarMetodo(ClasseInfo info, string nome, List<Expr> args, Ambiente amb, int linha)
    {
        var lista = AcharLista(info, nome);
        if (lista is null) return null;
        if (UnicaQueCabe(lista, args.Count) is { } unica) return unica;
        return Escolher(lista, args, amb, linha, info.Nome + "::" + nome);
    }

    /// <summary>
    /// O caso de quase toda chamada: um metodo so com esse nome, com corpo, e
    /// que aceita esses argumentos. Sem as listas e o nome montados a cada
    /// chamada -- o lixo deles pesava num loop() que chama um metodo atras do outro.
    /// </summary>
    static Funcao UnicaQueCabe(List<Funcao> candidatas, int argumentos)
        => candidatas.Count == 1 && candidatas[0] is { Corpo: not null } unica && Cabe(unica, argumentos) ? unica : null;

    Valor ChamarMetodo(Objeto obj, Funcao f, List<Expr> args, Ambiente amb, int linha)
    {
        Ambiente escopo;
        if (f.Estatico || obj is null)
        {
            if (!f.Estatico)
                throw new ErroDeExecucao(
                    $"{NomeParaMostrar(f)}() nao e static: precisa de um objeto para rodar", linha);
            escopo = classes.TryGetValue(f.Classe ?? "", out var info) ? info.Estaticos : global;
        }
        else escopo = EscopoComSelf(obj, f.Classe);   // no Python, com o "self" (no C++, o proprio objeto)

        return ChamarUsuario(f, args, amb, escopo, linha);
    }

    /// <summary>
    /// Roda uma funcao ou metodo do programa. Os parametros nascem num
    /// escopo novo, filho do escopo de onde a funcao mora -- as globais,
    /// o objeto ou os static da classe.
    /// </summary>
    Valor ChamarUsuario(Funcao f, List<Expr> args, Ambiente amb, Ambiente escopo, int linha)
    {
        if (f.Corpo is null)
            throw new ErroDeExecucao(
                $"{NomeParaMostrar(f)}() foi declarada, mas nao tem corpo em lugar nenhum "
                + "(na placa, o compilador da \"undefined reference\").", linha);

        // Sem parametros e sem nada declarado no corpo, a funcao le e escreve
        // so o que mora fora dela: roda direto no escopo de onde ela mora, sem
        // um escopo vazio a mais por chamada (o getter de toda classe).
        var local = f.Params.Count == 0 && !EhPython && !DeclaraNomes(f.Corpo) ? escopo : new Ambiente(escopo);
        CobrarChamada(args.Count);
        chamadasAbertas++;
        long quadro = 0;
        try
        {
            if (PilhaCheia()) EstourarPilha(linha);
            quadro = EntrarNaPilha(f, linha);
            VincularParametros(f, args, amb, local, linha);
            return ExecCorpoDeFuncao(f.Corpo, local, out var devolvido) ? Retornar(devolvido, f) : Valor.Vazio;
        }
        finally
        {
            chamadasAbertas--;
            if (local != escopo) FecharEscopo(local);
            SairDaPilha(quadro);
        }
    }

    /// <summary>
    /// A pilha estourou? Pelo limite do chip, ou pela pilha de verdade da
    /// thread que roda o sketch -- a dos testes tem 1 MB, e cada chamada
    /// interpretada gasta alguns KB dela. Sem este segundo teste, uma
    /// recursao sem fim derrubava o programa inteiro em vez de dar erro.
    /// </summary>
    bool PilhaCheia()
        => chamadasAbertas > LimiteDeChamadas
           || !System.Runtime.CompilerServices.RuntimeHelpers.TryEnsureSufficientExecutionStack();

    void EstourarPilha(int linha)
        => throw new ErroDeExecucao(
            $"a pilha estourou: {chamadasAbertas} chamadas de funcao uma dentro da outra "
            + "(recursao sem fim?). Na placa, isso reinicia o chip.", linha);

    /// <summary>
    /// O "return 2.5" de uma funcao int devolve 2, como no C++. So nas
    /// linguagens do analisador de C++: em Python a funcao nao tem tipo.
    /// </summary>
    Valor Retornar(Valor v, Funcao f)
    {
        if (!EhDoC || f.Retorno is null or "void" or "auto" || f.EhConstrutor) return v;
        return Coagir(v, f.Retorno);
    }

    void VincularParametros(Funcao f, List<Expr> args, Ambiente amb, Ambiente local, int linha)
    {
        for (int i = 0; i < f.Params.Count; i++)
        {
            var par = f.Params[i];
            if (par.Tipo == "...") break;

            if (i >= args.Count)
            {
                var padrao = par.Padrao is not null ? Avaliar(par.Padrao, local) : Valor.DeInt(0);
                DeclararParametro(local, par, Coagir(padrao, par.Tipo));
                continue;
            }

            var arg = args[i];

            // Um pedaco de vetor de varias dimensoes ("desenhar(quadros[2])"):
            // o parametro e o pedaco, e escrever nele escreve no vetor de fora.
            if (PedacoDoArgumento(arg, amb) is { } pedaco)
            {
                local.Declarar(par.Nome, Valor.DeReferencia(pedaco));
                local.DeclararTipo(par.Nome, par.Tipo + "*");
                continue;
            }

            // O vetor inteiro: "preencher(buffer, 8)". Entra por referencia,
            // como o ponteiro do C.
            if (VetorDoArgumento(arg, amb, out var vetor))
            {
                local.DeclararArray(par.Nome, vetor);
                DimensoesDoParametro(par, arg, amb, local);
                if (EhDeBytes(par.Tipo) || vetor.Length == 0 || vetor[0].Caractere) local.MarcarVetorDeBytes(par.Nome);

                // Dentro da funcao o vetor e um PONTEIRO: "sizeof(v)" da o
                // tamanho do endereco, e nao o do vetor -- como na placa.
                // Guardado como "item|forma": o tipo de cada item e como o
                // parametro foi escrito ("int v[]" ou "int *v").
                string itemDoVetor = par.EhVetor ? par.Tipo : par.Tipo.EndsWith('*') ? par.Tipo[..^1].TrimEnd() : par.Tipo;
                local.DeclararTipo(par.Nome, itemDoVetor + (par.EhVetor ? "|[]" : "|*"));
                continue;
            }

            // "int &a" ou "float *t": o parametro E a variavel de fora.
            if ((par.PorReferencia || EhPonteiroDeEscalar(par.Tipo)) && ApelidoDe(arg, amb) is { } apelido)
            {
                local.Declarar(par.Nome, Valor.DeReferencia(apelido));
                continue;
            }

            var v = Avaliar(arg, amb);

            // Objeto passado por valor chega como copia; por referencia ou
            // ponteiro, e o mesmo objeto.
            if (v.Referencia is Objeto o && o.Escopo is not null && !par.PorReferencia
                && !par.Tipo.EndsWith('*') && classes.ContainsKey(TipoBasico(par.Tipo)))
                v = Valor.DeReferencia(Copiar(o));

            var coagido = Coagir(v, par.Tipo);
            if (par.Tipo == "String" && !par.PorReferencia) coagido = DeclararTexto(local, par.Nome, coagido, linha);
            DeclararParametro(local, par, coagido);
        }
    }

    static void DeclararParametro(Ambiente local, Param par, Valor v)
    {
        if (EhCaractere(par.Tipo)) local.DeclararCaractere(par.Nome, v);
        else local.Declarar(par.Nome, v);
        local.DeclararTipo(par.Nome, par.Tipo);
    }

    static readonly HashSet<string> Numericos =
    [
        "int","long","short","float","double","bool","boolean","byte","unsigned","signed","word",
        "uint8_t","uint16_t","uint32_t","uint64_t","int8_t","int16_t","int32_t","int64_t","size_t",
        "unsigned int","unsigned long","unsigned short","long long","unsigned long long","long double",
        "uint","time_t",
    ];

    /// <summary>"float *t": ponteiro para um numero, que a funcao preenche.</summary>
    static bool EhPonteiroDeEscalar(string tipo)
        => tipo is not null && tipo.EndsWith('*') && Numericos.Contains(TipoBasico(tipo));

    /// <summary>O vetor por tras do argumento, se ele e um vetor inteiro.</summary>
    bool VetorDoArgumento(Expr e, Ambiente amb, out Valor[] vetor)
    {
        vetor = null;
        var sem = SemEndereco(e);

        if (sem is Variavel v)
            return !amb.Buscar(v.Nome, out _) && amb.BuscarArray(v.Nome, out vetor)
                   || amb.Buscar(v.Nome, out var guardado) && guardado.Referencia is Valor[] dentro && (vetor = dentro) is not null;

        if (sem is Membro m && ObjetoDaExpressao(m.Obj, amb) is { Escopo: { } escopo }
            && escopo.BuscarArrayLocal(m.Nome, out vetor))
            return true;

        return false;
    }

    /// <summary>
    /// Um jeito de ler e escrever a variavel que o argumento nomeia -- ou
    /// null, se o argumento e uma conta e nao um lugar.
    /// </summary>
    Apelido ApelidoDe(Expr e, Ambiente amb)
    {
        var sem = SemEndereco(e);
        if (sem is Unaria { Op: "*" } u) sem = u.Alvo;

        switch (sem)
        {
            case Variavel v when amb.Buscar(v.Nome, out var atual) && atual.Referencia is not Objeto:
                return new Apelido(
                    () => amb.Buscar(v.Nome, out var x) ? x : Valor.Vazio,
                    novo => amb.Atribuir(v.Nome, novo));

            case Membro m when ObjetoDaExpressao(m.Obj, amb) is { } dono && dono.Campos.ContainsKey(m.Nome):
                return new Apelido(
                    () => dono.Campos.TryGetValue(m.Nome, out var x) ? x : Valor.Vazio,
                    novo =>
                    {
                        if (dono.Escopo is not null) dono.Escopo.Atribuir(m.Nome, novo);
                        else dono.Campos[m.Nome] = novo;
                    });

            // "int &x = cubo[0][1][2]", "trocar(m[0][0], m[0][1])": o item do vetor esticado.
            case Indice ixNd when PosicaoNoVetor(ixNd, amb, out var vetorNd, out int posNd, out var faltamNd, out _):
            {
                if (faltamNd.Length > 0 || posNd < 0 || posNd >= vetorNd.Length) return null;
                return new Apelido(
                    () => vetorNd[posNd],
                    novo => vetorNd[posNd] = vetorNd[posNd].Caractere ? Valor.ComoCaractere(novo) : novo);
            }

            case Indice ix when ix.Alvo is Variavel av && amb.BuscarArray(av.Nome, out var vetor):
            {
                int i = (int)Avaliar(ix.Idx, amb).Num;
                if (i < 0 || i >= vetor.Length)
                    throw new ErroDeExecucao($"indice {i} fora do vetor '{av.Nome}' (0..{vetor.Length - 1})", ix.Linha);
                return new Apelido(
                    () => vetor[i],
                    novo => vetor[i] = vetor[i].Caractere ? Valor.ComoCaractere(novo) : novo);
            }
        }

        return null;
    }

    // ---------------- escolha da sobrecarga ----------------

    static int Obrigatorios(Funcao f) => f.Params.Count(p => p.Padrao is null && p.Tipo != "...");

    static bool Cabe(Funcao f, int n)
        => n >= Obrigatorios(f) && (n <= f.Params.Count || f.Params.Any(p => p.Tipo == "..."));

    /// <summary>
    /// Qual das funcoes com este nome recebe estes argumentos: primeiro
    /// pela quantidade, depois pelo tipo do que chegou.
    /// </summary>
    Funcao Escolher(List<Funcao> candidatas, List<Expr> args, Ambiente amb, int linha, string nome)
    {
        if (UnicaQueCabe(candidatas, args.Count) is { } unica) return unica;

        var comCorpo = candidatas.Where(f => f.Corpo is not null).ToList();
        if (comCorpo.Count == 0)
            throw new ErroDeExecucao(
                $"{nome}() foi declarada, mas nao tem corpo em lugar nenhum "
                + "(na placa, o compilador da \"undefined reference\").", linha);

        var cabem = comCorpo.Where(f => Cabe(f, args.Count)).ToList();

        if (cabem.Count == 0)
        {
            var formas = comCorpo.Select(f => $"{nome.Split("::")[^1]}({string.Join(", ", f.Params.Select(p => p.Tipo + " " + p.Nome))})");
            throw new ErroDeExecucao(
                $"{nome}() nao recebe {args.Count} argumento(s). O que existe: {string.Join("; ", formas)}", linha);
        }

        if (cabem.Count == 1) return cabem[0];

        var tipos = args.Select(a => TipoDoArgumento(a, amb)).ToList();
        return cabem.OrderByDescending(f => Pontos(f, tipos)).First();
    }

    /// <summary>O que o argumento e, sem rodar nada que tenha efeito.</summary>
    string TipoDoArgumento(Expr e, Ambiente amb)
    {
        switch (SemEndereco(e))
        {
            case Literal { EhTexto: true }: return "texto";
            case Literal { Caractere: true }: return "char";
            case Literal { Inteiro: true }: return "int";
            case Literal: return "real";
            case Pronto pr: return TipoDoValor(pr.Valor);
            case Conversao cv:
                return cv.Tipo == "String" || cv.Tipo.EndsWith("char*") ? "texto"
                     : cv.Tipo is "float" or "double" ? "real"
                     : cv.Tipo == "char" ? "char" : "int";
            case Variavel v:
                if (amb.BuscarObjeto(v.Nome, out var obj)) return "objeto:" + obj.Classe;
                if (amb.Buscar(v.Nome, out var valor)) return TipoDoValor(valor);
                if (amb.BuscarArray(v.Nome, out var vet))
                    return vet.Length == 0 || vet[0].Caractere || amb.EhVetorDeBytes(v.Nome) ? "texto" : "vetor";
                if (funcoes.ContainsKey(v.Nome)) return "funcao";
                return "int";
            case Binaria { Op: "+" } b when TipoDoArgumento(b.Esq, amb) == "texto" || TipoDoArgumento(b.Dir, amb) == "texto":
                return "texto";
            case Binaria { Op: "/" or "*" or "+" or "-" } b when TipoDoArgumento(b.Esq, amb) == "real" || TipoDoArgumento(b.Dir, amb) == "real":
                return "real";
            case FuncaoAnonima: return "funcao";
            default: return "int";
        }
    }

    static string TipoDoValor(Valor v)
        => v.Referencia is Objeto o ? "objeto:" + o.Classe
         : v.Referencia is Valor[] ? "vetor"
         : v.EhTexto ? "texto"
         : v.Caractere ? "char"
         : v.Inteiro ? "int" : "real";

    int Pontos(Funcao f, List<string> tipos)
    {
        int soma = 0;
        for (int i = 0; i < tipos.Count && i < f.Params.Count; i++)
        {
            string par = f.Params[i].Tipo ?? "";
            string basico = TipoBasico(par);
            bool texto = basico == "String" || (par.EndsWith('*') && basico is "char" or "unsigned char" or "uint8_t");
            string arg = tipos[i];

            soma += arg switch
            {
                "texto" => texto ? 4 : basico == "char" ? 1 : -4,
                "char" => basico == "char" ? 4 : texto ? 1 : Numericos.Contains(basico) ? 2 : -2,
                "int" => Numericos.Contains(basico) && basico is not ("float" or "double") ? 3
                       : basico is "float" or "double" ? 2 : texto ? -2 : basico == "char" ? 1 : -1,
                "real" => basico is "float" or "double" ? 4 : Numericos.Contains(basico) ? 1 : -3,
                "vetor" => par.EndsWith('*') ? 3 : -2,
                "funcao" => par == "funcao*" ? 4 : -2,
                _ when arg.StartsWith("objeto:") => DerivaDe(arg[7..], basico) ? 5 : basico == "auto" ? 1 : -4,
                _ => 0,
            };
        }
        return soma;
    }

    // ---------------- chamadas pelo nome ----------------

    /// <summary>
    /// "ler()" dentro de um metodo, "Classe::contar()", "Botao(4)", a funcao
    /// do programa e a variavel que guarda uma funcao. Devolve false se o
    /// nome nao e nada disso -- e ai a chamada segue para as do simulador.
    /// </summary>
    bool ChamarDoPrograma(Variavel fn, Chamada c, Ambiente amb, out Valor resultado)
    {
        resultado = Valor.Vazio;
        string nome = fn.Nome;
        bool soGlobal = nome.StartsWith("::", StringComparison.Ordinal);
        if (soGlobal) nome = nome[2..];

        if (!soGlobal && classes.Count > 0)
        {
            // Metodo da propria classe, sem "this->". Na classe de verdade
            // do objeto: e o que faz o virtual chamar o da derivada.
            var eu = amb.ObjetoAtual();
            string minha = eu?.Classe ?? amb.ClasseAtual();
            if (minha is not null && classes.TryGetValue(minha, out var atual))
            {
                if (ProcurarMetodo(atual, nome, c.Args, amb, c.Linha) is { } metodo)
                {
                    resultado = ChamarMetodo(metodo.Estatico ? null : eu, metodo, c.Args, amb, c.Linha);
                    return true;
                }

                // "print(x)" dentro de uma classe derivada do Print: o do Print.
                if (eu is not null && atual.Imprime && nome is "print" or "println" or "printf")
                {
                    resultado = ImprimirPeloWrite(eu, atual, nome, c, amb);
                    return true;
                }
            }

            // "Classe::contar()" (static) e "Base::desenhar()" (a da base,
            // chamada de dentro da derivada).
            int corte = nome.LastIndexOf("::", StringComparison.Ordinal);
            if (corte > 0)
            {
                string dono = nome[..corte], qual = nome[(corte + 2)..];
                int corteDono = dono.LastIndexOf("::", StringComparison.Ordinal);
                if (corteDono >= 0) dono = dono[(corteDono + 2)..];

                if (classes.TryGetValue(dono, out var info))
                {
                    var f = ProcurarMetodo(info, qual, c.Args, amb, c.Linha)
                            ?? throw new ErroDeExecucao($"a classe {dono} nao tem o metodo {qual}()", c.Linha);
                    resultado = ChamarMetodo(f.Estatico ? null : amb.ObjetoAtual(), f, c.Args, amb, c.Linha);
                    return true;
                }
            }

            // "Botao(4)": o objeto montado ali mesmo, como valor.
            if (classes.TryGetValue(nome, out var construida))
            {
                resultado = Valor.DeReferencia(Construir(construida, c.Args, amb, c.Linha));
                return true;
            }
        }

        if (EhDoC && sobrecargas.TryGetValue(nome, out var lista))
        {
            // So o prototipo aqui, e o corpo no C nativo: chama o C.
            if (lista.All(x => x.Corpo is null) && TemNativa(nome, out var nativa))
            {
                resultado = ChamarNativa(nome, nativa, [.. c.Args.Select(a => Avaliar(a, amb))], c.Linha);
                return true;
            }

            var f = Escolher(lista, c.Args, amb, c.Linha, nome);
            resultado = ChamarUsuario(f, c.Args, amb, global, c.Linha);
            return true;
        }

        // Variavel que guarda uma funcao: "void (*acao)() = piscar; acao();",
        // ou o campo "_aoApertar" de um objeto.
        if (!funcoes.ContainsKey(nome) && amb.Buscar(nome, out var guardada)
            && ChamarGuardada(guardada, c, amb, out resultado))
            return true;

        return false;
    }

    /// <summary>Chama o que uma variavel de ponteiro de funcao guarda.</summary>
    bool ChamarGuardada(Valor guardada, Chamada c, Ambiente amb, out Valor resultado)
    {
        resultado = Valor.Vazio;

        if (guardada.Referencia is Rotina rotina)
        {
            var valores = c.Args.Select(a => (object)Avaliar(a, amb)).ToArray();
            resultado = ChamarRotina(rotina, valores);
            return true;
        }

        if (guardada.EhTexto && sobrecargas.TryGetValue(guardada.Texto, out var alvo))
        {
            var f = Escolher(alvo, c.Args, amb, c.Linha, guardada.Texto);
            resultado = ChamarUsuario(f, c.Args, amb, global, c.Linha);
            return true;
        }

        return false;
    }

    /// <summary>
    /// "botao.ler()" num objeto de classe do programa. Se a classe nao tem
    /// o metodo mas deriva do Print, o print() e o println() saem pelo
    /// write() dela -- e o que o Print do Arduino faz.
    /// </summary>
    Valor ChamarObjetoDeClasse(Objeto obj, ClasseInfo info, string metodo, Chamada c, Ambiente amb)
    {
        var f = ProcurarMetodo(info, metodo, c.Args, amb, c.Linha);
        if (f is not null) return ChamarMetodo(f.Estatico ? null : obj, f, c.Args, amb, c.Linha);

        if (obj.Campos.TryGetValue(metodo, out var guardada) && ChamarGuardada(guardada, c, amb, out var r))
            return r;

        if (info.Imprime && metodo is "print" or "println" or "printf" or "write")
            return ImprimirPeloWrite(obj, info, metodo, c, amb);

        var tem = Hierarquia(info).SelectMany(k => k.Metodos.Keys).Distinct().OrderBy(n => n).ToList();
        throw new ErroDeExecucao(
            $"a classe {info.Nome} nao tem o metodo {metodo}()"
            + (tem.Count > 0 ? ". Tem: " + string.Join(", ", tem.Select(n => n + "()")) : ""), c.Linha);
    }

    Valor ImprimirPeloWrite(Objeto obj, ClasseInfo info, string metodo, Chamada c, Ambiente amb)
    {
        var escrever = AcharLista(info, "write")?.FirstOrDefault(f => f.Corpo is not null && f.Params.Count == 1)
            ?? throw new ErroDeExecucao(
                $"a classe {info.Nome} deriva do Print, mas nao escreveu o write(uint8_t) que o print() usa",
                c.Linha);

        var args = c.Args.Select(a => Avaliar(a, amb)).ToList();
        string texto = metodo switch
        {
            "printf" => Formatar(args),
            "write" => args.Count > 0 ? (args[0].EhTexto ? args[0].Texto : ((char)((int)args[0].Num & 0xFF)).ToString()) : "",
            _ => Imprimivel(args),
        };
        if (metodo == "println") texto += "\r\n";

        int escritos = 0;
        foreach (byte b in TextoArduino.ParaBytes(texto))
        {
            var volta = ChamarMetodo(obj, escrever, [new Pronto(Valor.DeInt(b), c.Linha)], amb, c.Linha);
            escritos += volta.EhTexto ? 1 : (int)volta.Num;
        }
        return Valor.DeInt(escritos);
    }

    /// <summary>"a == b", "a + b" com operator== e operator+ escritos na classe.</summary>
    bool OperarObjetos(string op, Valor x, Valor y, int linha, out Valor resultado)
    {
        resultado = Valor.Vazio;

        if (x.Referencia is Objeto ox && ox.Escopo is not null && classes.TryGetValue(ox.Classe, out var info)
            && AcharLista(info, "operator" + op) is { } lista)
        {
            var args = new List<Expr> { new Pronto(y, linha) };
            var f = Escolher(lista, args, global, linha, info.Nome + "::operator" + op);
            resultado = ChamarMetodo(ox, f, args, global, linha);
            return true;
        }

        if (sobrecargas.TryGetValue("operator" + op, out var livres)
            && (x.Referencia is Objeto || y.Referencia is Objeto))
        {
            var args = new List<Expr> { new Pronto(x, linha), new Pronto(y, linha) };
            var f = Escolher(livres, args, global, linha, "operator" + op);
            resultado = ChamarUsuario(f, args, global, global, linha);
            return true;
        }

        // Ponteiros: iguais quando apontam para o mesmo objeto.
        if (op is "==" or "!=" && (x.Referencia is Objeto or Valor[] || y.Referencia is Objeto or Valor[]))
        {
            bool iguais = ReferenceEquals(x.Referencia, y.Referencia)
                          || (x.Referencia is null && y.Referencia is null && x.Num == y.Num);
            resultado = Valor.DeBool(op == "==" ? iguais : !iguais);
            return true;
        }

        return false;
    }

    /// <summary>"Classe::CONSTANTE", "Classe::contador", "Cor::VERMELHO".</summary>
    bool NomeComEscopo(string nome, Ambiente amb, out Valor valor)
    {
        valor = Valor.Vazio;
        int corte = nome.IndexOf("::", StringComparison.Ordinal);
        if (corte <= 0) return false;

        string dono = nome[..corte], resto = nome[(corte + 2)..];

        if (classes.TryGetValue(dono, out var info))
        {
            string ultimo = resto[(resto.LastIndexOf("::", StringComparison.Ordinal) + 2)..];
            foreach (var qual in new[] { resto, ultimo })
            {
                if (info.Estaticos.Buscar(qual, out valor)) return true;
                if (info.Estaticos.BuscarObjeto(qual, out var obj)) { valor = Valor.DeReferencia(obj); return true; }
            }
        }

        // "Cor::VERMELHO" de um enum class, "espaco::x": o nome sozinho.
        string sozinho = nome[(nome.LastIndexOf("::", StringComparison.Ordinal) + 2)..];
        if (amb.Buscar(sozinho, out valor)) return true;
        return false;
    }

    /// <summary>"Classe::contador = 5" de fora da classe.</summary>
    bool AtribuirComEscopo(string nome, Valor v)
    {
        int corte = nome.IndexOf("::", StringComparison.Ordinal);
        if (corte <= 0) return false;

        string dono = nome[..corte], resto = nome[(corte + 2)..];
        return classes.TryGetValue(dono, out var info) && info.Estaticos.Atribuir(resto, v);
    }

    /// <summary>
    /// "int Botao::contador = 0;" -- a definicao de um static, escrita fora
    /// da classe. O valor vai morar no escopo dos static dela.
    /// </summary>
    bool DeclararEstatico(CmdDecl d)
    {
        int corte = d.Nome.LastIndexOf("::", StringComparison.Ordinal);
        if (corte <= 0) return false;

        string dono = d.Nome[..corte];
        int corteDono = dono.LastIndexOf("::", StringComparison.Ordinal);
        if (corteDono >= 0) dono = dono[(corteDono + 2)..];
        if (!classes.TryGetValue(dono, out var info)) return false;

        ExecDecl(d with { Nome = d.Nome[(corte + 2)..] }, info.Estaticos);
        return true;
    }

    /// <summary>"for (int x : v)": um item por volta; com "&amp;", o proprio item.</summary>
    void ExecParaCadaC(CmdParaCadaC pc, Ambiente amb)
    {
        Valor[] vetor = null;
        string texto = null;

        // "for (auto &plano : cubo)": vetor de varias dimensoes, pedaco a pedaco.
        if (DimensoesDaSequencia(pc.Sequencia, amb) is { } comDimensoes)
        {
            PercorrerDimensoes(pc, comDimensoes, amb);
            return;
        }

        if (VetorDoArgumento(pc.Sequencia, amb, out var achado)) vetor = achado;
        else
        {
            var sequencia = Avaliar(pc.Sequencia, amb);
            if (sequencia.Referencia is Valor[] devolvido) vetor = devolvido;
            else if (sequencia.EhTexto) texto = sequencia.Texto;
            else
                throw new ErroDeExecucao(
                    "o for (x : lista) percorre um vetor ou uma String, e isto nao e nenhum dos dois",
                    pc.Linha);
        }

        int quantos = vetor?.Length ?? texto.Length;
        for (int i = 0; i < quantos; i++)
        {
            if (maq.Parar) throw maq.MotivoDaParada();

            var escopo = new Ambiente(amb);
            if (vetor is not null && pc.PorReferencia)
            {
                int k = i;
                escopo.Declarar(pc.Nome, Valor.DeReferencia(new Apelido(
                    () => vetor[k],
                    novo => vetor[k] = vetor[k].Caractere ? Valor.ComoCaractere(novo) : novo)));
            }
            else
            {
                var item = vetor is not null ? vetor[i] : Valor.DeCaractere(texto[i]);
                if (EhCaractere(pc.Tipo)) escopo.DeclararCaractere(pc.Nome, item);
                else escopo.Declarar(pc.Nome, Coagir(item, pc.Tipo));
            }

            Exec(pc.Corpo, escopo);
            if (SaiuDoLaco()) break;
        }
    }

    // ---------------- membros ----------------

    /// <summary>
    /// "obj.campo", "sensor->leitura", "this->buffer", "lista[2].nome":
    /// o campo, o objeto de dentro ou o vetor de dentro de um objeto de
    /// classe. False quando o dono nao e um objeto de classe do programa
    /// -- e ai a leitura segue o caminho de sempre.
    /// </summary>
    bool LerMembroDeClasse(Membro m, Ambiente amb, out Valor valor)
    {
        valor = Valor.Vazio;
        if (classes.Count == 0 && m.Obj is Variavel) return false;

        var dono = ObjetoDaExpressao(m.Obj, amb);
        if (dono is null || dono.Escopo is null)
        {
            // struct devolvida por funcao ou lida de vetor: "pontos[1].x"
            if (dono is not null && dono.Campos.TryGetValue(m.Nome, out var campoSimples))
            {
                valor = campoSimples;
                return true;
            }
            return false;
        }

        if (dono.Escopo.BuscarLocal(m.Nome, out valor)) return true;

        if (dono.Escopo.BuscarObjetoLocal(m.Nome, out var dentro))
        {
            valor = Valor.DeReferencia(dentro);
            return true;
        }

        if (dono.Escopo.BuscarArrayLocal(m.Nome, out var vetor))
        {
            valor = vetor.Length == 0 || vetor[0].Caractere || dono.Escopo.EhVetorDeBytes(m.Nome)
                ? Valor.DeTexto(TextoDoVetor(vetor))
                : Valor.DeReferencia(vetor);
            return true;
        }

        // Um static lido pelo objeto: "botao.contador".
        if (classes.TryGetValue(dono.Classe, out var info) && info.Estaticos.Buscar(m.Nome, out valor))
            return true;

        var tem = dono.Campos.Keys.OrderBy(k => k).ToList();
        throw new ErroDeExecucao(
            $"o objeto {dono.Classe} nao tem o campo '{m.Nome}'"
            + (tem.Count > 0 ? ". Tem: " + string.Join(", ", tem) : ""), m.Linha);
    }

    bool GuardarMembroDeClasse(Membro m, Valor v, Ambiente amb)
    {
        if (classes.Count == 0) return false;

        var dono = ObjetoDaExpressao(m.Obj, amb);
        if (dono?.Escopo is null) return false;

        if (dono.Campos.ContainsKey(m.Nome))
        {
            dono.Escopo.Atribuir(m.Nome, v);
            return true;
        }

        if (dono.Escopo.BuscarObjetoLocal(m.Nome, out var dentro) && dentro.Escopo is not null
            && v.Referencia is Objeto fonte && fonte.Escopo is not null)
        {
            fonte.Escopo.CopiarConteudo(dentro.Escopo, Copiar);
            return true;
        }

        if (classes.TryGetValue(dono.Classe, out var info) && info.Estaticos.Atribuir(m.Nome, v)) return true;

        throw new ErroDeExecucao($"o objeto {dono.Classe} nao tem o campo '{m.Nome}'", m.Linha);
    }

    /// <summary>O vetor de dentro de um objeto: "this->buffer", "fila.itens".</summary>
    Valor[] VetorDoMembro(Membro m, Ambiente amb)
    {
        var dono = ObjetoDaExpressao(m.Obj, amb);
        if (dono?.Escopo is not null && dono.Escopo.BuscarArrayLocal(m.Nome, out var vetor)) return vetor;
        if (dono is not null && dono.Campos.TryGetValue(m.Nome, out var campo) && campo.Referencia is Valor[] guardado)
            return guardado;
        return null;
    }

    /// <summary>"lista[3]" com operator[] escrito na classe.</summary>
    bool IndiceDeClasse(Indice ix, Ambiente amb, out Valor valor)
    {
        valor = Valor.Vazio;
        if (classes.Count == 0) return false;

        var dono = ObjetoDaExpressao(ix.Alvo, amb);
        if (dono?.Escopo is null || !classes.TryGetValue(dono.Classe, out var info)) return false;
        if (AcharLista(info, "operator[]") is not { } lista) return false;

        var args = new List<Expr> { ix.Idx };
        valor = ChamarMetodo(dono, Escolher(lista, args, amb, ix.Linha, info.Nome + "::operator[]"), args, amb, ix.Linha);
        return true;
    }
}
