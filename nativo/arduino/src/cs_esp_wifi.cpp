/* cs_esp_wifi.cpp -- o WiFi do ESP-IDF (esp_wifi.h, esp_netif.h, os
 * cabecalhos de verdade) sobre a rede da bancada do simulador.
 *
 * O programa do IDF faz o caminho do exemplo "station": esp_netif_init,
 * esp_event_loop_create_default, esp_netif_create_default_wifi_sta,
 * esp_wifi_init, esp_wifi_set_mode, esp_wifi_set_config, esp_wifi_start e,
 * no WIFI_EVENT_STA_START, esp_wifi_connect. Os eventos (WIFI_EVENT,
 * IP_EVENT) chegam pelo laco padrao, os mesmos que o WiFi do Arduino gera
 * (cs_wifi.cpp) -- a rede e uma so. E o ESP-NOW que falta (os pares). */
#include "Arduino.h"
#if defined(ESP32)
#include "soc/soc_caps.h"
#if SOC_WIFI_SUPPORTED
#include "WiFi.h"
#include "esp_wifi.h"
#include "esp_netif.h"
#include "esp_wifi_default.h"
#include "esp_now.h"
#include "cs_objeto.h"
#include <vector>

ESP_EVENT_DEFINE_BASE(WIFI_EVENT);
ESP_EVENT_DEFINE_BASE(IP_EVENT);

/* O que o WIFI_INIT_CONFIG_DEFAULT() aponta (as funcoes do sistema que o
 * driver binario da placa usa): aqui so precisam existir. */
/* (No IDF, a tabela de funcoes do sistema operacional para o driver.) */
struct wifi_osi_funcs_t {
    int32_t _version;
};
wifi_osi_funcs_t g_wifi_osi_funcs;
const wpa_crypto_funcs_t g_wifi_default_wpa_crypto_funcs = {};

void cs_wifi_ligar_sta();
void cs_wifi_ligar_ap(const char *ssid);
void cs_wifi_desligar_sta();
void cs_wifi_varredura_terminou(int16_t n);

/* As interfaces de rede do IDF: a do STA e a do AP. */
struct esp_netif_obj {
    bool ap;
    char chave[16];
    char desc[8];
    char nome[33];
    bool dhcp = true;
};

namespace {

bool iniciado = false, ligado = false;
wifi_mode_t modo = WIFI_MODE_NULL;
wifi_config_t configSta = {}, configAp = {};
wifi_ps_type_t sono = WIFI_PS_MIN_MODEM;
int8_t potencia = 80;
uint8_t protocolo[2] = {WIFI_PROTOCOL_11B | WIFI_PROTOCOL_11G | WIFI_PROTOCOL_11N, WIFI_PROTOCOL_11B | WIFI_PROTOCOL_11G | WIFI_PROTOCOL_11N};
wifi_bandwidth_t banda[2] = {WIFI_BW_HT40, WIFI_BW_HT40};
wifi_country_t pais = {"01", 1, 11, 20, WIFI_COUNTRY_POLICY_AUTO};
bool promiscuo = false;
esp_netif_obj interfaceSta = {false, "WIFI_STA_DEF", "sta", ""};
esp_netif_obj interfaceAp = {true, "WIFI_AP_DEF", "ap", ""};
bool netifIniciado = false;
std::vector<wifi_ap_record_t> *varredura;

void ipDe(IPAddress ip, esp_ip4_addr_t &saida) { saida.addr = (uint32_t)ip; }

}  // namespace

/* =====================================================================
 * esp_wifi */
extern "C" esp_err_t esp_wifi_init(const wifi_init_config_t *config)
{
    if (!config) return ESP_ERR_INVALID_ARG;
    iniciado = true;
    return ESP_OK;
}
extern "C" esp_err_t esp_wifi_deinit(void)
{
    if (ligado) return ESP_ERR_WIFI_NOT_STOPPED;
    iniciado = false;
    return ESP_OK;
}
extern "C" esp_err_t esp_wifi_set_mode(wifi_mode_t mode)
{
    if (!iniciado) return ESP_ERR_WIFI_NOT_INIT;
    if (mode >= WIFI_MODE_MAX) return ESP_ERR_INVALID_ARG;
    modo = mode;
    return ESP_OK;
}
extern "C" esp_err_t esp_wifi_get_mode(wifi_mode_t *mode)
{
    if (!iniciado) return ESP_ERR_WIFI_NOT_INIT;
    if (!mode) return ESP_ERR_INVALID_ARG;
    *mode = modo;
    return ESP_OK;
}

extern "C" esp_err_t esp_wifi_set_config(wifi_interface_t interface, wifi_config_t *conf)
{
    if (!iniciado) return ESP_ERR_WIFI_NOT_INIT;
    if (!conf) return ESP_ERR_INVALID_ARG;
    if (interface == WIFI_IF_STA) configSta = *conf;
    else if (interface == WIFI_IF_AP) configAp = *conf;
    else return ESP_ERR_INVALID_ARG;
    return ESP_OK;
}
extern "C" esp_err_t esp_wifi_get_config(wifi_interface_t interface, wifi_config_t *conf)
{
    if (!iniciado) return ESP_ERR_WIFI_NOT_INIT;
    if (!conf) return ESP_ERR_INVALID_ARG;
    *conf = interface == WIFI_IF_AP ? configAp : configSta;
    return ESP_OK;
}

/* O start sobe as interfaces do modo: o STA (WIFI_EVENT_STA_START) e o AP
 * (com a rede de configAp). Conectar e com o esp_wifi_connect. */
extern "C" esp_err_t esp_wifi_start(void)
{
    if (!iniciado) return ESP_ERR_WIFI_NOT_INIT;
    ligado = true;
    WiFi.mode(modo);
    if (modo == WIFI_MODE_AP || modo == WIFI_MODE_APSTA) {
        char ssid[33] = {0};
        memcpy(ssid, configAp.ap.ssid, configAp.ap.ssid_len ? configAp.ap.ssid_len : 32);
        const char *senha = configAp.ap.password[0] ? (const char *)configAp.ap.password : nullptr;
        WiFi.softAP(ssid[0] ? ssid : "ESP_AP", senha);
    }
    if (modo == WIFI_MODE_STA || modo == WIFI_MODE_APSTA) cs_wifi_ligar_sta();
    return ESP_OK;
}
extern "C" esp_err_t esp_wifi_stop(void)
{
    if (!iniciado) return ESP_ERR_WIFI_NOT_INIT;
    if (ligado) {
        WiFi.disconnect(false, false);
        cs_wifi_desligar_sta();
        if (modo == WIFI_MODE_AP || modo == WIFI_MODE_APSTA) WiFi.softAPdisconnect(false);
    }
    ligado = false;
    return ESP_OK;
}
extern "C" esp_err_t esp_wifi_restore(void) { return ESP_OK; }

extern "C" esp_err_t esp_wifi_connect(void)
{
    if (!iniciado) return ESP_ERR_WIFI_NOT_INIT;
    if (!ligado) return ESP_ERR_WIFI_NOT_STARTED;
    if (modo != WIFI_MODE_STA && modo != WIFI_MODE_APSTA) return ESP_ERR_WIFI_MODE;
    char ssid[33] = {0};
    memcpy(ssid, configSta.sta.ssid, 32);
    if (!ssid[0]) return ESP_ERR_WIFI_SSID;
    WiFi.begin(ssid, configSta.sta.password[0] ? (const char *)configSta.sta.password : nullptr);
    return ESP_OK;
}
extern "C" esp_err_t esp_wifi_disconnect(void)
{
    if (!iniciado) return ESP_ERR_WIFI_NOT_INIT;
    WiFi.disconnect(false, false);
    return ESP_OK;
}
extern "C" esp_err_t esp_wifi_clear_fast_connect(void) { return ESP_OK; }
extern "C" esp_err_t esp_wifi_deauth_sta(uint16_t aid)
{
    (void)aid;
    return ESP_OK;
}

/* A varredura: as redes que o WiFi.scanNetworks acha, com o canal e o BSSID. */
extern "C" esp_err_t esp_wifi_scan_start(const wifi_scan_config_t *config, bool block)
{
    (void)config;
    (void)block;
    if (!iniciado) return ESP_ERR_WIFI_NOT_INIT;
    if (!ligado) return ESP_ERR_WIFI_NOT_STARTED;
    int16_t n = WiFi.scanNetworks();
    if (!varredura) varredura = new std::vector<wifi_ap_record_t>();
    varredura->clear();
    for (int16_t i = 0; i < n; i++) {
        wifi_ap_record_t r = {};
        snprintf((char *)r.ssid, sizeof(r.ssid), "%s", WiFi.SSID(i).c_str());
        WiFi.BSSID(i, r.bssid);
        r.primary = (uint8_t)WiFi.channel(i);
        r.rssi = (int8_t)WiFi.RSSI(i);
        r.authmode = WiFi.encryptionType(i);
        r.phy_11b = r.phy_11g = r.phy_11n = 1;
        r.country = pais;
        varredura->push_back(r);
    }
    cs_wifi_varredura_terminou(n);
    return ESP_OK;
}
extern "C" esp_err_t esp_wifi_scan_stop(void) { return ESP_OK; }
extern "C" esp_err_t esp_wifi_scan_get_ap_num(uint16_t *number)
{
    if (!number) return ESP_ERR_INVALID_ARG;
    *number = varredura ? (uint16_t)varredura->size() : 0;
    return ESP_OK;
}
extern "C" esp_err_t esp_wifi_scan_get_ap_records(uint16_t *number, wifi_ap_record_t *ap_records)
{
    if (!number || !ap_records) return ESP_ERR_INVALID_ARG;
    uint16_t n = varredura ? (uint16_t)varredura->size() : 0;
    if (*number > n) *number = n;
    for (uint16_t i = 0; i < *number; i++) ap_records[i] = (*varredura)[i];
    if (varredura) varredura->clear();
    return ESP_OK;
}
extern "C" esp_err_t esp_wifi_scan_get_ap_record(wifi_ap_record_t *ap_record)
{
    if (!ap_record) return ESP_ERR_INVALID_ARG;
    if (!varredura || varredura->empty()) return ESP_FAIL;
    *ap_record = varredura->front();
    varredura->erase(varredura->begin());
    return ESP_OK;
}
extern "C" esp_err_t esp_wifi_clear_ap_list(void)
{
    if (varredura) varredura->clear();
    return ESP_OK;
}
extern "C" esp_err_t esp_wifi_sta_get_ap_info(wifi_ap_record_t *ap_info)
{
    if (!ap_info) return ESP_ERR_INVALID_ARG;
    if (!WiFi.isConnected()) return ESP_ERR_WIFI_NOT_CONNECT;
    *ap_info = {};
    snprintf((char *)ap_info->ssid, sizeof(ap_info->ssid), "%s", WiFi.SSID().c_str());
    WiFi.BSSID(ap_info->bssid);
    ap_info->primary = (uint8_t)WiFi.channel();
    ap_info->rssi = WiFi.RSSI();
    ap_info->authmode = configSta.sta.password[0] ? WIFI_AUTH_WPA2_PSK : WIFI_AUTH_OPEN;
    ap_info->phy_11b = ap_info->phy_11g = ap_info->phy_11n = 1;
    ap_info->country = pais;
    return ESP_OK;
}

extern "C" esp_err_t esp_wifi_set_ps(wifi_ps_type_t type)
{
    sono = type;
    return ESP_OK;
}
extern "C" esp_err_t esp_wifi_get_ps(wifi_ps_type_t *type)
{
    if (!type) return ESP_ERR_INVALID_ARG;
    *type = sono;
    return ESP_OK;
}
extern "C" esp_err_t esp_wifi_set_protocol(wifi_interface_t ifx, uint8_t protocol_bitmap)
{
    if (ifx > WIFI_IF_AP) return ESP_ERR_INVALID_ARG;
    protocolo[ifx] = protocol_bitmap;
    return ESP_OK;
}
extern "C" esp_err_t esp_wifi_get_protocol(wifi_interface_t ifx, uint8_t *protocol_bitmap)
{
    if (ifx > WIFI_IF_AP || !protocol_bitmap) return ESP_ERR_INVALID_ARG;
    *protocol_bitmap = protocolo[ifx];
    return ESP_OK;
}
extern "C" esp_err_t esp_wifi_set_bandwidth(wifi_interface_t ifx, wifi_bandwidth_t bw)
{
    if (ifx > WIFI_IF_AP) return ESP_ERR_INVALID_ARG;
    banda[ifx] = bw;
    return ESP_OK;
}
extern "C" esp_err_t esp_wifi_get_bandwidth(wifi_interface_t ifx, wifi_bandwidth_t *bw)
{
    if (ifx > WIFI_IF_AP || !bw) return ESP_ERR_INVALID_ARG;
    *bw = banda[ifx];
    return ESP_OK;
}
extern "C" esp_err_t esp_wifi_set_channel(uint8_t primary, wifi_second_chan_t second)
{
    (void)second;
    return primary >= 1 && primary <= 14 ? ESP_OK : ESP_ERR_INVALID_ARG;
}
extern "C" esp_err_t esp_wifi_get_channel(uint8_t *primary, wifi_second_chan_t *second)
{
    if (!primary || !second) return ESP_ERR_INVALID_ARG;
    *primary = (uint8_t)(WiFi.channel() ? WiFi.channel() : 1);
    *second = WIFI_SECOND_CHAN_NONE;
    return ESP_OK;
}
extern "C" esp_err_t esp_wifi_set_country(const wifi_country_t *country)
{
    if (!country) return ESP_ERR_INVALID_ARG;
    pais = *country;
    return ESP_OK;
}
extern "C" esp_err_t esp_wifi_get_country(wifi_country_t *country)
{
    if (!country) return ESP_ERR_INVALID_ARG;
    *country = pais;
    return ESP_OK;
}
extern "C" esp_err_t esp_wifi_set_mac(wifi_interface_t ifx, const uint8_t mac[6])
{
    (void)ifx;
    (void)mac;
    return ESP_OK;
}
extern "C" esp_err_t esp_wifi_get_mac(wifi_interface_t ifx, uint8_t mac[6])
{
    if (!mac) return ESP_ERR_INVALID_ARG;
    if (ifx == WIFI_IF_AP) WiFi.softAPmacAddress(mac);
    else WiFi.macAddress(mac);
    return ESP_OK;
}
extern "C" esp_err_t esp_wifi_set_promiscuous_rx_cb(wifi_promiscuous_cb_t cb)
{
    (void)cb;
    return ESP_OK;
}
extern "C" esp_err_t esp_wifi_set_promiscuous(bool en)
{
    promiscuo = en;
    return ESP_OK;
}
extern "C" esp_err_t esp_wifi_get_promiscuous(bool *en)
{
    if (!en) return ESP_ERR_INVALID_ARG;
    *en = promiscuo;
    return ESP_OK;
}
extern "C" esp_err_t esp_wifi_set_promiscuous_filter(const wifi_promiscuous_filter_t *filter)
{
    (void)filter;
    return ESP_OK;
}
extern "C" esp_err_t esp_wifi_ap_get_sta_list(wifi_sta_list_t *sta)
{
    if (!sta) return ESP_ERR_INVALID_ARG;
    *sta = {};
    sta->num = WiFi.softAPgetStationNum();
    for (int i = 0; i < sta->num && i < ESP_WIFI_MAX_CONN_NUM; i++) {
        sta->sta[i].mac[0] = 0x24;
        sta->sta[i].mac[5] = (uint8_t)(i + 1);
        sta->sta[i].rssi = -45;
    }
    return ESP_OK;
}
extern "C" esp_err_t esp_wifi_ap_get_sta_aid(const uint8_t mac[6], uint16_t *aid)
{
    if (!mac || !aid) return ESP_ERR_INVALID_ARG;
    *aid = mac[5];
    return ESP_OK;
}
extern "C" esp_err_t esp_wifi_set_storage(wifi_storage_t storage)
{
    (void)storage;
    return ESP_OK;
}
extern "C" esp_err_t esp_wifi_set_max_tx_power(int8_t power)
{
    if (power < 8 || power > 84) return ESP_ERR_INVALID_ARG;
    potencia = power;
    return ESP_OK;
}
extern "C" esp_err_t esp_wifi_get_max_tx_power(int8_t *power)
{
    if (!power) return ESP_ERR_INVALID_ARG;
    *power = potencia;
    return ESP_OK;
}
extern "C" esp_err_t esp_wifi_set_event_mask(uint32_t mask)
{
    (void)mask;
    return ESP_OK;
}
extern "C" esp_err_t esp_wifi_set_inactive_time(wifi_interface_t ifx, uint16_t sec)
{
    (void)ifx;
    return WiFi.setInactiveTime(sec) ? ESP_OK : ESP_FAIL;
}
extern "C" esp_err_t esp_wifi_sta_get_rssi(int *rssi)
{
    if (!rssi) return ESP_ERR_INVALID_ARG;
    *rssi = WiFi.RSSI();
    return ESP_OK;
}

/* =====================================================================
 * esp_netif */
extern "C" esp_err_t esp_netif_init(void)
{
    netifIniciado = true;
    return ESP_OK;
}
extern "C" esp_err_t esp_netif_deinit(void) { return ESP_ERR_NOT_SUPPORTED; }
extern "C" esp_netif_t *esp_netif_create_default_wifi_sta(void) { return &interfaceSta; }
extern "C" esp_netif_t *esp_netif_create_default_wifi_ap(void) { return &interfaceAp; }
extern "C" void esp_netif_destroy_default_wifi(void *esp_netif) { (void)esp_netif; }
extern "C" void esp_netif_destroy(esp_netif_t *esp_netif) { (void)esp_netif; }
extern "C" esp_netif_t *esp_netif_get_handle_from_ifkey(const char *if_key)
{
    if (!if_key) return nullptr;
    if (strcmp(if_key, interfaceSta.chave) == 0) return &interfaceSta;
    if (strcmp(if_key, interfaceAp.chave) == 0) return &interfaceAp;
    return nullptr;
}
extern "C" const char *esp_netif_get_ifkey(esp_netif_t *esp_netif) { return esp_netif ? esp_netif->chave : nullptr; }
extern "C" const char *esp_netif_get_desc(esp_netif_t *esp_netif) { return esp_netif ? esp_netif->desc : nullptr; }

extern "C" esp_err_t esp_netif_get_ip_info(esp_netif_t *esp_netif, esp_netif_ip_info_t *ip_info)
{
    if (!esp_netif || !ip_info) return ESP_ERR_INVALID_ARG;
    if (esp_netif->ap) {
        ipDe(WiFi.softAPIP(), ip_info->ip);
        ipDe(WiFi.softAPSubnetMask(), ip_info->netmask);
        ipDe(WiFi.softAPIP(), ip_info->gw);
    } else if (WiFi.isConnected()) {
        ipDe(WiFi.localIP(), ip_info->ip);
        ipDe(WiFi.subnetMask(), ip_info->netmask);
        ipDe(WiFi.gatewayIP(), ip_info->gw);
    } else {
        *ip_info = {};
    }
    return ESP_OK;
}
extern "C" esp_err_t esp_netif_set_ip_info(esp_netif_t *esp_netif, const esp_netif_ip_info_t *ip_info)
{
    if (!esp_netif || !ip_info) return ESP_ERR_INVALID_ARG;
    if (esp_netif->ap) return WiFi.softAPConfig(IPAddress(ip_info->ip.addr), IPAddress(ip_info->gw.addr), IPAddress(ip_info->netmask.addr)) ? ESP_OK : ESP_FAIL;
    return WiFi.config(IPAddress(ip_info->ip.addr), IPAddress(ip_info->gw.addr), IPAddress(ip_info->netmask.addr)) ? ESP_OK : ESP_FAIL;
}
extern "C" esp_err_t esp_netif_get_mac(esp_netif_t *esp_netif, uint8_t mac[])
{
    if (!esp_netif || !mac) return ESP_ERR_INVALID_ARG;
    return esp_wifi_get_mac(esp_netif->ap ? WIFI_IF_AP : WIFI_IF_STA, mac);
}
extern "C" esp_err_t esp_netif_set_mac(esp_netif_t *esp_netif, uint8_t mac[])
{
    (void)esp_netif;
    (void)mac;
    return ESP_OK;
}
extern "C" esp_err_t esp_netif_set_hostname(esp_netif_t *esp_netif, const char *hostname)
{
    if (!esp_netif || !hostname) return ESP_ERR_INVALID_ARG;
    snprintf(esp_netif->nome, sizeof(esp_netif->nome), "%s", hostname);
    return WiFi.setHostname(hostname) ? ESP_OK : ESP_FAIL;
}
extern "C" esp_err_t esp_netif_get_hostname(esp_netif_t *esp_netif, const char **hostname)
{
    if (!esp_netif || !hostname) return ESP_ERR_INVALID_ARG;
    *hostname = WiFi.getHostname();
    return ESP_OK;
}
extern "C" esp_err_t esp_netif_dhcpc_start(esp_netif_t *esp_netif)
{
    if (!esp_netif) return ESP_ERR_INVALID_ARG;
    esp_netif->dhcp = true;
    return ESP_OK;
}
extern "C" esp_err_t esp_netif_dhcpc_stop(esp_netif_t *esp_netif)
{
    if (!esp_netif) return ESP_ERR_INVALID_ARG;
    esp_netif->dhcp = false;
    return ESP_OK;
}
extern "C" esp_err_t esp_netif_dhcpc_get_status(esp_netif_t *esp_netif, esp_netif_dhcp_status_t *status)
{
    if (!esp_netif || !status) return ESP_ERR_INVALID_ARG;
    *status = esp_netif->dhcp ? ESP_NETIF_DHCP_STARTED : ESP_NETIF_DHCP_STOPPED;
    return ESP_OK;
}
extern "C" esp_err_t esp_netif_dhcps_start(esp_netif_t *esp_netif) { return esp_netif ? ESP_OK : ESP_ERR_INVALID_ARG; }
extern "C" esp_err_t esp_netif_dhcps_stop(esp_netif_t *esp_netif) { return esp_netif ? ESP_OK : ESP_ERR_INVALID_ARG; }
extern "C" esp_err_t esp_netif_get_dns_info(esp_netif_t *esp_netif, esp_netif_dns_type_t type, esp_netif_dns_info_t *dns)
{
    (void)type;
    if (!esp_netif || !dns) return ESP_ERR_INVALID_ARG;
    *dns = {};
    dns->ip.type = ESP_IPADDR_TYPE_V4;
    dns->ip.u_addr.ip4.addr = (uint32_t)WiFi.dnsIP();
    return ESP_OK;
}
extern "C" esp_err_t esp_netif_set_dns_info(esp_netif_t *esp_netif, esp_netif_dns_type_t type, esp_netif_dns_info_t *dns)
{
    (void)type;
    if (!esp_netif || !dns) return ESP_ERR_INVALID_ARG;
    return WiFi.setDNS(IPAddress(dns->ip.u_addr.ip4.addr)) ? ESP_OK : ESP_FAIL;
}
extern "C" bool esp_netif_is_netif_up(esp_netif_t *esp_netif)
{
    return esp_netif && (esp_netif->ap ? WiFi.softAPIP() != IPAddress() : WiFi.isConnected());
}
extern "C" esp_err_t esp_netif_set_default_netif(esp_netif_t *esp_netif) { return esp_netif ? ESP_OK : ESP_ERR_INVALID_ARG; }
extern "C" esp_netif_t *esp_netif_get_default_netif(void) { return &interfaceSta; }
extern "C" esp_err_t esp_netif_napt_enable(esp_netif_t *esp_netif) { return esp_netif ? ESP_OK : ESP_ERR_INVALID_ARG; }
extern "C" esp_err_t esp_netif_napt_disable(esp_netif_t *esp_netif) { return esp_netif ? ESP_OK : ESP_ERR_INVALID_ARG; }

/* O texto do IP ("192.168.4.1") e o numero. */
extern "C" char *esp_ip4addr_ntoa(const esp_ip4_addr_t *addr, char *buf, int buflen)
{
    if (!addr || !buf || buflen < 16) return nullptr;
    const uint8_t *b = (const uint8_t *)&addr->addr;
    snprintf(buf, (size_t)buflen, "%u.%u.%u.%u", b[0], b[1], b[2], b[3]);
    return buf;
}
extern "C" uint32_t esp_ip4addr_aton(const char *addr)
{
    IPAddress ip;
    return addr && ip.fromString(addr) ? (uint32_t)ip : 0;
}
extern "C" esp_err_t esp_netif_str_to_ip4(const char *src, esp_ip4_addr_t *dst)
{
    if (!src || !dst) return ESP_ERR_INVALID_ARG;
    IPAddress ip;
    if (!ip.fromString(src)) return ESP_FAIL;
    dst->addr = (uint32_t)ip;
    return ESP_OK;
}

/* =====================================================================
 * o ESP-NOW que faltava: os pares guardados aqui, para listar e mudar. */
namespace {
std::vector<esp_now_peer_info_t> *pares;
std::vector<esp_now_peer_info_t> &listaDePares()
{
    if (!pares) pares = new std::vector<esp_now_peer_info_t>();
    return *pares;
}
size_t buscaDePar = 0;
}  // namespace

void cs_esp_now_par_adicionado(const esp_now_peer_info_t *peer)
{
    auto &l = listaDePares();
    for (auto &p : l)
        if (memcmp(p.peer_addr, peer->peer_addr, 6) == 0) {
            p = *peer;
            return;
        }
    l.push_back(*peer);
}
void cs_esp_now_par_removido(const uint8_t *mac)
{
    auto &l = listaDePares();
    for (size_t i = 0; i < l.size(); i++)
        if (memcmp(l[i].peer_addr, mac, 6) == 0) {
            l.erase(l.begin() + i);
            return;
        }
}

extern "C" esp_err_t esp_now_get_version(uint32_t *version)
{
    if (!version) return ESP_ERR_ESPNOW_ARG;
    *version = ESP_NOW_MAX_DATA_LEN_V2 > 250 ? 2 : 1;
    return ESP_OK;
}
extern "C" esp_err_t esp_now_mod_peer(const esp_now_peer_info_t *peer)
{
    if (!peer) return ESP_ERR_ESPNOW_ARG;
    for (auto &p : listaDePares())
        if (memcmp(p.peer_addr, peer->peer_addr, 6) == 0) {
            p = *peer;
            return ESP_OK;
        }
    return ESP_ERR_ESPNOW_NOT_FOUND;
}
extern "C" esp_err_t esp_now_get_peer(const uint8_t *peer_addr, esp_now_peer_info_t *peer)
{
    if (!peer_addr || !peer) return ESP_ERR_ESPNOW_ARG;
    for (auto &p : listaDePares())
        if (memcmp(p.peer_addr, peer_addr, 6) == 0) {
            *peer = p;
            return ESP_OK;
        }
    return ESP_ERR_ESPNOW_NOT_FOUND;
}
extern "C" esp_err_t esp_now_fetch_peer(bool from_head, esp_now_peer_info_t *peer)
{
    if (!peer) return ESP_ERR_ESPNOW_ARG;
    if (from_head) buscaDePar = 0;
    auto &l = listaDePares();
    if (buscaDePar >= l.size()) return ESP_ERR_ESPNOW_NOT_FOUND;
    *peer = l[buscaDePar++];
    return ESP_OK;
}
extern "C" esp_err_t esp_now_get_peer_num(esp_now_peer_num_t *num)
{
    if (!num) return ESP_ERR_ESPNOW_ARG;
    num->total_num = (int)listaDePares().size();
    num->encrypt_num = 0;
    for (auto &p : listaDePares())
        if (p.encrypt) num->encrypt_num++;
    return ESP_OK;
}
extern "C" esp_err_t esp_now_set_peer_rate_config(const uint8_t *peer_addr, esp_now_rate_config_t *config)
{
    (void)config;
    return peer_addr ? ESP_OK : ESP_ERR_ESPNOW_ARG;
}
extern "C" esp_err_t esp_wifi_config_espnow_rate(wifi_interface_t ifx, wifi_phy_rate_t rate)
{
    (void)ifx;
    (void)rate;
    return ESP_OK;
}
extern "C" esp_err_t esp_now_set_wake_window(uint16_t window)
{
    (void)window;
    return ESP_OK;
}

#endif
#endif
