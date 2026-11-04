/* cs_idf_perif.cpp -- os outros perifericos do ESP-IDF no simulador: o temporizador
 * antigo por grupo, o DAC, o sensor de temperatura, o GPIO da RTC, o toque, o
 * sigma-delta, o I2S novo e o TWAI (o CAN entre as placas da bancada). */
#include "Arduino.h"
#if defined(ESP32)
#include "cs_objeto.h"
#include "esp32-hal-timer.h"
#include "driver/timer.h"
#include "driver/temperature_sensor.h"
#include "driver/rtc_io.h"
#include "driver/twai.h"
#include "driver/sdm.h"
#include "driver/i2s_std.h"
#include "freertos/queue.h"
#include "freertos/task.h"
#if SOC_DAC_SUPPORTED
#include "driver/dac_oneshot.h"
#endif
#if SOC_TOUCH_SENSOR_SUPPORTED
#include "driver/touch_sensor_common.h"
#endif
#include <deque>
#include <vector>

/* =====================================================================
 * o temporizador antigo: grupo e numero */
namespace {
const uint64_t RELOGIO_DA_APB = 80000000ULL;
struct TimerAntigo {
    hw_timer_t *hw;
    timer_config_t cfg;
    uint64_t alarme;
    timer_isr_t rotina;
    void *arg;
    bool alarmeLigado;
};
TimerAntigo antigos[2][2] = {};

TimerAntigo *antigo(timer_group_t g, timer_idx_t i)
{
    if ((int)g < 0 || (int)g >= 2 || (int)i < 0 || (int)i >= 2) return nullptr;
    return &antigos[(int)g][(int)i];
}
void ARDUINO_ISR_ATTR alarmeAntigo(void *arg)
{
    auto *t = (TimerAntigo *)arg;
    if (t->rotina) t->rotina(t->arg);
}
void armar(TimerAntigo *t)
{
    if (t->hw && t->alarmeLigado) timerAlarm(t->hw, t->alarme, t->cfg.auto_reload == TIMER_AUTORELOAD_EN, 0);
}
} /* namespace */

extern "C" {
esp_err_t timer_init(timer_group_t g, timer_idx_t i, const timer_config_t *cfg)
{
    TimerAntigo *t = antigo(g, i);
    if (!t || !cfg || cfg->divider < 2) return ESP_ERR_INVALID_ARG;
    t->cfg = *cfg;
    if (!t->hw) t->hw = timerBegin((uint32_t)(RELOGIO_DA_APB / cfg->divider));
    if (!t->hw) return ESP_ERR_NOT_FOUND;
    t->alarmeLigado = cfg->alarm_en == TIMER_ALARM_EN;
    timerAttachInterruptArg(t->hw, alarmeAntigo, t);
    if (cfg->counter_en == TIMER_START) timerStart(t->hw);
    else timerStop(t->hw);
    return ESP_OK;
}
esp_err_t timer_deinit(timer_group_t g, timer_idx_t i)
{
    TimerAntigo *t = antigo(g, i);
    if (!t || !t->hw) return ESP_ERR_INVALID_ARG;
    timerEnd(t->hw);
    t->hw = nullptr;
    return ESP_OK;
}
esp_err_t timer_start(timer_group_t g, timer_idx_t i)
{
    TimerAntigo *t = antigo(g, i);
    if (!t || !t->hw) return ESP_ERR_INVALID_ARG;
    timerStart(t->hw);
    return ESP_OK;
}
esp_err_t timer_pause(timer_group_t g, timer_idx_t i)
{
    TimerAntigo *t = antigo(g, i);
    if (!t || !t->hw) return ESP_ERR_INVALID_ARG;
    timerStop(t->hw);
    return ESP_OK;
}
esp_err_t timer_get_counter_value(timer_group_t g, timer_idx_t i, uint64_t *valor)
{
    TimerAntigo *t = antigo(g, i);
    if (!t || !t->hw || !valor) return ESP_ERR_INVALID_ARG;
    *valor = timerRead(t->hw);
    return ESP_OK;
}
esp_err_t timer_get_counter_time_sec(timer_group_t g, timer_idx_t i, double *tempo)
{
    TimerAntigo *t = antigo(g, i);
    if (!t || !t->hw || !tempo) return ESP_ERR_INVALID_ARG;
    *tempo = timerReadSeconds(t->hw);
    return ESP_OK;
}
esp_err_t timer_set_counter_value(timer_group_t g, timer_idx_t i, uint64_t valor)
{
    TimerAntigo *t = antigo(g, i);
    if (!t || !t->hw) return ESP_ERR_INVALID_ARG;
    timerWrite(t->hw, valor);
    return ESP_OK;
}
esp_err_t timer_set_alarm_value(timer_group_t g, timer_idx_t i, uint64_t valor)
{
    TimerAntigo *t = antigo(g, i);
    if (!t) return ESP_ERR_INVALID_ARG;
    t->alarme = valor;
    armar(t);
    return ESP_OK;
}
esp_err_t timer_get_alarm_value(timer_group_t g, timer_idx_t i, uint64_t *valor)
{
    TimerAntigo *t = antigo(g, i);
    if (!t || !valor) return ESP_ERR_INVALID_ARG;
    *valor = t->alarme;
    return ESP_OK;
}
esp_err_t timer_set_alarm(timer_group_t g, timer_idx_t i, timer_alarm_t ligar)
{
    TimerAntigo *t = antigo(g, i);
    if (!t) return ESP_ERR_INVALID_ARG;
    t->alarmeLigado = ligar == TIMER_ALARM_EN;
    armar(t);
    return ESP_OK;
}
esp_err_t timer_set_auto_reload(timer_group_t g, timer_idx_t i, timer_autoreload_t recarga)
{
    TimerAntigo *t = antigo(g, i);
    if (!t) return ESP_ERR_INVALID_ARG;
    t->cfg.auto_reload = recarga;
    armar(t);
    return ESP_OK;
}
esp_err_t timer_set_divider(timer_group_t g, timer_idx_t i, uint32_t divisor)
{
    TimerAntigo *t = antigo(g, i);
    if (!t || !t->hw || divisor < 2) return ESP_ERR_INVALID_ARG;
    t->cfg.divider = divisor;
    timerSetDivider(t->hw, (uint16_t)divisor);
    return ESP_OK;
}
esp_err_t timer_set_counter_mode(timer_group_t g, timer_idx_t i, timer_count_dir_t modo)
{
    TimerAntigo *t = antigo(g, i);
    if (!t) return ESP_ERR_INVALID_ARG;
    t->cfg.counter_dir = modo;
    return ESP_OK;
}
esp_err_t timer_get_config(timer_group_t g, timer_idx_t i, timer_config_t *cfg)
{
    TimerAntigo *t = antigo(g, i);
    if (!t || !cfg) return ESP_ERR_INVALID_ARG;
    *cfg = t->cfg;
    return ESP_OK;
}
esp_err_t timer_isr_callback_add(timer_group_t g, timer_idx_t i, timer_isr_t rotina, void *arg, int flags)
{
    (void)flags;
    TimerAntigo *t = antigo(g, i);
    if (!t || !rotina) return ESP_ERR_INVALID_ARG;
    t->rotina = rotina;
    t->arg = arg;
    return ESP_OK;
}
esp_err_t timer_isr_callback_remove(timer_group_t g, timer_idx_t i)
{
    TimerAntigo *t = antigo(g, i);
    if (!t) return ESP_ERR_INVALID_ARG;
    t->rotina = nullptr;
    return ESP_OK;
}
esp_err_t timer_enable_intr(timer_group_t g, timer_idx_t i)
{
    return antigo(g, i) ? ESP_OK : ESP_ERR_INVALID_ARG;
}
esp_err_t timer_disable_intr(timer_group_t g, timer_idx_t i)
{
    TimerAntigo *t = antigo(g, i);
    if (!t) return ESP_ERR_INVALID_ARG;
    t->alarmeLigado = false;
    if (t->hw) timerAlarm(t->hw, 0xFFFFFFFFFFFFFFFFULL, false, 0);
    return ESP_OK;
}
void timer_group_set_alarm_value_in_isr(timer_group_t g, timer_idx_t i, uint64_t valor) { timer_set_alarm_value(g, i, valor); }
uint64_t timer_group_get_counter_value_in_isr(timer_group_t g, timer_idx_t i)
{
    uint64_t v = 0;
    timer_get_counter_value(g, i, &v);
    return v;
}
void timer_group_enable_alarm_in_isr(timer_group_t g, timer_idx_t i) { timer_set_alarm(g, i, TIMER_ALARM_EN); }
void timer_group_set_counter_enable_in_isr(timer_group_t g, timer_idx_t i, timer_start_t ligar)
{
    if (ligar == TIMER_START) timer_start(g, i);
    else timer_pause(g, i);
}
bool timer_group_get_auto_reload_in_isr(timer_group_t g, timer_idx_t i)
{
    TimerAntigo *t = antigo(g, i);
    return t && t->cfg.auto_reload == TIMER_AUTORELOAD_EN;
}

/* =====================================================================
 * o sensor de temperatura do chip */
struct temperature_sensor_obj_t {
    bool ligado;
};
esp_err_t temperature_sensor_install(const temperature_sensor_config_t *cfg, temperature_sensor_handle_t *saida)
{
    if (!cfg || !saida) return ESP_ERR_INVALID_ARG;
    *saida = (temperature_sensor_handle_t) new temperature_sensor_obj_t{false};
    return ESP_OK;
}
esp_err_t temperature_sensor_uninstall(temperature_sensor_handle_t t)
{
    if (!t) return ESP_ERR_INVALID_ARG;
    delete (temperature_sensor_obj_t *)t;
    return ESP_OK;
}
esp_err_t temperature_sensor_enable(temperature_sensor_handle_t t)
{
    if (!t) return ESP_ERR_INVALID_ARG;
    ((temperature_sensor_obj_t *)t)->ligado = true;
    return ESP_OK;
}
esp_err_t temperature_sensor_disable(temperature_sensor_handle_t t)
{
    if (!t) return ESP_ERR_INVALID_ARG;
    ((temperature_sensor_obj_t *)t)->ligado = false;
    return ESP_OK;
}
esp_err_t temperature_sensor_get_celsius(temperature_sensor_handle_t t, float *celsius)
{
    if (!t || !celsius) return ESP_ERR_INVALID_ARG;
    if (!((temperature_sensor_obj_t *)t)->ligado) return ESP_ERR_INVALID_STATE;
    *celsius = temperatureRead();
    return ESP_OK;
}

/* =====================================================================
 * o GPIO da RTC: no simulador, os mesmos pinos */
#if SOC_RTCIO_INPUT_OUTPUT_SUPPORTED
esp_err_t rtc_gpio_init(gpio_num_t n) { return n >= 0 ? ESP_OK : ESP_ERR_INVALID_ARG; }
esp_err_t rtc_gpio_deinit(gpio_num_t n) { return n >= 0 ? ESP_OK : ESP_ERR_INVALID_ARG; }
uint32_t rtc_gpio_get_level(gpio_num_t n) { return digitalRead(n); }
esp_err_t rtc_gpio_set_level(gpio_num_t n, uint32_t nivel)
{
    digitalWrite(n, nivel ? HIGH : LOW);
    return ESP_OK;
}
esp_err_t rtc_gpio_set_direction(gpio_num_t n, rtc_gpio_mode_t modo)
{
    pinMode(n, modo == RTC_GPIO_MODE_INPUT_ONLY ? INPUT : OUTPUT);
    return ESP_OK;
}
esp_err_t rtc_gpio_pullup_en(gpio_num_t n)
{
    pinMode(n, INPUT_PULLUP);
    return ESP_OK;
}
esp_err_t rtc_gpio_pulldown_en(gpio_num_t n)
{
    pinMode(n, INPUT_PULLDOWN);
    return ESP_OK;
}
#endif

/* =====================================================================
 * o toque: o touch_pad_t e o T0, T1... do Arduino */
#if SOC_TOUCH_SENSOR_SUPPORTED
#if CONFIG_IDF_TARGET_ESP32
static const int8_t PINOS_DO_TOQUE[] = {4, 0, 2, 15, 13, 12, 14, 27, 33, 32};
#else
static const int8_t PINOS_DO_TOQUE[] = {-1, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14};
#endif
esp_err_t touch_pad_init(void) { return ESP_OK; }
esp_err_t touch_pad_deinit(void) { return ESP_OK; }
esp_err_t touch_pad_config(touch_pad_t pad, ...)
{
    return (int)pad < (int)sizeof(PINOS_DO_TOQUE) ? ESP_OK : ESP_ERR_INVALID_ARG;
}
esp_err_t touch_pad_read(touch_pad_t pad, uint16_t *valor)
{
    if ((int)pad >= (int)sizeof(PINOS_DO_TOQUE) || PINOS_DO_TOQUE[(int)pad] < 0 || !valor) return ESP_ERR_INVALID_ARG;
    *valor = (uint16_t)touchRead((uint8_t)PINOS_DO_TOQUE[(int)pad]);
    return ESP_OK;
}
esp_err_t touch_pad_read_raw_data(touch_pad_t pad, uint32_t *valor)
{
    if ((int)pad >= (int)sizeof(PINOS_DO_TOQUE) || PINOS_DO_TOQUE[(int)pad] < 0 || !valor) return ESP_ERR_INVALID_ARG;
    *valor = (uint32_t)touchRead((uint8_t)PINOS_DO_TOQUE[(int)pad]);
    return ESP_OK;
}
#endif

/* =====================================================================
 * o DAC de uma leitura */
#if SOC_DAC_SUPPORTED
static int pinoDoDac(dac_channel_t canal)
{
#if CONFIG_IDF_TARGET_ESP32
    return canal == DAC_CHAN_0 ? 25 : 26;
#else
    return canal == DAC_CHAN_0 ? 17 : 18;
#endif
}
struct dac_oneshot_s {
    int pino;
};
esp_err_t dac_oneshot_new_channel(const dac_oneshot_config_t *cfg, dac_oneshot_handle_t *saida)
{
    if (!cfg || !saida) return ESP_ERR_INVALID_ARG;
    *saida = new dac_oneshot_s{pinoDoDac(cfg->chan_id)};
    return ESP_OK;
}
esp_err_t dac_oneshot_del_channel(dac_oneshot_handle_t h)
{
    if (!h) return ESP_ERR_INVALID_ARG;
    dacDisable((uint8_t)h->pino);
    delete h;
    return ESP_OK;
}
esp_err_t dac_oneshot_output_voltage(dac_oneshot_handle_t h, uint8_t valor)
{
    if (!h) return ESP_ERR_INVALID_ARG;
    dacWrite((uint8_t)h->pino, valor);
    return ESP_OK;
}
#endif

/* =====================================================================
 * o sigma-delta */
struct sdm_channel_t {
    int pino;
    bool ligado;
};
esp_err_t sdm_new_channel(const sdm_config_t *cfg, sdm_channel_handle_t *saida)
{
    if (!cfg || !saida || cfg->gpio_num < 0) return ESP_ERR_INVALID_ARG;
    auto *c = new sdm_channel_t{cfg->gpio_num, false};
    sigmaDeltaAttach((uint8_t)c->pino, cfg->sample_rate_hz ? cfg->sample_rate_hz : 1000000);
    *saida = c;
    return ESP_OK;
}
esp_err_t sdm_del_channel(sdm_channel_handle_t c)
{
    if (!c) return ESP_ERR_INVALID_ARG;
    if (c->ligado) return ESP_ERR_INVALID_STATE;
    sigmaDeltaDetach((uint8_t)c->pino);
    delete c;
    return ESP_OK;
}
esp_err_t sdm_channel_enable(sdm_channel_handle_t c)
{
    if (!c) return ESP_ERR_INVALID_ARG;
    c->ligado = true;
    return ESP_OK;
}
esp_err_t sdm_channel_disable(sdm_channel_handle_t c)
{
    if (!c) return ESP_ERR_INVALID_ARG;
    c->ligado = false;
    return ESP_OK;
}
esp_err_t sdm_channel_set_pulse_density(sdm_channel_handle_t c, int8_t densidade)
{
    if (!c) return ESP_ERR_INVALID_ARG;
    sigmaDeltaWrite((uint8_t)c->pino, (uint8_t)(densidade + 128));
    return ESP_OK;
}
esp_err_t sdm_channel_set_duty(sdm_channel_handle_t c, int8_t densidade) { return sdm_channel_set_pulse_density(c, densidade); }

/* =====================================================================
 * o I2S novo: saida pelo mesmo caminho do I2SClass (cs_i2s.cpp). A entrada le silencio. */
bool cs_i2s_abrir(uint32_t taxa, int bits, int canais, int bclk, int ws, int dout);
size_t cs_i2s_escrever(const void *dados, size_t n);
void cs_i2s_fechar(void);

struct i2s_channel_obj_t {
    bool saida, ligado, configurado;
    uint32_t taxa;
    int bits, canais, bclk, ws, dout;
};
esp_err_t i2s_new_channel(const i2s_chan_config_t *cfg, i2s_chan_handle_t *tx, i2s_chan_handle_t *rx)
{
    if (!cfg || (!tx && !rx)) return ESP_ERR_INVALID_ARG;
    if (tx) {
        auto *c = new i2s_channel_obj_t();
        c->saida = true;
        *tx = c;
    }
    if (rx) *rx = new i2s_channel_obj_t();
    return ESP_OK;
}
esp_err_t i2s_del_channel(i2s_chan_handle_t c)
{
    if (!c) return ESP_ERR_INVALID_ARG;
    if (c->ligado) return ESP_ERR_INVALID_STATE;
    delete c;
    return ESP_OK;
}
esp_err_t i2s_channel_init_std_mode(i2s_chan_handle_t c, const i2s_std_config_t *cfg)
{
    if (!c || !cfg) return ESP_ERR_INVALID_ARG;
    c->taxa = cfg->clk_cfg.sample_rate_hz;
    c->bits = (int)cfg->slot_cfg.data_bit_width;
    c->canais = cfg->slot_cfg.slot_mode == I2S_SLOT_MODE_MONO ? 1 : 2;
    c->bclk = cfg->gpio_cfg.bclk;
    c->ws = cfg->gpio_cfg.ws;
    c->dout = cfg->gpio_cfg.dout;
    c->configurado = true;
    return ESP_OK;
}
esp_err_t i2s_channel_enable(i2s_chan_handle_t c)
{
    if (!c) return ESP_ERR_INVALID_ARG;
    if (!c->configurado || c->ligado) return ESP_ERR_INVALID_STATE;
    c->ligado = true;
    if (c->saida) cs_i2s_abrir(c->taxa, c->bits, c->canais, c->bclk, c->ws, c->dout);
    return ESP_OK;
}
esp_err_t i2s_channel_disable(i2s_chan_handle_t c)
{
    if (!c) return ESP_ERR_INVALID_ARG;
    if (!c->ligado) return ESP_ERR_INVALID_STATE;
    c->ligado = false;
    if (c->saida) cs_i2s_fechar();
    return ESP_OK;
}
esp_err_t i2s_channel_write(i2s_chan_handle_t c, const void *dados, size_t n, size_t *feitos, uint32_t tempo_ms)
{
    (void)tempo_ms;
    if (!c || !dados || !c->saida) return ESP_ERR_INVALID_ARG;
    if (!c->ligado) return ESP_ERR_INVALID_STATE;
    size_t e = cs_i2s_escrever(dados, n);
    if (feitos) *feitos = e;
    return ESP_OK;
}
esp_err_t i2s_channel_read(i2s_chan_handle_t c, void *dados, size_t n, size_t *feitos, uint32_t tempo_ms)
{
    if (!c || !dados || c->saida) return ESP_ERR_INVALID_ARG;
    if (!c->ligado) return ESP_ERR_INVALID_STATE;
    memset(dados, 0, n);
    /* Silencio no tempo certo: o buffer leva o tempo das amostras. */
    uint32_t bytesPorSegundo = c->taxa * (uint32_t)(c->bits / 8) * (uint32_t)c->canais;
    uint32_t ms = bytesPorSegundo ? (uint32_t)((uint64_t)n * 1000 / bytesPorSegundo) : 0;
    vTaskDelay(pdMS_TO_TICKS(ms < tempo_ms ? ms : tempo_ms));
    if (feitos) *feitos = n;
    return ESP_OK;
}

/* =====================================================================
 * o TWAI (CAN): os quadros vao pelo radio da bancada no canal do CAN, para todas
 * as placas com o driver ligado; o simulador nao confere a fiacao do barramento */
static const int CANAL_DO_CAN = 99;
static bool twaiInstalado = false, twaiRodando = false;
static twai_mode_t modoDoTwai = TWAI_MODE_NORMAL;
static std::deque<twai_message_t> &filaDoTwai()
{
    static auto *f = new std::deque<twai_message_t>();
    return *f;
}
static twai_filter_config_t filtroDoTwai;
static uint32_t transmitidos = 0, recebidos = 0;

static void receberTwai()
{
    static uint8_t entrada[32];
    for (int i = 0; i < 16; i++) {
        int n = (int)cs_obj_i(nullptr, "__154_receber", cs_vetor_mutavel(entrada, (int)sizeof(entrada)));
        if (n <= 1) return;
        if (entrada[0] != CANAL_DO_CAN || n < 7) continue;
        twai_message_t m = {};
        m.identifier = (uint32_t)entrada[1] | ((uint32_t)entrada[2] << 8) | ((uint32_t)entrada[3] << 16) | ((uint32_t)entrada[4] << 24);
        m.flags = entrada[5];
        m.data_length_code = entrada[6];
        memcpy(m.data, entrada + 7, m.data_length_code > 8 ? 8 : m.data_length_code);
        /* O filtro de aceitacao (modo simples, sobre o identificador de 11 bits). */
        if (filtroDoTwai.acceptance_mask != 0xFFFFFFFF) {
            uint32_t chave = m.identifier << 21;
            if ((chave ^ filtroDoTwai.acceptance_code) & ~filtroDoTwai.acceptance_mask) continue;
        }
        filaDoTwai().push_back(m);
        recebidos++;
    }
}
esp_err_t twai_driver_install(const twai_general_config_t *g, const twai_timing_config_t *t, const twai_filter_config_t *f)
{
    if (!g || !t || !f) return ESP_ERR_INVALID_ARG;
    if (twaiInstalado) return ESP_ERR_INVALID_STATE;
    twaiInstalado = true;
    modoDoTwai = g->mode;
    filtroDoTwai = *f;
    cs_obj_n(nullptr, "__154_ligar", 1);
    return ESP_OK;
}
esp_err_t twai_driver_uninstall(void)
{
    if (!twaiInstalado || twaiRodando) return ESP_ERR_INVALID_STATE;
    twaiInstalado = false;
    return ESP_OK;
}
esp_err_t twai_start(void)
{
    if (!twaiInstalado || twaiRodando) return ESP_ERR_INVALID_STATE;
    twaiRodando = true;
    return ESP_OK;
}
esp_err_t twai_stop(void)
{
    if (!twaiRodando) return ESP_ERR_INVALID_STATE;
    twaiRodando = false;
    return ESP_OK;
}
esp_err_t twai_transmit(const twai_message_t *m, TickType_t ticks)
{
    (void)ticks;
    if (!m || m->data_length_code > 8) return ESP_ERR_INVALID_ARG;
    if (!twaiRodando) return ESP_ERR_INVALID_STATE;
    if (modoDoTwai == TWAI_MODE_LISTEN_ONLY) return ESP_ERR_NOT_SUPPORTED;
    uint8_t q[15];
    q[0] = (uint8_t)m->identifier;
    q[1] = (uint8_t)(m->identifier >> 8);
    q[2] = (uint8_t)(m->identifier >> 16);
    q[3] = (uint8_t)(m->identifier >> 24);
    q[4] = (uint8_t)m->flags;
    q[5] = m->data_length_code;
    memcpy(q + 6, m->data, 8);
    cs_obj_n(nullptr, "__154_enviar", CANAL_DO_CAN, cs_vetor(q, 6 + m->data_length_code));
    if (m->self || modoDoTwai == TWAI_MODE_NO_ACK) {
        if (m->self) filaDoTwai().push_back(*m);
    }
    transmitidos++;
    return ESP_OK;
}
esp_err_t twai_receive(twai_message_t *m, TickType_t ticks)
{
    if (!m) return ESP_ERR_INVALID_ARG;
    if (!twaiRodando) return ESP_ERR_INVALID_STATE;
    uint32_t inicio = millis(), limite = pdTICKS_TO_MS(ticks);
    for (;;) {
        receberTwai();
        if (!filaDoTwai().empty()) {
            *m = filaDoTwai().front();
            filaDoTwai().pop_front();
            return ESP_OK;
        }
        if (ticks != portMAX_DELAY && millis() - inicio >= limite) return ESP_ERR_TIMEOUT;
        vTaskDelay(1);
    }
}
esp_err_t twai_read_alerts(uint32_t *alerts, TickType_t ticks)
{
    if (alerts) *alerts = 0;
    vTaskDelay(ticks == portMAX_DELAY ? 1 : ticks);
    return ESP_ERR_TIMEOUT;
}
esp_err_t twai_reconfigure_alerts(uint32_t alerts_enabled, uint32_t *current_alerts)
{
    (void)alerts_enabled;
    if (current_alerts) *current_alerts = 0;
    return ESP_OK;
}
esp_err_t twai_initiate_recovery(void) { return ESP_OK; }
esp_err_t twai_clear_transmit_queue(void) { return ESP_OK; }
esp_err_t twai_clear_receive_queue(void)
{
    filaDoTwai().clear();
    return ESP_OK;
}
esp_err_t twai_get_status_info(twai_status_info_t *status)
{
    if (!status) return ESP_ERR_INVALID_ARG;
    memset(status, 0, sizeof(*status));
    status->state = twaiRodando ? TWAI_STATE_RUNNING : TWAI_STATE_STOPPED;
    status->msgs_to_rx = (uint32_t)filaDoTwai().size();
    return ESP_OK;
}
} /* extern "C" */
#endif
