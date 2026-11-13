"""python ferramentas/extrair-svg/demos-alpha19.py

As demos novas da Alpha 19.

Cada uma mostra uma coisa que o CircuitSketch faz e que um pisca-pisca
nao mostra. O codigo e o que se escreve para a placa de verdade -- o
mesmo dos tutoriais que a turma copia --, porque demo que so roda aqui
nao ensina nada: o aluno monta na bancada e nao funciona.

  esp32-bluetooth     Bluetooth: comandos do celular acendem o LED RGB,
                      tocam o buzzer e pedem a leitura do DHT22; o LCD
                      mostra o ultimo comando.
  esp32-espnow        ESP-NOW: duas ESP32 sem roteador. Uma le sensor,
                      potenciometro e botao; a outra mostra no LCD, move
                      o servo e acende o LED. O botao do painel acende o
                      LED do sensor -- a volta tambem existe.
"""
import io
import json

BASE = 'C:/Users/seraf/OneDrive/Desktop/SimuladorESP32/'
PASTA = BASE + 'app/assets/demos/'

PRETO = '#1A1A1A'
VERMELHO = '#D6202A'
VERDE = '#00A651'
AZUL = '#1F6FEB'
AMARELO = '#F2C300'
LARANJA = '#F28C28'
ROXO = '#8E44AD'
BRANCO = '#E8E8E8'

# O DHT22 le de -40 a 80 C, e o controle da peca vai de 0 a 1: comecar
# no zero e comecar em -40 graus. 24 graus e uma sala.
TEMPERATURA_24 = (24 - (-40)) / 120


def fio(de, pino_de, para, pino_para, cor):
    return {'DePeca': de, 'DePino': pino_de, 'ParaPeca': para, 'ParaPino': pino_para,
            'Cor': cor, 'Estilo': 'solto'}


def gravar(recurso, codigo, pecas, fios, codigos_por_placa=None):
    """Grava o .sktch. Com mais de uma placa, cada uma leva o seu codigo."""
    if codigos_por_placa:
        for p in pecas:
            if p['Id'] in codigos_por_placa:
                p['Codigo'] = codigos_por_placa[p['Id']].strip('\n').split('\n')

    projeto = {
        'formato': 'sktch',
        'versao': 1,
        'gerado': 'demo do CircuitSketch',
        'codigo': codigo.strip('\n').split('\n'),
        'circuito': {'Pecas': pecas, 'Fios': fios},
    }
    io.open(PASTA + recurso + '.sktch', 'w', encoding='utf-8', newline='').write(
        json.dumps(projeto, ensure_ascii=False, indent=1))
    print(f'{recurso}.sktch')


# =====================================================================
#  Bluetooth
# =====================================================================

BLUETOOTH = r'''
// Bluetooth control, on the ESP32.
//
// In the BLUETOOTH tab, click Connect and send a command:
//   red, green, blue, white, off  -> RGB LED color
//   beep                          -> sounds the buzzer
//   temp                          -> answers temperature and humidity
//   help                          -> lists the commands
//
// A real phone works too: with the sketch running, the board opens a port
// on 127.0.0.1 (the panel says which). Any TCP terminal plays the part of
// the "Serial Bluetooth Terminal" app, byte by byte.

#include <BluetoothSerial.h>
#include <DHT.h>
#include <Wire.h>
#include <LiquidCrystal_I2C.h>

BluetoothSerial SerialBT;
DHT dht(4, DHT22);
LiquidCrystal_I2C lcd(0x27, 16, 2);

const int PIN_R = 25;
const int PIN_G = 26;
const int PIN_B = 27;
const int BUZZER = 14;

unsigned long lastReport = 0;

void color(int r, int g, int b) {
  analogWrite(PIN_R, r);
  analogWrite(PIN_G, g);
  analogWrite(PIN_B, b);
}

void show(String line1, String line2) {
  lcd.clear();
  lcd.setCursor(0, 0);
  lcd.print(line1);
  lcd.setCursor(0, 1);
  lcd.print(line2);
}

void setup() {
  Serial.begin(115200);
  SerialBT.begin("ESP32-Room");   // the name the phone shows
  dht.begin();
  lcd.init();
  lcd.backlight();
  pinMode(BUZZER, OUTPUT);
  color(0, 0, 0);
  show("Bluetooth ready", "ESP32-Room");
  Serial.println("Waiting for the phone: BLUETOOTH tab > Connect");
}

void loop() {
  if (SerialBT.available()) {
    String command = SerialBT.readStringUntil('\n');
    command.trim();
    command.toLowerCase();
    Serial.println("got: " + command);

    if (command == "red") {
      color(255, 0, 0);
      SerialBT.println("LED red");
    } else if (command == "green") {
      color(0, 255, 0);
      SerialBT.println("LED green");
    } else if (command == "blue") {
      color(0, 0, 255);
      SerialBT.println("LED blue");
    } else if (command == "white") {
      color(255, 255, 255);
      SerialBT.println("LED white");
    } else if (command == "off") {
      color(0, 0, 0);
      SerialBT.println("LED off");
    } else if (command == "beep") {
      tone(BUZZER, 880, 200);
      SerialBT.println("beep!");
    } else if (command == "temp") {
      SerialBT.print("temperature: ");
      SerialBT.print(dht.readTemperature(), 1);
      SerialBT.println(" C");
      SerialBT.print("humidity: ");
      SerialBT.print(dht.readHumidity(), 0);
      SerialBT.println(" %");
    } else if (command == "help") {
      SerialBT.println("commands: red, green, blue, white, off, beep, temp");
    } else {
      SerialBT.println("unknown '" + command + "'. Send: help");
    }

    show("Last command:", command);
  }

  // Every 5 s the board sends the temperature on its own, like a sensor
  // would -- but only if someone is connected to listen.
  if (millis() - lastReport > 5000) {
    lastReport = millis();
    if (SerialBT.hasClient()) {
      SerialBT.print("[auto] ");
      SerialBT.print(dht.readTemperature(), 1);
      SerialBT.println(" C");
    }
  }
}
'''

gravar('esp32-bluetooth', BLUETOOTH,
       pecas=[
           {'Id': 'placa', 'Tipo': 'esp32-devkit-v1', 'X': 330, 'Y': 90},
           {'Id': 'rgb', 'Tipo': 'rgb-led', 'X': 110, 'Y': 110},
           {'Id': 'rr', 'Tipo': 'resistor', 'X': 190, 'Y': 150, 'Ohms': 220},
           {'Id': 'rg', 'Tipo': 'resistor', 'X': 190, 'Y': 175, 'Ohms': 220},
           {'Id': 'rb', 'Tipo': 'resistor', 'X': 190, 'Y': 200, 'Ohms': 220},
           {'Id': 'buzzer', 'Tipo': 'buzzer', 'X': 150, 'Y': 250},
           {'Id': 'dht', 'Tipo': 'dht22', 'X': 520, 'Y': 110, 'Giro': TEMPERATURA_24},
           {'Id': 'lcd', 'Tipo': 'lcd1602-i2c', 'X': 480, 'Y': 290},
       ],
       fios=[
           fio('placa', 'D25', 'rr', '2', VERMELHO),
           fio('rr', '1', 'rgb', 'R', VERMELHO),
           fio('placa', 'D26', 'rg', '2', VERDE),
           fio('rg', '1', 'rgb', 'G', VERDE),
           fio('placa', 'D27', 'rb', '2', AZUL),
           fio('rb', '1', 'rgb', 'B', AZUL),
           fio('rgb', 'COM', 'placa', 'GND.2', PRETO),
           fio('placa', 'D14', 'buzzer', '2', LARANJA),
           fio('buzzer', '1', 'placa', 'GND.2', PRETO),
           fio('dht', 'VCC', 'placa', '3V3', VERMELHO),
           fio('dht', 'SDA', 'placa', 'D4', AMARELO),
           fio('dht', 'GND', 'placa', 'GND.1', PRETO),
           fio('lcd', 'GND', 'placa', 'GND.1', PRETO),
           fio('lcd', 'VCC', 'placa', 'VIN', VERMELHO),
           fio('lcd', 'SDA', 'placa', 'D21', AZUL),
           fio('lcd', 'SCL', 'placa', 'D22', ROXO),
       ])


# =====================================================================
#  ESP-NOW, duas placas
# =====================================================================

SENSOR = r'''
// ESP-NOW: the SENSOR board.
//
// Reads the DHT22, the potentiometer and the button and sends it all to the
// PANEL board every half second -- no router and no WiFi connection:
// ESP-NOW talks straight from board to board. The way back exists too: the
// panel's button turns this board's LED on and off.
//
// (In version 2.x of the ESP32 package, onReceive gets
// "const uint8_t *mac" instead of esp_now_recv_info_t.)

#include <esp_now.h>
#include <WiFi.h>
#include <DHT.h>

const int DHT_PIN = 4;
const int POT_PIN = 34;
const int BUTTON_PIN = 13;
const int LED_PIN = 27;

DHT dht(DHT_PIN, DHT22);

// Broadcast address: every board with ESP-NOW listens. On the real bench,
// you can swap it for the other board's MAC (the serial monitor shows it).
uint8_t toEveryone[] = {0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF};

// The SAME struct on both boards: what leaves one is copied byte by byte
// into the other.
typedef struct {
  float temperature;
  float humidity;
  int potentiometer;   // 0 to 4095
  bool button;
} Reading;

typedef struct {
  bool ledOn;
} Command;

Reading reading;
Command command;
esp_now_peer_info_t peer;

void onSend(const uint8_t *mac, esp_now_send_status_t status) {
  if (status != ESP_NOW_SEND_SUCCESS) Serial.println("the panel did not hear");
}

void onReceive(const esp_now_recv_info_t *info, const uint8_t *data, int length) {
  memcpy(&command, data, sizeof(command));
  digitalWrite(LED_PIN, command.ledOn ? HIGH : LOW);
  Serial.println(command.ledOn ? "the panel turned the LED on" : "the panel turned the LED off");
}

void setup() {
  Serial.begin(115200);
  pinMode(BUTTON_PIN, INPUT_PULLUP);
  pinMode(LED_PIN, OUTPUT);
  dht.begin();

  WiFi.mode(WIFI_STA);
  Serial.print("sensor MAC: ");
  Serial.println(WiFi.macAddress());

  if (esp_now_init() != ESP_OK) {
    Serial.println("ESP-NOW did not start");
    return;
  }
  esp_now_register_send_cb(onSend);
  esp_now_register_recv_cb(onReceive);

  memcpy(peer.peer_addr, toEveryone, 6);
  peer.channel = 0;
  peer.encrypt = false;
  if (esp_now_add_peer(&peer) != ESP_OK) {
    Serial.println("could not add the peer");
    return;
  }
  Serial.println("sensor ready");
}

void loop() {
  reading.temperature = dht.readTemperature();
  reading.humidity = dht.readHumidity();
  reading.potentiometer = analogRead(POT_PIN);
  reading.button = digitalRead(BUTTON_PIN) == LOW;

  esp_now_send(toEveryone, (uint8_t *) &reading, sizeof(reading));
  delay(500);
}
'''

PAINEL = r'''
// ESP-NOW: the PANEL board.
//
// Receives the readings from the SENSOR board and shows them: temperature
// and humidity on the LCD; the potentiometer over there becomes this
// servo's angle; the button over there lights the LED here. The button
// HERE goes the other way: it turns the sensor board's LED on and off.

#include <esp_now.h>
#include <WiFi.h>
#include <Wire.h>
#include <LiquidCrystal_I2C.h>
#include <ESP32Servo.h>

const int SERVO_PIN = 18;
const int LED_PIN = 19;
const int BUTTON_PIN = 23;

LiquidCrystal_I2C lcd(0x27, 16, 2);
Servo pointer;

// Broadcast address: every board with ESP-NOW listens. On the real bench,
// you can swap it for the other board's MAC (the serial monitor shows it).
uint8_t toEveryone[] = {0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF};

// The SAME struct on both boards: what leaves one is copied byte by byte
// into the other.
typedef struct {
  float temperature;
  float humidity;
  int potentiometer;   // 0 to 4095
  bool button;
} Reading;

typedef struct {
  bool ledOn;
} Command;

Reading reading;
Command command;
esp_now_peer_info_t peer;

// The callback only copies and flags: the loop is what touches the LCD and
// the servo. Drawing inside the callback would make the radio wait on the
// display.
volatile bool arrived = false;
unsigned long lastReading = 0;
int lastButton = HIGH;

void onReceive(const esp_now_recv_info_t *info, const uint8_t *data, int length) {
  memcpy(&reading, data, sizeof(reading));
  arrived = true;
}

void setup() {
  Serial.begin(115200);
  pinMode(LED_PIN, OUTPUT);
  pinMode(BUTTON_PIN, INPUT_PULLUP);

  lcd.init();
  lcd.backlight();
  lcd.print("Waiting for the");
  lcd.setCursor(0, 1);
  lcd.print("sensor...");
  pointer.attach(SERVO_PIN);

  WiFi.mode(WIFI_STA);
  Serial.print("panel MAC: ");
  Serial.println(WiFi.macAddress());

  if (esp_now_init() != ESP_OK) {
    Serial.println("ESP-NOW did not start");
    return;
  }
  esp_now_register_recv_cb(onReceive);

  memcpy(peer.peer_addr, toEveryone, 6);
  peer.channel = 0;
  peer.encrypt = false;
  esp_now_add_peer(&peer);
  Serial.println("panel ready");
}

void loop() {
  if (arrived) {
    arrived = false;
    lastReading = millis();

    lcd.clear();
    lcd.setCursor(0, 0);
    lcd.print("Temp: ");
    lcd.print(reading.temperature, 1);
    lcd.print(" C");
    lcd.setCursor(0, 1);
    lcd.print("Hum:  ");
    lcd.print(reading.humidity, 0);
    lcd.print(" %");

    pointer.write(map(reading.potentiometer, 0, 4095, 0, 180));
    digitalWrite(LED_PIN, reading.button ? HIGH : LOW);
  }

  // Three seconds with no news from the sensor: warn.
  if (lastReading > 0 && millis() - lastReading > 3000) {
    lcd.clear();
    lcd.print("Sensor is gone!");
    lastReading = 0;
  }

  // Each press of the button here flips the sensor board's LED.
  int button = digitalRead(BUTTON_PIN);
  if (lastButton == HIGH && button == LOW) {
    command.ledOn = !command.ledOn;
    esp_now_send(toEveryone, (uint8_t *) &command, sizeof(command));
    Serial.println(command.ledOn ? "sent: turn on" : "sent: turn off");
  }
  lastButton = button;
  delay(20);
}
'''

gravar('esp32-espnow', SENSOR,
       pecas=[
           # ---- a placa sensor e o que e dela ----
           {'Id': 'sensor', 'Tipo': 'esp32-devkit-v1', 'X': 330, 'Y': 90},
           {'Id': 'pot', 'Tipo': 'potentiometer', 'X': 150, 'Y': 70, 'Giro': 0.5},
           {'Id': 'botao1', 'Tipo': 'pushbutton', 'X': 150, 'Y': 190},
           {'Id': 'led1', 'Tipo': 'led', 'X': 170, 'Y': 260, 'Cor': 'verde'},
           {'Id': 'r1', 'Tipo': 'resistor', 'X': 150, 'Y': 330, 'Ohms': 220},
           {'Id': 'dht', 'Tipo': 'dht22', 'X': 500, 'Y': 90, 'Giro': TEMPERATURA_24},

           # ---- a placa painel e o que e dela ----
           {'Id': 'painel', 'Tipo': 'esp32-devkit-v1', 'X': 720, 'Y': 90},
           {'Id': 'lcd', 'Tipo': 'lcd1602-i2c', 'X': 880, 'Y': 20},
           {'Id': 'servo', 'Tipo': 'servo', 'X': 900, 'Y': 190},
           {'Id': 'led2', 'Tipo': 'led', 'X': 900, 'Y': 330, 'Cor': 'vermelho'},
           {'Id': 'r2', 'Tipo': 'resistor', 'X': 960, 'Y': 400, 'Ohms': 220},
           {'Id': 'botao2', 'Tipo': 'pushbutton', 'X': 1010, 'Y': 330},
       ],
       fios=[
           # sensor
           fio('pot', 'VCC', 'sensor', '3V3', VERMELHO),
           fio('pot', 'SIG', 'sensor', 'D34', AMARELO),
           fio('pot', 'GND', 'sensor', 'GND.2', PRETO),
           fio('botao1', '1.r', 'sensor', 'D13', AZUL),
           fio('botao1', '2.r', 'sensor', 'GND.2', PRETO),
           fio('sensor', 'D27', 'led1', 'A', VERDE),
           fio('led1', 'C', 'r1', '1', PRETO),
           fio('r1', '2', 'sensor', 'GND.2', PRETO),
           fio('dht', 'VCC', 'sensor', '3V3', VERMELHO),
           fio('dht', 'SDA', 'sensor', 'D4', AMARELO),
           fio('dht', 'GND', 'sensor', 'GND.1', PRETO),

           # painel
           fio('lcd', 'GND', 'painel', 'GND.1', PRETO),
           fio('lcd', 'VCC', 'painel', 'VIN', VERMELHO),
           fio('lcd', 'SDA', 'painel', 'D21', AZUL),
           fio('lcd', 'SCL', 'painel', 'D22', ROXO),
           fio('servo', 'GND', 'painel', 'GND.1', PRETO),
           fio('servo', 'V+', 'painel', 'VIN', VERMELHO),
           fio('servo', 'PWM', 'painel', 'D18', LARANJA),
           fio('painel', 'D19', 'led2', 'A', VERMELHO),
           fio('led2', 'C', 'r2', '1', PRETO),
           fio('r2', '2', 'painel', 'GND.1', PRETO),
           fio('botao2', '1.l', 'painel', 'D23', AZUL),
           fio('botao2', '2.l', 'painel', 'GND.1', PRETO),
       ],
       codigos_por_placa={'sensor': SENSOR, 'painel': PAINEL})


# =====================================================================
#  Analisador logico: cinco sinais de verdade, lado a lado
# =====================================================================

ANALISADOR = r'''
// Logic analyzer: five real signals, side by side.
//
// Select the analyzer in the circuit and click "Open the analyzer":
//   D0  pin 5    25% PWM at 976 Hz (Timer0) - the dim LED
//   D1  pin 6    75% PWM at 976 Hz - the bright LED
//   D2  pin 8    1 kHz tone(), in 60 ms bursts
//   D3  pin 9    the servo pulse, 50 Hz: turn the potentiometer and the
//                width changes from 0.5 to 2.4 ms
//   D4  pin 11   shiftOut DATA
//   D5  pin 12   shiftOut CLOCK
//   D6  pin 13   LATCH
//   D7  pin 1    the serial port going out on TX, at 9600 baud - with the
//                character written over each byte
//
// Stop the capture (Space), zoom with the mouse wheel and hover over a
// channel: the analyzer measures the width, period and frequency.
// "Save .vcd" takes the capture to PulseView.
//
// On the real board the pins were chosen so they don't clash: Servo uses
// Timer1 (and disables PWM on pins 9 and 10), and tone() uses Timer2 (and
// disables PWM on pins 3 and 11). That is why the PWM is on 5 and 6, which
// belong to Timer0.

#include <Servo.h>

const int DIM_LED = 5;
const int BRIGHT_LED = 6;
const int BUZZER = 8;
const int SERVO = 9;
const int DATA = 11;
const int CLOCK = 12;
const int LATCH = 13;
const int POT = A0;

Servo pointer;
byte counter = 0;
unsigned long lastBeep = 0;

void setup() {
  Serial.begin(9600);
  pinMode(DATA, OUTPUT);
  pinMode(CLOCK, OUTPUT);
  pinMode(LATCH, OUTPUT);

  analogWrite(DIM_LED, 64);      // high 25% of the time
  analogWrite(BRIGHT_LED, 192);  // 75%
  pointer.attach(SERVO);
}

void loop() {
  // The servo follows the potentiometer.
  int angle = map(analogRead(POT), 0, 1023, 0, 180);
  pointer.write(angle);

  // One byte through shiftOut, wrapped by the latch: what a 74HC595 would
  // receive to light eight LEDs.
  digitalWrite(LATCH, LOW);
  shiftOut(DATA, CLOCK, MSBFIRST, counter);
  digitalWrite(LATCH, HIGH);

  // Every half second, a beep and a line on the serial port.
  if (millis() - lastBeep >= 500) {
    lastBeep = millis();
    tone(BUZZER, 1000, 60);
    Serial.print("n=");
    Serial.println(counter);
  }

  counter++;
  delay(20);
}
'''

gravar('uno-analisador', ANALISADOR,
       pecas=[
           {'Id': 'placa', 'Tipo': 'arduino-uno', 'X': 300, 'Y': 220},
           {'Id': 'la', 'Tipo': 'logic-analyzer', 'X': 330, 'Y': 20, 'Nome': 'analyzer',
            'JanelaUs': 50000},
           {'Id': 'led1', 'Tipo': 'led', 'X': 150, 'Y': 70, 'Cor': 'amarelo'},
           {'Id': 'r1', 'Tipo': 'resistor', 'X': 125, 'Y': 150, 'Ohms': 220},
           {'Id': 'led2', 'Tipo': 'led', 'X': 220, 'Y': 70, 'Cor': 'vermelho'},
           {'Id': 'r2', 'Tipo': 'resistor', 'X': 195, 'Y': 170, 'Ohms': 220},
           {'Id': 'buzzer', 'Tipo': 'buzzer', 'X': 640, 'Y': 60},
           {'Id': 'servo', 'Tipo': 'servo', 'X': 640, 'Y': 260},
           {'Id': 'pot', 'Tipo': 'potentiometer', 'X': 360, 'Y': 470, 'Giro': 0.5},
       ],
       fios=[
           # LEDs de PWM
           fio('placa', '5', 'led1', 'A', AMARELO),
           fio('led1', 'C', 'r1', '1', PRETO),
           fio('r1', '2', 'placa', 'GND.1', PRETO),
           fio('placa', '6', 'led2', 'A', VERMELHO),
           fio('led2', 'C', 'r2', '1', PRETO),
           fio('r2', '2', 'placa', 'GND.1', PRETO),
           # buzzer, servo e potenciometro
           fio('placa', '8', 'buzzer', '2', LARANJA),
           fio('buzzer', '1', 'placa', 'GND.1', PRETO),
           fio('servo', 'PWM', 'placa', '9', LARANJA),
           fio('servo', 'V+', 'placa', '5V', VERMELHO),
           fio('servo', 'GND', 'placa', 'GND.2', PRETO),
           fio('pot', 'VCC', 'placa', '5V', VERMELHO),
           fio('pot', 'SIG', 'placa', 'A0', AMARELO),
           fio('pot', 'GND', 'placa', 'GND.3', PRETO),
           # os oito canais do analisador
           fio('placa', '5', 'la', 'D0', AMARELO),
           fio('placa', '6', 'la', 'D1', VERMELHO),
           fio('placa', '8', 'la', 'D2', LARANJA),
           fio('placa', '9', 'la', 'D3', ROXO),
           fio('placa', '11', 'la', 'D4', AZUL),
           fio('placa', '12', 'la', 'D5', VERDE),
           fio('placa', '13', 'la', 'D6', BRANCO),
           fio('placa', '1', 'la', 'D7', AZUL),
           fio('la', 'GND', 'placa', 'GND.1', PRETO),
       ])


# As descricoes (o texto embaixo do codigo) moram num script so, para as
# demos antigas e estas: rodar este arquivo de novo nao pode apaga-las.
import os
import runpy

runpy.run_path(os.path.join(os.path.dirname(os.path.abspath(__file__)), 'descricoes-das-demos.py'))
