/* esp_event.h: o laco de eventos (WiFi conectou, ganhou IP...). Os
 * tratadores rodam na tarefa do laco, como no ESP-IDF. */
#ifndef CS_ESP_EVENT_H
#define CS_ESP_EVENT_H

#include <stdint.h>
#include <stdbool.h>
#include <stddef.h>
#include "esp_err.h"
#include "freertos/FreeRTOS.h"

typedef const char *esp_event_base_t;
typedef void *esp_event_loop_handle_t;
typedef void (*esp_event_handler_t)(void *arg, esp_event_base_t base, int32_t id, void *dados);
typedef void *esp_event_handler_instance_t;

#define ESP_EVENT_DECLARE_BASE(id) extern esp_event_base_t const id
#define ESP_EVENT_DEFINE_BASE(id) esp_event_base_t const id = #id
#define ESP_EVENT_ANY_BASE NULL
#define ESP_EVENT_ANY_ID -1

typedef struct {
    int32_t queue_size;
    const char *task_name;
    UBaseType_t task_priority;
    uint32_t task_stack_size;
    BaseType_t task_core_id;
} esp_event_loop_args_t;

esp_err_t esp_event_loop_create_default(void);
esp_err_t esp_event_loop_delete_default(void);
esp_err_t esp_event_loop_create(const esp_event_loop_args_t *args, esp_event_loop_handle_t *laco);
esp_err_t esp_event_loop_delete(esp_event_loop_handle_t laco);
esp_err_t esp_event_loop_run(esp_event_loop_handle_t laco, TickType_t espera);

esp_err_t esp_event_handler_register(esp_event_base_t base, int32_t id, esp_event_handler_t tratador, void *arg);
esp_err_t esp_event_handler_unregister(esp_event_base_t base, int32_t id, esp_event_handler_t tratador);
esp_err_t esp_event_handler_instance_register(esp_event_base_t base, int32_t id, esp_event_handler_t tratador,
                                              void *arg, esp_event_handler_instance_t *instancia);
esp_err_t esp_event_handler_instance_unregister(esp_event_base_t base, int32_t id,
                                                esp_event_handler_instance_t instancia);
esp_err_t esp_event_handler_register_with(esp_event_loop_handle_t laco, esp_event_base_t base, int32_t id,
                                          esp_event_handler_t tratador, void *arg);
esp_err_t esp_event_handler_instance_register_with(esp_event_loop_handle_t laco, esp_event_base_t base,
                                                   int32_t id, esp_event_handler_t tratador, void *arg,
                                                   esp_event_handler_instance_t *instancia);
esp_err_t esp_event_post(esp_event_base_t base, int32_t id, const void *dados, size_t n, TickType_t espera);
esp_err_t esp_event_post_to(esp_event_loop_handle_t laco, esp_event_base_t base, int32_t id,
                            const void *dados, size_t n, TickType_t espera);
esp_err_t esp_event_isr_post(esp_event_base_t base, int32_t id, const void *dados, size_t n,
                             BaseType_t *acordou);

#endif
