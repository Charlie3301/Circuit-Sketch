namespace Simulador.App;

/// <summary>
/// Os drivers de motor de passo: o A4988 e o DRV8825 com o NEMA 17, e o
/// ULN2003 com o 28BYJ-48 -- micropasso, sentido, e os jeitos de ficar
/// parado que pegam todo mundo na bancada.
/// </summary>
public static partial class Autoteste
{
    sealed class BancadaDePasso
    {
        public Circuito C = new();
        public PecaNoCircuito Placa, Fonte, Driver, Motor;
        public Maquina Maq;
        public EditorCircuito Ed;
        Bancada bancada;
        Netlist rede;

        public BancadaDePasso(string driver, string motor, double volts)
        {
            Placa = new PecaNoCircuito { Id = "placa", Tipo = "esp32-devkit-v1", X = 0, Y = 0, Usb = true };
            Fonte = new PecaNoCircuito { Id = "fonte", Tipo = "bateria", X = 300, Y = 300, Volts = volts, MiliAmperes = 3000 };
            Driver = new PecaNoCircuito { Id = "driver", Tipo = driver, X = 300, Y = 0 };
            Motor = new PecaNoCircuito { Id = "motor", Tipo = motor, X = 600, Y = 0 };
            C.Pecas.AddRange([Placa, Fonte, Driver, Motor]);
            Maq = new Maquina { Perfil = PerfilPlaca.Esp32 };
            Maq.Reiniciar();
        }

        public void Ligar(PecaNoCircuito a, string pa, PecaNoCircuito b, string pb)
            => C.Fios.Add(new Fio { DePeca = a.Id, DePino = pa, ParaPeca = b.Id, ParaPino = pb });

        public void Quadro()
        {
            if (Ed is null)
            {
                Ed = new EditorCircuito();
                Ed.Carregar(C);
                bancada = Simulador.App.Bancada.DeUmaSo(Maq);
                rede = new Netlist(C);
            }
            Ed.AplicarEntradas(bancada, rede);
        }

        public void Saida(int gpio) { Maq.PinMode(gpio, 1); }

        /// <summary>N pulsos no STEP (GPIO 13), com o DIR (GPIO 12) no nivel dado.</summary>
        public void Pulsos(int n, int dir)
        {
            Quadro();
            Maq.DigitalWrite(12, dir);
            for (int i = 0; i < n; i++)
            {
                Maq.DigitalWrite(13, 1);
                Maq.DigitalWrite(13, 0);
                if (i % 200 == 199) Quadro();
            }
            Quadro();
        }

        public double Angulo => Ed?.AnguloDoPassoParaTeste(Motor.Id) ?? 0;
        public List<Estouro> Avisos => Ed?.EstourosPelaEnergia() ?? [];
    }

    static void Passo_Drivers()
    {
        saida.AppendLine("drivers de motor de passo");

        // O A4988 do esquema da Pololu: 12 V no VMOT, 3,3 V no VDD, RST no SLP.
        BancadaDePasso A4988(double volts = 12, bool rstNoSlp = true, string[] bobinas = null, Action<BancadaDePasso> extra = null)
        {
            var b = new BancadaDePasso("a4988", "stepper-motor", volts);
            b.Ligar(b.Fonte, "+", b.Driver, "VMOT");
            b.Ligar(b.Fonte, "-", b.Driver, "GND");
            b.Ligar(b.Placa, "GND.2", b.Driver, "GND.2");
            b.Ligar(b.Placa, "3V3", b.Driver, "VDD");
            b.Ligar(b.Placa, "D13", b.Driver, "STEP");
            b.Ligar(b.Placa, "D12", b.Driver, "DIR");
            if (rstNoSlp) b.Ligar(b.Driver, "RST", b.Driver, "SLP");
            bobinas ??= ["1A", "1B", "2A", "2B"];
            string[] doMotor = ["A+", "A-", "B+", "B-"];
            for (int k = 0; k < 4; k++) b.Ligar(b.Driver, bobinas[k], b.Motor, doMotor[k]);
            extra?.Invoke(b);
            b.Saida(13);
            b.Saida(12);
            return b;
        }

        {
            var b = A4988();
            b.Pulsos(200, 1);
            saida.AppendLine($"        A4988, passo cheio: 200 pulsos -> {b.Angulo:0.0} graus");
            Conferir("A4988: MS solto = passo cheio; 200 pulsos dao uma volta do NEMA 17", Math.Abs(b.Angulo - 360) < 0.5, $"{b.Angulo:0.0}");
            b.Pulsos(100, 0);
            Conferir("A4988: DIR em LOW volta (100 pulsos = meia volta para tras)", Math.Abs(b.Angulo - 180) < 0.5, $"{b.Angulo:0.0}");
        }
        {
            var b = A4988(extra: x => x.Ligar(x.Placa, "3V3", x.Driver, "MS1"));
            b.Pulsos(200, 1);
            Conferir("A4988: MS1 em HIGH = meio passo (200 pulsos = meia volta)", Math.Abs(b.Angulo - 180) < 0.5, $"{b.Angulo:0.0}");
        }
        {
            var b = A4988(extra: x =>
            {
                foreach (string ms in new[] { "MS1", "MS2", "MS3" }) x.Ligar(x.Placa, "3V3", x.Driver, ms);
            });
            b.Pulsos(3200, 1);
            saida.AppendLine($"        A4988 a 1/16: 3200 pulsos -> {b.Angulo:0.0} graus");
            Conferir("A4988: MS1/MS2/MS3 em HIGH = 1/16 (3200 pulsos = uma volta)", Math.Abs(b.Angulo - 360) < 0.5, $"{b.Angulo:0.0}");
        }
        {
            var b = A4988(rstNoSlp: false);
            b.Pulsos(200, 1);
            var aviso = b.Avisos.FirstOrDefault(a => a.Peca == "driver");
            saida.AppendLine($"        A4988 com o RST solto: {b.Angulo:0.0} graus; {aviso?.Motivo}");
            Conferir("A4988: RST solto deixa o driver em reset (nao anda) e o aviso manda ligar no SLP",
                b.Angulo == 0 && aviso is not null && aviso.Motivo.Contains("SLP"), aviso?.Motivo ?? "(nada)");
        }
        {
            var b = A4988(volts: 5);
            b.Pulsos(200, 1);
            var aviso = b.Avisos.FirstOrDefault(a => a.Peca == "driver");
            Conferir("A4988: com 5 V no VMOT ele nao liga (minimo 8 V), e avisa",
                b.Angulo == 0 && aviso is not null && aviso.Motivo.Contains("8"), aviso?.Motivo ?? "(nada)");
        }
        {
            var b = A4988(bobinas: ["1B", "1A", "2A", "2B"]);
            b.Pulsos(200, 1);
            Conferir("A4988: uma bobina invertida inverte o giro", Math.Abs(b.Angulo + 360) < 0.5, $"{b.Angulo:0.0}");
        }
        {
            var b = A4988(bobinas: ["1A", "2A", "1B", "2B"]);
            b.Pulsos(200, 1);
            var aviso = b.Avisos.FirstOrDefault(a => a.Peca == "motor");
            Conferir("A4988: bobinas misturadas (uma ponta de cada em 1A/1B) so fazem o motor tremer, e o aviso explica",
                b.Angulo == 0 && aviso is not null && aviso.Motivo.Contains("misturadas"), aviso?.Motivo ?? "(nada)");
        }
        {
            var b = A4988(extra: x => x.Ligar(x.Placa, "D14", x.Driver, "EN"));
            b.Saida(14);
            b.Maq.DigitalWrite(14, 1);
            b.Pulsos(200, 1);
            Conferir("A4988: EN em HIGH desliga (o motor nao anda)", b.Angulo == 0, $"{b.Angulo:0.0}");
        }

        // ---- DRV8825 a 1/32 ----
        {
            var b = new BancadaDePasso("drv8825", "stepper-motor", 12);
            b.Ligar(b.Fonte, "+", b.Driver, "VMOT");
            b.Ligar(b.Fonte, "-", b.Driver, "GND");
            b.Ligar(b.Placa, "GND.2", b.Driver, "GND.2");
            b.Ligar(b.Placa, "D13", b.Driver, "STEP");
            b.Ligar(b.Placa, "D12", b.Driver, "DIR");
            b.Ligar(b.Driver, "RST", b.Driver, "SLP");
            b.Ligar(b.Placa, "3V3", b.Driver, "M0");
            b.Ligar(b.Placa, "3V3", b.Driver, "M2");
            string[] saidas = ["A1", "A2", "B1", "B2"], doMotor = ["A+", "A-", "B+", "B-"];
            for (int k = 0; k < 4; k++) b.Ligar(b.Driver, saidas[k], b.Motor, doMotor[k]);
            b.Saida(13); b.Saida(12);
            b.Pulsos(6400, 1);
            saida.AppendLine($"        DRV8825 a 1/32: 6400 pulsos -> {b.Angulo:0.0} graus");
            Conferir("DRV8825: M0 e M2 em HIGH = 1/32 (6400 pulsos = uma volta)", Math.Abs(b.Angulo - 360) < 0.5, $"{b.Angulo:0.0}");
        }

        // ---- ULN2003 e 28BYJ-48 ----
        {
            var b = new BancadaDePasso("uln2003", "motor-28byj48", 5);
            b.Ligar(b.Fonte, "+", b.Driver, "+");
            b.Ligar(b.Fonte, "-", b.Driver, "-");
            b.Ligar(b.Placa, "GND.2", b.Driver, "-");
            int[] gpios = [19, 18, 5, 17];
            string[] pinos = ["D19", "D18", "D5", "TX2"];
            for (int k = 0; k < 4; k++) b.Ligar(b.Placa, pinos[k], b.Driver, $"IN{k + 1}");
            string[] fios = ["orange", "yellow", "pink", "blue", "red"], conector = ["A", "B", "C", "D", "COM"];
            for (int k = 0; k < 5; k++) b.Ligar(b.Motor, fios[k], b.Driver, conector[k]);
            foreach (int g in gpios) b.Saida(g);
            b.Quadro();

            // Meio passo (8 fases), 4096 vezes: uma volta e pouco do eixo de saida.
            int[][] meioPasso = [[1, 0, 0, 0], [1, 1, 0, 0], [0, 1, 0, 0], [0, 1, 1, 0], [0, 0, 1, 0], [0, 0, 1, 1], [0, 0, 0, 1], [1, 0, 0, 1]];
            bool ledA = false;
            for (int i = 0; i < 4096; i++)
            {
                var fase = meioPasso[(i + 1) % 8];
                for (int k = 0; k < 4; k++) b.Maq.DigitalWrite(gpios[k], fase[k]);
                b.Maq.Avancar(1);
                if (i % 256 == 255) b.Quadro();
                if (i == 0) { b.Quadro(); ledA = b.Ed.LuzDeModuloAcesaParaTeste("driver", 0); }
            }
            b.Quadro();
            saida.AppendLine($"        28BYJ-48 no ULN2003: 4096 meios passos -> {b.Angulo:0.0} graus (2048 por volta e o arredondamento do 63,68:1)");
            Conferir("28BYJ-48: 4096 meios passos dao uma volta e 1,8 grau (a reducao e 63,68:1, nao 64)",
                Math.Abs(b.Angulo - 361.8) < 1.0, $"{b.Angulo:0.0}");
            Conferir("ULN2003: o LED da fase A acende com o IN1 em HIGH", ledA);
        }
    }
}
