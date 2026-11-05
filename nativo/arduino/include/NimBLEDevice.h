/* NimBLEDevice.h -- a NimBLE-Arduino 2.x (h2zero), por cima do BLE do simulador (cs_ble.h).
 *
 * A biblioteca de verdade traz a pilha NimBLE inteira, que precisa do controlador de radio do chip:
 * aqui o radio e o da bancada, o mesmo do BLEDevice.h. As classes tem os nomes e as assinaturas da
 * NimBLE (o std::string, o NimBLEConnInfo&, o NIMBLE_PROPERTY::READ) e chamam as do core. O que o
 * simulador nao tem -- o pareamento, o L2CAP, o anuncio estendido -- responde como sem par conectado. */
#ifndef CS_NIMBLE_DEVICE_H
#define CS_NIMBLE_DEVICE_H

#include "cs_ble.h"
#include <algorithm>
#include <functional>
#include <map>
#include <string>
#include <vector>
#include "freertos/FreeRTOS.h"
#include "freertos/task.h"

#define NIMBLE_CPP_VERSION_STR "NimBLE-CPP 2.5.2"
#define BLE_HS_CONN_HANDLE_NONE 0xFFFF
#define BLE_ERR_REM_USER_CONN_TERM 0x13
#define BLE_ERR_CONN_TERM_LOCAL 0x16
#define BLE_ATT_ATTR_MAX_LEN 512
#define CONFIG_BT_NIMBLE_MAX_CONNECTIONS 3
#define BLE_MAX_CONNECTIONS CONFIG_BT_NIMBLE_MAX_CONNECTIONS
#define NIMBLE_MAX_CONNECTIONS CONFIG_BT_NIMBLE_MAX_CONNECTIONS
#define BLE_HS_IO_DISPLAY_ONLY 0x00
#define BLE_HS_IO_DISPLAY_YESNO 0x01
#define BLE_HS_IO_KEYBOARD_ONLY 0x02
#define BLE_HS_IO_NO_INPUT_OUTPUT 0x03
#define BLE_HS_IO_KEYBOARD_DISPLAY 0x04
#define BLE_SM_PAIR_AUTHREQ_BOND 0x01
#define BLE_SM_PAIR_AUTHREQ_MITM 0x04
#define BLE_SM_PAIR_AUTHREQ_SC 0x08
#define BLE_SM_PAIR_AUTHREQ_KEYPRESS 0x10
#define BLE_SM_PAIR_KEY_DIST_ENC 0x01
#define BLE_SM_PAIR_KEY_DIST_ID 0x02
#define BLE_HCI_SCAN_FILT_NO_WL 0
#define BLE_HCI_SCAN_FILT_USE_WL 1
#define BLE_HCI_SCAN_FILT_NO_WL_INITA 2
#define BLE_HCI_SCAN_FILT_USE_WL_INITA 3
#define BLE_GAP_CONN_MODE_NON 0
#define BLE_GAP_CONN_MODE_DIR 1
#define BLE_GAP_CONN_MODE_UND 2
#define BLE_GAP_DISC_MODE_NON 0
#define BLE_GAP_DISC_MODE_LTD 1
#define BLE_GAP_DISC_MODE_GEN 2
#define BLE_ADDR_PUBLIC 0
#define BLE_ADDR_RANDOM 1
#define BLE_ADDR_PUBLIC_ID 2
#define BLE_ADDR_RANDOM_ID 3
#define BLE_HS_ADV_F_DISC_LTD 0x01
#define BLE_HS_ADV_F_DISC_GEN 0x02
#define BLE_HS_ADV_F_BREDR_UNSUP 0x04
#define BLE_GAP_PHY_1M_MASK 0x01
#define BLE_GAP_PHY_2M_MASK 0x02
#define BLE_GAP_PHY_CODED_MASK 0x04
#define BLE_HS_FOREVER INT32_MAX

struct ble_gap_upd_params {
    uint16_t itvl_min, itvl_max, latency, supervision_timeout, min_ce_len, max_ce_len;
};

namespace NIMBLE_PROPERTY {
constexpr uint16_t BROADCAST = 0x0001;
constexpr uint16_t READ = 0x0002;
constexpr uint16_t WRITE_NR = 0x0004;
constexpr uint16_t WRITE = 0x0008;
constexpr uint16_t NOTIFY = 0x0010;
constexpr uint16_t INDICATE = 0x0020;
constexpr uint16_t READ_ENC = 0x0200;
constexpr uint16_t READ_AUTHEN = 0x0400;
constexpr uint16_t READ_AUTHOR = 0x0800;
constexpr uint16_t WRITE_ENC = 0x1000;
constexpr uint16_t WRITE_AUTHEN = 0x2000;
constexpr uint16_t WRITE_AUTHOR = 0x4000;
} // namespace NIMBLE_PROPERTY

enum class NimBLETxPowerType { All = 0, Advertise = 1, Scan = 2, Connection = 3 };

/* As propriedades da NimBLE nas do BLE do core (que o simulador le no formato da placa). */
inline uint32_t cs_nimble_propriedades(uint16_t p)
{
#if defined(CONFIG_NIMBLE_ENABLED)
    return p & 0x3F;
#else
    uint32_t r = 0;
    if (p & NIMBLE_PROPERTY::READ) r |= BLECharacteristic::PROPERTY_READ;
    if (p & NIMBLE_PROPERTY::WRITE) r |= BLECharacteristic::PROPERTY_WRITE;
    if (p & NIMBLE_PROPERTY::WRITE_NR) r |= BLECharacteristic::PROPERTY_WRITE_NR;
    if (p & NIMBLE_PROPERTY::NOTIFY) r |= BLECharacteristic::PROPERTY_NOTIFY;
    if (p & NIMBLE_PROPERTY::INDICATE) r |= BLECharacteristic::PROPERTY_INDICATE;
    if (p & NIMBLE_PROPERTY::BROADCAST) r |= BLECharacteristic::PROPERTY_BROADCAST;
    return r;
#endif
}

/* Um objeto da NimBLE para cada um do core: o mesmo ponteiro volta de lugares diferentes. */
template <class W, class B> W *cs_nimble_de(B *b)
{
    if (!b) return nullptr;
    static std::map<B *, W *> vistos;
    W *&w = vistos[b];
    if (!w) w = new W(b);
    return w;
}

inline std::string cs_std(const String &s) { return std::string(s.c_str(), s.length()); }

// ---------------------------------------------------------------------------- UUID e endereco
class NimBLEUUID : public BLEUUID {
public:
    using BLEUUID::BLEUUID;
    NimBLEUUID() {}
    NimBLEUUID(const BLEUUID &u) : BLEUUID(u) {}
    NimBLEUUID(const std::string &s) : BLEUUID(String(s.c_str())) {}
    std::string toString() const { return cs_std(BLEUUID::toString()); }
    operator std::string() const { return toString(); }
    NimBLEUUID to128() const { return NimBLEUUID(BLEUUID::to128()); }
    NimBLEUUID to16() const { return NimBLEUUID(BLEUUID::to16()); }
    bool operator==(const NimBLEUUID &o) const { return equals(o); }
    bool operator!=(const NimBLEUUID &o) const { return !equals(o); }
    /* Os bytes como a NimBLE guarda: o menos significativo primeiro. */
    const uint8_t *getValue() const
    {
        const cs_uuid_nativo *n = getNative();
        static thread_local uint8_t b[16];
        memcpy(b, n->uuid.uuid128, 16);
        if (bitSize() == 16) memcpy(b, &n->uuid.uuid16, 2);
        if (bitSize() == 32) memcpy(b, &n->uuid.uuid32, 4);
        return b;
    }
    NimBLEUUID &reverseByteOrder() { return *this; }
};

class NimBLEAddress : public BLEAddress {
public:
    NimBLEAddress() {}
    NimBLEAddress(const BLEAddress &a) : BLEAddress(a) {}
    NimBLEAddress(const std::string &s, uint8_t tipo = BLE_ADDR_PUBLIC) : BLEAddress(String(s.c_str())) { setType(tipo); }
    NimBLEAddress(const char *s, uint8_t tipo = BLE_ADDR_PUBLIC) : BLEAddress(s) { setType(tipo); }
    NimBLEAddress(const uint8_t *b, uint8_t tipo = BLE_ADDR_PUBLIC) : BLEAddress(b, tipo) {}
    NimBLEAddress(uint64_t v, uint8_t tipo)
    {
        uint8_t b[6];
        for (int i = 0; i < 6; i++) b[i] = (uint8_t)(v >> (8 * (5 - i)));
        *this = NimBLEAddress(b, tipo);
    }
    std::string toString() const { return cs_std(BLEAddress::toString()); }
    operator std::string() const { return toString(); }
    operator uint64_t() const
    {
        uint64_t v = 0;
        const uint8_t *b = const_cast<NimBLEAddress *>(this)->getNative();
        for (int i = 0; i < 6; i++) v = v << 8 | b[i];
        return v;
    }
    const uint8_t *getVal() const { return const_cast<NimBLEAddress *>(this)->getNative(); }
    const uint8_t *getBase() const { return getVal(); }
    bool isNull() const { return BLEAddress::toString().length() == 0 || (uint64_t) * this == 0; }
    bool isPublic() const { return getType() == BLE_ADDR_PUBLIC; }
    bool isStatic() const { return getType() == BLE_ADDR_RANDOM; }
    bool isRpa() const { return false; }
    bool isNrpa() const { return false; }
    bool operator==(const NimBLEAddress &o) const { return equals(o); }
    bool operator!=(const NimBLEAddress &o) const { return !equals(o); }
};

// ---------------------------------------------------------------------------- o valor de um atributo
class NimBLEAttValue {
    std::vector<uint8_t> v;
    uint16_t maximo = BLE_ATT_ATTR_MAX_LEN;

public:
    NimBLEAttValue(uint16_t init_len = 20, uint16_t max_len = BLE_ATT_ATTR_MAX_LEN) : maximo(max_len) { v.reserve(init_len + 1); }
    NimBLEAttValue(const uint8_t *value, uint16_t len, uint16_t max_len = BLE_ATT_ATTR_MAX_LEN) : maximo(max_len) { setValue(value, len); }
    NimBLEAttValue(const char *value, uint16_t max_len = BLE_ATT_ATTR_MAX_LEN) : NimBLEAttValue((const uint8_t *)value, (uint16_t)strlen(value), max_len) {}
    NimBLEAttValue(std::initializer_list<uint8_t> lista, uint16_t max_len = BLE_ATT_ATTR_MAX_LEN) : NimBLEAttValue(lista.begin(), (uint16_t)lista.size(), max_len) {}
    NimBLEAttValue(const std::string &s, uint16_t max_len = BLE_ATT_ATTR_MAX_LEN) : NimBLEAttValue((const uint8_t *)s.data(), (uint16_t)s.size(), max_len) {}
    NimBLEAttValue(const std::vector<uint8_t> &d, uint16_t max_len = BLE_ATT_ATTR_MAX_LEN) : NimBLEAttValue(d.data(), (uint16_t)d.size(), max_len) {}
    NimBLEAttValue(const String &s, uint16_t max_len = BLE_ATT_ATTR_MAX_LEN) : NimBLEAttValue((const uint8_t *)s.c_str(), (uint16_t)s.length(), max_len) {}

    uint16_t max_size() const { return maximo; }
    uint16_t capacity() const { return (uint16_t)v.capacity(); }
    uint16_t length() const { return (uint16_t)(v.empty() ? 0 : v.size() - 1); }
    uint16_t size() const { return length(); }
    const uint8_t *data() const { return v.empty() ? (const uint8_t *)"" : v.data(); }
    const char *c_str() const { return (const char *)data(); }
    const uint8_t *begin() const { return data(); }
    const uint8_t *end() const { return data() + length(); }
    time_t getTimeStamp() const { return 0; }
    void setTimeStamp() {}
    void setTimeStamp(time_t) {}
    bool setValue(const uint8_t *value, uint16_t len)
    {
        if (len > maximo) return false;
        v.assign(value, value + len);
        v.push_back(0);
        return true;
    }
    bool setValue(const char *s, uint16_t len = 0) { return setValue((const uint8_t *)s, len ? len : (uint16_t)strlen(s)); }
    bool setValue(const std::string &s) { return setValue((const uint8_t *)s.data(), (uint16_t)s.size()); }
    bool setValue(const String &s) { return setValue((const uint8_t *)s.c_str(), (uint16_t)s.length()); }
    template <class T> typename std::enable_if<std::is_arithmetic<T>::value, bool>::type setValue(const T &x)
    {
        return setValue((const uint8_t *)&x, sizeof(T));
    }
    const NimBLEAttValue &getValue(time_t *t = nullptr) const
    {
        if (t) *t = 0;
        return *this;
    }
    template <class T> T getValue(time_t *t = nullptr, bool skipSizeCheck = false) const
    {
        if (t) *t = 0;
        T x{};
        if (!skipSizeCheck && length() < sizeof(T)) return x;
        memcpy(&x, data(), std::min<size_t>(sizeof(T), length()));
        return x;
    }
    NimBLEAttValue &append(const uint8_t *value, uint16_t len)
    {
        if (!v.empty()) v.pop_back();
        v.insert(v.end(), value, value + len);
        v.push_back(0);
        return *this;
    }
    uint8_t operator[](int i) const { return i < length() ? v[i] : 0; }
    operator std::string() const { return std::string(c_str(), length()); }
    operator std::vector<uint8_t>() const { return std::vector<uint8_t>(begin(), end()); }
    operator const uint8_t *() const { return data(); }
    NimBLEAttValue &operator+=(const NimBLEAttValue &o) { return append(o.data(), o.length()); }
    bool operator==(const NimBLEAttValue &o) const { return length() == o.length() && memcmp(data(), o.data(), length()) == 0; }
    bool operator!=(const NimBLEAttValue &o) const { return !(*this == o); }
};

/* Quem esta do outro lado da conexao. O simulador nao diz o endereco do celular a cada evento: fica o do
 * primeiro par, e o pareamento (que o simulador nao faz) aparece como sem criptografia. */
class NimBLEConnInfo {
public:
    NimBLEConnInfo() {}
    explicit NimBLEConnInfo(uint16_t handle, const NimBLEAddress &a = NimBLEAddress(), uint16_t mtu = 23) : handle(handle), endereco(a), mtu(mtu) {}
    NimBLEAddress getAddress() const { return endereco; }
    NimBLEAddress getIdAddress() const { return endereco; }
    uint16_t getConnHandle() const { return handle; }
    uint16_t getConnInterval() const { return 24; }
    uint16_t getConnTimeout() const { return 400; }
    uint16_t getConnLatency() const { return 0; }
    uint16_t getMTU() const { return mtu; }
    bool isMaster() const { return false; }
    bool isSlave() const { return true; }
    bool isBonded() const { return false; }
    bool isEncrypted() const { return false; }
    bool isAuthenticated() const { return false; }
    uint8_t getSecKeySize() const { return 0; }
    std::string toString() const { return "Address: " + endereco.toString() + "\nConnection handle: " + std::to_string(handle) + "\n"; }

private:
    uint16_t handle = BLE_HS_CONN_HANDLE_NONE;
    NimBLEAddress endereco;
    uint16_t mtu = 23;
};

class NimBLEServer;
class NimBLEService;
class NimBLECharacteristic;
class NimBLEDescriptor;
class NimBLEClient;
class NimBLERemoteService;
class NimBLERemoteCharacteristic;
class NimBLEScan;
class NimBLEAdvertising;
class NimBLEAdvertisedDevice;

// ---------------------------------------------------------------------------- os callbacks
class NimBLEServerCallbacks {
public:
    virtual ~NimBLEServerCallbacks() {}
    virtual void onConnect(NimBLEServer *, NimBLEConnInfo &) {}
    virtual void onDisconnect(NimBLEServer *, NimBLEConnInfo &, int) {}
    virtual void onMTUChange(uint16_t, NimBLEConnInfo &) {}
    virtual uint32_t onPassKeyDisplay() { return 123456; }
    virtual void onPassKeyEntry(NimBLEConnInfo &) {}
    virtual void onConfirmPassKey(NimBLEConnInfo &, uint32_t) {}
    virtual void onAuthenticationComplete(NimBLEConnInfo &) {}
    virtual void onIdentity(NimBLEConnInfo &) {}
    virtual void onConnParamsUpdate(NimBLEConnInfo &) {}
    virtual void onPhyUpdate(NimBLEConnInfo &, uint8_t, uint8_t) {}
};

class NimBLECharacteristicCallbacks {
public:
    virtual ~NimBLECharacteristicCallbacks() {}
    virtual void onRead(NimBLECharacteristic *, NimBLEConnInfo &) {}
    virtual void onWrite(NimBLECharacteristic *, NimBLEConnInfo &) {}
    virtual void onStatus(NimBLECharacteristic *, int) {}
    virtual void onStatus(NimBLECharacteristic *, NimBLEConnInfo &, int) {}
    virtual void onSubscribe(NimBLECharacteristic *, NimBLEConnInfo &, uint16_t) {}
};

class NimBLEDescriptorCallbacks {
public:
    virtual ~NimBLEDescriptorCallbacks() = default;
    virtual void onRead(NimBLEDescriptor *, NimBLEConnInfo &) {}
    virtual void onWrite(NimBLEDescriptor *, NimBLEConnInfo &) {}
};

class NimBLEClientCallbacks {
public:
    virtual ~NimBLEClientCallbacks() {}
    virtual void onConnect(NimBLEClient *) {}
    virtual void onConnectFail(NimBLEClient *, int) {}
    virtual void onDisconnect(NimBLEClient *, int) {}
    virtual bool onConnParamsUpdateRequest(NimBLEClient *, const ble_gap_upd_params *) { return true; }
    virtual void onPassKeyEntry(NimBLEConnInfo &) {}
    virtual uint32_t onPassKeyDisplay(NimBLEConnInfo &) { return 123456; }
    virtual void onAuthenticationComplete(NimBLEConnInfo &) {}
    virtual void onConfirmPasskey(NimBLEConnInfo &, uint32_t) {}
    virtual void onIdentity(NimBLEConnInfo &) {}
    virtual void onMTUChange(NimBLEClient *, uint16_t) {}
    virtual void onPhyUpdate(NimBLEClient *, uint8_t, uint8_t) {}
};

class NimBLEDeviceCallbacks {
public:
    virtual ~NimBLEDeviceCallbacks() {}
    virtual int onStoreStatus(struct ble_store_status_event *, void *) { return 0; }
};

// ---------------------------------------------------------------------------- o servidor
class NimBLEDescriptor {
public:
    explicit NimBLEDescriptor(BLEDescriptor *d) : d(d) {}
    NimBLEDescriptor(const char *uuid, uint16_t = NIMBLE_PROPERTY::READ | NIMBLE_PROPERTY::WRITE, uint16_t max_len = 100) : d(new BLEDescriptor(uuid, max_len)) {}
    NimBLEDescriptor(const NimBLEUUID &uuid, uint16_t = NIMBLE_PROPERTY::READ | NIMBLE_PROPERTY::WRITE, uint16_t max_len = 100) : d(new BLEDescriptor(uuid, max_len)) {}
    virtual ~NimBLEDescriptor() {}
    void setValue(const uint8_t *data, size_t length) { d->setValue((uint8_t *)data, length); }
    void setValue(const std::string &s) { setValue((const uint8_t *)s.data(), s.size()); }
    void setValue(const char *s) { setValue((const uint8_t *)s, strlen(s)); }
    template <class T> typename std::enable_if<std::is_arithmetic<T>::value>::type setValue(const T &v) { setValue((const uint8_t *)&v, sizeof(T)); }
    NimBLEAttValue getValue() const { return NimBLEAttValue(d->getValue()); }
    template <class T> T getValue(time_t *t = nullptr, bool skip = false) const { return getValue().getValue<T>(t, skip); }
    NimBLEUUID getUUID() const { return NimBLEUUID(d->getUUID()); }
    uint16_t getHandle() const { return d->getHandle(); }
    NimBLECharacteristic *getCharacteristic() const { return dona; }
    void setCallbacks(NimBLEDescriptorCallbacks *cb) { callbacks = cb; }
    std::string toString() const { return cs_std(d->toString()); }
    BLEDescriptor *cs_do_core() const { return d; }

protected:
    friend class NimBLECharacteristic;
    BLEDescriptor *d;
    NimBLECharacteristic *dona = nullptr;
    NimBLEDescriptorCallbacks *callbacks = nullptr;
};

class NimBLE2904 : public NimBLEDescriptor {
public:
    static const uint8_t FORMAT_BOOLEAN = 1, FORMAT_UINT2 = 2, FORMAT_UINT4 = 3, FORMAT_UINT8 = 4, FORMAT_UINT12 = 5, FORMAT_UINT16 = 6,
                         FORMAT_UINT24 = 7, FORMAT_UINT32 = 8, FORMAT_UINT48 = 9, FORMAT_UINT64 = 10, FORMAT_UINT128 = 11,
                         FORMAT_SINT8 = 12, FORMAT_SINT12 = 13, FORMAT_SINT16 = 14, FORMAT_SINT24 = 15, FORMAT_SINT32 = 16,
                         FORMAT_SINT48 = 17, FORMAT_SINT64 = 18, FORMAT_SINT128 = 19, FORMAT_FLOAT32 = 20, FORMAT_FLOAT64 = 21,
                         FORMAT_SFLOAT16 = 22, FORMAT_SFLOAT32 = 23, FORMAT_IEEE20601 = 24, FORMAT_UTF8 = 25, FORMAT_UTF16 = 26,
                         FORMAT_OPAQUE = 27;
    NimBLE2904() : NimBLEDescriptor(new BLE2904()) {}
    void setFormat(uint8_t f) { quatro()->setFormat(f); }
    void setUnit(uint16_t u) { quatro()->setUnit(u); }
    void setExponent(int8_t e) { quatro()->setExponent(e); }
    void setNamespace(uint8_t n) { quatro()->setNamespace(n); }
    void setDescription(uint16_t x) { quatro()->setDescription(x); }

private:
    BLE2904 *quatro() const { return static_cast<BLE2904 *>(d); }
};

class NimBLECharacteristic {
public:
    explicit NimBLECharacteristic(BLECharacteristic *c) : c(c) {}
    NimBLECharacteristic(const char *uuid, uint16_t props = NIMBLE_PROPERTY::READ | NIMBLE_PROPERTY::WRITE, uint16_t = BLE_ATT_ATTR_MAX_LEN)
        : c(new BLECharacteristic(uuid, cs_nimble_propriedades(props))), propriedades(props) {}
    NimBLECharacteristic(const NimBLEUUID &uuid, uint16_t props = NIMBLE_PROPERTY::READ | NIMBLE_PROPERTY::WRITE, uint16_t = BLE_ATT_ATTR_MAX_LEN)
        : c(new BLECharacteristic(uuid, cs_nimble_propriedades(props))), propriedades(props) {}

    std::string toString() const { return cs_std(c->toString()); }
    void addDescriptor(NimBLEDescriptor *d)
    {
        d->dona = this;
        c->addDescriptor(d->cs_do_core());
        descritores.push_back(d);
    }
    void removeDescriptor(NimBLEDescriptor *d, bool = false) { descritores.erase(std::remove(descritores.begin(), descritores.end(), d), descritores.end()); }
    uint16_t getProperties() const { return propriedades; }
    void setCallbacks(NimBLECharacteristicCallbacks *cb);
    NimBLECharacteristicCallbacks *getCallbacks() const { return callbacks; }
    NimBLEDescriptor *createDescriptor(const char *uuid, uint32_t props = NIMBLE_PROPERTY::READ | NIMBLE_PROPERTY::WRITE, uint16_t max_len = BLE_ATT_ATTR_MAX_LEN)
    {
        return createDescriptor(NimBLEUUID(uuid), props, max_len);
    }
    NimBLEDescriptor *createDescriptor(const NimBLEUUID &uuid, uint32_t props = NIMBLE_PROPERTY::READ | NIMBLE_PROPERTY::WRITE, uint16_t max_len = BLE_ATT_ATTR_MAX_LEN)
    {
        auto *d = new NimBLEDescriptor(uuid, (uint16_t)props, max_len);
        addDescriptor(d);
        return d;
    }
    NimBLE2904 *create2904()
    {
        auto *d = new NimBLE2904();
        addDescriptor(d);
        return d;
    }
    NimBLEDescriptor *getDescriptorByUUID(const char *uuid, uint16_t i = 0) const { return getDescriptorByUUID(NimBLEUUID(uuid), i); }
    NimBLEDescriptor *getDescriptorByUUID(const NimBLEUUID &uuid, uint16_t i = 0) const
    {
        for (auto *d : descritores)
            if (d->getUUID().equals(uuid) && i-- == 0) return d;
        return nullptr;
    }
    NimBLEDescriptor *getDescriptorByHandle(uint16_t h) const
    {
        for (auto *d : descritores)
            if (d->getHandle() == h) return d;
        return nullptr;
    }
    NimBLEService *getService() const { return servico; }
    NimBLEUUID getUUID() const { return NimBLEUUID(c->getUUID()); }
    uint16_t getHandle() const { return c->getHandle(); }

    void setValue(const uint8_t *data, size_t length) { c->setValue(data, length); }
    void setValue(const std::string &s) { setValue((const uint8_t *)s.data(), s.size()); }
    void setValue(const String &s) { setValue((const uint8_t *)s.c_str(), s.length()); }
    void setValue(const char *s) { setValue((const uint8_t *)s, strlen(s)); }
    void setValue(const NimBLEAttValue &v) { setValue(v.data(), v.length()); }
    void setValue(const std::vector<uint8_t> &v) { setValue(v.data(), v.size()); }
    template <class T> typename std::enable_if<std::is_arithmetic<T>::value>::type setValue(const T &v) { setValue((const uint8_t *)&v, sizeof(T)); }
    NimBLEAttValue getValue(time_t *t = nullptr) const
    {
        if (t) *t = 0;
        return NimBLEAttValue(c->getValue());
    }
    template <class T> T getValue(time_t *t = nullptr, bool skip = false) const { return getValue().getValue<T>(t, skip); }

    /* Na NimBLE o notify(valor) manda o valor sem mudar o guardado; aqui ele passa a ser o valor. */
    bool notify(uint16_t = BLE_HS_CONN_HANDLE_NONE) const
    {
        c->notify();
        return true;
    }
    bool notify(const uint8_t *value, size_t length, uint16_t h = BLE_HS_CONN_HANDLE_NONE) const
    {
        c->setValue(value, length);
        return notify(h);
    }
    bool notify(const std::string &s, uint16_t h = BLE_HS_CONN_HANDLE_NONE) const { return notify((const uint8_t *)s.data(), s.size(), h); }
    bool notify(const std::vector<uint8_t> &v, uint16_t h = BLE_HS_CONN_HANDLE_NONE) const { return notify(v.data(), v.size(), h); }
    template <class T> typename std::enable_if<std::is_arithmetic<T>::value, bool>::type notify(const T &v, uint16_t h = BLE_HS_CONN_HANDLE_NONE) const
    {
        return notify((const uint8_t *)&v, sizeof(T), h);
    }
    bool indicate(uint16_t = BLE_HS_CONN_HANDLE_NONE) const
    {
        c->indicate();
        return true;
    }
    bool indicate(const uint8_t *value, size_t length, uint16_t h = BLE_HS_CONN_HANDLE_NONE) const
    {
        c->setValue(value, length);
        return indicate(h);
    }
    bool indicate(const std::string &s, uint16_t h = BLE_HS_CONN_HANDLE_NONE) const { return indicate((const uint8_t *)s.data(), s.size(), h); }
    template <class T> typename std::enable_if<std::is_arithmetic<T>::value, bool>::type indicate(const T &v, uint16_t h = BLE_HS_CONN_HANDLE_NONE) const
    {
        return indicate((const uint8_t *)&v, sizeof(T), h);
    }
    BLECharacteristic *cs_do_core() const { return c; }

private:
    friend class NimBLEService;
    BLECharacteristic *c;
    NimBLEService *servico = nullptr;
    uint16_t propriedades = 0;
    NimBLECharacteristicCallbacks *callbacks = nullptr;
    std::vector<NimBLEDescriptor *> descritores;
};

class NimBLEService {
public:
    explicit NimBLEService(BLEService *s) : s(s) {}
    NimBLEServer *getServer() const { return servidor; }
    std::string toString() const { return cs_std(s->toString()); }
    void dump() const { s->dump(); }
    bool isStarted() const { return true; }
    bool start() { return true; }
    NimBLECharacteristic *createCharacteristic(const char *uuid, uint32_t props = NIMBLE_PROPERTY::READ | NIMBLE_PROPERTY::WRITE, uint16_t max_len = BLE_ATT_ATTR_MAX_LEN)
    {
        return createCharacteristic(NimBLEUUID(uuid), props, max_len);
    }
    NimBLECharacteristic *createCharacteristic(const NimBLEUUID &uuid, uint32_t props = NIMBLE_PROPERTY::READ | NIMBLE_PROPERTY::WRITE, uint16_t = BLE_ATT_ATTR_MAX_LEN)
    {
        auto *c = cs_nimble_de<NimBLECharacteristic>(s->createCharacteristic(uuid, cs_nimble_propriedades((uint16_t)props)));
        c->servico = this;
        c->propriedades = (uint16_t)props;
        /* A NimBLE poe o 2902 sozinha em quem notifica ou indica. */
        if (props & (NIMBLE_PROPERTY::NOTIFY | NIMBLE_PROPERTY::INDICATE)) c->cs_do_core()->addDescriptor(new BLE2902());
        caracteristicas.push_back(c);
        return c;
    }
    void addCharacteristic(NimBLECharacteristic *c)
    {
        c->servico = this;
        s->addCharacteristic(c->cs_do_core());
        caracteristicas.push_back(c);
    }
    void removeCharacteristic(NimBLECharacteristic *c, bool = false)
    {
        s->removeCharacteristic(c->cs_do_core());
        caracteristicas.erase(std::remove(caracteristicas.begin(), caracteristicas.end(), c), caracteristicas.end());
    }
    NimBLECharacteristic *getCharacteristic(const char *uuid, uint16_t i = 0) const { return getCharacteristic(NimBLEUUID(uuid), i); }
    NimBLECharacteristic *getCharacteristic(const NimBLEUUID &uuid, uint16_t i = 0) const
    {
        for (auto *c : caracteristicas)
            if (c->getUUID().equals(uuid) && i-- == 0) return c;
        return nullptr;
    }
    NimBLECharacteristic *getCharacteristicByHandle(uint16_t h) const
    {
        for (auto *c : caracteristicas)
            if (c->getHandle() == h) return c;
        return nullptr;
    }
    const std::vector<NimBLECharacteristic *> &getCharacteristics() const { return caracteristicas; }
    NimBLEUUID getUUID() const { return NimBLEUUID(s->getUUID()); }
    uint16_t getHandle() const { return s->getHandle(); }
    BLEService *cs_do_core() const { return s; }

private:
    friend class NimBLEServer;
    BLEService *s;
    NimBLEServer *servidor = nullptr;
    std::vector<NimBLECharacteristic *> caracteristicas;
};

// ---------------------------------------------------------------------------- o anuncio
class NimBLEAdvertisementData {
public:
    NimBLEAdvertisementData() : d(new BLEAdvertisementData()) {}
    bool addData(const uint8_t *data, size_t length)
    {
        d->addData(String((const char *)data, (unsigned)length));
        carga.insert(carga.end(), data, data + length);
        return true;
    }
    bool addData(const std::vector<uint8_t> &data) { return addData(data.data(), data.size()); }
    bool setAppearance(uint16_t a)
    {
        d->setAppearance(a);
        uint8_t b[] = {3, 0x19, (uint8_t)a, (uint8_t)(a >> 8)};
        return guardar(b, 4);
    }
    bool setFlags(uint8_t f)
    {
        d->setFlags(f);
        uint8_t b[] = {2, 0x01, f};
        return guardar(b, 3);
    }
    bool addTxPower()
    {
        uint8_t b[] = {2, 0x0A, 0};
        return guardar(b, 3);
    }
    bool setPreferredParams(uint16_t, uint16_t) { return true; }
    bool addServiceUUID(const NimBLEUUID &u) { return setCompleteServices(u); }
    bool addServiceUUID(const char *u) { return addServiceUUID(NimBLEUUID(u)); }
    bool removeServiceUUID(const NimBLEUUID &) { return true; }
    bool removeServiceUUID(const char *) { return true; }
    bool removeServices() { return true; }
    bool setManufacturerData(const uint8_t *data, size_t length)
    {
        String s((const char *)data, (unsigned)length);
        d->setManufacturerData(s);
        return estrutura(0xFF, data, length);
    }
    bool setManufacturerData(const std::string &data) { return setManufacturerData((const uint8_t *)data.data(), data.size()); }
    bool setManufacturerData(const std::vector<uint8_t> &data) { return setManufacturerData(data.data(), data.size()); }
    bool setURI(const std::string &uri) { return estrutura(0x24, (const uint8_t *)uri.data(), uri.size()); }
    bool setName(const std::string &name, bool isComplete = true)
    {
        if (isComplete) d->setName(String(name.c_str()));
        else d->setShortName(String(name.c_str()));
        return estrutura(isComplete ? 0x09 : 0x08, (const uint8_t *)name.data(), name.size());
    }
    bool setShortName(const std::string &name) { return setName(name, false); }
    bool setCompleteServices(const NimBLEUUID &u)
    {
        d->setCompleteServices(u);
        return true;
    }
    bool setPartialServices(const NimBLEUUID &u)
    {
        d->setPartialServices(u);
        return true;
    }
    bool setCompleteServices16(const std::vector<NimBLEUUID> &us)
    {
        for (auto &u : us) d->setCompleteServices(u);
        return true;
    }
    bool setCompleteServices32(const std::vector<NimBLEUUID> &us) { return setCompleteServices16(us); }
    bool setPartialServices16(const std::vector<NimBLEUUID> &us)
    {
        for (auto &u : us) d->setPartialServices(u);
        return true;
    }
    bool setPartialServices32(const std::vector<NimBLEUUID> &us) { return setPartialServices16(us); }
    bool setServiceData(const NimBLEUUID &uuid, const uint8_t *data, size_t length)
    {
        d->setServiceData(uuid, String((const char *)data, (unsigned)length));
        return true;
    }
    bool setServiceData(const NimBLEUUID &uuid, const std::string &data) { return setServiceData(uuid, (const uint8_t *)data.data(), data.size()); }
    bool setServiceData(const NimBLEUUID &uuid, const std::vector<uint8_t> &data) { return setServiceData(uuid, data.data(), data.size()); }
    bool setDirectedAddress(const NimBLEAddress &, bool = true) { return true; }
    void clearData()
    {
        d = new BLEAdvertisementData();
        carga.clear();
    }
    int getDataLocation(uint8_t tipo) const
    {
        for (size_t i = 0; i + 1 < carga.size(); i += carga[i] + 1)
            if (carga[i + 1] == tipo) return (int)i;
        return -1;
    }
    bool removeData(uint8_t) { return true; }
    std::vector<uint8_t> getPayload() const { return carga; }
    std::string toString() const
    {
        static const char h[] = "0123456789abcdef";
        std::string s;
        for (uint8_t b : carga) {
            s += h[b >> 4];
            s += h[b & 15];
        }
        return s;
    }
    BLEAdvertisementData &cs_do_core() const { return *d; }

private:
    BLEAdvertisementData *d;
    std::vector<uint8_t> carga;
    bool guardar(const uint8_t *b, size_t n)
    {
        carga.insert(carga.end(), b, b + n);
        return carga.size() <= 31;
    }
    bool estrutura(uint8_t tipo, const uint8_t *data, size_t n)
    {
        carga.push_back((uint8_t)(n + 1));
        carga.push_back(tipo);
        carga.insert(carga.end(), data, data + n);
        return carga.size() <= 31;
    }
};

class NimBLEAdvertising {
public:
    typedef std::function<void(NimBLEAdvertising *)> advCompleteCB_t;
    explicit NimBLEAdvertising(BLEAdvertising *a) : a(a) {}
    bool start(uint32_t duracao = 0, const NimBLEAddress * = nullptr)
    {
        if (mudou) a->setAdvertisementData(dados.cs_do_core());
        mudou = false;
        a->start();
        (void)duracao;
        return true;
    }
    void setAdvertisingCompleteCallback(advCompleteCB_t cb) { aoAcabar = cb; }
    bool stop()
    {
        a->stop();
        return true;
    }
    bool setConnectableMode(uint8_t) { return true; }
    bool setDiscoverableMode(uint8_t) { return true; }
    bool reset()
    {
        a->reset();
        dados.clearData();
        return true;
    }
    bool isAdvertising() { return a->isAdvertising(); }
    void setScanFilter(bool x, bool y) { a->setScanFilter(x, y); }
    void enableScanResponse(bool e) { a->setScanResponse(e); }
    void setAdvertisingInterval(uint16_t i)
    {
        a->setMinInterval(i);
        a->setMaxInterval(i);
    }
    void setMaxInterval(uint16_t i) { a->setMaxInterval(i); }
    void setMinInterval(uint16_t i) { a->setMinInterval(i); }
    bool setAdvertisementData(const NimBLEAdvertisementData &d)
    {
        dados = d;
        a->setAdvertisementData(dados.cs_do_core());
        mudou = false;
        return true;
    }
    bool setScanResponseData(const NimBLEAdvertisementData &d)
    {
        resposta = d;
        a->setScanResponseData(resposta.cs_do_core());
        return true;
    }
    const NimBLEAdvertisementData &getAdvertisementData() { return dados; }
    const NimBLEAdvertisementData &getScanData() { return resposta; }
    void clearData() { dados.clearData(); }
    bool refreshAdvertisingData() { return start(); }
    bool addServiceUUID(const NimBLEUUID &u)
    {
        a->addServiceUUID(u);
        return true;
    }
    bool addServiceUUID(const char *u) { return addServiceUUID(NimBLEUUID(u)); }
    bool removeServiceUUID(const NimBLEUUID &u)
    {
        a->removeServiceUUID(u);
        return true;
    }
    bool removeServiceUUID(const char *u) { return removeServiceUUID(NimBLEUUID(u)); }
    bool removeServices() { return true; }
    bool setAppearance(uint16_t ap)
    {
        a->setAppearance(ap);
        return true;
    }
    bool setPreferredParams(uint16_t, uint16_t) { return true; }
    bool addTxPower() { return true; }
    bool setName(const std::string &nome)
    {
        a->setName(String(nome.c_str()));
        return true;
    }
    bool setManufacturerData(const uint8_t *data, size_t n) { return mudar(dados.setManufacturerData(data, n)); }
    bool setManufacturerData(const std::string &s) { return mudar(dados.setManufacturerData(s)); }
    bool setManufacturerData(const std::vector<uint8_t> &v) { return mudar(dados.setManufacturerData(v)); }
    bool setURI(const std::string &uri) { return mudar(dados.setURI(uri)); }
    bool setServiceData(const NimBLEUUID &u, const uint8_t *data, size_t n) { return mudar(dados.setServiceData(u, data, n)); }
    bool setServiceData(const NimBLEUUID &u, const std::string &s) { return mudar(dados.setServiceData(u, s)); }
    bool setServiceData(const NimBLEUUID &u, const std::vector<uint8_t> &v) { return mudar(dados.setServiceData(u, v)); }

private:
    BLEAdvertising *a;
    NimBLEAdvertisementData dados, resposta;
    bool mudou = false;
    advCompleteCB_t aoAcabar;
    bool mudar(bool ok)
    {
        mudou = true;
        return ok;
    }
};

class NimBLEServer {
public:
    explicit NimBLEServer(BLEServer *s) : s(s) {}
    bool start() { return true; }
    uint8_t getConnectedCount() const { return (uint8_t)s->getConnectedCount(); }
    bool disconnect(uint16_t handle, uint8_t = BLE_ERR_REM_USER_CONN_TERM) const
    {
        s->disconnect(handle);
        return true;
    }
    bool disconnect(const NimBLEConnInfo &info, uint8_t reason = BLE_ERR_REM_USER_CONN_TERM) const { return disconnect(info.getConnHandle(), reason); }
    void setCallbacks(NimBLEServerCallbacks *cb, bool = true);
    void updateConnParams(uint16_t, uint16_t, uint16_t, uint16_t, uint16_t) const {}
    NimBLEService *createService(const char *uuid) { return createService(NimBLEUUID(uuid)); }
    NimBLEService *createService(const NimBLEUUID &uuid)
    {
        auto *sv = cs_nimble_de<NimBLEService>(s->createService(uuid));
        sv->servidor = this;
        /* A NimBLE mostra os servicos quando o servidor comeca; aqui ja ficam visiveis. */
        sv->cs_do_core()->start();
        servicos.push_back(sv);
        return sv;
    }
    NimBLEService *getServiceByUUID(const char *uuid, uint16_t i = 0) const { return getServiceByUUID(NimBLEUUID(uuid), i); }
    NimBLEService *getServiceByUUID(const NimBLEUUID &uuid, uint16_t i = 0) const
    {
        for (auto *sv : servicos)
            if (sv->getUUID().equals(uuid) && i-- == 0) return sv;
        return nullptr;
    }
    NimBLEService *getServiceByHandle(uint16_t h) const
    {
        for (auto *sv : servicos)
            if (sv->getHandle() == h) return sv;
        return nullptr;
    }
    void removeService(NimBLEService *sv, bool = false)
    {
        s->removeService(sv->cs_do_core());
        servicos.erase(std::remove(servicos.begin(), servicos.end(), sv), servicos.end());
    }
    void addService(NimBLEService *sv)
    {
        sv->servidor = this;
        sv->cs_do_core()->start();
        servicos.push_back(sv);
    }
    uint16_t getPeerMTU(uint16_t handle) const { return s->getPeerMTU(handle); }
    std::vector<uint16_t> getPeerDevices() const
    {
        std::vector<uint16_t> v;
        for (uint16_t i = 0; i < getConnectedCount(); i++) v.push_back(i);
        return v;
    }
    NimBLEConnInfo getPeerInfo(uint8_t i) const { return NimBLEConnInfo(i, NimBLEAddress(), getPeerMTU(i)); }
    NimBLEConnInfo getPeerInfo(const NimBLEAddress &a) const { return NimBLEConnInfo(0, a, getPeerMTU(0)); }
    NimBLEConnInfo getPeerInfoByHandle(uint16_t h) const { return getPeerInfo((uint8_t)h); }
    void advertiseOnDisconnect(bool e) { s->advertiseOnDisconnect(e); }
    void setDataLen(uint16_t, uint16_t) const {}
    bool updatePhy(uint16_t, uint8_t, uint8_t, uint16_t) { return true; }
    bool getPhy(uint16_t, uint8_t *tx, uint8_t *rx)
    {
        if (tx) *tx = BLE_GAP_PHY_1M_MASK;
        if (rx) *rx = BLE_GAP_PHY_1M_MASK;
        return true;
    }
    void sendServiceChangedIndication() const {}
    /* O servidor como cliente do celular conectado (o ANCS): o simulador nao abre esse caminho. */
    NimBLEClient *getClient(uint16_t) { return nullptr; }
    NimBLEClient *getClient(const NimBLEConnInfo &) { return nullptr; }
    void deleteClient() {}
    NimBLEAdvertising *getAdvertising() const;
    bool startAdvertising(uint32_t duracao = 0) const;
    bool stopAdvertising() const;
    BLEServer *cs_do_core() const { return s; }

private:
    BLEServer *s;
    NimBLEServerCallbacks *callbacks = nullptr;
    std::vector<NimBLEService *> servicos;
};

// ---------------------------------------------------------------------------- a varredura
class NimBLEAdvertisedDevice {
public:
    NimBLEAdvertisedDevice() = default;
    explicit NimBLEAdvertisedDevice(const BLEAdvertisedDevice &d) : d(d), endereco(const_cast<BLEAdvertisedDevice &>(d).getAddress())
    {
        uint8_t *p = this->d.getPayload();
        carga.assign(p, p + this->d.getPayloadLength());
    }
    uint8_t getAdvType() const { return d.getAdvType(); }
    uint8_t getAdvFlags() const { return 0x06; }
    uint16_t getAppearance() const { return d.getAppearance(); }
    uint16_t getAdvInterval() const { return 160; }
    uint16_t getMinInterval() const { return 160; }
    uint16_t getMaxInterval() const { return 160; }
    uint8_t getManufacturerDataCount() const { return d.haveManufacturerData() ? 1 : 0; }
    const NimBLEAddress &getAddress() const { return endereco; }
    std::string getManufacturerData(uint8_t = 0) const { return cs_std(d.getManufacturerData()); }
    template <class T> T getManufacturerData(bool skipSizeCheck = false, uint8_t = 0) const
    {
        return NimBLEAttValue(getManufacturerData()).getValue<T>(nullptr, skipSizeCheck);
    }
    std::string getURI() const { return ""; }
    std::string getPayloadByType(uint16_t tipo, uint8_t = 0) const
    {
        for (size_t i = 0; i + 1 < carga.size(); i += carga[i] + 1)
            if (carga[i + 1] == tipo) return std::string((const char *)&carga[i + 2], carga[i] - 1);
        return "";
    }
    std::string getName() const { return cs_std(d.getName()); }
    int8_t getRSSI() const { return (int8_t)d.getRSSI(); }
    NimBLEScan *getScan() const;
    uint8_t getServiceDataCount() const { return (uint8_t)d.getServiceDataCount(); }
    std::string getServiceData(uint8_t = 0) const { return cs_std(d.getServiceData()); }
    std::string getServiceData(const NimBLEUUID &u) const { return getServiceDataUUID().equals(u) ? getServiceData() : ""; }
    NimBLEUUID getServiceDataUUID(uint8_t = 0) const { return NimBLEUUID(d.getServiceDataUUID()); }
    NimBLEUUID getServiceUUID(uint8_t i = 0) const { return i == 0 ? NimBLEUUID(d.getServiceUUID()) : NimBLEUUID(); }
    uint8_t getServiceUUIDCount() const { return (uint8_t)d.getServiceUUIDCount(); }
    NimBLEAddress getTargetAddress(uint8_t = 0) const { return NimBLEAddress(); }
    uint8_t getTargetAddressCount() const { return 0; }
    int8_t getTXPower() const { return d.getTXPower(); }
    uint8_t getAdvLength() const { return (uint8_t)carga.size(); }
    uint8_t getAddressType() const { return d.getAddressType(); }
    bool isAdvertisingService(const NimBLEUUID &u) const { return d.isAdvertisingService(u); }
    bool haveAppearance() const { return d.haveAppearance(); }
    bool haveManufacturerData() const { return d.haveManufacturerData(); }
    bool haveName() const { return d.haveName(); }
    bool haveServiceData() const { return d.haveServiceData(); }
    bool haveServiceUUID() const { return d.haveServiceUUID(); }
    bool haveTXPower() const { return d.haveTXPower(); }
    bool haveConnParams() const { return false; }
    bool haveAdvInterval() const { return false; }
    bool haveTargetAddress() const { return false; }
    bool haveURI() const { return false; }
    bool haveType(uint16_t tipo) const { return getPayloadByType(tipo).size() > 0; }
    std::string toString() const { return cs_std(d.toString()); }
    bool isConnectable() const { return d.isConnectable(); }
    bool isScannable() const { return d.isScannable(); }
    bool isLegacyAdvertisement() const { return true; }
    const std::vector<uint8_t> &getPayload() const { return carga; }
    const BLEAdvertisedDevice &cs_do_core() const { return d; }

private:
    mutable BLEAdvertisedDevice d;
    NimBLEAddress endereco;
    std::vector<uint8_t> carga;
};

class NimBLEScanResults {
public:
    void dump() const
    {
        for (auto *d : lista) Serial.println(d->toString().c_str());
    }
    int getCount() const { return (int)lista.size(); }
    const NimBLEAdvertisedDevice *getDevice(uint32_t i) const { return i < lista.size() ? lista[i] : nullptr; }
    const NimBLEAdvertisedDevice *getDevice(const NimBLEAddress &a) const
    {
        for (auto *d : lista)
            if (d->getAddress().equals(a)) return d;
        return nullptr;
    }
    std::vector<NimBLEAdvertisedDevice *>::const_iterator begin() const { return lista.begin(); }
    std::vector<NimBLEAdvertisedDevice *>::const_iterator end() const { return lista.end(); }
    std::vector<NimBLEAdvertisedDevice *> lista;
};

class NimBLEScanCallbacks {
public:
    virtual ~NimBLEScanCallbacks() {}
    virtual void onDiscovered(const NimBLEAdvertisedDevice *) {}
    virtual void onResult(const NimBLEAdvertisedDevice *) {}
    virtual void onScanEnd(const NimBLEScanResults &, int) {}
};

class NimBLEScan {
public:
    enum Phy { SCAN_1M = 0x01, SCAN_CODED = 0x02, SCAN_ALL = 0x03 };
    explicit NimBLEScan(BLEScan *s) : s(s) {}
    /* Na NimBLE o start volta na hora e a varredura segue numa tarefa; o fim chega no onScanEnd. */
    bool start(uint32_t duracaoMs, bool isContinue = false, bool = true)
    {
        if (varrendo) return true;
        if (!isContinue) limpar();
        duracao = duracaoMs;
        varrendo = true;
        xTaskCreate(&NimBLEScan::tarefa, "NimBLEScan", 8192, this, 1, nullptr);
        return true;
    }
    bool isScanning() { return varrendo; }
    void setScanCallbacks(NimBLEScanCallbacks *cb, bool wantDuplicates = false);
    void setActiveScan(bool a) { s->setActiveScan(a); }
    void setInterval(uint16_t i) { s->setInterval(i); }
    void setWindow(uint16_t w) { s->setWindow(w); }
    void setDuplicateFilter(uint8_t f) { s->setDuplicateFilter(f != 0); }
    void setLimitedOnly(bool) {}
    void setFilterPolicy(uint8_t) {}
    bool stop()
    {
        parar = true;
        s->stop();
        return true;
    }
    void clearResults() { limpar(); }
    NimBLEScanResults getResults() { return resultados; }
    /* Esta espera, como na NimBLE. */
    NimBLEScanResults getResults(uint32_t duracaoMs, bool isContinue = false)
    {
        if (!isContinue) limpar();
        varrer(duracaoMs ? duracaoMs : 1000);
        return resultados;
    }
    void setMaxResults(uint8_t m) { maximo = m; }
    void erase(const NimBLEAddress &a)
    {
        auto &l = resultados.lista;
        l.erase(std::remove_if(l.begin(), l.end(), [&](NimBLEAdvertisedDevice *d) { return d->getAddress().equals(a); }), l.end());
    }
    void erase(const NimBLEAdvertisedDevice *d) { erase(d->getAddress()); }
    void setScanResponseTimeout(uint32_t) {}
    std::string getStatsString() const { return ""; }
    void setPhy(Phy) {}
    void setPeriod(uint32_t) {}

    /* Quem achou um anuncio (pelo callback do BLEScan do core). */
    void cs_achou(const BLEAdvertisedDevice &d)
    {
        auto *novo = new NimBLEAdvertisedDevice(d);
        bool repetido = false;
        for (auto *x : resultados.lista)
            if (x->getAddress().equals(novo->getAddress())) repetido = true;
        if (!repetido && (!maximo || resultados.lista.size() < maximo)) resultados.lista.push_back(novo);
        if (callbacks && (!repetido || duplicados)) {
            callbacks->onDiscovered(novo);
            callbacks->onResult(novo);
        }
    }

private:
    BLEScan *s;
    NimBLEScanCallbacks *callbacks = nullptr;
    NimBLEScanResults resultados;
    volatile bool varrendo = false, parar = false;
    bool duplicados = false;
    uint32_t duracao = 0;
    uint8_t maximo = 0xFF;

    void limpar() { resultados.lista.clear(); }
    void varrer(uint32_t ms)
    {
        uint32_t segundos = (ms + 999) / 1000;
        s->start(segundos ? segundos : 1, true);
    }
    static void tarefa(void *p)
    {
        auto *self = (NimBLEScan *)p;
        self->parar = false;
        do {
            self->varrer(self->duracao ? self->duracao : 1000);
        } while (self->duracao == 0 && !self->parar);
        self->varrendo = false;
        if (self->callbacks) self->callbacks->onScanEnd(self->resultados, self->parar ? BLE_ERR_CONN_TERM_LOCAL : 0);
        vTaskDelete(nullptr);
    }
};

inline void NimBLEScan::setScanCallbacks(NimBLEScanCallbacks *cb, bool wantDuplicates)
{
    struct Ponte : BLEAdvertisedDeviceCallbacks {
        NimBLEScan *scan;
        explicit Ponte(NimBLEScan *s) : scan(s) {}
        void onResult(BLEAdvertisedDevice d) override { scan->cs_achou(d); }
    };
    static std::map<NimBLEScan *, Ponte *> pontes;
    callbacks = cb;
    duplicados = wantDuplicates;
    Ponte *&p = pontes[this];
    if (!p) p = new Ponte(this);
    s->setAdvertisedDeviceCallbacks(p, true);
}

// ---------------------------------------------------------------------------- o cliente
class NimBLERemoteDescriptor {
public:
    explicit NimBLERemoteDescriptor(BLERemoteDescriptor *d) : d(d) {}
    NimBLEAttValue readValue(time_t * = nullptr) { return NimBLEAttValue(d->readValue()); }
    template <class T> T readValue(time_t *t = nullptr, bool skip = false) { return readValue().getValue<T>(t, skip); }
    bool writeValue(const uint8_t *data, size_t n, bool response = false) { return d->writeValue((uint8_t *)data, n, response); }
    bool writeValue(const std::string &s, bool response = false) { return writeValue((const uint8_t *)s.data(), s.size(), response); }
    template <class T> typename std::enable_if<std::is_arithmetic<T>::value, bool>::type writeValue(const T &v, bool response = false)
    {
        return writeValue((const uint8_t *)&v, sizeof(T), response);
    }
    NimBLEUUID getUUID() const { return NimBLEUUID(d->getUUID()); }
    uint16_t getHandle() const { return d->getHandle(); }
    NimBLERemoteCharacteristic *getRemoteCharacteristic() const;
    std::string toString() const { return cs_std(d->toString()); }

private:
    BLERemoteDescriptor *d;
};

class NimBLERemoteCharacteristic {
public:
    typedef std::function<void(NimBLERemoteCharacteristic *, uint8_t *, size_t, bool)> notify_callback;
    explicit NimBLERemoteCharacteristic(BLERemoteCharacteristic *c) : c(c) {}
    std::string toString() const { return cs_std(c->toString()); }
    const NimBLERemoteService *getRemoteService() const;
    void deleteDescriptors() const {}
    size_t deleteDescriptor(const NimBLEUUID &) const { return 0; }
    bool canBroadcast() const { return false; }
    bool canRead() const { return c->canRead(); }
    bool canWriteNoResponse() const { return c->canWriteNoResponse(); }
    bool canWrite() const { return c->canWrite(); }
    bool canNotify() const { return c->canNotify(); }
    bool canIndicate() const { return c->canIndicate(); }
    bool canWriteSigned() const { return false; }
    bool hasExtendedProps() const { return false; }
    NimBLEClient *getClient() const;
    bool subscribe(bool notificacoes = true, const notify_callback cb = nullptr, bool = true) const
    {
        auto *self = const_cast<NimBLERemoteCharacteristic *>(this);
        self->aoNotificar = cb;
        mapa()[c] = self;
        c->registerForNotify(&NimBLERemoteCharacteristic::ponte, notificacoes);
        return canNotify() || canIndicate();
    }
    bool unsubscribe(bool = true) const
    {
        c->registerForNotify(nullptr);
        return true;
    }
    NimBLERemoteDescriptor *getDescriptor(const NimBLEUUID &u) const { return cs_nimble_de<NimBLERemoteDescriptor>(c->getDescriptor(u)); }
    const std::vector<NimBLERemoteDescriptor *> &getDescriptors(bool = false) const
    {
        auto *self = const_cast<NimBLERemoteCharacteristic *>(this);
        self->descritores.clear();
        if (auto *d = getDescriptor(NimBLEUUID((uint16_t)0x2902))) self->descritores.push_back(d);
        return descritores;
    }
    NimBLEAttValue readValue(time_t * = nullptr)
    {
        valor = NimBLEAttValue(c->readValue());
        return valor;
    }
    template <class T> T readValue(time_t *t = nullptr, bool skip = false) { return readValue().getValue<T>(t, skip); }
    NimBLEAttValue getValue(time_t * = nullptr) const { return valor; }
    template <class T> T getValue(time_t *t = nullptr, bool skip = false) const { return valor.getValue<T>(t, skip); }
    bool writeValue(const uint8_t *data, size_t n, bool response = false)
    {
        c->writeValue((uint8_t *)data, n, response);
        return true;
    }
    bool writeValue(const std::string &s, bool response = false) { return writeValue((const uint8_t *)s.data(), s.size(), response); }
    bool writeValue(const char *s, bool response = false) { return writeValue((const uint8_t *)s, strlen(s), response); }
    bool writeValue(const NimBLEAttValue &v, bool response = false) { return writeValue(v.data(), v.length(), response); }
    bool writeValue(const std::vector<uint8_t> &v, bool response = false) { return writeValue(v.data(), v.size(), response); }
    template <class T> typename std::enable_if<std::is_arithmetic<T>::value, bool>::type writeValue(const T &v, bool response = false)
    {
        return writeValue((const uint8_t *)&v, sizeof(T), response);
    }
    NimBLEUUID getUUID() const { return NimBLEUUID(c->getUUID()); }
    uint16_t getHandle() const { return c->getHandle(); }
    BLERemoteCharacteristic *cs_do_core() const { return c; }

private:
    BLERemoteCharacteristic *c;
    notify_callback aoNotificar;
    NimBLEAttValue valor;
    std::vector<NimBLERemoteDescriptor *> descritores;
    static std::map<BLERemoteCharacteristic *, NimBLERemoteCharacteristic *> &mapa()
    {
        static std::map<BLERemoteCharacteristic *, NimBLERemoteCharacteristic *> m;
        return m;
    }
    static void ponte(BLERemoteCharacteristic *b, uint8_t *data, size_t n, bool isNotify)
    {
        auto i = mapa().find(b);
        if (i == mapa().end() || !i->second->aoNotificar) return;
        i->second->valor = NimBLEAttValue(data, (uint16_t)n);
        i->second->aoNotificar(i->second, data, n, isNotify);
    }
};

inline NimBLERemoteCharacteristic *NimBLERemoteDescriptor::getRemoteCharacteristic() const
{
    return cs_nimble_de<NimBLERemoteCharacteristic>(d->getRemoteCharacteristic());
}

class NimBLERemoteService {
public:
    explicit NimBLERemoteService(BLERemoteService *s) : s(s) {}
    NimBLERemoteCharacteristic *getCharacteristic(const char *uuid) const { return getCharacteristic(NimBLEUUID(uuid)); }
    NimBLERemoteCharacteristic *getCharacteristic(const NimBLEUUID &uuid) const
    {
        auto *c = cs_nimble_de<NimBLERemoteCharacteristic>(s->getCharacteristic(uuid));
        if (c) dono()[c] = this;
        return c;
    }
    void deleteCharacteristics() const {}
    size_t deleteCharacteristic(const NimBLEUUID &) const { return 0; }
    NimBLEClient *getClient() const;
    NimBLEAttValue getValue(const NimBLEUUID &uuid) const { return NimBLEAttValue(s->getValue(uuid)); }
    bool setValue(const NimBLEUUID &uuid, const NimBLEAttValue &v) const
    {
        s->setValue(uuid, String(v.c_str(), (unsigned)v.length()));
        return true;
    }
    std::string toString() const { return cs_std(s->toString()); }
    NimBLEUUID getUUID() const { return NimBLEUUID(s->getUUID()); }
    uint16_t getHandle() const { return s->getHandle(); }
    uint16_t getStartHandle() const { return getHandle(); }
    uint16_t getEndHandle() const { return (uint16_t)(getHandle() + 0x20); }
    const std::vector<NimBLERemoteCharacteristic *> &getCharacteristics(bool = false) const
    {
        auto *self = const_cast<NimBLERemoteService *>(this);
        self->lista.clear();
        for (auto &par : *s->getCharacteristics())
            if (auto *c = getCharacteristic(NimBLEUUID(String(par.first.c_str())))) self->lista.push_back(c);
        return lista;
    }
    std::vector<NimBLERemoteCharacteristic *>::iterator begin() const { return const_cast<NimBLERemoteService *>(this)->lista.begin(); }
    std::vector<NimBLERemoteCharacteristic *>::iterator end() const { return const_cast<NimBLERemoteService *>(this)->lista.end(); }
    static std::map<const NimBLERemoteCharacteristic *, const NimBLERemoteService *> &dono()
    {
        static std::map<const NimBLERemoteCharacteristic *, const NimBLERemoteService *> m;
        return m;
    }

private:
    BLERemoteService *s;
    std::vector<NimBLERemoteCharacteristic *> lista;
};

inline const NimBLERemoteService *NimBLERemoteCharacteristic::getRemoteService() const
{
    auto i = NimBLERemoteService::dono().find(this);
    return i == NimBLERemoteService::dono().end() ? nullptr : i->second;
}

class NimBLEClient {
public:
    struct Config {
        uint8_t deleteCallbacks : 1;
        uint8_t deleteOnDisconnect : 1;
        uint8_t deleteOnConnectFail : 1;
        uint8_t asyncConnect : 1;
        uint8_t exchangeMTU : 1;
        uint8_t connectFailRetries : 3;
        Config() : deleteCallbacks(0), deleteOnDisconnect(0), deleteOnConnectFail(0), asyncConnect(0), exchangeMTU(1), connectFailRetries(2) {}
    };
    explicit NimBLEClient(BLEClient *c) : c(c) {}
    bool connect(const NimBLEAdvertisedDevice *dispositivo, bool = true, bool = false, bool = true)
    {
        if (!dispositivo) return false;
        par = dispositivo->getAddress();
        return depois(c->connect(const_cast<BLEAdvertisedDevice *>(&dispositivo->cs_do_core())));
    }
    bool connect(const NimBLEAddress &endereco, bool = true, bool = false, bool = true)
    {
        par = endereco;
        return depois(c->connect(endereco));
    }
    bool connect(bool = true, bool = false, bool = true) { return depois(c->connect(par)); }
    bool disconnect(uint8_t = BLE_ERR_REM_USER_CONN_TERM)
    {
        bool estava = c->isConnected();
        c->disconnect();
        if (estava && callbacks) callbacks->onDisconnect(this, BLE_ERR_CONN_TERM_LOCAL);
        return true;
    }
    bool cancelConnect() const { return true; }
    void setConnectRetries(uint8_t) {}
    void setSelfDelete(bool, bool) {}
    NimBLEAddress getPeerAddress() const { return c->isConnected() ? NimBLEAddress(c->getPeerAddress()) : par; }
    bool setPeerAddress(const NimBLEAddress &a)
    {
        par = a;
        return true;
    }
    int getRssi() const { return c->getRssi(); }
    bool isConnected() const { return c->isConnected(); }
    void setClientCallbacks(NimBLEClientCallbacks *cb, bool = true) { callbacks = cb; }
    std::string toString() const { return cs_std(c->toString()); }
    uint16_t getConnHandle() const { return c->isConnected() ? c->getConnId() : BLE_HS_CONN_HANDLE_NONE; }
    uint16_t getMTU() const { return c->getMTU(); }
    bool exchangeMTU() { return true; }
    bool secureConnection(bool = false) const { return c->secureConnection(); }
    void setConnectTimeout(uint32_t) {}
    bool setDataLen(uint16_t) { return true; }
    bool discoverAttributes()
    {
        getServices(true);
        return true;
    }
    NimBLEConnInfo getConnInfo() const { return NimBLEConnInfo(getConnHandle(), getPeerAddress(), getMTU()); }
    int getLastError() const { return ultimoErro; }
    bool updateConnParams(uint16_t a, uint16_t b, uint16_t l, uint16_t t) { return c->updateConnParams(a, b, l, t); }
    void setConnectionParams(uint16_t, uint16_t, uint16_t, uint16_t, uint16_t = 16, uint16_t = 768) {}
    const std::vector<NimBLERemoteService *> &getServices(bool = false)
    {
        servicos.clear();
        for (auto &p : *c->getServices())
            if (auto *s = getService(NimBLEUUID(String(p.first.c_str())))) servicos.push_back(s);
        return servicos;
    }
    std::vector<NimBLERemoteService *>::iterator begin() { return servicos.begin(); }
    std::vector<NimBLERemoteService *>::iterator end() { return servicos.end(); }
    NimBLERemoteCharacteristic *getCharacteristic(uint16_t handle)
    {
        for (auto *s : getServices())
            for (auto *ch : s->getCharacteristics())
                if (ch->getHandle() == handle) return ch;
        return nullptr;
    }
    NimBLERemoteService *getService(const char *uuid) { return getService(NimBLEUUID(uuid)); }
    NimBLERemoteService *getService(const NimBLEUUID &uuid)
    {
        auto *s = cs_nimble_de<NimBLERemoteService>(c->getService(uuid));
        if (s) donos()[s] = this;
        return s;
    }
    void deleteServices() { servicos.clear(); }
    size_t deleteService(const NimBLEUUID &) { return 0; }
    NimBLEAttValue getValue(const NimBLEUUID &servico, const NimBLEUUID &caracteristica) { return NimBLEAttValue(c->getValue(servico, caracteristica)); }
    bool setValue(const NimBLEUUID &servico, const NimBLEUUID &caracteristica, const NimBLEAttValue &v, bool = false)
    {
        c->setValue(servico, caracteristica, String(v.c_str(), (unsigned)v.length()));
        return true;
    }
    void setConnectPhy(uint8_t) {}
    bool updatePhy(uint8_t, uint8_t, uint16_t = 0) { return true; }
    bool getPhy(uint8_t *tx, uint8_t *rx)
    {
        if (tx) *tx = BLE_GAP_PHY_1M_MASK;
        if (rx) *rx = BLE_GAP_PHY_1M_MASK;
        return true;
    }
    Config getConfig() const { return config; }
    void setConfig(Config c2) { config = c2; }
    static std::map<const NimBLERemoteService *, NimBLEClient *> &donos()
    {
        static std::map<const NimBLERemoteService *, NimBLEClient *> m;
        return m;
    }
    BLEClient *cs_do_core() const { return c; }

private:
    BLEClient *c;
    NimBLEAddress par;
    NimBLEClientCallbacks *callbacks = nullptr;
    std::vector<NimBLERemoteService *> servicos;
    Config config;
    int ultimoErro = 0;
    bool depois(bool ok)
    {
        /* 0x0d: o BLE_HS_ETIMEOUT, o erro da NimBLE quando ninguem responde. */
        ultimoErro = ok ? 0 : 13;
        if (callbacks) {
            if (ok) callbacks->onConnect(this);
            else callbacks->onConnectFail(this, ultimoErro);
        }
        return ok;
    }
};

inline NimBLEClient *NimBLERemoteService::getClient() const
{
    auto i = NimBLEClient::donos().find(this);
    return i == NimBLEClient::donos().end() ? nullptr : i->second;
}
inline NimBLEClient *NimBLERemoteCharacteristic::getClient() const
{
    const NimBLERemoteService *s = getRemoteService();
    return s ? s->getClient() : nullptr;
}

// ---------------------------------------------------------------------------- o aparelho (tudo static)
class NimBLEDevice {
public:
    static bool init(const std::string &nome)
    {
        BLEDevice::init(String(nome.c_str()));
        return true;
    }
    static bool deinit(bool clearAll = false)
    {
        BLEDevice::deinit(clearAll);
        return true;
    }
    static bool setDeviceName(const std::string &) { return true; }
    static bool isInitialized() { return BLEDevice::getInitialized(); }
    static NimBLEAddress getAddress() { return NimBLEAddress(BLEDevice::getAddress()); }
    static std::string toString() { return cs_std(BLEDevice::toString()); }
    static const char *getVersion() { return NIMBLE_CPP_VERSION_STR; }
    static bool whiteListAdd(const NimBLEAddress &a)
    {
        if (!onWhiteList(a)) permitidos().push_back(a);
        return true;
    }
    static bool whiteListRemove(const NimBLEAddress &a)
    {
        auto &p = permitidos();
        p.erase(std::remove_if(p.begin(), p.end(), [&](const NimBLEAddress &x) { return x.equals(a); }), p.end());
        return true;
    }
    static bool onWhiteList(const NimBLEAddress &a)
    {
        for (auto &x : permitidos())
            if (x.equals(a)) return true;
        return false;
    }
    static size_t getWhiteListCount() { return permitidos().size(); }
    static NimBLEAddress getWhiteListAddress(size_t i) { return i < permitidos().size() ? permitidos()[i] : NimBLEAddress(); }
    static bool setOwnAddrType(uint8_t) { return true; }
    static bool setOwnAddr(const NimBLEAddress &) { return true; }
    static bool setOwnAddr(const uint8_t *) { return true; }
    static void setDeviceCallbacks(NimBLEDeviceCallbacks *) {}
    static void setScanDuplicateCacheSize(uint16_t) {}
    static void setScanFilterMode(uint8_t) {}
    static void setScanDuplicateCacheResetTime(uint16_t) {}
    /* O pareamento: o radio da bancada nao cifra, entao o pedido e aceito e a conexao segue aberta. */
    static void setSecurityAuth(bool, bool, bool) {}
    static void setSecurityAuth(uint8_t) {}
    static void setSecurityIOCap(uint8_t) {}
    static void setSecurityInitKey(uint8_t) {}
    static void setSecurityRespKey(uint8_t) {}
    static void setSecurityPasskey(uint32_t k) { chave() = k; }
    static uint32_t getSecurityPasskey() { return chave(); }
    static bool startSecurity(uint16_t, int *rc = nullptr)
    {
        if (rc) *rc = 0;
        return true;
    }
    static bool injectConfirmPasskey(const NimBLEConnInfo &, bool) { return true; }
    static bool injectPassKey(const NimBLEConnInfo &, uint32_t) { return true; }
    static bool setMTU(uint16_t mtu)
    {
        BLEDevice::setMTU(mtu);
        return true;
    }
    static uint16_t getMTU() { return BLEDevice::getMTU(); }
    static int getPower(NimBLETxPowerType = NimBLETxPowerType::All) { return potencia(); }
    static bool setPower(int8_t dbm, NimBLETxPowerType = NimBLETxPowerType::All)
    {
        potencia() = dbm;
        return true;
    }
    static bool setDefaultPhy(uint8_t, uint8_t) { return true; }
    static NimBLEScan *getScan() { return cs_nimble_de<NimBLEScan>(BLEDevice::getScan()); }
    static NimBLEServer *createServer() { return cs_nimble_de<NimBLEServer>(BLEDevice::createServer()); }
    static NimBLEServer *getServer() { return cs_nimble_de<NimBLEServer>(BLEDevice::getServer()); }
    static NimBLEAdvertising *getAdvertising() { return cs_nimble_de<NimBLEAdvertising>(BLEDevice::getAdvertising()); }
    static bool startAdvertising(uint32_t duracao = 0) { return getAdvertising()->start(duracao); }
    static bool stopAdvertising() { return getAdvertising()->stop(); }
    static NimBLEClient *createClient()
    {
        auto *c = cs_nimble_de<NimBLEClient>(BLEDevice::createClient());
        clientes().push_back(c);
        return c;
    }
    static NimBLEClient *createClient(const NimBLEAddress &par)
    {
        auto *c = createClient();
        c->setPeerAddress(par);
        return c;
    }
    static bool deleteClient(NimBLEClient *c)
    {
        if (!c) return false;
        if (c->isConnected()) c->disconnect();
        auto &l = clientes();
        l.erase(std::remove(l.begin(), l.end(), c), l.end());
        return true;
    }
    static NimBLEClient *getClientByHandle(uint16_t h)
    {
        for (auto *c : clientes())
            if (c->isConnected() && c->getConnHandle() == h) return c;
        return nullptr;
    }
    static NimBLEClient *getClientByPeerAddress(const NimBLEAddress &a)
    {
        for (auto *c : clientes())
            if (c->getPeerAddress().equals(a)) return c;
        return nullptr;
    }
    static NimBLEClient *getDisconnectedClient()
    {
        for (auto *c : clientes())
            if (!c->isConnected()) return c;
        return nullptr;
    }
    static size_t getCreatedClientCount() { return clientes().size(); }
    static std::vector<NimBLEClient *> getConnectedClients()
    {
        std::vector<NimBLEClient *> v;
        for (auto *c : clientes())
            if (c->isConnected()) v.push_back(c);
        return v;
    }

private:
    static std::vector<NimBLEAddress> &permitidos()
    {
        static std::vector<NimBLEAddress> p;
        return p;
    }
    static std::vector<NimBLEClient *> &clientes()
    {
        static std::vector<NimBLEClient *> c;
        return c;
    }
    static uint32_t &chave()
    {
        static uint32_t k = 123456;
        return k;
    }
    static int &potencia()
    {
        static int p = 3;
        return p;
    }
};

inline NimBLEAdvertising *NimBLEServer::getAdvertising() const { return NimBLEDevice::getAdvertising(); }
inline bool NimBLEServer::startAdvertising(uint32_t d) const { return NimBLEDevice::startAdvertising(d); }
inline bool NimBLEServer::stopAdvertising() const { return NimBLEDevice::stopAdvertising(); }
inline NimBLEScan *NimBLEAdvertisedDevice::getScan() const { return NimBLEDevice::getScan(); }

inline void NimBLEServer::setCallbacks(NimBLEServerCallbacks *cb, bool)
{
    struct Ponte : BLEServerCallbacks {
        NimBLEServer *servidor;
        explicit Ponte(NimBLEServer *s) : servidor(s) {}
        void onConnect(BLEServer *) override
        {
            NimBLEConnInfo info((uint16_t)(servidor->getConnectedCount() ? servidor->getConnectedCount() - 1 : 0));
            if (servidor->callbacks) servidor->callbacks->onConnect(servidor, info);
        }
        void onDisconnect(BLEServer *) override
        {
            NimBLEConnInfo info(0);
            if (servidor->callbacks) servidor->callbacks->onDisconnect(servidor, info, BLE_ERR_REM_USER_CONN_TERM);
        }
    };
    static std::map<NimBLEServer *, Ponte *> pontes;
    callbacks = cb;
    Ponte *&p = pontes[this];
    if (!p) p = new Ponte(this);
    s->setCallbacks(cb ? p : nullptr);
}

inline void NimBLECharacteristic::setCallbacks(NimBLECharacteristicCallbacks *cb)
{
    struct Ponte : BLECharacteristicCallbacks {
        NimBLECharacteristic *dona;
        explicit Ponte(NimBLECharacteristic *c) : dona(c) {}
        void onRead(BLECharacteristic *) override
        {
            NimBLEConnInfo info(0);
            if (dona->callbacks) dona->callbacks->onRead(dona, info);
        }
        void onWrite(BLECharacteristic *) override
        {
            NimBLEConnInfo info(0);
            if (dona->callbacks) dona->callbacks->onWrite(dona, info);
        }
        void onStatus(BLECharacteristic *, Status s, uint32_t code) override
        {
            NimBLEConnInfo info(0);
            if (!dona->callbacks) return;
            int rc = s == SUCCESS_NOTIFY || s == SUCCESS_INDICATE ? 0 : (int)code;
            dona->callbacks->onStatus(dona, rc);
            dona->callbacks->onStatus(dona, info, rc);
        }
        void onSubscribe(BLECharacteristic *, ble_gap_conn_desc *, uint16_t sub) override
        {
            NimBLEConnInfo info(0);
            if (dona->callbacks) dona->callbacks->onSubscribe(dona, info, sub);
        }
    };
    static std::map<NimBLECharacteristic *, Ponte *> pontes;
    callbacks = cb;
    Ponte *&p = pontes[this];
    if (!p) p = new Ponte(this);
    c->setCallbacks(cb ? p : nullptr);
}

// ---------------------------------------------------------------------------- o Stream sobre uma caracteristica
class NimBLEStream : public Stream {
public:
    enum RxOverflowAction { DROP_OLDER_DATA, DROP_NEW_DATA };
    using RxOverflowCallback = std::function<RxOverflowAction(const uint8_t *data, size_t len, void *userArg)>;
    NimBLEStream() = default;
    virtual ~NimBLEStream() {}
    size_t write(uint8_t c) override { return write(&c, 1); }
    size_t write(const uint8_t *data, size_t len) override = 0;
    using Print::write;
    size_t availableForWrite() const { return tamanhoTx; }
    size_t read(uint8_t *buffer, size_t len)
    {
        size_t n = 0;
        while (n < len && !rx.empty()) {
            buffer[n++] = rx.front();
            rx.erase(rx.begin());
        }
        return n;
    }
    int available() override { return (int)rx.size(); }
    int read() override
    {
        if (rx.empty()) return -1;
        int c = rx.front();
        rx.erase(rx.begin());
        return c;
    }
    int peek() override { return rx.empty() ? -1 : rx.front(); }
    virtual bool ready() const = 0;
    void setRxOverflowCallback(RxOverflowCallback cb, void *userArg = nullptr)
    {
        aoTransbordar = cb;
        argumento = userArg;
    }
    operator bool() const { return ready(); }
    virtual void end() { rx.clear(); }
    size_t pushRx(const uint8_t *data, size_t len)
    {
        size_t cabe = tamanhoRx > rx.size() ? tamanhoRx - rx.size() : 0;
        if (len > cabe) {
            RxOverflowAction a = aoTransbordar ? aoTransbordar(data, len, argumento) : DROP_NEW_DATA;
            if (a == DROP_OLDER_DATA) {
                size_t tirar = std::min(rx.size(), len - cabe);
                rx.erase(rx.begin(), rx.begin() + tirar);
                cabe += tirar;
            }
        }
        size_t n = std::min(len, cabe);
        rx.insert(rx.end(), data, data + n);
        return n;
    }

protected:
    std::vector<uint8_t> rx;
    size_t tamanhoTx = 1024, tamanhoRx = 1024;
    RxOverflowCallback aoTransbordar;
    void *argumento = nullptr;
};

/* O servidor: o que o celular escreve chega no read(), e o write() vai por notify, em pedacos do MTU. */
class NimBLEStreamServer : public NimBLEStream, private NimBLECharacteristicCallbacks {
public:
    NimBLEStreamServer() = default;
    NimBLEStreamServer(const NimBLEStreamServer &) = delete;
    NimBLEStreamServer &operator=(const NimBLEStreamServer &) = delete;
    bool begin(NimBLECharacteristic *chr, uint32_t txBufSize = 1024, uint32_t rxBufSize = 1024)
    {
        if (!chr) return false;
        c = chr;
        tamanhoTx = txBufSize;
        tamanhoRx = rxBufSize;
        c->setCallbacks(this);
        return true;
    }
    bool begin(const NimBLEUUID &svcUuid, const NimBLEUUID &chrUuid, uint32_t txBufSize = 1024, uint32_t rxBufSize = 1024, bool secure = false)
    {
        (void)secure;
        NimBLEServer *s = NimBLEDevice::createServer();
        NimBLEService *sv = s->getServiceByUUID(svcUuid);
        if (!sv) sv = s->createService(svcUuid);
        NimBLECharacteristic *chr = sv->createCharacteristic(
            chrUuid, NIMBLE_PROPERTY::READ | NIMBLE_PROPERTY::WRITE | NIMBLE_PROPERTY::WRITE_NR | NIMBLE_PROPERTY::NOTIFY);
        return begin(chr, txBufSize, rxBufSize);
    }
    void end() override
    {
        NimBLEStream::end();
        if (c) c->setCallbacks(usuario);
        c = nullptr;
    }
    size_t write(const uint8_t *data, size_t len) override
    {
        if (!ready() || !len) return 0;
        size_t pedaco = (size_t)NimBLEDevice::getMTU() > 3 ? NimBLEDevice::getMTU() - 3 : 20;
        for (size_t i = 0; i < len; i += pedaco) c->notify(data + i, std::min(pedaco, len - i));
        return len;
    }
    using Print::write;
    uint16_t getPeerHandle() const { return par; }
    void setCallbacks(NimBLECharacteristicCallbacks *cb) { usuario = cb; }
    bool ready() const override { return c && c->getService() && c->getService()->getServer() && c->getService()->getServer()->getConnectedCount() > 0; }
    void flush() override {}

private:
    NimBLECharacteristic *c = nullptr;
    NimBLECharacteristicCallbacks *usuario = nullptr;
    uint16_t par = BLE_HS_CONN_HANDLE_NONE;
    void onWrite(NimBLECharacteristic *chr, NimBLEConnInfo &info) override
    {
        NimBLEAttValue v = chr->getValue();
        pushRx(v.data(), v.length());
        par = info.getConnHandle();
        if (usuario) usuario->onWrite(chr, info);
    }
    void onSubscribe(NimBLECharacteristic *chr, NimBLEConnInfo &info, uint16_t sub) override
    {
        par = info.getConnHandle();
        if (usuario) usuario->onSubscribe(chr, info, sub);
    }
    void onRead(NimBLECharacteristic *chr, NimBLEConnInfo &info) override
    {
        if (usuario) usuario->onRead(chr, info);
    }
};

/* O cliente: o que o servidor notifica chega no read(), e o write() escreve na caracteristica. */
class NimBLEStreamClient : public NimBLEStream {
public:
    NimBLEStreamClient() = default;
    NimBLEStreamClient(const NimBLEStreamClient &) = delete;
    NimBLEStreamClient &operator=(const NimBLEStreamClient &) = delete;
    bool begin(NimBLERemoteCharacteristic *pChr, bool subscribeNotify = false, uint32_t txBufSize = 1024, uint32_t rxBufSize = 1024)
    {
        if (!pChr) return false;
        c = pChr;
        tamanhoTx = txBufSize;
        tamanhoRx = rxBufSize;
        if (subscribeNotify)
            return c->subscribe(true, [this](NimBLERemoteCharacteristic *ch, uint8_t *d, size_t n, bool isNotify) {
                pushRx(d, n);
                if (aoNotificar) aoNotificar(ch, d, n, isNotify);
            });
        return true;
    }
    void end() override
    {
        NimBLEStream::end();
        if (c) c->unsubscribe();
        c = nullptr;
    }
    void setNotifyCallback(NimBLERemoteCharacteristic::notify_callback cb) { aoNotificar = cb; }
    size_t write(const uint8_t *data, size_t len) override
    {
        if (!ready() || !len) return 0;
        size_t pedaco = (size_t)NimBLEDevice::getMTU() > 3 ? NimBLEDevice::getMTU() - 3 : 20;
        for (size_t i = 0; i < len; i += pedaco) c->writeValue(data + i, std::min(pedaco, len - i), false);
        return len;
    }
    using Print::write;
    bool ready() const override { return c && c->getClient() && c->getClient()->isConnected(); }
    void flush() override {}

private:
    NimBLERemoteCharacteristic *c = nullptr;
    NimBLERemoteCharacteristic::notify_callback aoNotificar;
};

// ---------------------------------------------------------------------------- utilidades e o iBeacon
class NimBLEUtils {
public:
    static const char *returnCodeToString(int rc) { return rc == 0 ? "Success" : "Unknown"; }
    static std::string dataToHexString(const uint8_t *source, uint8_t length)
    {
        static const char h[] = "0123456789abcdef";
        std::string s;
        for (uint8_t i = 0; i < length; i++) {
            s += h[source[i] >> 4];
            s += h[source[i] & 15];
        }
        return s;
    }
    static const char *advTypeToString(uint8_t) { return "Unknown"; }
    static const char *gapEventToString(uint8_t) { return "Unknown"; }
    static NimBLEAddress generateAddr(bool) { return NimBLEAddress(); }
};


/* Como na NimBLE: o sketch escrito para o BLE do core compila com os nomes dela. */
#define BLEDevice NimBLEDevice
#define BLEClient NimBLEClient
#define BLERemoteService NimBLERemoteService
#define BLERemoteCharacteristic NimBLERemoteCharacteristic
#define BLERemoteDescriptor NimBLERemoteDescriptor
#define BLEAdvertisedDevice NimBLEAdvertisedDevice
#define BLEScan NimBLEScan
#define BLEUUID NimBLEUUID
#define BLEAddress NimBLEAddress
#define BLEUtils NimBLEUtils
#define BLEClientCallbacks NimBLEClientCallbacks
#define BLEAdvertisedDeviceCallbacks NimBLEScanCallbacks
#define BLEScanResults NimBLEScanResults
#define BLEServer NimBLEServer
#define BLEService NimBLEService
#define BLECharacteristic NimBLECharacteristic
#define BLEAdvertising NimBLEAdvertising
#define BLEServerCallbacks NimBLEServerCallbacks
#define BLECharacteristicCallbacks NimBLECharacteristicCallbacks
#define BLEAdvertisementData NimBLEAdvertisementData
#define BLEDescriptor NimBLEDescriptor
#define BLE2904 NimBLE2904
#define BLEDescriptorCallbacks NimBLEDescriptorCallbacks
#define BLEBeacon NimBLEBeacon
#define BLEEddystoneTLM NimBLEEddystoneTLM
#define BLEConnInfo NimBLEConnInfo

#endif
