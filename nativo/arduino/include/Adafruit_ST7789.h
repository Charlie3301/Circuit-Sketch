/* Adafruit_ST7789.h -- o TFT IPS de 1,54" (240 x 240) do circuito. */
#ifndef _ADAFRUIT_ST7789H_
#define _ADAFRUIT_ST7789H_

#include "Adafruit_ST77xx.h"

class Adafruit_ST7789 : public Adafruit_ST77xx {
public:
    Adafruit_ST7789(int8_t cs, int8_t dc, int8_t mosi, int8_t sclk, int8_t rst = -1) : Adafruit_ST77xx(240, 320)
    {
        cs_criar("Adafruit_ST7789", cs, dc, mosi, sclk, rst);
    }
    Adafruit_ST7789(int8_t cs, int8_t dc, int8_t rst) : Adafruit_ST77xx(240, 320) { cs_criar("Adafruit_ST7789", cs, dc, rst); }
    Adafruit_ST7789(SPIClass *spi, int8_t cs, int8_t dc, int8_t rst) : Adafruit_ST77xx(240, 320)
    {
        cs_criar("Adafruit_ST7789", spi, cs, dc, rst);
    }
    void init(uint16_t width, uint16_t height, uint8_t spiMode = SPI_MODE0) { cs_obj_n(cs_nome, "init", width, height, spiMode); }
};

#endif
