/* cs_pico.h -- o SDK em C da Raspberry Pi Pico (pico/stdlib.h e os
 * hardware/*.h) sobre o core nativo: o programa com main(), os pinos pelo
 * gpio_put, o tempo pelo sleep_ms e o printf no monitor serial (o stdio da
 * USB). Cada funcao cai na mesma do interpretador (InterpretadorPicoSdk),
 * pelo nome; as de tempo sao as do core (o sleep_ms cede as tarefas do
 * FreeRTOS, como o delay). */
#ifndef CS_PICO_H
#define CS_PICO_H

#include "Arduino.h"

#ifdef __cplusplus
extern "C" {
#endif

typedef unsigned int uint;
typedef uint64_t absolute_time_t;
typedef int64_t (*alarm_callback_t)(int32_t id, void *user_data);
typedef int32_t alarm_id_t;
struct repeating_timer;
typedef bool (*repeating_timer_callback_t)(struct repeating_timer *rt);
struct repeating_timer {
    int64_t delay_us;
    int32_t alarm_id;
    repeating_timer_callback_t callback;
    void *user_data;
};

#define PICO_DEFAULT_LED_PIN 25
#define PICO_DEFAULT_I2C_SDA_PIN 4
#define PICO_DEFAULT_I2C_SCL_PIN 5
#define PICO_DEFAULT_UART_TX_PIN 0
#define PICO_DEFAULT_UART_RX_PIN 1
#define GPIO_OUT 1
#define GPIO_IN 0
enum gpio_function { GPIO_FUNC_XIP = 0, GPIO_FUNC_SPI = 1, GPIO_FUNC_UART = 2, GPIO_FUNC_I2C = 3, GPIO_FUNC_PWM = 4,
                     GPIO_FUNC_SIO = 5, GPIO_FUNC_PIO0 = 6, GPIO_FUNC_PIO1 = 7, GPIO_FUNC_GPCK = 8, GPIO_FUNC_USB = 9,
                     GPIO_FUNC_NULL = 0x1f };
enum gpio_irq_level { GPIO_IRQ_LEVEL_LOW = 0x1u, GPIO_IRQ_LEVEL_HIGH = 0x2u, GPIO_IRQ_EDGE_FALL = 0x4u, GPIO_IRQ_EDGE_RISE = 0x8u };
typedef void (*gpio_irq_callback_t)(uint gpio, uint32_t event_mask);
#define PWM_CHAN_A 0
#define PWM_CHAN_B 1
#define ADC_TEMPERATURE_CHANNEL_NUM 4
#define nil_time ((absolute_time_t)0)
#define at_the_end_of_time ((absolute_time_t)INT64_MAX)
#define PICO_OK 0
#define PICO_ERROR_TIMEOUT -1
#define PICO_ERROR_GENERIC -2
#define PICO_ERROR_NO_DATA -3

/* ---- o stdio ---- */
bool stdio_init_all(void);
bool stdio_usb_init(void);
void stdio_uart_init(void);
bool stdio_usb_connected(void);
int getchar_timeout_us(uint32_t timeout_us);
void stdio_flush(void);

/* ---- os pinos ---- */
void gpio_init(uint gpio);
void gpio_deinit(uint gpio);
void gpio_init_mask(uint32_t mask);
void gpio_set_dir(uint gpio, bool out);
void gpio_set_dir_out_masked(uint32_t mask);
void gpio_set_dir_in_masked(uint32_t mask);
void gpio_set_dir_masked(uint32_t mask, uint32_t value);
void gpio_set_dir_all_bits(uint32_t values);
void gpio_put(uint gpio, bool value);
bool gpio_get(uint gpio);
bool gpio_get_out_level(uint gpio);
uint32_t gpio_get_all(void);
void gpio_put_all(uint32_t value);
void gpio_put_masked(uint32_t mask, uint32_t value);
void gpio_set_mask(uint32_t mask);
void gpio_clr_mask(uint32_t mask);
void gpio_xor_mask(uint32_t mask);
void gpio_pull_up(uint gpio);
void gpio_pull_down(uint gpio);
void gpio_disable_pulls(uint gpio);
void gpio_set_pulls(uint gpio, bool up, bool down);
bool gpio_is_pulled_up(uint gpio);
bool gpio_is_pulled_down(uint gpio);
void gpio_set_function(uint gpio, enum gpio_function fn);
enum gpio_function gpio_get_function(uint gpio);
void gpio_set_input_enabled(uint gpio, bool enabled);
bool gpio_is_dir_out(uint gpio);
void gpio_set_irq_enabled_with_callback(uint gpio, uint32_t event_mask, bool enabled, gpio_irq_callback_t callback);
void gpio_set_irq_enabled(uint gpio, uint32_t event_mask, bool enabled);
void gpio_set_irq_callback(gpio_irq_callback_t callback);
void gpio_acknowledge_irq(uint gpio, uint32_t event_mask);

/* ---- o tempo ---- */
void sleep_ms(uint32_t ms);
void sleep_us(uint64_t us);
void sleep_until(absolute_time_t t);
void busy_wait_us(uint64_t delay_us);
void busy_wait_us_32(uint32_t delay_us);
void busy_wait_ms(uint32_t delay_ms);
uint32_t time_us_32(void);
uint64_t time_us_64(void);
absolute_time_t get_absolute_time(void);
uint32_t to_ms_since_boot(absolute_time_t t);
uint64_t to_us_since_boot(absolute_time_t t);
int64_t absolute_time_diff_us(absolute_time_t from, absolute_time_t to);
absolute_time_t delayed_by_ms(absolute_time_t t, uint32_t ms);
absolute_time_t delayed_by_us(absolute_time_t t, uint64_t us);
absolute_time_t make_timeout_time_ms(uint32_t ms);
absolute_time_t make_timeout_time_us(uint64_t us);
bool time_reached(absolute_time_t t);
bool is_nil_time(absolute_time_t t);
alarm_id_t add_alarm_in_ms(uint32_t ms, alarm_callback_t callback, void *user_data, bool fire_if_past);
alarm_id_t add_alarm_in_us(uint64_t us, alarm_callback_t callback, void *user_data, bool fire_if_past);
bool cancel_alarm(alarm_id_t alarm_id);
bool add_repeating_timer_ms(int32_t delay_ms, repeating_timer_callback_t callback, void *user_data, struct repeating_timer *out);
bool add_repeating_timer_us(int64_t delay_us, repeating_timer_callback_t callback, void *user_data, struct repeating_timer *out);
bool cancel_repeating_timer(struct repeating_timer *timer);
void tight_loop_contents(void);

/* ---- o ADC ---- */
void adc_init(void);
void adc_gpio_init(uint gpio);
void adc_select_input(uint input);
uint adc_get_selected_input(void);
uint16_t adc_read(void);
void adc_set_temp_sensor_enabled(bool enable);
void adc_set_round_robin(uint input_mask);

/* ---- o PWM ---- */
typedef struct {
    uint32_t csr;
    uint32_t div;
    uint32_t top;
} pwm_config;
uint pwm_gpio_to_slice_num(uint gpio);
uint pwm_gpio_to_channel(uint gpio);
pwm_config pwm_get_default_config(void);
void pwm_config_set_wrap(pwm_config *c, uint16_t wrap);
void pwm_config_set_clkdiv(pwm_config *c, float div);
void pwm_config_set_clkdiv_int(pwm_config *c, uint div);
void pwm_config_set_clkdiv_int_frac(pwm_config *c, uint8_t integer, uint8_t fract);
void pwm_config_set_phase_correct(pwm_config *c, bool phase_correct);
void pwm_config_set_output_polarity(pwm_config *c, bool a, bool b);
void pwm_config_set_clkdiv_mode(pwm_config *c, int mode);
void pwm_init(uint slice_num, pwm_config *c, bool start);
void pwm_set_wrap(uint slice_num, uint16_t wrap);
void pwm_set_clkdiv(uint slice_num, float divider);
void pwm_set_clkdiv_int_frac(uint slice_num, uint8_t integer, uint8_t fract);
void pwm_set_enabled(uint slice_num, bool enabled);
void pwm_set_mask_enabled(uint32_t mask);
void pwm_set_chan_level(uint slice_num, uint chan, uint16_t level);
void pwm_set_both_levels(uint slice_num, uint16_t level_a, uint16_t level_b);
void pwm_set_gpio_level(uint gpio, uint16_t level);
void pwm_set_phase_correct(uint slice_num, bool phase_correct);

/* ---- os relogios ---- */
enum clock_index { clk_gpout0 = 0, clk_gpout1, clk_gpout2, clk_gpout3, clk_ref, clk_sys, clk_peri, clk_usb, clk_adc, clk_rtc };
uint32_t clock_get_hz(enum clock_index clk_index);
bool set_sys_clock_khz(uint32_t freq_khz, bool required);

#ifdef __cplusplus
}
#endif

#endif
