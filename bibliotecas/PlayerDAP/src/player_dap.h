/* player_dap.h -- um DAP (tocador de audio digital) para a ESP32-S3.
 *
 * Toca MP3, FLAC, WAV, AIFF, OGG Vorbis e DSD (DSF e DFF) do cartao microSD
 * e de um pendrive na USB da S3, com a biblioteca separada por artista,
 * album, genero, artista do album, ano, compositor e pasta, playlists,
 * favoritas, equalizador e os modos de reproducao. A tela e um ILI9341 de
 * 240x320 em pe; os controles sao uma roda (encoder KY-040, com o botao do
 * meio) e cinco botoes: volume +, volume -, voltar (segurado: repetir e
 * aleatorio), modo e pausa. O som sai por I2S (MAX98357A ou PCM5102A).
 *
 * Uso minimo, com os pinos de fabrica:
 *
 *     #include <player_dap.h>
 *     void setup() { Serial.begin(115200); dap_iniciar(); }
 *     void loop() { delay(1000); }
 *
 * Cada dap_...() de pinos troca um grupo; chame antes do dap_iniciar().
 * -1 quer dizer "nao tem". A bateria e a chave de ligar sao do sketch: ele
 * le a tensao (dap_bateria) e, para desligar, chama dap_desligar() antes do
 * sono profundo -- assim a musica volta de onde parou.
 */
#ifndef PLAYER_DAP_H
#define PLAYER_DAP_H

#ifdef __cplusplus
extern "C" {
#endif

/* O SPI que a tela e o cartao dividem. Padrao: MOSI 11, MISO 13, SCK 12. */
void dap_barramento(int mosi, int miso, int sck);

/* A tela ILI9341: CS, D/C, RESET e a luz de fundo. Padrao: 10, 9, 8, 7. */
void dap_tela(int cs, int dc, int rst, int luz);

/* O cartao microSD: o CS. Padrao: 14. */
void dap_cartao(int cs);

/* O pendrive na USB da S3 (GPIO 19 e 20), lido como um segundo cartao. 1 liga (padrao), 0 desliga. */
void dap_usb(int ligado);

/* O som por I2S: BCLK, LRC (WS) e DIN. Padrao: 40, 41, 42.
 * A taxa maxima do DAC: 96000 no MAX98357A, 192000 no PCM5102A. */
void dap_som(int bclk, int lrc, int din, int taxa_maxima);

/* Os botoes, ligados no GND (pull-up interno). Padrao: 4, 5, 6, 15, 16. */
void dap_botoes(int volume_mais, int volume_menos, int voltar, int modo, int pausa);

/* A roda: CLK, DT e o botao do meio (SW) de um KY-040. Padrao: 17, 18, 21. */
void dap_roda(int clk, int dt, int sw);

/* Liga tudo: monta o cartao, le a biblioteca e cria as tarefas. Volta logo. */
void dap_iniciar(void);

/* A tensao da bateria, em milivolts (na celula, ja sem o divisor). */
void dap_bateria(int milivolts);

/* Vai desligar: guarda onde parou, para o som e apaga a tela. */
void dap_desligar(void);

/* Para o sketch e para os testes. */
int dap_tocando(void);              /* 1 tocando, 0 pausado ou parado */
int dap_volume(void);               /* 0..30 */
int dap_faixas(void);               /* quantas musicas a biblioteca tem */
const char *dap_titulo(void);       /* o titulo da faixa atual ("" se nenhuma) */
int dap_posicao_ms(void);           /* onde a faixa atual esta */

#ifdef __cplusplus
}
#endif

#endif
