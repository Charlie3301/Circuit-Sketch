namespace Simulador;

/// <summary>
/// Um aparelho pendurado no barramento I2C.
///
/// Nao e o chip: e o endereco que responde. O que o LCD faz com os bytes
/// continua sendo feito pela biblioteca (lcd.print escreve na tela), do
/// mesmo jeito que o resto do simulador. O que este objeto acrescenta e
/// a unica coisa que o sketch consegue enxergar do barramento: quem
/// responde, quem nao responde, e o que voltou de uma leitura.
/// </summary>
public class DispositivoI2c(int endereco, string nome)
{
    /// <summary>Endereco de 7 bits, como o 0x27 do modulo do LCD.</summary>
    public int Endereco { get; } = endereco & 0x7F;

    public string Nome { get; } = nome;

    /// <summary>
    /// Os GPIOs da placa em que o SDA e o SCL da peca chegam pelo fio (-1:
    /// sem circuito -- o objeto que o sketch declarou no teste de motor).
    /// So responde o barramento que fez begin() nesses pinos.
    /// </summary>
    public int PinoSda { get; init; } = -1;
    public int PinoScl { get; init; } = -1;

    /// <summary>O barramento que o sketch deu ao objeto ("&amp;Wire1"); -1: o do fio.</summary>
    public int Barramento { get; init; } = -1;

    /// <summary>Os bytes da ultima transmissao recebida.</summary>
    public IReadOnlyList<byte> Ultimos => recebidos;
    readonly List<byte> recebidos = [];

    /// <summary>Quantas transmissoes chegaram ate agora.</summary>
    public int Transmissoes { get; private set; }

    public virtual void Receber(byte[] bytes)
    {
        recebidos.Clear();
        recebidos.AddRange(bytes);
        Transmissoes++;
    }

    /// <summary>
    /// O que o aparelho devolve num requestFrom.
    ///
    /// O padrao e 0xFF -- o que um barramento com resistor de pull-up e
    /// nenhum dado devolve de verdade. Quem tiver registro para ler
    /// sobrescreve isto.
    /// </summary>
    public virtual byte[] Responder(int quantos)
    {
        var r = new byte[Math.Max(0, quantos)];
        Array.Fill(r, (byte)0xFF);
        return r;
    }
}

/// <summary>
/// O barramento I2C da placa (o objeto "Wire" do sketch).
///
/// O professor esbarrou nisto de cara: o painel de carro dele comeca com
/// "Wire.begin(21, 22)" e o simulador parava na linha. Todo LCD de
/// modulo, todo MPU6050, todo DS1307 passa por aqui -- sem o Wire, meia
/// duzia de pecas que ja existiam eram inalcancaveis.
///
/// O nivel da simulacao e o mesmo do resto do programa: nao ha START,
/// nem ACK bit a bit, nem clock esticado. Ha endereco, bytes que vao,
/// bytes que voltam e o codigo de retorno do endTransmission -- que e
/// exatamente o que um sketch consegue observar, e o que faz o scanner
/// de I2C (o exercicio classico) dar a resposta certa.
/// </summary>
public class BarramentoI2c
{
    /// <summary>Codigos do endTransmission da biblioteca Wire.</summary>
    public const int Ok = 0;
    public const int SemEspaco = 1;
    public const int SemRespostaNoEndereco = 2;
    public const int SemRespostaNoDado = 3;
    public const int Outro = 4;

    public BarramentoI2c() : this(0, []) { }

    BarramentoI2c(int numero, List<DispositivoI2c> registro)
    {
        Numero = numero;
        aparelhos = registro;
    }

    /// <summary>0: o Wire; 1: o Wire1 (o 2o controlador da ESP32, o i2c1 da Pico, o I2C2 da STM32).</summary>
    public int Numero { get; }

    /// <summary>
    /// O outro controlador da mesma placa. As pecas do circuito sao as
    /// mesmas; cada uma responde so no barramento em que o fio dela chega --
    /// e e isso que deixa dois displays 0x3C numa ESP32 so.
    /// </summary>
    public BarramentoI2c Irmao(int numero) => new(numero, aparelhos);

    /// <summary>
    /// O buffer da Wire do core: 32 bytes no AVR, 128 na ESP32 (o
    /// setBufferSize muda). O write() alem dele devolve 0.
    /// </summary>
    public int TamanhoDoBuffer
    {
        get => tamanhoDoBuffer > 0 ? tamanhoDoBuffer : 32;
        set => tamanhoDoBuffer = value;
    }
    int tamanhoDoBuffer;

    /// <summary>Ninguem disse o tamanho ainda: o interpretador poe o do core.</summary>
    public bool BufferDefinido => tamanhoDoBuffer > 0;

    public bool Ligado { get; private set; }

    /// <summary>Pinos do begin(sda, scl); -1 enquanto usa os padroes da placa.</summary>
    public int Sda { get; private set; } = -1;
    public int Scl { get; private set; } = -1;

    public long Frequencia { get; set; } = 100_000;

    /// <summary>Endereco quando a placa e escrava; -1 quando e mestre.</summary>
    public int EnderecoDeEscravo { get; private set; } = -1;

    readonly List<DispositivoI2c> aparelhos;
    readonly List<byte> saindo = [];
    readonly Queue<byte> entrando = new();
    int destino = -1;

    /// <summary>O endereco do beginTransmission em andamento (-1: nenhum).</summary>
    public int Destino => destino;

    /// <summary>Quem responde neste barramento, em ordem de endereco.</summary>
    public IEnumerable<DispositivoI2c> Aparelhos
    {
        get { lock (aparelhos) return [.. aparelhos.Where(Alcanca).OrderBy(a => a.Endereco)]; }
    }

    /// <summary>
    /// A peca esta no fio deste barramento? A do circuito, so se o SDA e o
    /// SCL dela chegam nos pinos do begin(); a que o sketch declarou sem
    /// peca na tela, no barramento que ele deu ao objeto.
    /// </summary>
    public bool Alcanca(DispositivoI2c a)
        => (a.Barramento < 0 || a.Barramento == Numero)
           && (a.PinoSda < 0 || Sda < 0 || a.PinoSda == Sda)
           && (a.PinoScl < 0 || Scl < 0 || a.PinoScl == Scl);

    public void Begin(int sda = -1, int scl = -1)
    {
        Ligado = true;
        if (sda >= 0) Sda = sda;
        if (scl >= 0) Scl = scl;
    }

    /// <summary>setPins / setSDA / setSCL: os pinos do proximo begin().</summary>
    public void Pinos(int sda, int scl)
    {
        if (sda >= 0) Sda = sda;
        if (scl >= 0) Scl = scl;
    }

    /// <summary>O reset da placa: o barramento volta a como estava antes do setup().</summary>
    public void Zerar()
    {
        End();
        Sda = Scl = -1;
        Frequencia = 100_000;
        EnderecoDeEscravo = -1;
        tamanhoDoBuffer = 0;
    }

    /// <summary>Wire.begin(endereco): a placa entra como escrava.</summary>
    public void BeginEscravo(int endereco)
    {
        Ligado = true;
        EnderecoDeEscravo = endereco & 0x7F;
    }

    public void End()
    {
        Ligado = false;
        saindo.Clear();
        entrando.Clear();
        destino = -1;
    }

    public DispositivoI2c Ligar(DispositivoI2c aparelho)
    {
        // Dois aparelhos no mesmo endereco E no mesmo fio sao um curto de
        // dados: no barramento de verdade os dois respondem juntos e a
        // leitura sai embaralhada. Aqui o primeiro fica -- e quem avisa e a
        // bancada, que sabe o nome das pecas. Em outro fio (o do Wire1), o
        // mesmo endereco e outro aparelho: e para isso que o 2o barramento serve.
        lock (aparelhos)
        {
            var jaEstava = aparelhos.FirstOrDefault(a => a.Endereco == aparelho.Endereco && MesmoFio(a, aparelho));
            if (jaEstava is not null) return jaEstava;
            aparelhos.Add(aparelho);
            return aparelho;
        }
    }

    /// <summary>
    /// Os dois estao no mesmo fio? Pecas do circuito, pelos pinos; o objeto
    /// do sketch (sem pinos), pelo barramento -- e cai na peca do circuito
    /// que ja responde no endereco, como antes.
    /// </summary>
    static bool MesmoFio(DispositivoI2c a, DispositivoI2c b)
        => a.PinoSda >= 0 && b.PinoSda >= 0
            ? a.PinoSda == b.PinoSda && a.PinoScl == b.PinoScl
            : a.Barramento < 0 || b.Barramento < 0 || a.Barramento == b.Barramento;

    public DispositivoI2c Ligar(int endereco, string nome)
        => Ligar(new DispositivoI2c(endereco, nome));

    public void Desligar(int endereco)
    {
        lock (aparelhos) aparelhos.RemoveAll(a => a.Endereco == (endereco & 0x7F));
    }

    /// <summary>Tira um aparelho so (o do objeto que mudou de endereco ou de barramento).</summary>
    public void Desligar(DispositivoI2c aparelho)
    {
        lock (aparelhos) aparelhos.Remove(aparelho);
    }

    public void DesligarTodos()
    {
        lock (aparelhos) aparelhos.Clear();
    }

    public bool Existe(int endereco) => Achar(endereco) is not null;

    /// <summary>Quem responde neste endereco, neste barramento.</summary>
    public DispositivoI2c Achar(int endereco)
    {
        lock (aparelhos) return aparelhos.FirstOrDefault(a => a.Endereco == (endereco & 0x7F) && Alcanca(a));
    }

    /// <summary>
    /// A peca com este endereco que esta em OUTRO fio: o LCD nos pinos 25/26
    /// com o Wire.begin() nos 21/22. E so para o aviso -- ela nao responde.
    /// </summary>
    public DispositivoI2c ForaDoFio(int endereco)
    {
        lock (aparelhos) return aparelhos.FirstOrDefault(a => a.Endereco == (endereco & 0x7F) && !Alcanca(a));
    }

    public void ComecarTransmissao(int endereco)
    {
        destino = endereco & 0x7F;
        saindo.Clear();
    }

    /// <summary>Devolve quantos bytes entraram -- 1, ou 0 se o buffer encheu.</summary>
    public int Escrever(byte b)
    {
        if (saindo.Count >= TamanhoDoBuffer) return 0;
        saindo.Add(b);
        return 1;
    }

    public int Escrever(IEnumerable<byte> bytes)
    {
        int n = 0;
        foreach (byte b in bytes) n += Escrever(b);
        return n;
    }

    public int TerminarTransmissao()
    {
        var bytes = saindo.ToArray();
        int alvo = destino;
        saindo.Clear();
        destino = -1;

        if (!Ligado) return Outro;
        if (alvo < 0) return Outro;
        if (bytes.Length > TamanhoDoBuffer) return SemEspaco;

        var aparelho = Achar(alvo);
        if (aparelho is null) return SemRespostaNoEndereco;

        aparelho.Receber(bytes);
        return Ok;
    }

    /// <summary>requestFrom: devolve quantos bytes vieram de verdade.</summary>
    public int Pedir(int endereco, int quantos)
    {
        entrando.Clear();
        if (!Ligado || quantos <= 0) return 0;

        var aparelho = Achar(endereco);
        if (aparelho is null) return 0;

        foreach (byte b in aparelho.Responder(Math.Min(quantos, TamanhoDoBuffer)))
            entrando.Enqueue(b);
        return entrando.Count;
    }

    public int Disponivel() => entrando.Count;

    /// <summary>Ler sem nada na fila devolve -1, como a biblioteca.</summary>
    public int Ler() => entrando.Count > 0 ? entrando.Dequeue() : -1;

    public int Espiar() => entrando.Count > 0 ? entrando.Peek() : -1;
}
