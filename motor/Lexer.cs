namespace Simulador;

/// <summary>
/// Os tres ultimos so aparecem em MicroPython: o C nao tem bloco por
/// indentacao nem fim de comando pela quebra de linha.
/// </summary>
public enum T
{
    Num, Str, Char, Id, Op, Fim,

    /// <summary>Quebra de linha que fecha um comando.</summary>
    Nova,

    /// <summary>Um nivel de indentacao a mais: abriu bloco.</summary>
    Entra,

    /// <summary>Um nivel a menos: fechou bloco.</summary>
    Sai,

    /// <summary>
    /// Uma linha que comeca com "#", com o texto que vem depois dele. So
    /// existe entre a leitura crua e o pre-processador: o parser nunca a ve.
    /// </summary>
    Diretiva
}

public record Token(T Tipo, string Texto, double Valor, bool Inteiro, int Linha)
{
    public bool E(string s) => Texto == s;

    /// <summary>
    /// O "String" que veio escrito "std::string" (ou "string", com o
    /// "using namespace std"): para o simulador e o mesmo texto, mas o
    /// compilador nao converte um no outro -- e e assim que se acha o
    /// "std::string x = algo->getValue();" que o core 3.x recusa.
    /// </summary>
    public bool DoStd { get; init; }
    public override string ToString() => $"{Tipo}:{Texto}(l{Linha})";
}

public class ErroDeSintaxe(string msg, int linha)
    : Exception($"{Origens.Onde(linha)}: {msg}")
{
    public int Linha { get; } = linha;
}

public static class Lexer
{
    // Operadores de varios caracteres primeiro: a ordem importa.
    static readonly string[] Ops =
    [
        "<<=", ">>=", "...",
        "==", "!=", "<=", ">=", "&&", "||", "++", "--",
        "+=", "-=", "*=", "/=", "%=", "&=", "|=", "^=", "<<", ">>", "->", "##",
        "+", "-", "*", "/", "%", "=", "<", ">", "!", "&", "|", "^", "~",
        "(", ")", "{", "}", "[", "]", ";", ",", ".", "?", ":", "#"
    ];

    /// <summary>A marca de uma string que nao fechou na linha.</summary>
    const string StringAberta = "\"...";

    // Por codigo numerico para nao brigar com escapes.
    const char Barra = (char)92;   // \
    const char Aspas = (char)34;   // "
    const char Apost = (char)39;   // '

    /// <summary>
    /// "std::min" e "min", "std::string" e o String do Arduino: o
    /// simulador so tem uma biblioteca padrao, e o prefixo nao muda nada
    /// no que o codigo faz.
    /// </summary>
    static string SemStd(string nome)
    {
        if (!nome.StartsWith("std::", StringComparison.Ordinal)) return nome;
        string resto = nome[5..];
        return resto == "string" ? "String" : resto;
    }

    /// <summary>
    /// Por que este caractere nao serve.
    ///
    /// Traco longo e aspas curvas entram no sketch sozinhos quando o
    /// codigo e colado de uma pagina da web ou de um documento -- o
    /// editor de texto troca o "-" por "–" e as aspas retas pelas
    /// tortas. Sao identicos na tela, e "caractere inesperado" nao
    /// ajudava ninguem a ver o que estava errado.
    /// </summary>
    static string Recusa(char c) => c switch
    {
        '\u2013' or '\u2014' =>
            "aqui tem um traco longo (–), e nao o sinal de menos (-). "
            + "Isso costuma vir de codigo colado de uma pagina ou do Word.",
        '\u201C' or '\u201D' =>
            "aqui tem aspas curvas (\u201C \u201D), e nao as aspas retas que o C usa. "
            + "Isso costuma vir de codigo colado de uma pagina ou do Word.",
        '\u2018' or '\u2019' =>
            "aqui tem apostrofo curvo (\u2018 \u2019), e nao o reto que o C usa. "
            + "Isso costuma vir de codigo colado de uma pagina ou do Word.",
        '\u00A0' =>
            "aqui tem um espaco 'duro' invisivel, que veio junto do texto colado. "
            + "Apague o espaco e digite outro.",
        _ => $"caractere inesperado '{c}'",
    };

    /// <summary>O sketch sozinho, sem placa: so as diretivas que ele mesmo escreve.</summary>
    public static List<Token> Tokenizar(string src) => Tokenizar(src, new Preprocessador(), 0);

    /// <summary>
    /// Le o arquivo e passa pelo pre-processador: as diretivas somem, as
    /// macros sao trocadas pelo que valem e cada #include entra no lugar
    /// dele, com as linhas do proprio arquivo.
    /// </summary>
    public static List<Token> Tokenizar(string src, Preprocessador pp, int arquivo)
    {
        var saida = new List<Token>();
        var crus = Cru(src ?? "", arquivo, corpoDeMacro: false, out int ultimaLinha);
        Processar(crus, pp, arquivo, saida);
        saida.Add(new Token(T.Fim, "<fim>", 0, false, ultimaLinha));
        return saida;
    }

    /// <summary>
    /// Os tokens de um pedaco de texto, sem diretiva nem expansao: o corpo
    /// de um #define, a conta de um #if. Aqui "#" e "##" sao operadores.
    /// </summary>
    public static List<Token> TokensDeTexto(string texto, int linha)
    {
        var ts = Cru(texto ?? "", 0, corpoDeMacro: true, out _);
        for (int i = 0; i < ts.Count; i++) ts[i] = ts[i] with { Linha = linha };
        return ts;
    }

    // =================================================================
    //  Leitura crua
    // =================================================================

    static char Escapar(string src, ref int i)
    {
        char c = src[i];
        switch (c)
        {
            case 'n': return '\n';
            case 't': return '\t';
            case 'r': return '\r';
            case 'a': return '\a';
            case 'b': return '\b';
            case 'f': return '\f';
            case 'v': return '\v';

            // "\x41" e "\101" sao o mesmo 'A'. Antes o "\x" virava um x
            // solto e o resto do numero ia parar no texto.
            case 'x':
            {
                int valor = 0, lidos = 0;
                while (i + 1 < src.Length && lidos < 2 && Uri.IsHexDigit(src[i + 1]))
                {
                    valor = valor * 16 + Convert.ToInt32(src[i + 1].ToString(), 16);
                    i++; lidos++;
                }
                return lidos == 0 ? 'x' : (char)valor;
            }
        }

        if (c is >= '0' and <= '7')
        {
            int valor = c - '0', lidos = 1;
            while (i + 1 < src.Length && lidos < 3 && src[i + 1] is >= '0' and <= '7')
            {
                valor = valor * 8 + (src[i + 1] - '0');
                i++; lidos++;
            }
            return (char)valor;
        }

        return c;
    }

    static List<Token> Cru(string src, int arquivo, bool corpoDeMacro, out int ultimaLinha)
    {
        var toks = new List<Token>();
        int i = 0, linha = 1;
        bool inicioDeLinha = true;

        // Com "using namespace std;" (ou "using std::string;"), o "string"
        // sozinho e o std::string -- antes dava "classe 'string' nao suportada".
        bool usaStd = System.Text.RegularExpressions.Regex.IsMatch(
            src, @"using\s+(namespace\s+std|std::string)\s*;");

        int Onde() => Origens.Codificar(arquivo, linha);

        while (i < src.Length)
        {
            char c = src[i];

            if (c == '\n') { linha++; i++; inicioDeLinha = true; continue; }

            // Barra no fim da linha junta com a seguinte, tambem fora de
            // diretiva: e como uma macro longa continua.
            if (c == Barra && i + 1 < src.Length && (src[i + 1] == '\n' || src[i + 1] == '\r'))
            {
                i++;
                if (src[i] == '\r' && i + 1 < src.Length && src[i + 1] == '\n') i++;
                i++; linha++;
                continue;
            }

            if (char.IsWhiteSpace(c)) { i++; continue; }

            // comentario de linha
            if (c == '/' && i + 1 < src.Length && src[i + 1] == '/')
            {
                while (i < src.Length && src[i] != '\n') i++;
                continue;
            }

            // comentario de bloco
            if (c == '/' && i + 1 < src.Length && src[i + 1] == '*')
            {
                i += 2;
                while (i + 1 < src.Length && !(src[i] == '*' && src[i + 1] == '/'))
                {
                    if (src[i] == '\n') linha++;
                    i++;
                }
                i = Math.Min(i + 2, src.Length);
                continue;
            }

            // diretiva: so no comeco da linha. No meio dela -- no corpo de
            // uma macro -- o "#" e o operador que vira texto.
            if (c == '#' && inicioDeLinha && !corpoDeMacro)
            {
                int linhaDaDiretiva = Onde();
                var texto = new System.Text.StringBuilder();
                i++;
                while (i < src.Length && src[i] != '\n')
                {
                    if (src[i] == Barra && i + 1 < src.Length && (src[i + 1] == '\n' || src[i + 1] == '\r'))
                    {
                        i++;
                        if (src[i] == '\r' && i + 1 < src.Length && src[i + 1] == '\n') i++;
                        i++; linha++;
                        texto.Append(' ');
                        continue;
                    }

                    // Comentario dentro da diretiva: "#endif // GUARDA",
                    // "#define LED 13 /* azul */".
                    if (src[i] == '/' && i + 1 < src.Length && src[i + 1] == '/')
                    {
                        while (i < src.Length && src[i] != '\n') i++;
                        break;
                    }
                    if (src[i] == '/' && i + 1 < src.Length && src[i + 1] == '*')
                    {
                        i += 2;
                        while (i + 1 < src.Length && !(src[i] == '*' && src[i + 1] == '/'))
                        {
                            if (src[i] == '\n') linha++;
                            i++;
                        }
                        i = Math.Min(i + 2, src.Length);
                        texto.Append(' ');
                        continue;
                    }

                    // Aspas dentro da diretiva ficam como estao: o
                    // "#include" e o "#error" leem o texto cru.
                    if (src[i] == Aspas)
                    {
                        texto.Append(src[i++]);
                        while (i < src.Length && src[i] != Aspas && src[i] != '\n')
                        {
                            if (src[i] == Barra && i + 1 < src.Length) texto.Append(src[i++]);
                            texto.Append(src[i++]);
                        }
                        if (i < src.Length && src[i] == Aspas) texto.Append(src[i++]);
                        continue;
                    }

                    texto.Append(src[i++]);
                }

                toks.Add(new Token(T.Diretiva, texto.ToString().Trim(), 0, false, linhaDaDiretiva));
                continue;
            }

            inicioDeLinha = false;

            // numeros
            if (char.IsDigit(c) || (c == '.' && i + 1 < src.Length && char.IsDigit(src[i + 1])))
            {
                int ini = i;
                double val;
                bool inteiro;

                if (c == '0' && i + 1 < src.Length && (src[i + 1] == 'x' || src[i + 1] == 'X'))
                {
                    i += 2;
                    while (i < src.Length && (Uri.IsHexDigit(src[i]) || src[i] == Apost)) i++;
                    string hex = src[(ini + 2)..i].Replace("'", "");
                    val = hex.Length == 0 ? 0 : (double)Convert.ToUInt64(hex, 16);
                    inteiro = true;
                }
                else if (c == '0' && i + 1 < src.Length && (src[i + 1] == 'b' || src[i + 1] == 'B')
                         && i + 2 < src.Length && src[i + 2] is '0' or '1')
                {
                    // "0b1010": binario do C++14, que o GCC de todas as
                    // placas aceita.
                    i += 2;
                    while (i < src.Length && (src[i] is '0' or '1' || src[i] == Apost)) i++;
                    val = Convert.ToUInt64(src[(ini + 2)..i].Replace("'", ""), 2);
                    inteiro = true;
                }
                else
                {
                    while (i < src.Length && (char.IsDigit(src[i]) || src[i] == '.'
                                              || (src[i] == Apost && i + 1 < src.Length && char.IsDigit(src[i + 1]))))
                        i++;
                    if (i < src.Length && (src[i] == 'e' || src[i] == 'E'))
                    {
                        i++;
                        if (i < src.Length && (src[i] == '+' || src[i] == '-')) i++;
                        while (i < src.Length && char.IsDigit(src[i])) i++;
                    }

                    string txt = src[ini..i].Replace("'", "");
                    inteiro = !txt.Contains('.') && !txt.Contains('e') && !txt.Contains('E');

                    // "010" e oito no C: zero na frente e octal. E o que a
                    // placa faz, entao e o que o simulador faz.
                    if (inteiro && txt.Length > 1 && txt[0] == '0')
                    {
                        if (txt.Any(d => d is '8' or '9'))
                            throw new ErroDeSintaxe(
                                $"'{txt}' comeca com zero, e numero com zero na frente e octal no C "
                                + "(so vai de 0 a 7). Tire o zero da frente.", Onde());
                        val = Convert.ToInt64(txt, 8);
                    }
                    else val = double.Parse(txt, System.Globalization.CultureInfo.InvariantCulture);
                }

                // sufixos UL, L, f
                while (i < src.Length && "uUlLfF".Contains(src[i])) i++;

                toks.Add(new Token(T.Num, src[ini..i], val, inteiro, Onde()));
                continue;
            }

            // string crua: R"rawliteral( ... )rawliteral"
            //
            // E como os tutoriais de servidor web do ESP32 guardam a pagina
            // HTML inteira, com aspas dentro. Sem isto o sketch nem passava
            // do lexer.
            if (c == 'R' && i + 1 < src.Length && src[i + 1] == Aspas)
            {
                int abre = src.IndexOf('(', i + 2);
                if (abre > 0)
                {
                    string marca = src[(i + 2)..abre];
                    string fecha = ")" + marca + Aspas;
                    int fim = src.IndexOf(fecha, abre + 1, StringComparison.Ordinal);
                    if (fim < 0) throw new ErroDeSintaxe("string crua sem fechamento", Onde());

                    string conteudo = src[(abre + 1)..fim];
                    toks.Add(new Token(T.Str, conteudo, 0, false, Onde()));
                    linha += conteudo.Count(ch => ch == '\n');
                    i = fim + fecha.Length;
                    continue;
                }
            }

            // strings (o prefixo u8/L/u de texto largo nao muda nada aqui)
            if (c == Aspas || (c is 'L' or 'u' or 'U' && i + 1 < src.Length && src[i + 1] == Aspas)
                || (c == 'u' && i + 2 < src.Length && src[i + 1] == '8' && src[i + 2] == Aspas))
            {
                while (src[i] != Aspas) i++;
                i++;
                var sb = new System.Text.StringBuilder();
                bool fechou = true;
                while (i < src.Length && src[i] != Aspas)
                {
                    // Sem fechar na linha: vira uma marca, e o erro so sai se
                    // a linha estiver num trecho que o #if deixou valer.
                    if (src[i] == '\n') { fechou = false; break; }
                    if (src[i] == Barra && i + 1 < src.Length)
                    {
                        i++;
                        if (src[i] == '\n') { linha++; i++; continue; }
                        sb.Append(Escapar(src, ref i));
                    }
                    else sb.Append(src[i]);
                    i++;
                }
                if (!fechou || i >= src.Length)
                {
                    toks.Add(new Token(T.Op, StringAberta, 0, false, Onde()));
                    continue;
                }
                i++;
                toks.Add(new Token(T.Str, sb.ToString(), 0, false, Onde()));
                continue;
            }

            // literal de caractere
            if (c == Apost)
            {
                i++;
                char ch;
                if (i < src.Length && src[i] == Barra)
                {
                    i++;
                    ch = i < src.Length ? Escapar(src, ref i) : '\0';
                }
                else ch = i < src.Length ? src[i] : '\0';
                i++;
                if (i < src.Length && src[i] == Apost) i++;
                toks.Add(new Token(T.Char, ch.ToString(), ch, true, Onde()));
                continue;
            }

            // identificadores e palavras-chave
            if (char.IsLetter(c) || c == '_'
                || (c == ':' && i + 2 < src.Length && src[i + 1] == ':'
                    && (char.IsLetter(src[i + 2]) || src[i + 2] == '_')
                    && (toks.Count == 0 || toks[^1].Tipo != T.Id)))
            {
                int ini = i;

                // "::digitalWrite": o do escopo global, que um metodo com o
                // mesmo nome esconderia.
                if (c == ':') i += 2;

                while (i < src.Length && (char.IsLetterOrDigit(src[i]) || src[i] == '_')) i++;

                // "BLEDevice::init", "ESP32PWM::allocateTimer": o nome com
                // escopo vira UM nome so. Sem isto o "::" nem era token, e
                // o exemplo de BLE da propria ESP32 parava na primeira
                // linha do setup. "Botao::~Botao" e o destrutor.
                while (i + 2 < src.Length && src[i] == ':' && src[i + 1] == ':'
                       && (char.IsLetter(src[i + 2]) || src[i + 2] == '_'
                           || (src[i + 2] == '~' && i + 3 < src.Length && char.IsLetter(src[i + 3]))))
                {
                    i += 2;
                    if (src[i] == '~') i++;
                    while (i < src.Length && (char.IsLetterOrDigit(src[i]) || src[i] == '_')) i++;
                }

                string nome = src[ini..i];
                bool doStd = nome is "std::string" or "::std::string" || usaStd && nome == "string";
                if (usaStd && nome == "string") nome = "String";
                nome = nome.StartsWith("::", StringComparison.Ordinal)
                    ? "::" + SemStd(nome[2..])
                    : SemStd(nome);
                toks.Add(new Token(T.Id, nome, 0, false, Onde()) { DoStd = doStd });
                continue;
            }

            // operadores
            var op = Ops.FirstOrDefault(o => i + o.Length <= src.Length
                                            && string.CompareOrdinal(src, i, o, 0, o.Length) == 0);

            // Caractere que o C nao tem: guardado como esta. So e erro se
            // cair num trecho que vale -- dentro de um "#if 0", ou no lado de
            // outra placa, o compilador tambem nao reclama.
            if (op is null)
            {
                toks.Add(new Token(T.Op, c.ToString(), 0, false, Onde()));
                i++;
                continue;
            }

            toks.Add(new Token(T.Op, op, 0, false, Onde()));
            i += op.Length;
        }

        ultimaLinha = Onde();
        return toks;
    }

    // =================================================================
    //  Pre-processador
    // =================================================================

    static readonly HashSet<string> OperadoresValidos = [.. Ops];

    static bool EhOperador(string texto) => OperadoresValidos.Contains(texto);

    sealed class Condicao
    {
        public bool Ativo, JaTomado, PaiAtivo, TeveElse;
        public int Linha;
    }

    static void Processar(List<Token> crus, Preprocessador pp, int arquivo, List<Token> saida)
    {
        var pilha = new List<Condicao>();
        bool Ativo() => pilha.Count == 0 || pilha[^1].Ativo;

        for (int i = 0; i < crus.Count; i++)
        {
            var t = crus[i];

            if (t.Tipo == T.Diretiva)
            {
                Diretiva(t, pilha, Ativo(), pp, arquivo, saida);
                continue;
            }

            if (!Ativo()) continue;

            if (t.Tipo == T.Id)
            {
                i = ExpandirNoFluxo(crus, i, pp, arquivo, saida);
                continue;
            }

            if (t.Tipo == T.Op && !EhOperador(t.Texto))
                throw new ErroDeSintaxe(
                    t.Texto == StringAberta ? "string sem fechamento" : Recusa(t.Texto[0]), t.Linha);

            saida.Add(t);
        }

        if (pilha.Count > 0)
            throw new ErroDeSintaxe("este #if (ou #ifdef) nao tem o #endif que o fecha", pilha[^1].Linha);
    }

    static (string Nome, string Resto) Partir(string texto)
    {
        int k = 0;
        while (k < texto.Length && (char.IsLetterOrDigit(texto[k]) || texto[k] == '_')) k++;
        return (texto[..k], texto[k..].Trim());
    }

    static string PrimeiroNome(string texto)
    {
        var (nome, _) = Partir(texto.TrimStart('(', ' ', '\t'));
        return nome;
    }

    static void Diretiva(Token d, List<Condicao> pilha, bool ativo, Preprocessador pp,
                         int arquivo, List<Token> saida)
    {
        var (nome, resto) = Partir(d.Texto);

        switch (nome)
        {
            case "if":
                pilha.Add(Nova(ativo && AvaliarCondicao(resto, d.Linha, pp)));
                return;

            case "ifdef":
                pilha.Add(Nova(ativo && pp.Macros.ContainsKey(PrimeiroNome(resto))));
                return;

            case "ifndef":
                pilha.Add(Nova(ativo && !pp.Macros.ContainsKey(PrimeiroNome(resto))));
                return;

            case "elif":
            case "elifdef":
            case "elifndef":
            {
                var topo = Topo(pilha, "#" + nome, d.Linha);
                if (topo.TeveElse)
                    throw new ErroDeSintaxe($"#{nome} depois do #else", d.Linha);

                if (!topo.PaiAtivo || topo.JaTomado) { topo.Ativo = false; return; }

                bool vale = nome switch
                {
                    "elifdef" => pp.Macros.ContainsKey(PrimeiroNome(resto)),
                    "elifndef" => !pp.Macros.ContainsKey(PrimeiroNome(resto)),
                    _ => AvaliarCondicao(resto, d.Linha, pp),
                };
                topo.Ativo = vale;
                topo.JaTomado |= vale;
                return;
            }

            case "else":
            {
                var topo = Topo(pilha, "#else", d.Linha);
                if (topo.TeveElse) throw new ErroDeSintaxe("dois #else no mesmo #if", d.Linha);
                topo.TeveElse = true;
                topo.Ativo = topo.PaiAtivo && !topo.JaTomado;
                topo.JaTomado = true;
                return;
            }

            case "endif":
                Topo(pilha, "#endif", d.Linha);
                pilha.RemoveAt(pilha.Count - 1);
                return;
        }

        Condicao Nova(bool vale) => new()
        {
            Ativo = vale, JaTomado = vale, PaiAtivo = ativo, Linha = d.Linha,
        };

        if (!ativo) return;

        switch (nome)
        {
            case "define":
                Definir(resto, d.Linha, pp);
                return;

            case "undef":
                pp.Macros.Remove(PrimeiroNome(resto));
                return;

            case "include":
            case "include_next":
            case "import":
                Incluir(resto, d.Linha, pp, arquivo, saida);
                return;

            // O #error de verdade para a compilacao -- e e assim que uma
            // biblioteca diz "nao rodo nesta placa". Ignorar seria rodar
            // no simulador o que nem compila na bancada.
            case "error":
                throw new ErroDeSintaxe("#error " + resto.Trim().Trim('"'), d.Linha);

            case "warning":
                pp.Avisos.Add($"{Origens.Onde(d.Linha)}: #warning {resto.Trim().Trim('"')}");
                return;

            case "pragma":
                if (PrimeiroNome(resto) == "once") pp.UmaVezSo.Add(arquivo);
                return;

            // #line, #ident, a diretiva vazia e as que nao mudam nada aqui.
            default:
                return;
        }
    }

    static Condicao Topo(List<Condicao> pilha, string diretiva, int linha)
    {
        if (pilha.Count == 0)
            throw new ErroDeSintaxe($"{diretiva} sem o #if (ou #ifdef) que ele fecha", linha);
        return pilha[^1];
    }

    /// <summary>"#define NOME corpo" e "#define NOME(a, b) corpo".</summary>
    static void Definir(string texto, int linha, Preprocessador pp)
    {
        var (nome, _) = Partir(texto);
        if (nome.Length == 0) throw new ErroDeSintaxe("#define sem nome", linha);

        string depois = texto[nome.Length..];

        // Parametros so quando o parentese vem COLADO no nome: com espaco,
        // "#define X (1 + 2)" e uma macro sem parametros que vale (1 + 2).
        if (depois.StartsWith('('))
        {
            int fecha = depois.IndexOf(')');
            if (fecha < 0) throw new ErroDeSintaxe($"#define {nome}( sem o ')'", linha);

            var parametros = new List<string>();
            bool variadica = false;
            foreach (string bruto in depois[1..fecha].Split(','))
            {
                string p = bruto.Trim();
                if (p.Length == 0) continue;
                if (p == "...") { variadica = true; parametros.Add("__VA_ARGS__"); continue; }
                if (p.EndsWith("...")) { variadica = true; p = p[..^3].Trim(); }
                parametros.Add(p);
            }

            pp.Macros[nome] = new Macro
            {
                Nome = nome,
                Parametros = parametros,
                Variadica = variadica,
                Corpo = TokensDeTexto(depois[(fecha + 1)..].Trim(), linha),
            };
            return;
        }

        pp.Macros[nome] = new Macro { Nome = nome, Corpo = TokensDeTexto(depois.Trim(), linha) };
    }

    static void Incluir(string resto, int linha, Preprocessador pp, int arquivo, List<Token> saida)
    {
        string alvo = resto.Trim();

        // "#include CABECALHO": o nome vem de uma macro.
        if (alvo.Length > 0 && alvo[0] != '<' && alvo[0] != '"')
        {
            var expandido = Reexpandir(TokensDeTexto(alvo, linha), pp, [], 0);
            alvo = expandido.Count == 1 && expandido[0].Tipo == T.Str
                ? "\"" + expandido[0].Texto + "\""
                : string.Concat(expandido.Select(t => t.Texto));
        }

        bool comAspas;
        string nome;
        if (alvo.StartsWith('<'))
        {
            int fecha = alvo.IndexOf('>');
            if (fecha < 0) throw new ErroDeSintaxe("#include <... sem o '>'", linha);
            nome = alvo[1..fecha].Trim();
            comAspas = false;
        }
        else if (alvo.StartsWith('"'))
        {
            int fecha = alvo.IndexOf('"', 1);
            if (fecha < 0) throw new ErroDeSintaxe("#include \"... sem as aspas do fim", linha);
            nome = alvo[1..fecha].Trim();
            comAspas = true;
        }
        else return;

        var achado = pp.Resolver?.Invoke(nome, comAspas, arquivo);
        if (achado is null || pp.UmaVezSo.Contains(achado.Indice)) return;

        if (pp.Fundo > 40)
            throw new ErroDeSintaxe(
                $"#include \"{nome}\" entrou em arquivos demais, um dentro do outro. "
                + "Um arquivo inclui o outro de volta sem #pragma once ou #ifndef?", linha);

        pp.Fundo++;
        try
        {
            var crus = Cru(achado.Texto, achado.Indice, corpoDeMacro: false, out _);
            Processar(crus, pp, achado.Indice, saida);
        }
        finally { pp.Fundo--; }
    }

    // ---------------- expansao ----------------

    /// <summary>
    /// Nomes que so dizem algo ao compilador: PROGMEM guarda o texto na
    /// flash, IRAM_ATTR poe a rotina na RAM interna. Deixar passar fazia o
    /// parser ver dois nomes seguidos -- e e assim que os tutoriais de
    /// servidor web declaram a pagina.
    /// </summary>
    static bool Enfeite(string nome)
        => nome is "PROGMEM" or "IRAM_ATTR" or "ICACHE_RAM_ATTR" or "ICACHE_FLASH_ATTR"
               or "RAMFUNC" or "__always_inline" or "ARDUINO_ISR_ATTR" or "DRAM_ATTR";

    /// <summary>
    /// Um nome do arquivo: macro vira o que vale, o resto passa. Devolve o
    /// ultimo indice consumido -- a macro com parametros come os
    /// argumentos que vem depois dela.
    /// </summary>
    static int ExpandirNoFluxo(List<Token> crus, int i, Preprocessador pp, int arquivo, List<Token> saida)
    {
        var t = crus[i];

        if (Enfeite(t.Texto)) return i;

        if (t.Texto == "__LINE__")
        {
            saida.Add(new Token(T.Num, Origens.Linha(t.Linha).ToString(), Origens.Linha(t.Linha), true, t.Linha));
            return i;
        }
        if (t.Texto == "__FILE__")
        {
            saida.Add(new Token(T.Str, Origens.Nome(arquivo) ?? "sketch.ino", 0, false, t.Linha));
            return i;
        }

        if (!pp.Macros.TryGetValue(t.Texto, out var macro))
        {
            saida.Add(t);
            return i;
        }

        List<List<Token>> args = null;
        int fim = i;

        if (macro.Parametros is not null)
        {
            // Macro com parametros escrita sem parenteses e so o nome.
            if (i + 1 >= crus.Count || !crus[i + 1].E("(") || crus[i + 1].Tipo != T.Op)
            {
                saida.Add(t);
                return i;
            }

            args = ColherArgumentos(crus, i + 1, out fim, t.Linha);
        }

        var corpo = Substituir(macro, args, pp, t.Linha, []);
        foreach (var x in Reexpandir(corpo, pp, [macro.Nome], 0))
            saida.Add(x with { Linha = t.Linha });

        return fim;
    }

    /// <summary>
    /// Os argumentos entre os parenteses de uma chamada de macro, a partir
    /// do "(" em <paramref name="abre"/>. Virgula dentro de parentese a mais
    /// nao separa: MAX(f(a, b), c) tem dois argumentos.
    /// </summary>
    static List<List<Token>> ColherArgumentos(List<Token> ts, int abre, out int fim, int linha)
    {
        var args = new List<List<Token>> { new() };
        int fundo = 0;

        for (int k = abre + 1; k < ts.Count; k++)
        {
            var x = ts[k];
            if (x.Tipo == T.Diretiva) continue;

            if (x.Tipo == T.Op && x.Texto is "(" or "[" or "{") fundo++;
            else if (x.Tipo == T.Op && x.Texto is ")" or "]" or "}")
            {
                if (fundo == 0 && x.Texto == ")")
                {
                    fim = k;
                    if (args.Count == 1 && args[0].Count == 0) args.Clear();
                    return args;
                }
                fundo--;
            }
            else if (fundo == 0 && x.Tipo == T.Op && x.Texto == ",")
            {
                args.Add([]);
                continue;
            }

            args[^1].Add(x);
        }

        throw new ErroDeSintaxe("a chamada da macro nao fecha o parentese", linha);
    }

    static List<Token> Substituir(Macro macro, List<List<Token>> args, Preprocessador pp,
                                  int linha, HashSet<string> desligadas)
    {
        if (macro.Parametros is null) return [.. macro.Corpo];

        args ??= [];
        var valores = new Dictionary<string, List<Token>>(StringComparer.Ordinal);
        int fixos = macro.Variadica ? macro.Parametros.Count - 1 : macro.Parametros.Count;

        if (args.Count < fixos && !(fixos == 1 && args.Count == 0))
            throw new ErroDeSintaxe(
                $"a macro {macro.Nome}() pede {fixos} argumento(s), e veio {args.Count}", linha);
        if (!macro.Variadica && args.Count > fixos && fixos > 0)
            throw new ErroDeSintaxe(
                $"a macro {macro.Nome}() pede {fixos} argumento(s), e veio {args.Count}", linha);

        for (int k = 0; k < fixos; k++)
            valores[macro.Parametros[k]] = k < args.Count ? args[k] : [];

        if (macro.Variadica)
        {
            var resto = new List<Token>();
            for (int k = fixos; k < args.Count; k++)
            {
                if (k > fixos) resto.Add(new Token(T.Op, ",", 0, false, linha));
                resto.AddRange(args[k]);
            }
            valores[macro.Parametros[^1]] = resto;
        }

        var corpo = macro.Corpo;
        var saida = new List<Token>();

        for (int k = 0; k < corpo.Count; k++)
        {
            var x = corpo[k];

            // ", ## __VA_ARGS__": com o __VA_ARGS__ vazio, a virgula some.
            // E o jeito classico de escrever uma macro de log, e sem isto
            // LOG("oi") virava printf("oi", ).
            if (macro.Variadica && x.Tipo == T.Op && x.Texto == "," && k + 2 < corpo.Count
                && corpo[k + 1].E("##") && corpo[k + 2].Texto == macro.Parametros[^1])
            {
                var sobra = valores[macro.Parametros[^1]];
                if (sobra.Count > 0)
                {
                    saida.Add(x);
                    saida.AddRange(sobra);
                }
                k += 2;
                continue;
            }

            // "#x": o argumento vira texto.
            if (x.Tipo == T.Op && x.Texto == "#" && k + 1 < corpo.Count
                && corpo[k + 1].Tipo == T.Id && valores.TryGetValue(corpo[k + 1].Texto, out var comoTexto))
            {
                saida.Add(new Token(T.Str, Texto(comoTexto), 0, false, linha));
                k++;
                continue;
            }

            if (x.Tipo == T.Id && valores.TryGetValue(x.Texto, out var valor))
            {
                bool colado = (k > 0 && corpo[k - 1].E("##")) || (k + 1 < corpo.Count && corpo[k + 1].E("##"));
                saida.AddRange(colado ? valor : Reexpandir(valor, pp, desligadas, 0));
                continue;
            }

            saida.Add(x);
        }

        return Colar(saida, linha);
    }

    /// <summary>"a ## b" junta os dois num token so.</summary>
    static List<Token> Colar(List<Token> ts, int linha)
    {
        if (!ts.Any(t => t.Tipo == T.Op && t.Texto == "##")) return ts;

        var saida = new List<Token>();
        for (int k = 0; k < ts.Count; k++)
        {
            if (!(ts[k].Tipo == T.Op && ts[k].Texto == "##")) { saida.Add(ts[k]); continue; }

            if (k + 1 >= ts.Count) break;
            var direita = ts[++k];

            if (saida.Count == 0) { saida.Add(direita); continue; }
            var esquerda = saida[^1];

            var juntos = TokensDeTexto(esquerda.Texto + direita.Texto, linha);
            saida.RemoveAt(saida.Count - 1);
            saida.AddRange(juntos.Count > 0 ? juntos : [esquerda, direita]);
        }
        return saida;
    }

    static string Texto(List<Token> ts)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var t in ts)
        {
            if (sb.Length > 0) sb.Append(' ');
            sb.Append(t.Tipo switch
            {
                T.Str => "\"" + t.Texto.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"",
                T.Char => "'" + t.Texto + "'",
                _ => t.Texto,
            });
        }
        return sb.ToString();
    }

    /// <summary>
    /// Expande de novo o que uma macro virou. A macro nao se expande
    /// dentro de si mesma -- "#define LED LED" nao fica em laco --, que e
    /// a regra do C.
    /// </summary>
    static List<Token> Reexpandir(List<Token> ts, Preprocessador pp, HashSet<string> desligadas, int fundo)
    {
        if (fundo > 64) return ts;

        var saida = new List<Token>();
        for (int k = 0; k < ts.Count; k++)
        {
            var t = ts[k];
            if (t.Tipo != T.Id || desligadas.Contains(t.Texto) || !pp.Macros.TryGetValue(t.Texto, out var macro))
            {
                if (t.Tipo != T.Id || !Enfeite(t.Texto)) saida.Add(t);
                continue;
            }

            List<List<Token>> args = null;
            if (macro.Parametros is not null)
            {
                if (k + 1 >= ts.Count || !(ts[k + 1].Tipo == T.Op && ts[k + 1].Texto == "("))
                {
                    saida.Add(t);
                    continue;
                }
                args = ColherArgumentos(ts, k + 1, out int fim, t.Linha);
                k = fim;
            }

            var mais = new HashSet<string>(desligadas) { macro.Nome };
            var corpo = Substituir(macro, args, pp, t.Linha, mais);
            foreach (var x in Reexpandir(corpo, pp, mais, fundo + 1))
                saida.Add(x with { Linha = t.Linha });
        }
        return saida;
    }

    // ---------------- #if ----------------

    static bool AvaliarCondicao(string texto, int linha, Preprocessador pp)
    {
        var ts = TokensDeTexto(texto, linha);
        ts = TrocarDefined(ts, pp, linha);
        ts = Reexpandir(ts, pp, [], 0);
        ts = TrocarDefined(ts, pp, linha);

        if (ts.Count == 0) throw new ErroDeSintaxe("#if sem condicao", linha);

        // O que sobrou de nome depois das macros vale zero -- e a regra
        // do C, e e o que faz "#if VERSAO > 2" funcionar sem VERSAO.
        for (int k = 0; k < ts.Count; k++)
            if (ts[k].Tipo == T.Id)
                ts[k] = new Token(T.Num, ts[k].Texto, ts[k].Texto == "true" ? 1 : 0, true, linha);

        int p = 0;
        long valor = new ContaDoIf(ts, linha).Ler(ref p);
        if (p < ts.Count)
            throw new ErroDeSintaxe($"sobrou '{ts[p].Texto}' na condicao do #if", linha);
        return valor != 0;
    }

    static List<Token> TrocarDefined(List<Token> ts, Preprocessador pp, int linha)
    {
        var saida = new List<Token>();
        for (int k = 0; k < ts.Count; k++)
        {
            var t = ts[k];
            if (t.Tipo == T.Id && t.Texto == "defined")
            {
                string nome;
                if (k + 1 < ts.Count && ts[k + 1].E("("))
                {
                    if (k + 3 >= ts.Count || !ts[k + 3].E(")"))
                        throw new ErroDeSintaxe("defined( sem o ')'", linha);
                    nome = ts[k + 2].Texto;
                    k += 3;
                }
                else if (k + 1 < ts.Count) nome = ts[++k].Texto;
                else throw new ErroDeSintaxe("defined sem nome", linha);

                saida.Add(new Token(T.Num, "defined", pp.Macros.ContainsKey(nome) ? 1 : 0, true, linha));
                continue;
            }

            if (t.Tipo == T.Id && t.Texto is "__has_include" or "__has_include_next"
                && k + 1 < ts.Count && ts[k + 1].E("("))
            {
                var dentro = ColherArgumentos(ts, k + 1, out int fim, linha);
                k = fim;
                string pedido = dentro.Count == 0 ? "" : string.Concat(dentro[0].Select(x => x.Tipo == T.Str ? "\"" + x.Texto + "\"" : x.Texto));
                bool comAspas = pedido.StartsWith('"');
                string nome = pedido.Trim('<', '>', '"', ' ');
                bool existe = pp.Resolver?.Invoke(nome, comAspas, 0) is not null
                              || (pp.Conhecido?.Invoke(nome) ?? false);
                saida.Add(new Token(T.Num, "__has_include", existe ? 1 : 0, true, linha));
                continue;
            }

            saida.Add(t);
        }
        return saida;
    }

    /// <summary>A conta de um #if: so inteiros, com a precedencia do C.</summary>
    sealed class ContaDoIf(List<Token> ts, int linha)
    {
        static readonly string[][] Niveis =
        [
            ["||"], ["&&"], ["|"], ["^"], ["&"], ["==", "!="], ["<", ">", "<=", ">="],
            ["<<", ">>"], ["+", "-"], ["*", "/", "%"]
        ];

        public long Ler(ref int p)
        {
            long c = Binario(0, ref p);
            if (p < ts.Count && ts[p].E("?"))
            {
                p++;
                long sim = Ler(ref p);
                if (p >= ts.Count || !ts[p].E(":")) throw new ErroDeSintaxe("'?' sem ':' no #if", linha);
                p++;
                long nao = Ler(ref p);
                return c != 0 ? sim : nao;
            }
            return c;
        }

        long Binario(int nivel, ref int p)
        {
            if (nivel >= Niveis.Length) return Unario(ref p);
            long esq = Binario(nivel + 1, ref p);
            while (p < ts.Count && ts[p].Tipo == T.Op && Niveis[nivel].Contains(ts[p].Texto))
            {
                string op = ts[p++].Texto;
                long dir = Binario(nivel + 1, ref p);
                esq = op switch
                {
                    "||" => (esq != 0 || dir != 0) ? 1 : 0,
                    "&&" => (esq != 0 && dir != 0) ? 1 : 0,
                    "|" => esq | dir,
                    "^" => esq ^ dir,
                    "&" => esq & dir,
                    "==" => esq == dir ? 1 : 0,
                    "!=" => esq != dir ? 1 : 0,
                    "<" => esq < dir ? 1 : 0,
                    ">" => esq > dir ? 1 : 0,
                    "<=" => esq <= dir ? 1 : 0,
                    ">=" => esq >= dir ? 1 : 0,
                    "<<" => esq << (int)dir,
                    ">>" => esq >> (int)dir,
                    "+" => esq + dir,
                    "-" => esq - dir,
                    "*" => esq * dir,
                    "/" => dir == 0 ? throw new ErroDeSintaxe("divisao por zero no #if", linha) : esq / dir,
                    "%" => dir == 0 ? throw new ErroDeSintaxe("resto por zero no #if", linha) : esq % dir,
                    _ => esq,
                };
            }
            return esq;
        }

        long Unario(ref int p)
        {
            if (p >= ts.Count) throw new ErroDeSintaxe("a condicao do #if acabou no meio", linha);
            var t = ts[p];

            if (t.Tipo == T.Op)
            {
                switch (t.Texto)
                {
                    case "!": p++; return Unario(ref p) == 0 ? 1 : 0;
                    case "~": p++; return ~Unario(ref p);
                    case "-": p++; return -Unario(ref p);
                    case "+": p++; return Unario(ref p);
                    case "(":
                    {
                        p++;
                        long v = Ler(ref p);
                        if (p >= ts.Count || !ts[p].E(")")) throw new ErroDeSintaxe("'(' sem ')' no #if", linha);
                        p++;
                        return v;
                    }
                }
            }

            if (t.Tipo is T.Num or T.Char) { p++; return (long)t.Valor; }

            throw new ErroDeSintaxe($"'{t.Texto}' nao cabe numa condicao de #if", linha);
        }
    }
}
