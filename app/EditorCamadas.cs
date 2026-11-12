using System.Windows;
using System.Windows.Input;

namespace Simulador.App;

/// <summary>
/// A selecao por camadas: o clique pega o que esta por cima (como sempre foi), e
/// clicar de novo no mesmo lugar passa para o que esta atras -- a placa espetada,
/// a protoboard debaixo dela, o fio que passa por baixo, a PCB perfurada. Cada
/// clique a mais vai uma camada mais fundo, e depois da ultima volta a de cima.
/// Arrastar continua movendo o que esta selecionado.
/// </summary>
public partial class EditorCircuito
{
    /// <summary>
    /// Tudo o que da para selecionar debaixo do ponto, na ordem dos cliques: o que o
    /// clique simples pega, os corpos das pecas (a de cima primeiro), as bases
    /// (protoboards e PCBs) e os fios que passam ali -- a ESP32, a protoboard, o fio.
    /// </summary>
    internal List<(string Peca, int Fio)> PilhaNoPonto(Point p)
    {
        var pilha = new List<(string Peca, int Fio)>();
        void Por(string peca, int fio)
        {
            if (!pilha.Contains((peca, fio))) pilha.Add((peca, fio));
        }

        var acerto = Resolver(p);
        if (acerto.Camada is Camada.CorpoPeca or Camada.CorpoPlaca && acerto.Peca is not null) Por(acerto.Peca.Id, -1);
        else if (acerto.Camada == Camada.Fio) Por(null, acerto.IndiceFio);

        // As pecas por cima (a ultima da lista e a de cima): o corpo, ou uma perna
        // perto do ponto (o furo da PCB debaixo da perna tambem e lugar dela).
        for (int i = Atual.Pecas.Count - 1; i >= 0; i--)
        {
            var peca = Atual.Pecas[i];
            if (Catalogo.EhProtoboard(peca.Tipo) || EstaDoOutroLado(peca)) continue;
            var def = Catalogo.Obter(peca.Tipo);
            if (def is null) continue;
            bool perna = def.Pinos.Any(pino =>
            {
                var q = Geometria.PosicaoPino(peca, def, pino);
                double dx = q.X - p.X, dy = q.Y - p.Y;
                return dx * dx + dy * dy <= RaioPino * RaioPino;
            });
            if (perna || NoCorpoNaTela(peca, def, p)) Por(peca.Id, -1);
        }

        // E as bases, por ultimo.
        for (int i = Atual.Pecas.Count - 1; i >= 0; i--)
        {
            var peca = Atual.Pecas[i];
            if (!Catalogo.EhProtoboard(peca.Tipo)) continue;
            var def = Catalogo.Obter(peca.Tipo);
            if (def is not null && NoCorpo(peca, def, p)) Por(peca.Id, -1);
        }

        // E os fios que passam ali (o de cima primeiro).
        foreach (int fio in FiosPerto(p)) Por(null, fio);
        return pilha;
    }

    /// <summary>Os fios que passam perto do ponto, do de cima (o ultimo da lista) para o de baixo.</summary>
    IEnumerable<int> FiosPerto(Point p, double raio = 7)
    {
        for (int i = Atual.Fios.Count - 1; i >= 0; i--)
        {
            var fio = Atual.Fios[i];
            if (FioDoOutroLado(fio)) continue;
            var a = PosicaoPino(fio.DePeca, fio.DePino);
            var b = PosicaoPino(fio.ParaPeca, fio.ParaPino);
            if (a is null || b is null) continue;
            double desvio = DesvioDe(i);
            bool perto = false;
            for (int k = 0; k <= 48 && !perto; k++)
            {
                var q = NaCurva(fio, a.Value, b.Value, k / 48.0, desvio);
                double dx = q.X - p.X, dy = q.Y - p.Y;
                perto = dx * dx + dy * dy <= raio * raio;
            }
            if (perto) yield return i;
        }
    }

    /// <summary>Onde foi o ultimo clique (no mundo): clicar de novo ali e pedir a camada de baixo.</summary>
    Point? ultimoClique;

    /// <summary>O clique de agora e um "de novo": ao soltar sem arrastar, passa para a proxima camada.</summary>
    bool cliqueDeNovo;

    /// <summary>
    /// O apertar do botao esquerdo, antes do de sempre. Quando ele cai no mesmo lugar
    /// do anterior (sem Ctrl nem Shift), e ali ha mais de uma coisa, o clique e de
    /// camada: a selecao fica como esta (dai da para arrastar o que ja estava
    /// selecionado), e ao soltar sem arrastar seleciona a proxima.
    /// </summary>
    internal bool ApertarDeNovo(Point p)
    {
        bool mesmoLugar = ultimoClique is { } antes && (antes - p).Length <= 5 / Math.Max(Zoom, 0.05);
        ultimoClique = p;
        cliqueDeNovo = false;
        if (!mesmoLugar || Keyboard.Modifiers.HasFlag(ModifierKeys.Control) || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            return false;

        var acerto = Resolver(p);
        // A alca nova no meio do fio selecionado tambem: clicar nela sem arrastar nao
        // faz canto nenhum, e com o fio selecionado ela fica bem onde se clicou.
        bool alcaNova = acerto.Camada == Camada.DobraFio && acerto.DobraNova;
        if (!alcaNova && acerto.Camada is not (Camada.CorpoPeca or Camada.CorpoPlaca or Camada.Fio or Camada.Furo or Camada.PinoPeca))
            return false;
        // O suporte de pilhas selecionado: o clique de novo tira e poe a pilha, como sempre.
        if (acerto.Peca is { } alvo && Baterias.EhSuporte(alvo.Tipo) && selecionadas.Contains(alvo.Id)) return false;

        var pilha = PilhaNoPonto(p);
        if (pilha.Count == 0 || pilha.Count == 1 && origemFio is null) return false;

        cliqueDeNovo = true;
        moveuDeVerdade = false;
        if (selecionadas.Count == 1 && pilha.Any(x => x.Peca == selecionadas.First()) && Atual.Achar(selecionadas.First()) is { } peca)
            PrepararArrasto(peca, p);
        return true;
    }

    /// <summary>O soltar do clique de novo: sem arrasto, a proxima camada.</summary>
    void TerminarCliqueDeNovo()
    {
        if (!cliqueDeNovo) return;
        cliqueDeNovo = false;
        if (moveuDeVerdade || ultimoClique is not { } p) return;
        SelecionarProxima(p);
    }

    /// <summary>
    /// Seleciona o proximo da pilha debaixo do ponto, depois do que esta selecionado
    /// agora (e depois do ultimo, o primeiro de novo).
    /// </summary>
    internal bool SelecionarProxima(Point p)
    {
        var pilha = PilhaNoPonto(p);
        if (pilha.Count == 0) return false;

        int atual = pilha.FindIndex(x => x.Fio >= 0
            ? fiosSelecionados.Count == 1 && FioSelecionado == x.Fio && selecionadas.Count == 0
            : selecionadas.Count == 1 && selecionadas.Contains(x.Peca));
        var proximo = pilha[(atual + 1) % pilha.Count];

        // Desfaz o que o primeiro clique comecou: o fio puxado de um furo.
        if (origemFio is not null) CancelarFio();

        if (proximo.Fio >= 0)
        {
            selecionadas.Clear();
            fiosSelecionados.Clear();
            fiosSelecionados.Add(proximo.Fio);
            RedesenharFios();
            AtualizarRealce();
            SelecaoMudou?.Invoke();
        }
        else
        {
            fiosSelecionados.Clear();
            Selecionar(proximo.Peca, somar: false);
            RedesenharFios();
        }
        return true;
    }

    /// <summary>Para o teste: um clique (apertar e soltar, sem arrastar) no ponto, como o mouse faria.</summary>
    internal void CliqueParaTeste(Point p)
    {
        if (!ApertarDeNovo(p)) Apertar(p);
        Soltar();
    }
}
