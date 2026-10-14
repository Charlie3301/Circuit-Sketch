/* dap_aac.c -- o AAC: no M4A (a entrada 'mp4a' do stsd, com o esds) e cru
 * (ADTS, o .aac das radios e de alguns gravadores).
 *
 * O decodificador e o faad2 2.11.4 (src/aac; GPL 2 ou mais nova): AAC-LC e
 * o HE-AAC v1 e v2 (SBR e PS), com a saida em float e o 5.1 descendo para
 * estereo nele mesmo. Code from FAAD2 is copyright (c) Nero AG, www.nero.com.
 *
 * O faad2 segura o primeiro quadro (o atraso da sobreposicao da MDCT): a
 * saida do pacote k e o trecho do pacote k-1. Na busca do M4A comeca-se um
 * pacote antes do certo e a saida dele vai fora. O ADTS nao tem tabela: a
 * duracao sai do tamanho medio dos primeiros quadros, e a busca pula para
 * o byte proporcional e procura o proximo quadro (dois cabecalhos seguidos).
 *
 * Este arquivo vai sob a GNU GPL versao 2 ou mais nova, como o faad2.
 */
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include "aac/neaacdec.h"
#include "dap_interno.h"

#define AAC_ENTRADA (16 * 1024)   /* ADTS: o pedaco lido por vez (cabem varios quadros) */

typedef struct {
    FILE *f;
    NeAACDecHandle h;
    int adts;
    /* M4A */
    uint32_t n, prox;
    uint32_t *tamanhos, *posicoes;
    uint8_t *pacote;
    uint32_t cap_pacote;
    /* ADTS */
    long inicio, fim_dos_dados;
    double bytes_por_quadro;
    uint8_t *entrada;
    int na_entrada;
    /* a saida */
    float *pronto;
    int n_pronto, ja, cap_pronto;
    uint32_t pular;               /* depois de uma busca: amostras a jogar fora */
    uint32_t por_pacote;          /* amostras por pacote na saida (1024; 2048 com o SBR) */
    int taxa;
    int fim;
} aac_t;

static uint32_t be32(const uint8_t *p) { return (uint32_t)p[0] << 24 | (uint32_t)p[1] << 16 | (uint32_t)p[2] << 8 | p[3]; }

/* ------------------------------------------------------------ ADTS */

/* O tamanho do quadro ADTS que comeca em b (0 se nao e um cabecalho). */
static int quadro_adts(const uint8_t *b)
{
    if (b[0] != 0xFF || (b[1] & 0xF6) != 0xF0) return 0;
    if (((b[2] >> 2) & 0x0F) >= 13) return 0;               /* indice da taxa */
    int t = ((b[3] & 3) << 11) | (b[4] << 3) | (b[5] >> 5);
    return t >= 7 ? t : 0;
}

static void encher(aac_t *a)
{
    size_t n = fread(a->entrada + a->na_entrada, 1, (size_t)(AAC_ENTRADA - a->na_entrada), a->f);
    a->na_entrada += (int)n;
    if (a->na_entrada < AAC_ENTRADA) memset(a->entrada + a->na_entrada, 0, (size_t)(AAC_ENTRADA - a->na_entrada));
}

static void consumir(aac_t *a, int bytes)
{
    if (bytes > a->na_entrada) bytes = a->na_entrada;
    memmove(a->entrada, a->entrada + bytes, (size_t)(a->na_entrada - bytes));
    a->na_entrada -= bytes;
    encher(a);
}

/* Depois do ultimo quadro pode vir uma etiqueta (ID3v1, APEv2 ou ID3v2). */
static int etiqueta(const uint8_t *b)
{
    return memcmp(b, "TAG", 3) == 0 || memcmp(b, "APETAGEX", 8) == 0 || memcmp(b, "ID3", 3) == 0;
}

/* Acha o proximo quadro na entrada: um cabecalho valido e, se cabe, o
 * seguinte logo depois dele (um 0xFFF qualquer no meio do audio nao basta). */
static int sincronizar(aac_t *a)
{
    for (int voltas = 0; voltas < 256; voltas++) {
        if (a->na_entrada < 7) return 0;
        int i;
        for (i = 0; i + 7 <= a->na_entrada; i++) {
            int t = quadro_adts(a->entrada + i);
            if (!t) continue;
            const uint8_t *depois = a->entrada + i + t;
            if (i + t + 8 <= a->na_entrada && !quadro_adts(depois) && !etiqueta(depois)) continue;
            if (i) consumir(a, i);
            return 1;
        }
        consumir(a, i);   /* nada aqui: fica so o rabo, que pode ser o comeco de um cabecalho */
    }
    return 0;
}

/* ---------------------------------------------------------- comum */

/* A saida do faad2 (float, intercalada) vira estereo em a->pronto. */
static int guardar(aac_t *a, const float *amostras, const NeAACDecFrameInfo *fi)
{
    int canais = fi->channels;
    if (canais < 1 || fi->samples == 0) return 0;
    int quadros = (int)(fi->samples / (unsigned long)canais);
    if (quadros > a->cap_pronto) {
        float *novo = (float *)dap_realocar(a->pronto, (size_t)quadros * 2 * sizeof(float));
        if (!novo) return 0;
        a->pronto = novo;
        a->cap_pronto = quadros;
    }
    for (int k = 0; k < quadros; k++) {
        const float *q = amostras + (size_t)k * canais;
        float l, r;
        if (canais == 1) l = r = q[0];
        else if (canais == 2) { l = q[0]; r = q[1]; }
        else { l = q[1] + 0.7071f * q[0]; r = q[2] + 0.7071f * q[0]; }   /* C L R (o 5.1 o faad2 ja desceu) */
        a->pronto[2 * k] = l;
        a->pronto[2 * k + 1] = r;
    }
    a->n_pronto = quadros;
    a->ja = 0;
    if (fi->samplerate) a->taxa = (int)fi->samplerate;
    return quadros;
}

/* O proximo pacote decodificado: quantos quadros sairam (0 no fim). Um
 * pacote estragado vai fora, e o seguinte segue (o faad2 se recupera). */
static int proximo(aac_t *a)
{
    for (int tentativas = 0; tentativas < 64 && !a->fim; tentativas++) {
        NeAACDecFrameInfo fi;
        void *saida;
        if (a->adts) {
            if (!sincronizar(a)) { a->fim = 1; return 0; }
            int t = quadro_adts(a->entrada);
            saida = NeAACDecDecode(a->h, &fi, a->entrada, (unsigned long)a->na_entrada);
            consumir(a, fi.error || fi.bytesconsumed == 0 ? (t ? t : 1) : (int)fi.bytesconsumed);
        } else {
            if (a->prox >= a->n) { a->fim = 1; return 0; }
            uint32_t t = a->tamanhos[a->prox];
            if (t + 8 > a->cap_pacote) {
                uint8_t *novo = (uint8_t *)dap_realocar(a->pacote, t + 8);
                if (!novo) { a->fim = 1; return 0; }
                a->pacote = novo;
                a->cap_pacote = t + 8;
            }
            if (fseek(a->f, (long)a->posicoes[a->prox], SEEK_SET) != 0 || fread(a->pacote, 1, t, a->f) != t) { a->fim = 1; return 0; }
            memset(a->pacote + t, 0, 8);
            a->prox++;
            saida = NeAACDecDecode(a->h, &fi, a->pacote, t);
        }
        if (fi.error || !saida) continue;
        int q = guardar(a, (const float *)saida, &fi);
        if (q <= 0) continue;   /* o primeiro quadro (o atraso) nao sai */
        if (a->pular) {
            uint32_t fora = a->pular < (uint32_t)q ? a->pular : (uint32_t)q;
            a->ja = (int)fora;
            a->pular -= fora;
            if (a->ja >= a->n_pronto) continue;
        }
        return q;
    }
    return 0;
}

static void aac_fechar(void *s)
{
    aac_t *a = (aac_t *)s;
    if (!a) return;
    if (a->h) NeAACDecClose(a->h);
    if (a->f) fclose(a->f);
    free(a->tamanhos);
    free(a->posicoes);
    free(a->pacote);
    free(a->entrada);
    free(a->pronto);
    free(a);
}

/* Le um descritor do esds: a etiqueta e o tamanho (de 1 a 4 bytes de 7 bits). */
static int descritor(const uint8_t *b, int n, int *i, int *tamanho)
{
    if (*i >= n) return -1;
    int etiqueta = b[(*i)++], t = 0;
    for (int k = 0; k < 4 && *i < n; k++) {
        int c = b[(*i)++];
        t = (t << 7) | (c & 0x7F);
        if (!(c & 0x80)) break;
    }
    *tamanho = t;
    return etiqueta;
}

/* O AudioSpecificConfig de dentro do esds (o descritor 5), e o tipo do
 * objeto (o 4: 0x40 e AAC do MPEG-4, 0x66..0x68 do MPEG-2, 0x69/0x6B e MP3). */
static int config_do_esds(const uint8_t *b, int n, const uint8_t **asc, int *tasc, int *tipo)
{
    int i = 4, t;   /* versao e flags */
    *tipo = 0x40;
    while (i < n) {
        int e = descritor(b, n, &i, &t);
        if (e == 3) {
            /* ES_Descriptor: o ES_ID, as flags e os campos que elas ligam; os
             * descritores de dentro vem logo depois */
            if (i + 3 > n) return 0;
            int flags = b[i + 2];
            i += 3;
            if (flags & 0x80) i += 2;                      /* dependsOn_ES_ID */
            if (flags & 0x40) { if (i >= n) return 0; i += 1 + b[i]; }   /* a URL */
            if (flags & 0x20) i += 2;                      /* OCR_ES_Id */
        }
        else if (e == 4) { if (i < n) *tipo = b[i]; i += 13; }   /* DecoderConfigDescriptor */
        else if (e == 5) {
            if (t <= 0 || i + t > n) return 0;
            *asc = b + i;
            *tasc = t;
            return 1;
        } else if (e < 0) return 0;
        else i += t;
    }
    return 0;
}

static NeAACDecHandle novo_decodificador(void)
{
    NeAACDecHandle h = NeAACDecOpen();
    if (!h) return NULL;
    NeAACDecConfigurationPtr c = NeAACDecGetCurrentConfiguration(h);
    c->outputFormat = FAAD_FMT_FLOAT;
    c->downMatrix = 1;
    c->defObjectType = LC;
    NeAACDecSetConfiguration(h, c);
    return h;
}

static int abrir_m4a(aac_t *a, uint64_t *micros, char *erro, int tam_erro)
{
    dap_m4a_t m;
    long c, t;
    if (!dap_m4a_ler(a->f, &m)) { snprintf(erro, tam_erro, "o M4A esta estragado"); return 0; }
    if (memcmp(m.codec, "mp4a", 4) != 0 || !dap_m4a_caixa(a->f, m.filhos, m.filhos + m.tfilhos, "esds", &c, &t) || t < 8 || t > 4096) {
        snprintf(erro, tam_erro, "o M4A nao tem a configuracao do AAC");
        return 0;
    }
    uint8_t esds[4096];
    const uint8_t *asc = NULL;
    int tasc = 0, tipo = 0;
    if (fseek(a->f, c, SEEK_SET) != 0 || fread(esds, 1, (size_t)t, a->f) != (size_t)t || !config_do_esds(esds, (int)t, &asc, &tasc, &tipo)) {
        snprintf(erro, tam_erro, "o M4A nao tem a configuracao do AAC");
        return 0;
    }
    if (tipo == 0x69 || tipo == 0x6B) { snprintf(erro, tam_erro, "MP3 dentro do M4A nao toca"); return 0; }
    unsigned long taxa = 0;
    unsigned char canais = 0;
    if (NeAACDecInit2(a->h, (unsigned char *)asc, (unsigned long)tasc, &taxa, &canais) < 0) {
        snprintf(erro, tam_erro, "o AAC tem uma configuracao que o player nao toca");
        return 0;
    }
    a->taxa = (int)taxa;
    if (!dap_m4a_pacotes(a->f, &m, &a->n, &a->tamanhos, &a->posicoes)) {
        snprintf(erro, tam_erro, "o M4A nao tem a tabela dos pacotes");
        return 0;
    }
    /* a duracao do mdhd, em microssegundos: a taxa de saida (com o SBR, o
     * dobro) so se sabe no primeiro quadro */
    *micros = m.escala ? (uint64_t)((double)m.duracao / m.escala * 1000000.0) : 0;
    return 1;
}

static int abrir_adts(aac_t *a, uint64_t *micros, char *erro, int tam_erro)
{
    uint8_t cab[10];
    a->inicio = 0;
    if (fread(cab, 1, 10, a->f) == 10 && memcmp(cab, "ID3", 3) == 0)
        a->inicio = 10 + ((cab[6] & 0x7F) << 21 | (cab[7] & 0x7F) << 14 | (cab[8] & 0x7F) << 7 | (cab[9] & 0x7F)) + ((cab[5] & 0x10) ? 10 : 0);
    fseek(a->f, 0, SEEK_END);
    a->fim_dos_dados = ftell(a->f);
    fseek(a->f, a->inicio, SEEK_SET);
    a->entrada = (uint8_t *)dap_alocar(AAC_ENTRADA + 16);
    if (!a->entrada) { snprintf(erro, tam_erro, "sem memoria para o AAC"); return 0; }
    a->na_entrada = 0;
    encher(a);
    if (!sincronizar(a)) { snprintf(erro, tam_erro, "o AAC nao tem nenhum quadro"); return 0; }

    /* o tamanho medio dos primeiros quadros (para a duracao e a busca) */
    int vistos = 0;
    long soma = 0;
    for (int i = 0; vistos < 64 && i + 7 <= a->na_entrada; vistos++) {
        int t = quadro_adts(a->entrada + i);
        if (!t) break;
        soma += t;
        i += t;
    }
    a->bytes_por_quadro = vistos ? (double)soma / vistos : 0;

    unsigned long taxa = 0;
    unsigned char canais = 0;
    long pular = NeAACDecInit(a->h, a->entrada, (unsigned long)a->na_entrada, &taxa, &canais);
    if (pular < 0) { snprintf(erro, tam_erro, "o AAC tem uma configuracao que o player nao toca"); return 0; }
    if (pular > 0) consumir(a, (int)pular);
    a->taxa = (int)taxa;
    *micros = 0;
    return 1;
}

static void *aac_abrir(const char *caminho, dap_info_audio_t *info, char *erro, int tam_erro)
{
    aac_t *a = (aac_t *)calloc(1, sizeof(aac_t));
    if (!a) return NULL;
    a->f = fopen(caminho, "rb");
    if (!a->f) { snprintf(erro, tam_erro, "o AAC nao abre"); aac_fechar(a); return NULL; }
    uint8_t b[8];
    a->adts = fread(b, 1, 8, a->f) == 8 && memcmp(b + 4, "ftyp", 4) != 0;
    fseek(a->f, 0, SEEK_SET);
    a->h = novo_decodificador();
    uint64_t micros = 0;
    if (!a->h || !(a->adts ? abrir_adts(a, &micros, erro, tam_erro) : abrir_m4a(a, &micros, erro, tam_erro))) {
        if (!a->h) snprintf(erro, tam_erro, "sem memoria para o AAC");
        aac_fechar(a);
        return NULL;
    }

    /* O primeiro quadro que sai diz a taxa de verdade (com o SBR, o dobro da
     * do nucleo) e quantas amostras cada pacote da. */
    if (proximo(a) <= 0) {
        snprintf(erro, tam_erro, "o AAC nao decodifica");
        aac_fechar(a);
        return NULL;
    }
    a->por_pacote = (uint32_t)a->n_pronto;
    info->taxa = a->taxa;
    info->canais = 2;
    info->bits = 16;
    if (!a->adts)
        info->quadros = micros ? (uint64_t)((double)micros * a->taxa / 1000000.0) : (uint64_t)a->n * a->por_pacote;
    else if (a->bytes_por_quadro > 0)
        info->quadros = (uint64_t)((double)(a->fim_dos_dados - a->inicio) / a->bytes_por_quadro * a->por_pacote);
    return a;
}

static int aac_ler(void *s, float *estereo, int quadros)
{
    aac_t *a = (aac_t *)s;
    int feitos = 0;
    while (feitos < quadros) {
        if (a->ja >= a->n_pronto && proximo(a) <= 0) break;
        int n = a->n_pronto - a->ja;
        if (n > quadros - feitos) n = quadros - feitos;
        memcpy(estereo + 2 * feitos, a->pronto + 2 * a->ja, (size_t)n * 2 * sizeof(float));
        a->ja += n;
        feitos += n;
    }
    return feitos;
}

static int aac_buscar(void *s, uint64_t quadro)
{
    aac_t *a = (aac_t *)s;
    if (!a->por_pacote) return 0;
    uint64_t pacote = quadro / a->por_pacote;
    a->n_pronto = a->ja = 0;
    a->fim = 0;
    if (!a->adts) {
        if (pacote >= a->n) pacote = a->n;
        /* a saida do pacote k e o trecho k-1: o pacote "pacote" vai fora
         * (sem o anterior, a sobreposicao dele sai errada) */
        a->prox = (uint32_t)pacote;
        NeAACDecPostSeekReset(a->h, (long)pacote);
        a->pular = (uint32_t)((pacote > 0 ? a->por_pacote : 0) + (quadro - pacote * a->por_pacote));
        return 1;
    }
    long alvo = a->inicio + (long)(pacote * a->bytes_por_quadro);
    if (alvo >= a->fim_dos_dados) alvo = a->fim_dos_dados;
    if (fseek(a->f, alvo, SEEK_SET) != 0) return 0;
    a->na_entrada = 0;
    encher(a);
    NeAACDecPostSeekReset(a->h, (long)pacote);
    a->pular = pacote > 0 ? a->por_pacote : 0;
    return 1;
}

const dap_formato_t dap_formato_aac = { aac_abrir, aac_ler, aac_buscar, aac_fechar };
