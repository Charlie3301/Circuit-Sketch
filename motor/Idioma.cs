using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace Simulador;

/// <summary>
/// O idioma da interface e das mensagens (Alpha 25).
///
/// O texto original e o portugues que esta no codigo: ele mesmo e a chave.
/// <c>Idioma.T("Salvar projeto")</c> devolve a traducao do idioma escolhido,
/// ou o proprio portugues quando o catalogo ainda nao tem aquela frase -- a
/// migracao anda arquivo por arquivo e nada quebra no meio.
///
/// Frase com numero ou nome dentro vai como formato, e nao como texto
/// interpolado (a interpolacao muda a chave a cada valor):
/// <c>Idioma.T("{0} peças, {1} fios", pecas, fios)</c>. O numero sai na
/// cultura do idioma (virgula no portugues, ponto no ingles).
///
/// Os catalogos ficam em <c>motor/idiomas/&lt;codigo&gt;.json</c> (embutidos
/// no motor) e sao mantidos pelo <c>ferramentas/idiomas.py</c>, que acha as
/// frases novas no codigo e mostra o que falta traduzir.
/// </summary>
public static class Idioma
{
    /// <summary>O idioma em que o codigo esta escrito.</summary>
    public const string Original = "pt-BR";

    static Dictionary<string, string> textos = [];
    static CultureInfo cultura = CultureInfo.GetCultureInfo(Original);

    /// <summary>O codigo do idioma em uso ("pt-BR", "en"...).</summary>
    public static string Atual { get; private set; } = Original;

    /// <summary>A cultura dos numeros e das datas do idioma em uso.</summary>
    public static CultureInfo Cultura => cultura;

    /// <summary>
    /// Um catalogo: o nome do idioma escrito nele mesmo, a cultura, as frases,
    /// e se ele esta completo (o ferramentas/idiomas.py marca quando nao falta
    /// nenhuma frase do codigo).
    /// </summary>
    sealed record Catalogo(string Codigo, string Nome, string Cultura, Dictionary<string, string> Textos, bool Completo);

    /// <summary>
    /// Os idiomas que existem: o original e os que tem catalogo, com o nome de
    /// cada um. A janela de configuracoes so oferece os completos -- metade da
    /// tela em cada lingua e pior do que nenhuma traducao.
    /// </summary>
    public static IReadOnlyList<(string Codigo, string Nome)> Disponiveis(bool soCompletos = false)
    {
        var lista = new List<(string, string)> { (Original, "Português (Brasil)") };
        foreach (string recurso in typeof(Idioma).Assembly.GetManifestResourceNames())
        {
            if (!recurso.StartsWith("idiomas/", StringComparison.Ordinal)) continue;
            if (Ler(recurso) is { } c && (c.Completo || !soCompletos)) lista.Add((c.Codigo, c.Nome));
        }
        return lista;
    }

    /// <summary>
    /// Passa a usar este idioma. Sem catalogo para ele, fica no original.
    /// Chamado uma vez, ao abrir o programa: a janela ja montada nao troca
    /// de texto sozinha (o idioma novo vale ao reabrir).
    /// </summary>
    /// <returns>Se o idioma pedido foi encontrado.</returns>
    public static bool Usar(string codigo)
    {
        Catalogo catalogo = null;
        if (!string.IsNullOrEmpty(codigo) && codigo != Original)
            catalogo = Ler($"idiomas/{codigo}.json");

        textos = catalogo?.Textos ?? [];
        Atual = catalogo?.Codigo ?? Original;
        try { cultura = CultureInfo.GetCultureInfo(catalogo?.Cultura ?? Original); }
        catch (CultureNotFoundException) { cultura = CultureInfo.InvariantCulture; }

        CultureInfo.DefaultThreadCurrentCulture = cultura;
        CultureInfo.DefaultThreadCurrentUICulture = cultura;
        CultureInfo.CurrentCulture = cultura;
        CultureInfo.CurrentUICulture = cultura;
        return catalogo is not null || codigo is null or "" or Original;
    }

    /// <summary>
    /// Quanto do idioma esta traduzido, de 0 a 1 (o original e 1). A janela de
    /// configuracoes mostra o numero ao lado do nome enquanto a traducao anda.
    /// </summary>
    public static double Progresso(string codigo)
    {
        if (string.IsNullOrEmpty(codigo) || codigo == Original) return 1;
        var catalogo = Ler($"idiomas/{codigo}.json");
        if (catalogo is null) return 0;
        if (catalogo.Completo || catalogo.Textos.Count == 0) return catalogo.Completo ? 1 : 0;
        return catalogo.Textos.Values.Count(t => !string.IsNullOrEmpty(t)) / (double)catalogo.Textos.Count;
    }

    /// <summary>O idioma do Windows, se houver catalogo para ele (senao, o original).</summary>
    public static string DoSistema()
    {
        var sistema = CultureInfo.InstalledUICulture;
        var codigos = Disponiveis().Select(d => d.Codigo).ToList();
        return codigos.FirstOrDefault(c => c.Equals(sistema.Name, StringComparison.OrdinalIgnoreCase))
               ?? codigos.FirstOrDefault(c => c.Equals(sistema.TwoLetterISOLanguageName, StringComparison.OrdinalIgnoreCase))
               ?? Original;
    }

    /// <summary>A frase no idioma em uso (o portugues, se ainda nao foi traduzida).</summary>
    public static string T(string texto)
        => texto is not null && textos.TryGetValue(texto, out string traducao) && !string.IsNullOrEmpty(traducao)
            ? traducao : texto;

    /// <summary>
    /// Marca a frase para o catalogo SEM traduzir agora: para tabelas de dados
    /// cujo texto so vira tela depois (o nome de um braco de servo, de uma cor),
    /// e que la e passado por <see cref="T(string)"/>. Devolve a propria frase.
    /// </summary>
    public static string N(string texto) => texto;

    /// <summary>A frase com valores dentro ({0}, {1:0.0}), na cultura do idioma.</summary>
    public static string T(string formato, params object[] valores) => string.Format(cultura, T(formato), valores);

    static Catalogo Ler(string recurso)
    {
        using var fluxo = typeof(Idioma).Assembly.GetManifestResourceStream(recurso);
        if (fluxo is null) return null;
        try
        {
            using var doc = JsonDocument.Parse(fluxo);
            var raiz = doc.RootElement;
            var frases = new Dictionary<string, string>(StringComparer.Ordinal);
            if (raiz.TryGetProperty("textos", out var t) && t.ValueKind == JsonValueKind.Object)
                foreach (var p in t.EnumerateObject())
                    if (p.Value.ValueKind == JsonValueKind.String) frases[p.Name] = p.Value.GetString();
            return new Catalogo(raiz.GetProperty("codigo").GetString(), raiz.GetProperty("nome").GetString(),
                                raiz.TryGetProperty("cultura", out var c) ? c.GetString() : null, frases,
                                raiz.TryGetProperty("completo", out var k) && k.ValueKind == JsonValueKind.True);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return null;   // catalogo com defeito: fica no original, e a ferramenta acusa
        }
    }
}
