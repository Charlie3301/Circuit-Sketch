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
    bool commit()
    {
        if (copia) writeBytes(0, copia, length());
        return cs_obj_i("EEPROM", "commit") != 0;
    }
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

    /* Os de cada tipo: fora da EEPROM, o read devolve 0 e o write nao escreve nada. */
    template <class T> T readAll(int address, T &valor)
    {
        if (address < 0 || address + sizeof(T) > length()) return valor;
        return get(address, valor);
    }
    template <class T> size_t writeAll(int address, const T &valor)
    {
        if (address < 0 || address + sizeof(T) > length()) return 0;
        put(address, valor);
        return sizeof(T);
    }
    int8_t readChar(int a) { int8_t v = 0; return readAll(a, v); }
    uint8_t readUChar(int a) { uint8_t v = 0; return readAll(a, v); }
    bool readBool(int a) { int8_t v = 0; return readAll(a, v) != 0; }
    int16_t readShort(int a) { int16_t v = 0; return readAll(a, v); }
    uint16_t readUShort(int a) { uint16_t v = 0; return readAll(a, v); }
    int32_t readInt(int a) { int32_t v = 0; return readAll(a, v); }
    uint32_t readUInt(int a) { uint32_t v = 0; return readAll(a, v); }
    int32_t readLong(int a) { int32_t v = 0; return readAll(a, v); }
    uint32_t readULong(int a) { uint32_t v = 0; return readAll(a, v); }
    int64_t readLong64(int a) { int64_t v = 0; return readAll(a, v); }
    uint64_t readULong64(int a) { uint64_t v = 0; return readAll(a, v); }
    float readFloat(int a) { float v = 0; return readAll(a, v); }
    double readDouble(int a) { double v = 0; return readAll(a, v); }
    size_t readBytes(int a, void *valor, size_t maximo)
    {
        if (!valor || !maximo || a < 0 || a + maximo > length()) return 0;
        uint8_t *p = (uint8_t *)valor;
        for (size_t i = 0; i < maximo; i++) p[i] = read(a + (int)i);
        return maximo;
    }
    size_t writeChar(int a, int8_t v) { return writeAll(a, v); }
    size_t writeUChar(int a, uint8_t v) { return writeAll(a, v); }
    size_t writeBool(int a, bool v) { return writeAll(a, (uint8_t)(v ? 1 : 0)); }
    size_t writeShort(int a, int16_t v) { return writeAll(a, v); }
    size_t writeUShort(int a, uint16_t v) { return writeAll(a, v); }
    size_t writeInt(int a, int32_t v) { return writeAll(a, v); }
    size_t writeUInt(int a, uint32_t v) { return writeAll(a, v); }
    size_t writeLong(int a, int32_t v) { return writeAll(a, v); }
    size_t writeULong(int a, uint32_t v) { return writeAll(a, v); }
    size_t writeLong64(int a, int64_t v) { return writeAll(a, v); }
    size_t writeULong64(int a, uint64_t v) { return writeAll(a, v); }
    size_t writeFloat(int a, float v) { return writeAll(a, v); }
    size_t writeDouble(int a, double v) { return writeAll(a, v); }
    size_t writeBytes(int a, const void *valor, size_t n)
    {
        if (!valor || !n || a < 0 || a + n > length()) return 0;
        const uint8_t *p = (const uint8_t *)valor;
        for (size_t i = 0; i < n; i++) update(a + (int)i, p[i]);
        return n;
    }
    /* Os bytes em memoria, como na placa: quem escreve neles confirma com o commit(). */
    uint8_t *getDataPtr()
    {
        size_t n = length();
        if (!n) return nullptr;
        if (!copia) copia = (uint8_t *)calloc(n, 1);
        readBytes(0, copia, n);
        return copia;
    }
    bool isDirty()
    {
        if (!copia) return false;
        for (size_t i = 0, n = length(); i < n; i++)
            if (copia[i] != read((int)i)) return true;
        return false;
    }
    /* Aqui nao existe a particao "eeprom" antiga para migrar para a NVS. */
    uint16_t convert(bool, const char * = "eeprom", const char * = "eeprom") { return 0; }

private:
    uint8_t *copia = nullptr;
};

inline uint8_t EERef::operator*() const { return (uint8_t)cs_obj_i("EEPROM", "read", index); }
inline EERef &EERef::operator=(uint8_t in)
{
    cs_obj_n("EEPROM", "write", index, in);
    return *this;
}

static EEPROMClass EEPROM;

#endif
