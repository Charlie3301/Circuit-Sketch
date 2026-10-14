/* cs_formato.c -- a familia do printf e do scanf.
 *
 * Os inteiros sao formatados aqui. Os numeros de ponto flutuante vem do
 * simulador ja arredondados certo (o .NET faz a conta exata, como a
 * newlib da ESP32), e aqui ganham largura, sinal e zeros. */
#include <stdint.h>
#include <stddef.h>
#include <stdarg.h>
#include <string.h>
#include <stdlib.h>
#include <stdio.h>
#include <ctype.h>
#include <errno.h>
#include "cs_host.h"
#include "cs_interno.h"

typedef struct {
    char *buf;
    size_t cap;
    size_t n;
} saida;

static void por(saida *s, char c)
{
    if (s->n + 1 < s->cap) s->buf[s->n] = c;
    s->n++;
}

static void por_varios(saida *s, const char *p, size_t n)
{
    for (size_t i = 0; i < n; i++) por(s, p[i]);
}

static void repetir(saida *s, char c, int n)
{
    while (n-- > 0) por(s, c);
}

#define F_ESQUERDA 1
#define F_MAIS 2
#define F_ESPACO 4
#define F_ALTERNATIVO 8
#define F_ZEROS 16

/* Um campo ja pronto (sinal, prefixo, digitos) com largura e zeros. */
static void campo(saida *s, const char *sinal, const char *prefixo, const char *digitos, int ndig,
                  int precisao_zeros, int largura, int flags, int zeros_permitidos)
{
    int ns = (int)strlen(sinal), np = (int)strlen(prefixo);
    int extra = precisao_zeros > ndig ? precisao_zeros - ndig : 0;
    int total = ns + np + extra + ndig;
    int enchimento = largura > total ? largura - total : 0;

    if (!(flags & F_ESQUERDA) && !((flags & F_ZEROS) && zeros_permitidos)) repetir(s, ' ', enchimento);
    por_varios(s, sinal, (size_t)ns);
    por_varios(s, prefixo, (size_t)np);
    if (!(flags & F_ESQUERDA) && (flags & F_ZEROS) && zeros_permitidos) repetir(s, '0', enchimento);
    repetir(s, '0', extra);
    por_varios(s, digitos, (size_t)ndig);
    if (flags & F_ESQUERDA) repetir(s, ' ', enchimento);
}

static int digitos_de(unsigned long long v, int base, int maiusculas, char *tmp)
{
    const char *d = maiusculas ? "0123456789ABCDEF" : "0123456789abcdef";
    char inv[70];
    int n = 0;
    do {
        inv[n++] = d[v % (unsigned)base];
        v /= (unsigned)base;
    } while (v);
    for (int i = 0; i < n; i++) tmp[i] = inv[n - 1 - i];
    return n;
}

static int formatar(saida *s, const char *f, va_list ap)
{
    while (*f) {
        if (*f != '%') {
            por(s, *f++);
            continue;
        }
        f++;
        if (*f == '%') {
            por(s, '%');
            f++;
            continue;
        }

        int flags = 0;
        for (;; f++) {
            if (*f == '-') flags |= F_ESQUERDA;
            else if (*f == '+') flags |= F_MAIS;
            else if (*f == ' ') flags |= F_ESPACO;
            else if (*f == '#') flags |= F_ALTERNATIVO;
            else if (*f == '0') flags |= F_ZEROS;
            else break;
        }

        int largura = 0;
        if (*f == '*') {
            largura = va_arg(ap, int);
            if (largura < 0) {
                flags |= F_ESQUERDA;
                largura = -largura;
            }
            f++;
        } else {
            while (isdigit((unsigned char)*f)) largura = largura * 10 + (*f++ - '0');
        }

        int precisao = -1;
        if (*f == '.') {
            f++;
            precisao = 0;
            if (*f == '*') {
                precisao = va_arg(ap, int);
                if (precisao < 0) precisao = -1;
                f++;
            } else {
                while (isdigit((unsigned char)*f)) precisao = precisao * 10 + (*f++ - '0');
            }
        }

        /* tamanho: 0 int, 1 char, 2 short, 3 long, 4 long long, 5 size_t/ptrdiff/intmax */
        int tam = 0;
        if (*f == 'h') {
            f++;
            tam = 2;
            if (*f == 'h') { f++; tam = 1; }
        } else if (*f == 'l') {
            f++;
            tam = 3;
            if (*f == 'l') { f++; tam = 4; }
        } else if (*f == 'q' || *f == 'L') {
            f++;
            tam = 4;
        } else if (*f == 'j' || *f == 'z' || *f == 't' || *f == 'Z') {
            f++;
            tam = 5;
        }

        char conv = *f;
        if (!conv) break;
        f++;

        char tmp[80];
        switch (conv) {
        case 'd':
        case 'i': {
            long long v;
            if (tam == 4 || tam == 5) v = va_arg(ap, long long);
            else if (tam == 3) v = va_arg(ap, long);
            else v = va_arg(ap, int);
            if (tam == 1) v = (signed char)v;
            else if (tam == 2) v = (short)v;
            unsigned long long mag = v < 0 ? 0ULL - (unsigned long long)v : (unsigned long long)v;
            const char *sinal = v < 0 ? "-" : (flags & F_MAIS) ? "+" : (flags & F_ESPACO) ? " " : "";
            int n = (precisao == 0 && mag == 0) ? 0 : digitos_de(mag, 10, 0, tmp);
            campo(s, sinal, "", tmp, n, precisao, largura, flags, precisao < 0);
            break;
        }
        case 'u':
        case 'o':
        case 'x':
        case 'X':
        case 'b': {
            unsigned long long v;
            if (tam == 4 || tam == 5) v = va_arg(ap, unsigned long long);
            else if (tam == 3) v = va_arg(ap, unsigned long);
            else v = va_arg(ap, unsigned int);
            if (tam == 1) v = (unsigned char)v;
            else if (tam == 2) v = (unsigned short)v;
            int base = conv == 'u' ? 10 : conv == 'o' ? 8 : conv == 'b' ? 2 : 16;
            int n = (precisao == 0 && v == 0) ? 0 : digitos_de(v, base, conv == 'X', tmp);
            const char *prefixo = "";
            if ((flags & F_ALTERNATIVO) && v != 0) {
                if (conv == 'x') prefixo = "0x";
                else if (conv == 'X') prefixo = "0X";
            }
            if ((flags & F_ALTERNATIVO) && conv == 'o' && (n == 0 || tmp[0] != '0')) {
                if (precisao <= n) precisao = n + 1;
            }
            campo(s, "", prefixo, tmp, n, precisao, largura, flags, precisao < 0);
            break;
        }
        case 'p': {
            void *p = va_arg(ap, void *);
            int n = digitos_de((unsigned long long)(uintptr_t)p, 16, 0, tmp);
            campo(s, "", "0x", tmp, n, precisao, largura, flags & ~F_ZEROS, 0);
            break;
        }
        case 'c': {
            char c = (char)va_arg(ap, int);
            campo(s, "", "", &c, 1, -1, largura, flags, 0);
            break;
        }
        case 's': {
            const char *t = va_arg(ap, const char *);
            if (!t) t = "(null)";
            int n = precisao >= 0 ? (int)strnlen(t, (size_t)precisao) : (int)strlen(t);
            campo(s, "", "", t, n, -1, largura, flags, 0);
            break;
        }
        case 'n': {
            if (tam == 4 || tam == 5) *va_arg(ap, long long *) = (long long)s->n;
            else if (tam == 3) *va_arg(ap, long *) = (long)s->n;
            else if (tam == 2) *va_arg(ap, short *) = (short)s->n;
            else if (tam == 1) *va_arg(ap, signed char *) = (signed char)s->n;
            else *va_arg(ap, int *) = (int)s->n;
            break;
        }
        case 'f': case 'F': case 'e': case 'E': case 'g': case 'G': case 'a': case 'A': {
            double v = va_arg(ap, double);
            char num[400];
            int tipo = tolower((unsigned char)conv);
            int n = cs->real_texto(v, tipo, precisao, (flags & F_ALTERNATIVO) ? 1 : 0, num, (int)sizeof(num));
            if (n < 0) n = 0;
            if (n >= (int)sizeof(num)) n = (int)sizeof(num) - 1;
            num[n] = 0;
            if (conv >= 'A' && conv <= 'Z')
                for (int i = 0; i < n; i++) num[i] = (char)toupper((unsigned char)num[i]);

            const char *digitos = num;
            const char *sinal = "";
            if (num[0] == '-') {
                sinal = "-";
                digitos++;
                n--;
            } else if (flags & F_MAIS) {
                sinal = "+";
            } else if (flags & F_ESPACO) {
                sinal = " ";
            }
            int finito = isdigit((unsigned char)digitos[0]) || digitos[0] == '.';
            campo(s, sinal, "", digitos, n, -1, largura, flags, finito);
            break;
        }
        default:
            por(s, '%');
            por(s, conv);
            break;
        }
    }
    if (s->cap) s->buf[s->n < s->cap ? s->n : s->cap - 1] = 0;
    return (int)s->n;
}

int vsnprintf(char *buf, size_t n, const char *formato, va_list ap)
{
    saida s = { buf, n, 0 };
    return formatar(&s, formato, ap);
}

int snprintf(char *buf, size_t n, const char *formato, ...)
{
    va_list ap;
    va_start(ap, formato);
    int r = vsnprintf(buf, n, formato, ap);
    va_end(ap);
    return r;
}

int vsprintf(char *buf, const char *formato, va_list ap)
{
    return vsnprintf(buf, (size_t)1 << 30, formato, ap);
}

int sprintf(char *buf, const char *formato, ...)
{
    va_list ap;
    va_start(ap, formato);
    int r = vsprintf(buf, formato, ap);
    va_end(ap);
    return r;
}

int vasprintf(char **s, const char *formato, va_list ap)
{
    va_list copia;
    va_copy(copia, ap);
    int n = vsnprintf(NULL, 0, formato, copia);
    *s = (char *)malloc((size_t)n + 1);
    if (!*s) return -1;
    return vsnprintf(*s, (size_t)n + 1, formato, ap);
}

int asprintf(char **s, const char *formato, ...)
{
    va_list ap;
    va_start(ap, formato);
    int r = vasprintf(s, formato, ap);
    va_end(ap);
    return r;
}

/* Formata num pedaco da pilha; se nao couber, no heap. Quem recebe o
 * texto (serial, arquivo) e o escrever. */
int cs_vformatar_para(void (*escrever)(void *, const char *, int), void *alvo, const char *formato, va_list ap)
{
    char pilha[512];
    va_list copia;
    va_copy(copia, ap);
    int n = vsnprintf(pilha, sizeof(pilha), formato, ap);
    if (n < 0) return n;
    if ((size_t)n < sizeof(pilha)) {
        escrever(alvo, pilha, n);
        return n;
    }
    char *grande = (char *)malloc((size_t)n + 1);
    if (!grande) {
        escrever(alvo, pilha, (int)sizeof(pilha) - 1);
        return n;
    }
    vsnprintf(grande, (size_t)n + 1, formato, copia);
    escrever(alvo, grande, n);
    free(grande);
    return n;
}

/* ---------------------------------------------------------------------
 * scanf */
static int base_de(char c)
{
    switch (c) {
    case 'd': case 'u': return 10;
    case 'o': return 8;
    case 'x': case 'X': case 'p': return 16;
    case 'i': return 0;
    }
    return 10;
}

int vsscanf(const char *entrada, const char *f, va_list ap)
{
    const char *p = entrada;
    int atribuidos = 0;
    int algum = 0;

    while (*f) {
        if (isspace((unsigned char)*f)) {
            while (isspace((unsigned char)*f)) f++;
            while (isspace((unsigned char)*p)) p++;
            continue;
        }
        if (*f != '%') {
            if (*p != *f) break;
            p++;
            f++;
            continue;
        }
        f++;
        if (*f == '%') {
            while (isspace((unsigned char)*p)) p++;
            if (*p != '%') break;
            p++;
            f++;
            continue;
        }

        int suprimir = 0;
        if (*f == '*') { suprimir = 1; f++; }
        int largura = 0;
        while (isdigit((unsigned char)*f)) largura = largura * 10 + (*f++ - '0');
        int tam = 0;
        if (*f == 'h') { f++; tam = 2; if (*f == 'h') { f++; tam = 1; } }
        else if (*f == 'l') { f++; tam = 3; if (*f == 'l') { f++; tam = 4; } }
        else if (*f == 'L' || *f == 'q') { f++; tam = 4; }
        else if (*f == 'j' || *f == 'z' || *f == 't') { f++; tam = 5; }
        char conv = *f++;
        if (!conv) break;

        if (conv != 'c' && conv != '[' && conv != 'n')
            while (isspace((unsigned char)*p)) p++;
        if (!*p && conv != 'n') break;

        char tmp[400];
        switch (conv) {
        case 'd': case 'i': case 'u': case 'o': case 'x': case 'X': case 'p': {
            int max = largura > 0 && largura < (int)sizeof(tmp) ? largura : (int)sizeof(tmp) - 1;
            int k = 0;
            while (p[k] && k < max && !isspace((unsigned char)p[k])) { tmp[k] = p[k]; k++; }
            tmp[k] = 0;
            char *fim;
            unsigned long long v;
            if (conv == 'd' || conv == 'i') v = (unsigned long long)strtoll(tmp, &fim, base_de(conv));
            else v = strtoull(tmp, &fim, base_de(conv));
            if (fim == tmp) return algum ? atribuidos : (atribuidos ? atribuidos : 0);
            p += fim - tmp;
            algum = 1;
            if (suprimir) break;
            if (conv == 'p') *va_arg(ap, void **) = (void *)(uintptr_t)v;
            else if (tam == 1) *va_arg(ap, char *) = (char)v;
            else if (tam == 2) *va_arg(ap, short *) = (short)v;
            else if (tam == 3) *va_arg(ap, long *) = (long)v;
            else if (tam == 4 || tam == 5) *va_arg(ap, long long *) = (long long)v;
            else *va_arg(ap, int *) = (int)v;
            atribuidos++;
            break;
        }
        case 'f': case 'e': case 'g': case 'E': case 'G': case 'a': case 'F': {
            int max = largura > 0 && largura < (int)sizeof(tmp) ? largura : (int)sizeof(tmp) - 1;
            int k = 0;
            while (p[k] && k < max && !isspace((unsigned char)p[k])) { tmp[k] = p[k]; k++; }
            tmp[k] = 0;
            char *fim;
            double v = strtod(tmp, &fim);
            if (fim == tmp) return atribuidos;
            p += fim - tmp;
            algum = 1;
            if (suprimir) break;
            if (tam == 3 || tam == 4) *va_arg(ap, double *) = v;
            else *va_arg(ap, float *) = (float)v;
            atribuidos++;
            break;
        }
        case 's': {
            char *d = suprimir ? NULL : va_arg(ap, char *);
            int k = 0;
            while (*p && !isspace((unsigned char)*p) && (largura == 0 || k < largura)) {
                if (d) d[k] = *p;
                k++;
                p++;
            }
            if (d) d[k] = 0;
            algum = 1;
            if (!suprimir) atribuidos++;
            break;
        }
        case 'c': {
            int n = largura > 0 ? largura : 1;
            char *d = suprimir ? NULL : va_arg(ap, char *);
            for (int k = 0; k < n; k++) {
                if (!*p) return atribuidos;
                if (d) d[k] = *p;
                p++;
            }
            algum = 1;
            if (!suprimir) atribuidos++;
            break;
        }
        case '[': {
            int negar = 0;
            char conjunto[256];
            memset(conjunto, 0, sizeof(conjunto));
            if (*f == '^') { negar = 1; f++; }
            if (*f == ']') { conjunto[(unsigned char)']'] = 1; f++; }
            while (*f && *f != ']') {
                if (f[1] == '-' && f[2] && f[2] != ']') {
                    for (int c = (unsigned char)f[0]; c <= (unsigned char)f[2]; c++) conjunto[c] = 1;
                    f += 3;
                } else {
                    conjunto[(unsigned char)*f++] = 1;
                }
            }
            if (*f == ']') f++;
            char *d = suprimir ? NULL : va_arg(ap, char *);
            int k = 0;
            while (*p && (conjunto[(unsigned char)*p] != negar) && (largura == 0 || k < largura)) {
                if (d) d[k] = *p;
                k++;
                p++;
            }
            if (k == 0) return atribuidos;
            if (d) d[k] = 0;
            algum = 1;
            if (!suprimir) atribuidos++;
            break;
        }
        case 'n':
            if (!suprimir) *va_arg(ap, int *) = (int)(p - entrada);
            break;
        default:
            return atribuidos;
        }
    }
    if (!algum && !*p && atribuidos == 0) return EOF;
    return atribuidos;
}

int sscanf(const char *s, const char *formato, ...)
{
    va_list ap;
    va_start(ap, formato);
    int r = vsscanf(s, formato, ap);
    va_end(ap);
    return r;
}
