using System.Net;
using System.Net.Sockets;

namespace Simulador;

/// <summary>Um datagrama UDP que chegou: os bytes e quem mandou.</summary>
public sealed record Datagrama(byte[] Dados, string Ip, int Porta);

/// <summary>
/// Uma porta UDP aberta por uma placa (WiFiUDP.begin, AsyncUDP.listen).
///
/// Os datagramas das outras placas da bancada chegam direto na fila. A porta
/// tambem fica aberta no computador (127.0.0.1, a mesma porta se estiver
/// livre): um programa do PC manda para a placa e recebe a resposta. O que a
/// placa manda para fora da bancada (o pool.ntp.org, um servidor da
/// internet) sai por um UDP de verdade, e a resposta volta para a fila.
/// </summary>
public sealed class PortaUdp
{
    readonly Queue<Datagrama> fila = new();
    readonly object trava = new();

    public Maquina Dona { get; init; }
    public int Porta { get; init; }

    /// <summary>Os grupos de multicast em que a porta entrou (beginMulticast).</summary>
    public HashSet<string> Grupos { get; } = [];

    /// <summary>A porta no computador (127.0.0.1), ou 0.</summary>
    public int PortaNoComputador { get; internal set; }

    internal UdpClient NoComputador;

    /// <summary>O programa do computador que mandou por ultimo (a resposta da placa volta para ele).</summary>
    internal IPEndPoint UltimoDoComputador;
    internal UdpClient ParaFora;
    internal volatile bool Fechada;

    /// <summary>Quantos datagramas cabem na fila (o lwIP da placa descarta o resto).</summary>
    public const int Limite = 64;

    internal void Chegou(Datagrama d)
    {
        lock (trava)
        {
            if (Fechada || fila.Count >= Limite) return;
            fila.Enqueue(d);
        }
    }

    /// <summary>O proximo datagrama, ou null.</summary>
    public Datagrama Proximo()
    {
        lock (trava) return fila.Count > 0 ? fila.Dequeue() : null;
    }

    public int Esperando { get { lock (trava) return fila.Count; } }
}

public sealed partial class Rede
{
    readonly List<PortaUdp> portasUdp = [];

    /// <summary>O endereco de broadcast da bancada (a rede 10.0.0.x).</summary>
    const string BroadcastDaBancada = "10.0.0.255";

    /// <summary>
    /// Abre a porta UDP da placa. Porta 0: uma livre, como o lwIP escolhe.
    /// Null quando a placa ja tem essa porta aberta (o begin repetido).
    /// </summary>
    public PortaUdp AbrirUdp(Maquina dona, int porta)
    {
        lock (trava)
        {
            if (porta <= 0)
            {
                porta = 49152;
                while (portasUdp.Any(p => p.Dona == dona && p.Porta == porta)) porta++;
            }
            if (portasUdp.Any(p => p.Dona == dona && p.Porta == porta && !p.Fechada)) return null;
        }
        var nova = new PortaUdp { Dona = dona, Porta = porta };
        AbrirNoComputador(nova);
        lock (trava) portasUdp.Add(nova);
        Anotar(dona, "udp", nova.PortaNoComputador > 0
            ? $"abriu a porta UDP {porta} (no computador: 127.0.0.1:{nova.PortaNoComputador})"
            : $"abriu a porta UDP {porta}");
        return nova;
    }

    /// <summary>A mesma porta no 127.0.0.1 do computador (outra, se essa estiver ocupada).</summary>
    void AbrirNoComputador(PortaUdp p)
    {
        foreach (int tentativa in new[] { p.Porta, 0 })
        {
            try
            {
                var udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, tentativa));
                p.NoComputador = udp;
                p.PortaNoComputador = ((IPEndPoint)udp.Client.LocalEndPoint!).Port;
                _ = Escutar(p, udp, deFora: false);
                return;
            }
            catch (SocketException) { }
        }
    }

    static async Task Escutar(PortaUdp p, UdpClient udp, bool deFora)
    {
        try
        {
            while (!p.Fechada)
            {
                var r = await udp.ReceiveAsync().ConfigureAwait(false);
                // Do computador, a placa ve o PC como o roteador da rede.
                string ip = deFora ? r.RemoteEndPoint.Address.ToString() : "10.0.0.1";
                p.Chegou(new Datagrama(r.Buffer, ip, r.RemoteEndPoint.Port));
                if (!deFora) p.UltimoDoComputador = r.RemoteEndPoint;
            }
        }
        catch (ObjectDisposedException) { }
        catch (SocketException) { }
    }

    public void FecharUdp(PortaUdp p)
    {
        if (p is null) return;
        p.Fechada = true;
        try { p.NoComputador?.Dispose(); } catch { }
        try { p.ParaFora?.Dispose(); } catch { }
        lock (trava) portasUdp.Remove(p);
    }

    /// <summary>beginMulticast / joinMulticastGroup: o grupo vale para a bancada inteira.</summary>
    public void EntrarNoGrupo(PortaUdp p, string grupo)
    {
        lock (trava) p.Grupos.Add(grupo);
    }

    /// <summary>
    /// Manda um datagrama. Para uma placa da bancada (o IP 10.0.0.x dela), para
    /// todas (o broadcast), para um grupo de multicast, para o computador, ou
    /// para fora (com a internet do computador ligada). Devolve se saiu.
    /// </summary>
    public bool EnviarUdp(PortaUdp de, string host, int porta, byte[] dados)
    {
        var origem = de.Dona;
        if (!NaRede(origem) && !Wifi(origem).ModoAp)
        {
            Anotar(origem, "aviso", $"UDP para {host}:{porta}: a placa não está no WiFi");
            return false;
        }
        string ipDeQuem = Wifi(origem).Ip;
        host = (host ?? "").Trim();

        // O NTP: a rede responde com o relogio do computador, como o configTime.
        if (porta == 123 && dados.Length >= 48 && (dados[0] & 0x07) == 3)
        {
            de.Chegou(new Datagrama(RespostaNtp(dados), IpDoHost(host) ?? host, 123));
            return true;
        }

        bool broadcast = host is "255.255.255.255" or BroadcastDaBancada;
        bool multicast = IPAddress.TryParse(host, out var ipDestino) && ipDestino.AddressFamily == AddressFamily.InterNetwork
                         && ipDestino.GetAddressBytes()[0] is >= 224 and <= 239;
        if (broadcast || multicast || EhIpDaBancada(host))
        {
            List<PortaUdp> alvos;
            lock (trava)
                alvos = [.. portasUdp.Where(p => p != de && p.Porta == porta && !p.Fechada
                                                 && (broadcast ? p.Dona != origem
                                                     : multicast ? p.Grupos.Contains(host)
                                                     : Wifi(p.Dona).Ip == host))];
            foreach (var alvo in alvos) alvo.Chegou(new Datagrama(dados, ipDeQuem, de.Porta));
            // O roteador (10.0.0.1) e o computador: vai para quem mandou de la.
            if (host == Gateway && de.UltimoDoComputador is { } pc)
                try { de.NoComputador?.Send(dados, dados.Length, pc); } catch (SocketException) { }
            return true;
        }

        if (EhLocal(host))
        {
            try
            {
                (de.NoComputador ?? new UdpClient()).Send(dados, dados.Length, new IPEndPoint(IPAddress.Loopback, porta));
                return true;
            }
            catch (SocketException) { return false; }
        }

        if (!UsarInternet)
        {
            Anotar(origem, "udp", $"{host}:{porta}: sem internet (menu ⋮ > Rede usa a internet do computador)");
            return false;
        }
        try
        {
            if (de.ParaFora is null)
            {
                de.ParaFora = new UdpClient(AddressFamily.InterNetwork);
                _ = Escutar(de, de.ParaFora, deFora: true);
            }
            var ip = ipDestino ?? Dns.GetHostAddresses(host).First(a => a.AddressFamily == AddressFamily.InterNetwork);
            de.ParaFora.Send(dados, dados.Length, new IPEndPoint(ip, porta));
            return true;
        }
        catch (Exception ex) when (ex is SocketException or InvalidOperationException)
        {
            Anotar(origem, "udp", $"{host}:{porta}: {Explicar(ex)}");
            return false;
        }
    }

    static string IpDoHost(string host)
    {
        if (IPAddress.TryParse(host, out _)) return host;
        // pool.ntp.org e afins: um endereco fixo, para o remoteIP() ter o que mostrar.
        return "162.159.200.1";
    }

    /// <summary>A resposta de um servidor NTP (versao 4, estrato 2) com a hora do computador.</summary>
    static byte[] RespostaNtp(byte[] pedido)
    {
        var r = new byte[48];
        r[0] = 0x24;            // LI 0, versao 4, modo 4 (servidor)
        r[1] = 2;               // estrato
        r[2] = pedido[2];       // intervalo
        r[3] = 0xEC;            // precisao
        // A hora do pedido (transmit) vira a "originate" da resposta.
        Array.Copy(pedido, 40, r, 24, 8);
        var agora = DateTime.UtcNow;
        EscreverHoraNtp(r, 16, agora.AddSeconds(-30));   // referencia
        EscreverHoraNtp(r, 32, agora);                    // recebido
        EscreverHoraNtp(r, 40, agora);                    // enviado
        return r;
    }

    static void EscreverHoraNtp(byte[] b, int onde, DateTime utc)
    {
        double segundos = (utc - new DateTime(1900, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
        uint inteiro = (uint)Math.Floor(segundos);
        uint fracao = (uint)((segundos - Math.Floor(segundos)) * 4294967296.0);
        b[onde] = (byte)(inteiro >> 24); b[onde + 1] = (byte)(inteiro >> 16); b[onde + 2] = (byte)(inteiro >> 8); b[onde + 3] = (byte)inteiro;
        b[onde + 4] = (byte)(fracao >> 24); b[onde + 5] = (byte)(fracao >> 16); b[onde + 6] = (byte)(fracao >> 8); b[onde + 7] = (byte)fracao;
    }

    void EsquecerUdp(Maquina placa)
    {
        List<PortaUdp> abertas;
        lock (trava) abertas = [.. portasUdp.Where(p => p.Dona == placa)];
        foreach (var p in abertas) FecharUdp(p);
    }
}
