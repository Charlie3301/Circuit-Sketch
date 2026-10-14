/* limits.h do CircuitSketch (codigo nativo), com os valores do GCC e da
 * newlib do ESP32: o char e sem sinal (no Xtensa e no RISC-V), o long tem
 * 32 bits e o sys/syslimits.h vem junto (PATH_MAX 1024). */
#ifndef _LIBC_LIMITS_H_
#define _LIBC_LIMITS_H_ 1

#include <sys/syslimits.h>

#define MB_LEN_MAX 1
#define NL_ARGMAX 32

#define CHAR_BIT 8
#define SCHAR_MIN (-128)
#define SCHAR_MAX 127
#define UCHAR_MAX 255
#define CHAR_MIN 0
#define CHAR_MAX UCHAR_MAX
#define SHRT_MIN (-32768)
#define SHRT_MAX 32767
#define USHRT_MAX 65535
#define INT_MIN (-INT_MAX - 1)
#define INT_MAX 2147483647
#define UINT_MAX 4294967295U
#define LONG_MIN (-LONG_MAX - 1L)
#define LONG_MAX 2147483647L
#define ULONG_MAX 4294967295UL
#define LLONG_MIN (-LLONG_MAX - 1LL)
#define LLONG_MAX 9223372036854775807LL
#define ULLONG_MAX 18446744073709551615ULL
#define LONG_LONG_MIN LLONG_MIN
#define LONG_LONG_MAX LLONG_MAX
#define ULONG_LONG_MAX ULLONG_MAX

#define _POSIX2_RE_DUP_MAX 255

#endif
