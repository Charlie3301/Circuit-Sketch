/* cs_streamstring.cpp -- o StreamString do core (StreamString.h, o de
 * verdade): uma String que tambem e Stream. O que se escreve vai para o fim;
 * o que se le sai do comeco. Pela API publica da String do simulador. */
#include "Arduino.h"
#include "StreamString.h"

size_t StreamString::write(const uint8_t *data, size_t size)
{
    if (!size || !data) return 0;
    size_t antes = length();
    concat((const char *)data, (unsigned int)size);
    return length() - antes;
}

size_t StreamString::write(uint8_t data) { return concat((char)data) ? 1 : 0; }

int StreamString::available() { return (int)length(); }

int StreamString::read()
{
    if (!length()) return -1;
    char c = charAt(0);
    remove(0, 1);
    return (uint8_t)c;
}

int StreamString::peek() { return length() ? (uint8_t)charAt(0) : -1; }

void StreamString::flush() {}
