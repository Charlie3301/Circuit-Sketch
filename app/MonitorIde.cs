using System.IO;

namespace Simulador.App;

/// <summary>
/// O monitor serial da Arduino IDE ligado ao simulador, nos dois sentidos.
///
/// A IDE tem um monitor serial próprio, e com a placa fantasma ele
/// abria vazio: não há porta de verdade do outro lado. O plugin
/// (plataforma\tools\monitor-cano.ps1) faz a ponte por dois arquivos:
///
/// - <c>serial.txt</c>: o que o sketch imprime. O simulador acrescenta, o
///   plugin despeja no monitor da IDE.
/// - <c>entrada.txt</c>: o que a pessoa digita no monitor da IDE. O plugin
///   acrescenta, o simulador lê o que é novo e entrega ao Serial da placa
///   que está no editor (o mesmo caminho do campo "Enviar" daqui).
///
/// Arquivo, e não pipe ou socket, por dois motivos práticos: dá para
/// depurar com o Bloco de Notas, e nenhum dos dois lados precisa do outro
/// vivo -- o monitor da IDE pode abrir no meio da execução.
/// </summary>
public static class MonitorIde
{
    static string Pasta => Armazem.Pasta;

    /// <summary>O arquivo que o monitor da IDE acompanha.</summary>
    public static string Arquivo => Path.Combine(Pasta, "serial.txt");

    /// <summary>O arquivo onde o monitor da IDE deixa o que a pessoa digitou.</summary>
    public static string Entrada => Path.Combine(Pasta, "entrada.txt");

    /// <summary>Até onde o <see cref="Entrada"/> já foi entregue ao sketch.</summary>
    static long lidoDaEntrada;

    /// <summary>
    /// Zera os dois arquivos. Chamado ao rodar: execução nova, monitor
    /// limpo, igual ao que a IDE faz quando a placa reinicia. O que foi
    /// digitado antes de rodar não vale para a execução nova.
    /// </summary>
    public static void Comecar()
    {
        try
        {
            Directory.CreateDirectory(Pasta);
            File.WriteAllText(Arquivo, "");
            File.WriteAllText(Entrada, "");
            lidoDaEntrada = 0;
        }
        catch { /* sem escrita em %APPDATA%: o simulador segue igual */ }
    }

    /// <summary>
    /// Acrescenta as linhas que ainda não foram escritas.
    ///
    /// Falha em silêncio de propósito: o monitor da IDE é um extra, e
    /// derrubar a simulação porque o arquivo está travado seria trocar
    /// um problema pequeno por um grande.
    /// </summary>
    public static void Escrever(IEnumerable<string> linhas)
    {
        try
        {
            Directory.CreateDirectory(Pasta);
            File.AppendAllLines(Arquivo, linhas);
        }
        catch { }
    }

    /// <summary>
    /// Os bytes que chegaram do monitor da IDE desde a última vez, ou null.
    /// Já vêm com o fim de linha que a pessoa escolheu lá ("Nova linha",
    /// "Ambos NL e CR"...): vão para o sketch do jeito que estão.
    /// </summary>
    public static byte[] LerEntrada()
    {
        try
        {
            var info = new FileInfo(Entrada);
            if (!info.Exists) { lidoDaEntrada = 0; return null; }
            if (info.Length < lidoDaEntrada) lidoDaEntrada = 0;   // alguém zerou
            if (info.Length == lidoDaEntrada) return null;

            using var arquivo = new FileStream(Entrada, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            arquivo.Seek(lidoDaEntrada, SeekOrigin.Begin);
            var bytes = new byte[info.Length - lidoDaEntrada];
            int lidos = arquivo.Read(bytes, 0, bytes.Length);
            lidoDaEntrada += lidos;
            return lidos == bytes.Length ? bytes : bytes[..lidos];
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }
}
