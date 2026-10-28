/* cs_interno.h -- o que os arquivos do core compartilham entre si. */
#ifndef CS_INTERNO_H
#define CS_INTERNO_H

#include "Arduino.h"

String cs_texto(const char *alvo, const char *nome, int n, const cs_val *args);
/* O relogio da placa (us), sem o interpretador; negativo: a placa esta parando. */
int64_t cs_relogio(void);
int cs_registrar_rotina(void (*f)(void), void (*fa)(void *), void *arg);

/* Chamar o simulador com os argumentos na hora: ch_i(alvo, nome, cs_i(1), ...). */
template <class... T> static inline int64_t ch_i(const char *alvo, const char *nome, T... v)
{
    cs_val a[] = {v..., cs_val{}};
    return cs_int(alvo, nome, (int)sizeof...(T), a);
}
template <class... T> static inline double ch_r(const char *alvo, const char *nome, T... v)
{
    cs_val a[] = {v..., cs_val{}};
    return cs_real(alvo, nome, (int)sizeof...(T), a);
}
template <class... T> static inline void ch(const char *alvo, const char *nome, T... v)
{
    cs_val a[] = {v..., cs_val{}};
    cs_nada(alvo, nome, (int)sizeof...(T), a);
}
template <class... T> static inline String ch_t(const char *alvo, const char *nome, T... v)
{
    cs_val a[] = {v..., cs_val{}};
    return cs_texto(alvo, nome, (int)sizeof...(T), a);
}

#endif
