using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;

namespace Simulador.App;

/// <summary>
/// O que a placa de verdade precisa além de compilar: o compilador do
/// Arduino quando a IDE não está instalada, e o monitor serial da placa
/// ligada na USB.
/// </summary>
public static partial class PlacaDeVerdade
{
    /// <summary>De onde vem o arduino-cli oficial (o "latest" redireciona para a versão).</summary>
    public const string EnderecoDoCli = "https://downloads.arduino.cc/arduino-cli/arduino-cli_latest_Windows_64bit.zip";

    /// <summary>Para o teste: troca quem baixa.</summary>
    internal static Func<HttpClient> NovoNavegador = () => new HttpClient { Timeout = TimeSpan.FromMinutes(10) };

    /// <summary>
    /// Baixa o arduino-cli oficial de downloads.arduino.cc, confere o SHA-256
    /// com a lista que a Arduino publica junto e põe o executável em
    /// <see cref="CliBaixado"/>. Sem a Arduino IDE, é o que faz o visto e a
    /// seta funcionarem: os núcleos e as bibliotecas ele mesmo instala depois.
    /// </summary>
    public static async Task<bool> BaixarCli(Action<string> linha, CancellationToken cancelar)
    {
        string pasta = Path.GetDirectoryName(CliBaixado);
        string zip = Path.Combine(Path.GetTempPath(), $"circuitsketch-arduino-cli-{Environment.ProcessId}.zip");
        try
        {
            using var navegador = NovoNavegador();
            linha?.Invoke("> " + EnderecoDoCli);

            using var resposta = await navegador.GetAsync(EnderecoDoCli, HttpCompletionOption.ResponseHeadersRead, cancelar);
            resposta.EnsureSuccessStatusCode();

            // "arduino-cli_1.6.0_Windows_64bit.zip": a versão está no nome final.
            string nomeFinal = Path.GetFileName(resposta.RequestMessage?.RequestUri?.AbsolutePath ?? "");
            var versao = System.Text.RegularExpressions.Regex.Match(nomeFinal, @"^arduino-cli_(.+)_Windows_64bit\.zip$");

            long total = resposta.Content.Headers.ContentLength ?? 0;
            await using (var origem = await resposta.Content.ReadAsStreamAsync(cancelar))
            await using (var destino = File.Create(zip))
            {
                var buffer = new byte[81920];
                long feito = 0;
                int ultimoAviso = -1;
                int lidos;
                while ((lidos = await origem.ReadAsync(buffer, cancelar)) > 0)
                {
                    await destino.WriteAsync(buffer.AsMemory(0, lidos), cancelar);
                    feito += lidos;
                    int porCento = total > 0 ? (int)(feito * 100 / total) : -1;
                    if (porCento >= 0 && porCento / 10 != ultimoAviso)
                    {
                        ultimoAviso = porCento / 10;
                        linha?.Invoke(Idioma.T("baixando o compilador do Arduino… {0}% de {1} MB", porCento, (total / 1_048_576.0).ToString("0.0")));
                    }
                }
            }

            // A soma que a Arduino publica: "<sha256>  arduino-cli_X_Windows_64bit.zip".
            if (versao.Success)
            {
                string somas = await navegador.GetStringAsync(
                    $"https://downloads.arduino.cc/arduino-cli/{versao.Groups[1].Value}-checksums.txt", cancelar);
                string esperado = somas.Replace("\r", "").Split('\n')
                    .Select(l => l.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                    .FirstOrDefault(p => p.Length == 2 && p[1] == nomeFinal)?[0];
                string obtido;
                await using (var lido = File.OpenRead(zip))
                    obtido = Convert.ToHexStringLower(await SHA256.HashDataAsync(lido, cancelar));
                if (esperado is null || !string.Equals(esperado, obtido, StringComparison.OrdinalIgnoreCase))
                {
                    linha?.Invoke(Idioma.T("o arquivo baixado não confere com a soma publicada pela Arduino: nada foi instalado."));
                    return false;
                }
                linha?.Invoke("SHA-256 " + obtido + " OK");
            }

            Directory.CreateDirectory(pasta);
            using (var arquivo = ZipFile.OpenRead(zip))
            {
                var exe = arquivo.Entries.FirstOrDefault(e => e.Name.Equals("arduino-cli.exe", StringComparison.OrdinalIgnoreCase));
                if (exe is null)
                {
                    linha?.Invoke(Idioma.T("o arquivo baixado não tem o arduino-cli.exe."));
                    return false;
                }
                exe.ExtractToFile(CliBaixado, overwrite: true);
            }

            EsquecerCli();
            linha?.Invoke(Idioma.T("compilador do Arduino instalado em {0}", CliBaixado));
            return Cli is not null;
        }
        catch (OperationCanceledException)
        {
            linha?.Invoke(Idioma.T("download cancelado."));
            return false;
        }
        catch (Exception e) when (e is HttpRequestException or IOException or InvalidDataException or UnauthorizedAccessException)
        {
            linha?.Invoke(Idioma.T("não consegui baixar o compilador do Arduino: {0}", e.Message));
            return false;
        }
        finally
        {
            try { File.Delete(zip); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>
    /// A velocidade da serial que o sketch abre (o primeiro Serial.begin), ou
    /// a padrão da família quando ele não abre nenhuma.
    /// </summary>
    public static int BaudDoSketch(string sketch, bool de8Bits)
    {
        var m = System.Text.RegularExpressions.Regex.Match(sketch ?? "", @"\bSerial\s*\.\s*begin\s*\(\s*(\d+)");
        return m.Success && int.TryParse(m.Groups[1].Value, out int baud) && baud > 0 ? baud : de8Bits ? 9600 : 115200;
    }

    /// <summary>
    /// O monitor serial da placa de verdade: o "arduino-cli monitor" numa
    /// porta, com o que chega repassado aos pedaços (a placa nem sempre
    /// termina a linha) e o que a pessoa digita mandado para a placa.
    /// </summary>
    public sealed class Monitor : IDisposable
    {
        readonly Process processo;
        readonly CancellationTokenSource fim = new();

        public string Porta { get; }
        public int Baud { get; }

        /// <summary>Terminou (a placa saiu da USB, a porta foi tomada, ou fechou).</summary>
        public event Action<int> Terminou;

        Monitor(Process processo, string porta, int baud)
        {
            this.processo = processo;
            Porta = porta;
            Baud = baud;
        }

        /// <summary>Abre o monitor; null sem o arduino-cli ou se o processo não subiu.</summary>
        public static Monitor Abrir(string porta, int baud, Action<string> chegou)
        {
            if (Cli is null || string.IsNullOrEmpty(porta)) return null;

            var inicio = Inicio(["monitor", "-p", porta, "--config", $"baudrate={baud}", "--quiet"]);
            inicio.RedirectStandardInput = true;
            Process processo;
            try { processo = Process.Start(inicio); }
            catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
            {
                chegou?.Invoke(Idioma.T("nao consegui rodar o arduino-cli: {0}", e.Message) + "\n");
                return null;
            }
            if (processo is null) return null;

            var monitor = new Monitor(processo, porta, baud);
            _ = monitor.Ler(processo.StandardOutput, chegou);
            _ = monitor.Ler(processo.StandardError, chegou);
            _ = Task.Run(async () =>
            {
                try { await processo.WaitForExitAsync(monitor.fim.Token); }
                catch (OperationCanceledException) { return; }
                monitor.Terminou?.Invoke(processo.ExitCode);
            });
            return monitor;
        }

        async Task Ler(StreamReader leitor, Action<string> chegou)
        {
            var buffer = new char[1024];
            try
            {
                int lidos;
                while ((lidos = await leitor.ReadAsync(buffer.AsMemory(), fim.Token)) > 0)
                    chegou?.Invoke(SemCores(new string(buffer, 0, lidos)));
            }
            catch (Exception e) when (e is OperationCanceledException or IOException or ObjectDisposedException) { }
        }

        /// <summary>Manda o texto para a placa (com o fim de linha que vier junto).</summary>
        public void Enviar(string texto)
        {
            try
            {
                var bytes = Encoding.UTF8.GetBytes(texto);
                processo.StandardInput.BaseStream.Write(bytes);
                processo.StandardInput.BaseStream.Flush();
            }
            catch (Exception e) when (e is IOException or ObjectDisposedException or InvalidOperationException) { }
        }

        public bool Aberto
        {
            get { try { return !processo.HasExited; } catch (InvalidOperationException) { return false; } }
        }

        /// <summary>Fecha o monitor e solta a porta (para a gravação poder usar).</summary>
        public void Dispose()
        {
            fim.Cancel();
            try { if (!processo.HasExited) processo.Kill(entireProcessTree: true); }
            catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) { }
            try { processo.WaitForExit(3000); } catch (InvalidOperationException) { }
            processo.Dispose();
        }
    }
}
