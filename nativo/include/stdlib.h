#ifndef _STDLIB_H
#define _STDLIB_H

#include <stddef.h>

#define EXIT_SUCCESS 0
#define EXIT_FAILURE 1
#define RAND_MAX 2147483647
#define MB_CUR_MAX 1

typedef struct { int quot, rem; } div_t;
typedef struct { long quot, rem; } ldiv_t;
typedef struct { long long quot, rem; } lldiv_t;

void *malloc(size_t n);
void *calloc(size_t quantos, size_t tamanho);
void *realloc(void *p, size_t n);
void free(void *p);
void *aligned_alloc(size_t alinhamento, size_t n);
int posix_memalign(void **p, size_t alinhamento, size_t n);

int mblen(const char *s, size_t n);
int mbtowc(__WCHAR_TYPE__ *destino, const char *s, size_t n);
int wctomb(char *s, __WCHAR_TYPE__ c);
size_t mbstowcs(__WCHAR_TYPE__ *destino, const char *s, size_t n);
size_t wcstombs(char *destino, const __WCHAR_TYPE__ *s, size_t n);

int atoi(const char *s);
long atol(const char *s);
long long atoll(const char *s);
double atof(const char *s);
long strtol(const char *s, char **fim, int base);
unsigned long strtoul(const char *s, char **fim, int base);
long long strtoll(const char *s, char **fim, int base);
unsigned long long strtoull(const char *s, char **fim, int base);
double strtod(const char *s, char **fim);
float strtof(const char *s, char **fim);
char *itoa(int valor, char *s, int base);
char *ltoa(long valor, char *s, int base);
char *utoa(unsigned valor, char *s, int base);

int abs(int x);
long labs(long x);
long long llabs(long long x);
div_t div(int a, int b);
ldiv_t ldiv(long a, long b);
lldiv_t lldiv(long long a, long long b);

int rand(void);
void srand(unsigned semente);
long random(void);
void srandom(unsigned semente);

void qsort(void *base, size_t quantos, size_t tamanho, int (*comparar)(const void *, const void *));
void *bsearch(const void *chave, const void *base, size_t quantos, size_t tamanho,
              int (*comparar)(const void *, const void *));

void exit(int codigo) __attribute__((noreturn));
void _exit(int codigo) __attribute__((noreturn));
void abort(void) __attribute__((noreturn));
int (atexit)(void (*funcao)(void));
char *getenv(const char *nome);
int setenv(const char *nome, const char *valor, int substituir);
int system(const char *comando);

#endif
