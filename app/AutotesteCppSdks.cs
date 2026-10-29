namespace Simulador.App;

/// <summary>
/// Os SDKs pelo C/C++ compilado: o FreeRTOS (tarefas, filas, semaforos,
/// notificacoes, temporizadores, a tarefa que falha, a parada com tarefas
/// rodando), o ESP-IDF (app_main, gpio, esp_log, esp_timer), o SDK da Pico
/// (main, gpio_put, sleep_ms) e o programa em C puro com main().
/// </summary>
public static partial class Autoteste
{
    static void Cpp_Sdks()
    {
        saida.AppendLine("os SDKs pelo C/C++ compilado (FreeRTOS, ESP-IDF, Pico SDK, C com main)");
        if (!CompiladorNativo.Disponivel)
        {
            Conferir("SDKs: o compilador (nativo/zig) esta instalado", false, "faltou nativo/zig/zig.exe");
            return;
        }

        // ---- duas tarefas e o loop() revezando, com fila e semaforo ----
        const string tarefas = """
            QueueHandle_t fila;
            SemaphoreHandle_t mutex;
            volatile int produzidos = 0, consumidos = 0, soma = 0, voltasDoLoop = 0;

            void produtor(void *p) {
              for (int i = 1; i <= 10; i++) {
                xQueueSend(fila, &i, portMAX_DELAY);
                produzidos++;
                vTaskDelay(pdMS_TO_TICKS(5));
              }
              vTaskDelete(NULL);
            }

            void consumidor(void *p) {
              int v;
              while (true) {
                if (xQueueReceive(fila, &v, pdMS_TO_TICKS(200)) == pdTRUE) {
                  xSemaphoreTake(mutex, portMAX_DELAY);
                  soma += v;
                  consumidos++;
                  xSemaphoreGive(mutex);
                }
              }
            }

            void setup() {
              Serial.begin(115200);
              fila = xQueueCreate(4, sizeof(int));
              mutex = xSemaphoreCreateMutex();
              xTaskCreatePinnedToCore(produtor, "produtor", 4096, NULL, 1, NULL, 0);
              xTaskCreate(consumidor, "consumidor", 4096, NULL, 2, NULL);
            }

            void loop() {
              voltasDoLoop++;
              if (consumidos == 10) {
                delay(30);   // o produtor sai do ultimo vTaskDelay e se apaga
                Serial.println(String(soma) + " " + String(voltasDoLoop > 3 ? "loop rodou" : "loop parado")
                               + " " + String(uxTaskGetNumberOfTasks()));
                while (true) delay(100);
              }
              delay(10);
            }
            """;
        var (serial, erro, nativo, _) = RodarNativo(tarefas, 1500);
        Conferir("FreeRTOS (ESP32): produtor e consumidor pela fila, com mutex, o loop() revezando, e o produtor apagado (sobram 2 tarefas)",
            nativo && erro is null && serial == "55 loop rodou 2", $"{nativo} {erro} | {serial}");

        // ---- prioridade, notificacao e temporizador ----
        const string notificacao = """
            TaskHandle_t trabalhadora;
            volatile int avisos = 0, disparos = 0;
            String ordem;

            void trabalha(void *p) {
              while (true) {
                ulTaskNotifyTake(pdTRUE, portMAX_DELAY);
                avisos++;
                ordem += "T";
              }
            }

            void tique(TimerHandle_t t) { disparos++; }

            void setup() {
              Serial.begin(115200);
              xTaskCreate(trabalha, "trabalha", 4096, NULL, 3, &trabalhadora);
              TimerHandle_t t = xTimerCreate("tique", pdMS_TO_TICKS(20), pdTRUE, NULL, tique);
              xTimerStart(t, 0);
              for (int i = 0; i < 3; i++) {
                ordem += "L";
                xTaskNotifyGive(trabalhadora);   // a de prioridade 3 roda ja, antes do loop seguir
              }
              delay(230);
              Serial.println(ordem + " " + String(avisos) + " " + String(disparos >= 9 && disparos <= 12 ? "timer ok" : String(disparos)));
              Serial.println(String(uxTaskPriorityGet(trabalhadora)) + " " + pcTaskGetName(trabalhadora));
            }
            void loop() { delay(100); }
            """;
        (serial, erro, nativo, _) = RodarNativo(notificacao, 800);
        Conferir("FreeRTOS: a notificacao acorda a tarefa de prioridade maior na hora, e o temporizador de 20 ms dispara ~11 vezes em 230 ms",
            nativo && erro is null && serial == "LTLTLT 3 timer ok|3 trabalha", $"{nativo} {erro} | {serial}");

        // ---- grupo de eventos e a interrupcao que da o semaforo ----
        const string isr = """
            SemaphoreHandle_t sinal;
            EventGroupHandle_t grupo;
            volatile int vistos = 0;

            void IRAM_ATTR borda() {
              BaseType_t acordou = pdFALSE;
              xSemaphoreGiveFromISR(sinal, &acordou);
              portYIELD_FROM_ISR(acordou);
            }

            void espera(void *p) {
              while (true) {
                if (xSemaphoreTake(sinal, portMAX_DELAY)) {
                  vistos++;
                  if (vistos == 3) xEventGroupSetBits(grupo, 1 << 0);
                }
              }
            }

            void outra(void *p) {
              vTaskDelay(30);
              xEventGroupSetBits(grupo, 1 << 1);
              vTaskDelete(NULL);
            }

            void setup() {
              Serial.begin(115200);
              sinal = xSemaphoreCreateBinary();
              grupo = xEventGroupCreate();
              pinMode(4, INPUT);
              attachInterrupt(digitalPinToInterrupt(4), borda, RISING);
              xTaskCreate(espera, "espera", 4096, NULL, 2, NULL);
              xTaskCreate(outra, "outra", 4096, NULL, 1, NULL);
              EventBits_t b = xEventGroupWaitBits(grupo, 0b11, pdTRUE, pdTRUE, pdMS_TO_TICKS(2000));
              Serial.println((b & 0b11) == 0b11 ? "os dois bits" : String(b));
            }
            void loop() { delay(100); }
            """;
        int nivel = 0, voltas = 0;
        (serial, erro, nativo, _) = RodarNativo(isr, 900, durante: maq =>
        {
            if (++voltas % 6 != 0) return;
            nivel ^= 1;
            maq.NivelExterno[4] = nivel;
        });
        Conferir("FreeRTOS: a interrupcao da o semaforo (xSemaphoreGiveFromISR) e a tarefa acorda; o setup espera os dois bits do grupo",
            nativo && erro is null && serial.StartsWith("os dois bits"), $"{nativo} {erro} | {serial}");

        // ---- a falha numa tarefa e a parada com tarefas rodando ----
        const string falha = """
            void quebra(void *p) {
              vTaskDelay(20);
              int *nada = (int *)p;
              *nada = 42;
              vTaskDelete(NULL);
            }
            void setup() {
              Serial.begin(115200);
              xTaskCreate(quebra, "quebra", 4096, NULL, 1, NULL);
            }
            void loop() { delay(5); }
            """;
        (serial, erro, nativo, _) = RodarNativo(falha, 500);
        Conferir("FreeRTOS: o ponteiro nulo numa tarefa vira o erro da placa, e o simulador segue",
            nativo && erro is not null && erro.Contains("escrita num endereco invalido"), $"{nativo} {erro} | {serial}");

        const string presas = """
            void gira(void *p) { volatile unsigned long k = 0; while (true) k++; }
            void setup() {
              Serial.begin(115200);
              xTaskCreate(gira, "gira1", 4096, NULL, 1, NULL);
              xTaskCreate(gira, "gira2", 4096, NULL, 1, NULL);
              Serial.println("criadas");
            }
            void loop() { delay(10); }
            """;
        var relogio = System.Diagnostics.Stopwatch.StartNew();
        (serial, erro, nativo, _) = RodarNativo(presas, 300);
        Conferir("FreeRTOS: Parar tira a placa de duas tarefas presas num laco sem chamada",
            nativo && erro is null && serial == "criadas" && relogio.ElapsedMilliseconds < 3500,
            $"{nativo} {erro} | {serial} | {relogio.ElapsedMilliseconds} ms");

        // ---- o ESP-IDF: app_main, gpio, esp_log, esp_timer ----
        const string idf = """
            #include <stdio.h>
            #include "freertos/FreeRTOS.h"
            #include "freertos/task.h"
            #include "driver/gpio.h"
            #include "esp_log.h"
            #include "esp_timer.h"
            #include "sdkconfig.h"

            #define BLINK_GPIO CONFIG_BLINK_GPIO
            static const char *TAG = "example";
            static volatile int periodicos = 0;

            static void periodico(void *arg) { periodicos++; }

            void app_main(void)
            {
                gpio_reset_pin(BLINK_GPIO);
                gpio_set_direction(BLINK_GPIO, GPIO_MODE_OUTPUT);
                const esp_timer_create_args_t args = { .callback = &periodico, .name = "periodic" };
                esp_timer_handle_t t;
                ESP_ERROR_CHECK(esp_timer_create(&args, &t));
                ESP_ERROR_CHECK(esp_timer_start_periodic(t, 10000));
                int64_t t0 = esp_timer_get_time();
                for (int i = 0; i < 4; i++) {
                    gpio_set_level(BLINK_GPIO, i % 2 == 0);
                    ESP_LOGI(TAG, "Turning the LED %s!", i % 2 == 0 ? "ON" : "OFF");
                    vTaskDelay(25 / portTICK_PERIOD_MS);
                }
                int64_t dt = esp_timer_get_time() - t0;
                printf("dt %s, periodicos %s\n", dt >= 95000 && dt < 160000 ? "ok" : "errado",
                       periodicos >= 8 && periodicos <= 12 ? "ok" : "errado");
            }
            """;
        (serial, erro, nativo, var m) = RodarNativo(idf, 700);
        string limpo = System.Text.RegularExpressions.Regex.Replace(serial ?? "", @"\(\d+\)", "(t)");
        Conferir("ESP-IDF: app_main com gpio_set_level, ESP_LOGI, esp_timer periodico e vTaskDelay (o printf sai no monitor)",
            nativo && erro is null
            && limpo == "I (t) example: Turning the LED ON!|I (t) example: Turning the LED OFF!|I (t) example: Turning the LED ON!|I (t) example: Turning the LED OFF!|dt ok, periodicos ok"
            && m.Modo[2] == 1,
            $"{nativo} {erro} | {serial}");

        // ---- o SDK da Pico: main, gpio_put, sleep_ms, printf ----
        const string pico = """
            #include <stdio.h>
            #include "pico/stdlib.h"

            int main() {
                stdio_init_all();
                const uint LED_PIN = PICO_DEFAULT_LED_PIN;
                gpio_init(LED_PIN);
                gpio_set_dir(LED_PIN, GPIO_OUT);
                absolute_time_t t0 = get_absolute_time();
                for (int i = 0; i < 3; i++) {
                    gpio_put(LED_PIN, 1);
                    sleep_ms(20);
                    gpio_put(LED_PIN, 0);
                    sleep_ms(20);
                    printf("piscou %d\n", i + 1);
                }
                int64_t dt = absolute_time_diff_us(t0, get_absolute_time());
                printf("%s\n", dt >= 115000 && dt < 200000 ? "tempo ok" : "tempo errado");
                gpio_put(LED_PIN, 1);
                while (true) tight_loop_contents(), sleep_ms(10);
            }
            """;
        (serial, erro, nativo, m) = RodarNativo(pico, 600, "pi-pico");
        Conferir("Pico SDK: main com gpio_init/gpio_put/sleep_ms e printf, compilado em C",
            nativo && erro is null && serial == "piscou 1|piscou 2|piscou 3|tempo ok" && m.Modo[25] == 1,
            $"{nativo} {erro} | {serial}");

        // ---- C puro com main() na ESP32: malloc sem cast, struct, ponteiro de funcao ----
        const string c = """
            #include <stdio.h>
            #include <stdlib.h>
            #include <string.h>

            typedef struct { const char *nome; int idade; } Pessoa;
            static int compara(const void *a, const void *b) { return ((const Pessoa *)a)->idade - ((const Pessoa *)b)->idade; }

            int main(void) {
                Pessoa *p = malloc(3 * sizeof *p);
                p[0] = (Pessoa){ "bia", 31 };
                p[1] = (Pessoa){ .nome = "ana", .idade = 20 };
                p[2] = (Pessoa){ "caio", 25 };
                qsort(p, 3, sizeof *p, compara);
                for (int i = 0; i < 3; i++) printf("%s=%d%s", p[i].nome, p[i].idade, i < 2 ? "," : "\n");
                free(p);
                puts("fim");
                return 0;
            }
            """;
        (serial, erro, nativo, _) = RodarNativo(c, 300);
        Conferir("C puro: main() com malloc sem cast, literal composto, inicializador designado e qsort",
            nativo && erro is null && serial == "ana=20,caio=25,bia=31|fim", $"{nativo} {erro} | {serial}");

        // ---- a Arduino_FreeRTOS do Uno: o loop() e a tarefa ociosa ----
        const string uno = """
            #include <Arduino_FreeRTOS.h>
            volatile int a = 0, b = 0;
            void tarefaA(void *p) { for (;;) { a++; vTaskDelay(10 / portTICK_PERIOD_MS); } }
            void tarefaB(void *p) { for (;;) { b++; vTaskDelay(20 / portTICK_PERIOD_MS); } }
            void setup() {
              Serial.begin(9600);
              xTaskCreate(tarefaA, "A", 128, NULL, 2, NULL);
              xTaskCreate(tarefaB, "B", 128, NULL, 1, NULL);
            }
            void loop() {
              if (millis() > 250) {
                Serial.println(a > b && b >= 8 ? "A mais que B" : String(a) + " " + String(b));
                for (;;) delay(100);
              }
            }
            """;
        (serial, erro, nativo, _) = RodarNativo(uno, 700, "arduino-uno");
        Conferir("Arduino_FreeRTOS (Uno): duas tarefas de periodos diferentes, e o loop() como tarefa ociosa",
            nativo && erro is null && serial == "A mais que B", $"{nativo} {erro} | {serial}");
    }
}
