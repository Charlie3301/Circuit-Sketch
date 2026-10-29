/* Keypad.h -- o teclado de membrana do circuito (a Keypad de Mark Stanley e
 * Alexander Brevig). O mapa e os pinos vao como vetores. */
#ifndef KEYPAD_H
#define KEYPAD_H

#include "cs_objeto.h"

#define makeKeymap(x) ((char *)x)
#define NO_KEY '\0'

typedef enum { IDLE, PRESSED, HOLD, RELEASED } KeyState;
typedef char KeypadEvent;

class Keypad : public CsObjeto {
public:
    Keypad(char *userKeymap, byte *row, byte *col, byte numRows, byte numCols)
    {
        cs_criar("Keypad", cs_vetor(userKeymap, numRows * numCols), cs_vetor(row, numRows), cs_vetor(col, numCols),
                 numRows, numCols);
    }
    char getKey() { return (char)cs_obj_i(cs_nome, "getKey"); }
    char waitForKey() { return (char)cs_obj_i(cs_nome, "waitForKey"); }
    KeyState getState() { return (KeyState)cs_obj_i(cs_nome, "getState"); }
    bool isPressed(char keyChar) { return cs_obj_i(cs_nome, "isPressed", keyChar) != 0; }
    bool keyStateChanged() { return cs_obj_i(cs_nome, "keyStateChanged") != 0; }
    void setDebounceTime(unsigned int debounce) { cs_obj_n(cs_nome, "setDebounceTime", debounce); }
    void setHoldTime(unsigned int hold) { cs_obj_n(cs_nome, "setHoldTime", hold); }
    void begin(char *userKeymap) { (void)userKeymap; }
};

#endif
