/* esp_now.h -- o ESP-NOW (placa com placa, sem roteador) pelo ar da bancada.
 *
 * O que o sketch manda num esp_now_send() chega byte a byte no callback da
 * outra placa. O callback de recebimento tem as duas assinaturas: a do core
 * 3.x (esp_now_recv_info_t) e a do 2.x (o MAC); o de envio, a do status. */
#ifndef __ESP_NOW_H__
#define __ESP_NOW_H__

#include "cs_objeto.h"
#include "esp_wifi.h"

#define ESP_NOW_ETH_ALEN 6
#define ESP_NOW_KEY_LEN 16
#define ESP_NOW_MAX_TOTAL_PEER_NUM 20
#define ESP_NOW_MAX_ENCRYPT_PEER_NUM 6
#define ESP_NOW_MAX_DATA_LEN 250

#define ESP_ERR_ESPNOW_BASE 0x3064
#define ESP_ERR_ESPNOW_NOT_INIT 0x3065
#define ESP_ERR_ESPNOW_ARG 0x3066
#define ESP_ERR_ESPNOW_NO_MEM 0x3067
#define ESP_ERR_ESPNOW_FULL 0x3068
#define ESP_ERR_ESPNOW_NOT_FOUND 0x3069
#define ESP_ERR_ESPNOW_INTERNAL 0x306A
#define ESP_ERR_ESPNOW_EXIST 0x306B
#define ESP_ERR_ESPNOW_IF 0x306C

typedef enum { ESP_NOW_SEND_SUCCESS = 0, ESP_NOW_SEND_FAIL } esp_now_send_status_t;

typedef struct {
    uint8_t peer_addr[ESP_NOW_ETH_ALEN];
    uint8_t lmk[ESP_NOW_KEY_LEN];
    uint8_t channel;
    wifi_interface_t ifidx;
    bool encrypt;
    void *priv;
} esp_now_peer_info_t;

typedef struct {
    signed rssi : 8;
    unsigned channel : 4;
} wifi_pkt_rx_ctrl_t;

typedef struct {
    uint8_t *src_addr;
    uint8_t *des_addr;
    wifi_pkt_rx_ctrl_t *rx_ctrl;
} esp_now_recv_info_t;

typedef struct {
    const uint8_t *des_addr;
    const uint8_t *src_addr;
    wifi_interface_t ifidx;
} esp_now_send_info_t;

typedef void (*esp_now_recv_cb_t)(const esp_now_recv_info_t *esp_now_info, const uint8_t *data, int data_len);
typedef void (*esp_now_recv_cb_v2_t)(const uint8_t *mac_addr, const uint8_t *data, int data_len);
typedef void (*esp_now_send_cb_t)(const uint8_t *mac_addr, esp_now_send_status_t status);
typedef void (*esp_now_send_cb_v3_t)(const wifi_tx_info_t *tx_info, esp_now_send_status_t status);

esp_err_t esp_now_init(void);
esp_err_t esp_now_deinit(void);
esp_err_t esp_now_add_peer(const esp_now_peer_info_t *peer);
esp_err_t esp_now_del_peer(const uint8_t *peer_addr);
bool esp_now_is_peer_exist(const uint8_t *peer_addr);
esp_err_t esp_now_send(const uint8_t *peer_addr, const uint8_t *data, size_t len);
esp_err_t esp_now_set_pmk(const uint8_t *pmk);
esp_err_t esp_now_unregister_recv_cb(void);
esp_err_t esp_now_unregister_send_cb(void);
esp_err_t esp_now_register_recv_cb(esp_now_recv_cb_t cb);
esp_err_t esp_now_register_recv_cb(esp_now_recv_cb_v2_t cb);
esp_err_t esp_now_register_send_cb(esp_now_send_cb_t cb);
esp_err_t esp_now_register_send_cb(esp_now_send_cb_v3_t cb);

#endif
