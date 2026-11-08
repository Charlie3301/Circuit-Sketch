// Leds.cs -- a fita de LEDs enderecaveis do nanoFramework (Iot.Device.Ws28xx.Esp32,
// adaptado dos fontes de verdade, licenca MIT, (c) .NET Foundation e colaboradores)
// sobre o RMT da ESP32 (nanoFramework.Hardware.Esp32.Rmt). Os bytes saem pelo
// pino na ordem de cada chip (G R B no WS2812B) e o simulador acende a fita do
// circuito, como no Adafruit_NeoPixel.
using System;
using System.Drawing;
using CircuitSketch;

namespace nanoFramework.Hardware.Esp32.Rmt
{
    /// <summary>O canal do RMT que manda os bits dos LEDs (o tempo de cada nivel em passos de 100 ns).</summary>
    public class LedTransmitChannel : IDisposable
    {
        readonly int pino;

        public LedTransmitChannel(int gpioPin, ushort t0l, ushort t0h, ushort t1l, ushort t1h, ushort reset)
        {
            pino = gpioPin;
            Ponte.C(null, "pinMode", gpioPin, Ponte.Numero("OUTPUT", 1));
        }

        /// <summary>Cada byte, do bit 7 ao 0, vira o pulso do 1 ou do 0.</summary>
        public void SendLedData(byte[] data) => Ponte.C(null, "__fita", pino, data ?? Array.Empty<byte>());

        public void Dispose() { }
    }
}

namespace Iot.Device.Ws28xx.Esp32
{
    public abstract class BitmapImage
    {
        protected BitmapImage(byte[] data, int width, int height, int stride)
        {
            Data = data;
            Width = width;
            Height = height;
            Stride = stride;
        }

        public byte[] Data { get; }
        public int Width { get; }
        public int Height { get; }
        public int Stride { get; }

        public abstract void SetPixel(int x, int y, Color color);
        public abstract void SetPixel(int x, int y, byte r, byte g, byte b);

        public virtual void Clear(Color color)
        {
            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++) SetPixel(x, y, color);
        }

        public abstract void Clear();
        public abstract void Clear(int x, int y);

        /// <summary>Os bytes que vao para a fita, na ordem do chip.</summary>
        internal virtual byte[] Bytes => Data;
    }

    /// <summary>
    /// Os bits de cada cor espalhados em tres (o 0 vira 100, o 1 vira 110), como o original
    /// guarda; o que chega na fita e o byte de cada cor.
    /// </summary>
    public class BitmapImageNeo3 : BitmapImage
    {
        protected const int BytesPerComponent = 3;
        protected const int BytesPerPixel = BytesPerComponent * 3;
        protected static readonly byte[] Lookup = new byte[256 * BytesPerComponent];

        static BitmapImageNeo3() => Preencher(Lookup);

        internal static void Preencher(byte[] tabela)
        {
            for (int i = 0; i < 256; i++)
            {
                int data = 0;
                for (int j = 7; j >= 0; j--) data = (data << 3) | 0b100 | ((i >> j) << 1) & 2;
                tabela[(i * 3) + 0] = unchecked((byte)(data >> 16));
                tabela[(i * 3) + 1] = unchecked((byte)(data >> 8));
                tabela[(i * 3) + 2] = unchecked((byte)(data >> 0));
            }
        }

        /// <summary>De volta dos tres bytes ao byte: o bit do meio de cada trio.</summary>
        internal static byte[] Juntar(byte[] dados)
        {
            var r = new byte[dados.Length / 3];
            for (int k = 0; k < r.Length; k++)
            {
                int trio = dados[k * 3] << 16 | dados[k * 3 + 1] << 8 | dados[k * 3 + 2], b = 0;
                for (int j = 7; j >= 0; j--) b = b << 1 | (trio >> (j * 3 + 1) & 1);
                r[k] = (byte)b;
            }
            return r;
        }

        public BitmapImageNeo3(int width, int height) : base(new byte[width * height * BytesPerPixel], width, height, width * BytesPerPixel) { }

        protected void Gravar(int offset, byte v)
        {
            Data[offset] = Lookup[(v * BytesPerComponent) + 0];
            Data[offset + 1] = Lookup[(v * BytesPerComponent) + 1];
            Data[offset + 2] = Lookup[(v * BytesPerComponent) + 2];
        }

        public override void SetPixel(int x, int y, Color c) => SetPixel(x, y, c.R, c.G, c.B);

        public override void SetPixel(int x, int y, byte r, byte g, byte b)
        {
            var offset = (y * Stride) + (x * BytesPerPixel);
            Gravar(offset, g);
            Gravar(offset + 3, r);
            Gravar(offset + 6, b);
        }

        public override void Clear(int x, int y) => Array.Clear(Data, (y * Stride) + (x * BytesPerPixel), BytesPerPixel);
        public override void Clear() => Array.Clear(Data, 0, Data.Length);

        internal override byte[] Bytes => Juntar(Data);
    }

    /// <summary>O WS2815: a mesma codificacao, na ordem R G B.</summary>
    public class BitmapImageNeo3Rgb : BitmapImageNeo3
    {
        public BitmapImageNeo3Rgb(int width, int height) : base(width, height) { }

        public override void SetPixel(int x, int y, byte r, byte g, byte b)
        {
            var offset = (y * Stride) + (x * BytesPerPixel);
            Gravar(offset, r);
            Gravar(offset + 3, g);
            Gravar(offset + 6, b);
        }
    }

    /// <summary>O RGBW (o SK6812 de 4 cores): o alfa da cor e o branco.</summary>
    public class BitmapImageNeo4 : BitmapImage
    {
        const int BytesPerComponent = 3;
        const int BytesPerPixel = BytesPerComponent * 4;
        static readonly byte[] Lookup = new byte[256 * BytesPerComponent];

        static BitmapImageNeo4() => BitmapImageNeo3.Preencher(Lookup);

        public BitmapImageNeo4(int width, int height) : base(new byte[width * height * BytesPerPixel], width, height, width * BytesPerPixel) { }

        void Gravar(int offset, byte v)
        {
            Data[offset] = Lookup[(v * BytesPerComponent) + 0];
            Data[offset + 1] = Lookup[(v * BytesPerComponent) + 1];
            Data[offset + 2] = Lookup[(v * BytesPerComponent) + 2];
        }

        public override void Clear() => Array.Clear(Data, 0, Data.Length);
        public override void Clear(int x, int y) => SetPixel(x, y, Color.Black);

        public override void SetPixel(int x, int y, Color c)
        {
            var offset = (y * Stride) + (x * BytesPerPixel);
            Gravar(offset, c.G);
            Gravar(offset + 3, c.R);
            Gravar(offset + 6, c.B);
            Gravar(offset + 9, c.A);
        }

        public override void SetPixel(int x, int y, byte r, byte g, byte b) => SetPixel(x, y, Color.FromArgb(r, g, b));

        internal override byte[] Bytes => BitmapImageNeo3.Juntar(Data);
    }

    /// <summary>Um byte por cor, na ordem R G B (o WS2808).</summary>
    public class BitmapImageWs2808 : BitmapImage
    {
        const int BytesPerPixel = 3;

        public BitmapImageWs2808(int width, int height) : base(new byte[width * height * BytesPerPixel], width, height, width * BytesPerPixel) { }

        public override void SetPixel(int x, int y, Color c) => SetPixel(x, y, c.R, c.G, c.B);

        public override void SetPixel(int x, int y, byte r, byte g, byte b)
        {
            var offset = (y * Stride) + (x * BytesPerPixel);
            Data[offset++] = r;
            Data[offset++] = g;
            Data[offset] = b;
        }

        public override void Clear() => Array.Clear(Data, 0, Data.Length);
        public override void Clear(int x, int y) => Array.Clear(Data, (y * Stride) + (x * BytesPerPixel), 3);
    }

    /// <summary>Um byte por cor, na ordem G R B (o SK6812 e o WS2812C).</summary>
    public class BitmapImageWs2808Grb : BitmapImage
    {
        const int BytesPerPixel = 3;

        public BitmapImageWs2808Grb(int width, int height) : base(new byte[width * height * BytesPerPixel], width, height, width * BytesPerPixel) { }

        public override void SetPixel(int x, int y, Color c) => SetPixel(x, y, c.R, c.G, c.B);

        public override void SetPixel(int x, int y, byte r, byte g, byte b)
        {
            var offset = (y * Stride) + (x * BytesPerPixel);
            Data[offset++] = g;
            Data[offset++] = r;
            Data[offset] = b;
        }

        public override void Clear() => Array.Clear(Data, 0, Data.Length);
        public override void Clear(int x, int y) => Array.Clear(Data, (y * Stride) + (x * BytesPerPixel), 3);
    }

    public class Ws28xx : nanoFramework.Hardware.Esp32.Rmt.LedTransmitChannel
    {
        protected readonly int GpioPin;

        public BitmapImage Image { get; }

        public Ws28xx(int gpioPin, BitmapImage image, ushort t0l, ushort t0h, ushort t1l, ushort t1h, ushort reset)
            : base(gpioPin, t0l, t0h, t1l, t1h, reset)
        {
            if (gpioPin < 0) throw new ArgumentException();
            GpioPin = gpioPin;
            Image = image;
        }

        public void Update() => SendLedData(Image.Bytes);
    }

    public class Ws2812b : Ws28xx
    {
        public Ws2812b(int gpioPin, int width, int height = 1) : base(gpioPin, new BitmapImageNeo3(width, height), 9, 4, 5, 8, 550) { }
    }

    public class Ws2812c : Ws28xx
    {
        public Ws2812c(int gpioPin, int width, int height = 1) : base(gpioPin, new BitmapImageWs2808Grb(width, height), 6, 3, 3, 6, 2900) { }
    }

    public class Ws2815b : Ws28xx
    {
        public Ws2815b(int gpioPin, int width, int height = 1) : base(gpioPin, new BitmapImageNeo3Rgb(width, height), 6, 3, 3, 6, 2900) { }
    }

    public class Ws2808 : Ws28xx
    {
        public Ws2808(int gpioPin, int width, int height = 1) : base(gpioPin, new BitmapImageWs2808(width, height), 6, 3, 6, 6, 2800) { }
    }

    public class Sk6812 : Ws28xx
    {
        public Sk6812(int gpioPin, int width, int height = 1) : base(gpioPin, new BitmapImageWs2808Grb(width, height), 9, 3, 6, 6, 800) { }
    }
}
