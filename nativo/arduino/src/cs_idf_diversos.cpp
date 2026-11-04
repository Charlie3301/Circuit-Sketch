/* cs_idf_diversos.cpp -- o resto do ESP-IDF que os programas usam: o PWM com fade
 * por passo, a calibracao do ADC, o DAC, o MAC, os CRC, o watchdog de tarefas,
 * o SNTP, os bloqueios de energia, o GPIO da RTC e o sensor de temperatura. */
#include "Arduino.h"
#if defined(ESP32)
#include "cs_objeto.h"
#include "esp_err.h"
#include "esp_log.h"
#include "esp_mac.h"
#include "esp_system.h"
#include "esp_heap_caps.h"
#include "multi_heap.h"
#include "esp_timer.h"
#include "esp_task_wdt.h"
#include "esp_pm.h"
#include "esp_rom_crc.h"
#include "esp_rom_sys.h"
#include "esp_sntp.h"
#include "esp_netif_sntp.h"
#include "esp_adc/adc_cali.h"
#include "esp_adc/adc_cali_scheme.h"
#include "esp_adc/adc_oneshot.h"
#include "driver/ledc.h"
#include "driver/gpio.h"
#include "freertos/task.h"
#include <map>
#include <string>
#include <vector>
#include <sys/time.h>
#include <time.h>

static const char *const TAG = "simulador";

/* =====================================================================
 * o texto dos erros e o MAC */
extern "C" int strerror_r(int erro, char *saida, size_t n) { return strerror_s(saida, n, erro); }
extern "C" esp_err_t esp_read_mac(uint8_t *mac, esp_mac_type_t type)
{
    if (!mac) return ESP_ERR_INVALID_ARG;
    esp_err_t e = esp_efuse_mac_get_default(mac);
    if (e != ESP_OK) return e;
    /* Um base so: o WiFi e o base, e os outros somam, como na tabela do chip. */
    int soma = 0;
    switch (type) {
    case ESP_MAC_WIFI_STA: soma = 0; break;
    case ESP_MAC_WIFI_SOFTAP: soma = 1; break;
    case ESP_MAC_BT: soma = 2; break;
    case ESP_MAC_ETH: soma = 3; break;
    case ESP_MAC_IEEE802154: soma = 0; break;
    default: soma = 0; break;
    }
    mac[5] = (uint8_t)(mac[5] + soma);
    return ESP_OK;
}
static uint8_t baseDoMac[6];
static bool temBaseDoMac = false;
extern "C" esp_err_t esp_base_mac_addr_set(const uint8_t *mac)
{
    if (!mac) return ESP_ERR_INVALID_ARG;
    if (mac[0] & 1) return ESP_ERR_INVALID_ARG;
    memcpy(baseDoMac, mac, 6);
    temBaseDoMac = true;
    return ESP_OK;
}
extern "C" esp_err_t esp_base_mac_addr_get(uint8_t *mac)
{
    if (!mac) return ESP_ERR_INVALID_ARG;
    if (!temBaseDoMac) return esp_efuse_mac_get_default(mac);
    memcpy(mac, baseDoMac, 6);
    return ESP_OK;
}
extern "C" esp_err_t esp_derive_local_mac(uint8_t *local_mac, const uint8_t *universal_mac)
{
    if (!local_mac || !universal_mac) return ESP_ERR_INVALID_ARG;
    memcpy(local_mac, universal_mac, 6);
    local_mac[5] ^= 0x01;
    local_mac[0] = (uint8_t)((local_mac[0] | 0x02));
    return ESP_OK;
}
extern "C" size_t esp_mac_addr_len_get(esp_mac_type_t type) { return type == ESP_MAC_IEEE802154 ? 8 : 6; }

/* =====================================================================
 * o sistema */
extern "C" uint32_t esp_get_free_internal_heap_size(void) { return ESP.getFreeHeap(); }

static std::vector<shutdown_handler_t> &aoDesligar()
{
    static auto *v = new std::vector<shutdown_handler_t>();
    return *v;
}
extern "C" esp_err_t esp_register_shutdown_handler(shutdown_handler_t handler)
{
    if (!handler) return ESP_ERR_INVALID_ARG;
    aoDesligar().push_back(handler);
    return ESP_OK;
}
extern "C" esp_err_t esp_unregister_shutdown_handler(shutdown_handler_t handler)
{
    auto &v = aoDesligar();
    for (size_t i = 0; i < v.size(); i++)
        if (v[i] == handler) {
            v.erase(v.begin() + (long)i);
            return ESP_OK;
        }
    return ESP_ERR_INVALID_STATE;
}
/* O esp_restart chama os registrados antes de reiniciar (cs_idf.cpp). */
extern "C" void cs_chamar_ao_desligar(void)
{
    auto &v = aoDesligar();
    for (size_t i = v.size(); i > 0; i--) v[i - 1]();
}

extern "C" void *heap_caps_malloc_prefer(size_t size, size_t num, ...)
{
    (void)num;
    return malloc(size);
}
extern "C" void *heap_caps_realloc_prefer(void *ptr, size_t size, size_t num, ...)
{
    (void)num;
    return realloc(ptr, size);
}
extern "C" void *heap_caps_calloc_prefer(size_t n, size_t size, size_t num, ...)
{
    (void)num;
    return calloc(n, size);
}
extern "C" void *heap_caps_aligned_calloc(size_t alignment, size_t n, size_t size, uint32_t caps)
{
    (void)alignment;
    (void)caps;
    return calloc(n, size);
}
extern "C" bool heap_caps_check_integrity_all(bool print_errors)
{
    (void)print_errors;
    return true;
}
extern "C" bool heap_caps_check_integrity(uint32_t caps, bool print_errors)
{
    (void)caps;
    (void)print_errors;
    return true;
}
extern "C" bool heap_caps_check_integrity_addr(intptr_t addr, bool print_errors)
{
    (void)addr;
    (void)print_errors;
    return true;
}
extern "C" void heap_caps_get_info(multi_heap_info_t *info, uint32_t caps)
{
    memset(info, 0, sizeof(*info));
    info->total_free_bytes = heap_caps_get_free_size(caps);
    info->largest_free_block = heap_caps_get_largest_free_block(caps);
    info->minimum_free_bytes = heap_caps_get_minimum_free_size(caps);
    info->total_allocated_bytes = heap_caps_get_total_size(caps) - info->total_free_bytes;
    info->total_blocks = 1;
    info->free_blocks = 1;
}
extern "C" void heap_caps_print_heap_info(uint32_t caps)
{
    printf("Heap summary for capabilities 0x%08lX:\n  free %u bytes\n", (unsigned long)caps, (unsigned)heap_caps_get_free_size(caps));
}

extern "C" esp_err_t esp_timer_dump(FILE *stream)
{
    (void)stream;
    return ESP_OK;
}

/* =====================================================================
 * os CRC da ROM: os polinomios de cada familia, como em esp_rom_crc.h */
#define CS_CRC_LE(nome, tipo, poli)                                                 \
    extern "C" tipo nome(tipo crc, const uint8_t *buf, uint32_t len)                \
    {                                                                               \
        tipo c = (tipo)~crc;                                                        \
        for (uint32_t i = 0; i < len; i++) {                                        \
            c ^= buf[i];                                                            \
            for (int b = 0; b < 8; b++) c = (tipo)((c & 1) ? (c >> 1) ^ (poli) : c >> 1); \
        }                                                                           \
        return (tipo)~c;                                                            \
    }
#define CS_CRC_BE(nome, tipo, bits, poli)                                               \
    extern "C" tipo nome(tipo crc, const uint8_t *buf, uint32_t len)                    \
    {                                                                                   \
        tipo c = (tipo)~crc;                                                            \
        for (uint32_t i = 0; i < len; i++) {                                            \
            c ^= (tipo)((tipo)buf[i] << ((bits) - 8));                                  \
            for (int b = 0; b < 8; b++) c = (tipo)((c >> ((bits) - 1)) ? (tipo)(c << 1) ^ (poli) : (tipo)(c << 1)); \
        }                                                                               \
        return (tipo)~c;                                                                \
    }
CS_CRC_LE(esp_rom_crc32_le, uint32_t, 0xEDB88320u)
CS_CRC_BE(esp_rom_crc32_be, uint32_t, 32, 0x04C11DB7u)
CS_CRC_LE(esp_rom_crc16_le, uint16_t, 0x8408u)
CS_CRC_BE(esp_rom_crc16_be, uint16_t, 16, 0x1021u)
CS_CRC_LE(esp_rom_crc8_le, uint8_t, 0x8Cu)
CS_CRC_BE(esp_rom_crc8_be, uint8_t, 8, 0x07u)

/* ---- a ROM ---- */
extern "C" int esp_rom_vprintf(const char *fmt, va_list ap) { return vprintf(fmt, ap); }
extern "C" int esp_rom_printf(const char *fmt, ...)
{
    va_list ap;
    va_start(ap, fmt);
    int n = vprintf(fmt, ap);
    va_end(ap);
    return n;
}
extern "C" void esp_rom_software_reset_system(void) { esp_restart(); }
extern "C" soc_reset_reason_t esp_rom_get_reset_reason(int cpu_no)
{
    (void)cpu_no;
    return RESET_REASON_CHIP_POWER_ON;
}
extern "C" uint32_t esp_rom_get_cpu_ticks_per_us(void) { return getCpuFrequencyMhz(); }

/* =====================================================================
 * o PWM: o que faltava em cima de ledc_set_duty/ledc_set_fade_with_time */
extern "C" uint32_t ledc_find_suitable_duty_resolution(uint32_t src_clk_freq, uint32_t timer_freq)
{
    if (!timer_freq) return 0;
    uint32_t div = src_clk_freq / timer_freq;
    uint32_t bits = 0;
    while (div > 1) {
        div >>= 1;
        bits++;
    }
    return bits;
}
extern "C" esp_err_t ledc_set_fade_time_and_start(ledc_mode_t m, ledc_channel_t c, uint32_t alvo, uint32_t ms, ledc_fade_mode_t modo)
{
    esp_err_t e = ledc_set_fade_with_time(m, c, alvo, (int)ms);
    return e == ESP_OK ? ledc_fade_start(m, c, modo) : e;
}
extern "C" esp_err_t ledc_set_fade_with_step(ledc_mode_t m, ledc_channel_t c, uint32_t alvo, uint32_t escala, uint32_t ciclos)
{
    /* O fade por passo: cada "ciclos" do PWM soma "escala" no duty. */
    uint32_t agora = ledc_get_duty(m, c);
    uint32_t distancia = alvo > agora ? alvo - agora : agora - alvo;
    uint32_t frequencia = ledc_get_freq(m, LEDC_TIMER_0);
    if (!frequencia) frequencia = 1000;
    uint32_t passos = escala ? distancia / escala : 0;
    uint32_t ms = (uint32_t)(((uint64_t)passos * (ciclos ? ciclos : 1) * 1000) / frequencia);
    return ledc_set_fade_with_time(m, c, alvo, (int)(ms ? ms : 1));
}
extern "C" esp_err_t ledc_set_fade_step_and_start(ledc_mode_t m, ledc_channel_t c, uint32_t alvo, uint32_t escala, uint32_t ciclos, ledc_fade_mode_t modo)
{
    esp_err_t e = ledc_set_fade_with_step(m, c, alvo, escala, ciclos);
    return e == ESP_OK ? ledc_fade_start(m, c, modo) : e;
}
extern "C" esp_err_t ledc_set_duty_with_hpoint(ledc_mode_t m, ledc_channel_t c, uint32_t duty, uint32_t hpoint)
{
    (void)hpoint;
    return ledc_set_duty(m, c, duty);
}
extern "C" int ledc_get_hpoint(ledc_mode_t m, ledc_channel_t c)
{
    (void)m;
    (void)c;
    return 0;
}
extern "C" esp_err_t ledc_fade_stop(ledc_mode_t m, ledc_channel_t c) { return ledc_set_duty_and_update(m, c, ledc_get_duty(m, c), 0); }
extern "C" void ledc_fade_func_uninstall(void) {}

/* =====================================================================
 * a calibracao do ADC: reta do datasheet, pela atenuacao do canal */
struct adc_cali_scheme_t {
    adc_atten_t atenuacao;
    adc_bitwidth_t largura;
};
static int fundoDeEscala(adc_atten_t a)
{
    switch (a) {
    case ADC_ATTEN_DB_0: return 950;
    case ADC_ATTEN_DB_2_5: return 1250;
    case ADC_ATTEN_DB_6: return 1750;
    default: return 3100;
    }
}
extern "C" esp_err_t adc_cali_check_scheme(adc_cali_scheme_ver_t *scheme_mask)
{
    if (!scheme_mask) return ESP_ERR_INVALID_ARG;
#if ADC_CALI_SCHEME_CURVE_FITTING_SUPPORTED
    *scheme_mask = ADC_CALI_SCHEME_VER_CURVE_FITTING;
#else
    *scheme_mask = ADC_CALI_SCHEME_VER_LINE_FITTING;
#endif
    return ESP_OK;
}
#if ADC_CALI_SCHEME_CURVE_FITTING_SUPPORTED
extern "C" esp_err_t adc_cali_create_scheme_curve_fitting(const adc_cali_curve_fitting_config_t *config, adc_cali_handle_t *saida)
{
    if (!config || !saida) return ESP_ERR_INVALID_ARG;
    *saida = (adc_cali_handle_t) new adc_cali_scheme_t{config->atten, config->bitwidth};
    return ESP_OK;
}
extern "C" esp_err_t adc_cali_delete_scheme_curve_fitting(adc_cali_handle_t handle)
{
    delete (adc_cali_scheme_t *)handle;
    return ESP_OK;
}
#endif
#if ADC_CALI_SCHEME_LINE_FITTING_SUPPORTED
extern "C" esp_err_t adc_cali_create_scheme_line_fitting(const adc_cali_line_fitting_config_t *config, adc_cali_handle_t *saida)
{
    if (!config || !saida) return ESP_ERR_INVALID_ARG;
    *saida = (adc_cali_handle_t) new adc_cali_scheme_t{config->atten, config->bitwidth};
    return ESP_OK;
}
extern "C" esp_err_t adc_cali_delete_scheme_line_fitting(adc_cali_handle_t handle)
{
    delete (adc_cali_scheme_t *)handle;
    return ESP_OK;
}
extern "C" esp_err_t adc_cali_scheme_line_fitting_check_efuse(adc_cali_line_fitting_efuse_val_t *cali_val)
{
    if (!cali_val) return ESP_ERR_INVALID_ARG;
    *cali_val = ADC_CALI_LINE_FITTING_EFUSE_VAL_DEFAULT_VREF;
    return ESP_OK;
}
#endif
extern "C" esp_err_t adc_cali_raw_to_voltage(adc_cali_handle_t handle, int raw, int *voltage)
{
    if (!handle || !voltage) return ESP_ERR_INVALID_ARG;
    auto *c = (adc_cali_scheme_t *)handle;
    int bits = c->largura == ADC_BITWIDTH_DEFAULT ? 12 : (int)c->largura;
    *voltage = (int)((int64_t)raw * fundoDeEscala(c->atenuacao) / ((1 << bits) - 1));
    return ESP_OK;
}
extern "C" esp_err_t adc_oneshot_get_calibrated_result(adc_oneshot_unit_handle_t handle, adc_cali_handle_t cali, adc_channel_t chan, int *cali_result)
{
    int raw = 0;
    esp_err_t e = adc_oneshot_read(handle, chan, &raw);
    return e == ESP_OK ? adc_cali_raw_to_voltage(cali, raw, cali_result) : e;
}

/* =====================================================================
 * o GPIO: o que so faz sentido na placa de verdade fica como ajuste (gerado);
 * estes respondem o que o simulador sabe */
extern "C" esp_err_t gpio_input_enable(gpio_num_t pin)
{
    pinMode(pin, INPUT);
    return ESP_OK;
}
extern "C" esp_err_t gpio_dump_io_configuration(FILE *out_stream, uint64_t io_bit_mask)
{
    (void)out_stream;
    (void)io_bit_mask;
    return ESP_OK;
}

/* =====================================================================
 * o watchdog de tarefas: quem se inscreveu e nao chamou o reset no prazo aparece
 * na serial, como na placa, e com trigger_panic a placa reinicia */
namespace {
struct Inscrito {
    std::string nome;
    uint32_t ultimo;
    TaskHandle_t tarefa;
};
struct esp_task_wdt_user_handle_s_cs {};
bool wdtLigado = false, wdtPanico = false;
uint32_t wdtPrazo = 5000;
std::vector<Inscrito *> inscritos;
esp_timer_handle_t relogioDoWdt = nullptr;

void conferirWdt(void *)
{
    uint32_t agora = millis();
    for (Inscrito *i : inscritos) {
        if (agora - i->ultimo < wdtPrazo) continue;
        ESP_LOGE("task_wdt", "Task watchdog got triggered. The following tasks/users did not reset the watchdog in time:");
        ESP_LOGE("task_wdt", " - %s", i->nome.c_str());
        i->ultimo = agora;
        if (wdtPanico) esp_restart();
        return;
    }
}
Inscrito *inscritoDaTarefa(TaskHandle_t t)
{
    if (!t) t = xTaskGetCurrentTaskHandle();
    for (Inscrito *i : inscritos)
        if (i->tarefa == t) return i;
    return nullptr;
}
} /* namespace */

extern "C" esp_err_t esp_task_wdt_init(const esp_task_wdt_config_t *config)
{
    if (!config || !config->timeout_ms) return ESP_ERR_INVALID_ARG;
    if (wdtLigado) return ESP_ERR_INVALID_STATE;
    wdtLigado = true;
    wdtPrazo = config->timeout_ms;
    wdtPanico = config->trigger_panic;
    esp_timer_create_args_t a = {};
    a.callback = conferirWdt;
    a.name = "task_wdt";
    esp_timer_create(&a, &relogioDoWdt);
    esp_timer_start_periodic(relogioDoWdt, 100000);
    return ESP_OK;
}
extern "C" esp_err_t esp_task_wdt_reconfigure(const esp_task_wdt_config_t *config)
{
    if (!config || !config->timeout_ms) return ESP_ERR_INVALID_ARG;
    if (!wdtLigado) return ESP_ERR_INVALID_STATE;
    wdtPrazo = config->timeout_ms;
    wdtPanico = config->trigger_panic;
    return ESP_OK;
}
extern "C" esp_err_t esp_task_wdt_deinit(void)
{
    if (!wdtLigado) return ESP_ERR_INVALID_STATE;
    if (!inscritos.empty()) return ESP_ERR_INVALID_STATE;
    esp_timer_stop(relogioDoWdt);
    esp_timer_delete(relogioDoWdt);
    wdtLigado = false;
    return ESP_OK;
}
extern "C" esp_err_t esp_task_wdt_add(TaskHandle_t task_handle)
{
    if (!wdtLigado) return ESP_ERR_INVALID_STATE;
    TaskHandle_t t = task_handle ? task_handle : xTaskGetCurrentTaskHandle();
    if (inscritoDaTarefa(t)) return ESP_ERR_INVALID_ARG;
    inscritos.push_back(new Inscrito{pcTaskGetName(t), (uint32_t)millis(), t});
    return ESP_OK;
}
extern "C" esp_err_t esp_task_wdt_add_user(const char *user_name, esp_task_wdt_user_handle_t *user_handle_ret)
{
    if (!user_name || !user_handle_ret) return ESP_ERR_INVALID_ARG;
    if (!wdtLigado) return ESP_ERR_INVALID_STATE;
    auto *i = new Inscrito{user_name, (uint32_t)millis(), nullptr};
    inscritos.push_back(i);
    *user_handle_ret = (esp_task_wdt_user_handle_t)i;
    return ESP_OK;
}
extern "C" esp_err_t esp_task_wdt_reset(void)
{
    if (!wdtLigado) return ESP_ERR_INVALID_STATE;
    Inscrito *i = inscritoDaTarefa(nullptr);
    if (!i) return ESP_ERR_NOT_FOUND;
    i->ultimo = millis();
    return ESP_OK;
}
extern "C" esp_err_t esp_task_wdt_reset_user(esp_task_wdt_user_handle_t user_handle)
{
    if (!wdtLigado) return ESP_ERR_INVALID_STATE;
    if (!user_handle) return ESP_ERR_INVALID_ARG;
    ((Inscrito *)user_handle)->ultimo = millis();
    return ESP_OK;
}
static esp_err_t tirarInscrito(Inscrito *i)
{
    for (size_t k = 0; k < inscritos.size(); k++)
        if (inscritos[k] == i) {
            inscritos.erase(inscritos.begin() + (long)k);
            delete i;
            return ESP_OK;
        }
    return ESP_ERR_NOT_FOUND;
}
extern "C" esp_err_t esp_task_wdt_delete(TaskHandle_t task_handle)
{
    if (!wdtLigado) return ESP_ERR_INVALID_STATE;
    Inscrito *i = inscritoDaTarefa(task_handle);
    return i ? tirarInscrito(i) : ESP_ERR_NOT_FOUND;
}
extern "C" esp_err_t esp_task_wdt_delete_user(esp_task_wdt_user_handle_t user_handle)
{
    if (!wdtLigado) return ESP_ERR_INVALID_STATE;
    return user_handle ? tirarInscrito((Inscrito *)user_handle) : ESP_ERR_INVALID_ARG;
}
extern "C" esp_err_t esp_task_wdt_status(TaskHandle_t task_handle)
{
    if (!wdtLigado) return ESP_ERR_INVALID_STATE;
    return inscritoDaTarefa(task_handle) ? ESP_OK : ESP_ERR_NOT_FOUND;
}
extern "C" esp_err_t esp_task_wdt_print_triggered_tasks(task_wdt_msg_handler msg_handler, void *opaque, int *cpus_fail)
{
    (void)msg_handler;
    (void)opaque;
    if (cpus_fail) *cpus_fail = 0;
    return ESP_FAIL;
}

/* =====================================================================
 * os bloqueios de energia: o simulador nao muda a frequencia, mas a contagem vale */
struct esp_pm_lock {
    int contagem;
};
extern "C" esp_err_t esp_pm_lock_create(esp_pm_lock_type_t lock_type, int arg, const char *name, esp_pm_lock_handle_t *out_handle)
{
    (void)lock_type;
    (void)arg;
    (void)name;
    if (!out_handle) return ESP_ERR_INVALID_ARG;
    *out_handle = new esp_pm_lock{0};
    return ESP_OK;
}
extern "C" esp_err_t esp_pm_lock_acquire(esp_pm_lock_handle_t handle)
{
    if (!handle) return ESP_ERR_INVALID_ARG;
    handle->contagem++;
    return ESP_OK;
}
extern "C" esp_err_t esp_pm_lock_release(esp_pm_lock_handle_t handle)
{
    if (!handle) return ESP_ERR_INVALID_ARG;
    if (!handle->contagem) return ESP_ERR_INVALID_STATE;
    handle->contagem--;
    return ESP_OK;
}
extern "C" esp_err_t esp_pm_lock_delete(esp_pm_lock_handle_t handle)
{
    if (!handle) return ESP_ERR_INVALID_ARG;
    if (handle->contagem) return ESP_ERR_INVALID_STATE;
    delete handle;
    return ESP_OK;
}
extern "C" esp_err_t esp_pm_configure(const void *config)
{
    (void)config;
    return ESP_OK;
}
extern "C" esp_err_t esp_pm_get_configuration(void *config)
{
    (void)config;
    return ESP_ERR_NOT_SUPPORTED;
}
extern "C" esp_err_t esp_pm_dump_locks(FILE *stream)
{
    (void)stream;
    return ESP_OK;
}

/* =====================================================================
 * o SNTP: o simulador pega a hora do computador pelo configTime do core */
namespace {
const int MAX_SERVIDORES = SNTP_MAX_SERVERS;
std::string servidores[MAX_SERVIDORES];
bool sntpLigado = false;
uint8_t modoSntp = 0;
sntp_sync_mode_t modoDeAjuste = SNTP_SYNC_MODE_IMMED;
sntp_sync_status_t estadoDoAjuste = SNTP_SYNC_STATUS_RESET;
sntp_sync_time_cb_t avisoDoAjuste = nullptr;
uint32_t intervalo = 3600000;
TaskHandle_t tarefaDoSntp = nullptr;

bool horaValida()
{
    time_t agora = time(nullptr);
    return agora > 1600000000;
}
void esperarHora(void *)
{
    while (sntpLigado) {
        if (horaValida()) {
            struct timeval tv;
            gettimeofday(&tv, nullptr);
            sntp_sync_time(&tv);
            break;
        }
        vTaskDelay(pdMS_TO_TICKS(100));
    }
    tarefaDoSntp = nullptr;
    vTaskDelete(nullptr);
}
void ligarSntp()
{
    const char *a = servidores[0].empty() ? "pool.ntp.org" : servidores[0].c_str();
    configTime(0, 0, a, servidores[1].empty() ? nullptr : servidores[1].c_str(), servidores[2].empty() ? nullptr : servidores[2].c_str());
    sntpLigado = true;
    estadoDoAjuste = SNTP_SYNC_STATUS_RESET;
    if (!tarefaDoSntp) xTaskCreate(esperarHora, "sntp", 4096, nullptr, 5, &tarefaDoSntp);
}
} /* namespace */

extern "C" {
void sntp_sync_time(struct timeval *tv)
{
    (void)tv;
    estadoDoAjuste = SNTP_SYNC_STATUS_COMPLETED;
    if (avisoDoAjuste) avisoDoAjuste(tv);
}
void sntp_set_time_sync_notification_cb(sntp_sync_time_cb_t callback) { avisoDoAjuste = callback; }
void sntp_set_sync_mode(sntp_sync_mode_t sync_mode) { modoDeAjuste = sync_mode; }
sntp_sync_mode_t sntp_get_sync_mode(void) { return modoDeAjuste; }
sntp_sync_status_t sntp_get_sync_status(void)
{
    sntp_sync_status_t e = estadoDoAjuste;
    if (e == SNTP_SYNC_STATUS_COMPLETED) estadoDoAjuste = SNTP_SYNC_STATUS_RESET;
    return e;
}
void sntp_set_sync_status(sntp_sync_status_t sync_status) { estadoDoAjuste = sync_status; }
void sntp_set_sync_interval(uint32_t interval_ms) { intervalo = interval_ms; }
uint32_t sntp_get_sync_interval(void) { return intervalo; }
bool sntp_restart(void)
{
    if (!sntpLigado) return false;
    ligarSntp();
    return true;
}
void esp_sntp_setoperatingmode(esp_sntp_operatingmode_t operating_mode) { modoSntp = (uint8_t)operating_mode; }
esp_sntp_operatingmode_t esp_sntp_getoperatingmode(void) { return (esp_sntp_operatingmode_t)modoSntp; }
void esp_sntp_init(void) { ligarSntp(); }
void esp_sntp_stop(void) { sntpLigado = false; }
bool esp_sntp_enabled(void) { return sntpLigado; }
void esp_sntp_setservername(u8_t idx, const char *server)
{
    if (idx < MAX_SERVIDORES) servidores[idx] = server ? server : "";
}
const char *esp_sntp_getservername(u8_t idx) { return idx < MAX_SERVIDORES ? servidores[idx].c_str() : nullptr; }
void esp_sntp_setserver(u8_t idx, const ip_addr_t *addr)
{
    (void)idx;
    (void)addr;
}
const ip_addr_t *esp_sntp_getserver(u8_t idx)
{
    (void)idx;
    return nullptr;
}
void esp_sntp_servermode_dhcp(bool enable) { (void)enable; }
u8_t esp_sntp_getreachability(u8_t idx) { return idx == 0 && horaValida() ? 1 : 0; }

esp_err_t esp_netif_sntp_init(const esp_sntp_config_t *config)
{
    if (!config) return ESP_ERR_INVALID_ARG;
    for (int i = 0; i < config->num_of_servers && i < MAX_SERVIDORES; i++) servidores[i] = config->servers[i] ? config->servers[i] : "";
    if (config->sync_cb) avisoDoAjuste = config->sync_cb;
    if (config->smooth_sync) modoDeAjuste = SNTP_SYNC_MODE_SMOOTH;
    if (config->start) ligarSntp();
    return ESP_OK;
}
esp_err_t esp_netif_sntp_start(void)
{
    ligarSntp();
    return ESP_OK;
}
void esp_netif_sntp_deinit(void) { sntpLigado = false; }
esp_err_t esp_netif_sntp_sync_wait(TickType_t tout)
{
    uint32_t inicio = millis(), limite = pdTICKS_TO_MS(tout);
    while (!horaValida() || estadoDoAjuste != SNTP_SYNC_STATUS_COMPLETED) {
        if (tout != portMAX_DELAY && millis() - inicio >= limite) return ESP_ERR_TIMEOUT;
        vTaskDelay(pdMS_TO_TICKS(50));
    }
    return ESP_OK;
}
esp_err_t esp_netif_sntp_reachability(unsigned int index, unsigned int *reachability)
{
    if (!reachability || index >= (unsigned)MAX_SERVIDORES) return ESP_ERR_INVALID_ARG;
    *reachability = esp_sntp_getreachability((u8_t)index);
    return ESP_OK;
}
} /* extern "C" */

#endif
