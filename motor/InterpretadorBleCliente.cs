namespace Simulador;

/// <summary>
/// A placa do outro lado do BLE: procurando (BLEScan) e conectando no
/// servidor GATT de outra placa (BLEClient, BLERemoteService,
/// BLERemoteCharacteristic) -- a API do core 3.3 da ESP32. O servidor pode
/// ser outra ESP32 (a biblioteca BLE) ou uma placa com a ArduinoBLE; os dois
/// lados conversam pela <see cref="Rede"/>, como o celular conversa.
///
/// O que o core 3.x mudou e o simulador segue: o start() bloqueante devolve
/// BLEScanResults* (no 2.x era o objeto), e o getName()/readValue() devolvem
/// String (no 2.x, std::string).
/// </summary>
public partial class Interpretador
{
    /// <summary>A varredura do BLEScan.</summary>
    sealed class EstadoScanBle
    {
        public Objeto Callbacks;
        public bool Duplicados;
        public bool Ativa, Parou;
        public double FimUs;
        public Rotina AoTerminar;
        public readonly List<Objeto> Resultados = [];
        public readonly HashSet<string> Vistos = new(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>O que um BLEAdvertisedDevice guarda: o anuncio que a varredura viu.</summary>
    sealed class AnuncioVisto
    {
        public RadioBle Radio;
        public string Nome, Endereco;
        public int Rssi;
        public List<string> Uuids = [];
        public string FabricanteHex = "", ServicoUuid = "", ServicoHex = "";
    }

    /// <summary>Os bytes que um BLEAdvertisementData leva (o fabricante e o dado de servico, em hexadecimal).</summary>
    sealed class DadosDoAnuncioBle
    {
        public string FabricanteHex = "", ServicoUuid = "", ServicoHex = "";
    }

    sealed class EstadoClienteBle
    {
        public ConexaoBle Conexao;
        public Objeto Callbacks;
    }

    /// <summary>Um BLERemoteService ou BLERemoteCharacteristic: o lado do cliente de um servico ou caracteristica.</summary>
    sealed class EstadoRemotoBle
    {
        public Objeto Cliente;
        public ConexaoBle Conexao;
        public ServicoBle Servico;
        public CaracteristicaBle Caracteristica;
        public Rotina AoNotificar;
    }

    Objeto scanBle, resultadosBle;
    readonly List<Objeto> clientesBle = [];
    readonly Dictionary<(ConexaoBle, CaracteristicaBle), Objeto> remotasBle = [];

    // =====================================================================
    //  BLEDevice::getScan() e BLEDevice::createClient()
    // =====================================================================

    Objeto ScanBle()
    {
        if (scanBle is not null) return scanBle;
        scanBle = new Objeto("BLEScan") { Rede = new EstadoScanBle() };
        resultadosBle = new Objeto("BLEScanResults") { Rede = scanBle.Rede };
        return scanBle;
    }

    Objeto NovoClienteBle()
    {
        var cliente = new Objeto("BLEClient") { Rede = new EstadoClienteBle() };
        lock (clientesBle) clientesBle.Add(cliente);
        return cliente;
    }

    static readonly HashSet<string> ClassesDoClienteBle =
    [
        "BLEScan", "BLEScanResults", "BLEAdvertisedDevice", "BLEClient", "BLERemoteService", "BLERemoteCharacteristic",
    ];

    static bool EhClasseDoClienteBle(string classe) => ClassesDoClienteBle.Contains(classe);

    /// <summary>"new BLEAdvertisedDevice(advertisedDevice)": a copia do anuncio que o callback recebeu.</summary>
    void MontarObjetoDoClienteBle(Objeto obj, CmdDeclObj d, Ambiente amb)
    {
        if (d.Classe == "BLEAdvertisedDevice" && d.Args.Count > 0
            && Avaliar(d.Args[0], amb).Referencia is Objeto { Rede: AnuncioVisto visto })
            obj.Rede = visto;
        else if (d.Classe == "BLEAdvertisedDevice") obj.Rede = new AnuncioVisto { Nome = "", Endereco = "00:00:00:00:00:00" };
        else if (d.Classe == "BLEClient") obj.Rede = new EstadoClienteBle();

        // "BLEScanResults resultados = pBLEScan->start(5);" e do core 2.x:
        // no 3.x o start() devolve um ponteiro, e a linha nem compila.
        if (d.Classe == "BLEScanResults" && d.Inicial is Chamada { Alvo: Membro { Nome: "start" } })
            throw new ErroDeExecucao(Idioma.T("conversion from 'BLEScanResults*' to non-scalar type 'BLEScanResults' requested -- no core 3.x da ESP32 o start() devolve um ponteiro: escreva \"BLEScanResults *resultados = pBLEScan->start(...);\" e use resultados->getCount()."), d.Linha);
    }

    Objeto NovoAnuncio(RadioBle radio)
    {
        var visto = new AnuncioVisto
        {
            Radio = radio,
            Nome = radio.Nome ?? "",
            Endereco = radio.Endereco,
            Rssi = radio.Rssi,
            FabricanteHex = radio.FabricanteHex,
            ServicoUuid = radio.ServicoUuid,
            ServicoHex = radio.ServicoHex,
        };
        visto.Uuids.AddRange(radio.UuidsAnunciados);
        return new Objeto("BLEAdvertisedDevice") { Rede = visto };
    }

    // =====================================================================
    //  a varredura
    // =====================================================================

    /// <summary>
    /// Olha o ar uma vez: cada placa anunciando que ainda nao foi vista (ou
    /// toda vez, com wantDuplicates) vai para os resultados e para o
    /// onResult() do sketch.
    /// </summary>
    void VarrerBle(EstadoScanBle scan)
    {
        foreach (var radio in Maq.Rede.AnunciosBleVistosDe(Maq))
        {
            if (scan.Parou) return;
            bool novo = scan.Vistos.Add(radio.Endereco);
            if (!novo && !scan.Duplicados) continue;
            var anuncio = NovoAnuncio(radio);
            if (novo) scan.Resultados.Add(anuncio);
            if (scan.Callbacks is { } cb) ChamarCallbackDoSketch(cb, "onResult", Valor.DeReferencia(anuncio));
        }
    }

    /// <summary>
    /// O start(segundos): bloqueia ate a varredura acabar -- ou ate o
    /// stop(), que o exemplo do core chama de dentro do onResult(). Com 0
    /// segundos ela nao acaba sozinha, como na placa.
    /// </summary>
    Valor ComecarVarredura(EstadoScanBle scan, double segundos, bool continuar, Rotina aoTerminar)
    {
        if (!continuar)
        {
            scan.Resultados.Clear();
            scan.Vistos.Clear();
        }
        scan.Ativa = true;
        scan.Parou = false;
        scan.FimUs = segundos > 0 ? Maq.AgoraUs() + segundos * 1e6 : double.MaxValue;
        scan.AoTerminar = aoTerminar;
        if (aoTerminar is not null) return Valor.DeBool(true);   // a versao com callback volta na hora

        while (true)
        {
            // O primeiro anuncio chega no primeiro intervalo dele (~100 ms).
            Maq.Avancar(100);
            EntregarBle();
            VarrerBle(scan);
            if (scan.Parou || Maq.AgoraUs() >= scan.FimUs) break;
            if (Maq.Parar) throw Maq.MotivoDaParada();
        }
        scan.Ativa = false;
        return Valor.DeReferencia(resultadosBle);
    }

    /// <summary>A varredura que roda por tras (o start com callback): olhada a cada volta e a cada delay.</summary>
    void AndarComAVarredura()
    {
        if (scanBle?.Rede is not EstadoScanBle { Ativa: true, AoTerminar: not null } scan) return;
        VarrerBle(scan);
        if (!scan.Parou && Maq.AgoraUs() < scan.FimUs) return;
        scan.Ativa = false;
        var fim = scan.AoTerminar;
        scan.AoTerminar = null;
        ChamarRotina(fim, Valor.DeReferencia(resultadosBle));
    }

    // =====================================================================
    //  os metodos
    // =====================================================================

    Valor ChamarClienteBle(Objeto obj, string metodo, Chamada c, Ambiente amb)
    {
        switch (obj.Classe)
        {
            case "BLEScan": return ChamarScanBle((EstadoScanBle)obj.Rede, metodo, c, amb);
            case "BLEScanResults": return ChamarResultadosBle((EstadoScanBle)obj.Rede, metodo, c, amb);
            case "BLEAdvertisedDevice": return ChamarAnuncioBle(obj.Rede as AnuncioVisto ?? new AnuncioVisto(), metodo, c, amb);
            case "BLEClient": return ChamarObjetoClienteBle(obj, metodo, c, amb);
            case "BLERemoteService": return ChamarServicoRemotoBle(obj, metodo, c, amb);
            case "BLERemoteCharacteristic": return ChamarCaracteristicaRemotaBle(obj, metodo, c, amb);
        }
        throw new ErroDeExecucao(Idioma.T("{0}::{1}() ainda nao e suportado pelo simulador.", obj.Classe, metodo), c.Linha);
    }

    Valor ChamarScanBle(EstadoScanBle scan, string metodo, Chamada c, Ambiente amb)
    {
        // setAdvertisedDeviceCallbacks recebe um objeto do sketch.
        if (metodo == "setAdvertisedDeviceCallbacks")
        {
            scan.Callbacks = c.Args.Count > 0 ? Avaliar(c.Args[0], amb).Referencia as Objeto : null;
            scan.Duplicados = c.Args.Count > 1 && Avaliar(c.Args[1], amb).Verdadeiro;
            return Valor.Vazio;
        }

        if (metodo == "start")
        {
            double segundos = c.Args.Count > 0 ? Avaliar(c.Args[0], amb).Num : 0;
            // start(duracao, callback, continuar) ou start(duracao, continuar).
            Rotina aoTerminar = null;
            bool continuar = false;
            if (c.Args.Count > 1)
            {
                var segundo = SemEndereco(c.Args[1]);
                if (segundo is FuncaoAnonima || segundo is Variavel { Nome: var n } && funcoes.ContainsKey(n))
                {
                    aoTerminar = RotinaDe(c.Args[1], amb, "BLEScan::start()");
                    continuar = c.Args.Count > 2 && Avaliar(c.Args[2], amb).Verdadeiro;
                }
                else continuar = Avaliar(c.Args[1], amb).Verdadeiro;
            }
            return ComecarVarredura(scan, segundos, continuar, aoTerminar);
        }

        var args = c.Args.Select(a => Avaliar(a, amb)).ToList();
        switch (metodo)
        {
            case "stop":
                scan.Parou = true;
                scan.Ativa = false;
                return Valor.DeBool(true);
            case "isScanning": return Valor.DeBool(scan.Ativa);
            case "getResults": return Valor.DeReferencia(resultadosBle);
            case "clearResults":
                scan.Resultados.Clear();
                scan.Vistos.Clear();
                return Valor.Vazio;
            case "erase":
            {
                string endereco = args.Count > 0 ? EnderecoDoValor(args[0]) : "";
                scan.Resultados.RemoveAll(o => o.Rede is AnuncioVisto v && string.Equals(v.Endereco, endereco, StringComparison.OrdinalIgnoreCase));
                scan.Vistos.Remove(endereco);
                return Valor.Vazio;
            }
            case "setActiveScan": case "setInterval": case "setWindow": case "setDuplicateFilter":
            case "clearDuplicateCache": case "dump":
                return Valor.Vazio;
        }
        throw new ErroDeExecucao(Idioma.T("BLEScan::{0}() ainda nao e suportado pelo simulador.", metodo), c.Linha);
    }

    Valor ChamarResultadosBle(EstadoScanBle scan, string metodo, Chamada c, Ambiente amb)
    {
        var args = c.Args.Select(a => Avaliar(a, amb)).ToList();
        switch (metodo)
        {
            case "getCount": return Valor.DeInt(scan.Resultados.Count);
            case "getDevice":
            {
                int i = args.Count > 0 ? (int)args[0].Num : 0;
                // Fora da lista o core devolve um anuncio vazio.
                return Valor.DeReferencia(i >= 0 && i < scan.Resultados.Count
                    ? scan.Resultados[i]
                    : new Objeto("BLEAdvertisedDevice") { Rede = new AnuncioVisto { Nome = "", Endereco = "00:00:00:00:00:00" } });
            }
            case "dump":
                foreach (var o in scan.Resultados)
                    if (o.Rede is AnuncioVisto v) Escrever(TextoDoAnuncio(v), true);
                return Valor.Vazio;
        }
        throw new ErroDeExecucao(Idioma.T("BLEScanResults::{0}() ainda nao e suportado pelo simulador.", metodo), c.Linha);
    }

    /// <summary>O toString() do BLEAdvertisedDevice, na ordem do BLEAdvertisedDevice.cpp.</summary>
    /// <summary>Os bytes (em hexadecimal) como o texto do sketch interpretado: um caractere por byte.</summary>
    static string Latin1DoHex(string hex)
        => string.Concat(Convert.FromHexString(hex ?? "").Select(b => (char)b));

    string TextoDoAnuncio(AnuncioVisto v)
    {
        var texto = new System.Text.StringBuilder($"Name: {v.Nome}, Address: {v.Endereco}");
        foreach (string u in v.Uuids) texto.Append(", serviceUUID: ").Append(u);
        if (v.Radio is { NimBle: false, ArduinoBle: false }) texto.Append(", txPower: 3");
        texto.Append(", rssi: ").Append(v.Rssi);
        return texto.ToString();
    }

    Valor ChamarAnuncioBle(AnuncioVisto v, string metodo, Chamada c, Ambiente amb)
    {
        var args = c.Args.Select(a => Avaliar(a, amb)).ToList();
        switch (metodo)
        {
            case "getName": return Valor.DeTexto(v.Nome ?? "");
            case "haveName": return Valor.DeBool(!string.IsNullOrEmpty(v.Nome));
            case "getAddress": return Valor.DeReferencia(new Objeto("BLEAddress") { Texto = v.Endereco });
            case "getRSSI": return Valor.DeInt(v.Rssi);
            case "haveRSSI": return Valor.DeBool(true);
            case "haveServiceUUID": return Valor.DeBool(v.Uuids.Count > 0);
            case "getServiceUUIDCount": return Valor.DeInt(v.Uuids.Count);
            case "getServiceUUID":
            {
                int i = args.Count > 0 ? (int)args[0].Num : 0;
                return Valor.DeReferencia(new Objeto("BLEUUID") { Texto = i >= 0 && i < v.Uuids.Count ? v.Uuids[i] : "" });
            }
            case "isAdvertisingService":
                return Valor.DeBool(args.Count > 0 && v.Uuids.Contains(UuidDoValor(args[0]), StringComparer.OrdinalIgnoreCase));
            case "haveTXPower": return Valor.DeBool(v.Radio is { NimBle: false, ArduinoBle: false });
            case "getTXPower": return Valor.DeInt(3);
            case "isConnectable": case "isScannable": case "isLegacyAdvertisement": return Valor.DeBool(true);
            case "haveManufacturerData": return Valor.DeBool(v.FabricanteHex.Length > 0);
            case "haveServiceData": return Valor.DeBool(v.ServicoHex.Length > 0);
            case "haveAppearance": return Valor.DeBool(false);
            case "getManufacturerData": return Valor.DeTexto(Latin1DoHex(v.FabricanteHex));
            case "getServiceData": return Valor.DeTexto(Latin1DoHex(v.ServicoHex));
            case "getServiceDataUUID": return Valor.DeReferencia(new Objeto("BLEUUID") { Texto = v.ServicoUuid });
            // Para o C++ compilado: os bytes em hexadecimal.
            case "__fabricante_hex": return Valor.DeTexto(v.FabricanteHex);
            case "__servico_hex": return Valor.DeTexto(v.ServicoHex);
            case "__servico_uuid": return Valor.DeTexto(v.ServicoUuid);
            case "getAppearance": return Valor.DeInt(0);
            case "getAddressType": return Valor.DeInt(0);
            case "toString": return Valor.DeTexto(TextoDoAnuncio(v));
        }
        throw new ErroDeExecucao(Idioma.T("BLEAdvertisedDevice::{0}() ainda nao e suportado pelo simulador.", metodo), c.Linha);
    }

    static string EnderecoDoValor(Valor v) => v.Referencia is Objeto { Classe: "BLEAddress" } a ? a.Texto ?? "" : v.Mostrar();

    // ---------------- o cliente ----------------

    Valor ChamarObjetoClienteBle(Objeto obj, string metodo, Chamada c, Ambiente amb)
    {
        var estado = (EstadoClienteBle)obj.Rede;
        if (metodo == "setClientCallbacks")
        {
            estado.Callbacks = c.Args.Count > 0 ? Avaliar(c.Args[0], amb).Referencia as Objeto : null;
            return Valor.Vazio;
        }

        var args = c.Args.Select(a => Avaliar(a, amb)).ToList();
        var conexao = estado.Conexao is { Ativa: true } ativa ? ativa : null;
        switch (metodo)
        {
            case "connect":
            case "connectTimeout":
            {
                if (radioBle is null) return Valor.DeBool(false);
                if (conexao is not null) return Valor.DeBool(true);
                RadioBle alvo = args.Count == 0 ? null
                    : args[0].Referencia is Objeto { Rede: AnuncioVisto visto } ? Maq.Rede.RadioBleDe(visto.Endereco)
                    : Maq.Rede.RadioBleDe(EnderecoDoValor(args[0]));

                // A conexao leva o seu tempo no ar (~50 ms): na placa o connect() espera.
                Maq.Avancar(50);
                var nova = Maq.Rede.ConectarBlePlaca(radioBle, alvo);
                if (nova is null) return Valor.DeBool(false);
                estado.Conexao = nova;
                if (estado.Callbacks is { } cb) ChamarCallbackDoSketch(cb, "onConnect", Valor.DeReferencia(obj));
                return Valor.DeBool(true);
            }

            case "isConnected": return Valor.DeBool(conexao is not null);

            case "disconnect":
                if (conexao is not null)
                {
                    Maq.Rede.DesconectarBlePlaca(conexao);
                    estado.Conexao = null;
                    if (estado.Callbacks is { } cb) ChamarCallbackDoSketch(cb, "onDisconnect", Valor.DeReferencia(obj));
                }
                return Valor.DeInt(0);

            case "getService":
            {
                if (conexao is null || args.Count == 0) return Valor.DeInt(0);
                string uuid = UuidDoValor(args[0]);
                var servico = conexao.Servidor.ServicosIniciados().FirstOrDefault(s => s.Uuid == uuid);
                if (servico is null) return Valor.DeInt(0);   // nullptr: o servidor nao tem esse servico
                Maq.Avancar(20);   // a descoberta de servicos
                return Valor.DeReferencia(new Objeto("BLERemoteService")
                    { Rede = new EstadoRemotoBle { Cliente = obj, Conexao = conexao, Servico = servico } });
            }

            case "getValue":
            {
                if (conexao is null || args.Count < 2) return Valor.DeTexto("");
                var car = AcharCaracteristicaRemota(conexao, UuidDoValor(args[0]), UuidDoValor(args[1]));
                return Valor.DeTexto(TextoArduino.DeBytes(Maq.Rede.LerDaPlaca(conexao, car) ?? []));
            }

            case "setValue":
            {
                if (conexao is null || args.Count < 3) return Valor.Vazio;
                var car = AcharCaracteristicaRemota(conexao, UuidDoValor(args[0]), UuidDoValor(args[1]));
                Maq.Rede.EscreverDaPlaca(conexao, car, TextoArduino.ParaBytes(args[2].Mostrar()), comResposta: true);
                return Valor.Vazio;
            }

            case "__servicos":
                return Valor.DeTexto(conexao is null ? "" : string.Join(",", conexao.Servidor.ServicosIniciados().Select(s => s.Uuid)));
            case "getRssi": return Valor.DeInt(conexao?.Servidor.Rssi ?? 0);
            case "getPeerAddress": return Valor.DeReferencia(new Objeto("BLEAddress") { Texto = conexao?.Servidor.Endereco ?? "00:00:00:00:00:00" });
            case "getMTU": return Valor.DeInt(conexao?.Mtu ?? 23);
            case "setMTU":
                if (conexao is not null && args.Count > 0) conexao.Mtu = Math.Clamp((int)args[0].Num, 23, 517);
                return Valor.DeBool(conexao is not null);
            case "getConnId": return Valor.DeInt(conexao is null ? 0xFFFF : 0);
            case "secureConnection": case "updateConnParams": return Valor.DeBool(conexao is not null);
            case "toString":
                return Valor.DeTexto($"peer address: {conexao?.Servidor.Endereco ?? "00:00:00:00:00:00"}");
        }
        throw new ErroDeExecucao(Idioma.T("BLEClient::{0}() ainda nao e suportado pelo simulador.", metodo), c.Linha);
    }

    static CaracteristicaBle AcharCaracteristicaRemota(ConexaoBle conexao, string servico, string caracteristica)
    {
        var s = conexao.Servidor.ServicosIniciados().FirstOrDefault(x => x.Uuid == servico);
        if (s is null) return null;
        lock (s.Caracteristicas) return s.Caracteristicas.FirstOrDefault(x => x.Uuid == caracteristica);
    }

    Valor ChamarServicoRemotoBle(Objeto obj, string metodo, Chamada c, Ambiente amb)
    {
        var estado = (EstadoRemotoBle)obj.Rede;
        var args = c.Args.Select(a => Avaliar(a, amb)).ToList();
        switch (metodo)
        {
            case "getCharacteristic":
            {
                if (args.Count == 0 || estado.Servico is null) return Valor.DeInt(0);
                string uuid = UuidDoValor(args[0]);
                CaracteristicaBle car;
                lock (estado.Servico.Caracteristicas) car = estado.Servico.Caracteristicas.FirstOrDefault(x => x.Uuid == uuid);
                return car is null ? Valor.DeInt(0) : Valor.DeReferencia(RemotaDe(estado, car));
            }
            case "getUUID": return Valor.DeReferencia(new Objeto("BLEUUID") { Texto = estado.Servico?.Uuid ?? "" });
            case "getClient": return Valor.DeReferencia(estado.Cliente);
            case "getValue":
            {
                var car = args.Count > 0 ? AcharCaracteristicaRemota(estado.Conexao, estado.Servico?.Uuid, UuidDoValor(args[0])) : null;
                return Valor.DeTexto(TextoArduino.DeBytes(Maq.Rede.LerDaPlaca(estado.Conexao, car) ?? []));
            }
            case "setValue":
            {
                var car = args.Count > 1 ? AcharCaracteristicaRemota(estado.Conexao, estado.Servico?.Uuid, UuidDoValor(args[0])) : null;
                if (car is not null) Maq.Rede.EscreverDaPlaca(estado.Conexao, car, TextoArduino.ParaBytes(args[1].Mostrar()), comResposta: true);
                return Valor.Vazio;
            }
            case "__caracteristicas":
                if (estado.Servico is null) return Valor.DeTexto("");
                lock (estado.Servico.Caracteristicas) return Valor.DeTexto(string.Join(",", estado.Servico.Caracteristicas.Select(x => x.Uuid)));
            case "getHandle": return Valor.DeInt(40);
            case "toString": return Valor.DeTexto($"Service: uuid: {estado.Servico?.Uuid}");
        }
        throw new ErroDeExecucao(Idioma.T("BLERemoteService::{0}() ainda nao e suportado pelo simulador.", metodo), c.Linha);
    }

    /// <summary>O mesmo objeto para a mesma caracteristica da mesma conexao: o callback de notificacao fica nele.</summary>
    Objeto RemotaDe(EstadoRemotoBle servico, CaracteristicaBle car)
    {
        lock (remotasBle)
        {
            if (remotasBle.TryGetValue((servico.Conexao, car), out var existente)) return existente;
            var nova = new Objeto("BLERemoteCharacteristic")
            {
                Rede = new EstadoRemotoBle { Cliente = servico.Cliente, Conexao = servico.Conexao, Servico = servico.Servico, Caracteristica = car },
            };
            remotasBle[(servico.Conexao, car)] = nova;
            return nova;
        }
    }

    Valor ChamarCaracteristicaRemotaBle(Objeto obj, string metodo, Chamada c, Ambiente amb)
    {
        var estado = (EstadoRemotoBle)obj.Rede;
        var car = estado.Caracteristica;

        // registerForNotify(callback, notificacoes, ...): o callback e funcao ou lambda.
        if (metodo is "registerForNotify" or "subscribe")
        {
            bool notificacoes = true;
            Rotina rotina = null;
            if (metodo == "registerForNotify")
            {
                rotina = c.Args.Count > 0 && Avaliar(c.Args[0], amb) is { Referencia: null, Num: 0, EhTexto: false } ? null
                    : c.Args.Count > 0 ? RotinaDe(c.Args[0], amb, "registerForNotify()") : null;
                notificacoes = c.Args.Count < 2 || Avaliar(c.Args[1], amb).Verdadeiro;
            }
            else
            {
                notificacoes = c.Args.Count < 1 || Avaliar(c.Args[0], amb).Verdadeiro;
                if (c.Args.Count > 1) rotina = RotinaDe(c.Args[1], amb, "subscribe()");
            }
            estado.AoNotificar = rotina;
            bool ok = Maq.Rede.AssinarDaPlaca(estado.Conexao, car, ligar: rotina is not null || metodo == "subscribe",
                                              indicacao: !notificacoes);
            return metodo == "subscribe" ? Valor.DeBool(ok) : Valor.Vazio;
        }

        var args = c.Args.Select(a => Avaliar(a, amb)).ToList();
        switch (metodo)
        {
            case "unsubscribe":
                estado.AoNotificar = null;
                return Valor.DeBool(Maq.Rede.AssinarDaPlaca(estado.Conexao, car, false, false));

            case "canRead": return Valor.DeBool(car?.Le ?? false);
            case "canWrite": return Valor.DeBool(car?.Escreve ?? false);
            case "canWriteNoResponse": return Valor.DeBool(car?.EscreveSemResposta ?? false);
            case "canNotify": return Valor.DeBool(car?.Notifica ?? false);
            case "canIndicate": return Valor.DeBool(car?.Indica ?? false);
            case "canBroadcast": return Valor.DeBool(false);

            case "readValue":
                return Valor.DeTexto(TextoArduino.DeBytes(LerRemota(estado)));
            case "readUInt8":
            {
                var b = LerRemota(estado);
                return Valor.DeC(b.Length >= 1 ? b[0] : 0, TipoC.UChar);
            }
            case "readUInt16":
            {
                var b = LerRemota(estado);
                return Valor.DeC(b.Length >= 2 ? BitConverter.ToUInt16(b, 0) : 0, TipoC.UShort);
            }
            case "readUInt32":
            {
                var b = LerRemota(estado);
                return Valor.DeC(b.Length >= 4 ? BitConverter.ToUInt32(b, 0) : 0, TipoC.UInt);
            }
            case "readFloat":
            {
                var b = LerRemota(estado);
                return Valor.DeC(b.Length >= 4 ? BitConverter.ToSingle(b, 0) : 0, TipoC.Float);
            }
            case "readRawData":
                return Valor.DeReferencia(car is null ? [] : car.Valor.Select(x => Valor.DeC(x, TipoC.UChar)).ToArray());

            case "writeValue":
            {
                if (car is null) return Valor.DeBool(false);

                // As tres sobrecargas do core: (uint8_t*, tamanho, resposta),
                // (String, resposta) e (uint8_t, resposta) -- escolhidas como o
                // C++ escolhe. O exemplo do core escreve
                // writeValue(texto.c_str(), texto.length()): e a de String, e
                // o tamanho vira o "resposta" (true).
                byte[] dados;
                bool resposta;
                Expr primeiro = c.Args.Count > 0 ? c.Args[0] : null;
                bool ponteiro = primeiro is Conversao { Tipo: { } tipo } && tipo.TrimEnd().EndsWith('*')
                                || primeiro is not null && !ReferenceEquals(SemEndereco(primeiro), primeiro);
                if (ponteiro || args.Count > 0 && args[0].Referencia is Valor[])
                {
                    dados = BytesDoSetValue(c, amb);
                    resposta = args.Count > 2 && args[2].Verdadeiro;
                }
                else if (args.Count > 0 && args[0].EhTexto)
                {
                    dados = TextoArduino.ParaBytes(args[0].Texto);
                    resposta = args.Count > 1 && args[1].Verdadeiro;
                }
                else
                {
                    dados = [(byte)((long)(args.Count > 0 ? args[0].Num : 0) & 0xFF)];
                    resposta = args.Count > 1 && args[1].Verdadeiro;
                }
                bool ok = Maq.Rede.EscreverDaPlaca(estado.Conexao, car, dados, resposta);
                // Com resposta, a placa espera o servidor confirmar (~1 intervalo de conexao).
                if (ok && resposta) Maq.Avancar(30);
                return Valor.DeBool(ok);
            }

            case "getUUID": return Valor.DeReferencia(new Objeto("BLEUUID") { Texto = car?.Uuid ?? "" });
            case "getHandle": return Valor.DeInt(car is null ? 0 : 42 + car.Servico.Caracteristicas.IndexOf(car) * 2);
            case "getRemoteService":
                return Valor.DeReferencia(new Objeto("BLERemoteService")
                    { Rede = new EstadoRemotoBle { Cliente = estado.Cliente, Conexao = estado.Conexao, Servico = estado.Servico } });
            case "getDescriptor": return Valor.DeInt(0);
            case "toString":
                return Valor.DeTexto($"Characteristic: uuid: {car?.Uuid}, handle: {42 + (car?.Servico.Caracteristicas.IndexOf(car) ?? 0) * 2}");
            case "setAuth": return Valor.Vazio;
        }
        throw new ErroDeExecucao(Idioma.T("BLERemoteCharacteristic::{0}() ainda nao e suportado pelo simulador.", metodo), c.Linha);
    }

    byte[] LerRemota(EstadoRemotoBle estado)
    {
        // Uma leitura e uma ida e volta no ar: ~1 intervalo de conexao.
        Maq.Avancar(30);
        return Maq.Rede.LerDaPlaca(estado.Conexao, estado.Caracteristica) ?? [];
    }

    // =====================================================================
    //  os eventos do lado do cliente
    // =====================================================================

    /// <summary>Uma notificacao chegou: o callback do registerForNotify() recebe os bytes.</summary>
    void EntregarNotificacao(EventoBle ev)
    {
        if (ev.Conexao is null || ev.Caracteristica is null) return;
        Objeto remota;
        lock (remotasBle) remotasBle.TryGetValue((ev.Conexao, ev.Caracteristica), out remota);
        if (remota?.Rede is not EstadoRemotoBle { AoNotificar: { } rotina }) return;
        var dados = ev.Dados ?? [];
        ChamarRotina(rotina, Valor.DeReferencia(remota),
                     dados.Select(b => Valor.DeC(b, TipoC.UChar)).ToArray(),
                     Valor.DeC(dados.Length, TipoC.UInt),
                     Valor.DeBool(ev.Valor == 1));
    }

    /// <summary>O servidor saiu: o onDisconnect() de quem estava conectado nele.</summary>
    void EntregarQuedaDoCliente(EventoBle ev)
    {
        List<Objeto> clientes;
        lock (clientesBle) clientes = [.. clientesBle];
        foreach (var cliente in clientes)
        {
            if (cliente.Rede is not EstadoClienteBle estado || estado.Conexao != ev.Conexao) continue;
            estado.Conexao = null;
            if (estado.Callbacks is { } cb) ChamarCallbackDoSketch(cb, "onDisconnect", Valor.DeReferencia(cliente));
        }
    }

    /// <summary>
    /// Chama o metodo de um objeto de callbacks do sketch (o que deriva de
    /// BLEAdvertisedDeviceCallbacks, BLEClientCallbacks) com os argumentos
    /// dados -- a versao com o mesmo numero de parametros. Metodo que o
    /// sketch nao escreveu e o vazio da classe base.
    /// </summary>
    void ChamarCallbackDoSketch(Objeto cb, string metodo, params Valor[] args)
    {
        if (ChamarCallbackNativo(cb, metodo, args)) return;
        if (cb?.Escopo is null || !classes.TryGetValue(cb.Classe, out var info)) return;
        var lista = AcharLista(info, metodo);
        var f = lista?.FirstOrDefault(x => x.Params.Count == args.Length);
        if (f is null) return;
        ChamarMetodo(cb, f, [.. args.Select(a => (Expr)new Pronto(a, 0))], Global, 0);
    }
}
