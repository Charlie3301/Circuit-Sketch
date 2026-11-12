using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace Simulador.App;

public class JanelaCartao : Window
{
    readonly ListBox lista = new();
    readonly TextBox conteudo = new();
    readonly TextBlock aviso = new();
    readonly TextBlock linhaDaPasta = new();
    Button tirarPasta;
    readonly Dictionary<string, string> arquivos;
    readonly Dictionary<string, string> doComputador;
    readonly List<(string Nome, int Tamanho, string Origem)> binariosNaPlaca;
    readonly Dictionary<string, byte[]> gravados;
    readonly Func<string, byte[]> lerDaPlaca;
    readonly bool somenteLeitura;
    /// <summary>"cartão" ou "pendrive": os dois sao masculinos, e as frases servem aos dois.</summary>
    readonly string oQue;

    /// <summary>A pasta do computador que serve de cartao, ou null. A janela devolve a escolha por aqui.</summary>
    public string PastaDoComputador { get; private set; }

    string aberto;
    bool preenchendo;

    enum Tipo { Texto, DoComputador, Gravado, BinarioNaPlaca }

    /// <param name="arquivos">Os arquivos de texto da peca (vao dentro do .sktch).</param>
    /// <param name="doComputador">
    /// Os arquivos do computador que entram em bytes (nome no cartao -> caminho).
    /// </param>
    /// <param name="binariosNaPlaca">Com a simulacao rodando: os arquivos em bytes que estao no cartao agora.</param>
    /// <param name="gravados">Parada: os arquivos em bytes que o sketch gravou nas rodadas de antes (vao no .sktch).</param>
    /// <param name="lerDaPlaca">Rodando: le os bytes de um arquivo do cartao de agora, para salvar no computador.</param>
    /// <param name="titulo">"Cartão microSD" ou "Pendrive USB".</param>
    /// <param name="pastaDoComputador">A pasta do computador que serve de cartao, ou null.</param>
    public JanelaCartao(Window dono, Dictionary<string, string> arquivos, bool somenteLeitura,
                        Dictionary<string, string> doComputador = null,
                        List<(string Nome, int Tamanho, string Origem)> binariosNaPlaca = null,
                        Dictionary<string, byte[]> gravados = null,
                        Func<string, byte[]> lerDaPlaca = null,
                        string titulo = null,
                        string pastaDoComputador = null)
    {
        // O titulo chega sem traduzir ("Pendrive USB" ou "Cartão microSD"): e ele que diz o que e.
        titulo ??= Idioma.N("Cartão microSD");
        oQue = titulo.StartsWith("Pendrive", StringComparison.OrdinalIgnoreCase) ? Idioma.T("pendrive") : Idioma.T("cartão");
        titulo = Idioma.T(titulo);
        PastaDoComputador = pastaDoComputador;
        this.arquivos = arquivos;
        this.doComputador = doComputador ?? new(StringComparer.OrdinalIgnoreCase);
        this.binariosNaPlaca = binariosNaPlaca ?? [];
        this.gravados = gravados ?? new(StringComparer.OrdinalIgnoreCase);
        this.lerDaPlaca = lerDaPlaca;
        this.somenteLeitura = somenteLeitura;

        try { Owner = dono; } catch (InvalidOperationException) { }

        Title = titulo;
        Width = 700;
        Height = 500;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Tema.Pincel(Tema.Fundo);

        var raiz = new Grid { Margin = new Thickness(14) };
        raiz.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250) });
        raiz.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        raiz.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        raiz.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        raiz.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        raiz.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        aviso.Text = somenteLeitura
            ? Idioma.T("A simulação está rodando: aqui aparece o que o sketch gravou. Pare para editar.")
            : Idioma.T("Os arquivos de texto vão junto no .sktch, e os que o sketch gravou em bytes também; os do computador (um WAD, uma imagem, um som) ficam lá e entram no {0}, em bytes, quando a simulação começa.", oQue);
        aviso.Foreground = Tema.Pincel(Tema.TextoFraco);
        aviso.FontSize = 11;
        aviso.TextWrapping = TextWrapping.Wrap;
        aviso.Margin = new Thickness(0, 0, 0, 10);
        Grid.SetColumnSpan(aviso, 2);
        raiz.Children.Add(aviso);

        // A pasta do computador: uma biblioteca de musica inteira, sem
        // copiar arquivo por arquivo.
        var faixaDaPasta = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        if (!somenteLeitura)
        {
            tirarPasta = Botao(Idioma.T("Tirar a pasta"), () => { PastaDoComputador = null; MostrarPasta(); });
            tirarPasta.Margin = new Thickness(8, 0, 0, 0);
            DockPanel.SetDock(tirarPasta, Dock.Right);
            faixaDaPasta.Children.Add(tirarPasta);
            var usarPasta = Botao(Idioma.T("Usar uma pasta do computador…"), EscolherPasta);
            usarPasta.Margin = new Thickness(8, 0, 0, 0);
            DockPanel.SetDock(usarPasta, Dock.Right);
            faixaDaPasta.Children.Add(usarPasta);
        }
        linhaDaPasta.Foreground = Tema.Pincel(Tema.Texto);
        linhaDaPasta.FontSize = 12;
        linhaDaPasta.TextTrimming = TextTrimming.CharacterEllipsis;
        linhaDaPasta.VerticalAlignment = VerticalAlignment.Center;
        linhaDaPasta.ToolTip = Idioma.T("Tudo o que está na pasta, com as subpastas, aparece no {0} e é lido do disco só quando o sketch abre. O que o sketch grava fica no {0} simulado: a pasta nunca muda.", oQue);
        faixaDaPasta.Children.Add(linhaDaPasta);
        Grid.SetRow(faixaDaPasta, 1);
        Grid.SetColumnSpan(faixaDaPasta, 2);
        raiz.Children.Add(faixaDaPasta);

        lista.Background = Tema.Pincel(Tema.Campo);
        lista.Foreground = Tema.Pincel(Tema.Texto);
        lista.BorderBrush = Tema.Pincel(Tema.Borda);
        lista.Margin = new Thickness(0, 0, 10, 0);
        lista.SelectionChanged += (_, _) =>
        {
            if (!preenchendo) Mostrar((lista.SelectedItem as ListBoxItem)?.Tag as string);
        };
        Grid.SetRow(lista, 2);
        raiz.Children.Add(lista);

        conteudo.AcceptsReturn = true;
        conteudo.AcceptsTab = true;
        conteudo.TextWrapping = TextWrapping.NoWrap;
        conteudo.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        conteudo.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        conteudo.FontFamily = new System.Windows.Media.FontFamily("Consolas");
        conteudo.FontSize = 12;
        conteudo.Background = Tema.Pincel(Tema.Campo);
        conteudo.Foreground = Tema.Pincel(Tema.Texto);
        conteudo.BorderBrush = Tema.Pincel(Tema.Borda);
        conteudo.IsReadOnly = somenteLeitura;
        conteudo.TextChanged += (_, _) =>
        {
            if (aberto is not null && !somenteLeitura && TipoDe(aberto) == Tipo.Texto) arquivos[aberto] = conteudo.Text;
        };
        Grid.SetRow(conteudo, 2);
        Grid.SetColumn(conteudo, 1);
        raiz.Children.Add(conteudo);

        var barra = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 10, 0, 0),
        };
        Grid.SetRow(barra, 3);
        Grid.SetColumnSpan(barra, 2);

        if (!somenteLeitura)
        {
            barra.Children.Add(Botao(Idioma.T("Novo arquivo"), Novo));
            barra.Children.Add(Botao(Idioma.T("Carregar do computador"), Carregar));
            barra.Children.Add(Botao(Idioma.T("Renomear"), Renomear));
            barra.Children.Add(Botao(Idioma.T("Apagar"), Apagar));
        }

        barra.Children.Add(Botao(Idioma.T("Salvar no computador"), Exportar));

        var fechar = Botao(Idioma.T("Fechar"), Close);
        fechar.HorizontalAlignment = HorizontalAlignment.Right;
        barra.Children.Add(fechar);

        raiz.Children.Add(barra);
        Content = raiz;

        Preencher();
        MostrarPasta();
    }

    void EscolherPasta()
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog
        {
            Title = Idioma.T("A pasta que vira o {0} (as músicas do player, por exemplo)", oQue),
        };
        if (PastaDoComputador is not null && Directory.Exists(PastaDoComputador)) dlg.InitialDirectory = PastaDoComputador;
        if (dlg.ShowDialog(this) != true) return;
        PastaDoComputador = dlg.FolderName;
        MostrarPasta();
    }

    /// <summary>A linha da pasta: o caminho, e quantos arquivos e bytes ela tem (contados sem travar a janela).</summary>
    void MostrarPasta()
    {
        if (tirarPasta is not null) tirarPasta.IsEnabled = PastaDoComputador is not null;
        string pasta = PastaDoComputador;
        if (pasta is null)
        {
            linhaDaPasta.Text = Idioma.T("Sem pasta do computador: o {0} tem só os arquivos da lista.", oQue);
            return;
        }
        if (!Directory.Exists(pasta))
        {
            linhaDaPasta.Text = Idioma.T("Pasta do computador: {0} (NÃO ACHEI ESTA PASTA)", pasta);
            return;
        }
        linhaDaPasta.Text = Idioma.T("Pasta do computador: {0} (contando…)", pasta);
        Task.Run(() =>
        {
            long arquivos = 0, bytes = 0;
            try
            {
                var opcoes = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
                foreach (var f in new DirectoryInfo(pasta).EnumerateFiles("*", opcoes))
                {
                    arquivos++;
                    bytes += f.Length;
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            return (arquivos, bytes);
        }).ContinueWith(t =>
        {
            if (PastaDoComputador != pasta) return;
            linhaDaPasta.Text = Idioma.T("Pasta do computador: {0} ({1:N0} arquivo(s), {2})", pasta, t.Result.arquivos, Acervo.EmTexto(t.Result.bytes));
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    Button Botao(string texto, Action aoClicar)
    {
        var botao = new Button
        {
            Content = texto,
            Style = (Style)FindResource("BotaoPequeno"),
            Margin = new Thickness(0, 0, 8, 0),
        };
        botao.Click += (_, _) => aoClicar();
        return botao;
    }

    Tipo TipoDe(string nome)
        => arquivos.ContainsKey(nome) ? Tipo.Texto
           : doComputador.ContainsKey(nome) ? Tipo.DoComputador
           : gravados.ContainsKey(nome) ? Tipo.Gravado
           : Tipo.BinarioNaPlaca;

    void Preencher(string escolher = null)
    {
        preenchendo = true;
        lista.Items.Clear();

        var nomes = arquivos.Keys
            .Concat(doComputador.Keys)
            .Concat(gravados.Keys)
            .Concat(binariosNaPlaca.Select(b => b.Nome))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase);
        ListBoxItem escolhido = null;
        foreach (string nome in nomes)
        {
            string rotulo = TipoDe(nome) switch
            {
                Tipo.DoComputador => Idioma.T("{0}   ({1}, do computador)", nome, TamanhoDoArquivo(doComputador[nome])),
                Tipo.Gravado => Idioma.T("{0}   ({1}, gravado pelo sketch)", nome, Acervo.EmTexto(gravados[nome].Length)),
                Tipo.BinarioNaPlaca => $"{nome}   ({Acervo.EmTexto(binariosNaPlaca.First(b => b.Nome.Equals(nome, StringComparison.OrdinalIgnoreCase)).Tamanho)})",
                _ => nome,
            };
            var item = new ListBoxItem { Content = rotulo, Tag = nome };
            lista.Items.Add(item);
            if (escolher is not null && nome.Equals(escolher, StringComparison.OrdinalIgnoreCase)) escolhido = item;
        }
        preenchendo = false;

        if (escolhido is not null) lista.SelectedItem = escolhido;
        else if (lista.Items.Count > 0) lista.SelectedIndex = 0;
        else Mostrar(null);
    }

    static string TamanhoDoArquivo(string caminho)
    {
        try { return File.Exists(caminho) ? Acervo.EmTexto(new FileInfo(caminho).Length) : Idioma.T("não achado"); }
        catch (Exception) { return Idioma.T("não achado"); }
    }

    void Mostrar(string nome)
    {
        aberto = nome;
        if (nome is null)
        {
            conteudo.Text = "";
            conteudo.IsEnabled = false;
            return;
        }

        conteudo.IsEnabled = true;

        // O texto do arquivo como ele e (linha longa rola); a explicacao de
        // um arquivo em bytes quebra na largura da janela.
        conteudo.TextWrapping = TipoDe(nome) == Tipo.Texto ? TextWrapping.NoWrap : TextWrapping.Wrap;
        switch (TipoDe(nome))
        {
            case Tipo.Texto:
                conteudo.IsReadOnly = somenteLeitura;
                conteudo.Text = arquivos[nome];
                break;

            case Tipo.DoComputador:
            {
                string caminho = doComputador[nome];
                bool existe = File.Exists(caminho);
                conteudo.IsReadOnly = true;
                conteudo.Text = Idioma.T("Arquivo do computador. Ele entra no {0} do jeito que está, em bytes,\ntoda vez que a simulação começa:\n\n  {1}\n  {2}\n\nO .sktch guarda só o caminho, e não o arquivo: quem abrir o projeto em\noutro computador põe o arquivo dele com \"Carregar do computador\".", oQue, caminho, (existe ? TamanhoDoArquivo(caminho) : Idioma.T("NÃO ACHEI ESTE ARQUIVO: ele fica fora do {0} até você carregar de novo", oQue)));
                break;
            }

            case Tipo.Gravado:
                conteudo.IsReadOnly = true;
                conteudo.Text = Idioma.T("Arquivo em bytes que o sketch gravou no {0}: {1}.\nEle fica no {0} (e vai junto no .sktch) até você apagar, como num {0} de verdade.\n\n{2}", oQue, Acervo.EmTexto(gravados[nome].Length), EmHexa(gravados[nome]));
                break;

            default:
            {
                var b = binariosNaPlaca.First(x => x.Nome.Equals(nome, StringComparison.OrdinalIgnoreCase));
                conteudo.IsReadOnly = true;
                conteudo.Text = Idioma.T("Arquivo em bytes no {0}: {1}.\n\n{2}{3}", oQue, Acervo.EmTexto(b.Tamanho), (b.Origem is null ? Idioma.T("O sketch gravou este arquivo (ele fica no {0} depois de parar).", oQue) : Idioma.T("Veio de {0}", b.Origem)), (lerDaPlaca?.Invoke(nome) is { } bytes ? "\n\n" + EmHexa(bytes) : ""));
                break;
            }
        }
    }

    /// <summary>O comeco do arquivo em hexadecimal, 16 bytes por linha, como um editor de bytes.</summary>
    static string EmHexa(byte[] bytes)
    {
        var texto = new System.Text.StringBuilder();
        int ate = Math.Min(bytes.Length, 512);
        for (int i = 0; i < ate; i += 16)
        {
            int n = Math.Min(16, ate - i);
            texto.Append(i.ToString("X6")).Append("  ");
            for (int k = 0; k < 16; k++) texto.Append(k < n ? bytes[i + k].ToString("X2") + " " : "   ");
            texto.Append(' ');
            for (int k = 0; k < n; k++) texto.Append(bytes[i + k] is >= 32 and < 127 ? (char)bytes[i + k] : '.');
            texto.Append('\n');
        }
        if (bytes.Length > ate) texto.Append(Idioma.T("... (mais {0} bytes)", bytes.Length - ate));
        return texto.ToString();
    }

    void Novo()
    {
        string nome = Perguntar(Idioma.T("Nome do arquivo"), "dados.txt");
        if (nome is null) return;

        doComputador.Remove(nome);
        gravados.Remove(nome);
        arquivos[nome] = "";
        Preencher(nome);
    }

    static readonly HashSet<string> ExtensoesDeTexto = new(
        [".txt", ".csv", ".md", ".json", ".h", ".py", ".ini", ".cfg", ".log", ".htm", ".html", ".xml", ".js", ".css"],
        StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Texto que cabe na janela: extensao de texto, ate 1 MB e sem byte zero
    /// no comeco. O resto (um WAD, uma imagem) entra em bytes, pelo caminho.
    /// </summary>
    static bool PareceTexto(string caminho)
    {
        if (!ExtensoesDeTexto.Contains(Path.GetExtension(caminho))) return false;
        var info = new FileInfo(caminho);
        if (info.Length > 1024 * 1024) return false;
        using var f = File.OpenRead(caminho);
        var inicio = new byte[(int)Math.Min(8192, info.Length)];
        int lidos = f.Read(inicio, 0, inicio.Length);
        return Array.IndexOf(inicio, (byte)0, 0, lidos) < 0;
    }

    void Carregar()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = Idioma.T("Todos os arquivos|*.*|Texto (*.txt;*.csv;*.md;*.json;*.h;*.py)|*.txt;*.csv;*.md;*.json;*.h;*.py|WAD do DOOM (*.wad)|*.wad"),
            Multiselect = true,
        };
        if (dlg.ShowDialog() != true) return;

        string ultimo = null;
        foreach (string arquivo in dlg.FileNames)
        {
            try
            {
                string nome = Path.GetFileName(arquivo);
                gravados.Remove(nome);
                if (PareceTexto(arquivo))
                {
                    doComputador.Remove(nome);
                    arquivos[nome] = File.ReadAllText(arquivo);
                }
                else
                {
                    arquivos.Remove(nome);
                    doComputador[nome] = arquivo;
                }
                ultimo = nome;
            }
            catch (Exception e)
            {
                MessageBox.Show(this, e.Message, Idioma.T("Não deu para ler"),
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        Preencher(ultimo);
    }

    void Renomear()
    {
        if (aberto is null || TipoDe(aberto) == Tipo.BinarioNaPlaca) return;

        string novo = Perguntar(Idioma.T("Novo nome"), aberto);
        if (novo is null || novo == aberto) return;

        if (TipoDe(aberto) == Tipo.Gravado)
        {
            byte[] bytes = gravados[aberto];
            gravados.Remove(aberto);
            arquivos.Remove(novo);
            doComputador.Remove(novo);
            gravados[novo] = bytes;
            Preencher(novo);
            return;
        }

        var dicionario = TipoDe(aberto) == Tipo.Texto ? arquivos : doComputador;
        string valor = dicionario[aberto];
        dicionario.Remove(aberto);
        arquivos.Remove(novo);
        doComputador.Remove(novo);
        gravados.Remove(novo);
        dicionario[novo] = valor;
        Preencher(novo);
    }

    void Apagar()
    {
        if (aberto is null) return;

        arquivos.Remove(aberto);
        doComputador.Remove(aberto);
        gravados.Remove(aberto);
        Preencher();
    }

    void Exportar()
    {
        if (aberto is null) return;

        var tipo = TipoDe(aberto);
        byte[] daPlaca = tipo == Tipo.BinarioNaPlaca ? lerDaPlaca?.Invoke(aberto) : null;
        if (tipo == Tipo.BinarioNaPlaca && daPlaca is null)
        {
            MessageBox.Show(this, Idioma.T("Este arquivo não está mais no {0}.", oQue),
                Idioma.T("Salvar no computador"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dlg = new Microsoft.Win32.SaveFileDialog { FileName = Path.GetFileName(aberto) };
        if (dlg.ShowDialog() != true) return;

        try
        {
            if (tipo == Tipo.Texto) File.WriteAllText(dlg.FileName, arquivos[aberto]);
            else if (tipo == Tipo.Gravado) File.WriteAllBytes(dlg.FileName, gravados[aberto]);
            else if (tipo == Tipo.BinarioNaPlaca) File.WriteAllBytes(dlg.FileName, daPlaca);
            else File.Copy(doComputador[aberto], dlg.FileName, overwrite: true);
        }
        catch (Exception e)
        {
            MessageBox.Show(this, e.Message, Idioma.T("Não deu para gravar"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    string Perguntar(string titulo, string sugestao)
        => Dialogos.Perguntar(this, titulo, sugestao);
}
