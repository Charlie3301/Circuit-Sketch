/* Modificado (CircuitSketch): os modos de pino e de interrupcao so valem se o cs_placa.h (os numeros do simulador) nao os definiu. */
/*
 Arduino.h - Main include file for the Arduino SDK
 Copyright (c) 2005-2013 Arduino Team.  All right reserved.

 This library is free software; you can redistribute it and/or
 modify it under the terms of the GNU Lesser General Public
 License as published by the Free Software Foundation; either
 version 2.1 of the License, or (at your option) any later version.

 This library is distributed in the hope that it will be useful,
 but WITHOUT ANY WARRANTY; without even the implied warranty of
 MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
 Lesser General Public License for more details.

 You should have received a copy of the GNU Lesser General Public
 License along with this library; if not, write to the Free Software
 Foundation, Inc., 51 Franklin St, Fifth Floor, Boston, MA  02110-1301  USA
 */

#ifndef MAIN_ESP32_HAL_GPIO_H_
#define MAIN_ESP32_HAL_GPIO_H_

#ifdef __cplusplus
extern "C" {
#endif

#include "pins_arduino.h"
#include "esp32-hal.h"
#include "soc/soc_caps.h"
#include "driver/gpio.h"

#if (CONFIG_IDF_TARGET_ESP32S2 || CONFIG_IDF_TARGET_ESP32S3)
#define NUM_OUPUT_PINS 46
#define PIN_DAC1       17
#define PIN_DAC2       18
#else
#define NUM_OUPUT_PINS 34
#define PIN_DAC1       25
#define PIN_DAC2       26
#endif

#ifndef LOW
#define LOW 0x0
#endif
#ifndef HIGH
#define HIGH 0x1
#endif

//GPIO FUNCTIONS
#ifndef INPUT
#define INPUT 0x01
#endif
// Changed OUTPUT from 0x02 to behave the same as Arduino pinMode(pin,OUTPUT)
// where you can read the state of pin even when it is set as OUTPUT
#ifndef OUTPUT
#define OUTPUT 0x03
#endif
#ifndef PULLUP
#define PULLUP 0x04
#endif
#ifndef INPUT_PULLUP
#define INPUT_PULLUP 0x05
#endif
#ifndef PULLDOWN
#define PULLDOWN 0x08
#endif
#ifndef INPUT_PULLDOWN
#define INPUT_PULLDOWN 0x09
#endif
#ifndef OPEN_DRAIN
#define OPEN_DRAIN 0x10
#endif
#ifndef OUTPUT_OPEN_DRAIN
#define OUTPUT_OPEN_DRAIN 0x13
#endif
#ifndef ANALOG
#define ANALOG 0xC0
#endif

//Interrupt Modes
#ifndef DISABLED
#define DISABLED 0x00
#endif
#ifndef RISING
#define RISING 0x01
#endif
#ifndef FALLING
#define FALLING 0x02
#endif
#ifndef CHANGE
#define CHANGE 0x03
#endif
#ifndef ONLOW
#define ONLOW 0x04
#endif
#ifndef ONHIGH
#define ONHIGH 0x05
#endif
#ifndef ONLOW_WE
#define ONLOW_WE 0x0C
#endif
#ifndef ONHIGH_WE
#define ONHIGH_WE 0x0D
#endif

#define digitalPinIsValid(pin)   GPIO_IS_VALID_GPIO(pin)
#define digitalPinCanOutput(pin) GPIO_IS_VALID_OUTPUT_GPIO(pin)

#define digitalPinToRtcPin(pin)     ((RTC_GPIO_IS_VALID_GPIO(pin)) ? rtc_io_number_get(pin) : -1)
#define digitalPinToDacChannel(pin) (((pin) == DAC_CHANNEL_1_GPIO_NUM) ? 0 : ((pin) == DAC_CHANNEL_2_GPIO_NUM) ? 1 : -1)

void pinMode(uint8_t pin, uint8_t mode);
void digitalWrite(uint8_t pin, uint8_t val);
int digitalRead(uint8_t pin);

void attachInterrupt(uint8_t pin, void (*)(void), int mode);
void attachInterruptArg(uint8_t pin, void (*)(void *), void *arg, int mode);
void detachInterrupt(uint8_t pin);
void enableInterrupt(uint8_t pin);
void disableInterrupt(uint8_t pin);

int8_t digitalPinToTouchChannel(uint8_t pin);
int8_t digitalPinToAnalogChannel(uint8_t pin);
int8_t analogChannelToDigitalPin(uint8_t channel);

#ifdef __cplusplus
}
#endif

#endif /* MAIN_ESP32_HAL_GPIO_H_ */
