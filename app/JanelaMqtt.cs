using System.Collections.Concurrent;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using static Simulador.App.JanelaCelular;

namespace Simulador.App;

/// <summary>
/// O broker MQTT do computador, com cara propria: quem esta conectado (as
/// placas da simulacao e os apps de fora), os topicos com o ultimo valor,
/// as mensagens ao vivo e o envio -- o que se fazia com o MQTT Explorer ao
/// lado do simulador.
///
/// O broker e o interno do programa, aberto numa porta TCP do computador:
/// a placa da simulacao conecta nele pelo nome "circuitsketch" (ou por
/// localhost), o app do computador por localhost, e o celular ou uma placa
/// de verdade pelo IP do computador, com "aceitar da rede" marcado.
/// </summary>
public sealed class JanelaMqtt : Window
{
    const int MaximoDeMensagens = 600;

    readonly Rede rede;
    readonly DispatcherTimer tique;
    readonly ConcurrentQueue<Registro> chegando = new();
    readonly List<Registro> registros = [];
    bool pausado;
    volatile bool mudouAlgo = true;

    sealed record Registro(DateTime Quando, string Origem, string Broker, bool DeFora, bool Saindo, MensagemMqtt Mensagem);

    // ---- o que a janela mostra ----
    readonly TextBlock estado = new() { FontSize = 13, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
    readonly Border pilulaDoEstado = new() { CornerRadius = new CornerRadius(12), Padding = new Thickness(12, 4, 12, 4) };
    readonly TextBox campoPorta = Campo();
    readonly ToggleButton naRede, tudoAqui;
    readonly TextBlock comoConectar = new() { FontSize = 12, Foreground = TextoFraco, TextWrapping = TextWrapping.Wrap, FontFamily = new FontFamily("Consolas, Segoe UI") };
    readonly StackPanel listaClientes = new(), listaTopicos = new(), listaMensagens = new();
    readonly ScrollViewer rolagemMensagens;
    readonly TextBox filtro = Campo(), campoTopico = Campo(), campoMensagem = Campo();
    readonly ToggleButton retida, pausa;

    public JanelaMqtt(Window dono, Rede rede)
    {
        this.rede = rede;
        try { Owner = dono; } catch (InvalidOperationException) { }

        Title = Idioma.T("MQTT — o broker do computador");
        Width = 1280;
        Height = 760;
        MinWidth = 960;
        MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Fundo;
        FontFamily = new FontFamily("Segoe UI");

        naRede = Chave(Idioma.T("Aceitar da rede (celular, placa de verdade)"), rede.ServidorDoBroker?.NaRede ?? Preferencias.Ligado(Preferencias.MqttNaRede, false));
        naRede.ToolTip = Idioma.T("Abre o broker para a rede do computador: o celular ou uma ESP32 de verdade no mesmo WiFi conectam pelo IP dele. O Windows pode perguntar sobre o firewall.");
        tudoAqui = Chave(Idioma.T("Todas as placas usam este broker"), rede.MqttSoNoComputador);
        tudoAqui.ToolTip = Idioma.T("As placas da simulação vêm para cá qualquer que seja o broker do sketch (broker.hivemq.com, o IP da escola): dá para ver tudo aqui sem mudar o código que vai para a placa de verdade.");
        retida = Chave(Idioma.T("Retida"));
        retida.ToolTip = Idioma.T("O broker guarda a mensagem e entrega a quem assinar depois (retain)");
        pausa = Chave(Idioma.T("Pausar"));
        pausa.ToolTip = Idioma.T("Para de rolar a lista (as mensagens continuam chegando)");

        var raiz = new DockPanel { Margin = new Thickness(22, 16, 22, 16) };

        // ---- cabecalho: o nome, o estado, a porta e as chaves ----
        var cabecalho = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        DockPanel.SetDock(cabecalho, Dock.Top);
        pilulaDoEstado.Child = estado;
        DockPanel.SetDock(pilulaDoEstado, Dock.Right);
        cabecalho.Children.Add(pilulaDoEstado);
        var nome = new StackPanel();
        nome.Children.Add(Titulo("MQTT"));
        nome.Children.Add(new TextBlock
        {
            Text = Idioma.T("O broker deste computador: as placas da simulação, os apps do computador e, se quiser, o celular e as placas de verdade conversam por ele."),
            FontSize = 13, Foreground = TextoFraco, TextWrapping = TextWrapping.Wrap,
        });
        cabecalho.Children.Add(nome);
        raiz.Children.Add(cabecalho);

        var conexao = new DockPanel();
        var linhaPorta = new WrapPanel { Orientation = Orientation.Horizontal };
        linhaPorta.Children.Add(new TextBlock { Text = Idioma.T("Porta"), Foreground = TextoForte, FontSize = 13, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        campoPorta.Width = 80;
        campoPorta.Text = Preferencias.Numero(Preferencias.MqttPorta, 1883, 1, 65535).ToString();
        System.Windows.Automation.AutomationProperties.SetName(campoPorta, Idioma.T("Porta"));
        campoPorta.KeyDown += (_, ev) => { if (ev.Key == Key.Enter) { Reabrir(); ev.Handled = true; } };
        linhaPorta.Children.Add(campoPorta);
        var reabrir = Botao(Idioma.T("Reabrir"));
        reabrir.ToolTip = Idioma.T("Fecha e abre o broker de novo, na porta escrita");
        reabrir.Click += (_, _) => Reabrir();
        linhaPorta.Children.Add(reabrir);
        linhaPorta.Children.Add(new Border { Width = 18 });
        linhaPorta.Children.Add(naRede);
        linhaPorta.Children.Add(tudoAqui);
        DockPanel.SetDock(linhaPorta, Dock.Top);
        conexao.Children.Add(linhaPorta);
        comoConectar.Margin = new Thickness(0, 10, 0, 0);
        conexao.Children.Add(comoConectar);
        var cartaoConexao = Cartao(conexao);
        DockPanel.SetDock(cartaoConexao, Dock.Top);
        raiz.Children.Add(cartaoConexao);

        naRede.Click += (_, _) => Reabrir();
        tudoAqui.Click += (_, _) =>
        {
            rede.MqttSoNoComputador = tudoAqui.IsChecked == true;
            Preferencias.Definir(Preferencias.MqttSoNoComputador, rede.MqttSoNoComputador);
            AtualizarEstado();
        };

        // ---- publicar, embaixo ----
        var publicar = new DockPanel();
        var enviar = Botao(Idioma.T("Publicar"), principal: true);
        enviar.Click += (_, _) => Publicar();
        DockPanel.SetDock(enviar, Dock.Right);
        publicar.Children.Add(enviar);
        DockPanel.SetDock(retida, Dock.Right);
        publicar.Children.Add(retida);
        campoTopico.Width = 280;
        campoTopico.ToolTip = Idioma.T("Tópico (ex.: casa/sala/luz)");
        System.Windows.Automation.AutomationProperties.SetName(campoTopico, Idioma.T("Tópico"));
        campoMensagem.ToolTip = Idioma.T("Mensagem (texto ou JSON)");
        System.Windows.Automation.AutomationProperties.SetName(campoMensagem, Idioma.T("Mensagem"));
        campoMensagem.KeyDown += (_, ev) => { if (ev.Key == Key.Enter) { Publicar(); ev.Handled = true; } };
        campoTopico.KeyDown += (_, ev) => { if (ev.Key == Key.Enter) { campoMensagem.Focus(); ev.Handled = true; } };
        var rotuloTopico = new TextBlock { Text = Idioma.T("Tópico"), Foreground = TextoForte, FontSize = 13, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        DockPanel.SetDock(rotuloTopico, Dock.Left);
        publicar.Children.Add(rotuloTopico);
        DockPanel.SetDock(campoTopico, Dock.Left);
        publicar.Children.Add(campoTopico);
        var rotuloMensagem = new TextBlock { Text = Idioma.T("Mensagem"), Foreground = TextoForte, FontSize = 13, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 8, 0) };
        DockPanel.SetDock(rotuloMensagem, Dock.Left);
        publicar.Children.Add(rotuloMensagem);
        publicar.Children.Add(campoMensagem);
        var cartaoPublicar = Cartao(publicar);
        cartaoPublicar.Margin = new Thickness(0, 10, 0, 0);
        DockPanel.SetDock(cartaoPublicar, Dock.Bottom);
        raiz.Children.Add(cartaoPublicar);

        // ---- as tres colunas: clientes, topicos, mensagens ----
        var colunas = new Grid();
        colunas.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) });
        colunas.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        colunas.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(330) });
        colunas.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        colunas.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        colunas.Children.Add(Coluna(Idioma.T("CONECTADOS"), null, listaClientes, 0));

        var limparTopicos = Botao(Idioma.T("Limpar"));
        limparTopicos.ToolTip = Idioma.T("Esquece os tópicos vistos (as retidas ficam no broker)");
        limparTopicos.Click += (_, _) => { rede.Broker.EsquecerTopicos(); mudouAlgo = true; };
        colunas.Children.Add(Coluna(Idioma.T("TÓPICOS"), limparTopicos, listaTopicos, 2));

        var barraMensagens = new StackPanel { Orientation = Orientation.Horizontal };
        filtro.Width = 170;
        filtro.FontSize = 12;
        filtro.Padding = new Thickness(8, 4, 8, 4);
        filtro.ToolTip = Idioma.T("Filtro: parte do tópico ou um filtro MQTT com + e # (ex.: casa/+/temp)");
        System.Windows.Automation.AutomationProperties.SetName(filtro, Idioma.T("Filtro"));
        filtro.TextChanged += (_, _) => RedesenharMensagens();
        barraMensagens.Children.Add(filtro);
        pausa.Click += (_, _) => { pausado = pausa.IsChecked == true; if (!pausado) RedesenharMensagens(); };
        barraMensagens.Children.Add(pausa);
        var limparMensagens = Botao(Idioma.T("Limpar"));
        limparMensagens.ToolTip = Idioma.T("Apaga a lista de mensagens");
        limparMensagens.Click += (_, _) => { registros.Clear(); RedesenharMensagens(); };
        barraMensagens.Children.Add(limparMensagens);
        var colunaMensagens = Coluna(Idioma.T("MENSAGENS"), barraMensagens, listaMensagens, 4);
        rolagemMensagens = (ScrollViewer)((DockPanel)((Border)colunaMensagens).Child).Children[^1];
        colunas.Children.Add(colunaMensagens);

        raiz.Children.Add(colunas);
        Content = raiz;

        rede.Broker.Publicou += AoPublicar;
        rede.MqttDeFora += AoTrafegarFora;
        if (rede.ServidorDoBroker is { } servidor) servidor.Mudou += AoMudar;
        servidorOuvido = rede.ServidorDoBroker;

        tique = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(250) };
        tique.Tick += (_, _) => Atualizar();
        tique.Start();
        AtualizarEstado();
        Atualizar();
        RedesenharMensagens();

        Closed += (_, _) =>
        {
            tique.Stop();
            rede.Broker.Publicou -= AoPublicar;
            rede.MqttDeFora -= AoTrafegarFora;
            if (servidorOuvido is not null) servidorOuvido.Mudou -= AoMudar;
        };
    }

    ServidorMqtt servidorOuvido;

    UIElement Coluna(string titulo, UIElement extra, StackPanel lista, int coluna)
    {
        var dentro = new DockPanel();
        var topo = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        if (extra is not null)
        {
            DockPanel.SetDock(extra, Dock.Right);
            topo.Children.Add(extra);
        }
        topo.Children.Add(new TextBlock
        {
            Text = titulo, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Destaque,
            VerticalAlignment = VerticalAlignment.Center,
        });
        DockPanel.SetDock(topo, Dock.Top);
        dentro.Children.Add(topo);
        dentro.Children.Add(new ScrollViewer
        {
            Content = lista,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        });
        var cartao = Cartao(dentro);
        cartao.Margin = new Thickness(0);
        Grid.SetColumn(cartao, coluna);
        return cartao;
    }

    // =====================================================================
    //  O que chega (de outras threads)
    // =====================================================================

    void AoPublicar(MensagemMqtt mensagem, string origem)
    {
        chegando.Enqueue(new Registro(DateTime.Now, origem ?? "", null, false, false, mensagem));
        mudouAlgo = true;
    }

    void AoTrafegarFora(TrafegoMqtt trafego)
    {
        chegando.Enqueue(new Registro(DateTime.Now, trafego.Placa, trafego.Broker, true, trafego.Saindo, trafego.Mensagem));
        mudouAlgo = true;
    }

    void AoMudar() => mudouAlgo = true;

    // =====================================================================
    //  O broker: abrir, reabrir, dizer onde esta
    // =====================================================================

    void Reabrir()
    {
        if (!int.TryParse(campoPorta.Text.Trim(), out int porta) || porta < 1 || porta > 65535)
        {
            campoPorta.Text = (rede.ServidorDoBroker?.Porta ?? 1883).ToString();
            return;
        }
        bool aberto = naRede.IsChecked == true;
        Preferencias.Definir(Preferencias.MqttPorta, porta.ToString());
        Preferencias.Definir(Preferencias.MqttNaRede, aberto);

        if (servidorOuvido is not null) servidorOuvido.Mudou -= AoMudar;
        rede.ReabrirBroker(porta, aberto);
        servidorOuvido = rede.ServidorDoBroker;
        if (servidorOuvido is not null) servidorOuvido.Mudou += AoMudar;
        mudouAlgo = true;
        AtualizarEstado();
    }

    /// <summary>Os IPs deste computador na rede (WiFi, cabo), para o celular conectar.</summary>
    static List<string> EnderecosDaRede()
    {
        var enderecos = new List<string>();
        try
        {
            foreach (var placa in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (placa.OperationalStatus != OperationalStatus.Up
                    || placa.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                    continue;
                foreach (var ip in placa.GetIPProperties().UnicastAddresses)
                    if (ip.Address.AddressFamily == AddressFamily.InterNetwork && !System.Net.IPAddress.IsLoopback(ip.Address)
                        && !ip.Address.ToString().StartsWith("169.254.", StringComparison.Ordinal))
                        enderecos.Add(ip.Address.ToString());
            }
        }
        catch (Exception) { }
        return enderecos;
    }

    void AtualizarEstado()
    {
        var servidor = rede.ServidorDoBroker;
        if (servidor is null)
        {
            string pedida = campoPorta.Text.Trim();
            estado.Text = Idioma.T("porta {0} ocupada", pedida);
            estado.Foreground = Fundo;
            pilulaDoEstado.Background = Vermelho;
            comoConectar.Text = Idioma.T("Outro programa já usa a porta {0} (outro CircuitSketch aberto, um mosquitto instalado). As placas da simulação continuam conversando pelo broker interno; para os apps de fora, escolha outra porta (1884, por exemplo) e clique em Reabrir.", pedida);
            return;
        }

        estado.Text = servidor.NaRede ? Idioma.T("no ar na rede · porta {0}", servidor.Porta) : Idioma.T("no ar · localhost:{0}", servidor.Porta);
        estado.Foreground = Fundo;
        pilulaDoEstado.Background = Verde;
        if (!campoPorta.IsKeyboardFocused) campoPorta.Text = servidor.Porta.ToString();

        var texto = new System.Text.StringBuilder();
        texto.AppendLine(Idioma.T("Na placa da simulação:   client.setServer(\"circuitsketch\", {0});", servidor.Porta));
        texto.AppendLine(Idioma.T("Neste computador:        localhost:{0}  (MQTT Explorer, mosquitto_sub)", servidor.Porta));
        if (servidor.NaRede)
        {
            var ips = EnderecosDaRede();
            texto.Append(ips.Count == 0
                ? Idioma.T("Na rede:                 o computador não está em nenhuma rede agora")
                : Idioma.T("Na rede:                 {0}  (celular, ESP32 de verdade)", string.Join("  ", ips.Select(ip => $"{ip}:{servidor.Porta}"))));
        }
        else texto.Append(Idioma.T("Na rede:                 desligado (marque \"Aceitar da rede\")"));
        if (rede.MqttSoNoComputador)
            texto.Append("\n" + Idioma.T("Todas as placas da simulação vêm para cá, qualquer que seja o broker do sketch."));
        comoConectar.Text = texto.ToString();
    }

    // =====================================================================
    //  Publicar
    // =====================================================================

    void Publicar()
    {
        string topico = campoTopico.Text.Trim();
        if (topico.Length == 0)
        {
            campoTopico.Focus();
            return;
        }
        if (topico.Contains('+') || topico.Contains('#'))
        {
            MessageBox.Show(this, Idioma.T("Os curingas + e # são só para assinar: para publicar, escreva o tópico inteiro."),
                            "MQTT", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        rede.PublicarDoPainel(topico, campoMensagem.Text, retida.IsChecked == true, Idioma.T("janela MQTT"));
        campoMensagem.SelectAll();
        campoMensagem.Focus();
    }

    // =====================================================================
    //  A tela
    // =====================================================================

    void Atualizar()
    {
        bool novas = false;
        while (chegando.TryDequeue(out var r))
        {
            registros.Add(r);
            novas = true;
        }
        if (registros.Count > MaximoDeMensagens) registros.RemoveRange(0, registros.Count - MaximoDeMensagens);
        if (novas && !pausado) RedesenharMensagens();

        if (!mudouAlgo) return;
        mudouAlgo = false;
        if (servidorOuvido != rede.ServidorDoBroker)
        {
            if (servidorOuvido is not null) servidorOuvido.Mudou -= AoMudar;
            servidorOuvido = rede.ServidorDoBroker;
            if (servidorOuvido is not null) servidorOuvido.Mudou += AoMudar;
            AtualizarEstado();
        }
        RedesenharClientes();
        RedesenharTopicos();
    }

    static string Curto(string texto, int maximo)
    {
        texto = (texto ?? "").Replace("\r", "").Replace('\n', ' ');
        return texto.Length <= maximo ? texto : texto[..(maximo - 1)] + "…";
    }

    static string TextoDe(byte[] carga)
    {
        try { return System.Text.Encoding.UTF8.GetString(carga ?? []); }
        catch (Exception) { return ""; }
    }

    TextBlock Linha(string texto, Brush cor, double tamanho = 12, bool forte = false) => new()
    {
        Text = texto, Foreground = cor, FontSize = tamanho, TextWrapping = TextWrapping.Wrap,
        FontWeight = forte ? FontWeights.SemiBold : FontWeights.Normal,
    };

    string assinaturaClientes;

    void RedesenharClientes()
    {
        var cartoes = new List<(string Chave, Func<UIElement> Fazer)>();
        var assinaturas = rede.Broker.Assinaturas();

        foreach (var c in rede.ClientesMqtt().Where(c => c.Conectado))
        {
            var filtros = c.Interno ? assinaturas.Where(a => ReferenceEquals(a.Dono, c)).Select(a => a.Filtro).ToList() : [];
            string onde = c.Interno ? Idioma.T("broker do computador") : $"{c.Host}:{c.Porta}";
            string nome = c.Dona?.Nome ?? "";
            string id = c.Id;
            cartoes.Add(($"p|{nome}|{id}|{onde}|{string.Join(",", filtros)}", () => CartaoDeCliente(
                nome, Idioma.T("placa da simulação · id \"{0}\"", id), onde, filtros, c.Interno ? Verde : Amarelo)));
        }

        if (rede.ServidorDoBroker is { } servidor)
            foreach (var c in servidor.Clientes())
            {
                var filtros = c.Filtros.ToList();
                bool local = c.Endereco.StartsWith("127.", StringComparison.Ordinal) || c.Endereco.StartsWith("[::1]", StringComparison.Ordinal);
                string onde = local ? Idioma.T("este computador") : c.Endereco;
                string conta = Idioma.T("{0} enviadas · {1} recebidas", c.Enviadas, c.Recebidas);
                cartoes.Add(($"t|{c.Id}|{onde}|{conta}|{string.Join(",", filtros)}", () => CartaoDeCliente(
                    c.Id, Idioma.T("conectado por TCP · {0}", conta), onde, filtros, Destaque)));
            }

        string chave = string.Join("\n", cartoes.Select(c => c.Chave));
        if (chave == assinaturaClientes) return;
        assinaturaClientes = chave;

        listaClientes.Children.Clear();
        foreach (var (_, fazer) in cartoes) listaClientes.Children.Add(fazer());
        if (cartoes.Count == 0)
            listaClientes.Children.Add(Linha(Idioma.T("Ninguém conectado. Rode uma placa com PubSubClient, ou conecte um app de MQTT em localhost."), TextoFraco));
    }

    UIElement CartaoDeCliente(string nome, string tipo, string onde, List<string> filtros, Brush cor)
    {
        var pilha = new StackPanel();
        var titulo = new StackPanel { Orientation = Orientation.Horizontal };
        titulo.Children.Add(new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(4), Background = cor, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center });
        titulo.Children.Add(Linha(nome, TextoForte, 13, forte: true));
        pilha.Children.Add(titulo);
        pilha.Children.Add(Linha(tipo, TextoFraco, 11));
        pilha.Children.Add(Linha(onde, TextoFraco, 11));
        if (filtros.Count > 0)
            pilha.Children.Add(Linha(Idioma.T("assina: {0}", string.Join("  ", filtros)), Destaque, 11));
        return new Border
        {
            Background = Superficie2, CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 7, 10, 7),
            Margin = new Thickness(0, 0, 0, 6), Child = pilha,
        };
    }

    string assinaturaTopicos;

    void RedesenharTopicos()
    {
        var topicos = rede.Broker.Topicos();
        string chave = string.Join("\n", topicos.Select(t => $"{t.Topico}|{t.Vezes}|{t.Retida}"));
        if (chave == assinaturaTopicos) return;
        assinaturaTopicos = chave;

        listaTopicos.Children.Clear();
        foreach (var t in topicos)
        {
            var pilha = new StackPanel();
            var cima = new DockPanel();
            var vezes = Linha(t.Retida ? Idioma.T("retida · {0}×", t.Vezes) : $"{t.Vezes}×", t.Retida ? Amarelo : TextoFraco, 11);
            vezes.TextWrapping = TextWrapping.NoWrap;
            vezes.Margin = new Thickness(8, 0, 0, 0);
            DockPanel.SetDock(vezes, Dock.Right);
            cima.Children.Add(vezes);
            cima.Children.Add(Linha(t.Topico, TextoForte, 12, forte: true));
            pilha.Children.Add(cima);
            pilha.Children.Add(Linha(Curto(t.Texto, 160), Verde, 12));
            pilha.Children.Add(Linha(t.Quando.ToString("HH:mm:ss"), TextoFraco, 10));
            var cartao = new Border
            {
                Background = Superficie2, CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 6, 10, 6),
                Margin = new Thickness(0, 0, 0, 6), Child = pilha, Cursor = Cursors.Hand,
                ToolTip = Idioma.T("Clique para publicar neste tópico"),
            };
            string topico = t.Topico, valor = t.Texto;
            cartao.MouseLeftButtonUp += (_, _) =>
            {
                campoTopico.Text = topico;
                campoMensagem.Text = valor;
                campoMensagem.Focus();
                campoMensagem.SelectAll();
            };
            listaTopicos.Children.Add(cartao);
        }
        if (topicos.Count == 0)
            listaTopicos.Children.Add(Linha(Idioma.T("Nenhuma mensagem ainda."), TextoFraco));
    }

    bool PassaNoFiltro(Registro r)
    {
        string f = filtro.Text.Trim();
        if (f.Length == 0) return true;
        if (f.Contains('+') || f.Contains('#')) return BrokerMqtt.Casa(f, r.Mensagem.Topico);
        return r.Mensagem.Topico.Contains(f, StringComparison.OrdinalIgnoreCase)
               || r.Origem.Contains(f, StringComparison.OrdinalIgnoreCase);
    }

    void RedesenharMensagens()
    {
        var visiveis = registros.Where(PassaNoFiltro).TakeLast(250).ToList();
        listaMensagens.Children.Clear();
        foreach (var r in visiveis)
        {
            var pilha = new StackPanel();
            string seta = r.DeFora ? (r.Saindo ? Idioma.T("{0} → {1}", r.Origem, r.Broker) : Idioma.T("{0} ← {1}", r.Origem, r.Broker)) : r.Origem;
            var cima = new DockPanel();
            var hora = Linha(r.Quando.ToString("HH:mm:ss.fff"), TextoFraco, 11);
            hora.TextWrapping = TextWrapping.NoWrap;
            hora.Margin = new Thickness(10, 0, 0, 0);
            DockPanel.SetDock(hora, Dock.Right);
            cima.Children.Add(hora);
            var topico = Linha(r.Mensagem.Topico + (r.Mensagem.Retida ? "  " + Idioma.T("(retida)") : ""), TextoForte, 12, forte: true);
            cima.Children.Add(topico);
            pilha.Children.Add(cima);
            pilha.Children.Add(Linha(Curto(TextoDe(r.Mensagem.Carga), 600), Verde, 12));
            pilha.Children.Add(Linha(seta, r.DeFora ? Amarelo : TextoFraco, 11));
            listaMensagens.Children.Add(new Border
            {
                BorderBrush = Borda, BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(2, 5, 2, 6), Child = pilha,
            });
        }
        if (visiveis.Count == 0)
            listaMensagens.Children.Add(Linha(registros.Count == 0
                ? Idioma.T("As mensagens aparecem aqui assim que alguém publicar.")
                : Idioma.T("Nenhuma mensagem passa no filtro."), TextoFraco));
        if (!pausado) rolagemMensagens?.ScrollToEnd();
    }

    // =====================================================================
    //  Para os testes
    // =====================================================================

    internal void AtualizarParaTeste()
    {
        mudouAlgo = true;
        Atualizar();
    }

    internal int MensagensParaTeste => registros.Count;
    internal IReadOnlyList<string> TopicosParaTeste => [.. rede.Broker.Topicos().Select(t => t.Topico)];
    internal int CartoesDeClienteParaTeste => listaClientes.Children.OfType<Border>().Count();
    internal int CartoesDeTopicoParaTeste => listaTopicos.Children.OfType<Border>().Count();
    internal string EstadoParaTeste => estado.Text;

    internal void PublicarParaTeste(string topico, string texto, bool comRetida)
    {
        campoTopico.Text = topico;
        campoMensagem.Text = texto;
        retida.IsChecked = comRetida;
        Publicar();
    }

    internal void FiltrarParaTeste(string texto) => filtro.Text = texto;

    internal int MensagensVisiveisParaTeste => listaMensagens.Children.OfType<Border>().Count();
}
