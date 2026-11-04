/* cs_idf_uart.cpp -- o driver de UART do ESP-IDF (driver/uart.h) no simulador:
 * as mesmas portas do Serial, Serial1 e Serial2 do Arduino (esp32-hal-uart). A
 * fila de eventos e a da placa: UART_DATA chega quando entra dado na porta. */
#include "Arduino.h"
#if defined(ESP32)
#include "driver/uart.h"
#include "esp32-hal-uart.h"
#include "freertos/queue.h"
#include "freertos/task.h"

namespace {
struct Porta {
    uart_config_t cfg;
    bool configurada, instalada;
    int tx, rx, rts, cts;
    uart_t *hal;
    QueueHandle_t fila;
    TaskHandle_t vigia;
    uint32_t tamanhoRx;
    bool inverter;
    uart_mode_t modo;
    uart_hw_flowcontrol_t fluxo;
};
Porta portas[SOC_UART_NUM] = {};

Porta *portaDe(uart_port_t n)
{
    if ((int)n < 0 || (int)n >= SOC_UART_NUM) return nullptr;
    return &portas[(int)n];
}

/* O config do Arduino: 0x8000000 | dados << 2 | parada << 4 | paridade. */
uint32_t configDoArduino(const uart_config_t &c)
{
    return 0x8000000u | ((uint32_t)c.data_bits << 2) | ((uint32_t)c.stop_bits << 4) | (uint32_t)c.parity;
}

void ligar(uart_port_t n, Porta *p)
{
    int baud = p->cfg.baud_rate ? p->cfg.baud_rate : 115200;
    p->hal = uartBegin((uint8_t)n, (uint32_t)baud, p->configurada ? configDoArduino(p->cfg) : 0x800001Cu, (int8_t)p->rx, (int8_t)p->tx, p->tamanhoRx, 0, p->inverter, 112);
}

void vigiaDaPorta(void *arg)
{
    Porta *p = (Porta *)arg;
    uint32_t antes = 0;
    for (;;) {
        uint32_t agora = p->hal ? uartAvailable(p->hal) : 0;
        if (agora > antes && p->fila) {
            uart_event_t e = {};
            e.type = UART_DATA;
            e.size = agora - antes;
            e.timeout_flag = true;
            xQueueSend(p->fila, &e, 0);
        }
        antes = agora;
        vTaskDelay(pdMS_TO_TICKS(2));
    }
}
} /* namespace */

#define CS_PORTA(n)                                 \
    Porta *p = portaDe(n);                          \
    if (!p) return ESP_ERR_INVALID_ARG;

extern "C" {

esp_err_t uart_param_config(uart_port_t n, const uart_config_t *cfg)
{
    CS_PORTA(n);
    if (!cfg) return ESP_ERR_INVALID_ARG;
    p->cfg = *cfg;
    p->configurada = true;
    if (p->instalada) ligar(n, p);
    return ESP_OK;
}
esp_err_t uart_set_pin(uart_port_t n, int tx, int rx, int rts, int cts)
{
    CS_PORTA(n);
    if (tx >= 0) p->tx = tx;
    else if (!p->configurada && !p->instalada) p->tx = -1;
    if (rx >= 0) p->rx = rx;
    else if (!p->configurada && !p->instalada) p->rx = -1;
    p->rts = rts;
    p->cts = cts;
    if (p->instalada) ligar(n, p);
    return ESP_OK;
}
esp_err_t uart_driver_install(uart_port_t n, int rx_buffer_size, int tx_buffer_size, int queue_size, QueueHandle_t *uart_queue, int intr_alloc_flags)
{
    (void)tx_buffer_size;
    (void)intr_alloc_flags;
    CS_PORTA(n);
    if (rx_buffer_size <= SOC_UART_FIFO_LEN) return ESP_ERR_INVALID_ARG;
    if (p->instalada) return ESP_FAIL;
    p->tamanhoRx = (uint32_t)rx_buffer_size;
    if (!p->configurada && (int)n != 0) {
        p->tx = p->tx ? p->tx : -1;
        p->rx = p->rx ? p->rx : -1;
    }
    ligar(n, p);
    p->instalada = true;
    if (queue_size > 0 && uart_queue) {
        p->fila = xQueueCreate(queue_size, sizeof(uart_event_t));
        *uart_queue = p->fila;
        xTaskCreate(vigiaDaPorta, "uart_events", 4096, p, 12, &p->vigia);
    }
    return ESP_OK;
}
esp_err_t uart_driver_delete(uart_port_t n)
{
    CS_PORTA(n);
    if (!p->instalada) return ESP_OK;
    if (p->vigia) vTaskDelete(p->vigia);
    if (p->fila) vQueueDelete(p->fila);
    p->vigia = nullptr;
    p->fila = nullptr;
    p->instalada = false;
    uartEnd((uint8_t)n);
    p->hal = nullptr;
    return ESP_OK;
}
bool uart_is_driver_installed(uart_port_t n)
{
    Porta *p = portaDe(n);
    return p && p->instalada;
}
int uart_write_bytes(uart_port_t n, const void *src, size_t size)
{
    Porta *p = portaDe(n);
    if (!p || !p->instalada || !src) return -1;
    uartWriteBuf(p->hal, (const uint8_t *)src, size);
    return (int)size;
}
int uart_write_bytes_with_break(uart_port_t n, const void *src, size_t size, int brk_len)
{
    (void)brk_len;
    return uart_write_bytes(n, src, size);
}
int uart_tx_chars(uart_port_t n, const char *buffer, uint32_t len) { return uart_write_bytes(n, buffer, len); }
int uart_read_bytes(uart_port_t n, void *buf, uint32_t length, TickType_t ticks_to_wait)
{
    Porta *p = portaDe(n);
    if (!p || !p->instalada || !buf) return -1;
    uint32_t feitos = 0;
    uint32_t inicio = millis(), limite = pdTICKS_TO_MS(ticks_to_wait);
    uint8_t *saida = (uint8_t *)buf;
    while (feitos < length) {
        uint32_t tem = uartAvailable(p->hal);
        if (tem) {
            feitos += (uint32_t)uartReadBytes(p->hal, saida + feitos, min(tem, length - feitos), 0);
            continue;
        }
        if (ticks_to_wait != portMAX_DELAY && millis() - inicio >= limite) break;
        vTaskDelay(1);
    }
    return (int)feitos;
}
esp_err_t uart_get_buffered_data_len(uart_port_t n, size_t *size)
{
    CS_PORTA(n);
    if (!p->instalada) return ESP_FAIL;
    *size = uartAvailable(p->hal);
    return ESP_OK;
}
esp_err_t uart_get_tx_buffer_free_size(uart_port_t n, size_t *size)
{
    CS_PORTA(n);
    if (!p->instalada) return ESP_FAIL;
    *size = uartAvailableForWrite(p->hal);
    return ESP_OK;
}
esp_err_t uart_flush_input(uart_port_t n)
{
    CS_PORTA(n);
    if (!p->instalada) return ESP_FAIL;
    uint8_t lixo[64];
    while (uartAvailable(p->hal)) uartReadBytes(p->hal, lixo, sizeof(lixo), 0);
    return ESP_OK;
}
esp_err_t uart_flush(uart_port_t n) { return uart_flush_input(n); }
esp_err_t uart_wait_tx_done(uart_port_t n, TickType_t ticks_to_wait)
{
    (void)ticks_to_wait;
    CS_PORTA(n);
    return p->instalada ? ESP_OK : ESP_FAIL;
}
esp_err_t uart_wait_tx_idle_polling(uart_port_t n)
{
    CS_PORTA(n);
    return ESP_OK;
}
esp_err_t uart_set_baudrate(uart_port_t n, uint32_t baudrate)
{
    CS_PORTA(n);
    p->cfg.baud_rate = (int)baudrate;
    if (p->instalada) uartSetBaudRate(p->hal, baudrate);
    return ESP_OK;
}
esp_err_t uart_get_baudrate(uart_port_t n, uint32_t *baudrate)
{
    CS_PORTA(n);
    *baudrate = (uint32_t)(p->cfg.baud_rate ? p->cfg.baud_rate : 115200);
    return ESP_OK;
}
#define CS_CAMPO(nome_set, nome_get, tipo, campo, padrao)                                                                                                     \
    esp_err_t nome_set(uart_port_t n, tipo v)                                                                                                                  \
    {                                                                                                                                                          \
        CS_PORTA(n);                                                                                                                                           \
        p->cfg.campo = v;                                                                                                                                      \
        p->configurada = true;                                                                                                                                 \
        return ESP_OK;                                                                                                                                         \
    }                                                                                                                                                          \
    esp_err_t nome_get(uart_port_t n, tipo *v)                                                                                                                 \
    {                                                                                                                                                          \
        CS_PORTA(n);                                                                                                                                           \
        *v = p->configurada ? p->cfg.campo : padrao;                                                                                                           \
        return ESP_OK;                                                                                                                                         \
    }
CS_CAMPO(uart_set_word_length, uart_get_word_length, uart_word_length_t, data_bits, UART_DATA_8_BITS)
CS_CAMPO(uart_set_stop_bits, uart_get_stop_bits, uart_stop_bits_t, stop_bits, UART_STOP_BITS_1)
CS_CAMPO(uart_set_parity, uart_get_parity, uart_parity_t, parity, UART_PARITY_DISABLE)

esp_err_t uart_set_hw_flow_ctrl(uart_port_t n, uart_hw_flowcontrol_t flow_ctrl, uint8_t rx_thresh)
{
    (void)rx_thresh;
    CS_PORTA(n);
    p->fluxo = flow_ctrl;
    return ESP_OK;
}
esp_err_t uart_get_hw_flow_ctrl(uart_port_t n, uart_hw_flowcontrol_t *flow_ctrl)
{
    CS_PORTA(n);
    *flow_ctrl = p->fluxo;
    return ESP_OK;
}
esp_err_t uart_set_line_inverse(uart_port_t n, uint32_t inverse_mask)
{
    CS_PORTA(n);
    p->inverter = inverse_mask != 0;
    return ESP_OK;
}
esp_err_t uart_set_mode(uart_port_t n, uart_mode_t mode)
{
    CS_PORTA(n);
    p->modo = mode;
    return ESP_OK;
}
/* O que so ajusta o hardware da porta: aceito, sem efeito na simulacao. */
#define CS_AJUSTE(nome, ...)                     \
    esp_err_t nome(uart_port_t n, ##__VA_ARGS__) \
    {                                            \
        CS_PORTA(n);                             \
        return ESP_OK;                           \
    }
CS_AJUSTE(uart_set_rx_timeout, const uint8_t tout_thresh)
CS_AJUSTE(uart_set_rx_full_threshold, int threshold)
CS_AJUSTE(uart_set_tx_empty_threshold, int threshold)
CS_AJUSTE(uart_set_tx_idle_num, uint16_t idle_num)
CS_AJUSTE(uart_set_rts, int level)
CS_AJUSTE(uart_set_dtr, int level)
CS_AJUSTE(uart_set_sw_flow_ctrl, bool enable, uint8_t rx_thresh_xon, uint8_t rx_thresh_xoff)
CS_AJUSTE(uart_enable_rx_intr)
CS_AJUSTE(uart_disable_rx_intr)
CS_AJUSTE(uart_disable_tx_intr)
CS_AJUSTE(uart_enable_tx_intr, int enable, int thresh)
CS_AJUSTE(uart_enable_intr_mask, uint32_t enable_mask)
CS_AJUSTE(uart_disable_intr_mask, uint32_t disable_mask)
CS_AJUSTE(uart_clear_intr_status, uint32_t clr_mask)
CS_AJUSTE(uart_intr_config, const uart_intr_config_t *intr_conf)
CS_AJUSTE(uart_set_loop_back, bool loop_back_en)
CS_AJUSTE(uart_disable_pattern_det_intr)
CS_AJUSTE(uart_enable_pattern_det_baud_intr, char pattern_chr, uint8_t chr_num, int chr_tout, int post_idle, int pre_idle)
CS_AJUSTE(uart_pattern_queue_reset, int queue_length)
CS_AJUSTE(uart_set_wakeup_threshold, int wakeup_threshold)
void uart_set_always_rx_timeout(uart_port_t n, bool always_rx_timeout_en)
{
    (void)n;
    (void)always_rx_timeout_en;
}
esp_err_t uart_get_wakeup_threshold(uart_port_t n, int *out_wakeup_threshold)
{
    CS_PORTA(n);
    *out_wakeup_threshold = 3;
    return ESP_OK;
}
esp_err_t uart_get_collision_flag(uart_port_t n, bool *collision_flag)
{
    CS_PORTA(n);
    *collision_flag = false;
    return ESP_OK;
}
int uart_pattern_pop_pos(uart_port_t n)
{
    (void)n;
    return -1;
}
int uart_pattern_get_pos(uart_port_t n)
{
    (void)n;
    return -1;
}
} /* extern "C" */
#endif
