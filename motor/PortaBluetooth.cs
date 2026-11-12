using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace Simulador;

/// <summary>
/// O Bluetooth da placa numa porta do computador.
///
/// Alpha 18: "assim como servidor wifi, bluetooth funcional com sktch de
/// teste". O servidor WiFi já era de verdade -- abre uma porta em
/// 127.0.0.1 e o navegador do próprio computador entra nele. O Bluetooth
/// não: ele só existia entre duas placas da bancada e o celular
/// desenhado no painel.
///
/// Emparelhar de verdade não é possível -- o rádio é do Windows, e
/// nenhuma placa simulada aparece na lista de dispositivos dele. O que
/// dá, e é o que os terminais de Bluetooth fazem por baixo, é o mesmo
/// que o WiFi faz aqui: o <c>BluetoothSerial</c> vira um socket. Quem
/// abrir 127.0.0.1 na porta anunciada conversa com o sketch byte a byte,
/// exatamente como um terminal SPP pareado conversaria.
///
/// A primeira porta é a 9000, e cada placa visível pega a seguinte.
/// </summary>
public sealed class PortaBluetooth
{
    public const int PrimeiraPorta = 9000;

    readonly DispositivoBt aparelho;
    readonly TcpListener ouvinte;
    readonly List<TcpClient> conexoes = [];
    readonly Lock trava = new();
    volatile bool fechada;

    public int Porta { get; }

    /// <summary>Tem alguém do computador conectado agora.</summary>
    public bool TemAlguem { get { lock (trava) return conexoes.Count > 0; } }

    PortaBluetooth(DispositivoBt aparelho, TcpListener ouvinte)
    {
        this.aparelho = aparelho;
        this.ouvinte = ouvinte;
        Porta = ((IPEndPoint)ouvinte.LocalEndpoint).Port;
    }

    /// <summary>
    /// Abre a primeira porta livre a partir da pedida, ou null.
    ///
    /// Tentar só uma porta daria errado no caso normal da aula: duas
    /// ESP32 na mesma bancada, as duas com SerialBT.begin().
    /// </summary>
    public static PortaBluetooth Abrir(DispositivoBt aparelho, int porta, int tentativas = 16)
    {
        for (int i = 0; i < tentativas; i++)
        {
            var ouvinte = new TcpListener(IPAddress.Loopback, porta + i);

            try { ouvinte.Start(); }
            catch (SocketException) { continue; }

            var aberta = new PortaBluetooth(aparelho, ouvinte);
            aparelho.PeloComputador = aberta;

            new Thread(aberta.Aceitar)
            {
                IsBackground = true,
                Name = "bluetooth " + aparelho.Nome,
            }.Start();

            return aberta;
        }

        return null;
    }

    void Aceitar()
    {
        while (!fechada)
        {
            TcpClient tcp;
            try { tcp = ouvinte.AcceptTcpClient(); }
            catch { return; }

            lock (trava) conexoes.Add(tcp);
            aparelho.Avisar(DispositivoBt.EventoClienteAbriu, "computador");

            new Thread(() => Atender(tcp))
            {
                IsBackground = true,
                Name = "bluetooth cliente",
            }.Start();
        }
    }

    /// <summary>Tudo o que o computador manda vira entrada do sketch.</summary>
    void Atender(TcpClient tcp)
    {
        try
        {
            using (tcp)
            {
                var fluxo = tcp.GetStream();
                var balde = new byte[512];

                while (!fechada)
                {
                    int quantos = fluxo.Read(balde, 0, balde.Length);
                    if (quantos <= 0) break;

                    aparelho.Entrada.Adicionar(balde[..quantos]);
                    aparelho.Avisar(DispositivoBt.EventoDados, "computador");
                }
            }
        }
        catch (IOException) { }
        catch (SocketException) { }
        catch (ObjectDisposedException) { }
        finally
        {
            bool saiu;
            lock (trava) saiu = conexoes.Remove(tcp);
            if (saiu && !fechada) aparelho.Avisar(DispositivoBt.EventoFechou, "computador");
        }
    }

    /// <summary>O que o sketch escreve no SerialBT sai por todas as conexões.</summary>
    public void Escrever(byte[] dados)
    {
        List<TcpClient> agora;
        lock (trava) agora = [.. conexoes];

        foreach (var tcp in agora)
        {
            try { tcp.GetStream().Write(dados, 0, dados.Length); }
            catch (IOException) { Largar(tcp); }
            catch (SocketException) { Largar(tcp); }
            catch (ObjectDisposedException) { Largar(tcp); }
        }
    }

    void Largar(TcpClient tcp)
    {
        lock (trava) conexoes.Remove(tcp);
        try { tcp.Close(); } catch (SocketException) { }
    }

    public void Fechar()
    {
        fechada = true;

        try { ouvinte.Stop(); } catch (SocketException) { }

        List<TcpClient> agora;
        lock (trava) { agora = [.. conexoes]; conexoes.Clear(); }

        foreach (var tcp in agora)
            try { tcp.Close(); } catch (SocketException) { }
    }
}

/// <summary>
/// O Bluetooth DE VERDADE do computador: um servico de porta serial (o SPP,
/// que e o que o BluetoothSerial da ESP32 fala) no radio do PC, com o nome
/// da placa.
///
/// A placa simulada nao tem radio, mas o computador tem. O celular pareado
/// com o computador acha o servico "CircuitSketch ESP32-Aula" e conecta
/// nele pelo app de terminal Bluetooth (o Serial Bluetooth Terminal, no
/// Android) -- e o que ele manda chega no SerialBT.read() do sketch, como
/// chegaria na ESP32 de verdade.
///
/// Sai por Winsock (AF_BTH e WSASetService), que esta em todo Windows: nada
/// de biblioteca de fora dentro do executavel. Sem radio, ou com ele
/// desligado, nada muda -- o painel e a porta do computador continuam -- e
/// <see cref="Motivo"/> diz por que.
/// </summary>
public sealed class ServicoBluetoothDoPc
{
    const int AF_BTH = 32, BTHPROTO_RFCOMM = 3, NS_BTH = 16;
    static readonly Guid PortaSerial = new("00001101-0000-1000-8000-00805F9B34FB");

    /// <summary>Por que o radio do computador nao pode ser usado agora, ou null.</summary>
    public static string Motivo { get; private set; }

    readonly DispositivoBt aparelho;
    readonly Socket ouvinte;
    readonly PontoBluetooth local;
    readonly List<Socket> conexoes = [];
    readonly Lock trava = new();
    volatile bool fechado;

    /// <summary>O nome que o celular ve na lista de servicos do computador.</summary>
    public string Nome { get; }

    /// <summary>O canal RFCOMM que o Windows deu para o servico.</summary>
    public int Canal => (int)local.Porta;

    public bool TemAlguem { get { lock (trava) return conexoes.Count > 0; } }

    ServicoBluetoothDoPc(DispositivoBt aparelho, Socket ouvinte, PontoBluetooth local, string nome)
    {
        this.aparelho = aparelho;
        this.ouvinte = ouvinte;
        this.local = local;
        Nome = nome;
    }

    /// <summary>Abre o servico da placa no radio do computador, ou devolve null e diz por que.</summary>
    public static ServicoBluetoothDoPc Abrir(DispositivoBt aparelho)
    {
        if (!OperatingSystem.IsWindows())
        {
            Motivo = Idioma.T("o Bluetooth do computador só é usado no Windows");
            return null;
        }

        Socket socket = null;
        try
        {
            socket = new Socket((AddressFamily)AF_BTH, SocketType.Stream, (ProtocolType)BTHPROTO_RFCOMM);
            socket.Bind(new PontoBluetooth(0, Guid.Empty, 0xFFFFFFFF));
            socket.Listen(2);
            var local = (PontoBluetooth)socket.LocalEndPoint;

            string nome = "CircuitSketch " + aparelho.Nome;
            if (Sdp.Mudar(nome, PortaSerial, local, registrar: true) != 0)
            {
                int erro = Marshal.GetLastPInvokeError();
                socket.Close();
                Motivo = Idioma.T("o Windows não aceitou o serviço de porta serial no Bluetooth (erro {0})", erro);
                return null;
            }

            var servico = new ServicoBluetoothDoPc(aparelho, socket, local, nome);
            new Thread(servico.Aceitar) { IsBackground = true, Name = "bluetooth do pc " + aparelho.Nome }.Start();
            Motivo = null;
            return servico;
        }
        catch (SocketException e)
        {
            socket?.Close();
            Motivo = e.SocketErrorCode switch
            {
                SocketError.NetworkDown =>
                    Idioma.T("o Bluetooth do computador está desligado (ligue em Configurações do Windows > Bluetooth e rode de novo)"),
                SocketError.AddressFamilyNotSupported or SocketError.ProtocolNotSupported
                    or SocketError.SocketNotSupported or SocketError.ProtocolFamilyNotSupported =>
                    Idioma.T("este computador não tem Bluetooth"),
                _ => Idioma.T("o Windows recusou o Bluetooth: {0}", e.Message),
            };
            return null;
        }
        catch (Exception e) when (e is PlatformNotSupportedException or NotSupportedException
                                      or ObjectDisposedException or DllNotFoundException
                                      or EntryPointNotFoundException)
        {
            socket?.Close();
            Motivo = Idioma.T("o Bluetooth do computador não pode ser usado: {0}", e.Message);
            return null;
        }
    }

    void Aceitar()
    {
        while (!fechado)
        {
            Socket cliente;
            try { cliente = ouvinte.Accept(); }
            catch { return; }

            lock (trava) conexoes.Add(cliente);
            aparelho.Avisar(DispositivoBt.EventoClienteAbriu, "celular");
            aparelho.AnotarDoRadio(Idioma.T("um celular conectou pelo Bluetooth do computador em \"{0}\"", Nome));

            new Thread(() => Atender(cliente)) { IsBackground = true, Name = "bluetooth do pc cliente" }.Start();
        }
    }

    void Atender(Socket cliente)
    {
        try
        {
            var balde = new byte[512];
            while (!fechado)
            {
                int quantos = cliente.Receive(balde);
                if (quantos <= 0) break;
                aparelho.Entrada.Adicionar(balde[..quantos]);
                aparelho.Avisar(DispositivoBt.EventoDados, "celular");
                aparelho.AnotarDoRadio(Idioma.T("celular → {0}", TextoArduino.Corrigir(
                    TextoArduino.DeBytes(balde[..quantos]).TrimEnd('\r', '\n'))));
            }
        }
        catch (SocketException) { }
        catch (ObjectDisposedException) { }
        finally
        {
            bool saiu;
            lock (trava) saiu = conexoes.Remove(cliente);
            try { cliente.Close(); } catch (SocketException) { }
            if (saiu && !fechado)
            {
                aparelho.Avisar(DispositivoBt.EventoFechou, "celular");
                aparelho.AnotarDoRadio(Idioma.T("o celular desconectou do Bluetooth do computador"));
            }
        }
    }

    /// <summary>O que o sketch escreve no SerialBT vai para o celular conectado.</summary>
    public void Escrever(byte[] dados)
    {
        List<Socket> agora;
        lock (trava) agora = [.. conexoes];

        foreach (var cliente in agora)
        {
            try { cliente.Send(dados); }
            catch (SocketException) { Largar(cliente); }
            catch (ObjectDisposedException) { Largar(cliente); }
        }
    }

    void Largar(Socket cliente)
    {
        lock (trava) conexoes.Remove(cliente);
        try { cliente.Close(); } catch (SocketException) { }
    }

    public void Fechar()
    {
        if (fechado) return;
        fechado = true;

        try { Sdp.Mudar(Nome, PortaSerial, local, registrar: false); } catch { }
        try { ouvinte.Close(); } catch (SocketException) { }

        List<Socket> agora;
        lock (trava) { agora = [.. conexoes]; conexoes.Clear(); }
        foreach (var cliente in agora)
            try { cliente.Close(); } catch (SocketException) { }
    }

    /// <summary>
    /// O endereco Bluetooth: o SOCKADDR_BTH do Winsock, 30 bytes
    /// (familia, endereco do radio, servico e canal).
    /// </summary>
    sealed class PontoBluetooth(ulong endereco, Guid servico, uint porta) : EndPoint
    {
        public ulong Endereco { get; } = endereco;
        public Guid Servico { get; } = servico;
        public uint Porta { get; } = porta;

        public override AddressFamily AddressFamily => (AddressFamily)AF_BTH;

        public byte[] Bytes()
        {
            var b = new byte[30];
            BitConverter.GetBytes((ushort)AF_BTH).CopyTo(b, 0);
            BitConverter.GetBytes(Endereco).CopyTo(b, 2);
            Servico.ToByteArray().CopyTo(b, 10);
            BitConverter.GetBytes(Porta).CopyTo(b, 26);
            return b;
        }

        public override SocketAddress Serialize()
        {
            var endereco = new SocketAddress((AddressFamily)AF_BTH, 30);
            var b = Bytes();
            for (int i = 2; i < 30; i++) endereco[i] = b[i];
            return endereco;
        }

        public override EndPoint Create(SocketAddress endereco)
        {
            var b = new byte[30];
            for (int i = 0; i < 30 && i < endereco.Size; i++) b[i] = endereco[i];
            var guid = new byte[16];
            Array.Copy(b, 10, guid, 0, 16);
            return new PontoBluetooth(BitConverter.ToUInt64(b, 2), new Guid(guid), BitConverter.ToUInt32(b, 26));
        }
    }

    /// <summary>O registro SDP: e ele que faz o celular achar o servico de porta serial.</summary>
    static class Sdp
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct WSAQUERYSET
        {
            public int dwSize;
            public string lpszServiceInstanceName;
            public IntPtr lpServiceClassId;
            public IntPtr lpVersion;
            public string lpszComment;
            public int dwNameSpace;
            public IntPtr lpNSProviderId;
            public string lpszContext;
            public int dwNumberOfProtocols;
            public IntPtr lpafpProtocols;
            public string lpszQueryString;
            public int dwNumberOfCsAddrs;
            public IntPtr lpcsaBuffer;
            public int dwOutputFlags;
            public IntPtr lpBlob;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct CSADDR_INFO
        {
            public IntPtr LocalAddr;
            public int LocalLen;
            public IntPtr RemoteAddr;
            public int RemoteLen;
            public int iSocketType;
            public int iProtocol;
        }

        [DllImport("ws2_32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "WSASetServiceW")]
        static extern int WSASetService(ref WSAQUERYSET qs, int operacao, int flags);

        /// <summary>RNRSERVICE_REGISTER (0) para anunciar, RNRSERVICE_DELETE (2) para tirar.</summary>
        public static int Mudar(string nome, Guid servico, PontoBluetooth local, bool registrar)
        {
            var bytes = local.Bytes();
            IntPtr endereco = Marshal.AllocHGlobal(30);
            IntPtr guid = Marshal.AllocHGlobal(16);
            IntPtr csaddr = Marshal.AllocHGlobal(Marshal.SizeOf<CSADDR_INFO>());
            try
            {
                Marshal.Copy(bytes, 0, endereco, 30);
                Marshal.Copy(servico.ToByteArray(), 0, guid, 16);
                Marshal.StructureToPtr(new CSADDR_INFO
                {
                    LocalAddr = endereco, LocalLen = 30,
                    RemoteAddr = endereco, RemoteLen = 30,
                    iSocketType = 1, iProtocol = BTHPROTO_RFCOMM,
                }, csaddr, false);

                var consulta = new WSAQUERYSET
                {
                    dwSize = Marshal.SizeOf<WSAQUERYSET>(),
                    lpszServiceInstanceName = nome,
                    lpServiceClassId = guid,
                    lpszComment = Idioma.T("porta serial de uma placa do CircuitSketch"),
                    dwNameSpace = NS_BTH,
                    dwNumberOfCsAddrs = 1,
                    lpcsaBuffer = csaddr,
                };
                return WSASetService(ref consulta, registrar ? 0 : 2, 0);
            }
            finally
            {
                Marshal.FreeHGlobal(endereco);
                Marshal.FreeHGlobal(guid);
                Marshal.FreeHGlobal(csaddr);
            }
        }
    }
}
