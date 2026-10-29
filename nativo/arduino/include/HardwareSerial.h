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

#include <functional>

typedef enum {
    UART_NO_ERROR,
    UART_BREAK_ERROR,
    UART_BUFFER_FULL_ERROR,
    UART_FIFO_OVF_ERROR,
    UART_FRAME_ERROR,
    UART_PARITY_ERROR
} hardwareSerial_error_t;

typedef std::function<void(void)> OnReceiveCb;
typedef std::function<void(hardwareSerial_error_t)> OnReceiveErrorCb;

class HardwareSerial : public Stream {
    const char *nome;
    unsigned long _baud = 0;
    OnReceiveCb _recebe;
    OnReceiveErrorCb _erro;
    bool _soNoFim = false;
    void *_tarefa = nullptr;
    static void cs_vigiar(void *arg);

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

    void setPins(int8_t rx, int8_t tx, int8_t = -1, int8_t = -1)
    {
        cs_val a[] = {cs_i(rx), cs_i(tx)};
        cs_nada(nome, "setPins", 2, a);
    }
    void updateBaudRate(unsigned long baud);
    uint32_t baudRate() { return _baud; }
    operator bool() const { return true; }

    /* A funcao chamada quando chegam bytes (o onReceive do 3.x): uma tarefa
     * olha a porta, como a tarefa de eventos da UART da placa. */
    void onReceive(OnReceiveCb function, bool onlyOnTimeout = false);
    void onReceiveError(OnReceiveErrorCb function) { _erro = function; }
    void setDebugOutput(bool en) { (void)en; }
    void eventQueueReset() {}
    bool setRxTimeout(uint8_t symbols_timeout) { (void)symbols_timeout; return true; }
    bool setRxFIFOFull(uint8_t fifoBytes) { (void)fifoBytes; return true; }
    bool setRxInvert(bool invert) { (void)invert; return true; }
    bool setTxInvert(bool invert) { (void)invert; return true; }
    bool setCtsInvert(bool invert) { (void)invert; return true; }
    bool setRtsInvert(bool invert) { (void)invert; return true; }
    bool setHwFlowCtrlMode(uint8_t mode = 0, uint8_t threshold = 64) { (void)mode, (void)threshold; return true; }
    bool setMode(uint8_t mode) { (void)mode; return true; }
    bool setIrdaDirection(bool direction) { (void)direction; return true; }
    bool setClockSource(int clkSrc) { (void)clkSrc; return true; }
    bool enableRxInternalPull(bool enable) { (void)enable; return true; }
    size_t setRxBufferSize(size_t new_size) { return new_size; }
    size_t setTxBufferSize(size_t new_size) { return new_size; }

    const char *nomeNoSimulador() const { return nome; }
};

extern HardwareSerial Serial;
extern HardwareSerial Serial1;
extern HardwareSerial Serial2;

#endif
