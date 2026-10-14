/* stdarg.h do CircuitSketch (codigo nativo).
 *
 * O C nativo roda no PC em x64 do Windows, e e esse o jeito de passar
 * argumentos variaveis la: cada argumento ocupa 8 bytes na pilha. O
 * _WIN64 foi tirado para o codigo do sketch nao pegar os caminhos do
 * Windows, entao a escolha e feita aqui, direto. */
#ifndef _STDARG_H
#define _STDARG_H

typedef char *va_list;
#define va_start(ap, last) __builtin_va_start(ap, last)
#define va_arg(ap, t) ((sizeof(t) > 8 || (sizeof(t) & (sizeof(t) - 1))) \
    ? **(t **)((ap += 8) - 8) : *(t *)((ap += 8) - 8))
#define va_copy(dest, src) ((dest) = (src))
#define va_end(ap)

typedef va_list __gnuc_va_list;
#define _VA_LIST_DEFINED
#define __va_copy(d, s) va_copy(d, s)

#endif
