/* cs_base.c -- o chao do runtime nativo: o salto (setjmp/longjmp), as
 * tarefas, a parada da placa e as falhas.
 *
 * Tudo o que roda aqui roda numa thread do PC que faz o papel de uma
 * tarefa do FreeRTOS. Cada uma comeca por cs_rodar_tarefa (ou, a que vem
 * do sketch interpretado, por cs_chamar), que guarda um ponto de volta:
 *
 *   - a placa parou, ou a tarefa foi apagada: a tarefa salta para la com 1
 *     e a thread termina;
 *   - o codigo deu uma falha (ponteiro invalido, divisao por zero, pilha
 *     estourada): o tratador de excecoes do Windows (cs_excecao, abaixo)
 *     desvia a thread para la com 2, e o simulador mostra o "Guru
 *     Meditation" que a ESP32 mostraria, em vez de fechar o programa.
 *
 * O salto so atravessa quadros de C: quem chamou o C# sempre ja voltou
 * antes, porque o C# nunca salta -- devolve um codigo, e o C decide. */
#include <stdint.h>
#include <stddef.h>
#include <string.h>
#include <stdio.h>
#include <stdlib.h>
#include <setjmp.h>
#include "cs_host.h"
#include "cs_interno.h"
#include "esp_heap_caps.h"

const cs_host *cs;
volatile int cs_parando;

/* Para o C# conferir que a tabela dele tem o mesmo tamanho desta. */
const int cs_tamanho_do_host = (int)(sizeof(cs_host) / sizeof(void *));
uint32_t cs_tls;
uint64_t cs_codigo_inicio, cs_codigo_fim;

/* ---------------------------------------------------------------------
 * O salto. Guarda os registradores que o x64 do Windows manda preservar:
 * rbx, rbp, rdi, rsi, r12-r15, a pilha, o endereco de volta e xmm6-xmm15.
 * Os xmm8 a xmm15 vao em bytes: o montador do TinyCC nao os conhece. */
__asm__(
".text\n"
".global __cs_setjmp\n"
"__cs_setjmp:\n"
"  mov %rbx, 0(%rcx)\n"
"  mov %rbp, 8(%rcx)\n"
"  mov %rdi, 16(%rcx)\n"
"  mov %rsi, 24(%rcx)\n"
"  mov %r12, 32(%rcx)\n"
"  mov %r13, 40(%rcx)\n"
"  mov %r14, 48(%rcx)\n"
"  mov %r15, 56(%rcx)\n"
"  lea 8(%rsp), %rdx\n"
"  mov %rdx, 64(%rcx)\n"
"  mov (%rsp), %rdx\n"
"  mov %rdx, 72(%rcx)\n"
"  movups %xmm6, 80(%rcx)\n"
"  movups %xmm7, 96(%rcx)\n"
"  .byte 0x44,0x0f,0x11,0x41,0x70\n"
"  .byte 0x44,0x0f,0x11,0x89,0x80,0x00,0x00,0x00\n"
"  .byte 0x44,0x0f,0x11,0x91,0x90,0x00,0x00,0x00\n"
"  .byte 0x44,0x0f,0x11,0x99,0xa0,0x00,0x00,0x00\n"
"  .byte 0x44,0x0f,0x11,0xa1,0xb0,0x00,0x00,0x00\n"
"  .byte 0x44,0x0f,0x11,0xa9,0xc0,0x00,0x00,0x00\n"
"  .byte 0x44,0x0f,0x11,0xb1,0xd0,0x00,0x00,0x00\n"
"  .byte 0x44,0x0f,0x11,0xb9,0xe0,0x00,0x00,0x00\n"
"  xor %eax, %eax\n"
"  ret\n"
".global __cs_longjmp\n"
"__cs_longjmp:\n"
"  mov %edx, %eax\n"
"  test %eax, %eax\n"
"  jnz 1f\n"
"  inc %eax\n"
"1:\n"
"  mov 0(%rcx), %rbx\n"
"  mov 8(%rcx), %rbp\n"
"  mov 16(%rcx), %rdi\n"
"  mov 24(%rcx), %rsi\n"
"  mov 32(%rcx), %r12\n"
"  mov 40(%rcx), %r13\n"
"  mov 48(%rcx), %r14\n"
"  mov 56(%rcx), %r15\n"
"  movups 80(%rcx), %xmm6\n"
"  movups 96(%rcx), %xmm7\n"
"  .byte 0x44,0x0f,0x10,0x41,0x70\n"
"  .byte 0x44,0x0f,0x10,0x89,0x80,0x00,0x00,0x00\n"
"  .byte 0x44,0x0f,0x10,0x91,0x90,0x00,0x00,0x00\n"
"  .byte 0x44,0x0f,0x10,0x99,0xa0,0x00,0x00,0x00\n"
"  .byte 0x44,0x0f,0x10,0xa1,0xb0,0x00,0x00,0x00\n"
"  .byte 0x44,0x0f,0x10,0xa9,0xc0,0x00,0x00,0x00\n"
"  .byte 0x44,0x0f,0x10,0xb1,0xd0,0x00,0x00,0x00\n"
"  .byte 0x44,0x0f,0x10,0xb9,0xe0,0x00,0x00,0x00\n"
"  mov 64(%rcx), %rsp\n"
"  jmp *72(%rcx)\n"
/* Chamada generica, para o sketch interpretado chamar uma funcao do C:
 * uint64_t cs_chamar_geral(fn, slots, n, &real). Cada argumento e um
 * slot de 8 bytes; os quatro primeiros vao nos registradores inteiros E
 * nos xmm -- a funcao pega o que o tipo dela pede, como no x64 do
 * Windows. Os demais vao na pilha. */
".global cs_chamar_geral\n"
"cs_chamar_geral:\n"
"  push %rbp\n"
"  mov %rsp, %rbp\n"
"  push %rbx\n"
"  push %rsi\n"
"  push %rdi\n"
"  push %r12\n"
"  mov %rcx, %r10\n"
"  mov %rdx, %rsi\n"
"  mov %r8, %rdi\n"
"  mov %r9, %r12\n"
"  mov %rdi, %rax\n"
"  cmp $4, %rax\n"
"  jge 2f\n"
"  mov $4, %rax\n"
"2:\n"
"  shl $3, %rax\n"
"  add $15, %rax\n"
"  and $-16, %rax\n"
"  sub %rax, %rsp\n"
"  mov $4, %rcx\n"
"3:\n"
"  cmp %rdi, %rcx\n"
"  jge 4f\n"
"  mov (%rsi,%rcx,8), %rax\n"
"  mov %rax, (%rsp,%rcx,8)\n"
"  inc %rcx\n"
"  jmp 3b\n"
"4:\n"
"  mov 0(%rsi), %rcx\n"
"  mov 8(%rsi), %rdx\n"
"  mov 16(%rsi), %r8\n"
"  mov 24(%rsi), %r9\n"
"  movq %rcx, %xmm0\n"
"  movq %rdx, %xmm1\n"
"  movq %r8, %xmm2\n"
"  movq %r9, %xmm3\n"
"  call *%r10\n"
"  mov %r12, %rcx\n"
"  movq %xmm0, (%rcx)\n"
"  lea -32(%rbp), %rsp\n"
"  pop %r12\n"
"  pop %rdi\n"
"  pop %rsi\n"
"  pop %rbx\n"
"  pop %rbp\n"
"  ret\n"
);

uint64_t cs_chamar_geral(void *fn, const uint64_t *slots, int64_t n, double *real);

/* ---------------------------------------------------------------------
 * Travas de giro. */
void cs_girar(cs_giro *g)
{
    int voltas = 0;
    while (cs_trocar(&g->trava, 1)) {
        cs_pausa();
        if (++voltas > 2000) {
            voltas = 0;
            if (cs_parando) cs_encerrar_tarefa();
            cs->ceder();
        }
    }
}

/* ---------------------------------------------------------------------
 * Tarefas. */
static cs_giro trava_das_tarefas;
static cs_tarefa *todas_as_tarefas;
static volatile int proximo_id;
static int erro_sem_tarefa;

cs_tarefa *cs_tarefa_atual(void)
{
    return (cs_tarefa *)cs->tls_ler(cs_tls);
}

const char *cs_nome_da_tarefa(cs_tarefa *t)
{
    return t ? t->nome : "";
}

int cs_id_da_tarefa(cs_tarefa *t)
{
    return t ? t->id : 0;
}

cs_tarefa *cs_nova_tarefa(const char *nome, void (*funcao)(void *), void *arg, int prioridade,
                          int nucleo, int pilha)
{
    cs_tarefa *t = (cs_tarefa *)cs_alocar(sizeof(cs_tarefa), MALLOC_CAP_INTERNAL);
    if (!t) return NULL;
    memset(t, 0, sizeof(*t));

    /* A pilha da tarefa sai da RAM interna, como no ESP-IDF: criar tarefa
     * demais (ou com pilha demais) acaba a memoria de verdade. A pilha que
     * a thread usa no PC e outra, maior -- o codigo de 64 bits gasta mais. */
    if (pilha > 0) {
        t->reserva = cs_alocar((size_t)pilha, MALLOC_CAP_INTERNAL);
        if (!t->reserva) {
            cs_liberar(t);
            return NULL;
        }
    }

    t->funcao = funcao;
    t->argumento = arg;
    strncpy(t->nome, nome ? nome : "", sizeof(t->nome) - 1);
    t->prioridade = prioridade;
    t->nucleo = nucleo;
    t->pilha = pilha;
    t->id = cs_somar(&proximo_id, 1) + 1;
    t->aviso = cs->sem_criar(CS_SEM_CONTAGEM, 1, 0);
    t->retomar = cs->sem_criar(CS_SEM_CONTAGEM, 1, 0);

    cs_girar(&trava_das_tarefas);
    t->proxima = todas_as_tarefas;
    todas_as_tarefas = t;
    cs_soltar(&trava_das_tarefas);
    return t;
}

int cs_numero_de_tarefas(void)
{
    int n = 0;
    cs_girar(&trava_das_tarefas);
    for (cs_tarefa *t = todas_as_tarefas; t; t = t->proxima)
        if (t->estado != 4) n++;
    cs_soltar(&trava_das_tarefas);
    return n;
}

/* Para o C#: a thread do sketch interpretado (a "loopTask" do Arduino)
 * tambem precisa de uma tarefa, para chamar o C por cs_chamar. */
cs_tarefa *cs_tarefa_do_simulador(const char *nome, int prioridade, int nucleo)
{
    return cs_nova_tarefa(nome, NULL, NULL, prioridade, nucleo, 0);
}

static void relatar_falha(cs_tarefa *t);

/* O comeco de toda thread de tarefa: o C# cria a thread e chama isto. */
void cs_rodar_tarefa(cs_tarefa *t)
{
    int r = __cs_setjmp(t->salto);
    if (r == 0) {
        t->salto_pronto = 1;
        t->funcao(t->argumento);

        /* No FreeRTOS uma tarefa nao pode simplesmente voltar: tem que se
         * apagar com vTaskDelete(NULL). O ESP-IDF aborta. */
        char detalhe[96];
        snprintf(detalhe, sizeof(detalhe), "FreeRTOS Task \"%s\" should not return, Aborting now!", t->nome);
        cs_panico("abort()", detalhe);
    }
    t->salto_pronto = 0;
    if (r == 2) relatar_falha(t);

    t->estado = 4;
    if (t->reserva) {
        cs_liberar(t->reserva);
        t->reserva = NULL;
    }
}

/* O sketch interpretado chamando uma funcao do C. Devolve 0 quando a
 * funcao voltou, 1 quando a placa parou no meio, 2 quando deu falha. */
int cs_chamar(cs_tarefa *t, void *fn, const uint64_t *slots, int n, double *real, uint64_t *inteiro)
{
    int r = __cs_setjmp(t->salto);
    if (r == 0) {
        t->salto_pronto = 1;
        *inteiro = cs_chamar_geral(fn, slots, n, real);
        t->salto_pronto = 0;
        return 0;
    }
    t->salto_pronto = 0;
    if (r == 2) relatar_falha(t);
    return r;
}

void cs_encerrar_tarefa(void)
{
    cs_tarefa *t = cs_tarefa_atual();
    if (t && t->salto_pronto) __cs_longjmp(t->salto, 1);

    /* Sem ponto de volta nao ha como sair limpo: espera o simulador. */
    for (;;) cs->dormir(1000000);
}

void cs_conferir_parada(void)
{
    if (cs_parando) cs_encerrar_tarefa();

    cs_tarefa *t = cs_tarefa_atual();
    if (!t) return;
    if (t->apagar) cs_encerrar_tarefa();

    while (t->suspender) {
        int r = cs->sem_pegar(t->retomar, 100000);
        if (r < 0 || cs_parando || t->apagar) cs_encerrar_tarefa();
        if (r > 0) t->suspender = 0;
    }
}

/* ---------------------------------------------------------------------
 * Panico: o simulador mostra a mensagem, desliga as tarefas e reinicia a
 * placa -- e o que a ESP32 faz depois do "Guru Meditation". */
void cs_panico(const char *motivo, const char *detalhe)
{
    cs->panico(motivo, detalhe);
    cs_parando = 1;
    cs_encerrar_tarefa();
}

static const char *nome_da_falha(uint64_t codigo, uint64_t tipo)
{
    switch ((uint32_t)codigo) {
    case 0xC0000005u:
        return tipo == 1 ? "StoreProhibited" : tipo == 8 ? "InstrFetchProhibited" : "LoadProhibited";
    case 0xC0000094u:
    case 0xC0000095u:
        return "IntegerDivideByZero";
    case 0xC000001Du:
    case 0xC0000096u:
        return "IllegalInstruction";
    case 0xC00000FDu:
        return "stack overflow";
    case 0x80000003u:
        return "Unhandled debug exception";
    }
    return "Unhandled exception";
}

static void relatar_falha(cs_tarefa *t)
{
    char motivo[64], detalhe[200];
    const char *nome = nome_da_falha(t->falha_codigo, t->falha_tipo);

    if ((uint32_t)t->falha_codigo == 0xC00000FDu) {
        snprintf(motivo, sizeof(motivo), "stack overflow");
        snprintf(detalhe, sizeof(detalhe), "***ERROR*** A stack overflow in task %s has been detected.", t->nome);
    } else {
        snprintf(motivo, sizeof(motivo), "%s", nome);
        if ((uint32_t)t->falha_codigo == 0xC0000005u)
            snprintf(detalhe, sizeof(detalhe), "tarefa \"%s\", endereco 0x%llx, codigo em +0x%llx", t->nome,
                     (unsigned long long)t->falha_endereco,
                     (unsigned long long)(t->falha_onde - cs_codigo_inicio));
        else
            snprintf(detalhe, sizeof(detalhe), "tarefa \"%s\", codigo em +0x%llx", t->nome,
                     (unsigned long long)(t->falha_onde - cs_codigo_inicio));
    }
    cs->panico(motivo, detalhe);
    cs_parando = 1;
}

/* O tratador de excecoes do Windows (AddVectoredExceptionHandler). Roda na
 * thread que falhou, antes de tudo; so mexe na falha que aconteceu dentro
 * do codigo compilado pelo TinyCC -- o resto segue para o .NET. Nao chama o
 * C#: so le memoria, marca a tarefa e aponta a thread para o salto de
 * volta, com a pilha ja restaurada (a pilha estourada nao serve para nada). */
long cs_excecao(void *ponteiros)
{
    uint8_t **p = (uint8_t **)ponteiros;
    uint8_t *registro = p[0];
    uint8_t *contexto = p[1];
    uint32_t codigo = *(uint32_t *)registro;
    uint64_t rip = *(uint64_t *)(contexto + 0xF8);

    switch (codigo) {
    case 0xC0000005u: case 0xC0000094u: case 0xC0000095u: case 0xC000001Du:
    case 0xC0000096u: case 0xC00000FDu: case 0x80000003u:
        break;
    default:
        return 0;
    }
    if (rip < cs_codigo_inicio || rip >= cs_codigo_fim) return 0;

    cs_tarefa *t = (cs_tarefa *)cs->tls_ler(cs_tls);
    if (!t || !t->salto_pronto) return 0;

    uint64_t *info = (uint64_t *)(registro + 32);
    t->falha_codigo = codigo;
    t->falha_tipo = codigo == 0xC0000005u ? info[0] : 0;
    t->falha_endereco = codigo == 0xC0000005u ? info[1] : 0;
    t->falha_onde = rip;

    *(uint64_t *)(contexto + 0x98) = (t->salto[8] - 64) & ~15ULL;     /* Rsp */
    *(uint64_t *)(contexto + 0x80) = (uint64_t)t->salto;              /* Rcx */
    *(uint64_t *)(contexto + 0x88) = 2;                               /* Rdx */
    *(uint64_t *)(contexto + 0xF8) = (uint64_t)&__cs_longjmp;         /* Rip */
    return -1;
}

/* ---------------------------------------------------------------------
 * errno, assert, abort, exit. */
int *__cs_errno(void)
{
    cs_tarefa *t = cs_tarefa_atual();
    return t ? &t->erro_c : &erro_sem_tarefa;
}

void __cs_assert(const char *expressao, const char *arquivo, int linha, const char *funcao)
{
    char detalhe[256];
    const char *curto = strrchr(arquivo, '/');
    if (!curto) curto = strrchr(arquivo, '\\');
    snprintf(detalhe, sizeof(detalhe), "assert failed: %s %s:%d (%s)", funcao ? funcao : "?",
             curto ? curto + 1 : arquivo, linha, expressao);
    cs_panico("abort()", detalhe);
}

void abort(void)
{
    cs_panico("abort()", "abort() was called");
}

/* No ESP-IDF o exit() nao volta para lugar nenhum: aborta. */
void exit(int codigo)
{
    char detalhe[64];
    snprintf(detalhe, sizeof(detalhe), "exit(%d) was called", codigo);
    cs_panico("abort()", detalhe);
}

void _exit(int codigo)
{
    exit(codigo);
}

/* Registra e nunca chama: na ESP32 o programa nao termina. */
int atexit(void (*funcao)(void))
{
    (void)funcao;
    return 0;
}

/* ---------------------------------------------------------------------
 * O comeco de tudo: o C# chama isto uma vez, antes de qualquer tarefa. */
int cs_iniciar(const cs_host *host, uint32_t tls, void *interna, size_t tam_interna, size_t interna_placa,
               void *psram, size_t tam_psram, size_t psram_placa, uint64_t inicio, uint64_t fim)
{
    cs = host;
    cs_tls = tls;
    cs_codigo_inicio = inicio;
    cs_codigo_fim = fim;
    cs_parando = 0;
    cs_memoria_iniciar(interna, tam_interna, interna_placa, psram, tam_psram, psram_placa);
    cs_arquivos_iniciar();
    return 0;
}

/* ---------------------------------------------------------------------
 * O que o GCC tem embutido e o TinyCC nao. */
uint16_t __builtin_bswap16(uint16_t x)
{
    return (uint16_t)((x >> 8) | (x << 8));
}

uint32_t __builtin_bswap32(uint32_t x)
{
    return ((x & 0xffu) << 24) | ((x & 0xff00u) << 8) | ((x >> 8) & 0xff00u) | (x >> 24);
}

uint64_t __builtin_bswap64(uint64_t x)
{
    return ((uint64_t)__builtin_bswap32((uint32_t)x) << 32) | __builtin_bswap32((uint32_t)(x >> 32));
}

int __builtin_clz(unsigned int x)
{
    int n = 0;
    if (x == 0) return 32;
    while (!(x & 0x80000000u)) { x <<= 1; n++; }
    return n;
}

int __builtin_ctz(unsigned int x)
{
    int n = 0;
    if (x == 0) return 32;
    while (!(x & 1u)) { x >>= 1; n++; }
    return n;
}

int __builtin_clzl(unsigned long x) { return __builtin_clz((unsigned int)x); }
int __builtin_ctzl(unsigned long x) { return __builtin_ctz((unsigned int)x); }

int __builtin_clzll(unsigned long long x)
{
    if (x >> 32) return __builtin_clz((unsigned int)(x >> 32));
    return 32 + __builtin_clz((unsigned int)x);
}

int __builtin_ctzll(unsigned long long x)
{
    if ((unsigned int)x) return __builtin_ctz((unsigned int)x);
    return 32 + __builtin_ctz((unsigned int)(x >> 32));
}

int __builtin_popcount(unsigned int x)
{
    int n = 0;
    while (x) { x &= x - 1; n++; }
    return n;
}

int __builtin_popcountl(unsigned long x) { return __builtin_popcount((unsigned int)x); }

int __builtin_popcountll(unsigned long long x)
{
    return __builtin_popcount((unsigned int)x) + __builtin_popcount((unsigned int)(x >> 32));
}

int __builtin_ffs(int x)
{
    return x ? __builtin_ctz((unsigned int)x) + 1 : 0;
}

int ffs(int x)
{
    return __builtin_ffs(x);
}

int __builtin_parity(unsigned int x)
{
    return __builtin_popcount(x) & 1;
}

void __builtin_unreachable(void)
{
    cs_panico("abort()", "__builtin_unreachable() was reached");
}

void __builtin_trap(void)
{
    cs_panico("IllegalInstruction", "__builtin_trap()");
}
