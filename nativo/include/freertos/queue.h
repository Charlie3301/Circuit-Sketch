#ifndef INC_QUEUE_H
#define INC_QUEUE_H

#include "FreeRTOS.h"

typedef struct cs_fila *QueueHandle_t;
typedef QueueHandle_t xQueueHandle;
typedef QueueHandle_t QueueSetHandle_t;

QueueHandle_t xQueueCreate(UBaseType_t tamanho, UBaseType_t item);
void vQueueDelete(QueueHandle_t f);
BaseType_t xQueueSend(QueueHandle_t f, const void *item, TickType_t espera);
BaseType_t xQueueSendToBack(QueueHandle_t f, const void *item, TickType_t espera);
BaseType_t xQueueSendToFront(QueueHandle_t f, const void *item, TickType_t espera);
BaseType_t xQueueOverwrite(QueueHandle_t f, const void *item);
BaseType_t xQueueSendFromISR(QueueHandle_t f, const void *item, BaseType_t *acordou);
BaseType_t xQueueSendToBackFromISR(QueueHandle_t f, const void *item, BaseType_t *acordou);
BaseType_t xQueueSendToFrontFromISR(QueueHandle_t f, const void *item, BaseType_t *acordou);
BaseType_t xQueueOverwriteFromISR(QueueHandle_t f, const void *item, BaseType_t *acordou);
BaseType_t xQueueReceive(QueueHandle_t f, void *item, TickType_t espera);
BaseType_t xQueueReceiveFromISR(QueueHandle_t f, void *item, BaseType_t *acordou);
BaseType_t xQueuePeek(QueueHandle_t f, void *item, TickType_t espera);
UBaseType_t uxQueueMessagesWaiting(QueueHandle_t f);
UBaseType_t uxQueueMessagesWaitingFromISR(QueueHandle_t f);
UBaseType_t uxQueueSpacesAvailable(QueueHandle_t f);
BaseType_t xQueueReset(QueueHandle_t f);

#endif
