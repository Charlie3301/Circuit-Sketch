/* cs_posix.h -- o que a newlib da ESP32 da e o Windows nao tem (o bzero, o
 * bcopy, o bcmp, o strlcpy, o strlcat, o asprintf): o codigo do core, do IDF e
 * dos sketches usa. */
#ifndef CS_POSIX_H
#define CS_POSIX_H

#include <string.h>
#include <stdio.h>
#include <stdarg.h>
#include <stdlib.h>

#ifdef __cplusplus
extern "C" {
#endif
static inline void bzero(void *s, size_t n) { memset(s, 0, n); }
static inline void bcopy(const void *src, void *dst, size_t n) { memmove(dst, src, n); }
static inline int bcmp(const void *a, const void *b, size_t n) { return memcmp(a, b, n); }

/* O strlcpy e o strlcat moram no core (src/cs_stdio.cpp). */
size_t strlcpy(char *dst, const char *src, size_t size);
size_t strlcat(char *dst, const char *src, size_t size);
static inline int cs_asprintf(char **saida, const char *formato, ...)
{
    va_list a, b;
    va_start(a, formato);
    va_copy(b, a);
    int n = vsnprintf(NULL, 0, formato, a);
    va_end(a);
    *saida = NULL;
    if (n >= 0 && (*saida = (char *)malloc((size_t)n + 1)) != NULL) vsnprintf(*saida, (size_t)n + 1, formato, b);
    else n = -1;
    va_end(b);
    return n;
}
#define asprintf cs_asprintf
int strerror_r(int erro, char *saida, size_t n);

/* Os u_int*_t do sys/types.h da newlib (o Windows nao tem). */
typedef unsigned char u_int8_t;
typedef unsigned short u_int16_t;
typedef unsigned int u_int32_t;
typedef unsigned long long u_int64_t;
#ifdef __cplusplus
}
#endif

#endif
