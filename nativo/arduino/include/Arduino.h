/* Arduino.h do CircuitSketch -- o core do Arduino para o sketch compilado.
 *
 * E a API de sempre (pinMode, digitalWrite, millis, Serial, String...), com
 * a linguagem inteira por baixo: o sketch e C++ de verdade, compilado pelo
 * clang. As funcoes que mexem no hardware vao pela ponte (cs_ponte.h) ate o
 * simulador; o resto (String, Print, contas) e C++ daqui.
 *
 * As macros da placa ("ESP32", "__AVR__", F_CPU) e os nomes dos pinos (A0,
 * LED_BUILTIN, D5) vem do cs_placa.h, que o simulador gera para a placa do
 * projeto antes de compilar. */
#ifndef Arduino_h
#define Arduino_h

#include <stdint.h>
#include <stdbool.h>
#include <stddef.h>
#include <stdlib.h>
#include <stdio.h>
#include <string.h>
#include <math.h>
#include <ctype.h>
#include <inttypes.h>

#include "cs_placa.h"
#include "cs_ponte.h"
#include "binary.h"
#include "pgmspace.h"
#ifdef ESP32
#include "cs_esp_idf.h"
#endif

#ifdef __cplusplus
extern "C" {
#endif

/* ---- os tipos do Arduino ---- */
typedef bool boolean;
typedef uint8_t byte;
typedef unsigned int word;

/* ---- as constantes ---- */
#define HIGH 0x1
#define LOW  0x0

#ifndef PI
#define PI 3.1415926535897932384626433832795
#endif
#define HALF_PI 1.5707963267948966192313216916398
#define TWO_PI 6.283185307179586476925286766559
#define DEG_TO_RAD 0.017453292519943295769236907684886
#define RAD_TO_DEG 57.295779513082320876798154814105
#define EULER 2.718281828459045235360287471352

#define SERIAL  0x0
#define DISPLAY 0x1

#define LSBFIRST 0
#define MSBFIRST 1

#define BIN 2
#define OCT 8
#define DEC 10
#define HEX 16

/* ---- as macros de bit e de numero ---- */
#define radians(deg) ((deg) * DEG_TO_RAD)
#define degrees(rad) ((rad) * RAD_TO_DEG)
#define sq(x) ((x) * (x))

#define lowByte(w) ((uint8_t)((w) & 0xff))
#define highByte(w) ((uint8_t)((w) >> 8))

#define bitRead(value, bit) (((value) >> (bit)) & 0x01)
#define bitSet(value, bit) ((value) |= (1UL << (bit)))
#define bitClear(value, bit) ((value) &= ~(1UL << (bit)))
#define bitToggle(value, bit) ((value) ^= (1UL << (bit)))
#define bitWrite(value, bit, bitvalue) ((bitvalue) ? bitSet(value, bit) : bitClear(value, bit))
#ifndef bit
#define bit(b) (1UL << (b))
#endif
#define _BV(b) (1UL << (b))

#define clockCyclesPerMicrosecond() (F_CPU / 1000000L)
#define clockCyclesToMicroseconds(a) ((a) / clockCyclesPerMicrosecond())
#define microsecondsToClockCycles(a) ((a) * clockCyclesPerMicrosecond())

#ifndef __cplusplus
#define min(a, b) ((a) < (b) ? (a) : (b))
#define max(a, b) ((a) > (b) ? (a) : (b))
#define constrain(amt, low, high) ((amt) < (low) ? (low) : ((amt) > (high) ? (high) : (amt)))
#endif

/* ---- as funcoes do core ---- */
void pinMode(uint8_t pin, uint8_t mode);
void digitalWrite(uint8_t pin, uint8_t val);
int digitalRead(uint8_t pin);
#ifndef ESP32 /* na ESP32, os do esp32-hal-adc.h (uint16_t, uint8_t) */
int analogRead(uint8_t pin);
void analogReference(uint8_t mode);
void analogWrite(uint8_t pin, int val);
void analogReadResolution(int bits);
#endif
void analogWriteResolution(int bits);
void analogWriteFrequency(uint32_t freq);
void analogWriteRange(uint32_t range);

unsigned long millis(void);
unsigned long micros(void);
#ifndef ESP32 /* na ESP32, o do esp32-hal.h (uint32_t) */
void delay(unsigned long ms);
#endif
void delayMicroseconds(unsigned int us);
void yield(void);

unsigned long pulseIn(uint8_t pin, uint8_t state, unsigned long timeout);
unsigned long pulseInLong(uint8_t pin, uint8_t state, unsigned long timeout);

void shiftOut(uint8_t dataPin, uint8_t clockPin, uint8_t bitOrder, uint8_t val);
uint8_t shiftIn(uint8_t dataPin, uint8_t clockPin, uint8_t bitOrder);

void attachInterrupt(uint8_t interruptNum, void (*userFunc)(void), int mode);
void detachInterrupt(uint8_t interruptNum);
int digitalPinToInterrupt(int pin);
void interrupts(void);
void noInterrupts(void);
#define sei() interrupts()
#define cli() noInterrupts()

void cs_tone(uint8_t pin, unsigned int frequency, unsigned long duration);
void noTone(uint8_t pin);

long cs_random(long howsmall, long howbig);
void randomSeed(unsigned long seed);
long map(long x, long in_min, long in_max, long out_min, long out_max);

unsigned int makeWord(unsigned char h, unsigned char l);
#define word(...) makeWord(__VA_ARGS__)

/* As do stdlib do AVR que o C padrao nao tem. */
#ifdef ESP32 /* o do stdlib_noniso.h da ESP32 (int), e os longos */
char *dtostrf(double val, signed int width, unsigned int prec, char *sout);
char *lltoa(long long val, char *s, int radix);
char *ulltoa(unsigned long long val, char *s, int radix);
#else
char *dtostrf(double val, signed char width, unsigned char prec, char *sout);
#endif
char *ltoa(long value, char *str, int radix);
char *ultoa(unsigned long value, char *str, int radix);
char *utoa(unsigned int value, char *str, int radix);
char *itoa(int value, char *str, int radix);

#if defined(ESP32) || defined(ESP8266)
#define ESP_OK 0
#define ESP_FAIL -1
#endif

#ifdef ESP32
/* O LEDC do core 2.x (por canal), que o 3.x tirou; o resto (o ledcAttach,
 * o touchRead, o rgbLedWrite...) vem do esp32-hal.h, o do core oficial. */
double ledcSetup(uint8_t channel, double freq, uint8_t resolution_bits);
void ledcAttachPin(uint8_t pin, uint8_t channel);
void ledcDetachPin(uint8_t pin);
#endif

/* O sketch. */
void setup(void);
void loop(void);

/* Da libc da ESP32 e do AVR (a do Windows nao tem). */
size_t strlcpy(char *dst, const char *src, size_t size);
size_t strlcat(char *dst, const char *src, size_t size);

#ifdef __cplusplus
} /* extern "C" */
#endif

#ifdef __cplusplus
#include <algorithm>
#include <cmath>
#include "WCharacter.h"
#include "WString.h"
#include "HardwareSerial.h"
#include "cs_esp.h"

using std::abs;
using std::isinf;
using std::isnan;

/* Templates, e nao as macros do AVR: aceitam tipos diferentes (min(x, 5L))
 * sem quebrar o std::min que a biblioteca padrao usa por dentro. */
template <class A, class B>
constexpr auto min(const A &a, const B &b) -> decltype(a < b ? a : b) { return b < a ? b : a; }
template <class A, class B>
constexpr auto max(const A &a, const B &b) -> decltype(a < b ? b : a) { return a < b ? b : a; }
template <class T, class L, class H>
constexpr auto constrain(const T &amt, const L &low, const H &high) -> decltype(amt < low ? low : (amt > high ? high : amt))
{
    return amt < low ? low : (amt > high ? high : amt);
}

inline void tone(uint8_t pin, unsigned int frequency, unsigned long duration = 0) { cs_tone(pin, frequency, duration); }
inline unsigned long pulseIn(uint8_t pin, uint8_t state) { return pulseIn(pin, state, 1000000UL); }
inline unsigned long pulseInLong(uint8_t pin, uint8_t state) { return pulseInLong(pin, state, 1000000UL); }

unsigned int makeWord(unsigned int w);
long random(long howbig);
long random(long howsmall, long howbig);

/* attachInterrupt com argumento (ESP32), com a ligacao de C, como no core. */
extern "C" void attachInterruptArg(uint8_t pin, void (*userFunc)(void *), void *arg, int mode);
#else
#define tone(pin, freq, dur) cs_tone((pin), (freq), (dur))
#define random(a, b) cs_random((a), (b))
#endif

/* A ESP32 roda o Arduino em cima do FreeRTOS: xTaskCreate, filas e semaforos
 * valem sem #include, como no core dela. */
#ifdef ESP32
#include "freertos/FreeRTOS.h"
#include "cs_registradores.h"
/* O HAL do core oficial da ESP32 (os cabecalhos de verdade): o PWM, o toque,
 * o DAC, o RMT, a UART, o log_x... As funcoes sao as do simulador. */
#include "esp32-hal.h"
#include "cs_posix.h"

/* As macros do Arduino.h do core que o sketch usa para mudar o padrao: o
 * tamanho da pilha do loop, o relatorio do chip no boot e o nome do USB MIDI
 * (o simulador aceita; a pilha e o relatorio nao mudam nada nele). */
size_t getArduinoLoopTaskStackSize(void);
#define SET_LOOP_TASK_STACK_SIZE(sz)     \
  size_t getArduinoLoopTaskStackSize() { \
    return sz;                           \
  }
#define ESP32_USB_MIDI_DEFAULT_NAME "TinyUSB MIDI"
#define SET_USB_MIDI_DEVICE_NAME(name)        \
  const char *getUSBMIDIDefaultDeviceName() { \
    if (!name || strlen(name) == 0) {         \
      return ESP32_USB_MIDI_DEFAULT_NAME;     \
    }                                         \
    return name;                              \
  }
bool shouldPrintChipDebugReport(void);
#define ENABLE_CHIP_DEBUG_REPORT          \
  bool shouldPrintChipDebugReport(void) { \
    return true;                          \
  }
#ifdef __cplusplus
extern "C" {
#endif
int64_t esp_timer_get_time(void);

/* A PSRAM (a memoria de fora da ESP32-S3/WROVER): aqui, a do PC. */
bool psramFound(void);
bool psramInit(void);
void *ps_malloc(size_t size);
void *ps_calloc(size_t n, size_t size);
void *ps_realloc(void *ptr, size_t size);
#ifdef __cplusplus
}
#endif
#include "esp_heap_caps.h"
#endif

#endif
