/* cs_tinyusb.cpp -- o USB da S2 e da S3 no simulador: o esp32-hal-tinyusb.c do
 * core e as funcoes do TinyUSB que o codigo USB de verdade chama (USB.cpp,
 * USBCDC.cpp, USBHID.cpp, USBMIDI.cpp, USBVendor.cpp...).
 *
 * Na placa o TinyUSB fala com o controlador USB do chip e o computador
 * enumera a placa. Aqui o "computador" e o da bancada (motor/Usb.cs): a placa
 * aparece nele com as interfaces que o sketch ligou, o relatorio HID vira o
 * texto digitado, o mouse andando, o MIDI vira as notas; o CDC (USBSerial) e
 * o monitor serial. Como no computador de verdade, o host atende na hora: o
 * relatorio enviado ja volta como entregue (o tud_hid_report_complete_cb). */
#include "Arduino.h"
#if defined(ESP32)
#include "soc/soc_caps.h"
#include "sdkconfig.h"
#if SOC_USB_OTG_SUPPORTED && CONFIG_TINYUSB_ENABLED
#include "esp32-hal-tinyusb.h"
#include "tusb.h"
#include "cs_objeto.h"
#include "cs_interno.h"
#include <string>

static String cs_hex_de(const void *dados, size_t n)
{
    static const char digitos[] = "0123456789ABCDEF";
    String h;
    const uint8_t *p = (const uint8_t *)dados;
    for (size_t i = 0; i < n; i++) {
        h += digitos[p[i] >> 4];
        h += digitos[p[i] & 15];
    }
    return h;
}

/* =====================================================================
 * o esp32-hal-tinyusb (o que o USB.cpp e as classes usam para montar o dispositivo) */
static std::string cs_interfaces;
static uint8_t cs_proximo_in = 1, cs_proximo_out = 1, cs_proxima_string = 6;
static bool cs_iniciado = false;

static const char *cs_nome_da_interface(tinyusb_interface_t i)
{
    switch (i) {
    case USB_INTERFACE_MSC: return "MSC";
    case USB_INTERFACE_DFU: return "DFU";
    case USB_INTERFACE_HID: return "HID";
    case USB_INTERFACE_VENDOR: return "Vendor";
    case USB_INTERFACE_CDC:
    case USB_INTERFACE_CDC2: return "CDC";
    case USB_INTERFACE_MIDI: return "MIDI";
    case USB_INTERFACE_AUDIO: return "Audio";
    default: return "outra";
    }
}

esp_err_t tinyusb_enable_interface2(tinyusb_interface_t interface, uint16_t descriptor_len, tinyusb_descriptor_cb_t cb, bool reserve_endpoints)
{
    (void)descriptor_len;
    (void)reserve_endpoints;
    if (cs_iniciado) {
        log_e("Interface %s can not be enabled after USB has been started", cs_nome_da_interface(interface));
        return ESP_FAIL;
    }
    /* O descritor de cada interface o host pede na enumeracao: o da classe se monta aqui. */
    if (cb) {
        uint8_t itf = 0, buf[512];
        cb(buf, &itf);
    }
    if (!cs_interfaces.empty()) cs_interfaces += ", ";
    cs_interfaces += cs_nome_da_interface(interface);
    return ESP_OK;
}

esp_err_t tinyusb_enable_interface(tinyusb_interface_t interface, uint16_t descriptor_len, tinyusb_descriptor_cb_t cb)
{
    return tinyusb_enable_interface2(interface, descriptor_len, cb, false);
}

uint8_t tinyusb_add_string_descriptor(const char *str)
{
    (void)str;
    return cs_proxima_string++;
}
uint8_t tinyusb_get_free_duplex_endpoint(void)
{
    uint8_t ep = cs_proximo_in > cs_proximo_out ? cs_proximo_in : cs_proximo_out;
    cs_proximo_in = cs_proximo_out = ep + 1;
    return ep;
}
uint8_t tinyusb_get_free_in_endpoint(void) { return 0x80 | cs_proximo_in++; }
uint8_t tinyusb_get_free_out_endpoint(void) { return cs_proximo_out++; }

esp_err_t tinyusb_init(tinyusb_device_config_t *config)
{
    (void)config;
    if (cs_iniciado) return ESP_OK;
    cs_iniciado = true;
    ch(nullptr, "__usb", cs_t("conectou"), cs_i(0), cs_i(0), cs_t(cs_interfaces.c_str()));
    /* O computador montou a placa (o ARDUINO_USB_STARTED_EVENT do USB.cpp) e o
     * terminal abriu as portas CDC (o DTR, como o monitor serial faz). */
    tud_mount_cb();
#if CFG_TUD_CDC
    for (uint8_t i = 0; i < CFG_TUD_CDC; i++) tud_cdc_line_state_cb(i, true, true);
#endif
    return ESP_OK;
}

esp_err_t tinyusb_host_init(void)
{
    log_e("USB host: o simulador ainda nao liga aparelhos USB na placa");
    return ESP_ERR_NOT_SUPPORTED;
}

void usb_persist_restart(restart_type_t mode)
{
    if (mode == RESTART_BOOTLOADER || mode == RESTART_BOOTLOADER_DFU)
        log_i("USB: reiniciando no modo de gravacao (o simulador volta ao sketch do editor)");
    esp_restart();
}

/* =====================================================================
 * o TinyUSB: o dispositivo (tud_*) */
bool tud_mounted(void) { return cs_iniciado; }
bool tud_suspended(void) { return false; }
bool tud_connected(void) { return cs_iniciado; }
bool tud_remote_wakeup(void) { return cs_iniciado; }
bool tud_disconnect(void) { return true; }
bool tud_connect(void) { return true; }

/* O controle (EP0): o host de mentira aceita o que a classe responder. */
bool tud_control_status(uint8_t rhport, tusb_control_request_t const *request)
{
    (void)rhport;
    (void)request;
    return true;
}
bool tud_control_xfer(uint8_t rhport, tusb_control_request_t const *request, void *buffer, uint16_t len)
{
    (void)rhport;
    (void)request;
    (void)buffer;
    (void)len;
    return true;
}

#if CFG_TUD_HID
bool tud_hid_n_ready(uint8_t instance) { return cs_iniciado && instance == 0; }
uint8_t tud_hid_n_get_protocol(uint8_t instance)
{
    (void)instance;
    return HID_PROTOCOL_REPORT;
}
bool tud_hid_n_report(uint8_t instance, uint8_t report_id, void const *report, uint16_t len)
{
    if (!tud_hid_n_ready(instance)) return false;
    ch(nullptr, "__usb", cs_t("hid"), cs_i(0), cs_i(report_id), cs_t(cs_hex_de(report, len).c_str()));
    /* O host leu o relatorio: o USBHID espera por este aviso para mandar o proximo. */
    tud_hid_report_complete_cb(instance, (uint8_t const *)report, len);
    return true;
}
#endif

#if CFG_TUD_CDC
/* O USBSerial e o monitor serial (a mesma janela do Serial da placa). */
bool tud_cdc_n_connected(uint8_t itf) { return cs_iniciado && itf < CFG_TUD_CDC; }
uint32_t tud_cdc_n_write(uint8_t itf, void const *buffer, uint32_t bufsize)
{
    if (!tud_cdc_n_connected(itf) || !bufsize) return 0;
    ch("Serial", "print", cs_tn((const char *)buffer, (int)bufsize));
    tud_cdc_tx_complete_cb(itf);
    return bufsize;
}
uint32_t tud_cdc_n_write_available(uint8_t itf) { return tud_cdc_n_connected(itf) ? CFG_TUD_CDC_TX_BUFSIZE : 0; }
uint32_t tud_cdc_n_write_flush(uint8_t itf)
{
    (void)itf;
    return 0;
}
/* O que se digita no monitor chega pela Serial (a mesma janela); o USBSerial le dela. */
uint32_t tud_cdc_n_read(uint8_t itf, void *buffer, uint32_t bufsize)
{
    if (!tud_cdc_n_connected(itf)) return 0;
    uint8_t *p = (uint8_t *)buffer;
    uint32_t n = 0;
    while (n < bufsize && cs_obj_i("Serial", "available") > 0) p[n++] = (uint8_t)cs_obj_i("Serial", "read");
    return n;
}
uint32_t tud_cdc_n_available(uint8_t itf) { return tud_cdc_n_connected(itf) ? (uint32_t)cs_obj_i("Serial", "available") : 0; }
#endif

#if CFG_TUD_MIDI
bool tud_midi_n_packet_write(uint8_t itf, const uint8_t packet[4])
{
    (void)itf;
    if (!cs_iniciado) return false;
    ch(nullptr, "__usb", cs_t("midi"), cs_i(0), cs_i(0), cs_t(cs_hex_de(packet, 4).c_str()));
    return true;
}
/* O computador nao toca nada de volta para a placa. */
bool tud_midi_n_packet_read(uint8_t itf, uint8_t packet[4])
{
    (void)itf;
    (void)packet;
    return false;
}
#endif

#if CFG_TUD_VENDOR
bool tud_vendor_n_mounted(uint8_t idx) { return cs_iniciado && idx == 0; }
uint32_t tud_vendor_n_available(uint8_t idx)
{
    (void)idx;
    return 0;
}
uint32_t tud_vendor_n_read(uint8_t idx, void *buffer, uint32_t bufsize)
{
    (void)idx;
    (void)buffer;
    (void)bufsize;
    return 0;
}
uint32_t tud_vendor_n_write(uint8_t idx, const void *buffer, uint32_t bufsize)
{
    if (!tud_vendor_n_mounted(idx)) return 0;
    ch(nullptr, "__usb", cs_t("vendor"), cs_i(0), cs_i(0), cs_t(cs_hex_de(buffer, bufsize).c_str()));
    return bufsize;
}
uint32_t tud_vendor_n_write_available(uint8_t idx) { return tud_vendor_n_mounted(idx) ? 64 : 0; }
uint32_t tud_vendor_n_write_flush(uint8_t idx)
{
    (void)idx;
    return 0;
}
#endif

#if CFG_TUD_MSC
/* O disco USB (USBMSC/FirmwareMSC): o computador da bancada nao le discos. */
bool tud_msc_set_sense(uint8_t lun, uint8_t sense_key, uint8_t add_sense_code, uint8_t add_sense_qualifier)
{
    (void)lun;
    (void)sense_key;
    (void)add_sense_code;
    (void)add_sense_qualifier;
    return true;
}
#endif

#if CFG_TUD_AUDIO
/* A placa de som USB: o audio vai e nada volta. */
uint16_t tud_audio_n_write(uint8_t func_id, const void *data, uint16_t len)
{
    (void)func_id;
    (void)data;
    return cs_iniciado ? len : 0;
}
uint16_t tud_audio_n_read(uint8_t func_id, void *buffer, uint16_t bufsize)
{
    (void)func_id;
    (void)buffer;
    (void)bufsize;
    return 0;
}
bool tud_audio_buffer_and_schedule_control_xfer(uint8_t rhport, tusb_control_request_t const *p_request, void *data, uint16_t len)
{
    (void)rhport;
    (void)p_request;
    (void)data;
    (void)len;
    return true;
}
#endif

#endif
#endif
