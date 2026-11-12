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
        lock (voltas) { voltas.Clear(); moldes = null; }
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

    /// <summary>
    /// O idioma do Windows, se houver catalogo para ele (senao, o original).
    /// Sobe pela cultura mae: o Windows em zh-TW cai no zh-Hant, o zh-CN no
    /// zh-Hans, o es-MX no es.
    /// </summary>
    public static string DoSistema()
    {
        var codigos = Disponiveis().Select(d => d.Codigo).ToList();
        for (var c = CultureInfo.InstalledUICulture; !string.IsNullOrEmpty(c.Name); c = c.Parent)
            if (codigos.FirstOrDefault(d => d.Equals(c.Name, StringComparison.OrdinalIgnoreCase)) is { } achado)
                return achado;
        return Original;
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

    /// <summary>
    /// O caminho de volta: uma frase ja traduzida (com os valores dentro) de
    /// novo no portugues do codigo. Para quem decide pelo texto de uma
    /// mensagem -- o tipo da excecao do Python sai das palavras do erro -- e
    /// assim continua decidindo igual em qualquer idioma. Frase que nao veio
    /// do catalogo volta como esta.
    /// </summary>
    public static string Desfazer(string texto) => Desfazer(texto, 0);

    static string Desfazer(string texto, int fundo)
    {
        if (textos.Count == 0 || string.IsNullOrEmpty(texto) || fundo > 3) return texto;
        lock (voltas)
        {
            if (voltas.TryGetValue(texto, out string pronto)) return pronto;
            moldes ??= Moldes();
        }
        string resultado = texto;
        if (exatas.TryGetValue(texto, out string original)) resultado = original;
        else
        {
            string reserva = null;
            foreach (var molde in moldes)
            {
                var valores = new string[molde.Valores];
                if (!Casar(molde, texto, valores)) continue;
                var desfeitos = valores.Select(v => (object)Desfazer(v ?? "", fundo + 1)).ToArray();
                string montado;
                try { montado = string.Format(CultureInfo.InvariantCulture, molde.Original, desfeitos); }
                catch (FormatException) { continue; }
                // Valor com espaco na ponta e sinal de molde que engoliu o espaco
                // de outra frase ("GPIO {0}" casando com "GPIO 2, 4 "): fica de
                // reserva enquanto um molde mais justo ainda pode aparecer.
                if (valores.Any(v => v is { Length: > 0 } && (char.IsWhiteSpace(v[0]) || char.IsWhiteSpace(v[^1]))))
                {
                    reserva ??= montado;
                    continue;
                }
                resultado = montado;
                reserva = null;
                break;
            }
            if (reserva is not null) resultado = reserva;
        }
        lock (voltas)
        {
            if (voltas.Count > 4096) voltas.Clear();
            voltas[texto] = resultado;
        }
        return resultado;
    }

    /// <summary>
    /// A traducao em pedacos: o texto fixo entre as marcas (Pedacos tem um a
    /// mais que Marcas) e o numero de cada marca, na ordem em que aparecem.
    /// </summary>
    sealed record Molde(string[] Pedacos, int[] Marcas, int Valores, string Original, int Fixo);

    static readonly Dictionary<string, string> voltas = new(StringComparer.Ordinal);
    static Dictionary<string, string> exatas = new(StringComparer.Ordinal);
    static List<Molde> moldes;

    /// <summary>
    /// O texto bate com o molde? Comeca com o primeiro pedaco, acaba com o
    /// ultimo, e cada pedaco do meio e achado da esquerda para a direita --
    /// o valor de cada marca e o que fica entre eles. Sem expressao regular:
    /// e linear no tamanho do texto, por mais marcas que a frase tenha.
    /// </summary>
    static bool Casar(Molde molde, string texto, string[] valores)
    {
        var pedacos = molde.Pedacos;
        if (!texto.StartsWith(pedacos[0], StringComparison.Ordinal) || !texto.EndsWith(pedacos[^1], StringComparison.Ordinal))
            return false;
        int pos = pedacos[0].Length, limite = texto.Length - pedacos[^1].Length;
        if (limite < pos) return false;
        for (int i = 0; i < molde.Marcas.Length; i++)
        {
            bool ultima = i == molde.Marcas.Length - 1;
            int ate = ultima ? limite : texto.IndexOf(pedacos[i + 1], pos, limite - pos, StringComparison.Ordinal);
            if (ate < pos) return false;
            string valor = texto[pos..ate];
            int n = molde.Marcas[i];
            if (valores[n] is null) valores[n] = valor;
            else if (valores[n] != valor) return false;
            pos = ultima ? ate : ate + pedacos[i + 1].Length;
        }
        return true;
    }

    /// <summary>
    /// Quanto texto fixo um pedaco do molde tem. Um ideograma (chines,
    /// japones, coreano) diz o que uma palavra curta diz: "第 {0} 行" e tao
    /// especifico quanto "linha {0}", entao cada um conta como dois.
    /// </summary>
    public static int PesoDoFixo(string pedaco) => pedaco.Trim().Sum(c => c is >= '⺀' and <= '鿿' or >= '가' and <= '힯'
                                                                              or >= '豈' and <= '﫿' or >= '＀' and <= '￯' ? 2 : 1);

    /// <summary>
    /// Cada traducao com {0} vira um molde. Os de mais texto fixo vem primeiro
    /// (o mais especifico ganha), e o que e quase so marca ("{0}: {1}") fica
    /// de fora -- casaria com qualquer coisa.
    /// </summary>
    static List<Molde> Moldes()
    {
        var marca = new System.Text.RegularExpressions.Regex(@"\{\{|\}\}|\{(\d+)(?:,[^}:]*)?(?::[^}]*)?\}");
        var lista = new List<Molde>();
        var unicas = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (original, traducao) in textos)
        {
            if (string.IsNullOrEmpty(traducao)) continue;
            if (!traducao.Contains('{'))
            {
                unicas.TryAdd(traducao, original);
                continue;
            }
            var pedacos = new List<string>();
            var marcas = new List<int>();
            var atual = new System.Text.StringBuilder();
            int fim = 0;
            foreach (System.Text.RegularExpressions.Match m in marca.Matches(traducao))
            {
                atual.Append(traducao, fim, m.Index - fim);
                fim = m.Index + m.Length;
                if (!m.Groups[1].Success) { atual.Append(m.Value[0]); continue; }
                pedacos.Add(atual.ToString());
                atual.Clear();
                marcas.Add(int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture));
            }
            atual.Append(traducao, fim, traducao.Length - fim);
            pedacos.Add(atual.ToString());
            int fixo = pedacos.Sum(PesoDoFixo);
            // Duas marcas coladas ("{2}{2}") nao tem fronteira entre elas: fica de fora.
            if (marcas.Count == 0 || fixo < 4 || pedacos.Skip(1).SkipLast(1).Any(p => p.Length == 0)) continue;
            lista.Add(new Molde([.. pedacos], [.. marcas], marcas.Max() + 1, original, fixo));
        }
        exatas = unicas;
        return [.. lista.OrderByDescending(m => m.Fixo)];
    }

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
