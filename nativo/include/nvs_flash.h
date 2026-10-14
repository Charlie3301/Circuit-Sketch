#ifndef CS_NVS_FLASH_H
#define CS_NVS_FLASH_H

#include "esp_err.h"
#include "nvs.h"

esp_err_t nvs_flash_init(void);
esp_err_t nvs_flash_init_partition(const char *particao);
esp_err_t nvs_flash_deinit(void);
esp_err_t nvs_flash_erase(void);
esp_err_t nvs_flash_erase_partition(const char *particao);

#endif
