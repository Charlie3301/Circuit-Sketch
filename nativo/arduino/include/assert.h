/* assert.h -- o assert da newlib do ESP-IDF: a mensagem na serial e o abort, que da
 * o panico e reinicia a placa (o do Windows derrubaria o simulador). Como o de
 * verdade, sem guarda: reincluir com outro NDEBUG vale. */
#include_next <assert.h>
#undef assert
#ifndef __ASSERT_FUNC
#define __ASSERT_FUNC __func__
#endif
#ifdef NDEBUG
#define assert(e) ((void)0)
#else
#ifdef __cplusplus
extern "C"
#endif
void __assert_func(const char *arquivo, int linha, const char *funcao, const char *expressao) __attribute__((__noreturn__));
#define assert(e) ((e) ? (void)0 : __assert_func(__FILE__, __LINE__, __func__, #e))
#endif
