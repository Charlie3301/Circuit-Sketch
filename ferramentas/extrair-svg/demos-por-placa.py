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
// Relogio pela internet, na ESP32-S2.
//
// A placa entra no WiFi, pergunta a hora a um servidor NTP e mostra no
// display OLED. O botao troca entre a hora e a data. Sem pilha e sem
// ajuste: quem acerta o relogio e a internet.
//
// No simulador a hora chega pelo mesmo caminho da placa -- configTime e
// getLocalTime --, e vem do relogio deste computador.

#include <WiFi.h>
#include <time.h>
#include <Wire.h>
#include <Adafruit_GFX.h>
#include <Adafruit_SSD1306.h>

const int BOTAO = 4;

Adafruit_SSD1306 tela(128, 64, &Wire, -1);

// Horario de Brasilia: UTC-3, sem horario de verao.
const long FUSO = -3 * 3600;

bool mostrarData = false;
int botaoAntes = HIGH;

void setup() {
  Serial.begin(115200);
  pinMode(BOTAO, INPUT_PULLUP);

  Wire.begin();   // SDA no GPIO 8 e SCL no 9: os de fabrica da S2
  tela.begin(SSD1306_SWITCHCAPVCC, 0x3C);
  tela.clearDisplay();
  tela.setTextColor(SSD1306_WHITE);
  tela.setCursor(0, 0);
  tela.print("Conectando...");
  tela.display();

  WiFi.begin("Wokwi-GUEST", "");
  while (WiFi.status() != WL_CONNECTED) {
    delay(250);
    Serial.print(".");
  }
  Serial.println();
  Serial.print("conectado, IP ");
  Serial.println(WiFi.localIP());

  configTime(FUSO, 0, "pool.ntp.org", "time.google.com");
}

void loop() {
  int botao = digitalRead(BOTAO);
  if (botaoAntes == HIGH && botao == LOW) mostrarData = !mostrarData;
  botaoAntes = botao;

  struct tm agora;
  if (!getLocalTime(&agora)) {
    Serial.println("esperando a hora do NTP...");
    delay(500);
    return;
  }

  char linha[20];
  if (mostrarData) strftime(linha, sizeof(linha), "%d/%m/%Y", &agora);
  else strftime(linha, sizeof(linha), "%H:%M:%S", &agora);

  tela.clearDisplay();
  tela.setTextSize(1);
  tela.setCursor(0, 0);
  tela.print("Relogio NTP - S2");
  tela.setTextSize(2);
  tela.setCursor(0, 24);
  tela.print(linha);
  tela.setTextSize(1);
  tela.setCursor(0, 56);
  tela.print(mostrarData ? "botao: ver a hora" : "botao: ver a data");
  tela.display();

  delay(200);
}
''', c, '''
Relogio pela internet (NTP) no OLED, na ESP32-S2.

O que esta demo mostra:
- WiFi, NTP e o relogio do sistema (configTime e getLocalTime), do jeito
  que a ESP32 faz na bancada;
- o display OLED no I2C de fabrica da S2 (SDA no 8, SCL no 9).

Aperte F5 e o botao para trocar entre a hora e a data.
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
// Anel de 16 NeoPixels na ESP32-S3, com tres efeitos.
//
// O botao troca o efeito: arco-iris, cometa e respiracao. As cores saem
// das contas da propria biblioteca -- ColorHSV, gamma32 e sine8 --, entao
// o que aparece aqui e o que o anel de verdade mostra.
//
// Na bancada: o anel puxa ate 1 A no branco. Alimente pelo 5V (nunca por
// um pino), com um capacitor de 1000 uF no VCC e 330 ohms no DIN.

#include <Adafruit_NeoPixel.h>

const int PINO_ANEL = 5;
const int BOTAO = 4;
const int QUANTOS = 16;

Adafruit_NeoPixel anel(QUANTOS, PINO_ANEL, NEO_GRB + NEO_KHZ800);

int efeito = 0;          // 0 arco-iris, 1 cometa, 2 respiracao
int botaoAntes = HIGH;
long matiz = 0;
int passo = 0;

void arcoIris() {
  for (int i = 0; i < QUANTOS; i++) {
    long h = matiz + (i * 65536L / QUANTOS);
    anel.setPixelColor(i, anel.gamma32(anel.ColorHSV(h)));
  }
  matiz += 512;
}

void cometa() {
  // Um ponto claro com um rastro que vai apagando.
  for (int i = 0; i < QUANTOS; i++) {
    int distancia = (passo - i + QUANTOS) % QUANTOS;
    int brilho = distancia < 6 ? 255 >> distancia : 0;
    anel.setPixelColor(i, anel.Color(0, brilho / 2, brilho));
  }
  passo = (passo + 1) % QUANTOS;
}

void respiracao() {
  int brilho = anel.sine8(passo * 4);
  anel.fill(anel.Color(brilho, brilho / 4, 0));
  passo++;
}

void setup() {
  Serial.begin(115200);
  pinMode(BOTAO, INPUT_PULLUP);
  anel.begin();
  anel.setBrightness(80);
  Serial.println("Aperte o botao para trocar o efeito");
}

void loop() {
  int botao = digitalRead(BOTAO);
  if (botaoAntes == HIGH && botao == LOW) {
    efeito = (efeito + 1) % 3;
    passo = 0;
    anel.clear();
    Serial.print("efeito: ");
    if (efeito == 0) Serial.println("arco-iris");
    else if (efeito == 1) Serial.println("cometa");
    else Serial.println("respiracao");
  }
  botaoAntes = botao;

  if (efeito == 0) arcoIris();
  else if (efeito == 1) cometa();
  else respiracao();

  anel.show();
  delay(30);
}
''', c, '''
Anel NeoPixel com tres efeitos, na ESP32-S3.

O que esta demo mostra:
- LEDs enderecaveis com a Adafruit_NeoPixel: o arco-iris do ColorHSV com
  gamma32, um cometa e uma respiracao com sine8;
- o show() mandando a fita inteira de uma vez -- sem ele nada acende,
  aqui e na bancada.

Aperte F5 e o botao para trocar o efeito.
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
// Estufa automatica na ESP32-C3.
//
// O DHT22 mede a temperatura e a umidade. Com calor, a janela (o servo)
// abre e o ventilador (LED azul) liga; com frio, o aquecedor (LED
// vermelho) liga e a janela fecha. O LCD mostra tudo.

#include <DHT.h>
#include <Wire.h>
#include <LiquidCrystal_I2C.h>
#include <ESP32Servo.h>

const int PINO_DHT = 4;
const int PINO_JANELA = 5;
const int VENTILADOR = 6;
const int AQUECEDOR = 7;

const float QUENTE = 28.0;
const float FRIO = 18.0;

DHT dht(PINO_DHT, DHT22);
LiquidCrystal_I2C lcd(0x27, 16, 2);
Servo janela;

void setup() {
  Serial.begin(115200);
  pinMode(VENTILADOR, OUTPUT);
  pinMode(AQUECEDOR, OUTPUT);
  dht.begin();
  lcd.init();          // o I2C de fabrica da C3: SDA no 8, SCL no 9
  lcd.backlight();
  janela.attach(PINO_JANELA);
  janela.write(0);
}

void loop() {
  float temperatura = dht.readTemperature();
  float umidade = dht.readHumidity();

  if (isnan(temperatura) || isnan(umidade)) {
    Serial.println("o DHT22 nao respondeu");
    delay(2000);
    return;
  }

  String estado;
  if (temperatura > QUENTE) {
    janela.write(90);
    digitalWrite(VENTILADOR, HIGH);
    digitalWrite(AQUECEDOR, LOW);
    estado = "Ventilando";
  } else if (temperatura < FRIO) {
    janela.write(0);
    digitalWrite(VENTILADOR, LOW);
    digitalWrite(AQUECEDOR, HIGH);
    estado = "Aquecendo";
  } else {
    janela.write(0);
    digitalWrite(VENTILADOR, LOW);
    digitalWrite(AQUECEDOR, LOW);
    estado = "Tudo certo";
  }

  lcd.setCursor(0, 0);
  lcd.print(temperatura, 1);
  lcd.print("C  ");
  lcd.print(umidade, 0);
  lcd.print("%    ");
  lcd.setCursor(0, 1);
  lcd.print(estado);
  lcd.print("        ");

  Serial.print(temperatura, 1);
  Serial.print(" C, ");
  Serial.print(umidade, 0);
  Serial.print(" % -> ");
  Serial.println(estado);

  delay(2000);   // o DHT22 le no maximo uma vez a cada 2 s
}
''', c, '''
Estufa automatica, na ESP32-C3.

O que esta demo mostra:
- o DHT22 (temperatura e umidade), um servo e um LCD I2C na mesma placa;
- decisao por faixa: acima de 28 C ventila, abaixo de 18 C aquece.

A estufa comeca quente (31 C). Clique no DHT22 e baixe a temperatura para
ver a janela fechar e o aquecedor ligar.
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
// Alarme com sensor de presenca, na ESP32-C6.
//
// O botao arma e desarma. Armado (LED verde aceso), qualquer movimento no
// sensor PIR dispara a sirene e pisca o LED vermelho, ate alguem
// desarmar. Depois de armar ha 5 s para sair -- como nos alarmes de
// verdade.

const int PIR = 2;
const int SIRENE = 3;
const int LED_ALARME = 4;
const int LED_ARMADO = 5;
const int BOTAO = 6;

const unsigned long TEMPO_PARA_SAIR = 5000;

enum Estado { DESARMADO, SAINDO, ARMADO, DISPARADO };
Estado estado = DESARMADO;
unsigned long desde = 0;
int botaoAntes = HIGH;

void mudarPara(Estado novo) {
  estado = novo;
  desde = millis();
  noTone(SIRENE);
  digitalWrite(LED_ALARME, LOW);
  digitalWrite(LED_ARMADO, novo == ARMADO || novo == SAINDO);

  if (novo == DESARMADO) Serial.println("desarmado");
  if (novo == SAINDO) Serial.println("armando: 5 s para sair...");
  if (novo == ARMADO) Serial.println("ARMADO");
  if (novo == DISPARADO) Serial.println("*** INVASAO! ***");
}

void setup() {
  Serial.begin(115200);
  pinMode(PIR, INPUT);
  pinMode(LED_ALARME, OUTPUT);
  pinMode(LED_ARMADO, OUTPUT);
  pinMode(BOTAO, INPUT_PULLUP);
  mudarPara(DESARMADO);
}

void loop() {
  int botao = digitalRead(BOTAO);
  if (botaoAntes == HIGH && botao == LOW) {
    if (estado == DESARMADO) mudarPara(SAINDO);
    else mudarPara(DESARMADO);
  }
  botaoAntes = botao;

  if (estado == SAINDO) {
    // Pisca o verde enquanto da tempo de sair.
    digitalWrite(LED_ARMADO, (millis() / 250) % 2);
    if (millis() - desde > TEMPO_PARA_SAIR) mudarPara(ARMADO);
  }

  if (estado == ARMADO && digitalRead(PIR) == HIGH) {
    mudarPara(DISPARADO);
  }

  if (estado == DISPARADO) {
    // Sirene de duas notas e o LED vermelho piscando junto.
    bool alto = (millis() / 300) % 2;
    tone(SIRENE, alto ? 1200 : 800);
    digitalWrite(LED_ALARME, alto);
  }

  delay(20);
}
''', c, '''
Alarme com sensor de presenca, na ESP32-C6.

O que esta demo mostra:
- uma maquina de estados (enum): desarmado, saindo, armado, disparado;
- o sensor PIR, uma sirene de duas notas com tone() e o tempo medido com
  millis(), sem travar o loop.

Aperte F5, arme no botao, espere 5 s e clique no sensor PIR para simular
alguem passando. O botao desarma.
''')
demo('esp32c6-alarme', 'Alarme com sensor de presença', 'ESP32-C6 DevKit')


# =====================================================================
#  ESP32-H2: sensor de estacionamento
# =====================================================================

c = Circuito('placa', 'esp32-h2-devkit')
c.peca('sonar', 'hc-sr04', 300, 20, Giro=giro_distancia(35))
c.ligar('sonar', 'VCC', 'placa', '3V3', VERMELHO)
c.ligar('sonar', 'TRIG', 'placa', '4', AMARELO)
c.ligar('sonar', 'ECHO', 'placa', '5', VERDE)
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

gravar('esp32h2-estacionamento', r'''
// Sensor de estacionamento, na ESP32-H2.
//
// O ultrassom mede a distancia ate o carro de tras. Longe, o LED fica
// verde; perto, amarelo e com bipes cada vez mais rapidos; encostando,
// vermelho e o bipe vira continuo -- o mesmo de qualquer carro.
//
// Na bancada: o HC-SR04 comum e de 5 V, e o ECHO dele poe 5 V no pino --
// demais para a H2, que e de 3,3 V. Use o HC-SR04P, que funciona em 3,3 V,
// ou um divisor de 1 k e 2 k no ECHO.

const int TRIG = 4;
const int ECHO = 5;
const int VERMELHO = 10;
const int VERDE = 11;
const int AZUL = 12;
const int BUZZER = 22;

unsigned long ultimoBip = 0;

float medirCm() {
  digitalWrite(TRIG, LOW);
  delayMicroseconds(2);
  digitalWrite(TRIG, HIGH);
  delayMicroseconds(10);
  digitalWrite(TRIG, LOW);
  unsigned long idaEVolta = pulseIn(ECHO, HIGH, 30000);
  if (idaEVolta == 0) return 400;   // nada na frente
  return idaEVolta / 58.0;          // 58 us por centimetro, ida e volta
}

void cor(int r, int g, int b) {
  analogWrite(VERMELHO, r);
  analogWrite(VERDE, g);
  analogWrite(AZUL, b);
}

void setup() {
  Serial.begin(115200);
  pinMode(TRIG, OUTPUT);
  pinMode(ECHO, INPUT);
}

void loop() {
  float cm = medirCm();

  // O intervalo entre os bipes encolhe com a distancia.
  unsigned long intervalo;
  if (cm > 100) { cor(0, 255, 0); intervalo = 0; }
  else if (cm > 50) { cor(0, 255, 0); intervalo = 800; }
  else if (cm > 20) { cor(255, 160, 0); intervalo = 300; }
  else { cor(255, 0, 0); intervalo = 1; }

  if (intervalo == 0) {
    noTone(BUZZER);
  } else if (intervalo == 1) {
    tone(BUZZER, 2000);                    // encostando: continuo
  } else if (millis() - ultimoBip > intervalo) {
    ultimoBip = millis();
    tone(BUZZER, 2000, 80);
  }

  Serial.print("distancia: ");
  Serial.print(cm, 0);
  Serial.println(" cm");
  delay(60);
}
''', c, '''
Sensor de estacionamento, na ESP32-H2.

O que esta demo mostra:
- o ultrassom HC-SR04 com pulseIn: o eco vira distancia (58 us por cm);
- um LED RGB com PWM e bipes cada vez mais rapidos com tone().

Aperte F5 e clique no HC-SR04 para aproximar ou afastar o "carro".
''')
demo('esp32h2-estacionamento', 'Sensor de estacionamento (ultrassom)', 'ESP32-H2 DevKit')


# =====================================================================
#  NodeMCU: servidor web com LED e botao
# =====================================================================

c = Circuito('placa', 'esp8266-nodemcu')
c.led('led', 'D5', 'GND.3', 'verde', 320, 60, VERDE)
c.botao('botao', 'D6', 'GND.3', 300, 180)

gravar('nodemcu-servidor', r'''
// Servidor web na NodeMCU (ESP8266).
//
// A pagina mostra se o botao esta apertado e liga e desliga o LED. Ela se
// atualiza sozinha a cada 2 s: aperte o botao e veja a pagina mudar.
//
// Repare nos nomes: D5 e D6 sao os da serigrafia da NodeMCU, e o core do
// ESP8266 os traduz para os GPIO 14 e 12.

#include <ESP8266WiFi.h>
#include <ESP8266WebServer.h>

const int LED = D5;
const int BOTAO = D6;

ESP8266WebServer servidor(80);
bool ledLigado = false;

String pagina() {
  String html = "<!DOCTYPE html><html><head><meta charset='utf-8'>";
  html += "<meta http-equiv='refresh' content='2'>";
  html += "<title>NodeMCU</title></head><body style='font-family:sans-serif'>";
  html += "<h2>NodeMCU na rede</h2>";
  html += "<p>Botao: <b>";
  html += digitalRead(BOTAO) == LOW ? "APERTADO" : "solto";
  html += "</b></p><p>LED: <b>";
  html += ledLigado ? "aceso" : "apagado";
  html += "</b></p>";
  html += "<p><a href='/ligar'>Ligar</a> | <a href='/desligar'>Desligar</a></p>";
  html += "<p>Ligada ha ";
  html += millis() / 1000;
  html += " s</p></body></html>";
  return html;
}

void responder() {
  servidor.send(200, "text/html", pagina());
}

void ligar() {
  ledLigado = true;
  digitalWrite(LED, HIGH);
  responder();
}

void desligar() {
  ledLigado = false;
  digitalWrite(LED, LOW);
  responder();
}

void setup() {
  Serial.begin(115200);
  pinMode(LED, OUTPUT);
  pinMode(BOTAO, INPUT_PULLUP);

  WiFi.mode(WIFI_STA);
  WiFi.begin("Wokwi-GUEST", "");
  Serial.print("conectando");
  while (WiFi.status() != WL_CONNECTED) {
    delay(300);
    Serial.print(".");
  }
  Serial.println();
  Serial.print("IP: ");
  Serial.println(WiFi.localIP());

  servidor.on("/", responder);
  servidor.on("/ligar", ligar);
  servidor.on("/desligar", desligar);
  servidor.begin();
  Serial.println("servidor no ar: abra o endereco do painel REDE");
}

void loop() {
  servidor.handleClient();
  delay(2);
}
''', c, '''
Servidor web na NodeMCU (ESP8266).

O que esta demo mostra:
- uma pagina servida pela propria placa, com links que acendem o LED e o
  estado do botao atualizado a cada 2 s;
- os nomes D5 e D6 da serigrafia, que o core do ESP8266 traduz para os
  GPIO de verdade.

Aperte F5 e abra no navegador o endereco que aparece no painel REDE.
''')
demo('nodemcu-servidor', 'Servidor web: LED e botão na página', 'NodeMCU (ESP8266)')


# =====================================================================
#  ESP-01: tomada inteligente por MQTT
# =====================================================================

c = Circuito('placa', 'esp-01', 80, 100)
c.led_ativo_baixo('tomada', 'GPIO2', 'VCC', 'amarelo', 330, 60, AMARELO)
c.botao('botao', 'GPIO0', 'GND', 300, 190)

gravar('esp01-tomada-mqtt', r'''
// Tomada inteligente por MQTT, num ESP-01.
//
// O "rele" da tomada e o LED (no ESP-01 de verdade seria um modulo de rele
// no mesmo pino). Pelo MQTT:
//   casa/tomada          liga, desliga ou alterna
//   casa/tomada/estado   a placa responde "ligada" ou "desligada"
// O botao faz o mesmo na mao, e tambem avisa pelo MQTT.
//
// Por que o LED acende com LOW: no ESP-01 o GPIO0 e o GPIO2 precisam estar
// em HIGH quando a placa liga, senao ela nao da partida. Com o LED entre o
// 3,3 V e o pino, ele segura o GPIO2 em cima no boot -- e acende quando o
// pino vai para LOW.

#include <ESP8266WiFi.h>
#include <PubSubClient.h>

const int TOMADA = 2;   // GPIO2
const int BOTAO = 0;    // GPIO0: o mesmo botao de gravar

WiFiClient rede;
PubSubClient mqtt(rede);

bool ligada = false;
int botaoAntes = HIGH;

void aplicar(bool nova) {
  ligada = nova;
  digitalWrite(TOMADA, ligada ? LOW : HIGH);   // acende com LOW
  mqtt.publish("casa/tomada/estado", ligada ? "ligada" : "desligada", true);
  Serial.println(ligada ? "tomada LIGADA" : "tomada desligada");
}

void chegou(char* topico, byte* dados, unsigned int tamanho) {
  String comando;
  for (unsigned int i = 0; i < tamanho; i++) comando += (char)dados[i];
  comando.trim();
  comando.toLowerCase();

  if (comando == "liga") aplicar(true);
  else if (comando == "desliga") aplicar(false);
  else if (comando == "alterna") aplicar(!ligada);
  else Serial.println("comando desconhecido: " + comando);
}

void conectar() {
  // O ID do cliente tem que ser unico no broker: com o mesmo ID, cada
  // tomada que conecta derruba a outra. O MAC da placa resolve.
  String id = "tomada-" + WiFi.macAddress();
  id.replace(":", "");

  while (!mqtt.connected()) {
    Serial.print("broker... ");
    if (mqtt.connect(id.c_str())) {
      Serial.println("ok");
      mqtt.subscribe("casa/tomada");
      aplicar(ligada);
    } else {
      Serial.println("falhou, tento de novo em 2 s");
      delay(2000);
    }
  }
}

void setup() {
  Serial.begin(115200);
  pinMode(TOMADA, OUTPUT);
  digitalWrite(TOMADA, HIGH);   // comeca desligada
  pinMode(BOTAO, INPUT_PULLUP);

  WiFi.begin("Wokwi-GUEST", "");
  while (WiFi.status() != WL_CONNECTED) delay(250);
  Serial.print("IP: ");
  Serial.println(WiFi.localIP());

  mqtt.setServer("broker.hivemq.com", 1883);
  mqtt.setCallback(chegou);
}

void loop() {
  if (!mqtt.connected()) conectar();
  mqtt.loop();

  int botao = digitalRead(BOTAO);
  if (botaoAntes == HIGH && botao == LOW) aplicar(!ligada);
  botaoAntes = botao;
  delay(20);
}
''', c, '''
Tomada inteligente por MQTT, num ESP-01.

O que esta demo mostra:
- MQTT com a PubSubClient: assinar um topico, obedecer e responder o
  estado numa mensagem retida;
- o LED que acende com LOW, o jeito certo no ESP-01 (o GPIO2 precisa
  estar em HIGH na partida).

Aperte F5 e, no painel REDE, publique "liga", "desliga" ou "alterna" em
casa/tomada. O botao tambem liga e desliga.
''')
demo('esp01-tomada-mqtt', 'Tomada inteligente por MQTT', 'ESP-01')


# =====================================================================
#  ESP-02: campainha musical
# =====================================================================

c = Circuito('placa', 'esp-02', 80, 100)
c.botao('botao', 'GPIO0', 'GND', 300, 60)
c.buzzer('buzzer', 'GPIO2', 'GND', 320, 170)

gravar('esp02-campainha', r'''
// Campainha musical, num ESP-02.
//
// Aperte o botao e ela toca "ding-dong" -- ou, a cada quinta visita, o
// comeco da Ode a Alegria, de Beethoven. O monitor serial conta as visitas.
//
// O buzzer e passivo: e o tone() que faz cada nota, na frequencia dela.

const int BOTAO = 0;    // GPIO0
const int BUZZER = 2;   // GPIO2

// Ode a Alegria: notas (Hz) e duracoes (ms).
const int NOTAS[] = {330, 330, 349, 392, 392, 349, 330, 294,
                     262, 262, 294, 330, 330, 294, 294};
const int TEMPOS[] = {300, 300, 300, 300, 300, 300, 300, 300,
                      300, 300, 300, 300, 450, 150, 600};
const int QUANTAS = 15;

int visitas = 0;

void dingDong() {
  tone(BUZZER, 659, 300);   // mi
  delay(350);
  tone(BUZZER, 523, 500);   // do
  delay(600);
}

void odeAAlegria() {
  for (int i = 0; i < QUANTAS; i++) {
    tone(BUZZER, NOTAS[i], TEMPOS[i] * 9 / 10);
    delay(TEMPOS[i]);
  }
}

void setup() {
  Serial.begin(115200);
  pinMode(BOTAO, INPUT_PULLUP);
  Serial.println("Campainha pronta: aperte o botao");
}

void loop() {
  if (digitalRead(BOTAO) == LOW) {
    visitas++;
    Serial.print("Visita numero ");
    Serial.println(visitas);

    if (visitas % 5 == 0) odeAAlegria();
    else dingDong();

    // Segurar o botao nao toca de novo: espera soltar.
    while (digitalRead(BOTAO) == LOW) delay(10);
  }
  delay(20);
}
''', c, '''
Campainha musical, num ESP-02.

O que esta demo mostra:
- melodia com tone() e vetores de notas e duracoes;
- o "espera soltar", que impede o botao segurado de tocar sem parar.

Aperte F5 e o botao. A cada quinta visita a musica muda.
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
c.led('quente', 'GPIO5', 'GND', 'vermelho', 510, 150, VERMELHO)
c.led('seco', 'GPIO4', 'GND', 'amarelo', 600, 150, AMARELO)

gravar('esp03-termo-higrometro', r'''
// Termo-higrometro com DHT11, num ESP-03.
//
// Tres LEDs dizem como esta a sala: azul (frio, abaixo de 20 C), verde
// (confortavel) e vermelho (quente, acima de 27 C). O amarelo acende com o
// ar seco, abaixo de 40 % de umidade.

#include <DHT.h>

const int PINO_DHT = 14;
const int FRIO = 12;
const int CONFORTO = 13;
const int QUENTE = 5;
const int SECO = 4;

DHT dht(PINO_DHT, DHT11);

void setup() {
  Serial.begin(115200);
  pinMode(FRIO, OUTPUT);
  pinMode(CONFORTO, OUTPUT);
  pinMode(QUENTE, OUTPUT);
  pinMode(SECO, OUTPUT);
  dht.begin();
}

void loop() {
  delay(1000);   // o DHT11 le no maximo uma vez por segundo

  float t = dht.readTemperature();
  float u = dht.readHumidity();
  if (isnan(t) || isnan(u)) {
    Serial.println("falha ao ler o DHT11");
    return;
  }

  digitalWrite(FRIO, t < 20);
  digitalWrite(CONFORTO, t >= 20 && t <= 27);
  digitalWrite(QUENTE, t > 27);
  digitalWrite(SECO, u < 40);

  Serial.print("temperatura: ");
  Serial.print(t, 0);
  Serial.print(" C   umidade: ");
  Serial.print(u, 0);
  Serial.println(" %");
}
''', c, '''
Termo-higrometro com DHT11, num ESP-03.

O que esta demo mostra:
- temperatura e umidade do DHT11 decidindo quatro LEDs;
- comparacao escrita direto no digitalWrite ("t < 20" ja e HIGH ou LOW).

Aperte F5 e clique no DHT11 para mudar a temperatura e a umidade (o ar
comeca seco, com o LED amarelo aceso).
''')
demo('esp03-termo-higrometro', 'Termo-higrômetro com DHT11', 'ESP-03')


# =====================================================================
#  ESP-04: semaforo com botao de pedestre
# =====================================================================

c = Circuito('placa', 'esp-04', 80, 100)
c.led('carro_vermelho', 'GPIO14', 'GND', 'vermelho', 330, 20, VERMELHO)
c.led('carro_amarelo', 'GPIO12', 'GND', 'amarelo', 330, 110, AMARELO)
c.led('carro_verde', 'GPIO13', 'GND', 'verde', 330, 200, VERDE)
c.led('pedestre_vermelho', 'GPIO5', 'GND', 'vermelho', 470, 60, VERMELHO)
c.led('pedestre_verde', 'GPIO4', 'GND', 'verde', 470, 150, VERDE)
c.botao('botao', 'GPIO0', 'GND', 440, 250)
c.buzzer('buzzer', 'GPIO16', 'GND', 560, 230)

gravar('esp04-semaforo-pedestre', r'''
// Semaforo com botao de pedestre, num ESP-04.
//
// O carro fica no verde ate alguem apertar o botao. Ai o semaforo dos
// carros passa pelo amarelo e fecha, o do pedestre abre com um bipe
// ritmado (o sinal sonoro para quem nao enxerga) e, no fim, o vermelho do
// pedestre pisca avisando que vai fechar.

const int CARRO_VERMELHO = 14;
const int CARRO_AMARELO = 12;
const int CARRO_VERDE = 13;
const int PEDESTRE_VERMELHO = 5;
const int PEDESTRE_VERDE = 4;
const int BOTAO = 0;
const int BUZZER = 16;

void carros(int vermelho, int amarelo, int verde) {
  digitalWrite(CARRO_VERMELHO, vermelho);
  digitalWrite(CARRO_AMARELO, amarelo);
  digitalWrite(CARRO_VERDE, verde);
}

void pedestres(int vermelho, int verde) {
  digitalWrite(PEDESTRE_VERMELHO, vermelho);
  digitalWrite(PEDESTRE_VERDE, verde);
}

void setup() {
  Serial.begin(115200);
  pinMode(CARRO_VERMELHO, OUTPUT);
  pinMode(CARRO_AMARELO, OUTPUT);
  pinMode(CARRO_VERDE, OUTPUT);
  pinMode(PEDESTRE_VERMELHO, OUTPUT);
  pinMode(PEDESTRE_VERDE, OUTPUT);
  pinMode(BOTAO, INPUT_PULLUP);

  carros(LOW, LOW, HIGH);
  pedestres(HIGH, LOW);
  Serial.println("Verde para os carros. Aperte o botao para atravessar.");
}

void loop() {
  if (digitalRead(BOTAO) == LOW) {
    Serial.println("pedido de travessia!");
    delay(1000);                   // o carro nao para na hora
    carros(LOW, HIGH, LOW);
    delay(2000);
    carros(HIGH, LOW, LOW);
    delay(1000);

    pedestres(LOW, HIGH);
    Serial.println("pedestre: pode atravessar");
    for (int i = 0; i < 10; i++) { // 5 s com bipe
      tone(BUZZER, 880, 100);
      delay(500);
    }

    for (int i = 0; i < 6; i++) {  // o vermelho pisca: vai fechar
      pedestres(i % 2 == 0, LOW);
      delay(300);
    }
    pedestres(HIGH, LOW);
    delay(1000);
    carros(LOW, LOW, HIGH);
    Serial.println("verde para os carros");
  }
  delay(20);
}
''', c, '''
Semaforo com botao de pedestre, num ESP-04.

O que esta demo mostra:
- dois semaforos coordenados (carros e pedestres) numa sequencia de tempos;
- o bipe de acessibilidade e o vermelho piscando no fim da travessia.

Aperte F5 e o botao para pedir a travessia.
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
// Calculadora pela serial, num ESP-05.
//
// O ESP-05 so tem os pinos da serial para fora -- RX e TX --, e e
// justamente por eles que a conversa acontece. Digite uma conta no monitor
// serial e aperte Enter:
//
//   12 + 30      7.5 * 4      100 / 8      2 ^ 10
//
// O analisador logico esta no TX: abra-o para ver cada letra da resposta
// saindo a 115200 baud, com o caractere escrito em cima.

#include <math.h>

void calcular(String conta) {
  // O operador e o primeiro + - * / ^ depois do primeiro numero.
  int onde = -1;
  char operador = ' ';
  for (unsigned int i = 1; i < conta.length(); i++) {
    char c = conta.charAt(i);
    if (c == '+' || c == '-' || c == '*' || c == '/' || c == '^') {
      onde = i;
      operador = c;
      break;
    }
  }

  if (onde < 0) {
    Serial.println("nao achei o operador. Use + - * / ou ^");
    return;
  }

  float a = conta.substring(0, onde).toFloat();
  float b = conta.substring(onde + 1).toFloat();
  float r = 0;

  switch (operador) {
    case '+': r = a + b; break;
    case '-': r = a - b; break;
    case '*': r = a * b; break;
    case '/':
      if (b == 0) {
        Serial.println("divisao por zero!");
        return;
      }
      r = a / b;
      break;
    case '^': r = pow(a, b); break;
  }

  Serial.print(conta);
  Serial.print(" = ");
  if (r == (long)r) Serial.println((long)r);   // conta inteira: sem virgula
  else Serial.println(r, 4);
}

void setup() {
  Serial.begin(115200);
  Serial.println("Calculadora: digite uma conta (ex.: 12 + 30)");
}

void loop() {
  if (Serial.available()) {
    String conta = Serial.readStringUntil('\n');
    conta.trim();
    if (conta.length() > 0) calcular(conta);
  }
  delay(10);
}
''', c, '''
Calculadora pela serial, num ESP-05.

O que esta demo mostra:
- ler uma linha do monitor serial e separar os numeros do operador com
  os metodos de String;
- o analisador logico no pino TX: cada byte da resposta aparece como uma
  letra sobre a forma de onda.

Aperte F5, digite "12 + 30" no monitor serial e aperte Enter. Depois
selecione o analisador e clique em "Abrir o analisador".
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
c.botao('b_verde', 'GPIO5', 'GND', 400, 150, VERDE)
c.botao('b_azul', 'GPIO0', 'GND', 500, 150, AZUL)
c.buzzer('buzzer', 'GPIO4', 'GND', 420, 240)

gravar('esp06-genius', r'''
// Genius, num ESP-06.
//
// A placa acende uma sequencia de cores, cada uma com a sua nota. Repita
// apertando os botoes na mesma ordem. Acertou, a sequencia cresce; errou,
// o jogo diz quantas voce acertou e recomeca.
//
// Aperte qualquer botao para comecar.

const int LEDS[] = {16, 14, 12};       // vermelho, verde, azul
const int BOTOES[] = {13, 5, 0};
const int NOTAS[] = {262, 330, 392};   // do, mi, sol
const int BUZZER = 4;
const int MAXIMO = 20;

int sequencia[MAXIMO];
int tamanho = 0;

void mostrar(int cor, int ms) {
  digitalWrite(LEDS[cor], HIGH);
  tone(BUZZER, NOTAS[cor], ms);
  delay(ms);
  digitalWrite(LEDS[cor], LOW);
  delay(150);
}

// Espera um botao e devolve qual foi (0, 1 ou 2).
int esperarBotao() {
  while (true) {
    for (int i = 0; i < 3; i++) {
      if (digitalRead(BOTOES[i]) == LOW) {
        mostrar(i, 250);
        while (digitalRead(BOTOES[i]) == LOW) delay(5);   // espera soltar
        return i;
      }
    }
    delay(5);
  }
}

void perdeu() {
  Serial.print("Errou! Voce acertou ");
  Serial.print(tamanho - 1);
  Serial.println(" rodada(s).");
  tone(BUZZER, 110, 800);
  for (int k = 0; k < 3; k++) {
    for (int i = 0; i < 3; i++) digitalWrite(LEDS[i], HIGH);
    delay(200);
    for (int i = 0; i < 3; i++) digitalWrite(LEDS[i], LOW);
    delay(200);
  }
  tamanho = 0;
}

void setup() {
  Serial.begin(115200);
  for (int i = 0; i < 3; i++) {
    pinMode(LEDS[i], OUTPUT);
    pinMode(BOTOES[i], INPUT_PULLUP);
  }
  Serial.println("Genius: aperte qualquer botao para comecar");
  esperarBotao();
  randomSeed(micros());   // a hora do primeiro aperto vira a semente
}

void loop() {
  if (tamanho == MAXIMO) {
    Serial.println("Voce venceu o Genius!");
    tamanho = 0;
  }

  sequencia[tamanho] = random(3);
  tamanho++;
  Serial.print("Rodada ");
  Serial.println(tamanho);
  delay(600);

  for (int i = 0; i < tamanho; i++) mostrar(sequencia[i], 400);

  for (int i = 0; i < tamanho; i++) {
    if (esperarBotao() != sequencia[i]) {
      perdeu();
      delay(1000);
      return;
    }
  }
  delay(400);
}
''', c, '''
Jogo Genius, num ESP-06.

O que esta demo mostra:
- vetores (a sequencia e as tabelas de pinos e notas) e funcoes que
  devolvem valor;
- random() com semente tirada do primeiro aperto -- cada partida sai
  diferente, como na placa.

Aperte F5 e qualquer botao para comecar. Repita as cores.
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
// Scanner de redes WiFi, num ESP-07 -- o modulo com conector de antena.
//
// A cada 5 s a placa procura as redes em volta e lista no monitor serial:
// o nome, o sinal (RSSI, em dBm) e se e aberta ou com senha. A barra de
// LEDs mostra a forca da MELHOR rede: quanto mais perto de 0 dBm, mais LEDs.
//
// No simulador as redes sao as que o WiFi deste computador enxerga.

#include <ESP8266WiFi.h>

const int BARRA[] = {16, 14, 12, 13, 5};

void mostrarSinal(int rssi) {
  // De -90 dBm (quase nada) a -40 dBm (otimo): de 0 a 5 LEDs.
  int acesos = constrain(map(rssi, -90, -40, 0, 5), 0, 5);
  for (int i = 0; i < 5; i++) digitalWrite(BARRA[i], i < acesos);
}

void setup() {
  Serial.begin(115200);
  for (int i = 0; i < 5; i++) pinMode(BARRA[i], OUTPUT);
  WiFi.mode(WIFI_STA);
  WiFi.disconnect();
  delay(100);
}

void loop() {
  Serial.println("procurando redes...");
  int quantas = WiFi.scanNetworks();

  if (quantas <= 0) {
    Serial.println("nenhuma rede encontrada");
    mostrarSinal(-100);
  } else {
    int melhor = -100;
    for (int i = 0; i < quantas; i++) {
      Serial.print(i + 1);
      Serial.print(". ");
      Serial.print(WiFi.SSID(i));
      Serial.print("  ");
      Serial.print(WiFi.RSSI(i));
      Serial.print(" dBm  ");
      Serial.println(WiFi.encryptionType(i) == ENC_TYPE_NONE ? "aberta" : "com senha");
      if (WiFi.RSSI(i) > melhor) melhor = WiFi.RSSI(i);
    }
    mostrarSinal(melhor);
  }

  WiFi.scanDelete();
  delay(5000);
}
''', c, '''
Scanner de redes WiFi, num ESP-07.

O que esta demo mostra:
- WiFi.scanNetworks() com nome, sinal e tipo de seguranca de cada rede;
- map() e constrain() transformando dBm em LEDs acesos.

Aperte F5. As redes listadas sao as que o WiFi deste computador enxerga.
''')
demo('esp07-scanner-wifi', 'Scanner de redes WiFi com barra de sinal', 'ESP-07')


# =====================================================================
#  ESP-08: jogo de reflexo
# =====================================================================

c = Circuito('placa', 'esp-08', 80, 100)
c.led('prepara', 'GPIO12', 'GND', 'vermelho', 330, 30, VERMELHO)
c.led('vai', 'GPIO14', 'GND', 'verde', 430, 30, VERDE)
c.botao('botao', 'GPIO13', 'GND', 320, 150)
c.buzzer('buzzer', 'GPIO5', 'GND', 460, 150)

gravar('esp08-reflexo', r'''
// Jogo de reflexo, num ESP-08.
//
// O LED vermelho acende: prepare-se. Depois de um tempo sorteado, o verde
// acende -- aperte o botao o mais rapido que conseguir. O monitor serial
// mostra o seu tempo e o recorde. Apertou antes da hora? Queimou a
// largada, e o buzzer reclama.

const int PREPARA = 12;
const int VAI = 14;
const int BOTAO = 13;
const int BUZZER = 5;

unsigned long recorde = 0;

bool apertado() {
  return digitalRead(BOTAO) == LOW;
}

void setup() {
  Serial.begin(115200);
  pinMode(PREPARA, OUTPUT);
  pinMode(VAI, OUTPUT);
  pinMode(BOTAO, INPUT_PULLUP);
  Serial.println("Reflexo: aperte o botao quando o LED verde acender");
}

void loop() {
  digitalWrite(PREPARA, HIGH);
  randomSeed(micros());
  unsigned long espera = random(1500, 4000);
  unsigned long inicio = millis();

  // A espera sorteada -- vigiando quem aperta antes.
  while (millis() - inicio < espera) {
    if (apertado()) {
      digitalWrite(PREPARA, LOW);
      Serial.println("Queimou a largada! Espere o verde.");
      tone(BUZZER, 150, 600);
      while (apertado()) delay(5);
      delay(1500);
      return;
    }
    delay(2);
  }

  digitalWrite(PREPARA, LOW);
  digitalWrite(VAI, HIGH);
  unsigned long acendeu = millis();
  while (!apertado()) delay(1);
  unsigned long tempo = millis() - acendeu;
  digitalWrite(VAI, LOW);

  Serial.print("Seu tempo: ");
  Serial.print(tempo);
  Serial.print(" ms");
  if (recorde == 0 || tempo < recorde) {
    recorde = tempo;
    Serial.print("  <- recorde!");
    tone(BUZZER, 1319, 150);
  }
  Serial.println();

  while (apertado()) delay(5);   // espera soltar
  delay(1500);
}
''', c, '''
Jogo de reflexo, num ESP-08.

O que esta demo mostra:
- tempo medido com millis() com precisao de milissegundo;
- espera sorteada com random() e a vigia de quem aperta antes da hora.

Aperte F5 e o botao assim que o LED verde acender.
''')
demo('esp08-reflexo', 'Jogo de reflexo', 'ESP-08')


# =====================================================================
#  ESP-09: dado eletronico
# =====================================================================

c = Circuito('placa', 'esp-09', 80, 100)
# Os sete pontos na posicao do dado: grupos que acendem juntos dividem o pino.
PONTOS = [
    ('d_sup_esq', 'GPIO14', 330, 20),  ('d_sup_dir', 'GPIO12', 470, 20),
    ('d_meio_esq', 'GPIO13', 330, 110), ('d_centro', 'GPIO16', 400, 110),
    ('d_meio_dir', 'GPIO13', 470, 110),
    ('d_inf_esq', 'GPIO12', 330, 200), ('d_inf_dir', 'GPIO14', 470, 200),
]
for id, pino, x, y in PONTOS:
    c.led(id, pino, 'GND', 'vermelho', x, y, VERMELHO)
c.botao('botao', 'GPIO5', 'GND', 320, 300)
c.buzzer('buzzer', 'GPIO4', 'GND', 470, 290)

gravar('esp09-dado', r'''
// Dado eletronico, num ESP-09.
//
// Aperte o botao: o dado "rola" (os pontos mudam cada vez mais devagar e o
// buzzer faz o tec-tec) e para num numero de 1 a 6, desenhado como no
// dado de verdade.
//
// Sao sete LEDs e so quatro pinos: os pontos que acendem sempre juntos
// dividem o pino. No 6, por exemplo, as duas colunas acendem inteiras.

const int CENTRO = 16;      // o do meio
const int DIAGONAL1 = 14;   // canto de cima a esquerda e de baixo a direita
const int DIAGONAL2 = 12;   // os outros dois cantos
const int MEIO = 13;        // meio da esquerda e meio da direita
const int BOTAO = 5;
const int BUZZER = 4;

void mostrar(int n) {
  digitalWrite(CENTRO, n == 1 || n == 3 || n == 5);
  digitalWrite(DIAGONAL1, n >= 2);
  digitalWrite(DIAGONAL2, n >= 4);
  digitalWrite(MEIO, n == 6);
}

void setup() {
  Serial.begin(115200);
  pinMode(CENTRO, OUTPUT);
  pinMode(DIAGONAL1, OUTPUT);
  pinMode(DIAGONAL2, OUTPUT);
  pinMode(MEIO, OUTPUT);
  pinMode(BOTAO, INPUT_PULLUP);
  mostrar(6);
  Serial.println("Dado eletronico: aperte o botao para rolar");
}

void loop() {
  if (digitalRead(BOTAO) == LOW) {
    randomSeed(micros());
    // Rola desacelerando, como um dado de verdade parando.
    for (int espera = 30; espera < 250; espera += 20) {
      mostrar(random(1, 7));
      tone(BUZZER, 1800, 8);
      delay(espera);
    }
    int resultado = random(1, 7);
    mostrar(resultado);
    tone(BUZZER, 988, 120);
    Serial.print("Saiu ");
    Serial.println(resultado);
    while (digitalRead(BOTAO) == LOW) delay(10);
  }
  delay(20);
}
''', c, '''
Dado eletronico, num ESP-09.

O que esta demo mostra:
- sete LEDs em quatro pinos: os pontos que acendem juntos ficam em
  paralelo, cada um com o seu resistor;
- random(1, 7) com semente nova a cada jogada.

Aperte F5 e o botao para rolar o dado.
''')
demo('esp09-dado', 'Dado eletrônico', 'ESP-09')


# =====================================================================
#  ESP-10: ponte entre a serial e uma pagina web
# =====================================================================

c = Circuito('placa', 'esp-10', 120, 120)

gravar('esp10-ponte-serial-web', r'''
// Ponte entre a serial e a web, num ESP-10.
//
// O ESP-10 e um modulo de cinco pinos: so a serial sai dele. O uso de
// verdade e esse -- um "modem" de WiFi para outra placa. Aqui:
//   - o que chega pela serial (digite no monitor) aparece na pagina;
//   - o que se escreve na pagina sai pela serial.

#include <ESP8266WiFi.h>
#include <ESP8266WebServer.h>

ESP8266WebServer servidor(80);

String recebidas[5];     // as ultimas cinco linhas da serial
int quantas = 0;

void guardar(String linha) {
  if (quantas < 5) {
    recebidas[quantas] = linha;
    quantas++;
  } else {
    for (int i = 0; i < 4; i++) recebidas[i] = recebidas[i + 1];
    recebidas[4] = linha;
  }
}

void pagina() {
  // O formulario manda o texto em /?msg=...
  if (servidor.hasArg("msg") && servidor.arg("msg").length() > 0) {
    Serial.println("[web] " + servidor.arg("msg"));
  }

  String html = "<!DOCTYPE html><html><head><meta charset='utf-8'>";
  html += "<title>Ponte serial</title></head><body style='font-family:sans-serif'>";
  html += "<h2>Ponte serial e web (ESP-10)</h2>";
  html += "<h3>Chegou pela serial:</h3><ul>";
  for (int i = 0; i < quantas; i++) html += "<li>" + recebidas[i] + "</li>";
  if (quantas == 0) html += "<li><i>nada ainda: digite no monitor serial</i></li>";
  html += "</ul><form action='/'><input name='msg' placeholder='mandar pela serial'> ";
  html += "<button>Enviar</button></form><p><a href='/'>atualizar</a></p></body></html>";
  servidor.send(200, "text/html", html);
}

void setup() {
  Serial.begin(115200);
  WiFi.begin("Wokwi-GUEST", "");
  while (WiFi.status() != WL_CONNECTED) delay(250);
  Serial.print("pagina em http://");
  Serial.println(WiFi.localIP());

  servidor.on("/", pagina);
  servidor.begin();
}

void loop() {
  servidor.handleClient();

  if (Serial.available()) {
    String linha = Serial.readStringUntil('\n');
    linha.trim();
    if (linha.length() > 0) {
      guardar(linha);
      Serial.println("ok, esta na pagina: " + linha);
    }
  }
  delay(5);
}
''', c, '''
Ponte entre a serial e uma pagina web, num ESP-10.

O que esta demo mostra:
- um modulo de cinco pinos fazendo o que ele faz de verdade: levar a
  serial de outra placa para a rede;
- formulario HTML com hasArg() e arg(), e um vetor de String como fila.

Aperte F5, digite algo no monitor serial e abra a pagina (endereco no
painel REDE). O que se escreve na pagina volta pela serial.
''')
demo('esp10-ponte-serial-web', 'Ponte entre a serial e uma página web', 'ESP-10')


# =====================================================================
#  ESP-11: minuteria de escada
# =====================================================================

c = Circuito('placa', 'esp-11', 80, 100)
c.led_ativo_baixo('luz', 'GPIO2', 'VCC', 'branco', 330, 50, BRANCO)
c.botao('botao', 'GPIO0', 'GND', 300, 170)

gravar('esp11-minuteria', r'''
// Minuteria de escada, num ESP-11.
//
// Um toque no botao acende a luz por 10 s -- e ela da uma piscada antes de
// apagar, avisando quem ainda esta na escada. Segurar o botao por 2 s
// deixa a luz fixa (modo faxina); outro toque volta ao normal.
//
// Como no ESP-01: o GPIO2 precisa estar em HIGH na partida, entao a
// lampada (o LED) vai entre o 3,3 V e o pino, e acende com LOW.

const int BOTAO = 0;   // GPIO0
const int LUZ = 2;     // GPIO2, acende com LOW

const unsigned long TEMPO_ACESA = 10000;
const unsigned long TOQUE_LONGO = 2000;

bool fixa = false;
bool acesa = false;
unsigned long acendeuEm = 0;

void luz(bool ligar) {
  acesa = ligar;
  digitalWrite(LUZ, ligar ? LOW : HIGH);
}

void setup() {
  Serial.begin(115200);
  pinMode(BOTAO, INPUT_PULLUP);
  pinMode(LUZ, OUTPUT);
  luz(false);
  Serial.println("Minuteria pronta");
}

void loop() {
  if (digitalRead(BOTAO) == LOW) {
    unsigned long apertou = millis();
    while (digitalRead(BOTAO) == LOW && millis() - apertou < TOQUE_LONGO) delay(10);

    if (millis() - apertou >= TOQUE_LONGO) {
      fixa = true;
      luz(true);
      Serial.println("luz fixa (modo faxina)");
      while (digitalRead(BOTAO) == LOW) delay(10);   // espera soltar
    } else if (fixa) {
      fixa = false;
      luz(false);
      Serial.println("de volta ao modo normal");
    } else {
      luz(true);
      acendeuEm = millis();
      Serial.println("acendeu por 10 s");
    }
  }

  if (acesa && !fixa) {
    unsigned long passou = millis() - acendeuEm;
    if (passou > TEMPO_ACESA) {
      luz(false);
      Serial.println("apagou");
    } else if (passou > TEMPO_ACESA - 1000 && passou < TEMPO_ACESA - 700) {
      digitalWrite(LUZ, HIGH);   // a piscada de aviso
    } else {
      digitalWrite(LUZ, LOW);
    }
  }
  delay(20);
}
''', c, '''
Minuteria de escada, num ESP-11.

O que esta demo mostra:
- toque curto e toque longo no mesmo botao, medidos com millis();
- um temporizador que nao trava o loop, com aviso antes de apagar.

Aperte F5 e o botao (clique rapido: 10 s de luz; segure 2 s: luz fixa).
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
// Balanca com HX711, num ESP-12.
//
// A celula de carga e o HX711 medem o peso, e o OLED mostra em gramas. O
// botao e a TARA: ponha o prato vazio (ou um pote), aperte, e o zero passa
// a ser ali.
//
// O HX711 nao devolve gramas: devolve a contagem do conversor. E o
// set_scale() -- o fator de calibracao da sua celula -- que transforma a
// contagem em gramas. Na bancada, o fator se descobre pesando uma coisa de
// peso conhecido.

#include <HX711.h>
#include <Wire.h>
#include <Adafruit_GFX.h>
#include <Adafruit_SSD1306.h>

const int DT = 12;
const int SCK_HX = 14;
const int TARA = 13;

// A celula de 5 kg da peca da ~420 contagens por grama.
const float CALIBRACAO = 420.0;

HX711 balanca;
Adafruit_SSD1306 tela(128, 64, &Wire, -1);

void setup() {
  Serial.begin(115200);
  pinMode(TARA, INPUT_PULLUP);

  balanca.begin(DT, SCK_HX);
  balanca.set_scale(CALIBRACAO);
  balanca.tare();                 // o prato vazio e o zero

  Wire.begin();                   // SDA no GPIO4, SCL no GPIO5
  tela.begin(SSD1306_SWITCHCAPVCC, 0x3C);
  tela.setTextColor(SSD1306_WHITE);
}

void loop() {
  if (digitalRead(TARA) == LOW) {
    balanca.tare();
    Serial.println("tara feita");
    while (digitalRead(TARA) == LOW) delay(10);
  }

  float gramas = balanca.get_units(5);
  if (gramas > -1 && gramas < 1) gramas = 0;   // sem o tremido do zero

  tela.clearDisplay();
  tela.setTextSize(1);
  tela.setCursor(0, 0);
  tela.print("Balanca - ESP-12");
  tela.setTextSize(2);
  tela.setCursor(0, 24);
  tela.print(gramas, 0);
  tela.print(" g");
  tela.setTextSize(1);
  tela.setCursor(0, 56);
  tela.print("botao: tara");
  tela.display();

  Serial.print(gramas, 0);
  Serial.println(" g");
  delay(300);
}
''', c, '''
Balanca com HX711 e OLED, num ESP-12.

O que esta demo mostra:
- a celula de carga do jeito da biblioteca: contagem crua, tara (o zero)
  e calibracao (set_scale) para virar gramas;
- o OLED no I2C de fabrica do ESP8266 (GPIO4 e GPIO5).

Aperte F5 e clique no HX711 para por peso. O botao faz a tara.
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
// Cor do LED RGB pelo navegador, num ESP-12E.
//
// A pagina tem um seletor de cor: escolha e clique em Aplicar. A placa
// recebe "#rrggbb", separa o vermelho, o verde e o azul e manda cada um
// para o seu pino com PWM.

#include <ESP8266WiFi.h>
#include <ESP8266WebServer.h>

const int PINO_R = 14;
const int PINO_G = 12;
const int PINO_B = 13;

ESP8266WebServer servidor(80);
String corAtual = "#000000";

void aplicarCor(String hex) {
  long valor = strtol(hex.substring(1).c_str(), NULL, 16);
  int r = (valor >> 16) & 0xFF;
  int g = (valor >> 8) & 0xFF;
  int b = valor & 0xFF;
  analogWrite(PINO_R, r);
  analogWrite(PINO_G, g);
  analogWrite(PINO_B, b);
  corAtual = hex;
  Serial.printf("cor %s -> R=%d G=%d B=%d\n", hex.c_str(), r, g, b);
}

void pagina() {
  if (servidor.hasArg("cor")) aplicarCor(servidor.arg("cor"));

  String html = "<!DOCTYPE html><html><head><meta charset='utf-8'>";
  html += "<title>LED RGB</title></head><body style='font-family:sans-serif'>";
  html += "<h2>LED RGB pelo navegador</h2>";
  html += "<form action='/'><input type='color' name='cor' value='" + corAtual + "'> ";
  html += "<button>Aplicar</button></form>";
  html += "<p>Cor atual: " + corAtual + "</p></body></html>";
  servidor.send(200, "text/html", html);
}

void setup() {
  Serial.begin(115200);
  analogWriteRange(255);    // o PWM de 0 a 255, como no Arduino
  WiFi.begin("Wokwi-GUEST", "");
  while (WiFi.status() != WL_CONNECTED) delay(250);
  Serial.print("abra http://");
  Serial.println(WiFi.localIP());

  servidor.on("/", pagina);
  servidor.begin();
  aplicarCor("#ff6a00");    // comeca laranja
}

void loop() {
  servidor.handleClient();
  delay(2);
}
''', c, '''
Cor do LED RGB pelo navegador, num ESP-12E.

O que esta demo mostra:
- um seletor de cor HTML mandando "#rrggbb" para a placa;
- strtol() e deslocamento de bits separando o vermelho, o verde e o azul.

Aperte F5, abra o endereco do painel REDE e escolha uma cor.
''')
demo('esp12e-rgb-navegador', 'Cor do LED RGB pelo navegador', 'ESP-12E')


# =====================================================================
#  ESP-12F: luminaria NeoPixel por MQTT
# =====================================================================

c = Circuito('placa', 'esp-12f', 80, 100)
c.peca('matriz', 'neopixel-matrix', 320, 20)
c.ligar('matriz', 'GND', 'placa', 'GND', PRETO)
c.ligar('matriz', 'VCC', 'placa', 'VCC', VERMELHO)
c.ligar('matriz', 'DIN', 'placa', 'GPIO4', VERDE)

gravar('esp12f-luminaria-mqtt', r'''
// Luminaria NeoPixel pelo MQTT, num ESP-12F.
//
// Tres topicos mandam na luminaria:
//   casa/luminaria/cor      "#ff8800", ou vermelho, verde, azul, branco
//   casa/luminaria/brilho   0 a 255
//   casa/luminaria/efeito   fixo, arco-iris ou pulsar
// e ela responde o estado em casa/luminaria/estado.
//
// Na bancada: 64 LEDs puxam ate 3,8 A no branco. Alimente a matriz com uma
// fonte de 5 V propria, com o GND junto ao do modulo.

#include <ESP8266WiFi.h>
#include <PubSubClient.h>
#include <Adafruit_NeoPixel.h>

const int PINO_MATRIZ = 4;
const int QUANTOS = 64;

Adafruit_NeoPixel matriz(QUANTOS, PINO_MATRIZ, NEO_GRB + NEO_KHZ800);
WiFiClient rede;
PubSubClient mqtt(rede);

uint32_t cor = 0xFF8800;
String efeito = "fixo";
long matiz = 0;
int passo = 0;

String hex6(uint32_t valor) {
  String texto = String(valor, HEX);
  while (texto.length() < 6) texto = "0" + texto;
  texto.toUpperCase();
  return texto;
}

uint32_t corPeloNome(String nome) {
  if (nome == "vermelho") return 0xFF0000;
  if (nome == "verde") return 0x00FF00;
  if (nome == "azul") return 0x0000FF;
  if (nome == "branco") return 0xFFFFFF;
  if (nome.startsWith("#")) return strtol(nome.substring(1).c_str(), NULL, 16);
  return cor;
}

void publicarEstado() {
  String estado = "cor=#" + hex6(cor) + " brilho=" + String(matriz.getBrightness())
                + " efeito=" + efeito;
  mqtt.publish("casa/luminaria/estado", estado.c_str(), true);
  Serial.println(estado);
}

void chegou(char* topico, byte* dados, unsigned int tamanho) {
  String valor;
  for (unsigned int i = 0; i < tamanho; i++) valor += (char)dados[i];
  valor.trim();
  String qual = String(topico);

  if (qual == "casa/luminaria/cor") cor = corPeloNome(valor);
  else if (qual == "casa/luminaria/brilho") matriz.setBrightness(constrain(valor.toInt(), 0, 255));
  else if (qual == "casa/luminaria/efeito") efeito = valor;
  publicarEstado();
}

void conectar() {
  // ID unico no broker: o MAC da placa.
  String id = "luminaria-" + WiFi.macAddress();
  id.replace(":", "");

  while (!mqtt.connected()) {
    Serial.print("broker... ");
    if (mqtt.connect(id.c_str())) {
      Serial.println("ok");
      mqtt.subscribe("casa/luminaria/cor");
      mqtt.subscribe("casa/luminaria/brilho");
      mqtt.subscribe("casa/luminaria/efeito");
      publicarEstado();
    } else {
      Serial.println("falhou, tento de novo em 2 s");
      delay(2000);
    }
  }
}

void setup() {
  Serial.begin(115200);
  matriz.begin();
  matriz.setBrightness(60);

  WiFi.begin("Wokwi-GUEST", "");
  while (WiFi.status() != WL_CONNECTED) delay(250);
  mqtt.setServer("broker.hivemq.com", 1883);
  mqtt.setCallback(chegou);
}

void loop() {
  if (!mqtt.connected()) conectar();
  mqtt.loop();

  if (efeito == "arco-iris") {
    for (int i = 0; i < QUANTOS; i++)
      matriz.setPixelColor(i, matriz.gamma32(matriz.ColorHSV(matiz + i * 1024)));
    matiz += 256;
  } else if (efeito == "pulsar") {
    int nivel = matriz.sine8(passo);
    int r = ((cor >> 16) & 0xFF) * nivel / 255;
    int g = ((cor >> 8) & 0xFF) * nivel / 255;
    int b = (cor & 0xFF) * nivel / 255;
    matriz.fill(matriz.Color(r, g, b));
    passo += 4;
  } else {
    matriz.fill(cor);
  }
  matriz.show();
  delay(30);
}
''', c, '''
Luminaria NeoPixel por MQTT, num ESP-12F.

O que esta demo mostra:
- tres topicos MQTT (cor, brilho e efeito) comandando uma matriz 8x8;
- o estado publicado como mensagem retida, para quem chegar depois.

Aperte F5 e, no painel REDE, publique "arco-iris" em casa/luminaria/efeito
ou "azul" em casa/luminaria/cor.
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
// Fechadura com senha, num ESP-13.
//
// Digite a senha no teclado e aperte # para confirmar (* apaga). Certa, a
// fechadura abre por 5 s (LED verde) com um bipe duplo. Errada, bipe
// grave -- e tres erros seguidos travam o teclado por 30 s.
//
// A senha e 1234. So as tres primeiras colunas do teclado sao usadas.

#include <Keypad.h>

const byte LINHAS = 4;
const byte COLUNAS = 3;
char teclas[LINHAS][COLUNAS] = {
  {'1', '2', '3'},
  {'4', '5', '6'},
  {'7', '8', '9'},
  {'*', '0', '#'},
};
byte pinosLinhas[LINHAS] = {14, 12, 13, 5};
byte pinosColunas[COLUNAS] = {4, 0, 2};

Keypad teclado = Keypad(makeKeymap(teclas), pinosLinhas, pinosColunas, LINHAS, COLUNAS);

const int ABERTA = 16;   // LED verde: fechadura aberta
const int BUZZER = 15;
const String SENHA = "1234";

String digitado = "";
int erros = 0;

void bipe(int nota, int ms) {
  tone(BUZZER, nota, ms);
  delay(ms + 40);
}

void conferir() {
  if (digitado == SENHA) {
    Serial.println("SENHA CERTA: aberta por 5 s");
    erros = 0;
    digitalWrite(ABERTA, HIGH);
    bipe(1319, 100);
    bipe(1760, 150);
    delay(5000);
    digitalWrite(ABERTA, LOW);
    Serial.println("trancada de novo");
    return;
  }

  erros++;
  Serial.print("senha errada (");
  Serial.print(erros);
  Serial.println(" de 3)");
  bipe(150, 500);
  if (erros >= 3) {
    Serial.println("teclado travado por 30 s");
    delay(30000);
    erros = 0;
  }
}

void setup() {
  Serial.begin(115200);
  pinMode(ABERTA, OUTPUT);
  Serial.println("Fechadura: digite a senha e aperte #");
}

void loop() {
  char tecla = teclado.getKey();
  if (!tecla) {
    delay(10);
    return;
  }

  bipe(2000, 30);   // o clique de cada tecla

  if (tecla == '*') {
    digitado = "";
    Serial.println("apagado");
  } else if (tecla == '#') {
    conferir();
    digitado = "";
  } else {
    digitado += tecla;
    Serial.print("digitado: ");
    for (unsigned int i = 0; i < digitado.length(); i++) Serial.print('*');
    Serial.println();
  }
}
''', c, '''
Fechadura com teclado e senha, num ESP-13.

O que esta demo mostra:
- o teclado matricial com a biblioteca Keypad (4 linhas e 3 colunas);
- senha montada letra a letra numa String, e o bloqueio depois de tres
  erros.

Aperte F5 e clique nas teclas: 1, 2, 3, 4 e #.
''')
demo('esp13-fechadura', 'Fechadura com teclado e senha', 'ESP-13')


# =====================================================================
#  ESP-14: placar de jogo no OLED
# =====================================================================

c = Circuito('placa', 'esp-14', 80, 100)
c.peca('oled', 'ssd1306-i2c', 330, 10)
c.ligar('oled', 'GND', 'placa', 'GND', PRETO)
c.ligar('oled', 'VCC', 'placa', 'VCC', VERMELHO)
c.ligar('oled', 'SDA', 'placa', 'GPIO4', AZUL)
c.ligar('oled', 'SCL', 'placa', 'GPIO5', ROXO)
c.botao('time_a', 'GPIO14', 'GND', 300, 160, VERMELHO)
c.botao('time_b', 'GPIO12', 'GND', 400, 160, AZUL)
c.botao('zerar', 'GPIO13', 'GND', 500, 160, CINZA)
c.buzzer('buzzer', 'GPIO16', 'GND', 420, 240)

gravar('esp14-placar', r'''
// Placar de jogo, num ESP-14.
//
// Um botao para cada time soma um ponto; o terceiro, segurado por 1 s,
// zera o jogo. O placar aparece no OLED, o buzzer apita a cada ponto -- e
// toca uma fanfarra quando alguem chega a 10.
//
// (O ESP-14 tem um microcontrolador STM8 junto do ESP8266. Aqui so o
// ESP8266 e usado: e ele que roda o sketch do Arduino.)

#include <Wire.h>
#include <Adafruit_GFX.h>
#include <Adafruit_SSD1306.h>

const int BOTAO_A = 14;
const int BOTAO_B = 12;
const int ZERAR = 13;
const int BUZZER = 16;
const int VENCE = 10;

Adafruit_SSD1306 tela(128, 64, &Wire, -1);

int pontosA = 0;
int pontosB = 0;
int antesA = HIGH;
int antesB = HIGH;

void desenhar() {
  tela.clearDisplay();
  tela.setTextSize(1);
  tela.setCursor(0, 0);
  tela.print(" TIME A    TIME B");
  tela.setTextSize(3);
  tela.setCursor(12, 24);
  if (pontosA < 10) tela.print(" ");
  tela.print(pontosA);
  tela.print(" x ");
  tela.print(pontosB);
  tela.display();
}

void fanfarra() {
  int notas[] = {523, 659, 784, 1047};
  for (int i = 0; i < 4; i++) {
    tone(BUZZER, notas[i], 150);
    delay(180);
  }
}

void marcou(bool timeA) {
  if (timeA) pontosA++;
  else pontosB++;
  tone(BUZZER, 1500, 60);
  Serial.print(timeA ? "ponto do time A: " : "ponto do time B: ");
  Serial.print(pontosA);
  Serial.print(" x ");
  Serial.println(pontosB);
  desenhar();

  if (pontosA >= VENCE || pontosB >= VENCE) {
    Serial.println(timeA ? "o time A venceu!" : "o time B venceu!");
    fanfarra();
  }
}

void setup() {
  Serial.begin(115200);
  pinMode(BOTAO_A, INPUT_PULLUP);
  pinMode(BOTAO_B, INPUT_PULLUP);
  pinMode(ZERAR, INPUT_PULLUP);
  Wire.begin();   // SDA no GPIO4, SCL no GPIO5
  tela.begin(SSD1306_SWITCHCAPVCC, 0x3C);
  tela.setTextColor(SSD1306_WHITE);
  desenhar();
}

void loop() {
  int a = digitalRead(BOTAO_A);
  int b = digitalRead(BOTAO_B);
  if (antesA == HIGH && a == LOW) marcou(true);
  if (antesB == HIGH && b == LOW) marcou(false);
  antesA = a;
  antesB = b;

  if (digitalRead(ZERAR) == LOW) {
    unsigned long desde = millis();
    while (digitalRead(ZERAR) == LOW) {
      if (millis() - desde > 1000) {
        pontosA = 0;
        pontosB = 0;
        tone(BUZZER, 400, 300);
        Serial.println("placar zerado");
        desenhar();
        while (digitalRead(ZERAR) == LOW) delay(10);
      }
      delay(10);
    }
  }
  delay(15);
}
''', c, '''
Placar de jogo no OLED, num ESP-14.

O que esta demo mostra:
- deteccao de borda (o ponto conta uma vez por aperto, e nao enquanto o
  botao esta segurado) e toque longo para zerar;
- o OLED I2C atualizado so quando o placar muda.

Aperte F5 e os botoes dos times. Segure o terceiro por 1 s para zerar.
''')
demo('esp14-placar', 'Placar de jogo no OLED', 'ESP-14')


# =====================================================================
#  Franzininho: vela eletronica
# =====================================================================

c = Circuito('placa', 'franzininho', 40, 60)
c.led('chama1', 'PB0', 'GND.1', 'amarelo', 400, 20, AMARELO)
c.led('chama2', 'PB1', 'GND.1', 'laranja', 480, 20, LARANJA)
c.botao('sopro', 'PB2', 'GND.1', 400, 160)

gravar('franzininho-vela', r'''
// Vela eletronica, na Franzininho.
//
// Dois LEDs (amarelo e laranja) tremulam como chama de vela, com brilho
// sorteado a cada instante. O botao "sopra" a vela -- ela se apaga
// devagar -- e acende de novo no proximo aperto.
//
// PB0 e PB1 tem PWM: e o analogWrite que faz a chama variar.

const int CHAMA_AMARELA = 0;   // PB0
const int CHAMA_LARANJA = 1;   // PB1
const int BOTAO = 2;           // PB2

bool acesa = true;
int botaoAntes = HIGH;

void apagarDevagar() {
  for (int brilho = 200; brilho >= 0; brilho -= 5) {
    analogWrite(CHAMA_AMARELA, brilho);
    analogWrite(CHAMA_LARANJA, brilho / 2);
    delay(15);
  }
}

void setup() {
  pinMode(CHAMA_AMARELA, OUTPUT);
  pinMode(CHAMA_LARANJA, OUTPUT);
  pinMode(BOTAO, INPUT_PULLUP);
}

void loop() {
  int botao = digitalRead(BOTAO);
  if (botaoAntes == HIGH && botao == LOW) {
    acesa = !acesa;
    if (!acesa) apagarDevagar();
  }
  botaoAntes = botao;

  if (acesa) {
    // A chama: um brilho de base com um tremido sorteado por cima.
    analogWrite(CHAMA_AMARELA, random(120, 256));
    analogWrite(CHAMA_LARANJA, random(40, 140));
    delay(random(30, 110));
  } else {
    delay(20);
  }
}
''', c, '''
Vela eletronica, na Franzininho.

O que esta demo mostra:
- PWM com analogWrite nos pinos do ATtiny85 que tem PWM (PB0 e PB1);
- random() dando vida a chama, num chip de meio KB de RAM.

Aperte F5 e o botao para soprar e reacender a vela.
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
// Luz noturna automatica, no ATtiny85.
//
// O modulo de LDR mede a luz do ambiente. De dia o LED fica apagado; ao
// escurecer ele acende aos poucos, e no escuro fica no maximo.
//
// Como o modulo de verdade: MAIS luz, MENOR a leitura no AO.

const int SENSOR = A3;   // o PB3
const int LED = 0;       // o PB0, que tem PWM

void setup() {
  pinMode(LED, OUTPUT);
}

void loop() {
  int leitura = analogRead(SENSOR);   // perto de 0 com sol, perto de 1023 no escuro

  // Abaixo de 600 e dia: apagado. De 600 a 1000, acende aos poucos.
  int brilho = 0;
  if (leitura > 600) brilho = map(constrain(leitura, 600, 1000), 600, 1000, 10, 255);

  analogWrite(LED, brilho);
  delay(100);
}
''', c, '''
Luz noturna automatica, no ATtiny85.

O que esta demo mostra:
- o chip ATtiny85 sozinho, com os nomes do encapsulamento (A3 e o PB3);
- o modulo de LDR com a fisica de verdade: mais luz, leitura menor.

A demo comeca a noite (3 lux) e o LED acende. Clique no modulo de LDR e
aumente a luz para ver o LED apagar.
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
// Piano de oito teclas, na Raspberry Pi Pico (Arduino).
//
// Cada botao e uma nota da escala de do maior. Segure para a nota durar;
// solte e ela para. O monitor serial mostra o nome da nota.

const int TECLAS[] = {2, 3, 4, 5, 6, 7, 8, 9};
const int NOTAS[] = {262, 294, 330, 349, 392, 440, 494, 523};
const char* NOMES[] = {"do", "re", "mi", "fa", "sol", "la", "si", "do agudo"};
const int BUZZER = 15;

int tocando = -1;

void setup() {
  Serial.begin(115200);
  for (int i = 0; i < 8; i++) pinMode(TECLAS[i], INPUT_PULLUP);
}

void loop() {
  int apertada = -1;
  for (int i = 0; i < 8; i++) {
    if (digitalRead(TECLAS[i]) == LOW) {
      apertada = i;
      break;
    }
  }

  if (apertada != tocando) {
    if (apertada < 0) {
      noTone(BUZZER);
    } else {
      tone(BUZZER, NOTAS[apertada]);
      Serial.println(NOMES[apertada]);
    }
    tocando = apertada;
  }
  delay(10);
}
''', c, '''
Piano de oito teclas, na Raspberry Pi Pico (Arduino).

O que esta demo mostra:
- a Pico programada com o core do Arduino (o numero do pino e o do GP);
- tone() e noTone() seguindo a tecla segurada.

Aperte F5 e clique nas teclas (segure para a nota durar).
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
# Joystick e OLED na Raspberry Pi Pico, em MicroPython.
#
# O joystick move um simbolo pela tela do OLED, e o botao dele troca o
# simbolo. As leituras cruas aparecem na linha de cima.
#
# Na Pico, ADC(0) e o canal 0 -- o GP26 --, e o read_u16 vai de 0 a 65535.

from machine import Pin, ADC, I2C
import ssd1306
import time

eixo_x = ADC(0)          # GP26
eixo_y = ADC(1)          # GP27
botao = Pin(22, Pin.IN, Pin.PULL_UP)

i2c = I2C(0, sda=Pin(4), scl=Pin(5))
oled = ssd1306.SSD1306_I2C(128, 64, i2c)

simbolos = ["o", "*", "+", "@"]
qual = 0
antes = 1

while True:
    x = eixo_x.read_u16()
    y = eixo_y.read_u16()

    agora = botao.value()
    if antes == 1 and agora == 0:
        qual = (qual + 1) % len(simbolos)
    antes = agora

    # 0..65535 vira coluna 0..20 e linha 2..7 (as de cima sao do texto).
    coluna = x * 20 // 65535
    linha = 2 + y * 5 // 65535

    oled.fill(0)
    oled.text("X " + str(x) + " Y " + str(y), 0, 0)
    oled.text(simbolos[qual], coluna * 6, linha * 8)
    oled.show()
    time.sleep_ms(50)
''', c, '''
Joystick e OLED na Raspberry Pi Pico, em MicroPython.

O que esta demo mostra:
- MicroPython na Pico: ADC por canal (ADC(0) e o GP26), read_u16 de 0 a
  65535 e o driver ssd1306 no I2C 0;
- entrada analogica virando posicao na tela.

Aperte F5 e clique no joystick para mexer nos eixos X e Y.
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
// LED respirando na Raspberry Pi Pico, com o SDK em C.
//
// O PWM de hardware do RP2040 faz o brilho: o pino passa para o PWM com
// gpio_set_function, o contador da fatia conta ate 65535 e o nivel sobe e
// desce. O nivel vai ao quadrado -- a correcao de gama --, porque o olho
// percebe o brilho assim, e a respiracao fica suave.
//
// O analisador logico esta no GP15: abra-o para ver a largura do pulso
// mudar.

#include "pico/stdlib.h"
#include "hardware/pwm.h"

#define LED 15

int main() {
    stdio_init_all();

    gpio_set_function(LED, GPIO_FUNC_PWM);
    uint fatia = pwm_gpio_to_slice_num(LED);

    pwm_config config = pwm_get_default_config();
    pwm_config_set_clkdiv(&config, 16.f);   // 125 MHz / 16
    pwm_config_set_wrap(&config, 65535);     // ~119 Hz
    pwm_init(fatia, &config, true);

    int nivel = 0;
    int passo = 1;
    while (true) {
        pwm_set_gpio_level(LED, nivel * nivel);   // 0 a 65025
        nivel += passo;
        if (nivel == 255 || nivel == 0) passo = -passo;
        sleep_ms(8);
    }
}
''', c, '''
LED respirando com o PWM de hardware, na Raspberry Pi Pico (SDK em C).

O que esta demo mostra:
- o SDK da Raspberry: main() em vez de setup/loop, gpio_set_function, a
  fatia de PWM configurada por struct e o nivel com correcao de gama;
- o analisador logico mostrando o ciclo do PWM subir e descer.

Aperte F5 e abra o analisador (selecione-o e clique em "Abrir o
analisador").
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
// Estacao do tempo na web, na Raspberry Pi Pico W (Arduino).
//
// O DHT22 mede a temperatura e a umidade, e a Pico W serve duas coisas:
//   /        uma pagina que se atualiza sozinha a cada 5 s
//   /dados   os mesmos numeros em JSON, para outro programa ler

#include <WiFi.h>
#include <WebServer.h>
#include <DHT.h>

DHT dht(16, DHT22);
WebServer servidor(80);

float temperatura = 0;
float umidade = 0;
unsigned long ultimaLeitura = 0;

void ler() {
  float t = dht.readTemperature();
  float u = dht.readHumidity();
  if (!isnan(t)) temperatura = t;
  if (!isnan(u)) umidade = u;
}

void pagina() {
  String html = "<!DOCTYPE html><html><head><meta charset='utf-8'>";
  html += "<meta http-equiv='refresh' content='5'><title>Estacao</title></head>";
  html += "<body style='font-family:sans-serif;text-align:center'>";
  html += "<h2>Estacao do tempo (Pico W)</h2>";
  html += "<p style='font-size:3em'>" + String(temperatura, 1) + " &deg;C</p>";
  html += "<p style='font-size:2em'>" + String(umidade, 0) + " % de umidade</p>";
  html += "<p><a href='/dados'>ver em JSON</a></p></body></html>";
  servidor.send(200, "text/html", html);
}

void dados() {
  String json = "{\"temperatura\":" + String(temperatura, 1)
              + ",\"umidade\":" + String(umidade, 0)
              + ",\"segundos_ligada\":" + String(millis() / 1000) + "}";
  servidor.send(200, "application/json", json);
}

void setup() {
  Serial.begin(115200);
  dht.begin();
  ler();

  WiFi.begin("Wokwi-GUEST", "");
  while (WiFi.status() != WL_CONNECTED) delay(250);
  Serial.print("abra http://");
  Serial.println(WiFi.localIP());

  servidor.on("/", pagina);
  servidor.on("/dados", dados);
  servidor.begin();
}

void loop() {
  servidor.handleClient();
  if (millis() - ultimaLeitura > 2000) {
    ultimaLeitura = millis();
    ler();
    Serial.print(temperatura, 1);
    Serial.print(" C  ");
    Serial.print(umidade, 0);
    Serial.println(" %");
  }
  delay(2);
}
''', c, '''
Estacao do tempo na web, na Raspberry Pi Pico W (Arduino).

O que esta demo mostra:
- a Pico W no WiFi com o core do Arduino, servindo uma pagina e um
  endereco em JSON (/dados) para outro programa ler;
- o DHT22 lido de 2 em 2 s, sem travar o servidor.

Aperte F5 e abra o endereco do painel REDE no navegador.
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
# MQTT em MicroPython, na Raspberry Pi Pico W.
#
# A cada 2 s a placa publica a posicao do potenciometro (0 a 100 %) em
# "pico/pot", e obedece ao topico "pico/led": "on" acende, "off" apaga.

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
print("conectado:", wlan.ifconfig()[0])

def chegou(topico, mensagem):
    texto = mensagem.decode()
    print("recebi", topico.decode(), texto)
    if texto == "on":
        led.value(1)
    elif texto == "off":
        led.value(0)

# Um id por placa: no broker publico, duas com o mesmo id se derrubam uma
# a outra -- e a turma inteira roda este mesmo codigo.
cliente = MQTTClient(b"pico-w-" + ubinascii.hexlify(machine.unique_id()), "broker.hivemq.com")
cliente.set_callback(chegou)
cliente.connect()
cliente.subscribe(b"pico/led")
print("pronto: publique on ou off em pico/led")

ultimo = 0
while True:
    cliente.check_msg()
    if time.ticks_diff(time.ticks_ms(), ultimo) > 2000:
        ultimo = time.ticks_ms()
        porcento = pot.read_u16() * 100 // 65535
        cliente.publish(b"pico/pot", str(porcento))
        print("pot:", porcento, "%")
    time.sleep_ms(20)
''', c, '''
MQTT em MicroPython, na Raspberry Pi Pico W.

O que esta demo mostra:
- WiFi com network.WLAN e MQTT com umqtt.simple, o jeito do MicroPython;
- publicar uma leitura e obedecer a um topico no mesmo laco, com
  check_msg() e ticks_diff().

Aperte F5 e, no painel REDE, publique "on" ou "off" em pico/led. Gire o
potenciometro e veja pico/pot mudar.
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
// Voltimetro na Raspberry Pi Pico W, com o SDK em C.
//
// O ADC le a tensao do potenciometro no GP26 (canal 0), em 12 bits, e o
// printf mostra em volts. Acima de 2,5 V o LED de alerta acende. A cada
// 2 s, a temperatura de dentro do chip (canal 4) aparece tambem.

#include <stdio.h>
#include "pico/stdlib.h"
#include "hardware/adc.h"

#define ALERTA 15
#define LIMITE 2.5f

int main() {
    stdio_init_all();

    gpio_init(ALERTA);
    gpio_set_dir(ALERTA, GPIO_OUT);

    adc_init();
    adc_gpio_init(26);
    adc_set_temp_sensor_enabled(true);

    int voltas = 0;
    while (true) {
        adc_select_input(0);
        uint16_t bruto = adc_read();
        float volts = bruto * 3.3f / 4096;
        gpio_put(ALERTA, volts > LIMITE);
        printf("%.2f V (leitura %d)%s\n", volts, bruto, volts > LIMITE ? "  ALERTA" : "");

        voltas++;
        if (voltas % 8 == 0) {
            adc_select_input(4);
            float graus = 27 - (adc_read() * 3.3f / 4096 - 0.706f) / 0.001721f;
            printf("temperatura do chip: %.1f C\n", graus);
        }
        sleep_ms(250);
    }
}
''', c, '''
Voltimetro com o ADC, na Raspberry Pi Pico W (SDK em C).

O que esta demo mostra:
- o conversor de 12 bits pelo SDK: adc_select_input e adc_read, com a
  conta que transforma a leitura em volts;
- o sensor de temperatura de dentro do RP2040, no canal 4.

Aperte F5 e gire o potenciometro. Acima de 2,5 V o LED acende.
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
// Termometro com NTC e alarme, na ST Nucleo-L031K6.
//
// O NTC do modulo fica num divisor de tensao; a formula abaixo (a do
// exemplo do modulo, com beta 3950) transforma a leitura em graus. Acima
// de 30 C o LED vermelho acende e o buzzer apita; abaixo, fica o verde.
//
// O ADC da STM32 tem 12 bits, mas o core do Arduino entrega 10 de fabrica.
// O analogReadResolution(12) pede os 12 -- e a formula usa 4095.

const float BETA = 3950;
const float LIMITE = 30.0;

const int SENSOR = A0;
const int VERDE = 4;           // D4
const int VERMELHO = 5;        // D5
const int BUZZER = 3;          // D3

void setup() {
  Serial.begin(115200);
  pinMode(VERDE, OUTPUT);
  pinMode(VERMELHO, OUTPUT);
  analogReadResolution(12);
}

void loop() {
  int leitura = analogRead(SENSOR);
  float celsius = 1 / (log(1 / (4095.0 / leitura - 1)) / BETA + 1.0 / 298.15) - 273.15;

  bool quente = celsius > LIMITE;
  digitalWrite(VERDE, !quente);
  digitalWrite(VERMELHO, quente);
  if (quente) tone(BUZZER, 2200, 150);

  Serial.print("temperatura: ");
  Serial.print(celsius, 1);
  Serial.println(quente ? " C  <- ALARME" : " C");
  delay(1000);
}
''', c, '''
Termometro com NTC e alarme, na ST Nucleo-L031K6.

O que esta demo mostra:
- o termistor NTC e a formula beta que todo tutorial usa -- a leitura
  daqui segue a curva de verdade do sensor;
- analogReadResolution(12) na STM32, cujo core entrega 10 bits de fabrica.

Aperte F5 e clique no sensor NTC para mudar a temperatura (acima de 30 C
dispara o alarme).
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
// Relogio com RTC e LCD, na ST Nucleo-C031C6.
//
// O DS1307 guarda a hora mesmo com a placa desligada (a bateria dele
// segura). O LCD mostra a hora e a data, e o botao troca entre 24 h e
// 12 h. O RTC e o LCD dividem o mesmo I2C -- D14 e D15, como no Uno.
//
// Na primeira vez, o RTC e acertado com a hora em que o sketch foi
// compilado (__DATE__ e __TIME__).

#include <Wire.h>
#include <RTClib.h>
#include <LiquidCrystal_I2C.h>

RTC_DS1307 rtc;
LiquidCrystal_I2C lcd(0x27, 16, 2);

const int BOTAO = 7;   // D7
const char* DIAS[] = {"Dom", "Seg", "Ter", "Qua", "Qui", "Sex", "Sab"};

bool dozeHoras = false;
int botaoAntes = HIGH;

void doisDigitos(int n) {
  if (n < 10) lcd.print('0');
  lcd.print(n);
}

void setup() {
  Serial.begin(115200);
  pinMode(BOTAO, INPUT_PULLUP);
  lcd.init();
  lcd.backlight();

  if (!rtc.begin()) {
    lcd.print("RTC nao achado");
    while (true) delay(1000);
  }
  if (!rtc.isrunning()) {
    rtc.adjust(DateTime(F(__DATE__), F(__TIME__)));
  }
}

void loop() {
  int botao = digitalRead(BOTAO);
  if (botaoAntes == HIGH && botao == LOW) dozeHoras = !dozeHoras;
  botaoAntes = botao;

  DateTime agora = rtc.now();

  lcd.setCursor(0, 0);
  if (dozeHoras) doisDigitos(agora.twelveHour());
  else doisDigitos(agora.hour());
  lcd.print(':');
  doisDigitos(agora.minute());
  lcd.print(':');
  doisDigitos(agora.second());
  lcd.print(dozeHoras ? (agora.isPM() ? " PM   " : " AM   ") : "      ");

  lcd.setCursor(0, 1);
  lcd.print(DIAS[agora.dayOfTheWeek()]);
  lcd.print(' ');
  doisDigitos(agora.day());
  lcd.print('/');
  doisDigitos(agora.month());
  lcd.print('/');
  lcd.print(agora.year());

  delay(200);
}
''', c, '''
Relogio com RTC e LCD, na ST Nucleo-C031C6.

O que esta demo mostra:
- o RTC DS1307 com a RTClib: DateTime, now() e adjust();
- dois aparelhos no mesmo I2C (RTC e LCD, D14 e D15 no cabecalho do Uno).

Aperte F5. O botao troca entre 24 h e 12 h.
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
