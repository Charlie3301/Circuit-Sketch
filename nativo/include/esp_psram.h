#ifndef CS_ESP_PSRAM_H
#define CS_ESP_PSRAM_H

#include <stddef.h>
#include <stdbool.h>
#include "esp_err.h"

size_t esp_psram_get_size(void);
bool esp_psram_is_initialized(void);
esp_err_t esp_psram_init(void);

#endif
