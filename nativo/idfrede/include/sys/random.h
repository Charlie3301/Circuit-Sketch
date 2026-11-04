/* O sys/random.h da newlib: o sorteio vem do esp_random. */
#pragma once
#include <stddef.h>
#include "esp_random.h"
static inline long getrandom(void *buf, size_t n, unsigned flags) { (void)flags; esp_fill_random(buf, n); return (long)n; }
