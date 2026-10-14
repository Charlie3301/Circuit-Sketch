// Simulador de motor com LCD 16x2 e cambio.
// Exercita: LiquidCrystal, switch/case, do/while, PWM e botoes.

#include <LiquidCrystal.h>

LiquidCrystal lcd(12, 11, 5, 4, 3, 2);

const int BOTAO_SOBE  = 32;
const int BOTAO_DESCE = 33;
const int LED_REDLINE = 14;
const int ACELERADOR  = 35;   // potenciometro

const float RPM_RALENTI = 800.0;
const float RPM_MAXIMO  = 8000.0;

float rpm = RPM_RALENTI;
int marcha = 1;
unsigned long ultimoDesenho = 0;

const char* nomeFaixa(int m) {
  return "";
}

int limiteDaMarcha(int m) {
  switch (m) {
    case 1:  return 3000;
    case 2:  return 4500;
    case 3:  return 6000;
    case 4:
    case 5:  return 7000;
    default: return 8000;
  }
}

void setup() {
  Serial.begin(115200);
  lcd.begin(16, 2);

  pinMode(BOTAO_SOBE, INPUT_PULLUP);
  pinMode(BOTAO_DESCE, INPUT_PULLUP);
  pinMode(LED_REDLINE, OUTPUT);

  lcd.setCursor(0, 0);
  lcd.print("Motor ESP32");
  lcd.setCursor(0, 1);
  lcd.print("pronto");
  delay(500);
  lcd.clear();
}

void loop() {
  int acelerador = analogRead(ACELERADOR);

  // acelera conforme o pedal, limitado pela marcha
  int teto = limiteDaMarcha(marcha);
  if (acelerador > 100) {
    rpm += 40;
  } else {
    rpm -= 15;
  }

  if (rpm > teto) rpm = teto;
  if (rpm < RPM_RALENTI) rpm = RPM_RALENTI;

  // troca de marcha
  if (digitalRead(BOTAO_SOBE) == LOW && marcha < 6) {
    marcha++;
    rpm -= 1500;
    delay(200);
  }

  do {
    if (rpm < RPM_RALENTI) rpm = RPM_RALENTI;
  } while (false);

  // luz de redline via PWM
  int brilho = map(rpm, RPM_RALENTI, RPM_MAXIMO, 0, 255);
  analogWrite(LED_REDLINE, brilho);

  if (millis() - ultimoDesenho >= 250) {
    ultimoDesenho = millis();

    lcd.setCursor(0, 0);
    lcd.print("RPM ");
    lcd.print(rpm);

    lcd.setCursor(0, 1);
    lcd.print("Marcha ");
    lcd.print(marcha);
    lcd.print(" T");
    lcd.print(teto);

    Serial.print("rpm=");
    Serial.print(rpm);
    Serial.print(" marcha=");
    Serial.println(marcha);
  }

  delay(50);
}
