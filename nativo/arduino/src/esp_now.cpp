/* esp_now.cpp -- o ESP-NOW pelo simulador (ver esp_now.h). O simulador manda
 * o MAC em texto ("AA:BB:CC:DD:EE:FF"); os adaptadores daqui o devolvem em
 * bytes, na estrutura que o callback do sketch espera. */
#include "Arduino.h"
#if defined(ESP32)
#include "soc/soc_caps.h"
#if SOC_WIFI_SUPPORTED
#include "esp_now.h"
#include "esp_wifi.h"
#include "WiFi.h"
#include "cs_objeto.h"

static esp_now_recv_cb_t recebeNovo;
static esp_now_recv_cb_v2_t recebeVelho;
static esp_now_send_cb_v2_t enviouVelho;
static esp_now_send_cb_t enviouNovo;

static void mac(const char *texto, uint8_t *bytes)
{
    unsigned v[6] = {0};
    if (texto) sscanf(texto, "%x:%x:%x:%x:%x:%x", &v[0], &v[1], &v[2], &v[3], &v[4], &v[5]);
    for (int i = 0; i < 6; i++) bytes[i] = (uint8_t)v[i];
}

static void aoReceber(const char *origem, uint8_t *dados, int tamanho)
{
    uint8_t de[6], para[6];
    mac(origem, de);
    WiFi.macAddress(para);
    wifi_pkt_rx_ctrl_t radio = {};
    radio.rssi = -40;
    radio.channel = 1;
    if (recebeNovo) {
        esp_now_recv_info_t info = {de, para, &radio};
        recebeNovo(&info, dados, tamanho);
    } else if (recebeVelho) {
        recebeVelho(de, dados, tamanho);
    }
}

static void aoEnviar(const char *destino, int estado)
{
    uint8_t para[6], de[6];
    mac(destino, para);
    WiFi.macAddress(de);
    if (enviouVelho) enviouVelho(para, (esp_now_send_status_t)estado);
    else if (enviouNovo) {
        wifi_tx_info_t info = {};
        info.des_addr = para;
        info.src_addr = de;
        info.ifidx = WIFI_IF_STA;
        info.tx_status = estado == ESP_NOW_SEND_SUCCESS ? WIFI_SEND_SUCCESS : WIFI_SEND_FAIL;
        enviouNovo(&info, (esp_now_send_status_t)estado);
    }
}

esp_err_t esp_now_init(void) { return (esp_err_t)cs_obj_i(nullptr, "esp_now_init"); }
esp_err_t esp_now_deinit(void) { return (esp_err_t)cs_obj_i(nullptr, "esp_now_deinit"); }

/* Os pares tambem ficam guardados aqui (cs_esp_wifi.cpp): o get_peer, o
 * fetch_peer e o get_peer_num os leem. */
void cs_esp_now_par_adicionado(const esp_now_peer_info_t *peer);
void cs_esp_now_par_removido(const uint8_t *mac);

esp_err_t esp_now_add_peer(const esp_now_peer_info_t *peer)
{
    if (!peer) return ESP_ERR_ESPNOW_ARG;
    esp_err_t r = (esp_err_t)cs_obj_i(nullptr, "esp_now_add_peer", cs_vetor(peer->peer_addr, 6));
    if (r == ESP_OK) cs_esp_now_par_adicionado(peer);
    return r;
}

esp_err_t esp_now_del_peer(const uint8_t *peer_addr)
{
    if (!peer_addr) return ESP_ERR_ESPNOW_ARG;
    esp_err_t r = (esp_err_t)cs_obj_i(nullptr, "esp_now_del_peer", cs_vetor(peer_addr, 6));
    if (r == ESP_OK) cs_esp_now_par_removido(peer_addr);
    return r;
}
bool esp_now_is_peer_exist(const uint8_t *peer_addr) { return cs_obj_i(nullptr, "esp_now_is_peer_exist", cs_vetor(peer_addr, 6)) != 0; }

esp_err_t esp_now_send(const uint8_t *peer_addr, const uint8_t *data, size_t len)
{
    if (!peer_addr) return (esp_err_t)cs_obj_i(nullptr, "esp_now_send", 0, cs_vetor(data, (int)len), (int)len);
    return (esp_err_t)cs_obj_i(nullptr, "esp_now_send", cs_vetor(peer_addr, 6), cs_vetor(data, (int)len), (int)len);
}

esp_err_t esp_now_set_pmk(const uint8_t *pmk) { (void)pmk; return ESP_OK; }

esp_err_t esp_now_register_recv_cb(esp_now_recv_cb_t cb)
{
    recebeNovo = cb;
    recebeVelho = nullptr;
    return (esp_err_t)cs_obj_i(nullptr, "esp_now_register_recv_cb", &aoReceber);
}

esp_err_t esp_now_register_recv_cb(esp_now_recv_cb_v2_t cb)
{
    recebeVelho = cb;
    recebeNovo = nullptr;
    return (esp_err_t)cs_obj_i(nullptr, "esp_now_register_recv_cb", &aoReceber);
}

esp_err_t esp_now_register_send_cb(esp_now_send_cb_v2_t cb)
{
    enviouVelho = cb;
    enviouNovo = nullptr;
    return (esp_err_t)cs_obj_i(nullptr, "esp_now_register_send_cb", &aoEnviar);
}

esp_err_t esp_now_register_send_cb(esp_now_send_cb_t cb)
{
    enviouNovo = cb;
    enviouVelho = nullptr;
    return (esp_err_t)cs_obj_i(nullptr, "esp_now_register_send_cb", &aoEnviar);
}

esp_err_t esp_now_unregister_recv_cb(void) { return (esp_err_t)cs_obj_i(nullptr, "esp_now_unregister_recv_cb"); }
esp_err_t esp_now_unregister_send_cb(void) { return (esp_err_t)cs_obj_i(nullptr, "esp_now_unregister_send_cb"); }

#endif
#endif
