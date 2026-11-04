/* cs_idf_pwm.cpp -- o MCPWM novo do ESP-IDF (driver/mcpwm_prelude.h): temporizador,
 * operador, comparador e gerador. No simulador cada gerador e um PWM do pino: a
 * frequencia vem do temporizador, e o duty, do comparador -- e o bastante para o
 * servo e para o motor com ponte H dos exemplos do IDF. */
#include "Arduino.h"
#if defined(ESP32) && SOC_MCPWM_SUPPORTED
#include "driver/mcpwm_prelude.h"
#include <vector>

struct mcpwm_timer_t {
    uint32_t resolucao, periodo;
    bool ligado;
    mcpwm_timer_event_callbacks_t cbs;
    void *ctx;
};
struct mcpwm_oper_t {
    mcpwm_timer_t *timer;
};
struct mcpwm_cmpr_t {
    mcpwm_oper_t *oper;
    uint32_t valor;
    std::vector<struct mcpwm_gen_t *> geradores;
};
struct mcpwm_gen_t {
    mcpwm_oper_t *oper;
    int pino;
    mcpwm_cmpr_t *comparador;
    bool invertido, forcado, nivelForcado, anexado;
};

static const uint8_t BITS_DO_PWM = 12;

static void aplicar(mcpwm_gen_t *g)
{
    mcpwm_timer_t *t = g->oper ? g->oper->timer : nullptr;
    if (!t || !t->periodo || !t->resolucao) return;
    uint32_t frequencia = t->resolucao / t->periodo;
    if (!frequencia) frequencia = 1;
    if (!g->anexado) {
        ledcAttach(g->pino, frequencia, BITS_DO_PWM);
        g->anexado = true;
    }
    uint32_t maximo = (1u << BITS_DO_PWM) - 1;
    uint32_t duty;
    if (g->forcado) duty = g->nivelForcado ? maximo : 0;
    else {
        uint32_t valor = g->comparador ? g->comparador->valor : 0;
        duty = (uint32_t)((uint64_t)valor * maximo / t->periodo);
        if (duty > maximo) duty = maximo;
        if (g->invertido) duty = maximo - duty;
    }
    ledcWrite(g->pino, duty);
}

extern "C" {

esp_err_t mcpwm_new_timer(const mcpwm_timer_config_t *cfg, mcpwm_timer_handle_t *saida)
{
    if (!cfg || !saida || !cfg->resolution_hz || !cfg->period_ticks) return ESP_ERR_INVALID_ARG;
    *saida = new mcpwm_timer_t{cfg->resolution_hz, cfg->period_ticks, false, {}, nullptr};
    return ESP_OK;
}
esp_err_t mcpwm_del_timer(mcpwm_timer_handle_t t)
{
    if (!t) return ESP_ERR_INVALID_ARG;
    delete t;
    return ESP_OK;
}
esp_err_t mcpwm_timer_enable(mcpwm_timer_handle_t t)
{
    if (!t) return ESP_ERR_INVALID_ARG;
    t->ligado = true;
    return ESP_OK;
}
esp_err_t mcpwm_timer_disable(mcpwm_timer_handle_t t)
{
    if (!t) return ESP_ERR_INVALID_ARG;
    t->ligado = false;
    return ESP_OK;
}
esp_err_t mcpwm_timer_start_stop(mcpwm_timer_handle_t t, mcpwm_timer_start_stop_cmd_t cmd)
{
    (void)cmd;
    return t ? ESP_OK : ESP_ERR_INVALID_ARG;
}
esp_err_t mcpwm_timer_set_period(mcpwm_timer_handle_t t, uint32_t period_ticks)
{
    if (!t) return ESP_ERR_INVALID_ARG;
    t->periodo = period_ticks;
    return ESP_OK;
}
esp_err_t mcpwm_timer_register_event_callbacks(mcpwm_timer_handle_t t, const mcpwm_timer_event_callbacks_t *cbs, void *ctx)
{
    if (!t || !cbs) return ESP_ERR_INVALID_ARG;
    t->cbs = *cbs;
    t->ctx = ctx;
    return ESP_OK;
}
esp_err_t mcpwm_new_operator(const mcpwm_operator_config_t *cfg, mcpwm_oper_handle_t *saida)
{
    if (!cfg || !saida) return ESP_ERR_INVALID_ARG;
    *saida = new mcpwm_oper_t{nullptr};
    return ESP_OK;
}
esp_err_t mcpwm_del_operator(mcpwm_oper_handle_t o)
{
    if (!o) return ESP_ERR_INVALID_ARG;
    delete o;
    return ESP_OK;
}
esp_err_t mcpwm_operator_connect_timer(mcpwm_oper_handle_t o, mcpwm_timer_handle_t t)
{
    if (!o || !t) return ESP_ERR_INVALID_ARG;
    o->timer = t;
    return ESP_OK;
}
esp_err_t mcpwm_new_comparator(mcpwm_oper_handle_t o, const mcpwm_comparator_config_t *cfg, mcpwm_cmpr_handle_t *saida)
{
    if (!o || !cfg || !saida) return ESP_ERR_INVALID_ARG;
    auto *c = new mcpwm_cmpr_t();
    c->oper = o;
    c->valor = 0;
    *saida = c;
    return ESP_OK;
}
esp_err_t mcpwm_del_comparator(mcpwm_cmpr_handle_t c)
{
    if (!c) return ESP_ERR_INVALID_ARG;
    delete c;
    return ESP_OK;
}
esp_err_t mcpwm_comparator_set_compare_value(mcpwm_cmpr_handle_t c, uint32_t valor)
{
    if (!c) return ESP_ERR_INVALID_ARG;
    c->valor = valor;
    for (mcpwm_gen_t *g : c->geradores) aplicar(g);
    return ESP_OK;
}
esp_err_t mcpwm_new_generator(mcpwm_oper_handle_t o, const mcpwm_generator_config_t *cfg, mcpwm_gen_handle_t *saida)
{
    if (!o || !cfg || !saida || cfg->gen_gpio_num < 0) return ESP_ERR_INVALID_ARG;
    auto *g = new mcpwm_gen_t();
    g->oper = o;
    g->pino = cfg->gen_gpio_num;
    g->invertido = cfg->flags.invert_pwm;
    *saida = g;
    return ESP_OK;
}
esp_err_t mcpwm_del_generator(mcpwm_gen_handle_t g)
{
    if (!g) return ESP_ERR_INVALID_ARG;
    if (g->anexado) ledcDetach(g->pino);
    delete g;
    return ESP_OK;
}
esp_err_t mcpwm_generator_set_force_level(mcpwm_gen_handle_t g, int level, bool hold_on)
{
    (void)hold_on;
    if (!g) return ESP_ERR_INVALID_ARG;
    g->forcado = level >= 0;
    g->nivelForcado = level > 0;
    aplicar(g);
    return ESP_OK;
}
esp_err_t mcpwm_generator_set_action_on_timer_event(mcpwm_gen_handle_t g, mcpwm_gen_timer_event_action_t ev)
{
    if (!g) return ESP_ERR_INVALID_ARG;
    /* Sobe no comeco do periodo: PWM normal. Desce no comeco: invertido. */
    if (ev.event == MCPWM_TIMER_EVENT_EMPTY) g->invertido = ev.action == MCPWM_GEN_ACTION_LOW;
    return ESP_OK;
}
esp_err_t mcpwm_generator_set_action_on_compare_event(mcpwm_gen_handle_t g, mcpwm_gen_compare_event_action_t ev)
{
    if (!g || !ev.comparator) return ESP_ERR_INVALID_ARG;
    g->comparador = ev.comparator;
    ev.comparator->geradores.push_back(g);
    aplicar(g);
    return ESP_OK;
}
esp_err_t mcpwm_generator_set_action_on_brake_event(mcpwm_gen_handle_t g, mcpwm_gen_brake_event_action_t ev)
{
    (void)ev;
    return g ? ESP_OK : ESP_ERR_INVALID_ARG;
}
esp_err_t mcpwm_generator_set_dead_time(mcpwm_gen_handle_t in, mcpwm_gen_handle_t out, const mcpwm_dead_time_config_t *cfg)
{
    (void)cfg;
    if (!in || !out) return ESP_ERR_INVALID_ARG;
    /* A segunda saida da ponte: o mesmo comparador, invertida se pedir. */
    if (cfg && in != out) {
        out->comparador = in->comparador;
        out->invertido = cfg->flags.invert_output;
        if (in->comparador) in->comparador->geradores.push_back(out);
    }
    return ESP_OK;
}
} /* extern "C" */
#endif
