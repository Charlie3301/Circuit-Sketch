/* cs_idf.cpp -- os drivers do ESP-IDF sobre o core (ver cs_idf.h). */
#include "Arduino.h"
#if defined(ESP32)
#include "cs_idf.h"
#include "cs_interno.h"
#include <stdarg.h>

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

/* O ESP_ERROR_CHECK que falha: na placa, a mensagem e o abort (a placa
 * reinicia); aqui, a mensagem e o programa para com ela. */
extern "C" void cs_idf_conferir(esp_err_t r, const char *expr, const char *arquivo, int linha)
{
    if (r == ESP_OK) return;
    printf("ESP_ERROR_CHECK failed: esp_err_t 0x%x (%s) at %s:%d\nexpression: %s\n", (unsigned)r, esp_err_to_name(r),
           arquivo, linha, expr);
    abort();
}

/* ---- o log: "I (1234) tag: mensagem" ---- */
static esp_log_level_t nivelDoLog = ESP_LOG_INFO;

extern "C" void esp_log_level_set(const char *, esp_log_level_t level)
{
    nivelDoLog = level;
}
extern "C" uint32_t esp_log_timestamp(void) { return (uint32_t)millis(); }

static void escreverLog(char letra, const char *tag, const char *formato, va_list a)
{
    char corpo[512];
    vsnprintf(corpo, sizeof(corpo), formato, a);
    printf("%c (%lu) %s: %s\n", letra, (unsigned long)millis(), tag ? tag : "", corpo);
}

extern "C" void cs_idf_log(esp_log_level_t nivel, char letra, const char *tag, const char *formato, ...)
{
    if (nivel > nivelDoLog) return;
    va_list a;
    va_start(a, formato);
    escreverLog(letra, tag, formato, a);
    va_end(a);
}

extern "C" void esp_log_write(esp_log_level_t nivel, const char *tag, const char *formato, ...)
{
    if (nivel > nivelDoLog) return;
    va_list a;
    va_start(a, formato);
    char corpo[512];
    vsnprintf(corpo, sizeof(corpo), formato, a);
    va_end(a);
    printf("%s", corpo);
    (void)tag;
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
extern "C" esp_err_t esp_flash_get_size(void *, uint32_t *tamanho)
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
static const int8_t gpioDoAdc1[8] = {36, 37, 38, 39, 32, 33, 34, 35};
static const int8_t gpioDoAdc2[10] = {4, 0, 2, 15, 13, 12, 14, 27, 25, 26};
static uint8_t bitsDoAdc = 12;

extern "C" esp_err_t adc1_config_width(adc_bits_width_t w)
{
    bitsDoAdc = (uint8_t)(9 + (int)w);
    analogReadResolution(bitsDoAdc);
    return ESP_OK;
}
extern "C" esp_err_t adc1_config_channel_atten(adc1_channel_t c, adc_atten_t a)
{
    if (c >= ADC1_CHANNEL_MAX) return ESP_ERR_INVALID_ARG;
    (void)(gpioDoAdc1[c]), (void)(a);   /* a atenuacao nao muda a leitura do simulador */
    return ESP_OK;
}
extern "C" int adc1_get_raw(adc1_channel_t c) { return c < ADC1_CHANNEL_MAX ? analogRead(gpioDoAdc1[c]) : -1; }
extern "C" esp_err_t adc2_config_channel_atten(adc2_channel_t c, adc_atten_t a)
{
    if (c >= ADC2_CHANNEL_MAX) return ESP_ERR_INVALID_ARG;
    (void)(gpioDoAdc2[c]), (void)(a);   /* a atenuacao nao muda a leitura do simulador */
    return ESP_OK;
}
extern "C" esp_err_t adc2_get_raw(adc2_channel_t c, adc_bits_width_t, int *saida)
{
    if (c >= ADC2_CHANNEL_MAX || !saida) return ESP_ERR_INVALID_ARG;
    *saida = analogRead(gpioDoAdc2[c]);
    return ESP_OK;
}
extern "C" int hall_sensor_read(void) { return 0; }

struct cs_adc_unidade {
    adc_unit_t unidade;
};

extern "C" esp_err_t adc_oneshot_new_unit(const adc_oneshot_unit_init_cfg_t *cfg, adc_oneshot_unit_handle_t *saida)
{
    if (!cfg || !saida) return ESP_ERR_INVALID_ARG;
    *saida = new cs_adc_unidade{cfg->unit_id};
    return ESP_OK;
}
extern "C" esp_err_t adc_oneshot_config_channel(adc_oneshot_unit_handle_t u, adc_channel_t c, const adc_oneshot_chan_cfg_t *cfg)
{
    if (!u || !cfg) return ESP_ERR_INVALID_ARG;
    int gpio = u->unidade == ADC_UNIT_1 ? (c < 8 ? gpioDoAdc1[c] : -1) : (c < 10 ? gpioDoAdc2[c] : -1);
    if (gpio < 0) return ESP_ERR_INVALID_ARG;
    if (cfg->bitwidth) analogReadResolution((int)cfg->bitwidth);
    (void)(gpio), (void)(cfg->atten);   /* a atenuacao nao muda a leitura do simulador */
    return ESP_OK;
}
extern "C" esp_err_t adc_oneshot_read(adc_oneshot_unit_handle_t u, adc_channel_t c, int *saida)
{
    if (!u || !saida) return ESP_ERR_INVALID_ARG;
    int gpio = u->unidade == ADC_UNIT_1 ? (c < 8 ? gpioDoAdc1[c] : -1) : (c < 10 ? gpioDoAdc2[c] : -1);
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
    for (int i = 0; i < 8; i++)
        if (gpioDoAdc1[i] == io) {
            *unidade = ADC_UNIT_1;
            *canal = (adc_channel_t)i;
            return ESP_OK;
        }
    for (int i = 0; i < 10; i++)
        if (gpioDoAdc2[i] == io) {
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
