namespace Simulador.App;

/// <summary>
/// As ESP32 redesenhadas (Alpha 25, placas-esp32.py): a barra de verdade de
/// cada DevKit, com cada pino fazendo o que faz na placa -- os GPIO no GPIO
/// (o TX/RX da serigrafia no da UART0 de cada chip), os de energia
/// alimentando, o EN (o RST da serigrafia) no GND segurando a placa, o BOOT
/// levando o pino de boot a LOW e o LED RGB soldado acendendo na cor que o
/// sketch manda. E o que muda por fora: os nomes do Wokwi vao e voltam, e o
/// fio de um projeto antigo num pino que a placa real nao tem passa para o
/// pino de verdade.
/// </summary>
public static partial class Autoteste
{
    static readonly string[] PlacasEsp32 =
        ["esp32-devkit-v1", "esp32-s2-devkit", "esp32-s3-devkit", "esp32-c3-devkit", "esp32-c6-devkit", "esp32-h2-devkit"];

    static void Placas_Esp32()
    {
        saida.AppendLine("placas ESP32: a barra de verdade e a funcao de cada pino");

        static void Ligar(Circuito c, string a, string pa, string b, string pb)
            => c.Fios.Add(new Fio { DePeca = a, DePino = pa, ParaPeca = b, ParaPino = pb });

        static string Base(string n) => n.Contains('.') && n[(n.LastIndexOf('.') + 1)..].All(char.IsAsciiDigit) ? n[..n.LastIndexOf('.')] : n;

        // ---- cada pino tem uma funcao, e a barra tem o passo de 2,54 mm ----
        foreach (string tipo in PlacasEsp32)
        {
            var def = Catalogo.Obter(tipo);
            var perfil = PerfilPlaca.De(tipo);
            var sem = new List<string>();
            int gpios = 0, energia = 0, especiais = 0;
            foreach (var p in def.Pinos)
            {
                if (perfil.Numero(p.Nome) >= 0) gpios++;
                else if (Netlist.EhPinoDeAlimentacao(p.Nome) || Netlist.EhPinoDeTerra(p.Nome)) energia++;
                else if (Base(p.Nome) is "EN" or "NC") especiais++;
                else sem.Add(p.Nome);
            }
            Conferir($"{tipo}: todo pino tem funcao ({gpios} GPIO, {energia} de energia, {especiais} EN/NC)",
                sem.Count == 0 && def.Pinos.Any(p => p.Nome == "EN"), "sem funcao: " + string.Join(", ", sem));

            // duas colunas, os furos de 2,54 em 2,54 mm (9,6 px), dentro da caixa
            var colunas = def.Pinos.GroupBy(p => Math.Round(p.X)).ToList();
            bool passo = colunas.Count == 2 && colunas.All(col =>
            {
                var ys = col.Select(p => p.Y).OrderBy(y => y).ToList();
                return ys.Zip(ys.Skip(1), (a, b) => b - a).All(d => Math.Abs(d - 9.6) < 0.1);
            });
            bool dentro = def.Pinos.All(p => p.X > 0 && p.X < def.Largura && p.Y > 0 && p.Y < def.Altura);
            double entre = colunas.Count == 2 ? Math.Abs(colunas[0].Key - colunas[1].Key) : 0;
            Conferir($"{tipo}: duas barras no passo de 2,54 mm, a {entre / 96 * 25.4:0.00} mm uma da outra, dentro da caixa",
                passo && dentro && Math.Abs(entre - (tipo == "esp32-devkit-v1" ? 96 : 86.4)) < 0.6,
                $"colunas {colunas.Count}, passo {passo}, dentro {dentro}, entre {entre:0.0} px");
        }

        // ---- a barra na ordem da placa real (a do guia da Espressif) ----
        foreach (var (tipo, ordem) in new (string, string)[]
                 {
                     ("esp32-s3-devkit", "3V3 3V3.2 EN 4 5 6 7 15 16 17 18 8 3 46 9 10 11 12 13 14 5V GND"),
                     ("esp32-c3-devkit", "GND 3V3 3V3.2 2 3 GND.2 EN GND.3 0 1 10 GND.4 5V 5V.2 GND.5"),
                     ("esp32-c6-devkit", "3V3 EN 4 5 6 7 0 1 8 10 11 2 3 5V GND NC"),
                     ("esp32-h2-devkit", "3V3 EN 0 1 2 3 13 14 4 5 NC VBAT GND 5V GND.2"),
                     ("esp32-s2-devkit", "3V3 0 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15 16 17 5V GND"),
                     ("esp32-devkit-v1", "EN VP VN D34 D35 D32 D33 D25 D26 D27 D14 D12 D13 GND.2 VIN"),
                 })
        {
            var def = Catalogo.Obter(tipo);
            double xEsq = def.Pinos.Min(p => p.X);
            string achada = string.Join(" ", def.Pinos.Where(p => Math.Abs(p.X - xEsq) < 1).OrderBy(p => p.Y).Select(p => p.Nome));
            Conferir($"{tipo}: a barra da esquerda, de cima para baixo, e a da placa real", achada == ordem, achada);
        }

        // ---- o TX e o RX da serigrafia sao os da UART0 de cada chip ----
        foreach (var (tipo, tx, rx) in new (string, int, int)[]
                 {
                     ("esp32-s3-devkit", 43, 44), ("esp32-s2-devkit", 43, 44), ("esp32-c3-devkit", 21, 20),
                     ("esp32-c6-devkit", 16, 17), ("esp32-h2-devkit", 24, 23), ("esp32-devkit-v1", 1, 3),
                 })
        {
            var c = new Circuito();
            c.Pecas.Add(new PecaNoCircuito { Id = "placa", Tipo = tipo, X = 0, Y = 0 });
            c.Pecas.Add(new PecaNoCircuito { Id = "a", Tipo = "led", X = 600, Y = 300 });
            c.Pecas.Add(new PecaNoCircuito { Id = "b", Tipo = "led", X = 600, Y = 500 });
            bool v1 = tipo == "esp32-devkit-v1";
            Ligar(c, "placa", v1 ? "TX0" : "TX", "a", "A");
            Ligar(c, "placa", v1 ? "RX0" : "RX", "b", "A");
            var rede = new Netlist(c);
            int gtx = rede.GpioDe(c, "a", "A"), grx = rede.GpioDe(c, "b", "A");
            Conferir($"{tipo}: o TX chega no GPIO {tx} e o RX no {rx}", gtx == tx && grx == rx, $"{gtx}/{grx}");
        }

        // ---- os nomes do Wokwi vao e voltam ----
        foreach (var (tipo, deles) in new (string, string)[]
                 {
                     ("esp32-s3-devkit", "3V3.1 3V3.2 RST 4 5 6 7 15 16 17 18 8 3 46 9 10 11 12 13 14 5V GND.1 GND.2 TX RX 1 2 42 41 40 39 38 37 36 35 0 45 48 47 21 20 19 GND.3 GND.4"),
                     ("esp32-s2-devkit", "3V3 0 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15 16 17 5V GND.1 18 19 20 21 26 33 34 35 36 37 38 39 40 41 42 TX RX 45 46 RST GND.2"),
                     ("esp32-c3-devkit", "GND.1 3V3.1 3V3.2 2 3 GND.2 RST GND.3 0 1 10 GND.4 5V.1 5V.2 GND.5 GND.6 19 18 GND.7 4 5 6 7 GND.8 8 9 GND.9 RX TX GND.10"),
                     ("esp32-c6-devkit", "3V3 RST 4 5 6 7 0 1 8 10 11 2 3 5V GND.1 NC0 NC1 GND.2 12 13 GND.3 9 18 19 20 21 22 23 15 RX TX GND.4"),
                     ("esp32-h2-devkit", "3V3 RST 0 1 2 3 13 14 4 5 NC VBAT GND.1 5V GND.2 GND.3 26 27 GND.4 9 GND.5 22 8 12 25 11 10 RX TX GND.6"),
                     ("esp32-devkit-v1", "EN VN VP D34 D35 D32 D33 D25 D26 D27 D14 D12 D13 GND.2 VIN 3V3 GND.1 D15 D2 D4 RX2 TX2 D5 D18 D19 D21 RX0 TX0 D22 D23"),
                 })
        {
            var def = Catalogo.Obter(tipo);
            var nossos = def.Pinos.Select(p => p.Nome).ToHashSet();
            var faltam = deles.Split(' ').Where(n => FormatoWokwi.PinoDoWokwi(tipo, n) is not { } nosso || !nossos.Contains(nosso)).ToList();
            var idaEVolta = def.Pinos.Where(p => FormatoWokwi.PinoDoWokwi(tipo, FormatoWokwi.PinoParaWokwi(tipo, p.Nome)) != p.Nome)
                                     .Select(p => p.Nome).ToList();
            var foraDeles = def.Pinos.Select(p => FormatoWokwi.PinoParaWokwi(tipo, p.Nome)).Where(n => !deles.Split(' ').Contains(n)).ToList();
            Conferir($"{tipo}: cada pino do Wokwi tem o seu aqui, e cada um daqui vai e volta pelo nome de la",
                faltam.Count == 0 && idaEVolta.Count == 0 && foraDeles.Count == 0,
                $"sem par: {string.Join(",", faltam)}; nao voltam: {string.Join(",", idaEVolta)}; fora: {string.Join(",", foraDeles)}");
        }

        // ---- o projeto antigo: o fio no 16/17 da C6 (e no 24/23 da H2) passa para o TX/RX ----
        {
            var c = new Circuito();
            c.Pecas.Add(new PecaNoCircuito { Id = "c6", Tipo = "esp32-c6-devkit", X = 0, Y = 0 });
            c.Pecas.Add(new PecaNoCircuito { Id = "h2", Tipo = "esp32-h2-devkit", X = 300, Y = 0 });
            c.Pecas.Add(new PecaNoCircuito { Id = "led", Tipo = "led", X = 600, Y = 300 });
            Ligar(c, "c6", "16", "led", "A");
            Ligar(c, "led", "C", "h2", "23");
            Ligar(c, "c6", "5", "h2", "5");
            var lido = Circuito.DeJson(c.ParaJson());
            string pinos = string.Join(" ", lido.Fios.Select(f => $"{f.DePino}-{f.ParaPino}"));
            Conferir("projeto antigo: o fio no 16 da C6 vai para o TX, o do 23 da H2 para o RX; o resto fica",
                pinos == "TX-A C-RX 5-5" && !lido.FiosQuebrados().Any(), pinos);
        }

        // ---- o EN no GND segura a placa (o RST da serigrafia) ----
        foreach (var (tipo, gnd) in new[] { ("esp32-devkit-v1", "GND.2"), ("esp32-s3-devkit", "GND"), ("esp32-c3-devkit", "GND.3"),
                                            ("esp32-h2-devkit", "GND") })
        {
            var c = new Circuito();
            c.Pecas.Add(new PecaNoCircuito { Id = "placa", Tipo = tipo, X = 0, Y = 0, Usb = true });
            var ed = new EditorCircuito { EmSimulacao = true };
            ed.Carregar(c);
            var maq = new Maquina { Perfil = PerfilPlaca.De(tipo) };
            maq.Reiniciar();
            var b = Simulador.App.Bancada.DeUmaSo(maq);
            ed.AplicarEntradas(b, new Netlist(c));
            bool solto = !ed.ResetPorFioParaTeste("placa");
            Ligar(c, "placa", "EN", "placa", gnd);
            ed.AplicarEntradas(b, new Netlist(c));
            bool preso = ed.ResetPorFioParaTeste("placa");
            c.Fios.Clear();
            ed.AplicarEntradas(b, new Netlist(c));
            Conferir($"{tipo}: o EN ligado no {gnd} segura a placa em reset; tirou o fio, ela volta",
                solto && preso && !ed.ResetPorFioParaTeste("placa"), $"{solto}/{preso}");
        }

        // ---- os botoes: dois em cada placa; o BOOT leva o pino de boot a LOW ----
        foreach (var (tipo, boot) in new[] { ("esp32-devkit-v1", 0), ("esp32-s2-devkit", 0), ("esp32-s3-devkit", 0),
                                             ("esp32-c3-devkit", 9), ("esp32-c6-devkit", 9), ("esp32-h2-devkit", 9) })
        {
            var def = Catalogo.Obter(tipo);
            var botoes = EditorCircuito.BotoesPorPlaca[tipo];
            bool dentro = botoes.Length == 2 && botoes.Count(x => x.Nome == "BOOT") == 1
                          && botoes.All(x => x.X > 0 && x.X < def.Largura && x.Y > 0 && x.Y < def.Altura);
            var c = new Circuito();
            c.Pecas.Add(new PecaNoCircuito { Id = "placa", Tipo = tipo, X = 0, Y = 0, Usb = true });
            var ed = new EditorCircuito { EmSimulacao = true };
            ed.Carregar(c);
            var maq = new Maquina { Perfil = PerfilPlaca.De(tipo) };
            maq.Reiniciar();
            var b = Simulador.App.Bancada.DeUmaSo(maq);
            ed.AplicarEntradas(b, new Netlist(c));
            bool antes = maq.Pressionado[boot];
            ed.BootPressionado.Add("placa");
            ed.AplicarEntradas(b, new Netlist(c));
            bool apertado = maq.Pressionado[boot];
            ed.BootPressionado.Clear();
            ed.AplicarEntradas(b, new Netlist(c));
            Conferir($"{tipo}: BOOT e reset desenhados na caixa; o BOOT apertado leva o GPIO {boot} a LOW",
                dentro && !antes && apertado && !maq.Pressionado[boot], $"{dentro}/{antes}/{apertado}");
        }

        // ---- os LEDs soldados: o de energia e o RGB na cor do sketch ----
        foreach (string tipo in PlacasEsp32.Where(PerfilPlaca.TemLedRgb))
        {
            var c = new Circuito();
            c.Pecas.Add(new PecaNoCircuito { Id = "placa", Tipo = tipo, X = 0, Y = 0, Usb = true });
            var ed = new EditorCircuito();
            ed.Carregar(c);
            var perfil = PerfilPlaca.De(tipo);
            var maq = new Maquina { Perfil = perfil };
            maq.Reiniciar();
            string erro = null;
            try
            {
                var prog = new Parser(Lexer.Tokenizar("void setup() { neopixelWrite(RGB_BUILTIN, 0, 0, 200); }" + Environment.NewLine + "void loop() {}")).AnalisarPrograma();
                var interp = new Interpretador(prog, maq);
                interp.Preparar();
                interp.Setup();
            }
            catch (Exception e) { erro = e.Message; }
            var b = Simulador.App.Bancada.DeUmaSo(maq);
            var rede = new Netlist(c);
            ed.RefletirEstado(b, rede);
            var forca = ed.LedsDaPlacaParaTeste("placa");
            var cores = ed.CoresDosLedsDaPlacaParaTeste("placa");
            bool azul = cores.Length == 2 && cores[1].B > 200 && cores[1].R < 40 && cores[1].G < 40;
            maq.Fitas.Clear();
            maq.Pwm[perfil.LedInterno] = 255;                 // digitalWrite(RGB_BUILTIN, HIGH): branco fraco
            ed.RefletirEstado(b, rede);
            var branco = ed.CoresDosLedsDaPlacaParaTeste("placa");
            var forcaBranco = ed.LedsDaPlacaParaTeste("placa");
            Conferir($"{tipo}: o LED de energia acende; o RGB (GPIO {perfil.LedInterno}) fica azul com neopixelWrite e branco com digitalWrite",
                erro is null && forca.Length == 2 && forca[0] > 0.5 && forca[1] > 0.4 && azul
                && branco[1] == System.Windows.Media.Colors.White && forcaBranco[1] > 0.2,
                erro ?? $"{string.Join("/", forca.Select(v => v.ToString("0.00")))} {cores.LastOrDefault()} {forcaBranco.LastOrDefault():0.00}");
        }
        {
            var c = new Circuito();
            c.Pecas.Add(new PecaNoCircuito { Id = "placa", Tipo = "esp32-devkit-v1", X = 0, Y = 0, Usb = true });
            var ed = new EditorCircuito();
            ed.Carregar(c);
            var maq = new Maquina { Perfil = PerfilPlaca.Esp32 };
            maq.Reiniciar();
            var b = Simulador.App.Bancada.DeUmaSo(maq);
            var rede = new Netlist(c);
            ed.RefletirEstado(b, rede);
            var apagado = ed.LedsDaPlacaParaTeste("placa");
            maq.Pwm[2] = 255;
            ed.RefletirEstado(b, rede);
            var aceso = ed.LedsDaPlacaParaTeste("placa");
            Conferir("esp32-devkit-v1: o LED vermelho de energia acende e o azul segue o GPIO 2",
                apagado.Length == 2 && apagado[0] > 0.5 && apagado[1] < 0.05 && aceso[1] > 0.5,
                string.Join("/", apagado.Select(v => v.ToString("0.00"))) + " -> " + string.Join("/", aceso.Select(v => v.ToString("0.00"))));
        }
        {
            string erro = null;
            var maq = new Maquina { Perfil = PerfilPlaca.Esp32 };
            maq.Reiniciar();
            try
            {
                var prog = new Parser(Lexer.Tokenizar("void setup() { neopixelWrite(5, 1, 2, 3); }" + Environment.NewLine + "void loop() {}")).AnalisarPrograma();
                var interp = new Interpretador(prog, maq);
                interp.Preparar();
                interp.Setup();
            }
            catch (Exception e) { erro = e.Message; }
            Conferir("neopixelWrite numa fita do pino 5 da DevKit V1: uma fita de um LED com a cor",
                erro is null && maq.Fitas.TryGetValue(5, out var f) && f.Cores[0] == 0x010203, erro ?? "sem fita");
        }
    }
}
