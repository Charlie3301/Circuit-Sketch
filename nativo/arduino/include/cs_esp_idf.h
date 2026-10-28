/* cs_esp_idf.h -- o pedaco do ESP-IDF que o Arduino.h da ESP32 traz junto:
 * os atributos de memoria (esp_attr.h), o sono (esp_sleep.h) e o reinicio
 * (esp_system.h). O sono e o reinicio sao os do simulador, pela ponte.
 *
 * A memoria RTC e uma secao da DLL (".rtcdat" e ".rtcnoi"): o simulador
 * guarda os bytes dela quando a placa dorme e os devolve quando ela acorda,
 * como a RTC de verdade, que fica ligada no sono profundo. */
#ifndef CS_ESP_IDF_H
#define CS_ESP_IDF_H

#include <stdint.h>

#define RTC_DATA_ATTR __attribute__((section(".rtcdat")))
#define RTC_NOINIT_ATTR __attribute__((section(".rtcnoi")))
#define RTC_RODATA_ATTR
#define RTC_FAST_ATTR RTC_DATA_ATTR
#define RTC_SLOW_ATTR RTC_DATA_ATTR
#define RTC_IRAM_ATTR
#define IRAM_ATTR
#define DRAM_ATTR
#define EXT_RAM_ATTR
#define EXT_RAM_BSS_ATTR
#define WORD_ALIGNED_ATTR __attribute__((aligned(4)))
#define NOINLINE_ATTR __attribute__((noinline))
#define FORCE_INLINE_ATTR static inline __attribute__((always_inline))
#define ARDUINO_ISR_ATTR

#ifdef __cplusplus
extern "C" {
#endif

typedef int esp_err_t;
typedef int gpio_num_t;
typedef int esp_sleep_wakeup_cause_t;
typedef int esp_sleep_source_t;
typedef int esp_reset_reason_t;
typedef int esp_sleep_ext1_wakeup_mode_t;

#define ESP_ERR_INVALID_ARG 0x102
#define ESP_ERR_INVALID_STATE 0x103
#define ESP_ERR_NOT_SUPPORTED 0x106

#define ESP_SLEEP_WAKEUP_UNDEFINED 0
#define ESP_SLEEP_WAKEUP_ALL 1
#define ESP_SLEEP_WAKEUP_EXT0 2
#define ESP_SLEEP_WAKEUP_EXT1 3
#define ESP_SLEEP_WAKEUP_TIMER 4
#define ESP_SLEEP_WAKEUP_TOUCHPAD 5
#define ESP_SLEEP_WAKEUP_ULP 6
#define ESP_SLEEP_WAKEUP_GPIO 7
#define ESP_SLEEP_WAKEUP_UART 8
#define ESP_SLEEP_WAKEUP_WIFI 9
#define ESP_SLEEP_WAKEUP_COCPU 10
#define ESP_SLEEP_WAKEUP_COCPU_TRAP_TRIG 11
#define ESP_SLEEP_WAKEUP_BT 12

#define ESP_EXT1_WAKEUP_ALL_LOW 0
#define ESP_EXT1_WAKEUP_ANY_LOW 0
#define ESP_EXT1_WAKEUP_ANY_HIGH 1

#define ESP_RST_UNKNOWN 0
#define ESP_RST_POWERON 1
#define ESP_RST_EXT 2
#define ESP_RST_SW 3
#define ESP_RST_PANIC 4
#define ESP_RST_INT_WDT 5
#define ESP_RST_TASK_WDT 6
#define ESP_RST_WDT 7
#define ESP_RST_DEEPSLEEP 8
#define ESP_RST_BROWNOUT 9
#define ESP_RST_SDIO 10

#define GPIO_NUM_NC -1
#define GPIO_NUM_0 0
#define GPIO_NUM_1 1
#define GPIO_NUM_2 2
#define GPIO_NUM_3 3
#define GPIO_NUM_4 4
#define GPIO_NUM_5 5
#define GPIO_NUM_6 6
#define GPIO_NUM_7 7
#define GPIO_NUM_8 8
#define GPIO_NUM_9 9
#define GPIO_NUM_10 10
#define GPIO_NUM_11 11
#define GPIO_NUM_12 12
#define GPIO_NUM_13 13
#define GPIO_NUM_14 14
#define GPIO_NUM_15 15
#define GPIO_NUM_16 16
#define GPIO_NUM_17 17
#define GPIO_NUM_18 18
#define GPIO_NUM_19 19
#define GPIO_NUM_20 20
#define GPIO_NUM_21 21
#define GPIO_NUM_22 22
#define GPIO_NUM_23 23
#define GPIO_NUM_25 25
#define GPIO_NUM_26 26
#define GPIO_NUM_27 27
#define GPIO_NUM_28 28
#define GPIO_NUM_29 29
#define GPIO_NUM_30 30
#define GPIO_NUM_32 32
#define GPIO_NUM_33 33
#define GPIO_NUM_34 34
#define GPIO_NUM_35 35
#define GPIO_NUM_36 36
#define GPIO_NUM_37 37
#define GPIO_NUM_38 38
#define GPIO_NUM_39 39
#define GPIO_NUM_40 40
#define GPIO_NUM_41 41
#define GPIO_NUM_42 42
#define GPIO_NUM_43 43
#define GPIO_NUM_44 44
#define GPIO_NUM_45 45
#define GPIO_NUM_46 46
#define GPIO_NUM_47 47
#define GPIO_NUM_48 48

esp_err_t esp_sleep_enable_ext0_wakeup(gpio_num_t gpio_num, int level);
esp_err_t esp_sleep_enable_ext1_wakeup(uint64_t io_mask, int mode);
esp_err_t esp_sleep_enable_ext1_wakeup_io(uint64_t io_mask, int mode);
esp_err_t esp_sleep_enable_timer_wakeup(uint64_t time_in_us);
esp_err_t esp_sleep_enable_touchpad_wakeup(void);
esp_err_t esp_sleep_enable_gpio_wakeup(void);
esp_err_t esp_sleep_disable_wakeup_source(esp_sleep_source_t source);
esp_sleep_wakeup_cause_t esp_sleep_get_wakeup_cause(void);
uint64_t esp_sleep_get_ext1_wakeup_status(void);
void esp_deep_sleep_start(void);
esp_err_t esp_light_sleep_start(void);
void esp_deep_sleep(uint64_t time_in_us);
void esp_restart(void);
esp_reset_reason_t esp_reset_reason(void);

#ifdef __cplusplus
}
#endif

#endif
