namespace Simulador.App;

/// <summary>
/// Os servos como na bancada: o pulso no fio, a velocidade do datasheet, a
/// alimentacao no V+ e a corrente que muda com o que ele esta fazendo.
/// </summary>
public static partial class Autoteste
{
    static void Servos_Fisicos()
    {
        saida.AppendLine("servos: pulso, velocidade e alimentacao");

        (EditorCircuito Ed, Maquina Maq, Bancada B, Netlist Rede, PecaNoCircuito Servo) Montar(
            string tipo, bool comV = true, bool gndComum = true, double? bateria = null)
        {
            var c = new Circuito();
            var placa = new PecaNoCircuito { Id = "placa", Tipo = "esp32-devkit-v1", X = 0, Y = 0, Usb = true };
            var servo = new PecaNoCircuito { Id = "servo", Tipo = tipo, X = 400, Y = 0 };
            c.Pecas.AddRange([placa, servo]);
            void Ligar(PecaNoCircuito a, string pa, PecaNoCircuito b, string pb)
                => c.Fios.Add(new Fio { DePeca = a.Id, DePino = pa, ParaPeca = b.Id, ParaPino = pb });
            Ligar(placa, "D13", servo, "PWM");
            if (bateria is { } v)
            {
                var fonte = new PecaNoCircuito { Id = "fonte", Tipo = "bateria", X = 300, Y = 300, Volts = v, MiliAmperes = 3000 };
                c.Pecas.Add(fonte);
                Ligar(fonte, "+", servo, "V+");
                Ligar(fonte, "-", servo, "GND");
                if (gndComum) Ligar(placa, "GND.2", fonte, "-");
            }
            else
            {
                if (comV) Ligar(placa, "VIN", servo, "V+");
                if (gndComum) Ligar(placa, "GND.2", servo, "GND");
            }
            var ed = new EditorCircuito { PassoFixoParaTeste = 0.02 };
            ed.Carregar(c);
            var maq = new Maquina { Perfil = PerfilPlaca.Esp32 };
            maq.Reiniciar();
            maq.PinMode(13, 1);
            return (ed, maq, Simulador.App.Bancada.DeUmaSo(maq), new Netlist(c), servo);
        }

        static void Rodar(EditorCircuito ed, Bancada b, Netlist rede, double segundos)
        {
            for (double t = 0; t < segundos - 1e-9; t += 0.02) ed.AplicarEntradas(b, rede);
        }

        // ---- SG90 no 5 V da placa: 0,1 s por 60 graus ----
        {
            var (ed, maq, b, rede, servo) = Montar("servo");
            maq.EscreverServo(13, 90, 544 + 90 * (2400 - 544) / 180.0); maq.Avancar(25);
            Rodar(ed, b, rede, 0.3);
            double inicio = ed.AnguloDoServoParaTeste("servo");
            maq.EscreverServo(13, 0, 544); maq.Avancar(25);
            Rodar(ed, b, rede, 0.1);
            double em100ms = ed.AnguloDoServoParaTeste("servo");
            var conta = ed.EnergiaDoQuadro;
            double girando = conta.CorrenteDaPeca("servo") * 1000;
            Rodar(ed, b, rede, 0.3);
            double fim = ed.AnguloDoServoParaTeste("servo");
            double parado = ed.EnergiaDoQuadro.CorrenteDaPeca("servo") * 1000;
            saida.AppendLine($"        SG90 com 5 V: 90 -> 0 graus; em 100 ms estava em {em100ms:0} graus, chegou em {fim:0}; "
                             + $"{girando:0} mA girando, {parado:0} mA parado");
            Conferir("SG90: anda ~60 graus em 100 ms (0,1 s/60 graus do datasheet), sem pular para o alvo",
                Math.Abs(inicio - 90) < 0.5 && em100ms is > 20 and < 40 && fim == 0, $"{inicio:0}/{em100ms:0}/{fim:0}");
            Conferir("SG90: puxa ~220 mA girando e quase nada parado", girando is > 150 and < 300 && parado < 40,
                $"{girando:0} / {parado:0} mA");
        }

        // ---- sem V+: recebe o pulso e nao sai do lugar ----
        {
            var (ed, maq, b, rede, _) = Montar("servo", comV: false);
            maq.EscreverServo(13, 0, 544); maq.Avancar(25);
            Rodar(ed, b, rede, 0.5);
            var aviso = ed.AvisosDeServoParaTeste.FirstOrDefault();
            Conferir("SG90: sem nada no V+ o servo nao gira, e o aviso diz o que falta",
                ed.AnguloDoServoParaTeste("servo") == 90 && aviso is not null && aviso.Motivo.Contains("V+"), aviso?.Motivo ?? "(nada)");
        }

        // ---- ledcWrite a 50 Hz tambem e servo ----
        {
            var (ed, maq, b, rede, _) = Montar("servo");
            maq.AnalogWrite(13, 7, 50);     // 7/255 de 20 ms = 549 us: o canto do 0 grau
            Rodar(ed, b, rede, 0.5);
            double angulo = ed.AnguloDoServoParaTeste("servo");
            saida.AppendLine($"        SG90 por ledcWrite a 50 Hz (549 us): {angulo:0.0} graus");
            Conferir("SG90: um PWM de 50 Hz do ledc move o servo pela largura do pulso (antes nem se mexia)",
                angulo < 2, $"{angulo:0.0}");
        }

        // ---- MG996R numa bateria de 6 V, GND em comum ----
        {
            var (ed, maq, b, rede, _) = Montar("servo-mg996r", bateria: 6);
            maq.EscreverServo(13, 0, 544); maq.Avancar(25);
            Rodar(ed, b, rede, 0.1);
            double em100ms = ed.AnguloDoServoParaTeste("servo");
            saida.AppendLine($"        MG996R com 6 V: em 100 ms foi de 90 a {em100ms:0} graus");
            Conferir("MG996R: mais lento que o SG90 (0,14 s/60 graus a 6 V: anda ~43 graus em 100 ms)",
                em100ms is > 40 and < 52, $"{em100ms:0}");

            var (ed2, maq2, b2, rede2, _) = Montar("servo-mg996r", bateria: 6, gndComum: false);
            maq2.EscreverServo(13, 0, 544); maq2.Avancar(25);
            Rodar(ed2, b2, rede2, 0.3);
            var aviso = ed2.AvisosDeServoParaTeste.FirstOrDefault();
            Conferir("MG996R: sem o GND da bateria no GND da placa o pulso nao chega, e avisa",
                ed2.AnguloDoServoParaTeste("servo") == 90 && aviso is not null && aviso.Motivo.Contains("GND"), aviso?.Motivo ?? "(nada)");
        }

        // ---- 360: 1500 para, 1000 e 2000 giram para lados opostos ----
        {
            var (ed, maq, b, rede, _) = Montar("servo-360");
            maq.EscreverServo(13, 90, 1500); maq.Avancar(25);
            Rodar(ed, b, rede, 0.2);
            double parado = ed.VelocidadeDoServoParaTeste("servo");
            maq.EscreverServo(13, 0, 1000); maq.Avancar(25);
            Rodar(ed, b, rede, 0.2);
            double horario = ed.VelocidadeDoServoParaTeste("servo") / 6;
            maq.EscreverServo(13, 180, 2000); maq.Avancar(25);
            Rodar(ed, b, rede, 0.2);
            double antiHorario = ed.VelocidadeDoServoParaTeste("servo") / 6;
            saida.AppendLine($"        360: 1500 us {parado:0}; 1000 us {horario:0} rpm; 2000 us {antiHorario:0} rpm");
            Conferir("servo 360: 1500 us para; 1000 e 2000 us giram em sentidos opostos (~115 rpm com 5 V)",
                parado == 0 && horario is > 100 and < 130 && antiHorario is < -100 and > -130, $"{parado}/{horario:0}/{antiHorario:0}");
        }
    }

    static void Chave_FimDeCurso()
    {
        saida.AppendLine("chave fim de curso (micro switch)");

        // ---- no GPIO: COM no GND, NO no pino com INPUT_PULLUP ----
        {
            var c = new Circuito();
            var placa = new PecaNoCircuito { Id = "placa", Tipo = "esp32-devkit-v1", X = 0, Y = 0, Usb = true };
            var chave = new PecaNoCircuito { Id = "chave", Tipo = "micro-switch", X = 400, Y = 0 };
            c.Pecas.AddRange([placa, chave]);
            c.Fios.Add(new Fio { DePeca = placa.Id, DePino = "GND.2", ParaPeca = chave.Id, ParaPino = "COM" });
            c.Fios.Add(new Fio { DePeca = placa.Id, DePino = "D14", ParaPeca = chave.Id, ParaPino = "NO" });
            var ed = new EditorCircuito { PassoFixoParaTeste = 0.02 };
            ed.Carregar(c);
            var maq = new Maquina { Perfil = PerfilPlaca.Esp32 };
            maq.Reiniciar();
            maq.PinMode(14, 2);
            var b = Simulador.App.Bancada.DeUmaSo(maq);
            var rede = new Netlist(c);
            ed.AplicarEntradas(b, rede);
            int solta = maq.DigitalRead(14);
            ed.BotoesPressionados.Add(chave.Id);
            ed.AplicarEntradas(b, rede);
            int apertada = maq.DigitalRead(14);
            ed.RefletirEstado(b, rede);
            double alavanca = ed.MovimentoParaTeste(chave.Id).Angulo;
            ed.BotoesPressionados.Remove(chave.Id);
            ed.AplicarEntradas(b, rede);
            int soltaDeNovo = maq.DigitalRead(14);
            saida.AppendLine($"        NO no GPIO 14 (pull-up): solta {solta}, apertada {apertada}, solta {soltaDeNovo}; alavanca {alavanca:0} graus");
            Conferir("fim de curso: COM no GND e NO no pino com INPUT_PULLUP -- HIGH solta, LOW apertada",
                solta == 1 && apertada == 0 && soltaDeNovo == 1, $"{solta}{apertada}{soltaDeNovo}");
            Conferir("fim de curso: a alavanca desce quando aperta", alavanca > 3, $"{alavanca:0.0}");
        }

        // ---- na bateria: o NC acende um LED solto, o NO acende outro apertado ----
        {
            var c = new Circuito();
            var bat = new PecaNoCircuito { Id = "bat", Tipo = "bateria", X = 0, Y = 200, Volts = 9 };
            var chave = new PecaNoCircuito { Id = "chave", Tipo = "micro-switch", X = 200, Y = 0 };
            var rNc = new PecaNoCircuito { Id = "rnc", Tipo = "resistor", X = 350, Y = 0, Ohms = 330 };
            var rNo = new PecaNoCircuito { Id = "rno", Tipo = "resistor", X = 350, Y = 100, Ohms = 330 };
            var ledNc = new PecaNoCircuito { Id = "ledNc", Tipo = "led", X = 500, Y = 0 };
            var ledNo = new PecaNoCircuito { Id = "ledNo", Tipo = "led", X = 500, Y = 100, Cor = "verde" };
            c.Pecas.AddRange([bat, chave, rNc, rNo, ledNc, ledNo]);
            void Ligar(PecaNoCircuito a, string pa, PecaNoCircuito d, string pd)
                => c.Fios.Add(new Fio { DePeca = a.Id, DePino = pa, ParaPeca = d.Id, ParaPino = pd });
            Ligar(bat, "+", chave, "COM");
            Ligar(chave, "NC", rNc, "1"); Ligar(rNc, "2", ledNc, "A"); Ligar(ledNc, "C", bat, "-");
            Ligar(chave, "NO", rNo, "1"); Ligar(rNo, "2", ledNo, "A"); Ligar(ledNo, "C", bat, "-");
            var ed = new EditorCircuito { PassoFixoParaTeste = 0.02 };
            ed.Carregar(c);
            var b = Simulador.App.Bancada.DeUmaSo(new Maquina { Perfil = PerfilPlaca.Esp32 });
            var rede = new Netlist(c);
            ed.AplicarEntradas(b, rede);
            ed.RefletirEstado(b, rede);
            bool soltaNc = ed.BrilhoParaTeste("ledNc") > 0.5, soltaNo = ed.BrilhoParaTeste("ledNo") > 0.5;
            ed.BotoesPressionados.Add(chave.Id);
            ed.AplicarEntradas(b, rede);
            ed.RefletirEstado(b, rede);
            bool apertadaNc = ed.BrilhoParaTeste("ledNc") > 0.5, apertadaNo = ed.BrilhoParaTeste("ledNo") > 0.5;
            Conferir("fim de curso na bateria: solta acende o LED do NC; apertada, o do NO",
                soltaNc && !soltaNo && !apertadaNc && apertadaNo, $"{soltaNc}{soltaNo}{apertadaNc}{apertadaNo}");
        }
    }
}
