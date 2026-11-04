/* cs_idf_rmt.cpp -- o RMT novo do ESP-IDF (driver/rmt_tx.h, rmt_rx.h, rmt_encoder.h)
 * sobre o esp32-hal-rmt do simulador. Os codificadores sao os da placa (bytes e
 * copia) e rodam igual, entao um codificador do programa -- o do led_strip, o de
 * um controle remoto -- funciona: cada um escreve os simbolos no canal. */
#include "Arduino.h"
#if defined(ESP32) && SOC_RMT_SUPPORTED
#include "driver/rmt_tx.h"
#include "driver/rmt_rx.h"
#include "driver/rmt_encoder.h"
#include "esp32-hal-rmt.h"
#include "freertos/task.h"
#include <vector>

struct rmt_channel_t {
    int pino;
    uint32_t resolucao;
    bool rx, ligado;
    std::vector<rmt_symbol_word_t> simbolos;
    rmt_tx_event_callbacks_t cbTx;
    rmt_rx_event_callbacks_t cbRx;
    void *ctx;
    int nivelFinal;
};

/* O codificador escreve no canal que esta transmitindo. */
static rmt_channel_t *canalAtual = nullptr;

namespace {
struct CodificadorDeBytes {
    rmt_encoder_t base;
    rmt_bytes_encoder_config_t cfg;
};
struct CodificadorDeCopia {
    rmt_encoder_t base;
};
struct CodificadorSimples {
    rmt_encoder_t base;
    rmt_simple_encoder_config_t cfg;
};

size_t codificarBytes(rmt_encoder_t *e, rmt_channel_handle_t canal, const void *dados, size_t n, rmt_encode_state_t *estado)
{
    auto *c = (CodificadorDeBytes *)e;
    const uint8_t *b = (const uint8_t *)dados;
    for (size_t i = 0; i < n; i++)
        for (int k = 0; k < 8; k++) {
            int bit = c->cfg.flags.msb_first ? (b[i] >> (7 - k)) & 1 : (b[i] >> k) & 1;
            canal->simbolos.push_back(bit ? c->cfg.bit1 : c->cfg.bit0);
        }
    *estado = RMT_ENCODING_COMPLETE;
    return n;
}
size_t codificarCopia(rmt_encoder_t *e, rmt_channel_handle_t canal, const void *dados, size_t n, rmt_encode_state_t *estado)
{
    (void)e;
    const rmt_symbol_word_t *s = (const rmt_symbol_word_t *)dados;
    for (size_t i = 0; i < n / sizeof(rmt_symbol_word_t); i++) canal->simbolos.push_back(s[i]);
    *estado = RMT_ENCODING_COMPLETE;
    return n;
}
size_t codificarSimples(rmt_encoder_t *e, rmt_channel_handle_t canal, const void *dados, size_t n, rmt_encode_state_t *estado)
{
    auto *c = (CodificadorSimples *)e;
    /* O callback do programa escreve um pedaco de cada vez no espaco que recebe. */
    rmt_symbol_word_t espaco[256];
    size_t feito = 0;
    bool fim = false;
    while (!fim) {
        size_t escritos = c->cfg.callback(dados, n, feito, 256, espaco, &fim, c->cfg.arg);
        for (size_t i = 0; i < escritos; i++) canal->simbolos.push_back(espaco[i]);
        feito += escritos;
        if (!escritos && !fim) break;
    }
    *estado = RMT_ENCODING_COMPLETE;
    return n;
}
esp_err_t soReiniciar(rmt_encoder_t *e)
{
    (void)e;
    return ESP_OK;
}
esp_err_t apagarBytes(rmt_encoder_t *e)
{
    delete (CodificadorDeBytes *)e;
    return ESP_OK;
}
esp_err_t apagarCopia(rmt_encoder_t *e)
{
    delete (CodificadorDeCopia *)e;
    return ESP_OK;
}
esp_err_t apagarSimples(rmt_encoder_t *e)
{
    delete (CodificadorSimples *)e;
    return ESP_OK;
}
} /* namespace */

extern "C" {

esp_err_t rmt_new_tx_channel(const rmt_tx_channel_config_t *cfg, rmt_channel_handle_t *saida)
{
    if (!cfg || !saida || cfg->gpio_num < 0 || !cfg->resolution_hz) return ESP_ERR_INVALID_ARG;
    auto *c = new rmt_channel_t();
    c->pino = cfg->gpio_num;
    c->resolucao = cfg->resolution_hz;
    if (!rmtInit(c->pino, RMT_TX_MODE, RMT_MEM_NUM_BLOCKS_1, c->resolucao)) {
        delete c;
        return ESP_ERR_NOT_FOUND;
    }
    *saida = c;
    return ESP_OK;
}
esp_err_t rmt_new_rx_channel(const rmt_rx_channel_config_t *cfg, rmt_channel_handle_t *saida)
{
    if (!cfg || !saida || cfg->gpio_num < 0 || !cfg->resolution_hz) return ESP_ERR_INVALID_ARG;
    auto *c = new rmt_channel_t();
    c->pino = cfg->gpio_num;
    c->resolucao = cfg->resolution_hz;
    c->rx = true;
    if (!rmtInit(c->pino, RMT_RX_MODE, RMT_MEM_NUM_BLOCKS_1, c->resolucao)) {
        delete c;
        return ESP_ERR_NOT_FOUND;
    }
    *saida = c;
    return ESP_OK;
}
esp_err_t rmt_del_channel(rmt_channel_handle_t c)
{
    if (!c) return ESP_ERR_INVALID_ARG;
    if (c->ligado) return ESP_ERR_INVALID_STATE;
    rmtDeinit(c->pino);
    delete c;
    return ESP_OK;
}
esp_err_t rmt_enable(rmt_channel_handle_t c)
{
    if (!c) return ESP_ERR_INVALID_ARG;
    if (c->ligado) return ESP_ERR_INVALID_STATE;
    c->ligado = true;
    return ESP_OK;
}
esp_err_t rmt_disable(rmt_channel_handle_t c)
{
    if (!c) return ESP_ERR_INVALID_ARG;
    if (!c->ligado) return ESP_ERR_INVALID_STATE;
    c->ligado = false;
    return ESP_OK;
}
esp_err_t rmt_apply_carrier(rmt_channel_handle_t c, const rmt_carrier_config_t *cfg)
{
    if (!c) return ESP_ERR_INVALID_ARG;
    if (cfg) rmtSetCarrier(c->pino, true, cfg->flags.polarity_active_low, cfg->frequency_hz, cfg->duty_cycle);
    return ESP_OK;
}
esp_err_t rmt_tx_register_event_callbacks(rmt_channel_handle_t c, const rmt_tx_event_callbacks_t *cbs, void *ctx)
{
    if (!c || !cbs) return ESP_ERR_INVALID_ARG;
    c->cbTx = *cbs;
    c->ctx = ctx;
    return ESP_OK;
}
esp_err_t rmt_rx_register_event_callbacks(rmt_channel_handle_t c, const rmt_rx_event_callbacks_t *cbs, void *ctx)
{
    if (!c || !cbs) return ESP_ERR_INVALID_ARG;
    c->cbRx = *cbs;
    c->ctx = ctx;
    return ESP_OK;
}
esp_err_t rmt_transmit(rmt_channel_handle_t c, rmt_encoder_handle_t e, const void *dados, size_t n, const rmt_transmit_config_t *cfg)
{
    if (!c || !e || (!dados && n) || !cfg) return ESP_ERR_INVALID_ARG;
    if (!c->ligado) return ESP_ERR_INVALID_STATE;
    canalAtual = c;
    c->simbolos.clear();
    e->reset(e);
    rmt_encode_state_t estado = RMT_ENCODING_RESET;
    size_t feito = 0;
    int voltas = 0;
    while (!(estado & RMT_ENCODING_COMPLETE) && voltas++ < 100000) {
        estado = RMT_ENCODING_RESET;
        feito += e->encode(e, c, (const uint8_t *)dados + feito, n - feito, &estado);
        if (estado & RMT_ENCODING_MEM_FULL) continue;
    }
    int repeticoes = cfg->loop_count > 0 ? cfg->loop_count : 1;
    for (int i = 0; i < repeticoes; i++) rmtWrite(c->pino, (rmt_data_t *)c->simbolos.data(), c->simbolos.size(), RMT_WAIT_FOR_EVER);
    if (cfg->flags.eot_level) digitalWrite(c->pino, HIGH);
    if (c->cbTx.on_trans_done) {
        rmt_tx_done_event_data_t ev = {};
        ev.num_symbols = c->simbolos.size();
        c->cbTx.on_trans_done(c, &ev, c->ctx);
    }
    return ESP_OK;
}
esp_err_t rmt_tx_wait_all_done(rmt_channel_handle_t c, int timeout_ms)
{
    (void)timeout_ms;
    return c ? ESP_OK : ESP_ERR_INVALID_ARG;
}
esp_err_t rmt_receive(rmt_channel_handle_t c, void *buffer, size_t tamanho, const rmt_receive_config_t *cfg)
{
    if (!c || !buffer || !cfg) return ESP_ERR_INVALID_ARG;
    if (!c->ligado) return ESP_ERR_INVALID_STATE;
    size_t n = tamanho / sizeof(rmt_symbol_word_t);
    rmtRead(c->pino, (rmt_data_t *)buffer, &n, RMT_WAIT_FOR_EVER);
    if (c->cbRx.on_recv_done) {
        rmt_rx_done_event_data_t ev = {};
        ev.received_symbols = (rmt_symbol_word_t *)buffer;
        ev.num_symbols = n;
        c->cbRx.on_recv_done(c, &ev, c->ctx);
    }
    return ESP_OK;
}

esp_err_t rmt_new_bytes_encoder(const rmt_bytes_encoder_config_t *cfg, rmt_encoder_handle_t *saida)
{
    if (!cfg || !saida) return ESP_ERR_INVALID_ARG;
    auto *c = new CodificadorDeBytes();
    c->cfg = *cfg;
    c->base.encode = codificarBytes;
    c->base.reset = soReiniciar;
    c->base.del = apagarBytes;
    *saida = &c->base;
    return ESP_OK;
}
esp_err_t rmt_bytes_encoder_update_config(rmt_encoder_handle_t e, const rmt_bytes_encoder_config_t *cfg)
{
    if (!e || !cfg) return ESP_ERR_INVALID_ARG;
    ((CodificadorDeBytes *)e)->cfg = *cfg;
    return ESP_OK;
}
esp_err_t rmt_new_copy_encoder(const rmt_copy_encoder_config_t *cfg, rmt_encoder_handle_t *saida)
{
    if (!cfg || !saida) return ESP_ERR_INVALID_ARG;
    auto *c = new CodificadorDeCopia();
    c->base.encode = codificarCopia;
    c->base.reset = soReiniciar;
    c->base.del = apagarCopia;
    *saida = &c->base;
    return ESP_OK;
}
esp_err_t rmt_new_simple_encoder(const rmt_simple_encoder_config_t *cfg, rmt_encoder_handle_t *saida)
{
    if (!cfg || !saida || !cfg->callback) return ESP_ERR_INVALID_ARG;
    auto *c = new CodificadorSimples();
    c->cfg = *cfg;
    c->base.encode = codificarSimples;
    c->base.reset = soReiniciar;
    c->base.del = apagarSimples;
    *saida = &c->base;
    return ESP_OK;
}
esp_err_t rmt_del_encoder(rmt_encoder_handle_t e)
{
    if (!e) return ESP_ERR_INVALID_ARG;
    return e->del(e);
}
esp_err_t rmt_encoder_reset(rmt_encoder_handle_t e)
{
    if (!e) return ESP_ERR_INVALID_ARG;
    return e->reset(e);
}
void *rmt_alloc_encoder_mem(size_t size) { return calloc(1, size); }
} /* extern "C" */
#endif
