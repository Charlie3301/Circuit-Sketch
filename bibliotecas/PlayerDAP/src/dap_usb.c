/* dap_usb.c -- o pendrive na USB da S3, lido como um segundo cartao.
 *
 * A S3 tem USB OTG nos GPIO 19 (D-) e 20 (D+): como host, ela fala com um
 * pendrive pelo driver USB do ESP-IDF e pela classe de armazenamento em massa
 * (usb_host_msc, da Espressif), que monta o FAT dele em /usb -- dai em diante
 * e fopen/opendir como no cartao. Conectar e tirar com o player ligado funciona:
 * a biblioteca e lida de novo.
 *
 * Na placa de verdade o pendrive precisa de 5 V no VBUS da porta: na
 * DevKitC-1, a ponte "USB-OTG" soldada; ou um cabo OTG com alimentacao.
 */
#include <stdio.h>
#include <string.h>
#include "freertos/FreeRTOS.h"
#include "freertos/task.h"
#include "freertos/queue.h"
#include "dap_interno.h"

/* O usb_host_msc (componente da Espressif) nao vem no core do Arduino: com
 * ele no projeto, ou no simulador, o pendrive funciona; sem ele, so o cartao. */
#if defined(__TINYC__)
#define DAP_COM_USB 1
#elif defined(__has_include)
#if __has_include("usb/msc_host_vfs.h")
#define DAP_COM_USB 1
#endif
#endif

volatile int dap_usb_mudou;

#ifndef DAP_COM_USB
void dap_usb_iniciar(void)
{
    if (dap_cfg.usb) printf("DAP: sem o componente usb_host_msc o pendrive fica de fora; so o cartao vai ser lido\n");
}
#else

#include "esp_intr_alloc.h"
#include "esp_vfs_fat.h"
#include "usb/usb_host.h"
#include "usb/msc_host.h"
#include "usb/msc_host_vfs.h"

static QueueHandle_t eventos;
static msc_host_device_handle_t aparelho;
static msc_host_vfs_handle_t montagem;

static void ao_evento(const msc_host_event_t *e, void *arg)
{
    (void)arg;
    if (eventos) xQueueSend(eventos, e, 0);
}

/* O driver USB do ESP-IDF precisa de quem atenda os eventos dele. */
static void tarefa_da_usb(void *arg)
{
    (void)arg;
    for (;;) {
        uint32_t bandeiras = 0;
        usb_host_lib_handle_events(portMAX_DELAY, &bandeiras);
        if (bandeiras & USB_HOST_LIB_EVENT_FLAGS_NO_CLIENTS) usb_host_device_free_all();
    }
}

static void tarefa_do_pendrive(void *arg)
{
    (void)arg;
    msc_host_event_t e;
    for (;;) {
        if (xQueueReceive(eventos, &e, portMAX_DELAY) != pdTRUE) continue;
        if (e.event == MSC_DEVICE_CONNECTED) {
            if (aparelho) continue;
            if (msc_host_install_device(e.device.address, &aparelho) != ESP_OK) { aparelho = NULL; continue; }
            esp_vfs_fat_mount_config_t m = { .format_if_mount_failed = false, .max_files = 4, .allocation_unit_size = 16 * 1024 };
            if (msc_host_vfs_register(aparelho, DAP_RAIZ_USB, &m, &montagem) == ESP_OK) {
                msc_host_device_info_t info;
                if (msc_host_get_device_info(aparelho, &info) == ESP_OK)
                    printf("DAP: pendrive de %u MB em %s\n", (unsigned)((uint64_t)info.sector_count * info.sector_size >> 20), DAP_RAIZ_USB);
                dap_usb_ok = 1;
                dap_usb_mudou = 1;
            } else {
                printf("DAP: o pendrive nao tem um FAT que de para ler\n");
                msc_host_uninstall_device(aparelho);
                aparelho = NULL;
            }
        } else if (e.event == MSC_DEVICE_DISCONNECTED) {
            if (montagem) msc_host_vfs_unregister(montagem);
            if (aparelho) msc_host_uninstall_device(aparelho);
            montagem = NULL;
            aparelho = NULL;
            if (dap_usb_ok) {
                dap_usb_ok = 0;
                dap_usb_mudou = 1;
                printf("DAP: o pendrive saiu\n");
            }
        }
    }
}

void dap_usb_iniciar(void)
{
    if (!dap_cfg.usb) return;
    usb_host_config_t host = { .skip_phy_setup = false, .intr_flags = ESP_INTR_FLAG_LEVEL1 };
    if (usb_host_install(&host) != ESP_OK) {
        printf("DAP: o USB host nao iniciou\n");
        return;
    }
    eventos = xQueueCreate(4, sizeof(msc_host_event_t));
    xTaskCreatePinnedToCore(tarefa_da_usb, "dap_usb", 4096, NULL, 5, NULL, 0);
    msc_host_driver_config_t msc = {
        .create_backround_task = true,
        .task_priority = 5,
        .stack_size = 4096,
        .core_id = 0,
        .callback = ao_evento,
        .callback_arg = NULL,
    };
    if (msc_host_install(&msc) != ESP_OK) {
        printf("DAP: o driver do pendrive nao iniciou\n");
        return;
    }
    xTaskCreatePinnedToCore(tarefa_do_pendrive, "dap_pendrive", 4096, NULL, 5, NULL, 0);
    /* um pendrive ja conectado leva um instante para ser reconhecido */
    for (int i = 0; i < 30 && !dap_usb_ok; i++) vTaskDelay(pdMS_TO_TICKS(50));
}

#endif
