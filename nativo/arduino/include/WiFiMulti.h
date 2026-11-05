/* WiFiMulti.h -- o WiFiMulti da ESP32: varias redes conhecidas, e o run()
 * conecta na melhor que estiver por perto (a de sinal mais forte), como o
 * do core oficial. */
#pragma once

#include "WiFi.h"
#include <vector>
#include <functional>

typedef struct {
    char *ssid;
    char *passphrase;
    bool hasFailed;
} WifiAPlist_t;

typedef std::function<bool(void)> ConnectionTestCB_t;

class WiFiMulti {
public:
    WiFiMulti() {}
    void enableIPv6(bool) {}
    ~WiFiMulti() { APlistClean(); }

    bool addAP(const char *ssid, const char *passphrase = NULL)
    {
        if (!ssid || !*ssid || strlen(ssid) > 31) {
            log_e("[WIFI][APlistAdd] no ssid or ssid too long");
            return false;
        }
        if (passphrase && strlen(passphrase) > 64) {
            log_e("[WIFI][APlistAdd] passphrase too long");
            return false;
        }
        WifiAPlist_t ap = {strdup(ssid), passphrase ? strdup(passphrase) : nullptr, false};
        APlist.push_back(ap);
        return true;
    }

    /* Ja conectado numa conhecida: fica. Senao varre, escolhe a conhecida de
     * sinal mais forte (as que falharam por ultimo, so se nao houver outra) e
     * conecta, esperando ate connectTimeout. */
    uint8_t run(uint32_t connectTimeout = 5000, bool scanHidden = false)
    {
        (void)scanHidden;
        if (WiFi.status() == WL_CONNECTED) {
            if (!_connectionTestCBFunc || _connectionTestCBFunc()) return WL_CONNECTED;
            WiFi.disconnect(false, false);
            delay(10);
        }
        int16_t n = WiFi.scanNetworks();
        if (n <= 0) {
            WiFi.scanDelete();
            return WiFi.status();
        }
        int melhor = -1;
        int32_t melhorRssi = -1000;
        bool melhorFalhou = true;
        for (int16_t i = 0; i < n; i++) {
            String nome = WiFi.SSID(i);
            int32_t rssi = WiFi.RSSI(i);
            bool aberta = WiFi.encryptionType(i) == WIFI_AUTH_OPEN;
            for (size_t k = 0; k < APlist.size(); k++) {
                if (nome != APlist[k].ssid) continue;
                bool falhou = APlist[k].hasFailed && _bStrict;
                if (melhor < 0 || (melhorFalhou && !falhou) || (falhou == melhorFalhou && rssi > melhorRssi)) {
                    melhor = (int)k;
                    melhorRssi = rssi;
                    melhorFalhou = falhou;
                }
            }
            /* A rede aberta que ninguem cadastrou, se o sketch deixou. */
            if (melhor < 0 && aberta && _bAllowOpenAP) {
                APlist.push_back({strdup(nome.c_str()), nullptr, false});
                melhor = (int)APlist.size() - 1;
                melhorRssi = rssi;
                melhorFalhou = false;
            }
        }
        WiFi.scanDelete();
        if (melhor < 0) return WL_NO_SSID_AVAIL;

        WiFi.begin(APlist[melhor].ssid, APlist[melhor].passphrase);
        uint32_t ate = millis() + connectTimeout;
        wl_status_t st = WiFi.status();
        while (st != WL_CONNECTED && millis() < ate) {
            delay(10);
            st = WiFi.status();
        }
        if (st == WL_CONNECTED && _connectionTestCBFunc && !_connectionTestCBFunc()) st = WL_CONNECT_FAILED;
        if (st == WL_CONNECTED) {
            resetFails();
        } else {
            APlist[melhor].hasFailed = true;
        }
        return st;
    }

    void setStrictMode(bool bStrict = true) { _bStrict = bStrict; }
    void setAllowOpenAP(bool bAllowOpenAP = false) { _bAllowOpenAP = bAllowOpenAP; }
    void APlistClean(void)
    {
        for (auto &ap : APlist) {
            free(ap.ssid);
            free(ap.passphrase);
        }
        APlist.clear();
    }
    void setConnectionTestCallbackFunc(ConnectionTestCB_t cbFunc) { _connectionTestCBFunc = cbFunc; }

private:
    std::vector<WifiAPlist_t> APlist;
    bool _bStrict = true;
    bool _bAllowOpenAP = false;
    ConnectionTestCB_t _connectionTestCBFunc = NULL;

    void resetFails()
    {
        for (auto &ap : APlist) ap.hasFailed = false;
    }
};
