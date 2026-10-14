/* doom_esp32.c -- o comeco de tudo: os pinos, o cartao, o WAD e as tarefas.
 *
 * Era o prboom_main.c do ESP32-DOOM (o app_main do ESP-IDF). Numa
 * biblioteca do Arduino o app_main e do core, entao o jogo comeca por
 * doom_iniciar(), chamado do setup().
 *
 * This program is free software; you can redistribute it and/or modify it
 * under the terms of the GNU General Public License as published by the
 * Free Software Foundation; either version 2 of the License, or (at your
 * option) any later version. See COPYING.
 */
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include "freertos/FreeRTOS.h"
#include "freertos/task.h"
#include "esp_err.h"
#include "esp_heap_caps.h"
#include "esp_psram.h"
#include "esp_partition.h"
#include "driver/spi_master.h"
#include "driver/gpio.h"
#include "driver/sdspi_host.h"
#include "esp_vfs_fat.h"
#include "sdmmc_cmd.h"
#include "doom_esp32.h"
#include "doom_config.h"

/* Os pinos de fabrica sao os do circuito BASE ZERO do simulador. */
doom_config_t doom_config = {
    .mosi = 11, .miso = 13, .sck = 12,
    .tela_cs = 10, .tela_dc = 9, .tela_rst = 8, .tela_luz = 7,
    .cartao_cs = 14,
    .cima = 4, .baixo = 5, .esquerda = 6, .direita = 15, .menu = 16, .tiro = 17, .usar = 18,
    .correr = 1,
    .som_bclk = -1, .som_lrc = -1, .som_din = -1,
    .porta_web = 0,
};

extern void jsInit(void);
extern int doom_main(int argc, char const *const *argv);
extern void spi_lcd_init(void);
extern void doom_stream_init(void);
extern void doom_stream_start_server(int porta);
extern volatile int doom_quadros_desenhados;

static int iniciado;

void doom_barramento(int mosi, int miso, int sck)
{
    doom_config.mosi = mosi;
    doom_config.miso = miso;
    doom_config.sck = sck;
}

void doom_tela(int cs, int dc, int rst, int luz)
{
    doom_config.tela_cs = cs;
    doom_config.tela_dc = dc;
    doom_config.tela_rst = rst;
    doom_config.tela_luz = luz;
}

void doom_cartao(int cs)
{
    doom_config.cartao_cs = cs;
}

void doom_botoes(int cima, int baixo, int esquerda, int direita, int menu, int tiro, int usar)
{
    doom_config.cima = cima;
    doom_config.baixo = baixo;
    doom_config.esquerda = esquerda;
    doom_config.direita = direita;
    doom_config.menu = menu;
    doom_config.tiro = tiro;
    doom_config.usar = usar;
}

void doom_chave_correr(int pino)
{
    doom_config.correr = pino;
}

void doom_som(int bclk, int lrc, int din)
{
    doom_config.som_bclk = bclk;
    doom_config.som_lrc = lrc;
    doom_config.som_din = din;
}

void doom_web(int porta)
{
    doom_config.porta_web = porta;
}

int doom_quadros(void)
{
    return doom_quadros_desenhados;
}

/* O IWAD na raiz do cartao: o shareware (DOOM1.WAD) ou um completo. */
static int wad_no_cartao(void)
{
    static const char *const nomes[] = {
        "DOOM1.WAD", "DOOM.WAD", "DOOMU.WAD", "DOOM2.WAD", "PLUTONIA.WAD", "TNT.WAD", "FREEDOOM.WAD", "FREEDM.WAD", NULL
    };
    for (int i = 0; nomes[i]; i++) {
        char caminho[64];
        snprintf(caminho, sizeof(caminho), "/sdcard/%s", nomes[i]);
        FILE *f = fopen(caminho, "rb");
        if (!f) continue;
        char id[4] = { 0 };
        size_t n = fread(id, 1, 4, f);
        fclose(f);
        if (n == 4 && memcmp(id, "IWAD", 4) == 0) {
            strcpy(doom_config.wad, caminho);
            return 1;
        }
        printf("DOOM: %s nao e um IWAD (comeca com \"%.4s\")\n", caminho, id);
    }
    return 0;
}

/* Ou numa particao "doom_wad" da flash, como no ESP32-DOOM original. */
static int wad_na_flash(void)
{
    const esp_partition_t *p = esp_partition_find_first(ESP_PARTITION_TYPE_DATA, ESP_PARTITION_SUBTYPE_ANY, "doom_wad");
    if (!p) return 0;
    char id[4];
    if (esp_partition_read(p, 0, id, 4) != ESP_OK || memcmp(id, "IWAD", 4) != 0) return 0;
    doom_config.wad_na_flash = 1;
    return 1;
}

static void doomEngineTask(void *arg)
{
    (void)arg;
    char const *argv[] = { "doom", "-cout", "ICWEFDA", NULL };
    doom_main(3, argv);
    printf("DOOM: o motor terminou\n");
    vTaskDelete(NULL);
}

void doom_iniciar(void)
{
    if (iniciado) return;
    iniciado = 1;

    printf("\n== DOOM para ESP32-S3 (PrBoom 2.5.0, ESP32-DOOM) ==\n");

    size_t psram = esp_psram_get_size();
    if (psram == 0) {
        printf("DOOM: esta placa esta sem PSRAM. O DOOM precisa de uns 4 MB: use um ESP32-S3 N8R8\n"
               "      e, na IDE, Ferramentas > PSRAM: \"OPI PSRAM\".\n");
        return;
    }
    printf("DOOM: PSRAM de %u MB, RAM interna livre: %u KB\n", (unsigned)(psram / (1024 * 1024)),
           (unsigned)(heap_caps_get_free_size(MALLOC_CAP_INTERNAL) / 1024));

    /* O display e o cartao dividem o mesmo barramento SPI. */
    spi_bus_config_t barramento = {
        .mosi_io_num = doom_config.mosi,
        .miso_io_num = doom_config.miso,
        .sclk_io_num = doom_config.sck,
        .quadwp_io_num = -1,
        .quadhd_io_num = -1,
        .max_transfer_sz = 320 * 2 * 8 + 16,
    };
    esp_err_t r = spi_bus_initialize(DOOM_SPI_HOST, &barramento, SPI_DMA_CH_AUTO);
    if (r != ESP_OK) {
        printf("DOOM: nao deu para iniciar o SPI (%s)\n", esp_err_to_name(r));
        return;
    }

    int achou = 0;
    if (doom_config.cartao_cs >= 0) {
        sdmmc_host_t host = SDSPI_HOST_DEFAULT();
        host.slot = DOOM_SPI_HOST;
        sdspi_device_config_t slot = SDSPI_DEVICE_CONFIG_DEFAULT();
        slot.gpio_cs = (gpio_num_t)doom_config.cartao_cs;
        slot.host_id = DOOM_SPI_HOST;
        esp_vfs_fat_mount_config_t montagem = {
            .format_if_mount_failed = false,
            .max_files = 4,
            .allocation_unit_size = 16 * 1024,
        };
        sdmmc_card_t *cartao = NULL;
        r = esp_vfs_fat_sdspi_mount("/sdcard", &host, &slot, &montagem, &cartao);
        if (r == ESP_OK) achou = wad_no_cartao();
        else printf("DOOM: o cartao microSD nao respondeu (%s). Confira o cartao e o CS no GPIO %d.\n",
                    esp_err_to_name(r), doom_config.cartao_cs);
    }
    if (!achou) achou = wad_na_flash();
    if (!achou) {
        printf("DOOM: nao achei o WAD do jogo. Copie o DOOM1.WAD (o shareware serve) para a raiz\n"
               "      do cartao microSD e reinicie a placa.\n");
        return;
    }
    printf("DOOM: WAD em %s\n", doom_config.wad_na_flash ? "flash (particao doom_wad)" : doom_config.wad);

    spi_lcd_init();
    jsInit();
    // As teclas pela serial (o "K" + codigo + estado do doom_player.py)
    // funcionam sempre; a tela no navegador, so com doom_web().
    doom_stream_init();
    if (doom_config.porta_web > 0) doom_stream_start_server(doom_config.porta_web);

    /* O motor numa tarefa com pilha grande, como no ESP32-DOOM. */
    xTaskCreatePinnedToCore(&doomEngineTask, "doomEngine", 32768, NULL, 5, NULL, 0);
}
