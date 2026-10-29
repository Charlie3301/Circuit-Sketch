/* cs_ringbuf.cpp -- o ringbuffer do ESP-IDF (freertos/ringbuf.h, o cabecalho
 * de verdade): o buffer circular que o driver da UART, o cbuf do core e os
 * programas do IDF usam para passar dados entre tarefas.
 *
 * Os tres tipos do IDF:
 *   - NOSPLIT: itens inteiros, cada um com 8 bytes de cabecalho e tamanho
 *     arredondado para 4 (o que ocupa no buffer de verdade);
 *   - ALLOWSPLIT: igual, e o item que nao cabe no fim pode dividir em dois;
 *   - BYTEBUF: so bytes, sem itens.
 * O item recebido continua ocupando espaco ate o vRingbufferReturnItem, como
 * na placa. Quem espera (para mandar ou receber) espera de verdade, com o
 * tempo do FreeRTOS. */
#include "Arduino.h"
#if defined(ESP32)
#include "freertos/ringbuf.h"
#include <deque>
#include <vector>
#include <list>

namespace {

struct Item {
    std::vector<uint8_t> dados;
    bool emprestado = false;
};

struct Anel {
    RingbufferType_t tipo;
    size_t capacidade;
    size_t usado = 0;                 /* o que esta guardado, inclusive o emprestado */
    std::list<Item> itens;            /* NOSPLIT/ALLOWSPLIT */
    std::deque<uint8_t> bytes;        /* BYTEBUF */
    std::list<std::vector<uint8_t>> emprestados;   /* BYTEBUF: o que saiu e nao voltou */
    size_t bytesEmprestados = 0;
    SemaphoreHandle_t trava;
    SemaphoreHandle_t chegou;         /* da a vez a quem espera para receber */
    SemaphoreHandle_t liberou;        /* da a vez a quem espera para mandar */
};

/* O espaco que um item ocupa: 8 bytes de cabecalho e o tamanho alinhado em 4. */
size_t ocupa(const Anel *a, size_t n) { return a->tipo == RINGBUF_TYPE_BYTEBUF ? n : 8 + ((n + 3) & ~(size_t)3); }

Anel *de(RingbufHandle_t h) { return static_cast<Anel *>(h); }

/* Espera ate "pode" ficar verdadeiro, no maximo "ticks" (o sinal pode chegar
 * antes do que se espera: confere de novo). */
template <class F> bool esperar(Anel *a, SemaphoreHandle_t sinal, TickType_t ticks, F pode)
{
    TickType_t inicio = xTaskGetTickCount();
    for (;;) {
        xSemaphoreTake(a->trava, portMAX_DELAY);
        if (pode()) return true;   /* com a trava */
        xSemaphoreGive(a->trava);
        if (ticks == 0) return false;
        TickType_t passou = xTaskGetTickCount() - inicio;
        if (ticks != portMAX_DELAY && passou >= ticks) return false;
        xSemaphoreTake(sinal, ticks == portMAX_DELAY ? portMAX_DELAY : ticks - passou);
    }
}

void avisar(SemaphoreHandle_t sinal) { xSemaphoreGive(sinal); }

}  // namespace

extern "C" RingbufHandle_t xRingbufferCreate(size_t xBufferSize, RingbufferType_t xBufferType)
{
    if (xBufferType >= RINGBUF_TYPE_MAX || xBufferSize < 4) return nullptr;
    auto *a = new Anel();
    a->tipo = xBufferType;
    a->capacidade = xBufferType == RINGBUF_TYPE_BYTEBUF ? xBufferSize : (xBufferSize & ~(size_t)3);
    a->trava = xSemaphoreCreateMutex();
    a->chegou = xSemaphoreCreateBinary();
    a->liberou = xSemaphoreCreateBinary();
    return a;
}

extern "C" RingbufHandle_t xRingbufferCreateNoSplit(size_t xItemSize, size_t xItemNum)
{
    return xRingbufferCreate((((xItemSize + 3) & ~(size_t)3) + 8) * xItemNum, RINGBUF_TYPE_NOSPLIT);
}

extern "C" RingbufHandle_t xRingbufferCreateStatic(size_t xBufferSize, RingbufferType_t xBufferType, uint8_t *pucRingbufferStorage,
                                                   StaticRingbuffer_t *pxStaticRingbuffer)
{
    (void)pucRingbufferStorage;
    (void)pxStaticRingbuffer;
    return xRingbufferCreate(xBufferSize, xBufferType);
}

extern "C" RingbufHandle_t xRingbufferCreateWithCaps(size_t xBufferSize, RingbufferType_t xBufferType, UBaseType_t uxMemoryCaps)
{
    (void)uxMemoryCaps;
    return xRingbufferCreate(xBufferSize, xBufferType);
}

extern "C" void vRingbufferDelete(RingbufHandle_t h)
{
    Anel *a = de(h);
    if (!a) return;
    vSemaphoreDelete(a->trava);
    vSemaphoreDelete(a->chegou);
    vSemaphoreDelete(a->liberou);
    delete a;
}
extern "C" void vRingbufferDeleteWithCaps(RingbufHandle_t h) { vRingbufferDelete(h); }

extern "C" size_t xRingbufferGetMaxItemSize(RingbufHandle_t h)
{
    Anel *a = de(h);
    if (!a) return 0;
    if (a->tipo == RINGBUF_TYPE_BYTEBUF) return a->capacidade;
    if (a->tipo == RINGBUF_TYPE_ALLOWSPLIT) return a->capacidade - 8;
    return a->capacidade / 2 - 8;
}

extern "C" size_t xRingbufferGetCurFreeSize(RingbufHandle_t h)
{
    Anel *a = de(h);
    if (!a) return 0;
    xSemaphoreTake(a->trava, portMAX_DELAY);
    size_t livre = a->capacidade - a->usado;
    xSemaphoreGive(a->trava);
    if (a->tipo != RINGBUF_TYPE_BYTEBUF) livre = livre >= 8 ? livre - 8 : 0;
    return livre;
}

extern "C" BaseType_t xRingbufferSend(RingbufHandle_t h, const void *pvItem, size_t xItemSize, TickType_t xTicksToWait)
{
    Anel *a = de(h);
    if (!a || (!pvItem && xItemSize)) return pdFALSE;
    if (a->tipo != RINGBUF_TYPE_BYTEBUF && xItemSize > xRingbufferGetMaxItemSize(h)) return pdFALSE;
    if (a->tipo == RINGBUF_TYPE_BYTEBUF && xItemSize > a->capacidade) return pdFALSE;
    size_t precisa = ocupa(a, xItemSize);
    if (!esperar(a, a->liberou, xTicksToWait, [&] { return a->capacidade - a->usado >= precisa; })) return pdFALSE;
    const uint8_t *p = (const uint8_t *)pvItem;
    if (a->tipo == RINGBUF_TYPE_BYTEBUF)
        a->bytes.insert(a->bytes.end(), p, p + xItemSize);
    else
        a->itens.push_back(Item{std::vector<uint8_t>(p, p + xItemSize)});
    a->usado += precisa;
    xSemaphoreGive(a->trava);
    avisar(a->chegou);
    return pdTRUE;
}

extern "C" BaseType_t xRingbufferSendFromISR(RingbufHandle_t h, const void *pvItem, size_t xItemSize, BaseType_t *pxHigherPriorityTaskWoken)
{
    if (pxHigherPriorityTaskWoken) *pxHigherPriorityTaskWoken = pdFALSE;
    return xRingbufferSend(h, pvItem, xItemSize, 0);
}

/* O SendAcquire/SendComplete: o espaco e reservado, o programa escreve nele,
 * e so entao o item fica visivel para quem recebe. */
extern "C" BaseType_t xRingbufferSendAcquire(RingbufHandle_t h, void **ppvItem, size_t xItemSize, TickType_t xTicksToWait)
{
    Anel *a = de(h);
    if (!a || !ppvItem || a->tipo != RINGBUF_TYPE_NOSPLIT || xItemSize > xRingbufferGetMaxItemSize(h)) return pdFALSE;
    size_t precisa = ocupa(a, xItemSize);
    if (!esperar(a, a->liberou, xTicksToWait, [&] { return a->capacidade - a->usado >= precisa; })) return pdFALSE;
    a->itens.push_back(Item{std::vector<uint8_t>(xItemSize), true});
    a->usado += precisa;
    *ppvItem = a->itens.back().dados.data();
    xSemaphoreGive(a->trava);
    return pdTRUE;
}

extern "C" BaseType_t xRingbufferSendComplete(RingbufHandle_t h, void *pvItem)
{
    Anel *a = de(h);
    if (!a) return pdFALSE;
    xSemaphoreTake(a->trava, portMAX_DELAY);
    for (auto &i : a->itens)
        if (i.dados.data() == pvItem) i.emprestado = false;
    xSemaphoreGive(a->trava);
    avisar(a->chegou);
    return pdTRUE;
}

static void *receber(Anel *a, size_t *tamanho, TickType_t ticks, size_t maximo)
{
    if (a->tipo == RINGBUF_TYPE_BYTEBUF) {
        if (!esperar(a, a->chegou, ticks, [&] { return !a->bytes.empty(); })) return nullptr;
        size_t n = a->bytes.size() < maximo ? a->bytes.size() : maximo;
        a->emprestados.emplace_back(a->bytes.begin(), a->bytes.begin() + n);
        a->bytes.erase(a->bytes.begin(), a->bytes.begin() + n);
        a->bytesEmprestados += n;
        void *p = a->emprestados.back().data();
        if (tamanho) *tamanho = n;
        xSemaphoreGive(a->trava);
        return p;
    }
    auto pronto = [&] {
        for (auto &i : a->itens)
            if (!i.emprestado) return true;
        return false;
    };
    if (!esperar(a, a->chegou, ticks, pronto)) return nullptr;
    for (auto &i : a->itens)
        if (!i.emprestado) {
            i.emprestado = true;
            if (tamanho) *tamanho = i.dados.size();
            void *p = i.dados.data();
            xSemaphoreGive(a->trava);
            return p;
        }
    xSemaphoreGive(a->trava);
    return nullptr;
}

extern "C" void *xRingbufferReceive(RingbufHandle_t h, size_t *pxItemSize, TickType_t xTicksToWait)
{
    Anel *a = de(h);
    return a ? receber(a, pxItemSize, xTicksToWait, (size_t)-1) : nullptr;
}
extern "C" void *xRingbufferReceiveFromISR(RingbufHandle_t h, size_t *pxItemSize) { return xRingbufferReceive(h, pxItemSize, 0); }

extern "C" void *xRingbufferReceiveUpTo(RingbufHandle_t h, size_t *pxItemSize, TickType_t xTicksToWait, size_t xMaxSize)
{
    Anel *a = de(h);
    if (!a || a->tipo != RINGBUF_TYPE_BYTEBUF || xMaxSize == 0) return nullptr;
    return receber(a, pxItemSize, xTicksToWait, xMaxSize);
}
extern "C" void *xRingbufferReceiveUpToFromISR(RingbufHandle_t h, size_t *pxItemSize, size_t xMaxSize)
{
    return xRingbufferReceiveUpTo(h, pxItemSize, 0, xMaxSize);
}

/* Aqui o item nunca e guardado partido: a cabeca e o item inteiro. */
extern "C" BaseType_t xRingbufferReceiveSplit(RingbufHandle_t h, void **ppvHeadItem, void **ppvTailItem, size_t *pxHeadItemSize,
                                              size_t *pxTailItemSize, TickType_t xTicksToWait)
{
    Anel *a = de(h);
    if (!a || a->tipo != RINGBUF_TYPE_ALLOWSPLIT || !ppvHeadItem || !ppvTailItem) return pdFALSE;
    *ppvHeadItem = receber(a, pxHeadItemSize, xTicksToWait, (size_t)-1);
    *ppvTailItem = nullptr;
    if (pxTailItemSize) *pxTailItemSize = 0;
    return *ppvHeadItem ? pdTRUE : pdFALSE;
}
extern "C" BaseType_t xRingbufferReceiveSplitFromISR(RingbufHandle_t h, void **ppvHeadItem, void **ppvTailItem, size_t *pxHeadItemSize,
                                                     size_t *pxTailItemSize)
{
    return xRingbufferReceiveSplit(h, ppvHeadItem, ppvTailItem, pxHeadItemSize, pxTailItemSize, 0);
}

extern "C" void vRingbufferReturnItem(RingbufHandle_t h, void *pvItem)
{
    Anel *a = de(h);
    if (!a || !pvItem) return;
    xSemaphoreTake(a->trava, portMAX_DELAY);
    if (a->tipo == RINGBUF_TYPE_BYTEBUF) {
        for (auto i = a->emprestados.begin(); i != a->emprestados.end(); ++i)
            if (i->data() == pvItem) {
                a->usado -= i->size();
                a->bytesEmprestados -= i->size();
                a->emprestados.erase(i);
                break;
            }
    } else {
        for (auto i = a->itens.begin(); i != a->itens.end(); ++i)
            if (i->dados.data() == pvItem) {
                a->usado -= ocupa(a, i->dados.size());
                a->itens.erase(i);
                break;
            }
    }
    xSemaphoreGive(a->trava);
    avisar(a->liberou);
}
extern "C" void vRingbufferReturnItemFromISR(RingbufHandle_t h, void *pvItem, BaseType_t *pxHigherPriorityTaskWoken)
{
    if (pxHigherPriorityTaskWoken) *pxHigherPriorityTaskWoken = pdFALSE;
    vRingbufferReturnItem(h, pvItem);
}

extern "C" void vRingbufferGetInfo(RingbufHandle_t h, UBaseType_t *uxFree, UBaseType_t *uxRead, UBaseType_t *uxWrite,
                                   UBaseType_t *uxAcquire, UBaseType_t *uxItemsWaiting)
{
    Anel *a = de(h);
    if (!a) return;
    xSemaphoreTake(a->trava, portMAX_DELAY);
    if (uxFree) *uxFree = (UBaseType_t)a->usado;
    if (uxRead) *uxRead = 0;
    if (uxWrite) *uxWrite = (UBaseType_t)a->usado;
    if (uxAcquire) *uxAcquire = (UBaseType_t)a->usado;
    if (uxItemsWaiting) {
        if (a->tipo == RINGBUF_TYPE_BYTEBUF) {
            *uxItemsWaiting = (UBaseType_t)a->bytes.size();
        } else {
            UBaseType_t n = 0;
            for (auto &i : a->itens)
                if (!i.emprestado) n++;
            *uxItemsWaiting = n;
        }
    }
    xSemaphoreGive(a->trava);
}

extern "C" void xRingbufferPrintInfo(RingbufHandle_t h)
{
    Anel *a = de(h);
    if (!a) return;
    printf("Rb size:%u\tfree: %u\n", (unsigned)a->capacidade, (unsigned)(a->capacidade - a->usado));
}

extern "C" BaseType_t xRingbufferGetStaticBuffer(RingbufHandle_t h, uint8_t **ppucRingbufferStorage, StaticRingbuffer_t **ppxStaticRingbuffer)
{
    (void)h;
    if (ppucRingbufferStorage) *ppucRingbufferStorage = nullptr;
    if (ppxStaticRingbuffer) *ppxStaticRingbuffer = nullptr;
    return pdFALSE;
}

/* O conjunto de filas (para esperar o ringbuffer junto com outras filas): o
 * ringbuffer avisa o conjunto pelo semaforo de "chegou". */
extern "C" BaseType_t xRingbufferAddToQueueSetRead(RingbufHandle_t h, QueueSetHandle_t xQueueSet)
{
    (void)xQueueSet;
    return h ? pdTRUE : pdFALSE;
}
extern "C" BaseType_t xRingbufferRemoveFromQueueSetRead(RingbufHandle_t h, QueueSetHandle_t xQueueSet)
{
    (void)xQueueSet;
    return h ? pdTRUE : pdFALSE;
}

#endif
