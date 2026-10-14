using System.IO;
using System.Threading;
using System.IO.Pipes;
using System.Windows;

namespace Simulador.App;

public partial class App : Application
{
    // ============================================================
    //  Instancia unica.
    //
    //  O aluno vai clicar em Verificar varias vezes seguidas. Cada
    //  clique executa o enviar.bat, que dispara este executavel. Sem
    //  isto, cada clique abriria uma janela nova.
    //
    //  A primeira instancia segura o mutex e escuta um named pipe.
    //  As seguintes so entregam o caminho do sketch e encerram.
    //  Named pipe -- IPC local, sem porta TCP, sem firewall.
    // ============================================================
    const string NomeMutex = "CircuitSketch_Mutex";
    const string NomeCanal = "CircuitSketch_Canal";

    Mutex mutex;
    MainWindow janela;

    /// <summary>
    /// Sem console, uma excecao nao tratada mata a janela sem deixar
    /// rastro. Registrar em arquivo e a unica forma de diagnosticar na
    /// maquina do aluno.
    /// </summary>
    static void Registrar(string origem, object erro)
    {
        try
        {
            string arquivo = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "circuitsketch-erros.log");
            File.AppendAllText(arquivo,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {origem}{Environment.NewLine}{erro}{Environment.NewLine}{Environment.NewLine}");
        }
        catch { /* nada a fazer se nem logar da */ }
    }

    /// <summary>
    /// Fotografa o menu de opcoes.
    ///
    /// O menu e um popup em janela propria, entao nao entra na captura da
    /// janela principal -- e e justamente nele que os problemas de tema
    /// escuro aparecem. Renderizar o proprio controle e a unica forma de
    /// conferir isso sem alguem olhando a tela.
    /// </summary>
    static void CapturarMenu(MainWindow janela, string destino)
    {
        var menu = janela.MenuDeOpcoes;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Absolute;
        menu.HorizontalOffset = 0;
        menu.VerticalOffset = 0;
        menu.IsOpen = true;
        menu.UpdateLayout();

        // O submenu vive num Popup, que e outra janela: nem a captura da
        // janela nem a do menu o alcancam, e um Popup so e medido quando
        // aparece de verdade na tela. Para conferir como os itens ficam,
        // remonta-os num menu proprio -- o template e o mesmo, entao o
        // que se ve aqui e o que o submenu mostra.
        foreach (var item in menu.Items.OfType<System.Windows.Controls.MenuItem>())
        {
            if (item.Items.Count == 0) continue;

            var copia = new System.Windows.Controls.ContextMenu
            {
                Background = menu.Background,
                Foreground = menu.Foreground,
                BorderBrush = menu.BorderBrush,
                MinWidth = 220,
                Placement = System.Windows.Controls.Primitives.PlacementMode.Absolute
            };
            foreach (var filho in item.Items.OfType<System.Windows.Controls.MenuItem>().ToList())
            {
                item.Items.Remove(filho);
                copia.Items.Add(filho);
            }

            copia.IsOpen = true;
            copia.UpdateLayout();

            int lg = (int)Math.Ceiling(copia.ActualWidth);
            int at = (int)Math.Ceiling(copia.ActualHeight);
            if (lg > 0 && at > 0)
            {
                var foto = new System.Windows.Media.Imaging.RenderTargetBitmap(
                    lg, at, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                foto.Render(copia);
                var cod = new System.Windows.Media.Imaging.PngBitmapEncoder();
                cod.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(foto));
                string lado = System.IO.Path.Combine(
                    System.IO.Path.GetDirectoryName(destino) ?? ".",
                    System.IO.Path.GetFileNameWithoutExtension(destino)
                        + "-" + new string([.. item.Header.ToString().ToLowerInvariant()
                              .Where(char.IsLetter)]) + ".png");
                using var arq = File.Create(lado);
                cod.Save(arq);
            }
            copia.IsOpen = false;
        }

        menu.IsOpen = false;
    }

    static void Capturar(Window janela, string destino)
    {
        var alvo = new System.Windows.Media.Imaging.RenderTargetBitmap(
            (int)janela.ActualWidth, (int)janela.ActualHeight,
            96, 96, System.Windows.Media.PixelFormats.Pbgra32);

        janela.UpdateLayout();
        alvo.Render(janela);

        var png = new System.Windows.Media.Imaging.PngBitmapEncoder();
        png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(alvo));

        using var arquivo = File.Create(destino);
        png.Save(arquivo);
    }

    /// <summary>
    /// Aperta Rodar, espera, aperta Parar, espera e aperta Rodar de novo,
    /// anotando o estado em cada etapa. E a unica forma de conferir os
    /// botoes sem alguem clicando neles.
    /// </summary>
    void RodarFluxo(string sketch, string destino)
    {
        var relatorio = new System.Text.StringBuilder();
        var janela = new MainWindow();
        if (sketch is not null && !sketch.StartsWith("--")) janela.CarregarSketch(sketch);
        janela.Show();

        var etapas = new Queue<(int esperaMs, string rotulo, Action acao)>();
        etapas.Enqueue((300, "inicio", null));
        etapas.Enqueue((2200, "depois de RODAR", janela.TesteRodar));
        etapas.Enqueue((800, "depois de PARAR", janela.TesteParar));
        etapas.Enqueue((1800, "depois de RODAR de novo", janela.TesteRodar));
        etapas.Enqueue((600, "depois de PARAR de novo", janela.TesteParar));

        var passo = new System.Windows.Threading.DispatcherTimer();

        void Proximo()
        {
            if (etapas.Count == 0)
            {
                try { File.WriteAllText(destino, relatorio.ToString()); }
                catch (Exception ex) { Registrar("RodarFluxo", ex); }
                Shutdown();
                return;
            }

            var (espera, rotulo, acao) = etapas.Dequeue();
            acao?.Invoke();
            passo.Interval = TimeSpan.FromMilliseconds(espera);
            passo.Tag = rotulo;
            passo.Start();
        }

        passo.Tick += (_, _) =>
        {
            passo.Stop();
            relatorio.AppendLine($"{passo.Tag,-26} {janela.TesteEstado()}");
            Proximo();
        };

        Proximo();
    }

    void AoIniciar(object sender, StartupEventArgs e)
    {
        // Autoteste, passeio e fluxo rodam o programa de verdade -- e
        // gravariam nas preferencias e nos recentes de quem os rodou.
        // Tem que ser a primeira coisa: as preferencias ficam em memoria
        // depois da primeira leitura.
        if (e.Args.Any(a => a is "--teste" or "--passeio" or "--fluxo"))
        {
            Armazem.Isolar();
            Exit += (_, _) => Armazem.Descartar();
        }

        // O idioma (Alpha 25): o escolhido nas configuracoes. Enquanto os
        // catalogos estao pela metade, o padrao e o portugues original (e nao
        // o do Windows, que daria metade da tela em cada lingua). Os testes
        // conferem os textos em portugues: neles, sempre o original.
        Idioma.Usar(Armazem.Isolado ? Idioma.Original : Preferencias.Texto(Preferencias.Idioma, Idioma.Original));

        DispatcherUnhandledException += (_, ev) =>
        {
            Registrar("DispatcherUnhandledException", ev.Exception);

            // No passeio ninguem esta olhando para clicar no OK: a caixa
            // prenderia o roteiro inteiro. La a excecao vai para o
            // relatorio, que e o lugar dela.
            if (!Passeio.Ativo)
                MessageBox.Show(ev.Exception.Message, "Erro no simulador",
                                MessageBoxButton.OK, MessageBoxImage.Error);
            ev.Handled = true;                 // nao derruba a janela
        };

        AppDomain.CurrentDomain.UnhandledException += (_, ev) =>
            Registrar("UnhandledException", ev.ExceptionObject);

        TaskScheduler.UnobservedTaskException += (_, ev) =>
            Registrar("UnobservedTaskException", ev.Exception);

        // Modo fluxo: exercita rodar/parar/rodar sem ninguem clicar.
        int iFluxo = Array.IndexOf(e.Args, "--fluxo");
        if (iFluxo >= 0 && iFluxo + 1 < e.Args.Length)
        {
            RodarFluxo(e.Args[0], e.Args[iFluxo + 1]);
            return;
        }

        // Modo audio: toca uma escala curta e diz se a placa de som
        // respondeu. Serve para conferir uma maquina do laboratorio sem
        // depender de alguem descrever o que ouviu.
        int iAudio = Array.IndexOf(e.Args, "--audio");
        if (iAudio >= 0)
        {
            var buzina = new Buzina();
            var relato = new System.Text.StringBuilder();
            foreach (int hz in new[] { 262, 330, 392, 523 })
            {
                buzina.Definir(hz);
                Thread.Sleep(250);
                relato.AppendLine($"{hz,5} Hz  saida {(buzina.Ativa ? "aberta" : "MUDA")}");
            }
            buzina.Parar();
            Thread.Sleep(120);
            relato.AppendLine(buzina.Ativa
                ? "placa de som respondeu"
                : "sem saida de audio nesta maquina");
            buzina.Dispose();

            if (iAudio + 1 < e.Args.Length && !e.Args[iAudio + 1].StartsWith("--"))
                File.WriteAllText(e.Args[iAudio + 1], relato.ToString());
            Shutdown(0);
            return;
        }

        // Roda um .sktch inteiro sem abrir janela e conta o que
        // aconteceu: serial, pinos, display, fita. Serve para conferir
        // uma pasta de sketches de uma vez, que e o que o motor sozinho
        // nao consegue -- ele roda o codigo, mas sem o circuito.
        int iDiag = Array.IndexOf(e.Args, "--diagnostico");
        if (iDiag >= 0 && iDiag + 1 < e.Args.Length)
        {
            long msDiag = iDiag + 2 < e.Args.Length
                          && long.TryParse(e.Args[iDiag + 2], out long lido) ? lido : 2000;

            string relatorio;
            try { relatorio = Diagnostico.Rodar(e.Args[iDiag + 1], msDiag); }
            catch (Exception erro) { relatorio = $"ESTOUROU: {erro.GetType().Name}: {erro.Message}"; }

            int iOndeDiag = Array.IndexOf(e.Args, "--saida");
            if (iOndeDiag >= 0 && iOndeDiag + 1 < e.Args.Length)
                File.WriteAllText(e.Args[iOndeDiag + 1], relatorio);

            Shutdown(0);
            return;
        }

        // Confere um diagram.json sem abrir a janela: diz o que entrou e,
        // principalmente, o que NAO entrou. Serve para descobrir num
        // arquivo grande qual peca ou pino ficou pelo caminho, que era
        // exatamente o que nao dava para ver so olhando a tela.
        int iWokwi = Array.IndexOf(e.Args, "--wokwi");
        if (iWokwi >= 0 && iWokwi + 1 < e.Args.Length)
        {
            var relato = new System.Text.StringBuilder();
            try
            {
                var r = ImportarWokwi.Ler(File.ReadAllText(e.Args[iWokwi + 1]));
                relato.AppendLine($"pecas: {r.Circuito.Pecas.Count}");
                foreach (var grupo in r.Circuito.Pecas.GroupBy(x => x.Tipo))
                    relato.AppendLine($"  {grupo.Count(),3}x {grupo.Key}");

                relato.AppendLine($"fios: {r.Circuito.Fios.Count}");
                foreach (var f in r.Circuito.Fios)
                {
                    string nome(string id) => r.Circuito.Pecas
                        .FirstOrDefault(x => x.Id == id)?.Tipo ?? "?";
                    relato.AppendLine(
                        $"  {nome(f.DePeca)}:{f.DePino} -> {nome(f.ParaPeca)}:{f.ParaPino}"
                        + $"  [{f.Dobras.Count} dobra(s), {f.Estilo}]");
                }

                relato.AppendLine($"tipos sem equivalente: {r.Ignorados.Count}");
                foreach (string t in r.Ignorados) relato.AppendLine("  " + t);

                relato.AppendLine($"fios perdidos: {r.FiosPerdidos.Count}");
                foreach (string t in r.FiosPerdidos) relato.AppendLine("  " + t);

                var quebrados = r.Circuito.FiosQuebrados().ToList();
                relato.AppendLine($"fios quebrados: {quebrados.Count}");
                foreach (string t in quebrados) relato.AppendLine("  " + t);

                // O mesmo circuito escrito de volta, para comparar com o original.
                relato.AppendLine("--- escrito de volta ---");
                relato.Append(ExportarWokwi.Gerar(r.Circuito));
            }
            catch (Exception ex) { relato.AppendLine("ERRO: " + ex.Message); }

            int iOnde = Array.IndexOf(e.Args, "--saida");
            if (iOnde >= 0 && iOnde + 1 < e.Args.Length)
                File.WriteAllText(e.Args[iOnde + 1], relato.ToString());

            // "--para" grava o circuito importado como .sktch, para abrir
            // depois sem repetir a importacao.
            int iPara = Array.IndexOf(e.Args, "--para");
            if (iPara >= 0 && iPara + 1 < e.Args.Length)
            {
                try
                {
                    var r2 = ImportarWokwi.Ler(File.ReadAllText(e.Args[iWokwi + 1]));
                    Sktch.Salvar(e.Args[iPara + 1], r2.Circuito,
                        "void setup() {}" + Environment.NewLine + "void loop() {}");
                }
                catch (Exception ex) { Registrar("ConverterWokwi", ex); }
            }

            Shutdown(0);
            return;
        }

        // Completa o boards.txt do plugin da Arduino IDE com as placas
        // que ainda nao estao nele, tiradas dos perfis do simulador.
        int iIde = Array.IndexOf(e.Args, "--placas-da-ide");
        if (iIde >= 0 && iIde + 1 < e.Args.Length)
        {
            try { PlacasDaIde.Completar(e.Args[iIde + 1]); Shutdown(0); }
            catch (Exception ex) { Registrar("PlacasDaIde", ex); Shutdown(1); }
            return;
        }

        // Modo passeio: percorre a interface de verdade e fotografa cada
        // tela. E o que pega o defeito que so aparece olhando -- o
        // painel escondido, o texto cortado, a janela solta vazia.
        int iPasseio = Array.IndexOf(e.Args, "--passeio");
        if (iPasseio >= 0 && iPasseio + 1 < e.Args.Length)
        {
            Passeio.Comecar(this, e.Args[iPasseio + 1]);
            return;
        }

        // Modo teste: roda as verificacoes, escreve o resultado e sai.
        if (Array.IndexOf(e.Args, "--teste") >= 0)
        {
            // Os testes abrem e fecham janelas uma depois da outra. No modo de
            // sempre, fechar a ultima pede para o programa sair -- e o pedido,
            // atendido mais tarde, fechava a janela do teste SEGUINTE no meio
            // da compilacao (o player parava antes do setup). Aqui quem sai e
            // o Shutdown do fim.
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Autoteste.Rodando = true;
            int iSo = Array.IndexOf(e.Args, "--so");
            if (iSo >= 0 && iSo + 1 < e.Args.Length) Autoteste.Filtro = e.Args[iSo + 1];
            string relatorio = Autoteste.Rodar();
            int iSaida = Array.IndexOf(e.Args, "--saida");
            if (iSaida >= 0 && iSaida + 1 < e.Args.Length)
                File.WriteAllText(e.Args[iSaida + 1], relatorio);
            Shutdown(relatorio.Contains("FALHA") ? 1 : 0);
            return;
        }

        string sketch = e.Args.Length > 0 && !e.Args[0].StartsWith("--")
            ? e.Args[0] : null;

        // Placa escolhida na Arduino IDE. Quem manda na simulacao e a
        // placa do CIRCUITO; isto serve so para avisar quando as duas
        // discordam, que e o tipo de confusao dificil de descobrir.
        int iPlaca = Array.IndexOf(e.Args, "--placa");
        string placaDaIde = iPlaca >= 0 && iPlaca + 1 < e.Args.Length
            ? e.Args[iPlaca + 1] : null;

        mutex = new Mutex(true, NomeMutex, out bool souOPrimeiro);

        if (!souOPrimeiro)
        {
            EntregarParaInstanciaAberta(sketch);
            Shutdown();
            return;
        }

        // Modo captura: abre, espera renderizar, salva um PNG e sai.
        // Serve para conferir a tela sem precisar olhar o monitor --
        // util em teste automatizado e para revisar o visual.
        int iCaptura = Array.IndexOf(e.Args, "--captura");
        if (iCaptura >= 0 && iCaptura + 1 < e.Args.Length)
        {
            string destino = e.Args[iCaptura + 1];
            sketch = iCaptura > 0 ? e.Args[0] : null;

            janela = new MainWindow();

            int iCirc = Array.IndexOf(e.Args, "--circuito");
            if (iCirc >= 0 && iCirc + 1 < e.Args.Length)
                janela.CarregarCircuitoDe(e.Args[iCirc + 1]);

            if (sketch != null && !sketch.StartsWith("--")) janela.CarregarSketch(sketch);
            janela.Show();

            // "--rodar" liga a simulacao antes da foto. Sem isto, o que
            // aparece e o circuito parado -- e o que precisa ser visto
            // (LED aceso, peca queimada) so existe rodando.
            // "--pausa 12" marca um ponto de parada antes de rodar, para a
            // foto mostrar o depurador parado.
            int iPausa = Array.IndexOf(e.Args, "--pausa");
            if (iPausa >= 0 && iPausa + 1 < e.Args.Length)
                foreach (string textoDaPausa in e.Args[iPausa + 1].Split(','))
                    if (int.TryParse(textoDaPausa, out int linhaDaPausa))
                        janela.TesteMarcarPausa(linhaDaPausa);

            // "--aba rede" mostra uma das abas do painel de baixo na foto.
            int iAba = Array.IndexOf(e.Args, "--aba");
            if (iAba >= 0 && iAba + 1 < e.Args.Length) janela.TesteMostrarAba(e.Args[iAba + 1]);

            if (Array.IndexOf(e.Args, "--rodar") >= 0)
                janela.Dispatcher.BeginInvoke(
                    () => janela.TesteRodar(),
                    System.Windows.Threading.DispatcherPriority.Loaded);

            // Duas etapas de proposito: o estado dos controles precisa ser
            // aplicado DEPOIS que o sketch comecou a rodar, porque Rodar()
            // chama Reiniciar() e zera os pinos. Depois espera o efeito
            // aparecer na tela antes de fotografar.
            // "--olhar x,y,zoom": a foto sai de perto, centrada nesse
            // ponto do circuito. E como se revisa arte de peca.
            int iOlhar = Array.IndexOf(e.Args, "--olhar");
            if (iOlhar >= 0 && iOlhar + 1 < e.Args.Length)
            {
                var n = e.Args[iOlhar + 1].Split(',');
                double Ler(int k, double padrao)
                    => k < n.Length && double.TryParse(n[k],
                        System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out double v)
                        ? v : padrao;

                janela.Dispatcher.BeginInvoke(
                    () => janela.TesteOlharPara(Ler(0, 0), Ler(1, 0), Ler(2, 4)),
                    System.Windows.Threading.DispatcherPriority.Loaded);
            }

            // "--janela abertura" fotografa uma janela do programa em vez
            // do circuito. Serve para revisar o visual dela sem abrir o
            // programa a mao.
            int iJanela = Array.IndexOf(e.Args, "--janela");
            if (iJanela >= 0 && iJanela + 1 < e.Args.Length)
            {
                string qual = e.Args[iJanela + 1];
                janela.Dispatcher.BeginInvoke(() =>
                {
                    if (qual == "configuracoes")
                    {
                        // Esta abre modal; a foto sai do mesmo jeito,
                        // porque o relogio da captura continua correndo.
                        janela.TesteAbrirConfiguracoes();
                        return;
                    }

                    if (qual != "abertura") return;

                    var abertura = new JanelaAbertura(janela);
                    abertura.Show();
                    abertura.UpdateLayout();
                }, System.Windows.Threading.DispatcherPriority.Loaded);
            }

            int iModo = Array.IndexOf(e.Args, "--modo");
            if (iModo >= 0 && iModo + 1 < e.Args.Length)
            {
                string modo = e.Args[iModo + 1];
                janela.Dispatcher.BeginInvoke(
                    () => janela.MostrarModo(modo),
                    System.Windows.Threading.DispatcherPriority.Loaded);
            }

            int iTema = Array.IndexOf(e.Args, "--tema");
            if (iTema >= 0 && iTema + 1 < e.Args.Length) Tema.Aplicar(e.Args[iTema + 1]);

            int iMenu = Array.IndexOf(e.Args, "--menu");

            int iFio = Array.IndexOf(e.Args, "--fio");
            if (iFio >= 0 && iFio + 1 < e.Args.Length)
                janela.SelecionarFios(e.Args[iFio + 1].Split(',')
                    .Select(v => int.TryParse(v, out int n) ? n : -1)
                    .Where(n => n >= 0));

            int iSel = Array.IndexOf(e.Args, "--selecionar");
            if (iSel >= 0 && iSel + 1 < e.Args.Length
                )
            {
                var quais = e.Args[iSel + 1].Split(',')
                    .Select(x => int.TryParse(x, out int n) ? n : -1)
                    .Where(n => n >= 0);
                janela.SelecionarVarias(quais);
            }

            var prepara = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(3)
            };
            prepara.Tick += (_, _) =>
            {
                prepara.Stop();

                var espera = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(900)
                };
                espera.Tick += (_, _) =>
                {
                    espera.Stop();
                    if (iMenu >= 0 && iMenu + 1 < e.Args.Length)
                        CapturarMenu(janela, e.Args[iMenu + 1]);
                    try
                    {
                        // Com uma janela avulsa aberta, e ela que
                        // interessa na foto.
                        var alvo = Current.Windows.OfType<Window>()
                            .LastOrDefault(w => w.IsVisible && w != janela) ?? janela;
                        Capturar(alvo, destino);
                    }
                    catch (Exception ex) { Registrar("Capturar", ex); }
                    Shutdown();
                };
                espera.Start();
            };
            prepara.Start();
            return;
        }

        Tema.AplicarSalvo();

        janela = new MainWindow();
        janela.PlacaEscolhidaNaIde = placaDaIde;
        if (sketch != null) janela.CarregarSketch(sketch);
        janela.Show();

        // Reabrir o ultimo projeto, se as configuracoes pedirem -- e se
        // ele ainda existir. E o lugar da tela de abertura.
        string ultimo = Preferencias.Texto(Preferencias.UltimoProjeto);
        if (sketch is null && Preferencias.Ligado(Preferencias.ReabrirUltimo, false)
            && ultimo.Length > 0 && File.Exists(ultimo))
        {
            janela.Dispatcher.BeginInvoke(
                () => janela.AbrirProjeto(ultimo),
                System.Windows.Threading.DispatcherPriority.Loaded);
        }

        // A tela de abertura so aparece quando o programa foi aberto
        // sozinho: clicar num .ino pela IDE ja disse o que se quer.
        else if (sketch is null && Preferencias.Ligado(Preferencias.MostrarAbertura, true))
        {
            // Depois que a janela principal terminou de montar: a
            // abertura e um dialogo por cima dela, e dialogo sobre
            // janela pela metade pisca feio.
            janela.Dispatcher.BeginInvoke(
                () => MostrarAbertura(janela),
                System.Windows.Threading.DispatcherPriority.Loaded);
        }

        var escuta = new Thread(Escutar) { IsBackground = true };
        escuta.Start();
    }

    /// <summary>
    /// A tela de abertura, e o que fazer com a escolha dela.
    ///
    /// Fecha sem escolher nada? Fica o circuito de exemplo, que e o que
    /// o programa sempre abriu.
    /// </summary>
    static void MostrarAbertura(MainWindow janela)
    {
        try
        {
            var abertura = new JanelaAbertura(janela);
            abertura.ShowDialog();

            if (abertura.PlacaEscolhida is { } placa)
                janela.ComecarComPlaca(placa);
            else if (abertura.ProjetoEscolhido is { } projeto)
                janela.AbrirProjeto(projeto);
        }
        catch (Exception ex)
        {
            // Tela de abertura nao e motivo para o programa nao abrir.
            Registrar("MostrarAbertura", ex);
        }
    }

    static void EntregarParaInstanciaAberta(string sketch)
    {
        try
        {
            using var canal = new NamedPipeClientStream(".", NomeCanal, PipeDirection.Out);
            canal.Connect(2000);
            using var escritor = new StreamWriter(canal) { AutoFlush = true };
            escritor.WriteLine(sketch ?? "");
        }
        catch
        {
            // A instancia aberta pode estar fechando. Nada a fazer:
            // o aluno clica em Verificar de novo.
        }
    }

    void Escutar()
    {
        while (true)
        {
            try
            {
                using var canal = new NamedPipeServerStream(
                    NomeCanal, PipeDirection.In, 1,
                    PipeTransmissionMode.Byte, PipeOptions.None);

                canal.WaitForConnection();

                using var leitor = new StreamReader(canal);
                string sketch = leitor.ReadLine();
                if (string.IsNullOrWhiteSpace(sketch)) continue;

                Dispatcher.Invoke(() =>
                {
                    janela.CarregarSketch(sketch);

                    // Traz para a frente: o aluno acabou de pedir isso.
                    if (janela.WindowState == WindowState.Minimized)
                        janela.WindowState = WindowState.Normal;
                    janela.Activate();
                });
            }
            catch
            {
                // Encerrando o app: sai do laco.
                return;
            }
        }
    }
}
