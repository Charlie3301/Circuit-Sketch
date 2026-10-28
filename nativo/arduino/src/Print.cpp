/* Print.cpp -- o print/println do Arduino (ver Print.h): os numeros em
 * qualquer base e o real com N casas, do jeito exato do core. */
#include "Arduino.h"

size_t Print::write(const uint8_t *buffer, size_t size)
{
    size_t n = 0;
    while (size--) {
        if (write(*buffer++)) n++;
        else break;
    }
    return n;
}

size_t Print::vprintf(const char *format, va_list arg)
{
    char curto[128];
    va_list copia;
    va_copy(copia, arg);
    int len = vsnprintf(curto, sizeof(curto), format, copia);
    va_end(copia);
    if (len < 0) return 0;
    if ((size_t)len < sizeof(curto)) return write((const uint8_t *)curto, (size_t)len);
    char *longo = (char *)malloc((size_t)len + 1);
    if (!longo) return 0;
    vsnprintf(longo, (size_t)len + 1, format, arg);
    size_t n = write((const uint8_t *)longo, (size_t)len);
    free(longo);
    return n;
}

size_t Print::printf(const char *format, ...)
{
    va_list arg;
    va_start(arg, format);
    size_t n = vprintf(format, arg);
    va_end(arg);
    return n;
}

size_t Print::print(const __FlashStringHelper *ifsh)
{
    return print(reinterpret_cast<const char *>(ifsh));
}

size_t Print::print(const String &s)
{
    return write((const uint8_t *)s.c_str(), s.length());
}

size_t Print::print(const char str[])
{
    return write(str);
}

size_t Print::print(char c)
{
    return write((uint8_t)c);
}

size_t Print::print(unsigned char b, int base)
{
    return print((unsigned long)b, base);
}

size_t Print::print(int n, int base)
{
    return print((long)n, base);
}

size_t Print::print(unsigned int n, int base)
{
    return print((unsigned long)n, base);
}

/* Base 10 com sinal; nas outras, os bits do proprio tipo: print(-1, HEX)
 * num long da FFFFFFFF, como na placa de 32 bits. */
size_t Print::print(long n, int base)
{
    if (base == 0) return write((uint8_t)n);
    if (base == 10 && n < 0) {
        size_t t = print('-');
        return printNumber((unsigned long long)(-(long long)n), 10) + t;
    }
    return printNumber((unsigned long long)(unsigned long)n, (uint8_t)base);
}

size_t Print::print(unsigned long n, int base)
{
    if (base == 0) return write((uint8_t)n);
    return printNumber(n, (uint8_t)base);
}

size_t Print::print(long long n, int base)
{
    if (base == 0) return write((uint8_t)n);
    if (base == 10 && n < 0) {
        size_t t = print('-');
        return printNumber((unsigned long long)(-n), 10) + t;
    }
    return printNumber((unsigned long long)n, (uint8_t)base);
}

size_t Print::print(unsigned long long n, int base)
{
    if (base == 0) return write((uint8_t)n);
    return printNumber(n, (uint8_t)base);
}

size_t Print::print(double n, int digits)
{
    return printFloat(n, (uint8_t)digits);
}

size_t Print::print(const Printable &x)
{
    return x.printTo(*this);
}

size_t Print::println(void)
{
    return write("\r\n");
}

#define CS_PRINTLN(TIPO)                    \
    size_t Print::println(TIPO x)           \
    {                                       \
        size_t n = print(x);                \
        return n + println();               \
    }
#define CS_PRINTLN_BASE(TIPO)               \
    size_t Print::println(TIPO x, int base) \
    {                                       \
        size_t n = print(x, base);          \
        return n + println();               \
    }
CS_PRINTLN(const __FlashStringHelper *)
CS_PRINTLN(const String &)
CS_PRINTLN(const char *)
CS_PRINTLN(char)
CS_PRINTLN(const Printable &)
CS_PRINTLN_BASE(unsigned char)
CS_PRINTLN_BASE(int)
CS_PRINTLN_BASE(unsigned int)
CS_PRINTLN_BASE(long)
CS_PRINTLN_BASE(unsigned long)
CS_PRINTLN_BASE(long long)
CS_PRINTLN_BASE(unsigned long long)
CS_PRINTLN_BASE(double)

size_t Print::printNumber(unsigned long long n, uint8_t base)
{
    char buf[8 * sizeof(n) + 1];
    char *str = &buf[sizeof(buf) - 1];
    *str = '\0';
    if (base < 2) base = 10;
    do {
        char c = (char)(n % base);
        n /= base;
        *--str = c < 10 ? c + '0' : c + 'A' - 10;
    } while (n);
    return write(str);
}

size_t Print::printFloat(double number, uint8_t digits)
{
    size_t n = 0;
    if (isnan(number)) return print("nan");
    if (isinf(number)) return print("inf");
    if (number > 4294967040.0) return print("ovf");
    if (number < -4294967040.0) return print("ovf");

    if (number < 0.0) {
        n += print('-');
        number = -number;
    }

    double rounding = 0.5;
    for (uint8_t i = 0; i < digits; ++i) rounding /= 10.0;
    number += rounding;

    unsigned long int_part = (unsigned long)number;
    double remainder = number - (double)int_part;
    n += print(int_part);

    if (digits > 0) n += print('.');

    while (digits-- > 0) {
        remainder *= 10.0;
        unsigned int toPrint = (unsigned int)remainder;
        n += print(toPrint);
        remainder -= toPrint;
    }
    return n;
}
