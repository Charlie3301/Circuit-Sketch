// Arduino.cs -- a API do Arduino para o C# (PinMode, DigitalWrite, Delay,
// Serial.Println...), com os dois jeitos de escrever o nome: o do C#
// (DigitalWrite) e o do Arduino (digitalWrite). O programa a ve sem "using"
// (o compilador poe "global using static CircuitSketch.Arduino"), e as
// constantes da placa (HIGH, OUTPUT, LED_BUILTIN, A0) vem da CircuitSketch.Pinos,
// gerada para a placa do projeto.
using System;

namespace CircuitSketch
{
    public static class Arduino
    {
        static object[] A(params object[] a) => a;

        public static void PinMode(int pino, int modo) => Ponte.C(null, "pinMode", pino, modo);
        public static void DigitalWrite(int pino, int valor) => Ponte.C(null, "digitalWrite", pino, valor);
        public static void DigitalWrite(int pino, bool valor) => DigitalWrite(pino, valor ? 1 : 0);
        public static int DigitalRead(int pino) => (int)Ponte.I(null, "digitalRead", pino);
        public static int AnalogRead(int pino) => (int)Ponte.I(null, "analogRead", pino);
        public static void AnalogWrite(int pino, int valor) => Ponte.C(null, "analogWrite", pino, valor);
        public static void AnalogReadResolution(int bits) => Ponte.C(null, "analogReadResolution", bits);
        public static void AnalogWriteResolution(int bits) => Ponte.C(null, "analogWriteResolution", bits);
        public static void Delay(long ms) => Ponte.Dormir((int)Math.Min(ms, int.MaxValue));
        public static void DelayMicroseconds(long us) => Ponte.C(null, "delayMicroseconds", us);
        public static long Millis() => Ponte.I(null, "millis");
        public static long Micros() => Ponte.I(null, "micros");
        public static void Tone(int pino, int frequencia) => Ponte.C(null, "tone", pino, frequencia);
        public static void Tone(int pino, int frequencia, long duracao) => Ponte.C(null, "tone", pino, frequencia, duracao);
        public static void NoTone(int pino) => Ponte.C(null, "noTone", pino);
        public static long PulseIn(int pino, int estado, long timeout = 1000000) => Ponte.I(null, "pulseIn", pino, estado, timeout);
        public static void ShiftOut(int dados, int relogio, int ordem, int valor) => Ponte.C(null, "shiftOut", dados, relogio, ordem, valor);
        public static int ShiftIn(int dados, int relogio, int ordem) => (int)Ponte.I(null, "shiftIn", dados, relogio, ordem);
        public static int DigitalPinToInterrupt(int pino) => (int)Ponte.I(null, "digitalPinToInterrupt", pino);

        public static void AttachInterrupt(int interrupcao, Action rotina, int modo)
            => Ponte.CN(null, "attachInterrupt", A(interrupcao, Ponte.Registrar(rotina), modo), new[] { false, true, false });
        public static void DetachInterrupt(int interrupcao) => Ponte.C(null, "detachInterrupt", interrupcao);
        public static void Interrupts() => Ponte.C(null, "interrupts");
        public static void NoInterrupts() => Ponte.C(null, "noInterrupts");

        public static long Map(long x, long deMin, long deMax, long paraMin, long paraMax)
            => deMax == deMin ? paraMin : (x - deMin) * (paraMax - paraMin) / (deMax - deMin) + paraMin;
        public static double Map(double x, double deMin, double deMax, double paraMin, double paraMax)
            => deMax == deMin ? paraMin : (x - deMin) * (paraMax - paraMin) / (deMax - deMin) + paraMin;
        public static long Constrain(long x, long a, long b) => x < a ? a : x > b ? b : x;
        public static double Constrain(double x, double a, double b) => x < a ? a : x > b ? b : x;

        static readonly Random sorteio = new Random();
        public static long random(long ate) => ate <= 0 ? 0 : (long)(sorteio.NextDouble() * ate);
        public static long random(long de, long ate) => ate <= de ? de : de + random(ate - de);
        public static void RandomSeed(long semente) { }

        public static SerialDaPlaca Serial { get; } = new SerialDaPlaca("Serial");
        public static SerialDaPlaca Serial1 { get; } = new SerialDaPlaca("Serial1");
        public static SerialDaPlaca Serial2 { get; } = new SerialDaPlaca("Serial2");

        // ---- os mesmos, com o nome do Arduino ----
        public static void pinMode(int pino, int modo) => PinMode(pino, modo);
        public static void digitalWrite(int pino, int valor) => DigitalWrite(pino, valor);
        public static void digitalWrite(int pino, bool valor) => DigitalWrite(pino, valor);
        public static int digitalRead(int pino) => DigitalRead(pino);
        public static int analogRead(int pino) => AnalogRead(pino);
        public static void analogWrite(int pino, int valor) => AnalogWrite(pino, valor);
        public static void delay(long ms) => Delay(ms);
        public static void delayMicroseconds(long us) => DelayMicroseconds(us);
        public static long millis() => Millis();
        public static long micros() => Micros();
        public static void tone(int pino, int frequencia) => Tone(pino, frequencia);
        public static void tone(int pino, int frequencia, long duracao) => Tone(pino, frequencia, duracao);
        public static void noTone(int pino) => NoTone(pino);
        public static long pulseIn(int pino, int estado, long timeout = 1000000) => PulseIn(pino, estado, timeout);
        public static void attachInterrupt(int interrupcao, Action rotina, int modo) => AttachInterrupt(interrupcao, rotina, modo);
        public static void detachInterrupt(int interrupcao) => DetachInterrupt(interrupcao);
        public static int digitalPinToInterrupt(int pino) => DigitalPinToInterrupt(pino);
        public static long map(long x, long deMin, long deMax, long paraMin, long paraMax) => Map(x, deMin, deMax, paraMin, paraMax);
        public static long constrain(long x, long a, long b) => Constrain(x, a, b);
        public static double constrain(double x, double a, double b) => Constrain(x, a, b);
    }

    /// <summary>A Serial do Arduino (e a Serial1/Serial2), para o C#.</summary>
    public sealed class SerialDaPlaca
    {
        readonly string nome;
        public SerialDaPlaca(string nome) { this.nome = nome; }

        public void Begin(long baud) => Ponte.C(nome, "begin", baud);
        public void Begin(long baud, int config, int rx, int tx) => Ponte.C(nome, "begin", baud, config, rx, tx);
        public void End() => Ponte.C(nome, "end");
        public void Print(object v) => Ponte.C(nome, "print", Ponte.Texto(v is IFormattable f ? f.ToString(null, System.Globalization.CultureInfo.InvariantCulture) : v?.ToString()));
        public void Println() => Ponte.C(nome, "println");
        public void Println(object v)
        {
            Print(v);
            Println();
        }
        public void Write(int b) => Ponte.C(nome, "write", b);
        public int Available() => (int)Ponte.I(nome, "available");
        public int Read() => (int)Ponte.I(nome, "read");
        public int Peek() => (int)Ponte.I(nome, "peek");
        public string ReadString() => Ponte.T(nome, "readString");
        public string ReadStringUntil(char fim) => Ponte.T(nome, "readStringUntil", fim);
        public long ParseInt() => Ponte.I(nome, "parseInt");
        public double ParseFloat() => Ponte.R(nome, "parseFloat");
        public void Flush() => Ponte.C(nome, "flush");

        public void begin(long baud) => Begin(baud);
        public void print(object v) => Print(v);
        public void println() => Println();
        public void println(object v) => Println(v);
        public void write(int b) => Write(b);
        public int available() => Available();
        public int read() => Read();
        public int peek() => Peek();
        public string readString() => ReadString();
        public string readStringUntil(char fim) => ReadStringUntil(fim);
        public long parseInt() => ParseInt();
        public double parseFloat() => ParseFloat();
    }
}
