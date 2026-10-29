/* cs_idf.h -- o ESP-IDF (o SDK em C da Espressif) sobre o core nativo: o
 * programa com app_main() e os drivers de sempre (gpio, ledc, adc,
 * esp_timer, esp_log, nvs_flash). Cada driver cai na mesma funcao do
 * Arduino (o gpio_set_level e o digitalWrite), e o printf sai no monitor
 * serial, como o da UART0 da placa. Os cabecalhos de nome do IDF
 * (driver/gpio.h, esp_log.h...) so incluem este. */
#ifndef CS_IDF_H
#define CS_IDF_H

#include "Arduino.h"
#if !defined(ESP32)
#error "o ESP-IDF e o SDK da ESP32: escolha uma placa ESP32 para este programa"
#endif
#include "freertos/FreeRTOS.h"
#include "sdkconfig.h"

#ifdef __cplusplus
extern "C" {
#endif

/* ---- os erros (esp_err.h) ---- */
#ifndef ESP_OK
#define ESP_OK 0
#endif
#define ESP_FAIL -1
#define ESP_ERR_NO_MEM 0x101
#define ESP_ERR_INVALID_SIZE 0x104
#define ESP_ERR_NOT_FOUND 0x105
#define ESP_ERR_TIMEOUT 0x107
#define ESP_ERR_INVALID_RESPONSE 0x108
#define ESP_ERR_NVS_BASE 0x1100
#define ESP_ERR_NVS_NOT_FOUND 0x1102
#define ESP_ERR_NVS_NO_FREE_PAGES 0x110d
#define ESP_ERR_NVS_NEW_VERSION_FOUND 0x1110
const char *esp_err_to_name(esp_err_t code);
void cs_idf_conferir(esp_err_t r, const char *expr, const char *arquivo, int linha);
#define ESP_ERROR_CHECK(x) cs_idf_conferir((x), #x, __FILE__, __LINE__)
#define ESP_ERROR_CHECK_WITHOUT_ABORT(x) (x)
#define ESP_RETURN_ON_ERROR(x, tag, ...)                                                                               \
    do {                                                                                                               \
        esp_err_t cs_r_ = (x);                                                                                         \
        if (cs_r_ != ESP_OK) return cs_r_;                                                                             \
    } while (0)

/* ---- o log (esp_log.h) ---- */
typedef enum { ESP_LOG_NONE, ESP_LOG_ERROR, ESP_LOG_WARN, ESP_LOG_INFO, ESP_LOG_DEBUG, ESP_LOG_VERBOSE } esp_log_level_t;
void esp_log_level_set(const char *tag, esp_log_level_t level);
uint32_t esp_log_timestamp(void);
void esp_log_write(esp_log_level_t level, const char *tag, const char *format, ...);
void cs_idf_log(esp_log_level_t nivel, char letra, const char *tag, const char *formato, ...);
#define ESP_LOGE(tag, fmt, ...) cs_idf_log(ESP_LOG_ERROR, 'E', tag, fmt, ##__VA_ARGS__)
#define ESP_LOGW(tag, fmt, ...) cs_idf_log(ESP_LOG_WARN, 'W', tag, fmt, ##__VA_ARGS__)
#define ESP_LOGI(tag, fmt, ...) cs_idf_log(ESP_LOG_INFO, 'I', tag, fmt, ##__VA_ARGS__)
#define ESP_LOGD(tag, fmt, ...) cs_idf_log(ESP_LOG_DEBUG, 'D', tag, fmt, ##__VA_ARGS__)
#define ESP_LOGV(tag, fmt, ...) cs_idf_log(ESP_LOG_VERBOSE, 'V', tag, fmt, ##__VA_ARGS__)
#define ESP_EARLY_LOGE ESP_LOGE
#define ESP_EARLY_LOGW ESP_LOGW
#define ESP_EARLY_LOGI ESP_LOGI
#define ESP_DRAM_LOGI ESP_LOGI
#define LOG_LOCAL_LEVEL ESP_LOG_INFO

/* ---- o tempo ---- */
int64_t esp_timer_get_time(void);
void ets_delay_us(uint32_t us);
void esp_rom_delay_us(uint32_t us);
#define ets_printf printf
#define esp_rom_printf printf

typedef struct esp_timer *esp_timer_handle_t;
typedef void (*esp_timer_cb_t)(void *arg);
typedef enum { ESP_TIMER_TASK, ESP_TIMER_ISR } esp_timer_dispatch_t;
typedef struct {
    esp_timer_cb_t callback;
    void *arg;
    esp_timer_dispatch_t dispatch_method;
    const char *name;
    bool skip_unhandled_events;
} esp_timer_create_args_t;
esp_err_t esp_timer_create(const esp_timer_create_args_t *create_args, esp_timer_handle_t *out_handle);
esp_err_t esp_timer_start_once(esp_timer_handle_t timer, uint64_t timeout_us);
esp_err_t esp_timer_start_periodic(esp_timer_handle_t timer, uint64_t period);
esp_err_t esp_timer_restart(esp_timer_handle_t timer, uint64_t timeout_us);
esp_err_t esp_timer_stop(esp_timer_handle_t timer);
esp_err_t esp_timer_delete(esp_timer_handle_t timer);
bool esp_timer_is_active(esp_timer_handle_t timer);
esp_err_t esp_timer_init(void);

/* ---- o sistema ---- */
uint32_t esp_get_free_heap_size(void);
uint32_t esp_get_minimum_free_heap_size(void);
uint32_t esp_random(void);
void esp_fill_random(void *buf, size_t len);
typedef struct {
    int model;
    uint32_t features;
    uint16_t revision;
    uint8_t cores;
} esp_chip_info_t;
#define CHIP_ESP32 1
#define CHIP_FEATURE_WIFI_BGN (1 << 1)
#define CHIP_FEATURE_BLE (1 << 4)
#define CHIP_FEATURE_BT (1 << 5)
#define CHIP_FEATURE_EMB_FLASH (1 << 0)
void esp_chip_info(esp_chip_info_t *out_info);
esp_err_t esp_flash_get_size(void *chip, uint32_t *out_size);
const char *esp_get_idf_version(void);

/* ---- os pinos (driver/gpio.h) ---- */
typedef enum {
    GPIO_MODE_DISABLE = 0,
    GPIO_MODE_INPUT = 1,
    GPIO_MODE_OUTPUT = 2,
    GPIO_MODE_OUTPUT_OD = 6,
    GPIO_MODE_INPUT_OUTPUT_OD = 7,
    GPIO_MODE_INPUT_OUTPUT = 3
} gpio_mode_t;
typedef enum { GPIO_PULLUP_ONLY, GPIO_PULLDOWN_ONLY, GPIO_PULLUP_PULLDOWN, GPIO_FLOATING } gpio_pull_mode_t;
typedef enum { GPIO_PULLUP_DISABLE = 0, GPIO_PULLUP_ENABLE = 1 } gpio_pullup_t;
typedef enum { GPIO_PULLDOWN_DISABLE = 0, GPIO_PULLDOWN_ENABLE = 1 } gpio_pulldown_t;
typedef enum {
    GPIO_INTR_DISABLE = 0,
    GPIO_INTR_POSEDGE = 1,
    GPIO_INTR_NEGEDGE = 2,
    GPIO_INTR_ANYEDGE = 3,
    GPIO_INTR_LOW_LEVEL = 4,
    GPIO_INTR_HIGH_LEVEL = 5,
    GPIO_INTR_MAX
} gpio_int_type_t;
typedef enum { GPIO_DRIVE_CAP_0, GPIO_DRIVE_CAP_1, GPIO_DRIVE_CAP_2, GPIO_DRIVE_CAP_DEFAULT = 2, GPIO_DRIVE_CAP_3 } gpio_drive_cap_t;
typedef struct {
    uint64_t pin_bit_mask;
    gpio_mode_t mode;
    gpio_pullup_t pull_up_en;
    gpio_pulldown_t pull_down_en;
    gpio_int_type_t intr_type;
} gpio_config_t;
typedef void (*gpio_isr_t)(void *arg);
typedef void *gpio_isr_handle_t;
#ifndef BIT
#define BIT(n) (1UL << (n))
#endif
#ifndef BIT64
#define BIT64(n) (1ULL << (n))
#endif
#define ESP_INTR_FLAG_DEFAULT 0
#define ESP_INTR_FLAG_LEVEL1 (1 << 1)
#define ESP_INTR_FLAG_IRAM (1 << 10)
#define ESP_INTR_FLAG_EDGE (1 << 9)

esp_err_t gpio_config(const gpio_config_t *cfg);
esp_err_t gpio_reset_pin(gpio_num_t gpio_num);
esp_err_t gpio_set_direction(gpio_num_t gpio_num, gpio_mode_t mode);
esp_err_t gpio_set_level(gpio_num_t gpio_num, uint32_t level);
int gpio_get_level(gpio_num_t gpio_num);
esp_err_t gpio_set_pull_mode(gpio_num_t gpio_num, gpio_pull_mode_t pull);
esp_err_t gpio_pullup_en(gpio_num_t gpio_num);
esp_err_t gpio_pullup_dis(gpio_num_t gpio_num);
esp_err_t gpio_pulldown_en(gpio_num_t gpio_num);
esp_err_t gpio_pulldown_dis(gpio_num_t gpio_num);
esp_err_t gpio_set_intr_type(gpio_num_t gpio_num, gpio_int_type_t intr_type);
esp_err_t gpio_intr_enable(gpio_num_t gpio_num);
esp_err_t gpio_intr_disable(gpio_num_t gpio_num);
esp_err_t gpio_install_isr_service(int intr_alloc_flags);
void gpio_uninstall_isr_service(void);
esp_err_t gpio_isr_handler_add(gpio_num_t gpio_num, gpio_isr_t isr_handler, void *args);
esp_err_t gpio_isr_handler_remove(gpio_num_t gpio_num);
esp_err_t gpio_set_drive_capability(gpio_num_t gpio_num, gpio_drive_cap_t strength);
void esp_rom_gpio_pad_select_gpio(uint32_t gpio_num);
#define gpio_pad_select_gpio esp_rom_gpio_pad_select_gpio

/* ---- o PWM (driver/ledc.h) ---- */
typedef enum { LEDC_HIGH_SPEED_MODE = 0, LEDC_LOW_SPEED_MODE = 1, LEDC_SPEED_MODE_MAX } ledc_mode_t;
typedef enum { LEDC_TIMER_0, LEDC_TIMER_1, LEDC_TIMER_2, LEDC_TIMER_3, LEDC_TIMER_MAX } ledc_timer_t;
typedef enum { LEDC_CHANNEL_0, LEDC_CHANNEL_1, LEDC_CHANNEL_2, LEDC_CHANNEL_3, LEDC_CHANNEL_4, LEDC_CHANNEL_5,
               LEDC_CHANNEL_6, LEDC_CHANNEL_7, LEDC_CHANNEL_MAX } ledc_channel_t;
typedef enum { LEDC_TIMER_1_BIT = 1, LEDC_TIMER_2_BIT, LEDC_TIMER_3_BIT, LEDC_TIMER_4_BIT, LEDC_TIMER_5_BIT,
               LEDC_TIMER_6_BIT, LEDC_TIMER_7_BIT, LEDC_TIMER_8_BIT, LEDC_TIMER_9_BIT, LEDC_TIMER_10_BIT,
               LEDC_TIMER_11_BIT, LEDC_TIMER_12_BIT, LEDC_TIMER_13_BIT, LEDC_TIMER_14_BIT, LEDC_TIMER_15_BIT,
               LEDC_TIMER_16_BIT, LEDC_TIMER_BIT_MAX } ledc_timer_bit_t;
typedef enum { LEDC_AUTO_CLK = 0, LEDC_USE_APB_CLK, LEDC_USE_RC_FAST_CLK, LEDC_USE_REF_TICK, LEDC_USE_XTAL_CLK } ledc_clk_cfg_t;
typedef enum { LEDC_INTR_DISABLE = 0, LEDC_INTR_FADE_END } ledc_intr_type_t;
typedef enum { LEDC_FADE_NO_WAIT = 0, LEDC_FADE_WAIT_DONE } ledc_fade_mode_t;
typedef struct {
    ledc_mode_t speed_mode;
    ledc_timer_bit_t duty_resolution;
    ledc_timer_t timer_num;
    uint32_t freq_hz;
    ledc_clk_cfg_t clk_cfg;
    bool deconfigure;
} ledc_timer_config_t;
typedef struct {
    int gpio_num;
    ledc_mode_t speed_mode;
    ledc_channel_t channel;
    ledc_intr_type_t intr_type;
    ledc_timer_t timer_sel;
    uint32_t duty;
    int hpoint;
    struct {
        unsigned int output_invert : 1;
    } flags;
} ledc_channel_config_t;
esp_err_t ledc_timer_config(const ledc_timer_config_t *timer_conf);
esp_err_t ledc_channel_config(const ledc_channel_config_t *ledc_conf);
esp_err_t ledc_set_duty(ledc_mode_t speed_mode, ledc_channel_t channel, uint32_t duty);
esp_err_t ledc_update_duty(ledc_mode_t speed_mode, ledc_channel_t channel);
esp_err_t ledc_set_duty_and_update(ledc_mode_t speed_mode, ledc_channel_t channel, uint32_t duty, uint32_t hpoint);
uint32_t ledc_get_duty(ledc_mode_t speed_mode, ledc_channel_t channel);
esp_err_t ledc_set_freq(ledc_mode_t speed_mode, ledc_timer_t timer_num, uint32_t freq_hz);
uint32_t ledc_get_freq(ledc_mode_t speed_mode, ledc_timer_t timer_num);
esp_err_t ledc_stop(ledc_mode_t speed_mode, ledc_channel_t channel, uint32_t idle_level);
esp_err_t ledc_fade_func_install(int intr_alloc_flags);
esp_err_t ledc_set_fade_with_time(ledc_mode_t speed_mode, ledc_channel_t channel, uint32_t target_duty, int max_fade_time_ms);
esp_err_t ledc_fade_start(ledc_mode_t speed_mode, ledc_channel_t channel, ledc_fade_mode_t fade_mode);

/* ---- o ADC (driver/adc.h, o antigo, e esp_adc/adc_oneshot.h, o novo) ---- */
typedef enum { ADC_WIDTH_BIT_9, ADC_WIDTH_BIT_10, ADC_WIDTH_BIT_11, ADC_WIDTH_BIT_12, ADC_WIDTH_MAX } adc_bits_width_t;
#define ADC_WIDTH_12Bit ADC_WIDTH_BIT_12
typedef enum { ADC_ATTEN_DB_0, ADC_ATTEN_DB_2_5, ADC_ATTEN_DB_6, ADC_ATTEN_DB_12, ADC_ATTEN_MAX } adc_atten_t;
#define ADC_ATTEN_DB_11 ADC_ATTEN_DB_12
typedef enum { ADC1_CHANNEL_0, ADC1_CHANNEL_1, ADC1_CHANNEL_2, ADC1_CHANNEL_3, ADC1_CHANNEL_4, ADC1_CHANNEL_5,
               ADC1_CHANNEL_6, ADC1_CHANNEL_7, ADC1_CHANNEL_MAX } adc1_channel_t;
typedef enum { ADC2_CHANNEL_0, ADC2_CHANNEL_1, ADC2_CHANNEL_2, ADC2_CHANNEL_3, ADC2_CHANNEL_4, ADC2_CHANNEL_5,
               ADC2_CHANNEL_6, ADC2_CHANNEL_7, ADC2_CHANNEL_8, ADC2_CHANNEL_9, ADC2_CHANNEL_MAX } adc2_channel_t;
typedef enum { ADC_CHANNEL_0, ADC_CHANNEL_1, ADC_CHANNEL_2, ADC_CHANNEL_3, ADC_CHANNEL_4, ADC_CHANNEL_5, ADC_CHANNEL_6,
               ADC_CHANNEL_7, ADC_CHANNEL_8, ADC_CHANNEL_9 } adc_channel_t;
typedef enum { ADC_UNIT_1, ADC_UNIT_2 } adc_unit_t;
typedef enum { ADC_BITWIDTH_DEFAULT = 0, ADC_BITWIDTH_9 = 9, ADC_BITWIDTH_10, ADC_BITWIDTH_11, ADC_BITWIDTH_12, ADC_BITWIDTH_13 } adc_bitwidth_t;
typedef enum { ADC_ULP_MODE_DISABLE = 0 } adc_ulp_mode_t;
esp_err_t adc1_config_width(adc_bits_width_t width_bit);
esp_err_t adc1_config_channel_atten(adc1_channel_t channel, adc_atten_t atten);
int adc1_get_raw(adc1_channel_t channel);
esp_err_t adc2_config_channel_atten(adc2_channel_t channel, adc_atten_t atten);
esp_err_t adc2_get_raw(adc2_channel_t channel, adc_bits_width_t width_bit, int *raw_out);
int hall_sensor_read(void);

typedef struct cs_adc_unidade *adc_oneshot_unit_handle_t;
typedef struct {
    adc_unit_t unit_id;
    int clk_src;
    adc_ulp_mode_t ulp_mode;
} adc_oneshot_unit_init_cfg_t;
typedef struct {
    adc_atten_t atten;
    adc_bitwidth_t bitwidth;
} adc_oneshot_chan_cfg_t;
esp_err_t adc_oneshot_new_unit(const adc_oneshot_unit_init_cfg_t *init_config, adc_oneshot_unit_handle_t *ret_unit);
esp_err_t adc_oneshot_config_channel(adc_oneshot_unit_handle_t handle, adc_channel_t channel, const adc_oneshot_chan_cfg_t *config);
esp_err_t adc_oneshot_read(adc_oneshot_unit_handle_t handle, adc_channel_t chan, int *out_raw);
esp_err_t adc_oneshot_del_unit(adc_oneshot_unit_handle_t handle);
esp_err_t adc_oneshot_io_to_channel(int io_num, adc_unit_t *unit_id, adc_channel_t *channel);

/* ---- a flash (nvs_flash.h) ---- */
esp_err_t nvs_flash_init(void);
esp_err_t nvs_flash_erase(void);
esp_err_t nvs_flash_deinit(void);

#ifdef __cplusplus
}
#endif

#endif
