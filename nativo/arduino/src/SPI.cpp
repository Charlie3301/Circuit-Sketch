/* SPI.cpp -- o SPI pelo simulador (ver SPI.h). A transacao manda a
 * frequencia, a ordem e o modo do SPISettings antes de comecar. */
#include "cs_interno.h"
#include "SPI.h"
#include "cs_objeto.h"

/* Um bloco de bytes numa chamada so (em pedacos, para o vetor nao ficar
 * enorme): o simulador entrega inteiro ao display que esta ouvindo, ou
 * bate bit a bit nos pinos quando nao ha quem ouca. */
enum { CS_SPI_PEDACO = 4096 };

SPIClass SPI("SPI");
#if defined(ARDUINO_ARCH_RP2040)
SPIClass SPI1("SPI1");
#elif defined(ESP32)
SPIClass cs_segundo_spi("SPI1");
#endif

/* O HSPI da ESP32 (e o 1 das outras placas) e o outro barramento; o VSPI e o FSPI sao o proprio SPI. */
#ifdef ESP32
SPIClass::SPIClass(uint8_t bus) : nome(bus == HSPI ? "SPI1" : "SPI") {}
#else
SPIClass::SPIClass(uint8_t bus) : nome(bus == 1 ? "SPI1" : "SPI") {}
#endif

void SPIClass::begin() { ch(nome, "begin"); }

void SPIClass::begin(int8_t sck, int8_t miso, int8_t mosi, int8_t ss)
{
    if (ss >= 0) ch(nome, "begin", cs_i(sck), cs_i(miso), cs_i(mosi), cs_i(ss));
    else ch(nome, "begin", cs_i(sck), cs_i(miso), cs_i(mosi));
}

void SPIClass::end() { ch(nome, "end"); }

void SPIClass::beginTransaction(SPISettings s)
{
    ch(nome, "setFrequency", cs_i(s.clock));
    ch(nome, "setBitOrder", cs_i(s.bitOrder));
    ch(nome, "setDataMode", cs_i(s.dataMode));
    ch(nome, "beginTransaction");
}

void SPIClass::endTransaction() { ch(nome, "endTransaction"); }
uint8_t SPIClass::transfer(uint8_t data) { return (uint8_t)ch_i(nome, "transfer", cs_i(data)); }

void SPIClass::transfer(void *buf, size_t count)
{
    uint8_t *p = (uint8_t *)buf;
    for (size_t i = 0; i < count; i += CS_SPI_PEDACO) {
        int n = (int)(count - i < CS_SPI_PEDACO ? count - i : CS_SPI_PEDACO);
        ch(nome, "transfer", cs_de(cs_vetor_mutavel(p + i, n)), cs_i(n));
    }
}

uint16_t SPIClass::transfer16(uint16_t data) { return (uint16_t)ch_i(nome, "transfer16", cs_i(data)); }
uint32_t SPIClass::transfer32(uint32_t data) { return (uint32_t)ch_i(nome, "transfer32", cs_i(data)); }

void SPIClass::transferBytes(const uint8_t *data, uint8_t *out, uint32_t size)
{
    /* Sem dados para mandar (so ler), o MOSI fica em nivel alto. */
    uint8_t nada[64];
    memset(nada, 0xFF, sizeof(nada));
    uint32_t pedaco = data ? (uint32_t)CS_SPI_PEDACO : (uint32_t)sizeof(nada);
    for (uint32_t i = 0; i < size; i += pedaco) {
        int n = (int)(size - i < pedaco ? size - i : pedaco);
        const uint8_t *sai = data ? data + i : nada;
        if (out) ch(nome, "transferBytes", cs_de(cs_vetor(sai, n)), cs_de(cs_vetor_mutavel(out + i, n)), cs_i(n));
        else ch(nome, "writeBytes", cs_de(cs_vetor(sai, n)), cs_i(n));
    }
}

void SPIClass::write(uint8_t data) { ch(nome, "write", cs_i(data)); }
void SPIClass::write16(uint16_t data) { ch(nome, "write16", cs_i(data)); }
void SPIClass::write32(uint32_t data) { ch(nome, "write32", cs_i(data)); }

void SPIClass::writeBytes(const uint8_t *data, uint32_t size)
{
    for (uint32_t i = 0; i < size; i += CS_SPI_PEDACO) {
        int n = (int)(size - i < CS_SPI_PEDACO ? size - i : CS_SPI_PEDACO);
        ch(nome, "writeBytes", cs_de(cs_vetor(data + i, n)), cs_i(n));
    }
}

/* As palavras de 16 bits com o byte mais alto primeiro, como o painel espera (o da ILI9341). */
void SPIClass::writePixels(const void *data, uint32_t size)
{
    const uint8_t *b = (const uint8_t *)data;
    uint8_t troca[512];
    for (uint32_t i = 0; i + 1 < size;) {
        uint32_t n = 0;
        for (; n + 1 < sizeof(troca) && i + 1 < size; i += 2, n += 2) {
            troca[n] = b[i + 1];
            troca[n + 1] = b[i];
        }
        writeBytes(troca, n);
    }
}

void SPIClass::writePattern(const uint8_t *data, uint8_t size, uint32_t repeat)
{
    while (repeat--) writeBytes(data, size);
}

void SPIClass::transferBits(uint32_t data, uint32_t *out, uint8_t bits)
{
    uint32_t r = 0;
    for (int i = (bits - 1) / 8; i >= 0; i--) r = (r << 8) | transfer((uint8_t)(data >> (i * 8)));
    if (out) *out = r;
}

void SPIClass::setBitOrder(uint8_t bitOrder) { ch(nome, "setBitOrder", cs_i(bitOrder)); }
void SPIClass::setDataMode(uint8_t dataMode) { ch(nome, "setDataMode", cs_i(dataMode)); }
void SPIClass::setFrequency(uint32_t freq) { ch(nome, "setFrequency", cs_i(freq)); }
void SPIClass::setClockDivider(uint32_t clockDiv)
{
    divisor = clockDiv;
    ch(nome, "setClockDivider", cs_i(clockDiv));
}
void SPIClass::setHwCs(bool use) { ch(nome, "setHwCs", cs_b(use)); }
int8_t SPIClass::pinSS() { return (int8_t)ch_i(nome, "pinSS"); }
bool SPIClass::setRX(int pin) { return ch_i(nome, "setRX", cs_i(pin)) != 0; }
bool SPIClass::setTX(int pin) { return ch_i(nome, "setTX", cs_i(pin)) != 0; }
bool SPIClass::setSCK(int pin) { return ch_i(nome, "setSCK", cs_i(pin)) != 0; }
bool SPIClass::setCS(int pin) { return ch_i(nome, "setCS", cs_i(pin)) != 0; }
void SPIClass::setMOSI(int pin) { ch(nome, "setMOSI", cs_i(pin)); }
void SPIClass::setMISO(int pin) { ch(nome, "setMISO", cs_i(pin)); }
void SPIClass::setSCLK(int pin) { ch(nome, "setSCLK", cs_i(pin)); }
void SPIClass::setSSEL(int pin) { ch(nome, "setSSEL", cs_i(pin)); }
