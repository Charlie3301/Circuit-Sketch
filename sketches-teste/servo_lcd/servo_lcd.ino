// Servo varrendo, com o angulo no LCD 16x2.
// Exercita: objeto Servo, LiquidCrystal, map() e millis().

#include <Servo.h>
#include <LiquidCrystal.h>

Servo braco;
LiquidCrystal lcd(12, 11, 5, 4, 3, 2);

const int PINO_SERVO = 25;
const int POT = 32;

int angulo = 0;
int passo = 3;
unsigned long ultimo = 0;

void setup() {
  Serial.begin(115200);
  braco.attach(PINO_SERVO);
  lcd.begin(16, 2);
  lcd.setCursor(0, 0);
  lcd.print("Servo ESP32");
}

void loop() {
  if (millis() - ultimo >= 60) {
    ultimo = millis();

    angulo += passo;
    if (angulo >= 180) { angulo = 180; passo = -passo; }
    if (angulo <= 0)   { angulo = 0;   passo = -passo; }

    braco.write(angulo);

    lcd.setCursor(0, 1);
    lcd.print("Ang ");
    lcd.print(angulo);
    lcd.print("   ");

    Serial.print("angulo=");
    Serial.println(angulo);
  }
}
