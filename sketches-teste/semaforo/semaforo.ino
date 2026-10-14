// ===================================================================
// Simulador de motor com ESP32
// Potenciometro = acelerador | 3 speakers = som do motor
// LEDs verde/amarelo/vermelho = luzes de marcha (shift lights)
// 2 botoes = troca de marcha | 1 botao = ligar/desligar o carro
// LCD 16x2 I2C = mostra RPM, marcha e status do motor
//
// Requer a biblioteca "LiquidCrystal I2C" (Frank de Brabander) —
// instale pelo Gerenciador de Bibliotecas do Arduino IDE.
// ===================================================================

#include <LiquidCrystal.h>

// LCD paralelo 16x2 (16 pinos, sem módulo I2C atrás)
// Pinos: RS, E, D4, D5, D6, D7 (modo 4-bit)
LiquidCrystal lcd(5, 18, 19, 21, 22, 23);

// --- Potenciometro (acelerador) ---
const int PINO_POTENCIOMETRO = 34;

// --- Speakers (som do motor) ---
const int SPEAKER_1 = 25;
const int SPEAKER_2 = 26;
const int SPEAKER_3 = 27;

// --- LEDs (luzes de marcha) ---
const int LED_VERDE    = 14;
const int LED_AMARELO  = 13;
const int LED_VERMELHO = 16;

// --- Botoes ---
const int BOTAO_MARCHA_CIMA  = 32;
const int BOTAO_MARCHA_BAIXO = 33;
const int BOTAO_LIGAR        = 4; // liga/desliga o carro

// --- Parametros do motor ---
const float RPM_MARCHA_LENTA = 800;   // rpm de ralenti
const float RPM_MAX          = 8000;  // redline
const float TAXA_SUBIDA_RPM  = 40;    // rpm ganhos por tick com acelerador todo
const float TAXA_QUEDA_RPM   = 15;    // rpm perdidos por tick sem acelerador
const float QUEDA_AO_SUBIR_MARCHA   = 1500;
const float SUBIDA_AO_DESCER_MARCHA = 800;

const int MARCHA_MIN = 1;
const int MARCHA_MAX = 8;

float rpmAtual = 0; // carro comeca desligado
int marchaAtual = MARCHA_MIN;
bool motorLigado = false;

// --- Debounce dos botoes ---
const unsigned long DEBOUNCE_MS = 200;
unsigned long ultimoDebounceCima = 0;
unsigned long ultimoDebounceBaixo = 0;
unsigned long ultimoDebounceLigar = 0;

// --- Tick da simulação ---
const unsigned long INTERVALO_TICK = 20; // ms
unsigned long ultimoTick = 0;

// --- Blink do vermelho perto do limite ---
unsigned long ultimoBlinkVermelho = 0;
bool faseBlinkVermelho = false;
const unsigned long INTERVALO_BLINK = 80; // ms

// --- Corte de giro (rev limiter) ---
const unsigned long INTERVALO_CORTE = 60; // ms de cada fase do corte
unsigned long ultimaTrocaCorte = 0;
bool faseSomCorte = true;

// --- LCD ---
const unsigned long INTERVALO_LCD = 200; // ms
unsigned long ultimoLCD = 0;

// --- Mensagem de boas-vindas ao ligar ---
const char* MENSAGEM_LIGAR = "AUDI REVOLUT";
const unsigned long DURACAO_BOAS_VINDAS = 2000; // ms
bool mostrandoBoasVindas = false;
unsigned long inicioBoasVindas = 0;

void setup() {
  Serial.begin(115200);
  delay(300);

  pinMode(SPEAKER_1, OUTPUT);
  pinMode(SPEAKER_2, OUTPUT);
  pinMode(SPEAKER_3, OUTPUT);

  pinMode(LED_VERDE, OUTPUT);
  pinMode(LED_AMARELO, OUTPUT);
  pinMode(LED_VERMELHO, OUTPUT);

  pinMode(BOTAO_MARCHA_CIMA, INPUT_PULLUP);
  pinMode(BOTAO_MARCHA_BAIXO, INPUT_PULLUP);
  pinMode(BOTAO_LIGAR, INPUT_PULLUP);

  lcd.begin(16, 2);
  lcd.setCursor(0, 0);
  lcd.print("Motor desligado");
  lcd.setCursor(0, 1);
  lcd.print("Aperte p/ ligar");

  Serial.println("Simulador pronto - motor desligado");
}

void loop() {
  verificarBotaoLigar();

  if (motorLigado) {
    verificarBotoesMarcha();
  }

  if (millis() - ultimoTick >= INTERVALO_TICK) {
    ultimoTick = millis();
    if (motorLigado) {
      atualizarRPM();
      atualizarLuzesDeMarcha();
    } else {
      apagarLuzes();
    }
    atualizarSom();
  }

  atualizarLCD();
}

// --- Botao de ligar/desligar o carro ---
void verificarBotaoLigar() {
  if (digitalRead(BOTAO_LIGAR) == LOW && millis() - ultimoDebounceLigar > DEBOUNCE_MS) {
    ultimoDebounceLigar = millis();
    motorLigado = !motorLigado;

    if (motorLigado) {
      rpmAtual = RPM_MARCHA_LENTA;
      marchaAtual = MARCHA_MIN;
      mostrandoBoasVindas = true;
      inicioBoasVindas = millis();
      Serial.println("Motor ligado");
    } else {
      rpmAtual = 0;
      Serial.println("Motor desligado");
    }
    while (digitalRead(BOTAO_LIGAR) == LOW) delay(10);
  }
}

// --- Botoes de troca de marcha ---
void verificarBotoesMarcha() {
  if (digitalRead(BOTAO_MARCHA_CIMA) == LOW && millis() - ultimoDebounceCima > DEBOUNCE_MS) {
    ultimoDebounceCima = millis();
    if (marchaAtual < MARCHA_MAX) {
      marchaAtual++;
      rpmAtual -= QUEDA_AO_SUBIR_MARCHA;
      if (rpmAtual < RPM_MARCHA_LENTA) rpmAtual = RPM_MARCHA_LENTA;
      Serial.print("Subiu para marcha ");
      Serial.println(marchaAtual);
    } else {
      Serial.println("Ja esta na marcha maxima");
    }
    while (digitalRead(BOTAO_MARCHA_CIMA) == LOW) delay(10);
  }

  if (digitalRead(BOTAO_MARCHA_BAIXO) == LOW && millis() - ultimoDebounceBaixo > DEBOUNCE_MS) {
    ultimoDebounceBaixo = millis();
    if (marchaAtual > MARCHA_MIN) {
      marchaAtual--;
      rpmAtual += SUBIDA_AO_DESCER_MARCHA;
      if (rpmAtual > RPM_MAX) rpmAtual = RPM_MAX;
      Serial.print("Desceu para marcha ");
      Serial.println(marchaAtual);
    } else {
      Serial.println("Ja esta na marcha minima");
    }
    while (digitalRead(BOTAO_MARCHA_BAIXO) == LOW) delay(10);
  }
}

// --- RPM em função do acelerador ---
void atualizarRPM() {
  int leitura = analogRead(PINO_POTENCIOMETRO);
  float acelerador = leitura / 4095.0;

  float variacao = (acelerador * TAXA_SUBIDA_RPM) - ((1.0 - acelerador) * TAXA_QUEDA_RPM);
  rpmAtual += variacao;

  if (rpmAtual < RPM_MARCHA_LENTA) rpmAtual = RPM_MARCHA_LENTA;
  if (rpmAtual > RPM_MAX) rpmAtual = RPM_MAX; // corte de giro: nunca passa do limite
}

// --- Luzes de marcha ---
void atualizarLuzesDeMarcha() {
  float fracao = rpmAtual / RPM_MAX;

  if (fracao < 0.6) {
    digitalWrite(LED_VERDE, HIGH);
    digitalWrite(LED_AMARELO, LOW);
    digitalWrite(LED_VERMELHO, LOW);
  } else if (fracao < 0.85) {
    digitalWrite(LED_VERDE, LOW);
    digitalWrite(LED_AMARELO, HIGH);
    digitalWrite(LED_VERMELHO, LOW);
  } else if (fracao < 0.95) {
    digitalWrite(LED_VERDE, LOW);
    digitalWrite(LED_AMARELO, LOW);
    digitalWrite(LED_VERMELHO, HIGH);
  } else {
    digitalWrite(LED_VERDE, LOW);
    digitalWrite(LED_AMARELO, LOW);
    if (millis() - ultimoBlinkVermelho >= INTERVALO_BLINK) {
      ultimoBlinkVermelho = millis();
      faseBlinkVermelho = !faseBlinkVermelho;
    }
    digitalWrite(LED_VERMELHO, faseBlinkVermelho);
  }
}

void apagarLuzes() {
  digitalWrite(LED_VERDE, LOW);
  digitalWrite(LED_AMARELO, LOW);
  digitalWrite(LED_VERMELHO, LOW);
}

// --- Som do motor (com corte de giro no limite) ---
void atualizarSom() {
  if (!motorLigado) {
    noTone(SPEAKER_1);
    noTone(SPEAKER_2);
    noTone(SPEAKER_3);
    return;
  }

  bool cortando = (rpmAtual >= RPM_MAX);

  if (cortando) {
    if (millis() - ultimaTrocaCorte >= INTERVALO_CORTE) {
      ultimaTrocaCorte = millis();
      faseSomCorte = !faseSomCorte;
    }
    if (!faseSomCorte) {
      // fase "muda" do corte de giro: motor gagueja
      noTone(SPEAKER_1);
      noTone(SPEAKER_2);
      noTone(SPEAKER_3);
      return;
    }
  }

  int freqBase = map((int)rpmAtual, (int)RPM_MARCHA_LENTA, (int)RPM_MAX, 80, 400);
  // Os 3 speakers tocam a MESMA frequência, em fase, pra somar o
  // volume dos três em vez de espalhar em tons diferentes.
  tone(SPEAKER_1, freqBase);
  tone(SPEAKER_2, freqBase);
  tone(SPEAKER_3, freqBase);
}

// --- LCD: RPM, marcha e status ---
void atualizarLCD() {
  if (millis() - ultimoLCD < INTERVALO_LCD) return;
  ultimoLCD = millis();

  if (mostrandoBoasVindas) {
    if (millis() - inicioBoasVindas < DURACAO_BOAS_VINDAS) {
      lcd.clear();
      lcd.setCursor(0, 0);
      lcd.print(MENSAGEM_LIGAR);
      lcd.setCursor(0, 1);
      lcd.print("Ligando...");
      return;
    } else {
      mostrandoBoasVindas = false;
    }
  }

  lcd.clear();
  lcd.setCursor(0, 0);

  if (!motorLigado) {
    lcd.print("Motor desligado");
    lcd.setCursor(0, 1);
    lcd.print("Aperte p/ ligar");
    return;
  }

  lcd.print("RPM:");
  lcd.print((int)rpmAtual);
  lcd.print(" M:");
  lcd.print(marchaAtual);

  lcd.setCursor(0, 1);
  if (rpmAtual >= RPM_MAX) {
    lcd.print("CORTE DE GIRO!");
  } else {
    lcd.print("Ligado");
  }
}
