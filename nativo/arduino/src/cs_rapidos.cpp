/* cs_rapidos.cpp -- as bibliotecas do core que dependem de hardware ou de um
 * servico que o simulador nao tem. Os cabecalhos sao os de verdade, e cada uma
 * se comporta como a placa nessa situacao:
 *
 * - PPP: nenhum modem no circuito -- o AT nao responde e o begin() falha.
 * - Insights: sem a nuvem da Espressif -- as metricas e variaveis funcionam
 *   na placa (registrar, gravar, remover), e o send() so diz que foi.
 * - ESP_SR: os modelos de voz sao do chip, e o simulador nao os executa --
 *   o sr_start() responde que nao da, e o ESP_SR.begin() devolve false.
 * - WiFiProv: o aplicativo do celular nao existe na bancada -- o ponto de
 *   acesso (ou o BLE) abre e os eventos saem, e a placa fica esperando as
 *   credenciais, como a placa sem ninguem do outro lado. */
#include "Arduino.h"
#if defined(ESP32)
#include "sdkconfig.h"
#include "soc/soc_caps.h"
#include <map>

/* =====================================================================
 * o PPP */
#include "PPP.h"
#if CONFIG_LWIP_PPP_SUPPORT && ARDUINO_HAS_ESP_MODEM
PPPClass PPP;

PPPClass::PPPClass()
    : _dce(NULL), _pin_tx(-1), _pin_rx(-1), _pin_rts(-1), _pin_cts(-1), _flow_ctrl(ESP_MODEM_FLOW_CONTROL_NONE), _pin_rst(-1), _pin_rst_act_low(true),
      _pin_rst_delay(200), _boot_delay(100), _sendAtBurst(false), _pin(NULL), _apn(NULL), _rx_buffer_size(4096), _tx_buffer_size(512),
      _mode(ESP_MODEM_MODE_COMMAND), _uart_num(1), _ppp_event_handle(0)
{
    _interface_id = ESP_NETIF_ID_PPP;
}
PPPClass::~PPPClass() {}

bool PPPClass::begin(ppp_modem_model_t model, uint8_t uart_num, int baud_rate)
{
    (void)model;
    (void)baud_rate;
    _uart_num = uart_num;
    if (_pin_tx < 0 || _pin_rx < 0) {
        log_e("set the UART pins first");
        return false;
    }
    log_e("Failed to sync: o modem nao respondeu ao AT (o simulador nao tem um modem no circuito)");
    return false;
}
void PPPClass::end() {}
bool PPPClass::setApn(const char *apn)
{
    _apn = apn;
    return true;
}
bool PPPClass::setPin(const char *pin)
{
    _pin = pin;
    return true;
}
bool PPPClass::setPins(int8_t tx, int8_t rx, int8_t rts, int8_t cts, esp_modem_flow_ctrl_t flow_ctrl)
{
    _pin_tx = tx;
    _pin_rx = rx;
    _pin_rts = rts;
    _pin_cts = cts;
    _flow_ctrl = flow_ctrl;
    return true;
}
void PPPClass::setResetPin(int8_t rst, bool active_low, uint32_t reset_delay, uint32_t boot_delay)
{
    _pin_rst = rst;
    _pin_rst_act_low = active_low;
    _pin_rst_delay = reset_delay;
    _boot_delay = boot_delay;
}
void PPPClass::sendAtBurst(bool en) { _sendAtBurst = en; }
int PPPClass::RSSI() const { return -1; }
int PPPClass::BER() const { return -1; }
String PPPClass::IMSI() const { return String(); }
String PPPClass::IMEI() const { return String(); }
String PPPClass::moduleName() const { return String(); }
String PPPClass::operatorName() const { return String(); }
int PPPClass::networkMode() const { return -1; }
int PPPClass::radioState() const { return -1; }
bool PPPClass::attached() const { return false; }
bool PPPClass::sync() const { return false; }
int PPPClass::batteryVoltage() const { return -1; }
int PPPClass::batteryLevel() const { return -1; }
int PPPClass::batteryStatus() const { return -1; }
bool PPPClass::mode(esp_modem_dce_mode_t m)
{
    (void)m;
    return false;
}
bool PPPClass::setBaudrate(int baudrate)
{
    (void)baudrate;
    return false;
}
bool PPPClass::sms(const char *num, const char *message)
{
    (void)num;
    (void)message;
    return false;
}
String PPPClass::cmd(const char *at_command, int timeout)
{
    (void)at_command;
    (void)timeout;
    return String();
}
bool PPPClass::cmd(const char *at_command, String &response, int timeout)
{
    (void)at_command;
    (void)response;
    (void)timeout;
    return false;
}
bool PPPClass::powerDown() { return false; }
bool PPPClass::reset() { return false; }
bool PPPClass::storeProfile() { return false; }
esp_modem_dce_t *PPPClass::handle() const { return _dce; }
size_t PPPClass::printDriverInfo(Print &out) const { return out.print(",NO_MODEM"); }
void PPPClass::_onPppEvent(int32_t event_id, void *event_data)
{
    (void)event_id;
    (void)event_data;
}
void PPPClass::_onPppArduinoEvent(arduino_event_id_t event, arduino_event_info_t info)
{
    (void)event;
    (void)info;
}
#endif

/* =====================================================================
 * o Insights */
#include "Insights.h"
#include "AppInsights.h"
#ifdef CONFIG_ESP_INSIGHTS_ENABLED
namespace {
/* As chaves registradas (o tipo de cada uma) e o ultimo valor, em texto. */
struct ChaveDoInsights {
    char tipo;
    String valor;
};
std::map<String, ChaveDoInsights> metricasDoInsights, variaveisDoInsights;
char idDoNo[13];
} /* namespace */

/* Depois dos mapas: o destrutor do Insights ainda limpa eles. */
ESPInsightsClass Insights;

namespace {
bool adicionar(std::map<String, ChaveDoInsights> &m, bool ligado, const char *key, char tipo)
{
    if (!ligado || !key || m.count(key)) return false;
    m[key] = {tipo, String()};
    return true;
}
bool gravar(std::map<String, ChaveDoInsights> &m, bool ligado, const char *key, char tipo, const String &valor)
{
    if (!ligado || !key) return false;
    auto c = m.find(key);
    if (c == m.end() || c->second.tipo != tipo) return false;
    c->second.valor = valor;
    return true;
}
String ipEmTexto(uint32_t ip) { return IPAddress(ip).toString(); }
String macEmTexto(const uint8_t *mac)
{
    char t[18];
    snprintf(t, sizeof(t), "%02x:%02x:%02x:%02x:%02x:%02x", mac[0], mac[1], mac[2], mac[3], mac[4], mac[5]);
    return t;
}
} /* namespace */

#define CS_INSIGHTS_CLASSE(Classe, mapa)                                                                                                             \
    bool Classe::addBool(const char *tag, const char *key, const char *label, const char *path) { return adicionar(mapa, initialized, key, 'b'); }   \
    bool Classe::addInt(const char *tag, const char *key, const char *label, const char *path) { return adicionar(mapa, initialized, key, 'i'); }    \
    bool Classe::addUint(const char *tag, const char *key, const char *label, const char *path) { return adicionar(mapa, initialized, key, 'u'); }   \
    bool Classe::addFloat(const char *tag, const char *key, const char *label, const char *path) { return adicionar(mapa, initialized, key, 'f'); }  \
    bool Classe::addString(const char *tag, const char *key, const char *label, const char *path) { return adicionar(mapa, initialized, key, 's'); } \
    bool Classe::addIPv4(const char *tag, const char *key, const char *label, const char *path) { return adicionar(mapa, initialized, key, '4'); }   \
    bool Classe::addMAC(const char *tag, const char *key, const char *label, const char *path) { return adicionar(mapa, initialized, key, 'm'); }    \
    bool Classe::setBool(const char *key, bool b) { return gravar(mapa, initialized, key, 'b', b ? "true" : "false"); }                              \
    bool Classe::setInt(const char *key, int32_t i) { return gravar(mapa, initialized, key, 'i', String((long)i)); }                                 \
    bool Classe::setUint(const char *key, uint32_t u) { return gravar(mapa, initialized, key, 'u', String((unsigned long)u)); }                      \
    bool Classe::setFloat(const char *key, float f) { return gravar(mapa, initialized, key, 'f', String(f)); }                                       \
    bool Classe::setString(const char *key, const char *str) { return gravar(mapa, initialized, key, 's', str ? str : ""); }                         \
    bool Classe::setIPv4(const char *key, uint32_t ip) { return gravar(mapa, initialized, key, '4', ipEmTexto(ip)); }                                \
    bool Classe::setMAC(const char *key, uint8_t *mac) { return gravar(mapa, initialized, key, 'm', macEmTexto(mac)); }                              \
    bool Classe::remove(const char *key) { return initialized && key && mapa.erase(key) > 0; }                                                       \
    bool Classe::removeAll()                                                                                                                         \
    {                                                                                                                                                \
        if (!initialized) return false;                                                                                                              \
        mapa.clear();                                                                                                                                \
        return true;                                                                                                                                 \
    }

CS_INSIGHTS_CLASSE(ESPInsightsMetricsClass, metricasDoInsights)
CS_INSIGHTS_CLASSE(ESPInsightsVariablesClass, variaveisDoInsights)

void ESPInsightsMetricsClass::setHeapPeriod(uint32_t seconds) { (void)seconds; }
void ESPInsightsMetricsClass::setWiFiPeriod(uint32_t seconds) { (void)seconds; }
bool ESPInsightsMetricsClass::dumpHeap()
{
    if (!initialized) return false;
    log_printf("[Insights] heap livre: %u bytes\n", (unsigned)ESP.getFreeHeap());
    return true;
}
bool ESPInsightsMetricsClass::dumpWiFi() { return initialized; }

ESPInsightsClass::ESPInsightsClass() : initialized(false) {}
ESPInsightsClass::~ESPInsightsClass() { end(); }

bool ESPInsightsClass::begin(const char *auth_key, const char *node_id, uint32_t log_type, bool alloc_ext_ram, bool use_default_transport)
{
    (void)log_type;
    (void)alloc_ext_ram;
    if (initialized) return false;
    /* Sem a chave do painel da Espressif, o esp_insights_init falha. */
    if (use_default_transport && (!auth_key || !auth_key[0])) {
        log_e("Failed to initialize ESP Insights: auth_key");
        return false;
    }
    if (node_id && node_id[0]) {
        snprintf(idDoNo, sizeof(idDoNo), "%s", node_id);
    } else {
        uint64_t mac = ESP.getEfuseMac();
        snprintf(idDoNo, sizeof(idDoNo), "%012llX", (unsigned long long)mac);
    }
    initialized = true;
    metrics.setInitialized(true);
    variables.setInitialized(true);
    return true;
}
void ESPInsightsClass::end()
{
    if (!initialized) return;
    metrics.removeAll();
    variables.removeAll();
    metrics.setInitialized(false);
    variables.setInitialized(false);
    initialized = false;
}
/* A nuvem nao existe no simulador: o pedido de envio vale, sem sair nada da placa. */
bool ESPInsightsClass::send() { return initialized; }
const char *ESPInsightsClass::nodeID() { return initialized ? idDoNo : ""; }
void ESPInsightsClass::dumpTasksStatus() {}
bool ESPInsightsClass::event(const char *tag, const char *format, ...)
{
    if (!initialized) return false;
    char texto[128];
    va_list args;
    va_start(args, format);
    vsnprintf(texto, sizeof(texto), format, args);
    va_end(args);
    log_printf("[Insights] %s: %s\n", tag, texto);
    return true;
}

bool initAppInsights(uint32_t log_type, bool alloc_ext_ram) { return Insights.begin(NULL, NULL, log_type, alloc_ext_ram, false); }
#endif

/* =====================================================================
 * o reconhecimento de voz (ESP_SR) */
#include "esp32-hal-sr.h"
#if (CONFIG_IDF_TARGET_ESP32S3 || CONFIG_IDF_TARGET_ESP32P4) && (CONFIG_MODEL_IN_FLASH || CONFIG_MODEL_IN_SDCARD)
esp_err_t sr_start(sr_fill_cb fill_cb, void *fill_cb_arg, sr_channels_t rx_chan, sr_mode_t mode, const char *input_format, const sr_cmd_t *sr_commands,
                   size_t cmd_number, sr_event_cb cb, void *cb_arg)
{
    (void)fill_cb;
    (void)fill_cb_arg;
    (void)rx_chan;
    (void)mode;
    (void)input_format;
    (void)sr_commands;
    (void)cmd_number;
    (void)cb;
    (void)cb_arg;
    log_e("ESP_SR: os modelos de voz (WakeNet, MultiNet) rodam no chip; o simulador nao os executa");
    return ESP_ERR_NOT_SUPPORTED;
}
esp_err_t sr_stop(void) { return ESP_OK; }
esp_err_t sr_pause(void) { return ESP_OK; }
esp_err_t sr_resume(void) { return ESP_OK; }
esp_err_t sr_set_mode(sr_mode_t mode)
{
    (void)mode;
    return ESP_FAIL;
}
#endif

/* =====================================================================
 * o provisionamento do WiFi (WiFiProv) */
#include "WiFiProv.h"
#if SOC_WIFI_SUPPORTED
WiFiProvClass WiFiProv;

static void cs_prov_evento(arduino_event_id_t id)
{
    arduino_event_t e = {};
    e.event_id = id;
    Network.postEvent(&e);
}

void WiFiProvClass::initProvision(prov_scheme_t prov_scheme, scheme_handler_t scheme_handler, bool reset_provisioned)
{
    (void)prov_scheme;
    (void)scheme_handler;
    if (provInitDone) return;
    provInitDone = true;
    Network.begin();
    if (reset_provisioned) provisioned = false;
    cs_prov_evento(ARDUINO_EVENT_PROV_INIT);
}

void WiFiProvClass::beginProvision(prov_scheme_t prov_scheme, scheme_handler_t scheme_handler, network_prov_security_t security, const char *pop,
                                   const char *service_name, const char *service_key, uint8_t *uuid, bool reset_provisioned)
{
    (void)security;
    (void)pop;
    (void)uuid;
    initProvision(prov_scheme, scheme_handler, reset_provisioned);
    /* O nome de fabrica: PROV_ e o fim do MAC. */
    char nome[12];
    if (!service_name) {
        uint8_t mac[6];
        WiFi.macAddress(mac);
        snprintf(nome, sizeof(nome), "PROV_%02X%02X%02X", mac[3], mac[4], mac[5]);
        service_name = nome;
    }
#if (defined(CONFIG_BLUEDROID_ENABLED) || defined(CONFIG_NIMBLE_ENABLED))
    if (prov_scheme == NETWORK_PROV_SCHEME_BLE) {
        cs_obj_n(nullptr, "BLEDevice::init", service_name);
        cs_obj_n(nullptr, "BLEDevice::startAdvertising");
    } else
#endif
    {
        WiFi.softAP(service_name, service_key);
    }
    log_printf("Provisioning started. Use the provisioning app to connect to \"%s\" (the simulator has no phone app: the board keeps waiting).\n", service_name);
    cs_prov_evento(ARDUINO_EVENT_PROV_START);
}

void WiFiProvClass::endProvision()
{
    if (!provInitDone) return;
    provInitDone = false;
    cs_prov_evento(ARDUINO_EVENT_PROV_END);
    cs_prov_evento(ARDUINO_EVENT_PROV_DEINIT);
}

bool WiFiProvClass::disableAutoStop(uint32_t cleanup_delay)
{
    (void)cleanup_delay;
    return provInitDone;
}

void WiFiProvClass::printQR(const char *name, const char *pop, const char *transport, Print &out)
{
    if (!name || !transport) {
        log_w("Cannot generate QR code payload. Data missing.");
        return;
    }
    char payload[150];
    if (pop) snprintf(payload, sizeof(payload), "{\"ver\":\"%s\",\"name\":\"%s\",\"pop\":\"%s\",\"transport\":\"%s\"}", "v1", name, pop, transport);
    else snprintf(payload, sizeof(payload), "{\"ver\":\"%s\",\"name\":\"%s\",\"transport\":\"%s\"}", "v1", name, transport);
    out.printf("If QR code is not visible, copy paste the below URL in a browser.\nhttps://espressif.github.io/esp-jumpstart/qrcode.html?data=%s\n", payload);
}
#endif

#endif
