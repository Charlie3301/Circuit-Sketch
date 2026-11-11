using System.Text;

namespace Simulador;

/// <summary>
/// O Bluetooth LE de uma placa (a biblioteca BLE do core ESP32): o que ela
/// anuncia, o servidor GATT com os servicos e as caracteristicas, e o
/// celular conectado nele. Quem faz o papel do celular e a janela do celular
/// (ou o teste): procura, conecta, le, escreve e assina as notificacoes.
/// </summary>
public sealed class RadioBle
{
    readonly Queue<EventoBle> eventos = new();
    readonly object trava = new();

    internal RadioBle(Maquina dona, string nome, string endereco, bool nimble)
    {
        Dona = dona;
        Nome = nome;
        Endereco = endereco;
        NimBle = nimble;
    }

    public Maquina Dona { get; }
    public string Nome { get; internal set; }

    /// <summary>O endereco do Bluetooth: o MAC da placa com o ultimo byte + 2, como no chip.</summary>
    public string Endereco { get; }

    /// <summary>
    /// A pilha: NimBLE (S3, C3, C6, H2) ou Bluedroid (a ESP32). Muda o que o
    /// notify() exige: no NimBLE o celular tem que ter assinado; no
    /// Bluedroid, sem o descritor 2902 ele manda assim mesmo.
    /// </summary>
    public bool NimBle { get; }

    public bool Anunciando { get; internal set; }
    public List<string> UuidsAnunciados { get; } = [];

    /// <summary>Os dados do fabricante no anuncio (o iBeacon), em hexadecimal.</summary>
    public string FabricanteHex { get; internal set; } = "";

    /// <summary>Os dados de servico no anuncio (o Eddystone no FEAA): o UUID e os bytes em hexadecimal.</summary>
    public string ServicoUuid { get; internal set; } = "";
    public string ServicoHex { get; internal set; } = "";

    /// <summary>
    /// O anuncio byte a byte (as estruturas tamanho/tipo/dados), quando quem anuncia o montou assim
    /// (o gap_advertise do MicroPython); nulo: quem varre monta pelos campos de cima.
    /// </summary>
    public byte[] CargaDoAnuncio { get; internal set; }

    /// <summary>A resposta a varredura ativa (o resp_data do gap_advertise), se houver.</summary>
    public byte[] RespostaDoAnuncio { get; internal set; }

    /// <summary>O anuncio aceita conexao (o connectable do gap_advertise). De fabrica, sim.</summary>
    public bool Conectavel { get; internal set; } = true;

    /// <summary>O advertiseOnDisconnect(): volta a anunciar sozinho quando o celular sai. De fabrica, nao.</summary>
    public bool ReanunciarAoDesconectar { get; internal set; }

    public bool TemServidor { get; internal set; }
    public List<ServicoBle> Servicos { get; } = [];
    public bool CelularConectado { get; internal set; }

    /// <summary>As placas conectadas neste servidor GATT como cliente (central).</summary>
    public List<ConexaoBle> Clientes { get; } = [];

    /// <summary>As conexoes em que esta placa e o cliente: ela conectou no servidor de outra.</summary>
    public List<ConexaoBle> ComoCliente { get; } = [];

    /// <summary>Quantos estao conectados neste servidor: o celular e as placas.</summary>
    public int Conectados
    {
        get { lock (Clientes) return Clientes.Count + (CelularConectado ? 1 : 0); }
    }

    /// <summary>A biblioteca que a placa usa: a ArduinoBLE tem as regras dela para notificar.</summary>
    public bool ArduinoBle { get; internal set; }

    /// <summary>A potencia do sinal que o celular ve: fixa, perto de quem esta na mesa.</summary>
    public int Rssi => -48 - Math.Abs(Endereco.GetHashCode() % 17);

    internal void Avisar(EventoBle evento)
    {
        lock (trava) if (eventos.Count < 500) eventos.Enqueue(evento);
    }

    public bool ProximoEvento(out EventoBle evento)
    {
        lock (trava) return eventos.TryDequeue(out evento);
    }

    /// <summary>
    /// As caracteristicas dos servicos ja iniciados (o que o celular
    /// enxerga). A trava e a propria lista, a mesma que o sketch usa ao criar
    /// -- o celular le de outra thread.
    /// </summary>
    public IEnumerable<CaracteristicaBle> Visiveis()
    {
        var lista = new List<CaracteristicaBle>();
        foreach (var s in ServicosIniciados())
            lock (s.Caracteristicas) lista.AddRange(s.Caracteristicas);
        return lista;
    }

    /// <summary>Os servicos que o pService->start() ja ligou, numa copia.</summary>
    public List<ServicoBle> ServicosIniciados()
    {
        lock (Servicos) return [.. Servicos.Where(s => s.Iniciado)];
    }
}

public sealed class ServicoBle
{
    internal ServicoBle(RadioBle radio, string uuid)
    {
        Radio = radio;
        Uuid = uuid;
    }

    public RadioBle Radio { get; }
    public string Uuid { get; }

    /// <summary>O pService->start(): so depois dele o celular enxerga o servico.</summary>
    public bool Iniciado { get; internal set; }

    public List<CaracteristicaBle> Caracteristicas { get; } = [];
}

public sealed class CaracteristicaBle
{
    readonly object trava = new();
    byte[] valor = [];

    internal CaracteristicaBle(ServicoBle servico, string uuid, bool le, bool escreve, bool escreveSemResposta,
                               bool notifica, bool indica, int propriedades)
    {
        Servico = servico;
        Uuid = uuid;
        Le = le;
        Escreve = escreve;
        EscreveSemResposta = escreveSemResposta;
        Notifica = notifica;
        Indica = indica;
        Propriedades = propriedades;
    }

    public ServicoBle Servico { get; }
    public string Uuid { get; }

    /// <summary>As propriedades como o sketch passou (os bits da pilha do chip).</summary>
    public int Propriedades { get; }

    public bool Le { get; }
    public bool Escreve { get; }
    public bool EscreveSemResposta { get; }
    public bool Notifica { get; }
    public bool Indica { get; }

    /// <summary>O descritor 2902 (CCCD) que o sketch pos com addDescriptor(new BLE2902()).</summary>
    public bool Tem2902 { get; internal set; }

    /// <summary>O texto do descritor 2901 (setDescription), se houver.</summary>
    public string Descricao { get; internal set; }

    /// <summary>O celular ligou as notificacoes (escreveu no CCCD).</summary>
    public bool Assinada { get; internal set; }

    /// <summary>Os descritores que o programa registrou alem do CCCD (o bluetooth do MicroPython).</summary>
    public List<DescritorBle> Descritores { get; } = [];

    public byte[] Valor
    {
        get { lock (trava) return (byte[])valor.Clone(); }
        internal set { lock (trava) valor = value is null ? [] : (byte[])value.Clone(); }
    }

    /// <summary>O nome que um app de BLE mostra para os UUID conhecidos.</summary>
    public string NomeConhecido => Ble.NomeDoUuid(Uuid);
}

/// <summary>Um descritor de uma caracteristica: o UUID, o que ele deixa fazer e o valor.</summary>
public sealed class DescritorBle
{
    readonly object trava = new();
    byte[] valor = [];

    internal DescritorBle(CaracteristicaBle dona, string uuid, bool le, bool escreve)
    {
        Dona = dona;
        Uuid = uuid;
        Le = le;
        Escreve = escreve;
    }

    public CaracteristicaBle Dona { get; }
    public string Uuid { get; }
    public bool Le { get; }
    public bool Escreve { get; }

    public byte[] Valor
    {
        get { lock (trava) return (byte[])valor.Clone(); }
        internal set { lock (trava) valor = value is null ? [] : (byte[])value.Clone(); }
    }
}

public enum TipoDeEventoBle
{
    Conectou, Desconectou, Escreveu, Leu, Assinou,

    /// <summary>No cliente: chegou uma notificacao (ou indicacao) do servidor.</summary>
    Notificou,

    /// <summary>No cliente: a conexao acabou (o servidor desconectou, reiniciou ou parou).</summary>
    Caiu,
}

/// <summary>
/// Um evento do radio. <paramref name="Conexao"/>: a placa do outro lado
/// (nulo: o celular). <paramref name="Dados"/>: o que chegou numa notificacao
/// ou numa escrita. <paramref name="Descritor"/>: a escrita foi num descritor.
/// </summary>
public sealed record EventoBle(TipoDeEventoBle Tipo, CaracteristicaBle Caracteristica = null, int Valor = 0,
                               ConexaoBle Conexao = null, byte[] Dados = null, DescritorBle Descritor = null);

/// <summary>
/// Uma placa conectada no servidor GATT de outra, como cliente (central): o
/// BLEClient da ESP32, o BLEDevice da ArduinoBLE depois do connect(). Cada
/// conexao tem o seu CCCD -- uma placa assinar nao assina para o celular.
/// </summary>
public sealed class ConexaoBle
{
    readonly HashSet<CaracteristicaBle> assinadas = [];

    internal ConexaoBle(RadioBle servidor, RadioBle cliente)
    {
        Servidor = servidor;
        Cliente = cliente;
    }

    public RadioBle Servidor { get; }
    public RadioBle Cliente { get; }
    public bool Ativa { get; internal set; } = true;

    /// <summary>O MTU combinado (o setMTU do cliente): 23 de fabrica.</summary>
    public int Mtu { get; internal set; } = 23;

    public bool Assinou(CaracteristicaBle c)
    {
        lock (assinadas) return assinadas.Contains(c);
    }

    internal void Assinar(CaracteristicaBle c, bool ligar)
    {
        lock (assinadas)
        {
            if (ligar) assinadas.Add(c);
            else assinadas.Remove(c);
        }
    }
}

/// <summary>As contas de UUID e os nomes que os apps de BLE mostram.</summary>
public static class Ble
{
    public const string ServicoUart = "6e400001-b5a3-f393-e0a9-e50e24dcca9e";
    public const string UartRx = "6e400002-b5a3-f393-e0a9-e50e24dcca9e";
    public const string UartTx = "6e400003-b5a3-f393-e0a9-e50e24dcca9e";

    /// <summary>
    /// O UUID do jeito que o BLEUUID::toString() escreve: minusculo, com os
    /// hifens, e o de 16 ou 32 bits dentro do UUID base do Bluetooth.
    /// </summary>
    public static string Normalizar(string uuid)
    {
        string limpo = (uuid ?? "").Trim().ToLowerInvariant();
        if (limpo.StartsWith("0x")) limpo = limpo[2..];
        string soHex = limpo.Replace("-", "");
        if (soHex.Length is 4 or 8 && soHex.All(Uri.IsHexDigit))
            return soHex.PadLeft(8, '0') + "-0000-1000-8000-00805f9b34fb";
        if (soHex.Length == 32 && soHex.All(Uri.IsHexDigit))
            return $"{soHex[..8]}-{soHex[8..12]}-{soHex[12..16]}-{soHex[16..20]}-{soHex[20..]}";
        return limpo;
    }

    /// <summary>O UUID de 16 bits, se ele for um do Bluetooth SIG; -1 se nao for.</summary>
    public static int Curto(string uuid)
    {
        string n = Normalizar(uuid);
        return n.EndsWith("-0000-1000-8000-00805f9b34fb") && n.StartsWith("0000")
            ? Convert.ToInt32(n[4..8], 16) : -1;
    }

    public static string NomeDoUuid(string uuid) => Normalizar(uuid) switch
    {
        ServicoUart => "Nordic UART",
        UartRx => "UART RX (o celular escreve)",
        UartTx => "UART TX (a placa notifica)",
        _ => Curto(uuid) switch
        {
            0x1800 => "Generic Access",
            0x1801 => "Generic Attribute",
            0x180A => "Device Information",
            0x180F => "Battery Service",
            0x2A19 => "Battery Level",
            0x181A => "Environmental Sensing",
            0x2A6E => "Temperature",
            0x2A6F => "Humidity",
            0x180D => "Heart Rate",
            0x2A37 => "Heart Rate Measurement",
            0x2A29 => "Manufacturer Name",
            0x2A24 => "Model Number",
            _ => null,
        },
    };

    /// <summary>O valor como os apps mostram: o texto, quando e texto, e os bytes em hexa.</summary>
    public static string Mostrar(byte[] valor)
    {
        if (valor is null || valor.Length == 0) return "(vazio)";
        string hexa = "0x" + Convert.ToHexString(valor);
        // Texto e UTF-8 valido sem controle (o "25°C" tem bytes >= 0x80); o
        // fim de linha aparece escrito, para o valor caber numa linha so.
        string texto;
        try { texto = new UTF8Encoding(false, true).GetString(valor); }
        catch (DecoderFallbackException) { return hexa; }
        if (texto.Any(c => char.IsControl(c) && c is not ('\r' or '\n' or '\t'))) return hexa;
        return $"\"{texto.Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t")}\"  {hexa}";
    }
}

public sealed partial class Rede
{
    readonly List<RadioBle> radiosBle = [];

    /// <summary>A placa em que o celular (a janela do celular) esta conectado pelo BLE.</summary>
    public RadioBle BleDoCelular { get; private set; }

    /// <summary>
    /// A placa mandou uma notificacao (ou indicacao) para o celular. Vem da
    /// thread do sketch: quem mostra precisa passar para a sua.
    /// </summary>
    public event Action<RadioBle, CaracteristicaBle, byte[]> NotificouOCelular;

    /// <summary>A conexao BLE do celular acabou (a placa desconectou, reiniciou ou parou).</summary>
    public event Action<RadioBle> BleDoCelularCaiu;

    /// <param name="nimble">A pilha, quando nao e a do core do Arduino (o MicroPython usa o NimBLE em toda ESP32).</param>
    public RadioBle IniciarBle(Maquina placa, string nome, bool? nimble = null)
    {
        EsquecerBle(placa);

        // O MAC do Bluetooth e o da placa com o ultimo byte + 2 (o base MAC
        // + 2, na conta do esp_read_mac).
        string mac = Wifi(placa).Mac;
        var partes = mac.Split(':');
        if (partes.Length == 6 && int.TryParse(partes[5], System.Globalization.NumberStyles.HexNumber, null, out int ultimo))
            partes[5] = ((ultimo + 2) & 0xFF).ToString("X2");
        string endereco = string.Join(":", partes).ToLowerInvariant();

        // A ESP32 usa o Bluedroid; a S3, a C3, a C6 e a H2, o NimBLE (e o que
        // os sdkconfig.h do core 3.3.11 ligam em cada chip).
        bool pilha = nimble ?? ModuloNativo.Chip(placa.Perfil?.Id ?? "") != "esp32";
        var radio = new RadioBle(placa, string.IsNullOrWhiteSpace(nome) ? "ESP32" : nome, endereco, pilha);
        lock (trava) radiosBle.Add(radio);
        Anotar(placa, "ble", $"BLE ligado como \"{radio.Nome}\" ({endereco})");
        return radio;
    }

    public void EsquecerBle(Maquina placa)
    {
        List<RadioBle> saindo;
        lock (trava)
        {
            saindo = [.. radiosBle.Where(r => r.Dona == placa)];
            radiosBle.RemoveAll(r => r.Dona == placa);
        }
        foreach (var radio in saindo)
        {
            if (BleDoCelular == radio)
            {
                BleDoCelular = null;
                BleDoCelularCaiu?.Invoke(radio);
            }

            // As conexoes com outras placas caem dos dois lados.
            List<ConexaoBle> conexoes;
            lock (radio.Clientes) conexoes = [.. radio.Clientes];
            lock (radio.ComoCliente) conexoes.AddRange(radio.ComoCliente);
            foreach (var conexao in conexoes) DesconectarBlePlaca(conexao, avisarAQuemSaiu: false);
        }
    }

    /// <summary>O que uma placa encontra ao procurar: as outras placas anunciando.</summary>
    public List<RadioBle> AnunciosBleVistosDe(Maquina placa)
    {
        lock (trava) return [.. radiosBle.Where(r => r.Anunciando && r.Dona != placa)];
    }

    /// <summary>Uma placa anunciando, pelo endereco (o connect(BLEAddress) e o da ArduinoBLE).</summary>
    public RadioBle RadioBleDe(string endereco)
    {
        lock (trava)
            return radiosBle.FirstOrDefault(r => string.Equals(r.Endereco, endereco?.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Uma placa (o cliente) conecta no servidor GATT de outra. Como com o
    /// celular, so conecta em quem esta anunciando -- e o anuncio para com a
    /// conexao. Nulo: o servidor nao esta la (parou, reiniciou, nao anuncia).
    /// </summary>
    public ConexaoBle ConectarBlePlaca(RadioBle cliente, RadioBle servidor)
    {
        if (cliente is null || servidor is null || cliente == servidor) return null;
        lock (trava)
            if (!radiosBle.Contains(servidor) || !radiosBle.Contains(cliente) || !servidor.Anunciando || !servidor.Conectavel) return null;

        var conexao = new ConexaoBle(servidor, cliente);
        lock (servidor.Clientes) servidor.Clientes.Add(conexao);
        lock (cliente.ComoCliente) cliente.ComoCliente.Add(conexao);
        servidor.Anunciando = false;
        servidor.Avisar(new EventoBle(TipoDeEventoBle.Conectou, Conexao: conexao));
        Anotar(servidor.Dona, "ble", $"\"{cliente.Nome}\" ({cliente.Endereco}) conectou em \"{servidor.Nome}\"");
        return conexao;
    }

    /// <summary>
    /// A conexao entre duas placas acaba. Os dois lados ficam sabendo -- menos
    /// quem saiu do ar (reiniciou, parou), que ja nao escuta nada.
    /// </summary>
    public void DesconectarBlePlaca(ConexaoBle conexao, bool avisarAQuemSaiu = true)
    {
        if (conexao is null || !conexao.Ativa) return;
        conexao.Ativa = false;
        lock (conexao.Servidor.Clientes) conexao.Servidor.Clientes.Remove(conexao);
        lock (conexao.Cliente.ComoCliente) conexao.Cliente.ComoCliente.Remove(conexao);
        foreach (var c in conexao.Servidor.Visiveis()) conexao.Assinar(c, false);
        if (conexao.Servidor.ReanunciarAoDesconectar) conexao.Servidor.Anunciando = true;

        bool servidorNoAr, clienteNoAr;
        lock (trava)
        {
            servidorNoAr = radiosBle.Contains(conexao.Servidor);
            clienteNoAr = radiosBle.Contains(conexao.Cliente);
        }
        if (servidorNoAr || avisarAQuemSaiu) conexao.Servidor.Avisar(new EventoBle(TipoDeEventoBle.Desconectou, Conexao: conexao));
        if (clienteNoAr || avisarAQuemSaiu) conexao.Cliente.Avisar(new EventoBle(TipoDeEventoBle.Caiu, Conexao: conexao));
        Anotar(conexao.Servidor.Dona, "ble", $"\"{conexao.Cliente.Nome}\" desconectou de \"{conexao.Servidor.Nome}\"");
    }

    /// <summary>O cliente le uma caracteristica do servidor. Nulo: ela nao deixa ler (ou a conexao caiu).</summary>
    public byte[] LerDaPlaca(ConexaoBle conexao, CaracteristicaBle c)
    {
        if (conexao is not { Ativa: true } || c is null || !c.Le) return null;
        conexao.Servidor.Avisar(new EventoBle(TipoDeEventoBle.Leu, c, Conexao: conexao));
        return c.Valor;
    }

    /// <summary>O cliente escreve numa caracteristica do servidor. Falso: ela nao aceita (ou a conexao caiu).</summary>
    public bool EscreverDaPlaca(ConexaoBle conexao, CaracteristicaBle c, byte[] dados, bool comResposta)
    {
        if (conexao is not { Ativa: true } || c is null) return false;
        if (comResposta ? !c.Escreve : !(c.EscreveSemResposta || c.Escreve)) return false;
        c.Valor = dados;
        conexao.Servidor.Avisar(new EventoBle(TipoDeEventoBle.Escreveu, c, Conexao: conexao, Dados: dados));
        Anotar(conexao.Servidor.Dona, "ble", $"\"{conexao.Cliente.Nome}\" → {Ble.NomeDoUuid(c.Uuid) ?? c.Uuid}: {Ble.Mostrar(dados)}");
        return true;
    }

    /// <summary>O cliente escreve num descritor do servidor. Falso: ele nao aceita (ou a conexao caiu).</summary>
    public bool EscreverDescritorDaPlaca(ConexaoBle conexao, DescritorBle d, byte[] dados)
    {
        if (conexao is not { Ativa: true } || d is null || !d.Escreve) return false;
        d.Valor = dados;
        conexao.Servidor.Avisar(new EventoBle(TipoDeEventoBle.Escreveu, d.Dona, Conexao: conexao, Dados: dados, Descritor: d));
        return true;
    }

    /// <summary>
    /// O notify (ou indicate) para uma conexao so, com os bytes dados -- o gatts_notify do
    /// MicroPython, que manda mesmo sem o outro lado ter assinado. Conexao nula: o celular.
    /// </summary>
    internal bool NotificarPara(CaracteristicaBle c, ConexaoBle conexao, byte[] dados, bool indicacao)
    {
        var radio = c.Servico.Radio;
        if (conexao is null)
        {
            if (!radio.CelularConectado || BleDoCelular != radio) return false;
            NotificouOCelular?.Invoke(radio, c, dados);
            return true;
        }
        if (!conexao.Ativa) return false;
        conexao.Cliente.Avisar(new EventoBle(TipoDeEventoBle.Notificou, c, indicacao ? 0 : 1, conexao, dados));
        return true;
    }

    /// <summary>O cliente liga (ou desliga) as notificacoes: escreve no CCCD da conexao dele.</summary>
    public bool AssinarDaPlaca(ConexaoBle conexao, CaracteristicaBle c, bool ligar, bool indicacao)
    {
        if (conexao is not { Ativa: true } || c is null || !(c.Notifica || c.Indica)) return false;
        conexao.Assinar(c, ligar);
        conexao.Servidor.Avisar(new EventoBle(TipoDeEventoBle.Assinou, c, ligar ? (indicacao ? 2 : 1) : 0, conexao));
        return true;
    }

    /// <summary>O que o celular encontra ao procurar: as placas anunciando.</summary>
    public List<RadioBle> AnunciosBle()
    {
        lock (trava) return [.. radiosBle.Where(r => r.Anunciando)];
    }

    public void AnunciarBle(RadioBle radio, bool ligar)
    {
        if (radio.Anunciando == ligar) return;
        radio.Anunciando = ligar;
        Anotar(radio.Dona, "ble", ligar ? $"anunciando \"{radio.Nome}\"" + (radio.UuidsAnunciados.Count > 0
                                              ? $" (serviço {string.Join(", ", radio.UuidsAnunciados)})" : "")
                                        : "parou de anunciar");
    }

    public bool ConectarBleDoCelular(RadioBle radio)
    {
        if (radio is null || !radio.Anunciando || !radio.Conectavel) return false;
        if (BleDoCelular is { } antes && antes != radio) DesconectarBleDoCelular();

        // Como no controlador BLE: com a conexao, o anuncio para.
        radio.Anunciando = false;
        radio.CelularConectado = true;
        BleDoCelular = radio;
        radio.Avisar(new EventoBle(TipoDeEventoBle.Conectou));
        Anotar(radio.Dona, "ble", $"celular conectou em \"{radio.Nome}\"");
        return true;
    }

    /// <summary>O celular sai (ou a placa manda sair, com pServer->disconnect()).</summary>
    public void DesconectarBleDoCelular(bool pelaPlaca = false)
    {
        var radio = BleDoCelular;
        if (radio is null) return;
        BleDoCelular = null;
        radio.CelularConectado = false;
        foreach (var c in radio.Visiveis()) c.Assinada = false;
        if (radio.ReanunciarAoDesconectar) radio.Anunciando = true;
        radio.Avisar(new EventoBle(TipoDeEventoBle.Desconectou));
        Anotar(radio.Dona, "ble", pelaPlaca ? "a placa desconectou o celular" : "celular desconectou");
        if (pelaPlaca) BleDoCelularCaiu?.Invoke(radio);
    }

    /// <summary>O celular le uma caracteristica. Nulo: ela nao deixa ler.</summary>
    public byte[] LerDoCelular(CaracteristicaBle c)
    {
        if (c is null || !c.Le || BleDoCelular != c.Servico.Radio) return null;
        c.Servico.Radio.Avisar(new EventoBle(TipoDeEventoBle.Leu, c));
        return c.Valor;
    }

    /// <summary>O celular escreve. Falso: ela nao aceita escrita.</summary>
    public bool EscreverDoCelular(CaracteristicaBle c, byte[] dados)
    {
        if (c is null || !(c.Escreve || c.EscreveSemResposta) || BleDoCelular != c.Servico.Radio) return false;
        c.Valor = dados;
        c.Servico.Radio.Avisar(new EventoBle(TipoDeEventoBle.Escreveu, c, Dados: dados));
        Anotar(c.Servico.Radio.Dona, "ble", $"celular → {Ble.NomeDoUuid(c.Uuid) ?? c.Uuid}: {Ble.Mostrar(dados)}");
        return true;
    }

    /// <summary>O celular liga (ou desliga) as notificacoes: escreve no CCCD.</summary>
    public bool AssinarDoCelular(CaracteristicaBle c, bool ligar)
    {
        if (c is null || !(c.Notifica || c.Indica) || BleDoCelular != c.Servico.Radio) return false;
        c.Assinada = ligar;
        c.Servico.Radio.Avisar(new EventoBle(TipoDeEventoBle.Assinou, c, ligar ? (c.Notifica ? 1 : 2) : 0));
        return true;
    }

    /// <summary>
    /// O notify()/indicate() da placa. Devolve o status do onStatus() da
    /// biblioteca: 0 mandou; senao o motivo de nao ter mandado.
    /// </summary>
    internal StatusDoNotifyBle NotificarDaPlaca(CaracteristicaBle c, bool indicacao)
    {
        var radio = c.Servico.Radio;
        bool celular = radio.CelularConectado && BleDoCelular == radio;
        List<ConexaoBle> placas;
        lock (radio.Clientes) placas = [.. radio.Clientes.Where(x => x.Ativa)];
        if (!celular && placas.Count == 0) return StatusDoNotifyBle.SemCliente;

        // As regras de cada pilha, lidas do BLECharacteristic.cpp do core 3.3:
        // no NimBLE (e na ArduinoBLE) so vai para quem assinou; no Bluedroid,
        // sem o descritor 2902 vai assim mesmo, e com ele so para quem assinou.
        bool Recebe(bool assinou) => radio.NimBle || radio.ArduinoBle ? assinou : !c.Tem2902 || assinou;

        var dados = c.Valor;
        bool mandou = false;
        if (celular && Recebe(c.Assinada))
        {
            NotificouOCelular?.Invoke(radio, c, dados);
            mandou = true;
        }
        foreach (var conexao in placas)
        {
            if (!Recebe(conexao.Assinou(c))) continue;
            conexao.Cliente.Avisar(new EventoBle(TipoDeEventoBle.Notificou, c, indicacao ? 0 : 1, conexao, dados));
            mandou = true;
        }
        if (mandou) return StatusDoNotifyBle.Mandou;
        if (radio.NimBle || radio.ArduinoBle) return StatusDoNotifyBle.SemAssinante;
        return indicacao ? StatusDoNotifyBle.IndicacaoDesligada : StatusDoNotifyBle.NotificacaoDesligada;
    }
}

/// <summary>Os valores do BLECharacteristicCallbacks::Status do core.</summary>
public enum StatusDoNotifyBle
{
    Mandou = -1,
    IndicacaoOk = 0, NotificacaoOk = 1, IndicacaoDesligada = 2, NotificacaoDesligada = 3,
    ErroGatt = 4, SemCliente = 5, SemAssinante = 6,
}
