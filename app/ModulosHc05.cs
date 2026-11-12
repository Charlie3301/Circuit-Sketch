using System.Collections.Concurrent;
using System.Text;

namespace Simulador.App;

/// <summary>
/// O modulo Bluetooth HC-05 (a placa ZS-040) no circuito.
///
/// Ele e uma ponte: o que chega pelo RXD sai pelo Bluetooth, e o que chega
/// pelo Bluetooth sai pelo TXD -- no baud configurado (9600 de fabrica). Do
/// lado do Bluetooth ele e um aparelho da bancada como o SerialBT da ESP32:
/// o celular do painel conecta nele, um programa do computador tambem (a
/// porta 127.0.0.1:9000 em diante), e uma ESP32 mestre o acha pelo nome. Do
/// lado do fio, a UART dele mora numa maquina auxiliar (sem sketch), e a
/// bancada liga os fios dela como liga os de outra placa: baud trocado vira
/// lixo, o byte leva o tempo do baud para passar.
///
/// Com o EN em HIGH ao ligar o modulo entra no modo de comandos AT, a 38400
/// baud (o LED pisca devagar). Com o EN em HIGH depois de ligado, os
/// comandos valem no baud dos dados. O botao da placa (o KEY) faz o mesmo
/// que o EN em HIGH: segurado ao ligar, o modo AT. Os comandos sao os do
/// firmware 2.0 da ZS-040, terminados em CR e LF; a resposta termina em CR e
/// LF tambem. O que o AT muda (o nome, o baud, o papel, a senha) vai para a
/// peca, e dela para o arquivo do projeto -- a flash do modulo.
/// </summary>
public sealed class ModulosHc05
{
    public sealed class Modulo
    {
        public string Peca;
        internal PecaNoCircuito DaPeca;
        public string Endereco;
        public Maquina Uart;                // so a UART: o RX no "pino" 0, o TX no 1
        public PortaSerial Porta;
        public (string Placa, int Gpio) Rxd, Txd;
        internal ConfiguracaoHc05 Cfg;
        public DispositivoBt Bt;
        public bool Alimentado, ModoAt;
        internal bool EnAlto;
        internal DateTime LigouEm;
        internal readonly StringBuilder Linha = new();
        internal readonly ConcurrentQueue<byte[]> DoBluetooth = new();
        internal DateTime ProximaTentativa;
        internal bool AvisouFimDeLinha;

        public bool Conectado => Bt is { TemConexao: true };
    }

    readonly Dictionary<string, Modulo> modulos = [];
    Rede rede;
    Bancada bancada;

    public IReadOnlyDictionary<string, Modulo> Todos => modulos;

    /// <summary>O Rodar: a conta de energia de antes dele nao decide nada.</summary>
    public DateTime Inicio { get; private set; }

    public Modulo De(string peca) => peca is not null && modulos.TryGetValue(peca, out var m) ? m : null;

    /// <summary>O Rodar: uma UART para cada HC-05, com os fios do circuito de agora.</summary>
    public void Comecar(Circuito circuito, Netlist netlist, Bancada bancada, Rede rede)
    {
        Parar();
        Inicio = DateTime.Now;
        this.rede = rede;
        this.bancada = bancada;
        if (circuito is null || netlist is null) return;
        foreach (var peca in circuito.Pecas)
        {
            if (peca.Tipo != "hc-05") continue;
            var cfg = peca.Hc05?.Copia() ?? new ConfiguracaoHc05();
            var uart = new Maquina { TempoReal = true, Nome = $"HC-05 {peca.Nome ?? peca.Id}".Trim() };
            uart.Reiniciar();
            var porta = new PortaSerial(uart, "HC-05", 0, 1, 1024, 1 << 20) { Baud = cfg.Baud, Ligada = true };
            lock (uart.Portas) uart.Portas.Add(porta);
            var m = new Modulo
            {
                Peca = peca.Id, DaPeca = peca, Endereco = EnderecoDoModulo(peca.Id), Uart = uart, Porta = porta, Cfg = cfg,
                Rxd = netlist.PinoDe(circuito, peca.Id, "RXD"),
                Txd = netlist.PinoDe(circuito, peca.Id, "TXD"),
            };
            // O que o modulo manda pelo TXD chega em quem esta nesse fio.
            uart.LigadosAoPino = gpio => gpio == 1 && m.Txd.Placa is not null && bancada.De(m.Txd.Placa) is { } placa
                ? [(placa, m.Txd.Gpio)]
                : [];
            modulos[peca.Id] = m;
        }
    }

    /// <summary>A UART dos modulos com o RXD no fio deste GPIO (o TX da placa chega nela).</summary>
    public IEnumerable<(Maquina Maquina, int Gpio)> NoFioDaPlaca(string placa, int gpio)
    {
        foreach (var m in modulos.Values)
            if (m.Rxd.Placa == placa && m.Rxd.Gpio == gpio) yield return (m.Uart, 0);
    }

    public void Parar()
    {
        foreach (var m in modulos.Values) Desligar(m);
        modulos.Clear();
    }

    void Desligar(Modulo m)
    {
        if (m.Bt is not null) rede?.EsquecerBluetooth(m.Uart);
        m.Bt = null;
        m.Alimentado = m.ModoAt = false;
        m.Linha.Clear();
        while (m.DoBluetooth.TryDequeue(out _)) { }
    }

    /// <summary>
    /// Um quadro: a energia, o EN, o que chegou pelo fio (dado ou comando AT),
    /// o que chegou pelo Bluetooth, a conexao do mestre. Devolve o nivel do
    /// STATE (HIGH conectado), ou -1 com o modulo sem energia.
    /// </summary>
    public int Passo(string peca, double volts, bool enAlto)
    {
        if (De(peca) is not { } m) return -1;
        bool alimentado = volts >= 3.0;
        if (!alimentado)
        {
            if (m.Alimentado) Desligar(m);
            return -1;
        }
        m.EnAlto = enAlto;
        if (!m.Alimentado) Ligar(m, enAlto);

        // O que a placa mandou pelo RXD.
        m.Porta.Atualizar();
        if (m.Porta.Entrada.Disponivel > 0)
        {
            var bytes = new List<byte>();
            int b;
            while ((b = m.Porta.Entrada.Ler()) >= 0) bytes.Add((byte)b);
            if (m.ModoAt || enAlto) Comandos(m, bytes);
            else if (m.Conectado) m.Bt.Enviar([.. bytes]);
        }

        // O que chegou pelo Bluetooth sai pelo TXD.
        while (m.DoBluetooth.TryDequeue(out var chegou))
            if (!m.ModoAt) m.Porta.Transmitir(chegou);

        // O mestre procura o escravo sozinho: o do BIND, ou qualquer um com o CMODE em 1.
        if (!m.ModoAt && m.Cfg.Papel == 1 && m.Bt is { Par: null } && DateTime.Now >= m.ProximaTentativa)
        {
            m.ProximaTentativa = DateTime.Now.AddSeconds(1);
            var alvo = rede.BluetoothVisiveis(m.Bt).FirstOrDefault(d =>
                m.Cfg.Cmode == 1 || m.Cfg.Bind.Length > 0 && MesmoEndereco(d.Endereco, m.Cfg.Bind));
            if (alvo is not null) rede.ConectarBluetooth(m.Bt, alvo);
        }
        return m.Conectado ? 1 : 0;
    }

    void Ligar(Modulo m, bool enAlto)
    {
        m.Alimentado = true;
        m.LigouEm = DateTime.Now;
        m.ModoAt = enAlto;
        m.Linha.Clear();
        m.AvisouFimDeLinha = false;
        if (m.ModoAt)
        {
            m.Porta.Baud = 38400;
            rede?.Anotar(m.Uart, "bt", Idioma.T("HC-05 no modo de comandos AT (o EN estava em HIGH ao ligar): 38400 baud"));
            return;
        }
        m.Porta.Baud = m.Cfg.Baud;
        LigarBluetooth(m);
    }

    void LigarBluetooth(Modulo m)
    {
        if (rede is null) return;
        m.Bt = rede.IniciarBluetooth(m.Uart, m.Cfg.Nome, m.Cfg.Papel == 1);
        m.Bt.Endereco = m.Endereco;
        m.Bt.Entrada.Desvio = dados => m.DoBluetooth.Enqueue(dados);
        m.ProximaTentativa = DateTime.Now;
    }

    /// <summary>O LED da placa: rapido procurando, duas piscadas conectado, devagar no modo AT.</summary>
    public bool LedAceso(string peca)
    {
        if (De(peca) is not { Alimentado: true } m) return false;
        double ms = (DateTime.Now - m.LigouEm).TotalMilliseconds;
        if (m.ModoAt) return ms % 4000 < 2000;
        if (m.Conectado)
        {
            double t = ms % 2000;
            return t < 100 || t >= 200 && t < 300;
        }
        return ms % 200 < 100;
    }

    // =====================================================================
    //  os comandos AT
    // =====================================================================

    void Comandos(Modulo m, List<byte> bytes)
    {
        foreach (byte b in bytes)
        {
            char c = (char)b;
            if (c == '\n')
            {
                string linha = m.Linha.ToString();
                m.Linha.Clear();
                if (!linha.EndsWith('\r'))
                {
                    // O modulo de verdade fica calado: o comando so vale com o CR antes do LF.
                    if (!m.AvisouFimDeLinha && linha.Trim().Length > 0)
                        rede?.Anotar(m.Uart, "bt", Idioma.T("HC-05: o comando AT precisa terminar com CR e LF (no monitor, \"Ambos NL e CR\"); sem o CR ele nao responde"));
                    m.AvisouFimDeLinha = true;
                    continue;
                }
                Responder(m, Executar(m, linha.TrimEnd('\r')));
                Gravar(m);
                continue;
            }
            if (m.Linha.Length < 256) m.Linha.Append(c);
        }
    }

    /// <summary>O que o AT mudou vai para a peca (e dela para o arquivo), so quando mudou mesmo.</summary>
    static void Gravar(Modulo m)
    {
        var antes = m.DaPeca.Hc05 ?? new ConfiguracaoHc05();
        var c = m.Cfg;
        if (antes.Nome == c.Nome && antes.Senha == c.Senha && antes.Papel == c.Papel && antes.Baud == c.Baud
            && antes.Parada == c.Parada && antes.Paridade == c.Paridade && antes.Cmode == c.Cmode && antes.Bind == c.Bind)
            return;
        m.DaPeca.Hc05 = c.Copia();
    }

    void Responder(Modulo m, IEnumerable<string> linhas)
    {
        var texto = new StringBuilder();
        foreach (string l in linhas) texto.Append(l).Append("\r\n");
        if (texto.Length > 0) m.Porta.Transmitir(Encoding.ASCII.GetBytes(texto.ToString()));
    }

    static readonly long[] Bauds = [4800, 9600, 19200, 38400, 57600, 115200, 230400, 460800, 921600, 1382400];

    IEnumerable<string> Executar(Modulo m, string comando)
    {
        string c = comando.Trim();
        if (c.Length == 0) return [];
        string maiusculo = c.ToUpperInvariant();
        if (!maiusculo.StartsWith("AT")) return ["ERROR:(0)"];
        if (maiusculo == "AT") return ["OK"];
        if (!maiusculo.StartsWith("AT+")) return ["ERROR:(0)"];
        int igual = c.IndexOf('=');
        string nome = (igual >= 0 ? maiusculo[3..igual] : maiusculo[3..]).TrimEnd('?');
        bool pergunta = igual < 0 && maiusculo.EndsWith('?');
        string valor = igual >= 0 ? c[(igual + 1)..] : null;
        var cfg = m.Cfg;
        switch (nome)
        {
            case "VERSION": return ["+VERSION:2.0-20100601", "OK"];
            case "ADDR": return [$"+ADDR:{m.Endereco}", "OK"];
            case "NAME" when pergunta: return [$"+NAME:{cfg.Nome}", "OK"];
            case "NAME" when valor is not null:
                cfg.Nome = valor.Trim().Trim('"');
                return ["OK"];
            case "ROLE" when pergunta: return [$"+ROLE:{cfg.Papel}", "OK"];
            case "ROLE" when valor is not null:
                if (!int.TryParse(valor.Trim(), out int papel) || papel is < 0 or > 2) return ["ERROR:(0)"];
                cfg.Papel = papel;
                return ["OK"];
            case "PSWD" when pergunta: return [$"+PSWD:{cfg.Senha}", "OK"];
            case "PSWD" when valor is not null:
                cfg.Senha = valor.Trim().Trim('"');
                return ["OK"];
            case "UART" when pergunta: return [$"+UART:{cfg.Baud},{cfg.Parada},{cfg.Paridade}", "OK"];
            case "UART" when valor is not null:
            {
                var partes = valor.Split(',');
                if (partes.Length != 3 || !long.TryParse(partes[0].Trim(), out long baud) || !Bauds.Contains(baud)
                    || !int.TryParse(partes[1].Trim(), out int parada) || parada is < 0 or > 1
                    || !int.TryParse(partes[2].Trim(), out int paridade) || paridade is < 0 or > 2)
                    return ["ERROR:(1D)"];
                (cfg.Baud, cfg.Parada, cfg.Paridade) = (baud, parada, paridade);   // vale no proximo ligar
                return ["OK"];
            }
            case "CMODE" when pergunta: return [$"+CMOD:{cfg.Cmode}", "OK"];
            case "CMODE" when valor is not null:
                if (!int.TryParse(valor.Trim(), out int cmode) || cmode is < 0 or > 2) return ["ERROR:(0)"];
                cfg.Cmode = cmode;
                return ["OK"];
            case "BIND" when pergunta: return [$"+BIND:{(cfg.Bind.Length > 0 ? cfg.Bind : "0:0:0")}", "OK"];
            case "BIND" when valor is not null:
                cfg.Bind = valor.Trim().Replace(',', ':').ToLowerInvariant();
                return ["OK"];
            case "STATE":
                return [$"+STATE:{(m.Conectado ? "CONNECTED" : m.ModoAt ? "INITIALIZED" : "PAIRABLE")}", "OK"];
            case "INIT": return ["OK"];
            case "RMAAD": return ["OK"];
            case "INQ":
            {
                var lista = new List<string>();
                if (rede is not null)
                    foreach (var d in rede.BluetoothVisiveis(m.Bt))
                        if (d.Dona != m.Uart) lista.Add($"+INQ:{d.Endereco.ToUpperInvariant()},1F00,7FFF");
                lista.Add("OK");
                return lista;
            }
            case "LINK" when valor is not null:
            {
                if (m.ModoAt || m.Bt is null || rede is null) return ["FAIL"];
                var alvo = rede.BluetoothVisiveis(m.Bt).FirstOrDefault(d => MesmoEndereco(d.Endereco, valor));
                return [alvo is not null && rede.ConectarBluetooth(m.Bt, alvo) ? "OK" : "FAIL"];
            }
            case "ORGL":
                (cfg.Nome, cfg.Senha, cfg.Papel, cfg.Baud, cfg.Parada, cfg.Paridade, cfg.Cmode, cfg.Bind)
                    = ("H-C-2010-06-01", "1234", 0, 38400, 0, 0, 0, "");
                return ["OK"];
            case "RESET":
                // Responde no baud de agora e reinicia: o que o AT mudou passa a valer. Com o
                // EN ainda em HIGH ele volta ao modo AT; com o EN em LOW, ao de dados.
                Responder(m, ["OK"]);
                Desligar(m);
                m.Alimentado = true;
                m.LigouEm = DateTime.Now;
                m.ModoAt = m.EnAlto;
                m.Porta.Baud = m.ModoAt ? 38400 : cfg.Baud;
                if (!m.ModoAt) LigarBluetooth(m);
                return [];
        }
        return ["ERROR:(0)"];
    }

    /// <summary>O endereco de fabrica: o prefixo dos HC-05 e o resto pela peca (o mesmo a cada rodada).</summary>
    static string EnderecoDoModulo(string peca)
    {
        uint h = 2166136261;
        foreach (char ch in peca ?? "") h = (h ^ ch) * 16777619;
        return $"98d3:31:{h & 0xFFFFFF:x6}";
    }

    /// <summary>O mesmo aparelho, escrito do jeito do AT ("98d3,31,fb1234") ou com dois pontos.</summary>
    public static bool MesmoEndereco(string a, string b)
    {
        static string Limpo(string s) => string.Concat((s ?? "").ToLowerInvariant().Where(Uri.IsHexDigit));
        string la = Limpo(a), lb = Limpo(b);
        return la.Length > 0 && la == lb;
    }
}
