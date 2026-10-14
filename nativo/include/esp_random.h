#ifndef CS_ESP_RANDOM_H
#define CS_ESP_RANDOM_H

#include <stdint.h>
#include <stddef.h>

uint32_t esp_random(void);
void esp_fill_random(void *buf, size_t n);

#endif
