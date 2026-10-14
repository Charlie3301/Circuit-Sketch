/* dap_interno.h -- o que os modulos do PlayerDAP dividem entre si. */
#ifndef DAP_INTERNO_H
#define DAP_INTERNO_H

#include <stdint.h>
#include <stdbool.h>
#include <stddef.h>
#include <stdio.h>
#include "freertos/FreeRTOS.h"
#include "freertos/task.h"
#include "freertos/semphr.h"
#include "freertos/queue.h"
#include "dap_fontes.h"
#include "dap_icones.h"

#define DAP_LARGURA 240
#define DAP_ALTURA 320

/* ===================================================================
 *  configuracao: os pinos
 * =================================================================== */
typedef struct {
    int mosi, miso, sck;
    int tela_cs, tela_dc, tela_rst, tela_luz;
    int cartao_cs;
    int usb;
    int som_bclk, som_lrc, som_din, som_taxa_maxima;
    int vol_mais, vol_menos, voltar, modo, pausa;
    int roda_clk, roda_dt, roda_sw;
} dap_config_t;

extern dap_config_t dap_cfg;

/* Uma trava para o que a interface e o audio mexem juntos (a fila, o estado). */
extern SemaphoreHandle_t dap_trava;
#define DAP_TRAVAR() xSemaphoreTake(dap_trava, portMAX_DELAY)
#define DAP_SOLTAR() xSemaphoreGive(dap_trava)

/* Memoria grande: na PSRAM, se tem; senao na interna. */
void *dap_alocar(size_t n);
void *dap_realocar(void *p, size_t n);

/* ===================================================================
 *  tela (dap_tela.c) e desenho (dap_desenho.c)
 * =================================================================== */
extern uint16_t *dap_quadro;   /* 240x320, RGB565, na ordem do display (byte alto primeiro) */

int dap_tela_iniciar(void);
void dap_tela_mostrar(int x, int y, int l, int a);
void dap_tela_luz(int ligada);
/* Desligando: apaga a luz e poe o controlador para dormir (DISPOFF e SLPIN). */
void dap_tela_dormir(void);

#define RGB(r, g, b) ((uint16_t)((((r) & 0xF8) << 8) | (((g) & 0xFC) << 3) | (((b) & 0xFF) >> 3)))

/* As cores da interface. */
#define COR_FUNDO      RGB(14, 16, 21)
#define COR_FUNDO2     RGB(24, 27, 34)
#define COR_BARRA      RGB(32, 36, 45)
#define COR_TEXTO      RGB(236, 238, 242)
#define COR_TEXTO2     RGB(150, 158, 170)
#define COR_TEXTO3     RGB(96, 104, 116)
#define COR_DESTAQUE   RGB(255, 122, 69)
#define COR_DESTAQUE2  RGB(255, 178, 102)
#define COR_SELECAO    RGB(48, 40, 38)
#define COR_VERDE      RGB(96, 200, 120)
#define COR_VERMELHO   RGB(240, 84, 84)

void dap_recorte(int x, int y, int l, int a);
void dap_sem_recorte(void);
void dap_ret(int x, int y, int l, int a, uint16_t cor);
void dap_ret_arredondado(int x, int y, int l, int a, int raio, uint16_t cor);
void dap_moldura(int x, int y, int l, int a, uint16_t cor);
void dap_degrade_v(int x, int y, int l, int a, uint16_t de, uint16_t para);
uint16_t dap_misturar(uint16_t fundo, uint16_t frente, int alfa);   /* alfa 0..255 */

/* Texto em UTF-8. y e o topo da linha da fonte; devolve o x onde parou. */
int dap_texto(const dap_fonte_t *f, int x, int y, const char *s, uint16_t cor);
int dap_texto_largura(const dap_fonte_t *f, const char *s);
void dap_texto_cortado(const dap_fonte_t *f, int x, int y, int largura, const char *s, uint16_t cor);
void dap_texto_centro(const dap_fonte_t *f, int cx, int y, int largura, const char *s, uint16_t cor);
void dap_texto_direita(const dap_fonte_t *f, int x_direita, int y, const char *s, uint16_t cor);
/* Um icone da Material Icons (ICO_...), com o topo em y. */
int dap_icone(const dap_fonte_t *f, uint32_t icone, int x, int y, uint16_t cor);

/* ===================================================================
 *  formatos (dap_formatos.c): decodifica para PCM de 16 ou 32 bits
 * =================================================================== */
/* Os numeros vao no cache da biblioteca: formato novo entra no fim. O M4A
 * e o MP4 com AAC (o ALAC tem o seu); M4A, WMA e APE sao reconhecidos, mas
 * nao tocam. */
enum {
    DAP_FMT_NADA = 0, DAP_FMT_MP3, DAP_FMT_FLAC, DAP_FMT_WAV, DAP_FMT_AIFF,
    DAP_FMT_OGG, DAP_FMT_DSF, DAP_FMT_DFF,
    DAP_FMT_M4A, DAP_FMT_OPUS, DAP_FMT_WMA, DAP_FMT_APE, DAP_FMT_WV,
    DAP_FMT_ALAC,
    DAP_FMT_QUANTOS
};

const char *dap_nome_do_formato(int formato);
int dap_formato_tocavel(int formato);
/* O formato pelo conteudo (os primeiros bytes), e nao pela extensao. */
int dap_detectar_formato(const char *caminho);
/* A extensao parece de audio? (para a varredura nao abrir tudo) */
int dap_extensao_de_audio(const char *nome);

typedef struct dap_decodificador dap_decodificador_t;

typedef struct {
    int taxa;            /* amostras por segundo, por canal */
    int canais;          /* 1 ou 2 (mais que 2 vira 2) */
    int bits;            /* bits da fonte: 16, 24, 32, 1 (DSD) */
    int formato;         /* DAP_FMT_... */
    uint64_t quadros;    /* total de quadros (0: nao sabe) */
    int kbps;            /* taxa de bits media, para mostrar */
} dap_info_audio_t;

dap_decodificador_t *dap_abrir_audio(const char *caminho, dap_info_audio_t *info, char *erro, int tam_erro);
/* Le ate n quadros em float, estereo intercalado (L R L R), de -1 a 1 (mono
 * vira os dois lados; mais de dois canais, os dois primeiros). Devolve quantos. */
int dap_ler_audio(dap_decodificador_t *d, float *saida, int n);
int dap_buscar_audio(dap_decodificador_t *d, uint64_t quadro);
void dap_fechar_audio(dap_decodificador_t *d);

/* Os formatos que moram nos seus arquivos (dap_m4a.c, dap_opus.c, dap_wv.c):
 * cada um abre (e diz a taxa, os canais, os bits e os quadros), le em float
 * estereo intercalado, busca um quadro e fecha. */
typedef struct {
    void *(*abrir)(const char *caminho, dap_info_audio_t *info, char *erro, int tam_erro);
    int (*ler)(void *estado, float *estereo, int quadros);
    int (*buscar)(void *estado, uint64_t quadro);
    void (*fechar)(void *estado);
} dap_formato_t;

extern const dap_formato_t dap_formato_alac, dap_formato_opus, dap_formato_wv;

/* MP4/M4A: acha a caixa "tipo" em [inicio, fim) -- onde o conteudo comeca e
 * o tamanho dele --, e o codec da trilha de som (DAP_FMT_ALAC ou DAP_FMT_M4A). */
int dap_m4a_caixa(FILE *f, long inicio, long fim, const char *tipo, long *conteudo, long *tamanho);
int dap_m4a_codec(FILE *f);

/* ===================================================================
 *  etiquetas (dap_tags.c)
 * =================================================================== */
typedef struct {
    char titulo[96], artista[96], album[96], artista_album[96], genero[48], compositor[96];
    int ano, faixa, disco;
    int duracao_ms;
} dap_etiquetas_t;

/* Le as etiquetas (ID3v1/v2, Vorbis, RIFF INFO, AIFF, DSF); o que faltar
 * vem do nome do arquivo ("Artista - Titulo.mp3") e da pasta. */
void dap_ler_etiquetas(const char *caminho, int formato, dap_etiquetas_t *e);

/* ===================================================================
 *  biblioteca (dap_biblioteca.c)
 * =================================================================== */
#define DAP_MAX_FAIXAS 8192
#define DAP_SEM_TEXTO 0   /* o deslocamento do texto vazio no reservatorio */

typedef struct {
    uint32_t caminho, pasta, titulo, artista, album, artista_album, genero, compositor;
    uint32_t duracao_ms;
    uint32_t tamanho;
    uint16_t ano, faixa;
    uint8_t disco, formato, origem, marcas;
} dap_faixa_t;

#define DAP_MARCA_FAVORITA 1
#define DAP_MARCA_ESTRAGADA 2

extern dap_faixa_t *dap_faixas_tab;
extern int dap_n_faixas;
const char *dap_txt(uint32_t deslocamento);

/* O maior caminho de arquivo que a biblioteca usa (com o \0): um maior e pulado. */
#define DAP_MAX_CAMINHO 384
/* Copia s em destino (tam bytes, com o \0) sem partir um caractere UTF-8. */
void dap_copiar(char *destino, int tam, const char *s);
/* A faixa da biblioteca com esse caminho (FAT: sem diferenca de maiusculas), ou -1. */
int dap_faixa_do_caminho(const char *caminho);

typedef enum {
    DAP_CAT_TODAS, DAP_CAT_ARTISTAS, DAP_CAT_ALBUNS, DAP_CAT_ARTISTAS_ALBUM, DAP_CAT_GENEROS,
    DAP_CAT_ANOS, DAP_CAT_COMPOSITORES, DAP_CAT_PASTAS, DAP_CAT_PLAYLISTS, DAP_CAT_FAVORITAS,
} dap_categoria_t;

/* A varredura (na tarefa da biblioteca): devolve quantas achou. O progresso vai em dap_varredura_*. */
int dap_varrer(void);
extern volatile int dap_varredura_lidas, dap_varredura_achadas, dap_varredura_pronta;
extern char dap_varredura_atual[96];

/* Os valores distintos de uma categoria (artistas, albuns...), em ordem: os
 * deslocamentos do texto. Para os anos, o numero do ano. */
int dap_valores(dap_categoria_t cat, uint32_t *saida, int max);
/* As faixas de um valor, na ordem de tocar (disco, faixa, titulo). */
int dap_faixas_de(dap_categoria_t cat, uint32_t valor, int *saida, int max);
/* Os albuns de um artista (ou artista do album), em ordem: os deslocamentos. */
int dap_albuns_de(dap_categoria_t cat, uint32_t artista, uint32_t *saida, int max);
int dap_faixas_do_album_de(dap_categoria_t cat, uint32_t artista, uint32_t album, int *saida, int max);

/* Pastas: as subpastas (os nomes em ordem, num bloco so, que se libera com
 * um free) e as faixas de uma pasta (caminho completo). */
int dap_subpastas(const char *pasta, char ***nomes);
int dap_faixas_da_pasta(const char *pasta, int *saida, int max);

/* Favoritas e playlists (arquivos .m3u no cartao). */
void dap_favoritar(int faixa, int sim);
#define DAP_MAX_NOME_PLAYLIST 64
int dap_playlists(char nomes[][DAP_MAX_NOME_PLAYLIST], int max);
int dap_faixas_da_playlist(const char *nome, int *saida, int max);
int dap_nova_playlist(char *nome_criado, int tam);
int dap_adicionar_na_playlist(const char *nome, int faixa);
void dap_carregar_favoritas(void);

/* ===================================================================
 *  audio (dap_audio.c)
 * =================================================================== */
typedef enum {
    DAP_MODO_TODAS,          /* em ordem, e volta ao comeco */
    DAP_MODO_REPETIR_UMA_VEZ,/* a mesma de novo, uma vez, e segue */
    DAP_MODO_REPETIR_SEMPRE, /* a mesma sem parar */
    DAP_MODO_ALEATORIO,      /* qualquer uma da biblioteca */
    DAP_MODO_ALEATORIO_LISTA,/* qualquer uma da lista que esta tocando */
    DAP_MODOS
} dap_modo_t;

extern const char *const dap_nomes_dos_modos[DAP_MODOS];

typedef struct {
    const char *nome;
    int8_t ganho_db[5];   /* 60, 230, 910, 3600 e 14000 Hz */
} dap_preset_eq_t;

#define DAP_PRESETS_EQ 9
extern const dap_preset_eq_t dap_presets_eq[DAP_PRESETS_EQ];

typedef struct {
    /* a fila: as faixas da lista de onde a musica foi escolhida */
    int *fila;
    int n_fila;
    int pos_fila;               /* onde esta na fila */
    char nome_fila[64];         /* "Album: X", "Favoritas"... */
    int faixa;                  /* indice em dap_faixas_tab, -1 = nenhuma */
    int tocando, pausado;
    int volume;                 /* 0..30 */
    int eq;                     /* preset */
    dap_modo_t modo;
    int repetiu;                /* o "repetir uma vez" ja repetiu esta? */
    /* o que esta tocando agora (preenchido pelo audio) */
    dap_info_audio_t info;
    volatile uint64_t quadros_tocados;
    char erro[96];
} dap_estado_t;

extern dap_estado_t dap_est;

void dap_audio_iniciar(void);
void dap_tocar_fila(const int *faixas, int n, int pos, const char *nome);
void dap_retomar(int ms);
void dap_tocar_pausar(void);
void dap_proxima(int pelo_usuario);
void dap_anterior(void);
void dap_buscar_ms(int ms);
void dap_parar(void);
void dap_aplicar_eq(int preset);
void dap_mudar_volume(int delta);
int dap_posicao_atual_ms(void);
int dap_duracao_atual_ms(void);

/* ===================================================================
 *  entrada (dap_entrada.c): botoes e roda viram eventos
 * =================================================================== */
typedef enum {
    EV_NADA, EV_GIRO_HORARIO, EV_GIRO_ANTI, EV_OK, EV_OK_LONGO,
    EV_VOL_MAIS, EV_VOL_MENOS, EV_VOLTAR, EV_VOLTAR_LONGO, EV_MODO, EV_MODO_LONGO, EV_PAUSA, EV_PAUSA_LONGO,
} dap_evento_t;

void dap_entrada_iniciar(void);
dap_evento_t dap_proximo_evento(int espera_ms);

/* ===================================================================
 *  interface (dap_interface.c)
 * =================================================================== */
void dap_interface_iniciar(void);
void dap_interface_tarefa(void *arg);
extern volatile int dap_bateria_mv;
extern volatile int dap_desligando;

/* ===================================================================
 *  armazenamento (dap_armazenamento.c): o cartao e o pendrive
 * =================================================================== */
#define DAP_RAIZ_CARTAO "/sdcard"
#define DAP_RAIZ_USB "/usb"
extern int dap_cartao_ok, dap_usb_ok;
extern volatile int dap_usb_mudou;   /* o pendrive entrou ou saiu: a biblioteca e lida de novo */
int dap_montar_cartao(void);
void dap_usb_iniciar(void);
/* Estado salvo (volume, eq, modo, onde parou): /sdcard/.dap/estado.txt */
void dap_salvar_estado(void);
void dap_carregar_estado(void);

#endif
