using System.Net;
using System.Net.Sockets;

namespace Simulador;

/// <summary>
/// Os nomes da rede: o hostByName (o DNS) e o mDNS ("placa.local").
///
/// Uma placa da bancada e achada pelo nome que ela deu (WiFi.setHostname,
/// MDNS.begin) -- com ou sem o ".local". O resto vai ao DNS de verdade do
/// computador, se a internet estiver ligada (menu Rede).
/// </summary>
public sealed partial class Rede
{
    readonly Dictionary<Maquina, string> nomesMdns = [];

    /// <summary>MDNS.begin("nome"): a placa responde por "nome.local" na bancada.</summary>
    public void AnunciarMdns(Maquina placa, string nome)
    {
        lock (trava) nomesMdns[placa] = (nome ?? "").Trim().ToLowerInvariant();
        Anotar(placa, "mdns", $"responde por {nome}.local");
    }

    public void PararMdns(Maquina placa)
    {
        lock (trava)
        {
            nomesMdns.Remove(placa);
            servicosMdns.Remove(placa);
        }
    }

    readonly Dictionary<Maquina, List<(string Servico, string Protocolo, int Porta)>> servicosMdns = [];

    /// <summary>MDNS.addService("http", "tcp", 80): o servico que as outras placas acham.</summary>
    public void AnunciarServico(Maquina placa, string servico, string protocolo, int porta)
    {
        servico = (servico ?? "").TrimStart('_').ToLowerInvariant();
        protocolo = (protocolo ?? "").TrimStart('_').ToLowerInvariant();
        lock (trava)
        {
            if (!servicosMdns.TryGetValue(placa, out var lista)) servicosMdns[placa] = lista = [];
            lista.RemoveAll(s => s.Servico == servico && s.Protocolo == protocolo);
            lista.Add((servico, protocolo, porta));
        }
        Anotar(placa, "mdns", $"anuncia _{servico}._{protocolo} na porta {porta}");
    }

    /// <summary>MDNS.queryService: as placas da bancada (na rede) que anunciam o servico -- nome, IP e porta.</summary>
    public List<(string Nome, string Ip, int Porta)> BuscarServico(Maquina origem, string servico, string protocolo)
    {
        servico = (servico ?? "").TrimStart('_').ToLowerInvariant();
        protocolo = (protocolo ?? "").TrimStart('_').ToLowerInvariant();
        var achados = new List<(string, string, int)>();
        lock (trava)
            foreach (var (placa, lista) in servicosMdns)
            {
                if (placa == origem || !wifi.TryGetValue(placa, out var estado) || !estado.Ligado) continue;
                foreach (var s in lista)
                    if (s.Servico == servico && s.Protocolo == protocolo)
                        achados.Add((nomesMdns.TryGetValue(placa, out var n) ? n : estado.Hostname, estado.Ip, s.Porta));
            }
        return achados;
    }

    /// <summary>
    /// O endereco que o broker MQTT de fora ganha quando nao da para chegar nele (sem internet, o
    /// nome nao resolve): quem conecta ali cai no broker interno, como a PubSubClient ja cai. E
    /// da faixa de testes de rede (198.18.0.0/15), que nao e de ninguem na internet.
    /// </summary>
    public const string EnderecoDoBrokerInterno = "198.18.0.1";

    /// <summary>
    /// O IP do nome, ou null (o nome nao existe, ou a placa nao esta na rede). Com a porta do MQTT
    /// (o getaddrinfo do umqtt e do esp-mqtt a diz), o broker que nao resolve vira o interno.
    /// </summary>
    public string ResolverNome(Maquina origem, string host, int porta = 0)
    {
        host = (host ?? "").Trim().TrimEnd('.');
        if (host.Length == 0) return null;
        if (IPAddress.TryParse(host, out _)) return host;
        if (EhLocal(host)) return "127.0.0.1";

        string nome = host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) ? host[..^6] : host;
        lock (trava)
        {
            foreach (var (placa, estado) in wifi)
            {
                if (!estado.Ligado) continue;
                bool mesmo = estado.Hostname.Equals(nome, StringComparison.OrdinalIgnoreCase)
                             || (nomesMdns.TryGetValue(placa, out var mdns) && mdns.Equals(nome, StringComparison.OrdinalIgnoreCase));
                if (mesmo) return estado.Ip;
            }
        }
        // O ".local" so existe na rede local (o mDNS): fora da bancada, nao ha.
        if (host.EndsWith(".local", StringComparison.OrdinalIgnoreCase)) return null;
        bool paraOBroker = porta == 1883 && PortaDoBroker > 0;
        if (!UsarInternet)
        {
            // O NTP quem responde e a rede da bancada, com o relogio do computador (como no
            // configTime): o servidor de hora resolve para o roteador, com ou sem internet.
            if (porta == 123) return Gateway;
            if (paraOBroker)
            {
                Anotar(origem, "mqtt", $"{host}: sem internet -- o broker interno do simulador atende no lugar dele");
                return EnderecoDoBrokerInterno;
            }
            Anotar(origem, "dns", $"{host}: sem internet (menu ⋮ > Rede usa a internet do computador)");
            return null;
        }
        try
        {
            return Dns.GetHostAddresses(host).FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)?.ToString();
        }
        catch (SocketException)
        {
            if (paraOBroker)
            {
                Anotar(origem, "mqtt", $"{host}: o nome nao resolveu -- o broker interno do simulador atende no lugar dele");
                return EnderecoDoBrokerInterno;
            }
            Anotar(origem, "dns", $"{host}: o nome nao existe");
            return null;
        }
    }

    /// <summary>Quantas placas estao conectadas no ponto de acesso desta (softAPgetStationNum).</summary>
    public int EstacoesNoAp(Maquina ap)
    {
        var e = Wifi(ap);
        return e.Ligado && e.ModoAp ? ClientesDe(e.Ssid) : 0;
    }
}
