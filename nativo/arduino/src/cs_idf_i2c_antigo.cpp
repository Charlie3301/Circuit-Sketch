/* cs_idf_i2c_antigo.cpp -- o driver antigo de I2C do ESP-IDF (driver/i2c.h): a
 * lista de comandos (start, write, read, stop) roda no i2c_master_cmd_begin, no
 * barramento do Wire. A placa escrava nao existe no simulador. */
#include "Arduino.h"
#if defined(ESP32)
#include "Wire.h"
#include "driver/i2c.h"
#include <vector>

namespace {
struct Porta {
    i2c_config_t cfg;
    bool configurada, instalada;
};
Porta portas[SOC_HP_I2C_NUM] = {};

TwoWire &barramentoDe(int porta)
{
#if SOC_HP_I2C_NUM > 1
    if (porta == 1) return Wire1;
#else
    (void)porta;
#endif
    return Wire;
}

enum Tipo { INICIO, ESCREVER, LER, PARAR };
struct Comando {
    Tipo tipo;
    std::vector<uint8_t> dados;
    uint8_t *destino;
    size_t n;
    i2c_ack_type_t ack;
    bool checarAck;
};
struct Lista {
    std::vector<Comando> comandos;
};
} /* namespace */

extern "C" {

esp_err_t i2c_param_config(i2c_port_t i2c_num, const i2c_config_t *cfg)
{
    if ((int)i2c_num < 0 || (int)i2c_num >= SOC_HP_I2C_NUM || !cfg) return ESP_ERR_INVALID_ARG;
    if (cfg->mode == I2C_MODE_SLAVE) return ESP_OK;
    portas[i2c_num].cfg = *cfg;
    portas[i2c_num].configurada = true;
    if (portas[i2c_num].instalada) barramentoDe(i2c_num).begin((int)cfg->sda_io_num, (int)cfg->scl_io_num, cfg->master.clk_speed);
    return ESP_OK;
}
esp_err_t i2c_driver_install(i2c_port_t i2c_num, i2c_mode_t mode, size_t slv_rx_buf_len, size_t slv_tx_buf_len, int intr_alloc_flags)
{
    (void)slv_rx_buf_len;
    (void)slv_tx_buf_len;
    (void)intr_alloc_flags;
    if ((int)i2c_num < 0 || (int)i2c_num >= SOC_HP_I2C_NUM) return ESP_ERR_INVALID_ARG;
    if (portas[i2c_num].instalada) return ESP_FAIL;
    if (mode == I2C_MODE_SLAVE) {
        log_e("o simulador nao tem a placa como escrava de I2C");
        return ESP_ERR_NOT_SUPPORTED;
    }
    Porta &p = portas[i2c_num];
    if (p.configurada) barramentoDe(i2c_num).begin((int)p.cfg.sda_io_num, (int)p.cfg.scl_io_num, p.cfg.master.clk_speed);
    else barramentoDe(i2c_num).begin();
    p.instalada = true;
    return ESP_OK;
}
esp_err_t i2c_driver_delete(i2c_port_t i2c_num)
{
    if ((int)i2c_num < 0 || (int)i2c_num >= SOC_HP_I2C_NUM) return ESP_ERR_INVALID_ARG;
    barramentoDe(i2c_num).end();
    portas[i2c_num].instalada = false;
    return ESP_OK;
}
esp_err_t i2c_set_pin(i2c_port_t i2c_num, int sda_io_num, int scl_io_num, bool sda_pullup_en, bool scl_pullup_en, i2c_mode_t mode)
{
    (void)sda_pullup_en;
    (void)scl_pullup_en;
    (void)mode;
    if ((int)i2c_num < 0 || (int)i2c_num >= SOC_HP_I2C_NUM) return ESP_ERR_INVALID_ARG;
    portas[i2c_num].cfg.sda_io_num = sda_io_num;
    portas[i2c_num].cfg.scl_io_num = scl_io_num;
    return ESP_OK;
}

i2c_cmd_handle_t i2c_cmd_link_create(void) { return new Lista(); }
i2c_cmd_handle_t i2c_cmd_link_create_static(uint8_t *buffer, uint32_t size)
{
    (void)buffer;
    (void)size;
    return new Lista();
}
void i2c_cmd_link_delete(i2c_cmd_handle_t c) { delete (Lista *)c; }
void i2c_cmd_link_delete_static(i2c_cmd_handle_t c) { delete (Lista *)c; }

static esp_err_t acrescentar(i2c_cmd_handle_t c, Comando k)
{
    if (!c) return ESP_ERR_INVALID_ARG;
    ((Lista *)c)->comandos.push_back(std::move(k));
    return ESP_OK;
}
esp_err_t i2c_master_start(i2c_cmd_handle_t c) { return acrescentar(c, {INICIO, {}, nullptr, 0, I2C_MASTER_ACK, false}); }
esp_err_t i2c_master_stop(i2c_cmd_handle_t c) { return acrescentar(c, {PARAR, {}, nullptr, 0, I2C_MASTER_ACK, false}); }
esp_err_t i2c_master_write_byte(i2c_cmd_handle_t c, uint8_t dado, bool ack_en) { return acrescentar(c, {ESCREVER, {dado}, nullptr, 0, I2C_MASTER_ACK, ack_en}); }
esp_err_t i2c_master_write(i2c_cmd_handle_t c, const uint8_t *dados, size_t n, bool ack_en)
{
    if (!dados && n) return ESP_ERR_INVALID_ARG;
    return acrescentar(c, {ESCREVER, std::vector<uint8_t>(dados, dados + n), nullptr, 0, I2C_MASTER_ACK, ack_en});
}
esp_err_t i2c_master_read_byte(i2c_cmd_handle_t c, uint8_t *dado, i2c_ack_type_t ack) { return acrescentar(c, {LER, {}, dado, 1, ack, false}); }
esp_err_t i2c_master_read(i2c_cmd_handle_t c, uint8_t *dados, size_t n, i2c_ack_type_t ack)
{
    if (!dados || !n) return ESP_ERR_INVALID_ARG;
    return acrescentar(c, {LER, {}, dados, n, ack, false});
}

/* Roda a lista: cada INICIO abre uma transacao. O primeiro byte depois dele e o endereco. */
esp_err_t i2c_master_cmd_begin(i2c_port_t i2c_num, i2c_cmd_handle_t cmd, TickType_t ticks)
{
    (void)ticks;
    if ((int)i2c_num < 0 || (int)i2c_num >= SOC_HP_I2C_NUM || !cmd) return ESP_ERR_INVALID_ARG;
    if (!portas[i2c_num].instalada) return ESP_ERR_INVALID_STATE;
    TwoWire &w = barramentoDe(i2c_num);
    auto &lista = ((Lista *)cmd)->comandos;
    size_t i = 0;
    while (i < lista.size()) {
        if (lista[i].tipo != INICIO) {
            i++;
            continue;
        }
        i++;
        /* Os bytes de escrita e os pedidos de leitura ate o proximo INICIO ou PARAR. */
        std::vector<uint8_t> escrita;
        std::vector<Comando *> leituras;
        size_t total = 0;
        bool parar = false, checar = false;
        while (i < lista.size() && lista[i].tipo != INICIO) {
            if (lista[i].tipo == ESCREVER) {
                escrita.insert(escrita.end(), lista[i].dados.begin(), lista[i].dados.end());
                checar = checar || lista[i].checarAck;
            } else if (lista[i].tipo == LER) {
                leituras.push_back(&lista[i]);
                total += lista[i].n;
            } else if (lista[i].tipo == PARAR) {
                parar = true;
                i++;
                break;
            }
            i++;
        }
        if (escrita.empty()) return ESP_ERR_INVALID_STATE;
        uint8_t endereco = escrita[0] >> 1;
        bool leitura = escrita[0] & 1;
        if (leitura) {
            size_t veio = w.requestFrom(endereco, total, parar);
            if (veio < total && checar) return ESP_FAIL;
            for (Comando *l : leituras)
                for (size_t k = 0; k < l->n; k++) l->destino[k] = w.available() ? (uint8_t)w.read() : 0xFF;
        } else {
            w.beginTransmission(endereco);
            if (escrita.size() > 1) w.write(escrita.data() + 1, escrita.size() - 1);
            uint8_t r = w.endTransmission(parar);
            if (r == 5) return ESP_ERR_TIMEOUT;
            if (r && checar) return ESP_FAIL;
        }
    }
    return ESP_OK;
}

esp_err_t i2c_master_write_to_device(i2c_port_t i2c_num, uint8_t endereco, const uint8_t *dados, size_t n, TickType_t ticks)
{
    i2c_cmd_handle_t c = i2c_cmd_link_create();
    i2c_master_start(c);
    i2c_master_write_byte(c, (uint8_t)(endereco << 1), true);
    i2c_master_write(c, dados, n, true);
    i2c_master_stop(c);
    esp_err_t e = i2c_master_cmd_begin(i2c_num, c, ticks);
    i2c_cmd_link_delete(c);
    return e;
}
esp_err_t i2c_master_read_from_device(i2c_port_t i2c_num, uint8_t endereco, uint8_t *dados, size_t n, TickType_t ticks)
{
    i2c_cmd_handle_t c = i2c_cmd_link_create();
    i2c_master_start(c);
    i2c_master_write_byte(c, (uint8_t)(endereco << 1 | 1), true);
    i2c_master_read(c, dados, n, I2C_MASTER_LAST_NACK);
    i2c_master_stop(c);
    esp_err_t e = i2c_master_cmd_begin(i2c_num, c, ticks);
    i2c_cmd_link_delete(c);
    return e;
}
esp_err_t i2c_master_write_read_device(i2c_port_t i2c_num, uint8_t endereco, const uint8_t *escrita, size_t ne, uint8_t *leitura, size_t nl, TickType_t ticks)
{
    i2c_cmd_handle_t c = i2c_cmd_link_create();
    i2c_master_start(c);
    i2c_master_write_byte(c, (uint8_t)(endereco << 1), true);
    i2c_master_write(c, escrita, ne, true);
    i2c_master_start(c);
    i2c_master_write_byte(c, (uint8_t)(endereco << 1 | 1), true);
    i2c_master_read(c, leitura, nl, I2C_MASTER_LAST_NACK);
    i2c_master_stop(c);
    esp_err_t e = i2c_master_cmd_begin(i2c_num, c, ticks);
    i2c_cmd_link_delete(c);
    return e;
}
int i2c_slave_write_buffer(i2c_port_t i2c_num, const uint8_t *data, int size, TickType_t ticks)
{
    (void)i2c_num;
    (void)data;
    (void)size;
    (void)ticks;
    return -1;
}
int i2c_slave_read_buffer(i2c_port_t i2c_num, uint8_t *data, size_t max_size, TickType_t ticks)
{
    (void)i2c_num;
    (void)data;
    (void)max_size;
    (void)ticks;
    return -1;
}
/* Os ajustes de tempo do barramento: guardados de faz de conta, sem efeito na simulacao. */
#define CS_AJUSTE(nome, ...)                    \
    esp_err_t nome(i2c_port_t n, ##__VA_ARGS__) \
    {                                           \
        (void)n;                                \
        return ESP_OK;                          \
    }
CS_AJUSTE(i2c_reset_tx_fifo)
CS_AJUSTE(i2c_reset_rx_fifo)
CS_AJUSTE(i2c_set_period, int high_period, int low_period)
CS_AJUSTE(i2c_filter_enable, uint8_t cyc_num)
CS_AJUSTE(i2c_filter_disable)
CS_AJUSTE(i2c_set_start_timing, int setup_time, int hold_time)
CS_AJUSTE(i2c_set_stop_timing, int setup_time, int hold_time)
CS_AJUSTE(i2c_set_data_timing, int sample_time, int hold_time)
CS_AJUSTE(i2c_set_timeout, int timeout)
CS_AJUSTE(i2c_set_data_mode, i2c_trans_mode_t tx_trans_mode, i2c_trans_mode_t rx_trans_mode)
esp_err_t i2c_get_period(i2c_port_t n, int *high, int *low)
{
    (void)n;
    *high = 400;
    *low = 400;
    return ESP_OK;
}
esp_err_t i2c_get_start_timing(i2c_port_t n, int *setup, int *hold)
{
    (void)n;
    *setup = 400;
    *hold = 400;
    return ESP_OK;
}
esp_err_t i2c_get_stop_timing(i2c_port_t n, int *setup, int *hold)
{
    (void)n;
    *setup = 400;
    *hold = 400;
    return ESP_OK;
}
esp_err_t i2c_get_data_timing(i2c_port_t n, int *sample, int *hold)
{
    (void)n;
    *sample = 250;
    *hold = 250;
    return ESP_OK;
}
esp_err_t i2c_get_timeout(i2c_port_t n, int *timeout)
{
    (void)n;
    *timeout = 8000;
    return ESP_OK;
}
esp_err_t i2c_get_data_mode(i2c_port_t n, i2c_trans_mode_t *tx, i2c_trans_mode_t *rx)
{
    (void)n;
    *tx = I2C_DATA_MODE_MSB_FIRST;
    *rx = I2C_DATA_MODE_MSB_FIRST;
    return ESP_OK;
}
} /* extern "C" */
#endif
