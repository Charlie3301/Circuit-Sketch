#ifndef CS_ESP_ROM_SYS_H
#define CS_ESP_ROM_SYS_H

#include "sdkconfig.h"
#include <stdint.h>
#include <stdbool.h>
#include <inttypes.h>
#include <stdarg.h>
#include "soc/soc_caps.h"

int esp_rom_printf(const char *formato, ...);
void esp_rom_delay_us(uint32_t us);

#endif
