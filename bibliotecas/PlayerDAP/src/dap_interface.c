/* dap_interface.c -- as telas e os controles do PlayerDAP.
 *
 * Uma pilha de telas: o menu, as listas (artistas, albuns, faixas,
 * pastas...), a "Tocando" e os menus de opcoes. A roda anda nas listas; o
 * botao do meio escolhe; VOLTAR volta (segurado: repetir/aleatorio); MODO abre
 * o menu de favoritar, equalizador e modo de musica (segurado: favorita);
 * PAUSA pausa (segurado: para). Na "Tocando", a roda troca de faixa -- ou,
 * depois de um toque no meio, anda 5 s para a frente e para tras.
 *
 * O desenho e por partes: mexer a selecao redesenha duas linhas; o
 * progresso, so a barra; a tela inteira so quando muda de tela.
 */
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <math.h>
#include <sys/stat.h>
#include "freertos/FreeRTOS.h"
#include "freertos/task.h"
#include "esp_timer.h"
#include "dap_interno.h"

volatile int dap_bateria_mv;
volatile int dap_desligando;

/* ----------------------------------------------------------- as telas */

typedef enum { T_MENU, T_VALORES, T_ALBUNS, T_FAIXAS, T_PASTAS, T_TOCANDO, T_OPCOES, T_ESCOLHA } tipo_t;

enum {
    /* o que um item faz no OK */
    A_NADA, A_TOCANDO, A_CATEGORIA, A_CONFIG, A_FAVORITAR, A_ESCOLHER_EQ, A_ESCOLHER_MODO, A_ESCOLHER_PLAYLIST,
    A_IR_ALBUM, A_FILA, A_ATUALIZAR, A_SOBRE, A_ESCOLHER_TELA, A_EQ, A_MODO, A_PLAYLIST, A_NOVA_PLAYLIST, A_TEMPO_TELA,
    A_PASTA_RAIZ,
};

typedef struct {
    uint32_t icone;
    char texto[64];
    char sub[56];
    char direita[16];
    int acao, valor, marcado;
} item_t;

typedef struct {
    tipo_t tipo;
    char titulo[48];
    dap_categoria_t cat;
    uint32_t valor;
    uint32_t *valores;
    int n_valores;
    int *faixas;
    int n_faixas;
    item_t *itens;
    int n_itens;
    char pasta[DAP_MAX_CAMINHO];
    int n_subpastas;
    char **subpastas;           /* um bloco so (dap_subpastas) */
    int sel, topo;
} tela_t;

#define PILHA 12
static tela_t pilha[PILHA];
static int n_pilha;

#define Y_LISTA 56
#define ALTURA_LINHA 38
#define LINHAS_VISIVEIS 7

static int sujo_tudo, sujo_status, sujo_progresso;
static int linha_suja_a = -1, linha_suja_b = -1;
static int roda_avanca;                 /* na "Tocando": a roda busca em vez de trocar */
static int64_t ultima_atividade, aviso_ate, volume_ate;
static char aviso[64];
static int tela_apagada, segundos_para_apagar = 45;
static int64_t ultimo_salvamento;
static int ultimo_pct = -1;

static tela_t *topo(void) { return n_pilha ? &pilha[n_pilha - 1] : NULL; }

static int64_t agora_ms(void) { return esp_timer_get_time() / 1000; }

static void mostrar_aviso(const char *s)
{
    dap_copiar(aviso, (int)sizeof(aviso), s);
    aviso_ate = agora_ms() + 1600;
    sujo_tudo = 1;
}

static void soltar_tela(tela_t *t)
{
    free(t->valores);
    free(t->faixas);
    free(t->itens);
    free(t->subpastas);
    memset(t, 0, sizeof(*t));
}

static tela_t *nova_tela(tipo_t tipo, const char *titulo)
{
    if (n_pilha == PILHA) {
        /* cheia: a mais antiga (depois do menu) sai */
        soltar_tela(&pilha[1]);
        memmove(&pilha[1], &pilha[2], (PILHA - 2) * sizeof(tela_t));
        memset(&pilha[PILHA - 1], 0, sizeof(tela_t));
        n_pilha--;
    }
    tela_t *t = &pilha[n_pilha++];
    memset(t, 0, sizeof(*t));
    t->tipo = tipo;
    dap_copiar(t->titulo, (int)sizeof(t->titulo), titulo);
    sujo_tudo = 1;
    return t;
}

static void voltar_tela(void)
{
    if (n_pilha <= 1) return;
    soltar_tela(&pilha[--n_pilha]);
    sujo_tudo = 1;
}

static int linhas_da_tela(const tela_t *t)
{
    switch (t->tipo) {
    case T_VALORES:
    case T_ALBUNS: return t->n_valores + (t->tipo == T_ALBUNS ? 1 : 0) + t->n_itens;
    case T_FAIXAS: return t->n_faixas;
    case T_PASTAS: return t->n_subpastas + t->n_faixas + t->n_itens;
    default: return t->n_itens;
    }
}

static item_t *novo_item(tela_t *t, uint32_t icone, const char *texto, int acao, int valor)
{
    item_t *n = (item_t *)dap_realocar(t->itens, (size_t)(t->n_itens + 1) * sizeof(item_t));
    if (!n) return NULL;
    t->itens = n;
    item_t *i = &t->itens[t->n_itens++];
    memset(i, 0, sizeof(*i));
    i->icone = icone;
    dap_copiar(i->texto, (int)sizeof(i->texto), texto);
    i->acao = acao;
    i->valor = valor;
    return i;
}

/* ------------------------------------------------------------ textos */

static const char *ou(const char *s, const char *se_vazio) { return s && s[0] ? s : se_vazio; }

static void tempo(char *b, int tam, int ms)
{
    int s = ms / 1000;
    if (s >= 3600) snprintf(b, tam, "%d:%02d:%02d", s / 3600, s / 60 % 60, s % 60);
    else snprintf(b, tam, "%d:%02d", s / 60, s % 60);
}

static const char *nome_da_categoria(dap_categoria_t c)
{
    switch (c) {
    case DAP_CAT_ARTISTAS: return "Artistas";
    case DAP_CAT_ALBUNS: return "Álbuns";
    case DAP_CAT_ARTISTAS_ALBUM: return "Artistas do álbum";
    case DAP_CAT_GENEROS: return "Gêneros";
    case DAP_CAT_ANOS: return "Anos";
    case DAP_CAT_COMPOSITORES: return "Compositores";
    case DAP_CAT_PASTAS: return "Pastas";
    case DAP_CAT_PLAYLISTS: return "Playlists";
    case DAP_CAT_FAVORITAS: return "Favoritas";
    default: return "Todas as músicas";
    }
}

static const char *vazio_da_categoria(dap_categoria_t c)
{
    switch (c) {
    case DAP_CAT_ARTISTAS: case DAP_CAT_ARTISTAS_ALBUM: return "(Artista desconhecido)";
    case DAP_CAT_ALBUNS: return "(Sem álbum)";
    case DAP_CAT_GENEROS: return "(Sem gênero)";
    case DAP_CAT_COMPOSITORES: return "(Sem compositor)";
    default: return "(Sem ano)";
    }
}

static int bateria_pct(void)
{
    int mv = dap_bateria_mv;
    if (mv <= 0) return -1;
    static const int tab[][2] = { {4200, 100}, {4100, 90}, {4000, 79}, {3900, 64}, {3800, 48}, {3750, 38}, {3700, 27},
                                  {3650, 18}, {3600, 11}, {3500, 5}, {3300, 0} };
    if (mv >= 4200) return 100;
    for (int i = 1; i < 11; i++)
        if (mv >= tab[i][0])
            return tab[i][1] + (mv - tab[i][0]) * (tab[i - 1][1] - tab[i][1]) / (tab[i - 1][0] - tab[i][0]);
    return 0;
}

/* --------------------------------------------------------- desenho */

static void desenhar_status(void)
{
    dap_ret(0, 0, DAP_LARGURA, 22, COR_FUNDO2);
    int x = 8;
    /* o estado do som */
    uint32_t estado = dap_est.tocando && !dap_est.pausado ? ICO_TOCAR : dap_est.faixa >= 0 ? ICO_PAUSAR : ICO_PARAR;
    x = dap_icone(&dap_fonte_icones, estado, x, 3, dap_est.tocando && !dap_est.pausado ? COR_DESTAQUE : COR_TEXTO2) + 4;
    /* o modo */
    uint32_t modo = 0;
    switch (dap_est.modo) {
    case DAP_MODO_REPETIR_UMA_VEZ: modo = ICO_REPETIR_UMA_VEZ; break;
    case DAP_MODO_REPETIR_SEMPRE: modo = ICO_REPETIR_UMA; break;
    case DAP_MODO_ALEATORIO:
    case DAP_MODO_ALEATORIO_LISTA: modo = ICO_ALEATORIO; break;
    default: modo = ICO_REPETIR; break;
    }
    x = dap_icone(&dap_fonte_icones, modo, x, 3, COR_TEXTO2) + 4;
    if (dap_est.modo == DAP_MODO_ALEATORIO_LISTA) x = dap_texto(&dap_fonte_pequena, x - 2, 4, "L", COR_TEXTO2) + 4;
    if (dap_est.eq) dap_icone(&dap_fonte_icones, ICO_EQUALIZADOR, x, 3, COR_TEXTO2);

    /* a direita: o armazenamento e a bateria */
    int xd = DAP_LARGURA - 8;
    int pct = bateria_pct();
    char b[12];
    if (pct >= 0) {
        snprintf(b, sizeof(b), "%d%%", pct);
        uint16_t cor = pct <= 15 ? COR_VERMELHO : COR_TEXTO;
        /* a pilha desenhada: corpo, polo e o nivel */
        int bx = xd - 24, by = 6;
        dap_moldura(bx, by, 22, 11, cor);
        dap_ret(bx + 22, by + 3, 2, 5, cor);
        int cheio = 18 * pct / 100;
        if (cheio > 0) dap_ret(bx + 2, by + 2, cheio, 7, pct <= 15 ? COR_VERMELHO : COR_VERDE);
        xd = bx - 4;
        int l = dap_texto_largura(&dap_fonte_pequena, b);
        dap_texto(&dap_fonte_pequena, xd - l, 4, b, COR_TEXTO);
        xd -= l + 6;
    }
    if (dap_usb_ok) xd = dap_icone(&dap_fonte_icones, ICO_USB, xd - 16, 3, COR_TEXTO2) - 16 - 4;
    if (dap_cartao_ok) dap_icone(&dap_fonte_icones, ICO_CARTAO, xd - 16, 3, COR_TEXTO2);
    dap_tela_mostrar(0, 0, DAP_LARGURA, 22);
}

static void desenhar_cabecalho(const tela_t *t)
{
    dap_ret(0, 22, DAP_LARGURA, Y_LISTA - 22, COR_FUNDO);
    int x = 10;
    if (n_pilha > 1) x = dap_icone(&dap_fonte_icones, ICO_VOLTAR, 8, 31, COR_TEXTO2) + 6;
    dap_texto_cortado(&dap_fonte_titulo, x, 26, DAP_LARGURA - x - 10, t->titulo, COR_TEXTO);
    dap_ret(0, Y_LISTA - 1, DAP_LARGURA, 1, COR_BARRA);
}

/* O que vai numa linha de lista (sem desenhar): icone, texto, sub, direita, ativa, apagada. */
typedef struct { uint32_t icone; char texto[96]; char sub[96]; char direita[16]; int destaque, apagada, marcado; } linha_t;

static void contar(dap_categoria_t cat, uint32_t valor, int *faixas, int *albuns)
{
    *faixas = 0;
    *albuns = 0;
    int ultimo_album = -1;
    for (int i = 0; i < dap_n_faixas; i++) {
        const dap_faixa_t *t = &dap_faixas_tab[i];
        uint32_t v = cat == DAP_CAT_ARTISTAS ? t->artista : cat == DAP_CAT_ARTISTAS_ALBUM ? t->artista_album
                   : cat == DAP_CAT_GENEROS ? t->genero : cat == DAP_CAT_COMPOSITORES ? t->compositor : t->ano;
        if (v != valor) continue;
        (*faixas)++;
        if ((int)t->album != ultimo_album) { (*albuns)++; ultimo_album = (int)t->album; }
    }
}

static void linha_da_faixa(int f, linha_t *l)
{
    const dap_faixa_t *t = &dap_faixas_tab[f];
    l->icone = f == dap_est.faixa ? ICO_ONDA : ICO_NOTA;
    snprintf(l->texto, sizeof(l->texto), "%s", ou(dap_txt(t->titulo), "(sem título)"));
    if (t->marcas & DAP_MARCA_ESTRAGADA) {
        snprintf(l->sub, sizeof(l->sub), "%s — este formato não toca", dap_nome_do_formato(t->formato));
        l->apagada = 1;
    } else {
        const char *album = dap_txt(t->album);
        snprintf(l->sub, sizeof(l->sub), "%s%s%s", ou(dap_txt(t->artista), "Artista desconhecido"), album[0] ? " · " : "", album);
    }
    if (t->duracao_ms) tempo(l->direita, sizeof(l->direita), (int)t->duracao_ms);
    l->destaque = f == dap_est.faixa;
    l->marcado = (t->marcas & DAP_MARCA_FAVORITA) != 0;
}

static void montar_linha(const tela_t *t, int i, linha_t *l)
{
    memset(l, 0, sizeof(*l));
    switch (t->tipo) {
    case T_VALORES:
        if (t->cat == DAP_CAT_PLAYLISTS) {
            const item_t *it = &t->itens[i];
            l->icone = it->icone;
            snprintf(l->texto, sizeof(l->texto), "%s", it->texto);
            snprintf(l->sub, sizeof(l->sub), "%s", it->sub);
            break;
        }
        if (t->cat == DAP_CAT_ALBUNS) {
            const dap_faixa_t *rep = &dap_faixas_tab[t->valores[i]];
            l->icone = ICO_ALBUM;
            snprintf(l->texto, sizeof(l->texto), "%s", ou(dap_txt(rep->album), "(Sem álbum)"));
            if (rep->ano) snprintf(l->sub, sizeof(l->sub), "%s · %d", ou(dap_txt(rep->artista_album), "Artista desconhecido"), rep->ano);
            else snprintf(l->sub, sizeof(l->sub), "%s", ou(dap_txt(rep->artista_album), "Artista desconhecido"));
        } else {
            uint32_t v = t->valores[i];
            l->icone = t->cat == DAP_CAT_GENEROS ? ICO_GENERO : t->cat == DAP_CAT_ANOS ? ICO_ANO
                     : t->cat == DAP_CAT_COMPOSITORES ? ICO_COMPOSITOR : ICO_ARTISTA;
            if (t->cat == DAP_CAT_ANOS) {
                if (v) snprintf(l->texto, sizeof(l->texto), "%u", (unsigned)v);
                else snprintf(l->texto, sizeof(l->texto), "%s", vazio_da_categoria(t->cat));
            } else snprintf(l->texto, sizeof(l->texto), "%s", ou(dap_txt(v), vazio_da_categoria(t->cat)));
            int nf, na;
            contar(t->cat, v, &nf, &na);
            if (t->cat == DAP_CAT_ARTISTAS || t->cat == DAP_CAT_ARTISTAS_ALBUM)
                snprintf(l->sub, sizeof(l->sub), "%d %s · %d %s", na, na == 1 ? "álbum" : "álbuns", nf, nf == 1 ? "música" : "músicas");
            else snprintf(l->sub, sizeof(l->sub), "%d %s", nf, nf == 1 ? "música" : "músicas");
        }
        break;
    case T_ALBUNS:
        if (i == 0) {
            l->icone = ICO_BIBLIOTECA;
            snprintf(l->texto, sizeof(l->texto), "Todas as músicas");
            snprintf(l->sub, sizeof(l->sub), "de %s", ou(dap_txt(t->valor), "Artista desconhecido"));
        } else {
            const dap_faixa_t *rep = &dap_faixas_tab[t->valores[i - 1]];
            l->icone = ICO_ALBUM;
            snprintf(l->texto, sizeof(l->texto), "%s", ou(dap_txt(rep->album), "(Sem álbum)"));
            if (rep->ano) snprintf(l->sub, sizeof(l->sub), "%d", rep->ano);
        }
        break;
    case T_FAIXAS:
        linha_da_faixa(t->faixas[i], l);
        break;
    case T_PASTAS:
        if (i < t->n_itens) {
            const item_t *it = &t->itens[i];
            l->icone = it->icone;
            snprintf(l->texto, sizeof(l->texto), "%s", it->texto);
            snprintf(l->sub, sizeof(l->sub), "%s", it->sub);
        } else if (i < t->n_itens + t->n_subpastas) {
            l->icone = ICO_PASTA;
            snprintf(l->texto, sizeof(l->texto), "%s", t->subpastas[i - t->n_itens]);
        } else {
            linha_da_faixa(t->faixas[i - t->n_itens - t->n_subpastas], l);
            /* na pasta, o nome do arquivo diz mais que o titulo */
            const char *nome = strrchr(dap_txt(dap_faixas_tab[t->faixas[i - t->n_itens - t->n_subpastas]].caminho), '/');
            if (nome) snprintf(l->texto, sizeof(l->texto), "%s", nome + 1);
        }
        break;
    default: {
        const item_t *it = &t->itens[i];
        l->icone = it->icone;
        snprintf(l->texto, sizeof(l->texto), "%s", it->texto);
        snprintf(l->sub, sizeof(l->sub), "%s", it->sub);
        snprintf(l->direita, sizeof(l->direita), "%s", it->direita);
        l->marcado = it->marcado;
        l->apagada = it->acao == A_NADA && t->tipo == T_OPCOES;
        break;
    }
    }
}

static void desenhar_linha(const tela_t *t, int i)
{
    int r = i - t->topo;
    if (r < 0 || r >= LINHAS_VISIVEIS || i >= linhas_da_tela(t)) return;
    int y = Y_LISTA + r * ALTURA_LINHA;
    int sel = i == t->sel;
    dap_ret(0, y, DAP_LARGURA, ALTURA_LINHA, sel ? COR_SELECAO : COR_FUNDO);
    if (sel) dap_ret(0, y + 4, 3, ALTURA_LINHA - 8, COR_DESTAQUE);
    linha_t l;
    montar_linha(t, i, &l);
    uint16_t cor = l.apagada ? COR_TEXTO3 : l.destaque ? COR_DESTAQUE : COR_TEXTO;
    if (l.icone) dap_icone(&dap_fonte_icones, l.icone, 12, y + 11, l.destaque ? COR_DESTAQUE : sel ? COR_DESTAQUE2 : COR_TEXTO2);
    int direita = DAP_LARGURA - 10;
    if (l.marcado) {
        uint32_t ic = t->tipo == T_ESCOLHA ? ICO_CERTO : ICO_FAVORITA;
        dap_icone(&dap_fonte_icones, ic, direita - 16, y + 11, ic == ICO_FAVORITA ? COR_VERMELHO : COR_DESTAQUE);
        direita -= 20;
    }
    if (l.direita[0]) {
        int lw = dap_texto_largura(&dap_fonte_pequena, l.direita);
        dap_texto(&dap_fonte_pequena, direita - lw, y + 12, l.direita, COR_TEXTO3);
        direita -= lw + 6;
    }
    int x = l.icone ? 36 : 14;
    if (l.sub[0]) {
        dap_texto_cortado(sel ? &dap_fonte_forte : &dap_fonte_normal, x, y + 2, direita - x, l.texto, cor);
        dap_texto_cortado(&dap_fonte_pequena, x, y + 20, direita - x, l.sub, l.apagada ? COR_TEXTO3 : COR_TEXTO2);
    } else {
        dap_texto_cortado(sel ? &dap_fonte_forte : &dap_fonte_normal, x, y + 9, direita - x, l.texto, cor);
    }
    dap_ret(36, y + ALTURA_LINHA - 1, DAP_LARGURA - 36, 1, COR_FUNDO2);
    dap_tela_mostrar(0, y, DAP_LARGURA, ALTURA_LINHA);
}

static void desenhar_lista(const tela_t *t)
{
    int n = linhas_da_tela(t);
    dap_ret(0, Y_LISTA, DAP_LARGURA, DAP_ALTURA - Y_LISTA, COR_FUNDO);
    if (n == 0) {
        dap_icone(&dap_fonte_icones_grandes, t->tipo == T_PASTAS ? ICO_PASTA : ICO_NOTA, 96, 130, COR_TEXTO3);
        dap_texto_centro(&dap_fonte_normal, 120, 190, 220, "Nada por aqui", COR_TEXTO2);
        dap_tela_mostrar(0, Y_LISTA, DAP_LARGURA, DAP_ALTURA - Y_LISTA);
        return;
    }
    /* as linhas, uma a uma (cada uma ja manda o seu pedaco) */
    for (int i = t->topo; i < t->topo + LINHAS_VISIVEIS && i < n; i++) desenhar_linha(t, i);
    /* a barra de rolagem */
    if (n > LINHAS_VISIVEIS) {
        int h = DAP_ALTURA - Y_LISTA - 8;
        int bh = h * LINHAS_VISIVEIS / n;
        if (bh < 16) bh = 16;
        int by = Y_LISTA + 4 + (h - bh) * t->topo / (n - LINHAS_VISIVEIS);
        dap_ret(DAP_LARGURA - 3, Y_LISTA, 3, DAP_ALTURA - Y_LISTA, COR_FUNDO);
        dap_ret_arredondado(DAP_LARGURA - 3, by, 3, bh, 1, COR_TEXTO3);
        dap_tela_mostrar(DAP_LARGURA - 3, Y_LISTA, 3, DAP_ALTURA - Y_LISTA);
    }
    int sobra = Y_LISTA + (n - t->topo < LINHAS_VISIVEIS ? n - t->topo : LINHAS_VISIVEIS) * ALTURA_LINHA;
    if (sobra < DAP_ALTURA) dap_tela_mostrar(0, sobra, DAP_LARGURA, DAP_ALTURA - sobra);
}

/* A "capa": sem a arte do album, um degrade com a cor do nome dele. */
static void desenhar_capa(int x, int y, int lado, const dap_faixa_t *t)
{
    uint32_t h = 2166136261u;
    for (const char *s = t ? dap_txt(t->album) : ""; *s; s++) h = (h ^ (uint8_t)*s) * 16777619u;
    if (t && !dap_txt(t->album)[0]) for (const char *s = dap_txt(t->artista); *s; s++) h = (h ^ (uint8_t)*s) * 16777619u;
    static const uint16_t paleta[] = {
        RGB(255, 122, 69), RGB(84, 160, 255), RGB(170, 110, 255), RGB(60, 200, 160), RGB(255, 90, 140), RGB(240, 190, 60),
    };
    uint16_t a = paleta[h % 6], b = paleta[(h / 7 + 2) % 6];
    /* o degrade de cima para baixo, com os cantos arredondados (raio 12) */
    const int raio = 12;
    for (int k = 0; k < lado; k++) {
        uint16_t cor = dap_misturar(dap_misturar(a, b, k * 255 / lado), COR_FUNDO, 90);
        int dentro = 0;
        if (k < raio) dentro = raio - (int)sqrtf((float)(raio * raio - (raio - k) * (raio - k)) + 0.5f);
        else if (k >= lado - raio) dentro = raio - (int)sqrtf((float)(raio * raio - (k - (lado - raio - 1)) * (k - (lado - raio - 1))) + 0.5f);
        dap_ret(x + dentro, y + k, lado - 2 * dentro, 1, cor);
    }
    dap_icone(&dap_fonte_icones_grandes, ICO_NOTA, x + lado / 2 - 24, y + lado / 2 - 24, dap_misturar(COR_TEXTO, a, 60));
}

static void desenhar_progresso(void)
{
    int y = 262;
    dap_ret(0, y, DAP_LARGURA, 30, COR_FUNDO);
    int pos = dap_posicao_atual_ms(), dur = dap_duracao_atual_ms();
    int x0 = 20, l = DAP_LARGURA - 40;
    dap_ret_arredondado(x0, y + 4, l, 4, 2, COR_BARRA);
    int feito = dur > 0 ? (int)((int64_t)l * (pos > dur ? dur : pos) / dur) : 0;
    if (feito > 0) dap_ret_arredondado(x0, y + 4, feito, 4, 2, roda_avanca ? COR_DESTAQUE2 : COR_DESTAQUE);
    dap_ret_arredondado(x0 + feito - 5, y + 1, 10, 10, 5, COR_TEXTO);
    char a[16], b[16];
    tempo(a, sizeof(a), pos);
    b[0] = '-';
    tempo(b + 1, sizeof(b) - 1, dur > pos ? dur - pos : 0);
    dap_texto(&dap_fonte_pequena, x0, y + 13, a, COR_TEXTO2);
    dap_texto_direita(&dap_fonte_pequena, x0 + l, y + 13, dur ? b : "", COR_TEXTO2);
    if (roda_avanca) dap_texto_centro(&dap_fonte_pequena, 120, y + 13, 100, "roda: avançar", COR_DESTAQUE2);
    dap_tela_mostrar(0, y, DAP_LARGURA, 30);
}

static void desenhar_tocando(void)
{
    dap_ret(0, 22, DAP_LARGURA, DAP_ALTURA - 22, COR_FUNDO);
    int f = dap_est.faixa;
    const dap_faixa_t *t = f >= 0 && f < dap_n_faixas ? &dap_faixas_tab[f] : NULL;
    desenhar_capa(44, 30, 152, t);
    if (!t) {
        dap_texto_centro(&dap_fonte_titulo, 120, 196, 220, "Nada tocando", COR_TEXTO);
        dap_texto_centro(&dap_fonte_pequena, 120, 224, 220, "Escolha uma música no menu", COR_TEXTO2);
        dap_tela_mostrar(0, 22, DAP_LARGURA, DAP_ALTURA - 22);
        return;
    }
    dap_texto_centro(&dap_fonte_titulo, 120, 190, 224, ou(dap_txt(t->titulo), "(sem título)"), COR_TEXTO);
    dap_texto_centro(&dap_fonte_normal, 120, 216, 224, ou(dap_txt(t->artista), "Artista desconhecido"), COR_TEXTO2);
    dap_texto_centro(&dap_fonte_pequena, 120, 238, 224, ou(dap_txt(t->album), ""), COR_TEXTO3);
    desenhar_progresso();

    /* embaixo: o formato, o equalizador, a favorita */
    int y = 296;
    char fmt[40];
    const dap_info_audio_t *in = &dap_est.info;
    /* a taxa com virgula, como se escreve aqui: 44,1 kHz */
    int khz = in->taxa / 1000, decimo = in->taxa % 1000 / 100;
    if (in->bits == 1) snprintf(fmt, sizeof(fmt), "%s DSD%d", dap_nome_do_formato(in->formato), in->taxa >= 176400 ? 128 : 64);
    else if (in->formato == DAP_FMT_MP3 || in->formato == DAP_FMT_OGG || in->formato == DAP_FMT_OPUS)
        snprintf(fmt, sizeof(fmt), "%s %dk · %d,%d kHz", dap_nome_do_formato(in->formato), in->kbps, khz, decimo);
    else snprintf(fmt, sizeof(fmt), "%s %d bits · %d,%d kHz", dap_nome_do_formato(in->formato), in->bits, khz, decimo);
    if (!in->taxa) snprintf(fmt, sizeof(fmt), "%s", dap_nome_do_formato(t->formato));
    dap_ret_arredondado(12, y - 2, dap_texto_largura(&dap_fonte_pequena, fmt) + 12, 18, 9, COR_FUNDO2);
    dap_texto(&dap_fonte_pequena, 18, y, fmt, COR_DESTAQUE2);
    int xd = DAP_LARGURA - 12;
    xd = dap_icone(&dap_fonte_icones, (t->marcas & DAP_MARCA_FAVORITA) ? ICO_FAVORITA : ICO_NAO_FAVORITA, xd - 16, y, (t->marcas & DAP_MARCA_FAVORITA) ? COR_VERMELHO : COR_TEXTO3) - 16 - 8;
    if (dap_est.eq) {
        const char *eq = dap_presets_eq[dap_est.eq].nome;
        int l = dap_texto_largura(&dap_fonte_pequena, eq);
        dap_texto(&dap_fonte_pequena, xd - l, y, eq, COR_TEXTO2);
        dap_icone(&dap_fonte_icones, ICO_EQUALIZADOR, xd - l - 18, y, COR_TEXTO2);
    }
    if (dap_est.erro[0]) dap_texto_centro(&dap_fonte_pequena, 120, 252, 224, dap_est.erro, COR_VERMELHO);
    dap_tela_mostrar(0, 22, DAP_LARGURA, DAP_ALTURA - 22);
}

static void desenhar_sobreposicoes(void)
{
    int64_t agora = agora_ms();
    if (volume_ate > agora) {
        int x = 30, y = 120, l = 180, a = 64;
        dap_ret_arredondado(x, y, l, a, 14, COR_BARRA);
        dap_icone(&dap_fonte_icones_medios, dap_est.volume ? ICO_VOLUME : ICO_MUDO, x + 14, y + 20, COR_TEXTO);
        char v[8];
        snprintf(v, sizeof(v), "%d", dap_est.volume);
        dap_texto_direita(&dap_fonte_titulo, x + l - 14, y + 18, v, COR_TEXTO);
        int bx = x + 48, bl = l - 48 - 44;
        dap_ret_arredondado(bx, y + 30, bl, 6, 3, COR_FUNDO);
        if (dap_est.volume) dap_ret_arredondado(bx, y + 30, bl * dap_est.volume / 30, 6, 3, COR_DESTAQUE);
        dap_tela_mostrar(x, y, l, a);
    }
    if (aviso_ate > agora && aviso[0]) {
        int l = dap_texto_largura(&dap_fonte_normal, aviso) + 28;
        if (l > 224) l = 224;
        int x = 120 - l / 2, y = 268;
        dap_ret_arredondado(x, y, l, 30, 15, COR_BARRA);
        dap_texto_centro(&dap_fonte_normal, 120, y + 5, l - 20, aviso, COR_TEXTO);
        dap_tela_mostrar(x, y, l, 30);
    }
}

static void redesenhar(void)
{
    tela_t *t = topo();
    if (!t) return;
    desenhar_status();
    if (t->tipo == T_TOCANDO) desenhar_tocando();
    else {
        desenhar_cabecalho(t);
        dap_tela_mostrar(0, 22, DAP_LARGURA, Y_LISTA - 22);
        desenhar_lista(t);
    }
    desenhar_sobreposicoes();
    sujo_tudo = sujo_status = sujo_progresso = 0;
    linha_suja_a = linha_suja_b = -1;
}

/* ------------------------------------------------- abrir as telas */

static void abrir_menu_principal(void);
static void abrir_tocando(void);

static void abrir_categoria(dap_categoria_t cat)
{
    if (cat == DAP_CAT_TODAS || cat == DAP_CAT_FAVORITAS) {
        tela_t *t = nova_tela(T_FAIXAS, nome_da_categoria(cat));
        t->cat = cat;
        t->faixas = (int *)dap_alocar((size_t)(dap_n_faixas + 1) * sizeof(int));
        if (t->faixas) t->n_faixas = dap_faixas_de(cat, 0, t->faixas, dap_n_faixas);
        return;
    }
    if (cat == DAP_CAT_PASTAS) {
        tela_t *t = nova_tela(T_PASTAS, "Pastas");
        t->cat = cat;
        if (dap_cartao_ok) { item_t *i = novo_item(t, ICO_CARTAO, "Cartão SD", A_PASTA_RAIZ, 0); if (i) snprintf(i->sub, sizeof(i->sub), "%s", DAP_RAIZ_CARTAO); }
        if (dap_usb_ok) { item_t *i = novo_item(t, ICO_USB, "Pendrive USB", A_PASTA_RAIZ, 1); if (i) snprintf(i->sub, sizeof(i->sub), "%s", DAP_RAIZ_USB); }
        return;
    }
    if (cat == DAP_CAT_PLAYLISTS) {
        tela_t *t = nova_tela(T_VALORES, "Playlists");
        t->cat = cat;
        char (*nomes)[DAP_MAX_NOME_PLAYLIST] = (char (*)[DAP_MAX_NOME_PLAYLIST])dap_alocar(64 * DAP_MAX_NOME_PLAYLIST);
        novo_item(t, ICO_NOVA, "Nova playlist", A_NOVA_PLAYLIST, 0);
        int n = nomes ? dap_playlists(nomes, 64) : 0;
        for (int i = 0; i < n; i++) {
            item_t *it = novo_item(t, ICO_PLAYLIST, nomes[i], A_PLAYLIST, i);
            int *f = (int *)dap_alocar(DAP_MAX_FAIXAS * sizeof(int));
            if (it && f) {
                int q = dap_faixas_da_playlist(nomes[i], f, DAP_MAX_FAIXAS);
                snprintf(it->sub, sizeof(it->sub), "%d %s", q, q == 1 ? "música" : "músicas");
            }
            free(f);
        }
        free(nomes);
        return;
    }
    tela_t *t = nova_tela(T_VALORES, nome_da_categoria(cat));
    t->cat = cat;
    t->valores = (uint32_t *)dap_alocar((size_t)(dap_n_faixas + 1) * sizeof(uint32_t));
    if (t->valores) t->n_valores = dap_valores(cat, t->valores, dap_n_faixas);
}

static void abrir_pasta(const char *pasta, const char *titulo)
{
    tela_t *t = nova_tela(T_PASTAS, titulo);
    t->cat = DAP_CAT_PASTAS;
    snprintf(t->pasta, sizeof(t->pasta), "%s", pasta);
    t->n_subpastas = dap_subpastas(pasta, &t->subpastas);
    t->faixas = (int *)dap_alocar((size_t)(dap_n_faixas + 1) * sizeof(int));
    if (t->faixas) t->n_faixas = dap_faixas_da_pasta(pasta, t->faixas, dap_n_faixas);
}

static void abrir_faixas(const char *titulo, dap_categoria_t cat, uint32_t valor)
{
    tela_t *t = nova_tela(T_FAIXAS, titulo);
    t->cat = cat;
    t->valor = valor;
    t->faixas = (int *)dap_alocar((size_t)(dap_n_faixas + 1) * sizeof(int));
    if (t->faixas) t->n_faixas = dap_faixas_de(cat, valor, t->faixas, dap_n_faixas);
}

static void abrir_fila(void)
{
    tela_t *t = nova_tela(T_FAIXAS, dap_est.nome_fila[0] ? dap_est.nome_fila : "Fila de reprodução");
    DAP_TRAVAR();
    if (dap_est.fila && dap_est.n_fila > 0) {
        t->faixas = (int *)dap_alocar((size_t)dap_est.n_fila * sizeof(int));
        if (t->faixas) {
            memcpy(t->faixas, dap_est.fila, (size_t)dap_est.n_fila * sizeof(int));
            t->n_faixas = dap_est.n_fila;
            t->sel = dap_est.pos_fila;
        }
    }
    DAP_SOLTAR();
    t->cat = DAP_CAT_TODAS;
    t->valor = 0xFFFFFFFF;   /* e a fila */
    if (t->sel >= LINHAS_VISIVEIS) t->topo = t->sel - LINHAS_VISIVEIS / 2;
}

static void abrir_escolha_eq(void)
{
    tela_t *t = nova_tela(T_ESCOLHA, "Equalizador");
    for (int i = 0; i < DAP_PRESETS_EQ; i++) {
        item_t *it = novo_item(t, ICO_EQUALIZADOR, dap_presets_eq[i].nome, A_EQ, i);
        if (!it) continue;
        it->marcado = i == dap_est.eq;
        const int8_t *g = dap_presets_eq[i].ganho_db;
        snprintf(it->sub, sizeof(it->sub), "%+d %+d %+d %+d %+d dB", g[0], g[1], g[2], g[3], g[4]);
    }
    t->sel = dap_est.eq;
}

static void abrir_escolha_modo(void)
{
    static const uint32_t icones[DAP_MODOS] = { ICO_REPETIR, ICO_REPETIR_UMA_VEZ, ICO_REPETIR_UMA, ICO_ALEATORIO, ICO_ALEATORIO };
    static const char *const subs[DAP_MODOS] = {
        "A lista toda, e volta ao começo", "A música mais uma vez, e segue", "A mesma música, sem parar",
        "Qualquer música da biblioteca", "Qualquer uma da lista que está tocando",
    };
    tela_t *t = nova_tela(T_ESCOLHA, "Modo de música");
    for (int i = 0; i < DAP_MODOS; i++) {
        item_t *it = novo_item(t, icones[i], dap_nomes_dos_modos[i], A_MODO, i);
        if (!it) continue;
        it->marcado = i == (int)dap_est.modo;
        snprintf(it->sub, sizeof(it->sub), "%s", subs[i]);
    }
    t->sel = (int)dap_est.modo;
}

static void abrir_escolha_playlist(void)
{
    tela_t *t = nova_tela(T_ESCOLHA, "Adicionar à playlist");
    novo_item(t, ICO_NOVA, "Nova playlist", A_NOVA_PLAYLIST, -1);
    char (*nomes)[DAP_MAX_NOME_PLAYLIST] = (char (*)[DAP_MAX_NOME_PLAYLIST])dap_alocar(64 * DAP_MAX_NOME_PLAYLIST);
    int n = nomes ? dap_playlists(nomes, 64) : 0;
    for (int i = 0; i < n; i++) novo_item(t, ICO_PLAYLIST, nomes[i], A_PLAYLIST, i);
    free(nomes);
}

static void abrir_menu_modo(void)
{
    tela_t *t = nova_tela(T_OPCOES, "Modo");
    int f = dap_est.faixa;
    if (f >= 0 && f < dap_n_faixas) {
        int fav = (dap_faixas_tab[f].marcas & DAP_MARCA_FAVORITA) != 0;
        item_t *i = novo_item(t, fav ? ICO_FAVORITA : ICO_NAO_FAVORITA, fav ? "Tirar das favoritas" : "Favoritar música", A_FAVORITAR, 0);
        if (i) snprintf(i->sub, sizeof(i->sub), "%s", ou(dap_txt(dap_faixas_tab[f].titulo), ""));
    }
    item_t *eq = novo_item(t, ICO_EQUALIZADOR, "Equalizador", A_ESCOLHER_EQ, 0);
    if (eq) snprintf(eq->sub, sizeof(eq->sub), "%s", dap_presets_eq[dap_est.eq].nome);
    item_t *m = novo_item(t, ICO_REPETIR, "Modo de música", A_ESCOLHER_MODO, 0);
    if (m) snprintf(m->sub, sizeof(m->sub), "%s", dap_nomes_dos_modos[dap_est.modo]);
    if (f >= 0) {
        novo_item(t, ICO_ADICIONAR_LISTA, "Adicionar à playlist", A_ESCOLHER_PLAYLIST, 0);
        item_t *a = novo_item(t, ICO_ALBUM, "Ir para o álbum", A_IR_ALBUM, 0);
        if (a) snprintf(a->sub, sizeof(a->sub), "%s", ou(dap_txt(dap_faixas_tab[f].album), "(Sem álbum)"));
        novo_item(t, ICO_PLAYLIST, "Fila de reprodução", A_FILA, 0);
    }
}

static void abrir_configuracoes(void)
{
    tela_t *t = nova_tela(T_OPCOES, "Configurações");
    item_t *i = novo_item(t, ICO_EQUALIZADOR, "Equalizador", A_ESCOLHER_EQ, 0);
    if (i) snprintf(i->sub, sizeof(i->sub), "%s", dap_presets_eq[dap_est.eq].nome);
    i = novo_item(t, ICO_REPETIR, "Modo de música", A_ESCOLHER_MODO, 0);
    if (i) snprintf(i->sub, sizeof(i->sub), "%s", dap_nomes_dos_modos[dap_est.modo]);
    i = novo_item(t, ICO_INFO, "A tela apaga em", A_ESCOLHER_TELA, 0);
    if (i) {
        if (segundos_para_apagar) snprintf(i->sub, sizeof(i->sub), "%d segundos sem mexer", segundos_para_apagar);
        else snprintf(i->sub, sizeof(i->sub), "nunca");
    }
    novo_item(t, ICO_ATUALIZAR, "Atualizar a biblioteca", A_ATUALIZAR, 0);
    novo_item(t, ICO_INFO, "Sobre o player", A_SOBRE, 0);
}

static void abrir_sobre(void)
{
    tela_t *t = nova_tela(T_OPCOES, "Sobre");
    char b[64];
    item_t *i = novo_item(t, ICO_FONE, "PlayerDAP para ESP32-S3", A_NADA, 0);
    if (i) snprintf(i->sub, sizeof(i->sub), "CircuitSketch");
    snprintf(b, sizeof(b), "%d músicas na biblioteca", dap_n_faixas);
    novo_item(t, ICO_BIBLIOTECA, b, A_NADA, 0);
    i = novo_item(t, ICO_CARTAO, dap_cartao_ok ? "Cartão SD: lido" : "Cartão SD: não achado", A_NADA, 0);
    i = novo_item(t, ICO_USB, dap_usb_ok ? "Pendrive: lido" : "Pendrive: não conectado", A_NADA, 0);
    i = novo_item(t, ICO_NOTA, "MP3 · FLAC · WAV · AIFF", A_NADA, 0);
    if (i) snprintf(i->sub, sizeof(i->sub), "OGG Vorbis · DSD (DSF e DFF)");
    int pct = bateria_pct();
    if (pct >= 0) {
        snprintf(b, sizeof(b), "Bateria: %d%% (%d,%02d V)", pct, dap_bateria_mv / 1000, dap_bateria_mv % 1000 / 10);
        novo_item(t, ICO_BATERIA, b, A_NADA, 0);
    }
}

static void abrir_escolha_tela(void)
{
    static const int opcoes[] = { 15, 30, 45, 60, 120, 0 };
    tela_t *t = nova_tela(T_ESCOLHA, "A tela apaga em");
    for (int i = 0; i < 6; i++) {
        char b[32];
        if (opcoes[i]) snprintf(b, sizeof(b), "%d segundos", opcoes[i]);
        else snprintf(b, sizeof(b), "Nunca");
        item_t *it = novo_item(t, ICO_INFO, b, A_TEMPO_TELA, opcoes[i]);
        if (it) it->marcado = opcoes[i] == segundos_para_apagar;
        if (opcoes[i] == segundos_para_apagar) t->sel = i;
    }
}

static void abrir_menu_principal(void)
{
    tela_t *t = nova_tela(T_MENU, "Biblioteca");
    item_t *i = novo_item(t, ICO_FONE, "Tocando agora", A_TOCANDO, 0);
    if (i && dap_est.faixa >= 0 && dap_est.faixa < dap_n_faixas) dap_copiar(i->sub, (int)sizeof(i->sub), dap_txt(dap_faixas_tab[dap_est.faixa].titulo));
    i = novo_item(t, ICO_BIBLIOTECA, "Todas as músicas", A_CATEGORIA, DAP_CAT_TODAS);
    if (i) snprintf(i->direita, sizeof(i->direita), "%d", dap_n_faixas);
    novo_item(t, ICO_ARTISTA, "Artistas", A_CATEGORIA, DAP_CAT_ARTISTAS);
    novo_item(t, ICO_ALBUM, "Álbuns", A_CATEGORIA, DAP_CAT_ALBUNS);
    novo_item(t, ICO_ARTISTA, "Artistas do álbum", A_CATEGORIA, DAP_CAT_ARTISTAS_ALBUM);
    novo_item(t, ICO_GENERO, "Gêneros", A_CATEGORIA, DAP_CAT_GENEROS);
    novo_item(t, ICO_ANO, "Anos", A_CATEGORIA, DAP_CAT_ANOS);
    novo_item(t, ICO_COMPOSITOR, "Compositores", A_CATEGORIA, DAP_CAT_COMPOSITORES);
    novo_item(t, ICO_PASTA, "Pastas", A_CATEGORIA, DAP_CAT_PASTAS);
    novo_item(t, ICO_PLAYLIST, "Playlists", A_CATEGORIA, DAP_CAT_PLAYLISTS);
    novo_item(t, ICO_FAVORITA, "Favoritas", A_CATEGORIA, DAP_CAT_FAVORITAS);
    novo_item(t, ICO_CONFIGURAR, "Configurações", A_CONFIG, 0);
}

static void abrir_tocando(void)
{
    /* nao empilha duas "Tocando" */
    if (topo() && topo()->tipo == T_TOCANDO) { sujo_tudo = 1; return; }
    nova_tela(T_TOCANDO, "Tocando");
    roda_avanca = 0;
}

/* ---------------------------------------------------------- acoes */

static void tocar_lista(const tela_t *t, int indice_na_lista)
{
    if (!t->faixas || t->n_faixas <= 0) return;
    char nome[64];
    if (t->valor == 0xFFFFFFFF) snprintf(nome, sizeof(nome), "%s", dap_est.nome_fila);
    else snprintf(nome, sizeof(nome), "%s", t->titulo);
    dap_tocar_fila(t->faixas, t->n_faixas, indice_na_lista, nome);
    abrir_tocando();
}

static void favoritar_atual(void)
{
    int f = dap_est.faixa;
    if (f < 0 || f >= dap_n_faixas) return;
    int sim = !(dap_faixas_tab[f].marcas & DAP_MARCA_FAVORITA);
    dap_favoritar(f, sim);
    mostrar_aviso(sim ? "Favoritada" : "Tirada das favoritas");
}

static void ciclar_modo(void)
{
    static const dap_modo_t ordem[] = { DAP_MODO_TODAS, DAP_MODO_REPETIR_UMA_VEZ, DAP_MODO_REPETIR_SEMPRE,
                                        DAP_MODO_ALEATORIO, DAP_MODO_ALEATORIO_LISTA };
    int i = 0;
    while (i < 5 && ordem[i] != dap_est.modo) i++;
    dap_est.modo = ordem[(i + 1) % 5];
    dap_est.repetiu = 0;
    mostrar_aviso(dap_nomes_dos_modos[dap_est.modo]);
    sujo_status = 1;
}

static void atualizar_biblioteca(void);

static void ativar(tela_t *t)
{
    int i = t->sel;
    if (i < 0 || i >= linhas_da_tela(t)) return;
    switch (t->tipo) {
    case T_VALORES:
        if (t->cat == DAP_CAT_PLAYLISTS) {
            item_t *it = &t->itens[i];
            if (it->acao == A_NOVA_PLAYLIST) {
                char nome[48];
                if (dap_nova_playlist(nome, sizeof(nome))) {
                    char aviso_b[64];
                    snprintf(aviso_b, sizeof(aviso_b), "Criada: %s", nome);
                    voltar_tela();
                    abrir_categoria(DAP_CAT_PLAYLISTS);
                    mostrar_aviso(aviso_b);
                } else mostrar_aviso("Sem cartão para gravar");
                return;
            }
            tela_t *f = nova_tela(T_FAIXAS, it->texto);
            f->cat = DAP_CAT_PLAYLISTS;
            f->faixas = (int *)dap_alocar(DAP_MAX_FAIXAS * sizeof(int));
            if (f->faixas) f->n_faixas = dap_faixas_da_playlist(it->texto, f->faixas, DAP_MAX_FAIXAS);
            return;
        }
        if (t->cat == DAP_CAT_ARTISTAS || t->cat == DAP_CAT_ARTISTAS_ALBUM) {
            uint32_t artista = t->valores[i];
            dap_categoria_t cat = t->cat;
            tela_t *a = nova_tela(T_ALBUNS, ou(dap_txt(artista), "Artista desconhecido"));
            a->cat = cat;
            a->valor = artista;
            a->valores = (uint32_t *)dap_alocar((size_t)(dap_n_faixas + 1) * sizeof(uint32_t));
            if (a->valores) a->n_valores = dap_albuns_de(cat, artista, a->valores, dap_n_faixas);
            return;
        }
        if (t->cat == DAP_CAT_ALBUNS) {
            uint32_t rep = t->valores[i];
            abrir_faixas(ou(dap_txt(dap_faixas_tab[rep].album), "(Sem álbum)"), DAP_CAT_ALBUNS, rep);
            return;
        }
        {
            char titulo[48];
            if (t->cat == DAP_CAT_ANOS) {
                if (t->valores[i]) snprintf(titulo, sizeof(titulo), "%u", (unsigned)t->valores[i]);
                else snprintf(titulo, sizeof(titulo), "%s", vazio_da_categoria(t->cat));
            } else snprintf(titulo, sizeof(titulo), "%s", ou(dap_txt(t->valores[i]), vazio_da_categoria(t->cat)));
            abrir_faixas(titulo, t->cat, t->valores[i]);
        }
        return;
    case T_ALBUNS: {
        dap_categoria_t cat = t->cat;
        uint32_t artista = t->valor;
        if (i == 0) {
            char titulo[48];
            snprintf(titulo, sizeof(titulo), "%s", ou(dap_txt(artista), "Artista desconhecido"));
            abrir_faixas(titulo, cat, artista);
        } else {
            uint32_t rep = t->valores[i - 1];
            tela_t *f = nova_tela(T_FAIXAS, ou(dap_txt(dap_faixas_tab[rep].album), "(Sem álbum)"));
            f->cat = DAP_CAT_ALBUNS;
            f->faixas = (int *)dap_alocar((size_t)(dap_n_faixas + 1) * sizeof(int));
            if (f->faixas) f->n_faixas = dap_faixas_do_album_de(cat, artista, rep, f->faixas, dap_n_faixas);
        }
        return;
    }
    case T_FAIXAS:
        tocar_lista(t, i);
        return;
    case T_PASTAS:
        if (i < t->n_itens) {
            abrir_pasta(t->itens[i].valor ? DAP_RAIZ_USB : DAP_RAIZ_CARTAO, t->itens[i].texto);
        } else if (i < t->n_itens + t->n_subpastas) {
            char caminho[DAP_MAX_CAMINHO];
            if (snprintf(caminho, sizeof(caminho), "%s/%s", t->pasta, t->subpastas[i - t->n_itens]) < (int)sizeof(caminho))
                abrir_pasta(caminho, t->subpastas[i - t->n_itens]);
        } else {
            tocar_lista(t, i - t->n_itens - t->n_subpastas);
        }
        return;
    default:
        break;
    }

    /* menus e escolhas */
    item_t *it = &t->itens[i];
    switch (it->acao) {
    case A_TOCANDO: abrir_tocando(); break;
    case A_CATEGORIA: abrir_categoria((dap_categoria_t)it->valor); break;
    case A_CONFIG: abrir_configuracoes(); break;
    case A_FAVORITAR:
        favoritar_atual();
        voltar_tela();
        break;
    case A_ESCOLHER_EQ: abrir_escolha_eq(); break;
    case A_ESCOLHER_MODO: abrir_escolha_modo(); break;
    case A_ESCOLHER_PLAYLIST: abrir_escolha_playlist(); break;
    case A_ESCOLHER_TELA: abrir_escolha_tela(); break;
    case A_IR_ALBUM:
        if (dap_est.faixa >= 0) {
            int f = dap_est.faixa;
            voltar_tela();
            abrir_faixas(ou(dap_txt(dap_faixas_tab[f].album), "(Sem álbum)"), DAP_CAT_ALBUNS, (uint32_t)f);
        }
        break;
    case A_FILA:
        voltar_tela();
        abrir_fila();
        break;
    case A_ATUALIZAR: atualizar_biblioteca(); break;
    case A_SOBRE: abrir_sobre(); break;
    case A_EQ:
        dap_aplicar_eq(it->valor);
        mostrar_aviso(dap_presets_eq[it->valor].nome);
        voltar_tela();
        if (topo() && topo()->tipo == T_OPCOES) { voltar_tela(); }
        break;
    case A_MODO:
        dap_est.modo = (dap_modo_t)it->valor;
        dap_est.repetiu = 0;
        mostrar_aviso(dap_nomes_dos_modos[it->valor]);
        voltar_tela();
        if (topo() && topo()->tipo == T_OPCOES) { voltar_tela(); }
        break;
    case A_TEMPO_TELA:
        segundos_para_apagar = it->valor;
        voltar_tela();
        if (topo() && topo()->tipo == T_OPCOES) { voltar_tela(); abrir_configuracoes(); }
        break;
    case A_NOVA_PLAYLIST: {
        char nome[48];
        if (!dap_nova_playlist(nome, sizeof(nome))) { mostrar_aviso("Sem cartão para gravar"); break; }
        if (dap_adicionar_na_playlist(nome, dap_est.faixa)) {
            char b[64];
            snprintf(b, sizeof(b), "Em %s", nome);
            voltar_tela();
            if (topo() && topo()->tipo == T_OPCOES) voltar_tela();
            mostrar_aviso(b);
        }
        break;
    }
    case A_PLAYLIST: {
        char nome[64], b[80];
        snprintf(nome, sizeof(nome), "%s", it->texto);
        if (dap_adicionar_na_playlist(nome, dap_est.faixa)) snprintf(b, sizeof(b), "Em %s", nome);
        else snprintf(b, sizeof(b), "Não deu para gravar");
        voltar_tela();
        if (topo() && topo()->tipo == T_OPCOES) voltar_tela();
        mostrar_aviso(b);
        break;
    }
    default: break;
    }
}

static void mover(tela_t *t, int delta)
{
    int n = linhas_da_tela(t);
    if (n <= 0) return;
    int antes = t->sel, topo_antes = t->topo;
    t->sel += delta;
    if (t->sel < 0) t->sel = 0;
    if (t->sel >= n) t->sel = n - 1;
    if (t->sel < t->topo) t->topo = t->sel;
    if (t->sel >= t->topo + LINHAS_VISIVEIS) t->topo = t->sel - LINHAS_VISIVEIS + 1;
    if (t->topo != topo_antes) sujo_tudo = 1;
    else if (antes != t->sel) { linha_suja_a = antes; linha_suja_b = t->sel; }
}

/* ------------------------------------------------- a varredura */

static void desenhar_partida(const char *linha1, const char *linha2, int progresso)
{
    dap_ret(0, 0, DAP_LARGURA, DAP_ALTURA, COR_FUNDO);
    dap_icone(&dap_fonte_icones_grandes, ICO_FONE, 96, 92, COR_DESTAQUE);
    dap_texto_centro(&dap_fonte_grande, 120, 148, 220, "PlayerDAP", COR_TEXTO);
    dap_texto_centro(&dap_fonte_normal, 120, 196, 220, linha1, COR_TEXTO2);
    dap_texto_centro(&dap_fonte_pequena, 120, 218, 224, linha2, COR_TEXTO3);
    if (progresso >= 0) {
        /* sem saber quanto falta: um pedaco que anda pela barra */
        dap_ret_arredondado(40, 244, 160, 4, 2, COR_BARRA);
        dap_ret_arredondado(40 + progresso % 121, 244, 40, 4, 2, COR_DESTAQUE);
    }
    dap_tela_mostrar(0, 0, DAP_LARGURA, DAP_ALTURA);
}

static volatile int varrendo;

static void tarefa_varredura(void *arg)
{
    (void)arg;
    dap_varrer();
    varrendo = 0;
    vTaskDelete(NULL);
}

/* No monitor serial, o que a biblioteca achou: na placa de verdade e o
 * primeiro lugar para olhar quando uma musica nao aparece. */
static void contar_biblioteca(void)
{
    printf("DAP: %d musicas na biblioteca\n", dap_n_faixas);
    uint32_t *v = dap_n_faixas > 0 ? (uint32_t *)dap_alocar((size_t)dap_n_faixas * sizeof(uint32_t)) : NULL;
    if (!v) return;
    static const dap_categoria_t cats[] = { DAP_CAT_ARTISTAS, DAP_CAT_ALBUNS, DAP_CAT_ARTISTAS_ALBUM,
                                            DAP_CAT_GENEROS, DAP_CAT_ANOS, DAP_CAT_COMPOSITORES };
    int n[6];
    for (int i = 0; i < 6; i++) n[i] = dap_valores(cats[i], v, dap_n_faixas);
    free(v);
    char (*nomes)[DAP_MAX_NOME_PLAYLIST] = (char (*)[DAP_MAX_NOME_PLAYLIST])dap_alocar(64 * DAP_MAX_NOME_PLAYLIST);
    int playlists = nomes ? dap_playlists(nomes, 64) : 0;
    free(nomes);
    printf("DAP: %d artistas, %d albuns, %d artistas de album, %d generos, %d anos, %d compositores, %d playlists\n",
           n[0], n[1], n[2], n[3], n[4], n[5], playlists);
}

static void varrer_mostrando(void)
{
    varrendo = 1;
    /* a varredura desce ate 12 pastas, com um caminho em cada nivel */
    xTaskCreatePinnedToCore(tarefa_varredura, "dap_biblioteca", 16384, NULL, 4, NULL, 0);
    int giro = 0;
    while (varrendo) {
        char b[64];
        snprintf(b, sizeof(b), "Lendo a biblioteca: %d músicas", dap_varredura_achadas);
        desenhar_partida(b, dap_varredura_atual, giro += 7);
        vTaskDelay(pdMS_TO_TICKS(150));
    }
    contar_biblioteca();
}

static void atualizar_biblioteca(void)
{
    dap_parar();
    vTaskDelay(pdMS_TO_TICKS(100));
    DAP_TRAVAR();
    dap_est.faixa = -1;
    free(dap_est.fila);
    dap_est.fila = NULL;
    dap_est.n_fila = 0;
    DAP_SOLTAR();
    /* o cache sai: tudo e lido de novo */
    remove(DAP_RAIZ_CARTAO "/.dap/biblioteca.bin");
    while (n_pilha) soltar_tela(&pilha[--n_pilha]);
    varrer_mostrando();
    abrir_menu_principal();
    char b[48];
    snprintf(b, sizeof(b), "%d músicas", dap_n_faixas);
    mostrar_aviso(b);
}

/* -------------------------------------------------- os eventos */

static void tratar(dap_evento_t ev)
{
    tela_t *t = topo();
    if (!t) return;
    switch (ev) {
    case EV_VOL_MAIS:
    case EV_VOL_MENOS:
        dap_mudar_volume(ev == EV_VOL_MAIS ? 1 : -1);
        volume_ate = agora_ms() + 1500;
        sujo_tudo = 1;
        return;
    case EV_PAUSA:
        if (dap_est.faixa < 0 && t->tipo == T_FAIXAS && t->n_faixas > 0) { tocar_lista(t, t->sel); return; }
        if (dap_est.faixa < 0 && dap_n_faixas > 0) {
            int *todas = (int *)dap_alocar((size_t)dap_n_faixas * sizeof(int));
            if (todas) {
                int n = dap_faixas_de(DAP_CAT_TODAS, 0, todas, dap_n_faixas);
                dap_tocar_fila(todas, n, 0, "Todas as músicas");
                free(todas);
                abrir_tocando();
            }
            return;
        }
        dap_tocar_pausar();
        sujo_status = 1;
        if (t->tipo == T_TOCANDO) sujo_tudo = 1;
        return;
    case EV_PAUSA_LONGO:
        dap_parar();
        mostrar_aviso("Parado");
        return;
    case EV_VOLTAR:
        if (n_pilha > 1) voltar_tela();
        else if (dap_est.faixa >= 0) abrir_tocando();
        return;
    case EV_VOLTAR_LONGO:
        ciclar_modo();
        return;
    case EV_MODO:
        if (t->tipo == T_OPCOES && !strcmp(t->titulo, "Modo")) { voltar_tela(); return; }
        abrir_menu_modo();
        return;
    case EV_MODO_LONGO:
        favoritar_atual();
        return;
    default:
        break;
    }

    if (t->tipo == T_TOCANDO) {
        switch (ev) {
        case EV_GIRO_HORARIO:
        case EV_GIRO_ANTI:
            if (roda_avanca) {
                int p = dap_posicao_atual_ms() + (ev == EV_GIRO_HORARIO ? 5000 : -5000);
                dap_buscar_ms(p < 0 ? 0 : p);
                sujo_progresso = 1;
            } else if (ev == EV_GIRO_HORARIO) dap_proxima(1);
            else dap_anterior();
            sujo_tudo = 1;
            break;
        case EV_OK:
            roda_avanca = !roda_avanca;
            mostrar_aviso(roda_avanca ? "Roda: avançar e voltar" : "Roda: trocar de música");
            break;
        case EV_OK_LONGO:
            abrir_fila();
            break;
        default: break;
        }
        return;
    }
    switch (ev) {
    case EV_GIRO_HORARIO: mover(t, 1); break;
    case EV_GIRO_ANTI: mover(t, -1); break;
    case EV_OK: ativar(t); break;
    default: break;
    }
}

/* ------------------------------------------------ guardar onde parou */

void dap_salvar_estado(void)
{
    if (!dap_cartao_ok) return;
    mkdir(DAP_RAIZ_CARTAO "/.dap", 0777);
    FILE *f = fopen(DAP_RAIZ_CARTAO "/.dap/estado.txt", "w");
    if (!f) return;
    fprintf(f, "volume=%d\neq=%d\nmodo=%d\ntela=%d\n", dap_est.volume, dap_est.eq, (int)dap_est.modo, segundos_para_apagar);
    DAP_TRAVAR();
    if (dap_est.faixa >= 0 && dap_est.faixa < dap_n_faixas) {
        fprintf(f, "faixa=%s\nposicao=%d\nfila_nome=%s\npos_fila=%d\n", dap_txt(dap_faixas_tab[dap_est.faixa].caminho),
                dap_posicao_atual_ms(), dap_est.nome_fila, dap_est.pos_fila);
        FILE *q = fopen(DAP_RAIZ_CARTAO "/.dap/fila.m3u", "w");
        if (q) {
            for (int i = 0; i < dap_est.n_fila; i++) fprintf(q, "%s\n", dap_txt(dap_faixas_tab[dap_est.fila[i]].caminho));
            fclose(q);
        }
    }
    DAP_SOLTAR();
    fclose(f);
}

static char estado_faixa[DAP_MAX_CAMINHO];
static int estado_posicao, estado_pos_fila;
static char estado_fila_nome[64];

void dap_carregar_estado(void)
{
    FILE *f = fopen(DAP_RAIZ_CARTAO "/.dap/estado.txt", "r");
    if (!f) return;
    char linha[DAP_MAX_CAMINHO + 16];
    while (fgets(linha, sizeof(linha), f)) {
        linha[strcspn(linha, "\r\n")] = 0;
        char *igual = strchr(linha, '=');
        if (!igual) continue;
        *igual = 0;
        const char *v = igual + 1;
        if (!strcmp(linha, "volume")) dap_est.volume = atoi(v) < 0 ? 0 : atoi(v) > 30 ? 30 : atoi(v);
        else if (!strcmp(linha, "eq")) dap_est.eq = atoi(v) >= 0 && atoi(v) < DAP_PRESETS_EQ ? atoi(v) : 0;
        else if (!strcmp(linha, "modo")) dap_est.modo = (dap_modo_t)(atoi(v) >= 0 && atoi(v) < DAP_MODOS ? atoi(v) : 0);
        else if (!strcmp(linha, "tela")) segundos_para_apagar = atoi(v);
        else if (!strcmp(linha, "faixa")) dap_copiar(estado_faixa, (int)sizeof(estado_faixa), v);
        else if (!strcmp(linha, "posicao")) estado_posicao = atoi(v);
        else if (!strcmp(linha, "fila_nome")) dap_copiar(estado_fila_nome, (int)sizeof(estado_fila_nome), v);
        else if (!strcmp(linha, "pos_fila")) estado_pos_fila = atoi(v);
    }
    fclose(f);
}

/* Volta a fila e a faixa de antes de desligar, pausada no mesmo ponto. */
static int retomar_fila(void)
{
    if (!estado_faixa[0]) return 0;
    int *fila = (int *)dap_alocar(DAP_MAX_FAIXAS * sizeof(int));
    if (!fila) return 0;
    int n = 0;
    FILE *q = fopen(DAP_RAIZ_CARTAO "/.dap/fila.m3u", "r");
    char linha[DAP_MAX_CAMINHO + 16];
    if (q) {
        while (fgets(linha, sizeof(linha), q) && n < DAP_MAX_FAIXAS) {
            linha[strcspn(linha, "\r\n")] = 0;
            int i = dap_faixa_do_caminho(linha);
            if (i >= 0) fila[n++] = i;
        }
        fclose(q);
    }
    int atual = dap_faixa_do_caminho(estado_faixa);
    if (atual < 0) { free(fila); return 0; }
    if (n == 0) fila[n++] = atual;
    /* a posicao guardada, se ainda bate; senao, onde a faixa estiver na fila */
    int pos = -1;
    if (estado_pos_fila >= 0 && estado_pos_fila < n && fila[estado_pos_fila] == atual) pos = estado_pos_fila;
    for (int i = 0; i < n && pos < 0; i++) if (fila[i] == atual) pos = i;
    if (pos < 0) { fila[0] = atual; n = 1; pos = 0; }
    DAP_TRAVAR();
    free(dap_est.fila);
    dap_est.fila = fila;
    dap_est.n_fila = n;
    dap_est.pos_fila = pos;
    dap_est.faixa = atual;
    snprintf(dap_est.nome_fila, sizeof(dap_est.nome_fila), "%s", estado_fila_nome);
    DAP_SOLTAR();
    /* mostra no ponto em que estava, pausada: a PAUSA continua dali */
    dap_est.quadros_tocados = 0;
    dap_est.info.taxa = 1000;
    dap_est.quadros_tocados = (uint64_t)estado_posicao;
    dap_est.info.quadros = dap_faixas_tab[atual].duracao_ms;
    dap_est.info.formato = dap_faixas_tab[atual].formato;
    return 1;
}

/* O pendrive entrou ou saiu: a biblioteca e lida de novo e os indices das
 * faixas mudam. A fila e guardada pelos caminhos e volta sem as faixas que
 * sumiram; a que tocava (do cartao, ou de um pendrive que voltou) continua
 * do mesmo ponto. O som para durante a leitura: ninguem le a tabela
 * enquanto ela e refeita. */
static void trocar_de_biblioteca(void)
{
    int tocava = dap_est.tocando && !dap_est.pausado;
    int posicao = dap_posicao_atual_ms();
    char nome[sizeof(dap_est.nome_fila)];
    static char atual[DAP_MAX_CAMINHO];
    char *caminhos = NULL;
    int n = 0, pos_antiga = -1;

    DAP_TRAVAR();
    dap_copiar(nome, (int)sizeof(nome), dap_est.nome_fila);
    atual[0] = 0;
    if (dap_est.faixa >= 0 && dap_est.faixa < dap_n_faixas)
        dap_copiar(atual, (int)sizeof(atual), dap_txt(dap_faixas_tab[dap_est.faixa].caminho));
    if (dap_est.fila && dap_est.n_fila > 0) {
        size_t tam = 0;
        for (int i = 0; i < dap_est.n_fila; i++)
            if (dap_est.fila[i] >= 0 && dap_est.fila[i] < dap_n_faixas) tam += strlen(dap_txt(dap_faixas_tab[dap_est.fila[i]].caminho)) + 1;
        caminhos = (char *)dap_alocar(tam + 1);
        size_t usado = 0;
        for (int i = 0; caminhos && i < dap_est.n_fila; i++) {
            int f = dap_est.fila[i];
            if (f < 0 || f >= dap_n_faixas) continue;
            if (i == dap_est.pos_fila) pos_antiga = n;
            const char *c = dap_txt(dap_faixas_tab[f].caminho);
            size_t l = strlen(c) + 1;
            memcpy(caminhos + usado, c, l);
            usado += l;
            n++;
        }
    }
    DAP_SOLTAR();

    dap_parar();
    vTaskDelay(pdMS_TO_TICKS(100));
    DAP_TRAVAR();
    dap_est.faixa = -1;
    free(dap_est.fila);
    dap_est.fila = NULL;
    dap_est.n_fila = 0;
    DAP_SOLTAR();

    while (n_pilha) soltar_tela(&pilha[--n_pilha]);
    varrer_mostrando();
    abrir_menu_principal();

    /* a fila de volta, pelos caminhos */
    int *fila = n > 0 ? (int *)dap_alocar((size_t)n * sizeof(int)) : NULL;
    int m = 0, pos = -1;
    if (fila && caminhos) {
        const char *c = caminhos;
        for (int k = 0; k < n; k++, c += strlen(c) + 1) {
            int f = dap_faixa_do_caminho(c);
            if (f < 0) continue;
            if (k == pos_antiga) pos = m;
            fila[m++] = f;
        }
    }
    free(caminhos);
    int f_atual = atual[0] ? dap_faixa_do_caminho(atual) : -1;
    if (f_atual < 0 || m == 0) { free(fila); return; }   /* a que tocava sumiu: fica parado */

    DAP_TRAVAR();
    dap_est.fila = fila;
    dap_est.n_fila = m;
    dap_est.pos_fila = pos < 0 ? 0 : pos;
    dap_est.faixa = f_atual;
    dap_copiar(dap_est.nome_fila, (int)sizeof(dap_est.nome_fila), nome);
    DAP_SOLTAR();
    if (tocava) {
        dap_retomar(posicao);
        abrir_tocando();
    } else {
        /* pausada: mostra o ponto em que estava, e a PAUSA continua dali */
        dap_est.info.taxa = 1000;
        dap_est.quadros_tocados = (uint64_t)posicao;
        dap_est.info.quadros = dap_faixas_tab[f_atual].duracao_ms;
        dap_est.info.formato = dap_faixas_tab[f_atual].formato;
    }
}

/* --------------------------------------------------------- a tarefa */

void dap_interface_tarefa(void *arg)
{
    (void)arg;
    if (dap_tela_iniciar() != 0) { vTaskDelete(NULL); return; }
    desenhar_partida("Ligando...", "", -1);
    dap_tela_luz(1);

    dap_montar_cartao();
    dap_usb_iniciar();
    if (dap_cartao_ok) dap_carregar_estado();
    dap_audio_iniciar();
    dap_entrada_iniciar();

    if (!dap_cartao_ok && !dap_usb_ok) {
        desenhar_partida("Sem cartão nem pendrive", "Coloque um cartão microSD com músicas", -1);
        while (!dap_cartao_ok && !dap_usb_ok && !dap_desligando) {
            vTaskDelay(pdMS_TO_TICKS(1000));
            dap_montar_cartao();
        }
    }
    varrer_mostrando();

    abrir_menu_principal();
    if (retomar_fila()) abrir_tocando();
    redesenhar();
    ultima_atividade = ultimo_salvamento = agora_ms();
    int64_t ultimo_progresso = 0, ultimo_status = 0;
    int estava_tocando = -1, ultima_faixa = -2;

    while (!dap_desligando) {
        dap_evento_t ev = dap_proximo_evento(40);
        int64_t agora = agora_ms();
        if (ev != EV_NADA) {
            ultima_atividade = agora;
            if (tela_apagada) {
                tela_apagada = 0;
                dap_tela_luz(1);
                /* o volume e a pausa funcionam no escuro; o resto so acende */
                if (ev != EV_VOL_MAIS && ev != EV_VOL_MENOS && ev != EV_PAUSA) { sujo_tudo = 1; ev = EV_NADA; }
            }
            if (ev != EV_NADA) tratar(ev);
        }

        /* a faixa mudou sozinha (acabou a anterior) */
        if (dap_est.faixa != ultima_faixa || dap_est.tocando != estava_tocando) {
            ultima_faixa = dap_est.faixa;
            estava_tocando = dap_est.tocando;
            sujo_status = 1;
            tela_t *t = topo();
            if (t && (t->tipo == T_TOCANDO || t->tipo == T_FAIXAS || t->tipo == T_MENU)) sujo_tudo = 1;
        }

        /* o aviso e o volume somem sozinhos */
        if ((aviso_ate && agora >= aviso_ate) || (volume_ate && agora >= volume_ate)) {
            if (agora >= aviso_ate) aviso_ate = 0;
            if (agora >= volume_ate) volume_ate = 0;
            sujo_tudo = 1;
        }

        int pct = bateria_pct();
        if (pct != ultimo_pct && agora - ultimo_status > 2000) {
            if (pct >= 0 && pct <= 5 && ultimo_pct > 5) mostrar_aviso("Bateria fraca");
            ultimo_pct = pct;
            ultimo_status = agora;
            sujo_status = 1;
        }

        if (!tela_apagada) {
            if (sujo_tudo) redesenhar();
            else {
                tela_t *t = topo();
                if (sujo_status) { desenhar_status(); sujo_status = 0; }
                if (t && linha_suja_a >= 0) { desenhar_linha(t, linha_suja_a); linha_suja_a = -1; }
                if (t && linha_suja_b >= 0) { desenhar_linha(t, linha_suja_b); linha_suja_b = -1; }
                if (t && t->tipo == T_TOCANDO && (sujo_progresso || (dap_est.tocando && !dap_est.pausado && agora - ultimo_progresso >= 250))) {
                    desenhar_progresso();
                    if (aviso_ate > agora) desenhar_sobreposicoes();
                    ultimo_progresso = agora;
                    sujo_progresso = 0;
                }
            }
            if (segundos_para_apagar > 0 && agora - ultima_atividade > segundos_para_apagar * 1000LL) {
                tela_apagada = 1;
                dap_tela_luz(0);
            }
        }

        /* o pendrive entrou ou saiu: le a biblioteca de novo (o cartao vem do cache) */
        if (dap_usb_mudou) {
            dap_usb_mudou = 0;
            trocar_de_biblioteca();
            mostrar_aviso(dap_usb_ok ? "Pendrive conectado" : "Pendrive removido");
            sujo_tudo = 1;
        }

        /* de tempos em tempos, onde esta (a energia pode acabar sem aviso) */
        if (agora - ultimo_salvamento > 30000) {
            dap_salvar_estado();
            ultimo_salvamento = agora;
        }
    }

    /* desligando: guarda e apaga */
    dap_salvar_estado();
    dap_parar();
    dap_ret(0, 0, DAP_LARGURA, DAP_ALTURA, COR_FUNDO);
    dap_icone(&dap_fonte_icones_grandes, ICO_DESLIGAR, 96, 120, COR_TEXTO2);
    dap_texto_centro(&dap_fonte_normal, 120, 180, 220, "Desligando...", COR_TEXTO2);
    dap_tela_mostrar(0, 0, DAP_LARGURA, DAP_ALTURA);
    vTaskDelay(pdMS_TO_TICKS(300));
    dap_tela_dormir();
    dap_desligando = 2;   /* pronto: o sketch pode dormir */
    vTaskDelete(NULL);
}

void dap_interface_iniciar(void)
{
    xTaskCreatePinnedToCore(dap_interface_tarefa, "dap_interface", 12288, NULL, 5, NULL, 0);
}
