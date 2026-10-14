/* wchar.h do CircuitSketch: o que o newlib da placa tem de caractere largo.
 * As conversoes multibyte sao em UTF-8 (o wchar_t do PC tem 16 bits; o da
 * ESP32, 32 -- o que vai alem do plano basico nao cabe aqui). */
#ifndef _WCHAR_H
#define _WCHAR_H

#include <stddef.h>

#ifdef __cplusplus
extern "C" {
#endif

typedef unsigned int wint_t;
typedef struct { int __count; unsigned int __value; } mbstate_t;

#define WEOF ((wint_t)0xFFFFFFFFu)
#ifndef WCHAR_MIN
#define WCHAR_MIN 0
#endif
#ifndef WCHAR_MAX
#define WCHAR_MAX 0xFFFF
#endif

size_t wcslen(const wchar_t *s);
size_t wcsnlen(const wchar_t *s, size_t n);
int wcscmp(const wchar_t *a, const wchar_t *b);
int wcsncmp(const wchar_t *a, const wchar_t *b, size_t n);
wchar_t *wcscpy(wchar_t *destino, const wchar_t *origem);
wchar_t *wcsncpy(wchar_t *destino, const wchar_t *origem, size_t n);
wchar_t *wcscat(wchar_t *destino, const wchar_t *origem);
wchar_t *wcschr(const wchar_t *s, wchar_t c);
wchar_t *wcsrchr(const wchar_t *s, wchar_t c);
wchar_t *wmemcpy(wchar_t *destino, const wchar_t *origem, size_t n);
wchar_t *wmemset(wchar_t *destino, wchar_t c, size_t n);

size_t mbrtowc(wchar_t *destino, const char *s, size_t n, mbstate_t *ps);
size_t wcrtomb(char *destino, wchar_t c, mbstate_t *ps);
size_t mbsrtowcs(wchar_t *destino, const char **origem, size_t n, mbstate_t *ps);
size_t wcsrtombs(char *destino, const wchar_t **origem, size_t n, mbstate_t *ps);
int mbsinit(const mbstate_t *ps);
wint_t btowc(int c);
int wctob(wint_t c);

#ifdef __cplusplus
}
#endif

#endif
