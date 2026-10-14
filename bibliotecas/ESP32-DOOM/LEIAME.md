# ESP32-DOOM (porte para a ESP32-S3 do CircuitSketch)

O DOOM de verdade -- o motor PrBoom 2.5.0 -- numa ESP32-S3 com PSRAM, com
display ILI9341, cartão microSD com o WAD, botões, som por I2S (MAX98357A) e a
tela no navegador. É uma biblioteca da Arduino IDE: roda na placa de verdade e,
no CircuitSketch, no modo nativo (o C compilado de verdade).

Este código vem do projeto **ESP32-DOOM**, de Amirhosein Masoumi
(<https://github.com/AmirhoseinMasoumi/ESP32-DOOM>), que partiu do
**esp32-doom** da Espressif, que partiu do **PrBoom 2.5.0**, que partiu do
código do **DOOM** que a id Software abriu. Os autores estão em `AUTHORS`.

O WAD (os dados do jogo) **não** vem junto: é da id Software. O `DOOM1.WAD`
do shareware serve.

## Como usar

```cpp
#include <doom_esp32.h>

void setup() {
  doom_barramento(11, 13, 12);           // MOSI, MISO, SCK
  doom_tela(10, 9, 8, 7);                // CS, DC, RST, luz de fundo (-1: sem)
  doom_cartao(14);                       // CS do cartão (-1: o WAD na partição "doom_wad")
  doom_botoes(4, 5, 6, 15, 16, 17, 18);  // cima, baixo, esquerda, direita, menu, atirar, usar
  doom_chave_correr(1);                  // chave para o GND (-1: sem)
  doom_som(40, 41, 42);                  // BCLK, LRC, DIN do MAX98357A (sem a chamada: mudo)
  doom_web(80);                          // a tela no navegador (depois do WiFi.begin)
  doom_iniciar();
}

void loop() { delay(1000); }
```

`doom_quadros()` devolve quantos quadros já foram desenhados.

Na Arduino IDE: placa **ESP32S3 Dev Module** (um N8R8), **Ferramentas >
PSRAM: "OPI PSRAM"**, **Partition Scheme: "Huge APP (3MB No OTA/1MB
SPIFFS)"**, e o WAD na raiz de um cartão microSD em FAT32.

## Licença

`COPYING` é o texto da GPL versão 2. O PrBoom é "GPL versão 2 ou (à sua
escolha) qualquer versão posterior". O `spi_lcd.c` e o `gamepad.c` vieram de
modelos da Espressif sob a Apache 2.0 (o cabeçalho de cada um diz). A Apache
2.0 é compatível com a GPL versão 3, e é nela que o conjunto pode ser
distribuído.

O `LICENSE.md` é o do projeto original e fala da organização dele (as pastas
`prboom/`, `prboom-esp32-compat/`, o `psxcontroller.c`, os scripts): aqui tudo
está em `src/`, e o que saiu está listado abaixo.

## O que mudou em relação ao ESP32-DOOM (24 e 25/09/2026)

Cada arquivo alterado tem, no cabeçalho, uma nota "Modificado ... (CircuitSketch)".

- **Organização**: as pastas `prboom/`, `prboom-esp32-compat/` e
  `prboom-wad-tables/` viraram a `src/` de uma biblioteca da Arduino IDE.
  Saíram o `prboom_main.c` (o `app_main` do ESP-IDF; numa biblioteca, o jogo
  começa pelo `doom_iniciar()`), o `psxcontroller.c` (controle de PlayStation),
  os scripts de compilação e o `doom_player.py`.
- **Novos**: `doom_esp32.c` e `doom_esp32.h` (a interface para o sketch: os
  pinos, o cartão, o WAD no cartão ou na partição `doom_wad`) e
  `doom_config.h`.
- **`spi_lcd.c`**: para o ILI9341 da ESP32-S3: os pinos do sketch, o SPI2
  (dividido com o cartão), a imagem de 320x200 esticada para 240 linhas (o
  original mandava 240 linhas de um quadro de 200 e lia fora do buffer), a
  luz de fundo.
- **`gamepad.c`**: botões nos pinos do sketch, com o pull-up interno, uma
  interrupção por borda e a chave do correr (inclusive já ligada ao acordar).
- **`i_system.c`**: o WAD por `FILE*` no cartão microSD (ou pela partição da
  flash); qualquer nome `.wad` vai para o IWAD achado; o `prboom.wad` é
  recusado.
- **`i_sound.c`**: reescrito para o driver I2S novo do ESP-IDF 5 (`i2s_std`) e
  um MAX98357A (a ESP32-S3 não tem o DAC interno do ESP32 clássico): 11025 Hz,
  mixer de 8 canais com o volume do DOOM, o cabeçalho DMX dos efeitos pulado.
  Música continua desligada.
- **`doom_stream.c`**: o WiFi é do sketch; o servidor abre na porta do
  `doom_web()`; o fluxo SSE roda numa tarefa própria
  (`httpd_req_async_handler_begin`) -- no original ele segurava a única tarefa
  do servidor, e as teclas do navegador nunca eram atendidas com a tela aberta;
  a página aceita WASD.
- **`i_video.c`**: o fluxo para o navegador sempre compilado (ele não faz nada
  sem servidor); a paleta converte as 256 cores (o original parava na 255).
- **32 arquivos**: o `#ifdef HAVE_CONFIG_H` em volta do `#include "config.h"`
  virou o `#include` direto (a Arduino IDE não passa `-DHAVE_CONFIG_H`).
- **`m_menu.c`**: as definições provisórias de vetor sem tamanho
  (`setup_menu_t gen_settings1[], ...;`) viraram `extern`, que é o que elas
  são: os vetores são definidos mais abaixo no mesmo arquivo.
- **`md5.c`**: sem mudança. O `MD5Init`, o `MD5Update` e o `MD5Final` que ele
  deixa de fora (`#if 0`) vêm da ROM da ESP32.
