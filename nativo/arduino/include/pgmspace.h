/* pgmspace.h -- PROGMEM e as leituras da flash.
 *
 * No AVR a flash e outro espaco de enderecos e precisa de pgm_read_byte();
 * no PC (e na ESP32) e a mesma memoria: as macros so leem o endereco. */
#ifndef CS_PGMSPACE_H
#define CS_PGMSPACE_H

#include <stdint.h>
#include <string.h>
#include <stdio.h>

#define PROGMEM
#define PGM_P const char *
#define PGM_VOID_P const void *
#define PSTR(s) (s)
#define PROGMEM_LATE
#define FPSTR(p) ((const __FlashStringHelper *)(p))

typedef char prog_char;
typedef unsigned char prog_uchar;
typedef int8_t prog_int8_t;
typedef uint8_t prog_uint8_t;
typedef int16_t prog_int16_t;
typedef uint16_t prog_uint16_t;
typedef int32_t prog_int32_t;
typedef uint32_t prog_uint32_t;

#define pgm_read_byte(addr) (*(const unsigned char *)(addr))
#define pgm_read_word(addr) (*(const unsigned short *)(addr))
#define pgm_read_dword(addr) (*(const unsigned long *)(addr))
#define pgm_read_float(addr) (*(const float *)(addr))
#define pgm_read_ptr(addr) (*(void *const *)(addr))
#define pgm_read_byte_near(addr) pgm_read_byte(addr)
#define pgm_read_word_near(addr) pgm_read_word(addr)
#define pgm_read_dword_near(addr) pgm_read_dword(addr)
#define pgm_read_float_near(addr) pgm_read_float(addr)
#define pgm_read_ptr_near(addr) pgm_read_ptr(addr)
#define pgm_read_byte_far(addr) pgm_read_byte(addr)
#define pgm_read_word_far(addr) pgm_read_word(addr)
#define pgm_read_dword_far(addr) pgm_read_dword(addr)
#define pgm_read_float_far(addr) pgm_read_float(addr)
#define pgm_read_ptr_far(addr) pgm_read_ptr(addr)
#define pgm_get_far_address(var) ((uintptr_t)(&(var)))

#define memcpy_P memcpy
#define memccpy_P(dest, src, c, n) memccpy((dest), (src), (c), (n))
/* O memmem da newlib (o Windows nao tem): onde "agulha" aparece em "palheiro". */
static inline void *cs_memmem(const void *palheiro, size_t n, const void *agulha, size_t m)
{
    const unsigned char *p = (const unsigned char *)palheiro;
    if (m == 0) return (void *)p;
    for (size_t i = 0; i + m <= n; i++)
        if (p[i] == *(const unsigned char *)agulha && memcmp(p + i, agulha, m) == 0) return (void *)(p + i);
    return NULL;
}
#define memmem_P(h, hl, n, nl) cs_memmem((h), (hl), (n), (nl))
#define memcmp_P memcmp
#define memchr_P memchr
#define strcpy_P strcpy
#define strncpy_P strncpy
#define strcat_P strcat
#define strncat_P strncat
#define strcmp_P strcmp
#define strncmp_P strncmp
#define strcasecmp_P strcasecmp
#define strncasecmp_P strncasecmp
#define strlen_P strlen
#define strnlen_P strnlen
#define strstr_P strstr
#define strchr_P strchr
#define strrchr_P strrchr
#define sprintf_P sprintf
#define snprintf_P snprintf
#define vsnprintf_P vsnprintf
#define printf_P printf

#endif
