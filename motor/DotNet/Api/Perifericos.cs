// Perifericos.cs -- PWM, ADC, I2C, SPI e a porta serial do nanoFramework e
// do .NET IoT, sobre as mesmas funcoes do core (ledc/analogWrite, analogRead,
// Wire, SPI, Serial1/Serial2), e a configuracao de pinos da ESP32.
using System;
using System.Collections.Generic;
using System.Text;
using CircuitSketch;

namespace System
{
    /// <summary>O SpanByte do nanoFramework: um pedaco de um byte[].</summary>
    public readonly struct SpanByte
    {
        readonly byte[] vetor;
        readonly int inicio;

        public SpanByte(byte[] array) : this(array, 0, array?.Length ?? 0) { }
        public SpanByte(byte[] array, int start, int length)
        {
            vetor = array ?? Array.Empty<byte>();
            inicio = start;
            Length = length;
        }

        public int Length { get; }
        public bool IsEmpty => Length == 0;
        public static SpanByte Empty => new SpanByte(Array.Empty<byte>());

        public byte this[int i]
        {
            get => vetor[inicio + i];
            set => vetor[inicio + i] = value;
        }

        public SpanByte Slice(int start) => new SpanByte(vetor, inicio + start, Length - start);
        public SpanByte Slice(int start, int length) => new SpanByte(vetor, inicio + start, length);
        public byte[] ToArray()
        {
            var r = new byte[Length];
            Array.Copy(vetor, inicio, r, 0, Length);
            return r;
        }
        public void CopyTo(SpanByte destino)
        {
            for (int i = 0; i < Length; i++) destino[i] = this[i];
        }

        public static implicit operator SpanByte(byte[] array) => new SpanByte(array);
    }
}

namespace nanoFramework.Hardware.Esp32
{
    public enum DeviceFunction
    {
        PWM1, PWM2, PWM3, PWM4, PWM5, PWM6, PWM7, PWM8, PWM9, PWM10, PWM11, PWM12, PWM13, PWM14, PWM15, PWM16,
        SPI1_MOSI, SPI1_MISO, SPI1_CLOCK, SPI2_MOSI, SPI2_MISO, SPI2_CLOCK,
        I2C1_DATA, I2C1_CLOCK, I2C2_DATA, I2C2_CLOCK,
        COM1_TX, COM1_RX, COM1_RTS, COM1_CTS, COM2_TX, COM2_RX, COM2_RTS, COM2_CTS, COM3_TX, COM3_RX, COM3_RTS, COM3_CTS,
        ADC1_CH0, ADC1_CH3, ADC1_CH4, ADC1_CH5, ADC1_CH6, ADC1_CH7,
    }

    /// <summary>O pino de cada funcao (a UART2 no 16/17): o SerialPort e o I2C leem daqui.</summary>
    public static class Configuration
    {
        static readonly Dictionary<DeviceFunction, int> pinos = new Dictionary<DeviceFunction, int>();

        public static void SetPinFunction(int pin, DeviceFunction function) => pinos[function] = pin;
        public static int GetFunctionPin(DeviceFunction function) => pinos.TryGetValue(function, out int p) ? p : -1;
    }

    public static class Sleep
    {
        public enum WakeupGpioPin : ulong { None = 0, Pin0 = 1UL, Pin2 = 1UL << 2, Pin4 = 1UL << 4, Pin12 = 1UL << 12, Pin13 = 1UL << 13, Pin14 = 1UL << 14, Pin15 = 1UL << 15, Pin25 = 1UL << 25, Pin26 = 1UL << 26, Pin27 = 1UL << 27, Pin32 = 1UL << 32, Pin33 = 1UL << 33 }
        public static void EnableWakeupByTimer(TimeSpan t) => Ponte.C(null, "esp_sleep_enable_timer_wakeup", (long)(t.TotalMilliseconds * 1000));
        public static void StartDeepSleep() => Ponte.C(null, "esp_deep_sleep_start");
    }
}

namespace System.Device.Pwm
{
    /// <summary>
    /// O PWM do pino: na ESP32 o LEDC (ledcAttach/ledcWrite), nas outras o
    /// analogWrite. O ServoMotor usa o pino dele.
    /// </summary>
    public class PwmChannel : IDisposable
    {
        const int Bits = 10;
        int frequencia;
        double ciclo;
        bool ligado;

        internal int Pino { get; }

        PwmChannel(int pino, int frequencia, double ciclo)
        {
            Pino = pino;
            this.frequencia = frequencia;
            this.ciclo = ciclo;
        }

        public static PwmChannel CreateFromPin(int pin, int frequency = 400, double dutyCyclePercentage = 0.5)
            => new PwmChannel(pin, frequency, dutyCyclePercentage);

        public static PwmChannel Create(int chip, int channel, int frequency = 400, double dutyCyclePercentage = 0.5)
            => new PwmChannel(channel, frequency, dutyCyclePercentage);

        public int Frequency
        {
            get => frequencia;
            set
            {
                frequencia = value;
                if (ligado) Aplicar(true);
            }
        }

        public double DutyCycle
        {
            get => ciclo;
            set
            {
                if (value < 0 || value > 1) throw new ArgumentOutOfRangeException(nameof(DutyCycle), "o ciclo vai de 0 a 1");
                ciclo = value;
                if (ligado) Aplicar(false);
            }
        }

        void Aplicar(bool configurar)
        {
            if (Ponte.EhEsp32)
            {
                if (configurar) Ponte.C(null, "ledcAttach", Pino, frequencia, Bits);
                Ponte.C(null, "ledcWrite", Pino, (long)Math.Round(ciclo * ((1 << Bits) - 1)));
            }
            else
            {
                Ponte.C(null, "analogWrite", Pino, (long)Math.Round(ciclo * 255));
            }
        }

        public void Start()
        {
            ligado = true;
            Aplicar(true);
        }

        public void Stop()
        {
            ligado = false;
            if (Ponte.EhEsp32) Ponte.C(null, "ledcWrite", Pino, 0);
            else Ponte.C(null, "analogWrite", Pino, 0);
        }

        public void Dispose() => Stop();
    }
}

namespace System.Device.Adc
{
    public enum AdcChannelMode { SingleEnded, Differential }

    public class AdcController
    {
        public int MaxValue => Ponte.MaximoDoAdc;
        public int MinValue => 0;
        public int ResolutionInBits => MaxValue >= 4095 ? 12 : 10;
        public int ChannelCount => Ponte.EhEsp32 ? 20 : 8;
        public AdcChannelMode ChannelMode { get; set; }
        public bool IsChannelModeSupported(AdcChannelMode mode) => mode == AdcChannelMode.SingleEnded;

        // Os canais do nanoFramework na ESP32: 0..7 o ADC1 (GPIO 36, 37, 38, 39, 32, 33, 34, 35), 10..19 o ADC2.
        static readonly int[] Adc1 = { 36, 37, 38, 39, 32, 33, 34, 35 };
        static readonly int[] Adc2 = { 4, 0, 2, 15, 13, 12, 14, 27, 25, 26 };

        internal static int PinoDoCanal(int canal)
        {
            if (Ponte.EhEsp32)
            {
                if (canal >= 0 && canal < 8) return Adc1[canal];
                if (canal >= 10 && canal < 20) return Adc2[canal - 10];
                throw new ArgumentOutOfRangeException(nameof(canal), $"a ESP32 nao tem o canal {canal} do ADC");
            }
            return Ponte.Numero("A" + canal, canal);
        }

        public AdcChannel OpenChannel(int channelNumber) => new AdcChannel(this, channelNumber, PinoDoCanal(channelNumber));
    }

    public class AdcChannel : IDisposable
    {
        readonly AdcController dono;
        readonly int pino;

        internal AdcChannel(AdcController dono, int canal, int pino)
        {
            this.dono = dono;
            ChannelNumber = canal;
            this.pino = pino;
        }

        public int ChannelNumber { get; }
        public AdcController Controller => dono;
        public int ReadValue() => (int)Ponte.I(null, "analogRead", pino);
        public double ReadRatio() => ReadValue() / (double)dono.MaxValue;
        public void Dispose() { }
    }
}

namespace System.Device.I2c
{
    public enum I2cBusSpeed { StandardMode, FastMode, FastModePlus }
    public enum I2cTransferStatus { UnknownError, FullTransfer, ClockStretchTimeout, PartialTransfer, SlaveAddressNotAcknowledged }

    public readonly struct I2cTransferResult
    {
        public I2cTransferResult(I2cTransferStatus status, int bytesTransferred)
        {
            Status = status;
            BytesTransferred = (uint)bytesTransferred;
        }
        public I2cTransferStatus Status { get; }
        public uint BytesTransferred { get; }
    }

    public sealed class I2cConnectionSettings
    {
        public I2cConnectionSettings(int busId, int deviceAddress, I2cBusSpeed busSpeed = I2cBusSpeed.StandardMode)
        {
            BusId = busId;
            DeviceAddress = deviceAddress;
            BusSpeed = busSpeed;
        }
        public int BusId { get; }
        public int DeviceAddress { get; }
        public I2cBusSpeed BusSpeed { get; }
    }

    /// <summary>O dispositivo no barramento: o Wire do core (o bus 2 e o Wire1).</summary>
    public class I2cDevice : IDisposable
    {
        static readonly HashSet<string> ligados = new HashSet<string>();
        readonly string wire;

        I2cDevice(I2cConnectionSettings s)
        {
            ConnectionSettings = s;
            wire = s.BusId >= 2 ? "Wire1" : "Wire";
            if (ligados.Add(wire)) Ponte.C(wire, "begin");
        }

        public static I2cDevice Create(I2cConnectionSettings settings) => new I2cDevice(settings);

        public I2cConnectionSettings ConnectionSettings { get; }
        int Endereco => ConnectionSettings.DeviceAddress;

        I2cTransferResult Escrever(byte[] dados)
        {
            Ponte.C(wire, "beginTransmission", Endereco);
            foreach (byte b in dados) Ponte.C(wire, "write", (int)b);
            long r = Ponte.I(wire, "endTransmission");
            return new I2cTransferResult(r == 0 ? I2cTransferStatus.FullTransfer : I2cTransferStatus.SlaveAddressNotAcknowledged, r == 0 ? dados.Length : 0);
        }

        I2cTransferResult Ler(byte[] destino)
        {
            long n = Ponte.I(wire, "requestFrom", Endereco, destino.Length);
            int i = 0;
            for (; i < destino.Length && Ponte.I(wire, "available") > 0; i++) destino[i] = (byte)Ponte.I(wire, "read");
            return new I2cTransferResult(n == 0 ? I2cTransferStatus.SlaveAddressNotAcknowledged
                                         : i == destino.Length ? I2cTransferStatus.FullTransfer : I2cTransferStatus.PartialTransfer, i);
        }

        public byte ReadByte()
        {
            var b = new byte[1];
            Ler(b);
            return b[0];
        }
        public I2cTransferResult WriteByte(byte value) => Escrever(new[] { value });

        public I2cTransferResult Write(byte[] buffer) => Escrever(buffer);
        public I2cTransferResult Write(SpanByte buffer) => Escrever(buffer.ToArray());
        public void Write(ReadOnlySpan<byte> buffer) => Escrever(buffer.ToArray());

        public I2cTransferResult Read(byte[] buffer) => Ler(buffer);
        public I2cTransferResult Read(SpanByte buffer)
        {
            var b = new byte[buffer.Length];
            var r = Ler(b);
            for (int i = 0; i < b.Length; i++) buffer[i] = b[i];
            return r;
        }
        public void Read(Span<byte> buffer)
        {
            var b = new byte[buffer.Length];
            Ler(b);
            b.CopyTo(buffer);
        }

        public I2cTransferResult WriteRead(byte[] writeBuffer, byte[] readBuffer)
        {
            var e = Escrever(writeBuffer);
            return e.Status != I2cTransferStatus.FullTransfer ? e : Ler(readBuffer);
        }
        public I2cTransferResult WriteRead(SpanByte writeBuffer, SpanByte readBuffer)
        {
            var e = Escrever(writeBuffer.ToArray());
            return e.Status != I2cTransferStatus.FullTransfer ? e : Read(readBuffer);
        }
        public void WriteRead(ReadOnlySpan<byte> writeBuffer, Span<byte> readBuffer)
        {
            Escrever(writeBuffer.ToArray());
            Read(readBuffer);
        }

        public void Dispose() { }
    }
}

namespace System.Device.Spi
{
    public enum SpiMode { Mode0, Mode1, Mode2, Mode3 }
    public enum DataFlow { MsbFirst, LsbFirst }

    public sealed class SpiConnectionSettings
    {
        public SpiConnectionSettings(int busId, int chipSelectLine = -1)
        {
            BusId = busId;
            ChipSelectLine = chipSelectLine;
        }
        public int BusId { get; }
        public int ChipSelectLine { get; set; }
        public int ClockFrequency { get; set; } = 500_000;
        public SpiMode Mode { get; set; }
        public int DataBitLength { get; set; } = 8;
        public DataFlow DataFlow { get; set; }
        public bool ChipSelectLineActiveState { get; set; }
    }

    /// <summary>O SPI do core, com o CS no pino da configuracao.</summary>
    public class SpiDevice : IDisposable
    {
        static bool ligado;

        SpiDevice(SpiConnectionSettings s)
        {
            ConnectionSettings = s;
            if (!ligado)
            {
                ligado = true;
                Ponte.C("SPI", "begin");
            }
            if (s.ChipSelectLine >= 0)
            {
                Ponte.C(null, "pinMode", s.ChipSelectLine, Ponte.Numero("OUTPUT", 1));
                Ponte.C(null, "digitalWrite", s.ChipSelectLine, s.ChipSelectLineActiveState ? 0 : 1);
            }
        }

        public static SpiDevice Create(SpiConnectionSettings settings) => new SpiDevice(settings);
        public SpiConnectionSettings ConnectionSettings { get; }

        void Selecionar(bool sim)
        {
            int cs = ConnectionSettings.ChipSelectLine;
            if (cs >= 0) Ponte.C(null, "digitalWrite", cs, sim == ConnectionSettings.ChipSelectLineActiveState ? 1 : 0);
        }

        byte[] Trocar(byte[] envio)
        {
            var volta = new byte[envio.Length];
            Selecionar(true);
            for (int i = 0; i < envio.Length; i++) volta[i] = (byte)Ponte.I("SPI", "transfer", (int)envio[i]);
            Selecionar(false);
            return volta;
        }

        public byte ReadByte() => Trocar(new byte[1])[0];
        public void WriteByte(byte value) => Trocar(new[] { value });
        public void Write(byte[] buffer) => Trocar(buffer);
        public void Write(SpanByte buffer) => Trocar(buffer.ToArray());
        public void Write(ReadOnlySpan<byte> buffer) => Trocar(buffer.ToArray());
        public void Read(byte[] buffer) => Trocar(new byte[buffer.Length]).CopyTo(buffer, 0);
        public void Read(SpanByte buffer)
        {
            var b = Trocar(new byte[buffer.Length]);
            for (int i = 0; i < b.Length; i++) buffer[i] = b[i];
        }
        public void Read(Span<byte> buffer) => Trocar(new byte[buffer.Length]).CopyTo(buffer);
        public void TransferFullDuplex(byte[] writeBuffer, byte[] readBuffer) => Trocar(writeBuffer).CopyTo(readBuffer, 0);
        public void TransferFullDuplex(SpanByte writeBuffer, SpanByte readBuffer)
        {
            var b = Trocar(writeBuffer.ToArray());
            for (int i = 0; i < b.Length && i < readBuffer.Length; i++) readBuffer[i] = b[i];
        }
        public void TransferFullDuplex(ReadOnlySpan<byte> writeBuffer, Span<byte> readBuffer) => Trocar(writeBuffer.ToArray()).CopyTo(readBuffer);
        public void Dispose() { }
    }
}

namespace System.IO.Ports
{
    public enum Parity { None, Odd, Even, Mark, Space }
    public enum StopBits { None, One, Two, OnePointFive }
    public enum Handshake { None, XOnXOff, RequestToSend, RequestToSendXOnXOff }
    public enum SerialData { Chars = 1, Eof = 2, WatchChar = 2 }

    public class SerialDataReceivedEventArgs : EventArgs
    {
        public SerialDataReceivedEventArgs(SerialData eventType) { EventType = eventType; }
        public SerialData EventType { get; }
    }
    public delegate void SerialDataReceivedEventHandler(object sender, SerialDataReceivedEventArgs e);

    /// <summary>
    /// A porta serial: o COM1 e a Serial (o monitor), o COM2 a Serial1 e o COM3
    /// a Serial2, nos pinos do Configuration.SetPinFunction (ESP32).
    /// </summary>
    public class SerialPort : IDisposable
    {
        readonly string serial;
        SerialDataReceivedEventHandler aoReceber;
        System.Threading.Thread vigia;

        public SerialPort(string portName) : this(portName, 9600) { }
        public SerialPort(string portName, int baudRate, Parity parity = Parity.None, int dataBits = 8, StopBits stopBits = StopBits.One)
        {
            PortName = portName;
            BaudRate = baudRate;
            Parity = parity;
            DataBits = dataBits;
            StopBits = stopBits;
            serial = portName?.ToUpperInvariant() switch
            {
                "COM1" => "Serial",
                "COM2" => "Serial1",
                "COM3" => "Serial2",
                _ => throw new ArgumentException($"a porta {portName} nao existe (COM1, COM2 ou COM3)"),
            };
        }

        public string PortName { get; }
        public int BaudRate { get; set; }
        public Parity Parity { get; set; }
        public int DataBits { get; set; }
        public StopBits StopBits { get; set; }
        public Handshake Handshake { get; set; }
        public int ReadTimeout { get; set; } = System.Threading.Timeout.Infinite;
        public int WriteTimeout { get; set; } = System.Threading.Timeout.Infinite;
        public string NewLine { get; set; } = "\n";
        public Encoding Encoding { get; set; } = Encoding.UTF8;
        public bool IsOpen { get; private set; }
        public int BytesToRead => IsOpen ? (int)Ponte.I(serial, "available") : 0;
        public int BytesToWrite => 0;
        public int ReceivedBytesThreshold { get; set; } = 1;

        public void Open()
        {
            int rx = -1, tx = -1;
            if (serial == "Serial1")
            {
                rx = nanoFramework.Hardware.Esp32.Configuration.GetFunctionPin(nanoFramework.Hardware.Esp32.DeviceFunction.COM2_RX);
                tx = nanoFramework.Hardware.Esp32.Configuration.GetFunctionPin(nanoFramework.Hardware.Esp32.DeviceFunction.COM2_TX);
            }
            else if (serial == "Serial2")
            {
                rx = nanoFramework.Hardware.Esp32.Configuration.GetFunctionPin(nanoFramework.Hardware.Esp32.DeviceFunction.COM3_RX);
                tx = nanoFramework.Hardware.Esp32.Configuration.GetFunctionPin(nanoFramework.Hardware.Esp32.DeviceFunction.COM3_TX);
            }
            if (rx >= 0 || tx >= 0) Ponte.C(serial, "begin", BaudRate, Ponte.Numero("SERIAL_8N1", 6), rx, tx);
            else Ponte.C(serial, "begin", BaudRate);
            IsOpen = true;
            if (aoReceber != null) Vigiar();
        }

        public void Close()
        {
            IsOpen = false;
            Ponte.C(serial, "end");
        }

        public void Dispose()
        {
            if (IsOpen) Close();
        }

        void Exigir()
        {
            if (!IsOpen) throw new InvalidOperationException($"a porta {PortName} esta fechada (falta o Open())");
        }

        public void Write(string text)
        {
            Exigir();
            Ponte.C(serial, "print", text);
        }
        public void WriteLine(string text) => Write(text + NewLine);
        public void Write(byte[] buffer, int offset, int count)
        {
            Exigir();
            for (int i = 0; i < count; i++) Ponte.C(serial, "write", (int)buffer[offset + i]);
        }
        public void Write(char[] buffer, int offset, int count) => Write(new string(buffer, offset, count));

        int LerUm()
        {
            var relogio = System.Diagnostics.Stopwatch.StartNew();
            while (true)
            {
                long c = Ponte.I(serial, "read");
                if (c >= 0) return (int)c;
                if (ReadTimeout >= 0 && relogio.ElapsedMilliseconds >= ReadTimeout)
                    throw new TimeoutException($"nada chegou na {PortName} em {ReadTimeout} ms");
                Ponte.Dormir(1);
            }
        }

        public int ReadByte()
        {
            Exigir();
            return LerUm();
        }
        public int ReadChar() => ReadByte();

        public int Read(byte[] buffer, int offset, int count)
        {
            Exigir();
            int n = 0;
            buffer[offset + n++] = (byte)LerUm();
            while (n < count && Ponte.I(serial, "available") > 0) buffer[offset + n++] = (byte)Ponte.I(serial, "read");
            return n;
        }

        public string ReadExisting()
        {
            Exigir();
            var sb = new StringBuilder();
            while (Ponte.I(serial, "available") > 0) sb.Append((char)Ponte.I(serial, "read"));
            return sb.ToString();
        }

        public string ReadTo(string value)
        {
            Exigir();
            var sb = new StringBuilder();
            while (true)
            {
                sb.Append((char)LerUm());
                if (sb.Length >= value.Length && sb.ToString(sb.Length - value.Length, value.Length) == value)
                    return sb.ToString(0, sb.Length - value.Length);
            }
        }

        public string ReadLine() => ReadTo(NewLine).TrimEnd('\r');

        /// <summary>O DataReceived: uma thread olha a porta e avisa quando chega alguma coisa.</summary>
        public event SerialDataReceivedEventHandler DataReceived
        {
            add
            {
                aoReceber += value;
                if (IsOpen) Vigiar();
            }
            remove { aoReceber -= value; }
        }

        void Vigiar()
        {
            if (vigia != null) return;
            vigia = Ponte.NovaThread(() =>
            {
                while (IsOpen && !Ponte.Saindo)
                {
                    if (aoReceber != null && BytesToRead >= Math.Max(1, ReceivedBytesThreshold))
                        aoReceber(this, new SerialDataReceivedEventArgs(SerialData.Chars));
                    Ponte.Dormir(5);
                }
                vigia = null;
            });
            vigia.Start();
        }
    }
}
