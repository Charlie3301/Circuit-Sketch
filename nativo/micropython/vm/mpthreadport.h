/* mpthreadport.h -- as threads do MicroPython (o _thread) sobre o FreeRTOS do simulador, como no
 * port da ESP32: cada thread e uma tarefa, e a trava (o GIL, o Lock) e um semaforo. Na Pico de
 * verdade a thread roda no segundo nucleo; aqui e a mesma tarefa. */
#ifndef CS_MPTHREADPORT_H
#define CS_MPTHREADPORT_H

#include "freertos/FreeRTOS.h"
#include "freertos/task.h"
#include "freertos/semphr.h"

typedef struct _mp_thread_mutex_t {
    SemaphoreHandle_t handle;
} mp_thread_mutex_t;

void mp_thread_init(void *topo_da_pilha);
void mp_thread_gc_others(void);
void *mp_thread_topo_desta(void);

#endif
