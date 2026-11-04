/* esp_vfs_fat.h -- o do IDF, sem o caminho de Windows do FatFs (ver ff.h). */
#ifndef CS_ESP_VFS_FAT_H
#define CS_ESP_VFS_FAT_H
#ifndef MAX
#define MAX(a, b) (((a) > (b)) ? (a) : (b))
#endif
#ifndef MIN
#define MIN(a, b) (((a) < (b)) ? (a) : (b))
#endif
#include <stddef.h>
#include <stdint.h>
#include <sys/types.h>
#include "esp_err.h"
#include "sd_protocol_types.h"
#include "driver/sdspi_host.h"
#include "wear_levelling.h"
#pragma push_macro("_WIN32")
#undef _WIN32
#include_next <esp_vfs_fat.h>
#pragma pop_macro("_WIN32")
#endif
