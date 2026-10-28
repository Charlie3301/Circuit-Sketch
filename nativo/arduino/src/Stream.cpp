/* Stream.cpp -- a leitura com prazo do Arduino (ver Stream.h): o mesmo
 * algoritmo do core, com o setTimeout() valendo. */
#include "Arduino.h"

#define PARSE_TIMEOUT 1000

int Stream::timedRead()
{
    _startMillis = millis();
    do {
        int c = read();
        if (c >= 0) return c;
        yield();
    } while (millis() - _startMillis < _timeout);
    return -1;
}

int Stream::timedPeek()
{
    _startMillis = millis();
    do {
        int c = peek();
        if (c >= 0) return c;
        yield();
    } while (millis() - _startMillis < _timeout);
    return -1;
}

int Stream::peekNextDigit(LookaheadMode lookahead, bool detectDecimal)
{
    int c;
    while (1) {
        c = timedPeek();
        if (c < 0 || c == '-' || (c >= '0' && c <= '9') || (detectDecimal && c == '.')) return c;
        switch (lookahead) {
        case SKIP_NONE:
            return -1;
        case SKIP_WHITESPACE:
            switch (c) {
            case ' ':
            case '\t':
            case '\r':
            case '\n':
                break;
            default:
                return -1;
            }
        case SKIP_ALL:
            break;
        }
        read();
    }
}

bool Stream::find(const char *target)
{
    return findUntil(target, strlen(target), NULL, 0);
}

bool Stream::find(const char *target, size_t length)
{
    return findUntil(target, length, NULL, 0);
}

bool Stream::findUntil(const char *target, const char *terminator)
{
    return findUntil(target, strlen(target), terminator, strlen(terminator));
}

bool Stream::findUntil(const char *target, size_t targetLen, const char *terminator, size_t termLen)
{
    if (terminator == NULL) {
        MultiTarget t[1] = {{target, targetLen, 0}};
        return findMulti(t, 1) == 0;
    }
    MultiTarget t[2] = {{target, targetLen, 0}, {terminator, termLen, 0}};
    return findMulti(t, 2) == 0;
}

long Stream::parseInt(LookaheadMode lookahead, char ignore)
{
    bool isNegative = false;
    long value = 0;
    int c = peekNextDigit(lookahead, false);
    if (c < 0) return 0;
    do {
        if ((char)c == ignore)
            ;
        else if (c == '-')
            isNegative = true;
        else if (c >= '0' && c <= '9')
            value = value * 10 + c - '0';
        read();
        c = timedPeek();
    } while ((c >= '0' && c <= '9') || (char)c == ignore);
    if (isNegative) value = -value;
    return value;
}

float Stream::parseFloat(LookaheadMode lookahead, char ignore)
{
    bool isNegative = false;
    bool isFraction = false;
    double value = 0.0;
    int c;
    double fraction = 1.0;

    c = peekNextDigit(lookahead, true);
    if (c < 0) return 0;
    do {
        if ((char)c == ignore)
            ;
        else if (c == '-')
            isNegative = true;
        else if (c == '.')
            isFraction = true;
        else if (c >= '0' && c <= '9') {
            if (isFraction) {
                fraction *= 0.1;
                value = value + fraction * (c - '0');
            } else {
                value = value * 10 + c - '0';
            }
        }
        read();
        c = timedPeek();
    } while ((c >= '0' && c <= '9') || (c == '.' && !isFraction) || (char)c == ignore);
    if (isNegative) value = -value;
    return (float)value;
}

size_t Stream::readBytes(char *buffer, size_t length)
{
    size_t count = 0;
    while (count < length) {
        int c = timedRead();
        if (c < 0) break;
        *buffer++ = (char)c;
        count++;
    }
    return count;
}

size_t Stream::readBytesUntil(char terminator, char *buffer, size_t length)
{
    size_t index = 0;
    while (index < length) {
        int c = timedRead();
        if (c < 0 || (char)c == terminator) break;
        *buffer++ = (char)c;
        index++;
    }
    return index;
}

String Stream::readString()
{
    String ret;
    int c = timedRead();
    while (c >= 0) {
        ret += (char)c;
        c = timedRead();
    }
    return ret;
}

String Stream::readStringUntil(char terminator)
{
    String ret;
    int c = timedRead();
    while (c >= 0 && (char)c != terminator) {
        ret += (char)c;
        c = timedRead();
    }
    return ret;
}

int Stream::findMulti(struct Stream::MultiTarget *targets, int tCount)
{
    for (struct MultiTarget *t = targets; t < targets + tCount; ++t)
        if (t->len <= 0) return (int)(t - targets);

    while (1) {
        int c = timedRead();
        if (c < 0) return -1;

        for (struct MultiTarget *t = targets; t < targets + tCount; ++t) {
            if (c == t->str[t->index]) {
                if (++t->index == t->len) return (int)(t - targets);
                else continue;
            }
            if (t->index == 0) continue;

            int origIndex = (int)t->index;
            do {
                --t->index;
                if (c != t->str[t->index]) continue;
                if (t->index == 0) {
                    t->index++;
                    break;
                }
                int diff = origIndex - (int)t->index;
                size_t i;
                for (i = 0; i < t->index; ++i)
                    if (t->str[i] != t->str[i + diff]) break;
                if (i == t->index) {
                    t->index++;
                    break;
                }
            } while (t->index);
        }
    }
    return -1;
}
