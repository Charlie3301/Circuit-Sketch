/* WiFi.h -- o WiFi da ESP32 (e do ESP8266, pelo ESP8266WiFi.h): a rede da
 * bancada do simulador, com o mesmo roteador, IP e internet do interpretado.
 *
 * A API e a do core 3.x (o WiFiSTA, o WiFiAP, o WiFiScan e o WiFiGeneric
 * juntos no WiFiClass), com os tipos de verdade (WiFiType.h e o
 * esp_wifi_types.h do IDF). Os eventos (WiFi.onEvent) vem da tarefa
 * "arduino_events", como na placa: o simulador acompanha a rede e posta
 * cada mudanca (cs_wifi.cpp). */
#ifndef WiFi_h
#define WiFi_h

#if !defined(ESP32)
#include "cs_wifi_simples.h"
#else

/* O chip sem WiFi (a H2): como no core, o WiFi.h nao declara nada. */
#include "soc/soc_caps.h"
#if SOC_WIFI_SUPPORTED || CONFIG_ESP_HOSTED_ENABLED

#include "cs_objeto.h"
#include "IPAddress.h"
#include "WiFiType.h"
#include "esp_wifi_types.h"
#include "esp_smartconfig.h"
#include "Network.h"
#include "WiFiClient.h"
#include "WiFiUdp.h"

#define WiFiEventCb NetworkEventCb
#define WiFiEventFuncCb NetworkEventFuncCb
#define WiFiEventSysCb NetworkEventSysCb
#define wifi_event_id_t network_event_handle_t

typedef enum {
    WIFI_POWER_21dBm = 84,
    WIFI_POWER_20_5dBm = 82,
    WIFI_POWER_20dBm = 80,
    WIFI_POWER_19_5dBm = 78,
    WIFI_POWER_19dBm = 76,
    WIFI_POWER_18_5dBm = 74,
    WIFI_POWER_17dBm = 68,
    WIFI_POWER_15dBm = 60,
    WIFI_POWER_13dBm = 52,
    WIFI_POWER_11dBm = 44,
    WIFI_POWER_8_5dBm = 34,
    WIFI_POWER_7dBm = 28,
    WIFI_POWER_5dBm = 20,
    WIFI_POWER_2dBm = 8,
    WIFI_POWER_MINUS_1dBm = -4
} wifi_power_t;

typedef enum { WIFI_RX_ANT0 = 0, WIFI_RX_ANT1, WIFI_RX_ANT_AUTO } wifi_rx_ant_t;
typedef enum { WIFI_TX_ANT0 = 0, WIFI_TX_ANT1, WIFI_TX_ANT_AUTO } wifi_tx_ant_t;

/* O IP que o simulador devolve em texto ("192.168.4.2") vira IPAddress. */
inline IPAddress cs_ip(const String &texto)
{
    IPAddress ip;
    ip.fromString(texto);
    return ip;
}
inline cs_val cs_de(const IPAddress &ip)
{
    static thread_local char t[8][16];
    static thread_local int k;
    char *s = t[k++ & 7];
    snprintf(s, 16, "%s", ip.toString().c_str());
    return cs_t(s);
}

/* O STA e o AP como interfaces de rede (WiFi.STA, WiFi.AP), o jeito do 3.x. */
class STAClass : public NetworkInterface {
public:
    STAClass() : NetworkInterface(ESP_NETIF_ID_STA) {}
    bool begin(bool tryConnect = false);
    bool end();
    bool connect();
    bool connect(const char *ssid, const char *passphrase = NULL, int32_t channel = 0, const uint8_t *bssid = NULL, bool tryConnect = true);
    bool disconnect(bool eraseap = false, unsigned long timeout = 0);
    bool reconnect();
    bool erase();
    uint8_t waitForConnectResult(unsigned long timeoutLength = 60000);
    bool setAutoReconnect(bool autoReconnect);
    bool getAutoReconnect();
    wl_status_t status();
    String SSID() const;
    String psk() const;
    uint8_t *BSSID(uint8_t *bssid = NULL);
    String BSSIDstr();
    int8_t RSSI();
    bool bandwidth(wifi_bandwidth_t b) { (void)b; return true; }
    const char *disconnectReasonName(wifi_err_reason_t reason);
    uint16_t getInactiveTime() { return _inativo; }
    bool setInactiveTime(uint16_t segundos) { _inativo = segundos; return true; }
    void setMinSecurity(wifi_auth_mode_t) {}
    void setScanMethod(wifi_scan_method_t) {}
    void setSortMethod(wifi_sort_method_t) {}

protected:
    size_t printDriverInfo(Print &out) const override;

private:
    uint16_t _inativo = 6;
};

class APClass : public NetworkInterface {
public:
    APClass() : NetworkInterface(ESP_NETIF_ID_AP) {}
    bool begin();
    bool end();
    bool create(const char *ssid, const char *passphrase = NULL, int channel = 1, int ssid_hidden = 0, int max_connection = 4,
                bool ftm_responder = false, wifi_auth_mode_t auth_mode = WIFI_AUTH_WPA2_PSK, wifi_cipher_type_t cipher = WIFI_CIPHER_TYPE_CCMP);
    bool clear();
    bool bandwidth(wifi_bandwidth_t bandwidth);
    bool enableNAPT(bool enable = true);
    bool enableDhcpCaptivePortal();
    String SSID(void) const;
    uint8_t stationCount();

protected:
    size_t printDriverInfo(Print &out) const override;
};

/* As partes do WiFiClass de verdade (o NetworkEvents as declara amigas). */
class WiFiGenericClass {
public:
    /* Como na placa, sem precisar do objeto: WiFiGenericClass::getMode(). */
    static wifi_mode_t getMode() { return (wifi_mode_t)cs_obj_i("WiFi", "getMode"); }
    /* Os pinos do radio externo (SDIO, nas placas sem Wi-Fi no chip): o do simulador nao tem. */
    static bool setPins(int8_t, int8_t, int8_t, int8_t, int8_t, int8_t, int8_t) { return true; }
};
class WiFiSTAClass {};
class WiFiScanClass {};
class WiFiAPClass {};

class WiFiClass : public WiFiGenericClass, public WiFiSTAClass, public WiFiScanClass, public WiFiAPClass {
    bool _persistent = true;
    bool _autoReconnect = true;
    wifi_ps_type_t _sono = WIFI_PS_MIN_MODEM;
    wifi_power_t _potencia = WIFI_POWER_19_5dBm;
    uint16_t _inativo = 300;
    int16_t _varredura = -2;
    uint32_t _tempoDaVarredura = 10000;

public:
    STAClass STA;
    APClass AP;

    /* ---- o STA (WiFiSTA.h) ---- */
    wl_status_t begin(const char *ssid, const char *passphrase = NULL, int32_t channel = 0, const uint8_t *bssid = NULL,
                      bool connect = true);
    wl_status_t begin(const String &ssid, const String &pass = String(), int32_t channel = 0, const uint8_t *bssid = NULL,
                      bool connect = true)
    {
        return begin(ssid.c_str(), pass.c_str(), channel, bssid, connect);
    }
    wl_status_t begin(const char *ssid, const char *passphrase, int32_t channel, const uint8_t *bssid, bool connect, bool)
    {
        return begin(ssid, passphrase, channel, bssid, connect);
    }
    wl_status_t begin() { return (wl_status_t)cs_obj_i("WiFi", "begin"); }
    bool config(IPAddress local_ip, IPAddress gateway, IPAddress subnet, IPAddress dns1 = IPAddress(), IPAddress dns2 = IPAddress())
    {
        (void)dns2;
        return cs_obj_i("WiFi", "config", local_ip, gateway, subnet, dns1) != 0;
    }
    bool setDNS(IPAddress dns1, IPAddress dns2 = (uint32_t)0x00000000);
    bool reconnect() { return cs_obj_i("WiFi", "reconnect") != 0; }
    bool disconnectAsync(bool wifioff = false, bool eraseap = false) { return disconnect(wifioff, eraseap); }
    bool disconnect(bool wifioff = false, bool eraseap = false, unsigned long timeoutLength = 100);
    bool eraseAP(void) { return true; }
    bool isConnected() { return status() == WL_CONNECTED; }
    bool setAutoReconnect(bool autoReconnect)
    {
        _autoReconnect = autoReconnect;
        return cs_obj_i("WiFi", "setAutoReconnect", autoReconnect) != 0;
    }
    bool getAutoReconnect() { return _autoReconnect; }
    bool setAutoConnect(bool autoConnect) { return cs_obj_i("WiFi", "setAutoConnect", autoConnect) != 0; }
    uint8_t waitForConnectResult(unsigned long timeoutLength = 60000);
    void setMinSecurity(wifi_auth_mode_t minSecurity) { (void)minSecurity; }
    void setScanMethod(wifi_scan_method_t scanMethod) { (void)scanMethod; }
    void setSortMethod(wifi_sort_method_t sortMethod) { (void)sortMethod; }
    IPAddress localIP() { return cs_ip(cs_obj_t("WiFi", "localIP")); }
    uint8_t *macAddress(uint8_t *mac)
    {
        unsigned v[6] = {0};
        sscanf(macAddress().c_str(), "%x:%x:%x:%x:%x:%x", &v[0], &v[1], &v[2], &v[3], &v[4], &v[5]);
        for (int i = 0; i < 6; i++) mac[i] = (uint8_t)v[i];
        return mac;
    }
    String macAddress() { return cs_obj_t("WiFi", "macAddress"); }
    IPAddress subnetMask() { return cs_ip(cs_obj_t("WiFi", "subnetMask")); }
    IPAddress gatewayIP() { return cs_ip(cs_obj_t("WiFi", "gatewayIP")); }
    IPAddress dnsIP(uint8_t i = 0) { return cs_ip(cs_obj_t("WiFi", "dnsIP", i)); }
    IPAddress broadcastIP() { return NetworkInterface::calculateBroadcast(localIP(), subnetMask()); }
    IPAddress networkID() { return NetworkInterface::calculateNetworkID(localIP(), subnetMask()); }
    uint8_t subnetCIDR() { return NetworkInterface::calculateSubnetCIDR(subnetMask()); }
    bool enableIPv6(bool en = true) { return STA.enableIPv6(en); }
    IPAddress linkLocalIPv6() { return STA.linkLocalIPv6(); }
    IPAddress globalIPv6() { return STA.globalIPv6(); }
    IPAddress softAPlinkLocalIPv6() { return AP.linkLocalIPv6(); }
    bool bandwidth(wifi_bandwidth_t b) { return STA.bandwidth(b); }
    const char *getHostname()
    {
        static String h;
        h = cs_obj_t("WiFi", "getHostname");
        return h.c_str();
    }
    bool setHostname(const char *hostname) { return cs_obj_i("WiFi", "setHostname", hostname) != 0; }
    String hostname() { return cs_obj_t("WiFi", "hostname"); }
    bool hostname(const char *h) { return cs_obj_i("WiFi", "hostname", h) != 0; }
    bool hostname(const String &h) { return hostname(h.c_str()); }
    wl_status_t status() { return (wl_status_t)cs_obj_i("WiFi", "status"); }
    String SSID() { return cs_obj_t("WiFi", "SSID"); }
    String psk() const;
    uint8_t *BSSID(uint8_t *bssid = NULL);
    String BSSIDstr() { return cs_obj_t("WiFi", "BSSIDstr"); }
    int8_t RSSI() { return (int8_t)cs_obj_i("WiFi", "RSSI"); }
    bool beginSmartConfig(smartconfig_type_t type = SC_TYPE_ESPTOUCH, char *crypt_key = NULL);
    bool stopSmartConfig() { return true; }
    bool smartConfigDone() { return false; }
    void enableProv(bool status) { _prov = status; }
    bool isProvEnabled() { return _prov; }

    /* ---- o AP (WiFiAP.h) ---- */
    bool softAP(const char *ssid, const char *passphrase = NULL, int channel = 1, int ssid_hidden = 0, int max_connection = 4,
                bool ftm_responder = false, wifi_auth_mode_t auth_mode = WIFI_AUTH_WPA2_PSK, wifi_cipher_type_t cipher = WIFI_CIPHER_TYPE_CCMP);
    bool softAP(const String &ssid, const String &passphrase = String(), int channel = 1, int ssid_hidden = 0, int max_connection = 4,
                bool ftm_responder = false)
    {
        return softAP(ssid.c_str(), passphrase.length() ? passphrase.c_str() : NULL, channel, ssid_hidden, max_connection, ftm_responder);
    }
    bool softAPConfig(IPAddress local_ip, IPAddress gateway, IPAddress subnet, IPAddress dhcp_lease_start = (uint32_t)0,
                      IPAddress dns = (uint32_t)0);
    bool softAPdisconnect(bool wifioff = false, bool cs_parar = false)
    {
        (void)cs_parar;
        return cs_obj_i("WiFi", "softAPdisconnect", wifioff) != 0;
    }
    bool softAPbandwidth(wifi_bandwidth_t bandwidth) { return AP.bandwidth(bandwidth); }
    uint8_t softAPgetStationNum();
    String softAPSSID(void) const;
    IPAddress softAPIP() { return cs_ip(cs_obj_t("WiFi", "softAPIP")); }
    IPAddress softAPBroadcastIP() { return NetworkInterface::calculateBroadcast(softAPIP(), softAPSubnetMask()); }
    IPAddress softAPNetworkID() { return NetworkInterface::calculateNetworkID(softAPIP(), softAPSubnetMask()); }
    IPAddress softAPSubnetMask() { return IPAddress(255, 255, 255, 0); }
    uint8_t softAPSubnetCIDR() { return NetworkInterface::calculateSubnetCIDR(softAPSubnetMask()); }
    bool softAPenableIPv6(bool enable = true) { return AP.enableIPv6(enable); }
    const char *softAPgetHostname() { return getHostname(); }
    bool softAPsetHostname(const char *hostname) { return setHostname(hostname); }
    uint8_t *softAPmacAddress(uint8_t *mac)
    {
        unsigned v[6] = {0};
        sscanf(softAPmacAddress().c_str(), "%x:%x:%x:%x:%x:%x", &v[0], &v[1], &v[2], &v[3], &v[4], &v[5]);
        for (int i = 0; i < 6; i++) mac[i] = (uint8_t)v[i];
        return mac;
    }
    String softAPmacAddress() { return cs_obj_t("WiFi", "softAPmacAddress"); }

    /* ---- a varredura (WiFiScan.h) ---- */
    int16_t scanNetworks(bool async = false, bool show_hidden = false, bool passive = false, uint32_t max_ms_per_chan = 300,
                         uint8_t channel = 0, const char *ssid = nullptr, const uint8_t *bssid = nullptr);
    void setScanTimeout(uint32_t ms) { _tempoDaVarredura = ms; }
    void setScanActiveMinTime(uint32_t ms) { (void)ms; }
    int16_t scanComplete() { return _varredura; }
    void scanDelete()
    {
        cs_obj_n("WiFi", "scanDelete");
        _varredura = WIFI_SCAN_FAILED;
    }
    bool getNetworkInfo(uint8_t networkItem, String &ssid, uint8_t &encryptionType, int32_t &RSSI, uint8_t *&BSSID, int32_t &channel);
    String SSID(uint8_t i) { return cs_obj_t("WiFi", "SSID", i); }
    wifi_auth_mode_t encryptionType(uint8_t i) { return (wifi_auth_mode_t)cs_obj_i("WiFi", "encryptionType", i); }
    int32_t RSSI(uint8_t i) { return (int32_t)cs_obj_i("WiFi", "RSSI", i); }
    uint8_t *BSSID(uint8_t networkItem, uint8_t *bssid);
    String BSSIDstr(uint8_t networkItem);
    int32_t channel(uint8_t networkItem);
    static void *getScanInfoByIndex(int i) { (void)i; return nullptr; }

    /* ---- o geral (WiFiGeneric.h) ---- */
    wifi_event_id_t onEvent(WiFiEventCb cbEvent, arduino_event_id_t event = ARDUINO_EVENT_MAX);
    wifi_event_id_t onEvent(WiFiEventFuncCb cbEvent, arduino_event_id_t event = ARDUINO_EVENT_MAX);
    wifi_event_id_t onEvent(WiFiEventSysCb cbEvent, arduino_event_id_t event = ARDUINO_EVENT_MAX);
    void removeEvent(WiFiEventCb cbEvent, arduino_event_id_t event = ARDUINO_EVENT_MAX);
    void removeEvent(WiFiEventSysCb cbEvent, arduino_event_id_t event = ARDUINO_EVENT_MAX);
    void removeEvent(wifi_event_id_t id);
    static int getStatusBits();
    static int waitStatusBits(int bits, uint32_t timeout_ms);
    static int setStatusBits(int bits);
    static int clearStatusBits(int bits);
    int32_t channel(void);
    int setChannel(uint8_t primary, wifi_second_chan_t secondary = WIFI_SECOND_CHAN_NONE);
    void persistent(bool persistent)
    {
        _persistent = persistent;
        cs_obj_n("WiFi", "persistent", persistent);
    }
    void enableLongRange(bool enable) { (void)enable; }
    bool mode(wifi_mode_t m);
    bool enableSTA(bool enable);
    bool enableAP(bool enable);
    bool setSleep(bool enabled) { return setSleep(enabled ? WIFI_PS_MIN_MODEM : WIFI_PS_NONE); }
    bool setSleep(wifi_ps_type_t sleepType)
    {
        _sono = sleepType;
        return cs_obj_i("WiFi", "setSleep", sleepType != WIFI_PS_NONE) != 0;
    }
    wifi_ps_type_t getSleep() { return _sono; }
    bool setTxPower(wifi_power_t power)
    {
        _potencia = power;
        return cs_obj_i("WiFi", "setTxPower", (int)power) != 0;
    }
    bool setTxPower(int power) { return setTxPower((wifi_power_t)power); }
    wifi_power_t getTxPower() { return _potencia; }
    bool setBandMode(wifi_band_mode_t band_mode) { return band_mode == WIFI_BAND_MODE_2G_ONLY; }
    wifi_band_mode_t getBandMode() { return WIFI_BAND_MODE_2G_ONLY; }
    wifi_band_t getBand() { return WIFI_BAND_2G; }
    bool initiateFTM(uint8_t frm_count = 16, uint16_t burst_period = 2, uint8_t channel = 1, const uint8_t *mac = NULL)
    {
        (void)frm_count, (void)burst_period, (void)channel, (void)mac;
        return false;
    }
    bool setDualAntennaConfig(uint8_t gpio_ant1, uint8_t gpio_ant2, wifi_rx_ant_t rx_mode, wifi_tx_ant_t tx_mode)
    {
        (void)gpio_ant1, (void)gpio_ant2, (void)rx_mode, (void)tx_mode;
        return true;
    }
    bool setInactiveTime(uint16_t seconds)
    {
        _inativo = seconds;
        return true;
    }
    uint16_t getInactiveTime() { return _inativo; }
    static const char *eventName(arduino_event_id_t id) { return NetworkEvents::eventName(id); }
    static const char *disconnectReasonName(wifi_err_reason_t reason);
    static bool useStaticBuffers() { return false; }
    static void useStaticBuffers(bool bufferMode) { (void)bufferMode; }
    static int hostByName(const char *aHostname, IPAddress &aResult) { return Network.hostByName(aHostname, aResult); }
    static IPAddress calculateNetworkID(IPAddress ip, IPAddress subnet) { return NetworkInterface::calculateNetworkID(ip, subnet); }
    static IPAddress calculateBroadcast(IPAddress ip, IPAddress subnet) { return NetworkInterface::calculateBroadcast(ip, subnet); }
    static uint8_t calculateSubnetCIDR(IPAddress subnetMask) { return NetworkInterface::calculateSubnetCIDR(subnetMask); }
    void printDiag(Print &dest);
    size_t printDriverInfo(Print &out) const { (void)out; return 0; }

    /* Os que o simulador usa por dentro. */
    void cs_varreduraTerminou(int16_t n) { _varredura = n; }

private:
    bool _prov = false;
};

extern WiFiClass WiFi;

#endif /* SOC_WIFI_SUPPORTED */
#endif /* ESP32 */
#endif
