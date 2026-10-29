/* Adafruit_ST7735.h -- o TFT de 1,8" (128 x 160) do circuito. */
#ifndef _ADAFRUIT_ST7735H_
#define _ADAFRUIT_ST7735H_

#include "Adafruit_ST77xx.h"

class Adafruit_ST7735 : public Adafruit_ST77xx {
public:
    Adafruit_ST7735(int8_t cs, int8_t dc, int8_t mosi, int8_t sclk, int8_t rst) : Adafruit_ST77xx(128, 160)
    {
        cs_criar("Adafruit_ST7735", cs, dc, mosi, sclk, rst);
    }
    Adafruit_ST7735(int8_t cs, int8_t dc, int8_t rst) : Adafruit_ST77xx(128, 160) { cs_criar("Adafruit_ST7735", cs, dc, rst); }
    Adafruit_ST7735(SPIClass *spi, int8_t cs, int8_t dc, int8_t rst) : Adafruit_ST77xx(128, 160)
    {
        cs_criar("Adafruit_ST7735", spi, cs, dc, rst);
    }
    void initB(void) { cs_obj_n(cs_nome, "initB"); }
    void initR(uint8_t options = INITR_GREENTAB) { cs_obj_n(cs_nome, "initR", options); }
};

#endif
