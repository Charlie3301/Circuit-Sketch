/* dap_wma.c -- o WMA (Windows Media Audio v1 e v2), no contêiner ASF.
 *
 * O decodificador e o libwma do Rockbox (src/wma; o do FFmpeg passado para
 * ponto fixo, LGPL 2.1 ou mais nova, com a IMDCT e a leitura de bits do
 * codeclib do Rockbox, GPL 2 ou mais nova). O ASF e lido aqui: o cabecalho
 * objeto por objeto (a capa pode ter centenas de KB, e nao precisa vir para
 * a memoria), e os pacotes de tamanho fixo do objeto de dados, cada um com
 * os seus payloads -- um objeto de midia pode vir partido em varios, e e
 * remontado antes de ir para o decodificador.
 *
 * O WMA Pro, o Lossless e o Voice sao outros codecs, e o WMA com DRM e
 * cifrado: o player diz qual e o caso, e nao toca. A busca vai ao pacote
 * proporcional e acerta pelo tempo de apresentacao do primeiro objeto.
 *
 * Um WMA aberto por vez: a memoria grande do libwma (wma_mem, no wmadeci.c)
 * e uma so -- e o player fecha a musica antes de abrir a seguinte.
 *
 * Este arquivo vai sob a GNU GPL versao 2 ou mais nova, como o codeclib.
 */
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include "wma/wmadec.h"
#include "dap_interno.h"

static const uint8_t GUID_CABECALHO[16] = { 0x30, 0x26, 0xB2, 0x75, 0x8E, 0x66, 0xCF, 0x11, 0xA6, 0xD9, 0x00, 0xAA, 0x00, 0x62, 0xCE, 0x6C };
static const uint8_t GUID_DADOS[16] = { 0x36, 0x26, 0xB2, 0x75, 0x8E, 0x66, 0xCF, 0x11, 0xA6, 0xD9, 0x00, 0xAA, 0x00, 0x62, 0xCE, 0x6C };
static const uint8_t GUID_ARQUIVO[16] = { 0xA1, 0xDC, 0xAB, 0x8C, 0x47, 0xA9, 0xCF, 0x11, 0x8E, 0xE4, 0x00, 0xC0, 0x0C, 0x20, 0x53, 0x65 };
static const uint8_t GUID_FLUXO[16] = { 0x91, 0x07, 0xDC, 0xB7, 0xB7, 0xA9, 0xCF, 0x11, 0x8E, 0xE6, 0x00, 0xC0, 0x0C, 0x20, 0x53, 0x65 };
static const uint8_t GUID_AUDIO[16] = { 0x40, 0x9E, 0x69, 0xF8, 0x4D, 0x5B, 0xCF, 0x11, 0xA8, 0xFD, 0x00, 0x80, 0x5F, 0x5C, 0x44, 0x2B };
static const uint8_t GUID_DRM[16] = { 0xFB, 0xB3, 0x11, 0x22, 0x23, 0xBD, 0xD2, 0x11, 0xB4, 0xB7, 0x00, 0xA0, 0xC9, 0x55, 0xFC, 0x6E };
static const uint8_t GUID_DRM2[16] = { 0x14, 0xE6, 0x8A, 0x29, 0x22, 0x26, 0x17, 0x4C, 0xB9, 0x35, 0xDA, 0xE0, 0x7E, 0xE9, 0x28, 0x9C };

typedef struct {
    FILE *f;
    WMADecodeContext *wma;
    asf_waveformatex_t wfx;
    long dados;                   /* onde o primeiro pacote comeca */
    uint64_t pacotes;             /* quantos pacotes ha */
    uint64_t prox;                /* o proximo pacote a ler */
    uint32_t preroll;             /* ms: o tempo de apresentacao comeca nele */
    uint64_t duracao_ms;
    uint8_t *pacote;
    /* o objeto de midia sendo montado (quando vem partido) */
    uint8_t *objeto;
    uint32_t cap_objeto, tam_objeto, no_objeto, num_objeto;
    int montando;
    /* a saida */
    float *pronto;
    int n_pronto, ja, cap_pronto;
    int64_t pular;                /* depois de uma busca: amostras a jogar fora (-1: ainda nao sabe) */
    uint64_t alvo;                /* a amostra que a busca pediu */
    int fim;
    float escala;
} wma_t;

static uint16_t le16(const uint8_t *p) { return (uint16_t)(p[0] | p[1] << 8); }
static uint32_t le32(const uint8_t *p) { return (uint32_t)p[0] | (uint32_t)p[1] << 8 | (uint32_t)p[2] << 16 | (uint32_t)p[3] << 24; }
static uint64_t le64(const uint8_t *p) { return (uint64_t)le32(p) | (uint64_t)le32(p + 4) << 32; }

/* Os campos de tamanho variavel do pacote: 0, 1, 2 ou 4 bytes. */
static int tamanho_do_campo(int tipo) { return tipo == 3 ? 4 : tipo; }

static uint32_t campo(const uint8_t *p, int tipo)
{
    switch (tipo) {
    case 1: return p[0];
    case 2: return le16(p);
    case 3: return le32(p);
    default: return 0;
    }
}

/* ------------------------------------------------------------ saida */

/* Garante lugar para mais n quadros na saida. */
static int lugar(wma_t *w, int n)
{
    if (w->n_pronto + n <= w->cap_pronto) return 1;
    int cap = w->cap_pronto ? w->cap_pronto : 4096;
    while (cap < w->n_pronto + n) cap *= 2;
    float *novo = (float *)dap_realocar(w->pronto, (size_t)cap * 2 * sizeof(float));
    if (!novo) return 0;
    w->pronto = novo;
    w->cap_pronto = cap;
    return 1;
}

/* Um codec packet (uma "superframe" do WMA): os quadros dele vao para a saida. */
static void decodificar_superquadro(wma_t *w, const uint8_t *b, int n)
{
    if (wma_decode_superframe_init(w->wma, b, n) < 0) return;
    for (int i = 0; i < w->wma->nb_frames; i++) {
        int q = wma_decode_superframe_frame(w->wma, b, n);
        if (q <= 0 || !lugar(w, q)) continue;
        const fixed32 *e = (*w->wma->frame_out)[0];
        const fixed32 *d = w->wfx.channels > 1 ? (*w->wma->frame_out)[1] : e;
        int inicio = 0;
        if (w->pular > 0) {
            inicio = w->pular < q ? (int)w->pular : q;
            w->pular -= inicio;
        }
        float *s = w->pronto + 2 * w->n_pronto;
        for (int k = inicio; k < q; k++) {
            *s++ = (float)e[k] * w->escala;
            *s++ = (float)d[k] * w->escala;
        }
        w->n_pronto += q - inicio;
    }
}

/* Um objeto de midia inteiro: varios codec packets de block_align bytes
 * (ou um so, quando o tamanho nao e multiplo, como o Rockbox faz). */
static void decodificar_objeto(wma_t *w, const uint8_t *b, uint32_t n, uint32_t apresentacao)
{
    /* depois da busca: o primeiro objeto diz de onde vem o audio */
    if (w->pular < 0) {
        uint64_t ms = apresentacao > w->preroll ? apresentacao - w->preroll : 0;
        uint64_t ali = ms * w->wfx.rate / 1000;
        uint64_t alvo = w->alvo + (uint64_t)w->wma->frame_len;   /* o atraso de um quadro */
        w->pular = alvo > ali ? (int64_t)(alvo - ali) : 0;
    }
    uint32_t passo = w->wfx.blockalign && n % w->wfx.blockalign == 0 ? w->wfx.blockalign : n;
    for (uint32_t o = 0; o + passo <= n; o += passo) decodificar_superquadro(w, b + o, (int)passo);
}

/* Um payload do fluxo de audio: inteiro, ou um pedaco do objeto. */
static void payload(wma_t *w, uint32_t num, uint32_t deslocamento, uint32_t tamanho_objeto, uint32_t apresentacao,
                    const uint8_t *b, uint32_t n)
{
    if (deslocamento == 0 && n >= tamanho_objeto) {
        w->montando = 0;
        decodificar_objeto(w, b, n, apresentacao);
        return;
    }
    if (deslocamento == 0) {
        if (tamanho_objeto > (1u << 20)) { w->montando = 0; return; }
        if (tamanho_objeto > w->cap_objeto) {
            uint8_t *novo = (uint8_t *)dap_realocar(w->objeto, tamanho_objeto);
            if (!novo) { w->montando = 0; return; }
            w->objeto = novo;
            w->cap_objeto = tamanho_objeto;
        }
        w->montando = 1;
        w->num_objeto = num;
        w->tam_objeto = tamanho_objeto;
        w->no_objeto = 0;
    }
    /* o pedaco do meio tem que ser a continuacao do objeto que esta sendo montado */
    if (!w->montando || num != w->num_objeto || deslocamento != w->no_objeto || w->no_objeto + n > w->tam_objeto) {
        w->montando = 0;
        return;
    }
    memcpy(w->objeto + w->no_objeto, b, n);
    w->no_objeto += n;
    if (w->no_objeto == w->tam_objeto) {
        w->montando = 0;
        decodificar_objeto(w, w->objeto, w->tam_objeto, apresentacao);
    }
}

/* Le e desmonta o proximo pacote. 0 no fim. */
static int ler_pacote(wma_t *w)
{
    uint32_t t = w->wfx.packet_size;
    if (w->prox >= w->pacotes) return 0;
    if (fseek(w->f, w->dados + (long)(w->prox * t), SEEK_SET) != 0 || fread(w->pacote, 1, t, w->f) != t) return 0;
    w->prox++;

    const uint8_t *p = w->pacote;
    uint32_t i = 0;
    uint8_t b = p[i++];
    if (b & 0x80) {               /* os dados de correcao de erro */
        i += b & 0x0F;
        b = p[i++];
    }
    uint8_t bandeiras = b, propriedades = p[i++];
    int tipo_tamanho = (bandeiras >> 5) & 3, tipo_sequencia = (bandeiras >> 1) & 3, tipo_enchimento = (bandeiras >> 3) & 3;
    if (i + tamanho_do_campo(tipo_tamanho) + tamanho_do_campo(tipo_sequencia) + tamanho_do_campo(tipo_enchimento) + 6 > t) return 1;
    uint32_t tamanho = campo(p + i, tipo_tamanho);
    i += tamanho_do_campo(tipo_tamanho);
    i += tamanho_do_campo(tipo_sequencia);
    uint32_t enchimento = campo(p + i, tipo_enchimento);
    i += tamanho_do_campo(tipo_enchimento);
    i += 6;                       /* o send time e a duracao */
    if (tipo_tamanho == 0 || tamanho > t) tamanho = t;
    if (tamanho < t) enchimento += t - tamanho;

    int varios = bandeiras & 1, quantos = 1, tipo_payload = 2;
    if (varios) {
        if (i >= t) return 1;
        quantos = p[i] & 0x3F;
        tipo_payload = (p[i] >> 6) & 3;
        i++;
    }
    int tipo_replicado = propriedades & 3, tipo_deslocamento = (propriedades >> 2) & 3, tipo_objeto = (propriedades >> 4) & 3;
    for (int k = 0; k < quantos; k++) {
        uint32_t cab = 1 + tamanho_do_campo(tipo_objeto) + tamanho_do_campo(tipo_deslocamento) + tamanho_do_campo(tipo_replicado);
        if (i + cab > t) return 1;
        int fluxo = p[i] & 0x7F;
        i++;
        uint32_t num = campo(p + i, tipo_objeto);
        i += tamanho_do_campo(tipo_objeto);
        uint32_t deslocamento = campo(p + i, tipo_deslocamento);
        i += tamanho_do_campo(tipo_deslocamento);
        uint32_t replicado = campo(p + i, tipo_replicado);
        i += tamanho_do_campo(tipo_replicado);
        uint32_t tamanho_objeto = 0, apresentacao = 0;
        int comprimido = replicado == 1;
        if (comprimido) {
            apresentacao = deslocamento;   /* no payload comprimido, o campo e o tempo */
            i += 1;                        /* o delta do tempo */
        } else {
            if (replicado >= 8 && i + 8 <= t) {
                tamanho_objeto = le32(p + i);
                apresentacao = le32(p + i + 4);
            }
            i += replicado;
        }
        uint32_t n;
        if (varios) {
            if (i + tamanho_do_campo(tipo_payload) > t) return 1;
            n = campo(p + i, tipo_payload);
            i += tamanho_do_campo(tipo_payload);
        } else {
            n = i + enchimento <= t ? t - i - enchimento : 0;
        }
        if (i + n > t) return 1;
        if (fluxo == w->wfx.audiostream) {
            if (comprimido) {
                /* sub-payloads: um byte de tamanho e o objeto inteiro */
                for (uint32_t o = 0; o < n;) {
                    uint32_t s = p[i + o++];
                    if (o + s > n) break;
                    decodificar_objeto(w, p + i + o, s, apresentacao);
                    o += s;
                }
            } else {
                payload(w, num, deslocamento, tamanho_objeto ? tamanho_objeto : n, apresentacao, p + i, n);
            }
        }
        i += n;
    }
    return 1;
}

/* ------------------------------------------------------- cabecalho */

static int ler_cabecalho(wma_t *w, uint64_t *duracao_100ns, char *erro, int tam_erro)
{
    uint8_t h[30];
    if (fseek(w->f, 0, SEEK_SET) != 0 || fread(h, 1, 30, w->f) != 30 || memcmp(h, GUID_CABECALHO, 16) != 0) {
        snprintf(erro, tam_erro, "nao e um arquivo ASF/WMA");
        return 0;
    }
    uint64_t tamanho = le64(h + 16);
    long p = 30, fim = (long)tamanho;
    int achou_arquivo = 0, achou_audio = 0;
    while (p + 24 <= fim) {
        uint8_t o[24];
        if (fseek(w->f, p, SEEK_SET) != 0 || fread(o, 1, 24, w->f) != 24) break;
        uint64_t t = le64(o + 16);
        if (t < 24 || t > (uint64_t)(fim - p)) break;
        if (!memcmp(o, GUID_DRM, 16) || !memcmp(o, GUID_DRM2, 16)) {
            snprintf(erro, tam_erro, "WMA protegido (DRM): o player nao toca");
            return 0;
        }
        if (!memcmp(o, GUID_ARQUIVO, 16) && t >= 24 + 80) {
            uint8_t b[80];
            if (fread(b, 1, 80, w->f) == 80) {
                *duracao_100ns = le64(b + 40);
                w->preroll = (uint32_t)le64(b + 56);
                uint32_t minimo = le32(b + 68), maximo = le32(b + 72);
                w->wfx.packet_size = maximo;
                if (minimo != maximo) w->wfx.packet_size = 0;   /* o ASF de audio tem pacote fixo */
                achou_arquivo = 1;
            }
        } else if (!memcmp(o, GUID_FLUXO, 16) && !achou_audio && t >= 24 + 54 + 18) {
            uint8_t b[54 + 18 + 46];
            size_t n = fread(b, 1, sizeof(b), w->f);
            if (n >= 54 + 18 && !memcmp(b, GUID_AUDIO, 16)) {
                uint32_t especifico = le32(b + 40);
                const uint8_t *x = b + 54;   /* o WAVEFORMATEX */
                w->wfx.audiostream = le16(b + 48) & 0x7F;
                w->wfx.codec_id = le16(x);
                w->wfx.channels = le16(x + 2);
                w->wfx.rate = le32(x + 4);
                w->wfx.bitrate = le32(x + 8) * 8;
                w->wfx.blockalign = le16(x + 12);
                w->wfx.bitspersample = le16(x + 14);
                uint16_t extra = especifico >= 18 ? le16(x + 16) : 0;
                if (extra > 46) extra = 46;
                if (54 + 18 + extra > n) extra = (uint16_t)(n - 54 - 18);
                w->wfx.datalen = extra;
                memcpy(w->wfx.data, x + 18, extra);
                achou_audio = 1;
            }
        }
        p += (long)t;
    }
    if (!achou_audio) { snprintf(erro, tam_erro, "o WMA nao tem trilha de audio"); return 0; }
    switch (w->wfx.codec_id) {
    case ASF_CODEC_ID_WMAV1: case ASF_CODEC_ID_WMAV2: break;
    case ASF_CODEC_ID_WMAPRO: snprintf(erro, tam_erro, "WMA Pro: o player toca o WMA 1 e o 2"); return 0;
    case 0x163: snprintf(erro, tam_erro, "WMA Lossless: o player toca o WMA 1 e o 2"); return 0;
    case ASF_CODEC_ID_WMAVOICE: snprintf(erro, tam_erro, "WMA Voice: o player toca o WMA 1 e o 2"); return 0;
    default: snprintf(erro, tam_erro, "o ASF tem um audio (0x%04X) que o player nao toca", w->wfx.codec_id); return 0;
    }
    if (!achou_arquivo || w->wfx.packet_size < 32 || w->wfx.packet_size > 65536 || w->wfx.channels < 1 || w->wfx.channels > 2 || !w->wfx.rate) {
        snprintf(erro, tam_erro, "o WMA tem um formato que o player nao toca");
        return 0;
    }

    /* o objeto de dados vem logo depois do cabecalho */
    uint8_t d[50];
    if (fseek(w->f, fim, SEEK_SET) != 0 || fread(d, 1, 50, w->f) != 50 || memcmp(d, GUID_DADOS, 16) != 0) {
        snprintf(erro, tam_erro, "o WMA nao tem os dados");
        return 0;
    }
    w->dados = fim + 50;
    w->pacotes = le64(d + 40);
    fseek(w->f, 0, SEEK_END);
    uint64_t cabem = (uint64_t)(ftell(w->f) - w->dados) / w->wfx.packet_size;
    if (!w->pacotes || w->pacotes > cabem) w->pacotes = cabem;
    return 1;
}

static void wma_fechar(void *s)
{
    wma_t *w = (wma_t *)s;
    if (!w) return;
    if (w->f) fclose(w->f);
    free(w->wma);
    free(w->pacote);
    free(w->objeto);
    free(w->pronto);
    free(w);
}

static void *wma_abrir(const char *caminho, dap_info_audio_t *info, char *erro, int tam_erro)
{
    wma_t *w = (wma_t *)calloc(1, sizeof(wma_t));
    if (!w) return NULL;
    w->f = fopen(caminho, "rb");
    uint64_t duracao = 0;
    if (!w->f) { snprintf(erro, tam_erro, "o WMA nao abre"); wma_fechar(w); return NULL; }
    if (!ler_cabecalho(w, &duracao, erro, tam_erro)) { wma_fechar(w); return NULL; }
    w->wma = (WMADecodeContext *)dap_alocar(sizeof(WMADecodeContext));
    w->pacote = (uint8_t *)dap_alocar(w->wfx.packet_size);
    if (!w->wma || !w->pacote) { snprintf(erro, tam_erro, "sem memoria para o WMA"); wma_fechar(w); return NULL; }
    memset(w->wma, 0, sizeof(WMADecodeContext));
    if (wma_decode_init(w->wma, &w->wfx) < 0) {
        snprintf(erro, tam_erro, "o WMA tem uma configuracao que o player nao toca");
        wma_fechar(w);
        return NULL;
    }
    /* A saida do libwma e ponto fixo com 29 bits de fracao (o "sample depth"
     * do Rockbox), com o sinal trocado em relacao ao decodificador do FFmpeg
     * (a IMDCT do Rockbox): sai invertida de volta. O primeiro quadro e o
     * atraso do codec (o silencio que o codificador pos na frente): vai fora,
     * e o audio comeca na amostra 0 do original. */
    w->escala = -1.0f / (float)(1 << 29);
    w->pular = w->wma->frame_len;
    info->taxa = (int)w->wfx.rate;
    info->canais = w->wfx.channels;
    info->bits = 16;
    /* a duracao do arquivo conta o preroll */
    uint64_t ms = duracao / 10000;
    ms = ms > w->preroll ? ms - w->preroll : ms;
    w->duracao_ms = ms;
    info->quadros = ms * w->wfx.rate / 1000;
    return w;
}

static int wma_ler(void *s, float *estereo, int quadros)
{
    wma_t *w = (wma_t *)s;
    int feitos = 0;
    while (feitos < quadros) {
        if (w->ja >= w->n_pronto) {
            w->ja = w->n_pronto = 0;
            while (w->n_pronto == 0 && !w->fim)
                if (!ler_pacote(w)) w->fim = 1;
            if (w->n_pronto == 0) break;
        }
        int n = w->n_pronto - w->ja;
        if (n > quadros - feitos) n = quadros - feitos;
        memcpy(estereo + 2 * feitos, w->pronto + 2 * w->ja, (size_t)n * 2 * sizeof(float));
        w->ja += n;
        feitos += n;
    }
    return feitos;
}

static int wma_buscar(void *s, uint64_t quadro)
{
    wma_t *w = (wma_t *)s;
    if (!w->pacotes || !w->duracao_ms) return 0;
    /* O WMA e de taxa constante: o pacote proporcional ao tempo, um antes (o
     * objeto que comeca nele pode ter vindo do anterior). O tempo de
     * apresentacao do primeiro objeto acerta o resto (decodificar_objeto). */
    uint64_t ms = quadro * 1000 / w->wfx.rate;
    uint64_t pacote = ms * w->pacotes / w->duracao_ms;
    if (pacote > 0) pacote--;
    if (pacote >= w->pacotes) pacote = w->pacotes - 1;
    w->prox = pacote;
    w->montando = 0;
    w->n_pronto = w->ja = 0;
    w->fim = 0;
    /* o estado do decodificador (o reservatorio de bits e a sobreposicao) recomeca */
    w->wma->last_superframe_len = 0;
    w->wma->last_bitoffset = 0;
    memset(*w->wma->frame_out, 0, sizeof(*w->wma->frame_out));
    w->alvo = quadro;
    w->pular = quadro ? -1 : w->wma->frame_len;
    return 1;
}

const dap_formato_t dap_formato_wma = { wma_abrir, wma_ler, wma_buscar, wma_fechar };
