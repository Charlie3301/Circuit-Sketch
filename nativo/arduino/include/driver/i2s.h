/* driver/i2s.h -- o I2S do ESP-IDF (o "legado", o do core 2.x): o som vai ao
 * amplificador I2S do circuito (ver cs_i2s.h). */
#ifndef CS_DRIVER_I2S_H
#define CS_DRIVER_I2S_H

#include "cs_idf.h"
#include "cs_i2s.h"

#ifdef __cplusplus
extern "C" {
#endif

typedef enum { I2S_NUM_0 = 0, I2S_NUM_1 = 1, I2S_NUM_MAX } i2s_port_t;
typedef enum { I2S_MODE_MASTER = 1, I2S_MODE_SLAVE = 2, I2S_MODE_TX = 4, I2S_MODE_RX = 8, I2S_MODE_DAC_BUILT_IN = 16,
               I2S_MODE_ADC_BUILT_IN = 32, I2S_MODE_PDM = 64 } i2s_mode_t;
typedef enum { I2S_BITS_PER_SAMPLE_8BIT = 8, I2S_BITS_PER_SAMPLE_16BIT = 16, I2S_BITS_PER_SAMPLE_24BIT = 24,
               I2S_BITS_PER_SAMPLE_32BIT = 32 } i2s_bits_per_sample_t;
typedef enum { I2S_CHANNEL_FMT_RIGHT_LEFT, I2S_CHANNEL_FMT_ALL_RIGHT, I2S_CHANNEL_FMT_ALL_LEFT, I2S_CHANNEL_FMT_ONLY_RIGHT,
               I2S_CHANNEL_FMT_ONLY_LEFT } i2s_channel_fmt_t;
typedef enum { I2S_CHANNEL_MONO = 1, I2S_CHANNEL_STEREO = 2 } i2s_channel_t;
typedef enum { I2S_COMM_FORMAT_STAND_I2S = 0x01, I2S_COMM_FORMAT_STAND_MSB = 0x03, I2S_COMM_FORMAT_STAND_PCM_SHORT = 0x04,
               I2S_COMM_FORMAT_I2S = 0x01, I2S_COMM_FORMAT_I2S_MSB = 0x03, I2S_COMM_FORMAT_PCM = 0x04 } i2s_comm_format_t;
typedef enum { I2S_PIN_NO_CHANGE = -1 } i2s_pin_t;
#define I2S_PIN_NO_CHANGE (-1)

typedef struct {
    int mode;
    uint32_t sample_rate;
    i2s_bits_per_sample_t bits_per_sample;
    i2s_channel_fmt_t channel_format;
    int communication_format;
    int intr_alloc_flags;
    int dma_buf_count;
    int dma_buf_len;
    bool use_apll;
    bool tx_desc_auto_clear;
    int fixed_mclk;
    int mclk_multiple;
    int bits_per_chan;
} i2s_config_t;

typedef struct {
    int mck_io_num;
    int bck_io_num;
    int ws_io_num;
    int data_out_num;
    int data_in_num;
} i2s_pin_config_t;

esp_err_t i2s_driver_install(i2s_port_t i2s_num, const i2s_config_t *i2s_config, int queue_size, void *i2s_queue);
esp_err_t i2s_driver_uninstall(i2s_port_t i2s_num);
esp_err_t i2s_set_pin(i2s_port_t i2s_num, const i2s_pin_config_t *pin);
esp_err_t i2s_write(i2s_port_t i2s_num, const void *src, size_t size, size_t *bytes_written, TickType_t ticks_to_wait);
esp_err_t i2s_read(i2s_port_t i2s_num, void *dest, size_t size, size_t *bytes_read, TickType_t ticks_to_wait);
esp_err_t i2s_zero_dma_buffer(i2s_port_t i2s_num);
esp_err_t i2s_set_clk(i2s_port_t i2s_num, uint32_t rate, uint32_t bits_cfg, i2s_channel_t ch);
esp_err_t i2s_set_sample_rates(i2s_port_t i2s_num, uint32_t rate);
esp_err_t i2s_start(i2s_port_t i2s_num);
esp_err_t i2s_stop(i2s_port_t i2s_num);
float i2s_get_clk(i2s_port_t i2s_num);
esp_err_t i2s_write_expand(i2s_port_t i2s_num, const void *src, size_t size, size_t src_bits, size_t aim_bits, size_t *bytes_written, TickType_t ticks_to_wait);

/* O DAC e o ADC internos pelo I2S, o PCM e o PDM: a saida daqui e o amplificador do circuito, e os
 * ajustes so sao aceitos. */
typedef enum { I2S_DAC_CHANNEL_DISABLE = 0, I2S_DAC_CHANNEL_RIGHT_EN = 1, I2S_DAC_CHANNEL_LEFT_EN = 2, I2S_DAC_CHANNEL_BOTH_EN = 3, I2S_DAC_CHANNEL_MAX } i2s_dac_mode_t;
typedef enum { I2S_PDM_DSR_8S = 0, I2S_PDM_DSR_16S, I2S_PDM_DSR_MAX } i2s_pdm_dsr_t;
typedef enum { I2S_PCM_DISABLE = 0, I2S_PCM_A_DECOMPRESS, I2S_PCM_A_COMPRESS, I2S_PCM_U_DECOMPRESS, I2S_PCM_U_COMPRESS } i2s_pcm_compress_t;
typedef struct { i2s_pcm_compress_t pcm_type; } i2s_pcm_cfg_t;
typedef struct { uint32_t sample_rate; uint32_t fp; uint32_t fs; } i2s_pdm_tx_upsample_cfg_t;
esp_err_t i2s_set_dac_mode(i2s_dac_mode_t dac_mode);
esp_err_t i2s_adc_enable(i2s_port_t i2s_num);
esp_err_t i2s_adc_disable(i2s_port_t i2s_num);
esp_err_t i2s_set_adc_mode(int adc_unit, int adc_channel);
esp_err_t i2s_pcm_config(i2s_port_t i2s_num, const i2s_pcm_cfg_t *pcm_cfg);
esp_err_t i2s_set_pdm_rx_down_sample(i2s_port_t i2s_num, i2s_pdm_dsr_t downsample);
esp_err_t i2s_set_pdm_tx_up_sample(i2s_port_t i2s_num, const i2s_pdm_tx_upsample_cfg_t *upsample_cfg);

#ifdef __cplusplus
}
#endif

#endif
