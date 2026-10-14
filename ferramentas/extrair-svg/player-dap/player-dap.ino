// Player de musica (DAP) com a ESP32-S3: cartao microSD e pendrive USB,
// tela ILI9341 em pe, roda com clique (KY-040), cinco botoes, chave de
// liga/desliga, bateria medida pelo ADC e som I2S no MAX98357A.
//
// Toca MP3, FLAC, WAV, AIFF, OGG Vorbis, Opus, ALAC (M4A), WavPack e DSD
// (DSF e DFF); AAC, WMA e APE aparecem na lista, mas nao tocam. Le as
// etiquetas e separa as musicas por artista, album, genero, artista do
// album, ano, compositor e pasta; faz playlists (.m3u8 na pasta /Playlists
// do cartao) e guarda as favoritas.
//
// A roda anda pelas listas, e o clique dela e o OK. VOLTAR volta uma tela
// (segurado: troca a repeticao). MODO abre favoritar, equalizador e modo de
// musica (segurado: favorita a que esta tocando). PAUSA toca e pausa
// (segurado: para). Na tela "Tocando", a roda troca de musica; um clique
// nela faz a roda avancar e voltar dentro da musica.
#include <player_dap.h>
#include "esp_sleep.h"

const int CHAVE = 1;      // liga/desliga: LOW e ligado (10k para o 3V3)
const int BATERIA = 2;    // o meio do divisor de 100k + 100k da bateria

void setup() {
  Serial.begin(115200);
  pinMode(CHAVE, INPUT_PULLUP);
  if (digitalRead(CHAVE) == HIGH) dormir(0);    // acordou com a chave desligada

  int mv = bateria();
  if (vazia(mv)) {
    Serial.println("Bateria vazia: carregue, desligue e ligue a chave.");
    dormir(1);
  }

  dap_barramento(11, 13, 12);       // MOSI, MISO e SCK: a tela e o cartao
  dap_tela(10, 9, 8, 7);            // CS, DC, RST e a luz da tela
  dap_cartao(14);                   // CS do microSD
  dap_usb(1);                       // o pendrive no USB da S3 (GPIO 19 e 20)
  dap_som(40, 41, 42, 96000);       // BCLK, LRC, DIN e a maior taxa do DAC
  dap_botoes(17, 18, 21, 38, 39);   // volume +, volume -, voltar, modo, pausa
  dap_roda(4, 5, 6);                // CLK, DT e o clique da roda
  dap_bateria(mv);
  dap_iniciar();
}

void loop() {
  int mv = bateria();
  dap_bateria(mv);
  if (digitalRead(CHAVE) == HIGH) {
    dap_desligar();                 // guarda onde parou e apaga a tela
    dormir(0);
  }
  if (vazia(mv)) {
    Serial.println("Bateria vazia.");
    dap_desligar();
    dormir(1);
  }
  delay(250);
}

// O divisor corta a tensao da bateria pela metade (4,2 V viram 2,1 V).
int bateria() {
  return analogReadMilliVolts(BATERIA) * 2;
}

// Abaixo de 3,3 V a pilha de litio se estraga. Sem bateria ligada a
// leitura e zero, e ai quem alimenta e o USB.
bool vazia(int mv) {
  return mv > 1000 && mv < 3300;
}

// Dorme ate a chave ir para o nivel pedido: 0 para ligar, ou 1 (bateria
// vazia) para so religar depois de desligar a chave.
void dormir(int nivel) {
  Serial.println(nivel == 0 ? "Desligado: a placa dorme ate a chave ligar de novo."
                            : "A placa dorme ate a chave desligar.");
  esp_sleep_enable_ext0_wakeup(GPIO_NUM_1, nivel);
  esp_deep_sleep_start();
}
