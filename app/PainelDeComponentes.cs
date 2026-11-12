using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Simulador.App;

/// <summary>
/// O painel "Componentes": o circuito em cartoes, do lado do desenho.
///
/// Quem monta a arvore e a <see cref="ArvoreDeComponentes"/>; aqui ela vira
/// cartoes: um por placa (com o que esta ligado nela, e os pinos em
/// etiquetas com a cor do fio), um por protoboard (com o que esta espetado)
/// e um por peca solta. A seta abre e fecha; clicar seleciona a peca no
/// desenho.
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
        MontarResumoDosComponentes();

        if (itens.Count == 0)
        {
            listaDeComponentes.Children.Add(CircuitoVazio());
            return;
        }

        foreach (var item in itens) listaDeComponentes.Children.Add(Cartao(item, dentro: false));
    }

    /// <summary>As duas pilulas do canto: quantas pecas e quantos fios.</summary>
    void MontarResumoDosComponentes()
    {
        resumoComponentes.Children.Clear();
        int pecas = editor.Atual.Pecas.Count, fios = editor.Atual.Fios.Count;
        resumoComponentes.Children.Add(Pilula(pecas == 1 ? Idioma.T("1 peça") : Idioma.T("{0} peças", pecas)));
        resumoComponentes.Children.Add(Pilula(fios == 1 ? Idioma.T("1 fio") : Idioma.T("{0} fios", fios)));
    }

    static Border Pilula(string texto)
    {
        var rotulo = new TextBlock { Text = texto, FontSize = 11 };
        rotulo.SetResourceReference(TextBlock.ForegroundProperty, Tema.TextoFraco);
        var pilula = new Border
        {
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(9, 2, 9, 2),
            Margin = new Thickness(6, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = rotulo,
        };
        pilula.SetResourceReference(Border.BackgroundProperty, Tema.Painel);
        pilula.SetResourceReference(Border.BorderBrushProperty, Tema.Borda);
        return pilula;
    }

    /// <summary>O circuito sem nada: o recado no meio, e nao uma lista vazia.</summary>
    static UIElement CircuitoVazio()
    {
        var painel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 48, 0, 0) };
        var titulo = new TextBlock
        {
            Text = Idioma.T("O circuito está vazio"),
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        titulo.SetResourceReference(TextBlock.ForegroundProperty, Tema.TextoForte);
        var dica = new TextBlock
        {
            Text = Idioma.T("Aperte A (ou o +) para adicionar um componente."),
            FontSize = 12,
            Margin = new Thickness(0, 4, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        dica.SetResourceReference(TextBlock.ForegroundProperty, Tema.TextoFraco);
        painel.Children.Add(titulo);
        painel.Children.Add(dica);
        return painel;
    }

    /// <summary>
    /// Um cartao: a linha do item e, embaixo, o que esta pendurado nele. Os de
    /// fora sao os grandes (fundo de painel); a placa espetada numa protoboard
    /// vira um cartao menor dentro do dela.
    /// </summary>
    Border Cartao(ItemDeComponente item, bool dentro)
    {
        var conteudo = new StackPanel();
        conteudo.Children.Add(Linha(item, grande: !dentro));
        if (item.TemFilhos && !galhosFechados.Contains(item.Titulo))
            conteudo.Children.Add(Filhos(item));

        var cartao = new Border
        {
            CornerRadius = new CornerRadius(dentro ? 5 : 6),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(dentro ? 6 : 8),
            Margin = dentro ? new Thickness(0, 4, 0, 4) : new Thickness(0, 0, 0, 8),
            Child = conteudo,
        };
        cartao.SetResourceReference(Border.BackgroundProperty, dentro ? Tema.Campo : Tema.Painel);
        cartao.SetResourceReference(Border.BorderBrushProperty, Tema.Borda);
        return cartao;
    }

    /// <summary>
    /// O que fica embaixo de um item: os pares de pino viram etiquetas numa
    /// fileira so; as pecas viram linhas recuadas, com o traco da arvore na
    /// esquerda.
    /// </summary>
    UIElement Filhos(ItemDeComponente item)
    {
        if (item.Filhos.All(f => f.EhPar))
        {
            var etiquetas = new WrapPanel { Margin = new Thickness(LarguraDaSeta + LarguraDaMiniatura(false) + 10, 0, 0, 4) };
            foreach (var par in item.Filhos) etiquetas.Children.Add(Etiqueta(par));
            return etiquetas;
        }

        var lista = new StackPanel();
        foreach (var filho in item.Filhos)
        {
            // A placa espetada na protoboard leva junto o que esta ligado
            // nela: vira um cartao dentro do cartao da protoboard.
            if (filho.TemFilhos && !filho.Filhos.All(f => f.EhPar)) lista.Children.Add(Cartao(filho, dentro: true));
            else
            {
                lista.Children.Add(Linha(filho, grande: false));
                if (filho.TemFilhos && !galhosFechados.Contains(filho.Titulo)) lista.Children.Add(Filhos(filho));
            }
        }

        var galho = new Border
        {
            BorderThickness = new Thickness(1, 0, 0, 0),
            Margin = new Thickness(LarguraDaSeta / 2 + 4, 2, 0, 2),
            Padding = new Thickness(10, 0, 0, 0),
            Child = lista,
        };
        galho.SetResourceReference(Border.BorderBrushProperty, Tema.Borda);
        return galho;
    }

    const double LarguraDaSeta = 16;

    static double LarguraDaMiniatura(bool grande) => grande ? 44 : 32;

    /// <summary>
    /// Uma linha: a seta (se tem filhos), a miniatura da peca, o nome e,
    /// embaixo do nome, o detalhe ("ligada na ESP32", "sem ligação"...). A
    /// cor do nome e o estado da ligacao (amarelo sem ligacao, vermelho
    /// queimado), e e ele que pisca verde no play.
    /// </summary>
    Border Linha(ItemDeComponente item, bool grande)
    {
        bool aberto = !galhosFechados.Contains(item.Titulo);
        bool selecionada = item.Peca is not null && item.Peca == editor.Selecionada?.Id;

        var linha = new DockPanel { LastChildFill = true };

        var seta = new TextBlock
        {
            Text = item.TemFilhos ? (aberto ? "▾" : "▸") : "",
            Width = LarguraDaSeta,
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = item.TemFilhos ? Cursors.Hand : Cursors.Arrow,
            Background = Brushes.Transparent,
        };
        seta.SetResourceReference(TextBlock.ForegroundProperty, Tema.TextoFraco);
        if (item.TemFilhos)
            seta.MouseLeftButtonDown += (_, e) =>
            {
                if (!galhosFechados.Remove(item.Titulo)) galhosFechados.Add(item.Titulo);
                AtualizarComponentes(forcar: true);
                e.Handled = true;
            };
        DockPanel.SetDock(seta, Dock.Left);
        linha.Children.Add(seta);

        var miniatura = Miniatura(item.Tipo, grande);
        DockPanel.SetDock(miniatura, Dock.Left);
        linha.Children.Add(miniatura);

        // Quantas pecas estao ligadas na placa: o resumo do cartao dela.
        string detalhe = item.Detalhe;
        if (detalhe is null && grande && item.TemFilhos && item.Filhos.All(f => !f.EhPar))
        {
            int ligadas = item.Filhos.Count;
            detalhe = ligadas == 1 ? Idioma.T("1 peça ligada") : Idioma.T("{0} peças ligadas", ligadas);
        }
        else if (detalhe is null && grande && PerfilPlaca.EhPlaca(item.Tipo))
            detalhe = Idioma.T("nada ligado ainda");

        var textos = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
        var titulo = new TextBlock
        {
            Text = TituloNaTela(item),
            FontSize = grande ? 13 : 12,
            FontWeight = grande || selecionada ? FontWeights.SemiBold : FontWeights.Normal,
            Foreground = PincelDoEstado(item, selecionada),
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        textos.Children.Add(titulo);
        Anotar(item, titulo);

        if (detalhe is not null)
        {
            var subtitulo = new TextBlock
            {
                Text = detalhe,
                FontSize = 11,
                Margin = new Thickness(0, 1, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            if (item.Estado is EstadoDaLigacao.Erro or EstadoDaLigacao.SemLigacao)
                subtitulo.Foreground = PincelDoDetalhe(item);
            else
                subtitulo.SetResourceReference(TextBlock.ForegroundProperty, Tema.TextoFraco);
            textos.Children.Add(subtitulo);
        }
        linha.Children.Add(textos);

        var fundo = new Border
        {
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(2, 3, 6, 3),
            Child = linha,
            Background = Brushes.Transparent,
            Cursor = item.Peca is null ? Cursors.Arrow : Cursors.Hand,
        };
        if (selecionada) fundo.SetResourceReference(Border.BackgroundProperty, Tema.Selecao);
        else if (item.Peca is not null)
        {
            fundo.MouseEnter += (_, _) => fundo.SetResourceReference(Border.BackgroundProperty, Tema.Hover);
            fundo.MouseLeave += (_, _) => fundo.Background = Brushes.Transparent;
        }

        if (item.Peca is not null)
            fundo.MouseLeftButtonDown += (_, _) =>
            {
                editor.SelecionarPorId(item.Peca);
                AtualizarComponentes(forcar: true);
            };
        return fundo;
    }

    /// <summary>"[Protoboard 830 x2]" e "&gt; LED" sao o formato da arvore em texto; no cartao, so o nome.</summary>
    static string TituloNaTela(ItemDeComponente item)
    {
        string t = item.Titulo;
        if (t.StartsWith("> ", StringComparison.Ordinal)) t = t[2..];
        if (t.Length > 2 && t[0] == '[' && t[^1] == ']') t = t[1..^1];
        return t;
    }

    /// <summary>A miniatura da peca (a mesma da paleta), numa moldura do tamanho da linha.</summary>
    static Border Miniatura(string tipo, bool grande)
    {
        double largura = LarguraDaMiniatura(grande), altura = grande ? 32 : 24;
        var moldura = new Border
        {
            Width = largura,
            Height = altura,
            CornerRadius = new CornerRadius(4),
            VerticalAlignment = VerticalAlignment.Center,
        };
        moldura.SetResourceReference(Border.BackgroundProperty, Tema.FundoDoCircuito);

        var arte = tipo is null ? null : JanelaPecas.Miniatura(tipo);
        if (arte is not null)
            moldura.Child = new Image { Source = arte, Stretch = Stretch.Uniform, Margin = new Thickness(2) };
        else
        {
            // A protoboard nao tem desenho pronto (e desenhada na hora).
            var simbolo = new TextBlock
            {
                Text = "▤",
                FontSize = grande ? 17 : 13,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            simbolo.SetResourceReference(TextBlock.ForegroundProperty, Tema.TextoFraco);
            moldura.Child = simbolo;
        }
        return moldura;
    }

    /// <summary>
    /// Um par de pinos numa etiqueta: "D14 &gt; A", com o "&gt;" na cor do fio.
    ///
    /// O pedido foi curto -- "o &gt; tem que ter a cor do fio" -- e o
    /// motivo e bom: num circuito de sete fios, achar "o verde" na
    /// lista e imediato, e achar "D2 &gt; A" nao e. O botao direito abre o
    /// menu do fio (pino, cor, tracado); o clique seleciona a peca.
    /// </summary>
    Border Etiqueta(ItemDeComponente item)
    {
        bool selecionada = item.Peca is not null && item.Peca == editor.Selecionada?.Id;
        int seta = item.Titulo.IndexOf('>');

        var bloco = new TextBlock
        {
            FontFamily = new FontFamily("Consolas"),
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = PincelDoEstado(item, selecionada),
        };
        if (seta < 0) bloco.Text = item.Titulo;
        else
        {
            bloco.Inlines.Add(new System.Windows.Documents.Run(item.Titulo[..seta]));
            var meio = new System.Windows.Documents.Run(">") { FontWeight = FontWeights.Bold };
            if (item.PorFio) meio.Foreground = PincelDoFio(item.Cor);
            else meio.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, Tema.TextoFraco);
            bloco.Inlines.Add(meio);
            bloco.Inlines.Add(new System.Windows.Documents.Run(item.Titulo[(seta + 1)..]));
        }
        Anotar(item, bloco);

        var etiqueta = new Border
        {
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 2, 8, 2),
            Margin = new Thickness(0, 4, 6, 0),
            Child = bloco,
            Cursor = Cursors.Hand,
        };
        etiqueta.SetResourceReference(Border.BackgroundProperty, Tema.Campo);
        if (item.PorFio)
            etiqueta.BorderBrush = PincelDoFio(item.Cor);
        else
            etiqueta.SetResourceReference(Border.BorderBrushProperty, Tema.Borda);

        // A ligacao vira controle: o menu dela troca o pino da placa, a
        // cor e o tracado do fio. Alpha 18, "capacidade de controlar
        // onde os fios vao e conexoes de pinos".
        if (item.Fio is not null)
        {
            etiqueta.ContextMenu = MenuDaLigacao(item);
            etiqueta.ToolTip = Idioma.T("botão direito: pino, cor e traçado deste fio");
        }
        if (item.Peca is not null)
            etiqueta.MouseLeftButtonDown += (_, _) =>
            {
                editor.SelecionarPorId(item.Peca);
                AtualizarComponentes(forcar: true);
            };
        return etiqueta;
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

        return achada?.Estado.ToString() ?? Idioma.T("nao achou");
    }

    static ItemDeComponente Procurar(List<ItemDeComponente> itens, string titulo)
        => Achar(itens, i => i.Titulo.Contains(titulo));

    /// <summary>Para o teste: o estado da linha desta peca, pelo id dela.</summary>
    internal string TesteEstadoDaPeca(string idDaPeca)
        => Achar(ArvoreDeComponentes.Montar(editor.Atual, rede, editor.Queimadas),
                 i => i.Peca == idDaPeca)?.Estado.ToString() ?? Idioma.T("nao achou");

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
