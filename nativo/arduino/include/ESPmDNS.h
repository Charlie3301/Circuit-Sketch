/* ESPmDNS.h -- o mDNS da ESP32: a placa responde por "nome.local", anuncia
 * servicos (o "_http._tcp" na porta 80) e acha os das outras.
 *
 * Na rede da bancada do simulador: as outras placas acham esta por
 * "nome.local" (hostByName, MDNS.queryHost) e o queryService lista as
 * placas que anunciaram o servico. A mesma API do core oficial. */
#ifndef ESP32MDNS_H
#define ESP32MDNS_H

#include "Arduino.h"
#include "IPAddress.h"
#include "WiFi.h"
#include "cs_objeto.h"
#include <vector>

#ifndef ARDUINO_VARIANT
#define ARDUINO_VARIANT "esp32"
#endif

class MDNSResponder {
    struct Achado {
        String nome;
        IPAddress ip;
        uint16_t porta;
    };
    String _hostname, _instancia;
    std::vector<Achado> _achados;
    std::vector<std::pair<String, String>> _txt;

public:
    MDNSResponder() {}
    ~MDNSResponder() { end(); }

    bool begin(const String &hostName)
    {
        if (!hostName.length()) return false;
        _hostname = hostName;
        _hostname.toLowerCase();
        return cs_obj_i(nullptr, "__mdns", _hostname) != 0;
    }
    bool begin(const char *hostName) { return begin(String(hostName)); }
    void end()
    {
        if (_hostname.length()) cs_obj_n(nullptr, "__mdns", "");
        _hostname = "";
    }
    void setInstanceName(String name) { _instancia = name; }
    void setInstanceName(const char *name) { setInstanceName(String(name)); }
    void setInstanceName(char *name) { setInstanceName(String(name)); }

    bool addService(char *service, char *proto, uint16_t port)
    {
        if (!_hostname.length()) return false;
        return cs_obj_i(nullptr, "__mdns_servico", service, proto, (int)port) != 0;
    }
    bool addService(const char *service, const char *proto, uint16_t port) { return addService((char *)service, (char *)proto, port); }
    bool addService(String service, String proto, uint16_t port) { return addService(service.c_str(), proto.c_str(), port); }
    bool addServiceTxt(char *name, char *proto, char *key, char *value)
    {
        (void)name;
        (void)proto;
        _txt.push_back({String(key), String(value)});
        return true;
    }
    void addServiceTxt(const char *name, const char *proto, const char *key, const char *value)
    {
        addServiceTxt((char *)name, (char *)proto, (char *)key, (char *)value);
    }
    void addServiceTxt(String name, String proto, String key, String value)
    {
        addServiceTxt(name.c_str(), proto.c_str(), key.c_str(), value.c_str());
    }
    /* O "_arduino._tcp" que a IDE procura para mandar o sketch pela rede (OTA). */
    void enableArduino(uint16_t port = 3232, bool auth = false)
    {
        (void)auth;
        addService("arduino", "tcp", port);
    }
    void disableArduino() {}
#if SOC_WIFI_SUPPORTED
    void enableWorkstation(wifi_interface_t interface = WIFI_IF_STA) { (void)interface; }
#endif
    void disableWorkstation() {}

    IPAddress queryHost(char *host, uint32_t timeout = 2000)
    {
        (void)timeout;
        IPAddress ip;
        String nome = String(host) + ".local";
        String achado = cs_obj_t(nullptr, "__dns", nome);
        if (achado.length()) ip.fromString(achado);
        return ip;
    }
    IPAddress queryHost(const char *host, uint32_t timeout = 2000) { return queryHost((char *)host, timeout); }
    IPAddress queryHost(String host, uint32_t timeout = 2000) { return queryHost(host.c_str(), timeout); }

    int queryService(char *service, char *proto)
    {
        _achados.clear();
        String lista = cs_obj_t(nullptr, "__mdns_buscar", service, proto);
        int ini = 0;
        while (ini < (int)lista.length()) {
            int fim = lista.indexOf('\n', ini);
            if (fim < 0) fim = lista.length();
            String linha = lista.substring(ini, fim);
            int a = linha.indexOf('|'), b = linha.lastIndexOf('|');
            if (a > 0 && b > a) {
                Achado x;
                x.nome = linha.substring(0, a);
                x.ip.fromString(linha.substring(a + 1, b));
                x.porta = (uint16_t)linha.substring(b + 1).toInt();
                _achados.push_back(x);
            }
            ini = fim + 1;
        }
        return (int)_achados.size();
    }
    int queryService(const char *service, const char *proto) { return queryService((char *)service, (char *)proto); }
    int queryService(String service, String proto) { return queryService(service.c_str(), proto.c_str()); }

    String hostname(int idx) { return idx >= 0 && idx < (int)_achados.size() ? _achados[idx].nome : String(); }
    String instanceName(int idx) { return hostname(idx); }
    IPAddress address(int idx) { return idx >= 0 && idx < (int)_achados.size() ? _achados[idx].ip : IPAddress(); }
    IPAddress IP(int idx) { return address(idx); }
    IPAddress addressV6(int idx)
    {
        (void)idx;
        return IPAddress();
    }
    uint16_t port(int idx) { return idx >= 0 && idx < (int)_achados.size() ? _achados[idx].porta : 0; }
    int numTxt(int idx)
    {
        (void)idx;
        return 0;
    }
    bool hasTxt(int idx, const char *key)
    {
        (void)idx;
        (void)key;
        return false;
    }
    String txt(int idx, const char *key)
    {
        (void)idx;
        (void)key;
        return String();
    }
    String txt(int idx, int txtIdx)
    {
        (void)idx;
        (void)txtIdx;
        return String();
    }
    String txtKey(int idx, int txtIdx)
    {
        (void)idx;
        (void)txtIdx;
        return String();
    }
};

inline MDNSResponder MDNS;

#endif
