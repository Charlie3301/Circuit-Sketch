"""python ferramentas/extrair-svg/demos-por-placa.py

As demos de cada placa (Alpha 19).

Ate a Alpha 18, vinte e seis placas abriam com o mesmo pisca-pisca -- e o
pedido foi claro: "os projetos de demo precisam ser diferentes um dos
outros, para demonstrar do que o aplicativo e capaz". Agora cada placa
tem um projeto de verdade, e nenhum se repete:

  ESP32-S2      relogio pela internet (NTP) no OLED
  ESP32-S3      anel NeoPixel com tres efeitos
  ESP32-C3      estufa: DHT22, janela com servo e LCD
  ESP32-C6      alarme com sensor de presenca
  ESP32-H2      sensor de estacionamento (ultrassom)
  NodeMCU       servidor web com LED e botao
  ESP-01        tomada inteligente por MQTT
  ESP-02        campainha musical
  ESP-03        termo-higrometro com DHT11
  ESP-04        semaforo com botao de pedestre
  ESP-05        calculadora pela serial, com o analisador no TX
  ESP-06        jogo Genius
  ESP-07        scanner de redes WiFi com barra de sinal
  ESP-08        jogo de reflexo
  ESP-09        dado eletronico
  ESP-10        ponte entre a serial e uma pagina web
  ESP-11        minuteria de escada
  ESP-12        balanca com HX711 e OLED
  ESP-12E       cor do LED RGB pelo navegador
  ESP-12F       luminaria NeoPixel por MQTT
  ESP-13        fechadura com teclado e senha
  ESP-14        placar de jogo no OLED
  Franzininho   vela eletronica
  ATtiny85      luz noturna automatica
  Pico          piano (Arduino), joystick e OLED (MicroPython),
                LED respirando (SDK)
  Pico W        estacao do tempo na web (Arduino), MQTT (MicroPython),
                voltimetro (SDK)
  Nucleo-L031K6 termometro com NTC e alarme
  Nucleo-C031C6 relogio com RTC e LCD

O codigo e o que se escreve para a placa de verdade. Onde a bancada pede
algum cuidado que o simulador nao mostra -- o ECHO de 5 V do ultrassom
numa placa de 3,3 V, a fonte de uma matriz de 64 LEDs --, o comentario do
sketch diz.
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from demos_comum import *   # noqa: E402,F403

DEMOS = []   # (recurso, nome no menu, placa)


def demo(recurso, nome, placa):
    DEMOS.append((recurso, nome, placa))


# =====================================================================
#  ESP32-S2: relogio pela internet no OLED
# =====================================================================

c = Circuito('placa', 'esp32-s2-devkit')
c.peca('oled', 'ssd1306-i2c', 300, 40)
c.ligar('oled', 'GND', 'placa', 'GND', PRETO)
c.ligar('oled', 'VCC', 'placa', '3V3', VERMELHO)
c.ligar('oled', 'SDA', 'placa', '8', AZUL)
c.ligar('oled', 'SCL', 'placa', '9', ROXO)
c.botao('botao', '4', 'GND', 300, 200)

gravar('esp32s2-relogio-ntp', r'''
// Internet clock on the ESP32-S2.
//
// The board joins the WiFi, asks an NTP server for the time and shows it on
// the OLED display. The button switches between the time and the date. No
// battery and no setting: the internet keeps the clock right.
//
// In the simulator the time arrives the same way it does on the board --
// configTime and getLocalTime -- and comes from this computer's clock.

#include <WiFi.h>
#include <time.h>
#include <Wire.h>
#include <Adafruit_GFX.h>
#include <Adafruit_SSD1306.h>

const int BUTTON = 4;

Adafruit_SSD1306 display(128, 64, &Wire, -1);

// Brasilia time: UTC-3, no daylight saving.
const long TIME_ZONE = -3 * 3600;

bool showDate = false;
int lastButton = HIGH;

void setup() {
  Serial.begin(115200);
  pinMode(BUTTON, INPUT_PULLUP);

  Wire.begin();   // SDA on GPIO 8 and SCL on 9: the S2 defaults
  display.begin(SSD1306_SWITCHCAPVCC, 0x3C);
  display.clearDisplay();
  display.setTextColor(SSD1306_WHITE);
  display.setCursor(0, 0);
  display.print("Connecting...");
  display.display();

  WiFi.begin("Wokwi-GUEST", "");
  while (WiFi.status() != WL_CONNECTED) {
    delay(250);
    Serial.print(".");
  }
  Serial.println();
  Serial.print("connected, IP ");
  Serial.println(WiFi.localIP());

  configTime(TIME_ZONE, 0, "pool.ntp.org", "time.google.com");
}

void loop() {
  int button = digitalRead(BUTTON);
  if (lastButton == HIGH && button == LOW) showDate = !showDate;
  lastButton = button;

  struct tm now;
  if (!getLocalTime(&now)) {
    Serial.println("waiting for the NTP time...");
    delay(500);
    return;
  }

  char line[20];
  if (showDate) strftime(line, sizeof(line), "%d/%m/%Y", &now);
  else strftime(line, sizeof(line), "%H:%M:%S", &now);

  display.clearDisplay();
  display.setTextSize(1);
  display.setCursor(0, 0);
  display.print("NTP clock - S2");
  display.setTextSize(2);
  display.setCursor(0, 24);
  display.print(line);
  display.setTextSize(1);
  display.setCursor(0, 56);
  display.print(showDate ? "button: show time" : "button: show date");
  display.display();

  delay(200);
}
''', c, '''
Internet clock (NTP) on the OLED, on the ESP32-S2.

What this demo shows:
- WiFi, NTP and the system clock (configTime and getLocalTime), the way
  the ESP32 does it on the bench;
- the OLED display on the S2's default I2C (SDA on 8, SCL on 9).

Press F5, then the button to switch between the time and the date.
''')
demo('esp32s2-relogio-ntp', 'Relógio pela internet (NTP) no OLED', 'ESP32-S2 DevKit')


# =====================================================================
#  ESP32-S3: anel NeoPixel com tres efeitos
# =====================================================================

c = Circuito('placa', 'esp32-s3-devkit')
c.peca('anel', 'led-ring', 300, 40)
c.ligar('anel', 'GND', 'placa', 'GND', PRETO)
c.ligar('anel', 'VCC', 'placa', '5V', VERMELHO)
c.ligar('anel', 'DIN', 'placa', '5', VERDE)
c.botao('botao', '4', 'GND', 300, 230)

gravar('esp32s3-anel-neopixel', r'''
// Ring of 16 NeoPixels on the ESP32-S3, with three effects.
//
// The button changes the effect: rainbow, comet and breathing. The colors
// come from the library's own math -- ColorHSV, gamma32 and sine8 -- so
// what you see here is what the real ring shows.
//
// On the bench: the ring draws up to 1 A on white. Power it from 5V (never
// from a pin), with a 1000 uF capacitor on VCC and 330 ohms on DIN.

#include <Adafruit_NeoPixel.h>

const int RING_PIN = 5;
const int BUTTON = 4;
const int COUNT = 16;

Adafruit_NeoPixel ring(COUNT, RING_PIN, NEO_GRB + NEO_KHZ800);

int effect = 0;          // 0 rainbow, 1 comet, 2 breathing
int lastButton = HIGH;
long hue = 0;
int step = 0;

void rainbow() {
  for (int i = 0; i < COUNT; i++) {
    long h = hue + (i * 65536L / COUNT);
    ring.setPixelColor(i, ring.gamma32(ring.ColorHSV(h)));
  }
  hue += 512;
}

void comet() {
  // A bright dot with a trail that fades away.
  for (int i = 0; i < COUNT; i++) {
    int distance = (step - i + COUNT) % COUNT;
    int brightness = distance < 6 ? 255 >> distance : 0;
    ring.setPixelColor(i, ring.Color(0, brightness / 2, brightness));
  }
  step = (step + 1) % COUNT;
}

void breathing() {
  int brightness = ring.sine8(step * 4);
  ring.fill(ring.Color(brightness, brightness / 4, 0));
  step++;
}

void setup() {
  Serial.begin(115200);
  pinMode(BUTTON, INPUT_PULLUP);
  ring.begin();
  ring.setBrightness(80);
  Serial.println("Press the button to change the effect");
}

void loop() {
  int button = digitalRead(BUTTON);
  if (lastButton == HIGH && button == LOW) {
    effect = (effect + 1) % 3;
    step = 0;
    ring.clear();
    Serial.print("effect: ");
    if (effect == 0) Serial.println("rainbow");
    else if (effect == 1) Serial.println("comet");
    else Serial.println("breathing");
  }
  lastButton = button;

  if (effect == 0) rainbow();
  else if (effect == 1) comet();
  else breathing();

  ring.show();
  delay(30);
}
''', c, '''
NeoPixel ring with three effects, on the ESP32-S3.

What this demo shows:
- addressable LEDs with Adafruit_NeoPixel: the ColorHSV rainbow with
  gamma32, a comet and a breathing effect with sine8;
- show() sending the whole strip at once -- without it nothing lights up,
  here and on the bench.

Press F5, then the button to change the effect.
''')
demo('esp32s3-anel-neopixel', 'Anel NeoPixel com três efeitos', 'ESP32-S3 DevKit')


# =====================================================================
#  ESP32-C3: estufa automatica
# =====================================================================

c = Circuito('placa', 'esp32-c3-devkit')
c.peca('dht', 'dht22', 300, 20, Giro=giro_dht22(31), Giro2=giro2_umidade(62))
c.ligar('dht', 'VCC', 'placa', '3V3', VERMELHO)
c.ligar('dht', 'SDA', 'placa', '4', AMARELO)
c.ligar('dht', 'GND', 'placa', 'GND', PRETO)
c.peca('janela', 'servo', 420, 180)
c.ligar('janela', 'GND', 'placa', 'GND.2', PRETO)
c.ligar('janela', 'V+', 'placa', '5V', VERMELHO)
c.ligar('janela', 'PWM', 'placa', '5', LARANJA)
c.led('ventilador', '6', 'GND', 'azul', 380, 330, AZUL)
c.led('aquecedor', '7', 'GND', 'vermelho', 480, 330, VERMELHO)
c.peca('lcd', 'lcd1602-i2c', 60, 330)
c.ligar('lcd', 'GND', 'placa', 'GND.2', PRETO)
c.ligar('lcd', 'VCC', 'placa', '5V', VERMELHO)
c.ligar('lcd', 'SDA', 'placa', '8', AZUL)
c.ligar('lcd', 'SCL', 'placa', '9', ROXO)

gravar('esp32c3-estufa', r'''
// Automatic greenhouse on the ESP32-C3.
//
// The DHT22 measures temperature and humidity. When it is hot, the window
// (the servo) opens and the fan (blue LED) turns on; when it is cold, the
// heater (red LED) turns on and the window closes. The LCD shows it all.

#include <DHT.h>
#include <Wire.h>
#include <LiquidCrystal_I2C.h>
#include <ESP32Servo.h>

const int DHT_PIN = 4;
const int WINDOW_PIN = 5;
const int FAN = 6;
const int HEATER = 7;

const float HOT = 28.0;
const float COLD = 18.0;

DHT dht(DHT_PIN, DHT22);
LiquidCrystal_I2C lcd(0x27, 16, 2);
Servo window;

void setup() {
  Serial.begin(115200);
  pinMode(FAN, OUTPUT);
  pinMode(HEATER, OUTPUT);
  dht.begin();
  lcd.init();          // the C3's default I2C: SDA on 8, SCL on 9
  lcd.backlight();
  window.attach(WINDOW_PIN);
  window.write(0);
}

void loop() {
  float temperature = dht.readTemperature();
  float humidity = dht.readHumidity();

  if (isnan(temperature) || isnan(humidity)) {
    Serial.println("the DHT22 did not answer");
    delay(2000);
    return;
  }

  String state;
  if (temperature > HOT) {
    window.write(90);
    digitalWrite(FAN, HIGH);
    digitalWrite(HEATER, LOW);
    state = "Venting";
  } else if (temperature < COLD) {
    window.write(0);
    digitalWrite(FAN, LOW);
    digitalWrite(HEATER, HIGH);
    state = "Heating";
  } else {
    window.write(0);
    digitalWrite(FAN, LOW);
    digitalWrite(HEATER, LOW);
    state = "All good";
  }

  lcd.setCursor(0, 0);
  lcd.print(temperature, 1);
  lcd.print("C  ");
  lcd.print(humidity, 0);
  lcd.print("%    ");
  lcd.setCursor(0, 1);
  lcd.print(state);
  lcd.print("        ");

  Serial.print(temperature, 1);
  Serial.print(" C, ");
  Serial.print(humidity, 0);
  Serial.print(" % -> ");
  Serial.println(state);

  delay(2000);   // the DHT22 reads at most once every 2 s
}
''', c, '''
Automatic greenhouse, on the ESP32-C3.

What this demo shows:
- the DHT22 (temperature and humidity), a servo and an I2C LCD on the
  same board;
- deciding by range: above 28 C it vents, below 18 C it heats.

The greenhouse starts hot (31 C). Click the DHT22 and lower the
temperature to watch the window close and the heater turn on.
''')
demo('esp32c3-estufa', 'Estufa: DHT22, janela com servo e LCD', 'ESP32-C3 DevKit')


# =====================================================================
#  ESP32-C6: alarme com sensor de presenca
# =====================================================================

c = Circuito('placa', 'esp32-c6-devkit')
c.peca('pir', 'pir-motion-sensor', 300, 20)
c.ligar('pir', 'VCC', 'placa', '5V', VERMELHO)
c.ligar('pir', 'OUT', 'placa', '2', AMARELO)
c.ligar('pir', 'GND', 'placa', 'GND', PRETO)
c.buzzer('sirene', '3', 'GND', 430, 30)
c.led('alarme', '4', 'GND', 'vermelho', 380, 190, VERMELHO)
c.led('armado', '5', 'GND', 'verde', 480, 190, VERDE)
c.botao('botao', '6', 'GND', 320, 300)

gravar('esp32c6-alarme', r'''
// Alarm with a motion sensor, on the ESP32-C6.
//
// The button arms and disarms it. Armed (green LED on), any movement on the
// PIR sensor sets off the siren and blinks the red LED until someone
// disarms it. After arming there are 5 s to leave -- just like real
// alarms.

const int PIR = 2;
const int SIREN = 3;
const int ALARM_LED = 4;
const int ARMED_LED = 5;
const int BUTTON = 6;

const unsigned long EXIT_DELAY = 5000;

enum State { DISARMED, LEAVING, ARMED, TRIGGERED };
State state = DISARMED;
unsigned long since = 0;
int lastButton = HIGH;

void changeTo(State next) {
  state = next;
  since = millis();
  noTone(SIREN);
  digitalWrite(ALARM_LED, LOW);
  digitalWrite(ARMED_LED, next == ARMED || next == LEAVING);

  if (next == DISARMED) Serial.println("disarmed");
  if (next == LEAVING) Serial.println("arming: 5 s to leave...");
  if (next == ARMED) Serial.println("ARMED");
  if (next == TRIGGERED) Serial.println("*** INTRUDER! ***");
}

void setup() {
  Serial.begin(115200);
  pinMode(PIR, INPUT);
  pinMode(ALARM_LED, OUTPUT);
  pinMode(ARMED_LED, OUTPUT);
  pinMode(BUTTON, INPUT_PULLUP);
  changeTo(DISARMED);
}

void loop() {
  int button = digitalRead(BUTTON);
  if (lastButton == HIGH && button == LOW) {
    if (state == DISARMED) changeTo(LEAVING);
    else changeTo(DISARMED);
  }
  lastButton = button;

  if (state == LEAVING) {
    // Blinks the green LED while there is time to leave.
    digitalWrite(ARMED_LED, (millis() / 250) % 2);
    if (millis() - since > EXIT_DELAY) changeTo(ARMED);
  }

  if (state == ARMED && digitalRead(PIR) == HIGH) {
    changeTo(TRIGGERED);
  }

  if (state == TRIGGERED) {
    // Two-tone siren with the red LED blinking along.
    bool high = (millis() / 300) % 2;
    tone(SIREN, high ? 1200 : 800);
    digitalWrite(ALARM_LED, high);
  }

  delay(20);
}
''', c, '''
Alarm with a motion sensor, on the ESP32-C6.

What this demo shows:
- a state machine (enum): disarmed, leaving, armed, triggered;
- the PIR sensor, a two-tone siren with tone() and time measured with
  millis(), without blocking the loop.

Press F5, arm it with the button, wait 5 s and click the PIR sensor to
simulate someone walking by. The button disarms it.
''')
demo('esp32c6-alarme', 'Alarme com sensor de presença', 'ESP32-C6 DevKit')


# =====================================================================
#  ESP32-H2: sensor de estacionamento
# =====================================================================

c = Circuito('placa', 'esp32-h2-devkit')
c.peca('sonar', 'hc-sr04', 300, 20, Giro=giro_distancia(35))
c.ligar('sonar', 'TRIG', 'placa', '4', AMARELO)
c.ligar('sonar', 'GND', 'placa', 'GND', PRETO)
c.peca('rgb', 'rgb-led', 420, 190)
c.peca('r_r', 'resistor', 330, 280, Ohms=220)
c.peca('r_g', 'resistor', 330, 305, Ohms=220)
c.peca('r_b', 'resistor', 330, 330, Ohms=220)
c.ligar('placa', '10', 'r_r', '1', VERMELHO)
c.ligar('r_r', '2', 'rgb', 'R', VERMELHO)
c.ligar('placa', '11', 'r_g', '1', VERDE)
c.ligar('r_g', '2', 'rgb', 'G', VERDE)
c.ligar('placa', '12', 'r_b', '1', AZUL)
c.ligar('r_b', '2', 'rgb', 'B', AZUL)
c.ligar('rgb', 'COM', 'placa', 'GND.2', PRETO)
c.buzzer('buzzer', '22', 'GND.2', 520, 190)
# o HC-SR04 comum e de 5 V: o ECHO passa por um divisor de 1 k e 2 k (3,3 V no pino 5)
c.peca('r_echo1', 'resistor', 470, 70, Ohms=1000)
c.peca('r_echo2', 'resistor', 470, 110, Ohms=2000)
c.ligar('sonar', 'VCC', 'placa', '5V', VERMELHO)
c.ligar('sonar', 'ECHO', 'r_echo1', '1', VERDE)
c.ligar('r_echo1', '2', 'placa', '5', VERDE)
c.ligar('r_echo1', '2', 'r_echo2', '1', VERDE)
c.ligar('r_echo2', '2', 'placa', 'GND.3', PRETO)

gravar('esp32h2-estacionamento', r'''
// Parking sensor, on the ESP32-H2.
//
// The ultrasonic sensor measures the distance to the car behind. Far away,
// the LED is green; closer, yellow with beeps that get faster and faster;
// almost touching, red and the beep becomes continuous -- like any car.
//
// The common HC-SR04 runs on 5 V: VCC goes to the board's 5V, and ECHO --
// which puts 5 V on the pin, too much for the H2, a 3.3 V chip -- goes
// through a 1 k and 2 k divider (3.3 V on pin 5). Another way: the
// HC-SR04P, which works on 3.3 V.

const int TRIG = 4;
const int ECHO = 5;
const int RED = 10;
const int GREEN = 11;
const int BLUE = 12;
const int BUZZER = 22;

unsigned long lastBeep = 0;

float measureCm() {
  digitalWrite(TRIG, LOW);
  delayMicroseconds(2);
  digitalWrite(TRIG, HIGH);
  delayMicroseconds(10);
  digitalWrite(TRIG, LOW);
  unsigned long roundTrip = pulseIn(ECHO, HIGH, 30000);
  if (roundTrip == 0) return 400;   // nothing in front
  return roundTrip / 58.0;          // 58 us per centimeter, there and back
}

void color(int r, int g, int b) {
  analogWrite(RED, r);
  analogWrite(GREEN, g);
  analogWrite(BLUE, b);
}

void setup() {
  Serial.begin(115200);
  pinMode(TRIG, OUTPUT);
  pinMode(ECHO, INPUT);
}

void loop() {
  float cm = measureCm();

  // The gap between beeps shrinks with the distance.
  unsigned long interval;
  if (cm > 100) { color(0, 255, 0); interval = 0; }
  else if (cm > 50) { color(0, 255, 0); interval = 800; }
  else if (cm > 20) { color(255, 160, 0); interval = 300; }
  else { color(255, 0, 0); interval = 1; }

  if (interval == 0) {
    noTone(BUZZER);
  } else if (interval == 1) {
    tone(BUZZER, 2000);                    // almost touching: continuous
  } else if (millis() - lastBeep > interval) {
    lastBeep = millis();
    tone(BUZZER, 2000, 80);
  }

  Serial.print("distance: ");
  Serial.print(cm, 0);
  Serial.println(" cm");
  delay(60);
}
''', c, '''
Parking sensor, on the ESP32-H2.

What this demo shows:
- the HC-SR04 ultrasonic sensor with pulseIn: the echo becomes distance
  (58 us per cm);
- an RGB LED with PWM and beeps that get faster with tone().

Press F5 and click the HC-SR04 to move the "car" closer or farther.
''')
demo('esp32h2-estacionamento', 'Sensor de estacionamento (ultrassom)', 'ESP32-H2 DevKit')


# =====================================================================
#  NodeMCU: servidor web com LED e botao
# =====================================================================

c = Circuito('placa', 'esp8266-nodemcu')
c.led('led', 'D5', 'GND.3', 'verde', 320, 60, VERDE)
c.botao('botao', 'D6', 'GND.3', 300, 180)

gravar('nodemcu-servidor', r'''
// Web server on the NodeMCU (ESP8266).
//
// The page shows whether the button is pressed and turns the LED on and
// off. It refreshes itself every 2 s: press the button and watch the page
// change.
//
// Note the names: D5 and D6 are the ones printed on the NodeMCU, and the
// ESP8266 core translates them to GPIO 14 and 12.

#include <ESP8266WiFi.h>
#include <ESP8266WebServer.h>

const int LED = D5;
const int BUTTON = D6;

ESP8266WebServer server(80);
bool ledOn = false;

String page() {
  String html = "<!DOCTYPE html><html><head><meta charset='utf-8'>";
  html += "<meta http-equiv='refresh' content='2'>";
  html += "<title>NodeMCU</title></head><body style='font-family:sans-serif'>";
  html += "<h2>NodeMCU on the network</h2>";
  html += "<p>Button: <b>";
  html += digitalRead(BUTTON) == LOW ? "PRESSED" : "released";
  html += "</b></p><p>LED: <b>";
  html += ledOn ? "on" : "off";
  html += "</b></p>";
  html += "<p><a href='/on'>Turn on</a> | <a href='/off'>Turn off</a></p>";
  html += "<p>Up for ";
  html += millis() / 1000;
  html += " s</p></body></html>";
  return html;
}

void respond() {
  server.send(200, "text/html", page());
}

void turnOn() {
  ledOn = true;
  digitalWrite(LED, HIGH);
  respond();
}

void turnOff() {
  ledOn = false;
  digitalWrite(LED, LOW);
  respond();
}

void setup() {
  Serial.begin(115200);
  pinMode(LED, OUTPUT);
  pinMode(BUTTON, INPUT_PULLUP);

  WiFi.mode(WIFI_STA);
  WiFi.begin("Wokwi-GUEST", "");
  Serial.print("connecting");
  while (WiFi.status() != WL_CONNECTED) {
    delay(300);
    Serial.print(".");
  }
  Serial.println();
  Serial.print("IP: ");
  Serial.println(WiFi.localIP());

  server.on("/", respond);
  server.on("/on", turnOn);
  server.on("/off", turnOff);
  server.begin();
  Serial.println("server is up: open the address in the NETWORK panel");
}

void loop() {
  server.handleClient();
  delay(2);
}
''', c, '''
Web server on the NodeMCU (ESP8266).

What this demo shows:
- a page served by the board itself, with links that light the LED and
  the button state refreshed every 2 s;
- the D5 and D6 names printed on the board, which the ESP8266 core
  translates to the real GPIO.

Press F5 and open the address shown in the NETWORK panel in a browser.
''')
demo('nodemcu-servidor', 'Servidor web: LED e botão na página', 'NodeMCU (ESP8266)')


# =====================================================================
#  ESP-01: tomada inteligente por MQTT
# =====================================================================

c = Circuito('placa', 'esp-01', 80, 100)
c.led_ativo_baixo('tomada', 'GPIO2', 'VCC', 'amarelo', 330, 60, AMARELO)
c.botao('botao', 'GPIO0', 'GND', 300, 190)

gravar('esp01-tomada-mqtt', r'''
// Smart plug over MQTT, on an ESP-01.
//
// The plug's "relay" is the LED (on a real ESP-01 it would be a relay
// module on the same pin). Over MQTT:
//   home/plug          on, off or toggle
//   home/plug/state    the board answers "on" or "off"
// The button does the same by hand, and also reports over MQTT.
//
// Why the LED lights with LOW: on the ESP-01, GPIO0 and GPIO2 must be HIGH
// when the board powers up, or it won't boot. With the LED between 3.3 V
// and the pin, it holds GPIO2 high at boot -- and lights when the pin goes
// LOW.

#include <ESP8266WiFi.h>
#include <PubSubClient.h>

const int PLUG = 2;     // GPIO2
const int BUTTON = 0;   // GPIO0: the same button used for flashing

WiFiClient net;
PubSubClient mqtt(net);

bool isOn = false;
int lastButton = HIGH;

void apply(bool value) {
  isOn = value;
  digitalWrite(PLUG, isOn ? LOW : HIGH);   // lights with LOW
  mqtt.publish("home/plug/state", isOn ? "on" : "off", true);
  Serial.println(isOn ? "plug ON" : "plug off");
}

void received(char* topic, byte* payload, unsigned int length) {
  String command;
  for (unsigned int i = 0; i < length; i++) command += (char)payload[i];
  command.trim();
  command.toLowerCase();

  if (command == "on") apply(true);
  else if (command == "off") apply(false);
  else if (command == "toggle") apply(!isOn);
  else Serial.println("unknown command: " + command);
}

void reconnect() {
  // The client ID must be unique on the broker: with the same ID, each
  // plug that connects kicks the other one off. The board's MAC solves it.
  String id = "plug-" + WiFi.macAddress();
  id.replace(":", "");

  while (!mqtt.connected()) {
    Serial.print("broker... ");
    if (mqtt.connect(id.c_str())) {
      Serial.println("ok");
      mqtt.subscribe("home/plug");
      apply(isOn);
    } else {
      Serial.println("failed, trying again in 2 s");
      delay(2000);
    }
  }
}

void setup() {
  Serial.begin(115200);
  pinMode(PLUG, OUTPUT);
  digitalWrite(PLUG, HIGH);   // starts off
  pinMode(BUTTON, INPUT_PULLUP);

  WiFi.begin("Wokwi-GUEST", "");
  while (WiFi.status() != WL_CONNECTED) delay(250);
  Serial.print("IP: ");
  Serial.println(WiFi.localIP());

  mqtt.setServer("broker.hivemq.com", 1883);
  mqtt.setCallback(received);
}

void loop() {
  if (!mqtt.connected()) reconnect();
  mqtt.loop();

  int button = digitalRead(BUTTON);
  if (lastButton == HIGH && button == LOW) apply(!isOn);
  lastButton = button;
  delay(20);
}
''', c, '''
Smart plug over MQTT, on an ESP-01.

What this demo shows:
- MQTT with PubSubClient: subscribing to a topic, obeying it and
  reporting the state in a retained message;
- the LED that lights with LOW, the right way on the ESP-01 (GPIO2 must
  be HIGH at boot).

Press F5 and, in the NETWORK panel, publish "on", "off" or "toggle" to
home/plug. The button also turns it on and off.
''')
demo('esp01-tomada-mqtt', 'Tomada inteligente por MQTT', 'ESP-01')


# =====================================================================
#  ESP-02: campainha musical
# =====================================================================

c = Circuito('placa', 'esp-02', 80, 100)
c.botao('botao', 'GPIO0', 'GND', 300, 60)
c.buzzer('buzzer', 'GPIO2', 'GND', 320, 170)

gravar('esp02-campainha', r'''
// Musical doorbell, on an ESP-02.
//
// Press the button and it plays "ding-dong" -- or, on every fifth visit,
// the opening of Beethoven's Ode to Joy. The serial monitor counts the
// visits.
//
// The buzzer is passive: tone() makes each note, at its own frequency.

const int BUTTON = 0;   // GPIO0
const int BUZZER = 2;   // GPIO2

// Ode to Joy: notes (Hz) and durations (ms).
const int NOTES[] = {330, 330, 349, 392, 392, 349, 330, 294,
                     262, 262, 294, 330, 330, 294, 294};
const int DURATIONS[] = {300, 300, 300, 300, 300, 300, 300, 300,
                         300, 300, 300, 300, 450, 150, 600};
const int NOTE_COUNT = 15;

int visits = 0;

void dingDong() {
  tone(BUZZER, 659, 300);   // E
  delay(350);
  tone(BUZZER, 523, 500);   // C
  delay(600);
}

void odeToJoy() {
  for (int i = 0; i < NOTE_COUNT; i++) {
    tone(BUZZER, NOTES[i], DURATIONS[i] * 9 / 10);
    delay(DURATIONS[i]);
  }
}

void setup() {
  Serial.begin(115200);
  pinMode(BUTTON, INPUT_PULLUP);
  Serial.println("Doorbell ready: press the button");
}

void loop() {
  if (digitalRead(BUTTON) == LOW) {
    visits++;
    Serial.print("Visit number ");
    Serial.println(visits);

    if (visits % 5 == 0) odeToJoy();
    else dingDong();

    // Holding the button does not ring again: wait for release.
    while (digitalRead(BUTTON) == LOW) delay(10);
  }
  delay(20);
}
''', c, '''
Musical doorbell, on an ESP-02.

What this demo shows:
- a melody with tone() and arrays of notes and durations;
- "wait for release", which keeps a held button from ringing nonstop.

Press F5, then the button. Every fifth visit the tune changes.
''')
demo('esp02-campainha', 'Campainha musical', 'ESP-02')


# =====================================================================
#  ESP-03: termo-higrometro com DHT11
# =====================================================================

c = Circuito('placa', 'esp-03', 80, 100)
c.peca('dht', 'dht11', 300, 20, Giro=giro_dht11(24), Giro2=giro2_umidade(35))
c.ligar('dht', 'VCC', 'placa', 'VCC', VERMELHO)
c.ligar('dht', 'DATA', 'placa', 'GPIO14', AMARELO)
c.ligar('dht', 'GND', 'placa', 'GND', PRETO)
c.led('frio', 'GPIO12', 'GND', 'azul', 330, 150, AZUL)
c.led('conforto', 'GPIO13', 'GND', 'verde', 420, 150, VERDE)
c.led('quente', 'GPIO16', 'GND', 'vermelho', 510, 150, VERMELHO)
c.led('seco', 'GPIO15', 'GND', 'amarelo', 600, 150, AMARELO)

gravar('esp03-termo-higrometro', r'''
// Thermo-hygrometer with a DHT11, on an ESP-03.
//
// Three LEDs tell how the room is: blue (cold, below 20 C), green
// (comfortable) and red (hot, above 27 C). The yellow one lights when the
// air is dry, below 40 % humidity.
//
// The ESP-03 does not break out GPIO4 or GPIO5: the LEDs go on the pins it
// has. The one on GPIO15 even helps at boot, which needs that pin LOW.

#include <DHT.h>

const int DHT_PIN = 14;
const int COLD = 12;
const int COMFORT = 13;
const int HOT = 16;
const int DRY = 15;

DHT dht(DHT_PIN, DHT11);

void setup() {
  Serial.begin(115200);
  pinMode(COLD, OUTPUT);
  pinMode(COMFORT, OUTPUT);
  pinMode(HOT, OUTPUT);
  pinMode(DRY, OUTPUT);
  dht.begin();
}

void loop() {
  delay(1000);   // the DHT11 reads at most once per second

  float t = dht.readTemperature();
  float h = dht.readHumidity();
  if (isnan(t) || isnan(h)) {
    Serial.println("failed to read the DHT11");
    return;
  }

  digitalWrite(COLD, t < 20);
  digitalWrite(COMFORT, t >= 20 && t <= 27);
  digitalWrite(HOT, t > 27);
  digitalWrite(DRY, h < 40);

  Serial.print("temperature: ");
  Serial.print(t, 0);
  Serial.print(" C   humidity: ");
  Serial.print(h, 0);
  Serial.println(" %");
}
''', c, '''
Thermo-hygrometer with a DHT11, on an ESP-03.

What this demo shows:
- the DHT11's temperature and humidity deciding four LEDs;
- a comparison written straight into digitalWrite ("t < 20" is already
  HIGH or LOW).

Press F5 and click the DHT11 to change the temperature and humidity (the
air starts dry, with the yellow LED on).
''')
demo('esp03-termo-higrometro', 'Termo-higrômetro com DHT11', 'ESP-03')


# =====================================================================
#  ESP-04: semaforo com botao de pedestre
# =====================================================================

c = Circuito('placa', 'esp-04', 80, 100)
c.led('carro_vermelho', 'GPIO14', 'GND', 'vermelho', 330, 20, VERMELHO)
c.led('carro_amarelo', 'GPIO12', 'GND', 'amarelo', 330, 110, AMARELO)
c.led('carro_verde', 'GPIO13', 'GND', 'verde', 330, 200, VERDE)
c.led('pedestre_vermelho', 'GPIO15', 'GND', 'vermelho', 470, 60, VERMELHO)
c.led('pedestre_verde', 'GPIO2', 'GND', 'verde', 470, 150, VERDE)
c.botao('botao', 'GPIO0', 'GND', 440, 250)
c.buzzer('buzzer', 'GPIO16', 'GND', 560, 230)

gravar('esp04-semaforo-pedestre', r'''
// Traffic light with a pedestrian button, on an ESP-04.
//
// Cars keep the green until someone presses the button. Then the cars'
// light goes through yellow and turns red, the pedestrian light turns
// green with a rhythmic beep (the audible signal for the visually
// impaired) and, at the end, the pedestrian red blinks to warn it is about
// to close.
//
// The ESP-04 breaks out seven GPIO (0, 2, 12, 13, 14, 15 and 16) and this
// demo uses all seven.

const int CAR_RED = 14;
const int CAR_YELLOW = 12;
const int CAR_GREEN = 13;
const int WALK_RED = 15;
const int WALK_GREEN = 2;
const int BUTTON = 0;
const int BUZZER = 16;

void cars(int red, int yellow, int green) {
  digitalWrite(CAR_RED, red);
  digitalWrite(CAR_YELLOW, yellow);
  digitalWrite(CAR_GREEN, green);
}

void pedestrians(int red, int green) {
  digitalWrite(WALK_RED, red);
  digitalWrite(WALK_GREEN, green);
}

void setup() {
  Serial.begin(115200);
  pinMode(CAR_RED, OUTPUT);
  pinMode(CAR_YELLOW, OUTPUT);
  pinMode(CAR_GREEN, OUTPUT);
  pinMode(WALK_RED, OUTPUT);
  pinMode(WALK_GREEN, OUTPUT);
  pinMode(BUTTON, INPUT_PULLUP);

  cars(LOW, LOW, HIGH);
  pedestrians(HIGH, LOW);
  Serial.println("Green for the cars. Press the button to cross.");
}

void loop() {
  if (digitalRead(BUTTON) == LOW) {
    Serial.println("crossing requested!");
    delay(1000);                   // cars don't stop right away
    cars(LOW, HIGH, LOW);
    delay(2000);
    cars(HIGH, LOW, LOW);
    delay(1000);

    pedestrians(LOW, HIGH);
    Serial.println("pedestrians: walk");
    for (int i = 0; i < 10; i++) { // 5 s of beeping
      tone(BUZZER, 880, 100);
      delay(500);
    }

    for (int i = 0; i < 6; i++) {  // the red blinks: about to close
      pedestrians(i % 2 == 0, LOW);
      delay(300);
    }
    pedestrians(HIGH, LOW);
    delay(1000);
    cars(LOW, LOW, HIGH);
    Serial.println("green for the cars");
  }
  delay(20);
}
''', c, '''
Traffic light with a pedestrian button, on an ESP-04.

What this demo shows:
- two coordinated traffic lights (cars and pedestrians) in a timed
  sequence;
- the accessibility beep and the red blinking at the end of the crossing.

Press F5, then the button to request a crossing.
''')
demo('esp04-semaforo-pedestre', 'Semáforo com botão de pedestre', 'ESP-04')


# =====================================================================
#  ESP-05: calculadora pela serial, com o analisador no TX
# =====================================================================

c = Circuito('placa', 'esp-05', 80, 100)
c.peca('la', 'logic-analyzer', 320, 60, Nome='serial', JanelaUs=5000)
c.ligar('placa', 'TXD', 'la', 'D0', AZUL)
c.ligar('la', 'GND', 'placa', 'GND', PRETO)

gravar('esp05-calculadora', r'''
// Serial calculator, on an ESP-05.
//
// The ESP-05 only breaks out the serial pins -- RX and TX -- and that is
// exactly where the conversation happens. Type a calculation in the serial
// monitor and press Enter:
//
//   12 + 30      7.5 * 4      100 / 8      2 ^ 10
//
// The logic analyzer is on TX: open it to see each character of the answer
// going out at 115200 baud, with the character written on top.

#include <math.h>

void calculate(String expr) {
  // The operator is the first + - * / ^ after the first number.
  int at = -1;
  char op = ' ';
  for (unsigned int i = 1; i < expr.length(); i++) {
    char c = expr.charAt(i);
    if (c == '+' || c == '-' || c == '*' || c == '/' || c == '^') {
      at = i;
      op = c;
      break;
    }
  }

  if (at < 0) {
    Serial.println("no operator found. Use + - * / or ^");
    return;
  }

  float a = expr.substring(0, at).toFloat();
  float b = expr.substring(at + 1).toFloat();
  float r = 0;

  switch (op) {
    case '+': r = a + b; break;
    case '-': r = a - b; break;
    case '*': r = a * b; break;
    case '/':
      if (b == 0) {
        Serial.println("division by zero!");
        return;
      }
      r = a / b;
      break;
    case '^': r = pow(a, b); break;
  }

  Serial.print(expr);
  Serial.print(" = ");
  if (r == (long)r) Serial.println((long)r);   // whole result: no decimals
  else Serial.println(r, 4);
}

void setup() {
  Serial.begin(115200);
  Serial.println("Calculator: type a calculation (e.g. 12 + 30)");
}

void loop() {
  if (Serial.available()) {
    String expr = Serial.readStringUntil('\n');
    expr.trim();
    if (expr.length() > 0) calculate(expr);
  }
  delay(10);
}
''', c, '''
Serial calculator, on an ESP-05.

What this demo shows:
- reading a line from the serial monitor and splitting the numbers from
  the operator with String methods;
- the logic analyzer on the TX pin: each byte of the answer shows up as a
  character over the waveform.

Press F5, type "12 + 30" in the serial monitor and press Enter. Then
select the analyzer and click "Open the analyzer".
''')
demo('esp05-calculadora', 'Calculadora pela serial (com analisador no TX)', 'ESP-05')


# =====================================================================
#  ESP-06: jogo Genius
# =====================================================================

c = Circuito('placa', 'esp-06', 80, 100)
c.led('vermelho', 'GPIO16', 'GND', 'vermelho', 330, 20, VERMELHO)
c.led('verde', 'GPIO14', 'GND', 'verde', 430, 20, VERDE)
c.led('azul', 'GPIO12', 'GND', 'azul', 530, 20, AZUL)
c.botao('b_vermelho', 'GPIO13', 'GND', 300, 150, VERMELHO)
c.botao('b_verde', 'GPIO2', 'GND', 400, 150, VERDE)
c.botao('b_azul', 'GPIO0', 'GND', 500, 150, AZUL)
c.buzzer('buzzer', 'GPIO15', 'GND', 420, 240)

gravar('esp06-genius', r'''
// Genius, on an ESP-06.
//
// The board lights a sequence of colors, each with its own note. Repeat it
// by pressing the buttons in the same order. Get it right and the sequence
// grows; get it wrong and the game tells how many rounds you got and
// starts over.
//
// Press any button to start.

const int LEDS[] = {16, 14, 12};       // red, green, blue
const int BUTTONS[] = {13, 2, 0};
const int NOTES[] = {262, 330, 392};   // C, E, G
const int BUZZER = 15;
const int MAXIMUM = 20;

int sequence[MAXIMUM];
int length = 0;

void show(int color, int ms) {
  digitalWrite(LEDS[color], HIGH);
  tone(BUZZER, NOTES[color], ms);
  delay(ms);
  digitalWrite(LEDS[color], LOW);
  delay(150);
}

// Waits for a button and returns which one it was (0, 1 or 2).
int waitForButton() {
  while (true) {
    for (int i = 0; i < 3; i++) {
      if (digitalRead(BUTTONS[i]) == LOW) {
        show(i, 250);
        while (digitalRead(BUTTONS[i]) == LOW) delay(5);   // wait for release
        return i;
      }
    }
    delay(5);
  }
}

void lost() {
  Serial.print("Wrong! You got ");
  Serial.print(length - 1);
  Serial.println(" round(s) right.");
  tone(BUZZER, 110, 800);
  for (int k = 0; k < 3; k++) {
    for (int i = 0; i < 3; i++) digitalWrite(LEDS[i], HIGH);
    delay(200);
    for (int i = 0; i < 3; i++) digitalWrite(LEDS[i], LOW);
    delay(200);
  }
  length = 0;
}

void setup() {
  Serial.begin(115200);
  for (int i = 0; i < 3; i++) {
    pinMode(LEDS[i], OUTPUT);
    pinMode(BUTTONS[i], INPUT_PULLUP);
  }
  Serial.println("Genius: press any button to start");
  waitForButton();
  randomSeed(micros());   // the time of the first press becomes the seed
}

void loop() {
  if (length == MAXIMUM) {
    Serial.println("You beat Genius!");
    length = 0;
  }

  sequence[length] = random(3);
  length++;
  Serial.print("Round ");
  Serial.println(length);
  delay(600);

  for (int i = 0; i < length; i++) show(sequence[i], 400);

  for (int i = 0; i < length; i++) {
    if (waitForButton() != sequence[i]) {
      lost();
      delay(1000);
      return;
    }
  }
  delay(400);
}
''', c, '''
Genius game, on an ESP-06.

What this demo shows:
- arrays (the sequence and the pin and note tables) and functions that
  return a value;
- random() seeded from the first press -- each game comes out different,
  like on the board.

Press F5 and any button to start. Repeat the colors.
''')
demo('esp06-genius', 'Jogo Genius', 'ESP-06')


# =====================================================================
#  ESP-07: scanner de redes WiFi com barra de sinal
# =====================================================================

c = Circuito('placa', 'esp-07', 80, 100)
c.peca('barra', 'led-bar-graph', 420, 40)
for i, pino in enumerate(['GPIO16', 'GPIO14', 'GPIO12', 'GPIO13', 'GPIO5'], start=1):
    c.peca(f'r{i}', 'resistor', 300, 36 + (i - 1) * 24, Ohms=220)
    c.ligar('placa', pino, f'r{i}', '1', VERDE)
    c.ligar(f'r{i}', '2', 'barra', f'A{i}', VERDE)
    c.ligar('barra', f'C{i}', 'placa', 'GND', PRETO)

gravar('esp07-scanner-wifi', r'''
// WiFi network scanner, on an ESP-07 -- the module with an antenna
// connector.
//
// Every 5 s the board looks for the networks around it and lists them in
// the serial monitor: the name, the signal (RSSI, in dBm) and whether it is
// open or password-protected. The LED bar shows the strength of the BEST
// network: the closer to 0 dBm, the more LEDs.
//
// In the simulator the networks are the ones this computer's WiFi sees.

#include <ESP8266WiFi.h>

const int BAR[] = {16, 14, 12, 13, 5};

void showSignal(int rssi) {
  // From -90 dBm (almost nothing) to -40 dBm (great): from 0 to 5 LEDs.
  int lit = constrain(map(rssi, -90, -40, 0, 5), 0, 5);
  for (int i = 0; i < 5; i++) digitalWrite(BAR[i], i < lit);
}

void setup() {
  Serial.begin(115200);
  for (int i = 0; i < 5; i++) pinMode(BAR[i], OUTPUT);
  WiFi.mode(WIFI_STA);
  WiFi.disconnect();
  delay(100);
}

void loop() {
  Serial.println("scanning for networks...");
  int found = WiFi.scanNetworks();

  if (found <= 0) {
    Serial.println("no networks found");
    showSignal(-100);
  } else {
    int best = -100;
    for (int i = 0; i < found; i++) {
      Serial.print(i + 1);
      Serial.print(". ");
      Serial.print(WiFi.SSID(i));
      Serial.print("  ");
      Serial.print(WiFi.RSSI(i));
      Serial.print(" dBm  ");
      Serial.println(WiFi.encryptionType(i) == ENC_TYPE_NONE ? "open" : "secured");
      if (WiFi.RSSI(i) > best) best = WiFi.RSSI(i);
    }
    showSignal(best);
  }

  WiFi.scanDelete();
  delay(5000);
}
''', c, '''
WiFi network scanner, on an ESP-07.

What this demo shows:
- WiFi.scanNetworks() with each network's name, signal and security type;
- map() and constrain() turning dBm into lit LEDs.

Press F5. The networks listed are the ones this computer's WiFi sees.
''')
demo('esp07-scanner-wifi', 'Scanner de redes WiFi com barra de sinal', 'ESP-07')


# =====================================================================
#  ESP-08: jogo de reflexo
# =====================================================================

c = Circuito('placa', 'esp-08', 80, 100)
c.led('prepara', 'GPIO12', 'GND', 'vermelho', 330, 30, VERMELHO)
c.led('vai', 'GPIO14', 'GND', 'verde', 430, 30, VERDE)
c.botao('botao', 'GPIO13', 'GND', 320, 150)
c.buzzer('buzzer', 'GPIO16', 'GND', 460, 150)

gravar('esp08-reflexo', r'''
// Reflex game, on an ESP-08.
//
// The red LED lights: get ready. After a random time, the green one
// lights -- press the button as fast as you can. The serial monitor shows
// your time and the record. Pressed too early? False start, and the buzzer
// complains.

const int READY = 12;
const int GO = 14;
const int BUTTON = 13;
const int BUZZER = 16;   // the ESP-08 does not break out GPIO4 or GPIO5

unsigned long record = 0;

bool pressed() {
  return digitalRead(BUTTON) == LOW;
}

void setup() {
  Serial.begin(115200);
  pinMode(READY, OUTPUT);
  pinMode(GO, OUTPUT);
  pinMode(BUTTON, INPUT_PULLUP);
  Serial.println("Reflex: press the button when the green LED lights");
}

void loop() {
  digitalWrite(READY, HIGH);
  randomSeed(micros());
  unsigned long wait = random(1500, 4000);
  unsigned long start = millis();

  // The random wait -- watching for anyone pressing early.
  while (millis() - start < wait) {
    if (pressed()) {
      digitalWrite(READY, LOW);
      Serial.println("False start! Wait for the green.");
      tone(BUZZER, 150, 600);
      while (pressed()) delay(5);
      delay(1500);
      return;
    }
    delay(2);
  }

  digitalWrite(READY, LOW);
  digitalWrite(GO, HIGH);
  unsigned long litAt = millis();
  while (!pressed()) delay(1);
  unsigned long elapsed = millis() - litAt;
  digitalWrite(GO, LOW);

  Serial.print("Your time: ");
  Serial.print(elapsed);
  Serial.print(" ms");
  if (record == 0 || elapsed < record) {
    record = elapsed;
    Serial.print("  <- record!");
    tone(BUZZER, 1319, 150);
  }
  Serial.println();

  while (pressed()) delay(5);   // wait for release
  delay(1500);
}
''', c, '''
Reflex game, on an ESP-08.

What this demo shows:
- time measured with millis() to the millisecond;
- a random wait with random() and the watch for early presses.

Press F5, then the button as soon as the green LED lights.
''')
demo('esp08-reflexo', 'Jogo de reflexo', 'ESP-08')


# =====================================================================
#  ESP-09: dado eletronico
# =====================================================================

c = Circuito('placa', 'esp-09', 80, 100)
# Os sete pontos na posicao do dado: grupos que acendem juntos dividem o pino.
PONTOS = [
    ('d_sup_esq', 'GPIO14', 330, 20),  ('d_sup_dir', 'GPIO12', 470, 20),
    ('d_meio_esq', 'GPIO13', 330, 110),
    ('d_meio_dir', 'GPIO13', 470, 110),
    ('d_inf_esq', 'GPIO12', 330, 200), ('d_inf_dir', 'GPIO14', 470, 200),
]
for id, pino, x, y in PONTOS:
    c.led(id, pino, 'GND', 'vermelho', x, y, VERMELHO)
c.led_ativo_baixo('d_centro', 'GPIO0', 'VCC', 'vermelho', 400, 110, VERMELHO)
c.botao('botao', 'GPIO2', 'GND', 320, 300)
c.buzzer('buzzer', 'GPIO15', 'GND', 470, 290)

gravar('esp09-dado', r'''
// Electronic die, on an ESP-09.
//
// Press the button: the die "rolls" (the dots change slower and slower and
// the buzzer goes tick-tick) and stops on a number from 1 to 6, drawn like
// on a real die.
//
// That is seven LEDs and only four pins: the dots that always light
// together share a pin. On 6, for example, both columns light up whole.
//
// The ESP-09 only breaks out six GPIO (0, 2, 12, 13, 14 and 15), and three
// of them control boot: GPIO0 and GPIO2 must be HIGH, GPIO15 LOW. That is
// why the center dot goes between 3.3 V and GPIO0 (it lights with LOW),
// the button sits on GPIO2 and the buzzer on GPIO15.

const int CENTER = 0;       // the middle one, lights with LOW
const int DIAGONAL1 = 14;   // top-left and bottom-right corners
const int DIAGONAL2 = 12;   // the other two corners
const int MIDDLE = 13;      // middle-left and middle-right
const int BUTTON = 2;
const int BUZZER = 15;

void show(int n) {
  digitalWrite(CENTER, (n == 1 || n == 3 || n == 5) ? LOW : HIGH);
  digitalWrite(DIAGONAL1, n >= 2);
  digitalWrite(DIAGONAL2, n >= 4);
  digitalWrite(MIDDLE, n == 6);
}

void setup() {
  Serial.begin(115200);
  pinMode(CENTER, OUTPUT);
  pinMode(DIAGONAL1, OUTPUT);
  pinMode(DIAGONAL2, OUTPUT);
  pinMode(MIDDLE, OUTPUT);
  pinMode(BUTTON, INPUT_PULLUP);
  show(6);
  Serial.println("Electronic die: press the button to roll");
}

void loop() {
  if (digitalRead(BUTTON) == LOW) {
    randomSeed(micros());
    // Rolls slowing down, like a real die coming to rest.
    for (int wait = 30; wait < 250; wait += 20) {
      show(random(1, 7));
      tone(BUZZER, 1800, 8);
      delay(wait);
    }
    int result = random(1, 7);
    show(result);
    tone(BUZZER, 988, 120);
    Serial.print("Rolled ");
    Serial.println(result);
    while (digitalRead(BUTTON) == LOW) delay(10);
  }
  delay(20);
}
''', c, '''
Electronic die, on an ESP-09.

What this demo shows:
- seven LEDs on four pins: the dots that light together are in parallel,
  each with its own resistor;
- random(1, 7) with a new seed on every roll.

Press F5, then the button to roll the die.
''')
demo('esp09-dado', 'Dado eletrônico', 'ESP-09')


# =====================================================================
#  ESP-10: ponte entre a serial e uma pagina web
# =====================================================================

c = Circuito('placa', 'esp-10', 120, 120)

gravar('esp10-ponte-serial-web', r'''
// Bridge between the serial port and the web, on an ESP-10.
//
// The ESP-10 is a five-pin module: only the serial port comes out of it.
// That is its real use -- a WiFi "modem" for another board. Here:
//   - what arrives over serial (type in the monitor) shows up on the page;
//   - what is written on the page goes out over serial.

#include <ESP8266WiFi.h>
#include <ESP8266WebServer.h>

ESP8266WebServer server(80);

String received[5];     // the last five serial lines
int count = 0;

void store(String line) {
  if (count < 5) {
    received[count] = line;
    count++;
  } else {
    for (int i = 0; i < 4; i++) received[i] = received[i + 1];
    received[4] = line;
  }
}

void page() {
  // The form sends the text in /?msg=...
  if (server.hasArg("msg") && server.arg("msg").length() > 0) {
    Serial.println("[web] " + server.arg("msg"));
  }

  String html = "<!DOCTYPE html><html><head><meta charset='utf-8'>";
  html += "<title>Serial bridge</title></head><body style='font-family:sans-serif'>";
  html += "<h2>Serial and web bridge (ESP-10)</h2>";
  html += "<h3>Arrived over serial:</h3><ul>";
  for (int i = 0; i < count; i++) html += "<li>" + received[i] + "</li>";
  if (count == 0) html += "<li><i>nothing yet: type in the serial monitor</i></li>";
  html += "</ul><form action='/'><input name='msg' placeholder='send over serial'> ";
  html += "<button>Send</button></form><p><a href='/'>refresh</a></p></body></html>";
  server.send(200, "text/html", html);
}

void setup() {
  Serial.begin(115200);
  WiFi.begin("Wokwi-GUEST", "");
  while (WiFi.status() != WL_CONNECTED) delay(250);
  Serial.print("page at http://");
  Serial.println(WiFi.localIP());

  server.on("/", page);
  server.begin();
}

void loop() {
  server.handleClient();

  if (Serial.available()) {
    String line = Serial.readStringUntil('\n');
    line.trim();
    if (line.length() > 0) {
      store(line);
      Serial.println("ok, it is on the page: " + line);
    }
  }
  delay(5);
}
''', c, '''
Bridge between the serial port and a web page, on an ESP-10.

What this demo shows:
- a five-pin module doing what it really does: carrying another board's
  serial port to the network;
- an HTML form with hasArg() and arg(), and an array of String as a
  queue.

Press F5, type something in the serial monitor and open the page (address
in the NETWORK panel). What you write on the page comes back over serial.
''')
demo('esp10-ponte-serial-web', 'Ponte entre a serial e uma página web', 'ESP-10')


# =====================================================================
#  ESP-11: minuteria de escada
# =====================================================================

c = Circuito('placa', 'esp-11', 80, 100)
c.led_ativo_baixo('luz', 'RXD', 'VCC', 'branco', 330, 50, BRANCO)
c.botao('botao', 'GPIO0', 'GND', 300, 170)

gravar('esp11-minuteria', r'''
// Stairway timer switch, on an ESP-11.
//
// A tap on the button turns the light on for 10 s -- and it blinks once
// before going off, warning whoever is still on the stairs. Holding the
// button for 2 s keeps the light on (cleaning mode); another tap goes back
// to normal.
//
// The ESP-11 only breaks out GPIO0 and the serial pins (TX is GPIO1, RX is
// GPIO3). The lamp (the LED) goes on RX, between 3.3 V and the pin, and
// lights with LOW -- and the sketch does not open Serial, which would take
// the pin back.

const int BUTTON = 0;   // GPIO0
const int LIGHT = 3;    // RXD (GPIO3), lights with LOW

const unsigned long ON_TIME = 10000;
const unsigned long LONG_PRESS = 2000;

bool locked = false;
bool lit = false;
unsigned long litAt = 0;

void light(bool on) {
  lit = on;
  digitalWrite(LIGHT, on ? LOW : HIGH);
}

void setup() {
  pinMode(BUTTON, INPUT_PULLUP);
  pinMode(LIGHT, OUTPUT);
  light(false);
}

void loop() {
  if (digitalRead(BUTTON) == LOW) {
    unsigned long pressedAt = millis();
    while (digitalRead(BUTTON) == LOW && millis() - pressedAt < LONG_PRESS) delay(10);

    if (millis() - pressedAt >= LONG_PRESS) {
      locked = true;
      light(true);
      while (digitalRead(BUTTON) == LOW) delay(10);   // wait for release
    } else if (locked) {
      locked = false;
      light(false);
    } else {
      light(true);
      litAt = millis();
    }
  }

  if (lit && !locked) {
    unsigned long elapsed = millis() - litAt;
    if (elapsed > ON_TIME) {
      light(false);
    } else if (elapsed > ON_TIME - 1000 && elapsed < ON_TIME - 700) {
      digitalWrite(LIGHT, HIGH);   // the warning blink
    } else {
      digitalWrite(LIGHT, LOW);
    }
  }
  delay(20);
}
''', c, '''
Stairway timer switch, on an ESP-11.

What this demo shows:
- short press and long press on the same button, measured with millis();
- a timer that does not block the loop, with a warning before going off.

Press F5, then the button (quick click: 10 s of light; hold 2 s: light
stays on).
''')
demo('esp11-minuteria', 'Minuteria de escada', 'ESP-11')


# =====================================================================
#  ESP-12: balanca com HX711 e OLED
# =====================================================================

c = Circuito('placa', 'esp-12', 80, 100)
c.peca('celula', 'hx711', 330, 10, Giro=giro_peso(0))
c.ligar('celula', 'VCC', 'placa', 'VCC', VERMELHO)
c.ligar('celula', 'DT', 'placa', 'GPIO12', AMARELO)
c.ligar('celula', 'SCK', 'placa', 'GPIO14', VERDE)
c.ligar('celula', 'GND', 'placa', 'GND', PRETO)
c.peca('oled', 'ssd1306-i2c', 330, 210)
c.ligar('oled', 'GND', 'placa', 'GND', PRETO)
c.ligar('oled', 'VCC', 'placa', 'VCC', VERMELHO)
c.ligar('oled', 'SDA', 'placa', 'GPIO4', AZUL)
c.ligar('oled', 'SCL', 'placa', 'GPIO5', ROXO)
c.botao('tara', 'GPIO13', 'GND', 480, 230)

gravar('esp12-balanca', r'''
// Scale with an HX711, on an ESP-12.
//
// The load cell and the HX711 measure the weight, and the OLED shows it in
// grams. The button is the TARE: put the empty plate (or a bowl) on it,
// press, and that becomes zero.
//
// The HX711 does not return grams: it returns the converter's count. It is
// set_scale() -- your cell's calibration factor -- that turns the count
// into grams. On the bench, you find the factor by weighing something of
// known weight.

#include <HX711.h>
#include <Wire.h>
#include <Adafruit_GFX.h>
#include <Adafruit_SSD1306.h>

const int DT = 12;
const int SCK_HX = 14;
const int TARE = 13;

// The part's 5 kg cell gives ~420 counts per gram.
const float CALIBRATION = 420.0;

HX711 scale;
Adafruit_SSD1306 display(128, 64, &Wire, -1);

void setup() {
  Serial.begin(115200);
  pinMode(TARE, INPUT_PULLUP);

  scale.begin(DT, SCK_HX);
  scale.set_scale(CALIBRATION);
  scale.tare();                   // the empty plate is zero

  Wire.begin();                   // SDA on GPIO4, SCL on GPIO5
  display.begin(SSD1306_SWITCHCAPVCC, 0x3C);
  display.setTextColor(SSD1306_WHITE);
}

void loop() {
  if (digitalRead(TARE) == LOW) {
    scale.tare();
    Serial.println("tare done");
    while (digitalRead(TARE) == LOW) delay(10);
  }

  float grams = scale.get_units(5);
  if (grams > -1 && grams < 1) grams = 0;   // no jitter around zero

  display.clearDisplay();
  display.setTextSize(1);
  display.setCursor(0, 0);
  display.print("Scale - ESP-12");
  display.setTextSize(2);
  display.setCursor(0, 24);
  display.print(grams, 0);
  display.print(" g");
  display.setTextSize(1);
  display.setCursor(0, 56);
  display.print("button: tare");
  display.display();

  Serial.print(grams, 0);
  Serial.println(" g");
  delay(300);
}
''', c, '''
Scale with an HX711 and an OLED, on an ESP-12.

What this demo shows:
- the load cell the library's way: raw count, tare (zero) and calibration
  (set_scale) to get grams;
- the OLED on the ESP8266's default I2C (GPIO4 and GPIO5).

Press F5 and click the HX711 to put weight on it. The button does the
tare.
''')
demo('esp12-balanca', 'Balança com HX711 e OLED', 'ESP-12')


# =====================================================================
#  ESP-12E: cor do LED RGB pelo navegador
# =====================================================================

c = Circuito('placa', 'esp-12e', 80, 100)
c.peca('rgb', 'rgb-led', 440, 60)
c.peca('r_r', 'resistor', 330, 60, Ohms=220)
c.peca('r_g', 'resistor', 330, 90, Ohms=220)
c.peca('r_b', 'resistor', 330, 120, Ohms=220)
c.ligar('placa', 'GPIO14', 'r_r', '1', VERMELHO)
c.ligar('r_r', '2', 'rgb', 'R', VERMELHO)
c.ligar('placa', 'GPIO12', 'r_g', '1', VERDE)
c.ligar('r_g', '2', 'rgb', 'G', VERDE)
c.ligar('placa', 'GPIO13', 'r_b', '1', AZUL)
c.ligar('r_b', '2', 'rgb', 'B', AZUL)
c.ligar('rgb', 'COM', 'placa', 'GND', PRETO)

gravar('esp12e-rgb-navegador', r'''
// RGB LED color from the browser, on an ESP-12E.
//
// The page has a color picker: choose one and click Apply. The board gets
// "#rrggbb", splits out the red, green and blue and sends each one to its
// pin with PWM.

#include <ESP8266WiFi.h>
#include <ESP8266WebServer.h>

const int PIN_R = 14;
const int PIN_G = 12;
const int PIN_B = 13;

ESP8266WebServer server(80);
String currentColor = "#000000";

void applyColor(String hex) {
  long value = strtol(hex.substring(1).c_str(), NULL, 16);
  int r = (value >> 16) & 0xFF;
  int g = (value >> 8) & 0xFF;
  int b = value & 0xFF;
  analogWrite(PIN_R, r);
  analogWrite(PIN_G, g);
  analogWrite(PIN_B, b);
  currentColor = hex;
  Serial.printf("color %s -> R=%d G=%d B=%d\n", hex.c_str(), r, g, b);
}

void page() {
  if (server.hasArg("color")) applyColor(server.arg("color"));

  String html = "<!DOCTYPE html><html><head><meta charset='utf-8'>";
  html += "<title>RGB LED</title></head><body style='font-family:sans-serif'>";
  html += "<h2>RGB LED from the browser</h2>";
  html += "<form action='/'><input type='color' name='color' value='" + currentColor + "'> ";
  html += "<button>Apply</button></form>";
  html += "<p>Current color: " + currentColor + "</p></body></html>";
  server.send(200, "text/html", html);
}

void setup() {
  Serial.begin(115200);
  analogWriteRange(255);    // PWM from 0 to 255, like on the Arduino
  WiFi.begin("Wokwi-GUEST", "");
  while (WiFi.status() != WL_CONNECTED) delay(250);
  Serial.print("open http://");
  Serial.println(WiFi.localIP());

  server.on("/", page);
  server.begin();
  applyColor("#ff6a00");    // starts orange
}

void loop() {
  server.handleClient();
  delay(2);
}
''', c, '''
RGB LED color from the browser, on an ESP-12E.

What this demo shows:
- an HTML color picker sending "#rrggbb" to the board;
- strtol() and bit shifts splitting out the red, green and blue.

Press F5, open the address in the NETWORK panel and pick a color.
''')
demo('esp12e-rgb-navegador', 'Cor do LED RGB pelo navegador', 'ESP-12E')


# =====================================================================
#  ESP-12F: luminaria NeoPixel por MQTT
# =====================================================================

c = Circuito('placa', 'esp-12f', 80, 100)
c.pecas[0]['Usb'] = True
c.peca('matriz', 'neopixel-matrix', 320, 20)
# os 64 LEDs puxam mais do que o regulador da placa: a matriz come do power bank
c.peca('fonte', 'power-bank', 300, 330, Volts=5.0, MiliAmperes=2100, MiliAmperesHora=6500)
c.ligar('matriz', 'GND', 'placa', 'GND', PRETO)
c.ligar('matriz', 'DIN', 'placa', 'GPIO4', VERDE)
c.ligar('matriz', 'VCC', 'fonte', '+', VERMELHO)
c.ligar('fonte', '-', 'placa', 'GND', PRETO)

gravar('esp12f-luminaria-mqtt', r'''
// NeoPixel lamp over MQTT, on an ESP-12F.
//
// Three topics control the lamp:
//   home/lamp/color        "#ff8800", or red, green, blue, white
//   home/lamp/brightness   0 to 255
//   home/lamp/effect       solid, rainbow or pulse
// and it reports its state on home/lamp/state.
//
// On the bench: 64 LEDs draw up to 3.8 A on white. Power the matrix from
// its own 5 V supply, with its GND tied to the module's.

#include <ESP8266WiFi.h>
#include <PubSubClient.h>
#include <Adafruit_NeoPixel.h>

const int MATRIX_PIN = 4;
const int COUNT = 64;

Adafruit_NeoPixel matrix(COUNT, MATRIX_PIN, NEO_GRB + NEO_KHZ800);
WiFiClient net;
PubSubClient mqtt(net);

uint32_t color = 0xFF8800;
String effect = "solid";
long hue = 0;
int step = 0;

String hex6(uint32_t value) {
  String text = String(value, HEX);
  while (text.length() < 6) text = "0" + text;
  text.toUpperCase();
  return text;
}

uint32_t colorByName(String name) {
  if (name == "red") return 0xFF0000;
  if (name == "green") return 0x00FF00;
  if (name == "blue") return 0x0000FF;
  if (name == "white") return 0xFFFFFF;
  if (name.startsWith("#")) return strtol(name.substring(1).c_str(), NULL, 16);
  return color;
}

void publishState() {
  String state = "color=#" + hex6(color) + " brightness=" + String(matrix.getBrightness())
               + " effect=" + effect;
  mqtt.publish("home/lamp/state", state.c_str(), true);
  Serial.println(state);
}

void received(char* topic, byte* payload, unsigned int length) {
  String value;
  for (unsigned int i = 0; i < length; i++) value += (char)payload[i];
  value.trim();
  String which = String(topic);

  if (which == "home/lamp/color") color = colorByName(value);
  else if (which == "home/lamp/brightness") matrix.setBrightness(constrain(value.toInt(), 0, 255));
  else if (which == "home/lamp/effect") effect = value;
  publishState();
}

void reconnect() {
  // Unique ID on the broker: the board's MAC.
  String id = "lamp-" + WiFi.macAddress();
  id.replace(":", "");

  while (!mqtt.connected()) {
    Serial.print("broker... ");
    if (mqtt.connect(id.c_str())) {
      Serial.println("ok");
      mqtt.subscribe("home/lamp/color");
      mqtt.subscribe("home/lamp/brightness");
      mqtt.subscribe("home/lamp/effect");
      publishState();
    } else {
      Serial.println("failed, trying again in 2 s");
      delay(2000);
    }
  }
}

void setup() {
  Serial.begin(115200);
  matrix.begin();
  matrix.setBrightness(60);

  WiFi.begin("Wokwi-GUEST", "");
  while (WiFi.status() != WL_CONNECTED) delay(250);
  mqtt.setServer("broker.hivemq.com", 1883);
  mqtt.setCallback(received);
}

void loop() {
  if (!mqtt.connected()) reconnect();
  mqtt.loop();

  if (effect == "rainbow") {
    for (int i = 0; i < COUNT; i++)
      matrix.setPixelColor(i, matrix.gamma32(matrix.ColorHSV(hue + i * 1024)));
    hue += 256;
  } else if (effect == "pulse") {
    int level = matrix.sine8(step);
    int r = ((color >> 16) & 0xFF) * level / 255;
    int g = ((color >> 8) & 0xFF) * level / 255;
    int b = (color & 0xFF) * level / 255;
    matrix.fill(matrix.Color(r, g, b));
    step += 4;
  } else {
    matrix.fill(color);
  }
  matrix.show();
  delay(30);
}
''', c, '''
NeoPixel lamp over MQTT, on an ESP-12F.

What this demo shows:
- three MQTT topics (color, brightness and effect) driving an 8x8 matrix;
- the state published as a retained message, for whoever arrives later.

Press F5 and, in the NETWORK panel, publish "rainbow" to home/lamp/effect
or "blue" to home/lamp/color.
''')
demo('esp12f-luminaria-mqtt', 'Luminária NeoPixel por MQTT', 'ESP-12F')


# =====================================================================
#  ESP-13: fechadura com teclado e senha
# =====================================================================

c = Circuito('placa', 'esp-13', 60, 60)
c.peca('teclado', 'membrane-keypad', 300, 10)
for linha, pino in zip(['R1', 'R2', 'R3', 'R4'], ['GPIO14', 'GPIO12', 'GPIO13', 'GPIO5']):
    c.ligar('placa', pino, 'teclado', linha, AMARELO)
for coluna, pino in zip(['C1', 'C2', 'C3'], ['GPIO4', 'GPIO0', 'GPIO2']):
    c.ligar('placa', pino, 'teclado', coluna, AZUL)
c.led('aberta', 'GPIO16', 'GND', 'verde', 170, 260, VERDE)
c.buzzer('buzzer', 'GPIO15', 'GND', 60, 260)

gravar('esp13-fechadura', r'''
// Keypad lock with a code, on an ESP-13.
//
// Type the code on the keypad and press # to confirm (* clears). Right,
// and the lock opens for 5 s (green LED) with a double beep. Wrong, a low
// beep -- and three wrong tries in a row lock the keypad for 30 s.
//
// The code is 1234. Only the keypad's first three columns are used.

#include <Keypad.h>

const byte ROWS = 4;
const byte COLS = 3;
char keys[ROWS][COLS] = {
  {'1', '2', '3'},
  {'4', '5', '6'},
  {'7', '8', '9'},
  {'*', '0', '#'},
};
byte rowPins[ROWS] = {14, 12, 13, 5};
byte colPins[COLS] = {4, 0, 2};

Keypad keypad = Keypad(makeKeymap(keys), rowPins, colPins, ROWS, COLS);

const int OPEN = 16;     // green LED: lock open
const int BUZZER = 15;
const String CODE = "1234";

String typed = "";
int wrongTries = 0;

void beep(int note, int ms) {
  tone(BUZZER, note, ms);
  delay(ms + 40);
}

void check() {
  if (typed == CODE) {
    Serial.println("CODE OK: open for 5 s");
    wrongTries = 0;
    digitalWrite(OPEN, HIGH);
    beep(1319, 100);
    beep(1760, 150);
    delay(5000);
    digitalWrite(OPEN, LOW);
    Serial.println("locked again");
    return;
  }

  wrongTries++;
  Serial.print("wrong code (");
  Serial.print(wrongTries);
  Serial.println(" of 3)");
  beep(150, 500);
  if (wrongTries >= 3) {
    Serial.println("keypad locked for 30 s");
    delay(30000);
    wrongTries = 0;
  }
}

void setup() {
  Serial.begin(115200);
  pinMode(OPEN, OUTPUT);
  Serial.println("Lock: type the code and press #");
}

void loop() {
  char key = keypad.getKey();
  if (!key) {
    delay(10);
    return;
  }

  beep(2000, 30);   // the click of each key

  if (key == '*') {
    typed = "";
    Serial.println("cleared");
  } else if (key == '#') {
    check();
    typed = "";
  } else {
    typed += key;
    Serial.print("typed: ");
    for (unsigned int i = 0; i < typed.length(); i++) Serial.print('*');
    Serial.println();
  }
}
''', c, '''
Keypad lock with a code, on an ESP-13.

What this demo shows:
- the matrix keypad with the Keypad library (4 rows and 3 columns);
- a code built key by key in a String, and the lockout after three wrong
  tries.

Press F5 and click the keys: 1, 2, 3, 4 and #.
''')
demo('esp13-fechadura', 'Fechadura com teclado e senha', 'ESP-13')


# =====================================================================
#  ESP-14: placar no anel de LEDs, pelo MQTT
# =====================================================================

c = Circuito('placa', 'esp-14', 80, 100)
c.pecas[0]['Usb'] = True
c.peca('anel', 'led-ring', 320, 20)
# o anel puxa mais do que o regulador da placa: os LEDs comem do power bank
c.peca('fonte', 'power-bank', 300, 330, Volts=5.0, MiliAmperes=2100, MiliAmperesHora=6500)
c.ligar('anel', 'GND', 'placa', 'GND', PRETO)
c.ligar('anel', 'DIN', 'placa', 'GPIO0', VERDE)
c.ligar('anel', 'VCC', 'fonte', '+', VERMELHO)
c.ligar('fonte', '-', 'placa', 'GND', PRETO)

gravar('esp14-placar', r'''
// Scoreboard on an LED ring, over MQTT, on an ESP-14.
//
// The ESP-14 puts an ESP8266 and an STM8S003 in one module, and almost all
// the outside pins belong to the STM8: from the ESP8266 only GPIO0 comes
// out. Here only the ESP8266 runs (it is the one the Arduino programs) --
// and one pin is enough, because the ring of 16 NeoPixels talks over a
// single wire.
//
// Points arrive over MQTT:
//   scoreboard/point   "a" or "b" (one point for that team)
//   scoreboard/reset   anything
// Team A lights in red from the top, clockwise; team B in blue,
// counterclockwise. First to 8 wins, and the ring blinks in its color.
//
// On the bench: GPIO0 must be HIGH at boot, and the ring's DIN does not
// pull the pin either way. The ESP8266's serial goes to the STM8 inside the
// module; the monitor shows what it would say.

#include <ESP8266WiFi.h>
#include <PubSubClient.h>
#include <Adafruit_NeoPixel.h>

const int RING_PIN = 0;      // E_GPIO0, the only ESP8266 pin outside
const int COUNT = 16;
const int WINS_AT = 8;

Adafruit_NeoPixel ring(COUNT, RING_PIN, NEO_GRB + NEO_KHZ800);
WiFiClient net;
PubSubClient mqtt(net);

int pointsA = 0;
int pointsB = 0;

void draw() {
  ring.clear();
  for (int i = 0; i < pointsA; i++) ring.setPixelColor(i, ring.Color(255, 0, 0));
  for (int i = 0; i < pointsB; i++) ring.setPixelColor(COUNT - 1 - i, ring.Color(0, 0, 255));
  ring.show();
}

void celebrate(uint32_t color) {
  for (int flash = 0; flash < 4; flash++) {
    ring.fill(color);
    ring.show();
    delay(250);
    ring.clear();
    ring.show();
    delay(250);
  }
}

void publishScore() {
  String score = String(pointsA) + " x " + String(pointsB);
  mqtt.publish("scoreboard/state", score.c_str(), true);
  Serial.print("score: ");
  Serial.println(score);
}

void received(char* topic, byte* payload, unsigned int length) {
  String value;
  for (unsigned int i = 0; i < length; i++) value += (char)payload[i];
  value.trim();
  value.toLowerCase();
  bool playing = pointsA < WINS_AT && pointsB < WINS_AT;

  if (String(topic) == "scoreboard/reset") {
    pointsA = 0;
    pointsB = 0;
  } else if (value == "a" && playing) {
    pointsA++;
  } else if (value == "b" && playing) {
    pointsB++;
  }
  draw();
  publishScore();

  if (playing && (pointsA == WINS_AT || pointsB == WINS_AT)) {
    Serial.println(pointsA == WINS_AT ? "team A wins!" : "team B wins!");
    celebrate(pointsA == WINS_AT ? ring.Color(255, 0, 0) : ring.Color(0, 0, 255));
    draw();
  }
}

void reconnect() {
  // Unique ID on the broker: the board's MAC.
  String id = "scoreboard-" + WiFi.macAddress();
  id.replace(":", "");

  while (!mqtt.connected()) {
    Serial.print("broker... ");
    if (mqtt.connect(id.c_str())) {
      Serial.println("ok");
      mqtt.subscribe("scoreboard/point");
      mqtt.subscribe("scoreboard/reset");
      publishScore();
    } else {
      Serial.println("failed, trying again in 2 s");
      delay(2000);
    }
  }
}

void setup() {
  Serial.begin(115200);
  ring.begin();
  ring.setBrightness(40);
  draw();

  WiFi.begin("Wokwi-GUEST", "");
  while (WiFi.status() != WL_CONNECTED) delay(250);
  mqtt.setServer("broker.hivemq.com", 1883);
  mqtt.setCallback(received);
}

void loop() {
  if (!mqtt.connected()) reconnect();
  mqtt.loop();
  delay(10);
}
''', c, '''
Scoreboard on an LED ring, over MQTT, on an ESP-14.

What this demo shows:
- the ESP-14 inside: of the outside pins, only GPIO0 belongs to the
  ESP8266, and one pin is enough for a NeoPixel ring;
- points arriving over MQTT and the score published as a retained
  message.

Press F5 and, in the NETWORK panel, publish "a" or "b" to
scoreboard/point (and anything to scoreboard/reset to start over).
''')
demo('esp14-placar', 'Placar no anel de LEDs, pelo MQTT', 'ESP-14')


# =====================================================================
#  Franzininho: vela eletronica
# =====================================================================

c = Circuito('placa', 'franzininho', 40, 60)
c.led('chama1', 'PB0', 'GND.1', 'amarelo', 400, 20, AMARELO)
c.led('chama2', 'PB1', 'GND.1', 'laranja', 480, 20, LARANJA)
c.botao('sopro', 'PB2', 'GND.1', 400, 160)

gravar('franzininho-vela', r'''
// Electronic candle, on the Franzininho.
//
// Two LEDs (yellow and orange) flicker like a candle flame, with a random
// brightness at every instant. The button "blows out" the candle -- it
// fades away slowly -- and lights it again on the next press.
//
// PB0 and PB1 have PWM: analogWrite is what makes the flame vary.

const int YELLOW_FLAME = 0;   // PB0
const int ORANGE_FLAME = 1;   // PB1
const int BUTTON = 2;         // PB2

bool lit = true;
int lastButton = HIGH;

void fadeOut() {
  for (int brightness = 200; brightness >= 0; brightness -= 5) {
    analogWrite(YELLOW_FLAME, brightness);
    analogWrite(ORANGE_FLAME, brightness / 2);
    delay(15);
  }
}

void setup() {
  pinMode(YELLOW_FLAME, OUTPUT);
  pinMode(ORANGE_FLAME, OUTPUT);
  pinMode(BUTTON, INPUT_PULLUP);
}

void loop() {
  int button = digitalRead(BUTTON);
  if (lastButton == HIGH && button == LOW) {
    lit = !lit;
    if (!lit) fadeOut();
  }
  lastButton = button;

  if (lit) {
    // The flame: a base brightness with a random flicker on top.
    analogWrite(YELLOW_FLAME, random(120, 256));
    analogWrite(ORANGE_FLAME, random(40, 140));
    delay(random(30, 110));
  } else {
    delay(20);
  }
}
''', c, '''
Electronic candle, on the Franzininho.

What this demo shows:
- PWM with analogWrite on the ATtiny85 pins that have PWM (PB0 and PB1);
- random() bringing the flame to life, on a chip with half a KB of RAM.

Press F5, then the button to blow out and relight the candle.
''')
demo('franzininho-vela', 'Vela eletrônica', 'Franzininho')


# =====================================================================
#  ATtiny85: luz noturna automatica
# =====================================================================

c = Circuito('placa', 'attiny85', 120, 120)
c.peca('ldr', 'photoresistor-sensor', 300, 20, Giro=giro_lux(3))
c.ligar('ldr', 'VCC', 'placa', 'VCC', VERMELHO)
c.ligar('ldr', 'GND', 'placa', 'GND', PRETO)
c.ligar('ldr', 'AO', 'placa', 'PB3', AMARELO)
c.led('luz', 'PB0', 'GND', 'branco', 330, 150, BRANCO)

gravar('attiny85-luz-noturna', r'''
// Automatic night light, on the ATtiny85.
//
// The LDR module measures the ambient light. During the day the LED stays
// off; as it gets dark it slowly turns on, and in the dark it is at full
// brightness.
//
// Like the real module: MORE light, LOWER the reading on AO.

const int SENSOR = A3;   // PB3
const int LED = 0;       // PB0, which has PWM

void setup() {
  pinMode(LED, OUTPUT);
}

void loop() {
  int reading = analogRead(SENSOR);   // near 0 in sunlight, near 1023 in the dark

  // Below 600 it is day: off. From 600 to 1000, it slowly turns on.
  int brightness = 0;
  if (reading > 600) brightness = map(constrain(reading, 600, 1000), 600, 1000, 10, 255);

  analogWrite(LED, brightness);
  delay(100);
}
''', c, '''
Automatic night light, on the ATtiny85.

What this demo shows:
- the bare ATtiny85 chip, with the package names (A3 is PB3);
- the LDR module with real physics: more light, lower reading.

The demo starts at night (3 lux) and the LED is on. Click the LDR module
and raise the light to watch the LED turn off.
''')
demo('attiny85-luz-noturna', 'Luz noturna automática', 'ATtiny85')


# =====================================================================
#  Raspberry Pi Pico: piano (Arduino)
# =====================================================================

c = Circuito('placa', 'pi-pico', 60, 60)
for i, pino in enumerate(['GP2', 'GP3', 'GP4', 'GP5', 'GP6', 'GP7', 'GP8', 'GP9']):
    c.botao(f'tecla{i + 1}', pino, 'GND.3', 260 + (i % 4) * 90, 40 + (i // 4) * 90, BRANCO)
c.buzzer('buzzer', 'GP15', 'GND.4', 420, 240)

gravar('pico-piano', r'''
// Eight-key piano, on the Raspberry Pi Pico (Arduino).
//
// Each button is a note of the C major scale. Hold it for the note to
// last; release it and the note stops. The serial monitor shows the note's
// name.

const int KEYS[] = {2, 3, 4, 5, 6, 7, 8, 9};
const int NOTES[] = {262, 294, 330, 349, 392, 440, 494, 523};
const char* NAMES[] = {"C", "D", "E", "F", "G", "A", "B", "high C"};
const int BUZZER = 15;

int playing = -1;

void setup() {
  Serial.begin(115200);
  for (int i = 0; i < 8; i++) pinMode(KEYS[i], INPUT_PULLUP);
}

void loop() {
  int pressed = -1;
  for (int i = 0; i < 8; i++) {
    if (digitalRead(KEYS[i]) == LOW) {
      pressed = i;
      break;
    }
  }

  if (pressed != playing) {
    if (pressed < 0) {
      noTone(BUZZER);
    } else {
      tone(BUZZER, NOTES[pressed]);
      Serial.println(NAMES[pressed]);
    }
    playing = pressed;
  }
  delay(10);
}
''', c, '''
Eight-key piano, on the Raspberry Pi Pico (Arduino).

What this demo shows:
- the Pico programmed with the Arduino core (the pin number is the GP
  number);
- tone() and noTone() following the key being held.

Press F5 and click the keys (hold for the note to last).
''')
demo('pico-piano', 'Piano de oito teclas (Arduino)', 'Raspberry Pi Pico')


# =====================================================================
#  Raspberry Pi Pico: joystick e OLED (MicroPython)
# =====================================================================

c = Circuito('placa', 'pi-pico', 60, 60)
c.peca('joy', 'analog-joystick', 280, 20, Giro=0.5, Giro2=0.5)
c.ligar('joy', 'VCC', 'placa', '3V3', VERMELHO)
c.ligar('joy', 'HORZ', 'placa', 'GP26', AMARELO)
c.ligar('joy', 'VERT', 'placa', 'GP27', VERDE)
c.ligar('joy', 'SEL', 'placa', 'GP22', AZUL)
c.ligar('joy', 'GND', 'placa', 'GND.6', PRETO)
c.peca('oled', 'ssd1306-i2c', 280, 200)
c.ligar('oled', 'GND', 'placa', 'GND.2', PRETO)
c.ligar('oled', 'VCC', 'placa', '3V3', VERMELHO)
c.ligar('oled', 'SDA', 'placa', 'GP4', AZUL)
c.ligar('oled', 'SCL', 'placa', 'GP5', ROXO)

gravar('pico-joystick-oled', r'''
# Joystick and OLED on the Raspberry Pi Pico, in MicroPython.
#
# The joystick moves a symbol around the OLED screen, and its button
# changes the symbol. The raw readings show on the top line.
#
# On the Pico, ADC(0) is channel 0 -- GP26 -- and read_u16 goes from 0 to
# 65535.

from machine import Pin, ADC, I2C
import ssd1306
import time

axis_x = ADC(0)          # GP26
axis_y = ADC(1)          # GP27
button = Pin(22, Pin.IN, Pin.PULL_UP)

i2c = I2C(0, sda=Pin(4), scl=Pin(5))
oled = ssd1306.SSD1306_I2C(128, 64, i2c)

symbols = ["o", "*", "+", "@"]
which = 0
before = 1

while True:
    x = axis_x.read_u16()
    y = axis_y.read_u16()

    now = button.value()
    if before == 1 and now == 0:
        which = (which + 1) % len(symbols)
    before = now

    # 0..65535 becomes column 0..20 and row 2..7 (the top ones hold text).
    column = x * 20 // 65535
    row = 2 + y * 5 // 65535

    oled.fill(0)
    oled.text("X " + str(x) + " Y " + str(y), 0, 0)
    oled.text(symbols[which], column * 6, row * 8)
    oled.show()
    time.sleep_ms(50)
''', c, '''
Joystick and OLED on the Raspberry Pi Pico, in MicroPython.

What this demo shows:
- MicroPython on the Pico: ADC by channel (ADC(0) is GP26), read_u16 from
  0 to 65535 and the ssd1306 driver on I2C 0;
- an analog input turning into a position on the screen.

Press F5 and click the joystick to move the X and Y axes.
''')
demo('pico-joystick-oled', 'Joystick e OLED (MicroPython)', 'Raspberry Pi Pico')


# =====================================================================
#  Raspberry Pi Pico: LED respirando com PWM (SDK em C)
# =====================================================================

c = Circuito('placa', 'pi-pico', 60, 60)
c.led('led', 'GP15', 'GND.4', 'azul', 320, 200, AZUL)
c.peca('la', 'logic-analyzer', 300, 20, Nome='pwm', JanelaUs=50000)
c.ligar('placa', 'GP15', 'la', 'D0', AZUL)
c.ligar('la', 'GND', 'placa', 'GND.4', PRETO)

gravar('pico-sdk-respirar', r'''
// Breathing LED on the Raspberry Pi Pico, with the C SDK.
//
// The RP2040's hardware PWM makes the brightness: the pin is handed to the
// PWM with gpio_set_function, the slice counter counts up to 65535 and the
// level rises and falls. The level is squared -- gamma correction --
// because that is how the eye perceives brightness, and the breathing
// looks smooth.
//
// The logic analyzer is on GP15: open it to watch the pulse width change.

#include "pico/stdlib.h"
#include "hardware/pwm.h"

#define LED 15

int main() {
    stdio_init_all();

    gpio_set_function(LED, GPIO_FUNC_PWM);
    uint slice = pwm_gpio_to_slice_num(LED);

    pwm_config config = pwm_get_default_config();
    pwm_config_set_clkdiv(&config, 16.f);   // 125 MHz / 16
    pwm_config_set_wrap(&config, 65535);     // ~119 Hz
    pwm_init(slice, &config, true);

    int level = 0;
    int step = 1;
    while (true) {
        pwm_set_gpio_level(LED, level * level);   // 0 to 65025
        level += step;
        if (level == 255 || level == 0) step = -step;
        sleep_ms(8);
    }
}
''', c, '''
Breathing LED with the hardware PWM, on the Raspberry Pi Pico (C SDK).

What this demo shows:
- the Raspberry SDK: main() instead of setup/loop, gpio_set_function, the
  PWM slice configured through a struct and the level with gamma
  correction;
- the logic analyzer showing the PWM duty cycle rise and fall.

Press F5 and open the analyzer (select it and click "Open the
analyzer").
''')
demo('pico-sdk-respirar', 'LED respirando com PWM (SDK em C)', 'Raspberry Pi Pico')


# =====================================================================
#  Raspberry Pi Pico W: estacao do tempo na web (Arduino)
# =====================================================================

c = Circuito('placa', 'pi-pico-w', 60, 60)
c.peca('dht', 'dht22', 300, 60, Giro=giro_dht22(23.5), Giro2=giro2_umidade(58))
c.ligar('dht', 'VCC', 'placa', '3V3', VERMELHO)
c.ligar('dht', 'SDA', 'placa', 'GP16', AMARELO)
c.ligar('dht', 'GND', 'placa', 'GND.7', PRETO)

gravar('picow-estacao-web', r'''
// Weather station on the web, on the Raspberry Pi Pico W (Arduino).
//
// The DHT22 measures temperature and humidity, and the Pico W serves two
// things:
//   /       a page that refreshes itself every 5 s
//   /data   the same numbers in JSON, for another program to read

#include <WiFi.h>
#include <WebServer.h>
#include <DHT.h>

DHT dht(16, DHT22);
WebServer server(80);

float temperature = 0;
float humidity = 0;
unsigned long lastReading = 0;

void readSensor() {
  float t = dht.readTemperature();
  float h = dht.readHumidity();
  if (!isnan(t)) temperature = t;
  if (!isnan(h)) humidity = h;
}

void page() {
  String html = "<!DOCTYPE html><html><head><meta charset='utf-8'>";
  html += "<meta http-equiv='refresh' content='5'><title>Station</title></head>";
  html += "<body style='font-family:sans-serif;text-align:center'>";
  html += "<h2>Weather station (Pico W)</h2>";
  html += "<p style='font-size:3em'>" + String(temperature, 1) + " &deg;C</p>";
  html += "<p style='font-size:2em'>" + String(humidity, 0) + " % humidity</p>";
  html += "<p><a href='/data'>view as JSON</a></p></body></html>";
  server.send(200, "text/html", html);
}

void data() {
  String json = "{\"temperature\":" + String(temperature, 1)
              + ",\"humidity\":" + String(humidity, 0)
              + ",\"uptime_seconds\":" + String(millis() / 1000) + "}";
  server.send(200, "application/json", json);
}

void setup() {
  Serial.begin(115200);
  dht.begin();
  readSensor();

  WiFi.begin("Wokwi-GUEST", "");
  while (WiFi.status() != WL_CONNECTED) delay(250);
  Serial.print("open http://");
  Serial.println(WiFi.localIP());

  server.on("/", page);
  server.on("/data", data);
  server.begin();
}

void loop() {
  server.handleClient();
  if (millis() - lastReading > 2000) {
    lastReading = millis();
    readSensor();
    Serial.print(temperature, 1);
    Serial.print(" C  ");
    Serial.print(humidity, 0);
    Serial.println(" %");
  }
  delay(2);
}
''', c, '''
Weather station on the web, on the Raspberry Pi Pico W (Arduino).

What this demo shows:
- the Pico W on WiFi with the Arduino core, serving a page and a JSON
  address (/data) for another program to read;
- the DHT22 read every 2 s, without blocking the server.

Press F5 and open the address from the NETWORK panel in a browser.
''')
demo('picow-estacao-web', 'Estação do tempo na web (Arduino)', 'Raspberry Pi Pico W')


# =====================================================================
#  Raspberry Pi Pico W: MQTT em MicroPython
# =====================================================================

c = Circuito('placa', 'pi-pico-w', 60, 60)
c.peca('pot', 'potentiometer', 300, 60, Giro=0.4)
c.ligar('pot', 'VCC', 'placa', '3V3', VERMELHO)
c.ligar('pot', 'SIG', 'placa', 'GP26', AMARELO)
c.ligar('pot', 'GND', 'placa', 'AGND', PRETO)
c.led('led', 'GP15', 'GND.4', 'verde', 330, 200, VERDE)

gravar('picow-mqtt', r'''
# MQTT in MicroPython, on the Raspberry Pi Pico W.
#
# Every 2 s the board publishes the potentiometer position (0 to 100 %) to
# "pico/pot", and obeys the topic "pico/led": "on" lights it, "off" turns
# it off.

import network
import time
import machine
import ubinascii
from machine import Pin, ADC
from umqtt.simple import MQTTClient

led = Pin(15, Pin.OUT)
pot = ADC(0)              # GP26

wlan = network.WLAN(network.STA_IF)
wlan.active(True)
wlan.connect("Wokwi-GUEST", "")
while not wlan.isconnected():
    time.sleep_ms(250)
print("connected:", wlan.ifconfig()[0])

def received(topic, message):
    text = message.decode()
    print("got", topic.decode(), text)
    if text == "on":
        led.value(1)
    elif text == "off":
        led.value(0)

# One id per board: on the public broker, two with the same id knock each
# other off -- and the whole class runs this same code.
client = MQTTClient(b"pico-w-" + ubinascii.hexlify(machine.unique_id()), "broker.hivemq.com")
client.set_callback(received)
client.connect()
client.subscribe(b"pico/led")
print("ready: publish on or off to pico/led")

last = 0
while True:
    client.check_msg()
    if time.ticks_diff(time.ticks_ms(), last) > 2000:
        last = time.ticks_ms()
        percent = pot.read_u16() * 100 // 65535
        client.publish(b"pico/pot", str(percent))
        print("pot:", percent, "%")
    time.sleep_ms(20)
''', c, '''
MQTT in MicroPython, on the Raspberry Pi Pico W.

What this demo shows:
- WiFi with network.WLAN and MQTT with umqtt.simple, the MicroPython way;
- publishing a reading and obeying a topic in the same loop, with
  check_msg() and ticks_diff().

Press F5 and, in the NETWORK panel, publish "on" or "off" to pico/led.
Turn the potentiometer and watch pico/pot change.
''')
demo('picow-mqtt', 'MQTT em MicroPython', 'Raspberry Pi Pico W')


# =====================================================================
#  Raspberry Pi Pico W: voltimetro com o ADC (SDK em C)
# =====================================================================

c = Circuito('placa', 'pi-pico-w', 60, 60)
c.peca('pot', 'potentiometer', 300, 60, Giro=0.6)
c.ligar('pot', 'VCC', 'placa', '3V3', VERMELHO)
c.ligar('pot', 'SIG', 'placa', 'GP26', AMARELO)
c.ligar('pot', 'GND', 'placa', 'AGND', PRETO)
c.led('alerta', 'GP15', 'GND.4', 'vermelho', 330, 200, VERMELHO)

gravar('picow-sdk-voltimetro', r'''
// Voltmeter on the Raspberry Pi Pico W, with the C SDK.
//
// The ADC reads the potentiometer voltage on GP26 (channel 0), in 12 bits,
// and printf shows it in volts. Above 2.5 V the alert LED lights. Every
// 2 s, the temperature inside the chip (channel 4) shows up too.

#include <stdio.h>
#include "pico/stdlib.h"
#include "hardware/adc.h"

#define ALERT 15
#define LIMIT 2.5f

int main() {
    stdio_init_all();

    gpio_init(ALERT);
    gpio_set_dir(ALERT, GPIO_OUT);

    adc_init();
    adc_gpio_init(26);
    adc_set_temp_sensor_enabled(true);

    int rounds = 0;
    while (true) {
        adc_select_input(0);
        uint16_t raw = adc_read();
        float volts = raw * 3.3f / 4096;
        gpio_put(ALERT, volts > LIMIT);
        printf("%.2f V (reading %d)%s\n", volts, raw, volts > LIMIT ? "  ALERT" : "");

        rounds++;
        if (rounds % 8 == 0) {
            adc_select_input(4);
            float degrees = 27 - (adc_read() * 3.3f / 4096 - 0.706f) / 0.001721f;
            printf("chip temperature: %.1f C\n", degrees);
        }
        sleep_ms(250);
    }
}
''', c, '''
Voltmeter with the ADC, on the Raspberry Pi Pico W (C SDK).

What this demo shows:
- the 12-bit converter through the SDK: adc_select_input and adc_read,
  with the math that turns the reading into volts;
- the temperature sensor inside the RP2040, on channel 4.

Press F5 and turn the potentiometer. Above 2.5 V the LED lights.
''')
demo('picow-sdk-voltimetro', 'Voltímetro com o ADC (SDK em C)', 'Raspberry Pi Pico W')


# =====================================================================
#  ST Nucleo-L031K6: termometro com NTC e alarme
# =====================================================================

c = Circuito('placa', 'st-nucleo-l031k6', 60, 40)
c.peca('ntc', 'ntc-temperature-sensor', 230, 60, Giro=giro_ntc(26))
c.ligar('ntc', 'GND', 'placa', 'GND', PRETO)
c.ligar('ntc', 'VCC', 'placa', '3V3', VERMELHO)
c.ligar('ntc', 'OUT', 'placa', 'A0', AMARELO)
c.led('ok', 'D4', 'GND.2', 'verde', 330, 180, VERDE)
c.led('quente', 'D5', 'GND.2', 'vermelho', 430, 180, VERMELHO)
c.buzzer('buzzer', 'D3', 'GND.2', 520, 170)

gravar('nucleo-l031-termometro', r'''
// Thermometer with an NTC and an alarm, on the ST Nucleo-L031K6.
//
// The module's NTC sits in a voltage divider; the formula below (the one
// from the module's example, with beta 3950) turns the reading into
// degrees. Above 30 C the red LED lights and the buzzer beeps; below it,
// the green one stays on.
//
// The STM32 ADC has 12 bits, but the Arduino core delivers 10 by default.
// analogReadResolution(12) asks for all 12 -- and the formula uses 4095.

const float BETA = 3950;
const float LIMIT = 30.0;

const int SENSOR = A0;
const int GREEN = 4;           // D4
const int RED = 5;             // D5
const int BUZZER = 3;          // D3

void setup() {
  Serial.begin(115200);
  pinMode(GREEN, OUTPUT);
  pinMode(RED, OUTPUT);
  analogReadResolution(12);
}

void loop() {
  int reading = analogRead(SENSOR);
  float celsius = 1 / (log(1 / (4095.0 / reading - 1)) / BETA + 1.0 / 298.15) - 273.15;

  bool hot = celsius > LIMIT;
  digitalWrite(GREEN, !hot);
  digitalWrite(RED, hot);
  if (hot) tone(BUZZER, 2200, 150);

  Serial.print("temperature: ");
  Serial.print(celsius, 1);
  Serial.println(hot ? " C  <- ALARM" : " C");
  delay(1000);
}
''', c, '''
Thermometer with an NTC and an alarm, on the ST Nucleo-L031K6.

What this demo shows:
- the NTC thermistor and the beta formula every tutorial uses -- the
  reading here follows the sensor's real curve;
- analogReadResolution(12) on the STM32, whose core delivers 10 bits by
  default.

Press F5 and click the NTC sensor to change the temperature (above 30 C
sets off the alarm).
''')
demo('nucleo-l031-termometro', 'Termômetro com NTC e alarme', 'ST Nucleo-L031K6')


# =====================================================================
#  ST Nucleo-C031C6: relogio com RTC e LCD
# =====================================================================

c = Circuito('placa', 'st-nucleo-c031c6', 60, 40)
c.peca('rtc', 'ds1307', 260, 20)
c.ligar('rtc', 'GND', 'placa', 'GND', PRETO)
c.ligar('rtc', '5V', 'placa', '5V', VERMELHO)
c.ligar('rtc', 'SDA', 'placa', 'D14', AZUL)
c.ligar('rtc', 'SCL', 'placa', 'D15', ROXO)
c.peca('lcd', 'lcd1602-i2c', 240, 150)
c.ligar('lcd', 'GND', 'placa', 'GND.2', PRETO)
c.ligar('lcd', 'VCC', 'placa', '5V', VERMELHO)
c.ligar('lcd', 'SDA', 'placa', 'D14', AZUL)
c.ligar('lcd', 'SCL', 'placa', 'D15', ROXO)
c.botao('botao', 'D7', 'GND.3', 400, 20)

gravar('nucleo-c031-relogio-rtc', r'''
// Clock with an RTC and an LCD, on the ST Nucleo-C031C6.
//
// The DS1307 keeps the time even with the board off (its battery holds
// it). The LCD shows the time and the date, and the button switches
// between 24 h and 12 h. The RTC and the LCD share the same I2C -- D14 and
// D15, like on the Uno.
//
// The first time, the RTC is set to the time the sketch was compiled
// (__DATE__ and __TIME__).

#include <Wire.h>
#include <RTClib.h>
#include <LiquidCrystal_I2C.h>

RTC_DS1307 rtc;
LiquidCrystal_I2C lcd(0x27, 16, 2);

const int BUTTON = 7;   // D7
const char* DAYS[] = {"Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"};

bool twelveHour = false;
int lastButton = HIGH;

void twoDigits(int n) {
  if (n < 10) lcd.print('0');
  lcd.print(n);
}

void setup() {
  Serial.begin(115200);
  pinMode(BUTTON, INPUT_PULLUP);
  lcd.init();
  lcd.backlight();

  if (!rtc.begin()) {
    lcd.print("RTC not found");
    while (true) delay(1000);
  }
  if (!rtc.isrunning()) {
    rtc.adjust(DateTime(F(__DATE__), F(__TIME__)));
  }
}

void loop() {
  int button = digitalRead(BUTTON);
  if (lastButton == HIGH && button == LOW) twelveHour = !twelveHour;
  lastButton = button;

  DateTime now = rtc.now();

  lcd.setCursor(0, 0);
  if (twelveHour) twoDigits(now.twelveHour());
  else twoDigits(now.hour());
  lcd.print(':');
  twoDigits(now.minute());
  lcd.print(':');
  twoDigits(now.second());
  lcd.print(twelveHour ? (now.isPM() ? " PM   " : " AM   ") : "      ");

  lcd.setCursor(0, 1);
  lcd.print(DAYS[now.dayOfTheWeek()]);
  lcd.print(' ');
  twoDigits(now.day());
  lcd.print('/');
  twoDigits(now.month());
  lcd.print('/');
  lcd.print(now.year());

  delay(200);
}
''', c, '''
Clock with an RTC and an LCD, on the ST Nucleo-C031C6.

What this demo shows:
- the DS1307 RTC with RTClib: DateTime, now() and adjust();
- two devices on the same I2C (RTC and LCD, D14 and D15 on the Uno
  header).

Press F5. The button switches between 24 h and 12 h.
''')
demo('nucleo-c031-relogio-rtc', 'Relógio com RTC e LCD', 'ST Nucleo-C031C6')


# =====================================================================
#  Os pisca-piscas que estas substituem
# =====================================================================

apagar(['esp32s2-pisca', 'esp32s3-pisca', 'esp32c3-pisca', 'esp32c6-pisca', 'esp32h2-pisca',
        'nodemcu-pisca', 'attiny85-pisca', 'nucleo-l031-pisca', 'nucleo-c031-pisca',
        'picow-pisca', 'franzininho-pisca', 'pico-micropython',
        'pico-arduino', 'pico-upython', 'pico-sdk', 'picow-arduino', 'picow-upython', 'picow-sdk']
       + [f'esp-{n}-pisca' for n in ['01', '02', '03', '04', '05', '06', '07', '08', '09', '10',
                                      '11', '12', '12e', '12f', '13', '14']])

print(f'\n{len(DEMOS)} demos de placa')
print('\n--- para o Recentes.cs ---')
for recurso, nome, placa in DEMOS:
    print(f'        new("{recurso}", "{nome}", "{placa}"),')
