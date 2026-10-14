/* driver/sdmmc_host.h: o cartao pelo barramento SDMMC (1 ou 4 fios). */
#ifndef CS_DRIVER_SDMMC_HOST_H
#define CS_DRIVER_SDMMC_HOST_H

#include "driver/sdmmc_types.h"
#include "driver/gpio.h"

#define SDMMC_HOST_SLOT_0 0
#define SDMMC_HOST_SLOT_1 1
#define SDMMC_SLOT_NO_CD GPIO_NUM_NC
#define SDMMC_SLOT_NO_WP GPIO_NUM_NC
#define SDMMC_SLOT_WIDTH_DEFAULT 0
#define SDMMC_SLOT_FLAG_INTERNAL_PULLUP (1 << 0)

esp_err_t sdmmc_host_init(void);
esp_err_t sdmmc_host_deinit(void);

#define SDMMC_HOST_DEFAULT() { \
    .flags = SDMMC_HOST_FLAG_8BIT | SDMMC_HOST_FLAG_4BIT | SDMMC_HOST_FLAG_1BIT | SDMMC_HOST_FLAG_DDR, \
    .slot = SDMMC_HOST_SLOT_1, \
    .max_freq_khz = SDMMC_FREQ_DEFAULT, \
    .io_voltage = 3.3f, \
    .init = &sdmmc_host_init, \
    .deinit = &sdmmc_host_deinit, \
    .command_timeout_ms = 0, \
}

typedef struct {
    gpio_num_t clk;
    gpio_num_t cmd;
    gpio_num_t d0;
    gpio_num_t d1;
    gpio_num_t d2;
    gpio_num_t d3;
    gpio_num_t d4;
    gpio_num_t d5;
    gpio_num_t d6;
    gpio_num_t d7;
    gpio_num_t cd;
    gpio_num_t wp;
    uint8_t width;
    uint32_t flags;
} sdmmc_slot_config_t;

#define SDMMC_SLOT_CONFIG_DEFAULT() { \
    .clk = GPIO_NUM_14, .cmd = GPIO_NUM_15, .d0 = GPIO_NUM_2, .d1 = GPIO_NUM_4, \
    .d2 = GPIO_NUM_12, .d3 = GPIO_NUM_13, .d4 = GPIO_NUM_NC, .d5 = GPIO_NUM_NC, \
    .d6 = GPIO_NUM_NC, .d7 = GPIO_NUM_NC, .cd = SDMMC_SLOT_NO_CD, .wp = SDMMC_SLOT_NO_WP, \
    .width = SDMMC_SLOT_WIDTH_DEFAULT, .flags = 0, \
}

#endif
