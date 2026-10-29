/* Adafruit_GFX.h -- o desenho da Adafruit (pixels, linhas, formas, texto),
 * na tela do circuito: cada chamada vai ao display do simulador, que pinta
 * do mesmo jeito que pinta o sketch interpretado. */
#ifndef _ADAFRUIT_GFX_H
#define _ADAFRUIT_GFX_H

#include "cs_objeto.h"

typedef struct {
    uint16_t bitmapOffset;
    uint8_t width, height;
    uint8_t xAdvance;
    int8_t xOffset, yOffset;
} GFXglyph;

typedef struct {
    uint8_t *bitmap;
    GFXglyph *glyph;
    uint16_t first, last;
    uint8_t yAdvance;
} GFXfont;

class Adafruit_GFX : public Print, public CsObjeto {
protected:
    int16_t largura0 = 0, altura0 = 0;
    int16_t cursorX = 0, cursorY = 0;
    uint8_t tamanhoX = 1, tamanhoY = 1, rotacao = 0;

    Adafruit_GFX() {}

public:
    Adafruit_GFX(int16_t w, int16_t h) : largura0(w), altura0(h) {}

    CS_M_NADA(drawPixel)
    CS_M_NADA(drawLine)
    CS_M_NADA(drawFastHLine)
    CS_M_NADA(drawFastVLine)
    CS_M_NADA(drawRect)
    CS_M_NADA(fillRect)
    CS_M_NADA(fillScreen)
    CS_M_NADA(drawCircle)
    CS_M_NADA(fillCircle)
    CS_M_NADA(drawRoundRect)
    CS_M_NADA(fillRoundRect)
    CS_M_NADA(drawTriangle)
    CS_M_NADA(fillTriangle)
    CS_M_NADA(drawChar)
    CS_M_NADA(startWrite)
    CS_M_NADA(endWrite)
    CS_M_NADA(writePixel)
    CS_M_NADA(writeFillRect)
    CS_M_NADA(writeFastVLine)
    CS_M_NADA(writeFastHLine)
    CS_M_NADA(writeLine)
    CS_M_NADA(setTextWrap)
    CS_M_NADA(cp437)
    CS_M_NADA(invertDisplay)

    void setCursor(int16_t x, int16_t y)
    {
        cursorX = x;
        cursorY = y;
        cs_obj_n(cs_nome, "setCursor", x, y);
    }
    void setTextColor(uint16_t c) { cs_obj_n(cs_nome, "setTextColor", c); }
    void setTextColor(uint16_t c, uint16_t bg) { cs_obj_n(cs_nome, "setTextColor", c, bg); }
    void setTextSize(uint8_t s)
    {
        tamanhoX = tamanhoY = s ? s : 1;
        cs_obj_n(cs_nome, "setTextSize", s);
    }
    void setTextSize(uint8_t sx, uint8_t sy)
    {
        tamanhoX = sx ? sx : 1;
        tamanhoY = sy ? sy : 1;
        cs_obj_n(cs_nome, "setTextSize", sx, sy);
    }
    void setRotation(uint8_t r)
    {
        rotacao = r & 3;
        cs_obj_n(cs_nome, "setRotation", r);
    }
    void setFont(const GFXfont *f = NULL) { if (!f) cs_obj_n(cs_nome, "setFont"); }

    uint8_t getRotation() const { return rotacao; }
    int16_t width() { return (int16_t)cs_obj_i(cs_nome, "width"); }
    int16_t height() { return (int16_t)cs_obj_i(cs_nome, "height"); }
    int16_t getCursorX() { return (int16_t)cs_obj_i(cs_nome, "getCursorX"); }
    int16_t getCursorY() { return (int16_t)cs_obj_i(cs_nome, "getCursorY"); }

    /* A fonte padrao (6 x 8 por letra, vezes o tamanho). */
    void getTextBounds(const char *str, int16_t x, int16_t y, int16_t *x1, int16_t *y1, uint16_t *w, uint16_t *h)
    {
        int linhas = 1, maior = 0, atual = 0;
        for (const char *p = str; *p; p++) {
            if (*p == '\n') {
                linhas++;
                atual = 0;
            } else if (*p != '\r' && ++atual > maior)
                maior = atual;
        }
        *x1 = x;
        *y1 = y;
        *w = (uint16_t)(maior * 6 * tamanhoX);
        *h = (uint16_t)(linhas * 8 * tamanhoY);
    }
    void getTextBounds(const String &str, int16_t x, int16_t y, int16_t *x1, int16_t *y1, uint16_t *w, uint16_t *h)
    {
        getTextBounds(str.c_str(), x, y, x1, y1, w, h);
    }
    void getTextBounds(const __FlashStringHelper *s, int16_t x, int16_t y, int16_t *x1, int16_t *y1, uint16_t *w, uint16_t *h)
    {
        getTextBounds(reinterpret_cast<const char *>(s), x, y, x1, y1, w, h);
    }

    void drawBitmap(int16_t x, int16_t y, const uint8_t bitmap[], int16_t w, int16_t h, uint16_t color)
    {
        cs_obj_n(cs_nome, "drawBitmap", x, y, cs_vetor(bitmap, ((w + 7) / 8) * h), w, h, color);
    }
    void drawBitmap(int16_t x, int16_t y, const uint8_t bitmap[], int16_t w, int16_t h, uint16_t color, uint16_t bg)
    {
        cs_obj_n(cs_nome, "drawBitmap", x, y, cs_vetor(bitmap, ((w + 7) / 8) * h), w, h, color, bg);
    }
    void drawXBitmap(int16_t x, int16_t y, const uint8_t bitmap[], int16_t w, int16_t h, uint16_t color)
    {
        cs_obj_n(cs_nome, "drawXBitmap", x, y, cs_vetor(bitmap, ((w + 7) / 8) * h), w, h, color);
    }
    void drawRGBBitmap(int16_t x, int16_t y, const uint16_t bitmap[], int16_t w, int16_t h)
    {
        startWrite();
        for (int16_t j = 0; j < h; j++)
            for (int16_t i = 0; i < w; i++) writePixel(x + i, y + j, bitmap[j * w + i]);
        endWrite();
    }

    static uint16_t color565(uint8_t r, uint8_t g, uint8_t b)
    {
        return (uint16_t)(((r & 0xF8) << 8) | ((g & 0xFC) << 3) | (b >> 3));
    }

    CS_PRINT_PELO_SIMULADOR
};

class GFXcanvas1;

#endif
