using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Simulador;

/// <summary>
/// O sketch em C/C++ compilado de verdade.
///
/// O interpretador entende um pedaco do C++; um projeto um pouco maior (uma
/// classe com template, um std::vector, uma biblioteca que usa ponteiro de
/// funcao) parava nele. Aqui o projeto inteiro -- o .ino, as abas, as
/// bibliotecas instaladas -- passa pelo clang (o do Zig, em nativo/zig) e
/// vira uma DLL que roda nativa no PC, com o core do Arduino de
/// nativo/arduino por baixo. O que o core faz no hardware vai pela ponte ate
/// o simulador, e la cai no MESMO codigo que o interpretador usa.
///
/// Como a IDE do Arduino: as abas .ino viram um arquivo so, com o
/// #include &lt;Arduino.h&gt; no comeco e o prototipo de cada funcao antes da
/// primeira (o #line devolve o erro na linha certa do editor); cada .cpp e
/// cada biblioteca compilam sozinhos, e tudo se junta no fim.
///
/// Compilar de novo o que nao mudou e instantaneo: o core de cada placa e o
/// sketch ficam guardados pela impressao digital do que entrou neles.
/// </summary>
public static class CompiladorNativo
{
    /// <summary>A pasta "nativo" (com zig/ e arduino/). Quem roda pode definir.</summary>
    public static string Pasta { get; set; }

    /// <summary>Onde ficam o core compilado de cada placa e os sketches (por impressao digital).</summary>
    public static string PastaDoCache { get; set; }

    const string Padrao = "-std=gnu++17";
    const string PadraoC = "-std=gnu11";

    // As do GCC da IDE que o clang trata como erro e o -fpermissive do GCC deixa
    // passar: o "char *p = \"texto\"", o estreitamento numa chave, o "register".
    static readonly string[] Permissivos =
    [
        "-Wno-c++11-narrowing", "-Wno-register", "-Wno-writable-strings", "-Wno-deprecated-register",
        "-Wno-reserved-user-defined-literal", "-Wno-invalid-source-encoding",
    ];

    static readonly string[] PermissivosC =
    [
        "-Wno-error=implicit-function-declaration", "-Wno-error=int-conversion",
        "-Wno-error=incompatible-pointer-types", "-Wno-error=implicit-int",
    ];

    /// <summary>
    /// O zig.exe, ou null se o compilador nao esta instalado: o do pacote do
    /// instalador (nativo\zig), ou o que o ferramentas\baixar-zig.ps1 pos em
    /// %LOCALAPPDATA%\CircuitSketch\zig (fora da pasta do projeto: sao ~400 MB).
    /// </summary>
    public static string Zig
    {
        get
        {
            if (AcharPasta() is { } pasta && File.Exists(Path.Combine(pasta, "zig", "zig.exe")))
                return Path.Combine(pasta, "zig", "zig.exe");
            string local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                        "CircuitSketch", "zig", "zig.exe");
            return File.Exists(local) ? local : null;
        }
    }

    /// <summary>O compilador esta aqui? (sem ele, o C++ continua no interpretador)</summary>
    public static bool Disponivel => Zig is not null && AcharPasta() is { } p
                                     && File.Exists(Path.Combine(p, "arduino", "include", "Arduino.h"));

    static string AcharPasta()
    {
        if (Pasta is not null) return Pasta;
        string dir = AppContext.BaseDirectory;
        for (int i = 0; i < 7 && dir is not null; i++)
        {
            string candidata = Path.Combine(dir, "nativo");
            if (File.Exists(Path.Combine(candidata, "arduino", "include", "Arduino.h"))) return Pasta = candidata;
            dir = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar));
        }
        return null;
    }

    static string Cache()
    {
        string raiz = PastaDoCache ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                                   "CircuitSketch", "compilacao");
        Directory.CreateDirectory(raiz);
        return raiz;
    }

    // =====================================================================
    //  o que da para compilar
    // =====================================================================

    /// <summary>
    /// Os registradores do AVR e os cabecalhos do chip: o interpretador os
    /// imita bit a bit (o Timer1, o PORTB); o core nativo ainda nao.
    /// </summary>
    static readonly Regex RegistrosDoAvr = new(
        @"\b(PORT[A-L]|DDR[A-L]|PIN[A-L]|TCCR\d[A-C]?|OCR\d[A-C]?[HL]?|TIMSK\d|TIFR\d|TCNT\d[HL]?|ICR\d[HL]?|ADCSRA|ADCSRB|ADMUX|ADC[HL]|"
        + @"EICR[AB]|EIMSK|EIFR|PCICR|PCIFR|PCMSK\d|SREG|UCSR\d[A-C]|UDR\d|UBRR\d[HL]?|SPCR|SPSR|SPDR|TWBR|TWCR|TWDR|TWSR|"
        + @"WDTCSR|MCUSR|MCUCR|SMCR|PRR|ISR\s*\()\b", RegexOptions.Compiled);

    static readonly Regex Inclusao = new(@"^\s*#\s*include\s*[<""]([^>""]+)[>""]", RegexOptions.Compiled | RegexOptions.Multiline);

    /// <summary>Os cabecalhos que o core nativo tem (Arduino.h, Wire.h, SPI.h...).</summary>
    static HashSet<string> DoCore()
    {
        var conjunto = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string inc = Path.Combine(AcharPasta() ?? "", "arduino", "include");
        if (!Directory.Exists(inc)) return conjunto;
        foreach (string f in Directory.EnumerateFiles(inc, "*", SearchOption.AllDirectories))
            conjunto.Add(Path.GetRelativePath(inc, f).Replace('\\', '/'));
        return conjunto;
    }

    static readonly HashSet<string> DaLinguagem = new(StringComparer.Ordinal)
    {
        "stdio.h", "stdlib.h", "string.h", "strings.h", "math.h", "stdint.h", "stdbool.h", "ctype.h", "limits.h", "float.h",
        "stddef.h", "stdarg.h", "assert.h", "time.h", "errno.h", "inttypes.h", "setjmp.h", "locale.h", "wchar.h",
        "wctype.h", "iso646.h", "fenv.h", "complex.h", "tgmath.h", "stdalign.h", "stdnoreturn.h", "uchar.h",
    };

    /// <summary>
    /// Por que o projeto nao compila nativo (null: compila). O que o core
    /// nativo ainda nao tem fica com o interpretador, que o imita.
    /// </summary>
    public static string PorQueNao(Projeto projeto)
    {
        if (!Disponivel) return "o compilador de C++ (nativo/zig) nao esta instalado";
        var core = DoCore();
        var abas = projeto.Arquivos ?? new Dictionary<string, string>();
        var textos = new List<string> { projeto.Sketch ?? "" };
        textos.AddRange(abas.Where(a => EhFonte(a.Key)).Select(a => a.Value ?? ""));

        foreach (string texto in textos)
        {
            string semComentario = SemComentarios(texto);
            if (RegistrosDoAvr.Match(semComentario) is { Success: true } r)
                return $"usa o registrador {r.Groups[1].Value.TrimEnd('(', ' ')} do AVR";
            foreach (Match m in Inclusao.Matches(semComentario))
            {
                string nome = m.Groups[1].Value.Trim().Replace('\\', '/');
                if (abas.Keys.Any(k => k.Replace('\\', '/').Equals(nome, StringComparison.OrdinalIgnoreCase))) continue;
                if (core.Contains(nome)) continue;
                if (DaLinguagem.Contains(nome) || !nome.Contains('.')) continue;     // <vector>, <cstdio>
                if (projeto.DoSimulador?.Invoke(nome) == true) return $"usa {nome}, que o simulador imita";
                if (DeBiblioteca(projeto, nome) is not null) continue;
                return $"usa {nome}";
            }
        }
        return null;
    }

    static bool EhFonte(string nome)
        => Path.GetExtension(nome).ToLowerInvariant() is ".ino" or ".cpp" or ".cc" or ".cxx" or ".c" or ".h" or ".hpp";

    static BibliotecaDeFora DeBiblioteca(Projeto projeto, string cabecalho)
    {
        foreach (var lib in projeto.Bibliotecas ?? [])
        {
            string raiz = (lib.Raiz ?? "").Trim('/');
            string procurado = (raiz.Length > 0 ? raiz + "/" : "") + cabecalho;
            if (lib.Arquivos.Any(a => a.Equals(procurado, StringComparison.OrdinalIgnoreCase))) return lib;
        }
        return null;
    }

    static string SemComentarios(string texto)
        => Regex.Replace(texto ?? "", @"//[^\n]*|/\*.*?\*/", m => new string('\n', m.Value.Count(c => c == '\n')),
                         RegexOptions.Singleline);

    // =====================================================================
    //  aquecer
    // =====================================================================

    static Task aquecimento;
    static readonly object travaDaCompilacao = new();

    /// <summary>
    /// Compila um sketch vazio para as placas mais usadas, em segundo plano:
    /// a primeira compilacao monta o cache do compilador (a biblioteca padrao
    /// do C++, o core de cada placa) e leva minutos; depois, segundos.
    /// </summary>
    public static void AquecerEmSegundoPlano(Action<string> avisar = null)
    {
        if (!Disponivel || aquecimento is not null) return;
        aquecimento = Task.Run(() =>
        {
            foreach (string placa in new[] { "esp32-devkit-v1", "arduino-uno" })
            {
                try
                {
                    Compilar(new Projeto { Sketch = "void setup() {}\nvoid loop() {}\n", Placa = placa },
                             primeiraVez => avisar?.Invoke("preparando o compilador de C++ (so na primeira vez; pode levar alguns minutos)…"));
                }
                catch { }
            }
        });
    }

    // =====================================================================
    //  compilar
    // =====================================================================

    /// <summary>
    /// Compila o projeto e devolve o caminho da DLL. Erro de compilacao vira
    /// ErroDeSintaxe na linha do editor (as outras abas aparecem pelo nome).
    /// </summary>
    public static SketchCompilado Compilar(Projeto projeto, Action<string> avisar = null)
    {
        // Uma compilacao por vez: o aquecimento e o Rodar montariam o mesmo
        // core ao mesmo tempo, na mesma pasta.
        lock (travaDaCompilacao) return CompilarSemTrava(projeto, avisar);
    }

    static SketchCompilado CompilarSemTrava(Projeto projeto, Action<string> avisar)
    {
        string pasta = AcharPasta() ?? throw new ErroDeExecucao("o compilador de C++ (a pasta \"nativo\") nao foi encontrado.", 0);
        string zig = Zig ?? throw new ErroDeExecucao("o compilador de C++ (nativo/zig/zig.exe) nao foi encontrado.", 0);
        string placa = projeto.Placa ?? "esp32-devkit-v1";

        // ---- o core da placa ----
        string cabecalho = CabecalhoDaPlaca(placa);
        string inc = Path.Combine(pasta, "arduino", "include");
        string src = Path.Combine(pasta, "arduino", "src");
        var fontesDoCore = Directory.GetFiles(src, "*.cpp").OrderBy(f => f, StringComparer.Ordinal).ToList();
        string impressaoDoCore = Impressao(
            [VersaoDoZig(zig), Padrao, string.Join(" ", Permissivos), cabecalho,
             .. Directory.GetFiles(inc, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal).Select(f => f + File.ReadAllText(f)),
             .. Directory.GetFiles(src, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal).Select(f => f + File.ReadAllText(f))]);
        string pastaDoCore = Path.Combine(Cache(), "nucleo-" + impressaoDoCore[..16]);
        string pronto = Path.Combine(pastaDoCore, "pronto");
        if (!File.Exists(pronto))
        {
            avisar?.Invoke("compilando o core do Arduino desta placa (so na primeira vez)…");
            Directory.CreateDirectory(pastaDoCore);
            File.WriteAllText(Path.Combine(pastaDoCore, "cs_placa.h"), cabecalho);
            var erros = CompilarVarios(zig, fontesDoCore.Select(f => (f, Path.Combine(pastaDoCore, Path.GetFileNameWithoutExtension(f) + ".o"), true)),
                                       [pastaDoCore, inc, src]);
            if (erros.Count > 0) throw new ErroDeExecucao("o core nativo do Arduino nao compilou: " + string.Join("\n", erros.Take(8)), 0);
            File.WriteAllText(pronto, "");
        }
        var objetosDoCore = fontesDoCore.Select(f => Path.Combine(pastaDoCore, Path.GetFileNameWithoutExtension(f) + ".o")).ToList();

        // ---- o projeto ----
        var abas = new Dictionary<string, string>(projeto.Arquivos ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
        var unidades = new List<(string Nome, string Texto)>();
        if (EhPrograma(projeto.Sketch))
        {
            // O programa com main() (o C, o SDK da Pico) ou app_main() (o
            // ESP-IDF): o texto como esta, em C se nao tiver nada de C++, e a
            // entrada que o chama no lugar do setup().
            string sketch = projeto.Sketch ?? "";
            unidades.Add((TemCpp(sketch) ? "sketch.cpp" : "sketch.c", "#include \"cs_placa.h\"\n#line 1 \"sketch.ino\"\n" + sketch + "\n"));
            unidades.Add(("cs_principal.c", EntradaDoPrograma));
        }
        else unidades.Add(("sketch.ino.cpp", MontarIno(projeto.Sketch ?? "", abas)));
        foreach (var (nome, texto) in abas.Where(a => Path.GetExtension(a.Key).ToLowerInvariant() is ".cpp" or ".cc" or ".cxx" or ".c")
                                          .OrderBy(a => a.Key, StringComparer.Ordinal))
            unidades.Add((nome, texto ?? ""));

        // As bibliotecas instaladas que o projeto (e elas mesmas) incluem.
        var usadas = BibliotecasUsadas(projeto, [projeto.Sketch ?? "", .. abas.Values.Select(v => v ?? "")]);
        var fontesDasLibs = new List<string>();
        var incDasLibs = new List<string>();
        foreach (var lib in usadas)
        {
            incDasLibs.Add(Path.Combine(lib.Pasta, lib.Raiz ?? ""));
            foreach (string arquivo in lib.Arquivos)
            {
                string ext = Path.GetExtension(arquivo).ToLowerInvariant();
                if (ext is not (".c" or ".cpp" or ".cc" or ".cxx")) continue;
                string baixo = arquivo.ToLowerInvariant();
                if (baixo.StartsWith("examples/") || baixo.Contains("/examples/") || baixo.StartsWith("test")
                    || baixo.Contains("/test/") || baixo.Contains("/tests/") || baixo.StartsWith("extras/")) continue;
                fontesDasLibs.Add(Path.Combine(lib.Pasta, arquivo));
            }
        }

        string impressao = Impressao(
            [impressaoDoCore, .. unidades.Select(u => u.Nome + "\0" + u.Texto),
             .. abas.Select(a => a.Key + "\0" + a.Value),
             .. fontesDasLibs.Select(f => f + File.GetLastWriteTimeUtc(f).Ticks),
             .. usadas.SelectMany(l => l.Arquivos.Select(a => Path.Combine(l.Pasta, a))).Where(File.Exists)
                      .Select(f => f + File.GetLastWriteTimeUtc(f).Ticks)]);
        string pastaDoSketch = Path.Combine(Cache(), "sketch-" + impressao[..16]);
        string dll = Path.Combine(pastaDoSketch, "sketch.dll");
        if (File.Exists(dll)) return new SketchCompilado(dll, placa);

        string trabalho = pastaDoSketch + ".tmp-" + Guid.NewGuid().ToString("N")[..8];
        Directory.CreateDirectory(trabalho);
        try
        {
            File.WriteAllText(Path.Combine(trabalho, "cs_placa.h"), cabecalho);
            foreach (var (nome, texto) in abas)
            {
                string destino = Path.Combine(trabalho, nome.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(destino)!);
                File.WriteAllText(destino, texto ?? "", new UTF8Encoding(false));
            }
            foreach (var (nome, texto) in unidades.Where(u => !abas.ContainsKey(u.Nome)))
                File.WriteAllText(Path.Combine(trabalho, nome), texto, new UTF8Encoding(false));

            var tarefas = new List<(string Fonte, string Objeto, bool Cpp)>();
            int k = 0;
            foreach (var (nome, _) in unidades)
                tarefas.Add((Path.Combine(trabalho, nome.Replace('/', Path.DirectorySeparatorChar)),
                             Path.Combine(trabalho, $"u{k++}.o"), !nome.EndsWith(".c", StringComparison.OrdinalIgnoreCase)));
            foreach (string f in fontesDasLibs)
                tarefas.Add((f, Path.Combine(trabalho, $"b{k++}.o"), !f.EndsWith(".c", StringComparison.OrdinalIgnoreCase)));

            var erros = CompilarVarios(zig, tarefas, [trabalho, .. incDasLibs, inc]);
            if (erros.Count > 0) throw ErroDoCompilador(erros, abas);

            // A DLL: o ponto de entrada e o da ponte, que adia os construtores
            // das globais ate o simulador estar ligado.
            var ligar = new List<string> { "c++", "-shared", "-target", "x86_64-windows-gnu", "-o", Path.Combine(trabalho, "sketch.dll"),
                                           "-Wl,--entry=cs_entrada_da_dll" };
            ligar.AddRange(tarefas.Select(t => t.Objeto));
            ligar.AddRange(objetosDoCore);
            var (codigo, saida) = Rodar(zig, ligar, trabalho);
            if (codigo != 0) throw ErroDoLigador(saida);

            if (Directory.Exists(pastaDoSketch)) Directory.Delete(pastaDoSketch, true);
            Directory.Move(trabalho, pastaDoSketch);
        }
        catch
        {
            try { Directory.Delete(trabalho, true); } catch { }
            throw;
        }
        return new SketchCompilado(dll, placa);
    }

    /// <summary>Compila cada fonte num .o, em paralelo; devolve as mensagens de erro.</summary>
    static List<string> CompilarVarios(string zig, IEnumerable<(string Fonte, string Objeto, bool Cpp)> tarefas, IEnumerable<string> inclusoes)
    {
        var lista = tarefas.ToList();
        var incs = inclusoes.Where(Directory.Exists).Distinct().ToList();
        var mensagens = new string[lista.Count];
        var codigos = new int[lista.Count];
        Parallel.For(0, lista.Count, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(2, Environment.ProcessorCount) }, i =>
        {
            var (fonte, objeto, cpp) = lista[i];
            var args = new List<string> { cpp ? "c++" : "cc", "-c", "-target", "x86_64-windows-gnu", "-O1", "-w",
                                          "-ferror-limit=8", "-fno-caret-diagnostics", "-DCS_NATIVO=1",
                                          // O ponteiro nulo acessa de verdade (e cai como na placa), em vez de o
                                          // otimizador apagar o caminho e por uma armadilha no lugar.
                                          "-fno-delete-null-pointer-checks", cpp ? Padrao : PadraoC };
            if (cpp) args.Add("-x");
            if (cpp) args.Add("c++");
            args.AddRange(cpp ? Permissivos : PermissivosC);
            foreach (string inc in incs) { args.Add("-I"); args.Add(inc); }
            args.Add(fonte);
            args.Add("-o");
            args.Add(objeto);
            (codigos[i], mensagens[i]) = Rodar(zig, args, Path.GetDirectoryName(objeto));
        });
        var erros = new List<string>();
        for (int i = 0; i < lista.Count; i++)
            if (codigos[i] != 0)
                erros.AddRange(mensagens[i].Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0));
        return erros;
    }

    static (int Codigo, string Saida) Rodar(string zig, IEnumerable<string> args, string pasta)
    {
        var psi = new ProcessStartInfo(zig)
        {
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
            WorkingDirectory = pasta,
        };
        foreach (string a in args) psi.ArgumentList.Add(a);
        psi.Environment["ZIG_GLOBAL_CACHE_DIR"] = Path.Combine(Cache(), "zig");
        psi.Environment["ZIG_LOCAL_CACHE_DIR"] = Path.Combine(Cache(), "zig-local");
        using var p = Process.Start(psi)!;
        var erro = p.StandardError.ReadToEndAsync();
        string saida = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        return (p.ExitCode, saida + erro.Result);
    }

    static string versaoDoZig;

    static string VersaoDoZig(string zig)
        => versaoDoZig ??= Rodar(zig, ["version"], Path.GetDirectoryName(zig)).Saida.Trim();

    static string Impressao(IEnumerable<string> partes)
    {
        using var h = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (string p in partes)
        {
            h.AppendData(Encoding.UTF8.GetBytes(p ?? ""));
            h.AppendData([0]);
        }
        return Convert.ToHexString(h.GetHashAndReset()).ToLowerInvariant();
    }

    /// <summary>As bibliotecas instaladas incluidas pelo projeto, e as que elas incluem.</summary>
    static List<BibliotecaDeFora> BibliotecasUsadas(Projeto projeto, IEnumerable<string> textos)
    {
        var usadas = new List<BibliotecaDeFora>();
        var fila = new Queue<string>(textos);
        var vistos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (fila.Count > 0)
        {
            foreach (Match m in Inclusao.Matches(SemComentarios(fila.Dequeue())))
            {
                string nome = m.Groups[1].Value.Trim().Replace('\\', '/');
                if (!vistos.Add(nome)) continue;
                var lib = DeBiblioteca(projeto, nome);
                if (lib is null || usadas.Contains(lib)) continue;
                usadas.Add(lib);
                foreach (string arquivo in lib.Arquivos)
                    if (Path.GetExtension(arquivo).ToLowerInvariant() is ".h" or ".hpp" or ".c" or ".cpp")
                        fila.Enqueue(lib.Ler(arquivo) ?? "");
            }
        }
        return usadas;
    }

    // =====================================================================
    //  o .ino
    // =====================================================================

    /// <summary>
    /// As abas .ino num arquivo so (o sketch primeiro, as outras em ordem
    /// alfabetica), com o Arduino.h no comeco e os prototipos antes da
    /// primeira funcao. Cada pedaco comeca com #line: o erro aparece na
    /// linha (e na aba) de onde veio.
    /// </summary>
    static readonly Regex DefinicaoDeSetupOuLoop = new(@"\bvoid\s+(setup|loop)\s*\(\s*(void)?\s*\)\s*\{", RegexOptions.Compiled);
    static readonly Regex DefinicaoDeMain = new(@"\b(int|void)\s+(main|app_main)\s*\([^)]*\)\s*\{", RegexOptions.Compiled);
    static readonly Regex MarcasDeCpp = new(
        @"\b(class|namespace|template|nullptr|constexpr|operator|virtual)\b|std::|extern\s+""C""|\bnew\s+\w|#\s*include\s*<(iostream|vector|string|map|memory|functional|algorithm|array|cstdio|cstdint)>",
        RegexOptions.Compiled);

    /// <summary>O programa comeca no main() ou no app_main(), e nao no setup()/loop().</summary>
    public static bool EhPrograma(string sketch)
    {
        string texto = SemComentarios(sketch);
        return !DefinicaoDeSetupOuLoop.IsMatch(texto) && DefinicaoDeMain.IsMatch(texto);
    }

    static bool TemCpp(string sketch) => MarcasDeCpp.IsMatch(SemComentarios(sketch));

    /// <summary>
    /// A entrada do programa com main(): o setup() o chama (o app_main do
    /// ESP-IDF, o do C++ sem extern "C", ou o main), com o monitor ja ligado
    /// -- e o printf da placa. Quando ele volta, a tarefa principal acaba (as
    /// outras do FreeRTOS seguem, como no ESP-IDF) e a placa fica parada.
    /// </summary>
    const string EntradaDoPrograma = """
        #include "Arduino.h"
        #include "freertos/FreeRTOS.h"
        extern int main(void) __attribute__((weak));
        extern void app_main(void) __attribute__((weak));
        extern void cs_app_main_do_cpp(void) __asm__("_Z8app_mainv") __attribute__((weak));
        extern volatile int cs_rtos_ativo;

        void setup(void)
        {
            cs_val baud = cs_i(115200);
            cs_nada("Serial", "begin", 1, &baud);
            if (app_main) app_main();
            else if (cs_app_main_do_cpp) cs_app_main_do_cpp();
            else if (main) main();
            if (cs_rtos_ativo) vTaskDelete(NULL);
        }

        void loop(void) { delay(1000); }
        """;

    public static string MontarIno(string sketch, IDictionary<string, string> abas)
    {
        var partes = new List<(string Nome, string Texto)> { ("sketch.ino", sketch ?? "") };
        partes.AddRange(abas.Where(a => a.Key.EndsWith(".ino", StringComparison.OrdinalIgnoreCase))
                            .OrderBy(a => a.Key, StringComparer.Ordinal).Select(a => (a.Key, a.Value ?? "")));

        var prototipos = new List<string>();
        foreach (var (_, texto) in partes) prototipos.AddRange(Prototipos(texto));

        var sb = new StringBuilder();
        sb.Append("#include <Arduino.h>\n");
        bool inseridos = false;
        foreach (var (nome, texto) in partes)
        {
            int onde = inseridos ? -1 : InicioDaPrimeiraFuncao(texto);
            if (onde < 0)
            {
                sb.Append($"#line 1 \"{nome}\"\n").Append(texto).Append('\n');
                continue;
            }
            string antes = texto[..onde];
            int linhasAntes = antes.Count(c => c == '\n');
            sb.Append($"#line 1 \"{nome}\"\n").Append(antes);
            if (!antes.EndsWith('\n')) sb.Append('\n');
            foreach (string p in prototipos.Distinct()) sb.Append(p).Append('\n');
            sb.Append($"#line {linhasAntes + 1} \"{nome}\"\n");
            sb.Append(texto[onde..]).Append('\n');
            inseridos = true;
        }
        return sb.ToString();
    }

    /// <summary>Onde comeca a linha da primeira definicao de funcao (fora de chaves), ou -1.</summary>
    static int InicioDaPrimeiraFuncao(string texto)
    {
        foreach (var f in Funcoes(texto)) return f.InicioDaLinha;
        return -1;
    }

    /// <summary>Os prototipos das funcoes definidas no topo do arquivo.</summary>
    static IEnumerable<string> Prototipos(string texto)
    {
        foreach (var f in Funcoes(texto))
        {
            if (f.Assinatura.Contains('=')) continue;                         // argumento padrao: o prototipo repetiria
            if (Regex.IsMatch(f.Assinatura, @"^\s*(template|static\s+inline)\b")) continue;
            yield return f.Assinatura.Trim() + ";";
        }
    }

    sealed record Definicao(int InicioDaLinha, string Assinatura);

    /// <summary>
    /// As definicoes de funcao no nivel de fora (profundidade 0 de chaves),
    /// sem contar comentarios, textos e linhas do pre-processador.
    /// </summary>
    static IEnumerable<Definicao> Funcoes(string texto)
    {
        string limpo = Limpar(texto);
        int profundidade = 0;
        int inicioDoComando = 0;
        for (int i = 0; i < limpo.Length; i++)
        {
            char c = limpo[i];
            if (c == '{')
            {
                if (profundidade == 0)
                {
                    string cabeca = limpo[inicioDoComando..i];
                    var m = Regex.Match(cabeca, @"^\s*((?:[A-Za-z_][\w:<>,\*&\s]*?[\s\*&]+)?)([A-Za-z_]\w*)\s*\(([^()]*)\)\s*(const\s*)?$",
                                        RegexOptions.Singleline);
                    string nome = m.Success ? m.Groups[2].Value : "";
                    string tipo = m.Success ? m.Groups[1].Value.Trim() : "";
                    if (m.Success && tipo.Length > 0 && !tipo.TrimEnd().EndsWith("::")
                        && !Regex.IsMatch(cabeca, @"::\s*" + Regex.Escape(nome) + @"\s*\(")
                        && nome is not ("if" or "while" or "for" or "switch" or "catch")
                        && !Regex.IsMatch(tipo, @"\b(class|struct|union|enum|namespace|typedef|return|else|do|operator)\b")
                        && !tipo.EndsWith("=") && m.Groups[4].Value.Length == 0)
                    {
                        int linha = texto.LastIndexOf('\n', Math.Max(0, inicioDoComando + (cabeca.Length - cabeca.TrimStart().Length)));
                        yield return new Definicao(linha + 1, $"{tipo} {nome}({m.Groups[3].Value})");
                    }
                }
                profundidade++;
            }
            else if (c == '}')
            {
                profundidade = Math.Max(0, profundidade - 1);
                if (profundidade == 0) inicioDoComando = i + 1;
            }
            else if (c == ';' && profundidade == 0) inicioDoComando = i + 1;
        }
    }

    /// <summary>O texto com comentarios, textos, caracteres e diretivas trocados por espacos (o tamanho fica).</summary>
    static string Limpar(string texto)
    {
        var sb = new StringBuilder(texto);
        int i = 0;
        bool comecoDeLinha = true;
        while (i < sb.Length)
        {
            char c = sb[i];
            if (comecoDeLinha && c == '#')
            {
                // A diretiva inteira, com as continuacoes por barra invertida.
                while (i < sb.Length && sb[i] != '\n')
                {
                    if (sb[i] == '\\' && i + 1 < sb.Length && sb[i + 1] == '\n') { sb[i] = ' '; i += 2; continue; }
                    sb[i++] = ' ';
                }
                continue;
            }
            if (c == '/' && i + 1 < sb.Length && sb[i + 1] == '/')
            {
                while (i < sb.Length && sb[i] != '\n') sb[i++] = ' ';
                continue;
            }
            if (c == '/' && i + 1 < sb.Length && sb[i + 1] == '*')
            {
                sb[i++] = ' '; sb[i++] = ' ';
                while (i < sb.Length && !(sb[i] == '*' && i + 1 < sb.Length && sb[i + 1] == '/'))
                {
                    if (sb[i] != '\n') sb[i] = ' ';
                    i++;
                }
                if (i < sb.Length) { sb[i++] = ' '; if (i < sb.Length) sb[i++] = ' '; }
                continue;
            }
            if (c is '"' or '\'')
            {
                char fim = c;
                i++;
                while (i < sb.Length && sb[i] != fim && sb[i] != '\n')
                {
                    if (sb[i] == '\\' && i + 1 < sb.Length) sb[i++] = ' ';
                    sb[i++] = ' ';
                }
                i++;
                continue;
            }
            if (c == '\n') comecoDeLinha = true;
            else if (!char.IsWhiteSpace(c)) comecoDeLinha = false;
            i++;
        }
        return sb.ToString();
    }

    // =====================================================================
    //  a placa
    // =====================================================================

    /// <summary>
    /// O cs_placa.h: as macros do core da placa (as mesmas do pre-processador
    /// do interpretador), as constantes do Arduino com os numeros que o
    /// simulador usa, e os nomes dos pinos (A0, LED_BUILTIN, D5, SDA...).
    /// </summary>
    public static string CabecalhoDaPlaca(string placa)
    {
        var sb = new StringBuilder();
        sb.Append("/* Gerado pelo simulador para a placa ").Append(placa).Append(". */\n#pragma once\n");
        // O __cplusplus e o do compilador (num .c ele nao existe), e a memoria RTC
        // e a do cs_esp_idf.h (uma secao que o simulador guarda no sono).
        foreach (var (nome, valor) in Preprocessador.MacrosDoCore(placa).Where(m => m.Nome != "__cplusplus" && !m.Nome.StartsWith("RTC_")))
            sb.Append("#ifndef ").Append(nome).Append("\n#define ").Append(nome).Append(' ').Append(valor).Append("\n#endif\n");
        if (!Preprocessador.MacrosDoCore(placa).Any(m => m.Nome == "F_CPU"))
            sb.Append($"#define F_CPU {(long)((PerfilPlaca.De(placa)?.Megahertz ?? 16) * 1_000_000)}L\n");

        foreach (string nome in new[] { "INPUT", "OUTPUT", "INPUT_PULLUP", "INPUT_PULLDOWN", "RISING", "FALLING", "CHANGE",
                                        "DEFAULT", "EXTERNAL", "INTERNAL", "INTERNAL1V1", "INTERNAL2V56", "OUTPUT_OPEN_DRAIN",
                                        "ONLOW", "ONHIGH", "ONLOW_WE", "ONHIGH_WE", "OPEN_DRAIN", "PULLUP", "PULLDOWN" })
            if (Interpretador.ConstanteDoCore(nome) is { } v)
                sb.Append($"#define {nome} {v}\n");

        var perfil = PerfilPlaca.De(placa);
        if (perfil is not null)
        {
            sb.Append($"#define LED_BUILTIN {perfil.LedInterno}\n");
            sb.Append($"#define NUM_DIGITAL_PINS {perfil.NumPinos}\n");
            if (PerfilPlaca.TemLedRgb(perfil.Id))
                sb.Append($"#define RGB_BUILTIN {perfil.LedInterno}\n#define RGB_BRIGHTNESS 64\n");
            sb.Append("#ifdef __cplusplus\n");
            var nomes = new SortedDictionary<string, int>(StringComparer.Ordinal);
            foreach (var (nome, gpio) in perfil.NomesDoCore) nomes[nome] = gpio;
            var candidatos = Enumerable.Range(0, 20).Select(i => $"A{i}")
                .Concat(Enumerable.Range(0, 60).Select(i => $"D{i}"))
                .Concat("ABCDEFGH".SelectMany(p => Enumerable.Range(0, 16).Select(i => $"P{p}{i}")));
            foreach (string nome in candidatos)
                if (!nomes.ContainsKey(nome) && perfil.PinoDoCore(nome) is >= 0 and var g) nomes[nome] = g;
            foreach (var (nome, gpio) in nomes)
                if (Regex.IsMatch(nome, @"^[A-Za-z_]\w*$") && nome is not ("LED_BUILTIN" or "RGB_BUILTIN"))
                    sb.Append($"static const uint8_t {nome} = {gpio};\n");
            sb.Append("#endif\n");
        }
        return sb.ToString();
    }

    // =====================================================================
    //  as mensagens
    // =====================================================================

    static readonly Regex LinhaDeErro = new(@"^(?<arq>.*?):(?<lin>\d+):(?<col>\d+):\s*(?<tipo>fatal error|error|warning|note):\s*(?<msg>.*)$",
                                            RegexOptions.Compiled);

    /// <summary>O primeiro erro do clang como ErroDeSintaxe, na linha da aba de onde veio.</summary>
    static ErroDeSintaxe ErroDoCompilador(List<string> linhas, IDictionary<string, string> abas)
    {
        var erros = new List<(string Arquivo, int Linha, string Texto)>();
        foreach (string l in linhas)
        {
            var m = LinhaDeErro.Match(l);
            if (!m.Success || m.Groups["tipo"].Value is "warning" or "note") continue;
            string arq = Path.GetFileName(m.Groups["arq"].Value.Replace('/', '\\'));
            erros.Add((arq, int.Parse(m.Groups["lin"].Value), m.Groups["msg"].Value));
        }
        if (erros.Count == 0)
            return new ErroDeSintaxe("o compilador de C++ recusou o programa: " + string.Join(" ", linhas.Take(6)), 0);

        var (arquivo, linha, texto) = erros[0];
        string mais = erros.Count > 1 ? $" (e mais {erros.Count - 1} erro{(erros.Count > 2 ? "s" : "")})" : "";
        if (arquivo.Equals("sketch.ino", StringComparison.OrdinalIgnoreCase))
            return new ErroDeSintaxe(texto + mais, linha);
        int codigo = Origens.Codificar(Origens.Registrar("nativo:" + arquivo, arquivo), linha);
        return new ErroDeSintaxe(texto + mais, codigo);
    }

    static ErroDeSintaxe ErroDoLigador(string saida)
    {
        var faltando = Regex.Matches(saida, @"undefined symbol:\s*(.+)").Select(m => m.Groups[1].Value.Trim()).Distinct().ToList();
        if (faltando.Count > 0)
        {
            string nomes = string.Join(", ", faltando.Take(6).Select(n => Regex.Replace(n, @"\(.*\)$", "()")));
            if (faltando.Any(f => f.StartsWith("setup") || f.StartsWith("loop")))
                return new ErroDeSintaxe($"o sketch precisa de setup() e de loop() (faltou: {nomes}).", 0);
            return new ErroDeSintaxe($"funcao declarada e nunca definida: {nomes}.", 0);
        }
        return new ErroDeSintaxe("o ligador recusou o programa: " + saida.Trim().Split('\n').FirstOrDefault(), 0);
    }
}

/// <summary>A DLL do sketch, pronta para cada placa carregar a sua copia.</summary>
public sealed record SketchCompilado(string Dll, string Placa) : ProgramaCompilado;
