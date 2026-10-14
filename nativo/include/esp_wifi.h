/* esp_wifi.h: o WiFi da placa, na rede da bancada. As redes que existem
 * sao as do painel Rede (a mesma lista que o WiFi.begin() do Arduino ve). */
#ifndef CS_ESP_WIFI_H
#define CS_ESP_WIFI_H

#include <stdint.h>
#include <stdbool.h>
#include "esp_err.h"
#include "esp_event.h"
#include "esp_netif.h"

typedef enum { WIFI_MODE_NULL = 0, WIFI_MODE_STA, WIFI_MODE_AP, WIFI_MODE_APSTA, WIFI_MODE_NAN, WIFI_MODE_MAX } wifi_mode_t;
typedef enum { WIFI_IF_STA = 0, WIFI_IF_AP = 1, WIFI_IF_NAN = 2, WIFI_IF_MAX } wifi_interface_t;
#define ESP_IF_WIFI_STA WIFI_IF_STA
#define ESP_IF_WIFI_AP WIFI_IF_AP
typedef wifi_interface_t esp_interface_t;

typedef enum {
    WIFI_AUTH_OPEN = 0, WIFI_AUTH_WEP, WIFI_AUTH_WPA_PSK, WIFI_AUTH_WPA2_PSK, WIFI_AUTH_WPA_WPA2_PSK,
    WIFI_AUTH_ENTERPRISE, WIFI_AUTH_WPA2_ENTERPRISE = WIFI_AUTH_ENTERPRISE, WIFI_AUTH_WPA3_PSK,
    WIFI_AUTH_WPA2_WPA3_PSK, WIFI_AUTH_WAPI_PSK, WIFI_AUTH_OWE, WIFI_AUTH_MAX
} wifi_auth_mode_t;

typedef enum { WIFI_PS_NONE, WIFI_PS_MIN_MODEM, WIFI_PS_MAX_MODEM } wifi_ps_type_t;
typedef enum { WIFI_FAST_SCAN = 0, WIFI_ALL_CHANNEL_SCAN } wifi_scan_method_t;
typedef enum { WIFI_CONNECT_AP_BY_SIGNAL = 0, WIFI_CONNECT_AP_BY_SECURITY } wifi_sort_method_t;
typedef enum { WIFI_STORAGE_FLASH, WIFI_STORAGE_RAM } wifi_storage_t;
typedef enum { WPA3_SAE_PWE_UNSPECIFIED, WPA3_SAE_PWE_HUNT_AND_PECK, WPA3_SAE_PWE_HASH_TO_ELEMENT,
               WPA3_SAE_PWE_BOTH } wifi_sae_pwe_method_t;

typedef struct { int8_t rssi; wifi_auth_mode_t authmode; } wifi_scan_threshold_t;
typedef struct { bool capable; bool required; } wifi_pmf_config_t;

typedef struct {
    uint8_t ssid[32];
    uint8_t password[64];
    wifi_scan_method_t scan_method;
    bool bssid_set;
    uint8_t bssid[6];
    uint8_t channel;
    uint16_t listen_interval;
    wifi_sort_method_t sort_method;
    wifi_scan_threshold_t threshold;
    wifi_pmf_config_t pmf_cfg;
    uint32_t rm_enabled : 1;
    uint32_t btm_enabled : 1;
    uint32_t mbo_enabled : 1;
    uint32_t ft_enabled : 1;
    uint32_t owe_enabled : 1;
    uint32_t transition_disable : 1;
    uint32_t reserved : 26;
    wifi_sae_pwe_method_t sae_pwe_h2e;
    uint8_t failure_retry_cnt;
} wifi_sta_config_t;

typedef struct {
    uint8_t ssid[32];
    uint8_t password[64];
    uint8_t ssid_len;
    uint8_t channel;
    wifi_auth_mode_t authmode;
    uint8_t ssid_hidden;
    uint8_t max_connection;
    uint16_t beacon_interval;
    wifi_pmf_config_t pmf_cfg;
} wifi_ap_config_t;

typedef union {
    wifi_ap_config_t ap;
    wifi_sta_config_t sta;
} wifi_config_t;

typedef struct {
    uint8_t bssid[6];
    uint8_t ssid[33];
    uint8_t primary;
    int second;
    int8_t rssi;
    wifi_auth_mode_t authmode;
    wifi_auth_mode_t pairwise_cipher;
    wifi_auth_mode_t group_cipher;
    int ant;
    uint32_t phy_11b : 1;
    uint32_t phy_11g : 1;
    uint32_t phy_11n : 1;
    uint32_t phy_lr : 1;
    uint32_t wps : 1;
    uint32_t ftm_responder : 1;
    uint32_t ftm_initiator : 1;
    uint32_t reserved : 25;
} wifi_ap_record_t;

typedef struct {
    int magic;
} wifi_init_config_t;

#define WIFI_INIT_CONFIG_MAGIC 0x1F2F3F4F
#define WIFI_INIT_CONFIG_DEFAULT() { .magic = WIFI_INIT_CONFIG_MAGIC }

typedef enum {
    WIFI_EVENT_WIFI_READY = 0,
    WIFI_EVENT_SCAN_DONE,
    WIFI_EVENT_STA_START,
    WIFI_EVENT_STA_STOP,
    WIFI_EVENT_STA_CONNECTED,
    WIFI_EVENT_STA_DISCONNECTED,
    WIFI_EVENT_STA_AUTHMODE_CHANGE,
    WIFI_EVENT_STA_WPS_ER_SUCCESS,
    WIFI_EVENT_STA_WPS_ER_FAILED,
    WIFI_EVENT_STA_WPS_ER_TIMEOUT,
    WIFI_EVENT_STA_WPS_ER_PIN,
    WIFI_EVENT_STA_WPS_ER_PBC_OVERLAP,
    WIFI_EVENT_AP_START,
    WIFI_EVENT_AP_STOP,
    WIFI_EVENT_AP_STACONNECTED,
    WIFI_EVENT_AP_STADISCONNECTED,
    WIFI_EVENT_AP_PROBEREQRECVED,
    WIFI_EVENT_MAX = 64
} wifi_event_t;

ESP_EVENT_DECLARE_BASE(WIFI_EVENT);

typedef struct {
    uint8_t ssid[32];
    uint8_t ssid_len;
    uint8_t bssid[6];
    uint8_t channel;
    wifi_auth_mode_t authmode;
    uint16_t aid;
} wifi_event_sta_connected_t;

typedef struct {
    uint8_t ssid[32];
    uint8_t ssid_len;
    uint8_t bssid[6];
    uint8_t reason;
    int8_t rssi;
} wifi_event_sta_disconnected_t;

typedef struct {
    uint32_t status;
    uint8_t number;
    uint8_t scan_id;
} wifi_event_sta_scan_done_t;

typedef enum {
    WIFI_REASON_UNSPECIFIED = 1,
    WIFI_REASON_AUTH_EXPIRE = 2,
    WIFI_REASON_ASSOC_LEAVE = 8,
    WIFI_REASON_4WAY_HANDSHAKE_TIMEOUT = 15,
    WIFI_REASON_BEACON_TIMEOUT = 200,
    WIFI_REASON_NO_AP_FOUND = 201,
    WIFI_REASON_AUTH_FAIL = 202,
    WIFI_REASON_ASSOC_FAIL = 203,
    WIFI_REASON_HANDSHAKE_TIMEOUT = 204,
    WIFI_REASON_CONNECTION_FAIL = 205,
} wifi_err_reason_t;

typedef struct {
    bool show_hidden;
    uint8_t *ssid;
    uint8_t *bssid;
    uint8_t channel;
    int scan_type;
    struct { uint32_t min; uint32_t max; } scan_time;
    uint8_t home_chan_dwell_time;
} wifi_scan_config_t;

esp_err_t esp_wifi_init(const wifi_init_config_t *config);
esp_err_t esp_wifi_deinit(void);
esp_err_t esp_wifi_set_mode(wifi_mode_t modo);
esp_err_t esp_wifi_get_mode(wifi_mode_t *modo);
esp_err_t esp_wifi_set_config(wifi_interface_t interface, wifi_config_t *config);
esp_err_t esp_wifi_get_config(wifi_interface_t interface, wifi_config_t *config);
esp_err_t esp_wifi_set_storage(wifi_storage_t onde);
esp_err_t esp_wifi_start(void);
esp_err_t esp_wifi_stop(void);
esp_err_t esp_wifi_connect(void);
esp_err_t esp_wifi_disconnect(void);
esp_err_t esp_wifi_set_ps(wifi_ps_type_t tipo);
esp_err_t esp_wifi_get_ps(wifi_ps_type_t *tipo);
esp_err_t esp_wifi_get_mac(wifi_interface_t interface, uint8_t mac[6]);
esp_err_t esp_wifi_set_mac(wifi_interface_t interface, const uint8_t mac[6]);
esp_err_t esp_wifi_sta_get_ap_info(wifi_ap_record_t *info);
esp_err_t esp_wifi_sta_get_rssi(int *rssi);
esp_err_t esp_wifi_scan_start(const wifi_scan_config_t *config, bool bloquear);
esp_err_t esp_wifi_scan_stop(void);
esp_err_t esp_wifi_scan_get_ap_num(uint16_t *quantos);
esp_err_t esp_wifi_scan_get_ap_records(uint16_t *quantos, wifi_ap_record_t *lista);
esp_err_t esp_wifi_clear_ap_list(void);
esp_err_t esp_wifi_set_max_tx_power(int8_t potencia);
esp_err_t esp_wifi_get_max_tx_power(int8_t *potencia);

#endif
