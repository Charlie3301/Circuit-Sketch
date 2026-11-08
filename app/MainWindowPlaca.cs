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
        if (placa is null) { Erro("não há placa no circuito para verificar."); return; }

        var (doSimulador, simuladorAceitou) = await ConferirNoSimulador(placa);

        string fqbn = PlacaDeVerdade.FqbnDe(placa.Tipo);
        string porQueNao = PorQueNaoCompila(placa, fqbn);
        if (porQueNao is not null)
        {
            EstadoDaPlaca(placa, porQueNao);
            if (simuladorAceitou) Aviso(doSimulador + ". " + porQueNao);
            return;
        }

        if (simuladorAceitou) Aviso(doSimulador + "; o compilador do Arduino confere agora (aba PLACA)…");
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
                console.Info($"verificando {NomeDaPlaca(placa)}…");
                try { prog = await Task.Run(() => Compilador.CompilarProjeto(projeto)); }
                finally { verificando = false; }
            }
            foreach (string aviso in prog.Avisos) console.Aviso(aviso);

            if (PerfilPlaca.De(placa.Tipo) is { } perfil)
            {
                var conta = Memoria.Estimar(prog, perfil);
                if (conta.Estourou)
                {
                    Erro($"{NomeDaPlaca(placa)}: não cabe na placa — {conta.PorQueNaoCabe()}");
                    return (null, false);
                }
            }

            return ($"{NomeDaPlaca(placa)}: o simulador aceitou o código", true);
        }
        catch (Exception ex) when (ex is ErroDeSintaxe or ErroDeExecucao)
        {
            Erro($"{NomeDaPlaca(placa)}: {ex.Message}");
            return (null, false);
        }
    }

    /// <summary>Por que o compilador de verdade nao entra aqui, ou null quando entra.</summary>
    static string PorQueNaoCompila(PecaNoCircuito placa, string fqbn)
    {
        var linguagem = Compilador.Detectar(placa.Sketch ?? "");
        if (linguagem == Linguagem.MicroPython)
            return "MicroPython não passa pelo compilador do Arduino: na placa de verdade ele vai pelo Thonny ou pelo mpremote";
        if (linguagem != Linguagem.Arduino)
            return $"o compilador do Arduino é para C++; {Compilador.Nome(linguagem)} fica só no simulador";
        if (fqbn is null)
            return $"a {PerfilPlaca.De(placa.Tipo)?.Nome ?? placa.Tipo} não tem um núcleo do Arduino para compilar";
        if (PlacaDeVerdade.Cli is null)
            return "para compilar e mandar para a placa de verdade, instale a Arduino IDE 2 (arduino.cc/en/software) — "
                   + "o simulador usa o compilador que vem dentro dela";
        return null;
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
        if (placa is null) { Erro("não há placa no circuito para mandar."); return; }

        MostrarAba("placa");
        string fqbn = PlacaDeVerdade.FqbnDe(placa.Tipo);
        if (PorQueNaoCompila(placa, fqbn) is { } porQueNao)
        {
            EstadoDaPlaca(placa, porQueNao);
            Erro(porQueNao + ".");
            return;
        }

        EstadoDaPlaca(placa, "procurando as placas ligadas na USB…");
        portasVistas = await Task.Run(PlacaDeVerdade.Portas);

        if (portasVistas.Count == 0)
        {
            string nenhuma = "nenhuma placa na USB: ligue a placa no computador (com um cabo de dados, e não só de "
                             + "carga) e aperte a seta de novo";
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
            EstadoDaPlaca(placa, $"há {portasVistas.Count} portas na USB: escolha a da placa em \"Escolher a porta\"");
            AtualizarRotuloDaPorta();
            EscolherPortaDaPlaca();
            return;
        }

        portaEscolhida = porta.Endereco;
        AtualizarRotuloDaPorta();

        if (!Autoteste.Rodando && !Passeio.Ativo)
        {
            string detectada = porta.Placa ?? "placa que o Windows não identificou";
            bool outra = porta.Fqbn is not null && porta.Fqbn != fqbn;
            string pergunta = $"Mandar o código da {NomeDaPlaca(placa)} para a {porta.Endereco} ({detectada})?"
                              + (outra
                                  ? $"\n\nAtenção: nessa porta parece estar um(a) {porta.Placa}, e o código é para "
                                    + $"{NomeDaPlaca(placa)} ({fqbn})."
                                  : "")
                              + "\n\nO programa que está na placa vai ser trocado por este.";
            if (MessageBox.Show(this, pergunta, "Mandar para a placa", MessageBoxButton.YesNo,
                                outra ? MessageBoxImage.Warning : MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;
        }

        ComecarNaPlaca(placa, fqbn, porta.Endereco);
    }

    /// <summary>A lista de portas, num menu -- como o "Escolher a placa" do Bluetooth.</summary>
    async void EscolherPortaDaPlaca(object s = null, RoutedEventArgs e = null)
    {
        if (PlacaDeVerdade.Cli is null)
        {
            EstadoDaPlaca(PlacaDoEditor(), "sem a Arduino IDE 2 instalada, o simulador não enxerga as portas USB");
            return;
        }

        portasVistas = await Task.Run(PlacaDeVerdade.Portas);
        var menu = new ContextMenu { PlacementTarget = btPortaPlaca };

        if (portasVistas.Count == 0)
            menu.Items.Add(new MenuItem { Header = "nenhuma placa na USB", IsEnabled = false });

        foreach (var porta in portasVistas)
        {
            var item = new MenuItem
            {
                Header = porta.Placa is null ? porta.Endereco : $"{porta.Endereco} — {porta.Placa}",
                IsChecked = porta.Endereco == portaEscolhida,
            };
            string endereco = porta.Endereco;
            item.Click += (_, _) =>
            {
                portaEscolhida = endereco;
                AtualizarRotuloDaPorta();
            };
            menu.Items.Add(item);
        }

        var atualizar = new MenuItem { Header = "Procurar de novo" };
        atualizar.Click += (_, _) => EscolherPortaDaPlaca();
        menu.Items.Add(new Separator());
        menu.Items.Add(atualizar);

        btPortaPlaca.ContextMenu = menu;
        menu.IsOpen = true;
        AtualizarRotuloDaPorta();
    }

    void AtualizarRotuloDaPorta()
    {
        var porta = portasVistas.FirstOrDefault(p => p.Endereco == portaEscolhida);
        txtPortaEscolhida.Text = portaEscolhida is null
            ? portasVistas.Count == 0 ? "nenhuma placa na USB" : $"{portasVistas.Count} porta(s) — escolha uma"
            : porta?.Placa is { } nome ? $"{portaEscolhida} — {nome}" : portaEscolhida;
    }

    // =================================================================
    //  o trabalho
    // =================================================================

    void ComecarNaPlaca(PecaNoCircuito placa, string fqbn, string porta)
    {
        MostrarAba("placa");
        linhasDaPlaca.Clear();
        txtPlaca.Text = "";

        string nome = caminhoDoProjeto is null ? "sketch" : Path.GetFileNameWithoutExtension(caminhoDoProjeto);
        string pasta = PlacaDeVerdade.PrepararPasta(nome, placa.Sketch, editor.Atual.Arquivos);

        var cancelar = new CancellationTokenSource();
        trabalhoDaPlaca = cancelar;
        AtualizarBotoesDaPlaca();

        string nucleo = PlacaDeVerdade.NucleoDe(fqbn);
        EstadoDaPlaca(placa, porta is null
            ? $"compilando para {fqbn} com o compilador do Arduino… (a primeira vez de uma placa demora: "
              + "ele compila o núcleo inteiro)"
            : $"compilando e mandando para a {porta}…");
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

        string nucleo = PlacaDeVerdade.NucleoDe(fqbn);

        if (resultado.Resumo == "#nucleo")
        {
            string semNucleo = $"o núcleo {nucleo} não está instalado na Arduino IDE";
            EstadoDaPlaca(placa, semNucleo);
            Erro(semNucleo + ".");
            if (!Autoteste.Rodando && !Passeio.Ativo
                && MessageBox.Show(this, $"O núcleo {nucleo} (o que compila para {NomeDaPlaca(placa)}) não está "
                                         + "instalado na Arduino IDE.\n\nInstalar agora? Ele é baixado da internet "
                                         + "e pode ter centenas de MB.",
                                   "Instalar o núcleo", MessageBoxButton.YesNo, MessageBoxImage.Question)
                   == MessageBoxResult.Yes)
                InstalarNaIde(() => PlacaDeVerdade.InstalarNucleo(nucleo, l => Dispatcher.BeginInvoke(() => LogDaPlaca(l)),
                                                                  CancellationToken.None),
                              () => ComecarNaPlaca(placa, fqbn, porta));
            return;
        }

        if (resultado.Ok)
        {
            string feito = porta is null
                ? $"{NomeDaPlaca(placa)}: o compilador do Arduino também aceitou"
                : $"{NomeDaPlaca(placa)}: pronto, o código foi para a placa na {porta}";
            if (resultado.Memoria is not null) feito += " — " + resultado.Memoria;
            EstadoDaPlaca(placa, feito);
            Aviso(feito);
            return;
        }

        if (resultado.Resumo == "cancelado")
        {
            EstadoDaPlaca(placa, "cancelado");
            Aviso("compilação cancelada.");
            return;
        }

        EstadoDaPlaca(placa, "compilador do Arduino: " + resultado.Resumo);
        Erro("compilador do Arduino: " + resultado.Resumo);

        // A biblioteca que o simulador ja traz e a IDE ainda nao tem.
        var falta = System.Text.RegularExpressions.Regex.Match(resultado.Resumo, "falta a biblioteca \"([^\"]+)\"");
        if (falta.Success && !Autoteste.Rodando && !Passeio.Ativo
            && MessageBox.Show(this, $"Instalar a biblioteca \"{falta.Groups[1].Value}\" na Arduino IDE agora?",
                               "Instalar a biblioteca", MessageBoxButton.YesNo, MessageBoxImage.Question)
               == MessageBoxResult.Yes)
            InstalarNaIde(() => PlacaDeVerdade.InstalarBiblioteca(falta.Groups[1].Value,
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
        LogDaPlaca("> instalando na Arduino IDE…");

        Task.Run(instalar).ContinueWith(t => Dispatcher.BeginInvoke(() =>
        {
            if (trabalhoDaPlaca == cancelar) trabalhoDaPlaca = null;
            AtualizarBotoesDaPlaca();
            if (t.IsCompletedSuccessfully && t.Result) depois();
            else Erro("a instalação na Arduino IDE não deu certo (veja a aba PLACA).");
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
        btVerificar.ToolTip = ocupado ? "Cancelar" : "Verificar o código: o simulador e o compilador do Arduino (Ctrl+R)";
        btMandar.ToolTip = ocupado ? "Cancelar" : "Mandar para a placa de verdade, pela USB (Ctrl+U)";
        btVerificar.Content = ocupado ? "■" : "✓";
        btMandar.Content = ocupado ? "■" : "➜";
        btVerificarNaAba.Content = ocupado ? "Cancelar" : "Verificar";
        btMandarNaAba.IsEnabled = !ocupado;
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
            : "o visto (Ctrl+R) confere o código no simulador e no compilador do Arduino; a seta (Ctrl+U) "
              + "compila e grava na placa ligada na USB";
        EstadoDaPlaca(placa, texto);
    }

    void EstadoDaPlaca(PecaNoCircuito placa, string texto)
    {
        string qual = placa is null ? "" : $"{NomeDaPlaca(placa)} → {PlacaDeVerdade.FqbnDe(placa.Tipo) ?? "sem núcleo do Arduino"}";
        string compilador = PlacaDeVerdade.Cli is null ? "Arduino IDE 2 não encontrada" : "compilador: arduino-cli da Arduino IDE";
        txtPlacaEstado.Text = $"{qual}  ·  {compilador}\n{texto}";
    }

    void LogDaPlaca(string linha)
    {
        linhasDaPlaca.Add(linha);
        if (linhasDaPlaca.Count > 2000) linhasDaPlaca.RemoveRange(0, linhasDaPlaca.Count - 2000);
        txtPlaca.Text = string.Join("\n", linhasDaPlaca);
        rolagemPlaca.ScrollToEnd();
    }
}
