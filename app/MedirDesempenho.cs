using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace Simulador.App;

/// <summary>
/// Mede a janela de verdade: --desempenho 16,64,96 --saida arquivo abre o programa com um
/// circuito de N pecas ligadas e conta os quadros de cada situacao de uso. O tempo entre
/// quadros (e nao a media) e o que a pessoa sente: um quadro de 80 ms no meio de 59 de
/// 16 ms e um tranco, mesmo com a media perto de 60 FPS.
/// </summary>
public static class MedirDesempenho
{
    const string Sketch = """
        const int PINOS[] = {13, 14, 27, 26, 25, 33, 32, 5, 18, 19, 23, 4};
        void setup() {
          Serial.begin(115200);
          for (int p : PINOS) pinMode(p, OUTPUT);
          Serial.println("rodando");
        }
        void loop() {
          static int n = 0;
          n++;
          for (int i = 0; i < 12; i++) digitalWrite(PINOS[i], (n >> (i % 4)) & 1);
          Serial.println(n);
          delay(100);
        }
        """;

    static readonly string[] Gpios = ["D13", "D14", "D27", "D26", "D25", "D33", "D32", "D5", "D18", "D19", "D23", "D4"];
    static readonly string[] Outros = ["pushbutton", "potentiometer", "dht22", "servo", "hc-sr04", "photoresistor-sensor", "rgb-led", "lcd1602", "buzzer", "ssd1306-i2c", "7segment", "led-bar-graph"];

    /// <summary>Uma bancada de aula: a placa, LEDs com resistor e sensores e telas, tudo ligado.</summary>
    static Circuito Bancada(int pecas)
    {
        var c = new Circuito();
        var placa = new PecaNoCircuito { Tipo = "esp32-devkit-v1", X = 0, Y = 0 };
        c.Pecas.Add(placa);
        int i = 0;
        PecaNoCircuito Nova(string tipo)
        {
            var p = new PecaNoCircuito { Tipo = tipo, X = 260 + i % 12 * 120, Y = (i / 12) * 150 };
            i++;
            c.Pecas.Add(p);
            return p;
        }
        void Ligar(PecaNoCircuito a, string pinoA, PecaNoCircuito b, string pinoB)
            => c.Fios.Add(new Fio { DePeca = a.Id, DePino = pinoA, ParaPeca = b.Id, ParaPino = pinoB, Cor = "#00A651" });

        int outro = 0, gpio = 0;
        while (c.Pecas.Count < pecas)
        {
            string pinoDaPlaca = Gpios[gpio++ % Gpios.Length];
            if (c.Pecas.Count % 3 != 0 && c.Pecas.Count + 2 <= pecas)
            {
                var r = Nova("resistor");
                var led = Nova("led");
                var defR = Catalogo.Obter("resistor");
                var defL = Catalogo.Obter("led");
                Ligar(placa, pinoDaPlaca, r, defR.Pinos[0].Nome);
                Ligar(r, defR.Pinos[1].Nome, led, defL.Pinos[0].Nome);
                Ligar(led, defL.Pinos[1].Nome, placa, Catalogo.Obter(placa.Tipo).Pinos.First(x => x.Nome.StartsWith("GND")).Nome);
            }
            else
            {
                var p = Nova(Outros[outro++ % Outros.Length]);
                Ligar(placa, pinoDaPlaca, p, Catalogo.Obter(p.Tipo).Pinos[0].Nome);
            }
        }
        return c;
    }

    /// <summary>A janela nao aquece o compilador: competiria com os quadros que se quer contar.</summary>
    internal static bool Medindo { get; private set; }

    /// <summary>Grava a cada passo: se a janela travar, o arquivo diz onde.</summary>
    static string arquivo;
    static void Nota(StringBuilder saida, string texto)
    {
        saida.AppendLine(texto);
        if (arquivo is not null) File.WriteAllText(arquivo, saida.ToString());
    }

    public static string Rodar(string tamanhos, int msPorCena, string saidaEm = null)
    {
        arquivo = saidaEm;
        Medindo = true;
        var saida = new StringBuilder();
        saida.AppendLine($"CPU logicos: {Environment.ProcessorCount}; RenderCapability.Tier: {RenderCapability.Tier >> 16} (2 = GPU com tudo)");
        foreach (string texto in tamanhos.Split(','))
        {
            int n = int.Parse(texto);
            Nota(saida, $"[{n}] abrindo a janela");
            var janela = new MainWindow { WindowState = WindowState.Maximized };
            janela.Show();
            Esperar(3000);
            Nota(saida, $"[{n}] janela aberta");
            var circuito = Bancada(n);
            janela.TesteAbrir(circuito, Sketch);
            var ed = janela.TesteEditorCircuito;
            ed.Ajustar();
            Esperar(1500);
            saida.AppendLine();
            saida.AppendLine($"=== {circuito.Pecas.Count} pecas, {circuito.Fios.Count} fios; janela {janela.ActualWidth:0}x{janela.ActualHeight:0}; zoom {ed.Zoom:0.00} ===");

            // Sem nada sujo o WPF cai para uns 30 Hz e o FPS nao diz nada: um ponto de 2 px
            // muda a cada quadro, para a conta medir o que a janela consegue de verdade.
            var ponto = new System.Windows.Shapes.Rectangle { Width = 2, Height = 2, Fill = Brushes.Black, Opacity = 0.02, IsHitTestVisible = false };
            ed.MundoParaMedir.Children.Add(ponto);
            bool alterna = false;
            void Marcapasso()
            {
                alterna = !alterna;
                ponto.Opacity = alterna ? 0.02 : 0.03;
            }
            Cena(saida, "parado", msPorCena, Marcapasso);
            int direcao = 1, passos = 0;
            void Panorama()
            {
                if (++passos % 60 == 0) direcao = -direcao;
                ed.Deslocar(4 * direcao, 2 * direcao);
            }
            Cena(saida, "arrastar a tela", msPorCena, Panorama);

            bool aproximando = true;
            int passosDoZoom = 0;
            Cena(saida, "rodinha do zoom", msPorCena, () =>
            {
                if (++passosDoZoom % 40 == 0) aproximando = !aproximando;
                ed.MudarZoom(aproximando ? 1.02 : 1 / 1.02);
            });
            double zoomDeAntes = ed.Zoom;
            ed.MudarZoom(3 / zoomDeAntes);
            Esperar(500);
            Cena(saida, "arrastar a tela com zoom 3x", msPorCena, Panorama);
            ed.Ajustar();
            Esperar(500);

            var alvo = circuito.Pecas[circuito.Pecas.Count / 2];
            var def = Catalogo.Obter(alvo.Tipo);
            var meio = Geometria.PosicaoLocal(alvo, def, def.Largura / 2, def.Altura / 2);
            ed.Apertar(meio);
            double fase = 0;
            Cena(saida, "arrastar uma peca", msPorCena, () =>
            {
                Marcapasso();
                fase += 0.05;
                ed.Mover(new Point(meio.X + Math.Sin(fase) * 60, meio.Y + Math.Cos(fase) * 40), true);
            });
            ed.Soltar();

            Nota(saida, $"[{n}] rodando");
            janela.TesteRodar();
            Nota(saida, $"[{n}] voltou do Rodar");
            var relogio = Stopwatch.StartNew();
            while (!janela.TesteSerial.Contains("rodando") && relogio.Elapsed.TotalSeconds < 150)
            {
                janela.TesteAtualizarPaineis();
                Esperar(200);
            }
            saida.AppendLine($"  (subiu em {relogio.Elapsed.TotalSeconds:0.0} s; serial: {(janela.TesteSerial.Contains("rodando") ? "ok" : "NAO RODOU")}; erros: [{string.Join(" / ", janela.TesteLinhasDoNivel(NivelDoConsole.Erro))}])");
            Esperar(1000);
            Cena(saida, "simulando", msPorCena, Marcapasso);
            Cena(saida, "simulando e arrastando a tela", msPorCena, Panorama);
            janela.TesteParar();
            janela.TesteDarPorSalvo();
            janela.Close();
            Esperar(500);
        }
        return saida.ToString();
    }

    static void Cena(StringBuilder saida, string nome, int ms, Action aCadaQuadro)
    {
        Nota(saida, $"[cena] {nome}");
        var tempos = new List<double>(ms / 8);
        var relogio = Stopwatch.StartNew();
        double ultimo = -1;
        TimeSpan ultimoRender = TimeSpan.MinValue;

        void AoRender(object s, EventArgs e)
        {
            // O WPF chama de novo no mesmo quadro quando sobra tempo: so conta o quadro novo.
            if (e is RenderingEventArgs r)
            {
                if (r.RenderingTime == ultimoRender) return;
                ultimoRender = r.RenderingTime;
            }
            double agora = relogio.Elapsed.TotalMilliseconds;
            if (ultimo >= 0) tempos.Add(agora - ultimo);
            ultimo = agora;
            aCadaQuadro?.Invoke();
        }

        double ocupado = 0;
        long inicioDaOperacao = 0;
        int profundidade = 0;
        var hooks = Dispatcher.CurrentDispatcher.Hooks;
        void Comecou(object s, DispatcherHookEventArgs e)
        {
            if (profundidade++ == 0) inicioDaOperacao = Stopwatch.GetTimestamp();
        }
        void Terminou(object s, DispatcherHookEventArgs e)
        {
            if (profundidade > 0 && --profundidade == 0)
                ocupado += (Stopwatch.GetTimestamp() - inicioDaOperacao) * 1000.0 / Stopwatch.Frequency;
        }
        hooks.OperationStarted += Comecou;
        hooks.OperationCompleted += Terminou;
        var processo = Process.GetCurrentProcess();
        var cpuAntes = processo.TotalProcessorTime;
        var pausaAntes = GC.GetTotalPauseDuration();
        int gen0Antes = GC.CollectionCount(0), gen2Antes = GC.CollectionCount(2);
        long alocadoAntes = GC.GetTotalAllocatedBytes();
        var threadsAntes = processo.Threads.Cast<ProcessThread>().ToDictionary(t => t.Id, t => { try { return t.TotalProcessorTime; } catch { return TimeSpan.Zero; } });
        CompositionTarget.Rendering += AoRender;
        Esperar(ms);
        CompositionTarget.Rendering -= AoRender;
        processo.Refresh();
        double cpu = (processo.TotalProcessorTime - cpuAntes).TotalMilliseconds / ms * 100;
        var threads = processo.Threads.Cast<ProcessThread>().Select(t =>
        {
            try { return (Id: t.Id, Pct: (t.TotalProcessorTime - threadsAntes.GetValueOrDefault(t.Id)).TotalMilliseconds / ms * 100, Inicio: InicioDe(t.Id)); }
            catch { return (Id: t.Id, Pct: 0.0, Inicio: IntPtr.Zero); }
        }).OrderByDescending(t => t.Pct).Take(4).Where(t => t.Pct >= 5).ToList();
        string modulos = string.Join(" ", threads.Select(t => $"{t.Pct:0}%{(t.Id == Environment.CurrentManagedThreadId ? "" : "")}@{ModuloDe(processo, t.Inicio)}"));
        string memoria = $"GC {(GC.GetTotalPauseDuration() - pausaAntes).TotalMilliseconds / ms * 100:0}% do tempo, {GC.CollectionCount(0) - gen0Antes} gen0, {GC.CollectionCount(2) - gen2Antes} gen2, {(GC.GetTotalAllocatedBytes() - alocadoAntes) / 1024 / 1024 * 1000 / ms} MB/s";
        hooks.OperationStarted -= Comecou;
        hooks.OperationCompleted -= Terminou;

        var piores = tempos.Select((t, i) => (t, i)).OrderByDescending(x => x.t).Take(4).Select(x => $"quadro {x.i}: {x.t:0} ms");
        Nota(saida, $"    piores: {string.Join(", ", piores)}");
        if (tempos.Count == 0)
        {
            saida.AppendLine($"  {nome,-32} sem quadros");
            return;
        }
        Nota(saida, $"    {memoria} | threads: {modulos}");
        var ordem = tempos.Order().ToList();
        double Pct(double p) => ordem[Math.Min(ordem.Count - 1, (int)(ordem.Count * p))];
        double duracao = tempos.Sum();
        Nota(saida, $"  {nome,-32} {tempos.Count / (duracao / 1000),5:0.0} FPS | mediana {Pct(.5),5:0.0} ms | 95% {Pct(.95),5:0.0} ms | 99% {Pct(.99),5:0.0} ms | pior {ordem[^1],6:0.0} ms | acima de 20 ms: {tempos.Count(t => t > 20) * 100.0 / tempos.Count,4:0}% | janela {ocupado / ms * 100,3:0}% | processo {cpu,4:0}% de um nucleo");
    }

    [System.Runtime.InteropServices.DllImport("kernel32")] static extern IntPtr OpenThread(int acesso, bool herda, int id);
    [System.Runtime.InteropServices.DllImport("kernel32")] static extern bool CloseHandle(IntPtr h);
    [System.Runtime.InteropServices.DllImport("ntdll")] static extern int NtQueryInformationThread(IntPtr h, int classe, out IntPtr valor, int tamanho, IntPtr devolvido);

    /// <summary>Onde a thread comecou de verdade: o StartAddress do .NET da sempre o do ntdll.</summary>
    static IntPtr InicioDe(int id)
    {
        IntPtr h = OpenThread(0x40, false, id);
        if (h == IntPtr.Zero) return IntPtr.Zero;
        NtQueryInformationThread(h, 9, out IntPtr inicio, IntPtr.Size, IntPtr.Zero);
        CloseHandle(h);
        return inicio;
    }

    static string ModuloDe(Process p, IntPtr endereco)
    {
        foreach (ProcessModule m in p.Modules)
            if ((long)endereco >= (long)m.BaseAddress && (long)endereco < (long)m.BaseAddress + m.ModuleMemorySize)
                return Path.GetFileNameWithoutExtension(m.ModuleName);
        return "?";
    }

    /// <summary>Espera deixando a janela trabalhar.</summary>
    static void Esperar(int ms)
    {
        var quadro = new DispatcherFrame();
        var t = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(ms) };
        t.Tick += (_, _) =>
        {
            t.Stop();
            quadro.Continue = false;
        };
        t.Start();
        Dispatcher.PushFrame(quadro);
    }
}
