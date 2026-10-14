namespace Simulador.App;

/// <summary>
/// O receptor FlySky e o controle: o aeromodelo sem placa (o BEC do ESC
/// alimenta o receptor, que move o servo e acelera o motor), o failsafe, e a
/// placa lendo o receptor por pulseIn, por interrupcao, pelo PPM e pelo iBUS.
/// </summary>
public static partial class Autoteste
{
    static void Receptor_Radio()
    {
        saida.AppendLine("receptor de radio e controle");

        // ---- o aeromodelo, sem placa nenhuma ----
        {
            var c = new Circuito();
            var lipo = new PecaNoCircuito { Id = "lipo", Tipo = "bateria", X = 0, Y = 300, Volts = 11.1, MiliAmperes = 55000 };
            var esc = new PecaNoCircuito { Id = "esc", Tipo = "esc", X = 300, Y = 0 };
            var motor = new PecaNoCircuito { Id = "motor", Tipo = "motor-brushless", X = 0, Y = 0 };
            var rx = new PecaNoCircuito { Id = "rx", Tipo = "receptor-rc", X = 600, Y = 0 };
            var servo = new PecaNoCircuito { Id = "servo", Tipo = "servo", X = 600, Y = 200 };
            var controle = new PecaNoCircuito { Id = "controle", Tipo = "radio-controle", X = 900, Y = 0, Giro = 0 };
            c.Pecas.AddRange([lipo, esc, motor, rx, servo, controle]);
            void Ligar(PecaNoCircuito a, string pa, PecaNoCircuito d, string pd)
                => c.Fios.Add(new Fio { DePeca = a.Id, DePino = pa, ParaPeca = d.Id, ParaPino = pd });
            Ligar(lipo, "+", esc, "BAT+");
            Ligar(lipo, "-", esc, "BAT-");
            foreach (string f in new[] { "A", "B", "C" }) Ligar(esc, f, motor, f);
            Ligar(esc, "5V", rx, "VCC");
            Ligar(esc, "GND", rx, "GND");
            Ligar(rx, "CH3", esc, "SINAL");
            Ligar(rx, "CH1", servo, "PWM");
            Ligar(rx, "VCC", servo, "V+");
            Ligar(rx, "GND", servo, "GND");

            var ed = new EditorCircuito { PassoFixoParaTeste = 0.02 };
            ed.Carregar(c);
            var b = Simulador.App.Bancada.DeUmaSo(new Maquina { Perfil = PerfilPlaca.Esp32 });
            var rede = new Netlist(c);
            void Andar(double s) { for (double t = 0; t < s - 1e-9; t += 0.02) ed.AplicarEntradas(b, rede); }

            Andar(1.5);
            string armado = ed.EscParaTeste("esc").Fase;
            controle.Giro = 0.5;      // acelerador no meio
            controle.Giro3 = 1.0;     // aileron todo para a direita (CH1 = 2000 us)
            Andar(1.5);
            double rpm = ed.RpmDoMotor("motor");
            double servoAngulo = ed.AnguloDoServoParaTeste("servo");
            saida.AppendLine($"        sem placa: ESC {armado}; acelerador 50% -> {rpm:0} rpm; aileron no batente -> servo em {servoAngulo:0} graus");
            Conferir("aeromodelo sem placa: o BEC do ESC alimenta o receptor, e com o acelerador embaixo o ESC arma",
                armado == "Armado", armado);
            // 1500 us no ESC e 55% do acelerador (a faixa dele e 1060..1860): ~6.000 rpm com 11,1 V.
            Conferir("aeromodelo sem placa: o manche esquerdo acelera o brushless pelo CH3 (1500 us = 55%, ~6.000 rpm)",
                rpm is > 5600 and < 6300, $"{rpm:0}");
            Conferir("aeromodelo sem placa: o manche direito move o servo pelo CH1 (2000 us = ~141 graus)",
                Math.Abs(servoAngulo - 141.2) < 1.5, $"{servoAngulo:0.0}");

            // Controle desligado: o receptor para de mandar pulsos, o ESC corta e desarma.
            controle.Bits |= 4;
            Andar(0.5);
            Conferir("failsafe: controle desligado, o receptor para os pulsos e o ESC corta o motor e desarma",
                ed.EscParaTeste("esc").Fase == "AguardandoBaixo" && ed.EscParaTeste("esc").Acelerador == 0,
                ed.EscParaTeste("esc").Fase);
        }

        // ---- a placa lendo o receptor ----
        string Rodar(string sketch, string pinoDoCanal, string canal, string prefixo, double acelerador = 0.5)
        {
            var c = new Circuito();
            var placa = new PecaNoCircuito { Id = "placa", Tipo = "esp32-devkit-v1", X = 0, Y = 0, Usb = true };
            var rx = new PecaNoCircuito { Id = "rx", Tipo = "receptor-rc", X = 400, Y = 0 };
            var controle = new PecaNoCircuito { Id = "controle", Tipo = "radio-controle", X = 700, Y = 0, Giro = acelerador };
            c.Pecas.AddRange([placa, rx, controle]);
            c.Fios.Add(new Fio { DePeca = "placa", DePino = "VIN", ParaPeca = "rx", ParaPino = "VCC" });
            c.Fios.Add(new Fio { DePeca = "placa", DePino = "GND.2", ParaPeca = "rx", ParaPino = "GND" });
            c.Fios.Add(new Fio { DePeca = "placa", DePino = pinoDoCanal, ParaPeca = "rx", ParaPino = canal });

            var janela = new MainWindow();
            try
            {
                janela.TesteAbrir(c, sketch);
                janela.TesteRodar();
                var maquina = janela.TesteMaquinaDa("placa");
                EsperarPor(() => LinhasDoSerial(maquina).Count(l => l.StartsWith(prefixo)) >= 3, 8000);
                return LinhasDoSerial(maquina).LastOrDefault(l => l.StartsWith(prefixo));
            }
            finally
            {
                janela.TesteParar();
                try { janela.Close(); } catch (InvalidOperationException) { }
            }
        }

        static long Numero(string linha)
            => linha is not null && long.TryParse(linha.Split(' ').Last(), out long n) ? n : -1;

        {
            string linha = Rodar("""
                void setup() {
                  Serial.begin(115200);
                  pinMode(13, INPUT);
                }
                void loop() {
                  unsigned long w = pulseIn(13, HIGH, 30000);
                  Serial.print("ch3 ");
                  Serial.println(w);
                  delay(50);
                }
                """, "D13", "CH3", "ch3 ");
            saida.AppendLine($"        pulseIn no CH3 (acelerador no meio): '{linha}'");
            Conferir("placa: pulseIn le o CH3 do receptor (1500 us com o acelerador no meio)",
                Math.Abs(Numero(linha) - 1500) <= 2, linha ?? "(nada)");
        }
        {
            string linha = Rodar("""
                volatile unsigned long subida = 0;
                volatile unsigned long largura = 0;
                void IRAM_ATTR mudou() {
                  if (digitalRead(13)) subida = micros();
                  else largura = micros() - subida;
                }
                void setup() {
                  Serial.begin(115200);
                  pinMode(13, INPUT);
                  attachInterrupt(digitalPinToInterrupt(13), mudou, CHANGE);
                }
                void loop() {
                  delay(200);
                  Serial.print("isr ");
                  Serial.println(largura);
                }
                """, "D13", "CH3", "isr ", acelerador: 0.25);
            saida.AppendLine($"        interrupcao CHANGE no CH3 (acelerador a 25%), medindo no meio do delay: '{linha}'");
            Conferir("placa: a interrupcao mede o pulso do CH3 no meio do delay (1250 us)",
                Math.Abs(Numero(linha) - 1250) <= 3, linha ?? "(nada)");
        }
        {
            string linha = Rodar("""
                volatile unsigned long ultima = 0;
                volatile unsigned long canais[8];
                volatile int canal = 0;
                void IRAM_ATTR borda() {
                  unsigned long agora = micros();
                  unsigned long dt = agora - ultima;
                  ultima = agora;
                  if (dt > 3000) { canal = 0; return; }
                  if (canal < 8) canais[canal++] = dt;
                }
                void setup() {
                  Serial.begin(115200);
                  pinMode(14, INPUT);
                  attachInterrupt(digitalPinToInterrupt(14), borda, RISING);
                }
                void loop() {
                  delay(200);
                  Serial.print("ppm ");
                  Serial.println(canais[2]);
                }
                """, "D14", "PPM", "ppm ", acelerador: 0.75);
            saida.AppendLine($"        PPM no D14, canal 3 (acelerador a 75%): '{linha}'");
            Conferir("placa: o PPM traz os canais num fio so (CH3 = 1750 us)", Math.Abs(Numero(linha) - 1750) <= 3, linha ?? "(nada)");
        }
        {
            string linha = Rodar("""
                uint8_t quadro[32];
                int n = 0;
                void setup() {
                  Serial.begin(115200);
                  Serial2.begin(115200, SERIAL_8N1, 16, 17);
                }
                void loop() {
                  while (Serial2.available()) {
                    uint8_t b = Serial2.read();
                    if (n == 0 && b != 0x20) continue;
                    if (n == 1 && b != 0x40) { n = 0; continue; }
                    quadro[n++] = b;
                    if (n == 32) {
                      n = 0;
                      uint16_t ch3 = quadro[6] | (quadro[7] << 8);
                      Serial.print("ibus ");
                      Serial.println(ch3);
                    }
                  }
                  delay(20);
                }
                """, "RX2", "IBUS", "ibus ", acelerador: 0.6);
            saida.AppendLine($"        iBUS no RX2 (Serial2 a 115200), CH3 com o acelerador a 60%: '{linha}'");
            Conferir("placa: o iBUS chega na Serial2 (o quadro 0x20 0x40 com CH3 = 1600)", Numero(linha) == 1600, linha ?? "(nada)");
        }
    }
}
