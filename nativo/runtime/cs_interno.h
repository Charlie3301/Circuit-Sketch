/* cs_interno.h -- o que as partes do runtime dividem entre si. Nao e
 * para o codigo do projeto incluir. */
#ifndef CS_INTERNO_H
#define CS_INTERNO_H

#include <stdint.h>
#include <stddef.h>
#include "cs_host.h"

/* ---- atomicos (x64) ---- */
static inline int cs_trocar(volatile int *p, int v)
{
    __asm__ __volatile__("xchgl %0, %1" : "+r"(v), "+m"(*p) : : "memory");
    return v;
}

static inline int cs_somar(volatile int *p, int v)
{
    __asm__ __volatile__("lock; xaddl %0, %1" : "+r"(v), "+m"(*p) : : "memory");
    return v;
}

/* Compara e troca: devolve o que estava la (== esperado quando trocou). */
static inline int cs_cas(volatile int *p, int esperado, int novo)
{
    int anterior;
    __asm__ __volatile__("lock; cmpxchgl %2, %1" : "=a"(anterior), "+m"(*p) : "r"(novo), "0"(esperado) : "memory");
    return anterior;
}

static inline void cs_pausa(void)
{
    __asm__ __volatile__("pause" : : : "memory");
}

/* Trava de giro. Quem gira confere a parada: uma tarefa encerrada com a
 * trava na mao nao pode prender as outras para sempre. */
typedef struct { volatile int trava; } cs_giro;

void cs_girar(cs_giro *g);
static inline void cs_soltar(cs_giro *g)
{
    cs_trocar(&g->trava, 0);
}

/* ---- memoria ---- */
void cs_memoria_iniciar(void *interna, size_t tam_interna, size_t tam_interna_placa,
                        void *psram, size_t tam_psram, size_t tam_psram_placa);
void *cs_alocar(size_t n, uint32_t caps);
void cs_liberar(void *p);

/* ---- tarefas ---- */
cs_tarefa *cs_nova_tarefa(const char *nome, void (*funcao)(void *), void *arg, int prioridade,
                          int nucleo, int pilha);
int cs_id_da_tarefa(cs_tarefa *t);

/* ---- tempo ---- */
int64_t cs_micros(void);
void cs_dormir_us(int64_t us);

/* ---- esperas do simulador: o que devolve -1 manda a tarefa embora ---- */
int cs_sem_pegar(int64_t s, int64_t espera_us);
int64_t cs_espera_us(uint32_t ticks);

/* ---- arquivos ---- */
void cs_arquivos_iniciar(void);

/* ---- temporizadores ---- */
typedef struct cs_alarme {
    void (*funcao)(void *);
    void *arg;
    int64_t quando;
    int64_t periodo;
    int ativo;
    int apagado;
    struct cs_alarme *proximo;
} cs_alarme;

void cs_alarme_armar(cs_alarme *a, int64_t daqui_us, int64_t periodo_us);
void cs_alarme_parar(cs_alarme *a);
int cs_alarme_ativo(cs_alarme *a);

#endif
