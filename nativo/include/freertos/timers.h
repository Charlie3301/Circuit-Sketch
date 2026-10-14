#ifndef INC_TIMERS_H
#define INC_TIMERS_H

#include "FreeRTOS.h"
#include "task.h"

typedef struct cs_temporizador *TimerHandle_t;
typedef void (*TimerCallbackFunction_t)(TimerHandle_t t);

TimerHandle_t xTimerCreate(const char *nome, TickType_t periodo, UBaseType_t repetir,
                           void *id, TimerCallbackFunction_t funcao);
BaseType_t xTimerStart(TimerHandle_t t, TickType_t espera);
BaseType_t xTimerStop(TimerHandle_t t, TickType_t espera);
BaseType_t xTimerReset(TimerHandle_t t, TickType_t espera);
BaseType_t xTimerChangePeriod(TimerHandle_t t, TickType_t periodo, TickType_t espera);
BaseType_t xTimerDelete(TimerHandle_t t, TickType_t espera);
BaseType_t xTimerIsTimerActive(TimerHandle_t t);
void *pvTimerGetTimerID(TimerHandle_t t);

#endif
