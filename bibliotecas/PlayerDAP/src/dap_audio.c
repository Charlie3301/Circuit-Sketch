/* dap_audio.c -- a tarefa que toca: decodifica, equaliza, ajusta o volume e
 * manda pelo I2S.
 *
 * Roda no nucleo 1, sozinha: a interface fica no 0. O I2S e quem marca o
 * ritmo -- o i2s_channel_write so volta quando cabe mais no DMA. A saida e
 * de 32 bits (o DAC usa os de cima), estereo, na taxa do arquivo; acima do
 * que o DAC aguenta (96 kHz no MAX98357A) a taxa e dividida por 2, com um
 * filtro de meia banda antes, ate caber.
 *
 * O equalizador sao cinco filtros biquad (as formulas de Robert
 * Bristow-Johnson): prateleira nos graves (60 Hz) e nos agudos (14 kHz) e
 * picos em 230, 910 e 3600 Hz. Quando algum ganho e positivo, o sinal desce
 * o mesmo tanto antes, para nao estourar.
 */
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <math.h>
#include "freertos/FreeRTOS.h"
#include "freertos/task.h"
#include "freertos/queue.h"
#include "driver/i2s_std.h"
#include "esp_random.h"
#include "dap_interno.h"

dap_estado_t dap_est;

const char *const dap_nomes_dos_modos[DAP_MODOS] = {
    "Todas, em ordem", "Repetir uma vez", "Repetir sem parar", "Aleatório", "Aleatório na lista",
};

const dap_preset_eq_t dap_presets_eq[DAP_PRESETS_EQ] = {
    { "Normal", { 0, 0, 0, 0, 0 } },
    { "Graves+", { 6, 4, 0, 0, 0 } },
    { "Rock", { 4, 2, -2, 2, 4 } },
    { "Pop", { -1, 2, 4, 2, -1 } },
    { "Jazz", { 3, 1, -1, 1, 3 } },
    { "Clássica", { 4, 2, -1, 2, 3 } },
    { "Vocal", { -2, -1, 3, 3, 1 } },
    { "Eletrônica", { 5, 3, 0, 2, 4 } },
    { "Agudos+", { 0, 0, 0, 3, 6 } },
};

enum { CMD_TOCAR, CMD_PAUSAR, CMD_CONTINUAR, CMD_PARAR, CMD_BUSCAR };
typedef struct { int tipo, valor; } comando_t;

static QueueHandle_t comandos;
static i2s_chan_handle_t canal;
static int taxa_do_i2s;

#define BLOCO 512

/* --------------------------------------------------------------- EQ */

typedef struct { float b0, b1, b2, a1, a2; float x1[2], x2[2], y1[2], y2[2]; } biquad_t;

static biquad_t filtros[5];
static int eq_ativo, eq_taxa = -1, eq_preset = -1;
static float eq_pre = 1.0f;

static void projetar_filtro(biquad_t *b, int tipo, double f0, double fs, double db)
{
    /* tipo 0: prateleira de graves, 1: pico, 2: prateleira de agudos */
    if (f0 > fs * 0.45) f0 = fs * 0.45;
    double A = pow(10.0, db / 40.0), w0 = 2 * M_PI * f0 / fs, c = cos(w0), s = sin(w0);
    double b0, b1, b2, a0, a1, a2;
    if (tipo == 1) {
        double alfa = s / (2 * 1.1);
        b0 = 1 + alfa * A; b1 = -2 * c; b2 = 1 - alfa * A;
        a0 = 1 + alfa / A; a1 = -2 * c; a2 = 1 - alfa / A;
    } else {
        double alfa = s / 2 * sqrt(2.0), raiz = 2 * sqrt(A) * alfa;
        if (tipo == 0) {
            b0 = A * ((A + 1) - (A - 1) * c + raiz);
            b1 = 2 * A * ((A - 1) - (A + 1) * c);
            b2 = A * ((A + 1) - (A - 1) * c - raiz);
            a0 = (A + 1) + (A - 1) * c + raiz;
            a1 = -2 * ((A - 1) + (A + 1) * c);
            a2 = (A + 1) + (A - 1) * c - raiz;
        } else {
            b0 = A * ((A + 1) + (A - 1) * c + raiz);
            b1 = -2 * A * ((A - 1) + (A + 1) * c);
            b2 = A * ((A + 1) + (A - 1) * c - raiz);
            a0 = (A + 1) - (A - 1) * c + raiz;
            a1 = 2 * ((A - 1) - (A + 1) * c);
            a2 = (A + 1) - (A - 1) * c - raiz;
        }
    }
    b->b0 = (float)(b0 / a0); b->b1 = (float)(b1 / a0); b->b2 = (float)(b2 / a0);
    b->a1 = (float)(a1 / a0); b->a2 = (float)(a2 / a0);
    memset(b->x1, 0, sizeof(b->x1)); memset(b->x2, 0, sizeof(b->x2));
    memset(b->y1, 0, sizeof(b->y1)); memset(b->y2, 0, sizeof(b->y2));
}

static void preparar_eq(int preset, int taxa)
{
    if (preset == eq_preset && taxa == eq_taxa) return;
    eq_preset = preset;
    eq_taxa = taxa;
    const dap_preset_eq_t *p = &dap_presets_eq[preset >= 0 && preset < DAP_PRESETS_EQ ? preset : 0];
    static const double centros[5] = { 60, 230, 910, 3600, 14000 };
    eq_ativo = 0;
    int maior = 0;
    for (int i = 0; i < 5; i++) {
        if (p->ganho_db[i]) eq_ativo = 1;
        if (p->ganho_db[i] > maior) maior = p->ganho_db[i];
        projetar_filtro(&filtros[i], i == 0 ? 0 : i == 4 ? 2 : 1, centros[i], taxa, p->ganho_db[i]);
    }
    eq_pre = (float)pow(10.0, -maior / 20.0);
}

static void equalizar(float *x, int n)
{
    if (!eq_ativo) return;
    for (int i = 0; i < n; i++)
        for (int c = 0; c < 2; c++) {
            float v = x[2 * i + c] * eq_pre;
            for (int k = 0; k < 5; k++) {
                biquad_t *b = &filtros[k];
                float y = b->b0 * v + b->b1 * b->x1[c] + b->b2 * b->x2[c] - b->a1 * b->y1[c] - b->a2 * b->y2[c];
                b->x2[c] = b->x1[c]; b->x1[c] = v;
                b->y2[c] = b->y1[c]; b->y1[c] = y;
                v = y;
            }
            x[2 * i + c] = v;
        }
}

/* ------------------------------------------- divisao da taxa (meia banda) */

#define TAPS_MEIA 31
static float coef_meia[TAPS_MEIA];
static float hist_meia[3][2][TAPS_MEIA];
static int pos_meia[3], fase_meia[3];

static void preparar_meia_banda(void)
{
    /* sinc com janela de Blackman, corte em 1/4 da taxa de entrada */
    double soma = 0;
    for (int i = 0; i < TAPS_MEIA; i++) {
        double m = i - (TAPS_MEIA - 1) / 2.0;
        double s = m == 0 ? 0.5 : sin(M_PI * 0.5 * m) / (M_PI * m);
        double w = 0.42 - 0.5 * cos(2 * M_PI * i / (TAPS_MEIA - 1)) + 0.08 * cos(4 * M_PI * i / (TAPS_MEIA - 1));
        coef_meia[i] = (float)(s * w);
        soma += coef_meia[i];
    }
    for (int i = 0; i < TAPS_MEIA; i++) coef_meia[i] = (float)(coef_meia[i] / soma);
}

/* Divide por 2 (no estagio dado): devolve quantos quadros sairam. */
static int dividir_taxa(float *x, int n, int estagio)
{
    int saida = 0;
    for (int i = 0; i < n; i++) {
        for (int c = 0; c < 2; c++) hist_meia[estagio][c][pos_meia[estagio]] = x[2 * i + c];
        pos_meia[estagio] = (pos_meia[estagio] + 1) % TAPS_MEIA;
        if ((fase_meia[estagio] ^= 1) != 0) continue;
        for (int c = 0; c < 2; c++) {
            float s = 0;
            int p = pos_meia[estagio];
            for (int k = 0; k < TAPS_MEIA; k++) {
                s += coef_meia[k] * hist_meia[estagio][c][p];
                if (++p == TAPS_MEIA) p = 0;
            }
            x[2 * saida + c] = s;
        }
        saida++;
    }
    return saida;
}

/* --------------------------------------------------------------- I2S */

static int abrir_i2s(int taxa)
{
    if (canal && taxa == taxa_do_i2s) return 1;
    if (canal) {
        i2s_channel_disable(canal);
        i2s_std_clk_config_t clk = I2S_STD_CLK_DEFAULT_CONFIG((uint32_t)taxa);
        if (i2s_channel_reconfig_std_clock(canal, &clk) != ESP_OK) return 0;
        i2s_channel_enable(canal);
        taxa_do_i2s = taxa;
        return 1;
    }
    if (dap_cfg.som_bclk < 0 || dap_cfg.som_din < 0) return 0;
    i2s_chan_config_t cc = I2S_CHANNEL_DEFAULT_CONFIG(I2S_NUM_AUTO, I2S_ROLE_MASTER);
    cc.dma_desc_num = 6;
    cc.dma_frame_num = 480;
    if (i2s_new_channel(&cc, &canal, NULL) != ESP_OK) return 0;
    i2s_std_config_t std = {
        .clk_cfg = I2S_STD_CLK_DEFAULT_CONFIG((uint32_t)taxa),
        .slot_cfg = I2S_STD_PHILIPS_SLOT_DEFAULT_CONFIG(I2S_DATA_BIT_WIDTH_32BIT, I2S_SLOT_MODE_STEREO),
        .gpio_cfg = {
            .mclk = I2S_GPIO_UNUSED,
            .bclk = (gpio_num_t)dap_cfg.som_bclk,
            .ws = (gpio_num_t)dap_cfg.som_lrc,
            .dout = (gpio_num_t)dap_cfg.som_din,
            .din = I2S_GPIO_UNUSED,
            .invert_flags = { .mclk_inv = false, .bclk_inv = false, .ws_inv = false },
        },
    };
    if (i2s_channel_init_std_mode(canal, &std) != ESP_OK || i2s_channel_enable(canal) != ESP_OK) {
        i2s_del_channel(canal);
        canal = NULL;
        return 0;
    }
    taxa_do_i2s = taxa;
    return 1;
}

/* Silencio no DMA: a ultima amostra nao fica repetindo quando a musica pausa. */
static void calar(void)
{
    static int32_t zeros[256];
    size_t w;
    if (canal) for (int i = 0; i < 8; i++) i2s_channel_write(canal, zeros, sizeof(zeros), &w, 20);
}

/* ------------------------------------------------------------- volume */

static float ganho_atual;

static float ganho_do_volume(int v)
{
    if (v <= 0) return 0;
    if (v > 30) v = 30;
    return powf(10.0f, (v - 30) * 2.0f / 20.0f);   /* 2 dB por passo: 30 = 0 dB, 1 = -58 dB */
}

/* ---------------------------------------------------- a fila e os modos */

#define HISTORIA 64
static int historia[HISTORIA], n_historia;

static int tocavel(int i)
{
    return i >= 0 && i < dap_n_faixas && !(dap_faixas_tab[i].marcas & DAP_MARCA_ESTRAGADA);
}

static uint32_t sorteio(uint32_t n) { return n ? esp_random() % n : 0; }

/* Escolhe a proxima (chame com a trava). Devolve a faixa, ou -1 para parar. */
static int escolher_proxima(int automatico)
{
    dap_estado_t *e = &dap_est;
    if (automatico && e->modo == DAP_MODO_REPETIR_SEMPRE) return e->faixa;
    if (automatico && e->modo == DAP_MODO_REPETIR_UMA_VEZ && !e->repetiu) {
        e->repetiu = 1;
        return e->faixa;
    }
    e->repetiu = 0;
    if (e->faixa >= 0 && n_historia < HISTORIA) historia[n_historia++] = e->faixa;
    else if (e->faixa >= 0) { memmove(historia, historia + 1, (HISTORIA - 1) * sizeof(int)); historia[HISTORIA - 1] = e->faixa; }

    if (e->modo == DAP_MODO_ALEATORIO && dap_n_faixas > 0) {
        for (int tentativas = 0; tentativas < 32; tentativas++) {
            int i = (int)sorteio((uint32_t)dap_n_faixas);
            if (tocavel(i) && (i != e->faixa || dap_n_faixas == 1)) return i;
        }
    }
    if (!e->fila || e->n_fila <= 0) return -1;
    if (e->modo == DAP_MODO_ALEATORIO_LISTA && e->n_fila > 1) {
        int p;
        do p = (int)sorteio((uint32_t)e->n_fila); while (p == e->pos_fila);
        e->pos_fila = p;
        return e->fila[p];
    }
    /* em ordem: a seguinte, e do fim volta ao comeco */
    for (int k = 1; k <= e->n_fila; k++) {
        int p = (e->pos_fila + k) % e->n_fila;
        if (tocavel(e->fila[p])) { e->pos_fila = p; return e->fila[p]; }
    }
    return -1;
}

/* --------------------------------------------------------- a tarefa */

static dap_decodificador_t *dec;
static int taxa_da_saida, divisoes;
static volatile int pedido_de_fim;

static void fechar(void)
{
    if (dec) dap_fechar_audio(dec);
    dec = NULL;
}

/* Abre a faixa atual; pula as que nao abrem (no maximo uma volta na fila). */
static int abrir_atual(int buscar_ms)
{
    fechar();
    for (int tentativas = 0; tentativas < 64; tentativas++) {
        int f = dap_est.faixa;
        if (f < 0 || f >= dap_n_faixas) return 0;
        dap_info_audio_t info;
        dec = dap_abrir_audio(dap_txt(dap_faixas_tab[f].caminho), &info, dap_est.erro, sizeof(dap_est.erro));
        if (dec) {
            dap_est.info = info;
            if (!info.quadros && dap_faixas_tab[f].duracao_ms)
                dap_est.info.quadros = (uint64_t)dap_faixas_tab[f].duracao_ms * info.taxa / 1000;
            if (info.quadros && info.taxa) dap_faixas_tab[f].duracao_ms = (uint32_t)(info.quadros * 1000 / info.taxa);
            dap_est.info.kbps = dap_faixas_tab[f].duracao_ms
                ? (int)((uint64_t)dap_faixas_tab[f].tamanho * 8 / dap_faixas_tab[f].duracao_ms) : 0;
            dap_est.quadros_tocados = 0;
            if (buscar_ms > 0 && dap_buscar_audio(dec, (uint64_t)buscar_ms * info.taxa / 1000))
                dap_est.quadros_tocados = (uint64_t)buscar_ms * info.taxa / 1000;
            /* a taxa que o DAC aguenta */
            taxa_da_saida = info.taxa;
            divisoes = 0;
            int maxima = dap_cfg.som_taxa_maxima > 0 ? dap_cfg.som_taxa_maxima : 96000;
            while (taxa_da_saida > maxima && divisoes < 3) { taxa_da_saida /= 2; divisoes++; }
            memset(hist_meia, 0, sizeof(hist_meia));
            preparar_eq(dap_est.eq, taxa_da_saida);
            eq_preset = -1;   /* refaz os filtros na taxa nova */
            preparar_eq(dap_est.eq, taxa_da_saida);
            if (!abrir_i2s(taxa_da_saida)) {
                snprintf(dap_est.erro, sizeof(dap_est.erro), "o I2S nao abriu em %d Hz", taxa_da_saida);
                fechar();
                return 0;
            }
            dap_est.erro[0] = 0;
            return 1;
        }
        printf("DAP: %s: %s\n", dap_txt(dap_faixas_tab[f].caminho), dap_est.erro);
        dap_faixas_tab[f].marcas |= DAP_MARCA_ESTRAGADA;
        DAP_TRAVAR();
        int proxima = escolher_proxima(0);
        dap_est.faixa = proxima;
        DAP_SOLTAR();
        if (proxima < 0 || proxima == f) return 0;
    }
    return 0;
}

static void tarefa_audio(void *arg)
{
    (void)arg;
    float *bloco = (float *)dap_alocar(BLOCO * 2 * sizeof(float));
    int32_t *saida = (int32_t *)malloc(BLOCO * 2 * sizeof(int32_t));
    if (!bloco || !saida) {
        printf("DAP: sem memoria para o audio\n");
        vTaskDelete(NULL);
        return;
    }
    preparar_meia_banda();
    for (;;) {
        comando_t c;
        int ativo = dec && dap_est.tocando && !dap_est.pausado;
        while (xQueueReceive(comandos, &c, ativo ? 0 : pdMS_TO_TICKS(200)) == pdTRUE) {
            switch (c.tipo) {
            case CMD_TOCAR:
                dap_est.pausado = 0;
                dap_est.tocando = abrir_atual(c.valor);
                break;
            case CMD_PAUSAR:
                dap_est.pausado = 1;
                calar();
                break;
            case CMD_CONTINUAR:
                if (dec) dap_est.pausado = 0;
                break;
            case CMD_PARAR:
                fechar();
                dap_est.tocando = 0;
                dap_est.pausado = 0;
                calar();
                break;
            case CMD_BUSCAR:
                if (dec) {
                    uint64_t q = (uint64_t)(c.valor < 0 ? 0 : c.valor) * dap_est.info.taxa / 1000;
                    if (dap_buscar_audio(dec, q)) dap_est.quadros_tocados = q;
                }
                break;
            }
            ativo = dec && dap_est.tocando && !dap_est.pausado;
        }
        if (!ativo) continue;

        int n = dap_ler_audio(dec, bloco, BLOCO);
        if (n <= 0) {
            /* acabou a faixa: a proxima, pelo modo */
            DAP_TRAVAR();
            int proxima = escolher_proxima(1);
            dap_est.faixa = proxima;
            DAP_SOLTAR();
            if (proxima < 0) { fechar(); dap_est.tocando = 0; calar(); continue; }
            dap_est.tocando = abrir_atual(0);
            continue;
        }
        dap_est.quadros_tocados += (uint64_t)n;

        int m = n;
        for (int k = 0; k < divisoes; k++) m = dividir_taxa(bloco, m, k);
        if (dap_est.eq != eq_preset) preparar_eq(dap_est.eq, taxa_da_saida);
        equalizar(bloco, m);

        /* o volume anda em rampa ate o pedido: mexer no volume nao estala */
        float alvo = ganho_do_volume(dap_est.volume);
        for (int i = 0; i < m; i++) {
            ganho_atual += (alvo - ganho_atual) * 0.002f;
            for (int c2 = 0; c2 < 2; c2++) {
                float v = bloco[2 * i + c2] * ganho_atual;
                if (v > 0.9999999f) v = 0.9999999f;
                if (v < -1.0f) v = -1.0f;
                saida[2 * i + c2] = (int32_t)(v * 2147483647.0f);
            }
        }
        size_t escritos = 0;
        i2s_channel_write(canal, saida, (size_t)m * 2 * sizeof(int32_t), &escritos, portMAX_DELAY);
    }
}

void dap_audio_iniciar(void)
{
    comandos = xQueueCreate(16, sizeof(comando_t));
    dap_est.faixa = -1;
    if (!dap_est.volume) dap_est.volume = 18;
    ganho_atual = 0;
    xTaskCreatePinnedToCore(tarefa_audio, "dap_audio", 12288, NULL, 10, NULL, 1);
}

static void mandar(int tipo, int valor)
{
    comando_t c = { tipo, valor };
    if (comandos) xQueueSend(comandos, &c, pdMS_TO_TICKS(100));
}

void dap_tocar_fila(const int *faixas, int n, int pos, const char *nome)
{
    if (n <= 0) return;
    DAP_TRAVAR();
    int *nova = (int *)dap_alocar((size_t)n * sizeof(int));
    if (nova) {
        memcpy(nova, faixas, (size_t)n * sizeof(int));
        free(dap_est.fila);
        dap_est.fila = nova;
        dap_est.n_fila = n;
        dap_est.pos_fila = pos >= 0 && pos < n ? pos : 0;
        dap_est.faixa = dap_est.fila[dap_est.pos_fila];
        dap_est.repetiu = 0;
        snprintf(dap_est.nome_fila, sizeof(dap_est.nome_fila), "%s", nome ? nome : "");
    }
    DAP_SOLTAR();
    mandar(CMD_TOCAR, 0);
}

/* Tocar a faixa atual a partir de um ponto (a volta do desligar). */
void dap_retomar(int ms)
{
    if (dap_est.faixa >= 0) mandar(CMD_TOCAR, ms);
}

void dap_tocar_pausar(void)
{
    if (!dap_est.tocando) {
        if (dap_est.faixa >= 0) mandar(CMD_TOCAR, dap_posicao_atual_ms());
        return;
    }
    mandar(dap_est.pausado ? CMD_CONTINUAR : CMD_PAUSAR, 0);
}

void dap_proxima(int pelo_usuario)
{
    DAP_TRAVAR();
    int p = escolher_proxima(!pelo_usuario);
    if (p >= 0) dap_est.faixa = p;
    DAP_SOLTAR();
    if (p >= 0) mandar(CMD_TOCAR, 0);
}

void dap_anterior(void)
{
    /* passou de 3 s: volta ao comeco da mesma */
    if (dap_posicao_atual_ms() > 3000) { mandar(CMD_BUSCAR, 0); return; }
    DAP_TRAVAR();
    int p = -1;
    if (dap_est.modo == DAP_MODO_ALEATORIO || dap_est.modo == DAP_MODO_ALEATORIO_LISTA) {
        if (n_historia > 0) p = historia[--n_historia];
    } else if (dap_est.fila && dap_est.n_fila > 0) {
        dap_est.pos_fila = (dap_est.pos_fila - 1 + dap_est.n_fila) % dap_est.n_fila;
        p = dap_est.fila[dap_est.pos_fila];
    }
    if (p >= 0) dap_est.faixa = p;
    dap_est.repetiu = 0;
    DAP_SOLTAR();
    if (p >= 0) mandar(CMD_TOCAR, 0);
}

void dap_buscar_ms(int ms) { mandar(CMD_BUSCAR, ms); }
void dap_parar(void) { mandar(CMD_PARAR, 0); }

void dap_aplicar_eq(int preset)
{
    if (preset >= 0 && preset < DAP_PRESETS_EQ) dap_est.eq = preset;
}

void dap_mudar_volume(int delta)
{
    int v = dap_est.volume + delta;
    dap_est.volume = v < 0 ? 0 : v > 30 ? 30 : v;
}

int dap_posicao_atual_ms(void)
{
    return dap_est.info.taxa ? (int)(dap_est.quadros_tocados * 1000 / (uint64_t)dap_est.info.taxa) : 0;
}

int dap_duracao_atual_ms(void)
{
    if (dap_est.info.quadros && dap_est.info.taxa) return (int)(dap_est.info.quadros * 1000 / (uint64_t)dap_est.info.taxa);
    return dap_est.faixa >= 0 && dap_est.faixa < dap_n_faixas ? (int)dap_faixas_tab[dap_est.faixa].duracao_ms : 0;
}
