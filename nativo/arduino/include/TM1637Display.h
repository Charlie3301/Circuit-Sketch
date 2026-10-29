/* TM1637Display.h -- o display de 4 digitos com o TM1637 (a do avishorp). */
#ifndef __TM1637DISPLAY__
#define __TM1637DISPLAY__

#include "cs_objeto.h"

#define SEG_A 0b00000001
#define SEG_B 0b00000010
#define SEG_C 0b00000100
#define SEG_D 0b00001000
#define SEG_E 0b00010000
#define SEG_F 0b00100000
#define SEG_G 0b01000000
#define SEG_DP 0b10000000
#define DEFAULT_BIT_DELAY 100

class TM1637Display : public CsObjeto {
public:
    TM1637Display(uint8_t pinClk, uint8_t pinDIO, unsigned int bitDelay = DEFAULT_BIT_DELAY)
    {
        (void)bitDelay;
        cs_criar("TM1637Display", pinClk, pinDIO);
    }
    void setBrightness(uint8_t brightness, bool on = true) { cs_obj_n(cs_nome, "setBrightness", brightness, on); }
    void setSegments(const uint8_t segments[], uint8_t length = 4, uint8_t pos = 0)
    {
        cs_obj_n(cs_nome, "setSegments", cs_vetor(segments, length), length, pos);
    }
    void clear() { cs_obj_n(cs_nome, "clear"); }
    void showNumberDec(int num, bool leading_zero = false, uint8_t length = 4, uint8_t pos = 0)
    {
        cs_obj_n(cs_nome, "showNumberDec", num, leading_zero, length, pos);
    }
    void showNumberDecEx(int num, uint8_t dots = 0, bool leading_zero = false, uint8_t length = 4, uint8_t pos = 0)
    {
        cs_obj_n(cs_nome, "showNumberDecEx", num, dots, leading_zero, length, pos);
    }
    void showNumberHexEx(uint16_t num, uint8_t dots = 0, bool leading_zero = false, uint8_t length = 4, uint8_t pos = 0)
    {
        cs_obj_n(cs_nome, "showNumberHexEx", num, dots, leading_zero, length, pos);
    }
    static uint8_t encodeDigit(uint8_t digit)
    {
        static const uint8_t digitos[] = {0x3f, 0x06, 0x5b, 0x4f, 0x66, 0x6d, 0x7d, 0x07,
                                          0x7f, 0x6f, 0x77, 0x7c, 0x39, 0x5e, 0x79, 0x71};
        return digitos[digit & 0x0f];
    }
};

#endif
