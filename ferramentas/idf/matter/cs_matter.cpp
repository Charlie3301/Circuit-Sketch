/* cs_matter.cpp -- o ArduinoMatter e o modelo de dados do esp-matter no simulador.
 *
 * Os endpoints do core (MatterOnOffLight e os outros) sao os de verdade e chamam
 * o que esta aqui: criam o endpoint, leem e escrevem atributos. O protocolo do
 * Matter (PASE, CASE, a criptografia, o IPv6 das controladoras) nao roda: o
 * aplicativo e a bancada (motor/Matter.cs). Ela comissiona a placa ("le o codigo
 * de pareamento"), escreve nos atributos como o aplicativo escreveria (chega no
 * attributeChangeCB do endpoint) e recebe o que a placa reporta. Os codigos de
 * pareamento (o manual e o do QR) sao calculados como na especificacao. */
#include "Arduino.h"
#include <Matter.h>
#include <MatterEndPoint.h>
#include "cs_objeto.h"
#include "soc/soc_caps.h"
#if SOC_WIFI_SUPPORTED
#include "WiFi.h"
#endif
#include <map>
#include <memory>
#include <vector>

using namespace esp_matter;

namespace esp_matter {
struct attribute_t {
    uint16_t endpoint;
    uint32_t cluster, id;
    esp_matter_attr_val_t valor;
};
struct cluster_t {
    uint16_t endpoint;
    uint32_t id;
    std::map<uint32_t, attribute_t *> atributos;
};
struct endpoint_t {
    uint16_t id;
    void *priv;
    uint32_t dispositivo;
    std::map<uint32_t, cluster_t *> clusters;
};
struct node_t {
    std::map<uint16_t, endpoint_t *> endpoints;
    uint16_t proximo = 1;
};
} /* namespace esp_matter */

namespace {
node_t *oNo = nullptr;
attribute::callback_t avisoDeAtributo = nullptr;
bool comecou = false, comissionada = false;
matterNetwork_t redeEscolhida = MATTER_NETWORK_NONE;
uint16_t discriminador = 3840;
uint32_t senhaDePareamento = 20202021;
char nomeDoFabricante[33] = "", nomeDoProduto[33] = "", nomeDoAparelho[33] = "", numeroDeSerie[33] = "", versaoDoHw[65] = "";
uint16_t versaoDoHardware = 0;
bool bleLigado = true, liberarMemoriaBle = true;

/* O Verhoeff do codigo manual (a especificacao do Matter, 5.1.4). */
char digitoVerhoeff(const char *texto)
{
    static const uint8_t d[10][10] = {{0, 1, 2, 3, 4, 5, 6, 7, 8, 9}, {1, 2, 3, 4, 0, 6, 7, 8, 9, 5}, {2, 3, 4, 0, 1, 7, 8, 9, 5, 6}, {3, 4, 0, 1, 2, 8, 9, 5, 6, 7},
                                      {4, 0, 1, 2, 3, 9, 5, 6, 7, 8}, {5, 9, 8, 7, 6, 0, 4, 3, 2, 1}, {6, 5, 9, 8, 7, 1, 0, 4, 3, 2}, {7, 6, 5, 9, 8, 2, 1, 0, 4, 3},
                                      {8, 7, 6, 5, 9, 3, 2, 1, 0, 4}, {9, 8, 7, 6, 5, 4, 3, 2, 1, 0}};
    static const uint8_t p[8][10] = {{0, 1, 2, 3, 4, 5, 6, 7, 8, 9}, {1, 5, 7, 6, 2, 8, 3, 0, 9, 4}, {5, 8, 0, 3, 7, 9, 6, 1, 4, 2}, {8, 9, 1, 6, 0, 4, 3, 5, 2, 7},
                                     {9, 4, 5, 3, 1, 2, 6, 8, 7, 0}, {4, 2, 8, 6, 5, 7, 3, 9, 0, 1}, {2, 7, 9, 3, 8, 0, 6, 4, 1, 5}, {7, 0, 4, 6, 9, 1, 3, 2, 5, 8}};
    static const uint8_t inv[10] = {0, 4, 3, 2, 1, 5, 6, 7, 8, 9};
    int c = 0, n = (int)strlen(texto);
    for (int i = 0; i < n; i++) c = d[c][p[(i + 1) % 8][texto[n - 1 - i] - '0']];
    return (char)('0' + inv[c]);
}

/* O Base-38 do QR (a especificacao, 5.1.3.1). */
String base38(const uint8_t *dados, size_t n)
{
    static const char ALFABETO[] = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ-.";
    String saida;
    for (size_t i = 0; i < n; i += 3) {
        size_t tam = n - i < 3 ? n - i : 3;
        uint32_t v = 0;
        for (size_t k = 0; k < tam; k++) v |= (uint32_t)dados[i + k] << (8 * k);
        int caracteres = tam == 3 ? 5 : tam == 2 ? 4 : 2;
        for (int k = 0; k < caracteres; k++) {
            saida += ALFABETO[v % 38];
            v /= 38;
        }
    }
    return saida;
}

const uint16_t FABRICANTE_DE_TESTE = 0xFFF1, PRODUTO_DE_TESTE = 0x8000;

String codigoDoQr()
{
    uint8_t b[11] = {0};
    int pos = 0;
    auto poe = [&](uint32_t v, int bits) {
        for (int i = 0; i < bits; i++, pos++)
            if (v >> i & 1) b[pos / 8] |= (uint8_t)(1u << (pos % 8));
    };
    poe(0, 3);
    poe(FABRICANTE_DE_TESTE, 16);
    poe(PRODUTO_DE_TESTE, 16);
    poe(0, 2);
    poe(bleLigado ? 2 : 4, 8);
    poe(discriminador, 12);
    poe(senhaDePareamento, 27);
    poe(0, 4);
    return "MT:" + base38(b, sizeof(b));
}

cluster_t *clusterDe(endpoint_t *e, uint32_t id)
{
    auto &c = e->clusters[id];
    if (!c) {
        c = new cluster_t();
        c->endpoint = e->id;
        c->id = id;
    }
    return c;
}

attribute_t *atributoDe(cluster_t *c, uint32_t id)
{
    auto &a = c->atributos[id];
    if (!a) {
        a = new attribute_t();
        a->endpoint = c->endpoint;
        a->cluster = c->id;
        a->id = id;
        a->valor = esp_matter_invalid(nullptr);
    }
    return a;
}

void semear(endpoint_t *e, uint32_t cluster, uint32_t atributo, esp_matter_attr_val_t valor) { atributoDe(clusterDe(e, cluster), atributo)->valor = valor; }

void avisarEvento(matterEvent_t tipo)
{
    if (!ArduinoMatter::_matterEventCB) return;
    chip::DeviceLayer::ChipDeviceEvent evento = {};
    evento.Type = (uint16_t)tipo;
    ArduinoMatter::_matterEventCB(tipo, &evento);
}

double valorEmNumero(const esp_matter_attr_val_t &v)
{
    switch (v.type) {
    case ESP_MATTER_VAL_TYPE_BOOLEAN:
    case ESP_MATTER_VAL_TYPE_NULLABLE_BOOLEAN: return v.val.b;
    case ESP_MATTER_VAL_TYPE_INT8:
    case ESP_MATTER_VAL_TYPE_NULLABLE_INT8: return v.val.i8;
    case ESP_MATTER_VAL_TYPE_UINT8:
    case ESP_MATTER_VAL_TYPE_ENUM8:
    case ESP_MATTER_VAL_TYPE_BITMAP8:
    case ESP_MATTER_VAL_TYPE_NULLABLE_UINT8: return v.val.u8;
    case ESP_MATTER_VAL_TYPE_INT16:
    case ESP_MATTER_VAL_TYPE_NULLABLE_INT16: return v.val.i16;
    case ESP_MATTER_VAL_TYPE_UINT16:
    case ESP_MATTER_VAL_TYPE_ENUM16:
    case ESP_MATTER_VAL_TYPE_BITMAP16:
    case ESP_MATTER_VAL_TYPE_NULLABLE_UINT16: return v.val.u16;
    case ESP_MATTER_VAL_TYPE_INT32:
    case ESP_MATTER_VAL_TYPE_NULLABLE_INT32: return v.val.i32;
    case ESP_MATTER_VAL_TYPE_UINT32:
    case ESP_MATTER_VAL_TYPE_BITMAP32:
    case ESP_MATTER_VAL_TYPE_NULLABLE_UINT32: return v.val.u32;
    case ESP_MATTER_VAL_TYPE_FLOAT: return v.val.f;
    default: return 0;
    }
}

void relatar(uint16_t endpoint, uint32_t cluster, uint32_t atributo, const esp_matter_attr_val_t &v)
{
    char texto[32];
    snprintf(texto, sizeof(texto), "%g", valorEmNumero(v));
    cs_obj_n(nullptr, "__matter_relatar", (int)endpoint, (int64_t)cluster, (int64_t)atributo, texto);
}

/* Aplica no atributo: a escrita do aplicativo passa pelo callback, e o endpoint pode recusar. */
bool aplicar(attribute_t *a, esp_matter_attr_val_t *novo)
{
    if (a->valor.type != ESP_MATTER_VAL_TYPE_INVALID && novo->type != a->valor.type) novo->type = a->valor.type;
    if (avisoDeAtributo) {
        void *priv = endpoint::get_priv_data(a->endpoint);
        if (avisoDeAtributo(attribute::PRE_UPDATE, a->endpoint, a->cluster, a->id, novo, priv) != ESP_OK) return false;
    }
    a->valor = *novo;
    relatar(a->endpoint, a->cluster, a->id, a->valor);
    return true;
}

esp_err_t chamarEndpoint(attribute::callback_type_t tipo, uint16_t endpoint_id, uint32_t cluster_id, uint32_t attribute_id, esp_matter_attr_val_t *val, void *priv)
{
    if (tipo != attribute::PRE_UPDATE || !priv) return ESP_OK;
    return ((MatterEndPoint *)priv)->attributeChangeCB(endpoint_id, cluster_id, attribute_id, val) ? ESP_OK : ESP_FAIL;
}

/* A tarefa do Matter: o que o "aplicativo" da bancada pede. */
void tarefaDoMatter(void *)
{
    bool anuncio = false;
    for (;;) {
        if (!comissionada && cs_obj_i(nullptr, "__matter_comissionada")) {
            comissionada = true;
            avisarEvento(MATTER_COMMISSIONING_SESSION_STARTED);
            avisarEvento(MATTER_COMMISSIONING_COMPLETE);
            avisarEvento(MATTER_FABRIC_COMMITTED);
        } else if (comissionada && !cs_obj_i(nullptr, "__matter_comissionada")) {
            comissionada = false;
            avisarEvento(MATTER_FABRIC_REMOVED);
        }
        if (!anuncio && comecou) {
            anuncio = true;
            avisarEvento(MATTER_SERVER_READY);
            avisarEvento(MATTER_COMMISSIONING_WINDOW_OPEN);
        }
        /* [endpoint, cluster, atributo, valor] em texto */
        String pedido = cs_obj_t(nullptr, "__matter_proxima");
        if (pedido.length()) {
            int ep, c, at;
            double v;
            if (sscanf(pedido.c_str(), "%d %d %d %lf", &ep, &c, &at, &v) == 4 && oNo) {
                attribute_t *a = attribute::get((uint16_t)ep, (uint32_t)c, (uint32_t)at);
                if (a) {
                    esp_matter_attr_val_t novo = a->valor;
                    switch (novo.type) {
                    case ESP_MATTER_VAL_TYPE_BOOLEAN:
                    case ESP_MATTER_VAL_TYPE_NULLABLE_BOOLEAN: novo.val.b = v != 0; break;
                    case ESP_MATTER_VAL_TYPE_INT8:
                    case ESP_MATTER_VAL_TYPE_NULLABLE_INT8: novo.val.i8 = (int8_t)v; break;
                    case ESP_MATTER_VAL_TYPE_UINT8:
                    case ESP_MATTER_VAL_TYPE_ENUM8:
                    case ESP_MATTER_VAL_TYPE_BITMAP8:
                    case ESP_MATTER_VAL_TYPE_NULLABLE_UINT8: novo.val.u8 = (uint8_t)v; break;
                    case ESP_MATTER_VAL_TYPE_INT16:
                    case ESP_MATTER_VAL_TYPE_NULLABLE_INT16: novo.val.i16 = (int16_t)v; break;
                    case ESP_MATTER_VAL_TYPE_UINT16:
                    case ESP_MATTER_VAL_TYPE_ENUM16:
                    case ESP_MATTER_VAL_TYPE_BITMAP16:
                    case ESP_MATTER_VAL_TYPE_NULLABLE_UINT16: novo.val.u16 = (uint16_t)v; break;
                    default: novo.type = ESP_MATTER_VAL_TYPE_UINT32; novo.val.u32 = (uint32_t)v; break;
                    }
                    aplicar(a, &novo);
                }
            }
        }
        delay(10);
    }
}
} /* namespace */

/* =====================================================================
 * o modelo de dados */
namespace esp_matter {
namespace node {
node_t *create_raw()
{
    if (!oNo) oNo = new node_t();
    return oNo;
}
node_t *get() { return create_raw(); }
} /* namespace node */

namespace endpoint {
endpoint_t *create(node_t *no, uint8_t flags, void *priv_data)
{
    (void)flags;
    auto *e = new endpoint_t();
    e->id = no->proximo++;
    e->priv = priv_data;
    e->dispositivo = 0;
    no->endpoints[e->id] = e;
    return e;
}
endpoint_t *get(node_t *no, uint16_t endpoint_id)
{
    if (!no) return nullptr;
    auto i = no->endpoints.find(endpoint_id);
    return i == no->endpoints.end() ? nullptr : i->second;
}
endpoint_t *get(uint16_t endpoint_id) { return get(oNo, endpoint_id); }
uint16_t get_id(endpoint_t *e) { return e ? e->id : chip::kInvalidEndpointId; }
void *get_priv_data(uint16_t endpoint_id)
{
    endpoint_t *e = get(endpoint_id);
    return e ? e->priv : nullptr;
}
esp_err_t add_device_type(endpoint_t *e, uint32_t device_type_id, uint8_t version)
{
    (void)version;
    if (!e) return ESP_ERR_INVALID_ARG;
    e->dispositivo = device_type_id;
    return ESP_OK;
}
esp_err_t set_semantic_tags(endpoint_t *e, const void *tags, size_t count)
{
    (void)tags;
    (void)count;
    return e ? ESP_OK : ESP_ERR_INVALID_ARG;
}
esp_err_t set_semantic_tags(endpoint_t *e, chip::Span<const chip::app::Clusters::Descriptor::Structs::SemanticTagStruct::Type> tags)
{
    (void)tags;
    return e ? ESP_OK : ESP_ERR_INVALID_ARG;
}
} /* namespace endpoint */

namespace cluster {
cluster_t *get(endpoint_t *e, uint32_t cluster_id) { return e ? clusterDe(e, cluster_id) : nullptr; }
cluster_t *get(uint16_t endpoint_id, uint32_t cluster_id) { return get(endpoint::get(endpoint_id), cluster_id); }
esp_err_t destroy(cluster_t *c)
{
    (void)c;
    return ESP_OK;
}
#define CS_CLUSTER_VAZIO(nome, id)                                                                                                                              \
    namespace nome {                                                                                                                                            \
    cluster_t *create(endpoint_t *e, config_t *config, uint8_t flags)                                                                                           \
    {                                                                                                                                                           \
        (void)config;                                                                                                                                           \
        (void)flags;                                                                                                                                            \
        return clusterDe(e, id);                                                                                                                                \
    }                                                                                                                                                           \
    }
CS_CLUSTER_VAZIO(groups, chip::app::Clusters::Groups::Id)
CS_CLUSTER_VAZIO(scenes_management, chip::app::Clusters::ScenesManagement::Id)
CS_CLUSTER_VAZIO(fixed_label, chip::app::Clusters::FixedLabel::Id)
CS_CLUSTER_VAZIO(user_label, chip::app::Clusters::UserLabel::Id)
CS_CLUSTER_VAZIO(descriptor, chip::app::Clusters::Descriptor::Id)
namespace identify {
cluster_t *create(endpoint_t *e, void *config, uint8_t flags)
{
    (void)config;
    (void)flags;
    return clusterDe(e, chip::app::Clusters::Identify::Id);
}
}
namespace descriptor { namespace feature { namespace tag_list { esp_err_t add(cluster_t *c) { (void)c; return ESP_OK; } } } }
namespace on_off {
namespace feature { namespace lighting { esp_err_t add(cluster_t *c, config_t *config) { (void)c; (void)config; return ESP_OK; } } }
cluster_t *create(endpoint_t *e, config_t *config, uint8_t flags)
{
    (void)flags;
    semear(e, chip::app::Clusters::OnOff::Id, chip::app::Clusters::OnOff::Attributes::OnOff::Id, esp_matter_bool(config ? config->on_off : false));
    return clusterDe(e, chip::app::Clusters::OnOff::Id);
}
}
namespace level_control {
namespace feature {
namespace on_off { esp_err_t add(cluster_t *c) { (void)c; return ESP_OK; } }
namespace lighting { esp_err_t add(cluster_t *c, config_t *config) { (void)c; (void)config; return ESP_OK; } }
}
cluster_t *create(endpoint_t *e, config_t *config, uint8_t flags)
{
    (void)flags;
    semear(e, chip::app::Clusters::LevelControl::Id, chip::app::Clusters::LevelControl::Attributes::CurrentLevel::Id, esp_matter_nullable_uint8(config ? config->current_level.value_or(0) : 0));
    return clusterDe(e, chip::app::Clusters::LevelControl::Id);
}
}
namespace color_control {
namespace feature {
namespace hue_saturation {
esp_err_t add(cluster_t *c, config_t *config)
{
    atributoDe(c, chip::app::Clusters::ColorControl::Attributes::CurrentHue::Id)->valor = esp_matter_uint8(config->current_hue);
    atributoDe(c, chip::app::Clusters::ColorControl::Attributes::CurrentSaturation::Id)->valor = esp_matter_uint8(config->current_saturation);
    return ESP_OK;
}
}
namespace xy {
esp_err_t add(cluster_t *c, config_t *config)
{
    atributoDe(c, chip::app::Clusters::ColorControl::Attributes::CurrentX::Id)->valor = esp_matter_uint16(config->current_x);
    atributoDe(c, chip::app::Clusters::ColorControl::Attributes::CurrentY::Id)->valor = esp_matter_uint16(config->current_y);
    return ESP_OK;
}
}
}
namespace attribute {
attribute_t *create_remaining_time(cluster_t *c, uint16_t value)
{
    attribute_t *a = atributoDe(c, chip::app::Clusters::ColorControl::Attributes::RemainingTime::Id);
    a->valor = esp_matter_uint16(value);
    return a;
}
}
cluster_t *create(endpoint_t *e, config_t *config, uint8_t flags)
{
    (void)flags;
    cluster_t *c = clusterDe(e, chip::app::Clusters::ColorControl::Id);
    if (config) {
        atributoDe(c, chip::app::Clusters::ColorControl::Attributes::ColorMode::Id)->valor = esp_matter_enum8(config->color_mode);
        atributoDe(c, chip::app::Clusters::ColorControl::Attributes::EnhancedColorMode::Id)->valor = esp_matter_enum8(config->enhanced_color_mode);
    }
    return c;
}
}
namespace occupancy_sensing {
namespace attribute {
attribute_t *create_hold_time(cluster_t *c, uint16_t value)
{
    attribute_t *a = atributoDe(c, chip::app::Clusters::OccupancySensing::Attributes::HoldTime::Id);
    a->valor = esp_matter_uint16(value);
    return a;
}
attribute_t *create_hold_time_limits(cluster_t *c, uint8_t *value, uint16_t length, uint16_t count)
{
    (void)value;
    (void)length;
    (void)count;
    return atributoDe(c, 0x0004);
}
}
}
namespace temperature_control {
namespace attribute {
attribute_t *create_step(cluster_t *c, int16_t value)
{
    attribute_t *a = atributoDe(c, chip::app::Clusters::TemperatureControl::Attributes::Step::Id);
    a->valor = esp_matter_int16(value);
    return a;
}
}
}
namespace basic_information { namespace attribute { attribute_t *create_serial_number(cluster_t *c, char *value, uint16_t length) { (void)value; (void)length; return atributoDe(c, 0x000F); } } }
} /* namespace cluster */

namespace endpoint {
namespace C = chip::app::Clusters;
#define CS_CRIAR(nome, ...)                                                                                                                                     \
    namespace nome {                                                                                                                                            \
    endpoint_t *create(node_t *no, config_t *config, uint8_t flags, void *priv_data)                                                                            \
    {                                                                                                                                                           \
        endpoint_t *e = endpoint::create(no, flags, priv_data);                                                                                                 \
        __VA_ARGS__;                                                                                                                                            \
        return e;                                                                                                                                               \
    }                                                                                                                                                           \
    }
#define CS_LIGAR semear(e, C::OnOff::Id, C::OnOff::Attributes::OnOff::Id, esp_matter_bool(config->on_off.on_off))
#define CS_NIVELAR semear(e, C::LevelControl::Id, C::LevelControl::Attributes::CurrentLevel::Id, esp_matter_nullable_uint8(config->level_control.current_level.value_or(0)))
#define CS_COR                                                                                                                                                  \
    semear(e, C::ColorControl::Id, C::ColorControl::Attributes::ColorMode::Id, esp_matter_enum8(config->color_control.color_mode));                             \
    semear(e, C::ColorControl::Id, C::ColorControl::Attributes::EnhancedColorMode::Id, esp_matter_enum8(config->color_control.enhanced_color_mode));            \
    semear(e, C::ColorControl::Id, C::ColorControl::Attributes::ColorTemperatureMireds::Id, esp_matter_uint16(config->color_control_color_temperature.color_temperature_mireds))
CS_CRIAR(on_off_light, CS_LIGAR)
CS_CRIAR(on_off_plug_in_unit, CS_LIGAR)
CS_CRIAR(dimmable_light, CS_LIGAR; CS_NIVELAR)
CS_CRIAR(dimmable_plug_in_unit, CS_LIGAR; CS_NIVELAR)
CS_CRIAR(color_temperature_light, CS_LIGAR; CS_NIVELAR; CS_COR)
CS_CRIAR(extended_color_light, CS_LIGAR; CS_NIVELAR; CS_COR;
         semear(e, C::ColorControl::Id, C::ColorControl::Attributes::CurrentX::Id, esp_matter_uint16(config->color_control_xy.current_x));
         semear(e, C::ColorControl::Id, C::ColorControl::Attributes::CurrentY::Id, esp_matter_uint16(config->color_control_xy.current_y)))
CS_CRIAR(fan,
         semear(e, C::FanControl::Id, C::FanControl::Attributes::FanMode::Id, esp_matter_enum8(config->fan_control.fan_mode));
         semear(e, C::FanControl::Id, C::FanControl::Attributes::PercentSetting::Id, esp_matter_nullable_uint8(config->fan_control.percent_setting.value_or(0)));
         semear(e, C::FanControl::Id, C::FanControl::Attributes::PercentCurrent::Id, esp_matter_uint8(config->fan_control.percent_current)))
CS_CRIAR(temperature_sensor, semear(e, C::TemperatureMeasurement::Id, C::TemperatureMeasurement::Attributes::MeasuredValue::Id, esp_matter_nullable_int16(config->temperature_measurement.measured_value.value_or(0))))
CS_CRIAR(pressure_sensor, semear(e, C::PressureMeasurement::Id, C::PressureMeasurement::Attributes::MeasuredValue::Id, esp_matter_nullable_int16(config->pressure_measurement.measured_value.value_or(0))))
CS_CRIAR(humidity_sensor, semear(e, C::RelativeHumidityMeasurement::Id, C::RelativeHumidityMeasurement::Attributes::MeasuredValue::Id, esp_matter_nullable_uint16(config->relative_humidity_measurement.measured_value.value_or(0))))
CS_CRIAR(light_sensor, semear(e, C::IlluminanceMeasurement::Id, C::IlluminanceMeasurement::Attributes::MeasuredValue::Id, esp_matter_nullable_uint16(config->illuminance_measurement.measured_value.value_or(0))))
CS_CRIAR(contact_sensor, semear(e, C::BooleanState::Id, C::BooleanState::Attributes::StateValue::Id, esp_matter_bool(config->boolean_state.state_value)))
CS_CRIAR(water_leak_detector, semear(e, C::BooleanState::Id, C::BooleanState::Attributes::StateValue::Id, esp_matter_bool(config->boolean_state.state_value)))
CS_CRIAR(water_freeze_detector, semear(e, C::BooleanState::Id, C::BooleanState::Attributes::StateValue::Id, esp_matter_bool(config->boolean_state.state_value)))
CS_CRIAR(rain_sensor, semear(e, C::BooleanState::Id, C::BooleanState::Attributes::StateValue::Id, esp_matter_bool(config->boolean_state.state_value)))
CS_CRIAR(occupancy_sensor,
         semear(e, C::OccupancySensing::Id, C::OccupancySensing::Attributes::Occupancy::Id, esp_matter_bitmap8(config->occupancy_sensing.occupancy));
         semear(e, C::OccupancySensing::Id, C::Globals::Attributes::FeatureMap::Id, esp_matter_bitmap32(config->occupancy_sensing.feature_flags)))
CS_CRIAR(thermostat,
         semear(e, C::Thermostat::Id, C::Globals::Attributes::FeatureMap::Id, esp_matter_bitmap32(config->thermostat.feature_flags));
         semear(e, C::Thermostat::Id, C::Thermostat::Attributes::LocalTemperature::Id, esp_matter_nullable_int16(config->thermostat.local_temperature.value_or(0)));
         semear(e, C::Thermostat::Id, C::Thermostat::Attributes::SystemMode::Id, esp_matter_enum8(config->thermostat.system_mode));
         semear(e, C::Thermostat::Id, C::Thermostat::Attributes::ControlSequenceOfOperation::Id, esp_matter_enum8(config->thermostat.control_sequence_of_operation));
         semear(e, C::Thermostat::Id, C::Thermostat::Attributes::OccupiedHeatingSetpoint::Id, esp_matter_int16(config->thermostat.features.heating.occupied_heating_setpoint));
         semear(e, C::Thermostat::Id, C::Thermostat::Attributes::OccupiedCoolingSetpoint::Id, esp_matter_int16(config->thermostat.features.cooling.occupied_cooling_setpoint));
         semear(e, C::Thermostat::Id, C::Thermostat::Attributes::MinSetpointDeadBand::Id, esp_matter_int8(config->thermostat.features.auto_mode.min_setpoint_dead_band)))
CS_CRIAR(generic_switch,
         semear(e, C::Switch::Id, C::Switch::Attributes::CurrentPosition::Id, esp_matter_uint8(config->switch_cluster.current_position));
         semear(e, C::Switch::Id, C::Globals::Attributes::FeatureMap::Id, esp_matter_bitmap32(config->switch_cluster.feature_flags)))
CS_CRIAR(temperature_controlled_cabinet,
         semear(e, C::TemperatureControl::Id, C::Globals::Attributes::FeatureMap::Id, esp_matter_bitmap32(config->temperature_control.feature_flags));
         semear(e, C::TemperatureControl::Id, C::TemperatureControl::Attributes::TemperatureSetpoint::Id, esp_matter_int16(config->temperature_control.features.temperature_number.temp_setpoint));
         semear(e, C::TemperatureControl::Id, C::TemperatureControl::Attributes::MinTemperature::Id, esp_matter_int16(config->temperature_control.features.temperature_number.min_temperature));
         semear(e, C::TemperatureControl::Id, C::TemperatureControl::Attributes::MaxTemperature::Id, esp_matter_int16(config->temperature_control.features.temperature_number.max_temperature));
         semear(e, C::TemperatureControl::Id, C::TemperatureControl::Attributes::Step::Id, esp_matter_int16(config->temperature_control.features.temperature_step.step));
         semear(e, C::TemperatureControl::Id, C::TemperatureControl::Attributes::SelectedTemperatureLevel::Id, esp_matter_uint8(config->temperature_control.features.temperature_level.selected_temp_level)))
namespace window_covering {
endpoint_t *create(node_t *no, config_t *config, uint8_t flags, void *priv_data)
{
    endpoint_t *e = endpoint::create(no, flags, priv_data);
    const cluster::window_covering::config_t &w = config->window_covering;
    semear(e, C::WindowCovering::Id, C::Globals::Attributes::FeatureMap::Id, esp_matter_bitmap32(w.feature_flags));
    semear(e, C::WindowCovering::Id, C::WindowCovering::Attributes::Type::Id, esp_matter_enum8(w.type));
    semear(e, C::WindowCovering::Id, C::WindowCovering::Attributes::EndProductType::Id, esp_matter_enum8(w.end_product_type));
    semear(e, C::WindowCovering::Id, C::WindowCovering::Attributes::ConfigStatus::Id, esp_matter_bitmap8(w.config_status));
    semear(e, C::WindowCovering::Id, C::WindowCovering::Attributes::OperationalStatus::Id, esp_matter_bitmap8(w.operational_status));
    semear(e, C::WindowCovering::Id, C::WindowCovering::Attributes::CurrentPositionLiftPercent100ths::Id, esp_matter_nullable_uint16(w.features.position_aware_lift.current_position_lift_percent_100ths.value_or(0)));
    semear(e, C::WindowCovering::Id, C::WindowCovering::Attributes::TargetPositionLiftPercent100ths::Id, esp_matter_nullable_uint16(w.features.position_aware_lift.target_position_lift_percent_100ths.value_or(0)));
    semear(e, C::WindowCovering::Id, C::WindowCovering::Attributes::CurrentPositionTiltPercent100ths::Id, esp_matter_nullable_uint16(w.features.position_aware_tilt.current_position_tilt_percent_100ths.value_or(0)));
    semear(e, C::WindowCovering::Id, C::WindowCovering::Attributes::TargetPositionTiltPercent100ths::Id, esp_matter_nullable_uint16(w.features.position_aware_tilt.target_position_tilt_percent_100ths.value_or(0)));
    return e;
}
}
} /* namespace endpoint */

namespace attribute {
esp_err_t set_callback(callback_t callback)
{
    avisoDeAtributo = callback;
    return ESP_OK;
}
attribute_t *get(cluster_t *c, uint32_t attribute_id) { return c ? atributoDe(c, attribute_id) : nullptr; }
attribute_t *get(uint16_t endpoint_id, uint32_t cluster_id, uint32_t attribute_id) { return get(cluster::get(endpoint_id, cluster_id), attribute_id); }
esp_err_t get_val(attribute_t *a, esp_matter_attr_val_t *val)
{
    if (!a || !val) return ESP_ERR_INVALID_ARG;
    *val = a->valor;
    return ESP_OK;
}
esp_err_t set_val(attribute_t *a, esp_matter_attr_val_t *val, bool call_callbacks)
{
    (void)call_callbacks;
    if (!a || !val) return ESP_ERR_INVALID_ARG;
    a->valor = *val;
    return ESP_OK;
}
esp_err_t update(uint16_t endpoint_id, uint32_t cluster_id, uint32_t attribute_id, esp_matter_attr_val_t *val)
{
    attribute_t *a = get(endpoint_id, cluster_id, attribute_id);
    if (!a || !val) return ESP_ERR_INVALID_ARG;
    return aplicar(a, val) ? ESP_OK : ESP_FAIL;
}
esp_err_t report(uint16_t endpoint_id, uint32_t cluster_id, uint32_t attribute_id, esp_matter_attr_val_t *val)
{
    if (val) relatar(endpoint_id, cluster_id, attribute_id, *val);
    return ESP_OK;
}
esp_err_t set_deferred_persistence(attribute_t *a)
{
    (void)a;
    return ESP_OK;
}
attribute_t *create(cluster_t *c, uint32_t attribute_id, uint16_t flags, esp_matter_attr_val_t val)
{
    (void)flags;
    attribute_t *a = atributoDe(c, attribute_id);
    a->valor = val;
    return a;
}
} /* namespace attribute */

namespace cluster {
namespace switch_cluster {
namespace event {
static esp_err_t mandar(uint16_t endpoint_id, const char *nome, uint8_t posicao, uint8_t quantas)
{
    cs_obj_n(nullptr, "__matter_evento", (int)endpoint_id, nome, (int)posicao, (int)quantas);
    return ESP_OK;
}
esp_err_t send_initial_press(uint16_t e, uint8_t p) { return mandar(e, "InitialPress", p, 0); }
esp_err_t send_short_release(uint16_t e, uint8_t p) { return mandar(e, "ShortRelease", p, 0); }
esp_err_t send_long_press(uint16_t e, uint8_t p) { return mandar(e, "LongPress", p, 0); }
esp_err_t send_long_release(uint16_t e, uint8_t p) { return mandar(e, "LongRelease", p, 0); }
esp_err_t send_multi_press_ongoing(uint16_t e, uint8_t p, uint8_t q) { return mandar(e, "MultiPressOngoing", p, q); }
esp_err_t send_multi_press_complete(uint16_t e, uint8_t p, uint8_t q) { return mandar(e, "MultiPressComplete", p, q); }
} /* namespace event */
} /* namespace switch_cluster */
} /* namespace cluster */

namespace data_model {
chip::app::ServerClusterInterface *Registro::Get(const chip::app::ConcreteClusterPath &caminho)
{
    if (caminho.mClusterId != chip::app::Clusters::BooleanState::Id || !cluster::get(caminho.mEndpointId, caminho.mClusterId)) return nullptr;
    static auto *clusters = new std::map<uint16_t, chip::app::Clusters::BooleanStateCluster *>();
    auto &c = (*clusters)[caminho.mEndpointId];
    if (!c) c = new chip::app::Clusters::BooleanStateCluster(caminho.mEndpointId);
    return c;
}
namespace provider {
Registro &Provedor::registry()
{
    static Registro r;
    return r;
}
Provedor &get_instance()
{
    static Provedor p;
    return p;
}
} /* namespace provider */
} /* namespace data_model */

esp_err_t start(void *event_callback, intptr_t callback_arg)
{
    (void)event_callback;
    (void)callback_arg;
    return ESP_OK;
}
esp_err_t factory_reset()
{
    esp_restart();
    return ESP_OK;
}
} /* namespace esp_matter */

namespace chip {
namespace System {
int Layer::StartTimer(Clock::Milliseconds32 tempo, TimerCompleteCallback f, void *arg)
{
    (void)tempo;
    (void)f;
    (void)arg;
    return 0;
}
int Layer::CancelTimer(TimerCompleteCallback f, void *arg)
{
    (void)f;
    (void)arg;
    return 0;
}
int Layer::ScheduleLambda_(std::function<void()> f)
{
    f();
    return 0;
}
} /* namespace System */
namespace DeviceLayer {
void PlatformManagerImpl::LockChipStack() {}
void PlatformManagerImpl::UnlockChipStack() {}
System::Layer &SystemLayer()
{
    static System::Layer camada;
    return camada;
}
PlatformManagerImpl &PlatformMgr()
{
    static PlatformManagerImpl p;
    return p;
}
} /* namespace DeviceLayer */
namespace app {
namespace Clusters {
void BooleanStateCluster::SetStateValue(bool valor)
{
    esp_matter_attr_val_t v = esp_matter_bool(valor);
    esp_matter::attribute::update(endpoint, BooleanState::Id, BooleanState::Attributes::StateValue::Id, &v);
}
namespace TemperatureControl {
static SupportedTemperatureLevelsIteratorDelegate *niveis = nullptr;
SupportedTemperatureLevelsIteratorDelegate *GetInstance() { return niveis; }
void SetInstance(SupportedTemperatureLevelsIteratorDelegate *instancia) { niveis = instancia; }
} /* namespace TemperatureControl */
namespace OccupancySensing {
static Structs::HoldTimeLimitsStruct::Type limites = {1, 3600, 60};
Structs::HoldTimeLimitsStruct::Type *GetHoldTimeLimitsForEndpoint(EndpointId endpoint)
{
    (void)endpoint;
    return &limites;
}
CHIP_ERROR SetHoldTime(EndpointId endpoint, uint16_t holdTime)
{
    esp_matter_attr_val_t v = esp_matter_uint16(holdTime);
    return esp_matter::attribute::update(endpoint, Id, Attributes::HoldTime::Id, &v) == ESP_OK ? CHIP_NO_ERROR : CHIP_ERROR_INCORRECT_STATE;
}
CHIP_ERROR SetHoldTimeLimits(EndpointId endpoint, const Structs::HoldTimeLimitsStruct::Type &novos)
{
    (void)endpoint;
    limites = novos;
    return CHIP_NO_ERROR;
}
} /* namespace OccupancySensing */
} /* namespace Clusters */
} /* namespace app */
} /* namespace chip */

/* =====================================================================
 * o ArduinoMatter */
ArduinoMatter::matterEventCB ArduinoMatter::_matterEventCB;
ArduinoMatter::bleMemoryReleasedCB ArduinoMatter::_bleMemoryReleasedCB;

void ArduinoMatter::_init()
{
    if (!oNo) {
        node::config_t config;
        node::create<>(&config, chamarEndpoint, nullptr);
    }
}
void ArduinoMatter::begin()
{
    _init();
    if (comecou) return;
    comecou = true;
    xTaskCreate(tarefaDoMatter, "Matter", 8192, nullptr, 5, nullptr);
}
String ArduinoMatter::getManualPairingCode()
{
    /* O codigo curto (11 digitos): sem fabricante e produto. */
    uint32_t curto = (uint32_t)(discriminador >> 8);
    uint32_t c1 = (0u << 2) | (curto >> 2);
    uint32_t c2 = ((curto & 3) << 14) | (senhaDePareamento & 0x3FFF);
    uint32_t c3 = senhaDePareamento >> 14;
    char t[16];
    snprintf(t, sizeof(t), "%01u%05u%04u", (unsigned)c1, (unsigned)c2, (unsigned)c3);
    size_t n = strlen(t);
    t[n] = digitoVerhoeff(t);
    t[n + 1] = 0;
    return String(t);
}
String ArduinoMatter::getOnboardingQRCodeUrl()
{
    String texto = codigoDoQr();
    String url = "https://project-chip.github.io/connectedhomeip/qrcode.html?data=";
    for (size_t i = 0; i < texto.length(); i++) {
        if (texto[i] == ':') url += "%3A";
        else url += texto[i];
    }
    return url;
}
bool ArduinoMatter::setVendorName(const char *name) { snprintf(nomeDoFabricante, sizeof(nomeDoFabricante), "%s", name ? name : ""); return !comecou; }
bool ArduinoMatter::setProductName(const char *name) { snprintf(nomeDoProduto, sizeof(nomeDoProduto), "%s", name ? name : ""); return !comecou; }
bool ArduinoMatter::setDeviceName(const char *name) { snprintf(nomeDoAparelho, sizeof(nomeDoAparelho), "%s", name ? name : ""); return true; }
bool ArduinoMatter::setSerialNumber(const char *value) { snprintf(numeroDeSerie, sizeof(numeroDeSerie), "%s", value ? value : ""); return !comecou; }
bool ArduinoMatter::setHardwareVersion(uint16_t version) { versaoDoHardware = version; return !comecou; }
bool ArduinoMatter::setHardwareVersionString(const char *value) { snprintf(versaoDoHw, sizeof(versaoDoHw), "%s", value ? value : ""); return !comecou; }
bool ArduinoMatter::setSetupDiscriminator(uint16_t d)
{
    if (comecou || d > 0xFFF) return false;
    discriminador = d;
    return true;
}
bool ArduinoMatter::setSetupPasscode(uint32_t p)
{
    /* As senhas proibidas da especificacao (5.1.7.1). */
    static const uint32_t PROIBIDAS[] = {0, 11111111, 22222222, 33333333, 44444444, 55555555, 66666666, 77777777, 88888888, 99999999, 12345678, 87654321};
    if (comecou || p > 99999998) return false;
    for (uint32_t x : PROIBIDAS)
        if (p == x) return false;
    senhaDePareamento = p;
    return true;
}
bool ArduinoMatter::setBLECommissioningEnabled(bool enabled) { bleLigado = enabled; return !comecou; }
bool ArduinoMatter::setBLEMemoryReleaseEnabled(bool enabled) { liberarMemoriaBle = enabled; return !comecou; }
#if SOC_WIFI_SUPPORTED
bool ArduinoMatter::isWiFiStationEnabled() { return true; }
#else
bool ArduinoMatter::isWiFiStationEnabled() { return false; }
#endif
bool ArduinoMatter::isWiFiAccessPointEnabled() { return false; }
#if SOC_IEEE802154_SUPPORTED
bool ArduinoMatter::isThreadEnabled() { return true; }
#else
bool ArduinoMatter::isThreadEnabled() { return false; }
#endif
bool ArduinoMatter::isEthernetEnabled() { return false; }
bool ArduinoMatter::isBLECommissioningEnabled() { return bleLigado; }
bool ArduinoMatter::isBLEMemoryReleaseEnabled() { return bleLigado && liberarMemoriaBle; }
bool ArduinoMatter::isNetworkSupported(matterNetwork_t network)
{
    switch (network) {
    case MATTER_NETWORK_WIFI: return isWiFiStationEnabled();
    case MATTER_NETWORK_THREAD: return isThreadEnabled();
    case MATTER_NETWORK_ETHERNET: return isEthernetEnabled();
    default: return false;
    }
}
bool ArduinoMatter::selectNetwork(matterNetwork_t network) { return selectNetwork(network, false); }
bool ArduinoMatter::selectNetwork(matterNetwork_t network, bool disableBLECommissioning)
{
    if (!isNetworkSupported(network)) return false;
    redeEscolhida = network;
    if (disableBLECommissioning) bleLigado = false;
    return true;
}
matterNetwork_t ArduinoMatter::getSelectedNetwork() { return redeEscolhida; }
matterNetwork_t ArduinoMatter::getActiveNetwork() { return isDeviceConnected() ? (redeEscolhida != MATTER_NETWORK_NONE ? redeEscolhida : MATTER_NETWORK_WIFI) : MATTER_NETWORK_NONE; }
uint16_t ArduinoMatter::getNetworkEndPointId(matterNetwork_t network) { (void)network; return 0; }
bool ArduinoMatter::waitForNetwork(uint32_t timeoutMs)
{
    uint32_t inicio = millis();
    while (!isDeviceConnected() && millis() - inicio < timeoutMs) delay(50);
    return isDeviceConnected();
}
bool ArduinoMatter::isDeviceCommissioned() { return comissionada; }
#if SOC_WIFI_SUPPORTED
bool ArduinoMatter::isWiFiConnected() { return WiFi.status() == WL_CONNECTED; }
#else
bool ArduinoMatter::isWiFiConnected() { return false; }
#endif
bool ArduinoMatter::isThreadConnected() { return false; }
bool ArduinoMatter::isDeviceConnected() { return isWiFiConnected(); }
bool ArduinoMatter::isOnline() { return comissionada && isDeviceConnected(); }
void ArduinoMatter::decommission()
{
    cs_obj_n(nullptr, "__matter_descomissionar");
    comissionada = false;
}
