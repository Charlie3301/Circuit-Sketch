/* driver/i2s.h: o driver ANTIGO do I2S, que o ESP-IDF 5 ainda traz (com
 * aviso de obsoleto). O DAC interno so existe no ESP32 classico. */
#ifndef CS_DRIVER_I2S_LEGADO_H
#define CS_DRIVER_I2S_LEGADO_H

#include "driver/i2s_types.h"
#include "driver/gpio.h"
#include "freertos/FreeRTOS.h"
#include "sdkconfig.h"

typedef enum {
    I2S_MODE_MASTER = 1 << 0,
    I2S_MODE_SLAVE = 1 << 1,
    I2S_MODE_TX = 1 << 2,
    I2S_MODE_RX = 1 << 3,
#if defined(CONFIG_IDF_TARGET_ESP32)
    I2S_MODE_DAC_BUILT_IN = 1 << 4,
    I2S_MODE_ADC_BUILT_IN = 1 << 5,
#endif
    I2S_MODE_PDM = 1 << 6,
} i2s_mode_t;

typedef enum {
    I2S_BITS_PER_SAMPLE_8BIT = 8,
    I2S_BITS_PER_SAMPLE_16BIT = 16,
    I2S_BITS_PER_SAMPLE_24BIT = 24,
    I2S_BITS_PER_SAMPLE_32BIT = 32,
} i2s_bits_per_sample_t;

typedef enum {
    I2S_BITS_PER_CHAN_DEFAULT = 0,
    I2S_BITS_PER_CHAN_8BIT = 8,
    I2S_BITS_PER_CHAN_16BIT = 16,
    I2S_BITS_PER_CHAN_24BIT = 24,
    I2S_BITS_PER_CHAN_32BIT = 32,
} i2s_bits_per_chan_t;

typedef enum {
    I2S_CHANNEL_FMT_RIGHT_LEFT,
    I2S_CHANNEL_FMT_ALL_RIGHT,
    I2S_CHANNEL_FMT_ALL_LEFT,
    I2S_CHANNEL_FMT_ONLY_RIGHT,
    I2S_CHANNEL_FMT_ONLY_LEFT,
    I2S_CHANNEL_FMT_MULTIPLE,
} i2s_channel_fmt_t;

typedef enum {
    I2S_COMM_FORMAT_STAND_I2S = 0x01,
    I2S_COMM_FORMAT_STAND_MSB = 0x01 | 0x02,
    I2S_COMM_FORMAT_STAND_PCM_SHORT = 0x04,
    I2S_COMM_FORMAT_STAND_PCM_LONG = 0x04 | 0x08,
    I2S_COMM_FORMAT_STAND_MAX,
    I2S_COMM_FORMAT_I2S = 0x01,
    I2S_COMM_FORMAT_I2S_MSB = 0x01,
    I2S_COMM_FORMAT_I2S_LSB = 0x02,
    I2S_COMM_FORMAT_PCM = 0x04,
    I2S_COMM_FORMAT_PCM_SHORT = 0x04,
    I2S_COMM_FORMAT_PCM_LONG = 0x08,
} i2s_comm_format_t;

typedef enum { I2S_CHANNEL_MONO = 1, I2S_CHANNEL_STEREO = 2 } i2s_channel_t;

#if defined(CONFIG_IDF_TARGET_ESP32)
typedef enum {
    I2S_DAC_CHANNEL_DISABLE = 0,
    I2S_DAC_CHANNEL_RIGHT_EN = 1,
    I2S_DAC_CHANNEL_LEFT_EN = 2,
    I2S_DAC_CHANNEL_BOTH_EN = 3,
    I2S_DAC_CHANNEL_MAX = 4,
} i2s_dac_mode_t;
#endif

typedef struct {
    i2s_mode_t mode;
    uint32_t sample_rate;
    i2s_bits_per_sample_t bits_per_sample;
    i2s_channel_fmt_t channel_format;
    i2s_comm_format_t communication_format;
    int intr_alloc_flags;
    union { int dma_desc_num; int dma_buf_count; };
    union { int dma_frame_num; int dma_buf_len; };
    bool use_apll;
    bool tx_desc_auto_clear;
    int fixed_mclk;
    i2s_mclk_multiple_t mclk_multiple;
    i2s_bits_per_chan_t bits_per_chan;
} i2s_driver_config_t;
typedef i2s_driver_config_t i2s_config_t;

#define I2S_PIN_NO_CHANGE (-1)

typedef struct {
    int mck_io_num;
    int bck_io_num;
    int ws_io_num;
    int data_out_num;
    int data_in_num;
} i2s_pin_config_t;

typedef enum { I2S_EVENT_DMA_ERROR, I2S_EVENT_TX_DONE, I2S_EVENT_RX_DONE, I2S_EVENT_TX_Q_OVF,
               I2S_EVENT_RX_Q_OVF, I2S_EVENT_MAX } i2s_event_type_t;
typedef struct { i2s_event_type_t type; size_t size; } i2s_event_t;

esp_err_t i2s_driver_install(i2s_port_t porta, const i2s_config_t *config, int fila_tamanho, void *fila);
esp_err_t i2s_driver_uninstall(i2s_port_t porta);
esp_err_t i2s_set_pin(i2s_port_t porta, const i2s_pin_config_t *pinos);
esp_err_t i2s_write(i2s_port_t porta, const void *dados, size_t n, size_t *escritos, TickType_t espera);
esp_err_t i2s_write_expand(i2s_port_t porta, const void *dados, size_t n, size_t bits_de, size_t bits_para,
                           size_t *escritos, TickType_t espera);
esp_err_t i2s_read(i2s_port_t porta, void *dados, size_t n, size_t *lidos, TickType_t espera);
esp_err_t i2s_set_sample_rates(i2s_port_t porta, uint32_t taxa);
esp_err_t i2s_set_clk(i2s_port_t porta, uint32_t taxa, uint32_t bits, i2s_channel_t canais);
float i2s_get_clk(i2s_port_t porta);
esp_err_t i2s_start(i2s_port_t porta);
esp_err_t i2s_stop(i2s_port_t porta);
esp_err_t i2s_zero_dma_buffer(i2s_port_t porta);
#if defined(CONFIG_IDF_TARGET_ESP32)
esp_err_t i2s_set_dac_mode(i2s_dac_mode_t modo);
#endif

#endif
