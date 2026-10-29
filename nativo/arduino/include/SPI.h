/* SPI.h -- o SPI do Arduino, pelo barramento simulado da placa. */
#ifndef _SPI_H_INCLUDED
#define _SPI_H_INCLUDED

#include <stdint.h>
#include <stddef.h>

#ifdef __AVR__
#define SPI_MODE0 0x00
#define SPI_MODE1 0x04
#define SPI_MODE2 0x08
#define SPI_MODE3 0x0C
#define SPI_CLOCK_DIV4 0x00
#define SPI_CLOCK_DIV16 0x01
#define SPI_CLOCK_DIV64 0x02
#define SPI_CLOCK_DIV128 0x03
#define SPI_CLOCK_DIV2 0x04
#define SPI_CLOCK_DIV8 0x05
#define SPI_CLOCK_DIV32 0x06
#else
#define SPI_MODE0 0
#define SPI_MODE1 1
#define SPI_MODE2 2
#define SPI_MODE3 3
#define SPI_CLOCK_DIV2 2
#define SPI_CLOCK_DIV4 4
#define SPI_CLOCK_DIV8 8
#define SPI_CLOCK_DIV16 16
#define SPI_CLOCK_DIV32 32
#define SPI_CLOCK_DIV64 64
#define SPI_CLOCK_DIV128 128
#endif

#ifndef LSBFIRST
#define LSBFIRST 0
#endif
#ifndef MSBFIRST
#define MSBFIRST 1
#endif
#define SPI_LSBFIRST LSBFIRST
#define SPI_MSBFIRST MSBFIRST

#ifdef ESP32
#define FSPI 0
#define HSPI 1
#define VSPI 2
#endif

class SPISettings {
public:
    SPISettings() : clock(1000000), bitOrder(MSBFIRST), dataMode(SPI_MODE0) {}
    SPISettings(uint32_t clockFreq, uint8_t order, uint8_t mode) : clock(clockFreq), bitOrder(order), dataMode(mode) {}
    uint32_t clock;
    uint8_t bitOrder;
    uint8_t dataMode;
};

class SPIClass {
    const char *nome;

public:
    explicit SPIClass(const char *nomeDoObjeto) : nome(nomeDoObjeto) {}
    explicit SPIClass(uint8_t bus);

    void begin();
    void begin(int8_t sck, int8_t miso = -1, int8_t mosi = -1, int8_t ss = -1);
    void end();
    void beginTransaction(SPISettings settings);
    void endTransaction();
    uint8_t transfer(uint8_t data);
    void transfer(void *buf, size_t count);
    uint16_t transfer16(uint16_t data);
    uint32_t transfer32(uint32_t data);
    void transferBytes(const uint8_t *data, uint8_t *out, uint32_t size);
    void write(uint8_t data);
    void write16(uint16_t data);
    void write32(uint32_t data);
    void writeBytes(const uint8_t *data, uint32_t size);
    void setBitOrder(uint8_t bitOrder);
    void setDataMode(uint8_t dataMode);
    void setFrequency(uint32_t freq);
    void setClockDivider(uint32_t clockDiv);
    void setHwCs(bool use);
    int8_t pinSS();
    bool setRX(int pin);
    bool setTX(int pin);
    bool setSCK(int pin);
    bool setCS(int pin);
    void setMOSI(int pin);
    void setMISO(int pin);
    void setSCLK(int pin);
    void setSSEL(int pin);
    void usingInterrupt(int) {}
    void notUsingInterrupt(int) {}
    void attachInterrupt() {}
    void detachInterrupt() {}

    const char *nomeNoSimulador() const { return nome; }
};

extern SPIClass SPI;
#if defined(ESP32) || defined(ARDUINO_ARCH_RP2040)
extern SPIClass SPI1;
#endif

#endif
