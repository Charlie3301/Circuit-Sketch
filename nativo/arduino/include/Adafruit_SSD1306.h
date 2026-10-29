/* Adafruit_SSD1306.h -- o OLED SSD1306 do circuito (I2C ou SPI). O buffer, o
 * display() que manda para o painel e o logotipo do begin() sao os do
 * simulador, como no sketch interpretado. */
#ifndef _Adafruit_SSD1306_H_
#define _Adafruit_SSD1306_H_

#include "Adafruit_GFX.h"
#include "Wire.h"
#include "SPI.h"

#define BLACK 0
#define WHITE 1
#define INVERSE 2
#define SSD1306_BLACK 0
#define SSD1306_WHITE 1
#define SSD1306_INVERSE 2
#define SSD1306_MEMORYMODE 0x20
#define SSD1306_COLUMNADDR 0x21
#define SSD1306_PAGEADDR 0x22
#define SSD1306_SETCONTRAST 0x81
#define SSD1306_CHARGEPUMP 0x8D
#define SSD1306_SEGREMAP 0xA0
#define SSD1306_DISPLAYALLON_RESUME 0xA4
#define SSD1306_DISPLAYALLON 0xA5
#define SSD1306_NORMALDISPLAY 0xA6
#define SSD1306_INVERTDISPLAY 0xA7
#define SSD1306_SETMULTIPLEX 0xA8
#define SSD1306_DISPLAYOFF 0xAE
#define SSD1306_DISPLAYON 0xAF
#define SSD1306_COMSCANINC 0xC0
#define SSD1306_COMSCANDEC 0xC8
#define SSD1306_SETDISPLAYOFFSET 0xD3
#define SSD1306_SETDISPLAYCLOCKDIV 0xD5
#define SSD1306_SETPRECHARGE 0xD9
#define SSD1306_SETCOMPINS 0xDA
#define SSD1306_SETVCOMDETECT 0xDB
#define SSD1306_SETLOWCOLUMN 0x00
#define SSD1306_SETHIGHCOLUMN 0x10
#define SSD1306_SETSTARTLINE 0x40
#define SSD1306_EXTERNALVCC 0x01
#define SSD1306_SWITCHCAPVCC 0x02
#define SSD1306_RIGHT_HORIZONTAL_SCROLL 0x26
#define SSD1306_LEFT_HORIZONTAL_SCROLL 0x27
#define SSD1306_VERTICAL_AND_RIGHT_HORIZONTAL_SCROLL 0x29
#define SSD1306_VERTICAL_AND_LEFT_HORIZONTAL_SCROLL 0x2A
#define SSD1306_DEACTIVATE_SCROLL 0x2E
#define SSD1306_ACTIVATE_SCROLL 0x2F
#define SSD1306_SET_VERTICAL_SCROLL_AREA 0xA3

class Adafruit_SSD1306 : public Adafruit_GFX {
public:
    Adafruit_SSD1306(uint8_t w, uint8_t h, TwoWire *twi = &Wire, int8_t rst_pin = -1, uint32_t clkDuring = 400000UL,
                     uint32_t clkAfter = 100000UL)
        : Adafruit_GFX(w, h)
    {
        (void)clkDuring;
        (void)clkAfter;
        cs_criar("Adafruit_SSD1306", w, h, twi, rst_pin);
    }
    Adafruit_SSD1306(uint8_t w, uint8_t h, int8_t mosi_pin, int8_t sclk_pin, int8_t dc_pin, int8_t rst_pin, int8_t cs_pin)
        : Adafruit_GFX(w, h)
    {
        cs_criar("Adafruit_SSD1306", w, h, mosi_pin, sclk_pin, dc_pin, rst_pin, cs_pin);
    }
    Adafruit_SSD1306(uint8_t w, uint8_t h, SPIClass *spi, int8_t dc_pin, int8_t rst_pin, int8_t cs_pin,
                     uint32_t bitrate = 8000000UL)
        : Adafruit_GFX(w, h)
    {
        (void)bitrate;
        cs_criar("Adafruit_SSD1306", w, h, spi, dc_pin, rst_pin, cs_pin);
    }
    Adafruit_SSD1306(int8_t rst_pin = -1) : Adafruit_GFX(128, 64) { cs_criar("Adafruit_SSD1306", 128, 64, &Wire, rst_pin); }

    bool begin(uint8_t switchvcc = SSD1306_SWITCHCAPVCC, uint8_t i2caddr = 0, bool reset = true, bool periphBegin = true)
    {
        (void)reset;
        (void)periphBegin;
        return cs_obj_i(cs_nome, "begin", switchvcc, i2caddr) != 0;
    }
    CS_M_NADA(display)
    CS_M_NADA(clearDisplay)
    CS_M_NADA(dim)
    CS_M_NADA(startscrollright)
    CS_M_NADA(startscrollleft)
    CS_M_NADA(startscrolldiagright)
    CS_M_NADA(startscrolldiagleft)
    CS_M_NADA(stopscroll)
    CS_M_NADA(ssd1306_command)
    bool getPixel(int16_t x, int16_t y) { return cs_obj_i(cs_nome, "getPixel", x, y) != 0; }
};

#endif
