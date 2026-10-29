/* LiquidCrystal_I2C.h -- o LCD com o adaptador I2C (o PCF8574): o mesmo LCD,
 * com o endereco, o init() e a luz de fundo. */
#ifndef LiquidCrystal_I2C_h
#define LiquidCrystal_I2C_h

#include "LiquidCrystal.h"
#include "Wire.h"

class LiquidCrystal_I2C : public LiquidCrystal {
public:
    LiquidCrystal_I2C(uint8_t addr, uint8_t cols, uint8_t rows) : LiquidCrystal(0, "")
    {
        cs_criar("LiquidCrystal_I2C", addr, cols, rows);
    }
    LiquidCrystal_I2C(uint8_t addr, uint8_t cols, uint8_t rows, uint8_t charsize) : LiquidCrystal(0, "")
    {
        (void)charsize;
        cs_criar("LiquidCrystal_I2C", addr, cols, rows);
    }

    CS_M_NADA(init)
    CS_M_NADA(backlight)
    CS_M_NADA(noBacklight)
    CS_M_NADA(setBacklight)
    void begin() { cs_obj_n(cs_nome, "begin"); }
    template <class... A> void begin(A... a) { cs_obj_n(cs_nome, "begin", a...); }
    void printstr(const char *c) { print(c); }
    void load_custom_character(uint8_t n, uint8_t *rows) { createChar(n, rows); }
};

#endif
