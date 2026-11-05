/* machine_wdt.c -- o cao de guarda (incluido por extmod/machine_wdt.c), no watchdog de tarefa do IDF. */
#include "py/runtime.h"

typedef struct _machine_wdt_obj_t {
    mp_obj_base_t base;
} machine_wdt_obj_t;

void cs_py_wdt_iniciar(uint32_t ms);
void cs_py_wdt_alimentar(void);

static const machine_wdt_obj_t cao = {{&machine_wdt_type}};

static machine_wdt_obj_t *mp_machine_wdt_make_new_instance(mp_int_t id, mp_int_t timeout_ms)
{
    if (id != 0) mp_raise_ValueError(NULL);
    if (timeout_ms <= 0) mp_raise_ValueError(MP_ERROR_TEXT("WDT timeout too short"));
    cs_py_wdt_iniciar((uint32_t)timeout_ms);
    return (machine_wdt_obj_t *)&cao;
}

static void mp_machine_wdt_feed(machine_wdt_obj_t *self)
{
    (void)self;
    cs_py_wdt_alimentar();
}
