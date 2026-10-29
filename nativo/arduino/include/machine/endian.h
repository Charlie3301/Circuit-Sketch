/* machine/endian.h -- o da newlib (a libc da ESP32): a ordem dos bytes e as
 * trocas. A ESP32 e o PC sao little-endian, como aqui. */
#ifndef __MACHINE_ENDIAN_H__
#define __MACHINE_ENDIAN_H__

#include <stdint.h>

#ifndef _LITTLE_ENDIAN
#define _LITTLE_ENDIAN 1234
#endif
#ifndef _BIG_ENDIAN
#define _BIG_ENDIAN 4321
#endif
#ifndef _PDP_ENDIAN
#define _PDP_ENDIAN 3412
#endif
#ifndef _BYTE_ORDER
#define _BYTE_ORDER _LITTLE_ENDIAN
#endif
#ifndef LITTLE_ENDIAN
#define LITTLE_ENDIAN _LITTLE_ENDIAN
#endif
#ifndef BIG_ENDIAN
#define BIG_ENDIAN _BIG_ENDIAN
#endif
#ifndef PDP_ENDIAN
#define PDP_ENDIAN _PDP_ENDIAN
#endif
#ifndef BYTE_ORDER
#define BYTE_ORDER _BYTE_ORDER
#endif

#define __bswap16(x) __builtin_bswap16(x)
#define __bswap32(x) __builtin_bswap32(x)
#define __bswap64(x) __builtin_bswap64(x)

#define __htonl(x) __bswap32(x)
#define __htons(x) __bswap16(x)
#define __ntohl(x) __bswap32(x)
#define __ntohs(x) __bswap16(x)

#endif
