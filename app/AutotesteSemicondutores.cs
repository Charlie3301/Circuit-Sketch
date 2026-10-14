namespace Simulador.App;

/// <summary>
/// Transistor, MOSFET, optoacoplador, fusivel e varistor (Alpha 24): o NPN
/// satura com o resistor de base comum e fica na regiao ativa com pouca base;
/// o IRLZ44N liga com 3,3 V e o IRF520 nao; o opto leva o LED para o outro
/// lado; o fusivel abre; o varistor segura a tensao.
/// </summary>
public static partial class Autoteste
{
    static void Semicondutores_NoCircuito()
    {
        saida.AppendLine("transistor, MOSFET, opto, fusivel e varistor");

        // Uma ESP32 (na USB) com o D13 em HIGH ou LOW, e uma bateria para a carga.
        static Maquina Pino13(bool alto)
        {
            var maq = new Maquina { Perfil = PerfilPlaca.Esp32 };
            maq.Reiniciar();
            maq.Modo[13] = 1;
            maq.Digital[13] = alto ? 1 : 0;
            maq.Pwm[13] = alto ? 255 : 0;   // o digitalWrite(HIGH) poe o PWM em 255
            return maq;
        }
        static Circuito Base(double volts, out PecaNoCircuito placa)
        {
            var c = new Circuito();
            placa = new PecaNoCircuito { Id = "placa", Tipo = "esp32-devkit-v1", X = 0, Y = 0, Usb = true };
            c.Pecas.AddRange([placa, new PecaNoCircuito { Id = "bat", Tipo = "bateria", X = 0, Y = 300, Volts = volts, MiliAmperes = 20000, MiliAmperesHora = 1000 }]);
            c.Fios.Add(new Fio { DePeca = "bat", DePino = "-", ParaPeca = "placa", ParaPino = "GND.2" });
            return c;
        }
        static void Ligar(Circuito c, string a, string pa, string b, string pb) => c.Fios.Add(new Fio { DePeca = a, DePino = pa, ParaPeca = b, ParaPino = pb });
        static PecaNoCircuito R(string id, double ohms) => new() { Id = id, Tipo = "resistor", X = 200, Y = 0, Ohms = ohms };

        // ---- NPN: 9 V, carga de 100 ohms no coletor, base pelo resistor no D13 ----
        static (double Ic, double Vce) Npn(double ohmsDaBase, bool alto)
        {
            var c = Base(9, out _);
            c.Pecas.AddRange([new PecaNoCircuito { Id = "q", Tipo = "transistor-npn", X = 300, Y = 0 }, R("rb", ohmsDaBase), R("carga", 100)]);
            Ligar(c, "placa", "D13", "rb", "1"); Ligar(c, "rb", "2", "q", "B");
            Ligar(c, "bat", "+", "carga", "1"); Ligar(c, "carga", "2", "q", "C"); Ligar(c, "q", "E", "bat", "-");
            var conta = Energia.Calcular(c, Simulador.App.Bancada.DeUmaSo(Pino13(alto)));
            return (conta.CorrenteDoRamo("q", "coletor"), conta.TensaoDoPino("q", "C") - conta.TensaoDoPino("q", "E"));
        }
        var saturado = Npn(1000, true);
        var desligado = Npn(1000, false);
        var ativo = Npn(100_000, true);
        Conferir("transistor: o 2N2222A com 1 kohm na base satura (a carga leva ~88 mA, Vce ~0,2 V); com o pino em LOW, desliga",
            saturado.Ic is > 0.080 and < 0.090 && saturado.Vce < 0.5 && Math.Abs(desligado.Ic) < 1e-5,
            $"HIGH: {saturado.Ic * 1000:0.0} mA, Vce {saturado.Vce:0.00} V | LOW: {desligado.Ic * 1000:0.000} mA");
        Conferir("transistor: com 100 kohm na base ele fica na regiao ativa (Ic = beta x Ib ~ 3,9 mA)",
            ativo.Ic is > 0.0035 and < 0.0043 && ativo.Vce > 5, $"{ativo.Ic * 1000:0.00} mA, Vce {ativo.Vce:0.0} V");

        // ---- MOSFET: 12 V, carga de 10 ohms, gate direto no D13 (3,3 V) ----
        static (double Id, double Vds) Mosfet(string modelo)
        {
            var c = Base(12, out _);
            c.Pecas.AddRange([new PecaNoCircuito { Id = "m", Tipo = "mosfet-n", X = 300, Y = 0, Modelo = modelo }, R("carga", 10)]);
            Ligar(c, "placa", "D13", "m", "G");
            Ligar(c, "bat", "+", "carga", "1"); Ligar(c, "carga", "2", "m", "D"); Ligar(c, "m", "S", "bat", "-");
            var conta = Energia.Calcular(c, Simulador.App.Bancada.DeUmaSo(Pino13(true)));
            return (conta.CorrenteDoRamo("m", "dreno"), conta.TensaoDoPino("m", "D") - conta.TensaoDoPino("m", "S"));
        }
        var logico = Mosfet("irlz44n");
        var comum = Mosfet("irf520");
        Conferir("MOSFET: o IRLZ44N liga inteiro com os 3,3 V da ESP32 (1,2 A na carga); o IRF520 quase nao liga",
            logico.Id is > 1.15 and < 1.21 && logico.Vds < 0.1 && comum.Id < 0.06,
            $"IRLZ44N: {logico.Id:0.00} A, Vds {logico.Vds:0.000} V | IRF520: {comum.Id * 1000:0} mA, Vds {comum.Vds:0.0} V");

        // ---- opto: o LED pelo D13 (330 ohms), o coletor com 10 kohm para 5 V ----
        {
            var c = Base(5, out _);
            c.Pecas.AddRange([new PecaNoCircuito { Id = "o", Tipo = "optoacoplador", X = 300, Y = 0 }, R("rl", 330), R("pull", 10_000)]);
            Ligar(c, "placa", "D13", "rl", "1"); Ligar(c, "rl", "2", "o", "A"); Ligar(c, "o", "K", "placa", "GND.1");
            Ligar(c, "bat", "+", "pull", "1"); Ligar(c, "pull", "2", "o", "C"); Ligar(c, "o", "E", "bat", "-");
            double ligado = Energia.Calcular(c, Simulador.App.Bancada.DeUmaSo(Pino13(true))).TensaoDoPino("o", "C");
            double apagado = Energia.Calcular(c, Simulador.App.Bancada.DeUmaSo(Pino13(false))).TensaoDoPino("o", "C");
            Conferir("opto: o LED aceso puxa o coletor do outro lado para baixo; apagado, o pull-up deixa em 5 V",
                ligado < 0.5 && apagado > 4.9, $"aceso: {ligado:0.00} V | apagado: {apagado:0.00} V");
        }

        // ---- fusivel: 1 A com 2,4 A abre, e depois de queimado nao passa nada ----
        {
            var c = new Circuito();
            c.Pecas.AddRange([
                new PecaNoCircuito { Id = "bat", Tipo = "bateria", X = 0, Y = 0, Volts = 12, MiliAmperes = 20000, MiliAmperesHora = 1000 },
                new PecaNoCircuito { Id = "f", Tipo = "fusivel", X = 200, Y = 0, Modelo = "1a" }, R("carga", 5)]);
            Ligar(c, "bat", "+", "f", "1"); Ligar(c, "f", "2", "carga", "1"); Ligar(c, "carga", "2", "bat", "-");
            var ed = new EditorCircuito();
            ed.Carregar(c);
            var b = Simulador.App.Bancada.DeUmaSo(new Maquina());
            var rede = new Netlist(c);
            ed.AvancarEnergiaParaTeste(b, rede, 0.05);
            var abriu = ed.EstourosPelaEnergia().FirstOrDefault(e => e.Peca == "f");
            if (abriu is { Queimou: true }) ed.Queimar("f");
            ed.AvancarEnergiaParaTeste(b, rede, 0.05);
            double depois = ed.EnergiaDoQuadro.CorrenteDaBateria("bat");
            Conferir("fusivel: o de 1 A abre com 2,4 A, e aberto nao passa mais nada",
                abriu is { Queimou: true } && Math.Abs(depois) < 1e-4, $"{abriu?.Motivo} | depois: {depois * 1000:0.000} mA");
        }

        // ---- varistor: 27 V; com 12 V nao conduz, com 40 V por 100 ohms segura ~27 V ----
        {
            double Tensao(double volts)
            {
                var c = new Circuito();
                c.Pecas.AddRange([
                    new PecaNoCircuito { Id = "bat", Tipo = "bateria", X = 0, Y = 0, Volts = volts, MiliAmperes = 20000, MiliAmperesHora = 1000 },
                    R("r", 100), new PecaNoCircuito { Id = "v", Tipo = "varistor", X = 300, Y = 0 }]);
                Ligar(c, "bat", "+", "r", "1"); Ligar(c, "r", "2", "v", "1"); Ligar(c, "v", "2", "bat", "-");
                var conta = Energia.Calcular(c, null);
                return conta.TensaoDoPino("v", "1") - conta.TensaoDoPino("v", "2");
            }
            double baixo = Tensao(12), alto = Tensao(40);
            Conferir("varistor: com 12 V ele nao faz nada; com 40 V ele segura a tensao perto dos 27 V",
                Math.Abs(baixo - 12) < 0.05 && alto is > 27 and < 28.5, $"12 V -> {baixo:0.00} V | 40 V -> {alto:0.00} V");
        }
    }
}
