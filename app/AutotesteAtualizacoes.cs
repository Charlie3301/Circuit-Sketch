using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;

namespace Simulador.App;

/// <summary>
/// Buscar atualizações (Alpha 25): o programa procura nos Releases do GitHub,
/// baixa o instalador, confere o SHA-256 e o chama no modo /atualizar.
/// Tudo contra um GitHub de mentira: nenhum teste usa a internet.
/// </summary>
public static partial class Autoteste
{
    /// <summary>A API de releases e os downloads, de mentira.</summary>
    sealed class GitHubFalso : HttpMessageHandler
    {
        public string Releases = "[]";
        public string Etag = "\"lista-1\"";
        public bool Limite, SemInternet;
        public readonly Dictionary<string, byte[]> Arquivos = [];
        public readonly List<(string Url, string Agente, string SeNaoMudou)> Pedidos = [];
        public int NaoMudou;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage pedido, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            string url = pedido.RequestUri.AbsoluteUri;
            pedido.Headers.TryGetValues("If-None-Match", out var seNao);
            Pedidos.Add((url, pedido.Headers.UserAgent.ToString(), seNao?.FirstOrDefault()));
            if (SemInternet) throw new HttpRequestException("Sem internet (teste)");

            HttpResponseMessage resposta;
            if (url == Atualizacoes.UrlDaApi)
            {
                if (Limite) resposta = new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent("{\"message\":\"API rate limit exceeded\"}") };
                else if (seNao?.FirstOrDefault() == Etag) { NaoMudou++; resposta = new HttpResponseMessage(HttpStatusCode.NotModified); }
                else
                {
                    resposta = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Releases) };
                    resposta.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue(Etag);
                }
            }
            else if (Arquivos.TryGetValue(url, out var dados))
                resposta = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(dados) };
            else
                resposta = new HttpResponseMessage(HttpStatusCode.NotFound);
            resposta.RequestMessage = pedido;
            return Task.FromResult(resposta);
        }
    }

    static object ReleaseFalso(string tag, string nome, bool rascunho, params object[] arquivos) => new
    {
        tag_name = tag,
        name = nome,
        draft = rascunho,
        prerelease = true,
        html_url = $"https://github.com/{Atualizacoes.Repositorio}/releases/tag/{tag}",
        body = "## Novidades\r\n\r\n- **Frames** de carrinho, drone e braço\r\n- Buscar `atualizações` pelo [GitHub](https://github.com)\r\n",
        assets = arquivos,
    };

    static object ArquivoFalso(string nome, byte[] dados, string digest) => new
    {
        name = nome,
        size = dados?.Length ?? 0,
        digest,
        browser_download_url = $"https://github.com/{Atualizacoes.Repositorio}/releases/download/x/{nome}",
    };

    static string UrlFalsa(string nome) => $"https://github.com/{Atualizacoes.Repositorio}/releases/download/x/{nome}";

    static void Atualizacoes_GitHubReleases()
    {
        saida.AppendLine("buscar atualizacoes: os Releases do GitHub, o download conferido e o instalador");

        var navegadorAntes = Atualizacoes.NovoNavegador;
        var pastaAntes = Atualizacoes.Pasta;
        var versaoAntes = Atualizacoes.VersaoAtual;
        var executarAntes = Atualizacoes.Executar;
        string pasta = Path.Combine(Path.GetTempPath(), $"circuitsketch-atualizacao-{Guid.NewGuid():N}");
        var github = new GitHubFalso();
        Atualizacoes.NovoNavegador = () => new HttpClient(github, disposeHandler: false);
        Atualizacoes.Pasta = () => pasta;
        Atualizacoes.VersaoAtual = new Version(0, 24, 0);

        // As chamadas assincronas rodam fora da thread da janela (que e a do teste).
        static T Rodar<T>(Func<Task<T>> f) => Task.Run(f).GetAwaiter().GetResult();

        try
        {
            // ---- as versoes ----
            Conferir("atualizacoes: a tag vira versao (v0.25.0, 0.25, Alpha 25 = 0.25.0; texto solto nao)",
                Atualizacoes.VersaoDaTag("v0.25.0") == new Version(0, 25, 0) && Atualizacoes.VersaoDaTag("0.25") == new Version(0, 25, 0)
                && Atualizacoes.VersaoDaTag("Alpha 25") == new Version(0, 25, 0) && Atualizacoes.VersaoDaTag("alpha-26") == new Version(0, 26, 0)
                && Atualizacoes.VersaoDaTag("V1.2.3") == new Version(1, 2, 3) && Atualizacoes.VersaoDaTag("nightly") is null
                && Atualizacoes.VersaoDaTag("") is null);
            Conferir("atualizacoes: a versao deste programa sai do App.csproj (Version 0.24.0 = Alpha 24)",
                versaoAntes == Atualizacoes.Normalizar(typeof(Atualizacoes).Assembly.GetName().Version) && versaoAntes.Build >= 0);
            Conferir("atualizacoes: o instalador e achado pelo nome (o GitHub troca espaco por ponto)",
                Atualizacoes.EhInstalador("Instalar.CircuitSketch.Alpha.25.exe") && Atualizacoes.EhInstalador("CircuitSketch-Setup.exe")
                && !Atualizacoes.EhInstalador("LEIA-ME.txt") && !Atualizacoes.EhInstalador("CircuitSketch.exe")
                && !Atualizacoes.EhInstalador("Instalar.CircuitSketch.zip"));

            // ---- o release certo ----
            var exe26 = new byte[300_000];
            new Random(26).NextBytes(exe26);
            string sha26 = Convert.ToHexStringLower(SHA256.HashData(exe26));
            const string Nome26 = "Instalar.CircuitSketch.Alpha.26.exe";
            object[] lista =
            [
                ReleaseFalso("v0.99.0", "Rascunho", true, ArquivoFalso("Instalar.CircuitSketch.exe", exe26, "sha256:" + sha26)),
                ReleaseFalso("v0.30.0", "So o zip", false, ArquivoFalso("CircuitSketch.zip", exe26, "sha256:" + sha26)),
                ReleaseFalso("v0.26.0", "Alpha 26", false, ArquivoFalso(Nome26, exe26, "sha256:" + sha26), ArquivoFalso("LEIA-ME.txt", [1], null)),
                ReleaseFalso("Alpha 25", "", false, ArquivoFalso("Instalar.CircuitSketch.Alpha.25.exe", exe26, "sha256:" + sha26)),
                ReleaseFalso("v0.24.0", "Alpha 24", false, ArquivoFalso("Instalar.CircuitSketch.Alpha.24.exe", exe26, "sha256:" + sha26)),
            ];
            github.Releases = JsonSerializer.Serialize(lista);
            github.Arquivos[UrlFalsa(Nome26)] = exe26;

            var achado = Atualizacoes.Escolher(github.Releases, new Version(0, 24, 0));
            Conferir("atualizacoes: o mais novo com instalador (rascunho e release sem .exe ficam de fora)",
                achado is { Nome: "Alpha 26", Tag: "v0.26.0", NomeDoInstalador: Nome26 } && achado.Versao == new Version(0, 26, 0)
                && achado.Sha256 == sha26 && achado.Tamanho == exe26.Length && achado.UrlDoInstalador == UrlFalsa(Nome26),
                $"{achado?.Tag} {achado?.NomeDoInstalador}");
            Conferir("atualizacoes: ja na versao mais nova, nada para atualizar (e versao igual nao conta)",
                Atualizacoes.Escolher(github.Releases, new Version(0, 26, 0)) is null
                && Atualizacoes.Escolher("[]", new Version(0, 24, 0)) is null && Atualizacoes.Escolher("{}", new Version(0, 24, 0)) is null);
            Conferir("atualizacoes: release sem nome usa a tag",
                Atualizacoes.Escolher(github.Releases, new Version(0, 24, 5)) is { Nome: "Alpha 26" }
                && Atualizacoes.Escolher(JsonSerializer.Serialize(new[] { lista[3] }), new Version(0, 24, 0)) is { Nome: "Alpha 25" });

            // ---- a pergunta ao GitHub ----
            var procurado = Rodar(() => Atualizacoes.Procurar(CancellationToken.None));
            var primeiro = github.Pedidos.LastOrDefault();
            Conferir("atualizacoes: a pergunta vai a API de releases do repositorio, so com o nome e a versao do programa",
                procurado?.Tag == "v0.26.0" && primeiro.Url == $"https://api.github.com/repos/{Atualizacoes.Repositorio}/releases?per_page=30"
                && primeiro.Agente == "CircuitSketch/0.24.0" && primeiro.SeNaoMudou is null,
                $"{primeiro.Url} | {primeiro.Agente}");
            var denovo = Rodar(() => Atualizacoes.Procurar(CancellationToken.None));
            Conferir("atualizacoes: a segunda pergunta vai com o ETag e o \"nada mudou\" (304) usa a lista guardada",
                denovo?.Tag == "v0.26.0" && github.NaoMudou == 1 && github.Pedidos.Last().SeNaoMudou == github.Etag);

            // ---- o download ----
            double ultimo = 0;
            string baixado = Rodar(() => Atualizacoes.Baixar(achado, f => ultimo = f, CancellationToken.None));
            Conferir("atualizacoes: o instalador e baixado, conferido pelo SHA-256 e guardado pela versao",
                File.Exists(baixado) && Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(baixado))) == sha26
                && ultimo == 1 && Path.GetFileName(Path.GetDirectoryName(baixado)) == "0.26.0" && !File.Exists(baixado + ".parcial"),
                baixado);
            int pedidosAntes = github.Pedidos.Count;
            string deNovo = Rodar(() => Atualizacoes.Baixar(achado, _ => { }, CancellationToken.None));
            Conferir("atualizacoes: ja baixado e conferido, nao baixa de novo", deNovo == baixado && github.Pedidos.Count == pedidosAntes);

            // Arquivo trocado no caminho: nao confere, e apagado e nunca roda.
            var trocado = achado with { Versao = new Version(0, 26, 1), Sha256 = new string('0', 64) };
            string erroTrocado = null;
            try { Rodar(() => Atualizacoes.Baixar(trocado, _ => { }, CancellationToken.None)); }
            catch (InvalidDataException ex) { erroTrocado = ex.Message; }
            string pastaTrocado = Path.Combine(pasta, "0.26.1");
            Conferir("atualizacoes: download que nao confere com o SHA-256 e apagado",
                erroTrocado is not null && Directory.Exists(pastaTrocado) && Directory.GetFiles(pastaTrocado).Length == 0, erroTrocado);

            // Sem o SHA-256 no release (nem o digest, nem um .sha256 do lado), nao instala.
            var semSha = achado with { Versao = new Version(0, 26, 2), Sha256 = null, UrlDoSha256 = null };
            string erroSemSha = null;
            try { Rodar(() => Atualizacoes.Baixar(semSha, _ => { }, CancellationToken.None)); }
            catch (InvalidDataException ex) { erroSemSha = ex.Message; }
            Conferir("atualizacoes: release sem SHA-256 nao e baixado (nao da para conferir)",
                erroSemSha is not null && !Directory.EnumerateFiles(Path.Combine(pasta, "0.26.2")).Any());
            github.Arquivos[UrlFalsa(Nome26 + ".sha256")] = System.Text.Encoding.ASCII.GetBytes(sha26.ToUpperInvariant() + " *" + Nome26 + "\n");
            var comArquivoSha = semSha with { Versao = new Version(0, 26, 3), UrlDoSha256 = UrlFalsa(Nome26 + ".sha256") };
            string peloArquivoSha = Rodar(() => Atualizacoes.Baixar(comArquivoSha, _ => { }, CancellationToken.None));
            Conferir("atualizacoes: o SHA-256 tambem vale num arquivo .sha256 ao lado do instalador", File.Exists(peloArquivoSha));

            // ---- o caminho inteiro: procura, baixa, fica pronta ----
            Atualizacoes.Zerar();
            Directory.CreateDirectory(Path.Combine(pasta, "0.23.0"));
            Directory.CreateDirectory(Path.Combine(pasta, "0.24.0"));
            var estados = new List<Atualizacoes.Estado>();
            Atualizacoes.AoMudar = e => { lock (estados) estados.Add(e); };
            try { Atualizacoes.Verificar().GetAwaiter().GetResult(); }
            finally { Atualizacoes.AoMudar = null; }
            Conferir("atualizacoes: ao abrir procura e ja baixa; fica pronta para instalar ao fechar",
                Atualizacoes.Agora == Atualizacoes.Estado.Pronta && Atualizacoes.Nova?.Nome == "Alpha 26"
                && File.Exists(Atualizacoes.Arquivo) && estados.Contains(Atualizacoes.Estado.Procurando)
                && estados.Contains(Atualizacoes.Estado.Baixando) && estados.Last() == Atualizacoes.Estado.Pronta,
                $"{Atualizacoes.Agora} {Atualizacoes.Erro} [{string.Join(",", estados)}]");
            Conferir("atualizacoes: as versoes ja instaladas saem da pasta dos downloads",
                !Directory.Exists(Path.Combine(pasta, "0.23.0")) && !Directory.Exists(Path.Combine(pasta, "0.24.0"))
                && Directory.Exists(Path.Combine(pasta, "0.26.0")));

            // ---- o instalador e chamado no modo de atualizacao ----
            Conferir("atualizacoes: os testes e a copia de desenvolvimento nunca se instalam",
                !Atualizacoes.Instalado && !Atualizacoes.Instalar(false));
            ProcessStartInfo chamado = null;
            Atualizacoes.Executar = info => { chamado = info; return true; };
            Atualizacoes.FingirInstalado = true;
            try
            {
                bool foi = Atualizacoes.Instalar(reabrir: true);
                string pastaDoPrograma = Path.GetFullPath(AppContext.BaseDirectory).TrimEnd('\\');
                Conferir("atualizacoes: \"Atualizar agora\" chama o instalador baixado com /atualizar, a pasta do programa, o processo a esperar e /abrir",
                    foi && chamado?.FileName == Atualizacoes.Arquivo && !chamado.UseShellExecute
                    && chamado.ArgumentList.SequenceEqual(["/atualizar", "/destino=" + pastaDoPrograma, "/esperar=" + Environment.ProcessId, "/abrir"]),
                    string.Join(" ", chamado?.ArgumentList ?? []));
                Atualizacoes.Instalar(reabrir: false);
                Conferir("atualizacoes: ao fechar o programa, o mesmo instalador sem o /abrir",
                    chamado?.ArgumentList.Count == 3 && !chamado.ArgumentList.Contains("/abrir"));
            }
            finally { Atualizacoes.FingirInstalado = false; Atualizacoes.Executar = executarAntes; }
            Conferir("atualizacoes: a pasta vai sem a barra do fim (o instalador recebe o caminho limpo)",
                Atualizacoes.ArgumentosDoInstalador(@"C:\Programas\Circuit Sketch\", 7, false)
                    .SequenceEqual(["/atualizar", @"/destino=C:\Programas\Circuit Sketch", "/esperar=7"]));

            // ---- sem internet e limite do GitHub ----
            Atualizacoes.Zerar();
            github.SemInternet = true;
            Atualizacoes.Verificar().GetAwaiter().GetResult();
            Conferir("atualizacoes: sem internet, o erro e dito e o programa segue",
                Atualizacoes.Agora == Atualizacoes.Estado.Erro && Atualizacoes.Erro?.Contains("GitHub") == true && Atualizacoes.Nova is null,
                Atualizacoes.Erro);
            github.SemInternet = false;
            github.Limite = true;
            File.Delete(Path.Combine(pasta, "releases.json"));
            Atualizacoes.Zerar();
            Atualizacoes.Verificar().GetAwaiter().GetResult();
            Conferir("atualizacoes: o limite de consultas do GitHub (403) vira aviso, nao travamento",
                Atualizacoes.Agora == Atualizacoes.Estado.Erro && Atualizacoes.Erro?.Contains("GitHub") == true, Atualizacoes.Erro);
            github.Limite = false;

            // ---- ao abrir: so a copia instalada, e so com a opcao ligada ----
            Atualizacoes.Zerar();
            int pedidos = github.Pedidos.Count;
            Atualizacoes.ComecarAoAbrir();
            Conferir("atualizacoes: a copia nao instalada nao procura nada ao abrir",
                Atualizacoes.Agora == Atualizacoes.Estado.Parado && github.Pedidos.Count == pedidos);
            Conferir("atualizacoes: procurar ao abrir vem ligado de fabrica",
                Preferencias.Ligado(Preferencias.ProcurarAtualizacoes, true));

            // ---- as novidades do release ----
            string notas = JanelaAtualizacao.Notas(achado.Notas);
            Conferir("atualizacoes: as novidades (Markdown) viram texto corrido",
                notas.StartsWith("Novidades\n\n• Frames de carrinho") && notas.Contains("• Buscar atualizações pelo GitHub")
                && !notas.Contains('#') && !notas.Contains("**") && !notas.Contains('`') && !notas.Contains("](") && !notas.Contains('\r'),
                notas);

            // ---- a tela ----
            var janela = new MainWindow();
            try
            {
                Atualizacoes.Simular(Atualizacoes.Estado.EmDia, null);
                Conferir("atualizacoes: sem versao nova, o aviso do alto nao aparece", janela.btAtualizacao.Visibility != Visibility.Visible);
                Atualizacoes.Simular(Atualizacoes.Estado.Baixando, achado, progresso: 0.42);
                Conferir("atualizacoes: baixando, o aviso do alto mostra a versao e quanto falta",
                    janela.btAtualizacao.Visibility == Visibility.Visible && janela.txtAtualizacao.Text.Contains("Alpha 26")
                    && janela.txtAtualizacao.Text.Contains("42"), janela.txtAtualizacao.Text);
                Atualizacoes.Simular(Atualizacoes.Estado.Pronta, achado, baixado, 1);
                Conferir("atualizacoes: baixada, o aviso diz que esta pronta", janela.txtAtualizacao.Text == "Alpha 26: pronta para instalar",
                    janela.txtAtualizacao.Text);

                Atualizacoes.Simular(Atualizacoes.Estado.Baixando, achado, progresso: 0.42);
                var ver = new JanelaAtualizacao(janela, JanelaAtualizacao.Modo.Ver);
                try
                {
                    ver.Show();
                    Conferir("atualizacoes: a janela mostra a versao, as novidades, o download e os dois botoes",
                        ver.TituloParaTeste == "Alpha 26 disponível" && ver.NotasParaTeste?.Contains("• Frames") == true
                        && ver.StatusParaTeste?.StartsWith("Baixando: 42% de 0") == true && ver.StatusParaTeste.EndsWith("3 MB")
                        && ver.BotaoParaTeste == "Atualizar agora" && ver.OutroParaTeste == "Depois",
                        $"{ver.TituloParaTeste} | {ver.StatusParaTeste} | {ver.BotaoParaTeste}");
                    FotografarJanela(ver, Path.Combine(Path.GetTempPath(), "circuitsketch-teste-atualizacao.png"));
                    Atualizacoes.Simular(Atualizacoes.Estado.Erro, achado, erro: "falhou (teste)");
                    Conferir("atualizacoes: download que falhou mostra o erro e o \"Tentar de novo\"",
                        ver.StatusParaTeste == "falhou (teste)" && ver.BotaoParaTeste == "Tentar de novo", ver.StatusParaTeste);
                }
                finally { ver.Close(); }

                Atualizacoes.Simular(Atualizacoes.Estado.Baixando, achado, progresso: 0.5);
                var fechando = new JanelaAtualizacao(janela, JanelaAtualizacao.Modo.Fechando);
                Conferir("atualizacoes: fechando com o download no meio, a janela espera e deixa fechar sem atualizar",
                    fechando.TituloParaTeste == "Instalando a Alpha 26 ao fechar" && fechando.BotaoParaTeste is null
                    && fechando.OutroParaTeste == "Fechar sem atualizar" && fechando.NotasParaTeste is null);
                fechando.Close();

                Atualizacoes.Simular(Atualizacoes.Estado.Procurando, null);
                var procurando = new JanelaAtualizacao(janela, JanelaAtualizacao.Modo.Ver);
                Atualizacoes.Simular(Atualizacoes.Estado.EmDia, null);
                Conferir("atualizacoes: pelo menu, \"procurando\" vira \"esta atualizado\" na mesma janela",
                    procurando.TituloParaTeste == "O CircuitSketch está atualizado" && procurando.BotaoParaTeste is null
                    && procurando.OutroParaTeste == "OK");
                procurando.Close();
            }
            finally
            {
                Atualizacoes.Zerar();
                janela.TesteParar();
                try { janela.Close(); } catch (InvalidOperationException) { }
            }
        }
        finally
        {
            Atualizacoes.Zerar();
            Atualizacoes.AoMudar = null;
            Atualizacoes.NovoNavegador = navegadorAntes;
            Atualizacoes.Pasta = pastaAntes;
            Atualizacoes.VersaoAtual = versaoAntes;
            Atualizacoes.Executar = executarAntes;
            try { Directory.Delete(pasta, true); } catch { }
        }
    }
}
