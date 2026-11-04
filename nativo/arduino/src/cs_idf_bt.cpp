/* cs_idf_bt.cpp -- o Bluedroid do ESP-IDF (esp_gap_ble_api.h, esp_gatts_api.h,
 * esp_gattc_api.h, esp_spp_api.h) sobre o Bluetooth do simulador (cs_ble.h e
 * BluetoothSerial.h), as mesmas placas e os mesmos clientes da bancada. Os eventos
 * chegam nos callbacks do programa numa tarefa propria, na ordem da placa (a BTC
 * do Bluedroid), depois da chamada que os pediu. O NimBLE do IDF nao vem aqui. */
#include "Arduino.h"
#if defined(ESP32) && (SOC_BLE_SUPPORTED || SOC_BT_SUPPORTED)
#include "BLEDevice.h"
#include "BluetoothSerial.h"
#include "esp_bt.h"
#include "esp_bt_main.h"
#include "esp_bt_device.h"
#include "esp_gap_ble_api.h"
#include "esp_gatts_api.h"
#include "esp_gattc_api.h"
#include "freertos/queue.h"
#include "freertos/task.h"
#include <deque>
#include <functional>
#include <map>
#include <string>
#include <vector>

namespace {
/* ---- a tarefa dos eventos ---- */
std::deque<std::function<void()>> &filaBtc()
{
    static auto *f = new std::deque<std::function<void()>>();
    return *f;
}
SemaphoreHandle_t travaBtc = nullptr;
TaskHandle_t tarefaBtc = nullptr;

void rodarBtc(void *)
{
    for (;;) {
        std::function<void()> f;
        xSemaphoreTake(travaBtc, portMAX_DELAY);
        if (!filaBtc().empty()) {
            f = filaBtc().front();
            filaBtc().pop_front();
        }
        xSemaphoreGive(travaBtc);
        if (f) f();
        else vTaskDelay(1);
    }
}
void depois(std::function<void()> f)
{
    if (!travaBtc) {
        travaBtc = xSemaphoreCreateMutex();
        xTaskCreate(rodarBtc, "BTC_TASK", 8192, nullptr, 19, &tarefaBtc);
    }
    xSemaphoreTake(travaBtc, portMAX_DELAY);
    filaBtc().push_back(std::move(f));
    xSemaphoreGive(travaBtc);
}

/* ---- o estado ---- */
esp_bt_controller_status_t estadoControlador = ESP_BT_CONTROLLER_STATUS_IDLE;
esp_bluedroid_status_t estadoBluedroid = ESP_BLUEDROID_STATUS_UNINITIALIZED;
esp_gap_ble_cb_t cbGap = nullptr;
esp_gatts_cb_t cbGatts = nullptr;
esp_gattc_cb_t cbGattc = nullptr;
std::string nomeDoAparelho = "BLUEDROID";
bool bleLigado = false;
BLEServer *servidor = nullptr;
BLEAdvertisementData *anuncio = nullptr;
BLEAdvertisementData *respostaDoScan = nullptr;
uint16_t proximoHandle = 40;
esp_gatt_if_t ifDoServidor = 0;

void garantirBle()
{
    if (bleLigado) return;
    bleLigado = true;
    BLEDevice::init(nomeDoAparelho.c_str());
}

BLEUUID uuidDe(const esp_bt_uuid_t &u)
{
    if (u.len == ESP_UUID_LEN_16) return BLEUUID(u.uuid.uuid16);
    if (u.len == ESP_UUID_LEN_32) return BLEUUID(u.uuid.uuid32);
    return BLEUUID((uint8_t *)u.uuid.uuid128, 16, false);
}
esp_bt_uuid_t uuidNativo(BLEUUID u)
{
    esp_bt_uuid_t s = {};
    if (u.bitSize() == 16) {
        s.len = ESP_UUID_LEN_16;
        s.uuid.uuid16 = (uint16_t)strtoul(u.toString().c_str(), nullptr, 16);
    } else if (u.bitSize() == 32) {
        s.len = ESP_UUID_LEN_32;
        s.uuid.uuid32 = (uint32_t)strtoul(u.toString().c_str(), nullptr, 16);
    } else {
        s.len = ESP_UUID_LEN_128;
        String t = u.toString();
        t.replace("-", "");
        for (int i = 0; i < 16; i++) s.uuid.uuid128[15 - i] = (uint8_t)strtoul(t.substring(2 * i, 2 * i + 2).c_str(), nullptr, 16);
    }
    return s;
}
void enderecoNativo(BLEAddress a, uint8_t *bda)
{
    unsigned v[6] = {0};
    sscanf(a.toString().c_str(), "%x:%x:%x:%x:%x:%x", &v[0], &v[1], &v[2], &v[3], &v[4], &v[5]);
    for (int i = 0; i < 6; i++) bda[i] = (uint8_t)v[i];
}
String enderecoEmTexto(const uint8_t *bda)
{
    char t[18];
    snprintf(t, sizeof(t), "%02x:%02x:%02x:%02x:%02x:%02x", bda[0], bda[1], bda[2], bda[3], bda[4], bda[5]);
    return t;
}

void avisarGap(esp_gap_ble_cb_event_t ev, esp_ble_gap_cb_param_t p = {})
{
    depois([ev, p]() mutable {
        if (cbGap) cbGap(ev, &p);
    });
}

/* =====================================================================
 * o servidor GATT */
struct Atributo {
    BLEService *servico;
    BLECharacteristic *car;
    BLEDescriptor *desc;
    uint16_t servicoHandle;
    bool respondePeloApp;
    std::string respostaDoApp;
    bool temResposta;
};
std::map<uint16_t, Atributo> &atributos()
{
    static auto *m = new std::map<uint16_t, Atributo>();
    return *m;
}
uint16_t conexaoAtual = 0;
uint8_t bdaAtual[6] = {0};
uint32_t proximoTrans = 1;

/* Os callbacks do cs_ble rodam na tarefa de quem chamou (o cliente da bancada):
 * o evento do programa roda ali mesmo, como o tratamento sincrono do "read". */
class AvisosDaCaracteristica : public BLECharacteristicCallbacks {
    uint16_t handle;

public:
    explicit AvisosDaCaracteristica(uint16_t h) : handle(h) {}
    void onRead(BLECharacteristic *c) override
    {
        Atributo &a = atributos()[handle];
        if (!a.respondePeloApp || !cbGatts) return;
        esp_ble_gatts_cb_param_t p = {};
        p.read.conn_id = conexaoAtual;
        p.read.trans_id = proximoTrans++;
        memcpy(p.read.bda, bdaAtual, 6);
        p.read.handle = handle;
        p.read.need_rsp = true;
        a.temResposta = false;
        cbGatts(ESP_GATTS_READ_EVT, ifDoServidor, &p);
        if (a.temResposta) c->setValue((const uint8_t *)a.respostaDoApp.data(), a.respostaDoApp.size());
    }
    void onWrite(BLECharacteristic *c) override
    {
        if (!cbGatts) return;
        String v = c->getValue();
        std::vector<uint8_t> copia(v.c_str(), v.c_str() + v.length());
        uint16_t h = handle;
        uint16_t con = conexaoAtual;
        depois([h, copia, con]() mutable {
            if (!cbGatts) return;
            esp_ble_gatts_cb_param_t p = {};
            p.write.conn_id = con;
            p.write.trans_id = proximoTrans++;
            memcpy(p.write.bda, bdaAtual, 6);
            p.write.handle = h;
            p.write.len = (uint16_t)copia.size();
            p.write.value = copia.data();
            cbGatts(ESP_GATTS_WRITE_EVT, ifDoServidor, &p);
        });
    }
};
class AvisosDoServidor : public BLEServerCallbacks {
    void onConnect(BLEServer *) override
    {
        conexaoAtual++;
        depois([]() {
            if (!cbGatts) return;
            esp_ble_gatts_cb_param_t p = {};
            p.connect.conn_id = conexaoAtual;
            p.connect.link_role = 1;
            memcpy(p.connect.remote_bda, bdaAtual, 6);
            cbGatts(ESP_GATTS_CONNECT_EVT, ifDoServidor, &p);
        });
    }
    void onDisconnect(BLEServer *) override
    {
        depois([]() {
            if (!cbGatts) return;
            esp_ble_gatts_cb_param_t p = {};
            p.disconnect.conn_id = conexaoAtual;
            memcpy(p.disconnect.remote_bda, bdaAtual, 6);
            p.disconnect.reason = ESP_GATT_CONN_TERMINATE_PEER_USER;
            cbGatts(ESP_GATTS_DISCONNECT_EVT, ifDoServidor, &p);
        });
    }
};
BLEServer *oServidor()
{
    garantirBle();
    if (!servidor) {
        servidor = BLEDevice::createServer();
        servidor->setCallbacks(new AvisosDoServidor());
    }
    return servidor;
}
BLEAdvertisementData *dadosDoAnuncio()
{
    if (!anuncio) anuncio = new BLEAdvertisementData();
    return anuncio;
}
uint32_t propriedadesDoArduino(esp_gatt_char_prop_t p)
{
    uint32_t r = 0;
    if (p & ESP_GATT_CHAR_PROP_BIT_READ) r |= BLECharacteristic::PROPERTY_READ;
    if (p & ESP_GATT_CHAR_PROP_BIT_WRITE) r |= BLECharacteristic::PROPERTY_WRITE;
    if (p & ESP_GATT_CHAR_PROP_BIT_WRITE_NR) r |= BLECharacteristic::PROPERTY_WRITE_NR;
    if (p & ESP_GATT_CHAR_PROP_BIT_NOTIFY) r |= BLECharacteristic::PROPERTY_NOTIFY;
    if (p & ESP_GATT_CHAR_PROP_BIT_INDICATE) r |= BLECharacteristic::PROPERTY_INDICATE;
    if (p & ESP_GATT_CHAR_PROP_BIT_BROADCAST) r |= BLECharacteristic::PROPERTY_BROADCAST;
    return r;
}

/* =====================================================================
 * o cliente GATT */
struct CaracteristicaRemota {
    BLERemoteCharacteristic *r;
    uint16_t handle;
    esp_bt_uuid_t uuid;
    uint8_t propriedades;
};
struct Conexao {
    BLEClient *cliente;
    esp_gatt_if_t gattc_if;
    uint8_t bda[6];
    uint16_t id;
    std::map<uint16_t, BLERemoteService *> servicos;
    std::map<uint16_t, CaracteristicaRemota> caracteristicas;
};
std::map<uint16_t, Conexao> &conexoes()
{
    static auto *m = new std::map<uint16_t, Conexao>();
    return *m;
}
uint16_t proximaConexao = 1;
Conexao *conexaoDe(uint16_t id)
{
    auto i = conexoes().find(id);
    return i == conexoes().end() ? nullptr : &i->second;
}
/* O notify chega sem dizer de qual: a caracteristica se acha pelo ponteiro. */
void aoNotificar(BLERemoteCharacteristic *r, uint8_t *dados, size_t n, bool ehNotify)
{
    for (auto &c : conexoes())
        for (auto &k : c.second.caracteristicas)
            if (k.second.r == r) {
                std::vector<uint8_t> copia(dados, dados + n);
                esp_gatt_if_t gif = c.second.gattc_if;
                uint16_t id = c.first, h = k.first;
                depois([copia, gif, id, h, ehNotify]() mutable {
                    if (!cbGattc) return;
                    Conexao *cx = conexaoDe(id);
                    esp_ble_gattc_cb_param_t p = {};
                    p.notify.conn_id = id;
                    if (cx) memcpy(p.notify.remote_bda, cx->bda, 6);
                    p.notify.handle = h;
                    p.notify.value_len = (uint16_t)copia.size();
                    p.notify.value = copia.data();
                    p.notify.is_notify = ehNotify;
                    cbGattc(ESP_GATTC_NOTIFY_EVT, gif, &p);
                });
                return;
            }
}
} /* namespace */

extern "C" {

/* =====================================================================
 * o controlador e o Bluedroid */
esp_err_t esp_bt_controller_init(esp_bt_controller_config_t *cfg)
{
    (void)cfg;
    if (estadoControlador != ESP_BT_CONTROLLER_STATUS_IDLE) return ESP_ERR_INVALID_STATE;
    estadoControlador = ESP_BT_CONTROLLER_STATUS_INITED;
    return ESP_OK;
}
esp_err_t esp_bt_controller_deinit(void)
{
    if (estadoControlador != ESP_BT_CONTROLLER_STATUS_INITED) return ESP_ERR_INVALID_STATE;
    estadoControlador = ESP_BT_CONTROLLER_STATUS_IDLE;
    return ESP_OK;
}
esp_err_t esp_bt_controller_enable(esp_bt_mode_t mode)
{
    (void)mode;
    if (estadoControlador != ESP_BT_CONTROLLER_STATUS_INITED) return ESP_ERR_INVALID_STATE;
    estadoControlador = ESP_BT_CONTROLLER_STATUS_ENABLED;
    return ESP_OK;
}
esp_err_t esp_bt_controller_disable(void)
{
    if (estadoControlador != ESP_BT_CONTROLLER_STATUS_ENABLED) return ESP_ERR_INVALID_STATE;
    estadoControlador = ESP_BT_CONTROLLER_STATUS_INITED;
    return ESP_OK;
}
esp_bt_controller_status_t esp_bt_controller_get_status(void) { return estadoControlador; }
esp_err_t esp_bt_controller_mem_release(esp_bt_mode_t mode)
{
    (void)mode;
    return ESP_OK;
}
esp_err_t esp_bt_mem_release(esp_bt_mode_t mode)
{
    (void)mode;
    return ESP_OK;
}
esp_err_t esp_bluedroid_init(void)
{
    if (estadoControlador != ESP_BT_CONTROLLER_STATUS_ENABLED) return ESP_ERR_INVALID_STATE;
    if (estadoBluedroid != ESP_BLUEDROID_STATUS_UNINITIALIZED) return ESP_ERR_INVALID_STATE;
    estadoBluedroid = ESP_BLUEDROID_STATUS_INITIALIZED;
    return ESP_OK;
}
esp_err_t esp_bluedroid_init_with_cfg(esp_bluedroid_config_t *cfg)
{
    (void)cfg;
    return esp_bluedroid_init();
}
esp_err_t esp_bluedroid_deinit(void)
{
    if (estadoBluedroid != ESP_BLUEDROID_STATUS_INITIALIZED) return ESP_ERR_INVALID_STATE;
    estadoBluedroid = ESP_BLUEDROID_STATUS_UNINITIALIZED;
    return ESP_OK;
}
esp_err_t esp_bluedroid_enable(void)
{
    if (estadoBluedroid != ESP_BLUEDROID_STATUS_INITIALIZED) return ESP_ERR_INVALID_STATE;
    estadoBluedroid = ESP_BLUEDROID_STATUS_ENABLED;
    return ESP_OK;
}
esp_err_t esp_bluedroid_disable(void)
{
    if (estadoBluedroid != ESP_BLUEDROID_STATUS_ENABLED) return ESP_ERR_INVALID_STATE;
    estadoBluedroid = ESP_BLUEDROID_STATUS_INITIALIZED;
    return ESP_OK;
}
esp_bluedroid_status_t esp_bluedroid_get_status(void) { return estadoBluedroid; }
const uint8_t *esp_bt_dev_get_address(void)
{
    static uint8_t a[6];
    garantirBle();
    enderecoNativo(BLEDevice::getAddress(), a);
    return a;
}

/* =====================================================================
 * o GAP do BLE */
esp_err_t esp_ble_gap_register_callback(esp_gap_ble_cb_t cb)
{
    cbGap = cb;
    return ESP_OK;
}
esp_gap_ble_cb_t esp_ble_gap_get_callback(void) { return cbGap; }
esp_err_t esp_ble_gap_set_device_name(const char *nome)
{
    if (!nome || strlen(nome) > 248) return ESP_ERR_INVALID_ARG;
    nomeDoAparelho = nome;
    if (bleLigado) dadosDoAnuncio()->setName(nome);
    return ESP_OK;
}
esp_err_t esp_ble_gap_config_adv_data(esp_ble_adv_data_t *adv)
{
    if (!adv) return ESP_ERR_INVALID_ARG;
    garantirBle();
    BLEAdvertising *a = oServidor()->getAdvertising();
    if (adv->include_name) a->setScanResponse(adv->set_scan_rsp);
    for (uint16_t i = 0; adv->p_service_uuid && i + 16 <= adv->service_uuid_len; i += 16) a->addServiceUUID(BLEUUID(adv->p_service_uuid + i, 16, false));
    if (adv->manufacturer_len && adv->p_manufacturer_data) {
        BLEAdvertisementData *d = dadosDoAnuncio();
        d->setManufacturerData(String((const char *)adv->p_manufacturer_data, adv->manufacturer_len));
    }
    if (adv->flag) dadosDoAnuncio()->setFlags(adv->flag);
    esp_ble_gap_cb_param_t p = {};
    p.adv_data_cmpl.status = ESP_BT_STATUS_SUCCESS;
    avisarGap(adv->set_scan_rsp ? ESP_GAP_BLE_SCAN_RSP_DATA_SET_COMPLETE_EVT : ESP_GAP_BLE_ADV_DATA_SET_COMPLETE_EVT, p);
    return ESP_OK;
}
esp_err_t esp_ble_gap_config_adv_data_raw(uint8_t *raw, uint32_t n)
{
    if (!raw || n > ESP_BLE_ADV_DATA_LEN_MAX) return ESP_ERR_INVALID_ARG;
    garantirBle();
    dadosDoAnuncio()->addData(String((const char *)raw, n));
    esp_ble_gap_cb_param_t p = {};
    p.adv_data_raw_cmpl.status = ESP_BT_STATUS_SUCCESS;
    avisarGap(ESP_GAP_BLE_ADV_DATA_RAW_SET_COMPLETE_EVT, p);
    return ESP_OK;
}
esp_err_t esp_ble_gap_config_scan_rsp_data_raw(uint8_t *raw, uint32_t n)
{
    if (!raw || n > ESP_BLE_SCAN_RSP_DATA_LEN_MAX) return ESP_ERR_INVALID_ARG;
    garantirBle();
    if (!respostaDoScan) respostaDoScan = new BLEAdvertisementData();
    respostaDoScan->addData(String((const char *)raw, n));
    esp_ble_gap_cb_param_t p = {};
    p.scan_rsp_data_raw_cmpl.status = ESP_BT_STATUS_SUCCESS;
    avisarGap(ESP_GAP_BLE_SCAN_RSP_DATA_RAW_SET_COMPLETE_EVT, p);
    return ESP_OK;
}
esp_err_t esp_ble_gap_start_advertising(esp_ble_adv_params_t *params)
{
    if (!params) return ESP_ERR_INVALID_ARG;
    BLEAdvertising *a = oServidor()->getAdvertising();
    if (anuncio) a->setAdvertisementData(*anuncio);
    if (respostaDoScan) a->setScanResponseData(*respostaDoScan);
    a->start();
    esp_ble_gap_cb_param_t p = {};
    p.adv_start_cmpl.status = ESP_BT_STATUS_SUCCESS;
    avisarGap(ESP_GAP_BLE_ADV_START_COMPLETE_EVT, p);
    return ESP_OK;
}
esp_err_t esp_ble_gap_stop_advertising(void)
{
    if (servidor) servidor->getAdvertising()->stop();
    esp_ble_gap_cb_param_t p = {};
    p.adv_stop_cmpl.status = ESP_BT_STATUS_SUCCESS;
    avisarGap(ESP_GAP_BLE_ADV_STOP_COMPLETE_EVT, p);
    return ESP_OK;
}
esp_err_t esp_ble_gap_update_conn_params(esp_ble_conn_update_params_t *params)
{
    if (!params) return ESP_ERR_INVALID_ARG;
    esp_ble_gap_cb_param_t p = {};
    p.update_conn_params.status = ESP_BT_STATUS_SUCCESS;
    memcpy(p.update_conn_params.bda, params->bda, 6);
    p.update_conn_params.min_int = params->min_int;
    p.update_conn_params.max_int = params->max_int;
    p.update_conn_params.latency = params->latency;
    p.update_conn_params.timeout = params->timeout;
    avisarGap(ESP_GAP_BLE_UPDATE_CONN_PARAMS_EVT, p);
    return ESP_OK;
}
esp_err_t esp_ble_gap_set_scan_params(esp_ble_scan_params_t *params)
{
    if (!params) return ESP_ERR_INVALID_ARG;
    garantirBle();
    BLEScan *s = BLEDevice::getScan();
    s->setActiveScan(params->scan_type == BLE_SCAN_TYPE_ACTIVE);
    esp_ble_gap_cb_param_t p = {};
    p.scan_param_cmpl.status = ESP_BT_STATUS_SUCCESS;
    avisarGap(ESP_GAP_BLE_SCAN_PARAM_SET_COMPLETE_EVT, p);
    return ESP_OK;
}
esp_err_t esp_ble_gap_start_scanning(uint32_t segundos)
{
    garantirBle();
    esp_ble_gap_cb_param_t c = {};
    c.scan_start_cmpl.status = ESP_BT_STATUS_SUCCESS;
    avisarGap(ESP_GAP_BLE_SCAN_START_COMPLETE_EVT, c);
    depois([segundos]() {
        BLEScanResults *r = BLEDevice::getScan()->start(segundos ? (int)segundos : 1, false);
        int n = r ? r->getCount() : 0;
        for (int i = 0; i < n; i++) {
            BLEAdvertisedDevice d = r->getDevice((uint32_t)i);
            esp_ble_gap_cb_param_t p = {};
            p.scan_rst.search_evt = ESP_GAP_SEARCH_INQ_RES_EVT;
            enderecoNativo(d.getAddress(), p.scan_rst.bda);
            p.scan_rst.dev_type = ESP_BT_DEVICE_TYPE_BLE;
            p.scan_rst.ble_addr_type = BLE_ADDR_TYPE_PUBLIC;
            p.scan_rst.ble_evt_type = ESP_BLE_EVT_CONN_ADV;
            p.scan_rst.rssi = d.getRSSI();
            uint8_t *carga = d.getPayload();
            size_t tam = d.getPayloadLength() > sizeof(p.scan_rst.ble_adv) ? sizeof(p.scan_rst.ble_adv) : d.getPayloadLength();
            memcpy(p.scan_rst.ble_adv, carga, tam);
            p.scan_rst.adv_data_len = (uint8_t)tam;
            p.scan_rst.num_resps = 1;
            if (cbGap) cbGap(ESP_GAP_BLE_SCAN_RESULT_EVT, &p);
        }
        esp_ble_gap_cb_param_t f = {};
        f.scan_rst.search_evt = ESP_GAP_SEARCH_INQ_CMPL_EVT;
        f.scan_rst.num_resps = n;
        if (cbGap) cbGap(ESP_GAP_BLE_SCAN_RESULT_EVT, &f);
    });
    return ESP_OK;
}
esp_err_t esp_ble_gap_stop_scanning(void)
{
    if (bleLigado) BLEDevice::getScan()->stop();
    esp_ble_gap_cb_param_t p = {};
    p.scan_stop_cmpl.status = ESP_BT_STATUS_SUCCESS;
    avisarGap(ESP_GAP_BLE_SCAN_STOP_COMPLETE_EVT, p);
    return ESP_OK;
}
uint8_t *esp_ble_resolve_adv_data(uint8_t *adv, uint8_t tipo, uint8_t *tamanho)
{
    if (tamanho) *tamanho = 0;
    if (!adv) return nullptr;
    /* Campos [tamanho tipo dados...]: o anuncio mais a resposta, ate 62 bytes. */
    for (int i = 0; i + 1 < ESP_BLE_ADV_DATA_LEN_MAX + ESP_BLE_SCAN_RSP_DATA_LEN_MAX && adv[i];) {
        if (adv[i + 1] == tipo) {
            if (tamanho) *tamanho = (uint8_t)(adv[i] - 1);
            return adv + i + 2;
        }
        i += adv[i] + 1;
    }
    return nullptr;
}
esp_err_t esp_ble_gap_disconnect(esp_bd_addr_t remote)
{
    (void)remote;
    if (servidor) servidor->disconnect(conexaoAtual);
    return ESP_OK;
}
esp_err_t esp_ble_gap_read_rssi(esp_bd_addr_t remote)
{
    esp_ble_gap_cb_param_t p = {};
    p.read_rssi_cmpl.status = ESP_BT_STATUS_SUCCESS;
    p.read_rssi_cmpl.rssi = -40;
    memcpy(p.read_rssi_cmpl.remote_addr, remote, 6);
    avisarGap(ESP_GAP_BLE_READ_RSSI_COMPLETE_EVT, p);
    return ESP_OK;
}

/* =====================================================================
 * o servidor GATT */
esp_err_t esp_ble_gatts_register_callback(esp_gatts_cb_t cb)
{
    cbGatts = cb;
    return ESP_OK;
}
esp_gatts_cb_t esp_ble_gatts_get_callback(void) { return cbGatts; }
esp_err_t esp_ble_gatts_app_register(uint16_t app_id)
{
    if (estadoBluedroid != ESP_BLUEDROID_STATUS_ENABLED) return ESP_ERR_INVALID_STATE;
    if (app_id > ESP_APP_ID_MAX) return ESP_ERR_INVALID_ARG;
    static esp_gatt_if_t proximo = 3;
    esp_gatt_if_t gif = proximo++;
    ifDoServidor = gif;
    depois([gif, app_id]() {
        if (!cbGatts) return;
        esp_ble_gatts_cb_param_t p = {};
        p.reg.status = ESP_GATT_OK;
        p.reg.app_id = app_id;
        cbGatts(ESP_GATTS_REG_EVT, gif, &p);
    });
    return ESP_OK;
}
esp_err_t esp_ble_gatts_app_unregister(esp_gatt_if_t gif)
{
    (void)gif;
    return ESP_OK;
}
esp_err_t esp_ble_gatts_create_service(esp_gatt_if_t gif, esp_gatt_srvc_id_t *id, uint16_t num_handle)
{
    if (!id) return ESP_ERR_INVALID_ARG;
    BLEService *s = oServidor()->createService(uuidDe(id->id.uuid), num_handle);
    uint16_t h = proximoHandle;
    proximoHandle = (uint16_t)(proximoHandle + 1);
    atributos()[h] = {s, nullptr, nullptr, h, false, "", false};
    esp_gatt_srvc_id_t copia = *id;
    depois([gif, h, copia]() {
        if (!cbGatts) return;
        esp_ble_gatts_cb_param_t p = {};
        p.create.status = ESP_GATT_OK;
        p.create.service_handle = h;
        p.create.service_id = copia;
        cbGatts(ESP_GATTS_CREATE_EVT, gif, &p);
    });
    return ESP_OK;
}
esp_err_t esp_ble_gatts_start_service(uint16_t service_handle)
{
    auto i = atributos().find(service_handle);
    if (i == atributos().end() || !i->second.servico) return ESP_ERR_INVALID_ARG;
    i->second.servico->start();
    esp_gatt_if_t gif = ifDoServidor;
    depois([gif, service_handle]() {
        if (!cbGatts) return;
        esp_ble_gatts_cb_param_t p = {};
        p.start.status = ESP_GATT_OK;
        p.start.service_handle = service_handle;
        cbGatts(ESP_GATTS_START_EVT, gif, &p);
    });
    return ESP_OK;
}
esp_err_t esp_ble_gatts_stop_service(uint16_t service_handle)
{
    auto i = atributos().find(service_handle);
    if (i == atributos().end() || !i->second.servico) return ESP_ERR_INVALID_ARG;
    i->second.servico->stop();
    esp_gatt_if_t gif = ifDoServidor;
    depois([gif, service_handle]() {
        if (!cbGatts) return;
        esp_ble_gatts_cb_param_t p = {};
        p.stop.status = ESP_GATT_OK;
        p.stop.service_handle = service_handle;
        cbGatts(ESP_GATTS_STOP_EVT, gif, &p);
    });
    return ESP_OK;
}
esp_err_t esp_ble_gatts_delete_service(uint16_t service_handle)
{
    auto i = atributos().find(service_handle);
    if (i == atributos().end() || !i->second.servico) return ESP_ERR_INVALID_ARG;
    servidor->removeService(i->second.servico);
    atributos().erase(i);
    return ESP_OK;
}
static uint16_t novaCaracteristica(uint16_t service_handle, const esp_bt_uuid_t &uuid, esp_gatt_char_prop_t propriedades, const esp_attr_value_t *valor, bool peloApp)
{
    Atributo &s = atributos()[service_handle];
    BLECharacteristic *c = s.servico->createCharacteristic(uuidDe(uuid), propriedadesDoArduino(propriedades));
    if (valor && valor->attr_value && valor->attr_len) c->setValue(valor->attr_value, valor->attr_len);
    uint16_t h = proximoHandle;
    proximoHandle = (uint16_t)(proximoHandle + 2);
    atributos()[h] = {s.servico, c, nullptr, service_handle, peloApp, "", false};
    c->setCallbacks(new AvisosDaCaracteristica(h));
    return h;
}
esp_err_t esp_ble_gatts_add_char(uint16_t service_handle, esp_bt_uuid_t *uuid, esp_gatt_perm_t perm, esp_gatt_char_prop_t property, esp_attr_value_t *char_val, esp_attr_control_t *control)
{
    (void)perm;
    if (!uuid || atributos().find(service_handle) == atributos().end()) return ESP_ERR_INVALID_ARG;
    bool peloApp = !control || control->auto_rsp == ESP_GATT_RSP_BY_APP;
    uint16_t h = novaCaracteristica(service_handle, *uuid, property, char_val, peloApp);
    esp_bt_uuid_t copia = *uuid;
    esp_gatt_if_t gif = ifDoServidor;
    depois([gif, h, service_handle, copia]() {
        if (!cbGatts) return;
        esp_ble_gatts_cb_param_t p = {};
        p.add_char.status = ESP_GATT_OK;
        p.add_char.attr_handle = h;
        p.add_char.service_handle = service_handle;
        p.add_char.char_uuid = copia;
        cbGatts(ESP_GATTS_ADD_CHAR_EVT, gif, &p);
    });
    return ESP_OK;
}
esp_err_t esp_ble_gatts_add_char_descr(uint16_t service_handle, esp_bt_uuid_t *uuid, esp_gatt_perm_t perm, esp_attr_value_t *valor, esp_attr_control_t *control)
{
    (void)perm;
    (void)control;
    if (!uuid) return ESP_ERR_INVALID_ARG;
    /* O descritor vai na ultima caracteristica criada neste servico. */
    BLECharacteristic *dona = nullptr;
    for (auto &a : atributos())
        if (a.second.servicoHandle == service_handle && a.second.car) dona = a.second.car;
    if (!dona) return ESP_ERR_INVALID_STATE;
    BLEDescriptor *d;
    if (uuid->len == ESP_UUID_LEN_16 && uuid->uuid.uuid16 == ESP_GATT_UUID_CHAR_CLIENT_CONFIG) d = new BLE2902();
    else d = new BLEDescriptor(uuidDe(*uuid));
    if (valor && valor->attr_value && valor->attr_len) d->setValue(valor->attr_value, valor->attr_len);
    dona->addDescriptor(d);
    uint16_t h = proximoHandle++;
    atributos()[h] = {nullptr, nullptr, d, service_handle, false, "", false};
    esp_bt_uuid_t copia = *uuid;
    esp_gatt_if_t gif = ifDoServidor;
    depois([gif, h, service_handle, copia]() {
        if (!cbGatts) return;
        esp_ble_gatts_cb_param_t p = {};
        p.add_char_descr.status = ESP_GATT_OK;
        p.add_char_descr.attr_handle = h;
        p.add_char_descr.service_handle = service_handle;
        p.add_char_descr.descr_uuid = copia;
        cbGatts(ESP_GATTS_ADD_CHAR_DESCR_EVT, gif, &p);
    });
    return ESP_OK;
}
esp_err_t esp_ble_gatts_create_attr_tab(const esp_gatts_attr_db_t *db, esp_gatt_if_t gif, uint16_t max_nb_attr, uint8_t srvc_inst_id)
{
    if (!db || !max_nb_attr) return ESP_ERR_INVALID_ARG;
    auto *handles = new std::vector<uint16_t>(max_nb_attr, 0);
    BLEService *s = nullptr;
    uint16_t servicoHandle = 0;
    esp_gatt_char_prop_t propriedades = 0;
    esp_bt_uuid_t uuidDoServico = {};
    for (uint16_t i = 0; i < max_nb_attr; i++) {
        const esp_attr_desc_t &a = db[i].att_desc;
        uint16_t tipo = a.uuid_length == ESP_UUID_LEN_16 ? (uint16_t)(a.uuid_p[0] | (a.uuid_p[1] << 8)) : 0;
        esp_bt_uuid_t u = {};
        if (tipo == ESP_GATT_UUID_PRI_SERVICE || tipo == ESP_GATT_UUID_SEC_SERVICE) {
            u.len = a.length == 16 ? ESP_UUID_LEN_128 : ESP_UUID_LEN_16;
            if (a.length == 16) memcpy(u.uuid.uuid128, a.value, 16);
            else u.uuid.uuid16 = (uint16_t)(a.value[0] | (a.value[1] << 8));
            uuidDoServico = u;
            s = oServidor()->createService(uuidDe(u), max_nb_attr);
            servicoHandle = proximoHandle++;
            atributos()[servicoHandle] = {s, nullptr, nullptr, servicoHandle, false, "", false};
            (*handles)[i] = servicoHandle;
        } else if (tipo == ESP_GATT_UUID_CHAR_DECLARE) {
            propriedades = a.value ? a.value[0] : 0;
            (*handles)[i] = proximoHandle++;
        } else if (tipo == ESP_GATT_UUID_CHAR_CLIENT_CONFIG || (tipo >= 0x2900 && tipo <= 0x290F)) {
            esp_bt_uuid_t d = {};
            d.len = ESP_UUID_LEN_16;
            d.uuid.uuid16 = tipo;
            BLECharacteristic *dona = nullptr;
            for (auto &x : atributos())
                if (x.second.servicoHandle == servicoHandle && x.second.car) dona = x.second.car;
            if (dona) {
                BLEDescriptor *descritor = tipo == ESP_GATT_UUID_CHAR_CLIENT_CONFIG ? (BLEDescriptor *)new BLE2902() : new BLEDescriptor(uuidDe(d));
                dona->addDescriptor(descritor);
                uint16_t h = proximoHandle++;
                atributos()[h] = {nullptr, nullptr, descritor, servicoHandle, false, "", false};
                (*handles)[i] = h;
            }
        } else if (s) {
            if (a.uuid_length == ESP_UUID_LEN_128) {
                u.len = ESP_UUID_LEN_128;
                memcpy(u.uuid.uuid128, a.uuid_p, 16);
            } else {
                u.len = ESP_UUID_LEN_16;
                u.uuid.uuid16 = tipo;
            }
            esp_attr_value_t v = {};
            v.attr_len = a.length;
            v.attr_max_len = a.max_length;
            v.attr_value = a.value;
            (*handles)[i] = novaCaracteristica(servicoHandle, u, propriedades, a.value ? &v : nullptr, db[i].attr_control.auto_rsp == ESP_GATT_RSP_BY_APP);
        }
    }
    depois([gif, handles, max_nb_attr, srvc_inst_id, uuidDoServico]() {
        if (cbGatts) {
            esp_ble_gatts_cb_param_t p = {};
            p.add_attr_tab.status = ESP_GATT_OK;
            p.add_attr_tab.svc_uuid = uuidDoServico;
            p.add_attr_tab.svc_inst_id = srvc_inst_id;
            p.add_attr_tab.num_handle = max_nb_attr;
            p.add_attr_tab.handles = handles->data();
            cbGatts(ESP_GATTS_CREAT_ATTR_TAB_EVT, gif, &p);
        }
    });
    return ESP_OK;
}
esp_err_t esp_ble_gatts_send_response(esp_gatt_if_t gif, uint16_t conn_id, uint32_t trans_id, esp_gatt_status_t status, esp_gatt_rsp_t *rsp)
{
    (void)gif;
    (void)conn_id;
    (void)trans_id;
    (void)status;
    if (rsp) {
        auto i = atributos().find(rsp->attr_value.handle);
        if (i != atributos().end()) {
            i->second.respostaDoApp.assign((const char *)rsp->attr_value.value, rsp->attr_value.len);
            i->second.temResposta = true;
            if (i->second.car) i->second.car->setValue(rsp->attr_value.value, rsp->attr_value.len);
        }
    }
    return ESP_OK;
}
esp_err_t esp_ble_gatts_send_indicate(esp_gatt_if_t gif, uint16_t conn_id, uint16_t handle, uint16_t len, uint8_t *valor, bool need_confirm)
{
    auto i = atributos().find(handle);
    if (i == atributos().end() || !i->second.car) return ESP_ERR_INVALID_ARG;
    i->second.car->setValue(valor, len);
    if (need_confirm) i->second.car->indicate();
    else i->second.car->notify();
    depois([gif, conn_id, handle]() {
        if (!cbGatts) return;
        esp_ble_gatts_cb_param_t p = {};
        p.conf.status = ESP_GATT_OK;
        p.conf.conn_id = conn_id;
        p.conf.handle = handle;
        cbGatts(ESP_GATTS_CONF_EVT, gif, &p);
    });
    return ESP_OK;
}
esp_err_t esp_ble_gatts_set_attr_value(uint16_t handle, uint16_t len, const uint8_t *valor)
{
    auto i = atributos().find(handle);
    if (i == atributos().end()) return ESP_ERR_INVALID_ARG;
    if (i->second.car) i->second.car->setValue(valor, len);
    else if (i->second.desc) i->second.desc->setValue((uint8_t *)valor, len);
    return ESP_OK;
}
esp_gatt_status_t esp_ble_gatts_get_attr_value(uint16_t handle, uint16_t *len, const uint8_t **valor)
{
    auto i = atributos().find(handle);
    if (i == atributos().end() || !i->second.car) return ESP_GATT_INVALID_HANDLE;
    static String guardado;
    guardado = i->second.car->getValue();
    if (len) *len = (uint16_t)guardado.length();
    if (valor) *valor = (const uint8_t *)guardado.c_str();
    return ESP_GATT_OK;
}
esp_err_t esp_ble_gatts_close(esp_gatt_if_t gif, uint16_t conn_id)
{
    (void)gif;
    if (servidor) servidor->disconnect(conn_id);
    return ESP_OK;
}

/* =====================================================================
 * o cliente GATT */
esp_err_t esp_ble_gattc_register_callback(esp_gattc_cb_t cb)
{
    cbGattc = cb;
    return ESP_OK;
}
esp_gattc_cb_t esp_ble_gattc_get_callback(void) { return cbGattc; }
esp_err_t esp_ble_gattc_app_register(uint16_t app_id)
{
    if (estadoBluedroid != ESP_BLUEDROID_STATUS_ENABLED) return ESP_ERR_INVALID_STATE;
    static esp_gatt_if_t proximo = 10;
    esp_gatt_if_t gif = proximo++;
    depois([gif, app_id]() {
        if (!cbGattc) return;
        esp_ble_gattc_cb_param_t p = {};
        p.reg.status = ESP_GATT_OK;
        p.reg.app_id = app_id;
        cbGattc(ESP_GATTC_REG_EVT, gif, &p);
    });
    return ESP_OK;
}
esp_err_t esp_ble_gattc_app_unregister(esp_gatt_if_t gif)
{
    (void)gif;
    return ESP_OK;
}
esp_err_t esp_ble_gattc_open(esp_gatt_if_t gif, esp_bd_addr_t bda, esp_ble_addr_type_t tipo, bool direto)
{
    (void)tipo;
    (void)direto;
    garantirBle();
    std::string endereco = enderecoEmTexto(bda).c_str();
    uint8_t bdaCopia[6];
    memcpy(bdaCopia, bda, 6);
    std::vector<uint8_t> copia(bdaCopia, bdaCopia + 6);
    depois([gif, endereco, copia]() {
        BLEClient *c = BLEDevice::createClient();
        bool ok = c->connect(BLEAddress(endereco.c_str()));
        uint16_t id = proximaConexao++;
        if (ok) {
            Conexao k = {};
            k.cliente = c;
            k.gattc_if = gif;
            memcpy(k.bda, copia.data(), 6);
            k.id = id;
            conexoes()[id] = k;
            if (cbGattc) {
                esp_ble_gattc_cb_param_t p = {};
                p.connect.conn_id = id;
                memcpy(p.connect.remote_bda, copia.data(), 6);
                cbGattc(ESP_GATTC_CONNECT_EVT, gif, &p);
            }
        }
        if (cbGattc) {
            esp_ble_gattc_cb_param_t p = {};
            p.open.status = ok ? ESP_GATT_OK : ESP_GATT_ERROR;
            p.open.conn_id = id;
            memcpy(p.open.remote_bda, copia.data(), 6);
            p.open.mtu = 23;
            cbGattc(ESP_GATTC_OPEN_EVT, gif, &p);
        }
    });
    return ESP_OK;
}
esp_err_t esp_ble_gattc_close(esp_gatt_if_t gif, uint16_t conn_id)
{
    depois([gif, conn_id]() {
        Conexao *k = conexaoDe(conn_id);
        if (!k) return;
        k->cliente->disconnect();
        if (cbGattc) {
            esp_ble_gattc_cb_param_t p = {};
            p.close.status = ESP_GATT_OK;
            p.close.conn_id = conn_id;
            memcpy(p.close.remote_bda, k->bda, 6);
            cbGattc(ESP_GATTC_CLOSE_EVT, gif, &p);
            esp_ble_gattc_cb_param_t d = {};
            d.disconnect.conn_id = conn_id;
            memcpy(d.disconnect.remote_bda, k->bda, 6);
            d.disconnect.reason = ESP_GATT_CONN_TERMINATE_LOCAL_HOST;
            cbGattc(ESP_GATTC_DISCONNECT_EVT, gif, &d);
        }
        conexoes().erase(conn_id);
    });
    return ESP_OK;
}
esp_err_t esp_ble_gattc_send_mtu_req(esp_gatt_if_t gif, uint16_t conn_id)
{
    depois([gif, conn_id]() {
        if (!cbGattc) return;
        esp_ble_gattc_cb_param_t p = {};
        p.cfg_mtu.status = ESP_GATT_OK;
        p.cfg_mtu.conn_id = conn_id;
        p.cfg_mtu.mtu = 23;
        cbGattc(ESP_GATTC_CFG_MTU_EVT, gif, &p);
    });
    return ESP_OK;
}
/* O simulador acha o servico pelo UUID: a busca sem filtro nao lista nada. */
esp_err_t esp_ble_gattc_search_service(esp_gatt_if_t gif, uint16_t conn_id, esp_bt_uuid_t *filtro)
{
    bool temFiltro = filtro != nullptr;
    esp_bt_uuid_t f = {};
    if (filtro) f = *filtro;
    depois([gif, conn_id, temFiltro, f]() {
        Conexao *k = conexaoDe(conn_id);
        if (!k) return;
        if (temFiltro) {
            BLERemoteService *s = k->cliente->getService(uuidDe(f));
            if (s) {
                uint16_t h = proximoHandle;
                proximoHandle = (uint16_t)(proximoHandle + 32);
                k->servicos[h] = s;
                if (cbGattc) {
                    esp_ble_gattc_cb_param_t p = {};
                    p.search_res.conn_id = conn_id;
                    p.search_res.start_handle = h;
                    p.search_res.end_handle = (uint16_t)(h + 31);
                    p.search_res.srvc_id.uuid = f;
                    p.search_res.is_primary = true;
                    cbGattc(ESP_GATTC_SEARCH_RES_EVT, gif, &p);
                }
            }
        } else log_w("o simulador so acha servico com o filtro de UUID");
        if (cbGattc) {
            esp_ble_gattc_cb_param_t p = {};
            p.search_cmpl.status = ESP_GATT_OK;
            p.search_cmpl.conn_id = conn_id;
            cbGattc(ESP_GATTC_SEARCH_CMPL_EVT, gif, &p);
        }
    });
    return ESP_OK;
}
esp_gatt_status_t esp_ble_gattc_get_attr_count(esp_gatt_if_t gif, uint16_t conn_id, esp_gatt_db_attr_type_t tipo, uint16_t inicio, uint16_t fim, uint16_t char_handle, uint16_t *contagem)
{
    (void)gif;
    (void)tipo;
    (void)inicio;
    (void)fim;
    (void)char_handle;
    if (!conexaoDe(conn_id) || !contagem) return ESP_GATT_INVALID_HANDLE;
    *contagem = 8;
    return ESP_GATT_OK;
}
esp_gatt_status_t esp_ble_gattc_get_char_by_uuid(esp_gatt_if_t gif, uint16_t conn_id, uint16_t inicio, uint16_t fim, esp_bt_uuid_t uuid, esp_gattc_char_elem_t *resultado, uint16_t *contagem)
{
    (void)gif;
    (void)fim;
    Conexao *k = conexaoDe(conn_id);
    if (!k || !contagem) return ESP_GATT_INVALID_HANDLE;
    *contagem = 0;
    auto s = k->servicos.find(inicio);
    if (s == k->servicos.end()) return ESP_GATT_NOT_FOUND;
    BLERemoteCharacteristic *r = s->second->getCharacteristic(uuidDe(uuid));
    if (!r) return ESP_GATT_NOT_FOUND;
    uint16_t h = (uint16_t)(inicio + 1 + 2 * k->caracteristicas.size());
    uint8_t prop = 0;
    if (r->canRead()) prop |= ESP_GATT_CHAR_PROP_BIT_READ;
    if (r->canWrite()) prop |= ESP_GATT_CHAR_PROP_BIT_WRITE;
    if (r->canWriteNoResponse()) prop |= ESP_GATT_CHAR_PROP_BIT_WRITE_NR;
    if (r->canNotify()) prop |= ESP_GATT_CHAR_PROP_BIT_NOTIFY;
    if (r->canIndicate()) prop |= ESP_GATT_CHAR_PROP_BIT_INDICATE;
    k->caracteristicas[h] = {r, h, uuid, prop};
    if (resultado) {
        resultado->char_handle = h;
        resultado->properties = prop;
        resultado->uuid = uuid;
    }
    *contagem = 1;
    return ESP_GATT_OK;
}
esp_gatt_status_t esp_ble_gattc_get_descr_by_char_handle(esp_gatt_if_t gif, uint16_t conn_id, uint16_t char_handle, esp_bt_uuid_t uuid, esp_gattc_descr_elem_t *resultado, uint16_t *contagem)
{
    (void)gif;
    Conexao *k = conexaoDe(conn_id);
    if (!k || !contagem) return ESP_GATT_INVALID_HANDLE;
    if (k->caracteristicas.find(char_handle) == k->caracteristicas.end()) return ESP_GATT_NOT_FOUND;
    if (resultado) {
        resultado->handle = (uint16_t)(char_handle + 1);
        resultado->uuid = uuid;
    }
    *contagem = 1;
    return ESP_GATT_OK;
}
esp_err_t esp_ble_gattc_read_char(esp_gatt_if_t gif, uint16_t conn_id, uint16_t handle, esp_gatt_auth_req_t auth)
{
    (void)auth;
    depois([gif, conn_id, handle]() {
        Conexao *k = conexaoDe(conn_id);
        auto c = k ? k->caracteristicas.find(handle) : decltype(k->caracteristicas.end())();
        esp_ble_gattc_cb_param_t p = {};
        p.read.conn_id = conn_id;
        p.read.handle = handle;
        String v;
        if (k && c != k->caracteristicas.end()) {
            v = c->second.r->readValue();
            p.read.status = ESP_GATT_OK;
            p.read.value = (uint8_t *)v.c_str();
            p.read.value_len = (uint16_t)v.length();
        } else p.read.status = ESP_GATT_INVALID_HANDLE;
        if (cbGattc) cbGattc(ESP_GATTC_READ_CHAR_EVT, gif, &p);
    });
    return ESP_OK;
}
esp_err_t esp_ble_gattc_write_char(esp_gatt_if_t gif, uint16_t conn_id, uint16_t handle, uint16_t n, uint8_t *valor, esp_gatt_write_type_t tipo, esp_gatt_auth_req_t auth)
{
    (void)auth;
    std::vector<uint8_t> copia(valor, valor + n);
    depois([gif, conn_id, handle, copia, tipo]() mutable {
        Conexao *k = conexaoDe(conn_id);
        esp_ble_gattc_cb_param_t p = {};
        p.write.conn_id = conn_id;
        p.write.handle = handle;
        p.write.status = ESP_GATT_INVALID_HANDLE;
        if (k) {
            auto c = k->caracteristicas.find(handle);
            if (c != k->caracteristicas.end()) {
                c->second.r->writeValue(copia.data(), copia.size(), tipo == ESP_GATT_WRITE_TYPE_RSP);
                p.write.status = ESP_GATT_OK;
            }
        }
        if (cbGattc) cbGattc(ESP_GATTC_WRITE_CHAR_EVT, gif, &p);
    });
    return ESP_OK;
}
esp_err_t esp_ble_gattc_write_char_descr(esp_gatt_if_t gif, uint16_t conn_id, uint16_t handle, uint16_t n, uint8_t *valor, esp_gatt_write_type_t tipo, esp_gatt_auth_req_t auth)
{
    (void)n;
    (void)valor;
    (void)tipo;
    (void)auth;
    /* O CCCD: a assinatura ja foi feita no register_for_notify. */
    depois([gif, conn_id, handle]() {
        if (!cbGattc) return;
        esp_ble_gattc_cb_param_t p = {};
        p.write.conn_id = conn_id;
        p.write.handle = handle;
        p.write.status = ESP_GATT_OK;
        cbGattc(ESP_GATTC_WRITE_DESCR_EVT, gif, &p);
    });
    return ESP_OK;
}
esp_err_t esp_ble_gattc_register_for_notify(esp_gatt_if_t gif, esp_bd_addr_t bda, uint16_t handle)
{
    (void)bda;
    depois([gif, handle]() {
        esp_ble_gattc_cb_param_t p = {};
        p.reg_for_notify.handle = handle;
        p.reg_for_notify.status = ESP_GATT_INVALID_HANDLE;
        for (auto &k : conexoes()) {
            auto c = k.second.caracteristicas.find(handle);
            if (c != k.second.caracteristicas.end()) {
                c->second.r->registerForNotify(aoNotificar);
                p.reg_for_notify.status = ESP_GATT_OK;
            }
        }
        if (cbGattc) cbGattc(ESP_GATTC_REG_FOR_NOTIFY_EVT, gif, &p);
    });
    return ESP_OK;
}
esp_err_t esp_ble_gattc_unregister_for_notify(esp_gatt_if_t gif, esp_bd_addr_t bda, uint16_t handle)
{
    (void)gif;
    (void)bda;
    (void)handle;
    return ESP_OK;
}
} /* extern "C" */
#endif
