/* cs_idf.c -- o ESP-IDF da placa: esp_timer, log, sistema, pinos, SPI,
 * cartao SD, I2S, UART, NVS.
 *
 * O que chega no circuito (pino, byte de SPI, amostra de audio) passa
 * pelo simulador; o resto e daqui. */
#include <stdint.h>
#include <stddef.h>
#include <stdarg.h>
#include <string.h>
#include <stdlib.h>
#include <stdio.h>
#include <ctype.h>
#include "cs_host.h"
#include "cs_interno.h"
#include "freertos/FreeRTOS.h"
#include "freertos/task.h"
#include "esp_err.h"
#include "esp_log.h"
#include "esp_timer.h"
#include "esp_system.h"
#include "esp_chip_info.h"
#include "esp_heap_caps.h"
#include "esp_psram.h"
#include "esp_task_wdt.h"
#include "esp_mac.h"
#include "esp_cpu.h"
#include "esp_idf_version.h"
#include "rom/ets_sys.h"
#include "esp_rom_sys.h"
#include "driver/gpio.h"
#include "driver/spi_master.h"
#include "driver/sdspi_host.h"
#include "driver/sdmmc_host.h"
#include "esp_vfs_fat.h"
#include "sdmmc_cmd.h"
#include "driver/i2s_std.h"
#include "driver/i2s.h"
#include "driver/uart.h"
#include "nvs_flash.h"

static const char *TAG_SPI = "spi_master";

/* ---------------------------------------------------------------------
 * erros */
const char *esp_err_to_name(esp_err_t codigo)
{
    switch (codigo) {
    case ESP_OK: return "ESP_OK";
    case ESP_FAIL: return "ESP_FAIL";
    case ESP_ERR_NO_MEM: return "ESP_ERR_NO_MEM";
    case ESP_ERR_INVALID_ARG: return "ESP_ERR_INVALID_ARG";
    case ESP_ERR_INVALID_STATE: return "ESP_ERR_INVALID_STATE";
    case ESP_ERR_INVALID_SIZE: return "ESP_ERR_INVALID_SIZE";
    case ESP_ERR_NOT_FOUND: return "ESP_ERR_NOT_FOUND";
    case ESP_ERR_NOT_SUPPORTED: return "ESP_ERR_NOT_SUPPORTED";
    case ESP_ERR_TIMEOUT: return "ESP_ERR_TIMEOUT";
    case ESP_ERR_INVALID_RESPONSE: return "ESP_ERR_INVALID_RESPONSE";
    case ESP_ERR_INVALID_CRC: return "ESP_ERR_INVALID_CRC";
    case ESP_ERR_INVALID_VERSION: return "ESP_ERR_INVALID_VERSION";
    case ESP_ERR_INVALID_MAC: return "ESP_ERR_INVALID_MAC";
    case ESP_ERR_NOT_FINISHED: return "ESP_ERR_NOT_FINISHED";
    case ESP_ERR_NOT_ALLOWED: return "ESP_ERR_NOT_ALLOWED";
    case ESP_ERR_NVS_NOT_FOUND: return "ESP_ERR_NVS_NOT_FOUND";
    case ESP_ERR_NVS_NO_FREE_PAGES: return "ESP_ERR_NVS_NO_FREE_PAGES";
    case ESP_ERR_NVS_NEW_VERSION_FOUND: return "ESP_ERR_NVS_NEW_VERSION_FOUND";
    case ESP_ERR_WIFI_NOT_INIT: return "ESP_ERR_WIFI_NOT_INIT";
    case ESP_ERR_WIFI_NOT_STARTED: return "ESP_ERR_WIFI_NOT_STARTED";
    case ESP_ERR_HTTPD_HANDLERS_FULL: return "ESP_ERR_HTTPD_HANDLERS_FULL";
    case ESP_ERR_HTTPD_HANDLER_EXISTS: return "ESP_ERR_HTTPD_HANDLER_EXISTS";
    case ESP_ERR_HTTPD_INVALID_REQ: return "ESP_ERR_HTTPD_INVALID_REQ";
    case ESP_ERR_HTTPD_RESULT_TRUNC: return "ESP_ERR_HTTPD_RESULT_TRUNC";
    case ESP_ERR_HTTPD_RESP_SEND: return "ESP_ERR_HTTPD_RESP_SEND";
    case ESP_ERR_HTTPD_TASK: return "ESP_ERR_HTTPD_TASK";
    }
    return "UNKNOWN ERROR";
}

const char *esp_err_to_name_r(esp_err_t codigo, char *buf, size_t n)
{
    snprintf(buf, n, "%s", esp_err_to_name(codigo));
    return buf;
}

void _esp_error_check_failed(esp_err_t codigo, const char *arquivo, int linha, const char *funcao,
                             const char *expressao)
{
    char detalhe[256];
    printf("ESP_ERROR_CHECK failed: esp_err_t 0x%x (%s) at %p\n", codigo, esp_err_to_name(codigo), (void *)0);
    printf("file: \"%s\" line %d\nfunc: %s\nexpression: %s\n", arquivo, linha, funcao, expressao);
    snprintf(detalhe, sizeof(detalhe), "ESP_ERROR_CHECK failed: esp_err_t 0x%x (%s)", codigo,
             esp_err_to_name(codigo));
    cs_panico("abort()", detalhe);
}

/* ---------------------------------------------------------------------
 * log */
typedef struct nivel_de_tag { char tag[32]; esp_log_level_t nivel; struct nivel_de_tag *proximo; } nivel_de_tag;
static nivel_de_tag *niveis;
static esp_log_level_t nivel_padrao = (esp_log_level_t)CONFIG_LOG_DEFAULT_LEVEL;
static cs_giro trava_do_log;
static vprintf_like_t saida_do_log = vprintf;

void esp_log_level_set(const char *tag, esp_log_level_t nivel)
{
    if (!tag || strcmp(tag, "*") == 0) {
        nivel_padrao = nivel;
        return;
    }
    cs_girar(&trava_do_log);
    for (nivel_de_tag *t = niveis; t; t = t->proximo)
        if (strcmp(t->tag, tag) == 0) {
            t->nivel = nivel;
            cs_soltar(&trava_do_log);
            return;
        }
    nivel_de_tag *t = (nivel_de_tag *)malloc(sizeof(nivel_de_tag));
    if (t) {
        strncpy(t->tag, tag, sizeof(t->tag) - 1);
        t->tag[sizeof(t->tag) - 1] = 0;
        t->nivel = nivel;
        t->proximo = niveis;
        niveis = t;
    }
    cs_soltar(&trava_do_log);
}

esp_log_level_t esp_log_level_get(const char *tag)
{
    esp_log_level_t n = nivel_padrao;
    cs_girar(&trava_do_log);
    for (nivel_de_tag *t = niveis; t; t = t->proximo)
        if (tag && strcmp(t->tag, tag) == 0) {
            n = t->nivel;
            break;
        }
    cs_soltar(&trava_do_log);
    return n;
}

uint32_t esp_log_timestamp(void) { return (uint32_t)(cs_micros() / 1000); }
uint32_t esp_log_early_timestamp(void) { return esp_log_timestamp(); }

char *esp_log_system_timestamp(void)
{
    static char texto[24];
    uint32_t ms = esp_log_timestamp();
    snprintf(texto, sizeof(texto), "%02lu:%02lu:%02lu.%03lu", (unsigned long)(ms / 3600000) % 24,
             (unsigned long)(ms / 60000) % 60, (unsigned long)(ms / 1000) % 60, (unsigned long)ms % 1000);
    return texto;
}

vprintf_like_t esp_log_set_vprintf(vprintf_like_t funcao)
{
    vprintf_like_t antes = saida_do_log;
    saida_do_log = funcao ? funcao : vprintf;
    return antes;
}

static const char letras_do_nivel[] = "NEWIDV";

void esp_log_writev(esp_log_level_t nivel, const char *tag, const char *formato, va_list ap)
{
    if (nivel > esp_log_level_get(tag) || nivel == ESP_LOG_NONE) return;
    char linha[600];
    int n = snprintf(linha, sizeof(linha), "%c (%lu) %s: ", letras_do_nivel[nivel < 6 ? nivel : 5],
                     (unsigned long)esp_log_timestamp(), tag ? tag : "");
    if (n < 0) n = 0;
    if (n < (int)sizeof(linha)) vsnprintf(linha + n, sizeof(linha) - (size_t)n, formato, ap);
    size_t k = strlen(linha);
    if (k + 2 > sizeof(linha)) k = sizeof(linha) - 2;
    linha[k++] = '\n';
    linha[k] = 0;
    fwrite(linha, 1, k, stdout);
}

void esp_log_write(esp_log_level_t nivel, const char *tag, const char *formato, ...)
{
    va_list ap;
    va_start(ap, formato);
    esp_log_writev(nivel, tag, formato, ap);
    va_end(ap);
}

void esp_log_buffer_hex_internal(const char *tag, const void *buf, uint16_t n, esp_log_level_t nivel)
{
    const uint8_t *p = (const uint8_t *)buf;
    for (int i = 0; i < n; i += 16) {
        char linha[64];
        int k = 0;
        for (int j = i; j < n && j < i + 16; j++) k += snprintf(linha + k, sizeof(linha) - (size_t)k, "%02x ", p[j]);
        if (k) linha[k - 1] = 0;
        esp_log_write(nivel, tag, "%s", linha);
    }
}

void esp_log_buffer_char_internal(const char *tag, const void *buf, uint16_t n, esp_log_level_t nivel)
{
    const char *p = (const char *)buf;
    for (int i = 0; i < n; i += 16) {
        char linha[20];
        int k = 0;
        for (int j = i; j < n && j < i + 16; j++) linha[k++] = isprint((unsigned char)p[j]) ? p[j] : '.';
        linha[k] = 0;
        esp_log_write(nivel, tag, "%s", linha);
    }
}

void esp_log_buffer_hexdump_internal(const char *tag, const void *buf, uint16_t n, esp_log_level_t nivel)
{
    esp_log_buffer_hex_internal(tag, buf, n, nivel);
}

/* ---------------------------------------------------------------------
 * esp_timer */
struct cs_esp_timer {
    cs_alarme alarme;
    esp_timer_cb_t funcao;
    void *arg;
    char nome[24];
};

static void disparar_esp_timer(void *arg)
{
    struct cs_esp_timer *t = (struct cs_esp_timer *)arg;
    t->funcao(t->arg);
}

int64_t esp_timer_get_time(void)
{
    return cs_micros();
}

int64_t esp_timer_get_next_alarm(void) { return cs_micros() + 1000000; }
esp_err_t esp_timer_init(void) { return ESP_OK; }

esp_err_t esp_timer_create(const esp_timer_create_args_t *args, esp_timer_handle_t *timer)
{
    if (!args || !args->callback || !timer) return ESP_ERR_INVALID_ARG;
    struct cs_esp_timer *t = (struct cs_esp_timer *)cs_alocar(sizeof(*t), MALLOC_CAP_INTERNAL);
    if (!t) return ESP_ERR_NO_MEM;
    memset(t, 0, sizeof(*t));
    t->funcao = args->callback;
    t->arg = args->arg;
    strncpy(t->nome, args->name ? args->name : "", sizeof(t->nome) - 1);
    t->alarme.funcao = disparar_esp_timer;
    t->alarme.arg = t;
    *timer = t;
    return ESP_OK;
}

esp_err_t esp_timer_start_once(esp_timer_handle_t t, uint64_t timeout_us)
{
    if (!t) return ESP_ERR_INVALID_ARG;
    if (t->alarme.ativo) return ESP_ERR_INVALID_STATE;
    cs_alarme_armar(&t->alarme, (int64_t)timeout_us, 0);
    return ESP_OK;
}

esp_err_t esp_timer_start_periodic(esp_timer_handle_t t, uint64_t periodo_us)
{
    if (!t || periodo_us == 0) return ESP_ERR_INVALID_ARG;
    if (t->alarme.ativo) return ESP_ERR_INVALID_STATE;
    cs_alarme_armar(&t->alarme, (int64_t)periodo_us, (int64_t)periodo_us);
    return ESP_OK;
}

esp_err_t esp_timer_restart(esp_timer_handle_t t, uint64_t timeout_us)
{
    if (!t) return ESP_ERR_INVALID_ARG;
    int64_t periodo = t->alarme.periodo;
    cs_alarme_armar(&t->alarme, (int64_t)timeout_us, periodo ? (int64_t)timeout_us : 0);
    return ESP_OK;
}

esp_err_t esp_timer_stop(esp_timer_handle_t t)
{
    if (!t) return ESP_ERR_INVALID_ARG;
    if (!t->alarme.ativo) return ESP_ERR_INVALID_STATE;
    cs_alarme_parar(&t->alarme);
    return ESP_OK;
}

esp_err_t esp_timer_delete(esp_timer_handle_t t)
{
    if (!t) return ESP_ERR_INVALID_ARG;
    if (t->alarme.ativo) return ESP_ERR_INVALID_STATE;
    cs_liberar(t);
    return ESP_OK;
}

bool esp_timer_is_active(esp_timer_handle_t t) { return t && t->alarme.ativo; }

/* ---------------------------------------------------------------------
 * sistema */
static char nome_do_chip[16];

static const char *chip(void)
{
    if (!nome_do_chip[0]) cs->chip(nome_do_chip, (int)sizeof(nome_do_chip));
    return nome_do_chip;
}

void esp_restart(void)
{
    cs->reiniciar();
    cs_parando = 1;
    cs_encerrar_tarefa();
}

esp_reset_reason_t esp_reset_reason(void) { return (esp_reset_reason_t)cs->razao_do_reinicio(); }

uint32_t esp_random(void)
{
    return cs->aleatorio();
}

void esp_fill_random(void *buf, size_t n)
{
    uint8_t *p = (uint8_t *)buf;
    while (n) {
        uint32_t r = esp_random();
        for (int i = 0; i < 4 && n; i++, n--) {
            *p++ = (uint8_t)r;
            r >>= 8;
        }
    }
}

const char *esp_get_idf_version(void) { return "v5.5.5"; }

void esp_system_abort(const char *detalhe)
{
    cs_panico("abort()", detalhe);
}

void esp_chip_info(esp_chip_info_t *info)
{
    const char *c = chip();
    memset(info, 0, sizeof(*info));
    info->cores = 2;
    info->revision = 0;
    info->features = CHIP_FEATURE_WIFI_BGN | CHIP_FEATURE_BLE;
    if (strcmp(c, "esp32s3") == 0) info->model = CHIP_ESP32S3;
    else if (strcmp(c, "esp32s2") == 0) { info->model = CHIP_ESP32S2; info->cores = 1; info->features = CHIP_FEATURE_WIFI_BGN; }
    else if (strcmp(c, "esp32c3") == 0) { info->model = CHIP_ESP32C3; info->cores = 1; }
    else if (strcmp(c, "esp32c6") == 0) { info->model = CHIP_ESP32C6; info->cores = 1; }
    else if (strcmp(c, "esp32h2") == 0) { info->model = CHIP_ESP32H2; info->cores = 1; info->features = CHIP_FEATURE_BLE; }
    else { info->model = CHIP_ESP32; info->features |= CHIP_FEATURE_BT; info->revision = 301; }
    if (esp_psram_get_size() > 0) info->features |= CHIP_FEATURE_EMB_PSRAM;
}

esp_err_t esp_task_wdt_init(const esp_task_wdt_config_t *config) { (void)config; return ESP_OK; }
esp_err_t esp_task_wdt_reconfigure(const esp_task_wdt_config_t *config) { (void)config; return ESP_OK; }
esp_err_t esp_task_wdt_deinit(void) { return ESP_OK; }
esp_err_t esp_task_wdt_add(TaskHandle_t t) { (void)t; return ESP_OK; }
esp_err_t esp_task_wdt_delete(TaskHandle_t t) { (void)t; return ESP_OK; }

esp_err_t esp_task_wdt_reset(void)
{
    cs_conferir_parada();
    return ESP_OK;
}

esp_err_t esp_read_mac(uint8_t mac[6], esp_mac_type_t tipo)
{
    cs->wifi_mac(mac);
    if (tipo == ESP_MAC_WIFI_SOFTAP) mac[5] += 1;
    else if (tipo == ESP_MAC_BT) mac[5] += 2;
    else if (tipo == ESP_MAC_ETH) mac[5] += 3;
    return ESP_OK;
}

esp_err_t esp_efuse_mac_get_default(uint8_t mac[6]) { return esp_read_mac(mac, ESP_MAC_WIFI_STA); }
esp_err_t esp_base_mac_addr_get(uint8_t mac[6]) { return esp_read_mac(mac, ESP_MAC_WIFI_STA); }

esp_cpu_cycle_count_t esp_cpu_get_cycle_count(void)
{
    return (esp_cpu_cycle_count_t)(cs_micros() * 240);
}

int esp_cpu_get_core_id(void) { return (int)xPortGetCoreID(); }
void esp_cpu_wait_for_intr(void) { cs_dormir_us(1000); }

int ets_printf(const char *formato, ...)
{
    va_list ap;
    va_start(ap, formato);
    int r = vprintf(formato, ap);
    va_end(ap);
    return r;
}

int esp_rom_printf(const char *formato, ...)
{
    va_list ap;
    va_start(ap, formato);
    int r = vprintf(formato, ap);
    va_end(ap);
    return r;
}

/* Espera curta girando no relogio, como a da ROM: nao cede a tarefa. */
void ets_delay_us(uint32_t us)
{
    int64_t fim = cs_micros() + us;
    if (us > 2000) {
        cs_dormir_us((int64_t)us - 1000);
    }
    while (cs_micros() < fim) cs_pausa();
}

void esp_rom_delay_us(uint32_t us) { ets_delay_us(us); }

/* ---------------------------------------------------------------------
 * pinos */
#define PINOS 64

typedef struct {
    gpio_int_type_t tipo;
    int ligada;
    gpio_isr_t funcao;
    void *arg;
    int modo;
} pino_t;

static pino_t pinos[PINOS];
static int servico_de_isr;

/* O pino existe neste chip? E pode ser saida? (as mascaras do soc_caps.h) */
static int valido(gpio_num_t p) { return p >= 0 && p < PINOS && GPIO_IS_VALID_GPIO(p); }
static int saida_valida(gpio_num_t p) { return p >= 0 && p < PINOS && GPIO_IS_VALID_OUTPUT_GPIO(p); }

static void armar(int p)
{
    int tipo = pinos[p].ligada && pinos[p].funcao && servico_de_isr ? (int)pinos[p].tipo : 0;
    cs->gpio_interrupcao(p, tipo);
}

esp_err_t gpio_set_direction(gpio_num_t p, gpio_mode_t modo)
{
    if (!valido(p)) {
        ESP_LOGE("gpio", "GPIO number error");
        return ESP_ERR_INVALID_ARG;
    }
    if ((modo & GPIO_MODE_DEF_OUTPUT) && !saida_valida(p)) {
        ESP_LOGE("gpio", "io_num=%d can only be input", (int)p);
        return ESP_ERR_INVALID_ARG;
    }
    int m = pinos[p].modo & (CS_GPIO_PULLUP | CS_GPIO_PULLDOWN);
    if (modo & GPIO_MODE_DEF_INPUT) m |= CS_GPIO_ENTRADA;
    if (modo & GPIO_MODE_DEF_OUTPUT) m |= CS_GPIO_SAIDA;
    if (modo & GPIO_MODE_DEF_OD) m |= CS_GPIO_DRENO;
    pinos[p].modo = m;
    cs->gpio_modo(p, m);
    CS_CONFERIR();
    return ESP_OK;
}

esp_err_t gpio_set_pull_mode(gpio_num_t p, gpio_pull_mode_t pull)
{
    if (!valido(p)) return ESP_ERR_INVALID_ARG;
    int m = pinos[p].modo & ~(CS_GPIO_PULLUP | CS_GPIO_PULLDOWN);
    if (pull == GPIO_PULLUP_ONLY || pull == GPIO_PULLUP_PULLDOWN) m |= CS_GPIO_PULLUP;
    if (pull == GPIO_PULLDOWN_ONLY || pull == GPIO_PULLUP_PULLDOWN) m |= CS_GPIO_PULLDOWN;
    pinos[p].modo = m;
    cs->gpio_modo(p, m);
    CS_CONFERIR();
    return ESP_OK;
}

esp_err_t gpio_pullup_en(gpio_num_t p) { return valido(p) ? gpio_set_pull_mode(p, (pinos[p].modo & CS_GPIO_PULLDOWN) ? GPIO_PULLUP_PULLDOWN : GPIO_PULLUP_ONLY) : ESP_ERR_INVALID_ARG; }
esp_err_t gpio_pullup_dis(gpio_num_t p) { return valido(p) ? gpio_set_pull_mode(p, (pinos[p].modo & CS_GPIO_PULLDOWN) ? GPIO_PULLDOWN_ONLY : GPIO_FLOATING) : ESP_ERR_INVALID_ARG; }
esp_err_t gpio_pulldown_en(gpio_num_t p) { return valido(p) ? gpio_set_pull_mode(p, (pinos[p].modo & CS_GPIO_PULLUP) ? GPIO_PULLUP_PULLDOWN : GPIO_PULLDOWN_ONLY) : ESP_ERR_INVALID_ARG; }
esp_err_t gpio_pulldown_dis(gpio_num_t p) { return valido(p) ? gpio_set_pull_mode(p, (pinos[p].modo & CS_GPIO_PULLUP) ? GPIO_PULLUP_ONLY : GPIO_FLOATING) : ESP_ERR_INVALID_ARG; }

esp_err_t gpio_set_intr_type(gpio_num_t p, gpio_int_type_t tipo)
{
    if (!valido(p) || tipo >= GPIO_INTR_MAX) return ESP_ERR_INVALID_ARG;
    pinos[p].tipo = tipo;
    pinos[p].ligada = tipo != GPIO_INTR_DISABLE;
    armar(p);
    CS_CONFERIR();
    return ESP_OK;
}

esp_err_t gpio_intr_enable(gpio_num_t p)
{
    if (!valido(p)) return ESP_ERR_INVALID_ARG;
    pinos[p].ligada = pinos[p].tipo != GPIO_INTR_DISABLE;
    armar(p);
    return ESP_OK;
}

esp_err_t gpio_intr_disable(gpio_num_t p)
{
    if (!valido(p)) return ESP_ERR_INVALID_ARG;
    pinos[p].ligada = 0;
    armar(p);
    return ESP_OK;
}

esp_err_t gpio_config(const gpio_config_t *c)
{
    if (!c) return ESP_ERR_INVALID_ARG;
    /* As mesmas contas do gpio.c do ESP-IDF (inclusive o ~ de uma mascara
     * de 32 bits nos chips RISC-V, que nao olha os bits de cima). */
    if (c->pin_bit_mask == 0 || (c->pin_bit_mask & ~SOC_GPIO_VALID_GPIO_MASK)) {
        ESP_LOGE("gpio", "GPIO_PIN mask error");
        return ESP_ERR_INVALID_ARG;
    }
    if ((c->mode & GPIO_MODE_DEF_OUTPUT) && (c->pin_bit_mask & ~SOC_GPIO_VALID_OUTPUT_GPIO_MASK)) {
        ESP_LOGE("gpio", "GPIO can only be used as input mode");
        return ESP_ERR_INVALID_ARG;
    }
    for (int p = 0; p < GPIO_NUM_MAX; p++) {
        if (!(c->pin_bit_mask & (1ULL << p)) || !valido(p)) continue;
        int m = 0;
        if (c->mode & GPIO_MODE_DEF_INPUT) m |= CS_GPIO_ENTRADA;
        if (c->mode & GPIO_MODE_DEF_OUTPUT) m |= CS_GPIO_SAIDA;
        if (c->mode & GPIO_MODE_DEF_OD) m |= CS_GPIO_DRENO;
        if (c->pull_up_en) m |= CS_GPIO_PULLUP;
        if (c->pull_down_en) m |= CS_GPIO_PULLDOWN;
        pinos[p].modo = m;
        cs->gpio_modo(p, m);
        pinos[p].tipo = c->intr_type;
        pinos[p].ligada = c->intr_type != GPIO_INTR_DISABLE;
        armar(p);
    }
    CS_CONFERIR();
    return ESP_OK;
}

esp_err_t gpio_reset_pin(gpio_num_t p)
{
    if (!valido(p)) return ESP_ERR_INVALID_ARG;
    pinos[p].tipo = GPIO_INTR_DISABLE;
    pinos[p].ligada = 0;
    armar(p);
    pinos[p].modo = CS_GPIO_ENTRADA | CS_GPIO_PULLUP;
    cs->gpio_modo(p, pinos[p].modo);
    CS_CONFERIR();
    return ESP_OK;
}

esp_err_t gpio_set_level(gpio_num_t p, uint32_t nivel)
{
    if (!saida_valida(p)) {
        ESP_LOGE("gpio", "GPIO output gpio_num error");
        return ESP_ERR_INVALID_ARG;
    }
    cs->gpio_escrever(p, nivel ? 1 : 0);
    CS_CONFERIR();
    return ESP_OK;
}

int gpio_get_level(gpio_num_t p)
{
    if (!valido(p)) return 0;
    int v = cs->gpio_ler(p);
    CS_CONFERIR();
    return v;
}

static void tarefa_das_interrupcoes(void *arg)
{
    (void)arg;
    cs_tarefa *eu = cs_tarefa_atual();
    for (;;) {
        int p = cs->gpio_proxima_borda(-1);
        if (p == -2) cs_encerrar_tarefa();
        CS_CONFERIR();
        if (p < 0 || p >= PINOS) continue;
        gpio_isr_t funcao = pinos[p].funcao;
        void *argumento = pinos[p].arg;
        if (funcao && pinos[p].ligada) {
            eu->em_isr = 1;
            funcao(argumento);
            eu->em_isr = 0;
        }
    }
}

esp_err_t gpio_install_isr_service(int flags)
{
    (void)flags;
    if (servico_de_isr) {
        ESP_LOGE("gpio", "GPIO isr service already installed");
        return ESP_ERR_INVALID_STATE;
    }
    servico_de_isr = 1;
    xTaskCreatePinnedToCore(tarefa_das_interrupcoes, "gpio_isr", 2048, NULL, configMAX_PRIORITIES - 1, NULL, 0);
    for (int p = 0; p < GPIO_NUM_MAX; p++)
        if (valido(p)) armar(p);
    return ESP_OK;
}

void gpio_uninstall_isr_service(void)
{
    servico_de_isr = 0;
    for (int p = 0; p < GPIO_NUM_MAX; p++)
        if (valido(p)) armar(p);
}

esp_err_t gpio_isr_handler_add(gpio_num_t p, gpio_isr_t tratador, void *arg)
{
    if (!valido(p)) return ESP_ERR_INVALID_ARG;
    if (!servico_de_isr) {
        ESP_LOGE("gpio", "GPIO isr service is not installed, call gpio_install_isr_service() first");
        return ESP_ERR_INVALID_STATE;
    }
    pinos[p].funcao = tratador;
    pinos[p].arg = arg;
    armar(p);
    CS_CONFERIR();
    return ESP_OK;
}

esp_err_t gpio_isr_handler_remove(gpio_num_t p)
{
    if (!valido(p)) return ESP_ERR_INVALID_ARG;
    pinos[p].funcao = NULL;
    armar(p);
    return ESP_OK;
}

esp_err_t gpio_isr_register(void (*tratador)(void *), void *arg, int flags, gpio_isr_handle_t *alca)
{
    (void)tratador; (void)arg; (void)flags; (void)alca;
    ESP_LOGE("gpio", "gpio_isr_register: use gpio_install_isr_service + gpio_isr_handler_add");
    return ESP_ERR_NOT_SUPPORTED;
}

esp_err_t gpio_set_drive_capability(gpio_num_t p, gpio_drive_cap_t forca) { (void)forca; return valido(p) ? ESP_OK : ESP_ERR_INVALID_ARG; }

esp_err_t gpio_get_drive_capability(gpio_num_t p, gpio_drive_cap_t *forca)
{
    if (!valido(p)) return ESP_ERR_INVALID_ARG;
    *forca = GPIO_DRIVE_CAP_DEFAULT;
    return ESP_OK;
}

esp_err_t gpio_hold_en(gpio_num_t p) { return valido(p) ? ESP_OK : ESP_ERR_INVALID_ARG; }
esp_err_t gpio_hold_dis(gpio_num_t p) { return valido(p) ? ESP_OK : ESP_ERR_INVALID_ARG; }
void gpio_deep_sleep_hold_en(void) {}
void gpio_deep_sleep_hold_dis(void) {}
esp_err_t gpio_wakeup_enable(gpio_num_t p, gpio_int_type_t tipo) { (void)tipo; return valido(p) ? ESP_OK : ESP_ERR_INVALID_ARG; }
esp_err_t gpio_wakeup_disable(gpio_num_t p) { return valido(p) ? ESP_OK : ESP_ERR_INVALID_ARG; }
void gpio_iomux_in(uint32_t pino, uint32_t sinal) { (void)pino; (void)sinal; }
void gpio_iomux_out(uint8_t pino, int funcao, bool oen_inv) { (void)pino; (void)funcao; (void)oen_inv; }
void esp_rom_gpio_pad_select_gpio(uint32_t pino) { (void)pino; }

/* ---------------------------------------------------------------------
 * SPI */
typedef struct {
    int pronto;
    int mosi, miso, sclk;
    int max;
    int dispositivos;
    int64_t trava;
} barramento_t;

static barramento_t barramentos[SPI_HOST_MAX];

typedef struct spi_device_t spi_device_t;

struct spi_device_t {
    int host;
    int cs;
    int hz;
    int modo;
    uint32_t flags;
    uint8_t command_bits, address_bits, dummy_bits;
    transaction_cb_t pre_cb, post_cb;
    int64_t id;
    int fila;
    int64_t vagas;
    int64_t prontas;
    spi_transaction_t **feitas;
    int ini, n;
    cs_giro trava;
};

int cs_spi_barramento_pronto(int host)
{
    return host >= 0 && host < SPI_HOST_MAX && barramentos[host].pronto;
}

esp_err_t spi_bus_initialize(spi_host_device_t host, const spi_bus_config_t *config, spi_dma_chan_t dma)
{
    (void)dma;
    if (host <= SPI1_HOST || host >= SPI_HOST_MAX || !config) {
        ESP_LOGE(TAG_SPI, "invalid host_id");
        return ESP_ERR_INVALID_ARG;
    }
    if (barramentos[host].pronto) {
        ESP_LOGE("spi", "SPI%d already claimed by spi master.", (int)host + 1);
        return ESP_ERR_INVALID_STATE;
    }
    int r = cs->spi_barramento((int)host, config->mosi_io_num, config->miso_io_num, config->sclk_io_num);
    CS_CONFERIR();
    if (r != ESP_OK) return r;
    barramentos[host].pronto = 1;
    barramentos[host].mosi = config->mosi_io_num;
    barramentos[host].miso = config->miso_io_num;
    barramentos[host].sclk = config->sclk_io_num;
    barramentos[host].max = config->max_transfer_sz > 0 ? config->max_transfer_sz : 4092;
    barramentos[host].trava = cs->sem_criar(CS_SEM_RECURSIVO, 1, 1);
    return ESP_OK;
}

esp_err_t spi_bus_free(spi_host_device_t host)
{
    if (host <= SPI1_HOST || host >= SPI_HOST_MAX || !barramentos[host].pronto) return ESP_ERR_INVALID_STATE;
    if (barramentos[host].dispositivos > 0) return ESP_ERR_INVALID_STATE;
    cs->spi_liberar((int)host);
    cs->sem_apagar(barramentos[host].trava);
    memset(&barramentos[host], 0, sizeof(barramentos[host]));
    return ESP_OK;
}

void *spi_bus_dma_memory_alloc(spi_host_device_t host, size_t n, uint32_t caps_extra)
{
    (void)host;
    return heap_caps_malloc(n, MALLOC_CAP_DMA | caps_extra);
}

esp_err_t spi_bus_get_max_transaction_len(spi_host_device_t host, size_t *n)
{
    if (host <= SPI1_HOST || host >= SPI_HOST_MAX || !barramentos[host].pronto) return ESP_ERR_INVALID_ARG;
    *n = (size_t)barramentos[host].max;
    return ESP_OK;
}

static int maximo_de_cs(int host)
{
    return (strcmp(chip(), "esp32s3") == 0 && host == SPI2_HOST) ? 6 : 3;
}

esp_err_t spi_bus_add_device(spi_host_device_t host, const spi_device_interface_config_t *config,
                             spi_device_handle_t *alca)
{
    if (host <= SPI1_HOST || host >= SPI_HOST_MAX || !config || !alca) return ESP_ERR_INVALID_ARG;
    if (!barramentos[host].pronto) {
        ESP_LOGE(TAG_SPI, "host is not initialized");
        return ESP_ERR_INVALID_ARG;
    }
    if (config->clock_speed_hz <= 0) {
        ESP_LOGE(TAG_SPI, "invalid sclk speed");
        return ESP_ERR_INVALID_ARG;
    }
    if (barramentos[host].dispositivos >= maximo_de_cs(host)) {
        ESP_LOGE(TAG_SPI, "no free cs pins for the host");
        return ESP_ERR_NOT_FOUND;
    }
    spi_device_t *d = (spi_device_t *)cs_alocar(sizeof(spi_device_t), MALLOC_CAP_INTERNAL);
    if (!d) return ESP_ERR_NO_MEM;
    memset(d, 0, sizeof(*d));
    d->fila = config->queue_size > 0 ? config->queue_size : 1;
    d->feitas = (spi_transaction_t **)cs_alocar(sizeof(spi_transaction_t *) * (size_t)d->fila, MALLOC_CAP_INTERNAL);
    if (!d->feitas) {
        cs_liberar(d);
        return ESP_ERR_NO_MEM;
    }
    d->host = host;
    d->cs = config->spics_io_num;
    d->hz = config->clock_speed_hz;
    d->modo = config->mode;
    d->flags = config->flags;
    d->command_bits = config->command_bits;
    d->address_bits = config->address_bits;
    d->dummy_bits = config->dummy_bits;
    d->pre_cb = config->pre_cb;
    d->post_cb = config->post_cb;
    d->vagas = cs->sem_criar(CS_SEM_CONTAGEM, d->fila, d->fila);
    d->prontas = cs->sem_criar(CS_SEM_CONTAGEM, d->fila, 0);
    d->id = cs->spi_dispositivo((int)host, d->cs, d->hz, d->modo, (int)d->flags);
    CS_CONFERIR();
    barramentos[host].dispositivos++;
    *alca = d;
    return ESP_OK;
}

esp_err_t spi_bus_remove_device(spi_device_handle_t d)
{
    if (!d) return ESP_ERR_INVALID_ARG;
    if (d->n > 0) return ESP_ERR_INVALID_STATE;
    cs->spi_remover(d->id);
    cs->sem_apagar(d->vagas);
    cs->sem_apagar(d->prontas);
    barramentos[d->host].dispositivos--;
    cs_liberar(d->feitas);
    cs_liberar(d);
    return ESP_OK;
}

/* Uma transacao: o pre_cb (que acerta o D/C do display), as fases de
 * comando e endereco quando o dispositivo tem, os dados, o post_cb. */
static esp_err_t executar(spi_device_t *d, spi_transaction_t *t)
{
    if (!t) return ESP_ERR_INVALID_ARG;
    if ((t->flags & SPI_TRANS_USE_RXDATA) && t->rxlength > 32) return ESP_ERR_INVALID_ARG;
    if ((t->flags & SPI_TRANS_USE_TXDATA) && t->length > 32) return ESP_ERR_INVALID_ARG;
    size_t max_bits = (size_t)barramentos[d->host].max * 8;
    if (t->length > max_bits || t->rxlength > max_bits) {
        ESP_LOGE(TAG_SPI, "txdata transfer > host maximum");
        return ESP_ERR_INVALID_ARG;
    }

    int64_t trava = barramentos[d->host].trava;
    cs_sem_pegar(trava, -1);

    if (d->pre_cb) d->pre_cb(t);

    const uint8_t *tx = (t->flags & SPI_TRANS_USE_TXDATA) ? t->tx_data : (const uint8_t *)t->tx_buffer;
    uint8_t *rx = (t->flags & SPI_TRANS_USE_RXDATA) ? t->rx_data : (uint8_t *)t->rx_buffer;
    int bits_tx = (int)t->length;
    int bits_rx = rx ? (int)(t->rxlength ? t->rxlength : t->length) : 0;

    int cb = d->command_bits, ab = d->address_bits, db = d->dummy_bits;
    if (t->flags & (SPI_TRANS_VARIABLE_CMD | SPI_TRANS_VARIABLE_ADDR | SPI_TRANS_VARIABLE_DUMMY)) {
        spi_transaction_ext_t *e = (spi_transaction_ext_t *)t;
        if (t->flags & SPI_TRANS_VARIABLE_CMD) cb = e->command_bits;
        if (t->flags & SPI_TRANS_VARIABLE_ADDR) ab = e->address_bits;
        if (t->flags & SPI_TRANS_VARIABLE_DUMMY) db = e->dummy_bits;
    }

    if (cb || ab || db) {
        /* As fases vao antes dos dados, em bytes inteiros (MSB primeiro). */
        int bytes_fase = (cb + 7) / 8 + (ab + 7) / 8 + (db + 7) / 8;
        int bytes_dados = (bits_tx + 7) / 8;
        uint8_t pilha[64];
        uint8_t *junto = bytes_fase + bytes_dados <= (int)sizeof(pilha)
            ? pilha : (uint8_t *)malloc((size_t)(bytes_fase + bytes_dados));
        if (junto) {
            int k = 0;
            for (int b = (cb + 7) / 8 - 1; b >= 0; b--) junto[k++] = (uint8_t)(t->cmd >> (8 * b));
            for (int b = (ab + 7) / 8 - 1; b >= 0; b--) junto[k++] = (uint8_t)(t->addr >> (8 * b));
            for (int b = 0; b < (db + 7) / 8; b++) junto[k++] = 0;
            if (tx && bytes_dados) memcpy(junto + k, tx, (size_t)bytes_dados);
            else if (bytes_dados) memset(junto + k, 0, (size_t)bytes_dados);
            cs->spi_transferir(d->id, junto, NULL, 8 * bytes_fase, 0);
            if (bits_tx || bits_rx) cs->spi_transferir(d->id, junto + bytes_fase, rx, bits_tx, bits_rx);
            if (junto != pilha) free(junto);
        }
    } else {
        cs->spi_transferir(d->id, tx, rx, bits_tx, bits_rx);
    }

    if (d->post_cb) d->post_cb(t);
    cs->sem_dar(trava);
    CS_CONFERIR();
    return ESP_OK;
}

esp_err_t spi_device_queue_trans(spi_device_handle_t d, spi_transaction_t *t, TickType_t espera)
{
    if (!d || !t) return ESP_ERR_INVALID_ARG;
    if (cs_sem_pegar(d->vagas, cs_espera_us(espera)) == 0) return ESP_ERR_TIMEOUT;
    esp_err_t r = executar(d, t);
    if (r != ESP_OK) {
        cs->sem_dar(d->vagas);
        return r;
    }
    if (d->flags & SPI_DEVICE_NO_RETURN_RESULT) {
        cs->sem_dar(d->vagas);
        return ESP_OK;
    }
    cs_girar(&d->trava);
    d->feitas[(d->ini + d->n) % d->fila] = t;
    d->n++;
    cs_soltar(&d->trava);
    cs->sem_dar(d->prontas);
    return ESP_OK;
}

esp_err_t spi_device_get_trans_result(spi_device_handle_t d, spi_transaction_t **t, TickType_t espera)
{
    if (!d || !t) return ESP_ERR_INVALID_ARG;
    if (cs_sem_pegar(d->prontas, cs_espera_us(espera)) == 0) return ESP_ERR_TIMEOUT;
    cs_girar(&d->trava);
    *t = d->feitas[d->ini];
    d->ini = (d->ini + 1) % d->fila;
    d->n--;
    cs_soltar(&d->trava);
    cs->sem_dar(d->vagas);
    return ESP_OK;
}

esp_err_t spi_device_transmit(spi_device_handle_t d, spi_transaction_t *t)
{
    if (!d || !t) return ESP_ERR_INVALID_ARG;
    if (d->n > 0) {
        ESP_LOGE(TAG_SPI, "Cannot send polling transaction while the previous polling transaction is not terminated.");
    }
    return executar(d, t);
}

static spi_transaction_t *polling_pendente;

esp_err_t spi_device_polling_start(spi_device_handle_t d, spi_transaction_t *t, TickType_t espera)
{
    (void)espera;
    esp_err_t r = executar(d, t);
    if (r == ESP_OK) polling_pendente = t;
    return r;
}

esp_err_t spi_device_polling_end(spi_device_handle_t d, TickType_t espera)
{
    (void)d; (void)espera;
    polling_pendente = NULL;
    return ESP_OK;
}

esp_err_t spi_device_polling_transmit(spi_device_handle_t d, spi_transaction_t *t)
{
    if (!d || !t) return ESP_ERR_INVALID_ARG;
    return executar(d, t);
}

esp_err_t spi_device_acquire_bus(spi_device_handle_t d, TickType_t espera)
{
    if (!d) return ESP_ERR_INVALID_ARG;
    if (cs_sem_pegar(barramentos[d->host].trava, cs_espera_us(espera)) == 0) return ESP_ERR_TIMEOUT;
    return ESP_OK;
}

void spi_device_release_bus(spi_device_handle_t d)
{
    if (d) cs->sem_dar(barramentos[d->host].trava);
}

esp_err_t spi_device_get_actual_freq(spi_device_handle_t d, int *freq_khz)
{
    if (!d || !freq_khz) return ESP_ERR_INVALID_ARG;
    *freq_khz = spi_get_actual_clock(80000000, d->hz, 128) / 1000;
    return ESP_OK;
}

/* O divisor do relogio de 80 MHz: 40, 26,67, 20, 16... como o chip. */
int spi_get_actual_clock(int fapb, int hz, int duty_cycle)
{
    (void)duty_cycle;
    if (hz >= fapb) return fapb;
    int divisor = (fapb + hz - 1) / hz;
    return fapb / divisor;
}

/* ---------------------------------------------------------------------
 * cartao SD pelo SPI */
esp_err_t sdspi_host_init(void) { return ESP_OK; }
esp_err_t sdspi_host_do_transaction(int slot, sdmmc_command_t *cmd) { (void)slot; (void)cmd; return ESP_ERR_NOT_SUPPORTED; }
esp_err_t sdspi_host_set_card_clk(int slot, uint32_t freq_khz) { (void)slot; (void)freq_khz; return ESP_OK; }
esp_err_t sdspi_host_get_real_freq(int slot, int *freq) { (void)slot; *freq = 20000; return ESP_OK; }
esp_err_t sdspi_host_remove_device(int slot) { (void)slot; return ESP_OK; }
esp_err_t sdspi_host_deinit(void) { return ESP_OK; }
esp_err_t sdspi_host_io_int_enable(int slot) { (void)slot; return ESP_OK; }
esp_err_t sdspi_host_io_int_wait(int slot, TickType_t espera) { (void)slot; (void)espera; return ESP_OK; }
esp_err_t sdmmc_host_init(void) { return ESP_OK; }
esp_err_t sdmmc_host_deinit(void) { return ESP_OK; }

esp_err_t sdspi_host_init_device(const sdspi_device_config_t *config, sdspi_dev_handle_t *alca)
{
    if (!config) return ESP_ERR_INVALID_ARG;
    if (!cs_spi_barramento_pronto(config->host_id)) return ESP_ERR_INVALID_STATE;
    if (alca) *alca = config->host_id;
    return ESP_OK;
}

static void preencher_cartao(sdmmc_card_t *c, const sdmmc_host_t *host)
{
    memset(c, 0, sizeof(*c));
    if (host) c->host = *host;
    c->ocr = 0x40ff8000;
    c->cid.mfg_id = 3;
    strcpy(c->cid.name, "SC32G");
    c->csd.sector_size = 512;
    c->csd.capacity = 62333952;
    c->csd.read_block_len = 9;
    c->max_freq_khz = 20000;
    c->real_freq_khz = 20000;
    c->is_mem = 1;
}

esp_err_t esp_vfs_fat_sdspi_mount(const char *ponto, const sdmmc_host_t *host, const sdspi_device_config_t *slot,
                                  const esp_vfs_fat_mount_config_t *config, sdmmc_card_t **cartao)
{
    (void)config;
    if (!ponto || !host || !slot) return ESP_ERR_INVALID_ARG;
    int h = slot->host_id;
    if (!cs_spi_barramento_pronto(h)) {
        ESP_LOGE("sdspi_host", "spi_bus_add_device failed with rc=0x%x", ESP_ERR_INVALID_ARG);
        ESP_LOGE("vfs_fat_sdmmc", "Failed to attach sdspi device onto an SPI bus (rc=0x%x), please initialize the "
                 "            bus first and check the device parameters.", ESP_ERR_INVALID_ARG);
        return ESP_ERR_INVALID_ARG;
    }
    int r = cs->montar(ponto, CS_MONTAR_SD_SPI, h, slot->gpio_cs, NULL);
    CS_CONFERIR();
    if (r != ESP_OK) {
        if (r == ESP_ERR_TIMEOUT || r == ESP_ERR_NOT_FOUND)
            ESP_LOGE("vfs_fat_sdmmc", "sdmmc_card_init failed (0x%x).", r);
        else
            ESP_LOGE("vfs_fat_sdmmc", "failed to mount card (%d)", r);
        return r;
    }
    if (cartao) {
        sdmmc_card_t *c = (sdmmc_card_t *)malloc(sizeof(sdmmc_card_t));
        if (c) preencher_cartao(c, host);
        *cartao = c;
    }
    return ESP_OK;
}

esp_err_t esp_vfs_fat_sdmmc_mount(const char *ponto, const sdmmc_host_t *host, const void *slot,
                                  const esp_vfs_fat_mount_config_t *config, sdmmc_card_t **cartao)
{
    (void)config; (void)slot;
    if (!ponto || !host) return ESP_ERR_INVALID_ARG;
    int r = cs->montar(ponto, CS_MONTAR_SD_MMC, host->slot, -1, NULL);
    CS_CONFERIR();
    if (r != ESP_OK) {
        ESP_LOGE("vfs_fat_sdmmc", "sdmmc_card_init failed (0x%x).", r);
        return r;
    }
    if (cartao) {
        sdmmc_card_t *c = (sdmmc_card_t *)malloc(sizeof(sdmmc_card_t));
        if (c) preencher_cartao(c, host);
        *cartao = c;
    }
    return ESP_OK;
}

esp_err_t esp_vfs_fat_sdcard_unmount(const char *ponto, sdmmc_card_t *cartao)
{
    int r = cs->desmontar(ponto);
    free(cartao);
    return r;
}

esp_err_t esp_vfs_fat_sdmmc_unmount(void)
{
    return cs->desmontar("/sdcard");
}

esp_err_t esp_vfs_fat_spiflash_mount_rw_wl(const char *ponto, const char *particao,
                                           const esp_vfs_fat_mount_config_t *config, wl_handle_t *alca)
{
    (void)config;
    int r = cs->montar(ponto, CS_MONTAR_FLASH, -1, -1, particao);
    CS_CONFERIR();
    if (alca) *alca = r == ESP_OK ? 1 : WL_INVALID_HANDLE;
    return r;
}

esp_err_t esp_vfs_fat_spiflash_unmount_rw_wl(const char *ponto, wl_handle_t alca)
{
    (void)alca;
    return cs->desmontar(ponto);
}

esp_err_t esp_vfs_fat_spiflash_mount_ro(const char *ponto, const char *particao,
                                        const esp_vfs_fat_mount_config_t *config)
{
    (void)config;
    int r = cs->montar(ponto, CS_MONTAR_FLASH_SO_LEITURA, -1, -1, particao);
    CS_CONFERIR();
    return r;
}

esp_err_t esp_vfs_fat_spiflash_unmount_ro(const char *ponto, const char *particao)
{
    (void)particao;
    return cs->desmontar(ponto);
}

esp_err_t esp_vfs_fat_info(const char *ponto, uint64_t *total, uint64_t *livre)
{
    (void)ponto;
    if (total) *total = 31914983424ULL;
    if (livre) *livre = 31000000000ULL;
    return ESP_OK;
}

esp_err_t sdmmc_card_init(const sdmmc_host_t *host, sdmmc_card_t *card)
{
    preencher_cartao(card, host);
    return ESP_OK;
}

void sdmmc_card_print_info(FILE *saida, const sdmmc_card_t *c)
{
    if (!c) return;
    uint64_t mb = (uint64_t)c->csd.capacity * c->csd.sector_size / (1024 * 1024);
    fprintf(saida, "Name: %s\n", c->cid.name);
    fprintf(saida, "Type: SDHC/SDXC\n");
    fprintf(saida, "Speed: %.2f MHz (limit: %.2f MHz)\n", c->real_freq_khz / 1000.0, c->max_freq_khz / 1000.0);
    fprintf(saida, "Size: %lluMB\n", (unsigned long long)mb);
    fprintf(saida, "CSD: ver=2, sector_size=%lu, capacity=%lu read_bl_len=%lu\n",
            (unsigned long)c->csd.sector_size, (unsigned long)c->csd.capacity, (unsigned long)c->csd.read_block_len);
    fprintf(saida, "SSR: bus_width=1\n");
}

esp_err_t sdmmc_get_status(sdmmc_card_t *card) { (void)card; return ESP_OK; }

esp_err_t sdmmc_read_sectors(sdmmc_card_t *card, void *dst, size_t inicio, size_t quantos)
{
    (void)card; (void)dst; (void)inicio; (void)quantos;
    return ESP_ERR_NOT_SUPPORTED;
}

esp_err_t sdmmc_write_sectors(sdmmc_card_t *card, const void *src, size_t inicio, size_t quantos)
{
    (void)card; (void)src; (void)inicio; (void)quantos;
    return ESP_ERR_NOT_SUPPORTED;
}

/* ---------------------------------------------------------------------
 * I2S (o driver novo e o antigo) */
struct i2s_channel_obj_t {
    int porta;
    int iniciado;
    int ativo;
    int taxa, bits, canais;
    int bclk, ws, dout, din;
    int64_t canal;
};

static struct i2s_channel_obj_t *portas_i2s[2];

esp_err_t i2s_new_channel(const i2s_chan_config_t *config, i2s_chan_handle_t *tx, i2s_chan_handle_t *rx)
{
    if (!config || (!tx && !rx)) return ESP_ERR_INVALID_ARG;
    int porta = config->id;
    if (porta == I2S_NUM_AUTO) {
        porta = !portas_i2s[0] ? 0 : !portas_i2s[1] ? 1 : -1;
        if (porta < 0) return ESP_ERR_NOT_FOUND;
    }
    if (porta < 0 || porta > 1) return ESP_ERR_INVALID_ARG;
    if (portas_i2s[porta]) {
        ESP_LOGE("i2s_common", "no available channel found");
        return ESP_ERR_NOT_FOUND;
    }
    struct i2s_channel_obj_t *c = (struct i2s_channel_obj_t *)cs_alocar(sizeof(*c), MALLOC_CAP_INTERNAL);
    if (!c) return ESP_ERR_NO_MEM;
    memset(c, 0, sizeof(*c));
    c->porta = porta;
    c->bclk = c->ws = c->dout = c->din = -1;
    portas_i2s[porta] = c;
    if (tx) *tx = c;
    if (rx) *rx = tx ? NULL : c;
    return ESP_OK;
}

esp_err_t i2s_channel_init_std_mode(i2s_chan_handle_t c, const i2s_std_config_t *config)
{
    if (!c || !config) return ESP_ERR_INVALID_ARG;
    if (c->ativo) return ESP_ERR_INVALID_STATE;
    c->taxa = (int)config->clk_cfg.sample_rate_hz;
    c->bits = (int)config->slot_cfg.data_bit_width;
    c->canais = config->slot_cfg.slot_mode == I2S_SLOT_MODE_MONO ? 1 : 2;
    c->bclk = config->gpio_cfg.bclk;
    c->ws = config->gpio_cfg.ws;
    c->dout = config->gpio_cfg.dout;
    c->din = config->gpio_cfg.din;
    c->iniciado = 1;
    return ESP_OK;
}

esp_err_t i2s_channel_reconfig_std_clock(i2s_chan_handle_t c, const i2s_std_clk_config_t *clk)
{
    if (!c || !clk) return ESP_ERR_INVALID_ARG;
    if (c->ativo) return ESP_ERR_INVALID_STATE;
    c->taxa = (int)clk->sample_rate_hz;
    return ESP_OK;
}

esp_err_t i2s_channel_reconfig_std_slot(i2s_chan_handle_t c, const i2s_std_slot_config_t *slot)
{
    if (!c || !slot) return ESP_ERR_INVALID_ARG;
    if (c->ativo) return ESP_ERR_INVALID_STATE;
    c->bits = (int)slot->data_bit_width;
    c->canais = slot->slot_mode == I2S_SLOT_MODE_MONO ? 1 : 2;
    return ESP_OK;
}

esp_err_t i2s_channel_reconfig_std_gpio(i2s_chan_handle_t c, const i2s_std_gpio_config_t *gpio)
{
    if (!c || !gpio) return ESP_ERR_INVALID_ARG;
    if (c->ativo) return ESP_ERR_INVALID_STATE;
    c->bclk = gpio->bclk;
    c->ws = gpio->ws;
    c->dout = gpio->dout;
    c->din = gpio->din;
    return ESP_OK;
}

esp_err_t i2s_channel_enable(i2s_chan_handle_t c)
{
    if (!c) return ESP_ERR_INVALID_ARG;
    if (!c->iniciado || c->ativo) {
        ESP_LOGE("i2s_common", "the channel has already enabled or not initialized");
        return ESP_ERR_INVALID_STATE;
    }
    c->canal = cs->i2s_abrir(c->porta, c->taxa, c->bits, c->canais, c->bclk, c->ws, c->dout);
    CS_CONFERIR();
    c->ativo = 1;
    return ESP_OK;
}

esp_err_t i2s_channel_disable(i2s_chan_handle_t c)
{
    if (!c) return ESP_ERR_INVALID_ARG;
    if (!c->ativo) return ESP_ERR_INVALID_STATE;
    cs->i2s_fechar(c->canal);
    c->ativo = 0;
    return ESP_OK;
}

esp_err_t i2s_del_channel(i2s_chan_handle_t c)
{
    if (!c) return ESP_ERR_INVALID_ARG;
    if (c->ativo) return ESP_ERR_INVALID_STATE;
    portas_i2s[c->porta] = NULL;
    cs_liberar(c);
    return ESP_OK;
}

esp_err_t i2s_channel_get_info(i2s_chan_handle_t c, i2s_chan_info_t *info)
{
    if (!c || !info) return ESP_ERR_INVALID_ARG;
    memset(info, 0, sizeof(*info));
    info->dir = I2S_DIR_TX;
    info->role = I2S_ROLE_MASTER;
    info->mode = I2S_COMM_MODE_STD;
    info->total_dma_buf_size = 6 * 240 * 4;
    return ESP_OK;
}

esp_err_t i2s_channel_preload_data(i2s_chan_handle_t c, const void *dados, size_t n, size_t *carregados)
{
    (void)dados;
    if (!c) return ESP_ERR_INVALID_ARG;
    if (carregados) *carregados = n;
    return ESP_OK;
}

esp_err_t i2s_channel_write(i2s_chan_handle_t c, const void *dados, size_t n, size_t *escritos, uint32_t espera_ms)
{
    if (escritos) *escritos = 0;
    if (!c || !dados) return ESP_ERR_INVALID_ARG;
    if (!c->ativo) return ESP_ERR_INVALID_STATE;
    int64_t espera = espera_ms == portMAX_DELAY ? -1 : (int64_t)espera_ms * 1000;
    int r = cs->i2s_escrever(c->canal, dados, (int)n, espera);
    if (r < 0) cs_encerrar_tarefa();
    CS_CONFERIR();
    if (escritos) *escritos = (size_t)r;
    return (size_t)r < n ? ESP_ERR_TIMEOUT : ESP_OK;
}

esp_err_t i2s_channel_read(i2s_chan_handle_t c, void *dados, size_t n, size_t *lidos, uint32_t espera_ms)
{
    if (!c) return ESP_ERR_INVALID_ARG;
    memset(dados, 0, n);
    cs_dormir_us((int64_t)espera_ms * 1000 < 20000 ? (int64_t)espera_ms * 1000 : 20000);
    if (lidos) *lidos = n;
    return ESP_OK;
}

esp_err_t i2s_channel_register_event_callback(i2s_chan_handle_t c, const i2s_event_callbacks_t *cbs, void *ctx)
{
    (void)cbs; (void)ctx;
    return c ? ESP_OK : ESP_ERR_INVALID_ARG;
}

/* O driver antigo, por cima do novo. */
static struct i2s_channel_obj_t *legado[2];

esp_err_t i2s_driver_install(i2s_port_t porta, const i2s_config_t *config, int fila_tamanho, void *fila)
{
    (void)fila_tamanho; (void)fila;
    if (porta < 0 || porta > 1 || !config) return ESP_ERR_INVALID_ARG;
    if (legado[porta]) return ESP_ERR_INVALID_STATE;
    i2s_chan_config_t cc = I2S_CHANNEL_DEFAULT_CONFIG(porta, I2S_ROLE_MASTER);
    i2s_chan_handle_t tx;
    esp_err_t r = i2s_new_channel(&cc, &tx, NULL);
    if (r != ESP_OK) return r;
    tx->taxa = (int)config->sample_rate;
    tx->bits = (int)config->bits_per_sample;
    tx->canais = (config->channel_format == I2S_CHANNEL_FMT_ONLY_LEFT
                  || config->channel_format == I2S_CHANNEL_FMT_ONLY_RIGHT) ? 1 : 2;
#if defined(CONFIG_IDF_TARGET_ESP32)
    if (config->mode & I2S_MODE_DAC_BUILT_IN) {
        /* O DAC interno: a amostra sai nos pinos 25 e 26. */
        tx->bclk = tx->ws = -2;
        tx->dout = config->channel_format == I2S_CHANNEL_FMT_ONLY_RIGHT ? 25 : 26;
    }
#endif
    tx->iniciado = 1;
    legado[porta] = tx;
    return i2s_channel_enable(tx);
}

esp_err_t i2s_driver_uninstall(i2s_port_t porta)
{
    if (porta < 0 || porta > 1 || !legado[porta]) return ESP_ERR_INVALID_STATE;
    i2s_channel_disable(legado[porta]);
    i2s_del_channel(legado[porta]);
    legado[porta] = NULL;
    return ESP_OK;
}

esp_err_t i2s_set_pin(i2s_port_t porta, const i2s_pin_config_t *pinos_i2s)
{
    if (porta < 0 || porta > 1 || !legado[porta]) return ESP_ERR_INVALID_STATE;
    if (!pinos_i2s) return ESP_OK;
    struct i2s_channel_obj_t *c = legado[porta];
    cs->i2s_fechar(c->canal);
    if (pinos_i2s->bck_io_num != I2S_PIN_NO_CHANGE) c->bclk = pinos_i2s->bck_io_num;
    if (pinos_i2s->ws_io_num != I2S_PIN_NO_CHANGE) c->ws = pinos_i2s->ws_io_num;
    if (pinos_i2s->data_out_num != I2S_PIN_NO_CHANGE) c->dout = pinos_i2s->data_out_num;
    c->canal = cs->i2s_abrir(c->porta, c->taxa, c->bits, c->canais, c->bclk, c->ws, c->dout);
    CS_CONFERIR();
    return ESP_OK;
}

esp_err_t i2s_write(i2s_port_t porta, const void *dados, size_t n, size_t *escritos, TickType_t espera)
{
    if (porta < 0 || porta > 1 || !legado[porta]) return ESP_ERR_INVALID_STATE;
    return i2s_channel_write(legado[porta], dados, n, escritos, espera == portMAX_DELAY ? portMAX_DELAY : espera);
}

esp_err_t i2s_write_expand(i2s_port_t porta, const void *dados, size_t n, size_t bits_de, size_t bits_para,
                           size_t *escritos, TickType_t espera)
{
    (void)bits_de; (void)bits_para;
    return i2s_write(porta, dados, n, escritos, espera);
}

esp_err_t i2s_read(i2s_port_t porta, void *dados, size_t n, size_t *lidos, TickType_t espera)
{
    if (porta < 0 || porta > 1 || !legado[porta]) return ESP_ERR_INVALID_STATE;
    return i2s_channel_read(legado[porta], dados, n, lidos, espera);
}

esp_err_t i2s_set_sample_rates(i2s_port_t porta, uint32_t taxa)
{
    if (porta < 0 || porta > 1 || !legado[porta]) return ESP_ERR_INVALID_STATE;
    struct i2s_channel_obj_t *c = legado[porta];
    cs->i2s_fechar(c->canal);
    c->taxa = (int)taxa;
    c->canal = cs->i2s_abrir(c->porta, c->taxa, c->bits, c->canais, c->bclk, c->ws, c->dout);
    return ESP_OK;
}

esp_err_t i2s_set_clk(i2s_port_t porta, uint32_t taxa, uint32_t bits, i2s_channel_t canais)
{
    if (porta < 0 || porta > 1 || !legado[porta]) return ESP_ERR_INVALID_STATE;
    struct i2s_channel_obj_t *c = legado[porta];
    cs->i2s_fechar(c->canal);
    c->taxa = (int)taxa;
    c->bits = (int)(bits & 0xffff);
    c->canais = canais == I2S_CHANNEL_MONO ? 1 : 2;
    c->canal = cs->i2s_abrir(c->porta, c->taxa, c->bits, c->canais, c->bclk, c->ws, c->dout);
    return ESP_OK;
}

float i2s_get_clk(i2s_port_t porta)
{
    return (porta >= 0 && porta <= 1 && legado[porta]) ? (float)legado[porta]->taxa : 0.0f;
}

esp_err_t i2s_start(i2s_port_t porta) { return (porta >= 0 && porta <= 1 && legado[porta]) ? ESP_OK : ESP_ERR_INVALID_STATE; }
esp_err_t i2s_stop(i2s_port_t porta) { return (porta >= 0 && porta <= 1 && legado[porta]) ? ESP_OK : ESP_ERR_INVALID_STATE; }
esp_err_t i2s_zero_dma_buffer(i2s_port_t porta) { return (porta >= 0 && porta <= 1 && legado[porta]) ? ESP_OK : ESP_ERR_INVALID_STATE; }

#if defined(CONFIG_IDF_TARGET_ESP32)
esp_err_t i2s_set_dac_mode(i2s_dac_mode_t modo)
{
    (void)modo;
    return ESP_OK;
}
#endif

/* ---------------------------------------------------------------------
 * UART: a 0 e o monitor serial */
static int uart_instalada[3];
static uint32_t baud_da_uart[3] = { 115200, 115200, 115200 };

esp_err_t uart_driver_install(uart_port_t porta, int rx, int tx, int fila_tamanho, QueueHandle_t *fila, int flags)
{
    (void)tx; (void)fila_tamanho; (void)flags;
    if (porta < 0 || porta >= UART_NUM_MAX) return ESP_ERR_INVALID_ARG;
    if (rx <= UART_FIFO_LEN) {
        ESP_LOGE("uart", "uart rx buffer length error");
        return ESP_ERR_INVALID_ARG;
    }
    uart_instalada[porta] = 1;
    if (fila) *fila = NULL;
    return ESP_OK;
}

esp_err_t uart_driver_delete(uart_port_t porta)
{
    if (porta < 0 || porta >= UART_NUM_MAX) return ESP_ERR_INVALID_ARG;
    uart_instalada[porta] = 0;
    return ESP_OK;
}

bool uart_is_driver_installed(uart_port_t porta)
{
    return porta >= 0 && porta < UART_NUM_MAX && uart_instalada[porta];
}

esp_err_t uart_param_config(uart_port_t porta, const uart_config_t *config)
{
    if (porta < 0 || porta >= UART_NUM_MAX || !config) return ESP_ERR_INVALID_ARG;
    baud_da_uart[porta] = (uint32_t)config->baud_rate;
    return ESP_OK;
}

esp_err_t uart_set_pin(uart_port_t porta, int tx, int rx, int rts, int cts)
{
    (void)tx; (void)rx; (void)rts; (void)cts;
    return porta >= 0 && porta < UART_NUM_MAX ? ESP_OK : ESP_ERR_INVALID_ARG;
}

esp_err_t uart_set_baudrate(uart_port_t porta, uint32_t baud)
{
    if (porta < 0 || porta >= UART_NUM_MAX) return ESP_ERR_INVALID_ARG;
    baud_da_uart[porta] = baud;
    return ESP_OK;
}

esp_err_t uart_get_baudrate(uart_port_t porta, uint32_t *baud)
{
    if (porta < 0 || porta >= UART_NUM_MAX || !baud) return ESP_ERR_INVALID_ARG;
    *baud = baud_da_uart[porta];
    return ESP_OK;
}

int uart_read_bytes(uart_port_t porta, void *buf, uint32_t n, TickType_t espera)
{
    if (porta < 0 || porta >= UART_NUM_MAX) return -1;
    if (!uart_instalada[porta]) {
        ESP_LOGE("uart", "uart driver error");
        return -1;
    }
    if (porta != UART_NUM_0) {
        if (espera) cs_dormir_us(cs_espera_us(espera) < 0 ? 100000 : cs_espera_us(espera));
        return 0;
    }
    uint8_t *d = (uint8_t *)buf;
    uint32_t k = 0;
    int64_t limite = espera == portMAX_DELAY ? -1 : cs_micros() + cs_espera_us(espera);
    while (k < n) {
        int c = cs->ler_serial();
        CS_CONFERIR();
        if (c >= 0) {
            d[k++] = (uint8_t)c;
            continue;
        }
        if (limite >= 0 && cs_micros() >= limite) break;
        cs_dormir_us(1000);
    }
    return (int)k;
}

int uart_write_bytes(uart_port_t porta, const void *dados, size_t n)
{
    if (porta < 0 || porta >= UART_NUM_MAX) return -1;
    if (porta == UART_NUM_0) {
        cs->escrever((const char *)dados, (int)n);
        CS_CONFERIR();
    }
    return (int)n;
}

int uart_tx_chars(uart_port_t porta, const char *dados, uint32_t n)
{
    return uart_write_bytes(porta, dados, n);
}

esp_err_t uart_wait_tx_done(uart_port_t porta, TickType_t espera) { (void)espera; return porta >= 0 && porta < UART_NUM_MAX ? ESP_OK : ESP_ERR_INVALID_ARG; }
esp_err_t uart_flush(uart_port_t porta) { return uart_flush_input(porta); }

esp_err_t uart_flush_input(uart_port_t porta)
{
    if (porta < 0 || porta >= UART_NUM_MAX) return ESP_ERR_INVALID_ARG;
    if (porta == UART_NUM_0)
        while (cs->ler_serial() >= 0) {}
    return ESP_OK;
}

esp_err_t uart_get_buffered_data_len(uart_port_t porta, size_t *n)
{
    if (porta < 0 || porta >= UART_NUM_MAX || !n) return ESP_ERR_INVALID_ARG;
    *n = porta == UART_NUM_0 ? (size_t)cs->serial_disponivel() : 0;
    return ESP_OK;
}

esp_err_t uart_set_mode(uart_port_t porta, uart_mode_t modo) { (void)modo; return porta >= 0 && porta < UART_NUM_MAX ? ESP_OK : ESP_ERR_INVALID_ARG; }

/* ---------------------------------------------------------------------
 * NVS: guardado no simulador, por placa; sobrevive ao reinicio */
#define ESPACOS 16
static struct { char nome[16]; int so_leitura; int usado; } espacos[ESPACOS];
static int nvs_pronta;

esp_err_t nvs_flash_init(void) { nvs_pronta = 1; return ESP_OK; }
esp_err_t nvs_flash_init_partition(const char *particao) { (void)particao; nvs_pronta = 1; return ESP_OK; }
esp_err_t nvs_flash_deinit(void) { nvs_pronta = 0; return ESP_OK; }

esp_err_t nvs_flash_erase(void)
{
    cs->nvs_apagar(NULL, NULL);
    return ESP_OK;
}

esp_err_t nvs_flash_erase_partition(const char *particao) { (void)particao; return nvs_flash_erase(); }

esp_err_t nvs_open(const char *espaco, nvs_open_mode_t modo, nvs_handle_t *alca)
{
    if (!nvs_pronta) return 0x1101;   /* ESP_ERR_NVS_NOT_INITIALIZED */
    if (!espaco || strlen(espaco) >= NVS_NS_NAME_MAX_SIZE) return 0x1108;   /* ESP_ERR_NVS_INVALID_NAME */
    for (int i = 0; i < ESPACOS; i++)
        if (!espacos[i].usado) {
            espacos[i].usado = 1;
            strcpy(espacos[i].nome, espaco);
            espacos[i].so_leitura = modo == NVS_READONLY;
            *alca = (nvs_handle_t)(i + 1);
            return ESP_OK;
        }
    return ESP_ERR_NO_MEM;
}

esp_err_t nvs_open_from_partition(const char *particao, const char *espaco, nvs_open_mode_t modo, nvs_handle_t *alca)
{
    (void)particao;
    return nvs_open(espaco, modo, alca);
}

static int espaco_de(nvs_handle_t alca)
{
    int i = (int)alca - 1;
    return (i >= 0 && i < ESPACOS && espacos[i].usado) ? i : -1;
}

void nvs_close(nvs_handle_t alca)
{
    int i = espaco_de(alca);
    if (i >= 0) espacos[i].usado = 0;
}

esp_err_t nvs_commit(nvs_handle_t alca) { return espaco_de(alca) >= 0 ? ESP_OK : 0x1107; }

esp_err_t nvs_erase_key(nvs_handle_t alca, const char *chave)
{
    int i = espaco_de(alca);
    if (i < 0) return 0x1107;
    if (espacos[i].so_leitura) return 0x1107;
    return cs->nvs_apagar(espacos[i].nome, chave) == 0 ? ESP_OK : ESP_ERR_NVS_NOT_FOUND;
}

esp_err_t nvs_erase_all(nvs_handle_t alca)
{
    int i = espaco_de(alca);
    if (i < 0 || espacos[i].so_leitura) return 0x1107;
    cs->nvs_apagar(espacos[i].nome, NULL);
    return ESP_OK;
}

static esp_err_t gravar(nvs_handle_t alca, const char *chave, int tipo, const void *dados, int n)
{
    int i = espaco_de(alca);
    if (i < 0) return 0x1107;
    if (espacos[i].so_leitura) return 0x1107;   /* ESP_ERR_NVS_READ_ONLY */
    if (!chave || strlen(chave) >= NVS_KEY_NAME_MAX_SIZE) return 0x1109;   /* ESP_ERR_NVS_KEY_TOO_LONG */
    cs->nvs_gravar(espacos[i].nome, chave, tipo, dados, n);
    return ESP_OK;
}

static esp_err_t ler(nvs_handle_t alca, const char *chave, int tipo, void *dados, int n)
{
    int i = espaco_de(alca);
    if (i < 0) return 0x1107;
    int achado = 0;
    int r = cs->nvs_ler(espacos[i].nome, chave, &achado, dados, n);
    if (r < 0) return ESP_ERR_NVS_NOT_FOUND;
    if (achado != tipo) return 0x1105;   /* ESP_ERR_NVS_TYPE_MISMATCH */
    return ESP_OK;
}

esp_err_t nvs_set_i8(nvs_handle_t a, const char *k, int8_t v) { return gravar(a, k, NVS_TYPE_I8, &v, 1); }
esp_err_t nvs_set_u8(nvs_handle_t a, const char *k, uint8_t v) { return gravar(a, k, NVS_TYPE_U8, &v, 1); }
esp_err_t nvs_set_i16(nvs_handle_t a, const char *k, int16_t v) { return gravar(a, k, NVS_TYPE_I16, &v, 2); }
esp_err_t nvs_set_u16(nvs_handle_t a, const char *k, uint16_t v) { return gravar(a, k, NVS_TYPE_U16, &v, 2); }
esp_err_t nvs_set_i32(nvs_handle_t a, const char *k, int32_t v) { return gravar(a, k, NVS_TYPE_I32, &v, 4); }
esp_err_t nvs_set_u32(nvs_handle_t a, const char *k, uint32_t v) { return gravar(a, k, NVS_TYPE_U32, &v, 4); }
esp_err_t nvs_set_i64(nvs_handle_t a, const char *k, int64_t v) { return gravar(a, k, NVS_TYPE_I64, &v, 8); }
esp_err_t nvs_set_u64(nvs_handle_t a, const char *k, uint64_t v) { return gravar(a, k, NVS_TYPE_U64, &v, 8); }
esp_err_t nvs_set_str(nvs_handle_t a, const char *k, const char *v) { return gravar(a, k, NVS_TYPE_STR, v, (int)strlen(v) + 1); }
esp_err_t nvs_set_blob(nvs_handle_t a, const char *k, const void *v, size_t n) { return gravar(a, k, NVS_TYPE_BLOB, v, (int)n); }

esp_err_t nvs_get_i8(nvs_handle_t a, const char *k, int8_t *v) { return ler(a, k, NVS_TYPE_I8, v, 1); }
esp_err_t nvs_get_u8(nvs_handle_t a, const char *k, uint8_t *v) { return ler(a, k, NVS_TYPE_U8, v, 1); }
esp_err_t nvs_get_i16(nvs_handle_t a, const char *k, int16_t *v) { return ler(a, k, NVS_TYPE_I16, v, 2); }
esp_err_t nvs_get_u16(nvs_handle_t a, const char *k, uint16_t *v) { return ler(a, k, NVS_TYPE_U16, v, 2); }
esp_err_t nvs_get_i32(nvs_handle_t a, const char *k, int32_t *v) { return ler(a, k, NVS_TYPE_I32, v, 4); }
esp_err_t nvs_get_u32(nvs_handle_t a, const char *k, uint32_t *v) { return ler(a, k, NVS_TYPE_U32, v, 4); }
esp_err_t nvs_get_i64(nvs_handle_t a, const char *k, int64_t *v) { return ler(a, k, NVS_TYPE_I64, v, 8); }
esp_err_t nvs_get_u64(nvs_handle_t a, const char *k, uint64_t *v) { return ler(a, k, NVS_TYPE_U64, v, 8); }

static esp_err_t ler_tamanho_variavel(nvs_handle_t alca, const char *chave, int tipo, void *dados, size_t *n)
{
    int i = espaco_de(alca);
    if (i < 0) return 0x1107;
    int achado = 0;
    int tamanho = cs->nvs_ler(espacos[i].nome, chave, &achado, NULL, 0);
    if (tamanho < 0) return ESP_ERR_NVS_NOT_FOUND;
    if (achado != tipo) return 0x1105;
    if (!dados) {
        *n = (size_t)tamanho;
        return ESP_OK;
    }
    if (*n < (size_t)tamanho) {
        *n = (size_t)tamanho;
        return 0x110c;   /* ESP_ERR_NVS_INVALID_LENGTH */
    }
    cs->nvs_ler(espacos[i].nome, chave, &achado, dados, tamanho);
    *n = (size_t)tamanho;
    return ESP_OK;
}

esp_err_t nvs_get_str(nvs_handle_t a, const char *k, char *v, size_t *n) { return ler_tamanho_variavel(a, k, NVS_TYPE_STR, v, n); }
esp_err_t nvs_get_blob(nvs_handle_t a, const char *k, void *v, size_t *n) { return ler_tamanho_variavel(a, k, NVS_TYPE_BLOB, v, n); }
