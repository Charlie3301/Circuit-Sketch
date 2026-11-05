/* mdns.h -- o mDNS do ESP-IDF (o componente espressif/mdns, que o arduino-esp32 traz), por cima do
 * mDNS da rede da bancada: o mesmo nome ".local" e os mesmos servicos que o ESPmDNS.h anuncia. */
#ifndef CS_MDNS_H
#define CS_MDNS_H

#include <stddef.h>
#include <stdint.h>
#include "esp_err.h"
#include "esp_netif_ip_addr.h"

#ifdef __cplusplus
extern "C" {
#endif

#define MDNS_TYPE_A 0x0001
#define MDNS_TYPE_PTR 0x000C
#define MDNS_TYPE_TXT 0x0010
#define MDNS_TYPE_AAAA 0x001C
#define MDNS_TYPE_SRV 0x0021
#define MDNS_TYPE_OPT 0x0029
#define MDNS_TYPE_NSEC 0x002F
#define MDNS_TYPE_ANY 0x00FF

typedef enum { MDNS_IP_PROTOCOL_V4, MDNS_IP_PROTOCOL_V6, MDNS_IP_PROTOCOL_MAX } mdns_ip_protocol_t;

typedef struct {
    const char *key;
    const char *value;
} mdns_txt_item_t;

typedef struct mdns_ip_addr_s {
    esp_ip_addr_t addr;
    struct mdns_ip_addr_s *next;
} mdns_ip_addr_t;

typedef struct mdns_result_s {
    struct mdns_result_s *next;
    void *esp_netif;
    uint32_t ttl;
    mdns_ip_protocol_t ip_protocol;
    char *instance_name;
    char *service_type;
    char *proto;
    char *hostname;
    uint16_t port;
    mdns_txt_item_t *txt;
    uint8_t *txt_value_len;
    size_t txt_count;
    mdns_ip_addr_t *addr;
} mdns_result_t;

esp_err_t mdns_init(void);
void mdns_free(void);
esp_err_t mdns_hostname_set(const char *hostname);
esp_err_t mdns_hostname_get(char *hostname);
esp_err_t mdns_instance_name_set(const char *instance_name);
esp_err_t mdns_service_add(const char *instance_name, const char *service_type, const char *proto, uint16_t port,
                           mdns_txt_item_t txt[], size_t num_items);
esp_err_t mdns_service_remove(const char *service_type, const char *proto);
esp_err_t mdns_service_remove_all(void);
bool mdns_service_exists(const char *service_type, const char *proto, const char *hostname);
esp_err_t mdns_service_instance_name_set(const char *service_type, const char *proto, const char *instance_name);
esp_err_t mdns_service_port_set(const char *service_type, const char *proto, uint16_t port);
esp_err_t mdns_service_txt_set(const char *service_type, const char *proto, mdns_txt_item_t txt[], uint8_t num_items);
esp_err_t mdns_service_txt_item_set(const char *service_type, const char *proto, const char *key, const char *value);
esp_err_t mdns_service_txt_item_remove(const char *service_type, const char *proto, const char *key);
esp_err_t mdns_query(const char *name, const char *service_type, const char *proto, uint16_t type, uint32_t timeout,
                     size_t max_results, mdns_result_t **results);
esp_err_t mdns_query_ptr(const char *service_type, const char *proto, uint32_t timeout, size_t max_results, mdns_result_t **results);
esp_err_t mdns_query_srv(const char *instance_name, const char *service_type, const char *proto, uint32_t timeout, mdns_result_t **result);
esp_err_t mdns_query_txt(const char *instance_name, const char *service_type, const char *proto, uint32_t timeout, mdns_result_t **result);
esp_err_t mdns_query_a(const char *host_name, uint32_t timeout, esp_ip4_addr_t *addr);
esp_err_t mdns_query_aaaa(const char *host_name, uint32_t timeout, esp_ip6_addr_t *addr);
void mdns_query_results_free(mdns_result_t *results);

#ifdef __cplusplus
}
#endif

#endif
