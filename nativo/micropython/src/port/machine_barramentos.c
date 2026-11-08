/* machine_barramentos.c -- machine.I2C e machine.SPI de hardware, por cima do Wire e do SPI da placa. */
#include "py/mphal.h"
#include "py/mperrno.h"
#include "py/runtime.h"
#include "extmod/modmachine.h"
#include "port/cs_py_ponte.h"

/* ---- I2C ---- */
typedef struct _machine_hw_i2c_obj_t {
    mp_obj_base_t base;
    int8_t bus;
    int sda, scl;
    uint32_t freq;
    uint32_t espera;
} machine_hw_i2c_obj_t;

static machine_hw_i2c_obj_t i2cs[2];

static void machine_hw_i2c_print(const mp_print_t *print, mp_obj_t self_in, mp_print_kind_t kind)
{
    machine_hw_i2c_obj_t *self = MP_OBJ_TO_PTR(self_in);
    mp_printf(print, "I2C(%d, scl=%d, sda=%d, freq=%u)", self->bus, self->scl, self->sda, (unsigned)self->freq);
}

mp_obj_t machine_hw_i2c_make_new(const mp_obj_type_t *type, size_t n_args, size_t n_kw, const mp_obj_t *all_args)
{
    MP_MACHINE_I2C_CHECK_FOR_LEGACY_SOFTI2C_CONSTRUCTION(n_args, n_kw, all_args);
    enum { ARG_id, ARG_scl, ARG_sda, ARG_freq, ARG_timeout };
    static const mp_arg_t allowed_args[] = {
        { MP_QSTR_id, MP_ARG_REQUIRED | MP_ARG_OBJ, {.u_obj = MP_OBJ_NULL} },
        { MP_QSTR_scl, MP_ARG_KW_ONLY | MP_ARG_OBJ, {.u_rom_obj = MP_ROM_NONE} },
        { MP_QSTR_sda, MP_ARG_KW_ONLY | MP_ARG_OBJ, {.u_rom_obj = MP_ROM_NONE} },
        { MP_QSTR_freq, MP_ARG_KW_ONLY | MP_ARG_INT, {.u_int = 400000} },
        { MP_QSTR_timeout, MP_ARG_KW_ONLY | MP_ARG_INT, {.u_int = 50000} },
    };
    mp_arg_val_t args[MP_ARRAY_SIZE(allowed_args)];
    mp_arg_parse_all_kw_array(n_args, n_kw, all_args, MP_ARRAY_SIZE(allowed_args), allowed_args, args);
    mp_int_t bus = mp_obj_get_int(args[ARG_id].u_obj);
    if (bus < 0 || bus > 1) mp_raise_msg_varg(&mp_type_ValueError, MP_ERROR_TEXT("I2C(%d) doesn't exist"), bus);
    machine_hw_i2c_obj_t *self = &i2cs[bus];
    self->base.type = &machine_i2c_type;
    self->bus = bus;
    /* Os pinos de fabrica do port da ESP32. */
#if defined(ARDUINO_ARCH_RP2040)
    self->scl = args[ARG_scl].u_obj != mp_const_none ? mp_hal_get_pin_obj(args[ARG_scl].u_obj) : bus == 0 ? 9 : 7;
    self->sda = args[ARG_sda].u_obj != mp_const_none ? mp_hal_get_pin_obj(args[ARG_sda].u_obj) : bus == 0 ? 8 : 6;
#else
    self->scl = args[ARG_scl].u_obj != mp_const_none ? mp_hal_get_pin_obj(args[ARG_scl].u_obj) : bus == 0 ? 18 : 25;
    self->sda = args[ARG_sda].u_obj != mp_const_none ? mp_hal_get_pin_obj(args[ARG_sda].u_obj) : bus == 0 ? 19 : 26;
#endif
    self->freq = args[ARG_freq].u_int;
    self->espera = args[ARG_timeout].u_int;
    cs_py_i2c_iniciar(bus, self->sda, self->scl, self->freq);
    return MP_OBJ_FROM_PTR(self);
}

static int machine_hw_i2c_transfer_single(mp_obj_base_t *self_in, uint16_t addr, size_t len, uint8_t *buf, unsigned int flags)
{
    machine_hw_i2c_obj_t *self = (machine_hw_i2c_obj_t *)self_in;
    bool parar = (flags & MP_MACHINE_I2C_FLAG_STOP) != 0;
    if (flags & MP_MACHINE_I2C_FLAG_READ) {
        int n = cs_py_i2c_ler(self->bus, addr, buf, len, parar);
        return n == (int)len ? n : -MP_ENODEV;
    }
    int r = cs_py_i2c_escrever(self->bus, addr, buf, len, parar);
    /* O endTransmission: 0 deu certo; 2 e 3, ninguem respondeu; o resto, tempo esgotado. */
    return r == 0 ? (int)len : r == 2 || r == 3 ? -MP_ENODEV : -MP_ETIMEDOUT;
}

static const mp_machine_i2c_p_t machine_hw_i2c_p = {
    .transfer = mp_machine_i2c_transfer_adaptor,
    .transfer_single = machine_hw_i2c_transfer_single,
};

MP_DEFINE_CONST_OBJ_TYPE(
    machine_i2c_type,
    MP_QSTR_I2C,
    MP_TYPE_FLAG_NONE,
    make_new, machine_hw_i2c_make_new,
    print, machine_hw_i2c_print,
    protocol, &machine_hw_i2c_p,
    locals_dict, &mp_machine_i2c_locals_dict
    );

/* ---- SPI ---- */
typedef struct _machine_hw_spi_obj_t {
    mp_obj_base_t base;
    int bus;
    uint32_t baud;
    int polaridade, fase, bits, primeiro, sck, mosi, miso;
    bool ligado;
} machine_hw_spi_obj_t;

static machine_hw_spi_obj_t spis[3];

static void machine_hw_spi_print(const mp_print_t *print, mp_obj_t self_in, mp_print_kind_t kind)
{
    machine_hw_spi_obj_t *self = MP_OBJ_TO_PTR(self_in);
    mp_printf(print, "SPI(id=%d, baudrate=%u, polarity=%d, phase=%d, bits=%d, firstbit=%d, sck=%d, mosi=%d, miso=%d)",
              self->bus, (unsigned)self->baud, self->polaridade, self->fase, self->bits, self->primeiro, self->sck, self->mosi, self->miso);
}

static void machine_hw_spi_init(mp_obj_base_t *self_in, size_t n_args, const mp_obj_t *pos_args, mp_map_t *kw_args)
{
    machine_hw_spi_obj_t *self = (machine_hw_spi_obj_t *)self_in;
    enum { ARG_baudrate, ARG_polarity, ARG_phase, ARG_bits, ARG_firstbit, ARG_sck, ARG_mosi, ARG_miso };
    static const mp_arg_t allowed_args[] = {
        { MP_QSTR_baudrate, MP_ARG_INT, {.u_int = -1} },
        { MP_QSTR_polarity, MP_ARG_KW_ONLY | MP_ARG_INT, {.u_int = -1} },
        { MP_QSTR_phase, MP_ARG_KW_ONLY | MP_ARG_INT, {.u_int = -1} },
        { MP_QSTR_bits, MP_ARG_KW_ONLY | MP_ARG_INT, {.u_int = -1} },
        { MP_QSTR_firstbit, MP_ARG_KW_ONLY | MP_ARG_INT, {.u_int = -1} },
        { MP_QSTR_sck, MP_ARG_KW_ONLY | MP_ARG_OBJ, {.u_obj = MP_OBJ_NULL} },
        { MP_QSTR_mosi, MP_ARG_KW_ONLY | MP_ARG_OBJ, {.u_obj = MP_OBJ_NULL} },
        { MP_QSTR_miso, MP_ARG_KW_ONLY | MP_ARG_OBJ, {.u_obj = MP_OBJ_NULL} },
    };
    mp_arg_val_t args[MP_ARRAY_SIZE(allowed_args)];
    mp_arg_parse_all(n_args, pos_args, kw_args, MP_ARRAY_SIZE(allowed_args), allowed_args, args);
    if (args[ARG_baudrate].u_int >= 0) self->baud = args[ARG_baudrate].u_int;
    if (args[ARG_polarity].u_int >= 0) self->polaridade = args[ARG_polarity].u_int;
    if (args[ARG_phase].u_int >= 0) self->fase = args[ARG_phase].u_int;
    if (args[ARG_bits].u_int >= 0) self->bits = args[ARG_bits].u_int;
    if (args[ARG_firstbit].u_int >= 0) self->primeiro = args[ARG_firstbit].u_int;
    if (args[ARG_sck].u_obj != MP_OBJ_NULL) self->sck = mp_hal_get_pin_obj(args[ARG_sck].u_obj);
    if (args[ARG_mosi].u_obj != MP_OBJ_NULL) self->mosi = mp_hal_get_pin_obj(args[ARG_mosi].u_obj);
    if (args[ARG_miso].u_obj != MP_OBJ_NULL) self->miso = mp_hal_get_pin_obj(args[ARG_miso].u_obj);
    cs_py_spi_iniciar(self->bus, self->sck, self->mosi, self->miso, self->baud, self->polaridade, self->fase, self->primeiro);
    self->ligado = true;
}

mp_obj_t machine_hw_spi_make_new(const mp_obj_type_t *type, size_t n_args, size_t n_kw, const mp_obj_t *args)
{
    MP_MACHINE_SPI_CHECK_FOR_LEGACY_SOFTSPI_CONSTRUCTION(n_args, n_kw, args);
    mp_arg_check_num(n_args, n_kw, 1, MP_OBJ_FUN_ARGS_MAX, true);
    mp_int_t bus = mp_obj_get_int(args[0]);
#if defined(ARDUINO_ARCH_RP2040)
    if (bus < 0 || bus > 1) mp_raise_msg_varg(&mp_type_ValueError, MP_ERROR_TEXT("SPI(%d) doesn't exist"), bus);
#else
    if (bus < 1 || bus > 2) mp_raise_msg_varg(&mp_type_ValueError, MP_ERROR_TEXT("SPI(%d) doesn't exist"), bus);
#endif
    machine_hw_spi_obj_t *self = &spis[bus];
    self->base.type = &machine_spi_type;
    self->bus = bus;
    /* Os pinos e os valores de fabrica do port da ESP32. */
    self->baud = 500000;
    self->polaridade = 0;
    self->fase = 0;
    self->bits = 8;
    self->primeiro = 0;
#if defined(ARDUINO_ARCH_RP2040)
    self->sck = bus == 0 ? 18 : 10;
    self->mosi = bus == 0 ? 19 : 11;
    self->miso = bus == 0 ? 16 : 8;
#else
    self->sck = bus == 1 ? 14 : 18;
    self->mosi = bus == 1 ? 13 : 23;
    self->miso = bus == 1 ? 12 : 19;
#endif
    mp_map_t kw_args;
    mp_map_init_fixed_table(&kw_args, n_kw, args + n_args);
    machine_hw_spi_init(&self->base, n_args - 1, args + 1, &kw_args);
    return MP_OBJ_FROM_PTR(self);
}

static void machine_hw_spi_deinit(mp_obj_base_t *self_in)
{
    machine_hw_spi_obj_t *self = (machine_hw_spi_obj_t *)self_in;
    if (self->ligado) cs_py_spi_parar(self->bus);
    self->ligado = false;
}

static void machine_hw_spi_transfer(mp_obj_base_t *self_in, size_t len, const uint8_t *src, uint8_t *dest)
{
    machine_hw_spi_obj_t *self = (machine_hw_spi_obj_t *)self_in;
    if (!self->ligado) mp_raise_msg(&mp_type_OSError, MP_ERROR_TEXT("transfer on deinitialized SPI"));
    cs_py_spi_trocar(self->bus, len, src, dest);
}

static const mp_machine_spi_p_t machine_hw_spi_p = {
    .init = machine_hw_spi_init,
    .deinit = machine_hw_spi_deinit,
    .transfer = machine_hw_spi_transfer,
};

MP_DEFINE_CONST_OBJ_TYPE(
    machine_spi_type,
    MP_QSTR_SPI,
    MP_TYPE_FLAG_NONE,
    make_new, machine_hw_spi_make_new,
    print, machine_hw_spi_print,
    protocol, &machine_hw_spi_p,
    locals_dict, &mp_machine_spi_locals_dict
    );
