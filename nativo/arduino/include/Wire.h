/* Wire.h -- o I2C do Arduino (a placa como mestre).
 *
 * Cada operacao vai ao barramento simulado da placa: quem responde no
 * endereco, o ACK do endTransmission, os bytes do requestFrom -- o mesmo
 * barramento que o interpretador usa. */
#ifndef TwoWire_h
#define TwoWire_h

#include <inttypes.h>
#include "Stream.h"

#ifndef BUFFER_LENGTH
#define BUFFER_LENGTH 128
#endif
#define WIRE_HAS_END 1

class TwoWire : public Stream {
    const char *nome;

public:
    explicit TwoWire(const char *nomeDoObjeto) : nome(nomeDoObjeto) {}

    bool begin();
    bool begin(int sda, int scl, uint32_t frequency = 0);
    bool begin(uint8_t address);
    bool begin(int address) { return begin((uint8_t)address); }
    void end();
    void setClock(uint32_t frequency);
    uint32_t getClock();
    bool setPins(int sda, int scl);
    bool setSDA(int pin);
    bool setSCL(int pin);
    size_t setBufferSize(size_t size);
    void setTimeOut(uint16_t timeOutMillis);
    void setWireTimeout(uint32_t timeout = 25000, bool reset_with_timeout = false);
    uint16_t getTimeOut();

    void beginTransmission(uint8_t address);
    void beginTransmission(int address) { beginTransmission((uint8_t)address); }
    uint8_t endTransmission(bool sendStop);
    uint8_t endTransmission(void) { return endTransmission(true); }
    uint8_t requestFrom(uint8_t address, size_t quantity, bool sendStop);
    uint8_t requestFrom(uint8_t address, size_t quantity) { return requestFrom(address, quantity, true); }
    uint8_t requestFrom(int address, int quantity) { return requestFrom((uint8_t)address, (size_t)quantity, true); }
    uint8_t requestFrom(int address, int quantity, int sendStop)
    {
        return requestFrom((uint8_t)address, (size_t)quantity, sendStop != 0);
    }

    size_t write(uint8_t) override;
    size_t write(const uint8_t *, size_t) override;
    using Print::write;
    int available() override;
    int read() override;
    int peek() override;
    void flush() override {}

    inline size_t write(unsigned long n) { return write((uint8_t)n); }
    inline size_t write(long n) { return write((uint8_t)n); }
    inline size_t write(unsigned int n) { return write((uint8_t)n); }
    inline size_t write(int n) { return write((uint8_t)n); }

    /* A placa como escrava (o mestre e outra placa): ainda nao simulada --
     * o simulador diz isso na hora, como no sketch interpretado. */
    void onReceive(void (*)(int)) { ch_escravo("onReceive"); }
    void onRequest(void (*)(void)) { ch_escravo("onRequest"); }
    template <class F> void onReceive(F) { ch_escravo("onReceive"); }
    template <class F> void onRequest(F) { ch_escravo("onRequest"); }
    size_t slaveWrite(const uint8_t *, size_t) { return 0; }

    const char *nomeNoSimulador() const { return nome; }

private:
    void ch_escravo(const char *metodo)
    {
        cs_val a[] = {cs_nome("__sem_escravo")};
        cs_nada(nome, metodo, 1, a);
    }
};

extern TwoWire Wire;
extern TwoWire Wire1;

#endif
