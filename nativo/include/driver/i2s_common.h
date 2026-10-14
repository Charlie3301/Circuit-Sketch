#ifndef CS_DRIVER_I2S_COMMON_H
#define CS_DRIVER_I2S_COMMON_H

#include "driver/i2s_types.h"
#include "driver/gpio.h"
#include "freertos/FreeRTOS.h"

#define I2S_GPIO_UNUSED GPIO_NUM_NC

#define I2S_CHANNEL_DEFAULT_CONFIG(i2s_num, i2s_role) { \
    .id = i2s_num, \
    .role = i2s_role, \
    .dma_desc_num = 6, \
    .dma_frame_num = 240, \
    .auto_clear_after_cb = false, \
    .auto_clear_before_cb = false, \
    .intr_priority = 0, \
}

esp_err_t i2s_new_channel(const i2s_chan_config_t *config, i2s_chan_handle_t *tx, i2s_chan_handle_t *rx);
esp_err_t i2s_del_channel(i2s_chan_handle_t canal);
esp_err_t i2s_channel_get_info(i2s_chan_handle_t canal, i2s_chan_info_t *info);
esp_err_t i2s_channel_enable(i2s_chan_handle_t canal);
esp_err_t i2s_channel_disable(i2s_chan_handle_t canal);
esp_err_t i2s_channel_preload_data(i2s_chan_handle_t canal, const void *dados, size_t n, size_t *carregados);
esp_err_t i2s_channel_write(i2s_chan_handle_t canal, const void *dados, size_t n, size_t *escritos,
                            uint32_t espera_ms);
esp_err_t i2s_channel_read(i2s_chan_handle_t canal, void *dados, size_t n, size_t *lidos, uint32_t espera_ms);
esp_err_t i2s_channel_register_event_callback(i2s_chan_handle_t canal, const i2s_event_callbacks_t *cbs,
                                              void *contexto);

#endif
