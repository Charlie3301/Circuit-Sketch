/* esp_matter.h -- a API do esp-matter e o que a biblioteca Matter do core usa do
 * SDK do CHIP, para o simulador.
 *
 * O SDK de verdade (connectedhomeip) tem milhares de arquivos e o protocolo dele
 * (PASE, CASE, a criptografia, o Thread e o WiFi das controladoras) nao roda
 * aqui. Os cabecalhos e os endpoints do core (MatterOnOffLight e os outros) sao
 * os de verdade; este arquivo da a eles os numeros de verdade dos clusters e
 * atributos e o modelo de dados: nos, endpoints, clusters, atributos. Quem faz o
 * papel do aplicativo (o Google Home, o Apple Home) e a bancada (motor/Matter.cs):
 * comissiona a placa, escreve nos atributos e le o que a placa reporta. */
#pragma once
#include <stdint.h>
#include <stddef.h>
#include <string.h>
#include <functional>
#include "esp_err.h"
#include "sdkconfig.h"

/* ---- os tipos do CHIP que aparecem na biblioteca ---- */
namespace chip {
using EndpointId = uint16_t;
using ClusterId = uint32_t;
using AttributeId = uint32_t;
using CommandId = uint32_t;
using DeviceTypeId = uint32_t;
constexpr EndpointId kRootEndpointId = 0;
constexpr EndpointId kInvalidEndpointId = 0xFFFF;

template <typename T>
constexpr auto to_underlying(T e) { return static_cast<__underlying_type(T)>(e); }

template <typename E>
class BitMask {
public:
    using Raw = __underlying_type(E);
    constexpr BitMask() : valor(0) {}
    constexpr BitMask(Raw v) : valor(v) {}
    constexpr BitMask(E v) : valor(static_cast<Raw>(v)) {}
    constexpr bool Has(E v) const { return (valor & static_cast<Raw>(v)) != 0; }
    BitMask &Set(E v) { valor |= static_cast<Raw>(v); return *this; }
    BitMask &Clear(E v) { valor &= ~static_cast<Raw>(v); return *this; }
    constexpr Raw Raw_() const { return valor; }
    constexpr operator Raw() const { return valor; }

private:
    Raw valor;
};

template <typename T>
class Optional {
public:
    Optional() : tem(false), valor() {}
    Optional(const T &v) : tem(true), valor(v) {}
    bool HasValue() const { return tem; }
    const T &Value() const { return valor; }

private:
    bool tem;
    T valor;
};
template <typename T>
Optional<T> MakeOptional(const T &v) { return Optional<T>(v); }

template <typename T>
class Span {
public:
    Span() : p(nullptr), n(0) {}
    Span(T *dados, size_t tamanho) : p(dados), n(tamanho) {}
    template <size_t N>
    Span(T (&a)[N]) : p(a), n(N) {}
    T *data() const { return p; }
    size_t size() const { return n; }
    bool empty() const { return n == 0; }
    static Span fromCharString(T *texto) { return Span(texto, strlen(texto)); }
    void reduce_size(size_t k) { n = k; }

private:
    T *p;
    size_t n;
};
using CharSpan = Span<const char>;
using MutableCharSpan = Span<char>;
using ByteSpan = Span<const uint8_t>;
using MutableByteSpan = Span<uint8_t>;

namespace System {
namespace Clock {
struct Milliseconds32 { uint32_t ms; constexpr explicit Milliseconds32(uint32_t v) : ms(v) {} };
struct Seconds16 { uint16_t s; constexpr explicit Seconds16(uint16_t v) : s(v) {} };
} /* namespace Clock */
class Layer {
public:
    using TimerCompleteCallback = void (*)(Layer *, void *);
    int StartTimer(Clock::Milliseconds32 tempo, TimerCompleteCallback f, void *arg);
    int CancelTimer(TimerCompleteCallback f, void *arg);
    int ScheduleLambda_(std::function<void()> f);
    template <typename F> int ScheduleLambda(F f) { return ScheduleLambda_(std::function<void()>(f)); }
};
} /* namespace System */

namespace DeviceLayer {
namespace DeviceEventType {
enum { kRange_Public = 0x8000, kRange_PublicPlatformSpecific = 0x9000 };
enum PublicEventTypes {
    kWiFiConnectivityChange = kRange_Public,
    kThreadConnectivityChange,
    kInternetConnectivityChange,
    kServiceConnectivityChange,
    kServiceProvisioningChange,
    kTimeSyncChange,
    kCHIPoBLEConnectionEstablished,
    kCHIPoBLEConnectionClosed,
    kCloseAllBleConnections,
    kWiFiDeviceAvailable,
    kOperationalNetworkStarted,
    kThreadStateChange,
    kThreadInterfaceStateChange,
    kCHIPoBLEAdvertisingChange,
    kInterfaceIpAddressChanged,
    kCommissioningComplete,
    kFailSafeTimerExpired,
    kOperationalNetworkEnabled,
    kDnssdInitialized,
    kDnssdRestartNeeded,
    kBindingsChangedViaCluster,
    kOtaStateChanged,
    kServerReady,
    kBLEDeinitialized,
    kSecureSessionEstablished,
    kFactoryReset,
    kCommissioningSessionStarted,
    kCommissioningSessionStopped,
    kCommissioningWindowOpened,
    kCommissioningWindowClosed,
    kFabricWillBeRemoved,
    kFabricRemoved,
    kFabricCommitted,
    kFabricUpdated,
};
} /* namespace DeviceEventType */
struct ChipDeviceEvent {
    uint16_t Type;
};
class PlatformManagerImpl {
public:
    void LockChipStack();
    void UnlockChipStack();
};
extern System::Layer &SystemLayer();
PlatformManagerImpl &PlatformMgr();
} /* namespace DeviceLayer */

namespace app {
namespace Clusters {
namespace Globals {
namespace Attributes {
namespace FeatureMap { constexpr AttributeId Id = 0x0000FFFC; }
namespace ClusterRevision { constexpr AttributeId Id = 0x0000FFFD; }
} /* namespace Attributes */
enum class LocationTag : uint16_t { kIndoor = 0, kOutdoor = 1, kInside = 2, kOutside = 3 };
enum class PositionTag : uint16_t { kLeft = 0, kRight = 1, kTop = 2, kBottom = 3, kMiddle = 4, kRow = 5, kColumn = 6 };
} /* namespace Globals */

#define CS_ATRIBUTO(nome, id) namespace nome { constexpr AttributeId Id = id; }
#define CS_GLOBAIS CS_ATRIBUTO(FeatureMap, 0xFFFC) CS_ATRIBUTO(ClusterRevision, 0xFFFD)

namespace Identify {
constexpr ClusterId Id = 0x0003;
namespace Attributes { CS_ATRIBUTO(IdentifyTime, 0) CS_ATRIBUTO(IdentifyType, 1) CS_GLOBAIS }
enum class EffectIdentifierEnum : uint8_t { kBlink = 0, kBreathe = 1, kOkay = 2, kChannelChange = 11, kFinishEffect = 254, kStopEffect = 255 };
} /* namespace Identify */
namespace Groups { constexpr ClusterId Id = 0x0004; }
namespace ScenesManagement { constexpr ClusterId Id = 0x0062; }
namespace OnOff {
constexpr ClusterId Id = 0x0006;
namespace Attributes { CS_ATRIBUTO(OnOff, 0x0000) CS_ATRIBUTO(GlobalSceneControl, 0x4000) CS_ATRIBUTO(OnTime, 0x4001) CS_ATRIBUTO(OffWaitTime, 0x4002) CS_ATRIBUTO(StartUpOnOff, 0x4003) CS_GLOBAIS }
} /* namespace OnOff */
namespace LevelControl {
constexpr ClusterId Id = 0x0008;
namespace Attributes { CS_ATRIBUTO(CurrentLevel, 0x0000) CS_ATRIBUTO(RemainingTime, 0x0001) CS_ATRIBUTO(MinLevel, 0x0002) CS_ATRIBUTO(MaxLevel, 0x0003) CS_ATRIBUTO(Options, 0x000F) CS_ATRIBUTO(StartUpCurrentLevel, 0x4000) CS_GLOBAIS }
} /* namespace LevelControl */
namespace Descriptor {
constexpr ClusterId Id = 0x001D;
namespace Attributes { CS_ATRIBUTO(TagList, 0x0004) CS_GLOBAIS }
namespace Structs { namespace SemanticTagStruct { struct Type { uint16_t mfgCode; uint8_t namespaceID; uint8_t tag; Optional<CharSpan> label; }; } }
} /* namespace Descriptor */
namespace BasicInformation {
constexpr ClusterId Id = 0x0028;
namespace Attributes { CS_ATRIBUTO(NodeLabel, 0x0005) CS_GLOBAIS }
} /* namespace BasicInformation */
namespace Switch {
constexpr ClusterId Id = 0x003B;
namespace Attributes { CS_ATRIBUTO(NumberOfPositions, 0) CS_ATRIBUTO(CurrentPosition, 1) CS_ATRIBUTO(MultiPressMax, 2) CS_GLOBAIS }
} /* namespace Switch */
namespace FixedLabel { constexpr ClusterId Id = 0x0040; }
namespace UserLabel { constexpr ClusterId Id = 0x0041; }
namespace BooleanState {
constexpr ClusterId Id = 0x0045;
namespace Attributes { CS_ATRIBUTO(StateValue, 0) CS_GLOBAIS }
} /* namespace BooleanState */
namespace TemperatureControl {
constexpr ClusterId Id = 0x0056;
namespace Attributes { CS_ATRIBUTO(TemperatureSetpoint, 0) CS_ATRIBUTO(MinTemperature, 1) CS_ATRIBUTO(MaxTemperature, 2) CS_ATRIBUTO(Step, 3) CS_ATRIBUTO(SelectedTemperatureLevel, 4) CS_ATRIBUTO(SupportedTemperatureLevels, 5) CS_GLOBAIS }
} /* namespace TemperatureControl */
namespace WindowCovering {
constexpr ClusterId Id = 0x0102;
namespace Attributes {
CS_ATRIBUTO(Type, 0x0000) CS_ATRIBUTO(PhysicalClosedLimitLift, 0x0001) CS_ATRIBUTO(PhysicalClosedLimitTilt, 0x0002) CS_ATRIBUTO(CurrentPositionLift, 0x0003)
CS_ATRIBUTO(CurrentPositionTilt, 0x0004) CS_ATRIBUTO(NumberOfActuationsLift, 0x0005) CS_ATRIBUTO(NumberOfActuationsTilt, 0x0006) CS_ATRIBUTO(ConfigStatus, 0x0007)
CS_ATRIBUTO(CurrentPositionLiftPercentage, 0x0008) CS_ATRIBUTO(CurrentPositionTiltPercentage, 0x0009) CS_ATRIBUTO(OperationalStatus, 0x000A)
CS_ATRIBUTO(TargetPositionLiftPercent100ths, 0x000B) CS_ATRIBUTO(TargetPositionTiltPercent100ths, 0x000C) CS_ATRIBUTO(EndProductType, 0x000D)
CS_ATRIBUTO(CurrentPositionLiftPercent100ths, 0x000E) CS_ATRIBUTO(CurrentPositionTiltPercent100ths, 0x000F) CS_ATRIBUTO(InstalledOpenLimitLift, 0x0010)
CS_ATRIBUTO(InstalledClosedLimitLift, 0x0011) CS_ATRIBUTO(InstalledOpenLimitTilt, 0x0012) CS_ATRIBUTO(InstalledClosedLimitTilt, 0x0013) CS_ATRIBUTO(Mode, 0x0017)
CS_ATRIBUTO(SafetyStatus, 0x001A) CS_GLOBAIS
}
} /* namespace WindowCovering */
namespace Thermostat {
constexpr ClusterId Id = 0x0201;
namespace Attributes {
CS_ATRIBUTO(LocalTemperature, 0x0000) CS_ATRIBUTO(OccupiedCoolingSetpoint, 0x0011) CS_ATRIBUTO(OccupiedHeatingSetpoint, 0x0012) CS_ATRIBUTO(MinSetpointDeadBand, 0x0019)
CS_ATRIBUTO(ControlSequenceOfOperation, 0x001B) CS_ATRIBUTO(SystemMode, 0x001C) CS_GLOBAIS
}
enum class ControlSequenceOfOperationEnum : uint8_t { kCoolingOnly = 0, kCoolingWithReheat = 1, kHeatingOnly = 2, kHeatingWithReheat = 3, kCoolingAndHeating = 4, kCoolingAndHeatingWithReheat = 5 };
enum class SystemModeEnum : uint8_t { kOff = 0, kAuto = 1, kCool = 3, kHeat = 4, kEmergencyHeat = 5, kPrecooling = 6, kFanOnly = 7, kDry = 8, kSleep = 9 };
} /* namespace Thermostat */
namespace FanControl {
constexpr ClusterId Id = 0x0202;
namespace Attributes { CS_ATRIBUTO(FanMode, 0) CS_ATRIBUTO(FanModeSequence, 1) CS_ATRIBUTO(PercentSetting, 2) CS_ATRIBUTO(PercentCurrent, 3) CS_GLOBAIS }
enum class FanModeEnum : uint8_t { kOff = 0, kLow = 1, kMedium = 2, kHigh = 3, kOn = 4, kAuto = 5, kSmart = 6 };
enum class FanModeSequenceEnum : uint8_t { kOffLowMedHigh = 0, kOffLowHigh = 1, kOffLowMedHighAuto = 2, kOffLowHighAuto = 3, kOffHighAuto = 4, kOffHigh = 5 };
} /* namespace FanControl */
namespace ColorControl {
constexpr ClusterId Id = 0x0300;
namespace Attributes {
CS_ATRIBUTO(CurrentHue, 0x0000) CS_ATRIBUTO(CurrentSaturation, 0x0001) CS_ATRIBUTO(RemainingTime, 0x0002) CS_ATRIBUTO(CurrentX, 0x0003) CS_ATRIBUTO(CurrentY, 0x0004)
CS_ATRIBUTO(ColorTemperatureMireds, 0x0007) CS_ATRIBUTO(ColorMode, 0x0008) CS_ATRIBUTO(Options, 0x000F) CS_ATRIBUTO(EnhancedColorMode, 0x4001) CS_GLOBAIS
}
enum class ColorMode : uint8_t { kCurrentHueAndCurrentSaturation = 0, kCurrentXAndCurrentY = 1, kColorTemperature = 2 };
enum class ColorModeEnum : uint8_t { kCurrentHueAndCurrentSaturation = 0, kCurrentXAndCurrentY = 1, kColorTemperatureMireds = 2 };
enum class EnhancedColorModeEnum : uint8_t { kCurrentHueAndCurrentSaturation = 0, kCurrentXAndCurrentY = 1, kColorTemperatureMireds = 2, kEnhancedCurrentHueAndCurrentSaturation = 3 };
} /* namespace ColorControl */
namespace IlluminanceMeasurement {
constexpr ClusterId Id = 0x0400;
namespace Attributes { CS_ATRIBUTO(MeasuredValue, 0) CS_ATRIBUTO(MinMeasuredValue, 1) CS_ATRIBUTO(MaxMeasuredValue, 2) CS_GLOBAIS }
} /* namespace IlluminanceMeasurement */
namespace TemperatureMeasurement {
constexpr ClusterId Id = 0x0402;
namespace Attributes { CS_ATRIBUTO(MeasuredValue, 0) CS_ATRIBUTO(MinMeasuredValue, 1) CS_ATRIBUTO(MaxMeasuredValue, 2) CS_GLOBAIS }
} /* namespace TemperatureMeasurement */
namespace PressureMeasurement {
constexpr ClusterId Id = 0x0403;
namespace Attributes { CS_ATRIBUTO(MeasuredValue, 0) CS_ATRIBUTO(MinMeasuredValue, 1) CS_ATRIBUTO(MaxMeasuredValue, 2) CS_GLOBAIS }
} /* namespace PressureMeasurement */
namespace RelativeHumidityMeasurement {
constexpr ClusterId Id = 0x0405;
namespace Attributes { CS_ATRIBUTO(MeasuredValue, 0) CS_ATRIBUTO(MinMeasuredValue, 1) CS_ATRIBUTO(MaxMeasuredValue, 2) CS_GLOBAIS }
} /* namespace RelativeHumidityMeasurement */
namespace OccupancySensing {
constexpr ClusterId Id = 0x0406;
namespace Attributes { CS_ATRIBUTO(Occupancy, 0) CS_ATRIBUTO(OccupancySensorType, 1) CS_ATRIBUTO(OccupancySensorTypeBitmap, 2) CS_ATRIBUTO(HoldTime, 0x0003) CS_GLOBAIS }
enum class OccupancySensorTypeEnum : uint8_t { kPir = 0, kUltrasonic = 1, kPIRAndUltrasonic = 2, kPhysicalContact = 3 };
enum class OccupancyBitmap : uint8_t { kOccupied = 1 };
enum class OccupancySensorTypeBitmap : uint8_t { kPir = 1, kUltrasonic = 2, kPhysicalContact = 4 };
enum class Feature : uint32_t { kOther = 1, kPassiveInfrared = 2, kUltrasonic = 4, kPhysicalContact = 8 };
} /* namespace OccupancySensing */
} /* namespace Clusters */

/* O que leva um atributo com "nulo" ao numero (o maior valor do tipo). */
template <typename T>
struct NumericAttributeTraits {
    static constexpr T GetNullValue()
    {
        return static_cast<T>(-1) < 0 ? static_cast<T>(static_cast<T>(1) << (sizeof(T) * 8 - 1)) : static_cast<T>(-1);
    }
    static void SetNull(T &v) { v = GetNullValue(); }
    static bool IsNullValue(T v) { return v == GetNullValue(); }
};
} /* namespace app */
} /* namespace chip */

/* ---- os valores dos atributos (esp_matter_attribute_utils.h) ---- */
typedef enum {
    ESP_MATTER_VAL_TYPE_INVALID = 0,
    ESP_MATTER_VAL_TYPE_BOOLEAN = 1,
    ESP_MATTER_VAL_TYPE_INTEGER = 2,
    ESP_MATTER_VAL_TYPE_FLOAT = 3,
    ESP_MATTER_VAL_TYPE_ARRAY = 4,
    ESP_MATTER_VAL_TYPE_CHAR_STRING = 5,
    ESP_MATTER_VAL_TYPE_OCTET_STRING = 6,
    ESP_MATTER_VAL_TYPE_INT8 = 7,
    ESP_MATTER_VAL_TYPE_UINT8 = 8,
    ESP_MATTER_VAL_TYPE_INT16 = 9,
    ESP_MATTER_VAL_TYPE_UINT16 = 10,
    ESP_MATTER_VAL_TYPE_INT32 = 11,
    ESP_MATTER_VAL_TYPE_UINT32 = 12,
    ESP_MATTER_VAL_TYPE_INT64 = 13,
    ESP_MATTER_VAL_TYPE_UINT64 = 14,
    ESP_MATTER_VAL_TYPE_ENUM8 = 15,
    ESP_MATTER_VAL_TYPE_BITMAP8 = 16,
    ESP_MATTER_VAL_TYPE_BITMAP16 = 17,
    ESP_MATTER_VAL_TYPE_BITMAP32 = 18,
    ESP_MATTER_VAL_TYPE_ENUM16 = 19,
    ESP_MATTER_VAL_NULLABLE_BASE = 0x80,
    ESP_MATTER_VAL_TYPE_NULLABLE_BOOLEAN = ESP_MATTER_VAL_TYPE_BOOLEAN + ESP_MATTER_VAL_NULLABLE_BASE,
    ESP_MATTER_VAL_TYPE_NULLABLE_INT8 = ESP_MATTER_VAL_TYPE_INT8 + ESP_MATTER_VAL_NULLABLE_BASE,
    ESP_MATTER_VAL_TYPE_NULLABLE_UINT8 = ESP_MATTER_VAL_TYPE_UINT8 + ESP_MATTER_VAL_NULLABLE_BASE,
    ESP_MATTER_VAL_TYPE_NULLABLE_INT16 = ESP_MATTER_VAL_TYPE_INT16 + ESP_MATTER_VAL_NULLABLE_BASE,
    ESP_MATTER_VAL_TYPE_NULLABLE_UINT16 = ESP_MATTER_VAL_TYPE_UINT16 + ESP_MATTER_VAL_NULLABLE_BASE,
    ESP_MATTER_VAL_TYPE_NULLABLE_INT32 = ESP_MATTER_VAL_TYPE_INT32 + ESP_MATTER_VAL_NULLABLE_BASE,
    ESP_MATTER_VAL_TYPE_NULLABLE_UINT32 = ESP_MATTER_VAL_TYPE_UINT32 + ESP_MATTER_VAL_NULLABLE_BASE,
} esp_matter_val_type_t;

typedef union {
    bool b;
    int i;
    float f;
    int8_t i8;
    uint8_t u8;
    int16_t i16;
    uint16_t u16;
    int32_t i32;
    uint32_t u32;
    int64_t i64;
    uint64_t u64;
    struct { uint8_t *b; uint16_t s; uint16_t n; uint16_t t; } a;
    void *p;
} esp_matter_val_t;

typedef struct {
    esp_matter_val_type_t type;
    esp_matter_val_t val;
} esp_matter_attr_val_t;

#define CS_VALOR_MATTER(nome, tipo_c, campo, tipo_val)                                                                                                          \
    static inline esp_matter_attr_val_t nome(tipo_c v)                                                                                                          \
    {                                                                                                                                                           \
        esp_matter_attr_val_t a = {};                                                                                                                           \
        a.type = tipo_val;                                                                                                                                      \
        a.val.campo = v;                                                                                                                                        \
        return a;                                                                                                                                               \
    }
static inline esp_matter_attr_val_t esp_matter_invalid(void *) { return esp_matter_attr_val_t{ESP_MATTER_VAL_TYPE_INVALID, {}}; }
CS_VALOR_MATTER(esp_matter_bool, bool, b, ESP_MATTER_VAL_TYPE_BOOLEAN)
CS_VALOR_MATTER(esp_matter_int8, int8_t, i8, ESP_MATTER_VAL_TYPE_INT8)
CS_VALOR_MATTER(esp_matter_uint8, uint8_t, u8, ESP_MATTER_VAL_TYPE_UINT8)
CS_VALOR_MATTER(esp_matter_int16, int16_t, i16, ESP_MATTER_VAL_TYPE_INT16)
CS_VALOR_MATTER(esp_matter_uint16, uint16_t, u16, ESP_MATTER_VAL_TYPE_UINT16)
CS_VALOR_MATTER(esp_matter_int32, int32_t, i32, ESP_MATTER_VAL_TYPE_INT32)
CS_VALOR_MATTER(esp_matter_uint32, uint32_t, u32, ESP_MATTER_VAL_TYPE_UINT32)
CS_VALOR_MATTER(esp_matter_enum8, uint8_t, u8, ESP_MATTER_VAL_TYPE_ENUM8)
CS_VALOR_MATTER(esp_matter_bitmap8, uint8_t, u8, ESP_MATTER_VAL_TYPE_BITMAP8)
CS_VALOR_MATTER(esp_matter_bitmap16, uint16_t, u16, ESP_MATTER_VAL_TYPE_BITMAP16)
CS_VALOR_MATTER(esp_matter_bitmap32, uint32_t, u32, ESP_MATTER_VAL_TYPE_BITMAP32)
CS_VALOR_MATTER(esp_matter_nullable_uint8, uint8_t, u8, ESP_MATTER_VAL_TYPE_NULLABLE_UINT8)
CS_VALOR_MATTER(esp_matter_nullable_int16, int16_t, i16, ESP_MATTER_VAL_TYPE_NULLABLE_INT16)
CS_VALOR_MATTER(esp_matter_nullable_uint16, uint16_t, u16, ESP_MATTER_VAL_TYPE_NULLABLE_UINT16)

/* O "nullable<T>" das configuracoes dos clusters. */
template <typename T>
class nullable {
public:
    nullable() : nulo(true), valor() {}
    nullable(T v) : nulo(false), valor(v) {}
    nullable(std::nullptr_t) : nulo(true), valor() {}
    bool is_null() const { return nulo; }
    T value_or(T outro) const { return nulo ? outro : valor; }
    operator T() const { return valor; }

private:
    bool nulo;
    T valor;
};

/* ---- o modelo de dados ---- */
namespace esp_matter {
struct node_t;
struct endpoint_t;
struct cluster_t;
struct attribute_t;

enum { ENDPOINT_FLAG_NONE = 0, ENDPOINT_FLAG_DESTROYABLE = 1 };
enum { CLUSTER_FLAG_NONE = 0, CLUSTER_FLAG_SERVER = 1, CLUSTER_FLAG_CLIENT = 2 };
enum { ATTRIBUTE_FLAG_NONE = 0 };

namespace attribute {
enum callback_type_t { PRE_UPDATE, POST_UPDATE, READ, WRITE };
typedef esp_err_t (*callback_t)(callback_type_t type, uint16_t endpoint_id, uint32_t cluster_id, uint32_t attribute_id, esp_matter_attr_val_t *val, void *priv_data);
esp_err_t set_callback(callback_t callback);
attribute_t *get(cluster_t *cluster, uint32_t attribute_id);
attribute_t *get(uint16_t endpoint_id, uint32_t cluster_id, uint32_t attribute_id);
esp_err_t get_val(attribute_t *attribute, esp_matter_attr_val_t *val);
esp_err_t set_val(attribute_t *attribute, esp_matter_attr_val_t *val, bool call_callbacks = true);
esp_err_t update(uint16_t endpoint_id, uint32_t cluster_id, uint32_t attribute_id, esp_matter_attr_val_t *val);
esp_err_t report(uint16_t endpoint_id, uint32_t cluster_id, uint32_t attribute_id, esp_matter_attr_val_t *val);
esp_err_t set_deferred_persistence(attribute_t *attribute);
attribute_t *create(cluster_t *cluster, uint32_t attribute_id, uint16_t flags, esp_matter_attr_val_t val);
} /* namespace attribute */

namespace node {
struct config_t {
    struct {
        struct { char node_label[33]; } basic_information;
    } root_node;
    config_t() : root_node() {}
};
node_t *create_raw();
node_t *get();
template <typename F = attribute::callback_t>
node_t *create(config_t *config, attribute::callback_t cb, void *identify) { (void)config; (void)identify; attribute::set_callback(cb); return create_raw(); }
} /* namespace node */

namespace endpoint {
endpoint_t *create(node_t *node, uint8_t flags, void *priv_data);
endpoint_t *get(node_t *node, uint16_t endpoint_id);
endpoint_t *get(uint16_t endpoint_id);
uint16_t get_id(endpoint_t *endpoint);
void *get_priv_data(uint16_t endpoint_id);
esp_err_t add_device_type(endpoint_t *endpoint, uint32_t device_type_id, uint8_t version);
esp_err_t set_semantic_tags(endpoint_t *endpoint, const void *tags, size_t count);
esp_err_t set_semantic_tags(endpoint_t *endpoint, chip::Span<const chip::app::Clusters::Descriptor::Structs::SemanticTagStruct::Type> tags);
endpoint_t *criar_de_verdade(node_t *node, void *priv_data, uint32_t device_type);
} /* namespace endpoint */

namespace cluster {
cluster_t *get(endpoint_t *endpoint, uint32_t cluster_id);
cluster_t *get(uint16_t endpoint_id, uint32_t cluster_id);
cluster_t *criar(endpoint_t *endpoint, uint32_t cluster_id, uint8_t flags);
esp_err_t destroy(cluster_t *cluster);
} /* namespace cluster */

esp_err_t start(void *event_callback, intptr_t callback_arg = 0);
esp_err_t factory_reset();
namespace lock {
class ScopedChipStackLock {
public:
    explicit ScopedChipStackLock(uint32_t ticks = 0) { (void)ticks; }
};
} /* namespace lock */
} /* namespace esp_matter */

/* ---- as configuracoes dos clusters e dos dispositivos (esp_matter_cluster.h, esp_matter_endpoint.h) ---- */
namespace esp_matter {
namespace cluster {
#define CS_FEATURE(nome, bit) namespace nome { constexpr uint32_t get_id() { return bit; } }

namespace identify { cluster_t *create(endpoint_t *endpoint, void *config, uint8_t flags); struct config_t { uint16_t identify_time = 0; uint8_t identify_type = 0; }; }
namespace groups { struct config_t {}; cluster_t *create(endpoint_t *endpoint, config_t *config, uint8_t flags); }
namespace scenes_management { struct config_t {}; cluster_t *create(endpoint_t *endpoint, config_t *config, uint8_t flags); }
namespace fixed_label { struct config_t {}; cluster_t *create(endpoint_t *endpoint, config_t *config, uint8_t flags); }
namespace user_label { struct config_t {}; cluster_t *create(endpoint_t *endpoint, config_t *config, uint8_t flags); }
namespace descriptor {
struct config_t {};
cluster_t *create(endpoint_t *endpoint, config_t *config, uint8_t flags);
namespace feature { namespace tag_list { esp_err_t add(cluster_t *cluster); } }
}
namespace on_off {
struct config_t { bool on_off = false; };
namespace feature { CS_FEATURE(lighting, 1)
namespace lighting { struct config_t { uint8_t global_scene_control = 1; nullable<uint16_t> on_time = 0; nullable<uint16_t> off_wait_time = 0; nullable<uint8_t> start_up_on_off = nullptr; }; esp_err_t add(cluster_t *cluster, config_t *config); } }
cluster_t *create(endpoint_t *endpoint, config_t *config, uint8_t flags);
}
namespace level_control {
struct config_t { nullable<uint8_t> current_level = 0; uint8_t options = 0; };
namespace feature {
CS_FEATURE(on_off, 1) CS_FEATURE(lighting, 2)
namespace on_off { esp_err_t add(cluster_t *cluster); }
namespace lighting { struct config_t { uint16_t remaining_time = 0; uint8_t min_level = 1; uint8_t max_level = 254; nullable<uint8_t> start_up_current_level = nullptr; }; esp_err_t add(cluster_t *cluster, config_t *config); }
}
cluster_t *create(endpoint_t *endpoint, config_t *config, uint8_t flags);
}
namespace color_control {
struct config_t { uint8_t color_mode = 1; uint8_t enhanced_color_mode = 1; uint8_t options = 0; };
namespace feature {
CS_FEATURE(hue_saturation, 1) CS_FEATURE(xy, 8) CS_FEATURE(color_temperature, 16)
namespace hue_saturation { struct config_t { uint8_t current_hue = 0; uint8_t current_saturation = 0; }; esp_err_t add(cluster_t *cluster, config_t *config); }
namespace xy { struct config_t { uint16_t current_x = 24939; uint16_t current_y = 24701; }; esp_err_t add(cluster_t *cluster, config_t *config); }
namespace color_temperature {
struct config_t { uint16_t color_temperature_mireds = 250; uint16_t color_temp_physical_min_mireds = 0; uint16_t color_temp_physical_max_mireds = 0xFEFF; uint16_t couple_color_temp_to_level_min_mireds = 0; nullable<uint16_t> start_up_color_temperature_mireds = nullptr; };
}
}
namespace attribute { attribute_t *create_remaining_time(cluster_t *cluster, uint16_t value); }
cluster_t *create(endpoint_t *endpoint, config_t *config, uint8_t flags);
}
namespace fan_control { struct config_t { uint8_t fan_mode = 0; uint8_t fan_mode_sequence = 2; nullable<uint8_t> percent_setting = 0; uint8_t percent_current = 0; }; }
namespace thermostat {
namespace feature {
CS_FEATURE(heating, 1) CS_FEATURE(cooling, 2) CS_FEATURE(auto_mode, 32)
namespace heating { struct config_t { int16_t occupied_heating_setpoint = 2000; }; }
namespace cooling { struct config_t { int16_t occupied_cooling_setpoint = 2600; }; }
namespace auto_mode { struct config_t { int8_t min_setpoint_dead_band = 25; }; }
}
struct config_t {
    nullable<int16_t> local_temperature = nullptr;
    uint8_t control_sequence_of_operation = 4;
    uint8_t system_mode = 1;
    uint32_t feature_flags = 0;
    struct { feature::heating::config_t heating; feature::cooling::config_t cooling; feature::auto_mode::config_t auto_mode; } features;
};
}
namespace temperature_measurement { struct config_t { nullable<int16_t> measured_value = nullptr; nullable<int16_t> min_measured_value = nullptr; nullable<int16_t> max_measured_value = nullptr; }; }
namespace pressure_measurement { struct config_t { nullable<int16_t> pressure_measured_value = nullptr; nullable<int16_t> measured_value = nullptr; nullable<int16_t> min_measured_value = nullptr; nullable<int16_t> max_measured_value = nullptr; }; }
namespace relative_humidity_measurement { struct config_t { nullable<uint16_t> measured_value = nullptr; nullable<uint16_t> min_measured_value = nullptr; nullable<uint16_t> max_measured_value = nullptr; }; }
namespace illuminance_measurement { struct config_t { nullable<uint16_t> measured_value = 0; nullable<uint16_t> min_measured_value = nullptr; nullable<uint16_t> max_measured_value = nullptr; }; }
namespace boolean_state { struct config_t { bool state_value = false; }; }
namespace occupancy_sensing {
namespace feature {
CS_FEATURE(other, 1) CS_FEATURE(passive_infrared, 2) CS_FEATURE(ultrasonic, 4) CS_FEATURE(physical_contact, 8)
}
namespace attribute { attribute_t *create_hold_time(cluster_t *cluster, uint16_t value); attribute_t *create_hold_time_limits(cluster_t *cluster, uint8_t *value, uint16_t length, uint16_t count); }
struct config_t { uint8_t occupancy = 0; uint8_t occupancy_sensor_type = 0; uint8_t occupancy_sensor_type_bitmap = 1; uint32_t feature_flags = 0; };
}
namespace switch_cluster {
namespace feature {
CS_FEATURE(latching_switch, 1) CS_FEATURE(momentary_switch, 2) CS_FEATURE(momentary_switch_release, 4) CS_FEATURE(momentary_switch_long_press, 8) CS_FEATURE(momentary_switch_multi_press, 16)
namespace momentary_switch_multi_press { struct config_t { uint8_t multi_press_max = 2; }; }
}
struct config_t {
    uint8_t number_of_positions = 2;
    uint8_t current_position = 0;
    uint32_t feature_flags = 0;
    struct { feature::momentary_switch_multi_press::config_t momentary_switch_multi_press; } features;
};
}
namespace window_covering {
namespace feature {
CS_FEATURE(lift, 1) CS_FEATURE(tilt, 2) CS_FEATURE(position_aware_lift, 4) CS_FEATURE(position_aware_tilt, 16)
namespace position_aware_lift { struct config_t { nullable<uint16_t> current_position_lift_percent_100ths = nullptr; nullable<uint16_t> target_position_lift_percent_100ths = nullptr; }; }
namespace position_aware_tilt { struct config_t { nullable<uint16_t> current_position_tilt_percent_100ths = nullptr; nullable<uint16_t> target_position_tilt_percent_100ths = nullptr; }; }
}
struct config_t {
    uint8_t type = 0;
    uint8_t config_status = 0;
    uint8_t operational_status = 0;
    uint8_t end_product_type = 0;
    uint8_t mode = 0;
    uint32_t feature_flags = 0;
    struct { feature::position_aware_lift::config_t position_aware_lift; feature::position_aware_tilt::config_t position_aware_tilt; } features;
    config_t(uint8_t produto = 0) : end_product_type(produto) {}
};
}
namespace temperature_control {
constexpr uint8_t k_max_temp_level_count = 16;
namespace feature {
CS_FEATURE(temperature_number, 1) CS_FEATURE(temperature_level, 2) CS_FEATURE(temperature_step, 4)
namespace temperature_number { struct config_t { int16_t temp_setpoint = 0; int16_t min_temperature = 0; int16_t max_temperature = 0; }; }
namespace temperature_step { struct config_t { int16_t step = 0; }; }
namespace temperature_level { struct config_t { uint8_t selected_temp_level = 0; }; }
}
namespace attribute { attribute_t *create_step(cluster_t *cluster, int16_t value); }
struct config_t {
    uint32_t feature_flags = 0;
    struct { feature::temperature_number::config_t temperature_number; feature::temperature_step::config_t temperature_step; feature::temperature_level::config_t temperature_level; } features;
};
}
namespace basic_information { namespace attribute { attribute_t *create_serial_number(cluster_t *cluster, char *value, uint16_t length); } }
} /* namespace cluster */

namespace endpoint {
#define CS_DISPOSITIVO(nome, ...)                                                                                                                               \
    namespace nome {                                                                                                                                            \
    struct config_t { __VA_ARGS__ };                                                                                                                            \
    endpoint_t *create(node_t *node, config_t *config, uint8_t flags, void *priv_data);                                                                         \
    }
#define CS_LUZ cluster::on_off::config_t on_off; cluster::on_off::feature::lighting::config_t on_off_lighting;
#define CS_NIVEL cluster::level_control::config_t level_control; cluster::level_control::feature::lighting::config_t level_control_lighting;
CS_DISPOSITIVO(on_off_light, CS_LUZ)
CS_DISPOSITIVO(on_off_plug_in_unit, CS_LUZ)
CS_DISPOSITIVO(dimmable_light, CS_LUZ CS_NIVEL)
CS_DISPOSITIVO(dimmable_plug_in_unit, CS_LUZ CS_NIVEL)
CS_DISPOSITIVO(color_temperature_light, CS_LUZ CS_NIVEL cluster::color_control::config_t color_control; cluster::color_control::feature::color_temperature::config_t color_control_color_temperature;)
CS_DISPOSITIVO(extended_color_light, CS_LUZ CS_NIVEL cluster::color_control::config_t color_control; cluster::color_control::feature::color_temperature::config_t color_control_color_temperature; cluster::color_control::feature::xy::config_t color_control_xy;)
CS_DISPOSITIVO(fan, cluster::fan_control::config_t fan_control;)
CS_DISPOSITIVO(temperature_sensor, cluster::temperature_measurement::config_t temperature_measurement;)
CS_DISPOSITIVO(pressure_sensor, cluster::pressure_measurement::config_t pressure_measurement;)
CS_DISPOSITIVO(humidity_sensor, cluster::relative_humidity_measurement::config_t relative_humidity_measurement;)
CS_DISPOSITIVO(light_sensor, cluster::illuminance_measurement::config_t illuminance_measurement;)
CS_DISPOSITIVO(contact_sensor, cluster::boolean_state::config_t boolean_state;)
CS_DISPOSITIVO(water_leak_detector, cluster::boolean_state::config_t boolean_state;)
CS_DISPOSITIVO(water_freeze_detector, cluster::boolean_state::config_t boolean_state;)
CS_DISPOSITIVO(rain_sensor, cluster::boolean_state::config_t boolean_state;)
CS_DISPOSITIVO(occupancy_sensor, cluster::occupancy_sensing::config_t occupancy_sensing;)
CS_DISPOSITIVO(thermostat, cluster::thermostat::config_t thermostat;)
CS_DISPOSITIVO(generic_switch, cluster::switch_cluster::config_t switch_cluster;)
CS_DISPOSITIVO(temperature_controlled_cabinet, cluster::temperature_control::config_t temperature_control;)
namespace window_covering {
struct config_t { cluster::window_covering::config_t window_covering; config_t(uint8_t produto = 0) : window_covering(produto) {} };
endpoint_t *create(node_t *node, config_t *config, uint8_t flags, void *priv_data);
}
} /* namespace endpoint */
namespace identification {
enum callback_type_t { START, STOP, EFFECT };
typedef esp_err_t (*callback_t)(callback_type_t type, uint16_t endpoint_id, uint8_t effect_id, uint8_t effect_variant, void *priv_data);
}
namespace data_model {
class Registro { public: void *Get(const void *) { return nullptr; } };
} /* namespace data_model */
} /* namespace esp_matter */

/* ---- o que a biblioteca usa do servidor do CHIP (o protocolo nao roda aqui) ---- */
#define ESP_MATTER_MAX_SEMANTIC_TAG_COUNT 8
#define CHIP_ERROR_FORMAT "s"
class CHIP_ERROR {
public:
    constexpr CHIP_ERROR(int c = 0) : codigo(c) {}
    bool operator==(const CHIP_ERROR &o) const { return codigo == o.codigo; }
    bool operator!=(const CHIP_ERROR &o) const { return codigo != o.codigo; }
    const char *Format() const { return codigo ? "erro" : "ok"; }
    bool IsSuccess() const { return codigo == 0; }
    int AsInteger() const { return codigo; }

private:
    int codigo;
};
#define CHIP_NO_ERROR CHIP_ERROR(0)
#define CHIP_ERROR_INVALID_ARGUMENT CHIP_ERROR(0x2F)
#define CHIP_ERROR_INCORRECT_STATE CHIP_ERROR(0x03)
#define CHIP_IM_GLOBAL_STATUS(x) CHIP_ERROR(0x500)

namespace chip {
namespace app {
struct ConcreteClusterPath { EndpointId mEndpointId; ClusterId mClusterId; constexpr ConcreteClusterPath(EndpointId e = 0, ClusterId c = 0) : mEndpointId(e), mClusterId(c) {} };
struct ConcreteAttributePath : ConcreteClusterPath { AttributeId mAttributeId = 0; };
struct ConcreteReadAttributePath : ConcreteAttributePath {};
struct ConcreteDataAttributePath : ConcreteAttributePath {};
class AttributeValueEncoder { public: template <typename T> CHIP_ERROR Encode(const T &) { return CHIP_NO_ERROR; } };
class AttributeValueDecoder { public: template <typename T> CHIP_ERROR Decode(T &) { return CHIP_NO_ERROR; } };
class AttributeAccessInterface {
public:
    AttributeAccessInterface(const Optional<EndpointId> &, ClusterId) {}
    virtual ~AttributeAccessInterface() {}
    virtual CHIP_ERROR Read(const ConcreteReadAttributePath &, AttributeValueEncoder &) = 0;
    virtual CHIP_ERROR Write(const ConcreteDataAttributePath &, AttributeValueDecoder &) { return CHIP_NO_ERROR; }
};
class AttributeAccessInterfaceRegistry {
public:
    static AttributeAccessInterfaceRegistry &Instance() { static AttributeAccessInterfaceRegistry r; return r; }
    bool Register(AttributeAccessInterface *) { return true; }
    void Unregister(AttributeAccessInterface *) {}
};
namespace Clusters {
namespace OccupancySensing {
namespace Structs { namespace HoldTimeLimitsStruct { struct Type { uint16_t holdTimeMin; uint16_t holdTimeMax; uint16_t holdTimeDefault; }; } }
class Instance {
public:
    explicit Instance(BitMask<Feature>) {}
    CHIP_ERROR Read(const ConcreteReadAttributePath &, AttributeValueEncoder &) { return CHIP_NO_ERROR; }
    CHIP_ERROR Write(const ConcreteDataAttributePath &, AttributeValueDecoder &) { return CHIP_NO_ERROR; }
};
Structs::HoldTimeLimitsStruct::Type *GetHoldTimeLimitsForEndpoint(EndpointId endpoint);
CHIP_ERROR SetHoldTime(EndpointId endpoint, uint16_t holdTime);
CHIP_ERROR SetHoldTimeLimits(EndpointId endpoint, const Structs::HoldTimeLimitsStruct::Type &limites);
} /* namespace OccupancySensing */
} /* namespace Clusters */
} /* namespace app */
} /* namespace chip */
