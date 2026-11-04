using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Simulador.App;

/// <summary>
/// As APIs do ESP-IDF no C compilado: programas com app_main, como os exemplos do
/// IDF, com o NVS, os drivers, os temporizadores, os arquivos e a rede de verdade
/// (esp_http_client, esp_http_server, esp-mqtt) em cima dos sockets do simulador.
/// </summary>
public static partial class Autoteste
{
    static void Cpp_Idf()
    {
        saida.AppendLine("C compilado: as APIs do ESP-IDF");
        if (!CompiladorNativo.Disponivel)
        {
            Conferir("ESP-IDF: o compilador (nativo/zig) esta instalado", false, "faltou nativo/zig/zig.exe");
            return;
        }

        // ---- o NVS: a chave certa, o tipo errado, o tamanho e as chaves do namespace ----
        const string nvs = """
            #include <stdio.h>
            #include <string.h>
            #include "nvs_flash.h"
            #include "nvs.h"
            #include "esp_log.h"
            void app_main(void) {
              nvs_handle_t h;
              printf("sem init %s\n", esp_err_to_name(nvs_open("loja", NVS_READWRITE, &h)));
              ESP_ERROR_CHECK(nvs_flash_init());
              printf("ro novo %s\n", esp_err_to_name(nvs_open("loja", NVS_READONLY, &h)));
              ESP_ERROR_CHECK(nvs_open("loja", NVS_READWRITE, &h));
              ESP_ERROR_CHECK(nvs_set_i32(h, "conta", -42));
              ESP_ERROR_CHECK(nvs_set_str(h, "nome", "placa"));
              uint8_t b[3] = {7, 8, 9};
              ESP_ERROR_CHECK(nvs_set_blob(h, "dados", b, 3));
              ESP_ERROR_CHECK(nvs_commit(h));
              nvs_close(h);
              ESP_ERROR_CHECK(nvs_open("loja", NVS_READONLY, &h));
              int32_t v = 0;
              printf("i32 %s %ld\n", esp_err_to_name(nvs_get_i32(h, "conta", &v)), (long)v);
              uint8_t errado;
              printf("tipo errado %s\n", esp_err_to_name(nvs_get_u8(h, "conta", &errado)));
              printf("escrever ro %s\n", esp_err_to_name(nvs_set_i32(h, "x", 1)));
              size_t n = 0;
              nvs_get_str(h, "nome", NULL, &n);
              char nome[16];
              n = 2;
              printf("curto %s\n", esp_err_to_name(nvs_get_str(h, "nome", nome, &n)));
              n = sizeof(nome);
              nvs_get_str(h, "nome", nome, &n);
              printf("str %s %d\n", nome, (int)n);
              uint8_t volta[3];
              n = 3;
              nvs_get_blob(h, "dados", volta, &n);
              printf("blob %d %d\n", volta[0], volta[2]);
              nvs_iterator_t it = NULL;
              int quantas = 0;
              esp_err_t e = nvs_entry_find("nvs", "loja", NVS_TYPE_ANY, &it);
              while (e == ESP_OK) { quantas++; e = nvs_entry_next(&it); }
              printf("chaves %d\n", quantas);
              nvs_close(h);
              printf("fim\n");
            }
            """;
        var (serial, erro, nativo, placaNvs) = RodarNativo(nvs, 2000);
        Conferir("ESP-IDF: NVS -- sem nvs_flash_init falha; o namespace novo nao abre so para leitura; set/get/commit com os tipos",
            nativo && erro is null && serial?.Contains("sem init ESP_ERR_NVS_NOT_INITIALIZED") == true && serial.Contains("ro novo ESP_ERR_NVS_NOT_FOUND")
            && serial.Contains("i32 ESP_OK -42") && serial.Contains("tipo errado ESP_ERR_NVS_NOT_FOUND") && serial.Contains("escrever ro ESP_ERR_NVS_READ_ONLY"), $"{nativo} {erro} | {serial}");
        Conferir("ESP-IDF: NVS -- string e blob com o tamanho (o curto da erro), e o iterador acha as 3 chaves",
            serial?.Contains("curto ESP_ERR_NVS_INVALID_LENGTH") == true && serial.Contains("str placa 6") && serial.Contains("blob 7 9") && serial.Contains("chaves 3"), serial);

        // O Preferences do core e o de verdade, por cima do mesmo NVS.
        const string preferencias = """
            #include <Preferences.h>
            Preferences p;
            void setup() {
              Serial.begin(115200);
              p.begin("loja", false);
              Serial.println(String("do idf ") + p.getInt("conta", 0) + " " + p.getString("nome", "?"));
              p.putUInt("novo", 77);
              p.end();
              p.begin("loja", true);
              Serial.println(String("volta ") + p.getUInt("novo", 0) + " tipo " + (int)p.getType("novo"));
              p.end();
              Serial.println("fim");
            }
            void loop() { delay(10); }
            """;
        (serial, erro, nativo, _) = RodarNativo(preferencias, 1500, antes: (_, m) =>
        {
            lock (placaNvs.Nvs) foreach (var (k, v) in placaNvs.Nvs) m.Nvs[k] = v;
        });
        Conferir("arduino-esp32: o Preferences de verdade le o que o nvs.h gravou, e os tipos sao os do NVS",
            nativo && erro is null && serial?.Contains("do idf -42 placa") == true && serial.Contains("volta 77 tipo 5"), $"{nativo} {erro} | {serial}");

        // ---- a UART: o driver, a escrita, a leitura e a fila de eventos ----
        const string uart = """
            #include <stdio.h>
            #include "driver/uart.h"
            #include "freertos/FreeRTOS.h"
            #include "freertos/queue.h"
            void app_main(void) {
              uart_config_t c = {};
              c.baud_rate = 115200;
              c.data_bits = UART_DATA_8_BITS;
              c.parity = UART_PARITY_DISABLE;
              c.stop_bits = UART_STOP_BITS_1;
              c.flow_ctrl = UART_HW_FLOWCTRL_DISABLE;
              c.source_clk = UART_SCLK_DEFAULT;
              QueueHandle_t fila;
              ESP_ERROR_CHECK(uart_driver_install(UART_NUM_0, 1024, 0, 10, &fila, 0));
              ESP_ERROR_CHECK(uart_param_config(UART_NUM_0, &c));
              uint32_t baud = 0;
              uart_get_baudrate(UART_NUM_0, &baud);
              printf("baud %lu\n", (unsigned long)baud);
              uart_write_bytes(UART_NUM_0, "pelo driver\n", 12);
              uart_event_t e;
              if (xQueueReceive(fila, &e, pdMS_TO_TICKS(3000)) && e.type == UART_DATA) printf("evento %d\n", (int)e.size);
              char b[16] = {0};
              int n = uart_read_bytes(UART_NUM_0, b, 5, pdMS_TO_TICKS(1000));
              printf("leu %d %s\n", n, b);
              n = uart_read_bytes(UART_NUM_0, b, 5, pdMS_TO_TICKS(50));
              printf("vazio %d\n", n);
              printf("fim\n");
            }
            """;
        bool mandouUart = false;
        (serial, erro, nativo, _) = RodarNativo(uart, 4000, durante: (Maquina m) =>
        {
            if (mandouUart || !SerialTem(m, "pelo driver")) return;
            mandouUart = true;
            m.EntradaSerial.Adicionar("hello");
        });
        Conferir("ESP-IDF: driver/uart.h -- instala, configura, escreve, recebe (com o UART_DATA na fila) e o read respeita o prazo",
            nativo && erro is null && serial?.Contains("baud 115200") == true && serial.Contains("pelo driver") && serial.Contains("evento") && serial.Contains("leu 5 hello") && serial.Contains("vazio 0"), $"{nativo} {erro} | {serial}");

        // ---- o I2C: o driver antigo e o novo, no barramento com um aparelho ----
        const string i2cAntigo = """
            #include <stdio.h>
            #include "driver/i2c.h"
            void app_main(void) {
              i2c_config_t c = {};
              c.mode = I2C_MODE_MASTER;
              c.sda_io_num = 21;
              c.scl_io_num = 22;
              c.master.clk_speed = 100000;
              ESP_ERROR_CHECK(i2c_param_config(I2C_NUM_0, &c));
              ESP_ERROR_CHECK(i2c_driver_install(I2C_NUM_0, I2C_MODE_MASTER, 0, 0, 0));
              uint8_t d[2] = {1, 2};
              printf("presente %s\n", esp_err_to_name(i2c_master_write_to_device(I2C_NUM_0, 0x27, d, 2, 100)));
              printf("ausente %s\n", esp_err_to_name(i2c_master_write_to_device(I2C_NUM_0, 0x55, d, 2, 100)));
              i2c_cmd_handle_t k = i2c_cmd_link_create();
              i2c_master_start(k);
              i2c_master_write_byte(k, 0x27 << 1, true);
              i2c_master_stop(k);
              printf("lista %s\n", esp_err_to_name(i2c_master_cmd_begin(I2C_NUM_0, k, 100)));
              i2c_cmd_link_delete(k);
              printf("fim\n");
            }
            """;
        (serial, erro, nativo, _) = RodarNativo(i2cAntigo, 1500, antes: (_, m) => m.I2c.Ligar(0x27, "LCD"));
        Conferir("ESP-IDF: driver/i2c.h -- o aparelho responde (write_to_device e a lista de comandos) e o ausente da ESP_FAIL",
            nativo && erro is null && serial?.Contains("presente ESP_OK") == true && serial.Contains("ausente ESP_FAIL") && serial.Contains("lista ESP_OK"), $"{nativo} {erro} | {serial}");

        const string i2cNovo = """
            #include <stdio.h>
            #include "driver/i2c_master.h"
            void app_main(void) {
              i2c_master_bus_config_t b = {};
              b.i2c_port = -1;
              b.sda_io_num = GPIO_NUM_21;
              b.scl_io_num = GPIO_NUM_22;
              b.clk_source = I2C_CLK_SRC_DEFAULT;
              i2c_master_bus_handle_t bus;
              ESP_ERROR_CHECK(i2c_new_master_bus(&b, &bus));
              printf("sonda %s %s\n", esp_err_to_name(i2c_master_probe(bus, 0x27, 100)), esp_err_to_name(i2c_master_probe(bus, 0x55, 100)));
              i2c_device_config_t d = {};
              d.dev_addr_length = I2C_ADDR_BIT_LEN_7;
              d.device_address = 0x27;
              d.scl_speed_hz = 100000;
              i2c_master_dev_handle_t aparelho;
              ESP_ERROR_CHECK(i2c_master_bus_add_device(bus, &d, &aparelho));
              uint8_t v[1] = {3};
              printf("envia %s\n", esp_err_to_name(i2c_master_transmit(aparelho, v, 1, 100)));
              ESP_ERROR_CHECK(i2c_master_bus_rm_device(aparelho));
              ESP_ERROR_CHECK(i2c_del_master_bus(bus));
              printf("fim\n");
            }
            """;
        (serial, erro, nativo, _) = RodarNativo(i2cNovo, 1500, antes: (_, m) => m.I2c.Ligar(0x27, "LCD"));
        Conferir("ESP-IDF: driver/i2c_master.h -- o barramento, a sonda (ESP_ERR_NOT_FOUND no ausente) e o transmit",
            nativo && erro is null && serial?.Contains("sonda ESP_OK ESP_ERR_NOT_FOUND") == true && serial.Contains("envia ESP_OK") && serial.Contains("fim"), $"{nativo} {erro} | {serial}");

        // ---- o SPI: o CS desce so durante a transacao ----
        const string spi = """
            #include <stdio.h>
            #include "driver/spi_master.h"
            #include "driver/gpio.h"
            void app_main(void) {
              spi_bus_config_t b = {};
              b.mosi_io_num = 23;
              b.miso_io_num = 19;
              b.sclk_io_num = 18;
              b.quadwp_io_num = -1;
              b.quadhd_io_num = -1;
              ESP_ERROR_CHECK(spi_bus_initialize(SPI2_HOST, &b, SPI_DMA_CH_AUTO));
              printf("de novo %s\n", esp_err_to_name(spi_bus_initialize(SPI2_HOST, &b, SPI_DMA_CH_AUTO)));
              spi_device_interface_config_t d = {};
              d.clock_speed_hz = 1000000;
              d.mode = 0;
              d.spics_io_num = 5;
              d.queue_size = 2;
              spi_device_handle_t h;
              ESP_ERROR_CHECK(spi_bus_add_device(SPI2_HOST, &d, &h));
              printf("cs parado %d\n", gpio_get_level(GPIO_NUM_5));
              spi_transaction_t t = {};
              uint8_t tx[2] = {0xA5, 0x5A};
              t.length = 16;
              t.tx_buffer = tx;
              ESP_ERROR_CHECK(spi_device_transmit(h, &t));
              spi_transaction_t *feita;
              t.flags = SPI_TRANS_USE_TXDATA;
              t.tx_data[0] = 1;
              t.length = 8;
              ESP_ERROR_CHECK(spi_device_queue_trans(h, &t, 10));
              printf("fila %s %d\n", esp_err_to_name(spi_device_get_trans_result(h, &feita, 10)), feita == &t);
              printf("fim\n");
            }
            """;
        (serial, erro, nativo, var placaSpi) = RodarNativo(spi, 1500);
        int descidasDoCs;
        lock (placaSpi.Trava) descidasDoCs = placaSpi.Eventos.Count(e => e.Pino == 5 && e.Valor == 0);
        Conferir("ESP-IDF: driver/spi_master.h -- o barramento, o dispositivo com o CS em alto parado e baixo em cada transacao, e a fila",
            nativo && erro is null && serial?.Contains("de novo ESP_ERR_INVALID_STATE") == true && serial.Contains("cs parado 1") && serial.Contains("fila ESP_OK 1") && descidasDoCs == 2, $"{nativo} {erro} | {serial} | cs {descidasDoCs}");

        // ---- os temporizadores: GPTimer com alarme periodico e o antigo por grupo ----
        const string temporizadores = """
            #include <stdio.h>
            #include "driver/gptimer.h"
            #include "freertos/FreeRTOS.h"
            #include "freertos/task.h"
            static volatile int alarmes = 0;
            static bool aoAlarme(gptimer_handle_t t, const gptimer_alarm_event_data_t *e, void *ctx) { alarmes++; return false; }
            void app_main(void) {
              gptimer_config_t c = {};
              c.clk_src = GPTIMER_CLK_SRC_DEFAULT;
              c.direction = GPTIMER_COUNT_UP;
              c.resolution_hz = 1000000;
              gptimer_handle_t t;
              ESP_ERROR_CHECK(gptimer_new_timer(&c, &t));
              gptimer_event_callbacks_t cb = {};
              cb.on_alarm = aoAlarme;
              ESP_ERROR_CHECK(gptimer_register_event_callbacks(t, &cb, NULL));
              gptimer_alarm_config_t a = {};
              a.alarm_count = 20000;
              a.flags.auto_reload_on_alarm = true;
              ESP_ERROR_CHECK(gptimer_set_alarm_action(t, &a));
              printf("sem enable %s\n", esp_err_to_name(gptimer_start(t)));
              ESP_ERROR_CHECK(gptimer_enable(t));
              ESP_ERROR_CHECK(gptimer_start(t));
              vTaskDelay(pdMS_TO_TICKS(210));
              ESP_ERROR_CHECK(gptimer_stop(t));
              printf("alarmes %s\n", alarmes >= 5 && alarmes <= 14 ? "ok" : "fora");
              printf("fim\n");
            }
            """;
        (serial, erro, nativo, _) = RodarNativo(temporizadores, 2000);
        Conferir("ESP-IDF: driver/gptimer.h -- start sem enable recusa, e o alarme periodico de 20 ms chega umas 10 vezes em 210 ms",
            nativo && erro is null && serial?.Contains("sem enable ESP_ERR_INVALID_STATE") == true && serial.Contains("alarmes ok"), $"{nativo} {erro} | {serial}");

        // ---- as contas: CRC, MAC, calibracao do ADC, temperatura, cJSON, watchdog ----
        const string contas = """
            #include <stdio.h>
            #include <string.h>
            #include "esp_rom_crc.h"
            #include "esp_mac.h"
            #include "esp_idf_version.h"
            #include "esp_system.h"
            #include "esp_adc/adc_cali.h"
            #include "esp_adc/adc_cali_scheme.h"
            #include "driver/temperature_sensor.h"
            #include "driver/ledc.h"
            #include "cJSON.h"
            #include "esp_task_wdt.h"
            #include "freertos/FreeRTOS.h"
            #include "freertos/task.h"
            void app_main(void) {
              printf("crc32 %08lx\n", (unsigned long)esp_rom_crc32_le(0, (const uint8_t *)"123456789", 9));
              printf("crc16 %04x\n", esp_rom_crc16_be(0, (const uint8_t *)"123456789", 9));
              uint8_t sta[6], ap[6];
              esp_read_mac(sta, ESP_MAC_WIFI_STA);
              esp_read_mac(ap, ESP_MAC_WIFI_SOFTAP);
              printf("mac %d\n", (uint8_t)(ap[5] - sta[5]));
              printf("versao %s %d\n", esp_get_idf_version(), ESP_IDF_VERSION_MAJOR);
              cJSON *o = cJSON_Parse("{\"a\":5,\"b\":[1,2]}");
              printf("json %d %d\n", cJSON_GetObjectItem(o, "a")->valueint, cJSON_GetArraySize(cJSON_GetObjectItem(o, "b")));
              cJSON_AddStringToObject(o, "c", "x");
              char *texto = cJSON_PrintUnformatted(o);
              printf("%s\n", texto);
              free(texto);
              cJSON_Delete(o);
              temperature_sensor_config_t tc = {.range_min = 10, .range_max = 50};
              temperature_sensor_handle_t ts;
              ESP_ERROR_CHECK(temperature_sensor_install(&tc, &ts));
              float g;
              printf("temp sem ligar %s\n", esp_err_to_name(temperature_sensor_get_celsius(ts, &g)));
              printf("res %d\n", (int)ledc_find_suitable_duty_resolution(80000000, 5000));
              esp_task_wdt_config_t w = {.timeout_ms = 100, .idle_core_mask = 0, .trigger_panic = false};
              ESP_ERROR_CHECK(esp_task_wdt_init(&w));
              ESP_ERROR_CHECK(esp_task_wdt_add(NULL));
              vTaskDelay(pdMS_TO_TICKS(300));
              printf("fim\n");
            }
            """;
        (serial, erro, nativo, _) = RodarNativo(contas, 2000);
        Conferir("ESP-IDF: CRC da ROM (os valores de referencia de \"123456789\"), MAC por interface, versao, cJSON de verdade e sensor de temperatura",
            nativo && erro is null && serial?.Contains("crc32 cbf43926") == true && serial.Contains("crc16 d64e") && serial.Contains("mac 1") && serial.Contains("versao v5.5.1 5")
            && serial.Contains("json 5 2") && serial.Contains("{\"a\":5,\"b\":[1,2],\"c\":\"x\"}") && serial.Contains("temp sem ligar ESP_ERR_INVALID_STATE") && serial.Contains("res 13"), $"{nativo} {erro} | {serial}");
        Conferir("ESP-IDF: esp_task_wdt -- a tarefa inscrita que nao chama o reset aparece na serial",
            serial?.Contains("Task watchdog got triggered") == true, serial);

        // ---- os arquivos: fopen no SPIFFS montado, stat, remove e as pastas no FAT ----
        const string arquivos = """
            #include <stdio.h>
            #include <string.h>
            #include <sys/stat.h>
            #include <dirent.h>
            #include <fcntl.h>
            #include <unistd.h>
            #include "esp_spiffs.h"
            #include "esp_vfs_fat.h"
            void app_main(void) {
              esp_vfs_spiffs_conf_t c = {};
              c.base_path = "/spiffs";
              c.max_files = 5;
              c.format_if_mount_failed = true;
              ESP_ERROR_CHECK(esp_vfs_spiffs_register(&c));
              printf("montou %d\n", esp_spiffs_mounted(NULL));
              printf("nao existe %d\n", fopen("/spiffs/nada.txt", "r") == NULL);
              FILE *f = fopen("/spiffs/a.txt", "w");
              fprintf(f, "ola %d\n", 42);
              fclose(f);
              f = fopen("/spiffs/a.txt", "a");
              fputs("mais\n", f);
              fclose(f);
              char l[32];
              f = fopen("/spiffs/a.txt", "r");
              fgets(l, sizeof(l), f);
              printf("linha %s", l);
              fgets(l, sizeof(l), f);
              printf("linha %s", l);
              fclose(f);
              struct stat st;
              printf("stat %d %d\n", stat("/spiffs/a.txt", &st), (int)st.st_size);
              printf("stat nada %d\n", stat("/spiffs/nada.txt", &st));
              printf("renomeia %d\n", rename("/spiffs/a.txt", "/spiffs/b.txt"));
              int fd = open("/spiffs/b.txt", O_RDONLY);
              char b[4] = {0};
              read(fd, b, 3);
              close(fd);
              printf("posix %s\n", b);
              printf("remove %d %d\n", remove("/spiffs/b.txt"), stat("/spiffs/b.txt", &st));
              size_t total = 0, usado = 0;
              esp_spiffs_info(NULL, &total, &usado);
              printf("info %d\n", total > 0);
              esp_vfs_spiffs_unregister(NULL);
              esp_vfs_fat_mount_config_t m = {};
              m.format_if_mount_failed = true;
              m.max_files = 4;
              wl_handle_t wl;
              ESP_ERROR_CHECK(esp_vfs_fat_spiflash_mount_rw_wl("/fat", "ffat", &m, &wl));
              printf("mkdir %d\n", mkdir("/fat/pasta", 0777));
              f = fopen("/fat/pasta/x.bin", "wb");
              fwrite("\x01\x00\x02", 1, 3, f);
              fclose(f);
              DIR *d = opendir("/fat/pasta");
              struct dirent *e = readdir(d);
              printf("pasta %s %d\n", e->d_name, e->d_type == DT_REG);
              closedir(d);
              f = fopen("/fat/pasta/x.bin", "rb");
              uint8_t v[3];
              fread(v, 1, 3, f);
              fclose(f);
              printf("binario %d %d %d\n", v[0], v[1], v[2]);
              printf("fim\n");
            }
            """;
        (serial, erro, nativo, _) = RodarNativo(arquivos, 3000);
        Conferir("ESP-IDF: esp_vfs_spiffs -- fopen/fprintf/fgets no /spiffs, o append, stat, rename, open/read e remove nos arquivos da placa",
            nativo && erro is null && serial?.Contains("montou 1") == true && serial.Contains("nao existe 1") && serial.Contains("linha ola 42") && serial.Contains("linha mais")
            && serial.Contains("stat 0 12") && serial.Contains("stat nada -1") && serial.Contains("renomeia 0") && serial.Contains("posix ola") && serial.Contains("remove 0 -1") && serial.Contains("info 1"), $"{nativo} {erro} | {serial}");
        Conferir("ESP-IDF: esp_vfs_fat -- mkdir, opendir/readdir e arquivo binario na flash",
            serial?.Contains("mkdir 0") == true && serial.Contains("pasta x.bin 1") && serial.Contains("binario 1 0 2"), serial);

        // ---- PCNT, MCPWM, RMT, temporizador antigo, DAC e I2S ----
        const string perifericos = """
            #include <stdio.h>
            #include "driver/pulse_cnt.h"
            #include "driver/gpio.h"
            #include "driver/mcpwm_prelude.h"
            #include "driver/rmt_tx.h"
            #include "driver/rmt_encoder.h"
            #include "driver/timer.h"
            #include "driver/i2s_std.h"
            #include "freertos/FreeRTOS.h"
            #include "freertos/task.h"
            static volatile int alarmesAntigo = 0;
            static bool aoAntigo(void *a) { alarmesAntigo++; return false; }
            void app_main(void) {
              /* o contador: o pino 4 como saida e como borda do contador */
              pcnt_unit_config_t uc = {.low_limit = -100, .high_limit = 100};
              pcnt_unit_handle_t u;
              ESP_ERROR_CHECK(pcnt_new_unit(&uc, &u));
              pcnt_chan_config_t cc = {.edge_gpio_num = 4, .level_gpio_num = -1};
              pcnt_channel_handle_t ch;
              ESP_ERROR_CHECK(pcnt_new_channel(u, &cc, &ch));
              pcnt_channel_set_edge_action(ch, PCNT_CHANNEL_EDGE_ACTION_INCREASE, PCNT_CHANNEL_EDGE_ACTION_HOLD);
              ESP_ERROR_CHECK(pcnt_unit_enable(u));
              ESP_ERROR_CHECK(pcnt_unit_start(u));
              gpio_set_direction(GPIO_NUM_4, GPIO_MODE_INPUT_OUTPUT);
              for (int i = 0; i < 5; i++) { gpio_set_level(GPIO_NUM_4, 1); vTaskDelay(2); gpio_set_level(GPIO_NUM_4, 0); vTaskDelay(2); }
              int contagem = 0;
              pcnt_unit_get_count(u, &contagem);
              printf("pulsos %d\n", contagem);

              /* o MCPWM do exemplo do servo: 50 Hz, compare de 1500 de 20000 */
              mcpwm_timer_config_t tc = {.group_id = 0, .clk_src = MCPWM_TIMER_CLK_SRC_DEFAULT, .resolution_hz = 1000000, .count_mode = MCPWM_TIMER_COUNT_MODE_UP, .period_ticks = 20000};
              mcpwm_timer_handle_t t;
              ESP_ERROR_CHECK(mcpwm_new_timer(&tc, &t));
              mcpwm_operator_config_t oc = {.group_id = 0};
              mcpwm_oper_handle_t o;
              ESP_ERROR_CHECK(mcpwm_new_operator(&oc, &o));
              ESP_ERROR_CHECK(mcpwm_operator_connect_timer(o, t));
              mcpwm_comparator_config_t cpc = {};
              mcpwm_cmpr_handle_t c;
              ESP_ERROR_CHECK(mcpwm_new_comparator(o, &cpc, &c));
              mcpwm_generator_config_t gc = {.gen_gpio_num = 18};
              mcpwm_gen_handle_t g;
              ESP_ERROR_CHECK(mcpwm_new_generator(o, &gc, &g));
              ESP_ERROR_CHECK(mcpwm_generator_set_action_on_timer_event(g, MCPWM_GEN_TIMER_EVENT_ACTION(MCPWM_TIMER_DIRECTION_UP, MCPWM_TIMER_EVENT_EMPTY, MCPWM_GEN_ACTION_HIGH)));
              ESP_ERROR_CHECK(mcpwm_generator_set_action_on_compare_event(g, MCPWM_GEN_COMPARE_EVENT_ACTION(MCPWM_TIMER_DIRECTION_UP, c, MCPWM_GEN_ACTION_LOW)));
              ESP_ERROR_CHECK(mcpwm_timer_enable(t));
              ESP_ERROR_CHECK(mcpwm_timer_start_stop(t, MCPWM_TIMER_START_NO_STOP));
              ESP_ERROR_CHECK(mcpwm_comparator_set_compare_value(c, 10000));
              vTaskDelay(pdMS_TO_TICKS(20));
              printf("mcpwm ok\n");

              /* o RMT com o codificador de bytes */
              rmt_tx_channel_config_t rc = {.gpio_num = 5, .clk_src = RMT_CLK_SRC_DEFAULT, .resolution_hz = 1000000, .mem_block_symbols = 64, .trans_queue_depth = 4};
              rmt_channel_handle_t rt;
              ESP_ERROR_CHECK(rmt_new_tx_channel(&rc, &rt));
              rmt_bytes_encoder_config_t bc = {};
              bc.bit0.level0 = 1; bc.bit0.duration0 = 100; bc.bit0.level1 = 0; bc.bit0.duration1 = 100;
              bc.bit1.level0 = 1; bc.bit1.duration0 = 200; bc.bit1.level1 = 0; bc.bit1.duration1 = 100;
              bc.flags.msb_first = 1;
              rmt_encoder_handle_t e;
              ESP_ERROR_CHECK(rmt_new_bytes_encoder(&bc, &e));
              uint8_t dado = 0xA5;
              rmt_transmit_config_t tx = {};
              printf("sem enable %s\n", esp_err_to_name(rmt_transmit(rt, e, &dado, 1, &tx)));
              ESP_ERROR_CHECK(rmt_enable(rt));
              ESP_ERROR_CHECK(rmt_transmit(rt, e, &dado, 1, &tx));
              ESP_ERROR_CHECK(rmt_tx_wait_all_done(rt, 100));
              printf("rmt ok\n");

              /* o temporizador antigo por grupo */
              timer_config_t a = {.alarm_en = TIMER_ALARM_EN, .counter_en = TIMER_PAUSE, .intr_type = TIMER_INTR_LEVEL, .counter_dir = TIMER_COUNT_UP, .auto_reload = TIMER_AUTORELOAD_EN, .divider = 80};
              ESP_ERROR_CHECK(timer_init(TIMER_GROUP_0, TIMER_0, &a));
              timer_set_counter_value(TIMER_GROUP_0, TIMER_0, 0);
              timer_set_alarm_value(TIMER_GROUP_0, TIMER_0, 20000);
              timer_enable_intr(TIMER_GROUP_0, TIMER_0);
              timer_isr_callback_add(TIMER_GROUP_0, TIMER_0, aoAntigo, NULL, 0);
              timer_start(TIMER_GROUP_0, TIMER_0);
              vTaskDelay(pdMS_TO_TICKS(110));
              timer_pause(TIMER_GROUP_0, TIMER_0);
              printf("antigo %s\n", alarmesAntigo >= 2 && alarmesAntigo <= 8 ? "ok" : "fora");

              /* o I2S novo */
              i2s_chan_config_t ic = I2S_CHANNEL_DEFAULT_CONFIG(I2S_NUM_AUTO, I2S_ROLE_MASTER);
              i2s_chan_handle_t saida;
              ESP_ERROR_CHECK(i2s_new_channel(&ic, &saida, NULL));
              i2s_std_config_t sc = {.clk_cfg = I2S_STD_CLK_DEFAULT_CONFIG(16000), .slot_cfg = I2S_STD_PHILIPS_SLOT_DEFAULT_CONFIG(I2S_DATA_BIT_WIDTH_16BIT, I2S_SLOT_MODE_STEREO)};
              sc.gpio_cfg.bclk = 26; sc.gpio_cfg.ws = 25; sc.gpio_cfg.dout = 22; sc.gpio_cfg.din = I2S_GPIO_UNUSED; sc.gpio_cfg.mclk = I2S_GPIO_UNUSED;
              ESP_ERROR_CHECK(i2s_channel_init_std_mode(saida, &sc));
              int16_t amostras[8] = {0};
              size_t feitos = 0;
              printf("i2s desligado %s\n", esp_err_to_name(i2s_channel_write(saida, amostras, sizeof(amostras), &feitos, 10)));
              ESP_ERROR_CHECK(i2s_channel_enable(saida));
              ESP_ERROR_CHECK(i2s_channel_write(saida, amostras, sizeof(amostras), &feitos, 100));
              printf("i2s %d\n", (int)feitos);
              printf("fim\n");
            }
            """;
        (serial, erro, nativo, var placaPerif) = RodarNativo(perifericos, 3000);
        int bordasRmt, dutyMcpwm;
        lock (placaPerif.Trava) bordasRmt = placaPerif.Eventos.Count(ev => ev.Pino == 5);
        dutyMcpwm = placaPerif.Pwm[18];
        Conferir("ESP-IDF: driver/pulse_cnt.h -- o contador soma as 5 subidas do pino com a acao configurada",
            nativo && erro is null && serial?.Contains("pulsos 5") == true, $"{nativo} {erro} | {serial}");
        Conferir("ESP-IDF: MCPWM (o exemplo do servo) -- temporizador, operador, comparador e gerador viram PWM no pino, com 50% do compare",
            serial?.Contains("mcpwm ok") == true && dutyMcpwm is >= 120 and <= 135, $"{serial} | duty {dutyMcpwm}");
        Conferir("ESP-IDF: RMT novo -- transmitir sem enable recusa, e o codificador de bytes solta os pulsos no pino",
            serial?.Contains("sem enable ESP_ERR_INVALID_STATE") == true && serial.Contains("rmt ok") && bordasRmt >= 8, $"{serial} | bordas {bordasRmt}");
        Conferir("ESP-IDF: temporizador antigo (driver/timer.h) com o alarme de 20 ms e o I2S novo (canal, modo padrao, write)",
            serial?.Contains("antigo ok") == true && serial.Contains("i2s desligado ESP_ERR_INVALID_STATE") && serial.Contains("i2s 16") && serial.Contains("fim"), serial);

        // ---- o TWAI entre duas placas ----
        const string twaiA = """
            #include <stdio.h>
            #include "driver/twai.h"
            #include "freertos/FreeRTOS.h"
            #include "freertos/task.h"
            void app_main(void) {
              twai_general_config_t g = TWAI_GENERAL_CONFIG_DEFAULT(GPIO_NUM_21, GPIO_NUM_22, TWAI_MODE_NORMAL);
              twai_timing_config_t t = TWAI_TIMING_CONFIG_500KBITS();
              twai_filter_config_t f = TWAI_FILTER_CONFIG_ACCEPT_ALL();
              ESP_ERROR_CHECK(twai_driver_install(&g, &t, &f));
              ESP_ERROR_CHECK(twai_start());
              vTaskDelay(pdMS_TO_TICKS(300));
              twai_message_t m = {.identifier = 0x123, .data_length_code = 3, .data = {1, 2, 3}};
              printf("envia %s\n", esp_err_to_name(twai_transmit(&m, 100)));
              printf("fim\n");
            }
            """;
        const string twaiB = """
            #include <stdio.h>
            #include "driver/twai.h"
            void app_main(void) {
              twai_general_config_t g = TWAI_GENERAL_CONFIG_DEFAULT(GPIO_NUM_21, GPIO_NUM_22, TWAI_MODE_NORMAL);
              twai_timing_config_t t = TWAI_TIMING_CONFIG_500KBITS();
              twai_filter_config_t f = TWAI_FILTER_CONFIG_ACCEPT_ALL();
              ESP_ERROR_CHECK(twai_driver_install(&g, &t, &f));
              ESP_ERROR_CHECK(twai_start());
              twai_message_t m;
              esp_err_t e = twai_receive(&m, pdMS_TO_TICKS(2000));
              printf("recebe %s %x %d %d\n", esp_err_to_name(e), (unsigned)m.identifier, m.data_length_code, m.data[2]);
              printf("nada %s\n", esp_err_to_name(twai_receive(&m, 20)));
            }
            """;
        var (serA, serB, erroCan) = RodarDuasNativas(twaiA, twaiB, "esp32-devkit-v1", 4000, (a, b) => b.Contains("nada"));
        Conferir("ESP-IDF: TWAI (CAN) -- o quadro de uma placa chega na outra com o identificador e os dados, e o receive respeita o prazo",
            erroCan is null && serA?.Contains("envia ESP_OK") == true && serB?.Contains("recebe ESP_OK 123 3 3") == true && serB.Contains("nada ESP_ERR_TIMEOUT"), $"{erroCan} | {serA} || {serB}");

        // ---- o Bluedroid: servidor GATT do IDF e cliente Arduino na outra placa ----
        const string servidorBle = """
            #include <stdio.h>
            #include <string.h>
            #include "esp_bt.h"
            #include "esp_bt_main.h"
            #include "esp_gap_ble_api.h"
            #include "esp_gatts_api.h"
            #include "nvs_flash.h"
            static uint16_t servico, caracteristica;
            static esp_bt_uuid_t uuidServico = {.len = ESP_UUID_LEN_16, .uuid = {.uuid16 = 0x00FF}};
            static esp_bt_uuid_t uuidCaract = {.len = ESP_UUID_LEN_16, .uuid = {.uuid16 = 0xFF01}};
            static uint8_t inicial[] = {'o', 'l', 'a'};
            static esp_attr_value_t valor = {.attr_max_len = 16, .attr_len = 3, .attr_value = inicial};
            static esp_ble_adv_params_t params = {.adv_int_min = 0x20, .adv_int_max = 0x40, .adv_type = ADV_TYPE_IND, .own_addr_type = BLE_ADDR_TYPE_PUBLIC, .channel_map = ADV_CHNL_ALL};
            static void gap(esp_gap_ble_cb_event_t e, esp_ble_gap_cb_param_t *p) {
              if (e == ESP_GAP_BLE_ADV_DATA_RAW_SET_COMPLETE_EVT) esp_ble_gap_start_advertising(&params);
              if (e == ESP_GAP_BLE_ADV_START_COMPLETE_EVT) printf("anunciando %d\n", p->adv_start_cmpl.status);
            }
            static void gatts(esp_gatts_cb_event_t e, esp_gatt_if_t i, esp_ble_gatts_cb_param_t *p) {
              if (e == ESP_GATTS_REG_EVT) {
                esp_ble_gap_set_device_name("idf-ble");
                static uint8_t bruto[] = {2, 1, 6, 8, 9, 'i', 'd', 'f', '-', 'b', 'l', 'e'};
                esp_ble_gap_config_adv_data_raw(bruto, sizeof(bruto));
                esp_gatt_srvc_id_t id = {.is_primary = true, .id = {.uuid = uuidServico, .inst_id = 0}};
                esp_ble_gatts_create_service(i, &id, 4);
              } else if (e == ESP_GATTS_CREATE_EVT) {
                servico = p->create.service_handle;
                esp_ble_gatts_start_service(servico);
                esp_attr_control_t c = {.auto_rsp = ESP_GATT_AUTO_RSP};
                esp_ble_gatts_add_char(servico, &uuidCaract, ESP_GATT_PERM_READ | ESP_GATT_PERM_WRITE, ESP_GATT_CHAR_PROP_BIT_READ | ESP_GATT_CHAR_PROP_BIT_WRITE, &valor, &c);
              } else if (e == ESP_GATTS_ADD_CHAR_EVT) {
                caracteristica = p->add_char.attr_handle;
                printf("caracteristica %d\n", caracteristica != 0);
              } else if (e == ESP_GATTS_CONNECT_EVT) {
                printf("conectou\n");
              } else if (e == ESP_GATTS_WRITE_EVT) {
                printf("escreveu %.*s %d\n", p->write.len, p->write.value, p->write.handle == caracteristica);
              }
            }
            void app_main(void) {
              nvs_flash_init();
              esp_bt_controller_config_t c = BT_CONTROLLER_INIT_CONFIG_DEFAULT();
              printf("sem init %s\n", esp_err_to_name(esp_bluedroid_enable()));
              ESP_ERROR_CHECK(esp_bt_controller_init(&c));
              ESP_ERROR_CHECK(esp_bt_controller_enable(ESP_BT_MODE_BLE));
              ESP_ERROR_CHECK(esp_bluedroid_init());
              ESP_ERROR_CHECK(esp_bluedroid_enable());
              esp_ble_gatts_register_callback(gatts);
              esp_ble_gap_register_callback(gap);
              ESP_ERROR_CHECK(esp_ble_gatts_app_register(0));
            }
            """;
        const string clienteBle = """
            #include <BLEDevice.h>
            void setup() {
              Serial.begin(115200);
              delay(500);
              BLEDevice::init("cliente");
              BLEScan *s = BLEDevice::getScan();
              BLEScanResults *r = s->start(1, false);
              for (int i = 0; i < r->getCount(); i++) {
                BLEAdvertisedDevice d = r->getDevice(i);
                if (d.getName() != "idf-ble") continue;
                Serial.println("achou");
                BLEClient *c = BLEDevice::createClient();
                c->connect(&d);
                BLERemoteService *sv = c->getService(BLEUUID((uint16_t)0x00FF));
                BLERemoteCharacteristic *k = sv->getCharacteristic(BLEUUID((uint16_t)0xFF01));
                Serial.println(String("leu ") + k->readValue());
                k->writeValue("oi", true);
                Serial.println("fim");
              }
            }
            void loop() { delay(10); }
            """;
        (serA, serB, erroCan) = RodarDuasNativas(servidorBle, clienteBle, "esp32-devkit-v1", 6000, (a, b) => b.Contains("fim") && a.Contains("escreveu"));
        Conferir("ESP-IDF: Bluedroid -- a ordem do controlador, o REG/CREATE/ADD_CHAR nos eventos e o anuncio cru no ar",
            erroCan is null && serA?.Contains("sem init ESP_ERR_INVALID_STATE") == true && serA.Contains("caracteristica 1") && serA.Contains("anunciando 0") && serB?.Contains("achou") == true, $"{erroCan} | {serA} || {serB}");
        Conferir("ESP-IDF: Bluedroid -- o cliente da outra placa conecta, le o valor inicial, e a escrita chega no ESP_GATTS_WRITE_EVT",
            serA?.Contains("conectou") == true && serB?.Contains("leu ola") == true && serA.Contains("escreveu oi 1"), $"{serA} || {serB}");

        // ---- a rede de verdade: esp_http_client contra um servidor do computador ----
        using var ouvinte = new TcpListener(IPAddress.Loopback, 0);
        ouvinte.Start();
        int portaHttp = ((IPEndPoint)ouvinte.LocalEndpoint).Port;
        string pedidoVisto = "";
        var servidor = Task.Run(() =>
        {
            for (int i = 0; i < 2; i++)
            {
                using var c = ouvinte.AcceptTcpClient();
                var s = c.GetStream();
                var b = new byte[2048];
                int n = s.Read(b, 0, b.Length);
                string pedido = Encoding.ASCII.GetString(b, 0, n);
                pedidoVisto += pedido;
                while (pedido.StartsWith("POST") && !pedido.Contains("corpo") && (n = s.Read(b, 0, b.Length)) > 0) { pedido += Encoding.ASCII.GetString(b, 0, n); pedidoVisto += Encoding.ASCII.GetString(b, 0, n); }
                byte[] r = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: text/plain\r\nContent-Length: 5\r\nConnection: close\r\n\r\nolaaa");
                s.Write(r, 0, r.Length);
            }
        });
        string clienteHttp = """
            #include <stdio.h>
            #include <string.h>
            #include "esp_http_client.h"
            #include "esp_wifi.h"
            #include "esp_event.h"
            #include "esp_netif.h"
            #include "nvs_flash.h"
            #include "freertos/FreeRTOS.h"
            #include "freertos/task.h"
            static int eventos = 0;
            static esp_err_t aoEvento(esp_http_client_event_t *e) { if (e->event_id == HTTP_EVENT_ON_DATA) eventos += e->data_len; return ESP_OK; }
            void app_main(void) {
              nvs_flash_init();
              esp_netif_init();
              esp_event_loop_create_default();
              esp_netif_create_default_wifi_sta();
              wifi_init_config_t cfg = WIFI_INIT_CONFIG_DEFAULT();
              esp_wifi_init(&cfg);
              wifi_config_t w = {};
              strcpy((char *)w.sta.ssid, "CircuitSketch-WiFi");
              esp_wifi_set_mode(WIFI_MODE_STA);
              esp_wifi_set_config(WIFI_IF_STA, &w);
              esp_wifi_start();
              esp_wifi_connect();
              vTaskDelay(pdMS_TO_TICKS(1500));
              esp_http_client_config_t c = {};
              c.url = "http://127.0.0.1:PORTA/caminho?x=1";
              c.event_handler = aoEvento;
              esp_http_client_handle_t h = esp_http_client_init(&c);
              esp_err_t e = esp_http_client_perform(h);
              printf("get %s %d %d %d\n", esp_err_to_name(e), esp_http_client_get_status_code(h), (int)esp_http_client_get_content_length(h), eventos);
              esp_http_client_cleanup(h);
              c.url = "http://127.0.0.1:PORTA/envia";
              c.method = HTTP_METHOD_POST;
              h = esp_http_client_init(&c);
              esp_http_client_set_header(h, "X-Teste", "sim");
              esp_http_client_set_post_field(h, "corpo", 5);
              e = esp_http_client_perform(h);
              printf("post %s %d\n", esp_err_to_name(e), esp_http_client_get_status_code(h));
              esp_http_client_cleanup(h);
              printf("fim\n");
            }
            """.Replace("PORTA", portaHttp.ToString());
        (serial, erro, nativo, _) = RodarNativo(clienteHttp, 8000);
        ouvinte.Stop();
        try { servidor.Wait(1000); } catch (AggregateException) { }
        Conferir("ESP-IDF: esp_http_client de verdade -- GET e POST a um servidor do computador, com os eventos, o status e os cabecalhos",
            nativo && erro is null && serial?.Contains("get ESP_OK 200 5 5") == true && serial.Contains("post ESP_OK 200") && pedidoVisto.Contains("GET /caminho?x=1") && pedidoVisto.Contains("X-Teste: sim") && pedidoVisto.Contains("corpo"), $"{nativo} {erro} | {serial} | {pedidoVisto}");

        // ---- o esp_http_server de verdade: o navegador do computador abre a pagina ----
        string servidorHttp = """
            #include <stdio.h>
            #include <string.h>
            #include "esp_http_server.h"
            #include "esp_wifi.h"
            #include "esp_event.h"
            #include "esp_netif.h"
            #include "nvs_flash.h"
            #include "freertos/FreeRTOS.h"
            #include "freertos/task.h"
            static esp_err_t raiz(httpd_req_t *r) {
              char q[32] = "";
              httpd_req_get_url_query_str(r, q, sizeof(q));
              char resposta[64];
              snprintf(resposta, sizeof(resposta), "ola do idf %s", q);
              httpd_resp_set_type(r, "text/plain");
              return httpd_resp_send(r, resposta, HTTPD_RESP_USE_STRLEN);
            }
            static esp_err_t recebe(httpd_req_t *r) {
              char b[32] = "";
              int n = httpd_req_recv(r, b, sizeof(b) - 1);
              printf("post %d %s\n", n, b);
              return httpd_resp_sendstr(r, "ok");
            }
            void app_main(void) {
              nvs_flash_init();
              esp_netif_init();
              esp_event_loop_create_default();
              esp_netif_create_default_wifi_sta();
              wifi_init_config_t cfg = WIFI_INIT_CONFIG_DEFAULT();
              esp_wifi_init(&cfg);
              wifi_config_t w = {};
              strcpy((char *)w.sta.ssid, "CircuitSketch-WiFi");
              esp_wifi_set_mode(WIFI_MODE_STA);
              esp_wifi_set_config(WIFI_IF_STA, &w);
              esp_wifi_start();
              esp_wifi_connect();
              vTaskDelay(pdMS_TO_TICKS(1000));
              httpd_handle_t s = NULL;
              httpd_config_t c = HTTPD_DEFAULT_CONFIG();
              c.server_port = 8090;
              printf("start %s\n", esp_err_to_name(httpd_start(&s, &c)));
              httpd_uri_t a = {.uri = "/", .method = HTTP_GET, .handler = raiz, .user_ctx = NULL};
              httpd_register_uri_handler(s, &a);
              httpd_uri_t b = {.uri = "/envia", .method = HTTP_POST, .handler = recebe, .user_ctx = NULL};
              httpd_register_uri_handler(s, &b);
              printf("no ar\n");
              for (;;) vTaskDelay(100);
            }
            """;
        string paginaIdf = "", naoAchou = "", resposta404 = "";
        bool pediu = false;
        (serial, erro, nativo, _) = RodarNativo(servidorHttp, 9000, durante: (Maquina m) =>
        {
            if (pediu || !SerialTem(m, "no ar") || m.Rede is null) return;
            var porta = m.Rede.Servidores.FirstOrDefault(s => s.Porta == 8090 && s.UrlLocal is not null);
            if (porta is null) return;
            pediu = true;
            var url = porta.UrlLocal.TrimEnd('/');
            Task.Run(async () =>
            {
                using var cliente = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(4) };
                paginaIdf = await cliente.GetStringAsync(url + "/?a=1");
                var post = await cliente.PostAsync(url + "/envia", new System.Net.Http.StringContent("dados"));
                naoAchou = await post.Content.ReadAsStringAsync();
                var r = await cliente.GetAsync(url + "/nada");
                resposta404 = ((int)r.StatusCode).ToString();
            });
        });
        Conferir("ESP-IDF: esp_http_server de verdade -- o navegador do computador abre a rota com a query, faz o POST e recebe 404 na rota que nao existe",
            nativo && erro is null && serial?.Contains("start ESP_OK") == true && paginaIdf == "ola do idf a=1" && naoAchou == "ok" && serial.Contains("post 5 dados") && resposta404 == "404", $"{nativo} {erro} | {serial} | {paginaIdf} | {naoAchou} | {resposta404}");

        // ---- o esp-mqtt de verdade, com um corretor minimo do computador ----
        using var corretor = new TcpListener(IPAddress.Loopback, 0);
        corretor.Start();
        int portaMqtt = ((IPEndPoint)corretor.LocalEndpoint).Port;
        string publicado = "";
        using var fim = new CancellationTokenSource();
        var trabalhoDoCorretor = Task.Run(() =>
        {
            try
            {
                using var c = corretor.AcceptTcpClient();
                c.ReceiveTimeout = 10000;
                var s = c.GetStream();
                var b = new byte[1024];
                while (!fim.IsCancellationRequested)
                {
                    int n = s.Read(b, 0, b.Length);
                    if (n <= 0) return;
                    int i = 0;
                    while (i < n)
                    {
                        int tipo = b[i] >> 4, tam = b[i + 1], corpo = i + 2;
                        if (tipo == 1) s.Write([0x20, 0x02, 0, 0]);
                        else if (tipo == 8) s.Write([0x90, 0x03, b[corpo], b[corpo + 1], 0]);
                        else if (tipo == 3)
                        {
                            int nt = b[corpo] << 8 | b[corpo + 1];
                            publicado = Encoding.ASCII.GetString(b, corpo + 2, nt) + "=" + Encoding.ASCII.GetString(b, corpo + 2 + nt, tam - 2 - nt);
                            // Devolve para quem assinou: o mesmo cliente.
                            s.Write(b, i, tam + 2);
                        }
                        else if (tipo == 12) s.Write([0xD0, 0]);
                        i = corpo + tam;
                    }
                }
            }
            catch (IOException) { }
            catch (SocketException) { }
        });
        string clienteMqtt = """
            #include <stdio.h>
            #include <string.h>
            #include "mqtt_client.h"
            #include "esp_wifi.h"
            #include "esp_event.h"
            #include "esp_netif.h"
            #include "nvs_flash.h"
            #include "freertos/FreeRTOS.h"
            #include "freertos/task.h"
            static void aoEvento(void *arg, esp_event_base_t base, int32_t id, void *dados) {
              esp_mqtt_event_handle_t e = dados;
              switch ((esp_mqtt_event_id_t)id) {
              case MQTT_EVENT_CONNECTED:
                printf("conectou\n");
                esp_mqtt_client_subscribe(e->client, "casa/luz", 0);
                break;
              case MQTT_EVENT_SUBSCRIBED:
                printf("assinou\n");
                esp_mqtt_client_publish(e->client, "casa/luz", "ligada", 0, 0, 0);
                break;
              case MQTT_EVENT_DATA:
                printf("chegou %.*s %.*s\n", e->topic_len, e->topic, e->data_len, e->data);
                break;
              default: break;
              }
            }
            void app_main(void) {
              nvs_flash_init();
              esp_netif_init();
              esp_event_loop_create_default();
              esp_netif_create_default_wifi_sta();
              wifi_init_config_t cfg = WIFI_INIT_CONFIG_DEFAULT();
              esp_wifi_init(&cfg);
              wifi_config_t w = {};
              strcpy((char *)w.sta.ssid, "CircuitSketch-WiFi");
              esp_wifi_set_mode(WIFI_MODE_STA);
              esp_wifi_set_config(WIFI_IF_STA, &w);
              esp_wifi_start();
              esp_wifi_connect();
              vTaskDelay(pdMS_TO_TICKS(1000));
              esp_mqtt_client_config_t c = {};
              c.broker.address.uri = "mqtt://127.0.0.1:PORTA";
              esp_mqtt_client_handle_t h = esp_mqtt_client_init(&c);
              esp_mqtt_client_register_event(h, ESP_EVENT_ANY_ID, aoEvento, NULL);
              esp_mqtt_client_start(h);
            }
            """.Replace("PORTA", portaMqtt.ToString());
        (serial, erro, nativo, _) = RodarNativo(clienteMqtt, 6000);
        fim.Cancel();
        corretor.Stop();
        Conferir("ESP-IDF: esp-mqtt de verdade -- conecta no corretor, assina, publica e recebe a mensagem no evento",
            nativo && erro is null && serial?.Contains("conectou") == true && serial.Contains("assinou") && publicado == "casa/luz=ligada" && serial.Contains("chegou casa/luz ligada"), $"{nativo} {erro} | {serial} | {publicado}");

        // ---- a flash crua, o eFuse, o ADC continuo, o DAC antigo e as perguntas da interface ----
        const string resto = """
            #include <stdio.h>
            #include <string.h>
            #include "esp_flash.h"
            #include "spi_flash_mmap.h"
            #include "esp_efuse.h"
            #include "esp_efuse_table.h"
            #include "esp_adc/adc_continuous.h"
            #include "driver/dac.h"
            #include "esp_netif.h"
            #include "esp_wifi.h"
            #include "freertos/FreeRTOS.h"
            #include "freertos/task.h"
            static volatile int quadros;
            static bool aoQuadro(adc_continuous_handle_t h, const adc_continuous_evt_data_t *e, void *c) { quadros++; return false; }
            void app_main(void) {
              const uint32_t base = 0x290000;
              uint32_t tam = 0, id = 0;
              esp_flash_get_physical_size(NULL, &tam);
              esp_flash_read_id(esp_flash_default_chip, &id);
              printf("flash %lu MB id %s\n", (unsigned long)(tam >> 20), id ? "ok" : "zero");
              printf("sem alinhar %s\n", esp_err_to_name(esp_flash_erase_region(NULL, base + 1, 4096)));
              ESP_ERROR_CHECK(esp_flash_erase_region(NULL, base, 4096));
              ESP_ERROR_CHECK(esp_flash_write(NULL, "circuito", base + 16, 9));
              char lido[9] = {0};
              ESP_ERROR_CHECK(esp_flash_read(NULL, lido, base + 16, 9));
              uint8_t apagado = 0;
              esp_flash_read(NULL, &apagado, base, 1);
              printf("flash leu %s apagado %02X\n", lido, apagado);
              const void *m;
              spi_flash_mmap_handle_t alca;
              ESP_ERROR_CHECK(spi_flash_mmap(base, 4096, SPI_FLASH_MMAP_DATA, &m, &alca));
              printf("mmap %s\n", (const char *)m + 16);
              spi_flash_munmap(alca);
              printf("efuse %d %d\n", (int)esp_efuse_read_reg(EFUSE_BLK0, 0), esp_efuse_get_coding_scheme(EFUSE_BLK3) == EFUSE_CODING_SCHEME_NONE);

              adc_continuous_handle_t h;
              adc_continuous_handle_cfg_t ch = {.max_store_buf_size = 1024, .conv_frame_size = 256};
              ESP_ERROR_CHECK(adc_continuous_new_handle(&ch, &h));
              printf("sem padrao %s\n", esp_err_to_name(adc_continuous_start(h)));
              adc_digi_pattern_config_t pad = {.atten = ADC_ATTEN_DB_12, .channel = ADC_CHANNEL_6, .unit = ADC_UNIT_1, .bit_width = 12};
              adc_continuous_config_t cfg = {.pattern_num = 1, .adc_pattern = &pad, .sample_freq_hz = 20000,
                .conv_mode = ADC_CONV_SINGLE_UNIT_1, .format = ADC_DIGI_OUTPUT_FORMAT_TYPE1};
              ESP_ERROR_CHECK(adc_continuous_config(h, &cfg));
              adc_continuous_evt_cbs_t cbs = {.on_conv_done = aoQuadro};
              ESP_ERROR_CHECK(adc_continuous_register_event_callbacks(h, &cbs, NULL));
              ESP_ERROR_CHECK(adc_continuous_start(h));
              uint8_t b[256];
              uint32_t n = 0;
              esp_err_t e = adc_continuous_read(h, b, sizeof(b), &n, 1000);
              adc_digi_output_data_t *d = (adc_digi_output_data_t *)b;
              printf("adc %s %lu canal %d valor %d\n", esp_err_to_name(e), (unsigned long)n, d->type1.channel, d->type1.data);
              vTaskDelay(pdMS_TO_TICKS(100));
              ESP_ERROR_CHECK(adc_continuous_stop(h));
              printf("quadros %s\n", quadros >= 2 ? "varios" : "poucos");
              ESP_ERROR_CHECK(adc_continuous_deinit(h));

              dac_output_enable(DAC_CHAN_0);
              dac_output_voltage(DAC_CHAN_0, 128);

              esp_netif_init();
              esp_netif_t *sta = esp_netif_create_default_wifi_sta();
              esp_netif_create_default_wifi_ap();
              char nome[8];
              esp_netif_get_netif_impl_name(sta, nome);
              int conta = 0;
              for (esp_netif_t *i = esp_netif_next_unsafe(NULL); i; i = esp_netif_next_unsafe(i)) conta++;
              printf("netif %d %s %d\n", conta, nome, esp_netif_get_route_prio(sta));
              wifi_init_config_t wcfg = WIFI_INIT_CONFIG_DEFAULT();
              esp_wifi_init(&wcfg);
              esp_wifi_set_country_code("BR", true);
              char pais[3];
              esp_wifi_get_country_code(pais);
              wifi_band_t banda;
              esp_wifi_get_band(&banda);
              printf("wifi %s %d\n", pais, banda == WIFI_BAND_2G);
              printf("fim\n");
            }
            """;
        (serial, erro, nativo, _) = RodarNativo(resto, 3000, antes: (_, m) => m.Analogico[34] = 1860);
        Conferir("ESP-IDF: esp_flash e spi_flash_mmap -- 4 MB, recusa o que nao esta alinhado ao setor, e o que se grava volta pela leitura e pelo mapa",
            nativo && erro is null && serial?.Contains("flash 4 MB id ok") == true && serial.Contains("sem alinhar ESP_ERR_INVALID_ARG") && serial.Contains("flash leu circuito apagado FF") && serial.Contains("mmap circuito"), $"nativo={nativo} erro={erro} serial=[{serial}]");
        Conferir("ESP-IDF: eFuse de placa virgem e ADC continuo -- sem padrao recusa, le quadros de verdade no canal do pino e chama o on_conv_done",
            serial?.Contains("efuse 0 1") == true && serial.Contains("sem padrao ESP_ERR_INVALID_STATE") && serial.Contains("adc ESP_OK 256 canal 6 valor 1860") && serial.Contains("quadros varios"), $"serial=[{serial}]");
        Conferir("ESP-IDF: esp_netif e esp_wifi -- as duas interfaces na lista, o nome e a prioridade do IDF, e o pais e a banda",
            serial?.Contains("netif 2 st1 100") == true && serial.Contains("wifi BR 1") && serial.Contains("fim"), $"serial=[{serial}]");

        // ---- o REPL do esp_console, com um comando do programa ----
        const string repl = """
            #include <stdio.h>
            #include "esp_console.h"
            static int soma(int argc, char **argv) {
              if (argc != 3) return 1;
              printf("resultado %d\n", atoi(argv[1]) + atoi(argv[2]));
              return 0;
            }
            void app_main(void) {
              esp_console_repl_t *r = NULL;
              esp_console_repl_config_t rc = ESP_CONSOLE_REPL_CONFIG_DEFAULT();
              rc.prompt = "placa>";
              esp_console_dev_uart_config_t hw = ESP_CONSOLE_DEV_UART_CONFIG_DEFAULT();
              ESP_ERROR_CHECK(esp_console_new_repl_uart(&hw, &rc, &r));
              esp_console_cmd_t c = {.command = "soma", .help = "soma dois", .func = &soma};
              ESP_ERROR_CHECK(esp_console_cmd_register(&c));
              ESP_ERROR_CHECK(esp_console_register_help_command());
              ESP_ERROR_CHECK(esp_console_start_repl(r));
            }
            """;
        int passoRepl = 0;
        (serial, erro, nativo, _) = RodarNativo(repl, 3000, durante: (Maquina m) =>
        {
            if (passoRepl == 0 && SerialTem(m, "Type 'help'")) { passoRepl = 1; m.EntradaSerial.Adicionar("soma 2 3\n"); }
            else if (passoRepl == 1 && SerialTem(m, "resultado")) { passoRepl = 2; m.EntradaSerial.Adicionar("nada\n"); }
            else if (passoRepl == 2 && SerialTem(m, "Unrecognized")) { passoRepl = 3; m.EntradaSerial.Adicionar("soma 1\n"); }
        });
        Conferir("ESP-IDF: esp_console -- o REPL le o monitor serial, roda o comando, e responde ao comando desconhecido e ao que devolve erro",
            nativo && erro is null && serial?.Contains("resultado 5") == true && serial.Contains("Unrecognized command") && serial.Contains("Command returned non-zero error code: 0x1"), $"nativo={nativo} erro={erro} serial=[{serial}]");

        // ---- o ESP_ERROR_CHECK que falha e o assert: o panico da placa, e nao o fim do simulador ----
        const string aborta = """
            #include <stdio.h>
            #include "esp_err.h"
            #include "esp_system.h"
            void app_main(void) {
              printf("razao %d\n", esp_reset_reason());
              if (esp_reset_reason() == ESP_RST_PANIC) { printf("voltou\n"); return; }
              ESP_ERROR_CHECK(ESP_ERR_NOT_FOUND);
            }
            """;
        var (serAborta, erroAborta, _, _) = RodarNativo(aborta, 1500);
        Conferir("ESP-IDF: ESP_ERROR_CHECK que falha -- a mensagem do IDF e o abort pedem o reinicio da placa, sem derrubar o simulador",
            serAborta?.Contains("ESP_ERROR_CHECK failed: esp_err_t 0x105 (ESP_ERR_NOT_FOUND)") == true && serAborta.Contains("func: app_main") && serAborta.Contains("abort() was called") && serAborta.Contains("Rebooting...") && erroAborta is null, $"erro={erroAborta} serial=[{serAborta}]");
        const string afirma = """
            #include <assert.h>
            #include <stdio.h>
            void app_main(void) {
              int x = 1;
              assert(x == 1);
              printf("passou\n");
              assert(x == 2);
              printf("nao chega\n");
            }
            """;
        (serAborta, erroAborta, _, _) = RodarNativo(afirma, 1500);
        Conferir("ESP-IDF: assert -- o que vale passa e o que falha mostra a expressao e reinicia a placa",
            serAborta?.Contains("passou") == true && serAborta.Contains("assert failed:") && serAborta.Contains("(x == 2)") && !serAborta.Contains("nao chega") && erroAborta is null, $"erro={erroAborta} serial=[{serAborta}]");

        // ---- o esp_lcd de verdade: o SSD1306 pelo I2C e o ST7789 pelo SPI ----
        const string lcd = """
            #include <stdio.h>
            #include <string.h>
            #include "driver/i2c_master.h"
            #include "driver/spi_master.h"
            #include "esp_lcd_panel_io.h"
            #include "esp_lcd_panel_ops.h"
            #include "esp_lcd_panel_vendor.h"
            void app_main(void) {
              i2c_master_bus_config_t bc = {.i2c_port = I2C_NUM_0, .sda_io_num = 21, .scl_io_num = 22, .clk_source = I2C_CLK_SRC_DEFAULT, .glitch_ignore_cnt = 7};
              i2c_master_bus_handle_t bus;
              ESP_ERROR_CHECK(i2c_new_master_bus(&bc, &bus));
              esp_lcd_panel_io_handle_t io;
              esp_lcd_panel_io_i2c_config_t ic = {.dev_addr = 0x3C, .scl_speed_hz = 400000, .control_phase_bytes = 1, .dc_bit_offset = 6, .lcd_cmd_bits = 8, .lcd_param_bits = 8};
              ESP_ERROR_CHECK(esp_lcd_new_panel_io_i2c(bus, &ic, &io));
              esp_lcd_panel_handle_t tela;
              esp_lcd_panel_dev_config_t pc = {.bits_per_pixel = 1, .reset_gpio_num = -1};
              ESP_ERROR_CHECK(esp_lcd_new_panel_ssd1306(io, &pc, &tela));
              ESP_ERROR_CHECK(esp_lcd_panel_reset(tela));
              ESP_ERROR_CHECK(esp_lcd_panel_init(tela));
              ESP_ERROR_CHECK(esp_lcd_panel_disp_on_off(tela, true));
              static uint8_t imagem[128 * 8];
              memset(imagem, 0xFF, sizeof(imagem));
              ESP_ERROR_CHECK(esp_lcd_panel_draw_bitmap(tela, 0, 0, 128, 64, imagem));
              printf("ssd1306 ok\n");

              spi_bus_config_t sb = {.mosi_io_num = 23, .miso_io_num = -1, .sclk_io_num = 18, .quadwp_io_num = -1, .quadhd_io_num = -1, .max_transfer_sz = 4096};
              ESP_ERROR_CHECK(spi_bus_initialize(SPI2_HOST, &sb, SPI_DMA_CH_AUTO));
              esp_lcd_panel_io_handle_t iospi;
              esp_lcd_panel_io_spi_config_t sc = {.dc_gpio_num = 2, .cs_gpio_num = 5, .pclk_hz = 10000000, .lcd_cmd_bits = 8, .lcd_param_bits = 8, .spi_mode = 0, .trans_queue_depth = 4};
              ESP_ERROR_CHECK(esp_lcd_new_panel_io_spi(SPI2_HOST, &sc, &iospi));
              esp_lcd_panel_handle_t cor;
              esp_lcd_panel_dev_config_t pc2 = {.reset_gpio_num = 4, .rgb_ele_order = LCD_RGB_ELEMENT_ORDER_RGB, .bits_per_pixel = 16};
              ESP_ERROR_CHECK(esp_lcd_new_panel_st7789(iospi, &pc2, &cor));
              ESP_ERROR_CHECK(esp_lcd_panel_reset(cor));
              ESP_ERROR_CHECK(esp_lcd_panel_init(cor));
              uint16_t linha[16] = {0};
              ESP_ERROR_CHECK(esp_lcd_panel_draw_bitmap(cor, 0, 0, 16, 1, linha));
              printf("st7789 ok\n");
            }
            """;
        (serial, erro, nativo, _) = RodarNativo(lcd, 3000, antes: (_, m) => m.I2c.Ligar(0x3C, "OLED"));
        Conferir("ESP-IDF: esp_lcd de verdade -- SSD1306 pelo I2C e ST7789 pelo SPI, do reset ao draw_bitmap",
            nativo && erro is null && serial?.Contains("ssd1306 ok") == true && serial.Contains("st7789 ok"), $"nativo={nativo} erro={erro} serial=[{serial}]");

        // ---- o SPP do Bluetooth classico, entre duas placas ----
        const string servidorSpp = """
            #include <stdio.h>
            #include <string.h>
            #include "esp_bt.h"
            #include "esp_bt_main.h"
            #include "esp_gap_bt_api.h"
            #include "esp_spp_api.h"
            static void aoSpp(esp_spp_cb_event_t ev, esp_spp_cb_param_t *p) {
              if (ev == ESP_SPP_INIT_EVT) { printf("init\n"); esp_bt_gap_set_device_name("SPPCS"); esp_spp_start_srv(ESP_SPP_SEC_NONE, ESP_SPP_ROLE_SLAVE, 0, "srv"); }
              else if (ev == ESP_SPP_START_EVT) printf("no ar\n");
              else if (ev == ESP_SPP_SRV_OPEN_EVT) printf("abriu\n");
              else if (ev == ESP_SPP_DATA_IND_EVT) {
                printf("chegou %.*s\n", p->data_ind.len, p->data_ind.data);
                esp_spp_write(p->data_ind.handle, 4, (uint8_t *)"pong");
              }
            }
            void app_main(void) {
              esp_bt_controller_config_t c = BT_CONTROLLER_INIT_CONFIG_DEFAULT();
              ESP_ERROR_CHECK(esp_bt_controller_init(&c));
              ESP_ERROR_CHECK(esp_bt_controller_enable(ESP_BT_MODE_CLASSIC_BT));
              ESP_ERROR_CHECK(esp_bluedroid_init());
              ESP_ERROR_CHECK(esp_bluedroid_enable());
              ESP_ERROR_CHECK(esp_spp_register_callback(aoSpp));
              esp_spp_cfg_t sc = {.mode = ESP_SPP_MODE_CB, .tx_buffer_size = 0};
              ESP_ERROR_CHECK(esp_spp_enhanced_init(&sc));
            }
            """;
        const string clienteSpp = """
            #include "BluetoothSerial.h"
            BluetoothSerial bt;
            void setup() {
              Serial.begin(115200);
              bt.begin("CLIENTE", true);
              delay(500);
              bool ok = bt.connect("SPPCS");
              Serial.println(ok ? "conectou" : "nao conectou");
              bt.print("ping");
              unsigned long t = millis();
              while (!bt.available() && millis() - t < 2000) delay(5);
              String r;
              while (bt.available()) r += (char)bt.read();
              Serial.println("voltou " + r);
            }
            void loop() { delay(10); }
            """;
        (serA, serB, erroCan) = RodarDuasNativas(servidorSpp, clienteSpp, "esp32-devkit-v1", 5000, (a, b) => b.Contains("voltou"));
        Conferir("ESP-IDF: SPP -- o servidor do IDF abre no ar, o BluetoothSerial da outra placa conecta pelo nome e os dados vao e voltam nos eventos",
            erroCan is null && serA?.Contains("no ar") == true && serA.Contains("abriu") && serA.Contains("chegou ping") && serB?.Contains("conectou") == true && serB.Contains("voltou pong"), $"erro={erroCan} A=[{serA}] B=[{serB}]");
    }
}
