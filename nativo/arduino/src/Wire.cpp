/* Wire.cpp -- o I2C pelo simulador (ver Wire.h). */
#include "cs_interno.h"
#include "Wire.h"

TwoWire Wire("Wire");
TwoWire Wire1("Wire1");

bool TwoWire::begin() { return ch_i(nome, "begin") != 0; }

bool TwoWire::begin(int sda, int scl, uint32_t frequency)
{
    if (frequency) return ch_i(nome, "begin", cs_i(sda), cs_i(scl), cs_i(frequency)) != 0;
    return ch_i(nome, "begin", cs_i(sda), cs_i(scl)) != 0;
}

bool TwoWire::begin(uint8_t address) { return ch_i(nome, "begin", cs_i(address)) != 0; }
void TwoWire::end() { ch(nome, "end"); }
void TwoWire::setClock(uint32_t frequency) { ch(nome, "setClock", cs_i(frequency)); }
uint32_t TwoWire::getClock() { return (uint32_t)ch_i(nome, "getClock"); }
bool TwoWire::setPins(int sda, int scl) { return ch_i(nome, "setPins", cs_i(sda), cs_i(scl)) != 0; }
bool TwoWire::setSDA(int pin) { return ch_i(nome, "setSDA", cs_i(pin)) != 0; }
bool TwoWire::setSCL(int pin) { return ch_i(nome, "setSCL", cs_i(pin)) != 0; }
size_t TwoWire::setBufferSize(size_t size) { return (size_t)ch_i(nome, "setBufferSize", cs_i((int64_t)size)); }
void TwoWire::setTimeOut(uint16_t ms) { ch(nome, "setTimeOut", cs_i(ms)); }
void TwoWire::setWireTimeout(uint32_t timeout, bool) { ch(nome, "setWireTimeout", cs_i(timeout)); }
uint16_t TwoWire::getTimeOut() { return (uint16_t)ch_i(nome, "getTimeOut"); }

void TwoWire::beginTransmission(uint8_t address) { ch(nome, "beginTransmission", cs_i(address)); }

uint8_t TwoWire::endTransmission(bool sendStop)
{
    return (uint8_t)ch_i(nome, "endTransmission", cs_b(sendStop));
}

uint8_t TwoWire::requestFrom(uint8_t address, size_t quantity, bool sendStop)
{
    return (uint8_t)ch_i(nome, "requestFrom", cs_i(address), cs_i((int64_t)quantity), cs_b(sendStop));
}

size_t TwoWire::write(uint8_t c) { return (size_t)ch_i(nome, "write", cs_i(c)); }

size_t TwoWire::write(const uint8_t *data, size_t quantity)
{
    size_t n = 0;
    for (size_t i = 0; i < quantity; i++) n += write(data[i]);
    return n;
}

int TwoWire::available() { return (int)ch_i(nome, "available"); }
int TwoWire::read() { return (int)ch_i(nome, "read"); }
int TwoWire::peek() { return (int)ch_i(nome, "peek"); }
