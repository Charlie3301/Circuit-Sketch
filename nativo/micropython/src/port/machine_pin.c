/* machine_pin.c -- machine.Pin, com a mesma API do port da ESP32. */
#include "py/mphal.h"
#include "py/runtime.h"
#include "extmod/modmachine.h"
#include "extmod/virtpin.h"
#include "port/cs_py_ponte.h"

#define IRQ_SUBIDA CS_PY_SUBIDA
#define IRQ_DESCIDA CS_PY_DESCIDA

typedef struct _machine_pin_obj_t {
    mp_obj_base_t base;
    int id;
    mp_obj_t aoMudar;
    bool ultimoFoiSubida;
} machine_pin_obj_t;

/* Um objeto por pino, para o Pin(2) de dois lugares ser o mesmo. Os handlers ficam nas raizes do GC. */
#define MAXIMO_DE_PINOS 64
MP_REGISTER_ROOT_POINTER(void *cs_pinos[64]);

static machine_pin_obj_t *pinoDe(int id)
{
    if (!cs_py_pino_existe(id) || id >= MAXIMO_DE_PINOS) mp_raise_ValueError(MP_ERROR_TEXT("invalid pin"));
    machine_pin_obj_t *p = MP_STATE_PORT(cs_pinos)[id];
    if (!p) {
        p = mp_obj_malloc(machine_pin_obj_t, &machine_pin_type);
        p->id = id;
        p->aoMudar = mp_const_none;
        MP_STATE_PORT(cs_pinos)[id] = p;
    }
    return p;
}

mp_hal_pin_obj_t machine_pin_get_id(mp_obj_t pin_in)
{
    if (mp_obj_get_type(pin_in) != &machine_pin_type) mp_raise_ValueError(MP_ERROR_TEXT("expecting a pin"));
    machine_pin_obj_t *self = MP_OBJ_TO_PTR(pin_in);
    return self->id;
}

static void machine_pin_print(const mp_print_t *print, mp_obj_t self_in, mp_print_kind_t kind)
{
    machine_pin_obj_t *self = MP_OBJ_TO_PTR(self_in);
    mp_printf(print, "Pin(%d)", self->id);
}

static mp_obj_t machine_pin_obj_init_helper(machine_pin_obj_t *self, size_t n_args, const mp_obj_t *pos_args, mp_map_t *kw_args)
{
    enum { ARG_mode, ARG_pull, ARG_value, ARG_drive, ARG_hold };
    static const mp_arg_t allowed_args[] = {
        { MP_QSTR_mode, MP_ARG_OBJ, {.u_rom_obj = MP_ROM_NONE} },
        { MP_QSTR_pull, MP_ARG_OBJ, {.u_rom_obj = MP_ROM_NONE} },
        { MP_QSTR_value, MP_ARG_KW_ONLY | MP_ARG_OBJ, {.u_rom_obj = MP_ROM_NONE} },
        { MP_QSTR_drive, MP_ARG_KW_ONLY | MP_ARG_OBJ, {.u_rom_obj = MP_ROM_NONE} },
        { MP_QSTR_hold, MP_ARG_KW_ONLY | MP_ARG_OBJ, {.u_rom_obj = MP_ROM_NONE} },
    };
    mp_arg_val_t args[MP_ARRAY_SIZE(allowed_args)];
    mp_arg_parse_all(n_args, pos_args, kw_args, MP_ARRAY_SIZE(allowed_args), allowed_args, args);

    mp_obj_t modo = args[ARG_mode].u_obj;
    if (modo != mp_const_none) {
        mp_int_t m = mp_obj_get_int(modo);
        if (m == CS_PY_ENTRADA && args[ARG_pull].u_obj != mp_const_none) m = mp_obj_get_int(args[ARG_pull].u_obj);
        cs_py_pino_modo(self->id, m);
    } else if (args[ARG_pull].u_obj != mp_const_none) {
        cs_py_pino_modo(self->id, mp_obj_get_int(args[ARG_pull].u_obj));
    }
    if (args[ARG_value].u_obj != mp_const_none) cs_py_pino_escrever(self->id, mp_obj_is_true(args[ARG_value].u_obj));
    return mp_const_none;
}

mp_obj_t mp_pin_make_new(const mp_obj_type_t *type, size_t n_args, size_t n_kw, const mp_obj_t *args)
{
    mp_arg_check_num(n_args, n_kw, 1, MP_OBJ_FUN_ARGS_MAX, true);
    machine_pin_obj_t *self = pinoDe(mp_obj_get_int(args[0]));
    if (n_args > 1 || n_kw > 0) {
        mp_map_t kw_args;
        mp_map_init_fixed_table(&kw_args, n_kw, args + n_args);
        machine_pin_obj_init_helper(self, n_args - 1, args + 1, &kw_args);
    }
    return MP_OBJ_FROM_PTR(self);
}

static mp_obj_t machine_pin_call(mp_obj_t self_in, size_t n_args, size_t n_kw, const mp_obj_t *args)
{
    mp_arg_check_num(n_args, n_kw, 0, 1, false);
    machine_pin_obj_t *self = MP_OBJ_TO_PTR(self_in);
    if (n_args == 0) return MP_OBJ_NEW_SMALL_INT(cs_py_pino_ler(self->id));
    cs_py_pino_escrever(self->id, mp_obj_is_true(args[0]));
    return mp_const_none;
}

static mp_obj_t machine_pin_obj_init(size_t n_args, const mp_obj_t *args, mp_map_t *kw_args)
{
    return machine_pin_obj_init_helper(MP_OBJ_TO_PTR(args[0]), n_args - 1, args + 1, kw_args);
}
MP_DEFINE_CONST_FUN_OBJ_KW(machine_pin_init_obj, 1, machine_pin_obj_init);

static mp_obj_t machine_pin_value(size_t n_args, const mp_obj_t *args) { return machine_pin_call(args[0], n_args - 1, 0, args + 1); }
static MP_DEFINE_CONST_FUN_OBJ_VAR_BETWEEN(machine_pin_value_obj, 1, 2, machine_pin_value);

static mp_obj_t machine_pin_off(mp_obj_t self_in)
{
    machine_pin_obj_t *self = MP_OBJ_TO_PTR(self_in);
    cs_py_pino_escrever(self->id, 0);
    return mp_const_none;
}
static MP_DEFINE_CONST_FUN_OBJ_1(machine_pin_off_obj, machine_pin_off);

static mp_obj_t machine_pin_on(mp_obj_t self_in)
{
    machine_pin_obj_t *self = MP_OBJ_TO_PTR(self_in);
    cs_py_pino_escrever(self->id, 1);
    return mp_const_none;
}
static MP_DEFINE_CONST_FUN_OBJ_1(machine_pin_on_obj, machine_pin_on);

static mp_obj_t machine_pin_toggle(mp_obj_t self_in)
{
    machine_pin_obj_t *self = MP_OBJ_TO_PTR(self_in);
    cs_py_pino_escrever(self->id, !cs_py_pino_ler(self->id));
    return mp_const_none;
}
static MP_DEFINE_CONST_FUN_OBJ_1(machine_pin_toggle_obj, machine_pin_toggle);

/* A interrupcao chega na thread do vigia: o Python roda quando o agendador der a vez, como o irq sem hard. */
static void aoInterromper(void *arg)
{
    machine_pin_obj_t *self = arg;
    if (self->aoMudar != mp_const_none) mp_sched_schedule(self->aoMudar, MP_OBJ_FROM_PTR(self));
}

static mp_obj_t machine_pin_irq(size_t n_args, const mp_obj_t *pos_args, mp_map_t *kw_args)
{
    enum { ARG_handler, ARG_trigger, ARG_wake, ARG_hard };
    static const mp_arg_t allowed_args[] = {
        { MP_QSTR_handler, MP_ARG_OBJ, {.u_obj = mp_const_none} },
        { MP_QSTR_trigger, MP_ARG_INT, {.u_int = IRQ_SUBIDA | IRQ_DESCIDA} },
        { MP_QSTR_wake, MP_ARG_OBJ, {.u_obj = mp_const_none} },
        { MP_QSTR_hard, MP_ARG_BOOL, {.u_bool = false} },
    };
    machine_pin_obj_t *self = MP_OBJ_TO_PTR(pos_args[0]);
    mp_arg_val_t args[MP_ARRAY_SIZE(allowed_args)];
    mp_arg_parse_all(n_args - 1, pos_args + 1, kw_args, MP_ARRAY_SIZE(allowed_args), allowed_args, args);
    if (n_args > 1 || kw_args->used != 0) {
        mp_obj_t f = args[ARG_handler].u_obj;
        mp_int_t borda = args[ARG_trigger].u_int;
        self->aoMudar = f;
        if (f == mp_const_none || borda == 0) cs_py_pino_sem_interrupcao(self->id);
        else cs_py_pino_interrupcao(self->id, borda, aoInterromper, self);
    }
    return mp_const_none;
}
static MP_DEFINE_CONST_FUN_OBJ_KW(machine_pin_irq_obj, 1, machine_pin_irq);

static const mp_rom_map_elem_t machine_pin_locals_dict_table[] = {
    { MP_ROM_QSTR(MP_QSTR_init), MP_ROM_PTR(&machine_pin_init_obj) },
    { MP_ROM_QSTR(MP_QSTR_value), MP_ROM_PTR(&machine_pin_value_obj) },
    { MP_ROM_QSTR(MP_QSTR_off), MP_ROM_PTR(&machine_pin_off_obj) },
    { MP_ROM_QSTR(MP_QSTR_on), MP_ROM_PTR(&machine_pin_on_obj) },
    { MP_ROM_QSTR(MP_QSTR_low), MP_ROM_PTR(&machine_pin_off_obj) },
    { MP_ROM_QSTR(MP_QSTR_high), MP_ROM_PTR(&machine_pin_on_obj) },
    { MP_ROM_QSTR(MP_QSTR_toggle), MP_ROM_PTR(&machine_pin_toggle_obj) },
    { MP_ROM_QSTR(MP_QSTR_irq), MP_ROM_PTR(&machine_pin_irq_obj) },
    { MP_ROM_QSTR(MP_QSTR_IN), MP_ROM_INT(CS_PY_ENTRADA) },
    { MP_ROM_QSTR(MP_QSTR_OUT), MP_ROM_INT(CS_PY_SAIDA) },
    { MP_ROM_QSTR(MP_QSTR_OPEN_DRAIN), MP_ROM_INT(CS_PY_DRENO) },
    { MP_ROM_QSTR(MP_QSTR_PULL_UP), MP_ROM_INT(CS_PY_PULL_UP) },
    { MP_ROM_QSTR(MP_QSTR_PULL_DOWN), MP_ROM_INT(CS_PY_PULL_DOWN) },
    { MP_ROM_QSTR(MP_QSTR_IRQ_RISING), MP_ROM_INT(IRQ_SUBIDA) },
    { MP_ROM_QSTR(MP_QSTR_IRQ_FALLING), MP_ROM_INT(IRQ_DESCIDA) },
    { MP_ROM_QSTR(MP_QSTR_WAKE_LOW), MP_ROM_INT(4) },
    { MP_ROM_QSTR(MP_QSTR_WAKE_HIGH), MP_ROM_INT(5) },
    { MP_ROM_QSTR(MP_QSTR_DRIVE_0), MP_ROM_INT(0) },
    { MP_ROM_QSTR(MP_QSTR_DRIVE_1), MP_ROM_INT(1) },
    { MP_ROM_QSTR(MP_QSTR_DRIVE_2), MP_ROM_INT(2) },
    { MP_ROM_QSTR(MP_QSTR_DRIVE_3), MP_ROM_INT(3) },
};
static MP_DEFINE_CONST_DICT(machine_pin_locals_dict, machine_pin_locals_dict_table);

static mp_uint_t pin_ioctl(mp_obj_t self_in, mp_uint_t request, uintptr_t arg, int *errcode)
{
    (void)errcode;
    machine_pin_obj_t *self = MP_OBJ_TO_PTR(self_in);
    switch (request) {
    case MP_PIN_READ: return cs_py_pino_ler(self->id);
    case MP_PIN_WRITE:
        cs_py_pino_escrever(self->id, arg);
        return 0;
    }
    return -1;
}

static const mp_pin_p_t pin_pin_p = {
    .ioctl = pin_ioctl,
};

MP_DEFINE_CONST_OBJ_TYPE(
    machine_pin_type,
    MP_QSTR_Pin,
    MP_TYPE_FLAG_NONE,
    make_new, mp_pin_make_new,
    print, machine_pin_print,
    call, machine_pin_call,
    protocol, &pin_pin_p,
    locals_dict, &machine_pin_locals_dict
    );

/* A fita: o bitstream do neopixel.py, sem o tempo de cada bit (na bancada ela e um aparelho). */
void machine_bitstream_high_low(mp_hal_pin_obj_t pin, uint32_t *timing_ns, const uint8_t *buf, size_t len)
{
    (void)timing_ns;
    cs_py_fita_escrever(pin, buf, len);
}
