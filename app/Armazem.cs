using System.IO;

namespace Simulador.App;

/// <summary>
/// A pasta onde o programa guarda o que é da pessoa: preferências,
/// recentes, miniaturas, bibliotecas, chips próprios e o espelho do
/// monitor serial para a Arduino IDE.
///
/// Fica em %APPDATA%\CircuitSketch -- menos no autoteste e no passeio
/// pela interface. Os dois rodam o programa de verdade e mexem em tudo
/// isso: o passeio abre nove demos, e cada uma entrava na lista de
/// recentes de quem o rodou, empurrando para fora os projetos de
/// verdade e deixando a "última sessão" apontada para um arquivo
/// temporário. Nesses modos a pasta é uma temporária, jogada fora no
/// fim.
///
/// Antes cada arquivo montava o próprio caminho, e por isso não havia
/// UM lugar para trocar. Agora todos perguntam aqui.
/// </summary>
public static class Armazem
{
    static string isolada;

    public static string Pasta => isolada ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "CircuitSketch");

    /// <summary>Está usando a pasta temporária desta execução?</summary>
    public static bool Isolado => isolada is not null;

    /// <summary>
    /// Troca a pasta por uma temporária, só nesta execução. Tem que ser
    /// chamado antes de qualquer leitura: as preferências ficam em
    /// memória depois da primeira.
    /// </summary>
    public static void Isolar()
    {
        isolada = Path.Combine(Path.GetTempPath(),
                               $"circuitsketch-isolado-{Environment.ProcessId}");
        Directory.CreateDirectory(isolada);
    }

    /// <summary>Apaga a pasta temporária, se esta execução usou uma.</summary>
    public static void Descartar()
    {
        if (isolada is null) return;
        try { Directory.Delete(isolada, true); } catch { /* fica no temp, que o Windows limpa */ }
    }
}
