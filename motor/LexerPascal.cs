using System.Text;

namespace Simulador;

/// <summary>
/// Qual das linguagens da família Pascal este texto é.
///
/// As quatro compartilham a espinha dorsal -- <c>:=</c> para atribuir,
/// blocos que fecham com <c>end</c>, comentário de traço duplo -- e
/// divergem em detalhes: a palavra que abre uma sub-rotina, se o
/// <c>loop</c> precisa de <c>end loop</c>, como se declara variável.
///
/// Um analisador por dialeto daria quatro gramáticas para manter e
/// quatro chances de o mesmo <c>if</c> se comportar diferente. Um só,
/// com as diferenças nomeadas aqui, dá uma.
/// </summary>
public enum DialetoPascal
{
    /// <summary>Ada: procedure/function, declare/begin/end, case-insensitive.</summary>
    Ada,

    /// <summary>SPARK: Ada mais os contratos (Pre, Post, Global).</summary>
    Spark,

    /// <summary>Cowgol: sub/end sub, var x: uint8, comentário com #.</summary>
    Cowgol,

    /// <summary>Millipascal: proc/begin/end, set x = v.</summary>
    Millipascal,
}

/// <summary>
/// O leitor de símbolos da família Pascal.
///
/// Diferenças que obrigam a um lexer próprio, e não ao do C:
///
///   - comentário é <c>--</c> (Ada, SPARK, Cowgol) ou <c>#</c>
///     (Cowgol também) e <c>(* ... *)</c> (Pascal clássico);
///   - <c>:=</c> atribui e <c>=</c> COMPARA -- trocar os dois é o erro
///     que um lexer de C cometeria calado;
///   - texto fica entre aspas duplas e o caractere entre apóstrofos,
///     mas o apóstrofo em Ada também é atributo (<c>X'Length</c>);
///   - <c>..</c> é o intervalo do <c>for</c>, e precisa sair antes do
///     ponto sozinho;
///   - as palavras não têm caixa: <c>BEGIN</c>, <c>Begin</c> e
///     <c>begin</c> são a mesma, e é assim em Ada de verdade.
/// </summary>
public static class LexerPascal
{
    // A ordem importa: o mais longo primeiro, senão ":=" vira ":" e "=".
    static readonly string[] Ops =
    [
        ":=", "..", "=>", "/=", "<=", ">=", "**", "<>",

        // Os do Cowgol e do Millipascal, que escrevem como o C: "==" e "!="
        // comparam, "%" e o resto, e os deslocamentos de bit.
        "==", "!=", "<<", ">>",
        "+", "-", "*", "/", "=", "<", ">", "&", "%", "^", "~",
        "(", ")", "[", "]", ";", ",", ".", ":", "|", "'",
    ];

    const char Aspas = (char)34;
    const char Apost = (char)39;

    public static List<Token> Tokenizar(string fonte, DialetoPascal dialeto)
    {
        var saida = new List<Token>();
        string s = (fonte ?? "").Replace("\r\n", "\n");
        int i = 0, linha = 1;

        while (i < s.Length)
        {
            char c = s[i];

            if (c == '\n') { linha++; i++; continue; }
            if (char.IsWhiteSpace(c)) { i++; continue; }

            // ---- comentários ----
            //
            // O traço duplo é o de Ada e o de Cowgol. O "#" é o do
            // Cowgol e o do Millipascal. O "(* *)" é o do Pascal, e
            // continua valendo nos quatro porque nenhum deles usa
            // "(*" para outra coisa.
            if (c == '-' && i + 1 < s.Length && s[i + 1] == '-')
            {
                while (i < s.Length && s[i] != '\n') i++;
                continue;
            }

            if (c == '#')
            {
                while (i < s.Length && s[i] != '\n') i++;
                continue;
            }

            if (c == '(' && i + 1 < s.Length && s[i + 1] == '*')
            {
                i += 2;
                while (i + 1 < s.Length && !(s[i] == '*' && s[i + 1] == ')'))
                {
                    if (s[i] == '\n') linha++;
                    i++;
                }
                i = Math.Min(s.Length, i + 2);
                continue;
            }

            // ---- números ----
            if (char.IsDigit(c))
            {
                int ini = i;
                bool real = false;

                while (i < s.Length
                       && (char.IsDigit(s[i]) || s[i] == '_'
                           || s[i] == '#' || char.IsLetter(s[i])
                           || (s[i] == '.' && i + 1 < s.Length && char.IsDigit(s[i + 1]))))
                {
                    // "1..10" é intervalo, e não um real seguido de ponto.
                    if (s[i] == '.' && i + 1 < s.Length && s[i + 1] == '.') break;
                    if (s[i] == '.') real = true;
                    i++;
                }

                string bruto = s[ini..i].Replace("_", "");
                saida.Add(new Token(T.Num, bruto, LerNumero(bruto), !real, linha));
                continue;
            }

            // ---- nomes e palavras ----
            if (char.IsLetter(c) || c == '_')
            {
                int ini = i;
                while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] == '_')) i++;

                string nome = s[ini..i];

                // Palavra reservada sai sempre em minúscula, para o
                // analisador comparar com uma forma só; nome de
                // variável mantém a caixa que a pessoa escreveu.
                string chave = nome.ToLowerInvariant();
                saida.Add(new Token(T.Id, Reservadas.Contains(chave) ? chave : nome,
                                    0, false, linha));
                continue;
            }

            // ---- texto ----
            if (c == Aspas)
            {
                var texto = new StringBuilder();
                i++;

                while (i < s.Length && s[i] != Aspas)
                {
                    if (s[i] == '\n') linha++;
                    texto.Append(s[i++]);
                }

                // Aspas duplicadas dentro do texto são uma aspa só:
                // é assim que Pascal e Ada escapam, sem barra invertida.
                while (i + 1 < s.Length && s[i] == Aspas && s[i + 1] == Aspas)
                {
                    texto.Append(Aspas);
                    i += 2;
                    while (i < s.Length && s[i] != Aspas) texto.Append(s[i++]);
                }

                i++;
                saida.Add(new Token(T.Str, texto.ToString(), 0, false, linha));
                continue;
            }

            // ---- caractere ----
            //
            // 'a' é um caractere; X'Length é um atributo. O que separa
            // os dois é o apóstrofo de fechamento na terceira posição.
            if (c == Apost && i + 2 < s.Length && s[i + 2] == Apost)
            {
                saida.Add(new Token(T.Char, s[i + 1].ToString(), s[i + 1], true, linha));
                i += 3;
                continue;
            }

            // ---- operadores ----
            string achado = Ops.FirstOrDefault(op => i + op.Length <= s.Length
                                                     && s.AsSpan(i, op.Length).SequenceEqual(op));

            if (achado is null)
                throw new ErroDeSintaxe(Idioma.T("não entendi o símbolo '{0}'", c), linha);

            saida.Add(new Token(T.Op, achado, 0, false, linha));
            i += achado.Length;
        }

        saida.Add(new Token(T.Fim, "", 0, false, linha));
        return saida;
    }

    /// <summary>
    /// Um número escrito do jeito Ada.
    ///
    /// Ada escreve a base com cerquilha -- 16#FF# são 255 --, e usa o
    /// sublinhado como separador de milhar. Os dois já saíram na
    /// passagem anterior; aqui sobra converter.
    /// </summary>
    static double LerNumero(string bruto)
    {
        int cerquilha = bruto.IndexOf('#');

        if (cerquilha > 0 && int.TryParse(bruto[..cerquilha], out int baseDoNumero))
        {
            string corpo = bruto[(cerquilha + 1)..].TrimEnd('#');

            try { return Convert.ToInt64(corpo, baseDoNumero); }
            catch (Exception e) when (e is FormatException or ArgumentException) { return 0; }
        }

        // 0x10 é como Cowgol e Millipascal escrevem hexadecimal.
        if (bruto.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            try { return Convert.ToInt64(bruto[2..], 16); }
            catch (Exception e) when (e is FormatException or ArgumentException) { return 0; }
        }

        return double.TryParse(bruto, System.Globalization.NumberStyles.Float,
                               System.Globalization.CultureInfo.InvariantCulture, out double v)
            ? v : 0;
    }

    /// <summary>
    /// As palavras das quatro linguagens, juntas.
    ///
    /// Juntas de propósito: um programa em Cowgol não usa "procedure",
    /// mas reservar a palavra nos quatro dialetos custa nada e evita que
    /// alguém chame uma variável de "loop" e receba um erro sem pé nem
    /// cabeça três linhas adiante.
    /// </summary>
    static readonly HashSet<string> Reservadas =
    [
        // comuns
        "begin", "end", "if", "then", "else", "elsif", "elif", "while", "loop",
        "for", "in", "is", "return", "null", "and", "or", "not", "xor", "mod", "rem",
        "true", "false", "var", "const", "type", "record", "array", "of", "exit",
        "when", "case", "others", "declare", "range", "new", "abs",

        // Ada e SPARK
        "procedure", "function", "package", "body", "with", "use", "out", "inout",
        "pragma", "renames", "subtype", "access", "aliased", "constant",
        "pre", "post", "global", "depends", "spark_mode",

        // Cowgol
        "sub", "extern", "implements", "interface", "typedef", "include",

        // Millipascal
        "proc", "set", "data", "do", "as", "sizeof",
    ];

    public static bool EhReservada(string palavra) => Reservadas.Contains(palavra);
}
