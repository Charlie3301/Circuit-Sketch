using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Simulador;

/// <summary>Uma mensagem MQTT: tópico e carga, em bytes.</summary>
public sealed record MensagemMqtt(string Topico, byte[] Carga, bool Retida = false)
{
    public string Texto => Encoding.UTF8.GetString(Carga);
}

/// <summary>
/// O "last will" do MQTT: a mensagem que o broker publica quando a placa
/// some sem dizer tchau -- desligou, resetou, perdeu o WiFi. E o jeito
/// classico de um painel mostrar "offline".
/// </summary>
/// <summary>Um tópico que passou pelo broker, como a janela do MQTT mostra.</summary>
public sealed record TopicoMqtt(string Topico, byte[] Ultima, bool Retida, DateTime Quando, int Vezes)
{
    public string Texto => Encoding.UTF8.GetString(Ultima);
}

/// <summary>Uma mensagem de uma placa da simulação num broker de fora (o HiveMQ da aula), para a janela do MQTT.</summary>
public sealed record TrafegoMqtt(string Placa, string Broker, bool Saindo, MensagemMqtt Mensagem);

public sealed record TestamentoMqtt(string Topico, byte[] Carga, bool Retida, int Qos);

/// <summary>
/// O broker respondeu, e disse NAO: usuario ou senha errados, id recusado.
/// Nao e o broker fora do ar -- e por isso que aqui nao tem broker interno
/// para cair: a placa de verdade tambem ficaria sem conectar.
/// </summary>
public sealed class RecusaDoBroker(int codigo, string motivo) : IOException(motivo)
{
    /// <summary>O codigo do CONNACK, que e o que o state() devolve.</summary>
    public int Codigo { get; } = codigo;
}

/// <summary>
/// Ninguem atendeu: o nome nao resolveu, a porta recusou, o tempo acabou.
/// So nesse caso o CircuitSketch troca o broker de fora pelo interno.
/// </summary>
public sealed class BrokerSemResposta(string motivo, Exception causa) : IOException(motivo, causa);

/// <summary>
/// O <c>PubSubClient</c> de uma placa: onde ela está conectada e o que
/// chegou para ela e ainda não passou pelo <c>client.loop()</c>.
///
/// As mensagens esperam aqui, e não disparam o callback na hora, porque
/// na biblioteca de verdade é o <c>loop()</c> que chama o callback -- e é
/// por isso que um sketch sem <c>client.loop()</c> nunca recebe nada.
/// </summary>
public sealed class ClienteMqtt(Maquina dona)
{
    public Maquina Dona { get; } = dona;
    public string Host { get; set; } = "";
    public int Porta { get; set; } = 1883;
    public string Id { get; internal set; } = "";
    public int KeepAlive { get; set; } = 15;

    /// <summary>
    /// O <c>state()</c> do PubSubClient: 0 conectado, -1 desconectado,
    /// -2 a conexão falhou, -3 a conexão caiu, -4 o broker não respondeu.
    /// </summary>
    public int Estado { get; internal set; } = -1;

    public bool Conectado => Estado == 0;

    /// <summary>Conectado no broker que mora dentro do CircuitSketch.</summary>
    public bool Interno { get; internal set; }

    /// <summary>A conexao passa por TLS: o PubSubClient foi montado com um WiFiClientSecure.</summary>
    public bool Tls { get; set; }

    /// <summary>O setInsecure() do WiFiClientSecure: aceita qualquer certificado.</summary>
    public bool SemConferirCertificado { get; set; }

    public string Usuario { get; internal set; }
    public string Senha { get; internal set; }

    /// <summary>O "last will" do connect(), se o sketch passou um.</summary>
    public TestamentoMqtt Testamento { get; set; }

    /// <summary>Quando conectou, para saber se a queda veio logo depois.</summary>
    internal long ConectouEm;

    internal ConexaoMqtt Tcp;

    readonly Queue<MensagemMqtt> chegadas = new();

    internal void Chegou(MensagemMqtt m)
    {
        lock (chegadas) if (chegadas.Count < 1000) chegadas.Enqueue(m);
    }

    public MensagemMqtt Proxima()
    {
        lock (chegadas) return chegadas.Count > 0 ? chegadas.Dequeue() : null;
    }

    internal void EsvaziarChegadas() { lock (chegadas) chegadas.Clear(); }
}

/// <summary>
/// O broker de dentro do CircuitSketch.
///
/// Existe para MQTT funcionar sem internet: duas placas da bancada
/// conversam por ele, e o painel Rede publica nele. É o que acontece no
/// laboratório sem rede, ou quando o broker público da aula não responde.
///
/// Guarda as mensagens retidas e entende os curingas <c>+</c> e <c>#</c>.
/// </summary>
public sealed class BrokerMqtt
{
    readonly object trava = new();
    readonly List<(object Dono, string Filtro, Action<MensagemMqtt> Entregar)> assinaturas = [];
    readonly Dictionary<string, MensagemMqtt> retidas = [];
    readonly Dictionary<string, (byte[] Ultima, DateTime Quando, int Vezes)> topicos = new(StringComparer.Ordinal);

    /// <summary>
    /// Toda publicação que passa pelo broker, com quem mandou (o id do
    /// cliente, a placa, o painel). É o que a janela do MQTT mostra ao vivo.
    /// Chamado fora da trava, na thread de quem publicou.
    /// </summary>
    public event Action<MensagemMqtt, string> Publicou;

    /// <summary>Os tópicos que já passaram pelo broker: a última mensagem, quando, quantas vezes e se há retida.</summary>
    public IReadOnlyList<TopicoMqtt> Topicos()
    {
        lock (trava)
            return [.. topicos.OrderBy(t => t.Key, StringComparer.Ordinal)
                              .Select(t => new TopicoMqtt(t.Key, t.Value.Ultima, retidas.ContainsKey(t.Key), t.Value.Quando, t.Value.Vezes))];
    }

    /// <summary>Quem assina o quê agora (o dono é o cliente: a placa ou a conexão TCP).</summary>
    public IReadOnlyList<(object Dono, string Filtro)> Assinaturas()
    {
        lock (trava) return [.. assinaturas.Select(a => (a.Dono, a.Filtro))];
    }

    /// <summary>Esquece os tópicos vistos (as retidas ficam): o "limpar" da janela.</summary>
    public void EsquecerTopicos()
    {
        lock (trava) topicos.Clear();
    }

    public void Assinar(object dono, string filtro, Action<MensagemMqtt> entregar)
    {
        List<MensagemMqtt> guardadas;
        lock (trava)
        {
            assinaturas.RemoveAll(a => ReferenceEquals(a.Dono, dono) && a.Filtro == filtro);
            assinaturas.Add((dono, filtro, entregar));
            guardadas = [.. retidas.Values.Where(m => Casa(filtro, m.Topico))];
        }
        foreach (var m in guardadas) entregar(m);
    }

    public void Cancelar(object dono, string filtro)
    {
        lock (trava) assinaturas.RemoveAll(a => ReferenceEquals(a.Dono, dono) && a.Filtro == filtro);
    }

    public void RemoverDono(object dono)
    {
        lock (trava) assinaturas.RemoveAll(a => ReferenceEquals(a.Dono, dono));
    }

    /// <summary>
    /// Esquece as mensagens retidas. É o que um projeto novo precisa: a
    /// retida do projeto anterior chegaria ao assinante novo como se
    /// fosse dele.
    /// </summary>
    public void EsquecerRetidas()
    {
        lock (trava) retidas.Clear();
    }

    /// <summary>Quantas retidas o broker guarda agora.</summary>
    public int QuantasRetidas
    {
        get { lock (trava) return retidas.Count; }
    }

    public void Publicar(string topico, byte[] carga, bool retida, string origem = null)
    {
        var mensagem = new MensagemMqtt(topico, carga ?? [], retida);
        List<Action<MensagemMqtt>> alvos;
        lock (trava)
        {
            topicos.TryGetValue(topico, out var antes);
            topicos[topico] = (mensagem.Carga, DateTime.Now, antes.Vezes + 1);
            if (retida)
            {
                // Retida vazia apaga a retida do tópico, como no MQTT.
                if (mensagem.Carga.Length == 0) retidas.Remove(topico);
                else retidas[topico] = mensagem;
            }
            alvos = [.. assinaturas.Where(a => Casa(a.Filtro, topico)).Select(a => a.Entregar)];
        }

        // Quem já estava assinado recebe como mensagem ao vivo, sem a
        // marca de retida -- a marca é para quem assina depois.
        var aoVivo = mensagem with { Retida = false };
        foreach (var entregar in alvos)
        {
            try { entregar(aoVivo); }
            catch { /* um assinante com defeito não derruba os outros */ }
        }

        try { Publicou?.Invoke(mensagem, origem); }
        catch { /* a janela que mostra não derruba o broker */ }
    }

    /// <summary>O tópico casa com o filtro? <c>+</c> vale um nível, <c>#</c> vale o resto.</summary>
    public static bool Casa(string filtro, string topico)
    {
        if (filtro == topico) return true;
        var f = filtro.Split('/');
        var t = topico.Split('/');

        // Tópicos de sistema ($SYS) não casam com curinga no primeiro nível.
        if (topico.StartsWith('$') && (f[0] == "#" || f[0] == "+")) return false;

        for (int i = 0; i < f.Length; i++)
        {
            if (f[i] == "#") return true;
            if (i >= t.Length) return false;
            if (f[i] != "+" && f[i] != t[i]) return false;
        }
        return f.Length == t.Length;
    }
}

/// <summary>
/// Pacotes MQTT 3.1.1. Só o que um PubSubClient e um broker de bancada
/// precisam: conexão, publicação (QoS 0, e QoS 1 recebido), assinatura,
/// ping e desconexão.
/// </summary>
static class PacoteMqtt
{
    public const byte Connect = 0x10, Connack = 0x20, Publish = 0x30, Puback = 0x40,
        Subscribe = 0x82, Suback = 0x90, Unsubscribe = 0xA2, Unsuback = 0xB0,
        Pingreq = 0xC0, Pingresp = 0xD0, Disconnect = 0xE0;

    public static void Escrever(Stream fluxo, byte cabecalho, byte[] corpo)
    {
        var pacote = new List<byte>(corpo.Length + 5) { cabecalho };
        int resto = corpo.Length;
        do
        {
            byte b = (byte)(resto % 128);
            resto /= 128;
            if (resto > 0) b |= 0x80;
            pacote.Add(b);
        } while (resto > 0);
        pacote.AddRange(corpo);
        fluxo.Write([.. pacote]);
        fluxo.Flush();
    }

    /// <summary>Um pacote inteiro, ou null quando a conexão fechou.</summary>
    public static (byte Cabecalho, byte[] Corpo)? Ler(Stream fluxo)
    {
        int cabecalho = fluxo.ReadByte();
        if (cabecalho < 0) return null;

        int tamanho = 0, multiplicador = 1;
        for (int i = 0; i < 4; i++)
        {
            int b = fluxo.ReadByte();
            if (b < 0) return null;
            tamanho += (b & 127) * multiplicador;
            if ((b & 128) == 0) break;
            multiplicador *= 128;
        }
        if (tamanho > 1 << 20) throw new IOException(Idioma.T("pacote MQTT grande demais"));

        var corpo = new byte[tamanho];
        int lidos = 0;
        while (lidos < tamanho)
        {
            int n = fluxo.Read(corpo, lidos, tamanho - lidos);
            if (n <= 0) return null;
            lidos += n;
        }
        return ((byte)cabecalho, corpo);
    }

    public static void Texto(List<byte> destino, string texto)
    {
        var bytes = Encoding.UTF8.GetBytes(texto ?? "");
        Curto(destino, bytes.Length);
        destino.AddRange(bytes);
    }

    public static void Curto(List<byte> destino, int valor)
    {
        destino.Add((byte)(valor >> 8));
        destino.Add((byte)valor);
    }

    public static string LerTexto(byte[] dados, ref int i)
    {
        int n = LerCurto(dados, ref i);
        string texto = Encoding.UTF8.GetString(dados, i, n);
        i += n;
        return texto;
    }

    public static ushort LerCurto(byte[] dados, ref int i)
    {
        ushort v = (ushort)((dados[i] << 8) | dados[i + 1]);
        i += 2;
        return v;
    }

    public static byte[] CorpoConnect(string id, string usuario, string senha, int keepAlive,
                                      TestamentoMqtt testamento = null)
    {
        var b = new List<byte>();
        Texto(b, "MQTT");
        b.Add(4);                                   // MQTT 3.1.1
        byte flags = 0x02;                          // sessão limpa
        if (!string.IsNullOrEmpty(usuario)) flags |= 0x80;
        if (!string.IsNullOrEmpty(senha)) flags |= 0x40;
        if (testamento is not null)
        {
            flags |= 0x04;
            flags |= (byte)((Math.Clamp(testamento.Qos, 0, 2) & 3) << 3);
            if (testamento.Retida) flags |= 0x20;
        }
        b.Add(flags);
        Curto(b, keepAlive);
        Texto(b, id);
        if (testamento is not null)
        {
            Texto(b, testamento.Topico);
            Curto(b, testamento.Carga.Length);
            b.AddRange(testamento.Carga);
        }
        if (!string.IsNullOrEmpty(usuario)) Texto(b, usuario);
        if (!string.IsNullOrEmpty(senha)) Texto(b, senha);
        return [.. b];
    }

    public static byte[] CorpoPublish(string topico, byte[] carga)
    {
        var b = new List<byte>();
        Texto(b, topico);
        b.AddRange(carga);
        return [.. b];
    }

    public static byte[] CorpoSubscribe(ushort id, string filtro)
    {
        var b = new List<byte>();
        Curto(b, id);
        Texto(b, filtro);
        b.Add(0);                                   // QoS 0
        return [.. b];
    }

    public static byte[] CorpoUnsubscribe(ushort id, string filtro)
    {
        var b = new List<byte>();
        Curto(b, id);
        Texto(b, filtro);
        return [.. b];
    }

    public static (MensagemMqtt Mensagem, int Qos, ushort Id) LerPublish(byte cabecalho, byte[] corpo)
    {
        int i = 0;
        string topico = LerTexto(corpo, ref i);
        int qos = (cabecalho >> 1) & 3;
        ushort id = qos > 0 ? LerCurto(corpo, ref i) : (ushort)0;
        return (new MensagemMqtt(topico, corpo[i..], (cabecalho & 1) != 0), qos, id);
    }
}

/// <summary>Conexão MQTT de verdade, por TCP, com um broker fora da bancada.</summary>
sealed class ConexaoMqtt
{
    TcpClient tcp = new();
    Stream fluxo;
    readonly object escrita = new();
    readonly Action<MensagemMqtt> chegou;
    readonly int keepAlive;
    long ultimoEnvio;
    int proximoId;
    volatile bool fechada;

    public event Action Caiu;
    public bool Aberta => !fechada;

    ConexaoMqtt(int keepAlive, Action<MensagemMqtt> chegou)
    {
        this.keepAlive = Math.Max(keepAlive, 5);
        this.chegou = chegou;
    }

    /// <summary>
    /// Conecta no broker, tentando de novo quando ele fica mudo. O broker
    /// publico as vezes aceita o TCP e nunca responde o CONNECT: medido em
    /// 25/09/2026, um terco das tentativas no broker.hivemq.com ficou sem
    /// CONNACK por mais de 10 s, e as outras responderam em 0,4 s. A placa de
    /// verdade desistiria (state -4) e o sketch tentaria de novo no loop();
    /// aqui a tentativa nova e na hora, numa conexao nova -- e o que acha um
    /// servidor do broker que responde. So o tempo que sobra e dividido: com
    /// tres tentativas mudas seguidas, quem chamou cai no broker interno.
    /// </summary>
    public static ConexaoMqtt Conectar(string host, int porta, string id, string usuario, string senha,
        int keepAlive, int tempoMs, Action<MensagemMqtt> chegou, Func<bool> desistir,
        TestamentoMqtt testamento = null, bool tls = false, bool conferirCertificado = true)
    {
        const int PorTentativa = 3000;
        var relogio = Stopwatch.StartNew();
        for (int tentativa = 1; ; tentativa++)
        {
            int resta = tempoMs - (int)relogio.ElapsedMilliseconds;
            try
            {
                return ConectarUmaVez(host, porta, id, usuario, senha, keepAlive, Math.Min(resta, PorTentativa),
                                      chegou, desistir, testamento, tls, conferirCertificado);
            }
            catch (Exception ex) when (ex is IOException or TimeoutException or BrokerSemResposta
                                       && tentativa < 3 && tempoMs - relogio.ElapsedMilliseconds > 1000)
            {
                // Mudo: tenta de novo, numa conexao nova.
            }
        }
    }

    static ConexaoMqtt ConectarUmaVez(string host, int porta, string id, string usuario, string senha,
        int keepAlive, int tempoMs, Action<MensagemMqtt> chegou, Func<bool> desistir,
        TestamentoMqtt testamento, bool tls, bool conferirCertificado)
    {
        var c = new ConexaoMqtt(keepAlive, chegou);
        try
        {
            // Ate aqui, falhar e "ninguem atendeu": o nome nao resolveu, a
            // porta recusou ou o tempo acabou. Dai em diante o broker ja
            // respondeu, e falhar e outra coisa.
            try
            {
                c.tcp.Dispose();
                c.tcp = ConectarRapido(host, porta, tempoMs, desistir);
            }
            catch (ExecucaoInterrompida) { throw; }
            catch (Exception ex) when (ex is SocketException or TimeoutException or IOException)
            {
                throw new BrokerSemResposta(ex.Message, ex);
            }

            Stream bruto = c.tcp.GetStream();
            if (tls)
            {
                // O WiFiClientSecure: o broker na porta 8883, como o HiveMQ
                // Cloud e o EMQX que as aulas usam. Com setInsecure(), a
                // placa aceita qualquer certificado -- e aqui tambem.
                var seguro = new System.Net.Security.SslStream(bruto, false,
                    conferirCertificado ? null : (_, _, _, _) => true);
                Esperar(seguro.AuthenticateAsClientAsync(host), tempoMs, desistir, c.tcp,
                        Idioma.T("negociar o TLS com {0}", host));
                bruto = seguro;
            }

            c.fluxo = bruto;
            c.fluxo.ReadTimeout = tempoMs;

            c.Enviar(PacoteMqtt.Connect, PacoteMqtt.CorpoConnect(id, usuario, senha, keepAlive, testamento));
            var resposta = PacoteMqtt.Ler(c.fluxo) ?? throw new IOException(Idioma.T("o broker fechou a conexão"));
            if ((resposta.Cabecalho & 0xF0) != PacoteMqtt.Connack || resposta.Corpo.Length < 2)
                throw new IOException(Idioma.T("o broker respondeu algo que não é MQTT"));
            if (resposta.Corpo[1] != 0)
                throw new RecusaDoBroker(resposta.Corpo[1], resposta.Corpo[1] switch
                {
                    4 => Idioma.T("o broker recusou o usuário ou a senha"),
                    5 => Idioma.T("o broker exige autorização (usuário e senha)"),
                    2 => Idioma.T("o broker recusou o id do cliente"),
                    3 => Idioma.T("o broker está indisponível"),
                    _ => Idioma.T("o broker recusou a conexão (código {0})", resposta.Corpo[1]),
                });

            c.fluxo.ReadTimeout = Timeout.Infinite;
            new Thread(c.Ler) { IsBackground = true, Name = "mqtt " + host }.Start();
            return c;
        }
        catch
        {
            c.fechada = true;
            try { c.tcp.Close(); } catch { }
            throw;
        }
    }

    /// <summary>
    /// Espera uma tarefa de rede sem travar o botão Parar: a placa parada
    /// no meio de um connect() demorado solta na hora.
    /// </summary>
    internal static void Esperar(Task tarefa, int tempoMs, Func<bool> desistir, IDisposable fechar, string oque)
    {
        var relogio = Stopwatch.StartNew();
        while (true)
        {
            bool pronta;
            try { pronta = tarefa.Wait(40); }
            catch (AggregateException ae) { throw ae.InnerException ?? ae; }
            if (pronta) return;

            if (desistir?.Invoke() == true) { fechar?.Dispose(); throw new ExecucaoInterrompida(); }
            if (relogio.ElapsedMilliseconds > tempoMs)
            {
                fechar?.Dispose();
                throw new TimeoutException(Idioma.T("tempo esgotado ao {0}", oque));
            }
        }
    }

    /// <summary>
    /// Conecta pelo IPv4 primeiro e, se ele demorar, pelo IPv6 junto: fica o
    /// primeiro que atender. O IPv6 espera 1,5 s -- ou nada, se todo IPv4 ja
    /// falhou (a rede so de IPv6 continua funcionando).
    ///
    /// O ConnectAsync(nome) tentava um endereco de cada vez, o IPv6 primeiro.
    /// Na rede deste computador (25/09/2026) o IPv6 do broker.hivemq.com
    /// completa o aperto de mao do TCP e depois nao passa dado nenhum: o
    /// connect gastava o tempo todo nele e o broker parecia mudo, com o IPv4
    /// respondendo o CONNECT na hora. Por isso nao basta correr os dois
    /// juntos: conectar primeiro nao quer dizer que o caminho funciona.
    /// </summary>
    internal static async Task<Socket> ConectarRapidoAsync(string host, int porta, CancellationToken cancelar)
    {
        IPAddress[] enderecos = IPAddress.TryParse(host, out var ip)
            ? [ip]
            : await Dns.GetHostAddressesAsync(host, cancelar).ConfigureAwait(false);
        if (enderecos.Length == 0) throw new SocketException((int)SocketError.HostNotFound);

        using var fim = CancellationTokenSource.CreateLinkedTokenSource(cancelar);

        async Task<Socket> Tentar(IPAddress endereco, Task antes)
        {
            if (antes is not null) await antes.ConfigureAwait(false);
            fim.Token.ThrowIfCancellationRequested();
            var s = new Socket(endereco.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            try
            {
                await s.ConnectAsync(endereco, porta, fim.Token).ConfigureAwait(false);
                return s;
            }
            catch
            {
                s.Dispose();
                throw;
            }
        }

        var doIpv4 = enderecos.Where(e => e.AddressFamily == AddressFamily.InterNetwork)
                              .Select(e => Tentar(e, null)).ToList();
        Task vez = doIpv4.Count == 0
            ? Task.CompletedTask
            : Task.WhenAny(Task.Delay(1500, fim.Token),
                           Task.WhenAll(doIpv4).ContinueWith(_ => { }, TaskScheduler.Default));
        var tentativas = doIpv4.Concat(enderecos.Where(e => e.AddressFamily != AddressFamily.InterNetwork)
                                                .Select(e => Tentar(e, vez))).ToList();

        Exception primeira = null;
        while (tentativas.Count > 0)
        {
            var pronta = await Task.WhenAny(tentativas).ConfigureAwait(false);
            tentativas.Remove(pronta);
            if (pronta.IsCompletedSuccessfully)
            {
                // As outras param; a que conectar mesmo assim vai embora.
                fim.Cancel();
                foreach (var outra in tentativas)
                    _ = outra.ContinueWith(t => { if (t.IsCompletedSuccessfully) t.Result.Dispose(); },
                                           TaskScheduler.Default);
                return pronta.Result;
            }
            if (pronta.Exception?.InnerException is { } erro && erro is not OperationCanceledException)
                primeira ??= erro;
        }
        cancelar.ThrowIfCancellationRequested();
        throw primeira ?? new SocketException((int)SocketError.ConnectionRefused);
    }

    /// <summary>O mesmo, esperando como o <see cref="Esperar"/>: o botao Parar solta, e o tempo acaba.</summary>
    internal static TcpClient ConectarRapido(string host, int porta, int tempoMs, Func<bool> desistir)
    {
        var cancelar = new CancellationTokenSource();
        var tarefa = ConectarRapidoAsync(host, porta, cancelar.Token);
        try
        {
            Esperar(tarefa, tempoMs, desistir, null, Idioma.T("conectar em {0}:{1}", host, porta));
        }
        catch
        {
            cancelar.Cancel();
            _ = tarefa.ContinueWith(t => { if (t.IsCompletedSuccessfully) t.Result.Dispose(); },
                                    TaskScheduler.Default);
            throw;
        }
        return new TcpClient { Client = tarefa.Result };
    }

    void Ler()
    {
        try
        {
            while (!fechada)
            {
                var pacote = PacoteMqtt.Ler(fluxo);
                if (pacote is null) break;
                var (cabecalho, corpo) = pacote.Value;
                if ((cabecalho & 0xF0) != PacoteMqtt.Publish) continue;

                var (mensagem, qos, id) = PacoteMqtt.LerPublish(cabecalho, corpo);
                if (qos == 1) Enviar(PacoteMqtt.Puback, [(byte)(id >> 8), (byte)id]);
                chegou(mensagem);
            }
        }
        catch { }
        finally
        {
            if (!fechada)
            {
                fechada = true;
                try { tcp.Close(); } catch { }
                Caiu?.Invoke();
            }
        }
    }

    public void Publicar(string topico, byte[] carga, bool retida)
        => Enviar((byte)(PacoteMqtt.Publish | (retida ? 1 : 0)), PacoteMqtt.CorpoPublish(topico, carga));

    public void Assinar(string filtro) => Enviar(PacoteMqtt.Subscribe, PacoteMqtt.CorpoSubscribe(NovoId(), filtro));

    public void Cancelar(string filtro) => Enviar(PacoteMqtt.Unsubscribe, PacoteMqtt.CorpoUnsubscribe(NovoId(), filtro));

    ushort NovoId() => (ushort)(Interlocked.Increment(ref proximoId) % 65534 + 1);

    /// <summary>Ping na metade do keep-alive, como o client.loop() faz.</summary>
    public void ManterViva()
    {
        if (fechada) return;
        if (Environment.TickCount64 - Interlocked.Read(ref ultimoEnvio) < keepAlive * 500L) return;
        try { Enviar(PacoteMqtt.Pingreq, []); } catch { }
    }

    /// <summary>
    /// Fecha a conexao. Educado, manda o DISCONNECT, e o broker esquece o
    /// testamento. Sem educacao -- a placa desligou, resetou --, so derruba
    /// o TCP, e o broker publica o testamento, como faria com a placa de
    /// verdade tirada da tomada.
    /// </summary>
    public void Fechar(bool educado = true)
    {
        if (fechada) return;
        if (educado) try { Enviar(PacoteMqtt.Disconnect, []); } catch { }
        fechada = true;
        try { tcp.Close(); } catch { }
    }

    void Enviar(byte cabecalho, byte[] corpo)
    {
        lock (escrita)
        {
            if (fluxo is null || fechada) throw new IOException(Idioma.T("sem conexão com o broker"));
            PacoteMqtt.Escrever(fluxo, cabecalho, corpo);
            Interlocked.Exchange(ref ultimoEnvio, Environment.TickCount64);
        }
    }
}

/// <summary>
/// Um cliente conectado por TCP no broker do computador: um app de MQTT, o
/// celular, uma placa de verdade, ou a própria placa da simulação que
/// conectou em localhost.
/// </summary>
public sealed class ClienteDoServidor
{
    public string Id { get; internal set; } = "";
    public string Endereco { get; init; } = "";
    public string Usuario { get; internal set; }
    public DateTime Desde { get; init; } = DateTime.Now;
    public bool TemTestamento { get; internal set; }

    int enviadas, recebidas;
    public int Enviadas => enviadas;
    public int Recebidas => recebidas;
    internal void ContarEnviada() => Interlocked.Increment(ref enviadas);
    internal void ContarRecebida() => Interlocked.Increment(ref recebidas);

    readonly List<string> filtros = [];
    public IReadOnlyList<string> Filtros { get { lock (filtros) return [.. filtros]; } }
    internal void Assinou(string filtro) { lock (filtros) if (!filtros.Contains(filtro)) filtros.Add(filtro); }
    internal void Cancelou(string filtro) { lock (filtros) filtros.Remove(filtro); }

    internal TcpClient Tcp;
}

/// <summary>
/// O broker do computador: o broker interno aberto numa porta TCP, para um
/// app de MQTT do próprio computador (MQTT Explorer, mosquitto_sub) -- e,
/// com <see cref="NaRede"/>, o celular ou uma placa de verdade na mesma
/// rede -- enxergar e mandar mensagens para as placas da simulação.
///
/// Sem pedir, só em <c>localhost</c>: abrir para a rede faz o Windows
/// perguntar sobre o firewall, e no laboratório cada aluno veria a bancada
/// dos outros. A janela do MQTT abre para a rede quando a pessoa marca.
/// </summary>
public sealed class ServidorMqtt
{
    readonly BrokerMqtt broker;
    readonly TcpListener ouvinte;
    readonly List<ClienteDoServidor> clientes = [];
    volatile bool fechado;

    public int Porta { get; }

    /// <summary>Aceita conexões da rede (todas as placas de rede), e não só de localhost.</summary>
    public bool NaRede { get; }

    /// <summary>Alguém entrou, saiu ou mudou de assinatura.</summary>
    public event Action Mudou;

    public IReadOnlyList<ClienteDoServidor> Clientes()
    {
        lock (clientes) return [.. clientes];
    }

    ServidorMqtt(BrokerMqtt broker, TcpListener ouvinte, bool naRede)
    {
        this.broker = broker;
        this.ouvinte = ouvinte;
        NaRede = naRede;
        Porta = ((IPEndPoint)ouvinte.LocalEndpoint).Port;
    }

    /// <summary>Abre na porta pedida (0 = qualquer livre), ou null se ela estiver ocupada.</summary>
    public static ServidorMqtt Abrir(BrokerMqtt broker, int porta, bool naRede = false)
    {
        var ouvinte = new TcpListener(naRede ? IPAddress.Any : IPAddress.Loopback, porta);
        // Sem isto o Windows deixa um segundo programa abrir a mesma porta
        // em outro endereço, e o app de MQTT cai num broker ou no outro.
        ouvinte.ExclusiveAddressUse = true;
        try { ouvinte.Start(); }
        catch (SocketException) { return null; }

        var servidor = new ServidorMqtt(broker, ouvinte, naRede);
        new Thread(servidor.Aceitar) { IsBackground = true, Name = "broker mqtt" }.Start();
        return servidor;
    }

    void Avisar()
    {
        try { Mudou?.Invoke(); } catch { }
    }

    void Aceitar()
    {
        while (!fechado)
        {
            TcpClient tcp;
            try { tcp = ouvinte.AcceptTcpClient(); }
            catch { return; }
            new Thread(() => Atender(tcp)) { IsBackground = true, Name = "cliente mqtt" }.Start();
        }
    }

    void Atender(TcpClient tcp)
    {
        var cliente = new ClienteDoServidor { Endereco = tcp.Client.RemoteEndPoint?.ToString() ?? "", Tcp = tcp };
        var escrita = new object();
        TestamentoMqtt testamento = null;
        bool educado = false, conectou = false;
        try
        {
            var fluxo = tcp.GetStream();
            void Enviar(byte cabecalho, byte[] corpo)
            {
                lock (escrita) PacoteMqtt.Escrever(fluxo, cabecalho, corpo);
            }

            while (!fechado)
            {
                var pacote = PacoteMqtt.Ler(fluxo);
                if (pacote is null) break;
                var (cabecalho, corpo) = pacote.Value;

                switch (cabecalho & 0xF0)
                {
                    case 0x10:
                    {
                        (cliente.Id, cliente.Usuario, testamento) = LerConnect(corpo);
                        if (string.IsNullOrEmpty(cliente.Id)) cliente.Id = "auto-" + Random.Shared.Next(100000, 999999);
                        cliente.TemTestamento = testamento is not null;

                        // O mesmo id de novo: o broker de verdade derruba a conexão velha.
                        List<ClienteDoServidor> velhos;
                        lock (clientes)
                        {
                            velhos = [.. clientes.Where(c => c.Id == cliente.Id)];
                            clientes.Add(cliente);
                        }
                        foreach (var velho in velhos)
                            try { velho.Tcp?.Close(); } catch { }

                        conectou = true;
                        Enviar(PacoteMqtt.Connack, [0, 0]);
                        Avisar();
                        break;
                    }

                    case 0x30:
                    {
                        var (mensagem, qos, id) = PacoteMqtt.LerPublish(cabecalho, corpo);
                        if (qos == 1) Enviar(PacoteMqtt.Puback, [(byte)(id >> 8), (byte)id]);
                        cliente.ContarEnviada();
                        broker.Publicar(mensagem.Topico, mensagem.Carga, mensagem.Retida, cliente.Id);
                        break;
                    }

                    case 0x80:
                    {
                        int i = 0;
                        ushort id = PacoteMqtt.LerCurto(corpo, ref i);
                        var filtros = new List<string>();
                        while (i < corpo.Length)
                        {
                            filtros.Add(PacoteMqtt.LerTexto(corpo, ref i));
                            i++;                            // QoS pedido: atendemos com 0
                        }

                        // SUBACK antes das retidas: o cliente espera a
                        // confirmação da assinatura antes das mensagens.
                        var resposta = new List<byte> { (byte)(id >> 8), (byte)id };
                        resposta.AddRange(filtros.Select(_ => (byte)0));
                        Enviar(PacoteMqtt.Suback, [.. resposta]);

                        foreach (string filtro in filtros)
                        {
                            cliente.Assinou(filtro);
                            broker.Assinar(cliente, filtro, m =>
                            {
                                try
                                {
                                    Enviar((byte)(PacoteMqtt.Publish | (m.Retida ? 1 : 0)),
                                           PacoteMqtt.CorpoPublish(m.Topico, m.Carga));
                                    cliente.ContarRecebida();
                                }
                                catch { }
                            });
                        }
                        Avisar();
                        break;
                    }

                    case 0xA0:
                    {
                        int i = 0;
                        ushort id = PacoteMqtt.LerCurto(corpo, ref i);
                        while (i < corpo.Length)
                        {
                            string filtro = PacoteMqtt.LerTexto(corpo, ref i);
                            broker.Cancelar(cliente, filtro);
                            cliente.Cancelou(filtro);
                        }
                        Enviar(PacoteMqtt.Unsuback, [(byte)(id >> 8), (byte)id]);
                        Avisar();
                        break;
                    }

                    case 0xC0:
                        Enviar(PacoteMqtt.Pingresp, []);
                        break;

                    case 0xE0:
                        educado = true;
                        return;
                }
            }
        }
        catch { }
        finally
        {
            broker.RemoverDono(cliente);
            lock (clientes) clientes.Remove(cliente);
            try { tcp.Close(); } catch { }

            // Sumiu sem o DISCONNECT (caiu, foi fechado à força): o broker
            // publica o testamento dele, como o mosquitto faz.
            if (conectou && !educado && !fechado && testamento is not null)
                broker.Publicar(testamento.Topico, testamento.Carga, testamento.Retida, cliente.Id);
            if (conectou) Avisar();
        }
    }

    /// <summary>O id, o usuário e o testamento do CONNECT (MQTT 3.1.1).</summary>
    static (string Id, string Usuario, TestamentoMqtt Testamento) LerConnect(byte[] corpo)
    {
        try
        {
            int i = 0;
            PacoteMqtt.LerTexto(corpo, ref i);      // "MQTT"
            i++;                                     // o nível do protocolo
            byte marcas = corpo[i++];
            PacoteMqtt.LerCurto(corpo, ref i);       // o keep alive
            string id = PacoteMqtt.LerTexto(corpo, ref i);

            TestamentoMqtt testamento = null;
            if ((marcas & 0x04) != 0)
            {
                string topico = PacoteMqtt.LerTexto(corpo, ref i);
                int tamanho = PacoteMqtt.LerCurto(corpo, ref i);
                var carga = corpo[i..(i + tamanho)];
                i += tamanho;
                testamento = new TestamentoMqtt(topico, carga, (marcas & 0x20) != 0, (marcas >> 3) & 3);
            }
            string usuario = (marcas & 0x80) != 0 ? PacoteMqtt.LerTexto(corpo, ref i) : null;
            return (id, usuario, testamento);
        }
        catch (Exception)
        {
            return ("", null, null);
        }
    }

    public void Fechar()
    {
        fechado = true;
        try { ouvinte.Stop(); } catch { }
        lock (clientes)
            foreach (var c in clientes)
                try { c.Tcp?.Close(); } catch { }
    }
}
