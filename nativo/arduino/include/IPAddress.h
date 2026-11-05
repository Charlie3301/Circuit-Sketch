/* IPAddress.h -- o endereco do Arduino: IPv4 (4 bytes) ou, como no core 3.x, IPv6 (16 bytes). */
#ifndef IPAddress_h
#define IPAddress_h

#include "Printable.h"
#include "WString.h"
#include <ctype.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

enum IPType { IPv4, IPv6 };

struct ip_addr;
typedef struct ip_addr ip_addr_t;

class IPAddress : public Printable {
    uint8_t b[4] = {0, 0, 0, 0};
    uint8_t b6[16] = {0};
    IPType _tipo = IPv4;
    uint8_t _zona = 0;

    bool lerIpv6(const char *texto)
    {
        uint8_t saida[16] = {0};
        int campos = 0, compactado = -1;
        const char *p = texto;
        if (p[0] == ':' && p[1] == ':') {
            compactado = 0;
            p += 2;
        }
        while (*p && *p != '%') {
            if (campos == 8) return false;
            unsigned v = 0;
            int digitos = 0;
            for (; isxdigit((unsigned char)*p); p++, digitos++) v = v * 16 + (isdigit((unsigned char)*p) ? *p - '0' : (tolower((unsigned char)*p) - 'a' + 10));
            if (!digitos || digitos > 4) return false;
            saida[campos * 2] = (uint8_t)(v >> 8);
            saida[campos * 2 + 1] = (uint8_t)v;
            campos++;
            if (*p == ':') {
                p++;
                if (*p == ':') {
                    if (compactado >= 0) return false;
                    compactado = campos;
                    p++;
                } else if (!*p) return false;
            } else if (*p && *p != '%') return false;
        }
        if (compactado < 0 ? campos != 8 : campos == 8) return false;
        memset(b6, 0, 16);
        if (compactado < 0) memcpy(b6, saida, 16);
        else {
            memcpy(b6, saida, compactado * 2);
            memcpy(b6 + 16 - (campos - compactado) * 2, saida + compactado * 2, (campos - compactado) * 2);
        }
        _zona = *p == '%' ? (uint8_t)atoi(p + 1) : 0;
        _tipo = IPv6;
        return true;
    }

public:
    IPAddress() {}
    explicit IPAddress(IPType type) : _tipo(type) {}
    IPAddress(IPType type, const uint8_t *endereco, uint8_t z = 0) : _tipo(type), _zona(z)
    {
        if (type == IPv6) memcpy(b6, endereco, 16);
        else memcpy(b, endereco, 4);
    }
    IPAddress(uint8_t o1, uint8_t o2, uint8_t o3, uint8_t o4, uint8_t o5, uint8_t o6, uint8_t o7, uint8_t o8, uint8_t o9, uint8_t o10, uint8_t o11,
              uint8_t o12, uint8_t o13, uint8_t o14, uint8_t o15, uint8_t o16, uint8_t z = 0)
        : b6{o1, o2, o3, o4, o5, o6, o7, o8, o9, o10, o11, o12, o13, o14, o15, o16}, _tipo(IPv6), _zona(z) {}
    IPType type() const { return _tipo; }
    uint8_t zone() const { return _tipo == IPv6 ? _zona : 0; }
    /* O endereco do lwIP, de ida e de volta (em cs_rede_ip.cpp, que conhece o lwIP). */
    void to_ip_addr_t(ip_addr_t *addr) const;
    IPAddress &from_ip_addr_t(const ip_addr_t *addr);
    /* O tipo do IPv6: 0 desconhecido, 1 global, 2 link local, 3 site local, 4 unique local, 5 loopback. */
    int addr_type() const;
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
        if (texto && strchr(texto, ':')) return lerIpv6(texto);
        if (!texto || sscanf(texto, "%u.%u.%u.%u", &v[0], &v[1], &v[2], &v[3]) != 4) return false;
        for (int i = 0; i < 4; i++) {
            if (v[i] > 255) return false;
            b[i] = (uint8_t)v[i];
        }
        _tipo = IPv4;
        return true;
    }
    bool fromString(const String &texto) { return fromString(texto.c_str()); }
    String toString(bool comZona = false) const
    {
        if (_tipo == IPv6) {
            /* A forma canonica (RFC 5952): a maior sequencia de zeros vira "::", em minusculas. */
            int melhor = -1, tamanho = 1;
            for (int f = 0; f < 8;) {
                int n = 0;
                while (f + n < 8 && !b6[(f + n) * 2] && !b6[(f + n) * 2 + 1]) n++;
                if (n > tamanho) {
                    melhor = f;
                    tamanho = n;
                }
                f += n ? n : 1;
            }
            char t[48];
            int k = 0;
            for (int f = 0; f < 8; f++) {
                if (f == melhor) {
                    k += snprintf(t + k, sizeof(t) - k, f == 0 ? "::" : ":");
                    f += tamanho - 1;
                    continue;
                }
                k += snprintf(t + k, sizeof(t) - k, "%x%s", (b6[f * 2] << 8) | b6[f * 2 + 1], f < 7 ? ":" : "");
            }
            if (comZona && _zona) snprintf(t + k, sizeof(t) - k, "%%%u", _zona);
            return String(t);
        }
        char t[16];
        snprintf(t, sizeof(t), "%u.%u.%u.%u", b[0], b[1], b[2], b[3]);
        return String(t);
    }
    operator uint32_t() const { return (uint32_t)b[0] | ((uint32_t)b[1] << 8) | ((uint32_t)b[2] << 16) | ((uint32_t)b[3] << 24); }
    bool operator==(const IPAddress &o) const { return _tipo == o._tipo && (_tipo == IPv6 ? memcmp(b6, o.b6, 16) == 0 && _zona == o._zona : memcmp(b, o.b, 4) == 0); }
    bool operator!=(const IPAddress &o) const { return !(*this == o); }
    bool operator==(const uint8_t *o) const { return memcmp(b, o, 4) == 0; }
    uint8_t *raw_address() { return _tipo == IPv6 ? b6 : b; }
    const uint8_t *raw_address() const { return _tipo == IPv6 ? b6 : b; }
    bool isAny() const
    {
        if (_tipo == IPv4) return (uint32_t)*this == 0;
        for (uint8_t x : b6)
            if (x) return false;
        return true;
    }
    uint8_t operator[](int i) const { return _tipo == IPv6 ? b6[i & 15] : b[i & 3]; }
    uint8_t &operator[](int i) { return _tipo == IPv6 ? b6[i & 15] : b[i & 3]; }
    size_t printTo(Print &p) const override;
};

#include "Print.h"
inline size_t IPAddress::printTo(Print &p) const { return p.print(toString()); }

/* O lwIP define um INADDR_NONE numerico; se ele veio antes, vale este aqui. */
#undef INADDR_NONE
static const IPAddress INADDR_NONE(0, 0, 0, 0);
static const IPAddress IN6ADDR_ANY(IPv6);

#endif
