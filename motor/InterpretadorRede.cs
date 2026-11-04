using System.Text;

namespace Simulador;

/// <summary>
/// WiFi, HTTP, servidor web, MQTT e Bluetooth do lado do sketch.
///
/// Aqui mora só a TRADUÇÃO: cada chamada da biblioteca do Arduino vira
/// uma operação da <see cref="Simulador.Rede"/>, que é quem sabe falar
/// com as outras placas da bancada, com o computador e com a internet.
///
/// A regra de sempre vale também aqui: nada passa calado. Uma placa sem
/// WiFi recusa <c>WiFi.begin()</c> dizendo qual placa tem; um
/// <c>http.GET()</c> antes de conectar devolve o mesmo -1 da placa real,
/// com o motivo escrito no painel Rede.
/// </summary>
public partial class Interpretador
{
    static readonly HashSet<string> ClassesDeRede =
    [
        "WiFiClient", "WiFiClientSecure", "WiFiServer", "WebServer",
        "HTTPClient", "PubSubClient", "BluetoothSerial", "IPAddress",
    ];

    static bool EhClasseDeRede(string classe) => ClassesDeRede.Contains(classe);

    sealed class EstadoCliente
    {
        public ConexaoTcp Conexao;
        public bool Seguro;
        public bool Inseguro;
        /// <summary>A porta do servidor que aceitou (o localPort do client).</summary>
        public int PortaLocal;
    }

    sealed class EstadoServidor
    {
        public int Porta = 80;
        public bool Cru;
        public ServidorWeb Servidor;
        public readonly List<(string Caminho, int Metodo, Rotina Rotina)> Rotas = [];
        public Rotina NaoEncontrado;

        // O pedido que está sendo atendido agora: é dele que server.arg(),
        // server.uri() e server.method() falam.
        public PedidoHttp Atual;
        public List<KeyValuePair<string, string>> Argumentos = [];

        /// <summary>
        /// Os pedacos que o "{}" da rota capturou, para o pathArg().
        ///
        /// "/toggle/{}" atendendo "/toggle/2" deixa "2" aqui.
        /// </summary>
        public List<string> Capturados = [];
        public int Codigo;
        public string Tipo;
        public readonly StringBuilder Corpo = new();
        public readonly List<KeyValuePair<string, string>> CabecalhosDaResposta = [];
        public bool Respondeu;

        /// <summary>WiFiServer: o client da volta anterior, para fechar o que ficou aberto.</summary>
        public ConexaoTcp UltimaConexao;
    }

    sealed class EstadoHttp
    {
        public string Url;
        public readonly Dictionary<string, string> Cabecalhos = new(StringComparer.OrdinalIgnoreCase);
        public int TempoMs = 5000;
        public bool Inseguro;
        public RespostaHttp Resposta;
    }

    sealed class EstadoMqtt(ClienteMqtt cliente)
    {
        public ClienteMqtt Cliente { get; } = cliente;
        public Rotina Callback;

        /// <summary>
        /// O buffer do PubSubClient: 256 bytes, o MQTT_MAX_PACKET_SIZE da
        /// biblioteca. A mensagem que nao cabe nele nao sai (publish()
        /// devolve false) e a que chega maior e jogada fora -- o tropeco
        /// classico do JSON grande, que o setBufferSize() resolve.
        /// </summary>
        public int Buffer = 256;

        /// <summary>O cliente de rede por baixo: um WiFiClientSecure faz o MQTT ir por TLS.</summary>
        public EstadoCliente Transporte;

        /// <summary>O beginPublish() em andamento: o topico e o que ja foi escrito.</summary>
        public string TopicoAberto;
        public List<byte> CargaAberta;
        public bool RetidaAberta;

        public bool AvisouTamanho;
    }

    sealed class EstadoBt
    {
        public DispositivoBt Dispositivo;

        /// <summary>O register_callback() do sketch: recebe (evento, param).</summary>
        public Rotina AoEvento;
    }

    /// <summary>As placas com Bluetooth ligado neste sketch, para a entrega dos eventos.</summary>
    readonly List<EstadoBt> bluetoothsDoSketch = [];

    /// <summary>
    /// Entrega os eventos do Bluetooth ao register_callback(). Roda na
    /// volta do loop() e a cada chamada do SerialBT -- e o "Client
    /// Connected" dos tutoriais, que ate aqui nunca aparecia.
    /// </summary>
    void EntregarBluetooth()
    {
        foreach (var estado in bluetoothsDoSketch)
        {
            if (estado.Dispositivo is not { } dispositivo || estado.AoEvento is null) continue;

            for (int volta = 0; volta < 20 && dispositivo.ProximoEvento(out int evento, out string quem); volta++)
            {
                // O param->srv_open.rem_bda e o param->data_ind.len que os
                // exemplos leem.
                var param = new Objeto("esp_spp_cb_param_t");
                var aberta = new Objeto("srv_open");
                aberta.Campos["rem_bda"] = Valor.DeTexto(quem ?? "");
                aberta.Campos["handle"] = Valor.DeInt(1);
                aberta.Campos["status"] = Valor.DeInt(0);
                var dados = new Objeto("data_ind");
                dados.Campos["len"] = Valor.DeInt(dispositivo.Entrada.Disponivel);
                dados.Campos["handle"] = Valor.DeInt(1);
                param.Campos["srv_open"] = Valor.DeReferencia(aberta);
                param.Campos["open"] = Valor.DeReferencia(aberta);
                param.Campos["close"] = Valor.DeReferencia(aberta);
                param.Campos["data_ind"] = Valor.DeReferencia(dados);

                ChamarRotina(estado.AoEvento, Valor.DeInt(evento), Valor.DeReferencia(param));
            }
        }
    }


    /// <summary>
    /// O WiFiClient que o <c>server.available()</c> acabou de devolver,
    /// esperando a declaração que vai guardá-lo. Mesmo jeito do
    /// <c>File dados = SD.open(...)</c>: o valor da chamada só diz se veio
    /// alguém; o objeto em si vem por aqui.
    /// </summary>
    ConexaoTcp conexaoRecente;
    bool temConexaoRecente;

    /// <summary>O valor do "= ..." de uma declaração de objeto.</summary>
    Valor valorInicialDoObjeto;

    List<(string Ssid, int Rssi)> ultimaVarredura = [];

    ConexaoTcp PegarConexaoRecente()
    {
        var conexao = conexaoRecente;
        conexaoRecente = null;
        temConexaoRecente = false;
        return conexao;
    }

    void ExigirWifi(int linha, string oque)
    {
        if (Maq.Perfil.TemWifi || Maq.Rede.CaboConectado(Maq)) return;
        throw new ErroDeExecucao(
            $"{oque}: a {Maq.Perfil.Nome} nao tem WiFi. As placas com rede neste simulador sao a "
            + "ESP32 DevKit V1 e a Nano RP2040 Connect.", linha);
    }

    // =====================================================================
    //  Declaração dos objetos
    // =====================================================================

    void MontarObjetoDeRede(Objeto obj, CmdDeclObj d, Ambiente amb)
    {
        switch (d.Classe)
        {
            case "IPAddress":
                obj.Texto = d.Args.Count >= 4
                    ? string.Join(".", d.Args.Take(4).Select(a => (long)Avaliar(a, amb).Num & 0xFF))
                    : d.Inicial is not null ? valorInicialDoObjeto.Mostrar() : "0.0.0.0";
                break;

            case "WiFiClient":
            case "WiFiClientSecure":
                obj.Rede = new EstadoCliente
                {
                    Seguro = d.Classe == "WiFiClientSecure",
                    Conexao = d.Inicial is not null && temConexaoRecente ? PegarConexaoRecente() : null,
                };
                break;

            case "WiFiServer":
            case "WebServer":
                obj.Rede = new EstadoServidor
                {
                    Porta = d.Args.Count > 0 ? (int)Avaliar(d.Args[0], amb).Num : 80,
                    Cru = d.Classe == "WiFiServer",
                };
                break;

            case "HTTPClient":
                obj.Rede = new EstadoHttp();
                break;

            case "PubSubClient":
            {
                var estado = new EstadoMqtt(new ClienteMqtt(Maq));

                // Construtor antigo: PubSubClient client(servidor, porta, callback, wifiClient).
                if (d.Args.Count >= 3)
                {
                    estado.Cliente.Host = Avaliar(d.Args[0], amb).Mostrar();
                    estado.Cliente.Porta = (int)Avaliar(d.Args[1], amb).Num;
                    if (SemEndereco(d.Args[2]) is Variavel f && funcoes.ContainsKey(f.Nome))
                        estado.Callback = RotinaDe(d.Args[2], amb, "PubSubClient(...)");
                }

                // O WiFiClient (ou WiFiClientSecure) que vai por baixo: e ele
                // que decide se a conexao e com TLS.
                if (d.Args.Count > 0 && ObjetoDaExpressao(d.Args[^1], amb)?.Rede is EstadoCliente transporte)
                    estado.Transporte = transporte;

                obj.Rede = estado;
                break;
            }

            case "BluetoothSerial":
                obj.Rede = new EstadoBt();
                break;
        }
    }

    /// <summary>O valor de um objeto usado como valor: "if (client)", "Serial.println(ip)".</summary>
    static Valor ValorDoObjeto(Objeto obj) => obj.Classe switch
    {
        "IPAddress" => Valor.DeTexto(obj.Texto ?? "0.0.0.0"),
        "WiFiClient" or "WiFiClientSecure" =>
            Valor.DeBool(obj.Rede is EstadoCliente { Conexao: { } conexao }
                         && (conexao.Conectada || conexao.Disponivel > 0)),
        // O proprio objeto: passar "lcd" para uma funcao que recebe
        // "LiquidCrystal &tela" chega nela o display, e nao um 1.
        _ => Valor.DeReferencia(obj),
    };

    /// <summary>
    /// "client = server.available();": o objeto já existe e recebe a conexão
    /// nova. Sem isto a atribuição morria em "variavel nao declarada".
    /// </summary>
    bool RebindarObjeto(Variavel alvo, Valor valor, Ambiente amb)
    {
        if (!amb.BuscarObjeto(alvo.Nome, out var obj)) return false;

        // "central = BLE.central();", "peripheral = BLE.available();": o
        // objeto passa a ser o aparelho (ou a caracteristica) que voltou.
        if (obj.Rede is ServicoArduino or CaracteristicaArduino or DispositivoArduino
            && valor.Referencia is Objeto { Rede: var novo } && novo?.GetType() == obj.Rede.GetType())
        {
            obj.Rede = novo;
            return true;
        }

        switch (obj.Classe)
        {
            case "WiFiClient":
            case "WiFiClientSecure":
            {
                var estado = obj.Rede as EstadoCliente
                             ?? new EstadoCliente { Seguro = obj.Classe == "WiFiClientSecure" };
                estado.Conexao = temConexaoRecente ? PegarConexaoRecente() : estado.Conexao;
                obj.Rede = estado;
                return true;
            }

            case "IPAddress":
                obj.Texto = valor.Mostrar();
                return true;
        }
        return false;
    }

    // =====================================================================
    //  WiFi
    // =====================================================================

    Valor ChamarWiFi(string metodo, Chamada c, Ambiente amb)
    {
        ExigirWifi(c.Linha, $"WiFi.{metodo}()");

        var rede = Maq.Rede;
        var estado = rede.Wifi(Maq);
        var args = c.Args.Select(a => Avaliar(a, amb)).ToList();
        string S(int i) => i < args.Count ? args[i].Mostrar() : "";

        switch (metodo)
        {
            case "begin":
            {
                // O driver do WiFi pega a parte dele da heap; sem lugar, nao sobe.
                if (!LigarRadio("wifi", c.Linha)) return Valor.DeInt(4);
                rede.ConectarWifi(Maq, S(0), args.Count > 1 ? S(1) : null);

                // Na WiFiNINA da Nano RP2040 Connect o begin() ESPERA e
                // devolve o status; no ESP32 ele volta na hora e quem
                // espera e o "while (WiFi.status() != WL_CONNECTED)".
                if (Maq.Perfil.Id == "nano-rp2040-connect")
                {
                    EsperarWifi(rede);
                    return Valor.DeInt(rede.WifiConectado(Maq) ? 3 : 4);
                }
                return Valor.DeInt(6);
            }

            case "status":
                if (estado.Falha != 0) return Valor.DeInt(estado.Falha);
                return Valor.DeInt(rede.WifiConectado(Maq) ? 3 : estado.Ligado ? 6 : 0);

            case "isConnected":
                return Valor.DeBool(rede.WifiConectado(Maq));

            case "waitForConnectResult":
                EsperarWifi(rede);
                return Valor.DeInt(rede.WifiConectado(Maq) ? 3 : 4);

            case "localIP":
                return Valor.DeTexto(rede.WifiConectado(Maq) ? estado.Ip : "0.0.0.0");
            case "softAPIP":
                return Valor.DeTexto(estado.ModoAp ? estado.Ip : "0.0.0.0");
            case "gatewayIP":
            case "dnsIP":
                return Valor.DeTexto(Rede.Gateway);
            case "subnetMask":
                return Valor.DeTexto("255.255.255.0");

            case "macAddress":
            case "softAPmacAddress":
            case "BSSIDstr":
                return Valor.DeTexto(estado.Mac);

            case "SSID":
                if (args.Count > 0)
                {
                    int i = (int)args[0].Num;
                    return Valor.DeTexto(i >= 0 && i < ultimaVarredura.Count ? ultimaVarredura[i].Ssid : "");
                }
                return Valor.DeTexto(rede.WifiConectado(Maq) ? estado.Ssid : "");

            case "RSSI":
                if (args.Count > 0)
                {
                    int i = (int)args[0].Num;
                    return Valor.DeInt(i >= 0 && i < ultimaVarredura.Count ? ultimaVarredura[i].Rssi : 0);
                }
                return Valor.DeInt(rede.WifiConectado(Maq) ? -52 : 0);

            case "encryptionType":
            {
                int i = args.Count > 0 ? (int)args[0].Num : 0;
                if (i < 0 || i >= ultimaVarredura.Count) return Valor.DeInt(3);

                string nome = ultimaVarredura[i].Ssid;
                var ponto = rede.PontosDeAcesso.FirstOrDefault(p => p.Ssid == nome);
                bool aberta = ponto is not null
                    ? ponto.Aberta
                    : nome.Contains("GUEST", StringComparison.OrdinalIgnoreCase);

                // ESP8266: ENC_TYPE_NONE (7) ou ENC_TYPE_CCMP (4).
                // ESP32: WIFI_AUTH_OPEN (0) ou WIFI_AUTH_WPA2_PSK (3).
                if (Maq.Perfil.Processador == "ESP8266") return Valor.DeInt(aberta ? 7 : 4);
                return Valor.DeInt(aberta ? 0 : 3);
            }

            case "scanNetworks":
                if (!LigarRadio("wifi", c.Linha)) return Valor.DeInt(-2);   // WIFI_SCAN_FAILED
                ultimaVarredura = rede.RedesVisiveis();
                Maq.Avancar(300);
                return Valor.DeInt(ultimaVarredura.Count);

            case "scanDelete":
                ultimaVarredura = [];
                return Valor.Vazio;

            case "disconnect":
            case "softAPdisconnect":
                rede.DesligarWifi(Maq);
                // disconnect(true): o WiFi desliga de vez e o driver devolve a heap.
                if (args.Count > 0 && args[0].Verdadeiro) DesligarRadio("wifi");
                return Valor.DeBool(true);

            case "softAP":
                if (!LigarRadio("wifi", c.Linha)) return Valor.DeBool(false);
                rede.AbrirPontoDeAcesso(Maq, S(0));
                return Valor.DeBool(true);

            case "reconnect":
                if (estado.Ligado) rede.ConectarWifi(Maq, estado.Ssid);
                return Valor.DeBool(true);

            case "getHostname":
                return Valor.DeTexto(string.IsNullOrEmpty(estado.Hostname) ? "esp32-" + estado.Mac.Replace(":", "")[^6..] : estado.Hostname);

            // O nome que a placa da na rede: o hostByName e o mDNS das
            // outras placas acham ela por ele.
            case "setHostname":
            case "hostname" when args.Count > 0:
                estado.Hostname = S(0);
                return Valor.DeBool(true);
            case "hostname":
                return Valor.DeTexto(string.IsNullOrEmpty(estado.Hostname) ? "esp32-" + estado.Mac.Replace(":", "")[^6..] : estado.Hostname);

            case "getMode":
                return Valor.DeInt(estado.ModoAp ? 2 : 1);

            case "config":
                // IP fixo: o sketch escolhe o endereco da placa na bancada.
                if (args.Count > 0 && System.Net.IPAddress.TryParse(S(0), out _)) estado.Ip = S(0);
                return Valor.DeBool(true);

            // WiFi.mode(WIFI_STA) ja sobe o driver (e a heap dele);
            // WiFi.mode(WIFI_OFF) desce e devolve.
            case "mode":
                if (args.Count > 0 && args[0].Num == 0) DesligarRadio("wifi");
                else if (args.Count > 0) return Valor.DeBool(LigarRadio("wifi", c.Linha));
                return Valor.DeBool(true);

            // Ajustes que nao mudam nada aqui, mas o sketch chama.
            case "setSleep":
            case "persistent":
            case "setAutoReconnect":
            case "setAutoConnect":
            case "setTxPower":
            case "onEvent":
            case "setMinSecurity":
                return Valor.DeBool(true);
        }

        throw new ErroDeExecucao($"WiFi.{metodo}() ainda nao e suportado pelo simulador.", c.Linha);
    }

    /// <summary>Deixa o tempo da placa passar ate o WiFi ficar pronto.</summary>
    void EsperarWifi(Rede rede)
    {
        if (rede.Wifi(Maq).Falha != 0) return;

        for (int i = 0; i < 50 && !rede.WifiConectado(Maq); i++)
        {
            if (Maq.Parar) throw new ExecucaoInterrompida();
            Maq.Avancar(100);
        }
    }

    /// <summary>O objeto ESP do ESP32: informacoes da placa.</summary>
    /// <summary>
    /// O objeto ESP do core. Os numeros sao os da PLACA que roda o sketch --
    /// antes toda placa se dizia uma ESP32 de dois nucleos a 240 MHz, ate a
    /// C3 de um nucleo so.
    /// </summary>
    Valor ChamarEsp(string metodo, Chamada c, Ambiente amb)
    {
        var perfil = Maq.Perfil;
        bool esp8266 = perfil.Processador == "ESP8266";

        // Os bytes do MAC desta placa, que e o que o chip grava no eFuse.
        byte[] mac = (Maq.Rede?.MacDe(Maq) ?? $"24:0A:C4:{Rede.MeioDoMac}:01")
                     .Split(':').Select(b => Convert.ToByte(b, 16)).ToArray();

        switch (metodo)
        {
            case "getFreeHeap" or "getHeapSize" or "getMinFreeHeap" or "getMaxAllocHeap" or "getPsramSize"
                or "getFreePsram" or "getMinFreePsram" or "getMaxAllocPsram" or "getMaxFreeBlockSize"
                or "getHeapFragmentation" or "getFreeContStack"
                when MemoriaDoEsp(metodo, esp8266, out var daMemoria):
                return daMemoria;
            case "getCpuFreqMHz": return Valor.DeInt((int)perfil.Megahertz);
            case "getFlashChipSize": case "getFlashChipRealSize": return Valor.DeInt(4194304);
            case "getSdkVersion": return Valor.DeTexto(esp8266 ? "2.2.2-dev (CircuitSketch)" : "v5.1 (CircuitSketch)");
            case "getCoreVersion": return Valor.DeTexto(esp8266 ? "3.1.2" : "3.0.7");
            case "getSketchSize": return Valor.DeInt(perfil.BaseDaFlash);
            case "getFreeSketchSpace": return Valor.DeInt(perfil.FlashBytes - perfil.BaseDaFlash);

            // O ESP8266 conta o chip pelos tres ultimos bytes do MAC.
            case "getChipId" when esp8266:
                return Valor.DeInt((mac[3] << 16) | (mac[4] << 8) | mac[5]);

            // Da ESP32 para frente: o MAC inteiro, os bytes do primeiro ao
            // ultimo a partir do de baixo -- como o getEfuseMac() devolve.
            case "getEfuseMac" when !esp8266:
            {
                long valor = 0;
                for (int i = 0; i < mac.Length; i++) valor |= (long)mac[i] << (8 * i);
                return Valor.DeInt(valor);
            }

            case "getChipModel" when !esp8266:
                return Valor.DeTexto(perfil.Id switch
                {
                    "esp32-s2-devkit" => "ESP32-S2",
                    "esp32-s3-devkit" => "ESP32-S3",
                    "esp32-c3-devkit" => "ESP32-C3",
                    "esp32-c6-devkit" => "ESP32-C6",
                    "esp32-h2-devkit" => "ESP32-H2",
                    _ => "ESP32-D0WDQ6",
                });
            case "getChipCores" when !esp8266: return Valor.DeInt(perfil.Nucleos);
            case "getChipRevision" when !esp8266: return Valor.DeInt(1);

            // A placa comeca de novo pelo setup(), como na bancada. Quem roda
            // o sketch pega o reinicio e monta a rodada nova.
            case "restart":
            case "reset":
                throw Reiniciar(ReinicioDaPlaca.PeloSoftware, 0, 0);

            // O sono profundo do ESP8266 so acaba pelo RST: e o timer que
            // puxa o GPIO16 (D0) para LOW no fim do tempo, e sem o fio do
            // D0 no RST a placa dorme para sempre.
            case "deepSleep" when esp8266:
            case "deepSleepInstant" when esp8266:
                DormirComoEsp8266(c.Args.Count > 0 ? (long)Avaliar(c.Args[0], amb).Num : 0);
                return Valor.Vazio;
            case "deepSleepMax" when esp8266:
                return Valor.DeInt(14_300_000_000);

            // Os 512 bytes de memoria RTC do usuario: sobrevivem ao deepSleep.
            case "rtcUserMemoryRead" when esp8266:
                return ChamarMemoriaRtcDoUsuario(true, c, amb);
            case "rtcUserMemoryWrite" when esp8266:
                return ChamarMemoriaRtcDoUsuario(false, c, amb);

            case "getResetReason" when esp8266:
                return Valor.DeTexto(Origem?.Razao switch
                {
                    ReinicioDaPlaca.PeloSonoProfundo => "Deep-Sleep Wake",
                    ReinicioDaPlaca.PeloSoftware => "Software/System restart",
                    ReinicioDaPlaca.PeloWatchdog => "Software Watchdog",
                    ReinicioDaPlaca.PeloWatchdogDeHardware => "Hardware Watchdog",
                    ReinicioDaPlaca.PeloPanico => "Exception",
                    _ => "Power On",
                });

            // O watchdog do ESP8266: alimentar, desligar o de software (o de
            // hardware continua), religar.
            case "wdtFeed" when esp8266:
            case "wdtDisable" when esp8266:
            case "wdtEnable" when esp8266:
                WatchdogDoEsp(metodo);
                return Valor.Vazio;
        }

        throw new ErroDeExecucao(esp8266 && metodo is "getEfuseMac" or "getChipModel" or "getChipCores"
                ? $"ESP.{metodo}() e da ESP32: no ESP8266 use ESP.getChipId()."
                : !esp8266 && metodo == "getChipId"
                    ? "ESP.getChipId() e do ESP8266: na ESP32 use ESP.getEfuseMac()."
                    : $"ESP.{metodo}() ainda nao e suportado pelo simulador.", c.Linha);
    }

    // =====================================================================
    //  Despacho
    // =====================================================================

    Valor ChamarRede(Objeto obj, string metodo, Chamada c, Ambiente amb) => obj.Classe switch
    {
        "HTTPClient" => ChamarHttp(obj, metodo, c, amb),
        "WebServer" => ChamarServidorWeb(obj, metodo, c, amb),
        "WiFiServer" => ChamarServidorCru(obj, metodo, c, amb),
        "WiFiClient" or "WiFiClientSecure" => ChamarClienteTcp(obj, metodo, c, amb),
        "PubSubClient" => ChamarMqtt(obj, metodo, c, amb),
        "BluetoothSerial" => ChamarBluetooth(obj, metodo, c, amb),
        "IPAddress" => ChamarIp(obj, metodo, c, amb),
        _ => throw new ErroDeExecucao($"{obj.Classe}.{metodo}() ainda nao e suportado.", c.Linha),
    };

    Valor ChamarIp(Objeto obj, string metodo, Chamada c, Ambiente amb)
    {
        switch (metodo)
        {
            case "toString":
                return Valor.DeTexto(obj.Texto ?? "0.0.0.0");

            case "fromString":
            {
                string texto = c.Args.Count > 0 ? Avaliar(c.Args[0], amb).Mostrar() : "";
                bool vale = System.Net.IPAddress.TryParse(texto, out _);
                if (vale) obj.Texto = texto;
                return Valor.DeBool(vale);
            }
        }
        throw new ErroDeExecucao($"IPAddress.{metodo}() ainda nao e suportado.", c.Linha);
    }

    // =====================================================================
    //  HTTPClient
    // =====================================================================

    Valor ChamarHttp(Objeto obj, string metodo, Chamada c, Ambiente amb)
    {
        var http = obj.Rede as EstadoHttp ?? (EstadoHttp)(obj.Rede = new EstadoHttp());
        var args = c.Args.Select(a => Avaliar(a, amb)).ToList();
        string S(int i) => i < args.Count ? args[i].Mostrar() : "";

        Valor Enviar(string metodoHttp, string corpo)
        {
            ExigirWifi(c.Linha, $"http.{metodo}()");
            if (string.IsNullOrEmpty(http.Url))
                throw new ErroDeExecucao(
                    $"http.{metodo}() sem http.begin(url) antes.", c.Linha);

            http.Resposta = Maq.Rede.Requisitar(Maq, metodoHttp, http.Url, http.Cabecalhos,
                corpo, http.TempoMs, http.Inseguro);
            return Valor.DeInt(http.Resposta.Codigo);
        }

        switch (metodo)
        {
            case "begin":
            {
                // begin(url) / begin(client, url) / begin(host, porta, caminho)
                var objetos = ObjetosDosArgumentos(c, amb);
                var textos = args.Where((_, i) => objetos[i] is null).ToList();
                var cliente = objetos.FirstOrDefault(o => o?.Rede is EstadoCliente);
                http.Inseguro = (cliente?.Rede as EstadoCliente)?.Inseguro ?? false;

                http.Url = textos.Count >= 3
                    ? $"http://{textos[0].Mostrar()}:{(int)textos[1].Num}{textos[2].Mostrar()}"
                    : textos.Count >= 1 ? textos[0].Mostrar() : null;

                http.Cabecalhos.Clear();
                http.Resposta = null;
                return Valor.DeBool(Uri.TryCreate(http.Url, UriKind.Absolute, out _));
            }

            case "addHeader": http.Cabecalhos[S(0)] = S(1); return Valor.Vazio;
            case "setUserAgent": http.Cabecalhos["User-Agent"] = S(0); return Valor.Vazio;
            case "setTimeout": http.TempoMs = Math.Clamp((int)args[0].Num, 100, 60000); return Valor.Vazio;

            case "setAuthorization":
            {
                string credencial = args.Count >= 2
                    ? Convert.ToBase64String(Encoding.UTF8.GetBytes($"{S(0)}:{S(1)}"))
                    : S(0);
                http.Cabecalhos["Authorization"] = "Basic " + credencial;
                return Valor.Vazio;
            }

            case "GET": return Enviar("GET", null);
            case "POST": return Enviar("POST", S(0));
            case "PUT": return Enviar("PUT", S(0));
            case "PATCH": return Enviar("PATCH", S(0));
            case "sendRequest": return Enviar(S(0).ToUpperInvariant(), args.Count > 1 ? S(1) : null);

            case "getString": return Valor.DeTexto(http.Resposta?.Corpo ?? "");
            case "getSize":
                return Valor.DeInt(http.Resposta is null ? -1 : TextoArduino.ParaBytes(http.Resposta.Corpo).Length);
            case "header": return Valor.DeTexto(http.Resposta?.Cabecalhos.GetValueOrDefault(S(0)) ?? "");
            case "hasHeader": return Valor.DeBool(http.Resposta?.Cabecalhos.ContainsKey(S(0)) ?? false);
            case "connected": return Valor.DeBool(http.Resposta is not null);
            case "end": return Valor.Vazio;

            case "errorToString":
                return Valor.DeTexto((int)args[0].Num switch
                {
                    Rede.ErroConexaoRecusada => "connection refused",
                    Rede.ErroTempoDeLeitura => "read timeout",
                    _ => "erro " + (int)args[0].Num,
                });

            case "setConnectTimeout":
            case "setReuse":
            case "useHTTP10":
            case "setFollowRedirects":
            case "collectHeaders":
                return Valor.Vazio;

            case "getStream":
            case "getStreamPtr":
                throw new ErroDeExecucao(
                    "http.getStream() ainda nao e suportado. Use http.getString().", c.Linha);
        }

        throw new ErroDeExecucao($"http.{metodo}() ainda nao e suportado pelo simulador.", c.Linha);
    }

    /// <summary>Quais argumentos sao objetos (e quais sao valores comuns).</summary>
    List<Objeto> ObjetosDosArgumentos(Chamada c, Ambiente amb)
    {
        var achados = new List<Objeto>();
        foreach (var a in c.Args)
            achados.Add(SemEndereco(a) is Variavel v && amb.BuscarObjeto(v.Nome, out var obj) ? obj : null);
        return achados;
    }

    // =====================================================================
    //  WebServer (rotas)
    // =====================================================================

    Valor ChamarServidorWeb(Objeto obj, string metodo, Chamada c, Ambiente amb)
    {
        var s = (EstadoServidor)obj.Rede;

        // Estes recebem o NOME de uma funcao: nao passam pela avaliacao.
        switch (metodo)
        {
            case "on":
            {
                if (c.Args.Count < 2)
                    throw new ErroDeExecucao("server.on() espera o caminho e a funcao que atende.", c.Linha);

                string caminho = Avaliar(c.Args[0], amb).Mostrar();
                int metodoHttp = 127;
                int iRotina = 1;
                if (c.Args.Count >= 3) { metodoHttp = (int)Avaliar(c.Args[1], amb).Num; iRotina = 2; }

                s.Rotas.Add((caminho, metodoHttp, RotinaDe(c.Args[iRotina], amb, "server.on()")));
                return Valor.Vazio;
            }

            case "onNotFound":
                s.NaoEncontrado = RotinaDe(c.Args[0], amb, "server.onNotFound()");
                return Valor.Vazio;
        }

        var args = c.Args.Select(a => Avaliar(a, amb)).ToList();
        string S(int i) => i < args.Count ? args[i].Mostrar() : "";

        switch (metodo)
        {
            case "begin":
                ExigirWifi(c.Linha, "server.begin()");
                if (args.Count > 0) s.Porta = (int)args[0].Num;
                s.Servidor = Maq.Rede.AbrirServidor(Maq, s.Porta, cru: false);
                return Valor.Vazio;

            case "handleClient":
                AtenderPedidoWeb(s);
                return Valor.Vazio;

            case "close":
            case "stop":
                if (s.Servidor is not null) Maq.Rede.FecharServidor(s.Servidor);
                s.Servidor = null;
                return Valor.Vazio;

            case "send":
            case "send_P":
                s.Codigo = args.Count > 0 ? (int)args[0].Num : 200;
                s.Tipo = args.Count > 1 ? S(1) : "text/plain; charset=utf-8";
                s.Corpo.Clear().Append(args.Count > 2 ? S(2) : "");
                s.Respondeu = true;
                return Valor.Vazio;

            case "sendContent":
            case "sendContent_P":
                s.Corpo.Append(S(0));
                s.Respondeu = true;
                return Valor.Vazio;

            case "sendHeader":
                s.CabecalhosDaResposta.Add(new(S(0), S(1)));
                return Valor.Vazio;

            case "setContentLength":
                return Valor.Vazio;

            case "arg":
            {
                if (args.Count > 0 && !args[0].EhTexto)
                {
                    int i = (int)args[0].Num;
                    return Valor.DeTexto(i >= 0 && i < s.Argumentos.Count ? s.Argumentos[i].Value : "");
                }
                var achado = s.Argumentos.LastOrDefault(kv => kv.Key == S(0));
                return Valor.DeTexto(achado.Key is null ? "" : achado.Value);
            }

            case "argName":
            {
                int i = (int)(args.Count > 0 ? args[0].Num : 0);
                return Valor.DeTexto(i >= 0 && i < s.Argumentos.Count ? s.Argumentos[i].Key : "");
            }

            case "args": return Valor.DeInt(s.Argumentos.Count);
            case "hasArg": return Valor.DeBool(s.Argumentos.Any(kv => kv.Key == S(0)));
            case "uri": return Valor.DeTexto(s.Atual?.Caminho ?? "");

            // O que o "{}" da rota capturou: "/toggle/{}" atendendo
            // "/toggle/2" deixa "2" no pathArg(0).
            case "pathArg":
            {
                int i = args.Count > 0 ? (int)args[0].Num : 0;
                return Valor.DeTexto(i >= 0 && i < s.Capturados.Count ? s.Capturados[i] : "");
            }
            case "pathArgs": return Valor.DeInt(s.Capturados.Count);
            case "method": return Valor.DeInt(CodigoDoMetodo(s.Atual?.Metodo));
            case "header": return Valor.DeTexto(s.Atual?.Cabecalhos.GetValueOrDefault(S(0)) ?? "");
            case "hasHeader": return Valor.DeBool(s.Atual?.Cabecalhos.ContainsKey(S(0)) ?? false);
            case "headers": return Valor.DeInt(s.Atual?.Cabecalhos.Count ?? 0);
            case "collectHeaders": case "enableCORS": case "enableCrossOrigin": return Valor.Vazio;

            case "authenticate":
            {
                string esperado = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{S(0)}:{S(1)}"));
                return Valor.DeBool(s.Atual?.Cabecalhos.GetValueOrDefault("Authorization") == esperado);
            }

            case "requestAuthentication":
                s.Codigo = 401;
                s.Tipo = "text/plain; charset=utf-8";
                s.Corpo.Clear().Append("precisa de usuario e senha");
                s.CabecalhosDaResposta.Add(new("WWW-Authenticate", "Basic realm=\"CircuitSketch\""));
                s.Respondeu = true;
                return Valor.Vazio;
        }

        throw new ErroDeExecucao($"server.{metodo}() ainda nao e suportado pelo simulador.", c.Linha);
    }

    /// <summary>
    /// Atende UM pedido da fila, como o handleClient() da placa: acha a
    /// rota, chama a funcao do sketch e devolve o que ela mandou pelo
    /// server.send().
    /// </summary>
    void AtenderPedidoWeb(EstadoServidor s)
    {
        var pedido = s.Servidor?.Proximo();
        if (pedido is null) return;

        s.Atual = pedido;
        s.Argumentos = pedido.Argumentos();
        s.Codigo = 200;
        s.Tipo = "text/plain; charset=utf-8";
        s.Corpo.Clear();
        s.CabecalhosDaResposta.Clear();
        s.Respondeu = false;

        try
        {
            int metodo = CodigoDoMetodo(pedido.Metodo);
            s.Capturados.Clear();

            var rota = s.Rotas.FirstOrDefault(r => r.Caminho == pedido.Caminho
                                                   && (r.Metodo == 127 || r.Metodo == metodo));

            // Rota com "{}": "/toggle/{}" atende "/toggle/2", e o pedaco
            // capturado sai depois no server.pathArg(0).
            if (rota.Rotina is null)
                foreach (var candidata in s.Rotas)
                {
                    if (candidata.Metodo != 127 && candidata.Metodo != metodo) continue;
                    if (!CombinaComChaves(candidata.Caminho, pedido.Caminho, s.Capturados)) continue;
                    rota = candidata;
                    break;
                }

            if (rota.Rotina is not null) ChamarRotina(rota.Rotina);
            else if (s.NaoEncontrado is not null) ChamarRotina(s.NaoEncontrado);
            else
            {
                s.Codigo = 404;
                s.Corpo.Append($"Nao achei {pedido.Caminho}. O sketch registrou esta rota com server.on()?");
                s.Respondeu = true;
            }
        }
        finally
        {
            pedido.Responder(s.Respondeu
                ? PedidoHttp.MontarResposta(s.Codigo, s.Tipo, s.Corpo.ToString(), s.CabecalhosDaResposta)
                : PedidoHttp.MontarResposta(500, "text/plain; charset=utf-8",
                    $"a rota {pedido.Caminho} rodou mas nao chamou server.send()"));
            s.Atual = null;
        }
    }

    /// <summary>
    /// A rota "/led/{}/brilho/{}" bate com "/led/2/brilho/80"?
    ///
    /// Pedaco por pedaco: "{}" aceita qualquer coisa que nao tenha barra
    /// e guarda o que aceitou; o resto tem que ser igual.
    /// </summary>
    static bool CombinaComChaves(string molde, string caminho, List<string> capturados)
    {
        if (!molde.Contains("{}", StringComparison.Ordinal)) return false;

        var partesDoMolde = molde.Split('/');
        var partesDoCaminho = caminho.Split('/');
        if (partesDoMolde.Length != partesDoCaminho.Length) return false;

        var achados = new List<string>();
        for (int i = 0; i < partesDoMolde.Length; i++)
        {
            if (partesDoMolde[i] == "{}") { achados.Add(partesDoCaminho[i]); continue; }
            if (partesDoMolde[i] != partesDoCaminho[i]) return false;
        }

        capturados.Clear();
        capturados.AddRange(achados);
        return true;
    }

    static int CodigoDoMetodo(string metodo) => metodo switch
    {
        "DELETE" => 0,
        "HEAD" => 2,
        "POST" => 3,
        "PUT" => 4,
        "OPTIONS" => 6,
        "PATCH" => 28,
        _ => 1,
    };

    // =====================================================================
    //  WiFiServer (bytes crus)
    // =====================================================================

    Valor ChamarServidorCru(Objeto obj, string metodo, Chamada c, Ambiente amb)
    {
        var s = (EstadoServidor)obj.Rede;
        var args = c.Args.Select(a => Avaliar(a, amb)).ToList();

        switch (metodo)
        {
            case "begin":
                ExigirWifi(c.Linha, "server.begin()");
                if (args.Count > 0) s.Porta = (int)args[0].Num;
                s.Servidor = Maq.Rede.AbrirServidor(Maq, s.Porta, cru: true, tcpDeVerdade: RodaNativo);
                return Valor.Vazio;

            case "available":
            case "accept":
            {
                // O sketch compilado: o client e um objeto que ele guarda (o
                // WiFiClient de verdade fecha quando a ultima copia some), e a
                // conexao do navegador e TCP de verdade, aberta enquanto ele
                // quiser -- o WebSocket.
                if (RodaNativo)
                {
                    ConexaoTcp nova = s.Servidor?.ProximaConexao() is { } tcp ? ConexaoTcp.DaPonte(tcp)
                        : s.Servidor?.Proximo() is { } doPedido ? ConexaoTcp.DoServidor(doPedido, "10.0.0.1") : null;
                    if (nova is null) return Valor.DeTexto("");
                    return Valor.DeReferencia(new Objeto("WiFiClient") { Rede = new EstadoCliente { Conexao = nova, PortaLocal = s.Porta } });
                }

                // O client da volta anterior que o sketch esqueceu aberto:
                // responde agora, senao o navegador fica girando.
                s.UltimaConexao?.FecharSeEsquecida();
                s.UltimaConexao = null;

                var pedido = s.Servidor?.Proximo();
                conexaoRecente = pedido is null ? null : ConexaoTcp.DoServidor(pedido, "10.0.0.1");
                temConexaoRecente = true;
                s.UltimaConexao = conexaoRecente;
                return Valor.DeBool(conexaoRecente is not null);
            }

            case "hasClient":
                return Valor.DeBool((s.Servidor?.Esperando ?? 0) > 0);

            case "close":
            case "stop":
            case "end":
                if (s.Servidor is not null) Maq.Rede.FecharServidor(s.Servidor);
                s.Servidor = null;
                return Valor.Vazio;

            case "setNoDelay":
                return Valor.Vazio;
        }

        throw new ErroDeExecucao($"server.{metodo}() ainda nao e suportado pelo simulador.", c.Linha);
    }

    // =====================================================================
    //  WiFiClient
    // =====================================================================

    Valor ChamarClienteTcp(Objeto obj, string metodo, Chamada c, Ambiente amb)
    {
        var estado = obj.Rede as EstadoCliente
                     ?? (EstadoCliente)(obj.Rede = new EstadoCliente { Seguro = obj.Classe == "WiFiClientSecure" });
        var args = c.Args.Select(a => Avaliar(a, amb)).ToList();

        // Do C++ compilado: os bytes como estao (o quadro do WebSocket nao e texto).
        if (metodo == "__escrever_bytes")
        {
            byte[] dados = args.Count > 0 && args[0].Referencia is Valor[] v ? [.. v.Select(x => (byte)((long)x.Num & 0xFF))] : [];
            estado.Conexao?.Escrever(dados);
            return Valor.DeInt(dados.Length);
        }
        string S(int i) => i < args.Count ? args[i].Mostrar() : "";

        switch (metodo)
        {
            case "setInsecure": estado.Inseguro = true; return Valor.Vazio;
            case "setCACert": case "setCertificate": case "setPrivateKey":
            case "setTimeout": case "setNoDelay": case "flush":
                return Valor.Vazio;

            case "connect":
            {
                ExigirWifi(c.Linha, "client.connect()");
                estado.Conexao?.Fechar();
                int porta = args.Count > 1 ? (int)args[1].Num : estado.Seguro ? 443 : 80;
                estado.Conexao = Maq.Rede.ConectarTcp(Maq, S(0), porta, estado.Seguro, estado.Inseguro);
                return Valor.DeInt(estado.Conexao is null ? 0 : 1);
            }

            case "connected":
                return Valor.DeBool(estado.Conexao?.Conectada ?? false);

            case "stop":
                estado.Conexao?.Fechar();
                return Valor.Vazio;

            case "remoteIP": return Valor.DeTexto(estado.Conexao?.IpRemoto ?? "0.0.0.0");
            case "remotePort": return Valor.DeInt(estado.Conexao?.PortaRemota ?? 0);
            case "localIP": return Valor.DeTexto(Maq.Rede is { } r && r.NaRede(Maq) ? r.Wifi(Maq).Ip : "0.0.0.0");
            case "localPort": return Valor.DeInt(estado.PortaLocal);
            // O certificado do servidor (DER em base64), para o NetworkClientSecure do C++ compilado.
            case "__certificado":
                return Valor.DeTexto(estado.Conexao?.CertificadoRemoto is { } der ? Convert.ToBase64String(der) : "");
            case "available": return Valor.DeInt(estado.Conexao?.Disponivel ?? 0);

            case "print":
            case "println":
            case "printf":
            case "write":
            {
                string texto = metodo switch
                {
                    "printf" => Formatar(args),
                    "write" => ComoEscrita(args),
                    _ => Imprimivel(args),
                };
                if (metodo == "println") texto += "\r\n";
                estado.Conexao?.Escrever(TextoArduino.ParaBytes(texto));
                return Valor.DeInt(TextoArduino.ParaBytes(texto).Length);
            }
        }

        if (estado.Conexao is not null)
        {
            // Para outra placa da bancada: e a leitura que dispara o envio
            // do pedido, entao isto precisa vir antes.
            _ = estado.Conexao.Disponivel;
            if (ChamarFluxo(estado.Conexao.Entrada, metodo, args, out var lido)) return lido;
        }
        else
        {
            switch (metodo)
            {
                case "read": case "peek": return Valor.DeInt(-1);
                case "readString": case "readStringUntil": return Valor.DeTexto("");
                case "parseInt": return Valor.DeInt(0);
                case "parseFloat": return Valor.DeReal(0);
            }
        }

        throw new ErroDeExecucao($"client.{metodo}() ainda nao e suportado pelo simulador.", c.Linha);
    }

    // =====================================================================
    //  PubSubClient (MQTT)
    // =====================================================================

    /// <summary>
    /// A conta do publish() do PubSubClient: cabecalho (5), tamanho do
    /// topico (2), o topico e a carga tem que caber no buffer. Senao ele
    /// devolve false sem mandar nada -- e o sketch que nao confere o retorno
    /// fica sem entender por que o painel nao recebe.
    /// </summary>
    bool CabeNoBuffer(EstadoMqtt estado, string topico, int carga, int linha)
    {
        int pacote = 5 + 2 + TextoArduino.ParaBytes(topico).Length + carga;
        if (pacote <= estado.Buffer) return true;

        if (!estado.AvisouTamanho)
        {
            estado.AvisouTamanho = true;
            Maq.Rede?.Anotar(Maq, "aviso", $"publish(\"{topico}\") devolveu false: a mensagem ocupa {pacote} bytes e o "
                                       + $"buffer do PubSubClient tem {estado.Buffer}. Na placa acontece o mesmo; "
                                       + "client.setBufferSize(1024) no setup() resolve.");
        }
        _ = linha;
        return false;
    }

    Valor ChamarMqtt(Objeto obj, string metodo, Chamada c, Ambiente amb)
    {
        var estado = (EstadoMqtt)obj.Rede;
        var cliente = estado.Cliente;
        var rede = Maq.Rede;

        // Recebe o NOME de uma funcao: nao passa pela avaliacao.
        if (metodo == "setCallback")
        {
            estado.Callback = RotinaDe(c.Args[0], amb, "client.setCallback()");
            return Valor.DeBool(true);
        }

        var args = c.Args.Select(a => Avaliar(a, amb)).ToList();
        string S(int i) => i < args.Count ? args[i].Mostrar() : "";

        // NULL no lugar do usuario e da senha: "connect(id, NULL, NULL)".
        string Opcional(int i) => i < args.Count && !args[i].EhTexto && args[i].Referencia is null ? null
                                : i < args.Count ? args[i].Mostrar() : null;

        switch (metodo)
        {
            case "setServer":
                cliente.Host = S(0);
                cliente.Porta = args.Count > 1 ? (int)args[1].Num : 1883;
                return Valor.DeBool(true);

            case "setKeepAlive": cliente.KeepAlive = Math.Clamp((int)args[0].Num, 5, 300); return Valor.DeBool(true);

            case "setBufferSize":
                estado.Buffer = Math.Clamp((int)(args.Count > 0 ? args[0].Num : 256), 16, 65535);
                return Valor.DeBool(true);
            case "getBufferSize": return Valor.DeInt(estado.Buffer);

            case "setClient":
                if (c.Args.Count > 0 && ObjetoDaExpressao(c.Args[0], amb)?.Rede is EstadoCliente outro)
                    estado.Transporte = outro;
                return Valor.DeBool(true);

            case "setSocketTimeout": case "setStream":
                return Valor.DeBool(true);

            case "connect":
            {
                ExigirWifi(c.Linha, "client.connect()");

                // As formas do PubSubClient:
                //   connect(id)
                //   connect(id, usuario, senha)
                //   connect(id, topicoWill, qosWill, retidaWill, mensagemWill)
                //   connect(id, usuario, senha, topicoWill, qosWill, retidaWill, mensagemWill[, sessaoLimpa])
                string usuario = null, senha = null;
                int will = -1;
                if (args.Count == 3) { usuario = Opcional(1); senha = Opcional(2); }
                else if (args.Count == 5) will = 1;
                else if (args.Count >= 7) { usuario = Opcional(1); senha = Opcional(2); will = 3; }

                cliente.Testamento = will > 0 && Opcional(will) is { Length: > 0 } topicoWill
                    ? new TestamentoMqtt(topicoWill, TextoArduino.ParaBytes(S(will + 3)),
                                         args[will + 2].Verdadeiro, (int)args[will + 1].Num)
                    : null;

                cliente.Tls = estado.Transporte?.Seguro == true;
                cliente.SemConferirCertificado = estado.Transporte?.Inseguro == true;
                return Valor.DeBool(rede.ConectarMqtt(cliente, S(0), usuario, senha));
            }

            case "connected": return Valor.DeBool(cliente.Conectado);
            case "state": return Valor.DeInt(cliente.Estado);

            case "disconnect":
                rede.DesconectarMqtt(cliente);
                return Valor.Vazio;

            case "publish":
            case "publish_P":
            {
                // O vetor de bytes vai cru, com os zeros do meio: e o que
                // "publish(topico, buffer, tamanho)" manda de verdade.
                byte[] carga = c.Args.Count > 1 && VetorDoArgumento(c.Args[1], amb, out var vetor)
                    ? [.. vetor.Select(v => (byte)((long)v.Num & 0xFF))]
                    : TextoArduino.ParaBytes(S(1));

                // publish(topico, carga, retida) e publish(topico, carga,
                // tamanho[, retida]): o terceiro e booleano numa, numero na
                // outra. So o tipo separa as duas -- "publish(t, b, 1)" manda
                // UM byte, nao uma mensagem retida.
                bool comTamanho = args.Count >= 4 || (args.Count == 3 && !args[2].EhBooleano);
                if (comTamanho)
                {
                    int tamanho = Math.Clamp((int)args[2].Num, 0, carga.Length);
                    carga = carga[..tamanho];
                }
                else if (c.Args.Count > 1 && VetorDoArgumento(c.Args[1], amb, out _))
                {
                    // Sem tamanho, o char* vai ate o zero, como o strlen().
                    int zero = Array.IndexOf(carga, (byte)0);
                    if (zero >= 0) carga = carga[..zero];
                }
                bool retida = args.Count == 3 && !comTamanho ? args[2].Verdadeiro
                            : args.Count >= 4 && args[3].Verdadeiro;

                if (!CabeNoBuffer(estado, S(0), carga.Length, c.Linha)) return Valor.DeBool(false);
                return Valor.DeBool(rede.PublicarMqtt(cliente, S(0), carga, retida));
            }

            // A mensagem longa, escrita aos pedacos: beginPublish(topico,
            // tamanho, retida), write()/print() e endPublish().
            case "beginPublish":
                if (!cliente.Conectado) return Valor.DeBool(false);
                estado.TopicoAberto = S(0);
                estado.CargaAberta = [];
                estado.RetidaAberta = args.Count > 2 && args[2].Verdadeiro;
                return Valor.DeBool(true);

            case "write":
            case "print":
            case "println":
            {
                if (estado.CargaAberta is null) return Valor.DeInt(0);
                byte[] pedaco = metodo == "write" && args.Count == 1 && !args[0].EhTexto
                    ? [(byte)((long)args[0].Num & 0xFF)]
                    : metodo == "write" && c.Args.Count > 1 && VetorDoArgumento(c.Args[0], amb, out var bytes)
                        ? [.. bytes.Take((int)args[1].Num).Select(v => (byte)((long)v.Num & 0xFF))]
                        : TextoArduino.ParaBytes(Imprimivel(args) + (metodo == "println" ? "\r\n" : ""));
                estado.CargaAberta.AddRange(pedaco);
                return Valor.DeInt(pedaco.Length);
            }

            case "endPublish":
            {
                if (estado.CargaAberta is null) return Valor.DeInt(0);
                var carga = estado.CargaAberta.ToArray();
                string topico = estado.TopicoAberto;
                estado.CargaAberta = null;
                return Valor.DeInt(rede.PublicarMqtt(cliente, topico, carga, estado.RetidaAberta) ? 1 : 0);
            }

            case "subscribe":
                if (9 + TextoArduino.ParaBytes(S(0)).Length > estado.Buffer)
                {
                    rede.Anotar(Maq, "aviso", $"subscribe(\"{S(0)}\") devolveu false: o tópico não cabe no buffer "
                                          + $"de {estado.Buffer} bytes do PubSubClient");
                    return Valor.DeBool(false);
                }
                return Valor.DeBool(rede.AssinarMqtt(cliente, S(0)));
            case "unsubscribe": return Valor.DeBool(rede.CancelarMqtt(cliente, S(0)));

            case "loop":
            {
                bool vivo = rede.ManterMqtt(cliente);

                // E aqui, e so aqui, que o callback roda -- igual a
                // biblioteca de verdade. Sketch sem client.loop() nunca
                // recebe nada, e o aluno precisa ver isso acontecer.
                for (int i = 0; i < 50; i++)
                {
                    var mensagem = cliente.Proxima();
                    if (mensagem is null) break;
                    if (estado.Callback is null) continue;

                    // Maior que o buffer: a biblioteca le e joga fora, e o
                    // callback nunca fica sabendo.
                    int pacote = 5 + 2 + TextoArduino.ParaBytes(mensagem.Topico).Length + mensagem.Carga.Length;
                    if (pacote > estado.Buffer)
                    {
                        rede.Anotar(Maq, "aviso", $"chegou uma mensagem de {pacote} bytes em {mensagem.Topico}, e o "
                                              + $"buffer do PubSubClient tem {estado.Buffer}: ela foi jogada fora, como "
                                              + "na placa. client.setBufferSize(1024) no setup() resolve.");
                        continue;
                    }

                    var carga = new Valor[mensagem.Carga.Length + 1];
                    for (int k = 0; k < mensagem.Carga.Length; k++) carga[k] = Valor.DeInt(mensagem.Carga[k]);
                    carga[^1] = Valor.DeInt(0);

                    ChamarRotina(estado.Callback, Valor.DeTexto(mensagem.Topico), carga,
                                 Valor.DeInt(mensagem.Carga.Length));
                }
                return Valor.DeBool(vivo);
            }
        }

        throw new ErroDeExecucao($"client.{metodo}() ainda nao e suportado pelo simulador.", c.Linha);
    }

    // =====================================================================
    //  BluetoothSerial
    // =====================================================================

    Valor ChamarBluetooth(Objeto obj, string metodo, Chamada c, Ambiente amb)
    {
        if (!Maq.Perfil.TemBluetoothClassico)
            throw new ErroDeExecucao(
                $"SerialBT.{metodo}(): a {Maq.Perfil.Nome} nao tem Bluetooth classico. "
                + (Maq.Perfil.Id == "nano-rp2040-connect"
                    ? "A Nano RP2040 Connect so tem Bluetooth LE, que o simulador ainda nao cobre."
                    : "O BluetoothSerial e da ESP32."), c.Linha);

        var estado = (EstadoBt)obj.Rede;

        // register_callback(funcao): os eventos da conexao -- o celular que
        // conectou (ESP_SPP_SRV_OPEN_EVT), que saiu (ESP_SPP_CLOSE_EVT).
        if (metodo == "register_callback")
        {
            estado.AoEvento = RotinaDe(c.Args[0], amb, "SerialBT.register_callback()");
            if (!bluetoothsDoSketch.Contains(estado)) bluetoothsDoSketch.Add(estado);
            return Valor.DeInt(0);
        }

        if (metodo is "onData" or "onConfirmRequest" or "onAuthComplete")
            return Valor.DeBool(true);

        EntregarBluetooth();

        var args = c.Args.Select(a => Avaliar(a, amb)).ToList();
        string S(int i) => i < args.Count ? args[i].Mostrar() : "";
        var dispositivo = estado.Dispositivo;

        switch (metodo)
        {
            case "begin":
                // O Bluetooth classico pega a parte dele da heap; sem lugar, o begin() da false.
                if (!LigarRadio("bt", c.Linha)) return Valor.DeBool(false);
                estado.Dispositivo = Maq.Rede.IniciarBluetooth(Maq,
                    args.Count > 0 ? S(0) : "ESP32", args.Count > 1 && args[1].Verdadeiro);
                return Valor.DeBool(true);

            case "end":
                Maq.Rede.EsquecerBluetooth(Maq);
                DesligarRadio("bt");
                estado.Dispositivo = null;
                return Valor.Vazio;

            case "connect":
                return Valor.DeBool(dispositivo is not null && Maq.Rede.ConectarBluetooth(dispositivo, S(0)));

            case "connected":
            case "hasClient":
            case "isReady":
                return Valor.DeBool(dispositivo?.TemConexao ?? false);

            case "disconnect":
                if (dispositivo?.Par is { } par) { par.Par = null; dispositivo.Par = null; }
                return Valor.DeBool(true);

            case "print":
            case "println":
            case "printf":
            case "write":
            {
                string texto = metodo switch
                {
                    "printf" => Formatar(args),
                    "write" => ComoEscrita(args),
                    _ => Imprimivel(args),
                };
                if (metodo == "println") texto += "\r\n";
                dispositivo?.Enviar(TextoArduino.ParaBytes(texto));
                return Valor.DeInt(TextoArduino.ParaBytes(texto).Length);
            }

            case "setPin": case "enableSSP": case "setTimeout": case "flush":
            case "deleteAllBondedDevices": case "setName":
                return Valor.DeBool(true);

            case "getBtAddressString":
                return Valor.DeTexto(Maq.Rede.Wifi(Maq).Mac);

            // O discover do C++ compilado: "nome|endereco|rssi" de cada placa visivel.
            case "__descobrir":
                return Valor.DeTexto(dispositivo is null ? "" : string.Join("\n",
                    Maq.Rede.BluetoothVisiveis(dispositivo).Select(d => $"{d.Nome}|{Maq.Rede.Wifi(d.Dona).Mac}|-50")));

            // O connect(BTAddress): a placa visivel com esse endereco.
            case "__conectar_endereco":
            {
                var alvo = dispositivo is null ? null
                    : Maq.Rede.BluetoothVisiveis(dispositivo).FirstOrDefault(d => string.Equals(Maq.Rede.Wifi(d.Dona).Mac, S(0), StringComparison.OrdinalIgnoreCase));
                return Valor.DeBool(alvo is not null && Maq.Rede.ConectarBluetooth(dispositivo, alvo.Nome));
            }
        }

        if (dispositivo is not null && ChamarFluxo(dispositivo.Entrada, metodo, args, out var lido)) return lido;

        switch (metodo)
        {
            case "available": return Valor.DeInt(0);
            case "read": case "peek": return Valor.DeInt(-1);
            case "readString": case "readStringUntil": return Valor.DeTexto("");
        }

        throw new ErroDeExecucao($"SerialBT.{metodo}() ainda nao e suportado pelo simulador.", c.Linha);
    }
}
