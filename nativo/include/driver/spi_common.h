#ifndef CS_DRIVER_SPI_COMMON_H
#define CS_DRIVER_SPI_COMMON_H

#include <stdint.h>
#include <stdbool.h>
#include "esp_err.h"
#include "esp_intr_alloc.h"
#include "hal/spi_types.h"
#include "sdkconfig.h"

#define SPI_MAX_DMA_LEN (4096 - 4)
#define SPI_SWAP_DATA_TX(DATA, LEN) __builtin_bswap32((uint32_t)(DATA) << (32 - (LEN)))
#define SPI_SWAP_DATA_RX(DATA, LEN) (__builtin_bswap32(DATA) >> (32 - (LEN)))

#define SPICOMMON_BUSFLAG_SLAVE 0
#define SPICOMMON_BUSFLAG_MASTER (1 << 0)
#define SPICOMMON_BUSFLAG_IOMUX_PINS (1 << 1)
#define SPICOMMON_BUSFLAG_GPIO_PINS (1 << 2)
#define SPICOMMON_BUSFLAG_SCLK (1 << 3)
#define SPICOMMON_BUSFLAG_MISO (1 << 4)
#define SPICOMMON_BUSFLAG_MOSI (1 << 5)
#define SPICOMMON_BUSFLAG_DUAL (1 << 6)
#define SPICOMMON_BUSFLAG_WPHD (1 << 7)
#define SPICOMMON_BUSFLAG_QUAD (SPICOMMON_BUSFLAG_DUAL | SPICOMMON_BUSFLAG_WPHD)
#define SPICOMMON_BUSFLAG_IO4_IO7 (1 << 8)
#define SPICOMMON_BUSFLAG_OCTAL (SPICOMMON_BUSFLAG_QUAD | SPICOMMON_BUSFLAG_IO4_IO7)
#define SPICOMMON_BUSFLAG_NATIVE_PINS SPICOMMON_BUSFLAG_IOMUX_PINS

typedef enum {
    SPI_DMA_DISABLED = 0,
    SPI_DMA_CH1 = 1,
    SPI_DMA_CH2 = 2,
    SPI_DMA_CH_AUTO = 3,
} spi_common_dma_t;
typedef spi_common_dma_t spi_dma_chan_t;

typedef struct {
    union { int mosi_io_num; int data0_io_num; };
    union { int miso_io_num; int data1_io_num; };
    int sclk_io_num;
    union { int quadwp_io_num; int data2_io_num; };
    union { int quadhd_io_num; int data3_io_num; };
    int data4_io_num;
    int data5_io_num;
    int data6_io_num;
    int data7_io_num;
    int max_transfer_sz;
    uint32_t flags;
    esp_intr_cpu_affinity_t isr_cpu_id;
    int intr_flags;
} spi_bus_config_t;

esp_err_t spi_bus_initialize(spi_host_device_t host, const spi_bus_config_t *config, spi_dma_chan_t dma);
esp_err_t spi_bus_free(spi_host_device_t host);
void *spi_bus_dma_memory_alloc(spi_host_device_t host, size_t n, uint32_t caps_extra);
esp_err_t spi_bus_get_max_transaction_len(spi_host_device_t host, size_t *n);

#endif
