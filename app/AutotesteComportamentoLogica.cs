namespace Simulador.App;

/// <summary>
/// A logica e os modulos de ponta a ponta: a placa manda nas entradas pelos
/// GPIOs (digitalWrite) e le a saida num GPIO de entrada (digitalRead), pelo
/// editor, como no circuito de verdade -- e nao a porta sozinha entre os
/// trilhos, como o Pecas_Logica. A tabela-verdade inteira de cada porta, o
/// MUX nas oito combinacoes, os flip-flops com o clock vindo do sketch, o
/// gerador de clock contado pela placa, o analisador canal a canal e o
/// consumo dos modulos (o que eles fazem -- o som do MAX98357A e do PCM5102A,
/// o radio do nRF24L01, o LCD do modulo I2C -- tem os seus blocos).
/// </summary>
public static partial class Autoteste
{
    static void Comportamento_LogicaEModulos()
    {
        saida.AppendLine("comportamento: logica e modulos, pela placa");

        void Peca(string tipo, Action<SaidaEmTeste> testar)
        {
            try { testar(new SaidaEmTeste(tipo)); }
            catch (Exception e) { Conferir($"{tipo}: montagem e execucao -> sem excecao", false, $"{e.GetType().Name}: {e.Message}"); }
        }

        // Uma entrada da peca comandada pela placa, e uma saida lida por ela.
        static void Entrada(SaidaEmTeste t, string pino, int gpio) => t.Sinal(pino, gpio);
        static void Saida(SaidaEmTeste t, string pino, int gpio)
        {
            t.Sinal(pino, gpio);
            t.M.PinMode(gpio, 0);   // INPUT: quem manda no pino e a peca
        }

        // ---- as portas de duas entradas: as quatro linhas da tabela ----
        foreach (var (tipo, tabela) in new (string, int[])[]
                 {
                     ("and-gate", [0, 0, 0, 1]), ("nand-gate", [1, 1, 1, 0]), ("or-gate", [0, 1, 1, 1]),
                     ("nor-gate", [1, 0, 0, 0]), ("xor-gate", [0, 1, 1, 0]), ("xnor-gate", [1, 0, 0, 1]),
                 })
            Peca(tipo, t =>
            {
                Entrada(t, "A", 13);
                Entrada(t, "B", 14);
                Saida(t, "Q", 25);
                for (int linha = 0; linha < 4; linha++)
                {
                    int a = linha >> 1, b = linha & 1;
                    t.M.DigitalWrite(13, a);
                    t.M.DigitalWrite(14, b);
                    t.Quadro(2);
                    int q = t.M.DigitalRead(25);
                    t.Ver($"A={a} B={b}", $"Q={tabela[linha]}", q == tabela[linha], $"Q={q}");
                }
            });

        Peca("not-gate", t =>
        {
            Entrada(t, "A", 13);
            Saida(t, "Q", 25);
            foreach (int a in new[] { 0, 1, 0 })
            {
                t.M.DigitalWrite(13, a);
                t.Quadro(2);
                int q = t.M.DigitalRead(25);
                t.Ver($"A={a}", $"Q={1 - a}", q == 1 - a, $"Q={q}");
            }
        });

        Peca("mux-2", t =>
        {
            Entrada(t, "A", 13);
            Entrada(t, "B", 14);
            Entrada(t, "SEL", 26);
            Saida(t, "Q", 25);
            for (int k = 0; k < 8; k++)
            {
                int a = k & 1, b = (k >> 1) & 1, sel = k >> 2;
                t.M.DigitalWrite(13, a);
                t.M.DigitalWrite(14, b);
                t.M.DigitalWrite(26, sel);
                t.Quadro(2);
                int q = t.M.DigitalRead(25), esperado = sel == 1 ? b : a;
                t.Ver($"A={a} B={b} SEL={sel}", $"Q={esperado} (a entrada {(sel == 1 ? "B" : "A")})", q == esperado, $"Q={q}");
            }
        });

        // ---- os flip-flops: o clock vem do sketch ----
        Peca("flip-flop-d", t =>
        {
            Entrada(t, "D", 13);
            Entrada(t, "CLK", 14);
            Saida(t, "Q", 25);
            Saida(t, "NQ", 26);
            void Passo(int d, int clk) { t.M.DigitalWrite(13, d); t.M.DigitalWrite(14, clk); t.Quadro(2); }
            Passo(1, 0);
            Passo(1, 1);
            t.Ver("D=1 e o clock sobe", "Q=1 e NQ=0", t.M.DigitalRead(25) == 1 && t.M.DigitalRead(26) == 0, $"Q={t.M.DigitalRead(25)} NQ={t.M.DigitalRead(26)}");
            Passo(0, 1);
            t.Ver("D vai a 0 com o clock parado em cima", "Q guarda o 1", t.M.DigitalRead(25) == 1, $"Q={t.M.DigitalRead(25)}");
            Passo(0, 0);
            t.Ver("o clock desce", "Q guarda o 1 (so a subida conta)", t.M.DigitalRead(25) == 1, $"Q={t.M.DigitalRead(25)}");
            Passo(0, 1);
            t.Ver("D=0 e o clock sobe de novo", "Q=0 e NQ=1", t.M.DigitalRead(25) == 0 && t.M.DigitalRead(26) == 1, $"Q={t.M.DigitalRead(25)} NQ={t.M.DigitalRead(26)}");
        });

        Peca("flip-flop-sr", t =>
        {
            Entrada(t, "S", 13);
            Entrada(t, "R", 14);
            Entrada(t, "CLK", 27);
            Saida(t, "Q", 25);
            Saida(t, "NQ", 26);
            void Borda(int s, int r)
            {
                t.M.DigitalWrite(13, s);
                t.M.DigitalWrite(14, r);
                t.M.DigitalWrite(27, 0);
                t.Quadro(2);
                t.M.DigitalWrite(27, 1);
                t.Quadro(2);
            }
            Borda(1, 0);
            t.Ver("S=1 na subida do clock", "Q=1", t.M.DigitalRead(25) == 1 && t.M.DigitalRead(26) == 0, $"Q={t.M.DigitalRead(25)} NQ={t.M.DigitalRead(26)}");
            Borda(0, 0);
            t.Ver("S=0 R=0 na subida", "Q guarda o 1", t.M.DigitalRead(25) == 1, $"Q={t.M.DigitalRead(25)}");
            Borda(0, 1);
            t.Ver("R=1 na subida do clock", "Q=0", t.M.DigitalRead(25) == 0 && t.M.DigitalRead(26) == 1, $"Q={t.M.DigitalRead(25)} NQ={t.M.DigitalRead(26)}");
            t.M.DigitalWrite(13, 1);
            t.M.DigitalWrite(14, 0);
            t.Quadro(2);
            t.Ver("S=1 sem borda do clock", "Q continua 0 (o SR e com clock)", t.M.DigitalRead(25) == 0, $"Q={t.M.DigitalRead(25)}");
        });

        Peca("flip-flop-dsr", t =>
        {
            Entrada(t, "D", 13);
            Entrada(t, "S", 14);
            Entrada(t, "R", 26);
            Entrada(t, "CLK", 27);
            Saida(t, "Q", 25);
            void Pinos(int d, int s, int r, int clk)
            {
                t.M.DigitalWrite(13, d);
                t.M.DigitalWrite(14, s);
                t.M.DigitalWrite(26, r);
                t.M.DigitalWrite(27, clk);
                t.Quadro(2);
            }
            Pinos(0, 1, 0, 0);
            t.Ver("S=1 sem clock", "Q=1 na hora (o set e assincrono)", t.M.DigitalRead(25) == 1, $"Q={t.M.DigitalRead(25)}");
            Pinos(0, 0, 1, 0);
            t.Ver("R=1 sem clock", "Q=0 na hora (o reset e assincrono)", t.M.DigitalRead(25) == 0, $"Q={t.M.DigitalRead(25)}");
            Pinos(1, 0, 0, 0);
            Pinos(1, 0, 0, 1);
            t.Ver("S=R=0, D=1 e o clock sobe", "Q=1 (o D)", t.M.DigitalRead(25) == 1, $"Q={t.M.DigitalRead(25)}");
        });

        // ---- o gerador de clock, contado pela placa ----
        foreach (var (giro, hz) in new[] { (0.0, 0.5), (0.5, 10.25), (1.0, 20.0) })
            Peca("clock-generator", t =>
            {
                t.P.Giro = giro;
                Saida(t, "CLK", 25);
                int subidas = 0, antes = t.M.DigitalRead(25);
                for (int k = 0; k < 200; k++)   // 4 s, de 20 em 20 ms
                {
                    t.Quadro(1);
                    int agora = t.M.DigitalRead(25);
                    if (agora == 1 && antes == 0) subidas++;
                    antes = agora;
                }
                double esperado = hz * 4;
                t.Ver($"ajuste {giro:0.0}, contado por 4 s", $"~{esperado:0} subidas ({hz:0.##} Hz)", Math.Abs(subidas - esperado) <= Math.Max(1, esperado * 0.1),
                    $"{subidas} subidas");
            });

        // ---- o analisador: cada canal enxerga o GPIO da sua rede ----
        Peca("logic-analyzer", t =>
        {
            int[] gpios = [13, 14, 25, 26, 27, 32, 33, 4];
            for (int k = 0; k < 8; k++) Entrada(t, $"D{k}", gpios[k]);
            t.Ligar("GND", "GND.1");
            t.Quadro();
            var fontes = Analisador.Fontes(t.C, new Netlist(t.C), Simulador.App.Bancada.DeUmaSo(t.M), t.P);
            for (int k = 0; k < 8; k++)
            {
                var f = fontes.FirstOrDefault(x => x.Canal == k);
                t.Ver($"canal D{k} no GPIO {gpios[k]}", "enxerga esse GPIO", f.Ligada && f.Gpio == gpios[k], f.Ligada ? $"GPIO {f.Gpio}" : "solto");
            }
        });

        // ---- os modulos: ligados puxam corrente; sem o VCC, nada ----
        foreach (var (tipo, vcc, gnd) in new[] { ("max98357a", "VIN", "GND"), ("pcm5102a", "VIN", "GND"), ("nrf24l01", "VCC", "GND"), ("modulo-i2c", "VCC", "GND") })
            Peca(tipo, t =>
            {
                t.Ligar(gnd, "GND.1");
                t.Quadro();
                double solto = t.Ed.EnergiaDoQuadro?.CorrenteDaPeca(t.P.Id) ?? 0;
                t.Ligar(vcc, tipo == "nrf24l01" ? "3V3" : "VIN");
                t.Remontar();
                t.Quadro();
                double ligado = t.Corrente;
                t.Ver($"{vcc} solto e depois ligado", "so ligado puxa corrente", Math.Abs(solto) < 0.0001 && ligado > 0.001,
                    $"solto={solto * 1000:0.###} mA, ligado={ligado * 1000:0.###} mA");
            });
    }
}
