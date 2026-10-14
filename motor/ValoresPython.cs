namespace Simulador;

/// <summary>
/// O None do Python.
///
/// Uma instancia so, para "x is None" ser uma comparacao de referencia
/// e nao de conteudo.
/// </summary>
public sealed class NadaPy
{
    public static readonly NadaPy Unico = new();
    NadaPy() { }
}

/// <summary>
/// Lista ou tupla do Python.
///
/// As duas sao a mesma estrutura porque o simulador nao precisa impedir
/// a escrita numa tupla -- o que ele precisa e mostrar "(1, 2)" com
/// parenteses e "[1, 2]" com colchetes, que e o que o aluno le na tela.
/// O caso que aparece em aula e a cor do NeoPixel: "rgb[p] = (148, 0, 211)".
/// </summary>
public sealed class ListaPy(List<Valor> itens = null, bool ehTupla = false)
{
    public List<Valor> Itens { get; } = itens ?? [];
    public bool EhTupla { get; } = ehTupla;

    public static ListaPy De(params Valor[] itens) => new([.. itens]);

    /// <summary>
    /// O indice do Python, que aceita negativo contando do fim.
    /// Devolve -1 quando esta fora.
    /// </summary>
    public int Posicao(int i)
    {
        if (i < 0) i += Itens.Count;
        return i >= 0 && i < Itens.Count ? i : -1;
    }
}

/// <summary>
/// Dicionario do Python, com chave de texto.
///
/// Chave so de texto porque e o que os sketches usam -- o payload de
/// MQTT e de JSON. Um dicionario com chave numerica cai no mesmo balde
/// pela conversao em Mostrar(), sem erro.
/// </summary>
public sealed class DicionarioPy
{
    public Dictionary<string, Valor> Itens { get; } = [];

    /// <summary>A ordem em que as chaves entraram: o dict do Python 3.7+
    /// preserva isso, e o JSON gerado tem que sair na ordem escrita.</summary>
    public List<string> Ordem { get; } = [];

    public void Por(string chave, Valor v)
    {
        if (!Itens.ContainsKey(chave)) Ordem.Add(chave);
        Itens[chave] = v;
    }

    public IEnumerable<KeyValuePair<string, Valor>> Pares()
        => Ordem.Select(k => new KeyValuePair<string, Valor>(k, Itens[k]));
}

/// <summary>
/// Um modulo importado ("machine", "time", "network").
///
/// Guardar o modulo como VALOR resolve os dois jeitos de importar que o
/// aluno escreve: "import machine" deixa o nome do modulo na variavel,
/// e "from machine import Pin" deixa o membro. Os dois acabam aqui.
/// </summary>
public sealed class ModuloPy(string nome)
{
    public string Nome { get; } = nome;
}

/// <summary>
/// Um atalho para dentro de um modulo: o "Pin" de "from machine import Pin".
/// </summary>
public sealed class MembroDeModuloPy(string modulo, string nome)
{
    public string Modulo { get; } = modulo;
    public string Nome { get; } = nome;
}

/// <summary>
/// Como o Python mostra um valor.
///
/// Nao e detalhe de acabamento: e o que sai no monitor, no ujson.dumps
/// e no .format(). O Arduino escreve "25.00" para um real, porque o
/// Serial.print usa duas casas; o Python escreve "25.0", que e o
/// numero mais curto que representa aquele valor. Um sketch de MQTT que
/// publica "25.00" nao e o mesmo que publica "25.0" -- quem consome do
/// outro lado pode reclamar do formato.
/// </summary>
public static class TextoPy
{
    public static string De(Valor v) => v.Referencia switch
    {
        null => Simples(v),
        NadaPy => "None",
        ListaPy l => (l.EhTupla ? "(" : "[")
                     + string.Join(", ", l.Itens.Select(Citar))
                     + (l.EhTupla && l.Itens.Count == 1 ? "," : "")
                     + (l.EhTupla ? ")" : "]"),
        DicionarioPy d => "{"
                          + string.Join(", ", d.Pares().Select(
                              par => "'" + par.Key + "': " + Citar(par.Value)))
                          + "}",
        // A excecao (e o IPAddress) se mostram pelo texto: o print(e) do except.
        Objeto o => o.Texto ?? "<" + o.Classe + ">",
        ModuloPy m => "<module '" + m.Nome + "'>",
        MembroDeModuloPy m => "<class '" + m.Modulo + "." + m.Nome + "'>",
        _ => v.Referencia.ToString()
    };

    /// <summary>Dentro de lista e dicionario, o texto aparece com aspas.</summary>
    static string Citar(Valor v) => v.EhTexto ? "'" + v.Texto + "'" : De(v);

    static string Simples(Valor v)
    {
        if (v.EhTexto) return v.Texto;
        if (v.EhBooleano) return v.Num != 0 ? "True" : "False";
        if (v.Inteiro) return ((long)v.Num).ToString();

        // O menor texto que volta a ser o mesmo numero -- e a regra do
        // proprio Python. E o que faz 25.0 sair "25.0" e 1/3 sair com
        // todas as casas em vez de "0.33".
        string curto = v.Num.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        return curto.Contains('.') || curto.Contains('E') || curto.Contains("Inf")
               || curto.Contains("NaN")
            ? curto
            : curto + ".0";
    }
}
