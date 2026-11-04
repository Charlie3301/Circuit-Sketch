/* qrcode.h -- a API do componente qrcode da Espressif (o RMakerQR.h e o
 * provisionamento usam). O simulador nao desenha o QR no monitor: o sketch ve
 * a mesma API, e o core imprime a URL com os dados (src/cs_rainmaker.cpp). */
#pragma once

#include <stdint.h>
#include "esp_err.h"

#ifdef __cplusplus
extern "C" {
#endif

typedef const uint8_t *esp_qrcode_handle_t;

enum {
    ESP_QRCODE_ECC_LOW,
    ESP_QRCODE_ECC_MED,
    ESP_QRCODE_ECC_QUART,
    ESP_QRCODE_ECC_HIGH,
};

typedef struct {
    void (*display_func)(esp_qrcode_handle_t qrcode);
    int max_qrcode_version;
    int qrcode_ecc_level;
} esp_qrcode_config_t;

void esp_qrcode_print_console(esp_qrcode_handle_t qrcode);
esp_err_t esp_qrcode_generate(esp_qrcode_config_t *cfg, const char *text);

#define ESP_QRCODE_CONFIG_DEFAULT() \
    (esp_qrcode_config_t) { esp_qrcode_print_console, 10, ESP_QRCODE_ECC_LOW }

#ifdef __cplusplus
}
#endif
