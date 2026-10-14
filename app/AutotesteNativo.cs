using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Simulador.App;

/// <summary>
/// O modo nativo (Alpha 19, o pedido do DOOM): os .c do projeto e das
/// bibliotecas compilados de verdade pelo TinyCC, com o ESP-IDF do simulador
/// por baixo. O que a placa de verdade aceita e recusa, e a demo ESP32-DOOM
/// inteira na janela.
/// </summary>
public static partial class Autoteste
{
    /// <summary>
    /// Compila o projeto (com abas .c), roda o setup(), espera o tempo pedido
    /// no relogio de verdade -- as tarefas do C sao threads -- e encerra o
    /// codigo nativo. Devolve o serial, o erro, e o reinicio que a placa pediu.
    /// </summary>
    static (string Serial, string Erro, ReinicioDaPlaca Reinicio) RodarNativo(
        string sketch, IDictionary<string, string> abas, string placa = "esp32-s3-devkit", int ms = 300)
    {
        var maq = new Maquina { Perfil = PerfilPlaca.De(placa) ?? PerfilPlaca.Esp32 };
        try
        {
            var prog = Compilador.CompilarProjeto(new Projeto
            {
                Sketch = sketch,
                Arquivos = abas,
                Placa = placa,
                Bibliotecas = [],
                DoSimulador = Acervo.EhDoSimulador,
            });
            var interp = new Interpretador(prog, maq);
            interp.Preparar();
            try { interp.Setup(); }
            catch (ReinicioDaPlaca) { }
            var relogio = System.Diagnostics.Stopwatch.StartNew();
            while (relogio.ElapsedMilliseconds < ms && maq.ReinicioPedido is null) Thread.Sleep(10);
            return (string.Join("|", LinhasDoSerial(maq)), null, maq.ReinicioPedido);
        }
        catch (Exception e) when (e is ErroDeSintaxe or ErroDeExecucao)
        {
            return (string.Join("|", LinhasDoSerial(maq)), e.Message, maq.ReinicioPedido);
        }
        finally
        {
            maq.EncerrarNativo();
        }
    }

    const string SketchQueChama = """
        void teste();
        void setup() { Serial.begin(115200); teste(); }
        void loop() { delay(1000); }
        """;

    static void Nativo_CNaPlaca()
    {
        saida.AppendLine("modo nativo: o C da placa");

        // ---- chamada do .ino para o C, com numero, real e texto ----
        var (s1, e1, _) = RodarNativo("""
            int soma(int a, int b);
            double media(double a, double b);
            const char *saudacao(void);
            void setup() {
              Serial.begin(115200);
              Serial.println(soma(2, 3));
              Serial.println(media(2, 3));
              Serial.println(saudacao());
            }
            void loop() { delay(1000); }
            """, new Dictionary<string, string>
        {
            ["conta.c"] = """
                #include <stdio.h>
                int soma(int a, int b) { return a + b; }
                double media(double a, double b) { return (a + b) / 2; }
                const char *saudacao(void) { static char b[32]; snprintf(b, sizeof b, "ola do %s", "C"); return b; }
                """,
        });
        saida.AppendLine($"        {e1 ?? s1}");
        Conferir("nativo: o sketch chama o C com int, double e texto", e1 is null && s1 == "5|2.50|ola do C", e1 ?? s1);

        // ---- tarefas e filas do FreeRTOS ----
        var (s2, e2, _) = RodarNativo(SketchQueChama, new Dictionary<string, string>
        {
            ["tarefas.c"] = """
                #include <stdio.h>
                #include "freertos/FreeRTOS.h"
                static QueueHandle_t fila;
                static void produtor(void *p) { for (int i = 1; i <= 3; i++) { xQueueSend(fila, &i, portMAX_DELAY); vTaskDelay(pdMS_TO_TICKS(10)); } vTaskDelete(NULL); }
                static void consumidor(void *p) { int v, soma = 0; for (int i = 0; i < 3; i++) { xQueueReceive(fila, &v, portMAX_DELAY); soma += v; } printf("soma %d\n", soma); vTaskDelete(NULL); }
                void teste(void) {
                    fila = xQueueCreate(4, sizeof(int));
                    xTaskCreatePinnedToCore(consumidor, "c", 4096, NULL, 5, NULL, 1);
                    xTaskCreatePinnedToCore(produtor, "p", 4096, NULL, 5, NULL, 0);
                }
                """,
        }, ms: 600);
        Conferir("nativo: tarefas do FreeRTOS conversam por fila", e2 is null && s2.Contains("soma 6"), e2 ?? s2);

        // ---- erro de compilacao: mensagem, e o compilador continua de pe ----
        var (_, e3, _) = RodarNativo(SketchQueChama, new Dictionary<string, string> { ["e.c"] = "int = 3;\n" });
        saida.AppendLine($"        erro: {e3}");
        Conferir("nativo: erro no .c vira erro de sintaxe com arquivo e linha (sem derrubar o programa)",
            e3 is not null && e3.Contains("e.c:1:") && e3.Contains("error"), e3);
        var (s4, e4, _) = RodarNativo(SketchQueChama, new Dictionary<string, string>
        {
            ["a.c"] = "#ifdef _WIN32\n#error _WIN32 no primeiro\n#endif\nint um(void) { return 1; }\n",
            ["b.c"] = "#include <stdio.h>\n#ifdef _WIN32\n#error _WIN32 no segundo\n#endif\nint um(void);\nvoid teste(void) { printf(\"ok %d\\n\", um()); }\n",
        });
        Conferir("nativo: depois de um erro, compila de novo -- e nenhum .c ve o _WIN32",
            e4 is null && s4 == "ok 1", e4 ?? s4);

        // ---- os pinos de cada chip ----
        const string pinos = """
            #include <stdio.h>
            #include "driver/gpio.h"
            void teste(void) {
                gpio_config_t c = { .pin_bit_mask = (1ULL << 4) | (1ULL << 22), .mode = GPIO_MODE_INPUT };
                printf("%s ", esp_err_to_name(gpio_config(&c)));
                printf("%s\n", esp_err_to_name(gpio_set_direction(GPIO_NUM_34, GPIO_MODE_OUTPUT)));
            }
            """;
        var (naS3, e5, _) = RodarNativo(SketchQueChama, new Dictionary<string, string> { ["p.c"] = pinos });
        var (naEsp, e6, _) = RodarNativo(SketchQueChama, new Dictionary<string, string> { ["p.c"] = pinos }, "esp32-devkit-v1");
        var (_, naC3, _) = RodarNativo(SketchQueChama, new Dictionary<string, string> { ["p.c"] = pinos }, "esp32-c3-devkit");
        saida.AppendLine($"        s3: {naS3 ?? e5} | esp32: {naEsp ?? e6} | c3: {naC3}");
        Conferir("nativo: no S3 o GPIO 22 nao existe (GPIO_PIN mask error), e o 34 e saida",
            e5 is null && naS3.EndsWith("ESP_ERR_INVALID_ARG ESP_OK"), naS3 ?? e5);
        Conferir("nativo: na ESP32 o 22 existe, e o 34 so le (io_num=34 can only be input)",
            e6 is null && naEsp.StartsWith("ESP_OK ") && naEsp.Contains("can only be input")
            && naEsp.EndsWith("ESP_ERR_INVALID_ARG"), naEsp ?? e6);
        Conferir("nativo: no C3 o GPIO_NUM_34 nem compila, como no GCC",
            naC3 is not null && naC3.Contains("GPIO_NUM_34"), naC3);

        // ---- a ROM, as particoes do partitions.csv e a montagem so de leitura ----
        var (s7, e7, _) = RodarNativo(SketchQueChama, new Dictionary<string, string>
        {
            ["r.c"] = """
                #include <stdio.h>
                #include <string.h>
                #include <errno.h>
                #include "rom/md5_hash.h"
                #include "esp_partition.h"
                #include "esp_vfs_fat.h"
                void teste(void) {
                    struct MD5Context m; unsigned char r[16];
                    MD5Init(&m); MD5Update(&m, (const unsigned char *)"abc", 3); MD5Final(r, &m);
                    printf("md5 %02x%02x%02x%02x\n", r[0], r[1], r[2], r[3]);
                    const esp_partition_t *p = esp_partition_find_first(ESP_PARTITION_TYPE_DATA, ESP_PARTITION_SUBTYPE_ANY, "doom_wad");
                    if (p) printf("wad %d 0x%02x 0x%lx\n", p->type, p->subtype, (unsigned long)p->address);
                    esp_vfs_fat_mount_config_t cfg = { .max_files = 2 };
                    esp_vfs_fat_spiflash_mount_ro("/ro", "ffat", &cfg);
                    FILE *f = fopen("/ro/x.txt", "w");
                    printf("ro %s %d\n", f ? "abriu" : "NULL", errno);
                }
                """,
            ["partitions.csv"] = """
                nvs,      data, nvs,     0x9000,  0x5000,
                app0,     app,  ota_0,   0x10000, 0x300000,
                doom_wad, data, 0x40,    ,        0x400000,
                ffat,     data, fat,     ,        0x80000,
                """,
        });
        saida.AppendLine($"        {e7 ?? s7}");
        Conferir("nativo: o MD5 da ROM (MD5Init/Update/Final) da o resumo certo",
            e7 is null && s7.Contains("md5 90015098"), e7 ?? s7);
        Conferir("nativo: o partitions.csv do projeto vale, com o tipo e o subtipo da tabela",
            s7 is not null && s7.Contains("wad 1 0x40 0x310000"), s7);
        Conferir("nativo: montado so para leitura, o fopen de escrita da EROFS",
            s7 is not null && s7.Contains("ro NULL 30"), s7);

        // ---- o panico: Guru Meditation, e a placa reinicia ----
        var (s8, e8, reinicio) = RodarNativo(SketchQueChama, new Dictionary<string, string>
        {
            ["f.c"] = """
                #include "freertos/FreeRTOS.h"
                static void quebra(void *p) { vTaskDelay(5); volatile int *nada = NULL; *nada = 1; }
                void teste(void) { xTaskCreate(quebra, "quebra", 4096, NULL, 5, NULL); }
                """,
        }, ms: 2000);
        saida.AppendLine($"        panico: {e8 ?? s8?.Split('|').FirstOrDefault(l => l.Contains("Guru"))} / {reinicio?.Resumo}");
        Conferir("nativo: ponteiro nulo numa tarefa da Guru Meditation e reinicia a placa (razao 4)",
            s8 is not null && s8.Contains("Guru Meditation") && reinicio?.Razao == ReinicioDaPlaca.PeloPanico,
            $"{s8} / {reinicio?.Resumo}");
    }

    /// <summary>
    /// O WAD de quem roda a suite, se ele tiver um: o DOOM1.WAD nao vem com o
    /// programa. Sem ele, a demo e testada ate onde da (o aviso do cartao).
    /// </summary>
    internal static string WadParaTeste()
    {
        string[] candidatos =
        [
            Environment.GetEnvironmentVariable("CS_WAD_DO_DOOM"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "DOOM1.WAD"),
        ];
        return candidatos.FirstOrDefault(c => !string.IsNullOrEmpty(c) && File.Exists(c));
    }

    static void Demo_Doom()
    {
        saida.AppendLine("demo ESP32-DOOM");

        var janela = new MainWindow();
        try
        {
            janela.TesteAbrirDemo("esp32s3-doom");
            var pecas = janela.TesteEditorCircuito.Atual.Pecas;
            var placa = pecas.First(p => p.Tipo == "esp32-s3-devkit");
            var cartao = pecas.First(p => p.Tipo == "microsd-card");
            var falante = pecas.First(p => p.Tipo == "alto-falante");

            // ---- sem o WAD: o jogo explica o que falta ----
            janela.TesteRodar();
            var maquina = janela.TesteMaquinaDa(placa.Id);
            bool avisou = EsperarPor(() => LinhasDoSerial(maquina).Any(l => l.Contains("nao achei o WAD")), 15000);
            saida.AppendLine($"        sem WAD: {string.Join(" | ", LinhasDoSerial(maquina).Take(6))}");
            Conferir("doom: sem o WAD no cartao, o jogo diz o que falta", avisou,
                string.Join(" | ", LinhasDoSerial(maquina)));
            janela.TesteParar();

            string wad = WadParaTeste();
            if (wad is null)
            {
                saida.AppendLine("        (sem DOOM1.WAD nesta maquina: o resto da demo fica de fora)");
                return;
            }

            // ---- com o WAD do computador no cartao ----
            cartao.ArquivosDoComputador = new(StringComparer.OrdinalIgnoreCase) { ["DOOM1.WAD"] = wad };
            janela.TesteRodar();
            maquina = janela.TesteMaquinaDa(placa.Id);
            bool achou = EsperarPor(() => LinhasDoSerial(maquina).Any(l => l.Contains("WAD em /sdcard/DOOM1.WAD")), 15000);
            Conferir("doom: o WAD do computador entra no cartao e o jogo acha", achou,
                string.Join(" | ", LinhasDoSerial(maquina).Take(8)));

            // A tela de titulo: muitas cores no display.
            EsperarPor(() => (maquina.Tela?.Pixels.Distinct().Count() ?? 0) > 40, 10000);
            int cores = maquina.Tela?.Pixels.Distinct().Count() ?? 0;
            saida.AppendLine($"        cores no display: {cores}");
            Conferir("doom: a imagem do jogo chega no display ILI9341", cores > 40, $"{cores} cores");

            // O demo do jogo toca som: o alto-falante no amplificador recebe o I2S.
            double pico = 0;
            EsperarPor(() =>
            {
                var (audio, _) = janela.TesteEditorCircuito.SomDoAltoFalante(falante, janela.TesteBancada, janela.TesteRede);
                pico = Math.Max(pico, audio?.Nivel ?? 0);
                return pico > 0.05;
            }, 15000);
            saida.AppendLine($"        som no alto-falante: pico {pico:0.00}");
            Conferir("doom: o som do jogo sai pelo MAX98357A no alto-falante", pico > 0.05, $"{pico:0.00}");

            // O menu (Esc) e o OK (o atirar) tres vezes: um jogo novo, no E1M1.
            int niveis = LinhasDoSerial(maquina).Count(l => l.Contains("P_GetNodesVersion"));
            void Apertar(string id)
            {
                janela.TesteEditorCircuito.BotoesPressionados.Add(id);
                EsperarPor(() => false, 200);
                janela.TesteEditorCircuito.BotoesPressionados.Remove(id);
                EsperarPor(() => false, 700);
            }
            Apertar("menu");
            Apertar("atirar");
            Apertar("atirar");
            Apertar("atirar");
            bool jogo = EsperarPor(() => LinhasDoSerial(maquina).Count(l => l.Contains("P_GetNodesVersion")) > niveis, 8000);
            Conferir("doom: os botoes do circuito abrem o menu e comecam um jogo novo", jogo,
                string.Join(" | ", LinhasDoSerial(maquina).TakeLast(6)));

            // A foto da janela, para quem conferir de olho.
            EsperarPor(() => false, 1500);
            string foto = Path.Combine(Path.GetTempPath(), "circuitsketch-teste-doom.png");
            FotografarJanela(janela, foto);
            saida.AppendLine($"        foto: {foto}");
        }
        finally
        {
            janela.TesteParar();
            try { janela.Close(); } catch (InvalidOperationException) { }
        }
    }

    static void FotografarJanela(Window janela, string destino)
    {
        try
        {
            if (janela.ActualWidth < 10)
            {
                janela.Measure(new Size(1500, 900));
                janela.Arrange(new Rect(0, 0, 1500, 900));
            }
            janela.UpdateLayout();
            int largura = (int)Math.Max(janela.ActualWidth, 10), altura = (int)Math.Max(janela.ActualHeight, 10);
            var foto = new RenderTargetBitmap(largura, altura, 96, 96, PixelFormats.Pbgra32);
            var fundo = new DrawingVisual();
            using (var dc = fundo.RenderOpen())
                dc.DrawRectangle(janela.Background ?? Brushes.Black, null, new Rect(0, 0, largura, altura));
            foto.Render(fundo);
            foto.Render(janela);
            var png = new PngBitmapEncoder();
            png.Frames.Add(BitmapFrame.Create(foto));
            using var arquivo = File.Create(destino);
            png.Save(arquivo);
        }
        catch (Exception e)
        {
            saida.AppendLine($"        (a foto nao saiu: {e.Message})");
        }
    }
}
