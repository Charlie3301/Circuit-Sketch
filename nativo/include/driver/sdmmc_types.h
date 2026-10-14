#ifndef CS_DRIVER_SDMMC_TYPES_H
#define CS_DRIVER_SDMMC_TYPES_H

#include <stdint.h>
#include <stdbool.h>
#include <stddef.h>
#include "esp_err.h"
#include "freertos/FreeRTOS.h"

#define SDMMC_HOST_FLAG_1BIT (1 << 0)
#define SDMMC_HOST_FLAG_4BIT (1 << 1)
#define SDMMC_HOST_FLAG_8BIT (1 << 2)
#define SDMMC_HOST_FLAG_SPI (1 << 3)
#define SDMMC_HOST_FLAG_DDR (1 << 4)
#define SDMMC_HOST_FLAG_DEINIT_ARG (1 << 5)

#define SDMMC_FREQ_DEFAULT 20000
#define SDMMC_FREQ_HIGHSPEED 40000
#define SDMMC_FREQ_PROBING 400
#define SDMMC_FREQ_52M 52000
#define SDMMC_FREQ_26M 26000

typedef struct sdmmc_command_t sdmmc_command_t;

typedef struct {
    uint32_t flags;
    int slot;
    int max_freq_khz;
    float io_voltage;
    esp_err_t (*init)(void);
    esp_err_t (*set_bus_width)(int slot, size_t width);
    size_t (*get_bus_width)(int slot);
    esp_err_t (*set_bus_ddr_mode)(int slot, bool ddr);
    esp_err_t (*set_card_clk)(int slot, uint32_t freq_khz);
    esp_err_t (*set_cclk_always_on)(int slot, bool sempre);
    esp_err_t (*do_transaction)(int slot, sdmmc_command_t *cmd);
    union {
        esp_err_t (*deinit)(void);
        esp_err_t (*deinit_p)(int slot);
    };
    esp_err_t (*io_int_enable)(int slot);
    esp_err_t (*io_int_wait)(int slot, TickType_t espera);
    int command_timeout_ms;
    esp_err_t (*get_real_freq)(int slot, int *freq);
    int input_delay_phase;
    esp_err_t (*set_input_delay)(int slot, int fase);
    void *dma_aligned_buffer;
    void *pwr_ctrl_handle;
    esp_err_t (*get_dma_info)(int slot, void *info);
} sdmmc_host_t;

typedef struct {
    uint32_t sector_size;
    uint32_t capacity;
    uint32_t read_block_len;
    uint32_t card_command_class;
    uint32_t tr_speed;
} sdmmc_csd_t;

typedef struct {
    uint32_t mfg_id;
    uint32_t oem_id;
    char name[8];
    uint32_t revision;
    uint32_t serial;
    uint32_t date;
} sdmmc_cid_t;

typedef struct {
    uint32_t sd_spec;
    uint32_t erase_mem_state;
    uint32_t bus_width;
    uint32_t reserved;
    uint32_t rsvd_mnf;
} sdmmc_scr_t;

typedef struct {
    sdmmc_host_t host;
    uint32_t ocr;
    union { sdmmc_cid_t cid; uint32_t raw_cid[4]; };
    sdmmc_csd_t csd;
    sdmmc_scr_t scr;
    uint32_t rca;
    uint16_t max_freq_khz;
    int real_freq_khz;
    uint32_t is_mem : 1;
    uint32_t is_sdio : 1;
    uint32_t is_mmc : 1;
    uint32_t num_io_functions : 3;
    uint32_t log_bus_width : 2;
    uint32_t is_ddr : 1;
    uint32_t reserved : 23;
} sdmmc_card_t;

#endif
