/* BluetoothSerial.h -- o Bluetooth classico da ESP32 (SPP): a serial sem fio,
 * com o celular de verdade ou outra placa da bancada. */
#ifndef _BLUETOOTH_SERIAL_H_
#define _BLUETOOTH_SERIAL_H_

#include "cs_objeto.h"

class BluetoothSerial : public Stream, public CsObjeto {
public:
    BluetoothSerial() { cs_criar("BluetoothSerial"); }
    bool begin(const String &localName = String(), bool isMaster = false)
    {
        return localName.length() ? cs_obj_i(cs_nome, "begin", localName, isMaster) != 0 : cs_obj_i(cs_nome, "begin") != 0;
    }
    bool begin(unsigned long baud) { (void)baud; return begin(); }
    void end() { cs_obj_n(cs_nome, "end"); }
    int available() override { return (int)cs_obj_i(cs_nome, "available"); }
    int read() override { return (int)cs_obj_i(cs_nome, "read"); }
    int peek() override { return (int)cs_obj_i(cs_nome, "peek"); }
    void flush() override { cs_obj_n(cs_nome, "flush"); }
    size_t write(uint8_t c) override
    {
        cs_obj_n(cs_nome, "write", (int)c);
        return 1;
    }
    size_t write(const uint8_t *b, size_t n) override
    {
        cs_obj_n(cs_nome, "print", cs_tn((const char *)b, (int)n));
        return n;
    }
    using Print::write;
    String readString() override { return cs_obj_t(cs_nome, "readString"); }
    String readStringUntil(char terminator) override { return cs_obj_t(cs_nome, "readStringUntil", terminator); }
    bool hasClient() { return cs_obj_i(cs_nome, "hasClient") != 0; }
    bool connect(const String &remoteName) { return cs_obj_i(cs_nome, "connect", remoteName) != 0; }
    bool connect() { return cs_obj_i(cs_nome, "connect") != 0; }
    bool connected(int timeout = 0) { (void)timeout; return cs_obj_i(cs_nome, "connected") != 0; }
    bool isReady(bool checkMaster = false, int timeout = 0) { (void)checkMaster; (void)timeout; return cs_obj_i(cs_nome, "isReady") != 0; }
    bool disconnect() { return cs_obj_i(cs_nome, "disconnect") != 0; }
    bool setPin(const char *pin) { return cs_obj_i(cs_nome, "setPin", pin) != 0; }
    void enableSSP() { cs_obj_n(cs_nome, "enableSSP"); }
    String getBtAddressString() { return cs_obj_t(cs_nome, "getBtAddressString"); }
    void deleteAllBondedDevices() { cs_obj_n(cs_nome, "deleteAllBondedDevices"); }
    bool setName(const String &name) { return cs_obj_i(cs_nome, "setName", name) != 0; }
};

#endif
