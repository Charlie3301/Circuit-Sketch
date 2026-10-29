/* esp_wifi.h -- o pedaco do WiFi do ESP-IDF que os sketches de ESP-NOW usam. */
#ifndef __ESP_WIFI_H__
#define __ESP_WIFI_H__

#include "WiFi.h"

typedef enum { WIFI_IF_STA = 0, WIFI_IF_AP = 1 } wifi_interface_t;
typedef enum { WIFI_SECOND_CHAN_NONE = 0, WIFI_SECOND_CHAN_ABOVE, WIFI_SECOND_CHAN_BELOW } wifi_second_chan_t;
typedef enum { WIFI_PS_NONE = 0, WIFI_PS_MIN_MODEM, WIFI_PS_MAX_MODEM } wifi_ps_type_t;
typedef enum { WIFI_PROTOCOL_11B = 1, WIFI_PROTOCOL_11G = 2, WIFI_PROTOCOL_11N = 4, WIFI_PROTOCOL_LR = 8 } wifi_protocol_bits_t;

typedef struct {
    const uint8_t *des_addr;
    const uint8_t *src_addr;
    wifi_interface_t ifidx;
    uint8_t *data;
    int data_len;
} wifi_tx_info_t;

esp_err_t esp_wifi_set_channel(uint8_t primary, wifi_second_chan_t second);
esp_err_t esp_wifi_get_mac(wifi_interface_t ifx, uint8_t mac[6]);
esp_err_t esp_wifi_set_ps(wifi_ps_type_t type);
esp_err_t esp_wifi_set_protocol(wifi_interface_t ifx, uint8_t protocol_bitmap);
esp_err_t esp_wifi_set_max_tx_power(int8_t power);

#endif
