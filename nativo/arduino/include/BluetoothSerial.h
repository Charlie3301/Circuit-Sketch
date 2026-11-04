/* BluetoothSerial.h -- o Bluetooth classico da ESP32 (SPP): a serial sem fio,
 * com o celular de verdade ou outra placa da bancada. */
#ifndef _BLUETOOTH_SERIAL_H_
#define _BLUETOOTH_SERIAL_H_

#include "cs_objeto.h"
#if defined(ESP32)
#include "BTScan.h"
#include <functional>
#include <map>
#include <string>
#endif

/* A busca (discover) e os enderecos do Bluetooth classico: os BTScan.h,
 * BTAdvertisedDevice.h e BTAddress.h do core (so com o Bluedroid, como na placa). */
#if defined(ESP32) && SOC_BT_SUPPORTED && defined(CONFIG_BT_ENABLED) && defined(CONFIG_BLUEDROID_ENABLED)
#define CS_BT_BUSCA 1
#include "esp_spp_api.h"
typedef std::function<void(BTAdvertisedDevice *pAdvertisedDevice)> BTAdvertisedDeviceCb;
#endif

class BluetoothSerial : public Stream, public CsObjeto {
#ifdef CS_BT_BUSCA
    BTScanResultsSet achados;
    BTAdvertisedDeviceCb aoAchar;
    volatile bool buscando = false;

    /* As placas visiveis agora (o simulador manda "nome|endereco|rssi" por linha). */
    void procurar()
    {
        String lista = cs_obj_t(cs_nome, "__descobrir");
        int i = 0;
        while (i < (int)lista.length()) {
            int fim = lista.indexOf('\n', i);
            if (fim < 0) fim = lista.length();
            String linha = lista.substring(i, fim);
            i = fim + 1;
            int a = linha.indexOf('|'), b = linha.indexOf('|', a + 1);
            if (a < 0 || b < 0) continue;
            BTAdvertisedDeviceSet d;
            d.setName(linha.substring(0, a).c_str());
            d.setAddress(BTAddress(linha.substring(a + 1, b)));
            d.setRSSI((int8_t)linha.substring(b + 1).toInt());
            d.setCOD(0x1F00);
            if (achados.add(d) && aoAchar) aoAchar(&achados.m_vectorAdvertisedDevices[d.getAddress().toString().c_str()]);
        }
    }
    static void tarefaDaBusca(void *eu)
    {
        auto *bt = (BluetoothSerial *)eu;
        uint32_t ate = millis() + bt->duracao;
        while (bt->buscando && millis() < ate) {
            bt->procurar();
            delay(250);
        }
        bt->buscando = false;
        vTaskDelete(NULL);
    }
    int duracao = 0;
#endif

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
#ifdef CS_BT_BUSCA
    /* A busca dura o tempo pedido (em ms), como o inquiry da placa. */
    BTScanResults *discover(int timeout = 0x30 * 1280)
    {
        achados.clear();
        uint32_t ate = millis() + (uint32_t)timeout;
        procurar();
        while (millis() < ate) delay(10);
        procurar();
        return &achados;
    }
    bool discoverAsync(BTAdvertisedDeviceCb cb, int timeout = 0x30 * 1280)
    {
        if (buscando) return false;
        achados.clear();
        aoAchar = cb;
        duracao = timeout;
        buscando = true;
        return xTaskCreate(tarefaDaBusca, "bt_discover", 4096, this, 1, NULL) == pdPASS;
    }
    void discoverAsyncStop() { buscando = false; }
    void discoverClear() { achados.clear(); }
    BTScanResults *getScanResults() { return &achados; }
    bool connect(uint8_t remoteAddress[], int channel = 0, esp_spp_sec_t sec_mask = (ESP_SPP_SEC_ENCRYPT | ESP_SPP_SEC_AUTHENTICATE),
                 esp_spp_role_t role = ESP_SPP_ROLE_MASTER)
    {
        return connect(BTAddress(remoteAddress), channel, sec_mask, role);
    }
    bool connect(const BTAddress &remoteAddress, int channel = 0, esp_spp_sec_t sec_mask = (ESP_SPP_SEC_ENCRYPT | ESP_SPP_SEC_AUTHENTICATE),
                 esp_spp_role_t role = ESP_SPP_ROLE_MASTER)
    {
        (void)channel;
        (void)sec_mask;
        (void)role;
        return cs_obj_i(cs_nome, "__conectar_endereco", remoteAddress.toString()) != 0;
    }
    /* O SPP da outra placa: um canal, o servidor serial ("ESP32SPP", o nome do core). */
    std::map<int, std::string> getChannels(const BTAddress &remoteAddress)
    {
        std::map<int, std::string> canais;
        String lista = cs_obj_t(cs_nome, "__descobrir");
        if (lista.indexOf(remoteAddress.toString()) >= 0 || lista.indexOf(remoteAddress.toString(true)) >= 0) canais[1] = "ESP32SPP";
        return canais;
    }
    BTAddress getBtAddressObject() { return BTAddress(getBtAddressString()); }
#endif
};

#endif
