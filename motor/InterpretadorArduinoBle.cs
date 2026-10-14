namespace Simulador;

/// <summary>
/// A ArduinoBLE (2.1.0): a biblioteca de BLE da Nano RP2040 Connect -- e que
/// tambem roda nas ESP32. O objeto global BLE, os BLEService, os
/// BLECharacteristic (os genericos e os com tipo: BLEByteCharacteristic,
/// BLEFloatCharacteristic, BLEStringCharacteristic...), e o BLEDevice, que e
/// o aparelho do outro lado: a central conectada na placa (o celular ou
/// outra placa) ou o periferico que a placa achou procurando e em que ela
/// conecta.
///
/// Tudo passa pela mesma <see cref="Rede"/> da biblioteca BLE da ESP32: um
/// periferico da ArduinoBLE aparece no celular e para uma ESP32 cliente, e
/// uma placa com a ArduinoBLE conecta numa ESP32 servidor.
/// </summary>
public partial class Interpretador
{
    bool? usaArduinoBle;

    /// <summary>O sketch inclui a ArduinoBLE: o BLEService, o BLECharacteristic e o BLEDevice sao os dela.</summary>
    bool UsaArduinoBle => usaArduinoBle ??= prog?.Fonte?.Contains("ArduinoBLE.h", StringComparison.Ordinal) == true;

    sealed class ServicoArduino : IValorBooleano
    {
        public string Uuid;
        public readonly List<Objeto> Caracteristicas = [];
        public ServicoBle Real;
        public ConexaoBle Conexao;   // o do outro lado (achado pelo periferico remoto)
        public bool Verdadeiro => Real is not null || Conexao is null;
    }

    sealed class CaracteristicaArduino : IValorBooleano
    {
        public string Uuid;
        public int Propriedades;
        public int Tamanho;
        public bool Fixo;
        public TipoC Tipo;          // Nenhum: a generica (bytes); String: a BLEStringCharacteristic
        public byte[] Valor = [];
        public CaracteristicaBle Real;
        public ConexaoBle Conexao;  // nao nulo: e a de outro aparelho, vista daqui (central)
        public bool Escrita, Atualizada;

        /// <summary>A BLEStringCharacteristic: o valor e texto.</summary>
        public bool DeTexto;
        public readonly Dictionary<int, Rotina> Eventos = [];
        public bool Remota => Conexao is not null;
        public bool Verdadeiro => !Remota || Real is not null;
    }

    sealed class DispositivoArduino : IValorBooleano
    {
        public RadioBle Remoto;         // o periferico que a varredura achou (ou a placa central conectada)
        public ConexaoBle Conexao;      // a conexao com ele
        public bool Celular;            // a central e o celular da janela
        public string Endereco = "00:00:00:00:00:00";
        public string Nome = "";
        public List<string> Uuids = [];
        public int Rssi;
        public bool Verdadeiro => Endereco != "00:00:00:00:00:00";
    }

    sealed class DescritorArduino
    {
        public string Uuid;
        public string Texto;
    }

    /// <summary>As caracteristicas com tipo: o tipo do C de cada uma (o tamanho vem do chip).</summary>
    static readonly Dictionary<string, string> CaracteristicasComTipo = new(StringComparer.Ordinal)
    {
        ["BLEBoolCharacteristic"] = "bool", ["BLEBooleanCharacteristic"] = "bool",
        ["BLECharCharacteristic"] = "char", ["BLEUnsignedCharCharacteristic"] = "unsigned char",
        ["BLEByteCharacteristic"] = "byte", ["BLEShortCharacteristic"] = "short",
        ["BLEUnsignedShortCharacteristic"] = "unsigned short", ["BLEWordCharacteristic"] = "word",
        ["BLEIntCharacteristic"] = "int", ["BLEUnsignedIntCharacteristic"] = "unsigned int",
        ["BLELongCharacteristic"] = "long", ["BLEUnsignedLongCharacteristic"] = "unsigned long",
        ["BLEFloatCharacteristic"] = "float", ["BLEDoubleCharacteristic"] = "double",
    };

    static bool EhClasseDaArduinoBle(string classe)
        => classe is "BLEService" or "BLECharacteristic" or "BLEStringCharacteristic" or "BLEDescriptor" or "BLEDevice"
           || CaracteristicasComTipo.ContainsKey(classe);

    /// <summary>A central conectada agora: o que o BLE.central() devolve.</summary>
    Objeto centralArduino;

    /// <summary>Quem esta conectado como central, por conexao (nulo: o celular).</summary>
    readonly Dictionary<object, Objeto> centraisArduino = [];

    readonly Dictionary<CaracteristicaBle, Objeto> locaisArduino = [];
    readonly Dictionary<(ConexaoBle, CaracteristicaBle), Objeto> remotasArduino = [];
    readonly List<Objeto> perifericosArduino = [];
    readonly Dictionary<int, Rotina> eventosDoBle = [];

    // a varredura
    bool varrendoArduino, duplicadosArduino;
    string filtroNome, filtroUuid, filtroEndereco;
    readonly HashSet<string> vistosArduino = new(StringComparer.OrdinalIgnoreCase);
    readonly Queue<Objeto> achadosArduino = new();

    /// <summary>"BLERead", "BLENotify", "BLEConnected", "BLEWritten": os da ArduinoBLE.</summary>
    bool ConstanteDaArduinoBle(string nome, out Valor valor)
    {
        valor = Valor.Vazio;
        if (!UsaArduinoBle) return false;
        int? n = nome switch
        {
            "BLEBroadcast" => 0x01, "BLERead" => 0x02, "BLEWriteWithoutResponse" => 0x04, "BLEWrite" => 0x08,
            "BLENotify" => 0x10, "BLEIndicate" => 0x20, "BLEAuthSignedWrite" => 1 << 6, "BLEExtProp" => 1 << 7,
            "BLEEncryption" => 1 << 9, "BLEAuthentication" => 1 << 10, "BLEAuthorization" => 1 << 11,
            "BLEConnected" => 0, "BLEDisconnected" => 1, "BLEDiscovered" => 2,
            "BLESubscribed" => 0, "BLEUnsubscribed" => 1, "BLEWritten" => 3, "BLEUpdated" => 3,
            _ => null,
        };
        if (n is null) return false;
        valor = Valor.DeInt(n.Value);
        return true;
    }

    void ConferirChipDaArduinoBle(string oque, int linha)
    {
        string id = Maq.Perfil?.Id ?? "";
        if (id == "nano-rp2040-connect") return;
        if (id.StartsWith("esp32", StringComparison.Ordinal))
        {
            if (ModuloNativo.Chip(id) == "esp32s2")
                throw new ErroDeExecucao($"{oque}: a ESP32-S2 nao tem Bluetooth -- a ArduinoBLE nem inicia nela.", linha);
            return;
        }
        throw new ErroDeExecucao($"{oque}: a ArduinoBLE precisa de um radio BLE, e a {Maq.Perfil?.Nome ?? id} nao tem. "
                                 + "No simulador ela roda na Nano RP2040 Connect e nas ESP32.", linha);
    }

    // =====================================================================
    //  os objetos declarados
    // =====================================================================

    /// <summary>
    /// "BLEService ledService("19B10000-...")", "BLEByteCharacteristic
    /// sw("...", BLERead | BLEWrite)", "BLEDevice central = BLE.central();".
    /// Devolve o objeto (o mesmo que veio pronto do lado direito, quando veio).
    /// </summary>
    Objeto MontarObjetoDaArduinoBle(Objeto obj, string classe, List<Valor> args, Valor inicial)
    {
        if (inicial.Referencia is Objeto pronto && pronto.Rede is ServicoArduino or CaracteristicaArduino or DispositivoArduino)
        {
            // Uma caracteristica remota com tipo: "BLEByteCharacteristic c = peripheral.characteristic(...)".
            if (pronto.Rede is CaracteristicaArduino remota && CaracteristicasComTipo.TryGetValue(classe, out string tipoDaRemota))
            {
                remota.Tipo = TiposC.DoNome(tipoDaRemota, ModeloC);
                remota.Tamanho = Math.Max(1, ModeloC.Bits(remota.Tipo) / 8);
                return new Objeto(classe) { Rede = remota };
            }
            if (pronto.Rede is CaracteristicaArduino deTexto && classe == "BLEStringCharacteristic")
            {
                deTexto.DeTexto = true;
                return new Objeto(classe) { Rede = deTexto };
            }
            return pronto.Classe == classe ? pronto : new Objeto(classe) { Rede = pronto.Rede };
        }

        string S(int i) => i < args.Count ? args[i].Mostrar() : "";
        int N(int i) => i < args.Count ? (int)args[i].Num : 0;
        switch (classe)
        {
            case "BLEService":
                obj.Rede = new ServicoArduino { Uuid = Ble.Normalizar(S(0)) };
                break;

            case "BLEDevice":
                obj.Rede = new DispositivoArduino();
                break;

            case "BLEDescriptor":
                obj.Rede = new DescritorArduino { Uuid = Ble.Normalizar(S(0)), Texto = S(1) };
                break;

            case "BLEStringCharacteristic":
                obj.Rede = new CaracteristicaArduino
                    { Uuid = Ble.Normalizar(S(0)), Propriedades = N(1), Tamanho = Math.Max(0, N(2)), DeTexto = true };
                break;

            case "BLECharacteristic":
            {
                // (uuid, propriedades, tamanho, fixo) ou (uuid, propriedades, "valor inicial").
                var car = new CaracteristicaArduino { Uuid = Ble.Normalizar(S(0)), Propriedades = N(1), Tipo = TipoC.Nenhum };
                if (args.Count > 2 && args[2].EhTexto)
                {
                    car.Valor = TextoArduino.ParaBytes(args[2].Texto);
                    car.Tamanho = car.Valor.Length;
                }
                else
                {
                    car.Tamanho = Math.Max(0, N(2));
                    car.Fixo = args.Count > 3 && args[3].Verdadeiro;
                }
                obj.Rede = car;
                break;
            }

            default:
            {
                var tipo = TiposC.DoNome(CaracteristicasComTipo[classe], ModeloC);
                int bytes = Math.Max(1, ModeloC.Bits(tipo) / 8);
                obj.Rede = new CaracteristicaArduino
                    { Uuid = Ble.Normalizar(S(0)), Propriedades = N(1), Tamanho = bytes, Fixo = true, Tipo = tipo, Valor = new byte[bytes] };
                break;
            }
        }
        return obj;
    }

    static bool EhDeTexto(CaracteristicaArduino c) => c.DeTexto;

    /// <summary>Os bytes de um valor com tipo, na ordem do chip (little-endian).</summary>
    byte[] BytesDoTipo(Valor v, TipoC tipo, int tamanho)
    {
        if (tipo == TipoC.Float) return BitConverter.GetBytes((float)v.Num);
        if (tipo is TipoC.Double or TipoC.LongDouble) return tamanho >= 8 ? BitConverter.GetBytes(v.Num) : BitConverter.GetBytes((float)v.Num);
        var oito = BitConverter.GetBytes((long)v.Num);
        return oito[..Math.Clamp(tamanho, 1, 8)];
    }

    Valor ValorDosBytes(byte[] b, TipoC tipo, int tamanho)
    {
        var cheio = new byte[8];
        Array.Copy(b, cheio, Math.Min(b.Length, 8));
        double n = tipo switch
        {
            TipoC.Float => BitConverter.ToSingle(cheio, 0),
            TipoC.Double or TipoC.LongDouble => tamanho >= 8 ? BitConverter.ToDouble(cheio, 0) : BitConverter.ToSingle(cheio, 0),
            _ => BitConverter.ToInt64(cheio, 0),
        };
        return tipo == TipoC.Bool ? Valor.DeBool(n != 0) : Valor.DeC(n, tipo);
    }

    // =====================================================================
    //  o BLE (BLELocalDevice)
    // =====================================================================

    Valor ChamarBleLocal(string metodo, Chamada c, Ambiente amb)
    {
        ConferirChipDaArduinoBle($"BLE.{metodo}()", c.Linha);
        if (metodo == "setEventHandler")
        {
            int evento = c.Args.Count > 0 ? (int)Avaliar(c.Args[0], amb).Num : -1;
            if (c.Args.Count > 1) eventosDoBle[evento] = RotinaDe(c.Args[1], amb, "BLE.setEventHandler()");
            return Valor.Vazio;
        }
        var args = c.Args.Select(a => Avaliar(a, amb)).ToList();
        string S(int i) => i < args.Count ? args[i].Mostrar() : "";

        if (metodo == "begin")
        {
            if (radioBle is not null) return Valor.DeInt(1);
            if (!LigarRadio("arduinoble", c.Linha)) return Valor.DeInt(0);
            radioBle = Maq.Rede.IniciarBle(Maq, Maq.Perfil?.Id == "nano-rp2040-connect" ? "Arduino" : "ESP32");
            radioBle.ArduinoBle = true;
            return Valor.DeInt(1);
        }
        if (radioBle is null)
        {
            // Sem o begin() o modulo nem respondeu: o resto nao faz nada.
            return metodo switch
            {
                "address" => Valor.DeTexto("00:00:00:00:00:00"),
                "central" or "available" => Valor.DeReferencia(new Objeto("BLEDevice") { Rede = new DispositivoArduino() }),
                _ => Valor.DeInt(0),
            };
        }

        EntregarArduinoBle();
        switch (metodo)
        {
            case "end":
                Maq.Rede.EsquecerBle(Maq);
                DesligarRadio("arduinoble");
                radioBle = null;
                return Valor.Vazio;

            case "poll":
            {
                // poll(timeout): espera ate algo acontecer, ou o tempo acabar.
                long espera = args.Count > 0 ? (long)args[0].Num : 0;
                for (long feito = 0; feito < espera && !Maq.Parar; feito += 10)
                {
                    Maq.Avancar(10);
                    if (EntregarArduinoBle()) break;
                }
                return Valor.Vazio;
            }

            case "setLocalName":
            case "setDeviceName":
                radioBle.Nome = S(0);
                return Valor.DeBool(true);

            case "setAdvertisedService":
            case "setAdvertisedServiceUuid":
            {
                string uuid = args.Count > 0 && args[0].Referencia is Objeto { Rede: ServicoArduino s } ? s.Uuid : Ble.Normalizar(S(0));
                radioBle.UuidsAnunciados.Clear();
                radioBle.UuidsAnunciados.Add(uuid);
                return Valor.DeBool(true);
            }

            case "addService":
            {
                if (args.Count == 0 || args[0].Referencia is not Objeto { Rede: ServicoArduino servico }) return Valor.Vazio;
                var real = new ServicoBle(radioBle, servico.Uuid) { Iniciado = true };
                foreach (var co in servico.Caracteristicas)
                {
                    var car = (CaracteristicaArduino)co.Rede;
                    int p = car.Propriedades;
                    var cb = new CaracteristicaBle(real, car.Uuid,
                        le: (p & 0x02) != 0, escreve: (p & 0x08) != 0, escreveSemResposta: (p & 0x04) != 0,
                        notifica: (p & 0x10) != 0, indica: (p & 0x20) != 0, propriedades: p) { Valor = car.Valor };
                    car.Real = cb;
                    lock (real.Caracteristicas) real.Caracteristicas.Add(cb);
                    locaisArduino[cb] = co;
                }
                servico.Real = real;
                lock (radioBle.Servicos) radioBle.Servicos.Add(real);
                radioBle.TemServidor = true;
                return Valor.Vazio;
            }

            case "advertise":
                Maq.Rede.AnunciarBle(radioBle, true);
                return Valor.DeInt(1);

            case "stopAdvertise":
                Maq.Rede.AnunciarBle(radioBle, false);
                return Valor.Vazio;

            case "central":
            {
                // A central que ainda esta conectada AGORA: o evento de saida
                // pode chegar depois do connected() ja ter dado falso, e o
                // central() da ArduinoBLE e o connected() olham o mesmo estado.
                var conectada = centraisArduino.Values.FirstOrDefault(o => DispositivoConectado((DispositivoArduino)o.Rede));
                return Valor.DeReferencia(conectada ?? new Objeto("BLEDevice") { Rede = new DispositivoArduino() });
            }

            case "connected":
                return Valor.DeBool(radioBle.Conectados > 0);

            case "disconnect":
            {
                if (radioBle.CelularConectado) Maq.Rede.DesconectarBleDoCelular(pelaPlaca: true);
                List<ConexaoBle> clientes;
                lock (radioBle.Clientes) clientes = [.. radioBle.Clientes];
                foreach (var conexao in clientes) Maq.Rede.DesconectarBlePlaca(conexao);
                return Valor.DeBool(true);
            }

            case "address": return Valor.DeTexto(radioBle.Endereco);
            case "rssi": return Valor.DeInt(centralArduino?.Rede is DispositivoArduino { Remoto: { } r } ? r.Rssi : 127);

            case "scan":
            case "scanForName":
            case "scanForUuid":
            case "scanForAddress":
                varrendoArduino = true;
                vistosArduino.Clear();
                achadosArduino.Clear();
                filtroNome = metodo == "scanForName" ? S(0) : null;
                filtroUuid = metodo == "scanForUuid" ? Ble.Normalizar(S(0)) : null;
                filtroEndereco = metodo == "scanForAddress" ? S(0) : null;
                duplicadosArduino = args.Count > (metodo == "scan" ? 0 : 1) && args[metodo == "scan" ? 0 : 1].Verdadeiro;
                return Valor.DeInt(1);

            case "stopScan":
                varrendoArduino = false;
                return Valor.Vazio;

            case "available":
            {
                VarrerArduinoBle();
                return Valor.DeReferencia(achadosArduino.Count > 0 ? achadosArduino.Dequeue()
                                          : new Objeto("BLEDevice") { Rede = new DispositivoArduino() });
            }

            case "setConnectable": case "setAdvertisingInterval": case "setConnectionInterval": case "setSupervisionTimeout":
            case "setAppearance": case "setTimeout": case "debug": case "noDebug": case "setPairable":
            case "setManufacturerData": case "setAdvertisedServiceData": case "setAdvertisingData": case "setScanResponseData":
                return Valor.DeBool(true);
            case "paired": case "pairable": return Valor.DeBool(false);
        }
        throw new ErroDeExecucao($"BLE.{metodo}() ainda nao e suportado pelo simulador.", c.Linha);
    }

    /// <summary>
    /// Olha o ar: cada periferico anunciando que passa no filtro do
    /// scanFor...() entra na fila do BLE.available() -- uma vez so, sem o
    /// withDuplicates. Com o BLEDiscovered, o evento recebe o aparelho.
    /// </summary>
    void VarrerArduinoBle()
    {
        if (!varrendoArduino || radioBle is null) return;
        foreach (var radio in Maq.Rede.AnunciosBleVistosDe(Maq))
        {
            if (filtroNome is not null && radio.Nome != filtroNome) continue;
            if (filtroUuid is not null && !radio.UuidsAnunciados.Contains(filtroUuid, StringComparer.OrdinalIgnoreCase)) continue;
            if (filtroEndereco is not null && !string.Equals(radio.Endereco, filtroEndereco, StringComparison.OrdinalIgnoreCase)) continue;
            if (!vistosArduino.Add(radio.Endereco) && !duplicadosArduino) continue;

            var achado = new Objeto("BLEDevice")
            {
                Rede = new DispositivoArduino
                {
                    Remoto = radio, Endereco = radio.Endereco, Nome = radio.Nome ?? "", Rssi = radio.Rssi,
                    Uuids = [.. radio.UuidsAnunciados],
                },
            };
            lock (perifericosArduino) perifericosArduino.Add(achado);
            if (eventosDoBle.TryGetValue(2, out var aoAchar) && aoAchar is not null) ChamarRotina(aoAchar, Valor.DeReferencia(achado));
            else achadosArduino.Enqueue(achado);
        }
    }

    // =====================================================================
    //  os eventos (entregues na thread do sketch, a cada chamada da BLE)
    // =====================================================================

    /// <summary>Entrega o que chegou pelo ar. Verdadeiro: algo aconteceu (o poll(timeout) para de esperar).</summary>
    bool EntregarArduinoBle()
    {
        var radio = radioBle;
        if (radio is null) return false;
        if (eventosDoBle.ContainsKey(2)) VarrerArduinoBle();

        bool algum = false;
        for (int volta = 0; volta < 50 && radio.ProximoEvento(out var ev); volta++)
        {
            algum = true;
            switch (ev.Tipo)
            {
                case TipoDeEventoBle.Conectou:
                {
                    var central = new Objeto("BLEDevice")
                    {
                        Rede = new DispositivoArduino
                        {
                            Celular = ev.Conexao is null,
                            Conexao = ev.Conexao,
                            Endereco = ev.Conexao?.Cliente.Endereco ?? "5c:e9:1e:3a:7b:21",
                            Nome = ev.Conexao?.Cliente.Nome ?? "celular",
                            Rssi = ev.Conexao?.Cliente.Rssi ?? -60,
                        },
                    };
                    centraisArduino[(object)ev.Conexao ?? this] = central;
                    centralArduino = central;
                    if (eventosDoBle.TryGetValue(0, out var h) && h is not null) ChamarRotina(h, Valor.DeReferencia(central));
                    break;
                }

                case TipoDeEventoBle.Desconectou:
                {
                    object chave = (object)ev.Conexao ?? this;
                    centraisArduino.Remove(chave, out var saiu);
                    if (centralArduino == saiu) centralArduino = centraisArduino.Values.FirstOrDefault();
                    if (eventosDoBle.TryGetValue(1, out var h) && h is not null)
                        ChamarRotina(h, Valor.DeReferencia(saiu ?? new Objeto("BLEDevice") { Rede = new DispositivoArduino() }));
                    break;
                }

                case TipoDeEventoBle.Escreveu:
                case TipoDeEventoBle.Leu:
                case TipoDeEventoBle.Assinou:
                {
                    if (ev.Caracteristica is null || !locaisArduino.TryGetValue(ev.Caracteristica, out var co)) break;
                    var car = (CaracteristicaArduino)co.Rede;
                    int evento = ev.Tipo switch
                    {
                        TipoDeEventoBle.Escreveu => 3,
                        TipoDeEventoBle.Leu => 2,
                        _ => ev.Valor > 0 ? 0 : 1,
                    };
                    if (ev.Tipo == TipoDeEventoBle.Escreveu)
                    {
                        car.Valor = ev.Caracteristica.Valor;
                        car.Escrita = true;
                    }
                    if (!car.Eventos.TryGetValue(evento, out var h) || h is null) break;
                    var quem = centraisArduino.GetValueOrDefault((object)ev.Conexao ?? this)
                               ?? new Objeto("BLEDevice") { Rede = new DispositivoArduino() };
                    ChamarRotina(h, Valor.DeReferencia(quem), Valor.DeReferencia(new Objeto("BLECharacteristic") { Rede = car }));
                    break;
                }

                case TipoDeEventoBle.Notificou:
                {
                    if (ev.Conexao is null || ev.Caracteristica is null) break;
                    Objeto remota;
                    lock (remotasArduino) remotasArduino.TryGetValue((ev.Conexao, ev.Caracteristica), out remota);
                    if (remota?.Rede is not CaracteristicaArduino car) break;
                    car.Valor = ev.Dados ?? [];
                    car.Atualizada = true;
                    if (car.Eventos.TryGetValue(3, out var h) && h is not null)
                        ChamarRotina(h, Valor.DeReferencia(PerifericoDa(ev.Conexao)), Valor.DeReferencia(new Objeto("BLECharacteristic") { Rede = car }));
                    break;
                }

                case TipoDeEventoBle.Caiu:
                {
                    var periferico = PerifericoDa(ev.Conexao);
                    if (periferico.Rede is DispositivoArduino d) d.Conexao = null;
                    if (eventosDoBle.TryGetValue(1, out var h) && h is not null) ChamarRotina(h, Valor.DeReferencia(periferico));
                    break;
                }
            }
        }
        return algum;
    }

    Objeto PerifericoDa(ConexaoBle conexao)
    {
        lock (perifericosArduino)
            return perifericosArduino.FirstOrDefault(p => p.Rede is DispositivoArduino d && d.Conexao == conexao)
                   ?? new Objeto("BLEDevice") { Rede = new DispositivoArduino { Endereco = conexao?.Servidor.Endereco ?? "00:00:00:00:00:00" } };
    }

    // =====================================================================
    //  os metodos dos objetos
    // =====================================================================

    Valor ChamarObjetoDaArduinoBle(Objeto obj, string metodo, Chamada c, Ambiente amb)
    {
        EntregarArduinoBle();
        return obj.Rede switch
        {
            DispositivoArduino d => ChamarDispositivoArduino(obj, d, metodo, c, amb),
            ServicoArduino s => ChamarServicoArduino(s, metodo, c, amb),
            CaracteristicaArduino car => ChamarCaracteristicaArduino(obj, car, metodo, c, amb),
            DescritorArduino desc => metodo switch
            {
                "uuid" => Valor.DeTexto(desc.Uuid),
                "value" => Valor.DeTexto(desc.Texto ?? ""),
                "valueLength" or "valueSize" => Valor.DeInt(TextoArduino.ParaBytes(desc.Texto ?? "").Length),
                _ => throw new ErroDeExecucao($"BLEDescriptor::{metodo}() ainda nao e suportado pelo simulador.", c.Linha),
            },
            _ => throw new ErroDeExecucao($"{obj.Classe}::{metodo}() ainda nao e suportado pelo simulador.", c.Linha),
        };
    }

    bool DispositivoConectado(DispositivoArduino d)
        => d.Celular ? radioBle is { CelularConectado: true } && Maq.Rede.BleDoCelular == radioBle
           : d.Conexao is { Ativa: true };

    Valor ChamarDispositivoArduino(Objeto obj, DispositivoArduino d, string metodo, Chamada c, Ambiente amb)
    {
        var args = c.Args.Select(a => Avaliar(a, amb)).ToList();
        string S(int i) => i < args.Count ? args[i].Mostrar() : "";
        bool conectado = DispositivoConectado(d);

        switch (metodo)
        {
            case "address": return Valor.DeTexto(d.Endereco);
            case "connected": return Valor.DeBool(conectado);
            case "rssi": return Valor.DeInt(d.Remoto?.Rssi ?? d.Rssi);
            case "localName": case "deviceName": return Valor.DeTexto(d.Nome ?? "");
            case "hasLocalName": return Valor.DeBool(!string.IsNullOrEmpty(d.Nome));
            case "advertisedServiceUuid":
            {
                int i = args.Count > 0 ? (int)args[0].Num : 0;
                return Valor.DeTexto(i >= 0 && i < d.Uuids.Count ? d.Uuids[i] : "");
            }
            case "hasAdvertisedServiceUuid": return Valor.DeBool(d.Uuids.Count > (args.Count > 0 ? (int)args[0].Num : 0));
            case "advertisedServiceUuidCount": return Valor.DeInt(d.Uuids.Count);
            case "hasManufacturerData": case "hasAdvertisementData": return Valor.DeBool(false);
            case "appearance": return Valor.DeInt(0);
            case "poll": return Valor.Vazio;

            case "disconnect":
                if (d.Celular) { if (conectado) Maq.Rede.DesconectarBleDoCelular(pelaPlaca: true); }
                else if (d.Conexao is not null) Maq.Rede.DesconectarBlePlaca(d.Conexao);
                return Valor.DeBool(true);

            case "connect":
            {
                if (conectado) return Valor.DeBool(true);
                if (radioBle is null || d.Remoto is null) return Valor.DeBool(false);
                Maq.Avancar(50);
                d.Conexao = Maq.Rede.ConectarBlePlaca(radioBle, d.Remoto);
                return Valor.DeBool(d.Conexao is not null);
            }

            case "discoverAttributes":
                if (!conectado) return Valor.DeBool(false);
                Maq.Avancar(50);
                return Valor.DeBool(true);

            case "discoverService":
                return Valor.DeBool(conectado && d.Conexao.Servidor.ServicosIniciados().Any(s => s.Uuid == Ble.Normalizar(S(0))));

            case "serviceCount": return Valor.DeInt(conectado && d.Conexao is not null ? d.Conexao.Servidor.ServicosIniciados().Count : 0);
            case "hasService": return Valor.DeBool(conectado && d.Conexao is not null
                                                   && d.Conexao.Servidor.ServicosIniciados().Any(s => s.Uuid == Ble.Normalizar(S(0))));
            case "service":
            {
                var servico = !conectado || d.Conexao is null ? null
                    : args.Count > 0 && !args[0].EhTexto ? d.Conexao.Servidor.ServicosIniciados().ElementAtOrDefault((int)args[0].Num)
                    : d.Conexao.Servidor.ServicosIniciados().FirstOrDefault(s => s.Uuid == Ble.Normalizar(S(0)));
                return Valor.DeReferencia(new Objeto("BLEService")
                    { Rede = new ServicoArduino { Uuid = servico?.Uuid ?? "", Real = servico, Conexao = d.Conexao ?? new ConexaoBle(null, null) } });
            }

            case "characteristicCount":
                return Valor.DeInt(conectado && d.Conexao is not null ? d.Conexao.Servidor.Visiveis().Count() : 0);
            case "hasCharacteristic":
                return Valor.DeBool(conectado && d.Conexao is not null && d.Conexao.Servidor.Visiveis().Any(x => x.Uuid == Ble.Normalizar(S(0))));
            case "characteristic":
            {
                CaracteristicaBle car = !conectado || d.Conexao is null ? null
                    : args.Count > 0 && !args[0].EhTexto ? d.Conexao.Servidor.Visiveis().ElementAtOrDefault((int)args[0].Num)
                    : d.Conexao.Servidor.Visiveis().FirstOrDefault(x => x.Uuid == Ble.Normalizar(S(0)));
                return Valor.DeReferencia(RemotaArduino(d.Conexao, car));
            }
        }
        throw new ErroDeExecucao($"BLEDevice::{metodo}() ainda nao e suportado pelo simulador.", c.Linha);
    }

    /// <summary>A mesma caracteristica remota para a mesma conexao: o valor e o evento dela ficam no objeto.</summary>
    Objeto RemotaArduino(ConexaoBle conexao, CaracteristicaBle car)
    {
        if (conexao is null || car is null)
            return new Objeto("BLECharacteristic") { Rede = new CaracteristicaArduino { Uuid = "", Conexao = conexao ?? new ConexaoBle(null, null) } };
        lock (remotasArduino)
        {
            if (remotasArduino.TryGetValue((conexao, car), out var existente)) return existente;
            var nova = new Objeto("BLECharacteristic")
            {
                Rede = new CaracteristicaArduino
                {
                    Uuid = car.Uuid, Propriedades = car.Propriedades, Conexao = conexao, Real = car,
                    Tamanho = car.Valor.Length, Valor = car.Valor,
                },
            };
            remotasArduino[(conexao, car)] = nova;
            return nova;
        }
    }

    Valor ChamarServicoArduino(ServicoArduino s, string metodo, Chamada c, Ambiente amb)
    {
        var args = c.Args.Select(a => Avaliar(a, amb)).ToList();
        string S(int i) => i < args.Count ? args[i].Mostrar() : "";
        switch (metodo)
        {
            case "uuid": return Valor.DeTexto(s.Uuid);
            case "addCharacteristic":
                if (args.Count > 0 && args[0].Referencia is Objeto { Rede: CaracteristicaArduino } car) s.Caracteristicas.Add(car);
                return Valor.Vazio;
            case "characteristicCount":
                return Valor.DeInt(s.Conexao is not null ? s.Real?.Caracteristicas.Count ?? 0 : s.Caracteristicas.Count);
            case "hasCharacteristic":
                return Valor.DeBool(s.Real?.Caracteristicas.Any(x => x.Uuid == Ble.Normalizar(S(0))) ?? false);
            case "characteristic":
            {
                if (s.Conexao is null)
                    return Valor.DeReferencia(s.Caracteristicas.FirstOrDefault(o => ((CaracteristicaArduino)o.Rede).Uuid == Ble.Normalizar(S(0)))
                                              ?? new Objeto("BLECharacteristic") { Rede = new CaracteristicaArduino { Conexao = new ConexaoBle(null, null) } });
                CaracteristicaBle achada = null;
                if (s.Real is { } real)
                    lock (real.Caracteristicas) achada = real.Caracteristicas.FirstOrDefault(x => x.Uuid == Ble.Normalizar(S(0)));
                return Valor.DeReferencia(RemotaArduino(s.Conexao, achada));
            }
            case "clear": return Valor.Vazio;
        }
        throw new ErroDeExecucao($"BLEService::{metodo}() ainda nao e suportado pelo simulador.", c.Linha);
    }

    Valor ChamarCaracteristicaArduino(Objeto obj, CaracteristicaArduino car, string metodo, Chamada c, Ambiente amb)
    {
        if (metodo == "setEventHandler")
        {
            int evento = c.Args.Count > 0 ? (int)Avaliar(c.Args[0], amb).Num : -1;
            if (c.Args.Count > 1) car.Eventos[evento] = RotinaDe(c.Args[1], amb, "setEventHandler()");
            return Valor.Vazio;
        }

        bool comTipo = CaracteristicasComTipo.ContainsKey(obj.Classe) || car.Tipo != TipoC.Nenhum && obj.Classe != "BLECharacteristic";
        bool deTexto = obj.Classe == "BLEStringCharacteristic" || EhDeTexto(car) && obj.Classe != "BLECharacteristic";

        // readValue(variavel): a variavel e escrita (o parametro e uma referencia).
        if (metodo == "readValue" && c.Args.Count > 0)
        {
            byte[] lidos = LerCaracteristicaArduino(car);
            if (c.Args.Count >= 2 && VetorDoArgumento(c.Args[0], amb, out var vetor))
            {
                int n = Math.Min(Math.Min(vetor.Length, (int)Avaliar(c.Args[1], amb).Num), lidos.Length);
                for (int i = 0; i < n; i++) vetor[i] = NoLugar(vetor[i], Valor.DeC(lidos[i], TipoC.UChar));
                return Valor.DeInt(n);
            }
            string nome = SemEndereco(c.Args[0]) is Variavel v ? v.Nome : null;
            var tipoDoAlvo = nome is null ? TipoC.UChar : TiposC.DoNome(amb.TipoDe(nome) ?? "byte", ModeloC);
            if (tipoDoAlvo == TipoC.Nenhum) tipoDoAlvo = TipoC.UChar;
            int tamanho = Math.Max(1, ModeloC.Bits(tipoDoAlvo) / 8);
            Guardar(c.Args[0], ValorDosBytes(lidos, tipoDoAlvo, tamanho), amb);
            return Valor.DeInt(Math.Min(tamanho, lidos.Length));
        }

        var args = c.Args.Select(a => Avaliar(a, amb)).ToList();
        int props = car.Real?.Propriedades ?? car.Propriedades;
        switch (metodo)
        {
            case "uuid": return Valor.DeTexto(car.Uuid ?? "");
            case "properties": return Valor.DeC(props, TipoC.UChar);
            case "valueSize": return Valor.DeInt(car.Tamanho);
            case "valueLength": return Valor.DeInt(ValorAtual(car).Length);
            case "canRead": return Valor.DeBool((props & 0x02) != 0);
            case "canWrite": return Valor.DeBool((props & 0x0C) != 0);
            case "canSubscribe": return Valor.DeBool((props & 0x30) != 0);
            case "canUnsubscribe": return Valor.DeBool((props & 0x30) != 0);

            case "value":
            case "valueLE":
            case "valueBE":
            {
                var bytes = ValorAtual(car);
                if (deTexto) return Valor.DeTexto(TextoArduino.DeBytes(bytes));
                if (comTipo)
                {
                    if (metodo == "valueBE") bytes = [.. bytes.Reverse()];
                    return ValorDosBytes(bytes, car.Tipo, car.Tamanho);
                }
                return Valor.DeReferencia(bytes.Select(b => Valor.DeC(b, TipoC.UChar)).ToArray());
            }

            case "written":
            {
                bool foi = car.Escrita;
                car.Escrita = false;
                return Valor.DeBool(foi);
            }

            case "valueUpdated":
            {
                bool veio = car.Atualizada;
                car.Atualizada = false;
                return Valor.DeBool(veio);
            }

            case "subscribed":
            {
                if (car.Real is null || radioBle is null) return Valor.DeBool(false);
                bool celular = car.Real.Assinada && radioBle.CelularConectado;
                List<ConexaoBle> clientes;
                lock (radioBle.Clientes) clientes = [.. radioBle.Clientes];
                return Valor.DeBool(celular || clientes.Any(x => x.Assinou(car.Real)));
            }

            case "read":
                LerCaracteristicaArduino(car);
                return Valor.DeBool(car.Real is not null && (props & 0x02) != 0);

            case "subscribe":
                return Valor.DeBool(car.Remota && Maq.Rede.AssinarDaPlaca(car.Conexao, car.Real, true, indicacao: (props & 0x10) == 0));
            case "unsubscribe":
                return Valor.DeBool(car.Remota && Maq.Rede.AssinarDaPlaca(car.Conexao, car.Real, false, false));

            case "writeValue":
            case "setValue":
            case "writeValueLE":
            case "setValueLE":
            case "writeValueBE":
            case "setValueBE":
            {
                byte[] dados;
                bool resposta = true;
                if (deTexto) dados = TextoArduino.ParaBytes(args.Count > 0 ? args[0].Mostrar() : "");
                else if (comTipo)
                {
                    dados = BytesDoTipo(args.Count > 0 ? args[0] : Valor.DeInt(0), car.Tipo, car.Tamanho);
                    if (metodo.EndsWith("BE", StringComparison.Ordinal)) dados = [.. dados.Reverse()];
                }
                else
                {
                    // As sobrecargas da generica: (bytes, tamanho, resposta), (texto, resposta), (numero, resposta).
                    Expr primeiro = c.Args.Count > 0 ? c.Args[0] : null;
                    bool ponteiro = primeiro is Conversao { Tipo: { } t } && t.TrimEnd().EndsWith('*')
                                    || primeiro is not null && !ReferenceEquals(SemEndereco(primeiro), primeiro);
                    if (ponteiro || args.Count > 0 && args[0].Referencia is Valor[])
                    {
                        dados = BytesDoSetValue(c, amb);
                        resposta = args.Count < 3 || args[2].Verdadeiro;
                    }
                    else if (args.Count > 0 && args[0].EhTexto)
                    {
                        dados = TextoArduino.ParaBytes(args[0].Texto);
                        resposta = args.Count < 2 || args[1].Verdadeiro;
                    }
                    else
                    {
                        var v = args.Count > 0 ? args[0] : Valor.DeInt(0);
                        int tamanho = v.Tipo switch
                        {
                            TipoC.Short or TipoC.UShort => 2,
                            TipoC.Int or TipoC.UInt or TipoC.Long or TipoC.ULong or TipoC.LongLong or TipoC.ULongLong => 4,
                            _ => 1,
                        };
                        dados = BytesDoTipo(v, TipoC.Long, tamanho);
                        resposta = args.Count < 2 || args[1].Verdadeiro;
                    }
                }

                if (car.Remota)
                {
                    if (car.Real is null) return Valor.DeInt(0);
                    bool ok = Maq.Rede.EscreverDaPlaca(car.Conexao, car.Real, dados, resposta && (props & 0x08) != 0);
                    if (ok && resposta) Maq.Avancar(30);
                    return Valor.DeInt(ok ? 1 : 0);
                }

                // Local: o valor muda, e quem assinou recebe a notificacao.
                if (car.Fixo && car.Tamanho > 0 && dados.Length != car.Tamanho) Array.Resize(ref dados, car.Tamanho);
                else if (car.Tamanho > 0 && dados.Length > car.Tamanho) dados = dados[..car.Tamanho];
                car.Valor = dados;
                if (car.Real is not null)
                {
                    car.Real.Valor = dados;
                    if ((props & 0x30) != 0) Maq.Rede.NotificarDaPlaca(car.Real, indicacao: (props & 0x10) == 0);
                }
                return Valor.DeInt(1);
            }

            case "broadcast": return Valor.DeInt(1);
            case "addDescriptor":
                if (args.Count > 0 && args[0].Referencia is Objeto { Rede: DescritorArduino { Uuid: "00002901-0000-1000-8000-00805f9b34fb" } d2901 }
                    && car.Real is not null) car.Real.Descricao = d2901.Texto;
                return Valor.Vazio;
            case "descriptorCount": return Valor.DeInt(0);
            case "hasDescriptor": return Valor.DeBool(false);
        }
        throw new ErroDeExecucao($"{obj.Classe}::{metodo}() ainda nao e suportado pelo simulador.", c.Linha);
    }

    /// <summary>O valor de agora: o da caracteristica local, ou o ultimo que chegou da remota.</summary>
    static byte[] ValorAtual(CaracteristicaArduino car)
        => car.Remota ? car.Valor : car.Real?.Valor ?? car.Valor;

    /// <summary>A leitura de uma remota vai ao ar (~1 intervalo de conexao) e guarda o que voltou.</summary>
    byte[] LerCaracteristicaArduino(CaracteristicaArduino car)
    {
        if (!car.Remota) return ValorAtual(car);
        if (car.Real is null) return [];
        Maq.Avancar(30);
        var lidos = Maq.Rede.LerDaPlaca(car.Conexao, car.Real);
        if (lidos is not null) car.Valor = lidos;
        return car.Valor;
    }
}
