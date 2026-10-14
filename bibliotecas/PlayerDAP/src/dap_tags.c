/* dap_tags.c -- quem canta, de que album, de que ano: as etiquetas.
 *
 * ID3v2 (2.2, 2.3 e 2.4, nas quatro codificacoes de texto) e ID3v1 no MP3;
 * os comentarios Vorbis no FLAC e no Ogg; o RIFF INFO e o bloco "id3 " no
 * WAV; NAME/AUTH e o "ID3 " no AIFF; o ID3v2 do DSF e do DSDIFF. So os
 * quadros de texto sao lidos -- a capa (APIC) e pulada sem ler.
 *
 * O que nao veio nas etiquetas sai do nome do arquivo: "Artista - Titulo.mp3",
 * "07 - Titulo.flac". E a duracao vem do proprio arquivo: o cabecalho Xing
 * (ou a taxa de bits) no MP3, o STREAMINFO no FLAC, o tamanho dos dados no
 * WAV e no AIFF, a ultima pagina no Ogg.
 */
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <ctype.h>
#include "dap_interno.h"

/* --------------------------------------------------------------- texto */

/* Copia em UTF-8, cortando no tamanho (sem partir um caractere). */
static void por_utf8(char *destino, int tam, uint32_t c, int *n)
{
    char b[4];
    int k;
    if (c < 0x80) { b[0] = (char)c; k = 1; }
    else if (c < 0x800) { b[0] = (char)(0xC0 | (c >> 6)); b[1] = (char)(0x80 | (c & 0x3F)); k = 2; }
    else if (c < 0x10000) { b[0] = (char)(0xE0 | (c >> 12)); b[1] = (char)(0x80 | ((c >> 6) & 0x3F)); b[2] = (char)(0x80 | (c & 0x3F)); k = 3; }
    else { b[0] = (char)(0xF0 | (c >> 18)); b[1] = (char)(0x80 | ((c >> 12) & 0x3F)); b[2] = (char)(0x80 | ((c >> 6) & 0x3F)); b[3] = (char)(0x80 | (c & 0x3F)); k = 4; }
    if (*n + k >= tam) return;
    memcpy(destino + *n, b, k);
    *n += k;
}

/* Texto de etiqueta para UTF-8: 0 Latin-1, 1 UTF-16 com BOM, 2 UTF-16BE, 3 UTF-8. */
static void texto_id3(char *destino, int tam, const uint8_t *p, int n, int codificacao)
{
    int k = 0;
    if (codificacao == 1 || codificacao == 2) {
        int grande = codificacao == 2;
        if (n >= 2 && p[0] == 0xFF && p[1] == 0xFE) { grande = 0; p += 2; n -= 2; }
        else if (n >= 2 && p[0] == 0xFE && p[1] == 0xFF) { grande = 1; p += 2; n -= 2; }
        for (int i = 0; i + 1 < n; i += 2) {
            uint32_t c = grande ? (uint32_t)(p[i] << 8 | p[i + 1]) : (uint32_t)(p[i + 1] << 8 | p[i]);
            if (c == 0) break;
            if (c >= 0xD800 && c < 0xDC00 && i + 3 < n) {
                uint32_t c2 = grande ? (uint32_t)(p[i + 2] << 8 | p[i + 3]) : (uint32_t)(p[i + 3] << 8 | p[i + 2]);
                c = 0x10000 + ((c - 0xD800) << 10) + (c2 - 0xDC00);
                i += 2;
            }
            por_utf8(destino, tam, c, &k);
        }
    } else if (codificacao == 3) {
        for (int i = 0; i < n && p[i]; i++)
            if (k + 1 < tam) destino[k++] = (char)p[i];
    } else {
        for (int i = 0; i < n && p[i]; i++) por_utf8(destino, tam, p[i], &k);
    }
    destino[k] = 0;
    /* sem espacos sobrando nas pontas */
    while (k > 0 && (destino[k - 1] == ' ' || destino[k - 1] == '\r' || destino[k - 1] == '\n')) destino[--k] = 0;
    int ini = 0;
    while (destino[ini] == ' ') ini++;
    if (ini) memmove(destino, destino + ini, strlen(destino + ini) + 1);
}

/* So preenche o que ainda esta vazio: a primeira etiqueta que diz, vale. */
static void guardar(char *campo, int tam, const char *valor)
{
    if (!campo[0] && valor && valor[0]) {
        strncpy(campo, valor, tam - 1);
        campo[tam - 1] = 0;
    }
}

static const char *const generos_id3v1[] = {
    "Blues", "Classic Rock", "Country", "Dance", "Disco", "Funk", "Grunge", "Hip-Hop", "Jazz", "Metal", "New Age",
    "Oldies", "Other", "Pop", "R&B", "Rap", "Reggae", "Rock", "Techno", "Industrial", "Alternative", "Ska",
    "Death Metal", "Pranks", "Soundtrack", "Euro-Techno", "Ambient", "Trip-Hop", "Vocal", "Jazz+Funk", "Fusion",
    "Trance", "Classical", "Instrumental", "Acid", "House", "Game", "Sound Clip", "Gospel", "Noise", "Alternative Rock",
    "Bass", "Soul", "Punk", "Space", "Meditative", "Instrumental Pop", "Instrumental Rock", "Ethnic", "Gothic",
    "Darkwave", "Techno-Industrial", "Electronic", "Pop-Folk", "Eurodance", "Dream", "Southern Rock", "Comedy",
    "Cult", "Gangsta", "Top 40", "Christian Rap", "Pop/Funk", "Jungle", "Native American", "Cabaret", "New Wave",
    "Psychedelic", "Rave", "Showtunes", "Trailer", "Lo-Fi", "Tribal", "Acid Punk", "Acid Jazz", "Polka", "Retro",
    "Musical", "Rock & Roll", "Hard Rock", "Folk", "Folk-Rock", "National Folk", "Swing", "Fast Fusion", "Bebop",
    "Latin", "Revival", "Celtic", "Bluegrass", "Avantgarde", "Gothic Rock", "Progressive Rock", "Psychedelic Rock",
    "Symphonic Rock", "Slow Rock", "Big Band", "Chorus", "Easy Listening", "Acoustic", "Humour", "Speech", "Chanson",
    "Opera", "Chamber Music", "Sonata", "Symphony", "Booty Bass", "Primus", "Porn Groove", "Satire", "Slow Jam",
    "Club", "Tango", "Samba", "Folklore", "Ballad", "Power Ballad", "Rhythmic Soul", "Freestyle", "Duet", "Punk Rock",
    "Drum Solo", "A Cappella", "Euro-House", "Dance Hall", "Goa", "Drum & Bass", "Club-House", "Hardcore Techno",
    "Terror", "Indie", "BritPop", "Negerpunk", "Polsk Punk", "Beat", "Christian Gangsta Rap", "Heavy Metal",
    "Black Metal", "Crossover", "Contemporary Christian", "Christian Rock", "Merengue", "Salsa", "Thrash Metal",
    "Anime", "Jpop", "Synthpop",
};

/* "(17)", "(17)Rock", "17" e "Rock": o nome do genero. */
static void genero(char *destino, int tam)
{
    const char *p = destino;
    int numero = -1;
    if (p[0] == '(' && isdigit((unsigned char)p[1])) {
        numero = atoi(p + 1);
        const char *fim = strchr(p, ')');
        if (fim && fim[1]) { memmove(destino, fim + 1, strlen(fim + 1) + 1); return; }
    } else if (isdigit((unsigned char)p[0])) {
        int so_digitos = 1;
        for (const char *q = p; *q; q++) if (!isdigit((unsigned char)*q)) so_digitos = 0;
        if (so_digitos) numero = atoi(p);
    }
    if (numero >= 0) {
        int quantos = (int)(sizeof(generos_id3v1) / sizeof(generos_id3v1[0]));
        if (numero < quantos) snprintf(destino, tam, "%s", generos_id3v1[numero]);
        else destino[0] = 0;
    }
}

/* "3/12" -> 3 */
static int numero_de(const char *s)
{
    while (*s && !isdigit((unsigned char)*s)) s++;
    return atoi(s);
}

static void campo(dap_etiquetas_t *e, const char *nome, const char *valor)
{
    if (!valor || !valor[0]) return;
    if (!strcasecmp(nome, "TITLE") || !strcmp(nome, "TIT2") || !strcmp(nome, "TT2") || !strcmp(nome, "INAM") || !strcmp(nome, "NAME"))
        guardar(e->titulo, sizeof(e->titulo), valor);
    else if (!strcasecmp(nome, "ARTIST") || !strcmp(nome, "TPE1") || !strcmp(nome, "TP1") || !strcmp(nome, "IART") || !strcmp(nome, "AUTH"))
        guardar(e->artista, sizeof(e->artista), valor);
    else if (!strcasecmp(nome, "ALBUM") || !strcmp(nome, "TALB") || !strcmp(nome, "TAL") || !strcmp(nome, "IPRD"))
        guardar(e->album, sizeof(e->album), valor);
    else if (!strcasecmp(nome, "ALBUMARTIST") || !strcasecmp(nome, "ALBUM ARTIST") || !strcasecmp(nome, "ALBUM_ARTIST")
             || !strcmp(nome, "TPE2") || !strcmp(nome, "TP2"))
        guardar(e->artista_album, sizeof(e->artista_album), valor);
    else if (!strcasecmp(nome, "GENRE") || !strcmp(nome, "TCON") || !strcmp(nome, "TCO") || !strcmp(nome, "IGNR")) {
        if (!e->genero[0]) {
            guardar(e->genero, sizeof(e->genero), valor);
            genero(e->genero, sizeof(e->genero));
        }
    } else if (!strcasecmp(nome, "COMPOSER") || !strcmp(nome, "TCOM") || !strcmp(nome, "TCM"))
        guardar(e->compositor, sizeof(e->compositor), valor);
    else if (!strcasecmp(nome, "DATE") || !strcasecmp(nome, "YEAR") || !strcmp(nome, "TYER") || !strcmp(nome, "TYE")
             || !strcmp(nome, "TDRC") || !strcmp(nome, "TDOR") || !strcmp(nome, "ICRD")) {
        if (!e->ano) {
            int a = numero_de(valor);
            if (a >= 1000 && a <= 2999) e->ano = a;
        }
    } else if (!strcasecmp(nome, "TRACKNUMBER") || !strcmp(nome, "TRCK") || !strcmp(nome, "TRK") || !strcmp(nome, "ITRK")
               || !strcmp(nome, "IPRT")) {
        if (!e->faixa) e->faixa = numero_de(valor);
    } else if (!strcasecmp(nome, "DISCNUMBER") || !strcmp(nome, "TPOS") || !strcmp(nome, "TPA")) {
        if (!e->disco) e->disco = numero_de(valor);
    } else if (!strcmp(nome, "TLEN") || !strcmp(nome, "TLE")) {
        if (!e->duracao_ms) e->duracao_ms = atoi(valor);
    }
}

/* ---------------------------------------------------------------- ID3v2 */

static uint32_t sincrono(const uint8_t *p) { return (uint32_t)(p[0] & 0x7F) << 21 | (p[1] & 0x7F) << 14 | (p[2] & 0x7F) << 7 | (p[3] & 0x7F); }
static uint32_t be32t(const uint8_t *p) { return (uint32_t)p[0] << 24 | (uint32_t)p[1] << 16 | (uint32_t)p[2] << 8 | p[3]; }
static uint32_t le32t(const uint8_t *p) { return (uint32_t)p[3] << 24 | (uint32_t)p[2] << 16 | (uint32_t)p[1] << 8 | p[0]; }

/* Tira a "dessincronizacao" (FF 00 -> FF). Devolve o tamanho novo. */
static int dessincronizar(uint8_t *p, int n)
{
    int k = 0;
    for (int i = 0; i < n; i++) {
        p[k++] = p[i];
        if (p[i] == 0xFF && i + 1 < n && p[i + 1] == 0x00) i++;
    }
    return k;
}

/* Le uma etiqueta ID3v2 que comeca em "inicio". Devolve o tamanho dela (0: nao tem). */
static long ler_id3v2(FILE *f, long inicio, dap_etiquetas_t *e)
{
    uint8_t h[10];
    if (fseek(f, inicio, SEEK_SET) != 0 || fread(h, 1, 10, f) != 10 || memcmp(h, "ID3", 3) != 0) return 0;
    int versao = h[3], bandeiras = h[5];
    long tamanho = (long)sincrono(h + 6);
    long total = 10 + tamanho + ((bandeiras & 0x10) ? 10 : 0);
    if (versao < 2 || versao > 4) return total;
    int global = (bandeiras & 0x80) && versao < 4;   /* a 2.4 marca quadro a quadro */
    long pos = 10, fim = 10 + tamanho;

    /* o cabecalho estendido */
    if ((bandeiras & 0x40) && versao >= 3) {
        uint8_t x[4];
        if (fread(x, 1, 4, f) != 4) return total;
        pos += versao == 4 ? (long)sincrono(x) : (long)be32t(x) + 4;
    }

    int cab = versao == 2 ? 6 : 10;
    while (pos + cab <= fim) {
        uint8_t q[10];
        fseek(f, inicio + pos, SEEK_SET);
        if ((int)fread(q, 1, cab, f) != cab || q[0] == 0) break;
        char id[5] = { 0 };
        long t;
        int dessinc = global, quadro_flags = 0;
        if (versao == 2) {
            memcpy(id, q, 3);
            t = (long)(q[3] << 16 | q[4] << 8 | q[5]);
        } else {
            memcpy(id, q, 4);
            t = versao == 4 ? (long)sincrono(q + 4) : (long)be32t(q + 4);
            quadro_flags = q[9];
            if (versao == 4 && (quadro_flags & 0x02)) dessinc = 1;
            /* na 2.3 alguns gravam o tamanho sincrono: se nao cabe, tenta assim */
            if (versao == 3 && pos + cab + t > fim) t = (long)sincrono(q + 4);
        }
        if (t <= 0 || pos + cab + t > fim) break;
        pos += cab + t;

        /* So os quadros de texto (T...), e nao os grandes. */
        if (id[0] != 'T' || t > 1024 || (versao >= 3 && (quadro_flags & 0x0C))) continue;
        uint8_t dados[1024];
        if ((long)fread(dados, 1, t, f) != t) break;
        int n = (int)t;
        if (dessinc) n = dessincronizar(dados, n);
        if (versao == 4 && (quadro_flags & 0x01) && n > 4) { memmove(dados, dados + 4, n - 4); n -= 4; }   /* tamanho original */
        if (n < 2) continue;
        char valor[192];
        if (!strcmp(id, "TXXX") || !strcmp(id, "TXX")) {
            /* TXXX: descricao \0 valor -- "ALBUM ARTIST", "COMPOSER" de alguns programas */
            char desc[48];
            int cod = dados[0], i = 1, passo = (cod == 1 || cod == 2) ? 2 : 1;
            while (i + passo <= n && (dados[i] || (passo == 2 && dados[i + 1]))) i += passo;
            texto_id3(desc, sizeof(desc), dados + 1, i - 1, cod);
            i += passo;
            if (i < n) {
                texto_id3(valor, sizeof(valor), dados + i, n - i, cod);
                campo(e, desc, valor);
            }
            continue;
        }
        texto_id3(valor, sizeof(valor), dados + 1, n - 1, dados[0]);
        campo(e, id, valor);
    }
    return total;
}

static void ler_id3v1(FILE *f, dap_etiquetas_t *e)
{
    uint8_t t[128];
    if (fseek(f, -128, SEEK_END) != 0 || fread(t, 1, 128, f) != 128 || memcmp(t, "TAG", 3) != 0) return;
    char v[64];
    texto_id3(v, sizeof(v), t + 3, 30, 0); campo(e, "TIT2", v);
    texto_id3(v, sizeof(v), t + 33, 30, 0); campo(e, "TPE1", v);
    texto_id3(v, sizeof(v), t + 63, 30, 0); campo(e, "TALB", v);
    texto_id3(v, sizeof(v), t + 93, 4, 0); campo(e, "TYER", v);
    if (t[125] == 0 && t[126] && !e->faixa) e->faixa = t[126];
    if (t[127] < 255 && !e->genero[0]) {
        snprintf(v, sizeof(v), "%d", t[127]);
        campo(e, "TCON", v);
    }
}

/* ------------------------------------------------------------- APEv2 */

/* APEv2 (WavPack, Monkey's Audio, Musepack, alguns MP3): no fim do arquivo,
 * antes do ID3v1 quando ha os dois. As chaves sao as do Vorbis, quase. */
static void ler_ape(FILE *f, dap_etiquetas_t *e)
{
    uint8_t r[32];
    for (int com_id3v1 = 0; com_id3v1 < 2; com_id3v1++) {
        long rodape = 32 + (com_id3v1 ? 128 : 0);
        if (fseek(f, -rodape, SEEK_END) != 0 || fread(r, 1, 32, f) != 32 || memcmp(r, "APETAGEX", 8) != 0) continue;
        uint32_t tam = le32t(r + 12), itens = le32t(r + 16);
        if (tam <= 32 || tam > (1u << 20)) return;
        uint32_t t = tam - 32;   /* os itens; o tamanho conta o rodape, nao o cabecalho */
        uint8_t *b = (uint8_t *)malloc(t + 1);
        if (!b) return;
        if (fseek(f, -rodape - (long)t, SEEK_END) == 0 && fread(b, 1, t, f) == t) {
            uint32_t p = 0;
            for (uint32_t i = 0; i < itens && p + 8 < t; i++) {
                uint32_t lv = le32t(b + p), bandeiras = le32t(b + p + 4);
                uint32_t k = p + 8;
                while (k < t && b[k]) k++;
                if (k >= t || lv > t - k - 1) break;
                const char *chave = (const char *)b + p + 8;
                uint8_t *valor = b + k + 1;
                if ((bandeiras & 6) == 0) {   /* texto em UTF-8 */
                    char v[192];
                    uint32_t n = lv < sizeof(v) - 1 ? lv : (uint32_t)sizeof(v) - 1;
                    memcpy(v, valor, n);
                    v[n] = 0;
                    if (!strcasecmp(chave, "Track")) campo(e, "TRACKNUMBER", v);
                    else if (!strcasecmp(chave, "Disc")) campo(e, "DISCNUMBER", v);
                    else campo(e, chave, v);
                }
                p = k + 1 + lv;
            }
        }
        free(b);
        return;
    }
}

/* WavPack: a duracao pelo cabecalho do primeiro bloco (as amostras e a taxa). */
static void ler_wv(FILE *f, dap_etiquetas_t *e)
{
    static const int taxas[15] = { 6000, 8000, 9600, 11025, 12000, 16000, 22050, 24000,
                                   32000, 44100, 48000, 64000, 88200, 96000, 192000 };
    uint8_t h[32];
    if (fseek(f, 0, SEEK_SET) == 0 && fread(h, 1, 32, f) == 32 && memcmp(h, "wvpk", 4) == 0) {
        uint32_t amostras = le32t(h + 12), bandeiras = le32t(h + 24);
        int indice = (int)((bandeiras >> 23) & 0xF);
        int dsd = (bandeiras & 0x80000000u) != 0;
        if (amostras != 0xFFFFFFFFu && indice < 15 && !dsd) {
            uint64_t total = (uint64_t)amostras | (uint64_t)h[11] << 32;
            e->duracao_ms = (int)(total * 1000 / (uint64_t)taxas[indice]);
        }
    }
    ler_ape(f, e);
    if (!e->titulo[0]) ler_id3v1(f, e);
}

/* ------------------------------------------------------------- ASF */

/* WMA: o Content Description (titulo e autor) e o Extended Content
 * Description (WM/AlbumTitle, WM/Genre, WM/Year...), em UTF-16LE; a duracao
 * no File Properties (em 100 ns, contando o preroll, que vem em ms). Os
 * objetos sao lidos um a um: a capa (WM/Picture) pode ser grande e fica. */
static void ler_asf(FILE *f, dap_etiquetas_t *e)
{
    static const uint8_t cabecalho[16] = { 0x30, 0x26, 0xB2, 0x75, 0x8E, 0x66, 0xCF, 0x11, 0xA6, 0xD9, 0x00, 0xAA, 0x00, 0x62, 0xCE, 0x6C };
    static const uint8_t arquivo[16] = { 0xA1, 0xDC, 0xAB, 0x8C, 0x47, 0xA9, 0xCF, 0x11, 0x8E, 0xE4, 0x00, 0xC0, 0x0C, 0x20, 0x53, 0x65 };
    static const uint8_t descricao[16] = { 0x33, 0x26, 0xB2, 0x75, 0x8E, 0x66, 0xCF, 0x11, 0xA6, 0xD9, 0x00, 0xAA, 0x00, 0x62, 0xCE, 0x6C };
    static const uint8_t estendida[16] = { 0x40, 0xA4, 0xD0, 0xD2, 0x07, 0xE3, 0xD2, 0x11, 0x97, 0xF0, 0x00, 0xA0, 0xC9, 0x5E, 0xA8, 0x50 };
    uint8_t h[30];
    if (fseek(f, 0, SEEK_SET) != 0 || fread(h, 1, 30, f) != 30 || memcmp(h, cabecalho, 16) != 0) return;
    uint64_t fim = (uint64_t)le32t(h + 16) | (uint64_t)le32t(h + 20) << 32, p = 30;
    while (p + 24 <= fim) {
        uint8_t o[24];
        if (fseek(f, (long)p, SEEK_SET) != 0 || fread(o, 1, 24, f) != 24) return;
        uint64_t t = (uint64_t)le32t(o + 16) | (uint64_t)le32t(o + 20) << 32;
        if (t < 24 || t > fim - p) return;
        uint32_t tc = (uint32_t)(t - 24);
        int interessa = !memcmp(o, arquivo, 16) || !memcmp(o, descricao, 16) || !memcmp(o, estendida, 16);
        uint8_t *b = interessa && tc >= 2 && tc < (1u << 20) ? (uint8_t *)malloc(tc) : NULL;
        if (b && fread(b, 1, tc, f) == tc) {
            if (!memcmp(o, arquivo, 16) && tc >= 80) {
                uint64_t d = (uint64_t)le32t(b + 40) | (uint64_t)le32t(b + 44) << 32;
                uint64_t preroll = (uint64_t)le32t(b + 56) | (uint64_t)le32t(b + 60) << 32;
                uint64_t ms = d / 10000;
                if (!e->duracao_ms && ms > preroll) e->duracao_ms = (int)(ms - preroll);
            } else if (!memcmp(o, descricao, 16) && tc >= 10) {
                static const char *const nomes[2] = { "TITLE", "ARTIST" };
                uint32_t q = 10;
                for (int i = 0; i < 2; i++) {
                    uint32_t n = b[2 * i] | b[2 * i + 1] << 8;
                    if (q + n > tc) break;
                    char v[192];
                    texto_id3(v, sizeof(v), b + q, (int)n, 1);
                    campo(e, nomes[i], v);
                    q += n;
                }
            } else if (!memcmp(o, estendida, 16)) {
                uint32_t itens = b[0] | b[1] << 8, q = 2;
                for (uint32_t i = 0; i < itens && q + 2 <= tc; i++) {
                    uint32_t n = b[q] | b[q + 1] << 8;
                    if (q + 2 + n + 4 > tc) break;
                    char nome[64], v[192];
                    texto_id3(nome, sizeof(nome), b + q + 2, (int)n, 1);
                    q += 2 + n;
                    uint32_t tipo = b[q] | b[q + 1] << 8, tv = b[q + 2] | b[q + 3] << 8;
                    q += 4;
                    if (q + tv > tc) break;
                    const uint8_t *x = b + q;
                    v[0] = 0;
                    if (tipo == 0) texto_id3(v, sizeof(v), x, (int)tv, 1);
                    else if (tipo == 3 && tv >= 4) snprintf(v, sizeof(v), "%lu", (unsigned long)le32t(x));
                    else if (tipo == 5 && tv >= 2) snprintf(v, sizeof(v), "%u", (unsigned)(x[0] | x[1] << 8));
                    q += tv;
                    if (!strcmp(nome, "WM/AlbumTitle")) campo(e, "ALBUM", v);
                    else if (!strcmp(nome, "WM/AlbumArtist")) campo(e, "ALBUMARTIST", v);
                    else if (!strcmp(nome, "WM/Genre")) campo(e, "GENRE", v);
                    else if (!strcmp(nome, "WM/Year")) campo(e, "YEAR", v);
                    else if (!strcmp(nome, "WM/Composer")) campo(e, "COMPOSER", v);
                    else if (!strcmp(nome, "WM/PartOfSet")) campo(e, "DISCNUMBER", v);
                    else if (!strcmp(nome, "WM/TrackNumber")) campo(e, "TRACKNUMBER", v);
                    else if (!strcmp(nome, "WM/Track") && v[0]) {
                        /* o antigo, que conta do zero */
                        char um[16];
                        snprintf(um, sizeof(um), "%d", atoi(v) + 1);
                        campo(e, "TRACKNUMBER", um);
                    }
                }
            }
        }
        free(b);
        p += t;
    }
}

/* Monkey's Audio: a duracao pelo cabecalho (o da 3.98 em diante vem depois
 * do descritor; o antigo, logo depois do "MAC " e da versao). */
static void ler_mac(FILE *f, long inicio, dap_etiquetas_t *e)
{
    uint8_t h[80];
    memset(h, 0, sizeof(h));
    if (fseek(f, inicio, SEEK_SET) != 0 || fread(h, 1, 8, f) != 8 || memcmp(h, "MAC ", 4) != 0) return;
    int versao = h[4] | h[5] << 8;
    uint32_t por_quadro, ultimo, quadros, taxa;
    if (versao >= 3980) {
        /* o tamanho do descritor (logo depois do "MAC ", da versao e de 2 bytes
         * vazios); o cabecalho comeca no fim dele */
        if (fread(h, 1, 4, f) != 4) return;
        uint32_t descritor = le32t(h);
        if (descritor < 52 || descritor > 1024 || fseek(f, inicio + (long)descritor, SEEK_SET) != 0 || fread(h, 1, 24, f) != 24) return;
        por_quadro = le32t(h + 4);
        ultimo = le32t(h + 8);
        quadros = le32t(h + 12);
        taxa = le32t(h + 20);
    } else {
        /* compressao, bandeiras, canais, taxa, os dois tamanhos, quadros e o ultimo */
        if (fseek(f, inicio + 6, SEEK_SET) != 0 || fread(h, 1, 26, f) != 26) return;
        int compressao = h[0] | h[1] << 8;
        taxa = le32t(h + 6);
        quadros = le32t(h + 18);
        ultimo = le32t(h + 22);
        por_quadro = versao >= 3950 ? 73728 * 4 : (versao >= 3900 || (versao >= 3800 && compressao == 4000)) ? 73728 : 9216;
    }
    if (taxa && quadros && !e->duracao_ms)
        e->duracao_ms = (int)(((uint64_t)(quadros - 1) * por_quadro + ultimo) * 1000 / taxa);
}

/* AAC cru (ADTS): a duracao pelo tamanho medio dos primeiros quadros (1024
 * amostras cada) e o tamanho do arquivo. */
static void duracao_adts(FILE *f, long inicio, dap_etiquetas_t *e)
{
    static const int taxas[13] = { 96000, 88200, 64000, 48000, 44100, 32000, 24000, 22050, 16000, 12000, 11025, 8000, 7350 };
    if (e->duracao_ms || fseek(f, 0, SEEK_END) != 0) return;
    long tamanho = ftell(f), p = inicio, soma = 0;
    int quadros = 0, taxa = 0;
    uint8_t h[7];
    while (quadros < 64 && fseek(f, p, SEEK_SET) == 0 && fread(h, 1, 7, f) == 7) {
        if (h[0] != 0xFF || (h[1] & 0xF6) != 0xF0) break;
        int indice = (h[2] >> 2) & 0x0F, t = ((h[3] & 3) << 11) | (h[4] << 3) | (h[5] >> 5);
        if (indice >= 13 || t < 7) break;
        taxa = taxas[indice];
        soma += t;
        p += t;
        quadros++;
    }
    if (quadros && taxa) e->duracao_ms = (int)((double)(tamanho - inicio) / ((double)soma / quadros) * 1024.0 * 1000.0 / taxa);
}

/* ------------------------------------------------------------- M4A */

/* M4A: as etiquetas do iTunes em moov/udta/meta/ilst (cada item com uma
 * caixa 'data' dentro), e a duracao no mvhd. */
static void ler_m4a(FILE *f, dap_etiquetas_t *e)
{
    if (fseek(f, 0, SEEK_END) != 0) return;
    long fim = ftell(f), moov, tmoov, c, t;
    if (!dap_m4a_caixa(f, 0, fim, "moov", &moov, &tmoov)) return;
    if (dap_m4a_caixa(f, moov, moov + tmoov, "mvhd", &c, &t) && t >= 20) {
        uint8_t b[32];
        memset(b, 0, sizeof(b));
        if (fseek(f, c, SEEK_SET) == 0 && fread(b, 1, t < 32 ? (size_t)t : 32, f) >= 20) {
            uint32_t escala = b[0] == 1 ? be32t(b + 20) : be32t(b + 12);
            uint64_t d = b[0] == 1 ? ((uint64_t)be32t(b + 24) << 32 | be32t(b + 28)) : be32t(b + 16);
            if (escala) e->duracao_ms = (int)(d * 1000 / escala);
            /* no fragmentado, o mvhd conta so o que esta no moov (ou vem
             * zerado); a duracao inteira e a do mvex/mehd */
            long x, tx, h, th;
            uint8_t m[12];
            if (escala && dap_m4a_caixa(f, moov, moov + tmoov, "mvex", &x, &tx) && dap_m4a_caixa(f, x, x + tx, "mehd", &h, &th)
                && th >= 8 && fseek(f, h, SEEK_SET) == 0 && fread(m, 1, th >= 12 ? 12 : 8, f) >= 8) {
                uint64_t df = m[0] == 1 && th >= 12 ? ((uint64_t)be32t(m + 4) << 32 | be32t(m + 8)) : be32t(m + 4);
                if (df > d) e->duracao_ms = (int)(df * 1000 / escala);
            }
        }
    }
    long meta, tmeta, ilst, tilst;
    int achou = 0;
    if (dap_m4a_caixa(f, moov, moov + tmoov, "udta", &c, &t)) achou = dap_m4a_caixa(f, c, c + t, "meta", &meta, &tmeta);
    if (!achou) achou = dap_m4a_caixa(f, moov, moov + tmoov, "meta", &meta, &tmeta);
    if (!achou || tmeta < 12) return;
    /* o 'meta' do iTunes tem 4 bytes de versao antes dos filhos; o do QuickTime, nao */
    uint8_t m[12];
    if (fseek(f, meta, SEEK_SET) != 0 || fread(m, 1, 12, f) != 12) return;
    if (memcmp(m + 4, "hdlr", 4) != 0) { meta += 4; tmeta -= 4; }
    if (!dap_m4a_caixa(f, meta, meta + tmeta, "ilst", &ilst, &tilst)) return;

    long p = ilst;
    while (p + 8 <= ilst + tilst) {
        uint8_t h[8];
        if (fseek(f, p, SEEK_SET) != 0 || fread(h, 1, 8, f) != 8) return;
        long ti = (long)be32t(h);
        if (ti < 8 || p + ti > ilst + tilst) return;
        long dado, tdado;
        if (dap_m4a_caixa(f, p + 8, p + ti, "data", &dado, &tdado) && tdado > 8) {
            uint8_t v[200];
            long n = tdado - 8 < (long)sizeof(v) - 1 ? tdado - 8 : (long)sizeof(v) - 1;
            if (fseek(f, dado + 8, SEEK_SET) == 0 && fread(v, 1, (size_t)n, f) == (size_t)n) {
                v[n] = 0;
                const char *s = (const char *)v;
                if (!memcmp(h + 4, "\xA9nam", 4)) campo(e, "TITLE", s);
                else if (!memcmp(h + 4, "\xA9" "ART", 4)) campo(e, "ARTIST", s);
                else if (!memcmp(h + 4, "\xA9" "alb", 4)) campo(e, "ALBUM", s);
                else if (!memcmp(h + 4, "aART", 4)) campo(e, "ALBUMARTIST", s);
                else if (!memcmp(h + 4, "\xA9gen", 4)) campo(e, "GENRE", s);
                else if (!memcmp(h + 4, "\xA9" "day", 4)) campo(e, "DATE", s);
                else if (!memcmp(h + 4, "\xA9wrt", 4)) campo(e, "COMPOSER", s);
                else if (!memcmp(h + 4, "trkn", 4) && n >= 4 && !e->faixa) e->faixa = v[2] << 8 | v[3];
                else if (!memcmp(h + 4, "disk", 4) && n >= 4 && !e->disco) e->disco = v[2] << 8 | v[3];
                else if (!memcmp(h + 4, "gnre", 4) && n >= 2 && !e->genero[0]) {
                    /* o numero do genero do ID3v1, mais um */
                    char g[8];
                    snprintf(g, sizeof(g), "%d", (v[0] << 8 | v[1]) - 1);
                    campo(e, "TCON", g);
                }
            }
        }
        p += ti;
    }
}

/* ------------------------------------------------------- comentarios Vorbis */

static void ler_vorbis(const uint8_t *p, int n, dap_etiquetas_t *e)
{
    if (n < 8) return;
    uint32_t t = le32t(p);
    int i = 4 + (int)t;
    if (i + 4 > n) return;
    uint32_t quantos = le32t(p + i);
    i += 4;
    for (uint32_t k = 0; k < quantos && i + 4 <= n; k++) {
        uint32_t l = le32t(p + i);
        i += 4;
        if ((long)i + (long)l > n) break;
        const char *c = (const char *)p + i;
        const char *igual = memchr(c, '=', l);
        if (igual && igual - c < 32) {
            char nome[32], valor[192];
            int ln = (int)(igual - c);
            memcpy(nome, c, ln);
            nome[ln] = 0;
            int lv = (int)l - ln - 1;
            if (lv > (int)sizeof(valor) - 1) lv = sizeof(valor) - 1;
            memcpy(valor, igual + 1, lv);
            valor[lv] = 0;
            campo(e, nome, valor);
        }
        i += (int)l;
    }
}

/* FLAC: os blocos de metadados; STREAMINFO da a duracao. */
static void ler_flac(FILE *f, long inicio, dap_etiquetas_t *e)
{
    uint8_t h[4];
    fseek(f, inicio + 4, SEEK_SET);
    for (int ultimo = 0; !ultimo;) {
        if (fread(h, 1, 4, f) != 4) return;
        ultimo = h[0] & 0x80;
        int tipo = h[0] & 0x7F;
        long t = (long)(h[1] << 16 | h[2] << 8 | h[3]);
        long depois = ftell(f) + t;
        if (tipo == 0 && t >= 18) {
            uint8_t s[18];
            if (fread(s, 1, 18, f) != 18) return;
            uint32_t taxa = (uint32_t)(s[10] << 12 | s[11] << 4 | s[12] >> 4);
            uint64_t amostras = (uint64_t)(s[13] & 0x0F) << 32 | be32t(s + 14);
            if (taxa && amostras && !e->duracao_ms) e->duracao_ms = (int)(amostras * 1000 / taxa);
        } else if (tipo == 4 && t < 65536) {
            uint8_t *c = (uint8_t *)malloc(t);
            if (c && (long)fread(c, 1, t, f) == t) ler_vorbis(c, (int)t, e);
            free(c);
        }
        fseek(f, depois, SEEK_SET);
    }
}

/* Ogg: o segundo pacote (comentarios) e a posicao da ultima pagina (duracao). */
static void ler_ogg(FILE *f, dap_etiquetas_t *e)
{
    /* junta os primeiros 64 KB de corpo de pagina: cabecalhos cabem ai */
    uint8_t *corpo = (uint8_t *)malloc(65536);
    if (!corpo) return;
    int tem = 0, taxa = 0;
    fseek(f, 0, SEEK_SET);
    for (int paginas = 0; paginas < 16 && tem < 60000; paginas++) {
        uint8_t h[27], seg[255];
        if (fread(h, 1, 27, f) != 27 || memcmp(h, "OggS", 4) != 0) break;
        int ns = h[26];
        if ((int)fread(seg, 1, ns, f) != ns) break;
        int t = 0;
        for (int i = 0; i < ns; i++) t += seg[i];
        if (tem + t > 65536) break;
        if ((int)fread(corpo + tem, 1, t, f) != t) break;
        tem += t;
    }
    for (int i = 0; i + 16 < tem; i++) {
        if (corpo[i] == 1 && memcmp(corpo + i + 1, "vorbis", 6) == 0 && !taxa) taxa = (int)le32t(corpo + i + 12);
        if (memcmp(corpo + i, "OpusHead", 8) == 0 && !taxa) taxa = 48000;
        if (corpo[i] == 3 && memcmp(corpo + i + 1, "vorbis", 6) == 0) { ler_vorbis(corpo + i + 7, tem - i - 7, e); break; }
        if (memcmp(corpo + i, "OpusTags", 8) == 0) { ler_vorbis(corpo + i + 8, tem - i - 8, e); break; }
    }
    free(corpo);

    /* a duracao: o granulo da ultima pagina */
    if (taxa && !e->duracao_ms) {
        uint8_t b[8192];
        fseek(f, 0, SEEK_END);
        long fim = ftell(f), de = fim > (long)sizeof(b) ? fim - (long)sizeof(b) : 0;
        fseek(f, de, SEEK_SET);
        int n = (int)fread(b, 1, sizeof(b), f);
        for (int i = n - 27; i >= 0; i--)
            if (memcmp(b + i, "OggS", 4) == 0) {
                uint64_t g = (uint64_t)le32t(b + i + 10) << 32 | le32t(b + i + 6);
                if (g && g != 0xFFFFFFFFFFFFFFFFull) e->duracao_ms = (int)(g * 1000 / (uint64_t)taxa);
                break;
            }
    }
}

/* WAV: RIFF INFO e o bloco "id3 "; a duracao pelo tamanho dos dados. */
static void ler_wav(FILE *f, dap_etiquetas_t *e)
{
    uint8_t h[12];
    fseek(f, 0, SEEK_SET);
    if (fread(h, 1, 12, f) != 12 || memcmp(h, "RIFF", 4) != 0) return;
    uint32_t bytes_por_segundo = 0;
    for (int blocos = 0; blocos < 64; blocos++) {
        uint8_t c[8];
        if (fread(c, 1, 8, f) != 8) break;
        uint32_t t = le32t(c + 4);
        long depois = ftell(f) + (long)t + (long)(t & 1);
        if (memcmp(c, "fmt ", 4) == 0 && t >= 16) {
            uint8_t fm[16];
            if (fread(fm, 1, 16, f) != 16) break;
            bytes_por_segundo = le32t(fm + 8);
        } else if (memcmp(c, "data", 4) == 0) {
            if (bytes_por_segundo && !e->duracao_ms) e->duracao_ms = (int)((uint64_t)t * 1000 / bytes_por_segundo);
        } else if (memcmp(c, "LIST", 4) == 0 && t >= 4 && t < 65536) {
            uint8_t *l = (uint8_t *)malloc(t);
            if (l && fread(l, 1, t, f) == t && memcmp(l, "INFO", 4) == 0) {
                for (uint32_t i = 4; i + 8 <= t;) {
                    char id[5] = { 0 };
                    memcpy(id, l + i, 4);
                    uint32_t lt = le32t(l + i + 4);
                    if (i + 8 + lt > t) break;
                    char v[192];
                    texto_id3(v, sizeof(v), l + i + 8, (int)lt, 0);
                    /* o INFO e quase sempre Latin-1, mas ha quem grave UTF-8 */
                    int utf8 = 1;
                    for (uint32_t k = 0; k < lt; k++) if (l[i + 8 + k] >= 0x80) { utf8 = 0; break; }
                    if (!utf8) {
                        /* confere se e UTF-8 valido antes de tratar como Latin-1 */
                        int valido = 1;
                        for (uint32_t k = 0; k < lt && valido;) {
                            uint8_t b = l[i + 8 + k];
                            int n = b < 0x80 ? 1 : (b & 0xE0) == 0xC0 ? 2 : (b & 0xF0) == 0xE0 ? 3 : (b & 0xF8) == 0xF0 ? 4 : 0;
                            if (!n) { valido = b == 0; break; }
                            for (int m = 1; m < n; m++) if (k + m >= lt || (l[i + 8 + k + m] & 0xC0) != 0x80) valido = 0;
                            k += n;
                        }
                        if (valido) texto_id3(v, sizeof(v), l + i + 8, (int)lt, 3);
                    }
                    campo(e, id, v);
                    i += 8 + lt + (lt & 1);
                }
            }
            free(l);
        } else if (memcmp(c, "id3 ", 4) == 0 || memcmp(c, "ID3 ", 4) == 0) {
            ler_id3v2(f, ftell(f), e);
        }
        fseek(f, depois, SEEK_SET);
    }
}

/* AIFF: NAME, AUTH, "ID3 " e a duracao no COMM. */
static void ler_aiff(FILE *f, dap_etiquetas_t *e)
{
    uint8_t h[12];
    fseek(f, 0, SEEK_SET);
    if (fread(h, 1, 12, f) != 12) return;
    for (int blocos = 0; blocos < 64; blocos++) {
        uint8_t c[8];
        if (fread(c, 1, 8, f) != 8) break;
        uint32_t t = be32t(c + 4);
        long depois = ftell(f) + (long)t + (long)(t & 1);
        if ((memcmp(c, "NAME", 4) == 0 || memcmp(c, "AUTH", 4) == 0) && t < 192) {
            uint8_t v[192];
            char s[192];
            if (fread(v, 1, t, f) != t) break;
            texto_id3(s, sizeof(s), v, (int)t, 0);
            char id[5] = { 0 };
            memcpy(id, c, 4);
            campo(e, id, s);
        } else if (memcmp(c, "ID3 ", 4) == 0) {
            ler_id3v2(f, ftell(f), e);
        } else if (memcmp(c, "COMM", 4) == 0 && t >= 18) {
            uint8_t m[18];
            if (fread(m, 1, 18, f) != 18) break;
            uint32_t quadros = be32t(m + 2);
            int expoente = ((m[8] & 0x7F) << 8) | m[9];
            uint32_t mant = be32t(m + 10);
            /* 80 bits: mantissa de 64 com o expoente -- os 32 de cima bastam para a taxa */
            int desloc = expoente - 16383 - 31;
            double taxa = (double)mant;
            while (desloc > 0) { taxa *= 2; desloc--; }
            while (desloc < 0) { taxa /= 2; desloc++; }
            if (taxa > 0 && !e->duracao_ms) e->duracao_ms = (int)(quadros * 1000.0 / taxa);
        }
        fseek(f, depois, SEEK_SET);
    }
}

/* MP3: a duracao pelo Xing/Info (VBR) ou pela taxa de bits do primeiro quadro (CBR). */
static void duracao_mp3(FILE *f, long inicio, dap_etiquetas_t *e)
{
    if (e->duracao_ms) return;
    uint8_t b[4096];
    fseek(f, 0, SEEK_END);
    long tamanho = ftell(f);
    fseek(f, inicio, SEEK_SET);
    int n = (int)fread(b, 1, sizeof(b), f);
    for (int i = 0; i + 4 < n; i++) {
        if (b[i] != 0xFF || (b[i + 1] & 0xE0) != 0xE0) continue;
        int versao = (b[i + 1] >> 3) & 3, camada = (b[i + 1] >> 1) & 3, br = b[i + 2] >> 4, sr = (b[i + 2] >> 2) & 3;
        if (versao == 1 || camada != 1 || br == 0 || br == 15 || sr == 3) continue;   /* so a camada III */
        static const int kbps1[16] = { 0, 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320, 0 };
        static const int kbps2[16] = { 0, 8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160, 0 };
        static const int hz[4][3] = { {11025, 12000, 8000}, {0, 0, 0}, {22050, 24000, 16000}, {44100, 48000, 32000} };
        int mpeg1 = versao == 3, taxa = hz[versao][sr], kbps = mpeg1 ? kbps1[br] : kbps2[br];
        int mono = (b[i + 3] >> 6) == 3;
        int amostras_por_quadro = mpeg1 ? 1152 : 576;
        int lado = mpeg1 ? (mono ? 17 : 32) : (mono ? 9 : 17);
        int x = i + 4 + lado;
        if (x + 12 < n && (memcmp(b + x, "Xing", 4) == 0 || memcmp(b + x, "Info", 4) == 0) && (b[x + 7] & 1)) {
            uint32_t quadros = be32t(b + x + 8);
            e->duracao_ms = (int)((uint64_t)quadros * amostras_por_quadro * 1000 / taxa);
            return;
        }
        if (i + 36 + 18 < n && memcmp(b + i + 36, "VBRI", 4) == 0) {
            uint32_t quadros = be32t(b + i + 36 + 14);
            e->duracao_ms = (int)((uint64_t)quadros * amostras_por_quadro * 1000 / taxa);
            return;
        }
        if (kbps) e->duracao_ms = (int)((uint64_t)(tamanho - inicio - i) * 8 / kbps);
        return;
    }
}

/* DSF: o ID3v2 no fim (o ponteiro no cabecalho) e a duracao no "fmt ". */
static void ler_dsf(FILE *f, dap_etiquetas_t *e)
{
    uint8_t h[80];
    fseek(f, 0, SEEK_SET);
    if (fread(h, 1, 80, f) != 80) return;
    uint64_t meta = (uint64_t)le32t(h + 24) << 32 | le32t(h + 20);
    uint32_t taxa = le32t(h + 28 + 28);
    uint64_t amostras = (uint64_t)le32t(h + 28 + 40) << 32 | le32t(h + 28 + 36);
    if (taxa && !e->duracao_ms) e->duracao_ms = (int)(amostras * 1000 / taxa);
    if (meta) ler_id3v2(f, (long)meta, e);
}

/* DSDIFF: o bloco "ID3 " e o tamanho do "DSD ". */
static void ler_dff(FILE *f, dap_etiquetas_t *e)
{
    uint8_t h[16];
    fseek(f, 0, SEEK_SET);
    if (fread(h, 1, 16, f) != 16) return;
    long fim = 12 + (long)((uint64_t)be32t(h + 4) << 32 | be32t(h + 8)), pos = 16;
    uint32_t taxa = 0;
    int canais = 2;
    while (pos + 12 <= fim) {
        uint8_t c[12];
        fseek(f, pos, SEEK_SET);
        if (fread(c, 1, 12, f) != 12) break;
        uint64_t t = (uint64_t)be32t(c + 4) << 32 | be32t(c + 8);
        if (memcmp(c, "PROP", 4) == 0) {
            long p = pos + 16;
            while (p + 12 <= pos + 12 + (long)t) {
                uint8_t q[12], v[4];
                fseek(f, p, SEEK_SET);
                if (fread(q, 1, 12, f) != 12 || fread(v, 1, 4, f) != 4) break;
                uint64_t tq = (uint64_t)be32t(q + 4) << 32 | be32t(q + 8);
                if (memcmp(q, "FS  ", 4) == 0) taxa = be32t(v);
                else if (memcmp(q, "CHNL", 4) == 0) canais = (v[0] << 8) | v[1];
                p += 12 + (long)tq + (long)(tq & 1);
            }
        } else if (memcmp(c, "DSD ", 4) == 0) {
            if (taxa && canais && !e->duracao_ms) e->duracao_ms = (int)(t / canais * 8 * 1000 / taxa);
        } else if (memcmp(c, "ID3 ", 4) == 0) {
            ler_id3v2(f, pos + 12, e);
        }
        pos += 12 + (long)t + (long)(t & 1);
    }
}

/* ---------------------------------------------------- o nome do arquivo */

static void do_nome(const char *caminho, dap_etiquetas_t *e)
{
    const char *nome = strrchr(caminho, '/');
    nome = nome ? nome + 1 : caminho;
    char base[160];
    snprintf(base, sizeof(base), "%s", nome);
    char *ponto = strrchr(base, '.');
    if (ponto) *ponto = 0;

    char *s = base;
    /* "07 - Titulo", "07. Titulo", "07 Titulo": o numero da faixa */
    if (isdigit((unsigned char)s[0]) && isdigit((unsigned char)s[1])) {
        char *p = s;
        while (isdigit((unsigned char)*p)) p++;
        if (*p == ' ' || *p == '.' || *p == '-' || *p == '_') {
            int n = atoi(s);
            while (*p == ' ' || *p == '.' || *p == '-' || *p == '_') p++;
            if (*p) {
                if (!e->faixa && n > 0 && n < 1000) e->faixa = n;
                s = p;
            }
        }
    }
    /* "Artista - Titulo" */
    char *traco = strstr(s, " - ");
    if (traco && !e->titulo[0]) {
        *traco = 0;
        if (!e->artista[0]) guardar(e->artista, sizeof(e->artista), s);
        guardar(e->titulo, sizeof(e->titulo), traco + 3);
    }
    guardar(e->titulo, sizeof(e->titulo), s);
}

void dap_ler_etiquetas(const char *caminho, int formato, dap_etiquetas_t *e)
{
    memset(e, 0, sizeof(*e));
    FILE *f = fopen(caminho, "rb");
    if (f) {
        long depois_da_id3 = ler_id3v2(f, 0, e);
        switch (formato) {
        case DAP_FMT_MP3:
            duracao_mp3(f, depois_da_id3, e);
            ler_id3v1(f, e);
            break;
        case DAP_FMT_FLAC: {
            /* FLAC nativo (com ID3 na frente, as vezes) ou dentro do Ogg */
            uint8_t m[4];
            fseek(f, depois_da_id3, SEEK_SET);
            if (fread(m, 1, 4, f) == 4 && memcmp(m, "fLaC", 4) == 0) ler_flac(f, depois_da_id3, e);
            else ler_ogg(f, e);
            break;
        }
        case DAP_FMT_OGG:
        case DAP_FMT_OPUS: ler_ogg(f, e); break;
        case DAP_FMT_WAV: ler_wav(f, e); break;
        case DAP_FMT_AIFF: ler_aiff(f, e); break;
        case DAP_FMT_DSF: ler_dsf(f, e); break;
        case DAP_FMT_DFF: ler_dff(f, e); break;
        case DAP_FMT_M4A:
            ler_m4a(f, e);
            if (!e->duracao_ms) {   /* AAC cru (ADTS) */
                duracao_adts(f, depois_da_id3, e);
                if (!e->titulo[0]) ler_id3v1(f, e);
            }
            break;
        case DAP_FMT_ALAC: ler_m4a(f, e); break;
        case DAP_FMT_APE: ler_mac(f, depois_da_id3, e); ler_wv(f, e); break;
        case DAP_FMT_WV: ler_wv(f, e); break;
        case DAP_FMT_WMA: ler_asf(f, e); break;
        default: ler_id3v1(f, e); break;
        }
        fclose(f);
    }
    do_nome(caminho, e);
    if (!e->artista_album[0]) guardar(e->artista_album, sizeof(e->artista_album), e->artista);
}
