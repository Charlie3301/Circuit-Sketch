/* As particoes da flash. A que o simulador conhece e a que tem um arquivo
 * gravado nela -- o WAD do DOOM, por exemplo, na particao "doom_wad". */
#ifndef CS_ESP_PARTITION_H
#define CS_ESP_PARTITION_H

#include <stdint.h>
#include <stddef.h>
#include <stdbool.h>
#include "esp_err.h"

typedef enum { ESP_PARTITION_TYPE_APP = 0x00, ESP_PARTITION_TYPE_DATA = 0x01,
               ESP_PARTITION_TYPE_ANY = 0xff } esp_partition_type_t;
typedef enum {
    ESP_PARTITION_SUBTYPE_APP_FACTORY = 0x00,
    ESP_PARTITION_SUBTYPE_APP_OTA_0 = 0x10,
    ESP_PARTITION_SUBTYPE_DATA_OTA = 0x00,
    ESP_PARTITION_SUBTYPE_DATA_PHY = 0x01,
    ESP_PARTITION_SUBTYPE_DATA_NVS = 0x02,
    ESP_PARTITION_SUBTYPE_DATA_COREDUMP = 0x03,
    ESP_PARTITION_SUBTYPE_DATA_FAT = 0x81,
    ESP_PARTITION_SUBTYPE_DATA_SPIFFS = 0x82,
    ESP_PARTITION_SUBTYPE_DATA_LITTLEFS = 0x83,
    ESP_PARTITION_SUBTYPE_ANY = 0xff
} esp_partition_subtype_t;
typedef enum { ESP_PARTITION_MMAP_DATA, ESP_PARTITION_MMAP_INST } esp_partition_mmap_memory_t;
typedef uint32_t esp_partition_mmap_handle_t;
typedef struct esp_flash_t esp_flash_t;

typedef struct {
    esp_flash_t *flash_chip;
    esp_partition_type_t type;
    esp_partition_subtype_t subtype;
    uint32_t address;
    uint32_t size;
    uint32_t erase_size;
    char label[17];
    bool encrypted;
    bool readonly;
    int64_t __cs_particao;
} esp_partition_t;

typedef struct cs_iterador_particao *esp_partition_iterator_t;

const esp_partition_t *esp_partition_find_first(esp_partition_type_t tipo, esp_partition_subtype_t subtipo,
                                                const char *rotulo);
esp_partition_iterator_t esp_partition_find(esp_partition_type_t tipo, esp_partition_subtype_t subtipo,
                                            const char *rotulo);
const esp_partition_t *esp_partition_get(esp_partition_iterator_t it);
esp_partition_iterator_t esp_partition_next(esp_partition_iterator_t it);
void esp_partition_iterator_release(esp_partition_iterator_t it);
esp_err_t esp_partition_read(const esp_partition_t *p, size_t desloc, void *buf, size_t n);
esp_err_t esp_partition_read_raw(const esp_partition_t *p, size_t desloc, void *buf, size_t n);
esp_err_t esp_partition_write(const esp_partition_t *p, size_t desloc, const void *buf, size_t n);
esp_err_t esp_partition_erase_range(const esp_partition_t *p, size_t desloc, size_t n);
esp_err_t esp_partition_mmap(const esp_partition_t *p, size_t desloc, size_t n,
                             esp_partition_mmap_memory_t memoria, const void **ponteiro,
                             esp_partition_mmap_handle_t *alca);
void esp_partition_munmap(esp_partition_mmap_handle_t alca);

#endif
