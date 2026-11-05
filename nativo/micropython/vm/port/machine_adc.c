/* machine_adc.c -- o ADC (incluido por extmod/machine_adc.c), com os nomes do port da ESP32. */
#include "py/mphal.h"
#include "port/cs_py_ponte.h"

/* As do adc_attenuation_t do Arduino. */
#define ADC_ATTEN_DB_0 0
#define ADC_ATTEN_DB_2_5 1
#define ADC_ATTEN_DB_6 2
#define ADC_ATTEN_DB_11 3

#define MICROPY_PY_MACHINE_ADC_CLASS_CONSTANTS \
    { MP_ROM_QSTR(MP_QSTR_ATTN_0DB), MP_ROM_INT(ADC_ATTEN_DB_0) }, \
    { MP_ROM_QSTR(MP_QSTR_ATTN_2_5DB), MP_ROM_INT(ADC_ATTEN_DB_2_5) }, \
    { MP_ROM_QSTR(MP_QSTR_ATTN_6DB), MP_ROM_INT(ADC_ATTEN_DB_6) }, \
    { MP_ROM_QSTR(MP_QSTR_ATTN_11DB), MP_ROM_INT(ADC_ATTEN_DB_11) }, \
    { MP_ROM_QSTR(MP_QSTR_WIDTH_9BIT), MP_ROM_INT(9) }, \
    { MP_ROM_QSTR(MP_QSTR_WIDTH_10BIT), MP_ROM_INT(10) }, \
    { MP_ROM_QSTR(MP_QSTR_WIDTH_11BIT), MP_ROM_INT(11) }, \
    { MP_ROM_QSTR(MP_QSTR_WIDTH_12BIT), MP_ROM_INT(12) },

typedef struct _machine_adc_obj_t {
    mp_obj_base_t base;
    int pino;
    int atenuacao;
    int bits;
} machine_adc_obj_t;

static void mp_machine_adc_print(const mp_print_t *print, mp_obj_t self_in, mp_print_kind_t kind)
{
    machine_adc_obj_t *self = MP_OBJ_TO_PTR(self_in);
    mp_printf(print, "ADC(Pin(%d), atten=%d)", self->pino, self->atenuacao);
}

static void mp_machine_adc_atten_set(machine_adc_obj_t *self, mp_int_t atten)
{
    if (atten < 0 || atten > 3) mp_raise_ValueError(MP_ERROR_TEXT("invalid attenuation"));
    self->atenuacao = atten;
    cs_py_adc_atenuacao(self->pino, atten);
}

static void mp_machine_adc_width_set(machine_adc_obj_t *self, mp_int_t width)
{
    if (width < 9 || width > 12) mp_raise_ValueError(MP_ERROR_TEXT("invalid bit width"));
    self->bits = width;
}

static void mp_machine_adc_init_helper(machine_adc_obj_t *self, size_t n_pos_args, const mp_obj_t *pos_args, mp_map_t *kw_args)
{
    enum { ARG_atten };
    static const mp_arg_t allowed_args[] = {
        { MP_QSTR_atten, MP_ARG_KW_ONLY | MP_ARG_OBJ, {.u_obj = mp_const_none} },
    };
    mp_arg_val_t args[MP_ARRAY_SIZE(allowed_args)];
    mp_arg_parse_all(n_pos_args, pos_args, kw_args, MP_ARRAY_SIZE(allowed_args), allowed_args, args);
    if (args[ARG_atten].u_obj != mp_const_none) mp_machine_adc_atten_set(self, mp_obj_get_int(args[ARG_atten].u_obj));
}

static mp_obj_t mp_machine_adc_make_new(const mp_obj_type_t *type, size_t n_args, size_t n_kw, const mp_obj_t *args)
{
    mp_arg_check_num(n_args, n_kw, 1, MP_OBJ_FUN_ARGS_MAX, true);
    int pino = mp_obj_is_int(args[0]) ? mp_obj_get_int(args[0]) : mp_hal_get_pin_obj(args[0]);
    if (!cs_py_pino_existe(pino)) mp_raise_ValueError(MP_ERROR_TEXT("invalid pin"));
    machine_adc_obj_t *self = mp_obj_malloc(machine_adc_obj_t, type);
    self->pino = pino;
    self->atenuacao = ADC_ATTEN_DB_0;
    self->bits = 12;
    mp_map_t kw_args;
    mp_map_init_fixed_table(&kw_args, n_kw, args + n_args);
    mp_machine_adc_init_helper(self, n_args - 1, args + 1, &kw_args);
    return MP_OBJ_FROM_PTR(self);
}

static void mp_machine_adc_deinit(machine_adc_obj_t *self) { (void)self; }

static mp_int_t mp_machine_adc_read(machine_adc_obj_t *self)
{
    return cs_py_adc_ler(self->pino) >> (12 - self->bits);
}

static mp_int_t mp_machine_adc_read_u16(machine_adc_obj_t *self)
{
    return (mp_int_t)cs_py_adc_ler(self->pino) * 65535 / 4095;
}

static mp_int_t mp_machine_adc_read_uv(machine_adc_obj_t *self)
{
    /* O fundo de escala de cada atenuacao, em microvolts, como o datasheet da ESP32. */
    static const int fundo[] = {950000, 1250000, 1750000, 2450000};
    int a = self->atenuacao;
    return (int64_t)cs_py_adc_ler(self->pino) * (a == 3 ? 3300000 : fundo[a]) / 4095;
}
