/* cs_rainmaker.cpp -- o ESP RainMaker no simulador: os cabecalhos e a biblioteca
 * do core (RMaker, RMakerNode, RMakerDevice, RMakerParam) sao os de verdade, e
 * o esp_rmaker por baixo mora aqui.
 *
 * O no, os dispositivos e os parametros funcionam como na placa. A nuvem da
 * Espressif e o aplicativo sao o computador da bancada (motor/RainMaker.cs):
 * com a placa na rede, o "MQTT" conecta e os eventos saem na ordem da placa; o
 * que o sketch relata (updateAndReport) fica registrado; e o que o "aplicativo"
 * escreve num parametro chega no callback do sketch, com a origem CLOUD. */
#include "Arduino.h"
#if defined(ESP32)
#include "sdkconfig.h"
#ifdef CONFIG_ESP_RMAKER_WORK_QUEUE_TASK_STACK
#include "esp_rmaker_core.h"
#include "esp_rmaker_ota.h"
#include "esp_rmaker_scenes.h"
#include "esp_rmaker_schedule.h"
#include "esp_rmaker_common_events.h"
#include "esp_rmaker_utils.h"
#include "esp_event.h"
#include "esp_system.h"
#include "qrcode.h"
#include "cs_objeto.h"
#include <string>
#include <vector>
#include <map>

ESP_EVENT_DEFINE_BASE(RMAKER_EVENT);
ESP_EVENT_DEFINE_BASE(RMAKER_COMMON_EVENT);
ESP_EVENT_DEFINE_BASE(RMAKER_OTA_EVENT);

/* O certificado do servidor de OTA da Espressif: o OTA do RainMaker nao sai da placa aqui. */
const char *ESP_RMAKER_OTA_DEFAULT_SERVER_CERT = "";

/* O RMaker.cpp liga o WiFi "por baixo" antes do no; a rede do simulador ja esta pronta. */
bool wifiLowLevelInit(bool persistent)
{
    (void)persistent;
    return true;
}

namespace {

struct Parametro {
    std::string nome, tipo, tipoDeUi;
    esp_rmaker_param_val_t valor = {};
    uint8_t propriedades = 0;
    bool temLimites = false;
    esp_rmaker_param_val_t minimo = {}, maximo = {}, passo = {};
    std::vector<std::string> validos;
    struct Dispositivo *dono = nullptr;
    bool pendente = false;
};

struct Dispositivo {
    std::string nome, tipo, subtipo, modelo;
    void *dados = nullptr;
    bool servico = false;
    esp_rmaker_device_write_cb_t aoEscrever = nullptr;
    esp_rmaker_device_read_cb_t aoLer = nullptr;
    esp_rmaker_device_bulk_write_cb_t aoEscreverVarios = nullptr;
    std::vector<Parametro *> parametros;
    Parametro *primario = nullptr;
    std::map<std::string, std::string> atributos;
};

struct No {
    esp_rmaker_node_info_t info = {};
    std::vector<Dispositivo *> dispositivos;
    std::map<std::string, std::string> atributos;
};

No *noDaPlaca = nullptr;
esp_rmaker_state_t estado = ESP_RMAKER_STATE_DEINIT;
TaskHandle_t tarefa = nullptr;
char idDoNo[13];
volatile bool conectou = false;

Parametro *P(const esp_rmaker_param_t *p) { return (Parametro *)p; }
Dispositivo *D(const esp_rmaker_device_t *d) { return (Dispositivo *)d; }

char *copia(const char *s) { return s ? strdup(s) : nullptr; }

esp_rmaker_param_val_t copiaDoValor(esp_rmaker_param_val_t v)
{
    if ((v.type == RMAKER_VAL_TYPE_STRING || v.type == RMAKER_VAL_TYPE_OBJECT || v.type == RMAKER_VAL_TYPE_ARRAY) && v.val.s) v.val.s = strdup(v.val.s);
    return v;
}
void soltarValor(esp_rmaker_param_val_t &v)
{
    if (v.type == RMAKER_VAL_TYPE_STRING || v.type == RMAKER_VAL_TYPE_OBJECT || v.type == RMAKER_VAL_TYPE_ARRAY) free(v.val.s);
    v.val.s = nullptr;
}

String emTexto(const esp_rmaker_param_val_t &v)
{
    switch (v.type) {
    case RMAKER_VAL_TYPE_BOOLEAN: return v.val.b ? "true" : "false";
    case RMAKER_VAL_TYPE_INTEGER: return String(v.val.i);
    case RMAKER_VAL_TYPE_FLOAT: return String(v.val.f, 3);
    case RMAKER_VAL_TYPE_STRING:
    case RMAKER_VAL_TYPE_OBJECT:
    case RMAKER_VAL_TYPE_ARRAY: return v.val.s ? v.val.s : "";
    default: return "";
    }
}

/* O texto que o "aplicativo" mandou, no tipo do parametro. */
esp_rmaker_param_val_t doTexto(const Parametro *p, const String &t)
{
    esp_rmaker_param_val_t v = {};
    v.type = p->valor.type;
    switch (v.type) {
    case RMAKER_VAL_TYPE_BOOLEAN: v.val.b = t == "true" || t == "1"; break;
    case RMAKER_VAL_TYPE_INTEGER: v.val.i = t.toInt(); break;
    case RMAKER_VAL_TYPE_FLOAT: v.val.f = t.toFloat(); break;
    default: v.val.s = strdup(t.c_str()); break;
    }
    return v;
}

void relatar(const Dispositivo *d, const Parametro *p)
{
    cs_obj_n(nullptr, "__rmaker_relatar", d ? d->nome.c_str() : "", p->nome.c_str(), emTexto(p->valor));
}

/* O que o aplicativo escreveu ("dispositivo|parametro|valor"): o callback do sketch recebe, como na nuvem. */
void entregarEscrita(const String &pedido)
{
    int a = pedido.indexOf('|'), b = pedido.indexOf('|', a + 1);
    if (a < 0 || b < 0 || !noDaPlaca) return;
    String nomeD = pedido.substring(0, a), nomeP = pedido.substring(a + 1, b), valor = pedido.substring(b + 1);
    for (Dispositivo *d : noDaPlaca->dispositivos) {
        if (nomeD != d->nome.c_str()) continue;
        for (Parametro *p : d->parametros) {
            if (nomeP != p->nome.c_str() || !(p->propriedades & PROP_FLAG_WRITE)) continue;
            esp_rmaker_param_val_t v = doTexto(p, valor);
            esp_rmaker_write_ctx_t ctx = {ESP_RMAKER_REQ_SRC_CLOUD};
            if (d->aoEscrever) {
                d->aoEscrever((const esp_rmaker_device_t *)d, (const esp_rmaker_param_t *)p, v, d->dados, &ctx);
            } else if (d->aoEscreverVarios) {
                esp_rmaker_param_write_req_t pedidoDoCore = {(esp_rmaker_param_t *)p, v};
                d->aoEscreverVarios((const esp_rmaker_device_t *)d, &pedidoDoCore, 1, d->dados, &ctx);
            }
            soltarValor(v);
            return;
        }
    }
}

void tarefaDaNuvem(void *)
{
    for (;;) {
        if (!conectou && cs_obj_i(nullptr, "__rmaker_rede") != 0) {
            conectou = true;
            esp_event_post(RMAKER_COMMON_EVENT, RMAKER_MQTT_EVENT_CONNECTED, nullptr, 0, 0);
            esp_event_post(RMAKER_EVENT, RMAKER_EVENT_CONFIG_REPORTED, nullptr, 0, 0);
            estado = ESP_RMAKER_STATE_STARTED;
            esp_event_post(RMAKER_EVENT, RMAKER_EVENT_STARTED, nullptr, 0, 0);
            cs_obj_n(nullptr, "__rmaker_conectou");
            if (noDaPlaca)
                for (Dispositivo *d : noDaPlaca->dispositivos)
                    for (Parametro *p : d->parametros) relatar(d, p);
        }
        if (conectou) {
            String pedido = cs_obj_t(nullptr, "__rmaker_proxima");
            if (pedido.length()) {
                entregarEscrita(pedido);
                continue;
            }
        }
        delay(20);
    }
}

} /* namespace */

extern "C" {

/* ---- os valores ---- */
esp_rmaker_param_val_t esp_rmaker_bool(bool bval)
{
    esp_rmaker_param_val_t v = {};
    v.type = RMAKER_VAL_TYPE_BOOLEAN;
    v.val.b = bval;
    return v;
}
esp_rmaker_param_val_t esp_rmaker_int(int ival)
{
    esp_rmaker_param_val_t v = {};
    v.type = RMAKER_VAL_TYPE_INTEGER;
    v.val.i = ival;
    return v;
}
esp_rmaker_param_val_t esp_rmaker_float(float fval)
{
    esp_rmaker_param_val_t v = {};
    v.type = RMAKER_VAL_TYPE_FLOAT;
    v.val.f = fval;
    return v;
}
static esp_rmaker_param_val_t comTexto(esp_rmaker_val_type_t tipo, const char *s)
{
    esp_rmaker_param_val_t v = {};
    v.type = tipo;
    v.val.s = (char *)s;
    return v;
}
esp_rmaker_param_val_t esp_rmaker_str(const char *sval) { return comTexto(RMAKER_VAL_TYPE_STRING, sval); }
esp_rmaker_param_val_t esp_rmaker_obj(const char *val) { return comTexto(RMAKER_VAL_TYPE_OBJECT, val); }
esp_rmaker_param_val_t esp_rmaker_array(const char *val) { return comTexto(RMAKER_VAL_TYPE_ARRAY, val); }

const char *esp_rmaker_device_cb_src_to_str(esp_rmaker_req_src_t src)
{
    switch (src) {
    case ESP_RMAKER_REQ_SRC_INIT: return "Init";
    case ESP_RMAKER_REQ_SRC_CLOUD: return "Cloud";
    case ESP_RMAKER_REQ_SRC_SCHEDULE: return "Schedule";
    case ESP_RMAKER_REQ_SRC_SCENE_ACTIVATE: return "Scene Activate";
    case ESP_RMAKER_REQ_SRC_SCENE_DEACTIVATE: return "Scene Deactivate";
    case ESP_RMAKER_REQ_SRC_LOCAL: return "Local";
    case ESP_RMAKER_REQ_SRC_CMD_RESP: return "Command Response";
    case ESP_RMAKER_REQ_SRC_FIRMWARE: return "Firmware";
    case ESP_RMAKER_REQ_SRC_BLE_LOCAL: return "BLE Local";
    default: return "Invalid";
    }
}

/* ---- o no ---- */
esp_rmaker_node_t *esp_rmaker_node_init(const esp_rmaker_config_t *config, const char *name, const char *type)
{
    (void)config;
    if (noDaPlaca || !name || !type) return nullptr;
    esp_event_loop_create_default();
    uint64_t mac = ESP.getEfuseMac();
    snprintf(idDoNo, sizeof(idDoNo), "%012llX", (unsigned long long)mac);
    noDaPlaca = new No();
    noDaPlaca->info.name = copia(name);
    noDaPlaca->info.type = copia(type);
    noDaPlaca->info.fw_version = copia("1.0");
    noDaPlaca->info.model = copia("esp-rainmaker");
    estado = ESP_RMAKER_STATE_INIT_DONE;
    esp_event_post(RMAKER_EVENT, RMAKER_EVENT_INIT_DONE, nullptr, 0, 0);
    return (esp_rmaker_node_t *)noDaPlaca;
}

esp_err_t esp_rmaker_start(void)
{
    if (!noDaPlaca) return ESP_ERR_INVALID_STATE;
    if (tarefa) return ESP_OK;
    estado = ESP_RMAKER_STATE_STARTING;
    return xTaskCreate(tarefaDaNuvem, "esp_rmaker", 4096, nullptr, 5, &tarefa) == pdPASS ? ESP_OK : ESP_FAIL;
}

esp_err_t esp_rmaker_stop(void)
{
    if (tarefa) {
        vTaskDelete(tarefa);
        tarefa = nullptr;
    }
    if (conectou) esp_event_post(RMAKER_COMMON_EVENT, RMAKER_MQTT_EVENT_DISCONNECTED, nullptr, 0, 0);
    conectou = false;
    if (noDaPlaca) estado = ESP_RMAKER_STATE_INIT_DONE;
    return ESP_OK;
}

esp_err_t esp_rmaker_node_deinit(const esp_rmaker_node_t *node)
{
    if (!node || (No *)node != noDaPlaca) return ESP_ERR_INVALID_ARG;
    if (tarefa) return ESP_ERR_INVALID_STATE;
    delete noDaPlaca;
    noDaPlaca = nullptr;
    estado = ESP_RMAKER_STATE_DEINIT;
    return ESP_OK;
}

esp_rmaker_state_t esp_rmaker_get_state(void) { return estado; }
const esp_rmaker_node_t *esp_rmaker_get_node(void) { return (const esp_rmaker_node_t *)noDaPlaca; }
char *esp_rmaker_get_node_id(void) { return noDaPlaca ? idDoNo : nullptr; }
esp_rmaker_node_info_t *esp_rmaker_node_get_info(const esp_rmaker_node_t *node) { return node ? &((No *)node)->info : nullptr; }

esp_err_t esp_rmaker_node_add_attribute(const esp_rmaker_node_t *node, const char *attr_name, const char *val)
{
    if (!node || !attr_name || !val) return ESP_ERR_INVALID_ARG;
    auto &a = ((No *)node)->atributos;
    if (a.count(attr_name)) return ESP_FAIL;
    a[attr_name] = val;
    return ESP_OK;
}
esp_err_t esp_rmaker_node_edit_attribute(const esp_rmaker_node_t *node, const char *attr_name, const char *val)
{
    if (!node || !attr_name || !val) return ESP_ERR_INVALID_ARG;
    auto &a = ((No *)node)->atributos;
    if (!a.count(attr_name)) return ESP_ERR_NOT_FOUND;
    a[attr_name] = val;
    return ESP_OK;
}
static esp_err_t trocarInfo(const esp_rmaker_node_t *node, char *esp_rmaker_node_info_t::*campo, const char *val)
{
    if (!node || !val) return ESP_ERR_INVALID_ARG;
    esp_rmaker_node_info_t &i = ((No *)node)->info;
    free(i.*campo);
    i.*campo = strdup(val);
    return ESP_OK;
}
esp_err_t esp_rmaker_node_add_fw_version(const esp_rmaker_node_t *node, const char *v) { return trocarInfo(node, &esp_rmaker_node_info_t::fw_version, v); }
esp_err_t esp_rmaker_node_add_model(const esp_rmaker_node_t *node, const char *v) { return trocarInfo(node, &esp_rmaker_node_info_t::model, v); }
esp_err_t esp_rmaker_node_add_subtype(const esp_rmaker_node_t *node, const char *v) { return trocarInfo(node, &esp_rmaker_node_info_t::subtype, v); }
esp_err_t esp_rmaker_node_add_readme(const esp_rmaker_node_t *node, const char *v) { return trocarInfo(node, &esp_rmaker_node_info_t::readme, v); }

/* ---- os dispositivos ---- */
static esp_rmaker_device_t *novoDispositivo(const char *nome, const char *tipo, void *dados, bool servico)
{
    if (!nome) return nullptr;
    Dispositivo *d = new Dispositivo();
    d->nome = nome;
    d->tipo = tipo ? tipo : "";
    d->dados = dados;
    d->servico = servico;
    return (esp_rmaker_device_t *)d;
}
esp_rmaker_device_t *esp_rmaker_device_create(const char *dev_name, const char *type, void *priv_data) { return novoDispositivo(dev_name, type, priv_data, false); }
esp_rmaker_device_t *esp_rmaker_service_create(const char *serv_name, const char *type, void *priv_data) { return novoDispositivo(serv_name, type, priv_data, true); }

esp_err_t esp_rmaker_device_delete(const esp_rmaker_device_t *device)
{
    if (!device) return ESP_ERR_INVALID_ARG;
    Dispositivo *d = D(device);
    for (Parametro *p : d->parametros) {
        soltarValor(p->valor);
        delete p;
    }
    delete d;
    return ESP_OK;
}
esp_err_t esp_rmaker_device_add_cb(const esp_rmaker_device_t *device, esp_rmaker_device_write_cb_t write_cb, esp_rmaker_device_read_cb_t read_cb)
{
    if (!device) return ESP_ERR_INVALID_ARG;
    D(device)->aoEscrever = write_cb;
    D(device)->aoLer = read_cb;
    return ESP_OK;
}
esp_err_t esp_rmaker_device_add_bulk_cb(const esp_rmaker_device_t *device, esp_rmaker_device_bulk_write_cb_t write_cb, esp_rmaker_device_bulk_read_cb_t read_cb)
{
    (void)read_cb;
    if (!device) return ESP_ERR_INVALID_ARG;
    D(device)->aoEscreverVarios = write_cb;
    return ESP_OK;
}
esp_err_t esp_rmaker_node_add_device(const esp_rmaker_node_t *node, const esp_rmaker_device_t *device)
{
    if (!node || !device) return ESP_ERR_INVALID_ARG;
    ((No *)node)->dispositivos.push_back(D(device));
    return ESP_OK;
}
esp_err_t esp_rmaker_node_remove_device(const esp_rmaker_node_t *node, const esp_rmaker_device_t *device)
{
    if (!node || !device) return ESP_ERR_INVALID_ARG;
    auto &lista = ((No *)node)->dispositivos;
    for (size_t i = 0; i < lista.size(); i++) {
        if (lista[i] == D(device)) {
            lista.erase(lista.begin() + (long)i);
            return ESP_OK;
        }
    }
    return ESP_ERR_NOT_FOUND;
}
esp_rmaker_device_t *esp_rmaker_node_get_device_by_name(const esp_rmaker_node_t *node, const char *device_name)
{
    if (!node || !device_name) return nullptr;
    for (Dispositivo *d : ((No *)node)->dispositivos)
        if (d->nome == device_name) return (esp_rmaker_device_t *)d;
    return nullptr;
}
esp_err_t esp_rmaker_device_add_attribute(const esp_rmaker_device_t *device, const char *attr_name, const char *val)
{
    if (!device || !attr_name || !val) return ESP_ERR_INVALID_ARG;
    D(device)->atributos[attr_name] = val;
    return ESP_OK;
}
esp_err_t esp_rmaker_device_add_subtype(const esp_rmaker_device_t *device, const char *subtype)
{
    if (!device || !subtype) return ESP_ERR_INVALID_ARG;
    D(device)->subtipo = subtype;
    return ESP_OK;
}
esp_err_t esp_rmaker_device_add_model(const esp_rmaker_device_t *device, const char *model)
{
    if (!device || !model) return ESP_ERR_INVALID_ARG;
    D(device)->modelo = model;
    return ESP_OK;
}
char *esp_rmaker_device_get_name(const esp_rmaker_device_t *device) { return device ? (char *)D(device)->nome.c_str() : nullptr; }
void *esp_rmaker_device_get_priv_data(const esp_rmaker_device_t *device) { return device ? D(device)->dados : nullptr; }
char *esp_rmaker_device_get_type(const esp_rmaker_device_t *device) { return device ? (char *)D(device)->tipo.c_str() : nullptr; }
esp_err_t esp_rmaker_device_add_param(const esp_rmaker_device_t *device, const esp_rmaker_param_t *param)
{
    if (!device || !param) return ESP_ERR_INVALID_ARG;
    for (Parametro *p : D(device)->parametros)
        if (p->nome == P(param)->nome) return ESP_FAIL;
    P(param)->dono = D(device);
    D(device)->parametros.push_back(P(param));
    return ESP_OK;
}
esp_rmaker_param_t *esp_rmaker_device_get_param_by_type(const esp_rmaker_device_t *device, const char *param_type)
{
    if (!device || !param_type) return nullptr;
    for (Parametro *p : D(device)->parametros)
        if (p->tipo == param_type) return (esp_rmaker_param_t *)p;
    return nullptr;
}
esp_rmaker_param_t *esp_rmaker_device_get_param_by_name(const esp_rmaker_device_t *device, const char *param_name)
{
    if (!device || !param_name) return nullptr;
    for (Parametro *p : D(device)->parametros)
        if (p->nome == param_name) return (esp_rmaker_param_t *)p;
    return nullptr;
}
esp_err_t esp_rmaker_device_assign_primary_param(const esp_rmaker_device_t *device, const esp_rmaker_param_t *param)
{
    if (!device || !param) return ESP_ERR_INVALID_ARG;
    D(device)->primario = P(param);
    return ESP_OK;
}

/* ---- os parametros ---- */
esp_rmaker_param_t *esp_rmaker_param_create(const char *param_name, const char *type, esp_rmaker_param_val_t val, uint8_t properties)
{
    if (!param_name) return nullptr;
    Parametro *p = new Parametro();
    p->nome = param_name;
    p->tipo = type ? type : "";
    p->valor = copiaDoValor(val);
    p->propriedades = properties;
    return (esp_rmaker_param_t *)p;
}
esp_err_t esp_rmaker_param_add_ui_type(const esp_rmaker_param_t *param, const char *ui_type)
{
    if (!param || !ui_type) return ESP_ERR_INVALID_ARG;
    P(param)->tipoDeUi = ui_type;
    return ESP_OK;
}
esp_err_t esp_rmaker_param_add_bounds(const esp_rmaker_param_t *param, esp_rmaker_param_val_t min, esp_rmaker_param_val_t max, esp_rmaker_param_val_t step)
{
    if (!param) return ESP_ERR_INVALID_ARG;
    if (P(param)->valor.type != min.type || P(param)->valor.type != max.type || P(param)->valor.type != step.type) return ESP_ERR_INVALID_ARG;
    P(param)->temLimites = true;
    P(param)->minimo = min;
    P(param)->maximo = max;
    P(param)->passo = step;
    return ESP_OK;
}
esp_err_t esp_rmaker_param_add_simple_time_series_ttl(const esp_rmaker_param_t *param, uint16_t ttl_days)
{
    (void)ttl_days;
    return param ? ESP_OK : ESP_ERR_INVALID_ARG;
}
esp_err_t esp_rmaker_param_add_valid_str_list(const esp_rmaker_param_t *param, const char *strs[], uint8_t count)
{
    if (!param) return ESP_ERR_INVALID_ARG;
    P(param)->validos.clear();
    for (uint8_t i = 0; i < count; i++) P(param)->validos.push_back(strs[i]);
    return ESP_OK;
}
esp_err_t esp_rmaker_param_add_array_max_count(const esp_rmaker_param_t *param, int count)
{
    (void)count;
    return param ? ESP_OK : ESP_ERR_INVALID_ARG;
}
esp_err_t esp_rmaker_param_update(const esp_rmaker_param_t *param, esp_rmaker_param_val_t val)
{
    if (!param) return ESP_ERR_INVALID_ARG;
    if (P(param)->valor.type != val.type) return ESP_ERR_INVALID_ARG;
    soltarValor(P(param)->valor);
    P(param)->valor = copiaDoValor(val);
    P(param)->pendente = true;
    return ESP_OK;
}
esp_err_t esp_rmaker_report_updated_params(void)
{
    if (!noDaPlaca) return ESP_ERR_INVALID_STATE;
    for (Dispositivo *d : noDaPlaca->dispositivos) {
        for (Parametro *p : d->parametros) {
            if (!p->pendente) continue;
            p->pendente = false;
            relatar(d, p);
        }
    }
    return ESP_OK;
}
esp_err_t esp_rmaker_param_update_and_report(const esp_rmaker_param_t *param, esp_rmaker_param_val_t val)
{
    esp_err_t e = esp_rmaker_param_update(param, val);
    if (e != ESP_OK) return e;
    P(param)->pendente = false;
    relatar(P(param)->dono, P(param));
    return ESP_OK;
}
esp_err_t esp_rmaker_param_update_and_notify(const esp_rmaker_param_t *param, esp_rmaker_param_val_t val)
{
    esp_err_t e = esp_rmaker_param_update_and_report(param, val);
    if (e == ESP_OK) cs_obj_n(nullptr, "__rmaker_aviso", String("notificacao: ") + P(param)->nome.c_str() + " = " + emTexto(P(param)->valor));
    return e;
}
esp_err_t esp_rmaker_raise_alert(const char *alert_str)
{
    if (!alert_str) return ESP_ERR_INVALID_ARG;
    cs_obj_n(nullptr, "__rmaker_aviso", String("alerta: ") + alert_str);
    return ESP_OK;
}
char *esp_rmaker_param_get_name(const esp_rmaker_param_t *param) { return param ? (char *)P(param)->nome.c_str() : nullptr; }
char *esp_rmaker_param_get_type(const esp_rmaker_param_t *param) { return param ? (char *)P(param)->tipo.c_str() : nullptr; }
esp_rmaker_param_val_t *esp_rmaker_param_get_val(esp_rmaker_param_t *param) { return param ? &P(param)->valor : nullptr; }
esp_err_t esp_rmaker_report_node_details(void) { return noDaPlaca ? ESP_OK : ESP_ERR_INVALID_STATE; }

/* ---- os servicos: pedir para ligar vale; a nuvem que os usa nao existe aqui ---- */
esp_err_t esp_rmaker_timezone_service_enable(void) { return noDaPlaca ? ESP_OK : ESP_ERR_INVALID_STATE; }
esp_err_t esp_rmaker_system_service_enable(esp_rmaker_system_serv_config_t *config) { return noDaPlaca && config ? ESP_OK : ESP_ERR_INVALID_STATE; }
bool esp_rmaker_local_ctrl_service_started(void) { return false; }
esp_err_t esp_rmaker_ota_enable_default(void) { return noDaPlaca ? ESP_OK : ESP_ERR_INVALID_STATE; }
esp_err_t esp_rmaker_ota_enable(esp_rmaker_ota_config_t *ota_config, esp_rmaker_ota_type_t type)
{
    (void)ota_config;
    (void)type;
    return noDaPlaca ? ESP_OK : ESP_ERR_INVALID_STATE;
}
esp_err_t esp_rmaker_schedule_enable(void) { return noDaPlaca ? ESP_OK : ESP_ERR_INVALID_STATE; }
esp_err_t esp_rmaker_scenes_enable(void) { return noDaPlaca ? ESP_OK : ESP_ERR_INVALID_STATE; }

esp_err_t esp_rmaker_time_set_timezone(const char *tz)
{
    if (!tz) return ESP_ERR_INVALID_ARG;
    esp_event_post(RMAKER_COMMON_EVENT, RMAKER_EVENT_TZ_CHANGED, (void *)tz, strlen(tz) + 1, 0);
    return ESP_OK;
}
esp_err_t esp_rmaker_time_set_timezone_posix(const char *tz_posix)
{
    if (!tz_posix) return ESP_ERR_INVALID_ARG;
    esp_event_post(RMAKER_COMMON_EVENT, RMAKER_EVENT_TZ_POSIX_CHANGED, (void *)tz_posix, strlen(tz_posix) + 1, 0);
    return ESP_OK;
}

/* ---- o sistema (esp_rmaker_system_ctrl.h): o reinicio e o da placa simulada ---- */
esp_err_t esp_rmaker_reboot(int8_t seconds)
{
    esp_event_post(RMAKER_COMMON_EVENT, RMAKER_EVENT_REBOOT, &seconds, sizeof(seconds), 0);
    delay((uint32_t)seconds * 1000);
    esp_restart();
    return ESP_OK;
}
esp_err_t esp_rmaker_wifi_reset(int8_t reset_seconds, int8_t reboot_seconds)
{
    esp_event_post(RMAKER_COMMON_EVENT, RMAKER_EVENT_WIFI_RESET, nullptr, 0, 0);
    delay((uint32_t)reset_seconds * 1000);
    return reboot_seconds >= 0 ? esp_rmaker_reboot(reboot_seconds) : ESP_OK;
}
esp_err_t esp_rmaker_factory_reset(int8_t reset_seconds, int8_t reboot_seconds)
{
    esp_event_post(RMAKER_COMMON_EVENT, RMAKER_EVENT_FACTORY_RESET, nullptr, 0, 0);
    delay((uint32_t)reset_seconds * 1000);
    return reboot_seconds >= 0 ? esp_rmaker_reboot(reboot_seconds) : ESP_OK;
}

/* ---- o QR code do provisionamento: o texto vale, sem desenhar o QR no monitor ---- */
void esp_qrcode_print_console(esp_qrcode_handle_t qrcode) { (void)qrcode; }
esp_err_t esp_qrcode_generate(esp_qrcode_config_t *cfg, const char *text)
{
    (void)cfg;
    (void)text;
    return ESP_OK;
}

} /* extern "C" */

#endif
#endif
