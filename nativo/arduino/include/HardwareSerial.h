/* HardwareSerial.h -- a Serial (e a Serial1, a Serial2) da placa.
 *
 * Cada escrita e cada leitura vai ao simulador pelo nome do objeto: o
 * monitor serial, o baud que poe os bytes no pino TX, o que outra placa
 * mandou pelo RX -- tudo e o mesmo do interpretador. */
#ifndef HardwareSerial_h
#define HardwareSerial_h

#include <inttypes.h>
#include "Stream.h"

#define SERIAL_5N1 0x00
#define SERIAL_6N1 0x02
#define SERIAL_7N1 0x04
#define SERIAL_8N1 0x06
#define SERIAL_5N2 0x08
#define SERIAL_6N2 0x0A
#define SERIAL_7N2 0x0C
#define SERIAL_8N2 0x0E
#define SERIAL_5E1 0x20
#define SERIAL_6E1 0x22
#define SERIAL_7E1 0x24
#define SERIAL_8E1 0x26
#define SERIAL_5E2 0x28
#define SERIAL_6E2 0x2A
#define SERIAL_7E2 0x2C
#define SERIAL_8E2 0x2E
#define SERIAL_5O1 0x30
#define SERIAL_6O1 0x32
#define SERIAL_7O1 0x34
#define SERIAL_8O1 0x36
#define SERIAL_5O2 0x38
#define SERIAL_6O2 0x3A
#define SERIAL_7O2 0x3C
#define SERIAL_8O2 0x3E

class HardwareSerial : public Stream {
    const char *nome;

public:
    explicit HardwareSerial(const char *nomeDoObjeto) : nome(nomeDoObjeto) {}
    /* "HardwareSerial MySerial(1)" da ESP32: a UART 1 e o Serial1. */
    explicit HardwareSerial(int uart) : nome(uart == 0 ? "Serial" : uart == 1 ? "Serial1" : "Serial2") {}

    void begin(unsigned long baud) { begin(baud, SERIAL_8N1); }
    void begin(unsigned long baud, uint32_t config, int8_t rxPin = -1, int8_t txPin = -1, bool invert = false,
               unsigned long timeout_ms = 20000UL, uint8_t rxfifo_full_thrhd = 112);
    void end();
    int available() override;
    int peek() override;
    int read() override;
    int availableForWrite() override;
    void flush() override;
    size_t write(uint8_t) override;
    size_t write(const uint8_t *buffer, size_t size) override;
    using Print::write;

    String readString() override;
    String readStringUntil(char terminator) override;
    long parseInt(LookaheadMode lookahead = SKIP_ALL, char ignore = NO_IGNORE_CHAR) override;
    float parseFloat(LookaheadMode lookahead = SKIP_ALL, char ignore = NO_IGNORE_CHAR) override;

    void setRxBufferSize(size_t) {}
    void setTxBufferSize(size_t) {}
    void setPins(int8_t rx, int8_t tx, int8_t = -1, int8_t = -1)
    {
        cs_val a[] = {cs_i(rx), cs_i(tx)};
        cs_nada(nome, "setPins", 2, a);
    }
    void updateBaudRate(unsigned long baud);
    operator bool() const { return true; }

    const char *nomeNoSimulador() const { return nome; }
};

extern HardwareSerial Serial;
extern HardwareSerial Serial1;
extern HardwareSerial Serial2;

#endif
