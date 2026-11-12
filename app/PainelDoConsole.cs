using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace Simulador.App;

/// <summary>
/// O console desenhado na tela: a aba à esquerda do monitor serial.
///
/// Quem guarda as linhas é o <see cref="Registro"/>; aqui só se pinta.
/// As linhas novas são ACRESCENTADAS, e não redesenhadas: um sketch que
/// erra dentro do loop escreve depressa, e refazer duas mil linhas a
/// cada mensagem travaria a janela justo quando ela mais tem o que
/// mostrar.
/// </summary>
public partial class MainWindow
{
    readonly Registro console = new();

    /// <summary>Quanto do <see cref="Registro.Total"/> já está na tela.</summary>
    long consoleDesenhado;

    void LigarConsole()
    {
        // O Registro é escrito também das threads das placas, e o WPF só
        // aceita mexer nos controles pela thread da janela.
        //
        // Estando JÁ nessa thread, pinta na hora em vez de agendar: é o
        // caso da linha de status, e agendar deixaria o console um passo
        // atrás do que a janela está mostrando.
        console.Mudou += () =>
        {
            if (Dispatcher.CheckAccess()) DesenharConsole();
            else Dispatcher.BeginInvoke(new Action(DesenharConsole));
        };

        DesenharConsole();
    }

    void DesenharConsole()
    {
        if (txtConsole is null) return;

        var linhas = console.Linhas;
        long total = console.Total;
        long novas = total - consoleDesenhado;

        // Ou foi limpo, ou passaram tantas linhas de uma vez que as
        // antigas já caíram pela borda. Nos dois casos não há o que
        // aproveitar do que está na tela.
        if (novas < 0 || novas > linhas.Count)
        {
            txtConsole.Inlines.Clear();
            novas = linhas.Count;
        }

        for (int i = linhas.Count - (int)novas; i < linhas.Count; i++)
            txtConsole.Inlines.Add(new Run(linhas[i] + Environment.NewLine)
            {
                Foreground = PincelDoNivel(linhas[i].Nivel),
            });

        consoleDesenhado = total;

        if (novas > 0) rolagemConsole?.ScrollToEnd();
    }

    // Pelo tema: o amarelo do aviso e o vermelho claro do erro sumiam
    // no fundo branco dos temas claros.
    static Brush PincelDoNivel(NivelDoConsole nivel) => nivel switch
    {
        NivelDoConsole.Erro => Tema.Pincel(Tema.Erro),
        NivelDoConsole.Aviso => Tema.Pincel(Tema.Aviso),
        _ => Tema.Pincel(Tema.TextoFraco),
    };

    /// <summary>Pinta de novo todas as linhas -- depois de trocar o tema.</summary>
    void RepintarConsole()
    {
        consoleDesenhado = long.MinValue / 2;
        DesenharConsole();
    }

    void LimparConsole(object s = null, RoutedEventArgs e = null) => console.Limpar();

    void CopiarConsole(object s = null, RoutedEventArgs e = null)
    {
        string texto = console.EmTexto();
        if (texto.Length == 0) return;

        // A área de transferência é do Windows e pode estar ocupada por
        // outro programa; falhar ao copiar não pode derrubar a janela.
        try { Clipboard.SetText(texto); }
        catch (System.Runtime.InteropServices.COMException) { return; }

        Aviso(Idioma.T("console copiado."));
    }

    /// <summary>Para o teste: o console inteiro em texto.</summary>
    internal string TesteConsole => console.EmTexto();

    /// <summary>Para o teste: o texto das linhas de um nivel (erro, aviso).</summary>
    internal List<string> TesteLinhasDoNivel(NivelDoConsole nivel)
        => [.. console.Linhas.Where(l => l.Nivel == nivel).Select(l => l.Texto)];

    /// <summary>Para o teste: quantas linhas o console tem.</summary>
    internal int TesteLinhasDoConsole => console.Linhas.Count;

    /// <summary>Para o teste: quantas linhas já foram pintadas na tela.</summary>
    internal int TesteLinhasPintadas => txtConsole.Inlines.Count;

    /// <summary>Para o teste: escreve no console como o simulador escreveria.</summary>
    internal void TesteEscreverNoConsole(string nivel, string texto)
        => console.Escrever(nivel switch
        {
            "erro" => NivelDoConsole.Erro,
            "aviso" => NivelDoConsole.Aviso,
            _ => NivelDoConsole.Info,
        }, texto);

    /// <summary>Para o teste: limpa o console.</summary>
    internal void TesteLimparConsole() => LimparConsole();
}
