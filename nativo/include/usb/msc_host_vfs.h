/* usb/msc_host_vfs.h do CircuitSketch: a FAT do pendrive no VFS (fopen e opendir). */
#ifndef MSC_HOST_VFS_H
#define MSC_HOST_VFS_H

#include "esp_vfs_fat.h"
#include "usb/msc_host.h"

#ifdef __cplusplus
extern "C" {
#endif

typedef struct msc_host_vfs *msc_host_vfs_handle_t;

esp_err_t msc_host_vfs_register(msc_host_device_handle_t device, const char *base_path,
                                const esp_vfs_fat_mount_config_t *mount_config, msc_host_vfs_handle_t *vfs_handle);
esp_err_t msc_host_vfs_unregister(msc_host_vfs_handle_t vfs_handle);

#ifdef __cplusplus
}
#endif

#endif
