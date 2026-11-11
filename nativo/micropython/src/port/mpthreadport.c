/* mpthreadport.c -- o _thread do MicroPython sobre o FreeRTOS do simulador (adaptado do
 * ports/esp32/mpthreadport.c do MicroPython, licenca MIT). Cada thread do Python e uma tarefa; o
 * estado de cada uma (mp_state_thread_t) fica numa variavel da propria thread; o coletor, que roda
 * com as outras paradas na trava, olha a pilha e os registradores de cada uma (cs_py_threads_win.c). */
#include <stdio.h>
#include "py/runtime.h"
#include "py/gc.h"
#include "py/mpthread.h"
#include "py/mphal.h"
#include "mpthreadport.h"

#if MICROPY_PY_THREAD

/* A pilha de cada thread: na placa sao 4 KB, mas o quadro de cada funcao do C aqui e bem maior. */
#define TAMANHO_DA_PILHA (256 * 1024)
#define PRIORIDADE_DA_THREAD (1)

void *cs_py_thread_identidade(void);
void *cs_py_thread_topo(void);
int cs_py_thread_parar(void *thread, void **registradores, int quantos, void **pilha);
void cs_py_thread_seguir(void *thread);
void cs_py_thread_soltar(void *thread);

typedef enum {
    MP_THREAD_RUN_STATE_NEW,
    MP_THREAD_RUN_STATE_RUNNING,
    MP_THREAD_RUN_STATE_FINISHED,
} mp_thread_run_state_t;

typedef struct _mp_thread_t {
    TaskHandle_t id;                 /* a tarefa */
    mp_thread_run_state_t run_state;
    void *(*entrada)(void *);
    void *arg;                       /* os argumentos do Python: uma raiz do coletor */
    void *windows;                   /* a thread do Windows (para o coletor para-la) */
    void *topo;                      /* onde a pilha dela comeca */
    struct _mp_thread_t *next;
} mp_thread_t;

static mp_thread_mutex_t thread_mutex;
static mp_thread_t thread_entry0;
static mp_thread_t *thread = NULL; /* raiz: o mp_thread_gc_others marca */

/* O estado da thread que esta rodando. Quem nao e thread do Python (a rotina de interrupcao que
 * so agenda) ve o da principal. */
static __thread mp_state_thread_t *estado_desta;
/* Onde a pilha da thread que esta rodando comeca: o coletor olha dali para baixo. */
static __thread void *topo_desta;

void *mp_thread_topo_desta(void) { return topo_desta; }

void mp_thread_init(void *topo_da_pilha)
{
    mp_thread_set_state(&mp_state_ctx.thread);
    thread_entry0.id = xTaskGetCurrentTaskHandle();
    thread_entry0.run_state = MP_THREAD_RUN_STATE_RUNNING;
    thread_entry0.arg = NULL;
    thread_entry0.windows = cs_py_thread_identidade();
    thread_entry0.topo = topo_da_pilha;
    topo_desta = topo_da_pilha;
    thread_entry0.next = NULL;
    thread = &thread_entry0;
    mp_thread_mutex_init(&thread_mutex);
}

void mp_thread_gc_others(void)
{
    mp_thread_mutex_lock(&thread_mutex, 1);
    for (mp_thread_t *th = thread; th != NULL; th = th->next) {
        gc_collect_root((void **)&th, 1);
        gc_collect_root(&th->arg, 1);
        if (th->id == xTaskGetCurrentTaskHandle()) continue;
        if (th->run_state != MP_THREAD_RUN_STATE_RUNNING) continue;
        /* A outra esta parada (na trava, numa espera): os registradores dela e a pilha, do ponto
         * em que parou ate o comeco. */
        void *registradores[20];
        void *pilha = NULL;
        int quantos = cs_py_thread_parar(th->windows, registradores, 20, &pilha);
        if (quantos > 0 && pilha && (uintptr_t)pilha < (uintptr_t)th->topo) {
            gc_collect_root(registradores, quantos);
            uintptr_t base = (uintptr_t)pilha & ~(uintptr_t)(sizeof(void *) - 1);
            gc_collect_root((void **)base, ((uintptr_t)th->topo - base) / sizeof(void *));
        }
        cs_py_thread_seguir(th->windows);
    }
    mp_thread_mutex_unlock(&thread_mutex);
}

mp_state_thread_t *mp_thread_get_state(void)
{
    return estado_desta ? estado_desta : &mp_state_ctx.thread;
}

void mp_thread_set_state(mp_state_thread_t *state)
{
    estado_desta = state;
}

mp_uint_t mp_thread_get_id(void)
{
    return (mp_uint_t)xTaskGetCurrentTaskHandle();
}

void mp_thread_start(void)
{
    mp_thread_mutex_lock(&thread_mutex, 1);
    for (mp_thread_t *th = thread; th != NULL; th = th->next) {
        if (th->id == xTaskGetCurrentTaskHandle()) {
            th->run_state = MP_THREAD_RUN_STATE_RUNNING;
            break;
        }
    }
    mp_thread_mutex_unlock(&thread_mutex);
}

static void entrada_da_tarefa(void *arg)
{
    mp_thread_t *th = arg;
    /* A identidade e o topo antes de qualquer codigo do Python: o coletor pode passar logo. */
    void *windows = cs_py_thread_identidade();
    th->topo = topo_desta = cs_py_thread_topo();
    th->windows = windows;
    th->entrada(th->arg);
    /* Daqui em diante o th ja pode ter sido recolhido (saiu da lista no mp_thread_finish). */
    cs_py_thread_soltar(windows);
    vTaskDelete(NULL);
}

mp_uint_t mp_thread_create(void *(*entry)(void *), void *arg, size_t *stack_size)
{
    if (*stack_size < TAMANHO_DA_PILHA) *stack_size = TAMANHO_DA_PILHA;
    mp_thread_t *th = m_new_obj(mp_thread_t);
    th->run_state = MP_THREAD_RUN_STATE_NEW;
    th->entrada = entry;
    th->arg = arg;
    th->windows = NULL;
    th->topo = NULL;
    mp_thread_mutex_lock(&thread_mutex, 1);
    th->next = thread;
    thread = th;
    BaseType_t criou = xTaskCreate(entrada_da_tarefa, "mp_thread", (uint32_t)(*stack_size / sizeof(StackType_t)), th, PRIORIDADE_DA_THREAD, &th->id);
    if (criou != pdPASS) {
        thread = th->next;
        mp_thread_mutex_unlock(&thread_mutex);
        mp_raise_msg(&mp_type_OSError, MP_ERROR_TEXT("can't create thread"));
    }
    mp_thread_mutex_unlock(&thread_mutex);
    return (mp_uint_t)th->id;
}

void mp_thread_finish(void)
{
    mp_thread_mutex_lock(&thread_mutex, 1);
    for (mp_thread_t **th = &thread; *th != NULL; th = &(*th)->next) {
        if ((*th)->id == xTaskGetCurrentTaskHandle()) {
            (*th)->run_state = MP_THREAD_RUN_STATE_FINISHED;
            *th = (*th)->next;
            break;
        }
    }
    mp_thread_mutex_unlock(&thread_mutex);
}

void mp_thread_mutex_init(mp_thread_mutex_t *mutex)
{
    mutex->handle = xSemaphoreCreateBinary();
    xSemaphoreGive(mutex->handle);
}

int mp_thread_mutex_lock(mp_thread_mutex_t *mutex, int wait)
{
    return pdTRUE == xSemaphoreTake(mutex->handle, wait ? portMAX_DELAY : 0);
}

void mp_thread_mutex_unlock(mp_thread_mutex_t *mutex)
{
    xSemaphoreGive(mutex->handle);
    /* A vez passa para quem esperava a trava: sem isto a thread que so faz conta nunca a soltaria. */
    taskYIELD();
}

#endif /* MICROPY_PY_THREAD */
