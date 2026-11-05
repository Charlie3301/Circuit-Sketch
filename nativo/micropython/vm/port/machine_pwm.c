/* machine_pwm.c -- o PWM (incluido por extmod/machine_pwm.c), com o duty de 16 bits guardado. */
#include "py/mphal.h"
#include "port/cs_py_ponte.h"

typedef struct _machine_pwm_obj_t {
    mp_obj_base_t base;
    int pino;
    bool ativo;
    uint32_t freq;
    uint32_t duty16;
} machine_pwm_obj_t;

static void mp_machine_pwm_print(const mp_print_t *print, mp_obj_t self_in, mp_print_kind_t kind)
{
    machine_pwm_obj_t *self = MP_OBJ_TO_PTR(self_in);
    mp_printf(print, "PWM(Pin(%d)", self->pino);
    if (self->ativo) mp_printf(print, ", freq=%u, duty_u16=%u", (unsigned)self->freq, (unsigned)self->duty16);
    mp_printf(print, ")");
}

static void aplicar(machine_pwm_obj_t *self)
{
    cs_py_pwm_iniciar(self->pino, self->freq, self->duty16);
    self->ativo = true;
}

static void mp_machine_pwm_init_helper(machine_pwm_obj_t *self, size_t n_args, const mp_obj_t *pos_args, mp_map_t *kw_args)
{
    enum { ARG_freq, ARG_duty, ARG_duty_u16, ARG_duty_ns };
    static const mp_arg_t allowed_args[] = {
        { MP_QSTR_freq, MP_ARG_INT, {.u_int = -1} },
        { MP_QSTR_duty, MP_ARG_INT, {.u_int = -1} },
        { MP_QSTR_duty_u16, MP_ARG_KW_ONLY | MP_ARG_INT, {.u_int = -1} },
        { MP_QSTR_duty_ns, MP_ARG_KW_ONLY | MP_ARG_INT, {.u_int = -1} },
    };
    mp_arg_val_t args[MP_ARRAY_SIZE(allowed_args)];
    mp_arg_parse_all(n_args, pos_args, kw_args, MP_ARRAY_SIZE(allowed_args), allowed_args, args);
    if (args[ARG_freq].u_int == 0 || args[ARG_freq].u_int > 40000000) mp_raise_ValueError(MP_ERROR_TEXT("freq must be between 1Hz and 40MHz"));
    if (args[ARG_freq].u_int > 0) self->freq = args[ARG_freq].u_int;
    if (!self->freq) self->freq = 5000;
    if (args[ARG_duty].u_int >= 0) self->duty16 = (uint32_t)args[ARG_duty].u_int * 65535 / 1023;
    if (args[ARG_duty_u16].u_int >= 0) self->duty16 = args[ARG_duty_u16].u_int;
    if (args[ARG_duty_ns].u_int >= 0) self->duty16 = (uint64_t)args[ARG_duty_ns].u_int * self->freq * 65535 / 1000000000ull;
    if (self->duty16 > 65535) self->duty16 = 65535;
    aplicar(self);
}

static mp_obj_t mp_machine_pwm_make_new(const mp_obj_type_t *type, size_t n_args, size_t n_kw, const mp_obj_t *args)
{
    mp_arg_check_num(n_args, n_kw, 1, 2, true);
    machine_pwm_obj_t *self = mp_obj_malloc(machine_pwm_obj_t, type);
    self->pino = mp_hal_get_pin_obj(args[0]);
    self->ativo = false;
    self->freq = 0;
    self->duty16 = 32768;
    mp_map_t kw_args;
    mp_map_init_fixed_table(&kw_args, n_kw, args + n_args);
    mp_machine_pwm_init_helper(self, n_args - 1, args + 1, &kw_args);
    return MP_OBJ_FROM_PTR(self);
}

static void mp_machine_pwm_deinit(machine_pwm_obj_t *self)
{
    cs_py_pwm_parar(self->pino);
    self->ativo = false;
}

static mp_obj_t mp_machine_pwm_freq_get(machine_pwm_obj_t *self) { return MP_OBJ_NEW_SMALL_INT(self->freq); }

static void mp_machine_pwm_freq_set(machine_pwm_obj_t *self, mp_int_t freq)
{
    if (freq <= 0 || freq > 40000000) mp_raise_ValueError(MP_ERROR_TEXT("freq must be between 1Hz and 40MHz"));
    self->freq = freq;
    aplicar(self);
}

static mp_obj_t mp_machine_pwm_duty_get(machine_pwm_obj_t *self) { return MP_OBJ_NEW_SMALL_INT(self->duty16 * 1023 / 65535); }

static void definirDuty(machine_pwm_obj_t *self, uint32_t duty16)
{
    self->duty16 = duty16 > 65535 ? 65535 : duty16;
    if (!self->ativo) aplicar(self);
    else cs_py_pwm_duty(self->pino, self->duty16);
}

static void mp_machine_pwm_duty_set(machine_pwm_obj_t *self, mp_int_t duty) { definirDuty(self, (uint32_t)duty * 65535 / 1023); }
static mp_obj_t mp_machine_pwm_duty_get_u16(machine_pwm_obj_t *self) { return MP_OBJ_NEW_SMALL_INT(self->duty16); }
static void mp_machine_pwm_duty_set_u16(machine_pwm_obj_t *self, mp_int_t duty_u16) { definirDuty(self, duty_u16); }
static mp_obj_t mp_machine_pwm_duty_get_ns(machine_pwm_obj_t *self) { return mp_obj_new_int((uint64_t)self->duty16 * 1000000000ull / self->freq / 65535); }
static void mp_machine_pwm_duty_set_ns(machine_pwm_obj_t *self, mp_int_t duty_ns) { definirDuty(self, (uint64_t)duty_ns * self->freq * 65535 / 1000000000ull); }
