#ifndef CS_DRIVER_I2S_TYPES_H
#define CS_DRIVER_I2S_TYPES_H

#include <stdint.h>
#include <stdbool.h>
#include <stddef.h>
#include "esp_err.h"
#include "hal/i2s_types.h"

typedef struct i2s_channel_obj_t *i2s_chan_handle_t;

typedef struct {
    i2s_port_t id;
    i2s_role_t role;
    uint32_t dma_desc_num;
    uint32_t dma_frame_num;
    union {
        bool auto_clear;
        bool auto_clear_after_cb;
    };
    bool auto_clear_before_cb;
    bool allow_pd;
    int intr_priority;
} i2s_chan_config_t;

typedef struct {
    i2s_dir_t dir;
    i2s_role_t role;
    i2s_comm_mode_t mode;
    void *mode_cfg;
    uint32_t total_dma_buf_size;
} i2s_chan_info_t;

typedef struct {
    void *dma_buf;
    size_t size;
} i2s_event_data_t;

typedef bool (*i2s_isr_callback_t)(i2s_chan_handle_t handle, i2s_event_data_t *event, void *user_ctx);

typedef struct {
    i2s_isr_callback_t on_recv;
    i2s_isr_callback_t on_recv_q_ovf;
    i2s_isr_callback_t on_sent;
    i2s_isr_callback_t on_send_q_ovf;
} i2s_event_callbacks_t;

#endif
