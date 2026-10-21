namespace Simulador.App;

/// <summary>
/// As saidas novas no editor (Alpha 25): o TM1637 acende os segmentos que a
/// biblioteca escreveu, a matriz do MAX7219 os pontos (e a segunda da cadeia,
/// a do DOUT, mostra o aparelho 1), o semaforo cada LED pela corrente -- e
/// sem o VCC nada acende.
/// </summary>
public static partial class Autoteste
{
    static void Mostradores_NoEditor()
    {
        saida.AppendLine("saidas novas: TM1637, matriz MAX7219 e semaforo no editor");

        static Maquina Placa()
        {
            var maq = new Maquina { Perfil = PerfilPlaca.Esp32 };
            maq.Reiniciar();
            return maq;
        }
        static void Ligar(Circuito c, string a, string pa, string b, string pb) => c.Fios.Add(new Fio { DePeca = a, DePino = pa, ParaPeca = b, ParaPino = pb });
        static int Acesos(double[] luzes) => luzes.Count(v => v > 0.3);

        // ---- o TM1637 mostrando 12:34 ----
        {
            var c = new Circuito();
            c.Pecas.Add(new PecaNoCircuito { Id = "placa", Tipo = "esp32-devkit-v1", X = 0, Y = 0, Usb = true });
            c.Pecas.Add(new PecaNoCircuito { Id = "tm", Tipo = "tm1637", X = 400, Y = 0 });
            Ligar(c, "tm", "CLK", "placa", "D4"); Ligar(c, "tm", "DIO", "placa", "D5"); Ligar(c, "tm", "GND", "placa", "GND.1");
            var ed = new EditorCircuito { EmSimulacao = true };
            ed.Carregar(c);
            var maq = Placa();
            var tm = new Tm1637();
            maq.Mostradores[(4, 5)] = tm;
            byte[] doze = [0x06, 0x5B | 0x80, 0x4F, 0x66];
            for (int k = 0; k < 4; k++) tm.Definir(k, doze[k]);
            var b = Simulador.App.Bancada.DeUmaSo(maq);
            ed.RefletirEstado(b, new Netlist(c));
            int semVcc = Acesos(ed.LuzesDoLedParaTeste("tm"));
            Ligar(c, "tm", "VCC", "placa", "3V3");
            ed.Carregar(c);
            ed.RefletirEstado(b, new Netlist(c));
            int comVcc = Acesos(ed.LuzesDoLedParaTeste("tm"));
            Conferir("TM1637: o 12:34 acende 17 segmentos (2 + 5 + os dois pontos + 5 + 4); sem o VCC, nenhum",
                semVcc == 0 && comVcc == 17, $"{semVcc} / {comVcc}");
            tm.Luz(7, false);
            ed.RefletirEstado(b, new Netlist(c));
            Conferir("TM1637: o setBrightness(7, false) apaga o display", Acesos(ed.LuzesDoLedParaTeste("tm")) == 0);
        }

        // ---- duas matrizes em cadeia ----
        {
            var c = new Circuito();
            c.Pecas.Add(new PecaNoCircuito { Id = "placa", Tipo = "esp32-devkit-v1", X = 0, Y = 0, Usb = true });
            c.Pecas.Add(new PecaNoCircuito { Id = "m0", Tipo = "max7219-matriz", X = 400, Y = 0 });
            c.Pecas.Add(new PecaNoCircuito { Id = "m1", Tipo = "max7219-matriz", X = 600, Y = 0 });
            Ligar(c, "m0", "DIN", "placa", "D23"); Ligar(c, "m0", "CLK", "placa", "D18"); Ligar(c, "m0", "CS", "placa", "D5");
            Ligar(c, "m0", "VCC", "placa", "VIN"); Ligar(c, "m0", "GND", "placa", "GND.1");
            Ligar(c, "m1", "DIN", "m0", "DOUT"); Ligar(c, "m1", "CLK", "m0", "CLK.2"); Ligar(c, "m1", "CS", "m0", "CS.2");
            Ligar(c, "m1", "VCC", "m0", "VCC.2"); Ligar(c, "m1", "GND", "m0", "GND.2");
            var ed = new EditorCircuito { EmSimulacao = true };
            ed.Carregar(c);
            var maq = Placa();
            var cadeia = new Max7219(2);
            maq.Matrizes[(23, 18, 5)] = cadeia;
            cadeia.Desligado[0] = false; cadeia.Linhas[0][0] = 0xFF;
            cadeia.Desligado[1] = false; cadeia.Linhas[1][7] = 0x01;
            cadeia.Mudou();
            ed.RefletirEstado(Simulador.App.Bancada.DeUmaSo(maq), new Netlist(c));
            var l0 = ed.LuzesDoLedParaTeste("m0");
            var l1 = ed.LuzesDoLedParaTeste("m1");
            Conferir("MAX7219: a primeira acende a linha de cima (o setRow(0, 0, 0xFF)); a do DOUT e o aparelho 1 (o canto de baixo a direita)",
                Acesos(l0) == 8 && l0.Take(8).All(v => v > 0.3) && Acesos(l1) == 1 && l1[63] > 0.3, $"{Acesos(l0)} / {Acesos(l1)}");
            cadeia.Desligado[1] = true;
            cadeia.Mudou();
            ed.RefletirEstado(Simulador.App.Bancada.DeUmaSo(maq), new Netlist(c));
            Conferir("MAX7219: o shutdown(1, true) apaga so a segunda", Acesos(ed.LuzesDoLedParaTeste("m1")) == 0 && Acesos(ed.LuzesDoLedParaTeste("m0")) == 8);
        }

        // ---- o semaforo: R e G em HIGH, Y em LOW ----
        {
            var c = new Circuito();
            c.Pecas.Add(new PecaNoCircuito { Id = "placa", Tipo = "esp32-devkit-v1", X = 0, Y = 0, Usb = true });
            c.Pecas.Add(new PecaNoCircuito { Id = "sem", Tipo = "modulo-semaforo", X = 400, Y = 0 });
            Ligar(c, "sem", "R", "placa", "D2"); Ligar(c, "sem", "Y", "placa", "D4"); Ligar(c, "sem", "G", "placa", "D5");
            Ligar(c, "sem", "GND", "placa", "GND.1");
            var ed = new EditorCircuito { EmSimulacao = true };
            ed.Carregar(c);
            var maq = Placa();
            foreach (var (g, v) in new[] { (2, 1), (4, 0), (5, 1) }) { maq.Modo[g] = 1; maq.Digital[g] = v; maq.Pwm[g] = v * 255; }
            var b = Simulador.App.Bancada.DeUmaSo(maq);
            var rede = new Netlist(c);
            ed.AvancarEnergiaParaTeste(b, rede, 0.04);
            ed.RefletirEstado(b, rede);
            var luzes = ed.LuzesDoLedParaTeste("sem");
            double ir = ed.EnergiaDoQuadro.CorrenteDoRamo("sem", "R") * 1000;
            Conferir("semaforo: o vermelho e o verde (HIGH) acendem, o amarelo (LOW) nao; o vermelho puxa ~4 mA pelo resistor de 330 ohms",
                luzes.Length == 3 && luzes[0] > 0.3 && luzes[1] == 0 && luzes[2] > 0.3 && ir is > 3 and < 5,
                $"{string.Join("/", luzes.Select(v => v.ToString("0.00")))}, R {ir:0.0} mA");

            if (Environment.GetEnvironmentVariable("CS_FOTOS") is { Length: > 0 } fotos && System.IO.Directory.Exists(fotos))
            {
                var cf = new Circuito();
                cf.Pecas.Add(new PecaNoCircuito { Id = "placa", Tipo = "esp32-devkit-v1", X = 30, Y = 230, Usb = true });
                cf.Pecas.Add(new PecaNoCircuito { Id = "sem", Tipo = "modulo-semaforo", X = 30, Y = 10 });
                cf.Pecas.Add(new PecaNoCircuito { Id = "tm", Tipo = "tm1637", Modelo = "vermelho", X = 120, Y = 10 });
                cf.Pecas.Add(new PecaNoCircuito { Id = "m0", Tipo = "max7219-matriz", X = 320, Y = 10 });
                foreach (var (p, pp, q) in new[] { ("R", "D2", "sem"), ("Y", "D4", "sem"), ("G", "D5", "sem"), ("GND", "GND.1", "sem"),
                                                   ("CLK", "D13", "tm"), ("DIO", "D12", "tm"), ("VCC", "3V3", "tm"), ("GND", "GND.1", "tm"),
                                                   ("DIN", "D23", "m0"), ("CLK", "D18", "m0"), ("CS", "D15", "m0"), ("VCC", "VIN", "m0"), ("GND", "GND.2", "m0") })
                    Ligar(cf, q, p, "placa", pp);
                var edf = new EditorCircuito { EmSimulacao = true };
                edf.Carregar(cf);
                var mf = Placa();
                foreach (var (g, v) in new[] { (2, 1), (4, 0), (5, 1) }) { mf.Modo[g] = 1; mf.Digital[g] = v; mf.Pwm[g] = v * 255; }
                var tmf = new Tm1637();
                byte[] doze = [0x06, 0x5B | 0x80, 0x4F, 0x66];
                for (int k = 0; k < 4; k++) tmf.Definir(k, doze[k]);
                mf.Mostradores[(13, 12)] = tmf;
                var mx = new Max7219(1);
                mx.Desligado[0] = false;
                byte[] coracao = [0x00, 0x66, 0xFF, 0xFF, 0xFF, 0x7E, 0x3C, 0x18];
                for (int r = 0; r < 8; r++) mx.Linhas[0][r] = coracao[r];
                mf.Matrizes[(23, 18, 15)] = mx;
                var bf = Simulador.App.Bancada.DeUmaSo(mf);
                var rf = new Netlist(cf);
                edf.AvancarEnergiaParaTeste(bf, rf, 0.04);
                edf.RefletirEstado(bf, rf);
                FotografarEditor(edf, System.IO.Path.Combine(fotos, "mostradores.png"), 1500, 900);
            }
        }
    }
}
