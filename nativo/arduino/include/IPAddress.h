/* IPAddress.h -- o endereco IPv4 do Arduino (4 bytes, toString, fromString). */
#ifndef IPAddress_h
#define IPAddress_h

#include "Printable.h"
#include "WString.h"
#include <stdint.h>

/* O tipo do endereco (o IPAddress do 3.x tambem guarda IPv6; o simulador
 * so tem IPv4, e o IPv6 vale como "qualquer"). */
enum IPType { IPv4, IPv6 };

class IPAddress : public Printable {
    uint8_t b[4] = {0, 0, 0, 0};
    IPType _tipo = IPv4;

public:
    IPAddress() {}
    explicit IPAddress(IPType type) : _tipo(type) {}
    IPType type() const { return _tipo; }
    uint8_t zone() const { return 0; }
    IPAddress(uint8_t a0, uint8_t a1, uint8_t a2, uint8_t a3) : b{a0, a1, a2, a3} {}
    IPAddress(uint32_t endereco)
    {
        b[0] = endereco & 0xFF;
        b[1] = (endereco >> 8) & 0xFF;
        b[2] = (endereco >> 16) & 0xFF;
        b[3] = (endereco >> 24) & 0xFF;
    }
    IPAddress(const uint8_t *endereco) { memcpy(b, endereco, 4); }
    explicit IPAddress(const char *texto) { fromString(texto); }

    bool fromString(const char *texto)
    {
        unsigned v[4];
        if (!texto || sscanf(texto, "%u.%u.%u.%u", &v[0], &v[1], &v[2], &v[3]) != 4) return false;
        for (int i = 0; i < 4; i++) {
            if (v[i] > 255) return false;
            b[i] = (uint8_t)v[i];
        }
        return true;
    }
    bool fromString(const String &texto) { return fromString(texto.c_str()); }
    String toString() const
    {
        char t[16];
        snprintf(t, sizeof(t), "%u.%u.%u.%u", b[0], b[1], b[2], b[3]);
        return String(t);
    }
    operator uint32_t() const { return (uint32_t)b[0] | ((uint32_t)b[1] << 8) | ((uint32_t)b[2] << 16) | ((uint32_t)b[3] << 24); }
    bool operator==(const IPAddress &o) const { return memcmp(b, o.b, 4) == 0; }
    bool operator!=(const IPAddress &o) const { return !(*this == o); }
    bool operator==(const uint8_t *o) const { return memcmp(b, o, 4) == 0; }
    uint8_t *raw_address() { return b; }
    const uint8_t *raw_address() const { return b; }
    bool isAny() const { return (uint32_t)*this == 0; }
    uint8_t operator[](int i) const { return b[i & 3]; }
    uint8_t &operator[](int i) { return b[i & 3]; }
    size_t printTo(Print &p) const override;
};

#include "Print.h"
inline size_t IPAddress::printTo(Print &p) const { return p.print(toString()); }

static const IPAddress INADDR_NONE(0, 0, 0, 0);
static const IPAddress IN6ADDR_ANY(IPv6);

#endif
