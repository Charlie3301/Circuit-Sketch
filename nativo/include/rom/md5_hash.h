/* rom/md5_hash.h do CircuitSketch (codigo nativo): o MD5 que a ESP32 tem
 * na ROM. */
#pragma once

#include <stdint.h>

struct MD5Context {
    uint32_t buf[4];
    uint32_t bits[2];
    uint8_t in[64];
};

void MD5Init(struct MD5Context *context);
void MD5Update(struct MD5Context *context, unsigned char const *buf, unsigned len);
void MD5Final(unsigned char digest[16], struct MD5Context *context);
