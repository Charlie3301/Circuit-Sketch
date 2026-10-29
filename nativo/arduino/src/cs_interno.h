/* cs_interno.h -- o que os arquivos do core compartilham entre si. */
#ifndef CS_INTERNO_H
#define CS_INTERNO_H

#include "Arduino.h"

String cs_texto(const char *alvo, const char *nome, int n, const cs_val *args);
/* O relogio da placa (us), sem o interpretador; negativo: a placa esta parando. */
int64_t cs_relogio(void);
int cs_registrar_rotina(void (*f)(void), void (*fa)(void *), void *arg);

/* (O nucleo.cpp os define dentro do seu extern "C".) */
extern "C" {
/* A interrupcao ligada em cada pino: a rotina registrada, o modo, e se esta
 * ligada (o disableInterrupt desliga sem esquecer). */
#define CS_PINOS_COM_INTERRUPCAO 64
struct cs_interrupcao_t {
    int rotina;
    int modo;
    bool ligada;
};
extern cs_interrupcao_t cs_interrupcoes[CS_PINOS_COM_INTERRUPCAO];
void cs_religar_interrupcao(uint8_t pino);

/* O LEDC de cada pino (ou canal): a frequencia, os bits e se a saida e
 * invertida (o ledcOutputInvert). */
struct cs_ledc_t {
    uint32_t freq;
    uint8_t bits;
    bool invertido;
};
extern cs_ledc_t cs_ledc[CS_PINOS_COM_INTERRUPCAO];
}

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
