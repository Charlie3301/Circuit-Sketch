using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Simulador;

/// <summary>
/// A rede do modo nativo: o WiFi e o da bancada (o mesmo do WiFi.begin do
/// sketch), e o servidor HTTP do ESP-IDF ganha uma porta em localhost para o
/// navegador do computador -- com resposta em pedacos (chunked), que e o que
/// um fluxo de eventos (SSE) precisa.
/// </summary>
public sealed unsafe partial class ModuloNativo
{
    // =====================================================================
    //  WiFi
    // =====================================================================

    internal int WifiConectar(string ssid, string senha)
    {
        Maquina.Rede.ConectarWifi(Maquina, ssid, senha);
        return 0;
    }

    internal int WifiEstado()
    {
        var e = Maquina.Rede.Wifi(Maquina);
        if (!e.Ligado) return 0;
        if (e.Falha == 1) return 3;
        if (e.Falha == 4) return 4;
        return Maquina.Rede.WifiConectado(Maquina) ? 2 : 1;
    }

    internal int WifiIp(out byte[] ip, out byte[] mascara, out byte[] saida)
    {
        var e = Maquina.Rede.Wifi(Maquina);
        ip = IPAddress.TryParse(e.Ip, out var a) ? a.GetAddressBytes() : [0, 0, 0, 0];
        mascara = [255, 255, 255, 0];
        saida = [ip[0], ip[1], ip[2], 1];
        return WifiEstado() == 2 ? 1 : 0;
    }

    internal void WifiDesligar() => Maquina.Rede.DesligarWifi(Maquina);

    internal int WifiRssi() => -48;

    internal byte[] WifiMac()
    {
        var e = Maquina.Rede.Wifi(Maquina);
        var partes = (e.Mac ?? "").Split(':');
        var mac = new byte[6];
        for (int i = 0; i < 6 && i < partes.Length; i++)
            mac[i] = byte.TryParse(partes[i], System.Globalization.NumberStyles.HexNumber, null, out byte b) ? b : (byte)0;
        return mac;
    }

    // =====================================================================
    //  servidor HTTP
    // =====================================================================

    sealed class ServidorNativo
    {
        public int Porta;
        public TcpListener Ponte;
        public string Url;
        public readonly Queue<PedidoNativo> Fila = new();
        public volatile bool Fechado;
        public readonly List<TcpClient> Clientes = [];
    }

    sealed class PedidoNativo
    {
        public TcpClient Cliente;
        public NetworkStream Fluxo;
        public string Metodo = "GET", Caminho = "/", Consulta;
        public readonly Dictionary<string, string> Cabecalhos = new(StringComparer.OrdinalIgnoreCase);
        public byte[] Corpo = [];
        public int CorpoLido;
        public string Status = "200 OK", Tipo = "text/html";
        public readonly List<(string, string)> Extras = [];
        public bool Enviado, EmPedacos, Terminado, Caiu, Fechar;
        public readonly ManualResetEventSlim Fim = new(false);
    }

    readonly List<ServidorNativo> servidores = [];

    internal long HttpIniciar(int porta)
    {
        var s = new ServidorNativo { Porta = porta <= 0 ? 80 : porta };
        for (int p = 8080; p < 8100; p++)
        {
            var ouvinte = new TcpListener(IPAddress.Loopback, p);
            try { ouvinte.Start(); }
            catch (SocketException) { continue; }
            s.Ponte = ouvinte;
            s.Url = $"http://localhost:{p}/";
            break;
        }
        if (s.Ponte is null)
        {
            Maquina.Rede.Anotar(Maquina, "servidor", Idioma.T("servidor HTTP: as portas 8080 a 8099 do computador estao ocupadas"));
            return -1;
        }
        lock (servidores) servidores.Add(s);
        long id = Guardar(s);
        Maquina.Rede.Anotar(Maquina, "servidor", Idioma.T("servidor na porta {0} (httpd) · abra no navegador: {1}", s.Porta, s.Url));
        if (!Maquina.Rede.Wifi(Maquina).Ligado)
            Maquina.Rede.Anotar(Maquina, "aviso", Idioma.T("o servidor abriu, mas a placa ainda nao ligou o WiFi"));
        new Thread(() => Aceitar(s)) { IsBackground = true, Name = "httpd nativo " + s.Url }.Start();
        return id;
    }

    void Aceitar(ServidorNativo s)
    {
        while (!s.Fechado && !encerrando)
        {
            TcpClient cliente;
            try { cliente = s.Ponte.AcceptTcpClient(); }
            catch { return; }
            lock (s.Clientes) s.Clientes.Add(cliente);
            new Thread(() => Atender(s, cliente)) { IsBackground = true, Name = "conexao httpd" }.Start();
        }
    }

    /// <summary>Uma conexao: le um pedido, espera a placa responder, e le o proximo (keep-alive).</summary>
    void Atender(ServidorNativo s, TcpClient cliente)
    {
        try
        {
            cliente.NoDelay = true;
            var fluxo = cliente.GetStream();
            while (!s.Fechado && !encerrando)
            {
                var p = LerPedido(fluxo);
                if (p is null) break;
                p.Cliente = cliente;
                p.Fluxo = fluxo;
                lock (s)
                {
                    if (s.Fila.Count > 32) break;
                    s.Fila.Enqueue(p);
                    Monitor.PulseAll(s);
                }
                Maquina.Rede.Anotar(Maquina, "servidor", Idioma.T("navegador pediu {0} {1}{2}", p.Metodo, p.Caminho, (p.Consulta is null ? "" : "?" + p.Consulta)));
                while (!p.Fim.Wait(100))
                    if (s.Fechado || encerrando) return;
                if (p.Caiu || p.Fechar || !p.Terminado) break;
            }
        }
        catch { }
        finally
        {
            lock (s.Clientes) s.Clientes.Remove(cliente);
            try { cliente.Close(); } catch { }
        }
    }

    static PedidoNativo LerPedido(NetworkStream fluxo)
    {
        var cabecalho = new List<byte>();
        while (true)
        {
            int b = fluxo.ReadByte();
            if (b < 0) return null;
            cabecalho.Add((byte)b);
            int n = cabecalho.Count;
            if (n >= 4 && cabecalho[n - 4] == 13 && cabecalho[n - 3] == 10 && cabecalho[n - 2] == 13 && cabecalho[n - 1] == 10) break;
            if (n > 65536) return null;
        }
        var linhas = Encoding.Latin1.GetString([.. cabecalho]).Split("\r\n");
        var partes = linhas[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (partes.Length < 2) return null;
        var p = new PedidoNativo { Metodo = partes[0].ToUpperInvariant() };
        string alvo = partes[1];
        int q = alvo.IndexOf('?');
        p.Caminho = q < 0 ? alvo : alvo[..q];
        p.Consulta = q < 0 ? null : alvo[(q + 1)..];
        foreach (string linha in linhas.Skip(1))
        {
            int dp = linha.IndexOf(':');
            if (dp > 0) p.Cabecalhos[linha[..dp].Trim()] = linha[(dp + 1)..].Trim();
        }
        if (int.TryParse(p.Cabecalhos.GetValueOrDefault("Content-Length"), out int tamanho) && tamanho > 0)
        {
            tamanho = Math.Min(tamanho, 1 << 22);
            p.Corpo = new byte[tamanho];
            int lidos = 0;
            while (lidos < tamanho)
            {
                int n = fluxo.Read(p.Corpo, lidos, tamanho - lidos);
                if (n <= 0) break;
                lidos += n;
            }
            if (lidos < tamanho) Array.Resize(ref p.Corpo, lidos);
        }
        p.Fechar = p.Cabecalhos.GetValueOrDefault("Connection", "").Equals("close", StringComparison.OrdinalIgnoreCase);
        return p;
    }

    internal void HttpParar(long id)
    {
        var s = Obj<ServidorNativo>(id);
        if (s is null) return;
        FecharServidor(s);
        Apagar(id);
    }

    void FecharServidor(ServidorNativo s)
    {
        s.Fechado = true;
        try { s.Ponte?.Stop(); } catch { }
        lock (s) Monitor.PulseAll(s);
        lock (s.Clientes)
            foreach (var c in s.Clientes)
                try { c.Close(); } catch { }
        lock (servidores) servidores.Remove(s);
    }

    static readonly Dictionary<string, int> Metodos = new()
    {
        ["DELETE"] = 0, ["GET"] = 1, ["HEAD"] = 2, ["POST"] = 3, ["PUT"] = 4, ["CONNECT"] = 5,
        ["OPTIONS"] = 6, ["TRACE"] = 7, ["PATCH"] = 28,
    };

    internal long HttpEsperar(long id, long espera, IntPtr info)
    {
        var s = Obj<ServidorNativo>(id);
        if (s is null || s.Fechado) return -1;
        PedidoNativo p;
        lock (s)
        {
            int r = s.Fila.Count > 0 ? 1 : Esperar(s, () => s.Fila.Count > 0 || s.Fechado, espera);
            if (r < 0 || s.Fechado) return -1;
            if (r == 0 || s.Fila.Count == 0) return 0;
            p = s.Fila.Dequeue();
        }

        // cs_pedido_http: int metodo; (4 de folga) int64 tamanho; char uri[520]
        byte* d = (byte*)info;
        *(int*)d = Metodos.GetValueOrDefault(p.Metodo, 1);
        *(long*)(d + 8) = p.Corpo.Length;
        var uri = Encoding.UTF8.GetBytes(p.Caminho + (p.Consulta is null ? "" : "?" + p.Consulta));
        int k = Math.Min(uri.Length, 512);
        uri.AsSpan(0, k).CopyTo(new Span<byte>(d + 16, k));
        d[16 + k] = 0;
        return Guardar(p);
    }

    internal int HttpStatus(long req, string status)
    {
        var p = Obj<PedidoNativo>(req);
        if (p is null) return -1;
        p.Status = status ?? "200 OK";
        return 0;
    }

    internal int HttpTipo(long req, string tipo)
    {
        var p = Obj<PedidoNativo>(req);
        if (p is null) return -1;
        p.Tipo = tipo ?? "text/html";
        return 0;
    }

    internal int HttpCabecalho(long req, string nome, string valor)
    {
        var p = Obj<PedidoNativo>(req);
        if (p is null || nome is null) return -1;
        p.Extras.Add((nome, valor ?? ""));
        return 0;
    }

    void MandarCabecalho(PedidoNativo p, long tamanho)
    {
        var t = new StringBuilder();
        t.Append("HTTP/1.1 ").Append(p.Status).Append("\r\n");
        t.Append("Content-Type: ").Append(p.Tipo).Append("\r\n");
        if (tamanho >= 0) t.Append("Content-Length: ").Append(tamanho).Append("\r\n");
        else t.Append("Transfer-Encoding: chunked\r\n");
        foreach (var (nome, valor) in p.Extras) t.Append(nome).Append(": ").Append(valor).Append("\r\n");
        t.Append("\r\n");
        p.Fluxo.Write(Encoding.Latin1.GetBytes(t.ToString()));
        p.Enviado = true;
        p.EmPedacos = tamanho < 0;
    }

    internal int HttpEnviar(long req, ReadOnlySpan<byte> corpo)
    {
        var p = Obj<PedidoNativo>(req);
        if (p is null || p.Caiu) return -1;
        try
        {
            if (!p.Enviado) MandarCabecalho(p, corpo.Length);
            p.Fluxo.Write(corpo);
            p.Fluxo.Flush();
            p.Terminado = true;
            return 0;
        }
        catch
        {
            p.Caiu = true;
            return -1;
        }
    }

    internal int HttpPedaco(long req, ReadOnlySpan<byte> pedaco)
    {
        var p = Obj<PedidoNativo>(req);
        if (p is null || p.Caiu) return -1;
        try
        {
            if (!p.Enviado) MandarCabecalho(p, -1);
            if (pedaco.Length == 0)
            {
                p.Fluxo.Write("0\r\n\r\n"u8);
                p.Terminado = true;
            }
            else
            {
                p.Fluxo.Write(Encoding.ASCII.GetBytes(pedaco.Length.ToString("x") + "\r\n"));
                p.Fluxo.Write(pedaco);
                p.Fluxo.Write("\r\n"u8);
            }
            p.Fluxo.Flush();
            return 0;
        }
        catch
        {
            p.Caiu = true;
            return -1;
        }
    }

    internal string HttpConsulta(long req) => Obj<PedidoNativo>(req)?.Consulta;

    internal string HttpLerCabecalho(long req, string nome)
    {
        var p = Obj<PedidoNativo>(req);
        return p is not null && nome is not null && p.Cabecalhos.TryGetValue(nome, out var v) ? v : null;
    }

    internal int HttpLerCorpo(long req, IntPtr buf, int n)
    {
        var p = Obj<PedidoNativo>(req);
        if (p is null) return -1;
        int k = Math.Min(n, p.Corpo.Length - p.CorpoLido);
        if (k <= 0) return 0;
        p.Corpo.AsSpan(p.CorpoLido, k).CopyTo(new Span<byte>((void*)buf, k));
        p.CorpoLido += k;
        return k;
    }

    /// <summary>O tratador voltou (ou o assincrono terminou): fecha a resposta e solta a conexao.</summary>
    internal void HttpFim(long req, int resultado)
    {
        var p = Obj<PedidoNativo>(req);
        if (p is null) return;
        if (resultado != 0) p.Fechar = true;   // ESP_FAIL: o httpd fecha a conexao
        if (!p.Terminado) p.Fechar = true;     // resposta pela metade: nao da para reaproveitar
        p.Fim.Set();
        Apagar(req);
    }

    void PararServidores()
    {
        List<ServidorNativo> todos;
        lock (servidores) todos = [.. servidores];
        foreach (var s in todos) FecharServidor(s);
    }
}
