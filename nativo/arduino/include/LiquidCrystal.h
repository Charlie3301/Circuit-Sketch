/* LiquidCrystal.h -- o LCD de caracteres (HD44780) do circuito. O texto
 * passa pelo Print do core (o mesmo formato de numero da placa). */
#ifndef LiquidCrystal_h
#define LiquidCrystal_h

#include "cs_objeto.h"

#define LCD_5x10DOTS 0x04
#define LCD_5x8DOTS 0x00

class LiquidCrystal : public Print, public CsObjeto {
protected:
    LiquidCrystal(int, const char *classe) { (void)classe; }

public:
    template <class... A> LiquidCrystal(A... a) { cs_criar("LiquidCrystal", a...); }

    CS_M_NADA(begin)
    CS_M_NADA(clear)
    CS_M_NADA(home)
    CS_M_NADA(noDisplay)
    CS_M_NADA(display)
    CS_M_NADA(noBlink)
    CS_M_NADA(blink)
    CS_M_NADA(noCursor)
    CS_M_NADA(cursor)
    CS_M_NADA(scrollDisplayLeft)
    CS_M_NADA(scrollDisplayRight)
    CS_M_NADA(leftToRight)
    CS_M_NADA(rightToLeft)
    CS_M_NADA(autoscroll)
    CS_M_NADA(noAutoscroll)
    CS_M_NADA(command)
    void setCursor(uint8_t col, uint8_t row) { cs_obj_n(cs_nome, "setCursor", col, row); }
    void createChar(uint8_t location, const uint8_t charmap[]) { cs_obj_n(cs_nome, "createChar", location, cs_vetor(charmap, 8)); }
    void createChar(uint8_t location, const char charmap[]) { createChar(location, (const uint8_t *)charmap); }
    void createChar(int location, const uint8_t charmap[]) { createChar((uint8_t)location, charmap); }

    size_t write(uint8_t c) override
    {
        cs_obj_n(cs_nome, "write", (int)c);
        return 1;
    }
    size_t write(const uint8_t *b, size_t n) override
    {
        for (size_t i = 0; i < n; i++)
            if (b[i] < 8) {
                for (size_t k = 0; k < n; k++) write(b[k]);
                return n;
            }
        cs_obj_n(cs_nome, "print", cs_tn((const char *)b, (int)n));
        return n;
    }
    using Print::write;
};

#endif
