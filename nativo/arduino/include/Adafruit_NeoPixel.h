/* Adafruit_NeoPixel.h -- a fita/anel/matriz de WS2812 do circuito.
 * As contas estaticas (Color, ColorHSV, gamma, seno) sao as do simulador. */
#ifndef ADAFRUIT_NEOPIXEL_H
#define ADAFRUIT_NEOPIXEL_H

#include "cs_objeto.h"

#define NEO_RGB ((0 << 6) | (0 << 4) | (1 << 2) | (2))
#define NEO_RBG ((0 << 6) | (0 << 4) | (2 << 2) | (1))
#define NEO_GRB ((1 << 6) | (1 << 4) | (0 << 2) | (2))
#define NEO_GBR ((2 << 6) | (2 << 4) | (0 << 2) | (1))
#define NEO_BRG ((1 << 6) | (1 << 4) | (2 << 2) | (0))
#define NEO_BGR ((2 << 6) | (2 << 4) | (1 << 2) | (0))
#define NEO_WRGB ((0 << 6) | (1 << 4) | (2 << 2) | (3))
#define NEO_RGBW ((3 << 6) | (0 << 4) | (1 << 2) | (2))
#define NEO_GRBW ((3 << 6) | (1 << 4) | (0 << 2) | (2))
#define NEO_KHZ800 0x0000
#define NEO_KHZ400 0x0100

typedef uint16_t neoPixelType;

class Adafruit_NeoPixel : public CsObjeto {
    static Adafruit_NeoPixel &conta();
    explicit Adafruit_NeoPixel(int, int) { cs_criar("Adafruit_NeoPixel", 1, 0); }

public:
    Adafruit_NeoPixel(uint16_t n, int16_t pin = 6, neoPixelType type = NEO_GRB + NEO_KHZ800)
    {
        cs_criar("Adafruit_NeoPixel", n, pin, type);
    }
    Adafruit_NeoPixel() { cs_criar("Adafruit_NeoPixel"); }

    CS_M_NADA(begin)
    CS_M_NADA(show)
    CS_M_NADA(clear)
    CS_M_NADA(setPin)
    CS_M_NADA(updateType)
    CS_M_NADA(updateLength)
    CS_M_NADA(rainbow)
    CS_M_BOOL(canShow)
    void setPixelColor(uint16_t n, uint8_t r, uint8_t g, uint8_t b) { cs_obj_n(cs_nome, "setPixelColor", n, r, g, b); }
    void setPixelColor(uint16_t n, uint8_t r, uint8_t g, uint8_t b, uint8_t w) { cs_obj_n(cs_nome, "setPixelColor", n, r, g, b, w); }
    void setPixelColor(uint16_t n, uint32_t c) { cs_obj_n(cs_nome, "setPixelColor", n, c); }
    void fill(uint32_t c = 0, uint16_t first = 0, uint16_t count = 0) { cs_obj_n(cs_nome, "fill", c, first, count); }
    void setBrightness(uint8_t b) { cs_obj_n(cs_nome, "setBrightness", b); }
    uint8_t getBrightness() { return (uint8_t)cs_obj_i(cs_nome, "getBrightness"); }
    uint16_t numPixels() { return (uint16_t)cs_obj_i(cs_nome, "numPixels"); }
    uint32_t getPixelColor(uint16_t n) { return (uint32_t)cs_obj_i(cs_nome, "getPixelColor", n); }
    int16_t getPin() { return (int16_t)cs_obj_i(cs_nome, "getPin"); }

    static uint32_t Color(uint8_t r, uint8_t g, uint8_t b) { return ((uint32_t)r << 16) | ((uint32_t)g << 8) | b; }
    static uint32_t Color(uint8_t r, uint8_t g, uint8_t b, uint8_t w)
    {
        return ((uint32_t)w << 24) | ((uint32_t)r << 16) | ((uint32_t)g << 8) | b;
    }
    static uint32_t ColorHSV(uint16_t hue, uint8_t sat = 255, uint8_t val = 255)
    {
        return (uint32_t)cs_obj_i(conta().cs_nome, "ColorHSV", hue, sat, val);
    }
    static uint32_t gamma32(uint32_t x) { return (uint32_t)cs_obj_i(conta().cs_nome, "gamma32", x); }
    static uint8_t gamma8(uint8_t x) { return (uint8_t)cs_obj_i(conta().cs_nome, "gamma8", x); }
    static uint8_t sine8(uint8_t x) { return (uint8_t)cs_obj_i(conta().cs_nome, "sine8", x); }
};

/* Um objeto do simulador so para as contas estaticas (sem fita no circuito). */
inline Adafruit_NeoPixel &Adafruit_NeoPixel::conta()
{
    static Adafruit_NeoPixel c(0, 0);
    return c;
}

#endif
