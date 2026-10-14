namespace Simulador.App;

/// <summary>
/// As pontes H e os drivers de motor DC: o que cada um faz com o motor
/// amarelo, pelas tabelas-verdade e pelas quedas dos datasheets.
/// </summary>
public static partial class Autoteste
{
    /// <summary>Uma bancada de driver: a ESP32, as pilhas, o driver e um motor amarelo.</summary>
    sealed class BancadaDeDriver
    {
        public Circuito C = new();
        public PecaNoCircuito Placa, Pilhas, Driver, Motor;
        public Maquina Maq;
        public EditorCircuito Ed;
        Netlist rede;
        Bancada bancada;

        public BancadaDeDriver(string tipo, double volts = 6, double mA = 2000)
        {
            Placa = new PecaNoCircuito { Id = "placa", Tipo = "esp32-devkit-v1", X = 0, Y = 0, Usb = true };
            Pilhas = new PecaNoCircuito { Id = "pilhas", Tipo = "bateria", X = 300, Y = 300, Volts = volts, MiliAmperes = mA };
            Driver = new PecaNoCircuito { Id = "driver", Tipo = tipo, X = 300, Y = 0 };
            Motor = new PecaNoCircuito { Id = "motor", Tipo = "motor-tt", X = 600, Y = 0 };
            C.Pecas.AddRange([Placa, Pilhas, Driver, Motor]);
            Maq = new Maquina { Perfil = PerfilPlaca.Esp32 };
            Maq.Reiniciar();
        }

        public void Ligar(PecaNoCircuito a, string pa, PecaNoCircuito b, string pb)
            => C.Fios.Add(new Fio { DePeca = a.Id, DePino = pa, ParaPeca = b.Id, ParaPino = pb });

        public void Pino(int gpio, int pwm)
        {
            Maq.Modo[gpio] = 1;
            Maq.Pwm[gpio] = pwm;
            Maq.Digital[gpio] = pwm > 127 ? 1 : 0;
        }

        public Energia Rodar(double segundos)
        {
            if (Ed is null)
            {
                Ed = new EditorCircuito();
                Ed.Carregar(C);
                bancada = Simulador.App.Bancada.DeUmaSo(Maq);
            }
            rede = new Netlist(C);
            Energia conta = null;
            for (double t = 0; t < segundos - 1e-9; t += 0.04) conta = Ed.AvancarEnergiaParaTeste(bancada, rede, 0.04);
            return conta;
        }

        public double Rpm => Ed?.RpmDoMotor(Motor.Id) ?? 0;
        public double VoltsNoMotor => Ed?.MotorDcParaTeste(Motor.Id)?.Tensao ?? 0;
        public List<Estouro> Avisos => Ed?.EstourosPelaEnergia() ?? [];
    }

    static void Drivers_PonteH()
    {
        saida.AppendLine("pontes H e drivers de motor DC");

        // ---- L298N: 6 V de pilhas, a logica nos 5 V da placa ----
        BancadaDeDriver L298n(double volts = 6, bool cincoDaPlaca = true, bool gndComum = true, bool enaNoPino = false)
        {
            var b = new BancadaDeDriver("l298n", volts);
            b.Ligar(b.Pilhas, "+", b.Driver, "12V");
            b.Ligar(b.Pilhas, "-", b.Driver, "GND");
            if (gndComum) b.Ligar(b.Placa, "GND.2", b.Driver, "GND");
            if (cincoDaPlaca) b.Ligar(b.Placa, "VIN", b.Driver, "5V");
            b.Ligar(b.Placa, "D13", b.Driver, "IN1");
            b.Ligar(b.Placa, "D12", b.Driver, "IN2");
            if (enaNoPino) b.Ligar(b.Placa, "D14", b.Driver, "ENA");
            b.Ligar(b.Driver, "OUT1", b.Motor, "+");
            b.Ligar(b.Driver, "OUT2", b.Motor, "-");
            b.Pino(13, 255);
            b.Pino(12, 0);
            if (enaNoPino) b.Pino(14, 255);
            return b;
        }

        {
            var b = L298n();
            b.Rodar(3);
            saida.AppendLine($"        L298N com 6 V, IN1 HIGH/IN2 LOW: motor com {b.VoltsNoMotor:0.00} V, {b.Rpm:0} rpm; "
                             + $"luz {(b.Ed.LuzDeModuloAcesaParaTeste("driver", 0) ? "acesa" : "apagada")}");
            Conferir("L298N: 6 V nas pilhas viram ~4,5 V no motor (as duas chaves bipolares perdem ~1,5 V) e o amarelo anda ~180 rpm",
                b.VoltsNoMotor is > 4.2 and < 4.9 && b.Rpm is > 160 and < 195, $"{b.VoltsNoMotor:0.00} V {b.Rpm:0} rpm");
            Conferir("L298N: a luz de energia acende com a logica alimentada", b.Ed.LuzDeModuloAcesaParaTeste("driver", 0));

            b.Pino(13, 0);
            b.Pino(12, 255);
            b.Rodar(3);
            Conferir("L298N: IN1 LOW/IN2 HIGH inverte o sentido", b.Rpm is < -160 and > -195, $"{b.Rpm:0}");
        }

        // Freio (IN1 = IN2) x roda livre (ENA em LOW): o freio para bem antes.
        {
            var livre = L298n(enaNoPino: true);
            livre.Rodar(2);
            livre.Pino(14, 0);
            livre.Rodar(0.12);
            var freio = L298n(enaNoPino: true);
            freio.Rodar(2);
            freio.Pino(12, 255);
            freio.Rodar(0.12);
            saida.AppendLine($"        120 ms depois: roda livre (ENA LOW) {livre.Rpm:0} rpm, freio (IN1=IN2) {freio.Rpm:0} rpm");
            Conferir("L298N: freio (IN1 = IN2) para o motor mais rapido que a roda livre (ENA em LOW)",
                freio.Rpm < livre.Rpm * 0.8 && livre.Rpm > 0, $"{freio.Rpm:0} x {livre.Rpm:0}");
        }

        // PWM no ENA: metade do tempo ligado, perto de metade da velocidade.
        {
            var cheio = L298n(enaNoPino: true);
            cheio.Rodar(3);
            var meio = L298n(enaNoPino: true);
            meio.Pino(14, 128);
            meio.Rodar(3);
            saida.AppendLine($"        ENA com PWM 128: {meio.Rpm:0} rpm ({meio.VoltsNoMotor:0.00} V); cheio {cheio.Rpm:0}");
            Conferir("L298N: PWM de 50% no ENA da perto de metade da velocidade",
                meio.Rpm > cheio.Rpm * 0.38 && meio.Rpm < cheio.Rpm * 0.62, $"{meio.Rpm:0} de {cheio.Rpm:0}");
        }

        // Sem o GND em comum: a placa manda, o L298N nao entende.
        {
            var b = L298n(gndComum: false);
            b.Rodar(1.5);
            var aviso = b.Avisos.FirstOrDefault(a => a.Peca == "driver");
            saida.AppendLine($"        sem GND em comum: {b.Rpm:0} rpm; {aviso?.Motivo}");
            Conferir("L298N: sem o GND da placa no GND do modulo o motor nao gira, e o aviso diz por que",
                b.Rpm == 0 && aviso is { Queimou: false } && aviso.Motivo.Contains("GND dele nao esta ligado"), aviso?.Motivo ?? "(nada)");
        }

        // 6 V sem os 5 V da placa: o regulador do modulo nao alcanca.
        {
            var b = L298n(cincoDaPlaca: false);
            b.Rodar(1.5);
            var aviso = b.Avisos.FirstOrDefault(a => a.Peca == "driver");
            Conferir("L298N: com 6 V e sem 5 V na logica ele nao liga (o 78M05 precisa de 7 V), e avisa",
                b.Rpm == 0 && aviso is not null && aviso.Motivo.Contains("7 V"), aviso?.Motivo ?? "(nada)");
        }

        // 12 V: o regulador faz os 5 V sozinho, e o pino 5V vira saida.
        {
            var b = L298n(volts: 12, cincoDaPlaca: false);
            var conta = b.Rodar(1.5);
            double cinco = conta.TensaoDoPino("driver", "5V") - conta.TensaoDoPino("driver", "GND");
            saida.AppendLine($"        12 V no L298N: o pino 5V tem {cinco:0.00} V; motor a {b.Rpm:0} rpm");
            Conferir("L298N: com 12 V o regulador faz os 5 V (o pino vira saida) e o motor gira sem os 5 V da placa",
                cinco is > 4.8 and < 5.1 && b.Rpm > 300, $"{cinco:0.00} V {b.Rpm:0} rpm");
        }

        // ---- L293D: 600 mA, ~1,8 V de queda ----
        {
            var b = new BancadaDeDriver("l293d");
            b.Ligar(b.Placa, "VIN", b.Driver, "VCC1");
            b.Ligar(b.Pilhas, "+", b.Driver, "VCC2");
            b.Ligar(b.Pilhas, "-", b.Driver, "GND.1");
            b.Ligar(b.Placa, "GND.2", b.Driver, "GND.4");
            b.Ligar(b.Placa, "D14", b.Driver, "1,2EN");
            b.Ligar(b.Placa, "D13", b.Driver, "1A");
            b.Ligar(b.Placa, "D12", b.Driver, "2A");
            b.Ligar(b.Driver, "1Y", b.Motor, "+");
            b.Ligar(b.Driver, "2Y", b.Motor, "-");
            b.Pino(14, 255); b.Pino(13, 255); b.Pino(12, 0);
            b.Rodar(3);
            saida.AppendLine($"        L293D: {b.VoltsNoMotor:0.00} V no motor, {b.Rpm:0} rpm");
            Conferir("L293D: liga pelos quatro GND (sao o mesmo) e o amarelo anda com ~4,2 V",
                b.VoltsNoMotor is > 3.8 and < 4.7 && b.Rpm > 140, $"{b.VoltsNoMotor:0.00} V {b.Rpm:0} rpm");
            b.Pino(14, 0);
            b.Rodar(2);
            Conferir("L293D: o 1,2EN em LOW solta o motor (roda livre ate parar)", b.Rpm == 0, $"{b.Rpm:0}");
        }

        // ---- L9110S: as saidas seguem as entradas ----
        {
            var b = new BancadaDeDriver("l9110s");
            b.Ligar(b.Pilhas, "+", b.Driver, "VCC");
            b.Ligar(b.Pilhas, "-", b.Driver, "GND");
            b.Ligar(b.Placa, "GND.2", b.Driver, "GND");
            b.Ligar(b.Placa, "D13", b.Driver, "A-IA");
            b.Ligar(b.Placa, "D12", b.Driver, "A-IB");
            b.Ligar(b.Driver, "A1", b.Motor, "+");
            b.Ligar(b.Driver, "A2", b.Motor, "-");
            b.Pino(13, 255); b.Pino(12, 0);
            b.Rodar(3);
            double frente = b.Rpm;
            b.Pino(13, 0); b.Pino(12, 255);
            b.Rodar(3);
            saida.AppendLine($"        L9110S: {frente:0} rpm, invertido {b.Rpm:0} rpm");
            Conferir("L9110S: A-IA/A-IB giram o motor para um lado e para o outro",
                frente > 200 && b.Rpm < -200, $"{frente:0} / {b.Rpm:0}");
        }

        // ---- TB6612FNG: MOSFET, quase sem queda; STBY e PWM ----
        {
            var b = new BancadaDeDriver("tb6612fng");
            b.Ligar(b.Pilhas, "+", b.Driver, "VM");
            b.Ligar(b.Pilhas, "-", b.Driver, "GND");
            b.Ligar(b.Placa, "GND.2", b.Driver, "GND.2");
            b.Ligar(b.Placa, "3V3", b.Driver, "VCC");
            b.Ligar(b.Placa, "D27", b.Driver, "STBY");
            b.Ligar(b.Placa, "D13", b.Driver, "AIN1");
            b.Ligar(b.Placa, "D12", b.Driver, "AIN2");
            b.Ligar(b.Placa, "D14", b.Driver, "PWMA");
            b.Ligar(b.Driver, "AO1", b.Motor, "+");
            b.Ligar(b.Driver, "AO2", b.Motor, "-");
            b.Pino(27, 255); b.Pino(13, 255); b.Pino(12, 0); b.Pino(14, 255);
            b.Rodar(3);
            double cheio = b.Rpm, volts = b.VoltsNoMotor;
            b.Pino(14, 128);
            b.Rodar(3);
            double meio = b.Rpm;
            b.Pino(27, 0);
            b.Rodar(2);
            saida.AppendLine($"        TB6612FNG: {volts:0.00} V e {cheio:0} rpm; PWM 50% {meio:0} rpm; STBY LOW {b.Rpm:0} rpm");
            Conferir("TB6612FNG: MOSFET quase nao perde (o amarelo com ~5,9 V, ~245 rpm)",
                volts > 5.75 && cheio > 235, $"{volts:0.00} V {cheio:0}");
            Conferir("TB6612FNG: PWMA a 50% da perto de metade da velocidade", meio > cheio * 0.38 && meio < cheio * 0.62,
                $"{meio:0} de {cheio:0}");
            Conferir("TB6612FNG: STBY em LOW desliga tudo (o motor para)", b.Rpm == 0, $"{b.Rpm:0}");
        }

        // ---- DRV8833: 1/1 freia; 12 V queima ----
        {
            var b = new BancadaDeDriver("drv8833");
            b.Ligar(b.Pilhas, "+", b.Driver, "VM");
            b.Ligar(b.Pilhas, "-", b.Driver, "GND");
            b.Ligar(b.Placa, "GND.2", b.Driver, "GND");
            b.Ligar(b.Placa, "D13", b.Driver, "AIN1");
            b.Ligar(b.Placa, "D12", b.Driver, "AIN2");
            b.Ligar(b.Driver, "AO1", b.Motor, "+");
            b.Ligar(b.Driver, "AO2", b.Motor, "-");
            b.Pino(13, 255); b.Pino(12, 0);
            b.Rodar(3);
            double gira = b.Rpm;
            b.Pino(12, 255);
            b.Rodar(0.3);
            saida.AppendLine($"        DRV8833: {gira:0} rpm; 300 ms de freio (1/1): {b.Rpm:0} rpm");
            Conferir("DRV8833: AIN1 HIGH/AIN2 LOW gira; os dois HIGH freiam ate parar", gira > 235 && b.Rpm == 0,
                $"{gira:0} / {b.Rpm:0}");

            var queimado = new BancadaDeDriver("drv8833", volts: 12);
            queimado.Ligar(queimado.Pilhas, "+", queimado.Driver, "VM");
            queimado.Ligar(queimado.Pilhas, "-", queimado.Driver, "GND");
            queimado.Rodar(0.2);
            var estouro = queimado.Avisos.FirstOrDefault(a => a.Peca == "driver");
            Conferir("DRV8833: 12 V no VM (maximo 10,8 V) queima", estouro is { Queimou: true }, estouro?.Motivo ?? "(nada)");
        }

        // ---- BTS7960 (IBT-2): cada meia ponte com o seu enable ----
        {
            var b = new BancadaDeDriver("bts7960", volts: 12, mA: 10000);
            b.Ligar(b.Pilhas, "+", b.Driver, "B+");
            b.Ligar(b.Pilhas, "-", b.Driver, "B-");
            b.Ligar(b.Placa, "VIN", b.Driver, "VCC");
            b.Ligar(b.Placa, "GND.2", b.Driver, "GND");
            b.Ligar(b.Placa, "D25", b.Driver, "R_EN");
            b.Ligar(b.Placa, "D26", b.Driver, "L_EN");
            b.Ligar(b.Placa, "D13", b.Driver, "RPWM");
            b.Ligar(b.Placa, "D12", b.Driver, "LPWM");
            b.Ligar(b.Driver, "M+", b.Motor, "+");
            b.Ligar(b.Driver, "M-", b.Motor, "-");
            b.Pino(25, 255); b.Pino(26, 255); b.Pino(13, 77); b.Pino(12, 0);
            b.Rodar(3);
            saida.AppendLine($"        BTS7960 com 12 V e RPWM 30%: {b.VoltsNoMotor:0.00} V, {b.Rpm:0} rpm");
            Conferir("BTS7960: 30% de 12 V da ~3,6 V no motor", b.VoltsNoMotor is > 3.3 and < 3.9 && b.Rpm > 100,
                $"{b.VoltsNoMotor:0.00} V {b.Rpm:0} rpm");
        }
    }
}
