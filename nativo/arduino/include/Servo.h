/* Servo.h -- a Servo do Arduino: o servo do circuito, pelo simulador.
 * write() ate 543 e angulo; de 544 para cima e o pulso em us (como a de verdade). */
#ifndef Servo_h
#define Servo_h

#include "cs_objeto.h"

#define MIN_PULSE_WIDTH 544
#define MAX_PULSE_WIDTH 2400
#define DEFAULT_PULSE_WIDTH 1500
#define REFRESH_INTERVAL 20000
#define SERVOS_PER_TIMER 12
#define MAX_SERVOS 12
#define INVALID_SERVO 255

class Servo : public CsObjeto {
public:
    Servo() { cs_criar("Servo"); }
    uint8_t attach(int pin) { return (uint8_t)cs_obj_i(cs_nome, "attach", pin); }
    uint8_t attach(int pin, int min, int max) { return (uint8_t)cs_obj_i(cs_nome, "attach", pin, min, max); }
    void detach() { cs_obj_n(cs_nome, "detach"); }
    template <class T, class = typename std::enable_if<std::is_arithmetic<T>::value>::type>
    void write(T value) { cs_obj_n(cs_nome, "write", value); }
    void writeMicroseconds(int value) { cs_obj_n(cs_nome, "writeMicroseconds", value); }
    int read() { return (int)cs_obj_i(cs_nome, "read"); }
    int readMicroseconds() { return (int)cs_obj_i(cs_nome, "readMicroseconds"); }
    bool attached() { return cs_obj_i(cs_nome, "attached") != 0; }
    void setPeriodHertz(int hertz) { cs_obj_n(cs_nome, "setPeriodHertz", hertz); }
};

#endif
