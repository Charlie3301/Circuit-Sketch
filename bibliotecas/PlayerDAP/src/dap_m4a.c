/* dap_m4a.c -- o MP4 (M4A): as caixas e o ALAC.
 *
 * O M4A e uma arvore de caixas (4 bytes de tamanho, 4 letras de tipo). O que
 * o player usa mora em moov/trak/mdia: o mdhd (a escala de tempo e a
 * duracao), o hdlr ('soun' e a trilha de som) e o minf/stbl, com o stsd (o
 * codec: 'alac', ou 'mp4a', que e o AAC) e as tabelas que dizem onde esta
 * cada pacote no mdat -- stts (quantos quadros cada um tem), stsc (quantos
 * pacotes por bloco), stsz (o tamanho de cada um) e stco/co64 (onde cada
 * bloco comeca).
 *
 * O ALAC (Apple Lossless) e o decodificador da propria Apple, em src/alac
 * (Apache 2.0). O AAC mora no dap_aac.c, que usa as mesmas tabelas.
 */
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include "alac/alac_dec.h"
#include "alac/ALACBitUtilities.h"
#include "dap_interno.h"

static uint32_t be32(const uint8_t *p) { return (uint32_t)p[0] << 24 | (uint32_t)p[1] << 16 | (uint32_t)p[2] << 8 | p[3]; }
static uint64_t be64(const uint8_t *p) { return (uint64_t)be32(p) << 32 | be32(p + 4); }

int dap_m4a_caixa(FILE *f, long inicio, long fim, const char *tipo, long *conteudo, long *tamanho)
{
    long p = inicio;
    while (p + 8 <= fim) {
        uint8_t h[16];
        if (fseek(f, p, SEEK_SET) != 0 || fread(h, 1, 8, f) != 8) return 0;
        uint64_t t = be32(h);
        long cabecalho = 8;
        if (t == 1) {
            /* tamanho de 64 bits, depois do tipo */
            if (fread(h + 8, 1, 8, f) != 8) return 0;
            t = be64(h + 8);
            cabecalho = 16;
        } else if (t == 0) {
            t = (uint64_t)(fim - p);   /* vai ate o fim */
        }
        if (t < (uint64_t)cabecalho || t > (uint64_t)(fim - p)) return 0;
        if (memcmp(h + 4, tipo, 4) == 0) {
            *conteudo = p + cabecalho;
            *tamanho = (long)t - cabecalho;
            return 1;
        }
        p += (long)t;
    }
    return 0;
}

/* O mdia da trilha de som (o primeiro trak com hdlr 'soun') e o track_ID dela. */
static int trilha_de_som(FILE *f, long moov, long tamanho, long *mdia, long *tmdia, uint32_t *trilha)
{
    long p = moov, fim = moov + tamanho;
    long c, t;
    while (p < fim && dap_m4a_caixa(f, p, fim, "trak", &c, &t)) {
        long m, tm, h, th;
        if (dap_m4a_caixa(f, c, c + t, "mdia", &m, &tm) && dap_m4a_caixa(f, m, m + tm, "hdlr", &h, &th) && th >= 12) {
            uint8_t b[24];
            if (fseek(f, h, SEEK_SET) == 0 && fread(b, 1, 12, f) == 12 && memcmp(b + 8, "soun", 4) == 0) {
                *mdia = m;
                *tmdia = tm;
                /* o tkhd: o track_ID depois das datas (de 32 bits na versao 0, de 64 na 1) */
                *trilha = 0;
                long k, tk;
                if (dap_m4a_caixa(f, c, c + t, "tkhd", &k, &tk) && tk >= 24 && fseek(f, k, SEEK_SET) == 0 && fread(b, 1, 24, f) == 24)
                    *trilha = b[0] == 1 ? be32(b + 20) : be32(b + 12);
                return 1;
            }
        }
        p = c + t;
    }
    return 0;
}

int dap_m4a_ler(FILE *f, dap_m4a_t *m)
{
    memset(m, 0, sizeof(*m));
    if (fseek(f, 0, SEEK_END) != 0) return 0;
    long fim = ftell(f);
    long moov, tmoov, mdia, tmdia, c, t;
    if (!dap_m4a_caixa(f, 0, fim, "moov", &moov, &tmoov)) return 0;
    if (!trilha_de_som(f, moov, tmoov, &mdia, &tmdia, &m->trilha)) return 0;
    m->moov = moov;
    m->tmoov = tmoov;
    m->fim = fim;

    if (dap_m4a_caixa(f, mdia, mdia + tmdia, "mdhd", &c, &t) && t >= 24) {
        uint8_t b[32];
        memset(b, 0, sizeof(b));
        if (fseek(f, c, SEEK_SET) == 0 && fread(b, 1, t < 32 ? (size_t)t : 32, f) >= 24) {
            if (b[0] == 1 && t >= 32) { m->escala = be32(b + 20); m->duracao = be64(b + 24); }
            else { m->escala = be32(b + 12); m->duracao = be32(b + 16); }
        }
    }

    long minf, tminf;
    if (!dap_m4a_caixa(f, mdia, mdia + tmdia, "minf", &minf, &tminf)) return 0;
    if (!dap_m4a_caixa(f, minf, minf + tminf, "stbl", &m->stbl, &m->tstbl)) return 0;
    if (!dap_m4a_caixa(f, m->stbl, m->stbl + m->tstbl, "stsd", &c, &t) || t < 8 + 36) return 0;

    /* a primeira entrada: tamanho, formato e a descricao de som (versao 0:
     * 28 bytes; a 1 do QuickTime tem mais 16, e a 2, mais 36) */
    uint8_t e[36];
    if (fseek(f, c + 8, SEEK_SET) != 0 || fread(e, 1, 36, f) != 36) return 0;
    uint32_t te = be32(e);
    memcpy(m->codec, e + 4, 4);
    int versao = e[16] << 8 | e[17];
    m->canais = e[24] << 8 | e[25];
    m->bits = e[26] << 8 | e[27];
    m->taxa = (int)(be32(e + 32) >> 16);
    long extra = versao == 1 ? 16 : versao == 2 ? 36 : 0;
    m->filhos = c + 8 + 8 + 28 + extra;
    m->tfilhos = (long)te - 8 - 28 - extra;
    if (te > (uint32_t)(t - 8) || m->tfilhos < 0) m->tfilhos = 0;
    return 1;
}

int dap_m4a_codec(FILE *f)
{
    dap_m4a_t m;
    if (!dap_m4a_ler(f, &m)) return DAP_FMT_M4A;
    return memcmp(m.codec, "alac", 4) == 0 ? DAP_FMT_ALAC : DAP_FMT_M4A;
}

/* ===================================================================
 *  o ALAC
 * =================================================================== */
typedef struct {
    FILE *f;
    alac_dec_t alac;
    int canais;               /* no arquivo */
    uint32_t n;               /* pacotes */
    uint32_t *tamanhos;
    uint32_t *posicoes;
    uint32_t prox;            /* o proximo pacote a decodificar */
    uint8_t *pacote;
    uint32_t cap_pacote;
    uint8_t *pcm;             /* um pacote decodificado, intercalado */
    float *pronto;            /* o mesmo em float estereo */
    int n_pronto, ja;         /* quantos quadros ha, e quantos ja sairam */
} alac_t;

/* O conteudo de uma caixa do stbl, inteiro, na memoria. */
static uint8_t *caixa_inteira(FILE *f, long stbl, long tstbl, const char *tipo, long *tamanho)
{
    long c;
    if (!dap_m4a_caixa(f, stbl, stbl + tstbl, tipo, &c, tamanho) || *tamanho < 8) return NULL;
    uint8_t *b = (uint8_t *)dap_alocar((size_t)*tamanho);
    if (!b) return NULL;
    if (fseek(f, c, SEEK_SET) != 0 || fread(b, 1, (size_t)*tamanho, f) != (size_t)*tamanho) { free(b); return NULL; }
    return b;
}

/* Mais um pacote na tabela (a dos fragmentos cresce aos poucos). */
static int acrescentar(uint32_t *n, uint32_t *cap, uint32_t **tamanhos, uint32_t **posicoes, uint32_t tamanho, uint64_t posicao)
{
    if (*n >= 4000000 || posicao > 0xFFFFFFFFull) return 0;
    if (*n >= *cap) {
        uint32_t novo = *cap ? *cap * 2 : 256;
        uint32_t *t = (uint32_t *)dap_realocar(*tamanhos, novo * sizeof(uint32_t));
        if (!t) return 0;
        *tamanhos = t;
        uint32_t *p = (uint32_t *)dap_realocar(*posicoes, novo * sizeof(uint32_t));
        if (!p) return 0;
        *posicoes = p;
        *cap = novo;
    }
    (*tamanhos)[*n] = tamanho;
    (*posicoes)[*n] = (uint32_t)posicao;
    (*n)++;
    return 1;
}

/* O MP4 fragmentado (o DASH das lojas e do YouTube, o 3GP de celular):
 * depois do moov vem pares moof/mdat, e cada traf da trilha diz, no trun,
 * quantos pacotes, o tamanho de cada um e onde comecam. O tamanho que o
 * trun nao traz vem do tfhd ou do trex (no mvex do moov). */
static void fragmentos(FILE *f, const dap_m4a_t *m, uint32_t *n, uint32_t *cap, uint32_t **tamanhos, uint32_t **posicoes)
{
    uint32_t tamanho_trex = 0;
    long c, t, k, tk;
    if (dap_m4a_caixa(f, m->moov, m->moov + m->tmoov, "mvex", &c, &t)) {
        long p = c;
        while (p < c + t && dap_m4a_caixa(f, p, c + t, "trex", &k, &tk)) {
            uint8_t b[24];
            if (tk >= 24 && fseek(f, k, SEEK_SET) == 0 && fread(b, 1, 24, f) == 24 && be32(b + 4) == m->trilha) tamanho_trex = be32(b + 16);
            p = k + tk;
        }
    } else {
        return;   /* sem mvex nao ha fragmentos */
    }

    long p = 0;
    while (p < m->fim && dap_m4a_caixa(f, p, m->fim, "moof", &c, &t)) {
        long moof = c - 8, q = c;
        while (q < c + t && dap_m4a_caixa(f, q, c + t, "traf", &k, &tk)) {
            long h, th, r, tr;
            uint8_t b[40];
            memset(b, 0, sizeof(b));
            if (dap_m4a_caixa(f, k, k + tk, "tfhd", &h, &th) && th >= 8 && fseek(f, h, SEEK_SET) == 0
                && fread(b, 1, th < 40 ? (size_t)th : 40, f) >= 8 && be32(b + 4) == m->trilha) {
                uint32_t flags = be32(b) & 0xFFFFFF;
                int i = 8;
                uint64_t base = (uint64_t)moof;   /* sem base explicita: o comeco do moof */
                uint32_t tamanho_padrao = tamanho_trex;
                if (flags & 0x01) { base = be64(b + i); i += 8; }
                if (flags & 0x02) i += 4;
                if (flags & 0x08) i += 4;
                if (flags & 0x10) { tamanho_padrao = be32(b + i); i += 4; }

                uint64_t pos = base;
                long s = k;
                while (s < k + tk && dap_m4a_caixa(f, s, k + tk, "trun", &r, &tr)) {
                    uint8_t *run = tr >= 8 && tr < (1 << 24) ? (uint8_t *)dap_alocar((size_t)tr) : NULL;
                    if (run && fseek(f, r, SEEK_SET) == 0 && fread(run, 1, (size_t)tr, f) == (size_t)tr) {
                        uint32_t fl = be32(run) & 0xFFFFFF, amostras = be32(run + 4);
                        long j = 8;
                        if (fl & 0x001) { pos = base + (uint64_t)(int64_t)(int32_t)be32(run + j); j += 4; }
                        if (fl & 0x004) j += 4;
                        int por_amostra = ((fl & 0x100) ? 4 : 0) + ((fl & 0x200) ? 4 : 0) + ((fl & 0x400) ? 4 : 0) + ((fl & 0x800) ? 4 : 0);
                        for (uint32_t a = 0; a < amostras && j + por_amostra <= tr; a++) {
                            uint32_t tam = tamanho_padrao;
                            long o = j + ((fl & 0x100) ? 4 : 0);
                            if (fl & 0x200) tam = be32(run + o);
                            j += por_amostra;
                            if (!tam || !acrescentar(n, cap, tamanhos, posicoes, tam, pos)) break;
                            pos += tam;
                        }
                    }
                    free(run);
                    s = r + tr;
                }
            }
            q = k + tk;
        }
        p = c + t;
    }
}

/* As tabelas do stbl (e os fragmentos) viram o tamanho e a posicao de cada pacote. */
int dap_m4a_pacotes(FILE *f, const dap_m4a_t *m, uint32_t *n_saida, uint32_t **tamanhos, uint32_t **posicoes)
{
    long stbl = m->stbl, tstbl = m->tstbl;
    long tsz, tco, tsc;
    int co64 = 0;
    uint8_t *sz = caixa_inteira(f, stbl, tstbl, "stsz", &tsz);
    uint8_t *co = caixa_inteira(f, stbl, tstbl, "stco", &tco);
    if (!co) { co = caixa_inteira(f, stbl, tstbl, "co64", &tco); co64 = 1; }
    uint8_t *sc = caixa_inteira(f, stbl, tstbl, "stsc", &tsc);
    int ok = 0;
    uint32_t cap = 0;
    *n_saida = 0;
    *tamanhos = *posicoes = NULL;
    if (sz && co && sc && tsz >= 12 && be32(sz + 8) == 0) ok = 1;   /* tudo nos fragmentos */
    else if (sz && co && sc && tsz >= 12) {
        uint32_t fixo = be32(sz + 4), n = be32(sz + 8);
        uint32_t blocos = be32(co + 4), entradas = be32(sc + 4);
        if (n > 0 && n < 4000000 && (fixo || (uint64_t)tsz >= 12 + 4ull * n)
            && (uint64_t)tco >= 8 + (co64 ? 8ull : 4ull) * blocos && (uint64_t)tsc >= 8 + 12ull * entradas && entradas > 0) {
            *n_saida = cap = n;
            *tamanhos = (uint32_t *)dap_alocar(n * sizeof(uint32_t));
            *posicoes = (uint32_t *)dap_alocar(n * sizeof(uint32_t));
            if (*tamanhos && *posicoes) {
                for (uint32_t i = 0; i < n; i++) (*tamanhos)[i] = fixo ? fixo : be32(sz + 12 + 4 * i);
                uint32_t pacote = 0;
                for (uint32_t b = 0; b < blocos && pacote < n; b++) {
                    /* quantos pacotes este bloco tem: a ultima entrada do stsc que comeca nele ou antes */
                    uint32_t por_bloco = 0;
                    for (uint32_t k = 0; k < entradas; k++) {
                        if (be32(sc + 8 + 12 * k) - 1 <= b) por_bloco = be32(sc + 8 + 12 * k + 4);
                        else break;
                    }
                    uint64_t pos = co64 ? be64(co + 8 + 8 * b) : be32(co + 8 + 4 * b);
                    for (uint32_t k = 0; k < por_bloco && pacote < n; k++) {
                        (*posicoes)[pacote] = (uint32_t)pos;
                        pos += (*tamanhos)[pacote++];
                    }
                }
                ok = pacote == n;
            }
        }
    }
    free(sz);
    free(co);
    free(sc);
    if (ok) fragmentos(f, m, n_saida, &cap, tamanhos, posicoes);
    return ok && *n_saida > 0;
}

static void alac_fechar(void *s)
{
    alac_t *a = (alac_t *)s;
    if (!a) return;
    alac_dec_soltar(&a->alac);
    if (a->f) fclose(a->f);
    free(a->tamanhos);
    free(a->posicoes);
    free(a->pacote);
    free(a->pcm);
    free(a->pronto);
    free(a);
}

static void *alac_abrir(const char *caminho, dap_info_audio_t *info, char *erro, int tam_erro)
{
    alac_t *a = (alac_t *)calloc(1, sizeof(alac_t));
    if (!a) return NULL;
    a->f = fopen(caminho, "rb");
    dap_m4a_t m;
    long c, t;
    if (!a->f || !dap_m4a_ler(a->f, &m)) {
        snprintf(erro, tam_erro, "o M4A esta estragado");
        alac_fechar(a);
        return NULL;
    }
    if (memcmp(m.codec, "alac", 4) != 0 || !dap_m4a_caixa(a->f, m.filhos, m.filhos + m.tfilhos, "alac", &c, &t) || t < 4 + 24) {
        snprintf(erro, tam_erro, "o M4A nao e ALAC");
        alac_fechar(a);
        return NULL;
    }
    uint8_t cookie[64];
    long lidos = t - 4 > (long)sizeof(cookie) ? (long)sizeof(cookie) : t - 4;
    if (fseek(a->f, c + 4, SEEK_SET) != 0 || fread(cookie, 1, (size_t)lidos, a->f) != (size_t)lidos
        || alac_dec_iniciar(&a->alac, cookie, (uint32_t)lidos) != 0) {
        snprintf(erro, tam_erro, "o ALAC tem uma configuracao que nao abre");
        alac_fechar(a);
        return NULL;
    }
    a->canais = a->alac.config.numChannels;
    if (!dap_m4a_pacotes(a->f, &m, &a->n, &a->tamanhos, &a->posicoes)) {
        snprintf(erro, tam_erro, "o M4A nao tem a tabela dos pacotes");
        alac_fechar(a);
        return NULL;
    }
    uint32_t q = a->alac.config.frameLength;
    a->pcm = (uint8_t *)dap_alocar((size_t)q * a->canais * 4);
    a->pronto = (float *)dap_alocar((size_t)q * 2 * sizeof(float));
    if (!a->pcm || !a->pronto) {
        snprintf(erro, tam_erro, "sem memoria para o ALAC");
        alac_fechar(a);
        return NULL;
    }
    info->taxa = a->alac.config.sampleRate ? (int)a->alac.config.sampleRate : m.taxa;
    info->canais = a->canais > 2 ? 2 : a->canais;
    info->bits = a->alac.config.bitDepth;
    /* a duracao do mdhd, na escala dele; sem ela, todos os pacotes cheios */
    info->quadros = m.escala && m.duracao ? m.duracao * (uint64_t)info->taxa / m.escala : (uint64_t)a->n * q;
    return a;
}

/* Uma amostra do pacote decodificado, de -1 a 1. */
static float amostra(const alac_t *a, uint32_t quadro, int canal)
{
    uint32_t i = quadro * (uint32_t)a->canais + (uint32_t)canal;
    switch (a->alac.config.bitDepth) {
    case 16: return ((const int16_t *)a->pcm)[i] * (1.0f / 32768.0f);
    case 32: return (float)((const int32_t *)a->pcm)[i] * (1.0f / 2147483648.0f);
    default: {
        /* 20 e 24 bits: 3 bytes, o menos significativo primeiro (o 20 vem deslocado para cima) */
        const uint8_t *p = a->pcm + i * 3;
        int32_t v = (int32_t)((uint32_t)p[0] << 8 | (uint32_t)p[1] << 16 | (uint32_t)p[2] << 24) >> 8;
        return (float)v * (1.0f / 8388608.0f);
    }
    }
}

/* Mais de dois canais viram dois: a ordem do ALAC e C L R, depois os de tras e o LFE (que sai). */
static void para_estereo(alac_t *a, uint32_t quadros)
{
    for (uint32_t k = 0; k < quadros; k++) {
        float l, r;
        switch (a->canais) {
        case 1: l = r = amostra(a, k, 0); break;
        case 2: l = amostra(a, k, 0); r = amostra(a, k, 1); break;
        case 3: case 4: {
            float c = 0.7071f * amostra(a, k, 0), atras = a->canais == 4 ? 0.5f * amostra(a, k, 3) : 0;
            l = (amostra(a, k, 1) + c + atras) * 0.5f;
            r = (amostra(a, k, 2) + c + atras) * 0.5f;
            break;
        }
        case 8: {
            float c = 0.7071f * amostra(a, k, 0);
            l = (amostra(a, k, 3) + c + 0.7071f * (amostra(a, k, 1) + amostra(a, k, 5))) * 0.35f;
            r = (amostra(a, k, 4) + c + 0.7071f * (amostra(a, k, 2) + amostra(a, k, 6))) * 0.35f;
            break;
        }
        default: {   /* 5, 6 e 7: C L R Ls Rs (Cs) (LFE) */
            float c = 0.7071f * amostra(a, k, 0);
            float cs = a->canais == 7 ? 0.5f * amostra(a, k, 5) : 0;
            l = (amostra(a, k, 1) + c + 0.7071f * amostra(a, k, 3) + cs) * 0.4f;
            r = (amostra(a, k, 2) + c + 0.7071f * amostra(a, k, 4) + cs) * 0.4f;
            break;
        }
        }
        a->pronto[2 * k] = l;
        a->pronto[2 * k + 1] = r;
    }
}

/* O proximo pacote: devolve quantos quadros sairam (0 no fim, -1 com erro). */
static int proximo_pacote(alac_t *a)
{
    if (a->prox >= a->n) return 0;
    uint32_t t = a->tamanhos[a->prox];
    if (t > a->cap_pacote) {
        uint8_t *novo = (uint8_t *)dap_realocar(a->pacote, t + 8);
        if (!novo) return -1;
        a->pacote = novo;
        a->cap_pacote = t;
    }
    if (fseek(a->f, (long)a->posicoes[a->prox], SEEK_SET) != 0 || fread(a->pacote, 1, t, a->f) != t) return -1;
    a->prox++;
    BitBuffer bits;
    BitBufferInit(&bits, a->pacote, t);
    uint32_t saiu = 0;
    if (alac_dec_decodificar(&a->alac, &bits, a->pcm, a->alac.config.frameLength, (uint32_t)a->canais, &saiu) != 0) return -1;
    if (saiu > a->alac.config.frameLength) saiu = a->alac.config.frameLength;
    para_estereo(a, saiu);
    a->n_pronto = (int)saiu;
    a->ja = 0;
    return (int)saiu;
}

static int alac_ler(void *s, float *estereo, int quadros)
{
    alac_t *a = (alac_t *)s;
    int feitos = 0;
    while (feitos < quadros) {
        if (a->ja >= a->n_pronto && proximo_pacote(a) <= 0) break;
        int n = a->n_pronto - a->ja;
        if (n > quadros - feitos) n = quadros - feitos;
        memcpy(estereo + 2 * feitos, a->pronto + 2 * a->ja, (size_t)n * 2 * sizeof(float));
        a->ja += n;
        feitos += n;
    }
    return feitos;
}

static int alac_buscar(void *s, uint64_t quadro)
{
    alac_t *a = (alac_t *)s;
    uint32_t q = a->alac.config.frameLength;
    uint64_t pacote = quadro / q;
    if (pacote >= a->n) pacote = a->n;
    a->prox = (uint32_t)pacote;
    a->n_pronto = a->ja = 0;
    uint32_t pular = (uint32_t)(quadro - pacote * q);
    if (pular && proximo_pacote(a) > 0) a->ja = (int)pular < a->n_pronto ? (int)pular : a->n_pronto;
    return 1;
}

const dap_formato_t dap_formato_alac = { alac_abrir, alac_ler, alac_buscar, alac_fechar };
