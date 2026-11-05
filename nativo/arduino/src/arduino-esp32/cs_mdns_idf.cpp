/* cs_mdns_idf.cpp -- o mdns.h do ESP-IDF sobre o mDNS da bancada (o mesmo do ESPmDNS.h): a placa
 * responde por "nome.local", anuncia os servicos e acha os das outras placas. */
#include <Arduino.h>
#include <stdlib.h>
#include <string.h>
#include "IPAddress.h"
#include "cs_objeto.h"
#include "mdns.h"

static String nomeDaPlaca;
static bool ligado;

/* O IDF escreve "_http" e "_tcp"; a bancada guarda sem o sublinhado, como o ESPmDNS. */
static const char *semSublinhado(const char *s) { return s && *s == '_' ? s + 1 : s; }

static char *copia(const String &s)
{
    char *c = (char *)malloc(s.length() + 1);
    memcpy(c, s.c_str(), s.length() + 1);
    return c;
}

extern "C" {

esp_err_t mdns_init(void)
{
    ligado = true;
    return ESP_OK;
}

void mdns_free(void)
{
    if (nomeDaPlaca.length()) cs_obj_n(nullptr, "__mdns", "");
    nomeDaPlaca = "";
    ligado = false;
}

esp_err_t mdns_hostname_set(const char *hostname)
{
    if (!ligado) return ESP_ERR_INVALID_STATE;
    if (!hostname || !*hostname || strlen(hostname) > 63) return ESP_ERR_INVALID_ARG;
    nomeDaPlaca = hostname;
    nomeDaPlaca.toLowerCase();
    return cs_obj_i(nullptr, "__mdns", nomeDaPlaca) ? ESP_OK : ESP_FAIL;
}

esp_err_t mdns_hostname_get(char *hostname)
{
    if (!hostname) return ESP_ERR_INVALID_ARG;
    if (!ligado || !nomeDaPlaca.length()) return ESP_ERR_INVALID_STATE;
    strcpy(hostname, nomeDaPlaca.c_str());
    return ESP_OK;
}

esp_err_t mdns_instance_name_set(const char *instance_name) { return ligado ? (instance_name ? ESP_OK : ESP_ERR_INVALID_ARG) : ESP_ERR_INVALID_STATE; }

esp_err_t mdns_service_add(const char *instance_name, const char *service_type, const char *proto, uint16_t port,
                           mdns_txt_item_t txt[], size_t num_items)
{
    (void)instance_name;
    (void)txt;
    (void)num_items;
    if (!ligado || !nomeDaPlaca.length()) return ESP_ERR_INVALID_STATE;
    if (!service_type || !proto || !port) return ESP_ERR_INVALID_ARG;
    return cs_obj_i(nullptr, "__mdns_servico", semSublinhado(service_type), semSublinhado(proto), (int)port) ? ESP_OK : ESP_FAIL;
}

/* A bancada nao tira um servico sozinho: so o mDNS inteiro (o __mdns vazio). */
esp_err_t mdns_service_remove(const char *service_type, const char *proto) { return service_type && proto ? ESP_OK : ESP_ERR_INVALID_ARG; }
esp_err_t mdns_service_remove_all(void) { return ESP_OK; }
bool mdns_service_exists(const char *, const char *, const char *) { return ligado && nomeDaPlaca.length(); }
esp_err_t mdns_service_instance_name_set(const char *service_type, const char *proto, const char *instance_name)
{
    return service_type && proto && instance_name ? ESP_OK : ESP_ERR_INVALID_ARG;
}
esp_err_t mdns_service_port_set(const char *service_type, const char *proto, uint16_t port)
{
    if (!service_type || !proto || !port) return ESP_ERR_INVALID_ARG;
    return cs_obj_i(nullptr, "__mdns_servico", semSublinhado(service_type), semSublinhado(proto), (int)port) ? ESP_OK : ESP_FAIL;
}
esp_err_t mdns_service_txt_set(const char *service_type, const char *proto, mdns_txt_item_t[], uint8_t)
{
    return service_type && proto ? ESP_OK : ESP_ERR_INVALID_ARG;
}
esp_err_t mdns_service_txt_item_set(const char *service_type, const char *proto, const char *key, const char *value)
{
    return service_type && proto && key && value ? ESP_OK : ESP_ERR_INVALID_ARG;
}
esp_err_t mdns_service_txt_item_remove(const char *service_type, const char *proto, const char *key)
{
    return service_type && proto && key ? ESP_OK : ESP_ERR_INVALID_ARG;
}

esp_err_t mdns_query_a(const char *host_name, uint32_t timeout, esp_ip4_addr_t *addr)
{
    (void)timeout;
    if (!host_name || !addr) return ESP_ERR_INVALID_ARG;
    String achado = cs_obj_t(nullptr, "__dns", String(host_name) + ".local");
    IPAddress ip;
    if (!achado.length() || !ip.fromString(achado)) return ESP_ERR_NOT_FOUND;
    addr->addr = (uint32_t)ip;
    return ESP_OK;
}

esp_err_t mdns_query_aaaa(const char *host_name, uint32_t, esp_ip6_addr_t *addr) { return host_name && addr ? ESP_ERR_NOT_FOUND : ESP_ERR_INVALID_ARG; }

/* O PTR: as placas da bancada que anunciaram o servico, em "nome|ip|porta" por linha. */
esp_err_t mdns_query_ptr(const char *service_type, const char *proto, uint32_t timeout, size_t max_results, mdns_result_t **results)
{
    (void)timeout;
    if (!service_type || !proto || !results) return ESP_ERR_INVALID_ARG;
    *results = nullptr;
    String lista = cs_obj_t(nullptr, "__mdns_buscar", semSublinhado(service_type), semSublinhado(proto));
    mdns_result_t **fim = results;
    size_t n = 0;
    int ini = 0;
    while (ini < (int)lista.length() && (!max_results || n < max_results)) {
        int quebra = lista.indexOf('\n', ini);
        if (quebra < 0) quebra = lista.length();
        String linha = lista.substring(ini, quebra);
        ini = quebra + 1;
        int a = linha.indexOf('|'), b = linha.lastIndexOf('|');
        if (a <= 0 || b <= a) continue;
        auto *r = (mdns_result_t *)calloc(1, sizeof(mdns_result_t));
        r->hostname = copia(linha.substring(0, a));
        r->instance_name = copia(linha.substring(0, a));
        r->service_type = copia(service_type);
        r->proto = copia(proto);
        r->port = (uint16_t)linha.substring(b + 1).toInt();
        r->ttl = 120;
        r->ip_protocol = MDNS_IP_PROTOCOL_V4;
        IPAddress ip;
        if (ip.fromString(linha.substring(a + 1, b))) {
            r->addr = (mdns_ip_addr_t *)calloc(1, sizeof(mdns_ip_addr_t));
            r->addr->addr.type = ESP_IPADDR_TYPE_V4;
            r->addr->addr.u_addr.ip4.addr = (uint32_t)ip;
        }
        *fim = r;
        fim = &r->next;
        n++;
    }
    return ESP_OK;
}

esp_err_t mdns_query_srv(const char *instance_name, const char *service_type, const char *proto, uint32_t timeout, mdns_result_t **result)
{
    (void)instance_name;
    return mdns_query_ptr(service_type, proto, timeout, 1, result);
}

esp_err_t mdns_query_txt(const char *instance_name, const char *service_type, const char *proto, uint32_t timeout, mdns_result_t **result)
{
    (void)instance_name;
    return mdns_query_ptr(service_type, proto, timeout, 1, result);
}

esp_err_t mdns_query(const char *name, const char *service_type, const char *proto, uint16_t type, uint32_t timeout,
                     size_t max_results, mdns_result_t **results)
{
    if (!results) return ESP_ERR_INVALID_ARG;
    *results = nullptr;
    if (type == MDNS_TYPE_A && name) {
        esp_ip4_addr_t ip;
        if (mdns_query_a(name, timeout, &ip) != ESP_OK) return ESP_OK;
        auto *r = (mdns_result_t *)calloc(1, sizeof(mdns_result_t));
        r->hostname = copia(name);
        r->addr = (mdns_ip_addr_t *)calloc(1, sizeof(mdns_ip_addr_t));
        r->addr->addr.type = ESP_IPADDR_TYPE_V4;
        r->addr->addr.u_addr.ip4 = ip;
        *results = r;
        return ESP_OK;
    }
    return mdns_query_ptr(service_type, proto, timeout, max_results, results);
}

void mdns_query_results_free(mdns_result_t *results)
{
    while (results) {
        mdns_result_t *proximo = results->next;
        free(results->hostname);
        free(results->instance_name);
        free(results->service_type);
        free(results->proto);
        while (results->addr) {
            mdns_ip_addr_t *a = results->addr->next;
            free(results->addr);
            results->addr = a;
        }
        free(results);
        results = proximo;
    }
}

} // extern "C"
