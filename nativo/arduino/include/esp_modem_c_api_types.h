/* esp_modem_c_api_types.h -- os tipos da API do esp_modem que o PPP.h do core
 * usa (o componente esp_modem da Espressif). Os mesmos nomes e a mesma ordem;
 * no simulador nao ha modem no circuito (src/cs_rapidos.cpp). */
#pragma once

#ifdef __cplusplus
extern "C" {
#endif

typedef struct esp_modem_dce_wrap esp_modem_dce_t;

typedef enum {
    ESP_MODEM_MODE_COMMAND,
    ESP_MODEM_MODE_DATA,
    ESP_MODEM_MODE_CMUX,
    ESP_MODEM_MODE_CMUX_MANUAL,
    ESP_MODEM_MODE_CMUX_MANUAL_EXIT,
    ESP_MODEM_MODE_CMUX_MANUAL_SWAP,
    ESP_MODEM_MODE_CMUX_MANUAL_DATA,
    ESP_MODEM_MODE_CMUX_MANUAL_COMMAND,
    ESP_MODEM_MODE_DETECT,
    ESP_MODEM_MODE_UNDEF,
} esp_modem_dce_mode_t;

typedef enum {
    ESP_MODEM_DCE_GENETIC,
    ESP_MODEM_DCE_SIM7600,
    ESP_MODEM_DCE_SIM7070,
    ESP_MODEM_DCE_SIM7000,
    ESP_MODEM_DCE_BG96,
    ESP_MODEM_DCE_EC20,
    ESP_MODEM_DCE_SIM800,
    ESP_MODEM_DCE_CUSTOM,
} esp_modem_dce_device_t;

typedef enum {
    ESP_MODEM_FLOW_CONTROL_NONE = 0,
    ESP_MODEM_FLOW_CONTROL_SW,
    ESP_MODEM_FLOW_CONTROL_HW,
} esp_modem_flow_ctrl_t;

#ifdef __cplusplus
}
#endif
