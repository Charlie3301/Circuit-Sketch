namespace Simulador.App;

/// <summary>
/// As Pico redesenhadas (Alpha 25, placas-pico.py): os 40 furos castelados no
/// lugar da placa de verdade e os tres do conector de depuracao, com cada pino
/// fazendo o que faz nela -- o RUN no GND segura o RP2040, o 3V3_EN no GND
/// desliga o regulador, o ADC_VREF e a referencia do conversor, o AGND e
/// terra --, o BOOTSEL que o sketch le (nas tres formas: BOOTSEL e
/// rp2040.getBootselButton() no C++, rp2.bootsel_button() no MicroPython) e o
/// LED verde no LED_BUILTIN de cada uma.
/// </summary>
public static partial class Autoteste
{
    static readonly string[] PlacasPico = ["pi-pico", "pi-pico-w"];

    static void Placas_Pico()
    {
        saida.AppendLine("placas Pico: a barra de verdade e a funcao de cada pino");

        static void Ligar(Circuito c, string a, string pa, string b, string pb)
            => c.Fios.Add(new Fio { DePeca = a, DePino = pa, ParaPeca = b, ParaPino = pb });

        static string Base(string n) => n.Contains('.') && n[(n.LastIndexOf('.') + 1)..].All(char.IsAsciiDigit) ? n[..n.LastIndexOf('.')] : n;

        foreach (string tipo in PlacasPico)
        {
            var def = Catalogo.Obter(tipo);
            var perfil = PerfilPlaca.De(tipo);

            // ---- cada pino tem uma funcao ----
            var sem = new List<string>();
            int gpios = 0, energia = 0, especiais = 0;
            foreach (var p in def.Pinos)
            {
                if (perfil.Numero(p.Nome) >= 0) gpios++;
                else if (Netlist.EhPinoDeAlimentacao(p.Nome) || Netlist.EhPinoDeTerra(p.Nome)) energia++;
                else if (Base(p.Nome) is "RUN" or "3V3_EN" or "ADC_VREF" or "VSYS") especiais++;
                else if (p.Nome is "SWCLK" or "SWDIO" && !string.IsNullOrEmpty(p.Descricao)) especiais++;
                else sem.Add(p.Nome);
            }
            Conferir($"{tipo}: todo pino tem funcao ({gpios} GPIO, {energia} de energia, {especiais} RUN/3V3_EN/ADC_VREF/VSYS/SWD)",
                sem.Count == 0 && gpios == 26 && def.Pinos.Count == 43, "sem funcao: " + string.Join(", ", sem));

            // ---- as duas barras de 20 no passo de 2,54 mm, a 17,78 mm uma da outra ----
            var borda = def.Pinos.Where(p => p.Nome is not ("SWCLK" or "SWDIO" or "GND.8")).ToList();
            var colunas = borda.GroupBy(p => Math.Round(p.X)).OrderBy(g => g.Key).ToList();
            bool passo = colunas.Count == 2 && colunas.All(col =>
            {
                var ys = col.Select(p => p.Y).OrderBy(y => y).ToList();
                return ys.Count == 20 && ys.Zip(ys.Skip(1), (a, b) => b - a).All(d => Math.Abs(d - 9.6) < 0.1);
            });
            double entre = colunas.Count == 2 ? colunas[1].Key - colunas[0].Key : 0;
            Conferir($"{tipo}: duas barras de 20 no passo de 2,54 mm, a {entre / 96 * 25.4:0.00} mm uma da outra",
                passo && Math.Abs(entre - 67.2) < 0.6, $"colunas {colunas.Count}, passo {passo}, entre {entre:0.0} px");

            // ---- a ordem do pinout oficial (pino 1 em cima, a esquerda; o 40 em cima, a direita) ----
            if (colunas.Count == 2)
            {
                string esq = string.Join(" ", colunas[0].OrderBy(p => p.Y).Select(p => Base(p.Nome)));
                string dir = string.Join(" ", colunas[1].OrderBy(p => p.Y).Select(p => Base(p.Nome)));
                Conferir($"{tipo}: as barras na ordem do pinout oficial",
                    esq == "GP0 GP1 GND GP2 GP3 GP4 GP5 GND GP6 GP7 GP8 GP9 GND GP10 GP11 GP12 GP13 GND GP14 GP15"
                    && dir == "VBUS VSYS GND 3V3_EN 3V3 ADC_VREF GP28 AGND GP27 GP26 RUN GP22 GND GP21 GP20 GP19 GP18 GND GP17 GP16",
                    esq + " | " + dir);
            }

            // ---- o conector de depuracao: SWCLK, GND, SWDIO em fila, de 2,54 em 2,54 mm ----
            {
                var swd = new[] { "SWCLK", "GND.8", "SWDIO" }.Select(n => def.Pinos.FirstOrDefault(p => p.Nome == n)).ToList();
                bool fila = swd.All(p => p is not null) && Math.Abs(swd[0].Y - swd[2].Y) < 0.1
                            && Math.Abs(swd[1].X - swd[0].X - 9.6) < 0.2 && Math.Abs(swd[2].X - swd[1].X - 9.6) < 0.2
                            && swd.All(p => p.X > 0 && p.X < def.Largura && p.Y > 0 && p.Y < def.Altura);
                Conferir($"{tipo}: o SWD (SWCLK, GND, SWDIO) em fila de 2,54 mm, dentro da placa", fila,
                    string.Join(" ", swd.Select(p => p is null ? "?" : $"{p.Nome}({p.X:0.0},{p.Y:0.0})")));
            }

            // ---- o RUN e o 3V3_EN no GND seguram a placa ----
            foreach (var (pino, gnd) in new[] { ("RUN", "GND.7"), ("3V3_EN", "GND.5") })
            {
                var c = new Circuito();
                c.Pecas.Add(new PecaNoCircuito { Id = "placa", Tipo = tipo, X = 0, Y = 0, Usb = true });
                var ed = new EditorCircuito { EmSimulacao = true };
                ed.Carregar(c);
                var maq = new Maquina { Perfil = perfil };
                maq.Reiniciar();
                var b = Simulador.App.Bancada.DeUmaSo(maq);
                ed.AplicarEntradas(b, new Netlist(c));
                bool solto = !ed.ResetPorFioParaTeste("placa");
                Ligar(c, "placa", pino, "placa", gnd);
                ed.AplicarEntradas(b, new Netlist(c));
                bool preso = ed.ResetPorFioParaTeste("placa") && ed.PinoQueSegura("placa") == pino;
                c.Fios.Clear();
                ed.AplicarEntradas(b, new Netlist(c));
                Conferir($"{tipo}: o {pino} ligado no GND segura a placa (e a mensagem diz qual); tirou o fio, ela volta",
                    solto && preso && !ed.ResetPorFioParaTeste("placa") && ed.PinoQueSegura("placa") is null, $"{solto}/{preso}");
            }

            // ---- o BOOTSEL: desenhado na caixa, e o sketch le ----
            {
                var botoes = EditorCircuito.BotoesPorPlaca[tipo];
                bool dentro = botoes.Length == 1 && botoes[0].Nome == "BOOTSEL"
                              && botoes[0].X > 0 && botoes[0].X < def.Largura && botoes[0].Y > 0 && botoes[0].Y < def.Altura;
                var c = new Circuito();
                c.Pecas.Add(new PecaNoCircuito { Id = "placa", Tipo = tipo, X = 0, Y = 0, Usb = true });
                var ed = new EditorCircuito { EmSimulacao = true };
                ed.Carregar(c);
                var maq = new Maquina { Perfil = perfil };
                maq.Reiniciar();
                var b = Simulador.App.Bancada.DeUmaSo(maq);
                ed.AplicarEntradas(b, new Netlist(c));
                bool antes = maq.BootselApertado;
                ed.BootPressionado.Add("placa");
                ed.AplicarEntradas(b, new Netlist(c));
                bool apertado = maq.BootselApertado;
                ed.BootPressionado.Clear();
                ed.AplicarEntradas(b, new Netlist(c));
                Conferir($"{tipo}: o BOOTSEL fica na tampa da chave, dentro da placa, e apertar chega na maquina",
                    dentro && !antes && apertado && !maq.BootselApertado, $"{dentro}/{antes}/{apertado}");
            }

            foreach (bool apertado in new[] { false, true })
            {
                string erro = null, cpp = "", py = "";
                try
                {
                    var maq = new Maquina { Perfil = perfil };
                    maq.Reiniciar();
                    maq.BootselApertado = apertado;
                    var prog = new Parser(Lexer.Tokenizar(
                        "void setup() { Serial.begin(115200); Serial.println(BOOTSEL ? 7 : 3); Serial.println(rp2040.getBootselButton() ? 7 : 3); }"
                        + Environment.NewLine + "void loop() {}")).AnalisarPrograma();
                    var interp = new Interpretador(prog, maq);
                    interp.Preparar();
                    interp.Setup();
                    cpp = SoOTexto(maq);

                    var maqPy = new Maquina { Perfil = perfil };
                    maqPy.Reiniciar();
                    maqPy.BootselApertado = apertado;
                    var progPy = Compilador.Compilar("import rp2\nprint(rp2.bootsel_button())", Linguagem.MicroPython);
                    var interpPy = new Interpretador(progPy, maqPy);
                    interpPy.Preparar();
                    interpPy.Setup();
                    py = SoOTexto(maqPy);
                }
                catch (Exception e) { erro = e.Message; }
                string esperado = apertado ? "7|7" : "3|3";
                Conferir($"{tipo}: BOOTSEL {(apertado ? "apertado" : "solto")} -- BOOTSEL, rp2040.getBootselButton() e rp2.bootsel_button() leem {(apertado ? 1 : 0)}",
                    erro is null && cpp == esperado && py == (apertado ? "1" : "0"), erro ?? $"C++ {cpp}, MicroPython {py}");
            }

            // ---- o LED verde segue o LED_BUILTIN (o GP25; na W, o do radio) ----
            {
                var c = new Circuito();
                c.Pecas.Add(new PecaNoCircuito { Id = "placa", Tipo = tipo, X = 0, Y = 0, Usb = true });
                var ed = new EditorCircuito();
                ed.Carregar(c);
                var maq = new Maquina { Perfil = perfil };
                maq.Reiniciar();
                var b = Simulador.App.Bancada.DeUmaSo(maq);
                var rede = new Netlist(c);
                ed.RefletirEstado(b, rede);
                var apagado = ed.LedsDaPlacaParaTeste("placa");
                maq.Pwm[perfil.LedInterno] = 255;
                ed.RefletirEstado(b, rede);
                var aceso = ed.LedsDaPlacaParaTeste("placa");

                // Com CS_FOTOS apontando uma pasta, a placa com o LED aceso sai em foto.
                if (Environment.GetEnvironmentVariable("CS_FOTOS") is { Length: > 0 } fotos && System.IO.Directory.Exists(fotos))
                    FotografarEditor(ed, System.IO.Path.Combine(fotos, tipo + "-led.png"), 520, 1100);
                Conferir($"{tipo}: um LED soldado, apagado sozinho, aceso com o LED_BUILTIN ({perfil.LedInterno}) em HIGH",
                    apagado.Length == 1 && apagado[0] < 0.05 && aceso[0] > 0.5,
                    string.Join("/", apagado.Select(v => v.ToString("0.00"))) + " -> " + string.Join("/", aceso.Select(v => v.ToString("0.00"))));
            }

            // ---- o ADC_VREF: solto, os 3,3 V; no 3V3, igual; no GND, sem referencia ----
            foreach (var (destino, esperado) in new (string, double?)[] { (null, null), ("3V3", 3.3), ("AGND", 0) })
            {
                var c = new Circuito();
                c.Pecas.Add(new PecaNoCircuito { Id = "placa", Tipo = tipo, X = 0, Y = 0, Usb = true });
                if (destino is not null) Ligar(c, "placa", "ADC_VREF", "placa", destino);
                var ed = new EditorCircuito { EmSimulacao = true };
                ed.Carregar(c);
                var maq = new Maquina { Perfil = perfil };
                maq.Reiniciar();
                var b = Simulador.App.Bancada.DeUmaSo(maq);
                var rede = new Netlist(c);
                ed.AvancarEnergiaParaTeste(b, rede, 0.04);
                ed.AplicarEntradas(b, rede);
                bool ok = esperado is null ? maq.TensaoNoAref is null
                    : maq.TensaoNoAref is { } v && Math.Abs(v - esperado.Value) < 0.15;
                Conferir($"{tipo}: o ADC_VREF {(destino is null ? "solto fica com a referencia da placa" : $"no {destino} vira a referencia ({esperado:0.0} V)")}",
                    ok, $"{maq.TensaoNoAref?.ToString("0.00") ?? "nulo"}");
            }
        }

        // ---- a conta do conversor com a referencia de fora ----
        {
            var maq = new Maquina { Perfil = PerfilPlaca.PiPico };
            maq.Reiniciar();
            maq.Analogico[26] = (int)Math.Round(4095 * 1.5 / 3.3);    // 1,5 V no GP26
            int padrao = maq.AnalogRead(26);
            maq.TensaoNoAref = 3.0;
            int tres = maq.AnalogRead(26);
            maq.TensaoNoAref = 0;
            int semRef = maq.AnalogRead(26);
            maq.TensaoNoAref = null;
            Conferir("pi-pico: 1,5 V no GP26 le ~1861 com os 3,3 V da placa, ~2048 com 3,0 V no ADC_VREF, e satura no GND",
                padrao is > 1850 and < 1870 && tres is > 2035 and < 2060 && semRef == 4095 && maq.AnalogRead(26) == padrao,
                $"{padrao} / {tres} / {semRef}");
        }

        // ---- o AGND e terra; o rp2 e so da Pico ----
        Conferir("o AGND da Pico conta como terra", Netlist.EhPinoDeTerra("AGND"));
        {
            string erro = null;
            try
            {
                var maq = new Maquina { Perfil = PerfilPlaca.Esp32 };
                maq.Reiniciar();
                var interp = new Interpretador(Compilador.Compilar("import rp2\nprint(rp2.bootsel_button())", Linguagem.MicroPython), maq);
                interp.Preparar();
                interp.Setup();
            }
            catch (Exception e) { erro = e.Message; }
            Conferir("rp2.bootsel_button() na ESP32: o erro diz que o modulo e da Pico", erro?.Contains("rp2") == true, erro ?? "rodou");
        }

        // ---- os nomes do Wokwi vao e voltam ----
        foreach (string tipo in PlacasPico)
        {
            var def = Catalogo.Obter(tipo);
            var idaEVolta = def.Pinos.Where(p => FormatoWokwi.PinoDoWokwi(tipo, FormatoWokwi.PinoParaWokwi(tipo, p.Nome)) != p.Nome)
                                     .Select(p => p.Nome).ToList();
            Conferir($"{tipo}: cada pino vai e volta pelo nome do Wokwi", idaEVolta.Count == 0, string.Join(",", idaEVolta));
        }
    }
}
