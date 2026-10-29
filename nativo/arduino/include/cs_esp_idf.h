/* cs_esp_idf.h -- o pedaco do ESP-IDF que o Arduino.h da ESP32 traz junto:
 * os atributos de memoria (esp_attr.h), os erros (esp_err.h), o sono
 * (esp_sleep.h), o reinicio (esp_system.h) e os nomes dos pinos (GPIO_NUM_x).
 *
 * Sao os cabecalhos de verdade do ESP-IDF (nativo/idf), menos o esp_attr.h,
 * que e o do simulador (ver la). O sono e o reinicio sao os do simulador,
 * pela ponte. */
#ifndef CS_ESP_IDF_H
#define CS_ESP_IDF_H

#include <stdint.h>
#include "esp_attr.h"
#include "esp_err.h"
#include "soc/gpio_num.h"
#include "esp_sleep.h"
#include "esp_system.h"

#endif
