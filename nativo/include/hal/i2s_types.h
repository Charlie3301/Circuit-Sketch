#ifndef CS_HAL_I2S_TYPES_H
#define CS_HAL_I2S_TYPES_H

#include <stdint.h>

typedef enum { I2S_NUM_0 = 0, I2S_NUM_1 = 1, I2S_NUM_MAX, I2S_NUM_AUTO } i2s_port_t;
typedef enum { I2S_ROLE_MASTER, I2S_ROLE_SLAVE } i2s_role_t;
typedef enum { I2S_DIR_RX = 1 << 0, I2S_DIR_TX = 1 << 1 } i2s_dir_t;
typedef enum { I2S_COMM_MODE_STD, I2S_COMM_MODE_PDM, I2S_COMM_MODE_TDM, I2S_COMM_MODE_NONE } i2s_comm_mode_t;

typedef enum {
    I2S_DATA_BIT_WIDTH_8BIT = 8,
    I2S_DATA_BIT_WIDTH_16BIT = 16,
    I2S_DATA_BIT_WIDTH_24BIT = 24,
    I2S_DATA_BIT_WIDTH_32BIT = 32,
} i2s_data_bit_width_t;

typedef enum {
    I2S_SLOT_BIT_WIDTH_AUTO = 0,
    I2S_SLOT_BIT_WIDTH_8BIT = 8,
    I2S_SLOT_BIT_WIDTH_16BIT = 16,
    I2S_SLOT_BIT_WIDTH_24BIT = 24,
    I2S_SLOT_BIT_WIDTH_32BIT = 32,
} i2s_slot_bit_width_t;

typedef enum { I2S_SLOT_MODE_MONO = 1, I2S_SLOT_MODE_STEREO = 2 } i2s_slot_mode_t;

typedef enum {
    I2S_STD_SLOT_LEFT = 1 << 0,
    I2S_STD_SLOT_RIGHT = 1 << 1,
    I2S_STD_SLOT_BOTH = (1 << 0) | (1 << 1),
} i2s_std_slot_mask_t;

typedef enum {
    I2S_CLK_SRC_DEFAULT = 0,
    I2S_CLK_SRC_PLL_160M = 0,
    I2S_CLK_SRC_XTAL = 1,
    I2S_CLK_SRC_APLL = 2,
    I2S_CLK_SRC_EXTERNAL = 3,
} i2s_clock_src_t;

typedef enum {
    I2S_MCLK_MULTIPLE_128 = 128,
    I2S_MCLK_MULTIPLE_192 = 192,
    I2S_MCLK_MULTIPLE_256 = 256,
    I2S_MCLK_MULTIPLE_384 = 384,
    I2S_MCLK_MULTIPLE_512 = 512,
    I2S_MCLK_MULTIPLE_576 = 576,
    I2S_MCLK_MULTIPLE_768 = 768,
    I2S_MCLK_MULTIPLE_1024 = 1024,
    I2S_MCLK_MULTIPLE_1152 = 1152,
} i2s_mclk_multiple_t;

#endif
