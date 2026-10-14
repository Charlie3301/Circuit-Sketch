/* cs_usb.c -- o USB host do ESP-IDF e a classe de armazenamento em massa
 * (usb_host_msc), por cima do pendrive do circuito.
 *
 * Nao ha enumeracao nem SCSI de verdade: uma tarefa olha se ha pendrive
 * ligado nos D-/D+ da placa (e conectado) e avisa o callback com
 * MSC_DEVICE_CONNECTED e MSC_DEVICE_DISCONNECTED -- o mesmo que o driver
 * faz. O msc_host_vfs_register monta a FAT do pendrive no caminho pedido. */
#include <stdint.h>
#include <string.h>
#include "freertos/FreeRTOS.h"
#include "freertos/task.h"
#include "usb/usb_host.h"
#include "usb/msc_host.h"
#include "usb/msc_host_vfs.h"
#include "cs_host.h"
#include "cs_interno.h"

struct msc_host_device { int endereco; };
struct msc_host_vfs { char ponto[32]; };

static int host_instalado, msc_instalado, conectado;
static msc_host_driver_config_t config;
static struct msc_host_device o_aparelho = { 1 };
static struct msc_host_vfs a_montagem;

esp_err_t usb_host_install(const usb_host_config_t *c)
{
    (void)c;
    if (host_instalado) return ESP_ERR_INVALID_STATE;
    host_instalado = 1;
    return ESP_OK;
}

esp_err_t usb_host_uninstall(void)
{
    if (!host_instalado) return ESP_ERR_INVALID_STATE;
    host_instalado = 0;
    return ESP_OK;
}

esp_err_t usb_host_lib_handle_events(TickType_t espera, uint32_t *bandeiras)
{
    if (bandeiras) *bandeiras = 0;
    if (!host_instalado) return ESP_ERR_INVALID_STATE;
    /* sem nada para atender: espera, como o driver */
    vTaskDelay(espera == portMAX_DELAY ? pdMS_TO_TICKS(1000) : espera);
    return ESP_ERR_TIMEOUT;
}

esp_err_t usb_host_device_free_all(void) { return ESP_OK; }

/* Olha o pendrive e avisa quando ele entra ou sai. */
static void olhar(void)
{
    int agora = cs->usb_pendrive();
    if (agora == conectado) return;
    conectado = agora;
    msc_host_event_t e;
    memset(&e, 0, sizeof(e));
    if (agora) {
        e.event = MSC_DEVICE_CONNECTED;
        e.device.address = 1;
    } else {
        e.event = MSC_DEVICE_DISCONNECTED;
        e.device.handle = &o_aparelho;
    }
    if (config.callback) config.callback(&e, config.callback_arg);
}

static void tarefa_msc(void *arg)
{
    (void)arg;
    while (msc_instalado) {
        olhar();
        vTaskDelay(pdMS_TO_TICKS(200));
    }
    vTaskDelete(NULL);
}

esp_err_t msc_host_install(const msc_host_driver_config_t *c)
{
    if (!c || !c->callback) return ESP_ERR_INVALID_ARG;
    if (!host_instalado) return ESP_ERR_INVALID_STATE;
    if (msc_instalado) return ESP_ERR_INVALID_STATE;
    config = *c;
    msc_instalado = 1;
    conectado = 0;
    if (c->create_backround_task)
        xTaskCreatePinnedToCore(tarefa_msc, "usb_msc", c->stack_size ? (uint32_t)c->stack_size : 4096, NULL,
                                c->task_priority ? (UBaseType_t)c->task_priority : 5, NULL, c->core_id);
    return ESP_OK;
}

esp_err_t msc_host_uninstall(void)
{
    if (!msc_instalado) return ESP_ERR_INVALID_STATE;
    msc_instalado = 0;
    return ESP_OK;
}

esp_err_t msc_host_handle_events(TickType_t espera)
{
    if (!msc_instalado) return ESP_ERR_INVALID_STATE;
    olhar();
    vTaskDelay(espera);
    return ESP_OK;
}

esp_err_t msc_host_install_device(uint8_t endereco, msc_host_device_handle_t *aparelho)
{
    if (!aparelho) return ESP_ERR_INVALID_ARG;
    if (!conectado || endereco != 1) return ESP_ERR_NOT_FOUND;
    *aparelho = &o_aparelho;
    return ESP_OK;
}

esp_err_t msc_host_uninstall_device(msc_host_device_handle_t aparelho)
{
    return aparelho ? ESP_OK : ESP_ERR_INVALID_ARG;
}

esp_err_t msc_host_get_device_info(msc_host_device_handle_t aparelho, msc_host_device_info_t *info)
{
    if (!aparelho || !info) return ESP_ERR_INVALID_ARG;
    memset(info, 0, sizeof(*info));
    info->sector_size = 512;
    info->sector_count = 32u * 1024 * 1024 * 2;   /* um pendrive de 32 GB */
    info->idVendor = 0x0781;
    info->idProduct = 0x5567;
    const char *nome = "Pendrive USB";
    for (int i = 0; nome[i] && i < MSC_STR_DESC_SIZE - 1; i++) info->iProduct[i] = (wchar_t)nome[i];
    return ESP_OK;
}

esp_err_t msc_host_print_descriptors(msc_host_device_handle_t aparelho)
{
    return aparelho ? ESP_OK : ESP_ERR_INVALID_ARG;
}

esp_err_t msc_host_vfs_register(msc_host_device_handle_t aparelho, const char *ponto,
                                const esp_vfs_fat_mount_config_t *m, msc_host_vfs_handle_t *montagem)
{
    (void)m;
    if (!aparelho || !ponto || !montagem) return ESP_ERR_INVALID_ARG;
    int r = cs->montar(ponto, CS_MONTAR_USB, -1, -1, NULL);
    if (r != ESP_OK) return ESP_ERR_MSC_MOUNT_FAILED;
    strncpy(a_montagem.ponto, ponto, sizeof(a_montagem.ponto) - 1);
    *montagem = &a_montagem;
    return ESP_OK;
}

esp_err_t msc_host_vfs_unregister(msc_host_vfs_handle_t montagem)
{
    if (!montagem) return ESP_ERR_INVALID_ARG;
    cs->desmontar(montagem->ponto);
    return ESP_OK;
}
