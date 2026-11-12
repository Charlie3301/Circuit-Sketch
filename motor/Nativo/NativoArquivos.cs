using System.Globalization;

namespace Simulador;

/// <summary>
/// Os arquivos do modo nativo: o cartao microSD montado com
/// esp_vfs_fat_sdspi_mount (os arquivos da peca, inclusive os que vieram
/// do computador), a flash com as particoes, e as pastas.
/// </summary>
public sealed unsafe partial class ModuloNativo
{
    sealed class Montagem
    {
        public string Ponto;
        public CartaoSd Cartao;
        public bool SoLeitura;
    }

    readonly List<Montagem> montagens = [];

    const int ENOENT = 2, EBADF = 9, EACCES = 13, EEXIST = 17, ENODEV = 19, EISDIR = 21, EINVAL = 22, EROFS = 30;

    internal int Montar(string ponto, int tipo, int host, int cs, string particao)
    {
        if (string.IsNullOrEmpty(ponto) || !ponto.StartsWith('/')) return 0x102;   // ESP_ERR_INVALID_ARG
        ponto = ponto.TrimEnd('/');
        lock (montagens)
            if (montagens.Any(m => m.Ponto.Equals(ponto, StringComparison.OrdinalIgnoreCase)))
                return 0x103;   // ESP_ERR_INVALID_STATE

        CartaoSd cartao;
        if (tipo == 5)
        {
            // O pendrive da USB host: so se ha um ligado nos D-/D+ da placa.
            if (Maquina.PendriveConectado?.Invoke() != true) return 0x107;
            cartao = Maquina.Pendrive;
            cartao.Presente = true;
            cartao.Montar();
        }
        else if (tipo is 3 or 4)
        {
            // Sistema de arquivos na flash: mora na placa e sobrevive ao
            // reinicio. O 4 e o esp_vfs_fat_spiflash_mount_ro: so leitura.
            cartao = Maquina.SistemaDaFlash;
        }
        else
        {
            if (tipo == 1 && Maquina.AparelhosSpi is { } aparelhos)
            {
                var sd = aparelhos().FirstOrDefault(a => a.Tipo == "sd" && a.Cs == cs);
                BarramentoSpi bus;
                lock (barramentos) bus = host is >= 1 and <= 3 ? barramentos[host] : null;
                if (sd is null)
                {
                    Maquina.Avisar(Idioma.T("cartao SD: nenhum modulo microSD tem o CS no GPIO {0}.", cs));
                    return 0x107;   // ESP_ERR_TIMEOUT: o cartao nao respondeu
                }
                if (bus is not null && ((sd.Mosi >= 0 && sd.Mosi != bus.Mosi) || (sd.Sck >= 0 && sd.Sck != bus.Sclk)
                                        || (sd.Miso >= 0 && sd.Miso != bus.Miso)))
                {
                    Maquina.Avisar(Idioma.T("cartao SD: o modulo no CS {0} nao esta ligado no MOSI {1}, MISO {2} e SCK {3} que o codigo usa.", cs, bus.Mosi, bus.Miso, bus.Sclk));
                    return 0x107;
                }
            }
            if (!Maquina.Cartao.Presente) return 0x107;
            cartao = Maquina.Cartao;
            cartao.Montar();
        }

        lock (montagens) montagens.Add(new Montagem { Ponto = ponto, Cartao = cartao, SoLeitura = tipo == 4 });
        return 0;
    }

    internal int Desmontar(string ponto)
    {
        if (ponto is null) return 0x102;
        ponto = ponto.TrimEnd('/');
        lock (montagens)
            return montagens.RemoveAll(m => m.Ponto.Equals(ponto, StringComparison.OrdinalIgnoreCase)) > 0 ? 0 : 0x103;
    }

    bool Resolver(string caminho, out Montagem montagem, out string relativo)
    {
        montagem = null;
        relativo = null;
        if (string.IsNullOrEmpty(caminho)) return false;
        caminho = caminho.Replace('\\', '/');
        lock (montagens)
            foreach (var m in montagens.OrderByDescending(m => m.Ponto.Length))
            {
                if (caminho.Equals(m.Ponto, StringComparison.OrdinalIgnoreCase))
                {
                    montagem = m;
                    relativo = "";
                    return true;
                }
                if (caminho.StartsWith(m.Ponto + "/", StringComparison.OrdinalIgnoreCase))
                {
                    montagem = m;
                    relativo = caminho[(m.Ponto.Length + 1)..];
                    return true;
                }
            }
        return false;
    }

    internal long ArqAbrir(string caminho, int modo)
    {
        if (!Resolver(caminho, out var m, out string nome)) return -ENOENT;
        bool ler = (modo & 1) != 0, escrever = (modo & 2) != 0;
        if (escrever && m.SoLeitura) return -EROFS;
        var fluxo = m.Cartao.AbrirBytes(nome, ler, escrever, (modo & 4) != 0, (modo & 8) != 0, (modo & 16) != 0,
                                        (modo & 32) != 0, out int erro);
        if (fluxo is null) return -(erro == 0 ? ENOENT : erro);
        return Guardar(fluxo);
    }

    internal int ArqLer(long a, IntPtr buf, int n)
    {
        var f = Obj<FluxoDoCartao>(a);
        if (f is null) return -EBADF;
        return f.Ler(new Span<byte>((void*)buf, Math.Max(0, n)));
    }

    internal int ArqEscrever(long a, IntPtr buf, int n)
    {
        var f = Obj<FluxoDoCartao>(a);
        if (f is null) return -EBADF;
        return f.Escrever(new ReadOnlySpan<byte>((void*)buf, Math.Max(0, n)));
    }

    internal long ArqPosicionar(long a, long pos, int deOnde)
    {
        var f = Obj<FluxoDoCartao>(a);
        return f is null ? -EBADF : f.Posicionar(pos, deOnde);
    }

    internal long ArqTamanho(long a) => Obj<FluxoDoCartao>(a)?.Tamanho ?? 0;

    internal void ArqFechar(long a)
    {
        Obj<FluxoDoCartao>(a)?.Dispose();
        Apagar(a);
    }

    internal int ArqInfo(string caminho, out long tamanho)
    {
        tamanho = 0;
        if (!Resolver(caminho, out var m, out string nome)) return 0;
        return m.Cartao.Tipo(nome, out tamanho);
    }

    internal int ArqApagar(string caminho)
    {
        if (!Resolver(caminho, out var m, out string nome)) return -ENOENT;
        if (m.SoLeitura) return -EROFS;
        if (m.Cartao.Remover(nome) || m.Cartao.RemoverPasta(nome)) return 0;
        return -ENOENT;
    }

    internal int ArqRenomear(string velho, string novo)
    {
        if (!Resolver(velho, out var m1, out string a) || !Resolver(novo, out var m2, out string b)) return -ENOENT;
        if (m1 != m2) return -EINVAL;
        if (m1.SoLeitura) return -EROFS;
        return m1.Cartao.Renomear(a, b) ? 0 : -ENOENT;
    }

    internal int ArqCriarPasta(string caminho)
    {
        if (!Resolver(caminho, out var m, out string nome)) return -ENOENT;
        if (m.SoLeitura) return -EROFS;
        if (m.Cartao.Tipo(nome, out _) != 0) return -EEXIST;
        return m.Cartao.CriarPasta(nome) ? 0 : -EEXIST;
    }

    sealed class PastaAberta
    {
        public List<(string Nome, bool Pasta)> Itens;
        public int Proximo;
    }

    internal long PastaAbrir(string caminho)
    {
        if (!Resolver(caminho, out var m, out string nome)) return 0;
        if (m.Cartao.Tipo(nome, out _) != 2) return 0;
        return Guardar(new PastaAberta { Itens = m.Cartao.Listar(nome) });
    }

    internal int PastaLer(long p, out string nome, out bool pasta)
    {
        nome = null;
        pasta = false;
        var d = Obj<PastaAberta>(p);
        if (d is null || d.Proximo >= d.Itens.Count) return 0;
        (nome, pasta) = d.Itens[d.Proximo++];
        return 1;
    }

    internal void PastaFechar(long p) => Apagar(p);

    // =====================================================================
    //  particoes e flash
    // =====================================================================

    sealed record Particao(string Rotulo, int Tipo, int Subtipo, long Endereco, long Tamanho);

    List<Particao> particoes;

    /// <summary>
    /// A tabela de particoes: a do partitions.csv do projeto, quando tem
    /// (como a IDE faz), senao a padrao do core para o tamanho da flash.
    /// </summary>
    List<Particao> Particoes()
    {
        if (particoes is not null) return particoes;
        var lista = new List<Particao>();
        string csv = tabelaDeParticoes;
        if (string.IsNullOrWhiteSpace(csv))
        {
            csv = Maquina.Perfil.FlashTotalBytes >= 8 * 1024 * 1024
                ? "nvs,data,nvs,0x9000,0x5000\notadata,data,ota,0xe000,0x2000\napp0,app,ota_0,0x10000,0x330000\n"
                  + "app1,app,ota_1,0x340000,0x330000\nspiffs,data,spiffs,0x670000,0x180000\ncoredump,data,coredump,0x7F0000,0x10000"
                : "nvs,data,nvs,0x9000,0x5000\notadata,data,ota,0xe000,0x2000\napp0,app,ota_0,0x10000,0x140000\n"
                  + "app1,app,ota_1,0x150000,0x140000\nspiffs,data,spiffs,0x290000,0x160000\ncoredump,data,coredump,0x3F0000,0x10000";
        }

        long proximo = 0x9000;
        foreach (string cru in csv.Replace("\r", "").Split('\n'))
        {
            string linha = cru.Trim();
            if (linha.Length == 0 || linha.StartsWith('#')) continue;
            var c = linha.Split(',').Select(x => x.Trim()).ToArray();
            if (c.Length < 5) continue;
            int tipo = c[1] switch { "app" => 0, "data" => 1, _ => Numero(c[1]) };
            int subtipo = c[2] switch
            {
                "factory" => 0x00, "ota" => 0x00, "phy" => 0x01, "nvs" => 0x02, "coredump" => 0x03, "nvs_keys" => 0x04,
                "efuse" => 0x05, "fat" => 0x81, "spiffs" => 0x82, "littlefs" => 0x83,
                _ when c[2].StartsWith("ota_") => 0x10 + Numero(c[2][4..]),
                _ => Numero(c[2]),
            };
            long endereco = c[3].Length > 0 ? Numero(c[3]) : proximo;
            long tamanho = Tamanho(c[4]);
            lista.Add(new Particao(c[0], tipo, subtipo, endereco, tamanho));
            proximo = endereco + tamanho;
        }
        return particoes = lista;

        static int Numero(string s)
            => s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                ? int.Parse(s[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture)
                : int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : 0;

        static long Tamanho(string s)
        {
            s = s.Trim();
            if (s.EndsWith('K') || s.EndsWith('k')) return Numero(s[..^1]) * 1024L;
            if (s.EndsWith('M') || s.EndsWith('m')) return Numero(s[..^1]) * 1024L * 1024;
            return Numero(s);
        }
    }

    string tabelaDeParticoes;

    internal long ParticaoAchar(int tipo, int subtipo, string rotulo, int indice, out long endereco, out long tamanho, out string nome,
                                out int tipoAchado, out int subtipoAchado)
    {
        endereco = tamanho = 0;
        nome = null;
        tipoAchado = subtipoAchado = 0;
        var achadas = Particoes()
            .Select((p, i) => (p, i))
            .Where(x => (tipo == 0xFF || x.p.Tipo == tipo)
                        && (subtipo == 0xFF || x.p.Subtipo == subtipo)
                        && (rotulo is null || x.p.Rotulo == rotulo))
            .ToList();
        if (indice < 0 || indice >= achadas.Count) return 0;
        var (particao, posicao) = achadas[indice];
        endereco = particao.Endereco;
        tamanho = particao.Tamanho;
        nome = particao.Rotulo;
        tipoAchado = particao.Tipo;
        subtipoAchado = particao.Subtipo;
        return posicao + 1;
    }

    Particao DaTabela(long id)
    {
        var lista = Particoes();
        return id >= 1 && id <= lista.Count ? lista[(int)id - 1] : null;
    }

    internal int ParticaoLer(long id, long desloc, IntPtr buf, long n)
    {
        var p = DaTabela(id);
        if (p is null) return 0x102;
        if (desloc < 0 || desloc + n > p.Tamanho) return 0x104;   // ESP_ERR_INVALID_SIZE
        return FlashLer(p.Endereco + desloc, buf, n);
    }

    internal int ParticaoEscrever(long id, long desloc, IntPtr buf, long n)
    {
        var p = DaTabela(id);
        if (p is null) return 0x102;
        if (desloc < 0 || desloc + n > p.Tamanho) return 0x104;
        var flash = Maquina.MemoriaFlash();
        lock (flash)
        {
            // Como na flash de verdade: gravar so apaga bits (1 -> 0).
            var origem = new ReadOnlySpan<byte>((void*)buf, (int)n);
            long inicio = p.Endereco + desloc;
            for (int i = 0; i < origem.Length && inicio + i < flash.Length; i++) flash[inicio + i] &= origem[i];
        }
        return 0;
    }

    internal int ParticaoApagarFaixa(long id, long desloc, long n)
    {
        var p = DaTabela(id);
        if (p is null) return 0x102;
        if (desloc < 0 || desloc + n > p.Tamanho) return 0x104;
        var flash = Maquina.MemoriaFlash();
        lock (flash)
        {
            long inicio = p.Endereco + desloc;
            long fim = Math.Min(inicio + n, flash.Length);
            if (fim > inicio) flash.AsSpan((int)inicio, (int)(fim - inicio)).Fill(0xFF);
        }
        return 0;
    }

    internal int FlashLer(long endereco, IntPtr buf, long n)
    {
        var destino = new Span<byte>((void*)buf, (int)Math.Max(0, n));
        var flash = Maquina.MemoriaFlash();
        lock (flash)
        {
            destino.Fill(0xFF);
            if (endereco >= 0 && endereco < flash.Length)
            {
                int k = (int)Math.Min(n, flash.Length - endereco);
                flash.AsSpan((int)endereco, k).CopyTo(destino);
            }
        }
        return 0;
    }
}
