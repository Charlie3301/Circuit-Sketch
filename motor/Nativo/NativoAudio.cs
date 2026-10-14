namespace Simulador;

/// <summary>
/// O som que sai pelo I2S da placa: os quadros em estereo (esquerdo e
/// direito, 16 bits) e os pinos por onde sairam. Quem toca (a janela) le
/// daqui quando o circuito tem um amplificador ligado nesses pinos e um
/// alto-falante nele -- em mono, a media dos dois canais, que e o que o
/// MAX98357A faz de fabrica --, ou um DAC estereo (o PCM5102A) com a saida de
/// fone: aqui os dois canais vao separados.
/// </summary>
public sealed class SaidaDeAudio
{
    readonly short[] anel;     // os quadros intercalados: E, D, E, D...
    int inicio, quantos;       // em quadros
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
        anel = new short[Taxa * 2];   // um segundo de folga
    }

    int Capacidade => anel.Length / 2;

    void Por(short esquerdo, short direito)
    {
        if (quantos == Capacidade)
        {
            inicio = (inicio + 1) % Capacidade;
            quantos--;
        }
        int k = (inicio + quantos) % Capacidade;
        anel[2 * k] = esquerdo;
        anel[2 * k + 1] = direito;
        quantos++;
    }

    /// <summary>Amostras de um canal so: vao iguais para os dois lados.</summary>
    public void Escrever(ReadOnlySpan<short> amostras)
    {
        lock (trava)
            foreach (short s in amostras) Por(s, s);
    }

    /// <summary>Quadros estereo intercalados (E, D, E, D...).</summary>
    public void EscreverEstereo(ReadOnlySpan<short> intercalados)
    {
        lock (trava)
            for (int i = 0; i + 1 < intercalados.Length; i += 2) Por(intercalados[i], intercalados[i + 1]);
    }

    /// <summary>Tira ate destino.Length quadros, em mono (a media dos dois canais). Devolve quantos havia.</summary>
    public int Ler(Span<short> destino)
    {
        lock (trava)
        {
            int n = Math.Min(destino.Length, quantos);
            for (int i = 0; i < n; i++)
            {
                int k = (inicio + i) % Capacidade;
                destino[i] = (short)((anel[2 * k] + anel[2 * k + 1]) / 2);
            }
            inicio = (inicio + n) % Capacidade;
            quantos -= n;
            return n;
        }
    }

    /// <summary>Tira ate destino.Length / 2 quadros, intercalados (E, D...). Devolve quantos quadros.</summary>
    public int LerEstereo(Span<short> destino)
    {
        lock (trava)
        {
            int n = Math.Min(destino.Length / 2, quantos);
            for (int i = 0; i < n; i++)
            {
                int k = (inicio + i) % Capacidade;
                destino[2 * i] = anel[2 * k];
                destino[2 * i + 1] = anel[2 * k + 1];
            }
            inicio = (inicio + n) % Capacidade;
            quantos -= n;
            return n;
        }
    }

    /// <summary>Quantos quadros esperam para tocar.</summary>
    public int Disponiveis
    {
        get { lock (trava) return quantos; }
    }

    public void Limpar()
    {
        lock (trava) inicio = quantos = 0;
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
        // Os dois canais separados (o mono vai igual para os dois lados).
        var estereo = quadros <= 2048 ? stackalloc short[quadros * 2] : new short[quadros * 2];
        int pico = 0;
        for (int i = 0; i < quadros; i++)
            for (int lado = 0; lado < 2; lado++)
            {
                var a = dados.Slice(i * quadro + Math.Min(lado, c.Canais - 1) * bytesPorAmostra, bytesPorAmostra);
                int v = bytesPorAmostra switch
                {
                    1 => (a[0] - 128) << 8,
                    2 => (short)(a[0] | (a[1] << 8)),
                    _ => (short)(a[2] | (a[3] << 8)),
                };
                estereo[2 * i + lado] = (short)v;
                if (Math.Abs(v) > pico) pico = Math.Abs(v);
            }
        c.Saida.EscreverEstereo(estereo);
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
