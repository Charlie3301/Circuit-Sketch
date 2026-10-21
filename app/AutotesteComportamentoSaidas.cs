namespace Simulador.App;

/// <summary>
/// O comportamento de cada saida, atuador e motor (a TAREFA-005, do ChatGPT,
/// terminada pelo Claude): a peca ligada como na bancada (com resistor, com
/// ponte H, com fonte propria), o pino em dois ou tres estados e o que ela
/// faz -- a corrente, o brilho, o texto, os pixels, o tom, o contato, o
/// angulo, o passo e o rpm. As falhas que ele achou eram do simulador: o 7
/// segmentos sem os LEDs na conta de energia, a fita gastando o mesmo apagada
/// e branca, e o motor de passo que nao andava numa ponte H.
/// </summary>
public static partial class Autoteste
{
    // Fios validados no catalogo; placa USB e fontes separadas para potencia.
    sealed class SaidaEmTeste
    {
        public readonly Circuito C = new();
        public readonly PecaNoCircuito P;
        public readonly Maquina M;
        public readonly EditorCircuito Ed = new() { PassoFixoParaTeste = 0.02 };
        Netlist rede;
        public SaidaEmTeste(string tipo)
        {
            // WS2812 a 5 V recebe dados de uma placa de 5 V, sem depender
            // de um HIGH de 3,3 V abaixo do limiar garantido do datasheet.
            var perfil = tipo is "led-ring" or "neopixel" or "neopixel-matrix" ? PerfilPlaca.Uno : PerfilPlaca.Esp32;
            M = MaquinaLigada(perfil);
            C.Pecas.Add(new PecaNoCircuito { Id = "placa", Tipo = perfil.Id, Usb = true });
            P = Adicionar("peca", tipo);
        }
        public PecaNoCircuito Adicionar(string id, string tipo)
        {
            var p = new PecaNoCircuito { Id = id, Tipo = tipo, X = 400 + C.Pecas.Count * 200, Y = 0 };
            C.Pecas.Add(p); return p;
        }
        public void Fio(string a, string pa, string b, string pb)
        {
            foreach (var (id, pino) in new[] { (a, pa), (b, pb) })
                if (!Catalogo.Obter(C.Pecas.First(p => p.Id == id).Tipo).Pinos.Any(p => p.Nome == pino))
                    throw new InvalidOperationException($"Pino inexistente: {id}.{pino}");
            C.Fios.Add(new Fio { DePeca = a, DePino = pa, ParaPeca = b, ParaPino = pb });
        }
        public void Ligar(string pino, string placa) => Fio(P.Id, pino, "placa", M.Perfil == PerfilPlaca.Uno && placa == "VIN" ? "5V" : placa);
        public void Sinal(string pino, int gpio, string id = null)
        {
            string nome = Catalogo.Obter(M.Perfil.Id).Pinos.First(p => M.Perfil.Numero(p.Nome) == gpio).Nome;
            Fio(id ?? P.Id, pino, "placa", nome); M.PinMode(gpio, 1);
        }
        public void Fonte(double volts, string positivo, string negativo, string id = null)
        {
            var bat = Adicionar("bat" + C.Pecas.Count, "bateria"); bat.Volts = volts; bat.MiliAmperes = 10000;
            Fio(bat.Id, "+", id ?? P.Id, positivo); Fio(bat.Id, "-", id ?? P.Id, negativo);
            Fio(bat.Id, "-", "placa", "GND.1");
        }
        public void Resistor(string pino, int gpio, double ohms = 330)
        {
            var r = Adicionar("r" + C.Pecas.Count, "resistor"); r.Ohms = ohms;
            Fio(P.Id, pino, r.Id, "1"); Sinal("2", gpio, r.Id);
        }
        public void Quadro(int n = 3)
        {
            if (rede is null) { Ed.Carregar(C); rede = new Netlist(C); }
            var b = Simulador.App.Bancada.DeUmaSo(M);
            for (int i = 0; i < n; i++) { M.Avancar(20); Ed.AplicarEntradas(b, rede); }
            // Circuitos so com USB e LEDs nao calculam energia automaticamente.
            if (Ed.EnergiaDoQuadro is null) Ed.AvancarEnergiaParaTeste(b, rede, 0.02);
            Ed.RefletirEstado(b, rede);
        }
        public double Corrente => Math.Abs(Ed.EnergiaDoQuadro.CorrenteDaPeca(P.Id));
        /// <summary>Depois de um fio novo: o proximo quadro monta o netlist de novo.</summary>
        public void Remontar() => rede = null;
        public void Ver(string situacao, string esperado, bool ok, string lido)
            => Conferir($"{P.Tipo}: {situacao} -> {esperado}", ok, lido);
    }

    static void Comportamento_SaidasEMotores()
    {
        void Peca(string tipo, Action<SaidaEmTeste> testar)
        {
            try { testar(new SaidaEmTeste(tipo)); }
            catch (Exception e) { Conferir($"{tipo}: montagem e execucao -> sem excecao", false, $"{e.GetType().Name}: {e.Message} {e.StackTrace?.Split((char)10).FirstOrDefault()?.Trim()}"); }
        }

        foreach (string tipo in new[] { "led", "rgb-led", "led-bar-graph", "7segment" })
            Peca(tipo, t =>
            {
                string[] anodos = tipo switch { "rgb-led" => ["R", "G", "B"], "led-bar-graph" => ["A1", "A2"], "7segment" => ["A", "B"], _ => ["A"] };
                string[] catodos = tipo switch { "rgb-led" => ["COM"], "led-bar-graph" => ["C1", "C2"], "7segment" => ["COM.1", "COM.2"], _ => ["C"] };
                foreach (string c in catodos) t.Ligar(c, "GND.1");
                int[] gpios = [13, 14, 25];
                for (int k = 0; k < anodos.Length; k++) t.Resistor(anodos[k], gpios[k]);
                foreach (int pwm in new[] { 0, 128, 255 })
                {
                    foreach (int g in gpios.Take(anodos.Length)) t.M.AnalogWrite(g, pwm);
                    t.Quadro(); double corrente = t.Corrente;
                    // Resistores de 330 ohms: corrente limitada, cresce com o duty.
                    bool ok = pwm == 0 ? corrente < 0.0001 : corrente > 0.0003 && corrente < 0.025;
                    double brilho = tipo == "led" ? t.Ed.BrilhoParaTeste(t.P.Id) : 0;
                    if (tipo == "led") ok &= pwm == 0 ? brilho == 0 : brilho > 0;
                    t.Ver($"PWM {pwm}/255 com resistor em cada anodo", pwm == 0 ? "apagado, sem corrente" : "luz e corrente limitada", ok, $"I={corrente * 1000:0.###} mA, brilho={brilho:0.###}");
                }
            });

        foreach (string tipo in new[] { "led-ring", "neopixel", "neopixel-matrix" })
            Peca(tipo, t =>
            {
                t.Ligar(tipo == "neopixel" ? "VDD" : "VCC", "VIN");
                t.Ligar(tipo == "neopixel" ? "VSS" : "GND", "GND.1"); t.Sinal("DIN", 13);
                var fita = new Fita(tipo == "neopixel" ? 1 : 16); t.M.Fitas[13] = fita;
                fita.Limpar(); fita.Mostrar(); t.Quadro(); double repouso = t.Corrente;
                t.Ver("show preto", "todos apagados", fita.Cores.All(c => c == 0), $"acesos={fita.Cores.Count(c => c != 0)}, I={repouso * 1000:0.###} mA");
                for (int k = 0; k < fita.Quantidade; k++) fita.Definir(k, 0xFFFFFF);
                fita.Mostrar(); t.Quadro(); double branco = t.Corrente;
                t.Ver("show branco", "todos brancos e consumo maior que preto", fita.Cores.All(c => c == 0xFFFFFF) && branco > repouso + 0.001,
                    $"brancos={fita.Cores.Count(c => c == 0xFFFFFF)}, preto={repouso * 1000:0.###} mA, branco={branco * 1000:0.###} mA");
                fita.Limpar(); fita.Mostrar(); t.Quadro();
                t.Ver("clear e show", "apaga e volta ao consumo inicial", fita.Cores.All(c => c == 0) && Math.Abs(t.Corrente - repouso) < 0.001, $"I={t.Corrente * 1000:0.###} mA");
            });

        foreach (string tipo in new[] { "lcd1602", "lcd2004", "lcd1602-i2c", "lcd2004-i2c" })
            Peca(tipo, t =>
            {
                bool i2c = tipo.EndsWith("-i2c");
                if (i2c) { t.Ligar("VCC", "VIN"); t.Ligar("GND", "GND.1"); t.Sinal("SDA", 21); t.Sinal("SCL", 22); t.M.I2c.Begin(21, 22); }
                else
                {
                    t.Ligar("VDD", "VIN"); t.Ligar("VSS", "GND.1"); t.Ligar("RW", "GND.1"); t.Ligar("V0", "GND.1");
                    t.Resistor("A", 25); t.M.DigitalWrite(25, 1); t.Ligar("K", "GND.1");
                    string[] sinais = ["RS", "E", "D4", "D5", "D6", "D7"]; int[] pinos = [13, 14, 16, 17, 18, 19];
                    for (int k = 0; k < sinais.Length; k++) t.Sinal(sinais[k], pinos[k]);
                }
                var lcd = t.M.CriarLcd(); int colunas = tipo.StartsWith("lcd2004") ? 20 : 16, linhas = colunas == 20 ? 4 : 2;
                lcd.Begin(colunas, linhas);
                foreach (string texto in new[] { "TESTE 005", "SAIDA OK", "" })
                {
                    lcd.Limpar(); lcd.PosicionarCursor(0, linhas - 1); lcd.Escrever(texto); t.Quadro();
                    t.Ver($"texto '{texto}' na ultima linha", "conteudo e dimensoes corretos", lcd.LinhaTexto(linhas - 1) == texto.PadRight(colunas) && lcd.Ligado && t.Corrente > 0,
                        $"[{lcd.LinhaTexto(linhas - 1)}], {lcd.Colunas}x{lcd.Linhas}, I={t.Corrente * 1000:0.###} mA");
                }
            });

        foreach (string tipo in new[] { "ssd1306", "ssd1306-i2c", "ssd1306-128x32", "sh1106", "ili9341", "st7789", "st7735" })
            Peca(tipo, t =>
            {
                bool oled = EditorCircuito.EhOled(tipo), i2c = oled && tipo != "ssd1306";
                t.Ligar(tipo == "ssd1306" ? "3V3" : "VCC", "3V3"); t.Ligar("GND", "GND.1");
                string[] sinais = tipo switch { "ssd1306" => ["DATA", "CLK", "DC", "RST", "CS"], "ssd1306-i2c" or "ssd1306-128x32" or "sh1106" => ["SDA", "SCL"], "ili9341" => ["MOSI", "SCK", "D/C", "RST", "CS"], "st7735" => ["SDA", "SCK", "A0", "RESET", "CS"], _ => ["SDA", "SCL", "DC", "RES", "CS"] };
                int[] gpios = i2c ? [21, 22] : [23, 18, 25, 26, 27];
                for (int k = 0; k < sinais.Length; k++) t.Sinal(sinais[k], gpios[k]);
                if (i2c) t.M.I2c.Begin(21, 22);
                if (!oled) { t.Sinal(tipo == "st7789" ? "BLK" : "LED", 13); t.M.DigitalWrite(13, 1); }
                var tela = oled ? t.M.CriarPainelOled(128, tipo == "ssd1306-128x32" ? 32 : 64, spi: !i2c)
                         : tipo == "st7735" ? t.M.CriarTela(128, 160) : t.M.CriarTela(240, tipo == "st7789" ? 240 : 320);
                foreach (int cor in new[] { 0, oled ? 1 : 65535, 0 })
                {
                    tela.Limpar(cor); t.Quadro(); int acesos = t.Ed.AcesosNaTelaParaTeste(t.P.Id);
                    int esperado = cor == 0 ? 0 : tela.LarguraFisica * tela.AlturaFisica;
                    t.Ver($"preencher com {(cor == 0 ? "preto" : "branco")}", $"{esperado} pixels acesos", acesos == esperado, $"{acesos}");
                }
            });

        foreach (string tipo in new[] { "buzzer", "alto-falante" })
            Peca(tipo, t =>
            {
                // Resistor limita a corrente do alto-falante de 8 ohms no GPIO.
                t.Resistor(tipo == "buzzer" ? "1" : "+", 13, 330); t.Ligar(tipo == "buzzer" ? "2" : "-", "GND.1");
                foreach (int hz in new[] { 0, 440, 880 })
                {
                    if (hz == 0) t.M.NoTone(13); else t.M.Tone(13, hz);
                    t.Quadro(); t.Ver($"tom pedido {hz} Hz", $"{hz} Hz no fio", t.M.Frequencia[13] == hz, $"{t.M.Frequencia[13]} Hz");
                }
            });

        foreach (string tipo in new[] { "modulo-rele-1", "modulo-rele-2", "modulo-rele-4" })
            Peca(tipo, t =>
            {
                t.Ligar("VCC", "VIN"); t.Ligar("GND", "GND.1");
                for (int k = 1; k <= ModulosDeRele.Canais(tipo); k++) { t.Sinal(ModulosDeRele.Entrada(tipo, k), 12 + k); t.M.DigitalWrite(12 + k, 1); }
                t.Ligar(ModulosDeRele.Comum(tipo, 1), "GND.1"); t.Sinal(ModulosDeRele.Aberto(tipo, 1), 25); t.M.PinMode(25, 2);
                foreach (int nivel in new[] { 1, 0, 1 })
                {
                    t.M.DigitalWrite(13, nivel); t.Quadro(); bool ligado = t.Ed.CanalDoModuloParaTeste(t.P.Id, 1);
                    t.Ver($"IN1={nivel}", nivel == 0 ? "contato NO fecha" : "contato NO abre", ligado == (nivel == 0) && t.M.DigitalRead(25) == nivel,
                        $"armado={ligado}, NO={t.M.DigitalRead(25)}, I={t.Corrente * 1000:0.###} mA");
                }
            });

        Peca("ks2e-m-dc5", t =>
        {
            // Um modulo de rele chaveia a bobina: nunca exigir 70 mA do GPIO.
            t.Adicionar("chave", "modulo-rele-1");
            t.Fio("chave", "VCC", "placa", "VIN"); t.Fio("chave", "GND", "placa", "GND.1"); t.Sinal("IN", 13, "chave");
            t.Fonte(5, "COIL1", "COIL2");
            // Intercala o contato no positivo da fonte antes de carregar a bancada.
            var fio = t.C.Fios.First(f => f.ParaPeca == t.P.Id && f.ParaPino == "COIL1"); fio.ParaPeca = "chave"; fio.ParaPino = "COM";
            t.Fio("chave", "NO", t.P.Id, "COIL1");
            t.Ligar("P1", "GND.1"); t.Sinal("NO1", 25); t.Sinal("NC1", 26); t.M.PinMode(25, 2); t.M.PinMode(26, 2);
            foreach (int nivel in new[] { 1, 0, 1 })
            {
                t.M.DigitalWrite(13, nivel); t.Quadro(6);
                t.Ver($"bobina {(nivel == 0 ? "alimentada" : "desligada")}", nivel == 0 ? "NO fechado e NC aberto" : "NO aberto e NC fechado",
                    t.M.DigitalRead(25) == nivel && t.M.DigitalRead(26) == 1 - nivel,
                    $"NO={t.M.DigitalRead(25)}, NC={t.M.DigitalRead(26)}, I={t.Corrente * 1000:0.###} mA");
            }
        });

        foreach (string tipo in new[] { "servo", "servo-mg996r", "servo-360" })
            Peca(tipo, t =>
            {
                t.Fonte(5, "V+", "GND"); t.Sinal("PWM", 13);
                foreach (int us in new[] { 1500, 1000, 2000 })
                {
                    t.M.EscreverServo(13, 90, us); t.Quadro(60);
                    double valor = tipo == "servo-360" ? t.Ed.VelocidadeDoServoParaTeste(t.P.Id) : t.Ed.AnguloDoServoParaTeste(t.P.Id);
                    double esperado = (us - 544) * 180.0 / (2400 - 544);
                    bool ok = tipo == "servo-360" ? (us == 1500 ? Math.Abs(valor) < 1 : us == 1000 ? valor > 0 : valor < 0) : Math.Abs(valor - esperado) < 2;
                    t.Ver($"pulso {us} us estabilizado", tipo == "servo-360" ? (us == 1500 ? "parado" : us == 1000 ? "giro positivo" : "giro negativo") : $"{esperado:0.0} graus (calibracao Servo.h)", ok, $"{valor:0.###}, I={t.Corrente * 1000:0.###} mA");
                }
            });

        Comportamento_PontesEMotores(Peca);
        Comportamento_Passos(Peca);
        Comportamento_Escs(Peca);
    }

    static void Comportamento_PontesEMotores(Action<string, Action<SaidaEmTeste>> peca)
    {
        foreach (string tipo in new[] { "l298n", "l293d", "l9110s", "tb6612fng", "drv8833", "bts7960", "motor-dc", "motor-tt" })
            peca(tipo, t =>
            {
                bool motor = tipo.StartsWith("motor-"); string driver = motor ? "drv8833" : tipo;
                string d = motor ? "driver" : t.P.Id, m = motor ? t.P.Id : "motor";
                t.Adicionar(motor ? d : m, motor ? driver : "motor-tt");
                // Nomes do canal A nas placas reais; nenhuma expectativa vem do driver simulado.
                (string V, string G, string Logica, string A, string B, string O1, string O2, string En) p = driver switch
                {
                    "l298n" => ("12V", "GND", "5V", "IN1", "IN2", "OUT1", "OUT2", "ENA"),
                    "l293d" => ("VCC2", "GND.1", "VCC1", "1A", "2A", "1Y", "2Y", "1,2EN"),
                    "l9110s" => ("VCC", "GND", "", "A-IA", "A-IB", "A1", "A2", ""),
                    "tb6612fng" => ("VM", "GND", "VCC", "AIN1", "AIN2", "AO1", "AO2", "PWMA"),
                    "bts7960" => ("B+", "B-", "VCC", "RPWM", "LPWM", "M+", "M-", "R_EN"),
                    _ => ("VM", "GND", "", "AIN1", "AIN2", "AO1", "AO2", "")
                };
                t.Fonte(6, p.V, p.G, d);
                if (p.Logica != "") t.Fio(d, p.Logica, "placa", driver == "tb6612fng" ? "3V3" : "VIN");
                if (driver == "bts7960") { t.Fio(d, "GND", "placa", "GND.1"); t.Fio(d, "L_EN", "placa", "3V3"); }
                if (driver == "tb6612fng") t.Fio(d, "STBY", "placa", "3V3");
                if (p.En != "") t.Fio(d, p.En, "placa", "3V3");
                t.Sinal(p.A, 13, d); t.Sinal(p.B, 14, d);
                t.Fio(d, p.O1, m, "+"); t.Fio(d, p.O2, m, "-");
                foreach (int sentido in new[] { 0, 1, -1 })
                {
                    t.M.DigitalWrite(13, sentido == 1 ? 1 : 0); t.M.DigitalWrite(14, sentido == -1 ? 1 : 0); t.Quadro(150);
                    double rpm = t.Ed.RpmDoMotor(m);
                    t.Ver($"canal A {(sentido == 0 ? "00" : sentido == 1 ? "10" : "01")}, fonte 6 V", sentido == 0 ? "motor parado" : sentido == 1 ? "rpm positivo" : "rpm negativo",
                        sentido == 0 ? Math.Abs(rpm) < 1 : rpm * sentido > 20, $"{rpm:0.###} rpm");
                }
            });
    }

    static void Comportamento_Passos(Action<string, Action<SaidaEmTeste>> peca)
    {
        foreach (string tipo in new[] { "a4988", "drv8825", "stepper-motor" })
            peca(tipo, t =>
            {
                bool motor = tipo == "stepper-motor"; string driver = tipo == "drv8825" ? tipo : "a4988";
                string d = motor ? "driver" : t.P.Id, m = motor ? t.P.Id : "motor";
                t.Adicionar(motor ? d : m, motor ? driver : "stepper-motor");
                t.Fonte(12, "VMOT", "GND", d);
                if (driver == "a4988") t.Fio(d, "VDD", "placa", "3V3");
                foreach (string p in new[] { "RST", "SLP" }) t.Fio(d, p, "placa", "3V3");
                t.Sinal("STEP", 13, d); t.Sinal("DIR", 14, d); t.Sinal("EN", 25, d);
                string[] saidas = driver == "a4988" ? ["1A", "1B", "2A", "2B"] : ["A1", "A2", "B1", "B2"];
                string[] bobinas = ["A+", "A-", "B+", "B-"];
                for (int k = 0; k < 4; k++) t.Fio(d, saidas[k], m, bobinas[k]);
                t.Quadro();
                foreach (var (dir, en, esperado) in new[] { (1, 0, 18.0), (0, 0, 0.0), (1, 1, 0.0) })
                {
                    t.M.DigitalWrite(14, dir); t.M.DigitalWrite(25, en); t.Quadro();
                    for (int k = 0; k < 10; k++) { t.M.DigitalWrite(13, 1); t.M.Avancar(1); t.M.DigitalWrite(13, 0); t.M.Avancar(1); }
                    t.Quadro(); double angulo = t.Ed.AnguloDoPassoParaTeste(m);
                    t.Ver($"10 pulsos, DIR={dir}, EN={en}, passo cheio", $"eixo em {esperado} graus", Math.Abs(angulo - esperado) < 0.1, $"{angulo:0.###}");
                }
            });

        foreach (string tipo in new[] { "uln2003", "motor-28byj48" })
            peca(tipo, t =>
            {
                bool motor = tipo == "motor-28byj48"; string d = motor ? "driver" : t.P.Id, m = motor ? t.P.Id : "motor";
                t.Adicionar(motor ? d : m, motor ? "uln2003" : "motor-28byj48"); t.Fonte(5, "+", "-", d);
                int[] gpios = [13, 14, 25, 26]; string[] fases = ["laranja", "amarelo", "rosa", "azul"];
                for (int k = 0; k < 4; k++) { t.Sinal($"IN{k + 1}", gpios[k], d); t.Fio(d, "ABCD"[k].ToString(), m, fases[k]); }
                t.Fio(d, "COM", m, "vermelho"); t.Quadro();
                int[][] seq = [[1, 0, 0, 0], [1, 1, 0, 0], [0, 1, 0, 0], [0, 1, 1, 0], [0, 0, 1, 0], [0, 0, 1, 1], [0, 0, 0, 1], [1, 0, 0, 1]];
                void Fase(int i) { for (int k = 0; k < 4; k++) t.M.DigitalWrite(gpios[k], seq[(i % 8 + 8) % 8][k]); t.Quadro(1); }
                Fase(0); double inicio = t.Ed.AnguloDoPassoParaTeste(m);
                for (int i = 1; i <= 64; i++) Fase(i);
                double frente = t.Ed.AnguloDoPassoParaTeste(m);
                // 32 passos inteiros no rotor e reducao aproximada 63,68:1.
                double esperado = 64 * 360.0 / (64 * 63.68);
                t.Ver("64 meios passos para frente", $"deslocamento {esperado:0.###} graus", Math.Abs(frente - inicio - esperado) < 0.2, $"{frente - inicio:0.###}");
                for (int i = 63; i >= 0; i--) Fase(i);
                double volta = t.Ed.AnguloDoPassoParaTeste(m);
                t.Ver("64 meios passos para tras", "retorna ao inicio", Math.Abs(volta - inicio) < 0.2, $"inicio={inicio:0.###}, fim={volta:0.###}");
                foreach (int g in gpios) t.M.DigitalWrite(g, 0); t.Quadro();
                t.Ver("quatro entradas LOW", "LEDs das fases apagados", Enumerable.Range(0, 4).All(k => !t.Ed.LuzDeModuloAcesaParaTeste(d, k)), "fases A-D");
            });

        // O NEMA 17 numa ponte H (o L298N da Stepper.h, sem STEP/DIR): as
        // quatro entradas fazem o passo cheio nas duas bobinas (1010, 0110,
        // 0101, 1001); oito passos sao 14,4 graus, e a volta desfaz.
        peca("stepper-motor", t =>
        {
            t.Adicionar("ponte", "l298n");
            t.Fonte(12, "12V", "GND", "ponte");
            t.Fio("ponte", "5V", "placa", "VIN");
            t.Fio("ponte", "ENA", "placa", "3V3");
            t.Fio("ponte", "ENB", "placa", "3V3");
            int[] gpios = [13, 14, 25, 26];
            for (int k = 0; k < 4; k++) t.Sinal($"IN{k + 1}", gpios[k], "ponte");
            t.Fio("ponte", "OUT1", t.P.Id, "A+");
            t.Fio("ponte", "OUT2", t.P.Id, "A-");
            t.Fio("ponte", "OUT3", t.P.Id, "B+");
            t.Fio("ponte", "OUT4", t.P.Id, "B-");
            int[][] passos = [[1, 0, 1, 0], [0, 1, 1, 0], [0, 1, 0, 1], [1, 0, 0, 1]];
            void Passo(int i)
            {
                for (int k = 0; k < 4; k++) t.M.DigitalWrite(gpios[k], passos[(i % 4 + 4) % 4][k]);
                t.Quadro(2);
            }
            Passo(0);
            double inicio = t.Ed.AnguloDoPassoParaTeste(t.P.Id);
            for (int i = 1; i <= 8; i++) Passo(i);
            double frente = t.Ed.AnguloDoPassoParaTeste(t.P.Id);
            t.Ver("L298N: oito passos cheios da Stepper.h", "eixo anda 14,4 graus", Math.Abs(Math.Abs(frente - inicio) - 14.4) < 0.1,
                $"inicio={inicio:0.###}, depois={frente:0.###}");
            for (int i = 7; i >= 0; i--) Passo(i);
            double volta = t.Ed.AnguloDoPassoParaTeste(t.P.Id);
            t.Ver("L298N: os mesmos passos ao contrario", "volta ao inicio", Math.Abs(volta - inicio) < 0.1, $"inicio={inicio:0.###}, fim={volta:0.###}");
        });

        peca("biaxial-stepper", t =>
        {
            // Dois L293D, quatro pontes completas: bobinas nunca diretamente nos GPIOs.
            int[] gpios = [13, 14, 25, 26, 16, 17, 18, 19];
            for (int eixo = 0; eixo < 2; eixo++)
            {
                string d = "driver" + eixo; t.Adicionar(d, "l293d"); t.Fonte(5, "VCC2", "GND.1", d);
                t.Fio(d, "VCC1", "placa", "VIN");
                t.Fio(d, "1,2EN", "placa", "3V3"); t.Fio(d, "3,4EN", "placa", "3V3");
                string[] bobinas = [$"A{eixo + 1}+", $"B{eixo + 1}+", $"A{eixo + 1}-", $"B{eixo + 1}-"];
                int[] pontes = [1, 3, 2, 4];
                for (int k = 0; k < 4; k++) { t.Sinal($"{pontes[k]}A", gpios[eixo * 4 + k], d); t.Fio(d, $"{pontes[k]}Y", t.P.Id, bobinas[k]); }
            }
            int[][] seq = [[1, 0, 0, 0], [1, 1, 0, 0], [0, 1, 0, 0], [0, 1, 1, 0], [0, 0, 1, 0], [0, 0, 1, 1], [0, 0, 0, 1], [1, 0, 0, 1]];
            for (int eixo = 0; eixo < 2; eixo++)
            {
                t.Quadro(); double inicio = t.Ed.AnguloDoEixoParaTeste(t.P.Id, eixo), outro = t.Ed.AnguloDoEixoParaTeste(t.P.Id, 1 - eixo);
                for (int i = 0; i <= 16; i++) { for (int k = 0; k < 4; k++) t.M.DigitalWrite(gpios[eixo * 4 + k], seq[i % 8][k]); t.Quadro(1); }
                double fim = t.Ed.AnguloDoEixoParaTeste(t.P.Id, eixo);
                t.Ver($"sequencia nas duas bobinas do eixo {eixo + 1}", "eixo gira e o outro fica parado", Math.Abs(fim - inicio) > 0.1 && Math.Abs(t.Ed.AnguloDoEixoParaTeste(t.P.Id, 1 - eixo) - outro) < 0.1,
                    $"inicio={inicio:0.###}, fim={fim:0.###}, outro={t.Ed.AnguloDoEixoParaTeste(t.P.Id, 1 - eixo):0.###}");
            }
        });
    }

    static void Comportamento_Escs(Action<string, Action<SaidaEmTeste>> peca)
    {
        foreach (string tipo in new[] { "esc", "esc-4em1", "motor-brushless" })
            peca(tipo, t =>
            {
                bool motor = tipo == "motor-brushless", quatro = tipo == "esc-4em1";
                string d = motor ? "driver" : t.P.Id;
                if (motor) t.Adicionar(d, "esc");
                t.Fonte(11.1, "BAT+", "BAT-", d); t.Fio(d, "GND", "placa", "GND.1");
                int[] gpios = [13, 14, 25, 26]; var motores = new List<string>();
                for (int k = 0; k < (quatro ? 4 : 1); k++)
                {
                    string m = motor ? t.P.Id : "motor" + k; motores.Add(m);
                    if (!motor) t.Adicionar(m, "motor-brushless");
                    t.Sinal(quatro ? $"S{k + 1}" : "SINAL", gpios[k], d);
                    foreach (string f in new[] { "A", "B", "C" }) t.Fio(d, quatro ? $"M{k + 1}{f}" : f, m, f);
                }
                foreach (int us in new[] { 1000, 1500, 1000 })
                {
                    for (int k = 0; k < motores.Count; k++) t.M.EscreverServo(gpios[k], 0, us);
                    t.Quadro(200); var rpms = motores.Select(t.Ed.RpmDoMotor).ToArray();
                    t.Ver($"pulso {us} us por 4 s", us == 1000 ? "motores parados" : "todos os motores giram",
                        rpms.All(r => us == 1000 ? Math.Abs(r) < 1 : r > 1000), string.Join(", ", rpms.Select(r => $"{r:0.###} rpm")));
                }
            });
    }
}
