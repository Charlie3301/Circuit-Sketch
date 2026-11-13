using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace Simulador.App;

/// <summary>
/// Os botoes do canto direito: Verificar (o visto) e Mandar para a placa
/// (a seta), como os da Arduino IDE, e a aba PLACA embaixo, com as portas
/// USB e o que o compilador do Arduino escreve.
/// </summary>
public partial class MainWindow
{
    /// <summary>A compilacao ou o envio em andamento, para o segundo clique cancelar.</summary>
    CancellationTokenSource trabalhoDaPlaca;

    /// <summary>A porta que a pessoa escolheu (ou a unica que apareceu).</summary>
    string portaEscolhida;

    List<PlacaDeVerdade.Porta> portasVistas = [];

    readonly List<string> linhasDaPlaca = [];

    /// <summary>O ultimo resultado, para o teste.</summary>
    internal PlacaDeVerdade.Resultado TesteUltimoDaPlaca { get; private set; }

    internal bool TesteTrabalhandoNaPlaca => trabalhoDaPlaca is not null;

    internal void TesteVerificar() => Verificar();

    internal string TesteLogDaPlaca => string.Join("\n", linhasDaPlaca);

    // =================================================================
    //  Verificar
    // =================================================================

    /// <summary>
    /// O visto: o simulador confere na hora e, com a Arduino IDE instalada,
    /// o compilador de verdade confere tambem -- o que passa nos dois roda
    /// no simulador E compila para a placa.
    /// </summary>
    async void Verificar(object s = null, RoutedEventArgs e = null)
    {
        if (trabalhoDaPlaca is not null) { CancelarNaPlaca(); return; }
        if (verificando) return;

        GuardarCodigoDoEditor();
        var placa = PlacaDoEditor();
        if (placa is null) { Erro(Idioma.T("não há placa no circuito para verificar.")); return; }

        var (doSimulador, simuladorAceitou) = await ConferirNoSimulador(placa);

        string fqbn = PlacaDeVerdade.FqbnDe(placa.Tipo);
        if (PorQueNaoCompila(placa, fqbn, semCompilador: false) is null) await GarantirCompilador();
        string porQueNao = PorQueNaoCompila(placa, fqbn);
        if (porQueNao is not null)
        {
            EstadoDaPlaca(placa, porQueNao);
            if (simuladorAceitou) Aviso(doSimulador + ". " + porQueNao);
            return;
        }

        if (simuladorAceitou) Aviso(doSimulador + Idioma.T("; o compilador do Arduino confere agora (aba PLACA)…"));
        ComecarNaPlaca(placa, fqbn, null);
    }

    /// <summary>A conferencia do simulador em andamento: o segundo clique no visto espera por ela.</summary>
    bool verificando;

    /// <summary>
    /// A conferencia do simulador: compila e ve se cabe. Devolve a frase do resultado. Como no
    /// Rodar, o C++ compilado pode levar minutos na primeira vez: a janela continua respondendo
    /// (os testes e o passeio esperam ali mesmo).
    /// </summary>
    async Task<(string Frase, bool Aceitou)> ConferirNoSimulador(PecaNoCircuito placa)
    {
        try
        {
            var projeto = ProjetoDaPlaca(placa);
            Programa prog;
            if (Autoteste.Rodando || Passeio.Ativo) prog = Compilador.CompilarProjeto(projeto);
            else
            {
                verificando = true;
                console.Info(Idioma.T("verificando {0}…", NomeDaPlaca(placa)));
                try { prog = await Task.Run(() => Compilador.CompilarProjeto(projeto)); }
                finally { verificando = false; }
            }
            foreach (string aviso in prog.Avisos) console.Aviso(aviso);

            if (PerfilPlaca.De(placa.Tipo) is { } perfil)
            {
                var conta = Memoria.Estimar(prog, perfil);
                if (conta.Estourou)
                {
                    Erro(Idioma.T("{0}: não cabe na placa — {1}", NomeDaPlaca(placa), conta.PorQueNaoCabe()));
                    return (null, false);
                }
            }

            return (Idioma.T("{0}: o simulador aceitou o código", NomeDaPlaca(placa)), true);
        }
        catch (Exception ex) when (ex is ErroDeSintaxe or ErroDeExecucao)
        {
            Erro($"{NomeDaPlaca(placa)}: {ex.Message}");
            return (null, false);
        }
    }

    /// <summary>
    /// Por que o compilador de verdade nao entra aqui, ou null quando entra.
    /// Com <paramref name="semCompilador"/> falso, so a linguagem e a placa
    /// contam (para saber se vale oferecer o download do compilador).
    /// </summary>
    static string PorQueNaoCompila(PecaNoCircuito placa, string fqbn, bool semCompilador = true)
    {
        var linguagem = Compilador.Detectar(placa.Sketch ?? "");
        if (linguagem == Linguagem.MicroPython)
            return Idioma.T("MicroPython não passa pelo compilador do Arduino: na placa de verdade ele vai pelo Thonny ou pelo mpremote");
        if (linguagem != Linguagem.Arduino)
            return Idioma.T("o compilador do Arduino é para C++; {0} fica só no simulador", Compilador.Nome(linguagem));
        if (fqbn is null)
            return Idioma.T("a {0} não tem um núcleo do Arduino para compilar", PerfilPlaca.De(placa.Tipo)?.Nome ?? placa.Tipo);
        if (semCompilador && PlacaDeVerdade.Cli is null)
            return Idioma.T("para compilar e mandar para a placa de verdade, o CircuitSketch precisa do compilador do Arduino: instale a Arduino IDE 2 ou baixe o arduino-cli pelo menu USB, ao lado do visto");
        return null;
    }

    /// <summary>
    /// Sem a Arduino IDE (nem um arduino-cli no PATH): oferece baixar o
    /// arduino-cli oficial. Devolve se ha compilador no fim.
    /// </summary>
    async Task<bool> GarantirCompilador()
    {
        if (PlacaDeVerdade.Cli is not null) return true;
        if (Autoteste.Rodando || Passeio.Ativo) return false;

        if (MessageBox.Show(this,
                Idioma.T("Para compilar e mandar o código para uma placa de verdade, o CircuitSketch usa o compilador oficial do Arduino (arduino-cli, cerca de 20 MB, de downloads.arduino.cc). A Arduino IDE 2 já traz um, mas ela não foi encontrada.\n\nBaixar o arduino-cli agora?"),
                Idioma.T("Compilador do Arduino"), MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return false;

        return await BaixarCompilador();
    }

    /// <summary>Baixa o arduino-cli com o andamento na aba PLACA.</summary>
    async Task<bool> BaixarCompilador()
    {
        MostrarAba("placa");
        linhasDaPlaca.Clear();
        var cancelar = new CancellationTokenSource();
        trabalhoDaPlaca = cancelar;
        AtualizarBotoesDaPlaca();
        EstadoDaPlaca(PlacaDoEditor(), Idioma.T("baixando o compilador do Arduino (arduino-cli)…"));
        bool deu;
        try
        {
            deu = await PlacaDeVerdade.BaixarCli(l => Dispatcher.BeginInvoke(() => LogDaPlaca(l)), cancelar.Token);
        }
        finally
        {
            if (trabalhoDaPlaca == cancelar) trabalhoDaPlaca = null;
            AtualizarBotoesDaPlaca();
        }
        EstadoDaPlaca(PlacaDoEditor(), deu
            ? Idioma.T("compilador do Arduino pronto: o visto e a seta já funcionam com a placa de verdade")
            : Idioma.T("o compilador do Arduino não foi instalado (veja a aba PLACA)"));
        if (deu) _ = ProcurarPortas();
        return deu;
    }

    // =================================================================
    //  Mandar para a placa
    // =================================================================

    /// <summary>
    /// A seta: compila com o compilador do Arduino e grava na placa ligada
    /// na USB. Pergunta antes -- o programa que estava na placa vai embora.
    /// </summary>
    async void MandarParaPlaca(object s = null, RoutedEventArgs e = null)
    {
        if (trabalhoDaPlaca is not null) { CancelarNaPlaca(); return; }

        GuardarCodigoDoEditor();
        var placa = PlacaDoEditor();
        if (placa is null) { Erro(Idioma.T("não há placa no circuito para mandar.")); return; }

        MostrarAba("placa");
        string fqbn = PlacaDeVerdade.FqbnDe(placa.Tipo);
        if (PorQueNaoCompila(placa, fqbn, semCompilador: false) is null) await GarantirCompilador();
        if (PorQueNaoCompila(placa, fqbn) is { } porQueNao)
        {
            EstadoDaPlaca(placa, porQueNao);
            Erro(porQueNao + ".");
            return;
        }

        EstadoDaPlaca(placa, Idioma.T("procurando as placas ligadas na USB…"));
        AplicarPortas(await Task.Run(PlacaDeVerdade.Portas));

        if (portasVistas.Count == 0)
        {
            string nenhuma = Idioma.T("nenhuma placa na USB: ligue a placa no computador (com um cabo de dados, e não só de carga) e aperte a seta de novo");
            EstadoDaPlaca(placa, nenhuma);
            Erro(nenhuma + ".");
            AtualizarRotuloDaPorta();
            return;
        }

        var porta = portasVistas.FirstOrDefault(p => p.Endereco == portaEscolhida)
                    ?? (portasVistas.Count == 1
                        ? portasVistas[0]
                        : portasVistas.FirstOrDefault(p => p.Fqbn == fqbn));
        if (porta is null)
        {
            EstadoDaPlaca(placa, Idioma.T("há {0} portas na USB: escolha a da placa no menu USB, ao lado do visto", portasVistas.Count));
            AtualizarRotuloDaPorta();
            AbrirMenuDasPortas(btPlacaUsb);
            return;
        }

        portaEscolhida = porta.Endereco;
        AtualizarRotuloDaPorta();

        if (!Autoteste.Rodando && !Passeio.Ativo)
        {
            string detectada = porta.Placa ?? Idioma.T("placa que o Windows não identificou");
            bool outra = porta.Fqbn is not null && porta.Fqbn != fqbn;
            string pergunta = Idioma.T("Mandar o código da {0} para a {1} ({2})?{3}\n\nO programa que está na placa vai ser trocado por este.", NomeDaPlaca(placa), porta.Endereco, detectada, (outra
                                  ? Idioma.T("\n\nAtenção: nessa porta parece estar um(a) {0}, e o código é para {1} ({2}).", porta.Placa, NomeDaPlaca(placa), fqbn)
                                  : ""));
            if (MessageBox.Show(this, pergunta, Idioma.T("Mandar para a placa"), MessageBoxButton.YesNo,
                                outra ? MessageBoxImage.Warning : MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;
        }

        ComecarNaPlaca(placa, fqbn, porta.Endereco);
    }

    // =================================================================
    //  as portas USB
    // =================================================================

    /// <summary>Olha a USB a cada 2 s: o Windows avisa no registro quando uma porta COM aparece ou some.</summary>
    System.Windows.Threading.DispatcherTimer vigiaDaUsb;

    string assinaturaDaUsb;
    bool procurandoPortas, procurarDeNovo;

    /// <summary>Liga a vigia da USB (na abertura da janela; o autoteste nao precisa dela).</summary>
    void ComecarVigiaDaUsb()
    {
        Closed += (_, _) => { vigiaDaUsb?.Stop(); FecharMonitorDaPlaca(); };
        if (Autoteste.Rodando || Passeio.Ativo) return;

        assinaturaDaUsb = PlacaDeVerdade.AssinaturaDasPortas();
        vigiaDaUsb = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        vigiaDaUsb.Tick += (_, _) =>
        {
            string agora = PlacaDeVerdade.AssinaturaDasPortas();
            if (agora == assinaturaDaUsb) return;
            assinaturaDaUsb = agora;
            _ = ProcurarPortas();
        };
        vigiaDaUsb.Start();
        _ = ProcurarPortas();
    }

    /// <summary>
    /// Atualiza a lista: primeiro o que o Windows sabe (na hora), depois o
    /// que o arduino-cli reconhece (a placa e o FQBN, leva um segundo).
    /// </summary>
    async Task ProcurarPortas()
    {
        if (procurandoPortas) { procurarDeNovo = true; return; }
        procurandoPortas = true;
        try
        {
            do
            {
                procurarDeNovo = false;
                AplicarPortas(await Task.Run(PlacaDeVerdade.PortasDoWindows), soDoWindows: true);
                if (PlacaDeVerdade.Cli is not null) AplicarPortas(await Task.Run(PlacaDeVerdade.Portas));
            } while (procurarDeNovo);
        }
        finally { procurandoPortas = false; }
    }

    /// <summary>
    /// Troca a lista de portas. A escolhida continua escolhida (a placa que
    /// sai e volta na USB volta na mesma COM); sem escolha, fica a unica placa
    /// reconhecida, ou a que acabou de ser ligada -- como na Arduino IDE.
    /// </summary>
    void AplicarPortas(List<PlacaDeVerdade.Porta> portas, bool soDoWindows = false)
    {
        // A lista do Windows nao sabe o FQBN: guarda o que o arduino-cli ja disse.
        if (soDoWindows)
            portas = [.. portas.Select(p => portasVistas.FirstOrDefault(v => v.Endereco == p.Endereco) is { } vista
                                            ? p with { Placa = vista.Placa ?? p.Placa, Fqbn = vista.Fqbn }
                                            : p)];

        var antes = portasVistas.Select(p => p.Endereco).ToHashSet();
        portasVistas = portas;

        if (portaEscolhida is null || portas.All(p => p.Endereco != portaEscolhida))
        {
            var reconhecidas = portas.Where(p => p.Placa is not null).ToList();
            var nova = reconhecidas.Where(p => !antes.Contains(p.Endereco)).ToList();
            if (nova.Count == 1 && antes.Count > 0) portaEscolhida = nova[0].Endereco;
            else if (portaEscolhida is null && reconhecidas.Count == 1) portaEscolhida = reconhecidas[0].Endereco;
        }
        AtualizarRotuloDaPorta();
    }

    void AbrirMenuDasPortas(object s = null, RoutedEventArgs e = null)
    {
        var alvo = s as FrameworkElement ?? btPlacaUsb;
        AplicarPortas(PlacaDeVerdade.PortasDoWindows(), soDoWindows: true);
        var menu = MenuDasPortas();
        menu.PlacementTarget = alvo;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        alvo.ContextMenu = menu;
        menu.IsOpen = true;
        _ = ProcurarPortas();
    }

    /// <summary>As portas, a busca de novo, o compilador e o monitor da placa.</summary>
    ContextMenu MenuDasPortas()
    {
        var menu = new ContextMenu();
        menu.Items.Add(new MenuItem { Header = Idioma.T("Placas na USB"), IsEnabled = false, FontWeight = FontWeights.SemiBold });

        if (portasVistas.Count == 0)
            menu.Items.Add(new MenuItem { Header = Idioma.T("nenhuma placa na USB: ligue a placa com um cabo de dados"), IsEnabled = false });

        foreach (var porta in portasVistas)
        {
            var item = new MenuItem
            {
                Header = porta.Placa is null ? porta.Endereco : $"{porta.Endereco} — {porta.Placa}",
                IsCheckable = true,
                IsChecked = porta.Endereco == portaEscolhida,
            };
            if (porta.Fqbn is not null) item.ToolTip = porta.Fqbn;
            string endereco = porta.Endereco;
            item.Click += (_, _) =>
            {
                if (monitorDaPlaca is not null && monitorDaPlaca.Porta != endereco) FecharMonitorDaPlaca();
                portaEscolhida = endereco;
                AtualizarRotuloDaPorta();
            };
            menu.Items.Add(item);
        }

        menu.Items.Add(new Separator());
        var procurar = new MenuItem { Header = Idioma.T("Procurar de novo") };
        procurar.Click += (_, _) => _ = ProcurarPortas();
        menu.Items.Add(procurar);

        var monitor = new MenuItem
        {
            Header = Idioma.T("Monitor serial da placa"),
            IsCheckable = true,
            IsChecked = monitorDaPlaca is not null,
            IsEnabled = portaEscolhida is not null && PlacaDeVerdade.Cli is not null,
        };
        monitor.Click += (_, _) => AlternarMonitorDaPlaca();
        menu.Items.Add(monitor);

        if (PlacaDeVerdade.Cli is null)
        {
            var baixar = new MenuItem { Header = Idioma.T("Baixar o compilador do Arduino (arduino-cli)…") };
            baixar.Click += async (_, _) => await BaixarCompilador();
            menu.Items.Add(baixar);
        }
        else
        {
            menu.Items.Add(new MenuItem
            {
                Header = PlacaDeVerdade.Cli == PlacaDeVerdade.CliBaixado
                    ? Idioma.T("compilador: arduino-cli baixado pelo CircuitSketch")
                    : Idioma.T("compilador: arduino-cli da Arduino IDE"),
                IsEnabled = false,
            });
        }
        return menu;
    }

    void AtualizarRotuloDaPorta()
    {
        var porta = portasVistas.FirstOrDefault(p => p.Endereco == portaEscolhida);
        string texto = portaEscolhida is null
            ? portasVistas.Count == 0 ? Idioma.T("nenhuma placa na USB") : Idioma.T("{0} porta(s) — escolha uma", portasVistas.Count)
            : porta is null ? Idioma.T("{0} (desligada)", portaEscolhida)
            : porta.Placa is { } nome ? $"{portaEscolhida} — {nome}" : portaEscolhida;
        txtPortaEscolhida.Text = texto;
        txtPlacaUsb.Text = texto;
        btPlacaUsb.ToolTip = porta?.Fqbn is { } fqbn
            ? Idioma.T("A placa de verdade ligada na USB, que recebe o código da seta") + "\n" + fqbn
            : Idioma.T("A placa de verdade ligada na USB, que recebe o código da seta");
    }

    internal string TesteRotuloDaUsb => txtPlacaUsb.Text;

    internal void TesteAplicarPortas(List<PlacaDeVerdade.Porta> portas) => AplicarPortas(portas);

    internal string TestePortaEscolhida { get => portaEscolhida; set => portaEscolhida = value; }

    // =================================================================
    //  o monitor serial da placa de verdade
    // =================================================================

    PlacaDeVerdade.Monitor monitorDaPlaca;

    /// <summary>O monitor estava aberto quando a gravacao comecou: volta depois.</summary>
    bool reabrirMonitor;

    void AlternarMonitorDaPlaca(object s = null, RoutedEventArgs e = null)
    {
        if (monitorDaPlaca is not null) { FecharMonitorDaPlaca(); return; }
        AbrirMonitorDaPlaca();
    }

    void AbrirMonitorDaPlaca()
    {
        MostrarAba("placa");
        if (PlacaDeVerdade.Cli is null)
        {
            EstadoDaPlaca(PlacaDoEditor(), Idioma.T("para compilar e mandar para a placa de verdade, o CircuitSketch precisa do compilador do Arduino: instale a Arduino IDE 2 ou baixe o arduino-cli pelo menu USB, ao lado do visto"));
            return;
        }
        if (portaEscolhida is null)
        {
            EstadoDaPlaca(PlacaDoEditor(), Idioma.T("escolha a porta da placa no menu USB, ao lado do visto"));
            AbrirMenuDasPortas(btPlacaUsb);
            return;
        }

        var placa = PlacaDoEditor();
        int baud = PlacaDeVerdade.BaudDoSketch(placa?.Sketch, PerfilPlaca.De(placa?.Tipo ?? "")?.De8Bits == true);
        LogDaPlaca(Idioma.T("--- monitor serial da {0} a {1} baud (fechar: o mesmo botão) ---", portaEscolhida, baud));

        PlacaDeVerdade.Monitor aberto = null;
        aberto = PlacaDeVerdade.Monitor.Abrir(portaEscolhida, baud,
            pedaco => Dispatcher.BeginInvoke(() => AcrescentarNaPlaca(pedaco)));
        if (aberto is null) return;

        aberto.Terminou += codigo => Dispatcher.BeginInvoke(() =>
        {
            if (monitorDaPlaca != aberto) return;
            monitorDaPlaca = null;
            aberto.Dispose();
            LogDaPlaca(Idioma.T("--- monitor serial fechado ---"));
            AtualizarBotoesDaPlaca();
        });
        monitorDaPlaca = aberto;
        AtualizarBotoesDaPlaca();
        txtEntradaPlaca.Focus();
    }

    void FecharMonitorDaPlaca()
    {
        if (monitorDaPlaca is null) return;
        var fechando = monitorDaPlaca;
        monitorDaPlaca = null;
        fechando.Dispose();
        if (IsLoaded)
        {
            LogDaPlaca(Idioma.T("--- monitor serial fechado ---"));
            AtualizarBotoesDaPlaca();
        }
    }

    void EnviarParaAPlaca(object s = null, RoutedEventArgs e = null)
    {
        if (monitorDaPlaca is null || txtEntradaPlaca.Text.Length == 0) return;
        monitorDaPlaca.Enviar(txtEntradaPlaca.Text + FimDeLinhaDoMonitor());
        txtEntradaPlaca.Clear();
    }

    void TeclaNaEntradaPlaca(object s, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Enter) return;
        EnviarParaAPlaca();
        e.Handled = true;
    }

    /// <summary>Um pedaco do que a placa mandou: completa a ultima linha e abre as novas.</summary>
    void AcrescentarNaPlaca(string pedaco)
    {
        if (string.IsNullOrEmpty(pedaco)) return;
        var partes = pedaco.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        if (linhaAbertaNaPlaca && linhasDaPlaca.Count > 0) linhasDaPlaca[^1] += partes[0];
        else linhasDaPlaca.Add(partes[0]);
        for (int k = 1; k < partes.Length; k++) linhasDaPlaca.Add(partes[k]);
        linhaAbertaNaPlaca = true;
        if (linhasDaPlaca.Count > 2000) linhasDaPlaca.RemoveRange(0, linhasDaPlaca.Count - 2000);
        txtPlaca.Text = string.Join("\n", linhasDaPlaca);
        rolagemPlaca.ScrollToEnd();
    }

    internal void TesteAcrescentarNaPlaca(string pedaco) => AcrescentarNaPlaca(pedaco);

    /// <summary>A ultima linha da aba e da placa e ainda pode crescer (sem fim de linha).</summary>
    bool linhaAbertaNaPlaca;

    // =================================================================
    //  o trabalho
    // =================================================================

    void ComecarNaPlaca(PecaNoCircuito placa, string fqbn, string porta)
    {
        MostrarAba("placa");

        // A porta e de um programa so: com o monitor aberto, a gravacao
        // falharia com "Access is denied". Fecha e reabre no fim.
        reabrirMonitor = false;
        if (porta is not null && monitorDaPlaca?.Porta == porta)
        {
            FecharMonitorDaPlaca();
            reabrirMonitor = true;
        }

        linhasDaPlaca.Clear();
        linhaAbertaNaPlaca = false;
        txtPlaca.Text = "";

        string nome = caminhoDoProjeto is null ? "sketch" : Path.GetFileNameWithoutExtension(caminhoDoProjeto);
        string pasta = PlacaDeVerdade.PrepararPasta(nome, placa.Sketch, editor.Atual.Arquivos);

        var cancelar = new CancellationTokenSource();
        trabalhoDaPlaca = cancelar;
        AtualizarBotoesDaPlaca();

        string nucleo = PlacaDeVerdade.NucleoDe(fqbn);
        EstadoDaPlaca(placa, porta is null
            ? Idioma.T("compilando para {0} com o compilador do Arduino… (a primeira vez de uma placa demora: ele compila o núcleo inteiro)", fqbn)
            : Idioma.T("compilando e mandando para a {0}…", porta));
        LogDaPlaca($"> arduino-cli compile --fqbn {fqbn}" + (porta is null ? "" : $" --upload -p {porta}"));

        Task.Run(() =>
        {
            if (!PlacaDeVerdade.Nucleos().Contains(nucleo))
                return new PlacaDeVerdade.Resultado(false, "#nucleo", null);

            return PlacaDeVerdade.Compilar(pasta, fqbn, porta,
                linha => Dispatcher.BeginInvoke(() => LogDaPlaca(linha)), cancelar.Token);
        }).ContinueWith(t => Dispatcher.BeginInvoke(() =>
            TerminouNaPlaca(t.IsCompletedSuccessfully ? t.Result
                                : new PlacaDeVerdade.Resultado(false, t.Exception?.GetBaseException().Message ?? "falhou", null),
                            placa, fqbn, porta, cancelar)));
    }

    void TerminouNaPlaca(PlacaDeVerdade.Resultado resultado, PecaNoCircuito placa, string fqbn, string porta,
                         CancellationTokenSource cancelar)
    {
        if (trabalhoDaPlaca == cancelar) trabalhoDaPlaca = null;
        AtualizarBotoesDaPlaca();
        TesteUltimoDaPlaca = resultado;

        if (reabrirMonitor && porta is not null)
        {
            reabrirMonitor = false;
            portaEscolhida = porta;
            Dispatcher.BeginInvoke(AbrirMonitorDaPlaca, System.Windows.Threading.DispatcherPriority.Background);
        }

        string nucleo = PlacaDeVerdade.NucleoDe(fqbn);

        if (resultado.Resumo == "#nucleo")
        {
            string semNucleo = Idioma.T("o núcleo {0} não está instalado na Arduino IDE", nucleo);
            EstadoDaPlaca(placa, semNucleo);
            Erro(semNucleo + ".");
            if (!Autoteste.Rodando && !Passeio.Ativo
                && MessageBox.Show(this, Idioma.T("O núcleo {0} (o que compila para {1}) não está instalado na Arduino IDE.\n\nInstalar agora? Ele é baixado da internet e pode ter centenas de MB.", nucleo, NomeDaPlaca(placa)),
                                   Idioma.T("Instalar o núcleo"), MessageBoxButton.YesNo, MessageBoxImage.Question)
                   == MessageBoxResult.Yes)
                InstalarNaIde(() => PlacaDeVerdade.InstalarNucleo(nucleo, l => Dispatcher.BeginInvoke(() => LogDaPlaca(l)),
                                                                  CancellationToken.None),
                              () => ComecarNaPlaca(placa, fqbn, porta));
            return;
        }

        if (resultado.Ok)
        {
            string feito = porta is null
                ? Idioma.T("{0}: o compilador do Arduino também aceitou", NomeDaPlaca(placa))
                : Idioma.T("{0}: pronto, o código foi para a placa na {1}", NomeDaPlaca(placa), porta);
            if (resultado.Memoria is not null) feito += " — " + resultado.Memoria;
            EstadoDaPlaca(placa, feito);
            Aviso(feito);
            return;
        }

        if (resultado.Resumo == "cancelado")
        {
            EstadoDaPlaca(placa, "cancelado");
            Aviso(Idioma.T("compilação cancelada."));
            return;
        }

        EstadoDaPlaca(placa, Idioma.T("compilador do Arduino: {0}", resultado.Resumo));
        Erro(Idioma.T("compilador do Arduino: {0}", resultado.Resumo));

        // A biblioteca que o simulador ja traz e a IDE ainda nao tem.
        string falta = PlacaDeVerdade.BibliotecaQueFalta(resultado.Resumo);
        if (falta is not null && !Autoteste.Rodando && !Passeio.Ativo
            && MessageBox.Show(this, Idioma.T("Instalar a biblioteca \"{0}\" na Arduino IDE agora?", falta),
                               Idioma.T("Instalar a biblioteca"), MessageBoxButton.YesNo, MessageBoxImage.Question)
               == MessageBoxResult.Yes)
            InstalarNaIde(() => PlacaDeVerdade.InstalarBiblioteca(falta,
                                                                  l => Dispatcher.BeginInvoke(() => LogDaPlaca(l)),
                                                                  CancellationToken.None),
                          () => ComecarNaPlaca(placa, fqbn, porta));
    }

    /// <summary>Instala na IDE (nucleo ou biblioteca) e, dando certo, tenta de novo.</summary>
    void InstalarNaIde(Func<bool> instalar, Action depois)
    {
        var cancelar = new CancellationTokenSource();
        trabalhoDaPlaca = cancelar;
        AtualizarBotoesDaPlaca();
        LogDaPlaca(Idioma.T("> instalando na Arduino IDE…"));

        Task.Run(instalar).ContinueWith(t => Dispatcher.BeginInvoke(() =>
        {
            if (trabalhoDaPlaca == cancelar) trabalhoDaPlaca = null;
            AtualizarBotoesDaPlaca();
            if (t.IsCompletedSuccessfully && t.Result) depois();
            else Erro(Idioma.T("a instalação na Arduino IDE não deu certo (veja a aba PLACA)."));
        }));
    }

    void CancelarNaPlaca()
    {
        trabalhoDaPlaca?.Cancel();
        LogDaPlaca("> cancelando…");
    }

    /// <summary>Com trabalho andando, os botoes viram o "cancelar" dele.</summary>
    void AtualizarBotoesDaPlaca()
    {
        bool ocupado = trabalhoDaPlaca is not null;
        btVerificar.ToolTip = ocupado ? Idioma.T("Cancelar") : Idioma.T("Verificar o código: o simulador e o compilador do Arduino (Ctrl+R)");
        btMandar.ToolTip = ocupado ? Idioma.T("Cancelar") : Idioma.T("Mandar para a placa de verdade, pela USB (Ctrl+U)");
        btVerificar.Content = ocupado ? "■" : "✓";
        btMandar.Content = ocupado ? "■" : "➜";
        btVerificarNaAba.Content = ocupado ? Idioma.T("Cancelar") : Idioma.T("Verificar");
        btMandarNaAba.IsEnabled = !ocupado;
        btMonitorPlaca.Content = monitorDaPlaca is not null ? Idioma.T("Fechar o monitor") : Idioma.T("Monitor serial");
        painelEntradaPlaca.Visibility = monitorDaPlaca is not null ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// A aba aberta sem nada rodando: o que o visto e a seta fazem, e o que
    /// falta para eles funcionarem nesta maquina.
    /// </summary>
    void ApresentarAbaDaPlaca()
    {
        if (trabalhoDaPlaca is not null || linhasDaPlaca.Count > 0) return;

        var placa = PlacaDoEditor();
        string texto = placa is not null && PorQueNaoCompila(placa, PlacaDeVerdade.FqbnDe(placa.Tipo)) is { } porQueNao
            ? porQueNao
            : Idioma.T("o visto (Ctrl+R) confere o código no simulador e no compilador do Arduino; a seta (Ctrl+U) compila e grava na placa ligada na USB");
        EstadoDaPlaca(placa, texto);
    }

    void EstadoDaPlaca(PecaNoCircuito placa, string texto)
    {
        string qual = placa is null ? "" : $"{NomeDaPlaca(placa)} → {PlacaDeVerdade.FqbnDe(placa.Tipo) ?? "sem núcleo do Arduino"}";
        string compilador = PlacaDeVerdade.Cli is null ? Idioma.T("Arduino IDE 2 não encontrada") : Idioma.T("compilador: arduino-cli da Arduino IDE");
        txtPlacaEstado.Text = $"{qual}  ·  {compilador}\n{texto}";
    }

    void LogDaPlaca(string linha)
    {
        linhaAbertaNaPlaca = false;
        linhasDaPlaca.Add(linha);
        if (linhasDaPlaca.Count > 2000) linhasDaPlaca.RemoveRange(0, linhasDaPlaca.Count - 2000);
        txtPlaca.Text = string.Join("\n", linhasDaPlaca);
        rolagemPlaca.ScrollToEnd();
    }
}
