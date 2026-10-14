/* driver/gpio.h: os pinos da placa simulada. O que o codigo escreve aparece
 * no circuito (LED, display, analisador logico), e o que o circuito faz
 * (botao, chave) chega na leitura e nas interrupcoes. */
#ifndef CS_DRIVER_GPIO_H
#define CS_DRIVER_GPIO_H

#include <stdint.h>
#include <stdio.h>
#include <stdbool.h>
#include "sdkconfig.h"
#include "esp_err.h"
#include "esp_attr.h"
#include "esp_intr_alloc.h"
#include "esp_bit_defs.h"
/* Os pinos e as mascaras sao os do chip (nativo/include/chip/<chip>/soc):
 * no S3 nao existem os GPIO 22 a 25; no ESP32, os 34 a 39 so leem. */
#include "soc/soc_caps.h"
#include "soc/gpio_num.h"

#define GPIO_PIN_COUNT (SOC_GPIO_PIN_COUNT)
#define GPIO_IS_VALID_GPIO(gpio_num) ((gpio_num >= 0) && \
                                      (((1ULL << (gpio_num)) & SOC_GPIO_VALID_GPIO_MASK) != 0))
#define GPIO_IS_VALID_OUTPUT_GPIO(gpio_num) ((gpio_num >= 0) && \
                                             (((1ULL << (gpio_num)) & SOC_GPIO_VALID_OUTPUT_GPIO_MASK) != 0))
#define GPIO_IS_VALID_DIGITAL_IO_PAD(gpio_num) ((gpio_num >= 0) && \
                                                (((1ULL << (gpio_num)) & SOC_GPIO_VALID_DIGITAL_IO_PAD_MASK) != 0))

#define GPIO_MODE_DEF_DISABLE 0
#define GPIO_MODE_DEF_INPUT (1 << 0)
#define GPIO_MODE_DEF_OUTPUT (1 << 1)
#define GPIO_MODE_DEF_OD (1 << 2)

typedef enum {
    GPIO_MODE_DISABLE = GPIO_MODE_DEF_DISABLE,
    GPIO_MODE_INPUT = GPIO_MODE_DEF_INPUT,
    GPIO_MODE_OUTPUT = GPIO_MODE_DEF_OUTPUT,
    GPIO_MODE_OUTPUT_OD = GPIO_MODE_DEF_OUTPUT | GPIO_MODE_DEF_OD,
    GPIO_MODE_INPUT_OUTPUT_OD = GPIO_MODE_DEF_INPUT | GPIO_MODE_DEF_OUTPUT | GPIO_MODE_DEF_OD,
    GPIO_MODE_INPUT_OUTPUT = GPIO_MODE_DEF_INPUT | GPIO_MODE_DEF_OUTPUT,
} gpio_mode_t;

typedef enum { GPIO_PULLUP_DISABLE = 0, GPIO_PULLUP_ENABLE = 1 } gpio_pullup_t;
typedef enum { GPIO_PULLDOWN_DISABLE = 0, GPIO_PULLDOWN_ENABLE = 1 } gpio_pulldown_t;
typedef enum {
    GPIO_PULLUP_ONLY, GPIO_PULLDOWN_ONLY, GPIO_PULLUP_PULLDOWN, GPIO_FLOATING
} gpio_pull_mode_t;

typedef enum {
    GPIO_INTR_DISABLE = 0,
    GPIO_INTR_POSEDGE = 1,
    GPIO_INTR_NEGEDGE = 2,
    GPIO_INTR_ANYEDGE = 3,
    GPIO_INTR_LOW_LEVEL = 4,
    GPIO_INTR_HIGH_LEVEL = 5,
    GPIO_INTR_MAX,
} gpio_int_type_t;

typedef enum {
    GPIO_DRIVE_CAP_0 = 0, GPIO_DRIVE_CAP_1 = 1, GPIO_DRIVE_CAP_2 = 2,
    GPIO_DRIVE_CAP_DEFAULT = 2, GPIO_DRIVE_CAP_3 = 3, GPIO_DRIVE_CAP_MAX,
} gpio_drive_cap_t;

#if SOC_GPIO_SUPPORT_PIN_HYS_FILTER
typedef enum {
#if SOC_GPIO_SUPPORT_PIN_HYS_CTRL_BY_EFUSE
    GPIO_HYS_CTRL_EFUSE = 0,
#endif
    GPIO_HYS_SOFT_DISABLE,
    GPIO_HYS_SOFT_ENABLE,
} gpio_hys_ctrl_mode_t;
#endif

typedef struct {
    uint64_t pin_bit_mask;
    gpio_mode_t mode;
    gpio_pullup_t pull_up_en;
    gpio_pulldown_t pull_down_en;
    gpio_int_type_t intr_type;
#if SOC_GPIO_SUPPORT_PIN_HYS_FILTER
    gpio_hys_ctrl_mode_t hys_ctrl_mode;
#endif
} gpio_config_t;

typedef void (*gpio_isr_t)(void *arg);
typedef struct cs_gpio_isr *gpio_isr_handle_t;

esp_err_t gpio_config(const gpio_config_t *config);
esp_err_t gpio_reset_pin(gpio_num_t pino);
esp_err_t gpio_set_direction(gpio_num_t pino, gpio_mode_t modo);
esp_err_t gpio_set_level(gpio_num_t pino, uint32_t nivel);
int gpio_get_level(gpio_num_t pino);
esp_err_t gpio_set_pull_mode(gpio_num_t pino, gpio_pull_mode_t pull);
esp_err_t gpio_pullup_en(gpio_num_t pino);
esp_err_t gpio_pullup_dis(gpio_num_t pino);
esp_err_t gpio_pulldown_en(gpio_num_t pino);
esp_err_t gpio_pulldown_dis(gpio_num_t pino);
esp_err_t gpio_set_intr_type(gpio_num_t pino, gpio_int_type_t tipo);
esp_err_t gpio_intr_enable(gpio_num_t pino);
esp_err_t gpio_intr_disable(gpio_num_t pino);
esp_err_t gpio_install_isr_service(int flags);
void gpio_uninstall_isr_service(void);
esp_err_t gpio_isr_handler_add(gpio_num_t pino, gpio_isr_t tratador, void *arg);
esp_err_t gpio_isr_handler_remove(gpio_num_t pino);
esp_err_t gpio_isr_register(void (*tratador)(void *), void *arg, int flags, gpio_isr_handle_t *alca);
esp_err_t gpio_set_drive_capability(gpio_num_t pino, gpio_drive_cap_t forca);
esp_err_t gpio_get_drive_capability(gpio_num_t pino, gpio_drive_cap_t *forca);
esp_err_t gpio_hold_en(gpio_num_t pino);
esp_err_t gpio_hold_dis(gpio_num_t pino);
void gpio_deep_sleep_hold_en(void);
void gpio_deep_sleep_hold_dis(void);
esp_err_t gpio_wakeup_enable(gpio_num_t pino, gpio_int_type_t tipo);
esp_err_t gpio_wakeup_disable(gpio_num_t pino);
void gpio_iomux_in(uint32_t pino, uint32_t sinal);
void gpio_iomux_out(uint8_t pino, int funcao, bool oen_inv);
void esp_rom_gpio_pad_select_gpio(uint32_t pino);
#define gpio_pad_select_gpio(p) esp_rom_gpio_pad_select_gpio(p)

#endif
