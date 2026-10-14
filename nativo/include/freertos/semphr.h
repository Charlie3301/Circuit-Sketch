#ifndef INC_SEMPHR_H
#define INC_SEMPHR_H

#include "FreeRTOS.h"
#include "queue.h"

typedef struct cs_semaforo *SemaphoreHandle_t;
typedef SemaphoreHandle_t xSemaphoreHandle;

SemaphoreHandle_t xSemaphoreCreateBinary(void);
SemaphoreHandle_t xSemaphoreCreateMutex(void);
SemaphoreHandle_t xSemaphoreCreateRecursiveMutex(void);
SemaphoreHandle_t xSemaphoreCreateCounting(UBaseType_t maximo, UBaseType_t inicial);
void vSemaphoreDelete(SemaphoreHandle_t s);
BaseType_t xSemaphoreTake(SemaphoreHandle_t s, TickType_t espera);
BaseType_t xSemaphoreGive(SemaphoreHandle_t s);
BaseType_t xSemaphoreTakeFromISR(SemaphoreHandle_t s, BaseType_t *acordou);
BaseType_t xSemaphoreGiveFromISR(SemaphoreHandle_t s, BaseType_t *acordou);
BaseType_t xSemaphoreTakeRecursive(SemaphoreHandle_t s, TickType_t espera);
BaseType_t xSemaphoreGiveRecursive(SemaphoreHandle_t s);
UBaseType_t uxSemaphoreGetCount(SemaphoreHandle_t s);
TaskHandle_t xSemaphoreGetMutexHolder(SemaphoreHandle_t s);
#define vSemaphoreCreateBinary(s) do { (s) = xSemaphoreCreateBinary(); if (s) xSemaphoreGive(s); } while (0)

#endif
