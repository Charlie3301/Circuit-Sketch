namespace Simulador.App;

/// <summary>
/// Os motores DC da Alpha 23: o 130, o amarelo TT e o brushless A2212.
///
/// A fisica e conferida contra os numeros publicados (a Adafruit mediu o
/// amarelo em 3, 4,5 e 6 V; o 130 e o A2212 tem datasheet), e o circuito
/// contra o que acontece na bancada: a bateria gira, a chave e o rele
/// ligam e desligam, o motor no GPIO nao sai do lugar.
/// </summary>
public static partial class Autoteste
{
    static void MotorDc_Fisica()
    {
        saida.AppendLine("motores DC: a fisica");

        static MotorDc Rodar(ModeloDeMotor modelo, double volts, double segundos, double passo = 0.04)
        {
            var m = new MotorDc(modelo);
            for (double t = 0; t < segundos - 1e-9; t += passo) m.Avancar(volts, passo);
            return m;
        }

        // ---- o amarelo (TT 1:48), pelas medidas da Adafruit ----
        var tt6 = Rodar(ModeloDeMotor.MotorTT, 6, 3);
        var tt45 = Rodar(ModeloDeMotor.MotorTT, 4.5, 3);
        var tt3 = Rodar(ModeloDeMotor.MotorTT, 3, 3);
        saida.AppendLine($"        TT sem carga: 6 V {tt6.Rpm:0} rpm {tt6.Corrente * 1000:0} mA | "
                         + $"4,5 V {tt45.Rpm:0} rpm | 3 V {tt3.Rpm:0} rpm {tt3.Corrente * 1000:0} mA");
        Conferir("TT: 6 V sem carga da ~250 rpm e ~160 mA (Adafruit: 250 rpm, 160 mA)",
            tt6.Rpm is > 240 and < 260 && tt6.Corrente is > 0.14 and < 0.18, $"{tt6.Rpm:0} rpm {tt6.Corrente:0.000} A");
        Conferir("TT: 4,5 V da ~185 rpm (Adafruit: 185)", tt45.Rpm is > 170 and < 195, $"{tt45.Rpm:0}");
        Conferir("TT: 3 V da entre 90 e 125 rpm (Adafruit: 120 medido, 90 no minimo)",
            tt3.Rpm is > 90 and < 125, $"{tt3.Rpm:0}");

        var tt = ModeloDeMotor.MotorTT;
        double travado = (tt.Kt * 6 / tt.Resistencia - tt.AtritoSeco) / 0.0980665;
        saida.AppendLine($"        TT travado com 6 V: {tt.CorrenteTravado(6):0.00} A, {travado:0.00} kg.cm; "
                         + $"parte com {tt.TensaoDePartida:0.00} V");
        Conferir("TT: travado com 6 V puxa 1,5 A e faz ~0,8 kg.cm (datasheet)",
            Math.Abs(tt.CorrenteTravado(6) - 1.5) < 0.01 && travado is > 0.7 and < 0.9, $"{travado:0.00}");

        // A partida: ~0,37 s ate 95% (constante de tempo de ~0,12 s).
        var subindo = new MotorDc(tt);
        double t90 = -1;
        for (double t = 0; t < 2; t += 0.005)
        {
            subindo.Avancar(6, 0.005);
            if (t90 < 0 && subindo.Rpm >= 225) t90 = t + 0.005;
        }
        saida.AppendLine($"        TT de 0 a 90% (225 rpm): {t90 * 1000:0} ms");
        Conferir("TT: chega a 90% da velocidade entre 0,2 e 0,45 s", t90 is > 0.2 and < 0.45, $"{t90:0.000}");

        // A conta e exata: um passo de 3 s da o mesmo que 75 de 40 ms.
        var umPasso = new MotorDc(tt);
        umPasso.Avancar(6, 3);
        Conferir("TT: um passo longo da o mesmo que muitos curtos (conta exata, nao Euler)",
            Math.Abs(umPasso.Rpm - tt6.Rpm) < 0.5 && Math.Abs(umPasso.Angulo - tt6.Angulo) < 0.01 * tt6.Angulo,
            $"{umPasso.Rpm:0.0} x {tt6.Rpm:0.0} rpm, {umPasso.Angulo:0} x {tt6.Angulo:0} graus");

        var tras = Rodar(tt, -6, 3);
        Conferir("TT: polaridade invertida gira para tras", tras.Rpm is < -240 and > -260 && tras.Angulo < 0,
            $"{tras.Rpm:0} rpm, {tras.Angulo:0} graus");

        // Invertido com ele girando: passa pelo zero e acelera do outro lado.
        var virando = Rodar(tt, 6, 1);
        for (int k = 0; k < 50; k++) virando.Avancar(-6, 0.04);
        Conferir("TT: inverter com ele girando freia, passa pelo zero e gira para tras",
            virando.Rpm < -240, $"{virando.Rpm:0}");

        // ---- o 130 (Adafruit 711) ----
        var m130 = Rodar(ModeloDeMotor.Motor130, 6, 1.5, 0.01);
        saida.AppendLine($"        130 com 6 V: {m130.Rpm:0} rpm, {m130.Corrente * 1000:0} mA; "
                         + $"parte com {ModeloDeMotor.Motor130.TensaoDePartida:0.00} V");
        Conferir("130: 6 V sem carga da ~9100 rpm e ~70 mA (datasheet)",
            m130.Rpm is > 8600 and < 9600 && m130.Corrente is > 0.06 and < 0.08,
            $"{m130.Rpm:0} rpm {m130.Corrente:0.000} A");

        var fraco = Rodar(ModeloDeMotor.Motor130, 1.9, 1);
        var partiu = Rodar(ModeloDeMotor.Motor130, 2.1, 1);
        Conferir("130: com 1,9 V o eixo parado nao sai do lugar; com 2,1 V sai (partida de 2,0 V do datasheet)",
            fraco.Rpm == 0 && partiu.Rpm > 100, $"{fraco.Rpm:0} / {partiu.Rpm:0}");

        // Girando, ele continua bem abaixo da tensao de partida: so o atrito
        // de quem ja esta andando, que e menor.
        var embalado = Rodar(ModeloDeMotor.Motor130, 3, 1, 0.01);
        for (int k = 0; k < 100; k++) embalado.Avancar(1.0, 0.01);
        double com1V = embalado.Rpm;
        for (int k = 0; k < 200; k++) embalado.Avancar(0.5, 0.01);
        saida.AppendLine($"        130 embalado: com 1,0 V segue a {com1V:0} rpm; com 0,5 V para ({embalado.Rpm:0})");
        Conferir("130: embalado, segue girando com 1,0 V (abaixo da partida) e para com 0,5 V",
            com1V > 300 && embalado.Rpm == 0, $"{com1V:0} / {embalado.Rpm:0}");

        // ---- o brushless atras de um ESC: 1000 rpm por volt ----
        var bl = new MotorSemEscova();
        for (int k = 0; k < 100; k++) bl.Acelerar(11.1, 0.01);
        saida.AppendLine($"        A2212 com 11,1 V: {bl.Rpm:0} rpm, {bl.Equivalente.Corrente:0.00} A sem carga");
        Conferir("A2212: com 11,1 V sem carga gira perto de 11.100 rpm (1000 KV) e puxa ~0,5 A",
            bl.Rpm is > 10_300 and < 11_100 && bl.Equivalente.Corrente is > 0.35 and < 0.7,
            $"{bl.Rpm:0} rpm {bl.Equivalente.Corrente:0.00} A");

        // ---- o brushless pelas fases: o rotor segue o campo ----
        // Seis passos, 2 A: cada passo leva o campo 60 graus eletricos, e
        // com 7 pares de polos uma volta eletrica e 1/7 de volta do eixo.
        (double a, double b, double c)[] seis =
        [
            (2, -2, 0), (2, 0, -2), (0, 2, -2), (-2, 2, 0), (-2, 0, 2), (0, -2, 2),
        ];
        static double Girar((double a, double b, double c)[] passos, int voltas, double escala = 1)
        {
            // O primeiro so cria o campo; dali, voltas x 6 passos.
            var m = new MotorSemEscova();
            m.SeguirCampo(passos[0].a * escala, passos[0].b * escala, passos[0].c * escala, 0.01);
            for (int k = 1; k <= voltas * passos.Length; k++)
            {
                var (a, b, c) = passos[k % passos.Length];
                m.SeguirCampo(a * escala, b * escala, c * escala, 0.01);
            }
            return m.Angulo;
        }
        double frente = Girar(seis, 7);
        double paraTras = Girar([.. Enumerable.Reverse(seis)], 7);
        double fraca = Girar(seis, 7, 0.02);
        saida.AppendLine($"        A2212 pelas fases: 7 voltas eletricas = {frente:0.0} graus; ao contrario {paraTras:0.0}; "
                         + $"com 40 mA {fraca:0.0}");
        Conferir("A2212: seis passos com 2 A, 7 voltas eletricas = uma volta do eixo (7 pares de polos)",
            Math.Abs(frente - 360) < 0.5, $"{frente:0.00}");
        Conferir("A2212: a sequencia invertida gira para tras", Math.Abs(paraTras + 360) < 0.5, $"{paraTras:0.00}");
        Conferir("A2212: com a corrente de um GPIO (40 mA) o rotor nem se mexe", fraca == 0, $"{fraca:0.00}");

        var avesso = new MotorSemEscova();
        avesso.SeguirCampo(2, -2, 0, 0.01);
        avesso.SeguirCampo(-2, 2, 0, 0.01);
        Conferir("A2212: campo virado do avesso (180 graus eletricos) nao tem lado para ir: o rotor treme no lugar",
            avesso.Angulo == 0, $"{avesso.Angulo:0.00}");

        // ---- o desenho: no sentido certo, com teto ----
        Conferir("tela: ate 3 voltas por segundo o desenho gira na velocidade de verdade",
            MotorDc.GirosNaTela(2, 5) == 2 && MotorDc.GirosNaTela(-2, 5) == -2);
        Conferir("tela: o 130 a 9100 rpm (151 voltas/s) gira no teto, no mesmo sentido",
            MotorDc.GirosNaTela(151, 5) == 5 && MotorDc.GirosNaTela(-151, 5) == -5);
        Conferir("tela: comprimido, mas mais rapido continua mais rapido",
            MotorDc.GirosNaTela(4, 5) < MotorDc.GirosNaTela(6, 5) && MotorDc.GirosNaTela(4, 5) > 3);
    }

    static void MotorDc_NoCircuito()
    {
        saida.AppendLine("motores DC no circuito");

        static void Ligar(Circuito c, PecaNoCircuito a, string pa, PecaNoCircuito b, string pb)
            => c.Fios.Add(new Fio { DePeca = a.Id, DePino = pa, ParaPeca = b.Id, ParaPino = pb });

        // Roda os motores do editor com passo fixo, sem relogio de parede.
        static void Rodar(EditorCircuito ed, Bancada bancada, Netlist rede, double segundos)
        {
            for (double t = 0; t < segundos - 1e-9; t += 0.04) ed.AvancarMotores(bancada, rede, 0.04);
        }

        static Maquina Ligada(params int[] emAlta)
        {
            var maq = new Maquina { Perfil = PerfilPlaca.Esp32 };
            maq.Reiniciar();
            foreach (int p in emAlta) { maq.Modo[p] = 1; maq.Pwm[p] = 255; maq.Digital[p] = 1; }
            return maq;
        }

        // ---- bateria direto no amarelo ----
        {
            var c = new Circuito();
            var bat = new PecaNoCircuito { Tipo = "bateria", X = 0, Y = 0, Volts = 6, MiliAmperes = 2000 };
            var motor = new PecaNoCircuito { Tipo = "motor-tt", X = 300, Y = 0 };
            c.Pecas.AddRange([bat, motor]);
            Ligar(c, bat, "+", motor, "+");
            Ligar(c, bat, "-", motor, "-");

            var ed = new EditorCircuito();
            ed.Carregar(c);
            var bancada = Simulador.App.Bancada.DeUmaSo(new Maquina { Perfil = PerfilPlaca.Esp32 });
            var rede = new Netlist(c);
            Rodar(ed, bancada, rede, 3);
            double rpm = ed.RpmDoMotor(motor.Id);
            var antes = ed.MovimentoParaTeste(motor.Id).Angulo;
            ed.AvancarMotores(bancada, rede, 0.04);
            ed.AnimarParaTeste(motor.Id);
            var depois = ed.MovimentoParaTeste(motor.Id).Angulo;
            string rotulo = ed.RotuloDeGiroParaTeste(motor.Id);
            saida.AppendLine($"        pilhas de 6 V no amarelo: {rpm:0} rpm, rotulo '{rotulo}', "
                             + $"{ed.MotorDcParaTeste(motor.Id)?.Tensao:0.00} V nos fios");
            Conferir("bateria de 6 V direto no amarelo: ~250 rpm (a resistencia interna da bateria come um pouco)",
                rpm is > 235 and < 255, $"{rpm:0}");
            Conferir("o eixo do desenho gira", Math.Abs(depois - antes) > 1, $"{antes:0.0} -> {depois:0.0}");
            Conferir("o rotulo mostra o rpm, no sentido horario", rotulo is not null && rotulo.StartsWith("↻") && rotulo.EndsWith("rpm"),
                rotulo ?? "(nenhum)");

            // Os fios trocados: gira para o outro lado.
            c.Fios.Clear();
            Ligar(c, bat, "+", motor, "-");
            Ligar(c, bat, "-", motor, "+");
            rede = new Netlist(c);
            Rodar(ed, bancada, rede, 3);
            rotulo = ed.RotuloDeGiroParaTeste(motor.Id);
            Conferir("fios trocados: o amarelo gira para tras, e o rotulo diz anti-horario",
                ed.RpmDoMotor(motor.Id) < -235 && rotulo is not null && rotulo.StartsWith("↺"),
                $"{ed.RpmDoMotor(motor.Id):0} '{rotulo}'");

            // Sem fio nenhum: freia ate parar, e o rotulo some.
            c.Fios.Clear();
            rede = new Netlist(c);
            Rodar(ed, bancada, rede, 2);
            Conferir("sem bateria o motor para, e o rotulo some",
                ed.RpmDoMotor(motor.Id) == 0 && ed.RotuloDeGiroParaTeste(motor.Id) is null,
                $"{ed.RpmDoMotor(motor.Id):0}");
        }

        // ---- pelo botao, pela chave deslizante ----
        {
            var c = new Circuito();
            var bat = new PecaNoCircuito { Tipo = "bateria", X = 0, Y = 0, Volts = 6, MiliAmperes = 2000 };
            var botao = new PecaNoCircuito { Tipo = "pushbutton", X = 200, Y = 0 };
            var motor = new PecaNoCircuito { Tipo = "motor-tt", X = 400, Y = 0 };
            c.Pecas.AddRange([bat, botao, motor]);
            Ligar(c, bat, "+", botao, "1.l");
            Ligar(c, botao, "2.l", motor, "+");
            Ligar(c, motor, "-", bat, "-");

            var ed = new EditorCircuito();
            ed.Carregar(c);
            var bancada = Simulador.App.Bancada.DeUmaSo(new Maquina { Perfil = PerfilPlaca.Esp32 });
            var rede = new Netlist(c);
            Rodar(ed, bancada, rede, 1);
            double solto = ed.RpmDoMotor(motor.Id);
            ed.BotoesPressionados.Add(botao.Id);
            Rodar(ed, bancada, rede, 2);
            double apertado = ed.RpmDoMotor(motor.Id);
            var energia = ed.CalcularEnergia(bancada, rede);
            var nivel = energia.NivelDaPeca(botao.Id);
            ed.BotoesPressionados.Remove(botao.Id);
            Rodar(ed, bancada, rede, 2);
            double soltoDeNovo = ed.RpmDoMotor(motor.Id);
            saida.AppendLine($"        botao em serie: solto {solto:0}, apertado {apertado:0}, solto de novo {soltoDeNovo:0} rpm; "
                             + $"no botao {energia.CorrenteDaPeca(botao.Id) * 1000:0} mA ({nivel})");
            Conferir("botao em serie: solto o motor fica parado, apertado ele gira, soltou ele para",
                solto == 0 && apertado > 230 && soltoDeNovo == 0, $"{solto:0}/{apertado:0}/{soltoDeNovo:0}");
            Conferir("visao de energia: o motor passa corrente demais pelo botao de 50 mA",
                nivel == NivelDeEnergia.Demais, nivel.ToString());
        }
        {
            var c = new Circuito();
            var bat = new PecaNoCircuito { Tipo = "bateria", X = 0, Y = 0, Volts = 3, MiliAmperes = 2000 };
            var chave = new PecaNoCircuito { Tipo = "slide-switch", X = 200, Y = 0, Giro = 0 };
            var motor = new PecaNoCircuito { Tipo = "motor-dc", X = 400, Y = 0 };
            c.Pecas.AddRange([bat, chave, motor]);
            Ligar(c, bat, "+", chave, "2");
            Ligar(c, chave, "3", motor, "+");
            Ligar(c, motor, "-", bat, "-");

            var ed = new EditorCircuito();
            ed.Carregar(c);
            var bancada = Simulador.App.Bancada.DeUmaSo(new Maquina { Perfil = PerfilPlaca.Esp32 });
            var rede = new Netlist(c);
            Rodar(ed, bancada, rede, 1);
            double naEsquerda = ed.RpmDoMotor(motor.Id);
            chave.Giro = 1;
            Rodar(ed, bancada, rede, 1.5);
            double naDireita = ed.RpmDoMotor(motor.Id);
            saida.AppendLine($"        chave deslizante (comum no 2, motor no 3): esquerda {naEsquerda:0}, direita {naDireita:0} rpm");
            Conferir("chave deslizante: o comum (2) vai para o 1 na esquerda e para o 3 na direita",
                naEsquerda == 0 && naDireita > 3500, $"{naEsquerda:0}/{naDireita:0}");
        }

        // ---- pelo rele, com a placa mandando ----
        {
            var c = new Circuito();
            var placa = new PecaNoCircuito { Tipo = "esp32-devkit-v1", X = 0, Y = 0, Usb = true };
            var rele = new PecaNoCircuito { Tipo = "ks2e-m-dc5", X = 400, Y = 0 };
            var bat = new PecaNoCircuito { Tipo = "bateria", X = 400, Y = 200, Volts = 6, MiliAmperes = 2000 };
            var motor = new PecaNoCircuito { Tipo = "motor-tt", X = 700, Y = 0 };
            c.Pecas.AddRange([placa, rele, bat, motor]);
            Ligar(c, placa, "D13", rele, "COIL1");
            Ligar(c, rele, "COIL2", placa, "GND.2");
            Ligar(c, bat, "+", rele, "P1");
            Ligar(c, rele, "NO1", motor, "+");
            Ligar(c, motor, "-", bat, "-");

            var ed = new EditorCircuito();
            ed.Carregar(c);
            var maq = Ligada();
            var bancada = Simulador.App.Bancada.DeUmaSo(maq);
            var rede = new Netlist(c);
            Rodar(ed, bancada, rede, 1);
            double desligado = ed.RpmDoMotor(motor.Id);
            maq.Modo[13] = 1; maq.Pwm[13] = 255; maq.Digital[13] = 1;
            Rodar(ed, bancada, rede, 2);
            double ligado = ed.RpmDoMotor(motor.Id);
            maq.Pwm[13] = 0; maq.Digital[13] = 0;
            Rodar(ed, bancada, rede, 2);
            double deNovo = ed.RpmDoMotor(motor.Id);
            saida.AppendLine($"        rele no GPIO 13: LOW {desligado:0}, HIGH {ligado:0}, LOW de novo {deNovo:0} rpm");
            Conferir("rele: a placa liga e desliga o motor pelo contato NO, com a bateria do motor",
                desligado == 0 && ligado > 235 && deNovo == 0, $"{desligado:0}/{ligado:0}/{deNovo:0}");
        }

        // ---- direto no GPIO: nao sai do lugar, e o aviso explica ----
        {
            var c = new Circuito();
            var placa = new PecaNoCircuito { Tipo = "esp32-devkit-v1", X = 0, Y = 0 };
            var amarelo = new PecaNoCircuito { Tipo = "motor-tt", X = 400, Y = 0 };
            var m130 = new PecaNoCircuito { Tipo = "motor-dc", X = 400, Y = 200 };
            c.Pecas.AddRange([placa, amarelo, m130]);
            Ligar(c, placa, "D13", amarelo, "+");
            Ligar(c, amarelo, "-", placa, "GND.2");
            Ligar(c, placa, "D12", m130, "+");
            Ligar(c, m130, "-", placa, "GND.1");

            var ed = new EditorCircuito();
            ed.Carregar(c);
            var maq = Ligada(13, 12);
            var bancada = Simulador.App.Bancada.DeUmaSo(maq);
            var rede = new Netlist(c);
            Rodar(ed, bancada, rede, 2);
            var tt = ed.MotorDcParaTeste(amarelo.Id);
            var dc = ed.MotorDcParaTeste(m130.Id);
            var avisos = Queima.Conferir(c, rede, bancada);
            var doAmarelo = avisos.FirstOrDefault(a => a.Peca == amarelo.Id);
            saida.AppendLine($"        direto no GPIO: amarelo com {tt?.Tensao:0.00} V, {tt?.Rpm:0} rpm; "
                             + $"130 com {dc?.Tensao:0.00} V, {dc?.Rpm:0} rpm");
            saida.AppendLine($"        aviso: {doAmarelo?.Motivo}");
            Conferir("GPIO direto: a porta de 3,3 V nao segura a tensao e nenhum dos dois sai do lugar",
                tt?.Rpm == 0 && dc?.Rpm == 0 && tt.Tensao < 0.6 && dc.Tensao < 1.2,
                $"{tt?.Rpm:0} {tt?.Tensao:0.00} V / {dc?.Rpm:0} {dc?.Tensao:0.00} V");
            Conferir("GPIO direto: aviso (nao queima) mandando usar ponte H, transistor ou rele",
                doAmarelo is { Queimou: false } && doAmarelo.Motivo.Contains("ponte H")
                && avisos.Any(a => a.Peca == m130.Id && !a.Queimou), doAmarelo?.Motivo ?? "(nenhum)");
        }

        // ---- no 5V da placa: gira, mas a placa reinicia na bancada ----
        {
            var c = new Circuito();
            var placa = new PecaNoCircuito { Tipo = "esp32-devkit-v1", X = 0, Y = 0 };
            var motor = new PecaNoCircuito { Tipo = "motor-tt", X = 400, Y = 0 };
            c.Pecas.AddRange([placa, motor]);
            Ligar(c, placa, "VIN", motor, "+");
            Ligar(c, motor, "-", placa, "GND.2");

            var ed = new EditorCircuito();
            ed.Carregar(c);
            var bancada = Simulador.App.Bancada.DeUmaSo(Ligada());
            var rede = new Netlist(c);
            Rodar(ed, bancada, rede, 2);
            var aviso = Queima.Conferir(c, rede, bancada).FirstOrDefault(a => a.Peca == motor.Id);
            saida.AppendLine($"        no VIN da placa: {ed.RpmDoMotor(motor.Id):0} rpm; aviso: {aviso?.Motivo}");
            Conferir("no 5 V da placa o amarelo gira (~210 rpm)", ed.RpmDoMotor(motor.Id) is > 190 and < 225,
                $"{ed.RpmDoMotor(motor.Id):0}");
            Conferir("no 5 V da placa: aviso de brownout, alimentar o motor por fora",
                aviso is { Queimou: false } && aviso.Motivo.Contains("brownout"), aviso?.Motivo ?? "(nenhum)");
        }

        // ---- o brushless direto na placa ----
        {
            var c = new Circuito();
            var placa = new PecaNoCircuito { Tipo = "esp32-devkit-v1", X = 0, Y = 0 };
            var motor = new PecaNoCircuito { Tipo = "motor-brushless", X = 400, Y = 0 };
            c.Pecas.AddRange([placa, motor]);
            Ligar(c, placa, "D13", motor, "A");
            Ligar(c, placa, "D12", motor, "B");
            Ligar(c, placa, "D14", motor, "C");

            var ed = new EditorCircuito();
            ed.Carregar(c);
            var maq = Ligada(13);
            maq.Modo[12] = 1; maq.Modo[14] = 1;   // B e C em LOW
            var bancada = Simulador.App.Bancada.DeUmaSo(maq);
            var rede = new Netlist(c);
            Rodar(ed, bancada, rede, 1);
            var aviso = Queima.Conferir(c, rede, bancada).FirstOrDefault(a => a.Peca == motor.Id);
            saida.AppendLine($"        brushless nos GPIOs: {ed.RpmDoMotor(motor.Id):0} rpm; aviso: {aviso?.Motivo}");
            Conferir("brushless direto nos GPIOs: o rotor nao se mexe e o aviso manda usar um ESC",
                ed.RpmDoMotor(motor.Id) == 0 && aviso is { Queimou: false } && aviso.Motivo.Contains("ESC"),
                aviso?.Motivo ?? "(nenhum)");
        }

        // ---- correcao global que veio junto: resistor "ao contrario" ----
        {
            var c = new Circuito();
            var placa = new PecaNoCircuito { Tipo = "esp32-devkit-v1", X = 0, Y = 0 };
            var r = new PecaNoCircuito { Tipo = "resistor", X = 200, Y = 0, Ohms = 220 };
            var led = new PecaNoCircuito { Tipo = "led", X = 400, Y = 0 };
            c.Pecas.AddRange([placa, r, led]);
            Ligar(c, placa, "D13", r, "2");
            Ligar(c, r, "1", led, "A");
            Ligar(c, led, "C", placa, "GND.2");
            var energia = Energia.Calcular(c, Simulador.App.Bancada.DeUmaSo(Ligada(13)));
            double mA = energia.CorrenteDaPeca(r.Id) * 1000;
            Conferir("energia: o resistor com o pino 2 no lado da fonte tambem mostra a corrente (antes, zero)",
                mA is > 2 and < 12, $"{mA:0.0} mA");
        }
    }

    static void MotorDc_Pecas()
    {
        saida.AppendLine("motores DC: as pecas");

        foreach (var (tipo, pinos, nome) in new[]
        {
            ("motor-dc", 2, "Motor DC com escova (130, RS-540...)"),
            ("motor-tt", 2, "Motor TT amarelo (1:48)"),
            ("motor-brushless", 3, "Motor brushless (A2212, 2205...)"),
        })
        {
            var def = Catalogo.Obter(tipo);
            Conferir($"{tipo}: esta no catalogo, em Motores, com {pinos} pinos e nome de gente",
                def is not null && def.Pinos.Count == pinos && def.Nome == nome && def.Categoria == "Motores",
                def is null ? "(sem peca)" : $"{def.Pinos.Count} pinos, '{def.Nome}', {def.Categoria}");
            Conferir($"{tipo}: o desenho vem partido (corpo parado + parte que gira)",
                Pecas.Existe(tipo) && Pecas.Existe(tipo + "-corpo") && Pecas.Existe(tipo + "-movel"));

            // Na tela: a parte que gira existe e gira em torno do centro certo.
            var c = new Circuito();
            var peca = new PecaNoCircuito { Id = "m", Tipo = tipo, X = 100, Y = 100 };
            c.Pecas.Add(peca);
            var ed = new EditorCircuito();
            ed.Carregar(c);
            Conferir($"{tipo}: o editor cria a parte que gira", ed.TemParteMovelParaTeste("m"));
        }
    }

    /// <summary>
    /// A bancada dos motores, para o teste pela janela e para o passeio: o
    /// amarelo pelo rele (o GPIO 13 manda, 6 V de pilhas giram), o 130 direto
    /// em duas pilhas (3 V) e o brushless solto, so para a foto.
    /// </summary>
    internal static Circuito CircuitoDosMotores()
    {
        var c = new Circuito();
        var placa = new PecaNoCircuito { Id = "placa", Tipo = "esp32-devkit-v1", X = 40, Y = 60, Usb = true };
        var rele = new PecaNoCircuito { Id = "rele", Tipo = "ks2e-m-dc5", X = 300, Y = 110 };
        var pilhas6 = new PecaNoCircuito { Id = "pilhas6", Tipo = "bateria", X = 300, Y = 220, Volts = 6, MiliAmperes = 2000, Nome = "4 pilhas AA" };
        var amarelo = new PecaNoCircuito { Id = "amarelo", Tipo = "motor-tt", X = 470, Y = 80 };
        var pilhas3 = new PecaNoCircuito { Id = "pilhas3", Tipo = "bateria", X = 300, Y = 370, Volts = 3, MiliAmperes = 2000, Nome = "2 pilhas AA" };
        var m130 = new PecaNoCircuito { Id = "m130", Tipo = "motor-dc", X = 520, Y = 300 };
        var brushless = new PecaNoCircuito { Id = "brushless", Tipo = "motor-brushless", X = 700, Y = 280 };
        c.Pecas.AddRange([placa, rele, pilhas6, amarelo, pilhas3, m130, brushless]);

        void Ligar(PecaNoCircuito a, string pa, PecaNoCircuito b, string pb, string cor)
            => c.Fios.Add(new Fio { DePeca = a.Id, DePino = pa, ParaPeca = b.Id, ParaPino = pb, Cor = cor });

        Ligar(placa, "D13", rele, "COIL1", "#00A651");
        Ligar(rele, "COIL2", placa, "GND.2", "#1A1A1A");
        Ligar(pilhas6, "+", rele, "P1", "#D23B33");
        Ligar(rele, "NO1", amarelo, "+", "#D23B33");
        Ligar(amarelo, "-", pilhas6, "-", "#1A1A1A");
        Ligar(pilhas3, "+", m130, "+", "#D23B33");
        Ligar(m130, "-", pilhas3, "-", "#1A1A1A");
        return c;
    }

    internal const string SketchDosMotores = """
        // O rele liga o motor amarelo por 3 s e desliga por 1,5 s.
        void setup() {
          pinMode(13, OUTPUT);
        }

        void loop() {
          digitalWrite(13, HIGH);
          delay(3000);
          digitalWrite(13, LOW);
          delay(1500);
        }
        """;

    /// <summary>
    /// Foto so do editor, para quem conferir de olho: a janela do teste nao
    /// aparece na tela (nao rouba o foco de quem usa o computador), entao o
    /// editor e medido e desenhado sozinho, no tamanho pedido.
    /// </summary>
    static void FotografarEditor(EditorCircuito ed, string destino, int largura, int altura)
    {
        try
        {
            ed.Measure(new System.Windows.Size(largura, altura));
            ed.Arrange(new System.Windows.Rect(0, 0, largura, altura));
            ed.Ajustar();
            ed.UpdateLayout();
            var foto = new System.Windows.Media.Imaging.RenderTargetBitmap(
                largura, altura, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            foto.Render(ed);
            var png = new System.Windows.Media.Imaging.PngBitmapEncoder();
            png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(foto));
            using var arquivo = System.IO.File.Create(destino);
            png.Save(arquivo);
        }
        catch (Exception e)
        {
            saida.AppendLine($"        (a foto nao saiu: {e.Message})");
        }
    }

    /// <summary>
    /// Pela janela de verdade: o relogio da tela (25 quadros por segundo,
    /// tempo de parede) e quem gira os motores, e nao o passo fixo do teste.
    /// </summary>
    static void MotorDc_NaJanela()
    {
        saida.AppendLine("motores DC na janela");

        var janela = new MainWindow();
        try
        {
            janela.TesteAbrir(CircuitoDosMotores(), SketchDosMotores);
            var ed = janela.TesteEditorCircuito;

            // As pilhas giram o 130 com a simulacao parada, como na mesa.
            bool parado = EsperarPor(() => ed.RpmDoMotor("m130") > 3500, 3000);
            saida.AppendLine($"        simulacao parada: 130 a {ed.RpmDoMotor("m130"):0} rpm, amarelo a {ed.RpmDoMotor("amarelo"):0}");
            Conferir("janela: o 130 nas pilhas gira com a simulacao parada; o amarelo, no rele, nao",
                parado && ed.RpmDoMotor("amarelo") == 0, $"{ed.RpmDoMotor("m130"):0}/{ed.RpmDoMotor("amarelo"):0}");

            janela.TesteRodar();
            bool girou = EsperarPor(() => ed.RpmDoMotor("amarelo") > 235, 4000);
            double angulo1 = ed.MovimentoParaTeste("amarelo").Angulo;
            EsperarPor(() => false, 200);
            double angulo2 = ed.MovimentoParaTeste("amarelo").Angulo;
            string rotulo = ed.RotuloDeGiroParaTeste("amarelo");
            saida.AppendLine($"        rodando: amarelo a {ed.RpmDoMotor("amarelo"):0} rpm ('{rotulo}'), eixo {angulo1:0} -> {angulo2:0} graus; "
                             + $"130 '{ed.RotuloDeGiroParaTeste("m130")}'");
            Conferir("janela: o GPIO 13 puxa o rele e o amarelo gira, com o eixo andando na tela",
                girou && Math.Abs(angulo2 - angulo1) > 5 && rotulo is not null, $"{ed.RpmDoMotor("amarelo"):0}");

            string foto = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "circuitsketch-teste-motores.png");
            FotografarEditor(ed, foto, 1000, 560);
            saida.AppendLine($"        foto: {foto}");

            // O sketch solta o rele depois de 3 s: o amarelo para sozinho.
            bool parou = EsperarPor(() => ed.RpmDoMotor("amarelo") == 0, 4000);
            Conferir("janela: o rele solta e o amarelo freia ate parar", parou, $"{ed.RpmDoMotor("amarelo"):0}");
        }
        finally
        {
            janela.TesteParar();
            try { janela.Close(); } catch (InvalidOperationException) { }
        }
    }
}
