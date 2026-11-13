using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Simulador.App;

/// <summary>
/// O passeio pela interface: <c>CircuitSketch.exe --passeio &lt;pasta&gt;</c>.
///
/// O autoteste confere a LÓGICA -- quase dois mil casos --, mas ele roda
/// sem mostrar janela nenhuma, e há um tipo de defeito que só aparece
/// na tela: o painel que some atrás de outro, o texto cortado, a cor que
/// não se lê no fundo escuro, a janela solta que abre vazia. Esse é o
/// "glitch" que a pessoa vê e o teste não vê.
///
/// Então este modo abre a janela DE VERDADE, percorre as funções do
/// programa numa ordem fixa -- como um aluno faria numa aula --, e
/// fotografa cada estado. As fotos saem numa pasta, junto de um
/// relatório com o que cada passo encontrou e com toda exceção que
/// escapou, para alguém olhar depois.
///
/// A ordem importa: cada passo parte do estado que o anterior deixou,
/// porque é assim que o programa é usado. Um defeito que só aparece
/// depois de soltar e devolver um painel só é pego se o roteiro soltar
/// e devolver antes.
/// </summary>
public static class Passeio
{
    /// <summary>O passeio está rodando: erro vai para o relatório, e não para uma caixa.</summary>
    public static bool Ativo { get; private set; }

    /// <summary>Os .sktch das demos, escritos no temp para abrir -- e apagados no fim.</summary>
    static readonly List<string> temporarios = [];

    public static void Comecar(Application app, string pasta)
    {
        Ativo = true;
        Directory.CreateDirectory(pasta);

        var relatorio = new System.Text.StringBuilder();
        var erros = new List<string>();

        relatorio.AppendLine("passeio pela interface do CircuitSketch");
        relatorio.AppendLine(new string('=', 60));

        // Exceção que escapa não pode abrir caixa de diálogo: ela
        // travaria o passeio esperando um clique que não vem. Aqui ela
        // é ANOTADA, e o passeio segue -- o relatório é o lugar dela.
        app.DispatcherUnhandledException += (_, ev) =>
        {
            erros.Add($"{ev.Exception.GetType().Name}: {ev.Exception.Message}");
            relatorio.AppendLine($"   !! EXCEÇÃO: {ev.Exception.GetType().Name}: {ev.Exception.Message}");
            ev.Handled = true;
        };

        var janela = new MainWindow { Width = 1400, Height = 860 };
        janela.Show();

        JanelaPecas paleta = null;

        // Menu aberto não é janela: mora num popup, e o laço das fotos
        // não o encontra. Quem abre um menu diz aqui o que fotografar.
        Func<IEnumerable<(string, FrameworkElement)>> alemDasJanelas = null;
        string demos = Path.Combine(pasta, "..");
        int numero = 0;

        var passos = new List<(int Espera, string Nome, Action Fazer)>
        {
            (600, "01 abertura", null),

            // ---- a paleta, com as subabas ----
            (400, "02 paleta - tudo", () =>
            {
                paleta = new JanelaPecas(janela);
                paleta.Show();
            }),
            (300, "03 paleta - aba ESP32", () => paleta.TesteTrocar("ESP32")),
            (300, "03b paleta - aba ESP8266", () => paleta.TesteTrocar("ESP8266")),
            (300, "04 paleta - aba Raspberry Pi", () => paleta.TesteTrocar("Raspberry Pi")),
            (300, "05 paleta - aba STM32", () => paleta.TesteTrocar("STM32")),
            (300, "06 paleta - busca sem resultado", () =>
            {
                paleta.TesteTrocar("Arduino");
                paleta.TesteBuscar("esp32");
            }),
            (200, "07 paleta fechada", () => { paleta.Close(); paleta = null; }),

            // ---- rodar, e os painéis de baixo ----
            (2200, "08 rodando", janela.TesteRodar),
            (300, "09 aba console", () => janela.TesteMostrarAba("console")),
            (300, "10 aba rede", () => janela.TesteMostrarAba("rede")),
            (300, "11 aba bluetooth", () => janela.TesteMostrarAba("bluetooth")),
            (300, "11b aba placa", () => janela.TesteMostrarAba("placa")),
            (300, "12 aba serial", () => janela.TesteMostrarAba("serial")),

            // ---- a aba componentes, com as cores ----
            (400, "13 modo componentes", () => janela.MostrarModo("componentes")),
            (300, "14 modo circuito", () => janela.MostrarModo("circuito")),
            (300, "15 modo simulacao", () => janela.MostrarModo("simulacao")),

            // ---- soltar e devolver ----
            (500, "16 serial solto", () => janela.TesteSoltarAba("serial")),
            (500, "17 codigo solto tambem", () => janela.TesteSoltarPainel("codigo")),
            (500, "18 console solto tambem", () => janela.TesteSoltarAba("console")),
            (400, "19 tudo devolvido", () =>
            {
                janela.TesteSoltarAba("console");
                janela.TesteSoltarPainel("codigo");
                janela.TesteSoltarAba("serial");
            }),
            (400, "20 painel de baixo inteiro solto", () => janela.TesteSoltarPainel("baixo")),
            (400, "21 painel de baixo devolvido", () => janela.TesteSoltarPainel("baixo")),

            (400, "22 parado", janela.TesteParar),
        };

        // ---- cada linguagem, rodando ----
        foreach (var (demo, rotulo) in new[]
                 {
                     ("ada-semaforo", "Ada"), ("spark-termostato", "SPARK"),
                     ("rust-sirene", "Rust"), ("csharp-medidor-de-luz", "CSharp"),
                     ("cowgol-contador", "Cowgol"), ("millipascal-respirar", "Millipascal"),
                     ("assembly-contador", "Assembly"), ("pico-sdk-respirar", "C (Pico SDK)"),
                     ("pico-joystick-oled", "MicroPython"),
                 })
        {
            string nome = rotulo;
            passos.Add((1600, $"{23 + passos.Count - 22:00} linguagem {nome}", () =>
            {
                janela.TesteParar();
                AbrirDemo(janela, demo);
                janela.TesteRodar();
            }));
        }

        // ---- as placas novas, com a arte de cada uma ----
        foreach (var (demo, rotulo) in new[]
                 {
                     ("nucleo-c031-relogio-rtc", "Nucleo branca"),
                     ("picow-estacao-web", "Pico W com a logo"),
                     ("esp12e-rgb-navegador", "ESP-12E com a fileira de baixo"),
                     ("esp01-tomada-mqtt", "ESP-01"),
                     ("esp32c3-estufa", "ESP32-C3"),
                     ("nodemcu-servidor", "NodeMCU"),
                     ("attiny85-luz-noturna", "ATtiny85"),
                 })
        {
            string nome = rotulo;
            passos.Add((900, $"{23 + passos.Count - 22:00} placa {nome}", () =>
            {
                janela.TesteParar();
                AbrirDemo(janela, demo);
            }));
        }

        // ---- as demos novas, abertas pelo menu de verdade ----
        //
        // Pelo AbrirDemo da janela, e não pelo arquivo: foi por esse
        // caminho que o código de uma demo deixou de chegar na placa.
        foreach (var (demo, rotulo) in new[]
                 {
                     ("esp32-bluetooth", "Bluetooth"),
                     ("esp32-espnow", "ESP-NOW duas placas"),
                 })
        {
            string qual = demo;
            passos.Add((2000, $"{23 + passos.Count - 22:00} demo {rotulo}", () =>
            {
                janela.TesteParar();
                janela.TesteAbrirDemo(qual);
                janela.TesteRodar();
            }));
        }

        // ---- o jogo 3D: a demo aberta, o START apertado e uma partida ----
        passos.Add((1500, $"{23 + passos.Count - 22:00} demo BASE ZERO titulo", () =>
        {
            janela.TesteParar();
            janela.TesteAbrirDemo("esp32s3-base-zero");
            janela.TesteRodar();
        }));
        passos.Add((3000, $"{23 + passos.Count - 22:00} demo BASE ZERO jogando", () =>
        {
            // O START apertado por um instante, como um dedo.
            var editor = janela.TesteEditorCircuito;
            editor.BotoesPressionados.Add("start");
            var soltar = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            soltar.Tick += (_, _) => { soltar.Stop(); editor.BotoesPressionados.Remove("start"); };
            soltar.Start();
        }));

        // ---- o DOOM de verdade (modo nativo): sem o WAD, o aviso; com o WAD
        // de quem roda o passeio, o jogo na tela ----
        passos.Add((5000, $"{23 + passos.Count - 22:00} demo ESP32-DOOM sem WAD", () =>
        {
            janela.TesteParar();
            janela.TesteAbrirDemo("esp32s3-doom");
            janela.TesteRodar();
        }));
        if (Autoteste.WadParaTeste() is { } wad)
            passos.Add((7000, $"{23 + passos.Count - 22:00} demo ESP32-DOOM jogando", () =>
            {
                janela.TesteParar();
                var cartao = janela.TesteEditorCircuito.Atual.Pecas.First(p => p.Tipo == "microsd-card");
                cartao.ArquivosDoComputador = new(StringComparer.OrdinalIgnoreCase) { ["DOOM1.WAD"] = wad };
                janela.TesteRodar();
            }));

        // ---- o player de musica: o cartao e a pasta de musica de quem roda o
        // passeio (nada e copiado nem gravado nela), a tela em pe ----
        passos.Add((6000, $"{23 + passos.Count - 22:00} demo PLAYER DE MUSICA", () =>
        {
            janela.TesteParar();
            janela.TesteAbrirDemo("esp32s3-player-dap");
            var cartao = janela.TesteEditorCircuito.Atual.Pecas.First(p => p.Tipo == "microsd-card");
            string musicas = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
            if (Directory.Exists(musicas)) cartao.PastaDoComputador = musicas;
            janela.TesteRodar();
        }));

        // ---- os motores DC (Alpha 23): o amarelo pelo rele, o 130 nas
        // pilhas, o brushless parado; os rotulos de rpm ----
        passos.Add((2500, $"{23 + passos.Count - 22:00} motores DC", () =>
        {
            janela.TesteParar();
            janela.TesteAbrir(Autoteste.CircuitoDosMotores(), Autoteste.SketchDosMotores);
            janela.TesteRodar();
        }));

        // ---- as pecas da Alpha 23, pelos projetos da bateria: os dois
        // circuitos so com bateria (nada para rodar), as pontes, o passo, o
        // servo, o ESC (os bipes e o armar levam ~3 s) e o radio ----
        if (Autoteste.AcharABateria() is { } bateria)
            foreach (var (arquivo, rotulo, espera, rodar) in new[]
                     {
                         ("energia/bateria-e-led", "so bateria: LED", 1200, false),
                         ("energia/bateria-e-motor", "so bateria: motor", 1800, false),
                         ("componentes/l298n", "ponte H L298N", 2500, true),
                         ("componentes/roda-tt", "roda no motor amarelo", 2500, true),
                         ("componentes/a4988", "A4988 e NEMA 17", 2000, true),
                         ("componentes/motor-28byj48", "28BYJ-48 no ULN2003", 2500, true),
                         ("componentes/servo-mg996r", "servo MG996R", 2000, true),
                         ("componentes/esc", "ESC e brushless", 5500, true),
                         ("componentes/helice-1045", "helice 1045", 5500, true),
                         ("componentes/receptor-rc", "receptor e controle", 2000, true),
                     })
            {
                string caminho = Path.Combine(bateria, arquivo + ".sktch");
                if (!File.Exists(caminho)) continue;
                bool rodarDepois = rodar;
                passos.Add((espera, $"{23 + passos.Count - 22:00} {rotulo}", () =>
                {
                    janela.TesteParar();
                    var (circuito, codigo) = Sktch.Abrir(caminho);
                    janela.TesteAbrir(circuito, codigo);
                    if (rodarDepois) janela.TesteRodar();
                }));
            }

        // ---- a parte modular: a ficha do motor na barra, a ESC trocada para
        // 60 A rosa, e a lista de componentes com uma categoria recolhida ----
        if (Autoteste.AcharABateria() is { } bateriaModular)
        {
            string brushless = Path.Combine(bateriaModular, "componentes", "motor-brushless.sktch");
            if (File.Exists(brushless))
            {
                passos.Add((1500, $"{23 + passos.Count - 22:00} barra: ficha do motor sem escova", () =>
                {
                    janela.TesteParar();
                    var (circuito, codigo) = Sktch.Abrir(brushless);
                    janela.TesteAbrir(circuito, codigo);
                    janela.TesteSelecionar(circuito.Pecas.FindIndex(p => p.Tipo == "motor-brushless"));
                }));
                passos.Add((1500, $"{23 + passos.Count - 22:00} barra: ESC de 60 A rosa", () =>
                {
                    var circuito = janela.TesteCircuito;
                    janela.TesteSelecionar(circuito.Pecas.FindIndex(p => p.Tipo == "esc"));
                    janela.TesteEditorCircuito.DefinirEsc(60, "rosa");
                    janela.TesteSelecionar(circuito.Pecas.FindIndex(p => p.Tipo == "esc"));
                }));
            }
            passos.Add((1200, $"{23 + passos.Count - 22:00} motores de varios tamanhos", () =>
            {
                janela.TesteParar();
                var c = new Circuito();
                double x = 40;
                foreach (var (tipo, modelo) in new (string, string)[]
                         {
                             ("motor-dc", null), ("motor-dc", "rs-540"), ("motor-dc", "rs-775"),
                             ("motor-brushless", "1105-5000"), ("motor-brushless", "2205-2300"), ("motor-brushless", "2814-900"),
                         })
                {
                    var p = new PecaNoCircuito { Tipo = tipo, Modelo = modelo, X = x, Y = 120 };
                    c.Pecas.Add(p);
                    x += Geometria.Tamanho(p, Catalogo.Obter(tipo)).Largura + 40;
                }
                janela.TesteAbrir(c, "void setup() {}" + Environment.NewLine + "void loop() {}");
            }));
            // Os servos com os bracos de cada um (Alpha 25), e a escolha na barra.
            passos.Add((1200, $"{23 + passos.Count - 22:00} barra: braco do servo", () =>
            {
                janela.TesteParar();
                var c = new Circuito();
                double x = 40;
                foreach (var (tipo, braco) in new (string, string)[]
                         {
                             ("servo", "duplo"), ("servo-360", "cruz"), ("servo-mg996r", "disco"), ("servo-mg996r", "estrela"),
                         })
                {
                    var p = new PecaNoCircuito { Tipo = tipo, Braco = braco, X = x, Y = 140 };
                    c.Pecas.Add(p);
                    x += Geometria.Tamanho(p, Catalogo.Obter(tipo)).Largura + 30;
                }
                janela.TesteAbrir(c, "void setup() {}" + Environment.NewLine + "void loop() {}");
                janela.TesteSelecionar(2);
            }));
            // As baterias (Alpha 25): as prontas, os suportes com um slot
            // vazio, e a barra de cada tipo.
            passos.Add((1200, $"{23 + passos.Count - 22:00} baterias: suporte na barra", () =>
            {
                janela.TesteParar();
                var c = new Circuito();
                void Por(string tipo, double x, double y, string modelo = null, int bits = 0)
                {
                    var p = new PecaNoCircuito { Tipo = tipo, X = x, Y = y, Modelo = modelo, Bits = bits };
                    if (Baterias.DeFabrica(tipo) is { } f) (p.Volts, p.MiliAmperes, p.MiliAmperesHora) = f;
                    c.Pecas.Add(p);
                }
                Por("suporte-aa-4", 40, 60, null, 1 << 2);
                Por("suporte-18650-2", 300, 60);
                Por("suporte-cr2032", 640, 60);
                Por("suporte-aaa-4", 40, 330, "nimh");
                Por("bateria-9v", 300, 330);
                Por("lipo-1s-500", 640, 330);
                Por("lipo-3s-2200", 40, 520);
                Por("power-bank", 640, 480);
                janela.TesteAbrir(c, "void setup() {}" + Environment.NewLine + "void loop() {}");
                janela.TesteSelecionar(0);
            }));
            passos.Add((1200, $"{23 + passos.Count - 22:00} baterias: LiPo na barra", () =>
                janela.TesteSelecionar(janela.TesteCircuito.Pecas.FindIndex(p => p.Tipo == "lipo-3s-2200"))));
            passos.Add((1200, $"{23 + passos.Count - 22:00} componentes com MOTORES recolhida", () =>
            {
                var paletaRecolhida = new JanelaPecas(janela);
                paletaRecolhida.Show();
                paletaRecolhida.TesteTrocar("Tudo");
                paletaRecolhida.TesteAlternarCategoria("MOTORES");
            }));
            passos.Add((200, $"{23 + passos.Count - 22:00} componentes de volta", () =>
            {
                if (app.Windows.OfType<JanelaPecas>().FirstOrDefault() is { } aberta) aberta.TesteAlternarCategoria("MOTORES");
                FecharAsDeApoio(app, janela);
            }));
        }

        // ---- a bateria funcional (Alpha 24): a placa sem o cabo USB -- solta,
        // na LiPo, pelo regulador do L298N, queimada no 3V3 -- com a barra da
        // placa (o cabo USB e de onde vem a energia) aberta ----
        if (Autoteste.AcharABateria() is { } bateriaDaPlaca)
            foreach (var (arquivo, rotulo, espera) in new[]
                     {
                         ("bateria-solta", "placa sem energia (bateria solta)", 1800),
                         ("bateria-no-vin", "placa na LiPo pelo VIN", 1800),
                         ("l298n-alimenta-placa", "L298N alimentando a placa", 2500),
                         ("pilhas-no-3v3", "pilhas no 3V3: queimou", 1500),
                     })
            {
                string caminho = Path.Combine(bateriaDaPlaca, "alimentacao", arquivo + ".sktch");
                if (!File.Exists(caminho)) continue;
                passos.Add((espera, $"{23 + passos.Count - 22:00} {rotulo}", () =>
                {
                    janela.TesteParar();
                    var (circuito, codigo) = Sktch.Abrir(caminho);
                    janela.TesteAbrir(circuito, codigo);
                    janela.TesteRodar();
                    janela.TesteSelecionar(janela.TesteCircuito.Pecas.FindIndex(p => p.Id == "placa"));
                }));
            }

        // ---- o celular: o BLE da Lampada (conversa e servicos) e o
        // terminal do SerialBT ----
        JanelaCelular celularDoPasseio = null;
        passos.Add((2500, $"{23 + passos.Count - 22:00} celular BLE dispositivos", () =>
        {
            janela.TesteParar();
            janela.TesteAbrirDemo("esp32c3-ble-lampada");
            janela.TesteRodar();
            celularDoPasseio = janela.TesteAbrirCelular();
        }));
        passos.Add((2500, $"{23 + passos.Count - 22:00} celular BLE conversa", () =>
        {
            var redeDoPasseio = janela.TesteRedeSimulada;
            if (redeDoPasseio.AnunciosBle().FirstOrDefault() is { } radio) redeDoPasseio.ConectarBleDoCelular(radio);
            celularDoPasseio?.MostrarPagina("ble");
            celularDoPasseio?.Digitar("red", pelaUart: true);
        }));
        passos.Add((1500, $"{23 + passos.Count - 22:00} celular BLE servicos",
                    () => celularDoPasseio?.MostrarPagina("servicos")));
        passos.Add((2500, $"{23 + passos.Count - 22:00} celular terminal classico", () =>
        {
            janela.TesteParar();
            janela.TesteAbrirDemo("esp32-bluetooth");
            janela.TesteRodar();
            var redeDoPasseio = janela.TesteRedeSimulada;
            var depois = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
            depois.Tick += (_, _) =>
            {
                depois.Stop();
                if (redeDoPasseio.DispositivosVisiveis().FirstOrDefault() is { } nome) redeDoPasseio.ConectarCelular(nome);
                celularDoPasseio?.MostrarPagina("terminal");
                celularDoPasseio?.Digitar("help", pelaUart: false);
            };
            depois.Start();
        }));
        passos.Add((300, $"{23 + passos.Count - 22:00} celular fechado", () => celularDoPasseio?.Close()));

        // ---- o analisador logico, com a janela grande aberta ----
        passos.Add((2500, $"{23 + passos.Count - 22:00} demo analisador logico", () =>
        {
            janela.TesteParar();
            janela.TesteAbrirDemo("uno-analisador");
            janela.TesteRodar();
            var grande = janela.TesteAbrirAnalisador("la");
            grande.JanelaParaTeste(50_000);
        }));
        passos.Add((200, $"{23 + passos.Count - 22:00} analisador fechado", () => FecharAsDeApoio(app, janela)));

        // ---- o que não cabe: o limitador ----
        passos.Add((600, $"{23 + passos.Count - 22:00} limitador recusa o que nao cabe", () =>
        {
            janela.TesteParar();

            var c = new Circuito();
            c.Pecas.Add(new PecaNoCircuito { Id = "uno", Tipo = "arduino-uno", X = 80, Y = 60 });

            janela.TesteAbrir(c, "int tabela[1200];" + Environment.NewLine
                                 + "void setup() {}" + Environment.NewLine + "void loop() {}");
            janela.TesteRodar();
            janela.TesteMostrarAba("console");
        }));

        // ---- as outras janelas do programa ----
        //
        // Cada uma aberta sem ShowDialog -- modal prenderia o roteiro --,
        // fotografada, e fechada antes da seguinte. São as que ganharam
        // conteúdo na Alpha 18: a abertura lista as placas novas, e as
        // configurações e o acervo falam das linguagens e bibliotecas.
        foreach (var (rotulo, abrir) in new (string, Func<Window>)[]
                 {
                     ("tela de abertura", () => new JanelaAbertura(janela)),
                     ("roadmap", () => JanelaAbertura.JanelaDeTexto(janela, "Roadmap", JanelaAbertura.TextoDoRoadmap)),
                     ("atalhos", () => new JanelaAtalhos(janela)),
                     ("bibliotecas", () => new JanelaBibliotecas(janela, janela.TesteCodigo)),
                     ("cartao com arquivo do computador", () => new JanelaCartao(janela,
                         new(StringComparer.OrdinalIgnoreCase) { ["README.TXT"] = "This card does not have the game yet." },
                         false,
                         new(StringComparer.OrdinalIgnoreCase)
                         {
                             ["DOOM1.WAD"] = Autoteste.WadParaTeste() ?? @"C:\jogos\DOOM1.WAD",
                         })),
                 })
        {
            string nome = rotulo;
            var fazer = abrir;
            passos.Add((600, $"{23 + passos.Count - 22:00} janela {nome}", () =>
            {
                FecharAsDeApoio(app, janela);
                janela.TesteParar();
                fazer().Show();
            }));
        }

        // ---- o "Mais placas" da abertura, com os grupos abertos ----
        //
        // Os submenus são justamente o que o tema escuro já estragou uma
        // vez (texto claro em fundo claro). Então vão para a foto.
        JanelaAbertura abertura = null;
        System.Windows.Controls.ContextMenu maisPlacas = null;
        passos.Add((500, $"{23 + passos.Count - 22:00} abertura - mais placas", () =>
        {
            FecharAsDeApoio(app, janela);
            janela.TesteParar();
            abertura = new JanelaAbertura(janela);
            abertura.Show();
            abertura.UpdateLayout();
            maisPlacas = abertura.AbrirMaisPlacasParaTeste();
            alemDasJanelas = () => maisPlacas is null ? [] : [("menu", maisPlacas)];
        }));
        passos.Add((500, $"{23 + passos.Count - 22:00} abertura - grupo ESP aberto", () =>
        {
            var esp = maisPlacas?.Items.OfType<System.Windows.Controls.MenuItem>()
                                 .FirstOrDefault(m => ((string)m.Header).StartsWith("ESP"));
            if (esp is null) throw new InvalidOperationException("sem grupo ESP no menu");

            esp.IsSubmenuOpen = true;
            alemDasJanelas = () =>
            [
                ("menu", maisPlacas),
                ("submenu", ConteudoDoSubmenu(esp)),
            ];
        }));
        passos.Add((200, $"{23 + passos.Count - 22:00} abertura fechada", () =>
        {
            if (maisPlacas is not null) maisPlacas.IsOpen = false;
            alemDasJanelas = null;
            abertura?.Close();
        }));

        // ---- os menus de botão direito: no circuito e na ligação ----
        System.Windows.Controls.ContextMenu direito = null;
        passos.Add((500, $"{23 + passos.Count - 22:00} menu - botao direito na placa", () =>
        {
            FecharAsDeApoio(app, janela);
            AbrirDemo(janela, "esp32-semaforo");
            direito = janela.TesteMenuDoCircuito();
            alemDasJanelas = () => direito is null ? [] : [("menu", direito)];
        }));
        passos.Add((500, $"{23 + passos.Count - 22:00} menu - ligacao na aba componentes", () =>
        {
            if (direito is not null) direito.IsOpen = false;
            janela.MostrarModo("componentes");
            direito = janela.TesteMenuDaLigacao(0);
            alemDasJanelas = () => direito is null ? [] : [("menu", direito)];
        }));
        passos.Add((200, $"{23 + passos.Count - 22:00} menu - fechado", () =>
        {
            if (direito is not null) direito.IsOpen = false;
            alemDasJanelas = null;
            janela.MostrarModo("simulacao");
        }));

        // ---- o menu ⋮ da janela: "Exemplos prontos", em grupos ----
        System.Windows.Controls.ContextMenu opcoes = null;
        System.Windows.Controls.MenuItem exemplos = null;
        passos.Add((500, $"{23 + passos.Count - 22:00} menu - exemplos prontos", () =>
        {
            FecharAsDeApoio(app, janela);
            opcoes = janela.TesteAbrirMenuDeOpcoes();
            exemplos = janela.TesteMenuDeDemos();
            exemplos.IsSubmenuOpen = true;
            alemDasJanelas = () =>
            [
                ("menu", opcoes),
                ("exemplos", ConteudoDoSubmenu(exemplos)),
            ];
        }));
        passos.Add((500, $"{23 + passos.Count - 22:00} menu - exemplos da ESP", () =>
        {
            var esp = exemplos.Items.OfType<System.Windows.Controls.MenuItem>()
                              .First(m => ((string)m.Header).StartsWith("ESP"));
            esp.IsSubmenuOpen = true;
            alemDasJanelas = () =>
            [
                ("menu", opcoes),
                ("exemplos", ConteudoDoSubmenu(exemplos)),
                ("esp", ConteudoDoSubmenu(esp)),
            ];
        }));
        // A placa com varias demos tem o seu submenu (a DevKit tem cinco).
        passos.Add((500, $"{23 + passos.Count - 22:00} menu - exemplos da ESP32 DevKit", () =>
        {
            var esp = exemplos.Items.OfType<System.Windows.Controls.MenuItem>()
                              .First(m => ((string)m.Header).StartsWith("ESP32"));
            var devkit = esp.Items.OfType<System.Windows.Controls.MenuItem>()
                            .First(m => ((string)m.Header).StartsWith("ESP32  ("));
            devkit.IsSubmenuOpen = true;
            alemDasJanelas = () =>
            [
                ("menu", opcoes),
                ("exemplos", ConteudoDoSubmenu(exemplos)),
                ("esp", ConteudoDoSubmenu(esp)),
                ("devkit", ConteudoDoSubmenu(devkit)),
            ];
        }));
        passos.Add((500, $"{23 + passos.Count - 22:00} menu - exemplos do ESP8266", () =>
        {
            foreach (var aberto in exemplos.Items.OfType<System.Windows.Controls.MenuItem>())
                aberto.IsSubmenuOpen = false;
            var esp8266 = exemplos.Items.OfType<System.Windows.Controls.MenuItem>()
                                  .First(m => ((string)m.Header).StartsWith("ESP8266"));
            esp8266.IsSubmenuOpen = true;
            alemDasJanelas = () =>
            [
                ("menu", opcoes),
                ("exemplos", ConteudoDoSubmenu(exemplos)),
                ("esp8266", ConteudoDoSubmenu(esp8266)),
            ];
        }));
        passos.Add((200, $"{23 + passos.Count - 22:00} menu fechado", () =>
        {
            if (opcoes is not null) opcoes.IsOpen = false;
            alemDasJanelas = null;
        }));

        // ---- as configurações, uma página de cada vez ----
        //
        // A janela é a mesma nas páginas todas: fechar e abrir de novo a
        // cada uma esconderia justamente o defeito de trocar de página.
        JanelaConfiguracoes config = null;
        foreach (string assunto in janela.TesteCriarConfiguracoes().AssuntosParaTeste)
        {
            string qual = assunto;
            passos.Add((400, $"{23 + passos.Count - 22:00} configuracoes - {qual}", () =>
            {
                if (config is null || !config.IsVisible)
                {
                    FecharAsDeApoio(app, janela);
                    janela.TesteParar();
                    config = janela.TesteCriarConfiguracoes();
                    config.Show();
                }

                config.AbrirParaTeste(qual);
            }));
        }

        // ---- cada tema, na janela e na paleta ----
        //
        // Um passo por tema: a janela principal com uma demo rodando e a
        // paleta de componentes aberta por cima. E o que mostra texto que
        // sumiu no fundo -- a medida de contraste pega a conta, a foto
        // pega o lugar que ninguem lembrou de pintar.
        foreach (var cores in Tema.Paletas)
        {
            string id = cores.Id;
            passos.Add((700, $"{23 + passos.Count - 22:00} tema {cores.Nome}", () =>
            {
                FecharAsDeApoio(app, janela);
                Tema.Aplicar(id);
                if (id == Tema.Paletas[0].Id)
                {
                    janela.TesteParar();
                    janela.TesteAbrirDemo("esp32-bluetooth");
                    janela.TesteRodar();
                }
                var paletaDePecas = new JanelaPecas(janela);
                paletaDePecas.Show();
                paletaDePecas.TesteTrocar("ESP32");
            }));
        }
        passos.Add((300, $"{23 + passos.Count - 22:00} tema de volta ao escuro", () =>
        {
            FecharAsDeApoio(app, janela);
            Tema.Aplicar("escuro");
        }));

        passos.Add((300, $"{23 + passos.Count - 22:00} fim", () =>
        {
            FecharAsDeApoio(app, janela);
            janela.TesteParar();
        }));

        var fila = new Queue<(int, string, Action)>(passos);
        var relogio = new DispatcherTimer();

        void Seguinte()
        {
            if (fila.Count == 0)
            {
                relatorio.AppendLine(new string('=', 60));
                relatorio.AppendLine(erros.Count == 0
                    ? "nenhuma exceção escapou"
                    : $"{erros.Count} exceção(ões) escaparam");

                File.WriteAllText(Path.Combine(pasta, "relatorio.txt"), relatorio.ToString());

                foreach (string arquivo in temporarios)
                    try { File.Delete(arquivo); } catch { /* o Windows limpa o temp */ }

                JanelaSolta.FecharTodas();
                app.Shutdown();
                return;
            }

            var (espera, nome, fazer) = fila.Dequeue();

            try { fazer?.Invoke(); }
            catch (Exception ex)
            {
                erros.Add($"{nome}: {ex.GetType().Name}: {ex.Message}");
                relatorio.AppendLine($"   !! {nome}: {ex.GetType().Name}: {ex.Message}");
            }

            relogio.Interval = TimeSpan.FromMilliseconds(espera);
            relogio.Tag = nome;
            relogio.Start();
        }

        relogio.Tick += (_, _) =>
        {
            relogio.Stop();
            string nome = (string)relogio.Tag;
            numero++;

            try
            {
                // Todas as janelas abertas do programa, e não só a
                // principal: a janela solta e a paleta também são tela.
                int quantas = 0;

                foreach (Window w in app.Windows)
                {
                    if (!w.IsVisible || w.ActualWidth < 10) continue;

                    string sufixo = w == janela ? "" : "-" + NomeDe(w);
                    Fotografar(w, Path.Combine(pasta, Limpo(nome) + sufixo + ".png"));
                    quantas++;
                }

                foreach (var (sufixo, elemento) in alemDasJanelas?.Invoke() ?? [])
                {
                    if (elemento is null || elemento.ActualWidth < 4) continue;
                    FotografarElemento(elemento, Path.Combine(pasta, Limpo(nome) + "-" + sufixo + ".png"));
                    quantas++;
                }

                relatorio.AppendLine($"{nome,-44} {janela.TesteEstado()} "
                                     + $"| janelas: {quantas} | status: {janela.TesteStatus}");

                // O ritmo de verdade: quantas vezes o pino do LED mexeu e
                // quanto tempo o relógio da placa andou. É o que mostra se
                // um laço de espera em Assembly corre na velocidade do chip
                // -- ou na do computador, que seria centenas de vezes mais.
                if (janela.TesteMaquinaDa("placa") is { } maquina)
                {
                    int mexidas;
                    lock (maquina.Trava) mexidas = maquina.Eventos.Count;

                    relatorio.AppendLine($"{"",-44}   placa: relógio em {maquina.Ms} ms, "
                                         + $"{mexidas} mudança(s) de pino");
                }
            }
            catch (Exception ex)
            {
                erros.Add($"{nome} (foto): {ex.Message}");
                relatorio.AppendLine($"   !! {nome} (foto): {ex.Message}");
            }

            Seguinte();
        };

        _ = demos;
        Seguinte();
    }

    /// <summary>Fecha as janelas que o passeio abriu por cima da principal.</summary>
    static void FecharAsDeApoio(Application app, MainWindow janela)
    {
        foreach (var w in app.Windows.OfType<Window>().ToList())
            if (w != janela && w is not JanelaSolta && w.IsVisible) w.Close();
    }

    static void AbrirDemo(MainWindow janela, string recurso)
    {
        string texto = Demos.Texto(recurso)
                       ?? throw new InvalidOperationException($"demo '{recurso}' não veio no programa");

        string temporario = Path.Combine(Path.GetTempPath(), $"passeio-{recurso}.sktch");
        File.WriteAllText(temporario, texto);
        temporarios.Add(temporario);
        janela.CarregarSketch(temporario);
    }

    static string NomeDe(Window w) => w switch
    {
        JanelaPecas => "paleta",
        JanelaSolta s => "solta-" + Limpo(s.Title.Replace("CircuitSketch — ", "")
                                              .Split("  (")[0]),
        _ => Limpo(w.GetType().Name),
    };

    static string Limpo(string nome)
        => new([.. nome.Select(c => char.IsLetterOrDigit(c) || c is '-' or ' ' ? c : '_')]);

    /// <summary>O que o submenu mostra: o conteúdo do popup do item.</summary>
    static FrameworkElement ConteudoDoSubmenu(System.Windows.Controls.MenuItem item)
    {
        item.ApplyTemplate();

        // O popup tem nome diferente conforme o template: o do sistema
        // chama "PART_Popup", e o escuro do programa chama "submenu".
        var popup = (item.Template.FindName("PART_Popup", item)
                     ?? item.Template.FindName("submenu", item))
                    as System.Windows.Controls.Primitives.Popup;

        return popup?.Child as FrameworkElement;
    }

    /// <summary>Foto de um pedaço de tela que não é janela: um menu, um popup.</summary>
    static void FotografarElemento(FrameworkElement elemento, string destino)
    {
        elemento.UpdateLayout();

        var foto = new RenderTargetBitmap(
            (int)Math.Ceiling(elemento.ActualWidth), (int)Math.Ceiling(elemento.ActualHeight),
            96, 96, PixelFormats.Pbgra32);

        // Um fundo magenta por baixo: onde o menu for transparente, a
        // foto mostra -- e não finge um fundo que a tela não tem.
        var fundo = new DrawingVisual();
        using (var dc = fundo.RenderOpen())
            dc.DrawRectangle(Brushes.Magenta, null,
                             new Rect(0, 0, elemento.ActualWidth, elemento.ActualHeight));

        foto.Render(fundo);
        foto.Render(elemento);

        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(foto));

        using var arquivo = File.Create(destino);
        png.Save(arquivo);
    }

    static void Fotografar(Window janela, string destino)
    {
        janela.UpdateLayout();

        var foto = new RenderTargetBitmap(
            (int)janela.ActualWidth, (int)janela.ActualHeight, 96, 96, PixelFormats.Pbgra32);

        // O fundo da janela, e não transparente: sem isso a foto de uma
        // janela escura sai com o texto claro sobre o xadrez do visor,
        // e parece defeito sem ser.
        var fundo = new DrawingVisual();
        using (var dc = fundo.RenderOpen())
            dc.DrawRectangle(janela.Background ?? Brushes.Black, null,
                             new Rect(0, 0, janela.ActualWidth, janela.ActualHeight));

        foto.Render(fundo);
        foto.Render(janela);

        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(foto));

        using var arquivo = File.Create(destino);
        png.Save(arquivo);
    }
}
