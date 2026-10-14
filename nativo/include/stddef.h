/* stddef.h do CircuitSketch. Os tamanhos do ponteiro sao os do PC (64 bits). */
#ifndef _STDDEF_H
#define _STDDEF_H

typedef __SIZE_TYPE__ size_t;
typedef __PTRDIFF_TYPE__ ptrdiff_t;
typedef __WCHAR_TYPE__ wchar_t;
typedef struct { long long __ll; double __d; } max_align_t;

#ifndef NULL
#define NULL ((void *)0)
#endif

#define offsetof(tipo, campo) ((size_t)&((tipo *)0)->campo)

#endif
