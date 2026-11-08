/* machine_uart.c -- a UART (incluido por extmod/machine_uart.c), por cima das HardwareSerial. */
#include "py/mphal.h"
#include "py/mperrno.h"
#include "py/stream.h"
#include "port/cs_py_ponte.h"

#define MICROPY_PY_MACHINE_UART_CLASS_CONSTANTS

typedef struct _machine_uart_obj_t {
    mp_obj_base_t base;
    int uart;
    uint32_t baud;
    int bits, paridade, parada, tx, rx;
    int esperaMs, esperaCharMs;
} machine_uart_obj_t;

static void mp_machine_uart_print(const mp_print_t *print, mp_obj_t self_in, mp_print_kind_t kind)
{
    machine_uart_obj_t *self = MP_OBJ_TO_PTR(self_in);
    mp_printf(print, "UART(%d, baudrate=%u, bits=%d, parity=", self->uart, (unsigned)self->baud, self->bits);
    if (self->paridade < 0) mp_print_str(print, "None");
    else mp_printf(print, "%d", self->paridade);
    mp_printf(print, ", stop=%d, tx=%d, rx=%d, timeout=%d, timeout_char=%d)", self->parada, self->tx, self->rx, self->esperaMs, self->esperaCharMs);
}

static void mp_machine_uart_init_helper(machine_uart_obj_t *self, size_t n_args, const mp_obj_t *pos_args, mp_map_t *kw_args)
{
    enum { ARG_baudrate, ARG_bits, ARG_parity, ARG_stop, ARG_tx, ARG_rx, ARG_rts, ARG_cts, ARG_txbuf, ARG_rxbuf, ARG_timeout, ARG_timeout_char, ARG_invert, ARG_flow };
    static const mp_arg_t allowed_args[] = {
        { MP_QSTR_baudrate, MP_ARG_INT, {.u_int = 0} },
        { MP_QSTR_bits, MP_ARG_INT, {.u_int = 0} },
        { MP_QSTR_parity, MP_ARG_OBJ, {.u_rom_obj = MP_ROM_NONE} },
        { MP_QSTR_stop, MP_ARG_INT, {.u_int = 0} },
        { MP_QSTR_tx, MP_ARG_KW_ONLY | MP_ARG_OBJ, {.u_obj = MP_OBJ_NULL} },
        { MP_QSTR_rx, MP_ARG_KW_ONLY | MP_ARG_OBJ, {.u_obj = MP_OBJ_NULL} },
        { MP_QSTR_rts, MP_ARG_KW_ONLY | MP_ARG_OBJ, {.u_obj = MP_OBJ_NULL} },
        { MP_QSTR_cts, MP_ARG_KW_ONLY | MP_ARG_OBJ, {.u_obj = MP_OBJ_NULL} },
        { MP_QSTR_txbuf, MP_ARG_KW_ONLY | MP_ARG_INT, {.u_int = -1} },
        { MP_QSTR_rxbuf, MP_ARG_KW_ONLY | MP_ARG_INT, {.u_int = -1} },
        { MP_QSTR_timeout, MP_ARG_KW_ONLY | MP_ARG_INT, {.u_int = -1} },
        { MP_QSTR_timeout_char, MP_ARG_KW_ONLY | MP_ARG_INT, {.u_int = -1} },
        { MP_QSTR_invert, MP_ARG_KW_ONLY | MP_ARG_INT, {.u_int = -1} },
        { MP_QSTR_flow, MP_ARG_KW_ONLY | MP_ARG_INT, {.u_int = -1} },
    };
    mp_arg_val_t args[MP_ARRAY_SIZE(allowed_args)];
    mp_arg_parse_all(n_args, pos_args, kw_args, MP_ARRAY_SIZE(allowed_args), allowed_args, args);
    if (args[ARG_baudrate].u_int > 0) self->baud = args[ARG_baudrate].u_int;
    if (args[ARG_bits].u_int) {
        if (args[ARG_bits].u_int < 5 || args[ARG_bits].u_int > 8) mp_raise_ValueError(MP_ERROR_TEXT("invalid data bits"));
        self->bits = args[ARG_bits].u_int;
    }
    if (args[ARG_parity].u_obj != mp_const_none) self->paridade = mp_obj_get_int(args[ARG_parity].u_obj) & 1;
    if (args[ARG_stop].u_int) {
        if (args[ARG_stop].u_int < 1 || args[ARG_stop].u_int > 2) mp_raise_ValueError(MP_ERROR_TEXT("invalid stop bits"));
        self->parada = args[ARG_stop].u_int;
    }
    if (args[ARG_tx].u_obj != MP_OBJ_NULL) self->tx = mp_hal_get_pin_obj(args[ARG_tx].u_obj);
    if (args[ARG_rx].u_obj != MP_OBJ_NULL) self->rx = mp_hal_get_pin_obj(args[ARG_rx].u_obj);
    if (args[ARG_timeout].u_int >= 0) self->esperaMs = args[ARG_timeout].u_int;
    if (args[ARG_timeout_char].u_int >= 0) self->esperaCharMs = args[ARG_timeout_char].u_int;
    cs_py_uart_iniciar(self->uart, self->baud, self->bits, self->paridade, self->parada, self->tx, self->rx, args[ARG_rxbuf].u_int);
}

static mp_obj_t mp_machine_uart_make_new(const mp_obj_type_t *type, size_t n_args, size_t n_kw, const mp_obj_t *args)
{
    mp_arg_check_num(n_args, n_kw, 1, MP_OBJ_FUN_ARGS_MAX, true);
    mp_int_t uart = mp_obj_get_int(args[0]);
    if (uart < 0 || uart > 2) mp_raise_msg_varg(&mp_type_ValueError, MP_ERROR_TEXT("UART(%d) does not exist"), uart);
    machine_uart_obj_t *self = mp_obj_malloc(machine_uart_obj_t, type);
    self->uart = uart;
    self->baud = 115200;
    self->bits = 8;
    self->paridade = -1;
    self->parada = 1;
    /* Os pinos de fabrica da ESP32. */
#if defined(ARDUINO_ARCH_RP2040)
    self->tx = uart == 0 ? 0 : 4;
    self->rx = uart == 0 ? 1 : 5;
#else
    self->tx = uart == 0 ? 1 : uart == 1 ? 10 : 17;
    self->rx = uart == 0 ? 3 : uart == 1 ? 9 : 16;
#endif
    self->esperaMs = 0;
    self->esperaCharMs = 0;
    mp_map_t kw_args;
    mp_map_init_fixed_table(&kw_args, n_kw, args + n_args);
    mp_machine_uart_init_helper(self, n_args - 1, args + 1, &kw_args);
    return MP_OBJ_FROM_PTR(self);
}

static void mp_machine_uart_deinit(machine_uart_obj_t *self) { cs_py_uart_parar(self->uart); }
static mp_int_t mp_machine_uart_any(machine_uart_obj_t *self) { return cs_py_uart_disponivel(self->uart); }
static bool mp_machine_uart_txdone(machine_uart_obj_t *self) { (void)self; return true; }
static void mp_machine_uart_sendbreak(machine_uart_obj_t *self) { cs_py_uart_quebra(self->uart); }

static mp_uint_t mp_machine_uart_read(mp_obj_t self_in, void *buf_in, mp_uint_t size, int *errcode)
{
    machine_uart_obj_t *self = MP_OBJ_TO_PTR(self_in);
    uint8_t *buf = buf_in;
    if (size == 0) return 0;
    mp_uint_t n = 0;
    mp_uint_t inicio = mp_hal_ticks_ms();
    int espera = self->esperaMs;
    while (n < size) {
        if (cs_py_uart_disponivel(self->uart) > 0) {
            buf[n++] = (uint8_t)cs_py_uart_ler(self->uart);
            inicio = mp_hal_ticks_ms();
            espera = self->esperaCharMs;
            continue;
        }
        if ((mp_int_t)(mp_hal_ticks_ms() - inicio) >= espera) break;
        mp_event_wait_ms(1);
    }
    if (n == 0) {
        *errcode = MP_EAGAIN;
        return MP_STREAM_ERROR;
    }
    return n;
}

static mp_uint_t mp_machine_uart_write(mp_obj_t self_in, const void *buf_in, mp_uint_t size, int *errcode)
{
    machine_uart_obj_t *self = MP_OBJ_TO_PTR(self_in);
    (void)errcode;
    return cs_py_uart_escrever(self->uart, buf_in, size);
}

static mp_uint_t mp_machine_uart_ioctl(mp_obj_t self_in, mp_uint_t request, uintptr_t arg, int *errcode)
{
    machine_uart_obj_t *self = MP_OBJ_TO_PTR(self_in);
    if (request == MP_STREAM_POLL) {
        mp_uint_t r = 0;
        if ((arg & MP_STREAM_POLL_RD) && cs_py_uart_disponivel(self->uart) > 0) r |= MP_STREAM_POLL_RD;
        if (arg & MP_STREAM_POLL_WR) r |= MP_STREAM_POLL_WR;
        return r;
    }
    if (request == MP_STREAM_FLUSH) return 0;
    *errcode = MP_EINVAL;
    return MP_STREAM_ERROR;
}
