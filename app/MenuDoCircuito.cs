using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Simulador.App;

/// <summary>
/// O menu do botao direito no circuito.
///
/// Item 6 da lista do professor. O que entra no menu depende DO QUE
/// ESTA EMBAIXO DO PONTEIRO -- clicar numa peca oferece girar e apagar,
/// clicar num fio oferece a cor dele, clicar no vazio oferece colar e
/// ajustar a tela. Um menu igual em todo lugar seria um menu que nao
/// serve para nada.
///
/// Os atalhos aparecem ao lado de cada item, e saem da mesma tabela que
/// o teclado usa: quem trocar o atalho de girar ve a troca aqui tambem.
/// </summary>
public partial class MainWindow
{
    void MontarMenuDoCircuito(Point naTela, EditorCircuito.Acerto acerto)
    {
        var menu = new ContextMenu
        {
            PlacementTarget = editor,
            Placement = System.Windows.Controls.Primitives.PlacementMode.RelativePoint,
            HorizontalOffset = naTela.X,
            VerticalOffset = naTela.Y,
        };

        bool temPeca = editor.QuantidadeSelecionada > 0;
        bool temFio = editor.FioAtual is not null || acerto.Camada == EditorCircuito.Camada.Fio;

        if (temFio && !temPeca) MontarParaFio(menu);
        else if (temPeca) MontarParaPeca(menu, acerto);
        else MontarParaOVazio(menu);

        // Estes tres valem em qualquer lugar: sao do circuito, e nao do
        // que esta debaixo do ponteiro.
        Separar(menu);
        Item(menu, "Desfazer", "desfazer", Desfazer);
        Item(menu, "Refazer", "refazer", Refazer);
        Separar(menu);
        Item(menu, "Ajustar à tela", "ajustar", (_, _) => editor.Ajustar());
        Item(menu, "Adicionar componente…", "adicionar", (_, _) => AdicionarPeca());

        editor.ContextMenu = menu;
        menu.IsOpen = true;
    }

    void MontarParaPeca(ContextMenu menu, EditorCircuito.Acerto acerto)
    {
        int quantas = editor.QuantidadeSelecionada;
        var peca = editor.Selecionada;

        menu.Items.Add(new MenuItem
        {
            Header = quantas > 1
                ? $"{quantas} peças selecionadas"
                : Catalogo.Obter(peca?.Tipo)?.Nome ?? "peça",
            IsEnabled = false,
            FontWeight = FontWeights.SemiBold,
        });
        Separar(menu);

        Item(menu, "Girar 90°", "girar", Girar);
        Item(menu, "Espelhar", "espelhar", Espelhar);
        Separar(menu);
        Item(menu, "Copiar", "copiar", Copiar);
        Item(menu, "Colar", "colar", Colar);

        // Duplicar e copiar e colar num passo so: e o que se faz o tempo
        // todo montando uma fileira de LEDs.
        Item(menu, "Duplicar", null, (_, _) => { editor.Copiar(); editor.Colar(); });

        Separar(menu);
        Item(menu, "Excluir", "excluir", Excluir);

        if (quantas != 1 || peca is null) return;

        Separar(menu);

        // A placa tem o seu proprio par de acoes: o codigo e dela.
        if (PerfilPlaca.EhPlaca(peca.Tipo))
        {
            Item(menu, "Mostrar o código desta placa", null, (_, _) =>
            {
                placaDoEditor = peca.Id;
                AbrirArquivoDoProjeto(null);
                MostrarCodigoDaPlaca();
            });
            Item(menu, "Reiniciar (RESET)", null, (_, _) =>
            {
                SegurarReset(peca.Id);
                SoltarReset(peca.Id);
            });
        }

        Item(menu, "Renomear…", null, (_, _) =>
        {
            string novo = Dialogos.Perguntar(this, "Nome da peça", peca.Nome ?? "");
            if (novo is not null) editor.DefinirNome(peca, novo);
        });
    }

    void MontarParaFio(ContextMenu menu)
    {
        menu.Items.Add(new MenuItem
        {
            Header = "fio",
            IsEnabled = false,
            FontWeight = FontWeights.SemiBold,
        });
        Separar(menu);

        var cores = new MenuItem { Header = "Cor do fio" };
        foreach (var (nome, hex) in CoresDeFio)
        {
            string cor = hex;
            var escolha = new MenuItem { Header = nome };
            escolha.Click += (_, _) => editor.DefinirCorDoFio(cor);
            cores.Items.Add(escolha);
        }
        menu.Items.Add(cores);

        var estilos = new MenuItem { Header = "Formato" };
        foreach (var (id, nome) in new[]
                 {
                     (Fio.EstiloSolto, "Solto"),
                     (Fio.EstiloReto, "Reto"),
                     (Fio.EstiloDegrau, "Em degrau"),
                 })
        {
            string qual = id;
            var escolha = new MenuItem { Header = nome };
            escolha.Click += (_, _) => editor.DefinirEstiloDeTodos(qual);
            estilos.Items.Add(escolha);
        }
        menu.Items.Add(estilos);

        Separar(menu);
        Item(menu, "Excluir o fio", "excluir", Excluir);
    }

    void MontarParaOVazio(ContextMenu menu)
    {
        Item(menu, "Colar", "colar", Colar);
        Separar(menu);
        Item(menu, "Ligar ou desligar a grade", "grade", (_, _) => DefinirGrade(!editor.MostrarGrade));
    }

    /// <summary>
    /// Um item do menu, com o atalho escrito ao lado.
    ///
    /// O atalho vem da tabela de verdade, e nao de um texto fixo: quem
    /// trocou o atalho nas configuracoes ve o novo aqui.
    /// </summary>
    void Item(ContextMenu menu, string texto, string acao, RoutedEventHandler aoClicar)
    {
        var item = new MenuItem { Header = texto };

        if (acao is not null)
        {
            // Rotulo, e nao Escrever: Escrever e o formato de GUARDAR nas
            // preferencias ("2:C"), e era ele que aparecia no menu no
            // lugar de "Ctrl+C".
            var (tecla, mods) = AtalhosDoPrograma.Atalho(acao);
            if (tecla != Key.None) item.InputGestureText = AtalhosDoPrograma.Rotulo(tecla, mods);
        }

        item.Click += aoClicar;
        menu.Items.Add(item);
    }

    static void Separar(ContextMenu menu)
    {
        if (menu.Items.Count > 0 && menu.Items[^1] is not Separator)
            menu.Items.Add(new Separator());
    }
}
