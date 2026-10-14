/* dap_formatos.c -- de arquivo para PCM em float, estereo.
 *
 * O formato vem do CONTEUDO, e nao da extensao: um WAV salvo como .mp3, um
 * HTML que o navegador gravou como .mp3 -- uma biblioteca de verdade tem os
 * dois. MP3, FLAC (tambem dentro do Ogg), WAV (PCM, float, ADPCM, A-law,
 * u-law, RF64, W64) e Ogg Vorbis sao das bibliotecas dr_libs e stb_vorbis
 * (dominio publico); o AIFF/AIFC e o DSD (DSF e DSDIFF) sao daqui. O ALAC
 * (no M4A), o Opus e o WavPack moram nos seus arquivos (dap_m4a.c,
 * dap_opus.c, dap_wv.c), atras da mesma interface.
 *
 * O DSD e 1 bit a 2,8224 MHz (DSD64) ou 5,6448 MHz (DSD128): vira PCM de
 * 88,2 kHz por dois filtros FIR -- o primeiro, com tabelas de 8 bits por
 * vez, desce para 352,8 kHz; o segundo, em float, para 88,2 kHz.
 */
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <math.h>
#include "dr_mp3.h"
#include "dr_flac.h"
#include "dr_wav.h"
#define STB_VORBIS_HEADER_ONLY
#include "stb_vorbis.c"
#include "dap_interno.h"

static const char *const nomes[DAP_FMT_QUANTOS] = {
    "?", "MP3", "FLAC", "WAV", "AIFF", "OGG", "DSF", "DFF", "AAC", "OPUS", "WMA", "APE", "WV", "ALAC",
};

const char *dap_nome_do_formato(int formato)
{
    return formato > 0 && formato < DAP_FMT_QUANTOS ? nomes[formato] : "?";
}

int dap_formato_tocavel(int formato)
{
    switch (formato) {
    case DAP_FMT_MP3: case DAP_FMT_FLAC: case DAP_FMT_WAV: case DAP_FMT_AIFF: case DAP_FMT_OGG:
    case DAP_FMT_DSF: case DAP_FMT_DFF: case DAP_FMT_ALAC: case DAP_FMT_OPUS: case DAP_FMT_WV:
        return 1;
    default:
        return 0;
    }
}

/* O formato que mora no seu arquivo, ou NULL. */
static const dap_formato_t *de_fora(int formato)
{
    switch (formato) {
    case DAP_FMT_ALAC: return &dap_formato_alac;
    case DAP_FMT_OPUS: return &dap_formato_opus;
    case DAP_FMT_WV: return &dap_formato_wv;
    default: return NULL;
    }
}

int dap_extensao_de_audio(const char *nome)
{
    static const char *const extensoes[] = {
        ".mp3", ".mp2", ".mp1", ".flac", ".fla", ".oga", ".ogg", ".wav", ".wave", ".w64", ".rf64", ".bwf", ".aif",
        ".aiff", ".aifc", ".dsf", ".dff", ".m4a", ".m4b", ".mp4", ".aac", ".alac", ".opus", ".wma", ".ape", ".wv", NULL,
    };
    const char *p = strrchr(nome, '.');
    if (!p) return 0;
    for (int i = 0; extensoes[i]; i++)
        if (strcasecmp(p, extensoes[i]) == 0) return 1;
    return 0;
}

/* Um cabecalho de quadro MPEG valido (versao, camada, taxa de bits e de amostra) -- e o tamanho dele. */
static int quadro_mpeg(const uint8_t *h)
{
    if (h[0] != 0xFF || (h[1] & 0xE0) != 0xE0) return 0;
    int versao = (h[1] >> 3) & 3, camada = (h[1] >> 1) & 3, br = h[2] >> 4, sr = (h[2] >> 2) & 3, pad = (h[2] >> 1) & 1;
    if (versao == 1 || camada == 0 || br == 0 || br == 15 || sr == 3) return 0;
    static const int kbps[2][3][16] = {
        { {0, 32, 64, 96, 128, 160, 192, 224, 256, 288, 320, 352, 384, 416, 448, 0},
          {0, 32, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320, 384, 0},
          {0, 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320, 0} },
        { {0, 32, 48, 56, 64, 80, 96, 112, 128, 144, 160, 176, 192, 224, 256, 0},
          {0, 8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160, 0},
          {0, 8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160, 0} },
    };
    static const int hz[4][3] = { {11025, 12000, 8000}, {0, 0, 0}, {22050, 24000, 16000}, {44100, 48000, 32000} };
    int mpeg1 = versao == 3, c = 3 - camada;   /* c: 0 = camada I, 1 = II, 2 = III */
    int taxa = kbps[mpeg1 ? 0 : 1][c][br] * 1000, amostras = hz[versao][sr];
    if (!taxa || !amostras) return 0;
    if (c == 0) return (12 * taxa / amostras + pad) * 4;
    return (c == 2 && !mpeg1 ? 72 : 144) * taxa / amostras + pad;
}

int dap_detectar_formato(const char *caminho)
{
    FILE *f = fopen(caminho, "rb");
    if (!f) return DAP_FMT_NADA;
    uint8_t b[64];
    memset(b, 0, sizeof(b));
    size_t n = fread(b, 1, sizeof(b), f);
    long inicio = 0;

    /* A etiqueta ID3v2 na frente: o audio e o que vem depois dela. */
    if (n >= 10 && memcmp(b, "ID3", 3) == 0) {
        inicio = 10 + ((b[6] & 0x7F) << 21 | (b[7] & 0x7F) << 14 | (b[8] & 0x7F) << 7 | (b[9] & 0x7F)) + ((b[5] & 0x10) ? 10 : 0);
        fseek(f, inicio, SEEK_SET);
        memset(b, 0, sizeof(b));
        n = fread(b, 1, sizeof(b), f);
    }

    int formato = DAP_FMT_NADA;
    if (n < 12) formato = DAP_FMT_NADA;
    else if (memcmp(b, "fLaC", 4) == 0) formato = DAP_FMT_FLAC;
    else if ((memcmp(b, "RIFF", 4) == 0 || memcmp(b, "RF64", 4) == 0 || memcmp(b, "BW64", 4) == 0) && memcmp(b + 8, "WAVE", 4) == 0) formato = DAP_FMT_WAV;
    else if (memcmp(b, "riff", 4) == 0) formato = DAP_FMT_WAV;   /* Sony Wave64 */
    else if (memcmp(b, "FORM", 4) == 0 && (memcmp(b + 8, "AIFF", 4) == 0 || memcmp(b + 8, "AIFC", 4) == 0)) formato = DAP_FMT_AIFF;
    else if (memcmp(b, "DSD ", 4) == 0) formato = DAP_FMT_DSF;
    else if (memcmp(b, "FRM8", 4) == 0 && memcmp(b + 12, "DSD ", 4) == 0) formato = DAP_FMT_DFF;
    else if (memcmp(b + 4, "ftyp", 4) == 0) formato = dap_m4a_codec(f);   /* ALAC ou AAC: o stsd diz */
    else if (memcmp(b, "\x30\x26\xB2\x75", 4) == 0) formato = DAP_FMT_WMA;
    else if (memcmp(b, "MAC ", 4) == 0) formato = DAP_FMT_APE;
    else if (memcmp(b, "wvpk", 4) == 0) formato = DAP_FMT_WV;
    else if (memcmp(b, "OggS", 4) == 0) {
        /* O que vai dentro do Ogg: o primeiro pacote diz. */
        uint8_t p[96];
        fseek(f, inicio, SEEK_SET);
        size_t k = fread(p, 1, sizeof(p), f);
        formato = DAP_FMT_OGG;
        for (size_t i = 0; i + 8 <= k; i++) {
            if (memcmp(p + i, "OpusHead", 8) == 0) { formato = DAP_FMT_OPUS; break; }
            if (memcmp(p + i, "\x7F" "FLAC", 5) == 0) { formato = DAP_FMT_FLAC; break; }
            if (memcmp(p + i, "\x01vorbis", 7) == 0) { formato = DAP_FMT_OGG; break; }
        }
    } else if (b[0] == 0xFF && (b[1] & 0xF6) == 0xF0) formato = DAP_FMT_M4A;   /* AAC cru (ADTS) */
    else {
        /* MPEG: dois quadros validos seguidos, procurados nos primeiros 8 KB
         * (alguns arquivos tem lixo antes do primeiro). */
        uint8_t *busca = (uint8_t *)malloc(8192 + 4);
        if (busca) {
            fseek(f, inicio, SEEK_SET);
            size_t k = fread(busca, 1, 8192, f);
            for (size_t i = 0; i + 4 <= k && !formato; i++) {
                int t = quadro_mpeg(busca + i);
                if (t > 0 && (i + t + 4 > k || quadro_mpeg(busca + i + t) > 0)) formato = DAP_FMT_MP3;
            }
            free(busca);
        }
    }
    fclose(f);
    return formato;
}

/* ===================================================================
 *  o decodificador
 * =================================================================== */
typedef struct {
    FILE *f;
    long base;        /* onde o "arquivo" do dr_mp3 comeca (a busca aproximada) */
    long tamanho;
} fonte_mp3_t;

typedef struct dsd dsd_t;

struct dap_decodificador {
    int formato, canais, taxa;
    union {
        drmp3 *mp3;
        drflac *flac;
        drwav *wav;
        stb_vorbis *ogg;
    } u;
    fonte_mp3_t fonte;
    uint64_t posicao;     /* o quadro em que esta (o dr_mp3 recomeca do zero na busca aproximada) */
    uint64_t total;
    float *temp;
    int temp_cap;
    /* AIFF */
    FILE *f;
    long dados, quadros_aiff;
    int bits, bytes_quadro, grande, flutuante;
    uint8_t *bruto;
    int bruto_cap;
    /* DSD */
    dsd_t *dsd;
    /* ALAC, Opus e WavPack: o formato e o estado dele */
    const dap_formato_t *extra;
    void *estado;
};

static float *temporario(dap_decodificador_t *d, int floats)
{
    if (d->temp_cap < floats) {
        free(d->temp);
        d->temp = (float *)dap_alocar(floats * sizeof(float));
        d->temp_cap = d->temp ? floats : 0;
    }
    return d->temp;
}

/* O dr_mp3 le por estas: um FILE com o comeco deslocado. */
static size_t mp3_ler(void *u, void *p, size_t n)
{
    fonte_mp3_t *s = (fonte_mp3_t *)u;
    return fread(p, 1, n, s->f);
}

static drmp3_bool32 mp3_buscar(void *u, int deslocamento, drmp3_seek_origin de)
{
    fonte_mp3_t *s = (fonte_mp3_t *)u;
    long alvo = de == DRMP3_SEEK_SET ? s->base + deslocamento
              : de == DRMP3_SEEK_END ? s->tamanho + deslocamento
              : ftell(s->f) + deslocamento;
    if (alvo < s->base) alvo = s->base;
    return fseek(s->f, alvo, SEEK_SET) == 0;
}

static drmp3_bool32 mp3_onde(void *u, drmp3_int64 *cursor)
{
    fonte_mp3_t *s = (fonte_mp3_t *)u;
    *cursor = ftell(s->f) - s->base;
    return DRMP3_TRUE;
}

static int abrir_mp3(dap_decodificador_t *d, long base)
{
    if (!d->u.mp3) d->u.mp3 = (drmp3 *)dap_alocar(sizeof(drmp3));
    else drmp3_uninit(d->u.mp3);
    if (!d->u.mp3) return 0;
    d->fonte.base = base;
    fseek(d->fonte.f, base, SEEK_SET);
    return drmp3_init(d->u.mp3, mp3_ler, mp3_buscar, mp3_onde, NULL, &d->fonte, NULL);
}

/* ---------------------------------------------------------------- AIFF */

static uint32_t be32(const uint8_t *p) { return (uint32_t)p[0] << 24 | (uint32_t)p[1] << 16 | (uint32_t)p[2] << 8 | p[3]; }
static uint64_t be64(const uint8_t *p) { return (uint64_t)be32(p) << 32 | be32(p + 4); }
static uint32_t le32(const uint8_t *p) { return (uint32_t)p[3] << 24 | (uint32_t)p[2] << 16 | (uint32_t)p[1] << 8 | p[0]; }
static uint64_t le64(const uint8_t *p) { return (uint64_t)le32(p + 4) << 32 | le32(p); }

/* O "extended" de 80 bits do AIFF (a taxa de amostragem). */
static double estendido(const uint8_t *p)
{
    int expoente = ((p[0] & 0x7F) << 8) | p[1];
    uint64_t mantissa = be64(p + 2);
    if (!expoente && !mantissa) return 0;
    double v = (double)mantissa * pow(2.0, expoente - 16383 - 63);
    return (p[0] & 0x80) ? -v : v;
}

static int abrir_aiff(dap_decodificador_t *d, const char *caminho, char *erro, int tam)
{
    d->f = fopen(caminho, "rb");
    if (!d->f) return 0;
    uint8_t h[12];
    if (fread(h, 1, 12, d->f) != 12) return 0;
    int aifc = memcmp(h + 8, "AIFC", 4) == 0;
    int achou_comm = 0;
    d->grande = 1;
    for (;;) {
        uint8_t c[8];
        if (fread(c, 1, 8, d->f) != 8) break;
        uint32_t t = be32(c + 4);
        long depois = ftell(d->f) + t + (t & 1);
        if (memcmp(c, "COMM", 4) == 0) {
            uint8_t m[26];
            memset(m, 0, sizeof(m));
            if (fread(m, 1, t < 26 ? t : 26, d->f) < 18) return 0;
            d->canais = (m[0] << 8) | m[1];
            d->quadros_aiff = (long)be32(m + 2);
            d->bits = (m[6] << 8) | m[7];
            d->taxa = (int)(estendido(m + 8) + 0.5);
            if (aifc && t >= 22) {
                if (memcmp(m + 18, "sowt", 4) == 0) d->grande = 0;
                else if (memcmp(m + 18, "fl32", 4) == 0 || memcmp(m + 18, "FL32", 4) == 0) d->flutuante = 1;
                else if (memcmp(m + 18, "NONE", 4) != 0 && memcmp(m + 18, "twos", 4) != 0) {
                    snprintf(erro, tam, "AIFF-C comprimido (%.4s) nao e tocado", m + 18);
                    return 0;
                }
            }
            achou_comm = 1;
        } else if (memcmp(c, "SSND", 4) == 0) {
            uint8_t s[8];
            if (fread(s, 1, 8, d->f) != 8) return 0;
            d->dados = ftell(d->f) + (long)be32(s);
        }
        fseek(d->f, depois, SEEK_SET);
    }
    if (!achou_comm || !d->dados || d->canais <= 0 || d->taxa <= 0 || (d->bits != 8 && d->bits != 16 && d->bits != 24 && d->bits != 32))
        return 0;
    d->bytes_quadro = d->canais * ((d->bits + 7) / 8);
    fseek(d->f, d->dados, SEEK_SET);
    d->total = (uint64_t)d->quadros_aiff;
    return 1;
}

static int ler_aiff(dap_decodificador_t *d, float *saida, int n)
{
    if ((uint64_t)n > d->total - d->posicao) n = (int)(d->total - d->posicao);
    if (n <= 0) return 0;
    int bytes = n * d->bytes_quadro;
    if (!d->bruto || d->bruto_cap < bytes) {
        free(d->bruto);
        d->bruto = (uint8_t *)dap_alocar(bytes);
        d->bruto_cap = d->bruto ? bytes : 0;
    }
    if (!d->bruto) return 0;
    int lidos = (int)fread(d->bruto, 1, bytes, d->f) / d->bytes_quadro;
    int b = (d->bits + 7) / 8;
    for (int i = 0; i < lidos; i++) {
        float lr[2] = { 0, 0 };
        for (int c = 0; c < d->canais && c < 2; c++) {
            const uint8_t *p = d->bruto + i * d->bytes_quadro + c * b;
            float v;
            if (d->flutuante) {
                uint32_t u = d->grande ? be32(p) : le32(p);
                memcpy(&v, &u, 4);
            } else {
                int32_t x = 0;
                for (int k = 0; k < b; k++) x = (x << 8) | (d->grande ? p[k] : p[b - 1 - k]);
                x <<= 32 - 8 * b;
                v = (float)x / 2147483648.0f;   /* o AIFF de 8 bits ja e com sinal */
            }
            lr[c] = v;
        }
        if (d->canais == 1) lr[1] = lr[0];
        saida[2 * i] = lr[0];
        saida[2 * i + 1] = lr[1];
    }
    d->posicao += lidos;
    return lidos;
}

/* ---------------------------------------------------------------- DSD */

#define DSD_TAPS1 128                   /* o primeiro filtro: 16 bytes de historia */
#define DSD_BYTES1 (DSD_TAPS1 / 8)
#define DSD_TAPS2 63                    /* o segundo, a 352,8 kHz */

struct dsd {
    int dff;                    /* DSDIFF (bytes intercalados, MSB primeiro) ou DSF (blocos, LSB primeiro) */
    int lsb_primeiro;
    int canais, taxa_dsd;       /* 2822400 ou 5644800 */
    int decima1, decima2;       /* 8 ou 16; e 4 */
    long dados, bytes_por_canal_total;
    int bloco;                  /* DSF: bytes por canal em cada bloco */
    uint64_t byte_atual;        /* bytes de cada canal ja lidos */
    float tab[DSD_BYTES1][256]; /* a soma dos 8 coeficientes de cada byte */
    uint8_t hist[2][DSD_BYTES1];
    float coef2[DSD_TAPS2];
    float hist2[2][DSD_TAPS2];
    int pos2, fase2;
    uint8_t *bloco_buf;         /* DSF: um bloco de cada canal */
    int pos_no_bloco, bloco_cheio;
};

static void projetar(float *h, int n, double corte_normalizado)
{
    /* sinc com janela de Blackman, ganho 1 no DC */
    double soma = 0;
    for (int i = 0; i < n; i++) {
        double m = i - (n - 1) / 2.0;
        double s = m == 0 ? 2 * corte_normalizado : sin(2 * M_PI * corte_normalizado * m) / (M_PI * m);
        double w = 0.42 - 0.5 * cos(2 * M_PI * i / (n - 1)) + 0.08 * cos(4 * M_PI * i / (n - 1));
        h[i] = (float)(s * w);
        soma += h[i];
    }
    for (int i = 0; i < n; i++) h[i] = (float)(h[i] / soma);
}

static int abrir_dsd(dap_decodificador_t *d, const char *caminho, int dff, char *erro, int tam)
{
    d->f = fopen(caminho, "rb");
    if (!d->f) return 0;
    dsd_t *s = (dsd_t *)dap_alocar(sizeof(dsd_t));
    if (!s) return 0;
    memset(s, 0, sizeof(*s));
    d->dsd = s;
    s->dff = dff;
    uint8_t h[96];

    if (!dff) {
        /* DSF: "DSD " (28), "fmt " (52), "data" (12 + dados) */
        if (fread(h, 1, 28 + 52 + 12, d->f) != 92 || memcmp(h + 28, "fmt ", 4) != 0) return 0;
        const uint8_t *fmt = h + 28;
        if (le32(fmt + 16) != 0) { snprintf(erro, tam, "DSF com formato %u", (unsigned)le32(fmt + 16)); return 0; }
        s->canais = (int)le32(fmt + 24);
        s->taxa_dsd = (int)le32(fmt + 28);
        s->lsb_primeiro = le32(fmt + 32) == 1;
        uint64_t amostras = le64(fmt + 36);
        s->bloco = (int)le32(fmt + 44);
        if (memcmp(h + 80, "data", 4) != 0) return 0;
        s->dados = 92;
        s->bytes_por_canal_total = (long)(amostras / 8);
        s->bloco_buf = (uint8_t *)dap_alocar((size_t)s->bloco * s->canais);
        if (!s->bloco_buf) return 0;
        s->pos_no_bloco = s->bloco;   /* vazio: le no primeiro uso */
    } else {
        /* DSDIFF: FRM8 > PROP (FS, CHNL, CMPR) e o bloco "DSD " com os dados */
        if (fread(h, 1, 16, d->f) != 16) return 0;
        long fim = 12 + (long)be64(h + 4);
        long pos = 16;
        while (pos + 12 <= fim) {
            fseek(d->f, pos, SEEK_SET);
            if (fread(h, 1, 12, d->f) != 12) break;
            uint64_t t = be64(h + 4);
            if (memcmp(h, "PROP", 4) == 0) {
                long p = pos + 16, fim_prop = pos + 12 + (long)t;
                while (p + 12 <= fim_prop) {
                    uint8_t c[12], v[8];
                    fseek(d->f, p, SEEK_SET);
                    if (fread(c, 1, 12, d->f) != 12) break;
                    uint64_t tc = be64(c + 4);
                    if (fread(v, 1, 8, d->f) < 4) break;
                    if (memcmp(c, "FS  ", 4) == 0) s->taxa_dsd = (int)be32(v);
                    else if (memcmp(c, "CHNL", 4) == 0) s->canais = (v[0] << 8) | v[1];
                    else if (memcmp(c, "CMPR", 4) == 0 && memcmp(v, "DSD ", 4) != 0) {
                        snprintf(erro, tam, "DSDIFF comprimido (DST) nao e tocado");
                        return 0;
                    }
                    p += 12 + (long)tc + (long)(tc & 1);
                }
            } else if (memcmp(h, "DSD ", 4) == 0) {
                s->dados = pos + 12;
                s->bytes_por_canal_total = s->canais > 0 ? (long)(t / s->canais) : 0;
            }
            pos += 12 + (long)t + (long)(t & 1);
        }
    }
    if (s->canais < 1 || s->canais > 6 || (s->taxa_dsd != 2822400 && s->taxa_dsd != 5644800) || !s->dados) {
        snprintf(erro, tam, "DSD de %d Hz e %d canais nao e tocado", s->taxa_dsd, s->canais);
        return 0;
    }

    /* 2,8224 MHz / 8 = 352,8 kHz (DSD128: / 16), e / 4 = 88,2 kHz */
    s->decima1 = s->taxa_dsd == 2822400 ? 8 : 16;
    s->decima2 = 4;
    float h1[DSD_TAPS1];
    projetar(h1, DSD_TAPS1, 40000.0 / s->taxa_dsd);
    for (int k = 0; k < DSD_BYTES1; k++)
        for (int b = 0; b < 256; b++) {
            float soma = 0;
            for (int bit = 0; bit < 8; bit++) {
                /* o byte mais antigo e o k = 0; dentro dele, o bit mais significativo e o primeiro */
                int ligado = (b >> (7 - bit)) & 1;
                soma += h1[k * 8 + bit] * (ligado ? 1.0f : -1.0f);
            }
            s->tab[k][b] = soma;
        }
    projetar(s->coef2, DSD_TAPS2, 30000.0 / (s->taxa_dsd / s->decima1));
    d->canais = s->canais;
    d->taxa = s->taxa_dsd / s->decima1 / s->decima2;
    d->total = (uint64_t)s->bytes_por_canal_total * 8 / (s->decima1 * s->decima2);
    fseek(d->f, s->dados, SEEK_SET);
    return 1;
}

static uint8_t inverter_bits(uint8_t b)
{
    b = (uint8_t)((b & 0xF0) >> 4 | (b & 0x0F) << 4);
    b = (uint8_t)((b & 0xCC) >> 2 | (b & 0x33) << 2);
    return (uint8_t)((b & 0xAA) >> 1 | (b & 0x55) << 1);
}

/* O proximo byte de DSD de cada canal (MSB = o bit mais antigo). 0: acabou. */
static int proximos_bytes(dap_decodificador_t *d, uint8_t *bytes)
{
    dsd_t *s = d->dsd;
    if ((long)s->byte_atual >= s->bytes_por_canal_total) return 0;
    if (s->dff) {
        uint8_t b[8];
        if ((int)fread(b, 1, s->canais, d->f) != s->canais) return 0;
        for (int c = 0; c < s->canais && c < 2; c++) bytes[c] = b[c];
    } else {
        if (s->pos_no_bloco >= s->bloco) {
            if ((int)fread(s->bloco_buf, 1, (size_t)s->bloco * s->canais, d->f) < s->bloco * s->canais) return 0;
            s->pos_no_bloco = 0;
        }
        for (int c = 0; c < s->canais && c < 2; c++) {
            uint8_t b = s->bloco_buf[c * s->bloco + s->pos_no_bloco];
            bytes[c] = s->lsb_primeiro ? inverter_bits(b) : b;
        }
        s->pos_no_bloco++;
    }
    if (s->canais == 1) bytes[1] = bytes[0];
    s->byte_atual++;
    return 1;
}

static int ler_dsd(dap_decodificador_t *d, float *saida, int n)
{
    dsd_t *s = d->dsd;
    int feitos = 0, bytes_por_saida1 = s->decima1 / 8;
    while (feitos < n) {
        /* um quadro a 352,8 kHz: anda decima1/8 bytes e soma pelas tabelas */
        float a[2];
        for (int k = 0; k < bytes_por_saida1; k++) {
            uint8_t b[2];
            if (!proximos_bytes(d, b)) return feitos;
            for (int c = 0; c < 2; c++) {
                memmove(s->hist[c], s->hist[c] + 1, DSD_BYTES1 - 1);
                s->hist[c][DSD_BYTES1 - 1] = b[c];
            }
        }
        for (int c = 0; c < 2; c++) {
            float soma = 0;
            for (int k = 0; k < DSD_BYTES1; k++) soma += s->tab[k][s->hist[c][k]];
            a[c] = soma;
            s->hist2[c][s->pos2] = soma;
        }
        s->pos2 = (s->pos2 + 1) % DSD_TAPS2;
        if (++s->fase2 < s->decima2) continue;
        s->fase2 = 0;
        /* o segundo filtro, so nas amostras que ficam */
        for (int c = 0; c < 2; c++) {
            float soma = 0;
            int p = s->pos2;
            for (int k = 0; k < DSD_TAPS2; k++) {
                soma += s->coef2[k] * s->hist2[c][p];
                if (++p == DSD_TAPS2) p = 0;
            }
            /* o DSD cheio (+-1) e +6 dB acima do 0 dBFS do PCM: o padrao e -6 dB */
            saida[2 * feitos + c] = soma * 0.5f;
        }
        (void)a;
        feitos++;
        d->posicao++;
    }
    return feitos;
}

static int buscar_dsd(dap_decodificador_t *d, uint64_t quadro)
{
    dsd_t *s = d->dsd;
    uint64_t byte = quadro * (s->decima1 * s->decima2) / 8;
    if (!s->dff) byte = byte / s->bloco * s->bloco;   /* comeca num bloco */
    if ((long)byte > s->bytes_por_canal_total) byte = (uint64_t)s->bytes_por_canal_total;
    long pos = s->dados + (long)(s->dff ? byte * s->canais : byte * s->canais);
    if (fseek(d->f, pos, SEEK_SET) != 0) return 0;
    s->byte_atual = byte;
    s->pos_no_bloco = s->bloco;
    memset(s->hist, 0, sizeof(s->hist));
    memset(s->hist2, 0, sizeof(s->hist2));
    s->fase2 = 0;
    d->posicao = byte * 8 / (s->decima1 * s->decima2);
    return 1;
}

/* =================================================================== */

dap_decodificador_t *dap_abrir_audio(const char *caminho, dap_info_audio_t *info, char *erro, int tam_erro)
{
    memset(info, 0, sizeof(*info));
    if (erro && tam_erro) erro[0] = 0;
    int formato = dap_detectar_formato(caminho);
    info->formato = formato;
    if (!dap_formato_tocavel(formato)) {
        if (formato == DAP_FMT_NADA) snprintf(erro, tam_erro, "nao e um arquivo de audio");
        else snprintf(erro, tam_erro, "%s ainda nao e tocado por este player", dap_nome_do_formato(formato));
        return NULL;
    }

    dap_decodificador_t *d = (dap_decodificador_t *)calloc(1, sizeof(*d));
    if (!d) return NULL;
    d->formato = formato;
    int ok = 0;
    switch (formato) {
    case DAP_FMT_MP3:
        d->fonte.f = fopen(caminho, "rb");
        if (d->fonte.f) {
            fseek(d->fonte.f, 0, SEEK_END);
            d->fonte.tamanho = ftell(d->fonte.f);
            ok = abrir_mp3(d, 0);
        }
        if (ok) {
            d->canais = (int)d->u.mp3->channels;
            d->taxa = (int)d->u.mp3->sampleRate;
            d->total = d->u.mp3->totalPCMFrameCount != DRMP3_UINT64_MAX ? d->u.mp3->totalPCMFrameCount : 0;
            info->bits = 16;
        }
        break;
    case DAP_FMT_FLAC:
        d->u.flac = drflac_open_file(caminho, NULL);
        if ((ok = d->u.flac != NULL)) {
            d->canais = d->u.flac->channels;
            d->taxa = (int)d->u.flac->sampleRate;
            d->total = d->u.flac->totalPCMFrameCount;
            info->bits = d->u.flac->bitsPerSample;
        }
        break;
    case DAP_FMT_WAV:
        d->u.wav = (drwav *)dap_alocar(sizeof(drwav));
        if (d->u.wav && drwav_init_file(d->u.wav, caminho, NULL)) {
            ok = 1;
            d->canais = d->u.wav->channels;
            d->taxa = (int)d->u.wav->sampleRate;
            d->total = d->u.wav->totalPCMFrameCount;
            info->bits = d->u.wav->bitsPerSample;
        } else {
            free(d->u.wav);
            d->u.wav = NULL;
        }
        break;
    case DAP_FMT_OGG: {
        int e = 0;
        d->u.ogg = stb_vorbis_open_filename(caminho, &e, NULL);
        if ((ok = d->u.ogg != NULL)) {
            stb_vorbis_info vi = stb_vorbis_get_info(d->u.ogg);
            d->canais = vi.channels;
            d->taxa = (int)vi.sample_rate;
            d->total = stb_vorbis_stream_length_in_samples(d->u.ogg);
            info->bits = 16;
        }
        break;
    }
    case DAP_FMT_AIFF:
        ok = abrir_aiff(d, caminho, erro, tam_erro);
        info->bits = d->bits;
        break;
    case DAP_FMT_DSF:
    case DAP_FMT_DFF:
        ok = abrir_dsd(d, caminho, formato == DAP_FMT_DFF, erro, tam_erro);
        info->bits = 1;
        break;
    default:
        if ((d->extra = de_fora(formato)) != NULL) {
            d->estado = d->extra->abrir(caminho, info, erro, tam_erro);
            if ((ok = d->estado != NULL)) {
                d->canais = info->canais;
                d->taxa = info->taxa;
                d->total = info->quadros;
            }
        }
        break;
    }

    if (!ok || d->canais <= 0 || d->taxa <= 0) {
        if (erro && !erro[0]) snprintf(erro, tam_erro, "o %s esta estragado ou e de um tipo que nao abre", dap_nome_do_formato(formato));
        dap_fechar_audio(d);
        return NULL;
    }
    info->taxa = d->taxa;
    info->canais = d->canais > 2 ? 2 : d->canais;
    info->quadros = d->total;
    return d;
}

int dap_ler_audio(dap_decodificador_t *d, float *saida, int n)
{
    if (!d || n <= 0) return 0;
    if (d->extra) {
        int lidos = d->extra->ler(d->estado, saida, n);
        if (lidos > 0) d->posicao += (uint64_t)lidos;
        return lidos;
    }
    if (d->formato == DAP_FMT_AIFF) return ler_aiff(d, saida, n);
    if (d->formato == DAP_FMT_DSF || d->formato == DAP_FMT_DFF) return ler_dsd(d, saida, n);

    int canais = d->canais;
    float *destino = canais == 2 ? saida : temporario(d, n * canais);
    if (!destino) return 0;
    int lidos = 0;
    switch (d->formato) {
    case DAP_FMT_MP3: lidos = (int)drmp3_read_pcm_frames_f32(d->u.mp3, (drmp3_uint64)n, destino); break;
    case DAP_FMT_FLAC: lidos = (int)drflac_read_pcm_frames_f32(d->u.flac, (drflac_uint64)n, destino); break;
    case DAP_FMT_WAV: lidos = (int)drwav_read_pcm_frames_f32(d->u.wav, (drwav_uint64)n, destino); break;
    case DAP_FMT_OGG: lidos = stb_vorbis_get_samples_float_interleaved(d->u.ogg, canais, destino, n * canais); break;
    }
    if (canais != 2)
        for (int i = 0; i < lidos; i++) {
            float l = destino[i * canais], r = canais == 1 ? l : destino[i * canais + 1];
            saida[2 * i] = l;
            saida[2 * i + 1] = r;
        }
    d->posicao += lidos;
    return lidos;
}

int dap_buscar_audio(dap_decodificador_t *d, uint64_t quadro)
{
    if (!d) return 0;
    if (d->total && quadro > d->total) quadro = d->total;
    if (d->extra) {
        int certo = d->extra->buscar(d->estado, quadro);
        if (certo) d->posicao = quadro;
        return certo;
    }
    int ok = 0;
    switch (d->formato) {
    case DAP_FMT_MP3: {
        /* Para a frente e perto: decodifica ate la. Longe ou para tras:
         * pula para a fracao do arquivo e o dr_mp3 acha o proximo quadro. */
        if (quadro >= d->posicao && quadro - d->posicao < (uint64_t)d->taxa * 8) {
            uint64_t alvo = d->u.mp3->currentPCMFrame + (quadro - d->posicao);
            ok = drmp3_seek_to_pcm_frame(d->u.mp3, alvo);
        } else if (d->total) {
            long inicio = (long)d->u.mp3->streamStartOffset, fim = d->fonte.tamanho;
            long byte = inicio + (long)((double)(fim - inicio) * quadro / d->total);
            ok = abrir_mp3(d, byte);
        }
        break;
    }
    case DAP_FMT_FLAC: ok = drflac_seek_to_pcm_frame(d->u.flac, quadro); break;
    case DAP_FMT_WAV: ok = drwav_seek_to_pcm_frame(d->u.wav, quadro); break;
    case DAP_FMT_OGG: ok = stb_vorbis_seek(d->u.ogg, (unsigned)quadro); break;
    case DAP_FMT_AIFF:
        ok = fseek(d->f, d->dados + (long)quadro * d->bytes_quadro, SEEK_SET) == 0;
        break;
    case DAP_FMT_DSF:
    case DAP_FMT_DFF:
        return buscar_dsd(d, quadro);
    }
    if (ok) d->posicao = quadro;
    return ok;
}

void dap_fechar_audio(dap_decodificador_t *d)
{
    if (!d) return;
    if (d->extra && d->estado) d->extra->fechar(d->estado);
    switch (d->formato) {
    case DAP_FMT_MP3:
        if (d->u.mp3) { drmp3_uninit(d->u.mp3); free(d->u.mp3); }
        if (d->fonte.f) fclose(d->fonte.f);
        break;
    case DAP_FMT_FLAC: if (d->u.flac) drflac_close(d->u.flac); break;
    case DAP_FMT_WAV: if (d->u.wav) { drwav_uninit(d->u.wav); free(d->u.wav); } break;
    case DAP_FMT_OGG: if (d->u.ogg) stb_vorbis_close(d->u.ogg); break;
    }
    if (d->f) fclose(d->f);
    free(d->dsd ? d->dsd->bloco_buf : NULL);
    free(d->dsd);
    free(d->bruto);
    free(d->temp);
    free(d);
}
