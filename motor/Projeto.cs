namespace Simulador;

/// <summary>
/// Uma biblioteca instalada pela pessoa: uma pasta com .h e .cpp, do jeito
/// que vem do GitHub ou da pasta libraries da Arduino IDE.
///
/// Os arquivos sao lidos na hora em que o #include pede -- instalar trinta
/// bibliotecas nao pode deixar o Rodar trinta vezes mais lento.
/// </summary>
public sealed class BibliotecaDeFora
{
    public string Nome { get; init; } = "";

    /// <summary>A pasta dela. Serve de chave: o mesmo arquivo ganha sempre o mesmo numero.</summary>
    public string Pasta { get; init; } = "";

    /// <summary>Os caminhos relativos dos arquivos de codigo ("src/Botao.h"), com barra normal.</summary>
    public IReadOnlyList<string> Arquivos { get; init; } = [];

    /// <summary>
    /// Onde o "#include &lt;...&gt;" procura: "src/" no formato novo (o de
    /// library.properties), a propria pasta no antigo.
    /// </summary>
    public string Raiz { get; init; } = "";

    public Func<string, string> Ler { get; init; } = _ => "";
}

/// <summary>O que compila junto: o sketch, as abas, a placa e as bibliotecas instaladas.</summary>
public sealed class Projeto
{
    public string Sketch { get; init; } = "";

    /// <summary>As abas do projeto: "config.h", "motor.cpp".</summary>
    public IDictionary<string, string> Arquivos { get; init; }

    /// <summary>O id da placa: e ele que decide as macros do core ("ESP32", "__AVR__").</summary>
    public string Placa { get; init; }

    public IReadOnlyList<BibliotecaDeFora> Bibliotecas { get; init; } = [];

    /// <summary>
    /// Cabecalho que o simulador executa por dentro -- "Servo.h", "WiFi.h".
    /// Esses nunca sao lidos da pasta de uma biblioteca instalada: a Servo
    /// de verdade mexe nos timers do chip, e a do simulador move o servo
    /// que esta no circuito.
    /// </summary>
    public Func<string, bool> DoSimulador { get; init; }
}

/// <summary>
/// Monta o programa inteiro a partir das unidades de compilacao, como o
/// compilador do Arduino faz: o sketch (com as outras abas .ino coladas
/// nele), cada .cpp do projeto e cada .cpp das bibliotecas que alguem
/// incluiu. Cada unidade passa pelo pre-processador sozinha -- macro de
/// um .cpp nao vaza para o outro -- e no fim tudo vira um programa so.
/// </summary>
sealed class Unidades(Projeto projeto, Linguagem linguagem)
{
    readonly Dictionary<int, (BibliotecaDeFora Lib, string Caminho)> deBiblioteca = [];
    readonly List<BibliotecaDeFora> usadas = [];
    readonly Dictionary<string, string> lidos = new(StringComparer.OrdinalIgnoreCase);
    readonly List<string> avisos = [];

    readonly Dictionary<string, string> abas = projeto.Arquivos is null
        ? new(StringComparer.OrdinalIgnoreCase)
        : new(projeto.Arquivos, StringComparer.OrdinalIgnoreCase);

    static readonly string[] Codigo = [".c", ".cpp", ".cc", ".cxx"];

    static bool EhCodigo(string nome)
        => Codigo.Contains(System.IO.Path.GetExtension(nome).ToLowerInvariant());

    Preprocessador Novo() => Preprocessador.DaPlaca(projeto.Placa, linguagem, Resolver, Conhecido);

    bool Conhecido(string nome) => projeto.DoSimulador?.Invoke(nome) ?? false;

    /// <summary>
    /// Os metodos que devolvem o String do Arduino -- e nao std::string. O
    /// mais comum e o da BLE: no core 2.x da ESP32 o getValue() devolvia
    /// std::string, e os tutoriais escrevem "std::string valor =
    /// pCharacteristic->getValue();"; no 3.x ele devolve String, e a linha
    /// nao compila.
    /// </summary>
    static readonly HashSet<string> DevolvemString =
    [
        "getValue", "readValue", "getName", "toString", "readString", "readStringUntil", "getString",
        "substring", "getManufacturerData", "getServiceData",
    ];

    /// <summary>
    /// "std::string x = algo->getValue();": o String do Arduino nao vira
    /// std::string sozinho, e o GCC para a compilacao. O simulador, que guarda
    /// os dois do mesmo jeito, deixaria passar -- e o sketch rodaria aqui e
    /// nao compilaria na IDE.
    /// </summary>
    void ConferirStdString(List<Token> tokens)
    {
        if (projeto.Placa?.StartsWith("arduino-", StringComparison.Ordinal) == true) return;
        for (int i = 0; i + 2 < tokens.Count; i++)
        {
            if (!tokens[i].DoStd || tokens[i + 1].Tipo != T.Id || !tokens[i + 2].E("=")) continue;
            int fim = i + 3;
            while (fim < tokens.Count && !tokens[fim].E(";") && !tokens[fim].E("{") && !tokens[fim].E("}")) fim++;
            if (fim >= tokens.Count || !tokens[fim].E(";") || fim - 4 < i + 3) continue;
            var metodo = tokens[fim - 3];
            if (tokens[fim - 4].Texto is not ("->" or ".") || metodo.Tipo != T.Id || !DevolvemString.Contains(metodo.Texto)
                || !tokens[fim - 2].E("(") || !tokens[fim - 1].E(")"))
                continue;
            throw new ErroDeSintaxe(
                "conversion from 'String' to non-scalar type 'std::string' {aka 'std::__cxx11::basic_string<char>'} "
                + $"requested -- o {metodo.Texto}() devolve o String do Arduino" + (DaBle(metodo.Texto)
                    ? " (no core 2.x da ESP32 a biblioteca BLE devolvia std::string; no 3.x, String)" : "")
                + $". Declare \"String {tokens[i + 1].Texto} = ...\", ou converta: "
                + $"\"std::string {tokens[i + 1].Texto} = algo->{metodo.Texto}().c_str();\".", tokens[i].Linha);
        }

        static bool DaBle(string m) => m is "getValue" or "readValue" or "getName" or "getManufacturerData" or "getServiceData";
    }

    public Programa Compilar()
    {
        // A unidade principal: o sketch e as outras abas .ino, uma depois
        // da outra, como a IDE faz antes de compilar.
        var pp = Novo();
        var tokens = Lexer.Tokenizar(projeto.Sketch ?? "", pp, 0);
        foreach (var (nome, texto) in abas.Where(a => a.Key.EndsWith(".ino", StringComparison.OrdinalIgnoreCase))
                                          .OrderBy(a => a.Key, StringComparer.Ordinal))
        {
            tokens.RemoveAt(tokens.Count - 1);
            tokens.AddRange(Lexer.Tokenizar(texto, pp, DoProjeto(nome, texto).Indice));
        }
        avisos.AddRange(pp.Avisos);
        ConferirStdString(tokens);
        var principal = new Parser(tokens).AnalisarPrograma();

        // Os .cpp do projeto, cada um sozinho. Numa ESP32, os .c vao
        // para o compilador de verdade (TinyCC) e rodam nativos -- junto
        // com os .h do projeto, que eles incluem.
        bool nativo = NativoNaPlaca(projeto.Placa);
        var dasAbas = new List<Programa>();
        foreach (var (nome, texto) in abas.Where(a => EhCodigo(a.Key)).OrderBy(a => a.Key, StringComparer.Ordinal))
        {
            if (nativo && EhC(nome))
            {
                nativos.Add(new FonteNativa(nome, null, texto ?? "", null, null));
                continue;
            }
            dasAbas.Add(Unidade(texto, DoProjeto(nome, texto).Indice));
        }
        if (nativos.Count > 0)
            foreach (var (nome, texto) in abas.Where(a => EhCabecalho(a.Key)))
                nativos.Add(new FonteNativa(nome, null, texto ?? "", null, null));

        // Os .cpp de cada biblioteca incluida. A lista cresce enquanto
        // anda: uma biblioteca pode incluir outra.
        var dasBibliotecas = new List<Programa>();
        for (int i = 0; i < usadas.Count; i++)
            foreach (string caminho in FontesDe(usadas[i]))
            {
                if (nativo && EhC(caminho))
                {
                    var lib = usadas[i];
                    nativos.Add(new FonteNativa(caminho, System.IO.Path.Combine(lib.Pasta, caminho), null,
                                                System.IO.Path.Combine(lib.Pasta, lib.Raiz), lib.Nome));
                    continue;
                }
                var arquivo = DaBiblioteca(usadas[i], caminho);
                dasBibliotecas.Add(Unidade(arquivo.Texto, arquivo.Indice));
            }

        return Juntar([.. dasBibliotecas, .. dasAbas, principal]);
    }

    readonly List<FonteNativa> nativos = [];

    static bool EhC(string nome)
        => System.IO.Path.GetExtension(nome).Equals(".c", StringComparison.OrdinalIgnoreCase);

    static bool EhCabecalho(string nome)
        => System.IO.Path.GetExtension(nome).ToLowerInvariant() is ".h" or ".inc" or ".inl";

    /// <summary>
    /// O .c roda nativo nas placas da familia ESP32: e o ESP-IDF que o
    /// runtime nativo imita. Nas outras, continua no interpretador.
    /// </summary>
    public static bool NativoNaPlaca(string placa)
        => placa is not null && placa.StartsWith("esp32", StringComparison.OrdinalIgnoreCase);

    Programa Unidade(string texto, int indice)
    {
        var pp = Novo();
        var tokens = Lexer.Tokenizar(texto, pp, indice);
        avisos.AddRange(pp.Avisos);
        return new Parser(tokens).AnalisarPrograma();
    }

    /// <summary>
    /// As unidades viram um programa. O que veio do mesmo .h por dois
    /// caminhos -- o sketch e o .cpp da biblioteca incluem os dois o
    /// Botao.h -- entra uma vez so: e o mesmo arquivo, na mesma linha.
    /// </summary>
    Programa Juntar(List<Programa> unidades)
    {
        var globais = new List<Cmd>();
        var funcoes = new List<Funcao>();
        var classes = new List<ClasseDecl>();
        var vistos = new HashSet<string>();

        foreach (var unidade in unidades)
        {
            foreach (var cmd in unidade.Globais)
                if (Chave(cmd) is not { } chave || vistos.Add(chave)) globais.Add(cmd);

            foreach (var f in unidade.Funcoes)
                if (vistos.Add($"F|{f.Linha}|{f.Nome}|{f.Params.Count}")) funcoes.Add(f);

            foreach (var k in unidade.Classes)
                if (vistos.Add($"K|{k.Linha}|{k.Nome}")) classes.Add(k);
        }

        var prog = new Programa(globais, funcoes)
        {
            Classes = classes,
            Avisos = [.. avisos.Distinct()],
            Nativos = [.. nativos],
            Placa = projeto.Placa,
            Fonte = projeto.Sketch,
            TabelaDeParticoes = projeto.Arquivos?
                .FirstOrDefault(a => a.Key.Equals("partitions.csv", StringComparison.OrdinalIgnoreCase)).Value,
        };
        return linguagem == Linguagem.C ? Compilador.PartirNoLaco(prog, "main") : prog;
    }

    static string Chave(Cmd cmd) => cmd switch
    {
        CmdDecl d => $"D|{d.Linha}|{d.Nome}",
        CmdDeclObj o => $"O|{o.Linha}|{o.Nome}",
        ClasseDecl k => $"C|{k.Linha}|{k.Nome}",
        CmdEstrutura e => $"E|{e.Linha}|{string.Join(",", e.Nomes)}",
        CmdVarios v => $"V|{v.Linha}|{(v.Corpo.FirstOrDefault() as CmdDecl)?.Nome}",
        _ => null,
    };

    // ---------------- arquivos ----------------

    ArquivoDeCodigo DoProjeto(string nome, string texto)
        => new(Origens.Registrar("projeto|" + nome, nome), nome, texto ?? "");

    ArquivoDeCodigo DaBiblioteca(BibliotecaDeFora lib, string caminho)
    {
        int indice = Origens.Registrar(lib.Pasta + "|" + caminho, lib.Nome + "/" + System.IO.Path.GetFileName(caminho));
        deBiblioteca[indice] = (lib, caminho);

        string chave = lib.Pasta + "|" + caminho;
        if (!lidos.TryGetValue(chave, out var texto))
        {
            texto = lib.Ler(caminho) ?? "";
            lidos[chave] = texto;
        }
        return new ArquivoDeCodigo(indice, lib.Nome + "/" + System.IO.Path.GetFileName(caminho), texto);
    }

    static string Pasta(string caminho)
    {
        int corte = caminho.LastIndexOf('/');
        return corte < 0 ? "" : caminho[..(corte + 1)];
    }

    /// <summary>"src/../util/x.h" vira "util/x.h".</summary>
    static string Normalizar(string caminho)
    {
        var partes = new List<string>();
        foreach (string parte in caminho.Replace('\\', '/').Split('/'))
        {
            if (parte is "" or ".") continue;
            if (parte == "..") { if (partes.Count > 0) partes.RemoveAt(partes.Count - 1); continue; }
            partes.Add(parte);
        }
        return string.Join('/', partes);
    }

    static string Achar(BibliotecaDeFora lib, string caminho)
        => lib.Arquivos.FirstOrDefault(a => string.Equals(a, caminho, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// O arquivo que um #include pede. Aspas procuram ao lado de quem
    /// incluiu -- a aba do projeto, ou a pasta do arquivo da biblioteca --
    /// e depois nas bibliotecas; os sinais de menor e maior, so nelas.
    /// </summary>
    ArquivoDeCodigo Resolver(string nome, bool comAspas, int deQuem)
    {
        nome = nome.Replace('\\', '/');

        if (comAspas)
        {
            if (deBiblioteca.TryGetValue(deQuem, out var dono))
            {
                if (Achar(dono.Lib, Normalizar(Pasta(dono.Caminho) + nome)) is { } aoLado)
                    return DaBiblioteca(dono.Lib, aoLado);
            }
            else if (abas.TryGetValue(nome, out var texto))
                return DoProjeto(abas.Keys.First(k => string.Equals(k, nome, StringComparison.OrdinalIgnoreCase)), texto);
        }

        // A do simulador vence a instalada: e ela que mexe no circuito. As
        // que definem cores e comandos (a do display) entregam um .h so
        // com os #define de verdade.
        if (Cabecalhos.DoSimulador(nome) is { } doSimulador) return doSimulador;
        if (Conhecido(nome)) return null;

        foreach (var lib in NaOrdem(nome))
            if (Achar(lib, Normalizar(lib.Raiz + nome)) is { } achado)
            {
                if (!usadas.Contains(lib)) usadas.Add(lib);
                return DaBiblioteca(lib, achado);
            }

        return null;
    }

    /// <summary>
    /// A regra da IDE quando duas bibliotecas tem o mesmo .h: vence a da
    /// pasta com o nome do arquivo ("Botao/" para "Botao.h").
    /// </summary>
    IEnumerable<BibliotecaDeFora> NaOrdem(string cabecalho)
    {
        string nome = System.IO.Path.GetFileNameWithoutExtension(cabecalho);
        return (projeto.Bibliotecas ?? [])
            .OrderBy(b => string.Equals(b.Nome, nome, StringComparison.OrdinalIgnoreCase) ? 0 : 1);
    }

    /// <summary>
    /// Os .cpp que o compilador leva de uma biblioteca: tudo o que esta em
    /// src/ no formato novo; a pasta e a utility/ no antigo.
    /// </summary>
    static IEnumerable<string> FontesDe(BibliotecaDeFora lib)
        => lib.Arquivos.Where(a => EhCodigo(a) && (lib.Raiz.Length > 0
                ? a.StartsWith(lib.Raiz, StringComparison.OrdinalIgnoreCase)
                : !a.Contains('/') || a.StartsWith("utility/", StringComparison.OrdinalIgnoreCase)))
            .OrderBy(a => a, StringComparer.Ordinal);
}
