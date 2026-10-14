using System.Text;

namespace Simulador;

/// <summary>
/// O leitor de símbolos do Rust.
///
/// Rust é de chaves como o C, mas quatro coisas obrigam a um leitor
/// próprio:
///
///   - <c>//</c> comenta e <c>/* */</c> ANINHA -- em C não aninha, e um
///     leitor de C fecharia o comentário no primeiro <c>*/</c>, deixando
///     o resto do bloco entrar no programa;
///   - o apóstrofo é caractere (<c>'a'</c>) e também tempo de vida
///     (<c>&amp;'a str</c>), que não é valor nenhum;
///   - números carregam o tipo grudado: <c>13u8</c>, <c>500_u32</c>,
///     <c>1_000_000</c>;
///   - <c>::</c>, <c>=&gt;</c>, <c>..=</c> e <c>!</c> de macro são
///     símbolos que o C não tem.
/// </summary>
public static class LexerRust
{
    // O mais longo primeiro: "..=" antes de "..", que vem antes de ".".
    static readonly string[] Ops =
    [
        "..=", "<<=", ">>=",
        "::", "->", "=>", "==", "!=", "<=", ">=", "&&", "||", "..",
        "+=", "-=", "*=", "/=", "%=", "&=", "|=", "^=", "<<", ">>",
        "+", "-", "*", "/", "%", "=", "<", ">", "!", "&", "|", "^",
        "(", ")", "{", "}", "[", "]", ";", ",", ".", "?", ":", "#", "@",
    ];

    const char Barra = (char)92;
    const char Aspas = (char)34;
    const char Apost = (char)39;

    static char Escapar(char c) => c switch
    {
        'n' => '\n',
        't' => '\t',
        'r' => '\r',
        '0' => '\0',
        _ => c,
    };

    public static List<Token> Tokenizar(string fonte)
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
            if (c == '/' && i + 1 < s.Length && s[i + 1] == '/')
            {
                while (i < s.Length && s[i] != '\n') i++;
                continue;
            }

            if (c == '/' && i + 1 < s.Length && s[i + 1] == '*')
            {
                // Aninhado, que é como Rust define. Contar a
                // profundidade é a diferença entre parar no lugar certo
                // e engolir metade do programa.
                int fundo = 0;

                while (i < s.Length)
                {
                    if (s[i] == '/' && i + 1 < s.Length && s[i + 1] == '*') { fundo++; i += 2; continue; }
                    if (s[i] == '*' && i + 1 < s.Length && s[i + 1] == '/')
                    {
                        fundo--;
                        i += 2;
                        if (fundo == 0) break;
                        continue;
                    }

                    if (s[i] == '\n') linha++;
                    i++;
                }

                continue;
            }

            // ---- números ----
            if (char.IsDigit(c))
            {
                int ini = i;
                bool real = false;

                if (c == '0' && i + 1 < s.Length && (s[i + 1] is 'x' or 'b' or 'o'))
                {
                    char baseDoNumero = s[i + 1];
                    i += 2;
                    int corpo = i;

                    while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] == '_')) i++;

                    string bruto = s[corpo..i].Replace("_", "");

                    // O sufixo de tipo gruda no número: 0xFFu8.
                    bruto = TirarSufixo(bruto);

                    long valor = 0;
                    try
                    {
                        valor = Convert.ToInt64(bruto,
                            baseDoNumero == 'x' ? 16 : baseDoNumero == 'b' ? 2 : 8);
                    }
                    catch (Exception e) when (e is FormatException or ArgumentException) { }

                    saida.Add(new Token(T.Num, s[ini..i], valor, true, linha));
                    continue;
                }

                while (i < s.Length
                       && (char.IsLetterOrDigit(s[i]) || s[i] == '_'
                           || (s[i] == '.' && i + 1 < s.Length && char.IsDigit(s[i + 1]))))
                {
                    // "0..10" é intervalo, e não um real.
                    if (s[i] == '.' && i + 1 < s.Length && s[i + 1] == '.') break;
                    if (s[i] == '.') real = true;
                    i++;
                }

                string texto = TirarSufixo(s[ini..i].Replace("_", ""));

                double numero = double.TryParse(texto,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double v) ? v : 0;

                saida.Add(new Token(T.Num, s[ini..i], numero, !real, linha));
                continue;
            }

            // ---- nomes ----
            if (char.IsLetter(c) || c == '_')
            {
                int ini = i;
                while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] == '_')) i++;

                string nome = s[ini..i];

                // Macro: "println!" é um nome só, com a exclamação
                // junto. Separar os dois faria o "!" virar negação.
                if (i < s.Length && s[i] == '!' && (i + 1 >= s.Length || s[i + 1] != '='))
                {
                    i++;
                    saida.Add(new Token(T.Id, nome + "!", 0, false, linha));
                    continue;
                }

                saida.Add(new Token(T.Id, nome, 0, false, linha));
                continue;
            }

            // ---- texto ----
            if (c == Aspas)
            {
                var texto = new StringBuilder();
                i++;

                while (i < s.Length && s[i] != Aspas)
                {
                    if (s[i] == Barra && i + 1 < s.Length) { texto.Append(Escapar(s[++i])); i++; continue; }
                    if (s[i] == '\n') linha++;
                    texto.Append(s[i++]);
                }

                i++;
                saida.Add(new Token(T.Str, texto.ToString(), 0, false, linha));
                continue;
            }

            // ---- caractere, ou tempo de vida ----
            //
            // 'a' é um caractere; &'a str é um tempo de vida, que não
            // vale nada em tempo de execução. O fechamento decide.
            if (c == Apost)
            {
                bool fechaLogo = i + 2 < s.Length && s[i + 2] == Apost;
                bool escapado = i + 3 < s.Length && s[i + 1] == Barra && s[i + 3] == Apost;

                if (fechaLogo)
                {
                    saida.Add(new Token(T.Char, s[i + 1].ToString(), s[i + 1], true, linha));
                    i += 3;
                    continue;
                }

                if (escapado)
                {
                    char valor = Escapar(s[i + 2]);
                    saida.Add(new Token(T.Char, valor.ToString(), valor, true, linha));
                    i += 4;
                    continue;
                }

                // Tempo de vida: some, com o nome que vem depois dele.
                i++;
                while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] == '_')) i++;
                continue;
            }

            // ---- operadores ----
            string achado = Ops.FirstOrDefault(op => i + op.Length <= s.Length
                                                     && s.AsSpan(i, op.Length).SequenceEqual(op));

            if (achado is null)
                throw new ErroDeSintaxe($"não entendi o símbolo '{c}'", linha);

            saida.Add(new Token(T.Op, achado, 0, false, linha));
            i += achado.Length;
        }

        saida.Add(new Token(T.Fim, "", 0, false, linha));
        return saida;
    }

    /// <summary>
    /// Tira o tipo grudado no número: 13u8, 500i32, 1.5f32.
    ///
    /// Em Rust isso é comum e obrigatório em muitos lugares. Sem tirar,
    /// "13u8" não vira número nenhum e o programa falha numa linha que
    /// está certa.
    /// </summary>
    static string TirarSufixo(string bruto)
    {
        foreach (string sufixo in new[]
                 { "u8", "u16", "u32", "u64", "u128", "usize",
                   "i8", "i16", "i32", "i64", "i128", "isize", "f32", "f64" })
            if (bruto.EndsWith(sufixo, StringComparison.Ordinal))
                return bruto[..^sufixo.Length];

        return bruto;
    }
}
