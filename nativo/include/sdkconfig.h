/* sdkconfig.h: a configuracao do ESP-IDF da placa simulada.
 *
 * Sao os sdkconfig.h de verdade do core ESP32 do Arduino (3.3.11,
 * ESP-IDF 5.5): o codigo compilado aqui ve os mesmos CONFIG_ que veria
 * na IDE -- inclusive o nivel de log (ERROR), que faz um ESP_LOGI sumir
 * na placa. O simulador define qual chip (CS_CHIP_...). */
#ifndef CS_SDKCONFIG_H
#define CS_SDKCONFIG_H

#if defined(CS_CHIP_ESP32S3)
#include "sdkconfig/esp32s3.h"
#elif defined(CS_CHIP_ESP32S2)
#include "sdkconfig/esp32s2.h"
#elif defined(CS_CHIP_ESP32C3)
#include "sdkconfig/esp32c3.h"
#elif defined(CS_CHIP_ESP32C6)
#include "sdkconfig/esp32c6.h"
#elif defined(CS_CHIP_ESP32H2)
#include "sdkconfig/esp32h2.h"
#else
#include "sdkconfig/esp32.h"
#endif

#endif
