#ifndef CS_HAL_SPI_TYPES_H
#define CS_HAL_SPI_TYPES_H

#include "sdkconfig.h"

typedef enum {
    SPI1_HOST = 0,
    SPI2_HOST = 1,
    SPI3_HOST = 2,
    SPI_HOST_MAX,
} spi_host_device_t;

/* Os nomes antigos existem so no ESP32 classico, como no ESP-IDF 5. */
#if defined(CONFIG_IDF_TARGET_ESP32)
#define SPI_HOST SPI1_HOST
#define HSPI_HOST SPI2_HOST
#define VSPI_HOST SPI3_HOST
#elif defined(CONFIG_IDF_TARGET_ESP32S2) || defined(CONFIG_IDF_TARGET_ESP32S3)
#define SPI_HOST SPI1_HOST
#define FSPI_HOST SPI2_HOST
#define HSPI_HOST SPI3_HOST
#endif

typedef enum {
    SPI_CMD_HD_WRBUF = 1 << 0,
    SPI_CMD_HD_RDBUF = 1 << 1,
} spi_command_t;

#endif
