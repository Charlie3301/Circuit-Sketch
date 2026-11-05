/* mphalport.h -- o que o MicroPython pede do hardware: o relogio, os pinos, a espera. */
#ifndef CS_MPHALPORT_H
#define CS_MPHALPORT_H

#include <stdint.h>
#include <stddef.h>
#include "py/obj.h"
#include "shared/runtime/interrupt_char.h"
#include "port/cs_py_ponte.h"

static inline mp_uint_t mp_hal_ticks_ms(void) { return cs_py_ms(); }
static inline mp_uint_t mp_hal_ticks_us(void) { return cs_py_us(); }
static inline mp_uint_t mp_hal_ticks_cpu(void) { return (mp_uint_t)cs_py_us() * 240; }
void mp_hal_delay_ms(mp_uint_t ms);
void mp_hal_delay_us(mp_uint_t us);
#define mp_hal_delay_us_fast(us) cs_py_esperar_us(us)
#define mp_hal_quiet_timing_enter() MICROPY_BEGIN_ATOMIC_SECTION()
#define mp_hal_quiet_timing_exit(estado) MICROPY_END_ATOMIC_SECTION(estado)
uint64_t mp_hal_time_ns(void);

#define MP_HAL_PIN_FMT "%u"
#define mp_hal_pin_obj_t int
mp_hal_pin_obj_t machine_pin_get_id(mp_obj_t pin_in);
#define mp_hal_get_pin_obj(o) machine_pin_get_id(o)
#define mp_hal_pin_name(p) (p)
static inline void mp_hal_pin_input(mp_hal_pin_obj_t p) { cs_py_pino_modo(p, CS_PY_ENTRADA); }
static inline void mp_hal_pin_output(mp_hal_pin_obj_t p) { cs_py_pino_modo(p, CS_PY_SAIDA); }
static inline void mp_hal_pin_open_drain(mp_hal_pin_obj_t p) { cs_py_pino_modo(p, CS_PY_DRENO); }
static inline void mp_hal_pin_od_low(mp_hal_pin_obj_t p) { cs_py_pino_escrever(p, 0); }
static inline void mp_hal_pin_od_high(mp_hal_pin_obj_t p) { cs_py_pino_escrever(p, 1); }
static inline int mp_hal_pin_read(mp_hal_pin_obj_t p) { return cs_py_pino_ler(p); }
static inline void mp_hal_pin_write(mp_hal_pin_obj_t p, int v) { cs_py_pino_escrever(p, v); }
#define mp_hal_pin_high(p) mp_hal_pin_write(p, 1)
#define mp_hal_pin_low(p) mp_hal_pin_write(p, 0)

#endif
