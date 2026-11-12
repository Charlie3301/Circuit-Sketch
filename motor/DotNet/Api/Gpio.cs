// Gpio.cs -- System.Device.Gpio, o do nanoFramework e o do .NET IoT (a mesma
// API): GpioController, GpioPin, PinValue e o evento de mudanca do pino, que
// e a interrupcao do simulador.
using System;
using System.Collections.Generic;
using CircuitSketch;

namespace System.Device.Gpio
{
    public enum PinMode
    {
        Input,
        Output,
        InputPullDown,
        InputPullUp,
        OutputOpenDrain,
        OutputOpenDrainPullUp,
        OutputOpenSource,
        OutputOpenSourcePullDown,
    }

    public enum PinNumberingScheme
    {
        Logical,
        Board,
    }

    [Flags]
    public enum PinEventTypes
    {
        None = 0,
        Rising = 1,
        Falling = 2,
    }

    public readonly struct PinValue : IEquatable<PinValue>
    {
        readonly byte valor;
        PinValue(byte v) { valor = v; }

        public static PinValue Low => new PinValue(0);
        public static PinValue High => new PinValue(1);

        public static implicit operator PinValue(int v) => new PinValue((byte)(v != 0 ? 1 : 0));
        public static implicit operator PinValue(bool v) => new PinValue((byte)(v ? 1 : 0));
        public static explicit operator int(PinValue v) => v.valor;
        public static explicit operator byte(PinValue v) => v.valor;
        public static explicit operator bool(PinValue v) => v.valor != 0;
        public static bool operator ==(PinValue a, PinValue b) => a.valor == b.valor;
        public static bool operator !=(PinValue a, PinValue b) => a.valor != b.valor;
        public static PinValue operator !(PinValue v) => new PinValue((byte)(v.valor ^ 1));
        public bool Equals(PinValue o) => valor == o.valor;
        public override bool Equals(object o) => o is PinValue p && p.valor == valor;
        public override int GetHashCode() => valor;
        public override string ToString() => valor != 0 ? "High" : "Low";
    }

    public class PinValueChangedEventArgs : EventArgs
    {
        public PinValueChangedEventArgs(PinEventTypes changeType, int pinNumber)
        {
            ChangeType = changeType;
            PinNumber = pinNumber;
        }

        public PinEventTypes ChangeType { get; }
        public int PinNumber { get; }
    }

    public delegate void PinChangeEventHandler(object sender, PinValueChangedEventArgs pinValueChangedEventArgs);

    /// <summary>O pino aberto (o GpioPin do nanoFramework e do .NET IoT 2.x).</summary>
    public class GpioPin : IDisposable
    {
        readonly GpioController dono;
        PinMode modo;
        PinChangeEventHandler aoMudar;

        internal GpioPin(GpioController dono, int pino, PinMode modo)
        {
            this.dono = dono;
            PinNumber = pino;
            this.modo = modo;
        }

        public int PinNumber { get; }
        public TimeSpan DebounceTimeout { get; set; }

        public PinValue Read() => dono.Read(PinNumber);
        public void Write(PinValue valor) => dono.Write(PinNumber, valor);
        public void Toggle() => Write(!Read());
        public PinMode GetPinMode() => modo;
        public void SetPinMode(PinMode novo)
        {
            modo = novo;
            dono.SetPinMode(PinNumber, novo);
        }
        public bool IsPinModeSupported(PinMode m) => dono.IsPinModeSupported(PinNumber, m);

        public event PinChangeEventHandler ValueChanged
        {
            add
            {
                bool primeiro = aoMudar == null;
                aoMudar += value;
                if (primeiro) dono.RegisterCallbackForPinValueChangedEvent(PinNumber, PinEventTypes.Rising | PinEventTypes.Falling, Disparar);
            }
            remove
            {
                aoMudar -= value;
                if (aoMudar == null) dono.UnregisterCallbackForPinValueChangedEvent(PinNumber, Disparar);
            }
        }

        void Disparar(object sender, PinValueChangedEventArgs e) => aoMudar?.Invoke(this, e);

        public void Dispose() => dono.ClosePin(PinNumber);
    }

    public class GpioController : IDisposable
    {
        readonly Dictionary<int, GpioPin> abertos = new Dictionary<int, GpioPin>();
        readonly Dictionary<int, List<(PinEventTypes Tipos, PinChangeEventHandler Rotina)>> ouvintes =
            new Dictionary<int, List<(PinEventTypes, PinChangeEventHandler)>>();

        public GpioController() { }
        public GpioController(PinNumberingScheme numberingScheme) { NumberingScheme = numberingScheme; }

        public PinNumberingScheme NumberingScheme { get; }
        public int PinCount => (int)Ponte.Numero("NUM_DIGITAL_PINS", 40);

        static int ModoDoArduino(PinMode m) => m switch
        {
            PinMode.Output or PinMode.OutputOpenSource or PinMode.OutputOpenSourcePullDown => Ponte.Numero("OUTPUT", 1),
            PinMode.OutputOpenDrain or PinMode.OutputOpenDrainPullUp => Ponte.Numero("OUTPUT_OPEN_DRAIN", Ponte.Numero("OUTPUT", 1)),
            PinMode.InputPullUp => Ponte.Numero("INPUT_PULLUP", 2),
            PinMode.InputPullDown => Ponte.Numero("INPUT_PULLDOWN", Ponte.Numero("INPUT", 0)),
            _ => Ponte.Numero("INPUT", 0),
        };

        public GpioPin OpenPin(int pinNumber) => OpenPin(pinNumber, PinMode.Input);

        public GpioPin OpenPin(int pinNumber, PinMode mode)
        {
            if (abertos.ContainsKey(pinNumber)) throw new InvalidOperationException(Idioma.T("o pino {0} ja esta aberto", pinNumber));
            var p = new GpioPin(this, pinNumber, mode);
            abertos[pinNumber] = p;
            Ponte.C(null, "pinMode", pinNumber, ModoDoArduino(mode));
            return p;
        }

        public GpioPin OpenPin(int pinNumber, PinMode mode, PinValue initialValue)
        {
            var p = OpenPin(pinNumber, mode);
            Write(pinNumber, initialValue);
            return p;
        }

        public void ClosePin(int pinNumber)
        {
            if (abertos.Remove(pinNumber)) Ponte.C(null, "pinMode", pinNumber, Ponte.Numero("INPUT", 0));
            if (ouvintes.Remove(pinNumber)) Ponte.C(null, "detachInterrupt", Ponte.I(null, "digitalPinToInterrupt", pinNumber));
        }

        public bool IsPinOpen(int pinNumber) => abertos.ContainsKey(pinNumber);
        public void SetPinMode(int pinNumber, PinMode mode) => Ponte.C(null, "pinMode", pinNumber, ModoDoArduino(mode));
        public PinMode GetPinMode(int pinNumber) => abertos.TryGetValue(pinNumber, out var p) ? p.GetPinMode() : PinMode.Input;
        public bool IsPinModeSupported(int pinNumber, PinMode mode) => mode <= PinMode.OutputOpenDrain;

        public void Write(int pinNumber, PinValue value) => Ponte.C(null, "digitalWrite", pinNumber, (int)value);
        public PinValue Read(int pinNumber) => Ponte.I(null, "digitalRead", pinNumber) != 0;
        public void Toggle(int pinNumber) => Write(pinNumber, !Read(pinNumber));

        /// <summary>A mudanca do pino: a interrupcao do simulador (CHANGE), que diz se subiu ou desceu.</summary>
        public void RegisterCallbackForPinValueChangedEvent(int pinNumber, PinEventTypes eventTypes, PinChangeEventHandler callback)
        {
            if (!ouvintes.TryGetValue(pinNumber, out var lista))
            {
                lista = new List<(PinEventTypes, PinChangeEventHandler)>();
                ouvintes[pinNumber] = lista;
                int pino = pinNumber;
                Action rotina = () =>
                {
                    var tipo = Ponte.I(null, "digitalRead", pino) != 0 ? PinEventTypes.Rising : PinEventTypes.Falling;
                    if (!ouvintes.TryGetValue(pino, out var atuais)) return;
                    foreach (var (tipos, r) in atuais.ToArray())
                        if ((tipos & tipo) != 0) r(this, new PinValueChangedEventArgs(tipo, pino));
                };
                long interrupcao = Ponte.I(null, "digitalPinToInterrupt", pinNumber);
                Ponte.CN(null, "attachInterrupt", new object[] { interrupcao, Ponte.Registrar(rotina), Ponte.Numero("CHANGE", 3) },
                         new[] { false, true, false });
            }
            lista.Add((eventTypes, callback));
        }

        public void UnregisterCallbackForPinValueChangedEvent(int pinNumber, PinChangeEventHandler callback)
        {
            if (!ouvintes.TryGetValue(pinNumber, out var lista)) return;
            lista.RemoveAll(o => o.Rotina == callback);
            if (lista.Count > 0) return;
            ouvintes.Remove(pinNumber);
            Ponte.C(null, "detachInterrupt", Ponte.I(null, "digitalPinToInterrupt", pinNumber));
        }

        public void Dispose()
        {
            foreach (int p in new List<int>(abertos.Keys)) ClosePin(p);
        }
    }
}
