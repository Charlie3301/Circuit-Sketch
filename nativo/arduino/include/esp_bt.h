/* esp_bt.h -- o controlador Bluetooth do ESP-IDF.
 *
 * Na ESP32, na S3 e na C3, o de verdade (nativo/idf). O da C6 e o da H2
 * inclui o NimBLE e o controlador por dentro (nimble_npl.h e um caminho
 * relativo a arvore do IDF), que o simulador nao tem: o radio dele e o da
 * bancada. Para esses dois, aqui os tipos e os numeros da potencia, os mesmos
 * do esp_bt.h do IDF de cada chip. */
#pragma once

#include "sdkconfig.h"

#if CONFIG_IDF_TARGET_ESP32C6 || CONFIG_IDF_TARGET_ESP32H2
#include <stdint.h>
#include "esp_err.h"

#ifdef __cplusplus
extern "C" {
#endif

typedef enum {
    ESP_BLE_PWR_TYPE_CONN_HDL0 = 0,
    ESP_BLE_PWR_TYPE_CONN_HDL1 = 1,
    ESP_BLE_PWR_TYPE_CONN_HDL2 = 2,
    ESP_BLE_PWR_TYPE_CONN_HDL3 = 3,
    ESP_BLE_PWR_TYPE_CONN_HDL4 = 4,
    ESP_BLE_PWR_TYPE_CONN_HDL5 = 5,
    ESP_BLE_PWR_TYPE_CONN_HDL6 = 6,
    ESP_BLE_PWR_TYPE_CONN_HDL7 = 7,
    ESP_BLE_PWR_TYPE_CONN_HDL8 = 8,
    ESP_BLE_PWR_TYPE_ADV = 9,
    ESP_BLE_PWR_TYPE_SCAN = 10,
    ESP_BLE_PWR_TYPE_DEFAULT = 11,
    ESP_BLE_PWR_TYPE_NUM = 12,
} esp_ble_power_type_t;

typedef enum {
#if CONFIG_IDF_TARGET_ESP32H2
    ESP_PWR_LVL_N24 = 0,
    ESP_PWR_LVL_N21 = 1,
    ESP_PWR_LVL_N18 = 2,
#endif
    ESP_PWR_LVL_N15 = 3,
    ESP_PWR_LVL_N12 = 4,
    ESP_PWR_LVL_N9 = 5,
    ESP_PWR_LVL_N6 = 6,
    ESP_PWR_LVL_N3 = 7,
    ESP_PWR_LVL_N0 = 8,
    ESP_PWR_LVL_P3 = 9,
    ESP_PWR_LVL_P6 = 10,
    ESP_PWR_LVL_P9 = 11,
    ESP_PWR_LVL_P12 = 12,
    ESP_PWR_LVL_P15 = 13,
#if CONFIG_IDF_TARGET_ESP32H2
    ESP_PWR_LVL_P16 = 14,
    ESP_PWR_LVL_P17 = 14,
#endif
    ESP_PWR_LVL_P18 = 14,
#if CONFIG_IDF_TARGET_ESP32H2
    ESP_PWR_LVL_P19 = 15,
#endif
    ESP_PWR_LVL_P20 = 15,
    ESP_PWR_LVL_INVALID = 0xFF,
} esp_power_level_t;

esp_err_t esp_ble_tx_power_set(esp_ble_power_type_t power_type, esp_power_level_t power_level);
esp_power_level_t esp_ble_tx_power_get(esp_ble_power_type_t power_type);

#ifdef __cplusplus
}
#endif

#elif __has_include_next(<esp_bt.h>)
#include_next <esp_bt.h>
#endif
