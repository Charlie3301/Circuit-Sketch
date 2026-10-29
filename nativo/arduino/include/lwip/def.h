/* lwip/def.h -- as macros basicas do lwIP (a pilha TCP/IP da ESP32): a ordem
 * dos bytes na rede (htons, ntohl...) e o LWIP_MAKEU32. A ESP32 e o PC sao
 * little-endian e a rede e big-endian, como aqui. */
#ifndef LWIP_HDR_DEF_H
#define LWIP_HDR_DEF_H

#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

#define LWIP_MAX(x, y) (((x) > (y)) ? (x) : (y))
#define LWIP_MIN(x, y) (((x) < (y)) ? (x) : (y))
#define LWIP_ARRAYSIZE(x) (sizeof(x) / sizeof((x)[0]))
#define LWIP_MAKEU32(a, b, c, d) (((uint32_t)((a) & 0xff) << 24) | ((uint32_t)((b) & 0xff) << 16) | ((uint32_t)((c) & 0xff) << 8) | (uint32_t)((d) & 0xff))

static inline uint16_t lwip_htons(uint16_t x) { return (uint16_t)__builtin_bswap16(x); }
static inline uint32_t lwip_htonl(uint32_t x) { return __builtin_bswap32(x); }
#define lwip_ntohs(x) lwip_htons(x)
#define lwip_ntohl(x) lwip_htonl(x)
#define PP_HTONS(x) ((uint16_t)((((x) & 0x00ffUL) << 8) | (((x) & 0xff00UL) >> 8)))
#define PP_NTOHS(x) PP_HTONS(x)
#define PP_HTONL(x) ((((x) & 0x000000ffUL) << 24) | (((x) & 0x0000ff00UL) << 8) | (((x) & 0x00ff0000UL) >> 8) | (((x) & 0xff000000UL) >> 24))
#define PP_NTOHL(x) PP_HTONL(x)

#ifndef htons
#define htons(x) lwip_htons(x)
#endif
#ifndef ntohs
#define ntohs(x) lwip_ntohs(x)
#endif
#ifndef htonl
#define htonl(x) lwip_htonl(x)
#endif
#ifndef ntohl
#define ntohl(x) lwip_ntohl(x)
#endif

#ifdef __cplusplus
}
#endif

#endif
