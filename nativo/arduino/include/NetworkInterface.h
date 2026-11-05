/* NetworkInterface.h -- uma interface de rede do core 3.x da ESP32 (o STA, o
 * AP, a Ethernet): o IP, a mascara, o roteador, o MAC e o estado dela.
 *
 * A mesma API do core oficial; por baixo, a rede da bancada do simulador
 * (cs_wifi.cpp). */
#pragma once

#include "Arduino.h"
#include "Printable.h"
#include "IPAddress.h"
#include "WString.h"
#include "esp_event.h"
#include "esp_netif_types.h"

typedef enum {
    ESP_NETIF_ID_STA,
    ESP_NETIF_ID_AP,
    ESP_NETIF_ID_PPP,
    ESP_NETIF_ID_ETH0,
    ESP_NETIF_ID_ETH1,
    ESP_NETIF_ID_ETH2,
    ESP_NETIF_ID_MAX
} Network_Interface_ID;

static const int ESP_NETIF_STARTED_BIT = BIT0;
static const int ESP_NETIF_CONNECTED_BIT = BIT1;
static const int ESP_NETIF_HAS_IP_BIT = BIT2;
static const int ESP_NETIF_HAS_LOCAL_IP6_BIT = BIT3;
static const int ESP_NETIF_HAS_GLOBAL_IP6_BIT = BIT4;
static const int ESP_NETIF_WANT_IP6_BIT = BIT5;
static const int ESP_NETIF_HAS_STATIC_IP_BIT = BIT6;

#define ESP_NETIF_ID_ETH ESP_NETIF_ID_ETH0

class NetworkInterface : public Printable {
public:
    NetworkInterface() : _interface_id(ESP_NETIF_ID_MAX) {}
    explicit NetworkInterface(Network_Interface_ID id) : _interface_id(id) {}
    virtual ~NetworkInterface() {}

    bool config(IPAddress local_ip = (uint32_t)0x00000000, IPAddress gateway = (uint32_t)0x00000000,
                IPAddress subnet = (uint32_t)0x00000000, IPAddress dns1 = (uint32_t)0x00000000, IPAddress dns2 = (uint32_t)0x00000000,
                IPAddress dns3 = (uint32_t)0x00000000);
    bool dnsIP(uint8_t dns_no, IPAddress ip);
    const char *getHostname() const;
    bool setHostname(const char *hostname) const;
    bool started() const;
    bool connected() const;
    bool hasIP() const;
    bool hasLinkLocalIPv6() const { return false; }
    bool hasGlobalIPv6() const { return false; }
    IPAddress linkLocalIPv6() const { return IPAddress(IPv6); }
    IPAddress globalIPv6() const { return IPAddress(IPv6); }
    bool enableIPv6(bool en = true)
    {
        _ipv6 = en;
        return true;
    }
    bool linkUp() const { return connected(); }
    const char *ifkey() const;
    const char *desc() const;
    String impl_name() const;
    int impl_index() const { return (int)_interface_id; }
    int getRoutePrio() const { return _interface_id == ESP_NETIF_ID_STA ? 100 : 10; }
    int setRoutePrio(int prio) { return prio; }
    bool setDefault() { return true; }
    bool isDefault() const { return _interface_id == ESP_NETIF_ID_STA; }
    uint8_t *macAddress(uint8_t *mac) const;
    String macAddress() const;
    IPAddress localIP() const;
    IPAddress subnetMask() const;
    IPAddress gatewayIP() const;
    IPAddress dnsIP(uint8_t dns_no = 0) const;
    IPAddress broadcastIP() const;
    IPAddress networkID() const;
    uint8_t subnetCIDR() const;
    size_t printTo(Print &out) const override;
    esp_netif_t *netif() { return nullptr; }
    int getStatusBits() const;
    int waitStatusBits(int bits, uint32_t timeout_ms) const;

protected:
    Network_Interface_ID _interface_id;
    bool _ipv6 = false;
    virtual size_t printDriverInfo(Print &out) const { return 0; }

public:
    static IPAddress calculateNetworkID(IPAddress ip, IPAddress subnet);
    static IPAddress calculateBroadcast(IPAddress ip, IPAddress subnet);
    static uint8_t calculateSubnetCIDR(IPAddress subnetMask);
};
