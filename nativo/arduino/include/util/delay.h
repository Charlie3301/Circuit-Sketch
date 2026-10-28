/* _delay_ms e _delay_us do avr-libc: a espera do core. */
#ifndef CS_UTIL_DELAY_H
#define CS_UTIL_DELAY_H
#include "../Arduino.h"
#define _delay_ms(ms) delay((unsigned long)(ms))
#define _delay_us(us) delayMicroseconds((unsigned int)(us))
#endif
