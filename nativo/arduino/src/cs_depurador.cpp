/* cs_depurador.cpp -- o depurador do sketch compilado, dentro do proprio
 * processo (sem gdb/lldb).
 *
 * O simulador (DepuracaoNativa.cs) le o PDB do sketch e manda para ca o
 * endereco do comeco de cada linha do sketch (os "trechos") e os das linhas
 * marcadas. Parar e um int3 no comeco da linha: o tratador de excecoes
 * (cs_excecao) desvia a thread para o trampolim, que chama o simulador fora
 * do despacho da excecao, espera o comando (continuar, passo, sair) e volta
 * ao contexto exato com o RtlRestoreContext. Para o passo e para o Pausar,
 * int3 em TODO comeco de linha do sketch: a proxima linha que rodar para, sem
 * andar instrucao por instrucao pelo core. A instrucao trocada pelo int3
 * roda com o trap flag (uma so) e o int3 volta. */
#define boolean cs_boolean_do_windows
#include <windows.h>
#undef boolean
#include <stdint.h>
#include <string.h>
#include <stdlib.h>
#include "cs_ponte.h"

extern "C" void cs_sair_com(int codigo);
extern "C" void cs_rtos_entrar_critico(void);
extern "C" void cs_rtos_sair_critico(void);
extern "C" int cs_trocar_no_sketch(int v);

/* 0 continuar, 1 passo, 2 sair. */
typedef int (*cs_dep_parou_t)(void *ctx, CONTEXT *contexto, int linha);

struct Trecho {
    uint64_t inicio;
    int32_t linha;
    int32_t marcada;
};

struct Remendo {
    uint64_t endereco;
    uint8_t antes;
};

extern "C" __declspec(dllexport) volatile int cs_dep_ativo = 0;

static Trecho *trechos;          /* ordenados pelo inicio */
static int nTrechos;
static Remendo *postos;
static int nPostos, capPostos;
static volatile int todasAsLinhas;
static SRWLOCK trava = SRWLOCK_INIT;

static cs_dep_parou_t parou;
static void *ctxDoParou;

/* A parada em andamento: o contexto de quem parou (o trampolim le). */
static CONTEXT salvo;
static int linhaSalva;
static uint64_t religar;         /* o int3 que volta depois da instrucao original */
static int linhaDaPausa = -1;
static uint64_t rspDaPausa, ripDaPausa;

static Trecho *achar(uint64_t a)
{
    int lo = 0, hi = nTrechos - 1;
    while (lo <= hi) {
        int m = (lo + hi) / 2;
        if (trechos[m].inicio == a) return &trechos[m];
        if (trechos[m].inicio < a) lo = m + 1;
        else hi = m - 1;
    }
    return nullptr;
}

static int acharPosto(uint64_t a)
{
    for (int i = 0; i < nPostos; i++)
        if (postos[i].endereco == a) return i;
    return -1;
}

static void escreverByte(uint64_t a, uint8_t b)
{
    DWORD antes;
    if (!VirtualProtect((void *)a, 1, PAGE_EXECUTE_READWRITE, &antes)) return;
    *(volatile uint8_t *)a = b;
    VirtualProtect((void *)a, 1, antes, &antes);
    FlushInstructionCache(GetCurrentProcess(), (void *)a, 1);
}

static void por(uint64_t a)
{
    if (acharPosto(a) >= 0) return;
    if (nPostos == capPostos) {
        capPostos = capPostos ? capPostos * 2 : 256;
        postos = (Remendo *)realloc(postos, sizeof(Remendo) * capPostos);
    }
    postos[nPostos].endereco = a;
    postos[nPostos].antes = *(volatile uint8_t *)a;
    nPostos++;
    escreverByte(a, 0xCC);
}

static void tirarTodos(void)
{
    for (int i = 0; i < nPostos; i++) escreverByte(postos[i].endereco, postos[i].antes);
    nPostos = 0;
}

/* Os int3 que devem estar: as linhas marcadas, ou todas (passo, pausa). Na
 * secao critica: o vigia nao suspende quem esta com a trava (a interrupcao
 * que parasse num int3 travaria nela). */
static void montar(void)
{
    cs_rtos_entrar_critico();
    AcquireSRWLockExclusive(&trava);
    tirarTodos();
    if (cs_dep_ativo)
        for (int i = 0; i < nTrechos; i++)
            if (todasAsLinhas || trechos[i].marcada) por(trechos[i].inicio);
    ReleaseSRWLockExclusive(&trava);
    cs_rtos_sair_critico();
}

static bool deveEstar(uint64_t a)
{
    Trecho *t = achar(a);
    return t && cs_dep_ativo && (todasAsLinhas || t->marcada);
}

/* ---- o que o simulador chama ---- */
extern "C" __declspec(dllexport) void cs_dep_ligar(cs_dep_parou_t cb, void *ctx)
{
    parou = cb;
    ctxDoParou = ctx;
    cs_dep_ativo = 1;
}

extern "C" __declspec(dllexport) void cs_dep_trechos(const uint64_t *inicios, const int32_t *linhas, int n)
{
    AcquireSRWLockExclusive(&trava);
    tirarTodos();
    free(trechos);
    trechos = (Trecho *)calloc(n > 0 ? n : 1, sizeof(Trecho));
    for (int i = 0; i < n; i++) trechos[i] = {inicios[i], linhas[i], 0};
    nTrechos = n;
    ReleaseSRWLockExclusive(&trava);
}

extern "C" __declspec(dllexport) void cs_dep_pausas(const int32_t *linhas, int n)
{
    AcquireSRWLockExclusive(&trava);
    for (int i = 0; i < nTrechos; i++) {
        trechos[i].marcada = 0;
        for (int k = 0; k < n; k++)
            if (trechos[i].linha == linhas[k]) trechos[i].marcada = 1;
    }
    ReleaseSRWLockExclusive(&trava);
    montar();
}

/* Pausar ja: para na proxima linha do sketch que rodar. */
extern "C" __declspec(dllexport) void cs_dep_pedir_pausa(void)
{
    linhaDaPausa = -1;
    todasAsLinhas = 1;
    montar();
}

/* O Continuar da pausa feita no meio de um delay: o pedido nao vale mais. */
extern "C" __declspec(dllexport) void cs_dep_seguir(void)
{
    todasAsLinhas = 0;
    montar();
}

extern "C" __declspec(dllexport) void cs_dep_desligar(void)
{
    cs_dep_ativo = 0;
    todasAsLinhas = 0;
    montar();
}

/* ---- o contexto na entrada da ponte: a pausa pedida no meio de um delay
 * acha a linha do sketch subindo a pilha a partir daqui ---- */
static thread_local CONTEXT contextoDaPonte;

extern "C" void cs_dep_guardar_contexto(void)
{
    if (cs_dep_ativo) RtlCaptureContext(&contextoDaPonte);
}

extern "C" __declspec(dllexport) CONTEXT *cs_dep_contexto_da_ponte(void) { return &contextoDaPonte; }

/* ---- a parada ---- */
static void trampolim(void)
{
    alignas(16) CONTEXT c = salvo;
    /* A copia nao leva a area estendida (o XSTATE): sem tirar o pedido dela,
     * o RtlRestoreContext recusa e volta. */
    c.ContextFlags = CONTEXT_CONTROL | CONTEXT_INTEGER | CONTEXT_FLOATING_POINT;
    int linha = linhaSalva;
    linhaDaPausa = linha;
    rspDaPausa = c.Rsp;
    ripDaPausa = c.Rip;
    todasAsLinhas = 0;
    montar();

    int antes = cs_trocar_no_sketch(0);
    int comando = parou ? parou(ctxDoParou, &c, linha) : 0;
    cs_trocar_no_sketch(antes);
    if (comando == 2) cs_sair_com(CS_SAIR);
    if (comando == 1) todasAsLinhas = 1;
    montar();

    /* A instrucao em que parou estava sob um int3: roda a original, uma so. */
    cs_rtos_entrar_critico();
    AcquireSRWLockExclusive(&trava);
    int k = acharPosto(c.Rip);
    if (k >= 0) {
        escreverByte(postos[k].endereco, postos[k].antes);
        postos[k] = postos[--nPostos];
        religar = c.Rip;
        c.EFlags |= 0x100;
    }
    ReleaseSRWLockExclusive(&trava);
    cs_rtos_sair_critico();
    RtlRestoreContext(&c, nullptr);
}

/* Do cs_excecao: o int3 e o passo unico deste depurador. 0 = nao e daqui. */
extern "C" long cs_dep_excecao(EXCEPTION_POINTERS *p)
{
    if (!cs_dep_ativo) return 0;
    CONTEXT *c = p->ContextRecord;
    DWORD codigo = p->ExceptionRecord->ExceptionCode;

    if (codigo == EXCEPTION_SINGLE_STEP) {
        if (!religar) return 0;
        uint64_t a = religar;
        religar = 0;
        AcquireSRWLockExclusive(&trava);
        if (deveEstar(a)) por(a);
        ReleaseSRWLockExclusive(&trava);
        c->EFlags &= ~0x100u;
        return EXCEPTION_CONTINUE_EXECUTION;
    }
    if (codigo != EXCEPTION_BREAKPOINT) return 0;

    uint64_t a = (uint64_t)p->ExceptionRecord->ExceptionAddress;
    AcquireSRWLockExclusive(&trava);
    int k = acharPosto(a);
    Trecho *t = k >= 0 ? achar(a) : nullptr;
    ReleaseSRWLockExclusive(&trava);
    if (!t) return 0;
    c->Rip = a;

    /* No passo, a mesma linha adiante no mesmo quadro nao para de novo (a
     * linha com varios pedacos); a volta de um laco para. */
    bool parar = t->marcada || todasAsLinhas;
    if (!t->marcada && t->linha == linhaDaPausa && c->Rsp == rspDaPausa && a > ripDaPausa) parar = false;
    if (!parar) {
        AcquireSRWLockExclusive(&trava);
        int j = acharPosto(a);
        if (j >= 0) {
            escreverByte(postos[j].endereco, postos[j].antes);
            postos[j] = postos[--nPostos];
        }
        ReleaseSRWLockExclusive(&trava);
        religar = a;
        c->EFlags |= 0x100;
        return EXCEPTION_CONTINUE_EXECUTION;
    }

    /* Para fora do despacho da excecao: o trampolim roda abaixo da pilha de
     * quem parou (ela fica intacta) e volta ao contexto dela. */
    salvo = *c;
    linhaSalva = t->linha;
    c->Rsp = ((c->Rsp - 512) & ~15ULL) - 8;
    c->Rip = (uint64_t)&trampolim;
    return EXCEPTION_CONTINUE_EXECUTION;
}
