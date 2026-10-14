using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Simulador.App;

/// <summary>
/// O painel "Componentes": o circuito em arvore, do lado do desenho.
///
/// Quem monta a arvore e a <see cref="ArvoreDeComponentes"/>; aqui so se
/// desenham as linhas, com a seta que abre e fecha e o clique que
/// seleciona a peca no circuito.
///
/// Clicar na linha seleciona a peca LA no desenho. E o que transforma a
/// lista de uma tabela bonita numa ferramenta: num circuito com trinta
/// pecas, achar "o LED que esta no D14" pela lista e mais rapido do que
/// cacar na tela.
/// </summary>
public partial class MainWindow
{
    /// <summary>Os galhos que a pessoa fechou, por titulo.</summary>
    readonly HashSet<string> galhosFechados = [];

    /// <summary>O que a arvore mostrava da ultima vez, para nao remontar a toa.</summary>
    string arvoreMostrada;

    /// <summary>
    /// As linhas de ligacao que estao na tela agora, para piscar no play.
    ///
    /// Guardadas na hora de desenhar. Sair cacando TextBlock na arvore
    /// visual depois daria no mesmo com mais jeito de dar errado.
    /// </summary>
    readonly List<TextBlock> ligacoesNaTela = [];

    /// <summary>Verde do play, o mesmo botao de rodar.</summary>
    static readonly Color Verde = Color.FromRgb(0x2F, 0xA8, 0x4F);

    /// <summary>Amarelo de "esta ai mas nao chega em lugar nenhum".</summary>
    static readonly Color Amarelo = Color.FromRgb(0xF1, 0xC4, 0x0F);

    /// <summary>Vermelho de queimado.</summary>
    static readonly Color VermelhoDaLista = Color.FromRgb(0xF4, 0x83, 0x71);

    /// <summary>Quanto dura o pisca-e-apaga do play.</summary>
    static readonly Duration TempoDoPisca = TimeSpan.FromMilliseconds(900);

    void AtualizarComponentes(bool forcar = false)
    {
        if (modoDaTela != "componentes" && !forcar) return;

        var itens = ArvoreDeComponentes.Montar(editor.Atual, rede, editor.Queimadas);
        string assinatura = ArvoreDeComponentes.EmTexto(itens)
                            + "#" + string.Join(",", galhosFechados)
                            + "#" + (editor.Selecionada?.Id ?? "")
                            + "#" + string.Join(",", editor.Queimadas);

        if (assinatura == arvoreMostrada) return;
        arvoreMostrada = assinatura;

        listaDeComponentes.Children.Clear();
        ligacoesNaTela.Clear();

        if (itens.Count == 0)
        {
            listaDeComponentes.Children.Add(new TextBlock
            {
                Text = "O circuito está vazio. Aperte A para adicionar um componente.",
                Foreground = Tema.Pincel(Tema.TextoFraco),
                FontSize = 12,
                Margin = new Thickness(4, 8, 0, 0),
            });
            return;
        }

        foreach (var item in itens) DesenharItem(item);

        txtResumoComponentes.Text =
            $"{editor.Atual.Pecas.Count} peças · {editor.Atual.Fios.Count} fios";
    }

    void DesenharItem(ItemDeComponente item)
    {
        bool aberto = !galhosFechados.Contains(item.Titulo);

        var linha = new DockPanel
        {
            Margin = new Thickness(item.Nivel * 18 - 18, 1, 0, 1),
            Background = Brushes.Transparent,
            Cursor = item.Peca is null ? Cursors.Arrow : Cursors.Hand,
        };

        // A ligacao vira controle: o menu dela troca o pino da placa, a
        // cor e o tracado do fio. Alpha 18, "capacidade de controlar
        // onde os fios vao e conexoes de pinos".
        if (item.Fio is not null)
        {
            linha.ContextMenu = MenuDaLigacao(item);
            linha.ToolTip = "botão direito: pino, cor e traçado deste fio";
        }

        // A seta: so aparece em quem tem filho, mas o espaco e sempre
        // reservado, para os titulos ficarem alinhados na coluna.
        var seta = new TextBlock
        {
            Text = item.TemFilhos ? (aberto ? "▾" : "▸") : " ",
            Width = 16,
            FontSize = 11,
            Foreground = Tema.Pincel(Tema.TextoFraco),
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = item.TemFilhos ? Cursors.Hand : Cursors.Arrow,
        };
        DockPanel.SetDock(seta, Dock.Left);
        linha.Children.Add(seta);

        if (item.TemFilhos)
            seta.MouseLeftButtonDown += (_, e) =>
            {
                if (!galhosFechados.Remove(item.Titulo)) galhosFechados.Add(item.Titulo);
                AtualizarComponentes(forcar: true);
                e.Handled = true;
            };

        bool selecionada = item.Peca is not null && item.Peca == editor.Selecionada?.Id;

        // O ">" sai na cor do fio, e o resto do texto na cor normal.
        // Por isso a linha e montada em pedacos, e nao num Text so.
        if (item.PorFio && item.Titulo.Contains('>'))
        {
            linha.Children.Add(LigacaoColorida(item, selecionada));
            MontarFim(linha, item);
            return;
        }

        var titulo = new TextBlock
        {
            Text = item.Titulo,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12,
            Foreground = PincelDoEstado(item, selecionada),
            FontWeight = selecionada ? FontWeights.SemiBold : FontWeights.Normal,
            VerticalAlignment = VerticalAlignment.Center,
        };
        linha.Children.Add(titulo);
        Anotar(item, titulo);

        if (item.Detalhe is not null)
        {
            var detalhe = new TextBlock
            {
                Text = "  " + item.Detalhe,
                FontSize = 11,
                FontStyle = FontStyles.Italic,
                Foreground = PincelDoDetalhe(item),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6, 0, 0, 0),
            };
            DockPanel.SetDock(detalhe, Dock.Right);
            detalhe.HorizontalAlignment = HorizontalAlignment.Left;
            linha.Children.Add(detalhe);
        }

        if (item.Peca is not null)
            linha.MouseLeftButtonDown += (_, _) =>
            {
                editor.SelecionarPorId(item.Peca);
                AtualizarComponentes(forcar: true);
            };

        listaDeComponentes.Children.Add(linha);

        if (!aberto || !item.TemFilhos) return;
        foreach (var filho in item.Filhos) DesenharItem(filho);
    }

    /// <summary>
    /// "D2 > A" com o ">" na cor do fio.
    ///
    /// O pedido foi curto -- "o &gt; tem que ter a cor do fio" -- e o
    /// motivo e bom: num circuito de sete fios, achar "o verde" na
    /// lista e imediato, e achar "D2 &gt; A" nao e.
    /// </summary>
    TextBlock LigacaoColorida(ItemDeComponente item, bool selecionada)
    {
        int seta = item.Titulo.IndexOf('>');

        var bloco = new TextBlock
        {
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Tema.Pincel(selecionada ? Tema.TextoForte : Tema.Texto),
        };

        bloco.Foreground = PincelDoEstado(item, selecionada);

        bloco.Inlines.Add(new System.Windows.Documents.Run(item.Titulo[..seta]));

        bloco.Inlines.Add(new System.Windows.Documents.Run(">")
        {
            Foreground = PincelDoFio(item.Cor),
            FontWeight = FontWeights.Bold,
        });

        bloco.Inlines.Add(new System.Windows.Documents.Run(item.Titulo[(seta + 1)..]));
        Anotar(item, bloco);
        return bloco;
    }

    /// <summary>
    /// A cor do fio, clareada o suficiente para ler no fundo escuro.
    ///
    /// Fio preto e o caso que obriga: no painel escuro ele sumiria.
    /// </summary>
    static Brush PincelDoFio(string hex)
    {
        Color cor;
        try { cor = (Color)ColorConverter.ConvertFromString(hex); }
        catch (FormatException) { return Tema.Pincel(Tema.TextoFraco); }

        double brilho = (cor.R * 0.299 + cor.G * 0.587 + cor.B * 0.114) / 255.0;
        if (brilho >= 0.35) return new SolidColorBrush(cor);

        // Clareia na direcao do branco, sem perder a matiz.
        const double quanto = 0.55;
        return new SolidColorBrush(Color.FromRgb(
            (byte)(cor.R + (255 - cor.R) * quanto),
            (byte)(cor.G + (255 - cor.G) * quanto),
            (byte)(cor.B + (255 - cor.B) * quanto)));
    }

    /// <summary>O detalhe e o clique, que valem para os dois formatos de linha.</summary>
    void MontarFim(DockPanel linha, ItemDeComponente item)
    {
        if (item.Detalhe is not null)
            linha.Children.Add(new TextBlock
            {
                Text = "  " + item.Detalhe,
                FontSize = 11,
                FontStyle = FontStyles.Italic,
                Foreground = PincelDoDetalhe(item),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6, 0, 0, 0),
            });

        if (item.Peca is not null)
            linha.MouseLeftButtonDown += (_, _) =>
            {
                editor.SelecionarPorId(item.Peca);
                AtualizarComponentes(forcar: true);
            };

        listaDeComponentes.Children.Add(linha);
        if (item.TemFilhos) foreach (var filho in item.Filhos) DesenharItem(filho);
    }

    /// <summary>
    /// A cor de repouso da linha.
    ///
    /// Alpha 18: ligado em branco, sem ligacao em amarelo, com erro em
    /// vermelho. O verde nao entra aqui -- ele e so o pisca do play, e
    /// uma lista que fica verde para sempre nao diria mais nada.
    /// </summary>
    static Brush PincelDoEstado(ItemDeComponente item, bool selecionada) => item.Estado switch
    {
        EstadoDaLigacao.Erro => new SolidColorBrush(VermelhoDaLista),
        EstadoDaLigacao.SemLigacao => new SolidColorBrush(Amarelo),
        _ => new SolidColorBrush(Tema.CorDe(selecionada ? Tema.TextoForte : Tema.Texto)),
    };

    /// <summary>O detalhe acompanha o estado, mais apagado.</summary>
    static Brush PincelDoDetalhe(ItemDeComponente item) => item.Estado switch
    {
        EstadoDaLigacao.Erro => new SolidColorBrush(VermelhoDaLista),
        EstadoDaLigacao.SemLigacao => new SolidColorBrush(Amarelo),
        _ => new SolidColorBrush(Color.FromRgb(0xC8, 0x92, 0x3A)),
    };

    /// <summary>Guarda a linha se ela for uma ligacao ligada, que e quem pisca.</summary>
    void Anotar(ItemDeComponente item, TextBlock bloco)
    {
        if (item.Estado == EstadoDaLigacao.Ligado) ligacoesNaTela.Add(bloco);
    }

    /// <summary>
    /// O pisca do play: verde e some para o branco.
    ///
    /// Alpha 18: "ao dar play no circuito, deve piscar em verde e voltar
    /// para branco com fade quais fios estao conectados". O verde dura
    /// menos de um segundo de proposito -- o que ele responde e "o que
    /// esta de fato ligado", e nao "como ficou a lista".
    ///
    /// Quem esta sem ligacao fica amarelo e quem queimou fica vermelho,
    /// e esses NAO piscam: eles ja estao na cor que interessa.
    /// </summary>
    void PiscarLigacoes()
    {
        AtualizarComponentes(forcar: true);
        if (Autoteste.Rodando) return;

        foreach (var bloco in ligacoesNaTela)
        {
            // Pincel proprio por linha: animar um pincel compartilhado
            // mudaria a cor de tudo o que o usa.
            var pincel = new SolidColorBrush(Verde);
            bloco.Foreground = pincel;

            pincel.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation
            {
                From = Verde,
                To = Tema.CorDe(Tema.Texto),
                Duration = TempoDoPisca,
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
            });
        }
    }

    /// <summary>Para o teste: quantas linhas piscariam neste circuito.</summary>
    internal int TesteLigacoesQuePiscam
    {
        get
        {
            AtualizarComponentes(forcar: true);
            return ligacoesNaTela.Count;
        }
    }

    /// <summary>Para o teste: o estado de uma linha, pelo titulo dela.</summary>
    internal string TesteEstadoDaLinha(string titulo)
    {
        var achada = Procurar(
            ArvoreDeComponentes.Montar(editor.Atual, rede, editor.Queimadas), titulo);

        return achada?.Estado.ToString() ?? "nao achou";
    }

    static ItemDeComponente Procurar(List<ItemDeComponente> itens, string titulo)
        => Achar(itens, i => i.Titulo.Contains(titulo));

    /// <summary>Para o teste: o estado da linha desta peca, pelo id dela.</summary>
    internal string TesteEstadoDaPeca(string idDaPeca)
        => Achar(ArvoreDeComponentes.Montar(editor.Atual, rede, editor.Queimadas),
                 i => i.Peca == idDaPeca)?.Estado.ToString() ?? "nao achou";

    static ItemDeComponente Achar(List<ItemDeComponente> itens, Func<ItemDeComponente, bool> serve)
    {
        foreach (var item in itens)
        {
            if (serve(item)) return item;

            var dentro = Achar(item.Filhos, serve);
            if (dentro is not null) return dentro;
        }

        return null;
    }

    /// <summary>Para o teste: esta linha tem fio, e portanto menu?</summary>
    internal bool TesteLinhaTemFio(string titulo)
        => Procurar(ArvoreDeComponentes.Montar(editor.Atual, rede, editor.Queimadas),
                    titulo)?.Fio is not null;

    /// <summary>Para o teste: a cor que a lista mostra nesta linha.</summary>
    internal string TesteCorDaLinha(string titulo)
        => Procurar(ArvoreDeComponentes.Montar(editor.Atual, rede, editor.Queimadas),
                    titulo)?.Cor;

    /// <summary>Para o teste: a arvore em texto, sem abrir janela nenhuma.</summary>
    internal string TesteArvoreDeComponentes()
        => ArvoreDeComponentes.EmTexto(ArvoreDeComponentes.Montar(editor.Atual, rede));
}
