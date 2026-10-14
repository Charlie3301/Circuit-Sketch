/* dap_opus.c -- o Opus no Ogg (.opus, RFC 7845).
 *
 * O decodificador e o da libopus (Xiph.Org, licenca BSD), em src/opus. O Ogg
 * e lido aqui: paginas ("OggS", a tabela de segmentos e o corpo) que viram
 * pacotes -- um segmento de 255 bytes continua o pacote, um menor termina.
 * O primeiro pacote e o OpusHead (canais, o "pre-skip" que se joga fora no
 * comeco, o ganho de saida e o mapeamento dos canais); o segundo, o OpusTags.
 * Depois vem o som, sempre a 48 kHz: o granulo de cada pagina diz em que
 * amostra termina o ultimo pacote dela, e o da ultima pagina corta o fim.
 *
 * Buscar e por bissecao no arquivo, pelo granulo das paginas; o decodificador
 * comeca 80 ms antes do ponto, para assentar, e essa parte nao sai.
 */
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <math.h>
#include "opus/opus.h"
#include "opus/opus_multistream.h"
#include "dap_interno.h"

#define TAXA_OPUS 48000
#define QUADROS_MAX 5760          /* 120 ms a 48 kHz: o maior pacote */
#define PRE_ROLL 3840             /* 80 ms: o que o decodificador leva para assentar */
#define SEM_GRANULO 0xFFFFFFFFFFFFFFFFull

typedef struct {
    FILE *f;
    long dados, tamanho;          /* onde o som comeca (depois dos cabecalhos) e o fim do arquivo */
    uint32_t serie, serie_lida;   /* o numero de serie do Opus no Ogg, e o da ultima pagina lida */
    OpusDecoder *dec;
    OpusMSDecoder *ms;
    int canais;                   /* os do arquivo */
    int pre_skip;
    float ganho;
    uint64_t fim48;               /* o granulo da ultima pagina (0: nao sabe) */
    uint64_t pos48;               /* o granulo onde o ultimo pacote decodificado termina */
    uint64_t alvo48;              /* daqui para tras, nada sai (o pre-skip, o assentar da busca) */
    /* a pagina atual */
    uint8_t seg[255];
    int nseg, iseg;
    uint8_t *corpo;
    int pcorpo;
    uint64_t granulo_pagina;
    int pagina_final;             /* a ultima do fluxo: o granulo dela e o ponto de corte do fim */
    int pular_continuacao;        /* depois de buscar: o pedaco que veio da pagina anterior nao serve */
    /* o pacote */
    uint8_t *pacote;
    int tpacote, cap_pacote;
    uint64_t granulo_pacote;      /* o fim do pacote, quando ele e o ultimo da pagina; senao SEM_GRANULO */
    /* o som decodificado, esperando para sair */
    float *pcm;                   /* QUADROS_MAX x canais */
    int n_pcm, j_pcm;
} opus_t;

static uint32_t le32o(const uint8_t *p) { return (uint32_t)p[0] | (uint32_t)p[1] << 8 | (uint32_t)p[2] << 16 | (uint32_t)p[3] << 24; }
static uint64_t le64o(const uint8_t *p) { return (uint64_t)le32o(p) | (uint64_t)le32o(p + 4) << 32; }

/* Leva o arquivo ate o proximo "OggS", lendo em blocos. 0 se nao ha mais. */
static int sincronizar(opus_t *o)
{
    uint8_t b[512];
    for (;;) {
        long aqui = ftell(o->f);
        int n = (int)fread(b, 1, sizeof(b), o->f);
        if (n < 4) return 0;
        for (int i = 0; i + 4 <= n; i++)
            if (b[i] == 'O' && memcmp(b + i, "OggS", 4) == 0) return fseek(o->f, aqui + i, SEEK_SET) == 0;
        if (fseek(o->f, aqui + n - 3, SEEK_SET) != 0) return 0;
    }
}

/* A proxima pagina do Opus: acha o "OggS" e le o corpo. 0 no fim. */
static int ler_pagina(opus_t *o, int *tipo)
{
    uint8_t h[27];
    for (;;) {
        if (!sincronizar(o) || fread(h, 1, 27, o->f) != 27) return 0;
        int nseg = h[26];
        if ((int)fread(o->seg, 1, (size_t)nseg, o->f) != nseg) return 0;
        int t = 0;
        for (int i = 0; i < nseg; i++) t += o->seg[i];
        if ((int)fread(o->corpo, 1, (size_t)t, o->f) != t) return 0;
        o->serie_lida = le32o(h + 14);
        if (o->serie && o->serie_lida != o->serie) continue;   /* outra trilha do mesmo Ogg */
        o->nseg = nseg;
        o->iseg = 0;
        o->pcorpo = 0;
        o->granulo_pagina = le64o(h + 6);
        o->pagina_final = (h[5] & 4) != 0;
        *tipo = h[5];
        return 1;
    }
}

/* O proximo pacote inteiro, juntando os segmentos (e as paginas). 0 no fim. */
static int proximo_pacote(opus_t *o)
{
    o->tpacote = 0;
    for (;;) {
        if (o->iseg >= o->nseg) {
            int tipo;
            if (!ler_pagina(o, &tipo)) return 0;
            if (o->pular_continuacao) {
                /* o comeco desta pagina e o fim de um pacote que comecou antes */
                if (tipo & 1)
                    while (o->iseg < o->nseg) {
                        int s = o->seg[o->iseg++];
                        o->pcorpo += s;
                        if (s < 255) break;
                    }
                o->pular_continuacao = 0;
            }
            continue;
        }
        int s = o->seg[o->iseg++];
        if (o->tpacote + s > o->cap_pacote) {
            int novo = (o->tpacote + s) * 2;
            uint8_t *p = (uint8_t *)dap_realocar(o->pacote, (size_t)novo);
            if (!p) return 0;
            o->pacote = p;
            o->cap_pacote = novo;
        }
        memcpy(o->pacote + o->tpacote, o->corpo + o->pcorpo, (size_t)s);
        o->pcorpo += s;
        o->tpacote += s;
        if (s < 255) {
            /* o ultimo pacote da pagina termina no granulo dela -- menos na
             * ultima pagina, onde o granulo e onde o som acaba (o resto do
             * pacote sai fora) */
            o->granulo_pacote = o->iseg == o->nseg && !o->pagina_final ? o->granulo_pagina : SEM_GRANULO;
            return 1;
        }
    }
}

/* O granulo da ultima pagina do arquivo: o fim do som. */
static uint64_t ultimo_granulo(opus_t *o)
{
    uint8_t *b = (uint8_t *)dap_alocar(65536);
    if (!b) return 0;
    uint64_t g = 0;
    long de = o->tamanho > 65536 ? o->tamanho - 65536 : 0;
    if (fseek(o->f, de, SEEK_SET) == 0) {
        int n = (int)fread(b, 1, 65536, o->f);
        for (int i = n - 27; i >= 0; i--)
            if (memcmp(b + i, "OggS", 4) == 0 && le32o(b + i + 14) == o->serie) {
                uint64_t x = le64o(b + i + 6);
                if (x != SEM_GRANULO) { g = x; break; }
            }
    }
    free(b);
    return g;
}

static void opus_fechar(void *s)
{
    opus_t *o = (opus_t *)s;
    if (!o) return;
    if (o->dec) opus_decoder_destroy(o->dec);
    if (o->ms) opus_multistream_decoder_destroy(o->ms);
    if (o->f) fclose(o->f);
    free(o->corpo);
    free(o->pacote);
    free(o->pcm);
    free(o);
}

static void *opus_abrir(const char *caminho, dap_info_audio_t *info, char *erro, int tam_erro)
{
    opus_t *o = (opus_t *)calloc(1, sizeof(opus_t));
    if (!o) return NULL;
    o->f = fopen(caminho, "rb");
    o->corpo = (uint8_t *)dap_alocar(255 * 255);
    if (!o->f || !o->corpo) { opus_fechar(o); return NULL; }
    fseek(o->f, 0, SEEK_END);
    o->tamanho = ftell(o->f);
    fseek(o->f, 0, SEEK_SET);

    /* o OpusHead: a primeira pagina tem so ele */
    if (!proximo_pacote(o) || o->tpacote < 19 || memcmp(o->pacote, "OpusHead", 8) != 0) {
        snprintf(erro, tam_erro, "nao e um Opus (falta o OpusHead)");
        opus_fechar(o);
        return NULL;
    }
    o->serie = o->serie_lida;
    const uint8_t *h = o->pacote;
    o->canais = h[9];
    o->pre_skip = h[10] | h[11] << 8;
    int16_t ganho = (int16_t)(h[16] | h[17] << 8);
    o->ganho = powf(10.0f, ganho / (20.0f * 256.0f));
    int familia = h[18], e = OPUS_OK;
    if (familia == 0 && (o->canais == 1 || o->canais == 2))
        o->dec = opus_decoder_create(TAXA_OPUS, o->canais, &e);
    else if (familia != 0 && o->tpacote >= 21 + o->canais && o->canais >= 1 && o->canais <= 8)
        o->ms = opus_multistream_decoder_create(TAXA_OPUS, o->canais, h[19], h[20], h + 21, &e);
    else
        e = OPUS_UNIMPLEMENTED;
    if (e != OPUS_OK || (!o->dec && !o->ms)) {
        snprintf(erro, tam_erro, "o Opus tem um mapeamento de canais que nao abre");
        opus_fechar(o);
        return NULL;
    }

    /* o OpusTags (pode ocupar varias paginas); o som comeca na pagina seguinte */
    if (!proximo_pacote(o) || o->tpacote < 8 || memcmp(o->pacote, "OpusTags", 8) != 0) {
        snprintf(erro, tam_erro, "o Opus nao tem o OpusTags");
        opus_fechar(o);
        return NULL;
    }
    o->dados = ftell(o->f);
    o->pcm = (float *)dap_alocar((size_t)QUADROS_MAX * o->canais * sizeof(float));
    if (!o->pcm) { opus_fechar(o); return NULL; }

    o->fim48 = ultimo_granulo(o);
    fseek(o->f, o->dados, SEEK_SET);
    o->nseg = o->iseg = 0;
    o->pos48 = 0;
    o->alvo48 = (uint64_t)o->pre_skip;

    info->taxa = TAXA_OPUS;
    info->canais = o->canais > 2 ? 2 : o->canais;
    info->bits = 16;
    info->quadros = o->fim48 > (uint64_t)o->pre_skip ? o->fim48 - (uint64_t)o->pre_skip : 0;
    return o;
}

/* Um canal do pacote decodificado. */
static float c_(const opus_t *o, int quadro, int canal) { return o->pcm[quadro * o->canais + canal]; }

/* Para dois canais, na ordem do Vorbis (a do Opus): L C R, L R Ls Rs, L C R Ls Rs (LFE)... */
static void para_estereo(const opus_t *o, int k, float *l, float *r)
{
    switch (o->canais) {
    case 1: *l = *r = c_(o, k, 0); return;
    case 2: *l = c_(o, k, 0); *r = c_(o, k, 1); return;
    case 3: *l = (c_(o, k, 0) + 0.7071f * c_(o, k, 1)) * 0.6f; *r = (c_(o, k, 2) + 0.7071f * c_(o, k, 1)) * 0.6f; return;
    case 4: *l = (c_(o, k, 0) + 0.7071f * c_(o, k, 2)) * 0.6f; *r = (c_(o, k, 1) + 0.7071f * c_(o, k, 3)) * 0.6f; return;
    case 5: case 6: {
        float c = 0.7071f * c_(o, k, 1);
        *l = (c_(o, k, 0) + c + 0.7071f * c_(o, k, 3)) * 0.4f;
        *r = (c_(o, k, 2) + c + 0.7071f * c_(o, k, 4)) * 0.4f;
        return;
    }
    default: {   /* 7 e 8: L C R Ls Rs (Cs | Lr Rr) LFE */
        float c = 0.7071f * c_(o, k, 1);
        float atras_l = o->canais == 8 ? c_(o, k, 5) : 0.5f * c_(o, k, 5);
        float atras_r = o->canais == 8 ? c_(o, k, 6) : 0.5f * c_(o, k, 5);
        *l = (c_(o, k, 0) + c + 0.7071f * (c_(o, k, 3) + atras_l)) * 0.35f;
        *r = (c_(o, k, 2) + c + 0.7071f * (c_(o, k, 4) + atras_r)) * 0.35f;
        return;
    }
    }
}

/* O proximo pacote decodificado em o->pcm, ja sem o que nao sai (o pre-skip,
 * o assentar da busca, o que passa do fim). 0 no fim. */
static int decodificar(opus_t *o)
{
    for (;;) {
        if (!proximo_pacote(o)) return 0;
        int n = o->dec ? opus_decode_float(o->dec, o->pacote, o->tpacote, o->pcm, QUADROS_MAX, 0)
                       : opus_multistream_decode_float(o->ms, o->pacote, o->tpacote, o->pcm, QUADROS_MAX, 0);
        if (n <= 0) continue;   /* pacote estragado: o proximo */
        /* onde o pacote termina: o granulo, se ele fecha a pagina; senao, a conta */
        uint64_t fim = o->granulo_pacote != SEM_GRANULO ? o->granulo_pacote : o->pos48 + (uint64_t)n;
        uint64_t inicio = fim >= (uint64_t)n ? fim - (uint64_t)n : 0;
        o->pos48 = fim;
        int ate = n;
        if (o->fim48 && fim > o->fim48) ate = o->fim48 > inicio ? (int)(o->fim48 - inicio) : 0;
        int de = o->alvo48 > inicio ? (int)(o->alvo48 - inicio < (uint64_t)ate ? o->alvo48 - inicio : (uint64_t)ate) : 0;
        if (ate - de <= 0) {
            if (o->fim48 && fim >= o->fim48) return 0;
            continue;
        }
        o->j_pcm = de;
        o->n_pcm = ate;
        return 1;
    }
}

static int opus_ler(void *s, float *estereo, int quadros)
{
    opus_t *o = (opus_t *)s;
    int feitos = 0;
    while (feitos < quadros) {
        if (o->j_pcm >= o->n_pcm && !decodificar(o)) break;
        for (; o->j_pcm < o->n_pcm && feitos < quadros; o->j_pcm++, feitos++) {
            float l, r;
            para_estereo(o, o->j_pcm, &l, &r);
            estereo[2 * feitos] = l * o->ganho;
            estereo[2 * feitos + 1] = r * o->ganho;
        }
    }
    return feitos;
}

/* O granulo e o comeco da primeira pagina (com granulo) a partir de um byte; -1 se nao ha. */
static int64_t granulo_depois(opus_t *o, long byte, long *onde)
{
    if (fseek(o->f, byte, SEEK_SET) != 0) return -1;
    for (int i = 0; i < 64; i++) {
        if (!sincronizar(o)) return -1;
        long aqui = ftell(o->f);
        int tipo;
        if (!ler_pagina(o, &tipo)) return -1;
        if (o->granulo_pagina != SEM_GRANULO) {
            *onde = aqui;
            return (int64_t)o->granulo_pagina;
        }
    }
    return -1;
}

static int opus_buscar(void *s, uint64_t quadro)
{
    opus_t *o = (opus_t *)s;
    uint64_t alvo = quadro + (uint64_t)o->pre_skip;           /* no granulo */
    uint64_t desde = alvo > PRE_ROLL ? alvo - PRE_ROLL : 0;    /* onde o decodificador precisa comecar */

    /* bissecao: a ultima pagina com granulo <= desde; o som recomeca na seguinte */
    long baixo = o->dados, alto = o->tamanho, achada = -1;
    uint64_t granulo_achado = 0;
    for (int volta = 0; volta < 40 && alto - baixo > 4096; volta++) {
        long meio = baixo + (alto - baixo) / 2, pagina = 0;
        int64_t g = granulo_depois(o, meio, &pagina);
        if (g < 0 || (uint64_t)g > desde) alto = meio;
        else { baixo = meio; achada = pagina; granulo_achado = (uint64_t)g; }
    }

    o->nseg = o->iseg = 0;
    o->n_pcm = o->j_pcm = 0;
    if (o->dec) opus_decoder_ctl(o->dec, OPUS_RESET_STATE);
    else opus_multistream_decoder_ctl(o->ms, OPUS_RESET_STATE);
    if (achada < 0) {
        /* perto do comeco: de la mesmo */
        fseek(o->f, o->dados, SEEK_SET);
        o->pos48 = 0;
        o->pular_continuacao = 0;
    } else {
        /* le a pagina achada, para ficar no comeco da seguinte */
        int tipo;
        fseek(o->f, achada, SEEK_SET);
        if (!ler_pagina(o, &tipo)) return 0;
        o->nseg = o->iseg = 0;
        o->pos48 = granulo_achado;
        o->pular_continuacao = 1;
    }
    o->alvo48 = alvo;
    return 1;
}

const dap_formato_t dap_formato_opus = { opus_abrir, opus_ler, opus_buscar, opus_fechar };
