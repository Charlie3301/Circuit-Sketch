/* cs_idf_i2c.cpp -- os drivers de I2C do ESP-IDF no simulador: o antigo
 * (driver/i2c.h, a lista de comandos) e o novo (driver/i2c_master.h). Passam
 * pelo mesmo barramento do Wire e do Wire1, com os dispositivos do circuito. */
#include "Arduino.h"
#if defined(ESP32)
#include "Wire.h"
#include "driver/i2c_types.h"
#include "driver/i2c_master.h"
#include "esp_check.h"
#include <vector>

namespace {
TwoWire &barramentoDe(int porta)
{
#if SOC_HP_I2C_NUM > 1
    if (porta == 1) return Wire1;
#else
    (void)porta;
#endif
    return Wire;
}
} /* namespace */

/* =====================================================================
 * o driver novo */
struct i2c_master_bus_t {
    int porta;
    int sda, scl;
    uint32_t velocidade;
};
struct i2c_master_dev_t {
    i2c_master_bus_t *bus;
    uint16_t endereco;
    uint32_t velocidade;
    i2c_master_event_callbacks_t cbs;
    void *ctx;
};

static bool portaNovaUsada[SOC_HP_I2C_NUM];

static esp_err_t transmitir(i2c_master_dev_t *d, const uint8_t *dados, size_t n, bool parar)
{
    TwoWire &w = barramentoDe(d->bus->porta);
    if (d->velocidade) w.setClock(d->velocidade);
    w.beginTransmission((uint8_t)d->endereco);
    if (n) w.write(dados, n);
    uint8_t r = w.endTransmission(parar);
    if (r == 0) return ESP_OK;
    return r == 5 ? ESP_ERR_TIMEOUT : ESP_ERR_INVALID_RESPONSE;
}
static esp_err_t receber(i2c_master_dev_t *d, uint8_t *dados, size_t n)
{
    TwoWire &w = barramentoDe(d->bus->porta);
    if (d->velocidade) w.setClock(d->velocidade);
    size_t veio = w.requestFrom((uint8_t)d->endereco, n, true);
    if (veio < n) {
        while (w.available()) w.read();
        return ESP_ERR_INVALID_RESPONSE;
    }
    for (size_t i = 0; i < n; i++) dados[i] = (uint8_t)w.read();
    return ESP_OK;
}

extern "C" {

esp_err_t i2c_new_master_bus(const i2c_master_bus_config_t *cfg, i2c_master_bus_handle_t *saida)
{
    if (!cfg || !saida) return ESP_ERR_INVALID_ARG;
    int porta = cfg->i2c_port;
    if (porta < 0) {
        for (int i = 0; i < SOC_HP_I2C_NUM; i++)
            if (!portaNovaUsada[i]) {
                porta = i;
                break;
            }
        if (porta < 0) return ESP_ERR_NOT_FOUND;
    } else if (porta >= SOC_HP_I2C_NUM) return ESP_ERR_INVALID_ARG;
    else if (portaNovaUsada[porta]) return ESP_ERR_INVALID_STATE;
    if (cfg->sda_io_num < 0 || cfg->scl_io_num < 0) return ESP_ERR_INVALID_ARG;
    portaNovaUsada[porta] = true;
    auto *b = new i2c_master_bus_t{porta, cfg->sda_io_num, cfg->scl_io_num, 100000};
    barramentoDe(porta).begin((int)cfg->sda_io_num, (int)cfg->scl_io_num, 100000);
    *saida = b;
    return ESP_OK;
}
esp_err_t i2c_del_master_bus(i2c_master_bus_handle_t bus)
{
    if (!bus) return ESP_ERR_INVALID_ARG;
    barramentoDe(bus->porta).end();
    portaNovaUsada[bus->porta] = false;
    delete bus;
    return ESP_OK;
}
esp_err_t i2c_master_bus_add_device(i2c_master_bus_handle_t bus, const i2c_device_config_t *cfg, i2c_master_dev_handle_t *saida)
{
    if (!bus || !cfg || !saida) return ESP_ERR_INVALID_ARG;
    auto *d = new i2c_master_dev_t();
    d->bus = bus;
    d->endereco = cfg->device_address;
    d->velocidade = cfg->scl_speed_hz;
    *saida = d;
    return ESP_OK;
}
esp_err_t i2c_master_bus_rm_device(i2c_master_dev_handle_t d)
{
    if (!d) return ESP_ERR_INVALID_ARG;
    delete d;
    return ESP_OK;
}
esp_err_t i2c_master_transmit(i2c_master_dev_handle_t d, const uint8_t *dados, size_t n, int tempo)
{
    (void)tempo;
    if (!d || (!dados && n)) return ESP_ERR_INVALID_ARG;
    esp_err_t e = transmitir(d, dados, n, true);
    if (e == ESP_OK && d->cbs.on_trans_done) {
        i2c_master_event_data_t ev = {};
        d->cbs.on_trans_done(d, &ev, d->ctx);
    }
    return e;
}
esp_err_t i2c_master_receive(i2c_master_dev_handle_t d, uint8_t *dados, size_t n, int tempo)
{
    (void)tempo;
    if (!d || !dados) return ESP_ERR_INVALID_ARG;
    return receber(d, dados, n);
}
esp_err_t i2c_master_transmit_receive(i2c_master_dev_handle_t d, const uint8_t *tx, size_t ntx, uint8_t *rx, size_t nrx, int tempo)
{
    (void)tempo;
    if (!d || !rx) return ESP_ERR_INVALID_ARG;
    esp_err_t e = transmitir(d, tx, ntx, false);
    return e != ESP_OK ? e : receber(d, rx, nrx);
}
esp_err_t i2c_master_multi_buffer_transmit(i2c_master_dev_handle_t d, i2c_master_transmit_multi_buffer_info_t *buffers, size_t quantos, int tempo)
{
    (void)tempo;
    if (!d || !buffers) return ESP_ERR_INVALID_ARG;
    TwoWire &w = barramentoDe(d->bus->porta);
    w.beginTransmission((uint8_t)d->endereco);
    for (size_t i = 0; i < quantos; i++) w.write(buffers[i].write_buffer, buffers[i].buffer_size);
    return w.endTransmission(true) == 0 ? ESP_OK : ESP_ERR_INVALID_RESPONSE;
}
esp_err_t i2c_master_probe(i2c_master_bus_handle_t bus, uint16_t endereco, int tempo)
{
    (void)tempo;
    if (!bus) return ESP_ERR_INVALID_ARG;
    TwoWire &w = barramentoDe(bus->porta);
    w.beginTransmission((uint8_t)endereco);
    return w.endTransmission(true) == 0 ? ESP_OK : ESP_ERR_NOT_FOUND;
}
esp_err_t i2c_master_bus_reset(i2c_master_bus_handle_t bus) { return bus ? ESP_OK : ESP_ERR_INVALID_ARG; }
esp_err_t i2c_master_bus_wait_all_done(i2c_master_bus_handle_t bus, int tempo)
{
    (void)tempo;
    return bus ? ESP_OK : ESP_ERR_INVALID_ARG;
}
esp_err_t i2c_master_register_event_callbacks(i2c_master_dev_handle_t d, const i2c_master_event_callbacks_t *cbs, void *ctx)
{
    if (!d || !cbs) return ESP_ERR_INVALID_ARG;
    d->cbs = *cbs;
    d->ctx = ctx;
    return ESP_OK;
}
esp_err_t i2c_master_device_change_address(i2c_master_dev_handle_t d, uint16_t novo, int tempo)
{
    (void)tempo;
    if (!d) return ESP_ERR_INVALID_ARG;
    d->endereco = novo;
    return ESP_OK;
}
esp_err_t i2c_master_get_bus_handle(i2c_port_num_t porta, i2c_master_bus_handle_t *saida)
{
    (void)porta;
    if (!saida) return ESP_ERR_INVALID_ARG;
    *saida = nullptr;
    return ESP_ERR_INVALID_STATE;
}
esp_err_t i2c_master_execute_defined_operations(i2c_master_dev_handle_t d, i2c_operation_job_t *jobs, size_t n, int tempo)
{
    (void)d;
    (void)jobs;
    (void)n;
    (void)tempo;
    return ESP_ERR_NOT_SUPPORTED;
}
} /* extern "C" */
#endif
