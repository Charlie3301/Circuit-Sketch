// ESP32-DOOM no CircuitSketch: o DOOM de verdade -- o motor PrBoom 2.5.0,
// do projeto ESP32-DOOM -- numa ESP32-S3 com PSRAM, display ILI9341, cartao
// microSD, amplificador I2S MAX98357A e alto-falante.
//
// O jogo e a biblioteca ESP32-DOOM, escrita em C (GPL, com o codigo junto):
// no simulador ela roda no modo nativo, compilada de verdade, como na placa.
//
// ANTES DE RODAR: o cartao precisa do DOOM1.WAD (o shareware serve). Pare a
// simulacao, clique duas vezes no cartao do circuito e use "Carregar do
// computador". O WAD nao vem junto: e da id Software.
//
// NA PLACA DE VERDADE (Arduino IDE, core esp32 3.x):
//   - copie a pasta bibliotecas/ESP32-DOOM do CircuitSketch para
//     Documentos/Arduino/libraries;
//   - Placa: "ESP32S3 Dev Module" (um N8R8: 8 MB de flash e 8 de PSRAM);
//   - Ferramentas > PSRAM: "OPI PSRAM";
//   - Ferramentas > Partition Scheme: "Huge APP (3MB No OTA/1MB SPIFFS)";
//   - o DOOM1.WAD na raiz de um cartao microSD formatado em FAT32.
//
// Controles: os botoes do circuito (ou o teclado, ou um controle de video
// game). O "atirar" tambem e o OK dos menus; a chave liga o correr.

#include <WiFi.h>
#include <doom_esp32.h>

// A tela tambem vai para o navegador, se a placa entrar numa rede. Na placa
// de verdade, ponha aqui a sua; no simulador, qualquer nome conecta.
const char *REDE = "CircuitSketch-WiFi";
const char *SENHA = "";

void setup() {
  Serial.begin(115200);

  // Os pinos do circuito -- os mesmos da BASE ZERO.
  doom_barramento(11, 13, 12);           // MOSI, MISO, SCK (o display e o cartao dividem)
  doom_tela(10, 9, 8, 7);                // CS, DC, RST, luz de fundo
  doom_cartao(14);                       // CS do cartao
  doom_botoes(4, 5, 6, 15, 16, 17, 18);  // cima, baixo, esquerda, direita, menu, atirar, usar
  doom_chave_correr(1);
  doom_som(40, 41, 42);                  // BCLK, LRC, DIN do MAX98357A

  WiFi.begin(REDE, SENHA);
  for (int i = 0; i < 50 && WiFi.status() != WL_CONNECTED; i++) delay(100);
  if (WiFi.status() == WL_CONNECTED) {
    Serial.print("A tela no navegador: http://");
    Serial.println(WiFi.localIP());
    doom_web(80);
  } else {
    Serial.println("Sem rede: o jogo fica so no display.");
  }

  doom_iniciar();
}

void loop() {
  // O jogo roda nas tarefas do FreeRTOS que o doom_iniciar() criou.
  delay(1000);
}
