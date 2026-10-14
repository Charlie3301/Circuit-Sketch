// Copyright 2016-2017 Espressif Systems (Shanghai) PTE LTD
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
//
// Modificado para o ESP32-S3 (CircuitSketch): os pinos vem do sketch
// (doom_tela/doom_barramento), o barramento e o SPI2 (o VSPI do ESP32
// classico nao existe no S3) e ja foi iniciado pelo doom_iniciar, porque o
// cartao microSD divide o mesmo SPI; a imagem de 320x200 do DOOM e esticada
// para as 240 linhas do ILI9341 (o original mandava 240 linhas de um
// quadro de 200 e lia fora do buffer); a luz de fundo acende no fim.

#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <assert.h>
#include "freertos/FreeRTOS.h"
#include "freertos/task.h"
#include "freertos/semphr.h"
#include "esp_system.h"
#include "driver/spi_master.h"
#include "driver/gpio.h"
#include "esp_heap_caps.h"
#include "sdkconfig.h"
#include "doom_config.h"

#define LCD_WIDTH  320
#define LCD_HEIGHT 240

/* O DOOM desenha em 320x200 (a proporcao da VGA, com pixel "alto"):
 * esticar para 320x240 da justamente os 4:3 da tela original. */
#define DOOM_WIDTH  320
#define DOOM_HEIGHT 200

#define NO_SIM_TRANS 5            // transacoes na fila ao mesmo tempo
#define MEM_PER_TRANS LCD_WIDTH   // uma linha por transacao (em pixels)

typedef struct {
    uint8_t cmd;
    uint8_t data[16];
    uint8_t databytes;  // bit 7 = espera depois; 0xFF = fim
} ili_init_cmd_t;

// ILI9341, deitado (MADCTL 0x28: MV + BGR -- 320 colunas, 240 linhas).
static const ili_init_cmd_t ili_init_cmds[] = {
    {0xCF, {0x00, 0x83, 0X30}, 3},
    {0xED, {0x64, 0x03, 0X12, 0X81}, 4},
    {0xE8, {0x85, 0x01, 0x79}, 3},
    {0xCB, {0x39, 0x2C, 0x00, 0x34, 0x02}, 5},
    {0xF7, {0x20}, 1},
    {0xEA, {0x00, 0x00}, 2},
    {0xC0, {0x26}, 1},
    {0xC1, {0x11}, 1},
    {0xC5, {0x35, 0x3E}, 2},
    {0xC7, {0xBE}, 1},
    {0x36, {0x28}, 1},
    {0x3A, {0x55}, 1},
    {0xB1, {0x00, 0x1B}, 2},
    {0xF2, {0x08}, 1},
    {0x26, {0x01}, 1},
    {0xE0, {0x1F, 0x1A, 0x18, 0x0A, 0x0F, 0x06, 0x45, 0X87, 0x32, 0x0A, 0x07, 0x02, 0x07, 0x05, 0x00}, 15},
    {0XE1, {0x00, 0x25, 0x27, 0x05, 0x10, 0x09, 0x3A, 0x78, 0x4D, 0x05, 0x18, 0x0D, 0x38, 0x3A, 0x1F}, 15},
    {0x2A, {0x00, 0x00, 0x01, 0x3F}, 4},
    {0x2B, {0x00, 0x00, 0x00, 0xEF}, 4},
    {0x2C, {0}, 0},
    {0xB7, {0x07}, 1},
    {0xB6, {0x0A, 0x82, 0x27, 0x00}, 4},
    {0x11, {0}, 0x80},
    {0x29, {0}, 0x80},
    {0, {0}, 0xff},
};

static spi_device_handle_t spi;

volatile int doom_quadros_desenhados;

// Um comando para o ILI9341 (o D/C em LOW, pelo pre_cb).
void ili_cmd(spi_device_handle_t dev, const uint8_t cmd)
{
    spi_transaction_t t;
    memset(&t, 0, sizeof(t));
    t.length = 8;
    t.tx_buffer = &cmd;
    t.user = (void *)0;
    esp_err_t ret = spi_device_polling_transmit(dev, &t);
    assert(ret == ESP_OK);
}

// Os dados do comando (o D/C em HIGH).
void ili_data(spi_device_handle_t dev, const uint8_t *data, int len)
{
    if (len == 0) return;
    spi_transaction_t t;
    memset(&t, 0, sizeof(t));
    t.length = len * 8;
    t.tx_buffer = data;
    t.user = (void *)1;
    esp_err_t ret = spi_device_polling_transmit(dev, &t);
    assert(ret == ESP_OK);
}

// Antes de cada transacao: o D/C segue o campo user da transacao.
static void IRAM_ATTR ili_spi_pre_transfer_callback(spi_transaction_t *t)
{
    int dc = (int)(intptr_t)t->user;
    gpio_set_level((gpio_num_t)doom_config.tela_dc, dc);
}

static void ili_init(spi_device_handle_t dev)
{
    int cmd = 0;
    gpio_set_direction((gpio_num_t)doom_config.tela_dc, GPIO_MODE_OUTPUT);
    if (doom_config.tela_rst >= 0) {
        gpio_set_direction((gpio_num_t)doom_config.tela_rst, GPIO_MODE_OUTPUT);
        gpio_set_level((gpio_num_t)doom_config.tela_rst, 0);
        vTaskDelay(pdMS_TO_TICKS(100));
        gpio_set_level((gpio_num_t)doom_config.tela_rst, 1);
        vTaskDelay(pdMS_TO_TICKS(100));
    }

    while (ili_init_cmds[cmd].databytes != 0xff) {
        uint8_t dmdata[16];
        ili_cmd(dev, ili_init_cmds[cmd].cmd);
        memcpy(dmdata, ili_init_cmds[cmd].data, 16);
        ili_data(dev, dmdata, ili_init_cmds[cmd].databytes & 0x1F);
        if (ili_init_cmds[cmd].databytes & 0x80) vTaskDelay(pdMS_TO_TICKS(100));
        cmd++;
    }

    if (doom_config.tela_luz >= 0) {
        gpio_set_direction((gpio_num_t)doom_config.tela_luz, GPIO_MODE_OUTPUT);
        gpio_set_level((gpio_num_t)doom_config.tela_luz, 1);
    }
}

// A janela de escrita (CASET, PASET) e o RAMWR, na fila.
static void send_header_start(spi_device_handle_t dev, int xpos, int ypos, int w, int h)
{
    static spi_transaction_t trans[5];
    for (int x = 0; x < 5; x++) {
        memset(&trans[x], 0, sizeof(spi_transaction_t));
        if ((x & 1) == 0) {
            trans[x].length = 8;
            trans[x].user = (void *)0;
        } else {
            trans[x].length = 8 * 4;
            trans[x].user = (void *)1;
        }
        trans[x].flags = SPI_TRANS_USE_TXDATA;
    }
    trans[0].tx_data[0] = 0x2A;
    trans[1].tx_data[0] = xpos >> 8;
    trans[1].tx_data[1] = xpos & 0xff;
    trans[1].tx_data[2] = (xpos + w - 1) >> 8;
    trans[1].tx_data[3] = (xpos + w - 1) & 0xff;
    trans[2].tx_data[0] = 0x2B;
    trans[3].tx_data[0] = ypos >> 8;
    trans[3].tx_data[1] = ypos & 0xff;
    trans[3].tx_data[2] = (ypos + h - 1) >> 8;
    trans[3].tx_data[3] = (ypos + h - 1) & 0xff;
    trans[4].tx_data[0] = 0x2C;

    for (int x = 0; x < 5; x++) {
        esp_err_t ret = spi_device_queue_trans(dev, &trans[x], portMAX_DELAY);
        assert(ret == ESP_OK);
    }
}

static void send_header_cleanup(spi_device_handle_t dev)
{
    spi_transaction_t *rtrans;
    for (int x = 0; x < 5; x++) {
        esp_err_t ret = spi_device_get_trans_result(dev, &rtrans, portMAX_DELAY);
        assert(ret == ESP_OK);
    }
}

// O quadro que o display esta mandando: uma copia do do motor (8 bits por
// pixel, indice na paleta), para o motor seguir desenhando o proximo.
static uint8_t *currFbPtr = NULL;
SemaphoreHandle_t dispSem = NULL;
SemaphoreHandle_t dispDoneSem = NULL;

extern int16_t lcdpal[256];

static void displayTask(void *arg)
{
    (void)arg;
    int idx = 0;
    int inProgress = 0;
    static uint16_t *dmamem[NO_SIM_TRANS];
    spi_transaction_t trans[NO_SIM_TRANS];
    spi_transaction_t *rtrans;
    esp_err_t ret;

    printf("*** Display task starting.\n");
    ili_init(spi);

    for (int x = 0; x < NO_SIM_TRANS; x++) {
        dmamem[x] = heap_caps_malloc(MEM_PER_TRANS * 2, MALLOC_CAP_DMA);
        assert(dmamem[x]);
        memset(&trans[x], 0, sizeof(spi_transaction_t));
    }
    xSemaphoreGive(dispDoneSem);

    while (1) {
        xSemaphoreTake(dispSem, portMAX_DELAY);

        send_header_start(spi, 0, 0, LCD_WIDTH, LCD_HEIGHT);
        send_header_cleanup(spi);

        for (int y = 0; y < LCD_HEIGHT; y++) {
            // 240 linhas da tela a partir das 200 do quadro.
            const uint8_t *linha = currFbPtr + (y * DOOM_HEIGHT / LCD_HEIGHT) * DOOM_WIDTH;
            uint16_t *destino = dmamem[idx];
            for (int x = 0; x < LCD_WIDTH; x++) destino[x] = lcdpal[linha[x]];

            trans[idx].length = LCD_WIDTH * 16;
            trans[idx].user = (void *)1;
            trans[idx].tx_buffer = destino;
            ret = spi_device_queue_trans(spi, &trans[idx], portMAX_DELAY);
            assert(ret == ESP_OK);

            idx++;
            if (idx >= NO_SIM_TRANS) idx = 0;
            if (inProgress == NO_SIM_TRANS - 1) {
                ret = spi_device_get_trans_result(spi, &rtrans, portMAX_DELAY);
                assert(ret == ESP_OK);
            } else {
                inProgress++;
            }
        }
        while (inProgress) {
            ret = spi_device_get_trans_result(spi, &rtrans, portMAX_DELAY);
            assert(ret == ESP_OK);
            inProgress--;
        }
        doom_quadros_desenhados++;
    }
}

void spi_lcd_wait_finish(void)
{
}

void spi_lcd_send(uint16_t *scr)
{
    memcpy(currFbPtr, scr, DOOM_WIDTH * DOOM_HEIGHT);
    xSemaphoreGive(dispSem);
}

void spi_lcd_init(void)
{
    printf("spi_lcd_init(): ILI9341 %dx%d, CS %d, DC %d\n", LCD_WIDTH, LCD_HEIGHT, doom_config.tela_cs,
           doom_config.tela_dc);
    dispSem = xSemaphoreCreateBinary();
    dispDoneSem = xSemaphoreCreateBinary();
    currFbPtr = heap_caps_malloc(DOOM_WIDTH * DOOM_HEIGHT, MALLOC_CAP_32BIT);
    assert(currFbPtr);
    memset(currFbPtr, 0, DOOM_WIDTH * DOOM_HEIGHT);

    spi_device_interface_config_t devcfg = {
        .clock_speed_hz = 40 * 1000 * 1000,
        .mode = 0,
        .spics_io_num = doom_config.tela_cs,
        .queue_size = NO_SIM_TRANS + 2,
        .pre_cb = ili_spi_pre_transfer_callback,
    };
    esp_err_t ret = spi_bus_add_device(DOOM_SPI_HOST, &devcfg, &spi);
    assert(ret == ESP_OK);

    xTaskCreatePinnedToCore(&displayTask, "display", 6000, NULL, 6, NULL, 1);
}
