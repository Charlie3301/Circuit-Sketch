/* NimBLEBeacon.h -- o iBeacon da NimBLE-Arduino: os 25 bytes do dado do fabricante (a Apple, 0x004C). */
#ifndef CS_NIMBLE_BEACON_H
#define CS_NIMBLE_BEACON_H

#include "NimBLEDevice.h"

#define CS_TROCA16(x) ((uint16_t)(((x) & 0xFF00) >> 8 | ((x) & 0x00FF) << 8))

class NimBLEBeacon {
public:
    struct BeaconData {
        uint16_t manufacturerId{0x4c00};
        uint8_t subType{0x02};
        uint8_t subTypeLength{0x15};
        uint8_t proximityUUID[16]{};
        uint16_t major{};
        uint16_t minor{};
        int8_t signalPower{};
        operator std::vector<uint8_t>() const
        {
            return std::vector<uint8_t>(reinterpret_cast<const uint8_t *>(this), reinterpret_cast<const uint8_t *>(this) + sizeof(BeaconData));
        }
    } __attribute__((packed));

    const BeaconData &getData() { return dados; }
    uint16_t getMajor() { return CS_TROCA16(dados.major); }
    uint16_t getMinor() { return CS_TROCA16(dados.minor); }
    uint16_t getManufacturerId() { return CS_TROCA16(dados.manufacturerId); }
    NimBLEUUID getProximityUUID() { return NimBLEUUID(dados.proximityUUID, 16, true); }
    int8_t getSignalPower() { return dados.signalPower; }
    void setData(const uint8_t *data, uint8_t length)
    {
        if (length == sizeof(BeaconData)) memcpy(&dados, data, length);
    }
    void setData(const BeaconData &d) { dados = d; }
    void setMajor(uint16_t major) { dados.major = CS_TROCA16(major); }
    void setMinor(uint16_t minor) { dados.minor = CS_TROCA16(minor); }
    void setManufacturerId(uint16_t id) { dados.manufacturerId = CS_TROCA16(id); }
    /* O UUID vai na ordem do texto (o primeiro byte e o mais significativo). */
    void setProximityUUID(const NimBLEUUID &uuid)
    {
        std::string t = uuid.to128().toString();
        t.erase(std::remove(t.begin(), t.end(), '-'), t.end());
        for (int i = 0; i < 16 && t.size() == 32; i++) dados.proximityUUID[i] = (uint8_t)strtoul(t.substr(2 * i, 2).c_str(), nullptr, 16);
    }
    void setSignalPower(int8_t p) { dados.signalPower = p; }

private:
    BeaconData dados;
};

#endif
