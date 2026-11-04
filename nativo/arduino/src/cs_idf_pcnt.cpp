/* cs_idf_pcnt.cpp -- o contador de pulsos novo do ESP-IDF (driver/pulse_cnt.h): cada
 * canal olha o pino de borda por interrupcao e soma ou subtrai pela acao que o
 * programa configurou, olhando o pino de nivel, como no encoder de quadratura. */
#include "Arduino.h"
#if defined(ESP32) && SOC_PCNT_SUPPORTED
#include "driver/pulse_cnt.h"
#include <vector>

struct pcnt_unit_t {
    int baixo, alto;
    volatile int contagem;
    bool ligada, rodando;
    std::vector<struct pcnt_chan_t *> canais;
    std::vector<int> pontos;
    pcnt_event_callbacks_t cbs;
    void *ctx;
};
struct pcnt_chan_t {
    pcnt_unit_t *unidade;
    int pinoDeBorda, pinoDeNivel;
    pcnt_channel_edge_action_t subida, descida;
    pcnt_channel_level_action_t nivelAlto, nivelBaixo;
    bool anterior;
};

static void IRAM_ATTR aoMudar(void *arg)
{
    auto *c = (pcnt_chan_t *)arg;
    pcnt_unit_t *u = c->unidade;
    bool agora = digitalRead(c->pinoDeBorda);
    if (agora == c->anterior) return;
    c->anterior = agora;
    if (!u->rodando) return;
    pcnt_channel_edge_action_t acao = agora ? c->subida : c->descida;
    int passo = acao == PCNT_CHANNEL_EDGE_ACTION_INCREASE ? 1 : acao == PCNT_CHANNEL_EDGE_ACTION_DECREASE ? -1 : 0;
    if (c->pinoDeNivel >= 0) {
        pcnt_channel_level_action_t n = digitalRead(c->pinoDeNivel) ? c->nivelAlto : c->nivelBaixo;
        if (n == PCNT_CHANNEL_LEVEL_ACTION_INVERSE) passo = -passo;
        else if (n == PCNT_CHANNEL_LEVEL_ACTION_HOLD) passo = 0;
    }
    if (!passo) return;
    int v = u->contagem + passo;
    /* Passou do limite: volta a zero, como o contador da placa. */
    if (v >= u->alto || v <= u->baixo) {
        if (u->cbs.on_reach) {
            pcnt_watch_event_data_t ev = {};
            ev.watch_point_value = v;
            ev.zero_cross_mode = PCNT_UNIT_ZERO_CROSS_INVALID;
            u->cbs.on_reach(u, &ev, u->ctx);
        }
        v = 0;
    }
    u->contagem = v;
    for (int p : u->pontos)
        if (p == v && u->cbs.on_reach) {
            pcnt_watch_event_data_t ev = {};
            ev.watch_point_value = v;
            ev.zero_cross_mode = PCNT_UNIT_ZERO_CROSS_INVALID;
            u->cbs.on_reach(u, &ev, u->ctx);
        }
}

extern "C" {

esp_err_t pcnt_new_unit(const pcnt_unit_config_t *cfg, pcnt_unit_handle_t *saida)
{
    if (!cfg || !saida || cfg->high_limit <= 0 || cfg->low_limit >= 0) return ESP_ERR_INVALID_ARG;
    auto *u = new pcnt_unit_t();
    u->baixo = cfg->low_limit;
    u->alto = cfg->high_limit;
    *saida = u;
    return ESP_OK;
}
esp_err_t pcnt_del_unit(pcnt_unit_handle_t u)
{
    if (!u) return ESP_ERR_INVALID_ARG;
    if (u->ligada || !u->canais.empty()) return ESP_ERR_INVALID_STATE;
    delete u;
    return ESP_OK;
}
esp_err_t pcnt_unit_set_glitch_filter(pcnt_unit_handle_t u, const pcnt_glitch_filter_config_t *cfg)
{
    (void)cfg;
    return u ? ESP_OK : ESP_ERR_INVALID_ARG;
}
esp_err_t pcnt_unit_enable(pcnt_unit_handle_t u)
{
    if (!u) return ESP_ERR_INVALID_ARG;
    if (u->ligada) return ESP_ERR_INVALID_STATE;
    u->ligada = true;
    return ESP_OK;
}
esp_err_t pcnt_unit_disable(pcnt_unit_handle_t u)
{
    if (!u) return ESP_ERR_INVALID_ARG;
    if (!u->ligada) return ESP_ERR_INVALID_STATE;
    u->ligada = false;
    return ESP_OK;
}
esp_err_t pcnt_unit_start(pcnt_unit_handle_t u)
{
    if (!u) return ESP_ERR_INVALID_ARG;
    if (!u->ligada) return ESP_ERR_INVALID_STATE;
    u->rodando = true;
    return ESP_OK;
}
esp_err_t pcnt_unit_stop(pcnt_unit_handle_t u)
{
    if (!u) return ESP_ERR_INVALID_ARG;
    if (!u->ligada) return ESP_ERR_INVALID_STATE;
    u->rodando = false;
    return ESP_OK;
}
esp_err_t pcnt_unit_clear_count(pcnt_unit_handle_t u)
{
    if (!u) return ESP_ERR_INVALID_ARG;
    u->contagem = 0;
    return ESP_OK;
}
esp_err_t pcnt_unit_get_count(pcnt_unit_handle_t u, int *valor)
{
    if (!u || !valor) return ESP_ERR_INVALID_ARG;
    *valor = u->contagem;
    return ESP_OK;
}
esp_err_t pcnt_unit_register_event_callbacks(pcnt_unit_handle_t u, const pcnt_event_callbacks_t *cbs, void *ctx)
{
    if (!u || !cbs) return ESP_ERR_INVALID_ARG;
    if (u->ligada) return ESP_ERR_INVALID_STATE;
    u->cbs = *cbs;
    u->ctx = ctx;
    return ESP_OK;
}
esp_err_t pcnt_unit_add_watch_point(pcnt_unit_handle_t u, int ponto)
{
    if (!u || ponto >= u->alto + 1 || ponto <= u->baixo - 1) return ESP_ERR_INVALID_ARG;
    for (int p : u->pontos)
        if (p == ponto) return ESP_ERR_INVALID_STATE;
    u->pontos.push_back(ponto);
    return ESP_OK;
}
esp_err_t pcnt_unit_remove_watch_point(pcnt_unit_handle_t u, int ponto)
{
    if (!u) return ESP_ERR_INVALID_ARG;
    for (size_t i = 0; i < u->pontos.size(); i++)
        if (u->pontos[i] == ponto) {
            u->pontos.erase(u->pontos.begin() + (long)i);
            return ESP_OK;
        }
    return ESP_ERR_INVALID_STATE;
}
esp_err_t pcnt_new_channel(pcnt_unit_handle_t u, const pcnt_chan_config_t *cfg, pcnt_channel_handle_t *saida)
{
    if (!u || !cfg || !saida) return ESP_ERR_INVALID_ARG;
    if (u->ligada) return ESP_ERR_INVALID_STATE;
    auto *c = new pcnt_chan_t();
    c->unidade = u;
    c->pinoDeBorda = cfg->edge_gpio_num;
    c->pinoDeNivel = cfg->level_gpio_num;
    if (c->pinoDeBorda >= 0) {
        pinMode(c->pinoDeBorda, INPUT);
        c->anterior = digitalRead(c->pinoDeBorda);
        attachInterruptArg(c->pinoDeBorda, aoMudar, c, CHANGE);
    }
    if (c->pinoDeNivel >= 0) pinMode(c->pinoDeNivel, INPUT);
    u->canais.push_back(c);
    *saida = c;
    return ESP_OK;
}
esp_err_t pcnt_del_channel(pcnt_channel_handle_t c)
{
    if (!c) return ESP_ERR_INVALID_ARG;
    if (c->pinoDeBorda >= 0) detachInterrupt(c->pinoDeBorda);
    auto &v = c->unidade->canais;
    for (size_t i = 0; i < v.size(); i++)
        if (v[i] == c) {
            v.erase(v.begin() + (long)i);
            break;
        }
    delete c;
    return ESP_OK;
}
esp_err_t pcnt_channel_set_edge_action(pcnt_channel_handle_t c, pcnt_channel_edge_action_t pos_act, pcnt_channel_edge_action_t neg_act)
{
    if (!c) return ESP_ERR_INVALID_ARG;
    c->subida = pos_act;
    c->descida = neg_act;
    return ESP_OK;
}
esp_err_t pcnt_channel_set_level_action(pcnt_channel_handle_t c, pcnt_channel_level_action_t high_act, pcnt_channel_level_action_t low_act)
{
    if (!c) return ESP_ERR_INVALID_ARG;
    c->nivelAlto = high_act;
    c->nivelBaixo = low_act;
    return ESP_OK;
}
} /* extern "C" */
#endif
