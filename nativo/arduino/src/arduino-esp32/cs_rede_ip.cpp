/* cs_rede_ip.cpp -- o IPAddress com o ip_addr_t do lwIP (o cabecalho nao precisa conhecer o lwIP). */
#include "IPAddress.h"
#include "lwip/ip_addr.h"

void IPAddress::to_ip_addr_t(ip_addr_t *addr) const
{
    if (_tipo == IPv6) {
        IP_SET_TYPE_VAL(*addr, IPADDR_TYPE_V6);
        memcpy(ip_2_ip6(addr)->addr, b6, 16);
        ip_2_ip6(addr)->zone = _zona;
    } else {
        IP_SET_TYPE_VAL(*addr, IPADDR_TYPE_V4);
        memcpy(&ip_2_ip4(addr)->addr, b, 4);
    }
}

IPAddress &IPAddress::from_ip_addr_t(const ip_addr_t *addr)
{
    if (IP_IS_V6(addr)) {
        _tipo = IPv6;
        memcpy(b6, ip_2_ip6(addr)->addr, 16);
        _zona = ip_2_ip6(addr)->zone;
    } else {
        _tipo = IPv4;
        memcpy(b, &ip_2_ip4(addr)->addr, 4);
    }
    return *this;
}

int IPAddress::addr_type() const
{
    if (_tipo != IPv6) return 0;
    if (b6[0] == 0xFE && (b6[1] & 0xC0) == 0x80) return 2;
    if (b6[0] == 0xFE && (b6[1] & 0xC0) == 0xC0) return 3;
    if ((b6[0] & 0xFE) == 0xFC) return 4;
    static const uint8_t volta[16] = {0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1};
    if (memcmp(b6, volta, 16) == 0) return 5;
    return (b6[0] & 0xE0) == 0x20 ? 1 : 0;
}
