/* Adafruit_SH110X.h -- o OLED de 1,3" (SH1106) do circuito. */
#ifndef _Adafruit_SH110X_H_
#define _Adafruit_SH110X_H_

#include "Adafruit_GFX.h"
#include "Wire.h"

#define SH110X_BLACK 0
#define SH110X_WHITE 1
#define SH110X_INVERSE 2

class Adafruit_SH1106G : public Adafruit_GFX {
public:
    Adafruit_SH1106G(uint16_t w, uint16_t h, TwoWire *twi = &Wire, int8_t rst_pin = -1, uint32_t preclk = 400000,
                     uint32_t postclk = 100000)
        : Adafruit_GFX(w, h)
    {
        (void)preclk;
        (void)postclk;
        cs_criar("Adafruit_SH1106G", w, h, twi, rst_pin);
    }
    bool begin(uint8_t i2caddr = 0x3C, bool reset = true) { return cs_obj_i(cs_nome, "begin", i2caddr, reset) != 0; }
    CS_M_NADA(display)
    CS_M_NADA(clearDisplay)
    CS_M_NADA(setContrast)
    bool getPixel(int16_t x, int16_t y) { return cs_obj_i(cs_nome, "getPixel", x, y) != 0; }
};

typedef Adafruit_SH1106G Adafruit_SH1107;

#endif
