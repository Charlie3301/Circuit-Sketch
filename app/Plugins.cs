using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Simulador.App;

/// <summary>
/// Os plugins: cada pasta dentro de "plugins" (ao lado do executavel) e um plugin,
/// com um ou mais .cs que o Roslyn compila quando o programa abre.
///
/// O plugin e uma classe estatica com as constantes que o descrevem e os ganchos que quiser:
///
///   public const string Nome, Descricao, Versao, Categoria, Autor;   (so o Nome e obrigatorio)
///
/// Para abrir um diagrama de fora:
///   public static string TipoImportado(string tipo, IReadOnlyDictionary&lt;string, string&gt; atributos, Func&lt;string, bool&gt; existe)
///       -- o tipo da peca no nome daqui (ou null); o "existe" diz se um nome esta no
///          catalogo. (Tambem vale sem os atributos: TipoImportado(tipo, existe).)
///   public static string PinoImportado(string tipo, string pino, string[] pinos)
///       -- o pino no nome daqui (pinos: os da peca daqui), ou null.
///
/// Para exportar no formato de fora (o "Exportar" ganha a opcao "para o &lt;Formato&gt;"):
///   public const string Formato;
///   public static string TipoExportado(string tipo)
///   public static string PinoExportado(string tipo, string pino)              (null: o mesmo nome)
///   public static Dictionary&lt;string, string&gt; AtributosExportados(string tipo) (o que vai a mais nos attrs)
///
/// Os ganchos sao achados pelo nome e pela assinatura: o plugin so compila contra
/// o .NET, sem depender de nada do programa -- e um plugin que nao compila, ou que
/// da erro, aparece com o erro na lista e nao atrapalha o resto. Cada um liga e
/// desliga nas Configuracoes (a preferencia "plugin.&lt;pasta&gt;"; vem ligado).
/// </summary>
public static class Plugins
{
    public sealed record Plugin
    {
        /// <summary>O nome da pasta: e por ele que a preferencia guarda se esta ligado.</summary>
        public string Id { get; init; }
        public string Nome { get; init; }
        public string Descricao { get; init; }
        public string Versao { get; init; }

        /// <summary>Para filtrar a lista ("Importação", "Componentes", ...); "Outros" quando nao diz.</summary>
        public string Categoria { get; init; }
        public string Autor { get; init; }

        /// <summary>Os arquivos do plugin, relativos a pasta dos plugins.</summary>
        public IReadOnlyList<string> Arquivos { get; init; } = [];

        /// <summary>Por que nao carregou (nao compilou, nao tem a classe); null quando carregou.</summary>
        public string Erro { get; init; }

        internal Func<string, IReadOnlyDictionary<string, string>, Func<string, bool>, string> TipoImportado { get; init; }
        internal Func<string, string, string[], string> PinoImportado { get; init; }

        /// <summary>O nome do formato que o plugin exporta ("Wokwi"); null quando nao exporta.</summary>
        public string Formato { get; init; }
        internal Func<string, string> TipoExportado { get; init; }
        internal Func<string, string, string> PinoExportado { get; init; }
        internal Func<string, Dictionary<string, string>> AtributosExportados { get; init; }

        public bool Ligado => Erro is null && Preferencias.Ligado(Chave(Id), true);

        /// <summary>
        /// Quando apareceu aqui: a versao do programa, o dia e a versao do plugin daquela vez
        /// (nulo antes do <see cref="RegistrarInstalados"/> ver o plugin pela primeira vez).
        /// </summary>
        public (string Programa, string Dia, string Versao)? Instalado
        {
            get
            {
                var partes = Preferencias.Texto(Chave(Id) + ".instalado").Split('|');
                return partes.Length == 3 ? (partes[0], partes[1], partes[2]) : null;
            }
        }
    }

    /// <summary>A versao do programa, como a pessoa le ("Alpha 24").</summary>
    public static string VersaoDoPrograma => JanelaAbertura.Versao();

    /// <summary>
    /// Anota o dia e a versao do programa em que cada plugin apareceu (na primeira vez que
    /// e visto). Da thread da janela: as preferencias nao sao de varias threads.
    /// </summary>
    public static void RegistrarInstalados()
    {
        foreach (var plugin in Todos)
            if (plugin.Erro is null && plugin.Instalado is null)
                Preferencias.Definir(Chave(plugin.Id) + ".instalado",
                    $"{VersaoDoPrograma}|{DateTime.Now:dd/MM/yyyy}|{plugin.Versao}");
    }

    /// <summary>As categorias dos plugins da pasta, em ordem.</summary>
    public static IReadOnlyList<string> Categorias
        => [.. Todos.Select(p => p.Categoria).Distinct().Order(StringComparer.CurrentCultureIgnoreCase)];

    static string Chave(string id) => "plugin." + id;

    /// <summary>A pasta dos plugins, ao lado do executavel.</summary>
    public static string Pasta => Path.Combine(AppContext.BaseDirectory, "plugins");

    static readonly Lazy<IReadOnlyList<Plugin>> todos = new(Carregar, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>Os plugins da pasta, ligados ou nao, na ordem do nome da pasta.</summary>
    public static IReadOnlyList<Plugin> Todos => todos.Value;

    /// <summary>Compila os plugins em segundo plano, para a primeira importacao nao esperar o Roslyn.</summary>
    public static void AquecerEmSegundoPlano(Action depois = null)
        => Task.Run(() =>
        {
            Thread.CurrentThread.Priority = ThreadPriority.Lowest;
            _ = todos.Value;
            depois?.Invoke();
        });

    public static void Ligar(string id, bool ligado) => Preferencias.Definir(Chave(id), ligado);

    /// <summary>
    /// O tipo daqui de uma peca que veio de um diagrama de fora, pelo primeiro plugin
    /// ligado que souber -- e so se a peca existe no catalogo. Null quando nenhum sabe.
    /// </summary>
    public static string TipoImportado(string tipo, IReadOnlyDictionary<string, string> atributos = null)
    {
        if (string.IsNullOrEmpty(tipo)) return null;
        static bool Existe(string t) => !string.IsNullOrEmpty(t) && Catalogo.Obter(t) is not null;
        foreach (var plugin in Todos)
        {
            if (!plugin.Ligado || plugin.TipoImportado is null) continue;
            string nosso;
            try { nosso = plugin.TipoImportado(tipo, atributos ?? new Dictionary<string, string>(), Existe); }
            catch { continue; }     // um plugin com defeito nao derruba a importacao
            if (Existe(nosso)) return nosso;
        }
        return null;
    }

    /// <summary>O pino daqui de um pino de um diagrama de fora (pinos: os da peca daqui); null quando nenhum plugin sabe.</summary>
    public static string PinoImportado(string tipo, string pino, string[] pinos)
    {
        foreach (var plugin in Todos)
        {
            if (!plugin.Ligado || plugin.PinoImportado is null) continue;
            string nosso;
            try { nosso = plugin.PinoImportado(tipo, pino, pinos); }
            catch { continue; }
            if (nosso is not null && Array.IndexOf(pinos, nosso) >= 0) return nosso;
        }
        return null;
    }

    /// <summary>Os plugins ligados que exportam para um formato de fora (o "Exportar" oferece cada um).</summary>
    public static IReadOnlyList<Plugin> Exportadores
        => [.. Todos.Where(p => p.Ligado && p.TipoExportado is not null)];

    static IReadOnlyList<Plugin> Carregar()
    {
        var lista = new List<Plugin>();
        if (!Directory.Exists(Pasta)) return lista;
        foreach (string pasta in Directory.GetDirectories(Pasta).Order(StringComparer.OrdinalIgnoreCase))
        {
            var fontes = Directory.GetFiles(pasta, "*.cs", SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToList();
            if (fontes.Count == 0) continue;
            string id = Path.GetFileName(pasta);
            var arquivos = fontes.Select(f => Path.GetRelativePath(Pasta, f)).ToList();
            Plugin plugin;
            try { plugin = Compilar(id, [.. fontes.Select(f => (f, File.ReadAllText(f)))]); }
            catch (Exception e) { plugin = new Plugin { Id = id, Nome = id, Categoria = "Outros", Erro = e.Message }; }
            lista.Add(plugin with { Arquivos = arquivos });
        }
        return lista;
    }

    /// <summary>Compila as fontes de um plugin e acha a classe dele (internal para o autoteste).</summary>
    internal static Plugin Compilar(string id, IReadOnlyList<(string Arquivo, string Texto)> fontes)
    {
        var analise = new CSharpParseOptions(LanguageVersion.Latest);
        var arvores = fontes.Select(f => CSharpSyntaxTree.ParseText(f.Texto, analise, f.Arquivo, System.Text.Encoding.UTF8));
        var comp = CSharpCompilation.Create("CircuitSketch.Plugin." + id, arvores, CompiladorCSharp.ReferenciasDoNet,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release,
                                         nullableContextOptions: NullableContextOptions.Disable));
        using var ms = new MemoryStream();
        var r = comp.Emit(ms);
        if (!r.Success)
        {
            var erros = r.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Take(3)
                         .Select(d => $"{Path.GetFileName(d.Location.SourceTree?.FilePath ?? "")}"
                                      + $"({d.Location.GetLineSpan().StartLinePosition.Line + 1}): {d.GetMessage()}");
            return new Plugin { Id = id, Nome = id, Categoria = "Outros", Erro = Idioma.T("não compilou: {0}", string.Join(" | ", erros)) };
        }

        ms.Position = 0;
        var montagem = new AssemblyLoadContext("plugin." + id).LoadFromStream(ms);
        foreach (var tipo in montagem.GetTypes())
        {
            const BindingFlags Publico = BindingFlags.Public | BindingFlags.Static;
            if (tipo.GetField("Nome", Publico)?.GetValue(null) is not string nome) continue;
            string Constante(string campo) => tipo.GetField(campo, Publico)?.GetValue(null) as string ?? "";

            // Os ganchos, pelo nome e pela assinatura (o que retorna tambem conta).
            T Gancho<T>(string nomeDoMetodo, params Type[] parametros) where T : Delegate
                => tipo.GetMethod(nomeDoMetodo, Publico, parametros) is { } m
                   && m.ReturnType == typeof(T).GetMethod("Invoke")!.ReturnType
                    ? m.CreateDelegate<T>() : null;

            var importado = Gancho<Func<string, IReadOnlyDictionary<string, string>, Func<string, bool>, string>>(
                "TipoImportado", typeof(string), typeof(IReadOnlyDictionary<string, string>), typeof(Func<string, bool>));
            if (importado is null
                && Gancho<Func<string, Func<string, bool>, string>>("TipoImportado", typeof(string), typeof(Func<string, bool>)) is { } curto)
                importado = (t, _, existe) => curto(t, existe);

            var exporta = Gancho<Func<string, string>>("TipoExportado", typeof(string));
            return new Plugin
            {
                Id = id, Nome = nome, Descricao = Constante("Descricao"), Versao = Constante("Versao"),
                Categoria = Constante("Categoria") is { Length: > 0 } categoria ? categoria : "Outros",
                Autor = Constante("Autor"),
                TipoImportado = importado,
                PinoImportado = Gancho<Func<string, string, string[], string>>("PinoImportado", typeof(string), typeof(string), typeof(string[])),
                TipoExportado = exporta,
                PinoExportado = Gancho<Func<string, string, string>>("PinoExportado", typeof(string), typeof(string)),
                AtributosExportados = Gancho<Func<string, Dictionary<string, string>>>("AtributosExportados", typeof(string)),
                Formato = exporta is null ? null : Constante("Formato") is { Length: > 0 } formato ? formato : nome,
            };
        }
        return new Plugin { Id = id, Nome = id, Categoria = "Outros", Erro = Idioma.T("nenhuma classe com \"public const string Nome\"") };
    }
}
