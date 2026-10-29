/* WiFi.h -- o WiFi da ESP32 (e do ESP8266, pelo ESP8266WiFi.h): a rede da
 * bancada do simulador, com o mesmo roteador, IP e internet do interpretado. */
#ifndef WiFi_h
#define WiFi_h

#include "cs_objeto.h"
#include "IPAddress.h"
#include "WiFiClient.h"

typedef enum {
    WL_NO_SHIELD = 255,
    WL_IDLE_STATUS = 0,
    WL_NO_SSID_AVAIL = 1,
    WL_SCAN_COMPLETED = 2,
    WL_CONNECTED = 3,
    WL_CONNECT_FAILED = 4,
    WL_CONNECTION_LOST = 5,
    WL_DISCONNECTED = 6,
} wl_status_t;

typedef enum { WIFI_OFF = 0, WIFI_STA = 1, WIFI_AP = 2, WIFI_AP_STA = 3 } wifi_mode_t;
typedef wifi_mode_t WiFiMode_t;
#define WIFI_MODE_NULL WIFI_OFF
#define WIFI_MODE_STA WIFI_STA
#define WIFI_MODE_AP WIFI_AP
#define WIFI_MODE_APSTA WIFI_AP_STA

typedef enum {
    WIFI_AUTH_OPEN = 0,
    WIFI_AUTH_WEP,
    WIFI_AUTH_WPA_PSK,
    WIFI_AUTH_WPA2_PSK,
    WIFI_AUTH_WPA_WPA2_PSK,
    WIFI_AUTH_WPA2_ENTERPRISE,
    WIFI_AUTH_WPA3_PSK,
    WIFI_AUTH_WPA2_WPA3_PSK,
} wifi_auth_mode_t;

#define WIFI_SCAN_RUNNING (-1)
#define WIFI_SCAN_FAILED (-2)

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

class WiFiClass {
public:
    wl_status_t begin(const char *ssid, const char *passphrase = NULL, int32_t channel = 0, const uint8_t *bssid = NULL,
                      bool connect = true)
    {
        (void)channel;
        (void)bssid;
        (void)connect;
        return passphrase ? (wl_status_t)cs_obj_i("WiFi", "begin", ssid, passphrase) : (wl_status_t)cs_obj_i("WiFi", "begin", ssid);
    }
    wl_status_t begin(const String &ssid, const String &pass) { return begin(ssid.c_str(), pass.c_str()); }
    wl_status_t begin() { return (wl_status_t)cs_obj_i("WiFi", "begin"); }
    wl_status_t status() { return (wl_status_t)cs_obj_i("WiFi", "status"); }
    bool isConnected() { return status() == WL_CONNECTED; }
    bool disconnect(bool wifioff = false, bool eraseap = false) { (void)eraseap; return cs_obj_i("WiFi", "disconnect", wifioff) != 0; }
    bool reconnect() { return cs_obj_i("WiFi", "reconnect") != 0; }
    bool mode(wifi_mode_t m) { return cs_obj_i("WiFi", "mode", (int)m) != 0; }
    wifi_mode_t getMode() { return (wifi_mode_t)cs_obj_i("WiFi", "getMode"); }
    bool config(IPAddress local_ip, IPAddress gateway, IPAddress subnet, IPAddress dns1 = IPAddress(), IPAddress dns2 = IPAddress())
    {
        (void)dns2;
        return cs_obj_i("WiFi", "config", local_ip, gateway, subnet, dns1) != 0;
    }
    IPAddress localIP() { return cs_ip(cs_obj_t("WiFi", "localIP")); }
    IPAddress gatewayIP() { return cs_ip(cs_obj_t("WiFi", "gatewayIP")); }
    IPAddress subnetMask() { return cs_ip(cs_obj_t("WiFi", "subnetMask")); }
    IPAddress dnsIP(uint8_t i = 0) { return cs_ip(cs_obj_t("WiFi", "dnsIP", i)); }
    IPAddress softAPIP() { return cs_ip(cs_obj_t("WiFi", "softAPIP")); }
    String macAddress() { return cs_obj_t("WiFi", "macAddress"); }
    uint8_t *macAddress(uint8_t *mac)
    {
        unsigned v[6] = {0};
        sscanf(macAddress().c_str(), "%x:%x:%x:%x:%x:%x", &v[0], &v[1], &v[2], &v[3], &v[4], &v[5]);
        for (int i = 0; i < 6; i++) mac[i] = (uint8_t)v[i];
        return mac;
    }
    String softAPmacAddress() { return cs_obj_t("WiFi", "softAPmacAddress"); }
    String SSID() { return cs_obj_t("WiFi", "SSID"); }
    String SSID(uint8_t i) { return cs_obj_t("WiFi", "SSID", i); }
    int8_t RSSI() { return (int8_t)cs_obj_i("WiFi", "RSSI"); }
    int32_t RSSI(uint8_t i) { return (int32_t)cs_obj_i("WiFi", "RSSI", i); }
    String BSSIDstr() { return cs_obj_t("WiFi", "BSSIDstr"); }
    wifi_auth_mode_t encryptionType(uint8_t i) { return (wifi_auth_mode_t)cs_obj_i("WiFi", "encryptionType", i); }
    int16_t scanNetworks(bool async = false, bool show_hidden = false) { (void)async; (void)show_hidden; return (int16_t)cs_obj_i("WiFi", "scanNetworks"); }
    void scanDelete() { cs_obj_n("WiFi", "scanDelete"); }
    bool softAP(const char *ssid, const char *passphrase = NULL, int channel = 1, int ssid_hidden = 0, int max_connection = 4)
    {
        (void)channel;
        (void)ssid_hidden;
        (void)max_connection;
        return passphrase ? cs_obj_i("WiFi", "softAP", ssid, passphrase) != 0 : cs_obj_i("WiFi", "softAP", ssid) != 0;
    }
    bool softAPdisconnect(bool wifioff = false) { return cs_obj_i("WiFi", "softAPdisconnect", wifioff) != 0; }
    bool setHostname(const char *hostname) { return cs_obj_i("WiFi", "setHostname", hostname) != 0; }
    const char *getHostname()
    {
        static String h;
        h = cs_obj_t("WiFi", "getHostname");
        return h.c_str();
    }
    String hostname() { return cs_obj_t("WiFi", "hostname"); }
    bool hostname(const char *h) { return cs_obj_i("WiFi", "hostname", h) != 0; }
    bool setSleep(bool enabled) { return cs_obj_i("WiFi", "setSleep", enabled) != 0; }
    bool setAutoReconnect(bool autoReconnect) { return cs_obj_i("WiFi", "setAutoReconnect", autoReconnect) != 0; }
    bool setAutoConnect(bool autoConnect) { return cs_obj_i("WiFi", "setAutoConnect", autoConnect) != 0; }
    void persistent(bool persistent) { cs_obj_n("WiFi", "persistent", persistent); }
    bool setTxPower(int power) { return cs_obj_i("WiFi", "setTxPower", power) != 0; }
    uint8_t waitForConnectResult(unsigned long timeoutLength = 60000)
    {
        (void)timeoutLength;
        return (uint8_t)cs_obj_i("WiFi", "waitForConnectResult");
    }
    template <class F> void onEvent(F f) { cs_obj_n("WiFi", "onEvent", f); }
};

inline WiFiClass WiFi;

#endif
