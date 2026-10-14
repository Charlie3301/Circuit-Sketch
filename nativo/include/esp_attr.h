/* Os atributos de secao da ESP32: no PC nao ha IRAM nem RTC, e o codigo
 * roda igual sem eles. */
#ifndef CS_ESP_ATTR_H
#define CS_ESP_ATTR_H

#define IRAM_ATTR
#define DRAM_ATTR
#define IRAM_DATA_ATTR
#define IRAM_BSS_ATTR
#define DRAM_STR(s) (s)
#define WORD_ALIGNED_ATTR __attribute__((aligned(4)))
#define RTC_IRAM_ATTR
#define RTC_DATA_ATTR
#define RTC_NOINIT_ATTR
#define RTC_RODATA_ATTR
#define RTC_FAST_ATTR
#define RTC_SLOW_ATTR
#define EXT_RAM_ATTR
#define EXT_RAM_BSS_ATTR
#define EXT_RAM_NOINIT_ATTR
#define NOINLINE_ATTR __attribute__((noinline))
#define FORCE_INLINE_ATTR static inline
#define NOINIT_ATTR
#define COREDUMP_DRAM_ATTR
#define TCM_IRAM_ATTR
#define ESP_ATTR_H_

#endif
