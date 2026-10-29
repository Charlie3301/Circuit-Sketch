/* ESP32Servo.h -- a ESP32Servo: a mesma Servo, com o setPeriodHertz e o
 * ESP32PWM (os timers do LEDC, que aqui nao precisam ser reservados). */
#ifndef ESP32Servo_h
#define ESP32Servo_h

#include "Servo.h"

class ESP32PWM {
public:
    static void allocateTimer(int) {}
    static bool hasPwm(int) { return true; }
};

#endif
