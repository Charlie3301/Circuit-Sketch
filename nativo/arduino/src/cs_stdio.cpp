/* cs_stdio.cpp -- o printf da placa sai no monitor serial: na ESP32 e a
 * UART0, na Pico o stdio da USB, e no programa em C (main, app_main) e a
 * unica saida que existe. Estas definicoes ficam no lugar das da biblioteca
 * do C so dentro do sketch compilado. */
/* O stdio simples do mingw aqui: printf e __mingw_printf sao dois nomes. */
#undef __USE_MINGW_ANSI_STDIO
#define __USE_MINGW_ANSI_STDIO 0
#include "cs_interno.h"
#include <stdarg.h>

static void escrever(const char *s, int n)
{
    if (n > 0) ch("Serial", "print", cs_tn(s, n));
}

extern "C" int vprintf(const char *formato, va_list a)
{
    char curto[512];
    va_list copia;
    va_copy(copia, a);
    int n = vsnprintf(curto, sizeof(curto), formato, a);
    if (n < (int)sizeof(curto)) {
        escrever(curto, n);
    } else if (n > 0) {
        char *longo = (char *)malloc((size_t)n + 1);
        if (longo) {
            vsnprintf(longo, (size_t)n + 1, formato, copia);
            escrever(longo, n);
            free(longo);
        }
    }
    va_end(copia);
    return n;
}

extern "C" int printf(const char *formato, ...)
{
    va_list a;
    va_start(a, formato);
    int n = vprintf(formato, a);
    va_end(a);
    return n;
}

/* Com o stdio "ANSI" do mingw os nomes sao estes. */
extern "C" int __mingw_vprintf(const char *formato, va_list a) { return vprintf(formato, a); }
extern "C" int __mingw_printf(const char *formato, ...)
{
    va_list a;
    va_start(a, formato);
    int n = vprintf(formato, a);
    va_end(a);
    return n;
}

extern "C" int puts(const char *s)
{
    int n = (int)strlen(s);
    escrever(s, n);
    escrever("\n", 1);
    return n + 1;
}

extern "C" int putchar(int c)
{
    char b = (char)c;
    escrever(&b, 1);
    return c & 0xFF;
}

/* O getchar le do monitor (espera o que a pessoa digitar). */
extern "C" int getchar(void)
{
    while (true) {
        int c = (int)ch_i("Serial", "read");
        if (c >= 0) return c;
        delay(1);
    }
}
