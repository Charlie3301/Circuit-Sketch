/* esp_netif.h: a interface de rede da placa. O IP e o que a rede da
 * bancada deu (10.0.0.x); o navegador do computador chega nos servidores
 * pela ponte em localhost, que o painel Rede mostra. */
#ifndef CS_ESP_NETIF_H
#define CS_ESP_NETIF_H

#include <stdint.h>
#include <stdbool.h>
#include "esp_err.h"
#include "esp_event.h"

typedef struct { uint32_t addr; } esp_ip4_addr_t;
typedef struct { uint32_t addr[4]; uint8_t zone; } esp_ip6_addr_t;
typedef struct {
    union { esp_ip6_addr_t ip6; esp_ip4_addr_t ip4; } u_addr;
    uint8_t type;
} esp_ip_addr_t;

typedef struct {
    esp_ip4_addr_t ip;
    esp_ip4_addr_t netmask;
    esp_ip4_addr_t gw;
} esp_netif_ip_info_t;

#define esp_ip4_addr_get_byte(ipaddr, idx) (((const uint8_t *)(&(ipaddr)->addr))[idx])
#define esp_ip4_addr1(ipaddr) esp_ip4_addr_get_byte(ipaddr, 0)
#define esp_ip4_addr2(ipaddr) esp_ip4_addr_get_byte(ipaddr, 1)
#define esp_ip4_addr3(ipaddr) esp_ip4_addr_get_byte(ipaddr, 2)
#define esp_ip4_addr4(ipaddr) esp_ip4_addr_get_byte(ipaddr, 3)
#define esp_ip4_addr1_16(ipaddr) ((uint16_t)esp_ip4_addr1(ipaddr))
#define esp_ip4_addr2_16(ipaddr) ((uint16_t)esp_ip4_addr2(ipaddr))
#define esp_ip4_addr3_16(ipaddr) ((uint16_t)esp_ip4_addr3(ipaddr))
#define esp_ip4_addr4_16(ipaddr) ((uint16_t)esp_ip4_addr4(ipaddr))
#define IP2STR(ipaddr) esp_ip4_addr1_16(ipaddr), esp_ip4_addr2_16(ipaddr), esp_ip4_addr3_16(ipaddr), esp_ip4_addr4_16(ipaddr)
#define IPSTR "%d.%d.%d.%d"
#define ESP_IP4TOADDR(a, b, c, d) ((uint32_t)(((uint32_t)(d) << 24) | ((uint32_t)(c) << 16) | ((uint32_t)(b) << 8) | (uint32_t)(a)))
#define ESP_IP4TOUINT32(a, b, c, d) ESP_IP4TOADDR(a, b, c, d)
#define MACSTR "%02x:%02x:%02x:%02x:%02x:%02x"
#define MAC2STR(a) (a)[0], (a)[1], (a)[2], (a)[3], (a)[4], (a)[5]

typedef struct esp_netif_obj esp_netif_t;

typedef enum { IP_EVENT_STA_GOT_IP, IP_EVENT_STA_LOST_IP, IP_EVENT_AP_STAIPASSIGNED, IP_EVENT_GOT_IP6,
               IP_EVENT_ETH_GOT_IP, IP_EVENT_ETH_LOST_IP, IP_EVENT_PPP_GOT_IP, IP_EVENT_PPP_LOST_IP } ip_event_t;

ESP_EVENT_DECLARE_BASE(IP_EVENT);

typedef struct {
    esp_netif_t *esp_netif;
    esp_netif_ip_info_t ip_info;
    bool ip_changed;
} ip_event_got_ip_t;

typedef struct {
    esp_netif_t *esp_netif;
    esp_ip4_addr_t ip;
    uint8_t mac[6];
} ip_event_ap_staipassigned_t;

esp_err_t esp_netif_init(void);
esp_err_t esp_netif_deinit(void);
esp_netif_t *esp_netif_create_default_wifi_sta(void);
esp_netif_t *esp_netif_create_default_wifi_ap(void);
void esp_netif_destroy(esp_netif_t *netif);
void esp_netif_destroy_default_wifi(void *netif);
esp_err_t esp_netif_get_ip_info(esp_netif_t *netif, esp_netif_ip_info_t *info);
esp_err_t esp_netif_set_hostname(esp_netif_t *netif, const char *nome);
esp_err_t esp_netif_get_hostname(esp_netif_t *netif, const char **nome);
esp_err_t esp_netif_get_mac(esp_netif_t *netif, uint8_t mac[]);
esp_netif_t *esp_netif_get_handle_from_ifkey(const char *chave);
bool esp_netif_is_netif_up(esp_netif_t *netif);
char *esp_ip4addr_ntoa(const esp_ip4_addr_t *addr, char *buf, int n);
uint32_t esp_ip4addr_aton(const char *texto);
esp_err_t esp_netif_str_to_ip4(const char *texto, esp_ip4_addr_t *addr);

#endif
