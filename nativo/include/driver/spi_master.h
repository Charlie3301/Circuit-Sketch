/* driver/spi_master.h: o barramento SPI da placa. Cada transacao vai
 * para o aparelho ligado no CS do dispositivo -- o display ILI9341, por
 * exemplo, que decodifica os comandos e desenha na tela do circuito.
 * Sem aparelho ligado naquele CS (ou com o MOSI/SCK fora do lugar), a
 * transacao sai e ninguem recebe, como na bancada. */
#ifndef CS_DRIVER_SPI_MASTER_H
#define CS_DRIVER_SPI_MASTER_H

#include <stdint.h>
#include <stdbool.h>
#include <stddef.h>
#include "esp_err.h"
#include "driver/spi_common.h"
#include "freertos/FreeRTOS.h"

#define SPI_MASTER_FREQ_8M (80 * 1000 * 1000 / 10)
#define SPI_MASTER_FREQ_9M (80 * 1000 * 1000 / 9)
#define SPI_MASTER_FREQ_10M (80 * 1000 * 1000 / 8)
#define SPI_MASTER_FREQ_11M (80 * 1000 * 1000 / 7)
#define SPI_MASTER_FREQ_13M (80 * 1000 * 1000 / 6)
#define SPI_MASTER_FREQ_16M (80 * 1000 * 1000 / 5)
#define SPI_MASTER_FREQ_20M (80 * 1000 * 1000 / 4)
#define SPI_MASTER_FREQ_26M (80 * 1000 * 1000 / 3)
#define SPI_MASTER_FREQ_40M (80 * 1000 * 1000 / 2)
#define SPI_MASTER_FREQ_80M (80 * 1000 * 1000 / 1)

#define SPI_DEVICE_TXBIT_LSBFIRST (1 << 0)
#define SPI_DEVICE_RXBIT_LSBFIRST (1 << 1)
#define SPI_DEVICE_BIT_LSBFIRST (SPI_DEVICE_TXBIT_LSBFIRST | SPI_DEVICE_RXBIT_LSBFIRST)
#define SPI_DEVICE_3WIRE (1 << 2)
#define SPI_DEVICE_POSITIVE_CS (1 << 3)
#define SPI_DEVICE_HALFDUPLEX (1 << 4)
#define SPI_DEVICE_CLK_AS_CS (1 << 5)
#define SPI_DEVICE_NO_DUMMY (1 << 6)
#define SPI_DEVICE_DDRCLK (1 << 7)
#define SPI_DEVICE_NO_RETURN_RESULT (1 << 8)

#define SPI_TRANS_MODE_DIO (1 << 0)
#define SPI_TRANS_MODE_QIO (1 << 1)
#define SPI_TRANS_USE_RXDATA (1 << 2)
#define SPI_TRANS_USE_TXDATA (1 << 3)
#define SPI_TRANS_MODE_DIOQIO_ADDR (1 << 4)
#define SPI_TRANS_VARIABLE_CMD (1 << 5)
#define SPI_TRANS_VARIABLE_ADDR (1 << 6)
#define SPI_TRANS_VARIABLE_DUMMY (1 << 7)
#define SPI_TRANS_CS_KEEP_ACTIVE (1 << 8)
#define SPI_TRANS_MULTILINE_CMD (1 << 9)
#define SPI_TRANS_MODE_OCT (1 << 10)
#define SPI_TRANS_MULTILINE_ADDR SPI_TRANS_MODE_DIOQIO_ADDR

typedef struct spi_transaction_t spi_transaction_t;
typedef void (*transaction_cb_t)(spi_transaction_t *trans);

typedef enum { SPI_CLK_SRC_DEFAULT = 0, SPI_CLK_SRC_APB = 0, SPI_CLK_SRC_XTAL = 1 } spi_clock_source_t;

typedef struct {
    uint8_t command_bits;
    uint8_t address_bits;
    uint8_t dummy_bits;
    uint8_t mode;
    spi_clock_source_t clock_source;
    uint16_t duty_cycle_pos;
    uint16_t cs_ena_pretrans;
    uint8_t cs_ena_posttrans;
    int clock_speed_hz;
    int input_delay_ns;
    int spics_io_num;
    uint32_t flags;
    int queue_size;
    transaction_cb_t pre_cb;
    transaction_cb_t post_cb;
} spi_device_interface_config_t;

struct spi_transaction_t {
    uint32_t flags;
    uint16_t cmd;
    uint64_t addr;
    size_t length;
    size_t rxlength;
    void *user;
    union {
        const void *tx_buffer;
        uint8_t tx_data[4];
    };
    union {
        void *rx_buffer;
        uint8_t rx_data[4];
    };
};

typedef struct {
    struct spi_transaction_t base;
    uint8_t command_bits;
    uint8_t address_bits;
    uint8_t dummy_bits;
} spi_transaction_ext_t;

typedef struct spi_device_t *spi_device_handle_t;

esp_err_t spi_bus_add_device(spi_host_device_t host, const spi_device_interface_config_t *config,
                             spi_device_handle_t *alca);
esp_err_t spi_bus_remove_device(spi_device_handle_t alca);
esp_err_t spi_device_queue_trans(spi_device_handle_t alca, spi_transaction_t *trans, TickType_t espera);
esp_err_t spi_device_get_trans_result(spi_device_handle_t alca, spi_transaction_t **trans, TickType_t espera);
esp_err_t spi_device_transmit(spi_device_handle_t alca, spi_transaction_t *trans);
esp_err_t spi_device_polling_start(spi_device_handle_t alca, spi_transaction_t *trans, TickType_t espera);
esp_err_t spi_device_polling_end(spi_device_handle_t alca, TickType_t espera);
esp_err_t spi_device_polling_transmit(spi_device_handle_t alca, spi_transaction_t *trans);
esp_err_t spi_device_acquire_bus(spi_device_handle_t alca, TickType_t espera);
void spi_device_release_bus(spi_device_handle_t alca);
esp_err_t spi_device_get_actual_freq(spi_device_handle_t alca, int *freq_khz);
int spi_get_actual_clock(int fapb, int hz, int duty_cycle);

#endif
