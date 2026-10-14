using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace Simulador.App;

/// <summary>
/// O gerenciador de bibliotecas pela janela (Alpha 24): a aba "Na internet"
/// mostra quem fornece o #include que o sketch pede, busca, e instala com a
/// dependencia -- tudo por um servidor de mentira, sem internet.
/// </summary>
public static partial class Autoteste
{
    static void Bibliotecas_Janela()
    {
        saida.AppendLine("gerenciador de bibliotecas (a janela)");
        if (!Armazem.Isolado) { Conferir("janela de bibliotecas: armazem isolado", false, Armazem.Pasta); return; }

        var arquivos = new Dictionary<string, byte[]>();
        object Registro(string nome, string cabecalho, string dependencia)
        {
            using var memoria = new MemoryStream();
            using (var zip = new ZipArchive(memoria, ZipArchiveMode.Create, true))
            {
                using (var texto = new StreamWriter(zip.CreateEntry(nome + "/library.properties").Open()))
                    texto.Write($"name={nome}\nversion=2.1\n");
                using (var texto = new StreamWriter(zip.CreateEntry(nome + "/src/" + cabecalho).Open()))
                    texto.Write("#pragma once\nclass " + nome + " { public: int ler() { return 42; } };\n");
            }
            byte[] bytes = memoria.ToArray();
            string url = $"https://downloads.arduino.cc/libraries/teste/{nome}-2.1.zip";
            arquivos[url] = bytes;
            return new
            {
                name = nome, version = "2.1", author = "Kit", sentence = $"O sensor {nome} de teste.", paragraph = "",
                website = "", category = "Sensors", architectures = new[] { "*" }, providesIncludes = new[] { cabecalho },
                url, archiveFileName = $"{nome}-2.1.zip", size = bytes.Length,
                checksum = "SHA-256:" + Convert.ToHexString(SHA256.HashData(bytes)),
                dependencies = dependencia is null ? [] : new[] { new { name = dependencia } },
            };
        }
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(new
        {
            libraries = new[] { Registro("SensorJanela", "SensorJanela.h", "BaseJanela"), Registro("BaseJanela", "BaseJanela.h", null) },
        });
        using var gz = new MemoryStream();
        using (var compactar = new GZipStream(gz, CompressionMode.Compress, true)) compactar.Write(json);
        using var servidor = new ServidorBibliotecasFalso(gz.ToArray(), arquivos);

        var antes = JanelaBibliotecas.NovoNavegador;
        JanelaBibliotecas.TrocarNavegadorParaTeste(() => new HttpClient(servidor, disposeHandler: false));
        const string Sketch = "#include <SensorJanela.h>\nSensorJanela s;\nvoid setup() {}\nvoid loop() {}\n";
        var janela = new JanelaBibliotecas(null, Sketch) { ShowActivated = false };
        try
        {
            var carregar = janela.AbrirInternetParaTeste();
            EsperarPor(() => carregar.IsCompleted, 15000);
            Conferir("janela de bibliotecas: o indice da internet carrega (do servidor de mentira)",
                carregar.IsCompletedSuccessfully && janela.SituacaoParaTeste.Contains("2 bibliotecas"), janela.SituacaoParaTeste);
            Conferir("janela de bibliotecas: sem busca, mostra quem fornece o #include que o sketch pede",
                janela.ListadasParaTeste == 1, $"{janela.ListadasParaTeste} listada(s)");

            janela.BuscarParaTeste("BaseJanela.h");
            Conferir("janela de bibliotecas: buscar pelo #include acha a biblioteca", janela.ListadasParaTeste == 1, $"{janela.ListadasParaTeste}");
            janela.BuscarParaTeste("teste SensorJanela");   // duas palavras, fora de ordem
            Conferir("janela de bibliotecas: buscar com varias palavras (fora de ordem) acha pelo nome e pela descricao", janela.ListadasParaTeste == 1, $"{janela.ListadasParaTeste}");

            var instalar = janela.InstalarParaTeste("SensorJanela");
            EsperarPor(() => instalar.IsCompleted, 15000);
            Conferir("janela de bibliotecas: instalar baixa a dependencia antes (BaseJanela, SensorJanela)",
                janela.InstaladasParaTeste.SequenceEqual(["BaseJanela", "SensorJanela"]), string.Join(",", janela.InstaladasParaTeste) + " | " + janela.SituacaoParaTeste);
            Conferir("janela de bibliotecas: as duas caem na pasta do acervo",
                File.Exists(Path.Combine(Acervo.Pasta, "SensorJanela", "src", "SensorJanela.h"))
                && File.Exists(Path.Combine(Acervo.Pasta, "BaseJanela", "src", "BaseJanela.h")), Acervo.Pasta);
            Conferir("janela de bibliotecas: depois de instalar, o sketch nao pede mais nada",
                Acervo.SemDono(Sketch).Count == 0, string.Join(",", Acervo.SemDono(Sketch)));
        }
        finally
        {
            JanelaBibliotecas.TrocarNavegadorParaTeste(antes);
            try { janela.Close(); } catch (InvalidOperationException) { }
        }
    }
}
