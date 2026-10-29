/* esp_now.h -- o ESP-NOW do ESP-IDF (o cabecalho de verdade, logo abaixo),
 * pelo simulador (esp_now.cpp): as placas da bancada trocam pacotes direto,
 * pelo MAC, sem roteador.
 *
 * Alem do do IDF 5.5 (o callback com esp_now_recv_info_t e com
 * wifi_tx_info_t), o simulador aceita as formas do core 2.x, com o MAC no
 * lugar da estrutura -- as dos tutoriais mais antigos. */
#ifndef CS_ESP_NOW_H
#define CS_ESP_NOW_H

#include_next <esp_now.h>

#ifdef __cplusplus
typedef void (*esp_now_recv_cb_v2_t)(const uint8_t *mac_addr, const uint8_t *data, int data_len);
typedef void (*esp_now_send_cb_v2_t)(const uint8_t *mac_addr, esp_now_send_status_t status);
esp_err_t esp_now_register_recv_cb(esp_now_recv_cb_v2_t cb);
esp_err_t esp_now_register_send_cb(esp_now_send_cb_v2_t cb);
#endif

#endif
