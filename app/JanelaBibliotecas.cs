using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Simulador.App;

/// <summary>
/// O gerenciador de bibliotecas.
///
/// Em cima, o que o sketch aberto pede e ninguem atende -- que e a
/// resposta para "por que o meu codigo nao roda". Embaixo, duas abas: o
/// que o simulador executa e o que a pessoa instalou, e "Na internet": o
/// indice oficial da Arduino (o mesmo do Gerenciador de Bibliotecas da
/// Arduino IDE), com a busca, as dependencias e o download conferido.
/// </summary>
public class JanelaBibliotecas : Window
{
    readonly StackPanel lista = new();
    readonly TextBox busca = new();
    readonly TextBlock dica = new();
    readonly string sketch;

    // ---- a aba da internet ----
    bool naInternet;
    readonly Button abaInstaladas, abaInternet;
    readonly StackPanel blocoDaSituacao = new();
    readonly TextBlock situacao = new();
    readonly ProgressBar progresso = new();
    readonly Button atualizarIndice;
    IndiceDeBibliotecas indice;
    bool carregando, instalando;
    readonly CancellationTokenSource cancelar = new();
    readonly System.Windows.Threading.DispatcherTimer esperaDaBusca;

    /// <summary>O navegador do gerenciador (o teste troca por um de mentira).</summary>
    internal static Func<System.Net.Http.HttpClient> NovoNavegador = () =>
    {
        var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("CircuitSketch");
        return http;
    };
    static System.Net.Http.HttpClient navegador;
    static System.Net.Http.HttpClient Navegador => navegador ??= NovoNavegador();

    /// <summary>Para o teste: esquece o navegador (o proximo sai do <see cref="NovoNavegador"/>).</summary>
    internal static void TrocarNavegadorParaTeste(Func<System.Net.Http.HttpClient> novo)
    {
        NovoNavegador = novo;
        navegador = null;
    }

    public JanelaBibliotecas(Window dono, string sketch, bool abrirNaInternet = false)
    {
        this.sketch = sketch;

        try { Owner = dono; } catch (InvalidOperationException) { }

        Title = Idioma.T("Bibliotecas");
        Width = 760;
        Height = 620;
        MinWidth = 560;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Tema.Pincel(Tema.Fundo);

        var raiz = new DockPanel { Margin = new Thickness(16) };

        var topo = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        DockPanel.SetDock(topo, Dock.Top);
        topo.Children.Add(Faltando());

        // As abas: o que ja esta aqui, e o indice da Arduino na internet.
        var abas = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        abaInstaladas = Aba(Idioma.T("Instaladas e do simulador"), () => TrocarAba(false));
        abaInternet = Aba(Idioma.T("Na internet (índice da Arduino)"), () => TrocarAba(true));
        abas.Children.Add(abaInstaladas);
        abas.Children.Add(abaInternet);
        topo.Children.Add(abas);

        busca.Style = (Style)Application.Current.FindResource("CampoEscuro");
        busca.ToolTip = Idioma.T("Procurar biblioteca pelo nome ou pelo #include");
        esperaDaBusca = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        esperaDaBusca.Tick += (_, _) => { esperaDaBusca.Stop(); Preencher(); };
        busca.TextChanged += (_, _) =>
        {
            // Na internet a busca corre por milhares de bibliotecas: espera a
            // pessoa parar de digitar.
            if (naInternet) { esperaDaBusca.Stop(); esperaDaBusca.Start(); }
            else Preencher();
        };

        // O mesmo texto-fantasma da busca das configurações. Sem ele o
        // campo era uma faixa vazia sem explicação -- a dica só aparecia
        // parando o mouse em cima, e ninguém para o mouse numa faixa vazia.
        dica.Text = Idioma.T("Buscar pelo nome ou pelo #include");
        dica.Foreground = Tema.Pincel(Tema.TextoFraco);
        dica.FontSize = 12;
        dica.IsHitTestVisible = false;
        dica.Margin = new Thickness(8, 0, 0, 0);
        dica.VerticalAlignment = VerticalAlignment.Center;
        dica.HorizontalAlignment = HorizontalAlignment.Left;
        busca.TextChanged += (_, _) =>
            dica.Visibility = busca.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

        var comDica = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        comDica.Children.Add(busca);
        comDica.Children.Add(dica);
        topo.Children.Add(comDica);

        // A situacao do indice e o andamento do download (so na aba da internet).
        var linhaDaSituacao = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
        atualizarIndice = Botao(Idioma.T("Atualizar o índice"), () => _ = CarregarIndice(atualizar: true));
        atualizarIndice.Margin = new Thickness(8, 0, 0, 0);
        DockPanel.SetDock(atualizarIndice, Dock.Right);
        linhaDaSituacao.Children.Add(atualizarIndice);
        situacao.Foreground = Tema.Pincel(Tema.TextoFraco);
        situacao.FontSize = 11;
        situacao.TextWrapping = TextWrapping.Wrap;
        situacao.VerticalAlignment = VerticalAlignment.Center;
        linhaDaSituacao.Children.Add(situacao);
        progresso.Height = 4;
        progresso.Minimum = 0;
        progresso.Maximum = 1;
        progresso.Margin = new Thickness(0, 6, 0, 0);
        progresso.Foreground = Tema.Pincel(Tema.Destaque);
        progresso.Background = Tema.Pincel(Tema.Campo);
        progresso.BorderThickness = new Thickness(0);
        progresso.Visibility = Visibility.Collapsed;
        blocoDaSituacao.Children.Add(linhaDaSituacao);
        blocoDaSituacao.Children.Add(progresso);
        topo.Children.Add(blocoDaSituacao);

        raiz.Children.Add(topo);

        // DockPanel, e nao StackPanel: numa pilha horizontal o "Fechar"
        // ignorava o alinhamento a direita e ficava colado nos outros.
        var barra = new DockPanel
        {
            LastChildFill = false,
            Margin = new Thickness(0, 12, 0, 0),
        };
        DockPanel.SetDock(barra, Dock.Bottom);
        barra.Children.Add(Botao(Idioma.T("Instalar de um .zip…"), InstalarZip));
        barra.Children.Add(Botao(Idioma.T("Instalar de uma pasta…"), Instalar));
        barra.Children.Add(Botao(Idioma.T("Abrir a pasta das bibliotecas"), AbrirPasta));

        var fechar = Botao(Idioma.T("Fechar"), Close);
        fechar.Margin = new Thickness(0);          // encosta na borda da lista
        DockPanel.SetDock(fechar, Dock.Right);
        barra.Children.Add(fechar);
        raiz.Children.Add(barra);

        raiz.Children.Add(new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = lista,
        });

        Content = raiz;
        Closed += (_, _) => cancelar.Cancel();
        TrocarAba(abrirNaInternet);
    }

    Button Aba(string texto, Action aoClicar)
    {
        var aba = Botao(texto, aoClicar);
        aba.Margin = new Thickness(0, 0, 6, 0);
        return aba;
    }

    void TrocarAba(bool internet)
    {
        naInternet = internet;
        abaInstaladas.Opacity = internet ? 0.55 : 1;
        abaInternet.Opacity = internet ? 1 : 0.55;
        abaInstaladas.FontWeight = internet ? FontWeights.Normal : FontWeights.SemiBold;
        abaInternet.FontWeight = internet ? FontWeights.SemiBold : FontWeights.Normal;
        blocoDaSituacao.Visibility = internet ? Visibility.Visible : Visibility.Collapsed;
        dica.Text = internet ? Idioma.T("Buscar no índice da Arduino (nome, #include ou assunto)") : Idioma.T("Buscar pelo nome ou pelo #include");
        if (internet && indice is null && !carregando) _ = CarregarIndice(atualizar: false);
        Preencher();
    }

    /// <summary>O indice: do cache (o .gz baixado antes) ou da internet. Nunca trava a janela.</summary>
    async Task CarregarIndice(bool atualizar)
    {
        if (carregando || instalando) return;
        carregando = true;
        atualizarIndice.IsEnabled = false;
        situacao.Text = atualizar ? Idioma.T("Baixando o índice da Arduino…") : Idioma.T("Carregando o índice das bibliotecas…");
        Preencher();
        try
        {
            indice = await BibliotecasOnline.CarregarAsync(Navegador, atualizar, cancelar.Token);
            situacao.Text = DescreverIndice();
        }
        catch (OperationCanceledException) { return; }
        catch (Exception e) { situacao.Text = e.Message; }
        finally
        {
            carregando = false;
            atualizarIndice.IsEnabled = true;
        }
        Preencher();
    }

    string DescreverIndice()
    {
        string cache = System.IO.Path.Combine(BibliotecasOnline.PastaDoCache, "library_index.json.gz");
        string quando = System.IO.File.Exists(cache)
            ? System.IO.File.GetLastWriteTime(cache).ToString("dd/MM/yyyy HH:mm") : "agora";
        return Idioma.T("Índice da Arduino: {0:N0} bibliotecas (baixado em {1}).", indice?.Quantas ?? 0, quando);
    }

    /// <summary>A aba da internet: o que o sketch pede (sem busca) ou o que a busca acha.</summary>
    void PreencherDaInternet()
    {
        if (indice is null)
        {
            lista.Children.Add(Aviso(carregando ? Idioma.T("Carregando o índice…")
                : Idioma.T("Sem o índice ainda. Com a internet ligada, clique em \"Atualizar o índice\".")));
            return;
        }

        string filtro = busca.Text.Trim();
        var achadas = new List<BibliotecaOnline>();
        if (filtro.Length == 0)
        {
            // Sem busca: quem fornece cada #include que o sketch pede e ninguem atende.
            var sem = Acervo.SemDono(sketch);
            if (sem.Count == 0)
            {
                lista.Children.Add(Aviso(Idioma.T("Digite o nome da biblioteca, o #include (ex.: Adafruit_BMP280.h) ou um assunto (ex.: ultrassom).")));
                return;
            }
            lista.Children.Add(Aviso(Idioma.T("Quem fornece o que o sketch pede ({0}):", string.Join(", ", sem))));
            foreach (string cabecalho in sem)
            {
                var quem = indice.QuemTem(cabecalho);
                if (quem.Count == 0) lista.Children.Add(Aviso(Idioma.T("Nenhuma biblioteca do índice fornece <{0}>.", cabecalho)));
                foreach (var b in quem.Take(3))
                    if (!achadas.Contains(b)) achadas.Add(b);
            }
        }
        else
        {
            // O #include primeiro: "DHT.h" acha quem o fornece, e nao so quem tem "DHT" no nome.
            if (filtro.EndsWith(".h", StringComparison.OrdinalIgnoreCase)) achadas.AddRange(indice.QuemTem(filtro));
            achadas.AddRange(indice.Buscar(filtro, 60).Where(b => !achadas.Contains(b)));
            if (achadas.Count == 0) lista.Children.Add(Aviso(Idioma.T("Nenhuma biblioteca do índice com esse nome.")));
        }

        var instaladas = Acervo.Instaladas().Select(b => b.Nome).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var b in achadas.Take(60)) lista.Children.Add(LinhaDaInternet(b, instaladas));
    }

    UIElement Aviso(string texto) => new TextBlock
    {
        Text = texto,
        Foreground = Tema.Pincel(Tema.TextoFraco),
        FontSize = 12,
        Margin = new Thickness(2, 6, 0, 6),
        TextWrapping = TextWrapping.Wrap,
    };

    UIElement LinhaDaInternet(BibliotecaOnline b, HashSet<string> instaladas)
    {
        var caixa = new Border
        {
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(11, 9, 11, 10),
            Margin = new Thickness(0, 0, 0, 7),
            Background = Tema.Pincel(Tema.Painel),
            BorderThickness = new Thickness(1),
            BorderBrush = Tema.Pincel(Tema.Borda),
        };
        var linha = new DockPanel();

        bool jaTem = instaladas.Contains(b.Nome) || instaladas.Contains(Acervo.NomeLimpo(b.Nome));
        bool doSimulador = b.Cabecalhos.Count > 0 && b.Cabecalhos.All(Acervo.EhDoSimulador);
        var acao = Botao(doSimulador ? Idioma.T("Já roda") : jaTem ? Idioma.T("Reinstalar") : Idioma.T("Instalar"), () => _ = Instalar(b));
        acao.Margin = new Thickness(10, 0, 0, 0);
        acao.VerticalAlignment = VerticalAlignment.Center;
        acao.IsEnabled = !instalando && !doSimulador;
        DockPanel.SetDock(acao, Dock.Right);
        linha.Children.Add(acao);

        var coluna = new StackPanel();
        var titulo = new WrapPanel();
        titulo.Children.Add(new TextBlock
        {
            Text = b.Nome, Foreground = Tema.Pincel(Tema.TextoForte), FontSize = 13, FontWeight = FontWeights.SemiBold,
        });
        titulo.Children.Add(new TextBlock
        {
            Text = $"  {b.Versao}" + (string.IsNullOrWhiteSpace(b.Autor) ? "" : $" · {b.Autor}")
                   + (doSimulador ? "  · " + Idioma.T("o simulador já tem por dentro") : jaTem ? "  · " + Idioma.T("instalada") : ""),
            Foreground = Tema.Pincel(doSimulador || jaTem ? Tema.Destaque : Tema.TextoFraco),
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
        });
        coluna.Children.Add(titulo);
        if (b.Cabecalhos.Count > 0)
            coluna.Children.Add(new TextBlock
            {
                Text = string.Join("  ", b.Cabecalhos.Take(4).Select(h => $"#include <{h}>")) + (b.Cabecalhos.Count > 4 ? "  …" : ""),
                Foreground = Tema.Pincel(Tema.TextoFraco), FontFamily = new FontFamily("Consolas"), FontSize = 11,
                Margin = new Thickness(0, 3, 0, 0), TextWrapping = TextWrapping.Wrap,
            });
        coluna.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(b.Sentence) ? b.Paragraph : b.Sentence,
            Foreground = Tema.Pincel(Tema.Texto), FontSize = 11, Margin = new Thickness(0, 3, 0, 0), TextWrapping = TextWrapping.Wrap,
        });
        coluna.Children.Add(new TextBlock
        {
            Text = Acervo.EmTexto(b.Size)
                   + (b.Dependencias.Count > 0 ? Idioma.T(" · precisa de: {0}", string.Join(", ", b.Dependencias.Select(d => d.Nome))) : "")
                   + (b.Architectures.Count > 0 && !b.Architectures.Contains("*") ? Idioma.T(" · placas: {0}", string.Join(", ", b.Architectures)) : ""),
            Foreground = Tema.Pincel(Tema.TextoFraco), FontSize = 11, Margin = new Thickness(0, 3, 0, 0), TextWrapping = TextWrapping.Wrap,
        });
        linha.Children.Add(coluna);
        caixa.Child = linha;
        return caixa;
    }

    /// <summary>Instala com as dependencias, depois de mostrar o que vai baixar e quanto.</summary>
    async Task Instalar(BibliotecaOnline alvo)
    {
        if (instalando || indice is null) return;
        var jaInstaladas = Acervo.Instaladas().Select(b => b.Nome).ToHashSet(StringComparer.OrdinalIgnoreCase);
        jaInstaladas.Remove(alvo.Nome);                  // "Reinstalar" baixa de novo a que foi pedida
        jaInstaladas.Remove(Acervo.NomeLimpo(alvo.Nome));
        IReadOnlyList<BibliotecaOnline> plano;
        try { plano = indice.ParaInstalar(alvo, jaInstaladas); }
        catch (Exception e)
        {
            Avisar(e.Message, Idioma.T("Não deu para instalar"));
            return;
        }
        if (plano.Count == 0) return;

        string itens = string.Join("\n", plano.Select(b => $"  • {b.Nome} {b.Versao} ({Acervo.EmTexto(b.Size)})"));
        if (!SemPerguntarParaTeste
            && MessageBox.Show(this,
                Idioma.T("Baixar de downloads.arduino.cc e instalar:\n\n{0}\n\nTotal: {1}.{2}", itens, Acervo.EmTexto(plano.Sum(b => b.Size)), (plano.Count > 1 ? Idioma.T("\n\nAs outras são dependências: a biblioteca não funciona sem elas.") : "")),
                Idioma.T("Instalar biblioteca"), MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
            return;

        instalando = true;
        atualizarIndice.IsEnabled = false;
        progresso.Visibility = Visibility.Visible;
        var nomes = new List<string>();
        try
        {
            for (int i = 0; i < plano.Count; i++)
            {
                var b = plano[i];
                int k = i;
                situacao.Text = Idioma.T("Baixando {0} {1} ({2} de {3})…", b.Nome, b.Versao, k + 1, plano.Count);
                Preencher();
                var andamento = new Progress<double>(f => progresso.Value = (k + Math.Clamp(f, 0, 1)) / plano.Count);
                nomes.Add(await BibliotecasOnline.BaixarEInstalarAsync(Navegador, b, andamento, cancelar.Token));
            }
            situacao.Text = Idioma.T("Instalada(s): {0}.", string.Join(", ", nomes));
            InstaladasParaTeste.AddRange(nomes);
            if (!SemPerguntarParaTeste)
                MessageBox.Show(this,
                    Idioma.T("{0}: instalada(s).\n\nQuando o sketch fizer o #include, o simulador roda o código C++ que vem dentro.", string.Join(", ", nomes)),
                    Idioma.T("Biblioteca instalada"), MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (OperationCanceledException) { situacao.Text = Idioma.T("Download cancelado."); }
        catch (Exception e)
        {
            situacao.Text = (nomes.Count > 0 ? Idioma.T("Instalada(s): {0}. ", string.Join(", ", nomes)) : "") + "Falhou: " + e.Message;
            Avisar(e.Message, Idioma.T("Não deu para instalar"));
        }
        finally
        {
            instalando = false;
            atualizarIndice.IsEnabled = true;
            progresso.Visibility = Visibility.Collapsed;
            progresso.Value = 0;
        }
        Preencher();
    }

    void Avisar(string texto, string titulo)
    {
        if (!SemPerguntarParaTeste) MessageBox.Show(this, texto, titulo, MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    UIElement Faltando()
    {
        var sem = Acervo.SemDono(sketch);

        var caixa = new Border
        {
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(11, 8, 11, 9),
            Background = Tema.Pincel(Tema.Painel),
            BorderThickness = new Thickness(1),
            BorderBrush = sem.Count == 0
                ? Tema.Pincel(Tema.Borda)
                : new SolidColorBrush(Color.FromRgb(0x8A, 0x6D, 0x1A)),
        };

        var coluna = new StackPanel();
        coluna.Children.Add(new TextBlock
        {
            Text = sem.Count == 0
                ? Idioma.T("Este sketch não pede nenhuma biblioteca de fora.")
                : Idioma.T("O sketch pede {0} biblioteca(s) que o simulador não executa:", sem.Count),
            Foreground = Tema.Pincel(sem.Count == 0 ? Tema.TextoFraco : Tema.TextoForte),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
        });

        if (sem.Count > 0)
        {
            coluna.Children.Add(new TextBlock
            {
                Text = string.Join(" · ", sem),
                Foreground = new SolidColorBrush(Color.FromRgb(0xF1, 0xC4, 0x0F)),
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12,
                Margin = new Thickness(0, 5, 0, 0),
                TextWrapping = TextWrapping.Wrap,
            });
            coluna.Children.Add(new TextBlock
            {
                Text = Idioma.T("Baixe pelo índice da Arduino (o botão abaixo), ou instale a pasta ou o .zip, como na Arduino IDE: o simulador passa a rodar o código C++ dela."),
                Foreground = Tema.Pincel(Tema.TextoFraco),
                FontSize = 11,
                Margin = new Thickness(0, 6, 0, 0),
                TextWrapping = TextWrapping.Wrap,
            });
            var procurar = Botao(Idioma.T("Procurar na internet"), () => { busca.Text = ""; TrocarAba(true); });
            procurar.Margin = new Thickness(0, 7, 0, 0);
            procurar.HorizontalAlignment = HorizontalAlignment.Left;
            coluna.Children.Add(procurar);
        }

        caixa.Child = coluna;
        return caixa;
    }

    void Preencher()
    {
        string filtro = busca.Text.Trim();
        lista.Children.Clear();
        if (naInternet)
        {
            PreencherDaInternet();
            return;
        }

        // Sem ligar para acento: "radio" acha o "Rádio NRF24L01.".
        var comparador = System.Globalization.CultureInfo.InvariantCulture.CompareInfo;
        const System.Globalization.CompareOptions opcoes =
            System.Globalization.CompareOptions.IgnoreCase | System.Globalization.CompareOptions.IgnoreNonSpace;
        bool Tem(string texto) => comparador.IndexOf(texto ?? "", filtro, opcoes) >= 0;
        var todas = Acervo.Todas()
            .Where(b => filtro.Length == 0 || Tem(b.Nome) || Tem(b.Cabecalho) || Tem(b.Classes) || Tem(b.Resumo))
            .ToList();

        if (todas.Count == 0)
        {
            lista.Children.Add(new TextBlock
            {
                Text = Idioma.T("nenhuma biblioteca com esse nome (procure na aba \"Na internet\")"),
                Foreground = Tema.Pincel(Tema.TextoFraco),
                FontSize = 12,
                Margin = new Thickness(2, 10, 0, 0),
            });
            return;
        }

        foreach (var biblioteca in todas) lista.Children.Add(Linha(biblioteca));
    }

    UIElement Linha(BibliotecaDoAcervo biblioteca)
    {
        var caixa = new Border
        {
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(11, 9, 11, 10),
            Margin = new Thickness(0, 0, 0, 7),
            Background = Tema.Pincel(Tema.Painel),
            BorderThickness = new Thickness(1),
            BorderBrush = Tema.Pincel(Tema.Borda),
        };

        var linha = new DockPanel();

        if (!biblioteca.DoSimulador)
        {
            var tirar = Botao(Idioma.T("Remover"), () =>
            {
                if (MessageBox.Show(this,
                        Idioma.T("Remover \"{0}\" das bibliotecas instaladas?", biblioteca.Nome),
                        "CircuitSketch", MessageBoxButton.YesNo,
                        MessageBoxImage.Question) != MessageBoxResult.Yes)
                    return;

                try { Acervo.Remover(biblioteca); }
                catch (Exception e)
                {
                    MessageBox.Show(this, e.Message, Idioma.T("Não deu para remover"),
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                Preencher();
            });
            tirar.Margin = new Thickness(10, 0, 0, 0);
            tirar.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(tirar, Dock.Right);
            linha.Children.Add(tirar);
        }

        var coluna = new StackPanel();

        var titulo = new StackPanel { Orientation = Orientation.Horizontal };
        titulo.Children.Add(new TextBlock
        {
            Text = biblioteca.Nome,
            Foreground = Tema.Pincel(Tema.TextoForte),
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
        });
        titulo.Children.Add(new TextBlock
        {
            Text = biblioteca.DoSimulador ? Idioma.T("  roda no simulador") : Idioma.T("  instalada por você"),
            Foreground = Tema.Pincel(biblioteca.DoSimulador ? Tema.Destaque : Tema.TextoFraco),
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
        });
        coluna.Children.Add(titulo);

        coluna.Children.Add(new TextBlock
        {
            Text = $"#include <{biblioteca.Cabecalho}>",
            Foreground = Tema.Pincel(Tema.TextoFraco),
            FontFamily = new FontFamily("Consolas"),
            FontSize = 11,
            Margin = new Thickness(0, 3, 0, 0),
        });

        coluna.Children.Add(new TextBlock
        {
            Text = biblioteca.Resumo,
            Foreground = Tema.Pincel(Tema.Texto),
            FontSize = 11,
            Margin = new Thickness(0, 3, 0, 0),
            TextWrapping = TextWrapping.Wrap,
        });

        if (!string.IsNullOrWhiteSpace(biblioteca.Classes))
            coluna.Children.Add(new TextBlock
            {
                Text = biblioteca.DoSimulador
                    ? Idioma.T("objetos: ") + biblioteca.Classes
                    : Idioma.T("{0} cabeçalho(s): {1}", biblioteca.Arquivos, biblioteca.Classes),
                Foreground = Tema.Pincel(Tema.TextoFraco),
                FontSize = 11,
                Margin = new Thickness(0, 3, 0, 0),
                TextWrapping = TextWrapping.Wrap,
            });

        linha.Children.Add(coluna);
        caixa.Child = linha;
        return caixa;
    }

    void Instalar()
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog
        {
            Title = Idioma.T("Escolha a pasta da biblioteca (a que tem os .h dentro)"),
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            Instalada(Acervo.Instalar(dlg.FolderName));
        }
        catch (Exception e)
        {
            MessageBox.Show(this, e.Message, Idioma.T("Não deu para instalar"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    void InstalarZip()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = Idioma.T("Escolha o .zip da biblioteca (o que o GitHub ou o site dela entrega)"),
            Filter = Idioma.T("Biblioteca compactada (*.zip)|*.zip"),
        };
        if (dlg.ShowDialog(this) != true) return;

        try
        {
            Instalada(Acervo.InstalarZip(dlg.FileName));
        }
        catch (Exception e)
        {
            MessageBox.Show(this, e.Message, Idioma.T("Não deu para instalar"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    void Instalada(string nome)
    {
        Preencher();
        MessageBox.Show(this,
            Idioma.T("\"{0}\" entrou no acervo.\n\nQuando o sketch fizer o #include dela, o simulador roda o código C++ que vem dentro. "
                     + "Se ela mexer direto nos registradores de um chip específico, o erro diz em que arquivo e linha isso acontece.", nome),
            Idioma.T("Biblioteca instalada"), MessageBoxButton.OK, MessageBoxImage.Information);
    }

    void AbrirPasta()
    {
        try
        {
            System.IO.Directory.CreateDirectory(Acervo.Pasta);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = Acervo.Pasta,
                UseShellExecute = true,
            });
        }
        catch (Exception e)
        {
            MessageBox.Show(this, e.Message, Idioma.T("Não deu para abrir a pasta"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    Button Botao(string texto, Action aoClicar)
    {
        var botao = new Button
        {
            Content = texto,
            Style = (Style)FindResource("BotaoPequeno"),
            Margin = new Thickness(0, 0, 8, 0),
            Cursor = Cursors.Hand,
        };
        botao.Click += (_, _) => aoClicar();
        return botao;
    }

    /// <summary>Para o teste: quantas bibliotecas estao listadas agora.</summary>
    internal int ListadasParaTeste => lista.Children.OfType<Border>().Count();

    /// <summary>Para o teste: digitar na busca (sem esperar o tempo de digitacao).</summary>
    internal void BuscarParaTeste(string texto)
    {
        busca.Text = texto;
        esperaDaBusca.Stop();
        Preencher();
    }

    /// <summary>Para o teste: abre a aba da internet e baixa o indice (do navegador de mentira).</summary>
    internal Task AbrirInternetParaTeste()
    {
        // A carga marca "carregando" antes do primeiro await: a aba nao
        // dispara outra (do cache) por cima desta.
        var baixar = CarregarIndice(atualizar: true);
        TrocarAba(true);
        return baixar;
    }

    /// <summary>Para o teste: instala uma do indice sem as caixas de pergunta.</summary>
    internal Task InstalarParaTeste(string nome)
    {
        SemPerguntarParaTeste = true;
        var b = indice?.Buscar(nome, 5).FirstOrDefault(x => x.Nome.Equals(nome, StringComparison.OrdinalIgnoreCase));
        return b is null ? Task.CompletedTask : Instalar(b);
    }

    internal bool SemPerguntarParaTeste;
    internal readonly List<string> InstaladasParaTeste = [];
    internal string SituacaoParaTeste => situacao.Text;
}
