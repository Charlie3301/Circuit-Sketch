/* cs_ponte.cpp -- a entrada e a saida do sketch compilado.
 *
 * O simulador entra no C++ por cs_entrar (o setup, o loop, uma rotina de
 * interrupcao, os construtores das globais). Cada entrada guarda um ponto
 * de volta (o salto) e sai por ele quando:
 *
 *   - o simulador devolveu CS_SAIR: a placa parou, reiniciou ou o core deu
 *     erro (o C# guardou o motivo e o relanca do lado de la);
 *   - o codigo falhou (ponteiro invalido, divisao por zero, pilha
 *     estourada): o tratador de excecoes do Windows (cs_excecao) desvia a
 *     thread para o salto, em vez de derrubar o programa;
 *   - a placa parou com o sketch num laco que nao chama nada: o vigia
 *     suspende a thread e a aponta para o salto (como um depurador faria).
 *
 * O salto so atravessa quadros de C: o C# nunca salta, devolve um codigo, e
 * quem sai e o C. */
/* O rpcndr.h do Windows tem o seu "boolean" (unsigned char); o do Arduino e bool. */
#define boolean cs_boolean_do_windows
#include <windows.h>
#undef boolean
#include <exception>
#include <new>
#include <stdint.h>
#include <string.h>
#include <stdio.h>

#include "Arduino.h"
#include "cs_objeto.h"

const cs_ponte *cs_p;

/* ---------------------------------------------------------------------
 * O salto: guarda os registradores que o x64 do Windows manda preservar
 * (rbx, rbp, rdi, rsi, r12-r15, a pilha, o endereco de volta, xmm6-xmm15),
 * sem o desenrolar do longjmp da libc -- que passaria pelos quadros do .NET. */
extern "C" int __cs_setjmp(uint64_t *buf);
extern "C" void __cs_longjmp(uint64_t *buf, int valor);

extern "C" char __cs_longjmp_fim[];

/* O codigo do salto, para o simulador copiar para a memoria dele: com a
 * pilha-sombra do processador (CET) ligada, o Windows so deixa desviar uma
 * thread para um endereco registrado, e so registra memoria alocada em
 * tempo de execucao -- nao a de uma DLL. */
extern "C" __declspec(dllexport) void *cs_codigo_do_salto(int *n)
{
    *n = (int)(__cs_longjmp_fim - (char *)&__cs_longjmp);
    return (void *)&__cs_longjmp;
}

/* A copia registrada, para onde o tratador de falhas desvia a thread. */
static void (*saltoUsado)(uint64_t *, int) = __cs_longjmp;

extern "C" __declspec(dllexport) void cs_usar_salto(void *copia)
{
    if (copia) saltoUsado = (void (*)(uint64_t *, int))copia;
}
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
    "  movups %xmm8, 112(%rcx)\n"
    "  movups %xmm9, 128(%rcx)\n"
    "  movups %xmm10, 144(%rcx)\n"
    "  movups %xmm11, 160(%rcx)\n"
    "  movups %xmm12, 176(%rcx)\n"
    "  movups %xmm13, 192(%rcx)\n"
    "  movups %xmm14, 208(%rcx)\n"
    "  movups %xmm15, 224(%rcx)\n"
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
    "  movups 112(%rcx), %xmm8\n"
    "  movups 128(%rcx), %xmm9\n"
    "  movups 144(%rcx), %xmm10\n"
    "  movups 160(%rcx), %xmm11\n"
    "  movups 176(%rcx), %xmm12\n"
    "  movups 192(%rcx), %xmm13\n"
    "  movups 208(%rcx), %xmm14\n"
    "  movups 224(%rcx), %xmm15\n"
    "  mov 64(%rcx), %rsp\n"
    "  jmp *72(%rcx)\n"
    ".global __cs_longjmp_fim\n"
    "__cs_longjmp_fim:\n"
    "  int3\n");

/* Uma entrada no C, a mais interna de cada thread por cima das outras (a
 * interrupcao que chega dentro de um delay entra por cima do loop). */
struct Entrada {
    alignas(16) uint64_t salto[32];
    Entrada *anterior;
};

/* A falha que tirou a thread do C. */
struct Falha {
    uint32_t codigo;
    uint64_t tipo, endereco, onde;
    char texto[256];
};

static thread_local Entrada *topo;
static thread_local Falha falha;
/* 1 enquanto a thread roda codigo do sketch; 0 dentro do simulador (o C#):
 * a falha que acontece la e do .NET, e nao e daqui. */
static thread_local volatile int noSketch;

/* O topo de cada thread, para o vigia (que roda em outra thread) achar. */
struct Vista {
    volatile uint32_t tid;
    Entrada *volatile topo;
    volatile int *noSketch;
};
static Vista vistas[64];

/* Sem trava: o vigia suspende a thread em qualquer ponto do modulo, e uma
 * trava presa por ela travaria a interrupcao que viesse em seguida. Cada
 * thread so mexe na sua vaga, tomada com um compare-and-swap. */
static void publicar(void)
{
    uint32_t eu = GetCurrentThreadId();
    Vista *minha = nullptr;
    for (auto &v : vistas)
        if (v.tid == eu) { minha = &v; break; }
    if (!minha && topo)
        for (auto &v : vistas)
            if (v.tid == 0 && InterlockedCompareExchange((volatile LONG *)&v.tid, (LONG)eu, 0) == 0) { minha = &v; break; }
    if (!minha) return;
    minha->noSketch = &noSketch;
    minha->topo = topo;
    if (!topo) minha->tid = 0;
}

extern "C" IMAGE_DOS_HEADER __ImageBase;
static uint64_t inicioDoModulo, fimDoModulo;

static void medirModulo(void)
{
    auto *dos = &__ImageBase;
    auto *nt = (IMAGE_NT_HEADERS64 *)((uint8_t *)dos + dos->e_lfanew);
    inicioDoModulo = (uint64_t)dos;
    fimDoModulo = inicioDoModulo + nt->OptionalHeader.SizeOfImage;
}

/* ---------------------------------------------------------------------
 * As falhas. Roda na thread que falhou, antes do .NET; so mexe se ela
 * estava rodando o sketch. Nao chama o C#: marca a falha e aponta a thread
 * para o salto, com a pilha ja restaurada (a estourada nao serve). */
extern "C" long cs_dep_excecao(EXCEPTION_POINTERS *p);
extern "C" void cs_dep_guardar_contexto(void);
extern "C" volatile int cs_dep_ativo;

extern "C" __declspec(dllexport) long cs_excecao(EXCEPTION_POINTERS *p)
{
    uint32_t codigo = p->ExceptionRecord->ExceptionCode;
    /* O int3 e o passo unico do depurador (cs_depurador.cpp) vem primeiro. */
    if (codigo == EXCEPTION_BREAKPOINT || codigo == EXCEPTION_SINGLE_STEP)
        if (long r = cs_dep_excecao(p)) return r;
    switch (codigo) {
    case EXCEPTION_ACCESS_VIOLATION:
    case EXCEPTION_INT_DIVIDE_BY_ZERO:
    case EXCEPTION_INT_OVERFLOW:
    case EXCEPTION_ILLEGAL_INSTRUCTION:
    case EXCEPTION_PRIV_INSTRUCTION:
    case EXCEPTION_STACK_OVERFLOW:
    case EXCEPTION_ARRAY_BOUNDS_EXCEEDED:
    case EXCEPTION_DATATYPE_MISALIGNMENT:
    case EXCEPTION_IN_PAGE_ERROR:
    case EXCEPTION_BREAKPOINT:          /* a armadilha que o clang poe no comportamento indefinido */
        break;
    default:
        return EXCEPTION_CONTINUE_SEARCH;
    }
    Entrada *e = topo;
    if (!e || !noSketch) return EXCEPTION_CONTINUE_SEARCH;

    falha.codigo = codigo;
    falha.tipo = codigo == EXCEPTION_ACCESS_VIOLATION ? p->ExceptionRecord->ExceptionInformation[0] : 0;
    falha.endereco = codigo == EXCEPTION_ACCESS_VIOLATION ? p->ExceptionRecord->ExceptionInformation[1] : 0;
    falha.onde = p->ContextRecord->Rip;
    falha.texto[0] = 0;

    p->ContextRecord->Rsp = (e->salto[8] - 64) & ~15ULL;
    p->ContextRecord->Rcx = (uint64_t)e->salto;
    p->ContextRecord->Rdx = CS_FALHA;
    p->ContextRecord->Rip = (uint64_t)saltoUsado;
    return EXCEPTION_CONTINUE_EXECUTION;
}

/* A falha da ultima saida desta thread, em texto, para o simulador mostrar.
 * Devolve o codigo do Windows (0 se nao houve falha). */
extern "C" __declspec(dllexport) uint32_t cs_descrever_falha(char *buf, int cap)
{
    if (cap <= 0) return falha.codigo;
    if (falha.texto[0]) {
        snprintf(buf, (size_t)cap, "%s", falha.texto);
        return falha.codigo;
    }
    const char *nome = "excecao";
    switch (falha.codigo) {
    case EXCEPTION_ACCESS_VIOLATION:
        nome = falha.tipo == 1 ? "escrita num endereco invalido" : falha.tipo == 8 ? "codigo num endereco invalido"
                                                                                    : "leitura num endereco invalido";
        break;
    case EXCEPTION_INT_DIVIDE_BY_ZERO: nome = "divisao inteira por zero"; break;
    case EXCEPTION_INT_OVERFLOW: nome = "estouro na divisao inteira"; break;
    case EXCEPTION_ILLEGAL_INSTRUCTION: nome = "instrucao invalida"; break;
    case EXCEPTION_PRIV_INSTRUCTION: nome = "instrucao proibida"; break;
    case EXCEPTION_STACK_OVERFLOW: nome = "a pilha estourou (recursao sem fim?)"; break;
    case EXCEPTION_ARRAY_BOUNDS_EXCEEDED: nome = "indice fora do vetor"; break;
    case EXCEPTION_DATATYPE_MISALIGNMENT: nome = "acesso desalinhado"; break;
    case EXCEPTION_BREAKPOINT: nome = "o codigo chegou num ponto impossivel (comportamento indefinido do C++: ponteiro nulo, funcao sem return...)"; break;
    }
    /* A faixa dos perifericos da ESP32: e uma biblioteca falando direto com o chip, nao um ponteiro quebrado. */
    if (falha.codigo == EXCEPTION_ACCESS_VIOLATION && falha.endereco >= 0x3FF00000u && falha.endereco < 0x60100000u)
        snprintf(buf, (size_t)cap, "o codigo mexeu direto num registrador do chip (0x%llx), sem passar pelas funcoes do Arduino: o simulador nao tem esse registrador", (unsigned long long)falha.endereco);
    else if (falha.codigo == EXCEPTION_ACCESS_VIOLATION)
        snprintf(buf, (size_t)cap, "%s (0x%llx)", nome, (unsigned long long)falha.endereco);
    else
        snprintf(buf, (size_t)cap, "%s", nome);
    return falha.codigo;
}

/* Uma secao da DLL pelo nome (".rtcdat", a memoria RTC): o endereco e o
 * tamanho, para o simulador guardar e devolver os bytes no sono profundo. */
extern "C" __declspec(dllexport) void *cs_secao(const char *nome, uint32_t *tamanho)
{
    auto *dos = &__ImageBase;
    auto *nt = (IMAGE_NT_HEADERS64 *)((uint8_t *)dos + dos->e_lfanew);
    IMAGE_SECTION_HEADER *s = IMAGE_FIRST_SECTION(nt);
    for (int i = 0; i < nt->FileHeader.NumberOfSections; i++, s++) {
        char n[9] = {0};
        memcpy(n, s->Name, 8);
        if (strcmp(n, nome) == 0) {
            *tamanho = s->Misc.VirtualSize;
            return (uint8_t *)dos + s->VirtualAddress;
        }
    }
    *tamanho = 0;
    return nullptr;
}

/* Depois de uma pilha estourada, a pagina de guarda tem que voltar: senao o
 * proximo estouro derrubaria o programa. */
extern "C" __declspec(dllexport) void cs_refazer_guarda(void)
{
    _resetstkoflw();
}

/* Para o vigia: a thread "tid" esta rodando o sketch? E o salto dela. */
extern "C" __declspec(dllexport) void *cs_salto_da_thread(uint32_t tid, int *rodando)
{
    void *r = nullptr;
    *rodando = 0;
    for (auto &v : vistas) {
        Entrada *e = v.topo;
        if (v.tid == tid && e) {
            r = e->salto;
            *rodando = v.noSketch ? *v.noSketch : 0;
            break;
        }
    }
    return r;
}

/* O depurador parado numa linha roda o simulador (o C#) nesta thread: o
 * vigia nao pode tomar ela como "rodando o sketch". Devolve o valor de antes. */
extern "C" int cs_trocar_no_sketch(int v)
{
    int antes = noSketch;
    noSketch = v;
    return antes;
}

/* ---------------------------------------------------------------------
 * Sair. */
extern "C" void cs_sair_com(int codigo)
{
    Entrada *e = topo;
    if (e) __cs_longjmp(e->salto, codigo);
}

/* ---------------------------------------------------------------------
 * As rotinas que o simulador chama de volta: a de interrupcao e os callbacks
 * das bibliotecas (o do MQTT recebe o topico e a mensagem). Cada uma vira um
 * nome ("__nativa_3"); o "chamador" desempacota os argumentos nos tipos da
 * funcao (cs_objeto.h). */
struct Rotina {
    void *fn;
    cs_chamador_t chamador;
    void *arg;
};
static Rotina rotinas[256];
static SRWLOCK travaDasRotinas = SRWLOCK_INIT;

extern "C" void cs_rtos_entrar_critico(void);
extern "C" void cs_rtos_sair_critico(void);

extern "C" int cs_registrar_callback(void *fn, cs_chamador_t chamador, void *arg)
{
    /* A interrupcao espera: a trava presa pela thread suspensa a travaria. */
    cs_rtos_entrar_critico();
    AcquireSRWLockExclusive(&travaDasRotinas);
    int achou = -1;
    for (int i = 0; i < 256 && achou < 0; i++)
        if (rotinas[i].fn == fn && rotinas[i].chamador == chamador && rotinas[i].arg == arg) achou = i;
    for (int i = 0; i < 256 && achou < 0; i++)
        if (!rotinas[i].fn) {
            rotinas[i] = {fn, chamador, arg};
            achou = i;
        }
    ReleaseSRWLockExclusive(&travaDasRotinas);
    cs_rtos_sair_critico();
    return achou;
}

static void semArgumentos(void *fn, void *, int, const cs_val *) { ((void (*)(void))fn)(); }
static void comArgumento(void *fn, void *arg, int, const cs_val *) { ((void (*)(void *))fn)(arg); }

int cs_registrar_rotina(void (*f)(void), void (*fa)(void *), void *arg)
{
    if (f) return cs_registrar_callback((void *)f, semArgumentos, nullptr);
    return cs_registrar_callback((void *)fa, comArgumento, arg);
}

/* ---------------------------------------------------------------------
 * A chamada ao simulador. */
static thread_local char textoDeTrabalho[96];

extern "C" cs_val cs_t(const char *s)
{
    cs_val r = {CS_V_TEXTO, s ? (int32_t)strlen(s) : 0, 0, 0, s ? s : ""};
    return r;
}

extern "C" cs_val cs_tn(const char *s, int n)
{
    cs_val r = {CS_V_TEXTO, n, 0, 0, s};
    return r;
}

extern "C" cs_val cs_nome(const char *s)
{
    cs_val r = {CS_V_NOME, (int32_t)strlen(s), 0, 0, s};
    return r;
}

extern "C" volatile int cs_rtos_ativo;
extern "C" void cs_rtos_ponto(void);
extern "C" void cs_atender_timers(void);

extern "C" int cs_chamar(const char *alvo, const char *nome, int n, const cs_val *args, cs_val *volta)
{
    cs_val ignorada;
    if (!volta) volta = &ignorada;
    volta->tipo = CS_V_NADA;
    if (!cs_p) return CS_OK;
    /* O tick do FreeRTOS: a vez pode passar para outra tarefa aqui. */
    if (cs_rtos_ativo && noSketch) cs_rtos_ponto();
    /* O depurador: o Pausar no meio de um delay acha a linha daqui. */
    if (cs_dep_ativo && noSketch) cs_dep_guardar_contexto();
    int antes = noSketch;
    noSketch = 0;
    int r = cs_p->chamar(cs_p->ctx, alvo, nome, n, args, volta);
    noSketch = antes;
    if (r != CS_OK) cs_sair_com(r);
    return r;
}

int64_t cs_relogio(void)
{
    if (!cs_p) return 0;
    int antes = noSketch;
    noSketch = 0;
    int64_t us = cs_p->micros(cs_p->ctx);
    noSketch = antes;
    return us;
}

extern "C" int64_t cs_int(const char *alvo, const char *nome, int n, const cs_val *args)
{
    cs_val v;
    cs_chamar(alvo, nome, n, args, &v);
    switch (v.tipo) {
    case CS_V_REAL: return (int64_t)v.d;
    case CS_V_TEXTO: return v.s ? atoll(v.s) : 0;
    default: return v.i;
    }
}

extern "C" double cs_real(const char *alvo, const char *nome, int n, const cs_val *args)
{
    cs_val v;
    cs_chamar(alvo, nome, n, args, &v);
    switch (v.tipo) {
    case CS_V_REAL: return v.d;
    case CS_V_TEXTO: return v.s ? atof(v.s) : 0;
    default: return (double)v.i;
    }
}

extern "C" void cs_nada(const char *alvo, const char *nome, int n, const cs_val *args)
{
    cs_chamar(alvo, nome, n, args, nullptr);
}

/* O texto que voltou, numa String (vale o tamanho, nao o zero). */
String cs_texto(const char *alvo, const char *nome, int n, const cs_val *args)
{
    cs_val v;
    cs_chamar(alvo, nome, n, args, &v);
    if (v.tipo == CS_V_TEXTO && v.s) return String(v.s, (unsigned int)v.n);
    if (v.tipo == CS_V_CHAR) return String((char)v.i);
    if (v.tipo == CS_V_REAL) return String(v.d);
    if (v.tipo == CS_V_NADA) return String();
    snprintf(textoDeTrabalho, sizeof(textoDeTrabalho), "%lld", (long long)v.i);
    return String(textoDeTrabalho);
}

/* ---------------------------------------------------------------------
 * As tarefas do FreeRTOS (cs_freertos.cpp): cada uma na sua thread, com a
 * sua entrada -- a falha e a saida voltam por ela, e quem conta ao
 * simulador e a thread da placa (com a falha que a tarefa guardou). */
static Falha falhaDaTarefa;

extern "C" void cs_guardar_falha(void) { falhaDaTarefa = falha; }
extern "C" void cs_trazer_falha(void) { falha = falhaDaTarefa; }

extern "C" int cs_rodar_protegido(void (*fn)(void *), void *arg)
{
    Entrada e;
    e.anterior = topo;
    int antes = noSketch;
    topo = &e;
    falha.codigo = 0;
    publicar();
    volatile int r = __cs_setjmp(e.salto);
    if (r == 0) {
        noSketch = 1;
        try {
            fn(arg);
        } catch (const std::exception &x) {
            snprintf(falha.texto, sizeof(falha.texto), "excecao do C++ sem catch numa tarefa: %s", x.what());
            falha.codigo = 0xE0000001u;
            r = CS_FALHA;
        } catch (...) {
            snprintf(falha.texto, sizeof(falha.texto), "excecao do C++ sem catch numa tarefa");
            falha.codigo = 0xE0000001u;
            r = CS_FALHA;
        }
    }
    noSketch = antes;
    topo = e.anterior;
    publicar();
    return r;
}

/* ---------------------------------------------------------------------
 * A entrada. */
extern "C" BOOL WINAPI DllMainCRTStartup(HINSTANCE, DWORD, LPVOID);
static HINSTANCE instancia;
static volatile int construido;

/* O ponto de entrada da DLL: nada no carregamento. Os construtores das
 * globais rodam depois, por cs_entrar(3), com a ponte ja ligada -- uma
 * global "int x = analogRead(A0);" precisa do simulador. */
extern "C" BOOL WINAPI cs_entrada_da_dll(HINSTANCE h, DWORD razao, LPVOID reservado)
{
    if (razao == DLL_PROCESS_ATTACH) {
        instancia = h;
        return TRUE;
    }
    /* O programa esta fechando (com a placa ainda carregada): as outras threads ja
     * morreram, e o destrutor de uma global que espera uma tarefa nao volta nunca
     * -- e nessa hora nem o Gerenciador de Tarefas mata o processo. */
    if (razao == DLL_PROCESS_DETACH && reservado) return TRUE;
    if (razao == DLL_PROCESS_DETACH && construido) {
        cs_p = nullptr;
        return DllMainCRTStartup(h, razao, reservado);
    }
    if (construido) return DllMainCRTStartup(h, razao, reservado);
    return TRUE;
}

/* O abort() do programa (o ESP_ERROR_CHECK que falha, um assert, a excecao sem catch):
 * na placa e o panico e ela reinicia. O da libc do Windows mata o processo inteiro,
 * o simulador junto, sem passar pelo filtro de excecoes. */
extern "C" void abort(void)
{
    printf("\nabort() was called at PC %p on core 0\n\nRebooting...\n", __builtin_return_address(0));
    fflush(stdout);
    cs_obj_n(nullptr, "__abort");
    snprintf(falha.texto, sizeof(falha.texto), "abort() foi chamado");
    falha.codigo = 0xE0000002u;
    cs_sair_com(CS_FALHA);
    for (;;) {
    }
}

extern "C" void __assert_func(const char *arquivo, int linha, const char *funcao, const char *expressao)
{
    printf("assert failed: %s %s:%d (%s)\n", funcao ? funcao : "", arquivo, linha, expressao);
    abort();
}
[[noreturn]] static void terminar(void)
{
    snprintf(falha.texto, sizeof(falha.texto), "terminate(): uma excecao do C++ escapou sem catch");
    falha.codigo = 0xE0000001u;
    cs_sair_com(CS_FALHA);
    for (;;) {
    }
}

extern "C" __declspec(dllexport) int cs_iniciar(const cs_ponte *ponte)
{
    if (!ponte || ponte->tamanho != (int32_t)sizeof(cs_ponte) || ponte->versao != CS_VERSAO_DA_PONTE) return -1;
    cs_p = ponte;
    medirModulo();
    return 0;
}

extern "C" __declspec(dllexport) uint64_t cs_modulo(uint64_t *fim)
{
    *fim = fimDoModulo;
    return inicioDoModulo;
}

static void chamarRotina(int i, int n, const cs_val *args)
{
    if (i < 0 || i >= 256 || !rotinas[i].fn) return;
    rotinas[i].chamador(rotinas[i].fn, rotinas[i].arg, n, args);
}

/* qual: 0 setup, 1 loop, 2 a rotina "arg" (com os n argumentos), 3 os
 * construtores, 4 as interrupcoes de timer que venceram (cs_timer.cpp). Volta 0, ou o codigo da saida (CS_SAIR, CS_FALHA). */
extern "C" __declspec(dllexport) int cs_entrar(int qual, int arg, int n, const cs_val *args)
{
    Entrada e;
    e.anterior = topo;
    int antes = noSketch;
    topo = &e;
    falha.codigo = 0;
    publicar();
    volatile int r = __cs_setjmp(e.salto);
    if (r == 0) {
        noSketch = 1;
        try {
            switch (qual) {
            case 0: setup(); break;
            case 1:
                loop();
                if (cs_rtos_ativo) cs_rtos_ponto();
                break;
            case 2: chamarRotina(arg, n, args); break;
            case 4: cs_atender_timers(); break;
            case 3:
                std::set_terminate(terminar);
                construido = 1;
                DllMainCRTStartup(instancia, DLL_PROCESS_ATTACH, nullptr);
                break;
            }
        } catch (const std::exception &x) {
            snprintf(falha.texto, sizeof(falha.texto), "excecao do C++ sem catch: %s", x.what());
            falha.codigo = 0xE0000001u;
            r = CS_FALHA;
        } catch (...) {
            snprintf(falha.texto, sizeof(falha.texto), "excecao do C++ sem catch");
            falha.codigo = 0xE0000001u;
            r = CS_FALHA;
        }
    }
    noSketch = antes;
    topo = e.anterior;
    publicar();
    return r;
}
