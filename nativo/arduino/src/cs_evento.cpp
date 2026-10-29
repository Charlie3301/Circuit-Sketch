/* cs_evento.cpp -- o laco de eventos do ESP-IDF (esp_event.h, o cabecalho de
 * verdade): quem quer saber de um evento registra a funcao (por base e por
 * numero, ou "qualquer"), e quem posta pode estar em outra tarefa.
 *
 * Como no IDF: o laco com tarefa (o padrao, o "sys_evt") tem uma fila e
 * chama as funcoes na tarefa dele, na ordem em que os eventos chegaram; o
 * laco sem tarefa so roda quando o programa chama esp_event_loop_run. Os
 * dados do evento sao copiados no post (quem postou pode reaproveitar o
 * buffer). */
#include "Arduino.h"
#if defined(ESP32)
#include "esp_event.h"
#include <vector>
#include <string.h>

namespace {

struct Registro {
    esp_event_base_t base;
    int32_t id;
    esp_event_handler_t fn;
    void *arg;
    bool vivo = true;
};

struct Evento {
    esp_event_base_t base;
    int32_t id;
    void *dados;
};

struct Laco {
    std::vector<Registro *> registros;
    QueueHandle_t fila = nullptr;
    TaskHandle_t tarefa = nullptr;
    SemaphoreHandle_t trava = nullptr;
};

Laco *padrao;

/* A mesma base: o ponteiro (o do ESP_EVENT_DEFINE_BASE) ou o texto igual. */
bool mesmaBase(esp_event_base_t a, esp_event_base_t b) { return a == b || (a && b && strcmp(a, b) == 0); }

void despachar(Laco *l, const Evento &e)
{
    std::vector<Registro *> copia;
    xSemaphoreTake(l->trava, portMAX_DELAY);
    copia = l->registros;
    xSemaphoreGive(l->trava);
    /* Os especificos primeiro, depois os de qualquer numero, depois os de qualquer base (a ordem do IDF). */
    for (int passo = 0; passo < 3; passo++)
        for (Registro *r : copia) {
            if (!r->vivo) continue;
            bool qualquerBase = r->base == ESP_EVENT_ANY_BASE, qualquerId = r->id == ESP_EVENT_ANY_ID;
            int nivel = qualquerBase ? 2 : qualquerId ? 1 : 0;
            if (nivel != passo) continue;
            if (!qualquerBase && !mesmaBase(r->base, e.base)) continue;
            if (!qualquerId && r->id != e.id) continue;
            r->fn(r->arg, e.base, e.id, e.dados);
        }
    free(e.dados);
}

void tarefaDoLaco(void *arg)
{
    Laco *l = (Laco *)arg;
    Evento e;
    for (;;)
        if (xQueueReceive(l->fila, &e, portMAX_DELAY) == pdTRUE) despachar(l, e);
}

Laco *criar(const esp_event_loop_args_t *args)
{
    auto *l = new Laco();
    l->trava = xSemaphoreCreateMutex();
    l->fila = xQueueCreate(args && args->queue_size > 0 ? args->queue_size : 32, sizeof(Evento));
    if (!args || args->task_name)
        xTaskCreate(tarefaDoLaco, args && args->task_name ? args->task_name : "sys_evt",
                    args && args->task_stack_size ? args->task_stack_size : 4096, l, args ? args->task_priority : 20, &l->tarefa);
    return l;
}

esp_err_t registrar(Laco *l, esp_event_base_t base, int32_t id, esp_event_handler_t fn, void *arg, esp_event_handler_instance_t *instancia)
{
    if (!l) return ESP_ERR_INVALID_STATE;
    if (!fn || (base == ESP_EVENT_ANY_BASE && id != ESP_EVENT_ANY_ID)) return ESP_ERR_INVALID_ARG;
    auto *r = new Registro{base, id, fn, arg};
    xSemaphoreTake(l->trava, portMAX_DELAY);
    l->registros.push_back(r);
    xSemaphoreGive(l->trava);
    if (instancia) *instancia = r;
    return ESP_OK;
}

esp_err_t tirar(Laco *l, esp_event_base_t base, int32_t id, esp_event_handler_t fn, Registro *so)
{
    if (!l) return ESP_ERR_INVALID_STATE;
    xSemaphoreTake(l->trava, portMAX_DELAY);
    for (size_t i = 0; i < l->registros.size(); i++) {
        Registro *r = l->registros[i];
        bool este = so ? r == so : (r->fn == fn && r->id == id && (r->base == base || mesmaBase(r->base, base)));
        if (!este) continue;
        r->vivo = false;
        l->registros.erase(l->registros.begin() + i);
        break;
    }
    xSemaphoreGive(l->trava);
    return ESP_OK;
}

esp_err_t postar(Laco *l, esp_event_base_t base, int32_t id, const void *dados, size_t n, TickType_t espera)
{
    if (!l) return ESP_ERR_INVALID_STATE;
    Evento e{base, id, nullptr};
    if (dados && n) {
        e.dados = malloc(n);
        memcpy(e.dados, dados, n);
    }
    if (!l->tarefa) {
        /* O laco sem tarefa: o evento espera o esp_event_loop_run. */
        if (xQueueSend(l->fila, &e, espera) != pdTRUE) {
            free(e.dados);
            return ESP_ERR_TIMEOUT;
        }
        return ESP_OK;
    }
    if (xQueueSend(l->fila, &e, espera) != pdTRUE) {
        free(e.dados);
        return ESP_ERR_TIMEOUT;
    }
    return ESP_OK;
}

}  // namespace

extern "C" esp_err_t esp_event_loop_create(const esp_event_loop_args_t *event_loop_args, esp_event_loop_handle_t *event_loop)
{
    if (!event_loop_args || !event_loop) return ESP_ERR_INVALID_ARG;
    *event_loop = criar(event_loop_args);
    return ESP_OK;
}

extern "C" esp_err_t esp_event_loop_delete(esp_event_loop_handle_t event_loop)
{
    Laco *l = (Laco *)event_loop;
    if (!l) return ESP_ERR_INVALID_ARG;
    if (l->tarefa) vTaskDelete(l->tarefa);
    vQueueDelete(l->fila);
    vSemaphoreDelete(l->trava);
    for (Registro *r : l->registros) delete r;
    delete l;
    return ESP_OK;
}

extern "C" esp_err_t esp_event_loop_create_default(void)
{
    if (padrao) return ESP_ERR_INVALID_STATE;
    esp_event_loop_args_t a = {};
    a.queue_size = 32;
    a.task_name = "sys_evt";
    a.task_priority = 20;
    a.task_stack_size = 4096;
    padrao = criar(&a);
    return ESP_OK;
}

extern "C" esp_err_t esp_event_loop_delete_default(void)
{
    if (!padrao) return ESP_ERR_INVALID_STATE;
    esp_event_loop_delete(padrao);
    padrao = nullptr;
    return ESP_OK;
}

extern "C" esp_err_t esp_event_loop_run(esp_event_loop_handle_t event_loop, TickType_t ticks_to_run)
{
    Laco *l = (Laco *)event_loop;
    if (!l) return ESP_ERR_INVALID_ARG;
    TickType_t inicio = xTaskGetTickCount();
    Evento e;
    do {
        TickType_t passou = xTaskGetTickCount() - inicio;
        TickType_t resta = ticks_to_run == portMAX_DELAY ? portMAX_DELAY : (passou >= ticks_to_run ? 0 : ticks_to_run - passou);
        if (xQueueReceive(l->fila, &e, resta) != pdTRUE) break;
        despachar(l, e);
    } while (ticks_to_run == portMAX_DELAY || xTaskGetTickCount() - inicio < ticks_to_run);
    return ESP_OK;
}

extern "C" esp_err_t esp_event_handler_register(esp_event_base_t event_base, int32_t event_id, esp_event_handler_t event_handler,
                                                void *event_handler_arg)
{
    return registrar(padrao, event_base, event_id, event_handler, event_handler_arg, nullptr);
}
extern "C" esp_err_t esp_event_handler_register_with(esp_event_loop_handle_t event_loop, esp_event_base_t event_base, int32_t event_id,
                                                     esp_event_handler_t event_handler, void *event_handler_arg)
{
    return registrar((Laco *)event_loop, event_base, event_id, event_handler, event_handler_arg, nullptr);
}
extern "C" esp_err_t esp_event_handler_instance_register(esp_event_base_t event_base, int32_t event_id, esp_event_handler_t event_handler,
                                                         void *event_handler_arg, esp_event_handler_instance_t *instance)
{
    return registrar(padrao, event_base, event_id, event_handler, event_handler_arg, instance);
}
extern "C" esp_err_t esp_event_handler_instance_register_with(esp_event_loop_handle_t event_loop, esp_event_base_t event_base,
                                                              int32_t event_id, esp_event_handler_t event_handler,
                                                              void *event_handler_arg, esp_event_handler_instance_t *instance)
{
    return registrar((Laco *)event_loop, event_base, event_id, event_handler, event_handler_arg, instance);
}
extern "C" esp_err_t esp_event_handler_unregister(esp_event_base_t event_base, int32_t event_id, esp_event_handler_t event_handler)
{
    return tirar(padrao, event_base, event_id, event_handler, nullptr);
}
extern "C" esp_err_t esp_event_handler_unregister_with(esp_event_loop_handle_t event_loop, esp_event_base_t event_base, int32_t event_id,
                                                       esp_event_handler_t event_handler)
{
    return tirar((Laco *)event_loop, event_base, event_id, event_handler, nullptr);
}
extern "C" esp_err_t esp_event_handler_instance_unregister(esp_event_base_t event_base, int32_t event_id,
                                                           esp_event_handler_instance_t instance)
{
    return tirar(padrao, event_base, event_id, nullptr, (Registro *)instance);
}
extern "C" esp_err_t esp_event_handler_instance_unregister_with(esp_event_loop_handle_t event_loop, esp_event_base_t event_base,
                                                                int32_t event_id, esp_event_handler_instance_t instance)
{
    return tirar((Laco *)event_loop, event_base, event_id, nullptr, (Registro *)instance);
}

extern "C" esp_err_t esp_event_post(esp_event_base_t event_base, int32_t event_id, const void *event_data, size_t event_data_size,
                                    TickType_t ticks_to_wait)
{
    return postar(padrao, event_base, event_id, event_data, event_data_size, ticks_to_wait);
}
extern "C" esp_err_t esp_event_post_to(esp_event_loop_handle_t event_loop, esp_event_base_t event_base, int32_t event_id,
                                       const void *event_data, size_t event_data_size, TickType_t ticks_to_wait)
{
    return postar((Laco *)event_loop, event_base, event_id, event_data, event_data_size, ticks_to_wait);
}
extern "C" esp_err_t esp_event_isr_post(esp_event_base_t event_base, int32_t event_id, const void *event_data, size_t event_data_size,
                                        BaseType_t *task_unblocked)
{
    if (task_unblocked) *task_unblocked = pdFALSE;
    return postar(padrao, event_base, event_id, event_data, event_data_size, 0);
}
extern "C" esp_err_t esp_event_isr_post_to(esp_event_loop_handle_t event_loop, esp_event_base_t event_base, int32_t event_id,
                                           const void *event_data, size_t event_data_size, BaseType_t *task_unblocked)
{
    if (task_unblocked) *task_unblocked = pdFALSE;
    return postar((Laco *)event_loop, event_base, event_id, event_data, event_data_size, 0);
}

extern "C" esp_err_t esp_event_dump(FILE *file)
{
    if (!padrao) return ESP_OK;
    fprintf(file ? file : stdout, "Events posted: (loop padrao) %u handlers\n", (unsigned)padrao->registros.size());
    return ESP_OK;
}

#endif
