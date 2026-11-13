// ESP32-DOOM in CircuitSketch: the real DOOM -- the PrBoom 2.5.0 engine,
// from the ESP32-DOOM project -- on an ESP32-S3 with PSRAM, an ILI9341
// display, a microSD card, a MAX98357A I2S amplifier and a speaker.
//
// The game is the ESP32-DOOM library, written in C (GPL, source included):
// in the simulator it runs in native mode, really compiled, as on the board.
//
// BEFORE RUNNING: the card needs DOOM1.WAD (the shareware one works). Stop
// the simulation, double-click the card in the circuit and use "Load from
// the computer". The WAD is not included: it belongs to id Software.
//
// ON THE REAL BOARD (Arduino IDE, esp32 core 3.x):
//   - copy CircuitSketch's bibliotecas/ESP32-DOOM folder to
//     Documents/Arduino/libraries;
//   - Board: "ESP32S3 Dev Module" (an N8R8: 8 MB of flash and 8 of PSRAM);
//   - Tools > PSRAM: "OPI PSRAM";
//   - Tools > Partition Scheme: "Huge APP (3MB No OTA/1MB SPIFFS)";
//   - DOOM1.WAD in the root of a microSD card formatted as FAT32.
//
// Controls: the circuit's buttons (or the keyboard, or a game controller).
// "Fire" is also OK in the menus; the switch turns running on.

#include <WiFi.h>
#include <doom_esp32.h>

// The screen also goes to the browser if the board joins a network. On the
// real board, put yours here; in the simulator, any name connects.
const char *NETWORK = "CircuitSketch-WiFi";
const char *PASSWORD = "";

void setup() {
  Serial.begin(115200);

  // The circuit's pins -- the same as BASE ZERO.
  doom_barramento(11, 13, 12);           // MOSI, MISO, SCK (the display and the card share them)
  doom_tela(10, 9, 8, 7);                // CS, DC, RST, backlight
  doom_cartao(14);                       // card CS
  doom_botoes(4, 5, 6, 15, 16, 17, 18);  // up, down, left, right, menu, fire, use
  doom_chave_correr(1);
  doom_som(40, 41, 42);                  // MAX98357A BCLK, LRC, DIN

  WiFi.begin(NETWORK, PASSWORD);
  for (int i = 0; i < 50 && WiFi.status() != WL_CONNECTED; i++) delay(100);
  if (WiFi.status() == WL_CONNECTED) {
    Serial.print("The screen in the browser: http://");
    Serial.println(WiFi.localIP());
    doom_web(80);
  } else {
    Serial.println("No network: the game stays on the display only.");
  }

  doom_iniciar();
}

void loop() {
  // The game runs in the FreeRTOS tasks that doom_iniciar() created.
  delay(1000);
}
