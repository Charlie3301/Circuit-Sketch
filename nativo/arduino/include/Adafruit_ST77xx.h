/* Adafruit_ST77xx.h -- a base dos TFTs ST7735 e ST7789 (as cores ST77XX_*). */
#ifndef _ADAFRUIT_ST77XXH_
#define _ADAFRUIT_ST77XXH_

#include "Adafruit_GFX.h"
#include "SPI.h"

#define ST77XX_BLACK 0x0000
#define ST77XX_WHITE 0xFFFF
#define ST77XX_RED 0xF800
#define ST77XX_GREEN 0x07E0
#define ST77XX_BLUE 0x001F
#define ST77XX_CYAN 0x07FF
#define ST77XX_MAGENTA 0xF81F
#define ST77XX_YELLOW 0xFFE0
#define ST77XX_ORANGE 0xFC00

#define ST7735_BLACK ST77XX_BLACK
#define ST7735_WHITE ST77XX_WHITE
#define ST7735_RED ST77XX_RED
#define ST7735_GREEN ST77XX_GREEN
#define ST7735_BLUE ST77XX_BLUE
#define ST7735_CYAN ST77XX_CYAN
#define ST7735_MAGENTA ST77XX_MAGENTA
#define ST7735_YELLOW ST77XX_YELLOW
#define ST7735_ORANGE ST77XX_ORANGE

#define INITR_GREENTAB 0x00
#define INITR_REDTAB 0x01
#define INITR_BLACKTAB 0x02
#define INITR_18GREENTAB INITR_GREENTAB
#define INITR_18REDTAB INITR_REDTAB
#define INITR_18BLACKTAB INITR_BLACKTAB
#define INITR_144GREENTAB 0x101
#define INITR_MINI160x80 0x04
#define INITR_HALLOWING 0x05
#define INITR_MINI160x80_PLUGIN 0x06

class Adafruit_ST77xx : public Adafruit_GFX {
protected:
    Adafruit_ST77xx(int16_t w, int16_t h) : Adafruit_GFX(w, h) {}

public:
    CS_M_NADA(setAddrWindow)
    CS_M_NADA(pushColor)
    CS_M_NADA(writeColor)
    CS_M_NADA(enableDisplay)
    CS_M_NADA(enableSleep)
    CS_M_NADA(enableTearing)
    CS_M_NADA(setSPISpeed)
    CS_M_NADA(sendCommand)
    CS_M_NADA(setColRowStart)
};

#endif
