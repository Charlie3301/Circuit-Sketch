/* IRremoteESP8266.h -- a IRremoteESP8266 (ESP8266 e ESP32): o mesmo receptor
 * do circuito da IRremote, com o IRrecv(pino, buffer, timeout, guardar) e os
 * utilitarios de 64 bits dela (IRutils.h). */
#ifndef IRREMOTEESP8266_H_
#define IRREMOTEESP8266_H_

#include "IRremote.h"

const uint16_t kRawTick = 2;
const uint8_t kTimeoutMs = 15;
const uint16_t kRawBuf = 100;

inline String uint64ToString(uint64_t input, uint8_t base = 10)
{
    if (base < 2 || base > 36) base = 10;
    char t[66];
    int i = 65;
    t[i] = 0;
    do {
        int d = (int)(input % base);
        t[--i] = (char)(d < 10 ? '0' + d : 'A' + d - 10);
        input /= base;
    } while (input && i > 0);
    return String(t + i);
}

inline void serialPrintUint64(uint64_t input, uint8_t base = 10) { Serial.print(uint64ToString(input, base)); }

inline String resultToHexidecimal(const decode_results *results) { return "0x" + uint64ToString(results->value, 16); }
inline String resultToHumanReadableBasic(const decode_results *results)
{
    return "Protocol  : NEC\nCode      : " + resultToHexidecimal(results) + " (32 Bits)\n";
}
inline String typeToString(decode_type_t protocol, bool isRepeat = false)
{
    (void)isRepeat;
    return protocol == NEC ? "NEC" : "UNKNOWN";
}

#endif
