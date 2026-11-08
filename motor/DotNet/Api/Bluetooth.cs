// Bluetooth.cs -- o servidor GATT do nanoFramework (nanoFramework.Device.Bluetooth,
// com os nomes e o comportamento dos fontes de verdade, licenca MIT, (c) .NET
// Foundation e colaboradores) sobre o BLE do simulador: o celular (ou outra
// placa) acha o anuncio, le, escreve e recebe o notify, como no BLE do core.
// O Buffer, o DataReader e o DataWriter sao os de la.
using System;
using System.Collections;
using System.Text;
using CircuitSketch;
using nanoFramework.Device.Bluetooth.GenericAttributeProfile;

namespace nanoFramework.Device.Bluetooth
{
    public enum BluetoothError
    {
        Success = 0,
        RadioNotAvailable = 1,
        ResourceInUse = 2,
        DeviceNotConnected = 3,
        OtherError = 4,
        DisabledByPolicy = 5,
        NotSupported = 6,
        DisabledByUser = 7,
        ConsentRequired = 8,
        TransportNotSupported = 9,
    }

    public class Buffer
    {
        byte[] _buffer;
        protected uint _length;

        public Buffer(uint capacity)
        {
            _buffer = new byte[capacity];
            _length = 0;
        }

        public Buffer(byte[] array)
        {
            _buffer = array;
            _length = (uint)array.Length;
        }

        public uint Capacity => (uint)_buffer.Length;

        public uint Length
        {
            get => _length;
            set
            {
                if (value > _buffer.Length) throw new ArgumentException("Length greater than current buffer");
                _length = value;
            }
        }

        public void EnsureCapacity(uint newCapacity)
        {
            if (newCapacity <= _buffer.Length) return;
            var novo = new byte[newCapacity];
            Array.Copy(_buffer, 0, novo, 0, _buffer.Length);
            _buffer = novo;
        }

        internal byte[] Data => _buffer;

        internal byte[] Bytes()
        {
            var r = new byte[_length];
            Array.Copy(_buffer, r, (int)_length);
            return r;
        }
    }

    public class DataWriter : Buffer
    {
        public DataWriter() : base(32) { }

        public uint MeasureString(string value) => value is null ? throw new ArgumentNullException(nameof(value)) : (uint)Encoding.UTF8.GetBytes(value).Length;
        public void WriteBoolean(bool value) => WriteBytes(BitConverter.GetBytes(value));
        public void WriteBuffer(Buffer buffer) => WriteBuffer(buffer, 0, buffer.Length);
        public void WriteBuffer(Buffer buffer, uint start, uint count)
        {
            var copia = new byte[count];
            Array.Copy(buffer.Data, (int)start, copia, 0, (int)count);
            WriteBytes(copia);
        }
        public void WriteByte(byte value) => WriteBytes(new[] { value });
        public void WriteBytes(byte[] value)
        {
            EnsureCapacity((uint)(Length + value.Length));
            Array.Copy(value, 0, Data, (int)Length, value.Length);
            Length += (uint)value.Length;
        }
        public void WriteDateTime(DateTime value) => WriteInt64(value.Ticks);
        public void WriteDouble(double value) => WriteBytes(BitConverter.GetBytes(value));
        public void WriteGuid(Guid value) => WriteBytes(value.ToByteArray());
        public void WriteUuid(Guid value) => Ordenar(value, new[] { 15, 14, 13, 12, 11, 10, 9, 8, 6, 7, 4, 5, 0, 1, 2, 3 });
        public void WriteUuid2(Guid value) => Ordenar(value, new[] { 3, 2, 1, 0, 5, 4, 7, 6, 8, 9, 10, 11, 12, 13, 14, 15 });
        void Ordenar(Guid value, int[] ordem)
        {
            byte[] de = value.ToByteArray(), para = new byte[16];
            for (int i = 0; i < 16; i++) para[i] = de[ordem[i]];
            WriteBytes(para);
        }
        public void WriteInt16(short value) => WriteBytes(BitConverter.GetBytes(value));
        public void WriteInt32(int value) => WriteBytes(BitConverter.GetBytes(value));
        public void WriteInt64(long value) => WriteBytes(BitConverter.GetBytes(value));
        public void WriteSingle(float value) => WriteBytes(BitConverter.GetBytes(value));
        public uint WriteString(string value)
        {
            if (value is null) throw new ArgumentNullException(nameof(value));
            byte[] b = Encoding.UTF8.GetBytes(value);
            WriteBytes(b);
            return (uint)b.Length;
        }
        public void WriteTimeSpan(TimeSpan value) => WriteInt64(value.Ticks);
        public void WriteUInt16(ushort value) => WriteBytes(BitConverter.GetBytes(value));
        public void WriteUInt32(uint value) => WriteBytes(BitConverter.GetBytes(value));
        public void WriteUInt64(ulong value) => WriteBytes(BitConverter.GetBytes(value));
        public Buffer DetachBuffer() => this;
    }

    public class DataReader
    {
        Buffer _buffer;
        int _posicao;
        const int defaultBufferSize = 32;

        DataReader(Buffer buffer) => _buffer = buffer;

        public static DataReader FromBuffer(Buffer buffer) => new DataReader(buffer);

        public uint UnconsumedBufferLength => _buffer.Length - (uint)_posicao;

        int Andar(int count)
        {
            if (UnconsumedBufferLength < count) throw new ArgumentOutOfRangeException("count", "No data in buffer");
            int p = _posicao;
            _posicao += count;
            return p;
        }

        void Conferir()
        {
            if (_posicao != _buffer.Length) return;
            _buffer = new Buffer(defaultBufferSize);
            _posicao = 0;
        }

        T Ler<T>(int n, Func<byte[], int, T> conv)
        {
            byte[] dados = _buffer.Data;
            var v = conv(dados, Andar(n));
            Conferir();
            return v;
        }

        public bool ReadBoolean() => Ler(1, (d, i) => d[i] > 0);
        public byte ReadByte() => Ler(1, (d, i) => d[i]);
        public Buffer ReadBuffer(uint length)
        {
            var b = new Buffer(length);
            Array.Copy(_buffer.Data, Andar((int)length), b.Data, 0, (int)length);
            b.Length = length;
            Conferir();
            return b;
        }
        public void ReadBytes(byte[] value)
        {
            if (value is null) throw new ArgumentNullException("Argument is null");
            Array.Copy(_buffer.Data, Andar(value.Length), value, 0, value.Length);
            Conferir();
        }
        public DateTime ReadDateTime() => new DateTime(ReadInt64());
        public double ReadDouble() => Ler(8, BitConverter.ToDouble);
        public Guid ReadGuid()
        {
            var b = new byte[16];
            ReadBytes(b);
            return new Guid(b);
        }
        public Guid ReadUuid() => Reordenar(new[] { 12, 13, 14, 15, 10, 11, 8, 9, 7, 6, 5, 4, 3, 2, 1, 0 });
        public Guid ReadUuid2() => Reordenar(new[] { 3, 2, 1, 0, 5, 4, 7, 6, 8, 9, 10, 11, 12, 13, 14, 15 });
        Guid Reordenar(int[] ordem)
        {
            byte[] de = new byte[16], para = new byte[16];
            ReadBytes(de);
            for (int i = 0; i < 16; i++) para[i] = de[ordem[i]];
            return new Guid(para);
        }
        public short ReadInt16() => Ler(2, BitConverter.ToInt16);
        public int ReadInt32() => Ler(4, BitConverter.ToInt32);
        public long ReadInt64() => Ler(8, BitConverter.ToInt64);
        public float ReadSingle() => Ler(4, BitConverter.ToSingle);
        public string ReadString(uint codeUnitCount) => Ler((int)codeUnitCount, (d, i) => Encoding.UTF8.GetString(d, i, (int)codeUnitCount));
        public TimeSpan ReadTimeSpan() => new TimeSpan(ReadInt64());
        public ushort ReadUInt16() => Ler(2, BitConverter.ToUInt16);
        public uint ReadUInt32() => Ler(4, BitConverter.ToUInt32);
        public ulong ReadUInt64() => Ler(8, BitConverter.ToUInt64);
    }

    public enum UuidType { Uuid16, Uuid32, Uuid128 }

    public static class Utilities
    {
        static readonly byte[] baseSig = new Guid("00000000-0000-1000-8000-00805F9B34FB").ToByteArray();

        public static Guid CreateUuidFromShortCode(ushort uuid16) => new Guid($"0000{uuid16:x4}-0000-1000-8000-00805f9b34fb");

        public static bool IsBluetoothSigUUID(Guid uuid)
        {
            byte[] b = uuid.ToByteArray();
            for (int i = 4; i < 16; i++) if (b[i] != baseSig[i]) return false;
            return true;
        }

        public static bool IsBluetoothSigUUID16(Guid uuid) => IsBluetoothSigUUID(uuid) && ConvertUuidToIntId(uuid) <= 0xFFFF;

        public static UuidType TypeOfUuid(Guid uuid) => !IsBluetoothSigUUID(uuid) ? UuidType.Uuid128 : ConvertUuidToIntId(uuid) <= 0xFFFF ? UuidType.Uuid16 : UuidType.Uuid32;

        public static ushort ConvertUuidToShortId(Guid uuid) => (ushort)ConvertUuidToIntId(uuid);

        public static uint ConvertUuidToIntId(Guid uuid) => BitConverter.ToUInt32(uuid.ToByteArray(), 0);

        /// <summary>O UUID como o BLE do simulador espera: o curto ("180d") quando e da SIG.</summary>
        internal static string Texto(Guid uuid) => IsBluetoothSigUUID16(uuid) ? ConvertUuidToShortId(uuid).ToString("x4") : uuid.ToString();
    }

    /// <summary>O servidor GATT da placa: o nome do aparelho e os servicos criados pelo GattServiceProvider.</summary>
    public class BluetoothLEServer : IDisposable
    {
        static BluetoothLEServer unico;
        internal readonly ArrayList servicos = new ArrayList();
        internal string servidor;
        string nome = "nanoFramework";
        bool iniciado;

        BluetoothLEServer() { }

        public static BluetoothLEServer Instance => unico ??= new BluetoothLEServer();

        public string DeviceName
        {
            get => nome;
            set => nome = value;
        }

        public ushort Appearance { get; set; }

        public GattServiceProvider[] Services => (GattServiceProvider[])servicos.ToArray(typeof(GattServiceProvider));

        public GattServiceProvider GetServiceByUUID(Guid serviceUuid)
        {
            foreach (GattServiceProvider s in servicos) if (s.Service.Uuid == serviceUuid) return s;
            return null;
        }

        /// <summary>Liga o radio com o nome e monta no simulador os servicos que ja existem.</summary>
        public void Start()
        {
            if (iniciado) return;
            iniciado = true;
            Ponte.C(null, "BLEDevice::init", nome);
            servidor = Ponte.T(null, "BLEDevice::createServer");
            foreach (GattServiceProvider s in servicos) s.Montar();
        }

        public void Stop()
        {
            if (!iniciado) return;
            Ponte.C(null, "BLEDevice::deinit", false);
            iniciado = false;
            servidor = null;
            foreach (GattServiceProvider s in servicos) s.Desmontar();
        }

        internal bool Iniciado => iniciado;

        public void Dispose() => Stop();
    }
}

namespace nanoFramework.Device.Bluetooth.GenericAttributeProfile
{
    using nanoFramework.Device.Bluetooth;

    [Flags]
    public enum GattCharacteristicProperties : uint
    {
        None = 0,
        Broadcast = 1,
        Read = 2,
        WriteWithoutResponse = 4,
        Write = 8,
        Notify = 16,
        Indicate = 32,
        AuthenticatedSignedWrites = 64,
        ExtendedProperties = 128,
        ReliableWrites = 256,
        WritableAuxiliaries = 512,
    }

    public enum GattProtectionLevel { Plain = 0, AuthenticationRequired = 1, EncryptionRequired = 2, EncryptionAndAuthenticationRequired = 3 }

    public enum GattWriteOption { WriteWithResponse = 0, WriteWithoutResponse = 1 }

    public enum GattCommunicationStatus { Success = 0, Unreachable = 1, ProtocolError = 2, AccessDenied = 3 }

    public enum GattServiceProviderAdvertisementStatus { Created, Stopped, Started, Aborted, StartedWithoutAllAdvertisementData }

    public static class GattProtocolError
    {
        public static byte InvalidHandle => 1;
        public static byte ReadNotPermitted => 2;
        public static byte WriteNotPermitted => 3;
        public static byte InvalidPdu => 4;
        public static byte InsufficientAuthentication => 5;
        public static byte RequestNotSupported => 6;
        public static byte InvalidOffset => 7;
        public static byte InsufficientAuthorization => 8;
        public static byte PrepareQueueFull => 9;
        public static byte AttributeNotFound => 10;
        public static byte AttributeNotLong => 11;
        public static byte InsufficientEncryptionKeySize => 12;
        public static byte InvalidAttributeValueLength => 13;
        public static byte UnlikelyError => 14;
        public static byte InsufficientEncryption => 15;
        public static byte UnsupportedGroupType => 15;
        public static byte InsufficientResources => 17;
    }

    public delegate void GattLocalCharacteristicReadEventHandler(GattLocalCharacteristic sender, GattReadRequestedEventArgs ReadRequestEventArgs);
    public delegate void GattLocalCharacteristicWriteEventHandler(GattLocalCharacteristic sender, GattWriteRequestedEventArgs WriteRequestEventArgs);
    public delegate void GattLocalCharacteristicClientsChangedEventHandler(GattLocalCharacteristic sender, object args);

    public class GattSubscribedClient
    {
        internal GattSubscribedClient() { }
        public ushort MaxNotificationSize => 1024;
    }

    public class GattClientNotificationResult
    {
        internal GattClientNotificationResult(GattCommunicationStatus s, ushort enviados, GattSubscribedClient c)
        {
            Status = s;
            BytesSent = enviados;
            SubscribedClient = c;
        }
        public byte ProtocolError { get; }
        public GattCommunicationStatus Status { get; }
        public GattSubscribedClient SubscribedClient { get; }
        public ushort BytesSent { get; }
    }

    public class GattReadRequest
    {
        readonly GattLocalCharacteristic dona;
        internal GattReadRequest(GattLocalCharacteristic c) => dona = c;
        public uint Offset => 0;
        public void RespondWithValue(Buffer value) => dona.Gravar(value.Bytes());
        public void RespondWithProtocolError(byte protocolError) { }
    }

    public class GattReadRequestedEventArgs
    {
        readonly GattReadRequest pedido;
        internal GattReadRequestedEventArgs(GattReadRequest r) => pedido = r;
        public GattReadRequest GetRequest() => pedido;
    }

    public class GattWriteRequest
    {
        internal GattWriteRequest(Buffer valor, GattWriteOption opcao)
        {
            Value = valor;
            Option = opcao;
        }
        public Buffer Value { get; }
        public uint Offset => 0;
        public GattWriteOption Option { get; }
        public void Respond() { }
        public void RespondWithProtocolError(byte protocolError) { }
    }

    public class GattWriteRequestedEventArgs
    {
        readonly GattWriteRequest pedido;
        internal GattWriteRequestedEventArgs(GattWriteRequest r) => pedido = r;
        public GattWriteRequest GetRequest() => pedido;
    }

    public class GattLocalCharacteristicParameters
    {
        public GattCharacteristicProperties CharacteristicProperties { get; set; }
        public string UserDescription { get; set; }
        public Buffer StaticValue { get; set; }
        public GattProtectionLevel ReadProtectionLevel { get; set; } = GattProtectionLevel.Plain;
        public GattProtectionLevel WriteProtectionLevel { get; set; } = GattProtectionLevel.Plain;
    }

    public class GattLocalCharacteristicResult
    {
        internal GattLocalCharacteristicResult(GattLocalCharacteristic c, BluetoothError e)
        {
            Characteristic = c;
            Error = e;
        }
        public GattLocalCharacteristic Characteristic { get; }
        public BluetoothError Error { get; }
    }

    /// <summary>A caracteristica local: o simulador chama o onRead e o onWrite, e eles viram os eventos daqui.</summary>
    public class GattLocalCharacteristic
    {
        readonly Guid uuid;
        readonly ArrayList assinantes = new ArrayList();
        internal string noSimulador;
        byte[] valor;

        internal GattLocalCharacteristic(Guid uuid, GattLocalCharacteristicParameters p)
        {
            this.uuid = uuid;
            CharacteristicProperties = p.CharacteristicProperties;
            UserDescription = p.UserDescription;
            StaticValue = p.StaticValue;
            ReadProtectionLevel = p.ReadProtectionLevel;
            WriteProtectionLevel = p.WriteProtectionLevel;
            if (p.StaticValue is not null) valor = p.StaticValue.Bytes();
        }

        public event GattLocalCharacteristicReadEventHandler ReadRequested;
        public event GattLocalCharacteristicWriteEventHandler WriteRequested;
        public event GattLocalCharacteristicClientsChangedEventHandler SubscribedClientsChanged;

        public Guid Uuid => uuid;
        public GattCharacteristicProperties CharacteristicProperties { get; }
        public string UserDescription { get; }
        public Buffer StaticValue { get; }
        public GattProtectionLevel ReadProtectionLevel { get; }
        public GattProtectionLevel WriteProtectionLevel { get; }
        public GattSubscribedClient[] SubscribedClients => (GattSubscribedClient[])assinantes.ToArray(typeof(GattSubscribedClient));

        /// <summary>As propriedades do BLE do core (PROPERTY_READ, _WRITE, _NOTIFY...).</summary>
        long PropriedadesDoCore()
        {
            var p = CharacteristicProperties;
            long r = 0;
            if ((p & GattCharacteristicProperties.Read) != 0) r |= 1 << 0;
            if ((p & GattCharacteristicProperties.Write) != 0) r |= 1 << 1;
            if ((p & GattCharacteristicProperties.Notify) != 0) r |= 1 << 2;
            if ((p & GattCharacteristicProperties.Broadcast) != 0) r |= 1 << 3;
            if ((p & GattCharacteristicProperties.Indicate) != 0) r |= 1 << 4;
            if ((p & GattCharacteristicProperties.WriteWithoutResponse) != 0) r |= 1 << 5;
            return r;
        }

        internal void Montar(string servico)
        {
            noSimulador = Ponte.T(servico, "createCharacteristic", Utilities.Texto(uuid), PropriedadesDoCore());
            if (UserDescription is not null) Ponte.C(noSimulador, "setDescription", UserDescription);
            int indice = int.Parse(Ponte.Registrar(new Action<string, object, object, object>(Chamado)).Substring("__nativa_".Length));
            Ponte.CN(noSimulador, "setCallbacks", new object[] { Ponte.T(null, "__callbacks", (long)indice) }, new[] { true });
            // O valor de partida: o estatico, ou o que o ReadRequested responder (o simulador entrega
            // o onRead depois da leitura, entao a resposta vale para a proxima).
            if (valor is null && ReadRequested is not null) ReadRequested(this, new GattReadRequestedEventArgs(new GattReadRequest(this)));
            if (valor is not null) Ponte.C(noSimulador, "setValue", valor, (long)valor.Length);
        }

        void Chamado(string metodo, object a, object b, object c)
        {
            switch (metodo)
            {
                case "onWrite":
                {
                    string texto = Ponte.T(noSimulador, "getValue");
                    var bytes = new byte[texto.Length];
                    for (int i = 0; i < bytes.Length; i++) bytes[i] = (byte)texto[i];
                    valor = bytes;
                    WriteRequested?.Invoke(this, new GattWriteRequestedEventArgs(new GattWriteRequest(new Buffer(bytes), GattWriteOption.WriteWithResponse)));
                    break;
                }
                case "onRead":
                    if (StaticValue is null) ReadRequested?.Invoke(this, new GattReadRequestedEventArgs(new GattReadRequest(this)));
                    break;
                case "onSubscribe":
                {
                    long cccd = c is long v ? v : 0;
                    if (cccd != 0 && assinantes.Count == 0) assinantes.Add(new GattSubscribedClient());
                    if (cccd == 0) assinantes.Clear();
                    SubscribedClientsChanged?.Invoke(this, null);
                    break;
                }
            }
        }

        internal void Gravar(byte[] bytes)
        {
            valor = bytes;
            if (noSimulador is not null) Ponte.C(noSimulador, "setValue", bytes, (long)bytes.Length);
        }

        public GattClientNotificationResult[] NotifyValue(Buffer value)
        {
            Gravar(value.Bytes());
            if (noSimulador is null) return Array.Empty<GattClientNotificationResult>();
            Ponte.C(noSimulador, (CharacteristicProperties & GattCharacteristicProperties.Indicate) != 0 && (CharacteristicProperties & GattCharacteristicProperties.Notify) == 0 ? "indicate" : "notify");
            return new[] { new GattClientNotificationResult(GattCommunicationStatus.Success, (ushort)value.Length, assinantes.Count > 0 ? (GattSubscribedClient)assinantes[0] : new GattSubscribedClient()) };
        }

        public GattClientNotificationResult NotifyValue(Buffer value, GattSubscribedClient subscribedClient)
        {
            var r = NotifyValue(value);
            return r.Length > 0 ? r[0] : new GattClientNotificationResult(GattCommunicationStatus.Unreachable, 0, subscribedClient);
        }
    }

    public class GattLocalService
    {
        readonly ArrayList caracteristicas = new ArrayList();
        internal string noSimulador;

        internal GattLocalService(Guid uuid) => Uuid = uuid;

        public Guid Uuid { get; }
        public GattLocalCharacteristic[] Characteristics => (GattLocalCharacteristic[])caracteristicas.ToArray(typeof(GattLocalCharacteristic));

        public GattLocalCharacteristicResult CreateCharacteristic(Guid characteristicUuid, GattLocalCharacteristicParameters parameters)
        {
            var c = new GattLocalCharacteristic(characteristicUuid, parameters ?? new GattLocalCharacteristicParameters());
            caracteristicas.Add(c);
            if (noSimulador is not null) c.Montar(noSimulador);
            return new GattLocalCharacteristicResult(c, BluetoothError.Success);
        }

        internal void Montar(string servidor)
        {
            noSimulador = Ponte.T(servidor, "createService", Utilities.Texto(Uuid));
            foreach (GattLocalCharacteristic c in caracteristicas) c.Montar(noSimulador);
            Ponte.C(noSimulador, "start");
        }

        internal void Desmontar()
        {
            noSimulador = null;
            foreach (GattLocalCharacteristic c in caracteristicas) c.noSimulador = null;
        }
    }

    public class GattServiceProviderResult
    {
        internal GattServiceProviderResult(GattServiceProvider p, BluetoothError e)
        {
            ServiceProvider = p;
            Error = e;
        }
        public GattServiceProvider ServiceProvider { get; }
        public BluetoothError Error { get; }
    }

    public class GattServiceProviderAdvertisingParameters
    {
        public bool IsDiscoverable { get; set; } = true;
        public bool IsConnectable { get; set; } = true;
        public Buffer ServiceData { get; set; }
        public bool CustomAdvertisement { get; set; }
    }

    public class GattServiceProvider
    {
        readonly GattLocalService servico;

        GattServiceProvider(Guid uuid) => servico = new GattLocalService(uuid);

        public static GattServiceProviderResult Create(Guid serviceUuid)
        {
            var p = new GattServiceProvider(serviceUuid);
            BluetoothLEServer.Instance.servicos.Add(p);
            if (BluetoothLEServer.Instance.Iniciado) p.Montar();
            return new GattServiceProviderResult(p, BluetoothError.Success);
        }

        public GattLocalService Service => servico;
        public GattServiceProviderAdvertisementStatus AdvertisementStatus { get; private set; } = GattServiceProviderAdvertisementStatus.Created;

        internal void Montar() => servico.Montar(BluetoothLEServer.Instance.servidor);
        internal void Desmontar() => servico.Desmontar();

        public void StartAdvertising() => StartAdvertising(new GattServiceProviderAdvertisingParameters());

        public void StartAdvertising(GattServiceProviderAdvertisingParameters parameters)
        {
            BluetoothLEServer.Instance.Start();
            string anuncio = Ponte.T(null, "BLEDevice::getAdvertising");
            Ponte.C(anuncio, "addServiceUUID", Utilities.Texto(servico.Uuid));
            Ponte.C(anuncio, "start");
            AdvertisementStatus = GattServiceProviderAdvertisementStatus.Started;
        }

        public void StopAdvertising()
        {
            Ponte.C(null, "BLEDevice::stopAdvertising");
            AdvertisementStatus = GattServiceProviderAdvertisementStatus.Stopped;
        }
    }
}
