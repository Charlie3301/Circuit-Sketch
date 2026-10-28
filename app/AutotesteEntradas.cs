namespace Simulador.App;

/// <summary>
/// As entradas redesenhadas na Alpha 25 (pecas-entradas.py): cada uma no
/// tamanho real e com os pinos no passo da peca de verdade, a capa de cada
/// botao, as chaves do DIP, as teclas e os furos do disco onde o desenho os
/// mostra, os cursos das partes que se mexem, e as cores da capa do botao (o
/// "color" do Wokwi) e do corpo do DIP.
/// </summary>
public static partial class Autoteste
{
    static void Entradas_Redesenhadas()
    {
        saida.AppendLine("entradas: os desenhos novos, os pinos e o que se aperta");
        const double mm = 96 / 25.4;
        static bool Perto(double a, double b, double folga = 0.06) => Math.Abs(a - b) <= folga;
        static (double X, double Y) P(PecaDef d, string n) { var p = d.Pinos.First(x => x.Nome == n); return (p.X / mm, p.Y / mm); }

        // ---- todos os desenhos, com o corpo e o movel de quem se mexe ----
        string[] tipos = ["pushbutton", "pushbutton-6mm", "potentiometer", "slide-potentiometer", "slide-switch", "dip-switch-8",
                          "membrane-keypad", "analog-joystick", "ky-040", "rotary-dialer", "tilt-switch", "micro-switch"];
        string[] moveis = ["potentiometer", "slide-potentiometer", "slide-switch", "analog-joystick", "ky-040", "rotary-dialer", "micro-switch"];
        var faltando = tipos.Where(t => !Pecas.Existe(t) || Catalogo.Obter(t) is null)
            .Concat(moveis.SelectMany(t => new[] { $"{t}-corpo", $"{t}-movel" }).Where(n => !Pecas.Existe(n)))
            .Concat(EditorCircuito.OpcoesDeCorDoBotao.SelectMany(c => new[] { "solto", "pressionado" }
                .Select(e => c == "vermelho" ? $"pushbutton-{e}" : $"pushbutton-{c}-{e}")).Where(n => !Pecas.Existe(n)))
            .Concat(new[] { "pushbutton-6mm-solto", "pushbutton-6mm-pressionado", "dip-switch-8-azul", "dip-switch-8-preto" }.Where(n => !Pecas.Existe(n)))
            .ToList();
        Conferir("entradas: os 12 desenhos, o corpo e o movel de cada parte que se mexe, as 7 capas do botao (solto e apertado) e os 3 DIPs",
            faltando.Count == 0, string.Join(", ", faltando));

        // ---- os pinos no passo da peca de verdade ----
        {
            var bt = Catalogo.Obter("pushbutton");
            Conferir("pushbutton: a chave de 12 mm, pernas a 12,7 x 5,08 mm (1.l-1.r e 2.l-2.r de lado a lado)",
                Perto(P(bt, "1.r").X - P(bt, "1.l").X, 12.7) && Perto(P(bt, "2.l").Y - P(bt, "1.l").Y, 5.08) && Perto(P(bt, "1.l").Y, P(bt, "1.r").Y),
                $"{P(bt, "1.r").X - P(bt, "1.l").X:0.00} x {P(bt, "2.l").Y - P(bt, "1.l").Y:0.00}");
            var b6 = Catalogo.Obter("pushbutton-6mm");
            Conferir("pushbutton-6mm: pernas a 7,62 x 5,08 mm, e a caixa da chave de 6 mm",
                Perto(P(b6, "1.r").X - P(b6, "1.l").X, 7.62) && Perto(P(b6, "2.r").Y - P(b6, "1.r").Y, 5.08) && b6.Largura / mm < 9.5,
                $"{P(b6, "1.r").X - P(b6, "1.l").X:0.00} x {P(b6, "2.r").Y - P(b6, "1.r").Y:0.00}");
            var pot = Catalogo.Obter("potentiometer");
            Conferir("potentiometer: as tres pernas do de 16 mm no passo de 5 mm (GND, SIG, VCC)",
                Perto(P(pot, "SIG").X - P(pot, "GND").X, 5.08) && Perto(P(pot, "VCC").X - P(pot, "SIG").X, 5.08) && Perto(pot.Largura / mm, 17, 0.2));
            var sp = Catalogo.Obter("slide-potentiometer");
            Conferir("slide-potentiometer: o PTA3043 -- 43,5 mm do pino 1 ao 3, 3,5 mm do 1 ao 2, corpo de 45 mm",
                Perto(P(sp, "VCC").X - P(sp, "GND").X, 43.5) && Perto(P(sp, "SIG").Y - P(sp, "GND").Y, 3.5) && Perto(P(sp, "SIG").X, P(sp, "GND").X),
                $"{P(sp, "VCC").X - P(sp, "GND").X:0.00} / {P(sp, "SIG").Y - P(sp, "GND").Y:0.00}");
            var dip = Catalogo.Obter("dip-switch-8");
            Conferir("dip-switch-8: oito chaves no passo de 2,54 mm, as fileiras a 7,62 mm (0,3\")",
                Enumerable.Range(1, 7).All(k => Perto(P(dip, $"{k + 1}a").X - P(dip, $"{k}a").X, 2.54))
                && Perto(P(dip, "1a").Y - P(dip, "1b").Y, 7.62) && Perto(P(dip, "1a").X, P(dip, "1b").X));
            var ms = Catalogo.Obter("micro-switch");
            Conferir("micro-switch: a KW11-3Z -- C, NO e NC a 8,8 e 7,3 mm um do outro",
                Perto(P(ms, "NO").X - P(ms, "COM").X, 8.8) && Perto(P(ms, "NC").X - P(ms, "NO").X, 7.3),
                $"{P(ms, "NO").X - P(ms, "COM").X:0.00} / {P(ms, "NC").X - P(ms, "NO").X:0.00}");
            bool barras = new[] { ("slide-switch", new[] { "1", "2", "3" }), ("membrane-keypad", new[] { "R1", "R2", "R3", "R4", "C1", "C2", "C3", "C4" }),
                                  ("analog-joystick", new[] { "GND", "VCC", "HORZ", "VERT", "SEL" }), ("ky-040", new[] { "CLK", "DT", "SW", "VCC", "GND" }),
                                  ("tilt-switch", new[] { "GND", "VCC", "OUT" }) }
                .All(b => b.Item2.Zip(b.Item2.Skip(1)).All(par =>
                {
                    var d = Catalogo.Obter(b.Item1);
                    var (x1, y1) = P(d, par.First); var (x2, y2) = P(d, par.Second);
                    return Perto(Math.Abs(x2 - x1) + Math.Abs(y2 - y1), 2.54);
                }));
            Conferir("entradas: as barras (SS12D00, teclado, KY-023 na ordem GND +5V VRx VRy SW, KY-040, KY-020) no passo de 2,54 mm", barras);
            var tamanhos = new (string Tipo, double L, double A)[] { ("membrane-keypad", 69.6, 91.0), ("analog-joystick", 26.45, 40.5), ("ky-040", 32.5, 19.05), ("tilt-switch", 24.6, 15.35) };
            var errados = tamanhos.Where(t => !Perto(Catalogo.Obter(t.Tipo).Largura / mm, t.L, 0.2) || !Perto(Catalogo.Obter(t.Tipo).Altura / mm, t.A, 0.2)).Select(t => t.Tipo).ToList();
            Conferir("entradas: o teclado de 69 x 76 mm com o rabicho, o KY-023 de 26 x 34, o KY-040 de 26 x 18,6 e o KY-020 de 18,5 x 15 (com a barra a 90 graus)",
                errados.Count == 0, string.Join(", ", errados));
        }

        // ---- o que se aperta fica dentro da peca, e o clique cai nele ----
        {
            var fora = EditorCircuito.CapasDeBotao.Where(k => tipos.Contains(k.Key))
                .Where(k => Catalogo.Obter(k.Key) is { } d && (k.Value.X - k.Value.R < -0.5 || k.Value.Y - k.Value.R < -0.5
                                                                || k.Value.X + k.Value.R > d.Largura + 0.5 || k.Value.Y + k.Value.R > d.Altura + 0.5))
                .Select(k => k.Key).ToList();
            Conferir("entradas: a capa de cada botao (pushbuttons, chave deslizante, manche, eixo do encoder, alavanca do fim de curso) cabe na peca",
                fora.Count == 0, string.Join(", ", fora));

            // a capa do botao nao cobre nenhuma perna: quem clica na perna quer um fio
            var cobre = new[] { "pushbutton", "pushbutton-6mm" }.Where(t =>
            {
                var d = Catalogo.Obter(t); var c = EditorCircuito.CapasDeBotao[t];
                return d.Pinos.Any(p => Math.Sqrt((p.X - c.X) * (p.X - c.X) + (p.Y - c.Y) * (p.Y - c.Y)) < c.R + 2);
            }).ToList();
            Conferir("entradas: a capa dos pushbuttons nao cobre as pernas", cobre.Count == 0, string.Join(", ", cobre));
        }

        // ---- a cor da capa do botao: o desenho solto e o apertado ----
        {
            var c = new Circuito();
            var bt = new PecaNoCircuito { Id = "bt", Tipo = "pushbutton", X = 100, Y = 100, Modelo = "verde" };
            c.Pecas.Add(bt);
            var ed = new EditorCircuito();
            ed.Carregar(c);
            var img = ed.DesenhoDaPecaParaTeste("bt");
            bool solto = img?.Source == Pecas.Obter("pushbutton-verde-solto");
            var capa = EditorCircuito.CapasDeBotao["pushbutton"];
            ed.Apertar(new System.Windows.Point(bt.X + capa.X, bt.Y + capa.Y));
            bool apertado = ed.DesenhoDaPecaParaTeste("bt")?.Source == Pecas.Obter("pushbutton-verde-pressionado");
            ed.Soltar();
            bool voltou = ed.DesenhoDaPecaParaTeste("bt")?.Source == Pecas.Obter("pushbutton-verde-solto");
            Conferir("pushbutton: a capa verde aparece solta, apertada no clique e solta de novo", solto && apertado && voltou,
                $"{solto} / {apertado} / {voltou}");

            var c6 = new Circuito();
            var b6 = new PecaNoCircuito { Id = "b6", Tipo = "pushbutton-6mm", X = 100, Y = 100 };
            c6.Pecas.Add(b6);
            var ed6 = new EditorCircuito();
            ed6.Carregar(c6);
            var capa6 = EditorCircuito.CapasDeBotao["pushbutton-6mm"];
            ed6.Apertar(new System.Windows.Point(b6.X + capa6.X, b6.Y + capa6.Y));
            Conferir("pushbutton-6mm: o de 6 mm tambem tem o desenho apertado",
                ed6.BotoesPressionados.Contains("b6") && ed6.DesenhoDaPecaParaTeste("b6")?.Source == Pecas.Obter("pushbutton-6mm-pressionado"));
            ed6.Soltar();

            var cd = new Circuito();
            cd.Pecas.Add(new PecaNoCircuito { Id = "dip", Tipo = "dip-switch-8", X = 0, Y = 0, Modelo = "azul" });
            var edd = new EditorCircuito();
            edd.Carregar(cd);
            Conferir("dip-switch-8: o modelo azul desenha o corpo azul", edd.DesenhoDaPecaParaTeste("dip")?.Source == Pecas.Obter("dip-switch-8-azul"));
        }

        // ---- o "color" do Wokwi vira a cor da capa, e volta ----
        {
            const string diagrama = """
            {
              "version": 1,
              "parts": [
                { "type": "wokwi-pushbutton", "id": "btn1", "top": 0, "left": 0, "attrs": { "color": "green" } },
                { "type": "wokwi-pushbutton", "id": "btn2", "top": 0, "left": 100, "attrs": { "color": "yellow" } },
                { "type": "wokwi-pushbutton", "id": "btn3", "top": 0, "left": 200, "attrs": {} }
              ],
              "connections": []
            }
            """;
            var ci = ImportarWokwi.Ler(diagrama).Circuito;
            string Modelo(string id) => ci.Pecas.FirstOrDefault(x => x.Id == id)?.Modelo ?? "(nada)";
            Conferir("wokwi: o pushbutton verde e o amarelo entram com a capa da cor; sem color, fica o vermelho de fabrica",
                Modelo("btn1") == "verde" && Modelo("btn2") == "amarelo" && Modelo("btn3") == "(nada)",
                $"{Modelo("btn1")} / {Modelo("btn2")} / {Modelo("btn3")}");
            var nosso = new Circuito();
            nosso.Pecas.Add(new PecaNoCircuito { Id = "azul", Tipo = "pushbutton", Modelo = "azul" });
            string json = ExportarWokwi.Gerar(nosso);
            Conferir("wokwi: exportar o botao de capa azul escreve o color blue", json.Contains("\"color\": \"blue\""), json[..Math.Min(300, json.Length)]);
        }

        // ---- as chaves do DIP, as teclas e os furos do disco ----
        {
            var c = new Circuito();
            var dip = new PecaNoCircuito { Id = "dip", Tipo = "dip-switch-8", X = 0, Y = 0 };
            c.Pecas.Add(dip);
            var ed = new EditorCircuito();
            ed.Carregar(c);
            var def = Catalogo.Obter("dip-switch-8");
            // o meio de cada rasgo, um por um: liga as oito
            for (int k = 0; k < 8; k++)
                ed.Apertar(Geometria.PosicaoLocal(dip, def, DesenhoDasEntradas.DipPrimeira + k * DesenhoDasEntradas.DipPasso + DesenhoDasEntradas.DipLargura / 2,
                                                  DesenhoDasEntradas.DipTopo + DesenhoDasEntradas.DipAltura / 2));
            double topo = ed.ChaveDipParaTeste("dip", 0);
            Conferir("dip-switch-8: o clique no meio de cada rasgo vira a chave dele (as oito ligadas), e ela sobe para o ON",
                dip.Bits == 0xFF && Perto(topo, DesenhoDasEntradas.DipTopo + DesenhoDasEntradas.DipFolga, 0.01), $"bits {dip.Bits:X2}, topo {topo:0.0}");

            var ct = new Circuito();
            var teclado = new PecaNoCircuito { Id = "kp", Tipo = "membrane-keypad", X = 0, Y = 0 };
            ct.Pecas.Add(teclado);
            var edt = new EditorCircuito();
            edt.Carregar(ct);
            var deft = Catalogo.Obter("membrane-keypad");
            var maq = new Maquina { Perfil = PerfilPlaca.Esp32 };
            var lidas = new List<char>();
            foreach (var (x, y, tecla) in EditorCircuito.TeclasMatriciais)
            {
                edt.Apertar(Geometria.PosicaoLocal(teclado, deft, x, y));
                edt.AplicarEntradas(maq, new Netlist(ct));
                lidas.Add((char)maq.Leituras.GetValueOrDefault("keypad:tecla"));
                edt.Soltar();
            }
            Conferir("membrane-keypad: o clique no meio de cada tecla da a tecla (1 2 3 A / 4 5 6 B / 7 8 9 C / * 0 # D)",
                new string([.. lidas]) == "123A456B789C*0#D", new string([.. lidas]));

            // os furos do disco: 30 graus um do outro em volta do eixo, como o GrausPorFuro da discagem
            var dd = Catalogo.Obter("rotary-dialer");
            double ex = dd.Largura * DesenhoDasEntradas.DiscoEixoX, ey = dd.Altura * DesenhoDasEntradas.DiscoEixoY;
            var angulos = EditorCircuito.FurosDoDisco.Select(f => Math.Atan2(f.Y - ey, f.X - ex) * 180 / Math.PI).ToArray();
            bool passo = angulos.Zip(angulos.Skip(1)).All(par => Perto(((par.First - par.Second) % 360 + 360) % 360, 30, 0.1));
            var cdd = new Circuito();
            var disco = new PecaNoCircuito { Id = "disco", Tipo = "rotary-dialer", X = 0, Y = 0 };
            cdd.Pecas.Add(disco);
            var edd = new EditorCircuito();
            edd.Carregar(cdd);
            var (x7, y7, _) = EditorCircuito.FurosDoDisco.First(f => f.Numero == 7);
            edd.Apertar(Geometria.PosicaoLocal(disco, dd, x7, y7));
            Conferir("rotary-dialer: os dez furos a 30 graus um do outro (o 1 junto do batente), e o clique no 7 disca o 7",
                passo && edd.NumeroDiscado("disco") == 7, $"{string.Join(" ", angulos.Select(a => a.ToString("0")))} -> {edd.NumeroDiscado("disco")}");
        }

        // ---- os cursos das partes que se mexem ----
        {
            (EditorCircuito ed, PecaNoCircuito p) Montar(string tipo)
            {
                var c = new Circuito();
                var p = new PecaNoCircuito { Id = "p", Tipo = tipo, X = 0, Y = 0, Giro = tipo == "analog-joystick" ? 0.5 : 0 };
                c.Pecas.Add(p);
                var ed = new EditorCircuito();
                ed.Carregar(c);
                return (ed, p);
            }
            var (eds, ps) = Montar("slide-potentiometer");
            eds.DefinirGiro(ps, 1);
            double curso = eds.MovimentoParaTeste("p").X;
            Conferir("slide-potentiometer: o knob anda os 30 mm do curso do PTA3043", Perto(curso / mm, 30, 0.05), $"{curso / mm:0.00} mm");

            var (edj, pj) = Montar("analog-joystick");
            edj.DefinirGiro(pj, 1);
            edj.DefinirGiro2(pj, 0);
            var m = edj.MovimentoParaTeste("p");
            Conferir("analog-joystick: o manche vai 3 mm para cada lado (no canto: direita e para baixo)",
                Perto(m.X / mm, 3, 0.05) && Perto(m.Y / mm, 3, 0.05), $"({m.X / mm:0.00}; {m.Y / mm:0.00}) mm");

            var cm = new Circuito();
            cm.Pecas.Add(new PecaNoCircuito { Id = "p", Tipo = "micro-switch", X = 0, Y = 0 });
            var edm = new EditorCircuito();
            edm.Carregar(cm);
            var maq = new Maquina { Perfil = PerfilPlaca.Esp32 };
            maq.Reiniciar();
            var capa = EditorCircuito.CapasDeBotao["micro-switch"];
            edm.Apertar(new System.Windows.Point(capa.X, capa.Y));
            edm.RefletirEstado(Simulador.App.Bancada.DeUmaSo(maq), new Netlist(cm));
            double giro = edm.MovimentoParaTeste("p").Angulo;
            edm.Soltar();
            Conferir("micro-switch: apertada, a alavanca desce o bastante para o pino do botao entrar (entre 6 e 14 graus)",
                giro > 6 && giro < 14 && Perto(giro, DesenhoDasEntradas.FimDeCursoGiro, 0.01), $"{giro:0.0} graus");
        }

        // ---- a demo do botao na protoboard: a chave de 12 mm a cavalo no canal ----
        {
            string tmp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"cs-uno-botao-{Environment.ProcessId}.sktch");
            System.IO.File.WriteAllText(tmp, Demos.Texto("uno-botao"));
            var (c, _) = Sktch.Abrir(tmp);
            System.IO.File.Delete(tmp);
            var rede = new Netlist(c);
            string Raiz(string peca, string pino) => rede.RaizDe(Netlist.No(peca, pino));
            bool um = Raiz("bt", "1.l") is { } r1 && (r1 == Raiz("placa", "2") || r1 == Raiz("placa", "GND.1"));
            bool dois = Raiz("bt", "2.l") is { } r2 && (r2 == Raiz("placa", "2") || r2 == Raiz("placa", "GND.1"));
            Conferir("uno-botao: o botao de 12 mm a cavalo no canal liga um lado ao pino 2 e o outro ao GND (e solto nao junta os dois)",
                um && dois && Raiz("placa", "2") != Raiz("placa", "GND.1"), $"{Raiz("bt", "1.l")} / {Raiz("bt", "2.l")}");
        }

        // Com CS_FOTOS apontando uma pasta, as doze saem em foto, mexidas: os botoes
        // apertados, a tecla 5, o DIP azul com 1, 3, 6 e 8 ligadas, o manche no canto...
        if (Environment.GetEnvironmentVariable("CS_FOTOS") is { Length: > 0 } fotos && System.IO.Directory.Exists(fotos))
        {
            var cf = new Circuito();
            void Por(string id, string tipo, double x, double y, string modelo = null, double giro = 0, double giro2 = 0.5, int bits = 0)
                => cf.Pecas.Add(new PecaNoCircuito { Id = id, Tipo = tipo, X = x, Y = y, Modelo = modelo, Giro = giro, Giro2 = giro2, Bits = bits });
            Por("kp", "membrane-keypad", 20, 20);
            Por("disco", "rotary-dialer", 310, 20);
            Por("joy", "analog-joystick", 600, 20, giro: 0.9, giro2: 0.85);
            Por("enc", "ky-040", 720, 20, giro: 0.3);
            Por("tilt", "tilt-switch", 720, 130, giro: 1);
            Por("fim", "micro-switch", 880, 20);
            Por("chave", "slide-switch", 990, 30, giro: 1);
            Por("desl", "slide-potentiometer", 20, 400, giro: 0.65);
            Por("dip", "dip-switch-8", 230, 410, "azul", bits: 0xA5);
            Por("bt1", "pushbutton", 340, 405, "verde");
            Por("bt2", "pushbutton", 410, 405, "amarelo");
            Por("bt3", "pushbutton", 480, 405);
            Por("bt6", "pushbutton-6mm", 550, 415);
            Por("pot", "potentiometer", 610, 385, giro: 0.8);
            var edf = new EditorCircuito { EmSimulacao = true };
            edf.Carregar(cf);
            foreach (string id in new[] { "bt1", "bt6", "fim" })
            {
                var p = cf.Pecas.First(x => x.Id == id);
                var capa = EditorCircuito.CapasDeBotao[p.Tipo];
                edf.Apertar(new System.Windows.Point(p.X + capa.X, p.Y + capa.Y));
            }
            var kp = cf.Pecas.First(x => x.Id == "kp");
            var (x5, y5, _) = EditorCircuito.TeclasMatriciais.First(t => t.Tecla == "5");
            edf.Apertar(Geometria.PosicaoLocal(kp, Catalogo.Obter(kp.Tipo), x5, y5));
            var mf = new Maquina { Perfil = PerfilPlaca.Esp32 };
            mf.Reiniciar();
            edf.RefletirEstado(Simulador.App.Bancada.DeUmaSo(mf), new Netlist(cf));
            FotografarEditor(edf, System.IO.Path.Combine(fotos, "entradas.png"), 1500, 900);
            edf.Soltar();
        }
    }
}
