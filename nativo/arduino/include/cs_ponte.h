/* cs_ponte.h -- a ponte entre o sketch compilado de verdade e o simulador.
 *
 * O sketch em C/C++ e compilado pelo clang (o do Zig, em nativo/zig) e roda
 * nativo no PC. Tudo o que ele faria no hardware -- um pino, o relogio, a
 * serial, o I2C -- vira uma chamada daqui, que o simulador atende com a MESMA
 * implementacao que o interpretador usa: "delay" e "Serial.print" fazem
 * exatamente a mesma coisa nos dois caminhos.
 *
 * A ordem dos campos e contrato com o lado C# (motor/Nativo/SketchNativo.cs):
 * mudou aqui, muda la, na mesma posicao.
 *
 * Regra de ouro: o C# nunca salta nem lanca para dentro do C. Ele devolve um
 * codigo; e o C que sai (o salto de cs_ponte.cpp), sempre por quadros de C. */
#ifndef CS_PONTE_H
#define CS_PONTE_H

#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

/* O tipo de cada argumento e da volta. */
enum {
    CS_V_NADA = 0,
    CS_V_INT = 1,
    CS_V_REAL = 2,
    CS_V_TEXTO = 3,     /* s com n bytes (UTF-8) */
    CS_V_BOOL = 4,
    CS_V_CHAR = 5,
    CS_V_NOME = 6,      /* o NOME de uma funcao ("attachInterrupt(2, contar, RISING)") */
};

typedef struct cs_val {
    int32_t tipo;
    int32_t n;
    int64_t i;
    double d;
    const char *s;
} cs_val;

/* O que a chamada devolve: 0 deu certo; 1 o simulador lancou (a placa parou,
 * reiniciou ou deu erro -- o C# guardou o motivo); o C sai na hora. */
enum { CS_OK = 0, CS_SAIR = 1, CS_FALHA = 3 };

typedef struct cs_ponte {
    int32_t versao;
    int32_t tamanho;            /* sizeof(cs_ponte), para conferir */
    void *ctx;
    /* Uma funcao do core simulado. alvo NULL: funcao livre ("delay"); senao,
     * o objeto ("Serial", "Wire"). O texto da volta mora no simulador e vale
     * ate a proxima chamada. */
    int (*chamar)(void *ctx, const char *alvo, const char *nome, int n, const cs_val *args, cs_val *volta);
    /* O relogio da placa, sem passar pelo interpretador (micros() num laco
     * apertado). Negativo: a placa esta parando (o C sai). */
    int64_t (*micros)(void *ctx);
} cs_ponte;

#define CS_VERSAO_DA_PONTE 1

/* Do lado C (cs_ponte.cpp). */
extern const cs_ponte *cs_p;
int cs_chamar(const char *alvo, const char *nome, int n, const cs_val *args, cs_val *volta);
void cs_sair_com(int codigo);

static inline cs_val cs_i(int64_t v) { cs_val r = {CS_V_INT, 0, v, 0, 0}; return r; }
static inline cs_val cs_r(double v) { cs_val r = {CS_V_REAL, 0, 0, v, 0}; return r; }
static inline cs_val cs_b(int v) { cs_val r = {CS_V_BOOL, 0, v ? 1 : 0, 0, 0}; return r; }
static inline cs_val cs_c(int v) { cs_val r = {CS_V_CHAR, 0, v & 0xFF, 0, 0}; return r; }
cs_val cs_t(const char *s);
cs_val cs_tn(const char *s, int n);
cs_val cs_nome(const char *s);

/* Atalhos: a volta como numero. */
int64_t cs_int(const char *alvo, const char *nome, int n, const cs_val *args);
double cs_real(const char *alvo, const char *nome, int n, const cs_val *args);
void cs_nada(const char *alvo, const char *nome, int n, const cs_val *args);

#ifdef __cplusplus
}
#endif

#endif
