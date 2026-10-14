/* setjmp do CircuitSketch: guarda os registradores que o x64 do Windows
 * manda preservar (rbx, rbp, rdi, rsi, r12-r15, xmm6-xmm15), sem
 * desenrolar a pilha -- o codigo do TinyCC nao tem as tabelas que o
 * longjmp do Windows usaria. */
#ifndef _SETJMP_H
#define _SETJMP_H

typedef unsigned long long jmp_buf[32];
typedef jmp_buf sigjmp_buf;

int __cs_setjmp(jmp_buf b);
void __cs_longjmp(jmp_buf b, int valor) __attribute__((noreturn));

#define setjmp(b) __cs_setjmp(b)
#define longjmp(b, v) __cs_longjmp(b, v)
#define _setjmp(b) __cs_setjmp(b)
#define _longjmp(b, v) __cs_longjmp(b, v)
#define sigsetjmp(b, s) __cs_setjmp(b)
#define siglongjmp(b, v) __cs_longjmp(b, v)

#endif
