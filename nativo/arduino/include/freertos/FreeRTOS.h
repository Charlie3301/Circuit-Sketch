/* freertos/FreeRTOS.h -- o FreeRTOS do sketch compilado (o da ESP32, o do
 * ESP-IDF, a Arduino_FreeRTOS do Uno e o do RP2040).
 *
 * Cada tarefa e uma thread do Windows, mas so uma roda de cada vez, como
 * num nucleo: o escalonador (cs_freertos.cpp) passa a vez pela prioridade,
 * reparte o tempo entre as de mesma prioridade (1 tick = 1 ms) e acorda quem
 * espera pela fila, pelo semaforo ou pelo prazo. Todo o resto do simulador
 * continua vendo uma placa so. Este cabecalho tem a API inteira (task.h,
 * queue.h, semphr.h, timers.h, event_groups.h so o incluem). */
#ifndef INC_FREERTOS_H
#define INC_FREERTOS_H

#include <stdint.h>
#include <stddef.h>
#include <stdbool.h>
/* Na ESP32 o portmacro.h traz o BIT0..BIT31 (os bits do event group). */
#if __has_include("esp_bit_defs.h")
#include "esp_bit_defs.h"
#endif

#ifdef __cplusplus
extern "C" {
#endif

/* ---- os tipos e as constantes ---- */
typedef int32_t BaseType_t;
typedef uint32_t UBaseType_t;
typedef uint32_t TickType_t;
typedef uint32_t StackType_t;
typedef void (*TaskFunction_t)(void *);

#define portBASE_TYPE BaseType_t
#define portSTACK_TYPE StackType_t
#define portTickType TickType_t
#define pdFALSE ((BaseType_t)0)
#define pdTRUE ((BaseType_t)1)
#define pdPASS pdTRUE
#define pdFAIL pdFALSE
#define errQUEUE_EMPTY ((BaseType_t)0)
#define errQUEUE_FULL ((BaseType_t)0)
#define errCOULD_NOT_ALLOCATE_REQUIRED_MEMORY (-1)

#define configTICK_RATE_HZ 1000
#define CONFIG_FREERTOS_HZ 1000
#define configMAX_PRIORITIES 25
#define configMINIMAL_STACK_SIZE 768
#define configMAX_TASK_NAME_LEN 16
#define configTIMER_TASK_PRIORITY 1
#define configUSE_PREEMPTION 1
#define configUSE_TIME_SLICING 1
#define configUSE_MUTEXES 1
#define configUSE_RECURSIVE_MUTEXES 1
#define configUSE_COUNTING_SEMAPHORES 1
#define configUSE_TIMERS 1
#define configSUPPORT_STATIC_ALLOCATION 1
#define configSUPPORT_DYNAMIC_ALLOCATION 1
#define INCLUDE_vTaskDelete 1
#define INCLUDE_vTaskDelay 1
#define INCLUDE_vTaskSuspend 1
#define INCLUDE_xTaskGetSchedulerState 1
#define tskKERNEL_VERSION_NUMBER "V10.5.1"

#define portMAX_DELAY ((TickType_t)0xffffffffUL)
#define portTICK_PERIOD_MS ((TickType_t)(1000 / configTICK_RATE_HZ))
#define portTICK_RATE_MS portTICK_PERIOD_MS
#define pdMS_TO_TICKS(xTimeInMs) ((TickType_t)(((TickType_t)(xTimeInMs) * (TickType_t)configTICK_RATE_HZ) / (TickType_t)1000U))
#define pdTICKS_TO_MS(xTicks) ((TickType_t)(((uint64_t)(xTicks) * 1000U) / configTICK_RATE_HZ))

#if defined(ESP32)
#define portNUM_PROCESSORS 2
#else
#define portNUM_PROCESSORS 1
#endif
#define configNUMBER_OF_CORES portNUM_PROCESSORS
#define CONFIG_FREERTOS_NUMBER_OF_CORES portNUM_PROCESSORS
#define CONFIG_ARDUINO_RUNNING_CORE 1
#define ARDUINO_RUNNING_CORE 1
#define CONFIG_ARDUINO_EVENT_RUNNING_CORE 1
#define tskIDLE_PRIORITY ((UBaseType_t)0U)
#define tskNO_AFFINITY ((BaseType_t)0x7FFFFFFF)
#define PRO_CPU_NUM 0
#define APP_CPU_NUM 1

#ifndef IRAM_ATTR
#define IRAM_ATTR
#endif
#ifndef DRAM_ATTR
#define DRAM_ATTR
#endif

/* ---- a secao critica: a interrupcao espera ate ela acabar ---- */
typedef struct {
    volatile uint32_t owner;
    volatile uint32_t count;
} portMUX_TYPE;
typedef portMUX_TYPE spinlock_t;
#define portMUX_INITIALIZER_UNLOCKED {0, 0}
#define portMUX_FREE_VAL 0
#define SPINLOCK_INITIALIZER {0, 0}
#define spinlock_initialize(lock) ((void)(lock))
#define vPortCPUInitializeMutex(m) ((void)(m))

void cs_rtos_entrar_critico(void);
void cs_rtos_sair_critico(void);
void cs_rtos_ceder_da_isr(BaseType_t trocar);
BaseType_t cs_rtos_na_isr(void);
BaseType_t cs_rtos_nucleo(void);

#define portENTER_CRITICAL(...) cs_rtos_entrar_critico()
#define portEXIT_CRITICAL(...) cs_rtos_sair_critico()
#define portENTER_CRITICAL_ISR(...) cs_rtos_entrar_critico()
#define portEXIT_CRITICAL_ISR(...) cs_rtos_sair_critico()
#define portENTER_CRITICAL_SAFE(...) cs_rtos_entrar_critico()
#define portEXIT_CRITICAL_SAFE(...) cs_rtos_sair_critico()
#define taskENTER_CRITICAL(...) cs_rtos_entrar_critico()
#define taskEXIT_CRITICAL(...) cs_rtos_sair_critico()
#define taskENTER_CRITICAL_ISR(...) cs_rtos_entrar_critico()
#define taskEXIT_CRITICAL_ISR(...) cs_rtos_sair_critico()
#define taskENTER_CRITICAL_FROM_ISR() (cs_rtos_entrar_critico(), (UBaseType_t)0)
#define taskEXIT_CRITICAL_FROM_ISR(x) ((void)(x), cs_rtos_sair_critico())
#define taskDISABLE_INTERRUPTS() cs_rtos_entrar_critico()
#define taskENABLE_INTERRUPTS() cs_rtos_sair_critico()
#define portDISABLE_INTERRUPTS() cs_rtos_entrar_critico()
#define portENABLE_INTERRUPTS() cs_rtos_sair_critico()
#define portSET_INTERRUPT_MASK_FROM_ISR() (cs_rtos_entrar_critico(), (UBaseType_t)0)
#define portCLEAR_INTERRUPT_MASK_FROM_ISR(x) ((void)(x), cs_rtos_sair_critico())

#define portYIELD() taskYIELD()
#define portYIELD_FROM_ISR(...) cs_rtos_ceder_da_isr(0 __VA_OPT__(+ (__VA_ARGS__)))
#define portEND_SWITCHING_ISR(x) cs_rtos_ceder_da_isr(x)
#define portYIELD_WITHIN_API() taskYIELD()
#define xPortGetCoreID() cs_rtos_nucleo()
#define xPortInIsrContext() cs_rtos_na_isr()
#define xPortCanYield() (!cs_rtos_na_isr())
#define configASSERT(x) ((void)0)
#define portNOP() ((void)0)

size_t xPortGetFreeHeapSize(void);
size_t xPortGetMinimumEverFreeHeapSize(void);
void *pvPortMalloc(size_t xSize);
void vPortFree(void *pv);

/* ---- as tarefas (task.h) ---- */
struct tskTaskControlBlock;
typedef struct tskTaskControlBlock *TaskHandle_t;
typedef TaskHandle_t xTaskHandle;
typedef struct {
    uint8_t reservado[64];
} StaticTask_t;

typedef enum { eRunning = 0, eReady, eBlocked, eSuspended, eDeleted, eInvalid } eTaskState;
typedef enum { eNoAction = 0, eSetBits, eIncrement, eSetValueWithOverwrite, eSetValueWithoutOverwrite } eNotifyAction;

#define taskSCHEDULER_SUSPENDED ((BaseType_t)0)
#define taskSCHEDULER_NOT_STARTED ((BaseType_t)1)
#define taskSCHEDULER_RUNNING ((BaseType_t)2)

BaseType_t xTaskCreatePinnedToCore(TaskFunction_t pxTaskCode, const char *pcName, uint32_t usStackDepth, void *pvParameters,
                                   UBaseType_t uxPriority, TaskHandle_t *pxCreatedTask, BaseType_t xCoreID);
BaseType_t xTaskCreate(TaskFunction_t pxTaskCode, const char *pcName, uint32_t usStackDepth, void *pvParameters,
                       UBaseType_t uxPriority, TaskHandle_t *pxCreatedTask);
TaskHandle_t xTaskCreateStaticPinnedToCore(TaskFunction_t pxTaskCode, const char *pcName, uint32_t ulStackDepth,
                                           void *pvParameters, UBaseType_t uxPriority, StackType_t *puxStackBuffer,
                                           StaticTask_t *pxTaskBuffer, BaseType_t xCoreID);
TaskHandle_t xTaskCreateStatic(TaskFunction_t pxTaskCode, const char *pcName, uint32_t ulStackDepth, void *pvParameters,
                               UBaseType_t uxPriority, StackType_t *puxStackBuffer, StaticTask_t *pxTaskBuffer);
BaseType_t xTaskCreateAffinitySet(TaskFunction_t pxTaskCode, const char *pcName, uint32_t usStackDepth, void *pvParameters,
                                  UBaseType_t uxPriority, UBaseType_t uxCoreAffinityMask, TaskHandle_t *pxCreatedTask);
void vTaskDelete(TaskHandle_t xTaskToDelete);
void vTaskDelay(const TickType_t xTicksToDelay);
BaseType_t xTaskDelayUntil(TickType_t *const pxPreviousWakeTime, const TickType_t xTimeIncrement);
#define vTaskDelayUntil(pxPreviousWakeTime, xTimeIncrement) ((void)xTaskDelayUntil((pxPreviousWakeTime), (xTimeIncrement)))
BaseType_t xTaskAbortDelay(TaskHandle_t xTask);
TickType_t xTaskGetTickCount(void);
TickType_t xTaskGetTickCountFromISR(void);
void vTaskSuspend(TaskHandle_t xTaskToSuspend);
void vTaskResume(TaskHandle_t xTaskToResume);
BaseType_t xTaskResumeFromISR(TaskHandle_t xTaskToResume);
UBaseType_t uxTaskPriorityGet(TaskHandle_t xTask);
UBaseType_t uxTaskPriorityGetFromISR(TaskHandle_t xTask);
void vTaskPrioritySet(TaskHandle_t xTask, UBaseType_t uxNewPriority);
TaskHandle_t xTaskGetCurrentTaskHandle(void);
TaskHandle_t xTaskGetCurrentTaskHandleForCore(BaseType_t xCoreID);
TaskHandle_t xTaskGetHandle(const char *pcNameToQuery);
TaskHandle_t xTaskGetIdleTaskHandle(void);
TaskHandle_t xTaskGetIdleTaskHandleForCore(BaseType_t xCoreID);
char *pcTaskGetName(TaskHandle_t xTaskToQuery);
#define pcTaskGetTaskName pcTaskGetName
UBaseType_t uxTaskGetStackHighWaterMark(TaskHandle_t xTask);
#define uxTaskGetStackHighWaterMark2 uxTaskGetStackHighWaterMark
eTaskState eTaskGetState(TaskHandle_t xTask);
UBaseType_t uxTaskGetNumberOfTasks(void);
BaseType_t xTaskGetCoreID(TaskHandle_t xTask);
BaseType_t xTaskGetAffinity(TaskHandle_t xTask);
void vTaskSuspendAll(void);
BaseType_t xTaskResumeAll(void);
void vTaskStartScheduler(void);
void vTaskEndScheduler(void);
BaseType_t xTaskGetSchedulerState(void);
void vTaskList(char *pcWriteBuffer);
void cs_rtos_ceder(void);
#define taskYIELD() cs_rtos_ceder()

BaseType_t xTaskGenericNotify(TaskHandle_t xTaskToNotify, UBaseType_t uxIndexToNotify, uint32_t ulValue,
                              eNotifyAction eAction, uint32_t *pulPreviousNotificationValue);
BaseType_t xTaskGenericNotifyFromISR(TaskHandle_t xTaskToNotify, UBaseType_t uxIndexToNotify, uint32_t ulValue,
                                     eNotifyAction eAction, uint32_t *pulPreviousNotificationValue,
                                     BaseType_t *pxHigherPriorityTaskWoken);
BaseType_t xTaskGenericNotifyWait(UBaseType_t uxIndexToWaitOn, uint32_t ulBitsToClearOnEntry, uint32_t ulBitsToClearOnExit,
                                  uint32_t *pulNotificationValue, TickType_t xTicksToWait);
uint32_t ulTaskGenericNotifyTake(UBaseType_t uxIndexToWaitOn, BaseType_t xClearCountOnExit, TickType_t xTicksToWait);
BaseType_t xTaskGenericNotifyStateClear(TaskHandle_t xTask, UBaseType_t uxIndexToClear);
uint32_t ulTaskGenericNotifyValueClear(TaskHandle_t xTask, UBaseType_t uxIndexToClear, uint32_t ulBitsToClear);
#define xTaskNotify(t, v, a) xTaskGenericNotify((t), 0, (v), (a), NULL)
#define xTaskNotifyIndexed(t, i, v, a) xTaskGenericNotify((t), (i), (v), (a), NULL)
#define xTaskNotifyAndQuery(t, v, a, p) xTaskGenericNotify((t), 0, (v), (a), (p))
#define xTaskNotifyFromISR(t, v, a, w) xTaskGenericNotifyFromISR((t), 0, (v), (a), NULL, (w))
#define xTaskNotifyAndQueryFromISR(t, v, a, p, w) xTaskGenericNotifyFromISR((t), 0, (v), (a), (p), (w))
#define xTaskNotifyGive(t) xTaskGenericNotify((t), 0, 0, eIncrement, NULL)
#define xTaskNotifyGiveIndexed(t, i) xTaskGenericNotify((t), (i), 0, eIncrement, NULL)
#define vTaskNotifyGiveFromISR(t, w) ((void)xTaskGenericNotifyFromISR((t), 0, 0, eIncrement, NULL, (w)))
#define xTaskNotifyWait(e, x, p, t) xTaskGenericNotifyWait(0, (e), (x), (p), (t))
#define xTaskNotifyWaitIndexed(i, e, x, p, t) xTaskGenericNotifyWait((i), (e), (x), (p), (t))
#define ulTaskNotifyTake(c, t) ulTaskGenericNotifyTake(0, (c), (t))
#define ulTaskNotifyTakeIndexed(i, c, t) ulTaskGenericNotifyTake((i), (c), (t))
#define xTaskNotifyStateClear(t) xTaskGenericNotifyStateClear((t), 0)
#define ulTaskNotifyValueClear(t, b) ulTaskGenericNotifyValueClear((t), 0, (b))

/* ---- as filas e os semaforos (queue.h, semphr.h) ---- */
struct QueueDefinition;
typedef struct QueueDefinition *QueueHandle_t;
typedef QueueHandle_t xQueueHandle;
typedef QueueHandle_t SemaphoreHandle_t;
typedef QueueHandle_t xSemaphoreHandle;
typedef QueueHandle_t QueueSetHandle_t;
typedef QueueHandle_t QueueSetMemberHandle_t;
typedef struct {
    uint8_t reservado[96];
} StaticQueue_t;
typedef StaticQueue_t StaticSemaphore_t;
/* A lista do FreeRTOS guardada por fora (o StaticRingbuffer_t do IDF a usa). */
typedef struct {
    void *reservado[5];
    UBaseType_t numero;
} StaticList_t;

#define queueSEND_TO_BACK ((BaseType_t)0)
#define queueSEND_TO_FRONT ((BaseType_t)1)
#define queueOVERWRITE ((BaseType_t)2)
#define semGIVE_BLOCK_TIME ((TickType_t)0U)

QueueHandle_t xQueueCreate(UBaseType_t uxQueueLength, UBaseType_t uxItemSize);
QueueHandle_t xQueueCreateStatic(UBaseType_t uxQueueLength, UBaseType_t uxItemSize, uint8_t *pucQueueStorageBuffer,
                                 StaticQueue_t *pxQueueBuffer);
BaseType_t xQueueGenericSend(QueueHandle_t xQueue, const void *pvItemToQueue, TickType_t xTicksToWait, BaseType_t xCopyPosition);
BaseType_t xQueueGenericSendFromISR(QueueHandle_t xQueue, const void *pvItemToQueue, BaseType_t *pxHigherPriorityTaskWoken,
                                    BaseType_t xCopyPosition);
BaseType_t xQueueReceive(QueueHandle_t xQueue, void *pvBuffer, TickType_t xTicksToWait);
BaseType_t xQueueReceiveFromISR(QueueHandle_t xQueue, void *pvBuffer, BaseType_t *pxHigherPriorityTaskWoken);
BaseType_t xQueuePeek(QueueHandle_t xQueue, void *pvBuffer, TickType_t xTicksToWait);
BaseType_t xQueuePeekFromISR(QueueHandle_t xQueue, void *pvBuffer);
UBaseType_t uxQueueMessagesWaiting(QueueHandle_t xQueue);
UBaseType_t uxQueueMessagesWaitingFromISR(QueueHandle_t xQueue);
UBaseType_t uxQueueSpacesAvailable(QueueHandle_t xQueue);
BaseType_t xQueueReset(QueueHandle_t xQueue);
void vQueueDelete(QueueHandle_t xQueue);
BaseType_t xQueueIsQueueEmptyFromISR(QueueHandle_t xQueue);
BaseType_t xQueueIsQueueFullFromISR(QueueHandle_t xQueue);
#define xQueueSend(q, i, t) xQueueGenericSend((q), (i), (t), queueSEND_TO_BACK)
#define xQueueSendToBack(q, i, t) xQueueGenericSend((q), (i), (t), queueSEND_TO_BACK)
#define xQueueSendToFront(q, i, t) xQueueGenericSend((q), (i), (t), queueSEND_TO_FRONT)
#define xQueueOverwrite(q, i) xQueueGenericSend((q), (i), 0, queueOVERWRITE)
#define xQueueSendFromISR(q, i, w) xQueueGenericSendFromISR((q), (i), (w), queueSEND_TO_BACK)
#define xQueueSendToBackFromISR(q, i, w) xQueueGenericSendFromISR((q), (i), (w), queueSEND_TO_BACK)
#define xQueueSendToFrontFromISR(q, i, w) xQueueGenericSendFromISR((q), (i), (w), queueSEND_TO_FRONT)
#define xQueueOverwriteFromISR(q, i, w) xQueueGenericSendFromISR((q), (i), (w), queueOVERWRITE)
#define vQueueAddToRegistry(q, n) ((void)(q), (void)(n))
#define vQueueUnregisterQueue(q) ((void)(q))

QueueHandle_t cs_rtos_semaforo(UBaseType_t maximo, UBaseType_t inicial, int tipo);
BaseType_t cs_rtos_pegar(QueueHandle_t s, TickType_t espera, int recursivo);
BaseType_t cs_rtos_soltar(QueueHandle_t s, int recursivo);
BaseType_t cs_rtos_soltar_da_isr(QueueHandle_t s, BaseType_t *acordou);
BaseType_t cs_rtos_pegar_da_isr(QueueHandle_t s, BaseType_t *acordou);
TaskHandle_t xSemaphoreGetMutexHolder(SemaphoreHandle_t xSemaphore);
#define xSemaphoreCreateBinary() cs_rtos_semaforo(1, 0, 0)
#define xSemaphoreCreateBinaryStatic(b) ((void)(b), cs_rtos_semaforo(1, 0, 0))
#define xSemaphoreCreateCounting(m, i) cs_rtos_semaforo((m), (i), 0)
#define xSemaphoreCreateCountingStatic(m, i, b) ((void)(b), cs_rtos_semaforo((m), (i), 0))
#define xSemaphoreCreateMutex() cs_rtos_semaforo(1, 1, 1)
#define xSemaphoreCreateMutexStatic(b) ((void)(b), cs_rtos_semaforo(1, 1, 1))
#define xSemaphoreCreateRecursiveMutex() cs_rtos_semaforo(1, 1, 2)
#define xSemaphoreCreateRecursiveMutexStatic(b) ((void)(b), cs_rtos_semaforo(1, 1, 2))
#define vSemaphoreCreateBinary(s) ((s) = cs_rtos_semaforo(1, 1, 0))
#define xSemaphoreTake(s, t) cs_rtos_pegar((s), (t), 0)
#define xSemaphoreGive(s) cs_rtos_soltar((s), 0)
#define xSemaphoreTakeRecursive(s, t) cs_rtos_pegar((s), (t), 1)
#define xSemaphoreGiveRecursive(s) cs_rtos_soltar((s), 1)
#define xSemaphoreGiveFromISR(s, w) cs_rtos_soltar_da_isr((s), (w))
#define xSemaphoreTakeFromISR(s, w) cs_rtos_pegar_da_isr((s), (w))
#define uxSemaphoreGetCount(s) uxQueueMessagesWaiting(s)
#define vSemaphoreDelete(s) vQueueDelete(s)

/* ---- os temporizadores de software (timers.h) ---- */
struct tmrTimerControl;
typedef struct tmrTimerControl *TimerHandle_t;
typedef TimerHandle_t xTimerHandle;
typedef void (*TimerCallbackFunction_t)(TimerHandle_t xTimer);
typedef void (*PendedFunction_t)(void *, uint32_t);
typedef struct {
    uint8_t reservado[64];
} StaticTimer_t;

TimerHandle_t xTimerCreate(const char *pcTimerName, const TickType_t xTimerPeriodInTicks, const UBaseType_t uxAutoReload,
                           void *const pvTimerID, TimerCallbackFunction_t pxCallbackFunction);
TimerHandle_t xTimerCreateStatic(const char *pcTimerName, const TickType_t xTimerPeriodInTicks, const UBaseType_t uxAutoReload,
                                 void *const pvTimerID, TimerCallbackFunction_t pxCallbackFunction, StaticTimer_t *pxTimerBuffer);
BaseType_t cs_rtos_temporizador(TimerHandle_t t, int comando, TickType_t valor);
#define xTimerStart(t, w) cs_rtos_temporizador((t), 1, 0)
#define xTimerStop(t, w) cs_rtos_temporizador((t), 2, 0)
#define xTimerReset(t, w) cs_rtos_temporizador((t), 1, 0)
#define xTimerChangePeriod(t, p, w) cs_rtos_temporizador((t), 3, (p))
#define xTimerDelete(t, w) cs_rtos_temporizador((t), 4, 0)
#define xTimerStartFromISR(t, w) cs_rtos_temporizador((t), 1, 0)
#define xTimerStopFromISR(t, w) cs_rtos_temporizador((t), 2, 0)
#define xTimerResetFromISR(t, w) cs_rtos_temporizador((t), 1, 0)
#define xTimerChangePeriodFromISR(t, p, w) cs_rtos_temporizador((t), 3, (p))
void *pvTimerGetTimerID(const TimerHandle_t xTimer);
void vTimerSetTimerID(TimerHandle_t xTimer, void *pvNewID);
BaseType_t xTimerIsTimerActive(TimerHandle_t xTimer);
const char *pcTimerGetName(TimerHandle_t xTimer);
TickType_t xTimerGetPeriod(TimerHandle_t xTimer);
TickType_t xTimerGetExpiryTime(TimerHandle_t xTimer);
void vTimerSetReloadMode(TimerHandle_t xTimer, const UBaseType_t uxAutoReload);
UBaseType_t uxTimerGetReloadMode(TimerHandle_t xTimer);
BaseType_t xTimerPendFunctionCall(PendedFunction_t xFunctionToPend, void *pvParameter1, uint32_t ulParameter2, TickType_t xTicksToWait);

/* ---- os grupos de eventos (event_groups.h) ---- */
struct EventGroupDef_t;
typedef struct EventGroupDef_t *EventGroupHandle_t;
typedef uint32_t EventBits_t;
typedef struct {
    uint8_t reservado[64];
} StaticEventGroup_t;

EventGroupHandle_t xEventGroupCreate(void);
EventGroupHandle_t xEventGroupCreateStatic(StaticEventGroup_t *pxEventGroupBuffer);
EventBits_t xEventGroupWaitBits(EventGroupHandle_t xEventGroup, const EventBits_t uxBitsToWaitFor, const BaseType_t xClearOnExit,
                                const BaseType_t xWaitForAllBits, TickType_t xTicksToWait);
EventBits_t xEventGroupSetBits(EventGroupHandle_t xEventGroup, const EventBits_t uxBitsToSet);
BaseType_t xEventGroupSetBitsFromISR(EventGroupHandle_t xEventGroup, const EventBits_t uxBitsToSet,
                                     BaseType_t *pxHigherPriorityTaskWoken);
EventBits_t xEventGroupClearBits(EventGroupHandle_t xEventGroup, const EventBits_t uxBitsToClear);
#define xEventGroupClearBitsFromISR(g, b) xEventGroupClearBits((g), (b))
EventBits_t xEventGroupGetBits(EventGroupHandle_t xEventGroup);
#define xEventGroupGetBitsFromISR(g) xEventGroupGetBits(g)
EventBits_t xEventGroupSync(EventGroupHandle_t xEventGroup, const EventBits_t uxBitsToSet, const EventBits_t uxBitsToWaitFor,
                            TickType_t xTicksToWait);
void vEventGroupDelete(EventGroupHandle_t xEventGroup);

#ifdef __cplusplus
}
#endif

#endif
