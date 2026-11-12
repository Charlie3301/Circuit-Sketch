namespace Simulador;

/// <summary>
/// A porta de entrada do motor: texto do sketch -> <see cref="Programa"/>.
///
/// Existe para que o resto do simulador nao precise saber em que
/// linguagem o aluno escreveu. A janela, o depurador, os testes e o
/// modo de linha de comando pedem "compila isto aqui" e recebem a mesma
/// arvore, venha ela do C++ do Arduino ou do MicroPython.
/// </summary>
public static class Compilador
{
    /// <summary>
    /// Em que linguagem este sketch esta escrito.
    ///
    /// Por sinais, e nao por extensao de arquivo: o codigo vive DENTRO do
    /// .sktch, e o aluno troca de linguagem sem trocar de arquivo. Os
    /// sinais sao os que nao existem na outra linguagem -- "#include" e
    /// "void setup()" de um lado, "import" e "def" do outro -- e o
    /// desempate final e o ponto-e-virgula no fim da linha, que o Python
    /// nao usa.
    /// </summary>
    public static Linguagem Detectar(string fonte)
    {
        if (string.IsNullOrWhiteSpace(fonte)) return Linguagem.Arduino;

        var linhas = fonte.Replace("\r\n", "\n").Split('\n');
        int arduino = 0, python = 0;

        foreach (string bruta in linhas)
        {
            string linha = bruta.Trim();
            if (linha.Length == 0 || linha.StartsWith("//") || linha.StartsWith('#')
                && !linha.StartsWith("#include") && !linha.StartsWith("#define"))
                continue;

            if (linha.StartsWith("#include") || linha.StartsWith("#define")) arduino += 3;
            if (linha.Contains("void setup()") || linha.Contains("void loop()")) arduino += 3;
            if (linha.EndsWith(';') || linha.EndsWith('{') || linha.EndsWith('}')) arduino++;

            if (linha.StartsWith("import ") || linha.StartsWith("from ")) python += 3;
            if (linha.StartsWith("def ") && linha.EndsWith(':')) python += 3;
            if (linha.StartsWith("print(") || linha.StartsWith("elif ")) python += 2;

            // Um bloco aberto com dois-pontos e sem chave: so Python.
            if (linha.EndsWith(':') && !linha.Contains('?')
                && (linha.StartsWith("if ") || linha.StartsWith("for ")
                    || linha.StartsWith("while ") || linha.StartsWith("else")
                    || linha.StartsWith("try") || linha.StartsWith("with ")))
                python += 2;
        }

        // As outras familias sao decididas por sinais PROPRIOS, antes
        // do desempate entre C e Python: um programa em Ada tem muito
        // ponto-e-virgula e seria lido como C++ se chegasse ate la.
        var outra = OutraFamilia(fonte);
        if (outra is { } escolhida) return escolhida;

        if (python > arduino) return Linguagem.MicroPython;

        return EhCPuro(fonte) ? Linguagem.C : Linguagem.Arduino;
    }

    /// <summary>
    /// Assembly, Rust, C# ou a familia Pascal -- ou null.
    ///
    /// Cada uma e reconhecida por marcas que as outras nao tem. Nao e
    /// por extensao de arquivo porque o codigo mora DENTRO do .sktch, e
    /// a pessoa troca de linguagem sem trocar de arquivo.
    /// </summary>
    static Linguagem? OutraFamilia(string fonte)
    {
        string texto = fonte.Replace("\r\n", "\n");
        string minusculo = texto.ToLowerInvariant();

        // ---- Assembly ----
        //
        // O sinal mais seguro e a diretiva: ".org", ".equ", ".global",
        // "section .text". Nenhuma outra linguagem da lista usa ponto
        // no comeco da linha.
        if (Marcas(minusculo, ".org", ".equ", ".global", ".globl", ".section",
                   ".byte", ".word", ".asciz", "section .text") >= 1
            && !minusculo.Contains("void setup"))
            return Linguagem.Assembly;

        // Rotulo seguido de instrucao conhecida, sem nada de C.
        if (System.Text.RegularExpressions.Regex.IsMatch(
                minusculo, @"(^|\n)\s*(ldi|sbi|cbi|rjmp|rcall|brne|breq|nop|out\s+|in\s+)")
            && !minusculo.Contains('{'))
            return Linguagem.Assembly;

        // ---- Rust ----
        if (Marcas(texto, "fn main(", "let mut ", "#![no_std]", "#![no_main]",
                   "->  !", "-> !", "use panic_halt") >= 1
            && texto.Contains("fn "))
            return Linguagem.Rust;

        // ---- C# ----
        if (Marcas(texto, "using System", "static void Main", "namespace ",
                   "Console.WriteLine", "public class ", "GpioController", "Debug.WriteLine",
                   "Thread.Sleep(", "PinValue.", "System.Device.") >= 2)
            return Linguagem.CSharp;

        // ---- familia Pascal ----
        //
        // Primeiro os dialetos, que tem marca so deles; o Ada fica por
        // ultimo, como caso geral da familia.
        //
        // A ordem importa: um programa em Cowgol tem ":=" como todos os
        // da familia, e se o Ada fosse testado antes ficaria com todos.
        //
        // A quebra de linha na frente e para "\nproc " achar o "proc"
        // da PRIMEIRA linha tambem, que e onde ele costuma estar.
        string comQuebra = "\n" + minusculo;

        // SPARK e Ada com contratos. A marca e o pragma ou o "with Pre".
        if (Marcas(minusculo, "spark_mode", "with pre", "with post", "with global",
                   "pre =>", "post =>") >= 1)
            return Linguagem.Spark;

        if (Marcas(comQuebra, "\nsub ", "end sub", "@extern") >= 1)
            return Linguagem.Cowgol;

        if (Marcas(comQuebra, "\nproc ", "end proc", "\nset ", "\ndata ") >= 1)
            return Linguagem.Millipascal;

        // Ada: o ":=" e a marca da familia inteira -- nenhuma das
        // outras linguagens da lista atribui assim -- mais uma palavra
        // que so a familia usa. As duas juntas, porque ":=" sozinho
        // aparece em pseudocodigo dentro de comentario.
        bool pascal = texto.Contains(":=")
                      && Marcas(comQuebra, "\nbegin", "end loop", "end if", "end;",
                                "procedure ", "function ", " loop", " then") >= 1;

        return pascal ? Linguagem.Ada : null;
    }

    static int Marcas(string texto, params string[] marcas)
        => marcas.Count(texto.Contains);

    /// <summary>
    /// C puro, e nao um sketch.
    ///
    /// O sinal que decide e o ponto de entrada: sketch tem setup() e
    /// loop(), programa em C tem main(). O include do SDK confirma,
    /// mas sozinho nao bastaria -- da para incluir o pico/stdlib.h
    /// dentro de um sketch do Arduino, e nesse caso ele continua sendo
    /// um sketch.
    /// </summary>
    static bool EhCPuro(string fonte)
    {
        if (fonte.Contains("void setup(") || fonte.Contains("void loop(")) return false;

        return fonte.Contains("int main(") || fonte.Contains("void main(");
    }

    /// <summary>Compila pela linguagem detectada.</summary>
    public static Programa Compilar(string fonte)
        => Compilar(fonte, Detectar(fonte ?? ""));

    /// <summary>
    /// Compila o projeto inteiro: o sketch, as abas e as bibliotecas que
    /// ele inclui, com as macros da placa. E o caminho do botao Rodar.
    ///
    /// So o C e o C++ tem #include e pre-processador; as outras
    /// linguagens compilam o sketch sozinho, como antes.
    /// </summary>
    public static Programa CompilarProjeto(Projeto projeto)
    {
        string fonte = projeto?.Sketch ?? "";
        var linguagem = Detectar(fonte);

        // O C# de verdade: o Roslyn (CompiladorCSharp). O erro dele vale -- o
        // caminho antigo aceitava o que o C# recusa (int x = "texto").
        if (CppNativo && linguagem == Linguagem.CSharp)
            return new Programa([], [])
            {
                Linguagem = linguagem,
                Placa = projeto.Placa,
                Fonte = fonte,
                Compilado = CompiladorCSharp.Compilar(projeto),
            };

        // O Python de verdade: o MicroPython, o mesmo firmware da placa, compilado junto do core.
        if (CppNativo && linguagem == Linguagem.MicroPython && CompiladorNativo.Disponivel && CompiladorNativo.TemMicroPython(projeto.Placa, fonte))
            return new Programa([], [])
            {
                Linguagem = linguagem,
                Placa = projeto.Placa,
                Fonte = fonte,
                Compilado = CompiladorNativo.Compilar(CompiladorNativo.ProjetoDoMicroPython(projeto), AvisoDoCompilador),
            };

        if (linguagem is not (Linguagem.Arduino or Linguagem.C))
            return Compilar(fonte, linguagem);

        // O C/C++ de verdade: o projeto inteiro pelo clang (CompiladorNativo). Quem decide e
        // ele: "#ifdef __AVR__ #include <avr/power.h>" nao vale na ESP32, e so o
        // pre-processador sabe disso. Se ele recusar e o interpretador aceitar, fica o
        // interpretador, que roda o projeto de antes (o PORTB do Uno); se os dois
        // recusarem, vale o erro do clang -- e o da IDE.
        if (CppNativo && CompiladorNativo.Disponivel)
        {
            try
            {
                var compilado = CompiladorNativo.Compilar(projeto, AvisoDoCompilador);
                return new Programa([], [])
                {
                    Linguagem = linguagem,
                    Placa = projeto.Placa,
                    Fonte = fonte,
                    Compilado = compilado,
                };
            }
            catch (ErroDeSintaxe erroDoClang)
            {
                Programa interpretado;
                try { interpretado = new Unidades(projeto, linguagem).Compilar(); }
                catch (ErroDeSintaxe erroDoInterpretador)
                {
                    // O projeto que so roda no interpretador (o PORTB do Uno) e tem um erro
                    // proprio: o do interpretador e que aponta a linha certa, e nao o clang
                    // reclamando do PORTB, que esta certo.
                    if (CompiladorNativo.PorQueNao(projeto) is { } porque && !porque.StartsWith("usa ", StringComparison.Ordinal) || CompiladorNativo.SoNoInterpretador(projeto))
                        if (Origens.DoSketch(erroDoInterpretador.Linha)) throw;
                    throw CompiladorNativo.ErroLegivel(erroDoClang);
                }
                // A pessoa precisa saber: o erro que aparecer dai em diante e
                // do interpretador, e o motivo verdadeiro e este. Quem usa o que so ele
                // tem (o registrador do AVR) ja esperava por isso.
                if (CompiladorNativo.PorQueNao(projeto) is null)
                    AvisoDoCompilador?.Invoke(Idioma.T("o compilador de C++ recusou ({0}: {1}); rodando no interpretador, que nao tem tudo do C++.", Origens.Onde(erroDoClang.Linha), erroDoClang.Message));
                return interpretado;
            }
        }

        return new Unidades(projeto, linguagem).Compilar();
    }

    /// <summary>
    /// Compilar o C/C++ de verdade (o clang do Zig) em vez de interpretar.
    /// A janela liga; os testes antigos rodam interpretados, como sempre.
    /// </summary>
    public static bool CppNativo { get; set; }

    /// <summary>Onde o compilador nativo avisa o que esta fazendo ("compilando o core...").</summary>
    public static Action<string> AvisoDoCompilador { get; set; }

    /// <summary>Compila numa linguagem escolhida.</summary>
    /// <summary>
    /// Compila numa linguagem escolhida.
    ///
    /// O C e o C++ caem no MESMO analisador, de proposito: um sketch ja
    /// e C++, e um programa em C e um subconjunto dele. Dois
    /// analisadores dariam duas gramaticas para manter e duas verdades
    /// sobre o que "int x[4]" significa.
    /// </summary>
    public static Programa Compilar(string fonte, Linguagem linguagem)
    {
        fonte ??= "";

        switch (linguagem)
        {
            case Linguagem.MicroPython:
                return new ParserPython(LexerPython.Tokenizar(fonte)).AnalisarPrograma();

            case Linguagem.Ada:
            case Linguagem.Spark:
            case Linguagem.Cowgol:
            case Linguagem.Millipascal:
            {
                var dialeto = linguagem switch
                {
                    Linguagem.Spark => DialetoPascal.Spark,
                    Linguagem.Cowgol => DialetoPascal.Cowgol,
                    Linguagem.Millipascal => DialetoPascal.Millipascal,
                    _ => DialetoPascal.Ada,
                };

                return new ParserPascal(
                    LexerPascal.Tokenizar(fonte, dialeto), dialeto).Analisar();
            }

            case Linguagem.Rust:
                return new ParserRust(LexerRust.Tokenizar(fonte)).Analisar();

            case Linguagem.CSharp:
                return DoMain(new Parser(Lexer.Tokenizar(LimparCSharp(fonte)))
                                  .AnalisarPrograma()) with { Linguagem = Linguagem.CSharp };

            case Linguagem.Assembly:
                return Montador.Montar(fonte);

            default:
            {
                var pp = Preprocessador.DaPlaca(null, linguagem);
                var prog = DoMain(new Parser(Lexer.Tokenizar(fonte, pp, 0)).AnalisarPrograma());
                return pp.Avisos.Count == 0 ? prog : prog with { Avisos = [.. pp.Avisos] };
            }
        }
    }

    /// <summary>
    /// Programa em C puro: o main() vira setup() e loop().
    ///
    /// O SDK da Raspberry, o C de verdade e qualquer programa que nao
    /// seja um sketch comecam em <c>int main()</c>, e nao em
    /// setup/loop. Dentro dele ha sempre um laco infinito -- e o unico
    /// jeito de um microcontrolador nao parar --, entao a conversao e
    /// exata e nao inventa nada: o que vem ANTES do laco e o setup, e o
    /// corpo do laco e o loop.
    ///
    /// E a mesma ideia que a <see cref="ParserPython.MontarPrograma"/>
    /// usa para o "while True:" do MicroPython. Feita aqui, o resto do
    /// simulador -- interpretador, depurador, maquina -- continua
    /// vendo o unico formato que ele conhece.
    /// </summary>
    static Programa DoMain(Programa prog) => PartirNoLaco(prog, "main");

    /// <summary>
    /// Parte o procedimento principal em setup() e loop().
    ///
    /// Serve a seis linguagens -- C, C#, Rust, Ada, SPARK, Cowgol e
    /// Millipascal --, e por isso mora aqui e nao em cada analisador.
    /// Todas elas comecam num procedimento so, com o laco infinito
    /// escrito na mao, porque e assim que firmware se escreve fora do
    /// Arduino.
    ///
    /// A parte delicada nao e partir: e a VARIAVEL declarada antes do
    /// laco. No programa original ela e uma so, viva do comeco ao fim;
    /// depois da partida, setup e loop viram funcoes separadas, com
    /// escopos separados, e a variavel morreria no fim do setup. O
    /// sintoma seria "variavel 'Led' nao foi declarada" numa linha que
    /// esta perfeitamente certa.
    ///
    /// Por isso cada declaracao de antes do laco sobe para global, e o
    /// valor inicial dela FICA no setup, como atribuicao, na mesma
    /// posicao: o nome passa a existir para os dois, e a ordem em que
    /// as contas acontecem nao muda.
    /// </summary>
    public static Programa PartirNoLaco(Programa prog, string nomePrincipal)
    {
        if (prog is null) return null;

        if (prog.Funcoes.Any(f => f.Nome is "setup" or "loop" && f.Corpo is not null)) return prog;

        // So as que tem corpo: "int main(void);" no topo e so a promessa.
        var comCorpo = prog.Funcoes.Where(f => f.Corpo is not null).ToList();
        var principal = comCorpo.FirstOrDefault(f => f.Nome == nomePrincipal)
                        ?? comCorpo.FirstOrDefault(
                               f => f.Nome is "main" or "Main" or "principal")
                        ?? comCorpo.FirstOrDefault(f => f.Classe is null && !f.Nome.Contains("::"));

        if (principal is null) return prog;

        var antes = new List<Cmd>();
        var globais = new List<Cmd>(prog.Globais);
        List<Cmd> dentroDoLaco = null;

        foreach (var cmd in principal.Corpo.Corpo)
        {
            if (dentroDoLaco is null && EhLacoInfinito(cmd, out var corpo))
            {
                dentroDoLaco = corpo;
                continue;
            }

            // Depois do laco infinito nao roda nada na placa de
            // verdade: o "return 0" final e so o C exigindo um.
            if (dentroDoLaco is not null) continue;

            antes.AddRange(Subir(cmd, globais));
        }

        var funcoes = prog.Funcoes.Where(f => f != principal).ToList();

        funcoes.Add(new Funcao("void", "setup", [],
                               new CmdBloco(antes, principal.Linha), principal.Linha));

        funcoes.Add(new Funcao("void", "loop", [],
                               new CmdBloco(dentroDoLaco ?? [], principal.Linha), principal.Linha));

        return prog with { Globais = globais, Funcoes = funcoes };
    }

    /// <summary>
    /// A declaracao sobe para global, e o valor inicial vira atribuicao.
    /// </summary>
    static IEnumerable<Cmd> Subir(Cmd cmd, List<Cmd> globais)
    {
        switch (cmd)
        {
            case CmdDecl decl:
            {
                globais.Add(decl with { Inicial = null });

                if (decl.Inicial is null && decl.Itens is null) yield break;

                // Vetor com lista de valores nao vira atribuicao: ele
                // ja nasce cheio na declaracao global.
                if (decl.Itens is { Count: > 0 })
                {
                    globais[^1] = decl;
                    yield break;
                }

                yield return new CmdExpr(
                    new Atribuicao("=", new Variavel(decl.Nome, decl.Linha),
                                   decl.Inicial, decl.Linha),
                    decl.Linha);

                yield break;
            }

            case CmdVarios varios:
            {
                foreach (var dentro in varios.Corpo)
                    foreach (var saida in Subir(dentro, globais))
                        yield return saida;

                yield break;
            }

            // "Servo meu;" tambem precisa sobreviver ao setup.
            case CmdDeclObj obj:
                globais.Add(obj);
                yield break;

            default:
                yield return cmd;
                yield break;
        }
    }

    /// <summary>"while (1)", "while (true)" e "for (;;)".</summary>
    static bool EhLacoInfinito(Cmd cmd, out List<Cmd> corpo)
    {
        corpo = null;

        switch (cmd)
        {
            case CmdEnquanto e when Sempre(e.Cond):
                corpo = Miolo(e.Corpo);
                return true;

            // "for (;;)" nao tem condicao nenhuma, e e o outro jeito
            // classico de escrever o mesmo laco em C.
            case CmdPara p when p.Cond is null:
                corpo = Miolo(p.Corpo);
                return true;

            default: return false;
        }
    }

    static bool Sempre(Expr cond)
        => cond is Literal { EhTexto: false, Valor: not 0 }
           || cond is Variavel { Nome: "true" };

    static List<Cmd> Miolo(Cmd corpo)
        => corpo is CmdBloco b ? b.Corpo : [corpo];

    /// <summary>
    /// O nome que aparece na interface, para a pessoa saber o que o
    /// simulador entendeu antes de apertar Rodar.
    /// </summary>
    public static string Nome(Linguagem l) => l switch
    {
        Linguagem.MicroPython => "MicroPython",
        Linguagem.C => "C (main)",
        Linguagem.CSharp => "C#",
        Linguagem.Rust => "Rust",
        Linguagem.Ada => "Ada",
        Linguagem.Spark => "SPARK",
        Linguagem.Cowgol => "Cowgol",
        Linguagem.Millipascal => "Millipascal",
        Linguagem.Assembly => "Assembly",
        _ => "Arduino (C++)",
    };

    /// <summary>A extensao que o arquivo teria na IDE.</summary>
    public static string Extensao(Linguagem l) => l switch
    {
        Linguagem.MicroPython => ".py",
        Linguagem.C => ".c",
        Linguagem.CSharp => ".cs",
        Linguagem.Rust => ".rs",
        Linguagem.Ada or Linguagem.Spark => ".adb",
        Linguagem.Cowgol => ".cow",
        Linguagem.Millipascal => ".mp",
        Linguagem.Assembly => ".S",
        _ => ".ino",
    };

    /// <summary>
    /// Todas as linguagens que o simulador entende, na ordem do menu.
    /// </summary>
    public static readonly Linguagem[] Todas =
    [
        Linguagem.Arduino, Linguagem.C, Linguagem.CSharp, Linguagem.MicroPython,
        Linguagem.Rust, Linguagem.Ada, Linguagem.Spark,
        Linguagem.Cowgol, Linguagem.Millipascal, Linguagem.Assembly,
    ];

    /// <summary>
    /// Traduz o C# para o que o analisador de C++ ja entende.
    ///
    /// C# e C++ o bastante para o mesmo analisador dar conta, e um
    /// analisador proprio seria uma segunda gramatica quase igual --
    /// que divergiria da primeira na primeira correcao feita so numa
    /// delas. O que sobra e uma adaptacao de TEXTO, que e o que esta
    /// aqui, e ela faz tres coisas:
    ///
    ///   1. tira o "using" e o "namespace", que nao mudam o que o
    ///      programa faz porque as bibliotecas ja estao dentro;
    ///   2. desembrulha a classe, ficando so com o corpo dela -- um
    ///      programa de microcontrolador em C# e uma classe com um
    ///      Main, e a classe e so embalagem;
    ///   3. troca "Console.WriteLine" por "WriteLine" e
    ///      "Thread.Sleep" por "Sleep", para o Vocabulario reconhecer.
    ///
    /// O que NAO e traduzido -- propriedade, evento, LINQ, generico --
    /// da erro de sintaxe com a linha certa, em vez de rodar errado.
    /// </summary>
    static string LimparCSharp(string fonte)
    {
        var linhas = fonte.Replace("\r\n", "\n").Split('\n').ToList();

        for (int i = 0; i < linhas.Count; i++)
        {
            string linha = linhas[i];
            string cortada = linha.TrimStart();

            if (cortada.StartsWith("using ") && cortada.TrimEnd().EndsWith(";"))
            {
                linhas[i] = "";
                continue;
            }

            foreach (string palavra in new[]
                     { "public ", "private ", "protected ", "internal ", "sealed ",
                       "static ", "readonly ", "partial ", "override ", "virtual ",
                       "abstract ", "async " })
                linha = linha.Replace(palavra, "");

            // O que mora numa classe do .NET e o simulador tem com
            // outro nome. Tirar o prefixo deixa o Vocabulario achar.
            linha = linha
                .Replace("Console.WriteLine", "WriteLine")
                .Replace("Console.Write", "Write")
                .Replace("Thread.Sleep", "Sleep")
                .Replace("System.Threading.Thread.Sleep", "Sleep");

            linhas[i] = VetoresDoCSharp(TextoInterpolado(linha));
        }

        return Desembrulhar(string.Join("\n", linhas));
    }

    /// <summary>
    /// Vetor do C#, escrito como o C++ entende:
    ///
    ///   int[] leds = { 25, 26 };            ->  int leds[] = { 25, 26 };
    ///   int[] leds = new int[] { 25, 26 };  ->  int leds[] = { 25, 26 };
    ///   int[] fila = new int[10];           ->  int fila[10];
    /// </summary>
    static string VetoresDoCSharp(string linha)
    {
        linha = System.Text.RegularExpressions.Regex.Replace(linha,
            @"\b([A-Za-z_]\w*)\s*\[\s*\]\s+([A-Za-z_]\w*)\s*=\s*new\s+\1\s*\[\s*([^\]]+?)\s*\]\s*;",
            "$1 $2[$3];");
        return System.Text.RegularExpressions.Regex.Replace(linha,
            @"\b([A-Za-z_]\w*)\s*\[\s*\]\s+([A-Za-z_]\w*)\s*=\s*(?:new\s+\1\s*\[\s*\]\s*)?\{",
            "$1 $2[] = {");
    }

    /// <summary>
    /// $"leitura {x}, {y:0.0} V" vira "leitura " + String(x) + ", " +
    /// String(y) + " V". O formato depois dos dois pontos e ignorado -- o
    /// numero sai inteiro, com as casas que tiver.
    /// </summary>
    static string TextoInterpolado(string linha)
    {
        return System.Text.RegularExpressions.Regex.Replace(linha, "\\$\"((?:[^\"\\\\]|\\\\.)*)\"", m =>
        {
            var partes = new List<string>();
            string corpo = m.Groups[1].Value;
            var texto = new System.Text.StringBuilder();
            int i = 0;
            while (i < corpo.Length)
            {
                if (corpo[i] == '{' && i + 1 < corpo.Length && corpo[i + 1] == '{') { texto.Append('{'); i += 2; continue; }
                if (corpo[i] == '}' && i + 1 < corpo.Length && corpo[i + 1] == '}') { texto.Append('}'); i += 2; continue; }
                if (corpo[i] != '{') { texto.Append(corpo[i++]); continue; }

                int fim = corpo.IndexOf('}', i);
                if (fim < 0) { texto.Append(corpo[i..]); break; }
                string expressao = corpo[(i + 1)..fim];
                int formato = expressao.IndexOf(':');
                if (formato > 0) expressao = expressao[..formato];

                if (texto.Length > 0) partes.Add("\"" + texto + "\"");
                texto.Clear();
                partes.Add("String(" + expressao.Trim() + ")");
                i = fim + 1;
            }
            if (texto.Length > 0 || partes.Count == 0) partes.Add("\"" + texto + "\"");
            return "(" + string.Join(" + ", partes) + ")";
        });
    }

    /// <summary>
    /// Tira a classe e o namespace de fora, ficando com o corpo.
    ///
    /// O analisador de C++ nao aceita chave solta no topo do arquivo,
    /// entao apagar a linha da classe e deixar as chaves dela para tras
    /// daria "esperava uma declaracao, encontrei '{'" -- numa linha que
    /// a pessoa nao escreveu.
    /// </summary>
    static string Desembrulhar(string fonte)
    {
        string atual = fonte;

        // Duas camadas bastam: namespace por fora, classe por dentro.
        // Mais do que isso e classe aninhada, que um programa de placa
        // nao costuma ter.
        for (int camada = 0; camada < 2; camada++)
        {
            var achado = System.Text.RegularExpressions.Regex.Match(
                atual, @"(?m)^\s*(namespace|class|struct)\s+[^{;]*\{");

            if (!achado.Success) break;

            int abre = achado.Index + achado.Length - 1;
            int fecha = ParDaChave(atual, abre);
            if (fecha < 0) break;

            // O miolo fica, e as duas chaves viram espaco em branco --
            // e nao somem --, para o numero das linhas nao mudar. Uma
            // mensagem de erro que aponta a linha errada e pior do que
            // uma mensagem sem linha.
            atual = atual[..achado.Index]
                    + new string(' ', achado.Length).Insert(0, "")
                    + atual[(abre + 1)..fecha]
                    + " "
                    + atual[(fecha + 1)..];
        }

        return atual;
    }

    /// <summary>A chave que fecha esta, ou -1.</summary>
    static int ParDaChave(string texto, int abre)
    {
        int fundo = 0;

        for (int i = abre; i < texto.Length; i++)
        {
            if (texto[i] == '{') fundo++;
            else if (texto[i] == '}' && --fundo == 0) return i;
        }

        return -1;
    }

}
