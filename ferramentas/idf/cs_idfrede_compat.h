/* cs_idfrede_compat.h -- o que a newlib da ESP32 da de graca e o Windows nao tem,
 * para o codigo de verdade da rede do IDF compilar como esta (entra antes de
 * cada fonte de nativo/idfrede). */
#pragma once
#include <stdbool.h>
#include <stdint.h>
#include <stddef.h>
#include <stdarg.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <ctype.h>
#include <fcntl.h>
#include "cs_posix.h"
#include "esp_heap_caps.h"
#include "esp_attr.h"

#ifndef MIN
#define MIN(a, b) (((a) < (b)) ? (a) : (b))
#endif
#ifndef MAX
#define MAX(a, b) (((a) > (b)) ? (a) : (b))
#endif
#ifndef __containerof
#define __containerof(ptr, type, member) ((type *)((char *)(ptr) - offsetof(type, member)))
#endif
#ifndef F_GETFD
#define F_GETFD 1
#endif
#ifndef F_SETFD
#define F_SETFD 2
#endif

static inline int vasprintf(char **saida, const char *formato, va_list a)
{
    va_list copia;
    va_copy(copia, a);
    int n = vsnprintf(NULL, 0, formato, copia);
    va_end(copia);
    if (n < 0) return -1;
    *saida = (char *)malloc((size_t)n + 1);
    if (!*saida) return -1;
    return vsnprintf(*saida, (size_t)n + 1, formato, a);
}

static inline char *strndup(const char *s, size_t n)
{
    size_t k = strnlen(s, n);
    char *d = (char *)malloc(k + 1);
    if (d) {
        memcpy(d, s, k);
        d[k] = 0;
    }
    return d;
}

static inline char *strcasestr(const char *palheiro, const char *agulha)
{
    size_t n = strlen(agulha);
    for (; *palheiro; palheiro++)
        if (_strnicmp(palheiro, agulha, n) == 0) return (char *)palheiro;
    return n ? NULL : (char *)palheiro;
}
