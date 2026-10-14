namespace Simulador;

/// <summary>
/// Uma UART da placa (Serial, Serial1, Serial2...) ou uma SoftwareSerial:
/// os bytes saem pelo pino TX no ritmo do baud e chegam no RX de quem esta
/// ligado no fio -- outra placa, ou a propria (o TX no RX, o teste de eco).
/// Baud diferente dos dois lados vira lixo, como na bancada; o buffer de
/// recepcao tem o tamanho do de cada core, e o que nao cabe se perde (e o
/// overflow() da SoftwareSerial diz).
/// </summary>
public sealed class PortaSerial(Maquina dona, string nome, int rx, int tx, int buffer, int fifoDoTx,
                                FluxoDeEntrada entrada = null)
{
    public Maquina Dona { get; } = dona;
    public string Nome { get; } = nome;
    public int Rx { get; set; } = rx;
    public int Tx { get; set; } = tx;
    public long Baud { get; set; }
    public bool Ligada { get; set; }

    /// <summary>A SoftwareSerial do AVR: so uma escuta por vez (listen()).</summary>
    public bool Escutando { get; set; } = true;

    /// <summary>O buffer de recepcao do core (64 no AVR, 256 na ESP32, 32 na Pico...).</summary>
    public int Buffer { get; set; } = buffer;

    /// <summary>A fila do TX: cheia, o write espera o fio.</summary>
    public int FifoDoTx { get; } = fifoDoTx;

    /// <summary>A SoftwareSerial manda bit a bit com o processador: o write espera o byte inteiro.</summary>
    public bool PorSoftware { get; init; }

    public bool Transbordou { get; set; }
    public long TimeoutMs { get; set; } = 1000;

    public FluxoDeEntrada Entrada { get; } = entrada ?? new FluxoDeEntrada();

    readonly Queue<(byte Byte, double ChegaEmUs)> emTransito = new();
    readonly object trava = new();
    double fimDoTxUs;

    /// <summary>Quando o ultimo byte entrou no buffer (no relogio dos fios): o timeout das leituras conta dali.</summary>
    public double UltimaChegadaUs { get; private set; }

    /// <summary>Um byte vindo pelo fio: entra no buffer quando o ultimo bit chegar.</summary>
    public void Receber(byte b, double chegaEmUs)
    {
        lock (trava)
            if (emTransito.Count < 1 << 16) emTransito.Enqueue((b, chegaEmUs));
    }

    /// <summary>O que ja chegou vai para o buffer -- o que nao cabe, se perde.</summary>
    public void Atualizar()
    {
        double agora = Dona.AgoraNoFio;
        lock (trava)
            while (emTransito.Count > 0 && emTransito.Peek().ChegaEmUs <= agora)
            {
                var (b, quando) = emTransito.Dequeue();
                if (!Ligada || !Escutando) continue;
                if (Entrada.Disponivel >= Buffer)
                {
                    Transbordou = true;
                    continue;
                }
                Entrada.Adicionar([b]);
                UltimaChegadaUs = quando;
            }
    }

    /// <summary>Algum byte ainda vindo pelo fio?</summary>
    public bool ChegandoAinda
    {
        get { lock (trava) return emTransito.Count > 0; }
    }

    /// <summary>
    /// Os bytes saem pelo TX, no ritmo do baud: cada um leva 10 bits (8N1).
    /// Chegam em toda porta ligada no fio, com o RX no pino. Com a fila do
    /// TX cheia, quem escreve espera -- e a SoftwareSerial espera tudo.
    /// </summary>
    public void Transmitir(byte[] bytes)
    {
        if (!Ligada || Baud <= 0 || bytes.Length == 0) return;
        double porByte = 10e6 / Baud;
        double agora = Dona.AgoraNoFio;
        double inicio = Math.Max(agora, fimDoTxUs);

        var destinos = Tx >= 0 ? Dona.LigadosAoPino?.Invoke(Tx) : null;
        if (destinos is { Count: > 0 })
            foreach (var (maquina, gpio) in destinos)
                foreach (var porta in maquina.PortasNoPino(gpio))
                {
                    // Baud diferente (mais que ~3%): os bits caem fora do lugar.
                    bool casa = Math.Abs(porta.Baud - Baud) <= Baud * 0.03;
                    for (int i = 0; i < bytes.Length; i++)
                        porta.Receber(casa ? bytes[i] : (byte)(((bytes[i] ^ 0xA5) * 7 + i) & 0xFF), inicio + (i + 1) * porByte);
                }

        fimDoTxUs = inicio + bytes.Length * porByte;
        if (Tx >= 0) Dona.Linha.Serial(Dona.AgoraUs() + (inicio - agora), Tx, bytes, (int)Baud);

        double espera = PorSoftware ? fimDoTxUs - agora : fimDoTxUs - agora - FifoDoTx * porByte;
        if (espera > 0) Dona.EsperarNoChip(espera);
    }

    /// <summary>O flush(): espera o ultimo byte sair pelo fio.</summary>
    public void Esvaziar()
    {
        double falta = fimDoTxUs - Dona.AgoraNoFio;
        if (falta > 0) Dona.EsperarNoChip(falta);
    }

    /// <summary>Quantos bytes ainda cabem na fila do TX (o availableForWrite()).</summary>
    public int CabeNoTx()
    {
        if (Baud <= 0) return FifoDoTx;
        double naFila = Math.Max(0, (fimDoTxUs - Dona.AgoraNoFio) / (10e6 / Baud));
        return Math.Max(0, FifoDoTx - (int)Math.Ceiling(naFila));
    }
}
