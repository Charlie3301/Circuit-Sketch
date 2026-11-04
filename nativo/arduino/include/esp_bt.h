/* esp_bt.h -- o controlador Bluetooth do ESP-IDF.
 *
 * Na ESP32, na S3 e na C3, o de verdade (nativo/idf). O da C6 e o da H2
 * inclui o NimBLE e o controlador por dentro (nimble_npl.h e um caminho
 * relativo a arvore do IDF), que o simulador nao tem: o radio dele e o da
 * bancada. Para esses dois, aqui os tipos e os numeros da potencia, os mesmos
 * do esp_bt.h do IDF de cada chip. */
#pragma once

#include "sdkconfig.h"

/* O que o BT_CONTROLLER_INIT_CONFIG_DEFAULT usa e o sdkconfig do Arduino nao traz: sem efeito aqui. */
#ifndef CONFIG_BTDM_BLE_LLCP_CHAN_MAP_UPDATE
#define CONFIG_BTDM_BLE_LLCP_CHAN_MAP_UPDATE 0
#endif
#ifndef CONFIG_BTDM_BLE_LLCP_CONN_UPDATE
#define CONFIG_BTDM_BLE_LLCP_CONN_UPDATE 0
#endif
#ifndef CONFIG_BTDM_CTRL_AUTO_LATENCY_EFF
#define CONFIG_BTDM_CTRL_AUTO_LATENCY_EFF 0
#endif
#ifndef CONFIG_BTDM_CTRL_BR_EDR_MAX_SYNC_CONN_EFF
#define CONFIG_BTDM_CTRL_BR_EDR_MAX_SYNC_CONN_EFF 0
#endif
#ifndef CONFIG_BTDM_CTRL_CHECK_CONNECT_IND_ACCESS_ADDRESS
#define CONFIG_BTDM_CTRL_CHECK_CONNECT_IND_ACCESS_ADDRESS 0
#endif
#ifndef CONFIG_BTDM_CTRL_SCAN_BACKOFF_UPPERLIMITMAX
#define CONFIG_BTDM_CTRL_SCAN_BACKOFF_UPPERLIMITMAX 0
#endif
#ifndef CONFIG_BT_CTRL_AGC_RECORRECT_EN
#define CONFIG_BT_CTRL_AGC_RECORRECT_EN 0
#endif
#ifndef CONFIG_BT_CTRL_BLE_LLCP_CHAN_MAP_UPDATE
#define CONFIG_BT_CTRL_BLE_LLCP_CHAN_MAP_UPDATE 0
#endif
#ifndef CONFIG_BT_CTRL_BLE_LLCP_CONN_UPDATE
#define CONFIG_BT_CTRL_BLE_LLCP_CONN_UPDATE 0
#endif
#ifndef CONFIG_BT_CTRL_BLE_LLCP_PHY_UPDATE
#define CONFIG_BT_CTRL_BLE_LLCP_PHY_UPDATE 0
#endif
#ifndef CONFIG_BT_CTRL_BLE_TEST
#define CONFIG_BT_CTRL_BLE_TEST 0
#endif
#ifndef CONFIG_BT_CTRL_CHECK_CONNECT_IND_ACCESS_ADDRESS
#define CONFIG_BT_CTRL_CHECK_CONNECT_IND_ACCESS_ADDRESS 0
#endif
#ifndef CONFIG_BT_CTRL_CODED_AGC_RECORRECT_EN
#define CONFIG_BT_CTRL_CODED_AGC_RECORRECT_EN 0
#endif
#ifndef CONFIG_BT_CTRL_LE_LOG_EN
#define CONFIG_BT_CTRL_LE_LOG_EN 0
#endif
#ifndef CONFIG_BT_CTRL_LE_LOG_LEVEL
#define CONFIG_BT_CTRL_LE_LOG_LEVEL 0
#endif
#ifndef CONFIG_BT_CTRL_LE_LOG_MODE_EN
#define CONFIG_BT_CTRL_LE_LOG_MODE_EN 0
#endif
#ifndef CONFIG_BT_CTRL_RUN_IN_FLASH_ONLY
#define CONFIG_BT_CTRL_RUN_IN_FLASH_ONLY 0
#endif
#ifndef CONFIG_BT_CTRL_SCAN_BACKOFF_UPPERLIMITMAX
#define CONFIG_BT_CTRL_SCAN_BACKOFF_UPPERLIMITMAX 0
#endif

#if CONFIG_IDF_TARGET_ESP32C6 || CONFIG_IDF_TARGET_ESP32H2
#include <stdint.h>
#include "esp_err.h"

#ifdef __cplusplus
extern "C" {
#endif

typedef enum {
    ESP_BT_CONTROLLER_STATUS_IDLE = 0,
    ESP_BT_CONTROLLER_STATUS_INITED,
    ESP_BT_CONTROLLER_STATUS_ENABLED,
    ESP_BT_CONTROLLER_STATUS_NUM,
} esp_bt_controller_status_t;

typedef enum {
    ESP_BT_MODE_IDLE = 0x00,
    ESP_BT_MODE_BLE = 0x01,
    ESP_BT_MODE_CLASSIC_BT = 0x02,
    ESP_BT_MODE_BTDM = 0x03,
} esp_bt_mode_t;

/* Os campos do controlador regulam o radio de verdade: aqui nao mudam nada. */
typedef struct {
    uint32_t config_version;
} esp_bt_controller_config_t;
#define BT_CONTROLLER_INIT_CONFIG_DEFAULT() { 0 }

esp_err_t esp_bt_controller_init(esp_bt_controller_config_t *cfg);
esp_err_t esp_bt_controller_deinit(void);
esp_err_t esp_bt_controller_enable(esp_bt_mode_t mode);
esp_err_t esp_bt_controller_disable(void);
esp_bt_controller_status_t esp_bt_controller_get_status(void);

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
