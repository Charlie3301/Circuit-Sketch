/* cs_py_main.c -- liga o MicroPython: a memoria, o coletor de lixo, o programa e o REPL no monitor. */
#include <stdio.h>
#include <string.h>

#include "py/builtin.h"
#include "py/compile.h"
#include "py/gc.h"
#include "py/lexer.h"
#include "py/mperrno.h"
#include "py/mphal.h"
#include "py/mpstate.h"
#include "py/nlr.h"
#include "py/parse.h"
#include "py/repl.h"
#include "py/runtime.h"
#include "py/stackctrl.h"
#include "extmod/vfs.h"
#include "shared/readline/readline.h"
#include "shared/runtime/pyexec.h"
#include "port/cs_py_ponte.h"

/* A memoria do Python que a ESP32 tem livre logo depois de ligar. */
#define TAMANHO_DA_MEMORIA (160 * 1024)
static char memoria[TAMANHO_DA_MEMORIA];
static char *topoDaPilha;

/* A thread do sketch tem 1 MB de pilha; a recursao para com o RuntimeError do MicroPython bem antes. */
#define LIMITE_DA_PILHA (256 * 1024)

const char cs_texto_de_ajuda[] =
    "Welcome to MicroPython on the ESP32!\n\n"
    "For online docs please visit http://docs.micropython.org/\n\n"
    "Control commands:\n"
    "  CTRL-A        -- on a blank line, enter raw REPL mode\n"
    "  CTRL-B        -- on a blank line, enter normal REPL mode\n"
    "  CTRL-C        -- interrupt a running program\n"
    "  CTRL-D        -- on a blank line, do a soft reset of the board\n"
    "  CTRL-E        -- on a blank line, enter paste mode\n\n"
    "For further help on a specific object, type help(obj)\n"
    "For a list of available modules, type help('modules')\n";

mp_uint_t mp_hal_stdout_tx_strn(const char *str, size_t len)
{
    cs_py_serial_escrever(str, len);
    return len;
}

int mp_hal_stdin_rx_chr(void)
{
    for (;;) {
        int c = cs_py_serial_ler();
        if (c >= 0) return c;
        mp_event_wait_ms(1);
    }
}

uintptr_t mp_hal_stdio_poll(uintptr_t poll_flags)
{
    return poll_flags & 4 /* MP_STREAM_POLL_WR */;
}

#if MICROPY_PY_BLUETOOTH
void cs_py_ble_colher(void);
#else
#define cs_py_ble_colher()
#endif

void mp_hal_delay_ms(mp_uint_t ms)
{
    mp_uint_t inicio = mp_hal_ticks_ms();
    for (;;) {
        /* O que chegou pelo radio BLE: o irq do programa roda aqui, na espera. */
        cs_py_ble_colher();
        mp_handle_pending(true);
        mp_uint_t passou = mp_hal_ticks_ms() - inicio;
        if (passou >= ms) return;
        mp_uint_t resta = ms - passou;
        /* Em fatias: a interrupcao agendada roda antes do fim da espera. Quem dorme solta a
         * trava: as outras threads do Python rodam enquanto isso. */
        MP_THREAD_GIL_EXIT();
        cs_py_esperar_ms(resta > 10 ? 10 : resta);
        MP_THREAD_GIL_ENTER();
    }
}

void mp_hal_delay_us(mp_uint_t us)
{
    if (us >= 1000) mp_hal_delay_ms(us / 1000);
    cs_py_esperar_us(us % 1000);
}

void cs_py_aguardar(int ms)
{
    cs_py_ble_colher();
    MP_THREAD_GIL_EXIT();
    cs_py_esperar_ms(ms < 0 || ms > 1 ? 1 : (uint32_t)ms);
    MP_THREAD_GIL_ENTER();
}

uint64_t mp_hal_time_ns(void) { return (uint64_t)cs_py_epoca_us() * 1000; }

/* O coletor olha a pilha inteira: os registradores salvos ficam acima deste quadro. */
__attribute__((noinline)) static void coletarDaPilha(void)
{
    volatile uintptr_t marca = 0;
    uintptr_t base = (uintptr_t)&marca & ~(uintptr_t)7;
    /* Cada thread do Python tem a sua pilha: o topo e o da que esta coletando. */
    uintptr_t topo = (uintptr_t)topoDaPilha;
    #if MICROPY_PY_THREAD
    if (mp_thread_topo_desta()) topo = (uintptr_t)mp_thread_topo_desta();
    #endif
    gc_collect_root((void **)base, (topo - base) / sizeof(void *));
}

void gc_collect(void)
{
    gc_collect_start();
    __builtin_unwind_init();
    coletarDaPilha();
    #if MICROPY_PY_THREAD
    mp_thread_gc_others();
    #endif
    gc_collect_end();
}

void nlr_jump_fail(void *val)
{
    (void)val;
    printf("FATAL: uncaught NLR %p\n", val);
    abort();
}

void MP_NORETURN __fatal_error(const char *msg)
{
    printf("%s\n", msg);
    abort();
    for (;;) {
    }
}

/* A linha mais funda que e do programa: e nela que o editor marca o erro. */
static int linhaDoErro(mp_obj_t excecao, const char **arquivo)
{
    size_t n = 0, *valores = NULL;
    mp_obj_exception_get_traceback(excecao, &n, &valores);
    int melhor = 0;
    *arquivo = "main.py";
    for (size_t i = 0; i + 2 < n; i += 3) {
        const char *nome = qstr_str((qstr)valores[i]);
        if (nome[0] == '.' && nome[1] == 'f') continue;      /* as bibliotecas congeladas: ".frozen/x.py" */
        *arquivo = nome;
        melhor = (int)valores[i + 1];
        break;
    }
    return melhor;
}

static void contarErro(mp_obj_t excecao)
{
    mp_obj_print_exception(&mp_plat_print, excecao);
    vstr_t mensagem;
    vstr_init(&mensagem, 64);
    mp_print_t saida;
    vstr_init_print(&mensagem, 64, &saida);
    mp_obj_print_helper(&saida, excecao, PRINT_STR);
    const char *arquivo = NULL;
    int linha = linhaDoErro(excecao, &arquivo);
    cs_py_erro_do_programa(qstr_str(mp_obj_get_type(excecao)->name), vstr_null_terminated_str(&mensagem), arquivo, linha);
}

/* Roda um .py que veio como texto, com o nome dele nas mensagens. Devolve false se escapou um erro. */
static bool rodarTexto(const char *nome, const char *texto)
{
    nlr_buf_t nlr;
    if (nlr_push(&nlr) == 0) {
        qstr origem = qstr_from_str(nome);
        mp_lexer_t *lex = mp_lexer_new_from_str_len(origem, texto, strlen(texto), 0);
        mp_parse_tree_t arvore = mp_parse(lex, MP_PARSE_FILE_INPUT);
        mp_obj_t modulo = mp_compile(&arvore, origem, false);
        mp_call_function_0(modulo);
        mp_handle_pending(true);
        nlr_pop();
        return true;
    }
    mp_obj_t excecao = MP_OBJ_FROM_PTR(nlr.ret_val);
    if (mp_obj_is_subclass_fast(MP_OBJ_FROM_PTR(mp_obj_get_type(excecao)), MP_OBJ_FROM_PTR(&mp_type_SystemExit))) return true;
    contarErro(excecao);
    return false;
}

/* Os .py das abas vao para a flash, como o Thonny faria: o import e o open() os acham la. */
static void gravarAbas(void)
{
    for (int i = 0; i < cs_py_quantas_abas; i++) {
        char caminho[96];
        snprintf(caminho, sizeof(caminho), "/%s", cs_py_abas[i].nome);
        void *a = cs_py_arquivo_abrir(caminho, "w");
        if (!a) continue;
        cs_py_arquivo_escrever(a, (const uint8_t *)cs_py_abas[i].texto, strlen(cs_py_abas[i].texto));
        cs_py_arquivo_fechar(a);
    }
}

extern void cs_vfs_montar(void);

void cs_py_rodar(void)
{
    char aqui;
    topoDaPilha = &aqui + 256;
    mp_stack_set_top(topoDaPilha);
    mp_stack_set_limit(LIMITE_DA_PILHA);
    #if MICROPY_PY_THREAD
    mp_thread_init(topoDaPilha);
    #endif
    gc_init(memoria, memoria + sizeof(memoria));
    mp_init();
    mp_obj_list_append(mp_sys_path, MP_OBJ_NEW_QSTR(MP_QSTR_));
    mp_obj_list_append(mp_sys_path, MP_OBJ_NEW_QSTR(MP_QSTR__slash_lib));
    mp_obj_list_append(mp_sys_path, MP_OBJ_NEW_QSTR(MP_QSTR__dot_frozen));
    readline_init0();
    cs_vfs_montar();
    gravarAbas();

    bool semErro = cs_py_quantas_abas == 0 || rodarTexto(cs_py_abas[0].nome, cs_py_abas[0].texto);
    if (!semErro) return;

    /* O programa acabou: a placa fica no REPL, que so aparece quando alguem escreve no monitor. */
    for (;;) {
        int c = cs_py_serial_ler();
        if (c >= 0) {
            if (c != '\r' && c != '\n') ringbuf_devolver_para_o_repl:;
            pyexec_friendly_repl();
            return;
        }
        mp_event_wait_ms(5);
    }
}
