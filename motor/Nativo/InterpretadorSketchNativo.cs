namespace Simulador;

/// <summary>
/// O sketch compilado (CompiladorNativo) dentro do interpretador da placa.
///
/// O interpretador continua sendo quem a janela, o depurador e os testes
/// conhecem; quando o programa veio compilado, o setup() e o loop() sao os da
/// DLL, e o interpretador vira o "core" dela: cada pinMode, delay ou
/// Serial.print que o C++ chama cai aqui (ChamarDoNativo), na mesma
/// implementacao das funcoes do caminho interpretado.
/// </summary>
public partial class Interpretador
{
    ISketchCompilado sketchNativo;

    /// <summary>O programa veio compilado de verdade (e roda nativo).</summary>
    public bool RodaNativo => prog.Compilado is not null;

    /// <summary>Carrega a DLL desta placa (uma vez por rodada), na thread da placa.</summary>
    void AbrirSketchNativo()
    {
        if (sketchNativo is not null) return;
        maq.Sketch?.Encerrar();
        // O C# (Roslyn) roda no .NET, na thread da placa; o C/C++ e a DLL.
        if (prog.Compilado is ProgramaDotNet dotnet)
        {
            var programa = new SketchDotNet(maq, ChamarDoNativo, AtenderInterrupcoes);
            sketchNativo = programa;
            maq.Sketch = programa;
            programa.Carregar(dotnet);
            return;
        }
        var nativo = new SketchNativo(maq, ChamarDoNativo, AtenderInterrupcoes, () => maq.Interrupcoes.Count > 0);
        sketchNativo = nativo;
        maq.Sketch = nativo;
        nativo.Carregar((SketchCompilado)prog.Compilado, Origem);
    }

    /// <summary>
    /// Uma chamada do C++ ao core: vira a mesma chamada que o sketch
    /// interpretado faria ("delay(500)", "Serial.print(...)"), com os valores
    /// prontos no lugar das expressoes.
    /// </summary>
    Valor ChamarDoNativo(string alvo, string nome, List<Valor> args, List<bool> nomes)
    {
        // O objeto de uma biblioteca nasce como nasceria no sketch interpretado
        // ("Servo direcao;"), com um nome so dele, e o nome volta para o C++.
        if (alvo is null && nome == "__criar")
        {
            string novo = $"__obj{++objetosNativos}";
            var argsDoConstrutor = new List<Expr>();
            for (int i = 1; i < args.Count; i++)
                argsDoConstrutor.Add(nomes[i] ? new Variavel(args[i].Texto, 0) : new Pronto(args[i], 0));
            Exec(new CmdDeclObj(args[0].Texto, novo, argsDoConstrutor, 0), global);
            return Valor.DeTexto(novo);
        }
        // "__ler(objeto, "acceleration.x")": um campo do objeto (o evento do
        // sensor que o getEvent preencheu).
        if (alvo is null && nome == "__ler" && args.Count >= 2)
        {
            // "messages.0.text": o numero e o indice do vetor.
            Expr caminho = new Variavel(args[0].Texto, 0);
            foreach (string parte in args[1].Texto.Split('.'))
                caminho = int.TryParse(parte, out int indice)
                    ? new Indice(caminho, new Literal(indice, null, true, false, 0), 0)
                    : new Membro(caminho, parte, 0);
            var lido = Avaliar(caminho, global);
            return lido.Referencia is Objeto dentro ? Valor.DeTexto(NomeDoObjeto(dentro)) : lido;
        }

        // Os callbacks de um objeto do C++ (a classe que deriva de
        // BLECharacteristicCallbacks): um objeto daqui que o simulador chama
        // como chamaria o do sketch interpretado (ver ChamarCallbackNativo).
        if (alvo is null && nome == "__callbacks" && args.Count >= 1)
        {
            var cb = new Objeto(ClasseDeCallbackNativo);
            cb.Campos["_indice"] = args[0];
            return Valor.DeTexto(NomeDoObjeto(cb));
        }
        if (alvo is null && nome == "__destruir")
        {
            if (args.Count > 0) global.TirarObjeto(args[0].Texto, out _);
            return Valor.Vazio;
        }

        var exprs = new List<Expr>(args.Count);
        for (int i = 0; i < args.Count; i++)
        {
            // A funcao do sketch passada para a biblioteca: ela existe para o
            // interpretador, que a acha pelo nome e a chama de volta.
            if (nomes[i] && RotinaNativa(args[i].Texto, out _) && !funcoes.ContainsKey(args[i].Texto))
                funcoes[args[i].Texto] = new Funcao("void", args[i].Texto, [], new CmdBloco([], 0), 0);
            exprs.Add(nomes[i] ? new Variavel(args[i].Texto, 0) : new Pronto(args[i], 0));
        }
        Expr funcao = alvo is null ? new Variavel(nome, 0) : new Membro(new Variavel(alvo, 0), nome, 0);
        var volta = Avaliar(new Chamada(funcao, exprs, 0), global);

        // Um objeto devolvido (o DateTime do RTC.now()): ganha um nome, que o
        // C++ usa para chamar os metodos dele (e que ele solta com __destruir
        // quando o objeto era so de passagem, como o DateTime).
        if (volta.Referencia is Objeto devolvido) return Valor.DeTexto(NomeDoObjeto(devolvido));
        return volta;
    }

    /// <summary>
    /// O nome de um objeto do simulador para o C++ -- sempre o mesmo para o
    /// mesmo objeto (o servidor BLE que volta de dois lugares e um so).
    /// </summary>
    string NomeDoObjeto(Objeto o)
    {
        if (nomesDosObjetos.TryGetValue(o, out string nome) && global.BuscarObjeto(nome, out var ainda) && ReferenceEquals(ainda, o))
            return nome;
        nome = $"__volta{objetosDevolvidos++}";
        global.DeclararObjeto(nome, o);
        nomesDosObjetos[o] = nome;
        return nome;
    }

    readonly Dictionary<Objeto, string> nomesDosObjetos = new(ReferenceEqualityComparer.Instance);

    const string ClasseDeCallbackNativo = "__callbacks_do_cpp";

    /// <summary>
    /// O simulador chamando um metodo do objeto de callbacks do C++: o nome do
    /// metodo e os argumentos (objetos pelo nome) vao para o despachante dele.
    /// </summary>
    bool ChamarCallbackNativo(Objeto cb, string metodo, params Valor[] args)
    {
        if (cb?.Classe != ClasseDeCallbackNativo || sketchNativo is null) return false;
        var lista = new List<object> { Valor.DeTexto(metodo) };
        foreach (var a in args) lista.Add(ParaONativo(a));
        sketchNativo.Rotina((int)cb.Campos["_indice"].Num, [.. lista]);
        return true;
    }

    /// <summary>O argumento de um callback do C++: o objeto vai pelo nome, o vetor pelos bytes.</summary>
    object ParaONativo(object a) => a switch
    {
        Valor { Referencia: Objeto o } => Valor.DeTexto(NomeDoObjeto(o)),
        Valor { Referencia: Valor[] v } => v,
        _ => a,
    };

    int objetosNativos, objetosDevolvidos;


    /// <summary>"__nativa_3": a rotina de interrupcao 3 da DLL.</summary>
    static bool RotinaNativa(string nome, out int indice)
    {
        indice = -1;
        return nome is not null && nome.StartsWith("__nativa_", StringComparison.Ordinal)
               && int.TryParse(nome.AsSpan(9), out indice);
    }

    /// <summary>
    /// A constante do core com o numero que o simulador usa ("RISING" e 1):
    /// o cabecalho da placa do C++ compilado define as mesmas.
    /// </summary>
    public static string ConstanteDoCore(string nome)
        => Constantes.TryGetValue(nome, out var v) && !v.EhTexto ? ((long)v.Num).ToString() : null;
}
