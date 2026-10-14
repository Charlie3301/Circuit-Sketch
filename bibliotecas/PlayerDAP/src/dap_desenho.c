/* dap_desenho.c -- retangulos, degrades e texto no quadro da tela.
 *
 * O quadro guarda cada pixel na ordem do display (o byte alto primeiro):
 * quem escreve troca os bytes, quem mistura troca de volta. O texto e UTF-8,
 * desenhado com as letras de 4 bits da Noto Sans, misturadas com o fundo.
 */
#include <string.h>
#include "dap_interno.h"

static int rx0 = 0, ry0 = 0, rx1 = DAP_LARGURA, ry1 = DAP_ALTURA;

static inline uint16_t trocar(uint16_t c) { return (uint16_t)((c >> 8) | (c << 8)); }

void dap_recorte(int x, int y, int l, int a)
{
    rx0 = x < 0 ? 0 : x;
    ry0 = y < 0 ? 0 : y;
    rx1 = x + l > DAP_LARGURA ? DAP_LARGURA : x + l;
    ry1 = y + a > DAP_ALTURA ? DAP_ALTURA : y + a;
}

void dap_sem_recorte(void)
{
    rx0 = 0; ry0 = 0; rx1 = DAP_LARGURA; ry1 = DAP_ALTURA;
}

uint16_t dap_misturar(uint16_t fundo, uint16_t frente, int alfa)
{
    if (alfa <= 0) return fundo;
    if (alfa >= 255) return frente;
    int r1 = fundo >> 11, g1 = (fundo >> 5) & 63, b1 = fundo & 31;
    int r2 = frente >> 11, g2 = (frente >> 5) & 63, b2 = frente & 31;
    int r = r1 + ((r2 - r1) * alfa + 127) / 255;
    int g = g1 + ((g2 - g1) * alfa + 127) / 255;
    int b = b1 + ((b2 - b1) * alfa + 127) / 255;
    return (uint16_t)((r << 11) | (g << 5) | b);
}

static inline void ponto(int x, int y, uint16_t cor, int alfa)
{
    if (x < rx0 || x >= rx1 || y < ry0 || y >= ry1) return;
    uint16_t *p = &dap_quadro[y * DAP_LARGURA + x];
    *p = alfa >= 255 ? trocar(cor) : trocar(dap_misturar(trocar(*p), cor, alfa));
}

void dap_ret(int x, int y, int l, int a, uint16_t cor)
{
    int x0 = x < rx0 ? rx0 : x, y0 = y < ry0 ? ry0 : y;
    int x1 = x + l > rx1 ? rx1 : x + l, y1 = y + a > ry1 ? ry1 : y + a;
    if (x0 >= x1 || y0 >= y1) return;
    uint16_t v = trocar(cor);
    for (int yy = y0; yy < y1; yy++) {
        uint16_t *p = &dap_quadro[yy * DAP_LARGURA + x0];
        for (int xx = x0; xx < x1; xx++) *p++ = v;
    }
}

void dap_moldura(int x, int y, int l, int a, uint16_t cor)
{
    dap_ret(x, y, l, 1, cor);
    dap_ret(x, y + a - 1, l, 1, cor);
    dap_ret(x, y, 1, a, cor);
    dap_ret(x + l - 1, y, 1, a, cor);
}

/* Cantos arredondados, com a borda suavizada (cobertura pela distancia ao centro). */
void dap_ret_arredondado(int x, int y, int l, int a, int raio, uint16_t cor)
{
    if (raio * 2 > l) raio = l / 2;
    if (raio * 2 > a) raio = a / 2;
    if (raio <= 0) { dap_ret(x, y, l, a, cor); return; }
    dap_ret(x + raio, y, l - 2 * raio, a, cor);
    dap_ret(x, y + raio, raio, a - 2 * raio, cor);
    dap_ret(x + l - raio, y + raio, raio, a - 2 * raio, cor);
    for (int dy = 0; dy < raio; dy++)
        for (int dx = 0; dx < raio; dx++) {
            /* a distancia do centro do pixel ao centro do canto */
            float cx = raio - dx - 0.5f, cy = raio - dy - 0.5f;
            float d2 = cx * cx + cy * cy, r = (float)raio;
            int alfa;
            if (d2 <= (r - 1) * (r - 1)) alfa = 255;
            else if (d2 >= r * r) alfa = 0;
            else {
                /* entre r-1 e r: aproxima a cobertura pela fracao da distancia */
                float d = 0, lo = r - 1, hi = r;
                d = (d2 - lo * lo) / (hi * hi - lo * lo);
                alfa = (int)((1.0f - d) * 255);
            }
            if (!alfa) continue;
            ponto(x + dx, y + dy, cor, alfa);
            ponto(x + l - 1 - dx, y + dy, cor, alfa);
            ponto(x + dx, y + a - 1 - dy, cor, alfa);
            ponto(x + l - 1 - dx, y + a - 1 - dy, cor, alfa);
        }
}

void dap_degrade_v(int x, int y, int l, int a, uint16_t de, uint16_t para)
{
    for (int k = 0; k < a; k++) dap_ret(x, y + k, l, 1, dap_misturar(de, para, a > 1 ? k * 255 / (a - 1) : 0));
}

/* ------------------------------------------------------------------ texto */

/* O proximo caractere do UTF-8; byte solto (Latin-1 mal gravado) vale ele mesmo. */
static uint32_t proximo(const char **s)
{
    const uint8_t *u = (const uint8_t *)*s;
    uint32_t c = u[0];
    int n = c < 0x80 ? 1 : (c & 0xE0) == 0xC0 ? 2 : (c & 0xF0) == 0xE0 ? 3 : (c & 0xF8) == 0xF0 ? 4 : 0;
    if (n <= 1) { *s += 1; return c; }
    for (int i = 1; i < n; i++)
        if ((u[i] & 0xC0) != 0x80) { *s += 1; return c; }
    c &= 0x3F >> (n - 1);
    for (int i = 1; i < n; i++) c = (c << 6) | (u[i] & 0x3F);
    *s += n;
    return c;
}

static const dap_glifo_t *achar(const dap_fonte_t *f, uint32_t c)
{
    int lo = 0, hi = f->quantos - 1;
    while (lo <= hi) {
        int m = (lo + hi) / 2;
        if (f->glifos[m].codigo == c) return &f->glifos[m];
        if (f->glifos[m].codigo < c) lo = m + 1;
        else hi = m - 1;
    }
    return NULL;
}

static const dap_glifo_t *glifo(const dap_fonte_t *f, uint32_t c)
{
    const dap_glifo_t *g = achar(f, c);
    if (g) return g;
    /* o que a fonte nao tem: aspas e tracos viram os simples, o resto "?" */
    if (c == 0x2018 || c == 0x2019) g = achar(f, '\'');
    else if (c == 0x201C || c == 0x201D) g = achar(f, '"');
    else if (c >= 0x2010 && c <= 0x2015) g = achar(f, '-');
    return g ? g : achar(f, '?');
}

static int desenhar_glifo(const dap_fonte_t *f, const dap_glifo_t *g, int x, int base, uint16_t cor)
{
    if (!g) return 0;
    const uint8_t *d = f->dados + g->pos;
    int x0 = x + g->dx, y0 = base + g->dy;
    /* cada linha do desenho ocupa (larg+1)/2 bytes */
    int por_linha = (g->larg + 1) / 2;
    for (int yy = 0; yy < g->alt; yy++) {
        int y = y0 + yy;
        if (y < ry0 || y >= ry1) continue;
        const uint8_t *linha = d + yy * por_linha;
        for (int xx = 0; xx < g->larg; xx++) {
            int a = (xx & 1) ? (linha[xx >> 1] & 0x0F) : (linha[xx >> 1] >> 4);
            if (a) ponto(x0 + xx, y, cor, a == 15 ? 255 : a * 17);
        }
    }
    return g->avanco;
}

int dap_texto(const dap_fonte_t *f, int x, int y, const char *s, uint16_t cor)
{
    if (!s) return x;
    int base = y + f->ascendente;
    while (*s) {
        uint32_t c = proximo(&s);
        if (c == '\n' || c == '\r' || c == '\t') c = ' ';
        x += desenhar_glifo(f, glifo(f, c), x, base, cor);
    }
    return x;
}

int dap_texto_largura(const dap_fonte_t *f, const char *s)
{
    int l = 0;
    if (!s) return 0;
    while (*s) {
        const dap_glifo_t *g = glifo(f, proximo(&s));
        if (g) l += g->avanco;
    }
    return l;
}

void dap_texto_cortado(const dap_fonte_t *f, int x, int y, int largura, const char *s, uint16_t cor)
{
    if (!s) return;
    if (dap_texto_largura(f, s) <= largura) { dap_texto(f, x, y, s, cor); return; }
    const dap_glifo_t *pontos = glifo(f, 0x2026);
    int reserva = pontos ? pontos->avanco : 0, l = 0, base = y + f->ascendente;
    while (*s) {
        const char *antes = s;
        const dap_glifo_t *g = glifo(f, proximo(&s));
        int av = g ? g->avanco : 0;
        if (l + av + reserva > largura) { s = antes; break; }
        desenhar_glifo(f, g, x + l, base, cor);
        l += av;
    }
    desenhar_glifo(f, pontos, x + l, base, cor);
}

void dap_texto_centro(const dap_fonte_t *f, int cx, int y, int largura, const char *s, uint16_t cor)
{
    int l = dap_texto_largura(f, s);
    if (l > largura) dap_texto_cortado(f, cx - largura / 2, y, largura, s, cor);
    else dap_texto(f, cx - l / 2, y, s, cor);
}

void dap_texto_direita(const dap_fonte_t *f, int x_direita, int y, const char *s, uint16_t cor)
{
    dap_texto(f, x_direita - dap_texto_largura(f, s), y, s, cor);
}

int dap_icone(const dap_fonte_t *f, uint32_t icone, int x, int y, uint16_t cor)
{
    const dap_glifo_t *g = achar(f, icone);
    if (!g) return x;
    return x + desenhar_glifo(f, g, x, y + f->ascendente, cor);
}
