#ifndef CS_ESP_FLASH_H
#define CS_ESP_FLASH_H

#include <stdint.h>
#include <stddef.h>
#include "esp_err.h"

typedef struct esp_flash_t esp_flash_t;
extern esp_flash_t *esp_flash_default_chip;

esp_err_t esp_flash_read(esp_flash_t *chip, void *buf, uint32_t endereco, uint32_t n);
esp_err_t esp_flash_write(esp_flash_t *chip, const void *buf, uint32_t endereco, uint32_t n);
esp_err_t esp_flash_erase_region(esp_flash_t *chip, uint32_t endereco, uint32_t n);
esp_err_t esp_flash_get_size(esp_flash_t *chip, uint32_t *tamanho);

#endif
