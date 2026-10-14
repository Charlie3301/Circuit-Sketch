#ifndef INC_EVENT_GROUPS_H
#define INC_EVENT_GROUPS_H

#include "FreeRTOS.h"

typedef struct cs_grupo *EventGroupHandle_t;
typedef uint32_t EventBits_t;

EventGroupHandle_t xEventGroupCreate(void);
void vEventGroupDelete(EventGroupHandle_t g);
EventBits_t xEventGroupSetBits(EventGroupHandle_t g, EventBits_t bits);
EventBits_t xEventGroupClearBits(EventGroupHandle_t g, EventBits_t bits);
EventBits_t xEventGroupGetBits(EventGroupHandle_t g);
EventBits_t xEventGroupWaitBits(EventGroupHandle_t g, EventBits_t bits, BaseType_t limpar,
                                BaseType_t todos, TickType_t espera);
BaseType_t xEventGroupSetBitsFromISR(EventGroupHandle_t g, EventBits_t bits, BaseType_t *acordou);

#endif
