using System.Windows;
using System.Windows.Controls;

namespace Simulador.App;

/// <summary>
/// As opcoes da peca selecionada, uma embaixo da outra:
///
///     Nome   [ LED1       ]
///     X (mm) [ 12,5 ]
///     Y (mm) [ 40   ]
///     ─────────────────────
///     Cor    [ Vermelho ▾ ]
///
/// Ate aqui a aba era uma fila deitada, e a peca com muita opcao passava da
/// largura da tela. Quem monta a aba continua so pondo os controles na
/// ordem, como antes: um texto seguido de um controle e o rotulo da linha;
/// o que vem depois, ate o proximo rotulo, fica na mesma linha (o "Ω", a
/// leitura ao lado do deslizante) e quebra se nao couber; a barrinha em pe
/// (Border de largura 1) vira o traco que separa os grupos.
/// </summary>
public sealed class ColunaDeOpcoes : Panel
{
    const double EntreLinhas = 6;
    const double EntreRotuloECampo = 8;
    const double RotuloMaximo = 110;

    sealed class Linha
    {
        public UIElement Rotulo;
        public readonly List<UIElement> Itens = [];
        public bool Separador;
    }

    readonly HashSet<UIElement> separadores = [];

    bool EhSeparador(UIElement e) => separadores.Contains(e);

    List<Linha> Linhas()
    {
        var filhos = InternalChildren.Cast<UIElement>().Where(e => e.Visibility != Visibility.Collapsed).ToList();
        bool EhRotulo(int i) => filhos[i] is TextBlock && i + 1 < filhos.Count
                                && filhos[i + 1] is not TextBlock && !EhSeparador(filhos[i + 1]);

        var linhas = new List<Linha>();
        Linha atual = null;
        for (int i = 0; i < filhos.Count; i++)
        {
            var e = filhos[i];
            if (EhSeparador(e))
            {
                // Sem traco no comeco, no fim ou dois seguidos.
                if (linhas.Count > 0 && !linhas[^1].Separador) linhas.Add(new Linha { Separador = true, Rotulo = e });
                atual = null;
                continue;
            }
            if (EhRotulo(i))
            {
                atual = new Linha { Rotulo = e };
                linhas.Add(atual);
                continue;
            }
            if (atual is null)
            {
                atual = new Linha();
                linhas.Add(atual);
            }
            atual.Itens.Add(e);
        }
        if (linhas.Count > 0 && linhas[^1].Separador) linhas.RemoveAt(linhas.Count - 1);
        return linhas;
    }

    protected override void OnVisualChildrenChanged(DependencyObject adicionado, DependencyObject removido)
    {
        base.OnVisualChildrenChanged(adicionado, removido);
        if (removido is UIElement saiu) separadores.Remove(saiu);
        // A barrinha em pe vira o traco deitado.
        if (adicionado is Border { Child: null } b && b.Width == 1)
        {
            separadores.Add(b);
            b.Width = double.NaN;
            b.Height = 1;
            b.Margin = new Thickness(0, 3, 0, 3);
            b.SnapsToDevicePixels = true;
        }
        // O texto que nao cabe quebra a linha, em vez de sumir cortado.
        else if (adicionado is TextBlock t && t.TextWrapping == TextWrapping.NoWrap)
            t.TextWrapping = TextWrapping.Wrap;
    }

    double larguraDoRotulo;
    List<Linha> medidas;

    protected override Size MeasureOverride(Size disponivel)
    {
        var infinito = new Size(double.PositiveInfinity, double.PositiveInfinity);
        medidas = Linhas();

        larguraDoRotulo = 0;
        foreach (var l in medidas)
        {
            if (l.Separador || l.Rotulo is null) continue;
            l.Rotulo.Measure(new Size(RotuloMaximo, double.PositiveInfinity));
            larguraDoRotulo = Math.Max(larguraDoRotulo, Math.Min(RotuloMaximo, l.Rotulo.DesiredSize.Width));
        }

        // A largura: a da linha mais larga, sem passar do que cabe.
        double maxima = double.IsInfinity(disponivel.Width) ? 340 : disponivel.Width;
        double larga = 0;
        foreach (var l in medidas)
        {
            if (l.Separador) continue;
            double soma = l.Rotulo is null ? 0 : larguraDoRotulo + EntreRotuloECampo;
            foreach (var e in l.Itens) { e.Measure(infinito); soma += e.DesiredSize.Width; }
            larga = Math.Max(larga, soma);
        }
        double largura = Math.Min(larga, maxima);
        foreach (var l in medidas)
            if (l.Separador) l.Rotulo.Measure(new Size(largura, double.PositiveInfinity));

        double altura = 0;
        foreach (var l in medidas)
        {
            if (altura > 0) altura += EntreLinhas;
            altura += AlturaDa(l, largura, arrumar: false, 0);
        }
        return new Size(largura, altura);
    }

    protected override Size ArrangeOverride(Size final)
    {
        medidas ??= Linhas();
        double y = 0;
        foreach (var l in medidas)
        {
            if (y > 0) y += EntreLinhas;
            y += AlturaDa(l, final.Width, arrumar: true, y);
        }
        // Quem ficou fora (escondido) nao aparece num canto qualquer.
        var usados = medidas.SelectMany(l => l.Rotulo is null ? l.Itens : l.Itens.Prepend(l.Rotulo)).ToHashSet();
        foreach (UIElement e in InternalChildren)
            if (!usados.Contains(e)) e.Arrange(new Rect(0, 0, 0, 0));
        return final;
    }

    /// <summary>Mede (ou poe no lugar) uma linha, devolvendo a altura dela.</summary>
    double AlturaDa(Linha l, double largura, bool arrumar, double y)
    {
        if (l.Separador)
        {
            if (arrumar) l.Rotulo.Arrange(new Rect(0, y, largura, l.Rotulo.DesiredSize.Height));
            return l.Rotulo.DesiredSize.Height;
        }

        double inicio = l.Rotulo is null ? 0 : larguraDoRotulo + EntreRotuloECampo;
        double espaco = Math.Max(20, largura - inicio);

        // Os itens em fila, quebrando quando nao cabem.
        var filas = new List<(List<UIElement> Itens, double Altura)>();
        var fila = new List<UIElement>();
        double x = 0, alturaDaFila = 0;
        foreach (var e in l.Itens)
        {
            double w = Math.Min(e.DesiredSize.Width, espaco);
            if (fila.Count > 0 && x + w > espaco + 0.5)
            {
                filas.Add((fila, alturaDaFila));
                fila = [];
                x = 0;
                alturaDaFila = 0;
            }
            // O texto mais largo que o espaco quebra em linhas.
            if (e is TextBlock && e.DesiredSize.Width > espaco) e.Measure(new Size(espaco, double.PositiveInfinity));
            fila.Add(e);
            x += w;
            alturaDaFila = Math.Max(alturaDaFila, e.DesiredSize.Height);
        }
        if (fila.Count > 0) filas.Add((fila, alturaDaFila));

        double alturaDosItens = filas.Sum(f => f.Altura) + Math.Max(0, filas.Count - 1) * 4;
        double alturaDoRotulo = l.Rotulo?.DesiredSize.Height ?? 0;
        double altura = Math.Max(alturaDosItens, filas.Count > 0 ? 0 : alturaDoRotulo);
        if (filas.Count > 0) altura = Math.Max(altura, Math.Min(alturaDoRotulo, filas[0].Altura));
        altura = Math.Max(altura, alturaDoRotulo);

        if (!arrumar) return altura;

        if (l.Rotulo is not null)
        {
            // O rotulo na altura do meio da primeira fila.
            double primeira = filas.Count > 0 ? filas[0].Altura : alturaDoRotulo;
            l.Rotulo.Arrange(new Rect(0, y + Math.Max(0, (primeira - alturaDoRotulo) / 2),
                                      Math.Min(larguraDoRotulo, l.Rotulo.DesiredSize.Width), alturaDoRotulo));
        }
        double fy = y;
        foreach (var (itens, alturaF) in filas)
        {
            double fx = inicio;
            foreach (var e in itens)
            {
                double w = Math.Min(e.DesiredSize.Width, largura - fx);
                double h = e.DesiredSize.Height;
                e.Arrange(new Rect(fx, fy + (alturaF - h) / 2, Math.Max(0, w), h));
                fx += w;
            }
            fy += alturaF + 4;
        }
        return altura;
    }
}
