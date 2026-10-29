/* cs_idf.cpp -- os drivers do ESP-IDF sobre o core (ver cs_idf.h). */
#include "Arduino.h"
#if defined(ESP32)
#include "cs_idf.h"
/* O ADC antigo (driver/adc.h, que o IDF 5 ainda tem) e o I2S antigo. */
#include "driver/adc.h"
#include "cs_interno.h"
#include <stdarg.h>
#include <map>
#include <string>

/* ---- os erros ---- */
extern "C" const char *esp_err_to_name(esp_err_t code)
{
    switch (code) {
    case ESP_OK: return "ESP_OK";
    case ESP_FAIL: return "ESP_FAIL";
    case ESP_ERR_NO_MEM: return "ESP_ERR_NO_MEM";
    case ESP_ERR_INVALID_ARG: return "ESP_ERR_INVALID_ARG";
    case ESP_ERR_INVALID_STATE: return "ESP_ERR_INVALID_STATE";
    case ESP_ERR_INVALID_SIZE: return "ESP_ERR_INVALID_SIZE";
    case ESP_ERR_NOT_FOUND: return "ESP_ERR_NOT_FOUND";
    case ESP_ERR_NOT_SUPPORTED: return "ESP_ERR_NOT_SUPPORTED";
    case ESP_ERR_TIMEOUT: return "ESP_ERR_TIMEOUT";
    default: return "ERROR";
    }
}

/* O ESP_ERROR_CHECK que falha: a mensagem do esp_err.c do IDF e o abort (na
 * placa, ela reinicia; aqui, o programa para com a mensagem). */
static void mensagemDoCheck(esp_err_t r, const char *arquivo, int linha, const char *funcao, const char *expr)
{
    printf("ESP_ERROR_CHECK failed: esp_err_t 0x%x (%s) at %p\nfile: \"%s\" line %d\nfunc: %s\nexpression: %s\n",
           (unsigned)r, esp_err_to_name(r), __builtin_return_address(0), arquivo, linha, funcao, expr);
}
extern "C" void _esp_error_check_failed(esp_err_t r, const char *arquivo, int linha, const char *funcao, const char *expr)
{
    mensagemDoCheck(r, arquivo, linha, funcao, expr);
    abort();
}
extern "C" void _esp_error_check_failed_without_abort(esp_err_t r, const char *arquivo, int linha, const char *funcao,
                                                      const char *expr)
{
    mensagemDoCheck(r, arquivo, linha, funcao, expr);
}

/* ---- o log (esp_log.h): "I (1234) tag: mensagem" ----
 * O nivel e por tag, como no IDF ("*" vale para as que nao tem o seu). A
 * mensagem chega ja formatada pela macro (o LOG_FORMAT com a letra, o tempo e
 * a tag); o que sai vai pelo vprintf, que o programa pode trocar. */
static esp_log_level_t nivelPadrao = ESP_LOG_INFO;
static esp_log_level_t nivelMestre = ESP_LOG_VERBOSE;
static std::map<std::string, esp_log_level_t> *niveisPorTag;
static vprintf_like_t saidaDoLog = vprintf;

extern "C" void esp_log_level_set(const char *tag, esp_log_level_t level)
{
    if (!tag || strcmp(tag, "*") == 0) {
        nivelPadrao = level;
        if (niveisPorTag) niveisPorTag->clear();
        return;
    }
    if (!niveisPorTag) niveisPorTag = new std::map<std::string, esp_log_level_t>();
    (*niveisPorTag)[tag] = level;
}
extern "C" esp_log_level_t esp_log_level_get(const char *tag)
{
    if (tag && niveisPorTag) {
        auto e = niveisPorTag->find(tag);
        if (e != niveisPorTag->end()) return e->second;
    }
    return nivelPadrao;
}
extern "C" void esp_log_set_level_master(esp_log_level_t level) { nivelMestre = level; }
extern "C" esp_log_level_t esp_log_get_level_master(void) { return nivelMestre; }
extern "C" vprintf_like_t esp_log_set_vprintf(vprintf_like_t func)
{
    vprintf_like_t antes = saidaDoLog;
    saidaDoLog = func ? func : vprintf;
    return antes;
}
extern "C" uint32_t esp_log_timestamp(void) { return (uint32_t)millis(); }
extern "C" uint32_t esp_log_early_timestamp(void) { return (uint32_t)millis(); }
extern "C" char *esp_log_system_timestamp(void)
{
    static char t[24];
    unsigned long ms = millis();
    snprintf(t, sizeof(t), "%02lu:%02lu:%02lu.%03lu", ms / 3600000 % 24, ms / 60000 % 60, ms / 1000 % 60, ms % 1000);
    return t;
}

static bool deixa(esp_log_level_t nivel, const char *tag)
{
    return nivel != ESP_LOG_NONE && nivel <= nivelMestre && nivel <= esp_log_level_get(tag);
}

extern "C" void esp_log_writev(esp_log_level_t nivel, const char *tag, const char *formato, va_list a)
{
    if (deixa(nivel, tag)) saidaDoLog(formato, a);
}
extern "C" void esp_log_write(esp_log_level_t nivel, const char *tag, const char *formato, ...)
{
    va_list a;
    va_start(a, formato);
    esp_log_writev(nivel, tag, formato, a);
    va_end(a);
}
/* O log da versao 2 (esp_log): o formato vem cru, e a letra, o tempo e a tag
 * sao postos aqui. */
extern "C" void esp_log_va(esp_log_config_t config, const char *tag, const char *formato, va_list a)
{
    esp_log_level_t nivel = (esp_log_level_t)(config.data & ESP_LOG_CONFIG_LEVEL_MASK);
    if (!deixa(nivel, tag)) return;
    if (config.opts.require_formatting) {
        static const char letras[] = "NEWIDV";
        printf("%c (%lu) %s: ", letras[nivel < 6 ? nivel : 0], (unsigned long)millis(), tag ? tag : "");
    }
    saidaDoLog(formato, a);
    if (config.opts.require_formatting) printf("\n");
}
extern "C" void esp_log(esp_log_config_t config, const char *tag, const char *formato, ...)
{
    va_list a;
    va_start(a, formato);
    esp_log_va(config, tag, formato, a);
    va_end(a);
}

/* Os ESP_LOG_BUFFER_HEX / _CHAR / _HEXDUMP. */
static void linhaDoLog(esp_log_level_t nivel, const char *tag, const char *texto)
{
    static const char letras[] = "NEWIDV";
    esp_log_write(nivel, tag, "%c (%lu) %s: %s\n", letras[nivel < 6 ? nivel : 0], (unsigned long)millis(), tag ? tag : "", texto);
}
extern "C" void esp_log_buffer_hex_internal(const char *tag, const void *buffer, uint16_t n, esp_log_level_t nivel)
{
    const uint8_t *b = (const uint8_t *)buffer;
    for (uint16_t i = 0; i < n; i += 16) {
        char linha[16 * 3 + 1] = {0};
        for (uint16_t k = 0; k < 16 && i + k < n; k++) sprintf(linha + k * 3, "%02x ", b[i + k]);
        linhaDoLog(nivel, tag, linha);
    }
}
extern "C" void esp_log_buffer_char_internal(const char *tag, const void *buffer, uint16_t n, esp_log_level_t nivel)
{
    const char *b = (const char *)buffer;
    for (uint16_t i = 0; i < n; i += 16) {
        char linha[17] = {0};
        memcpy(linha, b + i, n - i < 16 ? n - i : 16);
        linhaDoLog(nivel, tag, linha);
    }
}
extern "C" void esp_log_buffer_hexdump_internal(const char *tag, const void *buffer, uint16_t n, esp_log_level_t nivel)
{
    const uint8_t *b = (const uint8_t *)buffer;
    for (uint16_t i = 0; i < n; i += 16) {
        char linha[100];
        int k = snprintf(linha, sizeof(linha), "%p  ", (void *)(b + i));
        for (int j = 0; j < 16; j++)
            k += i + j < n ? snprintf(linha + k, sizeof(linha) - k, "%02x %s", b[i + j], j == 7 ? " " : "")
                           : snprintf(linha + k, sizeof(linha) - k, "   %s", j == 7 ? " " : "");
        k += snprintf(linha + k, sizeof(linha) - k, " |");
        for (int j = 0; j < 16 && i + j < n; j++) linha[k++] = isprint(b[i + j]) ? (char)b[i + j] : '.';
        linha[k++] = '|';
        linha[k] = 0;
        linhaDoLog(nivel, tag, linha);
    }
}

/* ---- o tempo ---- */
extern "C" int64_t esp_timer_get_time(void) { return cs_relogio(); }
extern "C" void ets_delay_us(uint32_t us) { delayMicroseconds(us); }
extern "C" void esp_rom_delay_us(uint32_t us) { delayMicroseconds(us); }

/* O esp_timer e um temporizador do FreeRTOS (a tarefa "esp_timer" da placa
 * tambem chama os callbacks fora da interrupcao). */
struct esp_timer {
    esp_timer_cb_t cb;
    void *arg;
    char nome[16];
    TimerHandle_t t;
};

static void disparar(TimerHandle_t t)
{
    auto *e = (esp_timer *)pvTimerGetTimerID(t);
    if (e && e->cb) e->cb(e->arg);
}

extern "C" esp_err_t esp_timer_init(void) { return ESP_OK; }

extern "C" esp_err_t esp_timer_create(const esp_timer_create_args_t *args, esp_timer_handle_t *saida)
{
    if (!args || !saida || !args->callback) return ESP_ERR_INVALID_ARG;
    auto *e = new esp_timer();
    e->cb = args->callback;
    e->arg = args->arg;
    snprintf(e->nome, sizeof(e->nome), "%s", args->name ? args->name : "esp_timer");
    e->t = xTimerCreate(e->nome, 1, pdFALSE, e, disparar);
    *saida = e;
    return ESP_OK;
}

static esp_err_t ligar(esp_timer_handle_t e, uint64_t us, bool repete)
{
    if (!e) return ESP_ERR_INVALID_ARG;
    TickType_t ticks = (TickType_t)(us / 1000);
    vTimerSetReloadMode(e->t, repete ? pdTRUE : pdFALSE);
    xTimerChangePeriod(e->t, ticks ? ticks : 1, 0);
    return ESP_OK;
}

extern "C" esp_err_t esp_timer_start_once(esp_timer_handle_t e, uint64_t us) { return ligar(e, us, false); }
extern "C" esp_err_t esp_timer_start_periodic(esp_timer_handle_t e, uint64_t us) { return ligar(e, us, true); }
extern "C" esp_err_t esp_timer_restart(esp_timer_handle_t e, uint64_t us)
{
    return ligar(e, us, e && uxTimerGetReloadMode(e->t));
}
extern "C" esp_err_t esp_timer_stop(esp_timer_handle_t e)
{
    if (!e) return ESP_ERR_INVALID_ARG;
    xTimerStop(e->t, 0);
    return ESP_OK;
}
extern "C" esp_err_t esp_timer_delete(esp_timer_handle_t e)
{
    if (!e) return ESP_ERR_INVALID_ARG;
    xTimerDelete(e->t, 0);
    return ESP_OK;
}
extern "C" bool esp_timer_is_active(esp_timer_handle_t e) { return e && xTimerIsTimerActive(e->t); }

/* ---- o sistema ---- */
extern "C" uint32_t esp_get_free_heap_size(void) { return ESP.getFreeHeap(); }
extern "C" uint32_t esp_get_minimum_free_heap_size(void) { return ESP.getMinFreeHeap(); }
extern "C" uint32_t esp_random(void) { return (uint32_t)random(0x7FFFFFFF) ^ ((uint32_t)random(0xFFFF) << 16); }
extern "C" void esp_fill_random(void *buf, size_t n)
{
    for (size_t i = 0; i < n; i++) ((uint8_t *)buf)[i] = (uint8_t)random(256);
}
extern "C" void esp_chip_info(esp_chip_info_t *info)
{
    info->model = CHIP_ESP32;
    info->features = CHIP_FEATURE_WIFI_BGN | CHIP_FEATURE_BLE | CHIP_FEATURE_BT;
    info->revision = 301;
    info->cores = 2;
}
extern "C" esp_err_t esp_flash_get_size(esp_flash_t *, uint32_t *tamanho)
{
    *tamanho = 4u << 20;
    return ESP_OK;
}
extern "C" const char *esp_get_idf_version(void) { return "v5.1.4"; }

/* ---- os pinos ---- */
static uint8_t puxar[64];           /* 0 nada, 1 pull-up, 2 pull-down */
static gpio_int_type_t borda[64];
static gpio_isr_t rotina[64];
static void *argumento[64];

static void aplicarModo(int pino, gpio_mode_t modo)
{
    if (modo & GPIO_MODE_OUTPUT) pinMode(pino, OUTPUT);
    else if (modo & GPIO_MODE_INPUT) pinMode(pino, puxar[pino] == 1 ? INPUT_PULLUP : puxar[pino] == 2 ? INPUT_PULLDOWN : INPUT);
}

static int modoDaBorda(gpio_int_type_t t)
{
    switch (t) {
    case GPIO_INTR_POSEDGE: return RISING;
    case GPIO_INTR_NEGEDGE: return FALLING;
#ifdef ONLOW
    case GPIO_INTR_LOW_LEVEL: return ONLOW;
    case GPIO_INTR_HIGH_LEVEL: return ONHIGH;
#else
    /* O simulador nao tem a interrupcao por nivel: a borda que leva a ele. */
    case GPIO_INTR_LOW_LEVEL: return FALLING;
    case GPIO_INTR_HIGH_LEVEL: return RISING;
#endif
    default: return CHANGE;
    }
}

static void ligarRotina(int pino)
{
    if (rotina[pino] && borda[pino] != GPIO_INTR_DISABLE)
        attachInterruptArg(pino, rotina[pino], argumento[pino], modoDaBorda(borda[pino]));
}

extern "C" esp_err_t gpio_config(const gpio_config_t *cfg)
{
    if (!cfg) return ESP_ERR_INVALID_ARG;
    for (int p = 0; p < 64; p++) {
        if (!(cfg->pin_bit_mask & (1ULL << p))) continue;
        puxar[p] = cfg->pull_up_en ? 1 : cfg->pull_down_en ? 2 : 0;
        borda[p] = cfg->intr_type;
        aplicarModo(p, cfg->mode);
        ligarRotina(p);
    }
    return ESP_OK;
}

extern "C" esp_err_t gpio_reset_pin(gpio_num_t p)
{
    if (p < 0 || p >= 64) return ESP_ERR_INVALID_ARG;
    puxar[p] = 1;      /* o reset deixa o pino como entrada com pull-up */
    pinMode(p, INPUT_PULLUP);
    return ESP_OK;
}

extern "C" esp_err_t gpio_set_direction(gpio_num_t p, gpio_mode_t modo)
{
    if (p < 0 || p >= 64) return ESP_ERR_INVALID_ARG;
    aplicarModo(p, modo);
    return ESP_OK;
}

extern "C" esp_err_t gpio_set_level(gpio_num_t p, uint32_t nivel)
{
    if (p < 0 || p >= 64) return ESP_ERR_INVALID_ARG;
    digitalWrite(p, nivel ? HIGH : LOW);
    return ESP_OK;
}

extern "C" int gpio_get_level(gpio_num_t p) { return p < 0 || p >= 64 ? 0 : digitalRead(p); }

extern "C" esp_err_t gpio_set_pull_mode(gpio_num_t p, gpio_pull_mode_t modo)
{
    if (p < 0 || p >= 64) return ESP_ERR_INVALID_ARG;
    puxar[p] = modo == GPIO_PULLUP_ONLY ? 1 : modo == GPIO_PULLDOWN_ONLY ? 2 : 0;
    pinMode(p, puxar[p] == 1 ? INPUT_PULLUP : puxar[p] == 2 ? INPUT_PULLDOWN : INPUT);
    return ESP_OK;
}
extern "C" esp_err_t gpio_pullup_en(gpio_num_t p) { return gpio_set_pull_mode(p, GPIO_PULLUP_ONLY); }
extern "C" esp_err_t gpio_pullup_dis(gpio_num_t p) { return gpio_set_pull_mode(p, GPIO_FLOATING); }
extern "C" esp_err_t gpio_pulldown_en(gpio_num_t p) { return gpio_set_pull_mode(p, GPIO_PULLDOWN_ONLY); }
extern "C" esp_err_t gpio_pulldown_dis(gpio_num_t p) { return gpio_set_pull_mode(p, GPIO_FLOATING); }

extern "C" esp_err_t gpio_set_intr_type(gpio_num_t p, gpio_int_type_t tipo)
{
    if (p < 0 || p >= 64) return ESP_ERR_INVALID_ARG;
    borda[p] = tipo;
    if (tipo == GPIO_INTR_DISABLE) detachInterrupt(p);
    else ligarRotina(p);
    return ESP_OK;
}
extern "C" esp_err_t gpio_intr_enable(gpio_num_t p)
{
    ligarRotina(p);
    return ESP_OK;
}
extern "C" esp_err_t gpio_intr_disable(gpio_num_t p)
{
    detachInterrupt(p);
    return ESP_OK;
}
extern "C" esp_err_t gpio_install_isr_service(int) { return ESP_OK; }
extern "C" void gpio_uninstall_isr_service(void) {}

extern "C" esp_err_t gpio_isr_handler_add(gpio_num_t p, gpio_isr_t fn, void *arg)
{
    if (p < 0 || p >= 64) return ESP_ERR_INVALID_ARG;
    rotina[p] = fn;
    argumento[p] = arg;
    ligarRotina(p);
    return ESP_OK;
}

extern "C" esp_err_t gpio_isr_handler_remove(gpio_num_t p)
{
    if (p < 0 || p >= 64) return ESP_ERR_INVALID_ARG;
    rotina[p] = nullptr;
    detachInterrupt(p);
    return ESP_OK;
}

extern "C" esp_err_t gpio_set_drive_capability(gpio_num_t, gpio_drive_cap_t) { return ESP_OK; }
extern "C" void esp_rom_gpio_pad_select_gpio(uint32_t) {}

/* ---- o PWM: cada canal e o ledc do core no pino dele ---- */
static uint32_t frequencia[LEDC_TIMER_MAX] = {5000, 5000, 5000, 5000};
static uint8_t resolucao[LEDC_TIMER_MAX] = {8, 8, 8, 8};
static int pinoDoCanal[2][LEDC_CHANNEL_MAX];
static uint32_t ciclo[2][LEDC_CHANNEL_MAX];
static ledc_timer_t timerDoCanal[2][LEDC_CHANNEL_MAX];
static bool canaisIniciados;

static void iniciarCanais(void)
{
    if (canaisIniciados) return;
    canaisIniciados = true;
    for (int m = 0; m < 2; m++)
        for (int c = 0; c < LEDC_CHANNEL_MAX; c++) pinoDoCanal[m][c] = -1;
}

extern "C" esp_err_t ledc_timer_config(const ledc_timer_config_t *t)
{
    if (!t || t->timer_num >= LEDC_TIMER_MAX) return ESP_ERR_INVALID_ARG;
    frequencia[t->timer_num] = t->freq_hz;
    resolucao[t->timer_num] = (uint8_t)t->duty_resolution;
    return ESP_OK;
}

extern "C" esp_err_t ledc_channel_config(const ledc_channel_config_t *c)
{
    if (!c || c->channel >= LEDC_CHANNEL_MAX || c->speed_mode > 1) return ESP_ERR_INVALID_ARG;
    iniciarCanais();
    int m = c->speed_mode;
    pinoDoCanal[m][c->channel] = c->gpio_num;
    timerDoCanal[m][c->channel] = c->timer_sel;
    ciclo[m][c->channel] = c->duty;
    ledcAttach((uint8_t)c->gpio_num, frequencia[c->timer_sel], resolucao[c->timer_sel]);
    ledcWrite((uint8_t)c->gpio_num, c->duty);
    return ESP_OK;
}

extern "C" esp_err_t ledc_set_duty(ledc_mode_t m, ledc_channel_t c, uint32_t duty)
{
    if (c >= LEDC_CHANNEL_MAX || m > 1) return ESP_ERR_INVALID_ARG;
    ciclo[m][c] = duty;
    return ESP_OK;
}

extern "C" esp_err_t ledc_update_duty(ledc_mode_t m, ledc_channel_t c)
{
    iniciarCanais();
    if (c >= LEDC_CHANNEL_MAX || m > 1 || pinoDoCanal[m][c] < 0) return ESP_ERR_INVALID_STATE;
    ledcWrite((uint8_t)pinoDoCanal[m][c], ciclo[m][c]);
    return ESP_OK;
}

extern "C" esp_err_t ledc_set_duty_and_update(ledc_mode_t m, ledc_channel_t c, uint32_t duty, uint32_t)
{
    ledc_set_duty(m, c, duty);
    return ledc_update_duty(m, c);
}

extern "C" uint32_t ledc_get_duty(ledc_mode_t m, ledc_channel_t c) { return c < LEDC_CHANNEL_MAX && m <= 1 ? ciclo[m][c] : 0; }

extern "C" esp_err_t ledc_set_freq(ledc_mode_t m, ledc_timer_t t, uint32_t f)
{
    if (t >= LEDC_TIMER_MAX) return ESP_ERR_INVALID_ARG;
    frequencia[t] = f;
    iniciarCanais();
    for (int c = 0; c < LEDC_CHANNEL_MAX; c++)
        if (pinoDoCanal[m][c] >= 0 && timerDoCanal[m][c] == t) {
            ledcAttach((uint8_t)pinoDoCanal[m][c], f, resolucao[t]);
            ledcWrite((uint8_t)pinoDoCanal[m][c], ciclo[m][c]);
        }
    return ESP_OK;
}

extern "C" uint32_t ledc_get_freq(ledc_mode_t, ledc_timer_t t) { return t < LEDC_TIMER_MAX ? frequencia[t] : 0; }

extern "C" esp_err_t ledc_stop(ledc_mode_t m, ledc_channel_t c, uint32_t nivel)
{
    iniciarCanais();
    if (c >= LEDC_CHANNEL_MAX || m > 1 || pinoDoCanal[m][c] < 0) return ESP_ERR_INVALID_STATE;
    ledcWrite((uint8_t)pinoDoCanal[m][c], nivel ? (1u << resolucao[timerDoCanal[m][c]]) - 1 : 0);
    return ESP_OK;
}

/* O fade: o ciclo vai ate o alvo em passos, numa tarefa so dele. */
struct Fade {
    ledc_mode_t m;
    ledc_channel_t c;
    uint32_t alvo;
    int ms;
};
static Fade fadePedido[2][LEDC_CHANNEL_MAX];

static void fazerFade(void *p)
{
    Fade f = *(Fade *)p;
    delete (Fade *)p;
    uint32_t de = ciclo[f.m][f.c];
    int passos = f.ms / 10 > 0 ? f.ms / 10 : 1;
    for (int i = 1; i <= passos; i++) {
        ciclo[f.m][f.c] = de + (uint32_t)(((int64_t)f.alvo - (int64_t)de) * i / passos);
        ledc_update_duty(f.m, f.c);
        vTaskDelay(10);
    }
    vTaskDelete(nullptr);
}

extern "C" esp_err_t ledc_fade_func_install(int) { return ESP_OK; }
extern "C" esp_err_t ledc_set_fade_with_time(ledc_mode_t m, ledc_channel_t c, uint32_t alvo, int ms)
{
    if (c >= LEDC_CHANNEL_MAX || m > 1) return ESP_ERR_INVALID_ARG;
    fadePedido[m][c] = {m, c, alvo, ms};
    return ESP_OK;
}
extern "C" esp_err_t ledc_fade_start(ledc_mode_t m, ledc_channel_t c, ledc_fade_mode_t modo)
{
    if (c >= LEDC_CHANNEL_MAX || m > 1) return ESP_ERR_INVALID_ARG;
    Fade f = fadePedido[m][c];
    if (modo == LEDC_FADE_WAIT_DONE) {
        fazerFade(new Fade(f));
        return ESP_OK;
    }
    xTaskCreate(fazerFade, "ledc_fade", 2048, new Fade(f), 1, nullptr);
    return ESP_OK;
}

/* ---- o ADC: o canal e o GPIO dele, e a leitura e a do analogRead ---- */
/* O pino de cada canal: a tabela do proprio chip (soc/adc_channel.h do IDF). */
#include "soc/adc_channel.h"
static const int8_t gpioDoAdc1[10] = {
#ifdef ADC1_CHANNEL_0_GPIO_NUM
    ADC1_CHANNEL_0_GPIO_NUM,
#else
    -1,
#endif
#ifdef ADC1_CHANNEL_1_GPIO_NUM
    ADC1_CHANNEL_1_GPIO_NUM,
#else
    -1,
#endif
#ifdef ADC1_CHANNEL_2_GPIO_NUM
    ADC1_CHANNEL_2_GPIO_NUM,
#else
    -1,
#endif
#ifdef ADC1_CHANNEL_3_GPIO_NUM
    ADC1_CHANNEL_3_GPIO_NUM,
#else
    -1,
#endif
#ifdef ADC1_CHANNEL_4_GPIO_NUM
    ADC1_CHANNEL_4_GPIO_NUM,
#else
    -1,
#endif
#ifdef ADC1_CHANNEL_5_GPIO_NUM
    ADC1_CHANNEL_5_GPIO_NUM,
#else
    -1,
#endif
#ifdef ADC1_CHANNEL_6_GPIO_NUM
    ADC1_CHANNEL_6_GPIO_NUM,
#else
    -1,
#endif
#ifdef ADC1_CHANNEL_7_GPIO_NUM
    ADC1_CHANNEL_7_GPIO_NUM,
#else
    -1,
#endif
#ifdef ADC1_CHANNEL_8_GPIO_NUM
    ADC1_CHANNEL_8_GPIO_NUM,
#else
    -1,
#endif
#ifdef ADC1_CHANNEL_9_GPIO_NUM
    ADC1_CHANNEL_9_GPIO_NUM,
#else
    -1,
#endif
};
static const int8_t gpioDoAdc2[10] = {
#ifdef ADC2_CHANNEL_0_GPIO_NUM
    ADC2_CHANNEL_0_GPIO_NUM,
#else
    -1,
#endif
#ifdef ADC2_CHANNEL_1_GPIO_NUM
    ADC2_CHANNEL_1_GPIO_NUM,
#else
    -1,
#endif
#ifdef ADC2_CHANNEL_2_GPIO_NUM
    ADC2_CHANNEL_2_GPIO_NUM,
#else
    -1,
#endif
#ifdef ADC2_CHANNEL_3_GPIO_NUM
    ADC2_CHANNEL_3_GPIO_NUM,
#else
    -1,
#endif
#ifdef ADC2_CHANNEL_4_GPIO_NUM
    ADC2_CHANNEL_4_GPIO_NUM,
#else
    -1,
#endif
#ifdef ADC2_CHANNEL_5_GPIO_NUM
    ADC2_CHANNEL_5_GPIO_NUM,
#else
    -1,
#endif
#ifdef ADC2_CHANNEL_6_GPIO_NUM
    ADC2_CHANNEL_6_GPIO_NUM,
#else
    -1,
#endif
#ifdef ADC2_CHANNEL_7_GPIO_NUM
    ADC2_CHANNEL_7_GPIO_NUM,
#else
    -1,
#endif
#ifdef ADC2_CHANNEL_8_GPIO_NUM
    ADC2_CHANNEL_8_GPIO_NUM,
#else
    -1,
#endif
#ifdef ADC2_CHANNEL_9_GPIO_NUM
    ADC2_CHANNEL_9_GPIO_NUM,
#else
    -1,
#endif
};
static uint8_t bitsDoAdc = 12;

extern "C" esp_err_t adc1_config_width(adc_bits_width_t w)
{
    bitsDoAdc = (uint8_t)(9 + (int)w);
    analogReadResolution(bitsDoAdc);
    return ESP_OK;
}
/* A atenuacao do canal vale para a leitura do pino (a do analogSetPinAttenuation). */
extern "C" esp_err_t adc1_config_channel_atten(adc1_channel_t c, adc_atten_t a)
{
    if (c >= ADC1_CHANNEL_MAX || gpioDoAdc1[c] < 0) return ESP_ERR_INVALID_ARG;
    analogSetPinAttenuation(gpioDoAdc1[c], (adc_attenuation_t)a);
    return ESP_OK;
}
extern "C" int adc1_get_raw(adc1_channel_t c) { return c < ADC1_CHANNEL_MAX && gpioDoAdc1[c] >= 0 ? analogRead(gpioDoAdc1[c]) : -1; }
#if SOC_ADC_PERIPH_NUM >= 2
extern "C" esp_err_t adc2_config_channel_atten(adc2_channel_t c, adc_atten_t a)
{
    if (c >= ADC2_CHANNEL_MAX || gpioDoAdc2[c] < 0) return ESP_ERR_INVALID_ARG;
    analogSetPinAttenuation(gpioDoAdc2[c], (adc_attenuation_t)a);
    return ESP_OK;
}
extern "C" esp_err_t adc2_get_raw(adc2_channel_t c, adc_bits_width_t, int *saida)
{
    if (c >= ADC2_CHANNEL_MAX || !saida || gpioDoAdc2[c] < 0) return ESP_ERR_INVALID_ARG;
    *saida = analogRead(gpioDoAdc2[c]);
    return ESP_OK;
}
#endif
extern "C" int hall_sensor_read(void) { return 0; }

/* A unidade do ADC (o tipo opaco do IDF). */
struct adc_oneshot_unit_ctx_t {
    adc_unit_t unidade;
};

extern "C" esp_err_t adc_oneshot_new_unit(const adc_oneshot_unit_init_cfg_t *cfg, adc_oneshot_unit_handle_t *saida)
{
    if (!cfg || !saida) return ESP_ERR_INVALID_ARG;
    *saida = new adc_oneshot_unit_ctx_t{cfg->unit_id};
    return ESP_OK;
}
extern "C" esp_err_t adc_oneshot_config_channel(adc_oneshot_unit_handle_t u, adc_channel_t c, const adc_oneshot_chan_cfg_t *cfg)
{
    if (!u || !cfg) return ESP_ERR_INVALID_ARG;
    int gpio = u->unidade == ADC_UNIT_1 ? (c < 10 ? gpioDoAdc1[c] : -1) : (c < 10 ? gpioDoAdc2[c] : -1);
    if (gpio < 0) return ESP_ERR_INVALID_ARG;
    if (cfg->bitwidth) analogReadResolution((int)cfg->bitwidth);
    (void)(gpio), (void)(cfg->atten);   /* a atenuacao nao muda a leitura do simulador */
    return ESP_OK;
}
extern "C" esp_err_t adc_oneshot_read(adc_oneshot_unit_handle_t u, adc_channel_t c, int *saida)
{
    if (!u || !saida) return ESP_ERR_INVALID_ARG;
    int gpio = u->unidade == ADC_UNIT_1 ? (c < 10 ? gpioDoAdc1[c] : -1) : (c < 10 ? gpioDoAdc2[c] : -1);
    if (gpio < 0) return ESP_ERR_INVALID_ARG;
    *saida = analogRead(gpio);
    return ESP_OK;
}
extern "C" esp_err_t adc_oneshot_del_unit(adc_oneshot_unit_handle_t u)
{
    delete u;
    return ESP_OK;
}
extern "C" esp_err_t adc_oneshot_io_to_channel(int io, adc_unit_t *unidade, adc_channel_t *canal)
{
    for (int i = 0; i < 10; i++)
        if (gpioDoAdc1[i] >= 0 && gpioDoAdc1[i] == io) {
            *unidade = ADC_UNIT_1;
            *canal = (adc_channel_t)i;
            return ESP_OK;
        }
    for (int i = 0; i < 10; i++)
        if (gpioDoAdc2[i] >= 0 && gpioDoAdc2[i] == io) {
            *unidade = ADC_UNIT_2;
            *canal = (adc_channel_t)i;
            return ESP_OK;
        }
    return ESP_ERR_NOT_FOUND;
}

/* ---- a flash: a NVS do simulador ja existe (o Preferences); o init so liga ---- */
extern "C" esp_err_t nvs_flash_init(void) { return ESP_OK; }
extern "C" esp_err_t nvs_flash_erase(void) { return ESP_OK; }
extern "C" esp_err_t nvs_flash_deinit(void) { return ESP_OK; }

#endif

/* ---- o I2S do ESP-IDF (driver/i2s.h): a mesma saida do I2SClass ---- */
#if defined(ESP32)
#include "driver/i2s.h"

static i2s_config_t configDoI2s;
static int pinosDoI2s[3] = {-1, -1, -1};
static bool i2sInstalado;

static void reabrirI2s(void)
{
    if (!i2sInstalado) return;
    int canais = configDoI2s.channel_format == I2S_CHANNEL_FMT_RIGHT_LEFT ? 2 : 1;
    cs_i2s_abrir(configDoI2s.sample_rate, configDoI2s.bits_per_sample, canais, pinosDoI2s[0], pinosDoI2s[1], pinosDoI2s[2]);
}

extern "C" esp_err_t i2s_driver_install(i2s_port_t, const i2s_config_t *cfg, int, void *)
{
    if (!cfg) return ESP_ERR_INVALID_ARG;
    configDoI2s = *cfg;
    i2sInstalado = true;
    reabrirI2s();
    return ESP_OK;
}

extern "C" esp_err_t i2s_driver_uninstall(i2s_port_t)
{
    if (i2sInstalado) cs_i2s_fechar();
    i2sInstalado = false;
    return ESP_OK;
}

extern "C" esp_err_t i2s_set_pin(i2s_port_t, const i2s_pin_config_t *pin)
{
    if (!pin) return ESP_OK;
    pinosDoI2s[0] = pin->bck_io_num;
    pinosDoI2s[1] = pin->ws_io_num;
    pinosDoI2s[2] = pin->data_out_num;
    reabrirI2s();
    return ESP_OK;
}

extern "C" esp_err_t i2s_write(i2s_port_t, const void *src, size_t size, size_t *escritos, TickType_t)
{
    size_t n = i2sInstalado ? cs_i2s_escrever(src, size) : 0;
    if (escritos) *escritos = n;
    return i2sInstalado ? ESP_OK : ESP_ERR_INVALID_STATE;
}

extern "C" esp_err_t i2s_read(i2s_port_t, void *dest, size_t size, size_t *lidos, TickType_t)
{
    memset(dest, 0, size);
    if (lidos) *lidos = size;
    return ESP_OK;
}

extern "C" esp_err_t i2s_zero_dma_buffer(i2s_port_t) { return ESP_OK; }
extern "C" esp_err_t i2s_set_clk(i2s_port_t, uint32_t rate, uint32_t bits_cfg, i2s_channel_t ch)
{
    configDoI2s.sample_rate = rate;
    configDoI2s.bits_per_sample = (i2s_bits_per_sample_t)(bits_cfg & 0xFFFF);
    configDoI2s.channel_format = ch == I2S_CHANNEL_STEREO ? I2S_CHANNEL_FMT_RIGHT_LEFT : I2S_CHANNEL_FMT_ONLY_LEFT;
    reabrirI2s();
    return ESP_OK;
}
extern "C" esp_err_t i2s_set_sample_rates(i2s_port_t, uint32_t rate)
{
    configDoI2s.sample_rate = rate;
    reabrirI2s();
    return ESP_OK;
}
extern "C" esp_err_t i2s_start(i2s_port_t) { return ESP_OK; }
extern "C" esp_err_t i2s_stop(i2s_port_t) { return ESP_OK; }
#endif

/* ---- a PSRAM e a memoria por capacidade: a do PC ---- */
#if defined(ESP32)
#include <malloc.h>
extern "C" bool psramFound(void) { return true; }
extern "C" bool psramInit(void) { return true; }
extern "C" void *ps_malloc(size_t n) { return malloc(n); }
extern "C" void *ps_calloc(size_t n, size_t t) { return calloc(n, t); }
extern "C" void *ps_realloc(void *p, size_t n) { return realloc(p, n); }
extern "C" void *heap_caps_malloc(size_t n, uint32_t) { return malloc(n); }
extern "C" void *heap_caps_calloc(size_t n, size_t t, uint32_t) { return calloc(n, t); }
extern "C" void *heap_caps_realloc(void *p, size_t n, uint32_t) { return realloc(p, n); }
extern "C" void *heap_caps_aligned_alloc(size_t a, size_t n, uint32_t) { (void)a; return malloc(n); }  /* o malloc do PC alinha em 16 */
extern "C" void heap_caps_free(void *p) { free(p); }
extern "C" size_t heap_caps_get_free_size(uint32_t caps) { return caps & MALLOC_CAP_SPIRAM ? 8u << 20 : 280000; }
extern "C" size_t heap_caps_get_largest_free_block(uint32_t caps) { return caps & MALLOC_CAP_SPIRAM ? 8u << 20 : 110000; }
extern "C" size_t heap_caps_get_total_size(uint32_t caps) { return caps & MALLOC_CAP_SPIRAM ? 8u << 20 : 327680; }
extern "C" size_t heap_caps_get_minimum_free_size(uint32_t caps) { return heap_caps_get_free_size(caps); }
#endif
