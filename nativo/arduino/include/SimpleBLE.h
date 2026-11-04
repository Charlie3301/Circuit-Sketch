/* SimpleBLE.h -- a biblioteca SimpleBLE do core: so anuncia um nome por BLE
 * (o "beacon" mais simples). No simulador, pelo radio da bancada. */
#ifndef _SIMPLE_BLE_H_
#define _SIMPLE_BLE_H_

#include "soc/soc_caps.h"
#include "sdkconfig.h"
#if defined(SOC_BLE_SUPPORTED) || defined(CONFIG_ESP_HOSTED_ENABLE_BT_NIMBLE)
#if defined(CONFIG_BLUEDROID_ENABLED) || defined(CONFIG_NIMBLE_ENABLED)
#include "Arduino.h"
#include "cs_ble.h"

class SimpleBLE {
public:
    SimpleBLE(void) {}
    ~SimpleBLE(void) { end(); }
    /* Comeca a anunciar o nome (o do chip, sem nome: "ESP32"). */
    bool begin(String localName = String())
    {
        local_name = localName.length() ? localName : String("ESP32");
        BLEDevice::init(local_name);
        BLEDevice::getAdvertising()->start();
        ligado = true;
        return true;
    }
    void end(void)
    {
        if (!ligado) return;
        BLEDevice::getAdvertising()->stop();
        BLEDevice::deinit();
        ligado = false;
    }

private:
    String local_name;
    bool ligado = false;
};

#endif
#endif
#endif
