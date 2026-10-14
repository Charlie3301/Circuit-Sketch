using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Simulador.App;

/// <summary>
/// O menu de uma ligação, na aba Componentes.
///
/// Alpha 18: "capacidade de controlar onde os fios vão e conexões de
/// pinos na aba circuito".
///
/// Até a Alpha 17 essa aba só DESCREVIA o circuito: dava para ler
/// "D2 &gt; A" e não dava para fazer nada com aquilo. Mexer no fio exigia
/// achá-lo no desenho, que num circuito com vinte fios é exatamente o
/// problema que a lista existe para resolver -- a pessoa acha a ligação
/// na lista e depois tem que caçá-la na tela.
///
/// Agora a linha é o controle: o pino da placa, a cor e o traçado do fio
/// mudam de onde eles estão escritos.
/// </summary>
public partial class MainWindow
{
    /// <summary>O menu da linha, ou null quando ela não tem fio nenhum.</summary>
    ContextMenu MenuDaLigacao(ItemDeComponente item)
    {
        if (item.Fio is null) return null;

        var menu = new ContextMenu();
        var fio = item.Fio;

        // ---- o pino da placa ----
        //
        // É a parte que o pedido chama de "conexões de pinos": trocar o
        // D2 por um D4 sem desenhar o fio de novo.
        var pinos = new MenuItem { Header = "Ligar na placa em…" };
        foreach (var opcao in PinosDaPlacaDoFio(fio))
        {
            var escolha = new MenuItem
            {
                Header = opcao,
                IsCheckable = true,
                IsChecked = opcao == item.PinoDaPlaca,
            };

            string qual = opcao;
            escolha.Click += (_, _) => TrocarPinoDoFio(fio, qual);
            pinos.Items.Add(escolha);
        }

        if (pinos.Items.Count > 0) menu.Items.Add(pinos);

        // ---- a cor ----
        var cores = new MenuItem { Header = "Cor do fio" };
        foreach (var (nome, hex) in CoresDeFio)
        {
            string valor = hex;
            var escolha = new MenuItem
            {
                Header = nome,
                IsCheckable = true,
                IsChecked = string.Equals(fio.Cor, hex, StringComparison.OrdinalIgnoreCase),
                Icon = new Border
                {
                    Width = 12,
                    Height = 12,
                    CornerRadius = new CornerRadius(2),
                    Background = new SolidColorBrush(
                        (Color)ColorConverter.ConvertFromString(hex)),
                },
            };

            escolha.Click += (_, _) => MexerNoFio(() => fio.Cor = valor);
            cores.Items.Add(escolha);
        }
        menu.Items.Add(cores);

        // ---- o traçado ----
        //
        // "Onde os fios vão", a outra metade do pedido. Arrastar os
        // cantos continua sendo no desenho; o que a lista resolve é o
        // caso inverso -- desfazer um traçado que ficou ruim.
        var tracado = new MenuItem { Header = "Traçado" };

        foreach (var (rotulo, estilo) in new[]
                 {
                     ("solto (curva)", Fio.EstiloSolto),
                     ("reto", Fio.EstiloReto),
                     ("em degrau", Fio.EstiloDegrau),
                 })
        {
            string qual = estilo;
            var escolha = new MenuItem
            {
                Header = rotulo,
                IsCheckable = true,
                IsChecked = fio.Estilo == estilo,
            };

            escolha.Click += (_, _) => MexerNoFio(() => fio.Estilo = qual);
            tracado.Items.Add(escolha);
        }

        tracado.Items.Add(new Separator());

        var endireitar = new MenuItem
        {
            Header = $"Tirar os cantos ({fio.Dobras.Count})",
            IsEnabled = fio.Dobras.Count > 0,
        };
        endireitar.Click += (_, _) => MexerNoFio(fio.Dobras.Clear);
        tracado.Items.Add(endireitar);

        menu.Items.Add(tracado);
        menu.Items.Add(new Separator());

        var achar = new MenuItem { Header = "Mostrar no circuito" };
        achar.Click += (_, _) => MostrarFioNoCircuito(fio);
        menu.Items.Add(achar);

        var apagar = new MenuItem { Header = "Apagar este fio" };
        apagar.Click += (_, _) => MexerNoFio(() => editor.Atual.Fios.Remove(fio));
        menu.Items.Add(apagar);

        return menu;
    }

    /// <summary>
    /// Os pinos da placa que este fio pode alcançar.
    ///
    /// Só os da placa: trocar a ponta que está no LED mudaria qual peça
    /// está ligada, e isso é desenhar outro fio, não ajustar este.
    /// </summary>
    IEnumerable<string> PinosDaPlacaDoFio(Fio fio)
    {
        var placa = PontaNaPlaca(fio);
        if (placa is null) return [];

        return Catalogo.Obter(placa.Tipo)?.Pinos.Select(p => p.Nome) ?? [];
    }

    /// <summary>Qual das duas pontas do fio está numa placa de desenvolvimento.</summary>
    PecaNoCircuito PontaNaPlaca(Fio fio)
    {
        var de = editor.Atual.Achar(fio.DePeca);
        if (de is not null && PerfilPlaca.EhPlaca(de.Tipo)) return de;

        var para = editor.Atual.Achar(fio.ParaPeca);
        return para is not null && PerfilPlaca.EhPlaca(para.Tipo) ? para : null;
    }

    void TrocarPinoDoFio(Fio fio, string pino)
    {
        var placa = PontaNaPlaca(fio);
        if (placa is null) return;

        MexerNoFio(() =>
        {
            if (fio.DePeca == placa.Id) fio.DePino = pino;
            else fio.ParaPino = pino;
        });

        Aviso($"fio ligado em {pino}.");
    }

    /// <summary>Seleciona o fio no desenho e leva a tela até ele.</summary>
    void MostrarFioNoCircuito(Fio fio)
    {
        MostrarModo("circuito");
        editor.SelecionarFio(fio);
    }

    /// <summary>Para o teste: troca o pino da placa deste fio.</summary>
    internal void TesteTrocarPinoDoFio(int fio, string pino)
        => TrocarPinoDoFio(editor.Atual.Fios[fio], pino);

    /// <summary>Para o teste: pinta o fio.</summary>
    internal void TestePintarFio(int fio, string cor)
        => MexerNoFio(() => editor.Atual.Fios[fio].Cor = cor);

    /// <summary>Para o teste: tira os cantos do fio.</summary>
    internal void TesteEndireitarFio(int fio)
        => MexerNoFio(editor.Atual.Fios[fio].Dobras.Clear);

    /// <summary>Para o teste: apaga o fio.</summary>
    internal void TesteApagarFio(int fio)
        => MexerNoFio(() => editor.Atual.Fios.RemoveAt(fio));

    /// <summary>Para o teste: os pinos que o menu ofereceria para este fio.</summary>
    internal List<string> TestePinosOferecidos(int fio)
        => [.. PinosDaPlacaDoFio(editor.Atual.Fios[fio])];

    /// <summary>
    /// Toda mudança feita pelo menu passa por aqui.
    ///
    /// São sempre os mesmos quatro passos -- mexer, guardar para o
    /// desfazer, redesenhar e refazer a rede --, e esquecer um deles dá
    /// um sintoma diferente e confuso: fio que some ao rolar a lista,
    /// Ctrl+Z que não volta, LED que continua aceso no pino antigo.
    /// </summary>
    void MexerNoFio(Action mudanca)
    {
        mudanca();

        editor.Redesenhar();
        editor.AvisarQueMudou();

        AtualizarComponentes(forcar: true);
    }
}
