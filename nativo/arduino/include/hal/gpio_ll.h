/* hal/gpio_ll.h -- a camada do GPIO do ESP-IDF sobre o GPIO do simulador. Quem usa o hardware
 * direto (a TFT_eSPI) escreve em GPIO.out_w1ts: cada campo ja e o registrador (soc/gpio_struct.h). */
#ifndef CS_GPIO_LL_H
#define CS_GPIO_LL_H

#include <stdbool.h>
#include "soc/soc.h"
#include "soc/gpio_struct.h"
#include "hal/gpio_types.h"
#include "driver/gpio.h"

#ifdef __cplusplus
typedef cs_gpio_dev gpio_dev_t;
#else
typedef struct cs_gpio_dev_c gpio_dev_t;
#endif

#define GPIO_LL_GET_HW(num) ((gpio_dev_t *)0)

static inline void gpio_ll_set_level(gpio_dev_t *hw, uint32_t n, uint32_t v) { (void)hw; gpio_set_level((gpio_num_t)n, v); }
static inline int gpio_ll_get_level(gpio_dev_t *hw, uint32_t n) { (void)hw; return gpio_get_level((gpio_num_t)n); }
static inline void gpio_ll_output_enable(gpio_dev_t *hw, uint32_t n) { (void)hw; gpio_set_direction((gpio_num_t)n, GPIO_MODE_OUTPUT); }
static inline void gpio_ll_output_disable(gpio_dev_t *hw, uint32_t n) { (void)hw; gpio_set_direction((gpio_num_t)n, GPIO_MODE_INPUT); }
static inline void gpio_ll_input_enable(gpio_dev_t *hw, uint32_t n) { (void)hw; gpio_set_direction((gpio_num_t)n, GPIO_MODE_INPUT); }
static inline void gpio_ll_input_disable(gpio_dev_t *hw, uint32_t n) { (void)hw; (void)n; }
static inline void gpio_ll_od_enable(gpio_dev_t *hw, uint32_t n) { (void)hw; gpio_set_direction((gpio_num_t)n, GPIO_MODE_OUTPUT_OD); }
static inline void gpio_ll_od_disable(gpio_dev_t *hw, uint32_t n) { (void)hw; gpio_set_direction((gpio_num_t)n, GPIO_MODE_OUTPUT); }
static inline void gpio_ll_pullup_en(gpio_dev_t *hw, uint32_t n) { (void)hw; gpio_pullup_en((gpio_num_t)n); }
static inline void gpio_ll_pullup_dis(gpio_dev_t *hw, uint32_t n) { (void)hw; gpio_pullup_dis((gpio_num_t)n); }
static inline void gpio_ll_pulldown_en(gpio_dev_t *hw, uint32_t n) { (void)hw; gpio_pulldown_en((gpio_num_t)n); }
static inline void gpio_ll_pulldown_dis(gpio_dev_t *hw, uint32_t n) { (void)hw; gpio_pulldown_dis((gpio_num_t)n); }
#endif
