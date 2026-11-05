/* machine_diversos.c -- machine.Timer, RTC, TouchPad, DAC e SDCard, e os modulos esp e esp32. */
#include <string.h>

#include "py/mphal.h"
#include "py/runtime.h"
#include "extmod/modmachine.h"
#include "shared/timeutils/timeutils.h"
#include "port/cs_py_ponte.h"

/* ---- Timer ---- */
typedef struct _machine_timer_obj_t {
    mp_obj_base_t base;
    int id;
    void *tempo;
    mp_obj_t aoEsgotar;
    uint64_t periodoUs;
    bool repete;
} machine_timer_obj_t;

MP_REGISTER_ROOT_POINTER(void *cs_timers[4]);
const mp_obj_type_t machine_timer_type;

static void aoEsgotar(void *arg)
{
    machine_timer_obj_t *self = arg;
    if (self->aoEsgotar != mp_const_none) mp_sched_schedule(self->aoEsgotar, MP_OBJ_FROM_PTR(self));
}

static void machine_timer_print(const mp_print_t *print, mp_obj_t self_in, mp_print_kind_t kind)
{
    machine_timer_obj_t *self = MP_OBJ_TO_PTR(self_in);
    mp_printf(print, "Timer(%d, period=%u)", self->id, (unsigned)(self->periodoUs / 1000));
}

static mp_obj_t machine_timer_init_helper(machine_timer_obj_t *self, size_t n_args, const mp_obj_t *pos_args, mp_map_t *kw_args)
{
    enum { ARG_mode, ARG_callback, ARG_period, ARG_tick_hz, ARG_freq };
    static const mp_arg_t allowed_args[] = {
        { MP_QSTR_mode, MP_ARG_KW_ONLY | MP_ARG_INT, {.u_int = 1} },
        { MP_QSTR_callback, MP_ARG_KW_ONLY | MP_ARG_OBJ, {.u_obj = mp_const_none} },
        { MP_QSTR_period, MP_ARG_KW_ONLY | MP_ARG_INT, {.u_int = 0xffffffff} },
        { MP_QSTR_tick_hz, MP_ARG_KW_ONLY | MP_ARG_INT, {.u_int = 1000} },
        { MP_QSTR_freq, MP_ARG_KW_ONLY | MP_ARG_OBJ, {.u_obj = mp_const_none} },
    };
    mp_arg_val_t args[MP_ARRAY_SIZE(allowed_args)];
    mp_arg_parse_all(n_args, pos_args, kw_args, MP_ARRAY_SIZE(allowed_args), allowed_args, args);
    if (args[ARG_freq].u_obj != mp_const_none) {
        mp_float_t f = mp_obj_get_float(args[ARG_freq].u_obj);
        if (f <= 0) mp_raise_ValueError(MP_ERROR_TEXT("freq must be positive"));
        self->periodoUs = (uint64_t)(1000000.0f / f);
    } else {
        self->periodoUs = (uint64_t)args[ARG_period].u_int * 1000000 / args[ARG_tick_hz].u_int;
    }
    if (self->periodoUs == 0) self->periodoUs = 1;
    self->repete = args[ARG_mode].u_int != 0;
    self->aoEsgotar = args[ARG_callback].u_obj;
    if (!self->tempo) self->tempo = cs_py_timer_novo(aoEsgotar, self);
    cs_py_timer_iniciar(self->tempo, self->periodoUs, self->repete);
    return mp_const_none;
}

static mp_obj_t machine_timer_make_new(const mp_obj_type_t *type, size_t n_args, size_t n_kw, const mp_obj_t *args)
{
    mp_arg_check_num(n_args, n_kw, 1, MP_OBJ_FUN_ARGS_MAX, true);
    mp_int_t id = mp_obj_get_int(args[0]);
    if (id < -1 || id > 3) mp_raise_ValueError(MP_ERROR_TEXT("invalid Timer number"));
    machine_timer_obj_t *self = id >= 0 ? MP_STATE_PORT(cs_timers)[id] : NULL;
    if (!self) {
        self = mp_obj_malloc(machine_timer_obj_t, type);
        self->id = id;
        self->tempo = NULL;
        self->aoEsgotar = mp_const_none;
        self->periodoUs = 0;
        if (id >= 0) MP_STATE_PORT(cs_timers)[id] = self;
    }
    if (n_args > 1 || n_kw > 0) {
        mp_map_t kw_args;
        mp_map_init_fixed_table(&kw_args, n_kw, args + n_args);
        machine_timer_init_helper(self, n_args - 1, args + 1, &kw_args);
    }
    return MP_OBJ_FROM_PTR(self);
}

static mp_obj_t machine_timer_init(size_t n_args, const mp_obj_t *args, mp_map_t *kw_args)
{
    return machine_timer_init_helper(MP_OBJ_TO_PTR(args[0]), n_args - 1, args + 1, kw_args);
}
static MP_DEFINE_CONST_FUN_OBJ_KW(machine_timer_init_obj, 1, machine_timer_init);

static mp_obj_t machine_timer_deinit(mp_obj_t self_in)
{
    machine_timer_obj_t *self = MP_OBJ_TO_PTR(self_in);
    if (self->tempo) cs_py_timer_parar(self->tempo);
    return mp_const_none;
}
static MP_DEFINE_CONST_FUN_OBJ_1(machine_timer_deinit_obj, machine_timer_deinit);

static mp_obj_t machine_timer_value(mp_obj_t self_in)
{
    machine_timer_obj_t *self = MP_OBJ_TO_PTR(self_in);
    return mp_obj_new_int_from_ull(self->periodoUs / 1000);
}
static MP_DEFINE_CONST_FUN_OBJ_1(machine_timer_value_obj, machine_timer_value);

static const mp_rom_map_elem_t machine_timer_locals_dict_table[] = {
    { MP_ROM_QSTR(MP_QSTR_deinit), MP_ROM_PTR(&machine_timer_deinit_obj) },
    { MP_ROM_QSTR(MP_QSTR_init), MP_ROM_PTR(&machine_timer_init_obj) },
    { MP_ROM_QSTR(MP_QSTR_value), MP_ROM_PTR(&machine_timer_value_obj) },
    { MP_ROM_QSTR(MP_QSTR_ONE_SHOT), MP_ROM_INT(0) },
    { MP_ROM_QSTR(MP_QSTR_PERIODIC), MP_ROM_INT(1) },
};
static MP_DEFINE_CONST_DICT(machine_timer_locals_dict, machine_timer_locals_dict_table);

MP_DEFINE_CONST_OBJ_TYPE(
    machine_timer_type,
    MP_QSTR_Timer,
    MP_TYPE_FLAG_NONE,
    make_new, machine_timer_make_new,
    print, machine_timer_print,
    locals_dict, &machine_timer_locals_dict
    );

/* ---- RTC ---- */
typedef struct {
    mp_obj_base_t base;
} machine_rtc_obj_t;

extern const mp_obj_type_t machine_rtc_type;
static const machine_rtc_obj_t oRtc = {{&machine_rtc_type}};
static uint8_t memoriaDoRtc[2048];
static size_t tamanhoDaMemoriaDoRtc;

static mp_obj_t machine_rtc_make_new(const mp_obj_type_t *type, size_t n_args, size_t n_kw, const mp_obj_t *args)
{
    mp_arg_check_num(n_args, n_kw, 0, 0, false);
    return (mp_obj_t)&oRtc;
}

static mp_obj_t machine_rtc_datetime(size_t n_args, const mp_obj_t *args)
{
    if (n_args == 1) {
        int64_t us = cs_py_epoca_us();
        timeutils_struct_time_t tm;
        timeutils_seconds_since_epoch_to_struct_time(us / 1000000, &tm);
        mp_obj_t t[8] = {
            mp_obj_new_int(tm.tm_year), mp_obj_new_int(tm.tm_mon), mp_obj_new_int(tm.tm_mday), mp_obj_new_int(tm.tm_wday),
            mp_obj_new_int(tm.tm_hour), mp_obj_new_int(tm.tm_min), mp_obj_new_int(tm.tm_sec), mp_obj_new_int(us % 1000000),
        };
        return mp_obj_new_tuple(8, t);
    }
    mp_obj_t *itens;
    mp_obj_get_array_fixed_n(args[1], 8, &itens);
    int64_t segundos = timeutils_seconds_since_epoch(mp_obj_get_int(itens[0]), mp_obj_get_int(itens[1]), mp_obj_get_int(itens[2]),
                                                     mp_obj_get_int(itens[4]), mp_obj_get_int(itens[5]), mp_obj_get_int(itens[6]));
    cs_py_acertar_epoca_us(segundos * 1000000 + mp_obj_get_int(itens[7]));
    return mp_const_none;
}
static MP_DEFINE_CONST_FUN_OBJ_VAR_BETWEEN(machine_rtc_datetime_obj, 1, 2, machine_rtc_datetime);

static mp_obj_t machine_rtc_init(mp_obj_t self_in, mp_obj_t data)
{
    mp_obj_t a[2] = {self_in, data};
    return machine_rtc_datetime(2, a);
}
static MP_DEFINE_CONST_FUN_OBJ_2(machine_rtc_init_obj, machine_rtc_init);

static mp_obj_t machine_rtc_memory(size_t n_args, const mp_obj_t *args)
{
    if (n_args == 1) return mp_obj_new_bytes(memoriaDoRtc, tamanhoDaMemoriaDoRtc);
    mp_buffer_info_t b;
    mp_get_buffer_raise(args[1], &b, MP_BUFFER_READ);
    if (b.len > sizeof(memoriaDoRtc)) mp_raise_ValueError(MP_ERROR_TEXT("buffer too long"));
    memcpy(memoriaDoRtc, b.buf, b.len);
    tamanhoDaMemoriaDoRtc = b.len;
    return mp_const_none;
}
static MP_DEFINE_CONST_FUN_OBJ_VAR_BETWEEN(machine_rtc_memory_obj, 1, 2, machine_rtc_memory);

static const mp_rom_map_elem_t machine_rtc_locals_dict_table[] = {
    { MP_ROM_QSTR(MP_QSTR_init), MP_ROM_PTR(&machine_rtc_init_obj) },
    { MP_ROM_QSTR(MP_QSTR_datetime), MP_ROM_PTR(&machine_rtc_datetime_obj) },
    { MP_ROM_QSTR(MP_QSTR_memory), MP_ROM_PTR(&machine_rtc_memory_obj) },
};
static MP_DEFINE_CONST_DICT(machine_rtc_locals_dict, machine_rtc_locals_dict_table);

MP_DEFINE_CONST_OBJ_TYPE(
    machine_rtc_type,
    MP_QSTR_RTC,
    MP_TYPE_FLAG_NONE,
    make_new, machine_rtc_make_new,
    locals_dict, &machine_rtc_locals_dict
    );

/* ---- TouchPad ---- */
typedef struct {
    mp_obj_base_t base;
    int pino;
} machine_pino_simples_t;

static mp_obj_t machine_touchpad_make_new(const mp_obj_type_t *type, size_t n_args, size_t n_kw, const mp_obj_t *args)
{
    mp_arg_check_num(n_args, n_kw, 1, 1, false);
    machine_pino_simples_t *self = mp_obj_malloc(machine_pino_simples_t, type);
    self->pino = mp_hal_get_pin_obj(args[0]);
    return MP_OBJ_FROM_PTR(self);
}

static mp_obj_t machine_touchpad_read(mp_obj_t self_in)
{
    machine_pino_simples_t *self = MP_OBJ_TO_PTR(self_in);
    return MP_OBJ_NEW_SMALL_INT(cs_py_toque_ler(self->pino));
}
static MP_DEFINE_CONST_FUN_OBJ_1(machine_touchpad_read_obj, machine_touchpad_read);

static mp_obj_t machine_touchpad_config(mp_obj_t self_in, mp_obj_t valor)
{
    (void)self_in;
    (void)valor;
    return mp_const_none;
}
static MP_DEFINE_CONST_FUN_OBJ_2(machine_touchpad_config_obj, machine_touchpad_config);

static const mp_rom_map_elem_t machine_touchpad_locals_dict_table[] = {
    { MP_ROM_QSTR(MP_QSTR_read), MP_ROM_PTR(&machine_touchpad_read_obj) },
    { MP_ROM_QSTR(MP_QSTR_config), MP_ROM_PTR(&machine_touchpad_config_obj) },
};
static MP_DEFINE_CONST_DICT(machine_touchpad_locals_dict, machine_touchpad_locals_dict_table);

MP_DEFINE_CONST_OBJ_TYPE(
    machine_touchpad_type,
    MP_QSTR_TouchPad,
    MP_TYPE_FLAG_NONE,
    make_new, machine_touchpad_make_new,
    locals_dict, &machine_touchpad_locals_dict
    );

/* ---- DAC ---- */
static mp_obj_t machine_dac_make_new(const mp_obj_type_t *type, size_t n_args, size_t n_kw, const mp_obj_t *args)
{
    mp_arg_check_num(n_args, n_kw, 1, 1, false);
    int pino = mp_hal_get_pin_obj(args[0]);
    if (pino != 25 && pino != 26) mp_raise_ValueError(MP_ERROR_TEXT("invalid Pin for DAC"));
    machine_pino_simples_t *self = mp_obj_malloc(machine_pino_simples_t, type);
    self->pino = pino;
    return MP_OBJ_FROM_PTR(self);
}

static mp_obj_t machine_dac_write(mp_obj_t self_in, mp_obj_t valor)
{
    machine_pino_simples_t *self = MP_OBJ_TO_PTR(self_in);
    mp_int_t v = mp_obj_get_int(valor);
    if (v < 0 || v > 255) mp_raise_ValueError(MP_ERROR_TEXT("value out of range"));
    cs_py_dac_escrever(self->pino, v);
    return mp_const_none;
}
static MP_DEFINE_CONST_FUN_OBJ_2(machine_dac_write_obj, machine_dac_write);

static const mp_rom_map_elem_t machine_dac_locals_dict_table[] = {
    { MP_ROM_QSTR(MP_QSTR_write), MP_ROM_PTR(&machine_dac_write_obj) },
};
static MP_DEFINE_CONST_DICT(machine_dac_locals_dict, machine_dac_locals_dict_table);

MP_DEFINE_CONST_OBJ_TYPE(
    machine_dac_type,
    MP_QSTR_DAC,
    MP_TYPE_FLAG_NONE,
    make_new, machine_dac_make_new,
    locals_dict, &machine_dac_locals_dict
    );

/* ---- esp ---- */
static mp_obj_t esp_osdebug(size_t n_args, const mp_obj_t *args)
{
    (void)n_args;
    (void)args;
    return mp_const_none;
}
static MP_DEFINE_CONST_FUN_OBJ_VAR_BETWEEN(esp_osdebug_obj, 1, 2, esp_osdebug);

static mp_obj_t esp_flash_size(void) { return mp_obj_new_int(4 * 1024 * 1024); }
static MP_DEFINE_CONST_FUN_OBJ_0(esp_flash_size_obj, esp_flash_size);

static const mp_rom_map_elem_t esp_module_globals_table[] = {
    { MP_ROM_QSTR(MP_QSTR___name__), MP_ROM_QSTR(MP_QSTR_esp) },
    { MP_ROM_QSTR(MP_QSTR_osdebug), MP_ROM_PTR(&esp_osdebug_obj) },
    { MP_ROM_QSTR(MP_QSTR_flash_size), MP_ROM_PTR(&esp_flash_size_obj) },
    { MP_ROM_QSTR(MP_QSTR_LOG_NONE), MP_ROM_INT(0) },
    { MP_ROM_QSTR(MP_QSTR_LOG_ERROR), MP_ROM_INT(1) },
    { MP_ROM_QSTR(MP_QSTR_LOG_WARNING), MP_ROM_INT(2) },
    { MP_ROM_QSTR(MP_QSTR_LOG_INFO), MP_ROM_INT(3) },
    { MP_ROM_QSTR(MP_QSTR_LOG_DEBUG), MP_ROM_INT(4) },
    { MP_ROM_QSTR(MP_QSTR_LOG_VERBOSE), MP_ROM_INT(5) },
};
static MP_DEFINE_CONST_DICT(esp_module_globals, esp_module_globals_table);
const mp_obj_module_t esp_module = {
    .base = { &mp_type_module },
    .globals = (mp_obj_dict_t *)&esp_module_globals,
};
MP_REGISTER_MODULE(MP_QSTR_esp, esp_module);

/* ---- esp32 ---- */
static mp_obj_t esp32_raw_temperature(void) { return MP_OBJ_NEW_SMALL_INT(128); }
static MP_DEFINE_CONST_FUN_OBJ_0(esp32_raw_temperature_obj, esp32_raw_temperature);

static mp_obj_t esp32_idf_heap_info(mp_obj_t caps)
{
    (void)caps;
    return mp_obj_new_list(0, NULL);
}
static MP_DEFINE_CONST_FUN_OBJ_1(esp32_idf_heap_info_obj, esp32_idf_heap_info);

static const mp_rom_map_elem_t esp32_module_globals_table[] = {
    { MP_ROM_QSTR(MP_QSTR___name__), MP_ROM_QSTR(MP_QSTR_esp32) },
    { MP_ROM_QSTR(MP_QSTR_raw_temperature), MP_ROM_PTR(&esp32_raw_temperature_obj) },
    { MP_ROM_QSTR(MP_QSTR_idf_heap_info), MP_ROM_PTR(&esp32_idf_heap_info_obj) },
    { MP_ROM_QSTR(MP_QSTR_HEAP_DATA), MP_ROM_INT(4) },
    { MP_ROM_QSTR(MP_QSTR_HEAP_EXEC), MP_ROM_INT(1) },
};
static MP_DEFINE_CONST_DICT(esp32_module_globals, esp32_module_globals_table);
const mp_obj_module_t esp32_module = {
    .base = { &mp_type_module },
    .globals = (mp_obj_dict_t *)&esp32_module_globals,
};
MP_REGISTER_MODULE(MP_QSTR_esp32, esp32_module);

/* ---- _cs: o que as bibliotecas em Python daqui (dht.py) pedem da bancada ---- */
bool cs_py_dht_medir(int pino, int tipo, float *temperatura, float *umidade);

static mp_obj_t cs_dht(mp_obj_t pino, mp_obj_t tipo)
{
    float t = 0, h = 0;
    if (!cs_py_dht_medir(mp_hal_get_pin_obj(pino), mp_obj_get_int(tipo), &t, &h)) return mp_const_none;
    mp_obj_t par[2] = {mp_obj_new_float(t), mp_obj_new_float(h)};
    return mp_obj_new_tuple(2, par);
}
static MP_DEFINE_CONST_FUN_OBJ_2(cs_dht_obj, cs_dht);

static const mp_rom_map_elem_t cs_module_globals_table[] = {
    { MP_ROM_QSTR(MP_QSTR___name__), MP_ROM_QSTR(MP_QSTR__cs) },
    { MP_ROM_QSTR(MP_QSTR_dht), MP_ROM_PTR(&cs_dht_obj) },
};
static MP_DEFINE_CONST_DICT(cs_module_globals, cs_module_globals_table);
const mp_obj_module_t cs_module = {
    .base = { &mp_type_module },
    .globals = (mp_obj_dict_t *)&cs_module_globals,
};
MP_REGISTER_MODULE(MP_QSTR__cs, cs_module);
