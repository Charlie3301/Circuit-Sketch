using System.Text;

namespace Simulador;

/// <summary>
/// A biblioteca BLE do core ESP32 3.x (BLEDevice.h, BLEServer.h, BLE2902.h):
/// o servidor GATT, os servicos, as caracteristicas, o anuncio e os
/// callbacks que o sketch escreve derivando de BLEServerCallbacks e
/// BLECharacteristicCallbacks. Quem conecta e a janela do celular (ou o
/// teste), pela <see cref="Rede"/>.
///
/// O comportamento segue o codigo do core 3.3.11, com as duas pilhas: a
/// ESP32 usa o Bluedroid e as outras (S3, C3, C6, H2), o NimBLE -- os bits
/// das propriedades, os callbacks de dois argumentos e o que o notify()
/// exige mudam de uma para a outra.
/// </summary>
public partial class Interpretador
{
    static readonly HashSet<string> ClassesBle =
    [
        "BLEServer", "BLEService", "BLECharacteristic", "BLEAdvertising", "BLEAdvertisementData",
        "BLEDescriptor", "BLE2902", "BLE2901", "BLE2904", "BLEUUID", "BLEAddress", "BLESecurity",
    ];

    static bool EhClasseBle(string classe) => ClassesBle.Contains(classe) || EhClasseDoClienteBle(classe);

    static DadosDoAnuncioBle AnuncioDe(Objeto dados)
    {
        var estado = dados.Rede as EstadoBle ?? (EstadoBle)(dados.Rede = new EstadoBle());
        return estado.Anuncio ??= new DadosDoAnuncioBle();
    }

    sealed class EstadoBle
    {
        public ServicoBle Servico;
        public CaracteristicaBle Caracteristica;

        /// <summary>O objeto do sketch que deriva de BLEServerCallbacks ou BLECharacteristicCallbacks.</summary>
        public Objeto Callbacks;

        /// <summary>Descritor: a caracteristica em que ele foi posto, e o que ele guarda.</summary>
        public CaracteristicaBle Dono;
        public bool Notificacoes, Indicacoes;
        public string Descricao;

        /// <summary>BLEAdvertisementData: os bytes do anuncio que o C++ compilado mandou.</summary>
        public DadosDoAnuncioBle Anuncio;
    }

    RadioBle radioBle;
    Objeto servidorBle, anuncioBle;
    readonly Dictionary<CaracteristicaBle, Objeto> objetosBle = [];
    bool avisouDaPilhaBle;

    /// <summary>A placa tem BLE? Pelo chip: toda ESP32, menos a S2.</summary>
    void ConferirChipComBle(string oque, int linha)
    {
        string id = Maq.Perfil?.Id ?? "";
        if (id == "nano-rp2040-connect")
            throw new ErroDeExecucao($"{oque}: o BLEDevice.h e a biblioteca BLE da ESP32. Na Nano RP2040 Connect o "
                                     + "BLE e pela ArduinoBLE: #include <ArduinoBLE.h>.", linha);
        if (!id.StartsWith("esp32", StringComparison.Ordinal))
            throw new ErroDeExecucao($"{oque}: o BLEDevice.h e da ESP32 -- a {Maq.Perfil?.Nome ?? id} nao tem Bluetooth.", linha);
        if (ModuloNativo.Chip(id) == "esp32s2")
            throw new ErroDeExecucao($"{oque}: a ESP32-S2 nao tem Bluetooth nenhum (nem o classico, nem o BLE). "
                                     + "Na placa, o BLEDevice.h nem compila para ela.", linha);
    }

    /// <summary>Os bits das propriedades, os da pilha do chip (BLECharacteristic.h do core 3.3.11).</summary>
    bool NimBleNestaPlaca => ModuloNativo.Chip(Maq.Perfil?.Id ?? "") != "esp32";

    int BitDaPropriedade(string nome) => NimBleNestaPlaca
        ? nome switch
        {
            "PROPERTY_BROADCAST" => 0x0001, "PROPERTY_READ" => 0x0002, "PROPERTY_WRITE_NR" => 0x0004,
            "PROPERTY_WRITE" => 0x0008, "PROPERTY_NOTIFY" => 0x0010, "PROPERTY_INDICATE" => 0x0020,
            "PROPERTY_READ_ENC" => 0x0200, "PROPERTY_READ_AUTHEN" => 0x0400, "PROPERTY_READ_AUTHOR" => 0x0800,
            "PROPERTY_WRITE_ENC" => 0x1000, "PROPERTY_WRITE_AUTHEN" => 0x2000, "PROPERTY_WRITE_AUTHOR" => 0x4000,
            _ => -1,
        }
        : nome switch
        {
            "PROPERTY_READ" => 1 << 0, "PROPERTY_WRITE" => 1 << 1, "PROPERTY_NOTIFY" => 1 << 2,
            "PROPERTY_BROADCAST" => 1 << 3, "PROPERTY_INDICATE" => 1 << 4, "PROPERTY_WRITE_NR" => 1 << 5,
            "PROPERTY_READ_ENC" or "PROPERTY_READ_AUTHEN" or "PROPERTY_READ_AUTHOR"
                or "PROPERTY_WRITE_ENC" or "PROPERTY_WRITE_AUTHEN" or "PROPERTY_WRITE_AUTHOR" => 0,
            _ => -1,
        };

    /// <summary>"BLECharacteristic::PROPERTY_READ", "ESP_GATT_PERM_READ", "ESP_PWR_LVL_P9".</summary>
    bool ConstanteDoBle(string nome, out Valor valor)
    {
        valor = Valor.Vazio;
        if (nome.StartsWith("BLECharacteristic::", StringComparison.Ordinal))
        {
            int bit = BitDaPropriedade(nome["BLECharacteristic::".Length..]);
            if (bit < 0) return false;
            valor = Valor.DeInt(bit);
            return true;
        }
        if (nome.StartsWith("BLECharacteristicCallbacks::Status::", StringComparison.Ordinal)
            || nome.StartsWith("BLECharacteristicCallbacks::", StringComparison.Ordinal))
        {
            string s = nome[(nome.LastIndexOf("::", StringComparison.Ordinal) + 2)..];
            string[] ordem = ["SUCCESS_INDICATE", "SUCCESS_NOTIFY", "ERROR_INDICATE_DISABLED", "ERROR_NOTIFY_DISABLED",
                              "ERROR_GATT", "ERROR_NO_CLIENT", "ERROR_NO_SUBSCRIBER", "ERROR_INDICATE_TIMEOUT",
                              "ERROR_INDICATE_FAILURE"];
            int i = Array.IndexOf(ordem, s);
            if (i < 0) return false;
            valor = Valor.DeInt(i);
            return true;
        }

        int? n = nome switch
        {
            // esp_gatt_defs.h (Bluedroid); no NimBLE o core define os mesmos nomes.
            "ESP_GATT_PERM_READ" => NimBleNestaPlaca ? 0x01 : 1 << 0,
            "ESP_GATT_PERM_READ_ENCRYPTED" => NimBleNestaPlaca ? 0x02 : 1 << 1,
            "ESP_GATT_PERM_WRITE" => NimBleNestaPlaca ? 0x10 : 1 << 4,
            "ESP_GATT_PERM_WRITE_ENCRYPTED" => NimBleNestaPlaca ? 0x20 : 1 << 5,
            // esp_bt.h: esp_power_level_t
            "ESP_PWR_LVL_N12" => 0, "ESP_PWR_LVL_N9" => 1, "ESP_PWR_LVL_N6" => 2, "ESP_PWR_LVL_N3" => 3,
            "ESP_PWR_LVL_N0" => 4, "ESP_PWR_LVL_P3" => 5, "ESP_PWR_LVL_P6" => 6, "ESP_PWR_LVL_P9" => 7,
            "ESP_BLE_PWR_TYPE_DEFAULT" => 9, "ESP_BLE_PWR_TYPE_ADV" => 3, "ESP_BLE_PWR_TYPE_SCAN" => 4,
            _ => null,
        };
        if (n is null) return false;
        valor = Valor.DeInt(n.Value);
        return true;
    }

    Objeto NovoObjetoBle(string classe, object estado = null)
        => new(classe) { Rede = estado ?? new EstadoBle() };

    /// <summary>Um objeto da BLE declarado ou criado com new: "new BLE2902()", "BLEUUID uuid("...")".</summary>
    void MontarObjetoBle(Objeto obj, CmdDeclObj d, Ambiente amb)
    {
        if (EhClasseDoClienteBle(d.Classe))
        {
            MontarObjetoDoClienteBle(obj, d, amb);
            return;
        }
        obj.Rede = new EstadoBle();
        if (d.Classe == "BLEUUID" && d.Args.Count > 0) obj.Texto = UuidDoValor(Avaliar(d.Args[0], amb));
    }

    static string UuidDoValor(Valor v)
    {
        if (v.Referencia is Objeto { Classe: "BLEUUID" } o) return o.Texto;
        if (v.EhTexto) return Ble.Normalizar(v.Texto);
        return Ble.Normalizar(((long)v.Num).ToString("x4"));
    }

    // =====================================================================
    //  BLEDevice::  (tudo static)
    // =====================================================================

    Valor ChamarBleDevice(string nome, Chamada c, Ambiente amb)
    {
        string metodo = nome["BLEDevice::".Length..];
        ConferirChipComBle($"BLEDevice::{metodo}()", c.Linha);
        var args = c.Args.Select(a => Avaliar(a, amb)).ToList();

        if (metodo == "init")
        {
            // A pilha do Bluetooth pega a parte dela da heap. Sem lugar, o
            // esp_bluedroid_init falha e nada do BLE funciona depois.
            if (!LigarRadio("ble", c.Linha))
                throw new ErroDeExecucao("BLEDevice::init(): nao ha heap para a pilha do Bluetooth. Na placa o init falha "
                                         + "(ESP_ERR_NO_MEM) e o proximo passo do BLE trava a placa.", c.Linha);
            radioBle = Maq.Rede.IniciarBle(Maq, args.Count > 0 ? args[0].Mostrar() : "");
            servidorBle = null;
            anuncioBle = null;
            objetosBle.Clear();
            return Valor.DeBool(true);
        }
        if (metodo is "getInitialized") return Valor.DeBool(radioBle is not null);

        if (radioBle is null)
            throw new ErroDeExecucao($"BLEDevice::{metodo}() antes do BLEDevice::init(\"nome\"): na placa, a pilha do "
                                     + "Bluetooth ainda nem ligou.", c.Linha);

        switch (metodo)
        {
            case "createServer":
                radioBle.TemServidor = true;
                return Valor.DeReferencia(servidorBle ??= NovoObjetoBle("BLEServer"));

            case "getAdvertising":
                return Valor.DeReferencia(anuncioBle ??= NovoObjetoBle("BLEAdvertising"));

            case "startAdvertising":
                Maq.Rede.AnunciarBle(radioBle, true);
                return Valor.Vazio;

            case "stopAdvertising":
                Maq.Rede.AnunciarBle(radioBle, false);
                return Valor.Vazio;

            case "getAddress":
                return Valor.DeReferencia(new Objeto("BLEAddress") { Texto = radioBle.Endereco });

            case "deinit":
                Maq.Rede.EsquecerBle(Maq);
                DesligarRadio("ble");
                radioBle = null;
                servidorBle = anuncioBle = null;
                return Valor.Vazio;

            case "toString":
                return Valor.DeTexto($"BD Address: {radioBle.Endereco}");

            case "getMTU": return Valor.DeInt(23);
            case "setMTU": case "setPower": case "setEncryptionLevel": case "setSecurityCallbacks":
            case "setCustomGapHandler": case "setCustomGattsHandler": case "setCustomGattcHandler":
            case "setOwnAddrType": case "whiteListAdd": case "whiteListRemove":
                return Valor.DeInt(0);
            case "getPower": return Valor.DeInt(5);

            case "getScan":
                return Valor.DeReferencia(ScanBle());

            case "createClient":
                return Valor.DeReferencia(NovoClienteBle());
        }

        throw new ErroDeExecucao($"BLEDevice::{metodo}() ainda nao e suportado pelo simulador.", c.Linha);
    }

    // =====================================================================
    //  Os objetos: servidor, servico, caracteristica, anuncio, descritores
    // =====================================================================

    Valor ChamarBle(Objeto obj, string metodo, Chamada c, Ambiente amb)
    {
        EntregarBle();
        if (EhClasseDoClienteBle(obj.Classe)) return ChamarClienteBle(obj, metodo, c, amb);
        var estado = obj.Rede as EstadoBle ?? new EstadoBle();
        obj.Rede ??= estado;

        // setCallbacks recebe um objeto do sketch: nao pode virar valor antes.
        if (metodo == "setCallbacks")
        {
            estado.Callbacks = c.Args.Count > 0 ? Avaliar(c.Args[0], amb).Referencia as Objeto : null;
            return Valor.Vazio;
        }

        // setValue(dados, tamanho) olha o que veio escrito: "(uint8_t*)&valor".
        if (obj.Classe == "BLECharacteristic" && metodo == "setValue")
        {
            if (estado.Caracteristica is { } alvo) alvo.Valor = BytesDoSetValue(c, amb);
            return Valor.Vazio;
        }

        var args = c.Args.Select(a => Avaliar(a, amb)).ToList();
        string S(int i) => i < args.Count ? args[i].Mostrar() : "";

        switch (obj.Classe)
        {
            case "BLEServer":
                switch (metodo)
                {
                    case "createService":
                    {
                        if (radioBle is null) break;
                        var servico = new ServicoBle(radioBle, UuidDoValor(args.Count > 0 ? args[0] : Valor.DeTexto("")));
                        lock (radioBle.Servicos) radioBle.Servicos.Add(servico);
                        return Valor.DeReferencia(NovoObjetoBle("BLEService", new EstadoBle { Servico = servico }));
                    }
                    case "getAdvertising":
                        return Valor.DeReferencia(anuncioBle ??= NovoObjetoBle("BLEAdvertising"));
                    case "startAdvertising":
                        if (radioBle is not null) Maq.Rede.AnunciarBle(radioBle, true);
                        return Valor.Vazio;
                    case "stopAdvertising":
                        if (radioBle is not null) Maq.Rede.AnunciarBle(radioBle, false);
                        return Valor.Vazio;
                    case "advertiseOnDisconnect":
                        if (radioBle is not null) radioBle.ReanunciarAoDesconectar = args.Count == 0 || args[0].Verdadeiro;
                        return Valor.Vazio;
                    case "getConnectedCount":
                        return Valor.DeInt(radioBle?.Conectados ?? 0);
                    case "disconnect":
                    {
                        if (radioBle is { CelularConectado: true }) Maq.Rede.DesconectarBleDoCelular(pelaPlaca: true);
                        else if (radioBle is not null)
                        {
                            ConexaoBle primeira;
                            lock (radioBle.Clientes) primeira = radioBle.Clientes.FirstOrDefault();
                            Maq.Rede.DesconectarBlePlaca(primeira);
                        }
                        return Valor.Vazio;
                    }
                    case "getServiceByUUID":
                    {
                        string u = UuidDoValor(args.Count > 0 ? args[0] : Valor.DeTexto(""));
                        var s = radioBle?.Servicos.FirstOrDefault(x => x.Uuid == u);
                        return s is null ? Valor.DeInt(0) : Valor.DeReferencia(NovoObjetoBle("BLEService", new EstadoBle { Servico = s }));
                    }
                    case "getPeerMTU": return Valor.DeInt(23);
                    case "updatePeerMTU": case "removeService": case "updateConnParams":
                        return Valor.Vazio;
                }
                break;

            case "BLEService":
                switch (metodo)
                {
                    case "createCharacteristic":
                    {
                        var servico = estado.Servico;
                        if (servico is null) break;
                        string u = UuidDoValor(args.Count > 0 ? args[0] : Valor.DeTexto(""));
                        int p = args.Count > 1 ? (int)args[1].Num : 0;
                        bool nimble = NimBleNestaPlaca;
                        var car = new CaracteristicaBle(servico, u,
                            le: (p & (nimble ? 0x0002 : 1 << 0)) != 0,
                            escreve: (p & (nimble ? 0x0008 : 1 << 1)) != 0,
                            escreveSemResposta: (p & (nimble ? 0x0004 : 1 << 5)) != 0,
                            notifica: (p & (nimble ? 0x0010 : 1 << 2)) != 0,
                            indica: (p & (nimble ? 0x0020 : 1 << 4)) != 0,
                            propriedades: p);
                        lock (servico.Caracteristicas) servico.Caracteristicas.Add(car);
                        var co = NovoObjetoBle("BLECharacteristic", new EstadoBle { Caracteristica = car });
                        objetosBle[car] = co;
                        return Valor.DeReferencia(co);
                    }
                    case "start":
                        if (estado.Servico is { } s1) s1.Iniciado = true;
                        return Valor.Vazio;
                    case "stop":
                        if (estado.Servico is { } s2) s2.Iniciado = false;
                        return Valor.Vazio;
                    case "getCharacteristic":
                    {
                        string u = UuidDoValor(args.Count > 0 ? args[0] : Valor.DeTexto(""));
                        var car = estado.Servico?.Caracteristicas.FirstOrDefault(x => x.Uuid == u);
                        return car is not null && objetosBle.TryGetValue(car, out var co) ? Valor.DeReferencia(co) : Valor.DeInt(0);
                    }
                    case "getUUID":
                        return Valor.DeReferencia(new Objeto("BLEUUID") { Texto = estado.Servico?.Uuid ?? "" });
                    case "removeCharacteristic":
                        if (estado.Servico is { } s3 && args.Count > 0 && args[0].Referencia is Objeto { Rede: EstadoBle { Caracteristica: { } tirar } })
                            lock (s3.Caracteristicas) s3.Caracteristicas.Remove(tirar);
                        return Valor.Vazio;
                    case "toString":
                        return Valor.DeTexto($"UUID: {estado.Servico?.Uuid}");
                }
                break;

            case "BLECharacteristic":
            {
                var car = estado.Caracteristica;
                switch (metodo)
                {
                    case "getValue":
                        return Valor.DeTexto(TextoArduino.DeBytes(car?.Valor ?? []));
                    case "getLength":
                        return Valor.DeInt(car?.Valor.Length ?? 0);
                    case "getData":
                        return Valor.DeReferencia((car?.Valor ?? []).Select(b => Valor.DeInt(b)).ToArray());
                    case "notify":
                    case "indicate":
                    {
                        if (car is null) return Valor.Vazio;
                        var status = Maq.Rede.NotificarDaPlaca(car, metodo == "indicate");
                        if (estado.Callbacks is { } cb)
                        {
                            int codigo = status == StatusDoNotifyBle.Mandou
                                ? (metodo == "indicate" ? 0 : 1) : (int)status;
                            if (status == StatusDoNotifyBle.Mandou && metodo == "notify")
                                ChamarCallbackBle(cb, "onNotify", obj);
                            ChamarCallbackBle(cb, "onStatus", obj, Valor.DeInt(codigo), Valor.DeInt(0));
                        }
                        return Valor.Vazio;
                    }
                    case "addDescriptor":
                    {
                        if (car is null || args.Count == 0 || args[0].Referencia is not Objeto desc) return Valor.Vazio;
                        if (desc.Rede is not EstadoBle ed) desc.Rede = ed = new EstadoBle();
                        ed.Dono = car;
                        if (desc.Classe == "BLE2902") car.Tem2902 = true;
                        if (desc.Classe == "BLE2901" && ed.Descricao is not null) car.Descricao = ed.Descricao;
                        return Valor.Vazio;
                    }
                    case "getUUID":
                        return Valor.DeReferencia(new Objeto("BLEUUID") { Texto = car?.Uuid ?? "" });
                    case "getProperties":
                        return Valor.DeInt(car?.Propriedades ?? 0);
                    case "getDescriptorByUUID":
                        return Valor.DeInt(0);
                    case "getHandle":
                        return Valor.DeInt(car is null ? 0 : 42 + (car.Servico.Caracteristicas.IndexOf(car) * 2));
                    case "toString":
                        return Valor.DeTexto($"UUID: {car?.Uuid}, props: {car?.Propriedades}");
                    case "setReadProperty": case "setWriteProperty": case "setNotifyProperty":
                    case "setIndicateProperty": case "setBroadcastProperty": case "setWriteNoResponseProperty":
                    case "setAccessPermissions": case "setDescription":
                        return Valor.Vazio;
                }
                break;
            }

            case "BLEAdvertising":
                switch (metodo)
                {
                    case "addServiceUUID":
                        if (radioBle is not null && args.Count > 0) radioBle.UuidsAnunciados.Add(UuidDoValor(args[0]));
                        return Valor.Vazio;
                    case "removeServiceUUID":
                        if (radioBle is not null && args.Count > 0) radioBle.UuidsAnunciados.Remove(UuidDoValor(args[0]));
                        return Valor.Vazio;
                    case "start":
                        if (radioBle is not null) Maq.Rede.AnunciarBle(radioBle, true);
                        return Valor.DeBool(true);
                    case "stop":
                        if (radioBle is not null) Maq.Rede.AnunciarBle(radioBle, false);
                        return Valor.DeBool(true);
                    case "isAdvertising":
                        return Valor.DeBool(radioBle?.Anunciando ?? false);
                    case "setName":
                        if (radioBle is not null) radioBle.Nome = S(0);
                        return Valor.Vazio;
                    case "setAdvertisementData":
                        if (radioBle is not null && args.Count > 0 && args[0].Referencia is Objeto dados)
                        {
                            if (dados.Texto is { Length: > 0 } nome) radioBle.Nome = nome;
                            // Os bytes do anuncio (o C++ compilado manda: o iBeacon, o Eddystone).
                            if (dados.Rede is EstadoBle { Anuncio: { } bytes })
                            {
                                radioBle.FabricanteHex = bytes.FabricanteHex;
                                radioBle.ServicoUuid = bytes.ServicoUuid;
                                radioBle.ServicoHex = bytes.ServicoHex;
                            }
                        }
                        return Valor.Vazio;
                    case "setScanResponse": case "setMinPreferred": case "setMaxPreferred": case "setMinInterval":
                    case "setMaxInterval": case "setAppearance": case "setScanResponseData": case "reset":
                    case "setAdvertisementType": case "setScanFilter": case "setDeviceAddress": case "setPreferredParams":
                        return Valor.Vazio;
                }
                break;

            case "BLEAdvertisementData":
                switch (metodo)
                {
                    case "setName": case "setShortName":
                        obj.Texto = S(0);
                        return Valor.Vazio;
                    // As estruturas do anuncio (tamanho, tipo, dados), em hexadecimal.
                    case "__carga_hex":
                    {
                        var carga = new StringBuilder();
                        void Estrutura(int tipo, string hex) => carga.Append($"{hex.Length / 2 + 1:x2}{tipo:x2}{hex}");
                        if (obj.Texto is { Length: > 0 } nome) Estrutura(0x09, Convert.ToHexString(Encoding.UTF8.GetBytes(nome)).ToLowerInvariant());
                        if (obj.Rede is EstadoBle { Anuncio: { } a })
                        {
                            if (a.ServicoHex is { Length: > 0 } && a.ServicoUuid is { Length: 4 } u)
                                Estrutura(0x16, u[2..4] + u[..2] + a.ServicoHex);
                            if (a.FabricanteHex is { Length: > 0 }) Estrutura(0xFF, a.FabricanteHex);
                        }
                        return Valor.DeTexto(carga.ToString());
                    }
                    // Do C++ compilado: os bytes em hexadecimal (o texto do sketch nao e binario).
                    case "__fabricante_hex":
                        AnuncioDe(obj).FabricanteHex = S(0);
                        return Valor.Vazio;
                    case "__servico_hex":
                    {
                        var bytes = AnuncioDe(obj);
                        bytes.ServicoUuid = S(0);
                        bytes.ServicoHex = S(1);
                        return Valor.Vazio;
                    }
                    case "setFlags": case "setCompleteServices": case "setPartialServices": case "setManufacturerData":
                    case "setServiceData": case "setAppearance": case "addData": case "setPreferredParams":
                        return Valor.Vazio;
                }
                break;

            case "BLE2902":
                switch (metodo)
                {
                    case "getNotifications": return Valor.DeBool(estado.Dono?.Assinada ?? estado.Notificacoes);
                    case "getIndications": return Valor.DeBool(estado.Dono is { Indica: true, Assinada: true } || estado.Indicacoes);
                    case "setNotifications":
                        estado.Notificacoes = args.Count > 0 && args[0].Verdadeiro;
                        if (estado.Dono is { } d1) d1.Assinada = estado.Notificacoes;
                        return Valor.Vazio;
                    case "setIndications":
                        estado.Indicacoes = args.Count > 0 && args[0].Verdadeiro;
                        return Valor.Vazio;
                    case "setCallbacks": case "setAccessPermissions": case "setValue":
                        return Valor.Vazio;
                }
                break;

            case "BLE2901":
                switch (metodo)
                {
                    case "setDescription":
                        estado.Descricao = S(0);
                        if (estado.Dono is { } d2) d2.Descricao = estado.Descricao;
                        return Valor.Vazio;
                    case "setAccessPermissions": case "setValue":
                        return Valor.Vazio;
                }
                break;

            case "BLE2904": case "BLEDescriptor": case "BLESecurity":
                return Valor.Vazio;

            case "BLEUUID":
                switch (metodo)
                {
                    case "toString": return Valor.DeTexto(obj.Texto ?? "");
                    case "equals": return Valor.DeBool(args.Count > 0 && UuidDoValor(args[0]) == obj.Texto);
                    case "bitSize": return Valor.DeInt(Ble.Curto(obj.Texto) >= 0 ? 16 : 128);
                }
                break;

            case "BLEAddress":
                switch (metodo)
                {
                    case "toString": return Valor.DeTexto(obj.Texto ?? "");
                    case "equals": return Valor.DeBool(args.Count > 0 && string.Equals(S(0), obj.Texto, StringComparison.OrdinalIgnoreCase));
                }
                break;
        }

        throw new ErroDeExecucao($"{obj.Classe}::{metodo}() ainda nao e suportado pelo simulador.", c.Linha);
    }

    /// <summary>
    /// Os bytes do setValue: texto, numero (int de 4 bytes, float de 4) ou
    /// "(uint8_t*)&amp;valor, 4" -- os bytes da variavel, na ordem do chip
    /// (little-endian), no tamanho pedido.
    /// </summary>
    byte[] BytesDoSetValue(Chamada c, Ambiente amb)
    {
        if (c.Args.Count == 0) return [];
        Expr alvo = c.Args[0];
        while (alvo is Conversao cv) alvo = cv.Alvo;
        alvo = SemEndereco(alvo);
        var v = Avaliar(alvo, amb);
        int? tamanho = c.Args.Count > 1 ? (int)Avaliar(c.Args[1], amb).Num : null;

        byte[] dados;
        if (v.Referencia is Valor[] vetor)
            dados = [.. vetor.Take(tamanho ?? vetor.Length).Select(x => (byte)((long)x.Num & 0xFF))];
        else if (v.EhTexto)
        {
            dados = TextoArduino.ParaBytes(v.Texto);
            if (tamanho is { } t && t < dados.Length) dados = dados[..t];
        }
        else if (!v.Inteiro)
            dados = tamanho == 8 ? BitConverter.GetBytes(v.Num) : BitConverter.GetBytes((float)v.Num);
        else
        {
            var oito = BitConverter.GetBytes((long)v.Num);
            dados = oito[..Math.Clamp(tamanho ?? 4, 1, 8)];
        }
        return dados;
    }

    // =====================================================================
    //  Os callbacks: entregues na thread do sketch
    // =====================================================================

    /// <summary>
    /// O que o celular fez desde a ultima vez: conectou, escreveu, leu,
    /// assinou. Roda a cada volta do loop() e em cada delay() -- na placa, os
    /// callbacks rodam na tarefa do Bluetooth, sem esperar o sketch.
    /// </summary>
    void EntregarBle()
    {
        // A ArduinoBLE usa o mesmo radio, com os eventos dela.
        if (UsaArduinoBle)
        {
            EntregarArduinoBle();
            return;
        }
        var radio = radioBle;
        if (radio is null) return;

        AndarComAVarredura();
        for (int volta = 0; volta < 50 && radio.ProximoEvento(out var ev); volta++)
        {
            conexaoDoEvento = ev.Conexao;
            switch (ev.Tipo)
            {
                case TipoDeEventoBle.Notificou:
                    EntregarNotificacao(ev);
                    break;

                case TipoDeEventoBle.Caiu:
                    EntregarQuedaDoCliente(ev);
                    break;

                case TipoDeEventoBle.Conectou:
                case TipoDeEventoBle.Desconectou:
                {
                    if (servidorBle?.Rede is not EstadoBle { Callbacks: { } cb }) break;
                    string nome = ev.Tipo == TipoDeEventoBle.Conectou ? "onConnect" : "onDisconnect";
                    ChamarCallbackBle(cb, nome, servidorBle);
                    break;
                }

                case TipoDeEventoBle.Escreveu:
                case TipoDeEventoBle.Leu:
                {
                    if (ev.Caracteristica is not { } car || !objetosBle.TryGetValue(car, out var co)) break;
                    if (co.Rede is not EstadoBle { Callbacks: { } cb }) break;
                    ChamarCallbackBle(cb, ev.Tipo == TipoDeEventoBle.Escreveu ? "onWrite" : "onRead", co);
                    break;
                }

                case TipoDeEventoBle.Assinou:
                {
                    // So o NimBLE tem o onSubscribe (o valor e o do CCCD: 1 notificar, 2 indicar).
                    if (!radio.NimBle || ev.Caracteristica is not { } car || !objetosBle.TryGetValue(car, out var co)) break;
                    if (co.Rede is not EstadoBle { Callbacks: { } cb }) break;
                    ChamarCallbackBle(cb, "onSubscribe", co, Valor.Vazio, Valor.DeInt(ev.Valor));
                    break;
                }
            }
        }
    }

    /// <summary>
    /// Chama o metodo do objeto de callbacks do sketch, como a biblioteca faz:
    /// a versao de um argumento e, depois, a de dois (a do param do Bluedroid
    /// ou a do ble_gap_conn_desc do NimBLE, conforme o chip). Metodo que o
    /// sketch nao escreveu e o vazio da classe base.
    /// </summary>
    void ChamarCallbackBle(Objeto cb, string metodo, Objeto primeiro, params Valor[] resto)
    {
        if (ChamarCallbackNativo(cb, metodo, [Valor.DeReferencia(primeiro), .. resto])) return;
        if (cb?.Escopo is null || !classes.TryGetValue(cb.Classe, out var info)) return;
        var lista = AcharLista(info, metodo);
        if (lista is null) return;

        bool nimble = radioBle?.NimBle ?? NimBleNestaPlaca;
        string tipoDoParam = nimble ? "ble_gap_conn_desc" : "esp_ble_gatts_cb_param_t";

        foreach (var f in lista.OrderBy(f => f.Params.Count))
        {
            var args = new List<Expr> { new Pronto(Valor.DeReferencia(primeiro), 0) };
            if (metodo is "onStatus")
            {
                if (f.Params.Count != 3) continue;
                args.AddRange(resto.Select(r => (Expr)new Pronto(r, 0)));
            }
            else if (metodo is "onSubscribe")
            {
                if (f.Params.Count != 3) continue;
                args.Add(new Pronto(Valor.DeReferencia(ParametroDoBle(metodo, primeiro, nimble)), 0));
                args.Add(new Pronto(resto.Length > 1 ? resto[1] : Valor.DeInt(0), 0));
            }
            else if (f.Params.Count == 2)
            {
                // O segundo parametro e de uma pilha so: o do Bluedroid numa S3
                // nem compilaria na placa, e o callback nunca seria chamado.
                string tipo = f.Params[1].Tipo ?? "";
                if (!tipo.Contains(tipoDoParam, StringComparison.Ordinal))
                {
                    if (!avisouDaPilhaBle)
                    {
                        avisouDaPilhaBle = true;
                        Maq.Avisar($"BLE: {info.Nome}::{metodo}({string.Join(", ", f.Params.Select(x => x.Tipo))}) e da outra pilha. "
                                   + $"Na {Maq.Perfil?.Nome} o BLE e o {(nimble ? "NimBLE" : "Bluedroid")}, e o segundo "
                                   + $"parametro e {tipoDoParam}* -- na placa, esse metodo nao e chamado.");
                    }
                    continue;
                }
                args.Add(new Pronto(Valor.DeReferencia(ParametroDoBle(metodo, primeiro, nimble)), 0));
            }
            else if (f.Params.Count != 1) continue;

            ChamarMetodo(cb, f, args, Global, 0);
        }
    }

    /// <summary>O esp_ble_gatts_cb_param_t (Bluedroid) ou o ble_gap_conn_desc (NimBLE) que os exemplos leem.</summary>
    /// <summary>A placa do outro lado do evento que esta sendo entregue (nulo: o celular).</summary>
    ConexaoBle conexaoDoEvento;

    Objeto ParametroDoBle(string metodo, Objeto primeiro, bool nimble)
    {
        byte[] doOutroLado = conexaoDoEvento?.Cliente.Endereco is { } mac
            ? [.. mac.Split(':').Select(p => Convert.ToByte(p, 16))]
            : EnderecoDoCelular;
        Valor[] Endereco() => [.. doOutroLado.Select(b => Valor.DeInt(b))];

        if (nimble)
        {
            var desc = new Objeto("ble_gap_conn_desc");
            desc.Campos["conn_handle"] = Valor.DeInt(1);
            var ota = new Objeto("ble_addr_t");
            ota.Campos["val"] = Valor.DeReferencia(Endereco());
            ota.Campos["type"] = Valor.DeInt(1);
            desc.Campos["peer_ota_addr"] = Valor.DeReferencia(ota);
            desc.Campos["peer_id_addr"] = Valor.DeReferencia(ota);
            return desc;
        }

        var param = new Objeto("esp_ble_gatts_cb_param_t");
        var conexao = new Objeto("gatts_connect_evt_param");
        conexao.Campos["conn_id"] = Valor.DeInt(0);
        conexao.Campos["remote_bda"] = Valor.DeReferencia(Endereco());
        param.Campos["connect"] = Valor.DeReferencia(conexao);
        param.Campos["disconnect"] = Valor.DeReferencia(conexao);

        var escrita = new Objeto("gatts_write_evt_param");
        byte[] valor = (primeiro?.Rede as EstadoBle)?.Caracteristica?.Valor ?? [];
        escrita.Campos["conn_id"] = Valor.DeInt(0);
        escrita.Campos["len"] = Valor.DeInt(valor.Length);
        escrita.Campos["value"] = Valor.DeReferencia(valor.Select(b => Valor.DeInt(b)).ToArray());
        escrita.Campos["bda"] = Valor.DeReferencia(Endereco());
        param.Campos["write"] = Valor.DeReferencia(escrita);
        param.Campos["read"] = Valor.DeReferencia(escrita);
        return param;
    }

    /// <summary>O endereco do celular da janela, sempre o mesmo.</summary>
    static readonly byte[] EnderecoDoCelular = [0x5C, 0xE9, 0x1E, 0x3A, 0x7B, 0x21];
}
