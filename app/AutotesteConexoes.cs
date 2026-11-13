using System.Collections.Concurrent;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Simulador.App;

/// <summary>
/// O que veio com os pedidos de 13/11/2026: a tab da peca em pe, os atalhos
/// configuraveis e o broker MQTT do computador com janela propria.
/// </summary>
public static partial class Autoteste
{
    /// <summary>A tab da peca selecionada: uma opcao embaixo da outra, com o traco depois de X e Y.</summary>
    static void Editor_OpcoesNaVertical()
    {
        saida.AppendLine("as opcoes da peca, uma embaixo da outra");

        var janela = new MainWindow();
        try
        {
            var c = new Circuito();
            var esp = new PecaNoCircuito { Tipo = "esp32-devkit-v1", X = 0, Y = 0 };
            var res = new PecaNoCircuito { Tipo = "resistor", X = 400, Y = 120, Ohms = 470 };
            c.Pecas.AddRange([esp, res]);
            janela.TesteAbrir(c, "void setup() {} void loop() {}");
            janela.TesteSelecionar(c.Pecas.IndexOf(res));

            var opcoes = janela.TesteOpcoesDoObjeto;
            opcoes.Measure(new Size(340, double.PositiveInfinity));
            opcoes.Arrange(new Rect(opcoes.DesiredSize));

            var campos = opcoes.Children.OfType<TextBox>().ToList();
            var traco = opcoes.Children.OfType<Border>().FirstOrDefault(b => b.Child is null && b.Height == 1);
            double Topo(UIElement e) => e.TranslatePoint(new Point(0, 0), opcoes).Y;

            Conferir("opcoes: nome, X e Y, cada um na sua linha",
                campos.Count >= 3 && Topo(campos[0]) < Topo(campos[1]) && Topo(campos[1]) < Topo(campos[2]),
                string.Join(" / ", campos.Select(t => $"{t.Text}@{Topo(t):0}")));
            Conferir("opcoes: o traco depois de X e Y aparece deitado, na largura da tab",
                traco is not null && traco.ActualWidth > 100 && traco.ActualHeight >= 1
                && campos.Count >= 4 && Topo(traco) > Topo(campos[2]) && Topo(traco) < Topo(campos[3]),
                traco is null ? "sem traco" : $"{traco.ActualWidth:0}x{traco.ActualHeight:0} em {Topo(traco):0}");
            Conferir("opcoes: a tab nao passa da largura",
                opcoes.DesiredSize.Width <= 340.5, $"{opcoes.DesiredSize.Width:0}");

            var (x, y) = janela.TesteDigitarPosicao("10", "20");
            const double PorMm = 96 / 25.4;
            Conferir("opcoes: X e Y em mm levam a peca para o lugar exato",
                Math.Abs(res.X - 10 * PorMm) < 0.01 && Math.Abs(res.Y - 20 * PorMm) < 0.01, $"{x} / {y} -> {res.X:0.##},{res.Y:0.##}");
        }
        finally
        {
            try { janela.Close(); } catch (InvalidOperationException) { }
        }
    }

    /// <summary>Os atalhos: trocar, tirar, o menu, as dicas e a ajuda seguem a tabela.</summary>
    static void Atalhos_Configuraveis()
    {
        saida.AppendLine("atalhos configuraveis");
        string[] mexidos = ["rodar", "mqtt", "celular"];
        try
        {
            Conferir("atalhos: o + e os numeros do teclado numerico valem os de cima",
                AtalhosDoPrograma.Normalizar(Key.Add) == Key.OemPlus && AtalhosDoPrograma.Normalizar(Key.Subtract) == Key.OemMinus
                && AtalhosDoPrograma.Normalizar(Key.NumPad3) == Key.D3, "");
            Conferir("atalhos: de fabrica, F5 roda e Ctrl+Q abre o MQTT",
                AtalhosDoPrograma.Id(Key.F5, ModifierKeys.None) == "rodar" && AtalhosDoPrograma.Id(Key.Q, ModifierKeys.Control) == "mqtt",
                $"{AtalhosDoPrograma.Id(Key.F5, ModifierKeys.None)} / {AtalhosDoPrograma.Id(Key.Q, ModifierKeys.Control)}");
            Conferir("atalhos: nenhuma tecla repetida na tabela de fabrica",
                AtalhosDoPrograma.Todas.Where(a => a.Padrao != Key.None).GroupBy(a => (a.Padrao, a.Modificador)).All(g => g.Count() == 1),
                string.Join(", ", AtalhosDoPrograma.Todas.GroupBy(a => (a.Padrao, a.Modificador)).Where(g => g.Count() > 1)
                    .Select(g => string.Join("+", g.Select(a => a.Id)))));

            AtalhosDoPrograma.Definir("rodar", Key.F12, ModifierKeys.Control);
            Conferir("atalhos: trocado, o novo vale e o velho fica livre",
                AtalhosDoPrograma.Id(Key.F12, ModifierKeys.Control) == "rodar" && AtalhosDoPrograma.Id(Key.F5, ModifierKeys.None) is null, "");
            string dica = AtalhosDoPrograma.ComAtalho(Idioma.T("Rodar a simulação (F5)"), "rodar");
            Conferir("atalhos: a dica do botao diz o atalho de agora", dica.EndsWith("(Ctrl+F12)") && !dica.Contains("F5"), dica);
            Conferir("atalhos: quem ja usa a tecla e achado",
                AtalhosDoPrograma.QuemUsa("mqtt", Key.F12, ModifierKeys.Control) == "rodar"
                && AtalhosDoPrograma.QuemUsa("rodar", Key.F12, ModifierKeys.Control) is null, "");

            AtalhosDoPrograma.Limpar("rodar");
            dica = AtalhosDoPrograma.ComAtalho(Idioma.T("Rodar a simulação (F5)"), "rodar");
            Conferir("atalhos: sem atalho, a acao fica sem tecla e a dica sem parenteses",
                AtalhosDoPrograma.Atalho("rodar").Tecla == Key.None && !dica.Contains('(') && AtalhosDoPrograma.Rotulo("rodar") == "—", dica);

            AtalhosDoPrograma.Definir("mqtt", Key.M, ModifierKeys.Control | ModifierKeys.Shift);
            string ajuda = MainWindow.TextoDaAjuda();
            string nomeRodar = AtalhosDoPrograma.Acao("rodar").Nome, nomeMqtt = AtalhosDoPrograma.Acao("mqtt").Nome;
            Conferir("atalhos: a ajuda escreve os atalhos de agora (e pula a acao sem tecla)",
                ajuda.Contains("Shift+Ctrl+M") && ajuda.Contains(nomeMqtt) && !ajuda.Contains(" " + nomeRodar + Environment.NewLine),
                ajuda[Math.Max(0, ajuda.Length - 900)..]);

            var janela = new MainWindow();
            try
            {
                var menu = janela.TesteAbrirMenuDeOpcoes();
                var itens = menu.Items.OfType<MenuItem>().ToList();
                var itemMqtt = itens.FirstOrDefault(i => i.Uid == "mqtt");
                var itemCelular = itens.FirstOrDefault(i => i.Uid == "celular");
                Conferir("menu ⋮: tem Bluetooth e MQTT, com o atalho de agora ao lado",
                    itemMqtt?.InputGestureText == "Shift+Ctrl+M" && itemCelular?.InputGestureText == "Ctrl+B",
                    $"{itemMqtt?.InputGestureText} / {itemCelular?.InputGestureText}");
                menu.IsOpen = false;

                var lista = new JanelaAtalhos(janela);
                int todas = lista.LinhasParaTeste;
                lista.BuscarParaTeste("mqtt");
                int achadas = lista.LinhasParaTeste;
                lista.BuscarParaTeste("Ctrl+B");
                int pelaTecla = lista.LinhasParaTeste;
                lista.BuscarParaTeste("zzzz-nada");
                int nenhuma = lista.LinhasParaTeste;
                Conferir("janela dos atalhos: uma linha por acao, e a busca acha pelo nome e pela tecla",
                    todas == AtalhosDoPrograma.Todas.Length && achadas >= 1 && achadas < todas && pelaTecla >= 1 && nenhuma == 0,
                    $"{todas}/{AtalhosDoPrograma.Todas.Length}, mqtt {achadas}, Ctrl+B {pelaTecla}, nada {nenhuma}");
                lista.Close();
            }
            finally
            {
                try { janela.Close(); } catch (InvalidOperationException) { }
            }
        }
        finally
        {
            foreach (string id in mexidos) AtalhosDoPrograma.Esquecer(id);
        }
    }

    /// <summary>
    /// O broker do computador: os apps de fora por TCP, as placas da simulacao,
    /// os topicos, o testamento, "todas as placas aqui" e a janela do MQTT.
    /// </summary>
    static void Rede_BrokerDoComputador()
    {
        saida.AppendLine("o broker MQTT do computador");

        var rede = new Rede { UsarInternet = false };
        var conexoes = new List<ConexaoMqtt>();
        try
        {
            bool abriu = rede.AbrirBrokerLocal(0);
            int porta = rede.PortaDoBroker;
            var servidor = rede.ServidorDoBroker;
            saida.AppendLine($"        broker em localhost:{porta}");
            Conferir("broker: abre numa porta do computador, so para ele (nao para a rede)",
                abriu && porta > 0 && servidor is { NaRede: false }, $"{abriu} {porta}");

            var publicadas = new ConcurrentQueue<(string Topico, string Origem)>();
            rede.Broker.Publicou += (m, origem) => publicadas.Enqueue((m.Topico, origem));
            int mudou = 0;
            servidor.Mudou += () => Interlocked.Increment(ref mudou);

            // Um app do computador (o MQTT Explorer), por TCP de verdade.
            var noApp = new ConcurrentQueue<MensagemMqtt>();
            var app = ConexaoMqtt.Conectar("127.0.0.1", porta, "app-do-pc", null, null, 60, 4000, noApp.Enqueue, () => false);
            conexoes.Add(app);
            app.Assinar("sala/#");
            bool apareceu = EsperarPor(() => servidor.Clientes().Any(c => c.Id == "app-do-pc" && c.Filtros.Contains("sala/#")), 3000);
            var cliente = servidor.Clientes().FirstOrDefault(c => c.Id == "app-do-pc");
            Conferir("broker: o app conectado aparece com o id e o que assinou",
                apareceu && cliente is not null && cliente.Endereco.StartsWith("127.0.0.1") && Volatile.Read(ref mudou) > 0,
                string.Join(", ", servidor.Clientes().Select(c => $"{c.Id}@{c.Endereco} [{string.Join(" ", c.Filtros)}]")));

            // A placa da simulacao, pelo nome "circuitsketch".
            var (maq, interp) = MontarPlaca(rede, "ESP32-sala", """
                #include <WiFi.h>
                #include <PubSubClient.h>
                WiFiClient espClient;
                PubSubClient client(espClient);

                void callback(char* topic, byte* payload, unsigned int length) {
                  String msg;
                  for (int i = 0; i < length; i++) { msg += (char)payload[i]; }
                  Serial.println(String(topic) + "=" + msg);
                }

                void setup() {
                  Serial.begin(115200);
                  WiFi.begin("CircuitSketch-WiFi", "12345678");
                  while (WiFi.status() != WL_CONNECTED) { delay(200); }
                  client.setServer("circuitsketch", 1883);
                  client.setCallback(callback);
                  client.connect("placa-sala");
                  client.subscribe("sala/cmd");
                  client.publish("sala/temp", "22.5");
                }

                void loop() { client.loop(); }
                """);
            interp.Setup();
            bool noAppChegou = EsperarPor(() => { interp.Loop(); return noApp.Any(m => m.Topico == "sala/temp"); }, 4000);
            Conferir("broker: o que a placa publica chega no app do computador",
                noAppChegou && TextoDaCarga(noApp.First(m => m.Topico == "sala/temp").Carga) == "22.5",
                string.Join(" | ", noApp.Select(m => m.Topico)));
            Conferir("broker: cada publicacao diz quem publicou (a placa e o id)",
                publicadas.Any(p => p.Topico == "sala/temp" && p.Origem.Contains("ESP32-sala") && p.Origem.Contains("placa-sala")),
                string.Join(" | ", publicadas.Select(p => $"{p.Topico}<{p.Origem}")));

            app.Publicar("sala/cmd", System.Text.Encoding.UTF8.GetBytes("liga"), false);
            bool naPlaca = EsperarPor(() => { interp.Loop(); return LinhasDoSerial(maq).Contains("sala/cmd=liga"); }, 4000);
            Conferir("broker: o que o app publica chega na placa", naPlaca, string.Join(" | ", LinhasDoSerial(maq)));
            Conferir("broker: as contas do cliente (enviadas e recebidas)",
                cliente is { Enviadas: >= 1, Recebidas: >= 1 }, $"{cliente?.Enviadas} / {cliente?.Recebidas}");

            var topicos = rede.Broker.Topicos();
            Conferir("broker: os topicos com o ultimo valor e quantas vezes",
                topicos.Any(t => t.Topico == "sala/temp" && t.Texto == "22.5" && t.Vezes >= 1)
                && topicos.Any(t => t.Topico == "sala/cmd" && t.Texto == "liga"),
                string.Join(" | ", topicos.Select(t => $"{t.Topico}={t.Texto}x{t.Vezes}")));
            Conferir("broker: a placa aparece entre os clientes, com a assinatura",
                rede.ClientesMqtt().Any(c => c.Id == "placa-sala" && c.Conectado)
                && rede.Broker.Assinaturas().Any(a => a.Filtro == "sala/cmd"),
                string.Join(", ", rede.ClientesMqtt().Select(c => c.Id)));

            // O testamento: o app cai sem se despedir, o broker publica o "caiu".
            var morre = ConexaoMqtt.Conectar("127.0.0.1", porta, "vai-cair", null, null, 60, 4000, _ => { }, () => false,
                new TestamentoMqtt("pc/estado", System.Text.Encoding.UTF8.GetBytes("caiu"), true, 0));
            EsperarPor(() => servidor.Clientes().Any(c => c.Id == "vai-cair" && c.TemTestamento), 2000);
            morre.Fechar(educado: false);
            bool testamento = EsperarPor(() => rede.Broker.Topicos().Any(t => t.Topico == "pc/estado" && t.Retida && t.Texto == "caiu"), 3000);
            Conferir("broker: quem cai sem DISCONNECT tem o testamento publicado (retido)",
                testamento && servidor.Clientes().All(c => c.Id != "vai-cair"),
                string.Join(" | ", rede.Broker.Topicos().Select(t => $"{t.Topico}={t.Texto}")));

            var educado = ConexaoMqtt.Conectar("127.0.0.1", porta, "educado", null, null, 60, 4000, _ => { }, () => false,
                new TestamentoMqtt("pc/educado", System.Text.Encoding.UTF8.GetBytes("caiu"), false, 0));
            EsperarPor(() => servidor.Clientes().Any(c => c.Id == "educado"), 2000);
            educado.Fechar(educado: true);
            EsperarPor(() => servidor.Clientes().All(c => c.Id != "educado"), 2000);
            Conferir("broker: com DISCONNECT, o testamento e esquecido",
                rede.Broker.Topicos().All(t => t.Topico != "pc/educado"), "");

            // Id repetido: a conexao nova derruba a velha, como no mosquitto.
            var repetida = ConexaoMqtt.Conectar("127.0.0.1", porta, "app-do-pc", null, null, 60, 4000, _ => { }, () => false);
            conexoes.Add(repetida);
            bool trocou = EsperarPor(() => servidor.Clientes().Count(c => c.Id == "app-do-pc") == 1 && !app.Aberta, 3000);
            Conferir("broker: id repetido derruba a conexao antiga", trocou,
                $"{servidor.Clientes().Count(c => c.Id == "app-do-pc")} / velha conectada {app.Aberta}");

            // ---- a janela do MQTT ----
            var janela = new JanelaMqtt(null, rede);
            try
            {
                janela.Show();
                janela.AtualizarParaTeste();
                Conferir("janela mqtt: diz onde o broker esta", janela.EstadoParaTeste.Contains(porta.ToString()), janela.EstadoParaTeste);
                Conferir("janela mqtt: mostra a placa e o app conectados",
                    janela.CartoesDeClienteParaTeste >= 2, janela.CartoesDeClienteParaTeste.ToString());

                janela.PublicarParaTeste("sala/luz", "acesa", comRetida: true);
                interp.Loop();
                janela.AtualizarParaTeste();
                Conferir("janela mqtt: publicar pela janela entra no broker (retida) e aparece nos topicos",
                    janela.TopicosParaTeste.Contains("sala/luz") && rede.Broker.Topicos().Any(t => t.Topico == "sala/luz" && t.Retida)
                    && janela.CartoesDeTopicoParaTeste == rede.Broker.Topicos().Count,
                    string.Join(", ", janela.TopicosParaTeste));
                Conferir("janela mqtt: as mensagens chegam na lista", janela.MensagensParaTeste >= 1, janela.MensagensParaTeste.ToString());

                janela.FiltrarParaTeste("sala/+");
                int comFiltro = janela.MensagensVisiveisParaTeste;
                janela.FiltrarParaTeste("nada/#");
                int semNada = janela.MensagensVisiveisParaTeste;
                Conferir("janela mqtt: o filtro aceita os curingas do MQTT", comFiltro >= 1 && semNada == 0, $"{comFiltro} / {semNada}");
                FotografarJanela(janela, Path.Combine(Path.GetTempPath(), "circuitsketch-teste-mqtt.png"));
            }
            finally { janela.Close(); }

            // ---- reabrir em outra porta ----
            Conferir("broker: reabre em outra porta", rede.ReabrirBroker(0, false) && rede.PortaDoBroker > 0 && rede.ServidorDoBroker is not null,
                rede.PortaDoBroker.ToString());
        }
        finally
        {
            foreach (var c in conexoes) try { c.Fechar(); } catch { }
            rede.Encerrar();
        }

        // ---- "todas as placas usam este broker": o sketch pede o broker publico ----
        {
            var rede2 = new Rede { UsarInternet = true, MqttSoNoComputador = true };
            try
            {
                rede2.AbrirBrokerLocal(0);
                var (maq, interp) = MontarPlaca(rede2, "ESP32-aula", """
                    #include <WiFi.h>
                    #include <PubSubClient.h>
                    WiFiClient espClient;
                    PubSubClient client(espClient);
                    void setup() {
                      Serial.begin(115200);
                      WiFi.begin("CircuitSketch-WiFi", "12345678");
                      while (WiFi.status() != WL_CONNECTED) { delay(200); }
                      client.setServer("broker.hivemq.com", 1883);
                      Serial.println(client.connect("placa-aula"));
                      client.publish("aula/turma", "presente");
                    }
                    void loop() { client.loop(); }
                    """);
                interp.Setup();
                bool chegou = EsperarPor(() => { interp.Loop(); return rede2.Broker.Topicos().Any(t => t.Topico == "aula/turma"); }, 4000);
                long visto = 0;
                var notas = rede2.Novos(ref visto).Select(e => e.Texto).ToList();
                Conferir("broker: com \"todas as placas aqui\", o broker publico do sketch vira o do computador",
                    chegou && LinhasDoSerial(maq).Contains("1") && rede2.ClientesMqtt().All(c => c.Interno)
                    && notas.Any(n => n.Contains("broker.hivemq.com") && n.Contains("MQTT")),
                    string.Join(" | ", notas));
            }
            finally { rede2.Encerrar(); }
        }
    }

    static string TextoDaCarga(byte[] carga) => System.Text.Encoding.UTF8.GetString(carga ?? []);
}
