/* cs_idf.h -- o ESP-IDF (o SDK em C da Espressif) sobre o core nativo: o
 * programa com app_main() e os drivers de sempre (gpio, ledc, adc,
 * esp_timer, esp_log, nvs_flash). Cada driver cai na mesma funcao do
 * Arduino (o gpio_set_level e o digitalWrite), e o printf sai no monitor
 * serial, como o da UART0 da placa.
 *
 * Os cabecalhos sao os de verdade do ESP-IDF (nativo/idf, ver
 * ferramentas/idf/copiar_idf.py): os tipos, as constantes e as macros sao as
 * mesmas da placa. As funcoes sao as do simulador (cs_idf.cpp). */
#ifndef CS_IDF_H
#define CS_IDF_H

#include "Arduino.h"
#if !defined(ESP32)
#error "o ESP-IDF e o SDK da ESP32: escolha uma placa ESP32 para este programa"
#endif
#include "freertos/FreeRTOS.h"
#include "sdkconfig.h"
#include "esp_err.h"
#include "esp_check.h"
#include "esp_log.h"
#include "esp_timer.h"
#include "esp_system.h"
#include "esp_random.h"
#include "esp_chip_info.h"
#include "esp_flash.h"
#include "esp_idf_version.h"
#include "esp_rom_sys.h"
#include "esp_rom_gpio.h"
#include "rom/ets_sys.h"
#include "driver/gpio.h"
#include "driver/ledc.h"
#include "esp_adc/adc_oneshot.h"
#include "nvs_flash.h"

#endif
