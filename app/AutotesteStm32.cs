namespace Simulador.App;

/// <summary>
/// As STM32 redesenhadas (Alpha 25, placas-stm32.py): a Blue Pill com a barra
/// de verdade (o PB12 que faltava, as fileiras a 15,24 mm, o conector SWD com
/// o PA13 e o PA14), a Nucleo-L031K6 com as duas barras do Nano na ordem da
/// placa, e a Nucleo-C031C6 inteira -- o Arduino e os dois morpho de 38, cada
/// nome do mesmo pino do chip na mesma trilha. Os LEDs (o PC13 da Blue Pill
/// acende com LOW), o USER da Nucleo-64 no PC13 e o RESET de cada uma.
/// </summary>
public static partial class Autoteste
{
    static readonly string[] PlacasStm32 = ["stm32-bluepill", "st-nucleo-l031k6", "st-nucleo-c031c6"];

    static void Placas_Stm32()
    {
        saida.AppendLine("placas STM32: a barra de verdade e a funcao de cada pino");

        static void Ligar(Circuito c, string a, string pa, string b, string pb)
            => c.Fios.Add(new Fio { DePeca = a, DePino = pa, ParaPeca = b, ParaPino = pb });

        static string Base(string n) => n.Contains('.') && n[(n.LastIndexOf('.') + 1)..].All(char.IsAsciiDigit) ? n[..n.LastIndexOf('.')] : n;

        // ---- cada pino tem uma funcao ----
        foreach (string tipo in PlacasStm32)
        {
            var def = Catalogo.Obter(tipo);
            var perfil = PerfilPlaca.De(tipo);
            var sem = new List<string>();
            int gpios = 0, energia = 0, especiais = 0;
            foreach (var p in def.Pinos)
            {
                if (perfil.Numero(p.Nome) >= 0) gpios++;
                else if (Netlist.EhPinoDeAlimentacao(p.Nome) || Netlist.EhPinoDeTerra(p.Nome)) energia++;
                else if (Base(p.Nome) is "RST" or "AREF" or "VB" or "E5V" or "5V_USB_CHG" or "NC") especiais++;
                else sem.Add(p.Nome);
            }
            int esperados = tipo switch { "stm32-bluepill" => 44, "st-nucleo-l031k6" => 30, _ => 108 };
            Conferir($"{tipo}: todo pino tem funcao ({gpios} GPIO, {energia} de energia, {especiais} RST/VBAT/NC/E5V)",
                sem.Count == 0 && def.Pinos.Count == esperados, $"{def.Pinos.Count} pinos; sem funcao: " + string.Join(", ", sem));
            bool unicos = def.Pinos.Select(p => p.Nome).Distinct().Count() == def.Pinos.Count;
            Conferir($"{tipo}: nenhum nome de pino repetido", unicos);
        }

        // ---- as fileiras: o passo de 2,54 mm, a distancia entre elas e a ordem da placa ----
        foreach (var (tipo, esq, dir, entre) in new (string, string, string, double)[]
                 {
                     ("stm32-bluepill",
                      "VB PC13 PC14 PC15 PA0 PA1 PA2 PA3 PA4 PA5 PA6 PA7 PB0 PB1 PB10 PB11 RST 3V3 GND GND",
                      "3V3 GND 5V PB9 PB8 PB7 PB6 PB5 PB4 PB3 PA15 PA12 PA11 PA10 PA9 PA8 PB15 PB14 PB13 PB12", 15.24),
                     ("st-nucleo-l031k6",
                      "D1 D0 RST GND D2 D3 D4 D5 D6 D7 D8 D9 D10 D11 D12",
                      "VIN GND RST 5V A7 A6 A5 A4 A3 A2 A1 A0 AREF 3V3 D13", 15.24),
                 })
        {
            var def = Catalogo.Obter(tipo);
            var barra = def.Pinos.Where(p => p.Nome is not ("SWDIO" or "SWCLK" or "GND.4" or "3V3.3")).ToList();
            var colunas = barra.GroupBy(p => Math.Round(p.X / 3.78)).OrderBy(g => g.Key).ToList();
            string achadaE = colunas.Count == 2 ? string.Join(" ", colunas[0].OrderBy(p => p.Y).Select(p => Base(p.Nome))) : "?";
            string achadaD = colunas.Count == 2 ? string.Join(" ", colunas[1].OrderBy(p => p.Y).Select(p => Base(p.Nome))) : "?";
            bool passo = colunas.Count == 2 && colunas.All(col =>
            {
                var ys = col.Select(p => p.Y).OrderBy(y => y).ToList();
                return ys.Zip(ys.Skip(1), (a, b) => b - a).All(d => Math.Abs(d - 9.6) < 0.1);
            });
            double mm = colunas.Count == 2 ? (colunas[1].Average(p => p.X) - colunas[0].Average(p => p.X)) / 96 * 25.4 : 0;
            Conferir($"{tipo}: duas fileiras no passo de 2,54 mm, a {mm:0.00} mm uma da outra, na ordem da placa",
                passo && Math.Abs(mm - entre) < 0.1 && achadaE == esq && achadaD == dir, achadaE + " | " + achadaD);
        }

        // ---- a Blue Pill: o conector SWD (DIO = PA13, CLK = PA14) ----
        {
            var perfil = PerfilPlaca.Stm32BluePill;
            var def = Catalogo.Obter("stm32-bluepill");
            var swd = new[] { "GND.4", "SWCLK", "SWDIO", "3V3.3" }.Select(n => def.Pinos.FirstOrDefault(p => p.Nome == n)).ToList();
            bool fila = swd.All(p => p is not null) && swd.Zip(swd.Skip(1), (a, b) => Math.Abs(b.X - a.X - 9.6) < 0.2 && Math.Abs(b.Y - a.Y) < 0.1).All(x => x);
            Conferir("stm32-bluepill: o SWD em fila de 2,54 mm; o DIO e o PA13 e o CLK o PA14",
                fila && perfil.Numero("SWDIO") == 13 && perfil.Numero("SWCLK") == 14, string.Join(" ", swd.Select(p => p?.Nome ?? "?")));
        }

        // ---- a Nucleo-64: o morpho e o Arduino, e os nomes do mesmo pino na mesma trilha ----
        {
            var perfil = PerfilPlaca.De("st-nucleo-c031c6");
            Conferir("st-nucleo-c031c6: a numeracao do STM32duino (D13 = PA5, A0 = PA0 = 29, USER_BTN = PC13 = 21)",
                perfil.Numero("D13") == 13 && perfil.Numero("PA5") == 13 && perfil.Numero("A0") == 29 && perfil.Numero("PA0") == 29
                && perfil.Numero("D14") == 14 && perfil.PinoDoCore("USER_BTN") == 21 && perfil.Numero("PC13") == 21
                && perfil.PinoDoCore("A6") == 26 && perfil.Numero("PD2.2") == 17,
                $"D13={perfil.Numero("D13")} PA5={perfil.Numero("PA5")} A0={perfil.Numero("A0")} USER_BTN={perfil.PinoDoCore("USER_BTN")}");
            var c = new Circuito();
            c.Pecas.Add(new PecaNoCircuito { Id = "placa", Tipo = "st-nucleo-c031c6", X = 0, Y = 0 });
            c.Pecas.Add(new PecaNoCircuito { Id = "a", Tipo = "led", X = 600, Y = 300 });
            c.Pecas.Add(new PecaNoCircuito { Id = "b", Tipo = "led", X = 600, Y = 500 });
            Ligar(c, "placa", "PA5", "a", "A");
            Ligar(c, "placa", "PB0.2", "b", "A");
            var rede = new Netlist(c);
            bool juntos = rede.RaizDe(Netlist.No("placa", "PA5")) == rede.RaizDe(Netlist.No("placa", "D13"))
                          && rede.RaizDe(Netlist.No("placa", "PB0")) == rede.RaizDe(Netlist.No("placa", "D10"))
                          && rede.RaizDe(Netlist.No("placa", "PA0")) == rede.RaizDe(Netlist.No("placa", "A0"));
            bool soltos = rede.RaizDe(Netlist.No("placa", "NC")) != rede.RaizDe(Netlist.No("placa", "NC.2"));
            Conferir("st-nucleo-c031c6: o PA5 do morpho e o D13 do Arduino sao a mesma trilha (e os NC ficam soltos)",
                juntos && soltos && rede.GpioDe(c, "a", "A") == 13 && rede.GpioDe(c, "b", "A") == 10,
                $"{juntos}/{soltos}/{rede.GpioDe(c, "a", "A")}/{rede.GpioDe(c, "b", "A")}");

            var def = Catalogo.Obter("st-nucleo-c031c6");
            double X(string n) => def.Pinos.First(p => p.Nome == n).X / 96 * 25.4;
            double Y(string n) => def.Pinos.First(p => p.Nome == n).Y / 96 * 25.4;
            bool medidas = Math.Abs(X("D0") - X("A0") - 48.26) < 0.1 && Math.Abs(X("NC.10") - X("PD0") - 63.5) < 0.1
                           && Math.Abs(Y("D7") - Y("D8") - 4.06) < 0.1 && Math.Abs(Y("A0") - Y("VIN") - 5.08) < 0.1
                           && Math.Abs(Y("PA12") - Y("PD0") - 45.72) < 0.1;
            Conferir("st-nucleo-c031c6: o Arduino nas medidas do shield (48,26 mm entre as barras, os vaos de 4,06 e 5,08) e o morpho a 63,5 mm",
                medidas, $"{X("D0") - X("A0"):0.00} {Y("D7") - Y("D8"):0.00} {Y("A0") - Y("VIN"):0.00}");
        }

        // ---- projeto antigo: o AREF da Nucleo-64 vira o AVDD ----
        {
            var c = new Circuito();
            c.Pecas.Add(new PecaNoCircuito { Id = "placa", Tipo = "st-nucleo-c031c6", X = 0, Y = 0 });
            c.Pecas.Add(new PecaNoCircuito { Id = "r", Tipo = "resistor", X = 600, Y = 300 });
            Ligar(c, "placa", "AREF", "r", "1");
            var lido = Circuito.DeJson(c.ParaJson());
            Conferir("projeto antigo: o fio no AREF da Nucleo-C031C6 passa para o AVDD",
                lido.Fios.Single().DePino == "AVDD" && !lido.FiosQuebrados().Any(), lido.Fios.Single().DePino);
        }

        // ---- os botoes: o RESET de cada uma, o USER da Nucleo-64 no PC13 ----
        foreach (string tipo in PlacasStm32)
        {
            var def = Catalogo.Obter(tipo);
            var botoes = EditorCircuito.BotoesPorPlaca[tipo];
            Conferir($"{tipo}: o RESET desenhado, dentro da placa",
                botoes.Any(b => b.Nome == "RESET") && botoes.All(b => b.X > 0 && b.X < def.Largura && b.Y > 0 && b.Y < def.Altura),
                string.Join(" ", botoes.Select(b => b.Nome)));
        }
        {
            var c = new Circuito();
            c.Pecas.Add(new PecaNoCircuito { Id = "placa", Tipo = "st-nucleo-c031c6", X = 0, Y = 0, Usb = true });
            var ed = new EditorCircuito { EmSimulacao = true };
            ed.Carregar(c);
            var maq = new Maquina { Perfil = PerfilPlaca.De("st-nucleo-c031c6") };
            maq.Reiniciar();
            var b = Simulador.App.Bancada.DeUmaSo(maq);
            ed.AplicarEntradas(b, new Netlist(c));
            int solto = maq.DigitalRead(21);
            ed.BootPressionado.Add("placa");
            ed.AplicarEntradas(b, new Netlist(c));
            int apertado = maq.DigitalRead(21);
            ed.BootPressionado.Clear();
            ed.AplicarEntradas(b, new Netlist(c));
            Conferir("st-nucleo-c031c6: o USER (B1) le HIGH solto e LOW apertado no PC13 (o USER_BTN)",
                solto == 1 && apertado == 0 && maq.DigitalRead(21) == 1, $"{solto}/{apertado}");
        }

        // ---- os LEDs: o de energia acende sozinho; o do usuario segue o pino ----
        foreach (var (tipo, pino, invertido) in new[] { ("stm32-bluepill", 45, true), ("st-nucleo-l031k6", 13, false),
                                                        ("st-nucleo-c031c6", 13, false) })
        {
            var c = new Circuito();
            c.Pecas.Add(new PecaNoCircuito { Id = "placa", Tipo = tipo, X = 0, Y = 0, Usb = true });
            var ed = new EditorCircuito();
            ed.Carregar(c);
            var maq = new Maquina { Perfil = PerfilPlaca.De(tipo) };
            maq.Reiniciar();
            var b = Simulador.App.Bancada.DeUmaSo(maq);
            var rede = new Netlist(c);
            ed.RefletirEstado(b, rede);
            double Usuario() => ed.LedsDaPlacaParaTeste("placa").Last();
            double entrada = Usuario();
            maq.PinMode(pino, 1);
            maq.DigitalWrite(pino, 1);
            ed.RefletirEstado(b, rede);
            double alto = Usuario();
            maq.DigitalWrite(pino, 0);
            ed.RefletirEstado(b, rede);
            double baixo = Usuario();
            var energia = ed.LedsDaPlacaParaTeste("placa").SkipLast(1).ToList();
            bool certo = invertido ? entrada < 0.05 && alto < 0.05 && baixo > 0.5 : alto > 0.5 && baixo < 0.05;
            Conferir($"{tipo}: o LED de energia acende; o do usuario (GPIO {pino}) acende com {(invertido ? "LOW (e nao com o pino de entrada)" : "HIGH")}",
                certo && energia.Count > 0 && energia.All(v => v > 0.5),
                $"entrada {entrada:0.00}, HIGH {alto:0.00}, LOW {baixo:0.00}; energia {string.Join("/", energia.Select(v => v.ToString("0.00")))}");
        }

        // ---- nenhuma placa junta pinos de chips diferentes: so os nomes do mesmo pino ----
        {
            var juntos = new List<string>();
            foreach (var perfil in PerfilPlaca.Conhecidos)
            {
                if (Catalogo.Obter(perfil.Id) is not { } def) continue;
                foreach (var g in def.Pinos.Select(p => p.Nome).Where(n => perfil.Numero(n) >= 0).GroupBy(perfil.Numero)
                             .Where(g => g.Select(Base).Distinct().Count() > 1))
                    juntos.Add($"{perfil.Id}:{string.Join("=", g.Select(Base).Distinct())}");
            }
            saida.AppendLine("        juntos pelo GPIO: " + string.Join(", ", juntos));
            Conferir("os pinos juntados pelo GPIO sao so os nomes repetidos de verdade (o morpho da Nucleo-64, o SDA/SCL do Arduino)",
                juntos.All(j => j.StartsWith("st-nucleo-c031c6:", StringComparison.Ordinal) || j.Contains("SDA") || j.Contains("SCL")),
                string.Join(", ", juntos.Where(j => !j.StartsWith("st-nucleo-c031c6:", StringComparison.Ordinal))));
        }
    }
}
