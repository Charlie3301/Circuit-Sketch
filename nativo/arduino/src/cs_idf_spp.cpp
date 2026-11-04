/* cs_idf_spp.cpp -- o SPP e o GAP do Bluetooth classico do ESP-IDF (esp_spp_api.h,
 * esp_gap_bt_api.h) sobre o BluetoothSerial do simulador: o mesmo enlace entre as
 * placas da bancada, com os eventos numa tarefa propria, como na placa. */
#include "Arduino.h"
#if defined(ESP32) && SOC_BT_SUPPORTED && CONFIG_BT_ENABLED && CONFIG_BLUEDROID_ENABLED
#include "BluetoothSerial.h"
#include "esp_bt_device.h"
#include "esp_spp_api.h"
#include "esp_gap_bt_api.h"
#include "freertos/task.h"
#include <string>
#include <vector>

namespace {
BluetoothSerial &enlace()
{
    static auto *b = new BluetoothSerial();
    return *b;
}
std::string nomeClassico = "ESP32";
esp_spp_cb_t cbSpp = nullptr;
esp_bt_gap_cb_t cbGapClassico = nullptr;
bool sppLigado = false, servidorNoAr = false, conectado = false, ehCliente = false;
TaskHandle_t tarefaDoSpp = nullptr;
const uint32_t HANDLE_DO_ENLACE = 0x81;

void avisar(esp_spp_cb_event_t ev, esp_spp_cb_param_t p)
{
    if (cbSpp) cbSpp(ev, &p);
}

void vigiaDoSpp(void *)
{
    for (;;) {
        bool agora = enlace().connected();
        if (agora && !conectado && !ehCliente) {
            esp_spp_cb_param_t p = {};
            p.srv_open.status = ESP_SPP_SUCCESS;
            p.srv_open.handle = HANDLE_DO_ENLACE;
            avisar(ESP_SPP_SRV_OPEN_EVT, p);
        } else if (!agora && conectado) {
            esp_spp_cb_param_t p = {};
            p.close.status = ESP_SPP_SUCCESS;
            p.close.handle = HANDLE_DO_ENLACE;
            avisar(ESP_SPP_CLOSE_EVT, p);
            ehCliente = false;
        }
        conectado = agora;
        int n = enlace().available();
        if (n > 0) {
            std::vector<uint8_t> b((size_t)n);
            for (int i = 0; i < n; i++) b[(size_t)i] = (uint8_t)enlace().read();
            esp_spp_cb_param_t p = {};
            p.data_ind.status = ESP_SPP_SUCCESS;
            p.data_ind.handle = HANDLE_DO_ENLACE;
            p.data_ind.len = (uint16_t)n;
            p.data_ind.data = b.data();
            avisar(ESP_SPP_DATA_IND_EVT, p);
        }
        vTaskDelay(pdMS_TO_TICKS(5));
    }
}
} /* namespace */

extern "C" {

esp_err_t esp_spp_register_callback(esp_spp_cb_t callback)
{
    cbSpp = callback;
    return ESP_OK;
}
esp_err_t esp_spp_enhanced_init(const esp_spp_cfg_t *cfg)
{
    if (!cfg) return ESP_ERR_INVALID_ARG;
    if (!cbSpp || sppLigado) return ESP_ERR_INVALID_STATE;
    sppLigado = true;
    esp_spp_cb_param_t p = {};
    p.init.status = ESP_SPP_SUCCESS;
    avisar(ESP_SPP_INIT_EVT, p);
    return ESP_OK;
}
esp_err_t esp_spp_init(esp_spp_mode_t mode)
{
    esp_spp_cfg_t c = {};
    c.mode = mode;
    return esp_spp_enhanced_init(&c);
}
esp_err_t esp_spp_deinit(void)
{
    if (!sppLigado) return ESP_ERR_INVALID_STATE;
    sppLigado = false;
    enlace().end();
    if (tarefaDoSpp) vTaskDelete(tarefaDoSpp);
    tarefaDoSpp = nullptr;
    esp_spp_cb_param_t p = {};
    p.uninit.status = ESP_SPP_SUCCESS;
    avisar(ESP_SPP_UNINIT_EVT, p);
    return ESP_OK;
}
esp_err_t esp_spp_start_srv(esp_spp_sec_t sec, esp_spp_role_t role, uint8_t scn, const char *nome)
{
    (void)sec;
    (void)role;
    (void)nome;
    if (!sppLigado) return ESP_ERR_INVALID_STATE;
    enlace().begin(nomeClassico.c_str(), false);
    servidorNoAr = true;
    if (!tarefaDoSpp) xTaskCreate(vigiaDoSpp, "spp", 4096, nullptr, 5, &tarefaDoSpp);
    esp_spp_cb_param_t p = {};
    p.start.status = ESP_SPP_SUCCESS;
    p.start.handle = HANDLE_DO_ENLACE;
    p.start.scn = scn;
    avisar(ESP_SPP_START_EVT, p);
    return ESP_OK;
}
esp_err_t esp_spp_stop_srv(void)
{
    servidorNoAr = false;
    return ESP_OK;
}
esp_err_t esp_spp_connect(esp_spp_sec_t sec, esp_spp_role_t role, uint8_t scn, esp_bd_addr_t bda)
{
    (void)sec;
    (void)role;
    (void)scn;
    if (!sppLigado) return ESP_ERR_INVALID_STATE;
    enlace().begin(nomeClassico.c_str(), true);
    ehCliente = true;
    if (!tarefaDoSpp) xTaskCreate(vigiaDoSpp, "spp", 4096, nullptr, 5, &tarefaDoSpp);
    bool ok = enlace().connect(BTAddress(bda));
    esp_spp_cb_param_t p = {};
    p.open.status = ok ? ESP_SPP_SUCCESS : ESP_SPP_FAILURE;
    p.open.handle = HANDLE_DO_ENLACE;
    memcpy(p.open.rem_bda, bda, 6);
    avisar(ESP_SPP_OPEN_EVT, p);
    return ESP_OK;
}
esp_err_t esp_spp_disconnect(uint32_t handle)
{
    (void)handle;
    enlace().disconnect();
    return ESP_OK;
}
esp_err_t esp_spp_write(uint32_t handle, int n, uint8_t *dados)
{
    if (!sppLigado || n < 0 || !dados) return ESP_ERR_INVALID_ARG;
    enlace().write(dados, (size_t)n);
    esp_spp_cb_param_t p = {};
    p.write.status = ESP_SPP_SUCCESS;
    p.write.handle = handle;
    p.write.len = n;
    avisar(ESP_SPP_WRITE_EVT, p);
    return ESP_OK;
}

/* ---- o GAP classico ---- */
esp_err_t esp_bt_gap_register_callback(esp_bt_gap_cb_t callback)
{
    cbGapClassico = callback;
    return ESP_OK;
}
esp_err_t esp_bt_gap_set_device_name(const char *nome)
{
    if (!nome) return ESP_ERR_INVALID_ARG;
    nomeClassico = nome;
    return ESP_OK;
}
esp_err_t esp_bt_dev_set_device_name(const char *nome) { return esp_bt_gap_set_device_name(nome); }
esp_err_t esp_bt_gap_start_discovery(esp_bt_inq_mode_t mode, uint8_t duracao, uint8_t quantos)
{
    (void)mode;
    (void)quantos;
    enlace().begin(nomeClassico.c_str(), true);
    esp_bt_gap_cb_param_t e = {};
    e.disc_st_chg.state = ESP_BT_GAP_DISCOVERY_STARTED;
    if (cbGapClassico) cbGapClassico(ESP_BT_GAP_DISC_STATE_CHANGED_EVT, &e);
    BTScanResults *r = enlace().discover((int)duracao * 1280);
    for (int i = 0; r && i < r->getCount(); i++) {
        BTAdvertisedDevice *d = r->getDevice((unsigned)i);
        esp_bt_gap_cb_param_t p = {};
        memcpy(p.disc_res.bda, *d->getAddress().getNative(), 6);
        std::string nome = d->getName().c_str();
        esp_bt_gap_dev_prop_t prop = {};
        prop.type = ESP_BT_GAP_DEV_PROP_BDNAME;
        prop.len = (int)nome.size();
        prop.val = (void *)nome.c_str();
        p.disc_res.num_prop = 1;
        p.disc_res.prop = &prop;
        if (cbGapClassico) cbGapClassico(ESP_BT_GAP_DISC_RES_EVT, &p);
    }
    esp_bt_gap_cb_param_t f = {};
    f.disc_st_chg.state = ESP_BT_GAP_DISCOVERY_STOPPED;
    if (cbGapClassico) cbGapClassico(ESP_BT_GAP_DISC_STATE_CHANGED_EVT, &f);
    return ESP_OK;
}
esp_err_t esp_bt_gap_cancel_discovery(void)
{
    enlace().discoverAsyncStop();
    return ESP_OK;
}
} /* extern "C" */
#endif
