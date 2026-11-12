namespace Simulador;

/// <summary>
/// Em que linguagem o sketch esta escrito.
/// </summary>
public enum Linguagem
{
    /// <summary>C++ do Arduino: setup(), loop(), chaves e ponto-e-virgula.</summary>
    Arduino,

    /// <summary>MicroPython: indentacao, import, sem chaves.</summary>
    MicroPython,

    /// <summary>
    /// C puro: comeca no main(), sem setup nem loop.
    ///
    /// E a mesma arvore e o mesmo interpretador do Arduino -- um
    /// sketch JA e C++ --, entao nao ha front-end novo aqui. O que
    /// muda, e o motivo de a linguagem existir como opcao separada, sao
    /// duas coisas que a pessoa precisa ver antes de rodar: o ponto de
    /// entrada e main(), e o vocabulario e o do SDK da placa
    /// (gpio_put, sleep_ms) e nao o do Arduino.
    /// </summary>
    C,

    /// <summary>C#: nanoFramework e Meadow. class Program, static void Main.</summary>
    CSharp,

    /// <summary>Rust: fn main, let mut, loop, match.</summary>
    Rust,

    /// <summary>Ada: procedure, begin/end, := e end loop.</summary>
    Ada,

    /// <summary>SPARK: Ada com os contratos Pre, Post e Global.</summary>
    Spark,

    /// <summary>Cowgol: sub/end sub, var x: uint8.</summary>
    Cowgol,

    /// <summary>Millipascal: proc/begin/end, set x = v.</summary>
    Millipascal,

    /// <summary>
    /// Assembly.
    ///
    /// A unica da lista que NAO passa pelo interpretador de arvore: nao
    /// ha expressao nem funcao para montar, ha instrucao e registrador.
    /// Ela tem montador e executor proprios.
    /// </summary>
    Assembly,
}

/// <summary>
/// O lexer do MicroPython.
///
/// Separado do <see cref="Lexer"/> do C por um motivo que muda tudo: em
/// Python a INDENTACAO e sintaxe. Onde o C abre e fecha bloco com chave,
/// o Python conta espacos no comeco da linha -- entao o lexer guarda uma
/// pilha de colunas e emite <see cref="T.Entra"/> e <see cref="T.Sai"/>
/// nos lugares certos. Reaproveitar o outro lexer daria um remendo pior
/// do que dois arquivos.
///
/// O resto e parecido de proposito: mesmo <see cref="Token"/> e mesmo
/// <see cref="ErroDeSintaxe"/>, para o parser e as mensagens de erro da
/// tela continuarem os mesmos.
/// </summary>
public static class LexerPython
{
    // A ordem importa: os de tres caracteres antes dos de dois.
    static readonly string[] Ops =
    [
        "**=", "//=", ">>=", "<<=",
        "==", "!=", "<=", ">=", "**", "//", "+=", "-=", "*=", "/=", "%=",
        "&=", "|=", "^=", "<<", ">>", "->",
        "+", "-", "*", "/", "%", "=", "<", ">", "&", "|", "^", "~",
        "(", ")", "[", "]", "{", "}", ",", ".", ":", ";"
    ];

    const char Barra = (char)92;
    const char Aspas = (char)34;
    const char Apost = (char)39;

    /// <summary>
    /// Marca de f-string.
    ///
    /// Vai no campo numerico do token porque o <see cref="Token"/> e
    /// compartilhado com o lexer do C, que nao tem f-string: acrescentar
    /// um campo so para isto mexeria nos dois.
    /// </summary>
    internal const double Formatado = 1;

    static char Escapar(char c) => c switch
    {
        'n' => '\n',
        't' => '\t',
        'r' => '\r',
        '0' => '\0',
        _ => c
    };

    public static List<Token> Tokenizar(string src)
    {
        src = src.Replace("\r\n", "\n").Replace('\r', '\n');

        var toks = new List<Token>();
        var recuos = new Stack<int>();
        recuos.Push(0);

        int i = 0, linha = 1;

        // Parenteses abertos: dentro deles a quebra de linha nao termina
        // comando nenhum, e por isso uma chamada pode ocupar cinco linhas.
        int abertos = 0;

        bool inicioDeLinha = true;
        int marcaDaLinha = 0;

        while (i < src.Length)
        {
            // ---- comeco de linha: e aqui que o bloco abre e fecha ----
            if (inicioDeLinha && abertos == 0)
            {
                int col = 0, j = i;
                while (j < src.Length && (src[j] == ' ' || src[j] == '\t'))
                {
                    // O tab conta 8, que e a regra do proprio Python.
                    // Misturar tab e espaco e o erro de quem cola codigo
                    // da internet, e sem isto o bloco abriria no lugar
                    // errado sem dizer por que.
                    col = src[j] == '\t' ? (col / 8 + 1) * 8 : col + 1;
                    j++;
                }

                // Linha em branco ou so com comentario nao mexe em bloco.
                if (j >= src.Length) break;
                if (src[j] == '\n') { i = j + 1; linha++; continue; }
                if (src[j] == '#')
                {
                    while (j < src.Length && src[j] != '\n') j++;
                    i = j;
                    continue;
                }

                if (col > recuos.Peek())
                {
                    recuos.Push(col);
                    toks.Add(new Token(T.Entra, "<entra>", 0, false, linha));
                }
                else
                    while (col < recuos.Peek())
                    {
                        recuos.Pop();
                        toks.Add(new Token(T.Sai, "<sai>", 0, false, linha));
                        if (col > recuos.Peek())
                            throw new ErroDeSintaxe(
                                Idioma.T("a indentacao desta linha nao bate com nenhum bloco aberto. Confira se misturou espaco com tab."), linha);
                    }

                i = j;
                inicioDeLinha = false;
                marcaDaLinha = toks.Count;
                continue;
            }

            char c = src[i];

            // ---- fim de linha ----
            if (c == '\n')
            {
                linha++;
                i++;
                if (abertos == 0)
                {
                    if (toks.Count > marcaDaLinha)
                        toks.Add(new Token(T.Nova, "<nova>", 0, false, linha - 1));
                    inicioDeLinha = true;
                }
                continue;
            }

            if (c == ' ' || c == '\t') { i++; continue; }

            // Emenda de linha: a proxima linha e continuacao desta.
            if (c == Barra && i + 1 < src.Length && src[i + 1] == '\n')
            {
                i += 2;
                linha++;
                continue;
            }

            if (c == '#')
            {
                while (i < src.Length && src[i] != '\n') i++;
                continue;
            }

            // ---- texto de tres aspas, que atravessa linhas ----
            if ((c == Aspas || c == Apost) && i + 2 < src.Length
                && src[i + 1] == c && src[i + 2] == c)
            {
                string marca = new(c, 3);
                int fim = src.IndexOf(marca, i + 3, StringComparison.Ordinal);
                if (fim < 0)
                    throw new ErroDeSintaxe(Idioma.T("texto de tres aspas sem fechamento"), linha);

                string corpo = src[(i + 3)..fim];
                toks.Add(new Token(T.Str, corpo, 0, false, linha));
                linha += corpo.Count(ch => ch == '\n');
                i = fim + 3;
                continue;
            }

            // ---- texto comum, com prefixo f/r/b quando houver ----
            if (c == Aspas || c == Apost
                || (c is 'f' or 'F' or 'r' or 'R' or 'b' or 'B'
                    && i + 1 < src.Length && (src[i + 1] == Aspas || src[i + 1] == Apost)))
            {
                bool cru = c is 'r' or 'R';
                bool formatado = c is 'f' or 'F';
                if (c != Aspas && c != Apost) i++;

                char aspa = src[i];
                i++;
                var sb = new System.Text.StringBuilder();
                while (i < src.Length && src[i] != aspa && src[i] != '\n')
                {
                    if (!cru && src[i] == Barra && i + 1 < src.Length)
                    {
                        i++;
                        sb.Append(Escapar(src[i]));
                    }
                    else sb.Append(src[i]);
                    i++;
                }
                if (i >= src.Length || src[i] == '\n')
                    throw new ErroDeSintaxe(Idioma.T("texto sem fechamento"), linha);
                i++;

                toks.Add(new Token(T.Str, sb.ToString(),
                                   formatado ? Formatado : 0, false, linha));
                continue;
            }

            // ---- numeros ----
            if (char.IsDigit(c) || (c == '.' && i + 1 < src.Length && char.IsDigit(src[i + 1])))
            {
                int ini = i;
                bool hex = false, bin = false;

                if (c == '0' && i + 1 < src.Length && src[i + 1] is 'x' or 'X')
                {
                    hex = true;
                    i += 2;
                    while (i < src.Length && (Uri.IsHexDigit(src[i]) || src[i] == '_')) i++;
                }
                else if (c == '0' && i + 1 < src.Length && src[i + 1] is 'b' or 'B')
                {
                    bin = true;
                    i += 2;
                    while (i < src.Length && (src[i] is '0' or '1' or '_')) i++;
                }
                else
                {
                    while (i < src.Length
                           && (char.IsDigit(src[i]) || src[i] == '.' || src[i] == '_')) i++;
                    if (i < src.Length && src[i] is 'e' or 'E')
                    {
                        i++;
                        if (i < src.Length && src[i] is '+' or '-') i++;
                        while (i < src.Length && char.IsDigit(src[i])) i++;
                    }
                }

                string txt = src[ini..i].Replace("_", "");
                double val = hex ? Convert.ToInt64(txt[2..], 16)
                           : bin ? Convert.ToInt64(txt[2..], 2)
                           : double.Parse(txt, System.Globalization.CultureInfo.InvariantCulture);
                bool inteiro = hex || bin
                               || (!txt.Contains('.') && !txt.Contains('e') && !txt.Contains('E'));
                toks.Add(new Token(T.Num, txt, val, inteiro, linha));
                continue;
            }

            // ---- nomes ----
            if (char.IsLetter(c) || c == '_')
            {
                int ini = i;
                while (i < src.Length && (char.IsLetterOrDigit(src[i]) || src[i] == '_')) i++;
                toks.Add(new Token(T.Id, src[ini..i], 0, false, linha));
                continue;
            }

            // ---- operadores ----
            var op = Ops.FirstOrDefault(
                o => i + o.Length <= src.Length && src.AsSpan(i, o.Length).SequenceEqual(o));
            if (op is null)
                throw new ErroDeSintaxe(Idioma.T("caractere inesperado '{0}'", c), linha);

            if (op is "(" or "[" or "{") abertos++;
            if (op is ")" or "]" or "}") abertos = Math.Max(0, abertos - 1);

            toks.Add(new Token(T.Op, op, 0, false, linha));
            i += op.Length;
        }

        if (!inicioDeLinha && toks.Count > marcaDaLinha)
            toks.Add(new Token(T.Nova, "<nova>", 0, false, linha));

        while (recuos.Count > 1)
        {
            recuos.Pop();
            toks.Add(new Token(T.Sai, "<sai>", 0, false, linha));
        }

        toks.Add(new Token(T.Fim, "<fim>", 0, false, linha));
        return toks;
    }
}
