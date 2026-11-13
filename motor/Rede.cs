using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;

namespace Simulador;

/// <summary>Uma linha do painel Rede: quem, que tipo de coisa, e o quê.</summary>
public sealed record EventoDeRede(long Numero, DateTime Hora, string Placa, string Tipo, string Texto);

/// <summary>O WiFi de uma placa.</summary>
public sealed class EstadoWifi
{
    public string Ssid { get; internal set; } = "";
    public string Ip { get; internal set; } = "0.0.0.0";
    public string Mac { get; internal set; } = "";
    public bool Ligado { get; internal set; }
    public bool ModoAp { get; internal set; }

    /// <summary>O cabo de rede (o ETH da placa): ligado na bancada, com o mesmo IP da placa.</summary>
    public bool Cabo { get; internal set; }

    /// <summary>O nome da placa na rede (WiFi.setHostname); vazio, o de fabrica.</summary>
    public string Hostname { get; internal set; } = "";
    public int Falha { get; internal set; }
    internal long ConectaEm;
    internal bool Anunciado;
}

public sealed record PontoDeAcesso(string Ssid, string Senha)
{
    public bool Aberta => string.IsNullOrEmpty(Senha);
}

/// <summary>A resposta de um HTTPClient: código, corpo e cabeçalhos.</summary>
public sealed record RespostaHttp(int Codigo, string Corpo, Dictionary<string, string> Cabecalhos)
{
    public static RespostaHttp Erro(int codigo)
        => new(codigo, "", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
}

/// <summary>
/// A rede da bancada: WiFi, HTTP, TCP, MQTT e Bluetooth de todas as placas.
///
/// Uma só para a simulação inteira, e não uma por placa, porque rede é
/// justamente o que liga uma placa à outra: a ESP32 que serve uma página
/// e a que lê essa página precisam estar na mesma rede.
///
/// Três destinos, nesta ordem:
///
/// 1. **Outra placa da bancada**, pelo IP que ela ganhou no WiFi
///    (10.0.0.x). Não sai do programa.
/// 2. **O próprio computador** (<c>localhost</c>). Também não precisa de
///    internet.
/// 3. **A internet**, pela conexão do computador, quando
///    <see cref="UsarInternet"/> está ligado. Desligado, o pedido falha
///    como falharia uma placa sem rede -- e o painel diz por quê.
///
/// O MQTT tem ainda um broker interno: se o broker da aula não
/// responde (laboratório sem internet), a placa conecta nele e as placas
/// da bancada continuam conversando entre si.
/// </summary>
public sealed partial class Rede
{
    /// <summary>HTTP, TCP e MQTT podem sair pela internet do computador.</summary>
    public bool UsarInternet { get; set; } = true;

    readonly object trava = new();

    // =====================================================================
    //  Registro: o que aparece no painel Rede
    // =====================================================================

    readonly List<EventoDeRede> eventos = [];
    long numero;

    public void Anotar(Maquina placa, string tipo, string texto)
    {
        lock (trava)
        {
            eventos.Add(new EventoDeRede(++numero, DateTime.Now, placa?.Nome ?? "CircuitSketch", tipo, texto));
            if (eventos.Count > 600) eventos.RemoveRange(0, eventos.Count - 600);
        }
    }

    /// <summary>O que entrou no registro depois do número <paramref name="visto"/>.</summary>
    public List<EventoDeRede> Novos(ref long visto)
    {
        lock (trava)
        {
            long limite = visto;
            var novos = eventos.Where(e => e.Numero > limite).ToList();
            if (novos.Count > 0) visto = novos[^1].Numero;
            return novos;
        }
    }

    static string Curto(string texto, int maximo = 90)
    {
        texto = (texto ?? "").Replace("\r", "").Replace('\n', ' ');
        return texto.Length <= maximo ? texto : texto[..maximo] + "…";
    }

    static string Curto(byte[] carga) => Curto(TextoArduino.Corrigir(TextoArduino.DeBytes(carga)));

    // =====================================================================
    //  WiFi
    // =====================================================================

    /// <summary>O roteador de mentira. As placas ganham 10.0.0.2, .3, .4...</summary>
    public const string Gateway = "10.0.0.1";

    /// <summary>
    /// Quanto o WiFi demora para conectar, em tempo da placa. Um segundo
    /// é o bastante para o clássico "Conectando....." imprimir uns pontos,
    /// sem fazer ninguém esperar.
    /// </summary>
    public const int TempoParaConectar = 1000;

    readonly List<Maquina> ordemWifi = [];
    readonly Dictionary<Maquina, EstadoWifi> wifi = [];
    readonly List<PontoDeAcesso> pontos = [];

    public IReadOnlyList<PontoDeAcesso> PontosDeAcesso { get { lock (trava) return [.. pontos]; } }

    public void DefinirPontosDeAcesso(IEnumerable<PontoDeAcesso> novos)
    {
        var lista = (novos ?? [])
            .Where(p => !string.IsNullOrWhiteSpace(p.Ssid))
            .DistinctBy(p => p.Ssid, StringComparer.Ordinal)
            .ToList();

        lock (trava)
        {
            if (pontos.SequenceEqual(lista)) return;
            pontos.Clear();
            pontos.AddRange(lista);
        }
    }

    static readonly string[] RedesDaCasa = ["CircuitSketch-WiFi", "Wokwi-GUEST"];

    /// <summary>
    /// Os dois bytes do meio do MAC: fixos neste computador, e diferentes de
    /// um computador para o outro.
    ///
    /// O MAC de verdade e unico por placa, e e dele que os sketches tiram o
    /// ID do cliente MQTT. Com o mesmo MAC em toda a sala, trinta alunos no
    /// broker publico viravam o MESMO cliente -- e o broker derruba o mais
    /// antigo a cada conexao nova, um derrubando o outro sem parar.
    /// </summary>
    public static readonly string MeioDoMac = CalcularMeioDoMac();

    static string CalcularMeioDoMac()
    {
        byte[] resumo = System.Security.Cryptography.SHA256.HashData(
            Encoding.UTF8.GetBytes(Environment.MachineName + "|" + Environment.UserName));
        return $"{resumo[0]:X2}:{resumo[1]:X2}";
    }

    PontoDeAcesso Procurar(string ssid)
    {
        lock (trava)
        {
            var ponto = pontos.FirstOrDefault(p => p.Ssid == ssid);
            if (ponto is not null) return ponto;

            if (RedesDaCasa.Contains(ssid)) return new PontoDeAcesso(ssid, "");

            var ap = wifi.Values.FirstOrDefault(e => e.Ligado && e.ModoAp && e.Ssid == ssid);
            return ap is null ? null : new PontoDeAcesso(ssid, "");
        }
    }

    bool ExigeRedeDeVerdade { get { lock (trava) return pontos.Count > 0; } }

    public EstadoWifi Wifi(Maquina placa)
    {
        lock (trava)
        {
            if (!wifi.TryGetValue(placa, out var estado))
            {
                ordemWifi.Add(placa);
                int n = ordemWifi.Count + 1;
                estado = new EstadoWifi { Ip = $"10.0.0.{n}", Mac = $"24:0A:C4:{MeioDoMac}:{n:X2}" };
                wifi[placa] = estado;
            }
            return estado;
        }
    }

    public void ConectarWifi(Maquina placa, string ssid, string senha = null)
    {
        var estado = Wifi(placa);
        estado.Ssid = string.IsNullOrWhiteSpace(ssid) ? "CircuitSketch-WiFi" : ssid;
        estado.Ligado = true;
        estado.ModoAp = false;
        estado.ConectaEm = placa.Ms + TempoParaConectar;
        estado.Anunciado = false;
        estado.Falha = 0;

        var ponto = Procurar(estado.Ssid);

        if (ponto is null)
        {
            if (!ExigeRedeDeVerdade) return;

            estado.Falha = 1;
            Anotar(placa, "wifi", Idioma.T("nao achou a rede \"{0}\"", estado.Ssid));
            Avisar(placa,
                Idioma.T("WiFi.begin(\"{0}\"): nao ha rede com esse nome no circuito. Confira o SSID do ponto de acesso, ou tire a peca do circuito para voltar a conectar em qualquer nome.", estado.Ssid));
            return;
        }

        if (!ponto.Aberta && (senha ?? "") != ponto.Senha)
        {
            estado.Falha = 4;
            Anotar(placa, "wifi", Idioma.T("senha errada em \"{0}\"", estado.Ssid));
            Avisar(placa,
                Idioma.T("WiFi.begin(\"{0}\"): a senha nao confere com a do ponto de acesso.", estado.Ssid));
        }
    }

    static void Avisar(Maquina placa, string texto)
    {
        if (placa is null) return;
        lock (placa.Trava)
            if (!placa.Avisos.Contains(texto)) placa.Avisos.Add(texto);
    }

    /// <summary>
    /// <c>WiFi.softAP()</c>: a própria placa vira a rede. Fica pronta na
    /// hora, e as outras placas a enxergam pelo mesmo 10.0.0.x.
    /// </summary>
    public void AbrirPontoDeAcesso(Maquina placa, string ssid)
    {
        var estado = Wifi(placa);
        estado.Ssid = string.IsNullOrWhiteSpace(ssid) ? "ESP32-AP" : ssid;
        estado.Ligado = true;
        estado.ModoAp = true;
        estado.ConectaEm = placa.Ms;
        estado.Anunciado = true;
        Anotar(placa, "wifi", Idioma.T("abriu a rede \"{0}\" (ponto de acesso) · IP {1}", estado.Ssid, estado.Ip));
    }

    /// <summary>Quantas placas estao conectadas agora na rede deste nome (as luzes do roteador).</summary>
    public int ClientesDe(string ssid)
    {
        if (string.IsNullOrWhiteSpace(ssid)) return 0;
        lock (trava)
            return wifi.Count(par => par.Value is { Ligado: true, Falha: 0, ModoAp: false } e
                                     && e.Ssid == ssid.Trim() && par.Key.Ms >= e.ConectaEm);
    }

    public bool WifiConectado(Maquina placa)
    {
        var estado = Wifi(placa);
        if (!estado.Ligado || estado.Falha != 0 || placa.Ms < estado.ConectaEm) return false;
        if (!estado.Anunciado)
        {
            estado.Anunciado = true;
            Anotar(placa, "wifi", Idioma.T("conectou em \"{0}\" · IP {1}", estado.Ssid, estado.Ip));
        }
        return true;
    }

    /// <summary>
    /// O ETH.begin(): o cabo da placa no switch da bancada. A placa fica na
    /// rede (o IP dela, o TCP, o UDP, o HTTP) sem o WiFi estar conectado.
    /// </summary>
    public void LigarCabo(Maquina placa)
    {
        var estado = Wifi(placa);
        if (estado.Cabo) return;
        estado.Cabo = true;
        Anotar(placa, "eth", Idioma.T("cabo de rede ligado · IP {0}", estado.Ip));
    }

    public void DesligarCabo(Maquina placa)
    {
        var estado = Wifi(placa);
        if (!estado.Cabo) return;
        estado.Cabo = false;
        Anotar(placa, "eth", Idioma.T("cabo de rede desligado"));
    }

    public bool CaboConectado(Maquina placa) => Wifi(placa).Cabo;

    /// <summary>A placa esta na rede da bancada: pelo WiFi ou pelo cabo.</summary>
    public bool NaRede(Maquina placa) => CaboConectado(placa) || WifiConectado(placa);

    public void DesligarWifi(Maquina placa)
    {
        var estado = Wifi(placa);
        if (estado.Ligado && estado.Anunciado) Anotar(placa, "wifi", Idioma.T("desconectou do WiFi"));
        estado.Ligado = false;
    }

    /// <summary>
    /// De onde vieram as redes da ultima varredura.
    ///
    /// Null quando vieram do radio do computador; quando vieram das
    /// inventadas, traz o MOTIVO -- e a janela poe esse motivo na tela.
    /// O professor pediu exatamente isso: o aviso nao pode sair no
    /// monitor serial, que e do sketch, e sim na interface.
    /// </summary>
    public string MotivoDaSimulacao { get; private set; }

    /// <summary>
    /// Redes que o <c>WiFi.scanNetworks()</c> encontra.
    ///
    /// Comeca pelo radio DO COMPUTADOR: o professor rodou um scan, achou
    /// so "CircuitSketch-WiFi" e "Wokwi-GUEST" e perguntou por que o
    /// simulador nao parte do WiFi da maquina. Agora parte -- e quando
    /// nao da, diz por que e cai nas de mentira, em vez de fingir.
    ///
    /// As pontos de acesso do proprio circuito entram sempre: eles sao
    /// parte do exercicio, e nao do ambiente.
    /// </summary>
    public List<(string Ssid, int Rssi)> RedesVisiveis()
    {
        var redes = new List<(string, int)>();

        lock (trava)
        {
            foreach (var ponto in pontos) redes.Add((ponto.Ssid, -42));
            foreach (var estado in wifi.Values.Where(e => e.Ligado && e.ModoAp))
                redes.Add((estado.Ssid, -40));
        }

        var doComputador = RadioDoComputador.Redes();
        MotivoDaSimulacao = doComputador.Count > 0 ? null : RadioDoComputador.MotivoDoWifi;

        foreach (var vista in doComputador) redes.Add((vista.Ssid, vista.Rssi));

        // A rede da casa entra sempre: e nela que os sketches da turma
        // conectam, e tirar isso quebraria todo exercicio ja escrito.
        redes.Add(("CircuitSketch-WiFi", -48));

        // "Wokwi-GUEST" so aparece quando nao ha radio de verdade. Com
        // redes reais na lista, ela so confundiria -- foi ela que fez o
        // professor perguntar "por que ele acha wokwi?".
        if (doComputador.Count == 0) redes.Add(("Wokwi-GUEST", -61));

        return [.. redes.DistinctBy(r => r.Item1)];
    }

    /// <summary>
    /// Os aparelhos de Bluetooth que o computador conhece.
    ///
    /// Mesma regra do WiFi: o que a placa simulada "ve" e o que a
    /// maquina ve. Ela nao tem radio proprio, e a interface diz isso.
    /// </summary>
    public List<string> BluetoothsDoComputador() => RadioDoComputador.Bluetooths();

    /// <summary>Por que o Bluetooth esta sendo simulado, ou null.</summary>
    public string MotivoDoBluetooth => RadioDoComputador.MotivoDoBluetooth;

    bool EhIpDaBancada(string host)
    {
        lock (trava) return host == Gateway || wifi.Values.Any(e => e.Ip == host);
    }

    static bool EhLocal(string host)
        => host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
           || (IPAddress.TryParse(host, out var ip) && IPAddress.IsLoopback(ip));

    // =====================================================================
    //  Servidores (WebServer e WiFiServer)
    // =====================================================================

    readonly List<ServidorWeb> servidores = [];

    public IReadOnlyList<ServidorWeb> Servidores { get { lock (trava) return [.. servidores]; } }

    public ServidorWeb AbrirServidor(Maquina dona, int porta, bool cru, bool tcpDeVerdade = false)
    {
        porta = porta <= 0 ? 80 : porta;
        foreach (var antigo in Servidores.Where(s => s.Dona == dona && s.Porta == porta))
            FecharServidor(antigo);

        var servidor = new ServidorWeb(this, dona, porta, cru) { TcpDeVerdade = cru && tcpDeVerdade };
        lock (trava) servidores.Add(servidor);
        servidor.AbrirPonte();

        Anotar(dona, "servidor", servidor.UrlLocal is null
            ? Idioma.T("servidor na porta {0}; as portas 8080 a 8099 do computador estão ocupadas, então só as placas da bancada o alcançam", porta)
            : Idioma.T("servidor na porta {0} · abra no navegador: {1}", porta, servidor.UrlLocal));

        if (!Wifi(dona).Ligado)
            Anotar(dona, "aviso", Idioma.T("o servidor abriu, mas a placa ainda não chamou WiFi.begin()"));
        return servidor;
    }

    public void FecharServidor(ServidorWeb servidor)
    {
        lock (trava) servidores.Remove(servidor);
        servidor.Fechar();
    }

    ServidorWeb ServidorNoEndereco(string host, int porta)
    {
        lock (trava)
        {
            var dona = wifi.FirstOrDefault(par => par.Value.Ip == host && par.Value.Ligado).Key;
            return dona is null ? null : servidores.FirstOrDefault(s => s.Dona == dona && s.Porta == porta);
        }
    }

    // =====================================================================
    //  HTTP (HTTPClient)
    // =====================================================================

    /// <summary>Um HTTPClient só, para o programa inteiro: é o jeito recomendado no .NET.</summary>
    static readonly HttpClient Cliente = new(new SocketsHttpHandler
    {
        ConnectTimeout = TimeSpan.FromSeconds(5),
        AllowAutoRedirect = false,
        ConnectCallback = ConectarParaHttp,
    })
    { Timeout = Timeout.InfiniteTimeSpan };

    /// <summary>Para o <c>setInsecure()</c>: aceita qualquer certificado.</summary>
    static readonly HttpClient ClienteInseguro = new(new SocketsHttpHandler
    {
        ConnectTimeout = TimeSpan.FromSeconds(5),
        AllowAutoRedirect = false,
        SslOptions = { RemoteCertificateValidationCallback = (_, _, _, _) => true },
        ConnectCallback = ConectarParaHttp,
    })
    { Timeout = Timeout.InfiniteTimeSpan };

    /// <summary>O IPv4 e o IPv6 juntos, tambem no HTTP (ver ConexaoMqtt.ConectarRapidoAsync).</summary>
    static async ValueTask<Stream> ConectarParaHttp(SocketsHttpConnectionContext contexto, CancellationToken cancelar)
        => new NetworkStream(await ConexaoMqtt.ConectarRapidoAsync(
               contexto.DnsEndPoint.Host, contexto.DnsEndPoint.Port, cancelar).ConfigureAwait(false), ownsSocket: true);

    /// <summary>Códigos negativos do HTTPClient do ESP32.</summary>
    public const int ErroConexaoRecusada = -1, ErroTempoDeLeitura = -11;

    /// <summary>
    /// Um GET em bytes (o MP3 da fala, o arquivo de audio): a resposta do
    /// Requisitar e texto, e o binario nao sobrevive a ele. Do computador ou
    /// da internet, com a mesma regra da internet ligada. Null se nao veio.
    /// </summary>
    public byte[] Baixar(Maquina origem, string url, int tempoMs, int limite = 32 << 20)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;
        if (!EhLocal(uri.Host) && !UsarInternet)
        {
            Anotar(origem, "http", Idioma.T("GET {0} → sem internet: a rede do simulador está sem saída (menu ⋮ > Rede usa a internet do computador)", url));
            return null;
        }
        using var cancelar = new CancellationTokenSource(tempoMs);
        var tarefa = Task.Run(async () =>
        {
            using var mensagem = new HttpRequestMessage(HttpMethod.Get, uri);
            mensagem.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (ESP32 CircuitSketch)");
            using var recebida = await Cliente.SendAsync(mensagem, HttpCompletionOption.ResponseHeadersRead, cancelar.Token);
            if (!recebida.IsSuccessStatusCode) return null;
            using var fluxo = await recebida.Content.ReadAsStreamAsync(cancelar.Token);
            var memoria = new MemoryStream();
            var buffer = new byte[16384];
            int n;
            while ((n = await fluxo.ReadAsync(buffer, cancelar.Token)) > 0 && memoria.Length < limite) memoria.Write(buffer, 0, n);
            return memoria.ToArray();
        });
        try
        {
            while (!tarefa.Wait(40))
                if (origem.Parar) { cancelar.Cancel(); throw new ExecucaoInterrompida(); }
            var dados = tarefa.Result;
            Anotar(origem, "http", dados is null ? $"GET {url} → recusado" : Idioma.T("GET {0} → {1} bytes", url, dados.Length));
            return dados;
        }
        catch (AggregateException)
        {
            Anotar(origem, "http", Idioma.T("GET {0} → não veio", url));
            return null;
        }
    }

    public RespostaHttp Requisitar(Maquina origem, string metodo, string url,
        IDictionary<string, string> cabecalhos, string corpo, int tempoMs, bool inseguro)
    {
        metodo = (metodo ?? "GET").ToUpperInvariant();
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https"))
        {
            Anotar(origem, "http", Idioma.T("{0} {1}: endereço inválido (precisa começar com http:// ou https://)", metodo, url));
            return RespostaHttp.Erro(ErroConexaoRecusada);
        }

        if (!NaRede(origem))
        {
            Anotar(origem, "aviso", Idioma.T("{0} {1}: a placa não está no WiFi. Chame WiFi.begin() e espere WL_CONNECTED antes.", metodo, url));
            return RespostaHttp.Erro(ErroConexaoRecusada);
        }

        // 1. outra placa da bancada
        var alvo = ServidorNoEndereco(uri.Host, uri.Port);
        if (alvo is not null)
        {
            var pedido = PedidoHttp.DeUri(metodo, uri, cabecalhos, corpo);
            pedido.Origem = origem.Nome;
            byte[] bytes = alvo.Entregar(pedido, tempoMs, () => origem.Parar);
            if (origem.Parar) throw new ExecucaoInterrompida();
            if (bytes is null)
            {
                Anotar(origem, "http", Idioma.T("{0} {1} → {2} não respondeu a tempo", metodo, url, alvo.Dona.Nome));
                return RespostaHttp.Erro(ErroTempoDeLeitura);
            }
            var resposta = PedidoHttp.LerResposta(bytes);
            Anotar(origem, "http", $"{metodo} {url} → {resposta.Codigo} ({alvo.Dona.Nome})");
            return resposta;
        }

        if (EhIpDaBancada(uri.Host))
        {
            Anotar(origem, "http", Idioma.T("{0} {1} → recusado: nenhuma placa ouve na porta {2}", metodo, url, uri.Port));
            return RespostaHttp.Erro(ErroConexaoRecusada);
        }

        // 2 e 3. o computador ou a internet
        if (!EhLocal(uri.Host) && !UsarInternet)
        {
            Anotar(origem, "http", Idioma.T("{0} {1} → sem internet: a rede do simulador está sem saída (menu ⋮ > Rede usa a internet do computador)", metodo, url));
            return RespostaHttp.Erro(ErroConexaoRecusada);
        }

        var cliente = inseguro ? ClienteInseguro : Cliente;
        using var cancelar = new CancellationTokenSource(tempoMs);
        var tarefa = Task.Run(async () =>
        {
            using var mensagem = new HttpRequestMessage(new HttpMethod(metodo), uri);
            if (corpo is not null && metodo is not ("GET" or "HEAD"))
                mensagem.Content = new ByteArrayContent(TextoArduino.ParaBytes(corpo));

            foreach (var (nome, valor) in cabecalhos ?? new Dictionary<string, string>())
            {
                if (nome.StartsWith("Content-", StringComparison.OrdinalIgnoreCase))
                {
                    mensagem.Content ??= new ByteArrayContent([]);
                    mensagem.Content.Headers.Remove(nome);
                    mensagem.Content.Headers.TryAddWithoutValidation(nome, valor);
                }
                else mensagem.Headers.TryAddWithoutValidation(nome, valor);
            }
            if (!mensagem.Headers.UserAgent.Any())
                mensagem.Headers.TryAddWithoutValidation("User-Agent", "ESP32HTTPClient (CircuitSketch)");

            using var recebida = await cliente.SendAsync(mensagem, cancelar.Token);
            string texto = await recebida.Content.ReadAsStringAsync(cancelar.Token);

            var todos = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var h in recebida.Headers) todos[h.Key] = string.Join(", ", h.Value);
            foreach (var h in recebida.Content.Headers) todos[h.Key] = string.Join(", ", h.Value);
            return new RespostaHttp((int)recebida.StatusCode, texto, todos);
        });

        try
        {
            while (!tarefa.Wait(40))
                if (origem.Parar) { cancelar.Cancel(); throw new ExecucaoInterrompida(); }

            var resposta = tarefa.Result;
            Anotar(origem, "http", Idioma.T("{0} {1} → {2} ({3} caracteres)", metodo, url, resposta.Codigo, resposta.Corpo.Length));
            return resposta;
        }
        catch (AggregateException ae) when (ae.InnerException is OperationCanceledException)
        {
            Anotar(origem, "http", Idioma.T("{0} {1} → tempo esgotado ({2} ms)", metodo, url, tempoMs));
            return RespostaHttp.Erro(ErroTempoDeLeitura);
        }
        catch (AggregateException ae)
        {
            Anotar(origem, "http", Idioma.T("{0} {1} → não conectou: {2}", metodo, url, Explicar(ae.InnerException)));
            return RespostaHttp.Erro(ErroConexaoRecusada);
        }
    }

    static string Explicar(Exception ex)
    {
        while (ex?.InnerException is not null && ex is HttpRequestException) ex = ex.InnerException;
        return ex switch
        {
            null => Idioma.T("erro desconhecido"),
            SocketException { SocketErrorCode: SocketError.HostNotFound } => Idioma.T("endereço não encontrado (sem internet?)"),
            SocketException { SocketErrorCode: SocketError.ConnectionRefused } => Idioma.T("conexão recusada"),
            TimeoutException => Idioma.T("tempo esgotado"),
            _ => ex.Message,
        };
    }

    // =====================================================================
    //  TCP (WiFiClient.connect)
    // =====================================================================

    readonly List<(Maquina Dona, ConexaoTcp Conexao)> conexoes = [];

    public ConexaoTcp ConectarTcp(Maquina origem, string host, int porta, bool segura, bool insegura)
    {
        if (!NaRede(origem))
        {
            Anotar(origem, "aviso", Idioma.T("connect({0}, {1}): a placa não está no WiFi", host, porta));
            return null;
        }

        ConexaoTcp conexao;
        var servidor = ServidorNoEndereco(host, porta);
        if (servidor is not null)
        {
            conexao = ConexaoTcp.ParaPlaca(origem, servidor, host, porta);
            Anotar(origem, "tcp", Idioma.T("conectou em {0} ({1}:{2})", servidor.Dona.Nome, host, porta));
        }
        else if (EhIpDaBancada(host))
        {
            Anotar(origem, "tcp", Idioma.T("{0}:{1} recusou: nenhuma placa ouve nessa porta", host, porta));
            return null;
        }
        else if (host == EnderecoDoBrokerInterno && PortaDoBroker > 0 && !segura)
        {
            // O cliente MQTT por socket (o umqtt, o esp-mqtt) cujo broker nao resolveu: o interno.
            try { conexao = ConexaoTcp.Real(origem, "127.0.0.1", PortaDoBroker, false, false, 5000); }
            catch (ExecucaoInterrompida) { throw; }
            catch (Exception ex)
            {
                Anotar(origem, "tcp", Idioma.T("o broker interno não conectou: {0}", Explicar(ex)));
                return null;
            }
        }
        else if (MqttSoNoComputador && porta == 1883 && !segura && PortaDoBroker > 0 && !EhLocal(host))
        {
            try
            {
                conexao = ConexaoTcp.Real(origem, "127.0.0.1", PortaDoBroker, false, false, 5000);
                Anotar(origem, "mqtt", Idioma.T("\"{0}\" foi para o broker do computador (menu ⋮ > MQTT)", host));
            }
            catch (ExecucaoInterrompida) { throw; }
            catch (Exception ex)
            {
                Anotar(origem, "tcp", Idioma.T("o broker interno não conectou: {0}", Explicar(ex)));
                return null;
            }
        }
        else if (!EhLocal(host) && !UsarInternet)
        {
            Anotar(origem, "tcp", Idioma.T("{0}:{1}: sem internet (menu ⋮ > Rede usa a internet do computador)", host, porta));
            return null;
        }
        else
        {
            try
            {
                conexao = ConexaoTcp.Real(origem, host, porta, segura, insegura, 5000);
                Anotar(origem, "tcp", Idioma.T("conectou em {0}:{1}", host, porta));
            }
            catch (ExecucaoInterrompida) { throw; }
            catch (Exception ex)
            {
                // A rede que bloqueia a porta do MQTT (a da escola): o broker interno atende, como na PubSubClient.
                conexao = null;
                if (porta == 1883 && !segura && !EhLocal(host) && PortaDoBroker > 0)
                {
                    try
                    {
                        conexao = ConexaoTcp.Real(origem, "127.0.0.1", PortaDoBroker, false, false, 5000);
                        Anotar(origem, "mqtt", Idioma.T("{0}:{1} não respondeu ({2}) -- o broker interno do simulador atende no lugar dele", host, porta, Explicar(ex)));
                    }
                    catch (ExecucaoInterrompida) { throw; }
                    catch (Exception) { conexao = null; }
                }
                if (conexao is null)
                {
                    Anotar(origem, "tcp", Idioma.T("{0}:{1} não conectou: {2}", host, porta, Explicar(ex)));
                    return null;
                }
            }
        }

        lock (trava) conexoes.Add((origem, conexao));
        return conexao;
    }

    // =====================================================================
    //  MQTT (PubSubClient)
    // =====================================================================

    public BrokerMqtt Broker { get; } = new();

    ServidorMqtt servidorMqtt;

    /// <summary>Porta do broker interno em localhost, ou 0 se não abriu.</summary>
    public int PortaDoBroker => servidorMqtt?.Porta ?? 0;

    readonly List<ClienteMqtt> clientesMqtt = [];

    /// <summary>
    /// Abre o broker interno em localhost. Se a porta estiver em uso (um
    /// mosquitto instalado, por exemplo), ele continua funcionando dentro
    /// do programa, só não fica visível para outros apps.
    /// </summary>
    public bool AbrirBrokerLocal(int porta, bool naRede = false)
    {
        if (servidorMqtt is not null) return true;
        servidorMqtt = ServidorMqtt.Abrir(Broker, porta, naRede);
        return servidorMqtt is not null;
    }

    /// <summary>O broker do computador aberto agora (null se a porta estava ocupada).</summary>
    public ServidorMqtt ServidorDoBroker => servidorMqtt;

    /// <summary>
    /// Fecha e abre de novo o broker do computador, em outra porta ou aberto
    /// para a rede. As placas no broker interno nao percebem: o broker e o
    /// mesmo, so a porta de fora muda.
    /// </summary>
    public bool ReabrirBroker(int porta, bool naRede)
    {
        servidorMqtt?.Fechar();
        servidorMqtt = ServidorMqtt.Abrir(Broker, porta, naRede);
        return servidorMqtt is not null;
    }

    /// <summary>
    /// Toda placa da simulacao vai para o broker do computador, qualquer que
    /// seja o endereco do sketch (o broker.hivemq.com da aula, o IP do
    /// mosquitto da escola). E o jeito de ver tudo na janela do MQTT sem
    /// mudar o codigo que vai para a placa de verdade.
    /// </summary>
    public bool MqttSoNoComputador { get; set; }

    /// <summary>As mensagens das placas num broker de fora: a janela do MQTT mostra junto com as do computador.</summary>
    public event Action<TrafegoMqtt> MqttDeFora;

    void AvisarDeFora(ClienteMqtt cliente, bool saindo, MensagemMqtt mensagem)
    {
        try { MqttDeFora?.Invoke(new TrafegoMqtt(cliente.Dona?.Nome ?? "", $"{cliente.Host}:{cliente.Porta}", saindo, mensagem)); }
        catch { }
    }

    /// <summary>Os clientes MQTT das placas da simulacao, conectados ou nao.</summary>
    public IReadOnlyList<ClienteMqtt> ClientesMqtt()
    {
        lock (trava) return [.. clientesMqtt];
    }

    /// <summary>Quem publica: a placa e o id dela, como a janela do MQTT mostra.</summary>
    static string Origem(ClienteMqtt cliente)
        => string.IsNullOrEmpty(cliente.Id) ? cliente.Dona?.Nome ?? "" : $"{cliente.Dona?.Nome} · {cliente.Id}";

    /// <summary>
    /// Brokers que nao responderam, e quando. Na rede da escola que bloqueia
    /// a porta 1883, cada connect() esperava cinco segundos inteiros antes
    /// de cair no broker interno -- e o sketch que reconecta no loop()
    /// esperava de novo a cada volta. Lembrando, o segundo connect() ja cai
    /// direto.
    /// </summary>
    readonly Dictionary<string, long> semResposta = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Quanto tempo um broker que nao respondeu fica sem ser tentado de novo.</summary>
    public const int EsperaParaTentarDeNovoMs = 120_000;

    public bool ConectarMqtt(ClienteMqtt cliente, string id, string usuario, string senha)
    {
        var placa = cliente.Dona;
        DesconectarMqtt(cliente, calado: true);
        cliente.Id = id ?? "";
        cliente.Usuario = usuario;
        cliente.Senha = senha;

        if (!NaRede(placa))
        {
            cliente.Estado = -2;
            Anotar(placa, "aviso", Idioma.T("MQTT connect(): a placa não está no WiFi"));
            return false;
        }
        if (string.IsNullOrWhiteSpace(cliente.Host))
        {
            cliente.Estado = -2;
            Anotar(placa, "aviso", Idioma.T("MQTT connect() sem client.setServer(broker, porta) antes"));
            return false;
        }

        string host = cliente.Host;
        string chave = $"{host}:{cliente.Porta}";
        bool daBancada = host.Equals("circuitsketch", StringComparison.OrdinalIgnoreCase) || EhIpDaBancada(host);
        bool semSaida = (!UsarInternet && !EhLocal(host)) || (MqttSoNoComputador && !daBancada);

        if (!daBancada && !semSaida)
        {
            // Outra placa daqui, no mesmo broker, com o mesmo id: o broker de
            // verdade derruba uma quando a outra conecta, e as duas ficam se
            // derrubando para sempre. E o erro classico da aula em que todo
            // mundo roda o mesmo sketch.
            AvisarIdRepetido(cliente, externo: true);

            long agora = Environment.TickCount64;
            long falhou;
            lock (trava) semResposta.TryGetValue(chave, out falhou);
            bool lembrado = falhou != 0 && agora - falhou < EsperaParaTentarDeNovoMs;

            if (!lembrado)
            {
                try
                {
                    var tcp = ConexaoMqtt.Conectar(host, cliente.Porta, cliente.Id, usuario, senha,
                        cliente.KeepAlive, 9500, m =>
                        {
                            cliente.Chegou(m);
                            Anotar(placa, "mqtt", $"← {m.Topico}: {Curto(m.Carga)}");
                            AvisarDeFora(cliente, false, m);
                        }, () => placa.Parar, cliente.Testamento, cliente.Tls, !cliente.SemConferirCertificado);

                    tcp.Caiu += () =>
                    {
                        if (cliente.Tcp != tcp) return;
                        cliente.Estado = -3;

                        // Caiu logo depois de conectar, sem o sketch pedir:
                        // quase sempre e outro cliente com o mesmo id.
                        bool logo = Environment.TickCount64 - cliente.ConectouEm < 20_000;
                        Anotar(placa, "mqtt", Idioma.T("a conexão com {0} caiu{1}", host, (logo ? Idioma.T(" logo depois de conectar. Outro cliente entrou com o mesmo id (\"{0}\")? Num broker público cada placa precisa de um id diferente, como \"ESP32-\" + WiFi.macAddress().", cliente.Id) : "")));
                    };
                    cliente.Tcp = tcp;
                    cliente.Interno = false;
                    cliente.Estado = 0;
                    cliente.ConectouEm = Environment.TickCount64;
                    lock (trava)
                    {
                        semResposta.Remove(chave);
                        if (!clientesMqtt.Contains(cliente)) clientesMqtt.Add(cliente);
                    }
                    Anotar(placa, "mqtt", Idioma.T("conectou em {0}:{1}{2} como \"{3}\"", host, cliente.Porta, (cliente.Tls ? " (TLS)" : ""), cliente.Id));
                    return true;
                }
                catch (ExecucaoInterrompida) { throw; }
                catch (RecusaDoBroker recusa)
                {
                    // O broker respondeu que nao: a placa de verdade fica sem
                    // conectar, com o codigo no state(). Cair no broker
                    // interno aqui esconderia a senha errada.
                    cliente.Estado = recusa.Codigo;
                    Anotar(placa, "mqtt", Idioma.T("{0} recusou a conexão: {1} (client.state() = {2})", host, recusa.Message, recusa.Codigo));
                    return false;
                }
                catch (Exception ex)
                {
                    // Qualquer outra falha e "ninguem atendeu" para quem
                    // esta na aula: o nome nao resolveu, a porta esta
                    // bloqueada, ou o broker aceitou o TCP e nunca respondeu
                    // o CONNECT -- o que o broker.hivemq.com faz em algumas
                    // redes. A placa de verdade ficaria sem conectar (state
                    // -4); o simulador segue pelo broker interno, para a
                    // bancada e o painel continuarem conversando, e diz no
                    // painel o que aconteceu.
                    var causa = ex is BrokerSemResposta ? ex.InnerException ?? ex : ex;
                    if (EhLocal(host))
                    {
                        cliente.Estado = ex is TimeoutException || causa is TimeoutException ? -4 : -2;
                        Anotar(placa, "mqtt", Idioma.T("{0}:{1} não conectou: {2}", host, cliente.Porta, Explicar(causa)));
                        return false;
                    }

                    string dica = cliente.Porta == 8883 && !cliente.Tls
                        ? Idioma.T(" A porta 8883 é a do MQTT com TLS: o PubSubClient precisa de um WiFiClientSecure.")
                        : cliente.Porta == 1883 && cliente.Tls
                            ? Idioma.T(" A porta 1883 é a do MQTT sem TLS: com WiFiClientSecure, o broker costuma usar a 8883.")
                            : ex is BrokerSemResposta ? "" : Idioma.T(" (a conexão abriu, mas o broker não respondeu o CONNECT)");

                    lock (trava) semResposta[chave] = Environment.TickCount64;
                    Anotar(placa, "mqtt", Idioma.T("{0} não respondeu ({1}).{2} Usando o broker interno do CircuitSketch: as placas da bancada e o painel Rede conversam por ele", host, Explicar(causa), dica));
                }
            }
            else
            {
                long volta = (EsperaParaTentarDeNovoMs - (agora - falhou) + 999) / 1000;
                Anotar(placa, "mqtt", Idioma.T("{0} não respondeu há pouco; usando o broker interno direto (tenta {1} de novo em {2} s)", host, host, volta));
            }
        }
        else if (semSaida && MqttSoNoComputador)
        {
            Anotar(placa, "mqtt", Idioma.T("\"{0}\" foi para o broker do computador (menu ⋮ > MQTT)", host));
        }
        else if (semSaida)
        {
            Anotar(placa, "mqtt", Idioma.T("sem internet: \"{0}\" virou o broker interno do CircuitSketch", host));
        }

        // O broker interno tambem derruba o id repetido -- e o que qualquer
        // broker faz, e a placa de verdade na aula veria o mesmo.
        AvisarIdRepetido(cliente, externo: false);

        cliente.Tcp = null;
        cliente.Interno = true;
        cliente.Estado = 0;
        cliente.ConectouEm = Environment.TickCount64;
        lock (trava) if (!clientesMqtt.Contains(cliente)) clientesMqtt.Add(cliente);
        if (daBancada) Anotar(placa, "mqtt", Idioma.T("conectou no broker interno como \"{0}\"", cliente.Id));
        return true;
    }

    /// <summary>
    /// Outro cliente desta bancada com o mesmo id, no mesmo broker. No
    /// interno, o velho e derrubado agora -- como o broker de verdade faz. No
    /// de fora, quem derruba e o broker; aqui so fica o aviso.
    /// </summary>
    void AvisarIdRepetido(ClienteMqtt cliente, bool externo)
    {
        if (string.IsNullOrEmpty(cliente.Id)) return;

        List<ClienteMqtt> repetidos;
        lock (trava)
            repetidos = [.. clientesMqtt.Where(c => c != cliente && c.Conectado && c.Id == cliente.Id
                                                  && (externo
                                                      ? !c.Interno && string.Equals(c.Host, cliente.Host, StringComparison.OrdinalIgnoreCase)
                                                      : c.Interno))];

        foreach (var velho in repetidos)
        {
            string texto = Idioma.T("duas placas com o mesmo client id (\"{0}\"): o broker derruba uma quando a outra conecta. Dê um id diferente para cada uma, como \"ESP32-\" + WiFi.macAddress().", cliente.Id);
            Anotar(cliente.Dona, "aviso", texto);

            if (!externo)
            {
                Broker.RemoverDono(velho);
                velho.EsvaziarChegadas();
                velho.Estado = -3;
                Anotar(velho.Dona, "mqtt", Idioma.T("o broker derrubou a conexão: outra placa entrou com o mesmo id (\"{0}\")", velho.Id));
            }
        }
    }

    public bool PublicarMqtt(ClienteMqtt cliente, string topico, byte[] carga, bool retida)
    {
        if (!cliente.Conectado) return false;
        Anotar(cliente.Dona, "mqtt", $"→ {topico}: {Curto(carga)}");

        if (cliente.Interno)
        {
            Broker.Publicar(topico, carga, retida, Origem(cliente));
            return true;
        }
        AvisarDeFora(cliente, true, new MensagemMqtt(topico, carga ?? [], retida));
        try { cliente.Tcp.Publicar(topico, carga, retida); return true; }
        catch
        {
            cliente.Estado = -3;
            return false;
        }
    }

    public bool AssinarMqtt(ClienteMqtt cliente, string filtro)
    {
        if (!cliente.Conectado) return false;
        Anotar(cliente.Dona, "mqtt", Idioma.T("assinou {0}", filtro));

        if (cliente.Interno)
        {
            Broker.Assinar(cliente, filtro, cliente.Chegou);
            return true;
        }
        try { cliente.Tcp.Assinar(filtro); return true; }
        catch { cliente.Estado = -3; return false; }
    }

    public bool CancelarMqtt(ClienteMqtt cliente, string filtro)
    {
        if (!cliente.Conectado) return false;
        if (cliente.Interno) Broker.Cancelar(cliente, filtro);
        else try { cliente.Tcp.Cancelar(filtro); } catch { }
        return true;
    }

    /// <summary>O <c>client.loop()</c>: mantém a conexão viva e diz se ainda está conectada.</summary>
    public bool ManterMqtt(ClienteMqtt cliente)
    {
        if (!cliente.Conectado) return false;
        if (!cliente.Interno) cliente.Tcp?.ManterViva();
        return cliente.Conectado;
    }

    /// <summary>
    /// Desliga o cliente. O client.disconnect() do sketch e educado: manda o
    /// DISCONNECT, e o testamento nao sai. A placa que para, reseta ou sai do
    /// projeto some sem avisar -- e ai o broker publica o testamento dela.
    /// </summary>
    public void DesconectarMqtt(ClienteMqtt cliente, bool calado = false, bool educado = true)
    {
        bool estava = cliente.Conectado;
        Broker.RemoverDono(cliente);

        if (estava && !educado && cliente.Testamento is { } testamento && cliente.Interno)
        {
            Broker.Publicar(testamento.Topico, testamento.Carga, testamento.Retida, Origem(cliente));
            Anotar(cliente.Dona, "mqtt", Idioma.T("o broker publicou o testamento: {0}: {1}", testamento.Topico, Curto(testamento.Carga)));
        }

        cliente.Tcp?.Fechar(educado);
        cliente.Tcp = null;
        cliente.Estado = -1;
        cliente.EsvaziarChegadas();
        if (estava && !calado) Anotar(cliente.Dona, "mqtt", Idioma.T("desconectou do broker"));
    }

    // ---- o painel da janela ----

    readonly Dictionary<string, ConexaoMqtt> paineisMqtt = [];

    /// <summary>
    /// Publica pelo painel Rede: no broker interno e em todo broker de
    /// verdade que alguma placa está usando agora -- o painel faz o papel
    /// do celular com o app de MQTT.
    /// </summary>
    public void PublicarDoPainel(string topico, string texto, bool retida = false, string origem = null)
    {
        if (string.IsNullOrWhiteSpace(topico)) return;
        byte[] carga = Encoding.UTF8.GetBytes(texto ?? "");
        Anotar(null, "mqtt", Idioma.T("painel → {0}: {1}", topico, Curto(texto)));
        Broker.Publicar(topico, carga, retida, origem ?? Idioma.T("painel"));

        // Um por broker: o painel entra com o TLS e o usuario da placa que
        // ja conectou nele -- o HiveMQ Cloud nao aceita ninguem sem senha.
        List<ClienteMqtt> externos;
        lock (trava)
            externos = [.. clientesMqtt.Where(c => c.Conectado && !c.Interno)
                                       .GroupBy(c => $"{c.Host}:{c.Porta}", StringComparer.OrdinalIgnoreCase)
                                       .Select(g => g.First())];

        // Fora da janela: conectar num broker de fora leva ate uns segundos
        // (e ele as vezes fica mudo e precisa de outra tentativa), e o botao
        // de publicar nao pode congelar o programa enquanto isso.
        if (externos.Count == 0) return;
        Task.Run(() =>
        {
            lock (publicandoDoPainel)
                foreach (var modelo in externos)
                {
                    string host = modelo.Host;
                    int porta = modelo.Porta;
                    string chave = $"{host}:{porta}";
                    try
                    {
                        ConexaoMqtt conexao;
                        lock (trava) paineisMqtt.TryGetValue(chave, out conexao);
                        if (conexao is null || !conexao.Aberta)
                        {
                            conexao = ConexaoMqtt.Conectar(host, porta, "circuitsketch-painel-" + Random.Shared.Next(10000, 99999),
                                modelo.Usuario, modelo.Senha, 60, 9500, _ => { }, null, null,
                                modelo.Tls, !modelo.SemConferirCertificado);
                            lock (trava) paineisMqtt[chave] = conexao;
                        }
                        conexao.Publicar(topico, carga, retida);
                    }
                    catch (Exception ex)
                    {
                        Anotar(null, "mqtt", Idioma.T("painel não publicou em {0}: {1}", chave, Explicar(ex)));
                    }
                }
        });
    }

    /// <summary>Uma publicacao do painel de cada vez: a ordem das mensagens e a do clique.</summary>
    readonly object publicandoDoPainel = new();

    // =====================================================================
    //  Bluetooth (BluetoothSerial)
    // =====================================================================

    readonly List<DispositivoBt> bluetooth = [];
    DispositivoBt comCelular;

    /// <summary>
    /// Abrir o servico de porta serial no radio Bluetooth do computador,
    /// para o celular de verdade conectar. Ligado pelo programa; os testes
    /// deixam desligado, para nao mexer no radio da maquina.
    /// </summary>
    public bool UsarBluetoothDoPc { get; set; }

    public DispositivoBt IniciarBluetooth(Maquina placa, string nome, bool mestre)
    {
        EsquecerBluetooth(placa);
        var dispositivo = new DispositivoBt(this, placa, string.IsNullOrWhiteSpace(nome) ? "ESP32" : nome, mestre);
        lock (trava) bluetooth.Add(dispositivo);

        dispositivo.Avisar(DispositivoBt.EventoInicio);
        if (!mestre) dispositivo.Avisar(DispositivoBt.EventoServidor);

        if (mestre)
        {
            Anotar(placa, "bt", Idioma.T("Bluetooth ligado como mestre \"{0}\"", dispositivo.Nome));
            return dispositivo;
        }

        // A placa visível também vira uma porta do computador. É o que
        // o servidor WiFi já fazia e o Bluetooth não: sem isto, o
        // SerialBT só falava com o celular desenhado no painel, e não
        // dava para provar nada com um programa de verdade.
        var porta = PortaBluetooth.Abrir(dispositivo, PortaBluetooth.PrimeiraPorta);

        Anotar(placa, "bt", porta is null
            ? Idioma.T("Bluetooth visível como \"{0}\" — conecte pelo painel Bluetooth", dispositivo.Nome)
            : Idioma.T("Bluetooth visível como \"{0}\" — painel Bluetooth, ou 127.0.0.1:{1} de qualquer programa do computador", dispositivo.Nome, porta.Porta));

        // E no radio de verdade do computador, para o celular do aluno.
        if (UsarBluetoothDoPc)
        {
            dispositivo.PeloRadio = ServicoBluetoothDoPc.Abrir(dispositivo);
            Anotar(placa, "bt", dispositivo.PeloRadio is { } radio
                ? Idioma.T("celular de verdade: pareie este computador no celular e, no app de terminal Bluetooth, conecte no computador (serviço \"{0}\")", radio.Nome)
                : Idioma.T("celular de verdade: não dá agora — {0}", ServicoBluetoothDoPc.Motivo));
        }

        return dispositivo;
    }

    /// <summary>A placa saiu do ar: o Bluetooth dela some e o par se desfaz.</summary>
    public void EsquecerBluetooth(Maquina placa)
    {
        List<DispositivoBt> saindo;
        lock (trava)
        {
            saindo = [.. bluetooth.Where(d => d.Dona == placa)];
            bluetooth.RemoveAll(d => d.Dona == placa);
        }
        foreach (var d in saindo)
        {
            if (d.Par is { } par) { par.Par = null; d.Par = null; }
            if (comCelular == d) comCelular = null;

            // A porta some com a placa: deixá-la aberta seguraria o
            // número e a execução seguinte cairia na porta de baixo.
            d.PeloComputador?.Fechar();
            d.PeloComputador = null;
            d.PeloRadio?.Fechar();
            d.PeloRadio = null;
        }
    }

    /// <summary>Nomes que o celular do painel encontra ao procurar dispositivos.</summary>
    public List<string> DispositivosVisiveis()
    {
        lock (trava) return [.. bluetooth.Where(d => !d.Mestre).Select(d => d.Nome).Distinct()];
    }

    /// <summary><c>SerialBT.connect("nome")</c> de uma placa mestre para outra.</summary>
    public bool ConectarBluetooth(DispositivoBt mestre, string nome)
    {
        DispositivoBt alvo;
        lock (trava) alvo = bluetooth.FirstOrDefault(d => d != mestre && !d.Mestre && d.Nome == nome);
        if (alvo is null)
        {
            Anotar(mestre.Dona, "bt", Idioma.T("não encontrou \"{0}\" (a outra placa chamou SerialBT.begin(\"{1}\")?)", nome, nome));
            return false;
        }
        return ConectarBluetooth(mestre, alvo);
    }

    /// <summary>O mestre conecta num aparelho certo (o HC-05 pelo endereco do BIND: o nome pode repetir).</summary>
    public bool ConectarBluetooth(DispositivoBt mestre, DispositivoBt alvo)
    {
        string nome = alvo.Nome;
        lock (trava)
            if (!bluetooth.Contains(alvo) || alvo == mestre || alvo.Mestre) return false;
        mestre.Par = alvo;
        alvo.Par = mestre;
        mestre.Avisar(DispositivoBt.EventoAbriu, alvo.Nome);
        alvo.Avisar(DispositivoBt.EventoClienteAbriu, mestre.Nome);
        Anotar(mestre.Dona, "bt", Idioma.T("conectou em \"{0}\" ({1})", nome, alvo.Dona.Nome));
        return true;
    }

    /// <summary>As placas que a busca do Bluetooth classico acha (o discover): as visiveis, menos quem procura.</summary>
    public List<DispositivoBt> BluetoothVisiveis(DispositivoBt quem)
    {
        lock (trava) return bluetooth.Where(d => d != quem && !d.Mestre).ToList();
    }

    public string CelularConectadoEm => comCelular?.Nome;

    public bool ConectarCelular(string nome)
    {
        DispositivoBt alvo;
        lock (trava) alvo = bluetooth.FirstOrDefault(d => !d.Mestre && d.Nome == nome);
        if (alvo is null) return false;
        if (comCelular is not null && comCelular != alvo) comCelular.Avisar(DispositivoBt.EventoFechou, "celular");
        comCelular = alvo;
        alvo.Avisar(DispositivoBt.EventoClienteAbriu, "celular");
        Anotar(alvo.Dona, "bt", Idioma.T("celular conectou em \"{0}\"", nome));
        return true;
    }

    public void DesconectarCelular()
    {
        if (comCelular is null) return;
        comCelular.Avisar(DispositivoBt.EventoFechou, "celular");
        Anotar(comCelular.Dona, "bt", Idioma.T("celular desconectou"));
        comCelular = null;
    }

    /// <summary>O celular do painel manda texto para a placa conectada.</summary>
    public bool EnviarDoCelular(string texto)
    {
        var alvo = comCelular;
        if (alvo is null) return false;
        alvo.Entrada.Adicionar(texto);
        alvo.Avisar(DispositivoBt.EventoDados, "celular");
        Anotar(alvo.Dona, "bt", Idioma.T("celular → {0}", Curto(texto.TrimEnd('\n', '\r'))));
        return true;
    }

    internal bool TemCelular(DispositivoBt dispositivo) => comCelular == dispositivo;

    /// <summary>
    /// A placa mandou bytes pelo SerialBT para o celular: o terminal da janela
    /// do celular mostra do jeito que chegaram. Vem da thread do sketch.
    /// </summary>
    public event Action<DispositivoBt, byte[]> ChegouNoCelular;

    internal void EntregarNoCelular(DispositivoBt de, byte[] dados) => ChegouNoCelular?.Invoke(de, dados);

    // =====================================================================
    //  Fim de placa e fim de simulação
    // =====================================================================

    /// <summary>
    /// A placa reiniciou ou parou: tudo o que ela tinha aberto fecha. O IP
    /// fica guardado -- a mesma placa volta com o mesmo endereço, como
    /// num roteador de verdade.
    /// </summary>
    public void EsquecerPlaca(Maquina placa)
    {
        foreach (var servidor in Servidores.Where(s => s.Dona == placa)) FecharServidor(servidor);

        List<ClienteMqtt> clientes;
        List<ConexaoTcp> abertas;
        lock (trava)
        {
            clientes = [.. clientesMqtt.Where(c => c.Dona == placa)];
            clientesMqtt.RemoveAll(c => c.Dona == placa);
            abertas = [.. conexoes.Where(c => c.Dona == placa).Select(c => c.Conexao)];
            conexoes.RemoveAll(c => c.Dona == placa);
        }
        foreach (var cliente in clientes) DesconectarMqtt(cliente, calado: true, educado: false);
        foreach (var conexao in abertas) conexao.Fechar();

        Esquecer154(placa);
        EsquecerUdp(placa);
        PararMdns(placa);
        EsquecerBluetooth(placa);
        EsquecerBle(placa);
        EsquecerRadios(placa);

        lock (trava)
            if (wifi.TryGetValue(placa, out var estado)) estado.Ligado = false;
    }

    /// <summary>A simulação parou: fecha servidores, conexões e o broker em localhost.</summary>
    public void Encerrar()
    {
        // Toda placa que ligou alguma coisa: o WiFi, um servidor, o Bluetooth
        // classico ou o BLE (a placa so de Bluetooth nao passa pelo WiFi).
        List<Maquina> placas;
        lock (trava)
            placas = [.. wifi.Keys.Concat(servidores.Select(s => s.Dona)).Concat(bluetooth.Select(b => b.Dona))
                                  .Concat(radiosBle.Select(r => r.Dona)).Distinct()];
        foreach (var placa in placas) EsquecerPlaca(placa);

        lock (trava)
        {
            foreach (var conexao in paineisMqtt.Values) conexao.Fechar();
            paineisMqtt.Clear();
        }
        servidorMqtt?.Fechar();
        servidorMqtt = null;
        comCelular = null;
    }
}

/// <summary>
/// Um pedido HTTP que chegou a uma placa -- do navegador, por uma porta
/// do computador, ou de outra placa da bancada.
/// </summary>
public sealed class PedidoHttp
{
    public string Metodo { get; set; } = "GET";
    public string Caminho { get; set; } = "/";
    public string Consulta { get; set; } = "";
    public Dictionary<string, string> Cabecalhos { get; } = new(StringComparer.OrdinalIgnoreCase);
    public string Corpo { get; set; } = "";

    /// <summary>O pedido em bytes, do jeito que chegou: é o que o WiFiServer lê.</summary>
    public byte[] Cru { get; set; } = [];

    public string Origem { get; set; } = "";

    public string CaminhoCompleto => Consulta.Length > 0 ? $"{Caminho}?{Consulta}" : Caminho;

    readonly TaskCompletionSource<byte[]> resposta = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public bool Respondido => resposta.Task.IsCompleted;

    public void Responder(byte[] bytes) => resposta.TrySetResult(bytes ?? []);

    /// <summary>Espera a placa responder. Null se desistiu ou passou do tempo.</summary>
    public byte[] Esperar(int tempoMs, Func<bool> desistir)
    {
        var relogio = Stopwatch.StartNew();
        while (!resposta.Task.Wait(20))
        {
            if (desistir?.Invoke() == true) return null;
            if (relogio.ElapsedMilliseconds > tempoMs) return null;
        }
        return resposta.Task.Result;
    }

    /// <summary>Lê um pedido de uma conexão aberta pelo navegador.</summary>
    public static PedidoHttp Ler(Stream fluxo)
    {
        var cabecalho = new List<byte>();
        while (true)
        {
            int b = fluxo.ReadByte();
            if (b < 0) return null;
            cabecalho.Add((byte)b);
            int n = cabecalho.Count;
            if (n >= 4 && cabecalho[n - 4] == 13 && cabecalho[n - 3] == 10
                       && cabecalho[n - 2] == 13 && cabecalho[n - 1] == 10) break;
            if (n > 65536) return null;
        }

        var pedido = DeCabecalho(Encoding.Latin1.GetString([.. cabecalho]));
        if (pedido is null) return null;

        int tamanho = int.TryParse(pedido.Cabecalhos.GetValueOrDefault("Content-Length"), out int t)
            ? Math.Clamp(t, 0, 1 << 20) : 0;
        var corpo = new byte[tamanho];
        int lidos = 0;
        while (lidos < tamanho)
        {
            int n = fluxo.Read(corpo, lidos, tamanho - lidos);
            if (n <= 0) break;
            lidos += n;
        }

        pedido.Corpo = Encoding.UTF8.GetString(corpo, 0, lidos);
        pedido.Cru = [.. cabecalho, .. corpo[..lidos]];
        return pedido;
    }

    static PedidoHttp DeCabecalho(string texto)
    {
        var linhas = texto.Replace("\r\n", "\n").Split('\n');
        var partes = linhas[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (partes.Length < 2) return null;

        var pedido = new PedidoHttp { Metodo = partes[0].ToUpperInvariant() };
        string alvo = partes[1];
        if (Uri.TryCreate(alvo, UriKind.Absolute, out var absoluto)) alvo = absoluto.PathAndQuery;
        int interrogacao = alvo.IndexOf('?');
        pedido.Caminho = interrogacao < 0 ? alvo : alvo[..interrogacao];
        pedido.Consulta = interrogacao < 0 ? "" : alvo[(interrogacao + 1)..];

        foreach (string linha in linhas.Skip(1))
        {
            int doisPontos = linha.IndexOf(':');
            if (doisPontos > 0) pedido.Cabecalhos[linha[..doisPontos].Trim()] = linha[(doisPontos + 1)..].Trim();
        }
        return pedido;
    }

    public static int FimDoCabecalho(byte[] b)
    {
        for (int i = 0; i + 3 < b.Length; i++)
            if (b[i] == 13 && b[i + 1] == 10 && b[i + 2] == 13 && b[i + 3] == 10) return i;
        return -1;
    }

    /// <summary>Pedido a partir dos bytes que um sketch escreveu num WiFiClient.</summary>
    public static PedidoHttp DeCru(byte[] cru)
    {
        int fim = FimDoCabecalho(cru);
        if (fim < 0) return null;
        var pedido = DeCabecalho(Encoding.Latin1.GetString(cru, 0, fim));
        if (pedido is null) return null;
        pedido.Corpo = Encoding.UTF8.GetString(cru, fim + 4, cru.Length - fim - 4);
        pedido.Cru = cru;
        return pedido;
    }

    public static PedidoHttp DeUri(string metodo, Uri uri, IDictionary<string, string> cabecalhos, string corpo)
    {
        byte[] bytesDoCorpo = corpo is null ? [] : TextoArduino.ParaBytes(corpo);
        var texto = new StringBuilder();
        texto.Append($"{metodo} {uri.PathAndQuery} HTTP/1.1\r\nHost: {uri.Authority}\r\n");
        foreach (var (nome, valor) in cabecalhos ?? new Dictionary<string, string>())
            texto.Append($"{nome}: {valor}\r\n");
        if (bytesDoCorpo.Length > 0 || metodo is "POST" or "PUT" or "PATCH")
            texto.Append($"Content-Length: {bytesDoCorpo.Length}\r\n");
        texto.Append("Connection: close\r\n\r\n");
        return DeCru([.. Encoding.UTF8.GetBytes(texto.ToString()), .. bytesDoCorpo]);
    }

    public static byte[] MontarResposta(int codigo, string tipo, string corpo,
        IEnumerable<KeyValuePair<string, string>> extras = null)
    {
        byte[] bytesDoCorpo = TextoArduino.ParaBytes(corpo ?? "");
        var texto = new StringBuilder($"HTTP/1.1 {codigo} {Motivo(codigo)}\r\n");
        if (!string.IsNullOrEmpty(tipo)) texto.Append($"Content-Type: {tipo}\r\n");
        foreach (var (nome, valor) in extras ?? []) texto.Append($"{nome}: {valor}\r\n");
        texto.Append($"Content-Length: {bytesDoCorpo.Length}\r\nConnection: close\r\n\r\n");
        return [.. Encoding.UTF8.GetBytes(texto.ToString()), .. bytesDoCorpo];
    }

    public static string Motivo(int codigo) => codigo switch
    {
        200 => "OK", 201 => "Created", 202 => "Accepted", 204 => "No Content",
        301 => "Moved Permanently", 302 => "Found", 303 => "See Other", 304 => "Not Modified",
        400 => "Bad Request", 401 => "Unauthorized", 403 => "Forbidden", 404 => "Not Found",
        405 => "Method Not Allowed", 500 => "Internal Server Error", 501 => "Not Implemented",
        503 => "Service Unavailable", 504 => "Gateway Timeout",
        _ => "Status",
    };

    public static RespostaHttp LerResposta(byte[] bytes)
    {
        bytes ??= [];
        int fim = FimDoCabecalho(bytes);
        string cabecalho = Encoding.Latin1.GetString(bytes, 0, fim < 0 ? bytes.Length : fim);
        var linhas = cabecalho.Replace("\r\n", "\n").Split('\n');
        var partes = linhas[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        int codigo = partes.Length > 1 && int.TryParse(partes[1], out int c) ? c : -1;

        var cabecalhos = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string linha in linhas.Skip(1))
        {
            int doisPontos = linha.IndexOf(':');
            if (doisPontos > 0) cabecalhos[linha[..doisPontos].Trim()] = linha[(doisPontos + 1)..].Trim();
        }

        string corpo = fim < 0 ? "" : Encoding.UTF8.GetString(bytes, fim + 4, bytes.Length - fim - 4);
        return new RespostaHttp(codigo, corpo, cabecalhos);
    }

    /// <summary>
    /// Os argumentos do pedido, na ordem: os da URL e, num POST de
    /// formulário, os do corpo. Um POST com outro tipo de corpo fica em
    /// <c>"plain"</c>, que é onde o WebServer do ESP32 o põe.
    /// </summary>
    public List<KeyValuePair<string, string>> Argumentos()
    {
        var lista = new List<KeyValuePair<string, string>>();
        void Juntar(string texto)
        {
            foreach (string par in texto.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                int igual = par.IndexOf('=');
                string nome = WebUtility.UrlDecode(igual < 0 ? par : par[..igual]);
                string valor = igual < 0 ? "" : WebUtility.UrlDecode(par[(igual + 1)..]);
                lista.Add(new(nome, valor));
            }
        }

        Juntar(Consulta);
        if (Corpo.Length > 0)
        {
            if (Cabecalhos.GetValueOrDefault("Content-Type", "").StartsWith(
                    "application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase))
                Juntar(Corpo);
            else lista.Add(new("plain", Corpo));
        }
        return lista;
    }
}

/// <summary>
/// Um servidor aberto por uma placa (<c>WebServer</c> ou <c>WiFiServer</c>).
///
/// Os pedidos entram numa fila e esperam o sketch chamar
/// <c>server.handleClient()</c> (ou <c>server.available()</c>). É o que
/// acontece na placa: sem essa chamada no <c>loop()</c>, o navegador
/// fica girando -- e aqui ele recebe uma página explicando isso.
///
/// Para o navegador do computador chegar, cada servidor ganha uma porta
/// em <c>localhost</c> (8080, 8081...): é a "ponte".
/// </summary>
public sealed class ServidorWeb
{
    readonly Rede rede;
    readonly Queue<PedidoHttp> fila = new();
    readonly Queue<TcpClient> conexoesDaPonte = new();
    TcpListener ponte;
    volatile bool fechado;

    internal ServidorWeb(Rede rede, Maquina dona, int porta, bool cru)
    {
        this.rede = rede;
        Dona = dona;
        Porta = porta;
        Cru = cru;
    }

    public Maquina Dona { get; }
    public int Porta { get; }

    /// <summary>WiFiServer: o sketch lê e escreve bytes. WebServer: rotas e send().</summary>
    public bool Cru { get; }

    /// <summary>
    /// O WiFiServer do sketch compilado: o navegador chega por uma conexão
    /// TCP de verdade, que fica aberta enquanto os dois lados quiserem -- o
    /// WebSocket precisa disso. (No interpretado, o pedido HTTP vem inteiro
    /// e a resposta sai quando o client fecha.)
    /// </summary>
    public bool TcpDeVerdade { get; init; }

    public string UrlLocal { get; private set; }

    internal void AbrirPonte()
    {
        // A porta da placa, quando ela nao e a 80 e esta livre: a pagina que
        // monta "ws://" + location.hostname + ":81" acha o WebSocket da
        // placa. A 80 fica na 8080 (no computador ela costuma ter dono).
        var candidatas = (Porta is > 0 and not 80 ? new[] { Porta } : []).Concat(Enumerable.Range(8080, 20));
        foreach (int porta in candidatas)
        {
            var ouvinte = new TcpListener(IPAddress.Loopback, porta);
            try { ouvinte.Start(); }
            catch (SocketException) { continue; }
            ponte = ouvinte;
            UrlLocal = $"http://localhost:{porta}/";
            break;
        }
        if (ponte is null) return;
        new Thread(Aceitar) { IsBackground = true, Name = "ponte http " + UrlLocal }.Start();
    }

    void Aceitar()
    {
        while (!fechado)
        {
            TcpClient cliente;
            try { cliente = ponte.AcceptTcpClient(); }
            catch { return; }
            ThreadPool.QueueUserWorkItem(_ => Atender(cliente));
        }
    }

    void Atender(TcpClient cliente)
    {
        if (TcpDeVerdade)
        {
            lock (fila) conexoesDaPonte.Enqueue(cliente);
            rede.Anotar(Dona, "servidor", Idioma.T("o navegador conectou"));
            return;
        }
        using (cliente)
        {
            try
            {
                cliente.ReceiveTimeout = 5000;
                cliente.SendTimeout = 5000;
                var fluxo = cliente.GetStream();
                var pedido = PedidoHttp.Ler(fluxo);
                if (pedido is null) return;
                pedido.Origem = "navegador";

                byte[] resposta = Entregar(pedido, 15000, null)
                    ?? PedidoHttp.MontarResposta(504, "text/html; charset=utf-8",
                        Idioma.T("<h3>A placa não respondeu</h3><p>O <code>loop()</code> está chamando <code>server.handleClient()</code>? Sem isso o pedido nunca é atendido.</p>"));
                fluxo.Write(resposta);
            }
            catch { }
        }
    }

    /// <summary>Põe o pedido na fila e espera a placa responder.</summary>
    public byte[] Entregar(PedidoHttp pedido, int tempoMs, Func<bool> desistir)
    {
        if (fechado)
            return PedidoHttp.MontarResposta(503, "text/plain; charset=utf-8", Idioma.T("o servidor foi fechado"));

        lock (fila) fila.Enqueue(pedido);
        rede.Anotar(Dona, "servidor", Idioma.T("{0} pediu {1} {2}", pedido.Origem, pedido.Metodo, pedido.CaminhoCompleto));
        return pedido.Esperar(tempoMs, () => fechado || desistir?.Invoke() == true);
    }

    public PedidoHttp Proximo()
    {
        lock (fila) return fila.Count > 0 ? fila.Dequeue() : null;
    }

    /// <summary>A próxima conexão do navegador (TcpDeVerdade), ou null.</summary>
    public TcpClient ProximaConexao()
    {
        lock (fila) return conexoesDaPonte.Count > 0 ? conexoesDaPonte.Dequeue() : null;
    }

    public int Esperando { get { lock (fila) return fila.Count + conexoesDaPonte.Count; } }

    internal void Fechar()
    {
        fechado = true;
        try { ponte?.Stop(); } catch { }
        lock (fila)
            while (conexoesDaPonte.Count > 0)
                try { conexoesDaPonte.Dequeue().Close(); } catch { }
        lock (fila)
            while (fila.Count > 0)
                fila.Dequeue().Responder(PedidoHttp.MontarResposta(503, "text/plain; charset=utf-8",
                    Idioma.T("o servidor foi fechado (a placa parou ou reiniciou)")));
    }
}

/// <summary>
/// Um <c>WiFiClient</c>: um lado de uma conversa TCP.
///
/// Três origens com a mesma cara para o sketch:
///
/// - **do servidor**: o <c>WiFiServer.available()</c> devolveu. Lê o
///   pedido que chegou; o que o sketch escreve vira a resposta ao
///   <c>stop()</c>.
/// - **para outra placa**: o sketch conectou num 10.0.0.x. O que ele
///   escreve é o pedido; quando o pedido está completo, a resposta da
///   outra placa passa a estar disponível para leitura.
/// - **real**: conexão TCP de verdade, com o computador ou a internet.
/// </summary>
public sealed class ConexaoTcp
{
    enum Tipo { DoServidor, ParaPlaca, Real }

    Tipo tipo;
    readonly List<byte> saida = [];
    readonly object trava = new();
    volatile bool fechada;

    PedidoHttp pedidoRecebido;
    ServidorWeb alvo;
    Maquina origem;
    bool pedidoEnviado;
    TcpClient tcp;
    Stream fluxo;
    volatile bool leitorTerminou;

    public FluxoDeEntrada Entrada { get; } = new();
    public string IpRemoto { get; private set; } = "0.0.0.0";
    public int PortaRemota { get; private set; }
    /// <summary>O certificado do servidor (DER), na conexão segura (o getPeerCertificate da placa).</summary>
    public byte[] CertificadoRemoto { get; private set; }

    ConexaoTcp() { }

    public static ConexaoTcp DoServidor(PedidoHttp pedido, string ipRemoto)
    {
        var conexao = new ConexaoTcp { tipo = Tipo.DoServidor, pedidoRecebido = pedido, IpRemoto = ipRemoto };
        conexao.Entrada.Adicionar(pedido.Cru);
        return conexao;
    }

    /// <summary>A conexão que o navegador abriu na ponte de um WiFiServer (TcpDeVerdade).</summary>
    internal static ConexaoTcp DaPonte(TcpClient tcp)
    {
        tcp.NoDelay = true;
        var conexao = new ConexaoTcp { tipo = Tipo.Real, tcp = tcp, fluxo = tcp.GetStream(), IpRemoto = "10.0.0.100" };
        if (tcp.Client.RemoteEndPoint is IPEndPoint fim) conexao.PortaRemota = fim.Port;
        new Thread(conexao.Bombear) { IsBackground = true, Name = "tcp do navegador" }.Start();
        return conexao;
    }

    internal static ConexaoTcp ParaPlaca(Maquina origem, ServidorWeb alvo, string host, int porta)
        => new() { tipo = Tipo.ParaPlaca, origem = origem, alvo = alvo, IpRemoto = host, PortaRemota = porta };

    internal static ConexaoTcp Real(Maquina origem, string host, int porta, bool segura, bool insegura, int tempoMs)
    {
        var tcp = ConexaoMqtt.ConectarRapido(host, porta, tempoMs, () => origem.Parar);
        Stream fluxo = tcp.GetStream();
        if (segura)
        {
            var ssl = insegura
                ? new SslStream(fluxo, false, (_, _, _, _) => true)
                : new SslStream(fluxo, false);
            ConexaoMqtt.Esperar(ssl.AuthenticateAsClientAsync(host), tempoMs, () => origem.Parar, tcp,
                                Idioma.T("negociar a conexão segura"));
            fluxo = ssl;
        }
        byte[] certificado = (fluxo as SslStream)?.RemoteCertificate?.GetRawCertData();

        var conexao = new ConexaoTcp
        {
            tipo = Tipo.Real, origem = origem, tcp = tcp, fluxo = fluxo, IpRemoto = host, PortaRemota = porta,
            CertificadoRemoto = certificado,
        };
        new Thread(conexao.Bombear) { IsBackground = true, Name = "tcp " + host }.Start();
        return conexao;
    }

    void Bombear()
    {
        var buffer = new byte[4096];
        try
        {
            int n;
            while ((n = fluxo.Read(buffer, 0, buffer.Length)) > 0) Entrada.Adicionar(buffer[..n]);
        }
        catch { }
        finally { leitorTerminou = true; }
    }

    /// <summary><c>client.connected()</c>.</summary>
    public bool Conectada
    {
        get
        {
            switch (tipo)
            {
                case Tipo.DoServidor:
                    // O pedido já chegou inteiro: depois de lido, do lado do
                    // navegador não há mais nada vindo.
                    return !fechada && Entrada.Disponivel > 0;
                case Tipo.ParaPlaca:
                    EnviarSePronto();
                    return !fechada && (!pedidoEnviado || Entrada.Disponivel > 0);
                default:
                    return !fechada && (!leitorTerminou || Entrada.Disponivel > 0);
            }
        }
    }

    public int Disponivel
    {
        get
        {
            if (tipo == Tipo.ParaPlaca) EnviarSePronto();
            return Entrada.Disponivel;
        }
    }

    public void Escrever(byte[] dados)
    {
        if (fechada || dados.Length == 0) return;
        if (tipo == Tipo.Real)
        {
            try { lock (trava) { fluxo.Write(dados); fluxo.Flush(); } }
            catch { Fechar(); }
            return;
        }
        lock (trava) saida.AddRange(dados);
    }

    /// <summary>Para outra placa: manda o pedido quando ele estiver completo.</summary>
    void EnviarSePronto()
    {
        if (pedidoEnviado || fechada) return;
        byte[] escrito;
        lock (trava) escrito = [.. saida];

        int fim = PedidoHttp.FimDoCabecalho(escrito);
        if (fim < 0) return;
        var pedido = PedidoHttp.DeCru(escrito);
        if (pedido is null) return;
        int esperado = int.TryParse(pedido.Cabecalhos.GetValueOrDefault("Content-Length"), out int n) ? n : 0;
        if (escrito.Length - fim - 4 < esperado) return;

        pedidoEnviado = true;
        pedido.Origem = origem.Nome;
        byte[] resposta = alvo.Entregar(pedido, 10000, () => origem.Parar);
        if (origem.Parar) throw new ExecucaoInterrompida();
        if (resposta is not null) Entrada.Adicionar(resposta);
    }

    /// <summary><c>client.stop()</c>.</summary>
    public void Fechar()
    {
        if (fechada) return;
        fechada = true;

        switch (tipo)
        {
            case Tipo.DoServidor:
            {
                byte[] escrito;
                lock (trava) escrito = [.. saida];
                pedidoRecebido.Responder(escrito.Length > 0
                    ? escrito
                    : PedidoHttp.MontarResposta(500, "text/plain; charset=utf-8",
                        Idioma.T("a placa fechou a conexão sem responder nada")));
                break;
            }
            case Tipo.Real:
                try { tcp?.Close(); } catch { }
                break;
        }
    }

    /// <summary>Conexão do servidor que o sketch esqueceu de fechar: responde com o que tiver.</summary>
    internal void FecharSeEsquecida()
    {
        if (tipo == Tipo.DoServidor && !fechada) Fechar();
    }
}

/// <summary>O <c>BluetoothSerial</c> de uma placa.</summary>
public sealed class DispositivoBt
{
    readonly Rede rede;
    readonly StringBuilder pendente = new();

    internal DispositivoBt(Rede rede, Maquina dona, string nome, bool mestre)
    {
        this.rede = rede;
        Dona = dona;
        Nome = nome;
        Mestre = mestre;
        uint h = 2166136261;
        foreach (char c in nome ?? "") h = (h ^ c) * 16777619;
        Endereco = $"24:0a:c4:{h >> 16 & 0xFF:x2}:{h >> 8 & 0xFF:x2}:{h & 0xFF:x2}";
    }

    public Maquina Dona { get; }
    public string Nome { get; }
    public bool Mestre { get; }

    /// <summary>O endereco Bluetooth (o da ESP32 pelo nome; o HC-05 poe o dele).</summary>
    public string Endereco { get; set; }

    /// <summary>O que chegou pelo Bluetooth e o sketch ainda não leu.</summary>
    public FluxoDeEntrada Entrada { get; } = new();

    /// <summary>A outra placa, quando o par é entre duas placas.</summary>
    public DispositivoBt Par { get; internal set; }

    /// <summary>A porta do computador por onde este Bluetooth é acessível.</summary>
    public PortaBluetooth PeloComputador { get; internal set; }

    /// <summary>O servico no radio Bluetooth do computador, para o celular de verdade.</summary>
    public ServicoBluetoothDoPc PeloRadio { get; internal set; }

    internal void AnotarDoRadio(string texto) => rede.Anotar(Dona, "bt", texto);

    /// <summary>Os eventos do esp_spp da ESP32 (os valores do esp_spp_api.h).</summary>
    public const int EventoInicio = 0, EventoAbriu = 26, EventoFechou = 27, EventoServidor = 28,
        EventoDados = 30, EventoEscreveu = 33, EventoClienteAbriu = 34;

    readonly Queue<(int Evento, string Quem)> eventos = new();

    /// <summary>
    /// Um evento para o register_callback() do sketch. Fica guardado e o
    /// interpretador entrega na thread do sketch -- quem conecta e o painel
    /// ou a porta do computador, e o sketch nao pode rodar na thread deles.
    /// </summary>
    internal void Avisar(int evento, string quem = null)
    {
        lock (eventos) if (eventos.Count < 200) eventos.Enqueue((evento, quem));
    }

    public bool ProximoEvento(out int evento, out string quem)
    {
        lock (eventos)
        {
            if (eventos.Count == 0) { evento = -1; quem = null; return false; }
            (evento, quem) = eventos.Dequeue();
            return true;
        }
    }

    /// <summary>
    /// Tem com quem conversar.
    ///
    /// O programa do computador conta como conexão: para o sketch, um
    /// terminal aberto em 127.0.0.1 é indistinguível de um celular
    /// pareado -- que é justamente o ponto.
    /// </summary>
    public bool TemConexao => Par is not null || rede.TemCelular(this)
                              || PeloComputador is { TemAlguem: true } || PeloRadio is { TemAlguem: true };

    /// <summary>
    /// <c>SerialBT.write/print</c>. Vai para a placa pareada e/ou para o
    /// celular do painel; o painel mostra linha a linha.
    /// </summary>
    public void Enviar(byte[] dados)
    {
        Par?.Entrada.Adicionar(dados);
        Par?.Avisar(EventoDados, Nome);
        PeloComputador?.Escrever(dados);
        PeloRadio?.Escrever(dados);
        if (rede.TemCelular(this)) rede.EntregarNoCelular(this, dados);
        if (TemConexao) Avisar(EventoEscreveu);

        if (!rede.TemCelular(this) && Par is null
            && PeloComputador is not { TemAlguem: true } && PeloRadio is not { TemAlguem: true }) return;

        string destino = Par is not null ? Par.Nome
                       : rede.TemCelular(this) ? "celular"
                       : PeloRadio is { TemAlguem: true } ? Idioma.T("celular (Bluetooth do computador)")
                       : $"127.0.0.1:{PeloComputador.Porta}";
        lock (pendente)
        {
            pendente.Append(TextoArduino.DeBytes(dados));
            string texto = pendente.ToString();
            int quebra;
            while ((quebra = texto.IndexOf('\n')) >= 0)
            {
                string linha = texto[..quebra].TrimEnd('\r');
                rede.Anotar(Dona, "bt", $"→ {destino}: {TextoArduino.Corrigir(linha)}");
                texto = texto[(quebra + 1)..];
            }
            if (texto.Length > 120)
            {
                rede.Anotar(Dona, "bt", $"→ {destino}: {TextoArduino.Corrigir(texto)}");
                texto = "";
            }
            pendente.Clear().Append(texto);
        }
    }
}
