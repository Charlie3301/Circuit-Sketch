/* stdint.h -- os tipos de 32 bits como no compilador das placas: long, e nao int. O tamanho e o
 * mesmo aqui, mas o tipo muda o que compila: um volatile uint32_t * nao vira volatile unsigned
 * long * (a Encoder), e quem sobrecarrega int e int32_t daria redefinicao so no simulador. */
#ifndef CS_STDINT_H
#define CS_STDINT_H

#define int32_t cs_int32_do_computador
#define uint32_t cs_uint32_do_computador
#include_next <stdint.h>
#undef int32_t
#undef uint32_t

typedef long int32_t;
typedef unsigned long uint32_t;

#endif
