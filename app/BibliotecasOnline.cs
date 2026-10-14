using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Simulador.App;

public sealed record DependenciaOnline(string Nome, string Versao = null);

public sealed record BibliotecaOnline(
    string Nome, string Versao, string Autor, string Sentence, string Paragraph,
    string Website, string Category, IReadOnlyList<string> Architectures,
    IReadOnlyList<string> Cabecalhos, string Url, string ArchiveFileName,
    long Size, string Checksum, IReadOnlyList<DependenciaOnline> Dependencias)
{
    public IReadOnlyList<BibliotecaOnline> OutrasVersoes { get; init; } = [];

    /// <summary>O repositorio de origem ("https://github.com/knolleary/pubsubclient.git"): o nome dele diz o .h.</summary>
    public string Repositorio { get; init; } = "";
}

public sealed class IndiceDeBibliotecas
{
    readonly Dictionary<string, BibliotecaOnline> bibliotecas;
    IndiceDeBibliotecas(Dictionary<string, BibliotecaOnline> bibliotecas) => this.bibliotecas = bibliotecas;

    /// <summary>Quantas bibliotecas (nomes diferentes) o indice tem.</summary>
    public int Quantas => bibliotecas.Count;

    public static IndiceDeBibliotecas Ler(Stream json)
    {
        using var documento = JsonDocument.Parse(json);
        var grupos = new Dictionary<string, List<BibliotecaOnline>>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in documento.RootElement.GetProperty("libraries").EnumerateArray())
        {
            string Texto(string campo) => item.TryGetProperty(campo, out var v) ? v.GetString() ?? "" : "";
            string[] Lista(string campo) => item.TryGetProperty(campo, out var v)
                ? v.EnumerateArray().Select(e => e.GetString() ?? "").ToArray() : [];
            var dependencias = item.TryGetProperty("dependencies", out var deps)
                ? deps.EnumerateArray().Select(d => new DependenciaOnline(d.GetProperty("name").GetString(),
                    d.TryGetProperty("version", out var versao) ? versao.GetString() : null)).ToArray() : [];
            var b = new BibliotecaOnline(Texto("name"), Texto("version"), Texto("author"), Texto("sentence"),
                Texto("paragraph"), Texto("website"), Texto("category"), Lista("architectures"),
                Lista("providesIncludes"), Texto("url"), Texto("archiveFileName"),
                item.TryGetProperty("size", out var tamanho) ? tamanho.GetInt64() : 0, Texto("checksum"), dependencias);
            b = b with { Repositorio = Texto("repository") };
            if (!grupos.TryGetValue(b.Nome, out var grupo)) grupos[b.Nome] = grupo = [];
            grupo.Add(b);
        }
        var resultado = new Dictionary<string, BibliotecaOnline>(StringComparer.OrdinalIgnoreCase);
        foreach (var (nome, grupo) in grupos)
        {
            grupo.Sort((a, b) => CompararVersoes(b.Versao, a.Versao));
            resultado[nome] = grupo[0] with { OutrasVersoes = grupo.Skip(1).ToArray() };
        }
        return new(resultado);
    }

    // SemVer "relaxado", como o da Arduino IDE: o "1.0" (sem o patch) e o "2"
    // valem -- e no indice ha muitos assim. Numeros sem limite de int,
    // pre-release antes da final; build nao ordena.
    static readonly Regex semver = new(@"^v?([0-9]+)(?:\.([0-9]+))?(?:\.([0-9]+))?(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?$", RegexOptions.CultureInvariant);
    static Match VersaoValida(string texto)
    {
        var m = semver.Match(texto);
        return m.Success && !m.Groups[4].Value.Split('.').Any(p => p.Length > 1 && p[0] == '0' && p.All(char.IsAsciiDigit)) ? m : null;
    }
    internal static int CompararVersoes(string a, string b)
    {
        var x = VersaoValida(a); var y = VersaoValida(b);
        if (x is null || y is null) return x is null ? (y is null ? 0 : -1) : 1;
        for (int i = 1; i <= 3; i++)
        {
            static BigInteger N(Group g) => g.Success ? BigInteger.Parse(g.Value) : BigInteger.Zero;
            int c = N(x.Groups[i]).CompareTo(N(y.Groups[i]));
            if (c != 0) return c;
        }
        string px = x.Groups[4].Value, py = y.Groups[4].Value;
        if (px.Length == 0 || py.Length == 0) return px.Length == 0 ? (py.Length == 0 ? 0 : 1) : -1;
        var ax = px.Split('.'); var ay = py.Split('.');
        for (int i = 0; i < Math.Min(ax.Length, ay.Length); i++)
        {
            bool nx = BigInteger.TryParse(ax[i], NumberStyles.None, CultureInfo.InvariantCulture, out var vx);
            bool ny = BigInteger.TryParse(ay[i], NumberStyles.None, CultureInfo.InvariantCulture, out var vy);
            int c = nx && ny ? vx.CompareTo(vy) : nx != ny ? (nx ? -1 : 1) : string.CompareOrdinal(ax[i], ay[i]);
            if (c != 0) return c;
        }
        return ax.Length.CompareTo(ay.Length);
    }

    static readonly CompareInfo comparador = CultureInfo.InvariantCulture.CompareInfo;
    const CompareOptions opcoes = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;
    static bool Igual(string a, string b) => comparador.Compare(a, b, opcoes) == 0;
    static bool Contem(string a, string b) => comparador.IndexOf(a, b, opcoes) >= 0;

    public IReadOnlyList<BibliotecaOnline> Buscar(string texto, int maximo = 50)
    {
        texto = texto?.Trim() ?? "";
        // Varias palavras ("sensor de temperatura"): todas, em qualquer ordem, no nome ou na descricao.
        var palavras = texto.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        bool TodasAsPalavras(BibliotecaOnline b)
            => palavras.Length > 1 && palavras.All(p => Contem(b.Nome, p) || Contem(b.Sentence, p) || Contem(b.Paragraph, p)
                                                       || b.Cabecalhos.Any(h => Contem(h, p)));
        int Nota(BibliotecaOnline b) => Igual(b.Nome, texto) ? 0
            : comparador.IsPrefix(b.Nome, texto, opcoes) ? 1 : Contem(b.Nome, texto) ? 2
            : b.Cabecalhos.Any(h => Contem(h, texto)) ? 3
            : Contem(b.Sentence, texto) || Contem(b.Paragraph, texto) || TodasAsPalavras(b) ? 4 : 5;
        return bibliotecas.Values.Select(b => (Biblioteca: b, Nota: Nota(b))).Where(p => p.Nota < 5)
            .OrderBy(p => p.Nota).ThenBy(p => p.Biblioteca.Nome, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Max(0, maximo)).Select(p => p.Biblioteca).ToArray();
    }

    /// <summary>
    /// Quem fornece este #include, da mais provavel para a menos. Menos da
    /// metade das bibliotecas do indice lista os .h ("providesIncludes"): a
    /// PubSubClient, a DHT da Adafruit e a BMP280 nao listam -- e ha quem liste
    /// o .h de OUTRA que vem embutida (a EasyMQTT lista o PubSubClient.h).
    /// Entao, em camadas: (0) o nome da biblioteca ou do repositorio igual ao do
    /// .h ("PubSubClient", "Adafruit BMP280 Library", "TinyGPSPlus"); (1) quem
    /// lista o .h igualzinho; (2) quem lista o .h com outra caixa, ou o nome
    /// comecando pelo do .h ou o contrario ("DHT sensor library" para o DHT.h,
    /// "U8g2" para o U8g2lib.h). Na mesma camada, a com mais versoes
    /// publicadas (a mais mantida) primeiro.
    /// </summary>
    public IReadOnlyList<BibliotecaOnline> QuemTem(string cabecalho)
    {
        if (string.IsNullOrWhiteSpace(cabecalho)) return [];
        string baseDoCabecalho = Normal(Path.GetFileNameWithoutExtension(cabecalho.Trim()));
        if (baseDoCabecalho.Length == 0) return [];

        int Camada(BibliotecaOnline b)
        {
            string nome = Normal(b.Nome);
            string semLibrary = nome.EndsWith("library", StringComparison.Ordinal) ? nome[..^7] : nome;
            string repositorio = Normal(Path.GetFileNameWithoutExtension(b.Repositorio.TrimEnd('/')));
            bool listaOutros = b.Cabecalhos.Count > 0 && !b.Cabecalhos.Any(h => Igual(h, cabecalho));
            // O nome vence o repositorio: o fork "TBPubSubClient" mora num repositorio "pubsubclient".
            if (!listaOutros && (nome == baseDoCabecalho || semLibrary == baseDoCabecalho)) return 0;
            if (!listaOutros && repositorio == baseDoCabecalho) return 1;
            if (b.Cabecalhos.Any(h => h == cabecalho)) return 1;
            if (b.Cabecalhos.Any(h => Igual(h, cabecalho))) return 2;
            if (!listaOutros && Math.Min(semLibrary.Length, baseDoCabecalho.Length) >= 3
                && (semLibrary.StartsWith(baseDoCabecalho, StringComparison.Ordinal)
                    || baseDoCabecalho.StartsWith(semLibrary, StringComparison.Ordinal))) return 2;
            return 9;
        }
        return bibliotecas.Values.Select(b => (Biblioteca: b, Camada: Camada(b))).Where(p => p.Camada < 9)
            .OrderBy(p => p.Camada).ThenByDescending(p => p.Biblioteca.OutrasVersoes.Count)
            .ThenBy(p => p.Biblioteca.Nome, StringComparer.OrdinalIgnoreCase)
            .Select(p => p.Biblioteca).ToArray();
    }

    /// <summary>"TinyGPS++" -> "tinygpsplus" (o "++" e lido "Plus", como no nome da biblioteca); "Adafruit_BMP280" -> "adafruitbmp280".</summary>
    static string Normal(string texto)
    {
        var limpo = new System.Text.StringBuilder();
        foreach (char c in (texto ?? "").Replace("++", "plus").Replace("+", "plus"))
            if (char.IsAsciiLetterOrDigit(c)) limpo.Append(char.ToLowerInvariant(c));
        return limpo.ToString();
    }

    /// <summary>Ordena por dependencia. As restricoes de versao ficam no registro para o chamador;
    /// este plano usa a versao mais recente de cada dependencia do indice.</summary>
    public IReadOnlyList<BibliotecaOnline> ParaInstalar(BibliotecaOnline alvo, ISet<string> jaInstaladas)
    {
        var visitadas = new HashSet<string>(jaInstaladas, StringComparer.OrdinalIgnoreCase);
        var ordem = new List<BibliotecaOnline>();
        void Visitar(BibliotecaOnline b)
        {
            if (!visitadas.Add(b.Nome) || Acervo.EhDoSimulador(b.Cabecalhos.FirstOrDefault() ?? b.Nome + ".h")) return;
            foreach (var dep in b.Dependencias)
            {
                if (visitadas.Contains(dep.Nome) || Acervo.EhDoSimulador(dep.Nome + ".h")) continue;
                if (!bibliotecas.TryGetValue(dep.Nome, out var encontrada))
                    throw new InvalidOperationException(Idioma.T("A dependência {0} não está no índice de bibliotecas.", dep.Nome));
                Visitar(encontrada);
            }
            ordem.Add(b);
        }
        Visitar(alvo);
        return ordem;
    }
}

public static class BibliotecasOnline
{
    public const string UrlDoIndice = "https://downloads.arduino.cc/libraries/library_index.json.gz";
    public static string PastaDoCache => Path.Combine(Armazem.Pasta, "indice-bibliotecas");

    static async Task<IndiceDeBibliotecas> LerGzipAsync(string arquivo, CancellationToken ct)
    {
        return await Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            using var entrada = File.OpenRead(arquivo);
            using var gzip = new GZipStream(entrada, CompressionMode.Decompress);
            var indice = IndiceDeBibliotecas.Ler(gzip);
            ct.ThrowIfCancellationRequested();
            return indice;
        }, ct).ConfigureAwait(false);
    }

    public static async Task<IndiceDeBibliotecas> CarregarAsync(HttpClient http, bool atualizar, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Directory.CreateDirectory(PastaDoCache);
        string cache = Path.Combine(PastaDoCache, "library_index.json.gz");
        if (!atualizar && File.Exists(cache)) return await LerGzipAsync(cache, ct).ConfigureAwait(false);
        string temporario = cache + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using var resposta = await http.GetAsync(UrlDoIndice, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            resposta.EnsureSuccessStatusCode();
            await using (var arquivo = new FileStream(temporario, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
                await resposta.Content.CopyToAsync(arquivo, ct).ConfigureAwait(false);
            var indice = await LerGzipAsync(temporario, ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            File.Move(temporario, cache, true);
            return indice;
        }
        catch (Exception e) when (e is HttpRequestException or IOException or JsonException || e is OperationCanceledException && !ct.IsCancellationRequested)
        {
            ct.ThrowIfCancellationRequested();
            if (File.Exists(cache)) return await LerGzipAsync(cache, ct).ConfigureAwait(false);
            throw new InvalidOperationException(Idioma.T("Não foi possível carregar o índice de bibliotecas. Conecte-se à internet e tente novamente."), e);
        }
        finally { if (File.Exists(temporario)) File.Delete(temporario); }
    }

    static void ConferirUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps
            || !uri.Host.Equals("downloads.arduino.cc", StringComparison.OrdinalIgnoreCase)
            || !uri.IsDefaultPort || uri.UserInfo.Length > 0)
            throw new InvalidOperationException(Idioma.T("Só é permitido baixar bibliotecas de https://downloads.arduino.cc/."));
    }

    public static async Task<string> BaixarEInstalarAsync(HttpClient http, BibliotecaOnline lib,
        IProgress<double> progresso, CancellationToken ct)
    {
        ConferirUrl(lib.Url);
        ct.ThrowIfCancellationRequested();
        string temporario = Path.Combine(Path.GetTempPath(), "circuitsketch-biblioteca-" + Guid.NewGuid().ToString("N") + ".zip");
        try
        {
            using var resposta = await http.GetAsync(lib.Url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            resposta.EnsureSuccessStatusCode();
            if (resposta.RequestMessage?.RequestUri is { } final) ConferirUrl(final.AbsoluteUri);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long total = 0;
            progresso?.Report(0);
            await using (var destino = new FileStream(temporario, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            await using (var origem = await resposta.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
            {
                byte[] buffer = new byte[81920];
                int lidos;
                while ((lidos = await origem.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                {
                    total += lidos;
                    if (total > lib.Size) throw new InvalidDataException(Idioma.T("O tamanho da biblioteca não corresponde ao índice."));
                    hash.AppendData(buffer, 0, lidos);
                    await destino.WriteAsync(buffer.AsMemory(0, lidos), ct).ConfigureAwait(false);
                    progresso?.Report(lib.Size > 0 ? (double)total / lib.Size : 0);
                }
            }
            if (total != lib.Size) throw new InvalidDataException(Idioma.T("O tamanho da biblioteca não corresponde ao índice."));
            if (!string.Equals(lib.Checksum, "SHA-256:" + Convert.ToHexString(hash.GetHashAndReset()), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(Idioma.T("O SHA-256 da biblioteca não corresponde ao índice."));
            return await Task.Run(() => { ct.ThrowIfCancellationRequested(); return Acervo.InstalarZip(temporario); }, ct).ConfigureAwait(false);
        }
        finally { if (File.Exists(temporario)) File.Delete(temporario); }
    }

    public static async Task<List<string>> InstalarComDependenciasAsync(HttpClient http, IndiceDeBibliotecas indice,
        BibliotecaOnline alvo, ISet<string> jaInstaladas, IProgress<double> progresso, CancellationToken ct)
    {
        var plano = await Task.Run(() => indice.ParaInstalar(alvo, jaInstaladas), ct).ConfigureAwait(false);
        var nomes = new List<string>();
        foreach (var lib in plano)
        {
            ct.ThrowIfCancellationRequested();
            nomes.Add(await BaixarEInstalarAsync(http, lib, progresso, ct).ConfigureAwait(false));
        }
        return nomes;
    }
}
