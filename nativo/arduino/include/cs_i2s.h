/* cs_i2s.h -- a saida I2S do simulador: o som vai ao amplificador do circuito
 * (o MAX98357A nos pinos BCLK/LRC/DIN que o sketch deu). Comum ao I2SClass
 * (ESP_I2S.h), ao driver/i2s.h do ESP-IDF e ao Audio.h. */
#ifndef CS_I2S_H
#define CS_I2S_H

#include <stdint.h>
#include <stddef.h>
#include <stdbool.h>

#ifdef __cplusplus
extern "C" {
#endif

bool cs_i2s_abrir(uint32_t taxa, int bits, int canais, int bclk, int ws, int dout);
/* Escreve as amostras (intercaladas, little-endian) e espera o som tocar como
 * o DMA da placa: devolve os bytes aceitos. */
size_t cs_i2s_escrever(const void *dados, size_t n);
void cs_i2s_fechar(void);
bool cs_i2s_tocar_mp3(const uint8_t *dados, size_t n, int bclk, int ws, int dout);

#ifdef __cplusplus
}
#endif

#endif
