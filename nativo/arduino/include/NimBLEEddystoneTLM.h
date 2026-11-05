/* NimBLEEddystoneTLM.h -- o quadro de telemetria do Eddystone (a tensao, a temperatura, as contagens), como
 * a NimBLE-Arduino: os campos vao na ordem da rede (big-endian) e os get/set trocam os bytes. */
#ifndef CS_NIMBLE_EDDYSTONE_TLM_H
#define CS_NIMBLE_EDDYSTONE_TLM_H

#include "NimBLEDevice.h"

#define EDDYSTONE_TLM_FRAME_TYPE 0x20

class NimBLEEddystoneTLM {
public:
    struct BeaconData {
        uint8_t frameType{EDDYSTONE_TLM_FRAME_TYPE};
        uint8_t version{0};
        uint16_t volt{0xE40C};
        uint16_t temp{0x0017};
        uint32_t advCount{0};
        uint32_t tmil{0};
    } __attribute__((packed));

    const BeaconData getData() { return dados; }
    NimBLEUUID getUUID() { return NimBLEUUID(uuid); }
    uint8_t getVersion() { return dados.version; }
    uint16_t getVolt() { return troca16(dados.volt); }
    int16_t getTemp() { return (int16_t)troca16(dados.temp); }
    uint32_t getCount() { return troca32(dados.advCount); }
    uint32_t getTime() { return troca32(dados.tmil) / 10; }
    std::string toString()
    {
        uint32_t s = troca32(dados.tmil) / 10;
        char t[200];
        snprintf(t, sizeof(t),
                 "Version %d\nBattery Voltage %d mV\nTemperature %.2f C\nAdv. Count %u\nTime in seconds %u\nTime %04u %02u:%02u:%02u\n",
                 dados.version, troca16(dados.volt), (int16_t)troca16(dados.temp) / 256.0f, (unsigned)troca32(dados.advCount), (unsigned)s,
                 (unsigned)(s / 86400), (unsigned)(s / 3600 % 24), (unsigned)(s / 60 % 60), (unsigned)(s % 60));
        return t;
    }
    void setData(const uint8_t *data, uint8_t length)
    {
        if (length == sizeof(dados)) memcpy(&dados, data, length);
    }
    void setData(const BeaconData &d) { dados = d; }
    void setUUID(const NimBLEUUID &u)
    {
        if (u.bitSize() == 16) uuid = u.getNative()->uuid.uuid16;
    }
    void setVersion(uint8_t v) { dados.version = v; }
    void setVolt(uint16_t v) { dados.volt = troca16(v); }
    void setTemp(int16_t t) { dados.temp = troca16((uint16_t)t); }
    void setCount(uint32_t c) { dados.advCount = troca32(c); }
    void setTime(uint32_t t) { dados.tmil = troca32(t); }

private:
    uint16_t uuid{0xFEAA};
    BeaconData dados;
    static uint16_t troca16(uint16_t x) { return (uint16_t)(x >> 8 | x << 8); }
    static uint32_t troca32(uint32_t x) { return __builtin_bswap32(x); }
};

#endif
