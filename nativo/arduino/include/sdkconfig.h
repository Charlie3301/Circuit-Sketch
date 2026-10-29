/* sdkconfig.h -- a configuracao do menuconfig do ESP-IDF da placa simulada.
 *
 * Sao os sdkconfig.h de verdade do core ESP32 do Arduino (sdkconfig/, os
 * mesmos do runtime do TinyCC): o codigo compilado aqui ve os mesmos CONFIG_
 * que veria na IDE. O chip vem do cs_placa.h (CONFIG_IDF_TARGET_...). */
#ifndef CS_SDKCONFIG_H
#define CS_SDKCONFIG_H

/* O chip da placa (o CONFIG_IDF_TARGET_...): o cs_placa.h que o simulador gera. */
#include <stdint.h>
#include "cs_placa.h"

#if defined(CONFIG_IDF_TARGET_ESP32S3)
#include "sdkconfig/esp32s3.h"
#elif defined(CONFIG_IDF_TARGET_ESP32S2)
#include "sdkconfig/esp32s2.h"
#elif defined(CONFIG_IDF_TARGET_ESP32C3)
#include "sdkconfig/esp32c3.h"
#elif defined(CONFIG_IDF_TARGET_ESP32C6)
#include "sdkconfig/esp32c6.h"
#elif defined(CONFIG_IDF_TARGET_ESP32H2)
#include "sdkconfig/esp32h2.h"
#else
#include "sdkconfig/esp32.h"
#endif

/* O programa do ESP-IDF (app_main, compilado com CS_PROGRAMA_IDF) usa a
 * configuracao padrao de um projeto do IDF: o log em INFO. O sketch fica
 * com a do core do Arduino (o "Core Debug Level: None": so os erros). */
#ifdef CS_PROGRAMA_IDF
#undef CONFIG_LOG_DEFAULT_LEVEL_ERROR
#undef CONFIG_LOG_DEFAULT_LEVEL
#undef CONFIG_LOG_MAXIMUM_LEVEL
#undef CONFIG_LOG_MAXIMUM_EQUALS_DEFAULT
#define CONFIG_LOG_DEFAULT_LEVEL_INFO 1
#define CONFIG_LOG_DEFAULT_LEVEL 3
#define CONFIG_LOG_MAXIMUM_EQUALS_DEFAULT 1
#define CONFIG_LOG_MAXIMUM_LEVEL 3
#endif

/* Os dos exemplos oficiais do ESP-IDF (o pino do blink), que o menuconfig
 * do exemplo poe. */
#ifndef CONFIG_BLINK_GPIO
#define CONFIG_BLINK_GPIO 2
#endif
#ifndef CONFIG_BLINK_PERIOD
#define CONFIG_BLINK_PERIOD 1000
#endif
#ifndef CONFIG_BLINK_LED_GPIO
#define CONFIG_BLINK_LED_GPIO 1
#endif
#ifndef CONFIG_ESP32_DEFAULT_CPU_FREQ_MHZ
#define CONFIG_ESP32_DEFAULT_CPU_FREQ_MHZ CONFIG_ESP_DEFAULT_CPU_FREQ_MHZ
#endif
#ifndef CONFIG_CONSOLE_UART_BAUDRATE
#define CONFIG_CONSOLE_UART_BAUDRATE CONFIG_ESP_CONSOLE_UART_BAUDRATE
#endif


/* A versao do IDF (o ESP_IDF_VERSION_VAL que os cabecalhos comparam). */
#include "esp_idf_version.h"
#endif
