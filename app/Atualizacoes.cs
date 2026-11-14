using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;

namespace Simulador.App;

/// <summary>
/// As atualizações do programa, pelos Releases do GitHub.
///
/// Ao abrir, sem atrapalhar nada, pergunta à API do GitHub qual é o release
/// mais novo. Sendo mais novo que este programa, baixa o instalador dele,
/// confere o SHA-256 (o GitHub calcula o de cada arquivo do release) e deixa
/// pronto. Quem instala é o próprio instalador, chamado com /atualizar: ele
/// espera este processo fechar, troca os arquivos e repete as escolhas da
/// instalação (plugin, atalho). Isso acontece ao fechar o programa, ou na
/// hora pelo "Atualizar agora" -- aí o programa abre de novo no fim.
///
/// Só a cópia instalada pelo instalador se atualiza (é ela que tem o
/// desinstalar.txt ao lado do .exe): a pasta de compilação, a de publicação
/// e os testes nunca trocam os próprios arquivos.
///
/// Publicar uma versão: um release no repositório abaixo, com a tag do
/// &lt;Version&gt; do App.csproj ("v0.25.0") e o "Instalar CircuitSketch
/// ....exe" que o ferramentas\instalador\gerar.ps1 gera como arquivo.
/// </summary>
public static class Atualizacoes
{
    /// <summary>O repositório só dos instaladores (o código fica em outro).</summary>
    public const string Repositorio = "Charlie3301/CircuitSketch-Releases";

    public static string PaginaDosReleases => $"https://github.com/{Repositorio}/releases";

    internal static string UrlDaApi = $"https://api.github.com/repos/{Repositorio}/releases?per_page=30";

    public sealed record Lancamento(
        Version Versao, string Nome, string Tag, string Notas, string Pagina,
        string UrlDoInstalador, string NomeDoInstalador, long Tamanho,
        string Sha256, string UrlDoSha256);

    public enum Estado { Parado, Procurando, EmDia, Baixando, Pronta, Erro }

    public static Estado Agora { get; private set; }

    /// <summary>A versão nova achada (nula se não há, ou ainda não se sabe).</summary>
    public static Lancamento Nova { get; private set; }

    /// <summary>O instalador baixado e conferido, pronto para rodar.</summary>
    public static string Arquivo { get; private set; }

    /// <summary>0 a 1, durante o download.</summary>
    public static double Progresso { get; private set; }

    public static string Erro { get; private set; }

    /// <summary>Avisa a tela: sempre na thread da janela.</summary>
    public static event Action Mudou;

    // ---- trocáveis pelos testes ----
    internal static Func<HttpClient> NovoNavegador = () => new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
    internal static Func<string> Pasta = () => Armazem.Isolado
        ? Path.Combine(Armazem.Pasta, "atualizacao")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CircuitSketch", "atualizacao");
    internal static Func<string> PastaDoPrograma = () => AppContext.BaseDirectory;
    internal static Version VersaoAtual = Normalizar(Assembly.GetExecutingAssembly().GetName().Version);
    internal static Func<ProcessStartInfo, bool> Executar = info => { using var p = Process.Start(info); return p is not null; };

    static CancellationTokenSource cancelar;
    static Task tarefa;

    /// <summary>
    /// Foi instalado pelo instalador (e por isso pode se trocar)? Os testes
    /// e as cópias de desenvolvimento não.
    /// </summary>
    public static bool Instalado => FingirInstalado
        || (!Armazem.Isolado && File.Exists(Path.Combine(PastaDoPrograma(), "desinstalar.txt")));

    /// <summary>Os testes conferem o "instalar" sem um programa instalado.</summary>
    internal static bool FingirInstalado;

    /// <summary>Cada estado na hora em que acontece, na thread que for (os testes).</summary>
    internal static Action<Estado> AoMudar;

    /// <summary>A procura de sempre, ao abrir o programa.</summary>
    public static void ComecarAoAbrir()
    {
        if (!Instalado || !Preferencias.Ligado(Preferencias.ProcurarAtualizacoes, true)) return;
        Verificar();
    }

    /// <summary>
    /// Procura e, achando versão nova, já baixa. Fora da thread da janela;
    /// uma procura por vez (pedir de novo devolve a que está rodando).
    /// </summary>
    public static Task Verificar()
    {
        if (tarefa is { IsCompleted: false }) return tarefa;
        cancelar = new CancellationTokenSource();
        var ct = cancelar.Token;
        tarefa = Task.Run(() => Rodar(ct));
        return tarefa;
    }

    /// <summary>Baixa de novo a versão já achada (o download tinha falhado).</summary>
    public static Task BaixarDeNovo()
    {
        if (tarefa is { IsCompleted: false }) return tarefa;
        if (Nova is null) return Verificar();
        cancelar = new CancellationTokenSource();
        var ct = cancelar.Token;
        var lancamento = Nova;
        tarefa = Task.Run(() => BaixarEMarcar(lancamento, ct));
        return tarefa;
    }

    public static void Cancelar() => cancelar?.Cancel();

    static async Task Rodar(CancellationToken ct)
    {
        try
        {
            Erro = null;
            Mudar(Estado.Procurando);
            Limpar();
            var achado = await Procurar(ct);
            Nova = achado;
            if (achado is null) { Mudar(Estado.EmDia); return; }
            await BaixarEMarcar(achado, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { Mudar(Estado.Parado); }
        catch (Exception ex) { Falhou(ex); }
    }

    static async Task BaixarEMarcar(Lancamento lancamento, CancellationToken ct)
    {
        try
        {
            Erro = null;
            Progresso = 0;
            Mudar(Estado.Baixando);
            int ultimo = -1;
            var progresso = new Action<double>(f =>
            {
                Progresso = f;
                int porCento = (int)(f * 100);
                if (porCento == ultimo) return;
                ultimo = porCento;
                Mudar(Estado.Baixando);
            });
            Arquivo = await Baixar(lancamento, progresso, ct);
            Mudar(Estado.Pronta);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { Mudar(Estado.Parado); }
        catch (Exception ex) { Falhou(ex); }
    }

    static void Falhou(Exception ex)
    {
        Erro = ex switch
        {
            HttpRequestException or TaskCanceledException
                => Idioma.T("Sem conexão com o GitHub. A procura volta na próxima vez que o programa abrir."),
            _ => ex.Message,
        };
        Registrar(ex);
        Mudar(Estado.Erro);
    }

    static void Mudar(Estado estado)
    {
        Agora = estado;
        AoMudar?.Invoke(estado);
        var tela = Application.Current?.Dispatcher;
        if (tela is null || tela.CheckAccess()) Mudou?.Invoke();
        else tela.BeginInvoke(() => Mudou?.Invoke());
    }

    /// <summary>Um erro que o GitHub explicou (limite de consultas, repositório sumido).</summary>
    sealed class ProblemaNoGitHub(string mensagem) : Exception(mensagem);

    // ================= procurar =================

    static HttpRequestMessage Pedido(string url, string aceita)
    {
        var pedido = new HttpRequestMessage(HttpMethod.Get, url);
        // Só o nome e a versão do programa: nada de quem está usando.
        pedido.Headers.UserAgent.Add(new ProductInfoHeaderValue("CircuitSketch", VersaoAtual.ToString()));
        pedido.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(aceita));
        return pedido;
    }

    /// <summary>
    /// O release mais novo que este programa, ou nulo. A lista fica guardada
    /// com o ETag dela: a próxima pergunta vai com If-None-Match, e o "nada
    /// mudou" (304) do GitHub não gasta o limite de 60 consultas por hora --
    /// um laboratório inteiro abrindo o programa junto não estoura.
    /// </summary>
    internal static async Task<Lancamento> Procurar(CancellationToken ct)
    {
        string pasta = Pasta();
        string cache = Path.Combine(pasta, "releases.json");
        string arquivoEtag = Path.Combine(pasta, "releases.etag");

        using var http = NovoNavegador();
        using var pedido = Pedido(UrlDaApi, "application/vnd.github+json");
        pedido.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        if (File.Exists(cache) && File.Exists(arquivoEtag))
            pedido.Headers.TryAddWithoutValidation("If-None-Match", File.ReadAllText(arquivoEtag).Trim());

        using var resposta = await http.SendAsync(pedido, ct);
        string json;
        if (resposta.StatusCode == HttpStatusCode.NotModified && File.Exists(cache))
            json = await File.ReadAllTextAsync(cache, ct);
        else
        {
            if (resposta.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
                throw new ProblemaNoGitHub(Idioma.T("O GitHub pediu para esperar (limite de consultas por hora). A procura volta na próxima vez que o programa abrir."));
            if (resposta.StatusCode == HttpStatusCode.NotFound)
                throw new ProblemaNoGitHub(Idioma.T("O repositório das atualizações não foi achado no GitHub ({0}).", Repositorio));
            resposta.EnsureSuccessStatusCode();
            json = await resposta.Content.ReadAsStringAsync(ct);
            try
            {
                Directory.CreateDirectory(pasta);
                await File.WriteAllTextAsync(cache, json, ct);
                if (resposta.Headers.ETag is { } etag) await File.WriteAllTextAsync(arquivoEtag, etag.ToString(), ct);
                else File.Delete(arquivoEtag);
            }
            catch (IOException) { }     // sem guardar, a próxima pergunta só não vai com ETag
        }
        return Escolher(json, VersaoAtual);
    }

    /// <summary>
    /// Da lista de releases, o mais novo acima de <paramref name="atual"/>
    /// que traz um instalador. Os rascunhos ficam de fora; os marcados como
    /// pré-release entram (as Alphas e Betas são isso).
    /// </summary>
    internal static Lancamento Escolher(string json, Version atual)
    {
        Lancamento melhor = null;
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;

        foreach (var r in doc.RootElement.EnumerateArray())
        {
            if (r.TryGetProperty("draft", out var rascunho) && rascunho.ValueKind == JsonValueKind.True) continue;
            string tag = Texto(r, "tag_name");
            var versao = VersaoDaTag(tag);
            if (versao is null || versao <= atual || (melhor is not null && versao <= melhor.Versao)) continue;

            JsonElement? instalador = null;
            var arquivos = r.TryGetProperty("assets", out var a) && a.ValueKind == JsonValueKind.Array
                ? a.EnumerateArray().ToList() : [];
            foreach (var arq in arquivos)
                if (EhInstalador(Texto(arq, "name"))) { instalador = arq; break; }
            if (instalador is not { } exe) continue;

            string nomeExe = Texto(exe, "name");
            string sha = Texto(exe, "digest") is { } digest && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
                ? digest[7..].ToLowerInvariant() : null;
            string urlSha = arquivos.Where(x => string.Equals(Texto(x, "name"), nomeExe + ".sha256", StringComparison.OrdinalIgnoreCase))
                .Select(x => Texto(x, "browser_download_url")).FirstOrDefault();

            string nome = Texto(r, "name");
            melhor = new Lancamento(
                versao,
                string.IsNullOrWhiteSpace(nome) ? tag : nome.Trim(),
                tag,
                Texto(r, "body") ?? "",
                Texto(r, "html_url") ?? PaginaDosReleases,
                Texto(exe, "browser_download_url"),
                nomeExe,
                exe.TryGetProperty("size", out var t) && t.TryGetInt64(out long n) ? n : 0,
                sha,
                urlSha);
        }
        return melhor;
    }

    /// <summary>
    /// O instalador entre os arquivos do release. O GitHub troca os espaços
    /// do nome por pontos ("Instalar.CircuitSketch.Alpha.25.exe").
    /// </summary>
    internal static bool EhInstalador(string nome)
        => nome is not null && nome.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
           && Regex.IsMatch(nome, "instalar|instalador|setup|install", RegexOptions.IgnoreCase);

    /// <summary>
    /// "v0.25.0", "0.25" ou "Alpha 25" (que é a 0.25.0, como no App.csproj).
    /// </summary>
    internal static Version VersaoDaTag(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return null;
        tag = tag.Trim();
        var m = Regex.Match(tag, @"^v?(\d+)\.(\d+)(?:\.(\d+))?$", RegexOptions.IgnoreCase);
        if (m.Success)
            return new Version(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value),
                               m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : 0);
        m = Regex.Match(tag, @"^alpha[\s._-]*(\d+)$", RegexOptions.IgnoreCase);
        return m.Success ? new Version(0, int.Parse(m.Groups[1].Value), 0) : null;
    }

    internal static Version Normalizar(Version v)
        => v is null ? new Version(0, 0, 0) : new Version(v.Major, v.Minor, Math.Max(0, v.Build));

    static string Texto(JsonElement e, string nome)
        => e.TryGetProperty(nome, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    // ================= baixar =================

    /// <summary>
    /// Baixa o instalador para a pasta das atualizações e confere o SHA-256.
    /// Arquivo que não confere é apagado: um download cortado ou trocado no
    /// caminho nunca chega a rodar.
    /// </summary>
    internal static async Task<string> Baixar(Lancamento l, Action<double> progresso, CancellationToken ct)
    {
        string pasta = Path.Combine(Pasta(), l.Versao.ToString());
        Directory.CreateDirectory(pasta);
        string final = Path.Combine(pasta, $"Instalar CircuitSketch {l.Versao}.exe");

        using var http = NovoNavegador();

        string sha = l.Sha256;
        if (sha is null && l.UrlDoSha256 is not null)
        {
            using var pedidoSha = Pedido(l.UrlDoSha256, "application/octet-stream");
            using var respostaSha = await http.SendAsync(pedidoSha, ct);
            respostaSha.EnsureSuccessStatusCode();
            sha = (await respostaSha.Content.ReadAsStringAsync(ct)).Trim().Split([' ', '\t', '\r', '\n', '*'], 2)[0].ToLowerInvariant();
        }
        if (sha is null || !Regex.IsMatch(sha, "^[0-9a-f]{64}$"))
            throw new InvalidDataException(Idioma.T("O release não informa o SHA-256 do instalador, e sem ele não dá para conferir o arquivo baixado."));

        if (File.Exists(final) && await Hash(final, ct) == sha)
        {
            progresso(1);
            return final;
        }

        string parcial = final + ".parcial";
        using (var pedido = Pedido(l.UrlDoInstalador, "application/octet-stream"))
        using (var resposta = await http.SendAsync(pedido, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            resposta.EnsureSuccessStatusCode();
            long total = resposta.Content.Headers.ContentLength ?? l.Tamanho;
            await using var origem = await resposta.Content.ReadAsStreamAsync(ct);
            await using var saida = new FileStream(parcial, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true);
            var buffer = new byte[1 << 16];
            long feito = 0;
            int lidos;
            while ((lidos = await origem.ReadAsync(buffer, ct)) > 0)
            {
                await saida.WriteAsync(buffer.AsMemory(0, lidos), ct);
                feito += lidos;
                if (total > 0) progresso(Math.Min(1, feito / (double)total));
            }
        }

        if (await Hash(parcial, ct) != sha)
        {
            try { File.Delete(parcial); } catch (IOException) { }
            throw new InvalidDataException(Idioma.T("O instalador baixado não confere com o SHA-256 do release. Ele foi apagado, e o download recomeça na próxima vez."));
        }
        File.Move(parcial, final, true);
        progresso(1);
        return final;
    }

    static async Task<string> Hash(string arquivo, CancellationToken ct)
    {
        await using var f = new FileStream(arquivo, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, useAsync: true);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(f, ct));
    }

    /// <summary>
    /// Tira da pasta das atualizações as versões que já não interessam: a
    /// que está rodando e as mais velhas (já foram instaladas).
    /// </summary>
    static void Limpar()
    {
        try
        {
            string pasta = Pasta();
            if (!Directory.Exists(pasta)) return;
            foreach (string d in Directory.GetDirectories(pasta))
                if (Version.TryParse(Path.GetFileName(d), out var v) && Normalizar(v) <= VersaoAtual)
                    Directory.Delete(d, true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    // ================= instalar =================

    /// <summary>
    /// Roda o instalador baixado no modo de atualização. Ele espera este
    /// processo fechar antes de mexer em qualquer arquivo; com
    /// <paramref name="reabrir"/>, abre o programa de novo no fim.
    /// </summary>
    public static bool Instalar(bool reabrir)
    {
        if (Agora != Estado.Pronta || Arquivo is null || !File.Exists(Arquivo) || !Instalado) return false;
        var info = new ProcessStartInfo(Arquivo)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(Arquivo),
        };
        foreach (string a in ArgumentosDoInstalador(PastaDoPrograma(), Environment.ProcessId, reabrir))
            info.ArgumentList.Add(a);
        try { return Executar(info); }
        catch (Exception ex)
        {
            Registrar(ex);
            return false;
        }
    }

    internal static List<string> ArgumentosDoInstalador(string pasta, int pid, bool reabrir)
    {
        var lista = new List<string>
        {
            "/atualizar",
            "/destino=" + Path.GetFullPath(pasta).TrimEnd('\\', '/'),
            "/esperar=" + pid,
        };
        if (reabrir) lista.Add("/abrir");
        return lista;
    }

    static void Registrar(Exception ex)
    {
        try
        {
            string pasta = Pasta();
            Directory.CreateDirectory(pasta);
            File.AppendAllText(Path.Combine(pasta, "atualizacao.log"),
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + ex.GetType().Name + ": " + ex.Message + Environment.NewLine);
        }
        catch { }
    }

    /// <summary>Volta tudo ao começo (os testes).</summary>
    internal static void Zerar()
    {
        Cancelar();
        tarefa = null;
        Agora = Estado.Parado;
        Nova = null;
        Arquivo = null;
        Erro = null;
        Progresso = 0;
    }

    /// <summary>Põe um estado na mão (os testes da tela).</summary>
    internal static void Simular(Estado estado, Lancamento nova, string arquivo = null, double progresso = 0, string erro = null)
    {
        Nova = nova;
        Arquivo = arquivo;
        Progresso = progresso;
        Erro = erro;
        Mudar(estado);
    }
}
