using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Simulador.App;

public class JanelaAtalhos : Window
{
    public JanelaAtalhos(Window dono)
    {
        try { Owner = dono; } catch (InvalidOperationException) { }

        Title = "Atalhos do teclado";
        Width = 520;
        Height = 560;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Tema.Pincel(Tema.Fundo);

        var coluna = new StackPanel { Margin = new Thickness(18, 14, 18, 16) };

        coluna.Children.Add(new TextBlock
        {
            Text = "Clique em Trocar e aperte a tecla. Rodando um circuito com "
                 + "teclas vinculadas às peças, os atalhos de uma tecla só ficam calados.",
            Foreground = Tema.Pincel(Tema.TextoFraco),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 14),
        });

        foreach (var acao in AtalhosDoPrograma.Todas)
            coluna.Children.Add(Linha(acao));

        var rodape = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0),
        };

        var voltar = new Button
        {
            Content = "Voltar ao padrão",
            Style = (Style)FindResource("BotaoPequeno"),
            Margin = new Thickness(0, 0, 8, 0),
        };
        voltar.Click += (_, _) =>
        {
            foreach (var acao in AtalhosDoPrograma.Todas) AtalhosDoPrograma.Esquecer(acao.Id);
            foreach (var linha in atuais) linha.Value.Text = AtalhosDoPrograma.Rotulo(linha.Key);
        };
        rodape.Children.Add(voltar);

        var fechar = new Button
        {
            Content = "Fechar",
            Style = (Style)FindResource("BotaoPequeno"),
        };
        fechar.Click += (_, _) => Close();
        rodape.Children.Add(fechar);

        coluna.Children.Add(rodape);

        Content = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = coluna,
        };
    }

    readonly Dictionary<string, TextBlock> atuais = [];

    UIElement Linha(AcaoDoPrograma acao)
    {
        var linha = new DockPanel { Margin = new Thickness(0, 0, 0, 7) };

        var nome = new TextBlock
        {
            Text = acao.Nome,
            Foreground = Tema.Pincel(Tema.Texto),
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var trocar = new Button
        {
            Content = "Trocar",
            Style = (Style)FindResource("BotaoPequeno"),
            Width = 70,
        };
        DockPanel.SetDock(trocar, Dock.Right);
        linha.Children.Add(trocar);

        var atual = new TextBlock
        {
            Text = AtalhosDoPrograma.Rotulo(acao.Id),
            Foreground = Tema.Pincel(Tema.Destaque),
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Width = 120,
            TextAlignment = TextAlignment.Right,
            Margin = new Thickness(0, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        DockPanel.SetDock(atual, Dock.Right);
        linha.Children.Add(atual);

        // O nome entra por ULTIMO, e e ele que ocupa a sobra. Entrando
        // primeiro, quem ficava com a sobra era a tecla -- e ela se
        // centralizava no espaco entre o nome e o botao, entao cada
        // linha punha a tecla numa altura diferente, conforme o tamanho
        // do nome: "F5" la na direita, "Ctrl+N" quase no meio.
        linha.Children.Add(nome);

        atuais[acao.Id] = atual;

        trocar.Click += (_, _) =>
        {
            trocar.Content = "aperte…";
            atual.Text = "…";

            void Pegar(object _, KeyEventArgs ev)
            {
                var tecla = ev.Key == Key.System ? ev.SystemKey : ev.Key;
                ev.Handled = true;

                if (tecla is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
                    or Key.LeftShift or Key.RightShift) return;

                PreviewKeyDown -= Pegar;
                trocar.Content = "Trocar";

                if (tecla == Key.Escape)
                {
                    atual.Text = AtalhosDoPrograma.Rotulo(acao.Id);
                    return;
                }

                var mods = Keyboard.Modifiers;
                string conflito = AtalhosDoPrograma.EmUso(acao.Id, tecla, mods);
                if (conflito is not null)
                {
                    atual.Text = AtalhosDoPrograma.Rotulo(acao.Id);
                    MessageBox.Show(this,
                        $"{AtalhosDoPrograma.Rotulo(tecla, mods)} já é de \"{conflito}\".",
                        "Atalho ocupado", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                AtalhosDoPrograma.Definir(acao.Id, tecla, mods);
                atual.Text = AtalhosDoPrograma.Rotulo(acao.Id);
            }

            PreviewKeyDown += Pegar;
        };

        return linha;
    }
}
