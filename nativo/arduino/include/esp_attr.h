/* esp_attr.h -- os atributos de memoria do ESP-IDF (os mesmos nomes do
 * esp_attr.h de verdade).
 *
 * Na placa, cada um poe o codigo ou o dado numa secao do linker (a IRAM, a
 * RTC, a PSRAM). Aqui o programa e uma DLL do PC: so a memoria RTC tem
 * secao propria (".rtcdat" e ".rtcnoi"), que o simulador guarda quando a
 * placa dorme e devolve quando ela acorda, como a RTC de verdade, que fica
 * ligada no sono profundo. O resto nao muda onde as coisas moram. */
#ifndef __ESP_ATTR_H__
#define __ESP_ATTR_H__

#define RTC_DATA_ATTR __attribute__((section(".rtcdat")))
#define RTC_NOINIT_ATTR __attribute__((section(".rtcnoi")))
#define RTC_RODATA_ATTR
#define RTC_FAST_ATTR RTC_DATA_ATTR
#define RTC_SLOW_ATTR RTC_DATA_ATTR
#define RTC_IRAM_ATTR
#define COREDUMP_RTC_DATA_ATTR
#define COREDUMP_RTC_FAST_ATTR
#define COREDUMP_DRAM_ATTR
#define COREDUMP_IRAM_DATA_ATTR

#define IRAM_ATTR
#define FORCE_IRAM_ATTR
#define IRAM_DATA_ATTR
#define IRAM_BSS_ATTR
#define IRAM_8BIT_ACCESSIBLE
#define TCM_IRAM_ATTR
#define TCM_DRAM_ATTR
#define FORCE_TCM_IRAM_ATTR
#define DRAM_ATTR
#define DRAM_DMA_ALIGNED_ATTR __attribute__((aligned(4)))
#define DMA_ATTR WORD_ALIGNED_ATTR
#define DRAM_STR(str) (str)
#define EXT_RAM_ATTR
#define EXT_RAM_BSS_ATTR
#define EXT_RAM_NOINIT_ATTR
#define __NOINIT_ATTR
#define NOLOAD_ATTR
#define ROMFN_ATTR
#define ARDUINO_ISR_ATTR

#define WORD_ALIGNED_ATTR __attribute__((aligned(4)))
#define NOINLINE_ATTR __attribute__((noinline))
#define FORCE_INLINE_ATTR static inline __attribute__((always_inline))
#define PACKED_ATTR __attribute__((packed))
#define IDF_DEPRECATED(REASON) __attribute__((deprecated(REASON)))

/* Os operadores de bandeira de um enum C++ (FLAG_ATTR(gpio_mode_t)). */
#ifdef __cplusplus
#define FLAG_ATTR_IMPL(TYPE, INT_TYPE)                                                                                 \
    FORCE_INLINE_ATTR constexpr TYPE operator~(TYPE a) { return (TYPE) ~(INT_TYPE)a; }                                 \
    FORCE_INLINE_ATTR constexpr TYPE operator|(TYPE a, TYPE b) { return (TYPE)((INT_TYPE)a | (INT_TYPE)b); }           \
    FORCE_INLINE_ATTR constexpr TYPE operator&(TYPE a, TYPE b) { return (TYPE)((INT_TYPE)a & (INT_TYPE)b); }           \
    FORCE_INLINE_ATTR constexpr TYPE operator^(TYPE a, TYPE b) { return (TYPE)((INT_TYPE)a ^ (INT_TYPE)b); }           \
    FORCE_INLINE_ATTR constexpr TYPE operator>>(TYPE a, int b) { return (TYPE)((INT_TYPE)a >> b); }                    \
    FORCE_INLINE_ATTR constexpr TYPE operator<<(TYPE a, int b) { return (TYPE)((INT_TYPE)a << b); }                    \
    FORCE_INLINE_ATTR TYPE &operator|=(TYPE &a, TYPE b) { a = a | b; return a; }                                     \
    FORCE_INLINE_ATTR TYPE &operator&=(TYPE &a, TYPE b) { a = a & b; return a; }                                     \
    FORCE_INLINE_ATTR TYPE &operator^=(TYPE &a, TYPE b) { a = a ^ b; return a; }                                     \
    FORCE_INLINE_ATTR TYPE &operator>>=(TYPE &a, int b) { a = a >> b; return a; }                                    \
    FORCE_INLINE_ATTR TYPE &operator<<=(TYPE &a, int b) { a = a << b; return a; }
#define FLAG_ATTR_U32(TYPE) FLAG_ATTR_IMPL(TYPE, uint32_t)
#define FLAG_ATTR FLAG_ATTR_U32
#else
#define FLAG_ATTR(TYPE)
#endif

#endif
