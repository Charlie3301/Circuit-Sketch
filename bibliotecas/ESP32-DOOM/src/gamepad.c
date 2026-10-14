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
// (doom_botoes), os botoes ficam no GND com o pull-up interno, e uma chave
// segura o "correr" enquanto estiver ligada (inclusive se ja estava ligada
// quando a placa acordou).

#include <stdlib.h>
#include <stdint.h>

#include "doomdef.h"
#include "doomtype.h"
#include "m_argv.h"
#include "d_event.h"
#include "g_game.h"
#include "d_main.h"
#include "gamepad.h"
#include "lprintf.h"

#include "freertos/FreeRTOS.h"
#include "freertos/task.h"
#include "freertos/queue.h"
#include "driver/gpio.h"
#include "doom_config.h"

// O gamepad e um teclado de mentira, mas o motor espera estas variaveis.
int usejoystick = 0;
int joyleft, joyright, joyup, joydown;

volatile int joyVal = 0;

typedef struct {
    int gpio;
    int *key;
} GPIOKeyMap;

// Pino -> tecla do DOOM. O tiro tambem e o "Enter" do menu.
static GPIOKeyMap keymap[12];

static void montar_mapa(void)
{
    int n = 0;
#define TECLA(pino, tecla) do { if ((pino) >= 0) { keymap[n].gpio = (pino); keymap[n].key = &(tecla); n++; } } while (0)
    TECLA(doom_config.cima, key_up);
    TECLA(doom_config.baixo, key_down);
    TECLA(doom_config.esquerda, key_left);
    TECLA(doom_config.direita, key_right);
    TECLA(doom_config.tiro, key_fire);
    TECLA(doom_config.tiro, key_menu_enter);
    TECLA(doom_config.usar, key_use);
    TECLA(doom_config.menu, key_escape);
    TECLA(doom_config.correr, key_speed);
#undef TECLA
    keymap[n].gpio = 0;
    keymap[n].key = NULL;
}

void gamepadPoll(void)
{
}

static QueueHandle_t gpio_evt_queue = NULL;

static void IRAM_ATTR gpio_isr_handler(void *arg)
{
    uint32_t gpio_num = (uint32_t)(uintptr_t)arg;
    xQueueSendFromISR(gpio_evt_queue, &gpio_num, NULL);
}

static void postar(int pino, int nivel)
{
    event_t ev;
    for (int i = 0; keymap[i].key != NULL; i++)
        if (keymap[i].gpio == pino) {
            ev.type = nivel ? ev_keyup : ev_keydown;   // no GND = apertado
            ev.data1 = *keymap[i].key;
            ev.data2 = ev.data3 = 0;
            D_PostEvent(&ev);
        }
}

static void gpioTask(void *arg)
{
    (void)arg;
    uint32_t io_num;
    for (;;)
        if (xQueueReceive(gpio_evt_queue, &io_num, portMAX_DELAY))
            postar((int)io_num, gpio_get_level((gpio_num_t)io_num));
}

void gamepadInit(void)
{
    lprintf(LO_INFO, "gamepadInit: Initializing game pad.\n");
}

void jsInit(void)
{
    montar_mapa();

    gpio_config_t io_conf;
    io_conf.pin_bit_mask = 0;
    for (int i = 0; keymap[i].key != NULL; i++) io_conf.pin_bit_mask |= (1ULL << keymap[i].gpio);
    if (io_conf.pin_bit_mask == 0) return;
    io_conf.intr_type = GPIO_INTR_ANYEDGE;
    io_conf.mode = GPIO_MODE_INPUT;
    io_conf.pull_up_en = 1;
    io_conf.pull_down_en = 0;
    gpio_config(&io_conf);

    gpio_evt_queue = xQueueCreate(16, sizeof(uint32_t));
    xTaskCreatePinnedToCore(&gpioTask, "GPIO", 3072, NULL, 7, NULL, 0);

    gpio_install_isr_service(0);
    for (int i = 0; keymap[i].key != NULL; i++) {
        int repetido = 0;
        for (int k = 0; k < i; k++) repetido |= keymap[k].gpio == keymap[i].gpio;
        if (!repetido) gpio_isr_handler_add((gpio_num_t)keymap[i].gpio, gpio_isr_handler, (void *)(uintptr_t)keymap[i].gpio);
    }

    // A chave que ja acordou ligada: o motor precisa saber.
    if (doom_config.correr >= 0 && gpio_get_level((gpio_num_t)doom_config.correr) == 0) postar(doom_config.correr, 0);

    lprintf(LO_INFO, "jsInit: GPIO task created.\n");
}
