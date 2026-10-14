namespace Simulador.App;

public static partial class Autoteste
{
    // Bancada alimentada; valida os nomes contra o catalogo antes de criar fios.
    // As expectativas abaixo nao chamam Comportamento.Leitura/ValorReal.
    sealed class EntradaEmTeste
    {
        public readonly Circuito C = new();
        public readonly PecaNoCircuito P;
        public readonly Maquina M;
        public readonly EditorCircuito Ed = new() { PassoFixoParaTeste = 0.02 };
        Netlist rede;
        readonly PerfilPlaca perfil;
        public string Vcc => perfil == PerfilPlaca.Uno ? "5V" : "3V3";

        public EntradaEmTeste(string tipo, bool cincoVolts = false)
        {
            perfil = cincoVolts ? PerfilPlaca.Uno : PerfilPlaca.Esp32;
            M = MaquinaLigada(perfil);
            P = new PecaNoCircuito { Id = "entrada", Tipo = tipo, X = 500, Y = 0 };
            C.Pecas.AddRange([new PecaNoCircuito { Id = "placa", Tipo = perfil.Id, Usb = true }, P]);
            var pinos = Catalogo.Obter(tipo).Pinos;
            if (pinos.Any(p => p.Nome == "VCC")) Ligar("VCC", Vcc);
            if (pinos.Any(p => p.Nome == "GND")) Ligar("GND", "GND.1");
        }

        public void Ligar(string pino, string placa)
        {
            if (!Catalogo.Obter(P.Tipo).Pinos.Any(p => p.Nome == pino)
                || !Catalogo.Obter(perfil.Id).Pinos.Any(p => p.Nome == placa))
                throw new InvalidOperationException($"Pino inexistente: {P.Tipo}.{pino} / {perfil.Id}.{placa}");
            C.Fios.Add(new Fio { DePeca = P.Id, DePino = pino, ParaPeca = "placa", ParaPino = placa });
        }

        public int Sinal(string pino, int gpio = 4, bool pullup = false)
        {
            string nome = Catalogo.Obter(perfil.Id).Pinos.First(p => perfil.Numero(p.Nome) == gpio).Nome;
            Ligar(pino, nome);
            M.PinMode(gpio, pullup ? 2 : 0);
            return gpio;
        }

        public int Analogico(string pino)
        {
            string nome = perfil == PerfilPlaca.Uno ? "A0" : "D34";
            Ligar(pino, nome);
            return perfil.Numero(nome);
        }

        public void Pullup(string pino)
        {
            string id = "r" + C.Pecas.Count;
            C.Pecas.Add(new PecaNoCircuito { Id = id, Tipo = "resistor", Ohms = 4700, X = 700, Y = 200 });
            C.Fios.Add(new Fio { DePeca = P.Id, DePino = pino, ParaPeca = id, ParaPino = "1" });
            C.Fios.Add(new Fio { DePeca = id, DePino = "2", ParaPeca = "placa", ParaPino = Vcc });
        }

        public void Quadro()
        {
            if (rede is null) { Ed.Carregar(C); rede = new Netlist(C); }
            Ed.AplicarEntradas(Simulador.App.Bancada.DeUmaSo(M), rede);
        }

        public void Apertar(bool sim)
        {
            if (sim) Ed.BotoesPressionados.Add(P.Id); else Ed.BotoesPressionados.Remove(P.Id);
        }

        public void Valor(string situacao, double lido, double esperado, double tolerancia = 0)
            => Conferir($"{P.Tipo}: {situacao} -> {esperado:0.###}",
                Math.Abs(lido - esperado) <= tolerancia, $"{lido:0.###}");
    }

    static void Comportamento_EntradasESensores()
    {
        // Uma excecao nao deve esconder as outras 24 pecas.
        void Peca(string tipo, Action<EntradaEmTeste> testar, bool cinco = false)
        {
            try { testar(new EntradaEmTeste(tipo, cinco)); }
            catch (Exception e) { Conferir($"{tipo}: montagem e execucao -> sem excecao", false, $"{e.GetType().Name}: {e.Message}"); }
        }

        foreach (string tipo in new[] { "pushbutton", "pushbutton-6mm" })
            Peca(tipo, t =>
            {
                int g = t.Sinal("1.l", pullup: true); t.Ligar("2.l", "GND.1");
                foreach (bool aperto in new[] { false, true, false })
                {
                    t.Apertar(aperto); t.Quadro();
                    t.Valor(aperto ? "apertado com pull-up" : "solto com pull-up", t.M.DigitalRead(g), aperto ? 0 : 1);
                }
            });

        foreach (string tipo in new[] { "potentiometer", "slide-potentiometer" })
            Peca(tipo, t =>
            {
                int g = t.Analogico("SIG");
                foreach (double pos in new[] { 0.25, 0.5, 0.75 })
                {
                    t.P.Giro = pos; t.Quadro();
                    t.Valor($"cursor em {pos * 100}% do curso (ADC)", t.M.AnalogRead(g), pos * 4095, 2);
                }
            });

        // SPDT: 2 e comum; esquerda fecha 1-2, direita fecha 2-3.
        // https://docs.wokwi.com/parts/wokwi-slide-switch
        Peca("slide-switch", t =>
        {
            int g = t.Sinal("2", pullup: true); t.Ligar("1", "GND.1"); t.Ligar("3", "3V3");
            foreach (int pos in new[] { 0, 1, 0 })
            {
                t.P.Giro = pos; t.Quadro();
                t.Valor(pos == 0 ? "esquerda, comum no GND" : "direita, comum no 3V3", t.M.DigitalRead(g), pos);
            }
        });

        Peca("dip-switch-8", t =>
        {
            int[] gpios = [4, 13, 14, 16, 17, 18, 19, 23];
            for (int k = 0; k < 8; k++) { t.Sinal($"{k + 1}a", gpios[k], true); t.Ligar($"{k + 1}b", "GND.1"); }
            foreach (int bits in new[] { 0, 0x55, 0xAA })
            {
                t.P.Bits = bits; t.Quadro();
                var lidos = gpios.Select(t.M.DigitalRead).ToArray();
                var esperados = Enumerable.Range(0, 8).Select(k => (bits & (1 << k)) != 0 ? 0 : 1).ToArray();
                Conferir($"dip-switch-8: Bits=0x{bits:X2}, oito contatos no GND -> {string.Join(",", esperados)}",
                    lidos.SequenceEqual(esperados), string.Join(",", lidos));
            }
        });

        Peca("membrane-keypad", t =>
        {
            int[] linhas = [13, 14, 16, 17], colunas = [18, 19, 23, 25];
            for (int k = 0; k < 4; k++) { t.Sinal($"R{k + 1}", linhas[k], true); t.Sinal($"C{k + 1}", colunas[k]); }
            t.Quadro();
            foreach (string tecla in new string[] { "1", "D", null })
            {
                t.Ed.TeclaMatricialParaTeste(t.P.Id, tecla); t.Quadro();
                int pos = tecla is null ? -1 : "123A456B789C*0#D".IndexOf(tecla);
                double valor = t.M.Leitura("keypad:tecla"), linha = t.M.Leitura("keypad:linha"), coluna = t.M.Leitura("keypad:coluna");
                Conferir($"membrane-keypad: tecla {tecla ?? "solta"} -> caractere e par linha/coluna corretos",
                    valor == (tecla is null ? 0 : tecla[0]) && linha == (pos < 0 ? -1 : linhas[pos / 4])
                    && coluna == (pos < 0 ? -1 : colunas[pos % 4]), $"caractere={valor}, linha={linha}, coluna={coluna}");
            }
        });

        Peca("analog-joystick", t =>
        {
            int x = t.Analogico("HORZ"), y = t.Sinal("VERT", 35), sw = t.Sinal("SEL", 4, true);
            foreach (var (a, b, aperto) in new[] { (0.25, 0.75, false), (0.75, 0.25, true), (0.5, 0.5, false) })
            {
                t.P.Giro = a; t.P.Giro2 = b; t.Apertar(aperto); t.Quadro();
                Conferir($"analog-joystick: X={a}, Y={b}, botao={aperto} -> ADCs independentes e SEL {(aperto ? 0 : 1)}",
                    Math.Abs(t.M.AnalogRead(x) - a * 4095) < 2 && Math.Abs(t.M.AnalogRead(y) - b * 4095) < 2
                    && t.M.DigitalRead(sw) == (aperto ? 0 : 1), $"X={t.M.AnalogRead(x)}, Y={t.M.AnalogRead(y)}, SEL={t.M.DigitalRead(sw)}");
            }
        });

        Peca("ky-040", t =>
        {
            int clk = t.Sinal("CLK", 4, true), dt = t.Sinal("DT", 13, true), sw = t.Sinal("SW", 14, true);
            // Um ciclo Gray e sua volta: uma unica borda por movimento.
            int[] passos = [0, 1, 2, 3, 4, 3, 2, 1, 0], estados = [3, 1, 0, 2, 3, 2, 0, 1, 3];
            for (int k = 0; k < passos.Length; k++)
            {
                t.P.Giro = passos[k] / 40.0; t.Quadro();
                t.Valor($"quadratura, movimento {k}, CLK/DT em binario", t.M.DigitalRead(clk) * 2 + t.M.DigitalRead(dt), estados[k]);
            }
            foreach (bool aperto in new[] { true, false })
            { t.Apertar(aperto); t.Quadro(); t.Valor($"SW apertado={aperto}", t.M.DigitalRead(sw), aperto ? 0 : 1); }
        });

        Peca("rotary-dialer", t =>
        {
            int pulso = t.Sinal("PULSE", 4, true); t.Sinal("DIAL", 13, true); t.Quadro();
            foreach (int numero in new[] { 1, 3, 0 })
            {
                t.Ed.Discar(t.P.Id, numero);
                int anterior = t.M.DigitalRead(pulso), contagem = 0;
                for (int k = 0; k < 120; k++)
                { t.Quadro(); int atual = t.M.DigitalRead(pulso); if (anterior == 1 && atual == 0) contagem++; anterior = atual; }
                t.Valor($"discar {numero}, quantidade de pulsos", contagem, numero == 0 ? 10 : numero);
            }
        });

        Peca("micro-switch", t =>
        {
            t.Ligar("COM", "GND.1"); int no = t.Sinal("NO", 4, true), nc = t.Sinal("NC", 13, true);
            foreach (bool aperto in new[] { false, true, false })
            {
                t.Apertar(aperto); t.Quadro();
                Conferir($"micro-switch: apertado={aperto} -> NO={(aperto ? 0 : 1)}, NC={(aperto ? 1 : 0)}",
                    t.M.DigitalRead(no) == (aperto ? 0 : 1) && t.M.DigitalRead(nc) == (aperto ? 1 : 0),
                    $"NO={t.M.DigitalRead(no)}, NC={t.M.DigitalRead(nc)}");
            }
        });

        Peca("tilt-switch", t =>
        {
            int g = t.Sinal("OUT"); t.P.Giro = 0; t.Quadro(); int repouso = t.M.DigitalRead(g);
            t.P.Giro = 1; t.Quadro(); int inclinado = t.M.DigitalRead(g);
            Conferir("tilt-switch: mudar orientacao -> contato muda nivel", inclinado != repouso, $"repouso={repouso}, inclinado={inclinado}");
            t.P.Giro = 0; t.Quadro(); t.Valor("voltar a orientacao inicial", t.M.DigitalRead(g), repouso);
        });

        Peca("hc-sr04", t =>
        {
            t.Sinal("TRIG", 3); t.M.PinMode(3, 1); int g = t.Sinal("ECHO");
            foreach (double cm in new[] { 25.0, 100, 200 })
            {
                t.P.Giro = cm / 400; t.M.DigitalWrite(3, 1); t.Quadro(); t.M.DigitalWrite(3, 0); t.Quadro();
                t.Valor($"{cm} cm, eco em us", t.M.Leitura($"{g}:pulso"), cm * 58, 1);
            }
        }, true);

        foreach (string tipo in new[] { "dht22", "dht11" })
            Peca(tipo, t =>
            {
                string sinal = tipo == "dht22" ? "SDA" : "DATA"; int g = t.Sinal(sinal); t.Pullup(sinal);
                foreach (var (graus, umidade) in new[] { (20.0, 40.0), (30.0, 60.0), (40.0, 75.0) })
                {
                    t.P.Giro = tipo == "dht22" ? (graus + 40) / 120 : graus / 50; t.P.Giro2 = (umidade - 30) / 50; t.Quadro();
                    double temp = t.M.Leitura($"{g}:temperatura"), rh = t.M.Leitura($"{g}:umidade");
                    Conferir($"{tipo}: {graus} C e {umidade}% RH -> leitura nas unidades reais",
                        Math.Abs(temp - graus) < 0.1 && Math.Abs(rh - umidade) < 0.1, $"{temp} C, {rh}% RH");
                }
            }, true);

        Peca("photoresistor-sensor", t =>
        {
            int a = t.Analogico("AO"); t.Sinal("DO");
            foreach (double lux in new[] { 10.0, 100, 1000 })
            {
                t.P.Giro = (Math.Log10(lux) + 1) / 6; t.Quadro();
                double f = t.M.AnalogRead(a) / 4095.0, r = 10000 * f / (1 - f);
                double lido = Math.Pow(50000 * Math.Pow(10, 0.7) / r, 1 / 0.7);
                t.Valor($"{lux} lux, formula RL10=50 kohm e gamma=0,7", lido, lux, lux * 0.03);
            }
        });

        Peca("ntc-temperature-sensor", t =>
        {
            int g = t.Analogico("OUT");
            foreach (double graus in new[] { 0.0, 25, 60 })
            {
                t.P.Giro = (graus + 24) / 104; t.Quadro(); int adc = t.M.AnalogRead(g);
                double temp = 1 / (Math.Log(1 / (4095.0 / adc - 1)) / 3950 + 1 / 298.15) - 273.15;
                t.Valor($"{graus} C, formula do NTC beta=3950", temp, graus, 0.5);
            }
        });

        Peca("pir-motion-sensor", t =>
        {
            int g = t.Sinal("OUT");
            foreach (int movimento in new[] { 0, 1, 0 })
            { t.P.Giro = movimento; t.Quadro(); t.Valor($"movimento={movimento} (estado estabilizado)", t.M.DigitalRead(g), movimento); }
        }, true);

        // O som: o KY-037 e o KY-038 da Joy-IT, comparador ativo em HIGH
        // (https://sensorkit.joy-it.net/en/sensors/ky-037 e ky-038). A chama e
        // o gas: os modulos genericos de 4 pinos com o LM393 (o desenho e o
        // deles, e nao o KY-026), com o DOUT em LOW ao detectar.
        // Nao atribuir volts ou ppm arbitrarios ao controle normalizado.
        foreach (string tipo in new[] { "flame-sensor", "gas-sensor", "big-sound-sensor", "small-sound-sensor" })
            Peca(tipo, t =>
            {
                int d = t.Sinal("DOUT"), a = t.Analogico("AOUT");
                foreach (double pos in new[] { 0.1, 0.9, 0.1 })
                {
                    t.P.Giro = pos; t.Quadro(); bool detectado = pos > 0.5;
                    int esperado = tipo is "gas-sensor" or "flame-sensor" ? (detectado ? 0 : 1) : (detectado ? 1 : 0);
                    t.Valor($"estimulo {(detectado ? "acima" : "abaixo")} do limiar (DOUT; AOUT={t.M.AnalogRead(a)})", t.M.DigitalRead(d), esperado);
                }
            }, true);

        Peca("heart-beat-sensor", t =>
        {
            // OUT e uma amostra analogica, nao BPM. O controle injeta a
            // fracao instantanea de VCC; nao testa fisiologia nem frequencia.
            int g = t.Analogico("OUT");
            foreach (double fracao in new[] { 0.25, 0.75, 0.25 })
            { t.P.Giro = fracao; t.Quadro(); t.Valor($"amostra OUT={fracao * 5} V (ADC)", t.M.AnalogRead(g), fracao * 1023, 2); }
        }, true);

        Peca("mpu6050", t =>
        {
            t.Sinal("SDA", 21, true); t.Sinal("SCL", 22, true); t.Ligar("AD0", "GND.1");
            t.Pullup("SDA"); t.Pullup("SCL");
            // Faixa +/-2g: 16384 LSB/g; controle de inclinacao de -1g a +1g em X.
            foreach (double g in new[] { -1.0, 0, 1 })
            { t.P.Giro = (g + 1) / 2; t.Quadro(); t.Valor($"X={g} g, escala +/-2g (LSB)", t.M.Leitura("mpu:ax"), g * 16384, 1); }
        });

        Peca("hx711", t =>
        {
            t.Sinal("DT"); t.Sinal("SCK", 3); t.M.PinMode(3, 1);
            // A peca do catalogo inclui a celula; a grandeza e o peso ja
            // calibrado em gramas, nao a palavra bruta de 24 bits do ADC.
            foreach (double gramas in new[] { 0.0, 1250, 2500 })
            { t.P.Giro = gramas / 5000; t.Quadro(); t.Valor($"carga calibrada de {gramas} g", t.M.Leitura("hx711:peso"), gramas, 0.1); }
        }, true);
    }
}
