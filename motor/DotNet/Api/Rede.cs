// Rede.cs -- o WiFi, o HTTP e o MQTT do nanoFramework (nanoFramework.Networking,
// System.Device.Wifi, System.Net.Http e nanoFramework.M2Mqtt), com os nomes e as
// assinaturas de verdade, sobre a rede do simulador: o WiFi da bancada, o
// HTTPClient e a PubSubClient (o broker interno ou o de fora). O HttpClient e
// este, e nao o do .NET, para a placa precisar estar no WiFi como na bancada
// (ver CompiladorCSharp: o System.Net.Http do .NET fica de fora).
using System;
using System.Collections;
using System.IO;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CircuitSketch;

namespace System.Device.Wifi
{
    public enum WifiReconnectionKind { Automatic, Manual }

    public enum WifiConnectionStatus { AccessRevoked, InvalidCredential, NetworkNotAvailable, Success, Timeout, UnspecifiedFailure, UnsupportedAuthenticationProtocol }

    public enum WifiNetworkKind { Any, Infrastructure, Adhoc }

    public sealed class WifiConnectionResult
    {
        internal WifiConnectionResult(WifiConnectionStatus s) => ConnectionStatus = s;
        public WifiConnectionStatus ConnectionStatus { get; }
    }

    public sealed class WifiAvailableNetwork
    {
        public string Ssid { get; internal set; }
        public string Bsid { get; internal set; }
        public double NetworkRssiInDecibelMilliwatts { get; internal set; }
        public byte SignalBars { get; internal set; }
        public WifiNetworkKind NetworkKind => WifiNetworkKind.Infrastructure;
        public int ChannelCenterFrequencyInKilohertz { get; internal set; }
    }

    public sealed class WifiNetworkReport
    {
        internal WifiNetworkReport(WifiAvailableNetwork[] redes) => AvailableNetworks = redes;
        public WifiAvailableNetwork[] AvailableNetworks { get; }
    }

    public delegate void AvailableNetworksChangedEventHandler(WifiAdapter sender, object e);

    /// <summary>O radio WiFi da placa (o unico, como na ESP32).</summary>
    public class WifiAdapter : IDisposable
    {
        static readonly WifiAdapter unico = new WifiAdapter();

        public static WifiAdapter[] FindAllAdapters() => new[] { unico };

        public event AvailableNetworksChangedEventHandler AvailableNetworksChanged;

        WifiNetworkReport ultimo = new WifiNetworkReport(Array.Empty<WifiAvailableNetwork>());

        public void ScanAsync()
        {
            long n = Ponte.I("WiFi", "scanNetworks");
            var redes = new WifiAvailableNetwork[Math.Max(0, n)];
            for (int i = 0; i < redes.Length; i++)
            {
                long rssi = Ponte.I("WiFi", "RSSI", i);
                redes[i] = new WifiAvailableNetwork
                {
                    Ssid = Ponte.T("WiFi", "SSID", i),
                    Bsid = Ponte.T("WiFi", "BSSIDstr", i),
                    NetworkRssiInDecibelMilliwatts = rssi,
                    SignalBars = (byte)(rssi > -55 ? 4 : rssi > -67 ? 3 : rssi > -80 ? 2 : 1),
                    ChannelCenterFrequencyInKilohertz = 2407000 + 5000 * (int)Ponte.I("WiFi", "channel", i),
                };
            }
            ultimo = new WifiNetworkReport(redes);
            AvailableNetworksChanged?.Invoke(this, null);
        }

        public WifiNetworkReport NetworkReport => ultimo;

        public WifiConnectionResult Connect(string ssid, WifiReconnectionKind reconnectionKind, string passwordCredential)
        {
            Ponte.C("WiFi", "setAutoReconnect", reconnectionKind == WifiReconnectionKind.Automatic);
            Ponte.C("WiFi", "begin", ssid, passwordCredential ?? "");
            for (int i = 0; i < 100 && Ponte.I("WiFi", "status") != 3; i++) Ponte.Dormir(100);
            return new WifiConnectionResult(Ponte.I("WiFi", "status") == 3 ? WifiConnectionStatus.Success
                                            : Ponte.I("WiFi", "status") == 4 ? WifiConnectionStatus.InvalidCredential : WifiConnectionStatus.NetworkNotAvailable);
        }

        public WifiConnectionResult Connect(WifiAvailableNetwork availableNetwork, WifiReconnectionKind reconnectionKind, string passwordCredential)
            => Connect(availableNetwork.Ssid, reconnectionKind, passwordCredential);

        public void Disconnect() => Ponte.C("WiFi", "disconnect");

        public int NetworkInterface => 0;

        public void Dispose() { }
    }
}

namespace nanoFramework.Networking
{
    public enum NetworkHelperStatus
    {
        None,
        Started,
        NetworkIsReady,
        FailedNoNetworkInterface,
        TokenExpiredWaitingIPAddress,
        TokenExpiredWaitingDateTime,
        ErrorConnetingToWiFiWhileScanning,
        ExceptionOccurred,
        Reconnecting,
    }

    /// <summary>O IP fixo do ConnectFixAddress.</summary>
    public class IPConfiguration
    {
        public IPConfiguration(string ipAddress, string ipSubnetMask, string ipGatewayAddress, string[] ipDns = null)
        {
            IPAddress = ipAddress;
            IPSubnetMask = ipSubnetMask;
            IPGatewayAddress = ipGatewayAddress;
            IPDns = ipDns;
        }

        public string IPAddress { get; }
        public string IPSubnetMask { get; }
        public string IPGatewayAddress { get; }
        public string[] IPDns { get; }
    }

    /// <summary>O ajudante do WiFi: conecta, espera o IP (e o relogio, se pedido) e diz como foi.</summary>
    public static class WifiNetworkHelper
    {
        static readonly ManualResetEvent pronta = new ManualResetEvent(false);
        static string ssidSalvo, senhaSalva;

        public static ManualResetEvent NetworkReady => pronta;
        public static NetworkHelperStatus Status { get; private set; } = NetworkHelperStatus.None;
        public static Exception HelperException { get; internal set; }
        public static System.Device.Wifi.WifiAdapter WifiAdapter => System.Device.Wifi.WifiAdapter.FindAllAdapters()[0];

        public static void SetupNetworkHelper(bool requiresDateTime = false) => Status = NetworkHelperStatus.Started;

        public static void SetupNetworkHelper(string ssid, string password, System.Device.Wifi.WifiReconnectionKind reconnectionKind = System.Device.Wifi.WifiReconnectionKind.Automatic,
                                              bool requiresDateTime = false, int wifiAdapterId = 0)
        {
            Status = NetworkHelperStatus.Started;
            new Thread(() => Conectar(ssid, password, null, reconnectionKind, requiresDateTime, CancellationToken.None)) { IsBackground = true }.Start();
        }

        public static void Disconnect()
        {
            Ponte.C("WiFi", "disconnect");
            pronta.Reset();
            Status = NetworkHelperStatus.None;
        }

        public static bool ConnectDhcp(string ssid, string password, System.Device.Wifi.WifiReconnectionKind reconnectionKind = System.Device.Wifi.WifiReconnectionKind.Automatic,
                                       bool requiresDateTime = false, int wifiAdapterId = 0, CancellationToken token = default)
            => Conectar(ssid, password, null, reconnectionKind, requiresDateTime, token);

        public static bool ConnectFixAddress(string ssid, string password, IPConfiguration ipConfiguration,
                                             System.Device.Wifi.WifiReconnectionKind reconnectionKind = System.Device.Wifi.WifiReconnectionKind.Automatic,
                                             bool requiresDateTime = false, int WifiAdapter = 0, CancellationToken token = default)
            => Conectar(ssid, password, ipConfiguration, reconnectionKind, requiresDateTime, token);

        public static bool ScanAndConnectDhcp(string ssid, string password, System.Device.Wifi.WifiReconnectionKind reconnectionKind = System.Device.Wifi.WifiReconnectionKind.Automatic,
                                              bool requiresDateTime = false, int WifiAdapter = 0, CancellationToken token = default)
            => Conectar(ssid, password, null, reconnectionKind, requiresDateTime, token);

        public static bool Reconnect(bool requiresDateTime = false, int wifiAdapterId = 0, CancellationToken token = default)
            => ssidSalvo is not null && Conectar(ssidSalvo, senhaSalva, null, System.Device.Wifi.WifiReconnectionKind.Automatic, requiresDateTime, token);

        public static void Reset()
        {
            pronta.Reset();
            Status = NetworkHelperStatus.None;
            HelperException = null;
        }

        static bool Conectar(string ssid, string senha, IPConfiguration ip, System.Device.Wifi.WifiReconnectionKind religar, bool relogio, CancellationToken token)
        {
            try
            {
                ssidSalvo = ssid;
                senhaSalva = senha;
                Status = NetworkHelperStatus.Started;
                if (ip is not null) Ponte.C("WiFi", "config", ip.IPAddress, ip.IPGatewayAddress, ip.IPSubnetMask);
                Ponte.C("WiFi", "setAutoReconnect", religar == System.Device.Wifi.WifiReconnectionKind.Automatic);
                Ponte.C("WiFi", "begin", ssid, senha ?? "");
                // Sem token o original espera para sempre; aqui, como o exemplo de 60 s.
                var fim = DateTime.UtcNow.AddSeconds(60);
                while (Ponte.I("WiFi", "status") != 3)
                {
                    if (token.IsCancellationRequested || (!token.CanBeCanceled && DateTime.UtcNow > fim))
                    {
                        Status = NetworkHelperStatus.TokenExpiredWaitingIPAddress;
                        return false;
                    }
                    Ponte.Dormir(100);
                }
                if (relogio)
                {
                    Ponte.C(null, "configTime", 0L, 0L, "pool.ntp.org");
                    while (DateTime.UtcNow.Year < 2018)
                    {
                        if (token.IsCancellationRequested)
                        {
                            Status = NetworkHelperStatus.TokenExpiredWaitingDateTime;
                            return false;
                        }
                        Ponte.Dormir(100);
                    }
                }
                Status = NetworkHelperStatus.NetworkIsReady;
                pronta.Set();
                return true;
            }
            catch (ParadaDaPlaca) { throw; }
            catch (Exception e)
            {
                HelperException = e;
                Status = NetworkHelperStatus.ExceptionOccurred;
                return false;
            }
        }
    }
}

namespace System.Net.Http
{
    public class HttpRequestException : Exception
    {
        public HttpRequestException() : base() { }
        public HttpRequestException(string message) : base(message) { }
        public HttpRequestException(string message, Exception inner) : base(message, inner) { }
    }

    public enum HttpCompletionOption { ResponseContentRead, ResponseHeadersRead }

    public class HttpMethod
    {
        HttpMethod(string m) => Method = m;
        public string Method { get; }
        public static HttpMethod Get { get; } = new HttpMethod("GET");
        public static HttpMethod Post { get; } = new HttpMethod("POST");
        public static HttpMethod Put { get; } = new HttpMethod("PUT");
        public static HttpMethod Delete { get; } = new HttpMethod("DELETE");
        public static HttpMethod Patch { get; } = new HttpMethod("PATCH");
        public static HttpMethod Head { get; } = new HttpMethod("HEAD");
        public static HttpMethod Options { get; } = new HttpMethod("OPTIONS");
        public override string ToString() => Method;
    }

    /// <summary>Os cabecalhos (o nome e o valor, como chegam).</summary>
    public class HttpHeaders : IEnumerable
    {
        internal readonly Hashtable Itens = new Hashtable();
        public void Add(string name, string value) => Itens[name] = value;
        public bool Remove(string name)
        {
            bool tinha = Itens.Contains(name);
            Itens.Remove(name);
            return tinha;
        }
        public void Clear() => Itens.Clear();
        public bool Contains(string name) => Itens.Contains(name);
        public string[] GetValues(string name) => Itens[name] is string v ? new[] { v } : Array.Empty<string>();
        public IEnumerator GetEnumerator() => Itens.GetEnumerator();
    }

    public sealed class HttpRequestHeaders : HttpHeaders { }
    public sealed class HttpResponseHeaders : HttpHeaders { }
    public sealed class HttpContentHeaders : HttpHeaders
    {
        public string ContentType
        {
            get => Itens["Content-Type"] as string;
            set => Itens["Content-Type"] = value;
        }
    }

    public abstract class HttpContent : IDisposable
    {
        public HttpContentHeaders Headers { get; } = new HttpContentHeaders();
        internal abstract byte[] Bytes { get; }
        public string ReadAsString() => Encoding.UTF8.GetString(Bytes);
        public byte[] ReadAsByteArray() => Bytes;
        public Stream ReadAsStream() => new MemoryStream(Bytes);
        public void CopyTo(Stream stream) => stream.Write(Bytes, 0, Bytes.Length);
        public void LoadIntoBuffer() { }
        public Task<string> ReadAsStringAsync() => Task.FromResult(ReadAsString());
        public Task<string> ReadAsStringAsync(CancellationToken token) => Task.FromResult(ReadAsString());
        public Task<byte[]> ReadAsByteArrayAsync() => Task.FromResult(Bytes);
        public Task<Stream> ReadAsStreamAsync() => Task.FromResult(ReadAsStream());
        public virtual void Dispose() { }
    }

    public class ByteArrayContent : HttpContent
    {
        readonly byte[] dados;
        public ByteArrayContent(byte[] content) => dados = content ?? throw new ArgumentNullException(nameof(content));
        public ByteArrayContent(byte[] content, int offset, int count)
        {
            dados = new byte[count];
            Array.Copy(content, offset, dados, 0, count);
        }
        internal override byte[] Bytes => dados;
    }

    public class StringContent : ByteArrayContent
    {
        public StringContent(string content) : this(content, Encoding.UTF8, "text/plain") { }
        public StringContent(string content, Encoding encoding) : this(content, encoding, "text/plain") { }
        public StringContent(string content, Encoding encoding, string mediaType)
            : base((encoding ?? Encoding.UTF8).GetBytes(content ?? throw new ArgumentNullException(nameof(content))))
            => Headers.ContentType = (mediaType ?? "text/plain") + "; charset=" + ((encoding ?? Encoding.UTF8).WebName);
    }

    sealed class ConteudoDaResposta : HttpContent
    {
        readonly byte[] dados;
        public ConteudoDaResposta(byte[] d) => dados = d;
        internal override byte[] Bytes => dados;
    }

    public class HttpRequestMessage : IDisposable
    {
        public HttpRequestMessage() : this(HttpMethod.Get, (string)null) { }
        public HttpRequestMessage(HttpMethod method, string requestUri)
        {
            Method = method;
            RequestUri = requestUri is null ? null : new Uri(requestUri, UriKind.RelativeOrAbsolute);
        }
        public HttpRequestMessage(HttpMethod method, Uri requestUri)
        {
            Method = method;
            RequestUri = requestUri;
        }
        public HttpMethod Method { get; set; }
        public Uri RequestUri { get; set; }
        public HttpContent Content { get; set; }
        public HttpRequestHeaders Headers { get; } = new HttpRequestHeaders();
        public Version Version { get; set; } = new Version(1, 1);
        public void Dispose() => Content?.Dispose();
    }

    public class HttpResponseMessage : IDisposable
    {
        public HttpResponseMessage() : this(HttpStatusCode.OK) { }
        public HttpResponseMessage(HttpStatusCode statusCode)
        {
            StatusCode = statusCode;
            Content = new ConteudoDaResposta(Array.Empty<byte>());
        }
        public HttpStatusCode StatusCode { get; set; }
        public bool IsSuccessStatusCode => (int)StatusCode >= 200 && (int)StatusCode <= 299;
        public string ReasonPhrase { get; set; }
        public HttpContent Content { get; set; }
        public HttpResponseHeaders Headers { get; } = new HttpResponseHeaders();
        public HttpRequestMessage RequestMessage { get; set; }
        public Version Version { get; set; } = new Version(1, 1);
        public HttpResponseMessage EnsureSuccessStatusCode()
        {
            if (!IsSuccessStatusCode) throw new HttpRequestException($"Response status code does not indicate success: {(int)StatusCode} ({ReasonPhrase}).");
            return this;
        }
        public void Dispose() => Content?.Dispose();
    }

    public abstract class HttpMessageInvoker : IDisposable
    {
        public abstract HttpResponseMessage Send(HttpRequestMessage request);
        public virtual void Dispose() { }
    }

    public enum SslVerification { NoVerification, VerifyPeer, CertificateRequired, VerifyClientOnce }

    /// <summary>O cliente HTTP da placa: cada pedido vai pelo HTTPClient do simulador (o WiFi tem que estar conectado).</summary>
    public partial class HttpClient : HttpMessageInvoker
    {
        const HttpCompletionOption DefaultCompletionOption = HttpCompletionOption.ResponseContentRead;
        HttpRequestHeaders cabecalhos;

        public HttpClient() { }
        public HttpClient(HttpMessageInvoker handler) { }
        public HttpClient(HttpMessageInvoker handler, bool disposeHandler) { }

        public HttpRequestHeaders DefaultRequestHeaders => cabecalhos ??= new HttpRequestHeaders();
        public Uri BaseAddress { get; set; }
        public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(100);
        public System.Security.Authentication.SslProtocols SslProtocols { get; set; } = System.Security.Authentication.SslProtocols.Tls12;
        public SslVerification SslVerification { get; set; } = SslVerification.CertificateRequired;
        public X509Certificate HttpsAuthentCert { get; set; }

        public HttpResponseMessage Get(string requestUri) => Send(new HttpRequestMessage(HttpMethod.Get, requestUri), DefaultCompletionOption);
        public HttpResponseMessage Get(string requestUri, HttpCompletionOption completionOption) => Send(new HttpRequestMessage(HttpMethod.Get, requestUri), completionOption);
        public HttpResponseMessage Post(string requestUri, HttpContent content) => Send(new HttpRequestMessage(HttpMethod.Post, requestUri) { Content = content }, DefaultCompletionOption);
        public HttpResponseMessage Put(string requestUri, HttpContent content) => Send(new HttpRequestMessage(HttpMethod.Put, requestUri) { Content = content }, DefaultCompletionOption);
        public HttpResponseMessage Patch(string requestUri, HttpContent content) => Send(new HttpRequestMessage(HttpMethod.Patch, requestUri) { Content = content }, DefaultCompletionOption);
        public HttpResponseMessage Delete(string requestUri) => Send(new HttpRequestMessage(HttpMethod.Delete, requestUri), DefaultCompletionOption);

        public string GetString(string requestUri)
        {
            using var r = Get(requestUri);
            r.EnsureSuccessStatusCode();
            return r.Content.ReadAsString();
        }

        public byte[] GetByteArray(string requestUri)
        {
            using var r = Get(requestUri);
            r.EnsureSuccessStatusCode();
            return r.Content.ReadAsByteArray();
        }

        public Stream GetStream(string requestUri) => new MemoryStream(GetByteArray(requestUri));

        // O nanoFramework so tem os sincronos; os Async do .NET fazem o mesmo, ja terminados.
        public Task<HttpResponseMessage> GetAsync(string requestUri) => Task.FromResult(Get(requestUri));
        public Task<HttpResponseMessage> PostAsync(string requestUri, HttpContent content) => Task.FromResult(Post(requestUri, content));
        public Task<HttpResponseMessage> PutAsync(string requestUri, HttpContent content) => Task.FromResult(Put(requestUri, content));
        public Task<HttpResponseMessage> DeleteAsync(string requestUri) => Task.FromResult(Delete(requestUri));
        public Task<string> GetStringAsync(string requestUri) => Task.FromResult(GetString(requestUri));
        public Task<byte[]> GetByteArrayAsync(string requestUri) => Task.FromResult(GetByteArray(requestUri));
        public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request) => Task.FromResult(Send(request));

        public override HttpResponseMessage Send(HttpRequestMessage request) => Send(request, DefaultCompletionOption);

        public HttpResponseMessage Send(HttpRequestMessage request, HttpCompletionOption completionOption)
        {
            if (request is null) throw new ArgumentNullException(nameof(request));
            Uri uri = request.RequestUri;
            if (uri is null || !uri.IsAbsoluteUri)
            {
                if (BaseAddress is null) throw new InvalidOperationException("An invalid request URI was provided. Either the request URI must be an absolute URI or BaseAddress must be set.");
                uri = uri is null ? BaseAddress : new Uri(BaseAddress, uri);
            }

            string http = Ponte.T(null, "__criar", "HTTPClient");
            try
            {
                Ponte.C(http, "setTimeout", (long)Timeout.TotalMilliseconds);
                if (Ponte.I(http, "begin", uri.AbsoluteUri) == 0) throw new HttpRequestException("the request URI is not valid: " + uri);
                foreach (DictionaryEntry c in DefaultRequestHeaders.Itens) Ponte.C(http, "addHeader", (string)c.Key, (string)c.Value);
                foreach (DictionaryEntry c in request.Headers.Itens) Ponte.C(http, "addHeader", (string)c.Key, (string)c.Value);
                string corpo = null;
                if (request.Content is { } conteudo)
                {
                    foreach (DictionaryEntry c in conteudo.Headers.Itens) Ponte.C(http, "addHeader", (string)c.Key, (string)c.Value);
                    corpo = conteudo.ReadAsString();
                }
                long codigo = corpo is null ? Ponte.I(http, "sendRequest", request.Method.Method) : Ponte.I(http, "sendRequest", request.Method.Method, corpo);
                // O codigo negativo e a conexao que nao saiu (sem WiFi, o servidor nao responde): no
                // nanoFramework e a excecao do socket.
                if (codigo < 0) throw new HttpRequestException(Ponte.T(http, "errorToString", codigo));
                var resposta = new HttpResponseMessage((HttpStatusCode)codigo)
                {
                    RequestMessage = request,
                    Content = new ConteudoDaResposta(Encoding.UTF8.GetBytes(Ponte.T(http, "getString"))),
                };
                return resposta;
            }
            finally
            {
                Ponte.C(http, "end");
            }
        }
    }
}

namespace nanoFramework.M2Mqtt.Messages
{
    public enum MqttQoSLevel : byte { AtMostOnce = 0x00, AtLeastOnce = 0x01, ExactlyOnce = 0x02, GrantedFailure = 0x80 }

    public enum MqttReasonCode : byte
    {
        Success = 0x00,
        NormalDisconnection = 0x00,
        GrantedQoS0 = 0x00,
        GrantedQoS1 = 0x01,
        GrantedQoS2 = 0x02,
        DisconnectWithWillMessage = 0x04,
        NoMatchingSubscribers = 0x10,
        NoSubscriptionExisted = 0x11,
        ContinueAuthentication = 0x18,
        ReAuthenticate = 0x19,
        UnspecifiedError = 0x80,
        MalformedPacket = 0x81,
        ProtocolError = 0x82,
        ImplementationSpecificError = 0x83,
        UnsupportedProtocolVersion = 0x84,
        ClientIdentifierNotValid = 0x85,
        BadUserNameOrPassword = 0x86,
        NotAuthorized = 0x87,
        ServerUnavailable = 0x88,
        ServerBusy = 0x89,
        Banned = 0x8A,
        ServeShuttingDown = 0x8B,
        BadAuthenticationMethod = 0x8C,
        KeepAliveTimeout = 0x8D,
        SessionTakenOver = 0x8E,
        TopicFilterInvalid = 0x8F,
        TopicNameInvalid = 0x90,
        PacketIdentifierInUse = 0x91,
        PacketIdentifierNotFound = 0x92,
        ReceiveMaximumExceeded = 0x93,
        TopicAliasInvalid = 0x94,
        PacketTooLarge = 0x95,
        MessageRateTooHigh = 0x96,
        QuotaExceeded = 0x97,
        AdministrativeAction = 0x98,
        PayloadFormatInvalid = 0x99,
        RetainNotSupported = 0x9A,
        QoSNotSupported = 0x9B,
        UseAnotherServer = 0x9C,
        ServerMoved = 0x9D,
        SharedSubscriptionsNotSupported = 0x9E,
        ConnectionRateExceeded = 0x9F,
        MaximumConnectTime = 0xA0,
        SubscriptionIdentifiersNotSupported = 0xA1,
        WildcardSubscriptionsNotSupported = 0xA2,
        ConnectionRefusedUnacceptableProtocolVersion = 0x01,
        ConnectionRefusedIdentifierRejected = 0x02,
        ConnectionRefusedServerUnavailable = 0x03,
        ConnectionRefusedBadUserNameOrPassword = 0x04,
        ConnectionRefusedNotAuthorized = 0x05,
    }

    public class MqttMsgPublishEventArgs : EventArgs
    {
        public MqttMsgPublishEventArgs(string topic, byte[] message, bool dupFlag, MqttQoSLevel qosLevel, bool retain)
        {
            Topic = topic;
            Message = message;
            DupFlag = dupFlag;
            QosLevel = qosLevel;
            Retain = retain;
        }
        public string Topic { get; internal set; }
        public byte[] Message { get; internal set; }
        public bool DupFlag { get; internal set; }
        public MqttQoSLevel QosLevel { get; internal set; }
        public bool Retain { get; internal set; }
    }

    public class MqttMsgPublishedEventArgs : EventArgs
    {
        public MqttMsgPublishedEventArgs(ushort messageId, bool isPublished)
        {
            MessageId = messageId;
            IsPublished = isPublished;
        }
        public ushort MessageId { get; internal set; }
        public bool IsPublished { get; internal set; }
    }

    public class MqttMsgSubscribedEventArgs : EventArgs
    {
        public MqttMsgSubscribedEventArgs(ushort messageId, MqttQoSLevel[] grantedQosLevels)
        {
            MessageId = messageId;
            GrantedQoSLevels = grantedQosLevels;
        }
        public ushort MessageId { get; internal set; }
        public MqttQoSLevel[] GrantedQoSLevels { get; internal set; }
    }

    public class MqttMsgUnsubscribedEventArgs : EventArgs
    {
        public MqttMsgUnsubscribedEventArgs(ushort messageId) => MessageId = messageId;
        public ushort MessageId { get; internal set; }
    }
}

namespace nanoFramework.M2Mqtt
{
    using nanoFramework.M2Mqtt.Messages;

    public enum MqttSslProtocols { None, SSLv3, TLSv1_0, TLSv1_1, TLSv1_2 }

    public enum MqttProtocolVersion : byte { Unknown = 0x00, Version_3_1 = 0x03, Version_3_1_1 = 0x04, Version_5 = 0x05 }

    public class MqttCommunicationException : Exception
    {
        public MqttCommunicationException() { }
        public MqttCommunicationException(Exception e) : base("", e) { }
    }

    public interface IMqttClient : IDisposable
    {
        delegate void MqttMsgPublishEventHandler(object sender, MqttMsgPublishEventArgs e);
        delegate void MqttMsgPublishedEventHandler(object sender, MqttMsgPublishedEventArgs e);
        delegate void MqttMsgSubscribedEventHandler(object sender, MqttMsgSubscribedEventArgs e);
        delegate void MqttMsgUnsubscribedEventHandler(object sender, MqttMsgUnsubscribedEventArgs e);
        delegate void ConnectionClosedEventHandler(object sender, EventArgs e);

        bool IsConnected { get; }
        MqttReasonCode Connect(string clientId);
        void Disconnect();
        ushort Subscribe(string[] topics, MqttQoSLevel[] qosLevels);
        ushort Unsubscribe(string[] topics);
        ushort Publish(string topic, byte[] message);
        void Close();
        event MqttMsgPublishEventHandler MqttMsgPublishReceived;
        event ConnectionClosedEventHandler ConnectionClosed;
    }

    /// <summary>
    /// O cliente MQTT do nanoFramework sobre a PubSubClient do simulador. Como no original, uma
    /// thread recebe as mensagens e levanta o MqttMsgPublishReceived.
    /// </summary>
    public class MqttClient : IMqttClient
    {
        readonly string wifi, mqtt;
        ushort proximoId = 1;
        Thread recebendo;
        volatile bool fechando;

        public event IMqttClient.MqttMsgPublishEventHandler MqttMsgPublishReceived;
        public event IMqttClient.MqttMsgPublishedEventHandler MqttMsgPublished;
        public event IMqttClient.MqttMsgSubscribedEventHandler MqttMsgSubscribed;
        public event IMqttClient.MqttMsgUnsubscribedEventHandler MqttMsgUnsubscribed;
        public event IMqttClient.ConnectionClosedEventHandler ConnectionClosed;

        public bool IsConnected { get; private set; }
        public string ClientId { get; private set; }
        public bool CleanSession { get; private set; }
        public MqttProtocolVersion ProtocolVersion { get; set; } = MqttProtocolVersion.Version_3_1_1;

        public MqttClient(string brokerHostName) : this(brokerHostName, 1883, false, null, null, MqttSslProtocols.None) { }

        public MqttClient(string brokerHostName, int brokerPort, bool secure, X509Certificate caCert, X509Certificate clientCert, MqttSslProtocols sslProtocol)
        {
            wifi = Ponte.T(null, "__criar", secure ? "WiFiClientSecure" : "WiFiClient");
            if (secure && caCert is null) Ponte.C(wifi, "setInsecure");
            mqtt = Ponte.Texto(Ponte.CN(null, "__criar", new object[] { "PubSubClient", wifi }, new[] { false, true }));
            Ponte.C(mqtt, "setServer", brokerHostName, brokerPort);
            Ponte.CN(mqtt, "setCallback", new object[] { Ponte.Registrar(new Action<string, byte[], long>(Chegou)) }, new[] { true });
        }

        void Chegou(string topico, byte[] dados, long tamanho)
        {
            var mensagem = new byte[Math.Min(dados?.Length ?? 0, (int)tamanho)];
            if (dados is not null) Array.Copy(dados, mensagem, mensagem.Length);
            MqttMsgPublishReceived?.Invoke(this, new MqttMsgPublishEventArgs(topico, mensagem, false, MqttQoSLevel.AtMostOnce, false));
        }

        public MqttReasonCode Connect(string clientId) => Connect(clientId, null, null, false, MqttQoSLevel.AtMostOnce, false, null, null, true, 60);
        public MqttReasonCode Connect(string clientId, bool cleanSession) => Connect(clientId, null, null, false, MqttQoSLevel.AtMostOnce, false, null, null, cleanSession, 60);
        public MqttReasonCode Connect(string clientId, string username, string password) => Connect(clientId, username, password, false, MqttQoSLevel.AtMostOnce, false, null, null, true, 60);
        public MqttReasonCode Connect(string clientId, string username, string password, bool cleanSession, ushort keepAlivePeriod)
            => Connect(clientId, username, password, false, MqttQoSLevel.AtMostOnce, false, null, null, cleanSession, keepAlivePeriod);

        public MqttReasonCode Connect(string clientId, string username, string password, bool willRetain, MqttQoSLevel willQosLevel, bool willFlag,
                                      string willTopic, string willMessage, bool cleanSession, ushort keepAlivePeriod)
        {
            ClientId = clientId;
            CleanSession = cleanSession;
            Ponte.C(mqtt, "setKeepAlive", (long)keepAlivePeriod);
            bool ok = willFlag
                ? Ponte.I(mqtt, "connect", clientId, username ?? "", password ?? "", willTopic ?? "", (long)willQosLevel, willRetain, willMessage ?? "") != 0
                : Ponte.I(mqtt, "connect", clientId, username ?? "", password ?? "") != 0;
            if (!ok)
            {
                // O state() da PubSubClient: 1 versao, 2 id, 3 servidor, 4 senha, 5 sem permissao; o resto, a rede.
                long estado = Ponte.I(mqtt, "state");
                if (estado >= 1 && estado <= 5) throw new MqttConnectionException("Connection refused", (MqttReasonCode)estado);
                throw new MqttCommunicationException();
            }
            IsConnected = true;
            fechando = false;
            recebendo = new Thread(Receber) { IsBackground = true };
            recebendo.Start();
            return MqttReasonCode.Success;
        }

        void Receber()
        {
            while (!fechando && !Ponte.Saindo)
            {
                bool ainda;
                try { ainda = Ponte.I(mqtt, "loop") != 0; }
                catch (Exception) when (fechando || Ponte.Saindo) { return; }
                if (!ainda)
                {
                    IsConnected = false;
                    ConnectionClosed?.Invoke(this, EventArgs.Empty);
                    return;
                }
                Thread.Sleep(10);
            }
        }

        ushort Id() => proximoId++ == 0 ? proximoId++ : (ushort)(proximoId - 1);

        public ushort Subscribe(string[] topics, MqttQoSLevel[] qosLevels)
        {
            ushort id = Id();
            var concedidos = new MqttQoSLevel[topics.Length];
            for (int i = 0; i < topics.Length; i++)
            {
                long qos = qosLevels is not null && i < qosLevels.Length ? Math.Min((long)qosLevels[i], 1) : 0;
                concedidos[i] = Ponte.I(mqtt, "subscribe", topics[i], qos) != 0 ? (MqttQoSLevel)qos : MqttQoSLevel.GrantedFailure;
            }
            MqttMsgSubscribed?.Invoke(this, new MqttMsgSubscribedEventArgs(id, concedidos));
            return id;
        }

        public ushort Unsubscribe(string[] topics)
        {
            ushort id = Id();
            foreach (string t in topics) Ponte.C(mqtt, "unsubscribe", t);
            MqttMsgUnsubscribed?.Invoke(this, new MqttMsgUnsubscribedEventArgs(id));
            return id;
        }

        public ushort Publish(string topic, byte[] message) => Publish(topic, message, null, null, MqttQoSLevel.AtMostOnce, false);
        public ushort Publish(string topic, byte[] message, string contentType) => Publish(topic, message, contentType, null, MqttQoSLevel.AtMostOnce, false);
        public ushort Publish(string topic, byte[] message, string contentType, ArrayList userProperties) => Publish(topic, message, contentType, userProperties, MqttQoSLevel.AtMostOnce, false);
        public ushort Publish(string topic, byte[] message, string contentType, ArrayList userProperties, bool retain) => Publish(topic, message, contentType, userProperties, MqttQoSLevel.AtMostOnce, retain);

        public ushort Publish(string topic, byte[] message, string contentType, ArrayList userProperties, MqttQoSLevel qosLevel, bool retain)
        {
            ushort id = Id();
            bool saiu = Ponte.I(mqtt, "publish", topic, Encoding.UTF8.GetString(message ?? Array.Empty<byte>()), retain) != 0;
            MqttMsgPublished?.Invoke(this, new MqttMsgPublishedEventArgs(id, saiu));
            return id;
        }

        public void Disconnect()
        {
            fechando = true;
            Ponte.C(mqtt, "disconnect");
            IsConnected = false;
        }

        public void Close() => Disconnect();

        public void Dispose()
        {
            if (IsConnected) Disconnect();
        }
    }

    public class MqttConnectionException : Exception
    {
        public MqttConnectionException(string message, MqttReasonCode code) : base(message) => ReturnCode = code;
        public MqttReasonCode ReturnCode { get; }
    }
}
