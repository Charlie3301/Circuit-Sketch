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

    /// <summary>
    /// Os bracos parafusados no eixo (Alpha 25): cada servo escolhe o seu ou
    /// nenhum, os micro com os tres do saquinho e o MG996R com mais dois; o
    /// braco gira com o servo (o do 360 tambem), a roda encaixa no mesmo eixo
    /// e o diagram.json leva a escolha (o "horn" do wokwi-servo).
    /// </summary>
    static void Servos_Bracos()
    {
        saida.AppendLine("servos: os bracos (horns)");

        string[] micro = ["nenhum", "simples", "duplo", "cruz"];
        foreach (string tipo in new[] { "servo", "servo-360", "servo-mg996r" })
        {
            string[] esperadas = tipo == "servo-mg996r" ? [.. micro, "estrela", "disco"] : micro;
            var opcoes = EditorCircuito.OpcoesDeBraco(tipo).Select(o => o.Id).ToArray();
            Conferir($"{tipo}: os bracos {string.Join(", ", esperadas)}", opcoes.SequenceEqual(esperadas), string.Join(", ", opcoes));

            string esperado = tipo == "servo-360" ? "cruz" : "simples";
            string padrao = EditorCircuito.BracoDa(new PecaNoCircuito { Tipo = tipo });
            Conferir($"{tipo}: sem escolha, vem com o {esperado}", padrao == esperado, padrao);

            // Cada braco: com "nenhum" nao ha desenho por cima; com os outros, ha.
            foreach (string braco in esperadas)
            {
                var c = new Circuito();
                c.Pecas.Add(new PecaNoCircuito { Id = "s", Tipo = tipo, X = 100, Y = 100, Braco = braco });
                var ed = new EditorCircuito();
                ed.Carregar(c);
                Conferir($"{tipo}, braco {braco}: {(braco == "nenhum" ? "so o estriado" : "desenhado no eixo")}",
                    ed.BracoParaTeste("s").Existe == (braco != "nenhum"));
            }
        }

        // ---- trocar na barra: o desenho troca, e o copiar/colar leva junto ----
        {
            var c = new Circuito();
            var peca = new PecaNoCircuito { Id = "s", Tipo = "servo-mg996r", X = 100, Y = 100 };
            c.Pecas.Add(peca);
            var ed = new EditorCircuito();
            ed.Carregar(c);
            ed.SelecionarParaTeste("s");
            ed.DefinirBracoDoServo("disco");
            bool comDisco = peca.Braco == "disco" && ed.BracoParaTeste("s").Existe;
            ed.DefinirBracoDoServo("nenhum");
            bool semBraco = peca.Braco == "nenhum" && !ed.BracoParaTeste("s").Existe;
            ed.DefinirBracoDoServo("asa");
            Conferir("MG996R: trocar para o disco e para nenhum muda o desenho; um braco que nao existe e ignorado",
                comDisco && semBraco && peca.Braco == "nenhum", $"{comDisco}/{semBraco}/{peca.Braco}");
            Conferir("copiar o servo leva o braco", peca.Copia().Braco == "nenhum");
        }

        // ---- o braco gira com o servo, com a peca girada; o do 360 tambem ----
        foreach (string tipo in new[] { "servo", "servo-360" })
        {
            var c = new Circuito();
            var placa = new PecaNoCircuito { Id = "placa", Tipo = "esp32-devkit-v1", X = 0, Y = 0, Usb = true };
            var servo = new PecaNoCircuito { Id = "s", Tipo = tipo, X = 400, Y = 0, Rotacao = 90 };
            c.Pecas.AddRange([placa, servo]);
            c.Fios.Add(new Fio { DePeca = "placa", DePino = "D13", ParaPeca = "s", ParaPino = "PWM" });
            c.Fios.Add(new Fio { DePeca = "placa", DePino = "VIN", ParaPeca = "s", ParaPino = "V+" });
            c.Fios.Add(new Fio { DePeca = "placa", DePino = "GND.2", ParaPeca = "s", ParaPino = "GND" });
            var ed = new EditorCircuito { PassoFixoParaTeste = 0.02 };
            ed.Carregar(c);
            var maq = new Maquina { Perfil = PerfilPlaca.Esp32 };
            maq.Reiniciar();
            maq.PinMode(13, 1);
            var b = Simulador.App.Bancada.DeUmaSo(maq);
            var rede = new Netlist(c);
            maq.EscreverServo(13, 0, tipo == "servo-360" ? 1000 : 544); maq.Avancar(25);
            for (int i = 0; i < 25; i++) ed.AplicarEntradas(b, rede);
            ed.RefletirEstado(b, rede);
            double giro = ed.BracoParaTeste("s").Giro;
            double servoEm = ed.AnguloDoServoParaTeste("s");
            // A peca girada 90 graus: o giro do braco e o da peca mais o do servo (0 grau: para cima na peca).
            bool ok = Math.Abs(giro - (90 + servoEm - 90)) < 1e-6 && (tipo == "servo-360" ? Math.Abs(ed.VelocidadeDoServoParaTeste("s")) > 100 : Math.Abs(servoEm) < 2);
            Conferir($"{tipo}: o braco gira com o servo (e com a peca girada)", ok, $"servo {servoEm:0.#}, braco {giro:0.#}");
        }

        // ---- a roda do 360 encaixa no eixo onde o braco gira ----
        {
            var c = new Circuito();
            c.Pecas.Add(new PecaNoCircuito { Id = "s", Tipo = "servo-360", X = 100, Y = 100 });
            var ed = new EditorCircuito();
            ed.Carregar(c);
            var eixoDoBraco = ed.BracoParaTeste("s").Eixo;
            var eixoDaRoda = ed.EixoDoMotorParaTeste("s") ?? new System.Windows.Point(double.NaN, double.NaN);
            Conferir("servo 360: a roda encaixa no mesmo eixo do braco", (eixoDoBraco - eixoDaRoda).Length < 0.1,
                $"braco ({eixoDoBraco.X:0.##}, {eixoDoBraco.Y:0.##}), roda ({eixoDaRoda.X:0.##}, {eixoDaRoda.Y:0.##})");
        }

        // ---- o diagram.json: o "horn" do wokwi-servo; o resto no "braco" ----
        {
            var c = new Circuito();
            c.Pecas.Add(new PecaNoCircuito { Id = "servo1", Tipo = "servo", X = 0, Y = 0, Braco = "cruz" });
            c.Pecas.Add(new PecaNoCircuito { Id = "servo2", Tipo = "servo", X = 0, Y = 200, Braco = "nenhum" });
            c.Pecas.Add(new PecaNoCircuito { Id = "servo3", Tipo = "servo-mg996r", X = 0, Y = 400, Braco = "estrela" });
            c.Pecas.Add(new PecaNoCircuito { Id = "servo4", Tipo = "servo", X = 0, Y = 600 });
            string json = ExportarWokwi.Gerar(c);
            var volta = ImportarWokwi.Ler(json).Circuito;
            string Braco(string id) => volta?.Achar(id)?.Braco ?? "(nulo)";
            Conferir("diagram.json: o horn \"cross\" do Wokwi, o nenhum e a estrela do MG996R vao e voltam; sem escolha, nada",
                json.Contains("\"horn\": \"cross\"") && Braco("servo1") == "cruz" && Braco("servo2") == "nenhum"
                && Braco("servo3") == "estrela" && Braco("servo4") == "(nulo)",
                $"{Braco("servo1")}/{Braco("servo2")}/{Braco("servo3")}/{Braco("servo4")}");

            var deles = ImportarWokwi.Ler("""
                { "version": 1, "author": "x", "editor": "wokwi",
                  "parts": [ { "type": "wokwi-servo", "id": "servo1", "top": 0, "left": 0, "attrs": { "horn": "double", "hornColor": "black" } } ],
                  "connections": [] }
                """).Circuito?.Achar("servo1");
            Conferir("diagram.json do Wokwi: o horn \"double\" vira o braco duplo (e a cor deles fica guardada)",
                deles?.Braco == "duplo" && deles.AtributosDoWokwi?.GetValueOrDefault("hornColor") == "black",
                $"{deles?.Braco}/{deles?.AtributosDoWokwi?.GetValueOrDefault("hornColor")}");
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
