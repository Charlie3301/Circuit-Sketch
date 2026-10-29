/* NetworkManager.h -- o "Network" do core 3.x: os eventos de rede (os do
 * NetworkEvents de verdade), o nome do host e a resolucao de nomes (DNS),
 * pela rede da bancada do simulador (cs_wifi.cpp). */
#pragma once

#include "NetworkEvents.h"
#include "NetworkInterface.h"
#include "IPAddress.h"
#include "WString.h"

class NetworkManager : public NetworkEvents, public Printable {
public:
    NetworkManager() {}

    bool begin();
    /* 1 e o IP em aResult; 0 se o nome nao resolveu. */
    int hostByName(const char *aHostname, IPAddress &aResult);
    uint8_t *macAddress(uint8_t *mac);
    String macAddress();
    bool setDefaultInterface(NetworkInterface &ifc);
    NetworkInterface *getDefaultInterface();
    bool isOnline();
    size_t printTo(Print &out) const override;

    static const char *getHostname();
    static bool setHostname(const char *hostname);
    static bool hostname(const String &aHostname) { return setHostname(aHostname.c_str()); }

    /* O simulador posta os eventos da rede da bancada por aqui. */
    using NetworkEvents::postEvent;
};

extern NetworkManager Network;
