/* Stepper.h -- o motor de passo do circuito (a Stepper do Arduino). */
#ifndef Stepper_h
#define Stepper_h

#include "cs_objeto.h"

class Stepper : public CsObjeto {
public:
    Stepper(int number_of_steps, int motor_pin_1, int motor_pin_2)
    {
        cs_criar("Stepper", number_of_steps, motor_pin_1, motor_pin_2);
    }
    Stepper(int number_of_steps, int motor_pin_1, int motor_pin_2, int motor_pin_3, int motor_pin_4)
    {
        cs_criar("Stepper", number_of_steps, motor_pin_1, motor_pin_2, motor_pin_3, motor_pin_4);
    }
    Stepper(int number_of_steps, int motor_pin_1, int motor_pin_2, int motor_pin_3, int motor_pin_4, int motor_pin_5)
    {
        cs_criar("Stepper", number_of_steps, motor_pin_1, motor_pin_2, motor_pin_3, motor_pin_4, motor_pin_5);
    }
    void setSpeed(long whatSpeed) { cs_obj_n(cs_nome, "setSpeed", whatSpeed); }
    void step(int number_of_steps) { cs_obj_n(cs_nome, "step", number_of_steps); }
    int version(void) { return (int)cs_obj_i(cs_nome, "version"); }
};

#endif
