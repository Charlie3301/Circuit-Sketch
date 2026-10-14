/* A memoria da placa: a interna (DRAM) e a PSRAM, cada uma com o seu
 * tamanho. Pedir mais do que cabe devolve NULL, como na ESP32. */
#ifndef CS_ESP_HEAP_CAPS_H
#define CS_ESP_HEAP_CAPS_H

#include <stdint.h>
#include <stdlib.h>
#include <stdbool.h>
#include "sdkconfig.h"
#include "esp_err.h"
#include "esp_attr.h"

#define MALLOC_CAP_EXEC (1 << 0)
#define MALLOC_CAP_32BIT (1 << 1)
#define MALLOC_CAP_8BIT (1 << 2)
#define MALLOC_CAP_DMA (1 << 3)
#define MALLOC_CAP_PID2 (1 << 4)
#define MALLOC_CAP_SPIRAM (1 << 10)
#define MALLOC_CAP_INTERNAL (1 << 11)
#define MALLOC_CAP_DEFAULT (1 << 12)
#define MALLOC_CAP_IRAM_8BIT (1 << 13)
#define MALLOC_CAP_RETENTION (1 << 14)
#define MALLOC_CAP_RTCRAM (1 << 15)
#define MALLOC_CAP_INVALID (1 << 31)

typedef struct {
    size_t total_free_bytes;
    size_t total_allocated_bytes;
    size_t largest_free_block;
    size_t minimum_free_bytes;
    size_t allocated_blocks;
    size_t free_blocks;
    size_t total_blocks;
} multi_heap_info_t;

void *heap_caps_malloc(size_t n, uint32_t caps);
void *heap_caps_calloc(size_t quantos, size_t tamanho, uint32_t caps);
void *heap_caps_realloc(void *p, size_t n, uint32_t caps);
void *heap_caps_aligned_alloc(size_t alinhamento, size_t n, uint32_t caps);
void *heap_caps_malloc_prefer(size_t n, size_t quantos, ...);
void heap_caps_free(void *p);
void heap_caps_aligned_free(void *p);
size_t heap_caps_get_free_size(uint32_t caps);
size_t heap_caps_get_total_size(uint32_t caps);
size_t heap_caps_get_minimum_free_size(uint32_t caps);
size_t heap_caps_get_largest_free_block(uint32_t caps);
void heap_caps_get_info(multi_heap_info_t *info, uint32_t caps);
void heap_caps_print_heap_info(uint32_t caps);
int heap_caps_check_integrity_all(int imprimir);
int heap_caps_check_integrity(uint32_t caps, int imprimir);

#endif
