/* cs_freertos.c -- o FreeRTOS da ESP32 e o relogio.
 *
 * Tarefa e thread do PC; o tick e de 1 ms. Semaforos, filas e grupos de
 * eventos ficam no simulador, com os mesmos tempos de espera -- e quem
 * esta esperando quando a placa para, sai. As notificacoes e as secoes
 * criticas sao daqui. Os temporizadores (os do FreeRTOS e os do
 * esp_timer) rodam numa tarefa so, como o "Tmr Svc" e o "esp_timer". */
#include <stdint.h>
#include <stddef.h>
#include <string.h>
#include <stdlib.h>
#include <stdio.h>
#include <errno.h>
#include <time.h>
#include <sys/time.h>
#include <unistd.h>
#include <sched.h>
#include "cs_host.h"
#include "cs_interno.h"
#include "freertos/FreeRTOS.h"
#include "freertos/task.h"
#include "freertos/queue.h"
#include "freertos/semphr.h"
#include "freertos/event_groups.h"
#include "freertos/timers.h"
#include "esp_heap_caps.h"

/* ---------------------------------------------------------------------
 * tempo */
int64_t cs_micros(void)
{
    int64_t t = cs->micros();
    CS_CONFERIR();
    return t;
}

void cs_dormir_us(int64_t us)
{
    if (cs->dormir(us) < 0) cs_encerrar_tarefa();
    cs_conferir_parada();
}

int64_t cs_espera_us(uint32_t ticks)
{
    if (ticks == portMAX_DELAY) return -1;
    return (int64_t)ticks * (1000000 / configTICK_RATE_HZ);
}

int cs_sem_pegar(int64_t s, int64_t espera_us)
{
    int r = cs->sem_pegar(s, espera_us);
    if (r < 0) cs_encerrar_tarefa();
    return r;
}

/* ---------------------------------------------------------------------
 * tarefas */
BaseType_t xTaskCreatePinnedToCore(TaskFunction_t funcao, const char *nome, uint32_t pilha, void *argumento,
                                   UBaseType_t prioridade, TaskHandle_t *criada, BaseType_t nucleo)
{
    int n = (nucleo == tskNO_AFFINITY || nucleo < 0 || nucleo > 1) ? 0 : (int)nucleo;
    cs_tarefa *t = cs_nova_tarefa(nome, funcao, argumento, (int)prioridade, n, (int)pilha);
    if (!t) return errCOULD_NOT_ALLOCATE_REQUIRED_MEMORY;
    if (criada) *criada = t;
    if (!cs->tarefa_criar(t, (int)pilha)) {
        if (criada) *criada = NULL;
        return errCOULD_NOT_ALLOCATE_REQUIRED_MEMORY;
    }
    CS_CONFERIR();
    return pdPASS;
}

BaseType_t xTaskCreate(TaskFunction_t funcao, const char *nome, uint32_t pilha, void *argumento,
                       UBaseType_t prioridade, TaskHandle_t *criada)
{
    return xTaskCreatePinnedToCore(funcao, nome, pilha, argumento, prioridade, criada, tskNO_AFFINITY);
}

void vTaskDelete(TaskHandle_t t)
{
    cs_tarefa *eu = cs_tarefa_atual();
    if (t == NULL || t == eu) cs_encerrar_tarefa();
    t->apagar = 1;
    cs->tarefa_acordar(t);
    CS_CONFERIR();
}

void vTaskDelay(TickType_t ticks)
{
    if (ticks == 0) {
        cs->ceder();
        cs_conferir_parada();
        return;
    }
    cs_dormir_us(cs_espera_us(ticks));
}

BaseType_t xTaskDelayUntil(TickType_t *anterior, TickType_t periodo)
{
    TickType_t alvo = *anterior + periodo;
    TickType_t agora = xTaskGetTickCount();
    *anterior = alvo;
    int32_t falta = (int32_t)(alvo - agora);
    if (falta <= 0 || falta > (int32_t)periodo) {
        cs_conferir_parada();
        return pdFALSE;
    }
    cs_dormir_us((int64_t)falta * 1000);
    return pdTRUE;
}

void vTaskDelayUntil(TickType_t *anterior, TickType_t periodo)
{
    xTaskDelayUntil(anterior, periodo);
}

TickType_t xTaskGetTickCount(void)
{
    return (TickType_t)(cs_micros() / 1000);
}

TickType_t xTaskGetTickCountFromISR(void)
{
    return xTaskGetTickCount();
}

TaskHandle_t xTaskGetCurrentTaskHandle(void)
{
    return cs_tarefa_atual();
}

TaskHandle_t xTaskGetCurrentTaskHandleForCore(BaseType_t nucleo)
{
    (void)nucleo;
    return cs_tarefa_atual();
}

TaskHandle_t xTaskGetIdleTaskHandleForCore(BaseType_t nucleo)
{
    (void)nucleo;
    return NULL;
}

char *pcTaskGetName(TaskHandle_t t)
{
    if (!t) t = cs_tarefa_atual();
    return t ? t->nome : "";
}

/* Quanto da pilha nunca foi usado. No PC a pilha e outra (o codigo de 64
 * bits gasta mais); a conta aqui so diz que ainda ha folga. */
UBaseType_t uxTaskGetStackHighWaterMark(TaskHandle_t t)
{
    if (!t) t = cs_tarefa_atual();
    if (!t || t->pilha <= 0) return 1024;
    return (UBaseType_t)(t->pilha / 2);
}

UBaseType_t uxTaskPriorityGet(TaskHandle_t t)
{
    if (!t) t = cs_tarefa_atual();
    return t ? (UBaseType_t)t->prioridade : 0;
}

void vTaskPrioritySet(TaskHandle_t t, UBaseType_t prioridade)
{
    if (!t) t = cs_tarefa_atual();
    if (t) t->prioridade = (int)prioridade;
}

void vTaskSuspend(TaskHandle_t t)
{
    cs_tarefa *eu = cs_tarefa_atual();
    if (!t) t = eu;
    t->suspender = 1;
    if (t == eu) cs_conferir_parada();
}

void vTaskResume(TaskHandle_t t)
{
    if (!t || !t->suspender) return;
    t->suspender = 0;
    cs->sem_dar(t->retomar);
}

BaseType_t xTaskResumeFromISR(TaskHandle_t t)
{
    vTaskResume(t);
    return pdFALSE;
}

/* Parar o escalonador nao tem como: as threads sao do Windows. */
void vTaskSuspendAll(void) {}
BaseType_t xTaskResumeAll(void) { return pdFALSE; }

void taskYIELD(void)
{
    cs->ceder();
    cs_conferir_parada();
}

void vTaskStartScheduler(void) {}

eTaskState eTaskGetState(TaskHandle_t t)
{
    if (!t) return eInvalid;
    if (t == cs_tarefa_atual()) return eRunning;
    if (t->estado == 4) return eDeleted;
    if (t->suspender) return eSuspended;
    return eReady;
}

int cs_numero_de_tarefas(void);

UBaseType_t uxTaskGetNumberOfTasks(void)
{
    /* Mais as que o ESP-IDF sempre tem: IDLE0, IDLE1, ipc0, ipc1, esp_timer. */
    return (UBaseType_t)(cs_numero_de_tarefas() + 5);
}

BaseType_t xTaskGetAffinity(TaskHandle_t t)
{
    if (!t) t = cs_tarefa_atual();
    return t ? t->nucleo : 0;
}

BaseType_t xPortGetCoreID(void)
{
    cs_tarefa *t = cs_tarefa_atual();
    return t ? t->nucleo : 0;
}

BaseType_t xPortInIsrContext(void)
{
    cs_tarefa *t = cs_tarefa_atual();
    return t ? t->em_isr : 0;
}

/* ---------------------------------------------------------------------
 * notificacoes */
static cs_giro trava_das_notificacoes;

BaseType_t xTaskNotify(TaskHandle_t t, uint32_t valor, eNotifyAction acao)
{
    if (!t) return pdFAIL;
    int ok = 1;
    cs_girar(&trava_das_notificacoes);
    switch (acao) {
    case eSetBits: t->notificacao |= valor; break;
    case eIncrement: t->notificacao++; break;
    case eSetValueWithOverwrite: t->notificacao = valor; break;
    case eSetValueWithoutOverwrite:
        if (t->notificada) ok = 0;
        else t->notificacao = valor;
        break;
    default: break;
    }
    if (ok) t->notificada = 1;
    cs_soltar(&trava_das_notificacoes);
    if (ok) cs->sem_dar(t->aviso);
    CS_CONFERIR();
    return ok ? pdPASS : pdFAIL;
}

BaseType_t xTaskNotifyFromISR(TaskHandle_t t, uint32_t valor, eNotifyAction acao, BaseType_t *acordou)
{
    if (acordou) *acordou = pdFALSE;
    return xTaskNotify(t, valor, acao);
}

BaseType_t xTaskNotifyGive(TaskHandle_t t)
{
    return xTaskNotify(t, 0, eIncrement);
}

void vTaskNotifyGiveFromISR(TaskHandle_t t, BaseType_t *acordou)
{
    if (acordou) *acordou = pdFALSE;
    xTaskNotify(t, 0, eIncrement);
}

uint32_t ulTaskNotifyTake(BaseType_t zerar, TickType_t espera)
{
    cs_tarefa *t = cs_tarefa_atual();
    int64_t limite = espera == portMAX_DELAY ? -1 : cs_micros() + cs_espera_us(espera);
    for (;;) {
        cs_girar(&trava_das_notificacoes);
        uint32_t v = t->notificacao;
        if (v != 0) {
            t->notificacao = zerar ? 0 : v - 1;
            if (t->notificacao == 0) t->notificada = 0;
            cs_soltar(&trava_das_notificacoes);
            return v;
        }
        t->notificada = 0;
        cs_soltar(&trava_das_notificacoes);

        int64_t falta = limite < 0 ? -1 : limite - cs_micros();
        if (limite >= 0 && falta <= 0) return 0;
        cs_sem_pegar(t->aviso, falta);
    }
}

BaseType_t xTaskNotifyWait(uint32_t limpar_entrada, uint32_t limpar_saida, uint32_t *valor, TickType_t espera)
{
    cs_tarefa *t = cs_tarefa_atual();
    cs_girar(&trava_das_notificacoes);
    if (!t->notificada) t->notificacao &= ~limpar_entrada;
    cs_soltar(&trava_das_notificacoes);

    int64_t limite = espera == portMAX_DELAY ? -1 : cs_micros() + cs_espera_us(espera);
    for (;;) {
        cs_girar(&trava_das_notificacoes);
        if (t->notificada) {
            if (valor) *valor = t->notificacao;
            t->notificacao &= ~limpar_saida;
            t->notificada = 0;
            cs_soltar(&trava_das_notificacoes);
            return pdTRUE;
        }
        cs_soltar(&trava_das_notificacoes);
        int64_t falta = limite < 0 ? -1 : limite - cs_micros();
        if (limite >= 0 && falta <= 0) break;
        cs_sem_pegar(t->aviso, falta);
    }
    if (valor) *valor = t->notificacao;
    return pdFALSE;
}

/* ---------------------------------------------------------------------
 * secoes criticas: trava de giro com dona, que a mesma tarefa pode pegar
 * de novo (como o portMUX da ESP32) */
void vPortEnterCritical(portMUX_TYPE *m)
{
    int eu = cs_id_da_tarefa(cs_tarefa_atual());
    if (eu == 0) eu = -1;
    if (m->owner == eu) {
        m->count++;
        return;
    }
    int voltas = 0;
    while (cs_cas(&m->owner, 0, eu) != 0) {
        cs_pausa();
        if (++voltas > 2000) {
            voltas = 0;
            if (cs_parando) cs_encerrar_tarefa();
            cs->ceder();
        }
    }
    m->count = 1;
}

void vPortExitCritical(portMUX_TYPE *m)
{
    if (--m->count <= 0) {
        m->count = 0;
        cs_trocar(&m->owner, 0);
    }
}

/* ---------------------------------------------------------------------
 * semaforos */
#define SEM(s) ((int64_t)(intptr_t)(s))

static SemaphoreHandle_t criar_semaforo(int tipo, int maximo, int inicial)
{
    int64_t s = cs->sem_criar(tipo, maximo, inicial);
    CS_CONFERIR();
    return s > 0 ? (SemaphoreHandle_t)(intptr_t)s : NULL;
}

SemaphoreHandle_t xSemaphoreCreateBinary(void) { return criar_semaforo(CS_SEM_CONTAGEM, 1, 0); }
SemaphoreHandle_t xSemaphoreCreateMutex(void) { return criar_semaforo(CS_SEM_MUTEX, 1, 1); }
SemaphoreHandle_t xSemaphoreCreateRecursiveMutex(void) { return criar_semaforo(CS_SEM_RECURSIVO, 1, 1); }

SemaphoreHandle_t xSemaphoreCreateCounting(UBaseType_t maximo, UBaseType_t inicial)
{
    return criar_semaforo(CS_SEM_CONTAGEM, (int)maximo, (int)inicial);
}

void vSemaphoreDelete(SemaphoreHandle_t s)
{
    if (s) cs->sem_apagar(SEM(s));
}

BaseType_t xSemaphoreTake(SemaphoreHandle_t s, TickType_t espera)
{
    if (!s) return pdFALSE;
    int r = cs_sem_pegar(SEM(s), cs_espera_us(espera));
    CS_CONFERIR();
    return r > 0 ? pdTRUE : pdFALSE;
}

BaseType_t xSemaphoreGive(SemaphoreHandle_t s)
{
    if (!s) return pdFALSE;
    int r = cs->sem_dar(SEM(s));
    CS_CONFERIR();
    return r > 0 ? pdTRUE : pdFALSE;
}

BaseType_t xSemaphoreTakeFromISR(SemaphoreHandle_t s, BaseType_t *acordou)
{
    if (acordou) *acordou = pdFALSE;
    if (!s) return pdFALSE;
    return cs->sem_pegar(SEM(s), 0) > 0 ? pdTRUE : pdFALSE;
}

BaseType_t xSemaphoreGiveFromISR(SemaphoreHandle_t s, BaseType_t *acordou)
{
    if (acordou) *acordou = pdFALSE;
    if (!s) return pdFALSE;
    return cs->sem_dar(SEM(s)) > 0 ? pdTRUE : pdFALSE;
}

BaseType_t xSemaphoreTakeRecursive(SemaphoreHandle_t s, TickType_t espera) { return xSemaphoreTake(s, espera); }
BaseType_t xSemaphoreGiveRecursive(SemaphoreHandle_t s) { return xSemaphoreGive(s); }

UBaseType_t uxSemaphoreGetCount(SemaphoreHandle_t s)
{
    return s ? (UBaseType_t)cs->sem_contagem(SEM(s)) : 0;
}

TaskHandle_t xSemaphoreGetMutexHolder(SemaphoreHandle_t s)
{
    return s ? cs->sem_dono(SEM(s)) : NULL;
}

/* ---------------------------------------------------------------------
 * filas */
#define FILA(f) ((int64_t)(intptr_t)(f))

QueueHandle_t xQueueCreate(UBaseType_t tamanho, UBaseType_t item)
{
    if (tamanho == 0) return NULL;
    /* A fila gasta RAM interna na placa: tamanho x item. */
    void *reserva = cs_alocar((size_t)tamanho * (item ? item : 1) + 80, MALLOC_CAP_INTERNAL);
    if (!reserva) return NULL;
    cs_liberar(reserva);
    int64_t f = cs->fila_criar((int)tamanho, (int)item);
    CS_CONFERIR();
    return f > 0 ? (QueueHandle_t)(intptr_t)f : NULL;
}

void vQueueDelete(QueueHandle_t f)
{
    if (f) cs->fila_apagar(FILA(f));
}

static BaseType_t enviar(QueueHandle_t f, const void *item, int64_t espera, int modo)
{
    if (!f) return errQUEUE_FULL;
    int r = cs->fila_enviar(FILA(f), item, espera, modo);
    if (r < 0) cs_encerrar_tarefa();
    CS_CONFERIR();
    return r > 0 ? pdPASS : errQUEUE_FULL;
}

BaseType_t xQueueSend(QueueHandle_t f, const void *item, TickType_t espera) { return enviar(f, item, cs_espera_us(espera), CS_FILA_TRAS); }
BaseType_t xQueueSendToBack(QueueHandle_t f, const void *item, TickType_t espera) { return enviar(f, item, cs_espera_us(espera), CS_FILA_TRAS); }
BaseType_t xQueueSendToFront(QueueHandle_t f, const void *item, TickType_t espera) { return enviar(f, item, cs_espera_us(espera), CS_FILA_FRENTE); }
BaseType_t xQueueOverwrite(QueueHandle_t f, const void *item) { return enviar(f, item, 0, CS_FILA_SOBRESCREVER); }

BaseType_t xQueueSendFromISR(QueueHandle_t f, const void *item, BaseType_t *acordou)
{
    if (acordou) *acordou = pdFALSE;
    return enviar(f, item, 0, CS_FILA_TRAS);
}

BaseType_t xQueueSendToBackFromISR(QueueHandle_t f, const void *item, BaseType_t *acordou)
{
    return xQueueSendFromISR(f, item, acordou);
}

BaseType_t xQueueSendToFrontFromISR(QueueHandle_t f, const void *item, BaseType_t *acordou)
{
    if (acordou) *acordou = pdFALSE;
    return enviar(f, item, 0, CS_FILA_FRENTE);
}

BaseType_t xQueueOverwriteFromISR(QueueHandle_t f, const void *item, BaseType_t *acordou)
{
    if (acordou) *acordou = pdFALSE;
    return enviar(f, item, 0, CS_FILA_SOBRESCREVER);
}

static BaseType_t receber(QueueHandle_t f, void *item, int64_t espera, int espiar)
{
    if (!f) return pdFALSE;
    int r = cs->fila_receber(FILA(f), item, espera, espiar);
    if (r < 0) cs_encerrar_tarefa();
    CS_CONFERIR();
    return r > 0 ? pdTRUE : pdFALSE;
}

BaseType_t xQueueReceive(QueueHandle_t f, void *item, TickType_t espera) { return receber(f, item, cs_espera_us(espera), 0); }
BaseType_t xQueuePeek(QueueHandle_t f, void *item, TickType_t espera) { return receber(f, item, cs_espera_us(espera), 1); }

BaseType_t xQueueReceiveFromISR(QueueHandle_t f, void *item, BaseType_t *acordou)
{
    if (acordou) *acordou = pdFALSE;
    return receber(f, item, 0, 0);
}

UBaseType_t uxQueueMessagesWaiting(QueueHandle_t f) { return f ? (UBaseType_t)cs->fila_contagem(FILA(f)) : 0; }
UBaseType_t uxQueueMessagesWaitingFromISR(QueueHandle_t f) { return uxQueueMessagesWaiting(f); }
UBaseType_t uxQueueSpacesAvailable(QueueHandle_t f) { return f ? (UBaseType_t)cs->fila_livre(FILA(f)) : 0; }

BaseType_t xQueueReset(QueueHandle_t f)
{
    if (f) cs->fila_limpar(FILA(f));
    return pdPASS;
}

/* ---------------------------------------------------------------------
 * grupos de eventos */
#define GRUPO(g) ((int64_t)(intptr_t)(g))

EventGroupHandle_t xEventGroupCreate(void)
{
    int64_t g = cs->grupo_criar();
    CS_CONFERIR();
    return g > 0 ? (EventGroupHandle_t)(intptr_t)g : NULL;
}

void vEventGroupDelete(EventGroupHandle_t g)
{
    if (g) cs->grupo_apagar(GRUPO(g));
}

EventBits_t xEventGroupSetBits(EventGroupHandle_t g, EventBits_t bits)
{
    EventBits_t r = cs->grupo_mudar(GRUPO(g), bits, 0);
    CS_CONFERIR();
    return r;
}

EventBits_t xEventGroupClearBits(EventGroupHandle_t g, EventBits_t bits)
{
    EventBits_t r = cs->grupo_mudar(GRUPO(g), 0, bits);
    CS_CONFERIR();
    return r;
}

EventBits_t xEventGroupGetBits(EventGroupHandle_t g)
{
    return cs->grupo_mudar(GRUPO(g), 0, 0);
}

EventBits_t xEventGroupWaitBits(EventGroupHandle_t g, EventBits_t bits, BaseType_t limpar, BaseType_t todos,
                                TickType_t espera)
{
    int parar = 0;
    EventBits_t r = cs->grupo_esperar(GRUPO(g), bits, limpar, todos, cs_espera_us(espera), &parar);
    if (parar) cs_encerrar_tarefa();
    CS_CONFERIR();
    return r;
}

BaseType_t xEventGroupSetBitsFromISR(EventGroupHandle_t g, EventBits_t bits, BaseType_t *acordou)
{
    if (acordou) *acordou = pdFALSE;
    xEventGroupSetBits(g, bits);
    return pdPASS;
}

/* ---------------------------------------------------------------------
 * alarmes: uma tarefa so atende os temporizadores do FreeRTOS e os do
 * esp_timer, na ordem do relogio */
static cs_giro trava_dos_alarmes;
static cs_alarme *alarmes;
static int64_t acorda_alarmes;
static volatile int tarefa_dos_alarmes;

static void tirar_da_lista(cs_alarme *a)
{
    for (cs_alarme **p = &alarmes; *p; p = &(*p)->proximo)
        if (*p == a) {
            *p = a->proximo;
            a->proximo = NULL;
            return;
        }
}

static void por_na_lista(cs_alarme *a)
{
    cs_alarme **p = &alarmes;
    while (*p && (*p)->quando <= a->quando) p = &(*p)->proximo;
    a->proximo = *p;
    *p = a;
}

static void servico_dos_alarmes(void *arg)
{
    (void)arg;
    for (;;) {
        cs_girar(&trava_dos_alarmes);
        cs_alarme *a = alarmes;
        int64_t agora = cs->micros();
        if (a && a->quando <= agora) {
            alarmes = a->proximo;
            a->proximo = NULL;
            if (a->periodo > 0) {
                a->quando += a->periodo;
                if (a->quando < agora - a->periodo * 4) a->quando = agora + a->periodo;
                por_na_lista(a);
            } else {
                a->ativo = 0;
            }
            void (*funcao)(void *) = a->funcao;
            void *argumento = a->arg;
            cs_soltar(&trava_dos_alarmes);
            if (funcao) funcao(argumento);
            CS_CONFERIR();
            continue;
        }
        int64_t falta = a ? a->quando - agora : -1;
        cs_soltar(&trava_dos_alarmes);
        cs_sem_pegar(acorda_alarmes, falta);
        CS_CONFERIR();
    }
}

static void garantir_servico(void)
{
    if (cs_trocar(&tarefa_dos_alarmes, 1)) return;
    acorda_alarmes = cs->sem_criar(CS_SEM_CONTAGEM, 1, 0);
    xTaskCreatePinnedToCore(servico_dos_alarmes, "esp_timer", 4096, NULL, 22, NULL, 0);
}

void cs_alarme_armar(cs_alarme *a, int64_t daqui_us, int64_t periodo_us)
{
    garantir_servico();
    cs_girar(&trava_dos_alarmes);
    tirar_da_lista(a);
    a->quando = cs->micros() + (daqui_us > 0 ? daqui_us : 0);
    a->periodo = periodo_us;
    a->ativo = 1;
    por_na_lista(a);
    cs_soltar(&trava_dos_alarmes);
    cs->sem_dar(acorda_alarmes);
    CS_CONFERIR();
}

void cs_alarme_parar(cs_alarme *a)
{
    cs_girar(&trava_dos_alarmes);
    tirar_da_lista(a);
    a->ativo = 0;
    cs_soltar(&trava_dos_alarmes);
}

int cs_alarme_ativo(cs_alarme *a)
{
    return a->ativo;
}

/* ---------------------------------------------------------------------
 * temporizadores do FreeRTOS */
struct cs_temporizador {
    cs_alarme alarme;
    char nome[24];
    TickType_t periodo;
    UBaseType_t repetir;
    void *id;
    TimerCallbackFunction_t funcao;
};

static void disparar_temporizador(void *arg)
{
    struct cs_temporizador *t = (struct cs_temporizador *)arg;
    t->funcao(t);
}

TimerHandle_t xTimerCreate(const char *nome, TickType_t periodo, UBaseType_t repetir, void *id,
                           TimerCallbackFunction_t funcao)
{
    if (periodo == 0) return NULL;
    struct cs_temporizador *t = (struct cs_temporizador *)cs_alocar(sizeof(*t), MALLOC_CAP_INTERNAL);
    if (!t) return NULL;
    memset(t, 0, sizeof(*t));
    strncpy(t->nome, nome ? nome : "", sizeof(t->nome) - 1);
    t->periodo = periodo;
    t->repetir = repetir;
    t->id = id;
    t->funcao = funcao;
    t->alarme.funcao = disparar_temporizador;
    t->alarme.arg = t;
    return t;
}

BaseType_t xTimerStart(TimerHandle_t t, TickType_t espera)
{
    (void)espera;
    if (!t) return pdFAIL;
    int64_t p = cs_espera_us(t->periodo);
    cs_alarme_armar(&t->alarme, p, t->repetir ? p : 0);
    return pdPASS;
}

BaseType_t xTimerStop(TimerHandle_t t, TickType_t espera)
{
    (void)espera;
    if (!t) return pdFAIL;
    cs_alarme_parar(&t->alarme);
    return pdPASS;
}

BaseType_t xTimerReset(TimerHandle_t t, TickType_t espera) { return xTimerStart(t, espera); }

BaseType_t xTimerChangePeriod(TimerHandle_t t, TickType_t periodo, TickType_t espera)
{
    if (!t || periodo == 0) return pdFAIL;
    t->periodo = periodo;
    return xTimerStart(t, espera);
}

BaseType_t xTimerDelete(TimerHandle_t t, TickType_t espera)
{
    (void)espera;
    if (!t) return pdFAIL;
    cs_alarme_parar(&t->alarme);
    cs_liberar(t);
    return pdPASS;
}

BaseType_t xTimerIsTimerActive(TimerHandle_t t) { return t && t->alarme.ativo ? pdTRUE : pdFALSE; }
void *pvTimerGetTimerID(TimerHandle_t t) { return t ? t->id : NULL; }

/* ---------------------------------------------------------------------
 * time.h: a hora da ESP32 comeca em 1970 no boot, ate alguem acertar
 * (settimeofday, ou o SNTP) */
static int64_t epoca_no_boot_us;

int gettimeofday(struct timeval *tv, void *tz)
{
    (void)tz;
    int64_t t = epoca_no_boot_us + cs_micros();
    if (tv) {
        tv->tv_sec = (time_t)(t / 1000000);
        tv->tv_usec = (suseconds_t)(t % 1000000);
    }
    return 0;
}

int settimeofday(const struct timeval *tv, const struct timezone *tz)
{
    (void)tz;
    if (tv) epoca_no_boot_us = (int64_t)tv->tv_sec * 1000000 + tv->tv_usec - cs_micros();
    return 0;
}

time_t time(time_t *t)
{
    struct timeval tv;
    gettimeofday(&tv, NULL);
    if (t) *t = tv.tv_sec;
    return tv.tv_sec;
}

clock_t clock(void)
{
    return (clock_t)cs_micros();
}

double difftime(time_t fim, time_t inicio)
{
    return (double)(fim - inicio);
}

int clock_gettime(clockid_t relogio, struct timespec *ts)
{
    int64_t t = relogio == CLOCK_MONOTONIC ? cs_micros() : epoca_no_boot_us + cs_micros();
    ts->tv_sec = (time_t)(t / 1000000);
    ts->tv_nsec = (long)(t % 1000000) * 1000;
    return 0;
}

int nanosleep(const struct timespec *pedido, struct timespec *resto)
{
    if (resto) resto->tv_sec = resto->tv_nsec = 0;
    cs_dormir_us((int64_t)pedido->tv_sec * 1000000 + pedido->tv_nsec / 1000);
    return 0;
}

int usleep(useconds_t us)
{
    cs_dormir_us((int64_t)us);
    return 0;
}

unsigned int sleep(unsigned int segundos)
{
    cs_dormir_us((int64_t)segundos * 1000000);
    return 0;
}

int sched_yield(void)
{
    cs->ceder();
    cs_conferir_parada();
    return 0;
}

static const int dias_no_mes[12] = { 31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31 };

static int bissexto(int ano) { return (ano % 4 == 0 && ano % 100 != 0) || ano % 400 == 0; }

struct tm *gmtime_r(const time_t *t, struct tm *r)
{
    long long s = *t;
    long long dias = s / 86400;
    long long resto = s % 86400;
    if (resto < 0) {
        resto += 86400;
        dias--;
    }
    r->tm_hour = (int)(resto / 3600);
    r->tm_min = (int)(resto % 3600 / 60);
    r->tm_sec = (int)(resto % 60);
    r->tm_wday = (int)((dias + 4) % 7);
    if (r->tm_wday < 0) r->tm_wday += 7;

    int ano = 1970;
    for (;;) {
        int n = bissexto(ano) ? 366 : 365;
        if (dias >= n) { dias -= n; ano++; }
        else if (dias < 0) { ano--; dias += bissexto(ano) ? 366 : 365; }
        else break;
    }
    r->tm_year = ano - 1900;
    r->tm_yday = (int)dias;
    int mes = 0;
    for (;; mes++) {
        int n = dias_no_mes[mes] + (mes == 1 && bissexto(ano));
        if (dias < n) break;
        dias -= n;
    }
    r->tm_mon = mes;
    r->tm_mday = (int)dias + 1;
    r->tm_isdst = 0;
    return r;
}

static struct tm tm_compartilhada;

struct tm *gmtime(const time_t *t) { return gmtime_r(t, &tm_compartilhada); }

/* Sem fuso: a hora local e a UTC (o TZ da placa ainda nao entra aqui). */
struct tm *localtime_r(const time_t *t, struct tm *r) { return gmtime_r(t, r); }
struct tm *localtime(const time_t *t) { return gmtime_r(t, &tm_compartilhada); }

time_t mktime(struct tm *t)
{
    int ano = t->tm_year + 1900;
    int mes = t->tm_mon;
    ano += mes / 12;
    mes %= 12;
    if (mes < 0) { mes += 12; ano--; }
    long long dias = 0;
    for (int a = 1970; a < ano; a++) dias += bissexto(a) ? 366 : 365;
    for (int a = ano; a < 1970; a++) dias -= bissexto(a) ? 366 : 365;
    for (int m = 0; m < mes; m++) dias += dias_no_mes[m] + (m == 1 && bissexto(ano));
    dias += t->tm_mday - 1;
    time_t s = (time_t)(dias * 86400 + (long long)t->tm_hour * 3600 + (long long)t->tm_min * 60 + t->tm_sec);
    gmtime_r(&s, t);
    return s;
}

static const char *const dias_curtos[7] = { "Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat" };
static const char *const dias_longos[7] = { "Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday" };
static const char *const meses_curtos[12] = { "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" };
static const char *const meses_longos[12] = { "January", "February", "March", "April", "May", "June", "July",
                                              "August", "September", "October", "November", "December" };

size_t strftime(char *s, size_t n, const char *f, const struct tm *t)
{
    size_t k = 0;
    char tmp[64];
    for (; *f && k + 1 < n; f++) {
        if (*f != '%') {
            s[k++] = *f;
            continue;
        }
        f++;
        if (*f == 'E' || *f == 'O') f++;
        tmp[0] = 0;
        switch (*f) {
        case 'a': snprintf(tmp, sizeof(tmp), "%s", dias_curtos[t->tm_wday % 7]); break;
        case 'A': snprintf(tmp, sizeof(tmp), "%s", dias_longos[t->tm_wday % 7]); break;
        case 'b': case 'h': snprintf(tmp, sizeof(tmp), "%s", meses_curtos[t->tm_mon % 12]); break;
        case 'B': snprintf(tmp, sizeof(tmp), "%s", meses_longos[t->tm_mon % 12]); break;
        case 'c': snprintf(tmp, sizeof(tmp), "%s %s %2d %02d:%02d:%02d %d", dias_curtos[t->tm_wday % 7],
                           meses_curtos[t->tm_mon % 12], t->tm_mday, t->tm_hour, t->tm_min, t->tm_sec,
                           t->tm_year + 1900); break;
        case 'C': snprintf(tmp, sizeof(tmp), "%02d", (t->tm_year + 1900) / 100); break;
        case 'd': snprintf(tmp, sizeof(tmp), "%02d", t->tm_mday); break;
        case 'D': snprintf(tmp, sizeof(tmp), "%02d/%02d/%02d", t->tm_mon + 1, t->tm_mday, t->tm_year % 100); break;
        case 'e': snprintf(tmp, sizeof(tmp), "%2d", t->tm_mday); break;
        case 'F': snprintf(tmp, sizeof(tmp), "%d-%02d-%02d", t->tm_year + 1900, t->tm_mon + 1, t->tm_mday); break;
        case 'H': snprintf(tmp, sizeof(tmp), "%02d", t->tm_hour); break;
        case 'I': snprintf(tmp, sizeof(tmp), "%02d", t->tm_hour % 12 ? t->tm_hour % 12 : 12); break;
        case 'j': snprintf(tmp, sizeof(tmp), "%03d", t->tm_yday + 1); break;
        case 'm': snprintf(tmp, sizeof(tmp), "%02d", t->tm_mon + 1); break;
        case 'M': snprintf(tmp, sizeof(tmp), "%02d", t->tm_min); break;
        case 'n': snprintf(tmp, sizeof(tmp), "\n"); break;
        case 'p': snprintf(tmp, sizeof(tmp), "%s", t->tm_hour < 12 ? "AM" : "PM"); break;
        case 'r': snprintf(tmp, sizeof(tmp), "%02d:%02d:%02d %s", t->tm_hour % 12 ? t->tm_hour % 12 : 12,
                           t->tm_min, t->tm_sec, t->tm_hour < 12 ? "AM" : "PM"); break;
        case 'R': snprintf(tmp, sizeof(tmp), "%02d:%02d", t->tm_hour, t->tm_min); break;
        case 'S': snprintf(tmp, sizeof(tmp), "%02d", t->tm_sec); break;
        case 't': snprintf(tmp, sizeof(tmp), "\t"); break;
        case 'T': snprintf(tmp, sizeof(tmp), "%02d:%02d:%02d", t->tm_hour, t->tm_min, t->tm_sec); break;
        case 'u': snprintf(tmp, sizeof(tmp), "%d", t->tm_wday ? t->tm_wday : 7); break;
        case 'w': snprintf(tmp, sizeof(tmp), "%d", t->tm_wday); break;
        case 'x': snprintf(tmp, sizeof(tmp), "%02d/%02d/%02d", t->tm_mon + 1, t->tm_mday, t->tm_year % 100); break;
        case 'X': snprintf(tmp, sizeof(tmp), "%02d:%02d:%02d", t->tm_hour, t->tm_min, t->tm_sec); break;
        case 'y': snprintf(tmp, sizeof(tmp), "%02d", t->tm_year % 100); break;
        case 'Y': snprintf(tmp, sizeof(tmp), "%d", t->tm_year + 1900); break;
        case 'z': snprintf(tmp, sizeof(tmp), "+0000"); break;
        case 'Z': snprintf(tmp, sizeof(tmp), "UTC"); break;
        case '%': snprintf(tmp, sizeof(tmp), "%%"); break;
        default: tmp[0] = '%'; tmp[1] = *f; tmp[2] = 0; break;
        }
        for (char *p = tmp; *p && k + 1 < n; p++) s[k++] = *p;
        if (!*f) break;
    }
    if (n) s[k] = 0;
    return (*f == 0) ? k : 0;
}

static char texto_da_hora[32];

char *asctime(const struct tm *t)
{
    snprintf(texto_da_hora, sizeof(texto_da_hora), "%s %s %2d %02d:%02d:%02d %d\n",
             dias_curtos[t->tm_wday % 7], meses_curtos[t->tm_mon % 12], t->tm_mday, t->tm_hour, t->tm_min,
             t->tm_sec, t->tm_year + 1900);
    return texto_da_hora;
}

char *ctime(const time_t *t)
{
    return asctime(localtime(t));
}

void tzset(void) {}
