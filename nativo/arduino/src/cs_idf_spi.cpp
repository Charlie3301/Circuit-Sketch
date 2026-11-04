/* cs_idf_spi.cpp -- o driver de SPI mestre do ESP-IDF (driver/spi_master.h): pelo
 * barramento do SPI (SPI2_HOST) e do SPI1 (SPI3_HOST) do Arduino, com o CS, as
 * fases de comando e endereco e a fila de transacoes do driver. */
#include "Arduino.h"
#if defined(ESP32)
#include "SPI.h"
#include "driver/spi_master.h"
#include "esp_heap_caps.h"
#include <deque>

namespace {
struct Barramento {
    bool ligado;
    int dispositivos;
};
Barramento barramentos[SOC_SPI_PERIPH_NUM + 1] = {};

SPIClass &objetoDe(spi_host_device_t host)
{
    return (int)host >= 2 ? SPI1 : SPI;
}
} /* namespace */

struct spi_device_t {
    spi_host_device_t host;
    spi_device_interface_config_t cfg;
    std::deque<spi_transaction_t *> feitas;
    bool preso;
};

static void bytesDe(SPIClass &s, uint64_t valor, int bits, bool lsb)
{
    int bytes = (bits + 7) / 8;
    for (int i = 0; i < bytes; i++) {
        int k = lsb ? i : bytes - 1 - i;
        s.transfer((uint8_t)(valor >> (8 * k)));
    }
}

static esp_err_t executar(spi_device_handle_t d, spi_transaction_t *t)
{
    if (!d || !t) return ESP_ERR_INVALID_ARG;
    if (t->length == 0 && t->rxlength == 0 && !d->cfg.command_bits && !d->cfg.address_bits) return ESP_OK;
    SPIClass &s = objetoDe(d->host);
    bool lsb = d->cfg.flags & SPI_DEVICE_BIT_LSBFIRST;
    s.beginTransaction(SPISettings((uint32_t)d->cfg.clock_speed_hz, lsb ? LSBFIRST : MSBFIRST, d->cfg.mode));
    if (d->cfg.pre_cb) d->cfg.pre_cb(t);
    bool csAlto = d->cfg.flags & SPI_DEVICE_POSITIVE_CS;
    if (d->cfg.spics_io_num >= 0) digitalWrite(d->cfg.spics_io_num, csAlto ? HIGH : LOW);
    int bitsCmd = d->cfg.command_bits, bitsEnd = d->cfg.address_bits, bitsDummy = d->cfg.dummy_bits;
    if (t->flags & (SPI_TRANS_VARIABLE_CMD | SPI_TRANS_VARIABLE_ADDR | SPI_TRANS_VARIABLE_DUMMY)) {
        auto *e = (spi_transaction_ext_t *)t;
        if (t->flags & SPI_TRANS_VARIABLE_CMD) bitsCmd = e->command_bits;
        if (t->flags & SPI_TRANS_VARIABLE_ADDR) bitsEnd = e->address_bits;
        if (t->flags & SPI_TRANS_VARIABLE_DUMMY) bitsDummy = e->dummy_bits;
    }
    if (bitsCmd) bytesDe(s, t->cmd, bitsCmd, lsb);
    if (bitsEnd) bytesDe(s, t->addr, bitsEnd, lsb);
    if (bitsDummy) bytesDe(s, 0, bitsDummy, lsb);

    const uint8_t *tx = (t->flags & SPI_TRANS_USE_TXDATA) ? t->tx_data : (const uint8_t *)t->tx_buffer;
    uint8_t *rx = (t->flags & SPI_TRANS_USE_RXDATA) ? t->rx_data : (uint8_t *)t->rx_buffer;
    size_t bytesTx = (t->length + 7) / 8;
    size_t rxlength = t->rxlength ? t->rxlength : t->length;
    size_t bytesRx = (rxlength + 7) / 8;
    if (d->cfg.flags & SPI_DEVICE_HALFDUPLEX) {
        /* Meio duplex: primeiro escreve, depois le. */
        for (size_t i = 0; tx && i < bytesTx; i++) s.transfer(tx[i]);
        for (size_t i = 0; rx && i < bytesRx; i++) rx[i] = s.transfer(0);
    } else {
        for (size_t i = 0; i < bytesTx; i++) {
            uint8_t v = s.transfer(tx ? tx[i] : 0);
            if (rx && i < bytesRx) rx[i] = v;
        }
    }
    if (d->cfg.spics_io_num >= 0 && !(t->flags & SPI_TRANS_CS_KEEP_ACTIVE)) digitalWrite(d->cfg.spics_io_num, csAlto ? LOW : HIGH);
    if (d->cfg.post_cb) d->cfg.post_cb(t);
    s.endTransaction();
    return ESP_OK;
}

extern "C" {

esp_err_t spi_bus_initialize(spi_host_device_t host, const spi_bus_config_t *cfg, spi_dma_chan_t dma)
{
    (void)dma;
    if ((int)host < 0 || (int)host > SOC_SPI_PERIPH_NUM || !cfg) return ESP_ERR_INVALID_ARG;
    Barramento &b = barramentos[(int)host];
    if (b.ligado) return ESP_ERR_INVALID_STATE;
    objetoDe(host).begin((int8_t)cfg->sclk_io_num, (int8_t)cfg->miso_io_num, (int8_t)cfg->mosi_io_num, -1);
    b.ligado = true;
    return ESP_OK;
}
esp_err_t spi_bus_free(spi_host_device_t host)
{
    if ((int)host < 0 || (int)host > SOC_SPI_PERIPH_NUM) return ESP_ERR_INVALID_ARG;
    Barramento &b = barramentos[(int)host];
    if (!b.ligado || b.dispositivos) return ESP_ERR_INVALID_STATE;
    objetoDe(host).end();
    b.ligado = false;
    return ESP_OK;
}
void *spi_bus_dma_memory_alloc(spi_host_device_t host, size_t size, uint32_t extra_heap_caps)
{
    (void)host;
    (void)extra_heap_caps;
    return malloc(size);
}
esp_err_t spi_bus_add_device(spi_host_device_t host, const spi_device_interface_config_t *cfg, spi_device_handle_t *saida)
{
    if (!cfg || !saida || (int)host < 0 || (int)host > SOC_SPI_PERIPH_NUM) return ESP_ERR_INVALID_ARG;
    if (!barramentos[(int)host].ligado) return ESP_ERR_INVALID_STATE;
    if (barramentos[(int)host].dispositivos >= 6) return ESP_ERR_NOT_FOUND;
    if (cfg->clock_speed_hz <= 0 || cfg->mode > 3) return ESP_ERR_INVALID_ARG;
    auto *d = new spi_device_t();
    d->host = host;
    d->cfg = *cfg;
    if (cfg->spics_io_num >= 0) {
        pinMode(cfg->spics_io_num, OUTPUT);
        digitalWrite(cfg->spics_io_num, (cfg->flags & SPI_DEVICE_POSITIVE_CS) ? LOW : HIGH);
    }
    barramentos[(int)host].dispositivos++;
    *saida = d;
    return ESP_OK;
}
esp_err_t spi_bus_remove_device(spi_device_handle_t d)
{
    if (!d) return ESP_ERR_INVALID_ARG;
    barramentos[(int)d->host].dispositivos--;
    delete d;
    return ESP_OK;
}
esp_err_t spi_device_transmit(spi_device_handle_t d, spi_transaction_t *t) { return executar(d, t); }
esp_err_t spi_device_polling_transmit(spi_device_handle_t d, spi_transaction_t *t) { return executar(d, t); }
esp_err_t spi_device_queue_trans(spi_device_handle_t d, spi_transaction_t *t, TickType_t ticks)
{
    (void)ticks;
    esp_err_t e = executar(d, t);
    if (e == ESP_OK) d->feitas.push_back(t);
    return e;
}
esp_err_t spi_device_get_trans_result(spi_device_handle_t d, spi_transaction_t **t, TickType_t ticks)
{
    (void)ticks;
    if (!d || !t) return ESP_ERR_INVALID_ARG;
    if (d->feitas.empty()) return ESP_ERR_TIMEOUT;
    *t = d->feitas.front();
    d->feitas.pop_front();
    return ESP_OK;
}
esp_err_t spi_device_polling_start(spi_device_handle_t d, spi_transaction_t *t, TickType_t ticks)
{
    (void)ticks;
    return executar(d, t);
}
esp_err_t spi_device_polling_end(spi_device_handle_t d, TickType_t ticks)
{
    (void)ticks;
    return d ? ESP_OK : ESP_ERR_INVALID_ARG;
}
esp_err_t spi_device_acquire_bus(spi_device_handle_t d, TickType_t wait)
{
    (void)wait;
    if (!d) return ESP_ERR_INVALID_ARG;
    d->preso = true;
    return ESP_OK;
}
void spi_device_release_bus(spi_device_handle_t d)
{
    if (d) d->preso = false;
}
esp_err_t spi_device_get_actual_freq(spi_device_handle_t d, int *freq_khz)
{
    if (!d || !freq_khz) return ESP_ERR_INVALID_ARG;
    *freq_khz = d->cfg.clock_speed_hz / 1000;
    return ESP_OK;
}
esp_err_t spi_bus_get_max_transaction_len(spi_host_device_t host, size_t *max_bytes)
{
    (void)host;
    if (!max_bytes) return ESP_ERR_INVALID_ARG;
    *max_bytes = 4092;
    return ESP_OK;
}
int spi_get_actual_clock(int fapb, int hz, int duty_cycle)
{
    (void)fapb;
    (void)duty_cycle;
    return hz;
}
void spi_get_timing(bool gpio_is_used, int input_delay_ns, int eff_clk, int *dummy_o, int *cycles_remain_o)
{
    (void)gpio_is_used;
    (void)input_delay_ns;
    (void)eff_clk;
    if (dummy_o) *dummy_o = 0;
    if (cycles_remain_o) *cycles_remain_o = 0;
}
int spi_get_freq_limit(bool gpio_is_used, int input_delay_ns)
{
    (void)input_delay_ns;
    return gpio_is_used ? 40000000 : 80000000;
}
} /* extern "C" */
#endif
