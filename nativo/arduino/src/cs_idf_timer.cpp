/* cs_idf_timer.cpp -- os temporizadores do ESP-IDF: o GPTimer (driver/gptimer.h)
 * e o antigo por grupo (driver/timer.h), nos temporizadores da placa do
 * esp32-hal-timer (cs_timer.cpp). */
#include "Arduino.h"
#if defined(ESP32)
#include "driver/gptimer.h"
#include "esp32-hal-timer.h"

struct gptimer_t {
    hw_timer_t *hw;
    uint32_t resolucao;
    gptimer_alarm_cb_t cb;
    void *ctx;
    uint64_t alarme, recarga;
    bool repete, temAlarme, habilitado, rodando;
    uint64_t cargaInicial;
};

static void ARDUINO_ISR_ATTR alarmeDoGptimer(void *arg)
{
    auto *t = (gptimer_t *)arg;
    if (t->repete && t->recarga) timerWrite(t->hw, t->recarga);
    if (t->cb) {
        gptimer_alarm_event_data_t ev = {};
        ev.count_value = timerRead(t->hw);
        ev.alarm_value = t->alarme;
        t->cb(t, &ev, t->ctx);
    }
}

static void aplicarAlarme(gptimer_t *t)
{
    if (t->temAlarme) timerAlarm(t->hw, t->alarme, t->repete, 0);
}

extern "C" {

esp_err_t gptimer_new_timer(const gptimer_config_t *cfg, gptimer_handle_t *saida)
{
    if (!cfg || !saida || cfg->resolution_hz == 0) return ESP_ERR_INVALID_ARG;
    auto *t = new gptimer_t();
    t->resolucao = cfg->resolution_hz;
    t->hw = timerBegin(cfg->resolution_hz);
    if (!t->hw) {
        delete t;
        return ESP_ERR_NOT_FOUND;
    }
    timerStop(t->hw);
    timerWrite(t->hw, 0);
    *saida = t;
    return ESP_OK;
}
esp_err_t gptimer_del_timer(gptimer_handle_t t)
{
    if (!t) return ESP_ERR_INVALID_ARG;
    if (t->habilitado) return ESP_ERR_INVALID_STATE;
    timerEnd(t->hw);
    delete t;
    return ESP_OK;
}
esp_err_t gptimer_set_raw_count(gptimer_handle_t t, uint64_t valor)
{
    if (!t) return ESP_ERR_INVALID_ARG;
    timerWrite(t->hw, valor);
    return ESP_OK;
}
esp_err_t gptimer_get_raw_count(gptimer_handle_t t, uint64_t *valor)
{
    if (!t || !valor) return ESP_ERR_INVALID_ARG;
    *valor = timerRead(t->hw);
    return ESP_OK;
}
esp_err_t gptimer_get_resolution(gptimer_handle_t t, uint32_t *hz)
{
    if (!t || !hz) return ESP_ERR_INVALID_ARG;
    *hz = t->resolucao;
    return ESP_OK;
}
esp_err_t gptimer_get_captured_count(gptimer_handle_t t, uint64_t *valor)
{
    if (!t || !valor) return ESP_ERR_INVALID_ARG;
    *valor = timerRead(t->hw);
    return ESP_OK;
}
esp_err_t gptimer_register_event_callbacks(gptimer_handle_t t, const gptimer_event_callbacks_t *cbs, void *ctx)
{
    if (!t) return ESP_ERR_INVALID_ARG;
    if (t->habilitado) return ESP_ERR_INVALID_STATE;
    t->cb = cbs ? cbs->on_alarm : nullptr;
    t->ctx = ctx;
    timerAttachInterruptArg(t->hw, alarmeDoGptimer, t);
    return ESP_OK;
}
esp_err_t gptimer_set_alarm_action(gptimer_handle_t t, const gptimer_alarm_config_t *cfg)
{
    if (!t) return ESP_ERR_INVALID_ARG;
    if (!cfg) {
        t->temAlarme = false;
        timerAlarm(t->hw, 0xFFFFFFFFFFFFFFFFULL, false, 0);
        return ESP_OK;
    }
    t->alarme = cfg->alarm_count;
    t->recarga = cfg->reload_count;
    t->repete = cfg->flags.auto_reload_on_alarm;
    t->temAlarme = true;
    aplicarAlarme(t);
    return ESP_OK;
}
esp_err_t gptimer_enable(gptimer_handle_t t)
{
    if (!t) return ESP_ERR_INVALID_ARG;
    if (t->habilitado) return ESP_ERR_INVALID_STATE;
    t->habilitado = true;
    return ESP_OK;
}
esp_err_t gptimer_disable(gptimer_handle_t t)
{
    if (!t) return ESP_ERR_INVALID_ARG;
    if (!t->habilitado || t->rodando) return ESP_ERR_INVALID_STATE;
    t->habilitado = false;
    return ESP_OK;
}
esp_err_t gptimer_start(gptimer_handle_t t)
{
    if (!t) return ESP_ERR_INVALID_ARG;
    if (!t->habilitado || t->rodando) return ESP_ERR_INVALID_STATE;
    t->rodando = true;
    timerStart(t->hw);
    return ESP_OK;
}
esp_err_t gptimer_stop(gptimer_handle_t t)
{
    if (!t) return ESP_ERR_INVALID_ARG;
    if (!t->rodando) return ESP_ERR_INVALID_STATE;
    t->rodando = false;
    timerStop(t->hw);
    return ESP_OK;
}
} /* extern "C" */
#endif
