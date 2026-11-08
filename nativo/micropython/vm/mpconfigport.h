/* mpconfigport.h -- o MicroPython de verdade rodando na placa do simulador. As escolhas sao as do
 * port da ESP32 (ports/esp32/mpconfigport.h), para o programa ver a mesma linguagem. */
#include <stdint.h>
#include <malloc.h>

/* A familia (ESP32, ARDUINO_ARCH_RP2040) vem na linha de comando, como a IDE passa. */
#define MICROPY_HW_BOARD_NAME "CircuitSketch"
#if defined(ARDUINO_ARCH_RP2040)
#define MICROPY_HW_MCU_NAME "RP2040"
#define MICROPY_PY_SYS_PLATFORM "rp2"
#else
#define MICROPY_HW_MCU_NAME "ESP32"
#define MICROPY_PY_SYS_PLATFORM "esp32"
#endif

#define MICROPY_CONFIG_ROM_LEVEL (MICROPY_CONFIG_ROM_LEVEL_EXTRA_FEATURES)
#define MICROPY_OBJ_REPR (MICROPY_OBJ_REPR_A)
#define MICROPY_NLR_SETJMP (1)
#define MICROPY_ALLOC_PATH_MAX (128)

/* Os textos ficam inteiros: a ROM daqui nao e pequena. */
#define MICROPY_ROM_TEXT_COMPRESSION (0)

#define MICROPY_PERSISTENT_CODE_LOAD (0)
#define MICROPY_ENABLE_COMPILER (1)
#define MICROPY_ENABLE_GC (1)
#define MICROPY_READER_VFS (1)
#define MICROPY_VFS (1)
#define MICROPY_STACK_CHECK (1)
#define MICROPY_STACK_CHECK_MARGIN (4096)
#define MICROPY_ENABLE_EMERGENCY_EXCEPTION_BUF (1)
#define MICROPY_EMERGENCY_EXCEPTION_BUF_SIZE (256)
#define MICROPY_LONGINT_IMPL (MICROPY_LONGINT_IMPL_MPZ)
#define MICROPY_ERROR_REPORTING (MICROPY_ERROR_REPORTING_NORMAL)
#define MICROPY_WARNINGS (1)
#define MICROPY_FLOAT_IMPL (MICROPY_FLOAT_IMPL_FLOAT)
#define MICROPY_STREAMS_POSIX_API (0)
#define MICROPY_USE_INTERNAL_ERRNO (1)
#define MICROPY_USE_INTERNAL_PRINTF (0)
#define MICROPY_SCHEDULER_DEPTH (8)
#define MICROPY_SCHEDULER_STATIC_NODES (1)
#define MICROPY_KBD_EXCEPTION (1)
#define MICROPY_HELPER_REPL (1)
#define MICROPY_REPL_EVENT_DRIVEN (0)
#define MICROPY_MODULE_FROZEN_STR (1)
#define MICROPY_MODULE_FROZEN_MPY (1)
#define MICROPY_MODULE_FROZEN (1)
#define MICROPY_ENABLE_SOURCE_LINE (1)
#define MICROPY_ENABLE_DOC_STRING (0)
#define MICROPY_PY_BUILTINS_HELP (1)
#define MICROPY_PY_BUILTINS_HELP_TEXT cs_texto_de_ajuda
#define MICROPY_PY_BUILTINS_HELP_MODULES (1)
#define MICROPY_ENABLE_FINALISER (1)

#define MICROPY_PY_STR_BYTES_CMP_WARN (1)
#define MICROPY_PY_ALL_INPLACE_SPECIAL_METHODS (1)
#define MICROPY_PY_IO_BUFFEREDWRITER (1)
#define MICROPY_PY_TIME_GMTIME_LOCALTIME_MKTIME (1)
#define MICROPY_PY_TIME_TIME_TIME_NS (1)
#define MICROPY_PY_TIME_INCLUDEFILE "port/modtime.c"
#define MICROPY_PY_RANDOM_SEED_INIT_FUNC (cs_py_aleatorio())
#define MICROPY_PY_OS_INCLUDEFILE "port/modos.c"
#define MICROPY_PY_OS_DUPTERM (0)
#define MICROPY_PY_OS_SYNC (1)
#define MICROPY_PY_OS_UNAME (1)
#define MICROPY_PY_OS_URANDOM (1)
#define MICROPY_PY_SYS_STDFILES (1)
#define MICROPY_PY_SYS_EXC_INFO (1)
#define MICROPY_PY_SYS_MAXSIZE (1)

/* O machine: o que a ESP32 tem. */
#define MICROPY_PY_MACHINE (1)
#define MICROPY_PY_MACHINE_INCLUDEFILE "port/modmachine.c"
#define MICROPY_PY_MACHINE_RESET (1)
#define MICROPY_PY_MACHINE_BARE_METAL_FUNCS (1)
#define MICROPY_PY_MACHINE_DISABLE_IRQ_ENABLE_IRQ (1)
#define MICROPY_PY_MACHINE_ADC (1)
#define MICROPY_PY_MACHINE_ADC_INCLUDEFILE "port/machine_adc.c"
#define MICROPY_PY_MACHINE_ADC_ATTEN_WIDTH (1)
#define MICROPY_PY_MACHINE_ADC_INIT (1)
#define MICROPY_PY_MACHINE_ADC_DEINIT (1)
#define MICROPY_PY_MACHINE_ADC_READ (1)
#define MICROPY_PY_MACHINE_ADC_READ_UV (1)
#define MICROPY_PY_MACHINE_PIN_MAKE_NEW mp_pin_make_new
#define MICROPY_PY_MACHINE_BITSTREAM (1)
#define MICROPY_PY_MACHINE_DHT_READINTO (0)
#define MICROPY_PY_MACHINE_PULSE (1)
#define MICROPY_PY_MACHINE_PWM (1)
#define MICROPY_PY_MACHINE_PWM_DUTY (1)
#define MICROPY_PY_MACHINE_PWM_INCLUDEFILE "port/machine_pwm.c"
#define MICROPY_PY_MACHINE_I2C (1)
#define MICROPY_PY_MACHINE_I2C_TRANSFER_WRITE1 (1)
#define MICROPY_PY_MACHINE_SOFTI2C (1)
#define MICROPY_PY_MACHINE_SPI (1)
#define MICROPY_PY_MACHINE_SOFTSPI (1)
#define MICROPY_PY_MACHINE_UART (1)
#define MICROPY_PY_MACHINE_UART_INCLUDEFILE "port/machine_uart.c"
#define MICROPY_PY_MACHINE_UART_SENDBREAK (1)
#define MICROPY_PY_MACHINE_WDT (1)
#define MICROPY_PY_MACHINE_WDT_INCLUDEFILE "port/machine_wdt.c"
#define MICROPY_HW_SOFTSPI_MIN_DELAY (0)
#define MICROPY_HW_SOFTSPI_MAX_BAUDRATE (40000000)
#define MICROPY_PY_ONEWIRE (1)
#if defined(ESP32)
#define MICROPY_PY_NETWORK (1)
#define MICROPY_PY_NETWORK_INCLUDEFILE "port/modnetwork.h"
#define MICROPY_PY_NETWORK_MODULE_GLOBALS_INCLUDEFILE "port/modnetwork_globals.h"
#define MICROPY_PY_NETWORK_HOSTNAME_DEFAULT "mpy-esp32"
#define MICROPY_PY_NETWORK_WLAN (1)
#define MICROPY_PY_SOCKET (1)
#define MICROPY_HW_ENABLE_MDNS_QUERIES (0)
#define MICROPY_HW_ENABLE_MDNS_RESPONDER (0)
#define MICROPY_PY_SSL (1)
#define MICROPY_SSL_MBEDTLS (1)
#define MICROPY_PY_CRYPTOLIB (1)
#else
#define MICROPY_PY_NETWORK (0)
#define MICROPY_PY_CRYPTOLIB (0)
#endif
#define MICROPY_PY_HASHLIB (1)
#define MICROPY_PY_DEFLATE (1)

/* Tipos: ponteiro de 64 bits no PC. */
#define MP_SSIZE_MAX INT64_MAX
#define UINT_FMT "%llu"
#define INT_FMT "%lld"
#define SIZE_FMT "%llu"
typedef long long mp_int_t;
typedef unsigned long long mp_uint_t;
typedef long long mp_off_t;

/* As interrupcoes do simulador rodam na thread do vigia, com a do sketch parada. */
uint32_t cs_py_atomica_comeca(void);
void cs_py_atomica_termina(uint32_t estado);
#define MICROPY_BEGIN_ATOMIC_SECTION() cs_py_atomica_comeca()
#define MICROPY_END_ATOMIC_SECTION(estado) cs_py_atomica_termina(estado)

uint32_t cs_py_aleatorio(void);
extern const char cs_texto_de_ajuda[];
void cs_py_ceder(void);

/* Quem espera (time.sleep, o input) deixa o simulador rodar. */
#define MICROPY_INTERNAL_WFE(TIMEOUT_MS) cs_py_aguardar((int)(TIMEOUT_MS))
void cs_py_aguardar(int ms);

#define MP_STATE_PORT MP_STATE_VM
#define FFCONF_H "lib/oofatfs/ffconf.h"

#define MICROPY_MAKE_POINTER_CALLABLE(p) (p)
