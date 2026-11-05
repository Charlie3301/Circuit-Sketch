/* stdlib.h -- o do compilador, mais o que a placa tem e o Windows nao. */
#include_next <stdlib.h>

#ifndef CS_STDLIB_H
#define CS_STDLIB_H
/* O free do Windows so aceita o que veio do malloc, e o malloc do PC ja alinha em 16. */
#ifdef __cplusplus
extern "C" {
#endif
static inline void *aligned_alloc(size_t alinhamento, size_t tamanho) { (void)alinhamento; return malloc(tamanho); }
#ifdef __cplusplus
}
#endif
#endif
