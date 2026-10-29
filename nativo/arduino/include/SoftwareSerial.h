/* SoftwareSerial.h -- a serial por software do AVR (e do ESP8266/RP2040):
 * uma porta do simulador nos dois pinos dados, como a Serial do chip (o
 * modulo HC-05 do carrinho, o GPS, a outra placa pelo fio). */
#ifndef SoftwareSerial_h
#define SoftwareSerial_h

#include "cs_objeto.h"

class SoftwareSerial : public HardwareSerial {
    char proprio[32];

public:
    SoftwareSerial(uint8_t receivePin, uint8_t transmitPin, bool inverse_logic = false) : HardwareSerial(proprio)
    {
        cs_val v[] = {cs_t("SoftwareSerial"), cs_i(receivePin), cs_i(transmitPin), cs_b(inverse_logic)};
        String nome = cs_texto(nullptr, "__criar", 4, v);
        snprintf(proprio, sizeof(proprio), "%s", nome.c_str());
    }
    SoftwareSerial(const SoftwareSerial &) = delete;

    void begin(long speed) { HardwareSerial::begin((unsigned long)speed); }
    bool listen() { return cs_int(proprio, "listen", 0, nullptr) != 0; }
    bool isListening() { return cs_int(proprio, "isListening", 0, nullptr) != 0; }
    bool stopListening() { return cs_int(proprio, "stopListening", 0, nullptr) != 0; }
    bool overflow() { return cs_int(proprio, "overflow", 0, nullptr) != 0; }
};

#endif
