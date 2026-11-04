/* cs_wifi.cpp -- o WiFi e a rede do core 3.x da ESP32 (WiFi.h, Network.h)
 * sobre a rede da bancada do simulador.
 *
 * O que muda o estado (begin, softAP, mode, disconnect) vai ao simulador,
 * como no sketch interpretado. Os eventos vem como na placa: a tarefa
 * "arduino_events" do NetworkEvents de verdade entrega cada um as funcoes do
 * sketch, e uma tarefa daqui acompanha a rede (o STA que conectou, pegou IP
 * ou caiu; as placas que entraram no AP) e posta o evento com a informacao
 * que o core poe nele. */
#include "Arduino.h"
#if defined(ESP32)
#include "soc/soc_caps.h"
#if SOC_WIFI_SUPPORTED
#include "WiFi.h"
#include "cs_interno.h"

WiFiClass WiFi;
NetworkManager Network;

/* =====================================================================
 * os eventos */
namespace {

struct EstadoDaRede {
    bool comecou = false;
    bool staLigado = false;
    bool conectado = false;
    bool apLigado = false;
    uint8_t estacoes = 0;
    bool desligouDeProposito = false;
    bool tentando = false;
    String ssid, senha, ssidDoAp;
    uint8_t ultimaFalha = 0;
};
EstadoDaRede rede;

/* O mesmo evento no laco do ESP-IDF (o WIFI_EVENT e o IP_EVENT que um
 * programa do IDF registra): so se o laco padrao existir. */
void postarNoIdf(arduino_event_id_t id, const arduino_event_info_t *info)
{
    arduino_event_info_t vazio = {};
    if (!info) info = &vazio;
    switch (id) {
    case ARDUINO_EVENT_WIFI_STA_START: esp_event_post(WIFI_EVENT, WIFI_EVENT_STA_START, nullptr, 0, 0); break;
    case ARDUINO_EVENT_WIFI_STA_STOP: esp_event_post(WIFI_EVENT, WIFI_EVENT_STA_STOP, nullptr, 0, 0); break;
    case ARDUINO_EVENT_WIFI_STA_CONNECTED:
        esp_event_post(WIFI_EVENT, WIFI_EVENT_STA_CONNECTED, &info->wifi_sta_connected, sizeof(info->wifi_sta_connected), 0);
        break;
    case ARDUINO_EVENT_WIFI_STA_DISCONNECTED:
        esp_event_post(WIFI_EVENT, WIFI_EVENT_STA_DISCONNECTED, &info->wifi_sta_disconnected, sizeof(info->wifi_sta_disconnected), 0);
        break;
    case ARDUINO_EVENT_WIFI_STA_GOT_IP:
        esp_event_post(IP_EVENT, IP_EVENT_STA_GOT_IP, &info->got_ip, sizeof(info->got_ip), 0);
        break;
    case ARDUINO_EVENT_WIFI_STA_LOST_IP: esp_event_post(IP_EVENT, IP_EVENT_STA_LOST_IP, nullptr, 0, 0); break;
    case ARDUINO_EVENT_WIFI_AP_START: esp_event_post(WIFI_EVENT, WIFI_EVENT_AP_START, nullptr, 0, 0); break;
    case ARDUINO_EVENT_WIFI_AP_STOP: esp_event_post(WIFI_EVENT, WIFI_EVENT_AP_STOP, nullptr, 0, 0); break;
    case ARDUINO_EVENT_WIFI_AP_STACONNECTED:
        esp_event_post(WIFI_EVENT, WIFI_EVENT_AP_STACONNECTED, &info->wifi_ap_staconnected, sizeof(info->wifi_ap_staconnected), 0);
        break;
    case ARDUINO_EVENT_WIFI_AP_STADISCONNECTED:
        esp_event_post(WIFI_EVENT, WIFI_EVENT_AP_STADISCONNECTED, &info->wifi_ap_stadisconnected, sizeof(info->wifi_ap_stadisconnected), 0);
        break;
    case ARDUINO_EVENT_WIFI_AP_STAIPASSIGNED:
        esp_event_post(IP_EVENT, IP_EVENT_AP_STAIPASSIGNED, &info->wifi_ap_staipassigned, sizeof(info->wifi_ap_staipassigned), 0);
        break;
    case ARDUINO_EVENT_WIFI_SCAN_DONE:
        esp_event_post(WIFI_EVENT, WIFI_EVENT_SCAN_DONE, &info->wifi_scan_done, sizeof(info->wifi_scan_done), 0);
        break;
    default: break;
    }
}

void postar(arduino_event_id_t id, const arduino_event_info_t *info = nullptr)
{
    arduino_event_t e = {};
    e.event_id = id;
    if (info) e.event_info = *info;
    Network.postEvent(&e);
    postarNoIdf(id, info);
}

void ipDe(IPAddress ip, esp_ip4_addr_t &saida) { saida.addr = (uint32_t)ip; }

void eventosDoSta()
{
    wl_status_t st = WiFi.status();
    if (st == WL_CONNECTED && !rede.conectado) {
        rede.conectado = true;
        rede.ultimaFalha = 0;
        arduino_event_info_t i = {};
        String ssid = WiFi.SSID();
        snprintf((char *)i.wifi_sta_connected.ssid, sizeof(i.wifi_sta_connected.ssid), "%s", ssid.c_str());
        i.wifi_sta_connected.ssid_len = (uint8_t)ssid.length();
        WiFi.BSSID(i.wifi_sta_connected.bssid);
        i.wifi_sta_connected.channel = (uint8_t)WiFi.channel();
        i.wifi_sta_connected.authmode = rede.senha.length() ? WIFI_AUTH_WPA2_PSK : WIFI_AUTH_OPEN;
        postar(ARDUINO_EVENT_WIFI_STA_CONNECTED, &i);
        Network.setStatusBits(BIT0 | BIT1);
        arduino_event_info_t g = {};
        ipDe(WiFi.localIP(), g.got_ip.ip_info.ip);
        ipDe(WiFi.subnetMask(), g.got_ip.ip_info.netmask);
        ipDe(WiFi.gatewayIP(), g.got_ip.ip_info.gw);
        g.got_ip.ip_changed = true;
        postar(ARDUINO_EVENT_WIFI_STA_GOT_IP, &g);
        return;
    }
    if (st != WL_CONNECTED && rede.conectado) {
        rede.conectado = false;
        arduino_event_info_t i = {};
        snprintf((char *)i.wifi_sta_disconnected.ssid, sizeof(i.wifi_sta_disconnected.ssid), "%s", rede.ssid.c_str());
        i.wifi_sta_disconnected.ssid_len = (uint8_t)rede.ssid.length();
        i.wifi_sta_disconnected.reason = rede.desligouDeProposito ? WIFI_REASON_ASSOC_LEAVE : WIFI_REASON_BEACON_TIMEOUT;
        i.wifi_sta_disconnected.rssi = -90;
        postar(ARDUINO_EVENT_WIFI_STA_DISCONNECTED, &i);
        postar(ARDUINO_EVENT_WIFI_STA_LOST_IP);
        rede.desligouDeProposito = false;
        return;
    }
    /* A tentativa que nao conecta: o core avisa a cada tentativa, com o motivo. */
    if (!rede.conectado && rede.tentando && (st == WL_NO_SSID_AVAIL || st == WL_CONNECT_FAILED)) {
        uint8_t motivo = st == WL_NO_SSID_AVAIL ? WIFI_REASON_NO_AP_FOUND : WIFI_REASON_AUTH_FAIL;
        if (motivo != rede.ultimaFalha) {
            rede.ultimaFalha = motivo;
            arduino_event_info_t i = {};
            snprintf((char *)i.wifi_sta_disconnected.ssid, sizeof(i.wifi_sta_disconnected.ssid), "%s", rede.ssid.c_str());
            i.wifi_sta_disconnected.ssid_len = (uint8_t)rede.ssid.length();
            i.wifi_sta_disconnected.reason = motivo;
            postar(ARDUINO_EVENT_WIFI_STA_DISCONNECTED, &i);
        }
    }
}

void eventosDoAp()
{
    if (!rede.apLigado) return;
    uint8_t n = (uint8_t)cs_obj_i(nullptr, "__wifi_estacoes");
    while (n > rede.estacoes) {
        rede.estacoes++;
        arduino_event_info_t i = {};
        i.wifi_ap_staconnected.aid = rede.estacoes;
        i.wifi_ap_staconnected.mac[0] = 0x24;
        i.wifi_ap_staconnected.mac[5] = rede.estacoes;
        postar(ARDUINO_EVENT_WIFI_AP_STACONNECTED, &i);
        arduino_event_info_t ip = {};
        IPAddress dado = WiFi.softAPIP();
        dado[3] = (uint8_t)(dado[3] + rede.estacoes);
        ipDe(dado, ip.wifi_ap_staipassigned.ip);
        postar(ARDUINO_EVENT_WIFI_AP_STAIPASSIGNED, &ip);
    }
    while (n < rede.estacoes) {
        arduino_event_info_t i = {};
        i.wifi_ap_stadisconnected.aid = rede.estacoes;
        i.wifi_ap_stadisconnected.mac[0] = 0x24;
        i.wifi_ap_stadisconnected.mac[5] = rede.estacoes;
        rede.estacoes--;
        postar(ARDUINO_EVENT_WIFI_AP_STADISCONNECTED, &i);
    }
}

void observar(void *)
{
    for (;;) {
        if (rede.staLigado) eventosDoSta();
        eventosDoAp();
        vTaskDelay(50);
    }
}

/* A primeira vez que o sketch mexe no WiFi: o driver sobe (WIFI_READY) e a
 * tarefa que acompanha a rede comeca. */
void comecar()
{
    if (rede.comecou) return;
    rede.comecou = true;
    Network.begin();
    xTaskCreate(observar, "wifi_eventos", 4096, nullptr, 2, nullptr);
    postar(ARDUINO_EVENT_WIFI_READY);
}

void ligarSta()
{
    comecar();
    if (rede.staLigado) return;
    rede.staLigado = true;
    postar(ARDUINO_EVENT_WIFI_STA_START);
}

void ligarAp(const char *ssid)
{
    comecar();
    rede.ssidDoAp = ssid ? ssid : "";
    if (rede.apLigado) return;
    rede.apLigado = true;
    rede.estacoes = 0;
    postar(ARDUINO_EVENT_WIFI_AP_START);
}

}  // namespace

/* Para o esp_wifi do IDF (cs_esp_wifi.cpp): o esp_wifi_start sobe a mesma
 * interface que o WiFi.begin. */
void cs_wifi_ligar_sta() { ligarSta(); }
void cs_wifi_ligar_ap(const char *ssid) { ligarAp(ssid); }
void cs_wifi_desligar_sta()
{
    if (!rede.staLigado) return;
    rede.staLigado = false;
    postar(ARDUINO_EVENT_WIFI_STA_STOP);
}
void cs_wifi_varredura_terminou(int16_t n)
{
    arduino_event_info_t i = {};
    i.wifi_scan_done.number = (uint8_t)(n > 0 ? n : 0);
    postar(ARDUINO_EVENT_WIFI_SCAN_DONE, &i);
}

/* =====================================================================
 * o NetworkManager */
bool NetworkManager::begin()
{
    static bool feito = false;
    if (feito) return true;
    feito = true;
    return initNetworkEvents();
}

int NetworkManager::hostByName(const char *aHostname, IPAddress &aResult)
{
    String ip = cs_obj_t(nullptr, "__dns", aHostname ? aHostname : "");
    if (!ip.length()) return 0;
    return aResult.fromString(ip) ? 1 : 0;
}

uint8_t *NetworkManager::macAddress(uint8_t *mac) { return WiFi.macAddress(mac); }
String NetworkManager::macAddress() { return WiFi.macAddress(); }
bool NetworkManager::setDefaultInterface(NetworkInterface &ifc)
{
    (void)ifc;
    return true;
}
NetworkInterface *NetworkManager::getDefaultInterface() { return &WiFi.STA; }
bool NetworkManager::isOnline() { return WiFi.isConnected(); }
size_t NetworkManager::printTo(Print &out) const
{
    size_t n = out.printf("Network: hostname %s\n", getHostname());
    n += WiFi.STA.printTo(out);
    return n;
}
const char *NetworkManager::getHostname() { return WiFi.getHostname(); }
bool NetworkManager::setHostname(const char *hostname) { return WiFi.setHostname(hostname); }

/* =====================================================================
 * as interfaces (WiFi.STA, WiFi.AP) */
IPAddress NetworkInterface::calculateNetworkID(IPAddress ip, IPAddress subnet) { return IPAddress((uint32_t)ip & (uint32_t)subnet); }
IPAddress NetworkInterface::calculateBroadcast(IPAddress ip, IPAddress subnet)
{
    return IPAddress(((uint32_t)ip & (uint32_t)subnet) | ~(uint32_t)subnet);
}
uint8_t NetworkInterface::calculateSubnetCIDR(IPAddress subnetMask)
{
    uint8_t n = 0;
    for (uint32_t m = (uint32_t)subnetMask; m; m >>= 1) n += m & 1;
    return n;
}

static bool ehAp(Network_Interface_ID id) { return id == ESP_NETIF_ID_AP; }
static bool ehEth(Network_Interface_ID id) { return id >= ESP_NETIF_ID_ETH0 && id <= ESP_NETIF_ID_ETH2; }
static bool ehPpp(Network_Interface_ID id) { return id == ESP_NETIF_ID_PPP; }
bool cs_eth_iniciada();
bool cs_eth_conectada();
IPAddress cs_eth_ip();
IPAddress cs_eth_roteador();
String cs_eth_mac();
bool cs_eth_config(IPAddress ip);

bool NetworkInterface::config(IPAddress local_ip, IPAddress gateway, IPAddress subnet, IPAddress dns1, IPAddress dns2, IPAddress dns3)
{
    (void)dns3;
    if (ehEth(_interface_id)) return cs_eth_config(local_ip);
    if (ehAp(_interface_id)) return WiFi.softAPConfig(local_ip, gateway, subnet);
    return WiFi.config(local_ip, gateway, subnet, dns1, dns2);
}
bool NetworkInterface::dnsIP(uint8_t dns_no, IPAddress ip)
{
    (void)dns_no;
    return WiFi.setDNS(ip);
}
const char *NetworkInterface::getHostname() const { return WiFi.getHostname(); }
bool NetworkInterface::setHostname(const char *hostname) const { return WiFi.setHostname(hostname); }
bool NetworkInterface::started() const { return ehPpp(_interface_id) ? false : ehEth(_interface_id) ? cs_eth_iniciada() : ehAp(_interface_id) ? rede.apLigado : rede.staLigado; }
bool NetworkInterface::connected() const { return ehPpp(_interface_id) ? false : ehEth(_interface_id) ? cs_eth_conectada() : ehAp(_interface_id) ? rede.apLigado : WiFi.isConnected(); }
bool NetworkInterface::hasIP() const { return connected(); }
const char *NetworkInterface::ifkey() const { return ehEth(_interface_id) ? "ETH_DEF" : ehAp(_interface_id) ? "WIFI_AP_DEF" : "WIFI_STA_DEF"; }
const char *NetworkInterface::desc() const { return ehEth(_interface_id) ? "eth" : ehAp(_interface_id) ? "ap" : "sta"; }
String NetworkInterface::impl_name() const { return ehEth(_interface_id) ? "en" : ehAp(_interface_id) ? "ap" : "st"; }
uint8_t *NetworkInterface::macAddress(uint8_t *mac) const
{
    if (ehEth(_interface_id)) {
        unsigned b[6] = {};
        sscanf(cs_eth_mac().c_str(), "%x:%x:%x:%x:%x:%x", &b[0], &b[1], &b[2], &b[3], &b[4], &b[5]);
        for (int i = 0; i < 6; i++) mac[i] = (uint8_t)b[i];
        return mac;
    }
    return ehAp(_interface_id) ? WiFi.softAPmacAddress(mac) : WiFi.macAddress(mac);
}
String NetworkInterface::macAddress() const { return ehEth(_interface_id) ? cs_eth_mac() : ehAp(_interface_id) ? WiFi.softAPmacAddress() : WiFi.macAddress(); }
IPAddress NetworkInterface::localIP() const { return ehPpp(_interface_id) ? IPAddress() : ehEth(_interface_id) ? cs_eth_ip() : ehAp(_interface_id) ? WiFi.softAPIP() : WiFi.localIP(); }
IPAddress NetworkInterface::subnetMask() const
{
    if (ehEth(_interface_id)) return cs_eth_conectada() ? IPAddress(255, 255, 255, 0) : IPAddress();
    return ehAp(_interface_id) ? WiFi.softAPSubnetMask() : WiFi.subnetMask();
}
IPAddress NetworkInterface::gatewayIP() const { return ehEth(_interface_id) ? cs_eth_roteador() : ehAp(_interface_id) ? WiFi.softAPIP() : WiFi.gatewayIP(); }
IPAddress NetworkInterface::dnsIP(uint8_t dns_no) const { return ehEth(_interface_id) ? cs_eth_roteador() : ehAp(_interface_id) ? WiFi.softAPIP() : WiFi.dnsIP(dns_no); }
IPAddress NetworkInterface::broadcastIP() const { return calculateBroadcast(localIP(), subnetMask()); }
IPAddress NetworkInterface::networkID() const { return calculateNetworkID(localIP(), subnetMask()); }
uint8_t NetworkInterface::subnetCIDR() const { return calculateSubnetCIDR(subnetMask()); }
int NetworkInterface::getStatusBits() const { return Network.getStatusBits(); }
int NetworkInterface::waitStatusBits(int bits, uint32_t timeout_ms) const { return Network.waitStatusBits(bits, timeout_ms); }

size_t NetworkInterface::printTo(Print &out) const
{
    size_t n = out.printf("%s: <%s%s> ", desc(), started() ? "UP" : "DOWN", connected() ? ",CONNECTED" : "");
    n += out.printf("ether %s\n", macAddress().c_str());
    n += printDriverInfo(out);
    if (hasIP())
        n += out.printf("      inet %s netmask %s broadcast %s\n      gateway %s dns %s\n", localIP().toString().c_str(),
                        subnetMask().toString().c_str(), broadcastIP().toString().c_str(), gatewayIP().toString().c_str(),
                        dnsIP().toString().c_str());
    return n;
}

bool STAClass::begin(bool tryConnect)
{
    ligarSta();
    return tryConnect ? connect() : true;
}
bool STAClass::end() { return WiFi.disconnect(true); }
bool STAClass::connect() { return WiFi.begin() != WL_CONNECT_FAILED; }
bool STAClass::connect(const char *ssid, const char *passphrase, int32_t channel, const uint8_t *bssid, bool tryConnect)
{
    return WiFi.begin(ssid, passphrase, channel, bssid, tryConnect) != WL_CONNECT_FAILED;
}
bool STAClass::disconnect(bool eraseap, unsigned long timeout)
{
    (void)timeout;
    return WiFi.disconnect(false, eraseap);
}
bool STAClass::reconnect() { return WiFi.reconnect(); }
bool STAClass::erase() { return WiFi.eraseAP(); }
uint8_t STAClass::waitForConnectResult(unsigned long timeoutLength) { return WiFi.waitForConnectResult(timeoutLength); }
bool STAClass::setAutoReconnect(bool autoReconnect) { return WiFi.setAutoReconnect(autoReconnect); }
bool STAClass::getAutoReconnect() { return WiFi.getAutoReconnect(); }
wl_status_t STAClass::status() { return WiFi.status(); }
String STAClass::SSID() const { return WiFi.SSID(); }
String STAClass::psk() const { return WiFi.psk(); }
uint8_t *STAClass::BSSID(uint8_t *bssid) { return WiFi.BSSID(bssid); }
String STAClass::BSSIDstr() { return WiFi.BSSIDstr(); }
int8_t STAClass::RSSI() { return WiFi.RSSI(); }
size_t STAClass::printDriverInfo(Print &out) const
{
    return connected() ? out.printf("      ssid %s rssi %d channel %d\n", WiFi.SSID().c_str(), WiFi.RSSI(), (int)WiFi.channel()) : 0;
}

bool APClass::begin()
{
    ligarAp(rede.ssidDoAp.c_str());
    return true;
}
bool APClass::end() { return WiFi.softAPdisconnect(false); }
bool APClass::create(const char *ssid, const char *passphrase, int channel, int ssid_hidden, int max_connection, bool ftm_responder,
                     wifi_auth_mode_t auth_mode, wifi_cipher_type_t cipher)
{
    return WiFi.softAP(ssid, passphrase, channel, ssid_hidden, max_connection, ftm_responder, auth_mode, cipher);
}
bool APClass::clear() { return true; }
bool APClass::bandwidth(wifi_bandwidth_t bandwidth) { return bandwidth == WIFI_BW_HT20 || bandwidth == WIFI_BW_HT40; }
bool APClass::enableNAPT(bool enable)
{
    (void)enable;
    return true;
}
bool APClass::enableDhcpCaptivePortal() { return true; }
String APClass::SSID(void) const { return WiFi.softAPSSID(); }
uint8_t APClass::stationCount() { return WiFi.softAPgetStationNum(); }
size_t APClass::printDriverInfo(Print &out) const
{
    return rede.apLigado ? out.printf("      ssid %s stations %u\n", rede.ssidDoAp.c_str(), (unsigned)rede.estacoes) : 0;
}

/* =====================================================================
 * o WiFiClass */
wl_status_t WiFiClass::begin(const char *ssid, const char *passphrase, int32_t channel, const uint8_t *bssid, bool connect)
{
    (void)channel;
    (void)bssid;
    ligarSta();
    rede.ssid = ssid ? ssid : "";
    rede.senha = passphrase ? passphrase : "";
    rede.tentando = true;
    rede.ultimaFalha = 0;
    if (!connect) return WL_DISCONNECTED;
    return passphrase && *passphrase ? (wl_status_t)cs_obj_i("WiFi", "begin", ssid, passphrase) : (wl_status_t)cs_obj_i("WiFi", "begin", ssid);
}

bool WiFiClass::disconnect(bool wifioff, bool eraseap, unsigned long timeoutLength)
{
    (void)eraseap;
    (void)timeoutLength;
    rede.desligouDeProposito = true;
    rede.tentando = false;
    bool r = cs_obj_i("WiFi", "disconnect", wifioff) != 0;
    if (wifioff && rede.staLigado) {
        rede.staLigado = false;
        eventosDoSta();
        postar(ARDUINO_EVENT_WIFI_STA_STOP);
    }
    return r;
}

uint8_t WiFiClass::waitForConnectResult(unsigned long timeoutLength)
{
    unsigned long ate = millis() + timeoutLength;
    while (millis() < ate) {
        wl_status_t s = status();
        if (s == WL_CONNECTED || s == WL_CONNECT_FAILED || s == WL_NO_SSID_AVAIL) return s;
        delay(100);
    }
    return status();
}

bool WiFiClass::setDNS(IPAddress dns1, IPAddress dns2)
{
    (void)dns2;
    return cs_obj_i("WiFi", "config", localIP(), gatewayIP(), subnetMask(), dns1) != 0;
}

String WiFiClass::psk() const { return rede.senha; }

uint8_t *WiFiClass::BSSID(uint8_t *bssid)
{
    static uint8_t guardado[6];
    uint8_t *b = bssid ? bssid : guardado;
    unsigned v[6] = {0};
    sscanf(BSSIDstr().c_str(), "%x:%x:%x:%x:%x:%x", &v[0], &v[1], &v[2], &v[3], &v[4], &v[5]);
    for (int i = 0; i < 6; i++) b[i] = (uint8_t)v[i];
    return b;
}

/* O SmartConfig espera o celular mandar a rede pelo app ESPTouch: no
 * simulador, nenhum celular manda -- e o smartConfigDone() fica falso. */
bool WiFiClass::beginSmartConfig(smartconfig_type_t type, char *crypt_key)
{
    (void)type;
    (void)crypt_key;
    ligarSta();
    return true;
}

bool WiFiClass::softAP(const char *ssid, const char *passphrase, int channel, int ssid_hidden, int max_connection, bool ftm_responder,
                       wifi_auth_mode_t auth_mode, wifi_cipher_type_t cipher)
{
    (void)channel, (void)ssid_hidden, (void)max_connection, (void)ftm_responder, (void)auth_mode, (void)cipher;
    if (!ssid || !*ssid) {
        log_e("SSID missing!");
        return false;
    }
    if (passphrase && *passphrase && strlen(passphrase) < 8) {
        log_e("passphrase too short!");
        return false;
    }
    bool r = passphrase ? cs_obj_i("WiFi", "softAP", ssid, passphrase) != 0 : cs_obj_i("WiFi", "softAP", ssid) != 0;
    if (r) ligarAp(ssid);
    return r;
}

bool WiFiClass::softAPConfig(IPAddress local_ip, IPAddress gateway, IPAddress subnet, IPAddress dhcp_lease_start, IPAddress dns)
{
    (void)local_ip, (void)gateway, (void)subnet, (void)dhcp_lease_start, (void)dns;
    return true;
}

uint8_t WiFiClass::softAPgetStationNum() { return (uint8_t)cs_obj_i(nullptr, "__wifi_estacoes"); }
String WiFiClass::softAPSSID(void) const { return rede.ssidDoAp; }

int16_t WiFiClass::scanNetworks(bool async, bool show_hidden, bool passive, uint32_t max_ms_per_chan, uint8_t channel, const char *ssid,
                                const uint8_t *bssid)
{
    (void)show_hidden, (void)passive, (void)max_ms_per_chan, (void)channel, (void)ssid, (void)bssid;
    ligarSta();
    _varredura = WIFI_SCAN_RUNNING;
    int16_t n = (int16_t)cs_obj_i("WiFi", "scanNetworks");
    _varredura = n;
    if (async) {
        arduino_event_info_t i = {};
        i.wifi_scan_done.status = 0;
        i.wifi_scan_done.number = (uint8_t)(n > 0 ? n : 0);
        postar(ARDUINO_EVENT_WIFI_SCAN_DONE, &i);
        return WIFI_SCAN_RUNNING;
    }
    return n;
}

bool WiFiClass::getNetworkInfo(uint8_t networkItem, String &ssid, uint8_t &encryptionType, int32_t &rssi, uint8_t *&bssid, int32_t &ch)
{
    if (_varredura <= 0 || networkItem >= _varredura) return false;
    ssid = SSID(networkItem);
    encryptionType = (uint8_t)this->encryptionType(networkItem);
    rssi = RSSI(networkItem);
    static uint8_t b[6];
    bssid = BSSID(networkItem, b);
    ch = channel(networkItem);
    return true;
}

/* O BSSID de cada rede da varredura: o MAC do roteador, um por rede. */
uint8_t *WiFiClass::BSSID(uint8_t networkItem, uint8_t *bssid)
{
    static uint8_t guardado[6];
    uint8_t *b = bssid ? bssid : guardado;
    String nome = SSID(networkItem);
    uint32_t h = 2166136261u;
    for (unsigned i = 0; i < nome.length(); i++) h = (h ^ (uint8_t)nome[i]) * 16777619u;
    b[0] = 0x24;
    b[1] = 0x0A;
    b[2] = 0xC4;
    b[3] = (uint8_t)(h >> 16);
    b[4] = (uint8_t)(h >> 8);
    b[5] = (uint8_t)h;
    return b;
}
String WiFiClass::BSSIDstr(uint8_t networkItem)
{
    uint8_t b[6];
    BSSID(networkItem, b);
    char t[18];
    snprintf(t, sizeof(t), "%02X:%02X:%02X:%02X:%02X:%02X", b[0], b[1], b[2], b[3], b[4], b[5]);
    return String(t);
}
int32_t WiFiClass::channel(uint8_t networkItem)
{
    String nome = SSID(networkItem);
    uint32_t h = 0;
    for (unsigned i = 0; i < nome.length(); i++) h = h * 31 + (uint8_t)nome[i];
    return (int32_t)(h % 11) + 1;
}

wifi_event_id_t WiFiClass::onEvent(WiFiEventCb cbEvent, arduino_event_id_t event)
{
    comecar();
    return Network.onEvent(cbEvent, event);
}
wifi_event_id_t WiFiClass::onEvent(WiFiEventFuncCb cbEvent, arduino_event_id_t event)
{
    comecar();
    return Network.onEvent(cbEvent, event);
}
wifi_event_id_t WiFiClass::onEvent(WiFiEventSysCb cbEvent, arduino_event_id_t event)
{
    comecar();
    return Network.onEvent(cbEvent, event);
}
void WiFiClass::removeEvent(WiFiEventCb cbEvent, arduino_event_id_t event) { Network.removeEvent(cbEvent, event); }
void WiFiClass::removeEvent(WiFiEventSysCb cbEvent, arduino_event_id_t event) { Network.removeEvent(cbEvent, event); }
void WiFiClass::removeEvent(wifi_event_id_t id) { Network.removeEvent(id); }
int WiFiClass::getStatusBits() { return Network.getStatusBits(); }
int WiFiClass::waitStatusBits(int bits, uint32_t timeout_ms) { return Network.waitStatusBits(bits, timeout_ms); }
int WiFiClass::setStatusBits(int bits) { return Network.setStatusBits(bits); }
int WiFiClass::clearStatusBits(int bits) { return Network.clearStatusBits(bits); }

/* O canal do WiFi da bancada: o do roteador (o 1, se nao houver). */
int32_t WiFiClass::channel(void) { return isConnected() ? 1 : 0; }
int WiFiClass::setChannel(uint8_t primary, wifi_second_chan_t secondary)
{
    (void)secondary;
    return primary >= 1 && primary <= 14 ? ESP_OK : ESP_ERR_INVALID_ARG;
}

bool WiFiClass::mode(wifi_mode_t m)
{
    bool r = cs_obj_i("WiFi", "mode", (int)m) != 0;
    if (m == WIFI_MODE_STA || m == WIFI_MODE_APSTA) ligarSta();
    if (m == WIFI_MODE_NULL) {
        if (rede.staLigado) {
            rede.staLigado = false;
            postar(ARDUINO_EVENT_WIFI_STA_STOP);
        }
        if (rede.apLigado) {
            rede.apLigado = false;
            postar(ARDUINO_EVENT_WIFI_AP_STOP);
        }
        if (rede.comecou) postar(ARDUINO_EVENT_WIFI_OFF);
    }
    return r;
}
bool WiFiClass::enableSTA(bool enable)
{
    wifi_mode_t m = getMode();
    bool ap = m == WIFI_MODE_AP || m == WIFI_MODE_APSTA;
    return mode(enable ? (ap ? WIFI_MODE_APSTA : WIFI_MODE_STA) : (ap ? WIFI_MODE_AP : WIFI_MODE_NULL));
}
bool WiFiClass::enableAP(bool enable)
{
    wifi_mode_t m = getMode();
    bool sta = m == WIFI_MODE_STA || m == WIFI_MODE_APSTA;
    return mode(enable ? (sta ? WIFI_MODE_APSTA : WIFI_MODE_AP) : (sta ? WIFI_MODE_STA : WIFI_MODE_NULL));
}

const char *WiFiClass::disconnectReasonName(wifi_err_reason_t reason)
{
    switch (reason) {
    case WIFI_REASON_UNSPECIFIED: return "UNSPECIFIED";
    case WIFI_REASON_AUTH_EXPIRE: return "AUTH_EXPIRE";
    case WIFI_REASON_AUTH_LEAVE: return "AUTH_LEAVE";
    case WIFI_REASON_ASSOC_EXPIRE: return "ASSOC_EXPIRE";
    case WIFI_REASON_ASSOC_TOOMANY: return "ASSOC_TOOMANY";
    case WIFI_REASON_NOT_AUTHED: return "NOT_AUTHED";
    case WIFI_REASON_NOT_ASSOCED: return "NOT_ASSOCED";
    case WIFI_REASON_ASSOC_LEAVE: return "ASSOC_LEAVE";
    case WIFI_REASON_4WAY_HANDSHAKE_TIMEOUT: return "4WAY_HANDSHAKE_TIMEOUT";
    case WIFI_REASON_BEACON_TIMEOUT: return "BEACON_TIMEOUT";
    case WIFI_REASON_NO_AP_FOUND: return "NO_AP_FOUND";
    case WIFI_REASON_AUTH_FAIL: return "AUTH_FAIL";
    case WIFI_REASON_ASSOC_FAIL: return "ASSOC_FAIL";
    case WIFI_REASON_HANDSHAKE_TIMEOUT: return "HANDSHAKE_TIMEOUT";
    case WIFI_REASON_CONNECTION_FAIL: return "CONNECTION_FAIL";
    default: return "UNKNOWN";
    }
}

void WiFiClass::printDiag(Print &dest)
{
    const char *modos[] = {"NULL", "STA", "AP", "STA+AP"};
    wifi_mode_t m = getMode();
    dest.printf("Mode: %s\n", m <= 3 ? modos[m] : "?");
    dest.printf("Channel: %d\n", (int)channel());
    dest.printf("SSID (%d): %s\n", (int)SSID().length(), SSID().c_str());
    dest.printf("Passphrase (%d): %s\n", (int)psk().length(), psk().c_str());
    dest.printf("BSSID set: 0\n");
}

#else
/* =====================================================================
 * sem WiFi no chip (o ESP32-H2): o Network existe (a Thread, a Ethernet e o
 * PPP usam), mas nao ha interface ligada. */
#include "Network.h"
#include "cs_objeto.h"

NetworkManager Network;

static char cs_nome_do_host[33] = "esp32h2";

bool cs_eth_iniciada();
bool cs_eth_conectada();
IPAddress cs_eth_ip();
IPAddress cs_eth_roteador();
String cs_eth_mac();
bool cs_eth_config(IPAddress ip);

IPAddress NetworkInterface::calculateNetworkID(IPAddress ip, IPAddress subnet) { return IPAddress((uint32_t)ip & (uint32_t)subnet); }
IPAddress NetworkInterface::calculateBroadcast(IPAddress ip, IPAddress subnet)
{
    return IPAddress((uint32_t)ip | ~(uint32_t)subnet);
}
uint8_t NetworkInterface::calculateSubnetCIDR(IPAddress subnetMask)
{
    uint8_t n = 0;
    for (uint32_t m = (uint32_t)subnetMask; m; m >>= 1) n += m & 1;
    return n;
}
bool NetworkInterface::config(IPAddress local_ip, IPAddress gateway, IPAddress subnet, IPAddress dns1, IPAddress dns2, IPAddress dns3)
{
    (void)gateway;
    (void)subnet;
    (void)dns1;
    (void)dns2;
    (void)dns3;
    return cs_eth_config(local_ip);
}
bool NetworkInterface::dnsIP(uint8_t dns_no, IPAddress ip)
{
    (void)dns_no;
    (void)ip;
    return true;
}
const char *NetworkInterface::getHostname() const { return NetworkManager::getHostname(); }
bool NetworkInterface::setHostname(const char *hostname) const { return NetworkManager::setHostname(hostname); }
bool NetworkInterface::started() const { return cs_eth_iniciada(); }
bool NetworkInterface::connected() const { return cs_eth_conectada(); }
bool NetworkInterface::hasIP() const { return connected(); }
const char *NetworkInterface::ifkey() const { return "ETH_DEF"; }
const char *NetworkInterface::desc() const { return "eth"; }
String NetworkInterface::impl_name() const { return "en"; }
uint8_t *NetworkInterface::macAddress(uint8_t *mac) const
{
    unsigned b[6] = {};
    sscanf(cs_eth_mac().c_str(), "%x:%x:%x:%x:%x:%x", &b[0], &b[1], &b[2], &b[3], &b[4], &b[5]);
    for (int i = 0; i < 6; i++) mac[i] = (uint8_t)b[i];
    return mac;
}
String NetworkInterface::macAddress() const { return cs_eth_mac(); }
IPAddress NetworkInterface::localIP() const { return cs_eth_ip(); }
IPAddress NetworkInterface::subnetMask() const { return connected() ? IPAddress(255, 255, 255, 0) : IPAddress(); }
IPAddress NetworkInterface::gatewayIP() const { return cs_eth_roteador(); }
IPAddress NetworkInterface::dnsIP(uint8_t dns_no) const
{
    (void)dns_no;
    return cs_eth_roteador();
}
IPAddress NetworkInterface::broadcastIP() const { return calculateBroadcast(localIP(), subnetMask()); }
IPAddress NetworkInterface::networkID() const { return calculateNetworkID(localIP(), subnetMask()); }
uint8_t NetworkInterface::subnetCIDR() const { return calculateSubnetCIDR(subnetMask()); }
int NetworkInterface::getStatusBits() const { return Network.getStatusBits(); }
int NetworkInterface::waitStatusBits(int bits, uint32_t timeout_ms) const { return Network.waitStatusBits(bits, timeout_ms); }
size_t NetworkInterface::printTo(Print &out) const
{
    size_t n = out.printf("%s: <%s%s> ", desc(), started() ? "UP" : "DOWN", connected() ? ",CONNECTED" : "");
    n += out.printf("ether %s", macAddress().c_str());
    n += out.println();
    n += printDriverInfo(out);
    return n;
}

bool NetworkManager::begin()
{
    static bool feito = false;
    if (feito) return true;
    feito = true;
    return initNetworkEvents();
}
int NetworkManager::hostByName(const char *aHostname, IPAddress &aResult)
{
    String ip = cs_obj_t(nullptr, "__dns", aHostname ? aHostname : "");
    if (!ip.length()) return 0;
    return aResult.fromString(ip) ? 1 : 0;
}
uint8_t *NetworkManager::macAddress(uint8_t *mac)
{
    uint64_t m = ESP.getEfuseMac();
    for (int i = 0; i < 6; i++) mac[i] = (uint8_t)(m >> (8 * i));
    return mac;
}
String NetworkManager::macAddress()
{
    uint8_t m[6];
    char t[18];
    macAddress(m);
    snprintf(t, sizeof(t), "%02X:%02X:%02X:%02X:%02X:%02X", m[0], m[1], m[2], m[3], m[4], m[5]);
    return String(t);
}
bool NetworkManager::setDefaultInterface(NetworkInterface &ifc)
{
    (void)ifc;
    return false;
}
NetworkInterface *NetworkManager::getDefaultInterface() { return nullptr; }
bool NetworkManager::isOnline() { return cs_eth_conectada(); }
size_t NetworkManager::printTo(Print &out) const { return out.printf("Network: hostname %s\n", getHostname()); }
const char *NetworkManager::getHostname() { return cs_nome_do_host; }
bool NetworkManager::setHostname(const char *hostname)
{
    if (!hostname) return false;
    snprintf(cs_nome_do_host, sizeof(cs_nome_do_host), "%s", hostname);
    return true;
}
#endif
#endif
