/* Adafruit_ILI9341.h -- o TFT de 2,2" (240 x 320) do circuito. */
#ifndef _ADAFRUIT_ILI9341H_
#define _ADAFRUIT_ILI9341H_

#include "Adafruit_GFX.h"
#include "SPI.h"

#define ILI9341_TFTWIDTH 240
#define ILI9341_TFTHEIGHT 320
#define ILI9341_BLACK 0x0000
#define ILI9341_NAVY 0x000F
#define ILI9341_DARKGREEN 0x03E0
#define ILI9341_DARKCYAN 0x03EF
#define ILI9341_MAROON 0x7800
#define ILI9341_PURPLE 0x780F
#define ILI9341_OLIVE 0x7BE0
#define ILI9341_LIGHTGREY 0xC618
#define ILI9341_DARKGREY 0x7BEF
#define ILI9341_BLUE 0x001F
#define ILI9341_GREEN 0x07E0
#define ILI9341_CYAN 0x07FF
#define ILI9341_RED 0xF800
#define ILI9341_MAGENTA 0xF81F
#define ILI9341_YELLOW 0xFFE0
#define ILI9341_WHITE 0xFFFF
#define ILI9341_ORANGE 0xFD20
#define ILI9341_GREENYELLOW 0xAFE5
#define ILI9341_PINK 0xFC18

class Adafruit_ILI9341 : public Adafruit_GFX {
public:
    Adafruit_ILI9341(int8_t cs, int8_t dc, int8_t mosi, int8_t sclk, int8_t rst = -1, int8_t miso = -1)
        : Adafruit_GFX(ILI9341_TFTWIDTH, ILI9341_TFTHEIGHT)
    {
        cs_criar("Adafruit_ILI9341", cs, dc, mosi, sclk, rst, miso);
    }
    Adafruit_ILI9341(int8_t cs, int8_t dc, int8_t rst = -1) : Adafruit_GFX(ILI9341_TFTWIDTH, ILI9341_TFTHEIGHT)
    {
        cs_criar("Adafruit_ILI9341", cs, dc, rst);
    }
    Adafruit_ILI9341(SPIClass *spi, int8_t dc, int8_t cs = -1, int8_t rst = -1) : Adafruit_GFX(ILI9341_TFTWIDTH, ILI9341_TFTHEIGHT)
    {
        cs_criar("Adafruit_ILI9341", spi, dc, cs, rst);
    }
    void begin(uint32_t freq = 0) { if (freq) cs_obj_n(cs_nome, "begin", freq); else cs_obj_n(cs_nome, "begin"); }
    CS_M_NADA(setAddrWindow)
    CS_M_NADA(pushColor)
    CS_M_NADA(writeColor)
    CS_M_NADA(enableDisplay)
    CS_M_NADA(enableSleep)
    CS_M_NADA(setSPISpeed)
    CS_M_NADA(sendCommand)
    CS_M_INT(uint8_t, readcommand8)
};

#endif
