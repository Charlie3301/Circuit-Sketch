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
///   public static string TipoImportado(string tipo, Func&lt;string, bool&gt; existe)
///       -- o tipo de peca de um diagrama de fora, no nome daqui (ou null);
///          o "existe" diz se um nome esta no catalogo.
///
/// Os ganchos sao achados pelo nome e pela assinatura: o plugin so compila contra
/// o .NET, sem depender de nada do programa -- e um plugin que nao compila, ou que
/// da erro, aparece com o erro na lista e nao atrapalha o resto. Cada um liga e
/// desliga nas Configuracoes (a preferencia "plugin.&lt;pasta&gt;"; vem ligado).
/// </summary>
public static class Plugins
{
    public sealed class Plugin
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

        internal Func<string, Func<string, bool>, string> TipoImportado { get; init; }

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
    public static string TipoImportado(string tipo)
    {
        if (string.IsNullOrEmpty(tipo)) return null;
        static bool Existe(string t) => !string.IsNullOrEmpty(t) && Catalogo.Obter(t) is not null;
        foreach (var plugin in Todos)
        {
            if (!plugin.Ligado || plugin.TipoImportado is null) continue;
            string nosso;
            try { nosso = plugin.TipoImportado(tipo, Existe); }
            catch { continue; }     // um plugin com defeito nao derruba a importacao
            if (Existe(nosso)) return nosso;
        }
        return null;
    }

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
            lista.Add(new Plugin
            {
                Id = plugin.Id, Nome = plugin.Nome, Descricao = plugin.Descricao, Versao = plugin.Versao,
                Categoria = plugin.Categoria, Autor = plugin.Autor, Erro = plugin.Erro,
                TipoImportado = plugin.TipoImportado, Arquivos = arquivos,
            });
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
            return new Plugin { Id = id, Nome = id, Categoria = "Outros", Erro = "não compilou: " + string.Join(" | ", erros) };
        }

        ms.Position = 0;
        var montagem = new AssemblyLoadContext("plugin." + id).LoadFromStream(ms);
        foreach (var tipo in montagem.GetTypes())
        {
            const BindingFlags Publico = BindingFlags.Public | BindingFlags.Static;
            if (tipo.GetField("Nome", Publico)?.GetValue(null) is not string nome) continue;
            string Constante(string campo) => tipo.GetField(campo, Publico)?.GetValue(null) as string ?? "";

            Func<string, Func<string, bool>, string> importado = null;
            if (tipo.GetMethod("TipoImportado", Publico, [typeof(string), typeof(Func<string, bool>)]) is { } metodo
                && metodo.ReturnType == typeof(string))
                importado = metodo.CreateDelegate<Func<string, Func<string, bool>, string>>();

            return new Plugin
            {
                Id = id, Nome = nome, Descricao = Constante("Descricao"), Versao = Constante("Versao"),
                Categoria = Constante("Categoria") is { Length: > 0 } categoria ? categoria : "Outros",
                Autor = Constante("Autor"), TipoImportado = importado,
            };
        }
        return new Plugin { Id = id, Nome = id, Categoria = "Outros", Erro = "nenhuma classe com \"public const string Nome\"" };
    }
}
