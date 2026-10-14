/* cs_texto.c -- string.h, ctype.h e o resto da stdlib que nao e memoria
 * nem arquivo: conversoes, ordenacao, numeros aleatorios. */
#include <stdint.h>
#include <stddef.h>
#include <string.h>
#include <strings.h>
#include <ctype.h>
#include <stdlib.h>
#include <limits.h>
#include <errno.h>
#include <locale.h>
#include <signal.h>
#include <inttypes.h>
#include "cs_host.h"
#include "cs_interno.h"

/* ---------------------------------------------------------------------
 * memoria em bloco: "rep movsb" e "rep stosb", que o processador faz
 * rapido -- o DOOM copia e limpa a tela o tempo todo. */
void *memcpy(void *destino, const void *origem, size_t n)
{
    void *r = destino;
    __asm__ __volatile__("rep movsb" : "+D"(destino), "+S"(origem), "+c"(n) : : "memory");
    return r;
}

void *memmove(void *destino, const void *origem, size_t n)
{
    uint8_t *d = (uint8_t *)destino;
    const uint8_t *s = (const uint8_t *)origem;
    if (d == s || n == 0) return destino;
    if (d < s || d >= s + n) return memcpy(destino, origem, n);

    uint8_t *dd = d + n - 1;
    const uint8_t *ss = s + n - 1;
    __asm__ __volatile__("std\n\trep movsb\n\tcld" : "+D"(dd), "+S"(ss), "+c"(n) : : "memory");
    return destino;
}

void *memset(void *destino, int c, size_t n)
{
    void *r = destino;
    __asm__ __volatile__("rep stosb" : "+D"(destino), "+c"(n) : "a"(c) : "memory");
    return r;
}

int memcmp(const void *a, const void *b, size_t n)
{
    const uint8_t *x = (const uint8_t *)a, *y = (const uint8_t *)b;
    for (size_t i = 0; i < n; i++)
        if (x[i] != y[i]) return x[i] < y[i] ? -1 : 1;
    return 0;
}

void *memchr(const void *s, int c, size_t n)
{
    const uint8_t *p = (const uint8_t *)s;
    for (size_t i = 0; i < n; i++)
        if (p[i] == (uint8_t)c) return (void *)(p + i);
    return NULL;
}

void *memrchr(const void *s, int c, size_t n)
{
    const uint8_t *p = (const uint8_t *)s;
    while (n--)
        if (p[n] == (uint8_t)c) return (void *)(p + n);
    return NULL;
}

void *memmem(const void *palheiro, size_t n1, const void *agulha, size_t n2)
{
    const uint8_t *h = (const uint8_t *)palheiro;
    if (n2 == 0) return (void *)h;
    for (size_t i = 0; i + n2 <= n1; i++)
        if (h[i] == *(const uint8_t *)agulha && memcmp(h + i, agulha, n2) == 0) return (void *)(h + i);
    return NULL;
}

void bzero(void *s, size_t n) { memset(s, 0, n); }
void bcopy(const void *origem, void *destino, size_t n) { memmove(destino, origem, n); }
int bcmp(const void *a, const void *b, size_t n) { return memcmp(a, b, n); }

/* ---------------------------------------------------------------------
 * string.h */
size_t strlen(const char *s)
{
    const char *p = s;
    while (*p) p++;
    return (size_t)(p - s);
}

size_t strnlen(const char *s, size_t n)
{
    size_t i = 0;
    while (i < n && s[i]) i++;
    return i;
}

char *strcpy(char *destino, const char *origem)
{
    char *d = destino;
    while ((*d++ = *origem++)) {}
    return destino;
}

char *stpcpy(char *destino, const char *origem)
{
    while ((*destino = *origem++)) destino++;
    return destino;
}

char *strncpy(char *destino, const char *origem, size_t n)
{
    size_t i = 0;
    for (; i < n && origem[i]; i++) destino[i] = origem[i];
    for (; i < n; i++) destino[i] = 0;
    return destino;
}

size_t strlcpy(char *destino, const char *origem, size_t n)
{
    size_t tam = strlen(origem);
    if (n) {
        size_t k = tam >= n ? n - 1 : tam;
        memcpy(destino, origem, k);
        destino[k] = 0;
    }
    return tam;
}

size_t strlcat(char *destino, const char *origem, size_t n)
{
    size_t d = strnlen(destino, n);
    if (d == n) return n + strlen(origem);
    return d + strlcpy(destino + d, origem, n - d);
}

char *strcat(char *destino, const char *origem)
{
    strcpy(destino + strlen(destino), origem);
    return destino;
}

char *strncat(char *destino, const char *origem, size_t n)
{
    char *d = destino + strlen(destino);
    size_t i = 0;
    for (; i < n && origem[i]; i++) d[i] = origem[i];
    d[i] = 0;
    return destino;
}

int strcmp(const char *a, const char *b)
{
    while (*a && *a == *b) { a++; b++; }
    return (unsigned char)*a - (unsigned char)*b;
}

int strncmp(const char *a, const char *b, size_t n)
{
    for (size_t i = 0; i < n; i++) {
        if (a[i] != b[i] || !a[i]) return (unsigned char)a[i] - (unsigned char)b[i];
    }
    return 0;
}

int strcoll(const char *a, const char *b) { return strcmp(a, b); }

size_t strxfrm(char *destino, const char *origem, size_t n)
{
    return strlcpy(destino, origem, n);
}

char *strchr(const char *s, int c)
{
    for (;; s++) {
        if (*s == (char)c) return (char *)s;
        if (!*s) return NULL;
    }
}

char *strrchr(const char *s, int c)
{
    const char *achado = NULL;
    for (;; s++) {
        if (*s == (char)c) achado = s;
        if (!*s) return (char *)achado;
    }
}

char *index(const char *s, int c) { return strchr(s, c); }
char *rindex(const char *s, int c) { return strrchr(s, c); }

char *strstr(const char *palheiro, const char *agulha)
{
    size_t n = strlen(agulha);
    if (!n) return (char *)palheiro;
    for (; *palheiro; palheiro++)
        if (*palheiro == *agulha && strncmp(palheiro, agulha, n) == 0) return (char *)palheiro;
    return NULL;
}

char *strcasestr(const char *palheiro, const char *agulha)
{
    size_t n = strlen(agulha);
    if (!n) return (char *)palheiro;
    for (; *palheiro; palheiro++)
        if (strncasecmp(palheiro, agulha, n) == 0) return (char *)palheiro;
    return NULL;
}

size_t strspn(const char *s, const char *aceitos)
{
    size_t i = 0;
    while (s[i] && strchr(aceitos, s[i])) i++;
    return i;
}

size_t strcspn(const char *s, const char *recusados)
{
    size_t i = 0;
    while (s[i] && !strchr(recusados, s[i])) i++;
    return i;
}

char *strpbrk(const char *s, const char *aceitos)
{
    for (; *s; s++)
        if (strchr(aceitos, *s)) return (char *)s;
    return NULL;
}

char *strtok_r(char *s, const char *separadores, char **resto)
{
    if (!s) s = *resto;
    if (!s) return NULL;
    s += strspn(s, separadores);
    if (!*s) {
        *resto = NULL;
        return NULL;
    }
    char *fim = s + strcspn(s, separadores);
    if (*fim) {
        *fim = 0;
        *resto = fim + 1;
    } else {
        *resto = NULL;
    }
    return s;
}

static char *resto_do_strtok;

char *strtok(char *s, const char *separadores)
{
    return strtok_r(s, separadores, &resto_do_strtok);
}

char *strsep(char **s, const char *separadores)
{
    char *inicio = *s;
    if (!inicio) return NULL;
    char *fim = inicio + strcspn(inicio, separadores);
    if (*fim) {
        *fim = 0;
        *s = fim + 1;
    } else {
        *s = NULL;
    }
    return inicio;
}

char *strdup(const char *s)
{
    size_t n = strlen(s) + 1;
    char *p = (char *)malloc(n);
    if (p) memcpy(p, s, n);
    return p;
}

char *strndup(const char *s, size_t n)
{
    size_t k = strnlen(s, n);
    char *p = (char *)malloc(k + 1);
    if (p) {
        memcpy(p, s, k);
        p[k] = 0;
    }
    return p;
}

char *strlwr(char *s)
{
    for (char *p = s; *p; p++) *p = (char)tolower((unsigned char)*p);
    return s;
}

char *strupr(char *s)
{
    for (char *p = s; *p; p++) *p = (char)toupper((unsigned char)*p);
    return s;
}

int strcasecmp(const char *a, const char *b)
{
    for (;; a++, b++) {
        int x = tolower((unsigned char)*a), y = tolower((unsigned char)*b);
        if (x != y || !x) return x - y;
    }
}

int strncasecmp(const char *a, const char *b, size_t n)
{
    for (size_t i = 0; i < n; i++) {
        int x = tolower((unsigned char)a[i]), y = tolower((unsigned char)b[i]);
        if (x != y || !x) return x - y;
    }
    return 0;
}

char *strerror(int erro)
{
    switch (erro) {
    case 0: return "Success";
    case EPERM: return "Not owner";
    case ENOENT: return "No such file or directory";
    case EINTR: return "Interrupted system call";
    case EIO: return "I/O error";
    case EBADF: return "Bad file number";
    case EAGAIN: return "No more processes";
    case ENOMEM: return "Not enough space";
    case EACCES: return "Permission denied";
    case EFAULT: return "Bad address";
    case EBUSY: return "Device or resource busy";
    case EEXIST: return "File exists";
    case ENODEV: return "No such device";
    case ENOTDIR: return "Not a directory";
    case EISDIR: return "Is a directory";
    case EINVAL: return "Invalid argument";
    case ENFILE: return "Too many open files in system";
    case EMFILE: return "File descriptor value too large";
    case ENOSPC: return "No space left on device";
    case ESPIPE: return "Illegal seek";
    case EROFS: return "Read-only file system";
    case EPIPE: return "Broken pipe";
    case EDOM: return "Mathematics argument out of domain of function";
    case ERANGE: return "Result too large";
    case ENOSYS: return "Function not implemented";
    case ETIMEDOUT: return "Connection timed out";
    }
    return "Unknown error";
}

/* ---------------------------------------------------------------------
 * ctype.h (so ASCII, como o locale "C" da newlib) */
int isalnum(int c) { return isalpha(c) || isdigit(c); }
int isalpha(int c) { return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z'); }
int isblank(int c) { return c == ' ' || c == '\t'; }
int iscntrl(int c) { return (c >= 0 && c < 32) || c == 127; }
int isdigit(int c) { return c >= '0' && c <= '9'; }
int isgraph(int c) { return c > 32 && c < 127; }
int islower(int c) { return c >= 'a' && c <= 'z'; }
int isprint(int c) { return c >= 32 && c < 127; }
int ispunct(int c) { return isgraph(c) && !isalnum(c); }
int isspace(int c) { return c == ' ' || (c >= '\t' && c <= '\r'); }
int isupper(int c) { return c >= 'A' && c <= 'Z'; }
int isxdigit(int c) { return isdigit(c) || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'); }
int isascii(int c) { return c >= 0 && c < 128; }
int toascii(int c) { return c & 0x7f; }
int tolower(int c) { return isupper(c) ? c + 32 : c; }
int toupper(int c) { return islower(c) ? c - 32 : c; }

/* ---------------------------------------------------------------------
 * numeros em texto */
static int valor_do_digito(int c)
{
    if (c >= '0' && c <= '9') return c - '0';
    if (c >= 'a' && c <= 'z') return c - 'a' + 10;
    if (c >= 'A' && c <= 'Z') return c - 'A' + 10;
    return 99;
}

/* O miolo do strtol/strtoul: le sem sinal, avisa se passou do teto. */
static unsigned long long ler_inteiro(const char *s, char **fim, int base, int *negativo, int *estourou,
                                      unsigned long long teto)
{
    const char *p = s;
    *negativo = 0;
    *estourou = 0;
    while (isspace((unsigned char)*p)) p++;
    if (*p == '+' || *p == '-') {
        *negativo = *p == '-';
        p++;
    }
    if ((base == 0 || base == 16) && p[0] == '0' && (p[1] == 'x' || p[1] == 'X') && isxdigit((unsigned char)p[2])) {
        p += 2;
        base = 16;
    } else if (base == 0 && p[0] == '0') {
        base = 8;
    } else if (base == 0) {
        base = 10;
    }
    if (base < 2 || base > 36) {
        if (fim) *fim = (char *)s;
        errno = EINVAL;
        return 0;
    }

    unsigned long long v = 0;
    const char *inicio = p;
    for (;; p++) {
        int d = valor_do_digito((unsigned char)*p);
        if (d >= base) break;
        if (v > (teto - (unsigned long long)d) / (unsigned long long)base) *estourou = 1;
        else v = v * (unsigned long long)base + (unsigned long long)d;
    }
    if (fim) *fim = (char *)(p == inicio ? s : p);
    return v;
}

long long strtoll(const char *s, char **fim, int base)
{
    int neg, est;
    unsigned long long v = ler_inteiro(s, fim, base, &neg, &est, ULLONG_MAX);
    if (est || (!neg && v > (unsigned long long)LLONG_MAX) || (neg && v > (unsigned long long)LLONG_MAX + 1)) {
        errno = ERANGE;
        return neg ? LLONG_MIN : LLONG_MAX;
    }
    return neg ? (long long)(0 - v) : (long long)v;
}

unsigned long long strtoull(const char *s, char **fim, int base)
{
    int neg, est;
    unsigned long long v = ler_inteiro(s, fim, base, &neg, &est, ULLONG_MAX);
    if (est) {
        errno = ERANGE;
        return ULLONG_MAX;
    }
    return neg ? 0 - v : v;
}

long strtol(const char *s, char **fim, int base)
{
    int neg, est;
    unsigned long long v = ler_inteiro(s, fim, base, &neg, &est, ULLONG_MAX);
    if (est || (!neg && v > (unsigned long long)LONG_MAX) || (neg && v > (unsigned long long)LONG_MAX + 1)) {
        errno = ERANGE;
        return neg ? LONG_MIN : LONG_MAX;
    }
    return neg ? (long)(0 - v) : (long)v;
}

unsigned long strtoul(const char *s, char **fim, int base)
{
    int neg, est;
    unsigned long long v = ler_inteiro(s, fim, base, &neg, &est, ULLONG_MAX);
    if (est || v > ULONG_MAX) {
        errno = ERANGE;
        return ULONG_MAX;
    }
    return neg ? (unsigned long)(0 - v) : (unsigned long)v;
}

intmax_t strtoimax(const char *s, char **fim, int base) { return strtoll(s, fim, base); }
uintmax_t strtoumax(const char *s, char **fim, int base) { return strtoull(s, fim, base); }
intmax_t imaxabs(intmax_t x) { return x < 0 ? -x : x; }

int atoi(const char *s) { return (int)strtol(s, NULL, 10); }
long atol(const char *s) { return strtol(s, NULL, 10); }
long long atoll(const char *s) { return strtoll(s, NULL, 10); }

/* O double sai do simulador: a conversao correta (arredondada como a
 * newlib faz) e trabalho do .NET, que ja a tem. */
double strtod(const char *s, char **fim)
{
    int consumidos = 0;
    double v = cs->texto_real(s, &consumidos);
    if (fim) *fim = (char *)s + consumidos;
    return v;
}

float strtof(const char *s, char **fim) { return (float)strtod(s, fim); }
long double strtold(const char *s, char **fim) { return strtod(s, fim); }
double atof(const char *s) { return strtod(s, NULL); }

static char *escrever_na_base(unsigned long long v, int negativo, char *s, int base)
{
    char tmp[72];
    int n = 0;
    if (base < 2 || base > 36) base = 10;
    do {
        int d = (int)(v % (unsigned)base);
        tmp[n++] = (char)(d < 10 ? '0' + d : 'a' + d - 10);
        v /= (unsigned)base;
    } while (v);
    char *p = s;
    if (negativo) *p++ = '-';
    while (n) *p++ = tmp[--n];
    *p = 0;
    return s;
}

char *itoa(int valor, char *s, int base)
{
    if (base == 10 && valor < 0) return escrever_na_base((unsigned long long)(-(long long)valor), 1, s, base);
    return escrever_na_base((unsigned int)valor, 0, s, base);
}

char *ltoa(long valor, char *s, int base)
{
    if (base == 10 && valor < 0) return escrever_na_base((unsigned long long)(-(long long)valor), 1, s, base);
    return escrever_na_base((unsigned long)valor, 0, s, base);
}

char *utoa(unsigned valor, char *s, int base) { return escrever_na_base(valor, 0, s, base); }
char *ultoa(unsigned long valor, char *s, int base) { return escrever_na_base(valor, 0, s, base); }

int abs(int x) { return x < 0 ? -x : x; }
long labs(long x) { return x < 0 ? -x : x; }
long long llabs(long long x) { return x < 0 ? -x : x; }

div_t div(int a, int b)
{
    div_t r;
    r.quot = a / b;
    r.rem = a % b;
    return r;
}

ldiv_t ldiv(long a, long b)
{
    ldiv_t r;
    r.quot = a / b;
    r.rem = a % b;
    return r;
}

lldiv_t lldiv(long long a, long long b)
{
    lldiv_t r;
    r.quot = a / b;
    r.rem = a % b;
    return r;
}

/* ---------------------------------------------------------------------
 * rand: o mesmo gerador da newlib (congruencial de 64 bits), entao a
 * mesma semente da a mesma sequencia que na placa. */
static unsigned long long semente_do_rand = 1;

void srand(unsigned semente) { semente_do_rand = semente; }

int rand(void)
{
    semente_do_rand = semente_do_rand * 6364136223846793005ULL + 1;
    return (int)((semente_do_rand >> 32) & RAND_MAX);
}

int rand_r(unsigned *semente)
{
    unsigned long long s = *semente;
    s = s * 1103515245ULL + 12345;
    *semente = (unsigned)s;
    return (int)((s >> 16) & RAND_MAX);
}

long random(void) { return rand(); }
void srandom(unsigned semente) { srand(semente); }

/* ---------------------------------------------------------------------
 * qsort (quicksort com mediana de tres e insercao nos pedacos pequenos)
 * e bsearch */
static void trocar_bytes(uint8_t *a, uint8_t *b, size_t n)
{
    while (n--) {
        uint8_t t = *a;
        *a++ = *b;
        *b++ = t;
    }
}

static void ordenar(uint8_t *base, size_t quantos, size_t tam, int (*cmp)(const void *, const void *))
{
    while (quantos > 12) {
        uint8_t *meio = base + (quantos / 2) * tam;
        uint8_t *ultimo = base + (quantos - 1) * tam;
        if (cmp(meio, base) < 0) trocar_bytes(meio, base, tam);
        if (cmp(ultimo, base) < 0) trocar_bytes(ultimo, base, tam);
        if (cmp(ultimo, meio) < 0) trocar_bytes(ultimo, meio, tam);
        trocar_bytes(meio, ultimo - tam, tam);
        uint8_t *pivo = ultimo - tam;

        uint8_t *i = base, *j = pivo;
        for (;;) {
            do i += tam; while (cmp(i, pivo) < 0);
            do j -= tam; while (j > base && cmp(j, pivo) > 0);
            if (i >= j) break;
            trocar_bytes(i, j, tam);
        }
        trocar_bytes(i, pivo, tam);

        size_t esquerda = (size_t)(i - base) / tam;
        size_t direita = quantos - esquerda - 1;
        if (esquerda < direita) {
            ordenar(base, esquerda, tam, cmp);
            base = i + tam;
            quantos = direita;
        } else {
            ordenar(i + tam, direita, tam, cmp);
            quantos = esquerda;
        }
    }
    for (size_t k = 1; k < quantos; k++)
        for (uint8_t *p = base + k * tam; p > base && cmp(p - tam, p) > 0; p -= tam)
            trocar_bytes(p - tam, p, tam);
}

void qsort(void *base, size_t quantos, size_t tamanho, int (*comparar)(const void *, const void *))
{
    if (quantos > 1 && tamanho) ordenar((uint8_t *)base, quantos, tamanho, comparar);
}

void *bsearch(const void *chave, const void *base, size_t quantos, size_t tamanho,
              int (*comparar)(const void *, const void *))
{
    const uint8_t *b = (const uint8_t *)base;
    size_t lo = 0, hi = quantos;
    while (lo < hi) {
        size_t meio = (lo + hi) / 2;
        int c = comparar(chave, b + meio * tamanho);
        if (c == 0) return (void *)(b + meio * tamanho);
        if (c < 0) hi = meio;
        else lo = meio + 1;
    }
    return NULL;
}

/* ---------------------------------------------------------------------
 * ambiente: a ESP32 nao tem variaveis de ambiente de verdade -- so as
 * que o proprio programa poe (o TZ do relogio, por exemplo). */
typedef struct variavel { char *nome; char *valor; struct variavel *proxima; } variavel;
static variavel *ambiente;
static cs_giro trava_do_ambiente;

char *getenv(const char *nome)
{
    char *achado = NULL;
    cs_girar(&trava_do_ambiente);
    for (variavel *v = ambiente; v; v = v->proxima)
        if (strcmp(v->nome, nome) == 0) { achado = v->valor; break; }
    cs_soltar(&trava_do_ambiente);
    return achado;
}

int setenv(const char *nome, const char *valor, int substituir)
{
    cs_girar(&trava_do_ambiente);
    for (variavel *v = ambiente; v; v = v->proxima)
        if (strcmp(v->nome, nome) == 0) {
            if (substituir) {
                free(v->valor);
                v->valor = strdup(valor);
            }
            cs_soltar(&trava_do_ambiente);
            return 0;
        }
    variavel *v = (variavel *)malloc(sizeof(variavel));
    if (v) {
        v->nome = strdup(nome);
        v->valor = strdup(valor);
        v->proxima = ambiente;
        ambiente = v;
    }
    cs_soltar(&trava_do_ambiente);
    return v ? 0 : -1;
}

int unsetenv(const char *nome)
{
    cs_girar(&trava_do_ambiente);
    for (variavel **p = &ambiente; *p; p = &(*p)->proxima)
        if (strcmp((*p)->nome, nome) == 0) {
            variavel *v = *p;
            *p = v->proxima;
            free(v->nome);
            free(v->valor);
            free(v);
            break;
        }
    cs_soltar(&trava_do_ambiente);
    return 0;
}

int system(const char *comando)
{
    (void)comando;
    return -1;
}

/* ---------------------------------------------------------------------
 * locale e sinais: so o "C", e sinal nenhum chega */
static struct lconv convencoes = { "." };

char *setlocale(int categoria, const char *local)
{
    (void)categoria;
    if (!local || !*local || strcmp(local, "C") == 0 || strcmp(local, "POSIX") == 0) return "C";
    return NULL;
}

struct lconv *localeconv(void)
{
    return &convencoes;
}

sighandler_t signal(int sinal, sighandler_t tratador)
{
    (void)sinal;
    (void)tratador;
    return SIG_DFL;
}

int raise(int sinal)
{
    if (sinal == SIGABRT) abort();
    return 0;
}

/* ---------------------------------------------------------------------
 * caractere largo (wchar.h e o mbstowcs da stdlib): em UTF-8. */
#include <wchar.h>

size_t wcslen(const wchar_t *s) { size_t n = 0; while (s[n]) n++; return n; }
size_t wcsnlen(const wchar_t *s, size_t m) { size_t n = 0; while (n < m && s[n]) n++; return n; }
int wcscmp(const wchar_t *a, const wchar_t *b)
{
    while (*a && *a == *b) { a++; b++; }
    return (int)*a - (int)*b;
}
int wcsncmp(const wchar_t *a, const wchar_t *b, size_t n)
{
    for (; n; n--, a++, b++) if (*a != *b || !*a) return (int)*a - (int)*b;
    return 0;
}
wchar_t *wcscpy(wchar_t *d, const wchar_t *o) { wchar_t *r = d; while ((*d++ = *o++)) {} return r; }
wchar_t *wcsncpy(wchar_t *d, const wchar_t *o, size_t n)
{
    size_t i = 0;
    for (; i < n && o[i]; i++) d[i] = o[i];
    for (; i < n; i++) d[i] = 0;
    return d;
}
wchar_t *wcscat(wchar_t *d, const wchar_t *o) { wcscpy(d + wcslen(d), o); return d; }
wchar_t *wcschr(const wchar_t *s, wchar_t c)
{
    for (;; s++) { if (*s == c) return (wchar_t *)s; if (!*s) return NULL; }
}
wchar_t *wcsrchr(const wchar_t *s, wchar_t c)
{
    const wchar_t *r = NULL;
    for (;; s++) { if (*s == c) r = s; if (!*s) return (wchar_t *)r; }
}
wchar_t *wmemcpy(wchar_t *d, const wchar_t *o, size_t n) { return (wchar_t *)memcpy(d, o, n * sizeof(wchar_t)); }
wchar_t *wmemset(wchar_t *d, wchar_t c, size_t n) { for (size_t i = 0; i < n; i++) d[i] = c; return d; }

int mbsinit(const mbstate_t *ps) { return !ps || ps->__count == 0; }
wint_t btowc(int c) { return c >= 0 && c < 0x80 ? (wint_t)c : WEOF; }
int wctob(wint_t c) { return c < 0x80 ? (int)c : -1; }

size_t mbrtowc(wchar_t *destino, const char *s, size_t n, mbstate_t *ps)
{
    (void)ps;
    if (!s) return 0;
    if (n == 0) return (size_t)-2;
    const unsigned char *u = (const unsigned char *)s;
    unsigned int c = u[0];
    size_t k = c < 0x80 ? 1 : (c & 0xE0) == 0xC0 ? 2 : (c & 0xF0) == 0xE0 ? 3 : (c & 0xF8) == 0xF0 ? 4 : 0;
    if (!k) { errno = EILSEQ; return (size_t)-1; }
    if (n < k) return (size_t)-2;
    if (k > 1) c &= 0x3F >> (k - 1);
    for (size_t i = 1; i < k; i++) {
        if ((u[i] & 0xC0) != 0x80) { errno = EILSEQ; return (size_t)-1; }
        c = (c << 6) | (u[i] & 0x3F);
    }
    if (destino) *destino = (wchar_t)c;
    return c ? k : 0;
}

size_t wcrtomb(char *destino, wchar_t c, mbstate_t *ps)
{
    (void)ps;
    char tmp[4];
    char *d = destino ? destino : tmp;
    unsigned int v = (unsigned int)c;
    if (v < 0x80) { d[0] = (char)v; return 1; }
    if (v < 0x800) { d[0] = (char)(0xC0 | (v >> 6)); d[1] = (char)(0x80 | (v & 0x3F)); return 2; }
    d[0] = (char)(0xE0 | (v >> 12)); d[1] = (char)(0x80 | ((v >> 6) & 0x3F)); d[2] = (char)(0x80 | (v & 0x3F));
    return 3;
}

size_t mbsrtowcs(wchar_t *destino, const char **origem, size_t n, mbstate_t *ps)
{
    const char *s = *origem;
    size_t feitos = 0;
    while (!destino || feitos < n) {
        wchar_t w;
        size_t k = mbrtowc(&w, s, 4, ps);
        if (k == (size_t)-1 || k == (size_t)-2) return (size_t)-1;
        if (destino) destino[feitos] = w;
        if (k == 0) { if (destino) *origem = NULL; return feitos; }
        s += k;
        feitos++;
    }
    *origem = s;
    return feitos;
}

size_t wcsrtombs(char *destino, const wchar_t **origem, size_t n, mbstate_t *ps)
{
    const wchar_t *s = *origem;
    size_t feitos = 0;
    for (;; s++) {
        char tmp[4];
        size_t k = wcrtomb(tmp, *s, ps);
        if (destino && feitos + k > n) { *origem = s; return feitos; }
        if (destino) memcpy(destino + feitos, tmp, k);
        if (*s == 0) { if (destino) *origem = NULL; return feitos; }
        feitos += k;
    }
}

size_t mbstowcs(wchar_t *destino, const char *s, size_t n) { return mbsrtowcs(destino, &s, n, NULL); }
size_t wcstombs(char *destino, const wchar_t *s, size_t n) { return wcsrtombs(destino, &s, n, NULL); }
int mbtowc(wchar_t *destino, const char *s, size_t n)
{
    if (!s) return 0;
    size_t k = mbrtowc(destino, s, n, NULL);
    return k == (size_t)-1 || k == (size_t)-2 ? -1 : (int)k;
}
int wctomb(char *s, wchar_t c) { return s ? (int)wcrtomb(s, c, NULL) : 0; }
int mblen(const char *s, size_t n) { return mbtowc(NULL, s, n); }
