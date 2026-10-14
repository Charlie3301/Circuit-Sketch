using System.Text.RegularExpressions;

namespace Simulador;

/// <summary>
/// Um programa AVR montado: opcodes de verdade.
///
/// <see cref="Flash"/> é a memória de programa em palavras de 16 bits,
/// exatamente como ela fica no chip. Não é uma lista de "instruções
/// interpretadas": são os mesmos bytes que o avr-as produziria, e
/// <see cref="Hex"/> os escreve no formato que o avrdude grava.
/// </summary>
public sealed class ProgramaMontado
{
    /// <summary>A memória de programa, em palavras de 16 bits.</summary>
    public ushort[] Flash { get; init; } = [];

    /// <summary>Quantas palavras foram realmente escritas.</summary>
    public int Palavras { get; init; }

    /// <summary>De cada endereço de palavra para a linha do texto.</summary>
    public Dictionary<int, int> Linhas { get; init; } = [];

    /// <summary>Rótulo -&gt; endereço em palavras.</summary>
    public Dictionary<string, int> Rotulos { get; init; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Constantes do <c>.equ</c> e apelidos do <c>.def</c>.</summary>
    public Dictionary<string, string> Simbolos { get; init; } =
        new(StringComparer.OrdinalIgnoreCase);

    public int Bytes => Palavras * 2;

    /// <summary>A linha do texto que gerou a palavra neste endereço.</summary>
    public int LinhaDe(int palavra) => Linhas.GetValueOrDefault(palavra, 0);

    /// <summary>
    /// O programa em Intel HEX, do jeito que o avrdude grava.
    ///
    /// É o que fecha a conta para quem vai usar isto de verdade: o
    /// binário que rodou na tela é o MESMO que vai para a placa. Sem
    /// isso, "funcionou no simulador" continuaria sendo uma promessa
    /// sem como conferir.
    /// </summary>
    public string Hex()
    {
        var saida = new System.Text.StringBuilder();

        for (int i = 0; i < Palavras; i += 8)
        {
            int quantas = Math.Min(8, Palavras - i);
            int endereco = i * 2;

            var linha = new List<byte> { (byte)(quantas * 2),
                                         (byte)(endereco >> 8), (byte)endereco, 0x00 };

            for (int k = 0; k < quantas; k++)
            {
                // O AVR guarda a palavra com o byte baixo primeiro.
                linha.Add((byte)(Flash[i + k] & 0xFF));
                linha.Add((byte)(Flash[i + k] >> 8));
            }

            int soma = linha.Sum(b => b);
            linha.Add((byte)(-soma & 0xFF));

            saida.Append(':');
            foreach (byte b in linha) saida.Append(b.ToString("X2"));
            saida.AppendLine();
        }

        saida.AppendLine(":00000001FF");
        return saida.ToString();
    }
}

/// <summary>
/// O montador de Assembly AVR: texto -&gt; opcodes de 16 bits.
///
/// Esta é a diferença entre "simular Assembly" e MONTAR Assembly. Um
/// interpretador de instrução pode acertar o resultado e errar o tempo,
/// o tamanho e o desvio relativo -- e é justamente isso que derruba um
/// programa que funcionou na tela quando ele chega na placa.
///
/// Aqui cada instrução vira a palavra que o chip executa. As
/// consequências disso são as que importam:
///
///   - <c>rjmp .-4</c> conta BYTES de deslocamento, como manda o
///     manual, e não instruções;
///   - <c>lds</c> e <c>sts</c> ocupam duas palavras, então tudo depois
///     delas fica no endereço certo;
///   - o <c>.org</c> põe a rotina de interrupção no vetor certo;
///   - o resultado sai em <c>.hex</c> e pode ser gravado.
///
/// Duas passagens: a primeira mede e anota os rótulos, a segunda
/// codifica. É assim porque um salto para frente só tem endereço
/// depois que se sabe onde o rótulo caiu.
/// </summary>
public static class Montador
{
    public const int PalavrasDaFlash = 16384;   // 32 KB do ATmega328P

    public static Programa Montar(string fonte)
    {
        var montado = Juntar(fonte);

        // O setup zera o chip; o loop roda um punhado de ciclos por
        // vez. É o que permite parar, pausar e misturar com placas em
        // outras linguagens sem nada de especial no resto do programa.
        var setup = new Funcao("void", "setup", [],
            new CmdBloco([Chamar("__avr_reiniciar", null, 1)], 1), 1);

        var loop = new Funcao("void", "loop", [],
            new CmdBloco([Chamar("__avr_passo", CiclosPorVolta, 1)], 1), 1);

        return new Programa([], [setup, loop])
        {
            Linguagem = Linguagem.Assembly,
            Montado = montado,
        };
    }

    /// <summary>
    /// Quantos ciclos rodam a cada volta do loop.
    ///
    /// São 16 mil, ou um milissegundo de chip a 16 MHz. Alto o
    /// bastante para um laço de espera andar depressa, e baixo o
    /// bastante para o botão de parar responder na hora.
    /// </summary>
    public const int CiclosPorVolta = 16_000;

    static Cmd Chamar(string nome, int? argumento, int linha)
        => new CmdExpr(
            new Chamada(new Variavel(nome, linha),
                argumento is { } k ? [new Literal(k, "", true, false, linha)] : [],
                linha),
            linha);

    // =================================================================
    //  as duas passagens
    // =================================================================
    static readonly Regex DoRotulo =
        new(@"^([A-Za-z_.][A-Za-z0-9_]*)\s*:(.*)$", RegexOptions.Compiled);

    public static ProgramaMontado Juntar(string fonte)
    {
        var linhas = Limpar(fonte);

        var simbolos = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var rotulos = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        // ---- primeira passagem: onde cada coisa cai ----
        Percorrer(linhas, simbolos, rotulos, null, null, out _);

        // ---- segunda: codificar, agora com os rótulos na mão ----
        var flash = new ushort[PalavrasDaFlash];
        var deQuemVeio = new Dictionary<int, int>();

        Percorrer(linhas, simbolos, rotulos, flash, deQuemVeio, out int fim);

        return new ProgramaMontado
        {
            Flash = flash,
            Palavras = fim,
            Linhas = deQuemVeio,
            Rotulos = rotulos,
            Simbolos = simbolos,
        };
    }

    /// <summary>
    /// Uma passagem pelo texto.
    ///
    /// Com <paramref name="flash"/> nulo ela só MEDE: anota rótulos e
    /// anda com o contador. Com flash, codifica. As duas passagens
    /// precisam andar exatamente o mesmo tanto, senão os rótulos da
    /// primeira apontam para o lugar errado na segunda -- e é por isso
    /// que é o mesmo código, e não dois parecidos.
    /// </summary>
    static void Percorrer(List<(string Texto, int Linha)> linhas,
                          Dictionary<string, string> simbolos,
                          Dictionary<string, int> rotulos,
                          ushort[] flash,
                          Dictionary<int, int> deQuemVeio,
                          out int fim)
    {
        int onde = 0;      // endereço em PALAVRAS
        int maior = 0;

        foreach (var (bruta, numero) in linhas)
        {
            string texto = bruta;

            // Um rótulo pode dividir a linha com a instrução.
            var achado = DoRotulo.Match(texto);

            while (achado.Success)
            {
                rotulos[achado.Groups[1].Value] = onde;
                texto = achado.Groups[2].Value.Trim();
                achado = DoRotulo.Match(texto);
            }

            if (texto.Length == 0) continue;

            if (texto.StartsWith('.'))
            {
                onde = Diretiva(texto, numero, simbolos, rotulos, flash, ref maior, onde);
                continue;
            }

            var (nome, args) = Partir(texto);

            if (flash is null)
            {
                onde += Tamanho(nome, args);
            }
            else
            {
                var palavras = Codificar(nome, args, onde, numero, simbolos, rotulos);

                foreach (ushort palavra in palavras)
                {
                    if (onde >= flash.Length)
                        throw new ErroDeSintaxe("o programa nao cabe na flash", numero);

                    flash[onde] = palavra;
                    deQuemVeio[onde] = numero;
                    onde++;
                }
            }

            maior = Math.Max(maior, onde);
        }

        fim = Math.Max(maior, onde);
    }

    static List<(string, int)> Limpar(string fonte)
    {
        var saida = new List<(string, int)>();
        int numero = 0;

        foreach (string bruta in (fonte ?? "").Replace("\r\n", "\n").Split('\n'))
        {
            numero++;
            string texto = SemComentario(bruta).Trim();
            if (texto.Length > 0) saida.Add((texto, numero));
        }

        return saida;
    }

    /// <summary>Tira o comentário, respeitando o que está entre aspas.</summary>
    static string SemComentario(string linha)
    {
        bool dentro = false;

        for (int i = 0; i < linha.Length; i++)
        {
            char c = linha[i];

            if (c == (char)34) { dentro = !dentro; continue; }
            if (dentro) continue;

            if (c == ';') return linha[..i];
            if (c == '/' && i + 1 < linha.Length && linha[i + 1] == '/') return linha[..i];
        }

        return linha;
    }

    // =================================================================
    //  diretivas
    // =================================================================
    static int Diretiva(string texto, int linha,
                        Dictionary<string, string> simbolos,
                        Dictionary<string, int> rotulos,
                        ushort[] flash, ref int maior, int onde)
    {
        string[] partes = texto.Split([' ', '\t'], 2);
        string nome = partes[0].ToLowerInvariant();
        string resto = partes.Length > 1 ? partes[1].Trim() : "";

        switch (nome)
        {
            case ".equ":
            case ".set":
            case ".def":
            {
                var pedacos = resto.Split(['=', ','], 2);
                if (pedacos.Length == 2) simbolos[pedacos[0].Trim()] = pedacos[1].Trim();
                return onde;
            }

            // O .org é o que põe a rotina de interrupção no vetor
            // certo. Sem ele, um programa com ISR desvia para o nada.
            case ".org":
                return (int)Conta(resto, linha, simbolos, rotulos);

            case ".dw":
            case ".word":
            {
                foreach (string item in Argumentos(resto))
                {
                    ushort v = (ushort)Conta(item, linha, simbolos, rotulos);
                    if (flash is not null && onde < flash.Length) flash[onde] = v;
                    onde++;
                }

                maior = Math.Max(maior, onde);
                return onde;
            }

            case ".db":
            case ".byte":
            {
                var bytes = new List<byte>();

                foreach (string item in Argumentos(resto))
                {
                    if (item.StartsWith((char)34) && item.EndsWith((char)34))
                        bytes.AddRange(System.Text.Encoding.ASCII.GetBytes(item[1..^1]));
                    else
                        bytes.Add((byte)Conta(item, linha, simbolos, rotulos));
                }

                // A flash é de palavras: dois bytes por vez, o baixo
                // primeiro, que é a ordem do AVR.
                for (int i = 0; i < bytes.Count; i += 2)
                {
                    ushort v = bytes[i];
                    if (i + 1 < bytes.Count) v |= (ushort)(bytes[i + 1] << 8);

                    if (flash is not null && onde < flash.Length) flash[onde] = v;
                    onde++;
                }

                maior = Math.Max(maior, onde);
                return onde;
            }

            // Diretivas que o montador de verdade usa para escolher
            // seção, alinhar e nomear: aqui as instruções já vão para
            // a flash em ordem, e não há seção a escolher.
            default:
                return onde;
        }
    }

    // =================================================================
    //  partir a linha
    // =================================================================
    static (string, string[]) Partir(string texto)
    {
        string[] partes = texto.Split([' ', '\t'], 2);
        string nome = partes[0].ToLowerInvariant();

        return (nome, partes.Length > 1 ? Argumentos(partes[1]) : []);
    }

    static string[] Argumentos(string resto)
    {
        var saida = new List<string>();
        int fundo = 0, ini = 0;
        bool dentro = false;

        for (int i = 0; i < resto.Length; i++)
        {
            char c = resto[i];

            if (c == (char)34) dentro = !dentro;
            else if (dentro) continue;
            else if (c == '(') fundo++;
            else if (c == ')') fundo--;
            else if (c == ',' && fundo == 0)
            {
                saida.Add(resto[ini..i].Trim());
                ini = i + 1;
            }
        }

        saida.Add(resto[ini..].Trim());
        return [.. saida.Where(a => a.Length > 0)];
    }

    // =================================================================
    //  quantas palavras cada instrução ocupa
    // =================================================================
    //
    // Quatro instruções do AVR ocupam DUAS palavras. Errar isso
    // desalinha tudo o que vem depois -- e o sintoma é um salto que cai
    // no meio de outra instrução, que é o pior defeito possível.
    static int Tamanho(string nome, string[] args) => nome switch
    {
        "lds" or "sts" or "jmp" or "call" => 2,
        _ => TamanhoDoAtalho(nome, args),
    };

    static int TamanhoDoAtalho(string nome, string[] args)
    {
        _ = args;
        return 1;
    }

    // =================================================================
    //  a codificação
    // =================================================================
    static ushort[] Codificar(string nome, string[] args, int onde, int linha,
                              Dictionary<string, string> simbolos,
                              Dictionary<string, int> rotulos)
    {
        int Reg(int i) => Registrador(Arg(args, i, nome, linha), linha, simbolos);
        long K(int i) => Conta(Arg(args, i, nome, linha), linha, simbolos, rotulos);

        // O deslocamento de um salto relativo conta em PALAVRAS, a
        // partir da instrução SEGUINTE.
        int Salto(int i)
        {
            string alvo = Apelido(Arg(args, i, nome, linha), simbolos);

            // ".+4" e ".-2" contam BYTES no manual da Atmel, e é assim
            // que todo exemplo escreve.
            if (alvo.StartsWith('.') && long.TryParse(alvo[1..], out long bytes))
                return (int)(bytes / 2);

            if (alvo.StartsWith(".+") && long.TryParse(alvo[2..], out long mais))
                return (int)(mais / 2);

            long destino = Conta(alvo, linha, simbolos, rotulos);
            return (int)(destino - (onde + 1));
        }

        switch (nome)
        {
            // ---- dois registradores ----
            case "add": return [DoisRegistradores(0x0C00, Reg(0), Reg(1))];
            case "adc": return [DoisRegistradores(0x1C00, Reg(0), Reg(1))];
            case "sub": return [DoisRegistradores(0x1800, Reg(0), Reg(1))];
            case "sbc": return [DoisRegistradores(0x0800, Reg(0), Reg(1))];
            case "and": return [DoisRegistradores(0x2000, Reg(0), Reg(1))];
            case "or": return [DoisRegistradores(0x2800, Reg(0), Reg(1))];
            case "eor": return [DoisRegistradores(0x2400, Reg(0), Reg(1))];
            case "mov": return [DoisRegistradores(0x2C00, Reg(0), Reg(1))];
            case "cp": return [DoisRegistradores(0x1400, Reg(0), Reg(1))];
            case "cpc": return [DoisRegistradores(0x0400, Reg(0), Reg(1))];
            case "cpse": return [DoisRegistradores(0x1000, Reg(0), Reg(1))];
            case "mul": return [DoisRegistradores(0x9C00, Reg(0), Reg(1))];

            // ---- atalhos que o montador expande ----
            //
            // Não são instruções do chip: são nomes para instruções que
            // existem. "clr r16" É "eor r16, r16", e o binário sai
            // idêntico ao do avr-as.
            case "clr": return [DoisRegistradores(0x2400, Reg(0), Reg(0))];
            case "tst": return [DoisRegistradores(0x2000, Reg(0), Reg(0))];
            case "lsl": return [DoisRegistradores(0x0C00, Reg(0), Reg(0))];
            case "rol": return [DoisRegistradores(0x1C00, Reg(0), Reg(0))];
            case "ser": return [ComConstante(0xE000, Reg(0), 0xFF, linha)];
            case "cbr": return [ComConstante(0x7000, Reg(0), ~K(1) & 0xFF, linha)];
            case "sbr": return [ComConstante(0x6000, Reg(0), K(1), linha)];

            // ---- registrador e constante (r16..r31) ----
            case "ldi": return [ComConstante(0xE000, Reg(0), K(1), linha)];
            case "subi": return [ComConstante(0x5000, Reg(0), K(1), linha)];
            case "sbci": return [ComConstante(0x4000, Reg(0), K(1), linha)];
            case "andi": return [ComConstante(0x7000, Reg(0), K(1), linha)];
            case "ori": return [ComConstante(0x6000, Reg(0), K(1), linha)];
            case "cpi": return [ComConstante(0x3000, Reg(0), K(1), linha)];

            // ---- par de registradores e constante ----
            case "adiw": return [ComPar(0x9600, Reg(0), K(1), linha)];
            case "sbiw": return [ComPar(0x9700, Reg(0), K(1), linha)];

            // ---- um registrador ----
            case "com": return [UmRegistrador(0x9400, Reg(0))];
            case "neg": return [UmRegistrador(0x9401, Reg(0))];
            case "swap": return [UmRegistrador(0x9402, Reg(0))];
            case "inc": return [UmRegistrador(0x9403, Reg(0))];
            case "asr": return [UmRegistrador(0x9405, Reg(0))];
            case "lsr": return [UmRegistrador(0x9406, Reg(0))];
            case "ror": return [UmRegistrador(0x9407, Reg(0))];
            case "dec": return [UmRegistrador(0x940A, Reg(0))];
            case "push": return [UmRegistrador(0x920F, Reg(0))];
            case "pop": return [UmRegistrador(0x900F, Reg(0))];

            case "movw":
                return [(ushort)(0x0100 | ((Reg(0) / 2) << 4) | (Reg(1) / 2))];

            // ---- E/S ----
            case "in": return [(ushort)(0xB000 | Espalhar(EnderecoDeES(args, 1, simbolos, linha))
                                        | (Reg(0) << 4))];
            case "out": return [(ushort)(0xB800 | Espalhar(EnderecoDeES(args, 0, simbolos, linha))
                                         | (Reg(1) << 4))];

            case "sbi": return [BitDeES(0x9A00, args, simbolos, linha)];
            case "cbi": return [BitDeES(0x9800, args, simbolos, linha)];
            case "sbic": return [BitDeES(0x9900, args, simbolos, linha)];
            case "sbis": return [BitDeES(0x9B00, args, simbolos, linha)];

            // ---- memória ----
            case "lds": return [(ushort)(0x9000 | (Reg(0) << 4)), (ushort)K(1)];
            case "sts": return [(ushort)(0x9200 | (Reg(1) << 4)), (ushort)K(0)];

            case "ld": return [Carregar(Reg(0), Arg(args, 1, nome, linha), linha)];
            case "st": return [Guardar(Arg(args, 0, nome, linha), Reg(1), linha)];
            case "ldd": return [ComDeslocamento(Reg(0), Arg(args, 1, nome, linha), false, linha)];
            case "std": return [ComDeslocamento(Reg(1), Arg(args, 0, nome, linha), true, linha)];

            case "lpm":
                return args.Length == 0
                    ? [0x95C8]
                    : [(ushort)(0x9004 | (Reg(0) << 4)
                                | (Arg(args, 1, nome, linha).Contains('+') ? 1 : 0))];

            // ---- desvios ----
            case "rjmp": return [(ushort)(0xC000 | (Salto(0) & 0x0FFF))];
            case "rcall": return [(ushort)(0xD000 | (Salto(0) & 0x0FFF))];

            case "jmp":
            {
                long destino = Conta(Arg(args, 0, nome, linha), linha, simbolos, rotulos);
                return [(ushort)(0x940C | (((destino >> 17) & 0x3F) << 4)
                                 | ((destino >> 16) & 1)), (ushort)destino];
            }

            case "call":
            {
                long destino = Conta(Arg(args, 0, nome, linha), linha, simbolos, rotulos);
                return [(ushort)(0x940E | (((destino >> 17) & 0x3F) << 4)
                                 | ((destino >> 16) & 1)), (ushort)destino];
            }

            case "ret": return [0x9508];
            case "reti": return [0x9518];
            case "ijmp": return [0x9409];
            case "icall": return [0x9509];

            // ---- desvios condicionais ----
            //
            // Todos são a mesma instrução com o bit do SREG mudando.
            case "brcs" or "brlo": return [Ramo(true, AvrIsa.BitC, Salto(0))];
            case "brcc" or "brsh": return [Ramo(false, AvrIsa.BitC, Salto(0))];
            case "breq": return [Ramo(true, AvrIsa.BitZ, Salto(0))];
            case "brne": return [Ramo(false, AvrIsa.BitZ, Salto(0))];
            case "brmi": return [Ramo(true, AvrIsa.BitN, Salto(0))];
            case "brpl": return [Ramo(false, AvrIsa.BitN, Salto(0))];
            case "brvs": return [Ramo(true, AvrIsa.BitV, Salto(0))];
            case "brvc": return [Ramo(false, AvrIsa.BitV, Salto(0))];
            case "brlt": return [Ramo(true, AvrIsa.BitS, Salto(0))];
            case "brge": return [Ramo(false, AvrIsa.BitS, Salto(0))];
            case "brhs": return [Ramo(true, AvrIsa.BitH, Salto(0))];
            case "brhc": return [Ramo(false, AvrIsa.BitH, Salto(0))];
            case "brts": return [Ramo(true, AvrIsa.BitT, Salto(0))];
            case "brtc": return [Ramo(false, AvrIsa.BitT, Salto(0))];
            case "brie": return [Ramo(true, AvrIsa.BitI, Salto(0))];
            case "brid": return [Ramo(false, AvrIsa.BitI, Salto(0))];

            case "sbrc": return [(ushort)(0xFC00 | (Reg(0) << 4) | (int)(K(1) & 7))];
            case "sbrs": return [(ushort)(0xFE00 | (Reg(0) << 4) | (int)(K(1) & 7))];
            case "bst": return [(ushort)(0xFA00 | (Reg(0) << 4) | (int)(K(1) & 7))];
            case "bld": return [(ushort)(0xF800 | (Reg(0) << 4) | (int)(K(1) & 7))];

            // ---- bits do SREG ----
            case "sei": return [Bandeira(true, AvrIsa.BitI)];
            case "cli": return [Bandeira(false, AvrIsa.BitI)];
            case "sec": return [Bandeira(true, AvrIsa.BitC)];
            case "clc": return [Bandeira(false, AvrIsa.BitC)];
            case "sez": return [Bandeira(true, AvrIsa.BitZ)];
            case "clz": return [Bandeira(false, AvrIsa.BitZ)];
            case "sen": return [Bandeira(true, AvrIsa.BitN)];
            case "cln": return [Bandeira(false, AvrIsa.BitN)];
            case "sev": return [Bandeira(true, AvrIsa.BitV)];
            case "clv": return [Bandeira(false, AvrIsa.BitV)];
            case "set": return [Bandeira(true, AvrIsa.BitT)];
            case "clt": return [Bandeira(false, AvrIsa.BitT)];
            case "seh": return [Bandeira(true, AvrIsa.BitH)];
            case "clh": return [Bandeira(false, AvrIsa.BitH)];
            case "bset": return [Bandeira(true, (int)K(0))];
            case "bclr": return [Bandeira(false, (int)K(0))];

            // ---- controle ----
            case "nop": return [0x0000];
            case "sleep": return [0x9588];
            case "wdr": return [0x95A8];
            case "break": return [0x9598];

            default:
                throw new ErroDeSintaxe($"não conheço a instrução '{nome}'", linha);
        }
    }

    static string Arg(string[] args, int i, string nome, int linha)
        => i < args.Length
            ? args[i]
            : throw new ErroDeSintaxe($"falta um argumento em '{nome}'", linha);

    static ushort DoisRegistradores(int baseDoOpcode, int d, int r)
        => (ushort)(baseDoOpcode | ((r & 0x10) << 5) | (d << 4) | (r & 0x0F));

    static ushort UmRegistrador(int baseDoOpcode, int d)
        => (ushort)(baseDoOpcode | (d << 4));

    static ushort ComConstante(int baseDoOpcode, int d, long k, int linha)
    {
        if (d < 16)
            throw new ErroDeSintaxe(
                $"esta instrução só aceita r16 a r31, e veio r{d}", linha);

        return (ushort)(baseDoOpcode | (int)(((k >> 4) & 0x0F) << 8)
                        | ((d - 16) << 4) | (int)(k & 0x0F));
    }

    static ushort ComPar(int baseDoOpcode, int d, long k, int linha)
    {
        if (d is not (24 or 26 or 28 or 30))
            throw new ErroDeSintaxe(
                $"adiw e sbiw só aceitam r24, r26, r28 e r30 -- veio r{d}", linha);

        int par = (d - 24) / 2;
        return (ushort)(baseDoOpcode | (int)(((k >> 4) & 3) << 6)
                        | (par << 4) | (int)(k & 0x0F));
    }

    /// <summary>O endereço de E/S espalhado nos bits que o IN e o OUT usam.</summary>
    static int Espalhar(int endereco)
        => ((endereco & 0x30) << 5) | (endereco & 0x0F);

    static int EnderecoDeES(string[] args, int i, Dictionary<string, string> simbolos, int linha)
    {
        string nome = Apelido(Arg(args, i, "in/out", linha), simbolos);

        if (AvrIsa.Registradores.TryGetValue(nome, out int endereco))
        {
            // Os estendidos não cabem no IN nem no OUT: eles só se
            // alcançam por LDS e STS, e dizer isso é melhor do que
            // gravar um endereço truncado que acerta outro registrador.
            if (endereco > 0x3F)
                throw new ErroDeSintaxe(
                    $"'{nome}' está na E/S estendida: use lds ou sts, e não in ou out", linha);

            return endereco;
        }

        return (int)Numero(nome, linha);
    }

    static ushort BitDeES(int baseDoOpcode, string[] args,
                          Dictionary<string, string> simbolos, int linha)
    {
        int endereco = EnderecoDeES(args, 0, simbolos, linha);

        if (endereco > 0x1F)
            throw new ErroDeSintaxe(
                "sbi, cbi, sbic e sbis só alcançam os 32 primeiros registradores de E/S",
                linha);

        string bit = Apelido(Arg(args, 1, "sbi/cbi", linha), simbolos);

        long numero = AvrIsa.Bits.TryGetValue(bit, out int conhecido)
            ? conhecido : Numero(bit, linha);

        return (ushort)(baseDoOpcode | (endereco << 3) | (int)(numero & 7));
    }

    static ushort Ramo(bool ligado, int bit, int salto)
        => (ushort)((ligado ? 0xF000 : 0xF400) | ((salto & 0x7F) << 3) | bit);

    static ushort Bandeira(bool ligar, int bit)
        => (ushort)(0x9408 | (ligar ? 0 : 0x0080) | (bit << 4));

    /// <summary>"ld r16, X" e os parentes: X, X+, -X, Y, Y+, -Y, Z, Z+, -Z.</summary>
    static ushort Carregar(int d, string ponteiro, int linha) => ponteiro.ToUpperInvariant() switch
    {
        "X" => (ushort)(0x900C | (d << 4)),
        "X+" => (ushort)(0x900D | (d << 4)),
        "-X" => (ushort)(0x900E | (d << 4)),
        "Y" => (ushort)(0x8008 | (d << 4)),
        "Y+" => (ushort)(0x9009 | (d << 4)),
        "-Y" => (ushort)(0x900A | (d << 4)),
        "Z" => (ushort)(0x8000 | (d << 4)),
        "Z+" => (ushort)(0x9001 | (d << 4)),
        "-Z" => (ushort)(0x9002 | (d << 4)),
        _ => throw new ErroDeSintaxe($"'{ponteiro}' não é um ponteiro de carga", linha),
    };

    static ushort Guardar(string ponteiro, int r, int linha) => ponteiro.ToUpperInvariant() switch
    {
        "X" => (ushort)(0x920C | (r << 4)),
        "X+" => (ushort)(0x920D | (r << 4)),
        "-X" => (ushort)(0x920E | (r << 4)),
        "Y" => (ushort)(0x8208 | (r << 4)),
        "Y+" => (ushort)(0x9209 | (r << 4)),
        "-Y" => (ushort)(0x920A | (r << 4)),
        "Z" => (ushort)(0x8200 | (r << 4)),
        "Z+" => (ushort)(0x9201 | (r << 4)),
        "-Z" => (ushort)(0x9202 | (r << 4)),
        _ => throw new ErroDeSintaxe($"'{ponteiro}' não é um ponteiro de escrita", linha),
    };

    /// <summary>"ldd r16, Y+3" e "std Z+5, r16".</summary>
    static ushort ComDeslocamento(int registrador, string alvo, bool escrita, int linha)
    {
        int mais = alvo.IndexOf('+');
        if (mais < 0) throw new ErroDeSintaxe($"'{alvo}' precisa de um deslocamento", linha);

        char qual = char.ToUpperInvariant(alvo[0]);
        long q = Numero(alvo[(mais + 1)..].Trim(), linha);

        int baseDoOpcode = 0x8000 | (escrita ? 0x0200 : 0) | (qual == 'Y' ? 8 : 0);

        return (ushort)(baseDoOpcode
                        | (int)((q & 0x20) << 8) | (int)((q & 0x18) << 7)
                        | (registrador << 4) | (int)(q & 7));
    }

    // =================================================================
    //  nomes, números e contas
    // =================================================================
    static string Apelido(string nome, Dictionary<string, string> simbolos)
    {
        string atual = (nome ?? "").Trim();

        for (int i = 0; i < 8 && simbolos.TryGetValue(atual, out string outro); i++)
            atual = outro.Trim();

        return atual;
    }

    static int Registrador(string nome, int linha, Dictionary<string, string> simbolos)
    {
        string limpo = Apelido(nome, simbolos);

        if (limpo.Length >= 2 && (limpo[0] is 'r' or 'R')
            && int.TryParse(limpo[1..], out int n) && n is >= 0 and < 32)
            return n;

        return limpo.ToUpperInvariant() switch
        {
            "XL" => 26, "XH" => 27,
            "YL" => 28, "YH" => 29,
            "ZL" => 30, "ZH" => 31,
            _ => throw new ErroDeSintaxe($"'{nome}' não é um registrador", linha),
        };
    }

    /// <summary>
    /// Uma conta de montador: número, símbolo, rótulo ou expressão.
    ///
    /// O que aparece de verdade nos programas: "(1&lt;&lt;PB5)",
    /// "low(RAMEND)", "0x3F", "LED+1". Sem isto, metade dos exemplos de
    /// Assembly que circulam não monta.
    /// </summary>
    static long Conta(string texto, int linha,
                      Dictionary<string, string> simbolos,
                      Dictionary<string, int> rotulos)
    {
        string limpo = Apelido(texto, simbolos);

        while (limpo.StartsWith('(') && limpo.EndsWith(')') && Emparelha(limpo))
            limpo = limpo[1..^1].Trim();

        // low() e high() partem um endereço de 16 bits em dois bytes. É
        // como se carrega o ponteiro de pilha no começo de todo
        // programa: "ldi r16, low(RAMEND)".
        foreach (var (funcao, corta) in new (string, Func<long, long>)[]
                 {
                     ("low", v => v & 0xFF),
                     ("high", v => (v >> 8) & 0xFF),
                     ("byte1", v => v & 0xFF),
                     ("byte2", v => (v >> 8) & 0xFF),
                 })
        {
            if (!limpo.StartsWith(funcao + "(", StringComparison.OrdinalIgnoreCase)) continue;
            if (!limpo.EndsWith(')')) continue;

            return corta(Conta(limpo[(funcao.Length + 1)..^1], linha, simbolos, rotulos));
        }

        // Da menor precedência para a maior, como manda a aritmética.
        foreach (string op in new[] { "|", "^", "&", "<<", ">>", "+", "-", "*", "/" })
        {
            int onde = ForaDeParenteses(limpo, op);
            if (onde <= 0) continue;

            long esq = Conta(limpo[..onde], linha, simbolos, rotulos);
            long dir = Conta(limpo[(onde + op.Length)..], linha, simbolos, rotulos);

            return op switch
            {
                "|" => esq | dir,
                "^" => esq ^ dir,
                "&" => esq & dir,
                "<<" => esq << (int)dir,
                ">>" => esq >> (int)dir,
                "+" => esq + dir,
                "-" => esq - dir,
                "*" => esq * dir,
                _ => dir == 0 ? 0 : esq / dir,
            };
        }

        if (limpo.StartsWith('~')) return ~Conta(limpo[1..], linha, simbolos, rotulos);

        if (rotulos.TryGetValue(limpo, out int endereco)) return endereco;
        if (AvrIsa.Registradores.TryGetValue(limpo, out int registrador)) return registrador;
        if (AvrIsa.Bits.TryGetValue(limpo, out int bit)) return bit;
        if (AvrIsa.Vetores.TryGetValue(limpo, out int vetor)) return vetor;

        return Numero(limpo, linha);
    }

    static bool Emparelha(string texto)
    {
        int fundo = 0;

        for (int i = 0; i < texto.Length; i++)
        {
            if (texto[i] == '(') fundo++;
            else if (texto[i] == ')' && --fundo == 0) return i == texto.Length - 1;
        }

        return false;
    }

    /// <summary>Onde o operador aparece fora de parênteses, ou -1.</summary>
    static int ForaDeParenteses(string texto, string op)
    {
        int fundo = 0;

        for (int i = texto.Length - op.Length; i > 0; i--)
        {
            char c = texto[i];

            if (c == ')') fundo++;
            else if (c == '(') fundo--;
            else if (fundo == 0 && texto.AsSpan(i, op.Length).SequenceEqual(op))
            {
                // "<<" e ">>" não podem ser confundidos com "<" e ">"
                // sozinhos, e o "-" de um número negativo não é
                // subtração.
                if (op is "<" or ">") continue;
                return i;
            }
        }

        return -1;
    }

    static long Numero(string texto, int linha)
    {
        string limpo = texto.Trim();

        if (limpo.Length == 0) return 0;

        // 'a' é o código do caractere.
        if (limpo.Length == 3 && limpo[0] == (char)39 && limpo[2] == (char)39) return limpo[1];

        bool negativo = limpo.StartsWith('-');
        if (negativo) limpo = limpo[1..].Trim();

        long valor;

        try
        {
            if (limpo.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                valor = Convert.ToInt64(limpo[2..], 16);
            else if (limpo.StartsWith('$'))
                valor = Convert.ToInt64(limpo[1..], 16);
            else if (limpo.StartsWith("0b", StringComparison.OrdinalIgnoreCase))
                valor = Convert.ToInt64(limpo[2..], 2);
            else if (limpo.StartsWith('0') && limpo.Length > 1 && limpo.All(char.IsDigit))
                valor = Convert.ToInt64(limpo, 8);
            else if (!long.TryParse(limpo, out valor))
                throw new ErroDeSintaxe($"não entendi o valor '{texto}'", linha);
        }
        catch (Exception e) when (e is FormatException or ArgumentException or OverflowException)
        {
            throw new ErroDeSintaxe($"não entendi o valor '{texto}'", linha);
        }

        return negativo ? -valor : valor;
    }
}
