namespace Simulador;

/// <summary>
/// O som que sai pelo I2S da placa: as amostras, ja em mono de 16 bits, e
/// os pinos por onde sairam. Quem toca (a janela) le daqui quando o circuito
/// tem um amplificador ligado nesses pinos e um alto-falante nele.
/// </summary>
public sealed class SaidaDeAudio
{
    readonly short[] anel;
    int inicio, quantas;
    readonly object trava = new();

    public int Taxa { get; }
    public int Bclk { get; }
    public int Ws { get; }
    public int Dout { get; }

    /// <summary>Muda a cada abertura do I2S: quem toca sabe que recomecou.</summary>
    public int Geracao { get; }

    public SaidaDeAudio(int taxa, int bclk, int ws, int dout, int geracao)
    {
        Taxa = Math.Clamp(taxa, 1000, 96000);
        Bclk = bclk;
        Ws = ws;
        Dout = dout;
        Geracao = geracao;
        anel = new short[Taxa];   // um segundo de folga
    }

    public void Escrever(ReadOnlySpan<short> amostras)
    {
        lock (trava)
            foreach (short s in amostras)
            {
                if (quantas == anel.Length)
                {
                    inicio = (inicio + 1) % anel.Length;
                    quantas--;
                }
                anel[(inicio + quantas) % anel.Length] = s;
                quantas++;
            }
    }

    /// <summary>Tira ate destino.Length amostras. Devolve quantas havia.</summary>
    public int Ler(Span<short> destino)
    {
        lock (trava)
        {
            int n = Math.Min(destino.Length, quantas);
            for (int i = 0; i < n; i++) destino[i] = anel[(inicio + i) % anel.Length];
            inicio = (inicio + n) % anel.Length;
            quantas -= n;
            return n;
        }
    }

    public int Disponiveis
    {
        get { lock (trava) return quantas; }
    }

    public void Limpar()
    {
        lock (trava) inicio = quantas = 0;
    }

    /// <summary>O pico das ultimas amostras (0..1), para a peca "pulsar" com o som.</summary>
    public double Nivel { get; set; }
}

public sealed unsafe partial class ModuloNativo
{
    sealed class CanalI2s
    {
        public int Taxa, Bits, Canais;
        public double InicioUs;
        public long Enviadas;
        public SaidaDeAudio Saida;
    }

    static int geracaoDeAudio;

    internal long I2sAbrir(int porta, int taxa, int bits, int canais, int bclk, int ws, int dout)
    {
        var c = new CanalI2s
        {
            Taxa = Math.Clamp(taxa, 1000, 96000),
            Bits = bits is 8 or 16 or 24 or 32 ? bits : 16,
            Canais = canais == 1 ? 1 : 2,
        };
        c.Saida = new SaidaDeAudio(c.Taxa, bclk, ws, dout, Interlocked.Increment(ref geracaoDeAudio));
        Maquina.Audio = c.Saida;
        return Guardar(c);
    }

    internal void I2sFechar(long id)
    {
        var c = Obj<CanalI2s>(id);
        if (c is not null && Maquina.Audio == c.Saida) Maquina.Audio = null;
        Apagar(id);
    }

    /// <summary>
    /// As amostras vao para a saida, e a tarefa espera o tempo que elas
    /// levam para tocar -- o DMA do I2S da placa segura quem escreve do
    /// mesmo jeito. A folga e a dos buffers de DMA (uns 100 ms).
    /// </summary>
    internal int I2sEscrever(long id, IntPtr buf, int n, long espera)
    {
        var c = Obj<CanalI2s>(id);
        if (c is null) return 0;
        int bytesPorAmostra = c.Bits == 8 ? 1 : c.Bits == 16 ? 2 : 4;
        int quadro = bytesPorAmostra * c.Canais;
        int quadros = n / quadro;
        if (quadros <= 0) return n;

        var dados = new ReadOnlySpan<byte>((void*)buf, quadros * quadro);
        var mono = quadros <= 4096 ? stackalloc short[quadros] : new short[quadros];
        int pico = 0;
        for (int i = 0; i < quadros; i++)
        {
            int soma = 0;
            for (int k = 0; k < c.Canais; k++)
            {
                var a = dados.Slice(i * quadro + k * bytesPorAmostra, bytesPorAmostra);
                soma += bytesPorAmostra switch
                {
                    1 => (a[0] - 128) << 8,
                    2 => (short)(a[0] | (a[1] << 8)),
                    _ => (short)(a[2] | (a[3] << 8)),
                };
            }
            int v = soma / c.Canais;
            mono[i] = (short)v;
            if (Math.Abs(v) > pico) pico = Math.Abs(v);
        }
        c.Saida.Escrever(mono);
        c.Saida.Nivel = pico / 32768.0;

        // O relogio do I2S.
        double agora = Micros();
        if (c.Enviadas == 0 || c.InicioUs + c.Enviadas * 1e6 / c.Taxa < agora - 50_000)
        {
            c.InicioUs = agora;
            c.Enviadas = 0;
        }
        c.Enviadas += quadros;
        double tocaAte = c.InicioUs + c.Enviadas * 1e6 / c.Taxa;
        double folga = 100_000;
        if (tocaAte - agora > folga)
        {
            long dormir = (long)(tocaAte - agora - folga);
            if (espera >= 0 && dormir > espera) dormir = espera;
            if (Dormir(dormir) < 0) return -1;
        }
        return quadros * quadro;
    }

    void PararPerifericos()
    {
        PararServidores();
        Maquina.Audio = null;
    }
}
