// Estacao IoT: sonar, temperatura/umidade e fita de LEDs.
// Exercita pulseIn(), objeto DHT e Adafruit_NeoPixel.

#include <DHT.h>
#include <Adafruit_NeoPixel.h>

const int TRIG = 26;
const int ECHO = 27;
const int PINO_DHT = 4;
const int PINO_FITA = 5;

DHT dht(PINO_DHT, DHT22);
Adafruit_NeoPixel fita(8, PINO_FITA, NEO_GRB + NEO_KHZ800);

unsigned long ultimo = 0;

int medirDistancia() {
  digitalWrite(TRIG, LOW);
  delayMicroseconds(2);
  digitalWrite(TRIG, HIGH);
  delayMicroseconds(10);
  digitalWrite(TRIG, LOW);

  long duracao = pulseIn(ECHO, HIGH);
  return duracao / 58;
}

void setup() {
  Serial.begin(115200);
  pinMode(TRIG, OUTPUT);
  pinMode(ECHO, INPUT);
  dht.begin();
  fita.begin();
  fita.setBrightness(120);
}

void loop() {
  if (millis() - ultimo < 500) return;
  ultimo = millis();

  int cm = medirDistancia();
  float temp = dht.readTemperature();
  float umid = dht.readHumidity();

  // Quanto mais perto, mais LEDs acesos.
  int acesos = map(cm, 0, 200, 8, 0);
  fita.clear();
  for (int i = 0; i < acesos; i++) {
    fita.setPixelColor(i, fita.Color(255 - i * 30, i * 30, 0));
  }
  fita.show();

  Serial.print("dist=");
  Serial.print(cm);
  Serial.print("cm  temp=");
  Serial.print(temp);
  Serial.print("C  umid=");
  Serial.print(umid);
  Serial.print("%  leds=");
  Serial.println(acesos);
}
