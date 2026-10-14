namespace Simulador.App;

/// <summary>
/// A PCB perfurada (Alpha 24): o tamanho e o tipo, os furos sao isolados,
/// e so o fio entre dois furos liga uma coisa na outra.
/// </summary>
public static partial class Autoteste
{
    static void Pcb_Base()
    {
        saida.AppendLine("PCB perfurada");

        var def = Catalogo.Obter("pcb-10x6");
        Conferir("PCB: qualquer tamanho nasce na hora (10 x 6 = 60 furos), e o tamanho fora da faixa nao existe",
            def is { Pinos.Count: 60 } && Catalogo.Obter("pcb-2x900") is null && Catalogo.Obter(Catalogo.IdPcbPadrao) is not null,
            $"{def?.Pinos.Count} furos");
        Conferir("PCB: os furos sao isolados (nenhuma ligacao interna)",
            !Catalogo.LigacoesInternas("pcb-10x6").Any(), "");

        // Uma bateria em dois furos e um resistor em outros dois: sem fio por baixo, nada passa; com o fio, passa.
        static double Corrente(bool comFio)
        {
            var c = new Circuito();
            c.Pecas.AddRange([
                new PecaNoCircuito { Id = "pcb", Tipo = "pcb-10x6", X = 0, Y = 0 },
                new PecaNoCircuito { Id = "bat", Tipo = "bateria", X = 0, Y = 300, Volts = 5, MiliAmperes = 20000, MiliAmperesHora = 1000 },
                new PecaNoCircuito { Id = "r", Tipo = "resistor", X = 200, Y = 0, Ohms = 100 }]);
            void L(string a, string pa, string b, string pb) => c.Fios.Add(new Fio { DePeca = a, DePino = pa, ParaPeca = b, ParaPino = pb });
            L("bat", "+", "pcb", "x1y1"); L("r", "1", "pcb", "x3y1");
            L("r", "2", "pcb", "x3y3"); L("bat", "-", "pcb", "x3y3");
            if (comFio) L("pcb", "x1y1", "pcb", "x3y1");
            return Math.Abs(Energia.Calcular(c, null).CorrenteDaBateria("bat"));
        }
        double sem = Corrente(false), com = Corrente(true);
        Conferir("PCB: sem o fio entre os furos nao passa nada; com o fio, 50 mA no resistor de 100 ohms",
            sem < 1e-5 && com is > 0.045 and < 0.052, $"sem: {sem * 1000:0.000} mA | com: {com * 1000:0.0} mA");

        Pcb_Virar();
    }

    /// <summary>
    /// Virar a placa (etapa 2): as pernas continuam nos mesmos furos (o
    /// circuito nao muda), as pecas vao para o outro lado, o fio feito com
    /// ela virada e a trilha de baixo, e virar de novo devolve tudo.
    /// </summary>
    static void Pcb_Virar()
    {
        var c = new Circuito();
        var pcb = new PecaNoCircuito { Id = "pcb", Tipo = "pcb-12x8", X = 100, Y = 100, Rotacao = 90 };
        var r = new PecaNoCircuito { Id = "r", Tipo = "resistor", X = 0, Y = 0, Ohms = 330 };
        c.Pecas.AddRange([pcb, r]);
        var defPcb = Catalogo.Obter(pcb.Tipo);
        var defR = Catalogo.Obter("resistor");

        // Espeta o resistor com a perna 1 no furo x3y4.
        var furo = Geometria.PosicaoPino(pcb, defPcb, defPcb.Pinos.First(p => p.Nome == "x3y4"));
        var perna = Geometria.PosicaoPino(r, defR, defR.Pinos.First(p => p.Nome == "1"));
        r.X += furo.X - perna.X;
        r.Y += furo.Y - perna.Y;
        var ed = new EditorCircuito();
        ed.Carregar(c);

        string FuroDa(string pino)
        {
            var rede = new Netlist(c);
            string raiz = rede.RaizDe(Netlist.No("r", pino));
            return defPcb.Pinos.Select(p => p.Nome).FirstOrDefault(n => rede.RaizDe(Netlist.No("pcb", n)) == raiz) ?? "nenhum";
        }
        string antes1 = FuroDa("1"), antes2 = FuroDa("2");
        double x0 = r.X, y0 = r.Y, rot0 = r.Rotacao;
        Conferir("PCB virar: o resistor espetado (perna 1 no x3y4) liga nos furos", antes1 == "x3y4" && antes2 != "nenhum", $"{antes1}, {antes2}");

        ed.SelecionarParaTeste("pcb");
        ed.VirarPcbSelecionada();
        string depois1 = FuroDa("1"), depois2 = FuroDa("2");
        Conferir("PCB virar: a placa vira (espelhada) e as pernas continuam nos MESMOS furos",
            pcb.Virada && pcb.Espelhada && depois1 == antes1 && depois2 == antes2, $"{depois1}, {depois2}");
        Conferir("PCB virar: o resistor fica do outro lado (apagado, sem clique)", ed.EstaDoOutroLado(r));

        // Um fio entre dois furos livres, com a placa virada: a trilha de baixo.
        var a = Geometria.PosicaoPino(pcb, defPcb, defPcb.Pinos.First(p => p.Nome == "x8y2"));
        var b = Geometria.PosicaoPino(pcb, defPcb, defPcb.Pinos.First(p => p.Nome == "x8y6"));
        ed.Apertar(a);
        ed.Apertar(b);
        var trilha = c.Fios.LastOrDefault();
        Conferir("PCB virar: o fio feito com a placa virada e a trilha de baixo (furo a furo)",
            trilha is { Lado: Fio.LadoDeBaixo } && trilha.DePeca == "pcb" && trilha.ParaPeca == "pcb" && !ed.FioDoOutroLado(trilha),
            trilha is null ? "sem fio" : $"{trilha.DePino}-{trilha.ParaPino}, lado {trilha.Lado}");

        ed.VirarPcbSelecionada();
        Conferir("PCB desvirar: o resistor volta exatamente para onde estava (posicao e giro)",
            !pcb.Virada && !pcb.Espelhada && Math.Abs(r.X - x0) < 1e-6 && Math.Abs(r.Y - y0) < 1e-6 && r.Rotacao == rot0 && !r.Espelhada,
            $"({r.X:0.##}, {r.Y:0.##}) giro {r.Rotacao}");
        Conferir("PCB desvirar: a trilha de baixo fica do outro lado, e o circuito continua o mesmo",
            trilha is not null && ed.FioDoOutroLado(trilha) && FuroDa("1") == antes1 && !ed.EstaDoOutroLado(r));
        var rede = new Netlist(c);
        Conferir("PCB: a trilha de baixo conduz como qualquer fio (x8y2 e x8y6 no mesmo no)",
            rede.RaizDe(Netlist.No("pcb", "x8y2")) == rede.RaizDe(Netlist.No("pcb", "x8y6")));
    }
}
