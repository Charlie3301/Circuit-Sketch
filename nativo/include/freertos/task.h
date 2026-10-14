#ifndef INC_TASK_H
#define INC_TASK_H

#include "FreeRTOS.h"

typedef enum { eNoAction, eSetBits, eIncrement, eSetValueWithOverwrite, eSetValueWithoutOverwrite } eNotifyAction;
typedef enum { eRunning, eReady, eBlocked, eSuspended, eDeleted, eInvalid } eTaskState;

#define tskIDLE_PRIORITY ((UBaseType_t)0)
#define tskNO_AFFINITY ((BaseType_t)0x7FFFFFFF)

BaseType_t xTaskCreatePinnedToCore(TaskFunction_t funcao, const char *nome, uint32_t pilha,
                                   void *argumento, UBaseType_t prioridade,
                                   TaskHandle_t *criada, BaseType_t nucleo);
BaseType_t xTaskCreate(TaskFunction_t funcao, const char *nome, uint32_t pilha,
                       void *argumento, UBaseType_t prioridade, TaskHandle_t *criada);
void vTaskDelete(TaskHandle_t t);
void vTaskDelay(TickType_t ticks);
void vTaskDelayUntil(TickType_t *anterior, TickType_t periodo);
BaseType_t xTaskDelayUntil(TickType_t *anterior, TickType_t periodo);
TickType_t xTaskGetTickCount(void);
TickType_t xTaskGetTickCountFromISR(void);
TaskHandle_t xTaskGetCurrentTaskHandle(void);
TaskHandle_t xTaskGetCurrentTaskHandleForCore(BaseType_t nucleo);
TaskHandle_t xTaskGetIdleTaskHandleForCore(BaseType_t nucleo);
char *pcTaskGetName(TaskHandle_t t);
#define pcTaskGetTaskName pcTaskGetName
UBaseType_t uxTaskGetStackHighWaterMark(TaskHandle_t t);
UBaseType_t uxTaskPriorityGet(TaskHandle_t t);
void vTaskPrioritySet(TaskHandle_t t, UBaseType_t prioridade);
void vTaskSuspend(TaskHandle_t t);
void vTaskResume(TaskHandle_t t);
BaseType_t xTaskResumeFromISR(TaskHandle_t t);
void vTaskSuspendAll(void);
BaseType_t xTaskResumeAll(void);
void taskYIELD(void);
#define portYIELD() taskYIELD()
void vTaskStartScheduler(void);
eTaskState eTaskGetState(TaskHandle_t t);
UBaseType_t uxTaskGetNumberOfTasks(void);
BaseType_t xTaskGetAffinity(TaskHandle_t t);

BaseType_t xTaskNotifyGive(TaskHandle_t t);
void vTaskNotifyGiveFromISR(TaskHandle_t t, BaseType_t *acordou);
uint32_t ulTaskNotifyTake(BaseType_t zerar, TickType_t espera);
BaseType_t xTaskNotify(TaskHandle_t t, uint32_t valor, eNotifyAction acao);
BaseType_t xTaskNotifyFromISR(TaskHandle_t t, uint32_t valor, eNotifyAction acao, BaseType_t *acordou);
BaseType_t xTaskNotifyWait(uint32_t limpar_entrada, uint32_t limpar_saida, uint32_t *valor, TickType_t espera);

#endif
