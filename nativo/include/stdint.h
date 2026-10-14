/* stdint.h do CircuitSketch: os tipos do ESP-IDF 5 (int32_t e long, e
 * long tem 32 bits, como no Xtensa). O ponteiro e o do PC: intptr_t
 * acompanha o ponteiro. */
#ifndef _STDINT_H
#define _STDINT_H

#include <stddef.h>

typedef signed char int8_t;
typedef short int16_t;
typedef long int32_t;
typedef long long int64_t;
typedef unsigned char uint8_t;
typedef unsigned short uint16_t;
typedef unsigned long uint32_t;
typedef unsigned long long uint64_t;

typedef __PTRDIFF_TYPE__ intptr_t;
typedef __SIZE_TYPE__ uintptr_t;

typedef int8_t int_least8_t;
typedef int16_t int_least16_t;
typedef int32_t int_least32_t;
typedef int64_t int_least64_t;
typedef uint8_t uint_least8_t;
typedef uint16_t uint_least16_t;
typedef uint32_t uint_least32_t;
typedef uint64_t uint_least64_t;
typedef int int_fast8_t;
typedef int int_fast16_t;
typedef int int_fast32_t;
typedef int64_t int_fast64_t;
typedef unsigned int uint_fast8_t;
typedef unsigned int uint_fast16_t;
typedef unsigned int uint_fast32_t;
typedef uint64_t uint_fast64_t;
typedef long long intmax_t;
typedef unsigned long long uintmax_t;

#define INT8_MIN (-128)
#define INT16_MIN (-32768)
#define INT32_MIN (-2147483647L - 1)
#define INT64_MIN (-9223372036854775807LL - 1)
#define INT8_MAX 127
#define INT16_MAX 32767
#define INT32_MAX 2147483647L
#define INT64_MAX 9223372036854775807LL
#define UINT8_MAX 255
#define UINT16_MAX 65535
#define UINT32_MAX 4294967295UL
#define UINT64_MAX 18446744073709551615ULL
#define INT_LEAST8_MIN INT8_MIN
#define INT_LEAST16_MIN INT16_MIN
#define INT_LEAST32_MIN INT32_MIN
#define INT_LEAST64_MIN INT64_MIN
#define INT_LEAST8_MAX INT8_MAX
#define INT_LEAST16_MAX INT16_MAX
#define INT_LEAST32_MAX INT32_MAX
#define INT_LEAST64_MAX INT64_MAX
#define UINT_LEAST8_MAX UINT8_MAX
#define UINT_LEAST16_MAX UINT16_MAX
#define UINT_LEAST32_MAX UINT32_MAX
#define UINT_LEAST64_MAX UINT64_MAX
#define INTPTR_MIN INT64_MIN
#define INTPTR_MAX INT64_MAX
#define UINTPTR_MAX UINT64_MAX
#define INTMAX_MIN INT64_MIN
#define INTMAX_MAX INT64_MAX
#define UINTMAX_MAX UINT64_MAX
#define SIZE_MAX UINT64_MAX
#define PTRDIFF_MIN INT64_MIN
#define PTRDIFF_MAX INT64_MAX
#define SIG_ATOMIC_MIN (-2147483647 - 1)
#define SIG_ATOMIC_MAX 2147483647
#define WCHAR_MIN 0
#define WCHAR_MAX 65535

#define INT8_C(c) c
#define INT16_C(c) c
#define INT32_C(c) c ## L
#define INT64_C(c) c ## LL
#define UINT8_C(c) c
#define UINT16_C(c) c
#define UINT32_C(c) c ## UL
#define UINT64_C(c) c ## ULL
#define INTMAX_C(c) c ## LL
#define UINTMAX_C(c) c ## ULL

/* O que o GCC tem embutido e o TinyCC nao: o runtime tem como funcao. */
uint16_t __builtin_bswap16(uint16_t x);
uint32_t __builtin_bswap32(uint32_t x);
uint64_t __builtin_bswap64(uint64_t x);
int __builtin_clz(unsigned int x);
int __builtin_ctz(unsigned int x);
int __builtin_clzl(unsigned long x);
int __builtin_ctzl(unsigned long x);
int __builtin_clzll(unsigned long long x);
int __builtin_ctzll(unsigned long long x);
int __builtin_popcount(unsigned int x);
int __builtin_popcountl(unsigned long x);
int __builtin_popcountll(unsigned long long x);
int __builtin_ffs(int x);
int __builtin_parity(unsigned int x);
void __builtin_unreachable(void);
void __builtin_trap(void);

#endif
