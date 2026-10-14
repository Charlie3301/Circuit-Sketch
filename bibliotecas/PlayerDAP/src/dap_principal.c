/* dap_principal.c -- os pinos, a partida e o que o sketch chama.
 *
 * O dap_iniciar() so liga o SPI e cria a tarefa da interface: ela monta o
 * cartao (e o pendrive), le a biblioteca e cria as tarefas do audio e dos
 * botoes. Assim o setup() do sketch volta na hora.
 */
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include "freertos/FreeRTOS.h"
#include "freertos/task.h"
#include "freertos/semphr.h"
#include "esp_heap_caps.h"
#include "driver/spi_master.h"
#include "driver/sdspi_host.h"
#include "esp_vfs_fat.h"
#include "sdmmc_cmd.h"
#include "player_dap.h"
#include "dap_interno.h"

dap_config_t dap_cfg = {
    .mosi = 11, .miso = 13, .sck = 12,
    .tela_cs = 10, .tela_dc = 9, .tela_rst = 8, .tela_luz = 7,
    .cartao_cs = 14,
    .usb = 1,
    .som_bclk = 40, .som_lrc = 41, .som_din = 42, .som_taxa_maxima = 96000,
    .vol_mais = 4, .vol_menos = 5, .voltar = 6, .modo = 15, .pausa = 16,
    .roda_clk = 17, .roda_dt = 18, .roda_sw = 21,
};

SemaphoreHandle_t dap_trava;
int dap_cartao_ok, dap_usb_ok;
static int iniciado;

void *dap_alocar(size_t n)
{
    void *p = heap_caps_malloc(n ? n : 1, MALLOC_CAP_SPIRAM | MALLOC_CAP_8BIT);
    return p ? p : malloc(n ? n : 1);
}

void *dap_realocar(void *p, size_t n)
{
    void *q = heap_caps_realloc(p, n ? n : 1, MALLOC_CAP_SPIRAM | MALLOC_CAP_8BIT);
    return q ? q : realloc(p, n ? n : 1);
}

void dap_barramento(int mosi, int miso, int sck) { dap_cfg.mosi = mosi; dap_cfg.miso = miso; dap_cfg.sck = sck; }
void dap_tela(int cs, int dc, int rst, int luz) { dap_cfg.tela_cs = cs; dap_cfg.tela_dc = dc; dap_cfg.tela_rst = rst; dap_cfg.tela_luz = luz; }
void dap_cartao(int cs) { dap_cfg.cartao_cs = cs; }
void dap_usb(int ligado) { dap_cfg.usb = ligado; }

void dap_som(int bclk, int lrc, int din, int taxa_maxima)
{
    dap_cfg.som_bclk = bclk;
    dap_cfg.som_lrc = lrc;
    dap_cfg.som_din = din;
    dap_cfg.som_taxa_maxima = taxa_maxima > 0 ? taxa_maxima : 96000;
}

void dap_botoes(int volume_mais, int volume_menos, int voltar, int modo, int pausa)
{
    dap_cfg.vol_mais = volume_mais;
    dap_cfg.vol_menos = volume_menos;
    dap_cfg.voltar = voltar;
    dap_cfg.modo = modo;
    dap_cfg.pausa = pausa;
}

void dap_roda(int clk, int dt, int sw) { dap_cfg.roda_clk = clk; dap_cfg.roda_dt = dt; dap_cfg.roda_sw = sw; }

/* O cartao no SPI que ele divide com a tela. */
int dap_montar_cartao(void)
{
    if (dap_cartao_ok || dap_cfg.cartao_cs < 0) return dap_cartao_ok;
    sdmmc_host_t host = SDSPI_HOST_DEFAULT();
    host.slot = SPI2_HOST;
    host.max_freq_khz = 20000;
    sdspi_device_config_t slot = SDSPI_DEVICE_CONFIG_DEFAULT();
    slot.gpio_cs = (gpio_num_t)dap_cfg.cartao_cs;
    slot.host_id = SPI2_HOST;
    esp_vfs_fat_mount_config_t montagem = {
        .format_if_mount_failed = false,
        .max_files = 6,
        .allocation_unit_size = 16 * 1024,
    };
    sdmmc_card_t *cartao = NULL;
    esp_err_t r = esp_vfs_fat_sdspi_mount(DAP_RAIZ_CARTAO, &host, &slot, &montagem, &cartao);
    dap_cartao_ok = r == ESP_OK;
    if (!dap_cartao_ok)
        printf("DAP: o cartao microSD nao respondeu (%s). Confira o cartao e o CS no GPIO %d.\n", esp_err_to_name(r), dap_cfg.cartao_cs);
    return dap_cartao_ok;
}

void dap_iniciar(void)
{
    if (iniciado) return;
    iniciado = 1;
    printf("\n== PlayerDAP (ESP32-S3) ==\n");
    dap_trava = xSemaphoreCreateMutex();

    spi_bus_config_t barramento = {
        .mosi_io_num = dap_cfg.mosi,
        .miso_io_num = dap_cfg.miso,
        .sclk_io_num = dap_cfg.sck,
        .quadwp_io_num = -1,
        .quadhd_io_num = -1,
        .max_transfer_sz = DAP_LARGURA * 2 * 16 + 16,
    };
    esp_err_t r = spi_bus_initialize(SPI2_HOST, &barramento, SPI_DMA_CH_AUTO);
    if (r != ESP_OK) {
        printf("DAP: nao deu para iniciar o SPI (%s)\n", esp_err_to_name(r));
        return;
    }
    dap_interface_iniciar();
}

void dap_bateria(int milivolts) { dap_bateria_mv = milivolts; }

void dap_desligar(void)
{
    if (!iniciado) return;
    dap_desligando = 1;
    /* espera a interface guardar o estado e apagar a tela (ate 2 s) */
    for (int i = 0; i < 100 && dap_desligando != 2; i++) vTaskDelay(pdMS_TO_TICKS(20));
}

int dap_tocando(void) { return dap_est.tocando && !dap_est.pausado; }
int dap_volume(void) { return dap_est.volume; }
int dap_faixas(void) { return dap_varredura_pronta ? dap_n_faixas : 0; }
int dap_posicao_ms(void) { return dap_posicao_atual_ms(); }

const char *dap_titulo(void)
{
    int f = dap_est.faixa;
    return f >= 0 && f < dap_n_faixas ? dap_txt(dap_faixas_tab[f].titulo) : "";
}
