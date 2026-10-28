/* HardwareSerial.cpp -- a Serial pelo simulador (ver HardwareSerial.h).
 *
 * O texto sai num print so (o monitor quebra as linhas e o fio leva os
 * bytes com o baud); o byte solto e o que nao e UTF-8 saem por write(). */
#include "cs_interno.h"

HardwareSerial Serial("Serial");
HardwareSerial Serial1("Serial1");
HardwareSerial Serial2("Serial2");

void HardwareSerial::begin(unsigned long baud, uint32_t, int8_t, int8_t, bool, unsigned long, uint8_t)
{
    ch(nome, "begin", cs_i((int64_t)baud));
}

void HardwareSerial::end() { ch(nome, "end"); }
int HardwareSerial::available() { return (int)ch_i(nome, "available"); }
int HardwareSerial::peek() { return (int)ch_i(nome, "peek"); }
int HardwareSerial::read() { return (int)ch_i(nome, "read"); }
int HardwareSerial::availableForWrite() { return (int)ch_i(nome, "availableForWrite"); }
void HardwareSerial::flush() { ch(nome, "flush"); }
void HardwareSerial::updateBaudRate(unsigned long baud) { ch(nome, "updateBaudRate", cs_i((int64_t)baud)); }

size_t HardwareSerial::write(uint8_t c)
{
    ch(nome, "write", cs_i(c));
    return 1;
}

/* O pedaco e UTF-8 valido? (o acento do texto do sketch e; um byte de dado nao) */
static bool utf8(const uint8_t *p, size_t n)
{
    size_t i = 0;
    while (i < n) {
        uint8_t c = p[i];
        int seguem = c < 0x80 ? 0 : (c & 0xE0) == 0xC0 ? 1 : (c & 0xF0) == 0xE0 ? 2 : (c & 0xF8) == 0xF0 ? 3 : -1;
        if (seguem < 0 || c == 0) return false;
        if (seguem > 0 && i + (size_t)seguem >= n) return false;
        for (int k = 1; k <= seguem; k++)
            if ((p[i + (size_t)k] & 0xC0) != 0x80) return false;
        i += (size_t)seguem + 1;
    }
    return true;
}

size_t HardwareSerial::write(const uint8_t *buffer, size_t size)
{
    if (!size) return 0;
    if (utf8(buffer, size)) {
        ch(nome, "print", cs_tn((const char *)buffer, (int)size));
        return size;
    }
    for (size_t i = 0; i < size; i++) write(buffer[i]);
    return size;
}

String HardwareSerial::readString() { return ch_t(nome, "readString"); }
String HardwareSerial::readStringUntil(char terminator) { return ch_t(nome, "readStringUntil", cs_c(terminator)); }
long HardwareSerial::parseInt(LookaheadMode, char) { return (long)ch_i(nome, "parseInt"); }
float HardwareSerial::parseFloat(LookaheadMode, char) { return (float)ch_r(nome, "parseFloat"); }
