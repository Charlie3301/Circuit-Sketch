/* dap_ape.c -- o Monkey's Audio (APE), sem perdas.
 *
 * O decodificador e o libdemac (Dave Chapman, do Rockbox; GPL 2 ou mais
 * nova), em src/ape: ponto fixo, feito para tocador portatil, e le os
 * arquivos das versoes 3.97 a 3.99 -- o formato que o Monkey's Audio grava
 * ate hoje. O cabecalho e lido de um buffer (sem o read/lseek do POSIX), os
 * quadros vem do arquivo aos pedacos de 32 KB, e a busca vai pela tabela de
 * quadros do proprio arquivo. A etiqueta ID3v2 no comeco (o "junk") e pulada.
 *
 * Este arquivo vai sob a GNU GPL versao 2 ou mais nova, como o libdemac.
 */
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include "ape/demac.h"
#include "ape/parser.h"
#include "dap_interno.h"

#define APE_BLOCOS  4608           /* amostras por pedaco decodificado */
#define APE_ENTRADA (32 * 1024)    /* o que cabe de comprimido por vez */

typedef struct {
    FILE *f;
    struct ape_ctx_t ctx;
    uint32_t lixo;                 /* bytes antes do "MAC " (o ID3v2) */
    uint32_t *tabela;              /* onde comeca cada quadro (relativo ao "MAC ") */
    uint32_t pontos;
    unsigned char *entrada;
    int na_entrada, primeiro_byte;
    int32_t *d0, *d1;
    int prontos, usados;           /* amostras decodificadas e ja entregues */
    uint32_t quadro;
    int restam_no_quadro;
    uint32_t pular;                /* depois de uma busca: amostras a jogar fora */
    int fim;
    float escala;
} ape_t;

static void encher(ape_t *a)
{
    size_t n = fread(a->entrada + a->na_entrada, 1, (size_t)(APE_ENTRADA - a->na_entrada), a->f);
    a->na_entrada += (int)n;
    /* o decodificador le alguns bytes adiante: o fim do arquivo vira zeros */
    if (a->na_entrada < APE_ENTRADA) memset(a->entrada + a->na_entrada, 0, (size_t)(APE_ENTRADA - a->na_entrada));
}

static void consumir(ape_t *a, int bytes)
{
    if (bytes > a->na_entrada) bytes = a->na_entrada;
    memmove(a->entrada, a->entrada + bytes, (size_t)(a->na_entrada - bytes));
    a->na_entrada -= bytes;
    encher(a);
}

static int comecar_quadro(ape_t *a)
{
    if (a->quadro >= a->ctx.totalframes) { a->fim = 1; return 0; }
    int blocos = a->quadro == a->ctx.totalframes - 1 ? (int)a->ctx.finalframeblocks : (int)a->ctx.blocksperframe;
    a->ctx.currentframeblocks = blocos;
    a->restam_no_quadro = blocos;
    int consumidos = 0;
    init_frame_decoder(&a->ctx, a->entrada, &a->primeiro_byte, &consumidos);
    consumir(a, consumidos);
    return 1;
}

/* Posiciona no comeco do quadro n (o fluxo do APE e alinhado em 32 bits). */
static int ir_ao_quadro(ape_t *a, uint32_t n)
{
    if (n >= a->ctx.totalframes || n >= a->pontos) return 0;
    uint32_t rel = a->tabela[n];
    a->primeiro_byte = 3 - (int)(rel & 3);
    if (fseek(a->f, (long)(a->lixo + (rel & ~3u)), SEEK_SET) != 0) return 0;
    a->na_entrada = 0;
    encher(a);
    a->quadro = n;
    a->prontos = a->usados = 0;
    a->fim = 0;
    return comecar_quadro(a);
}

static int decodificar(ape_t *a)
{
    while (!a->fim) {
        if (a->restam_no_quadro <= 0) {
            a->quadro++;
            if (!comecar_quadro(a)) return 0;
        }
        int n = a->restam_no_quadro < APE_BLOCOS ? a->restam_no_quadro : APE_BLOCOS, consumidos = 0;
        if (decode_chunk(&a->ctx, a->entrada, &a->primeiro_byte, &consumidos, a->d0, a->d1, n) < 0) {
            a->fim = 1;
            return 0;
        }
        consumir(a, consumidos);
        a->restam_no_quadro -= n;
        a->prontos = n;
        a->usados = 0;
        if (a->pular) {
            uint32_t fora = a->pular < (uint32_t)n ? a->pular : (uint32_t)n;
            a->usados = (int)fora;
            a->pular -= fora;
        }
        if (a->usados < a->prontos) return 1;
    }
    return 0;
}

static void ape_fechar(void *s)
{
    ape_t *a = (ape_t *)s;
    if (!a) return;
    if (a->f) fclose(a->f);
    free(a->tabela);
    free(a->entrada);
    free(a->d0);
    free(a->d1);
    free(a);
}

/* O tamanho do ID3v2 no comeco (0 se nao tem). */
static uint32_t tamanho_do_id3(const unsigned char *b)
{
    if (memcmp(b, "ID3", 3) != 0) return 0;
    uint32_t t = ((uint32_t)(b[6] & 0x7F) << 21) | ((uint32_t)(b[7] & 0x7F) << 14) | ((uint32_t)(b[8] & 0x7F) << 7) | (b[9] & 0x7F);
    return t + 10 + ((b[5] & 0x10) ? 10 : 0);
}

static void *ape_abrir(const char *caminho, dap_info_audio_t *info, char *erro, int tam_erro)
{
    ape_t *a = (ape_t *)calloc(1, sizeof(ape_t));
    if (!a) return NULL;
    a->f = fopen(caminho, "rb");
    unsigned char cab[1024];
    if (!a->f) { snprintf(erro, tam_erro, "o APE nao abre"); ape_fechar(a); return NULL; }

    memset(cab, 0, sizeof(cab));
    fread(cab, 1, sizeof(cab), a->f);
    a->lixo = tamanho_do_id3(cab);
    if (a->lixo) {
        fseek(a->f, (long)a->lixo, SEEK_SET);
        memset(cab, 0, sizeof(cab));
        fread(cab, 1, sizeof(cab), a->f);
    }
    if (ape_parseheaderbuf(cab, &a->ctx) < 0) {
        snprintf(erro, tam_erro, "nao e um arquivo do Monkey's Audio");
        ape_fechar(a);
        return NULL;
    }
    if (a->ctx.fileversion < APE_MIN_VERSION || a->ctx.fileversion > APE_MAX_VERSION) {
        snprintf(erro, tam_erro, "APE da versao %d.%02d: o player toca de 3.97 a 3.99",
                 a->ctx.fileversion / 1000, (a->ctx.fileversion % 1000) / 10);
        ape_fechar(a);
        return NULL;
    }
    if (a->ctx.channels < 1 || a->ctx.channels > 2 || a->ctx.bps < 8 || a->ctx.bps > 24 || a->ctx.totalframes == 0) {
        snprintf(erro, tam_erro, "o APE tem um formato que o player nao toca (%d canais, %d bits)", a->ctx.channels, a->ctx.bps);
        ape_fechar(a);
        return NULL;
    }

    /* A tabela de quadros. */
    a->pontos = a->ctx.seektablelength / 4;
    if (a->pontos < a->ctx.totalframes) a->pontos = a->ctx.totalframes;
    a->tabela = (uint32_t *)dap_alocar((size_t)a->pontos * sizeof(uint32_t));
    a->entrada = (unsigned char *)dap_alocar(APE_ENTRADA + 16);
    a->d0 = (int32_t *)dap_alocar(APE_BLOCOS * sizeof(int32_t));
    a->d1 = (int32_t *)dap_alocar(APE_BLOCOS * sizeof(int32_t));
    if (!a->tabela || !a->entrada || !a->d0 || !a->d1) {
        snprintf(erro, tam_erro, "sem memoria para o APE");
        ape_fechar(a);
        return NULL;
    }
    memset(a->tabela, 0, (size_t)a->pontos * sizeof(uint32_t));
    fseek(a->f, (long)(a->lixo + a->ctx.seektablefilepos), SEEK_SET);
    unsigned char quatro[4];
    for (uint32_t i = 0; i < a->ctx.seektablelength / 4 && i < a->pontos; i++) {
        if (fread(quatro, 1, 4, a->f) != 4) break;
        a->tabela[i] = (uint32_t)quatro[0] | ((uint32_t)quatro[1] << 8) | ((uint32_t)quatro[2] << 16) | ((uint32_t)quatro[3] << 24);
    }
    if (a->ctx.seektablelength == 0) a->tabela[0] = a->ctx.firstframe;

    a->escala = 1.0f / (float)(1u << (a->ctx.bps - 1));
    if (!ir_ao_quadro(a, 0)) {
        snprintf(erro, tam_erro, "o APE nao tem o primeiro quadro");
        ape_fechar(a);
        return NULL;
    }
    info->taxa = (int)a->ctx.samplerate;
    info->canais = a->ctx.channels;
    info->bits = a->ctx.bps;
    info->quadros = a->ctx.totalsamples;
    return a;
}

static int ape_ler(void *s, float *estereo, int quadros)
{
    ape_t *a = (ape_t *)s;
    int feitos = 0;
    while (feitos < quadros) {
        if (a->usados >= a->prontos && !decodificar(a)) break;
        int n = a->prontos - a->usados;
        if (n > quadros - feitos) n = quadros - feitos;
        for (int i = 0; i < n; i++) {
            int32_t e = a->d0[a->usados + i];
            int32_t d = a->ctx.channels == 2 ? a->d1[a->usados + i] : e;
            estereo[2 * (feitos + i)] = (float)e * a->escala;
            estereo[2 * (feitos + i) + 1] = (float)d * a->escala;
        }
        a->usados += n;
        feitos += n;
    }
    return feitos;
}

static int ape_buscar(void *s, uint64_t quadro)
{
    ape_t *a = (ape_t *)s;
    if (a->ctx.blocksperframe == 0) return 0;
    uint32_t n = (uint32_t)(quadro / a->ctx.blocksperframe);
    if (n >= a->ctx.totalframes) n = a->ctx.totalframes - 1;
    if (!ir_ao_quadro(a, n)) return 0;
    a->pular = (uint32_t)(quadro - (uint64_t)n * a->ctx.blocksperframe);
    return 1;
}

const dap_formato_t dap_formato_ape = { ape_abrir, ape_ler, ape_buscar, ape_fechar };
