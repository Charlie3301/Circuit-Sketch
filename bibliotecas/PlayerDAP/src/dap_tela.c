/* dap_tela.c -- o ILI9341 em pe (240x320) e o quadro na memoria.
 *
 * Tudo e desenhado no quadro (na PSRAM) e so o retangulo que mudou vai para
 * o display: a tela inteira sao 150 KB, ~31 ms no SPI a 40 MHz; uma linha da
 * lista, menos de 2 ms. O quadro ja guarda o RGB565 na ordem do display (o
 * byte alto primeiro), e o envio copia por uma ponte na RAM interna, que e a
 * que o DMA do SPI alcanca.
 */
#include <string.h>
#include <stdio.h>
#include "freertos/FreeRTOS.h"
#include "freertos/task.h"
#include "driver/spi_master.h"
#include "driver/gpio.h"
#include "esp_heap_caps.h"
#include "esp_attr.h"
#include "dap_interno.h"

uint16_t *dap_quadro;

static spi_device_handle_t tela;
static uint16_t *ponte;
#define LINHAS_DA_PONTE 16

typedef struct {
    uint8_t cmd;
    uint8_t dados[15];
    uint8_t n;   /* bit 7: esperar 120 ms depois; 0xFF: fim */
} comando_t;

/* A partida do ILI9341. MADCTL 0x48: em pe (MX) e na ordem BGR do painel. */
static const comando_t partida[] = {
    {0xCF, {0x00, 0x83, 0x30}, 3},
    {0xED, {0x64, 0x03, 0x12, 0x81}, 4},
    {0xE8, {0x85, 0x01, 0x79}, 3},
    {0xCB, {0x39, 0x2C, 0x00, 0x34, 0x02}, 5},
    {0xF7, {0x20}, 1},
    {0xEA, {0x00, 0x00}, 2},
    {0xC0, {0x26}, 1},
    {0xC1, {0x11}, 1},
    {0xC5, {0x35, 0x3E}, 2},
    {0xC7, {0xBE}, 1},
    {0x36, {0x48}, 1},
    {0x3A, {0x55}, 1},
    {0xB1, {0x00, 0x1B}, 2},
    {0xF2, {0x08}, 1},
    {0x26, {0x01}, 1},
    {0xE0, {0x1F, 0x1A, 0x18, 0x0A, 0x0F, 0x06, 0x45, 0x87, 0x32, 0x0A, 0x07, 0x02, 0x07, 0x05, 0x00}, 15},
    {0xE1, {0x00, 0x25, 0x27, 0x05, 0x10, 0x09, 0x3A, 0x78, 0x4D, 0x05, 0x18, 0x0D, 0x38, 0x3A, 0x1F}, 15},
    {0xB7, {0x07}, 1},
    {0xB6, {0x0A, 0x82, 0x27, 0x00}, 4},
    {0x11, {0}, 0x80},
    {0x29, {0}, 0x80},
    {0, {0}, 0xFF},
};

/* Antes de cada transacao: o D/C segue o campo user (0 comando, 1 dado). */
static void IRAM_ATTR antes_de_mandar(spi_transaction_t *t)
{
    gpio_set_level((gpio_num_t)dap_cfg.tela_dc, (int)(intptr_t)t->user);
}

static void comando(uint8_t c)
{
    spi_transaction_t t;
    memset(&t, 0, sizeof(t));
    t.length = 8;
    t.tx_buffer = &c;
    t.user = (void *)0;
    spi_device_polling_transmit(tela, &t);
}

static void dados(const void *d, int n)
{
    if (n <= 0) return;
    spi_transaction_t t;
    memset(&t, 0, sizeof(t));
    t.length = n * 8;
    t.tx_buffer = d;
    t.user = (void *)1;
    spi_device_polling_transmit(tela, &t);
}

static void janela(int x0, int y0, int x1, int y1)
{
    uint8_t c[4] = { (uint8_t)(x0 >> 8), (uint8_t)x0, (uint8_t)(x1 >> 8), (uint8_t)x1 };
    uint8_t p[4] = { (uint8_t)(y0 >> 8), (uint8_t)y0, (uint8_t)(y1 >> 8), (uint8_t)y1 };
    comando(0x2A);
    dados(c, 4);
    comando(0x2B);
    dados(p, 4);
    comando(0x2C);
}

int dap_tela_iniciar(void)
{
    spi_device_interface_config_t dev;
    memset(&dev, 0, sizeof(dev));
    dev.clock_speed_hz = 40 * 1000 * 1000;
    dev.mode = 0;
    dev.spics_io_num = dap_cfg.tela_cs;
    dev.queue_size = 4;
    dev.pre_cb = antes_de_mandar;
    if (spi_bus_add_device(SPI2_HOST, &dev, &tela) != ESP_OK) {
        printf("DAP: o display nao entrou no SPI\n");
        return -1;
    }

    gpio_reset_pin((gpio_num_t)dap_cfg.tela_dc);
    gpio_set_direction((gpio_num_t)dap_cfg.tela_dc, GPIO_MODE_OUTPUT);
    if (dap_cfg.tela_luz >= 0) {
        gpio_reset_pin((gpio_num_t)dap_cfg.tela_luz);
        gpio_set_direction((gpio_num_t)dap_cfg.tela_luz, GPIO_MODE_OUTPUT);
        gpio_set_level((gpio_num_t)dap_cfg.tela_luz, 0);
    }
    if (dap_cfg.tela_rst >= 0) {
        gpio_reset_pin((gpio_num_t)dap_cfg.tela_rst);
        gpio_set_direction((gpio_num_t)dap_cfg.tela_rst, GPIO_MODE_OUTPUT);
        gpio_set_level((gpio_num_t)dap_cfg.tela_rst, 0);
        vTaskDelay(pdMS_TO_TICKS(10));
        gpio_set_level((gpio_num_t)dap_cfg.tela_rst, 1);
        vTaskDelay(pdMS_TO_TICKS(120));
    }

    for (int i = 0; partida[i].n != 0xFF; i++) {
        comando(partida[i].cmd);
        dados(partida[i].dados, partida[i].n & 0x1F);
        if (partida[i].n & 0x80) vTaskDelay(pdMS_TO_TICKS(120));
    }

    dap_quadro = (uint16_t *)dap_alocar(DAP_LARGURA * DAP_ALTURA * 2);
    ponte = (uint16_t *)heap_caps_malloc(DAP_LARGURA * LINHAS_DA_PONTE * 2, MALLOC_CAP_DMA | MALLOC_CAP_INTERNAL);
    if (!dap_quadro || !ponte) {
        printf("DAP: sem memoria para o quadro da tela\n");
        return -1;
    }
    memset(dap_quadro, 0, DAP_LARGURA * DAP_ALTURA * 2);
    dap_tela_mostrar(0, 0, DAP_LARGURA, DAP_ALTURA);
    return 0;
}

void dap_tela_mostrar(int x, int y, int l, int a)
{
    if (!tela || !dap_quadro) return;
    if (x < 0) { l += x; x = 0; }
    if (y < 0) { a += y; y = 0; }
    if (x + l > DAP_LARGURA) l = DAP_LARGURA - x;
    if (y + a > DAP_ALTURA) a = DAP_ALTURA - y;
    if (l <= 0 || a <= 0) return;

    janela(x, y, x + l - 1, y + a - 1);
    for (int linha = 0; linha < a;) {
        int n = a - linha < LINHAS_DA_PONTE ? a - linha : LINHAS_DA_PONTE;
        for (int k = 0; k < n; k++)
            memcpy(ponte + k * l, dap_quadro + (y + linha + k) * DAP_LARGURA + x, l * 2);
        dados(ponte, n * l * 2);
        linha += n;
    }
}

void dap_tela_luz(int ligada)
{
    if (dap_cfg.tela_luz >= 0) gpio_set_level((gpio_num_t)dap_cfg.tela_luz, ligada ? 1 : 0);
}

/* A placa vai dormir, mas o 3V3 continua ligado: o ILI9341 apaga a saida
 * (DISPOFF) e dorme (SLPIN), e gasta microamperes em vez de miliamperes. A
 * partida (dap_tela_iniciar) acorda ele de novo. */
void dap_tela_dormir(void)
{
    dap_tela_luz(0);
    comando(0x28);
    comando(0x10);
    vTaskDelay(pdMS_TO_TICKS(5));
}
