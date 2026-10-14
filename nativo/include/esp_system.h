#ifndef CS_ESP_SYSTEM_H
#define CS_ESP_SYSTEM_H

#include <stdint.h>
#include <stddef.h>
#include <stdbool.h>
#include "esp_err.h"
#include "esp_attr.h"
#include "esp_bit_defs.h"
#include "esp_idf_version.h"
#include "sdkconfig.h"

typedef enum {
    ESP_RST_UNKNOWN, ESP_RST_POWERON, ESP_RST_EXT, ESP_RST_SW, ESP_RST_PANIC,
    ESP_RST_INT_WDT, ESP_RST_TASK_WDT, ESP_RST_WDT, ESP_RST_DEEPSLEEP, ESP_RST_BROWNOUT, ESP_RST_SDIO
} esp_reset_reason_t;

void esp_restart(void) __attribute__((noreturn));
esp_reset_reason_t esp_reset_reason(void);
uint32_t esp_get_free_heap_size(void);
uint32_t esp_get_free_internal_heap_size(void);
uint32_t esp_get_minimum_free_heap_size(void);
uint32_t esp_random(void);
void esp_fill_random(void *buf, size_t n);
const char *esp_get_idf_version(void);
void esp_system_abort(const char *detalhe) __attribute__((noreturn));

#endif
