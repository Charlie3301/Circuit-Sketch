#ifndef CS_SPI_FLASH_MMAP_H
#define CS_SPI_FLASH_MMAP_H

#include <stdint.h>
#include <stddef.h>
#include "esp_err.h"

typedef uint32_t spi_flash_mmap_handle_t;
typedef enum { SPI_FLASH_MMAP_DATA, SPI_FLASH_MMAP_INST } spi_flash_mmap_memory_t;
#define SPI_FLASH_SEC_SIZE 4096
#define SPI_FLASH_MMU_PAGE_SIZE 0x10000

esp_err_t spi_flash_mmap(size_t endereco, size_t n, spi_flash_mmap_memory_t memoria,
                         const void **ponteiro, spi_flash_mmap_handle_t *alca);
void spi_flash_munmap(spi_flash_mmap_handle_t alca);

#endif
