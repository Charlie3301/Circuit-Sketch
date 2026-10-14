/* usb/msc_host.h do CircuitSketch: a classe de armazenamento em massa do
 * componente usb_host_msc da Espressif (a API dele). O pendrive do circuito
 * e o aparelho; a FAT dele, a pasta ou os arquivos que a peca aponta. */
#ifndef MSC_HOST_H
#define MSC_HOST_H

#include <stdint.h>
#include <stdbool.h>
#include <stddef.h>
#include <wchar.h>
#include "esp_err.h"
#include "usb/usb_host.h"
#include "freertos/FreeRTOS.h"

#ifdef __cplusplus
extern "C" {
#endif

#define ESP_ERR_MSC_HOST_BASE 0x1700
#define ESP_ERR_MSC_MOUNT_FAILED (ESP_ERR_MSC_HOST_BASE + 1)
#define ESP_ERR_MSC_FORMAT_FAILED (ESP_ERR_MSC_HOST_BASE + 2)
#define ESP_ERR_MSC_INTERNAL (ESP_ERR_MSC_HOST_BASE + 3)
#define ESP_ERR_MSC_STALL (ESP_ERR_MSC_HOST_BASE + 4)
#define MSC_STR_DESC_SIZE 32

typedef struct msc_host_device *msc_host_device_handle_t;

typedef struct {
    enum {
        MSC_DEVICE_CONNECTED,
        MSC_DEVICE_DISCONNECTED,
    } event;
    union {
        uint8_t address;
        msc_host_device_handle_t handle;
    } device;
} msc_host_event_t;

typedef void (*msc_host_event_cb_t)(const msc_host_event_t *event, void *arg);

typedef struct {
    bool create_backround_task;
    size_t task_priority;
    size_t stack_size;
    BaseType_t core_id;
    msc_host_event_cb_t callback;
    void *callback_arg;
} msc_host_driver_config_t;

typedef struct {
    uint32_t sector_count;
    uint32_t sector_size;
    uint16_t idProduct;
    uint16_t idVendor;
    wchar_t iManufacturer[MSC_STR_DESC_SIZE];
    wchar_t iProduct[MSC_STR_DESC_SIZE];
    wchar_t iSerialNumber[MSC_STR_DESC_SIZE];
} msc_host_device_info_t;

esp_err_t msc_host_install(const msc_host_driver_config_t *config);
esp_err_t msc_host_uninstall(void);
esp_err_t msc_host_install_device(uint8_t device_address, msc_host_device_handle_t *device);
esp_err_t msc_host_uninstall_device(msc_host_device_handle_t device);
esp_err_t msc_host_handle_events(TickType_t timeout);
esp_err_t msc_host_get_device_info(msc_host_device_handle_t device, msc_host_device_info_t *info);
esp_err_t msc_host_print_descriptors(msc_host_device_handle_t device);

#ifdef __cplusplus
}
#endif

#endif
