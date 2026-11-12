/* OneWire.h -- o fio de 1-Wire do circuito (a biblioteca OneWire de Paul Stoffregen).
 *
 * Os bits passam no fio que o simulador guarda para o pino (BarramentoUmFio):
 * o reset com a presenca, a leitura e a escrita de cada bit, a busca da ROM.
 * Os DS18B20 pendurados nele respondem como o chip. O CRC e conta: fica aqui. */
#ifndef OneWire_h
#define OneWire_h

#include "cs_objeto.h"

class OneWire : public CsObjeto {
public:
    OneWire() {}
    OneWire(uint8_t pin) { cs_criar("OneWire", pin); }
    void begin(uint8_t pin)
    {
        if (cs_nome[0]) cs_obj_n(cs_nome, "begin", pin);
        else cs_criar("OneWire", pin);
    }

    uint8_t reset(void) { return (uint8_t)cs_obj_i(cs_nome, "reset"); }
    void select(const uint8_t rom[8]) { cs_obj_n(cs_nome, "select", cs_vetor(rom, 8)); }
    void skip(void) { cs_obj_n(cs_nome, "skip"); }
    void write(uint8_t v, uint8_t power = 0) { (void)power; cs_obj_n(cs_nome, "write", v); }
    void write_bytes(const uint8_t *buf, uint16_t count, bool power = 0)
    {
        (void)power;
        cs_obj_n(cs_nome, "write_bytes", cs_vetor(buf, count), (int)count);
    }
    uint8_t read(void) { return (uint8_t)cs_obj_i(cs_nome, "read"); }
    void read_bytes(uint8_t *buf, uint16_t count) { cs_obj_n(cs_nome, "read_bytes", cs_vetor_mutavel(buf, count), (int)count); }
    void write_bit(uint8_t v) { cs_obj_n(cs_nome, "write_bit", v); }
    uint8_t read_bit(void) { return (uint8_t)cs_obj_i(cs_nome, "read_bit"); }
    void depower(void) {}

    void reset_search() { cs_obj_n(cs_nome, "reset_search"); }
    void target_search(uint8_t family_code) { cs_obj_n(cs_nome, "target_search", family_code); }
    bool search(uint8_t *newAddr, bool search_mode = true)
    {
        return cs_obj_i(cs_nome, "search", cs_vetor_mutavel(newAddr, 8), search_mode) != 0;
    }

    /* O CRC-8 da Maxim: o do fim da ROM e do scratchpad. */
    static uint8_t crc8(const uint8_t *addr, uint8_t len)
    {
        uint8_t crc = 0;
        while (len--) {
            uint8_t b = *addr++;
            for (uint8_t i = 8; i; i--) {
                uint8_t mistura = (crc ^ b) & 0x01;
                crc >>= 1;
                if (mistura) crc ^= 0x8C;
                b >>= 1;
            }
        }
        return crc;
    }

    /* O CRC-16 dos chips que mandam dois bytes de CRC (o DS2408, as memorias). */
    static uint16_t crc16(const uint8_t *input, uint16_t len, uint16_t crc = 0)
    {
        static const uint8_t oddparity[16] = {0, 1, 1, 0, 1, 0, 0, 1, 1, 0, 0, 1, 0, 1, 1, 0};
        for (uint16_t i = 0; i < len; i++) {
            uint16_t cdata = input[i];
            cdata = (cdata ^ crc) & 0xff;
            crc >>= 8;
            if (oddparity[cdata & 0x0F] ^ oddparity[cdata >> 4]) crc ^= 0xC001;
            cdata <<= 6;
            crc ^= cdata;
            cdata <<= 1;
            crc ^= cdata;
        }
        return crc;
    }
    static bool check_crc16(const uint8_t *input, uint16_t len, const uint8_t *inverted_crc, uint16_t crc = 0)
    {
        crc = ~crc16(input, len, crc);
        return (crc & 0xFF) == inverted_crc[0] && (crc >> 8) == inverted_crc[1];
    }
};

#endif
