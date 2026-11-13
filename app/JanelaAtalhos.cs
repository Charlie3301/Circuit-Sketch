using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Simulador.App;

/// <summary>
/// Os atalhos do teclado, todos trocaveis: por grupo, com busca pelo nome ou
/// pela tecla, um "sem atalho" por linha, e a troca que ja esta em uso
/// oferece passar a tecla de uma acao para a outra.
/// </summary>
public class JanelaAtalhos : Window
{
    readonly StackPanel lista = new();
    readonly TextBox busca;

    public JanelaAtalhos(Window dono)
    {
        try { Owner = dono; } catch (InvalidOperationException) { }

        Title = Idioma.T("Atalhos do teclado");
        Width = 600;
        Height = 680;
        MinWidth = 480;
        MinHeight = 360;
        ResizeMode = ResizeMode.CanResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Tema.Pincel(Tema.Fundo);

        var raiz = new DockPanel { Margin = new Thickness(18, 14, 18, 16) };

        var topo = new StackPanel();
        topo.Children.Add(new TextBlock
        {
            Text = Idioma.T("Clique em Trocar e aperte a tecla. Rodando um circuito com teclas vinculadas às peças, os atalhos de uma tecla só ficam calados."),
            Foreground = Tema.Pincel(Tema.TextoFraco),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10),
        });

        busca = new TextBox
        {
            Style = (Style)FindResource("CampoEscuro"),
            FontSize = 13,
            Padding = new Thickness(6, 4, 6, 4),
            ToolTip = Idioma.T("Procurar pelo nome da ação ou pela tecla"),
            Margin = new Thickness(0, 0, 0, 12),
        };
        System.Windows.Automation.AutomationProperties.SetName(busca, Idioma.T("Procurar atalho"));
        var dica = new TextBlock
        {
            Text = Idioma.T("Procurar (nome ou tecla)…"),
            Foreground = Tema.Pincel(Tema.TextoFraco),
            FontSize = 13,
            IsHitTestVisible = false,
            Margin = new Thickness(10, 5, 0, 0),
        };
        busca.TextChanged += (_, _) =>
        {
            dica.Visibility = busca.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            Montar();
        };
        topo.Children.Add(new Grid { Children = { busca, dica } });
        DockPanel.SetDock(topo, Dock.Top);
        raiz.Children.Add(topo);

        var rodape = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0),
        };
        var voltar = new Button
        {
            Content = Idioma.T("Voltar ao padrão"),
            Style = (Style)FindResource("BotaoPequeno"),
            Margin = new Thickness(0, 0, 8, 0),
        };
        voltar.Click += (_, _) =>
        {
            foreach (var acao in AtalhosDoPrograma.Todas) AtalhosDoPrograma.Esquecer(acao.Id);
            Montar();
        };
        rodape.Children.Add(voltar);

        var fechar = new Button
        {
            Content = Idioma.T("Fechar"),
            Style = (Style)FindResource("BotaoPequeno"),
        };
        fechar.Click += (_, _) => Close();
        rodape.Children.Add(fechar);
        DockPanel.SetDock(rodape, Dock.Bottom);
        raiz.Children.Add(rodape);

        raiz.Children.Add(new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = lista,
        });

        Content = raiz;
        Montar();
    }

    /// <summary>A acao esperando a tecla nova, se alguma.</summary>
    string trocando;

    void Montar()
    {
        lista.Children.Clear();
        string filtro = busca?.Text.Trim() ?? "";
        foreach (string categoria in AtalhosDoPrograma.Categorias)
        {
            var acoes = AtalhosDoPrograma.Todas
                .Where(a => a.Categoria == categoria)
                .Where(a => filtro.Length == 0
                            || a.Nome.Contains(filtro, StringComparison.CurrentCultureIgnoreCase)
                            || AtalhosDoPrograma.Rotulo(a.Id).Contains(filtro, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (acoes.Count == 0) continue;

            lista.Children.Add(new TextBlock
            {
                Text = categoria.ToUpper(System.Globalization.CultureInfo.CurrentCulture),
                Foreground = Tema.Pincel(Tema.TextoFraco),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, lista.Children.Count == 0 ? 0 : 12, 0, 6),
            });
            foreach (var acao in acoes) lista.Children.Add(Linha(acao));
        }
        if (lista.Children.Count == 0)
            lista.Children.Add(new TextBlock
            {
                Text = Idioma.T("Nenhum atalho com esse nome ou tecla."),
                Foreground = Tema.Pincel(Tema.TextoFraco),
                FontSize = 12,
            });
    }

    UIElement Linha(AcaoDoPrograma acao)
    {
        var linha = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };

        var limpar = new Button
        {
            Content = "✕",
            Style = (Style)FindResource("BotaoPequeno"),
            Width = 30,
            Margin = new Thickness(6, 0, 0, 0),
            ToolTip = Idioma.T("Deixar sem atalho"),
            IsEnabled = AtalhosDoPrograma.Atalho(acao.Id).Tecla != Key.None,
        };
        System.Windows.Automation.AutomationProperties.SetName(limpar, Idioma.T("Deixar sem atalho"));
        DockPanel.SetDock(limpar, Dock.Right);
        linha.Children.Add(limpar);

        var trocar = new Button
        {
            Content = trocando == acao.Id ? Idioma.T("aperte…") : Idioma.T("Trocar"),
            Style = (Style)FindResource("BotaoPequeno"),
            Width = 80,
        };
        DockPanel.SetDock(trocar, Dock.Right);
        linha.Children.Add(trocar);

        var (padrao, padraoMods) = (acao.Padrao, acao.Modificador);
        var (agora, agoraMods) = AtalhosDoPrograma.Atalho(acao.Id);
        bool mudou = agora != padrao || agoraMods != padraoMods;
        var atual = new TextBlock
        {
            Text = trocando == acao.Id ? "…" : AtalhosDoPrograma.Rotulo(acao.Id),
            Foreground = Tema.Pincel(Tema.Destaque),
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Width = 130,
            TextAlignment = TextAlignment.Right,
            Margin = new Thickness(0, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = mudou ? Idioma.T("O de fábrica: {0}", AtalhosDoPrograma.Rotulo(padrao, padraoMods)) : null,
        };
        DockPanel.SetDock(atual, Dock.Right);
        linha.Children.Add(atual);

        // O nome entra por ULTIMO, e e ele que ocupa a sobra. Entrando
        // primeiro, quem ficava com a sobra era a tecla -- e ela se
        // centralizava no espaco entre o nome e o botao, entao cada
        // linha punha a tecla numa altura diferente, conforme o tamanho
        // do nome: "F5" la na direita, "Ctrl+N" quase no meio.
        linha.Children.Add(new TextBlock
        {
            Text = acao.Nome + (mudou ? "  •" : ""),
            Foreground = Tema.Pincel(Tema.Texto),
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
        });

        limpar.Click += (_, _) =>
        {
            AtalhosDoPrograma.Limpar(acao.Id);
            Montar();
        };

        trocar.Click += (_, _) =>
        {
            trocando = acao.Id;
            trocar.Content = Idioma.T("aperte…");
            atual.Text = "…";

            void Pegar(object _, KeyEventArgs ev)
            {
                var tecla = ev.Key == Key.System ? ev.SystemKey : ev.Key;
                ev.Handled = true;

                if (tecla is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
                    or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin) return;

                PreviewKeyDown -= Pegar;
                trocando = null;

                if (tecla == Key.Escape)
                {
                    Montar();
                    return;
                }

                tecla = AtalhosDoPrograma.Normalizar(tecla);
                var mods = Keyboard.Modifiers & ~ModifierKeys.Windows;
                string outra = AtalhosDoPrograma.QuemUsa(acao.Id, tecla, mods);
                if (outra is not null)
                {
                    // A tecla ja e de outra acao: passa para esta, e a outra fica sem.
                    var resposta = MessageBox.Show(this,
                        Idioma.T("{0} já é de \"{1}\". Passar para \"{2}\"? \"{1}\" fica sem atalho.",
                                 AtalhosDoPrograma.Rotulo(tecla, mods), AtalhosDoPrograma.Acao(outra).Nome, acao.Nome),
                        Idioma.T("Atalho ocupado"), MessageBoxButton.YesNo, MessageBoxImage.Question);
                    if (resposta != MessageBoxResult.Yes)
                    {
                        Montar();
                        return;
                    }
                    AtalhosDoPrograma.Limpar(outra);
                }

                AtalhosDoPrograma.Definir(acao.Id, tecla, mods);
                Montar();
            }

            PreviewKeyDown += Pegar;
        };

        return linha;
    }

    /// <summary>Para o teste: as linhas que a busca deixou (nomes das acoes).</summary>
    internal int LinhasParaTeste => lista.Children.OfType<DockPanel>().Count();

    internal void BuscarParaTeste(string texto) => busca.Text = texto;
}
