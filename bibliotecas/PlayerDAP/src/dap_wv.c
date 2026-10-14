/* dap_wv.c -- o WavPack: sem perdas, hibrido (com perdas) e DSD.
 *
 * O decodificador e o do proprio WavPack (David Bryant, licenca BSD), em
 * src/wavpack: so os arquivos que tocam. Ele le o arquivo pelas funcoes
 * daqui (um leitor sobre o FILE do cartao ou do pendrive). O DSD guardado em
 * WavPack sai como PCM de 24 bits a 1/8 da taxa do DSD, e o multicanal sai
 * com os dois canais da frente.
 */
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include "wavpack/wavpack.h"
#include "dap_interno.h"

typedef struct {
    FILE *f;
    WavpackContext *w;
    int canais, bytes, flutuante;
    int32_t *buf;
    int cap;
} wv_t;

static int32_t wv_ler_bytes(void *id, void *dados, int32_t n) { return (int32_t)fread(dados, 1, (size_t)n, (FILE *)id); }
static int64_t wv_onde(void *id) { return ftell((FILE *)id); }
static int wv_ir(void *id, int64_t pos) { return fseek((FILE *)id, (long)pos, SEEK_SET); }
static int wv_andar(void *id, int64_t delta, int de) { return fseek((FILE *)id, (long)delta, de); }
static int wv_devolver(void *id, int c) { return ungetc(c, (FILE *)id); }
static int wv_pode(void *id) { (void)id; return 1; }

static int64_t wv_tamanho(void *id)
{
    FILE *f = (FILE *)id;
    long agora = ftell(f);
    if (fseek(f, 0, SEEK_END) != 0) return -1;
    long fim = ftell(f);
    fseek(f, agora, SEEK_SET);
    return fim;
}

static WavpackStreamReader64 leitor = {
    wv_ler_bytes, NULL, wv_onde, wv_ir, wv_andar, wv_devolver, wv_tamanho, wv_pode, NULL, NULL,
};

static void wv_fechar(void *s)
{
    wv_t *v = (wv_t *)s;
    if (!v) return;
    if (v->w) WavpackCloseFile(v->w);
    if (v->f) fclose(v->f);
    free(v->buf);
    free(v);
}

static void *wv_abrir(const char *caminho, dap_info_audio_t *info, char *erro, int tam_erro)
{
    wv_t *v = (wv_t *)calloc(1, sizeof(wv_t));
    if (!v) return NULL;
    v->f = fopen(caminho, "rb");
    char msg[80] = "";
    if (v->f) v->w = WavpackOpenFileInputEx64(&leitor, v->f, NULL, msg, OPEN_DSD_AS_PCM | OPEN_NORMALIZE | OPEN_2CH_MAX, 0);
    if (!v->w) {
        snprintf(erro, tam_erro, "o WavPack nao abre (%s)", msg[0] ? msg : "sem o arquivo");
        wv_fechar(v);
        return NULL;
    }
    v->canais = WavpackGetReducedChannels(v->w);
    v->bytes = WavpackGetBytesPerSample(v->w);
    v->flutuante = (WavpackGetMode(v->w) & MODE_FLOAT) != 0;
    if (v->canais < 1 || v->canais > 2 || v->bytes < 1 || v->bytes > 4) {
        snprintf(erro, tam_erro, "o WavPack tem um formato que o player nao toca");
        wv_fechar(v);
        return NULL;
    }
    info->taxa = (int)WavpackGetSampleRate(v->w);
    info->canais = v->canais;
    info->bits = WavpackGetBitsPerSample(v->w);
    int64_t n = WavpackGetNumSamples64(v->w);
    info->quadros = n > 0 ? (uint64_t)n : 0;
    return v;
}

static int wv_ler(void *s, float *estereo, int quadros)
{
    wv_t *v = (wv_t *)s;
    if (v->cap < quadros * v->canais) {
        free(v->buf);
        v->buf = (int32_t *)dap_alocar((size_t)quadros * v->canais * sizeof(int32_t));
        v->cap = v->buf ? quadros * v->canais : 0;
        if (!v->buf) return 0;
    }
    int n = (int)WavpackUnpackSamples(v->w, v->buf, (uint32_t)quadros);
    /* inteiro alinhado a direita no tamanho do recipiente (8 bits tambem com sinal); float vem como os bits dele */
    float escala = 1.0f / (float)(1u << (v->bytes * 8 - 1));
    for (int i = 0; i < n; i++)
        for (int c = 0; c < 2; c++) {
            int32_t x = v->buf[i * v->canais + (v->canais == 2 ? c : 0)];
            float y;
            if (v->flutuante) memcpy(&y, &x, sizeof(y));
            else y = (float)x * escala;
            estereo[2 * i + c] = y;
        }
    return n;
}

static int wv_buscar(void *s, uint64_t quadro)
{
    wv_t *v = (wv_t *)s;
    return WavpackSeekSample64(v->w, (int64_t)quadro);
}

const dap_formato_t dap_formato_wv = { wv_abrir, wv_ler, wv_buscar, wv_fechar };
