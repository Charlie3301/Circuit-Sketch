using System.IO;
using System.Text;

namespace Simulador;

/// <summary>
/// O cartão microSD: arquivos que existem só enquanto a simulação roda.
///
/// Era um dos que "desenhavam e não simulavam" — e o exercício que pede
/// cartão SD é sempre o mesmo, o **datalogger**: ler um sensor, gravar
/// numa linha do arquivo e depois abrir o arquivo para conferir. Sem
/// sistema de arquivos, esse exercício não tinha como acontecer.
///
/// Os arquivos vivem em memória e somem ao reiniciar a placa, e isso é
/// dito na tela quando o sketch abre o cartão. É a escolha honesta: um
/// arquivo de verdade no disco do laboratório traria pergunta demais
/// (onde fica? some quando? o aluno seguinte vê o meu?) para o que o
/// exercício precisa mostrar.
///
/// A API imita a biblioteca SD do Arduino, que é o que o aluno escreve:
/// `SD.begin(cs)`, `SD.open(nome, FILE_WRITE)`, `arquivo.println(...)`,
/// `arquivo.close()`, `SD.exists(nome)`, `SD.remove(nome)`.
///
/// Há dois tipos de arquivo. Os de texto são os do exercício, editáveis
/// na janela do cartão. Os binários vêm do computador -- o DOOM1.WAD,
/// uma imagem -- e são lidos byte a byte pelo código nativo (fopen). Um
/// vira o outro quando o outro lado mexe nele: o texto em bytes é o
/// UTF-8 que a placa gravaria, e os bytes em texto são um caractere por
/// byte, como um char[] da placa.
/// </summary>
public sealed class CartaoSd
{
    readonly Dictionary<string, StringBuilder> arquivos = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, Binario> binarios = new(StringComparer.OrdinalIgnoreCase);
    readonly HashSet<string> pastas = new(StringComparer.OrdinalIgnoreCase);
    readonly object trava = new();

    /// <summary>Um arquivo binário: os bytes (com folga no fim) e de onde vieram.</summary>
    public sealed class Binario
    {
        public byte[] Dados = [];
        public int Tamanho;

        /// <summary>O arquivo do computador de onde veio, ou null.</summary>
        public string Origem;

        /// <summary>
        /// Lido do disco sob demanda (o arquivo do computador, sem copiar para
        /// a memoria): o MP3 de 70 MB entra no cartao sem pesar. Null depois
        /// que o sketch escreve nele -- ai ele vira bytes do cartao.
        /// </summary>
        public string Disco;
    }

    /// <summary>
    /// Uma pasta do computador servindo de cartao: o que esta nela aparece no
    /// cartao, com as subpastas, e cada arquivo e lido do disco so quando o
    /// sketch abre -- uma biblioteca de musica de varios GB nao cabe na
    /// memoria. O que o sketch grava, renomeia ou apaga fica no cartao
    /// simulado: a pasta do computador nunca muda.
    /// </summary>
    public string PastaDoComputador { get; set; }

    /// <summary>O que o sketch apagou (ou renomeou) da pasta do computador: some do cartao, e so dele.</summary>
    readonly HashSet<string> apagadosDaPasta = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>O caminho no computador de um nome do cartao, se ele vem da pasta e nao foi apagado. Nunca sai da pasta.</summary>
    string NaPasta(string nome)
    {
        if (string.IsNullOrEmpty(PastaDoComputador)) return null;
        for (string p = nome; p.Length > 0; p = p.Contains('/') ? p[..p.LastIndexOf('/')] : "")
            if (apagadosDaPasta.Contains(p)) return null;
        try
        {
            string raiz = Path.GetFullPath(PastaDoComputador).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string caminho = Path.GetFullPath(Path.Combine(raiz, nome.Replace('/', Path.DirectorySeparatorChar)));
            return caminho.Equals(raiz, StringComparison.OrdinalIgnoreCase)
                   || caminho.StartsWith(raiz + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ? caminho : null;
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { return null; }
    }

    /// <summary>Um arquivo da pasta do computador como Binario do disco (sem ler os bytes).</summary>
    Binario DoDisco(string caminho)
    {
        long tamanho = new FileInfo(caminho).Length;
        return new Binario { Disco = caminho, Tamanho = (int)Math.Min(tamanho, int.MaxValue), Origem = caminho };
    }

    /// <summary>Os bytes de um Binario: os da memoria, ou os do disco.</summary>
    static byte[] BytesDe(Binario b)
    {
        if (b.Disco is null) return b.Dados.AsSpan(0, b.Tamanho).ToArray();
        try { return File.ReadAllBytes(b.Disco); }
        catch (IOException) { return []; }
        catch (UnauthorizedAccessException) { return []; }
    }

    /// <summary>O Binario que mora no disco passa a morar na memoria: o sketch vai escrever nele.</summary>
    static void TrazerParaAMemoria(Binario b)
    {
        if (b.Disco is null) return;
        var bytes = BytesDe(b);
        b.Dados = bytes;
        b.Tamanho = bytes.Length;
        b.Disco = null;
    }

    /// <summary>Um arquivo do computador no cartao, lido do disco quando o sketch abre.</summary>
    public void GravarDoDisco(string nome, string caminho)
    {
        lock (trava)
        {
            nome = Normalizar(nome);
            arquivos.Remove(nome);
            binarios[nome] = DoDisco(caminho);
        }
    }

    /// <summary>O cartão foi inicializado com SD.begin()?</summary>
    public bool Montado { get; private set; }

    public bool Montar()
    {
        Montado = Presente;
        return Montado;
    }

    public IReadOnlyCollection<string> Nomes
    {
        get { lock (trava) return [.. arquivos.Keys, .. binarios.Keys]; }
    }

    public bool Presente { get; set; }

    public void Gravar(string nome, string conteudo)
    {
        lock (trava)
        {
            nome = Normalizar(nome);
            binarios.Remove(nome);
            arquivos[nome] = new StringBuilder(conteudo ?? "");
        }
    }

    /// <summary>Um arquivo binário, como veio -- o WAD do DOOM, por exemplo.</summary>
    public void GravarBinario(string nome, byte[] dados, string origem = null)
    {
        lock (trava)
        {
            nome = Normalizar(nome);
            arquivos.Remove(nome);
            dados ??= [];
            binarios[nome] = new Binario { Dados = dados, Tamanho = dados.Length, Origem = origem };
        }
    }

    public Dictionary<string, string> Copia()
    {
        lock (trava)
            return arquivos.ToDictionary(x => x.Key, x => x.Value.ToString(),
                                         StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Os binários, com o tamanho de cada um -- para a janela mostrar.</summary>
    public List<(string Nome, int Tamanho, string Origem)> Binarios()
    {
        lock (trava)
            return [.. binarios.Select(b => (b.Key, b.Value.Tamanho, b.Value.Origem))];
    }

    /// <summary>
    /// Os arquivos em bytes que sao do cartao, e nao do computador: os que o
    /// sketch gravou (um savegame, uma foto, um log binario). Como num cartao
    /// de verdade, eles ficam depois de desligar -- o projeto guarda.
    /// </summary>
    public Dictionary<string, byte[]> BinariosGravados()
    {
        lock (trava)
            return binarios.Where(b => b.Value.Origem is null && b.Value.Disco is null)
                           .ToDictionary(b => b.Key, b => b.Value.Dados.AsSpan(0, b.Value.Tamanho).ToArray(),
                                         StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Os bytes de um arquivo como a placa gravou (o texto, em UTF-8). Null se nao existe.</summary>
    public byte[] Bytes(string nome)
    {
        lock (trava)
        {
            nome = Normalizar(nome);
            if (binarios.TryGetValue(nome, out var b)) return BytesDe(b);
            if (arquivos.TryGetValue(nome, out var t)) return TextoArduino.ParaBytes(t.ToString());
            if (NaPasta(nome) is { } doPc && File.Exists(doPc)) return BytesDe(DoDisco(doPc));
            return null;
        }
    }

    public bool Existe(string nome)
    {
        lock (trava)
        {
            nome = Normalizar(nome);
            return arquivos.ContainsKey(nome) || binarios.ContainsKey(nome)
                   || NaPasta(nome) is { } doPc && (File.Exists(doPc) || Directory.Exists(doPc));
        }
    }

    public bool Remover(string nome)
    {
        lock (trava)
        {
            nome = Normalizar(nome);
            bool removeu = arquivos.Remove(nome) | binarios.Remove(nome);
            if (NaPasta(nome) is { } doPc && File.Exists(doPc))
            {
                apagadosDaPasta.Add(nome);
                removeu = true;
            }
            return removeu;
        }
    }

    /// <summary>
    /// A placa reiniciou: o cartao e outro chip e continua com os arquivos,
    /// mas o sketch novo precisa do SD.begin() para enxerga-los.
    /// </summary>
    public void Desmontar() => Montado = false;

    public void Limpar()
    {
        lock (trava)
        {
            arquivos.Clear();
            binarios.Clear();
            pastas.Clear();
            apagadosDaPasta.Clear();
            Montado = false;
        }
    }

    /// <summary>
    /// Abre um arquivo. Escrita ACRESCENTA, como no Arduino: quem quiser
    /// começar do zero apaga antes com SD.remove(), e é assim que o
    /// datalogger não perde o que já gravou ao reiniciar o sketch.
    /// </summary>
    public ArquivoSd Abrir(string nome, bool paraEscrever, bool truncar = false)
    {
        lock (trava)
        {
            nome = Normalizar(nome);

            // Binário aberto pela API de texto: vira texto, um caractere por byte.
            if (binarios.Remove(nome, out var bin))
                arquivos[nome] = new StringBuilder(TextoArduino.DeBytes(BytesDe(bin)));

            // Da pasta do computador: entra no cartao (e o que o sketch escrever fica so nele).
            if (!arquivos.ContainsKey(nome) && NaPasta(nome) is { } doPc && File.Exists(doPc))
                arquivos[nome] = new StringBuilder(TextoArduino.DeBytes(BytesDe(DoDisco(doPc))));

            if (!arquivos.TryGetValue(nome, out var conteudo))
            {
                // Leitura de arquivo que não existe devolve nada, e o sketch
                // testa isso com "if (!arquivo)".
                if (!paraEscrever) return null;
                arquivos[nome] = conteudo = new StringBuilder();
            }

            // O "w" da ESP32 começa o arquivo do zero; o FILE_WRITE do Uno
            // escreve no fim.
            if (paraEscrever && truncar) conteudo.Clear();

            return new ArquivoSd(nome, conteudo, paraEscrever);
        }
    }

    /// <summary>
    /// Abre para o código nativo (fopen, open): bytes, com posição. Null
    /// quando não existe e não é para criar; <paramref name="erro"/> diz o
    /// errno.
    /// </summary>
    public FluxoDoCartao AbrirBytes(string nome, bool ler, bool escrever, bool criar, bool truncar,
                                    bool anexar, bool exclusivo, out int erro)
    {
        lock (trava)
        {
            erro = 0;
            nome = Normalizar(nome);
            if (nome.Length == 0 || pastas.Contains(nome))
            {
                erro = 21;   // EISDIR
                return null;
            }

            // Texto aberto como bytes: vira binário, com os bytes que a placa teria.
            if (arquivos.Remove(nome, out var texto))
            {
                var bytes = TextoArduino.ParaBytes(texto.ToString());
                binarios[nome] = new Binario { Dados = bytes, Tamanho = bytes.Length };
            }

            // Da pasta do computador: lido do disco; escrever traz para o cartao.
            if (!binarios.ContainsKey(nome) && NaPasta(nome) is { } doPc)
            {
                if (Directory.Exists(doPc))
                {
                    erro = 21;   // EISDIR
                    return null;
                }
                if (File.Exists(doPc))
                {
                    var doDisco = DoDisco(doPc);
                    if (!escrever)
                    {
                        if (criar && exclusivo)
                        {
                            erro = 17;
                            return null;
                        }
                        return new FluxoDoCartao(this, doDisco, ler, false, false);
                    }
                    binarios[nome] = doDisco;
                }
            }

            if (binarios.TryGetValue(nome, out var bin))
            {
                if (criar && exclusivo)
                {
                    erro = 17;   // EEXIST
                    return null;
                }
                if (escrever) TrazerParaAMemoria(bin);
            }
            else
            {
                if (!criar)
                {
                    erro = 2;   // ENOENT
                    return null;
                }
                bin = new Binario();
                binarios[nome] = bin;
            }

            if (truncar && escrever) bin.Tamanho = 0;
            return new FluxoDoCartao(this, bin, ler, escrever, anexar);
        }
    }

    /// <summary>0 não existe, 1 arquivo, 2 pasta.</summary>
    public int Tipo(string nome, out long tamanho)
    {
        lock (trava)
        {
            tamanho = 0;
            nome = Normalizar(nome);
            if (nome.Length == 0 || pastas.Contains(nome)) return 2;
            if (arquivos.TryGetValue(nome, out var t))
            {
                tamanho = TextoArduino.ParaBytes(t.ToString()).Length;
                return 1;
            }
            if (binarios.TryGetValue(nome, out var b))
            {
                tamanho = b.Tamanho;
                return 1;
            }
            // Pasta que existe porque tem arquivo dentro.
            string prefixo = nome + "/";
            if (arquivos.Keys.Concat(binarios.Keys).Any(k => k.StartsWith(prefixo, StringComparison.OrdinalIgnoreCase)))
                return 2;
            if (NaPasta(nome) is { } doPc)
            {
                if (Directory.Exists(doPc)) return 2;
                if (File.Exists(doPc))
                {
                    tamanho = new FileInfo(doPc).Length;
                    return 1;
                }
            }
            return 0;
        }
    }

    public bool CriarPasta(string nome)
    {
        lock (trava)
        {
            nome = Normalizar(nome);
            if (nome.Length == 0 || arquivos.ContainsKey(nome) || binarios.ContainsKey(nome)) return false;
            if (NaPasta(nome) is { } doPc && (File.Exists(doPc) || Directory.Exists(doPc))) return false;
            return pastas.Add(nome);
        }
    }

    public bool Renomear(string velho, string novo)
    {
        lock (trava)
        {
            velho = Normalizar(velho);
            novo = Normalizar(novo);
            if (arquivos.Remove(velho, out var t))
            {
                binarios.Remove(novo);
                arquivos[novo] = t;
                return true;
            }
            if (binarios.Remove(velho, out var b))
            {
                arquivos.Remove(novo);
                binarios[novo] = b;
                return true;
            }
            if (pastas.Remove(velho))
            {
                pastas.Add(novo);
                return true;
            }
            // Da pasta do computador: o arquivo vai para o cartao com o nome novo.
            if (NaPasta(velho) is { } doPc && File.Exists(doPc))
            {
                arquivos.Remove(novo);
                binarios[novo] = DoDisco(doPc);
                apagadosDaPasta.Add(velho);
                apagadosDaPasta.Remove(novo);
                return true;
            }
            return false;
        }
    }

    public bool RemoverPasta(string nome)
    {
        lock (trava)
        {
            nome = Normalizar(nome);
            bool removeu = pastas.Remove(nome);
            if (NaPasta(nome) is { } doPc && Directory.Exists(doPc))
            {
                apagadosDaPasta.Add(nome);
                removeu = true;
            }
            return removeu;
        }
    }

    /// <summary>O que tem dentro de uma pasta ("" é a raiz): nome e se é pasta.</summary>
    public List<(string Nome, bool Pasta)> Listar(string pasta)
    {
        lock (trava)
        {
            pasta = Normalizar(pasta);
            string prefixo = pasta.Length == 0 ? "" : pasta + "/";
            var vistos = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            foreach (string k in arquivos.Keys.Concat(binarios.Keys).Concat(pastas))
            {
                if (!k.StartsWith(prefixo, StringComparison.OrdinalIgnoreCase) || k.Length == prefixo.Length) continue;
                string resto = k[prefixo.Length..];
                int barra = resto.IndexOf('/');
                if (barra >= 0) vistos[resto[..barra]] = true;
                else if (!vistos.ContainsKey(resto)) vistos[resto] = pastas.Contains(k);
            }
            if ((pasta.Length == 0 ? NaPasta("") ?? (string.IsNullOrEmpty(PastaDoComputador) ? null : PastaDoComputador) : NaPasta(pasta)) is { } doPc
                && Directory.Exists(doPc))
            {
                try
                {
                    foreach (var item in new DirectoryInfo(doPc).EnumerateFileSystemInfos())
                    {
                        if (apagadosDaPasta.Contains(prefixo + item.Name) || vistos.ContainsKey(item.Name)) continue;
                        vistos[item.Name] = (item.Attributes & FileAttributes.Directory) != 0;
                    }
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            return [.. vistos.OrderBy(v => v.Key, StringComparer.OrdinalIgnoreCase).Select(v => (v.Key, v.Value))];
        }
    }

    /// <summary>Barra na frente ou não dá no mesmo, como na biblioteca.</summary>
    static string Normalizar(string nome)
        => (nome ?? "").Replace('\\', '/').Trim().Trim('/');

    /// <summary>Conteúdo inteiro, para o teste conferir de fora.</summary>
    public string Conteudo(string nome)
    {
        lock (trava)
        {
            nome = Normalizar(nome);
            if (arquivos.TryGetValue(nome, out var c)) return c.ToString();
            if (binarios.TryGetValue(nome, out var b)) return TextoArduino.DeBytes(BytesDe(b));
            if (NaPasta(nome) is { } doPc && File.Exists(doPc)) return TextoArduino.DeBytes(BytesDe(DoDisco(doPc)));
            return null;
        }
    }

    internal object Trava => trava;
}

/// <summary>Um arquivo aberto do cartão.</summary>
public sealed class ArquivoSd(string nome, StringBuilder conteudo, bool paraEscrever)
{
    public string Nome { get; } = nome;
    public bool ParaEscrever { get; } = paraEscrever;
    public bool Aberto { get; private set; } = true;

    readonly StringBuilder conteudo = conteudo;
    int leitura;

    public void Escrever(string texto)
    {
        if (Aberto && ParaEscrever) conteudo.Append(texto);
    }

    public void EscreverLinha(string texto)
    {
        if (Aberto && ParaEscrever) conteudo.Append(texto).Append('\n');
    }

    /// <summary>Quantos caracteres faltam ler.</summary>
    public int Disponivel() => Math.Max(0, conteudo.Length - leitura);

    /// <summary>Um caractere, ou -1 no fim -- como o read() da biblioteca.</summary>
    public int Ler() => leitura < conteudo.Length ? conteudo[leitura++] : -1;

    public string LerLinha()
    {
        if (leitura >= conteudo.Length) return "";

        var linha = new StringBuilder();
        while (leitura < conteudo.Length)
        {
            char c = conteudo[leitura++];
            if (c == '\n') break;
            if (c != '\r') linha.Append(c);
        }
        return linha.ToString();
    }

    public int Tamanho() => conteudo.Length;

    public void Fechar() => Aberto = false;
}

/// <summary>
/// Um arquivo do cartão aberto em bytes (o fopen do código nativo). Dois
/// abertos no mesmo arquivo veem o mesmo conteúdo, como no FAT.
/// </summary>
public sealed class FluxoDoCartao(CartaoSd cartao, CartaoSd.Binario arquivo, bool ler, bool escrever, bool anexar) : IDisposable
{
    long posicao;

    /// <summary>O arquivo do computador aberto para ler (o Binario que mora no disco).</summary>
    FileStream disco;

    public bool PodeLer => ler;
    public bool PodeEscrever => escrever;

    public int Ler(Span<byte> destino)
    {
        if (!ler) return -9;   // EBADF
        lock (cartao.Trava)
        {
            long resta = arquivo.Tamanho - posicao;
            if (resta <= 0) return 0;
            int n = (int)Math.Min(resta, destino.Length);
            if (arquivo.Disco is { } caminho)
            {
                try
                {
                    disco ??= new FileStream(caminho, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16);
                    disco.Position = posicao;
                    n = disco.Read(destino[..n]);
                }
                catch (IOException) { return -5; }   // EIO: o arquivo sumiu do computador
                catch (UnauthorizedAccessException) { return -13; }
            }
            else arquivo.Dados.AsSpan((int)posicao, n).CopyTo(destino);
            posicao += n;
            return n;
        }
    }

    public void Dispose()
    {
        lock (cartao.Trava)
        {
            disco?.Dispose();
            disco = null;
        }
    }

    public int Escrever(ReadOnlySpan<byte> origem)
    {
        if (!escrever) return -9;
        lock (cartao.Trava)
        {
            if (anexar) posicao = arquivo.Tamanho;
            long fim = posicao + origem.Length;
            if (fim > int.MaxValue) return -28;   // ENOSPC
            if (fim > arquivo.Dados.Length)
            {
                long novo = Math.Max(fim, Math.Max(4096, (long)arquivo.Dados.Length * 2));
                Array.Resize(ref arquivo.Dados, (int)Math.Min(novo, int.MaxValue));
            }
            if (posicao > arquivo.Tamanho) arquivo.Dados.AsSpan(arquivo.Tamanho, (int)(posicao - arquivo.Tamanho)).Clear();
            origem.CopyTo(arquivo.Dados.AsSpan((int)posicao));
            posicao = fim;
            if (fim > arquivo.Tamanho) arquivo.Tamanho = (int)fim;
            return origem.Length;
        }
    }

    /// <summary>lseek: 0 do começo, 1 de onde está, 2 do fim. Negativo é erro.</summary>
    public long Posicionar(long deslocamento, int deOnde)
    {
        lock (cartao.Trava)
        {
            long alvo = deOnde switch
            {
                1 => posicao + deslocamento,
                2 => arquivo.Tamanho + deslocamento,
                _ => deslocamento,
            };
            if (alvo < 0) return -22;   // EINVAL
            posicao = alvo;
            return posicao;
        }
    }

    public long Tamanho
    {
        get { lock (cartao.Trava) return arquivo.Tamanho; }
    }
}
