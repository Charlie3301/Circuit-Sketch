/* esp_vfs_fat.h: montar o cartao (ou a flash) num caminho, e dali em
 * diante fopen("/sdcard/DOOM1.WAD", "rb") le o arquivo do cartao. */
#ifndef CS_ESP_VFS_FAT_H
#define CS_ESP_VFS_FAT_H

#include <stdint.h>
#include <stdbool.h>
#include <stddef.h>
#include "esp_err.h"
#include "driver/sdmmc_types.h"
#include "driver/sdspi_host.h"
#include "driver/sdmmc_host.h"

typedef struct {
    bool format_if_mount_failed;
    int max_files;
    size_t allocation_unit_size;
    bool disk_status_check_enable;
    bool use_one_fat;
} esp_vfs_fat_mount_config_t;

typedef esp_vfs_fat_mount_config_t esp_vfs_fat_sdmmc_mount_config_t;

#define VFS_FAT_MOUNT_DEFAULT_CONFIG() { \
    .format_if_mount_failed = false, \
    .max_files = 5, \
    .allocation_unit_size = 0, \
    .disk_status_check_enable = false, \
    .use_one_fat = false, \
}

typedef int32_t wl_handle_t;
#define WL_INVALID_HANDLE -1

esp_err_t esp_vfs_fat_sdspi_mount(const char *ponto, const sdmmc_host_t *host,
                                  const sdspi_device_config_t *slot,
                                  const esp_vfs_fat_mount_config_t *config, sdmmc_card_t **cartao);
esp_err_t esp_vfs_fat_sdmmc_mount(const char *ponto, const sdmmc_host_t *host, const void *slot,
                                  const esp_vfs_fat_mount_config_t *config, sdmmc_card_t **cartao);
esp_err_t esp_vfs_fat_sdcard_unmount(const char *ponto, sdmmc_card_t *cartao);
esp_err_t esp_vfs_fat_sdmmc_unmount(void);
esp_err_t esp_vfs_fat_spiflash_mount_rw_wl(const char *ponto, const char *particao,
                                           const esp_vfs_fat_mount_config_t *config, wl_handle_t *alca);
esp_err_t esp_vfs_fat_spiflash_unmount_rw_wl(const char *ponto, wl_handle_t alca);
esp_err_t esp_vfs_fat_spiflash_mount_ro(const char *ponto, const char *particao,
                                        const esp_vfs_fat_mount_config_t *config);
esp_err_t esp_vfs_fat_spiflash_unmount_ro(const char *ponto, const char *particao);
esp_err_t esp_vfs_fat_info(const char *ponto, uint64_t *total, uint64_t *livre);

#endif
