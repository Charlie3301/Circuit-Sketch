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

/* Os bytes de um String em hexadecimal, e de volta (o anuncio passa pelo simulador assim). */
inline String cs_ble_hex(const String &bytes)
{
    String h;
    char t[3];
    for (unsigned i = 0; i < bytes.length(); i++) {
        snprintf(t, sizeof(t), "%02x", (uint8_t)bytes[i]);
        h += t;
    }
    return h;
}
inline String cs_ble_de_hex(const String &hex)
{
    String b;
    for (unsigned i = 0; i + 1 < hex.length(); i += 2) b += (char)strtoul(hex.substring(i, i + 2).c_str(), nullptr, 16);
    return b;
}

/* O UUID como o Bluedroid (uuid.uuid16/uuid32/uuid128) e o NimBLE
 * (u16/u32/u128.value) o guardam, para o codigo do core que mexe nos bytes
 * (o BLEBeacon, o Eddystone): os 128 bits do menos para o mais significativo. */
struct cs_uuid_nativo {
    uint16_t len;
    union {
        uint16_t uuid16;
        uint32_t uuid32;
        uint8_t uuid128[16];
    } uuid;
    struct {
        uint8_t type;
        uint16_t value;
    } u16;
    struct {
        uint8_t type;
        uint32_t value;
    } u32;
    struct {
        uint8_t type;
        uint8_t value[16];
    } u128;
};

#if defined(ESP32) && !defined(CONFIG_IDF_TARGET_ESP32S2)
#include "esp_bt_defs.h"
#endif

class BLEUUID {
    String texto;
    mutable cs_uuid_nativo nativo;

    /* Os 16 bytes do texto, do mais significativo para o menos (o "0000xxxx-0000-1000-8000-00805f9b34fb" no curto). */
    bool bytes128(uint8_t b[16]) const
    {
        String t = to128().texto;
        t.replace("-", "");
        if (t.length() != 32) return false;
        for (int i = 0; i < 16; i++) b[i] = (uint8_t)strtoul(t.substring(2 * i, 2 * i + 2).c_str(), nullptr, 16);
        return true;
    }

public:
    BLEUUID() {}
    /* Os bytes do UUID de 128 bits (msbFirst: o primeiro byte e o mais significativo, a ordem do texto). */
    BLEUUID(const uint8_t *pData, size_t size, bool msbFirst)
    {
        if (size != 16) return;
        char t[37];
        int k = 0;
        for (int i = 0; i < 16; i++) {
            if (i == 4 || i == 6 || i == 8 || i == 10) t[k++] = '-';
            snprintf(t + k, 3, "%02x", pData[msbFirst ? i : 15 - i]);
            k += 2;
        }
        texto = t;
    }
#if defined(ESP32) && !defined(CONFIG_IDF_TARGET_ESP32S2)
    BLEUUID(esp_bt_uuid_t u)
    {
        if (u.len == ESP_UUID_LEN_16) *this = BLEUUID(u.uuid.uuid16);
        else if (u.len == ESP_UUID_LEN_32) *this = BLEUUID(u.uuid.uuid32);
        else if (u.len == ESP_UUID_LEN_128) *this = BLEUUID(u.uuid.uuid128, 16, false);
    }
#endif
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
    static BLEUUID fromString(String uuid) { return BLEUUID(uuid); }
    /* O de 128 bits so encurta se estiver na base do Bluetooth. */
    BLEUUID to16() const
    {
        if (bitSize() == 16) return *this;
        if (bitSize() == 32) return texto.startsWith("0000") ? BLEUUID(texto.substring(4)) : BLEUUID();
        return texto.startsWith("0000") && texto.substring(8).equalsIgnoreCase("-0000-1000-8000-00805f9b34fb") ? BLEUUID(texto.substring(4, 8)) : BLEUUID();
    }
    bool equals(const BLEUUID &o) const { return texto.equalsIgnoreCase(o.texto); }
    bool operator==(const BLEUUID &o) const { return equals(o); }
    int bitSize() const { return texto.length() > 8 ? 128 : texto.length() > 4 ? 32 : 16; }
    /* O curto (16 ou 32 bits) na base do Bluetooth: 0000xxxx-0000-1000-8000-00805f9b34fb. */
    BLEUUID to128() const
    {
        if (bitSize() == 128) return *this;
        String t = texto;
        while (t.length() < 8) t = "0" + t;
        return BLEUUID(t + "-0000-1000-8000-00805f9b34fb");
    }
    const cs_uuid_nativo *getNative() const
    {
        memset(&nativo, 0, sizeof(nativo));
        nativo.len = (uint16_t)(bitSize() / 8);
        unsigned long curto = strtoul(texto.c_str(), nullptr, 16);
        nativo.uuid.uuid16 = nativo.u16.value = (uint16_t)curto;
        if (bitSize() == 32) nativo.uuid.uuid32 = nativo.u32.value = (uint32_t)curto;
        if (bitSize() == 128) {
            uint8_t b[16];
            if (bytes128(b))
                for (int i = 0; i < 16; i++) nativo.uuid.uuid128[i] = nativo.u128.value[i] = b[15 - i];
        }
        return &nativo;
    }
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
    BLEAddress(const uint8_t *b, uint8_t tipoDoEndereco = 0) : tipo(tipoDoEndereco)
    {
        char t[18];
        snprintf(t, sizeof(t), "%02x:%02x:%02x:%02x:%02x:%02x", b[0], b[1], b[2], b[3], b[4], b[5]);
        texto = t;
    }
    String toString() const { return texto; }
    uint8_t getType() const { return tipo; }
    void setType(uint8_t t) { tipo = t; }
    uint8_t *getNative()
    {
        unsigned v[6] = {0};
        sscanf(texto.c_str(), "%x:%x:%x:%x:%x:%x", &v[0], &v[1], &v[2], &v[3], &v[4], &v[5]);
        for (int i = 0; i < 6; i++) nativo[i] = (uint8_t)v[i];
        return nativo;
    }
    bool equals(const BLEAddress &o) const { return texto.equalsIgnoreCase(o.texto); }
    bool operator==(const BLEAddress &o) const { return equals(o); }

private:
    uint8_t tipo = 0;
    uint8_t nativo[6] = {0};
};
inline cs_val cs_de(const BLEAddress &a)
{
    static thread_local String t[8];
    static thread_local int k;
    String &s = t[k++ & 7];
    s = a.toString();
    return cs_tn(s.c_str(), (int)s.length());
}

#if defined(ESP32) && !defined(CONFIG_IDF_TARGET_ESP32S2)
#include "esp_gatts_api.h"
#include "esp_gap_ble_api.h"
#else
typedef union {
    struct {
        uint16_t conn_id;
        uint8_t remote_bda[6];
    } connect, disconnect;
} esp_ble_gatts_cb_param_t;
#endif
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
#if defined(ESP32) && !defined(CONFIG_IDF_TARGET_ESP32S2)
    virtual void onConnParamsUpdate(esp_bd_addr_t, uint16_t, uint16_t, uint16_t, esp_bt_status_t) {}
#endif
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
    BLECharacteristic *getCharacteristic() const { return m_dona; }
    uint16_t getHandle() const;
    String toString() const;

private:
    friend class BLECharacteristic;
    BLECharacteristic *m_dona = nullptr;
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
    void addDescriptor(BLEDescriptor *d)
    {
        d->m_dona = this;
        cs_obj_n(cs_nome, "addDescriptor", d);
    }
    uint32_t getProperties() const { return (uint32_t)cs_obj_i(cs_nome, "getProperties"); }
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

inline uint16_t BLEDescriptor::getHandle() const { return m_dona ? (uint16_t)(m_dona->getHandle() + 1) : 0xFFFF; }
inline String BLEDescriptor::toString() const
{
    return "UUID: " + const_cast<BLEDescriptor *>(this)->getUUID().toString() + ", handle: 0x" + String(getHandle(), HEX);
}

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
    BLEServer *getServer() { return m_servidor; }
    void executeCreate(BLEServer *servidor) { m_servidor = servidor; }
    void executeDelete() {}
    void removeCharacteristic(BLECharacteristic *c, bool = false) { cs_obj_n(cs_nome, "removeCharacteristic", c); }
    void dump() { Serial.printf("Service: uuid:%s, handle: 0x%.2x\n", getUUID().toString().c_str(), getHandle()); }

private:
    friend class BLEServer;
    BLEServer *m_servidor = nullptr;
};

class BLEAdvertisementData : public CsObjeto {
public:
    BLEAdvertisementData() { cs_criar("BLEAdvertisementData"); }
    void setName(const String &name) { cs_obj_n(cs_nome, "setName", name); }
    void setShortName(const String &name) { cs_obj_n(cs_nome, "setShortName", name); }
    void setFlags(uint8_t flags) { cs_obj_n(cs_nome, "setFlags", flags); }
    void setAppearance(uint16_t a) { cs_obj_n(cs_nome, "setAppearance", a); }
    /* Os bytes vao em hexadecimal (o simulador leva o anuncio para quem varre). */
    void setManufacturerData(const String &data) { cs_obj_n(cs_nome, "__fabricante_hex", cs_ble_hex(data)); }
    void setServiceData(BLEUUID uuid, const String &data) { cs_obj_n(cs_nome, "__servico_hex", uuid, cs_ble_hex(data)); }
    void addTxPower() {}
    void setPreferredParams(uint16_t, uint16_t) {}
    String getPayload() { return cs_ble_de_hex(cs_obj_t(cs_nome, "__carga_hex")); }
    void setCompleteServices(BLEUUID uuid) { cs_obj_n(cs_nome, "setCompleteServices", uuid); }
    void setPartialServices(BLEUUID uuid) { cs_obj_n(cs_nome, "setPartialServices", uuid); }
    /* As estruturas cruas do anuncio (tamanho, tipo, dados), como o Eddystone monta: o
     * fabricante (0xFF), o dado de servico de 16 bits (0x16) e o nome (0x08/0x09). */
    void addData(const String &data)
    {
        const uint8_t *p = (const uint8_t *)data.c_str();
        size_t n = data.length(), i = 0;
        while (i + 1 < n && p[i] > 0 && i + 1 + p[i] <= n) {
            uint8_t tam = p[i], tipo = p[i + 1];
            String corpo((const char *)p + i + 2, (unsigned)(tam - 1));
            if (tipo == 0xFF) setManufacturerData(corpo);
            else if (tipo == 0x16 && tam >= 3) setServiceData(BLEUUID((uint16_t)(p[i + 2] | p[i + 3] << 8)), corpo.substring(2));
            else if (tipo == 0x08 || tipo == 0x09) setName(corpo);
            i += 1 + tam;
        }
    }
};

class BLEAdvertising : public CsObjeto {
public:
    explicit BLEAdvertising(const String &nome) { cs_adotar(nome); }
    void addServiceUUID(const char *uuid) { cs_obj_n(cs_nome, "addServiceUUID", uuid); }
    void addServiceUUID(BLEUUID uuid) { cs_obj_n(cs_nome, "addServiceUUID", uuid); }
    void removeServiceUUID(BLEUUID uuid) { cs_obj_n(cs_nome, "removeServiceUUID", uuid); }
    bool start(uint32_t duracao = 0, void (*aoAcabar)(BLEAdvertising *) = nullptr)
    {
        (void)duracao;
        (void)aoAcabar;
        cs_obj_n(cs_nome, "start");
        return true;
    }
    bool stop() { cs_obj_n(cs_nome, "stop"); return true; }
    bool isAdvertising() { return cs_obj_i(cs_nome, "isAdvertising") != 0; }
    void reset() { cs_obj_n(cs_nome, "stop"); }
    void setName(String nome) { cs_obj_n(cs_nome, "setName", nome); }
    void addTxPower() {}
    void setPrivateAddress(uint8_t = 1) {}
    void setScanFilter(bool, bool) {}
    void setAdvertisementChannelMap(uint8_t) {}
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
    BLEService *createService(const char *uuid)
    {
        BLEService *s = cs_envolver<BLEService>(cs_obj_t(cs_nome, "createService", uuid));
        if (s) s->m_servidor = this;
        return s;
    }
    BLEService *createService(BLEUUID uuid, uint32_t numHandles = 15, uint8_t inst_id = 0)
    {
        (void)inst_id;
        BLEService *s = cs_envolver<BLEService>(cs_obj_t(cs_nome, "createService", uuid, numHandles));
        if (s) s->m_servidor = this;
        return s;
    }
    uint16_t getConnId() { return getConnectedCount() ? 0 : 0xFFFF; }
    uint16_t getHandle() { return 0; }
    bool connect(BLEAddress) { return false; }
    BLEServer *getServerByConnId(uint16_t) { return this; }
    void updatePeerMTU(uint16_t, uint16_t) {}
    bool removePeerDevice(uint16_t, bool) { return false; }
    void restoreCCCDValues(const BLEAddress &) {}
    bool requestConnParams(uint8_t *, uint16_t, uint16_t, uint16_t, uint16_t) { return getConnectedCount() != 0; }
    BLEService *getServiceByUUID(const char *uuid) { return cs_envolver<BLEService>(cs_obj_t(cs_nome, "getServiceByUUID", uuid)); }
    BLEAdvertising *getAdvertising() { return cs_envolver<BLEAdvertising>(cs_obj_t(cs_nome, "getAdvertising")); }
    void setCallbacks(BLEServerCallbacks *cb);
    void startAdvertising() { cs_obj_n(cs_nome, "startAdvertising"); }
    void stopAdvertising() { cs_obj_n(cs_nome, "stopAdvertising"); }
    /* O servidor GATT do NimBLE comeca sozinho no simulador (os servicos ja ficam visiveis no start deles). */
    void start() {}
    bool isStarted() { return true; }
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

/* O radio simulado nao cifra nem faz pareamento: a configuracao fica guardada e nada a pede. */
#ifndef BLE_SM_DEFAULT_PASSKEY
#define BLE_SM_DEFAULT_PASSKEY 123456
#endif

class BLESecurityCallbacks {
public:
    virtual ~BLESecurityCallbacks() = default;
    virtual uint32_t onPassKeyRequest() { return BLE_SM_DEFAULT_PASSKEY; }
    virtual void onPassKeyNotify(uint32_t) {}
    virtual bool onSecurityRequest() { return true; }
    virtual bool onConfirmPIN(uint32_t) { return true; }
    virtual bool onAuthorizationRequest(uint16_t, uint16_t, bool) { return true; }
#if defined(ESP32) && !defined(CONFIG_IDF_TARGET_ESP32S2) && !defined(CONFIG_NIMBLE_ENABLED)
    virtual void onAuthenticationComplete(esp_ble_auth_cmpl_t) {}
#endif
};

class BLESecurity {
public:
    BLESecurity() {}
    virtual ~BLESecurity() = default;
    static void setAuthenticationMode(uint8_t) {}
    static void setAuthenticationMode(bool, bool, bool) {}
    static void setCapability(uint8_t) {}
    static void setInitEncryptionKey(uint8_t = 0) {}
    static void setRespEncryptionKey(uint8_t = 0) {}
    static void setKeySize(uint8_t = 16) {}
    static void setStaticPIN(uint32_t pin) { chave() = pin; }
    static uint32_t setPassKey(bool = false, uint32_t passkey = BLE_SM_DEFAULT_PASSKEY) { return chave() = passkey; }
    static uint32_t getPassKey() { return chave(); }
    static uint32_t generateRandomPassKey() { return (uint32_t)random(0, 1000000); }
    static void regenPassKeyOnConnect(bool = false) {}
    static void resetSecurity() { chave() = BLE_SM_DEFAULT_PASSKEY; forcar() = false; }
    static void setForceAuthentication(bool v) { forcar() = v; }
    static bool getForceAuthentication() { return forcar(); }
    static void waitForAuthenticationComplete(uint32_t = 10000) {}
    static void signalAuthenticationComplete() {}
#if defined(ESP32) && !defined(CONFIG_IDF_TARGET_ESP32S2) && !defined(CONFIG_NIMBLE_ENABLED)
    static char *esp_key_type_to_str(esp_ble_key_type_t) { return (char *)"ESP_LE_KEY_NONE"; }
    static void setEncryptionLevel(esp_ble_sec_act_t) {}
    static bool startSecurity(esp_bd_addr_t, int *rc = nullptr)
    {
        if (rc) *rc = 0;
        return true;
    }
#else
    static bool startSecurity(uint16_t, int *rc = nullptr)
    {
        if (rc) *rc = 0;
        return true;
    }
#endif

private:
    static uint32_t &chave()
    {
        static uint32_t k = BLE_SM_DEFAULT_PASSKEY;
        return k;
    }
    static bool &forcar()
    {
        static bool f = false;
        return f;
    }
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
    class BLERemoteDescriptor *getDescriptor(BLEUUID uuid);
    std::map<std::string, class BLERemoteDescriptor *> *getDescriptors();
    class BLERemoteService *getRemoteService();
    bool canBroadcast() { return false; }
    void setAuth(uint8_t) {}
    uint8_t *readRawData()
    {
        static String guardado;
        guardado = readValue();
        return (uint8_t *)guardado.c_str();
    }
    bool subscribe(bool notificacoes = true, notify_callback cb = nullptr, bool = true)
    {
        registerForNotify(cb, notificacoes);
        return canNotify() || canIndicate();
    }
    bool unsubscribe(bool = true)
    {
        registerForNotify(nullptr);
        return true;
    }

private:
    class BLERemoteDescriptor *cccd = nullptr;
    std::map<std::string, class BLERemoteDescriptor *> descritores;
};

/* O descritor do lado do cliente. O simulador nao leva os descritores do
 * servidor pelo radio: o que existe e o 2902 (o CCCD), que toda
 * caracteristica com notify tem -- escrever 01 00 nele liga as notificacoes,
 * como no celular; o valor fica guardado aqui. */
class BLERemoteDescriptor {
public:
    BLERemoteDescriptor(BLERemoteCharacteristic *dona, BLEUUID uuid) : m_dona(dona), m_uuid(uuid) {}
    uint16_t getHandle() { return (uint16_t)(m_dona->getHandle() + 1); }
    BLERemoteCharacteristic *getRemoteCharacteristic() { return m_dona; }
    BLEUUID getUUID() { return m_uuid; }
    String readValue(void) { return m_valor; }
    uint8_t readUInt8(void) { return m_valor.length() >= 1 ? (uint8_t)m_valor[0] : 0; }
    uint16_t readUInt16(void) { return m_valor.length() >= 2 ? (uint16_t)((uint8_t)m_valor[0] | (uint8_t)m_valor[1] << 8) : 0; }
    uint32_t readUInt32(void)
    {
        uint32_t v = 0;
        for (unsigned i = 0; i < 4 && i < m_valor.length(); i++) v |= (uint32_t)(uint8_t)m_valor[i] << (8 * i);
        return v;
    }
    String toString(void) { return "Descriptor: uuid: " + m_uuid.toString() + ", handle: " + String(getHandle()); }
    bool writeValue(uint8_t *data, size_t length, bool response = false)
    {
        (void)response;
        m_valor = String((const char *)data, (unsigned)length);
        return true;
    }
    bool writeValue(String newValue, bool response = false) { return writeValue((uint8_t *)newValue.c_str(), newValue.length(), response); }
    bool writeValue(uint8_t newValue, bool response = false) { return writeValue(&newValue, 1, response); }
    void setAuth(uint8_t auth) { (void)auth; }

private:
    BLERemoteCharacteristic *m_dona;
    BLEUUID m_uuid;
    String m_valor = String("\0\0", 2);
};

inline BLERemoteDescriptor *BLERemoteCharacteristic::getDescriptor(BLEUUID uuid)
{
    if (!uuid.equals(BLEUUID((uint16_t)0x2902)) || !(canNotify() || canIndicate())) return nullptr;
    if (!cccd) cccd = new BLERemoteDescriptor(this, uuid);
    return cccd;
}

inline std::map<std::string, BLERemoteDescriptor *> *BLERemoteCharacteristic::getDescriptors()
{
    BLEUUID u((uint16_t)0x2902);
    if (BLERemoteDescriptor *d = getDescriptor(u)) descritores[u.to128().toString().c_str()] = d;
    return &descritores;
}

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
    uint16_t getHandle() { return (uint16_t)cs_obj_i(cs_nome, "getHandle"); }
    String toString() { return cs_obj_t(cs_nome, "toString"); }
    BLEClient *getClient();
    void retrieveCharacteristics() { getCharacteristics(); }
    /* O que o servidor tem, em ordem: a descoberta volta do simulador como lista de UUIDs. */
    std::map<std::string, BLERemoteCharacteristic *> *getCharacteristics()
    {
        String lista = cs_obj_t(cs_nome, "__caracteristicas");
        while (lista.length()) {
            int fim = lista.indexOf(',');
            String u = fim < 0 ? lista : lista.substring(0, fim);
            lista = fim < 0 ? String() : lista.substring(fim + 1);
            if (BLERemoteCharacteristic *c = getCharacteristic(BLEUUID(u))) {
                porUuid[u.c_str()] = c;
                porHandle[c->getHandle()] = c;
            }
        }
        return &porUuid;
    }
    std::map<uint16_t, BLERemoteCharacteristic *> *getCharacteristicsByHandle()
    {
        getCharacteristics();
        return &porHandle;
    }

private:
    std::map<std::string, BLERemoteCharacteristic *> porUuid;
    std::map<uint16_t, BLERemoteCharacteristic *> porHandle;
};

inline BLERemoteService *BLERemoteCharacteristic::getRemoteService() { return cs_envolver<BLERemoteService>(cs_obj_t(cs_nome, "getRemoteService")); }

typedef enum { BLE_UNKNOWN_FRAME, BLE_EDDYSTONE_UUID_FRAME, BLE_EDDYSTONE_URL_FRAME, BLE_EDDYSTONE_TLM_FRAME, BLE_FRAME_MAX } ble_frame_type_t;

class BLEAdvertisedDevice : public CsObjeto {
public:
    BLEAdvertisedDevice() {}
    explicit BLEAdvertisedDevice(const String &nome) { cs_adotar(nome); }
    String getName() { return cs_obj_t(cs_nome, "getName"); }
    BLEAddress getAddress() { return BLEAddress(cs_ble_texto(cs_obj_t(cs_nome, "getAddress"))); }
    int getRSSI() { return (int)cs_obj_i(cs_nome, "getRSSI"); }
    int8_t getTXPower() { return (int8_t)cs_obj_i(cs_nome, "getTXPower"); }
    uint16_t getAppearance() { return (uint16_t)cs_obj_i(cs_nome, "getAppearance"); }
    String getManufacturerData() { return cs_ble_de_hex(cs_obj_t(cs_nome, "__fabricante_hex")); }
    String getServiceData() { return cs_ble_de_hex(cs_obj_t(cs_nome, "__servico_hex")); }
    BLEUUID getServiceDataUUID() { return BLEUUID(cs_obj_t(cs_nome, "__servico_uuid")); }
    int getServiceDataCount() { return haveServiceData() ? 1 : 0; }
    int getServiceDataUUIDCount() { return getServiceDataCount(); }
    uint8_t getAddressType() { return tipoDeEndereco; }
    void setAddressType(uint8_t t) { tipoDeEndereco = t; }
    uint8_t getAdvType() { return tipoDeAnuncio; }
    void setAdvType(uint8_t t) { tipoDeAnuncio = t; }
    bool isLegacyAdvertisement() { return true; }
    bool isScannable() { return isConnectable(); }
    BLEScan *getScan();
    /* O Eddystone: o servico 0xFEAA, e o primeiro byte do dado diz o quadro. */
    ble_frame_type_t getFrameType()
    {
        if (!haveServiceData() || !getServiceDataUUID().equals(BLEUUID((uint16_t)0xFEAA))) return BLE_UNKNOWN_FRAME;
        String d = getServiceData();
        if (!d.length()) return BLE_UNKNOWN_FRAME;
        uint8_t tipo = (uint8_t)d[0];
        return tipo == 0x00 ? BLE_EDDYSTONE_UUID_FRAME : tipo == 0x10 ? BLE_EDDYSTONE_URL_FRAME : tipo == 0x20 ? BLE_EDDYSTONE_TLM_FRAME : BLE_UNKNOWN_FRAME;
    }
    /* O anuncio como chegou no ar (as estruturas tamanho/tipo/dados): os flags,
     * o nome, os servicos, o dado de servico e o do fabricante. */
    uint8_t *getPayload()
    {
        carga = "";
        auto ad = [&](uint8_t tipo, const String &dados) {
            carga += (char)(dados.length() + 1);
            carga += (char)tipo;
            carga += dados;
        };
        ad(0x01, String((char)0x06));
        if (haveName()) ad(0x09, getName());
        for (int i = 0; i < getServiceUUIDCount(); i++) {
            BLEUUID u = BLEUUID(cs_ble_texto(cs_obj_t(cs_nome, "getServiceUUID", i)));
            const cs_uuid_nativo *n = u.getNative();
            String b;
            if (u.bitSize() == 16) {
                b += (char)(n->uuid.uuid16 & 0xFF);
                b += (char)(n->uuid.uuid16 >> 8);
                ad(0x03, b);
            } else {
                for (int k = 0; k < 16; k++) b += (char)n->uuid.uuid128[k];
                ad(0x07, b);
            }
        }
        if (haveServiceData()) {
            const cs_uuid_nativo *n = getServiceDataUUID().getNative();
            String b;
            b += (char)(n->uuid.uuid16 & 0xFF);
            b += (char)(n->uuid.uuid16 >> 8);
            b += getServiceData();
            ad(0x16, b);
        }
        if (haveManufacturerData()) ad(0xFF, getManufacturerData());
        return (uint8_t *)carga.c_str();
    }
    size_t getPayloadLength()
    {
        if (!carga.length()) getPayload();
        return carga.length();
    }

private:
    String carga;
    uint8_t tipoDeEndereco = 0, tipoDeAnuncio = 0;

public:
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
    void dump()
    {
        Serial.printf("Number of devices: %d\n", getCount());
        for (int i = 0; i < getCount(); i++) Serial.printf("- %s\n", getDevice(i).toString().c_str());
    }
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
    /* O start daqui espera a varredura acabar: o aviso vem logo depois dela. */
    bool start(uint32_t duration, void (*scanCompleteCB)(BLEScanResults), bool is_continue = false)
    {
        BLEScanResults *r = start(duration, is_continue);
        if (scanCompleteCB) scanCompleteCB(*r);
        return true;
    }
    void stop() { cs_obj_n(cs_nome, "stop"); }
    void clearDuplicateCache() {}
    void erase(BLEAddress) {}
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
    virtual bool onConnParamsUpdateRequest(BLEClient *, const struct ble_gap_upd_params *) { return true; }
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
    bool connectTimeout(BLEAdvertisedDevice *device, uint32_t = 0xFFFFFFFF) { return connect(device); }
    uint16_t getConnId() { return (uint16_t)cs_obj_i(cs_nome, "getConnId"); }
    uint16_t getGattcIf() { return 0; }
    String getValue(BLEUUID servico, BLEUUID caracteristica) { return cs_obj_t(cs_nome, "getValue", servico, caracteristica); }
    void setValue(BLEUUID servico, BLEUUID caracteristica, String valor) { cs_obj_n(cs_nome, "setValue", servico, caracteristica, valor); }
    bool secureConnection() { return cs_obj_i(cs_nome, "secureConnection") != 0; }
    bool updateConnParams(uint16_t, uint16_t, uint16_t, uint16_t) { return cs_obj_i(cs_nome, "updateConnParams") != 0; }
    String toString() { return cs_obj_t(cs_nome, "toString"); }
    /* Os servicos do servidor: a descoberta volta do simulador como lista de UUIDs. */
    std::map<std::string, BLERemoteService *> *getServices()
    {
        String lista = cs_obj_t(cs_nome, "__servicos");
        while (lista.length()) {
            int fim = lista.indexOf(',');
            String u = fim < 0 ? lista : lista.substring(0, fim);
            lista = fim < 0 ? String() : lista.substring(fim + 1);
            if (BLERemoteService *s = getService(BLEUUID(u))) servicos[u.c_str()] = s;
        }
        return &servicos;
    }

private:
    std::map<std::string, BLERemoteService *> servicos;
};

inline BLEClient *BLERemoteService::getClient() { return cs_envolver<BLEClient>(cs_obj_t(cs_nome, "getClient")); }

enum class BLEStack { BLUEDROID, NIMBLE, UNKNOWN };

/* O que o core guarda de cada conexao (BLEDevice::getPeerDevices). */
typedef struct {
    void *peer_device;
    bool connected;
    uint16_t mtu;
} conn_status_t;

class BLEDeviceCallbacks {
public:
    virtual ~BLEDeviceCallbacks() {}
    virtual int onStoreStatus(struct ble_store_status_event *, void *) { return 0; }
};

/* ---- o BLEDevice (tudo static) ---- */
class BLEDevice {
    static String &nomeDoAparelho()
    {
        static String n;
        return n;
    }
    static bool &ligado()
    {
        static bool l = false;
        return l;
    }
    static BLEServer *&servidor()
    {
        static BLEServer *s = nullptr;
        return s;
    }
    static std::vector<BLEClient *> &clientes()
    {
        static std::vector<BLEClient *> c;
        return c;
    }
    static std::vector<String> &permitidos()
    {
        static std::vector<String> p;
        return p;
    }
    static void anotar(const String &nome)
    {
        nomeDoAparelho() = nome;
        ligado() = true;
    }

public:
    static void init(const String &deviceName)
    {
        cs_obj_n(nullptr, "BLEDevice::init", deviceName);
        anotar(deviceName);
    }
    static void init(const char *deviceName)
    {
        cs_obj_n(nullptr, "BLEDevice::init", deviceName);
        anotar(deviceName);
    }
    static void deinit(bool release_memory = false)
    {
        cs_obj_n(nullptr, "BLEDevice::deinit", release_memory);
        ligado() = false;
        servidor() = nullptr;
        clientes().clear();
    }
    static bool getInitialized() { return ligado(); }
    static String getDeviceName() { return nomeDoAparelho(); }
    static BLEServer *createServer() { return servidor() = cs_envolver<BLEServer>(cs_obj_t(nullptr, "BLEDevice::createServer")); }
    static BLEServer *getServer() { return servidor(); }
    static BLEClient *createClient()
    {
        BLEClient *c = cs_envolver<BLEClient>(cs_obj_t(nullptr, "BLEDevice::createClient"));
        if (c) clientes().push_back(c);
        return c;
    }
    static BLEClient *getClientByID(uint16_t id)
    {
        for (BLEClient *c : clientes())
            if (c->isConnected() && c->getConnId() == id) return c;
        return nullptr;
    }
    static BLEClient *getClientByGattIf(uint16_t id) { return getClientByID(id); }
    static BLEClient *getClientByAddress(BLEAddress endereco)
    {
        for (BLEClient *c : clientes())
            if (c->isConnected() && c->getPeerAddress().equals(endereco)) return c;
        return nullptr;
    }
    /* Liga, le ou escreve, e desliga, como a biblioteca. */
    static String getValue(BLEAddress endereco, BLEUUID servico, BLEUUID caracteristica)
    {
        BLEClient *c = createClient();
        if (!c || !c->connect(endereco)) return String();
        String valor = c->getValue(servico, caracteristica);
        c->disconnect();
        return valor;
    }
    static void setValue(BLEAddress endereco, BLEUUID servico, BLEUUID caracteristica, String valor)
    {
        BLEClient *c = createClient();
        if (!c || !c->connect(endereco)) return;
        c->setValue(servico, caracteristica, valor);
        c->disconnect();
    }
    static std::map<uint16_t, conn_status_t> getPeerDevices(bool)
    {
        std::map<uint16_t, conn_status_t> mapa;
        for (BLEClient *c : clientes())
            if (c->isConnected()) mapa[c->getConnId()] = {c, true, c->getMTU()};
        return mapa;
    }
    static void addPeerDevice(void *, bool, uint16_t) {}
    static void removePeerDevice(uint16_t, bool) {}
    static void whiteListAdd(BLEAddress endereco) { permitidos().push_back(endereco.toString()); }
    static void whiteListRemove(BLEAddress endereco)
    {
        auto &p = permitidos();
        p.erase(std::remove(p.begin(), p.end(), endereco.toString()), p.end());
    }
    static bool onWhiteList(BLEAddress &endereco)
    {
        auto &p = permitidos();
        return std::find(p.begin(), p.end(), endereco.toString()) != p.end();
    }
    static BLEStack getBLEStack()
    {
#ifdef CONFIG_NIMBLE_ENABLED
        return BLEStack::NIMBLE;
#else
        return BLEStack::BLUEDROID;
#endif
    }
    static String getBLEStackString() { return getBLEStack() == BLEStack::NIMBLE ? "NimBLE" : "Bluedroid"; }
    static bool isHostedBLE() { return false; }
    static bool setPins(int8_t, int8_t, int8_t, int8_t, int8_t, int8_t, int8_t) { return true; }
    static void setDeviceCallbacks(BLEDeviceCallbacks *) {}
    static bool setOwnAddrType(uint8_t) { return true; }
    static bool setOwnAddr(BLEAddress &) { return true; }
    static bool setOwnAddr(uint8_t *) { return true; }
    /* O radio simulado nao faz pareamento: nao ha chave de identidade (IRK) de ninguem. */
    static bool getLocalIRK(uint8_t *) { return false; }
    static String getLocalIRKString() { return String(); }
    static String getLocalIRKBase64() { return String(); }
    static String getLocalIRKReverse() { return String(); }
    static bool getPeerIRK(BLEAddress, uint8_t *) { return false; }
    static String getPeerIRKString(BLEAddress) { return String(); }
    static String getPeerIRKBase64(BLEAddress) { return String(); }
    static String getPeerIRKReverse(BLEAddress) { return String(); }
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

inline BLEScan *BLEAdvertisedDevice::getScan() { return BLEDevice::getScan(); }

#if defined(ESP32)
#include "BLEUtils.h"
#endif

/* A potencia (esp_power_level_t) e as permissoes (ESP_GATT_PERM_*): as do IDF. */
#if defined(ESP32) && !CONFIG_IDF_TARGET_ESP32S2
#include "esp_bt.h"
#else
#define ESP_PWR_LVL_N12 0
#define ESP_PWR_LVL_N9 1
#define ESP_PWR_LVL_N6 2
#define ESP_PWR_LVL_N3 3
#define ESP_PWR_LVL_N0 4
#define ESP_PWR_LVL_P3 5
#define ESP_PWR_LVL_P6 6
#define ESP_PWR_LVL_P9 7
#endif
#if __has_include("esp_gatt_defs.h")
#include "esp_gatt_defs.h"
#else
#define ESP_GATT_PERM_READ (1 << 0)
#define ESP_GATT_PERM_WRITE (1 << 4)
#endif

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
