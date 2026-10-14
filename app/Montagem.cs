namespace Simulador.App;

/// <summary>
/// Junta o sketch com os arquivos do projeto antes de compilar.
///
/// Um projeto de verdade nao cabe num arquivo so. O laptimer da lista do
/// professor abre com
///
/// <code>
///   #include "config.h"
///   #include "app_state.h"
/// </code>
///
/// e usa, tres linhas depois, uma variavel que esta dentro de um desses
/// arquivos. O simulador tinha as abas para criar esses arquivos --
/// mas na hora de rodar compilava so o sketch, e a variavel "nao existia".
///
/// A regra aqui e a do proprio C: aspas procuram ao LADO do sketch, e
/// os sinais de menor e maior procuram nas bibliotecas. Entao so o
/// <c>#include "..."</c> puxa arquivo do projeto; <c>#include &lt;WiFi.h&gt;</c>
/// continua sendo com o simulador.
/// </summary>
public static class Montagem
{
    /// <summary>Teto de arquivos juntados, contra include circular.</summary>
    const int Limite = 32;

    /// <summary>
    /// O sketch com os arquivos que ele inclui colados na frente.
    ///
    /// A ordem importa: o que foi incluido vem ANTES, porque e onde as
    /// variaveis e funcoes que o sketch usa estao declaradas.
    /// </summary>
    public static string Montar(string sketch, IDictionary<string, string> arquivos)
    {
        if (string.IsNullOrEmpty(sketch) || arquivos is null || arquivos.Count == 0)
            return sketch;

        var jaEntrou = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var partes = new List<string>();

        Juntar(sketch, arquivos, jaEntrou, partes, 0);

        // Os .cpp e .ino do projeto entram mesmo sem ninguem os incluir.
        //
        // E o que a Arduino IDE faz: ela compila TODO arquivo da pasta
        // do sketch, e nao so os que aparecem num #include. E disso que
        // depende a dupla classica -- "extern GnssState gnss;" no .h,
        // "GnssState gnss;" no .cpp. Sem o .cpp, a variavel e prometida
        // e nunca criada.
        foreach (var (nome, conteudo) in arquivos.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            if (!EhCodigo(nome) || !jaEntrou.Add(nome)) continue;

            Juntar(conteudo, arquivos, jaEntrou, partes, 1);
            partes.Add(conteudo);
        }

        partes.Add(sketch);

        return string.Join(Environment.NewLine, partes);
    }

    static void Juntar(string texto, IDictionary<string, string> arquivos,
                       HashSet<string> jaEntrou, List<string> partes, int fundo)
    {
        if (fundo > 8 || partes.Count > Limite) return;

        foreach (string nome in IncluidosDoProjeto(texto, arquivos))
        {
            // Cada arquivo entra UMA vez, como o include guard faz no C.
            // Sem isto, dois arquivos que se incluem um ao outro
            // encheriam a memoria antes de dar qualquer erro.
            if (!jaEntrou.Add(nome)) continue;

            string conteudo = arquivos[nome];

            // Profundidade primeiro: o que "config.h" inclui tem que vir
            // antes do proprio "config.h".
            Juntar(conteudo, arquivos, jaEntrou, partes, fundo + 1);
            partes.Add(conteudo);
        }
    }

    /// <summary>Arquivo que o compilador levaria junto, e nao so incluiria.</summary>
    static bool EhCodigo(string nome)
    {
        string ext = System.IO.Path.GetExtension(nome).ToLowerInvariant();
        return ext is ".cpp" or ".c" or ".ino" or ".cc";
    }

    /// <summary>
    /// Os <c>#include "..."</c> deste texto que existem no projeto.
    ///
    /// Com aspas, e so com aspas: e a diferenca que o C faz entre "o
    /// arquivo ao meu lado" e "a biblioteca instalada".
    /// </summary>
    public static IEnumerable<string> IncluidosDoProjeto(
        string texto, IDictionary<string, string> arquivos)
    {
        if (string.IsNullOrEmpty(texto)) yield break;

        foreach (string bruta in texto.Replace("\r\n", "\n").Split('\n'))
        {
            string linha = bruta.Trim();
            if (!linha.StartsWith("#include", StringComparison.Ordinal)) continue;

            int abre = linha.IndexOf('"');
            if (abre < 0) continue;

            int fecha = linha.IndexOf('"', abre + 1);
            if (fecha <= abre + 1) continue;

            string nome = linha[(abre + 1)..fecha].Trim();
            if (arquivos.ContainsKey(nome)) yield return nome;
        }
    }
}
