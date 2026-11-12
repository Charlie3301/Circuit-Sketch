using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Simulador.App;

/// <summary>
/// Uma linha da paleta: ou um cabecalho, ou uma peca.
///
/// O <see cref="Nivel"/> e so o recuo -- 0 para a categoria, 1 para a
/// familia da placa, e um a mais para as pecas de dentro.
/// </summary>
public sealed record ItemDaPaleta(string Cabecalho, PecaDef Peca, int Nivel);

/// <summary>
/// Paleta de componentes: lista as 50 pecas do catalogo agrupadas por
/// categoria, com busca.
///
/// Fica ABERTA em vez de ser um dialogo modal, e por um motivo: dialogo
/// modal desabilita a janela de tras, e janela desabilitada nao aceita
/// arrastar-e-soltar. Como paleta aberta da para escolher o lugar da
/// peca arrastando ate ele, que era o pedido -- e quem prefere o jeito
/// antigo continua com Enter e duplo clique, que poem no meio da tela.
/// </summary>
public class JanelaPecas : Window
{
    readonly TextBox busca = new();
    readonly ListBox lista = new();
    readonly WrapPanel tiras = new();

    /// <summary>A subaba com tudo dentro, e a que abre por padrao.</summary>
    public const string AbaTudo = "Tudo";

    /// <summary>Tudo o que NAO e placa de desenvolvimento.</summary>
    public const string AbaComponentes = "Componentes";

    string abaAtual = AbaTudo;

    /// <summary>Chamado quando a pessoa escolhe uma peca pelo teclado.</summary>
    public Action<string> AoEscolher { get; set; }

    /// <summary>Formato do arrasto; so esta janela e o editor conhecem.</summary>
    public const string FormatoArrasto = "CircuitSketch.Peca";

    Point inicioDoArrasto;

    public JanelaPecas(Window dono)
    {
        Owner = dono;
        Title = Idioma.T("Adicionar componente");
        Width = 520;
        Height = 600;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        // Tudo pelos recursos do tema, e nao por cor fixa: a paleta
        // ignorava ate o tema claro, e com ela aberta a troca de tema
        // tem que aparecer nela tambem.
        SetResourceReference(BackgroundProperty, Tema.Fundo);

        var raiz = new Grid { Margin = new Thickness(12) };
        raiz.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        raiz.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        raiz.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        raiz.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        tiras.Margin = new Thickness(0, 0, 0, 10);
        Grid.SetRow(tiras, 0);
        raiz.Children.Add(tiras);
        MontarSubabas();

        busca.Padding = new Thickness(8, 6, 8, 6);
        busca.FontSize = 13;
        busca.SetResourceReference(BackgroundProperty, Tema.Campo);
        busca.SetResourceReference(ForegroundProperty, Tema.TextoForte);
        busca.SetResourceReference(BorderBrushProperty, Tema.Borda);
        busca.SetResourceReference(System.Windows.Controls.TextBox.CaretBrushProperty, Tema.TextoForte);
        busca.Margin = new Thickness(0, 0, 0, 10);
        busca.TextChanged += (_, _) => Preencher(busca.Text);
        Grid.SetRow(busca, 1);
        raiz.Children.Add(busca);

        lista.SetResourceReference(BackgroundProperty, Tema.Painel);
        lista.SetResourceReference(ForegroundProperty, Tema.Texto);
        lista.SetResourceReference(BorderBrushProperty, Tema.Borda);
        lista.MouseDoubleClick += (_, _) => Confirmar();

        // Arrastar: guarda onde o dedo desceu e so comeca depois de andar
        // o bastante. Sem essa folga, um clique tremido vira arrasto e a
        // pessoa nao consegue mais so selecionar da lista.
        lista.PreviewMouseLeftButtonDown += (_, e) => inicioDoArrasto = e.GetPosition(null);
        lista.PreviewMouseMove += ComecarArrasto;
        Grid.SetRow(lista, 2);
        raiz.Children.Add(lista);

        var dica = new TextBlock
        {
            Text = Idioma.T("Arraste para a tela  ·  Enter ou duplo clique põe no meio  ·  Ctrl+Tab troca de aba  ·  Esc fecha"),
            FontSize = 11,
            Margin = new Thickness(2, 8, 0, 0),
            TextWrapping = TextWrapping.Wrap
        };
        dica.SetResourceReference(TextBlock.ForegroundProperty, Tema.TextoFraco);
        Grid.SetRow(dica, 3);
        raiz.Children.Add(dica);

        Content = raiz;
        Preencher("");

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Tab && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                Vizinha(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? -1 : 1);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape) { Close(); e.Handled = true; }
            else if (e.Key == Key.Enter) { Confirmar(); e.Handled = true; }
            else if (e.Key is Key.Down or Key.Up && busca.IsFocused)
            {
                lista.Focus();
                if (lista.SelectedIndex < 0 && lista.Items.Count > 0) lista.SelectedIndex = 0;
            }
        };

        Loaded += (_, _) => busca.Focus();
    }

    /// <summary>
    /// As subabas, na ordem: tudo, uma por familia de placa, e o resto.
    ///
    /// Sai do catalogo e nao de uma lista escrita aqui -- placa nova
    /// aparece na aba certa sem ninguem mexer nesta janela.
    /// </summary>
    public static List<string> Subabas()
        => [AbaTudo, .. Catalogo.Familias(), AbaComponentes];

    /// <summary>
    /// O conteudo da paleta, sem tocar em WPF nenhum.
    ///
    /// Fica separado da janela de proposito. Uma janela aberta num
    /// autoteste prende o processo -- ele so encerra quando a ultima
    /// some --, entao a parte que de fato pode quebrar, que e o
    /// agrupamento, precisa dar para conferir sem mostrar nada.
    /// </summary>
    public static List<ItemDaPaleta> Montar(string subaba, string filtro)
    {
        subaba ??= AbaTudo;
        filtro = (filtro ?? "").Trim().ToLowerInvariant();

        bool Passa(PecaDef peca) => filtro.Length == 0
                                    || peca.Nome.ToLowerInvariant().Contains(filtro)
                                    || peca.Id.Contains(filtro);

        var saida = new List<ItemDaPaleta>();

        // ---- subaba de familia: so as placas daquela familia ----
        //
        // Este e o pedido da Alpha 18 ao pe da letra: "Arduino com Mega,
        // Uno, etc.". Quem esta atras de uma placa nao quer rolar por
        // quarenta sensores ate achar.
        if (subaba != AbaTudo && subaba != AbaComponentes)
        {
            var daFamilia = Catalogo.Todas
                .Where(peca => Catalogo.Familia(peca.Id) == subaba && Passa(peca))
                .OrderBy(peca => peca.Nome)
                .ToList();

            if (daFamilia.Count == 0) return saida;

            saida.Add(new ItemDaPaleta(Catalogo.NomeDaFamilia(subaba).ToUpperInvariant(), null, 0));
            foreach (var peca in daFamilia) saida.Add(new ItemDaPaleta(null, peca, 1));
            return saida;
        }

        var pecas = Catalogo.Todas.Where(Passa);

        // "Componentes" e o complemento das outras abas: o que nao tem
        // familia e o que nao e placa.
        if (subaba == AbaComponentes)
            pecas = pecas.Where(peca => Catalogo.Familia(peca.Id) is null);

        var ordemDaFamilia = Catalogo.Familias().ToList();

        foreach (var categoria in pecas.GroupBy(peca => peca.Categoria)
                                       .OrderBy(g => OrdemCategoria(g.Key)))
        {
            saida.Add(new ItemDaPaleta(NomeDaCategoria(categoria.Key).ToUpperInvariant(), null, 0));

            // Fora de "Placas" a familia e null para todo mundo, entao
            // sai um grupo so, sem subcabecalho e sem recuo a mais. O
            // mesmo laco serve para os dois casos.
            foreach (var familia in categoria
                         .GroupBy(peca => Catalogo.Familia(peca.Id))
                         .OrderBy(g => g.Key is null ? -1 : ordemDaFamilia.IndexOf(g.Key)))
            {
                if (familia.Key is not null)
                    saida.Add(new ItemDaPaleta(Catalogo.NomeDaFamilia(familia.Key).ToUpperInvariant(), null, 1));

                foreach (var peca in familia.OrderBy(peca => peca.Nome))
                    saida.Add(new ItemDaPaleta(null, peca, familia.Key is null ? 1 : 2));
            }
        }

        return saida;
    }

    // ---------------- a tira de subabas ----------------
    void MontarSubabas()
    {
        tiras.Children.Clear();
        foreach (string nome in Subabas()) tiras.Children.Add(Chip(nome));
    }

    Border Chip(string nome)
    {
        bool ativa = nome == abaAtual;

        var texto = new TextBlock
        {
            Text = NomeDaAba(nome),
            FontSize = 11,
            FontWeight = ativa ? FontWeights.SemiBold : FontWeights.Normal,
        };
        texto.SetResourceReference(TextBlock.ForegroundProperty,
                                   ativa ? Tema.TextoNoDestaque : Tema.TextoFraco);

        var chip = new Border
        {
            Margin = new Thickness(0, 0, 6, 6),
            Padding = new Thickness(10, 4, 10, 5),
            CornerRadius = new CornerRadius(3),
            Cursor = Cursors.Hand,
            BorderThickness = new Thickness(1),
            Child = texto,
        };
        chip.SetResourceReference(Border.BackgroundProperty, ativa ? Tema.Destaque : Tema.Campo);
        chip.SetResourceReference(Border.BorderBrushProperty, ativa ? Tema.Destaque : Tema.Borda);

        chip.MouseLeftButtonDown += (_, _) => Trocar(nome);
        return chip;
    }

    void Trocar(string nome)
    {
        if (nome == abaAtual) return;

        abaAtual = nome;
        MontarSubabas();
        Preencher(busca.Text);
        busca.Focus();
    }

    /// <summary>A subaba ao lado, dando a volta nas pontas.</summary>
    void Vizinha(int passo)
    {
        var abas = Subabas();
        int i = abas.IndexOf(abaAtual);
        if (i < 0) i = 0;

        Trocar(abas[((i + passo) % abas.Count + abas.Count) % abas.Count]);
    }

    void Preencher(string filtro)
    {
        lista.Items.Clear();

        var itens = Montar(abaAtual, filtro);

        // A busca vale DENTRO da aba escolhida. Procurar no catalogo
        // inteiro e pular de aba sozinho seria mais esperto e mais
        // confuso: a aba deixaria de querer dizer o que esta escrito
        // nela. Quando nao acha nada, a mensagem diz onde procurar.
        if (itens.Count == 0)
        {
            lista.Items.Add(new ListBoxItem
            {
                IsHitTestVisible = false,
                Padding = new Thickness(6, 10, 6, 6),
                Content = new TextBlock
                {
                    Text = abaAtual == AbaTudo
                        ? Idioma.T("Nada com esse nome.")
                        : Idioma.T("Nada com esse nome em \u201c{0}\u201d. Tente na aba Tudo.", NomeDaAba(abaAtual)),
                    Foreground = Tema.Pincel(Tema.TextoFraco),
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap,
                },
            });
            return;
        }

        // As categorias recolhidas so mostram o cabecalho (com a seta e
        // quantas pecas tem). Buscando, tudo aparece: quem digitou quer achar.
        bool buscando = !string.IsNullOrWhiteSpace(filtro);
        string categoria = null;
        for (int k = 0; k < itens.Count; k++)
        {
            var item = itens[k];
            if (item.Peca is null && item.Nivel == 0)
            {
                categoria = item.Cabecalho;
                int quantas = 0;
                for (int j = k + 1; j < itens.Count && !(itens[j].Peca is null && itens[j].Nivel == 0); j++)
                    if (itens[j].Peca is not null) quantas++;
                lista.Items.Add(CabecalhoDeCategoria(item, quantas, aberta: buscando || !Recolhidas.Contains(categoria)));
                continue;
            }
            if (!buscando && categoria is not null && Recolhidas.Contains(categoria)) continue;
            lista.Items.Add(item.Peca is null ? Cabecalho(item) : Linha(item.Peca, item.Nivel));
        }

        // Ja deixa o primeiro item util escolhido, para o Enter funcionar.
        for (int i = 0; i < lista.Items.Count; i++)
        {
            if (lista.Items[i] is ListBoxItem it && it.Tag is not null)
            {
                lista.SelectedIndex = i;
                break;
            }
        }
    }

    /// <summary>As categorias recolhidas pela seta (valem enquanto o programa esta aberto).</summary>
    static readonly HashSet<string> Recolhidas = [];

    /// <summary>
    /// O cabecalho da categoria, com a seta: um clique recolhe as pecas dela
    /// (sao mais de cem, e a lista virava uma rolagem sem fim); outro, abre.
    /// </summary>
    ListBoxItem CabecalhoDeCategoria(ItemDaPaleta item, int quantas, bool aberta)
    {
        var cabecalho = Cabecalho(item);
        cabecalho.IsHitTestVisible = true;
        cabecalho.Cursor = Cursors.Hand;
        cabecalho.Focusable = false;
        cabecalho.Content = $"{(aberta ? "\u25BE" : "\u25B8")}  {item.Cabecalho}" + (aberta ? "" : $"  ({quantas})");
        cabecalho.ToolTip = aberta ? Idioma.T("Recolher esta categoria") : Idioma.T("Mostrar as pecas desta categoria");
        cabecalho.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (!Recolhidas.Remove(item.Cabecalho)) Recolhidas.Add(item.Cabecalho);
            e.Handled = true;
            Preencher(busca.Text);
        };
        return cabecalho;
    }

    /// <summary>Para o teste: recolhe (ou abre) uma categoria pelo nome do cabecalho.</summary>
    internal void TesteAlternarCategoria(string cabecalho)
    {
        if (!Recolhidas.Remove(cabecalho)) Recolhidas.Add(cabecalho);
        Preencher(busca.Text);
    }

    /// <summary>Para o teste: as linhas que a lista mostra agora.</summary>
    internal int TesteLinhasNaLista => lista.Items.Count;

    /// <summary>
    /// O cabecalho: a categoria em destaque, a familia um tom abaixo.
    ///
    /// Sao dois pesos porque dizem coisas diferentes -- "PLACAS" diz
    /// para que serve, "ARDUINO" diz de quem e. No mesmo peso a lista
    /// vira uma parede de maiusculas e nenhum dos dois se le.
    /// </summary>
    static ListBoxItem Cabecalho(ItemDaPaleta item)
    {
        var cabecalho = new ListBoxItem
        {
            Content = item.Cabecalho,
            IsHitTestVisible = false,
            FontSize = 10,
            FontWeight = item.Nivel == 0 ? FontWeights.Bold : FontWeights.SemiBold,
            Padding = new Thickness(4 + item.Nivel * 14, item.Nivel == 0 ? 10 : 6, 4, 2),
        };
        cabecalho.SetResourceReference(ForegroundProperty,
                                       item.Nivel == 0 ? Tema.Destaque : Tema.TextoFraco);
        return cabecalho;
    }

    /// <summary>
    /// Cada peca com seu proprio desenho em miniatura. Sem preview o
    /// aluno tem que adivinhar pelo nome o que vai aparecer na tela.
    /// </summary>
    static ListBoxItem Linha(PecaDef peca, int nivel)
    {
        var linha = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(nivel * 14 - 14, 0, 0, 0),
        };

        var moldura = new Border
        {
            Width = 44,
            Height = 34,
            Margin = new Thickness(6, 0, 10, 0),
            CornerRadius = new CornerRadius(3)
        };
        moldura.SetResourceReference(Border.BackgroundProperty, Tema.Campo);

        // A miniatura vem depois, sob demanda: so a linha que aparece na tela
        // a pede, e em segundo plano. Montar as ~200 de uma vez (cada uma lendo
        // o SVG da peca) travava a janela por segundos no "+".
        string idDaPeca = peca.Id;
        if (miniaturas.TryGetValue(idDaPeca, out var pronta)) PorMiniatura(moldura, pronta, idDaPeca);
        else
            moldura.Loaded += (_, _) =>
            {
                if (moldura.Child is not null) return;
                moldura.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, () =>
                {
                    if (moldura.Child is null) PorMiniatura(moldura, Miniatura(idDaPeca), idDaPeca);
                });
            };
        linha.Children.Add(moldura);

        var textos = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var nomeDaPeca = new TextBlock { Text = Idioma.NaLinha(peca.Nome), FontSize = 12 };
        nomeDaPeca.SetResourceReference(TextBlock.ForegroundProperty, Tema.Texto);
        textos.Children.Add(nomeDaPeca);

        var pinos = new TextBlock { Text = Idioma.T("{0} pinos", peca.Pinos.Count), FontSize = 10 };
        pinos.SetResourceReference(TextBlock.ForegroundProperty, Tema.TextoFraco);
        textos.Children.Add(pinos);
        linha.Children.Add(textos);

        return new ListBoxItem { Content = linha, Tag = peca.Id, Padding = new Thickness(2) };
    }

    /// <summary>As miniaturas prontas (um bitmap pequeno por peca; null: a peca nao tem desenho).</summary>
    static readonly Dictionary<string, ImageSource> miniaturas = [];

    /// <summary>Quantas miniaturas ja foram feitas (para o teste).</summary>
    internal static int MiniaturasProntas => miniaturas.Count;

    /// <summary>
    /// A miniatura da peca: o desenho dela num bitmap de 38x28 (o dobro de pixels,
    /// para a tela de alta densidade). O desenho vetorial inteiro de uma placa
    /// tem milhares de caminhos -- redesenha-lo a cada rolagem pesava tambem.
    /// </summary>
    internal static ImageSource Miniatura(string id)
    {
        if (miniaturas.TryGetValue(id, out var pronta)) return pronta;
        ImageSource feita = null;
        var arte = Pecas.Talvez(id) ?? Pecas.Talvez($"{id}-corpo");
        if (arte is not null && arte.Width > 0 && arte.Height > 0)
        {
            const double Largura = 38, Altura = 28;
            double escala = Math.Min(Largura / arte.Width, Altura / arte.Height);
            double w = arte.Width * escala, h = arte.Height * escala;
            var desenho = new DrawingVisual();
            using (var ctx = desenho.RenderOpen())
                ctx.DrawImage(arte, new Rect((Largura - w) / 2, (Altura - h) / 2, w, h));
            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(
                (int)(Largura * 2), (int)(Altura * 2), 192, 192, PixelFormats.Pbgra32);
            bitmap.Render(desenho);
            bitmap.Freeze();
            feita = bitmap;
        }
        miniaturas[id] = feita;
        return feita;
    }

    static void PorMiniatura(Border moldura, ImageSource arte, string id)
    {
        if (arte is not null)
        {
            moldura.Child = new Image { Source = arte, Stretch = Stretch.Uniform, Margin = new Thickness(3) };
            return;
        }
        // Protoboard nao tem SVG: e desenhada em tempo de execucao.
        var simbolo = new TextBlock
        {
            Text = "▤",
            FontSize = 17,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Tag = id,
        };
        simbolo.SetResourceReference(TextBlock.ForegroundProperty, Tema.TextoFraco);
        moldura.Child = simbolo;
    }

    /// <summary>A aba como aparece na tira: as chaves "Tudo" e "Componentes" se traduzem; a familia, so o "Outras".</summary>
    static string NomeDaAba(string aba)
        => aba == AbaTudo ? Idioma.T("Tudo") : aba == AbaComponentes ? Idioma.T("Componentes") : Catalogo.NomeDaFamilia(aba);

    /// <summary>A categoria como aparece no cabecalho. A do catalogo e chave (sem acento); esta e a da tela.</summary>
    static string NomeDaCategoria(string categoria) => categoria switch
    {
        "Base" => Idioma.T("Base"), "Placas" => Idioma.T("Placas"), "Saida" => Idioma.T("Saída"),
        "Entrada" => Idioma.T("Entrada"), "Sensores" => Idioma.T("Sensores"), "Atuadores" => Idioma.T("Atuadores"),
        "Motores" => Idioma.T("Motores"), "Modulos" => Idioma.T("Módulos"), "Rede" => Idioma.T("Rede"),
        "Alimentacao" => Idioma.T("Alimentação"), "Logica" => Idioma.T("Lógica"), "Passivos" => Idioma.T("Passivos"),
        "Semicondutores" => Idioma.T("Semicondutores"), "Chips" => Idioma.T("Chips"), "Anotacao" => Idioma.T("Anotação"),
        "Outros" => Idioma.T("Outros"),
        _ => Idioma.T(categoria),
    };

    static int OrdemCategoria(string c) => c switch
    {
        "Base" => 0, "Placas" => 1, "Saida" => 2, "Entrada" => 3,
        "Sensores" => 4, "Atuadores" => 5, "Motores" => 6, "Modulos" => 7, "Rede" => 8,
        "Alimentacao" => 9, "Logica" => 10, "Passivos" => 11,
        "Chips" => 12, "Anotacao" => 13, _ => 14
    };

    /// <summary>
    /// Comeca o arrasto quando o ponteiro anda o bastante com o botao
    /// apertado. O que viaja e so o id da peca -- quem monta o objeto e
    /// o editor, no ponto onde a peca foi solta.
    /// </summary>
    void ComecarArrasto(object remetente, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;

        var agora = e.GetPosition(null);
        if (Math.Abs(agora.X - inicioDoArrasto.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(agora.Y - inicioDoArrasto.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        // O item sob o ponteiro, e nao o selecionado: arrastar um item
        // sem antes clicar nele e o que a pessoa espera.
        var alvo = ItemSob(e.OriginalSource as DependencyObject);
        if (alvo?.Tag is not string id) return;

        var dados = new DataObject(FormatoArrasto, id);
        DragDrop.DoDragDrop(alvo, dados, DragDropEffects.Copy);
    }

    static ListBoxItem ItemSob(DependencyObject no)
    {
        while (no is not null and not ListBoxItem)
            no = System.Windows.Media.VisualTreeHelper.GetParent(no);
        return no as ListBoxItem;
    }

    /// <summary>Para o passeio: troca de subaba como um clique trocaria.</summary>
    internal void TesteTrocar(string aba) => Trocar(aba);

    /// <summary>Para o passeio: escreve na busca.</summary>
    internal void TesteBuscar(string texto) => busca.Text = texto;

    void Confirmar()
    {
        if (lista.SelectedItem is ListBoxItem it && it.Tag is string id)
            AoEscolher?.Invoke(id);
    }
}
