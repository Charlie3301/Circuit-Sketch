/* cs_lwip_def.c -- as funcoes de texto do lwIP (lwip/def.c): nada de rede. */
#include <ctype.h>
#include <stdio.h>
#include <string.h>
#include "lwip/def.h"

char *lwip_strnstr(const char *buffer, const char *token, size_t n)
{
    size_t k = strlen(token);
    if (k == 0) return (char *)buffer;
    for (const char *p = buffer; *p && (size_t)(p - buffer) + k <= n; p++)
        if (*p == *token && strncmp(p, token, k) == 0) return (char *)p;
    return NULL;
}

int lwip_strnicmp(const char *a, const char *b, size_t n)
{
    for (; n; n--, a++, b++) {
        int x = tolower((unsigned char)*a), y = tolower((unsigned char)*b);
        if (x != y) return 1;
        if (!x) break;
    }
    return 0;
}

int lwip_stricmp(const char *a, const char *b) { return lwip_strnicmp(a, b, (size_t)-1); }

char *lwip_strnistr(const char *buffer, const char *token, size_t n)
{
    size_t k = strlen(token);
    if (k == 0) return (char *)buffer;
    for (const char *p = buffer; *p && (size_t)(p - buffer) + k <= n; p++)
        if (lwip_strnicmp(p, token, k) == 0) return (char *)p;
    return NULL;
}

void lwip_itoa(char *result, size_t bufsize, int number)
{
    if (bufsize) snprintf(result, bufsize, "%d", number);
}
