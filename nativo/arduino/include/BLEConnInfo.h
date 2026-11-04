/* BLEConnInfo.h -- os dados de uma conexao BLE (so com o NimBLE: a S3, a C3,
 * a C6 e a H2; com o Bluedroid da ESP32 o arquivo fica vazio, como no core).
 * No simulador a conexao e a do radio da bancada: os tempos sao os padrao
 * do NimBLE (intervalo de 30 ms, tempo limite de 4 s). */
#ifndef BLECONNINFO_H_
#define BLECONNINFO_H_

#include "sdkconfig.h"
#if defined(CONFIG_NIMBLE_ENABLED)
#include "cs_ble.h"

class BLEConnInfo {
public:
    BLEConnInfo() {}
    BLEConnInfo(const BLEAddress &endereco, uint16_t conexao) : m_address(endereco), m_connHandle(conexao) {}
    BLEAddress getAddress() const { return m_address; }
    BLEAddress getIdAddress() const { return m_address; }
    uint16_t getConnHandle() const { return m_connHandle; }
    uint16_t getConnInterval() const { return 24; }
    uint16_t getConnTimeout() const { return 400; }
    uint16_t getConnLatency() const { return 0; }
    uint16_t getMTU() const { return BLEDevice::getMTU(); }
    bool isMaster() const { return false; }
    bool isSlave() const { return true; }
    bool isBonded() const { return false; }
    bool isEncrypted() const { return false; }
    bool isAuthenticated() const { return false; }
    uint8_t getSecKeySize() const { return 0; }

private:
    BLEAddress m_address;
    uint16_t m_connHandle = 0;
};

#endif
#endif
