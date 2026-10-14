/* driver/uart.h: a UART0 e o monitor serial (o que a pessoa digita chega
 * no uart_read_bytes). As outras UARTs so existem se ligadas em algo. */
#ifndef CS_DRIVER_UART_H
#define CS_DRIVER_UART_H

#include <stdint.h>
#include <stdbool.h>
#include <stddef.h>
#include "esp_err.h"
#include "esp_intr_alloc.h"
#include "freertos/FreeRTOS.h"
#include "freertos/queue.h"

typedef enum { UART_NUM_0 = 0, UART_NUM_1 = 1, UART_NUM_2 = 2, UART_NUM_MAX } uart_port_t;
typedef enum { UART_DATA_5_BITS = 0, UART_DATA_6_BITS, UART_DATA_7_BITS, UART_DATA_8_BITS, UART_DATA_BITS_MAX } uart_word_length_t;
typedef enum { UART_STOP_BITS_1 = 1, UART_STOP_BITS_1_5 = 2, UART_STOP_BITS_2 = 3, UART_STOP_BITS_MAX } uart_stop_bits_t;
typedef enum { UART_PARITY_DISABLE = 0, UART_PARITY_EVEN = 2, UART_PARITY_ODD = 3 } uart_parity_t;
typedef enum { UART_HW_FLOWCTRL_DISABLE = 0, UART_HW_FLOWCTRL_RTS = 1, UART_HW_FLOWCTRL_CTS = 2,
               UART_HW_FLOWCTRL_CTS_RTS = 3, UART_HW_FLOWCTRL_MAX } uart_hw_flowcontrol_t;
typedef enum { UART_SCLK_DEFAULT = 0, UART_SCLK_APB = 0, UART_SCLK_XTAL = 1, UART_SCLK_RTC = 2 } uart_sclk_t;
typedef enum { UART_MODE_UART = 0, UART_MODE_RS485_HALF_DUPLEX = 1 } uart_mode_t;

#define UART_PIN_NO_CHANGE (-1)
#define UART_FIFO_LEN 128

typedef struct {
    int baud_rate;
    uart_word_length_t data_bits;
    uart_parity_t parity;
    uart_stop_bits_t stop_bits;
    uart_hw_flowcontrol_t flow_ctrl;
    uint8_t rx_flow_ctrl_thresh;
    union {
        uart_sclk_t source_clk;
        uart_sclk_t lp_source_clk;
    };
    struct {
        uint32_t allow_pd : 1;
        uint32_t backup_before_sleep : 1;
    } flags;
} uart_config_t;

typedef enum { UART_DATA, UART_BREAK, UART_BUFFER_FULL, UART_FIFO_OVF, UART_FRAME_ERR,
               UART_PARITY_ERR, UART_DATA_BREAK, UART_PATTERN_DET, UART_EVENT_MAX } uart_event_type_t;
typedef struct { uart_event_type_t type; size_t size; bool timeout_flag; } uart_event_t;

esp_err_t uart_driver_install(uart_port_t porta, int rx, int tx, int fila_tamanho, QueueHandle_t *fila, int flags);
esp_err_t uart_driver_delete(uart_port_t porta);
bool uart_is_driver_installed(uart_port_t porta);
esp_err_t uart_param_config(uart_port_t porta, const uart_config_t *config);
esp_err_t uart_set_pin(uart_port_t porta, int tx, int rx, int rts, int cts);
esp_err_t uart_set_baudrate(uart_port_t porta, uint32_t baud);
esp_err_t uart_get_baudrate(uart_port_t porta, uint32_t *baud);
int uart_read_bytes(uart_port_t porta, void *buf, uint32_t n, TickType_t espera);
int uart_write_bytes(uart_port_t porta, const void *dados, size_t n);
int uart_tx_chars(uart_port_t porta, const char *dados, uint32_t n);
esp_err_t uart_wait_tx_done(uart_port_t porta, TickType_t espera);
esp_err_t uart_flush(uart_port_t porta);
esp_err_t uart_flush_input(uart_port_t porta);
esp_err_t uart_get_buffered_data_len(uart_port_t porta, size_t *n);
esp_err_t uart_set_mode(uart_port_t porta, uart_mode_t modo);

#endif
