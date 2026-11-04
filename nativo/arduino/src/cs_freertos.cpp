/* cs_freertos.cpp -- o escalonador do FreeRTOS (ver freertos/FreeRTOS.h).
 *
 * Cada tarefa tem a sua thread do Windows, e uma so tem a vez: a que tem a
 * vez roda o C e chama o simulador; as outras esperam no evento delas. A vez
 * passa (passarVez) quando a tarefa bloqueia (fila, semaforo, delay), quando
 * acaba, e nas chamadas ao simulador (cs_rtos_ponto) quando outra de mesma
 * prioridade espera ha mais de um tick, ou uma mais alta acordou -- o tick
 * da placa de verdade. Sem ninguem pronto, quem tem a vez espera pelo
 * simulador (o delay dele) ate o prazo mais proximo.
 *
 * A ESP32 tem dois nucleos: cada nucleo escolhe a sua mais alta, e as
 * escolhidas dos dois revezam -- a tarefa do nucleo 0 nao para o loop() do
 * nucleo 1 so por ter prioridade maior.
 *
 * As interrupcoes rodam na thread do vigia (SketchNativo) com quem tem a vez
 * suspensa; dentro da secao critica (cs_critico) o vigia espera. */
#define boolean cs_boolean_do_windows
#include <windows.h>
#undef boolean
#include <vector>
#include <string.h>
#include <stdlib.h>
#include <stdint.h>
#include "Arduino.h"
#include "freertos/FreeRTOS.h"
#include "cs_interno.h"

extern "C" int cs_rodar_protegido(void (*fn)(void *), void *arg);
extern "C" void cs_guardar_falha(void);
extern "C" void cs_trazer_falha(void);
extern "C" void cs_sair_com(int codigo);

/* Para o vigia (lidos pelo SketchNativo): a thread que tem a vez, e a secao
 * critica aberta. */
extern "C" __declspec(dllexport) volatile uint32_t cs_tid_atual = 0;
extern "C" __declspec(dllexport) volatile int cs_critico = 0;
extern "C" volatile int cs_rtos_ativo = 0;

enum Estado { PRONTA, DORMINDO, BLOQUEADA, SUSPENSA, MORTA };
enum { ESPERA_NADA, ESPERA_RECEBER, ESPERA_ENVIAR, ESPERA_NOTIFICACAO, ESPERA_BITS, ESPERA_TEMPORIZADOR };

static const int64_t SEMPRE = INT64_MAX;
static const int CS_TAREFA_ACABOU = 7;

struct tskTaskControlBlock {
    HANDLE thread = nullptr;
    HANDLE vez = nullptr;
    DWORD tid = 0;
    TaskFunction_t fn = nullptr;
    void *param = nullptr;
    char nome[configMAX_TASK_NAME_LEN] = {0};
    UBaseType_t prioridade = 1;
    BaseType_t nucleo = tskNO_AFFINITY;
    uint32_t pilha = 0;
    int estado = PRONTA;
    bool principal = false;
    bool suspensaAntes = false;     /* vTaskSuspend de quem estava bloqueada */
    int64_t acorda = SEMPRE;
    void *esperando = nullptr;
    int tipoDaEspera = ESPERA_NADA;
    BaseType_t resultado = pdFALSE;
    uint32_t notificacao[3] = {0, 0, 0};
    uint8_t notificada[3] = {0, 0, 0};
    int indiceEsperado = 0;
    EventBits_t bitsQuerido = 0, bitsNoAcordar = 0;
    bool todosOsBits = false;
    int64_t naVezDesde = 0;
};

struct QueueDefinition {
    uint8_t *dados = nullptr;
    UBaseType_t tamanho = 0, item = 0, quantos = 0, inicio = 0;
    int tipo = 0;                   /* 0 fila/semaforo, 1 mutex, 2 mutex recursivo */
    TaskHandle_t dono = nullptr;
    UBaseType_t recursoes = 0;
};

struct tmrTimerControl {
    char nome[configMAX_TASK_NAME_LEN] = {0};
    TickType_t periodo = 1;
    UBaseType_t repete = 0;
    void *id = nullptr;
    TimerCallbackFunction_t cb = nullptr;
    bool ativo = false;
    bool apagado = false;
    int64_t expira = 0;
};

struct EventGroupDef_t {
    EventBits_t bits = 0;
};

/* Criados no primeiro uso, nao como globais: uma global do sketch pode chamar o
 * FreeRTOS no construtor (o semaforo do ZigbeeEP), antes dos construtores daqui. */
static std::vector<TaskHandle_t> &listaDeTarefas()
{
    static auto *v = new std::vector<TaskHandle_t>();
    return *v;
}
static std::vector<tmrTimerControl *> &listaDeTemporizadores()
{
    static auto *v = new std::vector<tmrTimerControl *>();
    return *v;
}
#define tarefas listaDeTarefas()
#define temporizadores listaDeTemporizadores()
static TaskHandle_t atual;
static TaskHandle_t principal;
static TaskHandle_t servicoDosTemporizadores;
static volatile int trocarPedido;
static volatile int saidaPendente;
static volatile int encerrando;
static int escalonadorParado;       /* vTaskSuspendAll */
static int noEscalonador;
static int64_t ultimaTroca;
static int esperaDosTemporizadores; /* o objeto em que o servico dorme */

/* ---------------------------------------------------------------------
 * o relogio */
static int64_t relogioReal(void)
{
    static LARGE_INTEGER f;
    LARGE_INTEGER c;
    if (!f.QuadPart) QueryPerformanceFrequency(&f);
    QueryPerformanceCounter(&c);
    return (int64_t)((double)c.QuadPart * 1e6 / (double)f.QuadPart);
}

/* O relogio da placa (o do millis), em us. */
static int64_t agora(void)
{
    int64_t us = cs_relogio();
    if (us < 0) cs_sair_com(CS_SAIR);
    return us;
}

static int64_t prazoDe(TickType_t ticks)
{
    return ticks == portMAX_DELAY ? SEMPRE : agora() + (int64_t)ticks * 1000;
}

/* ---------------------------------------------------------------------
 * a secao critica (o vigia nao entra com a interrupcao no meio) */
extern "C" void cs_rtos_entrar_critico(void) { cs_critico = cs_critico + 1; }
extern "C" void cs_rtos_sair_critico(void)
{
    if (cs_critico > 0) cs_critico = cs_critico - 1;
}

struct Critico {
    Critico() { cs_rtos_entrar_critico(); }
    ~Critico() { cs_rtos_sair_critico(); }
};

/* Na interrupcao: a thread nao e a que tem a vez (o vigia). */
extern "C" BaseType_t cs_rtos_na_isr(void)
{
    return cs_rtos_ativo && GetCurrentThreadId() != cs_tid_atual ? pdTRUE : pdFALSE;
}

/* ---------------------------------------------------------------------
 * as tarefas */
static TaskHandle_t novaTarefa(const char *nome, UBaseType_t prioridade, BaseType_t nucleo)
{
    Critico c;
    auto *t = new tskTaskControlBlock();
    snprintf(t->nome, sizeof(t->nome), "%s", nome ? nome : "");
    t->prioridade = prioridade >= configMAX_PRIORITIES ? configMAX_PRIORITIES - 1 : prioridade;
    t->nucleo = nucleo;
    t->vez = CreateEventW(nullptr, FALSE, FALSE, nullptr);
    tarefas.push_back(t);
    return t;
}

/* A primeira chamada ao FreeRTOS: quem chamou (o setup, o loop, o main) vira
 * a tarefa principal -- o "loopTask" da ESP32. */
static void iniciar(void)
{
    if (cs_rtos_ativo) return;
#if defined(ESP32)
    principal = novaTarefa("loopTask", 1, 1);
#elif defined(__AVR__)
    principal = novaTarefa("loop", 0, tskNO_AFFINITY);   /* o loop() da Arduino_FreeRTOS e a tarefa ociosa */
#else
    principal = novaTarefa("main", 1, tskNO_AFFINITY);
#endif
    principal->principal = true;
    principal->tid = GetCurrentThreadId();
    DuplicateHandle(GetCurrentProcess(), GetCurrentThread(), GetCurrentProcess(), &principal->thread, 0, FALSE,
                    DUPLICATE_SAME_ACCESS);
    atual = principal;
    cs_tid_atual = principal->tid;
    ultimaTroca = relogioReal();
    cs_rtos_ativo = 1;
}

static bool vivas(TaskHandle_t t) { return t && t->estado != MORTA; }

static bool podeNoNucleo(TaskHandle_t t, int n)
{
    return portNUM_PROCESSORS == 1 || t->nucleo == tskNO_AFFINITY || t->nucleo == n;
}

/* As que tem direito a vez agora: em cada nucleo, as prontas de maior
 * prioridade. */
static bool elegivel(TaskHandle_t t, UBaseType_t maior[2])
{
    if (t->estado != PRONTA) return false;
    for (int n = 0; n < portNUM_PROCESSORS; n++)
        if (podeNoNucleo(t, n) && t->prioridade >= maior[n]) return true;
    return false;
}

static void maioresPorNucleo(UBaseType_t maior[2])
{
    maior[0] = maior[1] = 0;
    bool achou[2] = {false, false};
    for (auto *t : tarefas) {
        if (t->estado != PRONTA) continue;
        for (int n = 0; n < portNUM_PROCESSORS; n++)
            if (podeNoNucleo(t, n) && (!achou[n] || t->prioridade > maior[n])) {
                maior[n] = t->prioridade;
                achou[n] = true;
            }
    }
}

/* A proxima a rodar: a primeira elegivel depois da atual (o rodizio). */
static TaskHandle_t escolher(bool outra)
{
    UBaseType_t maior[2];
    maioresPorNucleo(maior);
    size_t n = tarefas.size(), aqui = 0;
    for (size_t i = 0; i < n; i++)
        if (tarefas[i] == atual) aqui = i;
    for (size_t k = 1; k <= n; k++) {
        TaskHandle_t t = tarefas[(aqui + k) % n];
        if (outra && t == atual) continue;
        if (elegivel(t, maior)) return t;
    }
    return nullptr;
}

static void acordar(TaskHandle_t t, BaseType_t resultado)
{
    if (t->estado == MORTA) return;
    if (t->estado == SUSPENSA && !t->suspensaAntes) return;
    t->estado = t->suspensaAntes ? SUSPENSA : PRONTA;
    t->suspensaAntes = false;
    t->esperando = nullptr;
    t->tipoDaEspera = ESPERA_NADA;
    t->acorda = SEMPRE;
    t->resultado = resultado;
    if (atual && t->prioridade > atual->prioridade) trocarPedido = 1;
}

/* Quem dormia ate agora acorda; quem esperava ate agora, acorda sem nada. */
static void acordarVencidas(int64_t t0)
{
    for (auto *t : tarefas)
        if ((t->estado == DORMINDO || t->estado == BLOQUEADA) && t->acorda <= t0) acordar(t, pdFALSE);
}

static int64_t proximoPrazo(void)
{
    int64_t p = SEMPRE;
    for (auto *t : tarefas)
        if ((t->estado == DORMINDO || t->estado == BLOQUEADA) && t->acorda < p) p = t->acorda;
    return p;
}

static bool alguemPodeSerAcordadoDeFora(void)
{
    for (auto *t : tarefas)
        if (t->estado == BLOQUEADA || t->estado == SUSPENSA) return true;
    return false;
}

static void depoisDeVoltar(TaskHandle_t eu)
{
    if (encerrando) {
        if (!eu->principal) ExitThread(0);
        return;
    }
    if (saidaPendente && eu->principal) {
        int r = saidaPendente;
        saidaPendente = 0;
        atual = principal;
        cs_tid_atual = principal->tid;
        cs_trazer_falha();
        cs_sair_com(r);
    }
}

/* Passa a vez para "proxima" e espera ela voltar (a morta so passa). */
static void passarVez(TaskHandle_t proxima)
{
    TaskHandle_t eu = atual;
    if (proxima == eu) return;
    atual = proxima;
    cs_tid_atual = proxima->tid;
    proxima->naVezDesde = ultimaTroca = relogioReal();
    trocarPedido = 0;
    SetEvent(proxima->vez);
    if (eu->estado == MORTA && !eu->principal) return;
    WaitForSingleObject(eu->vez, INFINITE);
    depoisDeVoltar(eu);
}

/* A atual nao pode seguir (dorme, esperou, morreu) ou cede: a vez vai para
 * a proxima pronta; sem nenhuma, espera pelo simulador o prazo mais proximo. */
static void reagendar(void)
{
    noEscalonador++;
    while (true) {
        int64_t t0 = agora();
        acordarVencidas(t0);
        TaskHandle_t t = escolher(false);
        if (t) {
            noEscalonador--;
            passarVez(t);
            return;
        }
        int64_t prazo = proximoPrazo();
        int64_t falta = prazo == SEMPRE ? 1000 : prazo - t0;
        if (alguemPodeSerAcordadoDeFora() && falta > 1000) falta = 1000;
        if (falta <= 0) continue;
        if (falta >= 1000)
            ch(nullptr, "delay", cs_i(falta / 1000));
        else
            ch(nullptr, "delayMicroseconds", cs_i(falta));
    }
}

/* O tick: chamado em cada chamada ao simulador. */
extern "C" void cs_rtos_ponto(void)
{
    if (!cs_rtos_ativo || noEscalonador || escalonadorParado || cs_critico) return;
    if (GetCurrentThreadId() != cs_tid_atual) return;
    if (!trocarPedido && relogioReal() - ultimaTroca < 1000) return;
    trocarPedido = 0;
    noEscalonador++;
    acordarVencidas(agora());
    TaskHandle_t outra = escolher(true);
    noEscalonador--;
    if (!outra) {
        ultimaTroca = relogioReal();
        return;
    }
    /* A atual cede se perdeu o direito, ou se e a vez da outra no rodizio. */
    passarVez(outra);
}

extern "C" void cs_rtos_ceder(void)
{
    if (!cs_rtos_ativo || cs_rtos_na_isr()) return;
    noEscalonador++;
    acordarVencidas(agora());
    TaskHandle_t outra = escolher(true);
    noEscalonador--;
    if (outra) passarVez(outra);
}

extern "C" void cs_rtos_ceder_da_isr(BaseType_t trocar)
{
    if (trocar) trocarPedido = 1;
}

extern "C" BaseType_t cs_rtos_nucleo(void)
{
    if (!cs_rtos_ativo || !atual) return portNUM_PROCESSORS > 1 ? 1 : 0;
    return atual->nucleo == tskNO_AFFINITY ? (portNUM_PROCESSORS > 1 ? 0 : 0) : atual->nucleo;
}

/* Bloqueia a atual no objeto ate o prazo; pdTRUE se o objeto a acordou.
 * Chamada com a secao critica aberta pela conferencia (a fila vazia), que ela
 * fecha depois de se registrar: senao a interrupcao que da o item entre as
 * duas coisas nao acharia ninguem esperando, e a tarefa dormiria com o item
 * ja na fila. */
static BaseType_t esperar(void *objeto, int tipo, int64_t prazo)
{
    TaskHandle_t eu = atual;
    eu->estado = prazo == 0 ? PRONTA : BLOQUEADA;
    eu->esperando = objeto;
    eu->tipoDaEspera = tipo;
    eu->acorda = prazo;
    eu->resultado = pdFALSE;
    cs_rtos_sair_critico();
    reagendar();
    return eu->resultado;
}

/* A que espera no objeto, a de maior prioridade. */
static TaskHandle_t quemEspera(void *objeto, int tipo)
{
    TaskHandle_t melhor = nullptr;
    for (auto *t : tarefas)
        if (t->estado == BLOQUEADA && t->esperando == objeto && t->tipoDaEspera == tipo
            && (!melhor || t->prioridade > melhor->prioridade))
            melhor = t;
    return melhor;
}

/* A thread de cada tarefa: espera a vez, roda a funcao, e quando ela acaba
 * (ou o sketch sai por ela) passa a vez adiante. */
static void rodarTarefa(void *p)
{
    auto *t = (TaskHandle_t)p;
    t->fn(t->param);
    /* A funcao da tarefa voltou: na placa e erro; aqui, a tarefa acaba. */
    vTaskDelete(nullptr);
}

static DWORD WINAPI corpoDaTarefa(void *p)
{
    auto *t = (TaskHandle_t)p;
    WaitForSingleObject(t->vez, INFINITE);
    if (encerrando) return 0;
    int r = cs_rodar_protegido(rodarTarefa, t);
    if (encerrando) return 0;
    if (r == CS_TAREFA_ACABOU) return 0;   /* a vez ja passou no vTaskDelete */
    /* O sketch saiu por esta tarefa (Parar, erro): quem conta ao simulador e
     * a principal, na thread dela. */
    t->estado = MORTA;
    cs_guardar_falha();
    saidaPendente = r ? r : CS_SAIR;
    atual = principal;
    cs_tid_atual = principal->tid;
    SetEvent(principal->vez);
    return 0;
}

extern "C" BaseType_t xTaskCreatePinnedToCore(TaskFunction_t fn, const char *nome, uint32_t pilha, void *param,
                                              UBaseType_t prioridade, TaskHandle_t *criada, BaseType_t nucleo)
{
    iniciar();
    TaskHandle_t t;
    {
        Critico c;
        t = novaTarefa(nome, prioridade, nucleo);
        t->fn = fn;
        t->param = param;
        t->pilha = pilha;
        t->thread = CreateThread(nullptr, 16u << 20, corpoDaTarefa, t, STACK_SIZE_PARAM_IS_A_RESERVATION, &t->tid);
        if (!t->thread) {
            t->estado = MORTA;
            return errCOULD_NOT_ALLOCATE_REQUIRED_MEMORY;
        }
    }
    if (criada) *criada = t;
    /* A nova de prioridade maior roda ja (a da ESP32 no outro nucleo, no rodizio). */
    if (!cs_rtos_na_isr() && t->prioridade > atual->prioridade) cs_rtos_ceder();
    return pdPASS;
}

extern "C" BaseType_t xTaskCreate(TaskFunction_t fn, const char *nome, uint32_t pilha, void *param, UBaseType_t prioridade,
                                  TaskHandle_t *criada)
{
    return xTaskCreatePinnedToCore(fn, nome, pilha, param, prioridade, criada, tskNO_AFFINITY);
}

extern "C" BaseType_t xTaskCreateAffinitySet(TaskFunction_t fn, const char *nome, uint32_t pilha, void *param,
                                             UBaseType_t prioridade, UBaseType_t mascara, TaskHandle_t *criada)
{
    BaseType_t nucleo = mascara == 1 ? 0 : mascara == 2 ? 1 : tskNO_AFFINITY;
    return xTaskCreatePinnedToCore(fn, nome, pilha, param, prioridade, criada, nucleo);
}

extern "C" TaskHandle_t xTaskCreateStaticPinnedToCore(TaskFunction_t fn, const char *nome, uint32_t pilha, void *param,
                                                      UBaseType_t prioridade, StackType_t *, StaticTask_t *, BaseType_t nucleo)
{
    TaskHandle_t t = nullptr;
    xTaskCreatePinnedToCore(fn, nome, pilha, param, prioridade, &t, nucleo);
    return t;
}

extern "C" TaskHandle_t xTaskCreateStatic(TaskFunction_t fn, const char *nome, uint32_t pilha, void *param,
                                          UBaseType_t prioridade, StackType_t *b, StaticTask_t *s)
{
    return xTaskCreateStaticPinnedToCore(fn, nome, pilha, param, prioridade, b, s, tskNO_AFFINITY);
}

extern "C" void vTaskDelete(TaskHandle_t t)
{
    iniciar();
    if (!t) t = atual;
    if (!vivas(t)) return;
    {
        Critico c;
        t->estado = MORTA;
        t->esperando = nullptr;
    }
    /* A de outra thread fica esperando a vez que nao vem mais (sai no fim da
     * rodada, em cs_rtos_encerrar). */
    if (t != atual) return;
    reagendar();
    /* A principal morta fica esperando (o loop() da placa nao volta mais);
     * a tarefa sai pela entrada dela. */
    if (!t->principal) cs_sair_com(CS_TAREFA_ACABOU);
}

extern "C" void vTaskDelay(const TickType_t ticks)
{
    if (!cs_rtos_ativo) {
        ch(nullptr, "delay", cs_i((int64_t)ticks));
        return;
    }
    if (cs_rtos_na_isr()) return;
    if (ticks == 0) {
        cs_rtos_ceder();
        return;
    }
    atual->estado = DORMINDO;
    atual->acorda = agora() + (int64_t)ticks * 1000;
    reagendar();
}

extern "C" BaseType_t xTaskDelayUntil(TickType_t *anterior, const TickType_t incremento)
{
    iniciar();
    TickType_t alvo = *anterior + incremento;
    TickType_t hoje = xTaskGetTickCount();
    *anterior = alvo;
    if ((int32_t)(alvo - hoje) <= 0) {
        cs_rtos_ceder();
        return pdFALSE;
    }
    vTaskDelay(alvo - hoje);
    return pdTRUE;
}

extern "C" BaseType_t xTaskAbortDelay(TaskHandle_t t)
{
    if (!t || (t->estado != DORMINDO && t->estado != BLOQUEADA)) return pdFAIL;
    acordar(t, pdFALSE);
    return pdPASS;
}

extern "C" TickType_t xTaskGetTickCount(void) { return (TickType_t)(cs_relogio() / 1000); }
extern "C" TickType_t xTaskGetTickCountFromISR(void) { return xTaskGetTickCount(); }

extern "C" void vTaskSuspend(TaskHandle_t t)
{
    iniciar();
    if (!t) t = atual;
    if (!vivas(t)) return;
    {
        Critico c;
        if (t->estado == BLOQUEADA || t->estado == DORMINDO) {
            t->esperando = nullptr;
            t->acorda = SEMPRE;
        }
        t->estado = SUSPENSA;
    }
    if (t == atual && !cs_rtos_na_isr()) reagendar();
}

extern "C" void vTaskResume(TaskHandle_t t)
{
    if (!t || t->estado != SUSPENSA) return;
    Critico c;
    t->estado = PRONTA;
    if (atual && t->prioridade > atual->prioridade) trocarPedido = 1;
}

extern "C" BaseType_t xTaskResumeFromISR(TaskHandle_t t)
{
    bool maior = t && atual && t->prioridade > atual->prioridade;
    vTaskResume(t);
    return maior ? pdTRUE : pdFALSE;
}

extern "C" UBaseType_t uxTaskPriorityGet(TaskHandle_t t)
{
    iniciar();
    return (t ? t : atual)->prioridade;
}
extern "C" UBaseType_t uxTaskPriorityGetFromISR(TaskHandle_t t) { return uxTaskPriorityGet(t); }

extern "C" void vTaskPrioritySet(TaskHandle_t t, UBaseType_t p)
{
    iniciar();
    if (!t) t = atual;
    t->prioridade = p >= configMAX_PRIORITIES ? configMAX_PRIORITIES - 1 : p;
    trocarPedido = 1;
}

extern "C" TaskHandle_t xTaskGetCurrentTaskHandle(void)
{
    iniciar();
    return atual;
}
extern "C" TaskHandle_t xTaskGetCurrentTaskHandleForCore(BaseType_t) { return xTaskGetCurrentTaskHandle(); }

extern "C" TaskHandle_t xTaskGetHandle(const char *nome)
{
    iniciar();
    for (auto *t : tarefas)
        if (vivas(t) && nome && strcmp(t->nome, nome) == 0) return t;
    return nullptr;
}

extern "C" TaskHandle_t xTaskGetIdleTaskHandle(void) { return nullptr; }
extern "C" TaskHandle_t xTaskGetIdleTaskHandleForCore(BaseType_t) { return nullptr; }

extern "C" char *pcTaskGetName(TaskHandle_t t)
{
    iniciar();
    return (t ? t : atual)->nome;
}

/* A pilha que sobra: a tarefa tem a pilha do PC (bem maior); o numero e o
 * que a placa mostraria com folga, para o sketch que o confere. */
extern "C" UBaseType_t uxTaskGetStackHighWaterMark(TaskHandle_t t)
{
    iniciar();
    t = t ? t : atual;
    return t->pilha ? t->pilha / 2 : 4096;
}

extern "C" eTaskState eTaskGetState(TaskHandle_t t)
{
    if (!t) return eInvalid;
    if (t == atual) return eRunning;
    switch (t->estado) {
    case PRONTA: return eReady;
    case DORMINDO:
    case BLOQUEADA: return eBlocked;
    case SUSPENSA: return eSuspended;
    default: return eDeleted;
    }
}

extern "C" UBaseType_t uxTaskGetNumberOfTasks(void)
{
    UBaseType_t n = 0;
    for (auto *t : tarefas)
        if (vivas(t)) n++;
    return n;
}

extern "C" BaseType_t xTaskGetCoreID(TaskHandle_t t)
{
    iniciar();
    return (t ? t : atual)->nucleo;
}
extern "C" BaseType_t xTaskGetAffinity(TaskHandle_t t) { return xTaskGetCoreID(t); }

extern "C" void vTaskSuspendAll(void)
{
    iniciar();
    escalonadorParado++;
}

extern "C" BaseType_t xTaskResumeAll(void)
{
    if (escalonadorParado > 0) escalonadorParado--;
    return pdFALSE;
}

/* O main() do C que entrega a placa as tarefas: a principal para aqui. */
extern "C" void vTaskStartScheduler(void)
{
    iniciar();
    if (!atual->principal) return;
    atual->estado = MORTA;
    reagendar();
    while (true) WaitForSingleObject(principal->vez, INFINITE), depoisDeVoltar(principal);
}

extern "C" void vTaskEndScheduler(void) {}

extern "C" BaseType_t xTaskGetSchedulerState(void)
{
    if (escalonadorParado) return taskSCHEDULER_SUSPENDED;
    return taskSCHEDULER_RUNNING;
}

extern "C" void vTaskList(char *saida)
{
    iniciar();
    saida[0] = 0;
    for (auto *t : tarefas) {
        if (!vivas(t)) continue;
        char e = t == atual ? 'X' : t->estado == PRONTA ? 'R' : t->estado == SUSPENSA ? 'S' : 'B';
        char linha[64];
        snprintf(linha, sizeof(linha), "%-16s%c\t%u\t%u\t%u\r\n", t->nome, e, (unsigned)t->prioridade,
                 (unsigned)uxTaskGetStackHighWaterMark(t), (unsigned)(t - tarefas[0]));
        strcat(saida, linha);
    }
}

/* ---------------------------------------------------------------------
 * as notificacoes */
extern "C" BaseType_t xTaskGenericNotify(TaskHandle_t t, UBaseType_t i, uint32_t valor, eNotifyAction acao, uint32_t *antes)
{
    iniciar();
    if (!t || i > 2) return pdFAIL;
    BaseType_t r = pdPASS;
    {
        Critico c;
        if (antes) *antes = t->notificacao[i];
        uint8_t estava = t->notificada[i];
        switch (acao) {
        case eSetBits: t->notificacao[i] |= valor; break;
        case eIncrement: t->notificacao[i]++; break;
        case eSetValueWithOverwrite: t->notificacao[i] = valor; break;
        case eSetValueWithoutOverwrite:
            if (estava) r = pdFAIL;
            else t->notificacao[i] = valor;
            break;
        default: break;
        }
        t->notificada[i] = 1;
        if (t->estado == BLOQUEADA && t->tipoDaEspera == ESPERA_NOTIFICACAO && t->indiceEsperado == (int)i) acordar(t, pdTRUE);
    }
    if (!cs_rtos_na_isr() && trocarPedido) cs_rtos_ceder();
    return r;
}

extern "C" BaseType_t xTaskGenericNotifyFromISR(TaskHandle_t t, UBaseType_t i, uint32_t valor, eNotifyAction acao,
                                                uint32_t *antes, BaseType_t *acordou)
{
    bool maior = t && atual && t->prioridade > atual->prioridade && t->estado == BLOQUEADA;
    BaseType_t r = xTaskGenericNotify(t, i, valor, acao, antes);
    if (acordou && maior) *acordou = pdTRUE;
    return r;
}

extern "C" BaseType_t xTaskGenericNotifyWait(UBaseType_t i, uint32_t limparAoEntrar, uint32_t limparAoSair, uint32_t *valor,
                                             TickType_t espera)
{
    iniciar();
    TaskHandle_t eu = atual;
    if (i > 2) return pdFALSE;
    int64_t prazo = espera == 0 ? 0 : prazoDe(espera);
    cs_rtos_entrar_critico();
    if (!eu->notificada[i]) eu->notificacao[i] &= ~limparAoEntrar;
    if (!eu->notificada[i] && espera && !cs_rtos_na_isr()) {
        eu->indiceEsperado = (int)i;
        esperar(eu, ESPERA_NOTIFICACAO, prazo);
    } else {
        cs_rtos_sair_critico();
    }
    Critico c;
    if (valor) *valor = eu->notificacao[i];
    if (!eu->notificada[i]) return pdFALSE;
    eu->notificacao[i] &= ~limparAoSair;
    eu->notificada[i] = 0;
    return pdTRUE;
}

extern "C" uint32_t ulTaskGenericNotifyTake(UBaseType_t i, BaseType_t zerar, TickType_t espera)
{
    iniciar();
    TaskHandle_t eu = atual;
    if (i > 2) return 0;
    int64_t prazo = espera ? prazoDe(espera) : 0;
    cs_rtos_entrar_critico();
    if (eu->notificacao[i] == 0 && espera && !cs_rtos_na_isr()) {
        eu->indiceEsperado = (int)i;
        esperar(eu, ESPERA_NOTIFICACAO, prazo);
    } else {
        cs_rtos_sair_critico();
    }
    Critico c;
    uint32_t v = eu->notificacao[i];
    if (v) eu->notificacao[i] = zerar ? 0 : v - 1;
    eu->notificada[i] = 0;
    return v;
}

extern "C" BaseType_t xTaskGenericNotifyStateClear(TaskHandle_t t, UBaseType_t i)
{
    iniciar();
    t = t ? t : atual;
    if (i > 2) return pdFALSE;
    BaseType_t estava = t->notificada[i];
    t->notificada[i] = 0;
    return estava;
}

extern "C" uint32_t ulTaskGenericNotifyValueClear(TaskHandle_t t, UBaseType_t i, uint32_t bits)
{
    iniciar();
    t = t ? t : atual;
    if (i > 2) return 0;
    uint32_t v = t->notificacao[i];
    t->notificacao[i] &= ~bits;
    return v;
}

/* ---------------------------------------------------------------------
 * as filas e os semaforos */
extern "C" QueueHandle_t xQueueCreate(UBaseType_t tamanho, UBaseType_t item)
{
    iniciar();
    auto *q = new QueueDefinition();
    q->tamanho = tamanho ? tamanho : 1;
    q->item = item;
    if (item) q->dados = (uint8_t *)calloc(q->tamanho, item);
    return q;
}

extern "C" QueueHandle_t xQueueCreateStatic(UBaseType_t tamanho, UBaseType_t item, uint8_t *, StaticQueue_t *)
{
    return xQueueCreate(tamanho, item);
}

/* Poe o item (a fila tem lugar, ou e a sobrescrita) e acorda quem esperava. */
static void colocar(QueueHandle_t q, const void *item, BaseType_t onde)
{
    if (onde == queueOVERWRITE && q->quantos == q->tamanho && q->quantos > 0) {
        q->quantos--;   /* a fila de um so: o novo toma o lugar do velho */
    }
    if (q->item) {
        UBaseType_t pos;
        if (onde == queueSEND_TO_FRONT) {
            q->inicio = (q->inicio + q->tamanho - 1) % q->tamanho;
            pos = q->inicio;
        } else {
            pos = (q->inicio + q->quantos) % q->tamanho;
        }
        memcpy(q->dados + (size_t)pos * q->item, item, q->item);
    }
    q->quantos++;
    if (TaskHandle_t t = quemEspera(q, ESPERA_RECEBER)) acordar(t, pdTRUE);
}

static void tirar(QueueHandle_t q, void *destino, bool olhar)
{
    if (q->item && destino) memcpy(destino, q->dados + (size_t)q->inicio * q->item, q->item);
    if (olhar) return;
    q->inicio = (q->inicio + 1) % q->tamanho;
    q->quantos--;
    if (TaskHandle_t t = quemEspera(q, ESPERA_ENVIAR)) acordar(t, pdTRUE);
}

extern "C" BaseType_t xQueueGenericSend(QueueHandle_t q, const void *item, TickType_t espera, BaseType_t onde)
{
    if (!q) return pdFAIL;
    iniciar();
    bool isr = cs_rtos_na_isr();
    int64_t prazo = espera == 0 || isr ? 0 : prazoDe(espera);
    while (true) {
        int64_t t0 = prazo == 0 || prazo == SEMPRE ? 0 : agora();
        cs_rtos_entrar_critico();
        if (q->quantos < q->tamanho || onde == queueOVERWRITE) {
            colocar(q, item, onde);
            cs_rtos_sair_critico();
            break;
        }
        if (prazo == 0 || (prazo != SEMPRE && t0 >= prazo)) {
            cs_rtos_sair_critico();
            return errQUEUE_FULL;
        }
        esperar(q, ESPERA_ENVIAR, prazo);
    }
    if (!isr && trocarPedido) cs_rtos_ceder();
    return pdPASS;
}

extern "C" BaseType_t xQueueGenericSendFromISR(QueueHandle_t q, const void *item, BaseType_t *acordou, BaseType_t onde)
{
    if (!q) return pdFAIL;
    Critico c;
    if (q->quantos >= q->tamanho && onde != queueOVERWRITE) return errQUEUE_FULL;
    TaskHandle_t t = quemEspera(q, ESPERA_RECEBER);
    colocar(q, item, onde);
    if (acordou && t && atual && t->prioridade > atual->prioridade) *acordou = pdTRUE;
    return pdPASS;
}

static BaseType_t receber(QueueHandle_t q, void *destino, TickType_t espera, bool olhar)
{
    if (!q) return pdFAIL;
    iniciar();
    bool isr = cs_rtos_na_isr();
    int64_t prazo = espera == 0 || isr ? 0 : prazoDe(espera);
    while (true) {
        int64_t t0 = prazo == 0 || prazo == SEMPRE ? 0 : agora();
        cs_rtos_entrar_critico();
        if (q->quantos > 0) {
            tirar(q, destino, olhar);
            cs_rtos_sair_critico();
            break;
        }
        if (prazo == 0 || (prazo != SEMPRE && t0 >= prazo)) {
            cs_rtos_sair_critico();
            return errQUEUE_EMPTY;
        }
        esperar(q, ESPERA_RECEBER, prazo);
    }
    if (!isr && trocarPedido) cs_rtos_ceder();
    return pdPASS;
}

extern "C" BaseType_t xQueueReceive(QueueHandle_t q, void *destino, TickType_t espera) { return receber(q, destino, espera, false); }
extern "C" BaseType_t xQueuePeek(QueueHandle_t q, void *destino, TickType_t espera) { return receber(q, destino, espera, true); }

extern "C" BaseType_t xQueueReceiveFromISR(QueueHandle_t q, void *destino, BaseType_t *acordou)
{
    if (!q) return pdFAIL;
    Critico c;
    if (q->quantos == 0) return pdFAIL;
    TaskHandle_t t = quemEspera(q, ESPERA_ENVIAR);
    tirar(q, destino, false);
    if (acordou && t && atual && t->prioridade > atual->prioridade) *acordou = pdTRUE;
    return pdPASS;
}

extern "C" BaseType_t xQueuePeekFromISR(QueueHandle_t q, void *destino)
{
    if (!q || q->quantos == 0) return pdFAIL;
    tirar(q, destino, true);
    return pdPASS;
}

extern "C" UBaseType_t uxQueueMessagesWaiting(QueueHandle_t q) { return q ? q->quantos : 0; }
extern "C" UBaseType_t uxQueueMessagesWaitingFromISR(QueueHandle_t q) { return uxQueueMessagesWaiting(q); }
extern "C" UBaseType_t uxQueueSpacesAvailable(QueueHandle_t q) { return q ? q->tamanho - q->quantos : 0; }
extern "C" BaseType_t xQueueIsQueueEmptyFromISR(QueueHandle_t q) { return !q || q->quantos == 0; }
extern "C" BaseType_t xQueueIsQueueFullFromISR(QueueHandle_t q) { return q && q->quantos == q->tamanho; }

extern "C" BaseType_t xQueueReset(QueueHandle_t q)
{
    if (!q) return pdFAIL;
    Critico c;
    q->quantos = q->inicio = 0;
    while (TaskHandle_t t = quemEspera(q, ESPERA_ENVIAR)) acordar(t, pdTRUE);
    return pdPASS;
}

extern "C" void vQueueDelete(QueueHandle_t q)
{
    if (!q) return;
    Critico c;
    for (auto *t : tarefas)
        if (t->esperando == q) acordar(t, pdFALSE);
    free(q->dados);
    delete q;
}

/* O semaforo e a fila sem dados: a contagem e o "quantos". O mutex comeca
 * cheio e tem dono. */
extern "C" QueueHandle_t cs_rtos_semaforo(UBaseType_t maximo, UBaseType_t inicial, int tipo)
{
    QueueHandle_t s = xQueueCreate(maximo, 0);
    s->quantos = inicial > maximo ? maximo : inicial;
    s->tipo = tipo;
    return s;
}

extern "C" BaseType_t cs_rtos_pegar(QueueHandle_t s, TickType_t espera, int recursivo)
{
    if (!s) return pdFAIL;
    iniciar();
    if (s->tipo == 2 && recursivo && s->dono == atual) {
        s->recursoes++;
        return pdPASS;
    }
    if (receber(s, nullptr, espera, false) != pdPASS) return pdFAIL;
    if (s->tipo) {
        s->dono = atual;
        s->recursoes = 1;
    }
    return pdPASS;
}

extern "C" BaseType_t cs_rtos_soltar(QueueHandle_t s, int recursivo)
{
    if (!s) return pdFAIL;
    iniciar();
    if (s->tipo) {
        if (s->dono != atual && !cs_rtos_na_isr()) return pdFAIL;
        if (s->tipo == 2 && recursivo && --s->recursoes > 0) return pdPASS;
        s->dono = nullptr;
        s->recursoes = 0;
    }
    return xQueueGenericSend(s, nullptr, 0, queueSEND_TO_BACK);
}

extern "C" BaseType_t cs_rtos_soltar_da_isr(QueueHandle_t s, BaseType_t *acordou)
{
    if (!s) return pdFAIL;
    if (s->tipo) {
        s->dono = nullptr;
        s->recursoes = 0;
    }
    return xQueueGenericSendFromISR(s, nullptr, acordou, queueSEND_TO_BACK);
}

extern "C" BaseType_t cs_rtos_pegar_da_isr(QueueHandle_t s, BaseType_t *acordou)
{
    return xQueueReceiveFromISR(s, nullptr, acordou);
}

extern "C" TaskHandle_t xSemaphoreGetMutexHolder(SemaphoreHandle_t s) { return s ? s->dono : nullptr; }

/* ---------------------------------------------------------------------
 * os temporizadores: uma tarefa de servico, como no FreeRTOS */
static void servico(void *)
{
    while (true) {
        int64_t t0 = agora();
        tmrTimerControl *vencido = nullptr;
        int64_t proximo = SEMPRE;
        cs_rtos_entrar_critico();
        for (auto *t : temporizadores) {
            if (!t->ativo || t->apagado) continue;
            if (t->expira <= t0 && (!vencido || t->expira < vencido->expira)) vencido = t;
            if (t->expira < proximo) proximo = t->expira;
        }
        if (vencido) {
            if (vencido->repete) vencido->expira += (int64_t)vencido->periodo * 1000;
            else vencido->ativo = false;
            cs_rtos_sair_critico();
            if (vencido->cb) vencido->cb(vencido);
            continue;
        }
        esperar(&esperaDosTemporizadores, ESPERA_TEMPORIZADOR, proximo);
    }
}

static void cutucarServico(void)
{
    if (!servicoDosTemporizadores) {
        xTaskCreatePinnedToCore(servico, "Tmr Svc", 4096, nullptr, configTIMER_TASK_PRIORITY, &servicoDosTemporizadores,
                                tskNO_AFFINITY);
        return;
    }
    if (TaskHandle_t t = quemEspera(&esperaDosTemporizadores, ESPERA_TEMPORIZADOR)) acordar(t, pdTRUE);
}

extern "C" TimerHandle_t xTimerCreate(const char *nome, const TickType_t periodo, const UBaseType_t repete, void *const id,
                                      TimerCallbackFunction_t cb)
{
    iniciar();
    auto *t = new tmrTimerControl();
    snprintf(t->nome, sizeof(t->nome), "%s", nome ? nome : "");
    t->periodo = periodo ? periodo : 1;
    t->repete = repete;
    t->id = id;
    t->cb = cb;
    Critico c;
    temporizadores.push_back(t);
    return t;
}

extern "C" TimerHandle_t xTimerCreateStatic(const char *nome, const TickType_t periodo, const UBaseType_t repete, void *const id,
                                            TimerCallbackFunction_t cb, StaticTimer_t *)
{
    return xTimerCreate(nome, periodo, repete, id, cb);
}

extern "C" BaseType_t cs_rtos_temporizador(TimerHandle_t t, int comando, TickType_t valor)
{
    if (!t) return pdFAIL;
    iniciar();
    {
        Critico c;
        switch (comando) {
        case 1:    /* start / reset */
            t->ativo = true;
            t->expira = agora() + (int64_t)t->periodo * 1000;
            break;
        case 2: t->ativo = false; break;
        case 3:    /* changePeriod (e liga) */
            t->periodo = valor ? valor : 1;
            t->ativo = true;
            t->expira = agora() + (int64_t)t->periodo * 1000;
            break;
        case 4:
            t->ativo = false;
            t->apagado = true;
            break;
        }
    }
    cutucarServico();
    return pdPASS;
}

extern "C" void *pvTimerGetTimerID(const TimerHandle_t t) { return t ? t->id : nullptr; }
extern "C" void vTimerSetTimerID(TimerHandle_t t, void *id)
{
    if (t) t->id = id;
}
extern "C" BaseType_t xTimerIsTimerActive(TimerHandle_t t) { return t && t->ativo ? pdTRUE : pdFALSE; }
extern "C" const char *pcTimerGetName(TimerHandle_t t) { return t ? t->nome : ""; }
extern "C" TickType_t xTimerGetPeriod(TimerHandle_t t) { return t ? t->periodo : 0; }
extern "C" TickType_t xTimerGetExpiryTime(TimerHandle_t t) { return t ? (TickType_t)(t->expira / 1000) : 0; }
extern "C" void vTimerSetReloadMode(TimerHandle_t t, const UBaseType_t repete)
{
    if (t) t->repete = repete;
}
extern "C" UBaseType_t uxTimerGetReloadMode(TimerHandle_t t) { return t ? t->repete : 0; }

struct Pendente {
    PendedFunction_t fn;
    void *p1;
    uint32_t p2;
};

static void rodarPendente(TimerHandle_t t)
{
    auto *p = (Pendente *)pvTimerGetTimerID(t);
    p->fn(p->p1, p->p2);
    delete p;
    cs_rtos_temporizador(t, 4, 0);
}

extern "C" BaseType_t xTimerPendFunctionCall(PendedFunction_t fn, void *p1, uint32_t p2, TickType_t)
{
    TimerHandle_t t = xTimerCreate("pend", 1, pdFALSE, new Pendente{fn, p1, p2}, rodarPendente);
    return cs_rtos_temporizador(t, 1, 0);
}

/* ---------------------------------------------------------------------
 * os grupos de eventos */
extern "C" EventGroupHandle_t xEventGroupCreate(void)
{
    iniciar();
    return new EventGroupDef_t();
}
extern "C" EventGroupHandle_t xEventGroupCreateStatic(StaticEventGroup_t *) { return xEventGroupCreate(); }

static bool satisfeito(EventBits_t bits, EventBits_t querido, bool todos)
{
    return todos ? (bits & querido) == querido : (bits & querido) != 0;
}

extern "C" EventBits_t xEventGroupSetBits(EventGroupHandle_t g, const EventBits_t bits)
{
    if (!g) return 0;
    iniciar();
    EventBits_t r;
    {
        Critico c;
        g->bits |= bits;
        EventBits_t limpar = 0;
        for (auto *t : tarefas)
            if (t->estado == BLOQUEADA && t->esperando == g && t->tipoDaEspera == ESPERA_BITS
                && satisfeito(g->bits, t->bitsQuerido, t->todosOsBits)) {
                t->bitsNoAcordar = g->bits;
                if (t->resultado) limpar |= t->bitsQuerido;   /* "resultado" guarda o clearOnExit da espera */
                acordar(t, pdTRUE);
            }
        r = g->bits;
        g->bits &= ~limpar;
    }
    if (!cs_rtos_na_isr() && trocarPedido) cs_rtos_ceder();
    return r;
}

extern "C" BaseType_t xEventGroupSetBitsFromISR(EventGroupHandle_t g, const EventBits_t bits, BaseType_t *acordou)
{
    xEventGroupSetBits(g, bits);
    if (acordou && trocarPedido) *acordou = pdTRUE;
    return pdPASS;
}

extern "C" EventBits_t xEventGroupClearBits(EventGroupHandle_t g, const EventBits_t bits)
{
    if (!g) return 0;
    Critico c;
    EventBits_t antes = g->bits;
    g->bits &= ~bits;
    return antes;
}

extern "C" EventBits_t xEventGroupGetBits(EventGroupHandle_t g) { return g ? g->bits : 0; }

extern "C" EventBits_t xEventGroupWaitBits(EventGroupHandle_t g, const EventBits_t querido, const BaseType_t limpar,
                                           const BaseType_t todos, TickType_t espera)
{
    if (!g) return 0;
    iniciar();
    int64_t prazo = espera ? prazoDe(espera) : 0;
    cs_rtos_entrar_critico();
    if (satisfeito(g->bits, querido, todos)) {
        EventBits_t r = g->bits;
        if (limpar) g->bits &= ~querido;
        cs_rtos_sair_critico();
        return r;
    }
    if (espera == 0 || cs_rtos_na_isr()) {
        cs_rtos_sair_critico();
        return g->bits;
    }
    TaskHandle_t eu = atual;
    eu->bitsQuerido = querido;
    eu->todosOsBits = todos != 0;
    eu->estado = BLOQUEADA;
    eu->esperando = g;
    eu->tipoDaEspera = ESPERA_BITS;
    eu->acorda = prazo;
    eu->resultado = limpar ? pdTRUE : pdFALSE;
    eu->bitsNoAcordar = 0;
    cs_rtos_sair_critico();
    reagendar();
    /* Acordou pelos bits (bitsNoAcordar tem o valor de entao) ou pelo prazo. */
    return eu->bitsNoAcordar ? eu->bitsNoAcordar : g->bits;
}

extern "C" EventBits_t xEventGroupSync(EventGroupHandle_t g, const EventBits_t poe, const EventBits_t querido, TickType_t espera)
{
    xEventGroupSetBits(g, poe);
    return xEventGroupWaitBits(g, querido, pdTRUE, pdTRUE, espera);
}

extern "C" void vEventGroupDelete(EventGroupHandle_t g)
{
    if (!g) return;
    Critico c;
    for (auto *t : tarefas)
        if (t->esperando == g) acordar(t, pdFALSE);
    delete g;
}

/* ---------------------------------------------------------------------
 * a memoria */
extern "C" size_t xPortGetFreeHeapSize(void) { return 280000; }
extern "C" size_t xPortGetMinimumEverFreeHeapSize(void) { return 250000; }
extern "C" void *pvPortMalloc(size_t n) { return malloc(n); }
extern "C" void vPortFree(void *p) { free(p); }

/* ---------------------------------------------------------------------
 * o fim da rodada: as threads das tarefas saem antes da DLL ser solta */
extern "C" __declspec(dllexport) void cs_rtos_encerrar(void)
{
    if (!cs_rtos_ativo) return;
    encerrando = 1;
    std::vector<HANDLE> threads;
    for (auto *t : tarefas) {
        if (t->principal || !t->thread) continue;
        SetEvent(t->vez);
        threads.push_back(t->thread);
    }
    for (size_t i = 0; i < threads.size(); i += 64)
        WaitForMultipleObjects((DWORD)(threads.size() - i < 64 ? threads.size() - i : 64), threads.data() + i, TRUE, 2000);
}
