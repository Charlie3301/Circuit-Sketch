/* dap_biblioteca.c -- as musicas do cartao e do pendrive, e as listas delas.
 *
 * A varredura desce todas as pastas (menos as escondidas, que comecam com
 * ponto), olha o CONTEUDO de cada arquivo com extensao de audio e le as
 * etiquetas. O resultado fica num cache no cartao (/.dap/biblioteca.bin): no
 * boot seguinte, o arquivo de mesmo caminho e mesmo tamanho nao e aberto de
 * novo. Os textos (artistas, albuns...) moram num reservatorio so, sem
 * repetir: dez faixas do mesmo album guardam o nome dele uma vez.
 *
 * As listas saem em ordem alfabetica sem diferenca de maiuscula nem de
 * acento ("Álbum" junto de "album"), e o vazio ("sem artista") por ultimo.
 */
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <ctype.h>
#include <dirent.h>
#include <sys/stat.h>
#include "dap_interno.h"

dap_faixa_t *dap_faixas_tab;
int dap_n_faixas;
volatile int dap_varredura_lidas, dap_varredura_achadas, dap_varredura_pronta;
char dap_varredura_atual[96];

/* --------------------------------------------------------- reservatorio */

static char *reservatorio;
static uint32_t usado, capacidade;
static uint32_t *hash_textos;          /* deslocamentos, por hash (0 = vazio) */
static uint32_t hash_cap;

static uint32_t hash_de(const char *s)
{
    uint32_t h = 2166136261u;
    while (*s) h = (h ^ (uint8_t)*s++) * 16777619u;
    return h;
}

/* O FAT nao diferencia maiusculas: o caminho e procurado sem elas. */
static uint32_t hash_do_caminho(const char *s)
{
    uint32_t h = 2166136261u;
    for (; *s; s++) {
        uint8_t c = (uint8_t)*s;
        if (c >= 'A' && c <= 'Z') c = (uint8_t)(c + 32);
        h = (h ^ c) * 16777619u;
    }
    return h;
}

void dap_copiar(char *destino, int tam, const char *s)
{
    if (tam <= 0) return;
    int n = (int)strlen(s);
    if (n >= tam) {
        n = tam - 1;
        /* nao deixa um pedaco de caractere no fim */
        while (n > 0 && ((uint8_t)s[n] & 0xC0) == 0x80) n--;
    }
    memcpy(destino, s, (size_t)n);
    destino[n] = 0;
}

const char *dap_txt(uint32_t d)
{
    return reservatorio && d < usado ? reservatorio + d : "";
}

static void zerar_reservatorio(void)
{
    if (!reservatorio) {
        capacidade = 256 * 1024;
        reservatorio = (char *)dap_alocar(capacidade);
    }
    usado = 1;
    if (reservatorio) reservatorio[0] = 0;
    if (!hash_textos) {
        hash_cap = 16384;
        hash_textos = (uint32_t *)dap_alocar(hash_cap * sizeof(uint32_t));
    }
    if (hash_textos) memset(hash_textos, 0, hash_cap * sizeof(uint32_t));
}

static void rehash(void)
{
    uint32_t novo_cap = hash_cap * 2;
    uint32_t *novo = (uint32_t *)dap_alocar(novo_cap * sizeof(uint32_t));
    if (!novo) return;
    memset(novo, 0, novo_cap * sizeof(uint32_t));
    for (uint32_t i = 0; i < hash_cap; i++) {
        uint32_t d = hash_textos[i];
        if (!d) continue;
        uint32_t j = hash_de(reservatorio + d) & (novo_cap - 1);
        while (novo[j]) j = (j + 1) & (novo_cap - 1);
        novo[j] = d;
    }
    free(hash_textos);
    hash_textos = novo;
    hash_cap = novo_cap;
}

/* O texto no reservatorio (o mesmo texto, o mesmo deslocamento). */
static uint32_t guardar_texto(const char *s)
{
    if (!s || !s[0] || !reservatorio || !hash_textos) return DAP_SEM_TEXTO;
    static uint32_t ocupados;
    if (usado == 1) ocupados = 0;
    uint32_t j = hash_de(s) & (hash_cap - 1);
    while (hash_textos[j]) {
        if (!strcmp(reservatorio + hash_textos[j], s)) return hash_textos[j];
        j = (j + 1) & (hash_cap - 1);
    }
    size_t n = strlen(s) + 1;
    if (usado + n > capacidade) {
        uint32_t nova = capacidade * 2;
        while (usado + n > nova) nova *= 2;
        char *r = (char *)dap_realocar(reservatorio, nova);
        if (!r) return DAP_SEM_TEXTO;
        reservatorio = r;
        capacidade = nova;
    }
    uint32_t d = usado;
    memcpy(reservatorio + d, s, n);
    usado += (uint32_t)n;
    hash_textos[j] = d;
    if (++ocupados * 2 > hash_cap) rehash();
    return d;
}

/* ---------------------------------------------------------- ordenacao */

/* A letra sem acento e minuscula: "Á" -> "a", "Ç" -> "c". */
static uint32_t dobrar(uint32_t c)
{
    if (c >= 'A' && c <= 'Z') return c + 32;
    if (c < 0xC0) return c;
    if (c <= 0x17F) {
        static const char base[] =
            /* C0 */ "aaaaaaaceeeeiiiidnooooo*ouuuuyts"
            /* E0 */ "aaaaaaaceeeeiiiidnooooo/ouuuuyty"
            /* 100 */ "aaaaaaccccccccddddeeeeeeeeeegggggggghhhhiiiiiiiiiiiijjkkklllllll"
            /* 140 */ "lllnnnnnnnnnoooooooorrrrrrssssssssttttttuuuuuuuuuuuuwwyyyzzzzzzs";
        return (unsigned char)base[c - 0xC0];
    }
    return c;
}

static uint32_t proximo_utf8(const char **s)
{
    const uint8_t *u = (const uint8_t *)*s;
    uint32_t c = u[0];
    int n = c < 0x80 ? 1 : (c & 0xE0) == 0xC0 ? 2 : (c & 0xF0) == 0xE0 ? 3 : (c & 0xF8) == 0xF0 ? 4 : 1;
    if (n > 1) {
        c &= 0x3F >> (n - 1);
        for (int i = 1; i < n; i++) {
            if ((u[i] & 0xC0) != 0x80) { *s += 1; return u[0]; }
            c = (c << 6) | (u[i] & 0x3F);
        }
    }
    *s += n;
    return c;
}

static int comparar_texto(const char *a, const char *b)
{
    /* o vazio vai para o fim */
    if (!a[0] || !b[0]) return !a[0] - !b[0];
    while (*a && *b) {
        uint32_t ca = dobrar(proximo_utf8(&a)), cb = dobrar(proximo_utf8(&b));
        if (ca != cb) return ca < cb ? -1 : 1;
    }
    return (*a != 0) - (*b != 0);
}

static int comparar_d(uint32_t a, uint32_t b) { return comparar_texto(dap_txt(a), dap_txt(b)); }

/* A ordem de tocar: disco, faixa e titulo (e o caminho, para desempatar). */
static int ordem_do_album(const dap_faixa_t *a, const dap_faixa_t *b)
{
    if (a->disco != b->disco) return a->disco - b->disco;
    if (a->faixa != b->faixa) {
        if (!a->faixa || !b->faixa) return !a->faixa - !b->faixa;
        return a->faixa - b->faixa;
    }
    int c = comparar_d(a->titulo, b->titulo);
    return c ? c : comparar_d(a->caminho, b->caminho);
}

/* Artista, album e a ordem do album: para gênero, ano, compositor. */
static int ordem_geral(const void *pa, const void *pb)
{
    const dap_faixa_t *a = &dap_faixas_tab[*(const int *)pa], *b = &dap_faixas_tab[*(const int *)pb];
    int c = comparar_d(a->artista, b->artista);
    if (!c) c = comparar_d(a->album, b->album);
    return c ? c : ordem_do_album(a, b);
}

static int ordem_album(const void *pa, const void *pb)
{
    return ordem_do_album(&dap_faixas_tab[*(const int *)pa], &dap_faixas_tab[*(const int *)pb]);
}

static int ordem_titulo(const void *pa, const void *pb)
{
    const dap_faixa_t *a = &dap_faixas_tab[*(const int *)pa], *b = &dap_faixas_tab[*(const int *)pb];
    int c = comparar_d(a->titulo, b->titulo);
    if (!c) c = comparar_d(a->artista, b->artista);
    return c ? c : comparar_d(a->caminho, b->caminho);
}

/* ------------------------------------------------------------ varredura */

#define MAGICA_CACHE 0x42504144u   /* "DAPB" */
#define VERSAO_CACHE 3   /* 3: o ALAC, o Opus e o WavPack passaram a tocar */
#define CAMINHO_CACHE DAP_RAIZ_CARTAO "/.dap/biblioteca.bin"

/* O cache da varredura anterior: o caminho e o tamanho de cada faixa, e as etiquetas. */
static dap_faixa_t *cache_faixas;
static char *cache_textos;
static int cache_n;
static uint32_t cache_usado;
static int *cache_hash;
static int cache_hash_cap;

static const char *cache_txt(uint32_t d) { return cache_textos && d < cache_usado ? cache_textos + d : ""; }

static void carregar_cache(void)
{
    FILE *f = fopen(CAMINHO_CACHE, "rb");
    if (!f) return;
    uint32_t h[4];
    if (fread(h, 4, 4, f) == 4 && h[0] == MAGICA_CACHE && h[1] == VERSAO_CACHE && h[2] <= DAP_MAX_FAIXAS && h[3] < (64u << 20)) {
        cache_n = (int)h[2];
        cache_usado = h[3];
        cache_faixas = (dap_faixa_t *)dap_alocar((size_t)cache_n * sizeof(dap_faixa_t) + 1);
        cache_textos = (char *)dap_alocar(cache_usado + 1);
        if (!cache_faixas || !cache_textos
            || fread(cache_faixas, sizeof(dap_faixa_t), cache_n, f) != (size_t)cache_n
            || fread(cache_textos, 1, cache_usado, f) != cache_usado) {
            free(cache_faixas); free(cache_textos);
            cache_faixas = NULL; cache_textos = NULL; cache_n = 0;
        }
    }
    fclose(f);
    if (cache_n) {
        cache_hash_cap = 1;
        while (cache_hash_cap < cache_n * 2) cache_hash_cap <<= 1;
        cache_hash = (int *)dap_alocar(cache_hash_cap * sizeof(int));
        if (!cache_hash) { cache_n = 0; return; }
        for (int i = 0; i < cache_hash_cap; i++) cache_hash[i] = -1;
        for (int i = 0; i < cache_n; i++) {
            uint32_t j = hash_de(cache_txt(cache_faixas[i].caminho)) & (cache_hash_cap - 1);
            while (cache_hash[j] >= 0) j = (j + 1) & (cache_hash_cap - 1);
            cache_hash[j] = i;
        }
    }
}

static int no_cache(const char *caminho, uint32_t tamanho)
{
    if (!cache_n) return -1;
    uint32_t j = hash_de(caminho) & (cache_hash_cap - 1);
    while (cache_hash[j] >= 0) {
        const dap_faixa_t *c = &cache_faixas[cache_hash[j]];
        if (c->tamanho == tamanho && !strcmp(cache_txt(c->caminho), caminho)) return cache_hash[j];
        j = (j + 1) & (cache_hash_cap - 1);
    }
    return -1;
}

static void soltar_cache(void)
{
    free(cache_faixas); free(cache_textos); free(cache_hash);
    cache_faixas = NULL; cache_textos = NULL; cache_hash = NULL;
    cache_n = 0;
}

static void gravar_cache(void)
{
    if (!dap_cartao_ok) return;
    mkdir(DAP_RAIZ_CARTAO "/.dap", 0777);
    FILE *f = fopen(CAMINHO_CACHE, "wb");
    if (!f) return;
    uint32_t h[4] = { MAGICA_CACHE, VERSAO_CACHE, (uint32_t)dap_n_faixas, usado };
    fwrite(h, 4, 4, f);
    fwrite(dap_faixas_tab, sizeof(dap_faixa_t), dap_n_faixas, f);
    fwrite(reservatorio, 1, usado, f);
    fclose(f);
}

static int origem_de(const char *caminho)
{
    return strncmp(caminho, DAP_RAIZ_USB, strlen(DAP_RAIZ_USB)) == 0 ? 1 : 0;
}

static void acrescentar(const char *caminho, const char *pasta, uint32_t tamanho)
{
    if (dap_n_faixas >= DAP_MAX_FAIXAS) return;
    dap_faixa_t *t = &dap_faixas_tab[dap_n_faixas];
    memset(t, 0, sizeof(*t));
    const char *nome = strrchr(caminho, '/');
    dap_copiar(dap_varredura_atual, (int)sizeof(dap_varredura_atual), nome ? nome + 1 : caminho);

    int c = no_cache(caminho, tamanho);
    if (c >= 0) {
        /* do cache: os textos vao para o reservatorio novo */
        const dap_faixa_t *v = &cache_faixas[c];
        *t = *v;
        t->caminho = guardar_texto(caminho);
        t->pasta = guardar_texto(pasta);
        t->titulo = guardar_texto(cache_txt(v->titulo));
        t->artista = guardar_texto(cache_txt(v->artista));
        t->album = guardar_texto(cache_txt(v->album));
        t->artista_album = guardar_texto(cache_txt(v->artista_album));
        t->genero = guardar_texto(cache_txt(v->genero));
        t->compositor = guardar_texto(cache_txt(v->compositor));
        t->marcas &= (uint8_t)~DAP_MARCA_FAVORITA;
        dap_n_faixas++;
        dap_varredura_achadas = dap_n_faixas;
        return;
    }

    int formato = dap_detectar_formato(caminho);
    if (formato == DAP_FMT_NADA) return;   /* um HTML com nome de .mp3: fica de fora */
    dap_etiquetas_t e;
    dap_ler_etiquetas(caminho, formato, &e);
    t->caminho = guardar_texto(caminho);
    t->pasta = guardar_texto(pasta);
    t->titulo = guardar_texto(e.titulo);
    t->artista = guardar_texto(e.artista);
    t->album = guardar_texto(e.album);
    t->artista_album = guardar_texto(e.artista_album);
    t->genero = guardar_texto(e.genero);
    t->compositor = guardar_texto(e.compositor);
    t->ano = (uint16_t)e.ano;
    t->faixa = (uint16_t)(e.faixa > 0 && e.faixa < 65536 ? e.faixa : 0);
    t->disco = (uint8_t)(e.disco > 0 && e.disco < 256 ? e.disco : 0);
    t->duracao_ms = (uint32_t)(e.duracao_ms > 0 ? e.duracao_ms : 0);
    t->tamanho = tamanho;
    t->formato = (uint8_t)formato;
    t->origem = (uint8_t)origem_de(caminho);
    if (!dap_formato_tocavel(formato)) t->marcas |= DAP_MARCA_ESTRAGADA;
    dap_n_faixas++;
    dap_varredura_achadas = dap_n_faixas;
}

static void varrer_pasta(const char *pasta, int profundidade)
{
    if (profundidade > 12) return;
    DIR *d = opendir(pasta);
    if (!d) return;
    struct dirent *ent;
    char caminho[DAP_MAX_CAMINHO];
    while ((ent = readdir(d)) != NULL) {
        if (ent->d_name[0] == '.') continue;
        if (!strcmp(ent->d_name, "System Volume Information")) continue;
        if (snprintf(caminho, sizeof(caminho), "%s/%s", pasta, ent->d_name) >= (int)sizeof(caminho)) continue;
        struct stat st;
        if (stat(caminho, &st) != 0) continue;
        if (S_ISDIR(st.st_mode)) {
            varrer_pasta(caminho, profundidade + 1);
            continue;
        }
        dap_varredura_lidas++;
        if (dap_extensao_de_audio(ent->d_name)) acrescentar(caminho, pasta, (uint32_t)st.st_size);
        vTaskDelay(0);
    }
    closedir(d);
}

/* o caminho -> o indice, para as playlists e as favoritas */
static int *por_caminho;
static int por_caminho_cap;

static void indexar_caminhos(void)
{
    por_caminho_cap = 1;
    while (por_caminho_cap < dap_n_faixas * 2 + 2) por_caminho_cap <<= 1;
    free(por_caminho);
    por_caminho = (int *)dap_alocar(por_caminho_cap * sizeof(int));
    if (!por_caminho) return;
    for (int i = 0; i < por_caminho_cap; i++) por_caminho[i] = -1;
    for (int i = 0; i < dap_n_faixas; i++) {
        uint32_t j = hash_do_caminho(dap_txt(dap_faixas_tab[i].caminho)) & (por_caminho_cap - 1);
        while (por_caminho[j] >= 0) j = (j + 1) & (por_caminho_cap - 1);
        por_caminho[j] = i;
    }
}

int dap_faixa_do_caminho(const char *caminho)
{
    if (!por_caminho) return -1;
    uint32_t j = hash_do_caminho(caminho) & (por_caminho_cap - 1);
    while (por_caminho[j] >= 0) {
        if (!strcasecmp(dap_txt(dap_faixas_tab[por_caminho[j]].caminho), caminho)) return por_caminho[j];
        j = (j + 1) & (por_caminho_cap - 1);
    }
    return -1;
}

int dap_varrer(void)
{
    dap_varredura_pronta = 0;
    dap_varredura_lidas = dap_varredura_achadas = 0;
    if (!dap_faixas_tab) dap_faixas_tab = (dap_faixa_t *)dap_alocar(DAP_MAX_FAIXAS * sizeof(dap_faixa_t));
    if (!dap_faixas_tab) return 0;
    dap_n_faixas = 0;
    carregar_cache();
    zerar_reservatorio();
    if (dap_cartao_ok) varrer_pasta(DAP_RAIZ_CARTAO, 0);
    if (dap_usb_ok) varrer_pasta(DAP_RAIZ_USB, 0);
    soltar_cache();
    indexar_caminhos();
    dap_carregar_favoritas();
    gravar_cache();
    dap_varredura_atual[0] = 0;
    dap_varredura_pronta = 1;
    return dap_n_faixas;
}

/* ---------------------------------------------------------- categorias */

static uint32_t valor_da_faixa(dap_categoria_t cat, const dap_faixa_t *t)
{
    switch (cat) {
    case DAP_CAT_ARTISTAS: return t->artista;
    case DAP_CAT_ALBUNS: return t->album;
    case DAP_CAT_ARTISTAS_ALBUM: return t->artista_album;
    case DAP_CAT_GENEROS: return t->genero;
    case DAP_CAT_COMPOSITORES: return t->compositor;
    case DAP_CAT_ANOS: return t->ano;
    default: return 0;
    }
}

/* O mesmo album: o nome e o artista do album. */
static int mesmo_album(const dap_faixa_t *a, const dap_faixa_t *b)
{
    return a->album == b->album && a->artista_album == b->artista_album;
}

static dap_categoria_t ordem_cat;
static int ordem_valores(const void *pa, const void *pb)
{
    uint32_t a = *(const uint32_t *)pa, b = *(const uint32_t *)pb;
    if (ordem_cat == DAP_CAT_ANOS) {
        if (!a || !b) return !a - !b;
        return (int)b - (int)a;   /* o ano mais novo primeiro */
    }
    if (ordem_cat == DAP_CAT_ALBUNS) {
        const dap_faixa_t *x = &dap_faixas_tab[a], *y = &dap_faixas_tab[b];
        int c = comparar_d(x->album, y->album);
        return c ? c : comparar_d(x->artista_album, y->artista_album);
    }
    return comparar_d(a, b);
}

int dap_valores(dap_categoria_t cat, uint32_t *saida, int max)
{
    int n = 0;
    for (int i = 0; i < dap_n_faixas && n < max; i++) {
        const dap_faixa_t *t = &dap_faixas_tab[i];
        uint32_t v = cat == DAP_CAT_ALBUNS ? (uint32_t)i : valor_da_faixa(cat, t);
        int ja = 0;
        for (int k = 0; k < n && !ja; k++)
            ja = cat == DAP_CAT_ALBUNS ? mesmo_album(&dap_faixas_tab[saida[k]], t) : saida[k] == v;
        if (!ja) saida[n++] = v;
    }
    ordem_cat = cat;
    qsort(saida, n, sizeof(uint32_t), ordem_valores);
    return n;
}

int dap_faixas_de(dap_categoria_t cat, uint32_t valor, int *saida, int max)
{
    int n = 0;
    for (int i = 0; i < dap_n_faixas && n < max; i++) {
        const dap_faixa_t *t = &dap_faixas_tab[i];
        int entra;
        switch (cat) {
        case DAP_CAT_TODAS: entra = 1; break;
        case DAP_CAT_FAVORITAS: entra = (t->marcas & DAP_MARCA_FAVORITA) != 0; break;
        case DAP_CAT_ALBUNS: entra = (int)valor < dap_n_faixas && mesmo_album(t, &dap_faixas_tab[valor]); break;
        default: entra = valor_da_faixa(cat, t) == valor; break;
        }
        if (entra) saida[n++] = i;
    }
    qsort(saida, n, sizeof(int), cat == DAP_CAT_TODAS || cat == DAP_CAT_FAVORITAS ? ordem_titulo
                                 : cat == DAP_CAT_ALBUNS ? ordem_album : ordem_geral);
    return n;
}

int dap_albuns_de(dap_categoria_t cat, uint32_t artista, uint32_t *saida, int max)
{
    int n = 0;
    for (int i = 0; i < dap_n_faixas && n < max; i++) {
        const dap_faixa_t *t = &dap_faixas_tab[i];
        if ((cat == DAP_CAT_ARTISTAS ? t->artista : t->artista_album) != artista) continue;
        int ja = 0;
        for (int k = 0; k < n && !ja; k++) ja = mesmo_album(&dap_faixas_tab[saida[k]], t);
        if (!ja) saida[n++] = (uint32_t)i;
    }
    ordem_cat = DAP_CAT_ALBUNS;
    qsort(saida, n, sizeof(uint32_t), ordem_valores);
    return n;
}

int dap_faixas_do_album_de(dap_categoria_t cat, uint32_t artista, uint32_t album, int *saida, int max)
{
    int n = 0;
    if ((int)album >= dap_n_faixas) return 0;
    for (int i = 0; i < dap_n_faixas && n < max; i++) {
        const dap_faixa_t *t = &dap_faixas_tab[i];
        if (!mesmo_album(t, &dap_faixas_tab[album])) continue;
        if (cat == DAP_CAT_ARTISTAS && t->artista != artista) continue;
        saida[n++] = i;
    }
    qsort(saida, n, sizeof(int), ordem_album);
    return n;
}

/* --------------------------------------------------------------- pastas */

static int ordem_nomes(const void *a, const void *b) { return comparar_texto((const char *)a, (const char *)b); }
static int ordem_ponteiros(const void *a, const void *b) { return comparar_texto(*(char *const *)a, *(char *const *)b); }

int dap_subpastas(const char *pasta, char ***saida)
{
    *saida = NULL;
    DIR *d = opendir(pasta);
    if (!d) return 0;
    /* os nomes, um atras do outro, num texto que cresce */
    char *texto = NULL;
    size_t usado = 0, cap = 0;
    int n = 0;
    struct dirent *ent;
    char caminho[DAP_MAX_CAMINHO];
    while ((ent = readdir(d)) != NULL) {
        if (ent->d_name[0] == '.' || !strcmp(ent->d_name, "System Volume Information")) continue;
        if (snprintf(caminho, sizeof(caminho), "%s/%s", pasta, ent->d_name) >= (int)sizeof(caminho)) continue;
        struct stat st;
        if (stat(caminho, &st) != 0 || !S_ISDIR(st.st_mode)) continue;
        size_t l = strlen(ent->d_name) + 1;
        if (usado + l > cap) {
            size_t novo_cap = cap ? cap * 2 : 2048;
            while (novo_cap < usado + l) novo_cap *= 2;
            char *novo = (char *)dap_realocar(texto, novo_cap);
            if (!novo) break;
            texto = novo;
            cap = novo_cap;
        }
        memcpy(texto + usado, ent->d_name, l);
        usado += l;
        n++;
    }
    closedir(d);
    if (n == 0) { free(texto); return 0; }
    /* um bloco so: os ponteiros na frente, os nomes atras */
    char **lista = (char **)dap_alocar((size_t)n * sizeof(char *) + usado);
    if (!lista) { free(texto); return 0; }
    char *p = (char *)(lista + n);
    memcpy(p, texto, usado);
    free(texto);
    for (int i = 0; i < n; i++) {
        lista[i] = p;
        p += strlen(p) + 1;
    }
    qsort(lista, n, sizeof(char *), ordem_ponteiros);
    *saida = lista;
    return n;
}

int dap_faixas_da_pasta(const char *pasta, int *saida, int max)
{
    int n = 0;
    for (int i = 0; i < dap_n_faixas && n < max; i++)
        if (!strcmp(dap_txt(dap_faixas_tab[i].pasta), pasta)) saida[n++] = i;
    qsort(saida, n, sizeof(int), ordem_album);
    return n;
}

/* ------------------------------------------------- favoritas e playlists */

#define ARQUIVO_FAVORITAS DAP_RAIZ_CARTAO "/.dap/favoritas.m3u"
#define PASTA_PLAYLISTS DAP_RAIZ_CARTAO "/Playlists"

void dap_carregar_favoritas(void)
{
    FILE *f = fopen(ARQUIVO_FAVORITAS, "r");
    if (!f) return;
    char linha[300];
    while (fgets(linha, sizeof(linha), f)) {
        linha[strcspn(linha, "\r\n")] = 0;
        if (!linha[0] || linha[0] == '#') continue;
        int i = dap_faixa_do_caminho(linha);
        if (i >= 0) dap_faixas_tab[i].marcas |= DAP_MARCA_FAVORITA;
    }
    fclose(f);
}

void dap_favoritar(int faixa, int sim)
{
    if (faixa < 0 || faixa >= dap_n_faixas) return;
    if (sim) dap_faixas_tab[faixa].marcas |= DAP_MARCA_FAVORITA;
    else dap_faixas_tab[faixa].marcas &= (uint8_t)~DAP_MARCA_FAVORITA;
    if (!dap_cartao_ok) return;
    mkdir(DAP_RAIZ_CARTAO "/.dap", 0777);
    FILE *f = fopen(ARQUIVO_FAVORITAS, "w");
    if (!f) return;
    fprintf(f, "#EXTM3U\n");
    for (int i = 0; i < dap_n_faixas; i++)
        if (dap_faixas_tab[i].marcas & DAP_MARCA_FAVORITA) fprintf(f, "%s\n", dap_txt(dap_faixas_tab[i].caminho));
    fclose(f);
}

static int eh_playlist(const char *nome_do_arquivo)
{
    const char *p = strrchr(nome_do_arquivo, '.');
    return p && (!strcasecmp(p, ".m3u") || !strcasecmp(p, ".m3u8"));
}

int dap_playlists(char nomes[][DAP_MAX_NOME_PLAYLIST], int max)
{
    int n = 0;
    DIR *d = opendir(PASTA_PLAYLISTS);
    if (!d) return 0;
    struct dirent *ent;
    while ((ent = readdir(d)) != NULL && n < max) {
        if (!eh_playlist(ent->d_name)) continue;
        char sem_extensao[256];
        int l = (int)(strrchr(ent->d_name, '.') - ent->d_name);
        memcpy(sem_extensao, ent->d_name, (size_t)l);
        sem_extensao[l] = 0;
        dap_copiar(nomes[n++], DAP_MAX_NOME_PLAYLIST, sem_extensao);
    }
    closedir(d);
    qsort(nomes, n, DAP_MAX_NOME_PLAYLIST, ordem_nomes);
    /* a mesma playlist em .m3u e em .m3u8 aparece uma vez so */
    int k = 0;
    for (int i = 0; i < n; i++)
        if (k == 0 || strcmp(nomes[k - 1], nomes[i])) memmove(nomes[k++], nomes[i], DAP_MAX_NOME_PLAYLIST);
    return k;
}

/* O arquivo de uma playlist pelo nome da tela: o .m3u, o .m3u8 ou, se o nome
 * e comprido (a lista o cortou), o primeiro arquivo que comeca igual. */
static int arquivo_da_playlist(const char *nome, char *caminho, int tam)
{
    static const char *const extensoes[] = { ".m3u", ".m3u8" };
    struct stat st;
    for (int k = 0; k < 2; k++)
        if (snprintf(caminho, tam, PASTA_PLAYLISTS "/%s%s", nome, extensoes[k]) < tam && stat(caminho, &st) == 0) return 1;
    size_t l = strlen(nome);
    if (l + 4 < DAP_MAX_NOME_PLAYLIST) return 0;
    DIR *d = opendir(PASTA_PLAYLISTS);
    if (!d) return 0;
    struct dirent *ent;
    int achou = 0;
    while (!achou && (ent = readdir(d)) != NULL)
        if (eh_playlist(ent->d_name) && !strncmp(ent->d_name, nome, l))
            achou = snprintf(caminho, tam, PASTA_PLAYLISTS "/%s", ent->d_name) < tam;
    closedir(d);
    return achou;
}

/* Uma linha de .m3u: caminho do player (/sd/...), da raiz do cartao ou do
 * pendrive (/Musicas/...), do Windows (E:\Musicas\...) ou relativo a pasta
 * da playlist (../Musicas/..., o que os programas do computador gravam). */
static int faixa_da_linha(char *l, char *completo, int tam)
{
    for (char *c = l; *c; c++) if (*c == '\\') *c = '/';
    if (isalpha((uint8_t)l[0]) && l[1] == ':') l += 2;   /* a letra do disco */
    if (l[0] == '/') {
        int i = dap_faixa_do_caminho(l);
        if (i < 0 && snprintf(completo, tam, DAP_RAIZ_CARTAO "%s", l) < tam) i = dap_faixa_do_caminho(completo);
        if (i < 0 && snprintf(completo, tam, DAP_RAIZ_USB "%s", l) < tam) i = dap_faixa_do_caminho(completo);
        return i;
    }
    int subir = 0;
    for (;;) {
        if (!strncmp(l, "./", 2)) l += 2;
        else if (!strncmp(l, "../", 3)) { l += 3; subir++; }
        else break;
    }
    int i = -1;
    if (!subir && snprintf(completo, tam, PASTA_PLAYLISTS "/%s", l) < tam) i = dap_faixa_do_caminho(completo);
    /* a pasta das playlists fica na raiz: um ../ ja chega nela */
    if (i < 0 && snprintf(completo, tam, DAP_RAIZ_CARTAO "/%s", l) < tam) i = dap_faixa_do_caminho(completo);
    return i;
}

int dap_faixas_da_playlist(const char *nome, int *saida, int max)
{
    char caminho[DAP_MAX_CAMINHO];
    if (!arquivo_da_playlist(nome, caminho, sizeof(caminho))) return 0;
    FILE *f = fopen(caminho, "r");
    if (!f) return 0;
    int n = 0;
    char linha[DAP_MAX_CAMINHO + 64], completo[DAP_MAX_CAMINHO];
    while (fgets(linha, sizeof(linha), f) && n < max) {
        linha[strcspn(linha, "\r\n")] = 0;
        char *l = linha;
        if ((uint8_t)l[0] == 0xEF && (uint8_t)l[1] == 0xBB && (uint8_t)l[2] == 0xBF) l += 3;   /* BOM do UTF-8 */
        if (!l[0] || l[0] == '#') continue;
        int i = faixa_da_linha(l, completo, sizeof(completo));
        if (i >= 0) saida[n++] = i;
    }
    fclose(f);
    return n;
}

int dap_nova_playlist(char *nome_criado, int tam)
{
    if (!dap_cartao_ok) return 0;
    mkdir(PASTA_PLAYLISTS, 0777);
    for (int k = 1; k < 1000; k++) {
        char nome[24], caminho[DAP_MAX_CAMINHO];
        snprintf(nome, sizeof(nome), "Playlist %d", k);
        if (arquivo_da_playlist(nome, caminho, sizeof(caminho))) continue;
        /* .m3u8: os caminhos vao em UTF-8 */
        snprintf(caminho, sizeof(caminho), PASTA_PLAYLISTS "/%s.m3u8", nome);
        FILE *f = fopen(caminho, "w");
        if (!f) return 0;
        fprintf(f, "#EXTM3U\n");
        fclose(f);
        dap_copiar(nome_criado, tam, nome);
        return 1;
    }
    return 0;
}

/* Um arquivo que nao termina em fim de linha (feito a mao) juntaria a linha nova a ultima. */
static int termina_em_linha(const char *caminho)
{
    FILE *f = fopen(caminho, "rb");
    if (!f) return 1;
    int sim = fseek(f, -1, SEEK_END) != 0 || fgetc(f) == '\n';
    fclose(f);
    return sim;
}

int dap_adicionar_na_playlist(const char *nome, int faixa)
{
    if (faixa < 0 || faixa >= dap_n_faixas) return 0;
    char caminho[DAP_MAX_CAMINHO];
    /* no arquivo que ja existe (pode ser um .m3u8 feito no computador) */
    if (!arquivo_da_playlist(nome, caminho, sizeof(caminho)) &&
        snprintf(caminho, sizeof(caminho), PASTA_PLAYLISTS "/%s.m3u8", nome) >= (int)sizeof(caminho))
        return 0;
    int quebrar = !termina_em_linha(caminho);
    FILE *f = fopen(caminho, "a");
    if (!f) return 0;
    const dap_faixa_t *t = &dap_faixas_tab[faixa];
    const char *c = dap_txt(t->caminho);
    /* do cartao, relativo a pasta da playlist: assim ela abre no computador tambem */
    const char *raiz = DAP_RAIZ_CARTAO "/";
    size_t lr = strlen(raiz);
    int relativo = !strncmp(c, raiz, lr);
    fprintf(f, "%s#EXTINF:%d,%s - %s\n%s%s\n", quebrar ? "\n" : "", (int)(t->duracao_ms / 1000), dap_txt(t->artista),
            dap_txt(t->titulo), relativo ? "../" : "", relativo ? c + lr : c);
    fclose(f);
    return 1;
}
