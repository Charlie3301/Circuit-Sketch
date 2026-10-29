/* EEPROM.h -- a EEPROM da placa (no Uno, a do chip; na ESP32, a emulada na
 * flash, com begin() e commit()). Cada byte e o do simulador; o get() e o
 * put() de qualquer tipo sao feitos byte a byte, como na biblioteca. */
#ifndef EEPROM_h
#define EEPROM_h

#include "cs_objeto.h"

class EEPROMClass;

struct EERef {
    EERef(int index) : index(index) {}
    uint8_t operator*() const;
    operator uint8_t() const { return **this; }
    EERef &operator=(const EERef &ref) { return *this = *ref; }
    EERef &operator=(uint8_t in);
    EERef &operator+=(uint8_t in) { return *this = **this + in; }
    EERef &operator-=(uint8_t in) { return *this = **this - in; }
    EERef &operator*=(uint8_t in) { return *this = **this * in; }
    EERef &operator/=(uint8_t in) { return *this = **this / in; }
    EERef &operator^=(uint8_t in) { return *this = **this ^ in; }
    EERef &operator%=(uint8_t in) { return *this = **this % in; }
    EERef &operator&=(uint8_t in) { return *this = **this & in; }
    EERef &operator|=(uint8_t in) { return *this = **this | in; }
    EERef &operator<<=(uint8_t in) { return *this = **this << in; }
    EERef &operator>>=(uint8_t in) { return *this = **this >> in; }
    EERef &update(uint8_t in) { return in != *this ? *this = in : *this; }
    EERef &operator++() { return *this += 1; }
    EERef &operator--() { return *this -= 1; }
    uint8_t operator++(int)
    {
        uint8_t ret = **this;
        ++(*this);
        return ret;
    }
    uint8_t operator--(int)
    {
        uint8_t ret = **this;
        --(*this);
        return ret;
    }
    int index;
};

class EEPROMClass {
public:
    bool begin(size_t size) { return cs_obj_i("EEPROM", "begin", (int)size) != 0; }
    void end() { cs_obj_n("EEPROM", "end"); }
    bool commit() { return cs_obj_i("EEPROM", "commit") != 0; }
    uint8_t read(int idx) { return (uint8_t)cs_obj_i("EEPROM", "read", idx); }
    void write(int idx, uint8_t val) { cs_obj_n("EEPROM", "write", idx, val); }
    void update(int idx, uint8_t val) { cs_obj_n("EEPROM", "update", idx, val); }
    uint16_t length() { return (uint16_t)cs_obj_i("EEPROM", "length"); }
    EERef operator[](int idx) { return EERef(idx); }

    template <typename T> T &get(int idx, T &t)
    {
        uint8_t *ptr = (uint8_t *)&t;
        for (int count = sizeof(T); count; --count, ++idx) *ptr++ = read(idx);
        return t;
    }
    template <typename T> const T &put(int idx, const T &t)
    {
        const uint8_t *ptr = (const uint8_t *)&t;
        for (int count = sizeof(T); count; --count, ++idx) update(idx, *ptr++);
        return t;
    }
    uint8_t readByte(int idx) { return read(idx); }
    void writeByte(int idx, uint8_t v) { write(idx, v); }
    String readString(int idx) { return cs_obj_t("EEPROM", "readString", idx); }
    size_t writeString(int idx, const String &v) { return (size_t)cs_obj_i("EEPROM", "writeString", idx, v); }
};

inline uint8_t EERef::operator*() const { return (uint8_t)cs_obj_i("EEPROM", "read", index); }
inline EERef &EERef::operator=(uint8_t in)
{
    cs_obj_n("EEPROM", "write", index, in);
    return *this;
}

static EEPROMClass EEPROM;

#endif
