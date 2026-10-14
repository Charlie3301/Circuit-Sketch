/* dap_entrada.c -- os botoes e a roda viram eventos.
 *
 * Uma tarefa le os pinos a cada milissegundo. Botao: so conta depois de 5 ms
 * estavel (o repique do contato); solto antes de 600 ms e um toque, segurado
 * e o "longo" (uma vez). O volume repete enquanto esta apertado.
 *
 * A roda (KY-040) e decodificada pela tabela da quadratura: cada mudanca de
 * CLK/DT anda +1 ou -1, e um clique da roda e quando ela volta ao descanso
 * (os dois em HIGH, pelos resistores do modulo) depois de andar um ciclo.
 */
#include <string.h>
#include "freertos/FreeRTOS.h"
#include "freertos/task.h"
#include "freertos/queue.h"
#include "driver/gpio.h"
#include "esp_timer.h"
#include "dap_interno.h"

static QueueHandle_t fila;

typedef struct {
    int pino;
    dap_evento_t curto, longo;
    int repete;          /* volume: repete segurado */
    int nivel, estavel;  /* 1 = solto (pull-up) */
    int64_t desde;       /* quando mudou para o nivel atual */
    int64_t apertou;     /* quando o estavel virou "apertado" */
    int64_t ultima_repeticao;
    int disparou_longo;
} botao_t;

static botao_t botoes[6];
static int n_botoes;

static void mandar(dap_evento_t e)
{
    if (fila) xQueueSend(fila, &e, 0);
}

static void preparar(int pino)
{
    if (pino < 0) return;
    gpio_reset_pin((gpio_num_t)pino);
    gpio_set_direction((gpio_num_t)pino, GPIO_MODE_INPUT);
    gpio_set_pull_mode((gpio_num_t)pino, GPIO_PULLUP_ONLY);
}

static void novo_botao(int pino, dap_evento_t curto, dap_evento_t longo, int repete)
{
    if (pino < 0) return;
    preparar(pino);
    botao_t *b = &botoes[n_botoes++];
    memset(b, 0, sizeof(*b));
    b->pino = pino;
    b->curto = curto;
    b->longo = longo;
    b->repete = repete;
    b->nivel = b->estavel = 1;
}

static void olhar_botao(botao_t *b, int64_t agora)
{
    int n = gpio_get_level((gpio_num_t)b->pino);
    if (n != b->nivel) { b->nivel = n; b->desde = agora; }
    if (n != b->estavel && agora - b->desde >= 5000) {
        b->estavel = n;
        if (n == 0) {
            b->apertou = agora;
            b->disparou_longo = 0;
            b->ultima_repeticao = agora;
            if (b->repete) mandar(b->curto);   /* volume: responde ja no aperto */
        } else if (!b->disparou_longo && !b->repete) {
            mandar(b->curto);
        }
    }
    if (b->estavel == 0) {
        int64_t segurado = agora - b->apertou;
        if (b->repete) {
            if (segurado > 450000 && agora - b->ultima_repeticao >= 120000) {
                b->ultima_repeticao = agora;
                mandar(b->curto);
            }
        } else if (!b->disparou_longo && segurado >= 600000 && b->longo != EV_NADA) {
            b->disparou_longo = 1;
            mandar(b->longo);
        }
    }
}

/* A roda: a tabela de quadratura (estado antes * 4 + estado agora). */
static const int8_t passo_da_roda[16] = { 0, -1, 1, 0, 1, 0, 0, -1, -1, 0, 0, 1, 0, 1, -1, 0 };
static int roda_estado, roda_soma;

static void olhar_roda(void)
{
    if (dap_cfg.roda_clk < 0 || dap_cfg.roda_dt < 0) return;
    int agora = (gpio_get_level((gpio_num_t)dap_cfg.roda_clk) << 1) | gpio_get_level((gpio_num_t)dap_cfg.roda_dt);
    if (agora == roda_estado) return;
    roda_soma += passo_da_roda[(roda_estado << 2) | agora];
    roda_estado = agora;
    if (agora == 3) {   /* no descanso: um clique, se andou (quase) um ciclo */
        if (roda_soma >= 2) mandar(EV_GIRO_HORARIO);
        else if (roda_soma <= -2) mandar(EV_GIRO_ANTI);
        roda_soma = 0;
    }
}

static void tarefa_entrada(void *arg)
{
    (void)arg;
    for (;;) {
        int64_t agora = esp_timer_get_time();
        for (int i = 0; i < n_botoes; i++) olhar_botao(&botoes[i], agora);
        olhar_roda();
        vTaskDelay(1);
    }
}

void dap_entrada_iniciar(void)
{
    fila = xQueueCreate(32, sizeof(dap_evento_t));
    n_botoes = 0;
    novo_botao(dap_cfg.vol_mais, EV_VOL_MAIS, EV_NADA, 1);
    novo_botao(dap_cfg.vol_menos, EV_VOL_MENOS, EV_NADA, 1);
    novo_botao(dap_cfg.voltar, EV_VOLTAR, EV_VOLTAR_LONGO, 0);
    novo_botao(dap_cfg.modo, EV_MODO, EV_MODO_LONGO, 0);
    novo_botao(dap_cfg.pausa, EV_PAUSA, EV_PAUSA_LONGO, 0);
    novo_botao(dap_cfg.roda_sw, EV_OK, EV_OK_LONGO, 0);
    preparar(dap_cfg.roda_clk);
    preparar(dap_cfg.roda_dt);
    if (dap_cfg.roda_clk >= 0 && dap_cfg.roda_dt >= 0)
        roda_estado = (gpio_get_level((gpio_num_t)dap_cfg.roda_clk) << 1) | gpio_get_level((gpio_num_t)dap_cfg.roda_dt);
    xTaskCreatePinnedToCore(tarefa_entrada, "dap_entrada", 3072, NULL, 6, NULL, 0);
}

dap_evento_t dap_proximo_evento(int espera_ms)
{
    dap_evento_t e = EV_NADA;
    if (fila && xQueueReceive(fila, &e, pdMS_TO_TICKS(espera_ms)) == pdTRUE) return e;
    return EV_NADA;
}
