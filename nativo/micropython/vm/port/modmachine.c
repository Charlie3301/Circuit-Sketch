/* modmachine.c -- o resto do modulo machine (incluido por extmod/modmachine.c). */
#include "py/mphal.h"
#include "port/cs_py_ponte.h"

typedef enum {
    CS_RESET_LIGOU = 1,
    CS_RESET_DURO,
    CS_RESET_WDT,
    CS_RESET_SONO,
    CS_RESET_SUAVE,
} cs_reset_t;

extern const mp_obj_type_t machine_timer_type;
extern const mp_obj_type_t machine_rtc_type;
extern const mp_obj_type_t machine_touchpad_type;
extern const mp_obj_type_t machine_dac_type;
extern const mp_obj_type_t machine_sdcard_type;
MP_DECLARE_CONST_FUN_OBJ_VAR_BETWEEN(machine_lightsleep_obj);

static mp_obj_t machine_wake_reason(void);
static MP_DEFINE_CONST_FUN_OBJ_0(machine_wake_reason_obj, machine_wake_reason);

#if defined(ESP32)
#define MICROPY_PY_MACHINE_SO_DA_ESP32 \
    { MP_ROM_QSTR(MP_QSTR_TouchPad), MP_ROM_PTR(&machine_touchpad_type) }, \
    { MP_ROM_QSTR(MP_QSTR_DAC), MP_ROM_PTR(&machine_dac_type) },
#else
#define MICROPY_PY_MACHINE_SO_DA_ESP32
#endif

#define MICROPY_PY_MACHINE_EXTRA_GLOBALS \
    { MP_ROM_QSTR(MP_QSTR_sleep), MP_ROM_PTR(&machine_lightsleep_obj) }, \
    { MP_ROM_QSTR(MP_QSTR_Timer), MP_ROM_PTR(&machine_timer_type) }, \
    { MP_ROM_QSTR(MP_QSTR_Pin), MP_ROM_PTR(&machine_pin_type) }, \
    { MP_ROM_QSTR(MP_QSTR_RTC), MP_ROM_PTR(&machine_rtc_type) }, \
    MICROPY_PY_MACHINE_SO_DA_ESP32 \
    { MP_ROM_QSTR(MP_QSTR_SDCard), MP_ROM_PTR(&machine_sdcard_type) }, \
    { MP_ROM_QSTR(MP_QSTR_SLEEP), MP_ROM_INT(2) }, \
    { MP_ROM_QSTR(MP_QSTR_DEEPSLEEP), MP_ROM_INT(4) }, \
    { MP_ROM_QSTR(MP_QSTR_HARD_RESET), MP_ROM_INT(CS_RESET_DURO) }, \
    { MP_ROM_QSTR(MP_QSTR_PWRON_RESET), MP_ROM_INT(CS_RESET_LIGOU) }, \
    { MP_ROM_QSTR(MP_QSTR_WDT_RESET), MP_ROM_INT(CS_RESET_WDT) }, \
    { MP_ROM_QSTR(MP_QSTR_DEEPSLEEP_RESET), MP_ROM_INT(CS_RESET_SONO) }, \
    { MP_ROM_QSTR(MP_QSTR_SOFT_RESET), MP_ROM_INT(CS_RESET_SUAVE) }, \
    { MP_ROM_QSTR(MP_QSTR_wake_reason), MP_ROM_PTR(&machine_wake_reason_obj) }, \
    { MP_ROM_QSTR(MP_QSTR_PIN_WAKE), MP_ROM_INT(2) }, \
    { MP_ROM_QSTR(MP_QSTR_EXT0_WAKE), MP_ROM_INT(2) }, \
    { MP_ROM_QSTR(MP_QSTR_EXT1_WAKE), MP_ROM_INT(3) }, \
    { MP_ROM_QSTR(MP_QSTR_TIMER_WAKE), MP_ROM_INT(4) }, \
    { MP_ROM_QSTR(MP_QSTR_TOUCHPAD_WAKE), MP_ROM_INT(5) }, \
    { MP_ROM_QSTR(MP_QSTR_ULP_WAKE), MP_ROM_INT(6) },

static mp_obj_t machine_wake_reason(void) { return MP_OBJ_NEW_SMALL_INT(0); }

static void mp_machine_idle(void)
{
    mp_event_wait_ms(1);
}

MP_NORETURN static void mp_machine_reset(void)
{
    cs_py_reiniciar();
    for (;;) {
    }
}

static mp_int_t mp_machine_reset_cause(void)
{
    /* Os do esp_reset_reason_t. */
    switch (cs_py_motivo_do_reinicio()) {
    case 8: return CS_RESET_SONO;
    case 5: case 6: case 7: return CS_RESET_WDT;
    case 3: case 4: return CS_RESET_SUAVE;
    case 2: return CS_RESET_DURO;
    default: return CS_RESET_LIGOU;
    }
}

static mp_obj_t mp_machine_unique_id(void)
{
    uint8_t mac[6];
    cs_py_wifi_mac(0, mac);
    return mp_obj_new_bytes(mac, 6);
}

#if defined(ARDUINO_ARCH_RP2040)
static mp_obj_t mp_machine_get_freq(void) { return mp_obj_new_int(125000000); }
#else
static mp_obj_t mp_machine_get_freq(void) { return mp_obj_new_int(240000000); }
#endif

static void mp_machine_set_freq(size_t n_args, const mp_obj_t *args)
{
    (void)n_args;
#if !defined(ARDUINO_ARCH_RP2040)
    mp_int_t f = mp_obj_get_int(args[0]) / 1000000;
    if (f != 20 && f != 40 && f != 80 && f != 160 && f != 240) mp_raise_ValueError(MP_ERROR_TEXT("frequency must be 20MHz, 40MHz, 80Mhz, 160MHz or 240MHz"));
#endif
}

static void mp_machine_lightsleep(size_t n_args, const mp_obj_t *args)
{
    cs_py_dormir_leve(n_args ? (uint64_t)mp_obj_get_int(args[0]) * 1000 : 0);
}

MP_NORETURN static void mp_machine_deepsleep(size_t n_args, const mp_obj_t *args)
{
    cs_py_dormir_fundo(n_args ? (uint64_t)mp_obj_get_int(args[0]) * 1000 : 0);
    for (;;) {
    }
}
