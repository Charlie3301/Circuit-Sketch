namespace Simulador.App;

/// <summary>Rodas e helices: o encaixe no eixo e o que elas fazem com o motor.</summary>
public static partial class Autoteste
{
    static void Acessorios_RodasHelices()
    {
        saida.AppendLine("rodas e helices");

        static void Ligar(Circuito c, PecaNoCircuito a, string pa, PecaNoCircuito b, string pb)
            => c.Fios.Add(new Fio { DePeca = a.Id, DePino = pa, ParaPeca = b.Id, ParaPino = pb });

        // ---- a roda no motor amarelo ----
        {
            var c = new Circuito();
            var pilhas = new PecaNoCircuito { Id = "pilhas", Tipo = "bateria", X = 500, Y = 300, Volts = 6, MiliAmperes = 2000 };
            var motor = new PecaNoCircuito { Id = "motor", Tipo = "motor-tt", X = 100, Y = 100 };
            // O centro da roda (33 mm) perto do eixo do motor (12 x 13 mm), uns 20 px fora.
            double mm = 96 / 25.4;
            var roda = new PecaNoCircuito { Id = "roda", Tipo = "roda-tt", X = 100 + 12 * mm - 33 * mm + 14, Y = 100 + 13 * mm - 33 * mm - 12 };
            var errada = new PecaNoCircuito { Id = "errada", Tipo = "helice-1045", X = 0, Y = 600 };
            c.Pecas.AddRange([pilhas, motor, roda, errada]);
            Ligar(c, pilhas, "+", motor, "+");
            Ligar(c, pilhas, "-", motor, "-");
            var ed = new EditorCircuito { PassoFixoParaTeste = 0.04 };
            ed.Carregar(c);
            string antes = ed.MotorDoAcessorioParaTeste("roda");
            ed.SoltarAcessorioParaTeste("roda");
            ed.SoltarAcessorioParaTeste("errada");
            string depois = ed.MotorDoAcessorioParaTeste("roda");
            var b = Simulador.App.Bancada.DeUmaSo(new Maquina { Perfil = PerfilPlaca.Esp32 });
            var rede = new Netlist(c);
            for (int i = 0; i < 75; i++) ed.AplicarEntradas(b, rede);
            ed.RefletirEstado(b, rede);
            double a1 = ed.MovimentoParaTeste("roda").Angulo;
            ed.AplicarEntradas(b, rede);
            ed.RefletirEstado(b, rede);
            double a2 = ed.MovimentoParaTeste("roda").Angulo;
            string rotulo = ed.RotuloDeGiroParaTeste("motor");
            saida.AppendLine($"        roda: antes '{antes}', depois de soltar '{depois}'; rotulo '{rotulo}'; giro {a1:0} -> {a2:0}");
            Conferir("roda: solta perto do eixo do amarelo, encaixa nele; a helice longe nao encaixa em nada",
                antes is null && depois == "motor" && ed.MotorDoAcessorioParaTeste("errada") is null, $"{antes}/{depois}");
            Conferir("roda: gira com o eixo e o rotulo mostra a velocidade na borda (~0,84 m/s a 248 rpm)",
                Math.Abs(a2 - a1) > 1 && rotulo is not null && rotulo.Contains("0,8") && rotulo.Contains("m/s"), rotulo ?? "(nada)");
        }

        // ---- a helice de tres pas no 130 ----
        {
            var c = new Circuito();
            var pilhas = new PecaNoCircuito { Id = "pilhas", Tipo = "bateria", X = 500, Y = 300, Volts = 6, MiliAmperes = 2000 };
            var motor = new PecaNoCircuito { Id = "motor", Tipo = "motor-dc", X = 100, Y = 100 };
            double mm = 96 / 25.4;
            var helice = new PecaNoCircuito { Id = "helice", Tipo = "helice-130", X = 100 + 14 * mm - 38 * mm, Y = 100 + 12.5 * mm - 38 * mm };
            c.Pecas.AddRange([pilhas, motor, helice]);
            Ligar(c, pilhas, "+", motor, "+");
            Ligar(c, pilhas, "-", motor, "-");
            var ed = new EditorCircuito { PassoFixoParaTeste = 0.04 };
            ed.Carregar(c);
            var b = Simulador.App.Bancada.DeUmaSo(new Maquina { Perfil = PerfilPlaca.Esp32 });
            var rede = new Netlist(c);
            for (int i = 0; i < 75; i++) ed.AplicarEntradas(b, rede);
            double rpm = ed.RpmDoMotor("motor");
            double corrente = ed.MotorDcParaTeste("motor").Corrente * 1000;
            double empuxo = ed.EmpuxoParaTeste("motor");
            saida.AppendLine($"        130 com helice a 6 V: {rpm:0} rpm, {corrente:0} mA, {empuxo:0} gf");
            Conferir("helice no 130: carrega o motor (~6.000 rpm e ~250 mA, a carga nominal do datasheet) e empurra ~25 gf",
                rpm is > 5300 and < 6700 && corrente is > 200 and < 300 && empuxo is > 18 and < 32,
                $"{rpm:0} rpm {corrente:0} mA {empuxo:0} gf");
        }

        // ---- a 1045 no A2212, pelo ESC, numa 3S ----
        {
            var c = new Circuito();
            var placa = new PecaNoCircuito { Id = "placa", Tipo = "esp32-devkit-v1", X = 0, Y = 0, Usb = true };
            var esc = new PecaNoCircuito { Id = "esc", Tipo = "esc", X = 300, Y = 0 };
            var lipo = new PecaNoCircuito { Id = "lipo", Tipo = "bateria", X = 600, Y = 300, Volts = 11.1, MiliAmperes = 55000 };
            var motor = new PecaNoCircuito { Id = "motor", Tipo = "motor-brushless", X = 200, Y = 400 };
            double mm = 96 / 25.4;
            var helice = new PecaNoCircuito { Id = "helice", Tipo = "helice-1045", X = 200 + 18 * mm - 128 * mm, Y = 400 + 18 * mm - 17 * mm };
            c.Pecas.AddRange([placa, esc, lipo, motor, helice]);
            Ligar(c, lipo, "+", esc, "BAT+");
            Ligar(c, lipo, "-", esc, "BAT-");
            Ligar(c, placa, "GND.2", esc, "GND");
            Ligar(c, placa, "D13", esc, "SINAL");
            foreach (string f in new[] { "A", "B", "C" }) Ligar(c, esc, f, motor, f);
            var ed = new EditorCircuito { PassoFixoParaTeste = 0.02 };
            ed.Carregar(c);
            var maq = new Maquina { Perfil = PerfilPlaca.Esp32 };
            maq.Reiniciar();
            maq.PinMode(13, 1);
            maq.EscreverServo(13, 0, 1000);
            maq.Avancar(25);
            var b = Simulador.App.Bancada.DeUmaSo(maq);
            var rede = new Netlist(c);
            for (int i = 0; i < 75; i++) ed.AplicarEntradas(b, rede);
            maq.EscreverServo(13, 0, 2000);
            maq.Avancar(25);
            for (int i = 0; i < 150; i++) ed.AplicarEntradas(b, rede);
            double rpm = ed.RpmDoMotor("motor");
            double empuxo = ed.EmpuxoParaTeste("motor");
            double corrente = ed.EnergiaDoQuadro.CorrenteDaBateria("lipo");
            saida.AppendLine($"        A2212 + 1045 numa 3S, acelerador no maximo: {rpm:0} rpm, {empuxo:0} gf, {corrente:0.0} A da bateria");
            Conferir("helice 1045 no A2212: segura o motor em ~9.000 rpm (a vazio seriam 11.000)", rpm is > 8300 and < 9700, $"{rpm:0}");
            Conferir("helice 1045 no A2212: ~800-900 gf de empuxo, como as tabelas", empuxo is > 750 and < 950, $"{empuxo:0}");
            Conferir("helice 1045 no A2212: a bateria entrega ~15 A", corrente is > 12 and < 19, $"{corrente:0.0}");
        }
    }
}
