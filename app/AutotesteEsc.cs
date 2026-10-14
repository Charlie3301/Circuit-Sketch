namespace Simulador.App;

/// <summary>
/// O ESC com o brushless A2212 numa LiPo 3S: armar, calibrar, acelerar,
/// inverter o sentido trocando dois fios, perder o sinal e o BEC.
/// </summary>
public static partial class Autoteste
{
    static void Esc_Brushless()
    {
        saida.AppendLine("ESC e motor brushless");

        (EditorCircuito Ed, Maquina Maq, Bancada B, Netlist Rede, Circuito C) Montar(double usAoLigar, bool fasesTrocadas = false)
        {
            var c = new Circuito();
            var placa = new PecaNoCircuito { Id = "placa", Tipo = "esp32-devkit-v1", X = 0, Y = 0, Usb = true };
            var esc = new PecaNoCircuito { Id = "esc", Tipo = "esc", X = 300, Y = 0 };
            var lipo = new PecaNoCircuito { Id = "lipo", Tipo = "bateria", X = 600, Y = 200, Volts = 11.1, MiliAmperes = 55000 };
            var motor = new PecaNoCircuito { Id = "motor", Tipo = "motor-brushless", X = 0, Y = 300 };
            c.Pecas.AddRange([placa, esc, lipo, motor]);
            void Ligar(PecaNoCircuito a, string pa, PecaNoCircuito d, string pd)
                => c.Fios.Add(new Fio { DePeca = a.Id, DePino = pa, ParaPeca = d.Id, ParaPino = pd });
            Ligar(lipo, "+", esc, "BAT+");
            Ligar(lipo, "-", esc, "BAT-");
            Ligar(placa, "GND.2", esc, "GND");
            Ligar(placa, "D13", esc, "SINAL");
            Ligar(esc, "A", motor, "A");
            Ligar(esc, "B", motor, fasesTrocadas ? "C" : "B");
            Ligar(esc, "C", motor, fasesTrocadas ? "B" : "C");
            var ed = new EditorCircuito { PassoFixoParaTeste = 0.02 };
            ed.Carregar(c);
            var maq = new Maquina { Perfil = PerfilPlaca.Esp32 };
            maq.Reiniciar();
            maq.PinMode(13, 1);
            maq.EscreverServo(13, 0, usAoLigar);
            maq.Avancar(25);
            return (ed, maq, Simulador.App.Bancada.DeUmaSo(maq), new Netlist(c), c);
        }

        static List<int> Rodar(EditorCircuito ed, Bancada b, Netlist rede, double segundos)
        {
            var tons = new List<int>();
            for (double t = 0; t < segundos - 1e-9; t += 0.02)
            {
                ed.AplicarEntradas(b, rede);
                int tom = ed.TomDosEscs();
                if (tom > 0 && (tons.Count == 0 || tons[^1] != tom)) tons.Add(tom);
            }
            return tons;
        }

        static void Acelerar(Maquina maq, double us) { maq.EscreverServo(13, 0, us); maq.Avancar(25); }

        // ---- liga com o acelerador embaixo: musica, arma, acelera ----
        {
            var (ed, maq, b, rede, _) = Montar(1000);
            var tons = Rodar(ed, b, rede, 1.5);
            var (fase, _) = ed.EscParaTeste("esc");
            saida.AppendLine($"        LiPo 3S, acelerador em 1000 us: tons {string.Join(" ", tons)} Hz; {fase}");
            Conferir("ESC: liga tocando os tres bipes subindo (880, 988, 1109 Hz) pelo motor",
                tons.Take(3).SequenceEqual([880, 988, 1109]), string.Join(" ", tons));
            Conferir("ESC: com o acelerador embaixo ele arma (e da o bipe longo de armado)",
                fase == "Armado" && tons.Contains(1175), $"{fase} / {string.Join(" ", tons)}");

            Acelerar(maq, 1460);
            Rodar(ed, b, rede, 1.0);
            double meio = ed.RpmDoMotor("motor");
            double corrente = ed.EnergiaDoQuadro.CorrenteDaBateria("lipo");
            Acelerar(maq, 2000);
            Rodar(ed, b, rede, 1.0);
            double cheio = ed.RpmDoMotor("motor");
            saida.AppendLine($"        acelerador 50%: {meio:0} rpm ({corrente:0.00} A da bateria, sem helice); 100%: {cheio:0} rpm");
            Conferir("ESC: metade do acelerador da ~metade de 1000 KV x 11,1 V (sem helice)",
                meio is > 4800 and < 5700, $"{meio:0}");
            Conferir("ESC: acelerador no maximo, ~11.000 rpm (1000 KV)", cheio is > 10_300 and < 11_150, $"{cheio:0}");
            Conferir("ESC: a bateria entrega a corrente do motor (a vazio, alguns decimos de ampere)",
                corrente is > 0.1 and < 1.5, $"{corrente:0.00} A");

            // O sinal some (detach): para e desarma.
            maq.SoltarServo(13);
            Rodar(ed, b, rede, 0.4);
            var (depois, acel) = ed.EscParaTeste("esc");
            Conferir("ESC: sem sinal por 250 ms ele corta o motor e desarma (failsafe)",
                depois == "AguardandoBaixo" && acel == 0, $"{depois} {acel}");
        }

        // ---- liga com o acelerador no meio: nao arma ----
        {
            var (ed, maq, b, rede, _) = Montar(1500);
            Rodar(ed, b, rede, 2.0);
            var (fase, _) = ed.EscParaTeste("esc");
            var aviso = ed.EstourosPelaEnergia().FirstOrDefault(e => e.Peca == "esc");
            saida.AppendLine($"        ligado com 1500 us: {fase}; {aviso?.Motivo}");
            Conferir("ESC: ligado com o acelerador no meio ele nao arma (a helice nao sai girando), e o aviso explica",
                fase == "AguardandoBaixo" && ed.RpmDoMotor("motor") == 0 && aviso is not null && aviso.Motivo.Contains("1000"),
                $"{fase} {aviso?.Motivo}");
            Acelerar(maq, 1000);
            Rodar(ed, b, rede, 0.8);
            Conferir("ESC: baixando o acelerador, arma", ed.EscParaTeste("esc").Fase == "Armado", ed.EscParaTeste("esc").Fase);
        }

        // ---- liga com o acelerador no maximo: calibracao ----
        {
            var (ed, maq, b, rede, _) = Montar(2000);
            Rodar(ed, b, rede, 1.0);
            string calibrando = ed.EscParaTeste("esc").Fase;
            Acelerar(maq, 1000);
            Rodar(ed, b, rede, 0.2);
            Conferir("ESC: acelerador no maximo ao ligar entra na calibracao; no minimo, arma",
                calibrando == "Calibrando" && ed.EscParaTeste("esc").Fase == "Armado", $"{calibrando} -> {ed.EscParaTeste("esc").Fase}");
        }

        // ---- dois fios trocados: o motor gira para o outro lado ----
        {
            var (ed, maq, b, rede, _) = Montar(1000, fasesTrocadas: true);
            Rodar(ed, b, rede, 1.5);
            Acelerar(maq, 1460);
            Rodar(ed, b, rede, 1.0);
            double rpm = ed.RpmDoMotor("motor");
            Conferir("ESC: trocar dois fios do motor inverte o giro", rpm < -4800, $"{rpm:0}");
        }

        // ---- o BEC: 5 V no fio vermelho do cabo de servo ----
        {
            var (ed, _, b, rede, _) = Montar(1000);
            Rodar(ed, b, rede, 0.2);
            var conta = ed.EnergiaDoQuadro;
            double bec = conta.TensaoDoPino("esc", "5V") - conta.TensaoDoPino("esc", "GND");
            saida.AppendLine($"        BEC: {bec:0.00} V");
            Conferir("ESC: o BEC poe 5 V no cabo de servo (da para alimentar o receptor ou a placa)", bec is > 4.8 and < 5.1, $"{bec:0.00}");
        }
    }
}
