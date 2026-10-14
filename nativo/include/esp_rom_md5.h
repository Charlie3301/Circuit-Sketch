/* esp_rom_md5.h do CircuitSketch (codigo nativo): o MD5 da ROM da ESP32,
 * pelos nomes do ESP-IDF. */
#pragma once

#include <stdint.h>
#include "sdkconfig.h"

#define ESP_ROM_MD5_DIGEST_LEN 16

typedef struct MD5Context {
    uint32_t buf[4];
    uint32_t bits[2];
    uint8_t in[64];
} md5_context_t;

void esp_rom_md5_init(md5_context_t *context);
void esp_rom_md5_update(md5_context_t *context, const void *buf, uint32_t len);
void esp_rom_md5_final(uint8_t *digest, md5_context_t *context);
