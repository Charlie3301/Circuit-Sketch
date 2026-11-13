namespace Simulador.App;

/// <summary>Arrastar e girar a peca leva junto tudo o que e desenhado por cima dela.</summary>
public static partial class Autoteste
{
    static void Editor_SobreposicoesNoArraste()
    {
        saida.AppendLine("o que vive em cima da peca acompanha o arraste");

        static void Ligar(Circuito c, PecaNoCircuito a, string pa, PecaNoCircuito b, string pb)
            => c.Fios.Add(new Fio { DePeca = a.Id, DePino = pa, ParaPeca = b.Id, ParaPino = pb });

        var c = new Circuito();
        var pilhas = new PecaNoCircuito { Id = "pilhas", Tipo = "bateria", X = 900, Y = 500, Volts = 6, MiliAmperes = 2000 };
        var motor = new PecaNoCircuito { Id = "motor", Tipo = "motor-tt", X = 100, Y = 100, Nome = "M1" };
        var rele = new PecaNoCircuito { Id = "rele", Tipo = "ks2e-m-dc5", X = 400, Y = 100 };
        var biaxial = new PecaNoCircuito { Id = "biaxial", Tipo = "biaxial-stepper", X = 100, Y = 400 };
        var analisador = new PecaNoCircuito { Id = "analisador", Tipo = "logic-analyzer", X = 500, Y = 400 };
        c.Pecas.AddRange([pilhas, motor, rele, biaxial, analisador]);
        Ligar(c, pilhas, "+", motor, "+");
        Ligar(c, pilhas, "-", motor, "-");

        var ed = new EditorCircuito { PassoFixoParaTeste = 0.04 };
        ed.Carregar(c);
        var b = Simulador.App.Bancada.DeUmaSo(new Maquina { Perfil = PerfilPlaca.Esp32 });
        var rede = new Netlist(c);
        for (int i = 0; i < 75; i++) ed.AplicarEntradas(b, rede);
        ed.RefletirEstado(b, rede);

        int total = 0;
        foreach (var peca in new[] { motor, rele, biaxial, analisador })
        {
            var def = Catalogo.Obter(peca.Tipo);
            var antes = ed.SobreposicoesParaTeste(peca.Id);
            double x0 = peca.X, y0 = peca.Y;

            // Pega pelo meio e arrasta, sem quadro de simulacao no meio: a simulacao parada.
            var pega = new System.Windows.Point(peca.X + def.Largura / 2, peca.Y + def.Altura / 2);
            ed.Apertar(pega);
            ed.Mover(new System.Windows.Point(pega.X + 137, pega.Y + 83), true);
            var naMao = ed.SobreposicoesNaMaoParaTeste(peca.Id);
            ed.Soltar();
            var solto = ed.SobreposicoesNaMaoParaTeste(peca.Id);
            Conferir($"{peca.Tipo}: durante o arraste o que vive em cima vai na mao, por cima do corpo; solto, volta",
                naMao.NaMao == naMao.Total && solto.NaMao == 0, $"{naMao.NaMao}/{naMao.Total} na mao, {solto.NaMao} depois");
            double dx = peca.X - x0, dy = peca.Y - y0;

            var depois = ed.SobreposicoesParaTeste(peca.Id);
            var paradas = antes.Zip(depois)
                .Where(par => Math.Abs(par.Second.Ponto.X - par.First.Ponto.X - dx) > 0.5
                              || Math.Abs(par.Second.Ponto.Y - par.First.Ponto.Y - dy) > 0.5)
                .Select(par => $"{par.First.Tipo} {par.First.Ponto.X:0},{par.First.Ponto.Y:0} -> {par.Second.Ponto.X:0},{par.Second.Ponto.Y:0}")
                .ToList();
            total += antes.Count;
            saida.AppendLine($"        {peca.Tipo}: {antes.Count} desenho(s) por cima; a peca andou {dx:0},{dy:0}");
            Conferir($"{peca.Tipo}: cada desenho por cima anda o mesmo que a peca (nada fica para tras)",
                (dx != 0 || dy != 0) && antes.Count == depois.Count && paradas.Count == 0,
                paradas.Count > 0 ? string.Join("; ", paradas) : $"{antes.Count} -> {depois.Count}, andou {dx:0},{dy:0}");
        }
        Conferir("o teste viu desenhos por cima (rotulo do giro, luz do rele, ponteiros, ondas)", total >= 4, $"{total}");

        // Girar: o rotulo do giro vai para cima da peca girada, e nao fica onde estava.
        {
            var def = Catalogo.Obter(motor.Tipo);
            var antes = ed.SobreposicoesParaTeste(motor.Id);
            var pega = new System.Windows.Point(motor.X + def.Largura / 2, motor.Y + def.Altura / 2);
            ed.Apertar(pega);
            ed.Soltar();
            ed.Girar();
            var depois = ed.SobreposicoesParaTeste(motor.Id);
            string rotulo = ed.RotuloDeGiroParaTeste(motor.Id);
            saida.AppendLine($"        motor girado {motor.Rotacao}: {string.Join(" ", depois.Select(p => $"{p.Tipo}@{p.Ponto.X:0},{p.Ponto.Y:0}"))}");
            Conferir("girar: os desenhos por cima seguem a peca girada (o rotulo continua em cima dela)",
                motor.Rotacao == 90 && rotulo is not null && depois.Count == antes.Count
                && !antes.Zip(depois).All(par => par.First.Ponto == par.Second.Ponto),
                $"{motor.Rotacao} {rotulo} {antes.Count}/{depois.Count}");
        }

        // Mover para um ponto exato: sem grade, e a protoboard leva quem esta nela.
        {
            var c2 = new Circuito();
            var proto = new PecaNoCircuito { Id = "proto", Tipo = Catalogo.IdProtoboard, X = 0, Y = 0 };
            c2.Pecas.Add(proto);
            var ed2 = new EditorCircuito();
            ed2.Carregar(c2);
            var led = new PecaNoCircuito { Id = "led", Tipo = "led", X = 0, Y = 0 };
            var furo = Catalogo.Obter(proto.Tipo).Pinos.First(p => p.Nome.StartsWith("10") || p.Nome.Contains("10"));
            c2.Pecas.Add(led);
            ed2.Carregar(c2);
            var defLed = Catalogo.Obter("led");
            var perna = defLed.Pinos[0];
            led.X = furo.X - perna.X; led.Y = furo.Y - perna.Y;
            ed2.Carregar(c2);
            double ledX = led.X, ledY = led.Y;
            ed2.MoverPara(proto, 123.45, 67.8);
            Conferir("mover para: a peca vai ao ponto exato, sem a grade",
                Math.Abs(proto.X - 123.45) < 1e-9 && Math.Abs(proto.Y - 67.8) < 1e-9, $"{proto.X},{proto.Y}");
            Conferir("mover para: o LED espetado na protoboard vai junto",
                Math.Abs(led.X - ledX - 123.45) < 1e-9 && Math.Abs(led.Y - ledY - 67.8) < 1e-9, $"{led.X - ledX},{led.Y - ledY}");
        }

        // O fio em construcao comeca do pino novo, e nao da curva do fio desistido.
        {
            var c5 = new Circuito();
            var ledA = new PecaNoCircuito { Id = "a", Tipo = "led", X = 100, Y = 100 };
            var ledB = new PecaNoCircuito { Id = "b", Tipo = "led", X = 600, Y = 400 };
            c5.Pecas.AddRange([ledA, ledB]);
            var ed5 = new EditorCircuito();
            ed5.Carregar(c5);
            var defLed = Catalogo.Obter("led");
            var pinoA = Geometria.PosicaoPino(ledA, defLed, defLed.Pinos[0]);
            var pinoB = Geometria.PosicaoPino(ledB, defLed, defLed.Pinos[0]);
            ed5.Apertar(pinoA); ed5.Soltar();
            ed5.Mover(new System.Windows.Point(400, 300), false);
            var puxado = ed5.FioEmCursoParaTeste;
            ed5.CancelarFio();
            var desistido = ed5.FioEmCursoParaTeste;
            ed5.Apertar(pinoB); ed5.Soltar();
            var novo = ed5.FioEmCursoParaTeste;
            Conferir("fio em construcao: desistido some; o proximo nasce no pino novo, sem a curva antiga",
                puxado.Visivel && puxado.Caixa.Width > 100 && !desistido.Visivel
                && novo.Visivel && !novo.Caixa.IsEmpty && novo.Caixa.Width < 20 && novo.Caixa.Height < 20
                && Math.Abs(novo.Caixa.X - pinoB.X) < 20 && Math.Abs(novo.Caixa.Y - pinoB.Y) < 20,
                $"{puxado.Caixa} / {desistido.Visivel} / {novo.Caixa} x {pinoB}");
            ed5.CancelarFio();
        }

        // O fundo preso ao circuito: o pan do botao do meio anda os pontos junto.
        foreach (string estilo in new[] { EditorCircuito.FundoPontilhado, EditorCircuito.FundoGrade, EditorCircuito.FundoQuadriculado })
        {
            var ed3 = new EditorCircuito { MostrarGrade = true, EstiloDoFundo = estilo };
            ed3.Carregar(new Circuito());
            ed3.AplicarGrade();
            ed3.PanearParaTeste(37, -21);
            var pincel = ed3.Background as System.Windows.Media.DrawingBrush;
            var origem = ed3.OrigemNaTelaParaTeste;
            double passo = Catalogo.Passo;
            // O furo (0, 0) fica no meio do primeiro passo do ladrilho.
            bool preso = pincel is not null
                && Math.Abs(Resto(origem.X - pincel.Viewport.X - passo / 2, passo)) < 1e-6
                && Math.Abs(Resto(origem.Y - pincel.Viewport.Y - passo / 2, passo)) < 1e-6;
            Conferir($"fundo {estilo}: o ladrilho anda junto com o pan do botao do meio",
                preso, pincel is null ? "sem pincel" : $"{pincel.Viewport} x {origem}");
        }
        {
            var ed4 = new EditorCircuito { MostrarGrade = false, EstiloDoFundo = EditorCircuito.FundoGrade };
            ed4.Carregar(new Circuito());
            ed4.AplicarGrade();
            Conferir("fundo limpo: grade desligada e so a cor", ed4.Background is System.Windows.Media.SolidColorBrush);
        }

        static double Resto(double a, double b)
        {
            double r = ((a % b) + b) % b;
            return Math.Min(r, b - r);
        }
    }
}
