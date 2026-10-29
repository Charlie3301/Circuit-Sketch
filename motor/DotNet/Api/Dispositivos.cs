// Dispositivos.cs -- as ligacoes (Iot.Device) mais usadas com o nanoFramework
// e o .NET IoT: o servo, o DHT e o sensor ultrassonico, sobre as pecas do
// simulador (o Servo e o DHT sao os da biblioteca do Arduino, pelo nome), e
// as unidades (UnitsNet) que elas devolvem.
using System;
using System.Device.Pwm;
using CircuitSketch;

namespace UnitsNet
{
    public readonly struct Temperature
    {
        Temperature(double c) { DegreesCelsius = c; }
        public double DegreesCelsius { get; }
        public double DegreesFahrenheit => DegreesCelsius * 9 / 5 + 32;
        public double Kelvins => DegreesCelsius + 273.15;
        public static Temperature FromDegreesCelsius(double c) => new Temperature(c);
        public static Temperature FromDegreesFahrenheit(double f) => new Temperature((f - 32) * 5 / 9);
        public static Temperature FromKelvins(double k) => new Temperature(k - 273.15);
        public override string ToString() => DegreesCelsius.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + " °C";
    }

    public readonly struct RelativeHumidity
    {
        RelativeHumidity(double p) { Percent = p; }
        public double Percent { get; }
        public static RelativeHumidity FromPercent(double p) => new RelativeHumidity(p);
        public override string ToString() => Percent.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + " %RH";
    }

    public readonly struct Length
    {
        Length(double m) { Meters = m; }
        public double Meters { get; }
        public double Centimeters => Meters * 100;
        public double Millimeters => Meters * 1000;
        public double Inches => Meters / 0.0254;
        public static Length FromMeters(double m) => new Length(m);
        public static Length FromCentimeters(double cm) => new Length(cm / 100);
        public static Length FromMillimeters(double mm) => new Length(mm / 1000);
        public override string ToString() => Centimeters.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " cm";
    }
}

namespace Iot.Device.ServoMotor
{
    /// <summary>O servo do circuito no pino do PWM (o servo da bancada gira pelo angulo).</summary>
    public class ServoMotor : IDisposable
    {
        readonly string servo;
        readonly double anguloMaximo, minimo, maximo;

        public ServoMotor(PwmChannel pwmChannel, double maximumAngle = 180, double minimumPulseWidthMicroseconds = 1000,
                          double maximumPulseWidthMicroseconds = 2000)
        {
            anguloMaximo = maximumAngle;
            minimo = minimumPulseWidthMicroseconds;
            maximo = maximumPulseWidthMicroseconds;
            servo = Ponte.T(null, "__criar", "Servo");
            Ponte.C(servo, "attach", pwmChannel.Pino, (long)minimo, (long)maximo);
        }

        public void Start() { }
        public void Stop() { }

        public void WriteAngle(double angle)
        {
            if (angle < 0 || angle > anguloMaximo) throw new ArgumentOutOfRangeException(nameof(angle));
            WritePulseWidth((int)Math.Round(minimo + (maximo - minimo) * angle / anguloMaximo));
        }

        public void WritePulseWidth(int microseconds) => Ponte.C(servo, "writeMicroseconds", microseconds);

        public void Calibrate(double maximumAngle, double minimumPulseWidthMicroseconds, double maximumPulseWidthMicroseconds) { }
        public void Dispose() => Ponte.C(servo, "detach");
    }

}

namespace Iot.Device.DHTxx
{
    /// <summary>O DHT do circuito (a biblioteca DHT do simulador, no pino).</summary>
    public abstract class DhtBase : IDisposable
    {
        readonly string dht;

        protected DhtBase(int pin, int tipo)
        {
            dht = Ponte.T(null, "__criar", "DHT", pin, tipo);
            Ponte.C(dht, "begin");
        }

        public bool IsLastReadSuccessful { get; private set; }

        public UnitsNet.Temperature Temperature
        {
            get
            {
                double c = Ponte.R(dht, "readTemperature");
                IsLastReadSuccessful = !double.IsNaN(c);
                return UnitsNet.Temperature.FromDegreesCelsius(c);
            }
        }

        public UnitsNet.RelativeHumidity Humidity
        {
            get
            {
                double h = Ponte.R(dht, "readHumidity");
                IsLastReadSuccessful = !double.IsNaN(h);
                return UnitsNet.RelativeHumidity.FromPercent(h);
            }
        }

        public bool TryReadTemperature(out UnitsNet.Temperature t)
        {
            t = Temperature;
            return IsLastReadSuccessful;
        }

        public bool TryReadHumidity(out UnitsNet.RelativeHumidity h)
        {
            h = Humidity;
            return IsLastReadSuccessful;
        }

        public void Dispose() { }
    }

    public class Dht11 : DhtBase
    {
        public Dht11(int pin) : base(pin, 11) { }
    }

    public class Dht22 : DhtBase
    {
        public Dht22(int pin) : base(pin, 22) { }
    }

    public class Dht21 : DhtBase
    {
        public Dht21(int pin) : base(pin, 21) { }
    }
}

namespace Iot.Device.Hcsr04
{
    /// <summary>O HC-SR04: o pulso no trigger e o eco medido (pulseIn).</summary>
    public class Hcsr04 : IDisposable
    {
        readonly int trigger, eco;

        public Hcsr04(int triggerPin, int echoPin)
        {
            trigger = triggerPin;
            eco = echoPin;
            Ponte.C(null, "pinMode", trigger, Ponte.Numero("OUTPUT", 1));
            Ponte.C(null, "pinMode", eco, Ponte.Numero("INPUT", 0));
        }

        public bool TryGetDistance(out UnitsNet.Length result)
        {
            Ponte.C(null, "digitalWrite", trigger, 0);
            Ponte.C(null, "delayMicroseconds", 2);
            Ponte.C(null, "digitalWrite", trigger, 1);
            Ponte.C(null, "delayMicroseconds", 10);
            Ponte.C(null, "digitalWrite", trigger, 0);
            long us = Ponte.I(null, "pulseIn", eco, 1, 30000L);
            result = UnitsNet.Length.FromCentimeters(us * 0.0343 / 2);
            return us > 0;
        }

        public UnitsNet.Length Distance => TryGetDistance(out var d) ? d : throw new InvalidOperationException("o eco nao voltou");

        public void Dispose() { }
    }
}
