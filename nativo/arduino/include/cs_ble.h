/* cs_ble.h -- o BLE da ESP32 (a biblioteca BLE do core: Bluedroid/NimBLE),
 * pelo radio da bancada (o celular de verdade ou outra placa).
 *
 * Cada objeto (servidor, servico, caracteristica...) e o do simulador, e o
 * mesmo objeto do simulador e sempre o mesmo ponteiro aqui (cs_envolver). As
 * classes de callback que o sketch estende (BLEServerCallbacks,
 * BLECharacteristicCallbacks...) viram um objeto que o simulador chama pelo
 * nome do metodo; o despachante daqui chama o metodo virtual do sketch. */
#ifndef CS_BLE_H
#define CS_BLE_H

#include "cs_objeto.h"

/* O BLEUUID e o BLEAddress que o simulador devolve sao objetos (pelo nome):
 * o texto deles e o toString(). */
inline String cs_ble_texto(const String &v) { return v.startsWith("__volta") ? cs_obj_t(v.c_str(), "toString") : v; }

class BLEUUID {
    String texto;

public:
    BLEUUID() {}
    BLEUUID(const char *uuid) : texto(uuid) {}
    BLEUUID(const String &uuid) : texto(uuid) {}
    BLEUUID(uint16_t uuid)
    {
        char t[8];
        snprintf(t, sizeof(t), "%04x", uuid);
        texto = t;
    }
    BLEUUID(uint32_t uuid)
    {
        char t[12];
        snprintf(t, sizeof(t), "%08lx", (unsigned long)uuid);
        texto = t;
    }
    String toString() const { return texto; }
    bool equals(const BLEUUID &o) const { return texto.equalsIgnoreCase(o.texto); }
    bool operator==(const BLEUUID &o) const { return equals(o); }
    int bitSize() const { return texto.length() > 8 ? 128 : texto.length() > 4 ? 32 : 16; }
};
inline cs_val cs_de(const BLEUUID &u)
{
    static thread_local String t[8];
    static thread_local int k;
    String &s = t[k++ & 7];
    s = u.toString();
    return cs_tn(s.c_str(), (int)s.length());
}

class BLEAddress {
    String texto;

public:
    BLEAddress() {}
    BLEAddress(const String &t) : texto(t) {}
    BLEAddress(const char *t) : texto(t) {}
    String toString() const { return texto; }
    bool equals(const BLEAddress &o) const { return texto.equalsIgnoreCase(o.texto); }
    bool operator==(const BLEAddress &o) const { return equals(o); }
};
inline cs_val cs_de(const BLEAddress &a)
{
    static thread_local String t[8];
    static thread_local int k;
    String &s = t[k++ & 7];
    s = a.toString();
    return cs_tn(s.c_str(), (int)s.length());
}

typedef union {
    struct {
        uint16_t conn_id;
        uint8_t remote_bda[6];
    } connect, disconnect;
} esp_ble_gatts_cb_param_t;
typedef struct {
    uint16_t conn_handle;
} ble_gap_conn_desc;

class BLEServer;
class BLEService;
class BLECharacteristic;
class BLEDescriptor;
class BLEAdvertising;
class BLEScan;
class BLEClient;

/* ---- os callbacks ---- */
class BLEServerCallbacks {
public:
    virtual ~BLEServerCallbacks() {}
    virtual void onConnect(BLEServer *) {}
    virtual void onConnect(BLEServer *, esp_ble_gatts_cb_param_t *) {}
    virtual void onDisconnect(BLEServer *) {}
    virtual void onDisconnect(BLEServer *, esp_ble_gatts_cb_param_t *) {}
    virtual void onMtuChanged(BLEServer *, esp_ble_gatts_cb_param_t *) {}
    /* Os do NimBLE (o BLE do C6/H2 e do core 3.x com NimBLE). */
    virtual void onConnect(BLEServer *, ble_gap_conn_desc *) {}
    virtual void onDisconnect(BLEServer *, ble_gap_conn_desc *) {}
};

class BLECharacteristicCallbacks {
public:
    typedef enum { SUCCESS_INDICATE, SUCCESS_NOTIFY, ERROR_INDICATE_DISABLED, ERROR_NOTIFY_DISABLED, ERROR_GATT,
                   ERROR_NO_CLIENT, ERROR_INDICATE_TIMEOUT, ERROR_INDICATE_FAILURE } Status;
    virtual ~BLECharacteristicCallbacks() {}
    virtual void onRead(BLECharacteristic *) {}
    virtual void onRead(BLECharacteristic *, esp_ble_gatts_cb_param_t *) {}
    virtual void onWrite(BLECharacteristic *) {}
    virtual void onWrite(BLECharacteristic *, esp_ble_gatts_cb_param_t *) {}
    virtual void onNotify(BLECharacteristic *) {}
    virtual void onStatus(BLECharacteristic *, Status, uint32_t) {}
    virtual void onRead(BLECharacteristic *, ble_gap_conn_desc *) {}
    virtual void onWrite(BLECharacteristic *, ble_gap_conn_desc *) {}
    virtual void onSubscribe(BLECharacteristic *, ble_gap_conn_desc *, uint16_t) {}
};

class BLEDescriptorCallbacks {
public:
    virtual ~BLEDescriptorCallbacks() {}
    virtual void onRead(BLEDescriptor *) {}
    virtual void onWrite(BLEDescriptor *) {}
};

/* O objeto de callbacks do simulador que despacha para o do sketch: o nome
 * dele (o mesmo para o mesmo objeto do sketch). */
typedef void (*cs_despachar_t)(void *cb, const char *metodo, int n, const cs_val *args);
const char *cs_callbacks_do_cpp(cs_despachar_t despachar, void *cb);

/* ---- os objetos ---- */
class BLEDescriptor : public CsObjeto {
public:
    explicit BLEDescriptor(const String &nome) { cs_adotar(nome); }
    BLEDescriptor(const char *uuid, uint16_t len = 100) { (void)len; cs_criar("BLEDescriptor", uuid); }
    BLEDescriptor(BLEUUID uuid, uint16_t len = 100) { (void)len; cs_criar("BLEDescriptor", uuid); }

protected:
    BLEDescriptor() {}

public:
    void setValue(const String &v) { cs_obj_n(cs_nome, "setValue", v); }
    void setValue(uint8_t *data, size_t length) { cs_obj_n(cs_nome, "setValue", cs_vetor(data, (int)length), (int)length); }
    String getValue() { return cs_obj_t(cs_nome, "getValue"); }
    size_t getLength() { return (size_t)cs_obj_i(cs_nome, "getLength"); }
    BLEUUID getUUID() { return BLEUUID(cs_ble_texto(cs_obj_t(cs_nome, "getUUID"))); }
    void setAccessPermissions(uint16_t perm) { cs_obj_n(cs_nome, "setAccessPermissions", perm); }
    void setCallbacks(BLEDescriptorCallbacks *cb) { (void)cb; }
};

class BLE2902 : public BLEDescriptor {
public:
    BLE2902() { cs_criar("BLE2902"); }
    void setNotifications(bool flag) { cs_obj_n(cs_nome, "setNotifications", flag); }
    void setIndications(bool flag) { cs_obj_n(cs_nome, "setIndications", flag); }
    bool getNotifications() { return cs_obj_i(cs_nome, "getNotifications") != 0; }
    bool getIndications() { return cs_obj_i(cs_nome, "getIndications") != 0; }
};

class BLE2901 : public BLEDescriptor {
public:
    BLE2901() { cs_criar("BLE2901"); }
    void setDescription(const String &d) { cs_obj_n(cs_nome, "setDescription", d); }
};

class BLE2904 : public BLEDescriptor {
public:
    static const uint8_t FORMAT_BOOLEAN = 1, FORMAT_UINT8 = 4, FORMAT_UINT16 = 6, FORMAT_UINT32 = 8, FORMAT_SINT8 = 12,
                         FORMAT_SINT16 = 14, FORMAT_SINT32 = 16, FORMAT_FLOAT32 = 20, FORMAT_UTF8 = 25;
    BLE2904() { cs_criar("BLE2904"); }
    void setFormat(uint8_t f) { cs_obj_n(cs_nome, "setFormat", f); }
    void setUnit(uint16_t u) { cs_obj_n(cs_nome, "setUnit", u); }
    void setExponent(int8_t e) { cs_obj_n(cs_nome, "setExponent", e); }
    void setNamespace(uint8_t n) { cs_obj_n(cs_nome, "setNamespace", n); }
    void setDescription(uint16_t d) { cs_obj_n(cs_nome, "setDescription", d); }
};

class BLECharacteristic : public CsObjeto {
public:
    static const uint32_t PROPERTY_READ = 1 << 0;
    static const uint32_t PROPERTY_WRITE = 1 << 1;
    static const uint32_t PROPERTY_NOTIFY = 1 << 2;
    static const uint32_t PROPERTY_BROADCAST = 1 << 3;
    static const uint32_t PROPERTY_INDICATE = 1 << 4;
    static const uint32_t PROPERTY_WRITE_NR = 1 << 5;

    explicit BLECharacteristic(const String &nome) { cs_adotar(nome); }
    BLECharacteristic(const char *uuid, uint32_t properties = 0) { cs_criar("BLECharacteristic", uuid, properties); }
    BLECharacteristic(BLEUUID uuid, uint32_t properties = 0) { cs_criar("BLECharacteristic", uuid, properties); }

    void setValue(const String &v) { cs_obj_n(cs_nome, "setValue", v); }
    void setValue(const char *v) { cs_obj_n(cs_nome, "setValue", v); }
    void setValue(std::string v) { cs_obj_n(cs_nome, "setValue", v.c_str()); }
    void setValue(uint8_t *data, size_t size) { cs_obj_n(cs_nome, "setValue", cs_vetor(data, (int)size), (int)size); }
    void setValue(const uint8_t *data, size_t size) { cs_obj_n(cs_nome, "setValue", cs_vetor(data, (int)size), (int)size); }
    void setValue(uint16_t v) { cs_obj_n(cs_nome, "setValue", v); }
    void setValue(uint32_t v) { cs_obj_n(cs_nome, "setValue", v); }
    void setValue(int v) { cs_obj_n(cs_nome, "setValue", v); }
    void setValue(float v) { cs_obj_n(cs_nome, "setValue", v); }
    void setValue(double v) { cs_obj_n(cs_nome, "setValue", v); }
    String getValue() { return cs_obj_t(cs_nome, "getValue"); }
    uint8_t *getData()
    {
        static String guardado;
        guardado = getValue();
        return (uint8_t *)guardado.c_str();
    }
    size_t getLength() { return (size_t)cs_obj_i(cs_nome, "getLength"); }
    void notify(bool is_notification = true) { (void)is_notification; cs_obj_n(cs_nome, "notify"); }
    void indicate() { cs_obj_n(cs_nome, "indicate"); }
    void addDescriptor(BLEDescriptor *d) { cs_obj_n(cs_nome, "addDescriptor", d); }
    BLEDescriptor *getDescriptorByUUID(const char *uuid) { return cs_envolver<BLEDescriptor>(cs_obj_t(cs_nome, "getDescriptorByUUID", uuid)); }
    BLEDescriptor *getDescriptorByUUID(BLEUUID uuid) { return cs_envolver<BLEDescriptor>(cs_obj_t(cs_nome, "getDescriptorByUUID", uuid)); }
    BLEUUID getUUID() { return BLEUUID(cs_ble_texto(cs_obj_t(cs_nome, "getUUID"))); }
    uint16_t getHandle() { return (uint16_t)cs_obj_i(cs_nome, "getHandle"); }
    void setCallbacks(BLECharacteristicCallbacks *cb);
    void setReadProperty(bool v) { cs_obj_n(cs_nome, "setReadProperty", v); }
    void setWriteProperty(bool v) { cs_obj_n(cs_nome, "setWriteProperty", v); }
    void setNotifyProperty(bool v) { cs_obj_n(cs_nome, "setNotifyProperty", v); }
    void setIndicateProperty(bool v) { cs_obj_n(cs_nome, "setIndicateProperty", v); }
    void setBroadcastProperty(bool v) { cs_obj_n(cs_nome, "setBroadcastProperty", v); }
    void setWriteNoResponseProperty(bool v) { cs_obj_n(cs_nome, "setWriteNoResponseProperty", v); }
    void setAccessPermissions(uint16_t perm) { cs_obj_n(cs_nome, "setAccessPermissions", perm); }
    String toString() { return cs_obj_t(cs_nome, "toString"); }
};

class BLEService : public CsObjeto {
public:
    explicit BLEService(const String &nome) { cs_adotar(nome); }
    BLECharacteristic *createCharacteristic(const char *uuid, uint32_t properties)
    {
        return cs_envolver<BLECharacteristic>(cs_obj_t(cs_nome, "createCharacteristic", uuid, properties));
    }
    BLECharacteristic *createCharacteristic(BLEUUID uuid, uint32_t properties)
    {
        return cs_envolver<BLECharacteristic>(cs_obj_t(cs_nome, "createCharacteristic", uuid, properties));
    }
    void addCharacteristic(BLECharacteristic *c) { cs_obj_n(cs_nome, "addCharacteristic", c); }
    BLECharacteristic *getCharacteristic(const char *uuid) { return cs_envolver<BLECharacteristic>(cs_obj_t(cs_nome, "getCharacteristic", uuid)); }
    BLECharacteristic *getCharacteristic(BLEUUID uuid) { return cs_envolver<BLECharacteristic>(cs_obj_t(cs_nome, "getCharacteristic", uuid)); }
    void start() { cs_obj_n(cs_nome, "start"); }
    void stop() { cs_obj_n(cs_nome, "stop"); }
    BLEUUID getUUID() { return BLEUUID(cs_ble_texto(cs_obj_t(cs_nome, "getUUID"))); }
    uint16_t getHandle() { return (uint16_t)cs_obj_i(cs_nome, "getHandle"); }
    String toString() { return cs_obj_t(cs_nome, "toString"); }
};

class BLEAdvertisementData : public CsObjeto {
public:
    BLEAdvertisementData() { cs_criar("BLEAdvertisementData"); }
    void setName(const String &name) { cs_obj_n(cs_nome, "setName", name); }
    void setShortName(const String &name) { cs_obj_n(cs_nome, "setShortName", name); }
    void setFlags(uint8_t flags) { cs_obj_n(cs_nome, "setFlags", flags); }
    void setAppearance(uint16_t a) { cs_obj_n(cs_nome, "setAppearance", a); }
    void setManufacturerData(const String &data) { cs_obj_n(cs_nome, "setManufacturerData", data); }
    void setServiceData(BLEUUID uuid, const String &data) { cs_obj_n(cs_nome, "setServiceData", uuid, data); }
    void setCompleteServices(BLEUUID uuid) { cs_obj_n(cs_nome, "setCompleteServices", uuid); }
    void setPartialServices(BLEUUID uuid) { cs_obj_n(cs_nome, "setPartialServices", uuid); }
    void addData(const String &data) { cs_obj_n(cs_nome, "addData", data); }
};

class BLEAdvertising : public CsObjeto {
public:
    explicit BLEAdvertising(const String &nome) { cs_adotar(nome); }
    void addServiceUUID(const char *uuid) { cs_obj_n(cs_nome, "addServiceUUID", uuid); }
    void addServiceUUID(BLEUUID uuid) { cs_obj_n(cs_nome, "addServiceUUID", uuid); }
    void removeServiceUUID(BLEUUID uuid) { cs_obj_n(cs_nome, "removeServiceUUID", uuid); }
    bool start() { cs_obj_n(cs_nome, "start"); return true; }
    bool stop() { cs_obj_n(cs_nome, "stop"); return true; }
    void setScanResponse(bool v) { cs_obj_n(cs_nome, "setScanResponse", v); }
    void setMinPreferred(uint16_t v) { cs_obj_n(cs_nome, "setMinPreferred", v); }
    void setMaxPreferred(uint16_t v) { cs_obj_n(cs_nome, "setMaxPreferred", v); }
    void setMinInterval(uint16_t v) { cs_obj_n(cs_nome, "setMinInterval", v); }
    void setMaxInterval(uint16_t v) { cs_obj_n(cs_nome, "setMaxInterval", v); }
    void setAppearance(uint16_t v) { cs_obj_n(cs_nome, "setAppearance", v); }
    void setAdvertisementType(uint8_t v) { cs_obj_n(cs_nome, "setAdvertisementType", v); }
    void setAdvertisementData(BLEAdvertisementData &d) { cs_obj_n(cs_nome, "setAdvertisementData", d); }
    void setScanResponseData(BLEAdvertisementData &d) { cs_obj_n(cs_nome, "setScanResponseData", d); }
    void setDeviceAddress(uint8_t *addr, uint8_t type = 0) { (void)addr; (void)type; }
};

class BLEServer : public CsObjeto {
public:
    explicit BLEServer(const String &nome) { cs_adotar(nome); }
    BLEService *createService(const char *uuid) { return cs_envolver<BLEService>(cs_obj_t(cs_nome, "createService", uuid)); }
    BLEService *createService(BLEUUID uuid, uint32_t numHandles = 15, uint8_t inst_id = 0)
    {
        (void)inst_id;
        return cs_envolver<BLEService>(cs_obj_t(cs_nome, "createService", uuid, numHandles));
    }
    BLEService *getServiceByUUID(const char *uuid) { return cs_envolver<BLEService>(cs_obj_t(cs_nome, "getServiceByUUID", uuid)); }
    BLEAdvertising *getAdvertising() { return cs_envolver<BLEAdvertising>(cs_obj_t(cs_nome, "getAdvertising")); }
    void setCallbacks(BLEServerCallbacks *cb);
    void startAdvertising() { cs_obj_n(cs_nome, "startAdvertising"); }
    void stopAdvertising() { cs_obj_n(cs_nome, "stopAdvertising"); }
    uint32_t getConnectedCount() { return (uint32_t)cs_obj_i(cs_nome, "getConnectedCount"); }
    void disconnect(uint16_t connId) { cs_obj_n(cs_nome, "disconnect", connId); }
    void advertiseOnDisconnect(bool v) { cs_obj_n(cs_nome, "advertiseOnDisconnect", v); }
    void removeService(BLEService *s) { cs_obj_n(cs_nome, "removeService", s); }
    uint16_t getPeerMTU(uint16_t conn_id) { return (uint16_t)cs_obj_i(cs_nome, "getPeerMTU", conn_id); }
    void updateConnParams(uint8_t *remote_bda, uint16_t minInterval, uint16_t maxInterval, uint16_t latency, uint16_t timeout)
    {
        (void)remote_bda;
        (void)minInterval;
        (void)maxInterval;
        (void)latency;
        (void)timeout;
    }
};

class BLESecurity : public CsObjeto {
public:
    BLESecurity() { cs_criar("BLESecurity"); }
    void setAuthenticationMode(uint8_t mode) { (void)mode; }
    void setCapability(uint8_t cap) { (void)cap; }
    void setInitEncryptionKey(uint8_t key) { (void)key; }
    void setStaticPIN(uint32_t pin) { (void)pin; }
};

/* ---- o cliente ---- */
class BLERemoteCharacteristic : public CsObjeto {
public:
    explicit BLERemoteCharacteristic(const String &nome) { cs_adotar(nome); }
    String readValue() { return cs_obj_t(cs_nome, "readValue"); }
    uint8_t readUInt8() { return (uint8_t)cs_obj_i(cs_nome, "readUInt8"); }
    uint16_t readUInt16() { return (uint16_t)cs_obj_i(cs_nome, "readUInt16"); }
    uint32_t readUInt32() { return (uint32_t)cs_obj_i(cs_nome, "readUInt32"); }
    float readFloat() { return (float)cs_obj_r(cs_nome, "readFloat"); }
    void writeValue(const String &v, bool response = false) { cs_obj_n(cs_nome, "writeValue", v, response); }
    void writeValue(const char *v, bool response = false) { cs_obj_n(cs_nome, "writeValue", v, response); }
    void writeValue(uint8_t v, bool response = false) { cs_obj_n(cs_nome, "writeValue", v, response); }
    void writeValue(uint8_t *data, size_t length, bool response = false)
    {
        cs_obj_n(cs_nome, "writeValue", cs_vetor(data, (int)length), (int)length, response);
    }
    bool canRead() { return cs_obj_i(cs_nome, "canRead") != 0; }
    bool canWrite() { return cs_obj_i(cs_nome, "canWrite") != 0; }
    bool canNotify() { return cs_obj_i(cs_nome, "canNotify") != 0; }
    bool canIndicate() { return cs_obj_i(cs_nome, "canIndicate") != 0; }
    bool canWriteNoResponse() { return cs_obj_i(cs_nome, "canWriteNoResponse") != 0; }
    BLEUUID getUUID() { return BLEUUID(cs_ble_texto(cs_obj_t(cs_nome, "getUUID"))); }
    uint16_t getHandle() { return (uint16_t)cs_obj_i(cs_nome, "getHandle"); }
    typedef void (*notify_callback)(BLERemoteCharacteristic *, uint8_t *, size_t, bool);
    void registerForNotify(notify_callback cb, bool notifications = true, bool descriptorRequiresRegistration = true);
    String toString() { return cs_obj_t(cs_nome, "toString"); }
};

class BLERemoteService : public CsObjeto {
public:
    explicit BLERemoteService(const String &nome) { cs_adotar(nome); }
    BLERemoteCharacteristic *getCharacteristic(const char *uuid)
    {
        return cs_envolver<BLERemoteCharacteristic>(cs_obj_t(cs_nome, "getCharacteristic", uuid));
    }
    BLERemoteCharacteristic *getCharacteristic(BLEUUID uuid)
    {
        return cs_envolver<BLERemoteCharacteristic>(cs_obj_t(cs_nome, "getCharacteristic", uuid));
    }
    String getValue(BLEUUID uuid) { return cs_obj_t(cs_nome, "getValue", uuid); }
    void setValue(BLEUUID uuid, const String &v) { cs_obj_n(cs_nome, "setValue", uuid, v); }
    BLEUUID getUUID() { return BLEUUID(cs_ble_texto(cs_obj_t(cs_nome, "getUUID"))); }
};

class BLEAdvertisedDevice : public CsObjeto {
public:
    BLEAdvertisedDevice() {}
    explicit BLEAdvertisedDevice(const String &nome) { cs_adotar(nome); }
    String getName() { return cs_obj_t(cs_nome, "getName"); }
    BLEAddress getAddress() { return BLEAddress(cs_ble_texto(cs_obj_t(cs_nome, "getAddress"))); }
    int getRSSI() { return (int)cs_obj_i(cs_nome, "getRSSI"); }
    int8_t getTXPower() { return (int8_t)cs_obj_i(cs_nome, "getTXPower"); }
    uint16_t getAppearance() { return (uint16_t)cs_obj_i(cs_nome, "getAppearance"); }
    String getManufacturerData() { return cs_obj_t(cs_nome, "getManufacturerData"); }
    String getServiceData() { return cs_obj_t(cs_nome, "getServiceData"); }
    BLEUUID getServiceUUID() { return BLEUUID(cs_ble_texto(cs_obj_t(cs_nome, "getServiceUUID"))); }
    int getServiceUUIDCount() { return (int)cs_obj_i(cs_nome, "getServiceUUIDCount"); }
    bool haveName() { return cs_obj_i(cs_nome, "haveName") != 0; }
    bool haveRSSI() { return cs_obj_i(cs_nome, "haveRSSI") != 0; }
    bool haveServiceUUID() { return cs_obj_i(cs_nome, "haveServiceUUID") != 0; }
    bool haveManufacturerData() { return cs_obj_i(cs_nome, "haveManufacturerData") != 0; }
    bool haveServiceData() { return cs_obj_i(cs_nome, "haveServiceData") != 0; }
    bool haveTXPower() { return cs_obj_i(cs_nome, "haveTXPower") != 0; }
    bool haveAppearance() { return cs_obj_i(cs_nome, "haveAppearance") != 0; }
    bool isAdvertisingService(BLEUUID uuid) { return cs_obj_i(cs_nome, "isAdvertisingService", uuid) != 0; }
    bool isConnectable() { return cs_obj_i(cs_nome, "isConnectable") != 0; }
    String toString() { return cs_obj_t(cs_nome, "toString"); }
};

class BLEAdvertisedDeviceCallbacks {
public:
    virtual ~BLEAdvertisedDeviceCallbacks() {}
    virtual void onResult(BLEAdvertisedDevice advertisedDevice) = 0;
};

class BLEScanResults : public CsObjeto {
public:
    BLEScanResults() {}
    explicit BLEScanResults(const String &nome) { cs_adotar(nome); }
    int getCount() { return cs_nome[0] ? (int)cs_obj_i(cs_nome, "getCount") : 0; }
    BLEAdvertisedDevice getDevice(uint32_t i) { return BLEAdvertisedDevice(cs_obj_t(cs_nome, "getDevice", i)); }
};

class BLEScan : public CsObjeto {
public:
    explicit BLEScan(const String &nome) { cs_adotar(nome); }
    void setAdvertisedDeviceCallbacks(BLEAdvertisedDeviceCallbacks *cb, bool wantDuplicates = false, bool shouldParse = true);
    void setActiveScan(bool active) { cs_obj_n(cs_nome, "setActiveScan", active); }
    void setInterval(uint16_t i) { cs_obj_n(cs_nome, "setInterval", i); }
    void setWindow(uint16_t w) { cs_obj_n(cs_nome, "setWindow", w); }
    void setDuplicateFilter(bool v) { cs_obj_n(cs_nome, "setDuplicateFilter", v); }
    BLEScanResults *start(uint32_t duration, bool is_continue = false)
    {
        (void)is_continue;
        static BLEScanResults r;
        r = BLEScanResults(cs_obj_t(cs_nome, "start", duration));
        return &r;
    }
    bool start(uint32_t duration, void (*scanCompleteCB)(BLEScanResults), bool is_continue = false)
    {
        (void)scanCompleteCB;
        start(duration, is_continue);
        return true;
    }
    void stop() { cs_obj_n(cs_nome, "stop"); }
    void clearResults() { cs_obj_n(cs_nome, "clearResults"); }
    bool isScanning() { return cs_obj_i(cs_nome, "isScanning") != 0; }
    BLEScanResults *getResults()
    {
        static BLEScanResults r;
        r = BLEScanResults(cs_obj_t(cs_nome, "getResults"));
        return &r;
    }
};

class BLEClientCallbacks {
public:
    virtual ~BLEClientCallbacks() {}
    virtual void onConnect(BLEClient *) {}
    virtual void onDisconnect(BLEClient *) {}
};

class BLEClient : public CsObjeto {
public:
    explicit BLEClient(const String &nome) { cs_adotar(nome); }
    bool connect(BLEAdvertisedDevice *device) { return cs_obj_i(cs_nome, "connect", device) != 0; }
    bool connect(BLEAddress address, uint8_t type = 0) { (void)type; return cs_obj_i(cs_nome, "connect", address) != 0; }
    void disconnect() { cs_obj_n(cs_nome, "disconnect"); }
    bool isConnected() { return cs_obj_i(cs_nome, "isConnected") != 0; }
    BLERemoteService *getService(const char *uuid) { return cs_envolver<BLERemoteService>(cs_obj_t(cs_nome, "getService", uuid)); }
    BLERemoteService *getService(BLEUUID uuid) { return cs_envolver<BLERemoteService>(cs_obj_t(cs_nome, "getService", uuid)); }
    void setClientCallbacks(BLEClientCallbacks *cb);
    int getRssi() { return (int)cs_obj_i(cs_nome, "getRssi"); }
    uint16_t getMTU() { return (uint16_t)cs_obj_i(cs_nome, "getMTU"); }
    bool setMTU(uint16_t mtu) { return cs_obj_i(cs_nome, "setMTU", mtu) != 0; }
    BLEAddress getPeerAddress() { return BLEAddress(cs_ble_texto(cs_obj_t(cs_nome, "getPeerAddress"))); }
};

/* ---- o BLEDevice (tudo static) ---- */
class BLEDevice {
public:
    static void init(const String &deviceName) { cs_obj_n(nullptr, "BLEDevice::init", deviceName); }
    static void init(const char *deviceName) { cs_obj_n(nullptr, "BLEDevice::init", deviceName); }
    static void deinit(bool release_memory = false) { cs_obj_n(nullptr, "BLEDevice::deinit", release_memory); }
    static BLEServer *createServer() { return cs_envolver<BLEServer>(cs_obj_t(nullptr, "BLEDevice::createServer")); }
    static BLEClient *createClient() { return cs_envolver<BLEClient>(cs_obj_t(nullptr, "BLEDevice::createClient")); }
    static BLEAdvertising *getAdvertising() { return cs_envolver<BLEAdvertising>(cs_obj_t(nullptr, "BLEDevice::getAdvertising")); }
    static BLEScan *getScan() { return cs_envolver<BLEScan>(cs_obj_t(nullptr, "BLEDevice::getScan")); }
    static void startAdvertising() { cs_obj_n(nullptr, "BLEDevice::startAdvertising"); }
    static void stopAdvertising() { cs_obj_n(nullptr, "BLEDevice::stopAdvertising"); }
    static BLEAddress getAddress() { return BLEAddress(cs_ble_texto(cs_obj_t(nullptr, "BLEDevice::getAddress"))); }
    static void setPower(int powerLevel) { cs_obj_n(nullptr, "BLEDevice::setPower", powerLevel); }
    static int getPower() { return (int)cs_obj_i(nullptr, "BLEDevice::getPower"); }
    static void setMTU(uint16_t mtu) { cs_obj_n(nullptr, "BLEDevice::setMTU", mtu); }
    static uint16_t getMTU() { return (uint16_t)cs_obj_i(nullptr, "BLEDevice::getMTU"); }
    static void setEncryptionLevel(int level) { cs_obj_n(nullptr, "BLEDevice::setEncryptionLevel", level); }
    static void setSecurityCallbacks(void *cb) { (void)cb; }
    static String toString() { return cs_obj_t(nullptr, "BLEDevice::toString"); }
};

#define ESP_PWR_LVL_N12 0
#define ESP_PWR_LVL_N9 1
#define ESP_PWR_LVL_N6 2
#define ESP_PWR_LVL_N3 3
#define ESP_PWR_LVL_N0 4
#define ESP_PWR_LVL_P3 5
#define ESP_PWR_LVL_P6 6
#define ESP_PWR_LVL_P9 7
#define ESP_GATT_PERM_READ (1 << 0)
#define ESP_GATT_PERM_WRITE (1 << 4)

/* ---- os despachantes: o metodo que o simulador chamou, no objeto do sketch ---- */
inline String cs_ble_arg(int n, const cs_val *a, int i)
{
    return i < n && a[i].s ? String(a[i].s, (unsigned)a[i].n) : String();
}

inline void BLEServer::setCallbacks(BLEServerCallbacks *cb)
{
    struct D {
        static void despachar(void *p, const char *m, int n, const cs_val *a)
        {
            auto *cb = (BLEServerCallbacks *)p;
            BLEServer *s = cs_envolver<BLEServer>(cs_ble_arg(n, a, 0));
            static esp_ble_gatts_cb_param_t param;
            static ble_gap_conn_desc desc;
            if (!strcmp(m, "onConnect")) {
                cb->onConnect(s);
                cb->onConnect(s, &param);
                cb->onConnect(s, &desc);
            } else if (!strcmp(m, "onDisconnect")) {
                cb->onDisconnect(s);
                cb->onDisconnect(s, &param);
                cb->onDisconnect(s, &desc);
            }
        }
    };
    if (!cb) return cs_obj_n(cs_nome, "setCallbacks", 0);
    cs_obj_n(cs_nome, "setCallbacks", cs_nome_de{cs_callbacks_do_cpp(&D::despachar, cb)});
}

inline void BLECharacteristic::setCallbacks(BLECharacteristicCallbacks *cb)
{
    struct D {
        static void despachar(void *p, const char *m, int n, const cs_val *a)
        {
            auto *cb = (BLECharacteristicCallbacks *)p;
            BLECharacteristic *c = cs_envolver<BLECharacteristic>(cs_ble_arg(n, a, 0));
            static esp_ble_gatts_cb_param_t param;
            static ble_gap_conn_desc desc;
            if (!strcmp(m, "onWrite")) {
                cb->onWrite(c);
                cb->onWrite(c, &param);
                cb->onWrite(c, &desc);
            } else if (!strcmp(m, "onRead")) {
                cb->onRead(c);
                cb->onRead(c, &param);
                cb->onRead(c, &desc);
            } else if (!strcmp(m, "onNotify")) {
                cb->onNotify(c);
            } else if (!strcmp(m, "onStatus")) {
                cb->onStatus(c, (BLECharacteristicCallbacks::Status)(n > 1 ? a[1].i : 0), (uint32_t)(n > 2 ? a[2].i : 0));
            } else if (!strcmp(m, "onSubscribe")) {
                cb->onSubscribe(c, &desc, (uint16_t)(n > 2 ? a[2].i : 0));
            }
        }
    };
    if (!cb) return cs_obj_n(cs_nome, "setCallbacks", 0);
    cs_obj_n(cs_nome, "setCallbacks", cs_nome_de{cs_callbacks_do_cpp(&D::despachar, cb)});
}

inline void BLEClient::setClientCallbacks(BLEClientCallbacks *cb)
{
    struct D {
        static void despachar(void *p, const char *m, int n, const cs_val *a)
        {
            auto *cb = (BLEClientCallbacks *)p;
            BLEClient *c = cs_envolver<BLEClient>(cs_ble_arg(n, a, 0));
            if (!strcmp(m, "onConnect")) cb->onConnect(c);
            else if (!strcmp(m, "onDisconnect")) cb->onDisconnect(c);
        }
    };
    if (!cb) return cs_obj_n(cs_nome, "setClientCallbacks", 0);
    cs_obj_n(cs_nome, "setClientCallbacks", cs_nome_de{cs_callbacks_do_cpp(&D::despachar, cb)});
}

inline void BLEScan::setAdvertisedDeviceCallbacks(BLEAdvertisedDeviceCallbacks *cb, bool wantDuplicates, bool shouldParse)
{
    (void)shouldParse;
    struct D {
        static void despachar(void *p, const char *m, int n, const cs_val *a)
        {
            if (!strcmp(m, "onResult")) ((BLEAdvertisedDeviceCallbacks *)p)->onResult(BLEAdvertisedDevice(cs_ble_arg(n, a, 0)));
        }
    };
    if (!cb) return cs_obj_n(cs_nome, "setAdvertisedDeviceCallbacks", 0, wantDuplicates);
    cs_obj_n(cs_nome, "setAdvertisedDeviceCallbacks", cs_nome_de{cs_callbacks_do_cpp(&D::despachar, cb)}, wantDuplicates);
}

inline void BLERemoteCharacteristic::registerForNotify(notify_callback cb, bool notifications, bool descriptorRequiresRegistration)
{
    (void)descriptorRequiresRegistration;
    if (!cb) return cs_obj_n(cs_nome, "registerForNotify", 0, notifications);
    cs_obj_n(cs_nome, "registerForNotify", cb, notifications);
}

#endif
