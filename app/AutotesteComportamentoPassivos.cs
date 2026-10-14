namespace Simulador.App;

/// <summary>
/// O comportamento dos passivos, dos semicondutores e da alimentacao, peca a
/// peca (a TAREFA-006, feita pelo Claude): cada uma na ligacao de verdade,
/// em duas ou tres situacoes, medida pela conta de energia, pelo ADC da placa
/// e pelos avisos. Os blocos Diodos_NoCircuito, Semicondutores_NoCircuito,
/// Passivos_NoTempo e Reguladores_DeTensao descem nos detalhes; aqui e a
/// varredura, uma por uma -- e foi ela que achou o LDR solto lendo a luz
/// direto no pino, sem o divisor (hoje ele e um resistor na energia).
/// </summary>
public static partial class Autoteste
{
    static void Comportamento_PassivosESemicondutores()
    {
        saida.AppendLine("comportamento: passivos, semicondutores e alimentacao");

        static void Ver(string tipo, string situacao, string esperado, bool ok, string visto)
            => Conferir($"{tipo}: {situacao} -> {esperado}", ok, visto);
        static PecaNoCircuito Bat(string id, double volts, double ma = 20000)
            => new() { Id = id, Tipo = "bateria", X = 0, Y = 300, Volts = volts, MiliAmperes = ma, MiliAmperesHora = 2000 };
        static PecaNoCircuito Peca(string id, string tipo, string modelo = null)
            => new() { Id = id, Tipo = tipo, X = 300, Y = 0, Modelo = modelo };
        static PecaNoCircuito R(string id, double ohms) => new() { Id = id, Tipo = "resistor", X = 200, Y = 0, Ohms = ohms };
        static void Fio(Circuito c, string a, string pa, string b, string pb)
            => c.Fios.Add(new Fio { DePeca = a, DePino = pa, ParaPeca = b, ParaPino = pb });
        static Circuito Novo(params PecaNoCircuito[] pecas)
        {
            var c = new Circuito();
            c.Pecas.AddRange(pecas);
            return c;
        }
        static Energia Conta(Circuito c, Maquina maq = null)
            => Energia.Calcular(c, Simulador.App.Bancada.DeUmaSo(maq ?? new Maquina()));
        // Uma ESP32 na USB com um pino de saida em HIGH ou LOW.
        static Maquina Esp32(int gpio, int nivel)
        {
            var maq = new Maquina { Perfil = PerfilPlaca.Esp32 };
            maq.Reiniciar();
            maq.Modo[gpio] = 1;
            maq.Digital[gpio] = nivel;
            maq.Pwm[gpio] = nivel == 1 ? 255 : 0;
            return maq;
        }
        // A placa e a bateria, com o - da bateria no GND da placa.
        static Circuito ComPlaca(double volts, params PecaNoCircuito[] pecas)
        {
            var c = Novo([new PecaNoCircuito { Id = "placa", Tipo = "esp32-devkit-v1", X = 0, Y = 0, Usb = true }, Bat("bat", volts), .. pecas]);
            Fio(c, "bat", "-", "placa", "GND.2");
            return c;
        }
        // O tempo: o editor anda a energia de passo em passo.
        static (EditorCircuito Ed, Simulador.App.Bancada B, Netlist Rede) NoTempo(Circuito c)
        {
            var ed = new EditorCircuito();
            ed.Carregar(c);
            return (ed, Simulador.App.Bancada.DeUmaSo(new Maquina()), new Netlist(c));
        }

        // ================================ passivos ================================

        foreach (double ohms in new[] { 100.0, 1_000, 10_000 })
        {
            var c = Novo(Bat("bat", 5), R("r", ohms));
            Fio(c, "bat", "+", "r", "1");
            Fio(c, "r", "2", "bat", "-");
            double i = Conta(c).CorrenteDaBateria("bat"), esperado = 5 / ohms;
            Ver("resistor", $"{ohms:0} ohms em 5 V", $"{esperado * 1000:0.##} mA (V/R)", Math.Abs(i - esperado) / esperado < 0.02, $"{i * 1000:0.###} mA");
        }

        // O LDR num divisor com 10 kohm, lido no GPIO 34: em cima, mais luz e
        // leitura maior; embaixo, o contrario; sem o resistor, o pino fica no
        // 3,3 V e a luz nao muda nada.
        {
            int Leitura(string montagem, double lux)
            {
                var t = new SaidaEmTeste("ldr");
                var rdiv = t.Adicionar("rdiv", "resistor");
                rdiv.Ohms = 10_000;
                switch (montagem)
                {
                    case "em cima":
                        t.Ligar("1", "3V3");
                        t.Fio(t.P.Id, "2", "placa", "D34");
                        t.Fio("rdiv", "1", "placa", "D34");
                        t.Fio("rdiv", "2", "placa", "GND.1");
                        break;
                    case "embaixo":
                        t.Fio("rdiv", "1", "placa", "3V3");
                        t.Fio("rdiv", "2", "placa", "D34");
                        t.Fio(t.P.Id, "1", "placa", "D34");
                        t.Ligar("2", "GND.1");
                        break;
                    default:   // so o LDR, sem o resistor do divisor
                        t.Ligar("1", "3V3");
                        t.Fio(t.P.Id, "2", "placa", "D34");
                        break;
                }
                t.P.Giro = (Math.Log10(lux) + 1) / 6;
                t.Quadro(2);
                return t.M.Analogico[34];
            }
            int cimaEscuro = Leitura("em cima", 10), cimaClaro = Leitura("em cima", 10_000);
            int baixoEscuro = Leitura("embaixo", 10), baixoClaro = Leitura("embaixo", 10_000);
            int soEscuro = Leitura("sem resistor", 10), soClaro = Leitura("sem resistor", 10_000);
            Ver("ldr", "em cima do divisor (3V3-LDR-pino-10k-GND), 10 lux e 10 mil lux",
                "a leitura sobe com a luz (~680 e ~3940 de 4095)", cimaEscuro is > 550 and < 820 && cimaClaro > 3800, $"{cimaEscuro} e {cimaClaro}");
            Ver("ldr", "embaixo do divisor (3V3-10k-pino-LDR-GND), 10 lux e 10 mil lux",
                "a leitura desce com a luz (~3410 e ~160)", baixoEscuro is > 3250 and < 3560 && baixoClaro < 300, $"{baixoEscuro} e {baixoClaro}");
            Ver("ldr", "sem o resistor do divisor", "o pino fica no 3,3 V e a luz nao muda a leitura", soEscuro > 3950 && soClaro > 3950, $"{soEscuro} e {soClaro}");
        }

        // O ceramico: 100 nF por 10 kohm (tau = 1 ms), de 50 em 50 us.
        {
            var c = Novo(Bat("bat", 5), R("r", 10_000), Peca("x", "capacitor", "100n"));
            Fio(c, "bat", "+", "r", "1");
            Fio(c, "r", "2", "x", "1");
            Fio(c, "x", "2", "bat", "-");
            var (ed, b, rede) = NoTempo(c);
            for (int k = 0; k < 20; k++) ed.AvancarEnergiaParaTeste(b, rede, 0.00005);
            double umTau = ed.TensaoDoCapacitorParaTeste("x");
            for (int k = 0; k < 200; k++) ed.AvancarEnergiaParaTeste(b, rede, 0.00005);
            double cheio = ed.TensaoDoCapacitorParaTeste("x");
            double regime = ed.CalcularEnergia(b, rede).CorrenteDaBateria("bat");
            Ver("capacitor", "100 nF por 10 kohm em 5 V", "~63% em 1 ms, cheio em ~5 ms, e em regime nao passa corrente",
                umTau is > 2.9 and < 3.3 && cheio > 4.95 && Math.Abs(regime) < 1e-5, $"1 ms: {umTau:0.00} V | 11 ms: {cheio:0.00} V | regime: {regime * 1e6:0.00} uA");
        }

        // O eletrolitico: 100 uF por 10 kohm (tau = 1 s); ao contrario, o aviso.
        {
            double Carga(bool aoContrario, out Estouro aviso)
            {
                var c = Novo(Bat("bat", 9), R("r", aoContrario ? 100 : 10_000), Peca("x", "capacitor-eletrolitico", "100u-25v"));
                Fio(c, "bat", "+", "r", "1");
                Fio(c, "r", "2", "x", aoContrario ? "-" : "+");
                Fio(c, "x", aoContrario ? "+" : "-", "bat", "-");
                var (ed, b, rede) = NoTempo(c);
                for (int k = 0; k < 20; k++) ed.AvancarEnergiaParaTeste(b, rede, 0.05);
                aviso = ed.EstourosPelaEnergia().FirstOrDefault(e => e.Peca == "x");
                return ed.TensaoDoCapacitorParaTeste("x");
            }
            double umSegundo = Carga(false, out var semAviso);
            Carga(true, out var invertido);
            Ver("capacitor-eletrolitico", "100 uF por 10 kohm em 9 V, 1 s", "~63% (5,6 V), sem aviso", umSegundo is > 5.4 and < 5.9 && semAviso is null, $"{umSegundo:0.00} V");
            Ver("capacitor-eletrolitico", "ligado ao contrario", "o aviso de que estoura", invertido is not null && invertido.Motivo.Contains("contrário"), invertido?.Motivo ?? "(nenhum aviso)");
        }

        // O indutor: a corrente sobe aos poucos (L/R) ate V/R.
        {
            var c = Novo(Bat("bat", 5), R("r", 10), Peca("x", "indutor", "10mh"));
            Fio(c, "bat", "+", "r", "1");
            Fio(c, "r", "2", "x", "1");
            Fio(c, "x", "2", "bat", "-");
            var (ed, b, rede) = NoTempo(c);
            ed.AvancarEnergiaParaTeste(b, rede, 0.0005);
            double comeco = ed.EnergiaDoQuadro.CorrenteDoRamo("x", "indutor");
            for (int k = 0; k < 40; k++) ed.AvancarEnergiaParaTeste(b, rede, 0.0005);
            double depois = ed.EnergiaDoQuadro.CorrenteDoRamo("x", "indutor");
            Ver("indutor", "10 mH com 10 ohms em 5 V", "a corrente comeca baixa e chega a ~V/R (0,28 A, com o fio dele)",
                comeco < 0.3 && Math.Abs(depois - 5 / 18.0) < 0.02, $"0,5 ms: {comeco:0.000} A | 20 ms: {depois:0.000} A");
        }

        // O cristal: em corrente continua e aberto.
        {
            var c = Novo(Bat("bat", 5), R("r", 1_000), Peca("x", "cristal"));
            Fio(c, "bat", "+", "r", "1");
            Fio(c, "r", "2", "x", "1");
            Fio(c, "x", "2", "bat", "-");
            var (ed, b, rede) = NoTempo(c);
            for (int k = 0; k < 50; k++) ed.AvancarEnergiaParaTeste(b, rede, 0.01);
            double i = ed.EnergiaDoQuadro.CorrenteDaBateria("bat");
            Ver("cristal", "5 V por 1 kohm, corrente continua", "nao passa corrente (e um capacitor entre as placas)", Math.Abs(i) < 1e-6, $"{i * 1e6:0.000} uA");
        }

        // A junction: os quatro pinos sao um no so.
        {
            var c = Novo(Bat("bat", 5), Peca("j", "junction"), R("r", 1_000));
            Fio(c, "bat", "+", "j", "1");
            Fio(c, "j", "3", "r", "1");
            Fio(c, "r", "2", "bat", "-");
            var conta = Conta(c);
            double i = conta.CorrenteDaBateria("bat");
            double v1 = conta.TensaoDoPino("j", "1"), v2 = conta.TensaoDoPino("j", "2"), v4 = conta.TensaoDoPino("j", "4");
            Ver("junction", "o + no pino 1 e o resistor no 3", "passa (5 mA), e os pinos 2 e 4 tem a mesma tensao do 1",
                Math.Abs(i - 0.005) < 0.0002 && Math.Abs(v2 - v1) < 1e-6 && Math.Abs(v4 - v1) < 1e-6, $"{i * 1000:0.00} mA, {v1:0.00}/{v2:0.00}/{v4:0.00} V");
        }

        // =============================== semicondutores ===============================

        foreach (var (tipo, queda) in new[] { ("diodo", 0.7), ("diodo-schottky", 0.3), ("diodo-zener", 0.7) })
        {
            double Corrente(bool direto)
            {
                var c = Novo(Bat("bat", 5), R("r", 100), Peca("d", tipo));
                Fio(c, "bat", "+", "r", "1");
                Fio(c, "r", "2", "d", direto ? "A" : "C");
                Fio(c, "d", direto ? "C" : "A", "bat", "-");
                return Conta(c).CorrenteDaBateria("bat");
            }
            double ida = Corrente(true), volta = Corrente(false), esperado = (5 - queda) / 100;
            Ver(tipo, "direto, 5 V por 100 ohms", $"conduz ~{esperado * 1000:0} mA (cai ~{queda:0.0} V)", Math.Abs(ida - esperado) < 0.003, $"{ida * 1000:0.0} mA");
            Ver(tipo, "ao contrario, 5 V", tipo == "diodo-zener" ? "nao conduz (5 V e menos que os 5,1 V dele)" : "nao conduz",
                Math.Abs(volta) < (tipo == "diodo-zener" ? 0.002 : 1e-5), $"{volta * 1000:0.000} mA");
        }
        {
            var c = Novo(Bat("bat", 9), R("r", 1_000), Peca("d", "diodo-zener"));
            Fio(c, "bat", "+", "r", "1");
            Fio(c, "r", "2", "d", "C");
            Fio(c, "d", "A", "bat", "-");
            var conta = Conta(c);
            double v = conta.TensaoDoPino("d", "C") - conta.TensaoDoPino("d", "A");
            Ver("diodo-zener", "ao contrario, 9 V por 1 kohm", "segura ~5,1 V", v is > 5.0 and < 5.3, $"{v:0.00} V");
        }

        // O NPN: 9 V, 100 ohms no coletor, 1 kohm na base pelo D13.
        {
            double Carga(int nivel)
            {
                var c = ComPlaca(9, Peca("q", "transistor-npn"), R("rb", 1_000), R("carga", 100));
                Fio(c, "placa", "D13", "rb", "1");
                Fio(c, "rb", "2", "q", "B");
                Fio(c, "bat", "+", "carga", "1");
                Fio(c, "carga", "2", "q", "C");
                Fio(c, "q", "E", "bat", "-");
                return Conta(c, Esp32(13, nivel)).CorrenteDaPeca("carga");
            }
            double alto = Carga(1), baixo = Carga(0);
            Ver("transistor-npn", "base em HIGH e em LOW (1 kohm), 100 ohms em 9 V", "satura (~88 mA) e corta (nada)",
                alto is > 0.080 and < 0.090 && baixo < 1e-5, $"HIGH {alto * 1000:0.0} mA | LOW {baixo * 1000:0.000} mA");
        }

        // O PNP como chave do lado de cima: emissor no 3V3, carga do coletor
        // ao GND; liga com a base em LOW e desliga em HIGH.
        {
            double Carga(int nivel)
            {
                var c = ComPlaca(5, Peca("q", "transistor-pnp"), R("rb", 1_000), R("carga", 100));
                Fio(c, "placa", "3V3", "q", "E");
                Fio(c, "placa", "D13", "rb", "1");
                Fio(c, "rb", "2", "q", "B");
                Fio(c, "q", "C", "carga", "1");
                Fio(c, "carga", "2", "placa", "GND.1");
                return Conta(c, Esp32(13, nivel)).CorrenteDaPeca("carga");
            }
            double baixo = Carga(0), alto = Carga(1);
            Ver("transistor-pnp", "emissor no 3V3, base em LOW e em HIGH", "liga com LOW (~30 mA em 100 ohms) e desliga com HIGH",
                baixo is > 0.026 and < 0.034 && alto < 1e-4, $"LOW {baixo * 1000:0.0} mA | HIGH {alto * 1000:0.000} mA");
        }

        // O MOSFET de nivel logico: 12 V, 10 ohms, gate no D13.
        {
            double Carga(int nivel)
            {
                var c = ComPlaca(12, Peca("m", "mosfet-n", "irlz44n"), R("carga", 10));
                Fio(c, "placa", "D13", "m", "G");
                Fio(c, "bat", "+", "carga", "1");
                Fio(c, "carga", "2", "m", "D");
                Fio(c, "m", "S", "bat", "-");
                return Conta(c, Esp32(13, nivel)).CorrenteDaPeca("carga");
            }
            double alto = Carga(1), baixo = Carga(0);
            Ver("mosfet-n", "IRLZ44N, gate em HIGH (3,3 V) e em LOW", "liga inteiro (~1,2 A) e corta",
                alto is > 1.15 and < 1.21 && baixo < 1e-4, $"HIGH {alto:0.00} A | LOW {baixo * 1000:0.000} mA");
        }

        // O opto: o LED pelo D13; do outro lado, 10 kohm para 5 V no coletor.
        {
            double Coletor(int nivel)
            {
                var c = ComPlaca(5, Peca("o", "optoacoplador"), R("rl", 330), R("pull", 10_000));
                Fio(c, "placa", "D13", "rl", "1");
                Fio(c, "rl", "2", "o", "A");
                Fio(c, "o", "K", "placa", "GND.1");
                Fio(c, "bat", "+", "pull", "1");
                Fio(c, "pull", "2", "o", "C");
                Fio(c, "o", "E", "bat", "-");
                return Conta(c, Esp32(13, nivel)).TensaoDoPino("o", "C");
            }
            double aceso = Coletor(1), apagado = Coletor(0);
            Ver("optoacoplador", "o LED aceso e apagado", "o coletor vai a ~0 V e volta aos 5 V do pull-up",
                aceso < 0.5 && apagado > 4.9, $"aceso {aceso:0.00} V | apagado {apagado:0.00} V");
        }

        // ================================= alimentacao =================================

        // A bateria de 9 V que aguenta 1 A (resistencia interna de 0,72 ohm).
        {
            double Terminal(double ohms)
            {
                var c = Novo(Bat("bat", 9, 1_000), R("r", ohms));
                Fio(c, "bat", "+", "r", "1");
                Fio(c, "r", "2", "bat", "-");
                var conta = Conta(c);
                return conta.TensaoDoPino("bat", "+") - conta.TensaoDoPino("bat", "-");
            }
            double leve = Terminal(100_000), pesada = Terminal(9);
            Ver("bateria", "9 V com 100 kohm e com 9 ohms", "a tensao cai com a carga: ~9 V e ~8,3 V",
                Math.Abs(leve - 9) < 0.01 && pesada is > 8.2 and < 8.45, $"{leve:0.00} V e {pesada:0.00} V");
        }

        // Os reguladores: a saida certa, e o caso em que nao dao conta.
        foreach (var (tipo, entrada, esperado, entrada2, minimo2, maximo2, situacao2) in new[]
                 {
                     ("regulador-7805", 9.0, 5.0, 6.0, 3.7, 4.3, "com 6 V na entrada so da ~4 V (precisa de 2 V de folga)"),
                     ("ams1117", 5.0, 3.3, 4.0, 2.7, 3.1, "com 4 V na entrada so da ~2,9 V"),
                     ("lm2596", 12.0, 5.0, 3.0, -0.01, 0.2, "com 3 V na entrada (abaixo de 4,5 V) desliga"),
                     ("mt3608", 3.7, 5.0, 9.0, 8.3, 8.9, "com 9 V na entrada sai ~8,6 V (so sobe)"),
                 })
        {
            double Saida(double volts)
            {
                var reg = new PecaNoCircuito { Id = "reg", Tipo = tipo, X = 200, Y = 300, Volts = Reguladores.VoltsDeFabrica(tipo) ?? 9 };
                var c = Novo(Bat("bat", volts), reg, R("carga", 100));
                var (e, te, s, ts) = Reguladores.PinosDe(tipo);
                Fio(c, "bat", "+", "reg", e);
                Fio(c, "bat", "-", "reg", te);
                Fio(c, "reg", s, "carga", "1");
                Fio(c, "carga", "2", "reg", ts);
                var conta = Conta(c);
                return conta.TensaoDoPino("reg", s) - conta.TensaoDoPino("reg", ts);
            }
            double v = Saida(entrada), v2 = Saida(entrada2);
            Ver(tipo, $"{entrada:0.0} V na entrada, 100 ohms na saida", $"{esperado:0.0} V na saida", Math.Abs(v - esperado) < 0.1, $"{v:0.00} V");
            Ver(tipo, situacao2, $"{minimo2:0.0} a {maximo2:0.0} V", v2 > minimo2 && v2 < maximo2, $"{v2:0.00} V");
        }

        // O fusivel de 1 A: com 0,5 A nada; com 2,4 A abre, e aberto nao passa nada.
        {
            (Estouro Aviso, double Depois) Ligar(double ohms)
            {
                var c = Novo(Bat("bat", 12), Peca("f", "fusivel", "1a"), R("carga", ohms));
                Fio(c, "bat", "+", "f", "1");
                Fio(c, "f", "2", "carga", "1");
                Fio(c, "carga", "2", "bat", "-");
                var (ed, b, rede) = NoTempo(c);
                ed.AvancarEnergiaParaTeste(b, rede, 0.05);
                var aviso = ed.EstourosPelaEnergia().FirstOrDefault(e => e.Peca == "f");
                if (aviso is { Queimou: true }) ed.Queimar("f");
                ed.AvancarEnergiaParaTeste(b, rede, 0.05);
                return (aviso, ed.EnergiaDoQuadro.CorrenteDaBateria("bat"));
            }
            var meio = Ligar(24);
            var forte = Ligar(5);
            Ver("fusivel", "1 A com 0,5 A passando", "nao abre", meio.Aviso is null && meio.Depois > 0.45, $"{meio.Depois:0.00} A");
            Ver("fusivel", "1 A com 2,4 A passando", "abre, e depois nao passa nada", forte.Aviso is { Queimou: true } && Math.Abs(forte.Depois) < 1e-4,
                $"{forte.Aviso?.Motivo} | depois {forte.Depois * 1000:0.000} mA");
        }

        // O varistor de 27 V: com 12 V nada; com 40 V por 100 ohms segura ~27 V.
        {
            double Tensao(double volts)
            {
                var c = Novo(Bat("bat", volts), R("r", 100), Peca("v", "varistor"));
                Fio(c, "bat", "+", "r", "1");
                Fio(c, "r", "2", "v", "1");
                Fio(c, "v", "2", "bat", "-");
                var conta = Conta(c);
                return conta.TensaoDoPino("v", "1") - conta.TensaoDoPino("v", "2");
            }
            double baixo = Tensao(12), alto = Tensao(40);
            Ver("varistor", "12 V e 40 V por 100 ohms", "nao faz nada em 12 V e segura ~27 V em 40 V",
                Math.Abs(baixo - 12) < 0.05 && alto is > 27 and < 28.5, $"{baixo:0.00} V e {alto:0.00} V");
        }

        // Os simbolos: o VCC e o trilho da placa do circuito (5 V no Uno, 3,3 V
        // na ESP32), como no Wokwi; o GND, o 0 V. Sem placa ligada, nada.
        foreach (var (placa, perfil, volts) in new[] { ("arduino-uno", PerfilPlaca.Uno, 5.0), ("esp32-devkit-v1", PerfilPlaca.Esp32, 3.3) })
        {
            var c = Novo(new PecaNoCircuito { Id = "placa", Tipo = placa, X = 0, Y = 0, Usb = true },
                         new PecaNoCircuito { Id = "vcc", Tipo = "vcc-symbol", X = 0, Y = 0 },
                         new PecaNoCircuito { Id = "gnd", Tipo = "gnd-symbol", X = 0, Y = 200 }, R("r", 1_000));
            Fio(c, "vcc", "VCC", "r", "1");
            Fio(c, "r", "2", "gnd", "GND");
            var maq = new Maquina { Perfil = perfil };
            maq.Reiniciar();
            var conta = Conta(c, maq);
            double v = conta.TensaoDoPino("vcc", "VCC") - conta.TensaoDoPino("gnd", "GND"), i = conta.CorrenteDaPeca("r");
            Ver("vcc-symbol", $"1 kohm do VCC ao GND, com a {placa}", $"o VCC e o trilho da placa ({volts:0.0} V, {volts:0.0} mA)",
                Math.Abs(v - volts) < 0.05 && Math.Abs(i - volts / 1000) < 0.0002, $"{v:0.00} V, {i * 1000:0.00} mA");
            Ver("gnd-symbol", $"o mesmo resistor, com a {placa}", "o GND e o 0 V do circuito",
                Math.Abs(conta.TensaoDoPino("gnd", "GND")) < 0.05, $"{conta.TensaoDoPino("gnd", "GND"):0.000} V");
        }
    }
}
