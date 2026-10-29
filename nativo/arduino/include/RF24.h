/* RF24.h -- o radio nRF24L01 (a RF24 do TMRh20), pelo ar da bancada. O que
 * vai num write() chega byte a byte no read() da outra placa. */
#ifndef __RF24_H__
#define __RF24_H__

#include "cs_objeto.h"
#include "SPI.h"

typedef enum { RF24_PA_MIN = 0, RF24_PA_LOW, RF24_PA_HIGH, RF24_PA_MAX, RF24_PA_ERROR } rf24_pa_dbm_e;
typedef enum { RF24_1MBPS = 0, RF24_2MBPS, RF24_250KBPS } rf24_datarate_e;
typedef enum { RF24_CRC_DISABLED = 0, RF24_CRC_8, RF24_CRC_16 } rf24_crclength_e;

class RF24 : public CsObjeto {
    uint8_t tamanho = 32;

public:
    RF24(uint16_t cepin, uint16_t cspin, uint32_t spi_speed = 10000000) { (void)spi_speed; cs_criar("RF24", cepin, cspin); }
    RF24() { cs_criar("RF24"); }
    bool begin() { return cs_obj_i(cs_nome, "begin") != 0; }
    bool begin(SPIClass *spi) { return cs_obj_i(cs_nome, "begin", spi) != 0; }
    bool begin(uint16_t cepin, uint16_t cspin) { return cs_obj_i(cs_nome, "begin", cepin, cspin) != 0; }
    bool isChipConnected() { return cs_obj_i(cs_nome, "isChipConnected") != 0; }
    bool isValid() { return cs_obj_i(cs_nome, "isValid") != 0; }
    void startListening() { cs_obj_n(cs_nome, "startListening"); }
    void stopListening() { cs_obj_n(cs_nome, "stopListening"); }
    bool available() { return cs_obj_i(cs_nome, "available") != 0; }
    bool available(uint8_t *pipe_num)
    {
        bool r = available();
        if (r && pipe_num) *pipe_num = 1;
        return r;
    }
    void read(void *buf, uint8_t len) { cs_obj_n(cs_nome, "read", cs_vetor_mutavel(buf, len), len); }
    bool write(const void *buf, uint8_t len) { return cs_obj_i(cs_nome, "write", cs_vetor(buf, len), len) != 0; }
    bool write(const void *buf, uint8_t len, bool multicast) { (void)multicast; return write(buf, len); }
    bool writeFast(const void *buf, uint8_t len) { return write(buf, len); }
    bool txStandBy() { return true; }
    void openWritingPipe(const uint8_t *address) { cs_obj_n(cs_nome, "openWritingPipe", cs_vetor(address, 6)); }
    void openWritingPipe(uint64_t address) { cs_obj_n(cs_nome, "openWritingPipe", address); }
    void openReadingPipe(uint8_t number, const uint8_t *address) { cs_obj_n(cs_nome, "openReadingPipe", number, cs_vetor(address, 6)); }
    void openReadingPipe(uint8_t number, uint64_t address) { cs_obj_n(cs_nome, "openReadingPipe", number, address); }
    void closeReadingPipe(uint8_t pipe) { cs_obj_n(cs_nome, "closeReadingPipe", pipe); }
    void setPALevel(uint8_t level, bool lnaEnable = 1) { (void)lnaEnable; cs_obj_n(cs_nome, "setPALevel", level); }
    uint8_t getPALevel() { return RF24_PA_MAX; }
    bool setDataRate(rf24_datarate_e speed) { cs_obj_n(cs_nome, "setDataRate", (int)speed); return true; }
    void setChannel(uint8_t channel) { cs_obj_n(cs_nome, "setChannel", channel); }
    uint8_t getChannel() { return (uint8_t)cs_obj_i(cs_nome, "getChannel"); }
    void setPayloadSize(uint8_t size) { tamanho = size; cs_obj_n(cs_nome, "setPayloadSize", size); }
    uint8_t getPayloadSize() { return tamanho; }
    uint8_t getDynamicPayloadSize() { return tamanho; }
    void setRetries(uint8_t delay, uint8_t count) { cs_obj_n(cs_nome, "setRetries", delay, count); }
    void setAutoAck(bool enable) { cs_obj_n(cs_nome, "setAutoAck", enable); }
    void setAutoAck(uint8_t pipe, bool enable) { (void)pipe; setAutoAck(enable); }
    void setCRCLength(rf24_crclength_e length) { cs_obj_n(cs_nome, "setCRCLength", (int)length); }
    void enableAckPayload() { cs_obj_n(cs_nome, "enableAckPayload"); }
    void enableDynamicPayloads() { cs_obj_n(cs_nome, "enableDynamicPayloads"); }
    void disableDynamicPayloads() { cs_obj_n(cs_nome, "disableDynamicPayloads"); }
    void setAddressWidth(uint8_t) {}
    void powerUp() { cs_obj_n(cs_nome, "powerUp"); }
    void powerDown() { cs_obj_n(cs_nome, "powerDown"); }
    void flush_tx() {}
    void flush_rx() {}
    bool testCarrier() { return cs_obj_i(cs_nome, "testCarrier") != 0; }
    bool testRPD() { return cs_obj_i(cs_nome, "testRPD") != 0; }
    void printDetails() { cs_obj_n(cs_nome, "printDetails"); }
    void printPrettyDetails() { cs_obj_n(cs_nome, "printPrettyDetails"); }
    void maskIRQ(bool tx_ok, bool tx_fail, bool rx_ready) { cs_obj_n(cs_nome, "maskIRQ", tx_ok, tx_fail, rx_ready); }
};

#endif
