/* doom_config.h -- os pinos e as escolhas que o sketch fez (doom_esp32.h). */
#ifndef DOOM_CONFIG_H
#define DOOM_CONFIG_H

#include <stdint.h>

typedef struct {
    int mosi, miso, sck;
    int tela_cs, tela_dc, tela_rst, tela_luz;
    int cartao_cs;
    int cima, baixo, esquerda, direita, menu, tiro, usar;
    int correr;
    int som_bclk, som_lrc, som_din;
    int porta_web;
    char wad[64];           /* o caminho do WAD achado ("/sdcard/DOOM1.WAD"), ou "" */
    int wad_na_flash;       /* o WAD esta na particao "doom_wad" */
} doom_config_t;

extern doom_config_t doom_config;

/* O barramento do display e do cartao (SPI2 no ESP32-S3). */
#define DOOM_SPI_HOST SPI2_HOST

#endif
