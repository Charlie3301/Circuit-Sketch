// Teremim simples: o potenciometro define a nota, o botao toca.
// Exercita tone(), analogRead(), INPUT_PULLUP e PWM ao mesmo tempo.

const int BUZZER = 25;
const int BOTAO  = 4;
const int POT    = 32;
const int LED    = 14;

unsigned long ultimoAviso = 0;

void setup() {
  Serial.begin(115200);
  pinMode(BOTAO, INPUT_PULLUP);
  pinMode(LED, OUTPUT);
  pinMode(BUZZER, OUTPUT);
}

void loop() {
  int leitura = analogRead(POT);
  int nota = map(leitura, 0, 4095, 220, 1760);   // de la3 a la6

  // brilho do LED acompanha o potenciometro
  analogWrite(LED, map(leitura, 0, 4095, 0, 255));

  if (digitalRead(BOTAO) == LOW) {
    tone(BUZZER, nota);
  } else {
    noTone(BUZZER);
  }

  if (millis() - ultimoAviso >= 500) {
    ultimoAviso = millis();
    Serial.print("pot=");
    Serial.print(leitura);
    Serial.print("  nota=");
    Serial.print(nota);
    Serial.println(" Hz");
  }

  delay(20);
}
