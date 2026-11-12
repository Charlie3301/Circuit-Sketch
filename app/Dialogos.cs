using System.Windows;
using System.Windows.Controls;

namespace Simulador.App;

public static class Dialogos
{
    public static string Perguntar(Window dono, string titulo, string sugestao)
    {
        var janela = new Window
        {
            Title = titulo,
            Width = 340,
            SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Tema.Pincel(Tema.Fundo),
        };
        try { janela.Owner = dono; } catch (InvalidOperationException) { }

        var coluna = new StackPanel { Margin = new Thickness(14) };
        var campo = new TextBox
        {
            Text = sugestao ?? "",
            Style = (Style)Application.Current.FindResource("CampoEscuro"),
            Margin = new Thickness(0, 0, 0, 12),
        };
        coluna.Children.Add(campo);

        string resposta = null;
        var linha = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };

        var ok = new Button
        {
            Content = "OK",
            Style = (Style)Application.Current.FindResource("BotaoPequeno"),
            Margin = new Thickness(0, 0, 8, 0),
            IsDefault = true,
        };
        ok.Click += (_, _) => { resposta = campo.Text.Trim(); janela.Close(); };
        linha.Children.Add(ok);

        var cancelar = new Button
        {
            Content = Idioma.T("Cancelar"),
            Style = (Style)Application.Current.FindResource("BotaoPequeno"),
            IsCancel = true,
        };
        cancelar.Click += (_, _) => janela.Close();
        linha.Children.Add(cancelar);

        coluna.Children.Add(linha);
        janela.Content = coluna;

        campo.Focus();
        campo.SelectAll();
        janela.ShowDialog();

        return string.IsNullOrWhiteSpace(resposta) ? null : resposta;
    }
}
