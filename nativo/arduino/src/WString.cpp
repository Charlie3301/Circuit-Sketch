/* WString.cpp -- a String do Arduino (ver WString.h). */
#include "Arduino.h"

#include <float.h>
#include <stdio.h>

void String::init(void)
{
    buffer = nullptr;
    capacity = 0;
    len = 0;
}

void String::invalidate(void)
{
    free(buffer);
    init();
}

bool String::reserve(unsigned int size)
{
    if (buffer && capacity >= size) return true;
    if (changeBuffer(size)) {
        if (len == 0) buffer[0] = 0;
        return true;
    }
    return false;
}

bool String::changeBuffer(unsigned int maxStrLen)
{
    char *novo = (char *)realloc(buffer, maxStrLen + 1);
    if (!novo) return false;
    buffer = novo;
    capacity = maxStrLen;
    return true;
}

String &String::copy(const char *cstr, unsigned int length)
{
    if (!reserve(length)) {
        invalidate();
        return *this;
    }
    len = length;
    memmove(buffer, cstr, length);
    buffer[len] = 0;
    return *this;
}

void String::move(String &rhs)
{
    if (this != &rhs) {
        free(buffer);
        buffer = rhs.buffer;
        capacity = rhs.capacity;
        len = rhs.len;
        rhs.init();
    }
}

/* ---- construtores ---- */
String::String(const char *cstr)
{
    init();
    if (cstr) copy(cstr, (unsigned int)strlen(cstr));
}

String::String(const char *cstr, unsigned int length)
{
    init();
    if (cstr) copy(cstr, length);
}

String::String(const String &value)
{
    init();
    *this = value;
}

String::String(const __FlashStringHelper *pstr)
{
    init();
    *this = pstr;
}

String::String(String &&rval) noexcept
{
    init();
    move(rval);
}

String::String(char c)
{
    init();
    char buf[2] = {c, 0};
    *this = buf;
}

static void numeroEmTexto(char *buf, unsigned long long valor, bool negativo, unsigned char base)
{
    if (base < 2) base = 10;
    char tmp[72];
    int i = 0;
    do {
        int d = (int)(valor % base);
        tmp[i++] = (char)(d < 10 ? '0' + d : 'a' + d - 10);
        valor /= base;
    } while (valor);
    int j = 0;
    if (negativo) buf[j++] = '-';
    while (i) buf[j++] = tmp[--i];
    buf[j] = 0;
}

String::String(unsigned char value, unsigned char base)
{
    init();
    char buf[72];
    numeroEmTexto(buf, value, false, base);
    *this = buf;
}

/* Base 10 com sinal; nas outras, o numero sem sinal do tamanho do tipo, como
 * o itoa/ltoa do core faz. */
#define CS_COM_SINAL(TIPO, SEM_SINAL)                                                   \
    String::String(TIPO value, unsigned char base)                                       \
    {                                                                                    \
        init();                                                                          \
        char buf[72];                                                                    \
        if (base == 10 && value < 0)                                                     \
            numeroEmTexto(buf, (unsigned long long)(-(long long)value), true, base);     \
        else                                                                             \
            numeroEmTexto(buf, (unsigned long long)(SEM_SINAL)value, false, base);       \
        *this = buf;                                                                     \
    }
CS_COM_SINAL(int, unsigned int)
CS_COM_SINAL(long, unsigned long)
CS_COM_SINAL(long long, unsigned long long)

String::String(unsigned int value, unsigned char base)
{
    init();
    char buf[72];
    numeroEmTexto(buf, value, false, base);
    *this = buf;
}

String::String(unsigned long value, unsigned char base)
{
    init();
    char buf[72];
    numeroEmTexto(buf, value, false, base);
    *this = buf;
}

String::String(unsigned long long value, unsigned char base)
{
    init();
    char buf[72];
    numeroEmTexto(buf, value, false, base);
    *this = buf;
}

String::String(float value, unsigned int decimalPlaces)
{
    init();
    char buf[64];
    *this = dtostrf(value, decimalPlaces + 2, (unsigned char)decimalPlaces, buf);
}

String::String(double value, unsigned int decimalPlaces)
{
    init();
    char buf[400];
    *this = dtostrf(value, decimalPlaces + 2, (unsigned char)decimalPlaces, buf);
}

String::~String()
{
    free(buffer);
}

/* ---- atribuicao ---- */
String &String::operator=(const String &rhs)
{
    if (this == &rhs) return *this;
    if (rhs.buffer) copy(rhs.buffer, rhs.len);
    else invalidate();
    return *this;
}

String &String::operator=(String &&rval) noexcept
{
    move(rval);
    return *this;
}

String &String::operator=(const char *cstr)
{
    if (cstr) copy(cstr, (unsigned int)strlen(cstr));
    else invalidate();
    return *this;
}

String &String::operator=(const __FlashStringHelper *pstr)
{
    return *this = reinterpret_cast<const char *>(pstr);
}

/* ---- concatenacao ---- */
bool String::concat(const String &s)
{
    return concat(s.buffer ? s.buffer : "", s.len);
}

bool String::concat(const char *cstr, unsigned int length)
{
    unsigned int novo = len + length;
    if (!cstr) return false;
    if (length == 0) return true;
    if (!reserve(novo)) return false;
    memmove(buffer + len, cstr, length);
    len = novo;
    buffer[len] = 0;
    return true;
}

bool String::concat(const char *cstr)
{
    if (!cstr) return false;
    return concat(cstr, (unsigned int)strlen(cstr));
}

bool String::concat(char c)
{
    return concat(&c, 1);
}

bool String::concat(unsigned char num) { return concat(String(num)); }
bool String::concat(int num) { return concat(String(num)); }
bool String::concat(unsigned int num) { return concat(String(num)); }
bool String::concat(long num) { return concat(String(num)); }
bool String::concat(unsigned long num) { return concat(String(num)); }
bool String::concat(long long num) { return concat(String(num)); }
bool String::concat(unsigned long long num) { return concat(String(num)); }
bool String::concat(float num) { return concat(String(num)); }
bool String::concat(double num) { return concat(String(num)); }
bool String::concat(const __FlashStringHelper *str) { return concat(reinterpret_cast<const char *>(str)); }

#define CS_SOMA(TIPO)                                                       \
    StringSumHelper &operator+(const StringSumHelper &lhs, TIPO rhs)        \
    {                                                                       \
        StringSumHelper &a = const_cast<StringSumHelper &>(lhs);            \
        if (!a.concat(rhs)) a.invalidate();                                 \
        return a;                                                           \
    }
CS_SOMA(const String &)
CS_SOMA(const char *)
CS_SOMA(char)
CS_SOMA(unsigned char)
CS_SOMA(int)
CS_SOMA(unsigned int)
CS_SOMA(long)
CS_SOMA(unsigned long)
CS_SOMA(long long)
CS_SOMA(unsigned long long)
CS_SOMA(float)
CS_SOMA(double)
CS_SOMA(const __FlashStringHelper *)

/* ---- comparacao ---- */
int String::compareTo(const String &s) const
{
    return strcmp(c_str(), s.c_str());
}

int String::compareTo(const char *cstr) const
{
    return strcmp(c_str(), cstr ? cstr : "");
}

bool String::equals(const String &s2) const
{
    return len == s2.len && compareTo(s2) == 0;
}

bool String::equals(const char *cstr) const
{
    return strcmp(c_str(), cstr ? cstr : "") == 0;
}

bool String::equalsIgnoreCase(const String &s2) const
{
    if (this == &s2) return true;
    if (len != s2.len) return false;
    for (unsigned int i = 0; i < len; i++)
        if (tolower((unsigned char)c_str()[i]) != tolower((unsigned char)s2.c_str()[i])) return false;
    return true;
}

bool String::equalsConstantTime(const String &s2) const
{
    if (len != s2.len) return false;
    unsigned int diferenca = 0;
    for (unsigned int i = 0; i < len; i++) diferenca |= (unsigned char)(c_str()[i] ^ s2.c_str()[i]);
    return diferenca == 0;
}

bool String::startsWith(const String &s2) const
{
    if (len < s2.len) return false;
    return startsWith(s2, 0);
}

bool String::startsWith(const String &s2, unsigned int offset) const
{
    if (s2.len > len || offset > len - s2.len) return false;
    if (s2.len == 0) return true;
    if (!buffer || !s2.buffer) return false;
    return strncmp(&buffer[offset], s2.buffer, s2.len) == 0;
}

bool String::endsWith(const String &s2) const
{
    if (len < s2.len || !buffer || !s2.buffer) return s2.len == 0;
    return strcmp(&buffer[len - s2.len], s2.buffer) == 0;
}

/* ---- caracteres ---- */
char String::charAt(unsigned int loc) const
{
    return operator[](loc);
}

void String::setCharAt(unsigned int loc, char c)
{
    if (loc < len) buffer[loc] = c;
}

char &String::operator[](unsigned int index)
{
    static char ninguem;
    if (index >= len || !buffer) {
        ninguem = 0;
        return ninguem;
    }
    return buffer[index];
}

char String::operator[](unsigned int index) const
{
    if (index >= len || !buffer) return 0;
    return buffer[index];
}

void String::getBytes(unsigned char *buf, unsigned int bufsize, unsigned int index) const
{
    if (!bufsize || !buf) return;
    if (index >= len) {
        buf[0] = 0;
        return;
    }
    unsigned int n = bufsize - 1;
    if (n > len - index) n = len - index;
    memcpy(buf, buffer + index, n);
    buf[n] = 0;
}

/* ---- busca ---- */
int String::indexOf(char c) const
{
    return indexOf(c, 0);
}

int String::indexOf(char ch, unsigned int fromIndex) const
{
    if (fromIndex >= len) return -1;
    const char *p = (const char *)memchr(buffer + fromIndex, ch, len - fromIndex);
    return p ? (int)(p - buffer) : -1;
}

int String::indexOf(const String &s2) const
{
    return indexOf(s2, 0);
}

int String::indexOf(const String &s2, unsigned int fromIndex) const
{
    if (fromIndex >= len) return -1;
    const char *found = strstr(buffer + fromIndex, s2.c_str());
    return found ? (int)(found - buffer) : -1;
}

int String::lastIndexOf(char theChar) const
{
    return lastIndexOf(theChar, len - 1);
}

int String::lastIndexOf(char ch, unsigned int fromIndex) const
{
    if (fromIndex >= len) return -1;
    for (int i = (int)fromIndex; i >= 0; i--)
        if (buffer[i] == ch) return i;
    return -1;
}

int String::lastIndexOf(const String &s2) const
{
    return lastIndexOf(s2, len - s2.len);
}

int String::lastIndexOf(const String &s2, unsigned int fromIndex) const
{
    if (s2.len == 0 || len == 0 || s2.len > len) return -1;
    if (fromIndex >= len) fromIndex = len - 1;
    int achado = -1;
    for (char *p = buffer; p <= buffer + fromIndex; p++) {
        p = strstr(p, s2.buffer);
        if (!p) break;
        if ((unsigned int)(p - buffer) <= fromIndex) achado = (int)(p - buffer);
    }
    return achado;
}

String String::substring(unsigned int left, unsigned int right) const
{
    if (left > right) {
        unsigned int t = right;
        right = left;
        left = t;
    }
    String out;
    if (left >= len) return out;
    if (right > len) right = len;
    out.copy(buffer + left, right - left);
    return out;
}

/* ---- mudar ---- */
void String::replace(char find, char replace)
{
    if (!buffer) return;
    for (char *p = buffer; *p; p++)
        if (*p == find) *p = replace;
}

void String::replace(const String &find, const String &replace)
{
    if (len == 0 || find.len == 0) return;
    String saida;
    saida.reserve(len);
    const char *p = buffer;
    while (true) {
        const char *achado = strstr(p, find.buffer);
        if (!achado) break;
        saida.concat(p, (unsigned int)(achado - p));
        saida.concat(replace);
        p = achado + find.len;
    }
    saida.concat(p);
    move(saida);
}

void String::remove(unsigned int index)
{
    remove(index, (unsigned int)-1);
}

void String::remove(unsigned int index, unsigned int count)
{
    if (index >= len) return;
    if (count <= 0) return;
    if (count > len - index) count = len - index;
    char *writeTo = buffer + index;
    len = len - count;
    memmove(writeTo, buffer + index + count, len - index);
    buffer[len] = 0;
}

void String::toLowerCase(void)
{
    if (!buffer) return;
    for (char *p = buffer; *p; p++) *p = (char)tolower((unsigned char)*p);
}

void String::toUpperCase(void)
{
    if (!buffer) return;
    for (char *p = buffer; *p; p++) *p = (char)toupper((unsigned char)*p);
}

void String::trim(void)
{
    if (!buffer || len == 0) return;
    char *begin = buffer;
    while (isspace((unsigned char)*begin)) begin++;
    char *end = buffer + len - 1;
    while (isspace((unsigned char)*end) && end >= begin) end--;
    len = (unsigned int)(end + 1 - begin);
    if (begin > buffer) memmove(buffer, begin, len);
    buffer[len] = 0;
}

/* ---- conversao ---- */
long String::toInt(void) const
{
    return buffer ? atol(buffer) : 0;
}

float String::toFloat(void) const
{
    return (float)toDouble();
}

double String::toDouble(void) const
{
    return buffer ? atof(buffer) : 0;
}
