#ifndef CS_ESP_CPU_H
#define CS_ESP_CPU_H

#include "sdkconfig.h"
#include <stdbool.h>
#include <stdint.h>
#include <assert.h>
#include "soc/soc_caps.h"
#include "esp_intr_alloc.h"
#include "esp_err.h"
#include "esp_attr.h"

typedef uint32_t esp_cpu_cycle_count_t;
esp_cpu_cycle_count_t esp_cpu_get_cycle_count(void);
int esp_cpu_get_core_id(void);
void esp_cpu_wait_for_intr(void);
#define esp_cpu_get_ccount esp_cpu_get_cycle_count

#endif
