using System.IO;

namespace Simulador.App;

/// <summary>
/// Espelha o monitor serial num arquivo, para a Arduino IDE ler.
///
/// A IDE tem um monitor serial próprio, e com a placa fantasma ele
/// abria vazio: não há porta de verdade do outro lado. Quem manda o
/// sketch pela IDE via as mensagens só aqui dentro do CircuitSketch, e
/// o `Serial.println` do exercício -- que é como o aluno depura --
/// simplesmente não chegava onde ele estava olhando.
///
/// O caminho é um arquivo, e não um named pipe ou um socket, por dois
/// motivos práticos:
///
/// - **Dá para depurar com o Bloco de Notas.** Quando a integração não
///   funciona no laboratório, abrir o arquivo já diz de que lado está o
///   problema -- e a alternativa era adivinhar.
/// - **Nenhum dos dois lados precisa do outro vivo.** O simulador
///   escreve mesmo sem a IDE aberta, e o monitor da IDE pode abrir no
///   meio da execução e continuar de onde o arquivo está.
///
/// Só o sentido simulador → IDE existe. Mandar texto da IDE para o
/// sketch pediria `Serial.read`, que o motor ainda não tem.
/// </summary>
public static class MonitorIde
{
    static string Pasta => Armazem.Pasta;

    /// <summary>O arquivo que o monitor da IDE acompanha.</summary>
    public static string Arquivo => Path.Combine(Pasta, "serial.txt");

    /// <summary>
    /// Zera o arquivo. Chamado ao rodar: execução nova, monitor limpo,
    /// igual ao que a IDE faz quando a placa reinicia.
    /// </summary>
    public static void Comecar()
    {
        try
        {
            Directory.CreateDirectory(Pasta);
            File.WriteAllText(Arquivo, "");
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
}
