namespace Simulador.App;

/// <summary>
/// Circuitos sem placa nenhuma: a bateria e quem manda. E o primeiro
/// exercicio de eletronica -- bateria, resistor e LED -- e o primeiro de
/// motor, e os dois precisam funcionar so com energia, sem Rodar nada.
/// </summary>
public static partial class Autoteste
{
    static void Energia_SemPlaca()
    {
        saida.AppendLine("energia sem placa: bateria, LED e motor");

        static void Ligar(Circuito c, PecaNoCircuito a, string pa, PecaNoCircuito b, string pb)
            => c.Fios.Add(new Fio { DePeca = a.Id, DePino = pa, ParaPeca = b.Id, ParaPino = pb });

        // O editor sem placa: a conta de energia do quadro e o desenho.
        static (EditorCircuito Ed, Energia Conta) Rodar(Circuito c, double segundos = 0.2)
        {
            var ed = new EditorCircuito();
            ed.Carregar(c);
            var bancada = Simulador.App.Bancada.DeUmaSo(new Maquina { Perfil = PerfilPlaca.Esp32 });
            var rede = new Netlist(c);
            Energia conta = null;
            for (double t = 0; t < segundos - 1e-9; t += 0.04) conta = ed.AvancarEnergiaParaTeste(bancada, rede, 0.04);
            ed.RefletirEstado(bancada, rede);
            return (ed, conta);
        }

        static PecaNoCircuito Bateria(double volts, double mA = 500, double x = 0)
            => new() { Tipo = "bateria", X = x, Y = 200, Volts = volts, MiliAmperes = mA };

        // ---- 9 V, 330 ohms e o LED: o primeiro circuito de todos ----
        {
            var c = new Circuito();
            var bat = Bateria(9);
            var r = new PecaNoCircuito { Tipo = "resistor", X = 200, Y = 0, Ohms = 330 };
            var led = new PecaNoCircuito { Tipo = "led", X = 400, Y = 0 };
            c.Pecas.AddRange([bat, r, led]);
            Ligar(c, bat, "+", r, "1");
            Ligar(c, r, "2", led, "A");
            Ligar(c, led, "C", bat, "-");
            var (ed, conta) = Rodar(c);
            double mA = conta.CorrenteDoRamo(led.Id, "led") * 1000;
            double brilho = ed.BrilhoParaTeste(led.Id);
            saida.AppendLine($"        9 V + 330 ohms + LED: {mA:0.0} mA, brilho {brilho:0.00}, "
                             + $"bateria entregando {conta.CorrenteDaBateria(bat.Id) * 1000:0.0} mA");
            Conferir("sem placa: bateria de 9 V + 330 ohms acende o LED com ~20 mA",
                mA is > 18 and < 23 && brilho > 0.5, $"{mA:0.0} mA, brilho {brilho:0.00}");
            Conferir("sem placa: e nada queima nem avisa", ed.EstourosPelaEnergia().Count == 0,
                string.Join(" | ", ed.EstourosPelaEnergia().Select(e => e.Motivo)));

            // O - solto: o circuito nao fecha, e o LED apaga. Antes o "-" da
            // bateria era o terra da conta, e o LED acendia assim mesmo.
            c.Fios.RemoveAt(2);
            var (edAberto, contaAberta) = Rodar(c);
            Conferir("sem placa: com o - da bateria solto o circuito nao fecha e o LED fica apagado",
                Math.Abs(contaAberta.CorrenteDoRamo(led.Id, "led")) < 1e-5 && edAberto.BrilhoParaTeste(led.Id) == 0,
                $"{contaAberta.CorrenteDoRamo(led.Id, "led") * 1000:0.000} mA");

            // Ao contrario: o diodo nao conduz.
            c.Fios.Clear();
            Ligar(c, bat, "+", r, "1");
            Ligar(c, r, "2", led, "C");
            Ligar(c, led, "A", bat, "-");
            var (edInvertido, contaInvertida) = Rodar(c);
            Conferir("sem placa: LED ao contrario nao acende",
                Math.Abs(contaInvertida.CorrenteDoRamo(led.Id, "led")) < 1e-5 && edInvertido.BrilhoParaTeste(led.Id) == 0);
        }

        // ---- sem resistor: queima ----
        {
            var c = new Circuito();
            var bat = Bateria(9);
            var led = new PecaNoCircuito { Tipo = "led", X = 400, Y = 0 };
            c.Pecas.AddRange([bat, led]);
            Ligar(c, bat, "+", led, "A");
            Ligar(c, led, "C", bat, "-");
            var (ed, conta) = Rodar(c);
            var estouro = ed.EstourosPelaEnergia().FirstOrDefault(e => e.Peca == led.Id);
            saida.AppendLine($"        9 V direto no LED: {conta.CorrenteDoRamo(led.Id, "led") * 1000:0} mA; {estouro?.Motivo}");
            Conferir("sem placa: LED direto na bateria de 9 V, sem resistor, queima (e o aviso diz o resistor certo)",
                estouro is { Queimou: true } && estouro.Motivo.Contains("470"), estouro?.Motivo ?? "(nada)");
        }

        // ---- duas baterias em serie somam ----
        {
            var c = new Circuito();
            var b1 = Bateria(4.5, 1000, 0);
            var b2 = Bateria(4.5, 1000, 200);
            var r = new PecaNoCircuito { Tipo = "resistor", X = 200, Y = 0, Ohms = 330 };
            var led = new PecaNoCircuito { Tipo = "led", X = 400, Y = 0 };
            c.Pecas.AddRange([b1, b2, r, led]);
            Ligar(c, b1, "+", b2, "-");
            Ligar(c, b2, "+", r, "1");
            Ligar(c, r, "2", led, "A");
            Ligar(c, led, "C", b1, "-");
            var (_, conta) = Rodar(c);
            double mA = conta.CorrenteDoRamo(led.Id, "led") * 1000;
            Conferir("sem placa: duas baterias de 4,5 V em serie dao os 9 V (o LED com ~20 mA)",
                mA is > 18 and < 23, $"{mA:0.0} mA");
        }

        // ---- botao em serie ----
        {
            var c = new Circuito();
            var bat = Bateria(9);
            var botao = new PecaNoCircuito { Tipo = "pushbutton", X = 150, Y = 0 };
            var r = new PecaNoCircuito { Tipo = "resistor", X = 300, Y = 0, Ohms = 330 };
            var led = new PecaNoCircuito { Tipo = "led", X = 450, Y = 0 };
            c.Pecas.AddRange([bat, botao, r, led]);
            Ligar(c, bat, "+", botao, "1.l");
            Ligar(c, botao, "2.l", r, "1");
            Ligar(c, r, "2", led, "A");
            Ligar(c, led, "C", bat, "-");
            var (edSolto, _) = Rodar(c);
            var ed = new EditorCircuito();
            ed.Carregar(c);
            ed.BotoesPressionados.Add(botao.Id);
            var bancada = Simulador.App.Bancada.DeUmaSo(new Maquina { Perfil = PerfilPlaca.Esp32 });
            var rede = new Netlist(c);
            ed.AvancarEnergiaParaTeste(bancada, rede, 0.04);
            ed.RefletirEstado(bancada, rede);
            Conferir("sem placa: botao em serie -- solto o LED apaga, apertado acende",
                edSolto.BrilhoParaTeste(led.Id) == 0 && ed.BrilhoParaTeste(led.Id) > 0.5,
                $"{edSolto.BrilhoParaTeste(led.Id):0.00} / {ed.BrilhoParaTeste(led.Id):0.00}");
        }

        // ---- RGB de catodo comum e de anodo comum, na bateria ----
        {
            var c = new Circuito();
            var bat = Bateria(9);
            var r = new PecaNoCircuito { Tipo = "resistor", X = 200, Y = 0, Ohms = 330 };
            var rgb = new PecaNoCircuito { Tipo = "rgb-led", X = 400, Y = 0 };
            c.Pecas.AddRange([rgb, bat, r]);   // o RGB antes da bateria na lista, de proposito
            Ligar(c, bat, "+", r, "1");
            Ligar(c, r, "2", rgb, "R");
            Ligar(c, rgb, "COM", bat, "-");
            var (_, catodo) = Rodar(c);
            double vermelho = catodo.CorrenteDoRamo(rgb.Id, "R") * 1000;

            c.Fios.Clear();
            Ligar(c, bat, "+", rgb, "COM");
            Ligar(c, rgb, "B", r, "1");
            Ligar(c, r, "2", bat, "-");
            var (_, anodo) = Rodar(c);
            double azul = Math.Abs(anodo.CorrenteDoRamo(rgb.Id, "B")) * 1000;
            saida.AppendLine($"        RGB na bateria: catodo comum, vermelho {vermelho:0.0} mA; anodo comum, azul {azul:0.0} mA");
            Conferir("sem placa: LED RGB de catodo comum (vermelho) e de anodo comum (azul) acendem na bateria",
                vermelho is > 15 and < 25 && azul is > 15 and < 25, $"{vermelho:0.0} / {azul:0.0}");
        }

        // ---- a bateria em curto: aviso ----
        {
            var c = new Circuito();
            var bat = Bateria(9);
            c.Pecas.Add(bat);
            Ligar(c, bat, "+", bat, "-");
            var (ed, conta) = Rodar(c);
            var aviso = ed.EstourosPelaEnergia().FirstOrDefault(e => e.Peca == bat.Id);
            saida.AppendLine($"        bateria em curto: {conta.CorrenteDaBateria(bat.Id):0.0} A; {aviso?.Motivo}");
            Conferir("sem placa: bateria em curto avisa (nao explode, mas esquenta e se esgota)",
                aviso is { Queimou: false } && aviso.Motivo.Contains("curto"), aviso?.Motivo ?? "(nada)");
        }

        // ---- os dois projetos de teste que o usuario pediu, na janela ----
        string pasta = AcharABateria();
        foreach (string nome in new[] { "bateria-e-led", "bateria-e-motor" })
        {
            string arquivo = pasta is null ? null : System.IO.Path.Combine(pasta, "energia", nome + ".sktch");
            if (arquivo is null || !System.IO.File.Exists(arquivo))
            {
                Conferir($"teste {nome}: o arquivo existe na bateria", false, arquivo ?? "(sem bateria)");
                continue;
            }

            var (circuito, codigo) = Sktch.Abrir(arquivo);
            var janela = new MainWindow();
            try
            {
                janela.TesteAbrir(circuito, codigo ?? "");
                var ed = janela.TesteEditorCircuito;
                bool funcionou = nome == "bateria-e-led"
                    ? EsperarPor(() => circuito.Pecas.Where(p => p.Tipo == "led").All(p => ed.BrilhoParaTeste(p.Id) > 0.5), 3000)
                    : EsperarPor(() => circuito.Pecas.Where(p => EditorCircuito.EhMotorDc(p.Tipo)).All(p => ed.RpmDoMotor(p.Id) > 200), 4000);
                string como = nome == "bateria-e-led"
                    ? string.Join(", ", circuito.Pecas.Where(p => p.Tipo == "led").Select(p => $"brilho {ed.BrilhoParaTeste(p.Id):0.00}"))
                    : string.Join(", ", circuito.Pecas.Where(p => EditorCircuito.EhMotorDc(p.Tipo)).Select(p => $"{ed.RpmDoMotor(p.Id):0} rpm"));
                saida.AppendLine($"        {nome}.sktch na janela, sem Rodar: {como}");
                Conferir($"teste {nome}: abre e funciona so com a bateria, sem placa e sem Rodar", funcionou, como);
            }
            finally
            {
                try { janela.Close(); } catch (InvalidOperationException) { }
            }
        }
    }

    /// <summary>
    /// Um projeto da bateria sem placa: a energia faz o trabalho. Todo LED
    /// precisa acender dentro da faixa (sem queimar) e todo motor, girar.
    /// </summary>
    static string RodarSemPlaca(Circuito circuito)
    {
        var ed = new EditorCircuito();
        ed.Carregar(circuito);
        var bancada = Simulador.App.Bancada.DeUmaSo(new Maquina { Perfil = PerfilPlaca.Esp32 });
        var rede = new Netlist(circuito);
        Energia conta = null;
        for (int i = 0; i < 50; i++) conta = ed.AvancarEnergiaParaTeste(bancada, rede, 0.04);
        if (conta is null) return "sem bateria nem motor: nada para a energia fazer";
        ed.RefletirEstado(bancada, rede);

        foreach (var estouro in ed.EstourosPelaEnergia())
            return $"{estouro.Peca}: {estouro.Motivo}";
        foreach (var led in circuito.Pecas.Where(p => p.Tipo == "led"))
            if (ed.BrilhoParaTeste(led.Id) <= 0.3) return $"o LED {led.Id} nao acendeu";
        foreach (var motor in circuito.Pecas.Where(p => EditorCircuito.EhMotorDc(p.Tipo)))
            if (ed.RpmDoMotor(motor.Id) == 0) return $"o motor {motor.Id} nao girou";
        return null;
    }
}
