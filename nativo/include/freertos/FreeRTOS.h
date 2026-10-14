/* FreeRTOS do CircuitSketch.
 *
 * Cada tarefa e uma thread do PC; o tick e de 1 ms, como na ESP32 com
 * CONFIG_FREERTOS_HZ=1000. Semaforos e filas sao do simulador, com os
 * mesmos timeouts -- e quando a placa para, quem esta esperando sai. */
#ifndef INC_FREERTOS_H
#define INC_FREERTOS_H

#include <stddef.h>
#include <stdint.h>
#include "sdkconfig.h"
#include <assert.h>

/* O que o portmacro.h do ESP-IDF traz junto -- e ha codigo que conta com
 * isso: quem inclui o FreeRTOS.h ja tem o esp_restart(), o
 * esp_get_free_heap_size(), o heap_caps_malloc(), o limits.h... */
#include <stdlib.h>
#include <stdbool.h>
#include <stdarg.h>
#include <limits.h>
#include "esp_attr.h"
#include "esp_cpu.h"
#include "esp_heap_caps.h"
#include "esp_rom_sys.h"
#include "esp_system.h"
#if CONFIG_FREERTOS_RUN_TIME_STATS_USING_ESP_TIMER
#include "esp_timer.h"
#endif

typedef int BaseType_t;
typedef unsigned int UBaseType_t;
typedef uint32_t TickType_t;
typedef uint8_t StackType_t;
typedef void (*TaskFunction_t)(void *);
typedef struct cs_tarefa *TaskHandle_t;

#define pdFALSE ((BaseType_t)0)
#define pdTRUE ((BaseType_t)1)
#define pdPASS pdTRUE
#define pdFAIL pdFALSE
#define errQUEUE_EMPTY ((BaseType_t)0)
#define errQUEUE_FULL ((BaseType_t)0)
#define errCOULD_NOT_ALLOCATE_REQUIRED_MEMORY (-1)

#define configTICK_RATE_HZ 1000
#define configMAX_PRIORITIES 25
#define configMINIMAL_STACK_SIZE 768
#define configMAX_TASK_NAME_LEN 16
#define configNUM_CORES 2
#define portMAX_DELAY ((TickType_t)0xffffffffUL)
#define portTICK_PERIOD_MS ((TickType_t)1000 / configTICK_RATE_HZ)
#define portTICK_RATE_MS portTICK_PERIOD_MS
#define pdMS_TO_TICKS(ms) ((TickType_t)(((uint64_t)(ms) * configTICK_RATE_HZ) / 1000U))
#define pdTICKS_TO_MS(t) ((uint32_t)(((uint64_t)(t) * 1000U) / configTICK_RATE_HZ))
#define portNUM_PROCESSORS 2
#define portSTACK_TYPE uint8_t
#define portBASE_TYPE int

/* A trava das secoes criticas: dona (a tarefa) e quantas vezes entrou. */
typedef struct { volatile int owner; volatile int count; } portMUX_TYPE;
typedef portMUX_TYPE spinlock_t;
#define portMUX_FREE_VAL 0
#define portMUX_INITIALIZER_UNLOCKED { 0, 0 }
#define SPINLOCK_INITIALIZER { 0, 0 }
#define portMUX_INITIALIZE(m) do { (m)->owner = 0; (m)->count = 0; } while (0)
#define spinlock_initialize(m) portMUX_INITIALIZE(m)
#define configASSERT(x) do { if (!(x)) __cs_assert(#x, __FILE__, __LINE__, __func__); } while (0)
void __cs_assert(const char *expressao, const char *arquivo, int linha, const char *funcao);
void vPortEnterCritical(portMUX_TYPE *m);
void vPortExitCritical(portMUX_TYPE *m);
#define portENTER_CRITICAL(m) vPortEnterCritical(m)
#define portEXIT_CRITICAL(m) vPortExitCritical(m)
#define portENTER_CRITICAL_ISR(m) vPortEnterCritical(m)
#define portEXIT_CRITICAL_ISR(m) vPortExitCritical(m)
#define portENTER_CRITICAL_SAFE(m) vPortEnterCritical(m)
#define portEXIT_CRITICAL_SAFE(m) vPortExitCritical(m)
#define taskENTER_CRITICAL(m) vPortEnterCritical(m)
#define taskEXIT_CRITICAL(m) vPortExitCritical(m)
#define taskENTER_CRITICAL_ISR(m) vPortEnterCritical(m)
#define taskEXIT_CRITICAL_ISR(m) vPortExitCritical(m)
#define portDISABLE_INTERRUPTS() ((void)0)
#define portENABLE_INTERRUPTS() ((void)0)
#define portYIELD_FROM_ISR(...) ((void)0)
#define portEND_SWITCHING_ISR(x) ((void)(x))
BaseType_t xPortGetCoreID(void);
BaseType_t xPortInIsrContext(void);

void *pvPortMalloc(size_t n);
void vPortFree(void *p);


/* Como o idf_additions.h do ESP-IDF 5: quem inclui o FreeRTOS.h ja tem
 * tarefas, filas, semaforos e grupos de eventos. */
#include "freertos/task.h"
#include "freertos/queue.h"
#include "freertos/semphr.h"
#include "freertos/event_groups.h"

#endif
