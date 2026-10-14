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
// Controle por Bluetooth, na ESP32.
//
// Na aba BLUETOOTH, clique em Conectar e mande um comando:
//   vermelho, verde, azul, branco, apagar  -> cor do LED RGB
//   bip                                    -> toca o buzzer
//   temp                                   -> responde temperatura e umidade
//   ajuda                                  -> lista os comandos
//
// Um celular de verdade tambem serve: com o sketch rodando, a placa abre
// uma porta em 127.0.0.1 (o painel diz qual). Um terminal TCP qualquer
// faz o papel do app "Serial Bluetooth Terminal", byte a byte.

#include <BluetoothSerial.h>
#include <DHT.h>
#include <Wire.h>
#include <LiquidCrystal_I2C.h>

BluetoothSerial SerialBT;
DHT dht(4, DHT22);
LiquidCrystal_I2C lcd(0x27, 16, 2);

const int PINO_R = 25;
const int PINO_G = 26;
const int PINO_B = 27;
const int BUZZER = 14;

unsigned long ultimoAviso = 0;

void cor(int r, int g, int b) {
  analogWrite(PINO_R, r);
  analogWrite(PINO_G, g);
  analogWrite(PINO_B, b);
}

void mostrar(String linha1, String linha2) {
  lcd.clear();
  lcd.setCursor(0, 0);
  lcd.print(linha1);
  lcd.setCursor(0, 1);
  lcd.print(linha2);
}

void setup() {
  Serial.begin(115200);
  SerialBT.begin("ESP32-Sala");   // o nome que aparece no celular
  dht.begin();
  lcd.init();
  lcd.backlight();
  pinMode(BUZZER, OUTPUT);
  cor(0, 0, 0);
  mostrar("Bluetooth pronto", "ESP32-Sala");
  Serial.println("Esperando o celular: aba BLUETOOTH > Conectar");
}

void loop() {
  if (SerialBT.available()) {
    String comando = SerialBT.readStringUntil('\n');
    comando.trim();
    comando.toLowerCase();
    Serial.println("recebi: " + comando);

    if (comando == "vermelho") {
      cor(255, 0, 0);
      SerialBT.println("LED vermelho");
    } else if (comando == "verde") {
      cor(0, 255, 0);
      SerialBT.println("LED verde");
    } else if (comando == "azul") {
      cor(0, 0, 255);
      SerialBT.println("LED azul");
    } else if (comando == "branco") {
      cor(255, 255, 255);
      SerialBT.println("LED branco");
    } else if (comando == "apagar") {
      cor(0, 0, 0);
      SerialBT.println("LED apagado");
    } else if (comando == "bip") {
      tone(BUZZER, 880, 200);
      SerialBT.println("bip!");
    } else if (comando == "temp") {
      SerialBT.print("temperatura: ");
      SerialBT.print(dht.readTemperature(), 1);
      SerialBT.println(" C");
      SerialBT.print("umidade: ");
      SerialBT.print(dht.readHumidity(), 0);
      SerialBT.println(" %");
    } else if (comando == "ajuda") {
      SerialBT.println("comandos: vermelho, verde, azul, branco, apagar, bip, temp");
    } else {
      SerialBT.println("nao conheco '" + comando + "'. Mande: ajuda");
    }

    mostrar("Ultimo comando:", comando);
  }

  // A cada 5 s a placa manda a temperatura sozinha, como um sensor
  // faria -- mas so se tiver alguem conectado para ouvir.
  if (millis() - ultimoAviso > 5000) {
    ultimoAviso = millis();
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
           fio('placa', 'D14', 'buzzer', '1', LARANJA),
           fio('buzzer', '2', 'placa', 'GND.2', PRETO),
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

COMUM_ESPNOW = r'''
// Endereco de difusao: todas as placas com ESP-NOW ouvem. Na bancada de
// verdade, pode trocar pelo MAC da outra placa (o monitor serial mostra).
uint8_t paraTodos[] = {0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF};

// A MESMA struct nas duas placas: o que sai de uma e copiado byte a byte
// para dentro da outra.
typedef struct {
  float temperatura;
  float umidade;
  int potenciometro;   // 0 a 4095
  bool botao;
} Leitura;

typedef struct {
  bool ledLigado;
} Comando;

Leitura leitura;
Comando comando;
esp_now_peer_info_t par;
'''

SENSOR = r'''
// ESP-NOW: a placa SENSOR.
//
// Le o DHT22, o potenciometro e o botao e manda tudo para a placa PAINEL
// a cada meio segundo -- sem roteador e sem WiFi conectado: o ESP-NOW
// fala direto de placa para placa. O caminho de volta tambem existe: o
// botao do painel acende e apaga o LED daqui.
//
// (Na versao 2.x do pacote da ESP32, o aoReceber recebe
// "const uint8_t *mac" no lugar do esp_now_recv_info_t.)

#include <esp_now.h>
#include <WiFi.h>
#include <DHT.h>

const int PINO_DHT = 4;
const int PINO_POT = 34;
const int PINO_BOTAO = 13;
const int PINO_LED = 27;

DHT dht(PINO_DHT, DHT22);
''' + COMUM_ESPNOW + r'''
void aoEnviar(const uint8_t *mac, esp_now_send_status_t estado) {
  if (estado != ESP_NOW_SEND_SUCCESS) Serial.println("o painel nao ouviu");
}

void aoReceber(const esp_now_recv_info_t *info, const uint8_t *dados, int tamanho) {
  memcpy(&comando, dados, sizeof(comando));
  digitalWrite(PINO_LED, comando.ledLigado ? HIGH : LOW);
  Serial.println(comando.ledLigado ? "o painel acendeu o LED" : "o painel apagou o LED");
}

void setup() {
  Serial.begin(115200);
  pinMode(PINO_BOTAO, INPUT_PULLUP);
  pinMode(PINO_LED, OUTPUT);
  dht.begin();

  WiFi.mode(WIFI_STA);
  Serial.print("MAC do sensor: ");
  Serial.println(WiFi.macAddress());

  if (esp_now_init() != ESP_OK) {
    Serial.println("ESP-NOW nao iniciou");
    return;
  }
  esp_now_register_send_cb(aoEnviar);
  esp_now_register_recv_cb(aoReceber);

  memcpy(par.peer_addr, paraTodos, 6);
  par.channel = 0;
  par.encrypt = false;
  if (esp_now_add_peer(&par) != ESP_OK) {
    Serial.println("nao consegui adicionar o par");
    return;
  }
  Serial.println("sensor pronto");
}

void loop() {
  leitura.temperatura = dht.readTemperature();
  leitura.umidade = dht.readHumidity();
  leitura.potenciometro = analogRead(PINO_POT);
  leitura.botao = digitalRead(PINO_BOTAO) == LOW;

  esp_now_send(paraTodos, (uint8_t *) &leitura, sizeof(leitura));
  delay(500);
}
'''

PAINEL = r'''
// ESP-NOW: a placa PAINEL.
//
// Recebe as leituras da placa SENSOR e mostra: temperatura e umidade no
// LCD; o potenciometro de la vira o angulo do servo daqui; o botao de la
// acende o LED daqui. O botao DAQUI faz o caminho de volta: acende e
// apaga o LED da placa sensor.

#include <esp_now.h>
#include <WiFi.h>
#include <Wire.h>
#include <LiquidCrystal_I2C.h>
#include <ESP32Servo.h>

const int PINO_SERVO = 18;
const int PINO_LED = 19;
const int PINO_BOTAO = 23;

LiquidCrystal_I2C lcd(0x27, 16, 2);
Servo ponteiro;
''' + COMUM_ESPNOW + r'''
// O callback so copia e marca: quem mexe no LCD e no servo e o loop.
// Desenhar dentro do callback seria fazer o radio esperar o display.
volatile bool chegou = false;
unsigned long ultimaLeitura = 0;
int botaoAntes = HIGH;

void aoReceber(const esp_now_recv_info_t *info, const uint8_t *dados, int tamanho) {
  memcpy(&leitura, dados, sizeof(leitura));
  chegou = true;
}

void setup() {
  Serial.begin(115200);
  pinMode(PINO_LED, OUTPUT);
  pinMode(PINO_BOTAO, INPUT_PULLUP);

  lcd.init();
  lcd.backlight();
  lcd.print("Esperando o");
  lcd.setCursor(0, 1);
  lcd.print("sensor...");
  ponteiro.attach(PINO_SERVO);

  WiFi.mode(WIFI_STA);
  Serial.print("MAC do painel: ");
  Serial.println(WiFi.macAddress());

  if (esp_now_init() != ESP_OK) {
    Serial.println("ESP-NOW nao iniciou");
    return;
  }
  esp_now_register_recv_cb(aoReceber);

  memcpy(par.peer_addr, paraTodos, 6);
  par.channel = 0;
  par.encrypt = false;
  esp_now_add_peer(&par);
  Serial.println("painel pronto");
}

void loop() {
  if (chegou) {
    chegou = false;
    ultimaLeitura = millis();

    lcd.clear();
    lcd.setCursor(0, 0);
    lcd.print("Temp: ");
    lcd.print(leitura.temperatura, 1);
    lcd.print(" C");
    lcd.setCursor(0, 1);
    lcd.print("Umid: ");
    lcd.print(leitura.umidade, 0);
    lcd.print(" %");

    ponteiro.write(map(leitura.potenciometro, 0, 4095, 0, 180));
    digitalWrite(PINO_LED, leitura.botao ? HIGH : LOW);
  }

  // Tres segundos sem noticia do sensor: avisa.
  if (ultimaLeitura > 0 && millis() - ultimaLeitura > 3000) {
    lcd.clear();
    lcd.print("Sensor sumiu!");
    ultimaLeitura = 0;
  }

  // Cada aperto do botao daqui inverte o LED da placa sensor.
  int botao = digitalRead(PINO_BOTAO);
  if (botaoAntes == HIGH && botao == LOW) {
    comando.ledLigado = !comando.ledLigado;
    esp_now_send(paraTodos, (uint8_t *) &comando, sizeof(comando));
    Serial.println(comando.ledLigado ? "mandei: acender" : "mandei: apagar");
  }
  botaoAntes = botao;
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
// Analisador logico: cinco sinais de verdade, lado a lado.
//
// Selecione o analisador no circuito e clique em "Abrir o analisador":
//   D0  pino 5   PWM de 25% a 976 Hz (Timer0) - o LED fraco
//   D1  pino 6   PWM de 75% a 976 Hz - o LED forte
//   D2  pino 8   tone() de 1 kHz, em rajadas de 60 ms
//   D3  pino 9   o pulso do servo, 50 Hz: gire o potenciometro e a
//                largura muda de 0,5 a 2,4 ms
//   D4  pino 11  DADO do shiftOut
//   D5  pino 12  RELOGIO do shiftOut
//   D6  pino 13  TRAVA (latch)
//   D7  pino 1   a serial saindo pelo TX, a 9600 baud - com a letra
//                escrita em cima de cada byte
//
// Pare a captura (Espaco), aproxime com a roda do mouse e passe o mouse
// num canal: o analisador mede a largura, o periodo e a frequencia. O
// "Salvar .vcd" leva a captura para o PulseView.
//
// Na placa de verdade os pinos foram escolhidos para nao brigarem: o
// Servo usa o Timer1 (e desliga o PWM dos pinos 9 e 10), e o tone() usa
// o Timer2 (e desliga o PWM dos pinos 3 e 11). Por isso o PWM esta no 5
// e no 6, que sao do Timer0.

#include <Servo.h>

const int LED_FRACO = 5;
const int LED_FORTE = 6;
const int BUZZER = 8;
const int SERVO = 9;
const int DADO = 11;
const int RELOGIO = 12;
const int TRAVA = 13;
const int POT = A0;

Servo ponteiro;
byte contador = 0;
unsigned long ultimoBip = 0;

void setup() {
  Serial.begin(9600);
  pinMode(DADO, OUTPUT);
  pinMode(RELOGIO, OUTPUT);
  pinMode(TRAVA, OUTPUT);

  analogWrite(LED_FRACO, 64);    // 25% do tempo alto
  analogWrite(LED_FORTE, 192);   // 75%
  ponteiro.attach(SERVO);
}

void loop() {
  // O servo segue o potenciometro.
  int angulo = map(analogRead(POT), 0, 1023, 0, 180);
  ponteiro.write(angulo);

  // Um byte pelo shiftOut, com a trava em volta: e o que um 74HC595
  // receberia para acender oito LEDs.
  digitalWrite(TRAVA, LOW);
  shiftOut(DADO, RELOGIO, MSBFIRST, contador);
  digitalWrite(TRAVA, HIGH);

  // A cada meio segundo, um bip e uma linha na serial.
  if (millis() - ultimoBip >= 500) {
    ultimoBip = millis();
    tone(BUZZER, 1000, 60);
    Serial.print("n=");
    Serial.println(contador);
  }

  contador++;
  delay(20);
}
'''

gravar('uno-analisador', ANALISADOR,
       pecas=[
           {'Id': 'placa', 'Tipo': 'arduino-uno', 'X': 300, 'Y': 220},
           {'Id': 'la', 'Tipo': 'logic-analyzer', 'X': 330, 'Y': 20, 'Nome': 'analisador',
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
           fio('placa', '8', 'buzzer', '1', LARANJA),
           fio('buzzer', '2', 'placa', 'GND.1', PRETO),
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
