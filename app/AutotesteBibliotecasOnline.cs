using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Simulador.App;

public static partial class Autoteste
{
    sealed class ServidorBibliotecasFalso(byte[] indice, Dictionary<string, byte[]> arquivos) : HttpMessageHandler
    {
        public bool SemInternet;
        public int Pedidos;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage pedido, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Pedidos++;
            if (SemInternet) throw new HttpRequestException("Sem internet (teste)");
            byte[] dados = pedido.RequestUri.AbsoluteUri == BibliotecasOnline.UrlDoIndice
                ? indice : arquivos[pedido.RequestUri.AbsoluteUri];
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                { Content = new ByteArrayContent(dados), RequestMessage = pedido });
        }
    }

    sealed class ProgressoBibliotecas : IProgress<double>
    {
        public readonly List<double> Valores = [];
        public void Report(double valor) => Valores.Add(valor);
    }

    static void Bibliotecas_Online() => Task.Run(Bibliotecas_OnlineAsync).GetAwaiter().GetResult();

    static async Task Bibliotecas_OnlineAsync()
    {
        Conferir("online: armazem isolado", Armazem.Isolado, Armazem.Pasta);
        if (!Armazem.Isolado) return;
        byte[] Zip(string nome)
        {
            using var memoria = new MemoryStream();
            using (var zip = new ZipArchive(memoria, ZipArchiveMode.Create, true))
            {
                using (var texto = new StreamWriter(zip.CreateEntry(nome + "/library.properties").Open()))
                    texto.Write("name=" + nome + "\nversion=1.0.0\n");
                using (var texto = new StreamWriter(zip.CreateEntry(nome + "/src/" + nome + ".h").Open()))
                    texto.Write("#pragma once\n");
            }
            return memoria.ToArray();
        }
        var arquivos = new Dictionary<string, byte[]>();
        object Registro(string nome, string versao, string cabecalho, string dependencia, string frase = "")
        {
            string url = "https://downloads.arduino.cc/libraries/" + Uri.EscapeDataString(nome) + ".zip";
            byte[] zip = Zip(nome);
            arquivos[url] = zip;
            return new { name = nome, version = versao, author = "Teste", sentence = frase,
                paragraph = "Descrição", website = "https://example.invalid", category = "Other",
                architectures = new[] { "*" }, providesIncludes = cabecalho is null ? Array.Empty<string>() : new[] { cabecalho },
                url, archiveFileName = nome + ".zip", size = zip.Length,
                checksum = "SHA-256:" + Convert.ToHexString(SHA256.HashData(zip)),
                dependencies = dependencia is null ? [] : new[] { new { name = dependencia, version = ">=1.0.0" } } };
        }
        // Cadeia Acao -> AcaoExtra -> Base; Base -> AcaoExtra forma um ciclo.
        var registros = new[] {
            Registro("Acao", "1.9.2", "Acao.h", "AcaoExtra"),
            Registro("Acao", "inválida", "Acao.h", "AcaoExtra"),
            Registro("Acao", "1.10.0-beta.2", "Acao.h", "AcaoExtra"),
            Registro("Acao", "1.10.0", "Acao.h", "AcaoExtra"),
            Registro("AcaoExtra", "1.0.0", "Extra.h", "Base"),
            Registro("Base", "1.0.0", "AcaoBase.h", "AcaoExtra"),
            Registro("Bounce2", "1.0.0", null, null, "Uma ação para testar") };
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(new { libraries = registros });
        using var entrada = new MemoryStream(json);
        var indice = IndiceDeBibliotecas.Ler(entrada);
        var alvo = indice.Buscar("Acao")[0];
        Conferir("online: versao semantica e anteriores", alvo.Versao == "1.10.0"
            && alvo.OutrasVersoes.Select(b => b.Versao).SequenceEqual(new[] { "1.10.0-beta.2", "1.9.2", "inválida" }), alvo.Versao);
        Conferir("online: versao sem o patch (\"1.0\", como no indice de verdade) compara como 1.0.0",
            IndiceDeBibliotecas.CompararVersoes("1.0.1", "1.0") > 0 && IndiceDeBibliotecas.CompararVersoes("1.10", "1.9.9") > 0
            && IndiceDeBibliotecas.CompararVersoes("2", "1.99") > 0 && IndiceDeBibliotecas.CompararVersoes("1.0", "1.0.0") == 0, "1.0 < 1.0.1 < 1.9.9 < 1.10 < 2");
        Conferir("online: pre-release numerico", IndiceDeBibliotecas.CompararVersoes("1.0.0-beta.10", "1.0.0-beta.2") > 0, "beta.10 > beta.2");
        string busca = string.Join(",", indice.Buscar("AÇÃO").Select(b => b.Nome));
        Conferir("online: busca ordenada sem acento", busca == "Acao,AcaoExtra,Base,Bounce2", busca);
        Conferir("online: busca contem nome", indice.Buscar("cao")[0].Nome == "Acao", "Acao");
        Conferir("online: limite", indice.Buscar("", 2).Count == 2 && indice.Buscar("", 0).Count == 0, "2/0");
        Conferir("online: quem tem includes", indice.QuemTem("acaobase.H").First().Nome == "Base", "Base");
        Conferir("online: quem tem fallback", indice.QuemTem("Bounce2.h").First().Nome == "Bounce2", "Bounce2");
        Conferir("online: cabecalho ausente", indice.QuemTem("Ausente.h").Count == 0, "0");

        // O indice de verdade: menos da metade lista os .h, e ha quem liste o
        // .h de outra biblioteca que vem embutida. O dono pelo nome vem antes.
        using (var real = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(new
        {
            libraries = new object[]
            {
                new { name = "EasyMqtt", version = "1.0.0", providesIncludes = new[] { "EasyMqtt.h", "PubSubClient.h" }, url = "https://downloads.arduino.cc/libraries/e.zip", size = 1, checksum = "" },
                new { name = "PubSubClient", version = "2.8.0", url = "https://downloads.arduino.cc/libraries/p.zip", size = 1, checksum = "" },
                new { name = "TBPubSubClient", version = "2.12.1", repository = "https://github.com/thingsboard/pubsubclient.git", url = "https://downloads.arduino.cc/libraries/t.zip", size = 1, checksum = "" },
                new { name = "TinyGPSPlus", version = "1.0.3", url = "https://downloads.arduino.cc/libraries/g.zip", size = 1, checksum = "" },
                new { name = "DHT sensor library", version = "1.4.7", url = "https://downloads.arduino.cc/libraries/d.zip", size = 1, checksum = "" },
                new { name = "Adafruit BMP280 Library", version = "3.0.0", url = "https://downloads.arduino.cc/libraries/b.zip", size = 1, checksum = "" },
            },
        })))
        {
            var comoNaInternet = IndiceDeBibliotecas.Ler(real);
            string Dono(string h) => comoNaInternet.QuemTem(h).FirstOrDefault()?.Nome ?? "ninguem";
            string vistos = $"{Dono("PubSubClient.h")} / {Dono("TinyGPS++.h")} / {Dono("DHT.h")} / {Dono("Adafruit_BMP280.h")}";
            Conferir("online: sem providesIncludes, o .h acha o dono pelo nome (PubSubClient antes do fork e de quem o embute; TinyGPS++ -> TinyGPSPlus; DHT.h -> DHT sensor library; Adafruit_BMP280.h -> Adafruit BMP280 Library)",
                vistos == "PubSubClient / TinyGPSPlus / DHT sensor library / Adafruit BMP280 Library", vistos);
        }
        var ordem = indice.ParaInstalar(alvo, new HashSet<string>());
        string nomes = string.Join(",", ordem.Select(b => b.Nome));
        Conferir("online: cadeia e ciclo sem repetir", nomes == "Base,AcaoExtra,Acao", nomes);
        Conferir("online: pula instalada sem diferenciar caixa",
            indice.ParaInstalar(alvo, new HashSet<string> { "acaoextra" }).Count == 1, "1");
        Conferir("online: pula interna pelo cabecalho",
            indice.ParaInstalar(alvo with { Cabecalhos = new[] { "Servo.h" } }, new HashSet<string>()).Count == 0, "0");
        Conferir("online: preserva metadados", alvo.Autor == "Teste" && alvo.Dependencias[0].Versao == ">=1.0.0"
            && alvo.Architectures[0] == "*", alvo.Autor);
        using var compactado = new MemoryStream();
        using (var gzip = new GZipStream(compactado, CompressionMode.Compress, true)) gzip.Write(json);
        using var servidor = new ServidorBibliotecasFalso(compactado.ToArray(), arquivos);
        using var http = new HttpClient(servidor);
        async Task Recusa(string nome, Func<Task> acao, Type tipo)
        {
            Exception erro = null;
            try { await acao(); } catch (Exception e) { erro = e; }
            Conferir(nome, erro is not null && tipo.IsInstanceOfType(erro), erro?.ToString() ?? "nao recusou");
        }
        servidor.SemInternet = true;
        await Recusa("online: sem rede nem cache", () => BibliotecasOnline.CarregarAsync(http, false, default), typeof(InvalidOperationException));
        servidor.SemInternet = false;
        var carregado = await BibliotecasOnline.CarregarAsync(http, true, default);
        Conferir("online: carrega gzip", carregado.Buscar("Acao")[0].Versao == "1.10.0", "1.10.0");
        int pedidos = servidor.Pedidos;
        servidor.SemInternet = true;
        await BibliotecasOnline.CarregarAsync(http, false, default);
        Conferir("online: cache sem pedir rede", servidor.Pedidos == pedidos, servidor.Pedidos.ToString());
        await BibliotecasOnline.CarregarAsync(http, true, default);
        Conferir("online: falha de rede usa cache", servidor.Pedidos == pedidos + 1, servidor.Pedidos.ToString());
        servidor.SemInternet = false;
        await Recusa("online: SHA errado", () => BibliotecasOnline.BaixarEInstalarAsync(http, alvo with { Checksum = "SHA-256:00" }, null, default), typeof(InvalidDataException));
        Conferir("online: SHA errado nao instala", !Directory.Exists(Path.Combine(Acervo.Pasta, alvo.Nome)), Acervo.Pasta);
        await Recusa("online: tamanho errado", () => BibliotecasOnline.BaixarEInstalarAsync(http, alvo with { Size = alvo.Size + 1 }, null, default), typeof(InvalidDataException));
        pedidos = servidor.Pedidos;
        await Recusa("online: host externo", () => BibliotecasOnline.BaixarEInstalarAsync(http, alvo with { Url = "https://downloads.arduino.cc.evil.invalid/a.zip" }, null, default), typeof(InvalidOperationException));
        Conferir("online: host recusado antes da rede", pedidos == servidor.Pedidos, servidor.Pedidos.ToString());
        using var cancelamento = new CancellationTokenSource();
        cancelamento.Cancel();
        await Recusa("online: cancelamento", () => BibliotecasOnline.BaixarEInstalarAsync(http, alvo, null, cancelamento.Token), typeof(OperationCanceledException));
        await Recusa("online: cache respeita cancelamento", () => BibliotecasOnline.CarregarAsync(http, false, cancelamento.Token), typeof(OperationCanceledException));
        var progresso = new ProgressoBibliotecas();
        var instaladas = await BibliotecasOnline.InstalarComDependenciasAsync(http, indice, alvo, new HashSet<string>(), progresso, default);
        Conferir("online: instala cadeia", instaladas.SequenceEqual(new[] { "Base", "AcaoExtra", "Acao" }), string.Join(",", instaladas));
        Conferir("online: destino isolado", instaladas.All(n => File.Exists(Path.Combine(Acervo.Pasta, n, "src", n + ".h"))), Acervo.Pasta);
        Conferir("online: progresso", progresso.Valores.First() == 0 && progresso.Valores.Last() == 1
            && progresso.Valores.All(p => p >= 0 && p <= 1), string.Join(",", progresso.Valores));
        Conferir("online: cache sem temporarios", !Directory.EnumerateFiles(BibliotecasOnline.PastaDoCache, "*.tmp").Any(), "0");
    }
}
