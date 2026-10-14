using System.Windows;
using System.Windows.Media;

namespace Simulador.App;

/// <summary>
/// Virar a PCB (Alpha 24, etapa 2): ver o lado de baixo para passar os fios.
///
/// Como na bancada, virar a placa e virar TUDO o que esta montado nela: a
/// PCB e as pecas espetadas espelham pelo eixo do meio da placa (a de cima
/// vira de baixo, o furo da esquerda vai para a direita). As pecas ficam do
/// outro lado -- aparecem apagadas, sem clique --, as ilhas de cobre e as
/// soldas das pernas ficam a mostra, e o fio feito agora e do lado de baixo
/// (Fio.Lado): a trilha de solda que liga furo a furo. Virando de novo, tudo
/// volta, e os fios de baixo e que ficam apagados.
///
/// O netlist nao muda: as pernas continuam nos mesmos furos (o espelho leva
/// as duas juntas) e o fio e fio dos dois lados.
/// </summary>
public partial class EditorCircuito
{
    /// <summary>A cor da trilha de baixo: o estanho da solda.</summary>
    internal const string CorDaTrilha = "#C3C7CE";

    /// <summary>As pecas espetadas em cada PCB (de qualquer lado), recalculadas a cada desenho.</summary>
    readonly Dictionary<string, string> pcbDaPeca = [];

    /// <summary>As pecas do outro lado agora: montadas numa PCB virada.</summary>
    readonly HashSet<string> doOutroLado = [];

    void CalcularLadosDasPcbs()
    {
        pcbDaPeca.Clear();
        doOutroLado.Clear();
        foreach (var pcb in Atual.Pecas.Where(p => Catalogo.EhPcb(p.Tipo)).ToList())
            foreach (var peca in EspetadasEm(pcb))
            {
                pcbDaPeca[peca.Id] = pcb.Id;
                if (pcb.Virada) doOutroLado.Add(peca.Id);
            }
    }

    internal bool EstaDoOutroLado(PecaNoCircuito peca) => peca is not null && doOutroLado.Contains(peca.Id);

    /// <summary>A PCB a que esta ponta de fio pertence (o furo dela, ou uma peca espetada nela), ou nulo.</summary>
    string PcbDaPonta(string idDaPeca)
        => Atual.Achar(idDaPeca) is { } p && Catalogo.EhPcb(p.Tipo) ? p.Id : pcbDaPeca.GetValueOrDefault(idDaPeca);

    /// <summary>
    /// O fio esta do lado que nao se ve agora? O de baixo, com a PCB do lado
    /// de cima; o de cima entre pecas da placa, com ela virada. O fio que
    /// sai da placa (para uma placa de fora, uma bateria) aparece sempre.
    /// </summary>
    internal bool FioDoOutroLado(Fio fio)
    {
        string a = PcbDaPonta(fio.DePeca), b = PcbDaPonta(fio.ParaPeca);
        string pcb = a ?? b;
        if (pcb is null) return false;
        bool virada = Atual.Achar(pcb)?.Virada == true;
        if (fio.Lado == Fio.LadoDeBaixo) return !virada;
        return virada && a is not null && b is not null;
    }

    /// <summary>Para o teste: seleciona a peca, como um clique nela.</summary>
    internal void SelecionarParaTeste(string id) => Selecionar(id, false);

    /// <summary>A barra: vira a PCB selecionada (e o que esta montado nela).</summary>
    public void VirarPcbSelecionada()
    {
        if (Selecionada is { } pcb && Catalogo.EhPcb(pcb.Tipo)) VirarPcb(pcb);
    }

    /// <summary>
    /// O espelho pelo eixo do meio da placa (o "vertical" dela, que gira com
    /// ela). A peca montada: o centro espelhado, a rotacao 2 x a da placa
    /// menos a dela, e o espelho trocado -- e o que leva cada perna para o
    /// furo espelhado. As dobras dos fios da placa vao junto.
    /// </summary>
    internal void VirarPcb(PecaNoCircuito pcb)
    {
        var defPcb = Catalogo.Obter(pcb.Tipo);
        if (defPcb is null) return;
        CalcularLadosDasPcbs();
        var montadas = pcbDaPeca.Where(p => p.Value == pcb.Id).Select(p => Atual.Achar(p.Key)).Where(p => p is not null).ToList();

        var (largura, altura) = Geometria.Tamanho(pcb, defPcb);
        var centro = new Point(pcb.X + largura / 2, pcb.Y + altura / 2);
        double r = pcb.Rotacao * Math.PI / 180;
        var eixo = new Vector(-Math.Sin(r), Math.Cos(r));   // o "vertical" da placa, no mundo
        Point Espelhar(Point p)
        {
            var v = p - centro;
            double ao = v.X * eixo.X + v.Y * eixo.Y;
            return centro + (2 * ao * eixo - v);
        }

        foreach (var peca in montadas)
        {
            var def = Catalogo.Obter(peca.Tipo);
            if (def is null) continue;
            var (l, a) = Geometria.Tamanho(peca, def);
            var novo = Espelhar(new Point(peca.X + l / 2, peca.Y + a / 2));
            peca.X = novo.X - l / 2;
            peca.Y = novo.Y - a / 2;
            peca.Rotacao = ((2 * pcb.Rotacao - peca.Rotacao) % 360 + 360) % 360;
            peca.Espelhada = !peca.Espelhada;
        }

        var daPlaca = new HashSet<string>(montadas.Select(p => p.Id)) { pcb.Id };
        foreach (var fio in Atual.Fios.Where(f => daPlaca.Contains(f.DePeca) && daPlaca.Contains(f.ParaPeca)))
            foreach (var dobra in fio.Dobras)
            {
                var p = Espelhar(new Point(dobra.X, dobra.Y));
                dobra.X = p.X;
                dobra.Y = p.Y;
            }

        pcb.Espelhada = !pcb.Espelhada;
        pcb.Virada = !pcb.Virada;
        Redesenhar();
        Selecionar(pcb.Id, false);
        Mudou?.Invoke();
    }

    /// <summary>Depois de desenhar: o que esta do outro lado fica apagado e sem clique.</summary>
    void ApagarOOutroLado()
    {
        foreach (string id in doOutroLado)
        {
            if (visuais.TryGetValue(id, out var visual)) { visual.Opacity = 0.2; visual.IsHitTestVisible = false; }
            if (parteMovel.TryGetValue(id, out var movel)) movel.Opacity = 0.2;
            if (rotulosDePeca.TryGetValue(id, out var rotulo)) rotulo.Opacity = 0.35;
        }
    }

    /// <summary>
    /// As soldas do lado de baixo: um ponto de estanho em cada furo com perna
    /// espetada (so se ve com a placa virada).
    /// </summary>
    GeometryGroup SoldasDaPcb(PecaNoCircuito pcb, PecaDef def)
    {
        var soldas = new GeometryGroup();
        if (!pcb.Virada) return soldas;
        var furos = def.Pinos.Select(p => (p, Geometria.PosicaoPino(pcb, def, p))).ToList();
        foreach (var peca in Atual.Pecas)
        {
            if (peca == pcb || Catalogo.EhProtoboard(peca.Tipo) || Catalogo.Obter(peca.Tipo) is not { } d) continue;
            foreach (var perna in d.Pinos)
            {
                var q = Geometria.PosicaoPino(peca, d, perna);
                foreach (var (furo, pf) in furos)
                {
                    double dx = pf.X - q.X, dy = pf.Y - q.Y;
                    if (dx * dx + dy * dy > Netlist.RaioDoFuro * Netlist.RaioDoFuro) continue;
                    soldas.Children.Add(new EllipseGeometry(new Point(furo.X, furo.Y), 3.2, 3.2));
                    break;
                }
            }
        }
        return soldas;
    }
}
