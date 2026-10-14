/* doom_esp32.h -- o DOOM (PrBoom 2.5.0) na ESP32-S3, como biblioteca do Arduino.
 *
 * Baseado no ESP32-DOOM de Amirhosein Masoumi (GPL-2.0), que vem do
 * esp32-doom da Espressif e do PrBoom. Esta versao roda no ESP32-S3 com
 * PSRAM, le o DOOM1.WAD do cartao microSD, desenha num ILI9341 de 320x240,
 * toca os efeitos por um amplificador I2S (MAX98357A) e ainda manda a tela
 * para o navegador pelo WiFi.
 *
 * Uso minimo, com os pinos de fabrica (os do circuito BASE ZERO):
 *
 *     #include <doom_esp32.h>
 *     void setup() { Serial.begin(115200); doom_iniciar(); }
 *     void loop() { delay(1000); }
 *
 * Cada doom_...() abaixo troca um grupo de pinos; chame antes do
 * doom_iniciar(). -1 quer dizer "nao tem".
 */
#ifndef DOOM_ESP32_H
#define DOOM_ESP32_H

#ifdef __cplusplus
extern "C" {
#endif

/* O barramento SPI que o display e o cartao dividem (padrao: 11, 13, 12). */
void doom_barramento(int mosi, int miso, int sck);

/* O display ILI9341: CS, D/C, RESET e a luz de fundo (padrao: 10, 9, 8, 7). */
void doom_tela(int cs, int dc, int rst, int luz);

/* O cartao microSD, com o DOOM1.WAD na raiz: o CS dele (padrao: 14). */
void doom_cartao(int cs);

/* Os botoes, ligados no GND (o pull-up e o interno): cima, baixo,
 * esquerda, direita, menu (START), tiro (A) e usar (B).
 * Padrao: 4, 5, 6, 15, 16, 17, 18. */
void doom_botoes(int cima, int baixo, int esquerda, int direita, int menu, int tiro, int usar);

/* Uma chave que liga "correr" (ligada = no GND). Padrao: 1. */
void doom_chave_correr(int pino);

/* O som por I2S: BCLK, LRC (WS) e DIN do MAX98357A. Padrao: sem som. */
void doom_som(int bclk, int lrc, int din);

/* O jogo no navegador: um servidor HTTP nesta porta, com a tela ao vivo e
 * o teclado. 0 = sem. O WiFi e do sketch: chame WiFi.begin() antes. */
void doom_web(int porta);

/* Liga tudo e comeca o jogo: cria as tarefas e volta. */
void doom_iniciar(void);

/* Quantos quadros ja foram desenhados. */
int doom_quadros(void);

#ifdef __cplusplus
}
#endif

#endif
