/* usb/usb_host.h do CircuitSketch: o driver USB host do ESP-IDF, no que um
 * pendrive precisa (o resto do protocolo fica com o simulador). */
#ifndef USB_HOST_H
#define USB_HOST_H

#include <stdint.h>
#include <stdbool.h>
#include "esp_err.h"
#include "freertos/FreeRTOS.h"

#ifdef __cplusplus
extern "C" {
#endif

#define USB_HOST_LIB_EVENT_FLAGS_NO_CLIENTS 0x01
#define USB_HOST_LIB_EVENT_FLAGS_ALL_FREE   0x02

typedef struct {
    bool skip_phy_setup;
    bool root_port_unpowered;
    int intr_flags;
    void *enum_filter_cb;
    int fifo_settings_custom;
    int peripheral_map;
} usb_host_config_t;

esp_err_t usb_host_install(const usb_host_config_t *config);
esp_err_t usb_host_uninstall(void);
esp_err_t usb_host_lib_handle_events(TickType_t timeout_ticks, uint32_t *event_flags_ret);
esp_err_t usb_host_device_free_all(void);

#ifdef __cplusplus
}
#endif

#endif
