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
        // O ponteiro aqui tem 64 bits e o da placa 32: o (uint32_t)ponteiro de quem imprime um endereco nao e erro.
        "-fms-extensions",
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

    /// <summary>
    /// O core nativo tem este cabecalho? (O que vem com o Arduino-ESP32 e o
    /// ESP-IDF -- "SD_MMC.h", "soc/gpio_reg.h" -- e nao e biblioteca que falta.)
    /// </summary>
    public static bool TemNoCore(string nome)
        => Disponivel && (DaLinguagem.Contains((nome ?? "").Trim()) || DoCore().Contains((nome ?? "").Trim().Replace('\\', '/')));

    /// <summary>Os cabecalhos que o core nativo tem (Arduino.h, Wire.h, SPI.h...).</summary>
    static HashSet<string> DoCore()
    {
        if (doCore is not null) return doCore;
        var conjunto = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string inc = Path.Combine(AcharPasta() ?? "", "arduino", "include");
        if (!Directory.Exists(inc)) return conjunto;
        foreach (string f in Directory.EnumerateFiles(inc, "*", SearchOption.AllDirectories))
            conjunto.Add(Path.GetRelativePath(inc, f).Replace('\\', '/'));
        // Os do ESP-IDF (nativo/idf/comum e nativo/idf/<chip>).
        string idf = Path.Combine(AcharPasta() ?? "", "idf");
        if (Directory.Exists(idf))
            foreach (string pastaDoIdf in Directory.EnumerateDirectories(idf))
                foreach (string f in Directory.EnumerateFiles(pastaDoIdf, "*", SearchOption.AllDirectories))
                    conjunto.Add(Path.GetRelativePath(pastaDoIdf, f).Replace('\\', '/'));
        // Os do mbedTLS ("mbedtls/md.h", "psa/crypto.h"), os do TinyUSB ("tusb.h", "class/hid/hid.h") e os das pilhas sob demanda.
        foreach (string outra in new[] { "mbedtls", "tinyusb" }.Concat(PilhasSobDemanda.Select(p => p.Pasta)))
        {
            string pastaDela = Path.Combine(AcharPasta() ?? "", outra, "include");
            if (Directory.Exists(pastaDela))
                foreach (string f in Directory.EnumerateFiles(pastaDela, "*", SearchOption.AllDirectories))
                    conjunto.Add(Path.GetRelativePath(pastaDela, f).Replace('\\', '/'));
        }
        return doCore = conjunto;
    }

    static HashSet<string> doCore;

    static readonly HashSet<string> DaLinguagem = new(StringComparer.Ordinal)
    {
        "stdio.h", "stdlib.h", "string.h", "strings.h", "math.h", "stdint.h", "stdbool.h", "ctype.h", "limits.h", "float.h",
        "stddef.h", "stdarg.h", "assert.h", "time.h", "errno.h", "inttypes.h", "setjmp.h", "locale.h", "wchar.h",
        "wctype.h", "iso646.h", "fenv.h", "complex.h", "tgmath.h", "stdalign.h", "stdnoreturn.h", "uchar.h",
        // O POSIX que os programas do IDF incluem (o compilador traz ou o core tem).
        "unistd.h", "sys/types.h", "sys/time.h", "sys/stat.h",
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
        var encobertas = AbasEncobertas(projeto, core);
        var textos = new List<string> { projeto.Sketch ?? "" };
        textos.AddRange(abas.Where(a => EhFonte(a.Key) && !encobertas.Contains(a.Key)).Select(a => a.Value ?? ""));

        foreach (string texto in textos)
        {
            string semComentario = SemComentarios(texto);
            if (RegistrosDoAvr.Match(semComentario) is { Success: true } r)
                return $"usa o registrador {r.Groups[1].Value.TrimEnd('(', ' ')} do AVR";
            foreach (Match m in Inclusao.Matches(semComentario))
            {
                string nome = m.Groups[1].Value.Trim().Replace('\\', '/');
                if (core.Contains(nome)) continue;
                if (abas.Keys.Any(k => k.Replace('\\', '/').Equals(nome, StringComparison.OrdinalIgnoreCase))) continue;
                if (DaLinguagem.Contains(nome) || !nome.Contains('.')) continue;     // <vector>, <cstdio>
                if (projeto.DoSimulador?.Invoke(nome) == true) return $"usa {nome}, que o simulador imita";
                if (DeBiblioteca(projeto, nome) is not null) continue;
                return $"usa {nome}";
            }
        }
        return null;
    }

    /// <summary>
    /// O #include que nem a placa nem as bibliotecas instaladas tem, nas palavras de quem esta
    /// lendo: o "No such file or directory" da IDE, e o que fazer.
    /// </summary>
    public static ErroDeSintaxe ErroLegivel(ErroDeSintaxe erro)
    {
        var falta = Regex.Match(erro.Message, @"'([^']+)' file not found");
        return !falta.Success ? erro
            : new ErroDeSintaxe($"{falta.Groups[1].Value} nao esta instalada e nao vem com a placa (na IDE do Arduino: \"No such file or directory\"). "
                                + "Instale a biblioteca que traz esse arquivo, ou tire o #include.", erro.Linha);
    }

    static readonly Regex InclusaoComAspas = new(@"^\s*#\s*include\s*""([^""]+)""", RegexOptions.Compiled | RegexOptions.Multiline);

    /// <summary>
    /// As abas que tem o nome de um cabecalho do core e so sao incluidas com
    /// &lt;&gt;: como no Arduino, o &lt;FS.h&gt; e o do core, e a aba fica de fora
    /// (o projeto trouxe um FS.h de outra placa, e ele nunca e lido).
    /// </summary>
    static HashSet<string> AbasEncobertas(Projeto projeto, HashSet<string> core)
    {
        var abas = projeto.Arquivos ?? new Dictionary<string, string>();
        var comAspas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string texto in abas.Values.Prepend(projeto.Sketch ?? ""))
            foreach (Match m in InclusaoComAspas.Matches(SemComentarios(texto)))
                comAspas.Add(m.Groups[1].Value.Trim().Replace('\\', '/'));
        return new HashSet<string>(abas.Keys.Where(k => core.Contains(k.Replace('\\', '/')) && !comAspas.Contains(k.Replace('\\', '/'))),
                                   StringComparer.OrdinalIgnoreCase);
    }

    static bool EhFonte(string nome)
        => Path.GetExtension(nome).ToLowerInvariant() is ".ino" or ".cpp" or ".cc" or ".cxx" or ".c" or ".h" or ".hpp";

    /// <summary>
    /// A biblioteca que traz este cabecalho. Com mais de uma, a pontuacao da IDE (o
    /// ComputePriority do arduino-cli): a feita para esta arquitetura, depois a que tem o
    /// nome do cabecalho, e so depois a ordem alfabetica.
    /// </summary>
    static BibliotecaDeFora DeBiblioteca(Projeto projeto, string cabecalho)
    {
        string arquitetura = Preprocessador.MacrosDoCore(projeto.Placa ?? "esp32-devkit-v1").Select(m => m.Nome)
            .FirstOrDefault(n => n.StartsWith("ARDUINO_ARCH_", StringComparison.Ordinal))?["ARDUINO_ARCH_".Length..].ToLowerInvariant();
        string nome = Path.GetFileNameWithoutExtension(cabecalho);
        BibliotecaDeFora melhor = null;
        int maior = -1;
        foreach (var lib in projeto.Bibliotecas ?? [])
        {
            string raiz = (lib.Raiz ?? "").Trim('/');
            string procurado = (raiz.Length > 0 ? raiz + "/" : "") + cabecalho;
            if (!lib.Arquivos.Any(a => a.Equals(procurado, StringComparison.OrdinalIgnoreCase))) continue;
            int pontos = PontosDaArquitetura(lib, arquitetura)
                         + (lib.Nome.Equals(nome, StringComparison.OrdinalIgnoreCase) ? 700
                            : lib.Nome.StartsWith(nome, StringComparison.OrdinalIgnoreCase) ? 500
                            : lib.Nome.Contains(nome, StringComparison.OrdinalIgnoreCase) ? 400 : 0);
            if (pontos > maior)
            {
                maior = pontos;
                melhor = lib;
            }
        }
        return melhor;
    }

    static int PontosDaArquitetura(BibliotecaDeFora lib, string arquitetura)
    {
        string arquivo = Path.Combine(lib.Pasta ?? "", "library.properties");
        if (!File.Exists(arquivo)) return 1000;
        string linha = File.ReadLines(arquivo).FirstOrDefault(l => l.StartsWith("architectures=", StringComparison.OrdinalIgnoreCase));
        if (linha is null) return 1000;
        var lista = linha["architectures=".Length..].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (arquitetura is not null && lista.Contains(arquitetura, StringComparer.OrdinalIgnoreCase)) return 1010;
        return lista.Contains("*") ? 1000 : 0;
    }

    /// <summary>
    /// O projeto usa algo que so o interpretador tem? E so para escolher qual dos dois erros
    /// mostrar: o texto nao ve o #ifdef, e quem decide o que compila continua sendo o clang.
    /// </summary>
    public static bool SoNoInterpretador(Projeto projeto)
        => PorQueNao(projeto) is { } porque && (porque.Contains("registrador") || porque.Contains("imita"));

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
            // Com a janela abrindo e a pessoa montando o circuito, quem espera e o aquecimento.
            Thread.CurrentThread.Priority = ThreadPriority.Lowest;
            foreach (string placa in new[] { "esp32-devkit-v1", "arduino-uno" })
            {
                try
                {
                    Compilar(new Projeto { Sketch = "void setup() {}\nvoid loop() {}\n", Placa = placa },
                             primeiraVez => avisar?.Invoke("preparando o compilador de C++ (so na primeira vez; pode levar alguns minutos)…"));
                }
                catch { }
            }
            Thread.CurrentThread.Priority = ThreadPriority.Normal;
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
        lock (travaDaCompilacao)
        {
            try { return CompilarSemTrava(projeto, avisar, instaladasVencem: false); }
            catch (ErroDeSintaxe erro) when (TemInstaladaEncoberta(projeto))
            {
                // O que o simulador imita nao tem tudo (o RTC_PCF8523 da RTClib). Se a pessoa
                // instalou a biblioteca de verdade, ela e a proxima tentativa; se tambem nao
                // der, o erro que vale e o primeiro.
                try
                {
                    var comAsInstaladas = CompilarSemTrava(projeto, avisar, instaladasVencem: true);
                    avisar?.Invoke("o que o simulador imita nao tinha o que o sketch pede: usando as bibliotecas instaladas.");
                    return comAsInstaladas;
                }
                catch (ErroDeSintaxe comAsInstaladas)
                {
                    ErroComAsInstaladas = comAsInstaladas;
                    throw erro;
                }
            }
        }
    }

    /// <summary>O erro da segunda tentativa, com as bibliotecas instaladas: a varredura mostra os dois.</summary>
    internal static ErroDeSintaxe ErroComAsInstaladas;

    /// <summary>Tem biblioteca instalada que ficou de fora porque a placa ja tem o mesmo cabecalho?</summary>
    static bool TemInstaladaEncoberta(Projeto projeto)
    {
        string[] textos = [projeto.Sketch ?? "", .. (projeto.Arquivos ?? new Dictionary<string, string>()).Values.Select(v => v ?? "")];
        return BibliotecasUsadas(projeto, textos, instaladasVencem: true).Count > BibliotecasUsadas(projeto, textos, instaladasVencem: false).Count;
    }

    static SketchCompilado CompilarSemTrava(Projeto projeto, Action<string> avisar, bool instaladasVencem)
    {
        string pasta = AcharPasta() ?? throw new ErroDeExecucao("o compilador de C++ (a pasta \"nativo\") nao foi encontrado.", 0);
        string zig = Zig ?? throw new ErroDeExecucao("o compilador de C++ (nativo/zig/zig.exe) nao foi encontrado.", 0);
        string placa = projeto.Placa ?? "esp32-devkit-v1";

        // ---- o core da placa ----
        string cabecalho = CabecalhoDaPlaca(placa);
        string inc = Path.Combine(pasta, "arduino", "include");
        string src = Path.Combine(pasta, "arduino", "src");
        var idf = PastasDoIdf(pasta, cabecalho);
        // O core: o do simulador (src/*.cpp) e o codigo do core oficial que vem
        // como esta (src/arduino-esp32, com .c), em subpastas -- esse so nas
        // placas ESP32.
        string doCoreDaEsp32 = Path.Combine(src, "arduino-esp32") + Path.DirectorySeparatorChar;
        // O mbedTLS das placas ESP32 ve a configuracao do IDF (o MBEDTLS_CONFIG_FILE, como no IDF).
        string[] definicoes = idf.Count > 0 ? [ConfiguracaoDoMbedtls] : [];
        var fontesDoCore = Directory.EnumerateFiles(src, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".cpp", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".c", StringComparison.OrdinalIgnoreCase))
            .Where(f => idf.Count > 0 || !f.StartsWith(doCoreDaEsp32, StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f, StringComparer.Ordinal).ToList();
        string impressaoDoCore = Impressao(
            [VersaoDoZig(zig), Padrao, string.Join(" ", Permissivos), cabecalho, ImpressaoDoIdf(idf),
             .. Directory.GetFiles(inc, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal).Select(f => f + File.ReadAllText(f)),
             .. Directory.GetFiles(src, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal).Select(f => f + File.ReadAllText(f))]);
        string pastaDoCore = Path.Combine(Cache(), "nucleo-" + impressaoDoCore[..16]);
        string ObjetoDoCore(string f) => Path.Combine(pastaDoCore, Path.GetRelativePath(src, f).Replace('\\', '_').Replace('/', '_') + ".o");
        string pronto = Path.Combine(pastaDoCore, "pronto");
        if (!File.Exists(pronto))
        {
            avisar?.Invoke("compilando o core do Arduino desta placa (so na primeira vez)…");
            Directory.CreateDirectory(pastaDoCore);
            File.WriteAllText(Path.Combine(pastaDoCore, "cs_placa.h"), cabecalho);
            var erros = CompilarVarios(zig, fontesDoCore.Select(f => (f, ObjetoDoCore(f), !f.EndsWith(".c", StringComparison.OrdinalIgnoreCase))),
                                       [pastaDoCore, inc, src, .. idf], definicoes: definicoes);
            if (erros.Count > 0) throw new ErroDeExecucao("o core nativo do Arduino nao compilou: " + string.Join("\n", erros.Take(8)), 0);
            var (codigoDoAr, saidaDoAr) = Rodar(zig, ["ar", "rcs", "core.a", .. fontesDoCore.Where(f => !FicaForaDoCoreA(f) && !EhFraco(f)).Select(f => Path.GetFileName(ObjetoDoCore(f)))], pastaDoCore);
            if (codigoDoAr != 0) throw new ErroDeExecucao("o core nativo nao virou biblioteca: " + saidaDoAr, 0);
            if (fontesDoCore.Any(EhFraco))
            {
                var (codigoDosFracos, saidaDosFracos) = Rodar(zig, ["ar", "rcs", "fracos.a", .. fontesDoCore.Where(EhFraco).Select(f => Path.GetFileName(ObjetoDoCore(f)))], pastaDoCore);
                if (codigoDosFracos != 0) throw new ErroDeExecucao("o core nativo nao virou biblioteca: " + saidaDosFracos, 0);
            }
            File.WriteAllText(pronto, "");
        }
        var objetosDoCore = fontesDoCore.Where(FicaForaDoCoreA).Select(ObjetoDoCore).Append(Path.Combine(pastaDoCore, "core.a")).ToList();
        string fracos = fontesDoCore.Any(EhFraco) ? Path.Combine(pastaDoCore, "fracos.a") : null;
        string mbedtls = idf.Count > 0 ? BibliotecaMbedtls(pasta, zig, cabecalho, inc, idf, avisar) : null;

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
        var usadas = BibliotecasUsadas(projeto, [projeto.Sketch ?? "", .. abas.Values.Select(v => v ?? "")], instaladasVencem);
        var fontesDasLibs = new List<string>();
        var incDasLibs = new List<string>();
        var fontesPorLib = new List<(BibliotecaDeFora Lib, List<string> Fontes)>();
        foreach (var lib in usadas)
        {
            incDasLibs.Add(Path.Combine(lib.Pasta, lib.Raiz ?? ""));
            if ((lib.Raiz ?? "").Length == 0) incDasLibs.Add(Path.Combine(lib.Pasta, "utility"));
            int antesDela = fontesDasLibs.Count;
            foreach (string arquivo in lib.Arquivos)
            {
                if (!EntraNaCompilacao(lib, arquivo)) continue;
                fontesDasLibs.Add(Path.Combine(lib.Pasta, arquivo));
            }
            fontesPorLib.Add((lib, fontesDasLibs.Skip(antesDela).ToList()));
        }

        // As pilhas grandes (OpenThread, Zigbee, a rede do IDF): so quem usa paga por elas. Quem
        // usa pode ser o projeto ou uma biblioteca dele (a FastLED abre o esp_http_server).
        var textoDoProjeto = new[] { projeto.Sketch ?? "" }.Concat((projeto.Arquivos ?? new Dictionary<string, string>()).Values.Select(v => v ?? "")).ToList();
        var pilhas = new List<string>();
        foreach (var pilha in PilhasSobDemanda)
        {
            // O MicroPython tambem e o da Pico, que nao tem IDF.
            string chipDaPilha = idf.Count > 0 ? Path.GetFileName(idf[0]) : placa.StartsWith("pi-pico", StringComparison.Ordinal) ? "rp2040" : null;
            if (chipDaPilha is null || !pilha.Chips.Contains(chipDaPilha)) continue;
            if (!textoDoProjeto.Any(pilha.Usa.IsMatch) && !usadas.Any(l => BibliotecaUsaPilha(l, pilha))) continue;
            // O MicroPython escolhe o port pela familia (o ESP32 ou o ARDUINO_ARCH_RP2040), como a IDE passa.
            pilhas.Add(BibliotecaDaPilha(pilha, pasta, zig, cabecalho, inc, idf, pilha.Pasta == "micropython" ? [.. definicoes, .. MacrosDaIde(placa)] : definicoes, avisar));
            definicoes = [.. definicoes, .. pilha.Publicas];
        }

        string encaminhados = PastaDeEncaminhados(usadas);
        string impressao = Impressao(
            [impressaoDoCore, ComDepuracao, "macros da IDE", encaminhados ?? "", .. pilhas, .. unidades.Select(u => u.Nome + "\0" + u.Texto),
             .. abas.Select(a => a.Key + "\0" + a.Value),
             .. fontesDasLibs.Select(f => f + File.GetLastWriteTimeUtc(f).Ticks),
             .. usadas.SelectMany(l => l.Arquivos.Select(a => Path.Combine(l.Pasta, a))).Where(File.Exists)
                      .Select(f => f + File.GetLastWriteTimeUtc(f).Ticks)]);
        string pastaDoSketch = Path.Combine(Cache(), "sketch-" + impressao[..16]);
        string dll = Path.Combine(pastaDoSketch, "sketch.dll");
        if (File.Exists(dll)) return new SketchCompilado(dll, placa, projeto.Sketch);

        string trabalho = pastaDoSketch + ".tmp-" + Guid.NewGuid().ToString("N")[..8];
        Directory.CreateDirectory(trabalho);
        try
        {
            // O programa do ESP-IDF (app_main) ve a configuracao de um projeto do
            // IDF (o log em INFO); o sketch, a do core do Arduino (so os erros).
            File.WriteAllText(Path.Combine(trabalho, "cs_placa.h"),
                              EhPrograma(projeto.Sketch) ? cabecalho + "#define CS_PROGRAMA_IDF 1\n" : cabecalho);
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
            // A pasta do projeto entra DEPOIS do core e das bibliotecas: o
            // #include "x.h" acha a aba primeiro (a pasta de quem inclui), e o
            // <x.h> acha o do core, como no Arduino.
            var erros = CompilarVarios(zig, tarefas, InclusoesDoProjeto(incDasLibs, inc, idf, encaminhados), depurar: true, depois: [trabalho], definicoes: [.. definicoes, .. MacrosDaIde(placa)]);
            if (erros.Count > 0) throw ErroDoCompilador(erros, abas);

            var bibliotecas = fontesPorLib.Where(b => b.Fontes.Count > 0)
                .Select(b => BibliotecaCompilada(b.Lib, b.Fontes, zig, EhPrograma(projeto.Sketch) ? cabecalho + "#define CS_PROGRAMA_IDF 1\n" : cabecalho, usadas, abas,
                                                 InclusoesDoProjeto(incDasLibs, inc, idf, encaminhados), [.. definicoes, .. MacrosDaIde(placa)], impressaoDoCore, avisar))
                .ToList();

            // A DLL: o ponto de entrada e o da ponte, que adia os construtores
            // das globais ate o simulador estar ligado.
            // O -g faz o PDB (o sketch.pdb ao lado da DLL): o depurador le as
            // linhas e as variaveis dele.
            var ligar = new List<string> { "c++", "-shared", "-g", "-target", "x86_64-windows-gnu", "-o", Path.Combine(trabalho, "sketch.dll"),
                                           "-Wl,--entry=cs_entrada_da_dll" };
            ligar.AddRange(tarefas.Select(t => t.Objeto));
            // Inteiras, como a IDE liga os objetos de uma biblioteca: o global que se registra
            // sozinho num .cpp que ninguem chama continua valendo.
            if (bibliotecas.Count > 0) ligar.AddRange(["-Wl,--whole-archive", .. bibliotecas, "-Wl,--no-whole-archive"]);
            ligar.AddRange(objetosDoCore);
            ligar.AddRange(pilhas);
            // O x509 do mbedTLS no Windows le o IP do certificado com o inet_pton (ws2_32).
            if (mbedtls is not null) ligar.AddRange([mbedtls, "-lws2_32"]);
            // Por ultimo e so se ninguem resolveu: sao os avisos do que o IDF declara e nao existe aqui.
            if (fracos is not null) ligar.Add(fracos);
            var (codigo, saida) = Rodar(zig, ligar, trabalho);
            for (int rodada = 0; codigo != 0 && rodada < 5; rodada++)
            {
                string ausentes = AusentesDasBibliotecas(saida, rodada, usadas, [.. tarefas.Select(t => t.Objeto), .. bibliotecas], zig, trabalho);
                if (ausentes is null) break;
                ligar.Add(ausentes);
                (codigo, saida) = Rodar(zig, ligar, trabalho);
            }
            if (codigo != 0) throw ErroDoLigador(saida);

            if (Directory.Exists(pastaDoSketch)) Directory.Delete(pastaDoSketch, true);
            Directory.Move(trabalho, pastaDoSketch);
            AparCache();
        }
        catch
        {
            try { Directory.Delete(trabalho, true); } catch { }
            throw;
        }
        return new SketchCompilado(dll, placa, projeto.Sketch);
    }

    /// <summary>
    /// O cache so cresce: cada versao de cada sketch deixa uma pasta (uns 10 MB, com o PDB),
    /// e o core e as pilhas, uma por placa a cada versao do programa. Sem limite, chega a
    /// encher o disco. Ficam as mais novas; o resto, o compilador refaz se precisar.
    /// A suite dos testes compila milhares de sketches e precisa das demos prontas:
    /// CS_CACHE_SEM_LIMITE deixa tudo.
    /// </summary>
    static void AparCache(int sketches = 300, int porPilha = 60)
    {
        if (Environment.GetEnvironmentVariable("CS_CACHE_SEM_LIMITE") == "1") return;
        try
        {
            var pastas = new DirectoryInfo(Cache()).GetDirectories().Where(d => d.Name != "zig").ToList();
            // A compilacao que morreu no meio (o processo fechou) deixa a pasta de trabalho.
            foreach (var d in pastas.Where(d => d.Name.Contains(".tmp-") && d.LastWriteTimeUtc < DateTime.UtcNow.AddDays(-1)))
                Apagar(d);
            foreach (var grupo in pastas.Where(d => !d.Name.Contains(".tmp-")).GroupBy(d => d.Name.Split('-')[0]))
                foreach (var d in grupo.OrderByDescending(d => d.LastWriteTimeUtc).Skip(grupo.Key == "sketch" ? sketches : porPilha))
                    Apagar(d);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }

        static void Apagar(DirectoryInfo pasta)
        {
            try { pasta.Delete(true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>Compila cada fonte num .o, em paralelo; devolve as mensagens de erro.</summary>
    /// <summary>
    /// O codigo do sketch vai sem otimizacao e com as informacoes de depuracao
    /// (CodeView, para o PDB): cada linha e uma linha, e cada variavel tem
    /// lugar na pilha -- o depurador para onde a pessoa marcou e mostra o valor.
    /// </summary>
    const string ComDepuracao = "depuracao 2: -O0 -gcodeview -fno-omit-frame-pointer -fno-sanitize=all";

    /// <summary>
    /// O programa em Python vira um sketch: as abas .py como texto, e o MicroPython de verdade roda no
    /// setup(), como o firmware dele roda o main.py. Ele nao volta enquanto o programa nao acabar.
    /// </summary>
    public static Projeto ProjetoDoMicroPython(Projeto projeto)
    {
        static string Texto(string t) => "R\"cs_py(" + (t ?? "").Replace("\r", "") + ")cs_py\"";
        var abas = new List<(string Nome, string Texto)> { ("main.py", projeto.Sketch) };
        foreach (var (nome, texto) in projeto.Arquivos ?? new Dictionary<string, string>())
            if (nome.EndsWith(".py", StringComparison.OrdinalIgnoreCase) && nome != "main.py") abas.Add((nome.Replace('\\', '/'), texto));
        var sketch = new StringBuilder("#include <Arduino.h>\nstruct cs_py_aba_t { const char *nome; const char *texto; };\nextern \"C\" void cs_py_rodar(void);\n");
        sketch.Append("extern \"C\" const cs_py_aba_t cs_py_abas[] = {\n");
        foreach (var (nome, texto) in abas) sketch.Append($"{{\"{nome}\", {Texto(texto)}}},\n");
        sketch.Append($"}};\nextern \"C\" const int cs_py_quantas_abas = {abas.Count};\nvoid setup() {{ cs_py_rodar(); }}\nvoid loop() {{ delay(100); }}\n");
        return new Projeto { Sketch = sketch.ToString(), Placa = projeto.Placa, Bibliotecas = [] };
    }

    /// <summary>
    /// O MicroPython daqui e o da ESP32 e o da Pico. A rede da Pico W e a do interpretador: os sockets
    /// do core (cs_sockets.cpp) sao o lwip do IDF, que so a ESP32 tem.
    /// </summary>
    public static bool TemMicroPython(string placa, string fonte = null)
    {
        placa ??= "esp32-devkit-v1";
        if (placa.StartsWith("esp32", StringComparison.Ordinal)) return true;
        return placa.StartsWith("pi-pico", StringComparison.Ordinal)
               && !Regex.IsMatch(fonte ?? "", @"^\s*(import|from)\s+(network|socket|usocket|urequests|requests|umqtt|ntptime|ssl|bluetooth)\b", RegexOptions.Multiline);
    }

    /// <summary>O arquivo de configuracao do mbedTLS (o do IDF, sem o hardware do chip: include/cs_mbedtls_config.h).</summary>
    const string ConfiguracaoDoMbedtls = "-DMBEDTLS_CONFIG_FILE=\"cs_mbedtls_config.h\"";

    /// <summary>
    /// O mbedTLS de verdade (nativo/mbedtls, o do ESP-IDF), compilado uma vez
    /// por chip num libmbedtls.a: o ligador so tira dele o que o sketch (ou o
    /// core) usa -- o sketch que nao usa nem fica maior.
    /// </summary>
    static string BibliotecaMbedtls(string pasta, string zig, string cabecalho, string inc, List<string> idf, Action<string> avisar)
    {
        string library = Path.Combine(pasta, "mbedtls", "library");
        if (!Directory.Exists(library)) return null;
        var fontes = Directory.GetFiles(library, "*.c").Order(StringComparer.Ordinal).ToList();
        string chip = Path.GetFileName(idf[0]);
        string impressao = Impressao(
            [VersaoDoZig(zig), PadraoC, string.Join(" ", PermissivosC), ConfiguracaoDoMbedtls, chip, ImpressaoDoIdf(idf),
             File.ReadAllText(Path.Combine(inc, "cs_mbedtls_config.h")),
             .. Directory.GetFiles(library).Order(StringComparer.Ordinal).Select(f => f + ":" + new FileInfo(f).Length)]);
        string destino = Path.Combine(Cache(), "mbedtls-" + impressao[..16]);
        string arquivo = Path.Combine(destino, "libmbedtls.a");
        if (File.Exists(arquivo)) return arquivo;

        avisar?.Invoke("compilando o mbedTLS desta placa (so na primeira vez)…");
        Directory.CreateDirectory(destino);
        // So o chip (o sdkconfig.h escolhe a configuracao pelo CONFIG_IDF_TARGET): o
        // resto do cs_placa.h (o INPUT, o OUTPUT...) bate com o windows.h que o
        // mbedTLS inclui no Windows.
        File.WriteAllText(Path.Combine(destino, "cs_placa.h"),
                          "#pragma once\n#define ESP32 1\n" + Regex.Match(cabecalho, @"#define CONFIG_IDF_TARGET_ESP32\w* 1").Value + "\n");
        var objetos = fontes.Select(f => Path.Combine(destino, Path.GetFileNameWithoutExtension(f) + ".o")).ToList();
        var erros = CompilarVarios(zig, fontes.Select((f, i) => (f, objetos[i], false)), [destino, inc, library, .. idf],
                                   definicoes: [ConfiguracaoDoMbedtls]);
        if (erros.Count > 0) throw new ErroDeExecucao("o mbedTLS nao compilou: " + string.Join("\n", erros.Take(8)), 0);
        var (codigo, saida) = Rodar(zig, ["ar", "rcs", arquivo, .. objetos], destino);
        if (codigo != 0) throw new ErroDeExecucao("o mbedTLS nao virou biblioteca: " + saida, 0);
        return arquivo;
    }

    /// <summary>
    /// Uma pilha que vem com a placa e pesa demais para todo sketch compilar:
    /// os cabecalhos (Pasta/include) valem sempre, e o codigo (Pasta/src) vira
    /// um arquivo .a na primeira vez que um sketch do chip a usa.
    /// </summary>
    sealed record PilhaSobDemanda(string Pasta, Regex Usa, string[] Chips, string[] Publicas, string[] Privadas, string[] Inclusoes);

    /// <summary>
    /// Se algum fonte da biblioteca (os .cpp e os cabecalhos que eles incluem, como os .cpp.hpp da
    /// FastLED) usa a pilha. Uma biblioteca grande tem milhares de arquivos: a resposta fica guardada
    /// pela data de cada um.
    /// </summary>
    static bool BibliotecaUsaPilha(BibliotecaDeFora lib, PilhaSobDemanda pilha)
    {
        foreach (string arquivo in lib.Arquivos)
        {
            if (Path.GetExtension(arquivo).ToLowerInvariant() is not (".c" or ".cpp" or ".cc" or ".h" or ".hpp" or ".hh" or ".inc" or ".ipp")) continue;
            string caminho = Path.Combine(lib.Pasta, arquivo);
            long data;
            try { data = File.GetLastWriteTimeUtc(caminho).Ticks; }
            catch (IOException) { continue; }
            var chave = (caminho, pilha.Pasta);
            bool usa;
            lock (UsoDasPilhas)
                if (UsoDasPilhas.TryGetValue(chave, out var guardado) && guardado.Data == data) { if (guardado.Usa) return true; continue; }
            try { usa = pilha.Usa.IsMatch(File.ReadAllText(caminho)); }
            catch (IOException) { continue; }
            lock (UsoDasPilhas) UsoDasPilhas[chave] = (data, usa);
            if (usa) return true;
        }
        return false;
    }

    static readonly Dictionary<(string Arquivo, string Pilha), (long Data, bool Usa)> UsoDasPilhas = [];

    static readonly PilhaSobDemanda[] PilhasSobDemanda =
    [
        // As definicoes sao as do components/openthread/CMakeLists.txt do IDF.
        new("openthread", new Regex(@"\b(OThread\w*|OpenThread|esp_openthread\w*|OpenThreadCLI)\b|openthread/", RegexOptions.Compiled),
            ["esp32c6", "esp32h2"],
            ["-DOPENTHREAD_FTD=1", "-DOPENTHREAD_CONFIG_FILE=\"openthread-core-esp32x-ftd-config.h\"",
             "-DOPENTHREAD_PROJECT_LIB_CONFIG_FILE=\"openthread-core-esp32x-ftd-config.h\""],
            ["-DOPENTHREAD_SETTINGS_RAM=1", "-DPACKAGE_VERSION=\"CircuitSketch\"", "-DOPENTHREAD_BUILD_DATETIME=\"\""],
            ["src", "src/core", "src/include", "src/lib"]),
        // A pilha de verdade (a ZBOSS) so existe para o chip: a API e a do simulador (src/cs_zigbee.cpp).
        // Os dois modos do menu "Zigbee mode" do Arduino valem: quem escolhe o papel e o Zigbee.begin().
        new("zigbee", new Regex(@"\b(Zigbee\w*|esp_zb_\w+|ZIGBEE_\w+)\b|esp_zigbee|zboss_api", RegexOptions.Compiled),
            ["esp32", "esp32s2", "esp32s3", "esp32c3", "esp32c6", "esp32h2"],
            ["-DZIGBEE_MODE_ED=1", "-DZIGBEE_MODE_ZCZR=1"], [], ["include/ep"]),
        // A rede de alto nivel do IDF, a de verdade, sobre os sockets do simulador.
        new("idfrede", new Regex(@"\b(esp_http_client\w*|esp_http_server|httpd_\w+|esp_tls\w*|esp_transport\w*|esp_mqtt\w*|mqtt_client|esp_https_ota\w*|esp_https_server|esp_crt_bundle\w*)\b", RegexOptions.Compiled),
            ["esp32", "esp32s2", "esp32s3", "esp32c3", "esp32c6", "esp32h2"],
            [], ["-include", "cs_idfrede_compat.h", "-DESP_PLATFORM=1", "-DIDF_VER=\"v5.5.1\""], ["src/privados"]),
        // O MicroPython de verdade (ferramentas/micropython): quem o pede e o programa em Python (ProjetoDoMicroPython).
        new("micropython", new Regex(@"\bcs_py_rodar\b", RegexOptions.Compiled),
            ["esp32", "esp32s2", "esp32s3", "esp32c3", "esp32c6", "esp32h2", "rp2040"],
            [], [], ["vm", "vm/py", "vm/lib"]),
        // Os endpoints do core sao os de verdade; o protocolo nao roda (src/cs_matter.cpp, motor/Matter.cs).
        new("matter", new Regex(@"\b(Matter\w*|esp_matter\w*)\b", RegexOptions.Compiled),
            ["esp32", "esp32s2", "esp32s3", "esp32c3", "esp32c6", "esp32h2"],
            [], [], []),
    ];

    /// <summary>Os que o simulador procura pelo nome na DLL (os dllexport): esses entram sempre, fora do .a.</summary>
    static bool FicaForaDoCoreA(string fonte)
        => Path.GetFileName(fonte) is "cs_ponte.cpp" or "cs_depurador.cpp" or "cs_freertos.cpp" or "cs_timer.cpp";

    /// <summary>
    /// Uma biblioteca instalada vira um .a no cache: mudar o sketch nao a recompila (o U8g2 tem
    /// 136 arquivos e leva meio minuto). As abas .h entram na conta porque a biblioteca pode
    /// incluir uma delas (o "lv_conf.h" da LVGL), e a pasta dela ve as abas como a do sketch.
    /// </summary>
    static bool EhFraco(string fonte) => Path.GetFileName(fonte) == "cs_idf_nao_simulado.c";

    /// <summary>Como na IDE, o core vem antes das bibliotecas, menos quando elas e que devem vencer o que ele imita.</summary>
    static List<string> InclusoesDoProjeto(List<string> bibliotecas, string inc, List<string> idf, string encaminhados)
        => encaminhados is null ? [inc, .. idf, .. bibliotecas] : [encaminhados, inc, .. idf, .. bibliotecas];

    /// <summary>
    /// Os cabecalhos que o simulador imita de bibliotecas de terceiros (a imitacao move a peca
    /// do circuito; a de verdade falaria com um chip que nao esta aqui). Os que vem com a
    /// placa ficam de fora: esses nunca cedem o lugar, como na IDE.
    /// </summary>
    static readonly HashSet<string> Imitados = new(
        ["Adafruit_GFX.h", "Adafruit_ILI9341.h", "Adafruit_MPU6050.h", "Adafruit_NeoPixel.h", "Adafruit_SH110X.h", "Adafruit_SSD1306.h",
         "Adafruit_ST7735.h", "Adafruit_ST7789.h", "Adafruit_ST77xx.h", "Adafruit_Sensor.h", "Audio.h", "DHT.h", "DallasTemperature.h", "ESP32Servo.h", "HX711.h",
         "IRrecv.h", "IRremote.h", "IRremote.hpp", "IRremoteESP8266.h", "IRutils.h", "Keypad.h", "LedControl.h", "LiquidCrystal.h",
         "LiquidCrystal_I2C.h", "MPU6050.h", "OneWire.h", "PubSubClient.h", "RF24.h", "nRF24L01.h", "printf.h", "RTClib.h", "Servo.h", "SoftwareSerial.h",
         "Stepper.h", "TM1637Display.h", "UniversalTelegramBot.h", "WebSocketsServer.h",
         "NimBLEDevice.h", "NimBLEBeacon.h", "NimBLEEddystoneTLM.h", "NimBLEStream.h", "NimBLEServer.h", "NimBLEClient.h", "NimBLEScan.h", "NimBLEAdvertising.h",
         "NimBLEAdvertisementData.h", "NimBLEAdvertisedDevice.h", "NimBLEUUID.h", "NimBLEAddress.h", "NimBLECharacteristic.h", "NimBLEService.h", "NimBLEDescriptor.h", "NimBLE2904.h", "NimBLERemoteCharacteristic.h", "NimBLERemoteService.h", "NimBLERemoteDescriptor.h", "NimBLEConnInfo.h", "NimBLEAttValue.h", "NimBLEUtils.h"],
        StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// O que nao tem como ser geral: cabecalho de biblioteca que escreve na pedra que o ponteiro
    /// da ESP32 tem 32 bits. Aqui ele tem 64, entao o tipo de tamanho muda so na copia usada na
    /// compilacao; a biblioteca instalada fica como esta.
    /// </summary>
    static readonly (string Cabecalho, (string De, string Para)[] Trocas)[] Remendos =
    [
        ("platforms/esp/int.h",
         [("typedef unsigned int size; ", "typedef unsigned long long size; "), ("typedef unsigned int uptr; ", "typedef unsigned long long uptr; "),
          ("typedef int iptr; ", "typedef long long iptr; "), ("typedef int ptrdiff; ", "typedef long long ptrdiff; ")]),
        ("platforms/esp/32/esp32_lazy_serial0_gate.h", [("#define FL_ESP32_STOCK_ARDUINO_MAIN 0", "#define FL_ESP32_STOCK_ARDUINO_MAIN 1")]),
        // O FastLED ve o _WIN32 do compilador daqui e compilaria o caminho do PC junto com o da placa.
        ("platforms/win/is_win.h", [("#if defined(_WIN32) || defined(_WIN64) || defined(__CYGWIN__)", "#if 0"),
                                    ("#if defined(__MINGW32__) || defined(__MINGW64__)", "#if 0")]),
        ("platforms/esp/32/core/memory_barrier.h",[("#if defined(__XTENSA__)", "#if 1\n#define FL_PLATFORMS_MEMORY_BARRIER __sync_synchronize()\n#elif defined(__XTENSA__)")]),
    ];

    /// <summary>
    /// Uma biblioteca entra inteira: a que veio de verdade traz os seus cabecalhos, mesmo os que
    /// o simulador imita, senao o .cpp dela compilaria contra o cabecalho da imitacao (o I2Cdev
    /// puxa a MPU6050, e o MPU6050.cpp nao combina com o MPU6050.h do simulador). Uma pasta de
    /// encaminhamentos, antes de todas, leva so esses nomes para a biblioteca; o Wire.h e o
    /// resto do que vem com a placa continuam os da placa. Sem nome em comum, nao ha pasta.
    /// </summary>
    static string PastaDeEncaminhados(List<BibliotecaDeFora> usadas)
    {
        var encaminhados = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var remendados = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var lib in usadas)
        {
            string raiz = lib.Raiz ?? "";
            foreach (string arquivo in lib.Arquivos)
            {
                if (!arquivo.StartsWith(raiz, StringComparison.OrdinalIgnoreCase)) continue;
                string nome = arquivo[raiz.Length..];
                if (Imitados.Contains(nome)) encaminhados.TryAdd(nome, Path.Combine(lib.Pasta, arquivo).Replace('\\', '/'));
                if (Remendos.FirstOrDefault(r => r.Cabecalho.Equals(nome, StringComparison.OrdinalIgnoreCase)) is { Cabecalho: not null } remendo)
                {
                    string texto = File.ReadAllText(Path.Combine(lib.Pasta, arquivo));
                    foreach (var (de, para) in remendo.Trocas) texto = texto.Replace(de, para);
                    remendados[nome] = texto;
                }
            }
        }
        if (encaminhados.Count == 0 && remendados.Count == 0) return null;
        string pasta = Path.Combine(Cache(), "encaminhados-" + Impressao([.. encaminhados.Select(e => e.Key + "\0" + e.Value), .. remendados.Select(e => e.Key + "\0" + e.Value)])[..16]);
        Directory.CreateDirectory(pasta);
        foreach (var (nome, texto) in remendados)
        {
            string caminho = Path.Combine(pasta, nome.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(caminho)!);
            File.WriteAllText(caminho, texto, new UTF8Encoding(false));
        }
        foreach (var (nome, destino) in encaminhados)
            File.WriteAllText(Path.Combine(pasta, nome), $"#include \"{destino}\"\n", new UTF8Encoding(false));
        return pasta;
    }

    static string BibliotecaCompilada(BibliotecaDeFora lib, List<string> fontes, string zig, string cabecalho, List<BibliotecaDeFora> usadas,
                                      Dictionary<string, string> abas, List<string> inclusoes, string[] definicoes, string impressaoDoCore, Action<string> avisar)
    {
        string impressao = Impressao(
            [impressaoDoCore, ComDepuracao, cabecalho, lib.Pasta, .. definicoes, .. inclusoes,
             .. usadas.SelectMany(l => l.Arquivos.Select(a => Path.Combine(l.Pasta, a))).Where(File.Exists).Select(f => f + File.GetLastWriteTimeUtc(f).Ticks),
             .. abas.Where(a => !EhFonteCompilada(a.Key)).OrderBy(a => a.Key, StringComparer.Ordinal).Select(a => a.Key + "\0" + a.Value)]);
        string destino = Path.Combine(Cache(), "biblioteca-" + impressao[..16]);
        string arquivo = Path.Combine(destino, "lib.a");
        if (File.Exists(arquivo)) return arquivo;

        avisar?.Invoke($"compilando a biblioteca {lib.Nome} (so na primeira vez)…");
        string trabalho = destino + ".tmp-" + Guid.NewGuid().ToString("N")[..8];
        Directory.CreateDirectory(trabalho);
        try
        {
            File.WriteAllText(Path.Combine(trabalho, "cs_placa.h"), cabecalho);
            foreach (var (nome, texto) in abas.Where(a => !EhFonteCompilada(a.Key)))
            {
                string aba = Path.Combine(trabalho, nome.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(aba)!);
                File.WriteAllText(aba, texto ?? "", new UTF8Encoding(false));
            }
            var objetos = fontes.Select((_, i) => $"b{i}.o").ToList();
            var erros = CompilarVarios(zig, fontes.Select((f, i) => (f, Path.Combine(trabalho, objetos[i]), !f.EndsWith(".c", StringComparison.OrdinalIgnoreCase))),
                                       inclusoes, depurar: true, depois: [trabalho], definicoes: definicoes);
            if (erros.Count > 0) throw ErroDoCompilador(erros, abas);
            var (codigo, saida) = Rodar(zig, ["ar", "rcs", "lib.a", .. objetos], trabalho);
            if (codigo != 0) throw new ErroDeExecucao($"a biblioteca {lib.Nome} nao virou .a: " + saida, 0);
            if (Directory.Exists(destino)) Directory.Delete(destino, true);
            Directory.Move(trabalho, destino);
        }
        catch
        {
            try { Directory.Delete(trabalho, true); } catch { }
            throw;
        }
        return arquivo;
    }

    static bool EhFonteCompilada(string nome)
        => Path.GetExtension(nome).ToLowerInvariant() is ".ino" or ".cpp" or ".cc" or ".cxx" or ".c";

    static string BibliotecaDaPilha(PilhaSobDemanda pilha, string pasta, string zig, string cabecalho, string inc, List<string> idf,
                                    string[] definicoes, Action<string> avisar)
    {
        string raiz = Path.Combine(pasta, pilha.Pasta);
        string src = Path.Combine(raiz, "src");
        var fontes = Directory.EnumerateFiles(src, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".cpp", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".c", StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal).ToList();
        string impressao = Impressao(
            [VersaoDoZig(zig), Padrao, PadraoC, string.Join(" ", Permissivos), string.Join(" ", PermissivosC), cabecalho, ImpressaoDoIdf(idf),
             .. definicoes, .. pilha.Publicas, .. pilha.Privadas,
             .. Directory.GetFiles(inc, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal).Select(f => f + ":" + new FileInfo(f).Length + ":" + File.GetLastWriteTimeUtc(f).Ticks),
             .. Directory.GetFiles(raiz, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal).Select(f => f + ":" + new FileInfo(f).Length + ":" + File.GetLastWriteTimeUtc(f).Ticks)]);
        string destino = Path.Combine(Cache(), pilha.Pasta + "-" + impressao[..16]);
        string arquivo = Path.Combine(destino, "lib" + pilha.Pasta + ".a");
        if (File.Exists(arquivo)) return arquivo;

        avisar?.Invoke($"compilando a pilha {pilha.Pasta} desta placa (so na primeira vez, pode levar alguns minutos)…");
        Directory.CreateDirectory(destino);
        File.WriteAllText(Path.Combine(destino, "cs_placa.h"), cabecalho);
        // Os objetos pelo nome relativo: sao centenas, e a linha de comando do Windows tem limite.
        var objetos = fontes.Select(f => Path.GetRelativePath(src, f).Replace('\\', '_').Replace('/', '_') + ".o").ToList();
        var erros = CompilarVarios(zig, fontes.Select((f, i) => (f, Path.Combine(destino, objetos[i]), !f.EndsWith(".c", StringComparison.OrdinalIgnoreCase))),
                                   [destino, inc, .. pilha.Inclusoes.Select(i => Path.Combine(raiz, i)), .. idf],
                                   definicoes: [.. definicoes, .. pilha.Publicas, .. pilha.Privadas]);
        if (erros.Count > 0) throw new ErroDeExecucao($"a pilha {pilha.Pasta} nao compilou: " + string.Join("\n", erros.Take(8)), 0);
        var (codigo, saida) = Rodar(zig, ["ar", "rcs", Path.GetFileName(arquivo), .. objetos], destino);
        if (codigo != 0) throw new ErroDeExecucao($"a pilha {pilha.Pasta} nao virou biblioteca: " + saida, 0);
        return arquivo;
    }

    static List<string> CompilarVarios(string zig, IEnumerable<(string Fonte, string Objeto, bool Cpp)> tarefas, IEnumerable<string> inclusoes,
                                       bool depurar = false, IEnumerable<string> depois = null, IEnumerable<string> definicoes = null)
    {
        var defs = (definicoes ?? []).ToList();
        var lista = tarefas.ToList();
        var incs = inclusoes.Where(Directory.Exists).Distinct().ToList();
        var incsDepois = (depois ?? []).Where(Directory.Exists).Distinct().ToList();
        var mensagens = new string[lista.Count];
        var codigos = new int[lista.Count];
        Parallel.For(0, lista.Count, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(2, Environment.ProcessorCount) }, i =>
        {
            var (fonte, objeto, cpp) = lista[i];
            var args = new List<string> { cpp ? "c++" : "cc", "-c", "-target", "x86_64-windows-gnu", depurar ? "-O0" : "-O1", "-w",
                                          "-ferror-limit=8", "-fno-caret-diagnostics", "-DCS_NATIVO=1",
                                          // O ponteiro nulo acessa de verdade (e cai como na placa), em vez de o
                                          // otimizador apagar o caminho e por uma armadilha no lugar.
                                          "-fno-delete-null-pointer-checks", cpp ? Padrao : PadraoC,
                                          // Como no compilador das placas (ver stdint.h do core).
                                          "-U__UINT32_TYPE__", "-D__UINT32_TYPE__=long unsigned int", "-U__INT32_TYPE__", "-D__INT32_TYPE__=long int" };
            if (depurar)
            {
                args.Add("-gcodeview");
                args.Add("-fno-omit-frame-pointer");
                // No -O0 o zig liga o UBSan (que aborta o processo no "panic"): o
                // sketch roda como no -O1 e na placa, sem verificacao a mais.
                args.Add("-fno-sanitize=all");
            }
            if (cpp) args.Add("-x");
            if (cpp) args.Add("c++");
            args.AddRange(cpp ? Permissivos : PermissivosC);
            args.AddRange(defs);
            foreach (string inc in incs) { args.Add("-I"); args.Add(inc); }
            foreach (string inc in incsDepois) { args.Add("-idirafter"); args.Add(inc); }
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

    /// <summary>
    /// O formato novo compila a pasta src inteira; o antigo, a raiz e a pasta utility (sem as
    /// subpastas dela), como a IDE: o utility/RPi da RF24 e de outra placa.
    /// </summary>
    static bool EntraNaCompilacao(BibliotecaDeFora lib, string arquivo)
    {
        if (Path.GetExtension(arquivo).ToLowerInvariant() is not (".c" or ".cpp" or ".cc" or ".cxx")) return false;
        string baixo = arquivo.ToLowerInvariant();
        if ((lib.Raiz ?? "").Length > 0) return baixo.StartsWith("src/", StringComparison.Ordinal);
        return !baixo.Contains('/') || (baixo.StartsWith("utility/", StringComparison.Ordinal) && !baixo["utility/".Length..].Contains('/'));
    }

    /// <summary>
    /// As bibliotecas que o projeto alcanca pelos #include, como o detector da IDE: os arquivos
    /// que entram na compilacao e os cabecalhos que eles incluem de fato. Um cabecalho que
    /// ninguem inclui (o BlynkSimpleEthernet.h de quem usa o BlynkSimpleEsp32.h) nao puxa nada,
    /// e o ramo de #if de outra placa tambem nao.
    /// </summary>
    static List<BibliotecaDeFora> BibliotecasUsadas(Projeto projeto, IEnumerable<string> textos, bool instaladasVencem)
    {
        var macros = new HashSet<string>(Preprocessador.MacrosDoCore(projeto.Placa ?? "esp32-devkit-v1").Select(m => m.Nome), StringComparer.Ordinal);
        var usadas = new List<BibliotecaDeFora>();
        var fila = new Queue<(string Texto, BibliotecaDeFora Lib, string Arquivo)>(textos.Select(t => (t, (BibliotecaDeFora)null, (string)null)));
        var vistos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var lidos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Ler(BibliotecaDeFora lib, string arquivo)
        {
            if (lidos.Add(lib.Pasta + "|" + arquivo)) fila.Enqueue((lib.Ler(arquivo) ?? "", lib, arquivo));
        }

        // O #include "x.h" de dentro da biblioteca: a pasta de quem inclui, depois a raiz dela.
        static string NaBiblioteca(BibliotecaDeFora lib, string quem, string nome)
        {
            string[] tentativas = [Path.Combine(Path.GetDirectoryName(quem) ?? "", nome), (lib.Raiz ?? "") + nome];
            foreach (string t in tentativas)
            {
                string caminho = Path.GetFullPath(Path.Combine("c:\\x", t))["c:\\x\\".Length..].Replace('\\', '/');
                if (lib.Arquivos.FirstOrDefault(a => a.Equals(caminho, StringComparison.OrdinalIgnoreCase)) is { } achado) return achado;
            }
            return null;
        }

        while (fila.Count > 0)
        {
            var (texto, dona, quem) = fila.Dequeue();
            foreach (Match m in Inclusao.Matches(SemRamosDeOutraPlaca(SemComentarios(texto), macros)))
            {
                string nome = m.Groups[1].Value.Trim().Replace('\\', '/');
                if (dona is not null && NaBiblioteca(dona, quem, nome) is { } proprio)
                {
                    Ler(dona, proprio);
                    continue;
                }
                if (!vistos.Add(nome)) continue;
                // Como na IDE, o que vem com a placa vence: o SD.h da ESP32 fica, e nao o SD
                // instalado, que nem conhece o chip.
                if (DoCore().Contains(nome) && !(instaladasVencem && Imitados.Contains(nome))) continue;
                var lib = DeBiblioteca(projeto, nome);
                if (lib is null) continue;
                Ler(lib, NaBiblioteca(lib, "", nome));
                if (usadas.Contains(lib)) continue;
                usadas.Add(lib);
                foreach (string arquivo in lib.Arquivos.Where(a => EntraNaCompilacao(lib, a))) Ler(lib, arquivo);
            }
        }
        return usadas;
    }

    static readonly Regex DiretivaDeRamo = new(@"^\s*#\s*(ifdef|ifndef|if|elif|else|endif)\b(.*)$", RegexOptions.Compiled);
    static readonly Regex MacroDePlaca = new(@"^(__AVR\w*|ARDUINO_ARCH_\w+|ESP8266|ESP32|ESP_PLATFORM|TEENSYDUINO|CORE_TEENSY|__SAM\w+|__MK\w+|__IMXRT\w+|__arm__|__ARM\w*|STM32\w*|NRF5\w*|PARTICLE|SPARK|ENERGIA|RASPBERRYPI_PICO|__MBED__|__ZEPHYR__)$", RegexOptions.Compiled);

    /// <summary>
    /// O texto sem os ramos de #if que sao de certeza de outra placa ("#ifdef __AVR__"). Um
    /// #if que nao da para decidir aqui (uma macro de configuracao, uma conta) fica como esta.
    /// </summary>
    static string SemRamosDeOutraPlaca(string texto, HashSet<string> macros)
    {
        if (!texto.Contains("#")) return texto;
        var linhas = texto.Split('\n');
        var pilha = new Stack<(bool PaiAtivo, bool Tomou, bool Ativo, bool Duvida)>();
        bool ativo = true;
        for (int n = 0; n < linhas.Length; n++)
        {
            var m = DiretivaDeRamo.Match(linhas[n].TrimEnd('\r'));
            if (!m.Success)
            {
                if (!ativo) linhas[n] = "";
                continue;
            }
            string tipo = m.Groups[1].Value, resto = m.Groups[2].Value.Trim();
            switch (tipo)
            {
                case "ifdef" or "ifndef" or "if":
                {
                    bool? c = tipo == "ifdef" ? Macro(resto, macros) : tipo == "ifndef" ? Nao(Macro(resto, macros)) : new Condicao(resto, macros).Avaliar();
                    pilha.Push((ativo, c == true, ativo && c != false, c is null));
                    ativo = pilha.Peek().Ativo;
                    break;
                }
                case "elif" when pilha.Count > 0:
                {
                    var t = pilha.Pop();
                    bool? c = new Condicao(resto, macros).Avaliar();
                    bool esta = t.PaiAtivo && !t.Tomou && c != false;
                    pilha.Push((t.PaiAtivo, t.Tomou || (c == true && !t.Duvida), esta, t.Duvida || c is null));
                    ativo = esta;
                    break;
                }
                case "else" when pilha.Count > 0:
                {
                    var t = pilha.Pop();
                    bool esta = t.PaiAtivo && !t.Tomou;
                    pilha.Push((t.PaiAtivo, true, esta, t.Duvida));
                    ativo = esta;
                    break;
                }
                case "endif" when pilha.Count > 0:
                    ativo = pilha.Pop().PaiAtivo;
                    break;
            }
        }
        return string.Join('\n', linhas);
    }

    static bool? Nao(bool? v) => v is null ? null : !v;

    static bool? Macro(string texto, HashSet<string> macros)
    {
        string nome = Regex.Match(texto, @"^\w+").Value;
        return macros.Contains(nome) ? true : MacroDePlaca.IsMatch(nome) ? false : null;
    }

    /// <summary>A conta de um #if com defined, !, && e ||. Qualquer outra coisa: nao da para saber (null).</summary>
    sealed class Condicao(string texto, HashSet<string> macros)
    {
        readonly string[] partes = Regex.Matches(Regex.Replace(texto, @"//.*$|/\*.*?\*/", ""), @"\w+|\|\||&&|!=|[!()]|\S").Select(m => m.Value).ToArray();
        int i;

        public bool? Avaliar()
        {
            bool? v = Ou();
            return i == partes.Length ? v : null;
        }

        bool? Ou()
        {
            bool? v = E();
            while (i < partes.Length && partes[i] == "||")
            {
                i++;
                bool? d = E();
                v = v == true || d == true ? true : v is null || d is null ? null : false;
            }
            return v;
        }

        bool? E()
        {
            bool? v = Termo();
            while (i < partes.Length && partes[i] == "&&")
            {
                i++;
                bool? d = Termo();
                v = v == false || d == false ? false : v is null || d is null ? null : true;
            }
            return v;
        }

        bool? Termo()
        {
            if (i >= partes.Length) return null;
            string p = partes[i++];
            if (p == "!") return Nao(Termo());
            if (p == "(")
            {
                bool? v = Ou();
                if (i < partes.Length && partes[i] == ")") i++;
                return v;
            }
            if (p == "defined")
            {
                bool abre = i < partes.Length && partes[i] == "(";
                if (abre) i++;
                bool? v = i < partes.Length ? Macro(partes[i++], macros) : null;
                if (abre && i < partes.Length && partes[i] == ")") i++;
                return v;
            }
            // Um numero ou uma conta (ARDUINO >= 100): engole o resto do termo, sem decidir.
            while (i < partes.Length && partes[i] is not ("||" or "&&" or ")")) i++;
            return null;
        }
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
        extern int cs_nvs_iniciada __attribute__((weak));

        void setup(void)
        {
            if (&cs_nvs_iniciada) cs_nvs_iniciada = 0;
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
        // Como a IDE: so se o sketch nao inclui. O "#define" antes do include dele vale.
        if (!Regex.IsMatch(SemComentarios(sketch ?? ""), @"(?m)^\s*#\s*include\s*[<""]Arduino\.h[>""]")) sb.Append("#include <Arduino.h>\n");
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
        foreach (var f in Funcoes(texto))
        {
            // Dentro de um #if, os prototipos sumiriam junto com o ramo: ficam antes dele.
            var abertos = RamosAbertos(texto, f.InicioDaLinha);
            return abertos.Count > 0 ? abertos[0].Inicio : f.InicioDaLinha;
        }
        return -1;
    }

    /// <summary>
    /// Os #if abertos nesta posicao do texto, com o caminho de cada um ate aqui (o #if e os
    /// #elif e #else ja passados).
    /// </summary>
    static List<(int Inicio, List<string> Caminho)> RamosAbertos(string texto, int ate)
    {
        var abertos = new List<(int Inicio, List<string> Caminho)>();
        // Sem os comentarios, mas com as mesmas linhas: um "#if" num bloco de comentario nao conta.
        var limpas = SemComentarios(texto[..Math.Min(ate, texto.Length)]).Split('\n');
        int inicio = 0, linhaOriginal = 0;
        var originais = texto.Split('\n');
        foreach (string linha in limpas)
        {
            var m = DiretivaDeRamo.Match(linha.TrimEnd('\r'));
            if (m.Success)
            {
                string tipo = m.Groups[1].Value;
                if (tipo is "if" or "ifdef" or "ifndef") abertos.Add((inicio, [linha.TrimEnd('\r')]));
                else if (tipo == "endif" && abertos.Count > 0) abertos.RemoveAt(abertos.Count - 1);
                else if (abertos.Count > 0) abertos[^1].Caminho.Add(linha.TrimEnd('\r'));
            }
            inicio += originais[Math.Min(linhaOriginal++, originais.Length - 1)].Length + 1;
        }
        return abertos;
    }

    /// <summary>Os prototipos das funcoes definidas no topo do arquivo.</summary>
    static IEnumerable<string> Prototipos(string texto)
    {
        foreach (var f in Funcoes(texto))
        {
            if (f.Assinatura.Contains('=')) continue;                         // argumento padrao: o prototipo repetiria
            if (Regex.IsMatch(f.Assinatura, @"^\s*(template|static\s+inline)\b")) continue;
            // Dentro do mesmo #if da funcao: o ramo de outra placa pode usar um tipo que aqui nao existe.
            var abertos = RamosAbertos(texto, f.InicioDaLinha);
            yield return string.Concat(abertos.SelectMany(a => a.Caminho).Select(l => l + "\n"))
                         + f.Assinatura.Trim() + ";"
                         + string.Concat(abertos.Select(_ => "\n#endif"));
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
                    var m = Regex.Match(cabeca, @"^\s*((?:[A-Za-z_][\w:<>,\*&\s]*?[\s\*&]+)?)([A-Za-z_]\w*)\s*\(((?:[^()]|\([^()]*\))*)\)\s*(const\s*)?$",
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

    /// <summary>
    /// Os nomes que o sketch declara fora das funcoes (as globais, os objetos
    /// "Servo s;" e "LiquidCrystal lcd(12, 11...)"): o depurador mostra os que
    /// o PDB tem como dado. Funcao com o mesmo jeito tambem entra aqui; o PDB
    /// diz que nao e variavel.
    /// </summary>
    public static IReadOnlyList<string> NomesDasGlobais(string texto)
    {
        string limpo = Limpar(texto ?? "");
        var nomes = new List<string>();
        var comando = new StringBuilder();
        int profundidade = 0;
        bool corpo = false;
        foreach (char c in limpo)
        {
            if (c == '{')
            {
                // Chave depois de "=": o valor inicial (int v[] = {1, 2}); sem: o corpo.
                if (profundidade == 0) corpo = !comando.ToString().Contains('=');
                profundidade++;
                if (!corpo) comando.Append(c);
                continue;
            }
            if (c == '}')
            {
                profundidade = Math.Max(0, profundidade - 1);
                if (profundidade == 0 && corpo) { corpo = false; comando.Clear(); }
                else if (!corpo) comando.Append(c);
                continue;
            }
            if (profundidade > 0 && corpo) continue;
            if (c == ';' && profundidade == 0)
            {
                foreach (Match m in Regex.Matches(comando.ToString(), @"(?<=[\w\*&>\],]\s*)\b([A-Za-z_]\w*)\s*(?=\[|=|,|\(|$)"))
                    if (!nomes.Contains(m.Groups[1].Value)) nomes.Add(m.Groups[1].Value);
                comando.Clear();
                continue;
            }
            comando.Append(c);
        }
        return nomes;
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
            // O texto bruto (R"rawliteral(...)rawliteral", a pagina da web
            // dentro do sketch): ate o fechamento com o mesmo delimitador,
            // atravessando as linhas -- o "function beep(){" do JavaScript
            // la dentro nao e funcao do C++.
            if (c == '"' && i > 0 && sb[i - 1] == 'R' && (i < 2 || !char.IsLetterOrDigit(sb[i - 2]) || sb[i - 2] is 'u' or 'U' or 'L' or '8'))
            {
                int abre = texto.IndexOf('(', i);
                string delimitador = abre > i && abre - i <= 17 ? texto[(i + 1)..abre] : null;
                int fecha = delimitador is null ? -1 : texto.IndexOf(")" + delimitador + "\"", abre, StringComparison.Ordinal);
                if (fecha > 0)
                {
                    int ate = fecha + delimitador.Length + 2;
                    for (; i < ate; i++)
                        if (sb[i] != '\n') sb[i] = ' ';
                    continue;
                }
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
    /// <summary>
    /// Os cabecalhos de verdade do ESP-IDF (nativo/idf, ver
    /// ferramentas/idf/copiar_idf.py) da placa: os do chip e os comuns, depois
    /// dos do simulador -- o que o simulador tem com o mesmo nome vale.
    /// </summary>
    static List<string> PastasDoIdf(string pasta, string cabecalho)
    {
        var chip = Regex.Match(cabecalho, @"#define CONFIG_IDF_TARGET_(ESP32\w*) 1");
        if (!chip.Success) return [];
        string raiz = Path.Combine(pasta, "idf");
        // Depois dos do IDF, os do mbedTLS (nativo/mbedtls/include) e os do TinyUSB (nativo/tinyusb/include).
        return new[] { Path.Combine(raiz, chip.Groups[1].Value.ToLowerInvariant()), Path.Combine(raiz, "comum"), Path.Combine(raiz, "proximos"),
                       Path.Combine(pasta, "mbedtls", "include"), Path.Combine(pasta, "tinyusb", "include") }
            .Concat(PilhasSobDemanda.Select(p => Path.Combine(pasta, p.Pasta, "include")))
            .Where(Directory.Exists).ToList();
    }

    /// <summary>Os cabecalhos do IDF mudam a impressao pelo nome e pelo tamanho (sao milhares: le-los a cada compilacao custaria).</summary>
    static string ImpressaoDoIdf(List<string> pastas)
        => string.Join("\n", pastas.SelectMany(p => Directory.EnumerateFiles(p, "*", SearchOption.AllDirectories))
                                    .Order(StringComparer.Ordinal).Select(f => f + ":" + new FileInfo(f).Length));

    /// <summary>
    /// As macros da placa na linha de comando, como a IDE do Arduino passa para todo arquivo. A
    /// biblioteca testa o "#ifdef ARDUINO" ou o "#ifdef ESP32" antes de incluir o Arduino.h (o
    /// U8x8lib.cpp inteiro fica dentro de um): sem isto, o codigo dela sumia sem erro nenhum.
    /// As constantes do cs_placa.h (INPUT, DEFAULT...) ficam de fora: na IDE quem as traz e
    /// o Arduino.h, e um .c que nao o inclui pode usar esses nomes.
    /// </summary>
    static IEnumerable<string> MacrosDaIde(string placa)
        => Preprocessador.MacrosDoCore(placa)
            // A IDE nao passa a versao do core (quem da e o esp_arduino_version.h), e o ESP32 dela
            // vale ele mesmo: o "#if ESP32" e falso, e um enum com esse nome compila.
            .Where(m => m.Nome != "__cplusplus" && !m.Nome.StartsWith("RTC_") && !m.Nome.StartsWith("ESP_ARDUINO_VERSION"))
            .Select(m => m.Nome == "ESP32" ? "-DESP32=ESP32" : $"-D{m.Nome}={m.Valor}")
            // Quem traz o time_t aqui e a libc do Windows: a biblioteca nao declara o dela (o TimeLib).
            .Append("-D__time_t_defined=1");

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

    /// <summary>
    /// Na IDE o ligador joga fora o que ninguem chama (--gc-sections) antes de reclamar, e uma funcao
    /// de biblioteca que chama outra que nao existe so e erro se for usada: a IRremote com
    /// NO_LED_FEEDBACK_CODE tem varias. O ligador do Windows reclama antes de jogar fora. Entao a funcao
    /// que so a biblioteca menciona ganha um corpo que para o programa se for mesmo chamada, e o sketch
    /// que a chama direto continua com o erro de sempre. O registrador de um periferico (o I2S0 e o
    /// MCPWM0 que os drivers da FastLED citam, e que na placa o script do ligador poe no endereco do
    /// chip) vira uma memoria zerada. Sem nada a fazer, devolve null.
    /// </summary>
    static string AusentesDasBibliotecas(string saida, int rodada, List<BibliotecaDeFora> usadas, IEnumerable<string> objetos, string zig, string trabalho)
    {
        var faltas = Regex.Matches(saida, @"undefined symbol:\s*(.+?)\r?\n((?:>>>.*(?:\r?\n|$))+)");
        if (faltas.Count == 0) return null;
        var pastas = usadas.Select(l => l.Pasta.Replace('/', '\\')).ToList();
        var padroes = new List<(string Legivel, Regex Padrao)>();
        var corpos = new StringBuilder("int printf(const char *, ...);\nvoid abort(void);\n");
        var vistos = new HashSet<string>();
        foreach (Match falta in faltas)
        {
            string legivel = falta.Groups[1].Value.Trim();
            string nome = Regex.Replace(legivel, @"\(.*\)( const)?$", "");
            // O de C (o extern "C" da FastLED, o registrador) vem sem os parenteses e o nome e o proprio simbolo.
            bool deC = nome == legivel;
            if (deC ? !Regex.IsMatch(nome, @"^[A-Za-z_]\w*$") : nome.Contains('<') || nome.Contains(' ')) return null;
            var quem = Regex.Matches(falta.Groups[2].Value, @"referenced by (.+)").Select(m => m.Groups[1].Value.Trim().Replace('/', '\\')).ToList();
            // O dado lido pelo endereco (.refptr) vem sem o arquivo, so com o objeto: o lib.a e o de uma biblioteca.
            bool dado = deC && quem.Count > 0 && quem.All(q => q.StartsWith("lib.a(") && q.EndsWith("(.refptr." + nome + ")"));
            if (!dado && !quem.All(q => pastas.Any(p => q.StartsWith(p, StringComparison.OrdinalIgnoreCase)))) return null;
            if (deC)
            {
                if (vistos.Add(nome))
                    corpos.Append(dado
                        ? $"__attribute__((weak, aligned(64))) unsigned char cs_ausente{vistos.Count}[16384] __asm__(\"{nome}\");\n"
                        : $"void cs_ausente{vistos.Count}(void) __asm__(\"{nome}\") __attribute__((weak));\n" +
                          $"void cs_ausente{vistos.Count}(void) {{ printf(\"\\n{nome}: a biblioteca chamou esta funcao, mas ela nao existe\\n\"); abort(); }}\n");
                continue;
            }
            var partes = nome.Split("::").Select(p => p.Length + Regex.Escape(p)).ToList();
            padroes.Add((legivel, new Regex((partes.Count > 1 ? "_ZNK?" : "_Z") + string.Concat(partes) + "[A-Za-z0-9_]*")));
        }
        foreach (string objeto in padroes.Count > 0 ? objetos.Where(File.Exists) : Enumerable.Empty<string>())
        {
            string bytes = Encoding.Latin1.GetString(File.ReadAllBytes(objeto));
            foreach (var (legivel, padrao) in padroes)
                foreach (Match m in padrao.Matches(bytes))
                    if (vistos.Add(m.Value))
                        corpos.Append($"void cs_ausente{vistos.Count}(void) __asm__(\"{m.Value}\") __attribute__((weak));\n" +
                                      $"void cs_ausente{vistos.Count}(void) {{ printf(\"\\n{legivel.Replace("\\", "").Replace("\"", "")}: a biblioteca chamou esta funcao, mas ela nao existe\\n\"); abort(); }}\n");
        }
        if (vistos.Count == 0) return null;
        string fonte = Path.Combine(trabalho, $"cs_ausentes{rodada}.c"), objetoNovo = Path.ChangeExtension(fonte, ".o");
        File.WriteAllText(fonte, corpos.ToString(), new UTF8Encoding(false));
        var (codigo, _) = Rodar(zig, ["cc", "-c", "-w", "-target", "x86_64-windows-gnu", fonte, "-o", objetoNovo], trabalho);
        return codigo == 0 ? objetoNovo : null;
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
public sealed record SketchCompilado(string Dll, string Placa, string Sketch = null) : ProgramaCompilado;
