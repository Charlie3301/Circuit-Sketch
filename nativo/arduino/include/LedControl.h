/* LedControl.h -- a matriz 8x8 (e os 7 segmentos) com o MAX7219, encadeada
 * pelo DOUT como na biblioteca do Eberhard Fahle. */
#ifndef LedControl_h
#define LedControl_h

#include "cs_objeto.h"

class LedControl : public CsObjeto {
public:
    LedControl(int dataPin, int clkPin, int csPin, int numDevices = 1) { cs_criar("LedControl", dataPin, clkPin, csPin, numDevices); }
    int getDeviceCount() { return (int)cs_obj_i(cs_nome, "getDeviceCount"); }
    void shutdown(int addr, bool status) { cs_obj_n(cs_nome, "shutdown", addr, status); }
    void setScanLimit(int addr, int limit) { cs_obj_n(cs_nome, "setScanLimit", addr, limit); }
    void setIntensity(int addr, int intensity) { cs_obj_n(cs_nome, "setIntensity", addr, intensity); }
    void clearDisplay(int addr) { cs_obj_n(cs_nome, "clearDisplay", addr); }
    void setLed(int addr, int row, int col, boolean state) { cs_obj_n(cs_nome, "setLed", addr, row, col, state); }
    void setRow(int addr, int row, byte value) { cs_obj_n(cs_nome, "setRow", addr, row, value); }
    void setColumn(int addr, int col, byte value) { cs_obj_n(cs_nome, "setColumn", addr, col, value); }
    void setDigit(int addr, int digit, byte value, boolean dp) { cs_obj_n(cs_nome, "setDigit", addr, digit, value, dp); }
    void setChar(int addr, int digit, char value, boolean dp) { cs_obj_n(cs_nome, "setChar", addr, digit, value, dp); }
};

#endif
