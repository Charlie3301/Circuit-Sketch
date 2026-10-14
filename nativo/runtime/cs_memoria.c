/* cs_memoria.c -- a memoria da placa: a RAM interna e a PSRAM.
 *
 * Cada uma e uma regiao so, do tamanho da placa (com folga: no PC um
 * ponteiro tem 8 bytes, e na ESP32, 4). Pedir mais do que cabe devolve
 * NULL, como na placa -- o DOOM depende disso: quando o malloc falha ele
 * solta o cache de lumps e tenta de novo.
 *
 * O alocador e o classico de etiquetas nas pontas: cada bloco sabe o
 * proprio tamanho e o do vizinho de tras, entao soltar um bloco junta os
 * vizinhos livres na hora. Os livres ficam em listas por tamanho. Os
 * numeros que o codigo le (heap_caps_get_free_size) saem na escala da
 * placa. */
#include <stdint.h>
#include <stddef.h>
#include <string.h>
#include <stdio.h>
#include <stdarg.h>
#include <errno.h>
#include "esp_psram.h"
#include "esp_system.h"
#include "cs_host.h"
#include "cs_interno.h"
#include "esp_heap_caps.h"

#define ALINHAMENTO 16
#define CABECALHO 16
#define MINIMO 32
#define CLASSES 192
#define EM_USO 1

typedef struct bloco {
    size_t anterior;
    size_t tamanho;
} bloco;

typedef struct livre {
    bloco cab;
    struct livre *prox, *ant;
} livre;

typedef struct regiao {
    uint8_t *inicio, *fim;
    livre *classes[CLASSES];
    uint64_t mapa[3];
    size_t livres, minimo_livre, total;
    size_t tam_placa;
    size_t blocos_usados;
    cs_giro trava;
    int existe;
} regiao;

static regiao interna, psram;

static size_t tamanho_de(bloco *b) { return b->tamanho & ~(size_t)EM_USO; }
static int usado(bloco *b) { return (int)(b->tamanho & EM_USO); }
static bloco *seguinte(bloco *b) { return (bloco *)((uint8_t *)b + tamanho_de(b)); }

static int classe_de(size_t n)
{
    if (n < 1024) return (int)(n / 16);
    int log = 63 - __builtin_clzll((unsigned long long)n);
    int sub = (int)((n >> (log - 2)) & 3);
    int c = 64 + (log - 10) * 4 + sub;
    return c >= CLASSES ? CLASSES - 1 : c;
}

static void marcar(regiao *r, int c, int cheia)
{
    if (cheia) r->mapa[c >> 6] |= 1ULL << (c & 63);
    else r->mapa[c >> 6] &= ~(1ULL << (c & 63));
}

static void inserir(regiao *r, livre *l)
{
    int c = classe_de(tamanho_de(&l->cab));
    l->ant = NULL;
    l->prox = r->classes[c];
    if (l->prox) l->prox->ant = l;
    r->classes[c] = l;
    marcar(r, c, 1);
}

static void retirar(regiao *r, livre *l)
{
    int c = classe_de(tamanho_de(&l->cab));
    if (l->ant) l->ant->prox = l->prox;
    else r->classes[c] = l->prox;
    if (l->prox) l->prox->ant = l->ant;
    if (!r->classes[c]) marcar(r, c, 0);
}

static void preparar(regiao *r, void *base, size_t tam, size_t tam_placa)
{
    memset(r, 0, sizeof(*r));
    if (!base || tam < 4096) return;

    uintptr_t ini = ((uintptr_t)base + ALINHAMENTO - 1) & ~(uintptr_t)(ALINHAMENTO - 1);
    uintptr_t fim = ((uintptr_t)base + tam) & ~(uintptr_t)(ALINHAMENTO - 1);
    r->inicio = (uint8_t *)ini;
    r->fim = (uint8_t *)fim;
    r->tam_placa = tam_placa;
    r->existe = 1;

    /* Um bloco livre com tudo e, no fim, um bloco "em uso" de tamanho
     * zero: a sentinela que impede a juncao de passar do fim. */
    size_t util = (size_t)(fim - ini) - CABECALHO;
    livre *l = (livre *)r->inicio;
    l->cab.anterior = 0;
    l->cab.tamanho = util;
    bloco *sentinela = (bloco *)(r->inicio + util);
    sentinela->anterior = util;
    sentinela->tamanho = EM_USO;
    inserir(r, l);
    r->livres = r->minimo_livre = r->total = util;
}

static livre *procurar(regiao *r, size_t n)
{
    int c = classe_de(n);

    /* Na classe certa, o primeiro que couber. */
    for (livre *l = r->classes[c]; l; l = l->prox)
        if (tamanho_de(&l->cab) >= n) return l;

    /* Nas de cima, qualquer um cabe: o mapa diz qual tem. */
    for (int k = c + 1; k < CLASSES; k++) {
        uint64_t palavra = r->mapa[k >> 6] >> (k & 63);
        if (!palavra) {
            k = (k | 63);
            continue;
        }
        k += __builtin_ctzll(palavra);
        if (k < CLASSES && r->classes[k]) return r->classes[k];
    }
    return NULL;
}

/* Parte o bloco b (em uso, de tamanho >= n): o que sobra vira livre. */
static void partir(regiao *r, bloco *b, size_t n)
{
    size_t tam = tamanho_de(b);
    if (tam - n < MINIMO) return;

    b->tamanho = n | EM_USO;
    livre *resto = (livre *)((uint8_t *)b + n);
    resto->cab.anterior = n;
    resto->cab.tamanho = tam - n;
    bloco *depois = seguinte(&resto->cab);
    depois->anterior = tam - n;

    /* O resto pode encostar num livre: junta. */
    if (!usado(depois)) {
        retirar(r, (livre *)depois);
        resto->cab.tamanho += tamanho_de(depois);
        seguinte(&resto->cab)->anterior = resto->cab.tamanho;
    }
    inserir(r, resto);
    r->livres += tam - n;
}

static void *alocar_em(regiao *r, size_t pedido)
{
    if (!r->existe) return NULL;
    if (pedido > (size_t)1 << 40) return NULL;

    size_t n = (pedido + CABECALHO + ALINHAMENTO - 1) & ~(size_t)(ALINHAMENTO - 1);
    if (n < MINIMO) n = MINIMO;

    cs_girar(&r->trava);
    livre *l = procurar(r, n);
    if (!l) {
        cs_soltar(&r->trava);
        return NULL;
    }
    retirar(r, l);
    bloco *b = &l->cab;
    b->tamanho |= EM_USO;
    r->livres -= tamanho_de(b);
    partir(r, b, n);
    r->blocos_usados++;
    if (r->livres < r->minimo_livre) r->minimo_livre = r->livres;
    cs_soltar(&r->trava);
    return (uint8_t *)b + CABECALHO;
}

static regiao *regiao_de(void *p)
{
    uint8_t *q = (uint8_t *)p;
    if (interna.existe && q >= interna.inicio && q < interna.fim) return &interna;
    if (psram.existe && q >= psram.inicio && q < psram.fim) return &psram;
    return NULL;
}

static void soltar_em(regiao *r, void *p)
{
    bloco *b = (bloco *)((uint8_t *)p - CABECALHO);

    cs_girar(&r->trava);
    if (!usado(b)) {
        cs_soltar(&r->trava);
        cs_panico("abort()", "free(): o bloco ja estava livre (double free)");
    }
    size_t tam = tamanho_de(b);
    b->tamanho = tam;
    r->livres += tam;
    r->blocos_usados--;

    bloco *depois = seguinte(b);
    if (!usado(depois)) {
        retirar(r, (livre *)depois);
        b->tamanho += tamanho_de(depois);
    }
    if (b->anterior && (uint8_t *)b > r->inicio) {
        bloco *antes = (bloco *)((uint8_t *)b - b->anterior);
        if (!usado(antes)) {
            retirar(r, (livre *)antes);
            antes->tamanho += b->tamanho;
            b = antes;
        }
    }
    seguinte(b)->anterior = tamanho_de(b);
    inserir(r, (livre *)b);
    cs_soltar(&r->trava);
}

void cs_memoria_iniciar(void *base_interna, size_t tam_interna, size_t interna_placa,
                        void *base_psram, size_t tam_psram, size_t psram_placa)
{
    preparar(&interna, base_interna, tam_interna, interna_placa);
    preparar(&psram, base_psram, tam_psram, psram_placa);
}

/* O malloc da placa com PSRAM (CONFIG_SPIRAM_USE_MALLOC): ate 4 KB tenta a
 * interna primeiro; mais que isso, a PSRAM. Sem lugar numa, vai na outra. */
#define SEMPRE_INTERNA 4096

void *cs_alocar(size_t n, uint32_t caps)
{
    if (caps & MALLOC_CAP_SPIRAM) return alocar_em(&psram, n);
    if (caps & (MALLOC_CAP_INTERNAL | MALLOC_CAP_DMA | MALLOC_CAP_EXEC | MALLOC_CAP_IRAM_8BIT | MALLOC_CAP_RTCRAM))
        return alocar_em(&interna, n);

    void *p;
    if (n <= SEMPRE_INTERNA || !psram.existe) {
        p = alocar_em(&interna, n);
        if (!p) p = alocar_em(&psram, n);
    } else {
        p = alocar_em(&psram, n);
        if (!p) p = alocar_em(&interna, n);
    }
    return p;
}

void cs_liberar(void *p)
{
    if (!p) return;
    regiao *r = regiao_de(p);
    if (!r) {
        char detalhe[96];
        snprintf(detalhe, sizeof(detalhe), "free(): o ponteiro %p nao veio do malloc", p);
        cs_panico("abort()", detalhe);
    }
    soltar_em(r, p);
}

static size_t tamanho_util(void *p)
{
    bloco *b = (bloco *)((uint8_t *)p - CABECALHO);
    return tamanho_de(b) - CABECALHO;
}

/* ---------------------------------------------------------------------
 * stdlib */
void *malloc(size_t n)
{
    void *p = cs_alocar(n ? n : 1, 0);
    if (!p) errno = 12;
    return p;
}

void *calloc(size_t quantos, size_t tamanho)
{
    if (tamanho && quantos > ((size_t)-1) / tamanho) return NULL;
    size_t n = quantos * tamanho;
    void *p = cs_alocar(n ? n : 1, 0);
    if (p) memset(p, 0, n);
    return p;
}

static void *realocar(void *p, size_t n, uint32_t caps)
{
    if (!p) return cs_alocar(n ? n : 1, caps);
    if (n == 0) {
        cs_liberar(p);
        return NULL;
    }

    regiao *r = regiao_de(p);
    if (!r) return NULL;
    size_t atual = tamanho_util(p);
    if (n <= atual) return p;

    /* Cresce no lugar quando o vizinho da frente esta livre e cabe. */
    size_t precisa = (n + CABECALHO + ALINHAMENTO - 1) & ~(size_t)(ALINHAMENTO - 1);
    bloco *b = (bloco *)((uint8_t *)p - CABECALHO);
    cs_girar(&r->trava);
    bloco *depois = seguinte(b);
    if (!usado(depois) && tamanho_de(b) + tamanho_de(depois) >= precisa) {
        retirar(r, (livre *)depois);
        r->livres -= tamanho_de(depois);
        b->tamanho = (tamanho_de(b) + tamanho_de(depois)) | EM_USO;
        seguinte(b)->anterior = tamanho_de(b);
        partir(r, b, precisa);
        if (r->livres < r->minimo_livre) r->minimo_livre = r->livres;
        cs_soltar(&r->trava);
        return p;
    }
    cs_soltar(&r->trava);

    void *novo = cs_alocar(n, caps ? caps : (r == &psram ? MALLOC_CAP_SPIRAM : 0));
    if (!novo && caps == 0) novo = cs_alocar(n, 0);
    if (!novo) return NULL;
    memcpy(novo, p, atual);
    cs_liberar(p);
    return novo;
}

void *realloc(void *p, size_t n)
{
    return realocar(p, n, 0);
}

void free(void *p)
{
    cs_liberar(p);
}

void *aligned_alloc(size_t alinhamento, size_t n)
{
    return heap_caps_aligned_alloc(alinhamento, n, 0);
}

int posix_memalign(void **p, size_t alinhamento, size_t n)
{
    *p = heap_caps_aligned_alloc(alinhamento, n, 0);
    return *p ? 0 : 12;
}

void *memalign(size_t alinhamento, size_t n)
{
    return heap_caps_aligned_alloc(alinhamento, n, 0);
}

/* ---------------------------------------------------------------------
 * heap_caps */
void *heap_caps_malloc(size_t n, uint32_t caps)
{
    return cs_alocar(n ? n : 1, caps);
}

void *heap_caps_calloc(size_t quantos, size_t tamanho, uint32_t caps)
{
    if (tamanho && quantos > ((size_t)-1) / tamanho) return NULL;
    size_t n = quantos * tamanho;
    void *p = cs_alocar(n ? n : 1, caps);
    if (p) memset(p, 0, n);
    return p;
}

void *heap_caps_realloc(void *p, size_t n, uint32_t caps)
{
    return realocar(p, n, caps);
}

void heap_caps_free(void *p)
{
    cs_liberar(p);
}

/* Alinhamento maior que 16: pede de sobra e devolve o pedaco da frente. */
void *heap_caps_aligned_alloc(size_t alinhamento, size_t n, uint32_t caps)
{
    if (alinhamento <= ALINHAMENTO) return cs_alocar(n ? n : 1, caps);
    if (alinhamento & (alinhamento - 1)) return NULL;

    uint8_t *cru = (uint8_t *)cs_alocar(n + alinhamento + MINIMO, caps);
    if (!cru) return NULL;
    uintptr_t alvo = ((uintptr_t)cru + MINIMO + alinhamento - 1) & ~(uintptr_t)(alinhamento - 1);
    size_t frente = alvo - (uintptr_t)cru;

    regiao *r = regiao_de(cru);
    bloco *b = (bloco *)(cru - CABECALHO);
    cs_girar(&r->trava);
    size_t tam = tamanho_de(b);
    bloco *novo = (bloco *)((uint8_t *)b + frente);
    novo->anterior = frente;
    novo->tamanho = (tam - frente) | EM_USO;
    seguinte(novo)->anterior = tam - frente;
    b->tamanho = frente | EM_USO;
    r->blocos_usados++;
    cs_soltar(&r->trava);
    cs_liberar(cru);
    return (void *)alvo;
}

void heap_caps_aligned_free(void *p)
{
    cs_liberar(p);
}

void *heap_caps_malloc_prefer(size_t n, size_t quantos, ...)
{
    va_list ap;
    va_start(ap, quantos);
    void *p = NULL;
    for (size_t i = 0; i < quantos && !p; i++) {
        uint32_t caps = va_arg(ap, uint32_t);
        p = cs_alocar(n, caps);
    }
    va_end(ap);
    return p;
}

void *pvPortMalloc(size_t n)
{
    return cs_alocar(n ? n : 1, MALLOC_CAP_INTERNAL);
}

void vPortFree(void *p)
{
    cs_liberar(p);
}

static size_t na_placa(regiao *r, size_t v)
{
    if (!r->existe || !r->total) return 0;
    return (size_t)((double)v * (double)r->tam_placa / (double)r->total);
}

static size_t maior_livre(regiao *r)
{
    size_t maior = 0;
    cs_girar(&r->trava);
    for (int c = CLASSES - 1; c >= 0; c--) {
        for (livre *l = r->classes[c]; l; l = l->prox)
            if (tamanho_de(&l->cab) > maior) maior = tamanho_de(&l->cab);
        if (maior) break;
    }
    cs_soltar(&r->trava);
    return maior > CABECALHO ? maior - CABECALHO : 0;
}

static int da_regiao(regiao *r, uint32_t caps)
{
    if (caps & MALLOC_CAP_SPIRAM) return r == &psram;
    if (caps & (MALLOC_CAP_INTERNAL | MALLOC_CAP_DMA | MALLOC_CAP_EXEC)) return r == &interna;
    return 1;
}

size_t heap_caps_get_free_size(uint32_t caps)
{
    size_t n = 0;
    if (da_regiao(&interna, caps)) n += na_placa(&interna, interna.livres);
    if (da_regiao(&psram, caps)) n += na_placa(&psram, psram.livres);
    return n;
}

size_t heap_caps_get_total_size(uint32_t caps)
{
    size_t n = 0;
    if (da_regiao(&interna, caps) && interna.existe) n += interna.tam_placa;
    if (da_regiao(&psram, caps) && psram.existe) n += psram.tam_placa;
    return n;
}

size_t heap_caps_get_minimum_free_size(uint32_t caps)
{
    size_t n = 0;
    if (da_regiao(&interna, caps)) n += na_placa(&interna, interna.minimo_livre);
    if (da_regiao(&psram, caps)) n += na_placa(&psram, psram.minimo_livre);
    return n;
}

size_t heap_caps_get_largest_free_block(uint32_t caps)
{
    size_t a = da_regiao(&interna, caps) ? maior_livre(&interna) : 0;
    size_t b = da_regiao(&psram, caps) ? maior_livre(&psram) : 0;
    size_t maior = a > b ? a : b;
    size_t teto = heap_caps_get_free_size(caps);
    return maior < teto ? maior : teto;
}

void heap_caps_get_info(multi_heap_info_t *info, uint32_t caps)
{
    memset(info, 0, sizeof(*info));
    info->total_free_bytes = heap_caps_get_free_size(caps);
    info->total_allocated_bytes = heap_caps_get_total_size(caps) - info->total_free_bytes;
    info->largest_free_block = heap_caps_get_largest_free_block(caps);
    info->minimum_free_bytes = heap_caps_get_minimum_free_size(caps);
    if (da_regiao(&interna, caps)) info->allocated_blocks += interna.blocos_usados;
    if (da_regiao(&psram, caps)) info->allocated_blocks += psram.blocos_usados;
    info->total_blocks = info->allocated_blocks;
}

void heap_caps_print_heap_info(uint32_t caps)
{
    multi_heap_info_t info;
    heap_caps_get_info(&info, caps);
    printf("Heap summary for capabilities 0x%08lX:\n", (unsigned long)caps);
    printf("  Totals:\n");
    printf("    free %u allocated %u min_free %u largest_free_block %u\n",
           (unsigned)info.total_free_bytes, (unsigned)info.total_allocated_bytes,
           (unsigned)info.minimum_free_bytes, (unsigned)info.largest_free_block);
}

static int integra(regiao *r, int imprimir)
{
    if (!r->existe) return 1;
    int ok = 1;
    cs_girar(&r->trava);
    bloco *b = (bloco *)r->inicio;
    size_t antes = 0;
    while (tamanho_de(b) != 0) {
        if (b->anterior != antes || (uint8_t *)b >= r->fim) {
            ok = 0;
            break;
        }
        antes = tamanho_de(b);
        b = seguinte(b);
    }
    cs_soltar(&r->trava);
    if (!ok && imprimir) printf("CORRUPT HEAP: bloco em %p\n", (void *)b);
    return ok;
}

int heap_caps_check_integrity_all(int imprimir)
{
    return integra(&interna, imprimir) && integra(&psram, imprimir);
}

int heap_caps_check_integrity(uint32_t caps, int imprimir)
{
    int ok = 1;
    if (da_regiao(&interna, caps)) ok &= integra(&interna, imprimir);
    if (da_regiao(&psram, caps)) ok &= integra(&psram, imprimir);
    return ok;
}

/* ---------------------------------------------------------------------
 * esp_system / esp_psram */
uint32_t esp_get_free_heap_size(void)
{
    return (uint32_t)heap_caps_get_free_size(MALLOC_CAP_DEFAULT);
}

uint32_t esp_get_free_internal_heap_size(void)
{
    return (uint32_t)heap_caps_get_free_size(MALLOC_CAP_INTERNAL);
}

uint32_t esp_get_minimum_free_heap_size(void)
{
    return (uint32_t)heap_caps_get_minimum_free_size(MALLOC_CAP_DEFAULT);
}

size_t esp_psram_get_size(void)
{
    return psram.existe ? psram.tam_placa : 0;
}

bool esp_psram_is_initialized(void)
{
    return psram.existe;
}

esp_err_t esp_psram_init(void)
{
    return psram.existe ? 0 : 0x103;
}
