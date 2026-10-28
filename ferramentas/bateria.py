"""python ferramentas/bateria.py [pasta-de-saida]

A bateria de testes da Alpha 18.

Gera um .sktch para CADA placa, CADA componente e CADA linguagem, e os
organiza em pastas. Depois o autoteste roda todos e diz quais falharam.

Por que gerar em vez de escrever a mao: sao mais de cem projetos. Escrever
cem a mao daria cem chances de errar o nome de um pino, e o erro apareceria
como "o simulador esta quebrado" quando o quebrado seria o teste.

A regra de cada projeto e sempre a mesma:

  - PLACA:      um LED com resistor no pino de saida mais seguro dela;
  - COMPONENTE: a peca ligada numa ESP32, com o sketch que a exercita;
  - LINGUAGEM:  o mesmo pisca-pisca, escrito no idioma de cada uma.

Assim, quando um falha, o que mudou e uma coisa so.
"""
import io
import json
import os
import sys

BASE = 'C:/Users/seraf/OneDrive/Desktop/SimuladorESP32/'
SAIDA = sys.argv[1] if len(sys.argv) > 1 else 'E:/Sktch[/BATERIA DE TESTES'

CATALOGO = json.load(io.open(BASE + 'app/assets/pecas.json', encoding='utf-8'))
POR_ID = {p['id']: p for p in CATALOGO}


def escrever(pasta, nome, projeto):
    caminho = os.path.join(SAIDA, pasta)
    os.makedirs(caminho, exist_ok=True)

    io.open(os.path.join(caminho, nome + '.sktch'), 'w',
            encoding='utf-8', newline='').write(
        json.dumps(projeto, ensure_ascii=False, indent=1))


def projeto(codigo, pecas, fios, descricao):
    return {
        'formato': 'sktch',
        'versao': 1,
        'gerado': 'bateria de testes (Alpha 18; pecas da Alpha 23; alimentacao da Alpha 24)',
        'descricao': descricao,
        'codigo': codigo,
        'circuito': {'Pecas': pecas, 'Fios': fios},
    }


def pinos_de(tipo):
    return [p['nome'] for p in POR_ID.get(tipo, {}).get('pinos', [])]


def terra(tipo):
    """O nome do terra desta placa: pode ser GND, GND.1, GND.2..."""
    nomes = pinos_de(tipo)

    for candidato in ('GND', 'GND.1', 'GND.2', 'GND.3'):
        if candidato in nomes:
            return candidato

    return next((n for n in nomes if n.startswith('GND')), 'GND')


def alimentacao(tipo):
    nomes = pinos_de(tipo)

    for candidato in ('3V3', '3.3V', '5V', 'VCC', '+3V3', '+5V', 'VIN', '3V3.1'):
        if candidato in nomes:
            return candidato

    return next((n for n in nomes if n in ('VBUS', 'VSYS')), nomes[0] if nomes else 'VCC')


# =====================================================================
#  1) uma por PLACA
# =====================================================================
#
# O pino de saida de cada uma, pelo nome da serigrafia dela, e o numero
# que o sketch escreve. Os dois tem que bater: e justamente essa
# traducao que o teste prova.
PINO_DA_PLACA = {
    'esp32-devkit-v1': ('D2', 2),
    'esp32-s2-devkit': ('4', 4),
    'esp32-s3-devkit': ('4', 4),
    'esp32-c3-devkit': ('4', 4),
    'esp32-c6-devkit': ('4', 4),
    'esp32-h2-devkit': ('4', 4),

    'arduino-uno': ('13', 13),
    'arduino-mega': ('13', 13),
    'arduino-nano': ('13', 13),
    'nano-rp2040-connect': ('D13', 13),
    'franzininho': ('PB1', 1),
    'attiny85': ('PB0', 0),

    'esp8266-nodemcu': ('D5', 14),

    'pi-pico': ('GP15', 15),
    'pi-pico-w': ('GP15', 15),

    'stm32-bluepill': ('PC13', 45),
    'st-nucleo-l031k6': ('D5', 5),
    'st-nucleo-c031c6': ('D5', 5),
}

# Os modulos ESP-xx: o GPIO que cada um traz para fora (a pinagem de
# verdade de cada modulo, Alpha 25).
for modulo in ('esp-01', 'esp-02'):
    PINO_DA_PLACA[modulo] = ('GPIO2', 2)

for modulo in ('esp-05', 'esp-10'):
    PINO_DA_PLACA[modulo] = ('TXD', 1)

# O ESP-11 so traz o GPIO0 (e a serial); o ESP-14, so o GPIO0 do ESP8266
# (o resto e do STM8S003 de dentro).
for modulo in ('esp-11', 'esp-14'):
    PINO_DA_PLACA[modulo] = ('GPIO0', 0)

# Os sem GPIO4: o GPIO14, que nao manda na partida.
for modulo in ('esp-03', 'esp-04', 'esp-06', 'esp-08', 'esp-09'):
    PINO_DA_PLACA[modulo] = ('GPIO14', 14)

for modulo in ('esp-07', 'esp-12', 'esp-12e', 'esp-12f', 'esp-13'):
    PINO_DA_PLACA[modulo] = ('GPIO4', 4)


def sketch_de_placa(nome, numero, rotulo):
    return [
        f'// Bateria de testes — {nome}.',
        '//',
        '// Pisca o LED externo e conta as piscadas no monitor serial.',
        f'// O pino {rotulo} da serigrafia é o GPIO {numero}: se essa',
        '// tradução estiver errada, o LED fica apagado sem dar erro.',
        '',
        f'const int LED = {numero};',
        'int piscadas = 0;',
        '',
        'void setup() {',
        '  Serial.begin(115200);',
        '  pinMode(LED, OUTPUT);',
        f'  Serial.println("{nome} acordou");',
        '}',
        '',
        'void loop() {',
        '  digitalWrite(LED, HIGH);',
        '  delay(200);',
        '  digitalWrite(LED, LOW);',
        '  delay(200);',
        '',
        '  piscadas++;',
        '  Serial.print("piscadas: ");',
        '  Serial.println(piscadas);',
        '}',
    ]


placas = 0

for tipo, (rotulo, numero) in sorted(PINO_DA_PLACA.items()):
    if tipo not in POR_ID:
        print('!! placa sem desenho:', tipo)
        continue

    nome = POR_ID[tipo].get('nome') or tipo

    escrever('placas', tipo, projeto(
        sketch_de_placa(tipo, numero, rotulo),
        [
            {'Id': 'placa', 'Tipo': tipo, 'X': 120, 'Y': 60},
            {'Id': 'led', 'Tipo': 'led', 'X': 520, 'Y': 120, 'Cor': 'vermelho'},
            {'Id': 'r', 'Tipo': 'resistor', 'X': 520, 'Y': 280, 'Ohms': 330},
        ],
        [
            {'DePeca': 'placa', 'DePino': rotulo,
             'ParaPeca': 'led', 'ParaPino': 'A', 'Cor': '#00A651'},
            {'DePeca': 'led', 'DePino': 'C',
             'ParaPeca': 'r', 'ParaPino': '1', 'Cor': '#1A1A1A'},
            {'DePeca': 'r', 'DePino': '2',
             'ParaPeca': 'placa', 'ParaPino': terra(tipo), 'Cor': '#1A1A1A'},
        ],
        [
            f'Teste da placa {tipo}.',
            '',
            f'O pino {rotulo} da serigrafia é o GPIO {numero}. O LED tem que',
            'piscar e o monitor tem que contar as piscadas.',
        ]))

    placas += 1

print(f'placas: {placas}')


# =====================================================================
#  2) uma por COMPONENTE
# =====================================================================
#
# Cada peca ligada numa ESP32, com o sketch que a exercita. A ESP32
# porque ela tem pino para tudo -- I2C, analogico, PWM -- e assim o que
# varia entre um teste e o outro e SO a peca.
ESP = 'esp32-devkit-v1'

# Peca -> (pinos dela que se liga, pinos da ESP32, o que o sketch faz)
#
# O que nao esta aqui recebe o tratamento generico: liga o primeiro pino
# num GPIO, o resto na alimentacao e no terra, e o sketch so mexe no
# GPIO. Nao prova a peca inteira, mas prova que ela entra no circuito,
# que o netlist a enxerga e que nada estoura.
LIGACOES = {
    'led': (['A', 'C'], ['D2', 'GND.1'], 'saida'),
    'rgb-led': (['R', 'G', 'B', 'COM'], ['D2', 'D4', 'D5', 'GND.1'], 'saida'),
    'pushbutton': (['1.l', '2.l'], ['D15', 'GND.1'], 'entrada'),
    'pushbutton-6mm': (['1.l', '2.l'], ['D15', 'GND.1'], 'entrada'),
    'potentiometer': (['VCC', 'SIG', 'GND'], ['3V3', 'D34', 'GND.1'], 'analogico'),
    'slide-potentiometer': (['VCC', 'SIG', 'GND'], ['3V3', 'D34', 'GND.1'], 'analogico'),
    'photoresistor-sensor': (['VCC', 'AO', 'GND'], ['3V3', 'D34', 'GND.1'], 'analogico'),
    # O NTC chama a saida de OUT, e nao de AO como o LDR.
    'ntc-temperature-sensor': (['VCC', 'OUT', 'GND'], ['3V3', 'D34', 'GND.1'], 'analogico'),
    'buzzer': (['1', '2'], ['D18', 'GND.1'], 'som'),
    # O alto-falante direto no pino, com tone(): toca, e o simulador avisa que
    # a porta trabalha no limite (o certo e pelo amplificador).
    'alto-falante': (['+', '-'], ['D18', 'GND.1'], 'som'),
    # O amplificador I2S: os tres fios do I2S e a alimentacao.
    'max98357a': (['BCLK', 'LRC', 'DIN', 'VIN', 'GND'], ['D26', 'D25', 'D22', '3V3', 'GND.1'], 'saida'),
    # O servo pede 4,8 a 6 V: no VIN (os 5 V da USB), e nao no 3V3.
    'servo': (['PWM', 'V+', 'GND'], ['D13', 'VIN', 'GND.1'], 'servo'),
    # A chave fim de curso: o comum no GND, o NA no pino com pull-up.
    'micro-switch': (['COM', 'NO'], ['GND.1', 'D15'], 'entrada'),
    'dht22': (['VCC', 'SDA', 'GND'], ['3V3', 'D4', 'GND.1'], 'dht'),
    # O DHT11 chama o pino de dados de DATA; o DHT22 chama de SDA.
    'dht11': (['VCC', 'DATA', 'GND'], ['3V3', 'D4', 'GND.1'], 'dht'),
    'hc-sr04': (['VCC', 'TRIG', 'ECHO', 'GND'],
                ['3V3', 'D5', 'D18', 'GND.1'], 'ultrassom'),
    'lcd1602-i2c': (['VCC', 'GND', 'SDA', 'SCL'],
                    ['3V3', 'GND.1', 'D21', 'D22'], 'lcd'),
    'lcd2004-i2c': (['VCC', 'GND', 'SDA', 'SCL'],
                    ['3V3', 'GND.1', 'D21', 'D22'], 'lcd'),
    'ssd1306-i2c': (['VCC', 'GND', 'SDA', 'SCL'],
                    ['3V3', 'GND.1', 'D21', 'D22'], 'oled'),
    'neopixel': (['VDD', 'DIN', 'VSS'], ['3V3', 'D5', 'GND.1'], 'fita'),
}

SKETCHES = {
    'saida': [
        'const int PINO = 2;',
        '',
        'void setup() {',
        '  Serial.begin(115200);',
        '  pinMode(PINO, OUTPUT);',
        '  Serial.println("saida pronta");',
        '}',
        '',
        'void loop() {',
        '  digitalWrite(PINO, HIGH);',
        '  delay(150);',
        '  digitalWrite(PINO, LOW);',
        '  delay(150);',
        '}',
    ],
    'entrada': [
        'const int PINO = 15;',
        'const int LED = 2;',
        '',
        'void setup() {',
        '  Serial.begin(115200);',
        '  pinMode(PINO, INPUT_PULLUP);',
        '  pinMode(LED, OUTPUT);',
        '  Serial.println("entrada pronta");',
        '}',
        '',
        'void loop() {',
        '  int estado = digitalRead(PINO);',
        '  digitalWrite(LED, estado == LOW ? HIGH : LOW);',
        '',
        '  Serial.print("botao: ");',
        '  Serial.println(estado);',
        '  delay(200);',
        '}',
    ],
    'analogico': [
        'const int PINO = 34;',
        '',
        'void setup() {',
        '  Serial.begin(115200);',
        '  Serial.println("analogico pronto");',
        '}',
        '',
        'void loop() {',
        '  int valor = analogRead(PINO);',
        '',
        '  Serial.print("leitura: ");',
        '  Serial.println(valor);',
        '  delay(200);',
        '}',
    ],
    'som': [
        'const int BUZZER = 18;',
        '',
        'void setup() {',
        '  Serial.begin(115200);',
        '  pinMode(BUZZER, OUTPUT);',
        '  Serial.println("buzzer pronto");',
        '}',
        '',
        'void loop() {',
        '  tone(BUZZER, 440);',
        '  delay(200);',
        '  noTone(BUZZER);',
        '  delay(200);',
        '}',
    ],
    'servo': [
        '#include <Servo.h>',
        '',
        'Servo meu;',
        '',
        'void setup() {',
        '  Serial.begin(115200);',
        '  meu.attach(13);',
        '  Serial.println("servo pronto");',
        '}',
        '',
        'void loop() {',
        '  meu.write(0);',
        '  delay(300);',
        '  meu.write(180);',
        '  delay(300);',
        '}',
    ],
    'dht': [
        '#include <DHT.h>',
        '',
        'DHT dht(4, DHT22);',
        '',
        'void setup() {',
        '  Serial.begin(115200);',
        '  dht.begin();',
        '  Serial.println("dht pronto");',
        '}',
        '',
        'void loop() {',
        '  float t = dht.readTemperature();',
        '  float u = dht.readHumidity();',
        '',
        '  Serial.print("temperatura: ");',
        '  Serial.println(t);',
        '  Serial.print("umidade: ");',
        '  Serial.println(u);',
        '  delay(400);',
        '}',
    ],
    'ultrassom': [
        'const int TRIG = 5;',
        'const int ECHO = 18;',
        '',
        'void setup() {',
        '  Serial.begin(115200);',
        '  pinMode(TRIG, OUTPUT);',
        '  pinMode(ECHO, INPUT);',
        '  Serial.println("ultrassom pronto");',
        '}',
        '',
        'void loop() {',
        '  digitalWrite(TRIG, LOW);',
        '  delayMicroseconds(2);',
        '  digitalWrite(TRIG, HIGH);',
        '  delayMicroseconds(10);',
        '  digitalWrite(TRIG, LOW);',
        '',
        '  long tempo = pulseIn(ECHO, HIGH);',
        '  long cm = tempo / 58;',
        '',
        '  Serial.print("distancia: ");',
        '  Serial.println(cm);',
        '  delay(300);',
        '}',
    ],
    'lcd': [
        '#include <LiquidCrystal_I2C.h>',
        '',
        'LiquidCrystal_I2C lcd(0x27, 16, 2);',
        'int voltas = 0;',
        '',
        'void setup() {',
        '  Serial.begin(115200);',
        '  lcd.init();',
        '  lcd.backlight();',
        '  Serial.println("lcd pronto");',
        '}',
        '',
        'void loop() {',
        '  lcd.setCursor(0, 0);',
        '  lcd.print("CircuitSketch");',
        '  lcd.setCursor(0, 1);',
        '  lcd.print(voltas);',
        '',
        '  voltas++;',
        '  delay(400);',
        '}',
    ],
    'oled': [
        '#include <Adafruit_SSD1306.h>',
        '',
        'Adafruit_SSD1306 tela(128, 64);',
        'int voltas = 0;',
        '',
        'void setup() {',
        '  Serial.begin(115200);',
        '  tela.begin(SSD1306_SWITCHCAPVCC, 0x3C);',
        '  Serial.println("oled pronto");',
        '}',
        '',
        'void loop() {',
        '  tela.clearDisplay();',
        '  tela.setCursor(0, 0);',
        '  tela.print("voltas: ");',
        '  tela.print(voltas);',
        '  tela.display();',
        '',
        '  voltas++;',
        '  delay(400);',
        '}',
    ],
    'fita': [
        '#include <Adafruit_NeoPixel.h>',
        '',
        'Adafruit_NeoPixel fita(8, 5, NEO_GRB + NEO_KHZ800);',
        '',
        'void setup() {',
        '  Serial.begin(115200);',
        '  fita.begin();',
        '  Serial.println("fita pronta");',
        '}',
        '',
        'void loop() {',
        '  for (int i = 0; i < 8; i++) {',
        '    fita.setPixelColor(i, fita.Color(120, 0, 0));',
        '  }',
        '  fita.show();',
        '  delay(300);',
        '',
        '  fita.clear();',
        '  fita.show();',
        '  delay(300);',
        '}',
    ],
    # Ponte H com enable (L298N, L293D, TB6612FNG): o sentido nas duas
    # entradas, a velocidade no PWM do enable.
    'ponte-h': [
        '// O sentido nas duas entradas; a velocidade no PWM do enable.',
        'const int ENTRADA_1 = 13;',
        'const int ENTRADA_2 = 12;',
        'const int VELOCIDADE = 14;',
        '',
        'void setup() {',
        '  Serial.begin(115200);',
        '  pinMode(ENTRADA_1, OUTPUT);',
        '  pinMode(ENTRADA_2, OUTPUT);',
        '  pinMode(VELOCIDADE, OUTPUT);',
        '  Serial.println("ponte H pronta");',
        '}',
        '',
        'void loop() {',
        '  Serial.println("para frente, acelerando");',
        '  digitalWrite(ENTRADA_1, HIGH);',
        '  digitalWrite(ENTRADA_2, LOW);',
        '  for (int v = 0; v <= 255; v += 51) {',
        '    analogWrite(VELOCIDADE, v);',
        '    delay(400);',
        '  }',
        '',
        '  Serial.println("para tras");',
        '  digitalWrite(ENTRADA_1, LOW);',
        '  digitalWrite(ENTRADA_2, HIGH);',
        '  delay(1500);',
        '',
        '  Serial.println("freio: as duas entradas iguais");',
        '  digitalWrite(ENTRADA_1, HIGH);',
        '  delay(1000);',
        '}',
    ],
    # Driver de duas entradas por motor (L9110S, DRV8833, BTS7960): a
    # velocidade e o PWM de uma entrada, com a outra em zero. So analogWrite
    # nos dois pinos: na ESP32, o digitalWrite num pino que ja tem PWM nao
    # solta o PWM.
    'ponte-h-2': [
        '// A velocidade e o PWM de uma entrada, com a outra em zero.',
        'const int ENTRADA_A = 13;',
        'const int ENTRADA_B = 12;',
        '',
        'void setup() {',
        '  Serial.begin(115200);',
        '  pinMode(ENTRADA_A, OUTPUT);',
        '  pinMode(ENTRADA_B, OUTPUT);',
        '  Serial.println("driver pronto");',
        '}',
        '',
        'void loop() {',
        '  Serial.println("para frente, acelerando");',
        '  analogWrite(ENTRADA_B, 0);',
        '  for (int v = 0; v <= 255; v += 51) {',
        '    analogWrite(ENTRADA_A, v);',
        '    delay(400);',
        '  }',
        '',
        '  Serial.println("para tras, a meia velocidade");',
        '  analogWrite(ENTRADA_A, 0);',
        '  analogWrite(ENTRADA_B, 128);',
        '  delay(1500);',
        '',
        '  Serial.println("freio: as duas em cima");',
        '  analogWrite(ENTRADA_A, 255);',
        '  analogWrite(ENTRADA_B, 255);',
        '  delay(1000);',
        '}',
    ],
    # O A4988 e o DRV8825: um pulso no STEP e um passo; o DIR e o sentido.
    'passo-driver': [
        '// Um pulso no STEP e um passo do motor; o DIR escolhe o sentido.',
        '// Com MS1..MS3 (ou M0..M2) soltos, o passo e inteiro: 200 = uma volta.',
        'const int PINO_STEP = 13;',
        'const int PINO_DIR = 12;',
        '',
        'void girar(int passos, int sentido) {',
        '  digitalWrite(PINO_DIR, sentido);',
        '  for (int i = 0; i < passos; i++) {',
        '    digitalWrite(PINO_STEP, HIGH);',
        '    delayMicroseconds(1000);',
        '    digitalWrite(PINO_STEP, LOW);',
        '    delayMicroseconds(1000);',
        '  }',
        '}',
        '',
        'void setup() {',
        '  Serial.begin(115200);',
        '  pinMode(PINO_STEP, OUTPUT);',
        '  pinMode(PINO_DIR, OUTPUT);',
        '  Serial.println("driver de passo pronto");',
        '}',
        '',
        'void loop() {',
        '  Serial.println("uma volta para um lado");',
        '  girar(200, HIGH);',
        '  delay(500);',
        '  Serial.println("e de volta");',
        '  girar(200, LOW);',
        '  delay(500);',
        '}',
    ],
    # O 28BYJ-48 pelo ULN2003, com a Stepper: os pinos na ordem IN1, IN3,
    # IN2, IN4 -- e a ordem que faz a sequencia da Stepper girar este motor.
    'passo-28byj': [
        '#include <Stepper.h>',
        '',
        '// 2048 passos por volta (32 no rotor x reducao de ~64).',
        '// Os pinos na ordem IN1, IN3, IN2, IN4: e assim que a Stepper gira o 28BYJ-48.',
        'Stepper motor(2048, 14, 26, 27, 25);',
        '',
        'void setup() {',
        '  Serial.begin(115200);',
        '  motor.setSpeed(10);',
        '  Serial.println("28BYJ-48 pronto");',
        '}',
        '',
        'void loop() {',
        '  Serial.println("um quarto de volta");',
        '  motor.step(512);',
        '  delay(300);',
        '  Serial.println("e de volta");',
        '  motor.step(-512);',
        '  delay(300);',
        '}',
    ],
    # O servo forte (MG996R), na fonte dele: o mesmo vai e volta do servo.
    'servo-forte': [
        '#include <ESP32Servo.h>',
        '',
        'Servo servo;',
        '',
        'void setup() {',
        '  Serial.begin(115200);',
        '  servo.attach(13, 500, 2500);',
        '  Serial.println("servo pronto");',
        '}',
        '',
        'void loop() {',
        '  servo.write(0);',
        '  delay(800);',
        '  servo.write(90);',
        '  delay(800);',
        '  servo.write(180);',
        '  delay(800);',
        '}',
    ],
    # O servo de rotacao continua: o "angulo" vira velocidade e sentido.
    'servo-360': [
        '#include <ESP32Servo.h>',
        '',
        '// No servo 360 o write() e velocidade: 90 para, 0 e 180 sao o maximo',
        '// de cada lado, e o que fica no meio e mais devagar.',
        'Servo roda;',
        '',
        'void setup() {',
        '  Serial.begin(115200);',
        '  roda.attach(13);',
        '  Serial.println("servo 360 pronto");',
        '}',
        '',
        'void loop() {',
        '  Serial.println("rapido para um lado");',
        '  roda.write(0);',
        '  delay(1500);',
        '  Serial.println("parado");',
        '  roda.write(90);',
        '  delay(1000);',
        '  Serial.println("devagar para o outro");',
        '  roda.write(135);',
        '  delay(1500);',
        '}',
    ],
    # O ESC: arma com o acelerador no minimo, depois acelera.
    'esc': [
        '#include <ESP32Servo.h>',
        '',
        '// O ESC le um pulso de servo: 1000 us e o acelerador no minimo,',
        '// 2000 us e o maximo. Ele so arma (os bipes) com o minimo primeiro.',
        'Servo esc;',
        '',
        'void setup() {',
        '  Serial.begin(115200);',
        '  esc.attach(13, 1000, 2000);',
        '  esc.writeMicroseconds(1000);',
        '  Serial.println("armando o ESC (acelerador no minimo)...");',
        '  delay(3000);',
        '  Serial.println("armado");',
        '}',
        '',
        'void loop() {',
        '  Serial.println("acelerador em 30%");',
        '  esc.writeMicroseconds(1300);',
        '  delay(3000);',
        '  Serial.println("acelerador em 60%");',
        '  esc.writeMicroseconds(1600);',
        '  delay(3000);',
        '  Serial.println("parado");',
        '  esc.writeMicroseconds(1000);',
        '  delay(3000);',
        '}',
    ],
    # A ESC 4-em-1 de drone: um sinal para cada motor.
    'esc-4em1': [
        '#include <ESP32Servo.h>',
        '',
        '// Os quatro motores de um drone na ESC 4-em-1: um sinal para cada.',
        '// Como na ESC simples, eles so armam com o acelerador no minimo.',
        'Servo m1, m2, m3, m4;',
        '',
        'void todos(int us) {',
        '  m1.writeMicroseconds(us);',
        '  m2.writeMicroseconds(us);',
        '  m3.writeMicroseconds(us);',
        '  m4.writeMicroseconds(us);',
        '}',
        '',
        'void setup() {',
        '  Serial.begin(115200);',
        '  m1.attach(13, 1000, 2000);',
        '  m2.attach(12, 1000, 2000);',
        '  m3.attach(14, 1000, 2000);',
        '  m4.attach(27, 1000, 2000);',
        '  todos(1000);',
        '  Serial.println("armando os quatro (acelerador no minimo)...");',
        '  delay(3000);',
        '  Serial.println("armados");',
        '}',
        '',
        'void loop() {',
        '  Serial.println("os quatro em 30%");',
        '  todos(1300);',
        '  delay(3000);',
        '  Serial.println("o motor 1 acelera");',
        '  m1.writeMicroseconds(1600);',
        '  delay(1500);',
        '  Serial.println("parados");',
        '  todos(1000);',
        '  delay(2000);',
        '}',
    ],
    # O receptor de radio: a largura do pulso de cada canal.
    'receptor': [
        '// Cada canal do receptor e um pulso de servo: 1000 a 2000 us.',
        '// Ligue o controle (a chave dele) e mexa os manches.',
        'const int CANAL_1 = 34;  // manche direito, para os lados',
        'const int CANAL_2 = 35;  // manche direito, para frente e para tras',
        'const int CANAL_3 = 32;  // acelerador (manche esquerdo)',
        '',
        'void setup() {',
        '  Serial.begin(115200);',
        '  pinMode(CANAL_1, INPUT);',
        '  pinMode(CANAL_2, INPUT);',
        '  pinMode(CANAL_3, INPUT);',
        '  Serial.println("receptor pronto: ligue o controle e mexa os manches");',
        '}',
        '',
        'void loop() {',
        '  unsigned long c1 = pulseIn(CANAL_1, HIGH, 30000);',
        '  unsigned long c2 = pulseIn(CANAL_2, HIGH, 30000);',
        '  unsigned long c3 = pulseIn(CANAL_3, HIGH, 30000);',
        '  if (c1 == 0) {',
        '    Serial.println("sem sinal (o controle esta desligado?)");',
        '  } else {',
        '    Serial.print("CH1 ");',
        '    Serial.print(c1);',
        '    Serial.print(" us   CH2 ");',
        '    Serial.print(c2);',
        '    Serial.print(" us   CH3 ");',
        '    Serial.print(c3);',
        '    Serial.println(" us");',
        '  }',
        '  delay(200);',
        '}',
    ],
    'passo': [
        '#include <Stepper.h>',
        '',
        'Stepper motor(200, 14, 27, 26, 25);',
        '',
        'void setup() {',
        '  Serial.begin(115200);',
        '  motor.setSpeed(60);',
        '  Serial.println("motor pronto");',
        '}',
        '',
        'void loop() {',
        '  motor.step(50);',
        '  delay(200);',
        '  motor.step(-50);',
        '  delay(200);',
        '}',
    ],
    # A placa ligada pelo regulador: o LED do pino 13 do Uno pisca.
    'pisca-13': [
        '// Esta placa e alimentada pelo regulador de tensao, sem o cabo USB.',
        'const int LED = 13;',
        '',
        'void setup() {',
        '  Serial.begin(9600);',
        '  pinMode(LED, OUTPUT);',
        '  Serial.println("ligou pelo regulador");',
        '}',
        '',
        'void loop() {',
        '  digitalWrite(LED, HIGH);',
        '  delay(300);',
        '  digitalWrite(LED, LOW);',
        '  delay(300);',
        '}',
    ],
    'generico': [
        '// A peça entra no circuito e o netlist a enxerga.',
        '// O sketch mexe no pino em que ela está ligada.',
        '',
        'const int PINO = 2;',
        '',
        'void setup() {',
        '  Serial.begin(115200);',
        '  pinMode(PINO, OUTPUT);',
        '  Serial.println("peca no circuito");',
        '}',
        '',
        'void loop() {',
        '  digitalWrite(PINO, HIGH);',
        '  delay(150);',
        '  digitalWrite(PINO, LOW);',
        '  delay(150);',
        '}',
    ],
}

# =====================================================================
#  as pecas que so funcionam acompanhadas (Alpha 23)
# =====================================================================
#
# Motor nao vai direto no GPIO: vai por um driver, com a bateria dele. Cada
# receita e o circuito inteiro -- as outras pecas, os fios e o sketch --, do
# jeito que se monta na bancada. 'placa' e a ESP32 e 'peca' e a do teste.

VERMELHO, PRETO, VERDE, AZUL, AMARELO, LARANJA, ROXO = (
    '#D23B33', '#1A1A1A', '#00A651', '#1F6FD1', '#E8B400', '#F07F1A', '#8E44AD')

MM = 96 / 25.4


def ligar(a, pa, b, pb, cor=PRETO):
    return {'DePeca': a, 'DePino': pa, 'ParaPeca': b, 'ParaPino': pb, 'Cor': cor}


def fonte(volts, corrente, capacidade, nome, x=300, y=300, id_='pilhas'):
    """A bateria: a corrente maxima que ela entrega (mA) e a carga dela (mAh)."""
    return {'Id': id_, 'Tipo': 'bateria', 'X': x, 'Y': y, 'Volts': volts,
            'MiliAmperes': corrente, 'MiliAmperesHora': capacidade, 'Nome': nome}


def no_eixo(acessorio, motor, x, y):
    """O acessorio com o centro no eixo do motor (que esta em x, y): encaixado."""
    eixo = {'motor-tt': (17.05, 13), 'motor-dc': (14, 12.5), 'motor-brushless': (18, 18)}[motor]
    centro = {'roda-tt': (33, 33), 'helice-130': (38, 38), 'helice-1045': (128, 17), 'helice-5045': (64, 64)}[acessorio]
    return (round(x + (eixo[0] - centro[0]) * MM, 2), round(y + (eixo[1] - centro[1]) * MM, 2))


def ponte_com_enable(driver, motor_tipo, ligar_driver, saidas, nome_bateria='4 pilhas AA (6 V)'):
    """Os fios de uma ponte com enable: a logica, as entradas e o motor."""
    pecas = [
        {'Id': 'motor', 'Tipo': motor_tipo, 'X': 520, 'Y': 60},
        fonte(6, 2000, 2500, nome_bateria),
    ]
    fios = ligar_driver + [
        ligar('peca', saidas[0], 'motor', '+', VERMELHO),
        ligar('peca', saidas[1], 'motor', '-', PRETO),
    ]
    return pecas, fios


def a4988(nome_peca):
    """O A4988 do esquema da Pololu: 12 V no VMOT, 3,3 V no VDD, RST no SLP."""
    driver = 'peca' if nome_peca == 'a4988' else 'driver'
    motor = 'peca' if nome_peca == 'stepper-motor' else 'motor'
    pecas = [
        fonte(12, 3000, 100000, 'fonte de 12 V'),
        {'Id': driver, 'Tipo': 'a4988', 'X': 300, 'Y': 60},
        {'Id': motor, 'Tipo': 'stepper-motor', 'X': 480, 'Y': 20},
    ]
    fios = [
        ligar('pilhas', '+', driver, 'VMOT', VERMELHO),
        ligar('pilhas', '-', driver, 'GND', PRETO),
        ligar('placa', 'GND.2', driver, 'GND.2', PRETO),
        ligar('placa', '3V3', driver, 'VDD', VERMELHO),
        ligar(driver, 'RST', driver, 'SLP', ROXO),
        ligar('placa', 'D13', driver, 'STEP', VERDE),
        ligar('placa', 'D12', driver, 'DIR', AZUL),
        ligar(driver, '1A', motor, 'A+', VERMELHO),
        ligar(driver, '1B', motor, 'A-', AZUL),
        ligar(driver, '2A', motor, 'B+', VERDE),
        ligar(driver, '2B', motor, 'B-', PRETO),
    ]
    return pecas, fios


def uln2003(nome_peca):
    """O ULN2003 nos 5 V da USB e o 28BYJ-48 no conector dele."""
    driver = 'peca' if nome_peca == 'uln2003' else 'driver'
    motor = 'peca' if nome_peca == 'motor-28byj48' else 'motor'
    pecas = [
        {'Id': driver, 'Tipo': 'uln2003', 'X': 300, 'Y': 40},
        {'Id': motor, 'Tipo': 'motor-28byj48', 'X': 500, 'Y': 40},
    ]
    fios = [
        ligar('placa', 'VIN', driver, '+', VERMELHO),
        ligar('placa', 'GND.2', driver, '-', PRETO),
        ligar('placa', 'D14', driver, 'IN1', LARANJA),
        ligar('placa', 'D27', driver, 'IN2', AMARELO),
        ligar('placa', 'D26', driver, 'IN3', ROXO),
        ligar('placa', 'D25', driver, 'IN4', AZUL),
        ligar(motor, 'laranja', driver, 'A', LARANJA),
        ligar(motor, 'amarelo', driver, 'B', AMARELO),
        ligar(motor, 'rosa', driver, 'C', ROXO),
        ligar(motor, 'azul', driver, 'D', AZUL),
        ligar(motor, 'vermelho', driver, 'COM', VERMELHO),
    ]
    return pecas, fios


def l9110s(motor_tipo, acessorio=None, driver='driver'):
    """O L9110S nas pilhas, com um motor (e, se vier, a roda ou a helice no eixo)."""
    pecas = [fonte(6, 2000, 2500, '4 pilhas AA (6 V)')]
    if driver != 'peca':
        pecas.append({'Id': 'driver', 'Tipo': 'l9110s', 'X': 300, 'Y': 60})
    motor = 'motor'
    if motor_tipo:
        pecas.append({'Id': 'motor', 'Tipo': motor_tipo, 'X': 520, 'Y': 80})
    if acessorio:
        x, y = no_eixo(acessorio, motor_tipo, 520, 80)
        pecas.append({'Id': 'peca', 'Tipo': acessorio, 'X': x, 'Y': y})
    fios = [
        ligar('pilhas', '+', driver, 'VCC', VERMELHO),
        ligar('pilhas', '-', driver, 'GND', PRETO),
        ligar('placa', 'GND.2', driver, 'GND', PRETO),
        ligar('placa', 'D13', driver, 'A-IA', VERDE),
        ligar('placa', 'D12', driver, 'A-IB', AZUL),
        ligar(driver, 'A1', motor, '+', VERMELHO),
        ligar(driver, 'A2', motor, '-', PRETO),
    ]
    return pecas, fios


def esc(nome_peca, modelo=None, volts=11.1, nome_bateria='LiPo 3S 2200 mAh (11,1 V)'):
    """
    Na ordem do fluxo: o motor, o ESC (as fases saem pela esquerda dele), e a
    LiPo e a placa do lado da bateria e do sinal. Com a helice 1045 (25 cm),
    o motor fica sozinho a esquerda: ela gira por cima de tudo o que estiver
    no disco dela.
    """
    controlador = 'peca' if nome_peca == 'esc' else 'esc'
    motor = 'peca' if nome_peca == 'motor-brushless' else 'motor'
    dx = {'helice-1045': 400, 'helice-5045': 160}.get(nome_peca, 0)
    pecas = [
        {'Id': motor, 'Tipo': 'motor-brushless', 'X': 40, 'Y': 40, **({'Modelo': modelo} if modelo else {})},
        {'Id': controlador, 'Tipo': 'esc', 'X': 240 + dx, 'Y': 120},
        fonte(volts, 20000, 2200, nome_bateria, x=560 + dx, y=20),
        {'Id': 'placa', 'Tipo': ESP, 'X': 620 + dx, 'Y': 150},
    ]
    if nome_peca in ('helice-1045', 'helice-5045'):
        x, y = no_eixo(nome_peca, 'motor-brushless', 40, 40)
        pecas.append({'Id': 'peca', 'Tipo': nome_peca, 'X': x, 'Y': y})
    fios = [
        ligar('pilhas', '+', controlador, 'BAT+', VERMELHO),
        ligar('pilhas', '-', controlador, 'BAT-', PRETO),
        ligar('placa', 'GND.2', controlador, 'GND', PRETO),
        ligar('placa', 'D13', controlador, 'SINAL', AMARELO),
        ligar(controlador, 'A', motor, 'A', AZUL),
        ligar(controlador, 'B', motor, 'B', AMARELO),
        ligar(controlador, 'C', motor, 'C', VERMELHO),
    ]
    return pecas, fios


def esc_4em1():
    """A 4-em-1 no meio, um A2212 em cada lado, a LiPo 4S embaixo e a placa em cima."""
    pecas = [
        {'Id': 'placa', 'Tipo': ESP, 'X': 420, 'Y': -80},
        {'Id': 'peca', 'Tipo': 'esc-4em1', 'X': 420, 'Y': 250},
        fonte(14.8, 60000, 1500, 'LiPo 4S 1500 mAh (14,8 V)', x=430, y=470),
    ]
    motores = [('m1', 160, 60), ('m2', 700, 60), ('m3', 700, 420), ('m4', 160, 420)]
    for mid, x, y in motores:
        pecas.append({'Id': mid, 'Tipo': 'motor-brushless', 'X': x, 'Y': y})
    fios = [
        ligar('pilhas', '+', 'peca', 'BAT+', VERMELHO),
        ligar('pilhas', '-', 'peca', 'BAT-', PRETO),
        ligar('placa', 'GND.2', 'peca', 'GND', PRETO),
    ]
    for k, (gpio, cor) in enumerate([('D13', AMARELO), ('D12', VERDE), ('D14', AZUL), ('D27', ROXO)], start=1):
        fios.append(ligar('placa', gpio, 'peca', f'S{k}', cor))
    for k, (mid, _, _) in enumerate(motores, start=1):
        for fase, cor in zip('ABC', [AZUL, AMARELO, VERMELHO]):
            fios.append(ligar('peca', f'M{k}{fase}', mid, fase, cor))
    return pecas, fios


def servo_na_fonte():
    """O servo forte nas pilhas dele (puxa ~1 A parado), com o GND em comum."""
    return ([fonte(6, 2000, 2500, '4 pilhas AA (6 V)')], [
        ligar('pilhas', '+', 'peca', 'V+', VERMELHO),
        ligar('pilhas', '-', 'peca', 'GND', PRETO),
        ligar('placa', 'GND.2', 'peca', 'GND', PRETO),
        ligar('placa', 'D13', 'peca', 'PWM', LARANJA),
    ])


def receptor(nome_peca):
    """O receptor nos 5 V, tres canais nos GPIOs; o controle fica na mesa."""
    rx = 'peca' if nome_peca == 'receptor-rc' else 'rx'
    controle = 'peca' if nome_peca == 'radio-controle' else 'controle'
    pecas = [
        {'Id': rx, 'Tipo': 'receptor-rc', 'X': 300, 'Y': 60},
        {'Id': controle, 'Tipo': 'radio-controle', 'X': 300, 'Y': 220},
    ]
    fios = [
        ligar('placa', 'VIN', rx, 'VCC', VERMELHO),
        ligar('placa', 'GND.2', rx, 'GND', PRETO),
        ligar(rx, 'CH1', 'placa', 'D34', AMARELO),
        ligar(rx, 'CH2', 'placa', 'D35', VERDE),
        ligar(rx, 'CH3', 'placa', 'D32', AZUL),
    ]
    return pecas, fios


def _ponte_l298n():
    pecas, fios = ponte_com_enable('l298n', 'motor-tt', [
        ligar('pilhas', '+', 'peca', '12V', VERMELHO),
        ligar('pilhas', '-', 'peca', 'GND', PRETO),
        ligar('placa', 'GND.2', 'peca', 'GND', PRETO),
        # Com 6 V o regulador da placa nao funciona (pede 7 V): a logica
        # vem dos 5 V da USB, pelo VIN.
        ligar('placa', 'VIN', 'peca', '5V', VERMELHO),
        ligar('placa', 'D13', 'peca', 'IN1', VERDE),
        ligar('placa', 'D12', 'peca', 'IN2', AZUL),
        ligar('placa', 'D14', 'peca', 'ENA', AMARELO),
    ], ('OUT1', 'OUT2'))
    return pecas, fios


def _ponte_l293d():
    pecas, fios = ponte_com_enable('l293d', 'motor-dc', [
        ligar('placa', 'VIN', 'peca', 'VCC1', VERMELHO),
        ligar('pilhas', '+', 'peca', 'VCC2', VERMELHO),
        ligar('pilhas', '-', 'peca', 'GND.1', PRETO),
        ligar('placa', 'GND.2', 'peca', 'GND.1', PRETO),
        ligar('placa', 'D14', 'peca', '1,2EN', AMARELO),
        ligar('placa', 'D13', 'peca', '1A', VERDE),
        ligar('placa', 'D12', 'peca', '2A', AZUL),
    ], ('1Y', '2Y'))
    return pecas, fios


def _ponte_tb6612():
    pecas, fios = ponte_com_enable('tb6612fng', 'motor-tt', [
        ligar('pilhas', '+', 'peca', 'VM', VERMELHO),
        ligar('pilhas', '-', 'peca', 'GND', PRETO),
        ligar('placa', 'GND.2', 'peca', 'GND.2', PRETO),
        ligar('placa', '3V3', 'peca', 'VCC', VERMELHO),
        ligar('placa', '3V3', 'peca', 'STBY', ROXO),
        ligar('placa', 'D14', 'peca', 'PWMA', AMARELO),
        ligar('placa', 'D13', 'peca', 'AIN1', VERDE),
        ligar('placa', 'D12', 'peca', 'AIN2', AZUL),
    ], ('AO1', 'AO2'))
    return pecas, fios


def _drv8833():
    return ([{'Id': 'motor', 'Tipo': 'motor-tt', 'X': 520, 'Y': 60}, fonte(6, 2000, 2500, '4 pilhas AA (6 V)')], [
        ligar('pilhas', '+', 'peca', 'VM', VERMELHO),
        ligar('pilhas', '-', 'peca', 'GND', PRETO),
        ligar('placa', 'GND.2', 'peca', 'GND', PRETO),
        ligar('placa', 'D13', 'peca', 'AIN1', VERDE),
        ligar('placa', 'D12', 'peca', 'AIN2', AZUL),
        ligar('peca', 'AO1', 'motor', '+', VERMELHO),
        ligar('peca', 'AO2', 'motor', '-', PRETO),
    ])


def _bts7960():
    return ([{'Id': 'motor', 'Tipo': 'motor-tt', 'X': 540, 'Y': 80}, fonte(6, 2000, 2500, '4 pilhas AA (6 V)')], [
        ligar('pilhas', '+', 'peca', 'B+', VERMELHO),
        ligar('pilhas', '-', 'peca', 'B-', PRETO),
        # A logica do modulo IBT-2 e de 5 V: o VCC e os dois enables no VIN.
        ligar('placa', 'VIN', 'peca', 'VCC', VERMELHO),
        ligar('placa', 'GND.2', 'peca', 'GND', PRETO),
        ligar('placa', 'VIN', 'peca', 'R_EN', ROXO),
        ligar('placa', 'VIN', 'peca', 'L_EN', ROXO),
        ligar('placa', 'D13', 'peca', 'RPWM', VERDE),
        ligar('placa', 'D12', 'peca', 'LPWM', AZUL),
        ligar('peca', 'M+', 'motor', '+', VERMELHO),
        ligar('peca', 'M-', 'motor', '-', PRETO),
    ])


def _drv8825():
    return ([fonte(12, 3000, 100000, 'fonte de 12 V'), {'Id': 'motor', 'Tipo': 'stepper-motor', 'X': 480, 'Y': 20}], [
        ligar('pilhas', '+', 'peca', 'VMOT', VERMELHO),
        ligar('pilhas', '-', 'peca', 'GND', PRETO),
        ligar('placa', 'GND.2', 'peca', 'GND.2', PRETO),
        ligar('peca', 'RST', 'peca', 'SLP', ROXO),
        ligar('placa', 'D13', 'peca', 'STEP', VERDE),
        ligar('placa', 'D12', 'peca', 'DIR', AZUL),
        ligar('peca', 'A1', 'motor', 'A+', VERMELHO),
        ligar('peca', 'A2', 'motor', 'A-', AZUL),
        ligar('peca', 'B1', 'motor', 'B+', VERDE),
        ligar('peca', 'B2', 'motor', 'B-', PRETO),
    ])


def _motor_no_l9110s(motor_tipo):
    # O motor e a peca do teste: o driver e o "driver".
    pecas, fios = l9110s(None)
    pecas.append({'Id': 'peca', 'Tipo': motor_tipo, 'X': 520, 'Y': 80})
    fios = [x if x['ParaPeca'] != 'motor' else dict(x, ParaPeca='peca') for x in fios]
    return pecas, fios


def _acessorio_no_l9110s(motor_tipo, acessorio):
    pecas, fios = l9110s(motor_tipo, acessorio)
    return pecas, fios


def _bateria_no_vin():
    """A bateria de 9 V no VIN: sem o cabo USB, e ela que liga a placa."""
    return ([fonte(9, 500, 500, 'Bateria 9 V', x=300, y=220, id_='peca')], [
        ligar('peca', '+', 'placa', 'VIN', VERMELHO),
        ligar('peca', '-', 'placa', 'GND.2', PRETO),
    ])


def _regulador(tipo, placa, bateria, entrada, saida_placa, gnd_placa, modelo=None, volts=None):
    """A bateria na entrada do regulador, e a saida dele alimentando a placa (sem o cabo USB)."""
    pe, te, ps, ts = {
        'regulador-7805': ('IN', 'GND', 'OUT', 'GND'),
        'ams1117': ('VIN', 'GND', 'VOUT', 'GND'),
        'lm2596': ('IN+', 'IN-', 'OUT+', 'OUT-'),
        'mt3608': ('VIN+', 'VIN-', 'VOUT+', 'VOUT-'),
    }[tipo]
    peca = {'Id': 'peca', 'Tipo': tipo, 'X': 300, 'Y': 200}
    if modelo:
        peca['Modelo'] = modelo
    # Os de trimpot saem de fabrica nos 5 V (sem o campo, o .sktch voltaria com 9).
    if volts is None and tipo in ('lm2596', 'mt3608'):
        volts = 5.0
    if volts:
        peca['Volts'] = volts
    pecas = [{'Id': 'placa', 'Tipo': placa, 'X': 40, 'Y': 40}, peca,
             fonte(*bateria, x=560, y=200, id_='pilhas')]
    fios = [
        ligar('pilhas', '+', 'peca', pe, VERMELHO),
        ligar('pilhas', '-', 'peca', te, PRETO),
        ligar('peca', ps, 'placa', saida_placa, VERMELHO),
        ligar('peca', ts, 'placa', gnd_placa, PRETO),
    ]
    return pecas, fios


def _motor_tt_no_l298n():
    pecas, fios = _ponte_l298n()
    # A ponte e o "driver"; o motor amarelo e a peca do teste.
    pecas = [p for p in pecas if p['Id'] != 'motor'] + [
        {'Id': 'driver', 'Tipo': 'l298n', 'X': 300, 'Y': 40},
        {'Id': 'peca', 'Tipo': 'motor-tt', 'X': 520, 'Y': 60},
    ]
    troca_ = {'peca': 'driver', 'motor': 'peca'}
    fios = [dict(x, DePeca=troca_.get(x['DePeca'], x['DePeca']), ParaPeca=troca_.get(x['ParaPeca'], x['ParaPeca']))
            for x in fios]
    return pecas, fios


# Peca -> (montagem, sketch, o que o teste mostra)
#
# Com bateria no circuito a placa fica "sem o cabo USB" (Alpha 24): so liga
# se a bateria chegar nela. Nestas receitas a bateria e do motor, e a placa
# esta na USB, como na bancada -- o projeto diz isso ("Usb": true). A excecao
# e a propria bateria, que liga a placa pelo VIN.
CIRCUITOS = {
    'bateria': (_bateria_no_vin, 'generico',
                'A bateria de 9 V no VIN da ESP32: sem o cabo USB, e ela que liga a placa (o regulador faz os 3,3 V).'),
    'regulador-7805': (lambda: _regulador('regulador-7805', 'arduino-uno', (9, 500, 500, 'Bateria 9 V'), 'IN', '5V', 'GND.2'),
                       'pisca-13', 'O 7805 faz 5 V da bateria de 9 V e liga o Uno pelo pino 5V (sem o cabo USB).'),
    'ams1117': (lambda: _regulador('ams1117', ESP, (7.4, 20000, 1000, 'LiPo 2S 1000 mAh (7,4 V)'), 'VIN', '3V3', 'GND.1'),
                'saida', 'O AMS1117 faz 3,3 V da LiPo 2S e liga a ESP32 direto pelo pino 3V3.'),
    'lm2596': (lambda: _regulador('lm2596', ESP, (11.1, 20000, 2200, 'LiPo 3S 2200 mAh (11,1 V)'), 'IN+', 'VIN', 'GND.2'),
               'saida', 'O LM2596 (step-down, 5 V no trimpot) baixa a LiPo 3S e liga a ESP32 pelo VIN, com pouca perda.'),
    'mt3608': (lambda: _regulador('mt3608', 'arduino-uno', (3.7, 5000, 2600, 'Bateria 18650 (3,7 V)'), 'VIN+', '5V', 'GND.2'),
               'pisca-13', 'O MT3608 (step-up, 5 V no trimpot) sobe a 18650 de 3,7 V e liga o Uno pelo pino 5V.'),
    'l298n': (_ponte_l298n, 'ponte-h',
              'O L298N com 4 pilhas AA no 12V e a logica nos 5 V da USB (o regulador dele pede 7 V).'),
    'l293d': (_ponte_l293d, 'ponte-h',
              'O L293D (CI): VCC1 = logica (5 V da USB), VCC2 = motor (as pilhas), o 1,2EN e a velocidade.'),
    'tb6612fng': (_ponte_tb6612, 'ponte-h',
                  'O TB6612FNG: VM nas pilhas, VCC e STBY no 3,3 V; PWMA e a velocidade.'),
    'l9110s': (lambda: l9110s('motor-tt', driver='peca'), 'ponte-h-2',
               'O L9110S: duas entradas por motor; a velocidade e o PWM de uma delas.'),
    'drv8833': (_drv8833, 'ponte-h-2',
                'O DRV8833: o SLP solto deixa ele acordado; a velocidade e o PWM de uma entrada.'),
    'bts7960': (_bts7960, 'ponte-h-2',
                'O BTS7960 (43 A): VCC, R_EN e L_EN nos 5 V (VIN); RPWM e LPWM, um para cada sentido.'),
    'a4988': (lambda: a4988('a4988'), 'passo-driver',
              'O A4988 com o NEMA 17: 12 V no VMOT, 3,3 V no VDD, RST ligado no SLP.'),
    'stepper-motor': (lambda: a4988('stepper-motor'), 'passo-driver',
                      'O NEMA 17 pelo A4988 (direto nos GPIOs ele nao anda: cada bobina pede ~1 A).'),
    'drv8825': (_drv8825, 'passo-driver',
                'O DRV8825 com o NEMA 17: a logica vem do VMOT; RST no SLP.'),
    'uln2003': (lambda: uln2003('uln2003'), 'passo-28byj',
                'O ULN2003 nos 5 V da USB, com o 28BYJ-48 no conector dele.'),
    'motor-28byj48': (lambda: uln2003('motor-28byj48'), 'passo-28byj',
                      'O 28BYJ-48 pelo ULN2003: o fio vermelho e o comum, nos 5 V.'),
    'motor-dc': (lambda: _motor_no_l9110s('motor-dc'), 'ponte-h-2',
                 'O motor 130 pelo L9110S, nas 4 pilhas AA.'),
    'motor-tt': (_motor_tt_no_l298n, 'ponte-h',
                 'O motor amarelo (1:48) pelo L298N, nas 4 pilhas AA.'),
    'roda-tt': (lambda: _acessorio_no_l9110s('motor-tt', 'roda-tt'), 'ponte-h-2',
                'A roda de 65 mm encaixada no eixo do motor amarelo: o rotulo mostra a velocidade na borda.'),
    'helice-130': (lambda: _acessorio_no_l9110s('motor-dc', 'helice-130'), 'ponte-h-2',
                   'A helice de 3 pas no motor 130: ela carrega o motor e empurra (gf no rotulo).'),
    'esc': (lambda: esc('esc'), 'esc',
            'O ESC 30A com o A2212 numa LiPo 3S: arma com o acelerador no minimo, depois acelera.'),
    'motor-brushless': (lambda: esc('motor-brushless'), 'esc',
                        'O A2212 (sem escova) pelo ESC, numa LiPo 3S.'),
    'helice-1045': (lambda: esc('helice-1045'), 'esc',
                    'A helice 1045 no A2212, pelo ESC: ~850 gf com o acelerador no maximo.'),
    'helice-5045': (lambda: esc('helice-5045', '2205-2300', 14.8, 'LiPo 4S 2200 mAh (14,8 V)'), 'esc',
                    'A helice 5045 de tres pas num 2205 2300KV (o motor de drone de 5"), na LiPo 4S.'),
    'esc-4em1': (esc_4em1, 'esc-4em1',
                 'A ESC 4-em-1 de drone (45 A por motor, 3S a 6S) com quatro A2212 numa LiPo 4S.'),
    'servo-mg996r': (servo_na_fonte, 'servo-forte',
                     'O MG996R nas 4 pilhas AA (ele puxa ~1 A travado: a USB nao da conta), GND em comum.'),
    'servo-360': (servo_na_fonte, 'servo-360',
                  'O servo de rotacao continua: o write() vira velocidade e sentido.'),
    'receptor-rc': (lambda: receptor('receptor-rc'), 'receptor',
                    'O receptor FS-iA6B nos 5 V; CH1 a CH3 nos GPIOs. Ligue o controle na mesa e mexa os manches.'),
    'radio-controle': (lambda: receptor('radio-controle'), 'receptor',
                       'O controle FS-i6 e o receptor: ligue a chave do controle e mexa os manches.'),
}

# O que NAO entra: placas (ja tem a pasta delas), protoboards (sao
# base, e nao componente) e a anotacao, que e um retangulo com texto.
FORA = set(PINO_DA_PLACA) | {
    'protoboard-170', 'protoboard-400', 'protoboard-830', 'texto',
}

componentes = 0

for peca in sorted(CATALOGO, key=lambda p: p['id']):
    tipo = peca['id']
    if tipo in FORA:
        continue

    if tipo in CIRCUITOS:
        montar, feitio, nota = CIRCUITOS[tipo]
        outras, fios = montar()
        # A placa no canto, a menos que a receita a ponha em outro lugar.
        pecas = [] if any(p['Id'] == 'placa' for p in outras) else [{'Id': 'placa', 'Tipo': ESP, 'X': 60, 'Y': 60}]
        if not any(p['Id'] == 'peca' for p in outras):
            pecas.append({'Id': 'peca', 'Tipo': tipo, 'X': 300, 'Y': 60})
        pecas += outras
        na_usb = (tipo not in ('bateria', 'regulador-7805', 'ams1117', 'lm2596', 'mt3608')
                  and any(p['Tipo'] == 'bateria' for p in pecas))
        if na_usb:
            for p in pecas:
                if p['Id'] == 'placa':
                    p['Usb'] = True
        escrever('componentes', tipo, projeto(SKETCHES[feitio], pecas, fios, [
            f'Teste do componente {tipo}.',
            '',
            nota,
            '',
            'Ligado numa ESP32 DevKit V1, com o que ele precisa para funcionar',
            'de verdade: o driver, o motor e a bateria de cada um.',
        ] + (['', 'A placa esta no cabo USB (na barra dela: USB -> cabo ligado); a bateria e so do motor.']
             if na_usb else [])))
        componentes += 1
        continue

    nomes = [p['nome'] for p in peca['pinos']]
    if not nomes:
        continue

    if tipo in LIGACOES:
        daPeca, daPlaca, feitio = LIGACOES[tipo]
    else:
        # Generico: o primeiro pino num GPIO, e o que parecer
        # alimentacao e terra nos lugares certos.
        feitio = 'generico'
        daPeca, daPlaca = [], []

        for nome in nomes[:4]:
            curto = nome.split('.')[0]

            if curto in ('VCC', 'VDD', 'V+', '+', '3V3', '5V', 'VIN'):
                daPeca.append(nome)
                daPlaca.append('3V3')
            elif curto in ('GND', 'VSS', 'V-', '-', 'COM'):
                daPeca.append(nome)
                daPlaca.append('GND.1')
            elif len(daPeca) == 0 or 'D2' not in daPlaca:
                daPeca.append(nome)
                daPlaca.append('D2')

        if not daPeca:
            daPeca, daPlaca = [nomes[0]], ['D2']

    fios = [
        {'DePeca': 'peca', 'DePino': a, 'ParaPeca': 'placa', 'ParaPino': b,
         'Cor': '#00A651' if i == 0 else '#1A1A1A'}
        for i, (a, b) in enumerate(zip(daPeca, daPlaca))
    ]

    escrever('componentes', tipo, projeto(
        SKETCHES[feitio],
        [
            {'Id': 'placa', 'Tipo': ESP, 'X': 100, 'Y': 60},
            {'Id': 'peca', 'Tipo': tipo, 'X': 520, 'Y': 160},
        ],
        fios,
        [
            f'Teste do componente {tipo}.',
            '',
            'Ligado numa ESP32 DevKit V1, que tem pino para tudo — assim o',
            'que muda de um teste para o outro é só a peça.',
            '',
            'Pinos ligados: ' + ', '.join(f'{a} → {b}' for a, b in zip(daPeca, daPlaca)),
        ]))

    componentes += 1

print(f'componentes: {componentes}')


# =====================================================================
#  3) uma por LINGUAGEM
# =====================================================================
#
# Todas fazem a MESMA coisa, para a comparacao ficar limpa.
LINGUAGENS = {
    'arduino-cpp': ('arduino-uno', '13', [
        '// Bateria — Arduino (C++).',
        '',
        'const int LED = 13;',
        '',
        'void setup() {',
        '  Serial.begin(9600);',
        '  pinMode(LED, OUTPUT);',
        '  Serial.println("cpp acordou");',
        '}',
        '',
        'void loop() {',
        '  digitalWrite(LED, HIGH);',
        '  delay(250);',
        '  digitalWrite(LED, LOW);',
        '  delay(250);',
        '}',
    ]),

    'c': ('pi-pico', 'GP15', [
        '// Bateria — C, com o SDK da Raspberry.',
        '//',
        '// Comeca no main(), e nao em setup/loop.',
        '',
        '#include "pico/stdlib.h"',
        '',
        '#define LED 15',
        '',
        'int main() {',
        '    stdio_init_all();',
        '    gpio_init(LED);',
        '    gpio_set_dir(LED, GPIO_OUT);',
        '',
        '    while (true) {',
        '        gpio_put(LED, 1);',
        '        sleep_ms(250);',
        '        gpio_put(LED, 0);',
        '        sleep_ms(250);',
        '    }',
        '}',
    ]),

    'csharp': ('esp32-devkit-v1', 'D2', [
        '// Bateria — C#.',
        '',
        'using System;',
        '',
        'public class Program',
        '{',
        '    public static void Main()',
        '    {',
        '        int led = 2;',
        '        PinMode(led, OUTPUT);',
        '        Console.WriteLine("csharp acordou");',
        '',
        '        while (true)',
        '        {',
        '            DigitalWrite(led, HIGH);',
        '            Delay(250);',
        '            DigitalWrite(led, LOW);',
        '            Delay(250);',
        '        }',
        '    }',
        '}',
    ]),

    'micropython': ('esp32-devkit-v1', 'D2', [
        '# Bateria — MicroPython.',
        '',
        'from machine import Pin',
        'import time',
        '',
        'led = Pin(2, Pin.OUT)',
        'print("micropython acordou")',
        '',
        'while True:',
        '    led.value(1)',
        '    time.sleep(0.25)',
        '    led.value(0)',
        '    time.sleep(0.25)',
    ]),

    'rust': ('arduino-uno', '13', [
        '// Bateria — Rust.',
        '',
        '#![no_std]',
        '#![no_main]',
        '',
        'const LED: u8 = 13;',
        '',
        'fn main() {',
        '    pin_mode(LED, OUTPUT);',
        '    println!("rust acordou");',
        '',
        '    loop {',
        '        digital_write(LED, HIGH);',
        '        delay_ms(250);',
        '        digital_write(LED, LOW);',
        '        delay_ms(250);',
        '    }',
        '}',
    ]),

    'ada': ('arduino-uno', '13', [
        '--  Bateria — Ada.',
        '',
        'with Ada.Text_IO; use Ada.Text_IO;',
        '',
        'procedure Pisca is',
        '   Led : constant Integer := 13;',
        'begin',
        '   Pin_Mode (Led, Output);',
        '   Put_Line ("ada acordou");',
        '',
        '   loop',
        '      Digital_Write (Led, High);',
        '      Delay_Ms (250);',
        '      Digital_Write (Led, Low);',
        '      Delay_Ms (250);',
        '   end loop;',
        'end Pisca;',
    ]),

    'spark': ('arduino-uno', '13', [
        '--  Bateria — SPARK.',
        '',
        'pragma SPARK_Mode (On);',
        '',
        'procedure Pisca is',
        '   Led : constant Integer := 13;',
        '',
        '   procedure Acender (Pino : Integer)',
        '     with Pre => Pino >= 0;',
        '',
        '   procedure Acender (Pino : Integer) is',
        '   begin',
        '      Digital_Write (Pino, High);',
        '   end Acender;',
        '',
        'begin',
        '   Pin_Mode (Led, Output);',
        '   Put_Line ("spark acordou");',
        '',
        '   loop',
        '      Acender (Led);',
        '      Delay_Ms (250);',
        '      Digital_Write (Led, Low);',
        '      Delay_Ms (250);',
        '   end loop;',
        'end Pisca;',
    ]),

    'cowgol': ('arduino-uno', '13', [
        '# Bateria — Cowgol.',
        '',
        'sub main() is',
        '    var led: uint8 := 13;',
        '',
        '    pin_mode(led, OUTPUT);',
        '',
        '    loop',
        '        digital_write(led, HIGH);',
        '        delay_ms(250);',
        '        digital_write(led, LOW);',
        '        delay_ms(250);',
        '    end loop;',
        'end sub;',
    ]),

    'millipascal': ('arduino-uno', '13', [
        '# Bateria — Millipascal.',
        '',
        'proc main',
        'begin',
        '    pin_mode(13, OUTPUT);',
        '',
        '    while true begin',
        '        digital_write(13, HIGH);',
        '        delay_ms(250);',
        '        digital_write(13, LOW);',
        '        delay_ms(250);',
        '    end',
        'end proc',
    ]),

    'assembly': ('arduino-uno', '13', [
        '; Bateria — Assembly AVR.',
        ';',
        '; Opcodes de verdade, ciclos do datasheet. O .hex que sai daqui',
        '; e o mesmo binario que o avrdude grava na placa.',
        ';',
        '; PB5 do ATmega328P E o pino 13 do Arduino Uno.',
        '',
        '.equ LED = 5',
        '',
        '    ldi r16, (1<<LED)',
        '    out DDRB, r16',
        '',
        'principal:',
        '    sbi PORTB, LED',
        '    rcall espera',
        '    cbi PORTB, LED',
        '    rcall espera',
        '    rjmp principal',
        '',
        '; 3 niveis de contagem: 3.916.867 ciclos por chamada,',
        '; que a 16 MHz sao 244,8 ms.',
        'espera:',
        '    ldi r20, 20',
        'nivel3:',
        '    ldi r18, 255',
        'nivel2:',
        '    ldi r17, 255',
        'nivel1:',
        '    dec r17',
        '    brne nivel1',
        '    dec r18',
        '    brne nivel2',
        '    dec r20',
        '    brne nivel3',
        '    ret',
    ]),
}

for nome, (tipo, pino, codigo) in sorted(LINGUAGENS.items()):
    escrever('linguagens', nome, projeto(
        codigo,
        [
            {'Id': 'placa', 'Tipo': tipo, 'X': 120, 'Y': 60},
            {'Id': 'led', 'Tipo': 'led', 'X': 520, 'Y': 120, 'Cor': 'vermelho'},
            {'Id': 'r', 'Tipo': 'resistor', 'X': 520, 'Y': 280, 'Ohms': 330},
        ],
        [
            {'DePeca': 'placa', 'DePino': pino,
             'ParaPeca': 'led', 'ParaPino': 'A', 'Cor': '#00A651'},
            {'DePeca': 'led', 'DePino': 'C',
             'ParaPeca': 'r', 'ParaPino': '1', 'Cor': '#1A1A1A'},
            {'DePeca': 'r', 'DePino': '2',
             'ParaPeca': 'placa', 'ParaPino': terra(tipo), 'Cor': '#1A1A1A'},
        ],
        [
            f'Teste da linguagem {nome}.',
            '',
            'Todas as dez fazem exatamente a mesma coisa — piscar um LED e',
            'contar — para que a comparação entre elas fique limpa.',
        ]))

print(f'linguagens: {len(LINGUAGENS)}')


# =====================================================================
#  4) ENERGIA: circuitos sem placa, so com bateria
# =====================================================================
#
# Pedido do usuario na Alpha 23: ver os componentes funcionando so com
# energia, sem microcontrolador. Nada para rodar -- o LED acende e o motor
# gira assim que o projeto abre.

SEM_PLACA = [
    '// Sem placa: este circuito funciona so com a bateria.',
    '// Nao ha nada para rodar -- a energia ja esta ligada.',
]

ENERGIA = {
    'bateria-e-led': (
        [
            {'Id': 'bateria', 'Tipo': 'bateria', 'X': 80, 'Y': 220,
             'Volts': 9, 'MiliAmperes': 500, 'MiliAmperesHora': 500, 'Nome': 'Bateria 9 V'},
            {'Id': 'resistor', 'Tipo': 'resistor', 'X': 250, 'Y': 150, 'Ohms': 330},
            {'Id': 'led', 'Tipo': 'led', 'X': 380, 'Y': 110, 'Cor': 'vermelho'},
        ],
        [
            {'DePeca': 'bateria', 'DePino': '+', 'ParaPeca': 'resistor', 'ParaPino': '1', 'Cor': '#D23B33'},
            {'DePeca': 'resistor', 'DePino': '2', 'ParaPeca': 'led', 'ParaPino': 'A', 'Cor': '#D23B33'},
            {'DePeca': 'led', 'DePino': 'C', 'ParaPeca': 'bateria', 'ParaPino': '-', 'Cor': '#1A1A1A'},
        ],
        [
            'Bateria e LED, sem placa.',
            '',
            'A bateria de 9 V acende o LED pelo resistor de 330 ohms: uns 20 mA.',
            'O resistor e o que segura a corrente -- tire-o e o LED queima,',
            'como na bancada. Inverta o LED e ele apaga (o diodo so conduz',
            'num sentido). Solte o fio do "-" e o circuito abre.',
        ]),
    'bateria-e-motor': (
        [
            {'Id': 'motor', 'Tipo': 'motor-tt', 'X': 60, 'Y': 90},
            {'Id': 'pilhas', 'Tipo': 'bateria', 'X': 430, 'Y': 170,
             'Volts': 6, 'MiliAmperes': 2000, 'MiliAmperesHora': 2500, 'Nome': '4 pilhas AA (6 V)'},
        ],
        [
            {'DePeca': 'pilhas', 'DePino': '+', 'ParaPeca': 'motor', 'ParaPino': '+', 'Cor': '#D23B33'},
            {'DePeca': 'pilhas', 'DePino': '-', 'ParaPeca': 'motor', 'ParaPino': '-', 'Cor': '#1A1A1A'},
        ],
        [
            'Bateria e motor, sem placa.',
            '',
            'Quatro pilhas AA (6 V) no motor amarelo de reducao 1:48: ~250 rpm,',
            'o numero que a Adafruit mediu. Troque os fios de lado e ele gira',
            'para o outro lado; tire um fio e ele freia ate parar.',
        ]),
}

for nome, (pecas_e, fios_e, descricao) in ENERGIA.items():
    escrever('energia', nome, projeto(SEM_PLACA, pecas_e, fios_e, descricao))

print(f'energia: {len(ENERGIA)}')


# =====================================================================
#  5) ALIMENTACAO: a placa sem o cabo USB (Alpha 24)
# =====================================================================
#
# Com bateria no circuito a placa esta sem o cabo: so liga se a bateria
# chegar num pino de alimentacao dela (o VIN, o 5V, o VSYS, o 3V3), dentro
# da faixa daquele pino. Na barra da placa, "USB" escolhe: automatico, cabo
# ligado ou sem o cabo.

def pisca(pino, ola='ligou pela bateria'):
    return [
        '// Sem o cabo USB: esta placa so roda se a bateria a alimentar.',
        f'const int LED = {pino};',
        '',
        'void setup() {',
        '  Serial.begin(115200);',
        '  pinMode(LED, OUTPUT);',
        f'  Serial.println("{ola}");',
        '}',
        '',
        'void loop() {',
        '  digitalWrite(LED, HIGH);',
        '  delay(300);',
        '  digitalWrite(LED, LOW);',
        '  delay(300);',
        '}',
    ]


def led_no(placa, pino, gnd, x=360, y=60):
    """O LED com resistor num pino da placa."""
    return ([{'Id': 'led', 'Tipo': 'led', 'X': x, 'Y': y, 'Cor': 'verde'},
             {'Id': 'r', 'Tipo': 'resistor', 'X': x - 20, 'Y': y + 120, 'Ohms': 330}],
            [ligar(placa, pino, 'r', '1', VERDE),
             ligar('r', '2', 'led', 'A', VERDE),
             ligar('led', 'C', placa, gnd, PRETO)])


def com_led(tipo, pino, gnd, bateria, fios_bateria, x_placa=60, y_placa=40):
    pecas_l, fios_l = led_no('placa', pino, gnd)
    return ([{'Id': 'placa', 'Tipo': tipo, 'X': x_placa, 'Y': y_placa}] + pecas_l + [bateria],
            fios_l + fios_bateria)


LIPO_2S = fonte(7.4, 20000, 1000, 'LiPo 2S 1000 mAh (7,4 V)', x=380, y=300, id_='bateria')

ALIMENTACAO = {
    'bateria-solta': (pisca(2), *com_led(ESP, 'D2', 'GND.1', LIPO_2S, []), [
        'A bateria na mesa, mas nao ligada na placa.',
        '',
        'Com bateria no circuito a placa esta SEM o cabo USB: aperte F5 e ela',
        'nao liga ("sem energia"). Ligue o + da bateria no VIN e o - no GND',
        'com a simulacao rodando: a placa liga sozinha, do boot, e o LED pisca.',
        '',
        'Para testar so o programa, sem a bateria: na barra da placa,',
        'USB -> cabo ligado.',
    ]),
    'bateria-no-vin': (pisca(2), *com_led(ESP, 'D2', 'GND.1', LIPO_2S, [
        ligar('bateria', '+', 'placa', 'VIN', VERMELHO),
        ligar('bateria', '-', 'placa', 'GND.2', PRETO),
    ]), [
        'A LiPo 2S (7,4 V) no VIN da ESP32, sem o cabo USB.',
        '',
        'O VIN passa pelo regulador da placa (AMS1117): de 4,3 a 12 V ela liga,',
        'e o regulador faz os 3,3 V do chip e do pino 3V3. Tire o fio do +',
        'com a simulacao rodando e ela apaga; ponha de volta e ela liga do boot.',
    ]),
    'uno-bateria-9v': (pisca(13), *com_led('arduino-uno', '13', 'GND.1',
        fonte(9, 500, 500, 'Bateria 9 V', x=420, y=300, id_='bateria'), [
            ligar('bateria', '+', 'placa', 'VIN', VERMELHO),
            ligar('bateria', '-', 'placa', 'GND.2', PRETO),
        ]), [
        'O Arduino Uno na bateria de 9 V, pelo VIN.',
        '',
        'O VIN do Uno pede 7 a 12 V: o regulador dele precisa de folga sobre os',
        '5 V. Com a bateria de 9 V ele liga, e o pino 5V passa a dar 5 V para',
        'os sensores. Baixe a bateria para 6 V (na barra dela) e ele apaga.',
    ]),
    'pilhas-fracas': (pisca(2), *com_led(ESP, 'D2', 'GND.1',
        fonte(3, 2000, 2500, '2 pilhas AA (3 V)', x=380, y=300, id_='bateria'), [
            ligar('bateria', '+', 'placa', 'VIN', VERMELHO),
            ligar('bateria', '-', 'placa', 'GND.2', PRETO),
        ]), [
        'Duas pilhas AA (3 V) no VIN: nao bastam.',
        '',
        'O regulador do VIN precisa de ~4,3 V para dar 3,3 V: com 3 V a placa',
        'nao liga, e o aviso diz quanto falta. Mude para 4 pilhas (6 V) na',
        'barra da bateria e ela liga.',
    ]),
    'pilhas-no-3v3': (pisca(2), *com_led(ESP, 'D2', 'GND.1',
        fonte(4.5, 2000, 2500, '3 pilhas AA (4,5 V)', x=380, y=300, id_='bateria'), [
            ligar('bateria', '+', 'placa', '3V3', VERMELHO),
            ligar('bateria', '-', 'placa', 'GND.1', PRETO),
        ]), [
        'Tres pilhas AA (4,5 V) direto no 3V3: a ESP32 queima.',
        '',
        'O 3V3 e a saida do regulador, ligada direto no chip: aceita de 3,0 a',
        '3,6 V. Com 4,5 V ali ela queima ao ligar. Ponha a bateria no VIN',
        '(que passa pelo regulador) e rode de novo.',
    ]),
    'pico-pilhas-vsys': ([
        '// Sem o cabo USB: o Pico roda pelas pilhas no VSYS.',
        'const int LED = 15;',
        '',
        'void setup() {',
        '  Serial.begin(115200);',
        '  pinMode(LED, OUTPUT);',
        '  Serial.println("ligou pela bateria");',
        '}',
        '',
        'void loop() {',
        '  digitalWrite(LED, HIGH);',
        '  delay(300);',
        '  digitalWrite(LED, LOW);',
        '  delay(300);',
        '}',
    ], *com_led('pi-pico', 'GP15', 'GND.4',
        fonte(4.5, 2000, 2500, '3 pilhas AA (4,5 V)', x=420, y=300, id_='bateria'), [
            ligar('bateria', '+', 'placa', 'VSYS', VERMELHO),
            ligar('bateria', '-', 'placa', 'GND.5', PRETO),
        ]), [
        'O Raspberry Pi Pico com 3 pilhas AA no VSYS.',
        '',
        'O VSYS e a entrada do regulador do Pico: aceita de 1,8 a 5,5 V, e e por',
        'ali que se liga uma bateria nele (o VBUS e o 5 V da USB).',
    ]),
}


def _l298n_alimenta_placa():
    pecas = [
        {'Id': 'placa', 'Tipo': ESP, 'X': 60, 'Y': 60},
        {'Id': 'driver', 'Tipo': 'l298n', 'X': 300, 'Y': 40},
        {'Id': 'motor', 'Tipo': 'motor-tt', 'X': 540, 'Y': 60},
        fonte(11.1, 20000, 2200, 'LiPo 3S 2200 mAh (11,1 V)', x=320, y=320, id_='bateria'),
    ]
    fios = [
        ligar('bateria', '+', 'driver', '12V', VERMELHO),
        ligar('bateria', '-', 'driver', 'GND', PRETO),
        # O regulador do L298N (5 V, com 7 V ou mais no 12V) alimenta a placa.
        ligar('driver', '5V', 'placa', 'VIN', VERMELHO),
        ligar('driver', 'GND', 'placa', 'GND.2', PRETO),
        ligar('placa', 'D13', 'driver', 'IN1', VERDE),
        ligar('placa', 'D12', 'driver', 'IN2', AZUL),
        ligar('placa', 'D14', 'driver', 'ENA', AMARELO),
        ligar('driver', 'OUT1', 'motor', '+', VERMELHO),
        ligar('driver', 'OUT2', 'motor', '-', PRETO),
    ]
    return pecas, fios


def _bec_alimenta_placa():
    pecas = [
        {'Id': 'motor', 'Tipo': 'motor-brushless', 'X': 40, 'Y': 40},
        {'Id': 'esc', 'Tipo': 'esc', 'X': 240, 'Y': 120},
        fonte(11.1, 20000, 2200, 'LiPo 3S 2200 mAh (11,1 V)', x=560, y=20, id_='bateria'),
        {'Id': 'placa', 'Tipo': ESP, 'X': 620, 'Y': 150},
    ]
    fios = [
        ligar('bateria', '+', 'esc', 'BAT+', VERMELHO),
        ligar('bateria', '-', 'esc', 'BAT-', PRETO),
        # O cabo de servo do ESC: o sinal, o GND e os 5 V do BEC (o fio vermelho).
        ligar('esc', '5V', 'placa', 'VIN', VERMELHO),
        ligar('esc', 'GND', 'placa', 'GND.2', PRETO),
        ligar('placa', 'D13', 'esc', 'SINAL', AMARELO),
        ligar('esc', 'A', 'motor', 'A', AZUL),
        ligar('esc', 'B', 'motor', 'B', AMARELO),
        ligar('esc', 'C', 'motor', 'C', VERMELHO),
    ]
    return pecas, fios


ALIMENTACAO['l298n-alimenta-placa'] = (SKETCHES['ponte-h'], *_l298n_alimenta_placa(), [
    'Uma bateria so para tudo: o carrinho de verdade.',
    '',
    'A LiPo 3S no 12V do L298N; o regulador dele (5 V, que pede 7 V ou mais',
    'na entrada) alimenta a ESP32 pelo VIN. Sem o cabo USB: e o L298N que',
    'liga a placa, e a placa que manda no motor.',
])
ALIMENTACAO['bec-alimenta-placa'] = (SKETCHES['esc'], *_bec_alimenta_placa(), [
    'O BEC do ESC alimenta a placa: o aeromodelo de verdade.',
    '',
    'O ESC tira 5 V da LiPo (o BEC, no fio vermelho do cabo de servo) e eles',
    'vao no VIN da ESP32. Sem o cabo USB: a LiPo liga o ESC, o ESC liga a',
    'placa, e a placa arma e acelera o motor.',
])

for nome, (codigo_a, pecas_a, fios_a, descricao) in ALIMENTACAO.items():
    escrever('alimentacao', nome, projeto(codigo_a, pecas_a, fios_a, descricao))

print(f'alimentacao: {len(ALIMENTACAO)}')
print(f'\ntudo em: {SAIDA}')
