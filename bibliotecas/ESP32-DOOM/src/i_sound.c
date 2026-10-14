/* Emacs style mode select   -*- C++ -*-
 *-----------------------------------------------------------------------------
 *
 *
 *  PrBoom: a Doom port merged with LxDoom and LSDLDoom
 *  based on BOOM, a modified and improved DOOM engine
 *  Copyright (C) 1999 by
 *  id Software, Chi Hoang, Lee Killough, Jim Flynn, Rand Phares, Ty Halderman
 *  Copyright (C) 1999-2000 by
 *  Jess Haas, Nicolas Kalkhof, Colin Phipps, Florian Schulze
 *  Copyright 2005, 2006 by
 *  Florian Schulze, Colin Phipps, Neil Stevens, Andrey Budko
 *
 *  This program is free software; you can redistribute it and/or
 *  modify it under the terms of the GNU General Public License
 *  as published by the Free Software Foundation; either version 2
 *  of the License, or (at your option) any later version.
 *
 *  This program is distributed in the hope that it will be useful,
 *  but WITHOUT ANY WARRANTY; without even the implied warranty of
 *  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 *  GNU General Public License for more details.
 *
 *  You should have received a copy of the GNU General Public License
 *  along with this program; if not, write to the Free Software
 *  Foundation, Inc., 59 Temple Place - Suite 330, Boston, MA
 *  02111-1307, USA.
 *
 * DESCRIPTION:
 *  System interface for sound.
 *
 *  Modificado para o ESP32-S3 (CircuitSketch): o som sai pelo driver I2S
 *  novo do ESP-IDF 5 (i2s_std) para um amplificador MAX98357A, e nao mais
 *  pelo DAC interno do ESP32 classico, que o S3 nao tem. O mixer ganhou
 *  volume por canal, pula o cabecalho DMX dos efeitos e sabe quando um
 *  efeito acabou (I_SoundIsPlaying). Musica continua desligada.
 *
 *-----------------------------------------------------------------------------
 */

#include "freertos/FreeRTOS.h"
#include "freertos/task.h"
#include "driver/i2s_std.h"

#include "config.h"
#include <math.h>
#include <string.h>
#include <stdio.h>

#include "z_zone.h"

#include "m_swap.h"
#include "i_sound.h"
#include "m_argv.h"
#include "m_misc.h"
#include "w_wad.h"
#include "lprintf.h"
#include "s_sound.h"

#include "doomdef.h"
#include "doomstat.h"
#include "doomtype.h"

#include "d_main.h"
#include "dma.h"
#include "doom_config.h"

bool audioStarted = false;

int snd_card = 0;
int mus_card = 0;
int snd_samplerate = 11025;

#define TAXA 11025                // Hz, a dos efeitos do DOOM
#define AMOSTRAS_POR_BLOCO 256    // 23 ms por escrita no I2S
#define NUM_MIX_CHANNELS 8

// O tamanho de cada efeito (em amostras, sem o cabecalho DMX).
int lengths[NUMSFX];

typedef struct {
    const unsigned char *dados;   // NULL = canal livre
    uint32_t pos, fim;            // em 16.16
    uint32_t passo;
    int vol;                      // 0..127 (o s_sound.c manda snd_SfxVolume * 8)
    int id;
} canal_t;

static canal_t canais[NUM_MIX_CHANNELS];
static portMUX_TYPE trava_do_mixer = portMUX_INITIALIZER_UNLOCKED;
static i2s_chan_handle_t canal_tx;
static int16_t *mixbuffer;

// Um efeito do WAD. O lump DMX tem 8 bytes de cabecalho (formato, taxa,
// quantas amostras) e 16 de enchimento em cada ponta.
static void *getsfx(const char *sfxname, int *len, int *taxa)
{
    char name[20];
    int sfxlump;

    sprintf(name, "ds%s", sfxname);
    if (W_CheckNumForName(name) == -1) sfxlump = W_GetNumForName("dspistol");
    else sfxlump = W_GetNumForName(name);

    int size = W_LumpLength(sfxlump);
    const unsigned char *sfx = (const unsigned char *)W_CacheLumpNum(sfxlump);
    *taxa = TAXA;
    if (size > 8 + 32 && sfx[0] == 3 && sfx[1] == 0) {
        *taxa = sfx[2] | (sfx[3] << 8);
        int n = sfx[4] | (sfx[5] << 8) | (sfx[6] << 16) | (sfx[7] << 24);
        if (n > size - 8) n = size - 8;
        *len = n - 32;
        return (void *)(sfx + 8 + 16);
    }
    *len = size;
    return (void *)sfx;
}

static int taxas[NUMSFX];

void I_UpdateSoundParams(int handle, int volume, int seperation, int pitch)
{
    (void)seperation; (void)pitch;
    int c = handle % NUM_MIX_CHANNELS;
    if (c < 0) return;
    portENTER_CRITICAL(&trava_do_mixer);
    canais[c].vol = volume;
    portEXIT_CRITICAL(&trava_do_mixer);
}

void I_SetChannels(void)
{
}

int I_GetSfxLumpNum(sfxinfo_t *sfx)
{
    char namebuf[9];
    sprintf(namebuf, "ds%s", sfx->name);
    return W_GetNumForName(namebuf);
}

int I_StartSound(int id, int channel, int vol, int sep, int pitch, int priority)
{
    (void)sep; (void)pitch; (void)priority;
    if (!audioStarted || id <= 0 || id >= NUMSFX || !S_sfx[id].data || lengths[id] <= 0) return -1;
    int c = channel % NUM_MIX_CHANNELS;
    if (c < 0) c = 0;
    portENTER_CRITICAL(&trava_do_mixer);
    canais[c].dados = (const unsigned char *)S_sfx[id].data;
    canais[c].pos = 0;
    canais[c].fim = (uint32_t)lengths[id] << 16;
    canais[c].passo = ((uint32_t)taxas[id] << 16) / TAXA;
    canais[c].vol = vol;
    canais[c].id = id;
    portEXIT_CRITICAL(&trava_do_mixer);
    return c;
}

void I_StopSound(int handle)
{
    int c = handle % NUM_MIX_CHANNELS;
    if (c < 0) return;
    portENTER_CRITICAL(&trava_do_mixer);
    canais[c].dados = NULL;
    portEXIT_CRITICAL(&trava_do_mixer);
}

int I_SoundIsPlaying(int handle)
{
    int c = handle % NUM_MIX_CHANNELS;
    return c >= 0 && canais[c].dados != NULL;
}

int I_AnySoundStillPlaying(void)
{
    for (int c = 0; c < NUM_MIX_CHANNELS; c++)
        if (canais[c].dados) return true;
    return false;
}

// Mistura os canais num bloco de amostras de 16 bits (mono).
static void misturar(int16_t *saida, int n)
{
    portENTER_CRITICAL(&trava_do_mixer);
    for (int i = 0; i < n; i++) {
        int soma = 0;
        for (int c = 0; c < NUM_MIX_CHANNELS; c++) {
            canal_t *k = &canais[c];
            if (!k->dados) continue;
            // A mesma escala da tabela de volume do DOOM original: volume
            // 127 e o efeito em escala cheia (+-32512).
            int amostra = (int)k->dados[k->pos >> 16] - 128;
            soma += amostra * k->vol * 256 / 127;
            k->pos += k->passo;
            if (k->pos >= k->fim) k->dados = NULL;
        }
        if (soma > 32767) soma = 32767;
        if (soma < -32768) soma = -32768;
        saida[i] = (int16_t)soma;
    }
    portEXIT_CRITICAL(&trava_do_mixer);
}

void I_UpdateSound(void)
{
}

void I_ShutdownSound(void)
{
    if (canal_tx) {
        i2s_channel_disable(canal_tx);
        i2s_del_channel(canal_tx);
        canal_tx = NULL;
    }
    audioStarted = false;
}

static void updateTask(void *arg)
{
    (void)arg;
    size_t bytesWritten;
    while (1) {
        misturar(mixbuffer, AMOSTRAS_POR_BLOCO);
        i2s_channel_write(canal_tx, mixbuffer, AMOSTRAS_POR_BLOCO * sizeof(int16_t), &bytesWritten, portMAX_DELAY);
    }
}

void I_InitSound(void)
{
    if (doom_config.som_din < 0 || doom_config.som_bclk < 0 || doom_config.som_lrc < 0) {
        lprintf(LO_INFO, "I_InitSound: sem pinos de som (doom_som): o jogo fica mudo\n");
        return;
    }

    mixbuffer = malloc(AMOSTRAS_POR_BLOCO * sizeof(int16_t));
    memset(mixbuffer, 0, AMOSTRAS_POR_BLOCO * sizeof(int16_t));

    i2s_chan_config_t cc = I2S_CHANNEL_DEFAULT_CONFIG(I2S_NUM_AUTO, I2S_ROLE_MASTER);
    cc.auto_clear = true;
    if (i2s_new_channel(&cc, &canal_tx, NULL) != ESP_OK) {
        lprintf(LO_ERROR, "I_InitSound: sem canal I2S livre\n");
        return;
    }
    i2s_std_config_t std = {
        .clk_cfg = I2S_STD_CLK_DEFAULT_CONFIG(TAXA),
        .slot_cfg = I2S_STD_PHILIPS_SLOT_DEFAULT_CONFIG(I2S_DATA_BIT_WIDTH_16BIT, I2S_SLOT_MODE_MONO),
        .gpio_cfg = {
            .mclk = I2S_GPIO_UNUSED,
            .bclk = (gpio_num_t)doom_config.som_bclk,
            .ws = (gpio_num_t)doom_config.som_lrc,
            .dout = (gpio_num_t)doom_config.som_din,
            .din = I2S_GPIO_UNUSED,
            .invert_flags = { .mclk_inv = false, .bclk_inv = false, .ws_inv = false },
        },
    };
    if (i2s_channel_init_std_mode(canal_tx, &std) != ESP_OK || i2s_channel_enable(canal_tx) != ESP_OK) {
        lprintf(LO_ERROR, "I_InitSound: o I2S nao aceitou os pinos\n");
        return;
    }

    // Todos os efeitos do WAD de uma vez, na PSRAM.
    lprintf(LO_INFO, "I_InitSound: ");
    for (int i = 1; i < NUMSFX; i++) {
        if (!S_sfx[i].link) {
            S_sfx[i].data = getsfx(S_sfx[i].name, &lengths[i], &taxas[i]);
        } else {
            int ligado = (int)(S_sfx[i].link - S_sfx);
            S_sfx[i].data = S_sfx[i].link->data;
            lengths[i] = lengths[ligado];
            taxas[i] = taxas[ligado];
        }
    }
    lprintf(LO_INFO, " pre-cached all sound data\n");

    audioStarted = true;
    lprintf(LO_INFO, "I_InitSound: sound module ready (I2S %d Hz: BCLK %d, LRC %d, DIN %d)\n", TAXA,
            doom_config.som_bclk, doom_config.som_lrc, doom_config.som_din);

    xTaskCreatePinnedToCore(&updateTask, "updateTask", 3072, NULL, 6, NULL, 1);
}

void I_ShutdownMusic(void)
{
}

void I_InitMusic(void)
{
}

void I_PlaySong(int handle, int looping)
{
}

extern int mus_pause_opt;   // From m_misc.c

void I_PauseSong(int handle)
{
}

void I_ResumeSong(int handle)
{
}

void I_StopSong(int handle)
{
}

void I_UnRegisterSong(int handle)
{
}

int I_RegisterSong(const void *data, size_t len)
{
    return (0);
}

int I_RegisterMusic(const char *filename, musicinfo_t *song)
{
    return 1;
}

void I_SetMusicVolume(int volume)
{
}
