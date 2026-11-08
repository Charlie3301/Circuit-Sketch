// Telas.cs -- o LCD de caracteres (Iot.Device.CharacterLcd) e o OLED
// (Iot.Device.Ssd13xx) do nanoFramework.IoT.Device, adaptados dos fontes de
// verdade (licenca MIT, (c) .NET Foundation e colaboradores). Os drivers mandam
// os mesmos bytes que mandariam na placa: o LCD pelo PCF8574 do modulo I2C (ou
// pelos pinos, no paralelo) e o OLED pelos comandos do SSD1306. O simulador
// decodifica os bytes no vidro da peca do circuito.
using System;
using System.Device.Gpio;
using System.Device.I2c;
using System.Drawing;
using CircuitSketch;
using Iot.Device.Ssd13xx.Commands;
using Iot.Device.Ssd13xx.Commands.Ssd1306Commands;

namespace System
{
    /// <summary>O SpanChar do nanoFramework: um pedaco de um char[].</summary>
    public readonly struct SpanChar
    {
        readonly char[] vetor;
        readonly int inicio;

        public SpanChar(char[] array) : this(array, 0, array?.Length ?? 0) { }
        public SpanChar(char[] array, int start, int length)
        {
            vetor = array ?? Array.Empty<char>();
            inicio = start;
            Length = length;
        }

        public int Length { get; }
        public bool IsEmpty => Length == 0;
        public static SpanChar Empty => new SpanChar(Array.Empty<char>());

        public char this[int i]
        {
            get => vetor[inicio + i];
            set => vetor[inicio + i] = value;
        }

        public SpanChar Slice(int start) => new SpanChar(vetor, inicio + start, Length - start);
        public SpanChar Slice(int start, int length) => new SpanChar(vetor, inicio + start, length);
        public char[] ToArray()
        {
            var r = new char[Length];
            Array.Copy(vetor, inicio, r, 0, Length);
            return r;
        }
        public void CopyTo(SpanChar destino)
        {
            for (int i = 0; i < Length; i++) destino[i] = this[i];
        }
        public override string ToString() => new string(vetor, inicio, Length);

        public static implicit operator SpanChar(char[] array) => new SpanChar(array);
    }
}

namespace Iot.Device.CharacterLcd
{
    [Flags]
    internal enum DisplayEntryMode : byte { DisplayShift = 0b_0001, Increment = 0b_0010, Command = 0b_0100 }

    [Flags]
    internal enum DisplayControl : byte { BlinkOn = 0b_0001, CursorOn = 0b_0010, DisplayOn = 0b_0100, Command = 0b_1000 }

    [Flags]
    internal enum DisplayShift : byte { Right = 0b_0000_0100, Display = 0b_0000_1000, Command = 0b_0001_0000 }

    [Flags]
    internal enum DisplayFunction : byte
    {
        ExtendedInstructionSet = 0b_0000_0001,
        Font5x10 = 0b_0000_0100,
        TwoLine = 0b_0000_1000,
        EightBit = 0b_0001_0000,
        Command = 0b_0010_0000,
    }

    public interface ICharacterLcd : IDisposable
    {
        bool BacklightOn { get; set; }
        bool DisplayOn { get; set; }
        bool UnderlineCursorVisible { get; set; }
        bool BlinkingCursorVisible { get; set; }
        Size Size { get; }
        int NumberOfCustomCharactersSupported { get; }
        void Clear();
        void CreateCustomCharacter(int location, SpanByte characterMap);
        void CreateCustomCharacter(int location, byte[] characterMap);
        void SetCursorPosition(int left, int top);
        void Write(string text);
        void Write(SpanChar text);
        void Write(char[] text);
    }

    public static class CharacterLcdExtensions
    {
        public static void CreateCustomCharacter(this ICharacterLcd self, byte location, byte b0, byte b1, byte b2, byte b3, byte b4, byte b5, byte b6, byte b7)
            => self.CreateCustomCharacter(location, new SpanByte(new[] { b0, b1, b2, b3, b4, b5, b6, b7 }));
    }

    /// <summary>Como o driver fala com o HD44780: pelo modulo I2C (4 bits), por I2C de 8 bits ou pelos pinos.</summary>
    public abstract partial class LcdInterface : IDisposable
    {
        bool descartado;

        public static LcdInterface CreateGpio(int registerSelectPin, int enablePin, int[] dataPins, int backlightPin = -1, float backlightBrightness = 1.0f,
                                              int readWritePin = -1, GpioController controller = null, bool shouldDispose = true)
            => new Gpio(dataPins?.Length == 8);

        public static LcdInterface CreateI2c(I2cDevice device, bool uses8Bit = true) => uses8Bit ? new I2c(device) : new I2c4Bit(device);

        public abstract void SendData(byte value);
        public abstract void SendCommand(byte command);
        public abstract void SendData(SpanByte values);
        public abstract void SendData(SpanChar values);
        public abstract void SendCommands(SpanByte values);

        public virtual void SendCommandAndWait(byte command)
        {
            SendCommand(command);
            Ponte.Dormir(1);
        }

        public abstract bool EightBitMode { get; }
        public double WaitMultiplier { get; set; } = 1.0;

        public virtual void WaitForNotBusy(int microseconds) => Ponte.C(null, "delayMicroseconds", (long)(microseconds * WaitMultiplier));

        public abstract bool BacklightOn { get; set; }

        protected virtual void Dispose(bool disposing) { }

        public void Dispose()
        {
            if (descartado) return;
            Dispose(true);
            descartado = true;
        }

        /// <summary>O modulo I2C do LCD (o PCF8574): o meio byte nos P4 a P7, o E no P2, o RS no P0 e a luz no P3.</summary>
        private class I2c4Bit : LcdInterface
        {
            const byte ENABLE = 0b0000_0100;
            const byte REGISTERSELECT = 0b0000_0001;
            const byte LCD_BACKLIGHT = 0x08;
            readonly I2cDevice dispositivo;
            bool luz;

            public I2c4Bit(I2cDevice i2cDevice)
            {
                dispositivo = i2cDevice ?? throw new ArgumentNullException(nameof(i2cDevice));
                luz = true;
                SendCommandAndWait(0x3);
                SendCommandAndWait(0x3);
                SendCommandAndWait(0x3);
                SendCommandAndWait(0x2);
            }

            public override bool EightBitMode => false;

            public override bool BacklightOn
            {
                get => luz;
                set
                {
                    luz = value;
                    SendCommandAndWait(0);
                }
            }

            byte BacklightFlag => luz ? LCD_BACKLIGHT : (byte)0;

            public override void SendCommand(byte command)
            {
                Write4Bits((byte)(command & 0xF0));
                Write4Bits((byte)((command << 4) & 0xF0));
            }

            void Write4Bits(byte command)
            {
                dispositivo.WriteByte((byte)(command | ENABLE | BacklightFlag));
                dispositivo.WriteByte((byte)((command & ~ENABLE) | BacklightFlag));
            }

            public override void SendCommands(SpanByte commands)
            {
                for (int i = 0; i < commands.Length; i++) SendCommand(commands[i]);
            }

            public override void SendData(byte value)
            {
                Write4Bits((byte)(REGISTERSELECT | (value & 0xF0)));
                Write4Bits((byte)(REGISTERSELECT | ((value << 4) & 0xF0)));
            }

            public override void SendData(SpanByte values)
            {
                for (int i = 0; i < values.Length; i++) SendData(values[i]);
            }

            public override void SendData(SpanChar values)
            {
                for (int i = 0; i < values.Length; i++) SendData((byte)values[i]);
            }
        }

        /// <summary>O LCD com controlador I2C proprio (o AIP31068 do Grove): o byte de controle 0x80 e o comando, 0x40 o dado.</summary>
        private class I2c : LcdInterface
        {
            readonly I2cDevice dispositivo;

            public I2c(I2cDevice device) => dispositivo = device ?? throw new ArgumentNullException(nameof(device));

            public override bool EightBitMode => true;
            public override bool BacklightOn
            {
                get => false;
                set { }
            }

            public override void SendCommand(byte command) => dispositivo.Write(new byte[] { 0x80, command });
            public override void SendCommands(SpanByte values)
            {
                var b = new byte[values.Length + 1];
                for (int i = 0; i < values.Length; i++) b[i + 1] = values[i];
                dispositivo.Write(b);
            }
            public override void SendData(byte value) => dispositivo.Write(new byte[] { 0x40, value });
            public override void SendData(SpanByte values)
            {
                var b = new byte[values.Length + 1];
                b[0] = 0x40;
                for (int i = 0; i < values.Length; i++) b[i + 1] = values[i];
                dispositivo.Write(b);
            }
            public override void SendData(SpanChar values)
            {
                var b = new byte[values.Length + 1];
                b[0] = 0x40;
                for (int i = 0; i < values.Length; i++) b[i + 1] = (byte)values[i];
                dispositivo.Write(b);
            }
        }

        /// <summary>O LCD ligado direto nos pinos: cada byte vai para o HD44780 do circuito.</summary>
        private class Gpio : LcdInterface
        {
            readonly bool oitoBits;
            bool luz = true;

            public Gpio(bool oitoBits) => this.oitoBits = oitoBits;

            internal int Colunas = 16, Linhas = 2;

            public override bool EightBitMode => oitoBits;
            public override bool BacklightOn
            {
                get => luz;
                set => luz = value;
            }

            void Mandar(bool dado, byte v) => Ponte.C(null, "__lcd_hd44780", dado ? 1L : 0L, (long)v, (long)Colunas, (long)Linhas);

            public override void SendCommand(byte command) => Mandar(false, command);
            public override void SendCommands(SpanByte values)
            {
                for (int i = 0; i < values.Length; i++) Mandar(false, values[i]);
            }
            public override void SendData(byte value) => Mandar(true, value);
            public override void SendData(SpanByte values)
            {
                for (int i = 0; i < values.Length; i++) Mandar(true, values[i]);
            }
            public override void SendData(SpanChar values)
            {
                for (int i = 0; i < values.Length; i++) Mandar(true, (byte)values[i]);
            }
        }

        /// <summary>O tamanho do vidro, para o paralelo dizer ao HD44780 do simulador.</summary>
        internal void Dimensionar(Size tamanho)
        {
            if (this is Gpio g)
            {
                g.Colunas = tamanho.Width;
                g.Linhas = tamanho.Height;
            }
        }
    }

    public class Hd44780 : ICharacterLcd, IDisposable
    {
        protected const byte ClearDisplayCommand = 0b_0001;
        protected const byte ReturnHomeCommand = 0b_0010;
        protected const byte SetCGRamAddressCommand = 0b_0100_0000;
        protected const byte SetDDRamAddressCommand = 0b_1000_0000;

        internal DisplayFunction _displayFunction = DisplayFunction.Command;
        internal DisplayControl _displayControl = DisplayControl.Command;
        internal DisplayEntryMode _displayMode = DisplayEntryMode.Command;

        protected readonly byte[] _rowOffsets;
        protected readonly LcdInterface _lcdInterface;

        public Hd44780(Size size, LcdInterface lcdInterface)
        {
            Size = size;
            _lcdInterface = lcdInterface;
            _lcdInterface.Dimensionar(size);
            AutoShift = false;
            if (_lcdInterface.EightBitMode) _displayFunction |= DisplayFunction.EightBit;
            Initialize(size.Height);
            _rowOffsets = InitializeRowOffsets(size.Height);
        }

        public Size Size { get; }
        public virtual int NumberOfCustomCharactersSupported => 8;

        private void Initialize(int rows)
        {
            if (GetTwoLineMode(rows)) _displayFunction |= DisplayFunction.TwoLine;
            _displayControl |= DisplayControl.DisplayOn;
            _displayMode |= DisplayEntryMode.Increment;
            SendCommandAndWait((byte)_displayFunction);
            SendCommandAndWait((byte)_displayControl);
            SendCommandAndWait((byte)_displayMode);
            SendCommandAndWait(ClearDisplayCommand);
        }

        public virtual bool BacklightOn
        {
            get => _lcdInterface.BacklightOn;
            set => _lcdInterface.BacklightOn = value;
        }

        protected void SendData(byte value) => _lcdInterface.SendData(value);
        protected void SendCommand(byte command) => _lcdInterface.SendCommand(command);
        protected void SendCommandAndWait(byte command) => _lcdInterface.SendCommandAndWait(command);
        protected void SendData(SpanByte values) => _lcdInterface.SendData(values);
        protected void SendData(SpanChar values) => _lcdInterface.SendData(values);
        protected void SendCommands(SpanByte commands) => _lcdInterface.SendCommands(commands);

        protected virtual bool GetTwoLineMode(int rows) => rows > 1;

        protected virtual byte[] InitializeRowOffsets(int rows) => rows switch
        {
            1 => new byte[1],
            2 => new byte[] { 0, 64 },
            4 => new byte[] { 0, 64, 20, 84 },
            _ => throw new ArgumentOutOfRangeException(nameof(rows)),
        };

        protected void WaitForNotBusy(int microseconds) => _lcdInterface.WaitForNotBusy(microseconds);

        public void Clear()
        {
            SendCommand(ClearDisplayCommand);
            WaitForNotBusy(2000);
        }

        public void Home()
        {
            SendCommand(ReturnHomeCommand);
            WaitForNotBusy(1520);
        }

        public void SetCursorPosition(int left, int top)
        {
            int rows = _rowOffsets.Length;
            if (top < 0 || top >= rows) throw new ArgumentOutOfRangeException(nameof(top));
            int newAddress = left + _rowOffsets[top];
            if (left < 0 || (rows == 1 && newAddress >= 80) || (rows > 1 && newAddress >= 104)) throw new ArgumentOutOfRangeException(nameof(left));
            SendCommand((byte)(SetDDRamAddressCommand | newAddress));
        }

        public bool DisplayOn
        {
            get => (_displayControl & DisplayControl.DisplayOn) > 0;
            set => SendCommandAndWait((byte)(value ? _displayControl |= DisplayControl.DisplayOn : _displayControl &= ~DisplayControl.DisplayOn));
        }

        public bool UnderlineCursorVisible
        {
            get => (_displayControl & DisplayControl.CursorOn) > 0;
            set => SendCommandAndWait((byte)(value ? _displayControl |= DisplayControl.CursorOn : _displayControl &= ~DisplayControl.CursorOn));
        }

        public bool BlinkingCursorVisible
        {
            get => (_displayControl & DisplayControl.BlinkOn) > 0;
            set => SendCommandAndWait((byte)(value ? _displayControl |= DisplayControl.BlinkOn : _displayControl &= ~DisplayControl.BlinkOn));
        }

        public bool AutoShift
        {
            get => (_displayMode & DisplayEntryMode.DisplayShift) > 0;
            set => SendCommandAndWait((byte)(value ? _displayMode |= DisplayEntryMode.DisplayShift : _displayMode &= ~DisplayEntryMode.DisplayShift));
        }

        public bool Increment
        {
            get => (_displayMode & DisplayEntryMode.Increment) > 0;
            set => SendCommandAndWait((byte)(value ? _displayMode |= DisplayEntryMode.Increment : _displayMode &= ~DisplayEntryMode.Increment));
        }

        public void ShiftDisplayLeft() => SendCommand((byte)(DisplayShift.Command | DisplayShift.Display));
        public void ShiftDisplayRight() => SendCommand((byte)(DisplayShift.Command | DisplayShift.Display | DisplayShift.Right));
        public void ShiftCursorLeft() => SendCommand((byte)DisplayShift.Command);
        public void ShiftCursorRight() => SendCommand((byte)(DisplayShift.Command | DisplayShift.Right));

        public void CreateCustomCharacter(int location, SpanByte characterMap)
        {
            if (location >= NumberOfCustomCharactersSupported) throw new ArgumentOutOfRangeException(nameof(location));
            if (characterMap.Length != 8) throw new ArgumentException("The character map must be exactly 8 bytes long", nameof(characterMap));
            SendCommand((byte)(SetCGRamAddressCommand | (location << 3)));
            SendData(characterMap);
        }

        public void CreateCustomCharacter(int location, byte[] characterMap) => CreateCustomCharacter(location, new SpanByte(characterMap));

        public void Write(string text)
        {
            var buffer = new byte[text.Length];
            for (int i = 0; i < text.Length; ++i) buffer[i] = (byte)text[i];
            SendData(new SpanByte(buffer));
        }

        public void Write(SpanChar text) => SendData(text);
        public void Write(char[] text) => SendData(new SpanChar(text));

        public virtual void Dispose() => _lcdInterface?.Dispose();
    }

    public class Lcd1602 : Hd44780
    {
        public Lcd1602(int registerSelectPin, int enablePin, int[] dataPins, int backlightPin = -1, float backlightBrightness = 1.0f, int readWritePin = -1,
                       GpioController controller = null, bool shouldDispose = true)
            : base(new Size(16, 2), LcdInterface.CreateGpio(registerSelectPin, enablePin, dataPins, backlightPin, backlightBrightness, readWritePin, controller, shouldDispose)) { }
        public Lcd1602(I2cDevice device, bool uses8Bit = true) : base(new Size(16, 2), LcdInterface.CreateI2c(device, uses8Bit)) { }
        public Lcd1602(LcdInterface lcdInterface) : base(new Size(16, 2), lcdInterface) { }
    }

    public class Lcd2004 : Hd44780
    {
        public Lcd2004(int registerSelectPin, int enablePin, int[] dataPins, int backlightPin = -1, float backlightBrightness = 1.0f, int readWritePin = -1,
                       GpioController controller = null, bool shouldDispose = true)
            : base(new Size(20, 4), LcdInterface.CreateGpio(registerSelectPin, enablePin, dataPins, backlightPin, backlightBrightness, readWritePin, controller, shouldDispose)) { }
        public Lcd2004(I2cDevice device, bool uses8Bit = true) : base(new Size(20, 4), LcdInterface.CreateI2c(device, uses8Bit)) { }
        public Lcd2004(LcdInterface lcdInterface) : base(new Size(20, 4), lcdInterface) { }
    }
}

namespace Iot.Device.Ssd13xx
{
    public enum DisplayOrientation : int { Portrait = 1, Landscape = 2, Portrait180 = 3, Landscape180 = 4 }

    public abstract class IFont
    {
        public virtual byte Width { get; private set; }
        public virtual byte Height { get; private set; }
        public abstract byte[] this[char character] { get; }
    }

    /// <summary>A base do SSD1306 e do SH1106: o quadro na memoria, o texto pela fonte e o Display() pagina por pagina.</summary>
    public abstract class Ssd13xx : IDisposable
    {
        readonly byte[] oled128x64Init =
        {
            0x00, 0xae, 0xd5, 0x80, 0xa8, 0x3f, 0xd3, 0x00, 0x40 | 0x0, 0x8d, 0x14, 0x20, 0x00, 0xa0 | 0x1, 0xc8, 0xda, 0x12,
            0x81, 0xcf, 0xd9, 0xf1, 0xdb, 0x40, 0xa4, 0xa6, 0xaf,
        };
        readonly byte[] oled64x128Init =
        {
            0x00, 0xd5, 0xae, 0xd5, 0x80, 0xa8, 0x3f, 0xd3, 0x1f, 0x40 | 0x0, 0x8d, 0x14, 0x20, 0x00, 0xa0 | 0x01, 0xc8, 0xda, 0x12,
            0x81, 0xcf, 0xd9, 0xf1, 0xdb, 0x40, 0xa4, 0xa6, 0xaf,
        };
        readonly byte[] oled128x32Init =
        {
            0x00, 0xae, 0xd5, 0x80, 0xa8, 0x1f, 0xd3, 0x00, 0x40 | 0x0, 0x8d, 0x14, 0x20, 0x00, 0xa0 | 0x1, 0xc8, 0xda, 0x02,
            0x81, 0x8f, 0xd9, 0xf1, 0xdb, 0x40, 0xa4, 0xa6, 0xaf,
        };
        readonly byte[] oled96x16Init =
        {
            0x00, 0xae, 0xd5, 0x80, 0xa8, 0x1f, 0xd3, 0x00, 0x40 | 0x0, 0x8d, 0x14, 0x20, 0x00, 0xa0 | 0x1, 0xc8, 0xda, 0x02,
            0x81, 0xaf, 0xd9, 0xf1, 0xdb, 0x40, 0xa4, 0xa6, 0xaf,
        };

        // O quadro (uma pagina de 8 linhas por fileira de bytes), mais 4 bytes como no original.
        // Os comandos e os dados vao num buffer proprio: o original montava o comando dentro do
        // quadro e escrevia o quadro inteiro como comando.
        byte[] _genericBuffer;
        byte[] _pageData;
        byte[] envio = new byte[64];

        protected I2cDevice _i2cDevice { get; set; }

        public DisplayOrientation Orientation { get; set; } = DisplayOrientation.Landscape;
        public int Width { get; set; }
        public int Height { get; set; }
        public byte Pages { get; set; }
        public IFont Font { get; set; }

        GpioController _gpioController;
        readonly int _resetPin;
        readonly bool _shouldDispose;
        bool _disposed;

        public Ssd13xx(I2cDevice i2cDevice, DisplayResolution resolution = DisplayResolution.OLED128x64, DisplayOrientation orientation = DisplayOrientation.Landscape,
                       int resetPin = -1, GpioController gpio = null, bool shouldDispose = true)
        {
            _resetPin = resetPin;
            if (resetPin >= 0)
            {
                _gpioController = gpio ?? new GpioController();
                Reset();
            }
            _shouldDispose = shouldDispose || gpio == null;
            _i2cDevice = i2cDevice ?? throw new ArgumentNullException(nameof(i2cDevice));
            Orientation = orientation;
            switch (resolution)
            {
                case DisplayResolution.OLED128x64:
                    Width = 128;
                    Height = 64;
                    _i2cDevice.Write(oled128x64Init);
                    break;
                case DisplayResolution.OLED64x128:
                    Width = 64;
                    Height = 128;
                    _i2cDevice.Write(oled64x128Init);
                    break;
                case DisplayResolution.OLED128x32:
                    Width = 128;
                    Height = 32;
                    _i2cDevice.Write(oled128x32Init);
                    break;
                case DisplayResolution.OLED96x16:
                    Width = 96;
                    Height = 16;
                    _i2cDevice.Write(oled96x16Init);
                    break;
            }
            Pages = (byte)(Height / 8);
            _genericBuffer = new byte[(Pages * Width) + 4];
            _pageData = new byte[Width + 1];
        }

        internal static bool InRange(uint value, uint start, uint end) => (value - start) <= (end - start);

        public abstract void SendCommand(ISharedCommand command);

        public virtual void SendData(SpanByte data)
        {
            if (data.IsEmpty) throw new ArgumentNullException(nameof(data));
            SpanByte writeBuffer = SliceGenericBuffer(data.Length + 1);
            writeBuffer[0] = 0x40;
            data.CopyTo(writeBuffer.Slice(1));
            _i2cDevice.Write(writeBuffer);
        }

        protected SpanByte SliceGenericBuffer(int length) => SliceGenericBuffer(0, length);

        protected SpanByte SliceGenericBuffer(int start, int length)
        {
            if (envio.Length < start + length) Array.Resize(ref envio, Math.Max(envio.Length * 2, start + length));
            return new SpanByte(envio, start, length);
        }

        public void DrawDirectAligned(int x, int y, int width, int height, byte[] buffer)
        {
            if ((y % 8) != 0) throw new ArgumentException("Y must be aligned to byte boundary.");
            if ((height % 8) != 0) throw new ArgumentException("Height must be aligned to byte boundary.");
            var dataHeightInBytes = height / 8;
            if ((dataHeightInBytes * width) != buffer.Length) throw new ArgumentException("Width and height do not match the bitmap size.");
            var genericBufferIdx = ((y / 8) * Width) + x;
            var srcIdx = 0;
            for (int i = 0; i < dataHeightInBytes; i++)
            {
                Array.Copy(buffer, srcIdx, _genericBuffer, genericBufferIdx, width);
                srcIdx += width;
                genericBufferIdx += Width;
            }
        }

        public void ClearDirectAligned(int x, int y, int width, int height)
        {
            if ((y % 8) != 0) throw new ArgumentException("Y must be aligned to byte boundary.");
            if ((height % 8) != 0) throw new ArgumentException("Height must be aligned to byte boundary.");
            var dataHeightInBytes = height / 8;
            var genericBufferIdx = ((y / 8) * Width) + x;
            for (int i = 0; i < dataHeightInBytes; i++)
            {
                Array.Clear(_genericBuffer, genericBufferIdx, width);
                genericBufferIdx += Width;
            }
        }

        static void Swap(ref int a, ref int b) => (a, b) = (b, a);

        public void DrawPixel(int x, int y, bool inverted = true)
        {
            var defaultOrientation = Width < Height ? DisplayOrientation.Portrait : DisplayOrientation.Landscape;
            if (defaultOrientation != Orientation)
            {
                switch (Math.Abs((int)defaultOrientation - (int)Orientation) * 90)
                {
                    case 90:
                        Swap(ref x, ref y);
                        x = Width - x - 1;
                        break;
                    case 180:
                        x = Width - x - 1;
                        y = Height - y - 1;
                        break;
                    case 270:
                        Swap(ref x, ref y);
                        y = Height - y - 1;
                        break;
                }
            }
            if (x >= Width || x < 0 || y >= Height || y < 0) return;
            int idx = x + ((y / 8) * Width);
            if (inverted) _genericBuffer[idx] |= (byte)(1 << (y & 7));
            else _genericBuffer[idx] &= (byte)~(1 << (y & 7));
        }

        public void DrawHorizontalLine(int x0, int y0, int length, bool inverted = true)
        {
            for (var x = x0; (x - x0) < length; x++) DrawPixel(x, y0, inverted);
        }

        public void DrawVerticalLine(int x0, int y0, int length, bool inverted = true)
        {
            for (var y = y0; (y - y0) < length; y++) DrawPixel(x0, y, inverted);
        }

        public void DrawFilledRectangle(int x0, int y0, int width, int height, bool inverted = true)
        {
            for (int i = 0; i <= height; i++) DrawHorizontalLine(x0, y0 + i, width, inverted);
        }

        public void DrawBitmap(int x, int y, int width, int height, byte[] bitmap, byte size = 1)
        {
            if ((width * height) != bitmap.Length) throw new ArgumentException("Width and height do not match the bitmap size.");
            for (var yO = 0; yO < height; yO++)
            {
                for (var xA = 0; xA < width; xA++)
                {
                    byte b = bitmap[(yO * width) + xA];
                    byte mask = 0x01;
                    for (var pixel = 0; pixel < 8; pixel++)
                    {
                        if (size == 1) DrawPixel(x + (8 * xA) + pixel, y + yO, (b & mask) > 0);
                        else DrawFilledRectangle((x + (8 * xA) + pixel) * size, ((y / size) + yO) * size, size, size, (b & mask) > 0);
                        mask <<= 1;
                    }
                }
            }
        }

        public void DrawString(int x, int y, string str, byte size = 1, bool center = false)
        {
            int w = Width;
            if (Width < Height && (Orientation == DisplayOrientation.Landscape || Orientation == DisplayOrientation.Landscape180)) w = Height;
            if (center && str != null)
            {
                int padSize = (((w / size) / Font.Width) - str.Length) / 2;
                if (padSize > 0) str = str.PadLeft(str.Length + padSize);
            }
            byte[] bitMap = Font.Width > 8 ? GetDoubleTextBytes(str) : GetTextBytes(str);
            DrawBitmap(x, y, bitMap.Length / Font.Height, Font.Height, bitMap, size);
        }

        public void Write(int x, int y, string str, byte size = 1, bool center = false) => DrawString(x * Font.Width, y * Font.Height, str, size, center);

        byte[] GetTextBytes(string text)
        {
            if (Font.Width != 8) throw new Exception("Font width must be 8");
            var bitMap = new byte[text.Length * Font.Height * Font.Width / 8];
            for (int i = 0; i < text.Length; i++)
            {
                var characterMap = Font[text[i]];
                for (int segment = 0; segment < Font.Height; segment++)
                    bitMap[i + (segment * text.Length)] = segment < characterMap.Length ? characterMap[segment] : byte.MinValue;
            }
            return bitMap;
        }

        byte[] GetDoubleTextBytes(string text)
        {
            if (Font.Width != 16) throw new Exception("Double-byte characters font width must be 16");
            var charCount = text.Length * 2;
            var bitMap = new byte[charCount * Font.Height * Font.Width / 16];
            byte[] characterMap = null;
            for (int i = 0; i < charCount; i++)
            {
                var even = i % 2 == 0;
                if (even) characterMap = Font[text[i / 2]];
                var lista = new System.Collections.Generic.List<byte>();
                for (int idx = even ? 0 : 1; idx < characterMap.Length; idx += 2) lista.Add(characterMap[idx]);
                for (int segment = 0; segment < Font.Height; segment++)
                    bitMap[i + (segment * charCount)] = segment < lista.Count ? lista[segment] : byte.MinValue;
            }
            return bitMap;
        }

        public void Display()
        {
            for (byte i = 0; i < Pages; i++)
            {
                _pageCmd[1] = (byte)(PageAddress.Page0 + i);
                _i2cDevice.Write(_pageCmd);
                _pageData[0] = 0x40;
                Array.Copy(_genericBuffer, i * Width, _pageData, 1, Width);
                _i2cDevice.Write(_pageData);
            }
        }

        public void ClearScreen()
        {
            Array.Clear(_genericBuffer, 0, _genericBuffer.Length);
            Display();
        }

        public enum DisplayResolution { OLED128x64, OLED128x32, OLED96x16, OLED64x128 }

        internal void SetPageCmd(byte[] pageCmd) => _pageCmd = pageCmd;

        byte[] _pageCmd = { 0x00, 0xB0, 0x00, 0x10 };

        void Reset()
        {
            GpioPin rstPin = _gpioController.OpenPin(_resetPin, PinMode.Output);
            rstPin.Write(PinValue.High);
            Ponte.Dormir(1);
            rstPin.Write(PinValue.Low);
            Ponte.Dormir(10);
            rstPin.Write(PinValue.High);
            Ponte.Dormir(1);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed || !disposing) return;
            if (_resetPin >= 0)
            {
                _gpioController.ClosePin(_resetPin);
                if (_shouldDispose)
                {
                    _gpioController.Dispose();
                    _gpioController = null;
                }
            }
            _i2cDevice?.Dispose();
            _i2cDevice = null;
            _disposed = true;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }

    public class Ssd1306 : Ssd13xx
    {
        public const byte DefaultI2cAddress = 0x3C;
        public const byte SecondaryI2cAddress = 0x3D;

        public Ssd1306(I2cDevice i2cDevice, int resetPin = -1) : base(i2cDevice, DisplayResolution.OLED128x64, DisplayOrientation.Landscape, resetPin) { }
        public Ssd1306(I2cDevice i2cDevice, DisplayResolution res, DisplayOrientation orientation = DisplayOrientation.Landscape, int resetPin = -1,
                       GpioController gpio = null, bool shouldDispose = true)
            : base(i2cDevice, res, orientation, resetPin, gpio, shouldDispose) { }

        public void SendCommand(ISsd1306Command command) => Mandar(command);
        public override void SendCommand(ISharedCommand command) => Mandar(command);

        void Mandar(ICommand command)
        {
            byte[] commandBytes = command?.GetBytes();
            if (commandBytes == null || commandBytes.Length == 0) throw new ArgumentNullException(nameof(command), "Argument is either null or there were no bytes to send.");
            SpanByte writeBuffer = SliceGenericBuffer(commandBytes.Length + 1);
            writeBuffer[0] = 0x00;
            new SpanByte(commandBytes).CopyTo(writeBuffer.Slice(1));
            _i2cDevice.Write(writeBuffer);
        }
    }

    /// <summary>O SH1106: a memoria tem 132 colunas e o vidro comeca na coluna 2.</summary>
    public class Sh1106 : Ssd1306
    {
        public Sh1106(I2cDevice i2cDevice, int resetPin = -1) : base(i2cDevice, resetPin) => SetPageCmd(new byte[] { 0x00, 0xB0, 0x02, 0x10 });
        public Sh1106(I2cDevice i2cDevice, DisplayResolution res, DisplayOrientation orientation = DisplayOrientation.Landscape, int resetPin = -1,
                      GpioController gpio = null, bool shouldDispose = true)
            : base(i2cDevice, res, orientation, resetPin, gpio, shouldDispose) => SetPageCmd(new byte[] { 0x00, 0xB0, 0x02, 0x10 });
    }
}

namespace Iot.Device.Ssd13xx.Commands
{
    public interface ICommand
    {
        byte Id { get; }
        byte[] GetBytes();
    }

    public interface ISharedCommand : ICommand { }
    public interface ISsd1306Command : ICommand { }

    public class ActivateScroll : ISharedCommand
    {
        public byte Id => 0x2F;
        public byte[] GetBytes() => new[] { Id };
    }

    public class DeactivateScroll : ISharedCommand
    {
        public byte Id => 0x2E;
        public byte[] GetBytes() => new[] { Id };
    }

    public class SetContrastControlForBank0 : ISharedCommand
    {
        public SetContrastControlForBank0(byte contrastSetting = 0x7F) => ContrastSetting = contrastSetting;
        public byte Id => 0x81;
        public byte ContrastSetting { get; }
        public byte[] GetBytes() => new[] { Id, ContrastSetting };
    }

    public class SetDisplayOff : ISharedCommand
    {
        public byte Id => 0xAE;
        public byte[] GetBytes() => new[] { Id };
    }

    public class SetDisplayOn : ISharedCommand
    {
        public byte Id => 0xAF;
        public byte[] GetBytes() => new[] { Id };
    }

    public class SetInverseDisplay : ISharedCommand
    {
        public byte Id => 0xA7;
        public byte[] GetBytes() => new[] { Id };
    }

    public class SetMultiplexRatio : ISharedCommand
    {
        public SetMultiplexRatio(byte multiplexRatio = 63)
        {
            if (!Ssd13xx.InRange(multiplexRatio, 0x0F, 0x7F)) throw new ArgumentException("The multiplex ratio is invalid.", nameof(multiplexRatio));
            MultiplexRatio = multiplexRatio;
        }
        public byte Id => 0xA8;
        public byte MultiplexRatio { get; }
        public byte[] GetBytes() => new[] { Id, MultiplexRatio };
    }
}

namespace Iot.Device.Ssd13xx.Commands.Ssd1306Commands
{
    public enum PageAddress { Page0 = 0xB0, Page1 = 0xB1, Page2 = 0xB2, Page3 = 0xB3, Page4 = 0xB4, Page5 = 0xB5, Page6 = 0xB6, Page7 = 0xB7 }

    public class EntireDisplayOn : ISsd1306Command
    {
        public EntireDisplayOn(bool entireDisplay) => EntireDisplay = entireDisplay;
        public byte Id => (byte)(EntireDisplay ? 0xA5 : 0xA4);
        public bool EntireDisplay { get; }
        public byte[] GetBytes() => new[] { Id };
    }

    public class NoOperation : ISsd1306Command
    {
        public byte Id => 0xE3;
        public byte[] GetBytes() => new[] { Id };
    }

    public class SetChargePump : ISsd1306Command
    {
        public SetChargePump(bool enableChargePump = false) => EnableChargePump = enableChargePump;
        public byte Id => 0x8D;
        public bool EnableChargePump { get; }
        public byte[] GetBytes() => new[] { Id, (byte)(EnableChargePump ? 0x14 : 0x10) };
    }

    public class SetColumnAddress : ISsd1306Command
    {
        public SetColumnAddress(byte startAddress = 0x00, byte endAddress = 0x7F)
        {
            if (startAddress > 0x7F) throw new ArgumentOutOfRangeException(nameof(startAddress));
            if (endAddress > 0x7F) throw new ArgumentOutOfRangeException(nameof(endAddress));
            StartAddress = startAddress;
            EndAddress = endAddress;
        }
        public byte Id => 0x21;
        public byte StartAddress { get; set; }
        public byte EndAddress { get; set; }
        public byte[] GetBytes() => new[] { Id, StartAddress, EndAddress };
    }

    public class SetComOutputScanDirection : ISsd1306Command
    {
        public SetComOutputScanDirection(bool normalMode = true) => NormalMode = normalMode;
        public byte Id => (byte)(NormalMode ? 0xC0 : 0xC8);
        public bool NormalMode { get; set; }
        public byte[] GetBytes() => new[] { Id };
    }

    public class SetComPinsHardwareConfiguration : ISsd1306Command
    {
        public SetComPinsHardwareConfiguration(bool alternativeComPinConfiguration = true, bool enableLeftRightRemap = false)
        {
            AlternativeComPinConfiguration = alternativeComPinConfiguration;
            EnableLeftRightRemap = enableLeftRightRemap;
        }
        public byte Id => 0xDA;
        public bool AlternativeComPinConfiguration { get; }
        public bool EnableLeftRightRemap { get; }
        public byte[] GetBytes() => new[] { Id, (byte)(0x02 | (AlternativeComPinConfiguration ? 0x10 : 0) | (EnableLeftRightRemap ? 0x20 : 0)) };
    }

    public class SetDisplayClockDivideRatioOscillatorFrequency : ISsd1306Command
    {
        public SetDisplayClockDivideRatioOscillatorFrequency(byte displayClockDivideRatio = 0x00, byte oscillatorFrequency = 0x08)
        {
            if (displayClockDivideRatio > 0x0F) throw new ArgumentOutOfRangeException(nameof(displayClockDivideRatio));
            if (oscillatorFrequency > 0x0F) throw new ArgumentOutOfRangeException(nameof(oscillatorFrequency));
            DisplayClockDivideRatio = displayClockDivideRatio;
            OscillatorFrequency = oscillatorFrequency;
        }
        public byte Id => 0xD5;
        public byte DisplayClockDivideRatio { get; }
        public byte OscillatorFrequency { get; }
        public byte[] GetBytes() => new[] { Id, (byte)((OscillatorFrequency << 4) | DisplayClockDivideRatio) };
    }

    public class SetDisplayOffset : ISsd1306Command
    {
        public SetDisplayOffset(byte displayOffset = 0x00)
        {
            if (displayOffset > 0x3F) throw new ArgumentOutOfRangeException(nameof(displayOffset));
            DisplayOffset = displayOffset;
        }
        public byte Id => 0xD3;
        public byte DisplayOffset { get; }
        public byte[] GetBytes() => new[] { Id, DisplayOffset };
    }

    public class SetDisplayStartLine : ISsd1306Command
    {
        public SetDisplayStartLine(byte displayStartLine = 0x00)
        {
            if (displayStartLine > 0x3F) throw new ArgumentOutOfRangeException(nameof(displayStartLine));
            DisplayStartLine = displayStartLine;
        }
        public byte Id => (byte)(0x40 + DisplayStartLine);
        public byte DisplayStartLine { get; }
        public byte[] GetBytes() => new[] { Id };
    }

    public class SetMemoryAddressingMode : ISsd1306Command
    {
        public SetMemoryAddressingMode(AddressingMode memoryAddressingMode = AddressingMode.Page) => MemoryAddressingMode = memoryAddressingMode;
        public byte Id => 0x20;
        public AddressingMode MemoryAddressingMode { get; }
        public byte[] GetBytes() => new[] { Id, (byte)MemoryAddressingMode };
        public enum AddressingMode { Horizontal = 0x00, Vertical = 0x01, Page = 0x02 }
    }

    public class SetNormalDisplay : ISsd1306Command
    {
        public byte Id => 0xA6;
        public byte[] GetBytes() => new[] { Id };
    }

    public class SetPageAddress : ISsd1306Command
    {
        public SetPageAddress(PageAddress startAddress = PageAddress.Page0, PageAddress endAddress = PageAddress.Page7)
        {
            StartAddress = startAddress;
            EndAddress = endAddress;
        }
        public byte Id => 0x22;
        public PageAddress StartAddress { get; }
        public PageAddress EndAddress { get; }
        public byte[] GetBytes() => new[] { Id, (byte)StartAddress, (byte)EndAddress };
    }

    public class SetPageStartAddressForPageAddressingMode : ISsd1306Command
    {
        public SetPageStartAddressForPageAddressingMode(PageAddress startAddress = PageAddress.Page0) => StartAddress = startAddress;
        public byte Id => unchecked((byte)(0xB0 + (int)StartAddress));
        public PageAddress StartAddress { get; }
        public byte[] GetBytes() => new[] { Id };
    }

    public class SetLowerColumnStartAddressForPageAddressingMode : ISsd1306Command
    {
        public SetLowerColumnStartAddressForPageAddressingMode(byte lowerColumnStartAddress = 0x00)
        {
            if (lowerColumnStartAddress > 0x0F) throw new ArgumentOutOfRangeException(nameof(lowerColumnStartAddress));
            LowerColumnStartAddress = lowerColumnStartAddress;
        }
        public byte Id => LowerColumnStartAddress;
        public byte LowerColumnStartAddress { get; }
        public byte[] GetBytes() => new[] { Id };
    }

    public class SetHigherColumnStartAddressForPageAddressingMode : ISsd1306Command
    {
        public SetHigherColumnStartAddressForPageAddressingMode(byte higherColumnStartAddress = 0x00)
        {
            if (higherColumnStartAddress > 0x0F) throw new ArgumentOutOfRangeException(nameof(higherColumnStartAddress));
            HigherColumnStartAddress = higherColumnStartAddress;
        }
        public byte Id => (byte)(0x10 + HigherColumnStartAddress);
        public byte HigherColumnStartAddress { get; }
        public byte[] GetBytes() => new[] { Id };
    }

    public class SetPreChargePeriod : ISsd1306Command
    {
        public SetPreChargePeriod(byte phase1Period = 0x02, byte phase2Period = 0x02)
        {
            if (!Ssd13xx.InRange(phase1Period, 0x01, 0x0F)) throw new ArgumentOutOfRangeException(nameof(phase1Period));
            if (!Ssd13xx.InRange(phase2Period, 0x01, 0x0F)) throw new ArgumentOutOfRangeException(nameof(phase2Period));
            Phase1Period = phase1Period;
            Phase2Period = phase2Period;
        }
        public byte Id => 0xD9;
        public byte Phase1Period { get; }
        public byte Phase2Period { get; }
        public byte[] GetBytes() => new[] { Id, (byte)((Phase2Period << 4) | Phase1Period) };
    }

    public class SetSegmentReMap : ISsd1306Command
    {
        public SetSegmentReMap(bool columnAddress127 = false) => ColumnAddress127 = columnAddress127;
        public byte Id => (byte)(ColumnAddress127 ? 0xA1 : 0xA0);
        public bool ColumnAddress127 { get; }
        public byte[] GetBytes() => new[] { Id };
    }

    public class SetVcomhDeselectLevel : ISsd1306Command
    {
        public SetVcomhDeselectLevel(DeselectLevel level = DeselectLevel.Vcc0_77) => Level = level;
        public byte Id => 0xDB;
        public DeselectLevel Level { get; }
        public byte[] GetBytes() => new[] { Id, (byte)Level };
        public enum DeselectLevel { Vcc0_65 = 0x00, Vcc0_77 = 0x20, Vcc0_83 = 0x30, Vcc1_00 = 0x40 }
    }
}
