using System.Windows;
using System.Windows.Controls;

namespace Simulador.App;

/// <summary>
/// O que acontece com o espaço de um painel que saiu para outra janela.
///
/// Alpha 18: "ao separar o código e o monitor serial, fica um vão que
/// não dá pra controlar no aplicativo, fica estranho". E ficava mesmo:
/// tirar o painel do Grid não tira a LINHA dele, então sobrava o buraco
/// com a altura que o painel tinha -- sem nada dentro, sem nada que
/// arrastar e sem jeito de fechar.
///
/// A regra é uma só: linha sem painel não ocupa espaço. Quando os dois
/// painéis da direita saem, a coluna inteira fecha e o circuito toma a
/// tela, que é o motivo de alguém soltar os dois.
/// </summary>
public partial class MainWindow
{
    // Os nomes usados na JanelaSolta. Ficam aqui porque e aqui que o
    // layout depende deles.
    const string PainelCodigo = "codigo";
    const string PainelDeBaixo = "baixo";

    /// <summary>As abas que da para soltar uma a uma.</summary>
    static readonly string[] AbasSoltaveis = ["console", "serial", "rede", "bluetooth"];

    /// <summary>
    /// As medidas que a pessoa escolheu arrastando as divisorias.
    ///
    /// Sao guardadas so enquanto os dois paineis estao em casa: com um
    /// deles fora as linhas valem zero, e guardar zero apagaria a
    /// escolha da pessoa na hora de devolver.
    /// </summary>
    GridLength alturaDoCodigo = new(1, GridUnitType.Star);
    GridLength alturaDeBaixo = new(232);
    GridLength larguraDoLado = new(420);

    void LembrarMedidas()
    {
        if (colunaDoCodigo is null) return;
        if (JanelaSolta.EstaSolto(PainelCodigo) || JanelaSolta.EstaSolto(PainelDeBaixo)) return;

        alturaDoCodigo = colunaDoCodigo.RowDefinitions[0].Height;
        alturaDeBaixo = colunaDoCodigo.RowDefinitions[4].Height;
        larguraDoLado = gradeRaiz.ColumnDefinitions[2].Width;
    }

    /// <summary>
    /// Refaz o layout da direita conforme quem esta em casa.
    ///
    /// Pode ser chamado a qualquer momento e quantas vezes for: ele nao
    /// acumula estado, so olha quem esta solto e decide. E por isso que
    /// da para chama-lo tanto do botao quanto do fechar da janela solta,
    /// sem combinar nada entre os dois.
    /// </summary>
    void AjustarVaos()
    {
        if (colunaDoCodigo is null || gradeRaiz is null) return;

        bool codigoFora = JanelaSolta.EstaSolto(PainelCodigo);
        bool baixoFora = JanelaSolta.EstaSolto(PainelDeBaixo);

        // As tres abas soltas deixam o painel de baixo so com a tira de
        // botoes. Nao e o painel fora, mas tambem nao e mais um painel:
        // nao ha conteudo para dividir altura com o codigo.
        bool soATira = !baixoFora && AbasSoltaveis.All(JanelaSolta.EstaSolto);

        var linhas = colunaDoCodigo.RowDefinitions;

        // Linha de painel que saiu nao ocupa espaco -- nem pelo MinHeight,
        // que sozinho ja segurava 120 px de vao.
        Medir(linhas[0], !codigoFora, alturaDoCodigo, 120);
        Medir(linhas[4], !baixoFora, alturaDeBaixo, 90);

        // Com um so em casa, ele fica com a coluna inteira. Deixar a
        // altura antiga daria o mesmo vao, so que embaixo.
        if (codigoFora && !baixoFora) linhas[4].Height = new GridLength(1, GridUnitType.Star);
        if (baixoFora && !codigoFora) linhas[0].Height = new GridLength(1, GridUnitType.Star);

        // So a tira: ela encolhe ate a propria altura e o codigo fica
        // com o resto. A tira tem que continuar a mao -- e por ela que
        // as abas voltam.
        if (soATira && !codigoFora)
        {
            linhas[4].Height = GridLength.Auto;
            linhas[4].MinHeight = 0;
            linhas[0].Height = new GridLength(1, GridUnitType.Star);
        }

        // A divisoria divide o espaco ENTRE os dois. Com um fora nao ha
        // o que dividir, e arrastar uma divisoria que nao divide nada e
        // exatamente o "não dá pra controlar" do pedido.
        bool temDivisoria = !codigoFora && !baixoFora && !soATira;
        divisoriaDoCodigo.Visibility = temDivisoria ? Visibility.Visible : Visibility.Collapsed;
        linhas[3].Height = new GridLength(temDivisoria ? 6 : 0);

        // Com o codigo fora tambem, nem a tira justifica manter a
        // coluna: as abas voltam fechando as janelas delas.
        FecharColunaVazia(codigoFora && (baixoFora || soATira));
    }

    /// <summary>
    /// A coluna da direita inteira, quando nao sobrou painel nenhum nela.
    ///
    /// Fecha a coluna E a divisoria vertical: uma coluna de 420 px vazia
    /// com uma divisoria que nao separa nada e pior do que nao ter
    /// coluna. O que sobrava la dentro -- o aviso de status -- sobe para
    /// a barra de cima, senao um erro de compilacao sumiria junto.
    /// </summary>
    void FecharColunaVazia(bool vazia)
    {
        var colunas = gradeRaiz.ColumnDefinitions;

        if (vazia)
        {
            colunas[1].Width = new GridLength(0);
            colunas[2].Width = new GridLength(0);
            colunas[2].MinWidth = 0;
            divisoriaDasColunas.Visibility = Visibility.Collapsed;
        }
        else
        {
            colunas[1].Width = new GridLength(6);
            colunas[2].MinWidth = 300;
            colunas[2].Width = larguraDoLado.Value > 0
                ? larguraDoLado : new GridLength(420);
            divisoriaDasColunas.Visibility = Visibility.Visible;
        }

        // O aviso segue a coluna: aparece em cima so quando ela fecha.
        txtStatusNaBarra.Visibility = vazia && txtStatus.Text.Length > 0
            ? Visibility.Visible : Visibility.Collapsed;
        txtStatusNaBarra.Text = txtStatus.Text;
        txtStatusNaBarra.Foreground = txtStatus.Foreground;
    }

    /// <summary>A linha com a medida guardada, ou encolhida ate sumir.</summary>
    static void Medir(RowDefinition linha, bool emCasa, GridLength medida, double minimo)
    {
        linha.MinHeight = emCasa ? minimo : 0;
        linha.Height = emCasa ? medida : new GridLength(0);
    }

    /// <summary>Para o teste: a altura de uma linha da coluna da direita.</summary>
    internal double TesteAlturaDaLinha(int linha)
        => colunaDoCodigo.RowDefinitions[linha].Height.Value
           + colunaDoCodigo.RowDefinitions[linha].MinHeight;

    /// <summary>
    /// Para o teste: a linha se ajusta ao conteudo, sem reservar altura.
    ///
    /// Nao da para conferir isso pela altura: GridLength.Auto guarda 1
    /// no Value, e nao 0 -- o que importa e a UNIDADE.
    /// </summary>
    internal bool TesteLinhaAoConteudo(int linha)
        => colunaDoCodigo.RowDefinitions[linha].Height.IsAuto
           && colunaDoCodigo.RowDefinitions[linha].MinHeight == 0;

    /// <summary>Para o teste: a largura da coluna da direita, divisoria inclusa.</summary>
    internal double TesteLarguraDoLado
        => gradeRaiz.ColumnDefinitions[1].Width.Value
           + gradeRaiz.ColumnDefinitions[2].Width.Value
           + gradeRaiz.ColumnDefinitions[2].MinWidth;

    /// <summary>Para o teste: o aviso que a barra de cima esta mostrando.</summary>
    internal string TesteAvisoNaBarra
        => txtStatusNaBarra.Visibility == Visibility.Visible ? txtStatusNaBarra.Text : "";
}
