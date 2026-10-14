/* driver/sdspi_host.h: o cartao microSD pelo SPI. O cartao e o modulo do
 * circuito ligado no CS escolhido; os arquivos sao os que estao na peca
 * (inclusive os que vieram do computador, como o DOOM1.WAD). */
#ifndef CS_DRIVER_SDSPI_HOST_H
#define CS_DRIVER_SDSPI_HOST_H

#include "driver/sdmmc_types.h"
#include "driver/spi_common.h"
#include "driver/gpio.h"

#define SDSPI_DEFAULT_HOST SPI2_HOST
#define SDSPI_DEFAULT_DMA SPI_DMA_CH_AUTO
#define SDSPI_SLOT_NO_CS GPIO_NUM_NC
#define SDSPI_SLOT_NO_CD GPIO_NUM_NC
#define SDSPI_SLOT_NO_WP GPIO_NUM_NC
#define SDSPI_SLOT_NO_INT GPIO_NUM_NC
#define SDSPI_IO_ACTIVE_LOW 0

typedef int sdspi_dev_handle_t;

esp_err_t sdspi_host_init(void);
esp_err_t sdspi_host_do_transaction(int slot, sdmmc_command_t *cmd);
esp_err_t sdspi_host_set_card_clk(int slot, uint32_t freq_khz);
esp_err_t sdspi_host_get_real_freq(int slot, int *freq);
esp_err_t sdspi_host_remove_device(int slot);
esp_err_t sdspi_host_deinit(void);
esp_err_t sdspi_host_io_int_enable(int slot);
esp_err_t sdspi_host_io_int_wait(int slot, TickType_t espera);

#define SDSPI_HOST_DEFAULT() { \
    .flags = SDMMC_HOST_FLAG_SPI | SDMMC_HOST_FLAG_DEINIT_ARG, \
    .slot = SDSPI_DEFAULT_HOST, \
    .max_freq_khz = SDMMC_FREQ_DEFAULT, \
    .io_voltage = 3.3f, \
    .init = &sdspi_host_init, \
    .set_bus_width = NULL, \
    .get_bus_width = NULL, \
    .set_bus_ddr_mode = NULL, \
    .set_card_clk = &sdspi_host_set_card_clk, \
    .set_cclk_always_on = NULL, \
    .do_transaction = &sdspi_host_do_transaction, \
    .deinit_p = &sdspi_host_remove_device, \
    .io_int_enable = &sdspi_host_io_int_enable, \
    .io_int_wait = &sdspi_host_io_int_wait, \
    .command_timeout_ms = 0, \
    .get_real_freq = &sdspi_host_get_real_freq, \
}

typedef struct {
    spi_host_device_t host_id;
    gpio_num_t gpio_cs;
    gpio_num_t gpio_cd;
    gpio_num_t gpio_wp;
    gpio_num_t gpio_int;
    bool gpio_wp_polarity;
} sdspi_device_config_t;

#define SDSPI_DEVICE_CONFIG_DEFAULT() { \
    .host_id = SDSPI_DEFAULT_HOST, \
    .gpio_cs = GPIO_NUM_13, \
    .gpio_cd = SDSPI_SLOT_NO_CD, \
    .gpio_wp = SDSPI_SLOT_NO_WP, \
    .gpio_int = GPIO_NUM_NC, \
    .gpio_wp_polarity = SDSPI_IO_ACTIVE_LOW, \
}

esp_err_t sdspi_host_init_device(const sdspi_device_config_t *config, sdspi_dev_handle_t *alca);

#endif
