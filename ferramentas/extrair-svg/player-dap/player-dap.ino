// Music player (DAP) with the ESP32-S3: microSD card and USB flash drive,
// portrait ILI9341 screen, click wheel (KY-040), five buttons, power
// switch, battery measured by the ADC and I2S sound on the MAX98357A.
//
// Plays MP3, FLAC, WAV, AIFF, OGG Vorbis, Opus, ALAC (M4A), WavPack and DSD
// (DSF and DFF); AAC, WMA and APE show up in the list, but do not play. It
// reads the tags and sorts the songs by artist, album, genre, album
// artist, year, composer and folder; makes playlists (.m3u8 in the card's
// /Playlists folder) and keeps the favorites.
//
// The wheel moves through the lists, and its click is OK. BACK goes back
// one screen (held: changes the repeat). MODE opens favorite, equalizer and
// play mode (held: favorites the song playing). PAUSE plays and pauses
// (held: stops). On the "Now playing" screen, the wheel changes song; a
// click on it makes the wheel seek back and forth within the song.
#include <player_dap.h>
#include "esp_sleep.h"

const int POWER = 1;      // power switch: LOW is on (10k to 3V3)
const int BATTERY = 2;    // the middle of the battery's 100k + 100k divider

void setup() {
  Serial.begin(115200);
  pinMode(POWER, INPUT_PULLUP);
  if (digitalRead(POWER) == HIGH) sleepUntil(0);   // woke up with the switch off

  int mv = battery();
  if (empty(mv)) {
    Serial.println("Battery empty: charge it, then switch off and on.");
    sleepUntil(1);
  }

  dap_barramento(11, 13, 12);       // MOSI, MISO and SCK: the screen and the card
  dap_tela(10, 9, 8, 7);            // CS, DC, RST and the screen backlight
  dap_cartao(14);                   // microSD CS
  dap_usb(1);                       // the flash drive on the S3's USB (GPIO 19 and 20)
  dap_som(40, 41, 42, 96000);       // BCLK, LRC, DIN and the DAC's highest rate
  dap_botoes(17, 18, 21, 38, 39);   // volume +, volume -, back, mode, pause
  dap_roda(4, 5, 6);                // CLK, DT and the wheel click
  dap_bateria(mv);
  dap_iniciar();
}

void loop() {
  int mv = battery();
  dap_bateria(mv);
  if (digitalRead(POWER) == HIGH) {
    dap_desligar();                 // saves where it stopped and turns off the screen
    sleepUntil(0);
  }
  if (empty(mv)) {
    Serial.println("Battery empty.");
    dap_desligar();
    sleepUntil(1);
  }
  delay(250);
}

// The divider cuts the battery voltage in half (4.2 V becomes 2.1 V).
int battery() {
  return analogReadMilliVolts(BATTERY) * 2;
}

// Below 3.3 V the lithium cell gets damaged. With no battery connected the
// reading is zero, and then the USB is what powers the board.
bool empty(int mv) {
  return mv > 1000 && mv < 3300;
}

// Sleeps until the switch goes to the requested level: 0 to turn on, or 1
// (empty battery) to only turn back on after switching off.
void sleepUntil(int level) {
  Serial.println(level == 0 ? "Off: the board sleeps until the switch turns on again."
                            : "The board sleeps until the switch turns off.");
  esp_sleep_enable_ext0_wakeup(GPIO_NUM_1, level);
  esp_deep_sleep_start();
}
